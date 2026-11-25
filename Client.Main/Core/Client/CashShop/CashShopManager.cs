#nullable enable
using System;
using System.Collections.Generic;

namespace Client.Main.Core.Client.CashShop
{
    /// <summary>An item of the cash shop storage (0xD2 0D) or gift storage (0xD2 0E).</summary>
    public sealed record CashShopStorageEntry(int StorageIndex, int ItemSequence, int ProductSeq, int PriceSeq, byte ItemType,
        string? Sender, string? Message);

    /// <summary>
    /// Cash shop (in-game shop, 0xD2) state of the account: script version, coin balances and the shown storage page.
    /// Filled by CashShopHandler, shown by CashShopWindow.
    /// </summary>
    public sealed class CashShopManager
    {
        private readonly List<CashShopStorageEntry> _storage = new();
        private bool _catalogLoaded;
        private CashShopCatalog? _catalog;

        /// <summary>Script version of CashShopScriptVersion (0xD2 0C); the shop stays locked until it is received.</summary>
        public (int SaleZone, int Year, int YearId)? ScriptVersion { get; private set; }
        public bool IsAvailable => ScriptVersion.HasValue;

        /// <summary>The server allowed opening the shop (CashShopOpenStateResponse) and it was not closed since.</summary>
        public bool IsOpen { get; private set; }

        public double WCoinC { get; private set; }
        public double WCoinP { get; private set; }
        public double GoblinPoints { get; private set; }

        /// <summary>Tab of the last storage request; the list header (0xD2 06) doesn't name it.</summary>
        public bool IsGiftStorage { get; set; }
        public int StoragePage { get; private set; } = 1;
        public int StorageTotalPages { get; private set; }
        public int StorageTotalItems { get; private set; }
        public IReadOnlyList<CashShopStorageEntry> Storage => _storage;

        /// <summary>Catalog from the client script of the server's version, null if the script files are missing.</summary>
        public CashShopCatalog? Catalog
        {
            get
            {
                if (!_catalogLoaded && ScriptVersion is { } v)
                {
                    _catalogLoaded = true;
                    _catalog = CashShopCatalog.TryLoad(Constants.DataPath, v.SaleZone, v.Year, v.YearId);
                }
                return _catalog;
            }
        }

        /// <summary>Points, storage or catalog changed.</summary>
        public event Action? Changed;

        /// <summary>Answer to an open request: allowed or not.</summary>
        public event Action<bool>? OpenStateReceived;

        /// <summary>Result of a buy / gift / use request: (success, message).</summary>
        public event Action<bool, string>? ResultReceived;

        public void SetScriptVersion(int saleZone, int year, int yearId)
        {
            ScriptVersion = (saleZone, year, yearId);
            _catalogLoaded = false;
            _catalog = null;
            Changed?.Invoke();
        }

        public void SetOpenState(bool allowed)
        {
            IsOpen = allowed;
            OpenStateReceived?.Invoke(allowed);
        }

        public void MarkClosed() => IsOpen = false;

        public void SetPoints(double wCoinC, double wCoinP, double goblinPoints)
        {
            WCoinC = wCoinC;
            WCoinP = wCoinP;
            GoblinPoints = goblinPoints;
            Changed?.Invoke();
        }

        /// <summary>Starts a storage page (0xD2 06); its items follow one by one.</summary>
        public void BeginStoragePage(int page, int totalPages, int totalItems)
        {
            StoragePage = Math.Max(1, page);
            StorageTotalPages = totalPages;
            StorageTotalItems = totalItems;
            _storage.Clear();
            Changed?.Invoke();
        }

        public void AddStorageItem(CashShopStorageEntry entry)
        {
            _storage.Add(entry);
            Changed?.Invoke();
        }

        internal void RaiseResult(bool success, string message) => ResultReceived?.Invoke(success, message);

        /// <summary>Drops everything tied to the current session (disconnect / character change).</summary>
        public void Reset()
        {
            ScriptVersion = null;
            IsOpen = false;
            _catalogLoaded = false;
            _catalog = null;
            WCoinC = WCoinP = GoblinPoints = 0;
            IsGiftStorage = false;
            BeginStoragePage(1, 0, 0);
        }
    }
}
