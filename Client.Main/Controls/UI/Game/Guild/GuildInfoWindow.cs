#nullable enable
using System;
using System.Collections.Generic;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Core.Client;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MUnique.OpenMU.Network.Packets.ServerToClient;

namespace Client.Main.Controls.UI.Game.Guild
{
    /// <summary>
    /// Guild window (hotkey G), modelled on NewUIGuildInfoWindow.cpp: tabs Guild / Members / Union,
    /// leave or disband on the Guild tab, kick on the Members tab. Drawn with the personal shop window chrome.
    /// </summary>
    public sealed class GuildInfoWindow : UIControl
    {
        private enum Tab { Guild, Members, Union }

        private const int Pad = 14;
        private const int HeaderHeight = 48;
        private const int WindowWidth = 300;
        private const int TabY = HeaderHeight + 8;
        private const int TabHeight = 24;
        private const int BodyY = TabY + TabHeight + 8;
        private const int RowHeight = 20;
        private const int VisibleRows = 13;
        private const int ListHeaderHeight = 20;
        private const int ListHeight = ListHeaderHeight + RowHeight * VisibleRows;
        private const int ButtonY = BodyY + ListHeight + 12;
        private const int WindowHeight = ButtonY + 32 + Pad;
        private const int BodyWidth = WindowWidth - Pad * 2;

        private static readonly Rectangle CloseLocal = new(WindowWidth - 34, 12, 22, 22);
        private static readonly Rectangle BodyLocal = new(Pad, BodyY, BodyWidth, ListHeight);
        private static readonly string[] TabNames = { "GUILD", "MEMBERS", "UNION" };

        private readonly GuildManager _guilds;
        private readonly CharacterState _state;
        private Tab _tab = Tab.Guild;
        private int _scroll;
        private string? _selectedMember;
        private string? _selectedAlliance;
        private ushort? _selectedTarget;
        private IReadOnlyList<AllianceGuildInfo>? _markSource;
        private readonly List<GuildInfo> _allianceMarks = new();
        private int _hoveredTab = -1;
        private int _hoveredRow = -1;
        private int _hoveredButton = -1;
        private bool _closeHovered;
        private bool _isDragging;
        private Point _dragOffset;

        public GuildInfoWindow(GuildManager guilds, CharacterState state)
        {
            _guilds = guilds;
            _state = state;
            AutoViewSize = false;
            Interactive = true;
            ControlSize = ViewSize = new Point(WindowWidth, WindowHeight);
            Align = ControlAlign.Right | ControlAlign.VerticalCenter;
            Margin = new Margin { Right = 10 };
            Visible = false;
            _guilds.GuildInfoChanged += OnGuildChanged;
        }

        public override void Dispose()
        {
            _guilds.GuildInfoChanged -= OnGuildChanged;
            base.Dispose();
        }

        private void OnGuildChanged()
        {
            // Joined, left or the guild was disbanded while the window is open: refresh the member list.
            MuGame.ScheduleOnMainThread(() =>
            {
                if (Visible && _guilds.HasGuild)
                    RequestData();
            });
        }

        // ─────────────────────────────── Visibility ───────────────────────────────

        public void Show()
        {
            _scroll = 0;
            _selectedMember = null;
            _selectedAlliance = null;
            _selectedTarget = null;
            Visible = true;
            BringToFront();
            RequestData();
            if (_tab == Tab.Union) RequestAllianceList();
        }

        public void Hide()
        {
            _isDragging = false;
            Visible = false;
        }

        public void Toggle()
        {
            if (Visible) Hide();
            else Show();
        }

        private void RequestData()
        {
            if (!_guilds.HasGuild) return;
            var service = MuGame.Network?.GetCharacterService();
            if (service == null) return;
            _ = service.SendGuildListRequestAsync();
            if (_guilds.ShouldRequestGuild(_guilds.OwnGuildId))
                _ = service.SendGuildInfoRequestAsync(_guilds.OwnGuildId);
        }

        private void RequestAllianceList()
        {
            if (_guilds.HasGuild) _ = MuGame.Network?.GetCharacterService()?.SendAllianceListRequestAsync();
        }

        private Rectangle Translate(Rectangle local)
            => new(DisplayRectangle.X + local.X, DisplayRectangle.Y + local.Y, local.Width, local.Height);

