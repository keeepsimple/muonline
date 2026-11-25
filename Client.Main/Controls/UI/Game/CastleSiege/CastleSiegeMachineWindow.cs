#nullable enable
using Client.Main.Controls.UI.Game.Friends;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.CastleSiege
{
    /// <summary>
    /// Siege machine (catapult) window opened by CastleSiegeMachineInterface (0xB7/0x00): pick a target zone to fire at.
    /// </summary>
    public sealed class CastleSiegeMachineWindow : MessengerWindowBase
    {
        private const int Width = 300;
        private const int ButtonHeight = 30;
        private const int ListY = HeaderHeight + 30;

        // Target zones of OpenMU's default configuration (CastleSiegeInitializer.InitializeMachineZones).
        private static readonly string[] AttackZones = { "62,103 - 72,112", "88,104 - 124,111", "116,105 - 124,112", "73,86 - 105,103" };
        private static readonly string[] DefenseZones = { "61,88 - 93,108", "92,89 - 127,111", "84,52 - 102,66" };

        private static CastleSiegeMachineWindow? _instance;
        public static CastleSiegeMachineWindow Instance => _instance ??= new CastleSiegeMachineWindow();
        public override bool NonDisposable => true;

        private ushort _machineId;
        private bool _isAttack;
        private int _hovered = -1;

        private CastleSiegeMachineWindow() : base(Width, ListY + AttackZones.Length * (ButtonHeight + 6) + Pad)
        {
            Align = ControlAlign.Right | ControlAlign.VerticalCenter;
            Margin = new Margin { Right = 10 };
        }

        protected override string Title => _isAttack ? "CATAPULT" : "DEFENSE CATAPULT";
        protected override string? Subtitle => "Choose a target zone";

        private string[] Zones => _isAttack ? AttackZones : DefenseZones;

        public void ShowMachine(ushort machineId, bool isAttack)
        {
            _machineId = machineId;
            _isAttack = isAttack;
            Show();
        }

        public override void Hide()
        {
            base.Hide();
            // Releases the operator reservation of the machine on the server.
            var service = MuGame.Network?.GetCharacterService();
            if (service?.IsNpcDialogOpen == true) _ = service.SendCloseNpcRequestAsync();
        }

        public override void Update(GameTime gameTime)
        {
            if (Visible && MuGame.Network?.GetCharacterService()?.IsNpcDialogOpen == false)
            {
                Hide();
                return;
            }

            base.Update(gameTime);
        }

        private static Rectangle ZoneLocal(int index) => new(Pad, ListY + index * (ButtonHeight + 6), Width - Pad * 2, ButtonHeight);

        protected override void UpdateContent(GameTime gameTime, bool mouseInside, int wheel)
        {
            _hovered = -1;
            for (int i = 0; i < Zones.Length; i++)
                if (Translate(ZoneLocal(i)).Contains(MousePosition)) _hovered = i;
        }

        protected override bool HandleClick()
        {
            if (_hovered < 0) return false;
            _ = MuGame.Network?.GetCastleSiegeService()?.SendFireMachineAsync(_machineId, (byte)(_hovered + 1));
            return true;
        }

        protected override void DrawContent(SpriteBatch sb, GameTime gameTime)
        {
            PlayerShopUi.DrawText(sb, "Fire at:", new Vector2(DisplayRectangle.X + Pad, DisplayRectangle.Y + HeaderHeight + 8), ModernHudTheme.TextGray, 12f, Alpha);
            for (int i = 0; i < Zones.Length; i++)
                PlayerShopUi.DrawButton(sb, Translate(ZoneLocal(i)), $"Zone {i + 1}  ({Zones[i]})", _hovered == i, true,
                    _isAttack ? ModernHudTheme.Danger : ModernHudTheme.Accent, Alpha, 12f);
        }
    }
}
