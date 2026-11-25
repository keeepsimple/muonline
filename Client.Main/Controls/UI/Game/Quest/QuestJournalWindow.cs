#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Core.Client;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Game.Quest
{
    /// <summary>
    /// Quest journal (hotkey T): active Season 6 quests (F6) with conditions, rewards and a cancel button.
    /// Opening it sends ActiveQuestListRequest; the QuestStateList answer makes QuestHandler request each QuestState.
    /// Quest text files are not loaded, so quests are named by group and number.
    /// </summary>
    public sealed class QuestJournalWindow : UIControl
    {
        private const int Pad = 14;
        private const int HeaderHeight = 48;
        private const int WindowWidth = 340;
        private const int BodyWidth = WindowWidth - Pad * 2;
        private const int RowHeight = 24;
        private const int VisibleRows = 6;
        private const int ListY = HeaderHeight + 8;
        private const int ListHeight = VisibleRows * RowHeight + 8;
        private const int DetailY = ListY + ListHeight + 8;
        private const int DetailHeight = 190;
        private const int ButtonY = DetailY + DetailHeight + 12;
        private const int ButtonHeight = 32;

        private static readonly Rectangle CloseLocal = new(WindowWidth - 34, 12, 22, 22);
        private static readonly Rectangle ListLocal = new(Pad, ListY, BodyWidth, ListHeight);
        private static readonly Rectangle DetailLocal = new(Pad, DetailY, BodyWidth, DetailHeight);
        private static readonly Rectangle CancelLocal = new(Pad, ButtonY, BodyWidth, ButtonHeight);

        public readonly record struct Row(QuestLog.QuestId Id, string Title, string Status, bool Ready);
        public readonly record struct Line(string Text, string Value, bool Met);

        private readonly QuestLog _log;
        private ushort? _selectedGroup;
        private int _scroll;
        private int _hoveredRow = -1;
        private bool _closeHovered;
        private bool _cancelHovered;
        private bool _isDragging;
        private Point _dragOffset;

        public QuestJournalWindow(QuestLog log)
        {
            _log = log;
            AutoViewSize = false;
            Interactive = true;
            ControlSize = ViewSize = new Point(WindowWidth, ButtonY + ButtonHeight + Pad);
            Align = ControlAlign.Right | ControlAlign.VerticalCenter;
            Margin = new Margin { Right = 10 };
            Visible = false;
        }

        // ───────────── view model (pure, tested in tests/QuestPacketChecks) ─────────────

        public static List<Row> BuildRows(QuestLog log) => log.ActiveQuests.Values
            .OrderBy(q => q.Id.Group)
            .Select(q => new Row(q.Id, $"Quest {q.Id.Group}/{q.Id.Number}", Ready(q) ? "Ready" : $"Step {q.Id.Number}", Ready(q)))
            .ToList();

        public static List<Line> ConditionLines(QuestLog.Quest quest) => quest.Conditions
            .Select(c => new Line(QuestLog.ConditionText(c), $"{Math.Min(c.CurrentCount, c.RequiredCount)}/{c.RequiredCount}", c.IsMet))
            .ToList();

        public static List<string> RewardLines(QuestLog.Quest quest) => quest.Rewards.Select(QuestLog.RewardText).ToList();

        /// <summary>The selected group if it is still active, otherwise the first active quest.</summary>
        public static QuestLog.Quest? ResolveSelection(QuestLog log, ushort? selectedGroup)
        {
            if (selectedGroup is { } g && log.ActiveQuests.TryGetValue(g, out var quest)) return quest;
            return log.ActiveQuests.Values.OrderBy(q => q.Id.Group).FirstOrDefault();
        }

        private static bool Ready(QuestLog.Quest q) => q.Conditions.Count > 0 && q.AllConditionsMet;

        // ───────────── show / hide ─────────────

        public void Toggle()
        {
            if (Visible) Hide();
            else Show();
        }

        public void Show()
        {
            Visible = true;
            BringToFront();
            _ = MuGame.Network?.GetCharacterService()?.SendActiveQuestListRequestAsync();
        }

        public void Hide()
        {
            _isDragging = false;
            Visible = false;
        }

        private void ConfirmCancel(QuestLog.QuestId id)
        {
            RequestDialog.Show(
                $"Cancel quest {id.Group}/{id.Number}?\nIts progress will be lost.",
                onAccept: () => _ = MuGame.Network?.GetCharacterService()?.SendQuestCancelRequestAsync(id.Number, id.Group),
                acceptText: "Cancel quest",
                rejectText: "Keep");
        }

        private Rectangle Translate(Rectangle local)
            => new(DisplayRectangle.X + local.X, DisplayRectangle.Y + local.Y, local.Width, local.Height);

        private Rectangle RowRect(int visibleIndex)
            => Translate(new Rectangle(ListLocal.X + 4, ListLocal.Y + 4 + visibleIndex * RowHeight, ListLocal.Width - 8, RowHeight - 2));

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (!Visible) return;

            if (MuGame.Instance.Keyboard.IsKeyDown(Keys.Escape) && MuGame.Instance.PrevKeyboard.IsKeyUp(Keys.Escape))
            {
                Scene?.ConsumeKeyboardEscape();
                Hide();
                return;
            }

            var mouse = MuGame.Instance.UiMouseState;
            var previous = MuGame.Instance.PrevUiMouseState;
            Point position = mouse.Position;
            bool leftDown = mouse.LeftButton == ButtonState.Pressed;
            bool leftPressed = leftDown && previous.LeftButton == ButtonState.Released;

            if (_isDragging)
            {
                if (leftDown) { X = position.X - _dragOffset.X; Y = position.Y - _dragOffset.Y; }
                else _isDragging = false;
                return;
            }

            var rows = BuildRows(_log);
            _scroll = Math.Clamp(_scroll, 0, Math.Max(0, rows.Count - VisibleRows));
            if (Translate(ListLocal).Contains(position) && mouse.ScrollWheelValue != previous.ScrollWheelValue)
                _scroll = Math.Clamp(_scroll - Math.Sign(mouse.ScrollWheelValue - previous.ScrollWheelValue), 0, Math.Max(0, rows.Count - VisibleRows));

            var selected = ResolveSelection(_log, _selectedGroup);
            _closeHovered = Translate(CloseLocal).Contains(position);
            _cancelHovered = selected != null && Translate(CancelLocal).Contains(position);
            _hoveredRow = -1;
            for (int i = 0; i < VisibleRows && _scroll + i < rows.Count; i++)
                if (RowRect(i).Contains(position)) _hoveredRow = _scroll + i;

            if (!DisplayRectangle.Contains(position) || !leftPressed) return;
            Scene?.SetMouseInputConsumed();

            if (_closeHovered) { Hide(); return; }
            if (_hoveredRow >= 0) { _selectedGroup = rows[_hoveredRow].Id.Group; return; }
            if (_cancelHovered && selected != null) { ConfirmCancel(selected.Id); return; }
            if (Translate(new Rectangle(0, 0, WindowWidth, HeaderHeight)).Contains(position))
            {
                _isDragging = true;
                _dragOffset = new Point(position.X - X, position.Y - Y);
                Align = ControlAlign.None;
            }
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready) return;
            var sb = GraphicsManager.Instance.Sprite;
            if (sb == null || GraphicsManager.Instance.Pixel == null) return;

            var rows = BuildRows(_log);
            var selected = ResolveSelection(_log, _selectedGroup);

            var scope = PlayerShopUi.BeginUiBatch();
            try
            {
                PlayerShopUi.DrawWindow(sb, DisplayRectangle, Alpha);
                PlayerShopUi.DrawHeader(sb, Translate(new Rectangle(0, 0, WindowWidth, HeaderHeight)), "QUESTS", rows.Count > 0 ? $"{rows.Count} active" : null, Alpha);
                PlayerShopUi.DrawCloseButton(sb, Translate(CloseLocal), _closeHovered, Alpha);

                var list = Translate(ListLocal);
                PlayerShopUi.DrawPanel(sb, list, ModernHudTheme.BgDarkest, Alpha);
                if (rows.Count == 0)
                    PlayerShopUi.DrawTextCentered(sb, "No active quests.", new Rectangle(list.X, list.Y + list.Height / 2 - 9, list.Width, 18), ModernHudTheme.TextGray, 12f, Alpha);

                for (int i = 0; i < VisibleRows && _scroll + i < rows.Count; i++)
                {
                    var row = rows[_scroll + i];
                    var rect = RowRect(i);
                    bool isSelected = selected != null && row.Id == selected.Id;
                    if (isSelected || _hoveredRow == _scroll + i)
                        sb.Draw(GraphicsManager.Instance.Pixel, rect, (isSelected ? ModernHudTheme.SlotSelected : ModernHudTheme.SlotHover) * Alpha);
                    PlayerShopUi.DrawText(sb, row.Title, new Vector2(rect.X + 8, rect.Y + 4), isSelected ? ModernHudTheme.TextGold : ModernHudTheme.TextWhite, 12f, Alpha);
                    var size = PlayerShopUi.Measure(row.Status, 11f);
                    PlayerShopUi.DrawText(sb, row.Status, new Vector2(rect.Right - 8 - size.X, rect.Y + 5), row.Ready ? ModernHudTheme.Success : ModernHudTheme.TextGray, 11f, Alpha);
                }

                if (rows.Count > VisibleRows)
                    PlayerShopUi.DrawText(sb, $"{_scroll + 1}-{Math.Min(rows.Count, _scroll + VisibleRows)}/{rows.Count}", new Vector2(list.Right - 52, list.Bottom - 2), ModernHudTheme.TextGray, 10f, Alpha);

                var detail = Translate(DetailLocal);
                PlayerShopUi.DrawPanel(sb, detail, ModernHudTheme.BgDarkest, Alpha);
                if (selected != null) DrawDetail(sb, detail, selected);

                PlayerShopUi.DrawButton(sb, Translate(CancelLocal), "CANCEL QUEST", _cancelHovered, selected != null, ModernHudTheme.Danger, Alpha, 13f);
            }
            finally
            {
                scope?.Dispose();
            }

            base.Draw(gameTime);
        }

        private void DrawDetail(SpriteBatch sb, Rectangle body, QuestLog.Quest quest)
        {
            float y = body.Y + 8;
            float maxWidth = body.Width - 80;
            PlayerShopUi.DrawText(sb, "Conditions", new Vector2(body.X + 10, y), ModernHudTheme.TextGold, 12f, Alpha);
            y += 20;
            var conditions = ConditionLines(quest);
            if (conditions.Count == 0)
            {
                PlayerShopUi.DrawText(sb, "-", new Vector2(body.X + 14, y), ModernHudTheme.TextGray, 11f, Alpha);
                y += 18;
            }

            foreach (var line in conditions)
            {
                PlayerShopUi.DrawText(sb, PlayerShopUi.Ellipsize(line.Text, 11f, maxWidth), new Vector2(body.X + 14, y), ModernHudTheme.TextWhite, 11f, Alpha);
                var size = PlayerShopUi.Measure(line.Value, 11f);
                PlayerShopUi.DrawText(sb, line.Value, new Vector2(body.Right - 12 - size.X, y), line.Met ? ModernHudTheme.Success : ModernHudTheme.Warning, 11f, Alpha);
                y += 18;
            }

            y += 6;
            sb.Draw(GraphicsManager.Instance.Pixel, new Rectangle(body.X + 10, (int)y, body.Width - 20, 1), ModernHudTheme.BorderInner * 0.35f * Alpha);
            y += 6;
            PlayerShopUi.DrawText(sb, "Rewards", new Vector2(body.X + 10, y), ModernHudTheme.TextGold, 12f, Alpha);
            y += 20;
            var rewards = RewardLines(quest);
            if (rewards.Count == 0)
                PlayerShopUi.DrawText(sb, "-", new Vector2(body.X + 14, y), ModernHudTheme.TextGray, 11f, Alpha);
            foreach (var reward in rewards)
            {
                if (y > body.Bottom - 16) break; // ponytail: 5 conditions + 5 rewards overflow the panel; scroll when real quests need it
                PlayerShopUi.DrawText(sb, PlayerShopUi.Ellipsize(reward, 11f, body.Width - 24), new Vector2(body.X + 14, y), ModernHudTheme.TextWhite, 11f, Alpha);
                y += 18;
            }
        }
    }
}
