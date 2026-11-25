// Offline checks that the OpenMU NPCs / event objects which used to be dropped by ScopeHandler (type not in
// NpcDatabase) are now registered, instantiable, reference only models of the official 1.20.61 data, and survive an
// AddNpcsToScope (0x13) packet routed through the real PacketRouter.
using System.Buffers;
using System.Buffers.Binary;
using System.Reflection;
using Client.Main;
using Client.Main.Configuration;
using Client.Main.Controllers;
using Client.Main.Core.Client;
using Client.Main.Core.Models;
using Client.Main.Core.Utilities;
using Client.Main.Networking;
using Client.Main.Networking.PacketHandling;
using Client.Main.Objects;
using Microsoft.Extensions.Logging.Abstractions;

string dataList = args.Length > 0 ? args[0]
    : System.IO.Path.Combine(AppContext.BaseDirectory, "datalist-1.20.61.txt");
var dataFiles = new HashSet<string>(
    File.ReadLines(dataList).Select(l => l.Split('\t')[0].Replace('\\', '/')),
    StringComparer.OrdinalIgnoreCase);
// BMD texture names are .jpg/.tga; the data ships them as .OZJ/.OZT.
bool DataExists(string path) => dataFiles.Contains("Data/" + Path.ChangeExtension(path, Path.GetExtension(path).ToLowerInvariant() switch
{
    ".jpg" => ".OZJ",
    ".tga" => ".OZT",
    var e => e,
}));

int failures = 0;
void Check(bool ok, string name, string detail = "")
{
    if (!ok) failures++;
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? " - " + detail : "")}");
}

ushort[] talkNpcs = { 415, 417, 452, 453, 478, 522, 545, 546, 547, 380, 381, 382, 383, 384, 220, 467, 468, 469, 470, 471, 472, 473, 474, 475 };
ushort[] positionObjects = { 216, 217, 218 };
ushort[] mapDrawnObjects = { 204, 205, 206, 207, 208, 209 }; // drawn by EncTerrain35, the NPC is a model-less marker
const ushort SoccerBall = 200; // no ball model in 1.20.61

foreach (var id in talkNpcs.Concat(positionObjects).Concat(mapDrawnObjects).Append(SoccerBall))
{
    if (!NpcDatabase.TryGetNpcType(id, out var type))
    {
        Check(false, $"{id} registered in NpcDatabase");
        continue;
    }

    object? instance = null;
    try { instance = Activator.CreateInstance(type); } catch (Exception ex) { Check(false, $"{id} instantiable", ex.Message); continue; }

    bool expectNpc = id != SoccerBall;
    Check(instance is WalkerObject && (expectNpc ? instance is NPCObject : instance is MonsterObject),
        $"{id} {type.Name} is a {(expectNpc ? "NPC" : "monster")}", NpcDatabase.GetNpcName(id));

    string? model = type.GetProperty("ModelPath")?.GetValue(instance) as string;
    if (talkNpcs.Contains(id) || positionObjects.Contains(id))
        Check(model != null && DataExists(model), $"{id} model exists in 1.20.61", model ?? "<none>");
    else
        Check(model == null, $"{id} has no own model", model ?? "<none>");

    if (type.GetProperty("TexturePath")?.GetValue(instance) is string texture)
        Check(DataExists(texture), $"{id} texture exists in 1.20.61", texture);
}

// The Little Santas are one model with eight distinct textures.
var santaTextures = Enumerable.Range(468, 8)
    .Select(id => NpcDatabase.TryGetNpcType((ushort)id, out var t) ? t.GetProperty("TexturePath")?.GetValue(Activator.CreateInstance(t)) as string : null)
    .ToList();
Check(santaTextures.All(t => t != null) && santaTextures.Distinct().Count() == 8, "Little Santas use eight different textures");

// Route one AddNpcsToScope (0x13) packet through the real router and check every entry lands in scope by name.
var logs = NullLoggerFactory.Instance;
var state = new CharacterState(logs) { Id = 0x0042, Name = "Tester" };
var network = new NetworkManager(logs, new MuOnlineSettings { ProtocolVersion = "Season6" }, state, new ScopeManager(logs, state));
var router = (PacketRouter)typeof(NetworkManager).GetField("_packetRouter", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(network)!;
var dispatcher = (MainThreadDispatcher)typeof(MuGame).GetField("_mainThreadDispatcher", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
router.SetRoutingMode(false);

ushort[] routed = { 478, 547, 452, 205, 216, 217, 380, 468, 200 };
const int entryLength = 10; // AddNpcsToScope.NpcData.Length, no effects
var packet = new byte[5 + routed.Length * entryLength];
packet[0] = 0xC2;
BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(1), (ushort)packet.Length);
packet[3] = 0x13;
packet[4] = (byte)routed.Length;
for (int i = 0; i < routed.Length; i++)
{
    var entry = packet.AsSpan(5 + i * entryLength, entryLength);
    BinaryPrimitives.WriteUInt16BigEndian(entry, (ushort)(0x2000 + i));
    BinaryPrimitives.WriteUInt16BigEndian(entry[2..], routed[i]);
    entry[4] = entry[6] = (byte)(100 + i);
    entry[5] = entry[7] = 120;
}
await router.RoutePacketAsync(new ReadOnlySequence<byte>(packet));
dispatcher.ProcessPending();

var scope = network.GetScopeManager();
for (int i = 0; i < routed.Length; i++)
{
    var npc = scope.GetScopeObjectByMaskedId((ushort)(0x2000 + i)) as NpcScopeObject;
    string expected = NpcDatabase.GetNpcName(routed[i]);
    Check(npc != null && npc.TypeNumber == routed[i] && npc.Name == expected && !expected.StartsWith("Unknown"),
        $"0x13 spawns {routed[i]} as a known NPC", npc?.Name ?? "<not in scope>");
}
Check(router.GetUnhandledPacketCounts().Count == 0, "0x13 routed to a handler");

Console.WriteLine(failures == 0 ? "NPC spawn checks passed." : $"NPC spawn checks: {failures} failure(s).");
return failures == 0 ? 0 : 1;
