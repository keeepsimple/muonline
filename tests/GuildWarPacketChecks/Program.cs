// Offline checks for guild war, battle soccer and alliance / hostility packets: packets laid out as in OpenMU's
// ServerToClientPackets.xml are routed through the real PacketRouter and the GuildManager state is checked.
using System.Buffers;
using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using Client.Main;
using Client.Main.Configuration;
using Client.Main.Controllers;
using Client.Main.Core.Client;
using Client.Main.Networking;
using Client.Main.Networking.PacketHandling;
using Client.Main.Networking.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.Network.Packets.ServerToClient;

var logs = NullLoggerFactory.Instance;
var settings = new MuOnlineSettings { ProtocolVersion = "Season6" };
var state = new CharacterState(logs) { Id = 0x0042, Name = "Tester" };
var network = new NetworkManager(logs, settings, state, new ScopeManager(logs, state));
var router = (PacketRouter)typeof(NetworkManager).GetField("_packetRouter", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(network)!;
var dispatcher = (MainThreadDispatcher)typeof(MuGame).GetField("_mainThreadDispatcher", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
router.SetRoutingMode(false);
var guilds = network.GetGuildManager();
guilds.AssignPlayer(0x0042, 7, GuildMemberRole.GuildMaster, isSelf: true);

int failures = 0;
void Check(bool ok, string name, string detail = "")
{
    if (!ok) failures++;
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? " - " + detail : "")}");
}

async Task Route(byte[] packet)
{
    await router.RoutePacketAsync(new ReadOnlySequence<byte>(packet));
    dispatcher.ProcessPending();
}

byte[] C1(int length, byte code)
{
    var data = new byte[length];
    data[0] = 0xC1; data[1] = (byte)length; data[2] = code;
    return data;
}

void Str(byte[] data, int offset, string value) => Encoding.UTF8.GetBytes(value).CopyTo(data, offset);

// 0x60 result: just must not throw / be unhandled (feedback is a chat line).
var warResult = C1(4, 0x60); warResult[3] = 1;
await Route(warResult);
Check(!guilds.IsInWar, "0x60 request result leaves war state untouched");

// 0x61 incoming soccer request: pending until answered.
var warRequest = C1(12, 0x61); Str(warRequest, 3, "Wolves"); warRequest[11] = 1;
await Route(warRequest);
Check(guilds.PendingWarRequestGuild == "Wolves" && guilds.PendingWarRequestType == GuildWarType.Soccer,
    "0x61 war request stored as pending", $"{guilds.PendingWarRequestGuild} {guilds.PendingWarRequestType}");

// 0x62 declared: normal war, team 1.
var declared = C1(13, 0x62); Str(declared, 3, "Bears"); declared[11] = 0; declared[12] = 1;
await Route(declared);
Check(guilds.IsInWar && guilds.WarEnemyGuild == "Bears" && guilds.WarType == GuildWarType.Normal && guilds.WarTeam == 1,
    "0x62 war declared", $"{guilds.WarEnemyGuild} {guilds.WarType} team {guilds.WarTeam}");

// 0x64 score.
var score = C1(6, 0x64); score[3] = 3; score[4] = 2;
await Route(score);
Check(guilds.WarOwnScore == 3 && guilds.WarEnemyScore == 2, "0x64 war score", $"{guilds.WarOwnScore}:{guilds.WarEnemyScore}");

// 0x63 ended (won).
var ended = C1(12, 0x63); ended[3] = 1; Str(ended, 4, "Bears");
await Route(ended);
Check(!guilds.IsInWar && guilds.WarOwnScore == 0, "0x63 war ended clears war state");

// F3/22 soccer time: OpenMU layout, 8 bytes, seconds LE at offset 6.
var time = C1(8, 0xF3); time[3] = 0x22; BinaryPrimitives.WriteUInt16LittleEndian(time.AsSpan(6), 299);
await Route(time);
Check(guilds.SoccerRemainingSeconds == 299, "F3/22 soccer time", guilds.SoccerRemainingSeconds.ToString());

