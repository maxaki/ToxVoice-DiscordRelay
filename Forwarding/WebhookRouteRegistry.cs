using Microsoft.Extensions.Options;
using ToxVoice.DiscordRelay.Configuration;

namespace ToxVoice.DiscordRelay.Forwarding;

public sealed class WebhookRouteRegistry
{
    private readonly Dictionary<string, WebhookRoute> _routes;

    public WebhookRouteRegistry(IOptions<RelayOptions> options, ILogger<WebhookRouteRegistry> logger)
    {
        _routes = new Dictionary<string, WebhookRoute>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, targets) in options.Value.Webhooks)
        {
            var validTargets = targets
                .Where(t => Uri.TryCreate(t.Url, UriKind.Absolute, out _))
                .ToList();

            if (validTargets.Count == 0)
            {
                logger.LogWarning("Webhook '{Name}' has no valid targets — skipping.", name);
                continue;
            }

            _routes[name] = new WebhookRoute(name, validTargets);
            logger.LogInformation(
                "Webhook '{Name}' configured with {Count} target(s).", name, validTargets.Count);
        }
    }

    public bool TryGet(string name, out WebhookRoute route) => _routes.TryGetValue(name, out route!);

    public IReadOnlyCollection<string> Names => _routes.Keys;
}
