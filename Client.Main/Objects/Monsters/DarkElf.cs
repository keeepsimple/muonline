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
    [NpcInfo(340, "Dark Elf")]
    public class DarkElf : SoundMonsterObject
    {
        private static readonly Vector3 SkillTint = new(0.6f, 0.3f, 1f);
        protected override string AttackSoundPath => "Sound/w35/darkelf_at1.wav";
        protected override string DeathSoundPath => "Sound/w35/darkelf_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w35/darkelf_idle1.wav" : "Sound/w35/darkelf_idle2.wav";

        protected override string GetAttackSoundPath(int attackType) => attackType switch
        {
            2 => "Sound/w35/darkelf_at2.wav",
            3 => "Sound/w35/darkelf_skill1.wav",
            4 => "Sound/w35/darkelf_skill2.wav",
            _ => AttackSoundPath
        };

        public DarkElf()
        {
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            // Crywolf monster skill 8 (0x69, ATTACK2): stuns its target.
            if (progress.SkillId == 8 && progress.CrossedTick(8) && TryGetAttackTargetPosition(out Vector3 target))
                MonsterAttackModelEffect.SpawnBossStrike(this, target, SkillTint);
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare($"Monster/Monster127.bmd");
            await base.Load();
        }
    }
}
