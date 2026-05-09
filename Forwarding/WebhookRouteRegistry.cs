using System.Collections.Frozen;
using Microsoft.Extensions.Options;
using ToxVoice.DiscordRelay.Configuration;

namespace ToxVoice.DiscordRelay.Forwarding;

public sealed class WebhookRouteRegistry
{
    private readonly FrozenDictionary<string, WebhookRoute> _routes;

    public WebhookRouteRegistry(IOptions<RelayOptions> options, ILogger<WebhookRouteRegistry> logger)
    {
        var routes = new Dictionary<string, WebhookRoute>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, targets) in options.Value.Webhooks)
        {
            var validTargets = new List<RuntimeTarget>(targets.Count);
            foreach (var target in targets)
            {
                if (Uri.TryCreate(target.Url, UriKind.Absolute, out var uri))
                    validTargets.Add(new RuntimeTarget(target, uri));
            }

            if (validTargets.Count == 0)
            {
                logger.LogWarning("Webhook '{Name}' has no valid targets — skipping.", name);
                continue;
            }

            routes[name] = new WebhookRoute(name, validTargets);
            logger.LogInformation(
                "Webhook '{Name}' configured with {Count} target(s).", name, validTargets.Count);
        }

        _routes = routes.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGet(string name, out WebhookRoute route) => _routes.TryGetValue(name, out route!);

    public IReadOnlyCollection<string> Names => _routes.Keys;
    public IReadOnlyCollection<WebhookRoute> Routes => _routes.Values;
}
