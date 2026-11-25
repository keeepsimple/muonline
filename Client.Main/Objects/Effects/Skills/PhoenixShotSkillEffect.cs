#nullable enable
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Objects.Effects.RageFighter;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects.Skills
{
    /// <summary>
    /// Factory for the Rage Fighter "Phoenix Shot" skill (ID 270). See <see cref="PhoenixShotEffect"/>.
    /// Local cast and 0x19 carry the target id; the 0x1E area packet of other players carries the target area centre.
    /// </summary>
    [SkillVisualEffect(270)]
    public sealed class PhoenixShotSkillEffect : ISkillVisualEffect
    {
        public WorldObject? CreateEffect(SkillEffectContext context)
        {
            if (context.Caster == null || context.World == null)
                return null;

            WalkerObject? target = null;
            ushort targetId = (ushort)(context.TargetId & 0x7FFF);
            if (targetId != 0 && targetId != (context.Caster.NetworkId & 0x7FFF) && context.World is WalkableWorldControl walkable)
                walkable.TryGetWalkerById(targetId, out target);

            return new PhoenixShotEffect(context.Caster, target, context.TargetPosition ?? Vector3.Zero);
        }
    }
}
