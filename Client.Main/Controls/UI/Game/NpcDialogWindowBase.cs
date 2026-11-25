#nullable enable
using System;
using System.Collections.Generic;
using Client.Main.Models;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Friends;
using Client.Main.Controls.UI.Game.Inventory;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game
{
    /// <summary>
    /// Event NPC dialog in the style of <see cref="KanturuGatewayWindow"/>: a text panel, an optional status line and a
    /// row of buttons. Closing it ends the NPC conversation; it closes itself when the conversation ended elsewhere.
    /// </summary>
    public abstract class NpcDialogWindowBase : MessengerWindowBase
    {
        protected const int Width = 340;
        protected const int ButtonHeight = 32;

        private readonly int _bodyHeight;
        private readonly List<(Rectangle Local, Func<string> Text, Func<bool> Enabled, Action Click)> _buttons = new();
        private int _hovered = -1;

        protected NpcDialogWindowBase(int bodyHeight, int buttonRows)
            : base(Width, HeaderHeight + 10 + bodyHeight + buttonRows * (ButtonHeight + 8) + Pad)
        {
            _bodyHeight = bodyHeight;
            Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;
        }

        public override bool NonDisposable => true;

        /// <summary>The text of the dialog panel (public for the offline checks).</summary>
        public abstract IReadOnlyList<string> BodyLines { get; }

        /// <summary>Result of the last request, shown below the text.</summary>
        public string? StatusText { get; set; }

        /// <summary>Adds a button in cell <paramref name="column"/> of <paramref name="columns"/> in button row <paramref name="row"/>.</summary>
        protected void AddButton(int row, int column, int columns, Func<string> text, Func<bool> enabled, Action click)
        {
            int width = (Width - Pad * (columns + 1)) / columns;
            var local = new Rectangle(Pad + column * (width + Pad), HeaderHeight + 10 + _bodyHeight + 8 + row * (ButtonHeight + 8), width, ButtonHeight);
            _buttons.Add((local, text, enabled, click));
        }

        protected void AddCloseButton(int row, int column, int columns) => AddButton(row, column, columns, () => "CLOSE", () => true, Hide);

        public override void Hide()
        {
            if (!Visible) return;
            base.Hide();
            var service = MuGame.Network?.GetCharacterService();
            if (service?.IsNpcDialogOpen == true) _ = service.SendCloseNpcRequestAsync();
        }

        public override void Update(GameTime gameTime)
        {
            // The conversation ended (e.g. the hero walked away): the server then ignores the requests of the dialog.
            if (Visible && MuGame.Network?.GetCharacterService()?.IsNpcDialogOpen == false)
            {
                Hide();
                return;
            }

            base.Update(gameTime);
        }

        protected override void UpdateContent(GameTime gameTime, bool mouseInside, int wheel)
            => _hovered = _buttons.FindIndex(b => Translate(b.Local).Contains(MousePosition));

        protected override bool HandleClick()
        {
            if (_hovered < 0 || !_buttons[_hovered].Enabled()) return false;
            _buttons[_hovered].Click();
            return true;
        }

        protected override void DrawContent(SpriteBatch sb, GameTime gameTime)
        {
            var area = Translate(new Rectangle(Pad, HeaderHeight + 10, Width - Pad * 2, _bodyHeight));
            PlayerShopUi.DrawPanel(sb, area, ModernHudTheme.BgDarkest, Alpha);
            int y = area.Y + 10;
            int bottom = area.Bottom - (StatusText != null ? 26 : 4);
            foreach (string text in BodyLines)
                foreach (string line in WrapText(text, 13f, area.Width - 16))
                {
                    if (y + 18 > bottom) break;
                    PlayerShopUi.DrawText(sb, line, new Vector2(area.X + 8, y), ModernHudTheme.TextWhite, 13f, Alpha);
                    y += 18;
                }

            if (StatusText != null)
                PlayerShopUi.DrawText(sb, StatusText, new Vector2(area.X + 8, area.Bottom - 22), ModernHudTheme.Warning, 12f, Alpha);

            for (int i = 0; i < _buttons.Count; i++)
                PlayerShopUi.DrawButton(sb, Translate(_buttons[i].Local), _buttons[i].Text(), i == _hovered, _buttons[i].Enabled(), ModernHudTheme.Accent, Alpha, 13f);
        }
    }
}
