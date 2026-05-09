namespace ToxVoice.DiscordRelay.Configuration;

public sealed class WebhookTarget
{
    public string Url { get; set; } = string.Empty;
    public string? OutboundIp { get; set; }
}
