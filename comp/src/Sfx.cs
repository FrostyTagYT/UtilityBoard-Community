using NAudio.CoreAudioApi;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
namespace BoardCompanion;

// Soundboard: short local clips from Documents\<data folder>\SFX (WAV/MP3/OGG, max 10 s), played on the Windows DEFAULT output
// (or the device picked on the board) - never the music device, which may be a virtual cable nobody hears. Each clip is
// peak-normalised to about -1 dBFS so quiet files are loud. They never go into Gorilla Tag voice chat.
public sealed class Soundboard : IDisposable
{
    public sealed class Clip { public string Name, Path; public double Duration; public float Gain = 1f; }
    public const double MaxSeconds = 10; public const int MaxClips = 24, MaxVoices = 4;
    readonly CompanionSettings cfg; readonly object L = new();
    public List<Clip> Clips = new(); public List<string> Skipped = new(); public string Error;
    readonly List<(WasapiOut o, WaveStream r)> voices = new();
    public static bool Mute;   // --test / self-test: silent
    public int Played;
    public Soundboard(CompanionSettings c) { cfg = c; }
    public string Folder => string.IsNullOrWhiteSpace(cfg.SfxFolder) ? Path.Combine(CompanionSettings.Dir, "SFX") : cfg.SfxFolder;

    static WaveStream Open(string p) => Path.GetExtension(p).ToLowerInvariant() == ".ogg" ? new VorbisWaveReader(p) : new AudioFileReader(p);
    public void Rescan()
    {
        lock (L)
        {
            Clips.Clear(); Skipped.Clear(); Error = null;
            try
            {
                Directory.CreateDirectory(Folder);
                foreach (var f in Directory.GetFiles(Folder).Where(f => new[] { ".wav", ".mp3", ".ogg" }.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    if (Clips.Count >= MaxClips) { Skipped.Add(Path.GetFileName(f) + " (over " + MaxClips + " clips)"); continue; }
                    try { using var r = Open(f); var d = r.TotalTime.TotalSeconds; if (d > MaxSeconds) Skipped.Add(Path.GetFileName(f) + $" (too long: {d:F0}s, max {MaxSeconds}s)"); else Clips.Add(new Clip { Name = Path.GetFileNameWithoutExtension(f), Path = f, Duration = d, Gain = NormGain(r) }); }
                    catch { Skipped.Add(Path.GetFileName(f) + " (not a readable audio file)"); }
                }
                if (Clips.Count == 0) Error = "No sounds in " + Folder;
            }
            catch (Exception e) { Error = "SFX folder error: " + e.Message; }
            Log.I($"SFX: {Clips.Count} clips, {Skipped.Count} skipped in {Folder}");
        }
    }
    // gain that brings the clip's peak to ~ -1 dBFS (max x6 so near-silent files are not blown up into noise)
    static float NormGain(WaveStream r)
    {
        try { var sp = r.ToSampleProvider(); var buf = new float[8192]; float peak = 0; int n; while ((n = sp.Read(buf, 0, buf.Length)) > 0) for (int i = 0; i < n; i++) { var a = Math.Abs(buf[i]); if (a > peak) peak = a; } r.Position = 0; return peak < 1e-4f ? 1f : Math.Clamp(0.89f / peak, 0.25f, 6f); }
        catch { return 1f; }
    }
    public static List<string> RenderDevices() { try { using var en = new MMDeviceEnumerator(); return en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).Select(d => d.FriendlyName).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(); } catch { return new(); } }
    public string DeviceName { get { if (string.IsNullOrEmpty(cfg.SfxDevice)) return "Windows default"; return cfg.SfxDevice; } }
    public string NextDevice()
    {
        var l = new List<string> { "" }; l.AddRange(RenderDevices()); int i = l.IndexOf(cfg.SfxDevice ?? ""); cfg.SfxDevice = l[(i + 1) % l.Count]; cfg.Save();
        Log.I("SFX output: " + DeviceName); return null;
    }
    MMDevice Device()
    {
        using var en = new MMDeviceEnumerator(); var want = cfg.SfxDevice;
        if (!string.IsNullOrEmpty(want)) foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) if (d.FriendlyName == want) return d;
        return en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }
    public string Play(int i)
    {
        lock (L)
        {
            if (i < 0 || i >= Clips.Count) return "Unknown sound";
            var c = Clips[i]; if (!File.Exists(c.Path)) return "Sound file missing: " + c.Name;
            Reap(); while (voices.Count >= MaxVoices) { Kill(voices[0]); voices.RemoveAt(0); }
            try
            {
                var r = Open(c.Path); var vol = new VolumeSampleProvider(r.ToSampleProvider()) { Volume = Mute ? 0f : cfg.SfxVolume / 100f * c.Gain };
                var o = new WasapiOut(Device(), AudioClientShareMode.Shared, true, 80); o.Init(vol); o.Play(); voices.Add((o, r)); Played++;
                Log.I("SFX: " + c.Name + " gain x" + c.Gain.ToString("F2") + (Mute ? " (muted test)" : "")); return null;
            }
            catch (Exception e) { return "Could not play " + c.Name + ": " + e.Message; }
        }
    }
    public int Active { get { lock (L) { Reap(); return voices.Count; } } }
    void Reap() { for (int k = voices.Count - 1; k >= 0; k--) if (voices[k].o.PlaybackState == PlaybackState.Stopped) { Kill(voices[k]); voices.RemoveAt(k); } }
    static void Kill((WasapiOut o, WaveStream r) v) { try { v.o.Stop(); } catch { } try { v.o.Dispose(); } catch { } try { v.r.Dispose(); } catch { } }
    public void StopAll() { lock (L) { foreach (var v in voices) Kill(v); voices.Clear(); } }
    public void SetVolume(int v) { cfg.SfxVolume = Math.Clamp(v, 0, 100); cfg.Save(); }
    public void Dispose() => StopAll();
}
