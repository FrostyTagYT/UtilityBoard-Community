using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace UtilityBoard
{
    // Real local wall clock. Unity's Mono can report TimeZoneInfo.Local as UTC, so DateTime.Now may be the UTC time.
    // Win32 GetLocalTime is always the PC's own local time; the zone label ("CT", "PT", ...) comes from GetTimeZoneInformation.
    public static class LocalClock
    {
        [StructLayout(LayoutKind.Sequential)] private struct SYSTEMTIME { public ushort y, mo, dow, d, h, mi, s, ms; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct TZI { public int Bias; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string StdName; public SYSTEMTIME StdDate; public int StdBias; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DstName; public SYSTEMTIME DstDate; public int DstBias; }
        [DllImport("kernel32.dll")] private static extern void GetLocalTime(out SYSTEMTIME t);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetTimeZoneInformation(out TZI z);
        private static bool broken; private static string zone;
        public static DateTime Now()
        {
            if (!broken) try { SYSTEMTIME t; GetLocalTime(out t); return new DateTime(t.y, t.mo, t.d, t.h, t.mi, t.s, t.ms, DateTimeKind.Local); } catch (Exception) { broken = true; }
            return DateTime.Now;
        }
        // "Central Standard Time" -> "CT"
        public static string Zone()
        {
            if (zone != null) return zone;
            try
            {
                TZI z; GetTimeZoneInformation(out z); var b = new StringBuilder();
                foreach (var w in (z.StdName ?? "").Split(' ')) { if (w.Length == 0 || w == "Standard" || w == "Daylight" || w == "Summer") continue; if (char.IsLetter(w[0])) b.Append(char.ToUpperInvariant(w[0])); }
                zone = b.Length >= 2 && b.Length <= 5 ? b.ToString() : "local";
            }
            catch (Exception) { zone = "local"; }
            return zone;
        }
    }

    // Lightweight performance monitor (no allocations per frame).
    public static class Perf
    {
        private static float acc, worst, shownFps = -1, shownMs, shownWorst; private static int frames; private static float windowStart = -1;
        public static float Fps => shownFps; public static float FrameMs => shownMs; public static float WorstMs => shownWorst;
        public static void Tick()
        {
            float dt = Time.unscaledDeltaTime; if (windowStart < 0) windowStart = Time.unscaledTime;
            acc += dt; frames++; if (dt > worst) worst = dt;
            if (Time.unscaledTime - windowStart >= 1f && frames > 0) { shownFps = frames / acc; shownMs = acc / frames * 1000f; shownWorst = worst * 1000f; acc = 0; frames = 0; worst = 0; windowStart = Time.unscaledTime; }
        }
        public static long ManagedMB => GC.GetTotalMemory(false) / (1024 * 1024);
    }
}