        private static Rectangle TabLocal(int index)
        {
            int width = (BodyWidth - 8) / 3;
            return new Rectangle(Pad + index * (width + 4), TabY, width, TabHeight);
        }

        private static Rectangle ButtonLocal(int index, int count)
        {
            int width = (BodyWidth - (count - 1) * 6) / count;
            return new Rectangle(Pad + index * (width + 6), ButtonY, width, 32);
        }

        private static Rectangle RowLocal(int visibleIndex)
            => new(Pad + 1, BodyY + ListHeaderHeight + visibleIndex * RowHeight, BodyWidth - 2, RowHeight);

        // ─────────────────────────────── Buttons ───────────────────────────────

        private readonly record struct ButtonSpec(string Text, bool Enabled, Color Accent, Action OnClick);

        private List<ButtonSpec> GetButtons()
        {
            var buttons = new List<ButtonSpec>(3);
            if (!_guilds.HasGuild) return buttons;

            switch (_tab)
            {
                case Tab.Guild:
                    buttons.Add(_guilds.IsGuildMaster
                        ? new ButtonSpec("DISBAND GUILD", true, ModernHudTheme.Danger, ConfirmDisband)
                        : new ButtonSpec("LEAVE GUILD", true, ModernHudTheme.Danger, ConfirmLeave));
                    break;
                case Tab.Members:
                    var selected = GetSelectedMember();
                    bool canKick = _guilds.IsGuildMaster && selected != null && selected.Role != GuildMemberRole.GuildMaster;
                    // Position appointment (0xE1) is not implemented by OpenMU 0.9.8.
                    buttons.Add(new ButtonSpec("APPOINT", false, ModernHudTheme.Accent, () => { }));
                    buttons.Add(new ButtonSpec("DISMISS", false, ModernHudTheme.Accent, () => { }));
                    buttons.Add(new ButtonSpec("KICK", canKick, ModernHudTheme.Danger, ConfirmKick));
                    break;
                case Tab.Union when _guilds.IsGuildMaster:
                    var target = GetSelectedTarget();
                    bool endHostility = target != null && GetHostilityRequestType(target.Value.GuildName, _guilds.RivalGuildName) == RequestLeave;
                    string? removable = GetRemovableAllianceGuild(_guilds, _selectedAlliance);
                    buttons.Add(new ButtonSpec("ALLIANCE", target != null, ModernHudTheme.Accent, () => SendRelationshipRequest(RelationshipAlliance)));
                    buttons.Add(new ButtonSpec(endHostility ? "END HOSTILITY" : "HOSTILITY", target != null, ModernHudTheme.Danger,
                        () => SendRelationshipRequest(RelationshipHostility)));
                    buttons.Add(new ButtonSpec(removable != null && removable == _guilds.OwnGuild?.Name ? "LEAVE" : "REMOVE", removable != null,
                        ModernHudTheme.Danger, ConfirmRemoveAlliance));
                    break;
            }

            return buttons;
        }

        private GuildMemberInfo? GetSelectedMember()
        {
            if (_selectedMember == null) return null;
            foreach (var member in _guilds.GetMembers())
                if (member.Name == _selectedMember) return member;
            return null;
        }

        private string OwnName => _state.Name ?? string.Empty;

        private void ConfirmLeave()
        {
            string guild = _guilds.OwnGuild?.Name ?? "the guild";
            RequestDialog.Show($"Do you really want to leave {guild}?",
                onAccept: () => SendKick(OwnName));
        }

        private void ConfirmDisband()
        {
            string guild = _guilds.OwnGuild?.Name ?? "your guild";
            RequestDialog.Show($"Do you really want to disband {guild}? All members will be removed.",
                onAccept: () => SendKick(OwnName));
        }

        private void ConfirmKick()
        {
            if (GetSelectedMember() is not { } member) return;
            RequestDialog.Show($"Remove {member.Name} from the guild?",
                onAccept: () => SendKick(member.Name));
        }

        private static void SendKick(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            var service = MuGame.Network?.GetCharacterService();
            if (service != null) _ = service.SendGuildKickPlayerAsync(name, string.Empty);
        }

        // ─────────────────────────────── Union (alliance / hostility) ───────────────────────────────

