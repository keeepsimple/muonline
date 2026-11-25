#nullable enable
using System;
using System.Collections.Generic;
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
    /// Illusion Temple persistent orders: 210 Order of Protection (pale blue ward around the caster) and
    /// 211 Order of Restraint (red binding cage of chained motes on the target). One instance per owner and
    /// skill; it lives until <see cref="End"/> (BF/07 or the 0x07 status ending), the owner dies, leaves the
    /// world, or <see cref="MaxLifeFrames"/> runs out in case the end packet is lost.
    /// </summary>
    internal sealed class IllusionTempleOrderEffect : SourceSkillEffectBase
    {
        public const ushort ProtectionSkill = 210;
        public const ushort RestraintSkill = 211;

        private const string GroundTexturePath = "Effect/Magic_Ground2.jpg";
        private const string GlowTexturePath = "Effect/flare01.jpg";
        private const float MaxLifeFrames = 20f * 25f; // server default is 15 s; the end packets are authoritative
        private const float FadeInFrames = 8f;
        private const float FadeOutFrames = 10f;
        private const int MotesPerBand = 6;

        // Main thread only: lets the factory refresh an existing instance and the end packets find it.
        private static readonly List<IllusionTempleOrderEffect> Active = new();

        private readonly bool _restraint;
        private readonly Vector3 _color;
        private Texture2D? _groundTexture;
        private Texture2D? _glowTexture;
        private float _life;
        private float _fade;
        private float _age;
        private bool _stopped;

        public IllusionTempleOrderEffect(WalkerObject owner, ushort skill)
            : base(owner, 0f, 0f, 96)
        {
            Skill = skill;
            _restraint = skill == RestraintSkill;
            _color = _restraint ? new Vector3(1f, 0.25f, 0.2f) : new Vector3(0.45f, 0.75f, 1f);
            _life = MaxLifeFrames;
            Active.Add(this);
        }

        public ushort Skill { get; }
        public WalkerObject Owner => Caster;
        public bool IsStopped => _stopped;

        /// <summary>The live order effect of <paramref name="owner"/>, refreshed for a new cast, or null.</summary>
        public static IllusionTempleOrderEffect? FindAndRefresh(WalkerObject owner, ushort skill)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                var e = Active[i];
                if (e.Skill == skill && ReferenceEquals(e.Caster, owner) && (e.World == null || e.World == owner.World))
                {
                    e._stopped = false;
                    e._life = MaxLifeFrames;
                    return e;
                }
            }
            return null;
        }

        /// <summary>Ends the order <paramref name="skill"/> on the object <paramref name="ownerId"/> (fades out).</summary>
        public static void End(ushort ownerId, ushort skill)
        {
            ownerId &= 0x7FFF;
            for (int i = 0; i < Active.Count; i++)
            {
                var e = Active[i];
                if (e.Skill == skill && (e.Caster.NetworkId & 0x7FFF) == ownerId)
                    e.Stop();
            }
        }

        public void Stop() => _stopped = true;

        protected override async Task LoadEffectContent()
        {
            _groundTexture = await LoadTexture(GroundTexturePath);
            _glowTexture = await LoadTexture(GlowTexturePath);
        }

        protected override void OnStart()
        {
        }

        protected override void OnTick(float factor)
        {
            Position = CasterPosition; // follow the owner so culling keeps the aura
            _age += factor;
            _life -= factor;
            if (_life <= 0f || Caster is PlayerObject { IsDead: true } || Caster is MonsterObject { IsDead: true })
                _stopped = true;

            _fade = _stopped
                ? MathF.Max(0f, _fade - factor / FadeOutFrames)
                : MathF.Min(1f, _fade + factor / FadeInFrames);

            float pulse = 0.85f + 0.15f * MathF.Sin(_age * 0.2f);
            Lights.Set(0, CasterPosition + Vector3.UnitZ * 90f, _color, _restraint ? 180f : 220f, 0.8f * _fade * pulse);
        }

        protected override bool HasActiveContent() => !_stopped || _fade > 0f;

        protected override void OnDraw()
        {
            if (_fade <= 0f || Caster.Hidden)
                return;

            Vector3 feet = CasterPosition;
            float pulse = 0.85f + 0.15f * MathF.Sin(_age * 0.2f);
            Vector3 light = _color * (_fade * pulse);

            if (Batch.Begin(_groundTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                float size = _restraint ? 1.6f : 2.2f;
                float spin = _age * (_restraint ? 6f : 2f);
                Batch.AddTerrainDecal(World?.Terrain, feet.X, feet.Y, size, size, light, spin);
                if (_restraint) // tighter counter-rotating seal
                    Batch.AddTerrainDecal(World?.Terrain, feet.X, feet.Y, size * 0.6f, size * 0.6f, light, -spin * 1.5f);
                Batch.End();
            }

            if (Batch.Begin(_glowTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                if (_restraint)
                    DrawRestraint(feet, light);
                else
                    DrawProtection(feet, light);
                Batch.End();
            }
        }

        // Soft ward around the body plus motes slowly circling and bobbing at chest height.
        private void DrawProtection(Vector3 feet, Vector3 light)
        {
            Batch.AddBillboard(feet + new Vector3(0f, 0f, 95f), 95f, 115f, 0f, new Color(light * 0.35f));
            float angle = _age * 0.06f;
            for (int i = 0; i < MotesPerBand; i++)
            {
                float a = angle + i * MathHelper.TwoPi / MotesPerBand;
                float z = 70f + 35f * MathF.Sin(_age * 0.08f + i);
                var p = feet + new Vector3(MathF.Cos(a) * 60f, MathF.Sin(a) * 60f, z);
                Batch.AddBillboard(p, 14f, 14f, 0f, new Color(light));
            }
        }

        // Three counter-rotating bands of motes at shin, waist and chest height: the binding chains.
        private void DrawRestraint(Vector3 feet, Vector3 light)
        {
            for (int band = 0; band < 3; band++)
            {
                float dir = band % 2 == 0 ? 1f : -1f;
                float angle = _age * 0.12f * dir + band;
                float z = 30f + band * 50f;
                for (int i = 0; i < MotesPerBand; i++)
                {
                    float a = angle + i * MathHelper.TwoPi / MotesPerBand;
                    var p = feet + new Vector3(MathF.Cos(a) * 45f, MathF.Sin(a) * 45f, z);
                    Batch.AddBillboard(p, 11f, 11f, 0f, new Color(light));
                }
            }
        }

        protected override void DisposeEffect() => Active.Remove(this);
    }
}
