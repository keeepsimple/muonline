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
    [NpcInfo(295, "Erohim")]
    public class Erohim : SoundMonsterObject
    {
        private static readonly Vector3 SkillTint = new(1f, 0.5f, 0.2f);
        protected override string DeathSoundPath => "Sound/w31/mELOdeath.wav";

        protected override string GetAttackSoundPath(int attackType)
        {
            if (attackType == 2)
                return "Sound/w31/mELOeff1.wav";

            return MuGame.Random.Next(2) == 0 ? "Sound/w31/mELOattack1.wav" : "Sound/w31/mELOattack2.wav";
        }

        public Erohim()
        {
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (!progress.CrossedTick(8))
                return;
            if (progress.Action == MonsterActionType.Attack1 && TryGetAttackTargetPosition(out Vector3 target))
                MonsterAttackModelEffect.SpawnBossStrike(this, target, SkillTint);
            else if (progress.Action == MonsterActionType.Attack2)
                MonsterAttackModelEffect.SpawnBossBurst(this, SkillTint);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster88.bmd");
            await base.Load();
        }
    }
}
