namespace ToxVoice.DiscordRelay.Forwarding;

public sealed class RelayedMessage
{
    public required byte[] Body { get; init; }
    public required string ContentType { get; init; }
    public required DateTimeOffset EnqueuedAt { get; init; }
}
