using System;
using System.Collections.Generic;
using UnityEngine;
using static UtilityBoard.Board;

namespace UtilityBoard
{
    // Room code card, session timer + real local clock, Windows-wide mic mute with mic selection (via companion), colour/theme picker.
    public partial class Plugin
    {
        // ---------- room code (read locally from GT's NetworkSystem; hidden until tapped) ----------
        public string SimRoomName; private bool roomReveal; private float roomRevealUntil; private string roomSeen = "";
        public const float RevealSeconds = 15f;
        public string RoomCodeNow(out int players) { players = 0; if (SimRoom >= 0) { players = SimRoom > 0 ? 3 : 0; return SimRoom > 0 ? SimRoomName : null; } return GTApi.RoomName(out players); }
        private string roomLogged = "-"; public bool RoomRevealed => roomReveal && Time.unscaledTime < roomRevealUntil;
        private void RoomRows(List<List<Btn>> rows)
        {
            int pl; var code = RoomCodeNow(out pl);
            if ((code ?? "") != roomSeen) { roomSeen = code ?? ""; roomReveal = false; }   // new room: hide again
            if ((code ?? "") != roomLogged) { roomLogged = code ?? ""; Logger.LogInfo("Room now: " + (code == null ? "offline" : "code length " + code.Length + " via " + GTApi.LastRoomSource)); }
            if (string.IsNullOrEmpty(code)) { rows.Add(new List<Btn> { Lbl("Room: offline (not in a room)") }); return; }
            bool show = RoomRevealed;
            rows.Add(new List<Btn> { Lbl("Room: " + (show ? code : new string('\u2022', Mathf.Clamp(code.Length, 4, 10))), show ? Color.white : (Color?)null) });   // full width: long codes fit
            rows.Add(new List<Btn> { B("room:reveal", show ? "Hide (" + Mathf.CeilToInt(roomRevealUntil - Time.unscaledTime) + "s)" : "Show code", true, show), Lbl(pl + " players \u00B7 " + RoomNote) });
        }

        // ---------- session timer + clock ----------
        private bool timerRun; private double timerAcc; private float timerStart;
        public double TimerSeconds => timerAcc + (timerRun ? Time.unscaledTime - timerStart : 0);
        public static string HMS(double s) { if (s < 0) s = 0; var t = (long)s; return (t / 3600) + ":" + (t / 60 % 60).ToString("00") + ":" + (t % 60).ToString("00"); }
        public static string ClockNow() { var n = LocalClock.Now(); return n.ToString("h:mm tt") + " " + LocalClock.Zone() + " \u00B7 " + n.ToString("ddd MMM d"); }

        // ---------- mic ----------
        public bool MicAvailable => music.Connected && music.B("mic", "available");
        public bool MicMuted => music.B("mic", "muted");

        private void ToolsPage(List<List<Btn>> rows, Color warn)
        {
            RoomRows(rows);
            var now = LocalClock.Now();
            rows.Add(new List<Btn> { Lbl("Session " + HMS(TimerSeconds), Color.white), Lbl(now.ToString("h:mm tt") + " " + LocalClock.Zone(), Color.white) });
            rows.Add(new List<Btn> { Lbl(now.ToString("dddd, MMMM d")) });
            rows.Add(new List<Btn> { B("tm:toggle", timerRun ? "Pause" : (TimerSeconds > 0 ? "Resume" : "Start timer"), true, timerRun), B("tm:reset", "Reset", TimerSeconds > 0) });
            if (!music.Connected) { rows.Add(new List<Btn> { B("mic:toggle", "Mic: companion off", false) }); rows.Add(new List<Btn> { Lbl("Start the companion for mic mute", warn) }); }
            else if (!MicAvailable) { rows.Add(new List<Btn> { B("mic:toggle", "Mic: not found", false), B("mic:next", "Next mic >", music.N("mic", "count") > 0) }); rows.Add(new List<Btn> { Lbl(music.S("mic", "error") ?? "no microphone found", warn) }); }
            else
            {
                rows.Add(new List<Btn> { B("mic:toggle", MicMuted ? "MIC MUTED - tap to unmute" : "MIC LIVE - tap to mute", true, MicMuted) });
                rows.Add(new List<Btn> { B("mic:next", "Mic: " + (music.S("mic", "name") ?? "-") + (music.B("mic", "isDefault") ? " (default)" : ""), music.N("mic", "count") > 1) });
            }
            if (Time.unscaledTime < toastUntil) rows.Add(new List<Btn> { Lbl(Toast) });
        }

