using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using ToxVoice.DiscordRelay.Configuration;

namespace ToxVoice.DiscordRelay.Forwarding;

public sealed class OutboundHttpClientPool : IDisposable
{
    private readonly ConcurrentDictionary<string, HttpClient> _clientsByIp = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _defaultClient;
    private readonly TimeSpan _timeout;
    private readonly ILogger<OutboundHttpClientPool> _logger;

    public OutboundHttpClientPool(IOptions<RelayOptions> options, ILogger<OutboundHttpClientPool> logger)
    {
        _logger = logger;
        _timeout = TimeSpan.FromSeconds(Math.Max(1, options.Value.RequestTimeoutSeconds));
        _defaultClient = new HttpClient { Timeout = _timeout };
    }

    public (HttpClient Client, string Label) GetClient(string? outboundIp)
    {
        // Empty/missing or wildcard addresses (0.0.0.0 / ::) = let the OS pick
        // the source IP via the routing table (no explicit Socket.Bind needed).
        if (string.IsNullOrWhiteSpace(outboundIp) || outboundIp is "0.0.0.0" or "::")
            return (_defaultClient, "os-default");

        var client = _clientsByIp.GetOrAdd(outboundIp, BuildClient);
        return (client, outboundIp);
    }

    private HttpClient BuildClient(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address))
        {
            _logger.LogWarning("Invalid OutboundIp '{Ip}', falling back to OS default for this target.", ip);
            return _defaultClient;
        }

        var endpoint = new IPEndPoint(address, 0);
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    socket.Bind(endpoint);
                    await socket.ConnectAsync(context.DnsEndPoint, cancellationToken).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
        return new HttpClient(handler) { Timeout = _timeout };
    }

    public void Dispose()
    {
        _defaultClient.Dispose();
        foreach (var client in _clientsByIp.Values)
            client.Dispose();
    }
}
