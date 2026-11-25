// Offline checks for the Castle Siege packets: packets laid out as in OpenMU's ServerToClientPackets.xml (0xB2-0xBB) are
// routed through the real PacketRouter (so code/sub-code registration is covered) and the CastleSiegeManager state is checked.
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
using Microsoft.Extensions.Logging.Abstractions;
using static Client.Main.Core.Client.CastleSiegeManager;

var logs = NullLoggerFactory.Instance;
var settings = new MuOnlineSettings { ProtocolVersion = "Season6" };
var state = new CharacterState(logs) { Id = 0x0042, Name = "Tester" };
var network = new NetworkManager(logs, settings, state, new ScopeManager(logs, state));
var router = (PacketRouter)typeof(NetworkManager).GetField("_packetRouter", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(network)!;
var dispatcher = (MainThreadDispatcher)typeof(MuGame).GetField("_mainThreadDispatcher", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
router.SetRoutingMode(false);
var siege = network.GetCastleSiegeManager();

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

// C1 packet of the given total length: code, sub-code, then fields written by the caller.
byte[] C1(int length, byte code, byte sub)
{
    var data = new byte[length];
    data[0] = 0xC1; data[1] = (byte)length; data[2] = code; data[3] = sub;
    return data;
}

// C2 list packet: Result@4, count (uint LE)@8, entries@12.
byte[] C2List(byte code, int entrySize, int count)
{
    var data = new byte[12 + entrySize * count];
    data[0] = 0xC2;
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(1), (ushort)data.Length);
    data[3] = code; data[4] = 1;
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), (uint)count);
    return data;
}

void Str(byte[] data, int offset, string value) => Encoding.UTF8.GetBytes(value).CopyTo(data, offset);

// B2/00 status: RegisterMark, owner "Lords"/"Master", 1 hour left.
var status = C1(46, 0xB2, 0x00);
status[4] = 1; status[5] = 3;
BinaryPrimitives.WriteUInt16BigEndian(status.AsSpan(6), 2026); status[8] = 11; status[9] = 20; status[10] = 18;
BinaryPrimitives.WriteUInt16BigEndian(status.AsSpan(12), 2026); status[14] = 11; status[15] = 27; status[16] = 18;
BinaryPrimitives.WriteUInt16BigEndian(status.AsSpan(18), 2026); status[20] = 11; status[21] = 29; status[22] = 20; status[23] = 30;
Str(status, 24, "Lords"); Str(status, 32, "Master");
BinaryPrimitives.WriteUInt32BigEndian(status.AsSpan(42), 3600);
await Route(status);
Check(siege.HasStatus && siege.State == SiegeState.RegisterMark && siege.OwnerGuild == "Lords" && siege.OwnerGuildMaster == "Master",
    "B2/00 status: state and owner", $"{siege.State} {siege.OwnerGuild}/{siege.OwnerGuildMaster}");
Check(siege.SiegeStart == new DateTime(2026, 11, 29, 20, 30, 0) && siege.Remaining.TotalSeconds is > 3590 and <= 3600,
    "B2/00 status: siege start and remaining time", $"{siege.SiegeStart} {siege.Remaining}");

// B2/03 registration state: registered, 7 marks, rank 2.
var reg = C1(19, 0xB2, 0x03);
reg[4] = 1; Str(reg, 5, "Wolves");
BinaryPrimitives.WriteUInt32BigEndian(reg.AsSpan(13), 7); reg[18] = 2;
await Route(reg);
Check(siege.IsRegistered == true && siege.RegisteredGuildName == "Wolves" && siege.Marks == 7 && siege.RegistrationRank == 2,
    "B2/03 registration state");

// B4 registered guild list (14-byte entries, marks big endian).
var list = C2List(0xB4, 14, 2);
Str(list, 12, "Wolves"); BinaryPrimitives.WriteUInt32BigEndian(list.AsSpan(20), 7); list[25] = 1;
Str(list, 26, "Bears"); BinaryPrimitives.WriteUInt32BigEndian(list.AsSpan(34), 300); list[39] = 2;
await Route(list);
Check(siege.RegisteredGuilds.Count == 2 && siege.RegisteredGuilds[1] == new RegisteredGuild("Bears", 300, false, 2),
    "B4 registered guild list", string.Join(", ", siege.RegisteredGuilds));

