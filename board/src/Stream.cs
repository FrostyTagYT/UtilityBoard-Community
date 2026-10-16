using System;
using System.Collections.Generic;
using UnityEngine;
using static UtilityBoard.Board;

namespace UtilityBoard
{
    // OBS stream tools, soundboard, next-launch mod toggles, counters. All of it runs in the local companion;
    // the mod only sends small commands over the local pipe (no network, no passwords or tokens ever reach the game).
    public partial class Plugin
    {
        public int SceneOffset, SfxOffset;
        private List<object> L(string a, string b) => music.Get(a, b) as List<object>;
        private bool NeedCompanion(List<List<Btn>> rows, Color warn, string what, string key = "available")
        {
            if (music.Connected && music.Get(what, key) != null) return false;
            rows.Add(new List<Btn> { Lbl(music.Connected ? "Update the companion to " + Version : "Companion not running", warn) });
            rows.Add(new List<Btn> { Lbl("Start " + Edition.CompanionExe + " on your PC") });
            return true;
        }
        private string Foot(string def) => Time.unscaledTime < toastUntil ? Toast : (DateTime.UtcNow - music.LastErrAt).TotalSeconds < 5 && music.LastReplyError != "" ? music.LastReplyError : def;
        private void StreamPage(List<List<Btn>> rows, Color warn)
        {
            if (NeedCompanion(rows, warn, "obs")) return;
            if (!music.B("obs", "available")) { rows.Add(new List<Btn> { Lbl(music.S("obs", "status") ?? "OBS not connected", warn) }); rows.Add(new List<Btn> { Lbl(music.S("obs", "error") ?? "") }); rows.Add(new List<Btn> { B("obs:refresh", "Try again") }); return; }
            bool rec = music.B("obs", "recording"), rb = music.B("obs", "replay");
            rows.Add(new List<Btn> { B("obs:rec", rec ? "Stop " + HMS(music.N("obs", "recordSeconds")) : "Start Recording", true, rec),
                                     B("obs:clip", rb ? "Save Replay" : rec ? "Chapter mark" : "Clip", rb || rec) });
            var sc = L("obs", "scenes"); int n = sc?.Count ?? 0; int cur = (int)music.N("obs", "sceneIndex");
            if (SceneOffset >= n) SceneOffset = 0;
            for (int i = 0; i < 4; i++)
            {
                int k = SceneOffset + i;
                if (k < n) rows.Add(new List<Btn> { B("obs:scene:" + k, sc[k] as string, true, k == cur) }); else rows.Add(new List<Btn> { Lbl(i == 0 ? "(no scenes)" : "") });
            }
            rows.Add(new List<Btn> { B("obs:sc-", "< Scenes", SceneOffset > 0), Lbl((n == 0 ? 0 : SceneOffset / 4 + 1) + "/" + Mathf.Max(1, (n + 3) / 4)), B("obs:sc+", "Scenes >", SceneOffset + 4 < n) });
            rows.Add(new List<Btn> { Lbl(Foot(music.S("obs", "last") ?? ("Replay buffer " + (rb ? "running" : music.B("obs", "replayAvailable") ? "off" : "not set up")))) });
        }
        private void SfxPage(List<List<Btn>> rows, Color warn)
        {
            if (NeedCompanion(rows, warn, "sfx")) return;
            var names = L("sfx", "names"); int n = names?.Count ?? 0;
            if (n == 0) { rows.Add(new List<Btn> { Lbl("No sounds yet", warn) }); rows.Add(new List<Btn> { Lbl("Put short WAV/MP3/OGG (max 10 s) in") }); rows.Add(new List<Btn> { Lbl("Documents\\" + Edition.DataFolder + "\\SFX") }); rows.Add(new List<Btn> { B("sfx:folder", "Open folder"), B("sfx:rescan", "Rescan") }); return; }
            if (SfxOffset >= n) SfxOffset = 0;
            for (int r = 0; r < 4; r++)
            {
                var row = new List<Btn>();
                for (int c = 0; c < 2; c++) { int k = SfxOffset + r * 2 + c; row.Add(k < n ? B("sfx:" + k, names[k] as string) : Lbl("")); }
                rows.Add(row);
            }
            rows.Add(new List<Btn> { B("sfx:pg-", "<", SfxOffset > 0), B("sfx:stop", "Stop all", true, music.N("sfx", "active") > 0), B("sfx:pg+", ">", SfxOffset + 8 < n) });
            rows.Add(new List<Btn> { B("sfx:vol-", "-"), Lbl("Vol " + (int)music.N("sfx", "volume")), B("sfx:vol+", "+") });
            rows.Add(new List<Btn> { B("sfx:dev", "Out: " + (music.S("sfx", "device") ?? "Windows default")) });
        }
        private void ModsPage(List<List<Btn>> rows, Color warn)
        {
            if (NeedCompanion(rows, warn, "mods", "list")) return;
            var list = music.Get("mods", "list") as List<object>; bool run = music.B("mods", "gameRunning"); int legacyIdx = -1;
            rows.Add(new List<Btn> { Lbl("Applies on NEXT game launch", warn) });
            for (int i = 0; i < 4; i++)
            {
                var d = list != null && i < list.Count ? list[i] as Dictionary<string, object> : null;
                if (d == null) { rows.Add(new List<Btn> { Lbl("") }); continue; }
                string name = d["name"] as string; bool inst = d["installed"] is bool b1 && b1, en = d["enabled"] is bool b2 && b2, next = d["next"] is bool b3 && b3, pend = d["pending"] is bool b4 && b4;
                bool leg = d.TryGetValue("legacy", out var lg) && lg is bool lb && lb; if (leg && legacyIdx < 0) legacyIdx = i;
                string label = !inst ? name + ": not installed" : leg && !pend ? name + ": OFF (old folder)" : pend ? name + " \u2192 " + (next ? "ON" : "OFF") + " next launch" : name + ": " + (en ? "ON" : "OFF");
                rows.Add(new List<Btn> { B("mods:" + i, label, inst, inst && next) });
            }
            rows.Add(new List<Btn> { Lbl(run ? "GT running: queued until you quit" : "GT closed: changes applied") });
            if (legacyIdx >= 0) rows.Add(new List<Btn> { B("modmig:" + legacyIdx, "Migrate old folder (stays OFF)", !run) });
            else rows.Add(new List<Btn> { Lbl(Foot("Tap a mod again to cancel")) });
        }
        private Dictionary<string, object> CD(string k) { var c = music.Get("counters", k); return c as Dictionary<string, object>; }
        private static string Big(object v) { if (!(v is double d)) return "--"; return d >= 1e6 ? (d / 1e6).ToString("0.##") + "M" : d >= 1e4 ? (d / 1e3).ToString("0.#") + "K" : ((long)d).ToString("N0"); }
        private bool StreamPress(string id)
        {
            if (id == "cnt:refresh") { music.Send("obs", "refreshCounters"); Say("Refreshing counters"); return true; }
            if (id.StartsWith("modmig:")) { int k; if (int.TryParse(id.Substring(7), out k)) { music.Send("mods", "migrate", k); Say("Moving old folder (game must be closed)"); } return true; }
            if (id == "obs:rec") { music.Send("obs", "toggleRecord"); Say(music.B("obs", "recording") ? "Stopping recording..." : "Starting recording..."); return true; }
            if (id == "obs:clip") { music.Send("obs", "clip"); Say("Clip requested"); return true; }
            if (id == "obs:refresh") { music.Send("obs", "refresh"); return true; }
            if (id == "obs:sc-") { SceneOffset = Mathf.Max(0, SceneOffset - 4); return true; }
            if (id == "obs:sc+") { SceneOffset += 4; return true; }
            if (id.StartsWith("obs:scene:")) { int k; if (int.TryParse(id.Substring(10), out k)) { music.Send("obs", "setScene", k); var sc = L("obs", "scenes"); Say("Scene: " + (sc != null && k < sc.Count ? sc[k] as string : k.ToString())); } return true; }
            if (id == "sfx:stop") { music.Send("sfx", "stopAll"); return true; }
            if (id == "sfx:pg-") { SfxOffset = Mathf.Max(0, SfxOffset - 8); return true; }
            if (id == "sfx:pg+") { SfxOffset += 8; return true; }
            if (id == "sfx:vol-") { music.Send("sfx", "setVolume", Mathf.Clamp((int)music.N("sfx", "volume") - 10, 0, 100)); return true; }
            if (id == "sfx:vol+") { music.Send("sfx", "setVolume", Mathf.Clamp((int)music.N("sfx", "volume") + 10, 0, 100)); return true; }
            if (id == "sfx:folder") { music.Send("sfx", "openFolder"); Say("Opened the SFX folder on your PC"); return true; }
            if (id == "sfx:dev") { music.Send("sfx", "nextDevice"); Say("Soundboard output changed"); return true; }
            if (id == "sfx:rescan") { music.Send("sfx", "rescan"); return true; }
            if (id.StartsWith("sfx:")) { int k; if (int.TryParse(id.Substring(4), out k)) music.Send("sfx", "play", k); return true; }
            if (id.StartsWith("mods:")) { int k; if (int.TryParse(id.Substring(5), out k)) music.Send("mods", "toggle", k); return true; }
            return false;
        }
    }
}
