using System;
using System.Collections.Generic;
using UnityEngine;
using static UtilityBoard.Board;

namespace UtilityBoard
{
    public partial class Plugin
    {
        private static readonly Dictionary<string, int> PageNo = new Dictionary<string, int> { { "home", 1 }, { "time", 2 }, { "weather", 3 }, { "music", 4 }, { "queue", 5 }, { "settings", 6 }, { "tools", 7 }, { "theme", 8 }, { "stream", 9 }, { "sfx", 10 }, { "mods", 11 }, { "counters", 12 }, { "perf", 13 }, { "live", 14 } };
        private static string Clock(double s) { if (s < 0) s = 0; int t = (int)s; return (t / 60) + ":" + (t % 60).ToString("00"); }
        private static string Bar(int pct) { int n = Mathf.RoundToInt(pct / 10f); return new string('|', n) + new string('.', 10 - n) + " " + pct + "%"; }
        private string Status()
        {
            if (Time.unscaledTime < toastUntil && Toast != "") return Toast;
            return music.Connected ? "Companion connected" : "Music companion not running";
        }

        private void Render()
        {
            var rows = new List<List<Btn>>(); string title;
            Color warn = new Color(1f, .75f, .45f);
            switch (Page)
            {
                default:
                case "home":
                    title = "MAIN MENU [1]";
                    rows.Add(new List<Btn> { B("go:time", "Time"), B("go:weather", "Weather") });
                    rows.Add(new List<Btn> { B("go:music", "Music"), B("go:sfx", "Soundboard") });
                    rows.Add(new List<Btn> { B("go:tools", "Room \u00B7 Timer \u00B7 Mic"), B("go:perf", "Performance") });
                    rows.Add(new List<Btn> { B("go:stream", "Stream (OBS)"), B("go:live", "TikTok LIVE") });
                    rows.Add(new List<Btn> { B("go:mods", "Mod Toggles (next launch)") });
                    rows.Add(new List<Btn> { B("go:settings", "Settings"), B("close", "Close") });
                    rows.Add(new List<Btn> { Lbl(Status() + " \u00B7 " + RoomNote) });
                    break;
                case "time":
                    title = "TIME [2]";
                    for (int p = 0; p < 5; p++)
                    {
                        bool ok = env.PresetIndex[p] >= 0 && env.PresetEnabled[p];
                        rows.Add(new List<Btn> { B("time:" + p, Env.PresetNames[p] + (ok ? "" : " (not verified)"), ok, env.ActivePreset == p) });
                    }
                    rows.Add(new List<Btn> { B("time:auto", "Automatic", true, env.ActivePreset < 0), B("env:restore", "Restore Default") });
                    rows.Add(new List<Btn> { Lbl("Game time: " + env.TimeString()) });
                    rows.Add(new List<Btn> { Lbl(Time.unscaledTime < toastUntil ? Toast : "Only you see this change") });
                    break;
                case "weather":
                    title = "WEATHER [3]";
                    var w = env.ActiveWeather;
                    rows.Add(new List<Btn> { B("wx:Clear", "Clear", true, w == Env.Weather.Clear), B("wx:Rain", "Rain", true, w == Env.Weather.Rain) });
                    rows.Add(new List<Btn> { B("wx:Snow", "Snow", true, w == Env.Weather.Snow), B("wx:Fog", Env.FogSupported ? "Light Fog" : "Fog (n/a)", Env.FogSupported, w == Env.Weather.Fog) });
                    rows.Add(new List<Btn> { B("wx:Thunderstorm", "Thunderstorm", true, w == Env.Weather.Thunderstorm), B("wx:lightning", "Flash: " + (S.lightning ? "ON" : "OFF"), true, S.lightning) });
                    rows.Add(new List<Btn> { B("wx:-", "-"), Lbl("Intensity " + S.weatherIntensity + "%"), B("wx:+", "+") });
                    rows.Add(new List<Btn> { B("env:restore", "Restore Game Default", true, w == Env.Weather.Default && env.ActivePreset < 0) });
                    rows.Add(new List<Btn> { Lbl(Env.FogSupported ? "Game weather: " + env.WeatherString() : "Fog off: game shaders ignore fog") });
                    rows.Add(new List<Btn> { Lbl(Time.unscaledTime < toastUntil ? Toast : "Only you see this change") });
                    break;
                case "music":
                    title = "MUSIC [4]";
                    rows.Add(new List<Btn> { B("tab:spotify", "Spotify", true, MusicTab == "spotify"), B("tab:local", "Local Music", true, MusicTab == "local") });
                    if (!music.Connected)
                    {
                        rows.Add(new List<Btn> { Lbl("Music companion not running", warn) });
                        rows.Add(new List<Btn> { Lbl("Start " + Edition.CompanionExe + " on your PC") });
                        rows.Add(new List<Btn> { Lbl(music.LastError) });
                    }
                    else if (MusicTab == "spotify")
                    {
                        bool av = music.B("spotify", "available");
                        if (!av) { rows.Add(new List<Btn> { Lbl("Spotify unavailable", warn) }); rows.Add(new List<Btn> { Lbl("open Spotify on your PC") }); }
                        else
                        {
                            rows.Add(new List<Btn> { Lbl(music.S("spotify", "title") ?? "(no track)", Color.white) });
                            rows.Add(new List<Btn> { Lbl((music.S("spotify", "artist") ?? "") + " \u00B7 " + (music.B("spotify", "playing") ? "Playing" : "Paused")) });
                        }
                        rows.Add(new List<Btn> { B("sp:previous", "|<", av), B("sp:toggle", music.B("spotify", "playing") ? "Pause" : "Play", av, music.B("spotify", "playing")), B("sp:next", ">|", av) });
                        int sv = (int)music.N("spotify", "volume"); bool vol = av && music.S("spotify", "volumeKind") == "spotify-app" && sv >= 0;
                        rows.Add(new List<Btn> { B("sp:vol-", "-", vol), Lbl(vol ? "Vol " + sv : "Vol n/a"), B("sp:vol+", "+", vol) });
                        rows.Add(new List<Btn> { Lbl(vol ? "Spotify app volume, not Windows master" : "Volume: press play in Spotify once") });
                        if (Time.unscaledTime < toastUntil) rows.Add(new List<Btn> { Lbl(Toast) });
                    }
                    else
                    {
                        bool av = music.B("local", "available"); bool playing = music.B("local", "playing");
                        if (!av) { rows.Add(new List<Btn> { Lbl("No music found", warn) }); rows.Add(new List<Btn> { Lbl("Put MP3/WAV/OGG in Documents\\" + Edition.DataFolder + "\\Music") }); }
                        else
                        {
                            rows.Add(new List<Btn> { Lbl(music.S("local", "title") ?? "(nothing playing)", Color.white) });
                            rows.Add(new List<Btn> { Lbl(Clock(music.N("local", "position")) + " / " + Clock(music.N("local", "duration")) + " \u00B7 " + (music.S("local", "playlist") ?? "")) });
                        }
                        rows.Add(new List<Btn> { B("lo:previous", "|<", av), B("lo:toggle", playing ? "Pause" : "Play", av, playing), B("lo:next", ">|", av) });
                        rows.Add(new List<Btn> { B("lo:vol-", "-", av), Lbl("Volume " + (int)music.N("local", "volume")), B("lo:vol+", "+", av) });
                        int rep = (int)music.N("local", "repeat");
                        rows.Add(new List<Btn> { B("lo:shuffle", "Shuffle", av, music.B("local", "shuffle")), B("lo:repeat", rep == 0 ? "Repeat: Off" : rep == 1 ? "Repeat: One" : "Repeat: All", av, rep != 0) });
                        rows.Add(new List<Btn> { B("lo:back10", "-10s", av), B("lo:fwd10", "+10s", av), B("go:queue", "Queue", av) });
                        rows.Add(new List<Btn> { B("lo:playlist", "List: " + (music.S("local", "playlist") ?? "-"), av && music.N("local", "playlistCount") > 1), B("lo:rescan", "Rescan") });
                        var err = music.S("local", "error"); if (Time.unscaledTime < toastUntil) err = Toast;
                        if (!string.IsNullOrEmpty(err) && rows.Count < 8) rows.Add(new List<Btn> { Lbl(err, warn) });
                    }
                    break;
                case "queue":
                    title = "QUEUE [5]";
                    int off, total; var tr = music.Tracks(out off, out total); var cur = music.S("local", "trackId");
                    for (int i = 0; i < 5; i++)
                    {
                        if (tr != null && i < tr.Count && tr[i] is Dictionary<string, object> d) { var id = d["id"] as string; rows.Add(new List<Btn> { B("q:" + id, (off + i + 1) + ". " + (d["name"] as string), true, id == cur) }); }
                        else rows.Add(new List<Btn> { Lbl(i == 0 && (tr == null || tr.Count == 0) ? "(queue empty)" : "") });
                    }
                    rows.Add(new List<Btn> { B("q:up", "< Prev", off > 0), B("q:down", "Next >", off + 5 < total) });
                    rows.Add(new List<Btn> { Lbl(total + " tracks \u00B7 " + (music.S("local", "playlist") ?? "")) });
                    break;
                case "stream": title = "STREAM TOOLS [9]"; StreamPage(rows, warn); break;
                case "sfx": title = "SOUNDBOARD [10]"; SfxPage(rows, warn); break;
                case "perf": title = "PERFORMANCE [13]"; PerfPage(rows); break;
                case "live": title = "TIKTOK LIVE [14]"; LivePage(rows, warn); break;
                case "mods": title = "MOD TOGGLES [11]"; ModsPage(rows, warn); break;
                case "tools": title = "ROOM \u00B7 TIMER \u00B7 MIC [7]"; ToolsPage(rows, warn); break;
                case "theme": title = "COLORS & THEME [8]"; ThemePage(rows); break;
                case "settings":
                    title = "SETTINGS [6]";
                    rows.Add(new List<Btn> { B("set:anchor", S.anchor == "Float" ? "Floating" : "On Wrist"), B("set:hand", S.rightHanded ? "Right-handed" : "Left-handed") });
                    rows.Add(new List<Btn> { B("set:scale-", "-"), Lbl("Size " + Mathf.RoundToInt(S.scale * 100) + "%"), B("set:scale+", "+") });
                    rows.Add(new List<Btn> { B("set:angle-", "-"), Lbl("Angle " + S.angle + "\u00B0"), B("set:angle+", "+") });
                    rows.Add(new List<Btn> { B("go:theme", "Colors: " + S.theme), B("set:click", "Click " + (S.clickSound ? "ON" : "OFF"), true, S.clickSound), B("set:haptics", "Haptic " + (S.haptics ? "ON" : "OFF"), true, S.haptics) });
                    rows.Add(new List<Btn> { B("set:pauseprev", "Autopause " + (music.PausePrevious ? "ON" : "OFF"), music.Connected, music.PausePrevious), B("set:device", "Music out: " + (music.S("local", "device") ?? "-"), music.Connected), B("set:folder", "Music dir", music.Connected) });
                    rows.Add(new List<Btn> { B("set:openbtn", "Open: " + ButtonLabel(S.openButton)), B("set:palm", "Palm " + (S.palmOpen ? "ON" : "OFF"), true, S.palmOpen), B("set:calib", "Calibrate palm") });
                    rows.Add(new List<Btn> { B("set:resetpos", "Reset board spot"), B("set:reset", "Reset all") });
                    rows.Add(new List<Btn> { Lbl(Time.unscaledTime < toastUntil ? Toast : "Grab the top bar (grip) to move \u00B7 v" + Version) });
                    break;
            }
            board.SetPage(title, rows);
        }

