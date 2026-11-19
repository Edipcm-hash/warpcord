# WarpCord

**A Discord-only split tunnel over Cloudflare WARP.** No VPS, no account, no full-device VPN — only Discord's traffic leaves through the tunnel; everything else keeps going out directly.

Tested on Windows 11 (build 26200) with sing-box 1.14.4, on a network where Discord is blocked by ISP-level DNS hijacking **and** SNI filtering.

---

## Why I built this

Discord got blocked in Turkey. Every guide told me to install a VPN — but a VPN reroutes *everything*: my CS2 ping doubles, downloads go through another country, and local services break. I wanted the opposite: **leave my connection alone, and tunnel only Discord.**

So I spent an evening measuring what the block actually does instead of guessing, and it turned out the block is much weaker than it looks. This repo is the result.

## What the block actually was

I probed each layer separately (see `tools/diagnose.py`):

| Layer | Result |
|---|---|
| DNS | `discord.com` → `195.175.254.2` (the ISP's block page). Everything else resolved normally. |
| Plain DNS to 1.1.1.1 / 8.8.8.8 / 9.9.9.9 | **Reset.** So "just change your DNS" does nothing — the ISP mangles every UDP/53 packet. |
| DoH (443) / DoT (853) | Worked. Encrypted DNS slipped through untouched. |
| IP block | **None.** TCP 443 to Discord's real Cloudflare IPs connected in 14–40 ms. |
| MITM | **None.** Normal sites served their real certificates (Cloudflare, Google, Sectigo). |
| SNI filter | **This was the block.** Same IP: `SNI=example.com` → TLS 1.3 handshake fine. `SNI=discord.com` → instant TCP reset. |

The block page itself answered with a self-signed cert (`CN=localhost.localdomain`), which is a nice tell: an ISP redirect, not real interception.

So the whole thing boils down to: **poisoned DNS + a box that reset connections whose ClientHello mentions Discord.** No IP blackhole, no deep MITM.

## What I rejected

- **Hosts file / 1.1.1.1** — port 53 is hijacked, and the SNI reset would still kill the connection.
- **A full VPN** — solves it, but reroutes all my traffic. Wrong tool.
- **GoodbyeDPI / Zapret (ClientHello fragmentation)** — I verified it works (splitting the ClientHello into 1–3 byte chunks defeats the DPI), but it's system-wide packet surgery, it doesn't hide my IP, and it's not a tunnel. Kept it as a fallback, not the solution.
- **A VPS + VLESS/Reality** — the most robust option, but it costs money monthly.
- **Public proxies** — unreliable, and I'm not handing my Discord traffic to a stranger.

Cloudflare WARP was the sweet spot: **free, no account, real tunnel, UDP support** (so voice works). I only needed to make it *selective*.

## How it works

```
  Discord.exe ─────┐
                   ├── tun0 (TUN, auto_route) ── sing-box ──┬── WARP (WireGuard) ──▶ Discord
  *.discord.com ───┘                                        └── direct ────────────▶ everything else
```

- **sing-box** creates a TUN device and takes over the default route.
- **Routing rules** decide per flow: Discord by process name *and* by domain suffix → `warp`; everything else → `direct` (the default final).
- **DNS** is hijacked into sing-box; only Discord domains are resolved over DNS-over-HTTPS (1.1.1.1), everything else uses the normal system resolver. That kills the DNS poisoning for Discord only.
- **The tunnel exit is a free WARP device.** There is no paid server anywhere in the chain.

### The WARP part (the fun bit)

WARP is a WireGuard tunnel to Cloudflare's edge. The official client needs an install and an account; the "register a device" API does not.

`tools/warp-register.py` does the whole thing with the Python standard library:

1. Generates an **X25519** keypair (implemented from RFC 7748 — no dependencies).
2. `POST https://api.cloudflareclient.com/v0a5641/reg` with the public key.
3. Receives the peer public key, the assigned address (`172.16.0.2`) and a `client_id`.
4. Derives the 3-byte WireGuard `reserved` field from `base64(client_id)`.
5. Writes a ready-to-run sing-box `config.json`.

### The selective part

The interesting lines of the config:

```jsonc
"route": {
  "rules": [
    { "action": "sniff" },                                // read the domain from the TLS SNI
    { "protocol": "dns", "action": "hijack-dns" },         // take over system DNS
    { "domain_suffix": ["discord.com", "discord.gg", "discordapp.com",
                        "discordapp.net", "discord.media", "discordstatus.com"],
      "outbound": "warp" },
    { "process_name": ["Discord.exe", "DiscordPTB.exe", "DiscordCanary.exe",
                       "DiscordDevelopment.exe", "Update.exe"],
      "outbound": "warp" }
  ],
  "final": "direct"                                        // unmatched traffic: never tunneled
}
```

Two rules instead of one, because they cover different things: the domain rule catches anything that talks to Discord (even a browser), and the process rule catches IP-literal and **UDP** flows — which is what makes voice channels work.

## Quick start

Requirements: Windows 10/11, admin rights (the TUN driver needs it), internet, Python 3.8+ for the one-time WARP registration.

```bat
:: 1. Register a free WARP device and write config.json
register.bat

:: 2. Install the tunnel (self-elevates via UAC)
install.bat
```

`install.bat` does the rest:

- downloads sing-box (pinned to 1.14.4) if it's not in `bin\`
- copies everything to `C:\ProgramData\DiscordTunnel`
- adds an outbound firewall rule for the binary
- registers the `DiscordOnlyTunnel` scheduled task (SYSTEM, at startup, restarts on failure)
- starts it and verifies the result

Then fully quit Discord (tray icon too) and start it again.

## Verify it works

```powershell
# Should return 162.159.x.x — not the ISP block IP
Resolve-DnsName discord.com -Type A

# Live log: Discord flows go through wireguard[warp], the rest through direct
Get-Content C:\ProgramData\DiscordTunnel\tunnel.log -Tail 30
```

What a healthy log looks like:

```
router: found process path: C:\...\Discord\app-1.0.9267\Discord.exe
inbound connection to 34.126.226.51:443
endpoint/wireguard[warp]: outbound connection to 34.126.226.51:443
```

and, in the same file, non-Discord traffic:

```
inbound connection to 104.20.23.154:443
outbound/direct[direct]: outbound connection to 104.20.23.154:443
```

One more sanity check — the Discord API through the tunnel should answer with the gateway URL:

```powershell
(Invoke-WebRequest https://discord.com/api/v9/gateway -UseBasicParsing).Content
# {"url": "wss://gateway.discord.gg"}
```

## Files

| File | Purpose |
|---|---|
| `install.bat` / `install.ps1` | Self-elevating installer: fetch, register, install task, verify |
| `uninstall.bat` / `uninstall.ps1` | Stops and removes the task, firewall rule and files |
| `register.bat` | Runs the WARP registration only (creates `config.json`) |
| `tools/warp-register.py` | WARP device registration + sing-box config generator (stdlib only) |
| `tools/diagnose.py` | Measures what *your* network is actually doing (DNS, SNI, certs). Run it with the tunnel stopped |
| `config.example.json` | The generated config shape, with placeholder keys |

## Limits & FAQ

**Is this a VPN?** No. Only Discord's flows enter the tunnel. Your IP is unchanged for everything else.

**Does voice work?** Yes — WARP carries UDP and the process rule catches Discord's UDP flows.

**How fast is it?** WARP-depended, typically 20–100 Mbps. Fine for Discord, not for downloads (which don't use it anyway).

**What if it breaks?** Run `uninstall.bat`. The task, firewall rule and files are removed; the routes disappear with the TUN device.

**What if WARP gets blocked later?** Re-run `register.bat` for a fresh device. If the *endpoint* is blocked, swap the WARP endpoint for your own VPS running VLESS/Reality — the routing rules stay identical, only the `endpoints` block changes.

**Why not just exclude Discord from a VPN?** Because that's the inverse problem: most VPN clients route everything by default and "exclude" modes are coarse. This routes nothing by default and includes only Discord.

## Disclaimer

Built for my own use to reach a service I need for work and friends. Use it at your own risk and within your local laws. This is not affiliated with Discord or Cloudflare; WARP is a free Cloudflare service with no uptime guarantee.

MIT licensed.
