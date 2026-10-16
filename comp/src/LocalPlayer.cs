using System.Security.Cryptography;
using System.Text;
using NAudio.CoreAudioApi;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
namespace BoardCompanion;

// Local music player for user-supplied MP3 / WAV / OGG files. Plays through the chosen Windows output device
// (default device unless set) with its own volume; never touches the game's or Windows' master volume.
public sealed class LocalPlayer : IDisposable
{
    public sealed class Track { public string Id, Path, Name, Kind, Playlist; public double Duration; }
    readonly object L = new();
    readonly CompanionSettings cfg;
    public List<Track> All = new();
    public List<string> Playlists = new();            // [0] = "All Music", then sub-folders
    public List<string> Unreadable = new();
    public int PlaylistIndex;
    List<Track> queue = new(); int qi = -1;
    WasapiOut outp; WaveStream reader; VolumeSampleProvider vol;
    bool userStop; public string Error; public string DeviceName = "Windows default";
    readonly Random rng = new();
    public event Action Changed;
    public static bool Mute;   // --test: output silenced (positions still advance), reported volume unchanged

    public LocalPlayer(CompanionSettings c) { cfg = c; }

    public static string IdFor(string fullPath) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath.ToLowerInvariant())))[..16].ToLowerInvariant();

    // Real header checks, not just the file extension.
    public static string Sniff(string path)
    {
        try
        {
            var b = new byte[12]; int n;
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) n = f.Read(b, 0, b.Length);
            if (n >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'A' && b[10] == 'V' && b[11] == 'E') return "wav";
            if (n >= 4 && b[0] == 'O' && b[1] == 'g' && b[2] == 'g' && b[3] == 'S') return "ogg";
            if (n >= 3 && b[0] == 'I' && b[1] == 'D' && b[2] == '3') return "mp3";
            if (n >= 2 && b[0] == 0xFF && (b[1] & 0xE0) == 0xE0) return "mp3";
        }
        catch { }
        return null;
    }
    static WaveStream Open(Track t) => t.Kind switch
    {
        "wav" => new WaveFileReader(t.Path),
        "ogg" => new VorbisWaveReader(t.Path),
        _ => new MediaFoundationReader(t.Path)
    };

    public void Rescan()
    {
        lock (L)
        {
            var root = cfg.MusicFolder; var all = new List<Track>(); var bad = new List<string>();
            try { Directory.CreateDirectory(root); } catch (Exception e) { Error = "Music folder not available: " + e.Message; }
            IEnumerable<string> files = Array.Empty<string>();
            try
            {
                files = Directory.EnumerateFiles(root, "*.*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 4, AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint })
                    .Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)).Take(5000).ToList();
            }
            catch (Exception e) { Error = "Could not read music folder: " + e.Message; }
            foreach (var f in files)
            {
                var kind = Sniff(f); var ext = Path.GetExtension(f).TrimStart('.').ToLowerInvariant();
                if (kind == null || kind != ext) { bad.Add(Path.GetFileName(f) + " (not a real " + ext.ToUpperInvariant() + " file)"); continue; }
                var t = new Track { Path = Path.GetFullPath(f), Kind = kind, Name = Path.GetFileNameWithoutExtension(f), Id = IdFor(Path.GetFullPath(f)) };
                var rel = Path.GetRelativePath(root, Path.GetDirectoryName(t.Path)); t.Playlist = rel == "." ? "" : rel.Split(Path.DirectorySeparatorChar)[0];
                try { using var r = Open(t); t.Duration = r.TotalTime.TotalSeconds; }
                catch (Exception e) { bad.Add(Path.GetFileName(f) + " (unreadable: " + e.Message + ")"); continue; }
                all.Add(t);
            }
            All = all.OrderBy(t => t.Playlist).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList(); Unreadable = bad;
            Playlists = new List<string> { "All Music" }; Playlists.AddRange(All.Select(t => t.Playlist).Where(p => p != "").Distinct().OrderBy(p => p));
            if (PlaylistIndex >= Playlists.Count) PlaylistIndex = 0;
            var cur = Current?.Id; BuildQueue(); if (cur != null) { var j = queue.FindIndex(t => t.Id == cur); if (j >= 0) qi = j; }
            if (All.Count > 0 && Error != null && Error.StartsWith("Music folder")) Error = null;
            Log.I($"Music scan of {root}: {All.Count} playable, {bad.Count} skipped, {Playlists.Count - 1} playlists");
            foreach (var b in bad) Log.W("  skipped " + b);
        }
        Changed?.Invoke();
    }
    void BuildQueue()
    {
        var pl = PlaylistIndex == 0 ? null : Playlists[PlaylistIndex];
        queue = All.Where(t => pl == null || t.Playlist == pl).ToList();
        if (cfg.Shuffle) for (int i = queue.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (queue[i], queue[j]) = (queue[j], queue[i]); }
        qi = queue.Count > 0 ? Math.Clamp(qi, 0, queue.Count - 1) : -1;
    }
    public Track Current => qi >= 0 && qi < queue.Count ? queue[qi] : null;
    public IReadOnlyList<Track> Queue => queue;
    public int QueueIndex => qi;
    public bool Playing { get { lock (L) return outp != null && outp.PlaybackState == PlaybackState.Playing; } }
    public bool Paused { get { lock (L) return outp != null && outp.PlaybackState == PlaybackState.Paused; } }
    public double Position { get { lock (L) { try { return reader?.CurrentTime.TotalSeconds ?? 0; } catch { return 0; } } } }
    public double Duration { get { lock (L) { try { return reader?.TotalTime.TotalSeconds ?? (Current?.Duration ?? 0); } catch { return 0; } } } }

    MMDevice PickDevice(out string name)
    {
        using var en = new MMDeviceEnumerator();
        if (!string.IsNullOrEmpty(cfg.OutputDevice))
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                if (d.FriendlyName == cfg.OutputDevice) { name = d.FriendlyName; return d; }
        var def = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); name = "Windows default (" + def.FriendlyName + ")"; return def;
    }
    public static List<string> Devices()
    {
        try { using var en = new MMDeviceEnumerator(); return en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).Select(d => d.FriendlyName).OrderBy(n => n).ToList(); }
        catch { return new List<string>(); }
    }

    void Teardown()
    {
        userStop = true;
        try { outp?.Stop(); } catch { }
        try { outp?.Dispose(); } catch { }
        try { reader?.Dispose(); } catch { }
        outp = null; reader = null; vol = null; userStop = false;
    }
    bool StartAt(int index, double seekSeconds = 0)
    {
        Teardown();
        if (queue.Count == 0) { Error = All.Count == 0 ? "No music found in " + cfg.MusicFolder : "Playlist is empty"; return false; }
        for (int attempt = 0; attempt < queue.Count; attempt++)
        {
            qi = ((index + attempt) % queue.Count + queue.Count) % queue.Count; var t = queue[qi];
            if (!File.Exists(t.Path)) { Error = "Missing file (removed or drive unplugged): " + t.Name; Log.W(Error); continue; }
            try
            {
                reader = Open(t);
                if (seekSeconds > 0) reader.CurrentTime = TimeSpan.FromSeconds(Math.Min(seekSeconds, Math.Max(0, reader.TotalTime.TotalSeconds - 1)));
                vol = new VolumeSampleProvider(reader.ToSampleProvider()) { Volume = Mute ? 0f : cfg.LocalVolume / 100f };
                var dev = PickDevice(out var dn); DeviceName = dn;
                outp = new WasapiOut(dev, AudioClientShareMode.Shared, true, 120);
                outp.PlaybackStopped += OnStopped; outp.Init(vol); outp.Play();
                Error = null; Log.I($"Local: playing '{t.Name}' ({t.Kind}) on {dn}"); return true;
            }
            catch (Exception e) { Error = "Cannot play " + t.Name + ": " + e.Message; Log.W(Error); Teardown(); }
        }
        return false;
    }
    void OnStopped(object s, StoppedEventArgs e)
    {
        if (userStop || !ReferenceEquals(s, outp)) return;
        if (e.Exception != null)
        {
            Error = "Audio output lost (" + e.Exception.Message + ") - retrying on the current device"; Log.W(Error);
            var pos = Position; var idx = qi;
            Task.Run(async () => { await Task.Delay(1500); lock (L) { if (outp != null && ReferenceEquals(s, outp)) StartAt(idx, pos); } Changed?.Invoke(); });
            return;
        }
        Task.Run(() => { lock (L) { if (!ReferenceEquals(s, outp)) return; AutoNext(); } Changed?.Invoke(); });
    }
    void AutoNext()
    {
        if (cfg.Repeat == 1) { StartAt(qi); return; }
        if (qi + 1 < queue.Count) { StartAt(qi + 1); return; }
        if (cfg.Repeat == 2) { if (cfg.Shuffle) BuildQueue(); StartAt(0); return; }
        Teardown(); Log.I("Local: end of queue");
    }

    // ---- commands (all validated by the caller) ----
    public string Play(string trackId)
    {
        lock (L)
        {
            if (trackId != null)
            {
                int j = queue.FindIndex(t => t.Id == trackId);
                if (j < 0) { int a = All.FindIndex(t => t.Id == trackId); if (a < 0) return "Unknown track"; PlaylistIndex = 0; BuildQueue(); j = queue.FindIndex(t => t.Id == trackId); }
                return StartAt(j) ? null : Error;
            }
            if (outp != null && outp.PlaybackState == PlaybackState.Paused) { outp.Play(); return null; }
            if (outp != null && outp.PlaybackState == PlaybackState.Playing) return null;
            return StartAt(Math.Max(qi, 0)) ? null : Error;
        }
    }
    public string Pause() { lock (L) { if (outp != null && outp.PlaybackState == PlaybackState.Playing) outp.Pause(); return null; } }
    public string Toggle() { lock (L) { if (outp != null && outp.PlaybackState == PlaybackState.Playing) { outp.Pause(); return null; } } return Play(null); }
    public string Stop() { lock (L) { Teardown(); return null; } }
    public string Next() { lock (L) { if (queue.Count == 0) return Error = "No music found in " + cfg.MusicFolder; return StartAt(qi + 1) ? null : Error; } }
    public string Previous()
    {
        lock (L)
        {
            if (queue.Count == 0) return Error = "No music found in " + cfg.MusicFolder;
            if (reader != null && reader.CurrentTime.TotalSeconds > 3) { reader.CurrentTime = TimeSpan.Zero; return null; }
            return StartAt(qi - 1) ? null : Error;
        }
    }
    public string Seek(double seconds)
    {
        lock (L)
        {
            if (reader == null) return "Nothing is playing";
            if (!reader.CanSeek) return "Seeking not supported for this file";
            reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, Math.Max(0, reader.TotalTime.TotalSeconds - .5))); return null;
        }
    }
    public void SetVolume(int v) { lock (L) { cfg.LocalVolume = Math.Clamp(v, 0, 100); if (vol != null) vol.Volume = Mute ? 0f : cfg.LocalVolume / 100f; } cfg.Save(); }
    public void SetShuffle(bool on) { lock (L) { var cur = Current?.Id; cfg.Shuffle = on; BuildQueue(); if (cur != null) { int j = queue.FindIndex(t => t.Id == cur); if (j >= 0) { if (on) (queue[0], queue[j]) = (queue[j], queue[0]); qi = on ? 0 : j; } } } cfg.Save(); }
    public void SetRepeat(int r) { cfg.Repeat = Math.Clamp(r, 0, 2); cfg.Save(); }
    public string SelectPlaylist(int i)
    {
        lock (L) { if (i < 0 || i >= Playlists.Count) return "Unknown playlist"; bool was = outp != null && outp.PlaybackState == PlaybackState.Playing; PlaylistIndex = i; qi = 0; BuildQueue(); if (was) StartAt(0); else Teardown(); return null; }
    }
    public string SetDevice(string name)
    {
        lock (L)
        {
            if (name != "" && !Devices().Contains(name)) return "Unknown output device";
            cfg.OutputDevice = name; cfg.Save();
            if (outp != null) { bool wasPlaying = outp.PlaybackState == PlaybackState.Playing; var pos = Position; StartAt(qi, pos); if (!wasPlaying) outp?.Pause(); }
            else { PickDevice(out var dn); DeviceName = dn; }
            return null;
        }
    }
    public void Dispose() { lock (L) Teardown(); }
}
