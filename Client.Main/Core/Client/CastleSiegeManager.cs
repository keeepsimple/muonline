#nullable enable
using System;
using System.Collections.Generic;

namespace Client.Main.Core.Client
{
    /// <summary>
    /// Castle Siege data received from the server (OpenMU 0xB2-0xBB packets): siege status, the own guild's
    /// registration, the registered/participating guilds and the battle state (side, switches, crown).
    /// Written by <c>CastleSiegeHandler</c> on the main thread, read by the Castle Siege windows and HUD.
    /// </summary>
    public sealed class CastleSiegeManager
    {
        /// <summary>Valley of Loren, the Castle Siege map.</summary>
        public const ushort SiegeMapId = 30;

        public enum SiegeState : byte { Idle1, RegisterGuild, Idle2, RegisterMark, Idle3, Notify, Ready, Start, End, EndCycle }

        public enum JoinSide : byte { None, Defense, Attack1, Attack2, Attack3 }

        /// <summary>CastleSiegeCrownAccessStateType.</summary>
        public enum CrownAccess : byte { Started, Succeeded, Failed, OccupiedByOtherPlayer, OccupiedByOtherSide }

        /// <summary>CastleSiegeHuntZoneAccessType of the Land of Trials guard (0xB9/0x03).</summary>
        public enum HuntZoneAccess : byte { Failed, Guest, OwnerAllianceMember, OwnerGuildMaster }

        public readonly record struct RegisteredGuild(string Name, uint Marks, bool IsGivingUp, byte Sequence);

        public readonly record struct Participant(JoinSide Side, bool IsAllianceMaster, string Name, uint Score);

        public readonly record struct SwitchInfo(bool IsOccupied, JoinSide Side, string GuildName, string UserName);

        /// <summary>Gate (277) or Guardian Statue (283) of the Senior's defense list (0xB3).</summary>
        public readonly record struct DefenseNpc(uint Number, uint Index, uint DefenseLevel, uint RegenerationLevel,
            uint MaxHp, uint CurrentHp, byte X, byte Y, bool IsAlive);

        // ─────────────────────────────── Status (0xB2/0x00) ───────────────────────────────

        public bool HasStatus { get; private set; }
        public SiegeState State { get; private set; }
        public DateTime StateStart { get; private set; }
        public DateTime StateEnd { get; private set; }
        public DateTime SiegeStart { get; private set; }
        public string OwnerGuild { get; private set; } = string.Empty;
        public string OwnerGuildMaster { get; private set; } = string.Empty;
        private DateTime _stateEndLocal;

        /// <summary>Owner guild mark (0xB9/0x02) as 8 x 8 palette indices; null until received, all zeros without owner.</summary>
        public byte[]? OwnerGuildMark { get; private set; }

