using System;
using System.Collections.Generic;

namespace Client.Main.Core.Client
{
    /// <summary>
    /// Kanturu event state of the in-map HUD (0xD1 packets). Show/hide follows the detail state of the server:
    /// the HUD is visible in the Maya monster/boss phases and the Nightmare battle, detail 0 hides it.
    /// </summary>
    public static class KanturuEventState
    {
        public const ushort MapId = 39;
        private const float ResultSeconds = 8f;

        public static byte State { get; private set; }
        public static byte Detail { get; private set; }
        public static int Monsters { get; private set; }
        public static int Users { get; private set; }
        public static float TimeLeft { get; private set; } = -1f;
        public static float ResultTimer { get; private set; }
        public static bool ResultVictory { get; private set; }

        public static bool HudVisible => State switch
        {
            2 => Detail is 3 or 4 or 8 or 9 or 13 or 14, // MayaBattle: monster waves and the hands
            3 => Detail == 3,                              // NightmareBattle: battle
            _ => false,
        };

        public static bool ResultVisible => ResultTimer > 0f;

        public static string StageLabel => (State, Detail) switch
        {
            (2, 3) => "Maya - Phase 1",
            (2, 4) => "Maya - Left Hand",
            (2, 8) => "Maya - Phase 2",
            (2, 9) => "Maya - Right Hand",
            (2, 13) => "Maya - Phase 3",
            (2, 14) => "Maya - Both Hands",
            (3, 3) => "Nightmare",
            _ => string.Empty,
        };

        public static void SetState(byte state, byte detail)
        {
            State = state;
            Detail = detail;
            if (!HudVisible)
                TimeLeft = -1f;
        }

        /// <summary>0xD1/0x05: the countdown duration in milliseconds.</summary>
        public static void SetTimeLimit(uint milliseconds) => TimeLeft = milliseconds / 1000f;

        public static void SetCounts(int monsters, int users)
        {
            Monsters = monsters;
            Users = users;
        }

        public static void SetResult(bool victory)
        {
            ResultVictory = victory;
            ResultTimer = ResultSeconds;
        }

        public static void Tick(float seconds)
        {
            if (TimeLeft > 0f) TimeLeft = Math.Max(0f, TimeLeft - seconds);
            if (ResultTimer > 0f) ResultTimer = Math.Max(0f, ResultTimer - seconds);
        }

        public static void Reset()
        {
            State = Detail = 0;
            Monsters = Users = 0;
            TimeLeft = -1f;
            ResultTimer = 0f;
        }
    }

    /// <summary>
    /// Crywolf fortress event state (0xBD packets): event phase, statue shield and altars, Balgass and Dark Elf
    /// status, the remaining time and the personal result.
    /// </summary>
    public static class CrywolfEventState
    {
        public const ushort MapId = 34;
        public const int AltarCount = 5;

        public readonly record struct Hero(string Name, uint Score, byte CharacterClass);

        private static int _lastMinute = -1;
        private static int _lastSeconds;

        public static byte Occupation { get; private set; }
        public static byte State { get; private set; }
        public static uint ShieldPercent { get; private set; }
        public static byte[] AltarStates { get; } = new byte[AltarCount];
        public static int BalgassHealth { get; private set; } = -1;
        public static int DarkElves { get; private set; }
        public static float TimeLeft { get; private set; } = -1f;
        public static int Rank { get; private set; } = -1;
        public static uint Experience { get; private set; }
        public static List<Hero> Heroes { get; } = new();
        public static byte ChaosRateBenefit { get; private set; }

        /// <summary>Ready (3), Start (4) and End (5) show the panel; the result stays until the cycle ends.</summary>
        public static bool Active => State is >= 3 and <= 5;

        public static void SetState(byte occupation, byte state)
        {
            Occupation = occupation;
            if (state == State) return;
            State = state;
            if (state < 3 || state > 5) ClearBattle();
        }

        /// <summary>0xBD/0x02: statue shield and the five altar states (high nibble 1 = contracted, low nibble = remaining contracts).</summary>
        public static void SetStatueAndAltars(uint shield, ReadOnlySpan<byte> altars)
        {
            ShieldPercent = Math.Min(shield, 100u);
            altars[..Math.Min(altars.Length, AltarCount)].CopyTo(AltarStates);
        }

