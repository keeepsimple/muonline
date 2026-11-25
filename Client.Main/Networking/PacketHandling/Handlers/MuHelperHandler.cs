using System;
using System.Threading.Tasks;
using Client.Main.Core.Client;
using Client.Main.Core.Utilities;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Network.Packets.ServerToClient;

namespace Client.Main.Networking.PacketHandling.Handlers
{
    /// <summary>
    /// MU Helper packets: the stored configuration (0xAE, sent on enter-game and after a save) and the
    /// start / stop / zen charge status (0xBF 0x51). Failures (level, zen) arrive as server messages.
    /// </summary>
    internal sealed class MuHelperHandler : IGamePacketHandler
    {
        private readonly ILogger<MuHelperHandler> _logger;
        private readonly CharacterState _characterState;

        public MuHelperHandler(ILoggerFactory loggerFactory, CharacterState characterState)
        {
            _logger = loggerFactory.CreateLogger<MuHelperHandler>();
            _characterState = characterState;
        }

        [PacketHandler(0xAE, PacketRouter.NoSubCode)] // MuHelperConfigurationData
        public Task HandleConfigurationDataAsync(Memory<byte> packet)
        {
            if (packet.Length < MuHelperConfigurationData.Length)
                return Task.CompletedTask;

            var config = MuHelperConfig.Parse(new MuHelperConfigurationData(packet).HelperData);
            string owner = _characterState.Name;
            _logger.LogDebug("MU Helper configuration received for {Name}.", owner);
            MuGame.ScheduleOnMainThread(() => MuHelperState.SetConfig(owner, config));
            return Task.CompletedTask;
        }

        [PacketHandler(0xBF, 0x51)] // MuHelperStatusUpdate
        public Task HandleStatusUpdateAsync(Memory<byte> packet)
        {
            if (packet.Length < MuHelperStatusUpdate.Length)
                return Task.CompletedTask;

            var status = new MuHelperStatusUpdate(packet);
            bool consumeMoney = status.ConsumeMoney, paused = status.PauseStatus;
            uint money = status.Money;
            _logger.LogDebug("MU Helper status: ConsumeMoney={Consume}, Money={Money}, Paused={Paused}.", consumeMoney, money, paused);
            MuGame.ScheduleOnMainThread(() => MuHelperState.ApplyStatus(consumeMoney, money, paused));
            return Task.CompletedTask;
        }
    }
}
