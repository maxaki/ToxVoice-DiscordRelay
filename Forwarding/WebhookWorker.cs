using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace ToxVoice.DiscordRelay.Forwarding;

public sealed class WebhookWorker
{
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DefaultRateLimitBackoff = TimeSpan.FromSeconds(5);

    private readonly string _routeName;
    private readonly int _index;
    private readonly RuntimeTarget _target;
    private readonly OutboundHttpClientPool _pool;
    private readonly ChannelReader<RelayedMessage> _reader;
    private readonly ILogger _logger;

    public WebhookWorker(
        string routeName,
        int index,
        RuntimeTarget target,
        OutboundHttpClientPool pool,
        ChannelReader<RelayedMessage> reader,
        ILogger logger)
    {
        _routeName = routeName;
        _index = index;
        _target = target;
        _pool = pool;
        _reader = reader;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var disposition = await DeliverWithRetryAsync(message, cancellationToken).ConfigureAwait(false);

                if (disposition == Disposition.WorkerDead)
                {
                    _logger.LogError(
                        "Worker for webhook '{Name}' target {Index} ({Url}) is permanently disabled. Other workers continue handling the queue.",
                        _routeName, _index, _target.Uri);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown — nothing to do.
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex,
                "Worker for webhook '{Name}' target {Index} crashed unexpectedly and will not deliver further messages.",
                _routeName, _index);
        }
    }

    private async Task<Disposition> DeliverWithRetryAsync(RelayedMessage message, CancellationToken cancellationToken)
    {
        var backoff = InitialBackoff;

        while (!cancellationToken.IsCancellationRequested)
        {
            var (client, ipLabel) = _pool.GetClient(_target.Config.OutboundIp);

            try
            {
                using var content = new ByteArrayContent(message.Body);
                content.Headers.TryAddWithoutValidation("Content-Type", message.ContentType);

                using var response = await client.PostAsync(_target.Uri, content, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug(
                        "Delivered message via webhook '{Name}' target {Index} (IP {Ip}, queue-age {AgeMs}ms).",
                        _routeName, _index, ipLabel, (int)(DateTimeOffset.UtcNow - message.EnqueuedAt).TotalMilliseconds);
                    return Disposition.Delivered;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = ParseRetryAfter(response);
                    _logger.LogWarning(
                        "Webhook '{Name}' target {Index} rate-limited via {Ip}; sleeping {Seconds:F1}s before retrying same message.",
                        _routeName, _index, ipLabel, retryAfter.TotalSeconds);
                    await Task.Delay(retryAfter, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogError(
                        "Webhook '{Name}' target {Index} returned 404 — webhook deleted at Discord. Dropping message and stopping worker.",
                        _routeName, _index);
                    return Disposition.WorkerDead;
                }

                if ((int)response.StatusCode is >= 400 and < 500)
                {
                    _logger.LogError(
                        "Webhook '{Name}' target {Index} returned {Status} via {Ip} — non-retriable, dropping message.",
                        _routeName, _index, response.StatusCode, ipLabel);
                    return Disposition.Dropped;
                }

                if ((int)response.StatusCode is >= 500 and < 600)
                {
                    _logger.LogWarning(
                        "Webhook '{Name}' target {Index} returned {Status} via {Ip} — backing off {Seconds}s and retrying.",
                        _routeName, _index, response.StatusCode, ipLabel, backoff.TotalSeconds);
                    await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
                    backoff = NextBackoff(backoff);
                    continue;
                }

                _logger.LogWarning(
                    "Webhook '{Name}' target {Index} returned unexpected status {Status} via {Ip} — dropping message.",
                    _routeName, _index, response.StatusCode, ipLabel);
                return Disposition.Dropped;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsTransientNetworkError(ex))
            {
                _logger.LogWarning(ex,
                    "Webhook '{Name}' target {Index} hit transient network error via {Ip} — backing off {Seconds}s and retrying.",
                    _routeName, _index, ipLabel, backoff.TotalSeconds);
                await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
                backoff = NextBackoff(backoff);
            }
        }

        return Disposition.Cancelled;
    }

    private static TimeSpan ParseRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Reset-After", out var resetAfterValues) &&
            double.TryParse(resetAfterValues.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var resetSeconds))
        {
            return TimeSpan.FromSeconds(resetSeconds);
        }

        if (response.Headers.RetryAfter is { } retryAfter)
        {
            if (retryAfter.Delta is { } delta)
                return delta;
            if (retryAfter.Date is { } date)
            {
                var diff = date - DateTimeOffset.UtcNow;
                return diff > TimeSpan.Zero ? diff : DefaultRateLimitBackoff;
            }
        }

        return DefaultRateLimitBackoff;
    }

    private static TimeSpan NextBackoff(TimeSpan current)
    {
        var next = TimeSpan.FromTicks(current.Ticks * 2);
        return next > MaxBackoff ? MaxBackoff : next;
    }

    private static bool IsTransientNetworkError(Exception ex) =>
        ex is HttpRequestException or SocketException or IOException or TaskCanceledException;

    private enum Disposition
    {
        Delivered,
        Dropped,
        WorkerDead,
        Cancelled
    }
}
