#nullable enable
using System.Collections.Generic;
using Client.Main.Core.Client;

namespace Client.Main.Controls.UI.Game
{
    /// <summary>
    /// Lucky Coin dialog of Delgado (NpcWindowResponse DelgadoLuckyCoinRegistration). Opening asks for the registered
    /// count (0xBF/0x0B); Register sends 0xBF/0x0C, the exchange buttons 0xBF/0x0D with the coin tiers of OpenMU.
    /// </summary>
    public sealed class DelgadoLuckyCoinWindow : NpcDialogWindowBase
    {
        public static readonly uint[] ExchangeTiers = [10, 20, 30];

        private static DelgadoLuckyCoinWindow? _instance;

        private DelgadoLuckyCoinWindow() : base(110, 2)
        {
            AddButton(0, 0, 2, () => "REGISTER", () => true,
                () => _ = MuGame.Network?.GetCharacterService()?.SendLuckyCoinRegistrationRequestAsync());
            AddCloseButton(0, 1, 2);
            for (int i = 0; i < ExchangeTiers.Length; i++)
            {
                uint coins = ExchangeTiers[i];
                AddButton(1, i, ExchangeTiers.Length, () => $"EXCHANGE {coins}", () => true,
                    () => _ = MuGame.Network?.GetCharacterService()?.SendLuckyCoinExchangeRequestAsync(coins));
            }
        }

        public static DelgadoLuckyCoinWindow Instance => _instance ??= new DelgadoLuckyCoinWindow();

        protected override string Title => "LUCKY COIN";
        protected override string? Subtitle => "Delgado";

        public override IReadOnlyList<string> BodyLines =>
        [
            $"Registered Lucky Coins: {EventItemState.LuckyCoinsRegistered}",
            "Register adds one Lucky Coin of your inventory.",
            "Exchange trades 10, 20 or 30 Lucky Coins of your inventory for a reward.",
        ];

        public void Open()
        {
            StatusText = null;
            Show();
            _ = MuGame.Network?.GetCharacterService()?.SendLuckyCoinCountRequestAsync();
        }
    }
}