        // Server GuildRelationshipType / GuildRequestType values (0xE5).
        public const byte RelationshipAlliance = 1;
        public const byte RelationshipHostility = 2;
        public const byte RequestJoin = 1;
        public const byte RequestLeave = 2;

        private const int AllianceRows = 7; // S6 alliance limit
        private const int TargetHeaderRow = AllianceRows;
        private const int TargetRows = VisibleRows - AllianceRows - 1;

        public readonly record struct UnionTarget(ushort PlayerId, string PlayerName, string GuildName);

        /// <summary>Guild masters of other guilds in scope (0xE5 needs the target player id); unknown names are skipped.</summary>
        public static List<UnionTarget> GetUnionTargets(GuildManager guilds, Func<ushort, string?> playerName)
        {
            var targets = new List<UnionTarget>();
            foreach (var (id, guildId) in guilds.GetForeignGuildMastersInScope())
            {
                string guild = guilds.GetGuildName(guildId);
                if (playerName(id) is { Length: > 0 } name && guild.Length > 0)
                    targets.Add(new UnionTarget(id, name, guild));
            }
            return targets;
        }

        /// <summary>Requesting hostility against the current rival guild ends it instead.</summary>
        public static byte GetHostilityRequestType(string targetGuild, string rivalGuild)
            => rivalGuild.Length > 0 && targetGuild == rivalGuild ? RequestLeave : RequestJoin;

        /// <summary>The own guild can always leave; other guilds can only be removed by the alliance master (0xEB 0x01).</summary>
        public static string? GetRemovableAllianceGuild(GuildManager guilds, string? selected)
        {
            if (!guilds.IsGuildMaster || string.IsNullOrEmpty(selected)) return null;
            bool listed = false;
            foreach (var guild in guilds.AllianceGuilds) listed |= guild.Name == selected;
            if (!listed) return null;
            return selected == guilds.OwnGuild?.Name || guilds.IsAllianceMaster ? selected : null;
        }

        private List<UnionTarget> GetUnionTargets()
        {
            var scope = MuGame.Network?.GetScopeManager();
            var targets = GetUnionTargets(_guilds, id => scope != null && scope.TryGetScopeObjectName(id, out var name) ? name : null);
            if (targets.Count > TargetRows) targets.RemoveRange(TargetRows, targets.Count - TargetRows); // ponytail: no scrolling, nearest few are enough
            return targets;
        }

        private UnionTarget? GetSelectedTarget()
        {
            if (_selectedTarget == null) return null;
            foreach (var target in GetUnionTargets())
                if (target.PlayerId == _selectedTarget) return target;
            return null;
        }

        private void SendRelationshipRequest(byte relationship)
        {
            if (GetSelectedTarget() is not { } target) return;
            byte request = relationship == RelationshipHostility ? GetHostilityRequestType(target.GuildName, _guilds.RivalGuildName) : RequestJoin;
            _ = MuGame.Network?.GetCharacterService()?.SendGuildRelationshipChangeRequestAsync(relationship, request, target.PlayerId);
        }

        private void ConfirmRemoveAlliance()
        {
            if (GetRemovableAllianceGuild(_guilds, _selectedAlliance) is not { } guild) return;
            string text = guild == _guilds.OwnGuild?.Name ? "Do you really want to leave the alliance?" : $"Remove {guild} from the alliance?";
            RequestDialog.Show(text, onAccept: () => _ = MuGame.Network?.GetCharacterService()?.SendRemoveAllianceGuildAsync(guild));
        }

        // ─────────────────────────────── Update ───────────────────────────────

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

            var members = _guilds.GetMembers();
            int maxScroll = Math.Max(0, members.Count - VisibleRows);
            if (_tab == Tab.Members && Translate(BodyLocal).Contains(position) && mouse.ScrollWheelValue != previous.ScrollWheelValue)
                _scroll -= Math.Sign(mouse.ScrollWheelValue - previous.ScrollWheelValue) * 3;
            _scroll = Math.Clamp(_scroll, 0, maxScroll);

            _closeHovered = Translate(CloseLocal).Contains(position);
            _hoveredTab = -1;
            for (int i = 0; i < TabNames.Length; i++)
                if (Translate(TabLocal(i)).Contains(position)) _hoveredTab = i;

