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
    [NpcInfo(510, "Kato")]
    internal class Kato : SoundMonsterObject
    {
        private static readonly Vector3 SkillTint = new(1f, 0.35f, 0.25f);
        protected override string MoveSoundPath => World?.MapId == 71 ? "Sound/w69w70w71w72/3Cato_move.wav" : null;
        protected override string DeathSoundPath => World?.MapId == 71 ? "Sound/w69w70w71w72/2Vermont_death.wav" : null;
        protected override string GetAttackSoundPath(int attackType) => World?.MapId != 71 ? null : attackType == 1
            ? "Sound/w69w70w71w72/CombatMaster_attack1.wav"
            : "Sound/w69w70w71w72/3Cato_attack2.wav";

        public Kato()
        {
            Scale = 1.35f;
        }

        public override void OnAppearAnimationStart()
        {
            base.OnAppearAnimationStart();
            if (World?.MapId == 71)
                PlayMonsterSound("Sound/w69w70w71w72/Raymond_rage.wav");
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
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster171.bmd");
            await base.Load();
        }
    }
}
