using System.Net;
using System.Net.WebSockets;
using System.Text;
namespace BoardCompanion;

// Fake TikFinity WebSocket API on localhost for tests: sends a scripted mix of like/chat/gift/follow/roomUser events.
public sealed class MockTikFinity : IDisposable
{
    public int Port; HttpListener hl; readonly CancellationTokenSource cts = new(); public int Sent;
    public static readonly string[] Script =
    {
        "{\"event\":\"roomUser\",\"data\":{\"viewerCount\":137}}",
        "{\"event\":\"like\",\"data\":{\"likeCount\":15,\"totalLikeCount\":106400,\"uniqueId\":\"viewer_a\",\"nickname\":\"Viewer A\"}}",
        "{\"event\":\"chat\",\"data\":{\"uniqueId\":\"viewer_b\",\"nickname\":\"Viewer B\",\"comment\":\"hello from chat\"}}",
        "{\"event\":\"gift\",\"data\":{\"uniqueId\":\"viewer_c\",\"nickname\":\"Viewer C\",\"giftName\":\"Rose\",\"giftType\":1,\"repeatCount\":3,\"repeatEnd\":false,\"diamondCount\":1}}",
        "{\"event\":\"gift\",\"data\":{\"uniqueId\":\"viewer_c\",\"nickname\":\"Viewer C\",\"giftName\":\"Rose\",\"giftType\":1,\"repeatCount\":5,\"repeatEnd\":true,\"diamondCount\":1}}",
        "{\"event\":\"follow\",\"data\":{\"uniqueId\":\"viewer_d\",\"nickname\":\"Viewer D\"}}",
        "[{\"type\":\"like\",\"payload\":{\"user\":{\"nickname\":\"Viewer E\"},\"likeCount\":5,\"totalLikeCount\":106450}}]",
        "{\"event\":\"chat\",\"data\":{\"uniqueId\":\"viewer_f\",\"nickname\":\"Viewer F\",\"comment\":\"second message\"}}",
    };
    public MockTikFinity Start()
    {
        for (int tries = 0; tries < 20; tries++)
        {
            Port = 47000 + Random.Shared.Next(2000); hl = new HttpListener(); hl.Prefixes.Add("http://localhost:" + Port + "/");
            try { hl.Start(); break; } catch { hl = null; }
        }
        if (hl == null) throw new IOException("mock TikFinity could not listen");
        _ = Task.Run(Loop); Log.I("MockTikFinity listening on localhost:" + Port); return this;
    }
    async Task Loop()
    {
        while (!cts.IsCancellationRequested)
        {
            HttpListenerContext c; try { c = await hl.GetContextAsync(); } catch { break; }
            if (!c.Request.IsWebSocketRequest) { c.Response.StatusCode = 400; c.Response.Close(); continue; }
            _ = Task.Run(async () =>
            {
                try
                {
                    var wsc = await c.AcceptWebSocketAsync(null); var ws = wsc.WebSocket; await Task.Delay(400);
                    foreach (var m in Script) { await ws.SendAsync(Encoding.UTF8.GetBytes(m), WebSocketMessageType.Text, true, cts.Token); Sent++; await Task.Delay(150); }
                    while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested) await Task.Delay(500);
                }
                catch { }
            });
        }
    }
    public void Dispose() { cts.Cancel(); try { hl?.Stop(); hl?.Close(); } catch { } }
}
