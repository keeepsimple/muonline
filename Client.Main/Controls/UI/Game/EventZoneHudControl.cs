#nullable enable
using System;
using System.Collections.Generic;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Core.Client;
using Client.Main.Models;
using Microsoft.Xna.Framework;

namespace Client.Main.Controls.UI.Game
{
    /// <summary>
    /// Top-right HUD of the Kanturu (Maya / Nightmare battle), Crywolf (altars, statue shield, Balgass, result) and Raklion
    /// (event and Selupan state, battle result) events, driven by <see cref="KanturuEventState"/>, <see cref="CrywolfEventState"/>
    /// and <see cref="RaklionEventState"/>. Same placement as the Blood Castle HUD.
    /// </summary>
    public sealed class EventZoneHudControl : UIControl
    {
        private const int PanelWidth = 230;
        private const int LineHeight = 17;
        private const float FontSize = 12f;

        private static EventZoneHudControl? _instance;

        private readonly List<(string Text, Color Color)> _lines = new();

        private EventZoneHudControl()
        {
            ControlSize = ViewSize = new Point(PanelWidth, 40);
            AutoViewSize = false;
            Interactive = false;
            Align = ControlAlign.Top | ControlAlign.Right;
            X = -10;
            Y = 10;
        }

        public override bool NonDisposable => true;
        public static EventZoneHudControl Instance => _instance ??= new EventZoneHudControl();

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            KanturuEventState.Tick(dt);
            CrywolfEventState.Tick(dt);

            // The state packets only reach players on the event map; drop it when the player has left.
            ushort map = MuGame.Network?.GetCharacterState().MapId ?? 0;
            if (map != KanturuEventState.MapId && KanturuEventState.State != 0) KanturuEventState.Reset();
            if (map != CrywolfEventState.MapId && CrywolfEventState.State != 0) CrywolfEventState.SetState(0, 0);

            BuildLines(map);
        }

        /// <summary>The lines drawn for the given map (public for the offline checks).</summary>
        public IReadOnlyList<(string Text, Color Color)> Lines => _lines;

        public void BuildLines(ushort map)
        {
            _lines.Clear();
            if (map == KanturuEventState.MapId)
                BuildKanturu();
            else if (map == CrywolfEventState.MapId && CrywolfEventState.Active)
                BuildCrywolf();
            else if (map is RaklionEventState.MapId or RaklionEventState.HatcheryMapId)
                BuildRaklion();
        }

        private void BuildRaklion()
        {
            _lines.Add(("RAKLION", ModernHudTheme.TextGold));
            _lines.Add((RaklionEventState.Describe(RaklionEventState.State), ModernHudTheme.TextWhite));
            string selupan = RaklionEventState.DescribeSelupan(RaklionEventState.SelupanState);
            if (selupan.Length > 0) _lines.Add((selupan, ModernHudTheme.Warning));
            if (RaklionEventState.LastBattleSuccess is bool success)
                _lines.Add((success ? "Selupan defeated" : "Battle failed", success ? ModernHudTheme.Success : ModernHudTheme.Danger));
        }

        private void BuildKanturu()
        {
            if (KanturuEventState.ResultVisible)
                _lines.Add((KanturuEventState.ResultVictory ? "KANTURU CLEARED" : "KANTURU FAILED",
                    KanturuEventState.ResultVictory ? ModernHudTheme.Success : ModernHudTheme.Danger));

            if (!KanturuEventState.HudVisible) return;
            _lines.Add(("KANTURU", ModernHudTheme.TextGold));
            _lines.Add((KanturuEventState.StageLabel, ModernHudTheme.TextWhite));
            if (KanturuEventState.TimeLeft >= 0f)
                _lines.Add(($"Time: {FormatTime(KanturuEventState.TimeLeft)}", KanturuEventState.TimeLeft < 60f ? ModernHudTheme.Danger : ModernHudTheme.Warning));
            _lines.Add(($"Monsters: {KanturuEventState.Monsters}", ModernHudTheme.TextWhite));
            _lines.Add(($"Players: {KanturuEventState.Users}", ModernHudTheme.TextGray));
        }

