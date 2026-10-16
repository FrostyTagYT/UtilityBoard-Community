using System.Diagnostics;
using NAudio.CoreAudioApi;
using Windows.Media.Control;
namespace BoardCompanion;

// Remote control for the user's already-running Spotify DESKTOP app via Windows media sessions (SMTC).
// No Spotify SDK / Web API, no credentials, no audio capture. Only a session whose SourceAppUserModelId identifies
// Spotify is ever controlled; if none exists we return an error and touch nothing else.
public sealed class SpotifyAdapter
{
    public const string Unavailable = "Spotify unavailable \u2014 open Spotify on your PC";
    GlobalSystemMediaTransportControlsSessionManager mgr;
    public string IdFilter = "spotify";   // matches "Spotify.exe" (desktop) and "SpotifyAB.SpotifyMusic_...!Spotify" (Store)

    public async Task InitAsync() { mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); }

    public GlobalSystemMediaTransportControlsSession Find()
    {
        if (mgr == null) return null;
        foreach (var s in mgr.GetSessions())
        {
            var id = s.SourceAppUserModelId ?? "";
            if (id.Contains(IdFilter, StringComparison.OrdinalIgnoreCase)) return s;
        }
        return null;
    }
    public IEnumerable<string> AllSessionIds() => mgr == null ? Array.Empty<string>() : mgr.GetSessions().Select(s => s.SourceAppUserModelId ?? "?");

    public sealed class State { public bool available; public bool playing; public string title; public string artist; public string appId; public string error; public int volume = -1; public string volumeKind = "unsupported"; public bool canNext, canPrev, canToggle; }

    public async Task<State> GetStateAsync()
    {
        var st = new State();
        var s = Find();
        if (s == null) { st.error = Unavailable; return st; }
        st.available = true; st.appId = s.SourceAppUserModelId;
        try
        {
            var pb = s.GetPlaybackInfo();
            st.playing = pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            st.canNext = pb.Controls.IsNextEnabled; st.canPrev = pb.Controls.IsPreviousEnabled; st.canToggle = pb.Controls.IsPlayPauseToggleEnabled || pb.Controls.IsPlayEnabled || pb.Controls.IsPauseEnabled;
            var mp = await s.TryGetMediaPropertiesAsync();
            st.title = string.IsNullOrEmpty(mp?.Title) ? null : mp.Title; st.artist = string.IsNullOrEmpty(mp?.Artist) ? null : mp.Artist;
        }
        catch (Exception e) { st.error = "Spotify session error: " + e.Message; }
        var v = GetAppVolume(); if (v >= 0) { st.volume = v; st.volumeKind = "spotify-app"; }
        return st;
    }

    public async Task<(bool ok, string err)> CommandAsync(string cmd)
    {
        var s = Find();
        if (s == null) return (false, Unavailable);
        bool r;
        try
        {
            r = cmd switch
            {
                "play" => await s.TryPlayAsync(),
                "pause" => await s.TryPauseAsync(),
                "toggle" => await s.TryTogglePlayPauseAsync(),
                "next" => await s.TrySkipNextAsync(),
                "previous" => await s.TrySkipPreviousAsync(),
                _ => false
            };
        }
        catch (Exception e) { return (false, "Spotify command failed: " + e.Message); }
        return r ? (true, null) : (false, "Spotify refused '" + cmd + "'");
    }

    // Spotify's OWN per-app mixer volume (Windows audio session of Spotify.exe), not the Windows master volume.
    // Returns -1 if Spotify has no audio session right now (e.g. never played since launch).
    static List<int> SpotifyPids() { try { return Process.GetProcessesByName("Spotify").Select(p => p.Id).ToList(); } catch { return new List<int>(); } }
    public int GetAppVolume()
    {
        var pids = SpotifyPids(); if (pids.Count == 0) return -1;
        try
        {
            using var en = new MMDeviceEnumerator();
            foreach (var dev in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                var sm = dev.AudioSessionManager; sm.RefreshSessions(); var ss = sm.Sessions;
                for (int i = 0; i < ss.Count; i++) { var se = ss[i]; if (pids.Contains((int)se.GetProcessID)) return (int)Math.Round(se.SimpleAudioVolume.Volume * 100); }
            }
        }
        catch (Exception e) { Log.W("Spotify volume read failed: " + e.Message); }
        return -1;
    }
    public bool SetAppVolume(int v)
    {
        v = Math.Clamp(v, 0, 100); var pids = SpotifyPids(); if (pids.Count == 0) return false; bool any = false;
        try
        {
            using var en = new MMDeviceEnumerator();
            foreach (var dev in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                var sm = dev.AudioSessionManager; sm.RefreshSessions(); var ss = sm.Sessions;
                for (int i = 0; i < ss.Count; i++) { var se = ss[i]; if (pids.Contains((int)se.GetProcessID)) { se.SimpleAudioVolume.Volume = v / 100f; any = true; } }
            }
        }
        catch (Exception e) { Log.W("Spotify volume set failed: " + e.Message); return false; }
        return any;
    }
}
