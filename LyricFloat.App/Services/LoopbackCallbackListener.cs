using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LyricFloat.App.Services;

internal sealed class LoopbackCallbackListener : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 43821);

    public void Start()
    {
        try
        {
            _listener.Server.ExclusiveAddressUse = true;
            _listener.Start(4);
        }
        catch (SocketException)
        {
            throw new InvalidOperationException("Cannot open Spotify callback port 43821. Close the app using that port and reconnect.");
        }
    }

    public async Task ReceiveAsync(Func<Dictionary<string, string>, Task> authorize, CancellationToken cancellationToken)
    {
        while (true)
        {
            using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            var stream = client.GetStream();
            string request;
            try { request = await ReadHeadersAsync(stream, requestTimeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { continue; }
            catch (IOException) { continue; }
            var lines = request.Split("\r\n");
            var first = lines[0].Split(' ');
            if (first.Length != 3 || first[0] != "GET" || !first[1].StartsWith("/callback?", StringComparison.Ordinal)
                || !lines.Any(l => l.Equals("Host: 127.0.0.1:43821", StringComparison.OrdinalIgnoreCase)))
            {
                await RespondAsync(stream, false, cancellationToken);
                continue;
            }
            try
            {
                var query = ParseQuery(first[1][(first[1].IndexOf('?') + 1)..]);
                await authorize(query);
            }
            catch
            {
                await RespondAsync(stream, false, cancellationToken);
                throw;
            }
            await RespondAsync(stream, true, cancellationToken);
            return;
        }
    }

    internal static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.Split('&'))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2 || !values.TryAdd(Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1].Replace('+', ' '))))
                throw new InvalidOperationException("Invalid OAuth callback parameters.");
        }
        return values;
    }

    private static async Task<string> ReadHeadersAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        // Small bounded request reader: never consume an unbounded callback or request body.
        var bytes = new byte[8192];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(count, 1), cancellationToken);
            if (read == 0) throw new IOException("Incomplete callback.");
            count += read;
            if (count >= 4 && bytes[count - 4] == 13 && bytes[count - 3] == 10 && bytes[count - 2] == 13 && bytes[count - 1] == 10)
                return Encoding.ASCII.GetString(bytes, 0, count);
        }
        throw new IOException("Callback headers too large.");
    }

    private static async Task RespondAsync(NetworkStream stream, bool success, CancellationToken cancellationToken)
    {
        var body = success ? "LyricFloat connected successfully. You can close this browser tab and return to LyricFloat."
            : "Spotify connection failed. You can close this browser tab.";
        var response = $"HTTP/1.1 {(success ? "200 OK" : "400 Bad Request")}\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}";
        try { await stream.WriteAsync(Encoding.UTF8.GetBytes(response), cancellationToken); }
        catch (IOException) { AppLog.Write("OAuth browser response could not be delivered."); }
    }

    public void Dispose() => _listener.Stop();
}