        private void BuildCrywolf()
        {
            byte state = CrywolfEventState.State;
            _lines.Add(("CRYWOLF FORTRESS", ModernHudTheme.TextGold));
            if (state == 5)
            {
                // The occupation is only Peace or Occupied in the End state.
                bool defended = CrywolfEventState.Occupation == 0;
                _lines.Add((defended ? "The fortress was defended" : "The fortress was occupied", defended ? ModernHudTheme.Success : ModernHudTheme.Danger));
                if (CrywolfEventState.Rank >= 0)
                    _lines.Add(($"Rank {CrywolfEventState.RankLetter(CrywolfEventState.Rank)}   Exp {CrywolfEventState.Experience:N0}", ModernHudTheme.TextWhite));
                if (CrywolfEventState.Heroes.Count > 0)
                {
                    _lines.Add(("Heroes", ModernHudTheme.TextGold));
                    for (int i = 0; i < CrywolfEventState.Heroes.Count; i++)
                        _lines.Add(($"{i + 1}. {CrywolfEventState.Heroes[i].Name}  {CrywolfEventState.Heroes[i].Score:N0}", ModernHudTheme.TextWhite));
                }
                if (CrywolfEventState.ChaosRateBenefit > 0)
                    _lines.Add(($"Chaos Machine bonus: +{CrywolfEventState.ChaosRateBenefit}%", ModernHudTheme.Success));
                return;
            }

            _lines.Add((state == 3 ? "Contract the altars!" : "Defend the statue!", ModernHudTheme.TextWhite));
            if (CrywolfEventState.TimeLeft >= 0f)
                _lines.Add(($"Time: {FormatTime(CrywolfEventState.TimeLeft)}", ModernHudTheme.Warning));
            _lines.Add(($"Statue shield: {CrywolfEventState.ShieldPercent}%", CrywolfEventState.ShieldPercent > 0 ? ModernHudTheme.Secondary : ModernHudTheme.Danger));
            for (int i = 0; i < CrywolfEventState.AltarCount; i++)
            {
                bool contracted = CrywolfEventState.IsAltarContracted(i);
                _lines.Add(($"Altar {i + 1}: {(contracted ? "Contracted" : "Free")} ({CrywolfEventState.AltarContractsLeft(i)} left)",
                    contracted ? ModernHudTheme.Success : ModernHudTheme.TextGray));
            }

            if (state == 4)
            {
                _lines.Add((CrywolfEventState.BalgassHealth >= 0 ? $"Balgass: {CrywolfEventState.BalgassHealth}%" : "Balgass: --", ModernHudTheme.TextWhite));
                _lines.Add(($"Dark Elves: {CrywolfEventState.DarkElves} / 12", ModernHudTheme.TextWhite));
            }
        }

        private static string FormatTime(float seconds)
        {
            int total = (int)MathF.Ceiling(seconds);
            return $"{total / 60:D2}:{total % 60:D2}";
        }

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready || _lines.Count == 0) return;
            var sb = GraphicsManager.Instance.Sprite;
            if (sb == null || GraphicsManager.Instance.Pixel == null) return;

            var rect = DisplayRectangle;
            var panel = new Rectangle(rect.X, rect.Y, PanelWidth, _lines.Count * LineHeight + 16);
            var scope = PlayerShopUi.BeginUiBatch();
            try
            {
                PlayerShopUi.DrawPanel(sb, panel, ModernHudTheme.BgDark, 0.85f);
                for (int i = 0; i < _lines.Count; i++)
                    PlayerShopUi.DrawTextCentered(sb, _lines[i].Text, new Rectangle(panel.X, panel.Y + 8 + i * LineHeight, panel.Width, LineHeight), _lines[i].Color, FontSize);
            }
            finally
            {
                scope?.Dispose();
            }
        }
    }
}
