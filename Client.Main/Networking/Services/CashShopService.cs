#nullable enable
using System;
using System.Buffers.Binary;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Network;

namespace Client.Main.Networking.Services
{
    /// <summary>
    /// Cash shop requests (C1 0xD2, OpenMU ClientToServerPackets.xml). Written by hand: the NuGet 0.9.8 structs of the
    /// gift, storage list and use requests have other lengths / offsets than the server (e.g. 234 instead of 238 bytes).
    /// </summary>
    public sealed class CashShopService
    {
        private const byte StorageInventoryType = (byte)'S';
        private const byte GiftInventoryType = (byte)'G';
        private const int NameLength = 11;
        private const int GiftTextLength = 200;

        private readonly ConnectionManager _connectionManager;
        private readonly ILogger<CashShopService> _logger;

        public CashShopService(ConnectionManager connectionManager, ILogger<CashShopService> logger)
        {
            _connectionManager = connectionManager;
            _logger = logger;
        }

        /// <summary>CashShopPointInfoRequest (D2 01).</summary>
        public Task<bool> SendPointInfoRequestAsync() => SendAsync("cash shop point info request", 0x01, 4);

        /// <summary>CashShopOpenState (D2 02): opening is answered with CashShopOpenStateResponse, closing is not.</summary>
        public Task<bool> SendOpenStateAsync(bool isClosed)
            => SendAsync(isClosed ? "cash shop close" : "cash shop open request", 0x02, 5, p => p[4] = isClosed ? (byte)1 : (byte)0);

        /// <summary>CashShopItemBuyRequest (D2 03): price sequence 0 when the package has a single price option (or is a bundle).</summary>
        public Task<bool> SendBuyRequestAsync(int packageSeq, int categorySeq, int priceSeq, int cashType)
            => SendAsync("cash shop buy request", 0x03, 23, p =>
            {
                BinaryPrimitives.WriteInt32LittleEndian(p[4..], packageSeq);
                BinaryPrimitives.WriteInt32LittleEndian(p[8..], categorySeq);
                BinaryPrimitives.WriteInt32LittleEndian(p[12..], priceSeq);
                BinaryPrimitives.WriteInt32LittleEndian(p[18..], cashType);
            });

        /// <summary>CashShopItemGiftRequest (D2 04).</summary>
        public Task<bool> SendGiftRequestAsync(int packageSeq, int categorySeq, int priceSeq, int cashType, string receiver, string message)
            => SendAsync("cash shop gift request", 0x04, 238, p =>
            {
                BinaryPrimitives.WriteInt32LittleEndian(p[4..], packageSeq);
                BinaryPrimitives.WriteInt32LittleEndian(p[8..], categorySeq);
                BinaryPrimitives.WriteInt32LittleEndian(p[12..], priceSeq);
                BinaryPrimitives.WriteInt32LittleEndian(p[22..], cashType);
                WriteString(p.Slice(27, NameLength), receiver);
                WriteString(p.Slice(38, GiftTextLength), message);
            });

        /// <summary>CashShopStorageListRequest (D2 05): one-based page of the storage or the gift storage.</summary>
        public Task<bool> SendStorageListRequestAsync(int page, bool gift)
            => SendAsync("cash shop storage list request", 0x05, 10, p =>
            {
                BinaryPrimitives.WriteInt32LittleEndian(p[4..], page);
                p[8] = gift ? GiftInventoryType : StorageInventoryType;
            });

        /// <summary>CashShopStorageItemConsumeRequest (D2 0B): echoes storage index and item sequence of the storage item.</summary>
        public Task<bool> SendUseStorageItemAsync(int storageIndex, int itemSequence, byte productType)
            => SendAsync("cash shop storage item use", 0x0B, 15, p =>
            {
                BinaryPrimitives.WriteInt32LittleEndian(p[4..], storageIndex);
                BinaryPrimitives.WriteInt32LittleEndian(p[8..], itemSequence);
                p[14] = productType;
            });

        // Null-terminated, truncated to the field (the remaining bytes stay zero).
        private static void WriteString(Span<byte> target, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            bytes.AsSpan(0, Math.Min(bytes.Length, target.Length - 1)).CopyTo(target);
        }

        private delegate void BodyWriter(Span<byte> packet);

        private async Task<bool> SendAsync(string description, byte subCode, int length, BodyWriter? write = null)
        {
            if (!_connectionManager.IsConnected)
            {
                _logger.LogError("Not connected - cannot send {Description}.", description);
                return false;
            }

            try
            {
                await _connectionManager.Connection.SendAsync(() =>
                {
                    var packet = _connectionManager.Connection.Output.GetSpan(length)[..length];
                    packet.Clear();
                    packet[0] = 0xC1;
                    packet[1] = (byte)length;
                    packet[2] = 0xD2;
                    packet[3] = subCode;
                    write?.Invoke(packet);
                    return length;
                });
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
