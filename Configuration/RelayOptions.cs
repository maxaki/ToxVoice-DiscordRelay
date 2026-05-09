namespace ToxVoice.DiscordRelay.Configuration;

public sealed class RelayOptions
{
    public const string SectionName = "Relay";

    public int Port { get; set; } = 8787;
    public string BindAddress { get; set; } = "127.0.0.1";
    public Dictionary<string, List<WebhookTarget>> Webhooks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
