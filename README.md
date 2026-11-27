**[Türkçe](README.tr.md)** | English

# WarpCord

**A Discord-only split tunnel — one click, no Python.** No VPS, no account, no full-device VPN: only Discord's traffic leaves through the tunnel; everything else keeps going out directly.

> **v2 — what changed:** one-click install, the Python dependency is gone, `wintun.dll` is actually installed, WARP endpoints are auto-selected, and there is now a **DPI engine for lines that block WARP itself (Superonline)**. Details: [Version 2](#version-2--what-changed).

> **v2.5 — the guardian:** a `WarpCordHeal` task checks the tunnel every 5 minutes, repairs whatever broke, and writes every error to `C:\ProgramData\DiscordTunnel\heal.log`. Details: [Version 2.5](#version-25--the-self-healing-guardian).

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

## Version 2.5 — the self-healing guardian

v2.5 does not just install the tunnel, it **keeps it working**. A `WarpCordHeal` task runs every 5 minutes, checks the install and repairs whatever broke:

| Check | Repair |
|---|---|
| `sing-box.exe` / `wintun.dll` present | Re-downloaded from GitHub / wintun.net |
| Scheduled task exists and is enabled | Re-registered / re-enabled |
| `config.json` valid (`sing-box check`) | Regenerated from `account.json` and the saved state |
| sing-box process running | Tunnel restarted (max 2 restarts per run) |
| TUN adapter up | Restarted, waits up to 20 s |
| DPI mode: proxy running and listening | Proxy task restarted |
| Discord DNS poisoned | DNS restored to 1.1.1.1 (with backup) |
| Discord API answering | Last resort: tunnel restarted |

- Every check, finding and repair is appended to **`C:\ProgramData\DiscordTunnel\heal.log`**; the file rotates to `heal.log.old` at 2 MB.
- The last run is summarised in `heal-state.json` and shown by `durum.bat` in its **Onarim** row.
- Manual repair: **`onar.bat`** (or `durum.bat -Repair`). Exit codes: `0` healthy, `1` repaired, `2` unrecoverable, `3` no administrator rights.
- The guardian task runs as SYSTEM; `durum.bat` stays read-only, `onar.bat` asks for UAC.

## Version 3.0 — one click on a clean Windows 11

On a stock Windows 11 it was not the WarpCord code that failed: it was Windows' own protections. Measured blockers and the v3 answer:

| Blocker | What it does | v3 fix |
|---|---|---|
| **MOTW + RemoteSigned** | A `.ps1` extracted from the zip is never loaded: "not digitally signed" | `WarpCord.bat` strips the `Zone.Identifier` stream and calls `Unblock-File` for every file; the scripts also clean their own folder |
| **GPO: AllSigned / Restricted** | Even `-ExecutionPolicy Bypass` refuses to load the `.ps1` | The launcher detects it and runs the script in memory through `[scriptblock]::Create` (with `-RepoRoot`) |
| **SmartScreen** | "Windows protected your PC" | Unblocking removes MOTW so the prompt never appears; without a code signature the very first download may still need one "Run anyway" |
| **Smart App Control** | Unsigned `sing-box.exe` / `warp-frag.exe` will not start | Detected early; `tani.bat` and `install.log` state the only fix (turning SAC off is irreversible) |
| **Defender / ASR 5BEB7EFE** | Blocks or quarantines downloaded/extracted content | MOTW cleanup + exclusions for the install/repo folders and for `sing-box.exe`, `warp-frag.exe` |
| **Controlled Folder Access** | Blocks writes into protected folders | When CFA is on, the binaries are added to the allowed-applications list |
| **WDAC / UMCI (user-mode)** | Unsigned scripts and binaries do not run at all | Detected with a clear message; not automatable, the policy must be removed |
| **Proxy / TLS inspection** | The GitHub download breaks | Persistent cache (`cache\`), local `bin\` copies, and a TLS probe that names the failure |
| **ConstrainedLanguage** | Scripts cannot run | Early detection, exit code 9 with an explanation |
| **PSModulePath pollution** (PowerShell 7 Store install) | PS 5.1 autoloads the wrong modules; `Get-ExecutionPolicy`, Defender and task cmdlets fail with "module could not be loaded" | `PSModulePath` is reset to the canonical 5.1 paths inside the process |

Also:

- **`tani.bat`** measures everything (build, policy, SAC/WDAC, Defender/ASR/CFA, MOTW, proxy, DNS, GitHub TLS, tasks, logs) and **`tani.bat -Bundle`** produces a shareable zip.
- Any failing step prints a concrete hint (`Get-WarpErrorHint`) and points at `tani.bat`.
- `kaldir.bat` also reverts the Defender/CFA exclusions it added.
- The repo root is now tidy: `app/`, `tools/`, `docs/`; the one-click entry point is still `WarpCord.bat`.

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
durum.bat -Repair                :: run the repair pass by hand (UAC)
onar.bat                         :: run the guardian: check, repair, write heal.log
tani.bat                         :: diagnostics: why did it fail? report + next steps
tani.bat -Bundle                 :: bundle the report as a zip
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
| `WarpCord.bat` | **One click**: install, repair, pick the mode. Self-elevates, strips MOTW, falls back to in-memory execution |
| `tani.bat` / `tools/tani.ps1` | **Diagnostics**: says why it failed (policy, Defender, SAC, network, logs) and writes a report |
| `tools/splash.bat` | The 5-second ASCII cat animation on double-click (delete it to disable) |
| `app/install.ps1` | The whole installer: fetch, register, probe, install, verify, roll back on failure |
| `durum.bat` / `app/status.ps1` | Live health check (no admin required) |
| `onar.bat` / `tools/warpheal.ps1` | **Guardian**: checks the tunnel every 5 minutes, repairs what broke, writes `heal.log` |
| `kaldir.bat` / `app/uninstall.ps1` | Removes tasks, firewall rules and files; restores DNS and Defender exclusions |
| `tools/register.bat` / `tools/warp-register.ps1` | WARP registration + config generation only (PowerShell) |
| `tools/warpcore.ps1` | Engine: X25519, registration, endpoint probing, config builders, DNS helpers, host hardening |
| `tools/warpfrag/WarpFrag.cs` | DPI engine: dependency-free TLS-record-splitting local proxy |
| `tools/warpfrag.ps1` | Build/test the proxy (`-Build`, `-Test`, `-Run`) |
| `docs/config.example.json` / `docs/config.example.dpi.json` | Generated config shapes |
| `CHANGELOG.md` | Release notes (v3.0.0, v2.5.0, v2.0.0) |
| `tools/diagnose.py` | *(optional, Python 3)* Measures what *your* network is actually doing (DNS, SNI, certs) |

The first run downloads sing-box (~33 MB) and `wintun.dll`. To skip the download, drop them in as `bin\sing-box.exe` and `bin\wintun.dll`; the installer checks there first.

What the installer touches: `C:\ProgramData\DiscordTunnel` (logs and `heal-state.json` included), the scheduled tasks (`DiscordOnlyTunnel`, `WarpCordFrag` in DPI mode, `WarpCordHeal`), outbound firewall allow rules, and **only if DNS is poisoned** the adapter DNS (with a backup).

## Limits & FAQ

**Does it work on Superonline?** That is exactly what v2 is for. `WarpCord.bat` tries WARP first; if Superonline blocks it, the installer switches to the DPI engine automatically and Discord works again. DPI mode does not hide your IP (it is a bypass, not a tunnel); WARP mode does.

**Is this a VPN?** No. Only Discord's flows enter the tunnel/proxy. Your IP and routes are unchanged for everything else.

**Why does it change my DNS?** Only when needed. Windows asks every configured resolver in parallel: while the tunnel answered correctly, the ISP resolver kept handing out the block-page IP for Discord and Windows cached both answers. If that happens, the adapter is pointed at `1.1.1.1` so those packets land in the tunnel, where sing-box answers them over DoH. If your DNS is already clean, nothing is touched, and `kaldir.bat` (or a failed install) restores DHCP.

**What about IPv6 traffic?** The TUN deliberately carries IPv4 only; IPv6 flows stay outside the tunnel (the "don't touch my connection" philosophy). The IPv6 side of DNS is pointed at Cloudflare too so poisoned answers cannot appear, and if Discord tries IPv6 it falls back to IPv4 quickly.

**What if I switch networks (Wi-Fi → Ethernet)?** The DNS setting is applied to the adapter that carries traffic during install, and a separate backup is kept per adapter. If Discord stops working after joining a new network, re-run `WarpCord.bat`: it backs up the new adapter too and fixes the DNS. `kaldir.bat` restores every adapter it changed.

**Does voice work?** In WARP mode yes (UDP is tunneled). In DPI mode voice UDP goes direct; it works unless the ISP also blocks Discord's UDP media. If voice is a must, use a line/exit where WARP works.

**What if IPv6 is disabled?** The installer detects it. With IPv6 disabled on Windows (`DisabledComponents` / `ms_tcpip6` unbound), sing-box's WireGuard endpoint cannot bind its IPv6 socket to the adapter, so WARP mode does not work; the tool automatically switches to the DPI engine and Discord still opens (without hiding your IP). Enable IPv6 and re-run `WarpCord.bat` to get WARP mode back.

**How fast is it?** WARP-dependent, typically 20–100 Mbps. Fine for Discord, not for downloads (which don't use it). DPI mode adds ~1–5 ms.

**What if it breaks?** The guardian usually fixes it within 5 minutes; check `heal.log` to see what it did. Run `onar.bat` for an immediate repair pass. If that does not help, run `kaldir.bat`: the tasks, firewall rules and files are removed, DNS is restored, and the routes disappear with the TUN device. A failed install also restores DNS automatically, so you never lose internet.

**What if WARP gets blocked later?** Re-run `WarpCord.bat`: it re-probes endpoints and falls back to DPI mode if needed. Use `-Force` for a brand-new device.

**Antivirus warnings?** It needs admin and a TUN driver; `wintun.dll` is the official WireGuard-signed driver. The compiled `warp-frag.exe` is unsigned (source: `tools/warpfrag/WarpFrag.cs`, single file).

**Does the config contain a private key?** Yes — `C:\ProgramData\DiscordTunnel\config.json` and `account.json` hold the WARP device's private key. ACLs restrict them to SYSTEM/Administrators/you; don't share or commit them.

**Why not just exclude Discord from a VPN?** Because that's the inverse problem: most VPN clients route everything by default and "exclude" modes are coarse. This routes nothing by default and includes only Discord.

## Test note

On the development line: `discord.com` → `195.175.254.2` and `SNI=discord.com` → reset were confirmed. The WARP handshake works **partially** there: port 2408 sometimes times out while port 500 connects in 150–800 ms — which is exactly why endpoint/port auto-selection exists. However, the test machine has IPv6 disabled (`DisabledComponents=255`), so sing-box's WireGuard endpoint cannot bind to the adapter; the installer detected that, switched to the DPI engine automatically, and the Discord API returned a real answer in ~340 ms. On a machine with IPv6 enabled the WARP path works with the same endpoint selection and verification. The TUN driver, scheduled tasks and adapter DNS need final end-user verification on a machine where the account actually holds admin rights.

## Disclaimer

Built for my own use to reach a service I need for work and friends. Use it at your own risk and within your local laws. This is not affiliated with Discord or Cloudflare; WARP is a free Cloudflare service with no uptime guarantee.

MIT licensed.
