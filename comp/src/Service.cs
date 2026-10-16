using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace BoardCompanion;

// Validates and executes one JSON-line request; returns one JSON-line response. Never accepts paths from clients.
public sealed class Service
{
    public readonly CompanionSettings Cfg; public readonly SpotifyAdapter Spotify; public readonly LocalPlayer Local; public MicControl Mic; public ObsClient Obs = new ObsClient(); public Soundboard Sfx; public ModToggles Mods; public TikFinityClient Tik = new TikFinityClient();
    public DateTime LastRequest = DateTime.MinValue;
    public const int MaxMessage = 2048;
    static readonly Regex IdRx = new("^[A-Za-z0-9_-]{1,32}$"), TrackRx = new("^[0-9a-f]{16}$");
    static readonly HashSet<string> SpotifyCmds = new() { "play", "pause", "toggle", "next", "previous", "setVolume" };
    static readonly HashSet<string> LocalCmds = new() { "play", "pause", "toggle", "stop", "next", "previous", "setVolume", "seek", "seekBy", "setShuffle", "setRepeat", "selectPlaylist", "rescan", "listTracks" };
    static readonly HashSet<string> GlobalCmds = new() { "getState", "listDevices", "setDevice", "setPausePrevious", "openMusicFolder", "ping" };
    public string ActiveSource = "";
    public bool DrySpotify;   // --test: log Spotify commands instead of executing them

    public Service(CompanionSettings c, SpotifyAdapter s, LocalPlayer l) { Cfg = c; Spotify = s; Local = l; Mic = new MicControl(c); Sfx = new Soundboard(c); Mods = new ModToggles(c);
    }

    static string Err(string id, string e) => new JsonObject { ["id"] = id, ["ok"] = false, ["error"] = e }.ToJsonString();

