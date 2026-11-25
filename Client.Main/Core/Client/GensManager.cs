#nullable enable
using System;
using System.Collections.Concurrent;
using MUnique.OpenMU.Network.Packets.ClientToServer;

namespace Client.Main.Core.Client
{
    /// <summary>
    /// Gens (Duprian / Vanert) state of the local character, filled by GensInfo (0xF8 07).
    /// The gens of other players in scope come from AssignCharactersToGens (0xF8 05); nothing renders them yet.
    /// </summary>
    public sealed class GensManager
    {
        public GensType Gens { get; private set; } = GensType.Undefined;
        public bool IsMember => Gens != GensType.Undefined;
        public uint RankingPosition { get; private set; }
        public uint Rank { get; private set; }
        public uint Contribution { get; private set; }

        /// <summary>Contribution points still missing for the next rank, 0 when unknown / at the top.</summary>
        public uint NextRankContribution { get; private set; }

        /// <summary>Raised when the own gens info changed (GensInfo, join / leave).</summary>
        public event Action? InfoChanged;

        /// <summary>A gens NPC (Duprian / Vanert) opened its dialog (0xF9 01): (gens of the NPC).</summary>
        public event Action<GensType>? NpcDialogRequested;

        /// <summary>Result text of a join / leave / reward request.</summary>
        public event Action<string>? ResultReceived;

        private readonly ConcurrentDictionary<ushort, (GensType Gens, uint Rank)> _players = new();

        /// <summary>AssignCharactersToGens (0xF8 05): gens and rank of a player in scope (masked id); Undefined removes it.</summary>
        public void AssignPlayer(ushort playerId, GensType gens, uint rank)
        {
            if (gens == GensType.Undefined)
                _players.TryRemove(playerId, out _);
            else
                _players[playerId] = (gens, rank);
        }

        public bool TryGetPlayerGens(ushort playerId, out GensType gens, out uint rank)
        {
            bool found = _players.TryGetValue(playerId, out var entry);
            (gens, rank) = entry;
            return found;
        }

        public void SetInfo(GensType gens, uint rankingPosition, uint rank, uint contribution, uint nextRankContribution)
        {
            Gens = gens;
            RankingPosition = rankingPosition;
            Rank = rank;
            Contribution = contribution;
            NextRankContribution = nextRankContribution;
            InfoChanged?.Invoke();
        }

        /// <summary>Drops everything tied to the current session (disconnect / character change).</summary>
        public void Reset()
        {
            _players.Clear();
            SetInfo(GensType.Undefined, 0, 0, 0, 0);
        }

        internal void RaiseNpcDialog(GensType npcGens) => NpcDialogRequested?.Invoke(npcGens);
        internal void RaiseResult(string text) => ResultReceived?.Invoke(text);
    }
}
