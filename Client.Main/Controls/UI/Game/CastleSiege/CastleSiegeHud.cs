#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Core.Client;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using static Client.Main.Core.Client.CastleSiegeManager;

namespace Client.Main.Controls.UI.Game.CastleSiege
{
    /// <summary>
    /// Castle Siege battle HUD shown in Valley of Loren during Ready/Start: state timer, own side, crown switch holders,
    /// crown state and the last commander order. OpenMU announces no state changes, so the status is polled.
    /// </summary>
    public sealed class CastleSiegeHud : UIControl
    {
        private const int Width = 250;
        private const int LineHeight = 17;
        private const int MaxLines = 9;
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

        private static CastleSiegeHud? _instance;
        public static CastleSiegeHud Instance => _instance ??= new CastleSiegeHud();
        public override bool NonDisposable => true;

        private readonly List<(string Text, Color Color)> _lines = new(MaxLines);
        private DateTime _nextPoll;

        private CastleSiegeHud()
        {
            AutoViewSize = false;
            Interactive = false;
            ControlSize = ViewSize = new Point(Width, 30 + LineHeight * MaxLines);
            // Top-right event-HUD column, below the FPS/ping labels and above the letter notifier (Top 220);
            // the top-left belongs to the party panel.
            Align = ControlAlign.Top | ControlAlign.Right;
            Margin = new Margin { Top = 36, Right = 10 };
            Visible = false;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            var network = MuGame.Network;
            var siege = network?.GetCastleSiegeManager();
            bool onSiegeMap = network?.GetCharacterState().MapId == SiegeMapId;
            if (siege == null || !onSiegeMap)
            {
                Visible = false;
                return;
            }

            if (network!.IsConnected && DateTime.UtcNow >= _nextPoll)
            {
                _nextPoll = DateTime.UtcNow + PollInterval;
                _ = network.GetCastleSiegeService().SendStatusRequestAsync();
            }

            Visible = siege.Side != JoinSide.None
                      || (siege.HasStatus && siege.State is SiegeState.Ready or SiegeState.Start);
            if (Visible) BuildLines(siege);
        }

        private void BuildLines(CastleSiegeManager siege)
        {
            _lines.Clear();
            string state = siege.HasStatus ? GetStateName(siege.State) : "Castle Siege";
            _lines.Add(($"{state}  {CastleSiegeWindow.FormatTime(siege.Remaining)}", ModernHudTheme.TextGold));
            _lines.Add(($"Your side: {GetSideName(siege.Side)}", siege.Side == JoinSide.Defense ? ModernHudTheme.Success : ModernHudTheme.Danger));

            int number = 1;
            foreach (var pair in siege.Switches.OrderBy(p => p.Key))
            {
                var info = pair.Value;
                string holder = info.IsOccupied
                    ? $"{info.UserName}{(info.GuildName.Length > 0 ? " [" + info.GuildName + "]" : "")} ({GetSideName(info.Side)})"
                    : "free";
                _lines.Add(($"Switch {number++}: {holder}", info.IsOccupied ? SideColor(siege, info.Side) : ModernHudTheme.TextGray));
            }

            if (siege.CrownAccessible is { } accessible)
                _lines.Add(($"Crown: {(accessible ? "unprotected" : "protected")}", accessible ? ModernHudTheme.Warning : ModernHudTheme.TextWhite));
            if (siege.CrownAccessState == CrownAccess.Started)
                _lines.Add(($"Crown registration: {siege.CrownAccumulatedMs / 1000f:0.0} s", ModernHudTheme.Warning));

            int building = siege.LifeStoneBuildTime.Values.Count(stage => stage < 5);
            if (building > 0) _lines.Add(($"Life Stones under construction: {building}", ModernHudTheme.TextWhite));
            if (siege.LastCommand.Length > 0) _lines.Add((siege.LastCommand, new Color(255, 200, 80)));
        }

        private static Color SideColor(CastleSiegeManager siege, JoinSide side)
            => side == siege.Side ? ModernHudTheme.Success : ModernHudTheme.Danger;

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready || _lines.Count == 0) return;
            var sb = GraphicsManager.Instance.Sprite;
            if (sb == null || GraphicsManager.Instance.Pixel == null) return;

            var scope = PlayerShopUi.BeginUiBatch();
            try
            {
                int count = Math.Min(_lines.Count, MaxLines);
                var rect = new Rectangle(DisplayRectangle.X, DisplayRectangle.Y, Width, 12 + LineHeight * count);
                PlayerShopUi.DrawPanel(sb, rect, ModernHudTheme.BgDarkest * 0.85f, Alpha);
                for (int i = 0; i < count; i++)
                {
                    var (text, color) = _lines[i];
                    PlayerShopUi.DrawText(sb, PlayerShopUi.Ellipsize(text, 11f, Width - 16), new Vector2(rect.X + 8, rect.Y + 6 + i * LineHeight),
                        color, i == 0 ? 12f : 11f, Alpha);
                }
            }
            finally
            {
                scope?.Dispose();
            }

            base.Draw(gameTime);
        }
    }
}
