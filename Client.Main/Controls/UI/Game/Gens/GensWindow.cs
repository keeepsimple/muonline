#nullable enable
using System;
using System.Collections.Generic;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Core.Client;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MUnique.OpenMU.Network.Packets.ClientToServer;

namespace Client.Main.Controls.UI.Game.Gens
{
    /// <summary>
    /// Gens window: family, rank, ranking position and contribution of the own character (GensInfo 0xF8 07).
    /// Opened with the hotkey for info only, or by the Duprian / Vanert NPC with join / leave / reward buttons.
    /// OpenMU sends no list of other players: there is no ranking table to show.
    /// </summary>
    public sealed class GensWindow : UIControl
    {
        private const int Pad = 14;
        private const int HeaderHeight = 48;
        private const int WindowWidth = 300;
        private const int BodyY = HeaderHeight + 8;
        private const int BodyHeight = 150;
        private const int ButtonY = BodyY + BodyHeight + 12;
        private const int ButtonHeight = 32;
        private const int BodyWidth = WindowWidth - Pad * 2;

        private static readonly Rectangle CloseLocal = new(WindowWidth - 34, 12, 22, 22);
        private static readonly Rectangle BodyLocal = new(Pad, BodyY, BodyWidth, BodyHeight);

        private readonly GensManager _gens;
        private GensType _npcGens = GensType.Undefined; // gens of the opened NPC, Undefined when opened by hotkey
        private string _message = string.Empty;
        private int _hoveredButton = -1;
        private bool _closeHovered;
        private bool _isDragging;
        private Point _dragOffset;

        public GensWindow(GensManager gens)
        {
            _gens = gens;
            AutoViewSize = false;
            Interactive = true;
            ControlSize = ViewSize = new Point(WindowWidth, ButtonY + ButtonHeight + Pad);
            Align = ControlAlign.Right | ControlAlign.VerticalCenter;
            Margin = new Margin { Right = 10 };
            Visible = false;
            _gens.NpcDialogRequested += ShowForNpc;
            _gens.ResultReceived += OnResult;
        }

        public override void Dispose()
        {
            _gens.NpcDialogRequested -= ShowForNpc;
            _gens.ResultReceived -= OnResult;
            base.Dispose();
        }

        public void Toggle()
        {
            if (Visible) Hide();
            else Show(GensType.Undefined);
        }

        private void ShowForNpc(GensType npcGens) => Show(npcGens);

        private void Show(GensType npcGens)
        {
            _npcGens = npcGens;
            _message = string.Empty;
            Visible = true;
            BringToFront();
            _ = MuGame.Network?.GetCharacterService()?.SendGensRankingRequestAsync();
        }

        public void Hide()
        {
            _isDragging = false;
            Visible = false;
            // The server keeps the player in its NpcDialogOpened state until the dialog is closed.
            var service = MuGame.Network?.GetCharacterService();
            if (_npcGens != GensType.Undefined && service?.IsNpcDialogOpen == true)
                _ = service.SendCloseNpcRequestAsync();
            _npcGens = GensType.Undefined;
        }

        private void OnResult(string text)
        {
            if (Visible) _message = text;
        }

        private Rectangle Translate(Rectangle local)
            => new(DisplayRectangle.X + local.X, DisplayRectangle.Y + local.Y, local.Width, local.Height);

        private static Rectangle ButtonLocal(int index, int count)
        {
            int width = (BodyWidth - (count - 1) * 6) / count;
            return new Rectangle(Pad + index * (width + 6), ButtonY, width, ButtonHeight);
        }

        private readonly record struct ButtonSpec(string Text, Color Accent, Action OnClick);