// B5 participants (16-byte entries, score little endian).
var participants = C2List(0xB5, 16, 1);
participants[12] = 1; participants[13] = 1; Str(participants, 14, "Lords");
BinaryPrimitives.WriteUInt32LittleEndian(participants.AsSpan(24), 1234);
await Route(participants);
Check(siege.Participants.Count == 1 && siege.Participants[0] == new Participant(JoinSide.Defense, true, "Lords", 1234),
    "B5 participant list");

// Battle: side, switch, crown.
var side = C1(5, 0xB2, 0x19); side[4] = 2;
await Route(side);
Check(siege.Side == JoinSide.Attack1, "B2/19 join side");

var sw = C1(27, 0xB2, 0x20);
BinaryPrimitives.WriteUInt16BigEndian(sw.AsSpan(4), 0x1234); sw[6] = 1; sw[7] = 2; Str(sw, 8, "Wolves"); Str(sw, 16, "Hero");
await Route(sw);
Check(siege.Switches.TryGetValue(0x1234, out var info) && info == new SwitchInfo(true, JoinSide.Attack1, "Wolves", "Hero"),
    "B2/20 switch info");

var crown = C1(5, 0xB2, 0x16); crown[4] = 0;
await Route(crown);
var access = C1(12, 0xB2, 0x15); access[4] = 0; BinaryPrimitives.WriteUInt32LittleEndian(access.AsSpan(8), 12_500);
await Route(access);
Check(siege.CrownAccessible == true && siege.CrownAccessState == CrownAccess.Started && siege.CrownAccumulatedMs == 12_500,
    "B2/16 + B2/15 crown state and access progress");

// B3 NPC list (28-byte entries).
var npcs = C2List(0xB3, 28, 1);
BinaryPrimitives.WriteUInt32LittleEndian(npcs.AsSpan(12), 277); BinaryPrimitives.WriteUInt32LittleEndian(npcs.AsSpan(16), 3);
BinaryPrimitives.WriteUInt32LittleEndian(npcs.AsSpan(28), 50_000); BinaryPrimitives.WriteUInt32LittleEndian(npcs.AsSpan(32), 42_000);
npcs[36] = 81; npcs[37] = 92; npcs[38] = 1;
await Route(npcs);
Check(siege.DefenseNpcs.Count == 1 && siege.DefenseNpcs[0] is { Number: 277, Index: 3, MaxHp: 50_000, CurrentHp: 42_000, X: 81, Y: 92, IsAlive: true },
    "B3 gate list");

// B6 / BB commander mini map.
var players = new byte[] { 0xC2, 0, 12, 0xB6, 2, 0, 0, 0, 10, 20, 30, 40 };
await Route(players);
var mapNpcs = new byte[] { 0xC2, 0, 8, 0xBB, 1, 0, 81, 92 };
await Route(mapNpcs);
Check(siege.MiniMapPlayers.Count == 2 && siege.MiniMapPlayers[1] == (30, 40) && siege.MiniMapNpcs.Count == 1 && siege.MiniMapNpcs[0] == (true, 81, 92),
    "B6 / BB mini map positions");

// B2/08 tax info: 2 % chaos, 3 % store, treasury (long big endian).
var tax = C1(15, 0xB2, 0x08); tax[4] = 1; tax[5] = 2; tax[6] = 3;
BinaryPrimitives.WriteUInt64BigEndian(tax.AsSpan(7), 5_000_000_000);
await Route(tax);
Check(siege.HasTaxInfo && siege.ChaosTaxRate == 2 && siege.StoreTaxRate == 3 && siege.Treasury == 5_000_000_000, "B2/08 tax info");

