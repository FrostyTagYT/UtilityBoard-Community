using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
namespace BoardCompanion;

// Local-only named pipe (Edition.Pipe). ACL: current user only, network logons denied.
// One JSON object per line; each line is size-limited and each client is rate-limited.
public sealed class PipeServer
{
    public const string DefaultName = Edition.Pipe; public string Name = DefaultName;
    readonly Service svc; int clients;
    public PipeServer(Service s) { svc = s; }

    static PipeSecurity Acl()
    {
        var ps = new PipeSecurity();
        ps.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        ps.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        return ps;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        Log.I(@"Listening on \\.\pipe\" + Name + " (current user only, local only)");
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream p = null;
            try
            {
                p = NamedPipeServerStreamAcl.Create(Name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, Acl());
                await p.WaitForConnectionAsync(ct);
                var conn = p; p = null; _ = Task.Run(() => ServeAsync(conn, ct));
            }
            catch (OperationCanceledException) { break; }
            catch (IOException e) { Log.W("pipe busy/error: " + e.Message); p?.Dispose(); await Task.Delay(500, ct).ContinueWith(_ => { }); }
            catch (Exception e) { Log.W("pipe error: " + e.Message); p?.Dispose(); await Task.Delay(1000, ct).ContinueWith(_ => { }); }
        }
    }

    async Task ServeAsync(NamedPipeServerStream p, CancellationToken ct)
    {
        int n = Interlocked.Increment(ref clients); Log.I("client connected (" + n + " active)");
        double tokens = 30; var last = DateTime.UtcNow;
        try
        {
            using (p)
            {
                var buf = new byte[4096]; var line = new MemoryStream(); bool overflow = false;
                while (p.IsConnected && !ct.IsCancellationRequested)
                {
                    int r = await p.ReadAsync(buf, ct); if (r <= 0) break;
                    for (int i = 0; i < r; i++)
                    {
                        if (buf[i] != (byte)'\n') { if (line.Length < Service.MaxMessage + 1) line.WriteByte(buf[i]); else overflow = true; continue; }
                        var text = Encoding.UTF8.GetString(line.ToArray()).TrimEnd('\r'); line.SetLength(0);
                        var now = DateTime.UtcNow; tokens = Math.Min(30, tokens + (now - last).TotalSeconds * 20); last = now;
                        string resp;
                        if (overflow) { resp = "{\"id\":null,\"ok\":false,\"error\":\"message too large\"}"; overflow = false; }
                        else if (tokens < 1) resp = "{\"id\":null,\"ok\":false,\"error\":\"rate limited\"}";
                        else { tokens -= 1; resp = text.Length == 0 ? null : await svc.HandleAsync(text); }
                        if (resp != null) { var b = Encoding.UTF8.GetBytes(resp + "\n"); await p.WriteAsync(b, ct); await p.FlushAsync(ct); }
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
        catch (Exception e) { Log.W("client error: " + e.Message); }
        n = Interlocked.Decrement(ref clients); Log.I("client disconnected (" + n + " active)");
    }
}
