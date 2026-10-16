using NAudio.CoreAudioApi;
namespace BoardCompanion;

// Windows-wide mute of ONE capture endpoint (IAudioEndpointVolume.Mute) = the mute box in Windows Sound settings, for every app.
// Which mic: the one you picked on the board (MicDeviceId), else the legacy name match, else the Windows DEFAULT communications mic
// (so a SteamVR / headset / any USB mic works without configuration). Dry = test mode: real mute never changed.
public sealed class MicControl
{
    public CompanionSettings Cfg; public bool Dry; bool dryMuted;
    public string Match { get => Cfg?.MicName ?? ""; set { if (Cfg != null) Cfg.MicName = value; found = DateTime.MinValue; } }
    MMDevice dev; bool isDefault; DateTime found = DateTime.MinValue; string lastErr; List<(string id, string name)> list = new();
    public MicControl(CompanionSettings c = null) { Cfg = c; }
    public List<(string id, string name)> Devices() { Find(); lock (list) return list.ToList(); }
    MMDevice Find()
    {
        if ((DateTime.UtcNow - found).TotalSeconds < 3 && dev != null) return dev;
        try
        {
            using var en = new MMDeviceEnumerator();
            var all = en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();
            lock (list) { list = all.Select(d => (d.ID, d.FriendlyName)).OrderBy(x => x.FriendlyName, StringComparer.OrdinalIgnoreCase).ToList(); }
            string defId = null; try { defId = en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications).ID; } catch { }
            MMDevice hit = null; var want = Cfg?.MicDeviceId ?? ""; var match = Cfg?.MicName ?? "";
            if (want != "") hit = all.FirstOrDefault(d => d.ID == want);
            if (hit == null && match != "") hit = all.FirstOrDefault(d => d.FriendlyName.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0);
            if (hit == null && defId != null) hit = all.FirstOrDefault(d => d.ID == defId);
            if (hit == null) hit = all.FirstOrDefault();
            dev = hit; isDefault = hit != null && hit.ID == defId; found = DateTime.UtcNow;
            lastErr = hit == null ? "no microphone found (plugged in?)" : null;
        }
        catch (Exception e) { dev = null; lastErr = "audio devices unavailable: " + e.Message; }
        return dev;
    }
    public (bool available, bool muted, string name, string error, bool isDefault, int index, int count) State()
    {
        var d = Find(); int cnt; int idx; lock (list) { cnt = list.Count; idx = d == null ? -1 : list.FindIndex(x => x.id == d.ID); }
        if (d == null) return (false, false, null, lastErr, false, -1, cnt);
        try { return (true, Dry ? dryMuted : d.AudioEndpointVolume.Mute, d.FriendlyName, null, isDefault, idx, cnt); }
        catch (Exception e) { dev = null; found = DateTime.MinValue; return (false, false, null, "mic read failed: " + e.Message, false, -1, cnt); }
    }
    public string SetMute(bool m)
    {
        var d = Find(); if (d == null) return lastErr;
        if (Dry) { dryMuted = m; Log.I("DRY-RUN mic " + (m ? "mute" : "unmute") + " '" + d.FriendlyName + "' (test mode: real mic not touched)"); return null; }
        try { d.AudioEndpointVolume.Mute = m; Log.I("mic " + (m ? "MUTED" : "unmuted") + " (Windows-wide) '" + d.FriendlyName + "'"); return null; }
        catch (Exception e) { dev = null; found = DateTime.MinValue; return "mic mute failed: " + e.Message; }
    }
    public string Toggle() { var s = State(); return s.available ? SetMute(!s.muted) : s.error; }
    // pick the next capture device; remembered in companion.json
    public string Next()
    {
        var d = Find(); List<(string id, string name)> l; lock (list) l = list.ToList(); if (l.Count == 0) return lastErr ?? "no microphone found";
        int i = d == null ? -1 : l.FindIndex(x => x.id == d.ID); var n = l[(i + 1) % l.Count];
        if (Cfg != null) { Cfg.MicDeviceId = n.id; Cfg.MicName = ""; Cfg.Save(); }
        dev = null; found = DateTime.MinValue; dryMuted = false; Log.I("mic selected: '" + n.name + "'"); return null;
    }
}
