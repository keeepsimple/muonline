using System.Reflection;
using System.Buffers;
using Client.Data.BMD;
using Client.Main.Content;
using Client.Main.Networking.PacketHandling;
using Client.Main.Objects;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Effects.Skills;
using Client.Main.Objects.Player;
using Client.Main.Models;
using Microsoft.Xna.Framework;

int failures = 0;
void Check(bool ok, string name)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}");
    if (!ok) failures++;
}

Check(SkillDefinitions.GetSkillType(236) == SkillType.Area && SkillDefinitions.GetSkillAnimation(236) == 184,
    "Flame Strike uses the area cast and the original animation");
Check(string.IsNullOrEmpty(SkillDefinitions.GetSkillSound(236)), "Flame Strike sound is reserved for release");
foreach (int skillId in new[] { 230, 456 })
{
    Check(SkillDefinitions.GetSkillType(skillId) == SkillType.Area && SkillDefinitions.GetSkillAnimation(skillId) == 185,
        $"Lightning Shock {skillId} uses the area cast and original animation");
    Check(string.IsNullOrEmpty(SkillDefinitions.GetSkillSound(skillId)),
        $"Lightning Shock {skillId} sound is reserved for release");
}
var casterTileMethod = typeof(Client.Main.Scenes.GameScene).Assembly
    .GetType("Client.Main.Scenes.GameSceneSkillController")!
    .GetMethod("UsesCasterTileForAreaRequest", BindingFlags.Static | BindingFlags.NonPublic)!;
foreach (ushort skillId in new ushort[] { 230, 456, 8, 236, 76 })
    Check((bool)casterTileMethod.Invoke(null, new object[] { skillId })!, $"Area skill {skillId} sends the caster tile");
Check(!(bool)casterTileMethod.Invoke(null, new object[] { (ushort)38 })!, "Decay keeps its selected destination tile");
foreach (int skillId in new[] { 214, 458 })
    Check(SkillDefinitions.GetSkillType(skillId) == SkillType.Area && SkillDefinitions.GetSkillAnimation(skillId) == 168,
        $"Drain Life {skillId} uses a target-bearing area request and its original animation");
var drainTargetMethod = typeof(Client.Main.Scenes.GameScene).Assembly
    .GetType("Client.Main.Scenes.GameSceneSkillController")!
    .GetMethod("IsDrainLife", BindingFlags.Static | BindingFlags.NonPublic)!;
Check((bool)drainTargetMethod.Invoke(null, new object[] { (ushort)214 })! &&
      (bool)drainTargetMethod.Invoke(null, new object[] { (ushort)458 })! &&
      !(bool)drainTargetMethod.Invoke(null, new object[] { (ushort)38 })!,
    "Drain Life requires a selected target while ordinary area spells may target the ground");
var areaCastState = new Client.Main.Core.Client.CharacterState(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
{
    LastAreaSkillId = 214, LastAreaSkillTargetId = 0x42, LastAreaSkillSentAtMs = 1000
};
Check(areaCastState.GetRecentAreaSkillTarget(214, 1250) == 0x42 &&
      areaCastState.GetRecentAreaSkillTarget(458, 1250) == 0 &&
      areaCastState.GetRecentAreaSkillTarget(214, 2501) == 0 &&
      areaCastState.GetRecentAreaSkillTarget(214, 999) == 0,
    "Drain Life recovers only the recent target for the matching local cast");
Check(string.IsNullOrEmpty(SkillDefinitions.GetSkillSound(214)) && string.IsNullOrEmpty(SkillDefinitions.GetSkillSound(458)),
    "Drain Life sound is reserved for the timed effect release");
Type drainEffectType = typeof(PlayerObject).Assembly.GetType("Client.Main.Objects.Effects.Summoner.DrainLifeEffect")!;
var drainEffect = Activator.CreateInstance(drainEffectType, new object[] { new PlayerObject(), new PlayerObject() })!;
var drainFlags = BindingFlags.Instance | BindingFlags.NonPublic;
drainEffectType.GetMethod("SpawnEnergy", drainFlags)!.Invoke(drainEffect, new object[] { Vector3.Zero, Vector3.Zero });
var drainJoints = (Array)drainEffectType.GetField("s_joints", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
int drainJointIndex = -1;
for (int i = 0; i < drainJoints.Length; i++)
    if (ReferenceEquals(drainJoints.GetValue(i)!.GetType().GetField("Owner")!.GetValue(drainJoints.GetValue(i)), drainEffect)) { drainJointIndex = i; break; }
drainEffectType.GetMethod("MoveJoints", drainFlags)!.Invoke(drainEffect, new object[] { 1f, true });
var drainJoint = drainJoints.GetValue(drainJointIndex)!;
Type drainJointType = drainJoint.GetType();
Check((Vector3)drainJointType.GetField("Position")!.GetValue(drainJoint)! == new Vector3(0f, -3f, 6f) &&
      (float)drainJointType.GetField("Life")!.GetValue(drainJoint)! == 119f &&
      ((Vector3)drainJointType.GetField("Angle")!.GetValue(drainJoint)!).Z == 10f,
    "Drain Life energy first moves and rises before it starts homing");
drainEffectType.GetMethod("DisposeEffect", drainFlags)!.Invoke(drainEffect, null);
Check(drainJointType.GetField("Owner")!.GetValue(drainJoints.GetValue(drainJointIndex)) == null,
    "Drain Life releases its shared joint slot when the cast is disposed");
var shockCaster = new PlayerObject();
var shockWorld = (Client.Main.Controls.WalkableWorldControl)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Client.Main.Worlds.AcheronWorld));
var shock = new LightningShockSkillEffect().CreateEffect(new SkillEffectContext { Caster = shockCaster, World = shockWorld, SkillId = 230 })!;
Type shockType = shock.GetType();
var shockFlags = BindingFlags.Instance | BindingFlags.NonPublic;
var shockTick = shockType.GetMethod("OnTick", shockFlags)!;
shockType.GetField("_orbLife", shockFlags)!.SetValue(shock, 1f);
shockTick.Invoke(shock, new object[] { 1f });
Check(!(bool)shockType.GetField("_orbAlive", shockFlags)!.GetValue(shock)! &&
      (float)shockType.GetField("_impactLife", shockFlags)!.GetValue(shock)! == 0f,
    "Lightning Shock expires without creating a ground impact when no collision occurs");
