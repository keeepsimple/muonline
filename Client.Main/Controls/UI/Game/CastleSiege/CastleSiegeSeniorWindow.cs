#nullable enable
using System;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Friends;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Core.Client;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.CastleSiege
{
    /// <summary>
    /// Castle management window of the Senior NPC (NpcWindow 12, castle owner's guild master): Chaos Machine and
    /// store tax rates, treasury withdrawal, and the gate / Guardian Statue list with repair.
    /// </summary>
    public sealed class CastleSiegeSeniorWindow : MessengerWindowBase
    {
        private enum Tab { Taxes, Gates, Statues }

        private const int Width = 380;
        private const int TabY = HeaderHeight + 8;
        private const int TabHeight = 24;
        private const int BodyY = TabY + TabHeight + 8;
        private const int RowHeight = 20;
        private const int VisibleRows = 9;
        private const int BodyHeight = 22 + RowHeight * VisibleRows;
        private const int ButtonY = BodyY + BodyHeight + 12;
        private const int Height = ButtonY + 32 + Pad;
        private const int BodyWidth = Width - Pad * 2;

        // OpenMU CastleSiegeTaxProvider.MaximumPercentageTax.
        private const int MaxTaxRate = 3;

        private static readonly string[] TabNames = { "TAXES", "GATES", "STATUES" };

        private static CastleSiegeSeniorWindow? _instance;
        public static CastleSiegeSeniorWindow Instance => _instance ??= new CastleSiegeSeniorWindow();
        public override bool NonDisposable => true;

        private readonly TextFieldControl _withdrawField;
        private Tab _tab = Tab.Taxes;
        private int _selected = -1;
        private int _hoveredTab = -1;
        private int _hoveredRow = -1;
        private int _hoveredTaxButton = -1;
        private bool _actionHovered;

        private CastleSiegeSeniorWindow() : base(Width, Height)
        {
            Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;
            _withdrawField = AddField(Pad + 10, BodyY + 110, 180, 10);
        }

        private static CastleSiegeManager? Siege => MuGame.Network?.GetCastleSiegeManager();

        protected override string Title => "CASTLE MANAGEMENT";
        protected override string? Subtitle => Siege is { OwnerGuild.Length: > 0 } s ? s.OwnerGuild : null;

        public override void Show()
        {
            _tab = Tab.Taxes;
            _selected = -1;
            _withdrawField.Value = string.Empty;
            base.Show();
            RequestData();
        }

        public override void Hide()
        {
            base.Hide();
            var service = MuGame.Network?.GetCharacterService();
            if (service?.IsNpcDialogOpen == true) _ = service.SendCloseNpcRequestAsync();
        }

        private void RequestData()
        {
            var service = MuGame.Network?.GetCastleSiegeService();
            if (service == null) return;
            if (_tab == Tab.Taxes)
            {
                _ = service.SendStatusRequestAsync();
                _ = service.SendOwnerLogoRequestAsync();
                _ = service.SendTaxInfoRequestAsync();
            }
            else
            {
                _ = service.SendDefenseListRequestAsync(_tab == Tab.Statues);
            }
        }

        private static Rectangle TabLocal(int index)
        {
            int width = (BodyWidth - 8) / 3;
            return new Rectangle(Pad + index * (width + 4), TabY, width, TabHeight);
        }

        private static Rectangle RowLocal(int index) => new(Pad + 1, BodyY + 22 + index * RowHeight, BodyWidth - 2, RowHeight);

        /// <summary>Tax +/- buttons: 0/1 Chaos Machine, 2/3 store.</summary>
        private static Rectangle TaxButtonLocal(int index)
            => new(Pad + BodyWidth - 80 + (index % 2) * 36, BodyY + 12 + (index / 2) * 32, 30, 24);

        private static readonly Rectangle ActionLocal = new(Pad, ButtonY, BodyWidth, 32);

        // ─────────────────────────────── Update ───────────────────────────────

        public override void Update(GameTime gameTime)
        {
            if (Visible && MuGame.Network?.GetCharacterService()?.IsNpcDialogOpen == false)
            {
                Hide();
                return;
            }

            _withdrawField.Visible = _tab == Tab.Taxes;
            base.Update(gameTime);
        }

        protected override void UpdateContent(GameTime gameTime, bool mouseInside, int wheel)
        {
            _hoveredTab = -1;
            for (int i = 0; i < TabNames.Length; i++)
                if (Translate(TabLocal(i)).Contains(MousePosition)) _hoveredTab = i;

            _hoveredTaxButton = -1;
            _hoveredRow = -1;
            if (_tab == Tab.Taxes)
            {
                for (int i = 0; i < 4; i++)
                    if (Translate(TaxButtonLocal(i)).Contains(MousePosition)) _hoveredTaxButton = i;
            }
            else
            {
                int count = Siege?.DefenseNpcs.Count ?? 0;
                for (int i = 0; i < VisibleRows && i < count; i++)
                    if (Translate(RowLocal(i)).Contains(MousePosition)) _hoveredRow = i;
            }

            _actionHovered = Translate(ActionLocal).Contains(MousePosition);
        }

        protected override bool HandleClick()
        {
            var siege = Siege;
            var service = MuGame.Network?.GetCastleSiegeService();
            if (siege == null || service == null) return false;

            if (_hoveredTab >= 0)
            {
                if ((Tab)_hoveredTab != _tab)
                {
                    _tab = (Tab)_hoveredTab;
                    _selected = -1;
                    siege.SetDefenseNpcs(Array.Empty<CastleSiegeManager.DefenseNpc>());
                    RequestData();
                }
                return true;
            }

            if (_hoveredTaxButton >= 0)
            {
                bool chaos = _hoveredTaxButton < 2;
                int current = chaos ? siege.ChaosTaxRate : siege.StoreTaxRate;
                int next = Math.Clamp(current + (_hoveredTaxButton % 2 == 0 ? -1 : 1), 0, MaxTaxRate);
                if (next != current) _ = service.SendTaxChangeAsync(chaos ? (byte)1 : (byte)2, (uint)next);
                return true;
            }

            if (_hoveredRow >= 0)
            {
                _selected = _hoveredRow;
                return true;
            }

            if (!_actionHovered) return false;
            if (_tab == Tab.Taxes)
            {
                if (PlayerShopUi.TryParseZen(_withdrawField.Value, out uint amount) && amount > 0)
                {
                    _ = service.SendTributeWithdrawAsync(amount);
                    _withdrawField.Value = string.Empty;
                }
                else RequestDialog.ShowInfo("Enter the amount of Zen to withdraw.");
            }
            else if (_selected >= 0 && _selected < siege.DefenseNpcs.Count)
            {
                var npc = siege.DefenseNpcs[_selected];
                RequestDialog.Show($"Repair {NpcName(npc)} #{npc.Index}?",
                    onAccept: () => _ = service.SendDefenseRepairAsync(npc.Number, npc.Index));
            }
            return true;
        }

        // ─────────────────────────────── Draw ───────────────────────────────

        protected override void DrawContent(SpriteBatch sb, GameTime gameTime)
        {
            var siege = Siege;
            if (siege == null) return;

            CastleSiegeWindow.DrawOwnerMark(sb, Translate(CastleSiegeWindow.OwnerMarkLocal), Alpha);
            for (int i = 0; i < TabNames.Length; i++)
            {
                bool active = (int)_tab == i;
                PlayerShopUi.DrawButton(sb, Translate(TabLocal(i)), TabNames[i], _hoveredTab == i || active, true,
                    active ? ModernHudTheme.AccentBright : ModernHudTheme.Accent, Alpha, 12f);
            }

            var body = Translate(new Rectangle(Pad, BodyY, BodyWidth, BodyHeight));
            PlayerShopUi.DrawPanel(sb, body, ModernHudTheme.BgDarkest, Alpha);
            if (_tab == Tab.Taxes)
            {
                DrawTaxes(sb, body, siege);
                PlayerShopUi.DrawButton(sb, Translate(ActionLocal), "WITHDRAW", _actionHovered, true, ModernHudTheme.Accent, Alpha, 13f);
            }
            else
            {
                DrawDefenses(sb, body, siege);
                bool canRepair = _selected >= 0 && _selected < siege.DefenseNpcs.Count;
                PlayerShopUi.DrawButton(sb, Translate(ActionLocal), "REPAIR", _actionHovered && canRepair, canRepair, ModernHudTheme.Accent, Alpha, 13f);
            }
        }

        private void DrawTaxes(SpriteBatch sb, Rectangle body, CastleSiegeManager siege)
        {
            DrawTaxRow(sb, body, 0, "Chaos Machine tax", siege.HasTaxInfo ? $"{siege.ChaosTaxRate} %" : "-");
            DrawTaxRow(sb, body, 1, "Store tax", siege.HasTaxInfo ? $"{siege.StoreTaxRate} %" : "-");
            PlayerShopUi.DrawText(sb, "Treasury", new Vector2(body.X + 10, body.Y + 80), ModernHudTheme.TextGray, 12f, Alpha);
            string treasury = siege.HasTaxInfo ? $"{siege.Treasury:N0} Zen" : "-";
            var size = PlayerShopUi.Measure(treasury, 12f);
            PlayerShopUi.DrawText(sb, treasury, new Vector2(body.Right - 10 - size.X, body.Y + 80), ModernHudTheme.TextGold, 12f, Alpha);
            PlayerShopUi.DrawText(sb, "Only the castle owner's guild master can change taxes", new Vector2(body.X + 10, body.Bottom - 44), ModernHudTheme.TextGray, 11f, Alpha);
            PlayerShopUi.DrawText(sb, "or withdraw Zen; taxes cannot change during the battle.", new Vector2(body.X + 10, body.Bottom - 28), ModernHudTheme.TextGray, 11f, Alpha);
        }

        private void DrawTaxRow(SpriteBatch sb, Rectangle body, int row, string label, string value)
        {
            float y = body.Y + 15 + row * 32;
            PlayerShopUi.DrawText(sb, label, new Vector2(body.X + 10, y), ModernHudTheme.TextGray, 12f, Alpha);
            PlayerShopUi.DrawText(sb, value, new Vector2(body.Right - 130, y), ModernHudTheme.TextWhite, 12f, Alpha);
            for (int i = 0; i < 2; i++)
            {
                int index = row * 2 + i;
                PlayerShopUi.DrawButton(sb, Translate(TaxButtonLocal(index)), i == 0 ? "-" : "+", _hoveredTaxButton == index, true, ModernHudTheme.Accent, Alpha, 13f);
            }
        }

        protected override void DrawOverlay(SpriteBatch sb, GameTime gameTime)
        {
            if (_tab == Tab.Taxes) DrawPlaceholder(sb, _withdrawField, "Zen to withdraw");
        }

        private void DrawDefenses(SpriteBatch sb, Rectangle body, CastleSiegeManager siege)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            var header = new Rectangle(body.X + 1, body.Y + 1, body.Width - 2, 20);
            sb.Draw(pixel, header, ModernHudTheme.BgMid * Alpha);
            int nameX = body.X + 8, posX = body.X + 110, hpX = body.X + 175, levelX = body.Right - 70;
            PlayerShopUi.DrawText(sb, "Structure", new Vector2(nameX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);
            PlayerShopUi.DrawText(sb, "Pos", new Vector2(posX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);
            PlayerShopUi.DrawText(sb, "HP", new Vector2(hpX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);
            PlayerShopUi.DrawText(sb, "Def/Reg", new Vector2(levelX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);

            var npcs = siege.DefenseNpcs;
            if (npcs.Count == 0)
            {
                PlayerShopUi.DrawTextCentered(sb, "No data (castle owner's guild master only).", new Rectangle(body.X, body.Y + body.Height / 2 - 8, body.Width, 16),
                    ModernHudTheme.TextGray, 12f, Alpha);
                return;
            }

            for (int i = 0; i < VisibleRows && i < npcs.Count; i++)
            {
                var npc = npcs[i];
                var row = Translate(RowLocal(i));
                if (i == _selected) sb.Draw(pixel, row, ModernHudTheme.Accent * 0.35f * Alpha);
                else if (i == _hoveredRow) sb.Draw(pixel, row, Color.White * 0.06f * Alpha);
                Color color = npc.IsAlive ? ModernHudTheme.TextWhite : ModernHudTheme.Danger;
                PlayerShopUi.DrawText(sb, $"{NpcName(npc)} #{npc.Index}", new Vector2(nameX, row.Y + 3), color, 12f, Alpha);
                PlayerShopUi.DrawText(sb, $"{npc.X},{npc.Y}", new Vector2(posX, row.Y + 3), ModernHudTheme.TextGray, 12f, Alpha);
                string hp = npc.IsAlive ? $"{npc.CurrentHp:N0}/{npc.MaxHp:N0}" : "Destroyed";
                PlayerShopUi.DrawText(sb, PlayerShopUi.Ellipsize(hp, 12f, levelX - hpX - 4), new Vector2(hpX, row.Y + 3), color, 12f, Alpha);
                PlayerShopUi.DrawText(sb, $"{npc.DefenseLevel}/{npc.RegenerationLevel}", new Vector2(levelX, row.Y + 3), ModernHudTheme.TextGray, 12f, Alpha);
            }
        }

        private static string NpcName(CastleSiegeManager.DefenseNpc npc) => npc.Number == 277 ? "Gate" : "Statue";
    }
}
