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
    [NpcInfo(344, "Balram")]
    public class Balram : SoundMonsterObject
    {
        private static readonly Vector3 SkillTint = new(1f, 0.3f, 0.2f);
        protected override string AttackSoundPath => "Sound/w35/balram_attack1.wav";
        protected override string DeathSoundPath => "Sound/w35/balram_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w35/balram_idle1.wav" : "Sound/w35/balram_idle2.wav";

        protected override string GetAttackSoundPath(int attackType) =>
            attackType == 2 ? "Sound/w35/balram_attack2.wav" : AttackSoundPath;

        public Balram()
        {
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            // Crywolf monster skill 9 (0x69, ATTACK2): removes the greater defense of its target.
            if (progress.SkillId == 9 && progress.CrossedTick(8) && TryGetAttackTargetPosition(out Vector3 target))
                MonsterAttackModelEffect.SpawnBossStrike(this, target, SkillTint);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster92.bmd");
            await base.Load();
        }
    }
}
