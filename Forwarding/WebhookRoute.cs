using System.Threading.Channels;

namespace ToxVoice.DiscordRelay.Forwarding;

public sealed class WebhookRoute
{
    private readonly Channel<RelayedMessage> _channel;

    public WebhookRoute(string name, IReadOnlyList<RuntimeTarget> targets)
    {
        Name = name;
        Targets = targets;
        _channel = Channel.CreateUnbounded<RelayedMessage>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
    }

    public string Name { get; }
    public IReadOnlyList<RuntimeTarget> Targets { get; }
    public ChannelReader<RelayedMessage> Reader => _channel.Reader;

    public bool TryEnqueue(RelayedMessage message) => _channel.Writer.TryWrite(message);

    public void Complete() => _channel.Writer.TryComplete();
}
