#nullable enable
using System.Collections.Generic;

namespace Client.Main.Controls.UI.Game
{
    /// <summary>
    /// Doppelganger entry dialog of Lugard (NpcWindowResponse LugardDoppelgangerEntry; the byte after the window is the
    /// minutes until the entrance opens). Enter sends DoppelgangerEnterRequest (0xBF/0x0E); a failed DoppelgangerEnterResult
    /// locks the button, EntranceAvailable (4) unlocks it.
    /// </summary>
    public sealed class DoppelgangerEnterControl : NpcDialogWindowBase
    {
        private static DoppelgangerEnterControl? _instance;
        private byte _minutes;

        private DoppelgangerEnterControl() : base(110, 1)
        {
            AddButton(0, 0, 2, () => "ENTER", () => !EnterLocked,
                () => _ = MuGame.Network?.GetCharacterService()?.SendDoppelgangerEnterRequestAsync());
            AddCloseButton(0, 1, 2);
        }

        public static DoppelgangerEnterControl Instance => _instance ??= new DoppelgangerEnterControl();

        protected override string Title => "DOPPELGANGER";
        protected override string? Subtitle => "Lugard";

        public bool EnterLocked { get; private set; }

        public override IReadOnlyList<string> BodyLines =>
        [
            _minutes == 0 ? "You may now enter." : $"The entrance opens in {_minutes} minute(s).",
            "Ticket: Mirror of Dimensions or Doppelganger Free Ticket.",
            "Stop the monsters before they reach the magic circle.",
        ];

        public void ShowEntry(byte minutesUntilOpen)
        {
            _minutes = minutesUntilOpen;
            EnterLocked = minutesUntilOpen > 0;
            Show();
        }

        /// <summary>BF/0E: 0 entered (the server warps the player), 4 the entrance is available, others lock the button.</summary>
        public void ApplyEnterResult(byte result)
        {
            if (result == 0) Hide();
            else if (result == 4) { _minutes = 0; EnterLocked = false; }
            else EnterLocked = true;
        }
    }
}
