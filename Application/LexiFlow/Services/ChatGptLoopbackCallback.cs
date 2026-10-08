using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LexiFlow.Services;

internal sealed class ChatGptLoopbackCallback : IDisposable
{
    private readonly TcpListener _listener;
    private int _consumed;
    public Uri RedirectUri { get; }

    public ChatGptLoopbackCallback()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Server.ExclusiveAddressUse = true;
        _listener.Start(4);
        RedirectUri = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/auth/callback");
    }

    public async Task<(string Code, string ClientId)> ReceiveAsync(string expectedState, string? existingClientId, CancellationToken ct)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lifetime.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            for (var attempt = 0; attempt < 32; attempt++)
            {
                using var client = await _listener.AcceptTcpClientAsync(lifetime.Token);
                if (client.Client.RemoteEndPoint is not IPEndPoint remote || !remote.Address.Equals(IPAddress.Loopback)) continue;
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                using var stream = client.GetStream();
                string request;
                try { request = await ReadHeadersAsync(stream, requestTimeout.Token); }
                catch (OperationCanceledException) when (!lifetime.IsCancellationRequested) { continue; }
                catch (InvalidDataException) { await ReplyAsync(stream, false, lifetime.Token); continue; }
                var lines = request.Split("\r\n", StringSplitOptions.None);
                var first = lines[0].Split(' ');
                if (first.Length != 3 || first[0] != "GET" || first[2] != "HTTP/1.1"
                    || !first[1].StartsWith("/auth/callback?", StringComparison.Ordinal))
                {
                    await ReplyAsync(stream, false, lifetime.Token);
                    continue;
                }
                var hosts = lines.Skip(1).Where(x => x.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (hosts.Length != 1 || !string.Equals(hosts[0][5..].Trim(), RedirectUri.Authority, StringComparison.Ordinal)
                    || lines.Skip(1).Any(x => x.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase)
                        || x.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)))
                {
                    await ReplyAsync(stream, false, lifetime.Token);
                    continue;
                }
                if (Interlocked.Exchange(ref _consumed, 1) != 0) throw new ChatGptException(ChatGptFailureKind.InvalidResponse);
                try
                {
                    var result = ParseCallback(first[1], expectedState, existingClientId);
                    await ReplyAsync(stream, true, lifetime.Token);
                    return result;
                }
                catch
                {
                    await ReplyAsync(stream, false, lifetime.Token);
                    throw;
                }
            }
            throw new ChatGptException(ChatGptFailureKind.InvalidResponse);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new ChatGptException(ChatGptFailureKind.SignInCancelled); }
        catch (ChatGptException) { throw; }
        catch { throw new ChatGptException(ChatGptFailureKind.Unavailable); }
        finally { _listener.Stop(); }
    }

    internal static (string Code, string ClientId) ParseCallback(string target, string expectedState, string? existingClientId)
    {
        if (!target.StartsWith("/auth/callback?", StringComparison.Ordinal) || target.Length > 8192 || target.Contains('#'))
            throw new ChatGptException(ChatGptFailureKind.InvalidResponse);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var pair in target[(target.IndexOf('?') + 1)..].Split('&'))
            {
                var pieces = pair.Split('=', 2);
                if (pieces.Length != 2) throw new InvalidDataException();
                var key = Uri.UnescapeDataString(pieces[0].Replace('+', ' '));
                var value = Uri.UnescapeDataString(pieces[1].Replace('+', ' '));
                if (key.Length > 100 || value.Length > 4096 || key.Any(char.IsControl) || value.Any(char.IsControl)
                    || !values.TryAdd(key, value)) throw new InvalidDataException();
            }
        }
        catch { throw new ChatGptException(ChatGptFailureKind.InvalidResponse); }
        if (!values.TryGetValue("state", out var state) || !ChatGptOAuthClient.FixedEquals(expectedState, state))
            throw new ChatGptException(ChatGptFailureKind.InvalidResponse);
        if (values.ContainsKey("error")) throw new ChatGptException(ChatGptFailureKind.SignInCancelled);
        if (!values.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            throw new ChatGptException(ChatGptFailureKind.InvalidResponse);
        values.TryGetValue("client_id", out var returnedClientId);
        var clientId = existingClientId ?? returnedClientId;
        if (!ChatGptOAuthClient.IsIssuedClientId(clientId)
            || (existingClientId is not null && returnedClientId is not null && returnedClientId != existingClientId))
            throw new ChatGptException(ChatGptFailureKind.InvalidResponse);
        return (code, clientId!);
    }

    private static async Task<string> ReadHeadersAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[12288];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count, Math.Min(1024, buffer.Length - count)), ct);
            if (read == 0) throw new InvalidDataException();
            count += read;
            for (var i = 0; i < count; i++)
                if (buffer[i] > 127 || (buffer[i] < 32 && buffer[i] is not 9 and not 10 and not 13)) throw new InvalidDataException();
            var text = Encoding.ASCII.GetString(buffer, 0, count);
            var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end >= 0)
            {
                if (end + 4 != count) throw new InvalidDataException();
                return text[..end];
            }
        }
        throw new InvalidDataException();
    }

    private static async Task ReplyAsync(NetworkStream stream, bool received, CancellationToken ct)
    {
        // No callback values, account info, tokens, scripts or external resources.
        var body = received ? "Sign-in received. Return to LexiFlow to finish." : "This sign-in request could not be verified. Return to LexiFlow.";
        var bytes = Encoding.ASCII.GetBytes($"HTTP/1.1 {(received ? "200 OK" : "400 Bad Request")}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nPragma: no-cache\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'none'; frame-ancestors 'none'\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n{body}");
        try { await stream.WriteAsync(bytes, ct); }
        catch (IOException) { }
    }

    public void Dispose() => _listener.Stop();
}
