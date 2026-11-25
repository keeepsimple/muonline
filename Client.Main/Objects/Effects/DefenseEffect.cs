#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Defense (skill 18, PLAYER_DEFENSE1): a short blue-white flash on the caster's shield hand that follows the
    /// block pose, with one dynamic light. Single-pass and cheap.
    /// </summary>
    internal sealed class DefenseEffect : SourceSkillEffectBase
    {
        private const string ShineTexturePath = "Effect/Shiny02.jpg";
        private const string GlowTexturePath = "Effect/flare01.jpg";
        private const int ShieldBone = 42;            // left hand (c->Weapon[1].LinkBone)
        private const float ShieldFallbackHeight = 110f;
        private const float LifeFrames = 14f;
        private const float ShineScale = 0.9f;
        private const float GlowScale = 1.4f;
        private const float LightRadius = 220f;

        private static readonly Vector3 ShieldLight = new(0.55f, 0.75f, 1f);

        private Texture2D? _shineTexture;
        private Texture2D? _glowTexture;
        private Vector3 _position;
        private float _age;
        private float _rotation;

        public DefenseEffect(WalkerObject caster)
            : base(caster, 0f, 0f, 4)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _shineTexture = await LoadTexture(ShineTexturePath);
            _glowTexture = await LoadTexture(GlowTexturePath);
        }

        protected override void OnStart()
        {
            _rotation = MuGame.Random.Next(360);
            _position = GetCasterBonePosition(ShieldBone, ShieldFallbackHeight);
        }

        protected override void OnTick(float factor)
        {
            _age += factor;
            _rotation += 12f * factor;
            _position = GetCasterBonePosition(ShieldBone, ShieldFallbackHeight);
            Lights.Set(0, _position, ShieldLight, LightRadius, Intensity);
        }

        // Quick rise over the first 2 frames, then decays.
        private float Intensity => MathF.Min(1f, _age / 2f) * EffectLights.Flash(_age, LifeFrames, 1.2f);

        protected override bool HasActiveContent() => _age < LifeFrames;

        protected override void OnDraw()
        {
            float f = Intensity;
            if (f <= 0f)
                return;

            float grow = 0.7f + 0.5f * (_age / LifeFrames);
            if (Batch.Begin(_glowTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                Batch.AddSprite(_position, GlowScale * grow, ShieldLight * f);
                Batch.End();
            }

            if (Batch.Begin(_shineTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                Batch.AddSprite(_position, ShineScale * grow, Vector3.One * f, _rotation);
                Batch.AddSprite(_position, ShineScale * grow * 0.7f, ShieldLight * f, -_rotation);
                Batch.End();
            }
        }
    }
}
