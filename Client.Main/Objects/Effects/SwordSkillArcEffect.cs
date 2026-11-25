#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Basic Dark Knight weapon skills (Falling Slash 19, Lunge 20, Uppercut 21, Cyclone 22, Slash 23 and their
    /// strengtheners): one short additive sword-arc ribbon swept along the skill's motion once the swing animation
    /// reaches its strike frame, then an impact flash and a few sparks on the target. The player's own weapon blur
    /// (<see cref="PlayerWeaponBlur"/>) still draws the blade trail; this only adds the slash shape. Kept cheap because
    /// these skills are spammed.
    /// </summary>
    internal sealed class SwordSkillArcEffect : SourceSkillEffectBase
    {
        public enum Motion { FallingSlash, Lunge, Uppercut, Cyclone, Slash }

        private const string ArcTexturePath = "Effect/motion_blur_r.jpg"; // BITMAP_BLUR + 2, also the weapon blur
        private const string FlashTexturePath = "Effect/flare01.jpg";

        private const int ArcSegments = 14;
        private const int MaxSparkJoints = 6;
        private const int MaxSparks = 10;
        private const float WaitTimeoutSeconds = 0.45f;   // release anyway when the strike frame never comes
        private const float FadeFrames = 5f;
        private const float FlashFrames = 6f;
        private const float FlashScale = 1.6f;
        private const float LungeHalfWidth = 16f;
        private const float HitHeight = 90f;
        private const float FallbackHitDistance = 120f;
        private const float HitLightRadius = 200f;

        private static readonly Vector3 ArcLight = new(0.7f, 0.82f, 1f);
        private static readonly Vector3 HitLight = new(1f, 0.85f, 0.6f);

        private readonly Motion _motion;
        private readonly WalkerObject? _target;
        private readonly PlayerAction _action;
        private readonly PlayerAction _altAction;
        private readonly float _startFrame;
        private readonly SourceSparkPool _sparks = new(MaxSparkJoints, MaxSparks);

        private Texture2D? _arcTexture;
        private Texture2D? _flashTexture;
        private Texture2D? _sparkJointTexture;
        private Texture2D? _sparkTexture;

        // Arc shape, fixed at release. Arcs: angle in degrees in the (A, B) plane; Lunge: distance along B.
        private float _from;
        private float _to;
        private float _tailSpan;
        private float _sweepFrames;
        private float _innerRadius;
        private float _outerRadius;
        private Vector3 _center;
        private Vector3 _axisA;
        private Vector3 _axisB;

        private float _waitSeconds;
        private bool _released;
        private float _age;
        private bool _hit;
        private float _flashAge = float.MaxValue;
        private float _flashRotation;
        private Vector3 _hitPosition;

        public SwordSkillArcEffect(WalkerObject caster, WalkerObject? target, Motion motion)
            : base(caster, 0f, 0f, ArcSegments + 4)
        {
            _target = target;
            _motion = motion;
            _action = motion switch
            {
                Motion.FallingSlash => PlayerAction.PlayerAttackSkillSword1,
                Motion.Lunge => PlayerAction.PlayerAttackSkillSword2,
                Motion.Uppercut => PlayerAction.PlayerAttackSkillSword3,
                Motion.Cyclone => PlayerAction.PlayerAttackSkillSword4,
                _ => PlayerAction.PlayerAttackSkillSword5
            };
            // Slash alternates with the two-hand third swing (PlayerObject.ApplySlashSwordCount).
            _altAction = motion == Motion.Slash ? PlayerAction.PlayerAttackTwoHandSword3 : _action;
            _startFrame = motion switch
            {
                Motion.Cyclone => 2f,
                Motion.FallingSlash => 3.5f,
                _ => 3f
            };
        }

        internal Motion SkillMotion => _motion;

        protected override async Task LoadEffectContent()
        {
            _arcTexture = await LoadTexture(ArcTexturePath);
            _flashTexture = await LoadTexture(FlashTexturePath);
            _sparkJointTexture = await LoadTexture(SourceSparkPool.JointTexturePath);
            _sparkTexture = await LoadTexture(SourceSparkPool.ParticleTexturePath);
        }

        protected override void OnStart()
        {
        }

        protected override void OnTick(float factor)
        {
            if (!_released)
            {
                // Wait for the swing itself (the hero's local cast is released at the click, before the blade moves).
                _waitSeconds += factor * 0.04f;
                bool striking = Caster is PlayerObject p && (p.CurrentAction == _action || p.CurrentAction == _altAction)
                    && CasterFrame >= _startFrame;
                if (!striking && _waitSeconds < WaitTimeoutSeconds)
                    return;
                Release();
            }

            _age += factor;
            if (!_hit && _age >= _sweepFrames)
                Hit();

            if (_flashAge != float.MaxValue)
                _flashAge += factor;

            _sparks.Move(factor, World?.Terrain);
            Lights.Set(0, _hitPosition, HitLight, HitLightRadius, EffectLights.Flash(_flashAge, FlashFrames));
        }

        private void Release()
        {
            _released = true;
            _age = 0f;

            Vector3 origin = CasterPosition;
            float yaw = CasterYawDegrees;
            Vector3 forward = SourceJointMath.Rotate(new Vector3(0f, -1f, 0f), new Vector3(0f, 0f, yaw));
            Vector3 right = SourceJointMath.Rotate(new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, yaw));

            switch (_motion)
            {
                case Motion.FallingSlash: // overhead chop: from straight up down to the ground in front
                    SetArc(origin + Vector3.UnitZ * 110f + forward * 30f, Vector3.UnitZ, forward, 10f, 160f, 80f, 4f, 40f, 150f);
                    break;
                case Motion.Uppercut:     // rising cut: from the ground in front up over the head
                    SetArc(origin + Vector3.UnitZ * 100f + forward * 30f, Vector3.UnitZ, forward, 165f, 15f, 80f, 4f, 40f, 150f);
                    break;
                case Motion.Cyclone:      // one full spin around the caster
                    SetArc(origin + Vector3.UnitZ * 90f, right, forward, 0f, 360f, 200f, 8f, 60f, 170f);
                    break;
                case Motion.Lunge:        // straight thrust: distance along forward, width along right
                    SetArc(origin + Vector3.UnitZ * 100f, right, forward, 30f, 240f, 120f, 3f, 0f, 0f);
                    break;
                default:                  // horizontal swing from the right side across to the left
                    SetArc(origin + Vector3.UnitZ * 100f, right, forward, -95f, 95f, 90f, 4f, 50f, 160f);
                    break;
            }

            _hitPosition = IsAlive(_target)
                ? _target!.WorldPosition.Translation + Vector3.UnitZ * HitHeight
                : origin + forward * FallbackHitDistance + Vector3.UnitZ * HitHeight;
        }

        private void SetArc(Vector3 center, Vector3 axisA, Vector3 axisB, float from, float to, float tailSpan,
            float sweepFrames, float innerRadius, float outerRadius)
        {
            _center = center;
            _axisA = axisA;
            _axisB = axisB;
            _from = from;
            _to = to;
            _tailSpan = tailSpan;
            _sweepFrames = sweepFrames;
            _innerRadius = innerRadius;
            _outerRadius = outerRadius;
        }

        private void Hit()
        {
            _hit = true;
            if (IsAlive(_target))
                _hitPosition = _target!.WorldPosition.Translation + Vector3.UnitZ * HitHeight;

            _flashAge = 0f;
            _flashRotation = MuGame.Random.Next(360);
            for (int i = 0; i < MaxSparkJoints / 2; i++)
                _sparks.SpawnJoint(_hitPosition, new Vector3(MuGame.Random.Next(60) - 30, 0f, MuGame.Random.Next(360)));
            for (int i = 0; i < MaxSparks / 2; i++)
                _sparks.SpawnSpark(_hitPosition, new Vector3(MuGame.Random.Next(60) - 30, 0f, 0f));
        }

        private void EdgePoints(float x, out Vector3 inner, out Vector3 outer)
        {
            if (_motion == Motion.Lunge)
            {
                Vector3 p = _center + _axisB * x;
                inner = p - _axisA * LungeHalfWidth;
                outer = p + _axisA * LungeHalfWidth;
                return;
            }

            float rad = MathHelper.ToRadians(x);
            Vector3 dir = _axisA * MathF.Cos(rad) + _axisB * MathF.Sin(rad);
            inner = _center + dir * _innerRadius;
            outer = _center + dir * _outerRadius;
        }

        private bool ArcActive => _released && _age < _sweepFrames + FadeFrames;

        protected override bool HasActiveContent() =>
            !_released || ArcActive || _flashAge < FlashFrames || _sparks.HasLive;

        protected override void OnDraw()
        {
            if (!_released)
                return;

            if (ArcActive && Batch.Begin(_arcTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                float progress = MathHelper.Clamp(_age / _sweepFrames, 0f, 1f);
                float fade = _age <= _sweepFrames ? 1f : 1f - (_age - _sweepFrames) / FadeFrames;
                float sign = MathF.Sign(_to - _from);
                float head = _from + (_to - _from) * progress;
                float tail = head - sign * _tailSpan;
                if ((tail - _from) * sign < 0f)
                    tail = _from;

                EdgePoints(tail, out Vector3 inner0, out Vector3 outer0);
                for (int i = 1; i <= ArcSegments; i++)
                {
                    float t1 = i / (float)ArcSegments;
                    float t0 = (i - 1) / (float)ArcSegments;
                    EdgePoints(MathHelper.Lerp(tail, head, t1), out Vector3 inner1, out Vector3 outer1);
                    // U runs head (0) to tail (1) like the weapon blur, brightest at the blade.
                    AddArcQuad(inner0, outer0, inner1, outer1, 1f - t0, 1f - t1, ArcLight * (fade * t0), ArcLight * (fade * t1));
                    inner0 = inner1;
                    outer0 = outer1;
                }
                Batch.End();
            }

            if (_flashAge < FlashFrames && Batch.Begin(_flashTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                float f = EffectLights.Flash(_flashAge, FlashFrames);
                Batch.AddSprite(_hitPosition, FlashScale * (0.6f + 0.4f * f), HitLight * f, _flashRotation);
                Batch.End();
            }

            if (_sparks.HasLive)
            {
                _sparks.DrawJoints(Batch, _sparkJointTexture);
                _sparks.DrawSparks(Batch, _sparkTexture);
            }
        }

        private void AddArcQuad(Vector3 inner0, Vector3 outer0, Vector3 inner1, Vector3 outer1, float u0, float u1, Vector3 light0, Vector3 light1) =>
            Batch.AddQuad(
                inner0, new Vector2(u0, 1f),
                outer0, new Vector2(u0, 0f),
                outer1, new Vector2(u1, 0f),
                inner1, new Vector2(u1, 1f),
                SourceQuadBatch.ToColor(light0),
                SourceQuadBatch.ToColor(light1));

        private static bool IsAlive(WalkerObject? walker)
            => walker != null && walker.Status != GameControlStatus.Disposed && walker.World != null;
    }
}
