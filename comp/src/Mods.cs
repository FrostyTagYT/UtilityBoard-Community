namespace BoardCompanion;

// Quick mod toggles for the NEXT game launch. Moves a plugin folder between BepInEx\plugins\<mod> and
// BepInEx\<Edition.DisabledDir>\<mod> (outside plugins, so BepInEx does not load it). While ANY Gorilla Tag process is
// running nothing is moved (the DLLs are loaded/locked): the change is queued and applied once the game has closed.
// Only the three listed mods can be toggled; the board itself and its dependencies never are.
public sealed class ModToggles
{
    // community: every plugin folder of YOUR game (enabled or disabled), except the board itself; first 8 by name
    public string[] Allowed
    {
        get
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in new[] { Path.Combine(GameDir, "BepInEx", "plugins"), Path.Combine(GameDir, "BepInEx", Edition.DisabledDir) })
                try { if (Directory.Exists(root)) foreach (var d in Directory.GetDirectories(root)) set.Add(Path.GetFileName(d)); } catch { }
            set.Remove(Edition.BoardFolder); return set.Take(8).ToArray();
        }
    }
    readonly CompanionSettings cfg; readonly object L = new();
    public Func<bool> GameRunning = () => System.Diagnostics.Process.GetProcessesByName("Gorilla Tag").Length > 0;
    public string GameDir => string.IsNullOrWhiteSpace(cfg.GameDir) ? @"C:\Program Files (x86)\Steam\steamapps\common\Gorilla Tag" : cfg.GameDir;
    string On(string m) => Path.Combine(GameDir, "BepInEx", "plugins", m);
    string Off(string m) => Path.Combine(GameDir, "BepInEx", Edition.DisabledDir, m);
    // older hand-made "disabled" locations (LegacyModDirs) count as OFF
    string Legacy(string m) => cfg.LegacyModDirs != null && cfg.LegacyModDirs.TryGetValue(m, out var p) && !string.IsNullOrWhiteSpace(p) && Directory.Exists(p) ? p : null;
    string OffSrc(string m) => Directory.Exists(Off(m)) ? Off(m) : Legacy(m);
    public ModToggles(CompanionSettings c) { cfg = c; cfg.PendingMods ??= new(); }

    public sealed class ModState { public string Name, Where; public bool Installed, Enabled, Pending, PendingEnable, LegacyOff; }
    public List<ModState> States()
    {
        lock (L)
        {
            var l = new List<ModState>();
            foreach (var m in Allowed)
            {
                bool on = Directory.Exists(On(m)), off = Directory.Exists(Off(m)), leg = Legacy(m) != null;
                bool pend = cfg.PendingMods.TryGetValue(m, out var want);
                l.Add(new ModState { Name = m, Installed = on || off || leg, Enabled = on, Pending = pend, PendingEnable = want, LegacyOff = !on && !off && leg,
                    Where = on ? "plugins" : off ? Edition.DisabledDir : leg ? Legacy(m) : "" });
            }
            return l;
        }
    }
    // tap = flip what the next launch will load
    public string Toggle(int index)
    {
        lock (L)
        {
            var al = Allowed; if (index < 0 || index >= al.Length) return "Unknown mod";
            var m = al[index]; bool on = Directory.Exists(On(m)), off = OffSrc(m) != null;
            if (!on && !off) return m + " is not installed";
            if (on && off) return m + " exists both enabled and in a disabled folder - fix by hand";
            bool cur = cfg.PendingMods.TryGetValue(m, out var w) ? w : on; bool want = !cur;
            if (want == on) cfg.PendingMods.Remove(m); else cfg.PendingMods[m] = want;
            cfg.Save(); Log.I($"Mods: {m} -> {(want ? "ON" : "OFF")} next launch" + (want == on ? " (cancelled pending change)" : ""));
            Apply(); return null;
        }
    }
    // optional: move an old hand-made disabled folder into the disabled folder (stays OFF). Only while the game is closed.
    public string Migrate(int index)
    {
        lock (L)
        {
            var al = Allowed; if (index < 0 || index >= al.Length) return "Unknown mod";
            var m = al[index]; var leg = Legacy(m);
            if (leg == null) return m + " has no old disabled folder";
            if (Directory.Exists(Off(m)) || Directory.Exists(On(m))) return m + " already exists in the game folder - not moving";
            if (GameRunning()) return "Quit Gorilla Tag first, then move it";
            if (cfg.PendingMods.ContainsKey(m)) return m + " has a queued change - wait or cancel it first";
            try { Directory.CreateDirectory(Path.GetDirectoryName(Off(m))); Directory.Move(leg, Off(m)); Log.I($"Mods: migrated {leg} -> {Off(m)} (still OFF)"); return null; }
            catch (Exception e) { return "Move failed: " + e.Message; }
        }
    }
    // move folders for queued changes, only when no Gorilla Tag process is running. Returns number applied.
    public int Apply()
    {
        lock (L)
        {
            if (cfg.PendingMods.Count == 0 || GameRunning()) return 0;
            int n = 0;
            foreach (var kv in cfg.PendingMods.ToList())
            {
                var m = kv.Key; if (!Allowed.Contains(m)) { cfg.PendingMods.Remove(m); continue; }
                var from = kv.Value ? OffSrc(m) : On(m); var to = kv.Value ? On(m) : Off(m);
                try
                {
                    if (from == null || !Directory.Exists(from)) { if (Directory.Exists(to)) cfg.PendingMods.Remove(m); continue; }
                    if (Directory.Exists(to)) { Log.W($"Mods: {m}: target already exists, not moving"); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(to)); Directory.Move(from, to); cfg.PendingMods.Remove(m); n++;
                    Log.I($"Mods: moved {from} -> {to}");
                }
                catch (Exception e) { Log.W($"Mods: {m} move failed (will retry): {e.Message}"); }
            }
            cfg.Save(); return n;
        }
    }
}
