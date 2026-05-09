using ToxVoice.DiscordRelay.Configuration;

namespace ToxVoice.DiscordRelay.Forwarding;

public sealed class WebhookRoute
{
    private long _counter = -1;

    public WebhookRoute(string name, IReadOnlyList<WebhookTarget> targets)
    {
        Name = name;
        Targets = targets;
    }

    public string Name { get; }
    public IReadOnlyList<WebhookTarget> Targets { get; }

    public int NextStartIndex()
    {
        var raw = Interlocked.Increment(ref _counter) & long.MaxValue;
        return (int)(raw % Targets.Count);
    }

    public WebhookTarget GetAt(int index)
    {
        var i = ((index % Targets.Count) + Targets.Count) % Targets.Count;
        return Targets[i];
    }
}
