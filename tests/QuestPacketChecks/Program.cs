// Offline checks for the Season 6 quest packets (C1/C2 F6): packets laid out as in OpenMU's ServerToClientPackets.xml
// are routed through the real PacketRouter (so code/sub-code registration is covered) and the quest state is checked.
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Reflection;
using Client.Main;
using Client.Main.Configuration;
using Client.Main.Controllers;
using Client.Main.Core.Client;
using Client.Main.Networking;
using Client.Main.Networking.PacketHandling;
using Client.Main.Networking.PacketHandling.Handlers;
using Client.Main.Controls.UI.Game.Quest;
using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.Network.Packets.ServerToClient;

var logs = NullLoggerFactory.Instance;
var settings = new MuOnlineSettings { ProtocolVersion = "Season6" };
var state = new CharacterState(logs) { Id = 0x0042, Name = "Tester" };
var network = new NetworkManager(logs, settings, state, new ScopeManager(logs, state));
var router = (PacketRouter)typeof(NetworkManager).GetField("_packetRouter", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(network)!;
var dispatcher = (MainThreadDispatcher)typeof(MuGame).GetField("_mainThreadDispatcher", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
var handler = (QuestHandler)typeof(PacketRouter).GetField("_questHandler", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(router)!;
var log = network.GetQuestLog();
router.SetRoutingMode(false);

int failures = 0;
void Check(bool ok, string name, string detail = "")
{
    if (!ok) failures++;
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? " - " + detail : "")}");
}

Check(ReferenceEquals(log, handler.Quests), "NetworkManager.GetQuestLog exposes the handler's log");

async Task Route(byte[] packet)
{
    await router.RoutePacketAsync(new ReadOnlySequence<byte>(packet));
    dispatcher.ProcessPending();
}

byte[] C1(byte sub, int length)
{
    var p = new byte[length];
    p[0] = 0xC1; p[1] = (byte)length; p[2] = 0xF6; p[3] = sub;
    return p;
}

void U16(byte[] p, int at, int v) => BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(at), (ushort)v);
void U32(byte[] p, int at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(at), v);

// QuestProgress/QuestState (C1, 251): number @4, group @6, counts @8/9, conditions @11 (26 each), rewards @141 (22 each).
byte[] Basic(byte sub, int group, int number, (ConditionType T, int Id, uint Req, uint Cur)[] conds, (RewardType T, int Id, uint N)[] rewards)
{
    var p = C1(sub, 251);
    U16(p, 4, number); U16(p, 6, group); p[8] = (byte)conds.Length; p[9] = (byte)rewards.Length;
    for (int i = 0; i < conds.Length; i++)
    {
        int o = 11 + i * 26;
        U32(p, o, (uint)conds[i].T); U16(p, o + 4, conds[i].Id); U32(p, o + 6, conds[i].Req); U32(p, o + 10, conds[i].Cur);
    }
    for (int i = 0; i < rewards.Length; i++)
    {
        int o = 141 + i * 22;
        U32(p, o, (uint)rewards[i].T); U16(p, o + 4, rewards[i].Id); U32(p, o + 6, rewards[i].N);
    }
    return p;
}

// QuestProgressExtended/QuestStateExtended (C2, 272): counts @5/6, number @8, group @10, conditions @12 (28 each), rewards @152 (24 each).
byte[] Extended(byte sub, int group, int number, (ConditionType T, int Id, uint Req, uint Cur)[] conds, (RewardType T, int Id, uint N)[] rewards)
{
    var p = new byte[272];
    p[0] = 0xC2; BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(1), 272); p[3] = 0xF6; p[4] = sub;
    p[5] = (byte)conds.Length; p[6] = (byte)rewards.Length; U16(p, 8, number); U16(p, 10, group);
    for (int i = 0; i < conds.Length; i++)
    {
        int o = 12 + i * 28;
        U16(p, o, (int)conds[i].T); U16(p, o + 2, conds[i].Id); U32(p, o + 4, conds[i].Req); U32(p, o + 8, conds[i].Cur);
    }
    for (int i = 0; i < rewards.Length; i++)
    {
        int o = 152 + i * 24;
        U16(p, o, (int)rewards[i].T); U16(p, o + 2, rewards[i].Id); U32(p, o + 4, rewards[i].N);
    }
    return p;
}

