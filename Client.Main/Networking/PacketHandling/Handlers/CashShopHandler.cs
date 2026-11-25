#nullable enable
using System;
using System.Buffers.Binary;
using System.Text;
using System.Threading.Tasks;
using Client.Main.Core.Client.CashShop;
using Client.Main.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Client.Main.Networking.PacketHandling.Handlers
{
    /// <summary>
    /// Handles the cash shop packets of OpenMU (C1 0xD2). They are not part of the NuGet packet library (0.9.8),
    /// so they are parsed by hand; layouts follow ServerToClientPackets.xml.
    /// </summary>
    public class CashShopHandler : IGamePacketHandler
    {
        private readonly ILogger<CashShopHandler> _logger;
        private readonly NetworkManager _networkManager;

        public CashShopHandler(ILoggerFactory loggerFactory, NetworkManager networkManager)
        {
            _logger = loggerFactory.CreateLogger<CashShopHandler>();
            _networkManager = networkManager;
        }

        private CashShopManager CashShop => _networkManager.GetCashShopManager();

        /// <summary>CashShopPointInfo (D2 01): ViewType @4, doubles TotalCash @5, WCoinC @13, WCoinP @21, TotalPoints @29, GoblinPoints @37.</summary>
        [PacketHandler(0xD2, 0x01)]
        public Task HandlePointInfoAsync(Memory<byte> packet)
        {
            if (packet.Length < 45)
                return Task.CompletedTask;

            var span = packet.Span;
            double wCoinC = BinaryPrimitives.ReadDoubleLittleEndian(span[13..]);
            double wCoinP = BinaryPrimitives.ReadDoubleLittleEndian(span[21..]);
            double goblin = BinaryPrimitives.ReadDoubleLittleEndian(span[37..]);
            MuGame.ScheduleOnMainThread(() => CashShop.SetPoints(wCoinC, wCoinP, goblin));
            return Task.CompletedTask;
        }

        /// <summary>CashShopOpenStateResponse (D2 02): IsAllowed @4.</summary>
        [PacketHandler(0xD2, 0x02)]
        public Task HandleOpenStateAsync(Memory<byte> packet)
        {
            if (packet.Length < 5)
                return Task.CompletedTask;

            bool allowed = packet.Span[4] != 0;
            MuGame.ScheduleOnMainThread(() => CashShop.SetOpenState(allowed));
            return Task.CompletedTask;
        }

        /// <summary>CashShopItemBuyResult (D2 03): Result @4.</summary>
        [PacketHandler(0xD2, 0x03)]
        public Task HandleBuyResultAsync(Memory<byte> packet)
        {
            if (packet.Length < 5)
                return Task.CompletedTask;

            byte result = packet.Span[4];
            string text = result switch
            {
                0 => "The item was bought. It is in your cash shop storage.",
                1 => "Not enough coins.",
                2 => "Your cash shop storage is full.",
                3 => "The item is sold out.",
                4 => "The item is not available currently.",
                5 => "The item is no longer available.",
                6 => "The item cannot be bought.",
                7 => "Event items cannot be bought.",
                8 => "The event item limit is exceeded.",
                9 => "This item cannot be bought with this coin.",
                _ => $"Buying failed ({result}).",
            };
            Raise(result == 0, text);
            return Task.CompletedTask;
        }

        /// <summary>CashShopItemGiftResult (D2 04): Result @4.</summary>
        [PacketHandler(0xD2, 0x04)]
        public Task HandleGiftResultAsync(Memory<byte> packet)
        {
            if (packet.Length < 5)
                return Task.CompletedTask;

            byte result = packet.Span[4];
            string text = result switch
            {
                0 => "The gift was sent.",
                1 => "Not enough coins.",
                2 => "The storage of the receiver is full.",
                3 or 20 => "The receiver was not found.",
                4 => "The item is sold out.",
                5 or 6 => "The item is no longer available.",
                7 => "The item cannot be gifted.",
                8 => "Event items cannot be gifted.",
                9 => "The event item gift limit is exceeded.",
                10 => "This item cannot be gifted with this coin.",
                _ => $"Sending the gift failed ({result}).",
            };
            Raise(result == 0, text);
            return Task.CompletedTask;
        }

        /// <summary>CashShopStorageListResponse (D2 06): TotalItemCount @4, PageItemCount @6, PageIndex @8, TotalPages @10 (LE shorts).</summary>
        [PacketHandler(0xD2, 0x06)]
        public Task HandleStorageListAsync(Memory<byte> packet)
        {
            if (packet.Length < 12)
                return Task.CompletedTask;

            var span = packet.Span;
            int total = BinaryPrimitives.ReadInt16LittleEndian(span[4..]);
            int page = BinaryPrimitives.ReadInt16LittleEndian(span[8..]);
            int pages = BinaryPrimitives.ReadInt16LittleEndian(span[10..]);
            MuGame.ScheduleOnMainThread(() => CashShop.BeginStoragePage(page, pages, total));
            return Task.CompletedTask;
        }

        /// <summary>CashShopStorageItem (D2 0D): one item of the storage page.</summary>
        [PacketHandler(0xD2, 0x0D)]
        public Task HandleStorageItemAsync(Memory<byte> packet)
        {
            if (packet.Length >= 33)
                AddStorageItem(packet.Span, sender: null, message: null);
            return Task.CompletedTask;
        }

        /// <summary>CashShopGiftStorageItem (D2 0E): like 0D, plus SenderName @33 (11) and Message @44 (200).</summary>
        [PacketHandler(0xD2, 0x0E)]
        public Task HandleGiftStorageItemAsync(Memory<byte> packet)
        {
            if (packet.Length >= 244)
            {
                var span = packet.Span;
                AddStorageItem(span, ReadString(span.Slice(33, 11)), ReadString(span.Slice(44, 200)));
            }
            return Task.CompletedTask;
        }

        /// <summary>CashShopStorageItemConsumeResult (D2 0B): Result @4.</summary>
        [PacketHandler(0xD2, 0x0B)]
        public Task HandleConsumeResultAsync(Memory<byte> packet)
        {
            if (packet.Length < 5)
                return Task.CompletedTask;

            byte result = packet.Span[4];
            string text = result switch
            {
                0 => "The item was moved into your inventory.",
                1 => "The item was not found.",
                2 => "The item can only be used in a PC cafe.",
                3 or 4 => "A conflicting plan is active.",
                21 => "Your inventory is full.",
                22 => "The item cannot be used.",
                24 => "A conflicting item is active.",
                _ => $"Using the item failed ({result}).",
            };
            Raise(result == 0, text);
            return Task.CompletedTask;
        }

        /// <summary>CashShopScriptVersion (D2 0C): SaleZone @4, Year @6, YearId @8 (LE shorts). Unlocks the shop.</summary>
        [PacketHandler(0xD2, 0x0C)]
        public Task HandleScriptVersionAsync(Memory<byte> packet)
        {
            if (packet.Length < 10)
                return Task.CompletedTask;

            var span = packet.Span;
            int saleZone = BinaryPrimitives.ReadInt16LittleEndian(span[4..]);
            int year = BinaryPrimitives.ReadInt16LittleEndian(span[6..]);
            int yearId = BinaryPrimitives.ReadInt16LittleEndian(span[8..]);
            _logger.LogInformation("Cash shop script version {Version}.", CashShopCatalog.ScriptFolder(saleZone, year, yearId));
            MuGame.ScheduleOnMainThread(() => CashShop.SetScriptVersion(saleZone, year, yearId));
            return Task.CompletedTask;
        }

        // The banner (D2 15) is only an image of the original client; nothing to show.
        [PacketHandler(0xD2, 0x15)]
        public Task HandleBannerVersionAsync(Memory<byte> packet) => Task.CompletedTask;

        /// <summary>PeriodItemListCount (D2 11): a full list of period item expirations follows.</summary>
        [PacketHandler(0xD2, 0x11)]
        public Task HandlePeriodItemListCountAsync(Memory<byte> packet)
        {
            _networkManager.GetCharacterState().ClearItemExpirations();
            return Task.CompletedTask;
        }

        /// <summary>PeriodItemInfo (D2 12): ItemSlot @6 (LE short), ExpirationDate @8 (LE int, Unix seconds). Shown in the item tooltip.</summary>
        [PacketHandler(0xD2, 0x12)]
        public Task HandlePeriodItemInfoAsync(Memory<byte> packet)
        {
            if (packet.Length < 12)
            {
                _logger.LogWarning("PeriodItemInfo packet too short: {Length}", packet.Length);
                return Task.CompletedTask;
            }

            var span = packet.Span;
            ushort slot = BinaryPrimitives.ReadUInt16LittleEndian(span[6..]);
            uint seconds = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
            if (slot <= byte.MaxValue)
                _networkManager.GetCharacterState().SetItemExpiration((byte)slot, DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime);
            return Task.CompletedTask;
        }

        private void AddStorageItem(ReadOnlySpan<byte> span, string? sender, string? message)
        {
            var entry = new CashShopStorageEntry(
                BinaryPrimitives.ReadInt32LittleEndian(span[4..]),
                BinaryPrimitives.ReadInt32LittleEndian(span[8..]),
                BinaryPrimitives.ReadInt32LittleEndian(span[16..]),
                BinaryPrimitives.ReadInt32LittleEndian(span[20..]),
                span[32],
                sender,
                message);
            MuGame.ScheduleOnMainThread(() => CashShop.AddStorageItem(entry));
        }

        private void Raise(bool success, string text)
            => MuGame.ScheduleOnMainThread(() => CashShop.RaiseResult(success, text));

        private static string ReadString(ReadOnlySpan<byte> field)
        {
            int end = field.IndexOf((byte)0);
            return Encoding.UTF8.GetString(end >= 0 ? field[..end] : field);
        }
    }
}
