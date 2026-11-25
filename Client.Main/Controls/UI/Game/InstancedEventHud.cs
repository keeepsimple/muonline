using System;
using System.Collections.Generic;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game
{
    public enum InstancedEventMode
    {
        None,
        IllusionTemple,
        Doppelganger,
        EmpireGuardian,
    }

    /// <summary>
    /// In-match HUD of the instanced events (top-right, like <see cref="BloodCastleTimeControl"/>):
    /// Illusion Temple (time, team points, relic carrier, skill points), Doppelganger (time, monsters at the
    /// magic circle, path progress bar) and Empire Guardian (timer type, remaining time and monsters).
    /// Fed by <c>InstancedEventHandler</c>; the server cycles every update once per second.
    /// </summary>
    public sealed class InstancedEventHud : UIControl
    {
        private const int Width = 230;
        private const int LineHeight = 17;
        private const float FontSize = 12f;
        private const int BarHeight = 8;
        // ponytail: the server sends no "left the event" packet, so a HUD without updates for this long
        // hides itself (all three events update every second; Illusion Temple preparation lasts 20 s).
        private const long StaleMs = 30000;

        /// <summary>Last path position of the Doppelganger frame (the magic circle).</summary>
        public const int DoppelgangerPathLength = 22;

        private static InstancedEventHud _instance;

        private long _touchedMs;
        private ushort _ownId = 0xFFFF;

        // Illusion Temple
        private int _remainingSeconds = -1;
        private int _alliedPoints;
        private int _illusionPoints;
        private byte _myTeam;
        private ushort _relicCarrierId = 0xFFFF;
        private ushort _relicNameId = 0xFFFF;
        private string _relicName = string.Empty;
        private int _skillPoints;

        // Doppelganger
        private int _monsterPosition = -1;
        private int _iceWalkerPosition = -1;
        private int _goalCount;
        private int _goalMax;
        private (ushort PlayerId, int Position)[] _players = Array.Empty<(ushort, int)>();

        // Empire Guardian
        private int _day;
        private int _zone;
        private int _timerType = -1;
        private int _monsterCount;

        private InstancedEventHud()
        {
            AutoViewSize = false;
            Interactive = false;
            Visible = false;
            Align = ControlAlign.Top | ControlAlign.Right;
            X = -10;
            Y = 10;
        }

        public override bool NonDisposable => true;
        public static InstancedEventHud Instance => _instance ??= new InstancedEventHud();

        public InstancedEventMode Mode { get; private set; }
        public int RemainingSeconds => _remainingSeconds;
        public int AlliedPoints => _alliedPoints;
        public int IllusionPoints => _illusionPoints;
        public int SkillPoints => _skillPoints;
        public int MonsterPosition => _monsterPosition;
        public int IceWalkerPosition => _iceWalkerPosition;
        public int GoalCount => _goalCount;
        public int GoalMax => _goalMax;
        public int PlayerCount => _players.Length;
        public int Day => _day;
        public int Zone => _zone;
        public int MonsterCount => _monsterCount;
        public int TimerType => _timerType;
        public string RelicHolder => RelicText();

        // ───────────────────────────── Illusion Temple ─────────────────────────────

        /// <summary>BF/09 Preparation or BattleStarted: open the HUD before the first cyclic state arrives.</summary>
        public void BeginIllusionTemple()
        {
            if (Mode != InstancedEventMode.IllusionTemple)
            {
                Reset(InstancedEventMode.IllusionTemple, 5);
            }

            Touch();
        }

        public void SetIllusionTempleState(int remainingSeconds, int alliedPoints, int illusionPoints, byte myTeam, ushort relicCarrierId, ushort ownId)
        {
            BeginIllusionTemple();
            _remainingSeconds = remainingSeconds;
            _alliedPoints = alliedPoints;
            _illusionPoints = illusionPoints;
            _myTeam = myTeam;
            _relicCarrierId = relicCarrierId;
            _ownId = ownId;
            if (relicCarrierId == 0xFFFF)
            {
                _relicNameId = 0xFFFF;
            }
        }

        public void SetIllusionTempleRelic(ushort userIndex, string name)
        {
            _relicNameId = userIndex;
            _relicName = name;
        }

        /// <summary>BF/03: players waiting in each of the six temples (meant for the entrance dialog, which the client lacks).</summary>
        public int[] IllusionTempleUserCounts { get; } = new int[6];

        public void SetIllusionTempleUserCounts(ReadOnlySpan<byte> counts)
        {
            for (int i = 0; i < IllusionTempleUserCounts.Length && i < counts.Length; i++)
                IllusionTempleUserCounts[i] = counts[i];
        }

        public void SetIllusionTempleSkillPoints(int skillPoints)
        {
            BeginIllusionTemple();
            _skillPoints = skillPoints;
        }

        // ───────────────────────────── Doppelganger ─────────────────────────────

        public void BeginDoppelganger()
        {
            if (Mode != InstancedEventMode.Doppelganger)
            {
                Reset(InstancedEventMode.Doppelganger, 5, bar: true);
            }

            Touch();
        }

        public void SetDoppelgangerPlayInfo(int remainingSeconds, IReadOnlyList<(ushort PlayerId, int Position)> players, ushort ownId)
        {
            BeginDoppelganger();
            _remainingSeconds = remainingSeconds;
            _ownId = ownId;
            var copy = new (ushort, int)[players.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = players[i];
            _players = copy;
        }

        public void SetDoppelgangerMonsterPosition(int position)
        {
            BeginDoppelganger();
            _monsterPosition = position;
        }

        public void SetDoppelgangerIceWalker(bool appeared, int position)
        {
            BeginDoppelganger();
            _iceWalkerPosition = appeared ? position : -1;
        }

        public void SetDoppelgangerGoal(int goalCount, int goalMax)
        {
            BeginDoppelganger();
            _goalCount = goalCount;
            _goalMax = goalMax;
        }

        // ───────────────────────────── Empire Guardian ─────────────────────────────

        public void BeginEmpireGuardian(int day, int zone)
        {
            Reset(InstancedEventMode.EmpireGuardian, 4);
            _day = day;
            _zone = zone;
            Touch();
        }

        public void SetEmpireGuardianTimer(int timerType, uint remainingMilliseconds, int monsterCount)
        {
            if (Mode != InstancedEventMode.EmpireGuardian)
            {
                Reset(InstancedEventMode.EmpireGuardian, 4);
            }

            _timerType = timerType;
            _remainingSeconds = (int)Math.Min(remainingMilliseconds / 1000, int.MaxValue);
            _monsterCount = monsterCount;
            Touch();
        }

        public void Hide()
        {
            Visible = false;
            Mode = InstancedEventMode.None;
        }

        private void Reset(InstancedEventMode mode, int lines, bool bar = false)
        {
            Mode = mode;
            _remainingSeconds = -1;
            _alliedPoints = _illusionPoints = _skillPoints = 0;
            _relicCarrierId = _relicNameId = 0xFFFF;
            _relicName = string.Empty;
            _monsterPosition = _iceWalkerPosition = -1;
            _goalCount = _goalMax = 0;
            _players = Array.Empty<(ushort, int)>();
            _timerType = -1;
            _monsterCount = _day = _zone = 0;
            ControlSize = ViewSize = new Point(Width, 14 + lines * LineHeight + (bar ? 22 : 0));
            Visible = true;
        }

        private void Touch() => _touchedMs = Environment.TickCount64;

        public override void Update(GameTime gameTime)
        {
            if (Visible && Environment.TickCount64 - _touchedMs > StaleMs)
            {
                Hide();
            }

            base.Update(gameTime);
        }

        // ───────────────────────────── Drawing ─────────────────────────────

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready || Mode == InstancedEventMode.None) return;

            var sb = GraphicsManager.Instance.Sprite;
            var pixel = GraphicsManager.Instance.Pixel;
            if (sb == null || pixel == null) return;

            using var scope = new SpriteBatchScope(
                sb,
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                GraphicsManager.GetQualityLinearSamplerState(),
                transform: UiScaler.SpriteTransform);

            var rect = DisplayRectangle;
            sb.Draw(pixel, rect, ModernHudTheme.BgDark);
            UiDrawHelper.DrawBorder(sb, rect, ModernHudTheme.BorderOuter);
            sb.Draw(pixel, new Rectangle(rect.X + 1, rect.Y + 1, rect.Width - 2, 1), ModernHudTheme.BorderInner * 0.5f);

            float y = rect.Y + 7;
            foreach (var (text, color) in BuildLines())
            {
                PlayerShopUi.DrawTextCentered(sb, text, new Rectangle(rect.X, (int)y, rect.Width, LineHeight), color, FontSize);
                y += LineHeight;
            }

            if (Mode == InstancedEventMode.Doppelganger)
            {
                DrawDoppelgangerBar(sb, pixel, new Rectangle(rect.X + 12, (int)y + 6, rect.Width - 24, BarHeight));
            }
        }

        private List<(string Text, Color Color)> BuildLines()
        {
            var lines = new List<(string, Color)>();
            Color white = ModernHudTheme.TextWhite;
            Color gold = ModernHudTheme.TextGold;
            switch (Mode)
            {
                case InstancedEventMode.IllusionTemple:
                    bool allied = _myTeam == 0;
                    lines.Add(("ILLUSION TEMPLE", gold));
                    lines.Add((TimeText(), TimeColor()));
                    lines.Add(($"Allied {_alliedPoints}  :  {_illusionPoints} Illusion", white));
                    lines.Add(($"Your team: {(allied ? "Allied Forces" : "Illusion Forces")}", allied ? ModernHudTheme.SecondaryBright : ModernHudTheme.Danger));
                    lines.Add(($"Relic: {RelicText()}   SP: {_skillPoints}", white));
                    break;

                case InstancedEventMode.Doppelganger:
                    lines.Add(("DOPPELGANGER", gold));
                    lines.Add((TimeText(), TimeColor()));
                    lines.Add(($"Monsters passed: {_goalCount} / {_goalMax}", _goalMax > 0 && _goalCount * 2 >= _goalMax ? ModernHudTheme.Danger : white));
                    lines.Add(("Defend the magic circle", ModernHudTheme.TextGray));
                    lines.Add((_iceWalkerPosition >= 0 ? "Ice Walker appeared!" : string.Empty, ModernHudTheme.SecondaryBright));
                    break;

                case InstancedEventMode.EmpireGuardian:
                    lines.Add((_day > 0 ? $"EMPIRE GUARDIAN  -  Day {_day}, Zone {_zone}" : "EMPIRE GUARDIAN", gold));
                    lines.Add((TimerLabel(), ModernHudTheme.TextGray));
                    lines.Add((TimeText(), TimeColor()));
                    lines.Add(($"Monsters: {_monsterCount}", white));
                    break;
            }

            return lines;
        }

        private void DrawDoppelgangerBar(SpriteBatch sb, Texture2D pixel, Rectangle bar)
        {
            sb.Draw(pixel, bar, ModernHudTheme.BgDarkest);
            UiDrawHelper.DrawBorder(sb, bar, ModernHudTheme.BorderInner);

            int X(int position) => bar.X + (int)(Math.Clamp(position, 0, DoppelgangerPathLength) * (bar.Width - 4) / (float)DoppelgangerPathLength);
            void Mark(int position, Color color, int width = 4, int extra = 4)
                => sb.Draw(pixel, new Rectangle(X(position), bar.Y - extra / 2, width, bar.Height + extra), color);

            Mark(DoppelgangerPathLength, ModernHudTheme.TextGold, 4, 6); // magic circle
            foreach (var (playerId, position) in _players)
            {
                Mark(position, playerId == _ownId ? Color.Yellow : ModernHudTheme.Success);
            }

            if (_iceWalkerPosition >= 0) Mark(_iceWalkerPosition, new Color(120, 220, 255), 5, 6);
            if (_monsterPosition >= 0) Mark(_monsterPosition, ModernHudTheme.Danger, 5, 6);
        }

        private string TimeText() => _remainingSeconds < 0 ? "--:--" : $"{_remainingSeconds / 60:D2}:{_remainingSeconds % 60:D2}";

        private Color TimeColor() => _remainingSeconds >= 0 && _remainingSeconds < 60 ? ModernHudTheme.Danger : ModernHudTheme.Warning;

        private string TimerLabel() => _timerType switch
        {
            0 => "Loot time",
            1 => "Monsters appear in",
            2 => "Time attack",
            _ => string.Empty,
        };

        private string RelicText()
        {
            if (_relicCarrierId == 0xFFFF) return "--";
            if (_relicCarrierId == _ownId) return "You";
            return _relicNameId == _relicCarrierId && _relicName.Length > 0 ? _relicName : "carried";
        }
    }
}
