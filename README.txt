ToxVoice Discord Relay
======================

A small local service that forwards Discord webhook posts from the ToxVoice
Rust plugin to Discord. It runs alongside your Rust dedicated server and:

  * Decouples the plugin from Discord outages (the plugin POSTs locally
    and never blocks on Discord network latency).
  * Rotates outbound traffic across multiple local IP addresses, with
    automatic failover when an IP encounters network errors or 5xx.
  * Logs every forward to disk (logs/) so you can audit Discord delivery.

This service runs on the SAME machine as your Rust dedicated server.
It listens only on localhost by default (no external ports exposed).


Quick setup
-----------

1. Unzip this folder anywhere. A common location:
     C:\RustServer\ToxVoiceRelay\         (Windows)
     /opt/toxvoice-relay/                 (Linux)

2. Open appsettings.json in any text editor and fill in:

     "Webhooks": {
       "default": "https://discord.com/api/webhooks/<id>/<token>",
       "alerts":  "https://discord.com/api/webhooks/<id>/<token>"
     }

   You can rename "default" / "alerts" to anything (e.g. "voice", "moderation")
   and add as many entries as you want. The plugin must use the same names.

3. (Optional) Configure outbound IPs if your machine has multiple public IPs
   and you want load distribution + failover:

     "OutboundIps": [ "203.0.113.10", "203.0.113.11" ]

   Leave the array empty ([]) to use the OS default route.

4. Start the relay:

     Windows: double-click ToxVoiceRelay.exe   (or run from cmd)
     Linux:   ./toxvoice-relay

5. Configure the ToxVoice Rust plugin to point at the relay:

     Webhook URL: http://127.0.0.1:8787/webhook/default
                  http://127.0.0.1:8787/webhook/alerts

   (Replace "default" / "alerts" with whatever names you used in step 2.)


Configuration reference
-----------------------

Port               TCP port the relay listens on (default 8787).
BindAddress        Interface to bind on. Keep "127.0.0.1" for localhost-only.
                   Use "0.0.0.0" only if the relay must be reachable from
                   another machine (then add firewall rules!).

OutboundIps        Optional list of local IPv4/IPv6 addresses to send
                   Discord traffic from. Round-robin per request, with
                   automatic failover to the next IP on network errors
                   or 5xx responses. Leave empty for OS default routing.

Webhooks           Map of name -> Discord webhook URL. The plugin uses
                   the name in the relay URL path:
                     POST http://127.0.0.1:8787/webhook/<name>

RequestTimeoutSeconds  Per-request timeout to Discord (default 30).


Health check
------------

  GET http://127.0.0.1:8787/health   -> 200 OK { "status": "ok" }


Logs
----

Logs are written to the logs/ directory next to the executable. Files
roll daily; the last 7 days are kept by default. Adjust in appsettings.json
under Serilog.WriteTo.

When forwarding succeeds, the response includes:

  X-Relay-Outbound-Ip:  which local IP was used
  X-Relay-Attempts:     number of IPs tried before success


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

* "All N outbound IP(s) exhausted"
    -> Every configured outbound IP failed (network error or 5xx from
       Discord). Check logs/ for the underlying errors. If Discord is
       up but a specific IP fails, that IP may be blocked or misrouted
       on your machine.

* Discord 401 / 403
    -> Webhook URL is wrong or the webhook has been revoked in Discord.

* Discord 404
    -> The webhook was deleted in Discord. Replace the URL in
       appsettings.json.