var shockImpact = new LightningShockSkillEffect().CreateEffect(new SkillEffectContext { Caster = shockCaster, World = shockWorld, SkillId = 230 })!;
shockType.GetField("_orbAlive", shockFlags)!.SetValue(shockImpact, false);
shockType.GetMethod("StartImpact", shockFlags)!.Invoke(shockImpact, new object[] { 1f });
shockTick.Invoke(shockImpact, new object[] { 1f });
Check(Math.Abs((float)shockType.GetField("_markScale", shockFlags)!.GetValue(shockImpact)! - .95f) < .0001f &&
      Math.Abs((float)shockType.GetField("_flashScale", shockFlags)!.GetValue(shockImpact)! - .6f) < .0001f,
    "Lightning Shock impact marks and flashes grow according to their source lifetimes");
for (int i = 0; i < 80; i++) shockTick.Invoke(shockImpact, new object[] { 1f });
Check(!(bool)shockType.GetMethod("HasActiveContent", shockFlags)!.Invoke(shockImpact, null)!,
    "Lightning Shock impact and its particles eventually release the effect");
Check(SkillDefinitions.GetSkillType(16) == SkillType.Target, "Soul Barrier can target party members as well as self");
foreach (ushort target in new ushort[] { 0x42, 0x43 })
{
    var writer = new ArrayBufferWriter<byte>();
    int size = PacketBuilder.BuildSkillRequestPacket(writer, 16, target);
    writer.Advance(size);
    var packet = new MUnique.OpenMU.Network.Packets.ClientToServer.TargetedSkill(writer.WrittenSpan.ToArray());
    Check(packet.SkillId == 16 && packet.TargetId == target, $"Soul Barrier request retains target {target:X4}");
}
foreach (ushort target in new ushort[] { 0x42, 0x43 })
{
    var writer = new ArrayBufferWriter<byte>();
    int size = PacketBuilder.BuildSkillRequestPacket(writer, 71, target);
    writer.Advance(size);
    var packet = new MUnique.OpenMU.Network.Packets.ClientToServer.TargetedSkill(writer.WrittenSpan.ToArray());
    Check(packet.SkillId == 71 && packet.TargetId == target, $"Cancel Invisibility request retains target {target:X4}");
}
foreach (ushort target in new ushort[] { 0x42, 0x43 })
{
    var writer = new ArrayBufferWriter<byte>();
    int size = PacketBuilder.BuildSkillRequestPacket(writer, 72, target);
    writer.Advance(size);
    var packet = new MUnique.OpenMU.Network.Packets.ClientToServer.TargetedSkill(writer.WrittenSpan.ToArray());
    Check(packet.SkillId == 72 && packet.TargetId == target, $"Abolish Magic request retains target {target:X4}");
}
Vector3 rotated = SourceJointMath.Rotate(new Vector3(0, -1, 0), new Vector3(0, 0, 90));
Check(Vector3.Distance(rotated, Vector3.UnitX) < 0.0001f, "Source rotation sends forward to +X at yaw 90");

