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
    /// Illusion Temple 213 Shield Burn (server: halves the target's shield): a blue shield flash on the
    /// target's chest that swells and fades while shards burst outward and fall. One-shot, ~20 frames.
    /// </summary>
    internal sealed class ShieldBurnEffect : SourceSkillEffectBase
    {
        private const string FlashTexturePath = "Effect/flare01.jpg";
        private const string ShardTexturePath = "Effect/Spark03.jpg";
        private const float FlashFrames = 16f;
        private const float ChestHeight = 100f;
        private const float Gravity = 1.5f;
        private const int ShardCount = 24;

        private struct Shard
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float Life;
        }

        private static readonly Vector3 ShieldColor = new(0.35f, 0.65f, 1f);
        private readonly Shard[] _shards = new Shard[ShardCount];
        private Texture2D? _flashTexture;
        private Texture2D? _shardTexture;
        private float _age;

        public ShieldBurnEffect(WalkerObject target)
            : base(target, 0f, 0f, ShardCount + 2)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _flashTexture = await LoadTexture(FlashTexturePath);
            _shardTexture = await LoadTexture(ShardTexturePath);
        }

        protected override void OnStart()
        {
            Vector3 chest = CasterPosition + Vector3.UnitZ * ChestHeight;
            for (int i = 0; i < _shards.Length; i++)
            {
                float a = MuGame.Random.Next(360) * MathF.PI / 180f;
                float speed = 6f + MuGame.Random.Next(60) * 0.1f;
                _shards[i] = new Shard
                {
                    Position = chest,
                    Velocity = new Vector3(MathF.Cos(a) * speed, MathF.Sin(a) * speed, 4f + MuGame.Random.Next(40) * 0.1f),
                    Life = 12f + MuGame.Random.Next(8),
                };
            }
        }

        protected override void OnTick(float factor)
        {
            _age += factor;
            for (int i = 0; i < _shards.Length; i++)
            {
                ref Shard s = ref _shards[i];
                if (s.Life <= 0f)
                    continue;
                s.Life -= factor;
                s.Position += s.Velocity * factor;
                s.Velocity.Z -= Gravity * factor;
            }

            Lights.Set(0, CasterPosition + Vector3.UnitZ * ChestHeight, ShieldColor, 260f,
                1.6f * EffectLights.Flash(_age, FlashFrames));
        }

        protected override bool HasActiveContent()
        {
            if (_age < FlashFrames)
                return true;
            for (int i = 0; i < _shards.Length; i++)
                if (_shards[i].Life > 0f)
                    return true;
            return false;
        }

        protected override void OnDraw()
        {
            if (_age < FlashFrames && Batch.Begin(_flashTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                float t = _age / FlashFrames;
                float half = 50f + 70f * t;
                Batch.AddBillboard(CasterPosition + Vector3.UnitZ * ChestHeight, half, half, t * 2f,
                    new Color(ShieldColor * (1f - t)));
                Batch.End();
            }

            if (Batch.Begin(_shardTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _shards.Length; i++)
                {
                    ref Shard s = ref _shards[i];
                    if (s.Life > 0f)
                        Batch.AddBillboard(s.Position, 9f, 9f, 0f, new Color(ShieldColor * MathF.Min(1f, s.Life / 8f)));
                }
                Batch.End();
            }
        }
    }
}
