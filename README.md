**[Türkçe](README.tr.md)** | English

# WarpCord

**A Discord-only split tunnel — one click, no Python.** No VPS, no account, no full-device VPN: only Discord's traffic leaves through the tunnel; everything else keeps going out directly.

> **v2 — what changed:** one-click install, the Python dependency is gone, `wintun.dll` is actually installed, WARP endpoints are auto-selected, and there is now a **DPI engine for lines that block WARP itself (Superonline)**. Details: [Version 2](#version-2--what-changed).

Tested on Windows 11 (build 26200) with sing-box 1.14.4, on a network where Discord is blocked by ISP-level DNS hijacking **and** SNI filtering. The v2 DPI engine was additionally verified live on a Turkish ISP line with the same block profile.

---

## Why two engines?

Discord is blocked in Turkey by two layers at once:

1. **Poisoned DNS** — `discord.com` resolves to the ISP's block page.
2. **SNI filter** — any TLS ClientHello mentioning Discord is reset immediately.

The **WARP engine** handles both: DNS is answered over DoH inside the tunnel, and the TLS handshake leaves through the tunnel, so only WireGuard packets are visible outside.

But on some lines — **Superonline** in particular — Cloudflare WARP itself is blocked, so a tunnel cannot be established at all. That is why v2 adds a second engine that defeats the filter directly: **splitting the TLS ClientHello across multiple TLS records**. RFC 8446 explicitly allows a handshake message to span records, so the server reassembles it happily, while a DPI that parses records one at a time never sees the whole SNI. It is the same trick GoodbyeDPI/zapret uses, but driver-free, scoped to Discord only, and implemented as a single source file.

The installer knows both and picks whichever works:

| Mode | When | Is your IP hidden? | Voice |
|---|---|---|---|
| `warp` | WARP is reachable | Yes, Discord traffic exits via Cloudflare | Yes, UDP is tunneled |
| `dpi` | WARP is blocked (Superonline) | **No**, the block is just bypassed | Usually yes; voice UDP goes direct |

## Quick start — one click

1. Extract the archive anywhere (Desktop is fine).
2. Double-click **`WarpCord.bat`** and accept the UAC prompt. (A double-click shows a 5-second cat greeting first; running from a command line with arguments skips it.)
3. Wait. The installer downloads sing-box + `wintun.dll`, registers a free WARP device, probes endpoints one by one, picks the working mode, installs the scheduled tasks, and **verifies against the real Discord API**.
4. Fully quit Discord (tray icon too) and start it again.

Check: **`durum.bat`** &nbsp;•&nbsp; Uninstall: **`kaldir.bat`**

Requirements: Windows 10/11 **64-bit** (AMD64 or ARM64), admin rights (the TUN driver needs it), internet. **No Python required.**

## Compatibility — which Windows, which connection?

Wired and wireless make no difference: both use the same TUN + routing path. Only the adapter name and the interface that receives the DNS setting differ, and both are supported.

| Environment | Status |
|---|---|
| Windows 10 (1809+) / Windows 11, 64-bit | ✅ supported and tested |
| Windows 11 ARM64 | ✅ (arm64 binary + arm64 `wintun.dll`) |
| **Wired Ethernet** | ✅ |
| **Wi-Fi** | ✅ |
| USB tethering / phone hotspot | ✅ (DPI engine when WARP is blocked) |
| PPPoE behind a router (typical Superonline fiber) | ✅ the PC just sees Ethernet/Wi-Fi |
| PPPoE directly on the PC | ⚠️ attempted; falls back to the interface index when the alias is empty |
| Systems with IPv6 disabled | ⚠️ WARP mode unavailable → automatic DPI mode (no IP hiding) |
| Another VPN running | ⚠️ conflicts; disconnect other VPNs while using WarpCord |
| Captive portal (hotel/cafe login page) | ⚠️ pass the portal first; the installer warns while one is open |
| 32-bit Windows | ❌ sing-box ships no 32-bit Windows build |
| Windows 7 / 8.1 | ❌ misses PowerShell 5.1 + sing-box 1.14 requirements |
| Non-admin user | ❌ UAC elevation is required |
| No internet at all | ❌ (the installer detects this early and says so) |

**If you switch networks** (e.g. Wi-Fi to cable, or a new modem): the DNS setting is written to the adapter carrying traffic, with a separate backup per adapter. If Discord stops working, re-run `WarpCord.bat` — it backs up the new adapter too and fixes it.

## Version 2 — what changed

| Before | Now |
|---|---|
| `register.bat` + `install.bat`, Python required | One `WarpCord.bat`; WARP registration in PowerShell (RFC 7748 X25519, compiled C#) |
| `wintun.dll` was never installed (TUN fails on most machines) | `wintun.dll` is downloaded and placed next to sing-box |
| Hard-coded endpoint `162.159.192.1:2408` | Cloudflare's assigned endpoint + ports 2408/500/1701/4500 + fallback anycast addresses + IPv6 when available; fastest working one wins |
| Dead end if WARP is blocked | DPI engine takes over automatically |
| DNS changed on every install | Only when the ISP poisons Discord names; restored automatically if the install fails |
| All DNS sent to Cloudflare | DNS is changed only when needed; poisoned answers are eliminated by DoH |
| No health check beyond uninstall | `durum.bat` live health check, `kaldir.bat` clean removal |
| AMD64 only | AMD64 + ARM64, TLS 1.2 forced, BOM-free JSON, non-ASCII path safe |

## What the block actually was

I probed each layer separately (see `tools/diagnose.py`):

| Layer | Result |
|---|---|
| DNS | `discord.com` → `195.175.254.2` (the ISP's block page). Everything else resolved normally. |
| Plain DNS to 1.1.1.1 / 8.8.8.8 / 9.9.9.9 | **Reset.** So "just change your DNS" does nothing on its own. |
| DoH (443) / DoT (853) | Worked. Encrypted DNS slipped through untouched. |
| IP block | **None.** TCP 443 to Discord's real Cloudflare IPs connected in 14–40 ms. |
| MITM | **None.** Normal sites served their real certificates. |
| SNI filter | **This was the block.** Same IP: `SNI=example.com` → TLS fine. `SNI=discord.com` → instant reset. |
| WARP (UDP 2408/500/1701/4500) | **Variable.** Some endpoints/ports time out, while port 500 completes the handshake in 150–800 ms. WARP mode was still unusable on the test machine because IPv6 was disabled (see the test note). |

## How it works

```
  Discord.exe ─────┐
  *.discord.com ───┤  TUN (auto_route) ── sing-box ──┬── warp  : WireGuard ──▶ Cloudflare WARP ──▶ Discord
                   │                                 ├── frag  : 127.0.0.1:1081 ──▶ TLS record splitting ──▶ Discord
                   │                                 └── direct ─────────────▶ everything else
```

- **sing-box** creates a TUN device (`WarpCord`) and takes over the default route.
- **Routing rules** decide per flow: Discord by domain suffix **or** by process → `warp`/`frag`; everything else → `direct` (the `final`).
- **DNS** is hijacked into sing-box and answered over DoH (1.1.1.1), so the ISP's poisoned answers never reach Windows. If the network's DNS is already clean, the adapter setting is left untouched and resolution stays with the ISP resolver (see the FAQ).
- In **`warp` mode** the exit is a free WARP device.
- In **`dpi` mode** Discord's TCP flows are sent to `warp-frag.exe`, a small local HTTP CONNECT proxy that splits the ClientHello into 2–3 TLS records at the SNI. Even the DoH request goes through that proxy, so resolution survives a filtered `cloudflare-dns.com`.

### The WARP part

WARP is a WireGuard tunnel to Cloudflare's edge. The official client needs an install and an account; the "register a device" API does not.

`tools/warpcore.ps1` does the whole thing in PowerShell:

1. Generates an **X25519** keypair (RFC 7748 Montgomery ladder, compiled from C# — checked against the RFC test vectors).
2. `POST https://api.cloudflareclient.com/v0a5641/reg` with the public key (alternate API versions and, if needed, the DPI proxy as fallback).
3. Receives the peer public key, the assigned address and a `client_id`.
4. Derives the 3-byte WireGuard `reserved` field from `base64(client_id)`.
5. Writes a ready-to-run sing-box `config.json`.

### Endpoint auto-selection (the Superonline part)

Cloudflare's assigned endpoint is not the only option. The installer tries, in order: the assigned endpoint → ports 500 / 1701 / 4500 → fallback anycast addresses → IPv6 endpoints (when the line has IPv6). Each candidate is verified by making a **real request to the Discord API** through it; the fastest working one is written to the config. If none works, it falls back to DPI mode.

### The selective part

```jsonc
"route": {
  "rules": [
    { "action": "sniff" },                            // read the domain from the TLS SNI
    { "protocol": "dns", "action": "hijack-dns" },    // take over system DNS
    { "domain_suffix": ["discord.com", "discord.gg", ...], "outbound": "warp" },
    { "process_name": ["Discord.exe", "Update.exe", ...], "outbound": "warp" },
    { "process_path_regex": ["(?i).*[\\\\/]Discord[\\\\/].*"], "outbound": "warp" }
  ],
  "final": "direct"                                   // unmatched traffic: never tunneled
}
```

Two rule families, because they cover different things: the domain rule catches anything that talks to Discord (even a browser), and the process rule catches IP-literal and **UDP** flows — which is what makes voice channels work.

## Command-line options

```bat
WarpCord.bat                     :: auto: probe and pick the working mode (recommended)
WarpCord.bat -Mode warp          :: WARP only; fails if unreachable
WarpCord.bat -Mode dpi           :: DPI engine only (no WARP registration)
WarpCord.bat -Force              :: register a fresh free WARP device
WarpCord.bat -Endpoint 162.159.192.1:2408   :: pin the endpoint
WarpCord.bat -KeepDns            :: never touch system DNS
WarpCord.bat -SkipProbe          :: skip probing, reuse the recorded endpoint
durum.bat                        :: live health check
durum.bat -Restart               :: restart the scheduled tasks
kaldir.bat                       :: remove everything and restore DNS
```

## Verify it works

```powershell
# Should return 162.159.x.x — not the ISP block IP
Resolve-DnsName discord.com -Type A

# No cached block-page answer (empty output is the good case)
Get-DnsClientCache | Where-Object { $_.Data -eq '195.175.254.2' }

# Live log: Discord flows go through warp/frag, the rest through direct
Get-Content C:\ProgramData\DiscordTunnel\tunnel.log -Tail 30
```

What a healthy log looks like (warp mode):

```
router: found process path: C:\...\Discord\app-1.0.9267\Discord.exe
inbound connection to 34.126.226.51:443
endpoint/wireguard[warp]: outbound connection to 34.126.226.51:443
```

and in dpi mode:

```
inbound connection to discord.com:443
outbound/http[frag]: outbound connection to discord.com:443
```

and, in the same file, non-Discord traffic:

```
outbound/direct[direct]: outbound connection to 104.20.23.154:443
```

One more sanity check:

```powershell
(Invoke-WebRequest https://discord.com/api/v9/gateway -UseBasicParsing).Content
# {"url": "wss://gateway.discord.gg"}
```

## Files

| File | Purpose |
|---|---|
| `WarpCord.bat` | **One click**: install, repair, pick the mode. Self-elevates via UAC |
| `tools/splash.bat` | The 5-second ASCII cat animation on double-click (delete it to disable) |
| `install.ps1` | The whole installer: fetch, register, probe, install, verify, roll back on failure |
| `durum.bat` / `status.ps1` | Live health check (no admin required) |
| `kaldir.bat` / `uninstall.ps1` | Removes tasks, firewall rules and files; restores DNS |
| `register.bat` / `tools/warp-register.ps1` | WARP registration + config generation only (PowerShell) |
| `tools/warpcore.ps1` | Engine: X25519, registration, endpoint probing, config builders, DNS helpers |
| `tools/warpfrag/WarpFrag.cs` | DPI engine: dependency-free TLS-record-splitting local proxy |
| `tools/warpfrag.ps1` | Build/test the proxy (`-Build`, `-Test`, `-Run`) |
| `tools/diagnose.py` | *(optional, Python 3)* Measures what *your* network is actually doing (DNS, SNI, certs) |
| `config.example.json` / `config.example.dpi.json` | Generated config shapes |

The first run downloads sing-box (~33 MB) and `wintun.dll`. To skip the download, drop them in as `bin\sing-box.exe` and `bin\wintun.dll`; the installer checks there first.

What the installer touches: `C:\ProgramData\DiscordTunnel`, two scheduled tasks (`DiscordOnlyTunnel`, plus `WarpCordFrag` in DPI mode), outbound firewall allow rules, and **only if DNS is poisoned** the adapter DNS (with a backup).

## Limits & FAQ

**Does it work on Superonline?** That is exactly what v2 is for. `WarpCord.bat` tries WARP first; if Superonline blocks it, the installer switches to the DPI engine automatically and Discord works again. DPI mode does not hide your IP (it is a bypass, not a tunnel); WARP mode does.

**Is this a VPN?** No. Only Discord's flows enter the tunnel/proxy. Your IP and routes are unchanged for everything else.

**Why does it change my DNS?** Only when needed. Windows asks every configured resolver in parallel: while the tunnel answered correctly, the ISP resolver kept handing out the block-page IP for Discord and Windows cached both answers. If that happens, the adapter is pointed at `1.1.1.1` so those packets land in the tunnel, where sing-box answers them over DoH. If your DNS is already clean, nothing is touched, and `kaldir.bat` (or a failed install) restores DHCP.

**What about IPv6 traffic?** The TUN deliberately carries IPv4 only; IPv6 flows stay outside the tunnel (the "don't touch my connection" philosophy). The IPv6 side of DNS is pointed at Cloudflare too so poisoned answers cannot appear, and if Discord tries IPv6 it falls back to IPv4 quickly.

**What if I switch networks (Wi-Fi → Ethernet)?** The DNS setting is applied to the adapter that carries traffic during install, and a separate backup is kept per adapter. If Discord stops working after joining a new network, re-run `WarpCord.bat`: it backs up the new adapter too and fixes the DNS. `kaldir.bat` restores every adapter it changed.

**Does voice work?** In WARP mode yes (UDP is tunneled). In DPI mode voice UDP goes direct; it works unless the ISP also blocks Discord's UDP media. If voice is a must, use a line/exit where WARP works.

**What if IPv6 is disabled?** The installer detects it. With IPv6 disabled on Windows (`DisabledComponents` / `ms_tcpip6` unbound), sing-box's WireGuard endpoint cannot bind its IPv6 socket to the adapter, so WARP mode does not work; the tool automatically switches to the DPI engine and Discord still opens (without hiding your IP). Enable IPv6 and re-run `WarpCord.bat` to get WARP mode back.

**How fast is it?** WARP-dependent, typically 20–100 Mbps. Fine for Discord, not for downloads (which don't use it). DPI mode adds ~1–5 ms.

**What if it breaks?** Run `kaldir.bat`. The tasks, firewall rules and files are removed, DNS is restored, and the routes disappear with the TUN device. A failed install also restores DNS automatically, so you never lose internet.

**What if WARP gets blocked later?** Re-run `WarpCord.bat`: it re-probes endpoints and falls back to DPI mode if needed. Use `-Force` for a brand-new device.

**Antivirus warnings?** It needs admin and a TUN driver; `wintun.dll` is the official WireGuard-signed driver. The compiled `warp-frag.exe` is unsigned (source: `tools/warpfrag/WarpFrag.cs`, single file).

**Does the config contain a private key?** Yes — `C:\ProgramData\DiscordTunnel\config.json` and `account.json` hold the WARP device's private key. ACLs restrict them to SYSTEM/Administrators/you; don't share or commit them.

**Why not just exclude Discord from a VPN?** Because that's the inverse problem: most VPN clients route everything by default and "exclude" modes are coarse. This routes nothing by default and includes only Discord.

## Test note

On the development line: `discord.com` → `195.175.254.2` and `SNI=discord.com` → reset were confirmed. The WARP handshake works **partially** there: port 2408 sometimes times out while port 500 connects in 150–800 ms — which is exactly why endpoint/port auto-selection exists. However, the test machine has IPv6 disabled (`DisabledComponents=255`), so sing-box's WireGuard endpoint cannot bind to the adapter; the installer detected that, switched to the DPI engine automatically, and the Discord API returned a real answer in ~340 ms. On a machine with IPv6 enabled the WARP path works with the same endpoint selection and verification. The TUN driver, scheduled tasks and adapter DNS need final end-user verification on a machine where the account actually holds admin rights.

## Disclaimer

Built for my own use to reach a service I need for work and friends. Use it at your own risk and within your local laws. This is not affiliated with Discord or Cloudflare; WARP is a free Cloudflare service with no uptime guarantee.

MIT licensed.