            _hoveredRow = -1;
            if (_tab == Tab.Members)
            {
                for (int i = 0; i < VisibleRows && _scroll + i < members.Count; i++)
                    if (Translate(RowLocal(i)).Contains(position)) _hoveredRow = _scroll + i;
            }
            else if (_tab == Tab.Union)
            {
                for (int i = 0; i < VisibleRows; i++)
                    if (Translate(RowLocal(i)).Contains(position)) _hoveredRow = i;
                int allianceCount = Math.Min(AllianceRows, _guilds.AllianceGuilds.Count);
                int targetCount = _guilds.IsGuildMaster ? GetUnionTargets().Count : 0;
                if (!(_hoveredRow < allianceCount || (_hoveredRow > TargetHeaderRow && _hoveredRow - TargetHeaderRow - 1 < targetCount)))
                    _hoveredRow = -1;
            }

            var buttons = GetButtons();
            _hoveredButton = -1;
            for (int i = 0; i < buttons.Count; i++)
                if (Translate(ButtonLocal(i, buttons.Count)).Contains(position)) _hoveredButton = i;

            if (!DisplayRectangle.Contains(position) || !leftPressed) return;
            Scene?.SetMouseInputConsumed();

            if (_closeHovered) { Hide(); return; }
            if (_hoveredTab >= 0)
            {
                var tab = (Tab)_hoveredTab;
                if (tab != _tab)
                {
                    _tab = tab;
                    _scroll = 0;
                    if (tab == Tab.Members) RequestData();
                    if (tab == Tab.Union) RequestAllianceList();
                }
                return;
            }
            if (_hoveredRow >= 0 && _tab == Tab.Union)
            {
                if (_hoveredRow < AllianceRows) _selectedAlliance = _guilds.AllianceGuilds[_hoveredRow].Name;
                else _selectedTarget = GetUnionTargets()[_hoveredRow - TargetHeaderRow - 1].PlayerId;
                return;
            }
            if (_hoveredRow >= 0)
            {
                _selectedMember = members[_hoveredRow].Name;
                return;
            }
            if (_hoveredButton >= 0 && buttons[_hoveredButton].Enabled)
            {
                buttons[_hoveredButton].OnClick();
                return;
            }
            if (Translate(new Rectangle(0, 0, WindowWidth, HeaderHeight)).Contains(position))
            {
                _isDragging = true;
                _dragOffset = new Point(position.X - X, position.Y - Y);
                Align = ControlAlign.None;
            }
        }

