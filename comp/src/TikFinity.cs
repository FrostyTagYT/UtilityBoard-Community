using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
namespace BoardCompanion;

// Read-only client for TikFinity's local WebSocket API (ws://127.0.0.1:<port>/) on THIS PC. TikFinity is logged into the
// user's own TikTok; we only listen to the events it forwards (like, chat, gift, follow, share, roomUser). Nothing is sent
// to TikTok, no token is needed or stored, and only loopback addresses are accepted.
public sealed class TikFinityClient
{
    public bool Enabled; public int Port = 21213; public string HostOverride;   // tests: "localhost"
    public volatile bool Connected; public string Status = "not started";
    public long? TotalLikes; public long SessionLikes, Viewers, Follows, Gifts, Diamonds, Shares, Events, AlertSeq; public string LastGift, LastAlert;
    readonly object L = new(); readonly List<string> chat = new();
    public DateTime LastEventAt = DateTime.MinValue; CancellationTokenSource kick = new();
    public long Likes => TotalLikes ?? SessionLikes;
    public List<string> Chat { get { lock (L) return chat.ToList(); } }
    public void Reconnect() { try { kick.Cancel(); } catch { } }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!Enabled) { Status = "not set up"; Connected = false; await Task.Delay(2000, ct).ContinueWith(_ => { }); continue; }
            using var ws = new ClientWebSocket(); kick = new CancellationTokenSource(); using var link = CancellationTokenSource.CreateLinkedTokenSource(ct, kick.Token);
            try
            {
                var host = HostOverride ?? "localhost"; var uri = new Uri("ws://" + host + ":" + Port + "/");
                if (!uri.IsLoopback) throw new InvalidOperationException("only this PC (loopback) is allowed");
                using (var to = CancellationTokenSource.CreateLinkedTokenSource(link.Token)) { to.CancelAfter(2500); await ws.ConnectAsync(uri, to.Token); }
                Connected = true; Status = "waiting for LIVE events"; Log.I("TikFinity: connected to local WebSocket API :" + Port);
                var buf = new byte[65536]; using var ms = new MemoryStream();
                while (ws.State == WebSocketState.Open && !link.IsCancellationRequested)
                {
                    var r = await ws.ReceiveAsync(buf, link.Token); if (r.MessageType == WebSocketMessageType.Close) break;
                    ms.Write(buf, 0, r.Count); if (ms.Length > 1 << 20) { ms.SetLength(0); continue; }
                    if (!r.EndOfMessage) continue;
                    var txt = Encoding.UTF8.GetString(ms.ToArray()); ms.SetLength(0);
                    try { Handle(txt); } catch (Exception e) { Log.W("TikFinity: bad event ignored (" + e.GetType().Name + ")"); }
                }
                Status = "TikFinity closed the connection";
            }
            catch (Exception) when (ct.IsCancellationRequested) { break; }
            catch (Exception e) { Status = e is WebSocketException or OperationCanceledException ? "TikFinity not running (port " + Port + ")" : "TikFinity error: " + e.Message; }
            if (Connected) Log.I("TikFinity: disconnected"); Connected = false;
            await Task.Delay(4000, ct).ContinueWith(_ => { });
        }
    }
    static string S(JsonNode n, string k) { try { return n?[k]?.GetValue<string>(); } catch { return n?[k]?.ToString(); } }
    static long N(JsonNode n, string k) { try { var v = n?[k]; if (v == null) return 0; if (v is JsonValue jv && jv.TryGetValue<long>(out var l)) return l; if (v is JsonValue jd && jd.TryGetValue<double>(out var d)) return (long)d; return long.TryParse(v.ToString(), out var p) ? p : 0; } catch { return 0; } }
    static string Clean(string s, int n) { if (string.IsNullOrEmpty(s)) return ""; var b = new StringBuilder(); foreach (var c in s) if (!char.IsControl(c)) b.Append(c); var r = b.ToString(); return r.Length <= n ? r : r[..(n - 1)] + "\u2026"; }
    void Alert(string t) { LastAlert = t; AlertSeq++; }
    public void Handle(string json)
    {
        var root = JsonNode.Parse(json);
        if (root is JsonArray arr) { foreach (var it in arr) if (it is JsonObject jo) HandleOne(jo); }
        else if (root is JsonObject o) HandleOne(o);
    }
    // TikFinity envelopes vary between versions: type in event/type/eventType/event_type; fields in data/payload/eventData or flat;
    // user and gift fields either flat or nested under "user" / "gift".
    static JsonNode Pick(JsonNode n, params string[] keys) { foreach (var k in keys) { var v = n?[k]; if (v != null) return v; } return null; }
    static string SS(JsonNode[] scopes, params string[] keys) { foreach (var sc in scopes) { var v = Pick(sc, keys); if (v is JsonValue jv) { if (jv.TryGetValue<string>(out var str)) return str; return jv.ToString(); } } return null; }
    static long NN(JsonNode[] scopes, params string[] keys) { foreach (var sc in scopes) foreach (var k in keys) { var v = sc?[k]; if (v == null) continue; var x = N(sc, k); if (x != 0) return x; } return 0; }
    static bool? BB(JsonNode[] scopes, string key) { foreach (var sc in scopes) if (sc?[key] is JsonValue v && v.TryGetValue<bool>(out var b)) return b; return null; }
    void HandleOne(JsonObject o)
    {
        var ev = (Pick(o, "event", "type", "eventType", "event_type") as JsonValue)?.ToString(); if (ev == null) return;
        var d = Pick(o, "data", "payload", "eventData") ?? o;
        var sc = new[] { d, d?["user"], d?["gift"], (JsonNode)o, o["user"], o["gift"] }.Where(x => x is JsonObject).ToArray();
        string who = Clean(SS(sc, "nickname", "displayName", "uniqueId", "userName") ?? "someone", 18);
        lock (L)
        {
            Events++; LastEventAt = DateTime.Now; Status = "live \u00B7 " + Events + " events";
            switch (ev)
            {
                case "like": SessionLikes += Math.Max(0, NN(sc, "likeCount", "like_count")); var tl = NN(sc, "totalLikeCount", "total_like_count", "totalLikes"); if (tl > 0) TotalLikes = Math.Max(TotalLikes ?? 0, tl); break;
                case "chat": chat.Add(who + ": " + Clean(SS(sc, "comment", "commentText", "text", "message"), 60)); if (chat.Count > 8) chat.RemoveAt(0); break;
                case "gift":
                    // streakable gifts (giftType 1) can send many in-progress events; count only the final one
                    var end = BB(sc, "repeatEnd"); if (NN(sc, "giftType") == 1 && end == false) break;
                    long cnt = Math.Max(1, NN(sc, "repeatCount", "repeat_count", "count")); Gifts += cnt; Diamonds += NN(sc, "diamondCount", "diamond_count", "coins") * cnt;
                    LastGift = who + " sent " + Clean(SS(sc, "giftName", "gift_name") ?? "a gift", 20) + (cnt > 1 ? " x" + cnt : ""); Alert(LastGift); break;
                case "follow": Follows++; Alert(who + " followed!"); break;
                case "social": var dt = SS(sc, "displayType") ?? ""; if (dt.Contains("follow", StringComparison.OrdinalIgnoreCase)) { Follows++; Alert(who + " followed!"); } else if (dt.Contains("share", StringComparison.OrdinalIgnoreCase)) Shares++; break;
                case "share": Shares++; break;
                case "roomUser": case "viewer": var vc = NN(sc, "viewerCount", "viewer_count", "viewers"); if (vc > 0) Viewers = vc; break;
                case "streamEnd": Status = "stream ended"; break;
            }
        }
    }
    public JsonObject Json() { lock (L) return new JsonObject
    {
        ["enabled"] = Enabled, ["connected"] = Connected, ["status"] = Status, ["likes"] = Likes, ["sessionLikes"] = SessionLikes, ["viewers"] = Viewers, ["follows"] = Follows,
        ["gifts"] = Gifts, ["diamonds"] = Diamonds, ["shares"] = Shares, ["events"] = Events, ["lastGift"] = LastGift, ["chat"] = new JsonArray(chat.TakeLast(4).Select(x => (JsonNode)x).ToArray()),
        ["alertSeq"] = AlertSeq, ["lastAlert"] = LastAlert
    }; }
}
