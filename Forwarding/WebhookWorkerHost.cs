using Microsoft.Extensions.Hosting;

namespace ToxVoice.DiscordRelay.Forwarding;

public sealed class WebhookWorkerHost : IHostedService
{
    private readonly WebhookRouteRegistry _registry;
    private readonly OutboundHttpClientPool _pool;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<WebhookWorkerHost> _logger;
    private readonly List<Task> _workerTasks = new();
    private CancellationTokenSource? _shutdownCts;

    public WebhookWorkerHost(
        WebhookRouteRegistry registry,
        OutboundHttpClientPool pool,
        ILoggerFactory loggerFactory,
        ILogger<WebhookWorkerHost> logger)
    {
        _registry = registry;
        _pool = pool;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _shutdownCts = new CancellationTokenSource();

        var totalWorkers = 0;
        foreach (var route in _registry.Routes)
        {
            for (var i = 0; i < route.Targets.Count; i++)
            {
                var workerLogger = _loggerFactory.CreateLogger($"Worker[{route.Name}#{i}]");
                var worker = new WebhookWorker(
                    route.Name,
                    i,
                    route.Targets[i],
                    _pool,
                    route.Reader,
                    workerLogger);

                _workerTasks.Add(worker.RunAsync(_shutdownCts.Token));
                totalWorkers++;
            }
        }

        _logger.LogInformation(
            "Started {Count} webhook worker(s) across {Routes} route(s).", totalWorkers, _registry.Routes.Count);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_shutdownCts is null)
            return;

        _shutdownCts.Cancel();

        foreach (var route in _registry.Routes)
            route.Complete();

        try
        {
            await Task.WhenAll(_workerTasks).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Forced shutdown — workers may still be running, host has been told to stop.
        }

        _shutdownCts.Dispose();
        _logger.LogInformation("All webhook workers stopped.");
    }
}
