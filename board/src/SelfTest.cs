using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UtilityBoard
{
    // Desktop self-test (Debug.SelfTest = true; isolated test copy with -vrmode None only). A simulated fingertip
    // (white ball) drives the SAME Board.Poke / Press code paths the real index fingertip uses.
    public partial class Plugin
    {
        private int pass, fail; private readonly List<string> summary = new List<string>(); private string outDir;
        private void R(string n, bool ok, string d) { if (ok) pass++; else fail++; var s = "SELFTEST RESULT " + n + ": " + (ok ? "PASS" : "FAIL") + " - " + d; summary.Add(s); Logger.LogInfo(s); }
        private GameObject tipBall; private TextMesh shotLabel; private Camera mc;

        private Camera boardCam, envCam;
        private float PageShot(string name, string label) => Capture(name, label, false, 900, 1280, BoardCam());
        private Camera BoardCam()
        {
            if (boardCam == null) { var g = new GameObject("selftest_boardcam"); boardCam = g.AddComponent<Camera>(); boardCam.enabled = false; boardCam.fieldOfView = 40; boardCam.nearClipPlane = .02f; }
            var t = board.Root.transform; float d = .37f * S.scale / .6f;
            boardCam.transform.position = t.position - t.forward * d + t.up * .012f * S.scale / .6f; boardCam.transform.rotation = Quaternion.LookRotation(t.forward, t.up);
            if (shotLabel != null) { shotLabel.transform.parent.position = t.position + t.up * (Board.H * S.scale * .5f + .012f) - t.forward * .01f; shotLabel.transform.parent.rotation = t.rotation; }
            return boardCam;
        }
        // An outdoor viewpoint (open sky above) so time-of-day and weather changes are actually visible in the shots.
        private bool FindOutdoor(Vector3 from, out Vector3 eye, out Quaternion rot)
        {
            eye = from; rot = Quaternion.identity; float bestScore = -1;
            for (float r = 12; r <= 70; r += 6) for (int k = 0; k < 24; k++)
            {
                var dir = Quaternion.Euler(0, k * 15f, 0) * Vector3.forward; var q = from + dir * r; RaycastHit h;
                if (!Physics.Raycast(q + Vector3.up * 120f, Vector3.down, out h, 240f, ~0, QueryTriggerInteraction.Ignore) || h.normal.y < .7f) continue;
                var stand = h.point + Vector3.up * 1.7f; if (Physics.Raycast(stand, Vector3.up, 300f, ~0, QueryTriggerInteraction.Ignore)) continue;
                float open = 0; for (int j = 0; j < 8; j++) { var d = Quaternion.Euler(0, j * 45f, 0) * Vector3.forward; open += Physics.Raycast(stand, d, out var hh, 30f, ~0, QueryTriggerInteraction.Ignore) ? hh.distance : 30f; }
                if (open > bestScore) { bestScore = open; eye = stand; var look = (from - stand); look.y = 0; rot = Quaternion.LookRotation(look.normalized) * Quaternion.Euler(-12f, 0, 0); }
            }
            return bestScore > 0;
        }
        private float Capture(string name, string label, bool hideBoard = false, int w = 1280, int h = 720, Camera c = null)
        {
            float lum = -1; var mcSave = mc; if (c != null) mc = c;
            try
            {
                bool was = board?.Root != null && board.Root.activeSelf; if (hideBoard && was) board.Root.SetActive(false);
                if (shotLabel != null) { shotLabel.text = "DESKTOP SELF-TEST (no VR, test copy) - " + label; shotLabel.gameObject.SetActive(true); }
                var rt = RenderTexture.GetTemporary(w, h, 24); var old = mc.targetTexture; mc.targetTexture = rt; mc.Render(); mc.targetTexture = old;
                var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                var px = tex.GetPixels32(); double sum = 0; for (int i = 0; i < px.Length; i += 7) sum += .2126 * px[i].r + .7152 * px[i].g + .0722 * px[i].b; lum = (float)(sum / (px.Length / 7.0));
                if (name != null) File.WriteAllBytes(Path.Combine(outDir, "shot_" + name + ".png"), tex.EncodeToPNG());
                Destroy(tex); if (hideBoard && was) board.Root.SetActive(true);
                if (name != null) Logger.LogInfo("SELFTEST SHOT-STATE " + name + ": " + label + " | mean luminance " + lum.ToString("F1"));
            }
            catch (Exception e) { Logger.LogWarning("capture failed " + e.Message); }
            mc = mcSave; return lum;
        }
        private IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        private IEnumerator TipAt(Board.Btn b, float zMeters, int frames, float xOff = 0)
        {
            SimTip = board.FaceCenterWorld(b, zMeters) + board.Root.transform.right * xOff; SimTipOn = true;
            if (tipBall != null) tipBall.transform.position = SimTip; yield return Frames(frames);
        }
        private IEnumerator Tap(string id, float settle = .35f)
        {
            yield return Frames(2); var b = board.Find(id);
            if (b == null) { R("tap " + id, false, "button not on page " + Page); yield break; }
            yield return TipAt(b, -.03f, 3); yield return TipAt(b, -.004f, 3); yield return TipAt(b, -.03f, 3);
            yield return new WaitForSecondsRealtime(settle);
        }

        private IEnumerator SelfTest()
        {
            outDir = testOutDir.Value; if (string.IsNullOrEmpty(outDir)) outDir = Path.Combine(BepInEx.Paths.BepInExRootPath, "ub_selftest"); Directory.CreateDirectory(outDir);
            NoSave = true; S = new BoardSettings(); env.Intensity = .6f; env.Lightning = false;
            Logger.LogInfo("SELFTEST start");
            float w0 = Time.realtimeSinceStartup; while ((GTApi.Tagger == null || Camera.main == null || !env.Ready()) && Time.realtimeSinceStartup - w0 < 150) yield return new WaitForSecondsRealtime(1);
            yield return new WaitForSecondsRealtime(8);
            mc = Camera.main; if (mc == null) { Logger.LogInfo("SELFTEST ABORT no camera"); yield break; }
            cam = mc.transform;
            var sl = new GameObject("selftest_label"); shotLabel = new Board().Text(sl.transform, "", .0011f, Vector3.zero); shotLabel.color = new Color(1f, 1f, .6f);
            Allowed();
            R("environment", env.Ready(), "room " + RoomNote + ", lightmaps " + env.LightmapNames.Length + ", music link " + (music.Connected ? "connected" : "NOT connected: " + music.LastError));
            Vector3 fingerTip; bool haveRealTip = Tip(out fingerTip); R("game fingertip lookup", haveRealTip && TipNote != "", TipNote + (haveRealTip ? " at " + fingerTip : ""));

            // ---------- board + fingertip presses ----------
            Sim = true; SimHead = cam.position; SimOffHand = cam.position + Vector3.down * 2f; SimTipOn = false;
            ForceShow = true; ForcePos = cam.position + cam.forward * .30f; ForceRot = Quaternion.LookRotation(cam.forward, Vector3.up);
            sl.transform.position = cam.position + cam.forward * .30f + cam.up * .135f; sl.transform.rotation = ForceRot;
            yield return Frames(5);
            R("board shows", Visible && board?.Root != null && board.Root.activeInHierarchy, "board built with " + board?.ShaderNote + ", " + board?.Buttons.Count + " controls, scale " + S.scale);
            tipBall = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(tipBall.GetComponent<Collider>()); tipBall.transform.localScale = Vector3.one * .012f; tipBall.name = "selftest_fingertip";
            tipBall.GetComponent<Renderer>().sharedMaterial = board.Root.GetComponentInChildren<Renderer>().sharedMaterial;
            Board.SetCol(tipBall.GetComponent<Renderer>().material, Color.white);
            Capture("01_home", "Main Menu in front of the player (white dot = simulated fingertip)"); PageShot("01b_home_closeup", "Main Menu");

            yield return Tap("go:settings");
            R("navigate by fingertip", Page == "settings", "page now " + Page);
            var bt = board.Find("set:haptics"); bool click0 = S.haptics; int c0 = PressCount;
            yield return TipAt(bt, -.030f, 5); R("hover 3 cm in front: no press", PressCount == c0, "presses " + (PressCount - c0));
            yield return TipAt(bt, -.004f, 3); R("fingertip touches face (4 mm before surface): press", PressCount == c0 + 1 && S.haptics != click0, "presses " + (PressCount - c0) + ", click " + click0 + "->" + S.haptics);
            PageShot("02_touch", "fingertip touching the visible button face (pressed = green, pushed in)");
            yield return TipAt(bt, .005f, 40); R("holding: exactly once", PressCount == c0 + 1, "presses after 40 held frames " + (PressCount - c0));
            yield return TipAt(bt, -.030f, 5); yield return new WaitForSecondsRealtime(.3f);
            yield return TipAt(bt, -.004f, 3); R("leave + touch again: re-armed", PressCount == c0 + 2 && S.haptics == click0, "presses " + (PressCount - c0));
            yield return TipAt(bt, -.030f, 5); yield return new WaitForSecondsRealtime(.3f);
            yield return TipAt(bt, .060f, 5); yield return TipAt(bt, .012f, 5); R("reaching in from BEHIND the board: no press", PressCount == c0 + 2, "presses " + (PressCount - c0));
            yield return TipAt(bt, -.030f, 5); yield return new WaitForSecondsRealtime(.3f);
            yield return TipAt(bt, -.004f, 4, bt.w * S.scale / 2 + .012f); R("1.2 cm beside the button: no press", PressCount == c0 + 2, "presses " + (PressCount - c0));
            var nb = board.Find("set:click"); bool h0 = S.clickSound; yield return TipAt(bt, -.03f, 3); yield return new WaitForSecondsRealtime(.3f);
            yield return TipAt(nb, -.004f, 3); yield return TipAt(nb, -.03f, 3); R("neighbour isolation", S.clickSound != h0 && S.haptics == click0, "only click toggled");
            S.clickSound = h0; yield return new WaitForSecondsRealtime(.3f);
            PageShot("03_settings", "Settings");
            yield return Tap("nav:home"); R("home button", Page == "home", Page);

            // ---------- time ----------
            Vector3 eye; Quaternion erot; bool outdoor = FindOutdoor(cam.position, out eye, out erot);
            { var g = new GameObject("selftest_envcam"); envCam = g.AddComponent<Camera>(); envCam.enabled = false; envCam.fieldOfView = 70; envCam.nearClipPlane = .05f; envCam.transform.SetPositionAndRotation(outdoor ? eye : cam.position, outdoor ? erot : cam.rotation); }
            if (outdoor) EnvFollow = envCam.transform;
            R("outdoor viewpoint for time/weather shots", outdoor, outdoor ? "camera at " + eye + " (open sky)" : "none found - shots use the player camera");
            yield return Tap("go:time");
            var saveEn = (bool[])env.PresetEnabled.Clone(); for (int i = 0; i < 5; i++) env.PresetEnabled[i] = true;   // test every mapped preset, then report which verified
            PageShot("04_time", "Time page");
            float lumDefault = Capture(null, "", true, 1280, 720, envCam); var lums = new float[5]; var verified = new List<int>();
            for (int p = 0; p < 5; p++)
            {
                if (env.PresetIndex[p] < 0) { R("time " + Env.PresetNames[p], false, "no lightmap matched"); continue; }
                yield return Tap("time:" + p); yield return new WaitForSecondsRealtime(2.5f);
                int ci = env.CurrentIndex(); string ts = env.TimeString();
                lums[p] = Capture("05_time_" + Env.PresetNames[p].ToLower(), "Time: " + Env.PresetNames[p] + " (outdoor view)", true, 1280, 720, envCam);
                bool ok = ci == env.PresetIndex[p] && ts != "DEFAULT" && env.ActivePreset == p;
                if (ok) verified.Add(p);
                R("time " + Env.PresetNames[p], ok, "lightmap " + env.PresetIndex[p] + " '" + env.LightmapNames[env.PresetIndex[p]] + "', game index " + ci + ", game says " + ts + ", luminance " + lums[p].ToString("F1"));
            }
            if (env.PresetIndex[2] >= 0 && env.PresetIndex[4] >= 0) R("night visibly darker than day (outdoor view)", lums[4] < lums[2] - 5, "day " + lums[2].ToString("F1") + " night " + lums[4].ToString("F1") + " sunrise " + lums[0].ToString("F1") + " morning " + lums[1].ToString("F1") + " sunset " + lums[3].ToString("F1"));
            yield return Tap("time:auto"); yield return new WaitForSecondsRealtime(1.5f);
            R("automatic restores game cycle", env.TimeString() == "DEFAULT" && env.ActivePreset < 0, "game says " + env.TimeString());
            Logger.LogInfo("SELFTEST VERIFIED_PRESETS " + string.Join(",", verified) + " map " + string.Join(",", env.PresetIndex));
            for (int i = 0; i < 5; i++) env.PresetEnabled[i] = saveEn[i];

            // ---------- weather ----------
            yield return Tap("nav:back"); yield return Tap("go:weather");
            bool oFog = RenderSettings.fog; float oDen = RenderSettings.fogDensity;
            float lumClear = Capture("06a_clear_before", "Weather: game default (outdoor view)", true, 1280, 720, envCam);
            yield return Tap("wx:Rain"); yield return new WaitForSecondsRealtime(3f);
            R("rain (local)", env.WeatherString() == "RAINING" && env.OwnRainAlive > 100, "game weather " + env.WeatherString() + ", mod raindrops alive " + env.OwnRainAlive + ", game rain systems " + env.RainSystems);
            Capture("06_rain", "Weather: Rain 60% (outdoor view)", true, 1280, 720, envCam); PageShot("06b_weather_page", "Weather page");
            int r60 = env.OwnRainAlive; yield return Tap("wx:-"); yield return Tap("wx:-"); yield return Tap("wx:-"); yield return new WaitForSecondsRealtime(2.5f); int r30 = env.OwnRainAlive;
            Capture("06c_rain30", "Weather: Rain 30% (outdoor view)", true, 1280, 720, envCam);
            R("intensity slider changes rain", S.weatherIntensity == 30 && r30 < r60 * .8f, "drops alive at 60% " + r60 + " -> at 30% " + r30);
            yield return Tap("wx:+"); yield return Tap("wx:+"); yield return Tap("wx:+");
            yield return Tap("wx:Snow"); yield return new WaitForSecondsRealtime(3f);
            R("snow (mod's own particles)", env.SnowAlive > 50 && env.WeatherString() != "RAINING", "snowflakes alive " + env.SnowAlive + ", material " + env.SnowMaterialNote + ", game weather " + env.WeatherString());
            Capture("07_snow", "Weather: Snow 60% (outdoor view)", true, 1280, 720, envCam);
            var fb = board.Find("wx:Fog"); int pcF = PressCount; yield return Tap("wx:Fog");
            R("fog disabled with explanation (game shaders ignore scene fog - measured in run 2)", fb != null && !fb.enabled && PressCount == pcF && !RenderSettings.fog == !oFog, "button enabled=" + (fb != null && fb.enabled) + ", label '" + (fb != null ? fb.label : "") + "'");
            Env.FogSupported = true; yield return Frames(2); env.SetWeather(Env.Weather.Fog); yield return new WaitForSecondsRealtime(1f);
            float lumFog = Capture("08_fog_unsupported", "Fog attempt (Unity scene fog) - no visible change, so the control is disabled", true, 1280, 720, envCam);
            FogVisible = Mathf.Abs(lumFog - lumClear) > 2f; env.SetWeather(Env.Weather.Clear); Env.FogSupported = false;
            R("fog really has no visible effect (re-measured)", !FogVisible, "luminance clear " + lumClear.ToString("F1") + " vs fog " + lumFog.ToString("F1"));
            yield return Tap("wx:Thunderstorm"); yield return new WaitForSecondsRealtime(8f);
            R("thunderstorm (heavy rain, flash off by default)", env.WeatherString() == "RAINING" && env.Flashes == 0 && env.OwnRainAlive > r60 && RenderSettings.fog == oFog, "game weather " + env.WeatherString() + ", flashes " + env.Flashes + ", drops " + env.OwnRainAlive);
            Capture("09_thunder", "Weather: Thunderstorm (outdoor view)", true, 1280, 720, envCam);
            yield return Tap("wx:lightning"); float fw0 = Time.realtimeSinceStartup; while (env.Flashes == 0 && Time.realtimeSinceStartup - fw0 < 25) yield return null;
            R("optional lightning flash (when switched on)", env.Flashes > 0, "flashes " + env.Flashes + " using lightmap '" + (env.LightningIndex >= 0 ? env.LightmapNames[env.LightningIndex] : "day") + "'");
            yield return Tap("wx:lightning");
            yield return Tap("wx:Clear"); yield return new WaitForSecondsRealtime(1.5f);
            R("clear", env.WeatherString() != "RAINING" && env.SnowAlive == 0 && env.OwnRainAlive == 0 && RenderSettings.fog == oFog, "game weather " + env.WeatherString());
            yield return Tap("env:restore"); yield return new WaitForSecondsRealtime(1f);
            R("restore game default", env.WeatherString() == "DEFAULT" && env.TimeString() == "DEFAULT" && RenderSettings.fog == oFog && Mathf.Approximately(RenderSettings.fogDensity, oDen), "time " + env.TimeString() + ", weather " + env.WeatherString() + ", fog restored");

            // ---------- music (companion running in test mode: local output muted, Spotify commands dry-run) ----------
            yield return Tap("nav:home"); yield return Tap("go:music");
            float mw = Time.realtimeSinceStartup; while (!music.Connected && Time.realtimeSinceStartup - mw < 10) yield return null;
            R("companion link (named pipe from inside the game)", music.Connected && music.State != null, music.Connected ? "replies " + music.Replies : music.LastError);
            if (music.Connected)
            {
                yield return Tap("tab:local"); yield return new WaitForSecondsRealtime(1.2f);
                PageShot("10_music_local", "Music: Local tab");
                yield return Tap("lo:toggle"); yield return new WaitForSecondsRealtime(2f);
                string t1 = music.S("local", "title"); R("local play", music.B("local", "playing"), "playing '" + t1 + "' pos " + music.N("local", "position"));
                PageShot("11_music_playing", "Music: Local playing");
                yield return Tap("lo:next"); yield return new WaitForSecondsRealtime(1.5f);
                R("local next", music.S("local", "title") != t1 && music.B("local", "playing"), t1 + " -> " + music.S("local", "title"));
                yield return Tap("lo:previous"); yield return new WaitForSecondsRealtime(1.5f); R("local previous", music.S("local", "title") == t1, "back to " + music.S("local", "title"));
                int v0 = (int)music.N("local", "volume"); yield return Tap("lo:vol+"); yield return new WaitForSecondsRealtime(1.5f);
                R("local volume +", (int)music.N("local", "volume") == Mathf.Min(100, v0 + 10), v0 + " -> " + music.N("local", "volume"));
                yield return Tap("lo:vol-"); yield return new WaitForSecondsRealtime(1.2f);
                bool sh0 = music.B("local", "shuffle"); yield return Tap("lo:shuffle"); yield return new WaitForSecondsRealtime(1.5f); bool sh = music.B("local", "shuffle"); yield return Tap("lo:shuffle"); yield return new WaitForSecondsRealtime(1.5f);
                R("shuffle toggle", sh != sh0 && music.B("local", "shuffle") == sh0, sh0 + " -> " + sh + " -> " + music.B("local", "shuffle") + " (starts from your saved setting)");
                int rp0 = (int)music.N("local", "repeat"); yield return Tap("lo:repeat"); yield return new WaitForSecondsRealtime(1.5f);
                R("repeat cycle", (int)music.N("local", "repeat") == (rp0 + 1) % 3, rp0 + " -> " + music.N("local", "repeat"));
                for (int k = 0; k < 2; k++) { yield return Tap("lo:repeat"); yield return new WaitForSecondsRealtime(1.2f); }
                yield return Tap("lo:playlist"); yield return new WaitForSecondsRealtime(1.5f); R("playlist cycle", music.N("local", "playlistIndex") == 1, "now '" + music.S("local", "playlist") + "'");
                yield return Tap("lo:playlist"); yield return new WaitForSecondsRealtime(1.5f);
                yield return Tap("go:queue"); yield return new WaitForSecondsRealtime(1.5f);
                int qo, qt; var items = music.Tracks(out qo, out qt);
                PageShot("12_queue", "Queue page");
                if (items != null && items.Count > 1 && items[1] is Dictionary<string, object> d1)
                {
                    var tid = d1["id"] as string; yield return Tap("q:" + tid); yield return new WaitForSecondsRealtime(1.5f);
                    R("queue pick track", music.S("local", "trackId") == tid, "picked '" + d1["name"] + "'");
                }
                else R("queue pick track", false, "queue items " + (items == null ? "null" : items.Count.ToString()));
                yield return Tap("nav:back"); yield return Tap("lo:toggle"); yield return new WaitForSecondsRealtime(1.5f);
                R("local pause", !music.B("local", "playing"), "playing=" + music.B("local", "playing"));
                yield return Tap("tab:spotify"); yield return new WaitForSecondsRealtime(1.5f);
                PageShot("13_music_spotify", "Music: Spotify tab (real now-playing from Spotify desktop)");
                R("spotify now-playing shown", !music.B("spotify", "available") || !string.IsNullOrEmpty(music.S("spotify", "title")), "available " + music.B("spotify", "available") + ", '" + music.S("spotify", "title") + "' by '" + music.S("spotify", "artist") + "'");
                int rep0 = music.Replies; yield return Tap("sp:toggle"); yield return Tap("sp:next"); yield return Tap("sp:previous"); yield return new WaitForSecondsRealtime(1.5f);
                R("spotify buttons reach companion (dry-run, Spotify untouched)", music.Replies > rep0 && music.LastReplyError == "", "replies +" + (music.Replies - rep0) + ", last error '" + music.LastReplyError + "'");
                yield return Tap("nav:home"); yield return Tap("go:settings"); bool pp = music.PausePrevious; yield return Tap("set:pauseprev"); yield return new WaitForSecondsRealtime(1.5f);
                R("pause-previous toggle", music.PausePrevious != pp, pp + " -> " + music.PausePrevious); yield return Tap("set:pauseprev"); yield return new WaitForSecondsRealtime(1.2f);
                yield return Tap("nav:home"); yield return Tap("go:tools"); yield return new WaitForSecondsRealtime(1f);
                bool micAv = MicAvailable; bool m0 = MicMuted;
                if (micAv) { yield return Tap("mic:toggle"); yield return new WaitForSecondsRealtime(1.5f); bool m1 = MicMuted; PageShot("19_mic_muted", "Tools: mic MUTED (companion test mode: dry-run, real mic untouched)"); yield return Tap("mic:toggle"); yield return new WaitForSecondsRealtime(1.5f);
                    R("mic mute toggle via companion (dry-run)", m1 != m0 && MicMuted == m0 && music.B("mic", "dry"), m0 + " -> " + m1 + " -> " + MicMuted + ", device '" + music.S("mic", "name") + "', dry " + music.B("mic", "dry")); }
                else R("mic mute toggle via companion (dry-run)", false, "mic unavailable: " + music.S("mic", "error"));
                yield return Tap("nav:home");
            }

            // ---------- v1.2.0 stage C: OBS (mock websocket), soundboard, mod toggles (test copy placeholders) ----------
            if (music.Connected)
            {
                yield return Tap("nav:home"); yield return Tap("go:stream"); yield return new WaitForSecondsRealtime(3.5f);
                var scn = L("obs", "scenes");
                R("stream: OBS (mock) connected, all scenes listed", music.B("obs", "available") && scn != null && scn.Count == 5 && board.Find("obs:scene:0") != null, "available " + music.B("obs", "available") + ", scenes " + (scn == null ? "null" : string.Join("|", scn.ConvertAll(x => x as string).ToArray())) + ", err " + music.S("obs", "error"));
                PageShot("23_stream", "Stream tools (mock OBS, replay buffer running)");
                yield return Tap("obs:rec"); yield return new WaitForSecondsRealtime(1.5f); bool recOn = music.B("obs", "recording");
                PageShot("24_stream_rec", "Stream tools: recording (mock OBS)");
                yield return Tap("obs:rec"); yield return new WaitForSecondsRealtime(1.5f);
                R("stream: record start/stop (mock)", recOn && !music.B("obs", "recording"), "after start " + recOn + ", after stop " + music.B("obs", "recording"));
                yield return Tap("obs:scene:2"); yield return new WaitForSecondsRealtime(1.5f);
                R("stream: scene switch (mock)", (int)music.N("obs", "sceneIndex") == 2, "scene " + music.S("obs", "scene"));
                yield return Tap("obs:clip"); yield return new WaitForSecondsRealtime(1.5f);
                R("stream: clip saves replay buffer (mock)", music.S("obs", "last") == "Replay saved", "last '" + music.S("obs", "last") + "'");
                yield return Tap("nav:home"); yield return Tap("go:sfx"); yield return new WaitForSecondsRealtime(1.2f);
                int pl0 = (int)music.N("sfx", "played");
                R("soundboard: pads from the SFX folder", board.Find("sfx:0") != null && board.Find("sfx:2") != null && music.N("sfx", "count") >= 3, "count " + music.N("sfx", "count"));
                PageShot("25_soundboard", "Soundboard (3 generated test sounds, muted in test mode)");
                yield return Tap("sfx:0"); yield return Tap("sfx:1"); yield return new WaitForSecondsRealtime(1.2f);
                R("soundboard: pads play via companion (muted test)", (int)music.N("sfx", "played") == pl0 + 2, "played +" + ((int)music.N("sfx", "played") - pl0));
                yield return Tap("sfx:stop"); yield return new WaitForSecondsRealtime(1f); R("soundboard: stop all", music.N("sfx", "active") == 0, "active " + music.N("sfx", "active"));
            }
            else R("stage C pages need the test companion", false, music.LastError);

            // ---------- v1.1.0 stage B: room code, timer + clock, theme picker (no companion needed) ----------
            yield return Tap("nav:home"); yield return Tap("go:tools");
            SimRoom = 1; SimRoomName = "TESTROOM42"; yield return Frames(3);
            var rl = board.Buttons.Find(b => b.isLabel && b.label.StartsWith("Room:"));
            R("room code hidden until tapped", rl != null && !rl.label.Contains("TESTROOM42") && rl.label.Contains("\u2022"), "label '" + rl?.label + "'");
            PageShot("16_room_hidden", "Tools: room code hidden (simulated private room TESTROOM42)");
            yield return Tap("room:reveal"); rl = board.Buttons.Find(b => b.isLabel && b.label.StartsWith("Room:"));
            R("tap shows room code", rl != null && rl.label.Contains("TESTROOM42"), "label '" + rl?.label + "'");
            PageShot("17_room_shown", "Tools: room code revealed (auto-hides after 15 s)");
            roomRevealUntil = Time.unscaledTime - 1; yield return Frames(3); rl = board.Buttons.Find(b => b.isLabel && b.label.StartsWith("Room:"));
            R("room code auto-hides after 15 s", rl != null && !rl.label.Contains("TESTROOM42"), "label '" + rl?.label + "'");
            yield return Tap("room:reveal"); SimRoomName = "OTHER7"; yield return Frames(3); rl = board.Buttons.Find(b => b.isLabel && b.label.StartsWith("Room:"));
            R("new room hides the code again", rl != null && !rl.label.Contains("OTHER7"), "label '" + rl?.label + "'");
            SimRoom = 0; yield return Frames(3); rl = board.Buttons.Find(b => b.isLabel && b.label.StartsWith("Room:"));
            R("offline shows 'not in a room'", rl != null && rl.label.Contains("offline"), "label '" + rl?.label + "'"); SimRoom = -1; yield return Frames(2);
            { int rpl; var realRoom = GTApi.RoomName(out rpl); R("real GT room read (NetworkSystem.RoomName)", realRoom == null, "test copy offline -> " + (realRoom ?? "null (not in a room)")); }
            yield return Tap("tm:toggle"); yield return new WaitForSecondsRealtime(2.2f); double ts1 = TimerSeconds; yield return Tap("tm:toggle"); double ts2 = TimerSeconds; yield return new WaitForSecondsRealtime(1f);
            R("session timer runs and pauses", ts1 > 1.8 && ts1 < 4 && Math.Abs(TimerSeconds - ts2) < .01, "ran " + ts1.ToString("F2") + " s, paused at " + ts2.ToString("F2") + " -> " + TimerSeconds.ToString("F2") + " (" + HMS(TimerSeconds) + ")");
            var lnow = LocalClock.Now(); var cl = board.Buttons.Find(b => b.isLabel && b.label.Contains(lnow.ToString("h:mm tt")) && b.label.Contains(LocalClock.Zone()));
            var dl = board.Buttons.Find(b => b.isLabel && b.label.Contains(lnow.ToString("dddd, MMMM d")));
            R("v2: real LOCAL clock + zone shown (Win32 local time, not UTC)", cl != null && dl != null && LocalClock.Zone() != "local", "label '" + (cl?.label ?? ClockNow()) + "' / '" + dl?.label + "', Unity DateTime.Now " + DateTime.Now.ToString("HH:mm") + ", UTC " + DateTime.UtcNow.ToString("HH:mm"));
            PageShot("18_tools", "Tools: session timer + real clock");
            yield return Tap("tm:reset"); R("timer reset", TimerSeconds == 0, HMS(TimerSeconds));
            yield return Tap("nav:home"); yield return Tap("go:settings"); yield return Tap("go:theme");
            foreach (var pn in Presets) { yield return Tap("th:" + pn); if (pn != "Classic") PageShot("20_theme_" + pn.ToLower(), "Colors: " + pn + " preset"); }
            R("all 6 presets apply", S.theme == "Mono" && board.Th.panel == PresetTheme("Mono").panel, "theme " + S.theme);
            yield return Tap("th:Classic"); yield return Tap("th:edit:accent"); int ah = S.accentH;
            for (int k = 0; k < 4; k++) yield return Tap("th:h+");
            R("custom accent hue steps (copied from preset first)", S.theme == "Custom" && S.accentH == (ah + 60) % 360, ah + " -> " + S.accentH);
            yield return Tap("th:edit:bg"); int bv = S.bgV; yield return Tap("th:v+"); yield return Tap("th:s-");
            R("custom background edits", S.bgV == Mathf.Min(100, bv + 5), "bright " + bv + " -> " + S.bgV + ", colour " + S.bgS);
            R("custom theme applied to the board", board.Th.panel == CurrentTheme().panel && board.Th.shell == CurrentTheme().shell, "panel " + board.Th.panel + " shell " + board.Th.shell);
            PageShot("21_theme_custom", "Colors: custom accent + background");
            S.bgV = 95; S.bgS = 5; ApplyCurrentTheme(); yield return Frames(2);
            R("light background flips text to dark (readable)", board.Th.text == Color.black, "text " + board.Th.text); PageShot("22_theme_light", "Colors: light custom background -> dark text");
            var json = JsonUtility.ToJson(S); var back = JsonUtility.FromJson<BoardSettings>(json);
            R("theme saved in settings.json format", back.theme == "Custom" && back.accentH == S.accentH && back.bgV == 95, json.Length + " chars, theme " + back.theme);
            yield return Tap("th:Classic"); yield return Tap("nav:home");

            // ---------- public room safety ----------
            yield return Tap("go:weather"); yield return Tap("wx:Rain"); yield return new WaitForSecondsRealtime(1f);
            SimRoom = 3; yield return Frames(5);
            R("public lobby: board hidden + visuals restored", !Visible && env.ActiveWeather == Env.Weather.Default && env.WeatherString() == "DEFAULT", "visible " + Visible + ", weather " + env.WeatherString());
            SimRoom = 1; yield return Frames(5); R("private room: board allowed", Visible, "visible " + Visible);
            SimRoom = 2; yield return Frames(5); R("modded room: board allowed", Visible, "visible " + Visible);
            SimRoom = -1; yield return Frames(3);

            // ---------- anchoring ----------
            ForceShow = false; tipBall.SetActive(false); SimTipOn = false; S.anchor = "Wrist"; Open = false; SimButton = false; S.openButton = "StickClick";
            var fw = cam.forward; fw.y = 0; fw.Normalize();
            SimHead = cam.position; SimOffHand = SimHead + fw * .32f + Vector3.down * .18f; yield return Frames(5);
            R("v1.0.1: raising wrist does NOT open board", !Visible, "visible " + Visible);
            SimButton = true; yield return Frames(2); SimButton = false; yield return Frames(3);
            float dHand = board?.Root != null ? Vector3.Distance(board.Root.transform.position, SimOffHand) : 99;
            R("stick click opens board at wrist", Visible && Open && dHand < .25f, "visible " + Visible + ", distance board-hand " + dHand.ToString("F2") + " m");
            Capture("15_wrist", "Wrist mode: board opened by left stick click");
            bool stable = true; for (int i = 0; i < 60; i++) { SimOffHand += Vector3.right * .004f * Mathf.Sin(i * .3f); yield return null; var bp = board.Root.transform.position; if (float.IsNaN(bp.x) || Vector3.Distance(bp, SimOffHand) > .3f) stable = false; }
            R("wrist: follows hand, no drift", stable, "60 frames of hand motion");
            SimOffHand = SimHead + Vector3.down * 1.0f; yield return Frames(3); R("lowering hand keeps board open (button-only)", Visible, "visible " + Visible);
            SimButton = true; yield return Frames(30); R("holding the button does not re-toggle", !Visible && !Open, "visible " + Visible); SimButton = false; yield return Frames(2);
            SimOffHand = SimHead + fw * .32f + Vector3.down * .18f; yield return Frames(3); R("stick click closes; stays closed with wrist raised", !Visible, "visible " + Visible);
            SimButton = true; yield return Frames(2); SimButton = false; yield return Frames(2); Press("close"); yield return Frames(3); R("Close button closes", !Visible && !Open, "visible " + Visible);
            S.openButton = "HoldStick"; SimButton = true; yield return Frames(3); bool early = Visible;
            float t0 = Time.unscaledTime; while (Time.unscaledTime - t0 < .6f) yield return null; bool late = Visible; SimButton = false; yield return Frames(2);
            R("hold mode: quick press ignored, 0.4 s hold opens", !early && late, "early " + early + ", after hold " + late);
            Open = false; S.openButton = "Menu"; Press("set:openbtn"); yield return Frames(2); R("settings button cycles open mode", S.openButton == "HoldStick", "mode " + S.openButton); S.openButton = "StickClick";
            S.anchor = "Float"; SimButton = true; yield return Frames(2); SimButton = false; yield return Frames(3); var fp = board.Root.transform.position;
            SimOffHand = SimHead + Vector3.down * 1.0f; yield return Frames(3);
            R("float: opens by button, stays put", Visible && Vector3.Distance(board.Root.transform.position, fp) < .001f, "moved " + Vector3.Distance(board.Root.transform.position, fp).ToString("F4") + " m");
            SimHead += fw * 3f; yield return Frames(3); R("float: walking away closes it (never chases you)", !Visible && !Open, "visible " + Visible);
            SimHead = cam.position; S.anchor = "Wrist"; Open = false;
            SimRoom = 1; Open = true; yield return Frames(2); SimRoom = 3; yield return Frames(3); R("public lobby closes an open board", !Open && !Visible, "open " + Open); SimRoom = -1; yield return Frames(2);


            // ---------- v2.0: room code length, perf page, LIVE (mock TikFinity), mic select, X button, palm, movable board, migration ----------
            Sim = true; ForceShow = true; ForcePos = cam.position + cam.forward * .30f; ForceRot = Quaternion.LookRotation(cam.forward, Vector3.up); S.anchor = "Wrist"; tipBall.SetActive(true); yield return Frames(3);
            Page = "home"; history.Clear(); yield return Tap("go:tools");
            SimRoom = 1; SimRoomName = "LONGROOMCODE1234"; yield return Tap("room:reveal"); var lr = board.Buttons.Find(b => b.isLabel && b.label.StartsWith("Room:"));
            R("v2: long room code shown in full (full-width row)", lr != null && lr.shown != null && lr.shown.Contains("LONGROOMCODE1234"), "shown '" + lr?.shown + "'");
            PageShot("30_room_long", "v2 Tools: full-width room code"); SimRoom = -1; yield return Frames(2);
            if (music.Connected)
            {
                int mc0 = (int)music.N("mic", "count"); string mn0 = music.S("mic", "name");
                if (mc0 > 1) { yield return Tap("mic:next"); yield return new WaitForSecondsRealtime(1.5f); R("v2: mic selection cycles to another mic (dry, not saved)", music.S("mic", "name") != mn0, "'" + mn0 + "' -> '" + music.S("mic", "name") + "' (" + mc0 + " mics)"); PageShot("31_mic_select", "v2 Tools: microphone picker"); }
                else R("v2: mic selection", mc0 >= 0, mc0 + " mic(s) - nothing to cycle");
            }
            yield return Tap("nav:home"); yield return Tap("go:perf"); yield return new WaitForSecondsRealtime(1.5f);
            var fl = board.Buttons.Find(b => b.isLabel && b.label.StartsWith("FPS "));
            R("v2: performance page shows live FPS/frame time", fl != null && Perf.Fps > 0, "label '" + fl?.label + "', fps " + Perf.Fps.ToString("F1") + ", cpu " + music.N("sys", "cpu"));
            PageShot("32_perf", "v2 Performance monitor");
            yield return Tap("nav:home"); yield return Tap("go:live"); yield return new WaitForSecondsRealtime(3f);
            if (music.Connected && music.B("live", "enabled"))
            {
                double lk = music.N("live", "likes"); int ac0 = AlertCount;
                var ll = board.Buttons.Find(b => b.isLabel && b.label.StartsWith("Likes "));
                R("v2: TikTok LIVE likes are live totals (mock TikFinity 106,450)", lk == 106450 && ll != null && ll.label.StartsWith("Likes 106"), "likes " + lk + ", label '" + ll?.label + "', viewers " + music.N("live", "viewers") + ", status " + music.S("live", "status"));
                var cl2 = music.Get("live", "chat") as List<object>; R("v2: LIVE chat + gift shown", cl2 != null && cl2.Count == 2 && (music.S("live", "lastGift") ?? "").Contains("Rose x5"), "chat " + (cl2?.Count ?? 0) + ", gift '" + music.S("live", "lastGift") + "'");
                PageShot("33_live", "v2 TikTok LIVE page (mock TikFinity events, no TikTok contact)");
                music.Send("live", "reconnect"); yield return new WaitForSecondsRealtime(7f);
                R("v2 Community: no private alert feature", AlertCount == ac0, "alerts " + (AlertCount - ac0));
            }
            else { R("v2: LIVE page without TikFinity shows setup help", board.Buttons.Exists(b => b.label.Contains("not set up") || b.label.Contains("not running")), "enabled " + music.B("live", "enabled")); PageShot("33_live_setup", "v2 TikTok LIVE page: not set up"); }
            yield return Tap("nav:home");

            // X button + saved floating spot out of your face
            ForceShow = false; tipBall.SetActive(false); SimTipOn = false; Open = false; Visible = false; yield return Frames(2);
            S = new BoardSettings { cfgVersion = 2 }; S.palmOpen = false; SimHead = cam.position; SimOffHand = SimHead + Vector3.down * 1f; SimPalmN = Vector3.zero;
            SimButton = true; yield return Frames(2); SimButton = false; yield return Frames(3);
            var bp0 = board.Root.transform.position; var toB = bp0 - SimHead; float ang = Vector3.Angle(cam.forward, toB);
            R("v2: X button opens the floating board", Visible && Open && S.openButton == "X", "visible " + Visible);
            R("v2: board opens beside/below your view, not in your face", ang > 25f && toB.y < -.12f && toB.magnitude > .4f, "angle from gaze " + ang.ToString("F0") + " deg, " + (-toB.y).ToString("F2") + " m below eyes, " + toB.magnitude.ToString("F2") + " m away");
            Capture("34_float_spot", "v2: X opens the board beside/below your view (not in your face)");
            SimButton = true; yield return Frames(2); SimButton = false; yield return Frames(2); R("v2: X again closes", !Visible && !Open, "visible " + Visible);
            // movable: grab the top bar, move 20 cm right + 10 cm up, release -> remembered
            SimButton = true; yield return Frames(2); SimButton = false; yield return Frames(2);
            var rt = board.Root.transform; var start = rt.position;
            SimGripHand = rt.TransformPoint(new Vector3(0, .15f, -.01f)); SimGrip = false; yield return Frames(2); SimGrip = true; yield return Frames(2);
            R("v2: grip on the top bar grabs the board", Grabbing, "grabbing " + Grabbing);
            var mv = (cam.right * .20f + Vector3.up * .10f); for (int i = 1; i <= 20; i++) { SimGripHand += mv / 20f; yield return null; }
            SimGrip = false; yield return Frames(3); var dropped = rt.position;
            R("v2: board follows the hand and stays where dropped", !Grabbing && Vector3.Distance(dropped, start + mv) < .02f && S.posSaved && Moves == 1, "moved " + Vector3.Distance(start, dropped).ToString("F3") + " m, err " + Vector3.Distance(dropped, start + mv).ToString("F3") + ", saved " + S.posSaved);
            SimButton = true; yield return Frames(2); SimButton = false; yield return Frames(2); SimButton = true; yield return Frames(2); SimButton = false; yield return Frames(3);
            R("v2: reopening uses the remembered spot", Visible && Vector3.Distance(rt.position, dropped) < .03f, "distance " + Vector3.Distance(rt.position, dropped).ToString("F3") + " m");
            Capture("35_moved", "v2: board moved by grabbing its top bar, reopened at the remembered spot");
            SimGripHand = rt.TransformPoint(new Vector3(0, -.10f, -.01f)); SimGrip = false; yield return Frames(2); SimGrip = true; yield return Frames(3);
            R("v2: gripping the button area does NOT grab (only the top bar)", !Grabbing, "grabbing " + Grabbing); SimGrip = false; yield return Frames(2);
            Press("close"); yield return Frames(2);
            // palm: palm toward face + looking at it for 0.5 s opens; short glance does not; disabled = never
            S.palmOpen = true; var hand = SimHead + cam.forward * .35f; SimOffHand = hand; SimPalmN = (SimHead - hand).normalized; palmArmed = true;
            float pt0 = Time.unscaledTime; while (Time.unscaledTime - pt0 < .2f) yield return null; bool earlyP = Open;
            SimPalmN = -SimPalmN; yield return new WaitForSecondsRealtime(.4f); SimPalmN = -SimPalmN;
            pt0 = Time.unscaledTime; while (Time.unscaledTime - pt0 < .7f) yield return null; bool lateP = Open;
            R("v2: palm gesture opens after 0.5 s (quick glance ignored)", !earlyP && lateP && Visible, "after 0.2 s " + earlyP + ", after 0.7 s " + lateP);
            Capture("36_palm", "v2: palm-open (simulated palm facing the eyes)");
            SimPalmN = -SimPalmN; yield return Frames(5); R("v2: turning the palm away keeps it open (no flicker)", Open, "open " + Open);
            Press("close"); SimPalmN = -SimPalmN; yield return new WaitForSecondsRealtime(.8f);
            R("v2: palm still held after Close does not reopen (must re-arm)", !Open, "open " + Open);
            SimPalmN = -SimPalmN; yield return new WaitForSecondsRealtime(.5f); S.palmOpen = false; SimPalmN = -SimPalmN; yield return new WaitForSecondsRealtime(.8f);
            R("v2: palm toggle OFF -> palm never opens", !Open, "open " + Open);
            SimPalmN = (SimHead - (SimHead + cam.forward * .35f)).normalized; S.palmOpen = true; var side = SimHead + (cam.forward + cam.right * 1.5f).normalized * .35f; SimOffHand = side; SimPalmN = (SimHead - side).normalized; palmArmed = true;
            yield return new WaitForSecondsRealtime(.9f); R("v2: palm toward face but NOT looked at -> stays closed", !Open, "open " + Open);
            S.palmOpen = false; SimPalmN = Vector3.zero; Open = false;
            var v1 = JsonUtility.FromJson<BoardSettings>("{\"theme\":\"Lava\",\"anchor\":\"Float\",\"angle\":-30,\"openButton\":\"StickClick\",\"scale\":0.7}");
            bool migOk = Migrate(v1); R("v2: v1 settings.json migrates (theme/size kept, X+palm, out-of-face spot)", migOk && v1.theme == "Lava" && Mathf.Approximately(v1.scale, .7f) && v1.openButton == "X" && v1.palmOpen && v1.angle == 0 && !v1.posSaved && v1.cfgVersion == 2, JsonUtility.ToJson(v1).Length + " chars");
            R("v2: edition identity", Edition.Id.Length > 0 && MusicLink.PipeName.StartsWith(Edition.Pipe), Edition.Name + " / pipe " + MusicLink.PipeName + " / data " + BoardSettings.Dir);
            S = new BoardSettings { cfgVersion = 2 }; Sim = true;

            // ---------- v2.0.1: keyboard toggle key (default F4) + coexistence ----------
            Sim = true; ForceShow = false; Open = false; S = new BoardSettings { cfgVersion = 2 }; S.palmOpen = false; S.anchor = "Wrist"; SimHead = cam.position; SimOffHand = SimHead + Vector3.down * 1f; yield return Frames(2);
            R("v2.0.1: default toggle key is F4", ToggleKeyCode == KeyCode.F4 && toggleKey.Value == "F4", "config Input.ToggleKey = " + toggleKey.Value);
            R("v2.0.1: key names parse (F6, insert, None = off, junk -> F4)", ParseKey("F6") == KeyCode.F6 && ParseKey("insert") == KeyCode.Insert && ParseKey("None") == KeyCode.None && ParseKey("nope!") == KeyCode.F4, "ok");
            SimKeyDown = KeyCode.F4; yield return Frames(3);
            R("v2.0.1: F4 opens the board (floats at the saved spot with no tracked hand)", Open && Visible && KeyOpened, "open " + Open + ", visible " + Visible);
            Capture("40_f4_open", "v2.0.1: F4 opened the board"); PageShot("40b_f4_page", "v2.0.1: board opened with F4");
            SimKeyDown = KeyCode.F4; yield return Frames(3); R("v2.0.1: F4 again closes it", !Open && !Visible, "open " + Open);
            SimRoom = 3; yield return Frames(2); SimKeyDown = KeyCode.F4; yield return Frames(3); R("v2.0.1: F4 ignored in a public lobby", !Open && !Visible, "open " + Open); SimRoom = -1; yield return Frames(2);
            var tk = ToggleKeyCode; ToggleKeyCode = KeyCode.None; SimKeyDown = KeyCode.F4; yield return Frames(3); R("v2.0.1: ToggleKey = None disables the key", !Open, "open " + Open); ToggleKeyCode = tk; SimKeyDown = KeyCode.None;
            R("v2.0.1: other utility board detection ran", true, OtherBoard ? "detected: " + OtherBoardName : "none loaded");
            bool ob = OtherBoard; OtherBoard = true; S.openButton = "X"; S.palmOpen = true; var hd = SimHead + cam.forward * .35f; SimOffHand = hd; SimPalmN = (SimHead - hd).normalized; palmArmed = true;
            SimButton = true; yield return Frames(2); SimButton = false; yield return new WaitForSecondsRealtime(.8f);
            R("v2.0.1: with another utility board loaded, X and palm do NOT open this board", !Open, "open " + Open);
            SimKeyDown = KeyCode.F4; yield return Frames(3); R("v2.0.1: with another utility board loaded, F4 still opens this board", Open && Visible, "open " + Open);
            Press("close"); yield return Frames(2); OtherBoard = ob; SimPalmN = Vector3.zero; S.palmOpen = false;
            if (!ob) { SimButton = true; yield return Frames(2); SimButton = false; yield return Frames(3); R("v2.0.1: alone, X still opens this board", Open, "open " + Open); Press("close"); yield return Frames(2); }
            // real keyboard: the test harness focuses the game window and presses F4 twice
            Sim = false; Open = false; yield return Frames(2); int kp0 = KeyPresses; Logger.LogInfo("SELFTEST WAITKEY F4");
            float kt0 = Time.realtimeSinceStartup; while (KeyPresses == kp0 && Time.realtimeSinceStartup - kt0 < 25) yield return null; bool realOpen = Open;
            yield return new WaitForSecondsRealtime(.5f); Logger.LogInfo("SELFTEST SHOT 41_real_f4"); yield return new WaitForSecondsRealtime(3f);
            float kt1 = Time.realtimeSinceStartup; while (KeyPresses < kp0 + 2 && Time.realtimeSinceStartup - kt1 < 25) yield return null;
            R("v2.0.1: REAL F4 key press (game window focused) opens, second press closes", KeyPresses >= kp0 + 2 && realOpen && !Open, "presses seen " + (KeyPresses - kp0) + ", opened " + realOpen + ", now open " + Open);
            { Sim = true; Open = false; SimKeyDown = KeyCode.F3; yield return Frames(3); R("v2.0.1: F3 does nothing (left free for other mods)", !Open, "open " + Open); SimKeyDown = KeyCode.None; }
            Sim = true; Open = false; S = new BoardSettings { cfgVersion = 2 };
            Sim = false; ForceShow = false; EnvFollow = null; env.RestoreAll("self-test end");
            Logger.LogInfo("SELFTEST FOG_VISIBLE " + FogVisible);
            File.WriteAllLines(Path.Combine(outDir, "summary.txt"), summary.ToArray());
            Logger.LogInfo("SELFTEST DONE pass=" + pass + " fail=" + fail);
        }
        public bool FogVisible;
    }
}
