using ToxVoice.DiscordRelay.Configuration;

namespace ToxVoice.DiscordRelay.Forwarding;

public sealed class RuntimeTarget
{
    public RuntimeTarget(WebhookTarget config, Uri uri)
    {
        Config = config;
        Uri = uri;
    }

    public WebhookTarget Config { get; }
    public Uri Uri { get; }
}