var fenrirCaster = new PlayerObject { NetworkId = 0x42 };
var fenrirIndex = typeof(PlayerObject).GetField("_currentVehicleIndex", BindingFlags.Instance | BindingFlags.NonPublic)!;
var ridingField = typeof(PlayerObject).GetField("_isRiding", BindingFlags.Instance | BindingFlags.NonPublic)!;
foreach (var (vehicleIndex, expectedAction) in new (short, PlayerAction)[]
{
    (7, PlayerAction.PlayerSkillDrainLifeUni), (8, PlayerAction.PlayerSkillDrainLifeDino),
    (14, PlayerAction.PlayerSkillDrainLifeFenrir)
})
{
    ridingField.SetValue(fenrirCaster, true);
    fenrirIndex.SetValue(fenrirCaster, vehicleIndex);
    Check(fenrirCaster.GetPacketSkillAnimation(214) == (int)expectedAction &&
          fenrirCaster.GetPacketSkillAnimation(458) == (int)expectedAction,
        $"Drain Life selects mount animation for vehicle {vehicleIndex}");
}
ridingField.SetValue(fenrirCaster, false);
var plasmaWorld = (Client.Main.Controls.WalkableWorldControl)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Client.Main.Worlds.AcheronWorld));
var plasmaFactory = new PlasmaStormSkillEffect();
foreach (var (vehicleIndex, expectedType) in new (short, int)[] { (11, 0), (12, 2), (13, 3), (14, 1), (15, 0), (16, 2), (17, 3), (18, 1) })
{
    fenrirIndex.SetValue(fenrirCaster, vehicleIndex);
    var plasma = plasmaFactory.CreateEffect(new SkillEffectContext
    {
        Caster = fenrirCaster, World = plasmaWorld, SkillId = 76, TargetId = 0, TargetPosition = Vector3.Zero
    })!;
    var plasmaType = plasma.GetType();
    Check(ReferenceEquals(plasmaType.GetField("_mainTarget", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plasma), fenrirCaster) &&
          (int)plasmaType.GetField("_fenrirType", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plasma)! == expectedType,
        $"Plasma Storm area packet uses the caster and Fenrir variant {vehicleIndex}");
}
fenrirIndex.SetValue(fenrirCaster, (short)14);
var plasmaPair = plasmaFactory.CreateEffect(new SkillEffectContext
{
    Caster = fenrirCaster, World = plasmaWorld, SkillId = 76, TargetPosition = Vector3.Zero
})!;
Type plasmaTypeForTest = plasmaPair.GetType();
plasmaTypeForTest.GetMethod("SpawnPair", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(plasmaPair, new object[] { fenrirCaster, 4 });
var plasmaJoints = (Array)plasmaTypeForTest.GetField("_joints", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plasmaPair)!;
object firstPlasmaJoint = plasmaJoints.GetValue(0)!;
object secondPlasmaJoint = plasmaJoints.GetValue(1)!;
Type plasmaJointType = firstPlasmaJoint.GetType();
Check((Vector3)plasmaJointType.GetField("Angle")!.GetValue(firstPlasmaJoint)! ==
      (Vector3)plasmaJointType.GetField("Angle")!.GetValue(secondPlasmaJoint)! &&
      (int)plasmaJointType.GetField("SubType")!.GetValue(firstPlasmaJoint)! == 1 &&
      (int)plasmaJointType.GetField("SubType")!.GetValue(secondPlasmaJoint)! == 4,
    "Plasma Storm creates each lightning pair with the same source angle and correct subtypes");
plasmaTypeForTest.GetMethod("SpawnCoil", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(plasmaPair, null);
object redCoil = plasmaJoints.GetValue(2)!;
float initialCoilWait = (float)plasmaJointType.GetField("Weapon")!.GetValue(redCoil)!;
object[] coilMoveArgs = { redCoil, 0.5f };
plasmaTypeForTest.GetMethod("MoveCoil", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, coilMoveArgs);
Check((float)plasmaJointType.GetField("Weapon")!.GetValue(coilMoveArgs[0])! == initialCoilWait - 0.5f,
    "Plasma Storm coil countdown follows the frame factor");
fenrirIndex.SetValue(fenrirCaster, (short)13);
var goldPlasma = plasmaFactory.CreateEffect(new SkillEffectContext
{
    Caster = fenrirCaster, World = plasmaWorld, SkillId = 76, TargetPosition = Vector3.Zero
})!;
plasmaTypeForTest.GetMethod("SpawnCoil", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(goldPlasma, null);
var goldJoints = (Array)plasmaTypeForTest.GetField("_joints", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(goldPlasma)!;
object goldJoint = goldJoints.GetValue(0)!;
Check((int)plasmaJointType.GetField("SubType")!.GetValue(goldJoint)! == 14 &&
      plasmaJointType.GetField("Trail")!.GetValue(goldJoint) == null &&
      (float)plasmaJointType.GetField("Life")!.GetValue(goldJoint)! == 15f,
    "gold Fenrir subtype 14 follows the source branch without inventing a spiral");
Check(fenrirCaster.GetSkillAction(76, false) == PlayerAction.PlayerFenrirSkill,
    "unarmed Plasma Storm uses the Fenrir skill animation");
fenrirCaster.CharacterClass = MUnique.OpenMU.Network.Packets.CharacterClassNumber.RageFighter;
Check(fenrirCaster.GetSkillAction(76, false) == PlayerAction.PlayerRageFenrir,
    "unarmed Rage Fighter uses its Fenrir skill animation");
fenrirCaster.Weapon2.Model = new BMD();
Check(fenrirCaster.GetSkillAction(76, false) == PlayerAction.PlayerRageFenrirOneRight,
    "Plasma Storm selects the right-hand Rage Fighter animation");
fenrirCaster.Weapon1.Model = new BMD();
Check(fenrirCaster.GetSkillAction(76, false) == PlayerAction.PlayerRageFenrirTwoSword,
    "Plasma Storm selects the two-hand Rage Fighter animation");

Assembly assembly = typeof(ModelObject).Assembly;
Type rendererType = assembly.GetType("Client.Main.Objects.Effects.Joints.SourceModelRenderer")!;
object renderer = Activator.CreateInstance(rendererType, "unused.bmd", -2, -1, true)!;
var setV = rendererType.GetMethod("SetMeshScrollV")!;
var getDeformer = rendererType.GetMethod("GetVertexDeformerForMesh", BindingFlags.Instance | BindingFlags.NonPublic)!;
setV.Invoke(renderer, new object[] { 1, 0.5f });
var mesh0 = (ITexCoordDeformer?)getDeformer.Invoke(renderer, new object[] { 0 });
var mesh1 = (ITexCoordDeformer?)getDeformer.Invoke(renderer, new object[] { 1 });
Check(mesh0 == null && mesh1 != null && mesh1.DeformTexCoord(0.1f, 0.2f) == new Vector2(0.1f, 0.7f),
    "mesh 1 animates vertically without shifting mesh 0");
rendererType.GetProperty("ScrollU")!.SetValue(renderer, 0.25f);
mesh0 = (ITexCoordDeformer?)getDeformer.Invoke(renderer, new object[] { 0 });
mesh1 = (ITexCoordDeformer?)getDeformer.Invoke(renderer, new object[] { 1 });
Check(mesh0 != null && mesh1 != null &&
      Vector2.Distance(mesh0.DeformTexCoord(0, 0), new Vector2(0.25f, 0)) < 0.0001f &&
      Vector2.Distance(mesh1.DeformTexCoord(0, 0), new Vector2(0.25f, 0.5f)) < 0.0001f,
    "shared U scroll still composes with mesh-specific V scroll");

Type blurType = assembly.GetType("Client.Main.Objects.Effects.Joints.SourceObjectBlur")!;
object blur = Activator.CreateInstance(blurType, 600)!;
var add = blurType.GetMethod("Add")!;
var move = blurType.GetMethod("Move")!;
var count = blurType.GetField("_count", BindingFlags.Instance | BindingFlags.NonPublic)!;
for (int i = 0; i < 610; i++) add.Invoke(blur, new object[] { Vector3.Zero, Vector3.One });
Check((int)count.GetValue(blur)! == 599, "object blur respects the original 599-tail limit");
for (int i = 0; i < 30; i++) move.Invoke(blur, null);
Check((int)count.GetValue(blur)! == 0, "object blur expires after 30 rendered frames");
add.Invoke(blur, new object[] { Vector3.Zero, Vector3.One });
Check((int)count.GetValue(blur)! == 1, "an expired blur can be reused for a new cast");
// Dark Knight basic weapon skills + Defense, including master strengtheners through the fallback table.
var knightWorld = (Client.Main.Controls.WalkableWorldControl)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Client.Main.Worlds.AcheronWorld));
var knightFlags = BindingFlags.Instance | BindingFlags.NonPublic;
foreach (var (skillId, motion) in new (ushort, string?)[]
{
    (18, null), (19, "FallingSlash"), (20, "Lunge"), (21, "Uppercut"), (22, "Cyclone"), (23, "Slash"),
    (326, "Cyclone"), (327, "Slash"), (328, "FallingSlash"), (329, "Lunge"), (479, "Cyclone")
})
{
    Check(SkillVisualEffectRegistry.HasEffect(skillId), $"Knight skill {skillId} has a visual");
    var knightCaster = new PlayerObject();
    bool spawned = SkillVisualEffectRegistry.TrySpawn(skillId,
        new SkillEffectContext { Caster = knightCaster, World = knightWorld, SkillId = skillId }, out var knightFx);
    if (!spawned || knightFx == null) { Check(false, $"Knight skill {skillId} creates its effect"); continue; }
    Type fxType = knightFx.GetType();
    if (motion != null)
        Check(fxType.GetProperty("SkillMotion", knightFlags)!.GetValue(knightFx)!.ToString() == motion,
            $"Knight skill {skillId} uses the {motion} motion");
    var hasContent = fxType.GetMethod("HasActiveContent", knightFlags)!;
    var tick = fxType.GetMethod("OnTick", knightFlags)!;
    fxType.GetMethod("OnStart", knightFlags)!.Invoke(knightFx, null);
    bool activeAtStart = (bool)hasContent.Invoke(knightFx, null)!;
    int ticks = 0;
    while (ticks < 300 && (bool)hasContent.Invoke(knightFx, null)!) { tick.Invoke(knightFx, new object[] { 1f }); ticks++; }
    Check(activeAtStart && ticks > 2 && ticks < 300, $"Knight skill {skillId} plays and finishes offline ({ticks} ticks)");
    knightFx.Dispose();
    Check(knightFx.Status == GameControlStatus.Disposed, $"Knight skill {skillId} effect disposes cleanly");
}
// Nova (58 charge, 40 release) and Phoenix Shot (270).
{
    Client.Main.Constants.SOUND_EFFECTS = false;
    var fxFlags = BindingFlags.Instance | BindingFlags.NonPublic;
    var fxWorld = (Client.Main.Controls.WalkableWorldControl)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Client.Main.Worlds.AcheronWorld));
    foreach (ushort id in new ushort[] { 40, 58, 270 })
        Check(SkillVisualEffectRegistry.HasEffect(id), $"skill {id} resolves to a visual effect");

    int RunToEnd(object fx, int max)
    {
        var type = fx.GetType();
        var has = type.GetMethod("HasActiveContent", fxFlags)!;
        var tick = type.GetMethod("OnTick", fxFlags)!;
        int n = 0;
        while (n < max && (bool)has.Invoke(fx, null)!) { tick.Invoke(fx, new object[] { 1f }); n++; }
        return n;
    }

    Type chargeType = assembly.GetType("Client.Main.Objects.Effects.NovaChargeEffect")!;
    var activeCharges = (System.Collections.IDictionary)chargeType.GetField("Active", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    object NewCharge(PlayerObject caster)
    {
        // GetOrCreate adds to a live world; register the same way without one.
        var fx = Activator.CreateInstance(chargeType, fxFlags, null, new object[] { caster }, null)!;
        activeCharges[caster.NetworkId] = fx;
        return fx;
    }

    // Charge -> stage update -> release (the 40 factory consumes the stage and ends the charge).
    var novaCaster = new PlayerObject { NetworkId = 0x51 };
    novaCaster.CurrentAction = PlayerAction.PlayerSkillHellBegin;
    var charge = NewCharge(novaCaster);
    var chargeTick = chargeType.GetMethod("OnTick", fxFlags)!;
    for (int i = 0; i < 30; i++) chargeTick.Invoke(charge, new object[] { 1f });
    chargeType.GetMethod("UpdateStage")!.Invoke(null, new object?[] { null, (ushort)0x51, (byte)5 });
    Check((bool)chargeType.GetMethod("HasActiveContent", fxFlags)!.Invoke(charge, null)! &&
          (byte)chargeType.GetProperty("Stage")!.GetValue(charge)! == 5,
        "Nova charge stays alive while charging and takes the server stage");
    novaCaster.CurrentAction = PlayerAction.PlayerSkillHellStart;
    var release = new NovaSkillEffect().CreateEffect(new SkillEffectContext { Caster = novaCaster, World = fxWorld, SkillId = 40 });
    Check(release != null && (byte)release.GetType().GetField("_stage", fxFlags)!.GetValue(release)! == 5 &&
          !activeCharges.Contains((ushort)0x51),
        "Nova release consumes the charge stage and unregisters the charge");
    Check(RunToEnd(charge, 200) < 200, "Nova charge visual ends after release");
    release!.GetType().GetMethod("OnStart", fxFlags)!.Invoke(release, null);
    int releaseTicks = RunToEnd(release, 400);
    Check(releaseTicks > 10 && releaseTicks < 400, $"Nova release shockwave plays and finishes offline ({releaseTicks} ticks)");
    release.Dispose();
    ((IDisposable)charge).Dispose();

    // Cancel: the caster leaves the charge action (death, safe zone) and no release ever arrives.
    var cancelCaster = new PlayerObject { NetworkId = 0x52 };
    cancelCaster.CurrentAction = PlayerAction.PlayerSkillHellBegin;
    var cancelled = NewCharge(cancelCaster);
    for (int i = 0; i < 5; i++) chargeTick.Invoke(cancelled, new object[] { 1f });
    cancelCaster.CurrentAction = PlayerAction.PlayerDie1;
    int cancelTicks = RunToEnd(cancelled, 200);
    Check(cancelTicks < 200 && !activeCharges.Contains((ushort)0x52), $"Nova charge is cleaned up when the cast is cancelled ({cancelTicks} ticks)");
    var lateRelease = new NovaSkillEffect().CreateEffect(new SkillEffectContext { Caster = cancelCaster, World = fxWorld, SkillId = 40 });
    Check(lateRelease != null && (byte)lateRelease.GetType().GetField("_stage", fxFlags)!.GetValue(lateRelease)! == 0,
        "Nova release after a cancelled charge still plays without a stale stage");
    lateRelease?.Dispose();
    ((IDisposable)cancelled).Dispose();

    // Phoenix Shot from the area packet (target area centre, no target id).
    var phoenixCaster = new PlayerObject { NetworkId = 0x53 };
    var phoenix = new PhoenixShotSkillEffect().CreateEffect(new SkillEffectContext
    {
        Caster = phoenixCaster, World = fxWorld, SkillId = 270, TargetPosition = new Vector3(300f, 0f, 0f)
    })!;
    Type phoenixType = phoenix.GetType();
    phoenixType.GetMethod("OnStart", fxFlags)!.Invoke(phoenix, null);
    var phoenixTick = phoenixType.GetMethod("OnTick", fxFlags)!;
    var impacted = phoenixType.GetProperty("Impacted", fxFlags)!;
    int flight = 0;
    while (flight < 50 && !(bool)impacted.GetValue(phoenix)!) { phoenixTick.Invoke(phoenix, new object[] { 1f }); flight++; }
    Vector3 phoenixEnd = (Vector3)phoenixType.GetField("_end", fxFlags)!.GetValue(phoenix)!;
    Check(flight > 5 && flight < 20 && Math.Abs(phoenixEnd.X - 300f) < 0.01f, $"Phoenix Shot flies to the target area ({flight} ticks)");
    int phoenixTicks = RunToEnd(phoenix, 300);
    Check(phoenixTicks > 5 && phoenixTicks < 300, $"Phoenix Shot fire impact plays and finishes offline ({phoenixTicks} ticks)");
    phoenix.Dispose();
    Check(phoenix.Status == GameControlStatus.Disposed, "Phoenix Shot effect disposes cleanly");
}
// Illusion Temple 210-213 and Doppelganger Self Explosion 239.
{
    var itWorld = (Client.Main.Controls.WalkableWorldControl)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Client.Main.Worlds.AcheronWorld));
    var itFlags = BindingFlags.Instance | BindingFlags.NonPublic;
    WorldObject? Spawn(ushort skillId, WalkerObject caster, ushort targetId = 0) =>
        SkillVisualEffectRegistry.TrySpawn(skillId, new SkillEffectContext { Caster = caster, TargetId = targetId, SkillId = skillId, World = itWorld }, out var fx) ? fx : null;
    bool Active(WorldObject fx) => (bool)fx.GetType().GetMethod("HasActiveContent", itFlags)!.Invoke(fx, null)!;
    int Run(WorldObject fx, int max)
    {
        var tick = fx.GetType().GetMethod("OnTick", itFlags)!;
        int n = 0;
        while (n < max && Active(fx)) { tick.Invoke(fx, new object[] { 1f }); n++; }
        return n;
    }
    void Start(WorldObject fx) => fx.GetType().GetMethod("OnStart", itFlags)!.Invoke(fx, null);

    foreach (ushort id in new ushort[] { 210, 211, 212, 213, 239 })
        Check(SkillVisualEffectRegistry.HasEffect(id), $"Skill {id} has a visual");

    var warded = new PlayerObject { NetworkId = 0x60 };
    var ward = Spawn(210, warded)!;
    Start(ward);
    Check(ward.GetType().Name == "IllusionTempleOrderEffect" && Run(ward, 100) == 100,
        "Order of Protection ward persists while the order lasts");
    Check(ReferenceEquals(Spawn(210, warded), ward), "a repeated Order of Protection refreshes the live ward");
    var endMethod = ward.GetType().GetMethod("End", BindingFlags.Static | BindingFlags.Public)!;
    endMethod.Invoke(null, new object[] { (ushort)0x60, (ushort)210 });
    int fadeTicks = Run(ward, 100);
    Check(fadeTicks > 0 && fadeTicks < 20 && !Active(ward), $"Order of Protection ward fades out after it ends ({fadeTicks} ticks)");
    ward.Dispose();
    var fresh = Spawn(210, warded);
    Check(fresh != null && !ReferenceEquals(fresh, ward), "a disposed ward is not reused");
    fresh?.Dispose();

    var bound = new PlayerObject { NetworkId = 0x61 };
    var bind = Spawn(211, warded, 0x61);
    Check(bind == null, "Order of Restraint needs a resolvable target");
    bind = Spawn(211, bound, 0x61)!;
    Start(bind);
    int bindTicks = Run(bind, 1000);
    Check(bindTicks > 450 && bindTicks < 600 && !Active(bind), $"Order of Restraint bind ends after its maximum lifetime ({bindTicks} ticks)");
    bind.Dispose();

    foreach (var (id, target) in new (ushort, ushort)[] { (212, 0), (213, 0x61) })
    {
        var fx = Spawn(id, bound, target);
        if (fx == null) { Check(false, $"Skill {id} creates its effect"); continue; }
        Start(fx);
        int ticks = Run(fx, 300);
        Check(ticks > 2 && ticks < 300, $"Skill {id} {fx.GetType().Name} plays and finishes offline ({ticks} ticks)");
        fx.Dispose();
        Check(fx.Status == GameControlStatus.Disposed, $"Skill {id} effect disposes cleanly");
    }

    var doppelganger = new Client.Main.Objects.Monsters.Doppleganger();
    var blast = Spawn(239, doppelganger);
    if (blast == null) Check(false, "Doppelganger Self Explosion creates its effect");
    else
    {
        Start(blast);
        int ticks = Run(blast, 300);
        Check(ticks > 2 && ticks < 300, $"Doppelganger Self Explosion bursts and finishes offline ({ticks} ticks)");
        blast.Dispose();
    }
}
// Master skills have no own animation entry and must reuse the replaced skill's swing.
Check(Client.Main.Core.Utilities.SkillDatabase.GetSkillAnimation(326) == Client.Main.Core.Utilities.SkillDatabase.GetSkillAnimation(22)
      && Client.Main.Core.Utilities.SkillDatabase.GetSkillAnimation(22) >= 0,
    "master Cyclone Strengthener uses the Cyclone animation");
