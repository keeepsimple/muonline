using System;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Helpers;
using Client.Main.Models;
using Client.Main.Networking.PacketHandling.Handlers;
using Client.Main.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Game
{
    /// <summary>
    /// The four Illusion Temple special skills (210-213), shown under the <see cref="InstancedEventHud"/> while the hero
    /// is in a running temple. They are paid with the event's kill points (BF/06), not mana, and are cast with a click
    /// or Shift+1..4 (plain 1..0 stay the normal skill slots). Target skills (211, 213) use the hovered foe; without one
    /// the skill is armed and the next right-click on a foe casts it.
    /// </summary>
    public sealed class IllusionTempleSkillBar : UIControl
    {
        public static readonly ushort[] Skills = { 210, 211, 212, 213 };
        private static readonly string[] Labels = { "Protection", "Restraint", "Tracking", "Shield Burn" };
        private static readonly Keys[] HotkeyKeys = { Keys.D1, Keys.D2, Keys.D3, Keys.D4 };

        private const int Width = 230;
        private const int ButtonHeight = 26;
        private const float FontSize = 12f;

        private static IllusionTempleSkillBar _instance;
        private int _hovered = -1;

        private IllusionTempleSkillBar()
        {
            AutoViewSize = false;
            Interactive = true;
            Visible = false;
            Align = ControlAlign.Top | ControlAlign.Right;
            X = -10;
            Y = 10 + 99 + 4; // under the 5-line Illusion Temple HUD (14 + 5 * 17)
            ControlSize = ViewSize = new Point(Width, 20 + 2 * (ButtonHeight + 4) + 20);
        }

        public override bool NonDisposable => true;
        public static IllusionTempleSkillBar Instance => _instance ??= new IllusionTempleSkillBar();

        /// <summary>A target skill waiting for a right-click on a foe (0 = none).</summary>
        public ushort ArmedSkill { get; set; }

        // ───────────── rules (OpenMU IllusionTempleContext; public for the offline checks) ─────────────

        public static int Cost(ushort skill) => skill == 212 ? 10 : 5;

        public static bool CanAfford(ushort skill, int killPoints) => killPoints >= Cost(skill);

        /// <summary>Restraint and Shield Burn need a foe within 6 tiles; Protection and Tracking act on the caster.</summary>
        public static bool NeedsTarget(ushort skill) => skill is 211 or 213;

        public static bool IsIllusionTempleMap(int mapId) => mapId is >= 45 and <= 50 or 98;

        /// <summary>The bar is up while the hero is on a temple map and the event HUD runs a temple match.</summary>
        public static bool IsShown(InstancedEventMode mode, int mapId) => mode == InstancedEventMode.IllusionTemple && IsIllusionTempleMap(mapId);

        /// <summary>Skill of a fresh Shift+1..4 press, or 0.</summary>
        public static ushort HotkeySkill(KeyboardState keyboard, KeyboardState previous)
        {
            if (!keyboard.IsKeyDown(Keys.LeftShift) && !keyboard.IsKeyDown(Keys.RightShift)) return 0;
            for (int i = 0; i < HotkeyKeys.Length; i++)
                if (keyboard.IsKeyDown(HotkeyKeys[i]) && !previous.IsKeyDown(HotkeyKeys[i])) return Skills[i];
            return 0;
        }

        public static bool IsShiftDown(KeyboardState keyboard) => keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);

        // ───────────────────────────── input ─────────────────────────────

        public override void Update(GameTime gameTime)
        {
            Visible = IsShown(InstancedEventHud.Instance.Mode, MuGame.Network?.GetCharacterState()?.MapId ?? -1);
            if (!Visible)
            {
                ArmedSkill = 0;
                _hovered = -1;
                base.Update(gameTime);
                return;
            }

            base.Update(gameTime);
            _hovered = IsMouseOver ? ButtonAt(MuGame.Instance.UiMouseState.Position) : -1;

            if (Scene?.IsTextInputActive != true)
            {
                ushort skill = HotkeySkill(MuGame.Instance.Keyboard, MuGame.Instance.PrevKeyboard);
                if (skill != 0) Cast(skill);
            }
        }

        public override bool OnClick()
        {
            int index = ButtonAt(MuGame.Instance.UiMouseState.Position);
            if (index < 0) return false;
            ushort skill = Skills[index];
            if (ArmedSkill == skill) ArmedSkill = 0; // second click disarms
            else Cast(skill);
            return true;
        }

        private void Cast(ushort skill) => (Scene as GameScene)?.SkillController?.TryCastIllusionTempleSkill(skill);

        private Rectangle ButtonRect(int index)
        {
            var rect = DisplayRectangle;
            int width = (rect.Width - 18) / 2;
            return new Rectangle(rect.X + 6 + index % 2 * (width + 6), rect.Y + 22 + index / 2 * (ButtonHeight + 4), width, ButtonHeight);
        }

        private int ButtonAt(Point position)
        {
            for (int i = 0; i < Skills.Length; i++)
                if (ButtonRect(i).Contains(position)) return i;
            return -1;
        }

        // ───────────────────────────── drawing ─────────────────────────────

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready) return;

            var sb = GraphicsManager.Instance.Sprite;
            var pixel = GraphicsManager.Instance.Pixel;
            if (sb == null || pixel == null) return;

            using var scope = new SpriteBatchScope(
                sb,
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                GraphicsManager.GetQualityLinearSamplerState(),
                transform: UiScaler.SpriteTransform);

            var rect = DisplayRectangle;
            sb.Draw(pixel, rect, ModernHudTheme.BgDark);
            UiDrawHelper.DrawBorder(sb, rect, ModernHudTheme.BorderOuter);

            int points = InstancedEventHud.Instance.SkillPoints;
            PlayerShopUi.DrawTextCentered(sb, $"KILL POINTS: {points}", new Rectangle(rect.X, rect.Y + 3, rect.Width, 17), ModernHudTheme.TextGold, FontSize);

            for (int i = 0; i < Skills.Length; i++)
            {
                ushort skill = Skills[i];
                bool affordable = CanAfford(skill, points);
                var button = ButtonRect(i);
                Color fill = ArmedSkill == skill ? ModernHudTheme.Accent * 0.6f
                    : i == _hovered && affordable ? ModernHudTheme.Accent * 0.3f
                    : ModernHudTheme.BgDarkest;
                sb.Draw(pixel, button, fill);
                UiDrawHelper.DrawBorder(sb, button, affordable ? ModernHudTheme.BorderInner : ModernHudTheme.BorderInner * 0.4f);
                PlayerShopUi.DrawTextCentered(sb, $"{i + 1} {Labels[i]} ({Cost(skill)})", button,
                    affordable ? ModernHudTheme.TextWhite : ModernHudTheme.TextGray * 0.6f, FontSize);
            }

            string hint = ArmedSkill != 0 ? $"Right-click a foe: {InstancedEventHandler.IllusionTempleSkillName(ArmedSkill)}" : "Shift+1-4 or click to cast";
            PlayerShopUi.DrawTextCentered(sb, hint, new Rectangle(rect.X, rect.Bottom - 19, rect.Width, 17),
                ArmedSkill != 0 ? ModernHudTheme.Warning : ModernHudTheme.TextGray, 11f);
        }
    }
}
