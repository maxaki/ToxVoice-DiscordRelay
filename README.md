# ToxVoice Discord Relay

A small local service that forwards Discord webhook posts from the
[ToxVoice](https://toxvoice.com) Rust plugin to Discord. It runs alongside
your Rust dedicated server and lets you spread traffic across multiple
Discord webhooks, each bound to a specific outbound IP, to **bypass per-webhook
rate limits** and **isolate Cloudflare ban risk**.

## Why

Discord rate-limits are **per webhook** (`webhook_id + token`) — typically
~5 requests / 2 seconds. Adding more outbound IPs alone does **not** help,
because the bucket is on the webhook side. You need multiple webhooks to
get parallel rate-limit budgets.

Cloudflare's invalid-request limit (10K per 10 minutes) is **per source IP**.
If a specific IP is banned, only the webhook bound to it goes offline; the
others keep working. This is the real win from binding `webhook ↔ IP`.

## Features

- **Round-robin across multiple Discord webhooks** per route name, each
  optionally bound to a specific local outbound IP.
- **Automatic failover** on network errors, 5xx, and 429 (rate-limited):
  the relay rotates to the next `(webhook, IP)` target and retries.
- **Single self-contained binary**, no .NET runtime required at the endpoint.
- **Cross-platform**: Windows + Linux from the same source.
- **Logs every forward to disk** (rolling daily, 7-day retention).

## Download

Grab the latest release for your platform from the
[Releases](../../releases) page:

- `ToxVoiceRelay-win-x64.zip` — Windows 10/11 (Server 2019+)
- `ToxVoiceRelay-linux-x64.tar.gz` — Linux x86_64 (glibc)

## Quick setup

1. Unzip into any folder, e.g. `C:\RustServer\ToxVoiceRelay\`.
2. Open `appsettings.json` and add your webhooks:
   ```json
   "Webhooks": {
     "voice": [
       { "Url": "https://discord.com/api/webhooks/<id1>/<token1>", "OutboundIp": "203.0.113.10" },
       { "Url": "https://discord.com/api/webhooks/<id2>/<token2>", "OutboundIp": "203.0.113.11" },
       { "Url": "https://discord.com/api/webhooks/<id3>/<token3>", "OutboundIp": "203.0.113.12" }
     ],
     "alerts": [
       { "Url": "https://discord.com/api/webhooks/<id4>/<token4>" }
     ]
   }
   ```
   - `Url` — required Discord webhook URL.
   - `OutboundIp` — optional. Omit to use the OS default route.
3. Start the relay:
   - Windows: double-click `ToxVoiceRelay.exe`
   - Linux: `./toxvoice-relay`
4. Point the ToxVoice plugin at the relay:
   ```
   Webhook URL: http://127.0.0.1:8787/relay/voice
   ```

See `README.txt` shipped with the binary for the full reference.

## How rotation + failover works

For each request the relay receives:

1. A round-robin counter picks a starting target from the named list.
2. The configured `OutboundIp` is used as the local source IP for the
   POST to Discord.
3. If the request fails with a network error, 5xx, or **429 (rate
   limited)**, the relay immediately rotates to the **next target** and
   retries.
4. Other 4xx (400/401/403/404/...) are returned without rotating —
   those are application-level errors, not transient.

A rate-limited target is automatically skipped to the next
`(webhook, IP)` pair, giving the affected webhook time to recover its
bucket before the round-robin returns to it.

## Building from source

Requires .NET 10 SDK.

```bash
# Build
dotnet build -c Release

# Self-contained single-file publish (Windows)
dotnet publish -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true

# Linux
dotnet publish -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true
```

Output lands in `publish/<rid>/` next to the project.

## License

MIT
