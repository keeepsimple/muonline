// Offline audio checks: every Season 6 map (OpenMU VersionSeasonSix GameMapsInitializer) has a music track or is
// deliberately silent, and every "Music/..." path the client references exists, case-exact, in the 1.20.61 data.
using System.Text.RegularExpressions;
using Client.Main.Core.Utilities;
using Client.Main.Worlds;

// Data/Music of the official 1.20.61 client (datalist.txt), case-exact.
var musicFiles = new HashSet<string>(StringComparer.Ordinal)
{
    "Music/Acheron.mp3",
    "Music/Aida.mp3",
    "Music/ArenilTemple.mp3",
    "Music/AsherAida.mp3",
    "Music/atlans.mp3",
    "Music/BalgasBarrack.mp3",
    "Music/BalgasRefuge.mp3",
    "Music/BlazeKethotum.mp3",
    "Music/BlazeKethotum.ogg",
    "Music/BloodTarkan.mp3",
    "Music/castle.mp3",
    "Music/charge.mp3",
    "Music/Church.mp3",
    "Music/crywolf_back-03.ogg",
    "Music/crywolf_before-01.ogg",
    "Music/crywolf_ready-02.ogg",
    "Music/crywolf1st.mp3",
    "Music/cursedtempleplay.mp3",
    "Music/cursedtemplewait.mp3",
    "Music/Devias.mp3",
    "Music/devil_square_intro.mp3",
    "Music/devil_square_outro.mp3",
    "Music/DuelArena.mp3",
    "Music/Dungeon.mp3",
    "Music/elbeland.mp3",
    "Music/huntingground.mp3",
    "Music/icarus.mp3",
    "Music/iDoppelganger.mp3",
    "Music/IgnisVolcano.mp3",
    "Music/ImperialGuardianFort.mp3",
    "Music/kalima.mp3",
    "Music/kanturu_1st.mp3",
    "Music/kanturu_2nd.mp3",
    "Music/KanturuMayaBattle.mp3",
    "Music/KanturuNightmareBattle.mp3",
    "Music/KanturuTower.mp3",
    "Music/Karutan_A.mp3",
    "Music/Karutan_B.mp3",
    "Music/Karutan_Twisted.mp3",
    "Music/KuberaMine.mp3",
    "Music/LabyrinthOfDimension_1.MP3",
    "Music/LabyrinthOfDimension_2.MP3",
    "Music/lastend.mp3",
    "Music/login_theme.mp3",
    "Music/lose.mp3",
    "Music/lost_tower_a.mp3",
    "Music/lost_tower_b.mp3",
    "Music/main_theme.mp3",
    "Music/MuTheme.mp3",
    "Music/Nars01.mp3",
    "Music/Nars02.mp3",
    "Music/NixiesLake_2.mp3",
    "Music/Noria.mp3",
    "Music/OldKethotum.mp3",
    "Music/PK_Field.mp3",
    "Music/Pub.mp3",
    "Music/Raklion.mp3",
    "Music/Raklion_Hatchery.mp3",
    "Music/Santa_Village.mp3",
    "Music/ScorchedCanyon.mp3",
    "Music/SwampOfCalmness.mp3",
    "Music/SwampOfDarkness.mp3",
    "Music/SwampOfDarknessBossZone.mp3",
    "Music/tarkan.mp3",
    "Music/Urke_Clean_Area.mp3",
    "Music/Urke_Fire_Area.mp3",
    "Music/victory.mp3",
};

ushort[] season6Maps =
{
    0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29,
    30, 31, 32, 33, 34, 36, 37, 38, 39, 40, 41, 42, 45, 46, 47, 48, 49, 50, 51, 52, 53, 56, 57, 58, 62, 63, 64,
    65, 66, 67, 68, 69, 70, 71, 72, 79, 80, 81,
};

// Maps that deliberately have no map track.
var noMusic = new Dictionary<ushort, string>
{
    { 5, "Exile (unused)" }, { 6, "Arena" }, { 40, "Silent Map (GM)" }, { 79, "Loren Market (ambient only)" },
    { 11, "Blood Castle 1" }, { 12, "Blood Castle 2" }, { 13, "Blood Castle 3" }, { 14, "Blood Castle 4" },
    { 15, "Blood Castle 5" }, { 16, "Blood Castle 6" }, { 17, "Blood Castle 7" }, { 52, "Blood Castle 8" },
    { 18, "Chaos Castle 1" }, { 19, "Chaos Castle 2" }, { 20, "Chaos Castle 3" }, { 21, "Chaos Castle 4" },
    { 22, "Chaos Castle 5" }, { 23, "Chaos Castle 6" }, { 53, "Chaos Castle 7" },
};

int failures = 0;
void Check(bool ok, string name, string detail = "")
{
    if (!ok) failures++;
    if (!ok || detail.Length > 0) Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? " - " + detail : "")}");
}

foreach (var map in season6Maps)
{
    string? music = WorldAudioDatabase.GetMusic(map);
    bool silent = noMusic.ContainsKey(map);
    Check(music != null ^ silent, $"map {map}", music ?? (silent ? "no music: " + noMusic[map] : "MISSING"));
    if (music != null) Check(musicFiles.Contains(music), $"map {map} file exists", music);
}

// Every Music/ literal in the client sources.
var dir = new DirectoryInfo(AppContext.BaseDirectory);
while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Client.Main"))) dir = dir.Parent;
var src = Path.Combine(dir!.FullName, "Client.Main");
int refs = 0;
foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
{
    if (file.Contains("/obj/") || file.Contains("/bin/")) continue;
    foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"(Music/[^\"]+)\""))
    {
        refs++;
        if (!musicFiles.Contains(m.Groups[1].Value))
            Check(false, "path exists", $"{m.Groups[1].Value} in {Path.GetRelativePath(src, file)}");
    }
}
Check(refs > 30, "Music/ references scanned", refs.ToString());

// Lost Tower: floors 1-3 play lost_tower_a, floors 4-7 lost_tower_b (OpenMU spawn gates).
(int x, int y, int floor)[] spawns = { (208, 75, 1), (242, 237, 2), (86, 167, 3), (87, 87, 4), (129, 53, 5), (53, 53, 6), (8, 86, 7) };
foreach (var (x, y, floor) in spawns)
    Check(LostTowerWorld.MusicFor(x, y) == (floor >= 4 ? "Music/lost_tower_b.mp3" : "Music/lost_tower_a.mp3"), $"Lost Tower floor {floor}");

Console.WriteLine(failures == 0 ? "All audio checks passed." : $"{failures} audio check(s) failed.");
return failures == 0 ? 0 : 1;