// B2/05 buy rebuilds a destroyed gate, B2/07 upgrades its defense and regeneration levels.
var destroyed = C2List(0xB3, 28, 1);
BinaryPrimitives.WriteUInt32LittleEndian(destroyed.AsSpan(12), 277); BinaryPrimitives.WriteUInt32LittleEndian(destroyed.AsSpan(16), 3);
await Route(destroyed);
var buy = C1(16, 0xB2, 0x05); buy[4] = 1;
BinaryPrimitives.WriteUInt32LittleEndian(buy.AsSpan(8), 277); BinaryPrimitives.WriteUInt32LittleEndian(buy.AsSpan(12), 3);
await Route(buy);
Check(siege.DefenseNpcs.Count == 1 && siege.DefenseNpcs[0].IsAlive, "B2/05 defense buy marks the gate built");

byte[] Upgrade(byte result, uint type, uint level)
{
    var p = C1(24, 0xB2, 0x07); p[4] = result;
    BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(8), 277); BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(12), 3);
    BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(16), type); BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(20), level);
    return p;
}
await Route(Upgrade(1, 1, 2));
await Route(Upgrade(1, 2, 1));
await Route(Upgrade(3, 1, 3)); // not enough Zen: no change
Check(siege.DefenseNpcs[0] is { DefenseLevel: 2, RegenerationLevel: 1 }, "B2/07 defense upgrade levels", siege.DefenseNpcs[0].ToString());

// B9/02 owner logo: 32 packed bytes, high nibble first.
var logo = C1(36, 0xB9, 0x02); logo[4] = 0x12; logo[35] = 0xF0;
await Route(logo);
Check(siege.OwnerGuildMark is { Length: 64 } m && m[0] == 1 && m[1] == 2 && m[62] == 15 && m[63] == 0, "B9/02 owner logo unpacked");

// B9/03 Land of Trials guard info, B2/1F public access change.
var guard = C1(20, 0xB9, 0x03); guard[4] = 3; guard[5] = 1;
BinaryPrimitives.WriteUInt32LittleEndian(guard.AsSpan(8), 50_000);
BinaryPrimitives.WriteUInt32LittleEndian(guard.AsSpan(12), 300_000);
BinaryPrimitives.WriteUInt32LittleEndian(guard.AsSpan(16), 10_000);
await Route(guard);
Check(siege.HuntZoneAccessType == HuntZoneAccess.OwnerGuildMaster && siege.IsHuntZonePublic && siege.HuntZoneFee == 50_000
      && siege.HuntZoneMaxFee == 300_000 && siege.HuntZoneFeeStep == 10_000, "B9/03 hunting zone guard info");

var entrance = C1(6, 0xB2, 0x1F); entrance[4] = 1; entrance[5] = 0;
await Route(entrance);
Check(!siege.IsHuntZonePublic, "B2/1F hunting zone entrance setting");

var enter = C1(5, 0xB9, 0x05); enter[4] = 0;
await Route(enter);
var unhandled = router.GetUnhandledPacketCounts();
(byte, byte)[] added = { (0xB2, 0x05), (0xB2, 0x07), (0xB2, 0x1F), (0xB9, 0x02), (0xB9, 0x03), (0xB9, 0x05) };
Check(!added.Any(c => unhandled.Keys.Any(k => (k.Code, k.SubCode) == c)), "B2/05, B2/07, B2/1F, B9/02, B9/03, B9/05 have handlers",
    string.Join(", ", unhandled.Keys.Select(k => $"{k.Code:X2}/{k.SubCode:X2}")));

// A status outside Ready/Start drops the battle data.
status[5] = 8;
await Route(status);
Check(siege.Side == JoinSide.None && siege.Switches.Count == 0 && siege.CrownAccessible == null, "B2/00 End state clears battle data");

// Outgoing B2/04: inventory slot 20 (backpack index 8) goes out as ItemIndex 8, OpenMU adds the 12 equipment slots back.
var mark = Client.Main.Networking.Services.CastleSiegeService.BuildMarkRegistrationPacket(20);
Check(mark.SequenceEqual(new byte[] { 0xC1, 5, 0xB2, 0x04, 8 }), "B2/04 mark registration sends the backpack index", Convert.ToHexString(mark));

Console.WriteLine(failures == 0 ? "Castle Siege packet checks passed." : $"Castle Siege packet checks: {failures} failure(s).");
return failures == 0 ? 0 : 1;
