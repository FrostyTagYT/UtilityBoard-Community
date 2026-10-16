using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace BoardCompanion;

// Minimal fake obs-websocket v5 server on 127.0.0.1 for tests (auth, record, replay buffer, scenes, chapters).
// Lets the mutating OBS calls be tested without ever touching the user's real OBS.
public sealed class MockObs : IDisposable
{
    public int Port; public string Password = "mockpass123"; public bool ReplayEnabled = true, ReplayActive, Recording, ChapterSupported = true;
    public List<string> Scenes = new() { "Gameplay", "Starting Soon", "BRB", "Just Chatting", "Ending" }; public string Current = "Gameplay";
    public readonly List<string> Calls = new(); public int Saves, Chapters, AuthFailures;
    TcpListener tl; CancellationTokenSource cts = new();
    public MockObs Start(int port = 0)
    {
        tl = new TcpListener(IPAddress.Loopback, port); tl.Start(); Port = ((IPEndPoint)tl.LocalEndpoint).Port;
        _ = Task.Run(async () => { while (!cts.IsCancellationRequested) { try { var c = await tl.AcceptTcpClientAsync(cts.Token); _ = Task.Run(() => Serve(c)); } catch { break; } } });
        Log.I("MockObs listening on 127.0.0.1:" + Port); return this;
    }
    async Task Serve(TcpClient c)
    {
        using (c)
        {
            var s = c.GetStream(); var hdr = new StringBuilder(); var b = new byte[1];
            while (!hdr.ToString().EndsWith("\r\n\r\n")) { if (await s.ReadAsync(b) <= 0) return; hdr.Append((char)b[0]); if (hdr.Length > 8192) return; }
            var key = hdr.ToString().Split("\r\n").FirstOrDefault(l => l.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))?.Split(':', 2)[1].Trim();
            if (key == null) return;
            var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            var resp = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\nSec-WebSocket-Protocol: obswebsocket.json\r\n\r\n";
            await s.WriteAsync(Encoding.ASCII.GetBytes(resp));
            using var ws = WebSocket.CreateFromStream(s, true, "obswebsocket.json", TimeSpan.FromSeconds(30));
            string salt = "c2FsdA==", challenge = Convert.ToBase64String(RandomNumberGenerator.GetBytes(12));
            await Send(ws, new JsonObject { ["op"] = 0, ["d"] = new JsonObject { ["obsWebSocketVersion"] = "5.5.0-mock", ["rpcVersion"] = 1, ["authentication"] = new JsonObject { ["challenge"] = challenge, ["salt"] = salt } } });
            bool ident = false; var buf = new byte[16384];
            while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
            {
                WebSocketReceiveResult r; try { r = await ws.ReceiveAsync(buf, cts.Token); } catch { return; }
                if (r.MessageType == WebSocketMessageType.Close) return;
                var m = JsonNode.Parse(Encoding.UTF8.GetString(buf, 0, r.Count)) as JsonObject; int op = m?["op"]?.GetValue<int>() ?? -1;
                if (op == 1)
                {
                    var a = m["d"]?["authentication"]?.GetValue<string>();
                    if (a != ObsClient.Auth(Password, salt, challenge)) { AuthFailures++; await ws.CloseAsync((WebSocketCloseStatus)4009, "Authentication failed", default); return; }
                    ident = true; await Send(ws, new JsonObject { ["op"] = 2, ["d"] = new JsonObject { ["negotiatedRpcVersion"] = 1 } }); continue;
                }
                if (op != 6 || !ident) continue;
                var d = m["d"]; string type = d["requestType"].GetValue<string>(), id = d["requestId"].GetValue<string>(); var rd = d["requestData"] as JsonObject;
                lock (Calls) Calls.Add(type);
                bool ok = true; int code = 100; string comment = null; JsonObject outd = null;
                switch (type)
                {
                    case "GetVersion": outd = new JsonObject { ["obsVersion"] = "31.0.0-mock" }; break;
                    case "GetRecordStatus": outd = new JsonObject { ["outputActive"] = Recording, ["outputPaused"] = false, ["outputDuration"] = Recording ? 12345 : 0 }; break;
                    case "GetReplayBufferStatus": if (!ReplayEnabled) { ok = false; code = 604; comment = "Replay buffer is not available."; } else outd = new JsonObject { ["outputActive"] = ReplayActive }; break;
                    case "GetSceneList":
                        var arr = new JsonArray(); for (int i = Scenes.Count - 1; i >= 0; i--) arr.Add(new JsonObject { ["sceneName"] = Scenes[i], ["sceneIndex"] = Scenes.Count - 1 - i });
                        outd = new JsonObject { ["currentProgramSceneName"] = Current, ["scenes"] = arr }; break;
                    case "ToggleRecord": Recording = !Recording; outd = new JsonObject { ["outputActive"] = Recording }; break;
                    case "SetCurrentProgramScene": var sn = rd?["sceneName"]?.GetValue<string>(); if (!Scenes.Contains(sn)) { ok = false; code = 600; comment = "No source was found"; } else Current = sn; break;
                    case "SaveReplayBuffer": if (!ReplayActive) { ok = false; code = 501; comment = "Replay buffer is not active."; } else Saves++; break;
                    case "CreateRecordChapter": if (!Recording || !ChapterSupported) { ok = false; code = 501; comment = "The current recording does not support chapters."; } else Chapters++; break;
                    default: ok = false; code = 204; comment = "Unknown request type"; break;
                }
                var st = new JsonObject { ["result"] = ok, ["code"] = code }; if (comment != null) st["comment"] = comment;
                var dd = new JsonObject { ["requestType"] = type, ["requestId"] = id, ["requestStatus"] = st }; if (outd != null) dd["responseData"] = outd;
                await Send(ws, new JsonObject { ["op"] = 7, ["d"] = dd });
            }
        }
    }
    static Task Send(WebSocket ws, JsonObject o) => ws.SendAsync(Encoding.UTF8.GetBytes(o.ToJsonString()), WebSocketMessageType.Text, true, default);
    public void Dispose() { cts.Cancel(); try { tl.Stop(); } catch { } }
}
