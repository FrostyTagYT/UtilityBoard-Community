using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace BoardCompanion;

// obs-websocket v5 client, localhost only. The password is read from OBS's own local config each time we connect
// (%APPDATA%\obs-studio\plugin_config\obs-websocket\config.json) and is never stored, logged or sent to the game.
// ReadOnly = true refuses every request that changes OBS (used for tests against the user's real OBS).
public sealed class ObsClient : IDisposable
{
    public string ConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio", "plugin_config", "obs-websocket", "config.json");
    public int? PortOverride; public string PasswordOverride;   // mock tests only
    public Integrations Integ;                                  // per-user (DPAPI) settings: auto from OBS config, or manual port/password
    public bool ReadOnly;
    static readonly HashSet<string> ReadReqs = new() { "GetVersion", "GetRecordStatus", "GetReplayBufferStatus", "GetSceneList", "GetCurrentProgramScene" };
    ClientWebSocket ws; readonly SemaphoreSlim gate = new(1, 1); int rid;
    public volatile bool Connected; public string Error = "OBS not checked yet", Version;
    public bool Recording, RecordPaused, ReplayActive, ReplayAvailable; public long RecordMs; public string CurrentScene;
    public List<string> Scenes = new(); public string LastAction;
    DateTime nextTry = DateTime.MinValue;

    (int port, string pw, bool enabled, string err) ReadConfig()
    {
        if (PortOverride.HasValue) return (PortOverride.Value, PasswordOverride, true, null);
        if (Integ != null && !Integ.ObsEnabled) return (0, null, false, "OBS control turned off in setup");
        if (Integ != null && !Integ.ObsUseObsConfig) return (Integ.ObsPort, string.IsNullOrEmpty(Integ.ObsPassword) ? null : Integ.ObsPassword, true, null);
        try
        {
            if (!File.Exists(ConfigPath)) return (0, null, false, "OBS websocket config not found (is OBS installed?)");
            var j = JsonNode.Parse(File.ReadAllText(ConfigPath)) as JsonObject;
            bool en = j?["server_enabled"]?.GetValue<bool>() ?? false; int port = j?["server_port"]?.GetValue<int>() ?? 4455;
            bool auth = j?["auth_required"]?.GetValue<bool>() ?? false; string pw = auth ? j?["server_password"]?.GetValue<string>() : null;
            return (port, pw, en, en ? null : "OBS websocket server is turned off (Tools > WebSocket Server Settings)");
        }
        catch (Exception e) { return (0, null, false, "could not read OBS websocket config: " + e.Message); }
    }

