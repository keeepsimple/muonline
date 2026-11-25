using Client.Main.Content;
using System.Threading.Tasks;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(511, "Gallia")]
    public class Gallia : SoundMonsterObject
    {
        private static readonly Vector3 SkillTint = new(1f, 0.55f, 0.2f);
        private bool IsImperialGuardian => World?.MapId is >= 69 and <= 72;
        protected override string MoveSoundPath => IsImperialGuardian ? "Sound/w69w70w71w72/3Cato_move.wav" : null;
        protected override string DeathSoundPath => IsImperialGuardian ? "Sound/w69w70w71w72/2Vermont_death.wav" : null;
        protected override string GetAttackSoundPath(int attackType) => !IsImperialGuardian ? null : attackType == 1
            ? "Sound/w69w70w71w72/Jelint_attack1.wav"
            : "Sound/w69w70w71w72/4Gallia_attack2.wav";

        public Gallia() => Scale = 1.35f;

        public override void OnAppearAnimationStart()
        {
            base.OnAppearAnimationStart();
            if (IsImperialGuardian)
                PlayMonsterSound("Sound/w69w70w71w72/Jelint_rage.wav");
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
            Model = await BMDLoader.Instance.Prepare("Monster/Monster172.bmd");
            await base.Load();
        }
    }
}
