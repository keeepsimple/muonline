using Client.Main.Content;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using Client.Main.Models;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(508, "Destler")]
    public class Destler : SoundMonsterObject
    {
        private static readonly Vector3 SkillTint = new(0.3f, 0.5f, 1f);
        private GlowingEyesEffect _eyeGlow;

        protected override string GetAttackSoundPath(int attackType) => attackType switch
        {
            2 => "Sound/w69w70w71w72/1Deasuler_attack2.wav",
            3 => "Sound/w69w70w71w72/1Deasuler_attack3.wav",
            _ => null,
        };

        public Destler()
        {
            // Eyes: bones 14 (R), 15 (L). Secondary positions 71-74 are blade nodes, not eyes.
            _eyeGlow = new GlowingEyesEffect { LeftEyeBone = 15, RightEyeBone = 14, GlowColor = new Color(40, 100, 255) };
            Children.Add(_eyeGlow);
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            // Monster skills (0x69): ATTACK2 strikes the target, ATTACK3 bursts around the boss.
            if (progress.SkillId == 0 || !progress.CrossedTick(8))
                return;
            if (progress.Action == MonsterActionType.Attack2 && TryGetAttackTargetPosition(out Vector3 target))
                MonsterAttackModelEffect.SpawnBossStrike(this, target, SkillTint);
            else if (progress.Action == MonsterActionType.Attack3)
                MonsterAttackModelEffect.SpawnBossBurst(this, SkillTint);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster169.bmd");
            await base.Load();
        }
    }
}