        private List<ButtonSpec> GetButtons()
        {
            var buttons = new List<ButtonSpec>(2);
            var service = MuGame.Network?.GetCharacterService();
            if (_npcGens == GensType.Undefined || service == null) return buttons;

            if (!_gens.IsMember)
            {
                var gens = _npcGens;
                buttons.Add(new ButtonSpec($"JOIN {gens.ToString().ToUpperInvariant()}", ModernHudTheme.Accent, () => _ = service.SendGensJoinRequestAsync(gens)));
            }
            else
            {
                buttons.Add(new ButtonSpec("LEAVE GENS", ModernHudTheme.Danger, () => RequestDialog.Show("Do you really want to leave the gens?",
                    onAccept: () => _ = service.SendGensLeaveRequestAsync())));
                buttons.Add(new ButtonSpec("GET REWARD", ModernHudTheme.Accent, () => _ = service.SendGensRewardRequestAsync(_gens.Gens)));
            }

            return buttons;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (!Visible) return;

            // The conversation ended (e.g. the hero walked away): the server then ignores join / leave / reward,
            // so the window drops to info-only mode.
            if (_npcGens != GensType.Undefined && MuGame.Network?.GetCharacterService()?.IsNpcDialogOpen == false)
                _npcGens = GensType.Undefined;

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

            _closeHovered = Translate(CloseLocal).Contains(position);
            var buttons = GetButtons();
            _hoveredButton = -1;
            for (int i = 0; i < buttons.Count; i++)
                if (Translate(ButtonLocal(i, buttons.Count)).Contains(position)) _hoveredButton = i;

            if (!DisplayRectangle.Contains(position) || !leftPressed) return;
            Scene?.SetMouseInputConsumed();

            if (_closeHovered) { Hide(); return; }
            if (_hoveredButton >= 0) { buttons[_hoveredButton].OnClick(); return; }
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

            var scope = PlayerShopUi.BeginUiBatch();
            try
            {
                PlayerShopUi.DrawWindow(sb, DisplayRectangle, Alpha);
                PlayerShopUi.DrawHeader(sb, Translate(new Rectangle(0, 0, WindowWidth, HeaderHeight)), "GENS", _gens.IsMember ? _gens.Gens.ToString() : null, Alpha);
                PlayerShopUi.DrawCloseButton(sb, Translate(CloseLocal), _closeHovered, Alpha);

                var body = Translate(BodyLocal);
                PlayerShopUi.DrawPanel(sb, body, ModernHudTheme.BgDarkest, Alpha);
                if (!_gens.IsMember)
                {
                    PlayerShopUi.DrawTextCentered(sb, "You are not a member of a gens.", new Rectangle(body.X, body.Y + 40, body.Width, 18), ModernHudTheme.TextWhite, 13f, Alpha);
                    PlayerShopUi.DrawTextCentered(sb, "Talk to Duprian or Vanert to join.", new Rectangle(body.X, body.Y + 64, body.Width, 16), ModernHudTheme.TextGray, 11f, Alpha);
                }
                else
                {
                    float y = body.Y + 12;
                    DrawInfoLine(sb, body, ref y, "Family", _gens.Gens.ToString(), ModernHudTheme.TextGold);
                    DrawInfoLine(sb, body, ref y, "Rank", _gens.Rank.ToString(), ModernHudTheme.TextWhite);
                    DrawInfoLine(sb, body, ref y, "Ranking position", _gens.RankingPosition == 0 ? "-" : _gens.RankingPosition.ToString(), ModernHudTheme.TextWhite);
                    DrawInfoLine(sb, body, ref y, "Contribution", _gens.Contribution.ToString(), ModernHudTheme.TextWhite);
                    DrawInfoLine(sb, body, ref y, "Next rank needs", _gens.NextRankContribution == 0 ? "-" : _gens.NextRankContribution.ToString(), ModernHudTheme.TextGray);
                }

                if (_message.Length > 0)
                    PlayerShopUi.DrawTextCentered(sb, PlayerShopUi.Ellipsize(_message, 11f, body.Width - 8), new Rectangle(body.X, body.Bottom - 22, body.Width, 16), ModernHudTheme.TextGold, 11f, Alpha);

                var buttons = GetButtons();
                for (int i = 0; i < buttons.Count; i++)
                    PlayerShopUi.DrawButton(sb, Translate(ButtonLocal(i, buttons.Count)), buttons[i].Text, _hoveredButton == i, true, buttons[i].Accent, Alpha, 13f);
            }
            finally
            {
                scope?.Dispose();
            }

            base.Draw(gameTime);
        }

        private void DrawInfoLine(SpriteBatch sb, Rectangle body, ref float y, string label, string value, Color valueColor)
        {
            sb.Draw(GraphicsManager.Instance.Pixel, new Rectangle(body.X + 10, (int)y + 20, body.Width - 20, 1), ModernHudTheme.BorderInner * 0.35f * Alpha);
            PlayerShopUi.DrawText(sb, label, new Vector2(body.X + 12, y), ModernHudTheme.TextGray, 12f, Alpha);
            var size = PlayerShopUi.Measure(value, 12f);
            PlayerShopUi.DrawText(sb, value, new Vector2(body.Right - 12 - size.X, y), valueColor, 12f, Alpha);
            y += 26;
        }
    }
}
