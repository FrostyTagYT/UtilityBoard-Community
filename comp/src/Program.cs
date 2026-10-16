using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
namespace BoardCompanion;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length >= 2 && args[0] == "--send") return await Send(args[1], args.Length > 2 ? args[2] : PipeServer.DefaultName);
        if (args.Length >= 1 && args[0] == "--selftest") return await SelfTest2.Run(args.Skip(1).ToArray());
        if (args.Length >= 1 && args[0] == "--setup") return Setup.Run();

        string pipe = PipeServer.DefaultName; for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--pipe" && args[i + 1].StartsWith(Edition.Pipe + "_") && args.Contains("--test")) pipe = args[i + 1];
        using var mutex = new Mutex(true, pipe == PipeServer.DefaultName ? Edition.MutexName : Edition.MutexName + "_" + pipe, out bool first);
        if (!first) { Log.W(Edition.Product + " is already running."); return 1; }
        Console.Title = Edition.Product + " " + Edition.Version + " - close this window to stop";
        bool test = args.Contains("--test");
        var cfg = CompanionSettings.Load();
        if (test)
        {
            CompanionSettings.NoSave = true; LocalPlayer.Mute = true;
            cfg.GameDir = @"Z:\__no_game_folder_in_test_mode__"; cfg.SfxFolder = ""; Soundboard.Mute = true; cfg.StarterSfxDone = true; cfg.MicDeviceId = ""; cfg.PendingMods = new(); cfg.LegacyModDirs = new();   // test mode never touches the live game folder unless --gamedir is given
            for (int i = 0; i < args.Length - 1; i++) { if (args[i] == "--music") cfg.MusicFolder = args[i + 1]; if (args[i] == "--sfx") cfg.SfxFolder = args[i + 1]; if (args[i] == "--gamedir") cfg.GameDir = args[i + 1]; if (args[i] == "--legacy" && args[i + 1].Contains('=')) { var kv = args[i + 1].Split('=', 2); if (!kv[1].Contains("disabled_live")) cfg.LegacyModDirs[kv[0]] = kv[1]; } }
            Log.I("TEST MODE: settings not saved, local output muted, Spotify commands dry-run only");
        }
        var integ = SecretStore.Load();
        if (!test && !integ.SetupDone && !Console.IsInputRedirected)
        {   // first start: let THIS user connect their own TikFinity / OBS (or skip)
            try { Setup.Run(); integ = SecretStore.Load(); } catch (Exception e) { Log.W("setup skipped: " + e.Message); }
        }
        if (!test && !cfg.StarterSfxDone) { try { var sf = string.IsNullOrWhiteSpace(cfg.SfxFolder) ? Path.Combine(CompanionSettings.Dir, "SFX") : cfg.SfxFolder; int w = StarterSfx.WriteMissing(sf); Log.I("SFX: starter pack - " + w + " new sounds in " + sf); } catch (Exception e) { Log.W("starter SFX: " + e.Message); } cfg.StarterSfxDone = true; }
        cfg.Save();
        Log.I(Edition.Product + " " + Edition.Version + " (" + Edition.Id + " edition) starting. Music folder: " + cfg.MusicFolder);
        var sp = new SpotifyAdapter(); await sp.InitAsync();
        var local = new LocalPlayer(cfg); local.Rescan();
        var svc = new Service(cfg, sp, local) { DrySpotify = test }; svc.Mic.Dry = test; svc.Obs.Integ = integ;
        svc.Tik.Enabled = integ.TikFinityEnabled; svc.Tik.Port = integ.TikFinityPort;
        MockObs mock = null; MockTikFinity mockTik = null;
        if (test)
        {
            if (args.Contains("--tikfinity-mock")) { mockTik = new MockTikFinity().Start(); svc.Tik.Enabled = true; svc.Tik.Port = mockTik.Port; Log.I("TEST MODE: TikFinity = mock websocket (no TikTok contact)"); }
            else if (args.Contains("--no-tikfinity")) svc.Tik.Enabled = false;
            if (args.Contains("--obs-mock")) { mock = new MockObs { Recording = false, ReplayActive = true }.Start(); svc.Obs.PortOverride = mock.Port; svc.Obs.PasswordOverride = mock.Password; Log.I("TEST MODE: OBS = mock websocket (real OBS never contacted)"); }
            else { svc.Obs.ReadOnly = true; Log.I("TEST MODE: real OBS is READ-ONLY (record/scene/clip refused)"); }
        }
        svc.Sfx.Rescan(); Log.I("Mods: game folder " + svc.Mods.GameDir);
        using var cts = new CancellationTokenSource();
        _ = Task.Run(() => svc.Tik.RunAsync(cts.Token));
        Log.I("TikFinity: " + (svc.Tik.Enabled ? "on, local port " + svc.Tik.Port : "not set up (run Setup Integrations to connect YOUR TikFinity)"));
        _ = Task.Run(async () =>
        {
            int tick = 0;
            while (true)
            {
                try
                {
                    if ((DateTime.UtcNow - svc.LastRequest).TotalSeconds < 30)
                    {
                        await svc.Obs.RefreshAsync();
                    }
                    if (++tick % 2 == 0) { int n = svc.Mods.Apply(); if (n > 0) Log.I("Mods: applied " + n + " queued change(s) after the game closed"); }
                }
                catch (Exception e) { Log.W("background: " + e.Message); }
                await Task.Delay(1500);
            }
        });
        var ms = svc.Mic.State(); Log.I("Mic: " + (ms.available ? "'" + ms.name + "' " + (ms.muted ? "MUTED" : "live") + " (Windows-wide mute control)" : ms.error));
        var st = await sp.GetStateAsync();
        Log.I("Spotify: " + (st.available ? "found (" + st.appId + ")" : st.error));
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        Log.I("Ready. Leave this window open while you play; close it (or Ctrl+C) to stop.");
        await new PipeServer(svc) { Name = pipe }.RunAsync(cts.Token);
        mock?.Dispose(); mockTik?.Dispose(); svc.Sfx.Dispose();
        local.Dispose(); Log.I("Stopped.");
        return 0;
    }

    // Tiny local client for testing: <companion>.exe --send "{\"command\":\"getState\"}"
    public static async Task<int> Send(string json, string pipe)
    {
        try
        {
            using var c = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            await c.ConnectAsync(3000);
            var b = Encoding.UTF8.GetBytes(json + "\n"); await c.WriteAsync(b); await c.FlushAsync();
            using var r = new StreamReader(c, Encoding.UTF8); var line = await r.ReadLineAsync();
            Console.WriteLine(line); return 0;
        }
        catch (Exception e) { Console.WriteLine("ERROR " + e.Message); return 2; }
    }
}

public static class PipeTestClient
{
    public static async Task<List<string>> Exchange(string pipe, IEnumerable<string> lines, int timeoutMs = 5000)
    {
        var res = new List<string>();
        using var c = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        await c.ConnectAsync(timeoutMs);
        using var r = new StreamReader(c, Encoding.UTF8, false, 4096, true);
        foreach (var l in lines)
        {
            var b = Encoding.UTF8.GetBytes(l + "\n"); await c.WriteAsync(b); await c.FlushAsync();
            var t = r.ReadLineAsync(); if (await Task.WhenAny(t, Task.Delay(timeoutMs)) != t) { res.Add("TIMEOUT"); break; }
            res.Add(t.Result);
        }
        return res;
    }
}
