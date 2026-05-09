ToxVoice Discord Relay
======================

A small local service that forwards Discord webhook posts from the ToxVoice
Rust plugin to Discord. It runs alongside your Rust dedicated server and
lets you spread traffic across multiple Discord webhooks, each bound to a
specific outbound IP, to bypass per-webhook rate limits and isolate
Cloudflare ban risk.

How it helps
------------

  * Discord rate-limits are per webhook (webhook_id + token). One webhook
    gives you ~5 requests / 2 seconds. Configure several webhooks under
    the same name and the relay round-robins through them, multiplying
    your effective request budget.
  * Cloudflare blocks abusive source IPs (10K invalid requests / 10 min
    -> temporary IP ban). Binding each webhook to a specific OutboundIp
    means a banned IP only takes its own webhook offline; the others
    keep working.
  * The plugin POSTs locally and never blocks on Discord network latency.


Quick setup
-----------

1. Unzip this folder anywhere. A common location:
     C:\RustServer\ToxVoiceRelay\         (Windows)
     /opt/toxvoice-relay/                 (Linux)

2. Open appsettings.json in any text editor and add your webhooks:

     "Webhooks": {
       "default": [
         { "Url": "https://discord.com/api/webhooks/<id>/<token>",
           "OutboundIp": "203.0.113.10" },
         { "Url": "https://discord.com/api/webhooks/<id>/<token>",
           "OutboundIp": "203.0.113.11" }
       ],
       "alerts": [
         { "Url": "https://discord.com/api/webhooks/<id>/<token>" }
       ]
     }

   Each entry has:
     Url         - the Discord webhook URL (required)
     OutboundIp  - local IPv4/IPv6 to send from (optional; omit to use
                   the OS default route)

   You can name webhook routes anything (e.g. "voice", "moderation").
   The plugin must use the same names in its URL path.

3. Start the relay:

     Windows: double-click ToxVoiceRelay.exe   (or run from cmd)
     Linux:   ./toxvoice-relay

4. Configure the ToxVoice Rust plugin to point at the relay:

     Webhook URL: http://127.0.0.1:8787/webhook/default
                  http://127.0.0.1:8787/webhook/alerts


How rotation + failover works
-----------------------------

For each request the relay receives:

  1. A round-robin counter picks a starting target in the named list.
  2. The configured OutboundIp is used as the local source IP for the
     POST to Discord.
  3. If the request fails with a network error, 5xx, or 429
     (rate limited), the relay immediately rotates to the NEXT target
     in the list and retries.
  4. Other 4xx responses (400/401/403/404/...) are returned without
     rotating - those are application-level errors, not transient
     issues.

So a "rate-limited target" is automatically skipped to the next
(webhook, IP) pair, and the affected webhook gets a moment to recover
its bucket before the round-robin returns to it.


Configuration reference
-----------------------

Port               TCP port the relay listens on (default 8787).
BindAddress        Interface to bind on. Keep "127.0.0.1" for
                   localhost-only. Use "0.0.0.0" only if the relay must
                   be reachable from another machine (then add firewall
                   rules!).

Webhooks           Map of name -> array of (Url, OutboundIp) targets.
                   The plugin uses the name in the relay URL path:
                     POST http://127.0.0.1:8787/webhook/<name>

RequestTimeoutSeconds  Per-request timeout to Discord (default 30).


Health check
------------

  GET http://127.0.0.1:8787/health   -> 200 OK { "status": "ok" }


Logs
----

Logs are written to the logs/ directory next to the executable. Files
roll daily; the last 7 days are kept by default.

When forwarding, response headers include:

  X-Relay-Outbound-Ip:  which local IP was used
  X-Relay-Attempts:     number of targets tried before success/exhaustion


Running as a service
--------------------

For unattended operation, register the executable as a Windows service
(via NSSM or sc.exe) or a systemd unit on Linux. The relay logs to both
console and file so either approach works.


Troubleshooting
---------------

* "Unknown webhook" 404 from the relay
    -> The name in the URL path doesn't match any entry in
       appsettings.json -> Relay.Webhooks. Names are case-insensitive.

* "All N target(s) for webhook 'X' exhausted"
    -> Every configured target failed (network error, 5xx, or 429).
       Check logs/ for which IPs and status codes were returned. If
       Discord is up but specific webhooks 401/403, the URL or token
       is wrong.

* Discord 401 / 403
    -> Webhook URL is wrong or the webhook has been revoked in Discord.

* Discord 404
    -> The webhook was deleted in Discord. Replace the URL in
       appsettings.json.
