#nullable enable
using System;
using Client.Main.Controllers;
using Client.Main.Models;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Friends;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Core.Client;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game
{
    /// <summary>
    /// Entry dialog of the Kanturu Gateway Machine (NPC 367). The server answers the NPC talk with KanturuStateInfo
    /// (0xD1/0x00); the Enter button sends KanturuEnterRequest (0xD1/0x01). Closing the dialog ends the NPC conversation.
    /// </summary>
    public sealed class KanturuGatewayWindow : MessengerWindowBase
    {
        private const int Width = 340;
        private const int ButtonY = HeaderHeight + 128;
        private const int Height = ButtonY + 32 + Pad;
        private static readonly Rectangle EnterButtonLocal = new(Pad, ButtonY, (Width - Pad * 3) / 2, 32);
        private static readonly Rectangle CloseButtonLocal = new(Pad * 2 + (Width - Pad * 3) / 2, ButtonY, (Width - Pad * 3) / 2, 32);

        private static KanturuGatewayWindow? _instance;

        private byte _state;
        private bool _canEnter;
        private int _users;
        private uint _remainSeconds;
        private bool _enterHovered;
        private bool _closeHovered;

        private KanturuGatewayWindow() : base(Width, Height)
        {
            Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;
        }

        public override bool NonDisposable => true;
        public static KanturuGatewayWindow Instance => _instance ??= new KanturuGatewayWindow();

        protected override string Title => "KANTURU";
        protected override string? Subtitle => "Gateway Machine";

        /// <summary>The text of the dialog for a StateInfo packet; the server sends state "Standby" while the next opening is awaited.</summary>
        public static string DescribeState(byte state, bool canEnter, int users, uint remainSeconds) => state switch
        {
            1 => $"The event opens in {Math.Max(1, (remainSeconds + 59) / 60)} minute(s).",
            2 when canEnter => $"The gateway is open. {users} player(s) inside.",
            4 when canEnter => $"The Tower of Refinement is open. Closes in {FormatHoursMinutes(remainSeconds)}.",
            2 or 3 => "The battle is in progress. Entry is closed.",
            _ => "The gateway is closed.",
        };

        private static string FormatHoursMinutes(uint seconds) => $"{seconds / 3600}h {seconds % 3600 / 60:D2}m";

        public void ShowInfo(byte state, bool canEnter, int users, uint remainSeconds)
        {
            _state = state;
            _canEnter = canEnter;
            _users = users;
            _remainSeconds = remainSeconds;
            Show();
        }

        public override void Hide()
        {
            if (!Visible) return;
            base.Hide();
            var service = MuGame.Network?.GetCharacterService();
            if (service?.IsNpcDialogOpen == true) _ = service.SendCloseNpcRequestAsync();
        }

        public override void Update(GameTime gameTime)
        {
            // The conversation ended (e.g. the hero walked away and CloseNpcRequest was sent): the server then
            // ignores KanturuEnterRequest, so the dialog must not stay open.
            if (Visible && MuGame.Network?.GetCharacterService()?.IsNpcDialogOpen == false)
            {
                Hide();
                return;
            }

            base.Update(gameTime);
        }

        protected override void UpdateContent(GameTime gameTime, bool mouseInside, int wheel)
        {
            _enterHovered = Translate(EnterButtonLocal).Contains(MousePosition);
            _closeHovered = Translate(CloseButtonLocal).Contains(MousePosition);
        }

        protected override bool HandleClick()
        {
            if (_closeHovered)
            {
                Hide();
                return true;
            }

            if (_enterHovered && _canEnter)
            {
                var service = MuGame.Network?.GetCharacterService();
                if (service != null) _ = service.SendKanturuEnterRequestAsync();
                return true;
            }

            return false;
        }

        protected override void DrawContent(SpriteBatch sb, GameTime gameTime)
        {
            var area = Translate(new Rectangle(Pad, HeaderHeight + 10, Width - Pad * 2, 100));
            PlayerShopUi.DrawPanel(sb, area, ModernHudTheme.BgDarkest, Alpha);
            var lines = WrapText(DescribeState(_state, _canEnter, _users, _remainSeconds), 13f, area.Width - 16);
            for (int i = 0; i < lines.Count && i < 5; i++)
                PlayerShopUi.DrawText(sb, lines[i], new Vector2(area.X + 8, area.Y + 10 + i * 18), ModernHudTheme.TextWhite, 13f, Alpha);

            PlayerShopUi.DrawButton(sb, Translate(EnterButtonLocal), "ENTER", _enterHovered, _canEnter, ModernHudTheme.Accent, Alpha, 13f);
            PlayerShopUi.DrawButton(sb, Translate(CloseButtonLocal), "CLOSE", _closeHovered, true, ModernHudTheme.Accent, Alpha, 13f);
        }
    }
}
