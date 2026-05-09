# ToxVoice Discord Relay

A small local service that forwards Discord webhook posts from the
[ToxVoice](https://toxvoice.com) Rust plugin to Discord. It runs alongside
your Rust dedicated server and:

- **Decouples the plugin from Discord outages** — the plugin POSTs to the
  relay locally and never blocks on Discord network latency.
- **Rotates outbound traffic across multiple local IP addresses** with
  automatic failover when an IP encounters network errors or 5xx.
- **Logs every forward to disk** so you can audit Discord delivery.

## Download

Grab the latest release for your platform from the
[Releases](../../releases) page:

- `ToxVoiceRelay-win-x64.zip` — Windows 10/11 (Server 2019+)
- `ToxVoiceRelay-linux-x64.tar.gz` — Linux x86_64 (glibc)

The binary is **self-contained** — no .NET runtime install required.

## Quick setup

1. Unzip into any folder, e.g. `C:\RustServer\ToxVoiceRelay\`.
2. Open `appsettings.json` and fill in your Discord webhook URL(s):
   ```json
   "Webhooks": {
     "default": "https://discord.com/api/webhooks/<id>/<token>",
     "alerts":  "https://discord.com/api/webhooks/<id>/<token>"
   }
   ```
3. (Optional) Configure outbound IPs for load distribution + failover:
   ```json
   "OutboundIps": [ "203.0.113.10", "203.0.113.11" ]
   ```
4. Start the relay:
   - Windows: double-click `ToxVoiceRelay.exe`
   - Linux: `./toxvoice-relay`
5. Point the ToxVoice plugin at the relay:
   ```
   Webhook URL: http://127.0.0.1:8787/webhook/default
   ```

See `README.txt` shipped with the binary for the full reference.

## Building from source

Requires .NET 10 SDK.

```bash
# Build
dotnet build -c Release

# Self-contained single-file publish
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

## How outbound IP rotation works

When `OutboundIps` is non-empty, the relay creates one `HttpClient` per
listed IP, each with a `SocketsHttpHandler.ConnectCallback` that binds
the outgoing TCP socket to that local address.

For every forwarded request:

1. A round-robin counter picks a starting IP.
2. If the request fails with a transient network error or a 5xx
   response, the relay immediately retries on the next IP in the list.
3. 4xx responses (including 429 rate limits) are returned to the caller
   without rotating — those are application-level issues, not IP issues.

Empty `OutboundIps` (default) uses the OS routing table, i.e. your
primary interface IP.

## License

MIT
