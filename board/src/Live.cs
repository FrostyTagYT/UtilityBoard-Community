using System;
using System.Collections.Generic;
using UnityEngine;
using static UtilityBoard.Board;

namespace UtilityBoard
{
    // TikTok LIVE (read from TikFinity running on YOUR PC, via the companion) + performance monitor.
    // The board never sees any account, token or password: the companion owns the local TikFinity/OBS connections.
    public partial class Plugin
    {
        private static string Num(double v) => v >= 1e6 ? (v / 1e6).ToString("0.##") + "M" : v >= 1e4 ? (v / 1e3).ToString("0.#") + "K" : ((long)v).ToString("N0");
        private void LivePage(List<List<Btn>> rows, Color warn)
        {
            if (NeedCompanion(rows, warn, "live", "enabled")) return;
            bool en = music.B("live", "enabled"), con = music.B("live", "connected");
            if (!en)
            {
                rows.Add(new List<Btn> { Lbl("TikFinity not set up", warn) });
                rows.Add(new List<Btn> { Lbl("On your PC: run \"Setup Integrations\"") });
                rows.Add(new List<Btn> { Lbl("Uses YOUR TikTok via your own TikFinity") });
                rows.Add(new List<Btn> { Lbl("Nothing is shared with anyone else") });
                return;
            }
            rows.Add(new List<Btn> { Lbl(con ? "TikFinity connected \u00B7 " + (music.S("live", "status") ?? "") : (music.S("live", "status") ?? "TikFinity not running"), con ? (Color?)null : warn) });
            rows.Add(new List<Btn> { Lbl("Likes " + Num(music.N("live", "likes")), Color.white), Lbl("Viewers " + Num(music.N("live", "viewers")), Color.white) });
            rows.Add(new List<Btn> { Lbl("Follows +" + (long)music.N("live", "follows")), Lbl("Gifts " + (long)music.N("live", "gifts") + " \u00B7 " + Num(music.N("live", "diamonds")) + " dmd") });
            var chat = music.Get("live", "chat") as List<object>;
            for (int i = 0; i < 3; i++) { string line = chat != null && i < chat.Count ? chat[chat.Count - 1 - i] as string : (i == 0 ? "(no chat yet)" : ""); rows.Add(new List<Btn> { Lbl(line ?? "", i == 0 ? Color.white : (Color?)null) }); }
            rows.Add(new List<Btn> { Lbl(music.S("live", "lastGift") ?? "") });
            rows.Add(new List<Btn> { B("live:reconnect", "Reconnect"), Lbl(Foot("read-only: your own stream")) });
        }
        private bool LivePress(string id)
        {
            if (id == "live:alerts") { S.alerts = !S.alerts; SaveSettings(); return true; }
            if (id == "live:reconnect") { music.Send("live", "reconnect"); Say("Reconnecting to TikFinity"); return true; }
            return false;
        }
        // gift / follow alerts as a board toast + a small haptic tick
        private double alertSeen = -1;
        private void LiveAlerts()
        {
        }
        public int AlertCount;

        private void PerfPage(List<List<Btn>> rows)
        {
            int ping = GTApi.Ping(); int pl; var room = RoomCodeNow(out pl);
            rows.Add(new List<Btn> { Lbl("FPS " + (Perf.Fps < 0 ? "--" : Perf.Fps.ToString("0")), Color.white), Lbl(Perf.FrameMs.ToString("0.0") + " ms", Color.white) });
            rows.Add(new List<Btn> { Lbl("Worst frame " + Perf.WorstMs.ToString("0") + " ms (last 1 s)") });
            rows.Add(new List<Btn> { Lbl("Ping " + (ping >= 0 && room != null ? ping + " ms" : "-- (offline)")), Lbl(room != null ? pl + " players" : "offline") });
            bool sys = music.Connected && music.Get("sys", "cpu") != null;
            rows.Add(new List<Btn> { Lbl("PC CPU " + (sys ? music.N("sys", "cpu").ToString("0") + "%" : "--")), Lbl("PC RAM " + (sys ? music.N("sys", "ram").ToString("0") + "%" : "--")) });
            rows.Add(new List<Btn> { Lbl("Game managed mem " + Perf.ManagedMB + " MB") });
            rows.Add(new List<Btn> { Lbl("Up " + HMS(Time.realtimeSinceStartup) + " \u00B7 " + Edition.Id + " v" + Version) });
            rows.Add(new List<Btn> { Lbl(sys ? "CPU/RAM from the companion" : "Start the companion for CPU/RAM") });
        }
    }
}
