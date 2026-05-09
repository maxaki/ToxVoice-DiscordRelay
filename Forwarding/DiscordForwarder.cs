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
        WebhookRoute route,
        byte[] body,
        string contentType,
        CancellationToken cancellationToken)
    {
        var startIndex = route.NextStartIndex();
        var targetCount = route.Targets.Count;
        Exception? lastException = null;
        HttpStatusCode lastStatus = HttpStatusCode.BadGateway;

        for (var attempt = 0; attempt < targetCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var target = route.GetAt(startIndex + attempt);
            var (client, ipLabel) = _pool.GetClient(target.OutboundIp);

            if (!Uri.TryCreate(target.Url, UriKind.Absolute, out var targetUri))
            {
                _logger.LogWarning(
                    "Webhook '{Name}' target {Index} has invalid URL '{Url}', skipping.",
                    route.Name, attempt, target.Url);
                continue;
            }

            try
            {
                using var content = new ByteArrayContent(body);
                content.Headers.TryAddWithoutValidation("Content-Type", contentType);

                using var response = await client.PostAsync(targetUri, content, cancellationToken).ConfigureAwait(false);
                lastStatus = response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                    var responseContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";

                    return new DiscordForwardResult
                    {
                        StatusCode = response.StatusCode,
                        Body = responseBody,
                        ContentType = responseContentType,
                        OutboundIp = ipLabel,
                        Attempts = attempt + 1,
                        AllTargetsExhausted = false
                    };
                }

                if (!ShouldFailover(response.StatusCode))
                {
                    var responseBody = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                    var responseContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";

                    return new DiscordForwardResult
                    {
                        StatusCode = response.StatusCode,
                        Body = responseBody,
                        ContentType = responseContentType,
                        OutboundIp = ipLabel,
                        Attempts = attempt + 1,
                        AllTargetsExhausted = false
                    };
                }

                _logger.LogWarning(
                    "Webhook '{Name}' target {Index} returned {StatusCode} via {Ip} — failing over to next target.",
                    route.Name, attempt, (int)response.StatusCode, ipLabel);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsTransientNetworkError(ex))
            {
                lastException = ex;
                _logger.LogWarning(ex,
                    "Webhook '{Name}' target {Index} failed via {Ip} — failing over to next target.",
                    route.Name, attempt, ipLabel);
            }
        }

        _logger.LogError(lastException,
            "All {Count} target(s) for webhook '{Name}' exhausted. Last status: {Status}.",
            targetCount, route.Name, (int)lastStatus);

        return new DiscordForwardResult
        {
            StatusCode = lastStatus == HttpStatusCode.BadGateway ? HttpStatusCode.BadGateway : lastStatus,
            Body = Array.Empty<byte>(),
            ContentType = "application/json",
            OutboundIp = "all-exhausted",
            Attempts = targetCount,
            AllTargetsExhausted = true
        };
    }

    private static bool ShouldFailover(HttpStatusCode statusCode)
    {
        var status = (int)statusCode;
        // 429 (rate limited) and 5xx — both benefit from rotating to a different
        // (webhook, IP) pair. 4xx other than 429 are application errors and won't
        // succeed on retry.
        return status == 429 || (status >= 500 && status < 600);
    }

    private static bool IsTransientNetworkError(Exception ex) =>
        ex is HttpRequestException or SocketException or IOException or TaskCanceledException;
}
