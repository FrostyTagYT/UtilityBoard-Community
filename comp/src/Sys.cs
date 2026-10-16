using System.Runtime.InteropServices;
namespace BoardCompanion;

// PC CPU % (GetSystemTimes deltas) and RAM % (GlobalMemoryStatusEx) for the board's performance page.
public static class SysStats
{
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [StructLayout(LayoutKind.Sequential)] struct MEMSTAT { public uint len, load; public ulong total, avail, tpf, apf, tv, av, ave; }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMSTAT m);
    static long pi, pk, pu; static double cpu = -1; static DateTime last = DateTime.MinValue;
    public static double Cpu()
    {
        if ((DateTime.UtcNow - last).TotalMilliseconds < 900) return cpu; last = DateTime.UtcNow;
        if (!GetSystemTimes(out var i, out var k, out var u)) return cpu;
        long di = i - pi, dk = k - pk, du = u - pu; pi = i; pk = k; pu = u; long tot = dk + du;
        if (tot > 0 && pi != 0) cpu = Math.Round(Math.Clamp((tot - di) * 100.0 / tot, 0, 100), 1); return cpu;
    }
    public static double Ram() { var m = new MEMSTAT { len = (uint)Marshal.SizeOf<MEMSTAT>() }; return GlobalMemoryStatusEx(ref m) ? m.load : -1; }
}
