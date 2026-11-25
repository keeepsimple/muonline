#nullable enable
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Doppelganger Self Explosion (239, monster 533's attack skill): the Explosion bombs burst around the monster.
    /// Spawned from the monster branch of the remote skill animation (0x19).
    /// </summary>
    [SkillVisualEffect(SkillId)]
    public sealed class DoppelgangerSelfExplosionSkillEffect : ISkillVisualEffect
    {
        public const ushort SkillId = 239;

        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (!SupportSkillTargets.IsUsable(context))
                return null;

            // DarkLordExplosionEffect scatters its bombs over +0..160 in X/Y; shift so they centre on the monster.
            return new DarkLordExplosionEffect(context.Caster, SupportSkillTargets.Feet(context.Caster) - new Vector3(80f, 80f, 0f));
        }
    }
}
