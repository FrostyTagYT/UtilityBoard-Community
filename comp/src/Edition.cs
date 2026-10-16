namespace BoardCompanion;

// Edition constants for the Community Edition build.
public static class Edition
{
    public const string Id = "Community", Product = "Utility Board Companion", Pipe = "UtilityBoardCompanionV1", MutexName = @"Local\UtilityBoardCompanionV1";
    public const string DataFolder = "UtilityBoardCommunity", DisabledDir = "plugins_disabled_ub", BoardFolder = "UtilityBoardCommunity";
    public const string Version = "2.0.0";
    // music / SFX / settings (no secrets)
    public static string DocDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), DataFolder);
    // encrypted per-user integrations (DPAPI, current Windows user only)
    public static string SecretDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), DataFolder);
}
