# ToxVoice Discord Relay

A small local service that forwards Discord webhook posts from the
[ToxVoice](https://toxvoice.com) Rust plugin to Discord. It accepts
messages on a localhost endpoint, queues them, and delivers them via
one or more configured Discord webhooks — each optionally bound to a
specific outbound IP.

## Why

**Discord rate-limits are per webhook** (`webhook_id + token`),
typically ~5 requests / 2 seconds. Adding more outbound IPs alone does
**not** help — the rate-limit bucket lives on the webhook side. You
need multiple webhooks to get parallel rate-limit budgets.

**Cloudflare's invalid-request limit (10K per 10 min) is per source
IP.** Binding each webhook to its own outbound IP isolates ban risk:
a banned IP only takes its own webhook offline.

The relay gives you both — multiple webhooks fed from a shared queue,
each handled by a dedicated worker bound to its own outbound IP.

## How it works

```
[POST /relay/<route>]  →  202 Accepted (immediately)
       │
       ▼
   [Channel<RelayedMessage>]   one queue per route name
       │
       │   workers compete for messages
       │
   ┌───┼─────────────────────┬─────────────────────┐
   ▼                         ▼                     ▼
[Worker: hookA, IP1]   [Worker: hookB, IP2]   [Worker: hookC, IP3]
 read msg               read msg               read msg
 try send to Discord    try send to Discord    try send to Discord
 ↓ on 429: sleep,       ↓ on 429: sleep,       ↓ on 429: sleep,
   retry SAME msg         retry SAME msg         retry SAME msg
 ↓ on 5xx: backoff,     ↓ on 5xx: backoff,     ↓ on 5xx: backoff,
   retry SAME msg         retry SAME msg         retry SAME msg
 ↓ on 404: worker dies, others continue
 ↓ on other 4xx: log + drop, take next msg
```

A rate-limited worker naturally stops pulling from the queue while it
sleeps — the other workers keep delivering. Round-robin emerges from
worker availability, not from explicit rotation.

## Features

- **One worker per `(webhook, IP)` target**, all consuming a shared
  per-route channel.
- **No dropped messages on rate-limit.** A 429 response causes the
  worker to sleep for the `Retry-After` (or `X-RateLimit-Reset-After`)
  duration and retry the same message.
- **Per-target failure modes:**
  - `2xx` → delivered, take next
  - `429` → respect rate-limit, retry same
  - `5xx` / network errors → exponential backoff (1s → 60s cap), retry same
  - `404` → webhook deleted at Discord; worker stops permanently
  - other `4xx` → log error, drop message, take next
- **Single self-contained binary**, no .NET runtime required.
- **Cross-platform** (Windows + Linux).
- **Logs every event to disk** (rolling daily, 7-day retention).

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
       { "OutboundIp": "203.0.113.10", "Url": "https://discord.com/api/webhooks/<id1>/<token1>" },
       { "OutboundIp": "203.0.113.11", "Url": "https://discord.com/api/webhooks/<id2>/<token2>" },
       { "OutboundIp": "203.0.113.12", "Url": "https://discord.com/api/webhooks/<id3>/<token3>" }
     ],
     "alerts": [
       { "OutboundIp": "0.0.0.0", "Url": "https://discord.com/api/webhooks/<id4>/<token4>" }
     ]
   }
   ```
   - `OutboundIp` — local IPv4/IPv6. Use `"0.0.0.0"` (or omit the field) to let the OS pick via the default route.
   - `Url` — required Discord webhook URL.
3. Start the relay:
   - Windows: double-click `ToxVoiceRelay.exe`
   - Linux: `./toxvoice-relay`
4. Point the ToxVoice plugin at the relay:
   ```
   Webhook URL: http://127.0.0.1:8787/relay/voice
   ```

See `README.txt` shipped with the binary for the full reference.

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
