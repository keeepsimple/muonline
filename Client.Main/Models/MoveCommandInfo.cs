namespace Client.Main.Models
{
    public struct MoveCommandInfo
    {
        public int Index;
        public string ServerMapName;
        public string DisplayName;
        public int RequiredLevel;
        public int RequiredZen;
        /// <summary>Minimum master level from WarpRequirementsExtended (0xF5 0B), 0 = none.</summary>
        public int RequiredMasterLevel;
        public bool CanMove;
        public bool IsSelected;
        public bool IsStrifeMap;
        public bool IsEventMap;
    }
}