#nullable enable
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Core.Client;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using MUnique.OpenMU.Network.Packets.ServerToClient;

namespace Client.Main.Controls.UI.Game.Hud
{
    /// <summary>
    /// Guild war / battle soccer scoreboard (0x62-0x64, 0xF3 0x22/0x23), drawn like <see cref="DuelHudControl"/>.
    /// Hidden while <see cref="GetView"/> returns null.
    /// </summary>
    public sealed class GuildWarHudControl : UIControl
    {
        public readonly record struct View(string Title, string Left, string Right, int LeftScore, int RightScore, string Timer);

        private const int WIDTH = 320;
        private const int HEIGHT = 74;
        private const int HEADER_HEIGHT = 26;
        private const int PANEL_PADDING = 10;

        private readonly GuildManager _guilds;

        public GuildWarHudControl(GuildManager guilds)
        {
            _guilds = guilds;
            // ponytail: same slot as the duel HUD; both at once would overlap, stack them if that ever happens.
            Align = ControlAlign.Top | ControlAlign.HorizontalCenter;
            Margin = new Margin { Top = 8 };
            AutoViewSize = false;
            ViewSize = ControlSize = new Point(WIDTH, HEIGHT);
            Interactive = false;
            BackgroundColor = Color.Transparent;
            BorderColor = Color.Transparent;
            Visible = false;
        }

        /// <summary>The soccer scoreboard wins over the war score: during a soccer war both are sent.</summary>
        public static View? GetView(GuildManager guilds)
        {
            string timer = guilds.SoccerRemainingSeconds > 0 ? $"{guilds.SoccerRemainingSeconds / 60}:{guilds.SoccerRemainingSeconds % 60:00}" : string.Empty;
            if (guilds.SoccerScoreVisible)
                return new View("BATTLE SOCCER", guilds.SoccerRedTeam, guilds.SoccerBlueTeam, guilds.SoccerRedGoals, guilds.SoccerBlueGoals, timer);
            if (!guilds.IsInWar)
                return null;
            string own = guilds.OwnGuild?.Name is { Length: > 0 } name ? name : "Your guild";
            return new View(guilds.WarType == GuildWarType.Soccer ? "BATTLE SOCCER" : "GUILD WAR",
                own, guilds.WarEnemyGuild, guilds.WarOwnScore, guilds.WarEnemyScore, timer);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            Visible = GetView(_guilds) != null;
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready) return;
            var sb = GraphicsManager.Instance?.Sprite;
            var pixel = GraphicsManager.Instance?.Pixel;
            if (sb == null || pixel == null || GetView(_guilds) is not { } view) return;

            var scope = PlayerShopUi.BeginUiBatch();
            try
            {
                var rect = DisplayRectangle;
                sb.Draw(pixel, rect, ModernHudTheme.BorderOuter * Alpha);
                var inner = new Rectangle(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4);
                UiDrawHelper.DrawVerticalGradient(sb, inner, ModernHudTheme.BgDark * Alpha, ModernHudTheme.BgDarkest * Alpha);
                UiDrawHelper.DrawCornerAccents(sb, rect, ModernHudTheme.Accent * 0.35f * Alpha, size: 10, thickness: 2);

                var header = new Rectangle(rect.X + 8, rect.Y + 6, WIDTH - 16, HEADER_HEIGHT);
                UiDrawHelper.DrawPanel(sb, header, ModernHudTheme.BgMid, ModernHudTheme.BorderInner * 0.8f, ModernHudTheme.BorderOuter,
                    ModernHudTheme.BorderHighlight * 0.25f, true, ModernHudTheme.AccentGlow * 0.55f);
                sb.Draw(pixel, new Rectangle(rect.X + 18, rect.Y + 8, WIDTH - 36, 2), ModernHudTheme.Accent * 0.85f * Alpha);

                string title = view.Timer.Length > 0 ? $"{view.Title}  {view.Timer}" : view.Title;
                PlayerShopUi.DrawTextCentered(sb, title, header, ModernHudTheme.TextWhite, 13f, Alpha);

                int x = rect.X + PANEL_PADDING, y = header.Bottom + 10, width = WIDTH - PANEL_PADDING * 2;
                string score = $"{view.LeftScore} : {view.RightScore}";
                var scoreSize = PlayerShopUi.Measure(score, 16f);
                float nameWidth = (width - scoreSize.X) / 2 - 8;
                string left = PlayerShopUi.Ellipsize(view.Left, 13f, nameWidth);
                string right = PlayerShopUi.Ellipsize(view.Right, 13f, nameWidth);
                PlayerShopUi.DrawText(sb, left, new Vector2(x, y + 2), ModernHudTheme.TextGold, 13f, Alpha);
                PlayerShopUi.DrawText(sb, right, new Vector2(x + width - PlayerShopUi.Measure(right, 13f).X, y + 2), new Color(255, 120, 90), 13f, Alpha);
                PlayerShopUi.DrawText(sb, score, new Vector2(x + (width - scoreSize.X) / 2, y), ModernHudTheme.AccentBright, 16f, Alpha);
            }
            finally
            {
                scope?.Dispose();
            }
        }
    }
}
