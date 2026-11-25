#nullable enable
using System;
using System.Collections.Generic;
using Client.Main.Controls.UI.Game.Friends;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using static Client.Main.Core.Client.CastleSiegeManager;

namespace Client.Main.Controls.UI.Game.CastleSiege
{
    /// <summary>
    /// Castle Siege window of the Guardsman NPC (NpcWindow 13): castle owner, siege period, own guild registration,
    /// registered guilds (or the selected participants once chosen); register guild, register Sign of Lord, give up.
    /// </summary>
    public sealed class CastleSiegeWindow : MessengerWindowBase
    {
        private const int Width = 380;
        private const int InfoY = HeaderHeight + 8;
        private const int InfoRowHeight = 22;
        private const int InfoRows = 6;
        private const int ListY = InfoY + InfoRows * InfoRowHeight + 14;
        private const int RowHeight = 20;
        private const int VisibleRows = 8;
        private const int ListHeight = 22 + RowHeight * VisibleRows;
        private const int ButtonY = ListY + ListHeight + 12;
        private const int Height = ButtonY + 32 + Pad;

        // Sign of Lord (item 14/21, level 3), see CastleSiegeInitializer.
        private const byte SignGroup = 14;
        private const short SignNumber = 21;
        private const int SignLevel = 3;
        private const byte FirstInventorySlot = 12;

        private static CastleSiegeWindow? _instance;
        public static CastleSiegeWindow Instance => _instance ??= new CastleSiegeWindow();
        public override bool NonDisposable => true;

        private int _scroll;
        private int _hoveredButton = -1;

        private CastleSiegeWindow() : base(Width, Height)
        {
            Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;
        }

        private static CastleSiegeManager? Siege => MuGame.Network?.GetCastleSiegeManager();

        protected override string Title => "CASTLE SIEGE";
        protected override string? Subtitle => Siege is { HasStatus: true } s ? GetStateName(s.State) : "Loading...";

        public override void Show()
        {
            _scroll = 0;
            base.Show();
            RequestData();
        }

        public override void Hide()
        {
            base.Hide();
            // The Guardsman keeps the server-side NPC dialog open until the window closes.
            var service = MuGame.Network?.GetCharacterService();
            if (service?.IsNpcDialogOpen == true) _ = service.SendCloseNpcRequestAsync();
        }

        public void RequestData()
        {
            var service = MuGame.Network?.GetCastleSiegeService();
            if (service == null) return;
            _ = service.SendStatusRequestAsync();
            _ = service.SendOwnerLogoRequestAsync();
            _ = service.SendRegistrationStateRequestAsync();
            _ = service.SendRegisteredGuildListRequestAsync();
            _ = service.SendParticipantListRequestAsync();
        }

        // ─────────────────────────────── Buttons ───────────────────────────────

        private readonly record struct ButtonSpec(string Text, bool Enabled, Color Accent, Action OnClick);

        private List<ButtonSpec> GetButtons()
        {
            var siege = Siege;
            bool known = siege is { HasStatus: true };
            var state = siege?.State ?? SiegeState.Idle1;
            bool registered = siege?.IsRegistered == true;
            return new List<ButtonSpec>(4)
            {
                new("REGISTER", !known || (state == SiegeState.RegisterGuild && !registered), ModernHudTheme.Accent, ConfirmRegister),
                new("SIGN OF LORD", !known || (state == SiegeState.RegisterMark && registered), ModernHudTheme.Accent, RegisterSign),
                new("GIVE UP", !known || (state == SiegeState.RegisterGuild && registered), ModernHudTheme.Danger, ConfirmGiveUp),
                new("REFRESH", true, ModernHudTheme.Accent, RequestData),
            };
        }

        private static Rectangle ButtonLocal(int index, int count)
        {
            int width = (Width - Pad * 2 - (count - 1) * 6) / count;
            return new Rectangle(Pad + index * (width + 6), ButtonY, width, 32);
        }

        private static void ConfirmRegister()
            => RequestDialog.Show("Register your guild for the next Castle Siege?",
                onAccept: () => _ = MuGame.Network?.GetCastleSiegeService()?.SendRegisterGuildAsync());

        private static void ConfirmGiveUp()
            => RequestDialog.Show("Give up your guild's Castle Siege registration? Registered Signs of Lord are lost.",
                onAccept: () => _ = MuGame.Network?.GetCastleSiegeService()?.SendUnregisterGuildAsync());

