using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace UtilityBoard
{
    [Serializable]
    public class BoardSettings
    {
        public int cfgVersion;                    // 0 = settings.json from v1.x (migrated once to 2)
        public string theme = "Classic";          // Classic | Midnight | Ice | Lava | Mint | Mono | Custom
        public int accentH = 280, accentS = 80, accentV = 100, bgH = 270, bgS = 45, bgV = 11;   // Custom theme (HSV)
        public float scale = .6f;                 // 0.4 - 1.0 of the 36 x 23 cm design size
        public string anchor = "Float";           // Wrist | Float (Float = remembered spot, out of your face)
        public bool rightHanded = true;           // board on LEFT hand side, poke with RIGHT index finger
        public int angle = 0;                     // tilt in degrees, -30..30
        public bool clickSound = true, haptics = true;
        public int weatherIntensity = 60;         // 0-100
        public bool lightning = false;            // thunderstorm lightning flash (off by default: VR comfort)
        public string openButton = "X";           // X | StickClick | Menu | HoldStick  (off-hand controller)
        public bool palmOpen = true;              // look at your off-hand palm to open
        public string palmAxis = "";              // hand-local palm normal (+x,-x,+y,-y,+z,-z); "" = default guess until calibrated
        // remembered floating spot, relative to where you look when it opens: metres ahead, degrees left(-)/right(+), metres below eyes
        public float posDist = .50f, posYaw = -30f, posDown = .22f; public bool posSaved;
        public bool alerts = true;                // LIVE gift/follow alerts on the board
        public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Edition.DataFolder);
        public static string FilePath => Path.Combine(Dir, "settings.json");
    }

    // CLIENT-SIDE ONLY. Nothing here is networked. The board only appears offline, in private rooms and in modded
    // rooms; in public lobbies it hides and every time/weather change is restored.
    [BepInPlugin(Edition.Guid, Edition.Name, Edition.Version)]
    public partial class Plugin : BaseUnityPlugin
    {
        public const string Version = Edition.Version;
        public BoardSettings S = new BoardSettings();
        private ConfigEntry<bool> selfTest; private ConfigEntry<string> toggleKey; public KeyCode ToggleKeyCode = KeyCode.F4; public bool OtherBoard; private ConfigEntry<string> testOutDir, timeMap, verifiedPresets;
        public Transform EnvFollow; public Board board; public MusicLink music = new MusicLink(); public Env env;
        public string Page = "home"; private readonly Stack<string> history = new Stack<string>();
        public string MusicTab = "local"; public int QueueOffset; public string Toast = ""; private float toastUntil;
        private Transform cam; private AudioSource click;

        private void Awake()
        {
            selfTest = Config.Bind("Debug", "SelfTest", false, "Developer desktop self-test. Leave off.");
            testOutDir = Config.Bind("Debug", "SelfTestOutDir", "", "Developer only.");
            var pipeOv = Config.Bind("Debug", "PipeOverride", "", "Developer isolation only. Leave empty.");
            toggleKey = Config.Bind("Input", "ToggleKey", "F4", "Keyboard key that opens/closes the board while the Gorilla Tag window is focused (Unity KeyCode name, e.g. F4, F6, Insert). None = off.");
            ToggleKeyCode = ParseKey(toggleKey.Value);
            if (!string.IsNullOrEmpty(pipeOv.Value) && pipeOv.Value.StartsWith(Edition.Pipe + "_")) MusicLink.PipeName = pipeOv.Value;
            timeMap = Config.Bind("Time", "PresetLightmapIndices", "auto", "Sunrise,Morning,Daytime,Sunset,Nighttime lightmap indices, or auto.");
            verifiedPresets = Config.Bind("Time", "EnabledPresets", "0,1,2,3,4", "Presets that passed verification on this game build (comma list of indices 0-4). Edit only if you know a preset works.");
            env = new Env(s => Logger.LogInfo(s)) { Override = timeMap.Value };
            foreach (var p in (verifiedPresets.Value ?? "").Split(',')) { int i; if (int.TryParse(p.Trim(), out i) && i >= 0 && i < 5) env.PresetEnabled[i] = true; }
            if (selfTest.Value) NoSave = true;   // the desktop self-test never writes your settings.json (not even the v2 migration)
            LoadSettings();
            music.Start();
            Logger.LogInfo(Edition.Name + " " + Version + " (" + Edition.Id + " edition) loaded (client-side; offline/private/modded rooms only). Open: " + ButtonLabel(S.openButton) + (S.palmOpen ? " or look at your palm" : "") + ". Keyboard: " + (ToggleKeyCode == KeyCode.None ? "off" : ToggleKeyCode + " opens/closes") + ", Home key = home page, 1-9 press buttons. Companion pipe: " + MusicLink.PipeName);
            Logger.LogInfo("Clock check: Unity DateTime.Now " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " | Win32 local " + LocalClock.Now().ToString("yyyy-MM-dd HH:mm") + " " + LocalClock.Zone() + " | UTC " + DateTime.UtcNow.ToString("HH:mm"));
            if (selfTest.Value) StartCoroutine(SelfTest());
            StartCoroutine(DetectOtherBoard());
        }
        private void OnDestroy() { music.Stop(); env?.RestoreAll("unload"); }
        private void OnApplicationQuit() { music.Stop(); }

        public static readonly string[] OpenModes = { "X", "StickClick", "Menu", "HoldStick" };
        public void LoadSettings()
        {
            try { if (File.Exists(BoardSettings.FilePath)) S = JsonUtility.FromJson<BoardSettings>(File.ReadAllText(BoardSettings.FilePath)) ?? new BoardSettings(); }
            catch (Exception e) { Logger.LogWarning("settings.json unreadable, using defaults: " + e.Message); S = new BoardSettings(); }
            if (Migrate(S) && File.Exists(BoardSettings.FilePath)) { Logger.LogInfo("settings.json migrated to v2 (open = X button / palm, board moved out of your face; theme kept)"); SaveSettings(); }
            S.scale = Mathf.Clamp(S.scale, .4f, 1f); S.angle = Mathf.Clamp(S.angle, -30, 30); S.weatherIntensity = Mathf.Clamp(S.weatherIntensity, 0, 100);
            if (S.anchor != "Wrist") S.anchor = "Float"; if (Array.IndexOf(OpenModes, S.openButton) < 0) S.openButton = "X"; if (S.theme != "Custom" && Array.IndexOf(Presets, S.theme) < 0) S.theme = "Classic"; S.accentH = ((S.accentH % 360) + 360) % 360; S.bgH = ((S.bgH % 360) + 360) % 360; S.accentS = Mathf.Clamp(S.accentS, 0, 100); S.bgS = Mathf.Clamp(S.bgS, 0, 100); S.accentV = Mathf.Clamp(S.accentV, 10, 100); S.bgV = Mathf.Clamp(S.bgV, 0, 100);
            S.posDist = Mathf.Clamp(S.posDist, .25f, 1.2f); S.posYaw = Mathf.Clamp(S.posYaw, -90f, 90f); S.posDown = Mathf.Clamp(S.posDown, -.4f, .7f);
            if (Array.IndexOf(PalmAxes, S.palmAxis) < 0) S.palmAxis = "";
            env.Intensity = S.weatherIntensity / 100f; env.Lightning = S.lightning;
        }
        // v1.x -> v2: X button + palm, floating board beside/below your view instead of straight in front of your face
        public static bool Migrate(BoardSettings x)
        {
            if (x.cfgVersion >= 2) return false;
            x.cfgVersion = 2; x.openButton = "X"; x.palmOpen = true; x.anchor = "Float"; x.angle = 0; x.posSaved = false; x.posDist = .50f; x.posYaw = -30f; x.posDown = .22f; x.alerts = true;
            return true;
        }
        public bool NoSave;
        public void SaveSettings()
        {
            if (NoSave) return;
            try { Directory.CreateDirectory(BoardSettings.Dir); var tmp = BoardSettings.FilePath + ".tmp"; File.WriteAllText(tmp, JsonUtility.ToJson(S, true)); if (File.Exists(BoardSettings.FilePath)) File.Delete(BoardSettings.FilePath); File.Move(tmp, BoardSettings.FilePath); }
            catch (Exception e) { Logger.LogWarning("could not save settings.json: " + e.Message); }
        }

        // ---------- rooms ----------
        public int SimRoom = -1; public string RoomNote = "offline";
        public bool Allowed()
        {
            bool inRoom, priv; string gm;
            if (SimRoom >= 0) { inRoom = SimRoom > 0; priv = SimRoom == 1; gm = SimRoom == 2 ? "MODDED_DEFAULTINFECTION" : "INFECTION"; }
            else inRoom = GTApi.InRoom(out priv, out gm);
            bool modded = gm != null && gm.IndexOf("MODDED", StringComparison.OrdinalIgnoreCase) >= 0;
            RoomNote = !inRoom ? "offline" : priv ? "private room" : modded ? "modded room" : "public room";
            return !inRoom || priv || modded;
        }

        // ---------- player input (real, or simulated by the desktop self-test) ----------
        public bool Sim; public Vector3 SimHead, SimOffHand, SimTip; public bool SimTipOn;
        public Vector3 Head() { if (Sim) return SimHead; if (cam == null && Camera.main != null) cam = Camera.main.transform; return cam != null ? cam.position : Vector3.zero; }
        public Vector3 HeadForward() { if (cam == null && Camera.main != null) cam = Camera.main.transform; return cam != null ? cam.forward : Vector3.forward; }
        private bool OffHand(out Vector3 p) { if (Sim) { p = SimOffHand; return true; } var h = GTApi.Hand(S.rightHanded); p = h != null ? h.position : Vector3.zero; return h != null; }

        // ---------- show / anchor ----------
        public bool Visible; public bool ForceShow; public Vector3 ForcePos; public Quaternion ForceRot;
        private bool floatPlaced; private Vector3 floatPos; private Quaternion floatRot;
        private bool lastAllowed = true;
        private void Update()
        {
            bool allowed = Allowed();
            if (!allowed && lastAllowed) { Open = false; grabbing = false; env.RestoreAll("joined a " + RoomNote); if (board?.Root != null) board.Root.SetActive(false); Visible = false; Logger.LogInfo("Board hidden: " + RoomNote + " (offline / private / modded rooms only)"); }
            lastAllowed = allowed;
            Perf.Tick();
            DebugKeys(allowed);
            if (allowed) env.Tick(EnvFollow != null ? EnvFollow : cam);
            music.PollSeconds = Visible ? 1f : 5f;
            if (!allowed) return;
            LiveAlerts();
            Place();
            if (!Visible) return;
            Render();
            Vector3 tip; bool have = Tip(out tip);
            var id = grabbing ? null : board.Poke(tip, have);
            if (id != null) Press(id);
        }
        // ---------- open / close: X button (or stick/menu), palm gesture, Close/Hide buttons ----------
        public bool Open, KeyOpened; public int KeyPresses; public bool SimButton; private bool btnWas; private float btnDownAt; private bool holdFired;
        public static string ButtonLabel(string b) => b == "Menu" ? "Menu button" : b == "HoldStick" ? "Hold stick 0.4s" : b == "StickClick" ? "Stick click" : "X button";
        private bool ButtonDown()
        {
            if (Sim) return SimButton;
            bool offLeft = S.rightHanded; bool v = false;
            if (S.openButton == "X")
            {   // X = primary button of the off-hand (left) controller; GT's own poller first, then the XR device
                if (GTApi.Poller != null && GTApi.HasPollField(offLeft ? "leftControllerPrimaryButton" : "rightControllerPrimaryButton")) return GTApi.PollField<bool>(offLeft ? "leftControllerPrimaryButton" : "rightControllerPrimaryButton");
                var dx = GTApi.PollField<UnityEngine.XR.InputDevice>(offLeft ? "leftControllerDevice" : "rightControllerDevice");
                if (dx.isValid) try { dx.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out v); } catch (Exception) { }
                return v;
            }
            var d = GTApi.PollField<UnityEngine.XR.InputDevice>(offLeft ? "leftControllerDevice" : "rightControllerDevice");
            if (!d.isValid) return false;
            try { d.TryGetFeatureValue(S.openButton == "Menu" ? UnityEngine.XR.CommonUsages.menuButton : UnityEngine.XR.CommonUsages.primary2DAxisClick, out v); } catch (Exception) { }
            return v;
        }
        private void PollToggle()
        {
            bool down = ControllerOpenAllowed && ButtonDown(); bool fire = false;
            if (S.openButton == "HoldStick")
            {
                if (down && !btnWas) { btnDownAt = Time.unscaledTime; holdFired = false; }
                if (down && !holdFired && Time.unscaledTime - btnDownAt >= .4f) { holdFired = true; fire = true; }
            }
            else if (down && !btnWas) fire = true;
            btnWas = down;
            if (fire) SetOpen(!Open, ButtonLabel(S.openButton));
            PollPalm();
        }
        public void SetOpen(bool o, string why)
        {
            Open = o; KeyOpened = false; floatPlaced = false; grabbing = false; palmArmed = false; palmHeld = 0;
            if (S.haptics && !Sim) GTApi.Haptic(S.rightHanded, .25f, .03f);
            Logger.LogInfo("Board " + (Open ? "opened" : "closed") + " (" + why + ")");
        }

        // ---------- palm gesture: turn the off-hand palm toward your face and look at it for 0.5 s -> opens ----------
        public static readonly string[] PalmAxes = { "+x", "-x", "+y", "-y", "+z", "-z" };
        public Vector3 SimPalmN; public float PalmHold = .5f; private float palmHeld; private bool palmArmed = true; private float palmOffSince;
        public string PalmNote = "";
        private Vector3 Axis(Transform t, string a)
        {
            switch (a) { case "-x": return -t.right; case "+y": return t.up; case "-y": return -t.up; case "+z": return t.forward; case "-z": return -t.forward; default: return t.right; }
        }
        // GT hands: guessed palm normal until the user runs Settings > Calibrate palm in the headset
        private string PalmAxisNow => S.palmAxis != "" ? S.palmAxis : (S.rightHanded ? "+x" : "-x");
        public bool PalmFacing(out float facing, out float gaze)
        {
            facing = gaze = -1; Vector3 hand, n; var head = Head();
            if (Sim) { hand = SimOffHand; n = SimPalmN; }
            else { var t = GTApi.Hand(S.rightHanded); if (t == null) return false; hand = t.position; n = Axis(t, PalmAxisNow); }
            var toHead = head - hand; float dist = toHead.magnitude; if (dist < .05f || dist > .8f) return false;
            facing = Vector3.Dot(n.normalized, toHead / dist); gaze = Vector3.Dot(HeadForward(), -toHead / dist);
            return facing > .70f && gaze > .80f;
        }
        private void PollPalm()
        {
            if (!S.palmOpen || ForceShow || !ControllerOpenAllowed) return;
            float f, g; bool on = PalmFacing(out f, out g);
            if (!on) { if (palmOffSince < 0) palmOffSince = Time.unscaledTime; if (Time.unscaledTime - palmOffSince > .3f) palmArmed = true; palmHeld = 0; return; }
            palmOffSince = -1;
            if (Open || !palmArmed) return;   // palm only OPENS; X / Close / Hide close it. Re-arms after the palm turns away.
            palmHeld += Time.unscaledDeltaTime;
            if (palmHeld >= PalmHold) { SetOpen(true, "palm gesture, facing " + f.ToString("F2") + " gaze " + g.ToString("F2")); palmArmed = false; }
        }
        // calibration: user holds the palm toward the face; pick the hand axis that points at the head
        private float calibUntil = -1;
        public void StartPalmCalibration() { calibUntil = Time.unscaledTime + 3f; Say("Calibrating: hold your LEFT palm toward your face (3 s)"); }
        public string CalibratePalmNow(Transform t, Vector3 head)
        {
            string best = ""; float bd = -2; var toHead = (head - t.position).normalized;
            foreach (var a in PalmAxes) { float d = Vector3.Dot(Axis(t, a), toHead); if (d > bd) { bd = d; best = a; } }
            S.palmAxis = best; SaveSettings(); return best + " (" + bd.ToString("F2") + ")";
        }
        private void TickCalibration()
        {
            if (calibUntil < 0 || Time.unscaledTime < calibUntil) return; calibUntil = -1;
            var t = GTApi.Hand(S.rightHanded); if (t == null) { Say("Calibration failed: hand not found"); return; }
            var r = CalibratePalmNow(t, Head()); Say("Palm calibrated: " + r); palmArmed = false;
        }

        // ---------- movable panel: grab the board's top bar with your poking hand's grip, move it, let go -> remembered ----------
        public bool SimGrip; public Vector3 SimGripHand; private bool grabbing, gripWas; private Vector3 grabOffset; public int Moves;
        public bool Grabbing => grabbing;
        private bool GripHand(out Vector3 p, out bool grip)
        {
            if (Sim) { p = SimGripHand; grip = SimGrip; return true; }
            bool pokeLeft = !S.rightHanded; var h = GTApi.Hand(pokeLeft); p = h != null ? h.position : Vector3.zero;
            grip = GTApi.PollField<float>(pokeLeft ? "leftControllerGripFloat" : "rightControllerGripFloat") > .7f; return h != null;
        }
        private bool OnHandle(Vector3 w)
        {
            if (board?.Root == null) return false; float s = Mathf.Max(.05f, board.Root.transform.lossyScale.x); var lp = board.Root.transform.InverseTransformPoint(w);
            return Mathf.Abs(lp.x) < Board.W / 2 + .03f / s && lp.y > .125f && lp.y < Board.H / 2 + .04f / s && Mathf.Abs(lp.z) < .07f / s;   // title bar + 3-4 cm margin
        }
        private void PollGrab(Vector3 head)
        {
            Vector3 hp; bool grip; if (!GripHand(out hp, out grip)) { grabbing = false; return; }
            bool down = grip && !gripWas; gripWas = grip;
            if (!grabbing && down && OnHandle(hp)) { grabbing = true; grabOffset = board.Root.transform.position - hp; if (S.haptics && !Sim) GTApi.Haptic(!S.rightHanded, .3f, .04f); Logger.LogInfo("Board grabbed (move it, release grip to drop)"); }
            if (!grabbing) return;
            if (grip)
            {
                var pos = hp + grabOffset; var look = pos - head; if (look.sqrMagnitude < .0001f) look = Vector3.forward;
                floatPos = pos; floatRot = Quaternion.LookRotation(look, Vector3.up) * Quaternion.Euler(S.angle, 0, 0); floatPlaced = true;
                if (S.anchor != "Float") S.anchor = "Float";
                return;
            }
            grabbing = false; Moves++;
            // remember the spot relative to your view (so it opens there again, wherever you stand)
            var f = HeadForward(); f.y = 0; if (f.sqrMagnitude < .01f) f = Vector3.forward; f.Normalize();
            var d = floatPos - head; var flat = new Vector3(d.x, 0, d.z);
            S.posDist = Mathf.Clamp(flat.magnitude, .25f, 1.2f); S.posYaw = Mathf.Clamp(Vector3.SignedAngle(f, flat.sqrMagnitude > .0001f ? flat.normalized : f, Vector3.up), -90f, 90f); S.posDown = Mathf.Clamp(-d.y, -.4f, .7f); S.posSaved = true;
            SaveSettings(); if (S.haptics && !Sim) GTApi.Haptic(!S.rightHanded, .2f, .03f);
            Logger.LogInfo("Board dropped: remembered " + S.posDist.ToString("F2") + " m ahead, " + S.posYaw.ToString("F0") + " deg, " + S.posDown.ToString("F2") + " m below eyes");
        }
        public Vector3 SavedSpot(Vector3 head, out Quaternion rot)
        {
            var f = HeadForward(); f.y = 0; if (f.sqrMagnitude < .01f) f = Vector3.forward; f.Normalize();
            var dir = Quaternion.Euler(0, S.posYaw, 0) * f; var pos = head + dir * S.posDist + Vector3.down * S.posDown;
            rot = Quaternion.LookRotation(pos - head, Vector3.up) * Quaternion.Euler(S.angle, 0, 0); return pos;
        }

        private void Place()
        {
            if (cam == null && Camera.main != null) cam = Camera.main.transform;
            PollToggle(); TickCalibration();
            Vector3 head = Head(); Vector3 hand; bool haveHand = OffHand(out hand);
            if (Open && !grabbing && S.anchor == "Float" && floatPlaced && Vector3.Distance(floatPos, head) > 2.5f) { Open = false; floatPlaced = false; Logger.LogInfo("Board closed: walked away from floating board"); }   // never chase the player
            bool floatNow = S.anchor == "Float" || (KeyOpened && !haveHand);   // key-opened without a tracked hand: float at the saved spot
            bool show = ForceShow || (Open && (floatNow || haveHand || grabbing));
            if (!show) { if (board?.Root != null) board.Root.SetActive(false); Visible = false; grabbing = false; return; }
            if (board == null || board.Root == null) { board = new Board(); board.Th = CurrentTheme(); board.Build(); BuildClick(); Logger.LogInfo("Board built (" + board.ShaderNote + ")"); }
            board.Root.SetActive(true); Visible = true; board.Root.transform.localScale = Vector3.one * S.scale;
            Quaternion tilt = Quaternion.Euler(S.angle, 0, 0);
            if (ForceShow && !grabbing) { board.Root.transform.SetPositionAndRotation(ForcePos, ForceRot); PollGrab(head); if (grabbing) { ForcePos = board.Root.transform.position; } return; }
            PollGrab(head);
            if (floatNow || grabbing)
            {
                if (!floatPlaced) { floatPos = SavedSpot(head, out floatRot); floatPlaced = true; }
                board.Root.transform.SetPositionAndRotation(floatPos, floatRot);
                if (ForceShow) { ForcePos = floatPos; ForceRot = floatRot; }
                return;
            }
            // wrist: hover just above/in front of the off-hand wrist, facing the eyes; offset keeps the hand from clipping into it
            var toCam = (head - hand); toCam.y *= .5f; toCam = toCam.sqrMagnitude > .0001f ? toCam.normalized : Vector3.back;
            var pos = hand + toCam * .07f + Vector3.up * (Board.H * S.scale * .5f - .02f);
            board.Root.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-toCam, Vector3.up) * tilt);
        }
        // GT's own index-fingertip presser collider (same lookup the training dummy menu uses), falling back to 9 cm along the hand
        private readonly Transform[] tipT = new Transform[2]; private readonly Transform[] tipFallback = new Transform[2]; private float tipSearch; public string TipNote = "";
        private bool Tip(out Vector3 p)
        {
            if (Sim) { p = SimTip; return SimTipOn; }
            bool left = !S.rightHanded; int i = left ? 0 : 1;
            if (tipT[i] == null && Time.unscaledTime > tipSearch)
            {
                tipSearch = Time.unscaledTime + 3f; var ty = GTApi.GameType("GorillaTriggerColliderHandIndicator");
                if (ty != null) foreach (var o in Resources.FindObjectsOfTypeAll(ty))
                    {
                        var c = o as Component; if (c == null || !c.gameObject.scene.IsValid()) continue;
                        var f = ty.GetField("isLeftHand", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); bool isL = f != null && (bool)f.GetValue(c);
                        if (isL == left && c.gameObject.activeInHierarchy) { tipT[i] = c.transform; TipNote = "fingertip: " + ty.Name + " '" + c.name + "'"; break; }
                    }
            }
            if (tipT[i] != null) { p = tipT[i].position; return true; }
            var h = GTApi.Hand(left); if (h == null) { p = Vector3.zero; return false; }
            TipNote = "fingertip fallback: 9 cm forward of the hand"; p = h.position + h.forward * .09f; return true;
        }
        private void BuildClick()
        {
            click = board.Root.AddComponent<AudioSource>(); click.playOnAwake = false; click.spatialBlend = .6f; click.volume = .25f; click.minDistance = .3f;
            int n = 1323; var d = new float[n]; for (int i = 0; i < n; i++) { float t = i / 44100f; d[i] = Mathf.Sin(2 * Mathf.PI * 1800f * t) * Mathf.Exp(-t * 160f) * .6f; }
            var c = AudioClip.Create("ub_click", n, 1, 44100, false); c.SetData(d, 0); click.clip = c;
        }
        public void Feedback()
        {
            if (S.clickSound && click != null) click.Play();
            if (S.haptics && !Sim) GTApi.Haptic(!S.rightHanded, .35f, .04f);
        }
        public void Say(string s, float secs = 3f) { Toast = s; toastUntil = Time.unscaledTime + secs; Logger.LogInfo("Board: " + s); }

        // ---------- desktop debug ----------
        public static KeyCode ParseKey(string s)
        {
            s = (s ?? "").Trim(); if (s == "" || s.Equals("None", StringComparison.OrdinalIgnoreCase)) return KeyCode.None;
            try { return (KeyCode)Enum.Parse(typeof(KeyCode), s, true); } catch (Exception) { return KeyCode.F4; }
        }
        // ---------- coexistence: another utility board can be installed next to this one ----------
        // Two boards loaded: this board answers ONLY its keyboard key (X / palm stay with the other board,
        // so one press never opens two boards). Without another board: X + palm + key, as usual.
        public string OtherBoardName = "";
        // any OTHER loaded plugin that is a utility board (same board code, or a name containing "Utility Board")
        private System.Collections.IEnumerator DetectOtherBoard()
        {
            yield return null;
            try
            {
                foreach (var kv in BepInEx.Bootstrap.Chainloader.PluginInfos)
                {
                    if (kv.Key == Edition.Guid) continue; var pi = kv.Value; string tn = pi?.Instance != null ? pi.Instance.GetType().FullName : "";
                    if (tn == typeof(Plugin).FullName || (pi?.Metadata?.Name ?? "").IndexOf("Utility Board", StringComparison.OrdinalIgnoreCase) >= 0) { OtherBoard = true; OtherBoardName = pi.Metadata.Name + " " + pi.Metadata.Version; break; }
                }
            }
            catch (Exception) { }
            if (OtherBoard) Logger.LogInfo("Other utility board detected (" + OtherBoardName + "): X button and palm open are left to it; " + ToggleKeyCode + " opens this board.");
            else Logger.LogInfo("No other utility board loaded: X button, palm and " + ToggleKeyCode + " open this board.");
        }
        public bool ControllerOpenAllowed => !OtherBoard;
        public KeyCode SimKeyDown = KeyCode.None;   // desktop self-test: one simulated key press
        private bool legacy = true;
        private bool Key(KeyCode k)
        {
            if (k == KeyCode.None) return false;
            if (Sim && SimKeyDown == k) { SimKeyDown = KeyCode.None; return true; }
            if (legacy) { try { return Input.GetKeyDown(k); } catch (Exception) { legacy = false; } }
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current; if (kb == null) return false;
                string n = k.ToString(); if (n.StartsWith("Alpha")) n = "Digit" + n.Substring(5);
                try { var ik = (UnityEngine.InputSystem.Key)Enum.Parse(typeof(UnityEngine.InputSystem.Key), n, true); var kc = kb[ik]; if (kc != null) return kc.wasPressedThisFrame; } catch (Exception) { }
                switch (k)
                {
                    case KeyCode.F3: return kb.f3Key.wasPressedThisFrame; case KeyCode.F4: return kb.f4Key.wasPressedThisFrame; case KeyCode.Home: return kb.homeKey.wasPressedThisFrame;
                    case KeyCode.Alpha1: return kb.digit1Key.wasPressedThisFrame; case KeyCode.Alpha2: return kb.digit2Key.wasPressedThisFrame; case KeyCode.Alpha3: return kb.digit3Key.wasPressedThisFrame;
                    case KeyCode.Alpha4: return kb.digit4Key.wasPressedThisFrame; case KeyCode.Alpha5: return kb.digit5Key.wasPressedThisFrame; case KeyCode.Alpha6: return kb.digit6Key.wasPressedThisFrame;
                    case KeyCode.Alpha7: return kb.digit7Key.wasPressedThisFrame; case KeyCode.Alpha8: return kb.digit8Key.wasPressedThisFrame; case KeyCode.Alpha9: return kb.digit9Key.wasPressedThisFrame;
                }
            }
            catch (Exception) { }
            return false;
        }
        private bool debugShow;
        private void DebugKeys(bool allowed)
        {
            if (Key(ToggleKeyCode)) { if (allowed) { SetOpen(!Open, ToggleKeyCode + " key"); KeyOpened = Open; KeyPresses++; } else Logger.LogInfo(ToggleKeyCode + " ignored: " + RoomNote + " (board works offline / private / modded rooms only)"); }
            if (debugShow && !Sim)
            {
                if (cam == null && Camera.main != null) cam = Camera.main.transform;
                if (cam != null) { ForceShow = allowed; ForcePos = cam.position + cam.forward * .38f; ForceRot = Quaternion.LookRotation(cam.forward, Vector3.up); }
            }
            else if (!Sim && !debugShowWas) { }
            if (!debugShow && debugShowWas && !Sim) ForceShow = false;
            debugShowWas = debugShow;
            if (!Visible || board == null) return;
            if (Key(KeyCode.Home) && ToggleKeyCode != KeyCode.Home) Press("nav:home");   // desktop: Home key = home page (F4 is the open key)
            var press = new List<Board.Btn>(); foreach (var b in board.Buttons) if (b.go != null && b.enabled && !b.id.StartsWith("nav:")) press.Add(b);
            for (int i = 0; i < 9; i++) if (Key(KeyCode.Alpha1 + i) && i < press.Count) { press[i].flashUntil = Time.unscaledTime + .18f; Press(press[i].id); }
        }
        private bool debugShowWas;
    }
}