        // ---------- theme / colours ----------
        public static readonly string[] Presets = { "Classic", "Midnight", "Ice", "Lava", "Mint", "Mono" };
        public string EditTarget = "accent";
        public static Theme PresetTheme(string n)
        {
            switch (n)
            {
                case "Midnight": return Theme.Midnight;
                case "Ice": return Build(Color.HSVToRGB(.55f, .55f, .95f), Color.HSVToRGB(.60f, .45f, .10f));
                case "Lava": return Build(Color.HSVToRGB(.04f, .85f, .95f), Color.HSVToRGB(.00f, .40f, .09f));
                case "Mint": return Build(Color.HSVToRGB(.42f, .60f, .85f), Color.HSVToRGB(.45f, .35f, .08f));
                case "Mono": return Build(new Color(.82f, .82f, .82f), new Color(.07f, .07f, .07f));
                default: return Theme.Classic;
            }
        }
        // full theme from just an accent and a background colour; text colour follows contrast so labels stay readable
        public static Theme Build(Color accent, Color bg)
        {
            var btn = Color.Lerp(bg, accent, .22f); btn.a = 1;
            bool lightBtn = Lum(btn) > .5f;
            var accText = Lum(bg) > .5f ? Color.Lerp(accent, Color.black, .45f) : Color.Lerp(accent, Color.white, .45f);
            return new Theme
            {
                shell = Color.Lerp(accent, Color.black, .25f), panel = bg, head = Color.Lerp(bg, Color.black, .45f), btn = btn,
                btnOn = new Color(.13f, .37f, .25f), btnHover = Color.Lerp(bg, accent, .42f), btnOff = Color.Lerp(bg, btn, .35f),
                text = lightBtn ? Color.black : Color.white, textOff = Color.Lerp(btn, lightBtn ? Color.black : Color.white, .35f), accent = accText
            };
        }
        public static float Lum(Color c) => .2126f * c.r + .7152f * c.g + .0722f * c.b;
        public Theme CurrentTheme()
        {
            if (S.theme == "Custom") return Build(Color.HSVToRGB(S.accentH / 360f, S.accentS / 100f, S.accentV / 100f), Color.HSVToRGB(S.bgH / 360f, S.bgS / 100f, S.bgV / 100f));
            return PresetTheme(S.theme);
        }
        public void ApplyCurrentTheme() { if (board != null) board.ApplyTheme(CurrentTheme()); }
        private void ThemePage(List<List<Btn>> rows)
        {
            rows.Add(new List<Btn> { B("th:Classic", "Classic", true, S.theme == "Classic"), B("th:Midnight", "Midnight", true, S.theme == "Midnight"), B("th:Ice", "Ice", true, S.theme == "Ice") });
            rows.Add(new List<Btn> { B("th:Lava", "Lava", true, S.theme == "Lava"), B("th:Mint", "Mint", true, S.theme == "Mint"), B("th:Mono", "Mono", true, S.theme == "Mono") });
            rows.Add(new List<Btn> { B("th:edit:accent", "Custom accent", true, S.theme == "Custom" && EditTarget == "accent"), B("th:edit:bg", "Custom background", true, S.theme == "Custom" && EditTarget == "bg") });
            bool a = EditTarget == "accent"; int h = a ? S.accentH : S.bgH, s = a ? S.accentS : S.bgS, v = a ? S.accentV : S.bgV;
            rows.Add(new List<Btn> { B("th:h-", "-"), Lbl("Hue " + h + "\u00B0"), B("th:h+", "+") });
            rows.Add(new List<Btn> { B("th:s-", "-"), Lbl("Colour " + s + "%"), B("th:s+", "+") });
            rows.Add(new List<Btn> { B("th:v-", "-"), Lbl("Bright " + v + "%"), B("th:v+", "+") });
            rows.Add(new List<Btn> { Lbl(S.theme == "Custom" ? "Custom \u00B7 editing " + (a ? "accent" : "background") + " \u00B7 saved" : "Theme: " + S.theme + " (edit = switch to Custom)") });
        }
        // first edit from a preset copies that preset's colours so Custom starts from what you see
        private void StartCustom()
        {
            if (S.theme == "Custom") return;
            var t = PresetTheme(S.theme); float hh, ss, vv;
            Color.RGBToHSV(t.shell, out hh, out ss, out vv); S.accentH = Mathf.RoundToInt(hh * 360) % 360; S.accentS = Mathf.RoundToInt(ss * 100); S.accentV = Mathf.Clamp(Mathf.RoundToInt(vv / .75f * 100), 0, 100);
            Color.RGBToHSV(t.panel, out hh, out ss, out vv); S.bgH = Mathf.RoundToInt(hh * 360) % 360; S.bgS = Mathf.RoundToInt(ss * 100); S.bgV = Mathf.RoundToInt(vv * 100);
            S.theme = "Custom";
        }
        private bool ToolsPress(string id)
        {
            switch (id)
            {
                case "room:reveal": if (RoomRevealed) roomReveal = false; else { roomReveal = true; roomRevealUntil = Time.unscaledTime + RevealSeconds; } return true;
                case "tm:toggle": if (timerRun) { timerAcc += Time.unscaledTime - timerStart; timerRun = false; } else { timerStart = Time.unscaledTime; timerRun = true; } return true;
                case "tm:reset": timerAcc = 0; if (timerRun) timerStart = Time.unscaledTime; return true;
                case "mic:toggle": music.Send("mic", "toggle"); Say(MicMuted ? "Unmuting mic (Windows-wide)" : "Muting mic (Windows-wide)"); return true;
                case "mic:next": music.Send("mic", "next"); Say("Switching microphone"); return true;
            }
            if (id.StartsWith("th:edit:")) { StartCustom(); EditTarget = id.Substring(8); ApplyCurrentTheme(); SaveSettings(); return true; }
            if (id.StartsWith("th:") && id.Length == 5 && (id[4] == '-' || id[4] == '+'))
            {
                StartCustom(); bool a = EditTarget == "accent"; int d = id[4] == '+' ? 1 : -1;
                switch (id[3])
                {
                    case 'h': if (a) S.accentH = ((S.accentH + d * 15) % 360 + 360) % 360; else S.bgH = ((S.bgH + d * 15) % 360 + 360) % 360; break;
                    case 's': if (a) S.accentS = Mathf.Clamp(S.accentS + d * 10, 0, 100); else S.bgS = Mathf.Clamp(S.bgS + d * 10, 0, 100); break;
                    case 'v': if (a) S.accentV = Mathf.Clamp(S.accentV + d * 10, 10, 100); else S.bgV = Mathf.Clamp(S.bgV + d * 5, 0, 100); break;
                }
                ApplyCurrentTheme(); SaveSettings(); return true;
            }
            if (id.StartsWith("th:")) { var n = id.Substring(3); if (Array.IndexOf(Presets, n) >= 0) { S.theme = n; ApplyCurrentTheme(); SaveSettings(); } return true; }
            return false;
        }
    }
}