        /// <summary>Time left in the current state, counted down locally from the last status response.</summary>
        public TimeSpan Remaining
        {
            get
            {
                var left = _stateEndLocal - DateTime.UtcNow;
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }

        public bool IsBattleRunning => HasStatus && State == SiegeState.Start;

        // ─────────────────────────────── Registration ───────────────────────────────

        /// <summary>Own guild registration (0xB2/0x03): null until the server answered.</summary>
        public bool? IsRegistered { get; private set; }
        public string RegisteredGuildName { get; private set; } = string.Empty;
        public uint Marks { get; private set; }
        public bool IsGivingUp { get; private set; }
        public byte RegistrationRank { get; private set; }

        public IReadOnlyList<RegisteredGuild> RegisteredGuilds { get; private set; } = Array.Empty<RegisteredGuild>();

        /// <summary>Guilds selected for the battle (0xB5); empty before the selection.</summary>
        public IReadOnlyList<Participant> Participants { get; private set; } = Array.Empty<Participant>();

        // ─────────────────────────────── Battle ───────────────────────────────

        public JoinSide Side { get; private set; }
        public bool? CrownAccessible { get; private set; }
        public CrownAccess? CrownAccessState { get; private set; }
        public uint CrownAccumulatedMs { get; private set; }
        public Dictionary<ushort, SwitchInfo> Switches { get; } = new();
        public Dictionary<ushort, bool> GateOpen { get; } = new();
        public Dictionary<ushort, byte> LifeStoneBuildTime { get; } = new();
        public string LastCommand { get; private set; } = string.Empty;

        /// <summary>Commander mini map (0xB6 / 0xBB): own side players and the gates/statues.</summary>
        public IReadOnlyList<(byte X, byte Y)> MiniMapPlayers { get; private set; } = Array.Empty<(byte, byte)>();
        public IReadOnlyList<(bool IsGate, byte X, byte Y)> MiniMapNpcs { get; private set; } = Array.Empty<(bool, byte, byte)>();
        public DateTime MiniMapUpdated { get; private set; }

        // ─────────────────────────────── Senior (taxes, defenses) ───────────────────────────────

        public bool HasTaxInfo { get; private set; }
        public byte ChaosTaxRate { get; private set; }
        public byte StoreTaxRate { get; private set; }
        public ulong Treasury { get; private set; }
        public IReadOnlyList<DefenseNpc> DefenseNpcs { get; private set; } = Array.Empty<DefenseNpc>();

        // ─────────────────────────────── Land of Trials (hunting zone) ───────────────────────────────

        /// <summary>Last guard info (0xB9/0x03); null until the guard was talked to.</summary>
        public HuntZoneAccess? HuntZoneAccessType { get; private set; }
        public bool IsHuntZonePublic { get; private set; }
        public uint HuntZoneFee { get; private set; }
        public uint HuntZoneMaxFee { get; private set; }
        public uint HuntZoneFeeStep { get; private set; }

        /// <summary>The Senior has no request for the Land of Trials settings: they are known once the guard info or a change response arrived.</summary>
        public bool HuntZonePublicKnown { get; private set; }
        public bool HuntZoneFeeKnown { get; private set; }

        // ─────────────────────────────── Updates ───────────────────────────────

        public void SetStatus(SiegeState state, DateTime stateStart, DateTime stateEnd, DateTime siegeStart,
            string ownerGuild, string ownerMaster, uint remainingSeconds)
        {
            HasStatus = true;
            State = state;
            StateStart = stateStart;
            StateEnd = stateEnd;
            SiegeStart = siegeStart;
            if (ownerGuild != OwnerGuild) OwnerGuildMark = null;
            OwnerGuild = ownerGuild;
            OwnerGuildMaster = ownerMaster;
            _stateEndLocal = DateTime.UtcNow.AddSeconds(remainingSeconds);

            // Battle data does not outlive the battle; the server does not announce the end separately.
            if (state != SiegeState.Ready && state != SiegeState.Start)
                ClearBattle();
        }

        public void SetStatusUnavailable() => HasStatus = false;

        public void SetRegistration(bool registered, string guildName, uint marks, bool isGivingUp, byte rank)
        {
            IsRegistered = registered;
            RegisteredGuildName = guildName;
            Marks = marks;
            IsGivingUp = isGivingUp;
            RegistrationRank = rank;
        }

        public void SetMarks(uint marks) => Marks = marks;

        public void SetRegisteredGuilds(IReadOnlyList<RegisteredGuild> guilds) => RegisteredGuilds = guilds;

        public void SetParticipants(IReadOnlyList<Participant> guilds) => Participants = guilds;

        public void SetSide(JoinSide side) => Side = side;

        public void SetCrownAccessible(bool accessible) => CrownAccessible = accessible;

        public void SetCrownAccess(CrownAccess state, uint accumulatedMs)
        {
            CrownAccessState = state;
            CrownAccumulatedMs = accumulatedMs;
        }

        public void SetCommand(string text) => LastCommand = text;

        public void SetMiniMapPlayers(IReadOnlyList<(byte X, byte Y)> players)
        {
            MiniMapPlayers = players;
            MiniMapUpdated = DateTime.UtcNow;
        }

        public void SetMiniMapNpcs(IReadOnlyList<(bool IsGate, byte X, byte Y)> npcs)
        {
            MiniMapNpcs = npcs;
            MiniMapUpdated = DateTime.UtcNow;
        }

        public void SetTaxInfo(byte chaos, byte store, ulong treasury)
        {
            HasTaxInfo = true;
            ChaosTaxRate = chaos;
            StoreTaxRate = store;
            Treasury = treasury;
        }

        /// <summary>CastleSiegeTaxType 1 = Chaos Machine, 2 = Store (percent), 3 = Land of Trials entrance fee (Zen).</summary>
        public void SetTaxRate(byte taxType, uint value)
        {
            if (taxType == 1) ChaosTaxRate = (byte)Math.Min(value, byte.MaxValue);
            else if (taxType == 2) StoreTaxRate = (byte)Math.Min(value, byte.MaxValue);
            else if (taxType == 3)
            {
                HuntZoneFee = value;
                HuntZoneFeeKnown = true;
            }
        }

        public void SetTreasury(ulong treasury) => Treasury = treasury;

        public void SetDefenseNpcs(IReadOnlyList<DefenseNpc> npcs) => DefenseNpcs = npcs;

        public void UpdateDefenseNpcHp(uint number, uint index, uint currentHp, uint maxHp)
            => UpdateDefenseNpc(number, index, npc => npc with { CurrentHp = currentHp, MaxHp = maxHp });

        /// <summary>CastleSiegeUpgradeType 1 = defense, 2 = regeneration; 3 = life changes the HP, which only a new list shows.</summary>
        public void SetDefenseNpcUpgrade(uint number, uint index, uint upgradeType, uint level)
            => UpdateDefenseNpc(number, index, npc => upgradeType switch
            {
                1 => npc with { DefenseLevel = level },
                2 => npc with { RegenerationLevel = level },
                _ => npc,
            });

        public void SetDefenseNpcBuilt(uint number, uint index) => UpdateDefenseNpc(number, index, npc => npc with { IsAlive = true });

        private void UpdateDefenseNpc(uint number, uint index, Func<DefenseNpc, DefenseNpc> update)
        {
            var list = new List<DefenseNpc>(DefenseNpcs);
            for (int i = 0; i < list.Count; i++)
                if (list[i].Number == number && list[i].Index == index)
                    list[i] = update(list[i]);
            DefenseNpcs = list;
        }

        public void SetOwnerGuildMark(byte[] mark) => OwnerGuildMark = mark;

        public void SetHuntZoneInfo(HuntZoneAccess access, bool isPublic, uint fee, uint maxFee, uint feeStep)
        {
            HuntZoneAccessType = access;
            IsHuntZonePublic = isPublic;
            HuntZoneFee = fee;
            HuntZoneMaxFee = maxFee;
            HuntZoneFeeStep = feeStep;
            HuntZonePublicKnown = HuntZoneFeeKnown = true;
        }

        public void SetHuntZonePublic(bool isPublic)
        {
            IsHuntZonePublic = isPublic;
            HuntZonePublicKnown = true;
        }

        /// <summary>Forgets everything on logout / character change.</summary>
        public void Reset()
        {
            HasStatus = false;
            OwnerGuildMark = null;
            HuntZoneAccessType = null;
            HuntZonePublicKnown = HuntZoneFeeKnown = false;
            IsRegistered = null;
            RegisteredGuilds = Array.Empty<RegisteredGuild>();
            Participants = Array.Empty<Participant>();
            HasTaxInfo = false;
            DefenseNpcs = Array.Empty<DefenseNpc>();
            GateOpen.Clear();
            ClearBattle();
        }

        public void ClearBattle()
        {
            Side = JoinSide.None;
            CrownAccessible = null;
            CrownAccessState = null;
            CrownAccumulatedMs = 0;
            Switches.Clear();
            LifeStoneBuildTime.Clear();
            LastCommand = string.Empty;
            MiniMapPlayers = Array.Empty<(byte, byte)>();
            MiniMapNpcs = Array.Empty<(bool, byte, byte)>();
        }

        // ─────────────────────────────── Defense rules ───────────────────────────────

        public const uint GateNumber = 277;
        public const uint StatueNumber = 283;

        /// <summary>CastleSiegeUpgradeType of 0xB2/0x07.</summary>
        public enum UpgradeType : byte { Defense = 1, Regeneration = 2, Life = 3 }

        /// <summary>One upgrade level: its cost and the resulting value (defense, regeneration or max HP).</summary>
        public readonly record struct UpgradeLevel(uint Level, int Jewels, int Zen, int Value);

        // ponytail: OpenMU CastleSiegeInitializer defaults; the server can configure other prices, it then refuses/charges by its own table.
        public const int GateBuyPrice = 9_500_000;
        public const int StatueBuyPrice = 4_500_000;
        public const int MaxHuntZoneFee = 300_000; // CastleSiegeTaxProvider.MaximumHuntTax

        private static readonly UpgradeLevel[] GateDefense = { new(0, 0, 0, 100), new(1, 2, 3_000_000, 180), new(2, 3, 3_000_000, 300), new(3, 4, 3_000_000, 520) };
        private static readonly UpgradeLevel[] GateLife = { new(0, 0, 0, 1_900_000), new(1, 2, 1_000_000, 2_500_000), new(2, 3, 1_000_000, 3_500_000), new(3, 4, 1_000_000, 5_200_000) };
        private static readonly UpgradeLevel[] StatueDefense = { new(0, 0, 0, 80), new(1, 3, 3_000_000, 180), new(2, 5, 3_000_000, 340), new(3, 7, 3_000_000, 550) };
        private static readonly UpgradeLevel[] StatueLife = { new(0, 0, 0, 1_500_000), new(1, 3, 1_000_000, 2_200_000), new(2, 5, 1_000_000, 3_400_000), new(3, 7, 1_000_000, 5_000_000) };
        private static readonly UpgradeLevel[] StatueRegen = { new(0, 0, 0, 0), new(1, 3, 5_000_000, 1), new(2, 5, 5_000_000, 2), new(3, 7, 5_000_000, 3) };

        public static int GetBuyPrice(uint npcNumber) => npcNumber == StatueNumber ? StatueBuyPrice : npcNumber == GateNumber ? GateBuyPrice : 0;

        /// <summary>CastleSiegeConfigurationExtensions.GetUpgrades: gates have no regeneration upgrade.</summary>
        public static IReadOnlyList<UpgradeLevel>? GetUpgrades(uint npcNumber, UpgradeType type) => (npcNumber, type) switch
        {
            (GateNumber, UpgradeType.Defense) => GateDefense,
            (GateNumber, UpgradeType.Life) => GateLife,
            (StatueNumber, UpgradeType.Defense) => StatueDefense,
            (StatueNumber, UpgradeType.Life) => StatueLife,
            (StatueNumber, UpgradeType.Regeneration) => StatueRegen,
            _ => null,
        };

        /// <summary>Current level; the list carries no life level, so it is the level whose max HP the structure has.</summary>
        public static uint GetLevel(DefenseNpc npc, UpgradeType type)
        {
            if (type == UpgradeType.Defense) return npc.DefenseLevel;
            if (type == UpgradeType.Regeneration) return npc.RegenerationLevel;
            uint level = 0;
            foreach (var upgrade in GetUpgrades(npc.Number, UpgradeType.Life) ?? Array.Empty<UpgradeLevel>())
                if (npc.MaxHp >= upgrade.Value) level = upgrade.Level;
            return level;
        }

        /// <summary>The only level the server accepts next (current + 1), or null when unsupported, maxed or destroyed.</summary>
        public static UpgradeLevel? GetNextUpgrade(DefenseNpc npc, UpgradeType type)
        {
            if (!npc.IsAlive || GetUpgrades(npc.Number, type) is not { } upgrades) return null;
            uint next = GetLevel(npc, type) + 1;
            foreach (var upgrade in upgrades)
                if (upgrade.Level == next) return upgrade;
            return null;
        }

        // ─────────────────────────────── Names ───────────────────────────────

        public static string GetStateName(SiegeState state) => state switch
        {
            SiegeState.RegisterGuild => "Guild registration",
            SiegeState.RegisterMark => "Sign of Lord registration",
            SiegeState.Notify => "Announcement",
            SiegeState.Ready => "Preparation",
            SiegeState.Start => "Battle in progress",
            SiegeState.End => "Battle ended",
            SiegeState.EndCycle => "Cycle ended",
            _ => "Truce",
        };

        public static string GetSideName(JoinSide side) => side switch
        {
            JoinSide.Defense => "Defense",
            JoinSide.Attack1 => "Attack 1",
            JoinSide.Attack2 => "Attack 2",
            JoinSide.Attack3 => "Attack 3",
            _ => "None",
        };
    }
}
