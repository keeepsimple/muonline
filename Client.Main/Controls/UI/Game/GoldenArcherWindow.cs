#nullable enable
using System.Collections.Generic;
using Client.Main.Core.Client;

namespace Client.Main.Controls.UI.Game
{
    /// <summary>
    /// Golden Archer registration dialog. OpenMU opens it with EventChipRegistrationResult (0x94) when the player talks
    /// to the NPC (no NpcWindowResponse) and answers every registration with the same packet; Register sends 0x95.
    /// </summary>
    public sealed class GoldenArcherWindow : NpcDialogWindowBase
    {
        private static GoldenArcherWindow? _instance;

        private GoldenArcherWindow() : base(90, 1)
        {
            AddButton(0, 0, 2, () => "REGISTER", () => true,
                () => _ = MuGame.Network?.GetCharacterService()?.SendEventChipRegistrationRequestAsync());
            AddCloseButton(0, 1, 2);
        }

        public static GoldenArcherWindow Instance => _instance ??= new GoldenArcherWindow();

        protected override string Title => "GOLDEN ARCHER";

        public override IReadOnlyList<string> BodyLines =>
        [
            $"Registered: {EventItemState.EventChipsRegistered}",
            $"In your inventory: {EventItemState.EventChipsInInventory}",
            "Register hands one item in to the Golden Archer.",
        ];

        /// <summary>0x94: result 1 = no item to register; 0 opens the dialog or confirms a registration.</summary>
        public void ShowResult(byte result)
        {
            StatusText = result == 1 ? "You have no item to register." : null;
            Show();
        }
    }
}
