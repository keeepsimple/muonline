#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Client.Main.Core.Client.CashShop
{
    public sealed record CashShopCategory(int Seq, string Name, int DisplayOrder);

    /// <summary>One line of IBSProduct.txt: a property (quantity, period, ...) of a price option.</summary>
    public sealed record CashShopProductLine(int ProductSeq, string Name, string Value, string Unit, int Price, int PriceSeq);

    public sealed record CashShopPackage(int CategorySeq, int Seq, string Name, int Price, bool IsForSale, bool IsGiftable,
        int[] ProductSeqs, int ItemCode, int[] PriceSeqs, int CashType)
    {
        /// <summary>A bundle has no price options; it delivers all its products for the package price.</summary>
        public bool IsBundle => PriceSeqs.Length == 0;
    }

    /// <summary>
    /// In-game shop catalog from the client script (Data/InGameShopScript/[SaleZone].[Year].[YearId]/), whose version the
    /// server sends with CashShopScriptVersion (0xD2 0C). OpenMU has no catalog packets: it only knows the script's
    /// sequence numbers (server docs/CashShop.md). Field layout: MuMain GameShop/ShopListManager (ShopCategory.cpp,
    /// ShopPackage.cpp, ShopProduct.cpp): '@' separated records, '|' separated sub-lists.
    /// </summary>
    public sealed class CashShopCatalog
    {
        public const int WCoinCCashType = 508;
        public const int WCoinPCashType = 509;
        public const int GoblinPointsCashType = 0;
        private const int SalesFlagForSale = 182;
        private const int GiftFlagGiftable = 184;

        public IReadOnlyList<CashShopCategory> Categories { get; }
        public IReadOnlyList<CashShopPackage> Packages { get; }
        private readonly Dictionary<int, List<CashShopProductLine>> _linesByPriceSeq;

        private CashShopCatalog(IReadOnlyList<CashShopCategory> categories, IReadOnlyList<CashShopPackage> packages, IEnumerable<CashShopProductLine> lines)
        {
            Packages = packages;
            // Only categories which hold a package are shown.
            Categories = categories.Where(c => packages.Any(p => p.CategorySeq == c.Seq)).OrderBy(c => c.DisplayOrder).ToList();
            _linesByPriceSeq = lines.GroupBy(l => l.PriceSeq).ToDictionary(g => g.Key, g => g.ToList());
        }

        public static string ScriptFolder(int saleZone, int year, int yearId) => $"{saleZone}.{year}.{yearId:D3}";

        /// <summary>Loads the script of the given version from the data folder; null if its files are missing.</summary>
        public static CashShopCatalog? TryLoad(string dataPath, int saleZone, int year, int yearId)
        {
            string folder = Path.Combine(dataPath ?? string.Empty, "InGameShopScript", ScriptFolder(saleZone, year, yearId));
            string category = Path.Combine(folder, "IBSCategory.txt");
            string package = Path.Combine(folder, "IBSPackage.txt");
            string product = Path.Combine(folder, "IBSProduct.txt");
            if (!File.Exists(category) || !File.Exists(package) || !File.Exists(product)) return null;
            return Parse(File.ReadAllLines(category), File.ReadAllLines(package), File.ReadAllLines(product));
        }

        public static CashShopCatalog Parse(IEnumerable<string> categoryLines, IEnumerable<string> packageLines, IEnumerable<string> productLines)
        {
            var categories = new List<CashShopCategory>();
            foreach (var f in Records(categoryLines, 5))
                categories.Add(new CashShopCategory(Int(f[0]), f[1], Int(f[5])));

            // A package is listed once per category it is in; the server identifies it by its sequence alone.
            var packages = new List<CashShopPackage>();
            foreach (var f in Records(packageLines, 25))
                packages.Add(new CashShopPackage(Int(f[0]), Int(f[2]), f[3], Int(f[5]), Int(f[8]) == SalesFlagForSale,
                    Int(f[9]) == GiftFlagGiftable, IntList(f[19]), Int(f[20]), IntList(f[23]), Int(f[25])));

            var lines = new List<CashShopProductLine>();
            foreach (var f in Records(productLines, 6))
                lines.Add(new CashShopProductLine(Int(f[0]), f[1], f[3], f[4], Int(f[5]), Int(f[6])));

            return new CashShopCatalog(categories, packages, lines);
        }

        public IEnumerable<CashShopPackage> GetPackages(int categorySeq) => Packages.Where(p => p.CategorySeq == categorySeq && p.IsForSale);

        public IReadOnlyList<CashShopProductLine> GetPriceOption(int priceSeq)
            => _linesByPriceSeq.TryGetValue(priceSeq, out var list) ? list : Array.Empty<CashShopProductLine>();

        /// <summary>Price option label: price and its properties, e.g. "150 - 1 Day(s)".</summary>
        public string DescribePriceOption(int priceSeq)
        {
            var lines = GetPriceOption(priceSeq);
            if (lines.Count == 0) return $"Option #{priceSeq}";
            string properties = string.Join(", ", lines.Where(l => l.Value.Length > 0).Select(l => $"{l.Value} {l.Unit}".Trim()));
            return properties.Length > 0 ? $"{lines[0].Price} - {properties}" : lines[0].Price.ToString();
        }

        /// <summary>Name of a product (storage items name the product sequence), null if the script lacks it.</summary>
        public string? GetProductName(int productSeq)
            => _linesByPriceSeq.Values.SelectMany(l => l).FirstOrDefault(l => l.ProductSeq == productSeq)?.Name;

        public static string CoinName(int cashType) => cashType switch
        {
            WCoinCCashType => "WCoin(C)",
            WCoinPCashType => "WCoin(P)",
            GoblinPointsCashType => "Goblin Points",
            _ => $"coin {cashType}",
        };

        private static IEnumerable<string[]> Records(IEnumerable<string> lines, int lastIndex)
        {
            foreach (string line in lines)
            {
                var fields = line.TrimEnd('\r').Split('@');
                if (fields.Length > lastIndex && int.TryParse(fields[0], out _))
                    yield return fields;
            }
        }

        private static int Int(string value) => int.TryParse(value.Trim(), out int result) ? result : 0;

        private static int[] IntList(string value)
            => value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Int).Where(v => v != 0).ToArray();
    }
}
