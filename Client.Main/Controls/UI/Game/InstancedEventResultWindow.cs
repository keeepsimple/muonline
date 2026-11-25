#nullable enable
using System.Collections.Generic;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Friends;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game
{
    /// <summary>
    /// Result window of the instanced events (Illusion Temple score board, Doppelganger and Empire Guardian
    /// results): a headline, a few detail lines and an optional per-player table.
    /// </summary>
    public sealed class InstancedEventResultWindow : MessengerWindowBase
    {
        public readonly record struct Row(string Name, string Detail, string Value, bool Own);

        private const int Width = 380;
        private const int RowHeight = 20;
        private const int LineHeight = 18;
        private const int ButtonHeight = 32;

        private static InstancedEventResultWindow? _instance;

        private string _title = "EVENT RESULT";
        private string _headline = string.Empty;
        private Color _headlineColor = ModernHudTheme.TextWhite;
        private List<string> _details = new();
        private List<Row> _rows = new();
        private string _detailHeader = string.Empty;
        private string _valueHeader = string.Empty;
        private bool _closeHovered;

        private InstancedEventResultWindow() : base(Width, 200)
        {
            Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;
        }

        public override bool NonDisposable => true;
        public static InstancedEventResultWindow Instance => _instance ??= new InstancedEventResultWindow();

        protected override string Title => _title;

        public string Headline => _headline;
        public int RowCount => _rows.Count;

        public void ShowResult(
            string title,
            string headline,
            Color headlineColor,
            IEnumerable<string>? details = null,
            IEnumerable<Row>? rows = null,
            string detailHeader = "",
            string valueHeader = "")
        {
            _title = title;
            _headline = headline;
            _headlineColor = headlineColor;
            _details = new List<string>(details ?? System.Array.Empty<string>());
            _rows = new List<Row>(rows ?? System.Array.Empty<Row>());
            _detailHeader = detailHeader;
            _valueHeader = valueHeader;

            int height = HeaderHeight + 14 + 24 + _details.Count * LineHeight
                + (_rows.Count > 0 ? 10 + RowHeight + _rows.Count * RowHeight : 0)
                + 14 + ButtonHeight + Pad;
            ControlSize = ViewSize = new Point(Width, height);
            Show();
        }

        private Rectangle CloseButtonLocal => new(Width / 2 - 60, WindowHeight - Pad - ButtonHeight, 120, ButtonHeight);

        protected override void UpdateContent(GameTime gameTime, bool mouseInside, int wheel)
            => _closeHovered = Translate(CloseButtonLocal).Contains(MousePosition);

        protected override bool HandleClick()
        {
            if (!_closeHovered) return false;
            Hide();
            return true;
        }

        protected override void DrawContent(SpriteBatch sb, GameTime gameTime)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            int y = HeaderHeight + 14;

            PlayerShopUi.DrawTextCentered(sb, _headline, Translate(new Rectangle(0, y, Width, 24)), _headlineColor, 16f, Alpha);
            y += 24;

            foreach (string line in _details)
            {
                PlayerShopUi.DrawTextCentered(sb, line, Translate(new Rectangle(0, y, Width, LineHeight)), ModernHudTheme.TextWhite, 12f, Alpha);
                y += LineHeight;
            }

            if (_rows.Count > 0)
            {
                y += 10;
                var table = Translate(new Rectangle(Pad, y, Width - Pad * 2, RowHeight + _rows.Count * RowHeight));
                PlayerShopUi.DrawPanel(sb, table, ModernHudTheme.BgDarkest, Alpha);
                int nameX = table.X + 8, detailX = table.X + 160, valueX = table.X + 260;
                sb.Draw(pixel, new Rectangle(table.X + 1, table.Y + 1, table.Width - 2, RowHeight - 2), ModernHudTheme.BgMid * Alpha);
                PlayerShopUi.DrawText(sb, "Name", new Vector2(nameX, table.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);
                PlayerShopUi.DrawText(sb, _detailHeader, new Vector2(detailX, table.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);
                PlayerShopUi.DrawText(sb, _valueHeader, new Vector2(valueX, table.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);

                for (int i = 0; i < _rows.Count; i++)
                {
                    var row = _rows[i];
                    int rowY = table.Y + RowHeight + i * RowHeight;
                    if (row.Own) sb.Draw(pixel, new Rectangle(table.X + 1, rowY, table.Width - 2, RowHeight), ModernHudTheme.Accent * 0.35f * Alpha);
                    else if (i % 2 == 1) sb.Draw(pixel, new Rectangle(table.X + 1, rowY, table.Width - 2, RowHeight), Color.White * 0.025f * Alpha);
                    Color color = row.Own ? new Color(200, 255, 0) : ModernHudTheme.TextWhite;
                    PlayerShopUi.DrawText(sb, PlayerShopUi.Ellipsize(row.Name, 12f, detailX - nameX - 6), new Vector2(nameX, rowY + 3), color, 12f, Alpha);
                    PlayerShopUi.DrawText(sb, row.Detail, new Vector2(detailX, rowY + 3), color, 12f, Alpha);
                    PlayerShopUi.DrawText(sb, row.Value, new Vector2(valueX, rowY + 3), ModernHudTheme.Success, 12f, Alpha);
                }
            }

            PlayerShopUi.DrawButton(sb, Translate(CloseButtonLocal), "CLOSE", _closeHovered, true, ModernHudTheme.Accent, Alpha, 13f);
        }
    }
}
