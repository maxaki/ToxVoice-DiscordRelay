using System.Net;
using System.Net.Sockets;

namespace ToxVoice.DiscordRelay.Forwarding;

public sealed class DiscordForwarder
{
    private readonly OutboundHttpClientPool _pool;
    private readonly ILogger<DiscordForwarder> _logger;

    public DiscordForwarder(OutboundHttpClientPool pool, ILogger<DiscordForwarder> logger)
    {
        _pool = pool;
        _logger = logger;
    }

    public async Task<DiscordForwardResult> ForwardAsync(
        Uri target,
        byte[] body,
        string contentType,
        CancellationToken cancellationToken)
    {
        var startIndex = _pool.NextStartIndex();
        var clientCount = _pool.Count;
        Exception? lastException = null;

        for (var attempt = 0; attempt < clientCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (client, label) = _pool.GetAt(startIndex + attempt);

            try
            {
                using var content = new ByteArrayContent(body);
                content.Headers.TryAddWithoutValidation("Content-Type", contentType);

                using var response = await client.PostAsync(target, content, cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                var responseContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";

                if (response.IsSuccessStatusCode || !ShouldFailover(response.StatusCode))
                {
                    return new DiscordForwardResult
                    {
                        StatusCode = response.StatusCode,
                        Body = responseBody,
                        ContentType = responseContentType,
                        OutboundIp = label,
                        Attempts = attempt + 1,
                        AllIpsExhausted = false
                    };
                }

                _logger.LogWarning(
                    "Discord returned {StatusCode} via outbound {Ip} (attempt {Attempt}/{Total}). Failing over to next IP.",
                    (int)response.StatusCode, label, attempt + 1, clientCount);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsTransientNetworkError(ex))
            {
                lastException = ex;
                _logger.LogWarning(ex,
                    "Network error forwarding via outbound {Ip} (attempt {Attempt}/{Total}). Failing over to next IP.",
                    label, attempt + 1, clientCount);
            }
        }

        _logger.LogError(lastException,
            "All {Count} outbound IP(s) exhausted forwarding to {Target}.", clientCount, target);

        return new DiscordForwardResult
        {
            StatusCode = HttpStatusCode.BadGateway,
            Body = Array.Empty<byte>(),
            ContentType = "application/json",
            OutboundIp = "all-exhausted",
            Attempts = clientCount,
            AllIpsExhausted = true
        };
    }

    private static bool ShouldFailover(HttpStatusCode statusCode)
    {
        var status = (int)statusCode;
        return status >= 500 && status < 600;
    }

    private static bool IsTransientNetworkError(Exception ex) => ex is HttpRequestException or SocketException or IOException or TaskCanceledException;
}
