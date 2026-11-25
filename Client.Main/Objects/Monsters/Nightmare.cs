using Client.Main.Content;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Monsters
{
    [NpcInfo(361, "Nightmare")]
    public class Nightmare : SoundMonsterObject
    {
        internal const ushort DecaySkill = 38;
        internal const ushort SpecialAttackSkill = 14; // KanturuNightmareDefinition.SpecialAttackSkillNumber (Inferno)
        protected override bool VaryBasicAttack => World?.MapId != 39;
        protected override string AttackSoundPath => "Sound/w39/nightmare_attack-01.wav";
        protected override string DeathSoundPath => "Sound/w39/nightmare_death.wav";

        protected override string GetMoveSoundPath() =>
            MuGame.Random.Next(2) == 0 ? "Sound/w39/nightmare_idle-01.wav" : "Sound/w39/nightmare_idle-02.wav";

        protected override string GetAttackSoundPath(int attackType) => attackType switch
        {
            2 => "Sound/w39/nightmare_skill-01.wav",
            3 => "Sound/w39/nightmare_skill-02.wav",
            4 => "Sound/w39/nightmare_skill-03.wav",
            _ => AttackSoundPath
        };

        public Nightmare()
        {
            Scale = 1.6f;
        }

        public override async Task Load()
        {
            Model = await BMDLoader.Instance.Prepare("Monster/Monster122.bmd");
            await base.Load();
        }

        internal void PlayTeleport(bool arriving)
        {
            if (World?.MapId != 39 || IsDead)
                return;

            CancelAttack();
            TeleportSparkEffect.Spawn(World, Position, this, playSound: false);
            PlayAttackSound("Sound/w39/nightmare_tele.wav");
            Alpha = arriving ? 1f : 0f;
        }

        public override void OnPerformAttack(int attackType = 1)
        {
            Alpha = 1f;
            base.OnPerformAttack(attackType);
        }

        protected override void OnAttackProgress(in AttackProgress progress)
        {
            if (World == null)
                return;

            // OpenMU's Kanturu Nightmare attacks with Decay (0x18 + paired 0x19): poison fire falls on the target.
            if (progress.SkillStarted && progress.SkillId == DecaySkill && TryGetAttackTargetPosition(out Vector3 target))
            {
                var decay = new DecayEffect(this, target);
                World.Objects.Add(decay);
                _ = decay.Load();
            }

            if (progress.Action == MonsterActionType.Attack3)
            {
                // GM_Kanturu_3rd emits on successive ticks from 14 onward. Two pulses
                // keep that rhythm without stacking a new model every game tick.
                for (int tick = 14; tick <= 19; tick += 5)
                {
                    if (progress.CrossedTick(tick))
                        MonsterAttackModelEffect.Spawn(World, "Skill/nightmaresum.bmd", Position,
                            Angle + Vector3.UnitZ * (tick == 14 ? 0f : MathHelper.PiOver2),
                            new Vector3(0.65f, 0.36f, 0.18f), 1.25f, 1.2f, endScale: 1.5f);
                }
            }
            else if (progress.Action == MonsterActionType.Attack4)
            {
                if (progress.CrossedTick(10))
                    InfernoEffect.SpawnMonsterAttack(this);

                for (int tick = 10; tick <= 14; tick += 4)
                {
                    if (progress.CrossedTick(tick))
                        MonsterAttackModelEffect.Spawn(World, "Skill/Circle01.bmd", Position + Vector3.UnitZ * 3f,
                            Angle, new Vector3(1f, 0.65f, 0.32f), 0.9f, 1f, endScale: 1.6f, animated: true);
                }
            }
        }
    }
}
