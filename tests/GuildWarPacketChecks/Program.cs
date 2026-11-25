// Offline checks for guild war, battle soccer and alliance / hostility packets: packets laid out as in OpenMU's
// ServerToClientPackets.xml are routed through the real PacketRouter and the GuildManager state is checked.
using System.Buffers;
using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using Client.Main;
using Client.Main.Configuration;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Game.Guild;
using Client.Main.Controls.UI.Game.Hud;
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
Check(GuildWarHudControl.GetView(guilds) == null, "HUD hidden without war");

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
var warView = GuildWarHudControl.GetView(guilds);
Check(warView == new GuildWarHudControl.View("GUILD WAR", "Your guild", "Bears", 3, 2, ""), "HUD shows war score", warView?.ToString() ?? "null");

// 0x63 ended (won).
var ended = C1(12, 0x63); ended[3] = 1; Str(ended, 4, "Bears");
await Route(ended);
Check(!guilds.IsInWar && guilds.WarOwnScore == 0, "0x63 war ended clears war state");
Check(GuildWarHudControl.GetView(guilds) == null, "HUD hidden after war end");

// F3/22 soccer time: OpenMU layout, 8 bytes, seconds LE at offset 6.
var time = C1(8, 0xF3); time[3] = 0x22; BinaryPrimitives.WriteUInt16LittleEndian(time.AsSpan(6), 299);
await Route(time);
Check(guilds.SoccerRemainingSeconds == 299, "F3/22 soccer time", guilds.SoccerRemainingSeconds.ToString());

// F3/23 soccer score, then the hide value (red goals 255).
var soccer = C1(22, 0xF3); soccer[3] = 0x23; Str(soccer, 4, "Wolves"); soccer[12] = 2; Str(soccer, 13, "Bears"); soccer[21] = 1;
await Route(soccer);
Check(guilds.SoccerScoreVisible && guilds.SoccerRedTeam == "Wolves" && guilds.SoccerRedGoals == 2 && guilds.SoccerBlueTeam == "Bears" && guilds.SoccerBlueGoals == 1,
    "F3/23 soccer score", $"{guilds.SoccerRedTeam} {guilds.SoccerRedGoals}:{guilds.SoccerBlueGoals} {guilds.SoccerBlueTeam}");
var soccerView = GuildWarHudControl.GetView(guilds);
Check(soccerView == new GuildWarHudControl.View("BATTLE SOCCER", "Wolves", "Bears", 2, 1, "4:59"), "HUD shows soccer timer and score", soccerView?.ToString() ?? "null");
var hide = C1(22, 0xF3); hide[3] = 0x23; hide[12] = 0xFF;
await Route(hide);
Check(!guilds.SoccerScoreVisible && guilds.SoccerRedTeam == "" && guilds.SoccerRemainingSeconds == 0, "F3/23 goals 255 hides the scoreboard");
Check(GuildWarHudControl.GetView(guilds) == null, "HUD hidden after soccer");

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

// Union tab: own guild 7 "Wolves" is alliance master, guild master 0x0100 of "Bears" (rival) is in scope.
guilds.StoreGuild(new GuildInfo { Id = 7, Name = "Wolves", AllianceName = "Wolves" });
guilds.StoreGuild(new GuildInfo { Id = 9, Name = "Bears" });
guilds.AssignPlayer(0x0100, 9, GuildMemberRole.GuildMaster, isSelf: false);
guilds.AssignPlayer(0x0101, 9, GuildMemberRole.NormalMember, isSelf: false);
guilds.AssignPlayer(0x0102, 7, GuildMemberRole.GuildMaster, isSelf: false);
var targets = GuildInfoWindow.GetUnionTargets(guilds, id => id == 0x0100 ? "BearBoss" : "other");
Check(targets.Count == 1 && targets[0] == new GuildInfoWindow.UnionTarget(0x0100, "BearBoss", "Bears"), "union targets: foreign guild masters only",
    string.Join(", ", targets));
Check(GuildInfoWindow.GetHostilityRequestType("Bears", "Bears") == GuildInfoWindow.RequestLeave
      && GuildInfoWindow.GetHostilityRequestType("Bears", "") == GuildInfoWindow.RequestJoin, "hostility against the rival ends it");
Check(guilds.IsAllianceMaster && GuildInfoWindow.GetRemovableAllianceGuild(guilds, "Bears") == "Bears"
      && GuildInfoWindow.GetRemovableAllianceGuild(guilds, "Nobody") == null, "alliance master can remove listed guilds");
guilds.StoreGuild(new GuildInfo { Id = 7, Name = "Wolves", AllianceName = "Lions" });
guilds.UpdateAllianceList(new() { new("Lions", 3, new byte[64]), new("Wolves", 12, new byte[64]) });
Check(GuildInfoWindow.GetRemovableAllianceGuild(guilds, "Lions") == null && GuildInfoWindow.GetRemovableAllianceGuild(guilds, "Wolves") == "Wolves",
    "alliance member can only leave");

// Request packets: 0xE5 hand-written big endian id; 0xE9 / 0xEB 01 from the package structs match the server layout.
var relReq = new byte[CharacterService.GuildRelationshipChangeRequestLength];
CharacterService.WriteGuildRelationshipChangeRequest(relReq, GuildInfoWindow.RelationshipHostility, GuildInfoWindow.RequestJoin, 0x0100);
Check(relReq.SequenceEqual(new byte[] { 0xC1, 7, 0xE5, 2, 1, 0x01, 0x00 }), "0xE5 request layout", Convert.ToHexString(relReq));
var allianceReq = new byte[MUnique.OpenMU.Network.Packets.ClientToServer.RequestAllianceList.Length];
_ = new MUnique.OpenMU.Network.Packets.ClientToServer.RequestAllianceList(allianceReq);
Check(allianceReq.SequenceEqual(new byte[] { 0xC1, 3, 0xE9 }), "0xE9 request layout", Convert.ToHexString(allianceReq));
var removeReq = new byte[MUnique.OpenMU.Network.Packets.ClientToServer.RemoveAllianceGuildRequest.Length];
new MUnique.OpenMU.Network.Packets.ClientToServer.RemoveAllianceGuildRequest(removeReq).GuildName = "Bears";
Check(removeReq.SequenceEqual(new byte[] { 0xC1, 12, 0xEB, 0x01, (byte)'B', (byte)'e', (byte)'a', (byte)'r', (byte)'s', 0, 0, 0 }), "0xEB/01 request layout",
    Convert.ToHexString(removeReq));

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
