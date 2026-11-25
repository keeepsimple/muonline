// Offline checks that OpenMU monster types without their own model (Kalima tiers, Chaos Castle, Devil Square 7,
// Balgass, Aida, Kanturu Ruins, Crywolf) resolve to a client class, instantiate, and only reference model files
// that exist in the 1.20.61 data archive. Usage: dotnet run -- [path/to/datalist.txt]
using System.Buffers;
using System.Buffers.Binary;
using System.Collections;
using System.Reflection;
using Client.Main;
using Client.Main.Configuration;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Core.Client;
using Client.Main.Core.Models;
using Client.Main.Core.Utilities;
using Client.Main.Networking;
using Client.Main.Networking.PacketHandling;
using Client.Main.Networking.PacketHandling.Handlers;
using Client.Main.Objects;
using Microsoft.Extensions.Logging.Abstractions;

string dataList = args.Length > 0 ? args[0]
    : System.IO.Path.Combine(AppContext.BaseDirectory, "datalist-1.20.61.txt");
var dataFiles = new HashSet<string>(
    File.ReadLines(dataList).Select(l => l.Split('\t')[0].Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);

int failures = 0;
void Check(bool ok, string name, string detail = "")
{
    if (!ok) failures++;
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? " - " + detail : "")}");
}

ushort[] kalima = { 148, 174, 176, 177, 178, 179, 180, 182, 184, 185, 186, 187, 188, 190, 192, 193, 194, 195, 196,
    260, 262, 263, 264, 265, 266, 268, 270, 271, 272, 273, 274, 331, 332, 333, 334, 335, 337 };
ushort[] chaosCastle = { 162, 163, 164, 165, 166, 167, 168, 169, 170, 171, 172, 173, 426, 427 };
ushort[] others = { 434, 435, 436, 437, 438, 439, 440, 409, 410, 411, 412, 549, 550, 551, 553, 554, 555, 556, 314, 315, 316, 317 };
var all = kalima.Concat(chaosCastle).Concat(others).ToArray();

// Model loads resolve against an empty data folder: every BMDLoader.Prepare call is recorded but nothing loads.
Constants.DataPath = Path.Combine(Path.GetTempPath(), "MonsterSpawnChecks-empty-data");
Directory.CreateDirectory(Constants.DataPath);
var bmds = (IDictionary)typeof(BMDLoader).GetField("_bmds", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(BMDLoader.Instance)!;
await ItemDatabase.PreloadAsync();

string ToArchivePath(string key) =>
    "Data/" + (Path.IsPathRooted(key) ? Path.GetRelativePath(Constants.DataPath, key) : key).Replace('\\', '/');

foreach (var id in all)
{
    if (!NpcDatabase.TryGetNpcType(id, out var type))
    {
        Check(false, $"{id} registered in NpcDatabase");
        continue;
    }

    if (Activator.CreateInstance(type) is not WalkerObject obj)
    {
        Check(false, $"{id} {type.Name} instantiable as WalkerObject");
        continue;
    }

    lock (bmds) bmds.Clear();
    try { await obj.Load().WaitAsync(TimeSpan.FromSeconds(10)); }
    catch (Exception) { /* no graphics device / data: only the requested paths matter */ }

    List<string> paths;
    lock (bmds) paths = bmds.Keys.Cast<string>().Select(ToArchivePath).ToList();
    var missing = paths.Where(p => !dataFiles.Contains(p)).ToList();
    int minPaths = chaosCastle.Contains(id) ? 6 : 1; // CC: 5 armour parts + weapon(s); Player.bmd body is cached after the first
    Check(paths.Count >= minPaths && missing.Count == 0,
        $"{id} {NpcDatabase.GetNpcName(id)} -> {type.Name}",
        missing.Count > 0 ? "missing " + string.Join(", ", missing) : string.Join(", ", paths));
}

// Route AddNpcsToScope (C2 0x13) for a sample of the ids through the real PacketRouter.
var logs = NullLoggerFactory.Instance;
var settings = new MuOnlineSettings { ProtocolVersion = "Season6" };
var state = new CharacterState(logs) { Id = 0x0042, Name = "Tester", MapId = 24 };
var scope = new ScopeManager(logs, state);
var network = new NetworkManager(logs, settings, state, scope);
var router = (PacketRouter)typeof(NetworkManager).GetField("_packetRouter", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(network)!;
var dispatcher = (MainThreadDispatcher)typeof(MuGame).GetField("_mainThreadDispatcher", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
router.SetRoutingMode(false);

ushort[] sample = { 148, 334, 162, 427, 436, 409, 549, 556, 314 };
var body = new List<byte> { (byte)sample.Length };
for (int i = 0; i < sample.Length; i++)
{
    ushort netId = (ushort)(0x0100 + i);
    body.AddRange(new byte[] { (byte)(netId >> 8), (byte)netId, (byte)(sample[i] >> 8), (byte)sample[i], 100, 100, 100, 100, 0, 0 });
}
var packet = new byte[body.Count + 4];
packet[0] = 0xC2;
BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(1), (ushort)packet.Length);
packet[3] = 0x13;
body.CopyTo(packet, 4);
await router.RoutePacketAsync(new ReadOnlySequence<byte>(packet));
dispatcher.ProcessPending();

// ProcessNpcSpawnAsync turns each queued request into an object via NpcDatabase + Activator; do the same here
// (a real world and GPU are needed for the rest of the spawn).
var queue = (IEnumerable)typeof(ScopeHandler).GetField("_npcSpawnQueue", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
var queued = new Dictionary<ushort, ushort>();
foreach (var request in queue)
{
    var t = request.GetType();
    queued[(ushort)t.GetProperty("MaskedId")!.GetValue(request)!] = (ushort)t.GetProperty("Type")!.GetValue(request)!;
}

for (int i = 0; i < sample.Length; i++)
{
    ushort netId = (ushort)(0x0100 + i);
    bool scoped = scope.GetScopeObjectByMaskedId(netId) is NpcScopeObject npc && npc.TypeNumber == sample[i]
        && npc.Name == NpcDatabase.GetNpcName(sample[i]) && !npc.Name.StartsWith("Unknown");
    bool created = queued.TryGetValue(netId, out var queuedType) && queuedType == sample[i]
        && NpcDatabase.TryGetNpcType(queuedType, out var spawnType) && Activator.CreateInstance(spawnType) is WalkerObject;
    Check(scoped && created, $"13 scope packet spawns {sample[i]} {NpcDatabase.GetNpcName(sample[i])}");
}

Console.WriteLine(failures == 0 ? "All monster spawn checks passed." : $"{failures} monster spawn check(s) failed.");
return failures == 0 ? 0 : 1;
