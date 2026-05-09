using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using ToxVoice.DiscordRelay.Configuration;

namespace ToxVoice.DiscordRelay.Forwarding;

public sealed class OutboundHttpClientPool : IDisposable
{
    private readonly IReadOnlyList<HttpClient> _clients;
    private readonly IReadOnlyList<string> _labels;
    private long _nextIndex = -1;

    public OutboundHttpClientPool(IOptions<RelayOptions> options, ILogger<OutboundHttpClientPool> logger)
    {
        var relayOptions = options.Value;
        var timeout = TimeSpan.FromSeconds(Math.Max(1, relayOptions.RequestTimeoutSeconds));

        var clients = new List<HttpClient>();
        var labels = new List<string>();

        if (relayOptions.OutboundIps.Count == 0)
        {
            clients.Add(new HttpClient { Timeout = timeout });
            labels.Add("os-default");
            logger.LogInformation("No OutboundIps configured — using OS default routing.");
        }
        else
        {
            foreach (var ip in relayOptions.OutboundIps)
            {
                if (!IPAddress.TryParse(ip, out var address))
                {
                    logger.LogWarning("Ignoring invalid OutboundIp: {Ip}", ip);
                    continue;
                }

                clients.Add(BuildClient(address, timeout));
                labels.Add(ip);
            }

            if (clients.Count == 0)
            {
                logger.LogWarning("All OutboundIps were invalid — falling back to OS default routing.");
                clients.Add(new HttpClient { Timeout = timeout });
                labels.Add("os-default");
            }
        }

        _clients = clients;
        _labels = labels;
        logger.LogInformation("Outbound HTTP clients configured: [{Ips}]", string.Join(", ", labels));
    }

    public int Count => _clients.Count;

    public int NextStartIndex()
    {
        var raw = Interlocked.Increment(ref _nextIndex) & long.MaxValue;
        return (int)(raw % _clients.Count);
    }

    public (HttpClient Client, string Label) GetAt(int index)
    {
        var i = ((index % _clients.Count) + _clients.Count) % _clients.Count;
        return (_clients[i], _labels[i]);
    }

    public void Dispose()
    {
        foreach (var client in _clients)
            client.Dispose();
    }

    private static HttpClient BuildClient(IPAddress localAddress, TimeSpan timeout)
    {
        var endpoint = new IPEndPoint(localAddress, 0);
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

        return new HttpClient(handler) { Timeout = timeout };
    }
}
