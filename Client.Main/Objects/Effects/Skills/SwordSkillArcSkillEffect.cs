#nullable enable
using Client.Main.Core.Utilities;
using Motion = Client.Main.Objects.Effects.SwordSkillArcEffect.Motion;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the basic Dark Knight weapon skills. Strengtheners (326-329, 479) reach this factory through the
    /// master-skill fallback but keep their own id in <see cref="SkillEffectContext.SkillId"/>.
    /// </summary>
    [SkillVisualEffect(19)] // Falling Slash (AT_SKILL_SWORD1)
    [SkillVisualEffect(20)] // Lunge (AT_SKILL_SWORD2)
    [SkillVisualEffect(21)] // Uppercut (AT_SKILL_SWORD3)
    [SkillVisualEffect(22)] // Cyclone (AT_SKILL_SWORD4)
    [SkillVisualEffect(23)] // Slash (AT_SKILL_SWORD5)
    public sealed class SwordSkillArcSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            WalkerObject? target = null;
            if (context.TargetId != 0)
                context.World.TryGetWalkerById(context.TargetId, out target);

            Motion motion = context.SkillId switch
            {
                19 or 328 => Motion.FallingSlash,
                20 or 329 => Motion.Lunge,
                21 => Motion.Uppercut,
                22 or 326 or 479 => Motion.Cyclone,
                _ => Motion.Slash // 23, 327
            };

            return new SwordSkillArcEffect(context.Caster, target, motion);
        }
    }

    /// <summary>Factory for Defense (skill 18): shield flash on the caster.</summary>
    [SkillVisualEffect(18)] // Defense (AT_SKILL_BLOCKING)
    public sealed class DefenseSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context) =>
            context.Caster == null || context.World == null ? null : new DefenseEffect(context.Caster);
    }
}
