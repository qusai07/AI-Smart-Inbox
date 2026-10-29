using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SmartInbox.Application;

namespace SmartInbox.Infrastructure.Webhooks;

/// <summary>
/// Blocks webhook requests to addresses inside the network (SSRF): loopback, private, link-local (incl. cloud
/// metadata at 169.254.169.254), CGNAT, multicast and their IPv6 equivalents. Checked on the resolved IP at
/// connect time, so DNS names that point inside (or DNS rebinding) are caught too.
/// </summary>
public static class SsrfGuard
{
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0                                     // 0.0.0.0/8 "this network"
                     || b[0] == 10                                 // 10/8 private
                     || b[0] == 127                                // loopback
                     || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) // 100.64/10 carrier-grade NAT
                     || (b[0] == 169 && b[1] == 254)               // link-local, cloud metadata
                     || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)  // 172.16/12 private
                     || (b[0] == 192 && b[1] == 0 && b[2] == 0)    // 192.0.0/24 IETF protocol assignments
                     || (b[0] == 192 && b[1] == 168)               // 192.168/16 private
                     || (b[0] == 198 && (b[1] == 18 || b[1] == 19)) // 198.18/15 benchmarking
                     || b[0] >= 224);                              // multicast, reserved, broadcast
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (IPAddress.IPv6Loopback.Equals(address) || IPAddress.IPv6None.Equals(address)) return false;
            var b = address.GetAddressBytes();
            return !(address.IsIPv6LinkLocal                       // fe80::/10
                     || address.IsIPv6SiteLocal                    // fec0::/10 (deprecated)
                     || address.IsIPv6Multicast                    // ff00::/8
                     || (b[0] & 0xfe) == 0xfc                      // fc00::/7 unique local
                     || (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b)); // 64:ff9b::/96 NAT64 can reach IPv4 inside
        }

        return false;
    }

    /// <summary>SocketsHttpHandler.ConnectCallback that refuses to connect to non-public addresses.</summary>
    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> ConnectCallback(bool allowPrivateNetworks) =>
        async (context, ct) =>
        {
            var host = context.DnsEndPoint.Host;
            var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct);
            // Every address must be public: otherwise a name with one public and one private record could slip through.
            if (!allowPrivateNetworks && addresses.FirstOrDefault(a => !IsPublic(a)) is { } blocked)
                throw new HttpRequestException($"Blocked: {host} resolves to a non-public address ({blocked}).");

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };
}

/// <summary>POSTs a webhook with the "webhooks" HttpClient (SSRF guard, 10 s timeout, no redirects, no proxy).</summary>
public class HttpWebhookTransport(IHttpClientFactory clients) : IWebhookTransport
{
    public const string ClientName = "webhooks";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<WebhookResponse> SendAsync(WebhookRequest request, CancellationToken ct = default)
    {
        var client = clients.CreateClient(ClientName);
        using var message = new HttpRequestMessage(HttpMethod.Post, request.Url)
        {
            Content = new StringContent(request.Body, Encoding.UTF8, "application/json"),
        };
        foreach (var (name, value) in request.Headers) message.Headers.TryAddWithoutValidation(name, value);

        var watch = Stopwatch.StartNew();
        try
        {
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
            var snippet = await ReadSnippetAsync(response, ct);
            var code = (int)response.StatusCode;
            var error = code switch
            {
                >= 200 and < 300 => null,
                >= 300 and < 400 => $"HTTP {code}: redirects are not followed. Use the final URL.",
                _ => $"HTTP {code}",
            };
            return new WebhookResponse(code, snippet, error, (int)watch.ElapsedMilliseconds);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new WebhookResponse(null, null, $"Timed out after {Timeout.TotalSeconds:0} s.", (int)watch.ElapsedMilliseconds);
        }
        catch (HttpRequestException ex)
        {
            var reason = ex.InnerException is HttpRequestException inner ? inner.Message : ex.Message;
            return new WebhookResponse(null, null, reason, (int)watch.ElapsedMilliseconds);
        }
    }

    /// <summary>At most 1 KB of the response body: enough to debug, never a large or sensitive dump.</summary>
    private static async Task<string?> ReadSnippetAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[WebhookRules.MaxResponseSnippet];
        var read = 0;
        while (read < buffer.Length && await stream.ReadAsync(buffer.AsMemory(read), ct) is var n and > 0) read += n;
        return read == 0 ? null : Encoding.UTF8.GetString(buffer, 0, read);
    }
}
