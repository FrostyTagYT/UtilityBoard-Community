using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace UtilityBoard
{
    // Local-only client for the companion app (named pipe Edition.Pipe). Runs on a background thread;
    // the game thread only enqueues commands and reads the latest state snapshot. No network, no credentials.
    public sealed class MusicLink
    {
        public static string PipeName = Edition.Pipe;   // Debug.PipeOverride (developer isolation only) can change it
        private readonly ConcurrentQueue<string> outq = new ConcurrentQueue<string>();
        private readonly object L = new object();
        private Dictionary<string, object> state; private List<object> tracks; private int tracksOffset, tracksTotal; private List<object> devices;
        private Thread th; private volatile bool run; private int nextId = 1;
        public volatile bool Connected; public volatile string LastError = ""; public volatile string LastReplyError = ""; public volatile int Replies;
        public volatile float PollSeconds = 1f; public DateTime LastErrAt = DateTime.MinValue; public string Note = "";
        private DateTime lastPoll = DateTime.MinValue;

        public void Start() { if (th != null) return; run = true; th = new Thread(Loop) { IsBackground = true, Name = "UBCompanionLink" }; th.Start(); }
        public void Stop() { run = false; }

        public void Send(string source, string command, int? value = null, string trackId = null, string device = null)
        {
            var sb = new StringBuilder("{\"id\":\"" + (nextId++) + "\"");
            if (source != null) sb.Append(",\"source\":\"" + source + "\"");
            sb.Append(",\"command\":\"" + command + "\"");
            if (value.HasValue) sb.Append(",\"value\":" + value.Value.ToString(CultureInfo.InvariantCulture));
            if (trackId != null) sb.Append(",\"trackId\":\"" + Esc(trackId) + "\"");
            if (device != null) sb.Append(",\"device\":\"" + Esc(device) + "\"");
            sb.Append("}");
            if (outq.Count < 32) outq.Enqueue(sb.ToString());
        }
        private static string Esc(string s) { var b = new StringBuilder(); foreach (var c in s) { if (c == '"' || c == '\\') b.Append('\\').Append(c); else if (c < 32) b.Append(' '); else b.Append(c); } return b.ToString(); }

        private void Loop()
        {
            while (run)
            {
                NamedPipeClientStream p = null;
                try
                {
                    p = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                    p.Connect(800);
                    Connected = true; LastError = ""; Note = "connected"; 
                    var rd = new StreamReader(p, new UTF8Encoding(false)); var wr = new StreamWriter(p, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                    Send(null, "getState"); Send(null, "listDevices");
                    while (run && p.IsConnected)
                    {
                        string line;
                        if (!outq.TryDequeue(out line))
                        {
                            if ((DateTime.UtcNow - lastPoll).TotalSeconds >= PollSeconds) { lastPoll = DateTime.UtcNow; line = "{\"command\":\"getState\"}"; }
                            else { Thread.Sleep(30); continue; }
                        }
                        wr.WriteLine(line);
                        var resp = rd.ReadLine(); if (resp == null) break;
                        Handle(resp);
                    }
                }
                catch (TimeoutException) { LastError = "Companion not running"; }
                catch (Exception e) { LastError = e.GetType().Name + ": " + e.Message; }
                finally { try { p?.Dispose(); } catch (Exception) { } }
                if (Connected) Note = "disconnected"; Connected = false;
                for (int i = 0; i < 20 && run; i++) Thread.Sleep(100);   // retry every ~2 s
            }
        }
        private void Handle(string resp)
        {
            var o = MiniJson.Parse(resp) as Dictionary<string, object>; if (o == null) return;
            Replies++;
            bool ok = o.TryGetValue("ok", out var okv) && okv is bool b && b;
            if (!ok) LastErrAt = DateTime.UtcNow; LastReplyError = ok ? "" : (o.TryGetValue("error", out var e) ? e as string ?? "" : "error");
            lock (L)
            {
                if (o.TryGetValue("state", out var st) && st is Dictionary<string, object> sd) state = sd;
                if (o.TryGetValue("data", out var d))
                {
                    if (d is List<object> dl) devices = dl;
                    else if (d is Dictionary<string, object> dd && dd.ContainsKey("items")) { tracks = dd["items"] as List<object>; tracksOffset = (int)MiniJson.Num(dd, "offset"); tracksTotal = (int)MiniJson.Num(dd, "total"); }
                }
            }
        }
        public Dictionary<string, object> State { get { lock (L) return state; } }
        public object Get(string a, string b) { var s = State; if (s == null) return null; return s.TryGetValue(a, out var x) && x is Dictionary<string, object> d && d.TryGetValue(b, out var y) ? y : null; }
        public string S(string a, string b) => Get(a, b) as string;
        public bool B(string a, string b) => Get(a, b) is bool v && v;
        public double N(string a, string b) { var v = Get(a, b); return v is double d ? d : 0; }
        public List<object> Tracks(out int offset, out int total) { lock (L) { offset = tracksOffset; total = tracksTotal; return tracks; } }
        public List<object> Devices { get { lock (L) return devices; } }
        public bool PausePrevious { get { var s = State; return s != null && s.TryGetValue("pausePrevious", out var v) && v is bool b && b; } }
    }

    // Minimal JSON reader (objects, arrays, strings, numbers, true/false/null). Numbers come back as double.
    public static class MiniJson
    {
        public static object Parse(string s) { try { int i = 0; var v = Val(s, ref i); return v; } catch (Exception) { return null; } }
        public static double Num(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) && v is double x ? x : 0;
        private static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
        private static object Val(string s, ref int i)
        {
            Ws(s, ref i); char c = s[i];
            if (c == '{') { var d = new Dictionary<string, object>(); i++; Ws(s, ref i); if (s[i] == '}') { i++; return d; }
                while (true) { Ws(s, ref i); var k = Str(s, ref i); Ws(s, ref i); i++; d[k] = Val(s, ref i); Ws(s, ref i); if (s[i] == ',') { i++; continue; } i++; return d; } }
            if (c == '[') { var l = new List<object>(); i++; Ws(s, ref i); if (s[i] == ']') { i++; return l; }
                while (true) { l.Add(Val(s, ref i)); Ws(s, ref i); if (s[i] == ',') { i++; continue; } i++; return l; } }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i; while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
        }
        private static string Str(string s, ref int i)
        {
            var b = new StringBuilder(); i++;
            while (s[i] != '"')
            {
                if (s[i] == '\\') { i++; char e = s[i]; if (e == 'u') { b.Append((char)int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber)); i += 4; } else b.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e == 'r' ? '\r' : e == 'b' ? '\b' : e == 'f' ? '\f' : e); }
                else b.Append(s[i]);
                i++;
            }
            i++; return b.ToString();
        }
    }
}