        private static void RegisterSign()
        {
            var state = MuGame.Network?.GetCharacterState();
            if (state == null) return;
            foreach (var (slot, data) in state.GetInventoryItems())
            {
                if (slot < FirstInventorySlot
                    || !ItemDatabase.TryGetItemGroupAndNumber(data, out byte group, out short number)
                    || group != SignGroup || number != SignNumber
                    || ItemDatabase.ParseItemDetails(data).Level != SignLevel)
                    continue;

                _ = MuGame.Network?.GetCastleSiegeService()?.SendRegisterMarkAsync(slot);
                return;
            }

            RequestDialog.ShowInfo("You have no Sign of Lord in your inventory.");
        }

        // ─────────────────────────────── Update ───────────────────────────────

        public override void Update(GameTime gameTime)
        {
            // Walking away ends the NPC conversation (GameSceneWindowCloseController): close the window with it.
            if (Visible && MuGame.Network?.GetCharacterService()?.IsNpcDialogOpen == false)
            {
                Hide();
                return;
            }

            base.Update(gameTime);
        }

        protected override void UpdateContent(GameTime gameTime, bool mouseInside, int wheel)
        {
            int total = ListCount();
            if (wheel != 0) _scroll -= Math.Sign(wheel) * 2;
            _scroll = Math.Clamp(_scroll, 0, Math.Max(0, total - VisibleRows));

            var buttons = GetButtons();
            _hoveredButton = -1;
            for (int i = 0; i < buttons.Count; i++)
                if (Translate(ButtonLocal(i, buttons.Count)).Contains(MousePosition)) _hoveredButton = i;
        }

        protected override bool HandleClick()
        {
            var buttons = GetButtons();
            if (_hoveredButton < 0 || _hoveredButton >= buttons.Count || !buttons[_hoveredButton].Enabled) return false;
            buttons[_hoveredButton].OnClick();
            return true;
        }

        private static int ListCount()
        {
            var siege = Siege;
            if (siege == null) return 0;
            return siege.Participants.Count > 0 ? siege.Participants.Count : siege.RegisteredGuilds.Count;
        }

        // ─────────────────────────────── Draw ───────────────────────────────

        protected override void DrawContent(SpriteBatch sb, GameTime gameTime)
        {
            var siege = Siege;
            if (siege == null) return;

            DrawOwnerMark(sb, Translate(OwnerMarkLocal), Alpha);
            var info = Translate(new Rectangle(Pad, InfoY, Width - Pad * 2, InfoRows * InfoRowHeight + 4));
            PlayerShopUi.DrawPanel(sb, info, ModernHudTheme.BgDarkest, Alpha);
            float y = info.Y + 4;
            string owner = string.IsNullOrEmpty(siege.OwnerGuild) ? "None"
                : string.IsNullOrEmpty(siege.OwnerGuildMaster) ? siege.OwnerGuild : $"{siege.OwnerGuild} ({siege.OwnerGuildMaster})";
            DrawInfo(sb, info, ref y, "Castle owner", owner, ModernHudTheme.TextGold);
            DrawInfo(sb, info, ref y, "Current period", siege.HasStatus ? GetStateName(siege.State) : "-", ModernHudTheme.TextWhite);
            DrawInfo(sb, info, ref y, "Period", siege.HasStatus ? $"{FormatDate(siege.StateStart)} - {FormatDate(siege.StateEnd)}" : "-", ModernHudTheme.TextWhite);
            DrawInfo(sb, info, ref y, "Siege battle", siege.HasStatus ? FormatDate(siege.SiegeStart) : "-", ModernHudTheme.TextWhite);
            DrawInfo(sb, info, ref y, "Time left", siege.HasStatus ? FormatTime(siege.Remaining) : "-", ModernHudTheme.TextWhite);
            string own = siege.IsRegistered switch
            {
                true => $"{siege.RegisteredGuildName}: rank {siege.RegistrationRank}, {siege.Marks} mark(s)",
                false => "Not registered",
                _ => "-",
            };
            DrawInfo(sb, info, ref y, "Your guild", own, siege.IsRegistered == true ? ModernHudTheme.Success : ModernHudTheme.TextGray);

            DrawList(sb, siege);

            var buttons = GetButtons();
            for (int i = 0; i < buttons.Count; i++)
            {
                var spec = buttons[i];
                PlayerShopUi.DrawButton(sb, Translate(ButtonLocal(i, buttons.Count)), spec.Text, _hoveredButton == i, spec.Enabled, spec.Accent, Alpha, 11f);
            }
        }

        private void DrawInfo(SpriteBatch sb, Rectangle body, ref float y, string label, string value, Color color)
        {
            PlayerShopUi.DrawText(sb, label, new Vector2(body.X + 10, y), ModernHudTheme.TextGray, 12f, Alpha);
            string shown = PlayerShopUi.Ellipsize(value, 12f, body.Width - 120);
            var size = PlayerShopUi.Measure(shown, 12f);
            PlayerShopUi.DrawText(sb, shown, new Vector2(body.Right - 10 - size.X, y), color, 12f, Alpha);
            y += InfoRowHeight;
        }