        /// <summary>0xBD/0x03: the new state of one altar after a contract request (altar key 317 + index).</summary>
        public static void SetAltarState(int index, byte state)
        {
            if ((uint)index < AltarCount) AltarStates[index] = state;
        }

        public static bool IsAltarContracted(int index) => (AltarStates[index] >> 4) == 1;

        public static int AltarContractsLeft(int index) => AltarStates[index] & 0x0F;

        /// <summary>
        /// 0xBD/0x04: the server sends it every 20 seconds with the whole remaining minutes; like the original
        /// client, the seconds are 60 for the first packet of a minute and then 40 and 20.
        /// </summary>
        public static void SetLeftTime(byte hours, byte minutes)
        {
            int totalMinutes = hours * 60 + minutes;
            int seconds = totalMinutes == _lastMinute ? Math.Max(20, _lastSeconds - 20) : 60;
            _lastMinute = totalMinutes;
            _lastSeconds = seconds;
            TimeLeft = totalMinutes * 60 + seconds;
        }

        public static void SetBossInfo(int balgassHealth, byte darkElves)
        {
            BalgassHealth = balgassHealth;
            DarkElves = darkElves;
        }

        public static void SetPersonalRank(byte rank, uint experience)
        {
            Rank = rank;
            Experience = experience;
        }

        public static void SetHeroes(IEnumerable<Hero> heroes)
        {
            Heroes.Clear();
            Heroes.AddRange(heroes);
        }

        public static void SetChaosRateBenefit(byte percent) => ChaosRateBenefit = percent;

        public static void Tick(float seconds)
        {
            if (TimeLeft > 0f) TimeLeft = Math.Max(0f, TimeLeft - seconds);
        }

        public static char RankLetter(int rank) => rank switch { 0 => 'D', 1 => 'C', 2 => 'B', 3 => 'A', 4 => 'S', _ => '-' };

        private static void ClearBattle()
        {
            ShieldPercent = 0;
            Array.Clear(AltarStates);
            BalgassHealth = -1;
            DarkElves = 0;
            TimeLeft = -1f;
            _lastMinute = -1;
            Rank = -1;
            Experience = 0;
            Heroes.Clear();
        }
    }

    /// <summary>
    /// Raklion event state (0xD1/0x10-0x13). State 11 (DetailState) carries the Selupan state instead of an event state.
    /// </summary>
    public static class RaklionEventState
    {
        public const byte DetailStateMarker = 11;
        public const ushort MapId = 57;
        public const ushort HatcheryMapId = 58;

        public static byte State { get; private set; }
        /// <summary>0 none, 1 standby, 2-8 pattern 1-7, 9 dead.</summary>
        public static byte SelupanState { get; private set; }
        /// <summary>Result of the last Selupan battle, null while none ended.</summary>
        public static bool? LastBattleSuccess { get; private set; }

        public static void SetState(byte state, byte detail)
        {
            if (state == DetailStateMarker) { SelupanState = detail; return; }
            State = state;
            if (state is 0 or 10) { SelupanState = 0; LastBattleSuccess = null; }
        }

        public static void SetBattleResult(bool success) => LastBattleSuccess = success;

        /// <summary>Short text of the event state, for the HUD and the chat.</summary>
        public static string Describe(byte state) => state switch
        {
            0 => "The spider eggs are alive",
            1 => "Only a few spider eggs are left",
            2 => "Selupan is about to appear",
            3 => "Selupan appeared",
            4 => "Selupan rises",
            5 => "Battle against Selupan",
            6 => "The hatchery closes soon",
            7 => "The hatchery is closed",
            8 => "All players have fallen",
            9 => "The hatchery opens soon",
            10 => "The spider eggs appear again",
            _ => $"Unknown state {state}",
        };

        /// <summary>Selupan line of the HUD; empty while Selupan is not there.</summary>
        public static string DescribeSelupan(byte selupanState) => selupanState switch
        {
            1 => "Selupan: standby",
            >= 2 and <= 8 => $"Selupan: pattern {selupanState - 1}",
            9 => "Selupan: dead",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Counts of the event item NPCs: Lucky Coins registered at Delgado (0xBF/0x0B-0x0D) and items registered at the
    /// Golden Archer (0x94).
    /// </summary>
    public static class EventItemState
    {
        public static uint LuckyCoinsRegistered { get; set; }
        public static uint EventChipsRegistered { get; set; }
        public static int EventChipsInInventory { get; set; }
    }
}