byte[] NumberGroup(byte sub, int length, int group, int number)
{
    var p = C1(sub, length);
    U16(p, 4, number); U16(p, 6, group);
    return p;
}

// F6 03 QuestEventResponse, as OpenMU sends it: two quest identifications with number 1.
var ev = C1(0x03, 12);
U16(ev, 4, 1); U16(ev, 8, 1);
await Route(ev);
Check(log.EventQuests.SequenceEqual(new[] { new QuestLog.QuestId(0, 1), new QuestLog.QuestId(0, 1) }), "F6/03 event quests stored",
    string.Join(", ", log.EventQuests));

// F6 0A AvailableQuests: NPC 257 offers group 18 starting at 1 and group 19 starting at 4.
var avail = C1(0x0A, 16);
U16(avail, 4, 257); U16(avail, 6, 2); U16(avail, 8, 1); U16(avail, 10, 18); U16(avail, 12, 4); U16(avail, 14, 19);
await Route(avail);
Check(log.AvailableQuestsNpc == 257 && log.AvailableQuests.SequenceEqual(new[] { new QuestLog.QuestId(18, 1), new QuestLog.QuestId(19, 4) }),
    "F6/0A available quests stored", $"npc {log.AvailableQuestsNpc}: {string.Join(", ", log.AvailableQuests)}");
var truncated = (byte[])avail.Clone();
truncated[1] = 12; // count says 2, only one entry present
await Route(truncated[..12]);
Check(log.AvailableQuests.Count == 1, "F6/0A count is clamped to the packet length", $"{log.AvailableQuests.Count}");

// Player picks group 18 → QuestStepInfo with the starting number answers the selection (accept/refuse dialog).
handler.SelectQuest(new QuestLog.QuestId(18, 1));
Check(log.SelectedQuest == new QuestLog.QuestId(18, 1), "Picking a quest remembers the selection");
await Route(NumberGroup(0x0B, 11, 18, 1));
Check(log.SelectedQuest == null && log.LastStepByGroup[18] == 1, "F6/0B starting step answers the selection");
await Route(NumberGroup(0x0B, 11, 18, 2));
Check(log.LastStepByGroup[18] == 2, "F6/0B later step stored", $"{log.LastStepByGroup[18]}");

// F6 0C QuestProgress (C1): kill monster 3 (4/10); rewards 1000 exp, 5000 zen.
await Route(Basic(0x0C, 18, 2, new[] { (ConditionType.MonsterKills, 3, 10u, 4u) },
    new[] { (RewardType.Experience, 0, 1000u), (RewardType.Money, 0, 5000u) }));
var q = log.ActiveQuests.GetValueOrDefault((ushort)18);
Check(q != null && q.Id == new QuestLog.QuestId(18, 2) && q.Conditions.Count == 1
      && q.Conditions[0] == new QuestLog.Condition(ConditionType.MonsterKills, 3, 10, 4)
      && q.Rewards.SequenceEqual(new[] { new QuestLog.Reward(RewardType.Experience, 0, 1000), new QuestLog.Reward(RewardType.Money, 0, 5000) })
      && !q.AllConditionsMet,
    "F6/0C quest progress (C1) parsed", q == null ? "missing" : $"{q.Id} {string.Join(";", q.Conditions)} {string.Join(";", q.Rewards)}");

// F6 0C QuestProgressExtended (C2) as answer to picking the active quest again: kills done (10/10).
handler.SelectQuest(new QuestLog.QuestId(18, 1));
await Route(Extended(0x0C, 18, 2, new[] { (ConditionType.MonsterKills, 3, 10u, 10u) }, new[] { (RewardType.Item, (14 << 9) | 13, 1u) }));
q = log.ActiveQuests.GetValueOrDefault((ushort)18);
Check(q != null && q.Id == new QuestLog.QuestId(18, 2) && q.AllConditionsMet && log.SelectedQuest == null
      && q.Rewards.SingleOrDefault() == new QuestLog.Reward(RewardType.Item, (14 << 9) | 13, 1),
    "F6/0C quest progress (C2 extended) parsed and answers the selection",
    q == null ? "missing" : $"{q.Id} {string.Join(";", q.Conditions)} {string.Join(";", q.Rewards)}");

