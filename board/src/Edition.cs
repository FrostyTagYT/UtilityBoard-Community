namespace UtilityBoard
{
    // Edition constants for the Community Edition build.
    public static class Edition
    {
        public const string Guid = "community.utilityboard.gt", Name = "Utility Board Community Edition", Id = "Community";
        public const string Title = "UTILITY BOARD", Footer = "COMMUNITY EDITION \u00B7 CLIENT-SIDE", DataFolder = "UtilityBoardCommunity";
        public const string Pipe = "UtilityBoardCompanionV1", CompanionExe = "UtilityBoardCompanion.exe";
        public const bool Private = false;
        public const string Version = "2.0.1";   // 2.0.1: keyboard toggle (Input.ToggleKey, default F3) + coexistence with another utility board
    }
}