        private void DrawList(SpriteBatch sb, CastleSiegeManager siege)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            var list = Translate(new Rectangle(Pad, ListY, Width - Pad * 2, ListHeight));
            PlayerShopUi.DrawPanel(sb, list, ModernHudTheme.BgDarkest, Alpha);
            var header = new Rectangle(list.X + 1, list.Y + 1, list.Width - 2, 20);
            sb.Draw(pixel, header, ModernHudTheme.BgMid * Alpha);

            bool participants = siege.Participants.Count > 0;
            int rankX = list.X + 8, nameX = list.X + 34, valueX = list.X + 190, extraX = list.Right - 80;
            PlayerShopUi.DrawText(sb, "#", new Vector2(rankX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);
            PlayerShopUi.DrawText(sb, participants ? "Participating guild" : "Registered guild", new Vector2(nameX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);
            PlayerShopUi.DrawText(sb, participants ? "Side" : "Marks", new Vector2(valueX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);
            PlayerShopUi.DrawText(sb, participants ? "Score" : "", new Vector2(extraX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);

            int total = ListCount();
            if (total == 0)
            {
                PlayerShopUi.DrawTextCentered(sb, "No guild has registered yet.", new Rectangle(list.X, list.Y + list.Height / 2 - 8, list.Width, 16),
                    ModernHudTheme.TextGray, 12f, Alpha);
                return;
            }

            string ownGuild = siege.IsRegistered == true ? siege.RegisteredGuildName : string.Empty;
            for (int i = 0; i < VisibleRows && _scroll + i < total; i++)
            {
                int index = _scroll + i;
                var row = new Rectangle(list.X + 1, list.Y + 22 + i * RowHeight, list.Width - 2, RowHeight);
                string name, value, extra = string.Empty;
                if (participants)
                {
                    var p = siege.Participants[index];
                    name = p.IsAllianceMaster ? p.Name + " *" : p.Name;
                    value = GetSideName(p.Side);
                    extra = p.Score.ToString("N0");
                }
                else
                {
                    var g = siege.RegisteredGuilds[index];
                    name = g.IsGivingUp ? g.Name + " (gave up)" : g.Name;
                    value = g.Marks.ToString("N0");
                }

                bool own = !participants && siege.RegisteredGuilds[index].Name == ownGuild;
                if (own) sb.Draw(pixel, row, ModernHudTheme.Accent * 0.35f * Alpha);
                else if (i % 2 == 1) sb.Draw(pixel, row, Color.White * 0.025f * Alpha);
                Color color = own ? new Color(200, 255, 0) : ModernHudTheme.TextWhite;
                PlayerShopUi.DrawText(sb, (index + 1).ToString(), new Vector2(rankX, row.Y + 3), color, 12f, Alpha);
                PlayerShopUi.DrawText(sb, PlayerShopUi.Ellipsize(name, 12f, valueX - nameX - 6), new Vector2(nameX, row.Y + 3), color, 12f, Alpha);
                PlayerShopUi.DrawText(sb, value, new Vector2(valueX, row.Y + 3), color, 12f, Alpha);
                if (extra.Length > 0) PlayerShopUi.DrawText(sb, extra, new Vector2(extraX, row.Y + 3), ModernHudTheme.TextGold, 12f, Alpha);
            }

            DrawScrollBar(sb, new Rectangle(list.X, list.Y + 22, list.Width, RowHeight * VisibleRows), _scroll, VisibleRows, total);
        }

        /// <summary>Header left, mirroring the close button on the right.</summary>
        internal static readonly Rectangle OwnerMarkLocal = new(Pad, 12, 22, 22);

        /// <summary>Castle owner's guild mark (0xB9/0x02), drawn like the guild window's mark.</summary>
        internal static void DrawOwnerMark(SpriteBatch sb, Rectangle rect, float alpha)
        {
            if (Siege is not { OwnerGuild.Length: > 0, OwnerGuildMark: { } mark }) return;
            // ponytail: reuses the guild mark cache under a reserved id; collides only with a guild of id uint.MaxValue.
            var texture = GuildMarkTextureCache.Get(new GuildInfo { Id = uint.MaxValue, Mark = mark });
            using var scope = new SpriteBatchScope(sb, SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullNone, null, UiScaler.SpriteTransform);
            GuildMarkTextureCache.Draw(sb, texture, rect, alpha);
        }

        internal static string FormatDate(DateTime date) => date == DateTime.MinValue ? "-" : date.ToString("MM/dd HH:mm");

        internal static string FormatTime(TimeSpan time)
            => time.TotalDays >= 1 ? $"{(int)time.TotalDays}d {time.Hours:D2}:{time.Minutes:D2}" : $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}";
    }
}