Check(Client.Main.Core.Utilities.SkillDatabase.GetSkillAnimation(328) == Client.Main.Core.Utilities.SkillDatabase.GetSkillAnimation(19),
    "master Falling Slash Strengthener uses the Falling Slash animation");
// Kanturu Maya wide-area attack (D1/06) and the boss attack visuals.
{
    Client.Main.Constants.SOUND_EFFECTS = false;
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    var statics = BindingFlags.Static | BindingFlags.NonPublic;
    Type stormType = assembly.GetType("Client.Main.Objects.Effects.MayaStormEffect")!;
    Type rainType = assembly.GetType("Client.Main.Objects.Effects.MayaStoneRainEffect")!;

    foreach (Type type in new[] { stormType, rainType })
    {
        var hero = new PlayerObject { NetworkId = 0x70 };
        var fx = (WorldObject)Activator.CreateInstance(type, hero, false)!;
        var has = type.GetMethod("HasActiveContent", flags)!;
        var tick = type.GetMethod("OnTick", flags)!;
        type.GetMethod("OnStart", flags)!.Invoke(fx, null);
        bool activeAtStart = (bool)has.Invoke(fx, null)!;
        int ticks = 0;
        while (ticks < 400 && (bool)has.Invoke(fx, null)!) { tick.Invoke(fx, new object[] { 1f }); ticks++; }
        Check(activeAtStart && ticks > 20 && ticks < 400, $"{type.Name} plays and finishes offline ({ticks} ticks)");
        if (type == rainType)
            Check((int)type.GetField("_landed", flags)!.GetValue(fx)! == 14, "Maya stone rain lands every stone before it ends");
        fx.Dispose();
        Check(fx.Status == GameControlStatus.Disposed, $"{type.Name} disposes cleanly");
    }

    // D1/06 through the real PacketRouter: the storm / stone rain spawns on the local hero.
    var logs = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
    var settings = new Client.Main.Configuration.MuOnlineSettings { ProtocolVersion = "Season6" };
    var state = new Client.Main.Core.Client.CharacterState(logs) { Id = 0x0070, Name = "Tester" };
    var scope = new Client.Main.Core.Client.ScopeManager(logs, state);
    var network = new Client.Main.Networking.NetworkManager(logs, settings, state, scope);
    var router = (PacketRouter)typeof(Client.Main.Networking.NetworkManager)
        .GetField("_packetRouter", flags)!.GetValue(network)!;
    router.SetRoutingMode(false);
    var dispatcher = (Client.Main.Controllers.MainThreadDispatcher)typeof(Client.Main.MuGame)
        .GetField("_mainThreadDispatcher", statics)!.GetValue(null)!;

    var kanturu = (Client.Main.Controls.WalkableWorldControl)System.Runtime.CompilerServices.RuntimeHelpers
        .GetUninitializedObject(typeof(Client.Main.Worlds.KanturuWorld));
    var objects = Activator.CreateInstance(typeof(Client.Main.Helpers.ChildrenCollection<WorldObject>),
        BindingFlags.Instance | BindingFlags.NonPublic, null, new object?[] { null }, null)!;
    typeof(Client.Main.Controls.WorldControl).GetProperty("Objects")!.SetValue(kanturu, objects);
    kanturu.Walker = new PlayerObject { NetworkId = 0x70 };
    var game = (Client.Main.MuGame)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Client.Main.MuGame));
    var scene = (Client.Main.Scenes.BaseScene)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Client.Main.Scenes.GameScene));
    typeof(Client.Main.Scenes.BaseScene).GetProperty("World", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)!
        .SetValue(scene, kanturu);
    typeof(Client.Main.MuGame).GetProperty("ActiveScene")!.SetValue(game, scene);
    var instance = typeof(Client.Main.MuGame).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)!;
    instance.SetValue(null, game);
    try
    {
        foreach (var (type, kind) in new[] { (stormType, (byte)0), (rainType, (byte)1) })
        {
            await router.RoutePacketAsync(new ReadOnlySequence<byte>(new byte[] { 0xC1, 7, 0xD1, 0x06, 0, 0, kind }));
            dispatcher.ProcessPending();
            var spawned = kanturu.Objects.FirstOrDefault(o => o.GetType() == type);
            Check(spawned != null, $"D1/06 type {kind} spawns {type.Name} on the hero");
            if (spawned != null)
            {
                kanturu.Objects.Remove(spawned);
                spawned.Dispose();
            }
        }
    }
    finally
    {
        instance.SetValue(null, null);
    }

    var crywolfSkill = typeof(Client.Main.Networking.PacketHandling.Handlers.ScopeHandler)
        .GetMethod("ResolveCrywolfMonsterSkill", statics)!;
    Check(crywolfSkill.Invoke(null, new object[] { new Client.Main.Objects.Monsters.DarkElf(), (ushort)8 })!.ToString() == "Attack2" &&
          crywolfSkill.Invoke(null, new object[] { new Client.Main.Objects.Monsters.Balram(), (ushort)9 })!.ToString() == "Attack2",
        "Crywolf Dark Elf (8) and Balram (9) monster skills play ATTACK2");

    // Every asset the new visuals reference exists in the 1.20.61 data list.
    string dataList = Environment.GetEnvironmentVariable("MU_DATALIST") ??
        System.IO.Path.Combine(AppContext.BaseDirectory, "datalist-1.20.61.txt");
    if (!File.Exists(dataList))
    {
        Console.WriteLine($"[SKIP] boss visual assets: data list {dataList} not found");
    }
    else
    {
        var files = new HashSet<string>(File.ReadLines(dataList).Select(l => l.Split('\t')[0]), StringComparer.OrdinalIgnoreCase);
        string Const(Type t, string name) => (string)t.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(null)!;
        Type modelFx = assembly.GetType("Client.Main.Objects.Effects.MonsterAttackModelEffect")!;
        var assets = new List<string>
        {
            Const(stormType, "TornadoModelPath"), Const(stormType, "SmokeTexturePath"), Const(stormType, "ThunderTexturePath"),
            Const(stormType, "SoundPath"), Const(rainType, "FireModelPath"), Const(rainType, "DustTexturePath"),
            Const(rainType, "ImpactSoundPath"), Const(modelFx, "StrikeCrackModel"), Const(modelFx, "StrikeRingModel"),
            Const(modelFx, "BurstModel"), Const(modelFx, "BurstWaveModel"), "Skill/Stone01.bmd", "Skill/Stone02.bmd",
        };
        assets.AddRange((string[])rainType.GetField("StoneModelPaths", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!);
        static string ToData(string path) => "Data/" + Path.ChangeExtension(path, Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" => ".OZJ",
            ".tga" => ".OZT",
            var ext => ext,
        });
        var missing = assets.Where(a => !files.Contains(ToData(a))).ToList();
        Check(missing.Count == 0, $"boss visual assets exist in the data list ({assets.Count} checked)" +
            (missing.Count > 0 ? ": missing " + string.Join(", ", missing) : ""));
    }
}
return failures == 0 ? 0 : 1;
