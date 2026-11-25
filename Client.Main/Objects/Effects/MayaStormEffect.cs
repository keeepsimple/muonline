#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Models;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Kanturu Maya wide-area attack "storm" (0xD1/0x06 type 0): the Maya tornado model spins over the
    /// target, a ring of smoke is pulled around it and lightning strikes the ground inside it.
    /// Follows the target while it moves; ends after <see cref="LifeFrames"/>.
    /// </summary>
    internal sealed class MayaStormEffect : SourceSkillEffectBase
    {
        internal const string TornadoModelPath = "Skill/mayatonedo.bmd";
        internal const string SmokeTexturePath = "Effect/smoke02.tga";
        internal const string ThunderTexturePath = "Effect/JointThunder01.jpg";
        internal const string SoundPath = "Sound/w39/Maya_Storm.wav";

        private const float LifeFrames = 75f;
        private const float FadeInFrames = 10f;
        private const float FadeOutFrames = 18f;
        private const int SmokeCount = 24;
        private const float SpinDegreesPerFrame = 14f;
        private static readonly Vector3 StormTint = new(0.45f, 0.6f, 1f);
        private static readonly Vector3 SmokeTint = new(0.32f, 0.38f, 0.5f);
        private static readonly Vector3 BoltTint = new(0.7f, 0.8f, 1f);

        private struct Smoke
        {
            public float Angle;
            public float Radius;
            public float Height;
            public float Rise;
            public float Scale;
        }

        private readonly Smoke[] _smoke = new Smoke[SmokeCount];
        private readonly SourceBoltPool _bolts = new(6);
        private readonly bool _playSound;
        private SourceModelRenderer? _tornado;
        private Texture2D? _smokeTexture;
        private Texture2D? _thunderTexture;
        private Vector3 _center;
        private float _age;
        private float _spin;
        private float _flash;
        private Vector3 _flashPosition;

        public MayaStormEffect(WalkerObject target, bool playSound = true)
            : base(target, triggerFrame: 0f, triggerTimeoutSeconds: 0f, maxQuads: SmokeCount + 6 * 10)
        {
            _playSound = playSound;
        }

        protected override async Task LoadEffectContent()
        {
            _smokeTexture = await LoadTexture(SmokeTexturePath);
            _thunderTexture = await LoadTexture(ThunderTexturePath);
            _tornado = await LoadModel(TornadoModelPath);
        }

        protected override void OnStart()
        {
            _center = CasterPosition;
            for (int i = 0; i < _smoke.Length; i++)
            {
                _smoke[i] = new Smoke
                {
                    Angle = i * (360f / SmokeCount),
                    Radius = 90f + MuGame.Random.Next(140),
                    Height = MuGame.Random.Next(260),
                    Rise = 3f + MuGame.Random.Next(5),
                    Scale = 0.9f + MuGame.Random.Next(8) * 0.1f,
                };
            }

            if (_playSound)
                SoundController.Instance.PlayBuffer(SoundPath);
        }

        protected override void OnTick(float factor)
        {
            _age += factor;
            _spin += SpinDegreesPerFrame * factor;
            if (Caster.Status != GameControlStatus.Disposed)
                _center = CasterPosition;

            for (int i = 0; i < _smoke.Length; i++)
            {
                ref Smoke s = ref _smoke[i];
                s.Angle += (10f + 900f / s.Radius) * factor;
                s.Height += s.Rise * factor;
                s.Radius = MathF.Max(40f, s.Radius - 0.6f * factor);
                if (s.Height > 420f)
                    s.Height -= 420f;
            }

            _bolts.Tick(factor);
            if (_age < LifeFrames - FadeOutFrames && FpsCheck(5, factor))
                Strike();

            _flash *= MathF.Pow(0.75f, factor);
            float intensity = Intensity;
            Lights.Set(0, _center + Vector3.UnitZ * 150f, StormTint, 420f, 0.8f * intensity);
            if (_flash > 0.02f)
                Lights.Set(1, _flashPosition, BoltTint, 380f, 1.8f * _flash);
            else
                Lights.Hide(1);
        }

        private void Strike()
        {
            float yaw = MuGame.Random.Next(360) * MathF.PI / 180f;
            float r = MuGame.Random.Next(160);
            Vector3 ground = _center + new Vector3(MathF.Cos(yaw) * r, MathF.Sin(yaw) * r, 0f);
            ground.Z = TerrainHeight(ground);
            Vector3 sky = ground + new Vector3(MuGame.Random.Next(200) - 100, MuGame.Random.Next(200) - 100, 650f);
            _bolts.Spawn(sky, ground, 36f, 6f, BoltTint, segments: 10);
            _flash = 1f;
            _flashPosition = ground + Vector3.UnitZ * 60f;
        }

        private float Intensity =>
            MathHelper.Clamp(_age / FadeInFrames, 0f, 1f) * MathHelper.Clamp((LifeFrames - _age) / FadeOutFrames, 0f, 1f);

        protected override bool HasActiveContent() => _age < LifeFrames || _bolts.HasActive;

        protected override void OnDraw()
        {
            float intensity = Intensity;
            if (intensity <= 0.01f && !_bolts.HasActive)
                return;

            float ground = TerrainHeight(_center);
            Vector3 foot = new(_center.X, _center.Y, ground);
            if (_tornado != null && intensity > 0.01f)
            {
                _tornado.Update(LastGameTime);
                float yaw = MathHelper.ToRadians(_spin);
                _tornado.DrawInstance(foot, new Vector3(0f, 0f, yaw), 1.1f, StormTint * intensity, intensity);
                _tornado.DrawInstance(foot, new Vector3(0f, 0f, -yaw * 0.7f), 0.75f, StormTint * 0.6f * intensity, intensity);
            }

            if (_smokeTexture != null && intensity > 0.01f &&
                Batch.Begin(_smokeTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _smoke.Length; i++)
                {
                    ref Smoke s = ref _smoke[i];
                    float a = MathHelper.ToRadians(s.Angle);
                    Vector3 p = foot + new Vector3(MathF.Cos(a) * s.Radius, MathF.Sin(a) * s.Radius, s.Height);
                    float fade = intensity * MathHelper.Clamp((420f - s.Height) / 120f, 0f, 1f);
                    Batch.AddSprite(p, s.Scale, SmokeTint * fade, s.Angle);
                }
                Batch.End();
            }

            if (_thunderTexture != null &&
                Batch.Begin(_thunderTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                _bolts.Draw(Batch);
                Batch.End();
            }
        }

        protected override void DisposeEffect() => _tornado?.Dispose();
    }
}
