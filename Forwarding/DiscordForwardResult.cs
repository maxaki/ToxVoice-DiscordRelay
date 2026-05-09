using System.Net;

namespace ToxVoice.DiscordRelay.Forwarding;

public sealed class DiscordForwardResult
{
    public required HttpStatusCode StatusCode { get; init; }
    public required byte[] Body { get; init; }
    public required string ContentType { get; init; }
    public required string OutboundIp { get; init; }
    public int Attempts { get; init; }
    public bool AllTargetsExhausted { get; init; }
}
