namespace ToxVoice.DiscordRelay.Configuration;

public sealed class WebhookTarget
{
    public string? OutboundIp { get; set; }
    public string Url { get; set; } = string.Empty;
}
