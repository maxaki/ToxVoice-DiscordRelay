namespace ToxVoice.DiscordRelay.Configuration;

public sealed class RelayOptions
{
    public const string SectionName = "Relay";

    public int Port { get; set; } = 8787;
    public string BindAddress { get; set; } = "127.0.0.1";
    public List<string> OutboundIps { get; set; } = new();
    public Dictionary<string, string> Webhooks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int RequestTimeoutSeconds { get; set; } = 30;
}
