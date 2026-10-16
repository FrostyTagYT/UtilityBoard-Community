using System.Text;
using System.Text.Json.Nodes;
namespace BoardCompanion;

// v2.0 self-test (both editions): DPAPI secrets, TikFinity parsing (mock), mic selection (dry), SFX device + loudness,
// starter pack, pipe state contents, community mod listing. Never touches real OBS, TikTok, the real mic mute or the game.
public static class SelfTest2
{
    static int pass, fail; static void R(string n, bool ok, string d) { if (ok) pass++; else fail++; Console.WriteLine("SELFTEST2 " + (ok ? "PASS" : "FAIL") + " " + n + " - " + d); }
    public static async Task<int> Run(string[] a)
    {
        CompanionSettings.NoSave = true; Soundboard.Mute = true; LocalPlayer.Mute = true;
        var tmp = Path.Combine(Path.GetTempPath(), "ub_selftest_" + Environment.ProcessId); Directory.CreateDirectory(tmp);
        Console.WriteLine("SELFTEST2 edition " + Edition.Id + " " + Edition.Version + ", pipe " + Edition.Pipe + ", data " + Edition.DocDir + ", secrets " + Edition.SecretDir);
        R("edition paths are edition-specific", Edition.DocDir.EndsWith(Edition.DataFolder) && Edition.SecretDir.EndsWith(Edition.DataFolder) && Edition.Pipe.Length > 5, Edition.DataFolder);

        // ---- DPAPI secrets ----
        SecretStore.PathOverride = Path.Combine(tmp, "integrations.dat");
        var d0 = SecretStore.Load();
        R("defaults: " + (Edition.Id == "Community" ? "nothing connected until the user sets it up" : "own TikFinity on"), Edition.Id == "Community" ? (!d0.TikFinityEnabled && !d0.SetupDone && d0.ObsPassword == "") : d0.TikFinityEnabled, $"tikfinity {d0.TikFinityEnabled}, setupDone {d0.SetupDone}, obs password set {d0.ObsPassword != ""}");
        var secret = "pw-SELFTEST-" + Guid.NewGuid().ToString("N")[..8];
        SecretStore.Save(new Integrations { SetupDone = true, TikFinityEnabled = true, TikFinityPort = 21213, ObsUseObsConfig = false, ObsPort = 4455, ObsPassword = secret });
        var raw = File.ReadAllBytes(SecretStore.PathOverride);
        bool plainAbsent = !Encoding.UTF8.GetString(raw).Contains(secret) && !Encoding.Unicode.GetString(raw).Contains(secret) && !Encoding.UTF8.GetString(raw).Contains("ObsPassword");
        R("secrets file is encrypted (no plaintext password/field names)", plainAbsent, raw.Length + " bytes");
        var back = SecretStore.Load(); R("secrets round-trip for this Windows user", back.ObsPassword == secret && !back.ObsUseObsConfig && back.TikFinityEnabled, "password restored " + (back.ObsPassword == secret));
        raw[raw.Length / 2] ^= 0x5A; File.WriteAllBytes(SecretStore.PathOverride, raw);
        var bad = SecretStore.Load(); R("tampered secrets file rejected (falls back to defaults)", bad.ObsPassword == "", "password after tamper empty: " + (bad.ObsPassword == ""));
        SecretStore.PathOverride = null;

        // ---- TikFinity (mock) ----
        using (var mock = new MockTikFinity().Start())
        {
            var tik = new TikFinityClient { Enabled = true, Port = mock.Port }; using var cts = new CancellationTokenSource(); var run = tik.RunAsync(cts.Token);
            var t0 = DateTime.UtcNow; while (tik.Events < MockTikFinity.Script.Length && (DateTime.UtcNow - t0).TotalSeconds < 10) await Task.Delay(100);
            var j = tik.Json();
            R("tikfinity: connects to local WebSocket API", j["connected"]?.GetValue<bool>() == true, tik.Status);
            R("tikfinity: live total likes (not stuck)", tik.Likes == 106450 && tik.SessionLikes == 20, $"likes {tik.Likes}, session {tik.SessionLikes}");
            R("tikfinity: viewers", tik.Viewers == 137, tik.Viewers.ToString());
            R("tikfinity: gift streak counted once (x5)", tik.Gifts == 5 && tik.LastGift == "Viewer C sent Rose x5", $"gifts {tik.Gifts}, last '{tik.LastGift}'");
            R("tikfinity: follow + alerts", tik.Follows == 1 && tik.AlertSeq == 2, $"follows {tik.Follows}, alerts {tik.AlertSeq} last '{tik.LastAlert}'");
            var chat = tik.Chat; R("tikfinity: chat lines", chat.Count == 2 && chat[^1] == "Viewer F: second message", string.Join(" | ", chat));
            R("tikfinity: array/payload envelope parsed", tik.Events == MockTikFinity.Script.Length, tik.Events + " events");
            cts.Cancel();
        }
        {
            var t2 = new TikFinityClient { Enabled = true, Port = 21213, HostOverride = "192.0.2.10" }; using var c2 = new CancellationTokenSource(); var r2 = t2.RunAsync(c2.Token); await Task.Delay(800); c2.Cancel();
            R("tikfinity: non-local address refused", !t2.Connected && t2.Status.Contains("loopback"), t2.Status);
        }

        // ---- mic selection (dry) ----
        var cfg = new CompanionSettings { MicName = "", MicDeviceId = "" };
        var mic = new MicControl(cfg) { Dry = true }; var st = mic.State(); var devs = mic.Devices();
        R("mic: default communications mic used when none picked", devs.Count == 0 || (st.available && st.isDefault), $"'{st.name}' default {st.isDefault}, {devs.Count} mics: " + string.Join(", ", devs.Select(x => x.name)));
        if (devs.Count > 1) { mic.Next(); var st2 = mic.State(); R("mic: 'next' selects another mic (remembered by id)", st2.name != st.name && cfg.MicDeviceId != "", $"'{st.name}' -> '{st2.name}'"); }
        else R("mic: 'next' selects another mic", true, "only " + devs.Count + " mic(s) on this PC - skipped");
        var m0 = mic.State().muted; mic.Toggle(); var m1 = mic.State().muted; mic.Toggle();
        R("mic: mute toggle acts on the selected mic (dry run)", devs.Count == 0 || (m1 != m0 && mic.State().muted == m0), $"{m0} -> {m1} -> {mic.State().muted}");
        cfg.MicName = "NoSuchMicXYZ"; cfg.MicDeviceId = ""; var mic3 = new MicControl(cfg) { Dry = true }; var st3 = mic3.State();
        R("mic: missing named mic falls back to a real one (no single-mic lock-in)", devs.Count == 0 || st3.available, $"'{st3.name}'");

        // ---- SFX ----
        var sfxDir = Path.Combine(tmp, "sfx"); int w1 = StarterSfx.WriteMissing(sfxDir); int w2 = StarterSfx.WriteMissing(sfxDir);
        R("sfx: starter pack written once, never overwritten", w1 >= 10 && w2 == 0, $"{w1} written, second run {w2}");
        // a deliberately quiet clip
        var q = Path.Combine(sfxDir, "Quiet Test.wav"); WriteTone(q, .12);
        var scfg = new CompanionSettings { SfxFolder = sfxDir, OutputDevice = "CABLE Input (VB-Audio Virtual Cable)", SfxDevice = "" };
        var sb = new Soundboard(scfg); sb.Rescan(); var qc = sb.Clips.FirstOrDefault(c => c.Name == "Quiet Test");
        R("sfx: quiet clip normalised louder", qc != null && qc.Gain > 5f, "gain x" + qc?.Gain.ToString("F2"));
        R("sfx: plays on Windows default output, NOT the music device (VB-Cable)", sb.DeviceName == "Windows default", "sfx device '" + sb.DeviceName + "', music device '" + scfg.OutputDevice + "'");
        R("sfx: default volume 100", new CompanionSettings().SfxVolume == 100, new CompanionSettings().SfxVolume.ToString());
        var err = sb.Play(0); R("sfx: pad plays (muted test)", err == null && sb.Played == 1, err ?? "ok"); sb.StopAll();

        // ---- pipe state ----
        var lcfg = new CompanionSettings { MusicFolder = Path.Combine(tmp, "music") }; Directory.CreateDirectory(lcfg.MusicFolder);
        var sp = new SpotifyAdapter(); var lp = new LocalPlayer(lcfg); lp.Rescan();
        var svc = new Service(lcfg, sp, lp) { DrySpotify = true }; svc.Mic.Dry = true; svc.Obs.Integ = new Integrations { ObsEnabled = false };
        svc.Tik.Enabled = false;
        var resp = await svc.HandleAsync("{\"command\":\"getState\"}"); var o = JsonNode.Parse(resp);
        R("state: edition + live + sys + mic list info", o?["state"]?["edition"]?.GetValue<string>() == Edition.Id && o["state"]["live"] != null && o["state"]["sys"] != null && o["state"]["mic"]?["count"] != null, "edition " + o?["state"]?["edition"]);
        R("state: community edition has no private counters", Edition.Id != "Community" || o?["state"]?["counters"] == null, "counters " + (o?["state"]?["counters"] == null ? "absent" : "present"));
        R("state never contains a password/secret", !resp.Contains("assword") && !resp.Contains(secret), resp.Length + " chars");
        var lv = await svc.HandleAsync("{\"source\":\"live\",\"command\":\"reconnect\"}"); R("live reconnect command", JsonNode.Parse(lv)?["ok"]?.GetValue<bool>() == true, lv.Length + " chars");
        var nd = await svc.HandleAsync("{\"source\":\"sfx\",\"command\":\"nextDevice\"}"); R("sfx nextDevice command", JsonNode.Parse(nd)?["ok"]?.GetValue<bool>() == true, JsonNode.Parse(nd)?["state"]?["sfx"]?["device"]?.ToString());
        var obsOff = JsonNode.Parse(resp)?["state"]?["obs"]?["status"]?.ToString(); R("obs: honest status text when off", obsOff != null && obsOff.Length > 0, "'" + obsOff + "'");

        // ---- mods listing (community = any plugin folder except the board) ----
        var gd = Path.Combine(tmp, "game"); foreach (var n in new[] { "ModA", "ModB", Edition.BoardFolder }) Directory.CreateDirectory(Path.Combine(gd, "BepInEx", "plugins", n));
        var mt = new ModToggles(new CompanionSettings { GameDir = gd }) { GameRunning = () => false }; var names = mt.Allowed;
        R("mods: lists the user's own plugin folders, never the board itself", names.SequenceEqual(new[] { "ModA", "ModB" }), string.Join(",", names));
        var tg = mt.Toggle(0); R("mods: toggle off moves to disabled folder (game closed)", tg == null && Directory.Exists(Path.Combine(gd, "BepInEx", Edition.DisabledDir, "ModA")), tg ?? "moved");
        mt.Toggle(0); R("mods: toggle back on", Directory.Exists(Path.Combine(gd, "BepInEx", "plugins", "ModA")), "restored");
        var cpu = SysStats.Cpu(); await Task.Delay(1000); cpu = SysStats.Cpu(); var ram = SysStats.Ram();
        R("sys: CPU and RAM readings", cpu >= 0 && cpu <= 100 && ram > 0 && ram <= 100, $"cpu {cpu}% ram {ram}%");
        try { Directory.Delete(tmp, true); } catch { }
        Console.WriteLine($"SELFTEST2 DONE pass={pass} fail={fail}");
        return fail == 0 ? 0 : 1;
    }
    static void WriteTone(string path, double amp)
    {
        int rate = 44100, n = rate / 2; using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8.ToArray()); w.Write(36 + n * 2); w.Write("WAVEfmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16); w.Write("data"u8.ToArray()); w.Write(n * 2);
        for (int i = 0; i < n; i++) w.Write((short)(Math.Sin(2 * Math.PI * 600 * i / rate) * amp * 32767));
    }
}
