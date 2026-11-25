#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Client.Main.Controllers;
using Client.Main.Controls.UI.Common;
using Client.Main.Controls.UI.Game.Common;
using Client.Main.Controls.UI.Game.Friends;
using Client.Main.Controls.UI.Game.Inventory;
using Client.Main.Core.Client.CashShop;
using Client.Main.Models;
using Client.Main.Networking.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Controls.UI.Game.CashShop
{
    /// <summary>
    /// In-game cash shop (X): coin balances, the catalog of the client script by category, buy and gift, and the
    /// storage / gift storage whose items are used into the inventory. Protocol: OpenMU docs/CashShop.md (0xD2).
    /// </summary>
    public sealed class CashShopWindow : MessengerWindowBase
    {
        private enum Tab { Shop, Storage, Gifts }

        private const int Width = 520;
        private const int TabY = HeaderHeight + 8;
        private const int TabHeight = 24;
        private const int SelectorY = TabY + TabHeight + 8;
        private const int SelectorHeight = 24;
        private const int ListY = SelectorY + SelectorHeight + 6;
        private const int RowHeight = 20;
        private const int VisibleRows = 11;
        private const int ListHeight = RowHeight * VisibleRows + 20;
        private const int OptionY = ListY + ListHeight + 6;
        private const int GiftY = OptionY + SelectorHeight + 8;
        private const int StatusY = GiftY + 30;
        private const int ButtonY = StatusY + 22;
        private const int Height = ButtonY + 32 + Pad;
        private const int NameLength = 10;
        private const int GiftTextLength = 199;

        private static readonly string[] TabNames = { "SHOP", "STORAGE", "GIFTS" };
        private static readonly Rectangle ListLocal = new(Pad, ListY, Width - Pad * 2, ListHeight);
        private static readonly Rectangle PrevLocal = new(Pad, SelectorY, 32, SelectorHeight);
        private static readonly Rectangle NextLocal = new(Width - Pad - 32, SelectorY, 32, SelectorHeight);
        private static readonly Rectangle PrevOptionLocal = new(Pad, OptionY, 32, SelectorHeight);
        private static readonly Rectangle NextOptionLocal = new(Width - Pad - 32, OptionY, 32, SelectorHeight);

        private readonly CashShopManager _shop;
        private readonly TextFieldControl _receiver;
        private readonly TextFieldControl _giftText;
        private Tab _tab = Tab.Shop;
        private int _categoryIndex, _optionIndex, _scroll;
        private int? _selectedPackage, _selectedStorage;
        private bool _opening;
        private string? _status;
        private bool _statusIsError;
        private int _hoveredTab = -1, _hoveredRow = -1, _hoveredButton = -1, _hoveredArrow = -1;

        public CashShopWindow(CashShopManager shop) : base(Width, Height)
        {
            _shop = shop;
            Align = ControlAlign.HorizontalCenter | ControlAlign.VerticalCenter;
            _receiver = AddField(Pad, GiftY, 150, NameLength);
            _giftText = AddField(Pad + 158, GiftY, Width - Pad * 2 - 158, GiftTextLength);
            _shop.OpenStateReceived += OnOpenState;
            _shop.ResultReceived += OnResult;
        }

        public override void Dispose()
        {
            _shop.OpenStateReceived -= OnOpenState;
            _shop.ResultReceived -= OnResult;
            base.Dispose();
        }

        protected override string Title => "CASH SHOP";

        protected override string? Subtitle
            => $"WCoin(C) {Format(_shop.WCoinC)}   WCoin(P) {Format(_shop.WCoinP)}   Goblin Points {Format(_shop.GoblinPoints)}";

        private static string Format(double value) => value.ToString("N0", CultureInfo.InvariantCulture);

        private static CashShopService? Service => MuGame.Network?.GetCashShopService();

        // ─────────────────────────────── Open / close ───────────────────────────────

        /// <summary>X: asks the server to open the shop (safezone only); the window shows on its answer.</summary>
        public void ToggleShop()
        {
            if (Visible)
            {
                Hide();
                return;
            }

            if (!_shop.IsAvailable)
            {
                RequestDialog.ShowInfo("The cash shop is not available on this server.");
                return;
            }

            _opening = true;
            _ = Service?.SendOpenStateAsync(isClosed: false);
        }

        private void OnOpenState(bool allowed)
        {
            if (!_opening) return;
            _opening = false;
            if (!allowed)
            {
                RequestDialog.ShowInfo("The cash shop can only be opened in a safe zone, while not trading or talking to an NPC.");
                return;
            }

            _status = null;
            Show();
            _ = Service?.SendPointInfoRequestAsync();
            RequestStorage(_tab == Tab.Gifts, 1);
        }

        public override void Hide()
        {
            if (_shop.IsOpen)
            {
                _shop.MarkClosed();
                _ = Service?.SendOpenStateAsync(isClosed: true);
            }
            base.Hide();
        }

        private void OnResult(bool success, string message)
        {
            _status = message;
            _statusIsError = !success;
            if (!success || !Visible) return;
            // The client asks for the points after every purchase and use, and the storage changed.
            _ = Service?.SendPointInfoRequestAsync();
            RequestStorage(_shop.IsGiftStorage, _shop.StoragePage);
        }

        private void RequestStorage(bool gift, int page)
        {
            _shop.IsGiftStorage = gift;
            _selectedStorage = null;
            _ = Service?.SendStorageListRequestAsync(Math.Max(1, page), gift);
        }

        // ─────────────────────────────── Data ───────────────────────────────

        private CashShopCategory? Category
        {
            get
            {
                var categories = _shop.Catalog?.Categories;
                if (categories == null || categories.Count == 0) return null;
                _categoryIndex = Math.Clamp(_categoryIndex, 0, categories.Count - 1);
                return categories[_categoryIndex];
            }
        }

        private List<CashShopPackage> Packages
            => Category is { } category ? _shop.Catalog!.GetPackages(category.Seq).ToList() : new List<CashShopPackage>();

        private CashShopPackage? SelectedPackage => Packages.FirstOrDefault(p => p.Seq == _selectedPackage);

        private CashShopStorageEntry? SelectedStorage => _shop.Storage.FirstOrDefault(e => e.StorageIndex == _selectedStorage);

        private int RowCount => _tab == Tab.Shop ? Packages.Count : _shop.Storage.Count;

        /// <summary>Price sequence sent with buy / gift: 0 for a single price option or a bundle (server docs).</summary>
        private int SelectedPriceSeq(CashShopPackage package)
            => package.PriceSeqs.Length > 1 ? package.PriceSeqs[Math.Clamp(_optionIndex, 0, package.PriceSeqs.Length - 1)] : 0;

        private string DescribeOption(CashShopPackage package)
        {
            if (package.IsBundle) return $"Bundle - {package.Price}";
            int priceSeq = package.PriceSeqs[Math.Clamp(_optionIndex, 0, package.PriceSeqs.Length - 1)];
            return _shop.Catalog!.DescribePriceOption(priceSeq);
        }

        private int PriceOf(CashShopPackage package)
        {
            if (package.IsBundle) return package.Price;
            var lines = _shop.Catalog!.GetPriceOption(package.PriceSeqs[Math.Clamp(_optionIndex, 0, package.PriceSeqs.Length - 1)]);
            return lines.Count > 0 ? lines[0].Price : package.Price;
        }

        private string StorageName(CashShopStorageEntry entry)
            => _shop.Catalog?.GetProductName(entry.ProductSeq) ?? $"Product #{entry.ProductSeq}";

        // ─────────────────────────────── Actions ───────────────────────────────

        private readonly record struct ButtonSpec(string Text, bool Enabled, Color Accent, Action OnClick);

        private List<ButtonSpec> GetButtons()
        {
            if (_tab == Tab.Shop)
            {
                var package = SelectedPackage;
                return new List<ButtonSpec>
                {
                    new("BUY", package != null, ModernHudTheme.Success, () => Buy(package!)),
                    new("GIFT", package?.IsGiftable == true && _receiver.Value.Trim().Length > 0, ModernHudTheme.Accent, () => Gift(package!)),
                };
            }

            var entry = SelectedStorage;
            return new List<ButtonSpec>
            {
                new("USE", entry != null, ModernHudTheme.Success, () => Use(entry!)),
                new("REFRESH", true, ModernHudTheme.Accent, () => RequestStorage(_tab == Tab.Gifts, _shop.StoragePage)),
            };
        }

        private void Buy(CashShopPackage package)
        {
            int category = Category?.Seq ?? 0;
            int priceSeq = SelectedPriceSeq(package);
            RequestDialog.Show($"Buy {package.Name} ({DescribeOption(package)}) for {PriceOf(package)} {CashShopCatalog.CoinName(package.CashType)}?",
                onAccept: () => _ = Service?.SendBuyRequestAsync(package.Seq, category, priceSeq, package.CashType));
        }

        private void Gift(CashShopPackage package)
        {
            string receiver = _receiver.Value.Trim();
            if (string.Equals(receiver, MuGame.Network?.GetCharacterState()?.Name, StringComparison.OrdinalIgnoreCase))
            {
                _status = "You cannot send a gift to yourself.";
                _statusIsError = true;
                return;
            }

            int category = Category?.Seq ?? 0;
            int priceSeq = SelectedPriceSeq(package);
            string message = _giftText.Value.Trim();
            RequestDialog.Show($"Send {package.Name} ({DescribeOption(package)}) to {receiver} for {PriceOf(package)} {CashShopCatalog.CoinName(package.CashType)}?",
                onAccept: () => _ = Service?.SendGiftRequestAsync(package.Seq, category, priceSeq, package.CashType, receiver, message));
        }

        private void Use(CashShopStorageEntry entry)
            => RequestDialog.Show($"Move {StorageName(entry)} into your inventory?",
                onAccept: () => _ = Service?.SendUseStorageItemAsync(entry.StorageIndex, entry.ItemSequence, entry.ItemType));

        private void Step(int arrow)
        {
            int direction = arrow % 2 == 0 ? -1 : 1;
            if (arrow < 2)
            {
                if (_tab == Tab.Shop)
                {
                    _categoryIndex += direction;
                    _selectedPackage = null;
                    _scroll = 0;
                }
                else
                {
                    int page = _shop.StoragePage + direction;
                    if (page >= 1 && page <= Math.Max(1, _shop.StorageTotalPages)) RequestStorage(_tab == Tab.Gifts, page);
                }
            }
            else if (SelectedPackage is { PriceSeqs.Length: > 1 } package)
            {
                _optionIndex = (_optionIndex + direction + package.PriceSeqs.Length) % package.PriceSeqs.Length;
            }
        }

        // ─────────────────────────────── Update ───────────────────────────────

        private static Rectangle TabLocal(int index)
        {
            int width = (Width - Pad * 2 - 8) / 3;
            return new Rectangle(Pad + index * (width + 4), TabY, width, TabHeight);
        }

        private static Rectangle ButtonLocal(int index, int count)
        {
            int width = (Width - Pad * 2 - (count - 1) * 6) / count;
            return new Rectangle(Pad + index * (width + 6), ButtonY, width, 32);
        }

        private static Rectangle RowLocal(int visibleIndex)
            => new(Pad + 1, ListY + 20 + visibleIndex * RowHeight, Width - Pad * 2 - 2, RowHeight);

        private static readonly Rectangle[] Arrows = { PrevLocal, NextLocal, PrevOptionLocal, NextOptionLocal };

        protected override void UpdateContent(GameTime gameTime, bool mouseInside, int wheel)
        {
            _receiver.Visible = _giftText.Visible = _tab == Tab.Shop;

            int total = RowCount;
            if (wheel != 0 && Translate(ListLocal).Contains(MousePosition)) _scroll -= Math.Sign(wheel) * 3;
            _scroll = Math.Clamp(_scroll, 0, Math.Max(0, total - VisibleRows));

            _hoveredTab = -1;
            for (int i = 0; i < TabNames.Length; i++)
                if (Translate(TabLocal(i)).Contains(MousePosition)) _hoveredTab = i;

            _hoveredRow = -1;
            for (int i = 0; i < VisibleRows && _scroll + i < total; i++)
                if (Translate(RowLocal(i)).Contains(MousePosition)) _hoveredRow = _scroll + i;

            _hoveredArrow = -1;
            for (int i = 0; i < Arrows.Length; i++)
                if ((i < 2 || _tab == Tab.Shop) && Translate(Arrows[i]).Contains(MousePosition)) _hoveredArrow = i;

            var buttons = GetButtons();
            _hoveredButton = -1;
            for (int i = 0; i < buttons.Count; i++)
                if (Translate(ButtonLocal(i, buttons.Count)).Contains(MousePosition)) _hoveredButton = i;
        }

        protected override bool HandleClick()
        {
            if (_hoveredTab >= 0)
            {
                var tab = (Tab)_hoveredTab;
                if (tab != _tab && tab != Tab.Shop) RequestStorage(tab == Tab.Gifts, 1);
                _tab = tab;
                _scroll = 0;
                return true;
            }

            if (_hoveredArrow >= 0)
            {
                Step(_hoveredArrow);
                return true;
            }

            if (_hoveredRow >= 0)
            {
                if (_tab == Tab.Shop)
                {
                    _selectedPackage = Packages[_hoveredRow].Seq;
                    _optionIndex = 0;
                }
                else
                {
                    _selectedStorage = _shop.Storage[_hoveredRow].StorageIndex;
                }
                return true;
            }

            var buttons = GetButtons();
            if (_hoveredButton >= 0 && buttons[_hoveredButton].Enabled)
            {
                buttons[_hoveredButton].OnClick();
                return true;
            }

            return false;
        }

        // ─────────────────────────────── Draw ───────────────────────────────

        protected override void DrawContent(SpriteBatch sb, GameTime gameTime)
        {
            var pixel = GraphicsManager.Instance.Pixel;

            for (int i = 0; i < TabNames.Length; i++)
            {
                bool active = (int)_tab == i;
                PlayerShopUi.DrawButton(sb, Translate(TabLocal(i)), TabNames[i], _hoveredTab == i || active, true,
                    active ? ModernHudTheme.AccentBright : ModernHudTheme.Accent, Alpha, 12f);
            }

            string selector = _tab == Tab.Shop
                ? Category?.Name ?? "-"
                : $"Page {_shop.StoragePage} / {Math.Max(1, _shop.StorageTotalPages)}   ({_shop.StorageTotalItems} items)";
            DrawSelector(sb, 0, selector, SelectorY);

            var list = Translate(ListLocal);
            PlayerShopUi.DrawPanel(sb, list, ModernHudTheme.BgDarkest, Alpha);
            var header = new Rectangle(list.X + 1, list.Y + 1, list.Width - 2, 19);
            sb.Draw(pixel, header, ModernHudTheme.BgMid * Alpha);
            if (_tab == Tab.Shop) DrawPackages(sb, list, header);
            else DrawStorage(sb, list, header);
            DrawScrollBar(sb, new Rectangle(list.X, list.Y + 20, list.Width, RowHeight * VisibleRows), _scroll, VisibleRows, RowCount);

            if (_tab == Tab.Shop)
            {
                var package = SelectedPackage;
                DrawSelector(sb, 2, package != null ? DescribeOption(package) : "Select an item", OptionY);
            }
            else if (_tab == Tab.Gifts && SelectedStorage is { } gift)
            {
                string text = PlayerShopUi.Ellipsize($"From {gift.Sender}: {gift.Message}", 11f, Width - Pad * 2);
                PlayerShopUi.DrawText(sb, text, Translate(new Rectangle(Pad, OptionY + 4, 0, 0)).Location.ToVector2(), ModernHudTheme.TextWhite, 11f, Alpha);
            }

            if (_status != null)
                PlayerShopUi.DrawTextCentered(sb, _status, Translate(new Rectangle(Pad, StatusY, Width - Pad * 2, 16)),
                    _statusIsError ? ModernHudTheme.Danger : ModernHudTheme.TextGold, 11f, Alpha);

            var buttons = GetButtons();
            for (int i = 0; i < buttons.Count; i++)
                PlayerShopUi.DrawButton(sb, Translate(ButtonLocal(i, buttons.Count)), buttons[i].Text, _hoveredButton == i, buttons[i].Enabled, buttons[i].Accent, Alpha, 13f);
        }

        private void DrawSelector(SpriteBatch sb, int firstArrow, string text, int y)
        {
            PlayerShopUi.DrawButton(sb, Translate(Arrows[firstArrow]), "<", _hoveredArrow == firstArrow, true, ModernHudTheme.Accent, Alpha, 12f);
            PlayerShopUi.DrawButton(sb, Translate(Arrows[firstArrow + 1]), ">", _hoveredArrow == firstArrow + 1, true, ModernHudTheme.Accent, Alpha, 12f);
            var area = Translate(new Rectangle(Pad + 38, y, Width - Pad * 2 - 76, SelectorHeight));
            PlayerShopUi.DrawPanel(sb, area, ModernHudTheme.BgDarkest, Alpha);
            PlayerShopUi.DrawTextCentered(sb, PlayerShopUi.Ellipsize(text, 12f, area.Width - 10), area, ModernHudTheme.TextWhite, 12f, Alpha);
        }

        private void DrawRowBackground(SpriteBatch sb, Rectangle row, int index, bool selected)
        {
            var pixel = GraphicsManager.Instance.Pixel;
            if (selected) sb.Draw(pixel, row, ModernHudTheme.Accent * 0.35f * Alpha);
            else if (_hoveredRow == index) sb.Draw(pixel, row, Color.White * 0.06f * Alpha);
            else if (index % 2 == 1) sb.Draw(pixel, row, Color.White * 0.025f * Alpha);
        }

        private void DrawEmpty(SpriteBatch sb, Rectangle list, string text)
        {
            var lines = WrapText(text, 12f, list.Width - 30);
            int y = list.Y + list.Height / 2 - lines.Count * 9;
            foreach (string line in lines)
            {
                PlayerShopUi.DrawTextCentered(sb, line, new Rectangle(list.X, y, list.Width, 16), ModernHudTheme.TextGray, 12f, Alpha);
                y += 18;
            }
        }

        private void DrawPackages(SpriteBatch sb, Rectangle list, Rectangle header)
        {
            int nameX = list.X + 10, priceX = list.Right - 190, coinX = list.Right - 110;
            PlayerShopUi.DrawText(sb, "Item", new Vector2(nameX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);
            PlayerShopUi.DrawText(sb, "Price", new Vector2(priceX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);
            PlayerShopUi.DrawText(sb, "Coin", new Vector2(coinX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);

            if (_shop.Catalog == null)
            {
                // OpenMU sends no catalog: names and prices only exist in the script of the original client.
                string folder = _shop.ScriptVersion is { } v ? CashShopCatalog.ScriptFolder(v.SaleZone, v.Year, v.YearId) : "?";
                DrawEmpty(sb, list, $"The shop catalog comes from the client script Data/InGameShopScript/{folder} " +
                                    "(IBSCategory.txt, IBSPackage.txt, IBSProduct.txt), which is missing. The storage still works.");
                return;
            }

            var packages = Packages;
            for (int i = 0; i < VisibleRows && _scroll + i < packages.Count; i++)
            {
                var package = packages[_scroll + i];
                var row = Translate(RowLocal(i));
                DrawRowBackground(sb, row, _scroll + i, package.Seq == _selectedPackage);
                PlayerShopUi.DrawText(sb, PlayerShopUi.Ellipsize(package.Name, 12f, priceX - nameX - 8), new Vector2(nameX, row.Y + 3), ModernHudTheme.TextWhite, 12f, Alpha);
                PlayerShopUi.DrawText(sb, package.Price.ToString(CultureInfo.InvariantCulture), new Vector2(priceX, row.Y + 3), ModernHudTheme.TextGold, 12f, Alpha);
                PlayerShopUi.DrawText(sb, CashShopCatalog.CoinName(package.CashType), new Vector2(coinX, row.Y + 3), ModernHudTheme.TextGray, 12f, Alpha);
            }
        }

        private void DrawStorage(SpriteBatch sb, Rectangle list, Rectangle header)
        {
            int nameX = list.X + 10, extraX = list.Right - 150;
            PlayerShopUi.DrawText(sb, "Item", new Vector2(nameX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);
            PlayerShopUi.DrawText(sb, _tab == Tab.Gifts ? "From" : "Option", new Vector2(extraX, header.Y + 3), ModernHudTheme.TextGold, 11f, Alpha);

            var storage = _shop.Storage;
            if (storage.Count == 0)
            {
                DrawEmpty(sb, list, _tab == Tab.Gifts ? "No gifts." : "Your cash shop storage is empty.");
                return;
            }

            for (int i = 0; i < VisibleRows && _scroll + i < storage.Count; i++)
            {
                var entry = storage[_scroll + i];
                var row = Translate(RowLocal(i));
                DrawRowBackground(sb, row, _scroll + i, entry.StorageIndex == _selectedStorage);
                PlayerShopUi.DrawText(sb, PlayerShopUi.Ellipsize(StorageName(entry), 12f, extraX - nameX - 8), new Vector2(nameX, row.Y + 3), ModernHudTheme.TextWhite, 12f, Alpha);
                string extra = _tab == Tab.Gifts ? entry.Sender ?? string.Empty : _shop.Catalog?.DescribePriceOption(entry.PriceSeq) ?? string.Empty;
                PlayerShopUi.DrawText(sb, PlayerShopUi.Ellipsize(extra, 12f, list.Right - extraX - 10), new Vector2(extraX, row.Y + 3), ModernHudTheme.TextGray, 12f, Alpha);
            }
        }

        protected override void DrawOverlay(SpriteBatch sb, GameTime gameTime)
        {
            DrawPlaceholder(sb, _receiver, "Gift to (character)");
            DrawPlaceholder(sb, _giftText, "Gift message");
        }
    }
}
