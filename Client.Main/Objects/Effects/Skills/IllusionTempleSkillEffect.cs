#nullable enable
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Illusion Temple special skills. Spawned from BF/02 (own successful cast) and, for the persistent orders,
    /// from the 0x07 status of effect 210/211 (every observer); BF/07 ends them via <see cref="IllusionTempleOrderEffect.End"/>.
    /// </summary>
    [SkillVisualEffect(IllusionTempleOrderEffect.ProtectionSkill)] // Order of Protection: ward on the caster
    [SkillVisualEffect(IllusionTempleOrderEffect.RestraintSkill)]  // Order of Restraint: bind on the target
    [SkillVisualEffect(212)] // Order of Tracking: teleport flash at origin and destination (TargetPosition)
    [SkillVisualEffect(213)] // Shield Burn: shield burst on the target
    public sealed class IllusionTempleSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (!SupportSkillTargets.IsUsable(context))
                return null;

            switch (context.SkillId)
            {
                case IllusionTempleOrderEffect.ProtectionSkill:
                    return Order(context.Caster, context.SkillId);
                case IllusionTempleOrderEffect.RestraintSkill:
                    return SupportSkillTargets.ResolveTarget(context) is { } bound ? Order(bound, context.SkillId) : null;
                case 212:
                    Vector3 origin = SupportSkillTargets.Feet(context.Caster);
                    if (context.TargetPosition is { } destination &&
                        Vector3.DistanceSquared(origin, destination) > Constants.TERRAIN_SCALE * Constants.TERRAIN_SCALE)
                        TeleportSparkEffect.Spawn(context.World, destination, context.Caster, playSound: false);
                    return new TeleportSparkEffect(context.Caster, origin, playSound: true);
                case 213:
                    return SupportSkillTargets.ResolveTarget(context) is { } shielded ? new ShieldBurnEffect(shielded) : null;
                default:
                    return null;
            }
        }

        // One order visual per owner: a repeated cast or status refreshes the live one.
        private static WorldObject Order(WalkerObject owner, ushort skill) =>
            IllusionTempleOrderEffect.FindAndRefresh(owner, skill) ?? new IllusionTempleOrderEffect(owner, skill);
    }
}