// QuestProgress without active quest carries only the group.
log.ActiveQuests[21] = new QuestLog.Quest(new(21, 7), [], []);
await Route(Basic(0x0C, 21, 0, [], []));
Check(!log.ActiveQuests.ContainsKey(21), "F6/0C without active quest clears the group");

// F6 0D QuestCompletionResponse: inventory full keeps the quest, completed removes it.
var completion = NumberGroup(0x0D, 9, 18, 2);
completion[8] = 3;
await Route(completion);
Check(log.ActiveQuests.ContainsKey(18), "F6/0D refused completion keeps the quest");
completion[8] = 1;
await Route(completion);
Check(!log.ActiveQuests.ContainsKey(18), "F6/0D completed quest removed");

// F6 0F QuestCancelled (number is always 0, the group matters).
log.ActiveQuests[19] = new QuestLog.Quest(new(19, 5), [], []);
await Route(NumberGroup(0x0F, 8, 19, 0));
Check(!log.ActiveQuests.ContainsKey(19), "F6/0F cancelled quest removed");

// F6 1B QuestState (C1): bring item 14/13 (0/1).
await Route(Basic(0x1B, 19, 5, new[] { (ConditionType.Item, (14 << 9) | 13, 1u, 0u) }, new[] { (RewardType.GensContribution, 0, 10u) }));
q = log.ActiveQuests.GetValueOrDefault((ushort)19);
Check(q != null && q.Id == new QuestLog.QuestId(19, 5) && q.Conditions.Single() == new QuestLog.Condition(ConditionType.Item, (14 << 9) | 13, 1, 0)
      && q.Rewards.Single() == new QuestLog.Reward(RewardType.GensContribution, 0, 10),
    "F6/1B quest state (C1) parsed", q == null ? "missing" : $"{string.Join(";", q.Conditions)} {string.Join(";", q.Rewards)}");

// F6 1B QuestStateExtended (C2): two conditions.
await Route(Extended(0x1B, 20, 9, new[] { (ConditionType.MonsterKills, 7, 5u, 2u), (ConditionType.Item, (12 << 9) | 15, 3u, 3u) }, []));
q = log.ActiveQuests.GetValueOrDefault((ushort)20);
Check(q != null && q.Id == new QuestLog.QuestId(20, 9) && q.Conditions.Count == 2
      && q.Conditions[1] == new QuestLog.Condition(ConditionType.Item, (12 << 9) | 15, 3, 3) && !q.AllConditionsMet,
    "F6/1B quest state (C2 extended) parsed", q == null ? "missing" : string.Join(";", q.Conditions));

// Short packets are ignored instead of throwing.
await Route(C1(0x0C, 20));
await Route(C1(0x1B, 20));
Check(log.ActiveQuests.ContainsKey(19) && log.ActiveQuests.ContainsKey(20), "Short F6/0C and F6/1B packets are ignored");

// F6 1A QuestStateList (count @4, ids @5): drops groups not listed, keeps known quests, adds placeholders.
var stateList = C1(0x1A, 13);
stateList[4] = 2; U16(stateList, 5, 9); U16(stateList, 7, 20); U16(stateList, 9, 3); U16(stateList, 11, 22);
await Route(stateList);
Check(!log.ActiveQuests.ContainsKey(19) && log.ActiveQuests[20].Conditions.Count == 2
      && log.ActiveQuests.TryGetValue(22, out var placeholder) && placeholder.Id == new QuestLog.QuestId(22, 3) && placeholder.Conditions.Count == 0,
    "F6/1A quest state list syncs the active groups", string.Join(", ", log.ActiveQuests.Values.Select(v => v.Id)));

