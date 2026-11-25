#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Kanturu Maya wide-area attack "stone rain" (0xD1/0x06 type 1): burning Maya rocks fall around the
    /// target and burst into debris and dust on the ground. The impact points are fixed at the start.
    /// </summary>
    internal sealed class MayaStoneRainEffect : SourceSkillEffectBase
    {
        internal static readonly string[] StoneModelPaths =
        {
            "Skill/mayastone01.bmd", "Skill/mayastone02.bmd", "Skill/mayastone03.bmd",
            "Skill/mayastone04.bmd", "Skill/mayastone05.bmd",
        };
        internal const string FireModelPath = "Skill/mayastonebluefire.bmd";
        internal const string DustTexturePath = "Effect/smoke02.tga";
        internal const string ImpactSoundPath = "Sound/w39/maya_hand_attack-01.wav";

        private const int StoneCount = 14;
        private const float Radius = 380f;
        private const float MaxDelayFrames = 30f;
        private const float StartSpeed = 30f;
        private const float Gravity = 4f;
        private const float ImpactFlashFrames = 8f;
        private static readonly Vector3 StoneLight = new(0.85f, 0.85f, 0.95f);
        private static readonly Vector3 FireTint = new(0.4f, 0.6f, 1f);
        private static readonly Vector3 DustTint = new(0.36f, 0.32f, 0.28f);

        private struct Stone
        {
            public Vector3 Ground;
            public float Height;
            public float Speed;
            public float Delay;
            public Vector3 Spin;
            public Vector3 Angle;
            public float Scale;
            public int Model;
            public bool Landed;
        }

        private readonly Stone[] _stones = new Stone[StoneCount];
        private readonly SourceModelRenderer?[] _models = new SourceModelRenderer?[5];
        private readonly SourceStonePool _debris = new(StoneCount * 3, 0);
        private readonly SourceParticlePool _dust = new(StoneCount * 4);
        private readonly bool _playSound;
        private SourceModelRenderer? _fire;
        private Texture2D? _dustTexture;
        private float _age;
        private int _landed;
        private float _impactAge = ImpactFlashFrames;
        private Vector3 _impactPosition;

        public MayaStoneRainEffect(WalkerObject target, bool playSound = true)
            : base(target, triggerFrame: 0f, triggerTimeoutSeconds: 0f, maxQuads: StoneCount * 4)
        {
            _playSound = playSound;
        }

        protected override async Task LoadEffectContent()
        {
            _dustTexture = await LoadTexture(DustTexturePath);
            for (int i = 0; i < _models.Length; i++)
            {
                var model = await LoadModel(StoneModelPaths[i], -1);
                model?.UseNormalBlend();
                _models[i] = model;
            }
            _fire = await LoadModel(FireModelPath);
            await _debris.LoadAsync(World);
        }

        protected override void OnStart()
        {
            Vector3 center = CasterPosition;
            for (int i = 0; i < _stones.Length; i++)
            {
                float yaw = MuGame.Random.Next(360) * MathF.PI / 180f;
                // The first stone always lands on the target, the rest spread over the area.
                float r = i == 0 ? 0f : Radius * MathF.Sqrt((float)MuGame.Random.NextDouble());
                Vector3 ground = center + new Vector3(MathF.Cos(yaw) * r, MathF.Sin(yaw) * r, 0f);
                ground.Z = TerrainHeight(ground);
                _stones[i] = new Stone
                {
                    Ground = ground,
                    Height = 900f + MuGame.Random.Next(300),
                    Speed = StartSpeed,
                    Delay = i == 0 ? 0f : (float)MuGame.Random.NextDouble() * MaxDelayFrames,
                    Spin = new Vector3(MuGame.Random.Next(10) - 5, MuGame.Random.Next(10) - 5, 0f) * 0.02f,
                    Angle = new Vector3(0f, 0f, yaw),
                    Scale = 0.8f + MuGame.Random.Next(5) * 0.1f,
                    Model = MuGame.Random.Next(_models.Length),
                };
            }
        }

        protected override void OnTick(float factor)
        {
            _age += factor;
            _impactAge += factor;
            for (int i = 0; i < _stones.Length; i++)
            {
                ref Stone s = ref _stones[i];
                if (s.Landed || _age < s.Delay)
                    continue;

                s.Height -= s.Speed * factor;
                s.Speed += Gravity * factor;
                s.Angle += s.Spin * factor;
                if (s.Height <= 0f)
                    Land(ref s);
            }

            _debris.Move(factor, World?.Terrain);
            _dust.Tick(factor);

            if (_impactAge < ImpactFlashFrames)
                Lights.Set(0, _impactPosition, FireTint, 360f, 1.6f * EffectLights.Flash(_impactAge, ImpactFlashFrames));
            else
                Lights.Hide(0);
        }

        private void Land(ref Stone s)
        {
            s.Landed = true;
            _landed++;
            _impactAge = 0f;
            _impactPosition = s.Ground + Vector3.UnitZ * 40f;

            for (int k = 0; k < 3; k++)
                _debris.Spawn(s.Ground + Vector3.UnitZ * 10f, new Vector3(0f, 0f, MuGame.Random.Next(360)), StoneLight, 10);
            for (int k = 0; k < 4; k++)
            {
                float a = (k * 90f + MuGame.Random.Next(45)) * MathF.PI / 180f;
                var velocity = new Vector3(MathF.Cos(a) * 3f, MathF.Sin(a) * 3f, 1.5f);
                _dust.Spawn(s.Ground + Vector3.UnitZ * 30f, velocity, 0.9f, DustTint, 24f, scaleGrowth: 0.06f);
            }

            // One impact sound for the first stone and every fourth after it keeps the rain audible without stacking.
            if (_playSound && _landed % 4 == 1)
                SoundController.Instance.PlayBuffer(ImpactSoundPath);
        }

        protected override bool HasActiveContent() => _landed < _stones.Length || _debris.HasLive || _dust.HasActive;

        protected override void OnDraw()
        {
            for (int i = 0; i < _stones.Length; i++)
            {
                ref Stone s = ref _stones[i];
                if (s.Landed || _age < s.Delay)
                    continue;

                Vector3 position = s.Ground + Vector3.UnitZ * s.Height;
                _models[s.Model]?.DrawInstance(position, s.Angle, s.Scale, StoneLight, 1f);
                _fire?.DrawInstance(position, new Vector3(0f, 0f, s.Angle.Z), s.Scale, FireTint, 1f);
            }

            _debris.DrawModels(LastGameTime, World?.Terrain);

            if (_dustTexture != null && _dust.HasActive &&
                Batch.Begin(_dustTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                _dust.Draw(Batch);
                Batch.End();
            }
        }

        protected override void DisposeEffect()
        {
            for (int i = 0; i < _models.Length; i++)
                _models[i]?.Dispose();
            _fire?.Dispose();
            _debris.Dispose();
        }
    }
}
