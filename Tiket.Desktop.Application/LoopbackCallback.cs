using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Tiket.Desktop.Application;

/// <summary>One-shot raw loopback HTTP receiver: no HTTP.sys registration or URL ACL.</summary>
public sealed class LoopbackCallback : IDisposable
{
    private readonly TcpListener listener;
    private int used;
    public Uri RedirectUri { get; }

    public LoopbackCallback()
    {
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Server.ExclusiveAddressUse = true;
        listener.Start(8);
        RedirectUri = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/oidc/callback");
    }

    public async Task<string> ReceiveAsync(string expectedState, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref used, 1) != 0) throw new InvalidOperationException("Callback already consumed.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            while (true)
            {
                using var socket = await listener.AcceptTcpClientAsync(limit.Token);
                using var stream = socket.GetStream();
                using var requestLimit = CancellationTokenSource.CreateLinkedTokenSource(limit.Token);
                requestLimit.CancelAfter(TimeSpan.FromSeconds(3));
                var bytes = new List<byte>();
                var one = new byte[1];
                while (bytes.Count < 8192 && await stream.ReadAsync(one, requestLimit.Token) == 1)
                {
                    bytes.Add(one[0]);
                    if (bytes.Count >= 4 && Encoding.ASCII.GetString(bytes.TakeLast(4).ToArray()) == "\r\n\r\n") break;
                }
                var lines = Encoding.ASCII.GetString(bytes.ToArray()).Split("\r\n");
                var first = lines[0].Split(' ');
                Uri? target = null;
                var valid = first.Length == 3 && first[0] == "GET" && first[1].StartsWith("/oidc/callback?", StringComparison.Ordinal) &&
                    Uri.TryCreate(RedirectUri, first[1], out target) && target.AbsolutePath == RedirectUri.AbsolutePath &&
                    lines.Count(l => l.Equals("Host: " + RedirectUri.Authority, StringComparison.OrdinalIgnoreCase)) == 1;
                if (valid)
                {
                    var query = ParseQuery(target!.Query);
                    valid = query.TryGetValue("state", out var state) && state == expectedState &&
                        (query.ContainsKey("code") ^ query.ContainsKey("error"));
                }
                var response = valid ? "200 OK" : "400 Bad Request";
                var body = valid ? "Sign-in received. Return to Tiket Desktop." : "Invalid sign-in callback.";
                var output = Encoding.ASCII.GetBytes($"HTTP/1.1 {response}\r\nContent-Type: text/plain\r\nCache-Control: no-store\r\nContent-Security-Policy: default-src 'none'\r\nConnection: close\r\nContent-Length: {body.Length}\r\n\r\n{body}");
                await stream.WriteAsync(output, limit.Token);
                if (valid) return target!.AbsoluteUri;
            }
        }
        finally { Dispose(); }
    }

    public static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2 || !result.TryAdd(Uri.UnescapeDataString(parts[0]), Uri.UnescapeDataString(parts[1].Replace("+", " "))))
                return new Dictionary<string, string>();
        }
        return result;
    }

    public void Dispose() => listener.Stop();
}
