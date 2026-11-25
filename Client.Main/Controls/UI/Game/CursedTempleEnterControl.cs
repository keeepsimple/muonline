#nullable enable
using System.Collections.Generic;

namespace Client.Main.Controls.UI.Game
{
    /// <summary>
    /// Illusion Temple entry dialog of Mirage (NpcWindowResponse IllusionTemple). One button per temple sends
    /// IllusionTempleEnterRequest (0xBF/0x00) with slot 0xFF, so the server picks the Scroll of Blood of that level.
    /// The players per temple come from IllusionTempleUserCount (0xBF/0x03) when the server sends it.
    /// </summary>
    public sealed class CursedTempleEnterControl : NpcDialogWindowBase
    {
        public const int TempleCount = 6;

        private static CursedTempleEnterControl? _instance;
        private int[]? _userCounts;

        private CursedTempleEnterControl() : base(70, 4)
        {
            for (int i = 0; i < TempleCount; i++)
            {
                byte temple = (byte)(i + 1);
                AddButton(i / 2, i % 2, 2, () => TempleButtonText(temple, _userCounts), () => true,
                    () => _ = MuGame.Network?.GetCharacterService()?.SendIllusionTempleEnterRequestAsync(temple));
            }

            AddCloseButton(3, 1, 2);
        }

        public static CursedTempleEnterControl Instance => _instance ??= new CursedTempleEnterControl();

        protected override string Title => "ILLUSION TEMPLE";
        protected override string? Subtitle => "Mirage";

        public override IReadOnlyList<string> BodyLines =>
        [
            "Choose the temple of your Scroll of Blood.",
            "Two teams fight for the holy relic.",
        ];

        public static string TempleButtonText(int temple, int[]? userCounts)
            => userCounts == null ? $"TEMPLE {temple}" : $"TEMPLE {temple} ({userCounts[temple - 1]})";

        public string ButtonText(int temple) => TempleButtonText(temple, _userCounts);

        public void SetUserCounts(byte[] counts)
        {
            _userCounts = new int[TempleCount];
            for (int i = 0; i < TempleCount && i < counts.Length; i++) _userCounts[i] = counts[i];
        }
    }
}