    public async Task<string> HandleAsync(string line)
    {
        if (line == null || line.Length > MaxMessage) return Err(null, "message too large");
        JsonObject o;
        try { o = JsonNode.Parse(line) as JsonObject; } catch { return Err(null, "invalid JSON"); }
        if (o == null) return Err(null, "expected a JSON object");
        string id = Str(o, "id");
        if (id != null && !IdRx.IsMatch(id)) return Err(null, "invalid id");
        string source = Str(o, "source"), cmd = Str(o, "command");
        if (cmd == null) return Err(id, "missing command");
        int? value = null;
        if (o["value"] != null)
        {
            if (o["value"] is JsonValue jv && jv.TryGetValue<int>(out var iv)) value = iv;
            else if (o["value"] is JsonValue jb && jb.TryGetValue<bool>(out var bv)) value = bv ? 1 : 0;
            else return Err(id, "value must be an integer");
        }
        string trackId = Str(o, "trackId");
        if (trackId != null && !TrackRx.IsMatch(trackId)) return Err(id, "invalid trackId");
        string device = Str(o, "device");
        if (device != null && (device.Length > 200 || device.IndexOfAny(new[] { '\\', '/', ':', '\0' }) >= 0)) return Err(id, "invalid device");

        string error = null; JsonNode extra = null; LastRequest = DateTime.UtcNow;
        try
        {
            if (source == null)
            {
                if (!GlobalCmds.Contains(cmd)) return Err(id, "unknown command");
                switch (cmd)
                {
                    case "ping": break;
                    case "getState": break;
                    case "listDevices": extra = new JsonArray(LocalPlayer.Devices().Select(d => (JsonNode)d).ToArray()); break;
                    case "setDevice": if (device == null) return Err(id, "device required"); error = Local.SetDevice(device); break;
                    case "setPausePrevious": if (value is not (0 or 1)) return Err(id, "value must be 0 or 1"); Cfg.PausePreviousSourceOnSwitch = value == 1; Cfg.Save(); break;
                    case "openMusicFolder": System.Diagnostics.Process.Start("explorer.exe", "\"" + Cfg.MusicFolder + "\""); break;
                }
            }
            else if (source == "spotify")
            {
                if (!SpotifyCmds.Contains(cmd)) return Err(id, "unknown spotify command");
                if (cmd == "setVolume")
                {
                    if (value is null or < 0 or > 100) return Err(id, "volume must be 0-100");
                    if (DrySpotify) { Log.I("DRY-RUN spotify setVolume " + value); return await OkResp(id); }
                    if (Spotify.Find() == null) error = SpotifyAdapter.Unavailable;
                    else if (!Spotify.SetAppVolume(value.Value)) error = "Spotify volume unavailable (Spotify has no audio session yet - press play once)";
                }
                else
                {
                    bool starting = cmd == "play" || (cmd == "toggle" && !(await Spotify.GetStateAsync()).playing);
                    if (starting && !DrySpotify && Spotify.Find() != null && Cfg.PausePreviousSourceOnSwitch && Local.Playing) { Local.Pause(); Log.I("switch: paused local music because Spotify is starting"); }
                    if (DrySpotify) { Log.I("DRY-RUN spotify " + cmd + " (test mode: Spotify not touched)"); return (await OkResp(id)); }
                    var (ok, e) = await Spotify.CommandAsync(cmd); if (!ok) error = e; else if (starting) ActiveSource = "spotify";
                }
            }
            else if (source == "local")
            {
                if (!LocalCmds.Contains(cmd)) return Err(id, "unknown local command");
                bool starting = cmd == "play" || (cmd == "toggle" && !Local.Playing) || ((cmd == "next" || cmd == "previous") && !Local.Playing);
                if (starting && Cfg.PausePreviousSourceOnSwitch && !DrySpotify)
                {
                    var sp = await Spotify.GetStateAsync();
                    if (sp.available && sp.playing) { await Spotify.CommandAsync("pause"); Log.I("switch: paused Spotify because local music is starting"); }
                }
                switch (cmd)
                {
                    case "play": error = Local.Play(trackId); break;
                    case "pause": error = Local.Pause(); break;
                    case "toggle": error = Local.Toggle(); break;
                    case "stop": error = Local.Stop(); break;
                    case "next": error = Local.Next(); break;
                    case "previous": error = Local.Previous(); break;
                    case "setVolume": if (value is null or < 0 or > 100) return Err(id, "volume must be 0-100"); Local.SetVolume(value.Value); break;
                    case "seek": if (value is null or < 0 or > 86400) return Err(id, "seek must be 0-86400 s"); error = Local.Seek(value.Value); break;
                    case "seekBy": if (value is null or < -600 or > 600) return Err(id, "seekBy must be -600..600 s"); error = Local.Seek(Local.Position + value.Value); break;
                    case "setShuffle": if (value is not (0 or 1)) return Err(id, "value must be 0 or 1"); Local.SetShuffle(value == 1); break;
                    case "setRepeat": if (value is null or < 0 or > 2) return Err(id, "repeat must be 0,1,2"); Local.SetRepeat(value.Value); break;
                    case "selectPlaylist": if (value is null) return Err(id, "playlist index required"); error = Local.SelectPlaylist(value.Value); break;
                    case "rescan": Local.Rescan(); break;
                    case "listTracks":
                        int off = Math.Clamp(value ?? 0, 0, 100000); var q = Local.Queue;
                        extra = new JsonObject { ["offset"] = off, ["total"] = q.Count, ["items"] = new JsonArray(q.Skip(off).Take(8).Select(t => (JsonNode)new JsonObject { ["id"] = t.Id, ["name"] = Trim(t.Name, 60), ["duration"] = Math.Round(t.Duration) }).ToArray()) };
                        break;
                }
                if (starting && error == null) ActiveSource = "local";
            }
            else if (source == "obs")
            {
                switch (cmd)
                {
                    case "refresh": await Obs.RefreshAsync(); error = Obs.Connected ? null : Obs.Error; break;
                    case "toggleRecord": error = await Obs.ToggleRecordAsync(); break;
                    case "setScene": if (value is null or < 0 or > 200) return Err(id, "scene index required"); error = await Obs.SetSceneAsync(value.Value); break;
                    case "clip": error = await Obs.ClipAsync(); break;
                    default: return Err(id, "unknown obs command");
                }
            }
            else if (source == "sfx")
            {
                switch (cmd)
                {
                    case "play": if (value is null or < 0 or >= Soundboard.MaxClips) return Err(id, "sound index required"); error = Sfx.Play(value.Value); break;
                    case "stopAll": Sfx.StopAll(); break;
                    case "nextDevice": error = Sfx.NextDevice(); break;
                    case "setVolume": if (value is null or < 0 or > 100) return Err(id, "volume must be 0-100"); Sfx.SetVolume(value.Value); break;
                    case "rescan": Sfx.Rescan(); break;
                    case "openFolder": System.Diagnostics.Process.Start("explorer.exe", "\"" + Sfx.Folder + "\""); break;
                    default: return Err(id, "unknown sfx command");
                }
            }
            else if (source == "mods")
            {
                switch (cmd)
                {
                    case "toggle": if (value is null or < 0 or > 2) return Err(id, "mod index 0-2 required"); error = Mods.Toggle(value.Value); break;
                    case "migrate": if (value is null or < 0 or > 2) return Err(id, "mod index 0-2 required"); error = Mods.Migrate(value.Value); break;
                    default: return Err(id, "unknown mods command");
                }
            }
            else if (source == "live")
            {
                switch (cmd) { case "reconnect": Tik.Reconnect(); break; default: return Err(id, "unknown live command"); }
            }
            else if (source == "mic")
            {
                switch (cmd)
                {
                    case "toggle": error = Mic.Toggle(); break;
                    case "next": error = Mic.Next(); break;
                    case "setMute": if (value is not (0 or 1)) return Err(id, "value must be 0 or 1"); error = Mic.SetMute(value == 1); break;
                    default: return Err(id, "unknown mic command");
                }
            }
            else return Err(id, "unknown source");
        }
        catch (Exception e) { Log.W("command failed: " + e); error = "internal error: " + e.Message; }

        var resp = new JsonObject { ["id"] = id, ["ok"] = error == null };
        if (error != null) resp["error"] = error;
        if (extra != null) resp["data"] = extra;
        resp["state"] = await StateAsync();
        return resp.ToJsonString();
    }