        public void Press(string id)
        {
            Feedback(); Logger.LogInfo("Board press: " + id + " (page " + Page + ")"); LastPress = id; PressCount++;
            string err = null;
            if (id.StartsWith("go:")) { history.Push(Page); Page = id.Substring(3); if (Page == "queue") { QueueOffset = Mathf.Max(0, (int)music.N("local", "queueIndex") / 5 * 5); music.Send("local", "listTracks", QueueOffset); } return; }
            if (ToolsPress(id) || StreamPress(id) || LivePress(id)) return;
            switch (id)
            {
                case "nav:back": Page = history.Count > 0 ? history.Pop() : "home"; return;
                case "nav:home": history.Clear(); Page = "home"; return;
                case "nav:hide": case "close": Open = false; floatPlaced = false; grabbing = false; palmArmed = false; if (board?.Root != null) board.Root.SetActive(false); Visible = false; if (Sim || ForceShow) ForceShow = false; debugShow = false; return;
                case "time:auto": env.Automatic(); return;
                case "env:restore": env.RestoreAll("Restore Default pressed"); return;
                case "wx:-": S.weatherIntensity = Mathf.Max(0, S.weatherIntensity - 10); env.Intensity = S.weatherIntensity / 100f; SaveSettings(); return;
                case "wx:+": S.weatherIntensity = Mathf.Min(100, S.weatherIntensity + 10); env.Intensity = S.weatherIntensity / 100f; SaveSettings(); return;
                case "wx:lightning": S.lightning = !S.lightning; env.Lightning = S.lightning; SaveSettings(); return;
                case "tab:spotify": MusicTab = "spotify"; return;
                case "tab:local": MusicTab = "local"; return;
                case "sp:toggle": music.Send("spotify", "toggle"); return;
                case "sp:next": music.Send("spotify", "next"); return;
                case "sp:previous": music.Send("spotify", "previous"); return;
                case "sp:vol-": music.Send("spotify", "setVolume", Mathf.Clamp((int)music.N("spotify", "volume") - 10, 0, 100)); return;
                case "sp:vol+": music.Send("spotify", "setVolume", Mathf.Clamp((int)music.N("spotify", "volume") + 10, 0, 100)); return;
                case "lo:toggle": music.Send("local", "toggle"); return;
                case "lo:next": music.Send("local", "next"); return;
                case "lo:previous": music.Send("local", "previous"); return;
                case "lo:vol-": music.Send("local", "setVolume", Mathf.Clamp((int)music.N("local", "volume") - 10, 0, 100)); return;
                case "lo:vol+": music.Send("local", "setVolume", Mathf.Clamp((int)music.N("local", "volume") + 10, 0, 100)); return;
                case "lo:shuffle": music.Send("local", "setShuffle", music.B("local", "shuffle") ? 0 : 1); return;
                case "lo:repeat": music.Send("local", "setRepeat", ((int)music.N("local", "repeat") + 1) % 3); return;
                case "lo:back10": music.Send("local", "seekBy", -10); return;
                case "lo:fwd10": music.Send("local", "seekBy", 10); return;
                case "lo:playlist": { int n = Mathf.Max(1, (int)music.N("local", "playlistCount")); music.Send("local", "selectPlaylist", ((int)music.N("local", "playlistIndex") + 1) % n); return; }
                case "lo:rescan": music.Send("local", "rescan"); Say("Rescanning music folder"); return;
                case "q:up": QueueOffset = Mathf.Max(0, QueueOffset - 5); music.Send("local", "listTracks", QueueOffset); return;
                case "q:down": QueueOffset += 5; music.Send("local", "listTracks", QueueOffset); return;
                case "set:anchor": S.anchor = S.anchor == "Float" ? "Wrist" : "Float"; floatPlaced = false; SaveSettings(); return;
                case "set:hand": S.rightHanded = !S.rightHanded; SaveSettings(); return;
                case "set:scale-": S.scale = Mathf.Max(.4f, Mathf.Round((S.scale - .1f) * 10) / 10f); SaveSettings(); return;
                case "set:scale+": S.scale = Mathf.Min(1f, Mathf.Round((S.scale + .1f) * 10) / 10f); SaveSettings(); return;
                case "set:angle-": S.angle = Mathf.Max(-30, S.angle - 10); floatPlaced = false; SaveSettings(); return;
                case "set:angle+": S.angle = Mathf.Min(30, S.angle + 10); floatPlaced = false; SaveSettings(); return;
                case "set:click": S.clickSound = !S.clickSound; SaveSettings(); return;
                case "set:haptics": S.haptics = !S.haptics; SaveSettings(); return;
                case "set:pauseprev": music.Send(null, "setPausePrevious", music.PausePrevious ? 0 : 1); return;
                case "set:folder": music.Send(null, "openMusicFolder"); Say("Opened the music folder on your PC"); return;
                case "set:device":
                    {
                        var devs = music.Devices; var curDev = music.S("local", "device") ?? "";
                        var list = new List<string> { "" }; if (devs != null) foreach (var o in devs) if (o is string s) list.Add(s);
                        int ci = 0; for (int i = 1; i < list.Count; i++) if (curDev == list[i] || curDev.StartsWith(list[i])) ci = i;
                        if (curDev.StartsWith("Windows default")) ci = 0;
                        var nx = list[(ci + 1) % list.Count]; music.Send(null, "setDevice", null, null, nx); Say("Music output: " + (nx == "" ? "Windows default" : nx)); return;
                    }
                case "set:openbtn": S.openButton = OpenModes[(Array.IndexOf(OpenModes, S.openButton) + 1) % OpenModes.Length]; btnWas = true; SaveSettings(); Say("Open/close: " + ButtonLabel(S.openButton)); return;
                case "set:palm": S.palmOpen = !S.palmOpen; SaveSettings(); Say(S.palmOpen ? "Palm open ON: look at your palm" : "Palm open OFF"); return;
                case "set:calib": StartPalmCalibration(); return;
                case "set:resetpos": { var d = new BoardSettings(); S.posDist = d.posDist; S.posYaw = d.posYaw; S.posDown = d.posDown; S.posSaved = false; S.anchor = "Float"; floatPlaced = false; SaveSettings(); Say("Board spot reset (beside and below your view)"); return; }
                case "set:reset": S = new BoardSettings { cfgVersion = 2 }; env.Intensity = S.weatherIntensity / 100f; env.Lightning = S.lightning; ApplyCurrentTheme(); floatPlaced = false; SaveSettings(); Say("Settings reset"); return;
            }
            if (id.StartsWith("time:")) { int p; if (int.TryParse(id.Substring(5), out p)) err = env.SetPreset(p); }
            else if (id.StartsWith("wx:")) { Env.Weather w; if (Enum.TryParse(id.Substring(3), out w)) err = env.SetWeather(w); }
            else if (id.StartsWith("q:")) music.Send("local", "play", null, id.Substring(2));
            if (err != null) Say(err);
        }
        public string LastPress = ""; public int PressCount;
    }
}
