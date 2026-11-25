using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Network.Packets.ClientToServer;

namespace Client.Main.Networking.PacketHandling.Handlers
{
    /// <summary>
    /// Handles the Gens packets of OpenMU (group 0xF8) and the gens NPC dialog (0xF9 01). These are not part of
    /// the NuGet packet library (0.9.8), so they are parsed by hand; layouts follow ServerToClientPackets.xml.
    /// </summary>
    public class GensHandler : IGamePacketHandler
    {
        private const ushort DuprianNpcNumber = 543;
        private const ushort VanertNpcNumber = 544;

        private readonly ILogger<GensHandler> _logger;
        private readonly NetworkManager _networkManager;

        public GensHandler(ILoggerFactory loggerFactory, NetworkManager networkManager)
        {
            _logger = loggerFactory.CreateLogger<GensHandler>();
            _networkManager = networkManager;
        }

        private GensManager Gens => _networkManager.GetGensManager();

        /// <summary>OpenNpcDialog (0xF9 01): NpcNumber @4 (LE short), gens contribution @8 (LE int).</summary>
        [PacketHandler(0xF9, 0x01)]
        public Task HandleOpenNpcDialogAsync(Memory<byte> packet)
        {
            if (packet.Length < 8)
                return Task.CompletedTask;

            ushort npc = (ushort)(packet.Span[4] | (packet.Span[5] << 8));
            GensType gens = npc switch
            {
                DuprianNpcNumber => GensType.Duprian,
                VanertNpcNumber => GensType.Vanert,
                _ => GensType.Undefined,
            };
            if (gens != GensType.Undefined)
                MuGame.ScheduleOnMainThread(() => Gens.RaiseNpcDialog(gens));
            else // Any other NpcDialog NPC is a quest NPC: OpenMU answers F6 30 with the available quests (F6 0A).
                _ = _networkManager.GetCharacterService().SendAvailableQuestsRequestAsync();
            return Task.CompletedTask;
        }

        /// <summary>GensJoinResponse (0xF8 02): result @4, gens @5.</summary>
        [PacketHandler(0xF8, 0x02)]
        public Task HandleJoinResponseAsync(Memory<byte> packet)
        {
            if (packet.Length < 6)
                return Task.CompletedTask;

            string text = packet.Span[4] switch
            {
                0 => "You have joined the gens.",
                1 => "You are already a member of a gens.",
                2 => "You left a gens recently. Try again later.",
                3 => "Your level is too low to join a gens.",
                4 => "Your guild belongs to a different gens.",
                5 => "Your guild master is not a member of the gens.",
                6 => "You can't join a gens while in a party.",
                7 => "Members of a guild alliance can't join a gens.",
                var r => $"Joining the gens failed ({r}).",
            };
            MuGame.ScheduleOnMainThread(() => Gens.RaiseResult(text));
            return Task.CompletedTask;
        }

        /// <summary>GensLeaveResponse (0xF8 04): result @4.</summary>
        [PacketHandler(0xF8, 0x04)]
        public Task HandleLeaveResponseAsync(Memory<byte> packet)
        {
            if (packet.Length < 5)
                return Task.CompletedTask;

            string text = packet.Span[4] switch
            {
                0 => "You have left the gens.",
                1 => "You are not a member of a gens.",
                2 => "A guild master can't leave the gens.",
                3 => "You are a member of a different gens than this NPC.",
                var r => $"Leaving the gens failed ({r}).",
            };
            MuGame.ScheduleOnMainThread(() => Gens.RaiseResult(text));
            return Task.CompletedTask;
        }

        /// <summary>GensRewardResponse (0xF8 0A): result @4.</summary>
        [PacketHandler(0xF8, 0x0A)]
        public Task HandleRewardResponseAsync(Memory<byte> packet)
        {
            if (packet.Length < 5)
                return Task.CompletedTask;

            string text = packet.Span[4] switch
            {
                0 => "You received the gens reward.",
                1 => "Rewards are only given out during the reward period.",
                2 => "You are not eligible for a reward.",
                3 => "Your inventory has not enough space for the reward.",
                4 => "You already received the reward.",
                5 => "You are a member of a different gens than this NPC.",
                6 => "You are not a member of a gens.",
                var r => $"Gens reward failed ({r}).",
            };
            MuGame.ScheduleOnMainThread(() => Gens.RaiseResult(text));
            return Task.CompletedTask;
        }

        /// <summary>AssignCharactersToGens (C2 0xF8 05): PlayerCount @5, then 16 byte entries from @6: gens @0,
        /// player id @1 (BE), ranking position @4, rank @8, contribution @12 (LE ints).</summary>
        [PacketHandler(0xF8, 0x05)]
        public Task HandleAssignCharactersToGensAsync(Memory<byte> packet)
        {
            var span = packet.Span;
            if (span.Length < 6)
                return Task.CompletedTask;

            int count = Math.Min(span[5], (span.Length - 6) / 16);
            for (int i = 0; i < count; i++)
            {
                var entry = span.Slice(6 + i * 16, 16);
                ushort playerId = (ushort)(BinaryPrimitives.ReadUInt16BigEndian(entry[1..]) & 0x7FFF);
                Gens.AssignPlayer(playerId, (GensType)entry[0], BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]));
            }
            return Task.CompletedTask;
        }

        /// <summary>GensInfo (0xF8 07): gens @4, ranking position @8, rank @12, contribution @16, missing points for next rank @20 (LE ints).</summary>
        [PacketHandler(0xF8, 0x07)]
        public Task HandleGensInfoAsync(Memory<byte> packet)
        {
            if (packet.Length < 24)
            {
                _logger.LogWarning("GensInfo packet too short: {Length}", packet.Length);
                return Task.CompletedTask;
            }

            var span = packet.Span;
            var gens = (GensType)span[4];
            uint position = BitConverter.ToUInt32(span.Slice(8, 4));
            uint rank = BitConverter.ToUInt32(span.Slice(12, 4));
            uint contribution = BitConverter.ToUInt32(span.Slice(16, 4));
            uint next = BitConverter.ToUInt32(span.Slice(20, 4));
            MuGame.ScheduleOnMainThread(() => Gens.SetInfo(gens, position, rank, contribution, next));
            return Task.CompletedTask;
        }
    }
}