// Journal view model: what the window displays.
await Route(Basic(0x1B, 22, 3, new[] { (ConditionType.MonsterKills, 7, 5u, 5u) }, new[] { (RewardType.Money, 0, 5000u), (RewardType.Experience, 0, 1000u) }));
var rows = QuestJournalWindow.BuildRows(log);
Check(rows.Select(r => (r.Title, r.Status, r.Ready)).SequenceEqual(new[] { ("Quest 20/9", "Step 9", false), ("Quest 22/3", "Ready", true) }),
    "Journal rows: ordered by group with step / ready state", string.Join("; ", rows));
var lines = QuestJournalWindow.ConditionLines(log.ActiveQuests[20]);
Check(lines.Count == 2 && lines[0].Text.StartsWith("Kill ") && lines[0].Value == "2/5" && !lines[0].Met && lines[1].Value == "3/3" && lines[1].Met,
    "Journal condition lines show current/required", string.Join("; ", lines));
var rewardLines = QuestJournalWindow.RewardLines(log.ActiveQuests[22]);
Check(rewardLines.SequenceEqual(new[] { $"{5000:N0} Zen", $"{1000:N0} Exp" }), "Journal reward lines", string.Join("; ", rewardLines));
Check(QuestJournalWindow.ResolveSelection(log, 22)?.Id.Group == 22 && QuestJournalWindow.ResolveSelection(log, 99)?.Id.Group == 20,
    "Journal selection falls back to the first active quest");

// Requests sent by the journal (captured from a pipe-backed connection): F6 1A list, F6 1B state, F6 0F cancel.
var toServer = new Pipe();
var connection = new Connection(new DuplexPipe(new Pipe().Reader, toServer.Writer), null, null, NullLogger<Connection>.Instance);
var connectionManager = typeof(NetworkManager).GetField("_connectionManager", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(network)!;
connectionManager.GetType().GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(connectionManager, connection);
var service = network.GetCharacterService();
async Task<string> Sent(Func<Task<bool>> send)
{
    if (!await send()) return "not sent";
    var read = await toServer.Reader.ReadAsync();
    var bytes = read.Buffer.ToArray();
    toServer.Reader.AdvanceTo(read.Buffer.End);
    return Convert.ToHexString(bytes);
}
var hex = await Sent(() => service.SendActiveQuestListRequestAsync());
Check(hex == "C104F61A", "ActiveQuestListRequest bytes", hex);
hex = await Sent(() => service.SendQuestStateRequestAsync(9, 20));
Check(hex == "C108F61B09001400", "QuestStateRequest bytes", hex);
hex = await Sent(() => service.SendQuestCancelRequestAsync(3, 22));
Check(hex == "C108F60F03001600", "QuestCancelRequest bytes", hex);
await Route(NumberGroup(0x0F, 8, 22, 0));
Check(!log.ActiveQuests.ContainsKey(22) && QuestJournalWindow.BuildRows(log).Count == 1, "Cancelled quest leaves the journal");

var unhandled = router.GetUnhandledPacketCounts();
Check(unhandled.Count == 0, "All packets were routed to a handler", string.Join(", ", unhandled.Select(u => $"{u.Key.Code:X2}-{u.Key.SubCode:X2}")));

// Disconnect / character switch: the session reset clears the log.
log.AvailableQuests.Add(new(1, 1)); log.LastStepByGroup[1] = 1; log.SelectedQuest = new(1, 1);
await router.OnDisconnected();
dispatcher.ProcessPending();
Check(log.ActiveQuests.Count == 0 && log.AvailableQuests.Count == 0 && log.LastStepByGroup.Count == 0 && log.EventQuests.Count == 0 && log.SelectedQuest == null,
    "Session reset clears the quest log");

Console.WriteLine(failures == 0 ? "Quest packet checks passed." : $"Quest packet checks: {failures} failure(s).");
return failures == 0 ? 0 : 1;

sealed record DuplexPipe(PipeReader Input, PipeWriter Output) : IDuplexPipe;