// F3/23 soccer score, then the hide value (red goals 255).
var soccer = C1(22, 0xF3); soccer[3] = 0x23; Str(soccer, 4, "Wolves"); soccer[12] = 2; Str(soccer, 13, "Bears"); soccer[21] = 1;
await Route(soccer);
Check(guilds.SoccerScoreVisible && guilds.SoccerRedTeam == "Wolves" && guilds.SoccerRedGoals == 2 && guilds.SoccerBlueTeam == "Bears" && guilds.SoccerBlueGoals == 1,
    "F3/23 soccer score", $"{guilds.SoccerRedTeam} {guilds.SoccerRedGoals}:{guilds.SoccerBlueGoals} {guilds.SoccerBlueTeam}");
var hide = C1(22, 0xF3); hide[3] = 0x23; hide[12] = 0xFF;
await Route(hide);
Check(!guilds.SoccerScoreVisible && guilds.SoccerRedTeam == "" && guilds.SoccerRemainingSeconds == 0, "F3/23 goals 255 hides the scoreboard");

// 0xE5 incoming alliance join request from player 0x1234 (id big endian).
var relRequest = C1(7, 0xE5); relRequest[3] = 1; relRequest[4] = 1; BinaryPrimitives.WriteUInt16BigEndian(relRequest.AsSpan(5), 0x1234);
await Route(relRequest);
Check(guilds.PendingRelationshipRequest == ((byte)1, (byte)1, (ushort)0x1234), "0xE5 relationship request stored as pending",
    guilds.PendingRelationshipRequest?.ToString() ?? "null");

// Answer packet for it (0xE6 C2S): echo types, id big endian as in the server definition.
var response = new byte[CharacterService.GuildRelationshipChangeResponseLength];
CharacterService.WriteGuildRelationshipChangeResponse(response, 1, 1, true, 0x1234);
Check(response.SequenceEqual(new byte[] { 0xC1, 8, 0xE6, 1, 1, 1, 0x12, 0x34 }), "0xE6 response layout", Convert.ToHexString(response));

// 0xE6 result: must route through the main-code fallback (0xE6 is in SubCodeHolder) without throwing.
var relResult = C1(8, 0xE6); relResult[3] = 2; relResult[4] = 1; relResult[5] = 1;
await Route(relResult);
Check(guilds.HasGuild, "0xE6 relationship result handled");

// 0xE9 alliance list (C2): two 41-byte entries from offset 8.
var list = new byte[8 + 2 * 41];
list[0] = 0xC2; BinaryPrimitives.WriteUInt16BigEndian(list.AsSpan(1), (ushort)list.Length); list[3] = 0xE9; list[4] = 2; list[5] = 1;
list[8] = 12; list[9] = 0x12; Str(list, 8 + 33, "Wolves");
list[49] = 5; Str(list, 49 + 33, "Bears");
await Route(list);
Check(guilds.AllianceGuilds.Count == 2 && guilds.AllianceGuilds[0].Name == "Wolves" && guilds.AllianceGuilds[0].MemberCount == 12
      && guilds.AllianceGuilds[0].Mark[0] == 1 && guilds.AllianceGuilds[0].Mark[1] == 2 && guilds.AllianceGuilds[1].Name == "Bears",
    "0xE9 alliance list", string.Join(", ", guilds.AllianceGuilds.Select(g => $"{g.Name}({g.MemberCount})")));

// 0xEB/01 remove result.
var remove = C1(7, 0xEB); remove[3] = 0x01; remove[4] = 1; remove[5] = 2; remove[6] = 1;
await Route(remove);
Check(guilds.HasGuild, "0xEB/01 remove alliance result handled");

// Every packet above reached a handler (none logged as unhandled).
var unhandled = (System.Collections.IDictionary?)typeof(PacketRouter).GetField("_unhandledPackets", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(router);
Check(unhandled == null || unhandled.Count == 0, "no packet unhandled", unhandled == null ? "field not found" : $"{unhandled.Count} unhandled");

guilds.Reset();
Check(!guilds.IsInWar && guilds.PendingRelationshipRequest == null && guilds.AllianceGuilds.Count == 0 && guilds.PendingWarRequestGuild == "",
    "Reset clears war / alliance state");

Console.WriteLine(failures == 0 ? "Guild war packet checks passed." : $"Guild war packet checks: {failures} failure(s).");
return failures == 0 ? 0 : 1;
