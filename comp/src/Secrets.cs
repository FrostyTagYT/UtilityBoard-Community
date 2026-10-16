using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace BoardCompanion;

// Per-user integration settings (TikFinity / OBS), encrypted with Windows DPAPI for the CURRENT Windows user only:
// %APPDATA%\<edition data folder>\integrations.dat. Nothing here is ever sent to the game, logged, or shipped in a build.
// Another Windows account (or another PC) cannot decrypt it.
public sealed class Integrations
{
    public bool SetupDone { get; set; }
    public bool TikFinityEnabled { get; set; }
    public int TikFinityPort { get; set; } = 21213;          // TikFinity "WebSocket API" on this PC (127.0.0.1 only)
    public bool ObsEnabled { get; set; } = true;
    public bool ObsUseObsConfig { get; set; } = true;        // read port/password from OBS's own local websocket config each time
    public int ObsPort { get; set; } = 4455;
    public string ObsPassword { get; set; } = "";            // only if ObsUseObsConfig = false
}

public static class SecretStore
{
    public static string FilePath => Path.Combine(Edition.SecretDir, "integrations.dat");
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes(Edition.Id + "|ub-integrations|v1");
    public static string PathOverride;   // tests
    static string P => PathOverride ?? FilePath;
    public static Integrations Defaults()
    {
        return new Integrations();                                              // community: nothing connected until YOU set it up
    }
    public static Integrations Load()
    {
        try
        {
            if (!File.Exists(P)) return Defaults();
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(P), Entropy, DataProtectionScope.CurrentUser);
            var s = JsonSerializer.Deserialize<Integrations>(plain) ?? Defaults(); Array.Clear(plain);
            s.TikFinityPort = Math.Clamp(s.TikFinityPort, 1, 65535); s.ObsPort = Math.Clamp(s.ObsPort, 1, 65535); return s;
        }
        catch (Exception e) { Log.W("integrations.dat unreadable (other Windows user or damaged) - using defaults: " + e.GetType().Name); return Defaults(); }
    }
    public static void Save(Integrations s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(P));
        var plain = JsonSerializer.SerializeToUtf8Bytes(s); var enc = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser); Array.Clear(plain);
        var tmp = P + ".tmp"; File.WriteAllBytes(tmp, enc); File.Move(tmp, P, true);
        Log.I("integrations saved (DPAPI-encrypted, this Windows user only): TikFinity " + (s.TikFinityEnabled ? "on :" + s.TikFinityPort : "off") + ", OBS " + (s.ObsEnabled ? (s.ObsUseObsConfig ? "auto (OBS's own config)" : "manual :" + s.ObsPort + (string.IsNullOrEmpty(s.ObsPassword) ? " no password" : " password set")) : "off"));
    }
}

// Console setup wizard: <companion>.exe --setup   (also runs automatically on the Community edition's first start)
public static class Setup
{
    static string Ask(string q, string def) { Console.Write(q + (def != null ? " [" + def + "]" : "") + ": "); var a = Console.ReadLine(); return string.IsNullOrWhiteSpace(a) ? def : a.Trim(); }
    static bool Yes(string q, bool def) { var a = Ask(q + " (y/n)", def ? "y" : "n"); return a.StartsWith("y", StringComparison.OrdinalIgnoreCase); }
    static string Hidden(string q)
    {
        Console.Write(q + ": "); var sb = new StringBuilder();
        while (true) { var k = Console.ReadKey(true); if (k.Key == ConsoleKey.Enter) break; if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) { sb.Length--; Console.Write("\b \b"); } continue; } if (!char.IsControl(k.KeyChar)) { sb.Append(k.KeyChar); Console.Write('*'); } }
        Console.WriteLine(); return sb.ToString();
    }
    public static int Run()
    {
        var s = SecretStore.Load();
        Console.WriteLine("=== " + Edition.Product + " " + Edition.Version + " - integration setup ===");
        Console.WriteLine("Everything you enter stays on THIS PC, encrypted for YOUR Windows account only. Nothing is uploaded or sent to the game.");
        Console.WriteLine();
        Console.WriteLine("TikTok LIVE uses TikFinity on this PC (Settings > WebSocket API, usually port 21213). You log into YOUR TikTok in TikFinity.");
        s.TikFinityEnabled = Yes("Connect to TikFinity", s.TikFinityEnabled);
        if (s.TikFinityEnabled && int.TryParse(Ask("TikFinity WebSocket port", s.TikFinityPort.ToString()), out var tp)) s.TikFinityPort = Math.Clamp(tp, 1, 65535);
        Console.WriteLine();
        s.ObsEnabled = Yes("Control OBS (record, scenes, replay clip) through obs-websocket", s.ObsEnabled);
        if (s.ObsEnabled)
        {
            s.ObsUseObsConfig = Yes("Read the port/password automatically from OBS's own settings on this PC (recommended)", s.ObsUseObsConfig);
            if (!s.ObsUseObsConfig)
            {
                if (int.TryParse(Ask("OBS websocket port", s.ObsPort.ToString()), out var op)) s.ObsPort = Math.Clamp(op, 1, 65535);
                var pw = Hidden("OBS websocket password (Enter = keep current / none)"); if (pw.Length > 0) s.ObsPassword = pw;
            }
        }
        s.SetupDone = true; SecretStore.Save(s);
        Console.WriteLine("Saved. You can run setup again any time (Setup Integrations.bat).");
        return 0;
    }
}
