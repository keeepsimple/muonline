// Offline checks for the MU Helper: the 257-byte configuration layout (as read by OpenMU's MuHelperSettingsSerializer)
// survives a parse / serialize round trip, and 0xAE / 0xBF-0x51 are routed through the real PacketRouter.
using System.Buffers;
using System.Buffers.Binary;
using System.Reflection;
using Client.Main;
using Client.Main.Configuration;
using Client.Main.Controllers;
using Client.Main.Core.Client;
using Client.Main.Networking;
using Client.Main.Networking.PacketHandling;
using Microsoft.Extensions.Logging.Abstractions;

int failures = 0;
void Check(bool ok, string name, string detail = "")
{
    if (!ok) failures++;
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? " - " + detail : "")}");
}

// 1. Fields land on the offsets / bits OpenMU reads.
var config = new MuHelperConfig
{
    PickJewel = true, PickZen = true, PickExtraItems = true,
    HuntingRange = 7, ObtainRange = 4, MaxSecondsAway = 0x0123,
    BasicSkillId = 0x0215, ActivationSkill1Id = 9, DelaySkill1 = 15, ActivationSkill2Id = 0x0302, DelaySkill2 = 20,
    BuffIntervalSeconds = 60, PotionThreshold = 40, HealThreshold = 70, HealPartyThreshold = 30, DrainLifeThreshold = 90,
    UseHealPotion = true, ReturnToOriginalPosition = true, AutoHealParty = true,
    UseDarkRaven = true, Skill1UseCondition = true, Skill1SubCondition = 3,
    Skill2UseTimer = true, Skill2SubCondition = 2, PickSelectItems = true,
    DarkRavenMode = 2, AutoAcceptGuild = true, FallbackBasicAttack = true,
};
config.BuffSkillIds[0] = 28; config.BuffSkillIds[2] = 0x0410;
config.ExtraItemNames[0] = "Jewel of Bless";
config.ExtraItemNames[11] = "Box of Luck";
byte[] blob = config.ToBytes();

Check(blob.Length == 257, "Blob is 257 bytes");
Check(blob[1] == (1 << 3 | 1 << 6 | 1 << 7), "Pick-up flags (byte 1)", $"{blob[1]:X2}");
Check(blob[2] == 0x47, "Hunting / obtain range nibbles (byte 2)", $"{blob[2]:X2}");
Check(BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(3)) == 0x0123, "DistanceMin word (3-4)");
Check(BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(5)) == 0x0215, "Basic skill word (5-6)");
Check(BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(11)) == 0x0302, "Activation skill 2 word (11-12)");
Check(BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(15)) == 60, "Buff interval word (15-16)");
Check(BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(17)) == 28 && BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(21)) == 0x0410, "Buff skill words (17-22)");
Check(blob[23] == 0x74 && blob[24] == 0x93, "HP threshold nibbles (23, 24)", $"{blob[23]:X2} {blob[24]:X2}");
Check(blob[25] == (1 << 0 | 1 << 4 | 1 << 7), "Behaviour flags (byte 25)", $"{blob[25]:X2}");
Check(blob[26] == (1 << 1 | 1 << 4 | 3 << 6), "Skill 1 flags (byte 26)", $"{blob[26]:X2}");
Check(blob[27] == (1 << 0 | 2 << 3 | 1 << 7), "Skill 2 / pick-up mode flags (byte 27)", $"{blob[27]:X2}");
Check(blob[28] == 2 && blob[29] == (1 << 2 | 1 << 3), "Pet mode and helper flags (28, 29)");
Check(System.Text.Encoding.ASCII.GetString(blob, 65, 14) == "Jewel of Bless" && blob[79] == 0, "Extra item name slot 0 (65)");
Check(System.Text.Encoding.ASCII.GetString(blob, 65 + 11 * 15, 11) == "Box of Luck", "Extra item name slot 11 (230)");

var parsed = MuHelperConfig.Parse(blob);
Check(parsed.ToBytes().AsSpan().SequenceEqual(blob), "Serialized config round-trips");
Check(parsed.HuntingRange == 7 && parsed.PotionThreshold == 40 && parsed.Skill1SubCondition == 3 && parsed.ExtraItemNames[11] == "Box of Luck",
    "Parsed values match");