    async Task<bool> ConnectAsync(CancellationToken ct)
    {
        if (Connected && ws?.State == WebSocketState.Open) return true;
        if (DateTime.UtcNow < nextTry) return false;
        nextTry = DateTime.UtcNow.AddSeconds(4);
        var (port, pw, en, err) = ReadConfig(); if (!en) { Error = err; return false; }
        Drop();
        try
        {
            ws = new ClientWebSocket(); ws.Options.AddSubProtocol("obswebsocket.json");
            using var to = CancellationTokenSource.CreateLinkedTokenSource(ct); to.CancelAfter(2500);
            await ws.ConnectAsync(new Uri("ws://127.0.0.1:" + port + "/"), to.Token);
            var hello = await RecvAsync(to.Token); var d = hello?["d"];
            var ident = new JsonObject { ["rpcVersion"] = 1, ["eventSubscriptions"] = 0 };
            var auth = d?["authentication"];
            if (auth != null)
            {
                if (string.IsNullOrEmpty(pw)) { Error = "OBS needs a password but none is set in its websocket config"; Drop(); return false; }
                ident["authentication"] = Auth(pw, auth["salt"]!.GetValue<string>(), auth["challenge"]!.GetValue<string>());
            }
            await SendAsync(new JsonObject { ["op"] = 1, ["d"] = ident }, to.Token);
            JsonObject ok; try { ok = await RecvAsync(to.Token); } catch (IOException) { Error = "OBS refused the connection (wrong websocket password?)"; Drop(); return false; }
            if (ok?["op"]?.GetValue<int>() != 2) { Error = "OBS refused the connection (wrong websocket password?)"; Drop(); return false; }
            Connected = true; Error = null; Version = d?["obsWebSocketVersion"]?.GetValue<string>(); Log.I("OBS: connected (websocket " + Version + ", " + (ReadOnly ? "READ-ONLY" : "control") + ")");
            return true;
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException) { Error = "OBS not running (or websocket off)"; Drop(); return false; }
        catch (Exception e) { Error = "OBS connect failed: " + e.Message; Drop(); return false; }
    }
    public static string Auth(string pw, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(pw + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }
    void Drop() { Connected = false; try { ws?.Abort(); ws?.Dispose(); } catch { } ws = null; }

    async Task SendAsync(JsonObject o, CancellationToken ct) => await ws.SendAsync(Encoding.UTF8.GetBytes(o.ToJsonString()), WebSocketMessageType.Text, true, ct);
    async Task<JsonObject> RecvAsync(CancellationToken ct)
    {
        var buf = new byte[16384]; using var ms = new MemoryStream();
        while (true)
        {
            var r = await ws.ReceiveAsync(buf, ct); if (r.MessageType == WebSocketMessageType.Close) throw new IOException("closed");
            ms.Write(buf, 0, r.Count); if (ms.Length > 1 << 20) throw new IOException("message too large"); if (r.EndOfMessage) break;
        }
        return JsonNode.Parse(Encoding.UTF8.GetString(ms.ToArray())) as JsonObject;
    }

    // one request -> (ok, responseData, error)
    public async Task<(bool ok, JsonObject data, string err)> RequestAsync(string type, JsonObject data = null, CancellationToken ct = default)
    {
        if (ReadOnly && !ReadReqs.Contains(type)) { Log.W("OBS: refused " + type + " (read-only mode)"); return (false, null, "read-only mode: " + type + " not sent"); }
        await gate.WaitAsync(ct);
        try
        {
            if (!await ConnectAsync(ct)) return (false, null, Error);
            using var to = CancellationTokenSource.CreateLinkedTokenSource(ct); to.CancelAfter(3000);
            var id = "f" + Interlocked.Increment(ref rid);
            var d = new JsonObject { ["requestType"] = type, ["requestId"] = id }; if (data != null) d["requestData"] = data;
            await SendAsync(new JsonObject { ["op"] = 6, ["d"] = d }, to.Token);
            while (true)
            {
                var m = await RecvAsync(to.Token);
                if (m?["op"]?.GetValue<int>() != 7 || m["d"]?["requestId"]?.GetValue<string>() != id) continue;   // skip anything else
                var st = m["d"]["requestStatus"]; bool ok = st?["result"]?.GetValue<bool>() == true;
                return (ok, m["d"]["responseData"] as JsonObject, ok ? null : (st?["comment"]?.GetValue<string>() ?? "OBS error " + st?["code"]));
            }
        }
        catch (Exception e) { Drop(); Error = "OBS connection lost: " + e.Message; return (false, null, Error); }
        finally { gate.Release(); }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        LastRefresh = DateTime.UtcNow;
        var r = await RequestAsync("GetRecordStatus", null, ct);
        if (!r.ok) { if (!Connected) { Recording = false; ReplayActive = false; } return; }
        Recording = r.data?["outputActive"]?.GetValue<bool>() == true; RecordPaused = r.data?["outputPaused"]?.GetValue<bool>() == true;
        RecordMs = r.data?["outputDuration"]?.GetValue<long>() ?? 0;
        var rb = await RequestAsync("GetReplayBufferStatus", null, ct); ReplayAvailable = rb.ok; ReplayActive = rb.ok && rb.data?["outputActive"]?.GetValue<bool>() == true;
        var sl = await RequestAsync("GetSceneList", null, ct);
        if (sl.ok && sl.data?["scenes"] is JsonArray arr)
        {
            // OBS lists scenes bottom-up; show them top-down like the OBS scene dock
            var l = arr.Select(x => x?["sceneName"]?.GetValue<string>()).Where(x => x != null).Reverse().ToList(); Scenes = l;
            CurrentScene = sl.data["currentProgramSceneName"]?.GetValue<string>();
        }
    }

    public async Task<string> ToggleRecordAsync() { var r = await RequestAsync("ToggleRecord"); if (r.ok) { LastAction = Recording ? "Recording stopped" : "Recording started"; Log.I("OBS: " + LastAction); await RefreshAsync(); } return r.err; }
    public async Task<string> SetSceneAsync(int index)
    {
        var l = Scenes; if (index < 0 || index >= l.Count) return "Unknown scene";
        var r = await RequestAsync("SetCurrentProgramScene", new JsonObject { ["sceneName"] = l[index] });
        if (r.ok) { LastAction = "Scene: " + l[index]; Log.I("OBS: " + LastAction); await RefreshAsync(); }
        return r.err;
    }
    // "Clip": save the replay buffer if it is running, otherwise drop a chapter marker into the recording (OBS 30.2+, Hybrid MP4)
    public async Task<string> ClipAsync()
    {
        await RefreshAsync(); if (!Connected) return Error;
        if (ReplayActive) { var r = await RequestAsync("SaveReplayBuffer"); if (r.ok) { LastAction = "Replay saved"; Log.I("OBS: replay buffer saved"); } return r.err; }
        if (Recording)
        {
            var r = await RequestAsync("CreateRecordChapter", new JsonObject { ["chapterName"] = "Clip " + DateTime.Now.ToString("HH:mm:ss") });
            if (r.ok) { LastAction = "Chapter marker added"; Log.I("OBS: chapter marker"); return null; }
            return "Chapter markers need OBS 30.2+ recording to Hybrid MP4 (" + r.err + ")";
        }
        return "Start the replay buffer or a recording in OBS first";
    }
    // one honest status line for the board: what OBS is really doing right now
    public string StatusText()
    {
        if (Connected && ws?.State != WebSocketState.Open) { Connected = false; Error = "OBS connection lost"; }
        if (!Connected) return Error ?? "OBS not connected";
        return (Recording ? (RecordPaused ? "REC paused " : "REC ") + TimeSpan.FromMilliseconds(RecordMs).ToString(@"h\:mm\:ss") : "Not recording") + (ReplayActive ? " \u00B7 replay on" : "") + (CurrentScene != null ? " \u00B7 " + CurrentScene : "");
    }
    public DateTime LastRefresh = DateTime.MinValue;
    public void Dispose() => Drop();
}