    JsonObject ObsJson()
    {
        var sc = Obs.Scenes; int ci = Obs.CurrentScene == null ? -1 : sc.IndexOf(Obs.CurrentScene);
        return new JsonObject { ["available"] = Obs.Connected, ["error"] = Obs.Error, ["recording"] = Obs.Recording, ["paused"] = Obs.RecordPaused, ["recordSeconds"] = Obs.RecordMs / 1000,
            ["replayAvailable"] = Obs.ReplayAvailable, ["replay"] = Obs.ReplayActive, ["scene"] = Trim(Obs.CurrentScene, 40), ["sceneIndex"] = ci, ["sceneCount"] = sc.Count,
            ["scenes"] = new JsonArray(sc.Take(40).Select(x => (JsonNode)Trim(x, 40)).ToArray()), ["last"] = Obs.LastAction, ["readOnly"] = Obs.ReadOnly, ["status"] = Trim(Obs.StatusText(), 60) };
    }
    JsonObject SfxJson() => new JsonObject { ["available"] = Sfx.Clips.Count > 0, ["count"] = Sfx.Clips.Count, ["names"] = new JsonArray(Sfx.Clips.Select(c => (JsonNode)Trim(c.Name, 24)).ToArray()),
        ["volume"] = Cfg.SfxVolume, ["active"] = Sfx.Active, ["played"] = Sfx.Played, ["skipped"] = Sfx.Skipped.Count, ["error"] = Sfx.Error, ["muted"] = Soundboard.Mute, ["device"] = Trim(Sfx.DeviceName, 40) };
    JsonObject ModsJson()
    {
        var a = new JsonArray(); foreach (var m in Mods.States()) a.Add(new JsonObject { ["name"] = m.Name, ["installed"] = m.Installed, ["enabled"] = m.Enabled, ["pending"] = m.Pending, ["next"] = m.Pending ? m.PendingEnable : m.Enabled, ["legacy"] = m.LegacyOff });
        return new JsonObject { ["list"] = a, ["gameRunning"] = Mods.GameRunning() };
    }
    JsonObject MicJson() { var m = Mic.State(); return new JsonObject { ["available"] = m.available, ["muted"] = m.muted, ["name"] = Trim(m.name, 40), ["error"] = m.error, ["scope"] = "windows", ["dry"] = Mic.Dry, ["isDefault"] = m.isDefault, ["index"] = m.index, ["count"] = m.count }; }
    async Task<string> OkResp(string id) { var r = new JsonObject { ["id"] = id, ["ok"] = true }; r["state"] = await StateAsync(); return r.ToJsonString(); }
    static string Str(JsonObject o, string k) { var n = o[k]; if (n == null) return null; return n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null; }
    static string Trim(string s, int n) => s == null ? null : s.Length <= n ? s : s[..(n - 1)] + "\u2026";

    public async Task<JsonObject> StateAsync()
    {
        var sp = await Spotify.GetStateAsync(); var cur = Local.Current;
        return new JsonObject
        {
            ["spotify"] = new JsonObject { ["available"] = sp.available, ["playing"] = sp.playing, ["title"] = Trim(sp.title, 80), ["artist"] = Trim(sp.artist, 80), ["error"] = sp.error, ["volume"] = sp.volume, ["volumeKind"] = sp.volumeKind, ["canNext"] = sp.canNext, ["canPrev"] = sp.canPrev },
            ["local"] = new JsonObject
            {
                ["available"] = Local.All.Count > 0, ["playing"] = Local.Playing, ["paused"] = Local.Paused, ["title"] = Trim(cur?.Name, 80), ["trackId"] = cur?.Id, ["kind"] = cur?.Kind,
                ["position"] = Math.Round(Local.Position, 1), ["duration"] = Math.Round(Local.Duration, 1), ["volume"] = Cfg.LocalVolume, ["shuffle"] = Cfg.Shuffle, ["repeat"] = Cfg.Repeat,
                ["playlist"] = Local.Playlists.Count > 0 ? Trim(Local.Playlists[Local.PlaylistIndex], 40) : "All Music", ["playlistIndex"] = Local.PlaylistIndex, ["playlistCount"] = Local.Playlists.Count,
                ["queueIndex"] = Local.QueueIndex, ["queueCount"] = Local.Queue.Count, ["trackCount"] = Local.All.Count, ["skipped"] = Local.Unreadable.Count,
                ["error"] = Local.Error, ["device"] = Trim(Local.DeviceName, 60), ["folder"] = Trim(Cfg.MusicFolder, 120)
            },
            ["mic"] = MicJson(), ["obs"] = ObsJson(), ["sfx"] = SfxJson(), ["mods"] = ModsJson(), ["live"] = Tik.Json(), ["sys"] = new JsonObject { ["cpu"] = SysStats.Cpu(), ["ram"] = SysStats.Ram() }, ["edition"] = Edition.Id,
            ["pausePrevious"] = Cfg.PausePreviousSourceOnSwitch, ["active"] = ActiveSource, ["companion"] = Edition.Version
        };
    }
}