// 2. A received blob is written back unchanged, including bytes the client does not use (marker, padding, spare bits).
var random = new Random(1234);
for (int run = 0; run < 200; run++)
{
    var raw = new byte[257];
    random.NextBytes(raw);
    for (int slot = 0; slot < 12; slot++)
    {
        var name = raw.AsSpan(65 + slot * 15, 15);
        int end = name.IndexOf((byte)0);
        if (end >= 0) name[end..].Clear(); // names end at the first terminator
    }

    if (!MuHelperConfig.Parse(raw).ToBytes().AsSpan().SequenceEqual(raw))
    {
        Check(false, "Random blob round-trips", $"run {run}");
        break;
    }
    if (run == 199) Check(true, "Random blobs round-trip (200 runs)");
}

// 2b. An approach that makes no progress is given up after the timeout; progress or a new id restarts it.
var approach = new MuHelperApproach();
Check(!approach.IsStuck(7, 10f, 0) && !approach.IsStuck(7, 10f, MuHelperApproach.TimeoutMs - 1), "No progress: not stuck before the timeout");
Check(approach.IsStuck(7, 10.2f, MuHelperApproach.TimeoutMs), "No progress: stuck at the timeout");
approach = new MuHelperApproach();
approach.IsStuck(7, 10f, 0);
Check(!approach.IsStuck(7, 8f, 3000) && !approach.IsStuck(7, 8f, 6000) && approach.IsStuck(7, 8f, 7000), "Getting closer restarts the timeout");
Check(!approach.IsStuck(8, 8f, 7000), "A new target restarts the timeout");
approach.Reset();
Check(!approach.IsStuck(8, 8f, 20000), "Reset restarts the timeout");

// 3. Packets through the real router.
var logs = NullLoggerFactory.Instance;
var state = new CharacterState(logs) { Id = 0x0042, Name = "Tester" };
var network = new NetworkManager(logs, new MuOnlineSettings { ProtocolVersion = "Season6" }, state, new ScopeManager(logs, state));
var router = (PacketRouter)typeof(NetworkManager).GetField("_packetRouter", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(network)!;
var dispatcher = (MainThreadDispatcher)typeof(MuGame).GetField("_mainThreadDispatcher", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
router.SetRoutingMode(false);

async Task Route(byte[] packet)
{
    await router.RoutePacketAsync(new ReadOnlySequence<byte>(packet));
    dispatcher.ProcessPending();
}

var configPacket = new byte[261];
configPacket[0] = 0xC2;
BinaryPrimitives.WriteUInt16BigEndian(configPacket.AsSpan(1), 261);
configPacket[3] = 0xAE;
blob.CopyTo(configPacket, 4);
await Route(configPacket);
Check(MuHelperState.GetConfig("Tester").ToBytes().AsSpan().SequenceEqual(blob), "0xAE stores the configuration of the character");
Check(MuHelperState.GetConfig("Other").HuntingRange == new MuHelperConfig().HuntingRange, "Another character gets the defaults");

byte[] Status(bool consumeMoney, uint money, bool paused)
{
    var p = new byte[16];
    p[0] = 0xC1; p[1] = 16; p[2] = 0xBF; p[3] = 0x51;
    p[4] = (byte)(consumeMoney ? 1 : 0);
    BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(8), money);
    p[12] = (byte)(paused ? 1 : 0);
    return p;
}

await Route(Status(false, 0, false));
Check(MuHelperState.IsRunning, "Status start -> running");
await Route(Status(true, 1500, false));
await Route(Status(true, 2500, false));
Check(MuHelperState.IsRunning && MuHelperState.LastZenCost == 2500 && MuHelperState.TotalZenCost == 4000, "Zen charges are summed", $"{MuHelperState.TotalZenCost}");
await Route(Status(false, 0, true));
Check(!MuHelperState.IsRunning, "Status pause -> stopped");

var unhandled = router.GetUnhandledPacketCounts();
Check(unhandled.Count == 0, "All packets were routed to a handler", string.Join(", ", unhandled.Select(u => $"{u.Key.Code:X2}-{u.Key.SubCode:X2}")));

Console.WriteLine(failures == 0 ? "MU Helper checks passed." : $"MU Helper checks: {failures} failure(s).");
return failures == 0 ? 0 : 1;