        // ─────────────────────────────── Draw ───────────────────────────────

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready) return;
            var sb = GraphicsManager.Instance.Sprite;
            if (sb == null || GraphicsManager.Instance.Pixel == null) return;

            var guild = _guilds.OwnGuild;
            var scope = PlayerShopUi.BeginUiBatch();
            try
            {
                PlayerShopUi.DrawWindow(sb, DisplayRectangle, Alpha);
                string? subtitle = _guilds.HasGuild ? guild?.Name ?? "Loading..." : null;
                PlayerShopUi.DrawHeader(sb, Translate(new Rectangle(0, 0, WindowWidth, HeaderHeight)), "GUILD", subtitle, Alpha);
                PlayerShopUi.DrawCloseButton(sb, Translate(CloseLocal), _closeHovered, Alpha);

                for (int i = 0; i < TabNames.Length; i++)
                {
                    bool active = (int)_tab == i;
                    PlayerShopUi.DrawButton(sb, Translate(TabLocal(i)), TabNames[i], _hoveredTab == i || active, true,
                        active ? ModernHudTheme.AccentBright : ModernHudTheme.Accent, Alpha, 12f);
                }

                var body = Translate(BodyLocal);
                PlayerShopUi.DrawPanel(sb, body, ModernHudTheme.BgDarkest, Alpha);

                if (!_guilds.HasGuild)
                    DrawNoGuild(sb, body);
                else if (_tab == Tab.Guild)
                    DrawGuildTab(sb, body, guild);
                else if (_tab == Tab.Members)
                    DrawMembersTab(sb, body);
                else
                    DrawUnionTab(sb, body, guild);

                var buttons = GetButtons();
                for (int i = 0; i < buttons.Count; i++)
                {
                    var spec = buttons[i];
                    PlayerShopUi.DrawButton(sb, Translate(ButtonLocal(i, buttons.Count)), spec.Text, _hoveredButton == i, spec.Enabled, spec.Accent, Alpha, 13f);
                }
            }
            finally
            {
                scope?.Dispose();
            }

            // The 8x8 mark is upscaled: draw it with point sampling.
            if (_guilds.HasGuild && _tab == Tab.Guild && guild != null)
            {
                using var markScope = new SpriteBatchScope(sb, SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp,
                    DepthStencilState.None, RasterizerState.CullNone, null, UiScaler.SpriteTransform);
                GuildMarkTextureCache.Draw(sb, GuildMarkTextureCache.Get(guild), MarkRect(Translate(BodyLocal)), Alpha);
            }
            else if (_guilds.HasGuild && _tab == Tab.Union)
            {
                using var markScope = new SpriteBatchScope(sb, SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp,
                    DepthStencilState.None, RasterizerState.CullNone, null, UiScaler.SpriteTransform);
                var marks = GetAllianceMarks();
                for (int i = 0; i < marks.Count && i < AllianceRows; i++)
                {
                    var row = Translate(RowLocal(i));
                    GuildMarkTextureCache.Draw(sb, GuildMarkTextureCache.Get(marks[i]), new Rectangle(row.X + 6, row.Y + 2, 16, 16), Alpha);
                }
            }

            base.Draw(gameTime);
        }

        private static Rectangle MarkRect(Rectangle body) => new(body.X + 12, body.Y + 14, 64, 64);

        private void DrawNoGuild(SpriteBatch sb, Rectangle body)
        {
            PlayerShopUi.DrawTextCentered(sb, "You are not a member of a guild.", new Rectangle(body.X, body.Y + body.Height / 2 - 30, body.Width, 18),
                ModernHudTheme.TextWhite, 13f, Alpha);
            PlayerShopUi.DrawTextCentered(sb, "Talk to the Guild Master NPC to create one,", new Rectangle(body.X, body.Y + body.Height / 2 - 4, body.Width, 16),
                ModernHudTheme.TextGray, 11f, Alpha);
            PlayerShopUi.DrawTextCentered(sb, "or ask a guild master to accept you.", new Rectangle(body.X, body.Y + body.Height / 2 + 12, body.Width, 16),
                ModernHudTheme.TextGray, 11f, Alpha);
        }

        private void DrawGuildTab(SpriteBatch sb, Rectangle body, GuildInfo? guild)
        {
            var mark = MarkRect(body);
            float x = mark.Right + 14;
            float maxWidth = body.Right - x - 8;
            PlayerShopUi.DrawText(sb, PlayerShopUi.Ellipsize(guild?.Name ?? "Loading...", 16f, maxWidth), new Vector2(x, mark.Y), ModernHudTheme.TextGold, 16f, Alpha);
            PlayerShopUi.DrawText(sb, GuildManager.GetRoleName(_guilds.OwnRole), new Vector2(x, mark.Y + 24), new Color(200, 255, 100), 12f, Alpha);

            var members = _guilds.GetMembers();
            string master = "-";
            int online = 0;
            foreach (var member in members)
            {
                if (member.Role == GuildMemberRole.GuildMaster) master = member.Name;
                if (member.IsOnline) online++;
            }

            float y = mark.Bottom + 22;
            DrawInfoLine(sb, body, ref y, "Guild Master", master, ModernHudTheme.TextWhite);
            DrawInfoLine(sb, body, ref y, "Members", members.Count > 0 ? $"{members.Count} ({online} online)" : "-", ModernHudTheme.TextWhite);
            DrawInfoLine(sb, body, ref y, "Score", _guilds.TotalScore.ToString(), ModernHudTheme.TextWhite);
            string rival = string.IsNullOrEmpty(_guilds.RivalGuildName) ? "None" : _guilds.RivalGuildName;
            DrawInfoLine(sb, body, ref y, "Rival guild", rival, string.IsNullOrEmpty(_guilds.RivalGuildName) ? ModernHudTheme.TextGray : new Color(255, 30, 0));
            string alliance = string.IsNullOrEmpty(guild?.AllianceName) ? "None" : guild!.AllianceName;
            DrawInfoLine(sb, body, ref y, "Alliance", alliance, string.IsNullOrEmpty(guild?.AllianceName) ? ModernHudTheme.TextGray : ModernHudTheme.TextWhite);
        }

        private void DrawInfoLine(SpriteBatch sb, Rectangle body, ref float y, string label, string value, Color valueColor)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            sb.Draw(pixel, new Rectangle(body.X + 10, (int)y + 20, body.Width - 20, 1), ModernHudTheme.BorderInner * 0.35f * Alpha);
            PlayerShopUi.DrawText(sb, label, new Vector2(body.X + 12, y), ModernHudTheme.TextGray, 12f, Alpha);
            string shown = PlayerShopUi.Ellipsize(value, 12f, body.Width - 130);
            var size = PlayerShopUi.Measure(shown, 12f);
            PlayerShopUi.DrawText(sb, shown, new Vector2(body.Right - 12 - size.X, y), valueColor, 12f, Alpha);
            y += 26;
        }

        private void DrawMembersTab(SpriteBatch sb, Rectangle body)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            int nameX = body.X + 10, roleX = body.X + 130, serverX = body.Right - 54;
            var header = new Rectangle(body.X + 1, body.Y + 1, body.Width - 2, ListHeaderHeight - 1);
            sb.Draw(pixel, header, ModernHudTheme.BgMid * Alpha);
            PlayerShopUi.DrawText(sb, "Name", new Vector2(nameX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);
            PlayerShopUi.DrawText(sb, "Position", new Vector2(roleX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);
            PlayerShopUi.DrawText(sb, "Server", new Vector2(serverX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);

            var members = _guilds.GetMembers();
            if (members.Count == 0)
            {
                PlayerShopUi.DrawTextCentered(sb, "Loading member list...", new Rectangle(body.X, body.Y + body.Height / 2 - 8, body.Width, 16),
                    ModernHudTheme.TextGray, 12f, Alpha);
                return;
            }

            for (int i = 0; i < VisibleRows && _scroll + i < members.Count; i++)
            {
                var member = members[_scroll + i];
                var row = Translate(RowLocal(i));
                bool selected = member.Name == _selectedMember;
                if (selected) sb.Draw(pixel, row, ModernHudTheme.Accent * 0.35f * Alpha);
                else if (_hoveredRow == _scroll + i) sb.Draw(pixel, row, Color.White * 0.06f * Alpha);
                else if (i % 2 == 1) sb.Draw(pixel, row, Color.White * 0.025f * Alpha);

                Color nameColor = member.IsOnline ? (member.Name == OwnName ? new Color(200, 255, 0) : ModernHudTheme.TextWhite) : ModernHudTheme.TextGray * 0.8f;
                PlayerShopUi.DrawText(sb, PlayerShopUi.Ellipsize(member.Name, 12f, roleX - nameX - 6), new Vector2(nameX, row.Y + 3), nameColor, 12f, Alpha);
                Color roleColor = member.Role == GuildMemberRole.GuildMaster ? ModernHudTheme.TextGold
                                : member.Role == GuildMemberRole.BattleMaster ? new Color(255, 160, 70)
                                : ModernHudTheme.TextGray;
                PlayerShopUi.DrawText(sb, GuildManager.GetRoleName(member.Role), new Vector2(roleX, row.Y + 3), roleColor, 11f, Alpha);
                string server = member.IsOnline ? $"GS {member.ServerId + 1}" : "Offline";
                PlayerShopUi.DrawText(sb, server, new Vector2(serverX, row.Y + 3), member.IsOnline ? ModernHudTheme.Success : ModernHudTheme.TextGray * 0.7f, 11f, Alpha);
            }

            if (members.Count > VisibleRows)
            {
                int trackHeight = RowHeight * VisibleRows;
                int thumbHeight = Math.Max(16, trackHeight * VisibleRows / members.Count);
                int maxScroll = members.Count - VisibleRows;
                int thumbY = body.Y + ListHeaderHeight + (trackHeight - thumbHeight) * _scroll / Math.Max(1, maxScroll);
                sb.Draw(pixel, new Rectangle(body.Right - 4, body.Y + ListHeaderHeight, 2, trackHeight), ModernHudTheme.BorderInner * 0.4f * Alpha);
                sb.Draw(pixel, new Rectangle(body.Right - 5, thumbY, 4, thumbHeight), ModernHudTheme.AccentBright * Alpha);
            }
        }

        /// <summary>
        /// GuildMarkTextureCache is keyed by guild id; alliance entries have none, so they get synthetic ids per list slot.
        /// ponytail: ids 0xFFFFFF00+ assumed unused by real guilds.
        /// </summary>
        private List<GuildInfo> GetAllianceMarks()
        {
            var source = _guilds.AllianceGuilds;
            if (!ReferenceEquals(source, _markSource))
            {
                _markSource = source;
                _allianceMarks.Clear();
                for (int i = 0; i < source.Count; i++)
                    _allianceMarks.Add(new GuildInfo { Id = 0xFFFFFF00u + (uint)i, Name = source[i].Name, Mark = source[i].Mark });
            }
            return _allianceMarks;
        }

        private void DrawListHeader(SpriteBatch sb, Rectangle header, string left, string right)
        {
            sb.Draw(GraphicsManager.Instance.Pixel, header, ModernHudTheme.BgMid * Alpha);
            PlayerShopUi.DrawText(sb, left, new Vector2(header.X + 9, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);
            var size = PlayerShopUi.Measure(right, 11f);
            PlayerShopUi.DrawText(sb, right, new Vector2(header.Right - 10 - size.X, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);
        }

        private void DrawUnionRowBackground(SpriteBatch sb, Rectangle row, int index, bool selected)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (selected) sb.Draw(pixel, row, ModernHudTheme.Accent * 0.35f * Alpha);
            else if (_hoveredRow == index) sb.Draw(pixel, row, Color.White * 0.06f * Alpha);
            else if (index % 2 == 1) sb.Draw(pixel, row, Color.White * 0.025f * Alpha);
        }

        private void DrawUnionTab(SpriteBatch sb, Rectangle body, GuildInfo? guild)
        {
            string title = string.IsNullOrEmpty(guild?.AllianceName) ? "Alliance" : $"Alliance: {guild!.AllianceName}";
            DrawListHeader(sb, new Rectangle(body.X + 1, body.Y + 1, body.Width - 2, ListHeaderHeight - 1), PlayerShopUi.Ellipsize(title, 11f, body.Width - 90), "Members");

            var alliance = _guilds.AllianceGuilds;
            if (alliance.Count == 0)
                PlayerShopUi.DrawTextCentered(sb, "Your guild is not part of an alliance.", Translate(RowLocal(1)), ModernHudTheme.TextGray, 12f, Alpha);
            for (int i = 0; i < alliance.Count && i < AllianceRows; i++)
            {
                var entry = alliance[i];
                var row = Translate(RowLocal(i));
                DrawUnionRowBackground(sb, row, i, entry.Name == _selectedAlliance);
                bool master = entry.Name == guild?.AllianceName;
                PlayerShopUi.DrawText(sb, entry.Name, new Vector2(row.X + 30, row.Y + 3), master ? ModernHudTheme.TextGold : ModernHudTheme.TextWhite, 12f, Alpha);
                string count = entry.MemberCount.ToString();
                PlayerShopUi.DrawText(sb, count, new Vector2(row.Right - 10 - PlayerShopUi.Measure(count, 12f).X, row.Y + 3), ModernHudTheme.TextWhite, 12f, Alpha);
            }

            if (!_guilds.IsGuildMaster) return;

            var headerRow = Translate(RowLocal(TargetHeaderRow));
            DrawListHeader(sb, headerRow, "Guild masters nearby", "Guild");
            var targets = GetUnionTargets();
            if (targets.Count == 0)
                PlayerShopUi.DrawTextCentered(sb, "Stand near another guild master to send a request.", Translate(RowLocal(TargetHeaderRow + 1)),
                    ModernHudTheme.TextGray, 11f, Alpha);
            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                int index = TargetHeaderRow + 1 + i;
                var row = Translate(RowLocal(index));
                DrawUnionRowBackground(sb, row, index, target.PlayerId == _selectedTarget);
                PlayerShopUi.DrawText(sb, target.PlayerName, new Vector2(row.X + 9, row.Y + 3), ModernHudTheme.TextWhite, 12f, Alpha);
                bool rival = target.GuildName == _guilds.RivalGuildName;
                PlayerShopUi.DrawText(sb, target.GuildName, new Vector2(row.Right - 10 - PlayerShopUi.Measure(target.GuildName, 12f).X, row.Y + 3),
                    rival ? new Color(255, 30, 0) : ModernHudTheme.TextGold, 12f, Alpha);
            }
        }
    }
}
