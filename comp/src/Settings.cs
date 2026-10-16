using System.Text.Json;
namespace BoardCompanion;

// companion.json in Documents\<edition data folder> (no secrets: those live DPAPI-encrypted in SecretStore). Owned by the companion only.
public sealed class CompanionSettings
{
    public int CfgVersion { get; set; }
    public string MusicFolder { get; set; }
    public string OutputDevice { get; set; } = "";          // "" = Windows default output device
    public bool PausePreviousSourceOnSwitch { get; set; } = true;
    public int LocalVolume { get; set; } = 65;
    public bool Shuffle { get; set; }
    public string SfxFolder { get; set; } = "";              // "" = Documents\<data folder>\SFX
    public string SfxDevice { get; set; } = "";              // "" = Windows DEFAULT output (never follows the music device, e.g. a virtual cable)
    public int SfxVolume { get; set; } = 100;
    public bool StarterSfxDone { get; set; }                 // starter sound pack written once (never overwrites your files)
    public string GameDir { get; set; } = "";                // "" = Steam default Gorilla Tag folder (mod toggles)
    public Dictionary<string, bool> PendingMods { get; set; } = new();   // queued next-launch mod toggles
    public string MicDeviceId { get; set; } = "";            // "" = Windows default communications microphone
    public string MicName { get; set; } = "";                // legacy v1 name match (used only if MicDeviceId is empty)
    public int Repeat { get; set; } = 2;                     // 0 off, 1 repeat one, 2 repeat all
    public Dictionary<string, string> LegacyModDirs { get; set; } = new();

    public static string Dir => Edition.DocDir;
    public static string FilePath => Path.Combine(Dir, "companion.json");
    public static string DefaultMusic => Path.Combine(Dir, "Music");

    public static CompanionSettings Load()
    {
        CompanionSettings s = null;
        try { if (File.Exists(FilePath)) s = JsonSerializer.Deserialize<CompanionSettings>(File.ReadAllText(FilePath)); }
        catch (Exception e) { Log.W("companion.json unreadable, using defaults: " + e.Message); }
        s ??= new CompanionSettings();
        if (string.IsNullOrWhiteSpace(s.MusicFolder)) s.MusicFolder = DefaultMusic;
        s.LocalVolume = Math.Clamp(s.LocalVolume, 0, 100); s.SfxVolume = Math.Clamp(s.SfxVolume, 0, 100); s.PendingMods ??= new(); s.LegacyModDirs ??= new(); s.Repeat = Math.Clamp(s.Repeat, 0, 2);
        if (s.CfgVersion < 2) { s.CfgVersion = 2; s.SfxDevice = ""; s.SfxVolume = 100; }   // v2: soundboard on the Windows default output at full volume
        return s;
    }
    public static bool NoSave;
    public void Save()
    {
        if (NoSave) return;
        try { Directory.CreateDirectory(Dir); var tmp = FilePath + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); File.Move(tmp, FilePath, true); }
        catch (Exception e) { Log.W("could not save companion.json: " + e.Message); }
    }
}

public static class Log
{
    static readonly object L = new();
    public static string FilePath => Path.Combine(CompanionSettings.Dir, "companion.log");
    public static void I(string s) => Write("INFO", s);
    public static void W(string s) => Write("WARN", s);
    static void Write(string lvl, string s)
    {
        var line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + lvl + " " + s;
        lock (L) { Console.WriteLine(line); try { Directory.CreateDirectory(CompanionSettings.Dir); File.AppendAllText(FilePath, line + Environment.NewLine); } catch { } }
    }
}
