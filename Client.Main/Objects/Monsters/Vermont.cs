using Client.Main.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(509, "Vermont")]
    public class Vermont : SoundMonsterObject
    {
        private static readonly Vector3 SkillTint = new(0.6f, 1f, 0.5f);
        private bool IsImperialGuardian => World?.MapId is >= 69 and <= 72;
        protected override string DeathSoundPath => IsImperialGuardian ? "Sound/w69w70w71w72/2Vermont_death.wav" : null;
        protected override string GetMoveSoundPath() => !IsImperialGuardian ? null : MuGame.Random.Next(2) == 0
            ? "Sound/w69w70w71w72/Jelint_move01.wav"
            : "Sound/w69w70w71w72/Jelint_move02.wav";
        protected override string GetAttackSoundPath(int attackType) => !IsImperialGuardian ? null : attackType == 2
            ? "Sound/w69w70w71w72/2Vermont_attack2.wav"
            : "Sound/w69w70w71w72/2Vermont_attack1.wav";

        public Vermont()
        {
            Scale = 1.3f;
        }

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
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster170.bmd");
            await base.Load();
        }
    }
}
