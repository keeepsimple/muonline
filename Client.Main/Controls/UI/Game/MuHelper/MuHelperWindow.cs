using System;
using System.Collections.Generic;
using System.Linq;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Controls.UI.Game.PauseMenu.Settings;
using Client.Main.Controls.UI.Ornate;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Client.Main.Controls.UI.Game.MuHelper
{
    /// <summary>
    /// MU Helper configuration window (hotkey Home): every option of the S6 helper data blob, grouped in tabs, with
    /// Save (0xAE) and Start / Stop (0xBF 0x51). Rows reuse the option model of the settings window.
    /// </summary>
    internal sealed class MuHelperWindow : OrnateWindowControl
    {
        private const int WindowWidth = 540, WindowHeight = 620;
        private const int Pad = 24, TabY = 62, TabHeight = 34;
        private const int RowsTop = 108, RowHeight = 30, VisibleRows = 13, ControlWidth = 210;
        private const int InputY = RowsTop + VisibleRows * RowHeight + 8, InputHeight = 30;
        private const int ButtonY = WindowHeight - 62;

        private const int HitSave = FirstHitId, HitStart = FirstHitId + 1, HitAdd = FirstHitId + 2, HitField = FirstHitId + 3;
        private const int HitTab = 100, HitRow = 1000;
        private const int PartRow = 0, PartLeft = 1, PartRight = 2, PartCenter = 3;
        private const int PickupTab = 3;

        private static readonly string[] TabNames = { "Hunting", "Skills", "Support", "Pick-up" };
        private static readonly string[] SubConditions = { "2+ monsters", "3+ monsters", "4+ monsters", "5+ monsters" };
        private static readonly string[] RavenModes = { "Cease", "Auto attack", "Attack with me" };

        /// <summary>A list row that runs an action when clicked (named pick-up filters).</summary>
        private sealed class ActionRow : SettingOption
        {
            public Action OnClick { get; init; }
        }

        private readonly TextFieldControl _nameField;
        private MuHelperConfig _edit = new();
        private List<SettingOption> _rows = new();
        private int _tab;
        private int _scroll;
        private bool _dirty;
        private bool _nameFocused;

        public MuHelperWindow() : base(new Point(WindowWidth, WindowHeight))
        {
            Title = "MU HELPER";
            ShowCloseButton = true;
            Visible = false;
            CloseRequested += (_, _) => Hide();

            _nameField = TextFieldControl.Create();
            OrnateWidgets.StyleTextField(_nameField, 13f);
            _nameField.AutoViewSize = false;
            _nameField.X = Pad + 10;
            _nameField.Y = InputY + 3;
            _nameField.ViewSize = _nameField.ControlSize = new Point(WindowWidth - Pad * 2 - 130, InputHeight - 6);
            _nameField.Visible = false;
            _nameField.ValueChanged += (_, _) =>
            {
                string value = new string(_nameField.Value.Where(c => c >= 32 && c < 127).Take(MuHelperConfig.MaxExtraItemNameLength).ToArray());
                if (value != _nameField.Value) _nameField.Value = value;
            };
            _nameField.EnterKeyPressed += (_, _) => MuGame.ScheduleOnMainThread(AddName);
            Controls.Add(_nameField);
        }

        private static CharacterState State => MuGame.Network?.GetCharacterState();

        // ─────────────────────────────── Visibility ───────────────────────────────

        public void Show()
        {
            _edit = MuHelperState.GetConfig(State?.Name).Clone();
            _dirty = false;
            Visible = true;
            BringToFront();
            SelectTab(_tab);
        }

        public void Hide()
        {
            BlurName();
            Visible = false;
        }

        public void Toggle()
        {
            if (Visible) Hide();
            else Show();
        }

        private void SelectTab(int tab)
        {
            _tab = tab;
            _scroll = 0;
            _rows = BuildRows(tab);
            _nameField.Visible = tab == PickupTab;
            if (tab != PickupTab) BlurName();
        }

        private void FocusName()
        {
            _nameFocused = true;
            if (!_nameField.IsFocused) _nameField.Focus();
            if (Scene != null) Scene.FocusControl = _nameField;
        }

        private void BlurName()
        {
            _nameFocused = false;
            _nameField.Blur();
            if (Scene?.FocusControl == _nameField) Scene.FocusControl = null;
        }

        // ─────────────────────────────── Actions ───────────────────────────────

        private void Save()
        {
            _ = MuGame.Network?.GetCharacterService()?.SendMuHelperSaveDataAsync(_edit.ToBytes());
            _dirty = false;
        }

        private void StartOrStop()
        {
            var service = MuGame.Network?.GetCharacterService();
            if (service == null) return;
            if (MuHelperState.IsRunning)
            {
                _ = service.SendMuHelperStatusChangeAsync(true);
                return;
            }

            // The hunt loop runs with the configuration stored on the server; it is echoed before the start reply.
            if (_dirty) Save();
            _ = service.SendMuHelperStatusChangeAsync(false);
        }

        private void AddName()
        {
            string name = _nameField.Value.Trim();
            int free = Array.IndexOf(_edit.ExtraItemNames, string.Empty);
            if (name.Length == 0 || free < 0 || _edit.ExtraItemNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
            _edit.ExtraItemNames[free] = name;
            _edit.PickExtraItems = true;
            _nameField.Value = string.Empty;
            Changed();
        }

        private void RemoveName(int index)
        {
            for (int i = index; i < MuHelperConfig.ExtraItemCount - 1; i++)
                _edit.ExtraItemNames[i] = _edit.ExtraItemNames[i + 1];
            _edit.ExtraItemNames[^1] = string.Empty;
            Changed();
        }

        private void Changed()
        {
            _dirty = true;
            int scroll = _scroll;
            _rows = BuildRows(_tab);
            _scroll = Math.Clamp(scroll, 0, MaxScroll);
        }

        // ─────────────────────────────── Rows ───────────────────────────────

        private List<SettingOption> BuildRows(int tab)
        {
            var c = _edit;
            var rows = new List<SettingOption>();
            switch (tab)
            {
                case 0:
                    rows.Add(Header("Hunting"));
                    rows.Add(Numbers("Hunting range", 1, 15, 1, " tiles", () => c.HuntingRange, v => c.HuntingRange = v));
                    rows.Add(Toggle("Return to start position", () => c.ReturnToOriginalPosition, v => c.ReturnToOriginalPosition = v));
                    rows.Add(Numbers("Max. time away", 0, 300, 5, " s", () => c.MaxSecondsAway, v => c.MaxSecondsAway = v));
                    rows.Add(Toggle("Counter long-range attacks", () => c.LongRangeCounterAttack, v => c.LongRangeCounterAttack = v));
                    rows.Add(Toggle("Self defense", () => c.UseSelfDefense, v => c.UseSelfDefense = v));
                    rows.Add(Header("Dark Raven"));
                    rows.Add(Toggle("Use Dark Raven", () => c.UseDarkRaven, v => c.UseDarkRaven = v));
                    rows.Add(Choice("Raven mode", RavenModes, () => c.DarkRavenMode, v => c.DarkRavenMode = v));
                    rows.Add(Header("Other"));
                    rows.Add(Toggle("Repair items", () => c.RepairItem, v => c.RepairItem = v));
                    rows.Add(Toggle("Accept party: friends", () => c.AutoAcceptFriend, v => c.AutoAcceptFriend = v));
                    rows.Add(Toggle("Accept party: guild", () => c.AutoAcceptGuild, v => c.AutoAcceptGuild = v));
                    break;
                case 1:
                    rows.Add(Header("Attack"));
                    rows.Add(Skill("Basic skill", () => c.BasicSkillId, v => c.BasicSkillId = v));
                    rows.Add(Toggle("Basic attack if skill fails", () => c.FallbackBasicAttack, v => c.FallbackBasicAttack = v));
                    rows.Add(Toggle("Combo (basic, 1, 2)", () => c.UseCombo, v => c.UseCombo = v));
                    rows.Add(Header("Activation skill 1"));
                    rows.Add(Skill("Skill", () => c.ActivationSkill1Id, v => c.ActivationSkill1Id = v));
                    rows.Add(Toggle("Use every", () => c.Skill1UseTimer, v => c.Skill1UseTimer = v));
                    rows.Add(Numbers("Interval", 0, 60, 1, " s", () => c.DelaySkill1, v => c.DelaySkill1 = v));
                    rows.Add(Toggle("Use on condition", () => c.Skill1UseCondition, v => c.Skill1UseCondition = v));
                    rows.Add(Choice("Condition", new[] { "Monsters nearby", "Attacking me" }, () => c.Skill1ConditionAttacking ? 1 : 0, v => c.Skill1ConditionAttacking = v == 1));
                    rows.Add(Choice("Count", SubConditions, () => c.Skill1SubCondition, v => c.Skill1SubCondition = v));
                    rows.Add(Header("Activation skill 2"));
                    rows.Add(Skill("Skill", () => c.ActivationSkill2Id, v => c.ActivationSkill2Id = v));
                    rows.Add(Toggle("Use every", () => c.Skill2UseTimer, v => c.Skill2UseTimer = v));
                    rows.Add(Numbers("Interval", 0, 60, 1, " s", () => c.DelaySkill2, v => c.DelaySkill2 = v));
                    rows.Add(Toggle("Use on condition", () => c.Skill2UseCondition, v => c.Skill2UseCondition = v));
                    rows.Add(Choice("Condition", new[] { "Monsters nearby", "Attacking me" }, () => c.Skill2ConditionAttacking ? 1 : 0, v => c.Skill2ConditionAttacking = v == 1));
                    rows.Add(Choice("Count", SubConditions, () => c.Skill2SubCondition, v => c.Skill2SubCondition = v));
                    break;
                case 2:
                    rows.Add(Header("Recovery"));
                    rows.Add(Toggle("Auto potion", () => c.UseHealPotion, v => c.UseHealPotion = v));
                    rows.Add(Numbers("Potion at HP", 10, 90, 10, "%", () => c.PotionThreshold, v => c.PotionThreshold = v));
                    rows.Add(Toggle("Auto heal (skill)", () => c.AutoHeal, v => c.AutoHeal = v));
                    rows.Add(Numbers("Heal at HP", 10, 90, 10, "%", () => c.HealThreshold, v => c.HealThreshold = v));
                    rows.Add(Toggle("Drain Life", () => c.UseDrainLife, v => c.UseDrainLife = v));
                    rows.Add(Numbers("Drain Life at HP", 10, 90, 10, "%", () => c.DrainLifeThreshold, v => c.DrainLifeThreshold = v));
                    rows.Add(Header("Buffs"));
                    for (int i = 0; i < c.BuffSkillIds.Length; i++)
                    {
                        int slot = i;
                        rows.Add(Skill($"Buff {i + 1}", () => c.BuffSkillIds[slot], v => c.BuffSkillIds[slot] = v));
                    }
                    rows.Add(Toggle("Recast when expired", () => c.BuffOnDuration, v => c.BuffOnDuration = v));
                    rows.Add(Numbers("Recast every", 0, 600, 10, " s", () => c.BuffIntervalSeconds, v => c.BuffIntervalSeconds = v));
                    rows.Add(Header("Party"));
                    rows.Add(Toggle("Support party", () => c.SupportParty, v => c.SupportParty = v));
                    rows.Add(Toggle("Buff party when expired", () => c.BuffDurationForParty, v => c.BuffDurationForParty = v));
                    rows.Add(Toggle("Heal party", () => c.AutoHealParty, v => c.AutoHealParty = v));
                    rows.Add(Numbers("Heal member at HP", 10, 90, 10, "%", () => c.HealPartyThreshold, v => c.HealPartyThreshold = v));
                    break;
                default:
                    rows.Add(Header("Pick-up"));
                    rows.Add(Numbers("Pick-up range", 1, 15, 1, " tiles", () => c.ObtainRange, v => c.ObtainRange = v));
                    rows.Add(Toggle("All items nearby", () => c.PickAllItems, v => c.PickAllItems = v));
                    rows.Add(Toggle("Selected items", () => c.PickSelectItems, v => c.PickSelectItems = v));
                    rows.Add(Toggle("  Jewels / gems", () => c.PickJewel, v => c.PickJewel = v));
                    rows.Add(Toggle("  Excellent items", () => c.PickExcellent, v => c.PickExcellent = v));
                    rows.Add(Toggle("  Ancient items", () => c.PickAncient, v => c.PickAncient = v));
                    rows.Add(Toggle("  Zen", () => c.PickZen, v => c.PickZen = v));
                    rows.Add(Toggle("  Named items", () => c.PickExtraItems, v => c.PickExtraItems = v));
                    rows.Add(Header("Item names (click to remove)"));
                    for (int i = 0; i < c.ExtraItemNames.Length; i++)
                    {
                        int index = i;
                        if (c.ExtraItemNames[i].Length > 0)
                            rows.Add(new ActionRow { Label = c.ExtraItemNames[i], OnClick = () => RemoveName(index) });
                    }
                    break;
            }

            return rows;
        }

        private static SettingHeader Header(string label) => new() { Label = label };

        private static SettingToggle Toggle(string label, Func<bool> get, Action<bool> set)
            => new() { Label = label, Get = get, Set = set };

        private static SettingChoice Choice(string label, IReadOnlyList<string> names, Func<int> get, Action<int> set)
            => new() { Label = label, Choices = names, GetIndex = get, SetIndex = set };

        private static SettingChoice Values(string label, IReadOnlyList<string> names, List<int> values, Func<int> get, Action<int> set)
            => new() { Label = label, Choices = names, GetIndex = () => values.IndexOf(get()), SetIndex = i => set(values[i]) };

        private static SettingChoice Numbers(string label, int min, int max, int step, string suffix, Func<int> get, Action<int> set)
        {
            var values = new List<int>();
            for (int v = min; v <= max; v += step) values.Add(v);
            int current = get();
            if (!values.Contains(current)) { values.Add(current); values.Sort(); } // keep values saved by another client
            return Values(label, values.Select(v => v + suffix).ToList(), values, get, set);
        }

        /// <summary>"None" plus the learned skills (and the saved skill, should it no longer be learned).</summary>
        private static SettingChoice Skill(string label, Func<int> get, Action<int> set)
        {
            var values = new List<int> { 0 };
            if (State != null)
                values.AddRange(State.GetSkills().Select(s => (int)s.SkillId).Distinct().OrderBy(id => id));
            if (!values.Contains(get())) values.Add(get());
            var names = values.Select(id => id == 0 ? "None" : SkillDatabase.GetSkillName(id)).ToList();
            return Values(label, names, values, get, set);
        }

        // ─────────────────────────────── Layout ───────────────────────────────

        private int MaxScroll => Math.Max(0, _rows.Count - VisibleRows);
        private Rectangle TabBounds(int i)
        {
            int width = (WindowWidth - Pad * 2 - 3 * 6) / TabNames.Length;
            return At(Pad + i * (width + 6), TabY, width, TabHeight);
        }
        private Rectangle RowBounds(int visibleIndex) => At(Pad, RowsTop + visibleIndex * RowHeight, WindowWidth - Pad * 2 - (MaxScroll > 0 ? 12 : 0), RowHeight - 2);
        private static Rectangle ControlBounds(Rectangle row) => new(row.Right - ControlWidth - 6, row.Y, ControlWidth, row.Height);
        private Rectangle FieldBounds => At(Pad, InputY, WindowWidth - Pad * 2 - 116, InputHeight);
        private Rectangle AddBounds => At(WindowWidth - Pad - 106, InputY - 2, 106, InputHeight + 4);
        private Rectangle SaveBounds => At(WindowWidth - Pad - 250, ButtonY, 120, 40);
        private Rectangle StartBounds => At(WindowWidth - Pad - 120, ButtonY, 120, 40);

        // ─────────────────────────────── Input ───────────────────────────────

        public override void Update(GameTime gameTime)
        {
            if (Visible && _nameFocused)
            {
                if (!_nameField.IsFocused) _nameField.Focus();
                if (Scene != null && Scene.FocusControl != _nameField) Scene.FocusControl = _nameField;
            }

            base.Update(gameTime);
            if (!Visible) return;

            if (MuGame.Instance.Keyboard.IsKeyDown(Keys.Escape) && MuGame.Instance.PrevKeyboard.IsKeyUp(Keys.Escape))
            {
                Scene?.ConsumeKeyboardEscape();
                Hide();
                return;
            }

            var mouse = MuGame.Instance.UiMouseState;
            if (DisplayRectangle.Contains(mouse.Position) && mouse.LeftButton == ButtonState.Pressed)
                Scene?.SetMouseInputConsumed();
        }

        public override bool ProcessMouseScroll(int scrollDelta)
        {
            if (!Visible) return false;
            _scroll = Math.Clamp(_scroll + (scrollDelta > 0 ? -1 : 1), 0, MaxScroll);
            return true;
        }

        protected override int HitTest(Point p)
        {
            if (SaveBounds.Contains(p)) return HitSave;
            if (StartBounds.Contains(p)) return HitStart;
            if (_tab == PickupTab && AddBounds.Contains(p)) return HitAdd;
            if (_tab == PickupTab && FieldBounds.Contains(p)) return HitField;

            for (int i = 0; i < TabNames.Length; i++)
                if (TabBounds(i).Contains(p))
                    return HitTab + i;

            for (int v = 0; v < VisibleRows && _scroll + v < _rows.Count; v++)
            {
                int index = _scroll + v;
                Rectangle row = RowBounds(v);
                if (!row.Contains(p) || _rows[index] is SettingHeader)
                    continue;

                int part = PartRow;
                if (_rows[index] is SettingChoice)
                {
                    Rectangle box = OrnateWidgets.ChoiceBox(ControlBounds(row));
                    part = OrnateWidgets.ChoiceLeft(box).Contains(p) ? PartLeft
                        : OrnateWidgets.ChoiceRight(box).Contains(p) ? PartRight
                        : PartCenter;
                }

                return HitRow + index * 4 + part;
            }

            return HitNone;
        }

        protected override void OnActivate(int hit)
        {
            if (hit == HitField)
            {
                FocusName();
                return;
            }

            BlurName();
            if (hit == HitSave) { PlayClickSound(); Save(); return; }
            if (hit == HitStart) { PlayClickSound(); StartOrStop(); return; }
            if (hit == HitAdd) { PlayClickSound(); AddName(); return; }
            if (hit >= HitTab && hit < HitRow)
            {
                if (hit - HitTab != _tab) { PlayClickSound(); SelectTab(hit - HitTab); }
                return;
            }

            if (hit < HitRow || (hit - HitRow) / 4 >= _rows.Count) return;
            int part = (hit - HitRow) % 4;
            switch (_rows[(hit - HitRow) / 4])
            {
                case SettingToggle toggle:
                    toggle.Set(!toggle.Get());
                    break;
                case SettingChoice choice when choice.Choices.Count > 0:
                    int current = choice.GetIndex(), count = choice.Choices.Count;
                    int next = part switch
                    {
                        PartLeft => Math.Max(0, current - 1),
                        PartRight => Math.Min(count - 1, current + 1),
                        _ => (current + 1) % count
                    };
                    if (next == current) return;
                    choice.SetIndex(next);
                    break;
                case ActionRow action:
                    action.OnClick();
                    PlayClickSound();
                    return;
                default:
                    return;
            }

            PlayClickSound();
            Changed();
        }

        // ─────────────────────────────── Draw ───────────────────────────────

        public override void Draw(GameTime gameTime)
        {
            using (PlayerShopUi.BeginUiBatch())
                base.Draw(gameTime);
        }

        protected override void DrawContent(SpriteBatch sb, float a)
        {
            for (int i = 0; i < TabNames.Length; i++)
            {
                var state = new WidgetState(Hover: Clicks.IsHover(HitTab + i) || i == _tab, Pressed: Clicks.IsPressed(HitTab + i));
                OrnateWidgets.Button(sb, TabBounds(i), TabNames[i], state, a, overhang: 0.3f);
            }

            for (int v = 0; v < VisibleRows && _scroll + v < _rows.Count; v++)
                DrawRow(sb, _scroll + v, RowBounds(v), a);
            OrnateWidgets.Scrollbar(sb, At(WindowWidth - Pad - 6, RowsTop, 5, VisibleRows * RowHeight - 2), _scroll, VisibleRows, _rows.Count, a);

            if (_tab == PickupTab)
            {
                OrnateWidgets.InputFrame(sb, FieldBounds, _nameField.IsFocused, false, a);
                if (string.IsNullOrEmpty(_nameField.Value) && !_nameField.IsFocused)
                    OrnateDraw.TextLeft(sb, "Item name (part of it)", new Rectangle(FieldBounds.X + 12, FieldBounds.Y, FieldBounds.Width - 20, FieldBounds.Height), 12.5f, OrnateStyle.TextDisabled, a);
                OrnateWidgets.Button(sb, AddBounds, "Add", new WidgetState(Clicks.IsHover(HitAdd), Clicks.IsPressed(HitAdd)), a, overhang: 0.3f);
            }

            string status = MuHelperState.IsRunning
                ? $"Running - {MuHelperState.TotalZenCost:N0} zen spent"
                : _dirty ? "Unsaved changes" : "Stopped";
            OrnateDraw.TextLeft(sb, status, At(Pad, ButtonY, WindowWidth - Pad * 2 - 260, 40), 13f,
                MuHelperState.IsRunning ? OrnateStyle.Gold : OrnateStyle.TextMuted, a);
            OrnateWidgets.Button(sb, SaveBounds, "Save", new WidgetState(Clicks.IsHover(HitSave), Clicks.IsPressed(HitSave)), a, overhang: 0.3f);
            OrnateWidgets.Button(sb, StartBounds, MuHelperState.IsRunning ? "Stop" : "Start", new WidgetState(Clicks.IsHover(HitStart), Clicks.IsPressed(HitStart)), a, overhang: 0.3f);
        }

        private void DrawRow(SpriteBatch sb, int index, Rectangle row, float a)
        {
            SettingOption option = _rows[index];
            if (option is SettingHeader)
            {
                OrnateWidgets.SectionHeader(sb, row, option.Label, a);
                return;
            }

            int hover = Clicks.Hover;
            bool hovered = hover >= HitRow && (hover - HitRow) / 4 == index;
            OrnateWidgets.RowBackground(sb, row, index, hovered, a);
            OrnateDraw.TextLeft(sb, option.Label, new Rectangle(row.X + 12, row.Y, row.Width - ControlWidth - 24, row.Height), 13f,
                hovered ? Color.White : OrnateStyle.TextLight, a);

            Rectangle control = ControlBounds(row);
            switch (option)
            {
                case SettingToggle toggle:
                    OrnateWidgets.Toggle(sb, control, toggle.Get(), a);
                    break;
                case SettingChoice choice:
                    int current = choice.GetIndex();
                    string text = current >= 0 && current < choice.Choices.Count ? choice.Choices[current] : "-";
                    OrnateWidgets.Choice(sb, control, Fit(text),
                        canLeft: current > 0, canRight: current < choice.Choices.Count - 1,
                        hoverLeft: hover == HitRow + index * 4 + PartLeft, hoverRight: hover == HitRow + index * 4 + PartRight, a);
                    break;
                case ActionRow:
                    OrnateDraw.TextLeft(sb, "remove", control, 12f, hovered ? OrnateStyle.Gold : OrnateStyle.TextMuted, a);
                    break;
            }
        }

        /// <summary>Shortens long skill names so they fit between the arrows of the choice box.</summary>
        private static string Fit(string text)
        {
            const float size = 13.5f, maxWidth = ControlWidth - 64;
            if (OrnateDraw.Measure(text, size).X <= maxWidth) return text;
            for (int length = text.Length - 1; length > 0; length--)
            {
                string candidate = text[..length].TrimEnd() + "..";
                if (OrnateDraw.Measure(candidate, size).X <= maxWidth) return candidate;
            }
            return text;
        }
    }
}
