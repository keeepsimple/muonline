#nullable enable
using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Network;

namespace Client.Main.Networking.Services
{
    /// <summary>
    /// Castle Siege requests (OpenMU ClientToServerPackets.xml, codes 0xB2-0xB9). The packets are written by hand
    /// from the server specification because the OpenMU packet NuGet predates the siege implementation.
    /// </summary>
    public sealed class CastleSiegeService
    {
        private readonly ConnectionManager _connectionManager;
        private readonly ILogger<CastleSiegeService> _logger;

        public CastleSiegeService(ConnectionManager connectionManager, ILogger<CastleSiegeService> logger)
        {
            _connectionManager = connectionManager;
            _logger = logger;
        }

        /// <summary>Siege status: owner, state and period (0xB2/0x00).</summary>
        public Task<bool> SendStatusRequestAsync() => SendAsync("castle siege status request", 0xB2, 0x00);

        /// <summary>Registers the own guild for the next siege (0xB2/0x01, guild master).</summary>
        public Task<bool> SendRegisterGuildAsync() => SendAsync("castle siege registration", 0xB2, 0x01);

        /// <summary>Withdraws the own guild's registration (0xB2/0x02).</summary>
        public Task<bool> SendUnregisterGuildAsync() => SendAsync("castle siege unregistration", 0xB2, 0x02, 1);

        /// <summary>Own guild registration state: marks and rank (0xB2/0x03).</summary>
        public Task<bool> SendRegistrationStateRequestAsync() => SendAsync("castle siege registration state request", 0xB2, 0x03);

        /// <summary>Registers the Sign of Lord in the given (absolute) inventory slot (0xB2/0x04).</summary>
        public Task<bool> SendRegisterMarkAsync(byte inventorySlot)
            => SendPacketAsync("castle siege mark registration", BuildMarkRegistrationPacket(inventorySlot));

        /// <summary>
        /// B2/04 carries the backpack-grid index: OpenMU adds the 12 equipment slots itself
        /// (CastleSiegeMarkRegistrationHandlerPlugIn.TryGetInventorySlot).
        /// </summary>
        public static byte[] BuildMarkRegistrationPacket(byte inventorySlot)
            => BuildPacket(0xB2, 0x04, new[] { (byte)(inventorySlot - FirstBackpackSlot) });

        private const byte FirstBackpackSlot = 12;

        /// <summary>Repairs a gate or statue (0xB2/0x06, castle owner).</summary>
        public Task<bool> SendDefenseRepairAsync(uint npcNumber, uint npcIndex)
        {
            var body = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(body, npcNumber);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), npcIndex);
            return SendAsync("castle siege defense repair", 0xB2, 0x06, body);
        }

        /// <summary>Tax rates and treasury (0xB2/0x08, castle owner).</summary>
        public Task<bool> SendTaxInfoRequestAsync() => SendAsync("castle siege tax info request", 0xB2, 0x08);

        /// <summary>Changes a tax rate (0xB2/0x09): type 1 = Chaos Machine, 2 = Store (0-3 %), 3 = hunting zone fee.</summary>
        public Task<bool> SendTaxChangeAsync(byte taxType, uint value)
        {
            var body = new byte[5];
            body[0] = taxType;
            BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(1), value);
            return SendAsync("castle siege tax change", 0xB2, 0x09, body);
        }

        /// <summary>Withdraws Zen from the castle treasury (0xB2/0x10).</summary>
        public Task<bool> SendTributeWithdrawAsync(uint amount)
        {
            var body = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(body, amount);
            return SendAsync("castle siege tribute withdraw", 0xB2, 0x10, body);
        }

        /// <summary>Opens or closes the gate of the talked-to lever (0xB2/0x12).</summary>
        public Task<bool> SendGateToggleAsync(bool open, ushort gateId)
        {
            var body = new byte[3];
            body[0] = open ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(1), gateId);
            return SendAsync("castle siege gate toggle", 0xB2, 0x12, body);
        }

        /// <summary>Gate (0xB3/0x01) or Guardian Statue (0xB3/0x02) list of the Senior's defense window.</summary>
        public Task<bool> SendDefenseListRequestAsync(bool statues) => SendAsync("castle siege defense list request", 0xB3, statues ? (byte)0x02 : (byte)0x01);

        /// <summary>Guilds registered for the next siege (0xB4).</summary>
        public Task<bool> SendRegisteredGuildListRequestAsync() => SendAsync("castle siege registered guild list request", 0xB4, null);

        /// <summary>Guilds selected for the battle with their sides (0xB5).</summary>
        public Task<bool> SendParticipantListRequestAsync() => SendAsync("castle siege participant list request", 0xB5, null);

        /// <summary>Fires the operated catapult at a target zone (0xB7/0x01, zone 1-based).</summary>
        public Task<bool> SendFireMachineAsync(ushort machineId, byte zone)
        {
            var body = new byte[3];
            BinaryPrimitives.WriteUInt16BigEndian(body, machineId);
            body[2] = zone;
            return SendAsync("castle siege machine fire", 0xB7, 0x01, body);
        }

        /// <summary>Guild mark of the castle owner (0xB9/0x02).</summary>
        public Task<bool> SendOwnerLogoRequestAsync() => SendAsync("castle owner logo request", 0xB9, 0x02);

        /// <summary>Enters the Land of Trials through the talked-to guard (0xB9/0x05); the server charges the fee itself.</summary>
        public Task<bool> SendHuntZoneEnterAsync(uint fee)
        {
            var body = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(body, fee);
            return SendAsync("castle siege hunting zone enter", 0xB9, 0x05, body);
        }

        private Task<bool> SendAsync(string description, byte code, byte? subCode, params byte[] body)
            => SendPacketAsync(description, BuildPacket(code, subCode, body));

        /// <summary>C1 packet: code, optional sub-code, body.</summary>
        private static byte[] BuildPacket(byte code, byte? subCode, byte[] body)
        {
            int header = subCode.HasValue ? 4 : 3;
            var packet = new byte[header + body.Length];
            packet[0] = 0xC1;
            packet[1] = (byte)packet.Length;
            packet[2] = code;
            if (subCode.HasValue) packet[3] = subCode.Value;
            body.CopyTo(packet, header);
            return packet;
        }

        private Task<bool> SendPacketAsync(string description, byte[] packet)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send {Description}.", description);
                return Task.FromResult(false);
            }

            return SendCoreAsync(description, packet);
        }

        private async Task<bool> SendCoreAsync(string description, byte[] packet)
        {
            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    packet.CopyTo(_connectionManager.Connection.Output.GetSpan(packet.Length));
                    return packet.Length;
                });
                _logger.LogInformation("{Description} sent.", description);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending {Description}.", description);
                return false;
            }
        }
    }
}
