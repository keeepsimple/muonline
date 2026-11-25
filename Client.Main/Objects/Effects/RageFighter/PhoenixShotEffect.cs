#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Models;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.RageFighter
{
    /// <summary>
    /// Phoenix Shot (skill 270, PLAYER_SKILL_PHOENIX_SHOT): a flaming phoenix (fire core, two flapping flame wing
    /// ribbons, a tail and falling embers) flies from the caster's fists to the target, then bursts into a fire
    /// explosion: flash, expanding scorched shock ring on the ground and a spray of flames (the server hits every
    /// enemy within 2 tiles of the target, see OpenMU PhoenixShotSkillPlugIn).
    /// </summary>
    internal sealed class PhoenixShotEffect : SourceSkillEffectBase
    {
        private const string FireTexturePath = "Effect/Fire01.jpg";
        private const string FlareTexturePath = "Effect/flare01.jpg";
        private const string TrailTexturePath = "Effect/JointSpirit01.jpg";
        private const string RingTexturePath = "Effect/ShockWave.jpg";
        private const string LaunchSoundPath = "Sound/mPhoenixAttack1.wav";
        private const string ImpactSoundPath = "Sound/eFirebustBoom.wav";

        private const float TriggerFrame = 3f;             // fists thrust forward
        private const float TriggerTimeoutSeconds = 0.35f;
        private const float FlightFrames = 11f;            // 25 Hz frames from the caster to the target
        private const float ArcHeight = 70f;
        private const float WingSpan = 95f;
        private const float WingWidth = 34f;
        private const float TailWidth = 46f;
        private const float FadeFrames = 10f;              // trails collapse into the impact point
        private const float FlashFrames = 9f;
        private const float RingFrames = 18f;
        private const int BurstCount = 30;
        private const float MaxDistance = 900f;

        private static readonly Vector3 FireColor = new(1f, 0.55f, 0.18f);
        private static readonly Vector3 CoreColor = new(1f, 0.85f, 0.5f);
        private static readonly Vector3 WingColor = new(1f, 0.45f, 0.1f);
        private static readonly Vector3 LightColor = new(1f, 0.55f, 0.2f);

        private struct Ember
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Velocity;
            public float Scale;
            public float Rotation;
            public float LifeTime;
            public float MaxLife;
        }

        private readonly WalkerObject? _target;
        private readonly Vector3 _fallbackTarget;
        private readonly Ember[] _embers = new Ember[96];
        private SourceTrail? _leftWing;
        private SourceTrail? _rightWing;
        private SourceTrail? _tail;
        private Texture2D? _fireTexture;
        private Texture2D? _flareTexture;
        private Texture2D? _trailTexture;
        private Texture2D? _ringTexture;

        private Vector3 _start;
        private Vector3 _end;
        private Vector3 _head;
        private float _age;
        private float _impactAge = -1f;
        private float _fade = 1f;

        /// <param name="target">Live target to home on (may be null).</param>
        /// <param name="fallbackTarget">Impact point when there is no live target (area packet centre).</param>
        public PhoenixShotEffect(WalkerObject caster, WalkerObject? target, Vector3 fallbackTarget)
            : base(caster, TriggerFrame, TriggerTimeoutSeconds, 1024)
        {
            _target = target;
            _fallbackTarget = fallbackTarget;
        }

        internal bool Impacted => _impactAge >= 0f;

        protected override async Task LoadEffectContent()
        {
            _fireTexture = await LoadTexture(FireTexturePath);
            _flareTexture = await LoadTexture(FlareTexturePath);
            _trailTexture = await LoadTexture(TrailTexturePath);
            _ringTexture = await LoadTexture(RingTexturePath);
        }

        protected override void OnStart()
        {
            _start = CasterPosition + CasterForward * 50f + new Vector3(0f, 0f, 110f);
            _end = ResolveTarget();
            _head = _start;

            int maxTails = SourceJointMath.ScaledMaxTails(8);
            _leftWing = new SourceTrail(maxTails);
            _rightWing = new SourceTrail(maxTails);
            _tail = new SourceTrail(SourceJointMath.ScaledMaxTails(10));

            PlayUnmappedSound(270, LaunchSoundPath);
        }

        private Vector3 ResolveTarget()
        {
            if (TargetAlive)
                return _target!.WorldPosition.Translation + new Vector3(0f, 0f, 90f);

            Vector3 caster = CasterPosition;
            Vector3 end = _fallbackTarget;
            Vector2 flat = new(end.X - caster.X, end.Y - caster.Y);
            if (end == Vector3.Zero || flat.LengthSquared() < 1f || flat.Length() > MaxDistance)
            {
                // No usable target: shoot 3 tiles ahead of the caster.
                end = caster + CasterForward * 300f;
                end.Z = TerrainHeight(end);
            }

            end.Z += 90f;
            return end;
        }

        private Vector3 CasterForward => SourceJointMath.Rotate(new Vector3(0f, -1f, 0f), new Vector3(0f, 0f, CasterYawDegrees));

        private bool TargetAlive => _target != null && _target.Status != GameControlStatus.Disposed && _target.World != null;

        protected override void OnTick(float factor)
        {
            _age += factor;

            if (!Impacted)
            {
                if (TargetAlive)
                    _end = _target!.WorldPosition.Translation + new Vector3(0f, 0f, 90f);

                float t = MathF.Min(1f, _age / FlightFrames);
                _head = Vector3.Lerp(_start, _end, t);
                _head.Z += MathF.Sin(t * MathF.PI) * ArcHeight;
                PushTrails(t);
                EmitEmbers(factor);
                if (t >= 1f)
                    Impact();
            }
            else
            {
                _impactAge += factor;
                _fade = MathF.Max(0f, 1f - _impactAge / FadeFrames);
                if (_fade > 0f)
                    PushTrails(1f);
            }

            TickEmbers(factor);
            UpdateLights();
        }

        private void PushTrails(float t)
        {
            Vector3 forward = _end - _start;
            if (forward.LengthSquared() < 1f)
                forward = Vector3.UnitX;
            forward.Normalize();
            Vector3 side = Vector3.Cross(forward, Vector3.UnitZ);
            if (side.LengthSquared() < 0.001f)
                side = Vector3.UnitY;
            side.Normalize();
            Vector3 up = Vector3.Cross(side, forward);

            // Wings beat about twice per flight and fold in when the bird hits.
            float flap = MathF.Sin(_age * 1.1f);
            float span = WingSpan * (0.65f + 0.35f * MathF.Cos(_age * 1.1f)) * _fade * MathF.Min(1f, 0.3f + t * 2f);
            Vector3 root = _head - forward * 25f;
            Vector3 lift = up * (flap * span * 0.55f);
            _leftWing!.Push(root + side * span + lift, forward, up, WingWidth);
            _rightWing!.Push(root - side * span + lift, forward, up, WingWidth);
            _tail!.Push(_head, side, up, TailWidth * MathF.Max(0.2f, _fade));
        }

        private void Impact()
        {
            _impactAge = 0f;
            _head = _end;
            SoundController.Instance.PlayBuffer(ImpactSoundPath);

            Vector3 ground = _end;
            ground.Z = TerrainHeight(_end) + 30f;
            for (int i = 0; i < BurstCount; i++)
            {
                float yaw = MathHelper.TwoPi * i / BurstCount + (float)MuGame.Random.NextDouble() * 0.2f;
                float speed = 9f + MuGame.Random.Next(9);
                var velocity = new Vector3(MathF.Cos(yaw) * speed, MathF.Sin(yaw) * speed, 4f + MuGame.Random.Next(8));
                SpawnEmber(ground + new Vector3(0f, 0f, MuGame.Random.Next(60)), velocity, 1.4f + MuGame.Random.Next(8) * 0.1f, 16f + MuGame.Random.Next(10));
            }
        }

        private void EmitEmbers(float factor)
        {
            // About two embers per 25 Hz frame shed from the body and wing roots.
            int count = FpsCheck(1, factor) ? 2 : 0;
            for (int i = 0; i < count; i++)
            {
                var jitter = new Vector3(MuGame.Random.Next(41) - 20, MuGame.Random.Next(41) - 20, MuGame.Random.Next(41) - 20);
                var velocity = new Vector3((MuGame.Random.Next(5) - 2) * 0.5f, (MuGame.Random.Next(5) - 2) * 0.5f, -1.5f);
                SpawnEmber(_head + jitter, velocity, 0.7f + MuGame.Random.Next(5) * 0.1f, 10f + MuGame.Random.Next(6));
            }
        }

        private void SpawnEmber(Vector3 position, Vector3 velocity, float scale, float lifeTime)
        {
            for (int i = 0; i < _embers.Length; i++)
            {
                ref Ember e = ref _embers[i];
                if (e.Live)
                    continue;

                e.Live = true;
                e.Position = position;
                e.Velocity = velocity;
                e.Scale = scale;
                e.Rotation = MuGame.Random.Next(360);
                e.LifeTime = lifeTime;
                e.MaxLife = lifeTime;
                return;
            }
        }

        private void TickEmbers(float factor)
        {
            float drag = MathF.Pow(0.88f, factor);
            for (int i = 0; i < _embers.Length; i++)
            {
                ref Ember e = ref _embers[i];
                if (!e.Live)
                    continue;

                e.LifeTime -= factor;
                if (e.LifeTime <= 0f)
                {
                    e.Live = false;
                    continue;
                }

                e.Position += e.Velocity * factor;
                e.Velocity.X *= drag;
                e.Velocity.Y *= drag;
                e.Velocity.Z += 0.6f * factor; // flames rise
                e.Rotation += 6f * factor;
            }
        }

        private void UpdateLights()
        {
            if (!Impacted)
            {
                Lights.Set(0, _head, LightColor, 220f, 1.2f);
                return;
            }

            Lights.Hide(0);
            float flash = EffectLights.Flash(_impactAge, RingFrames + 6f, 1.4f);
            if (flash > 0f)
                Lights.Set(1, _end, LightColor, 300f + 160f * (1f - flash), 2f * flash);
            else
                Lights.Hide(1);
        }

        protected override bool HasActiveContent()
        {
            if (!Impacted || _impactAge < RingFrames || _fade > 0f)
                return true;
            for (int i = 0; i < _embers.Length; i++) if (_embers[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            if (_impactAge >= 0f && _impactAge < RingFrames &&
                Batch.Begin(_ringTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                float k = _impactAge / RingFrames;
                float size = 1f + 4.5f * k;
                Batch.AddTerrainDecal(World?.Terrain, _end.X, _end.Y, size, size, FireColor * (1f - k) * 1.5f, _impactAge * 4f);
                Batch.End();
            }

            if (_fade > 0f && Batch.Begin(_trailTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                Vector3 wing = WingColor * _fade;
                if (_leftWing != null)
                    Batch.AddTrail(_leftWing, SourceQuadBatch.TrailFaces.Both, wing, SourceQuadBatch.TrailShade.JointForce, _leftWing.MaxTails - 1);
                if (_rightWing != null)
                    Batch.AddTrail(_rightWing, SourceQuadBatch.TrailFaces.Both, wing, SourceQuadBatch.TrailShade.JointForce, _rightWing.MaxTails - 1);
                if (_tail != null)
                    Batch.AddTrail(_tail, SourceQuadBatch.TrailFaces.Both, FireColor * _fade, SourceQuadBatch.TrailShade.JointForce, _tail.MaxTails - 1);
                Batch.End();
            }

            if (Batch.Begin(_fireTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                if (!Impacted)
                {
                    Batch.AddSprite(_head, 2.2f, FireColor, _age * 25f);
                    Batch.AddSprite(_head, 1.6f, FireColor, -_age * 31f);
                }
                for (int i = 0; i < _embers.Length; i++)
                {
                    ref Ember e = ref _embers[i];
                    if (e.Live)
                    {
                        float life = e.LifeTime / e.MaxLife;
                        Batch.AddSprite(e.Position, e.Scale * (0.4f + 0.6f * life), FireColor * life, e.Rotation);
                    }
                }
                Batch.End();
            }

            if (Batch.Begin(_flareTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                if (!Impacted)
                    Batch.AddSprite(_head, 1.4f, CoreColor);
                else
                {
                    float flash = EffectLights.Flash(_impactAge, FlashFrames, 1.2f);
                    if (flash > 0f)
                        Batch.AddSprite(_end, 3f + 4f * (1f - flash), CoreColor * flash);
                }
                Batch.End();
            }
        }
    }
}
