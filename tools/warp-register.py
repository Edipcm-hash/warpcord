#!/usr/bin/env python3
r"""
warp-register.py — register a free Cloudflare WARP device (no account, no email)
and write a ready-to-run sing-box config that tunnels ONLY Discord through it.

Everything here uses the Python standard library. The X25519 implementation is
straight from RFC 7748 (Montgomery ladder), because pulling a crypto dependency
for one keypair is overkill.

Usage:
    python tools/warp-register.py --out config.json
    python tools/warp-register.py --out config.json --install-dir C:\ProgramData\DiscordTunnel

Notes:
    * The WARP device is free and anonymous. Re-running the script creates a NEW
      device identity (old one keeps working but is never reused).
    * config.json contains a PRIVATE KEY. It is gitignored on purpose. Never commit it.
"""

import argparse
import base64
import json
import os
import secrets
import ssl
import sys
import urllib.error
import urllib.request
from datetime import datetime, timezone

REGISTER_URL = "https://api.cloudflareclient.com/v0a5641/reg"

DISCORD_SUFFIX = [
    "discord.com", "discord.gg", "discordapp.com", "discordapp.net",
    "discord.media", "discordstatus.com", "discord.co",
]
DISCORD_PROCESSES = [
    "Discord.exe", "DiscordPTB.exe", "DiscordCanary.exe",
    "DiscordDevelopment.exe", "Update.exe",
]

# WARP's well-known peer public key (same for every WARP device).
WARP_PEER_PUBLIC_KEY = "bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo="
WARP_ENDPOINT = "162.159.192.1"
WARP_PORT = 2408

# --------------------------------------------------------------------------- #
# X25519 (RFC 7748)                                                           #
# --------------------------------------------------------------------------- #
P = 2 ** 255 - 19
A24 = 121665


def _cswap(swap, x2, x3):
    dummy = swap * ((x2 - x3) % P)
    return (x2 - dummy) % P, (x3 + dummy) % P


def x25519(k_bytes: bytes, u_bytes: bytes) -> bytes:
    k = bytearray(k_bytes)
    k[0] &= 248
    k[31] &= 127
    k[31] |= 64
    k_int = int.from_bytes(bytes(k), "little")

    u = bytearray(u_bytes)
    u[31] &= 127
    x1 = int.from_bytes(bytes(u), "little")

    x2, z2, x3, z3 = 1, 0, x1, 1
    swap = 0
    for t in range(254, -1, -1):
        kt = (k_int >> t) & 1
        swap ^= kt
        x2, x3 = _cswap(swap, x2, x3)
        z2, z3 = _cswap(swap, z2, z3)
        swap = kt

        a = (x2 + z2) % P
        aa = a * a % P
        b = (x2 - z2) % P
        bb = b * b % P
        e = (aa - bb) % P
        c = (x3 + z3) % P
        d = (x3 - z3) % P
        da = d * a % P
        cb = c * b % P
        x3 = pow(da + cb, 2, P)
        z3 = x1 * pow(da - cb, 2, P) % P
        x2 = aa * bb % P
        z2 = e * (aa + A24 * e) % P

    x2, x3 = _cswap(swap, x2, x3)
    z2, z3 = _cswap(swap, z2, z3)
    return (x2 * pow(z2, P - 2, P) % P).to_bytes(32, "little")


# --------------------------------------------------------------------------- #
# WARP registration                                                           #
# --------------------------------------------------------------------------- #
def _ssl_contexts(allow_insecure: bool):
    """Verified context first; unverified one only as a last-resort fallback.

    A broken local trust store (some Windows installs ship very few root CAs)
    should not make the tool unusable, but it must be loud about it.
    """
    if allow_insecure:
        return [ssl._create_unverified_context()]
    return [ssl.create_default_context(), ssl._create_unverified_context()]


def register(allow_insecure: bool = False) -> dict:
    private_key = secrets.token_bytes(32)
    public_key = x25519(private_key, bytes([9] + [0] * 31))
    pub_b64 = base64.b64encode(public_key).decode()
    priv_b64 = base64.b64encode(private_key).decode()

    body = {
        "key": pub_b64,
        "install_id": "",
        "fcm_token": "",
        "tos": datetime.now(timezone.utc).isoformat(timespec="milliseconds"),
        "model": "PC",
        "serial_number": "",
        "locale": "en_US",
    }
    data = json.dumps(body).encode()

    last_error = None
    contexts = _ssl_contexts(allow_insecure)
    payload = None
    for idx, ctx in enumerate(contexts):
        req = urllib.request.Request(
            REGISTER_URL,
            data=data,
            method="POST",
            headers={
                "Content-Type": "application/json",
                "User-Agent": "okhttp/3.12.1",
                "CF-Client-Version": "a-6.30-3106",
                "Accept": "application/json",
            },
        )
        try:
            with urllib.request.urlopen(req, timeout=45, context=ctx) as resp:
                payload = json.load(resp)
            break
        except ssl.SSLError as exc:
            last_error = exc
            if idx + 1 < len(contexts):
                print("  ! TLS dogrulamasi basarisiz: %s" % exc)
                print("    sertifika deposu eksik olabilir, dogrulamasiz deneniyor...")
                continue
            raise
    if payload is None:
        raise SystemExit("Kayit basarisiz: %s" % last_error)

    cfg = payload["config"]
    peer = cfg["peers"][0]
    address = cfg["interface"]["addresses"]["v4"]
    client_id = payload.get("client_id") or cfg.get("client_id")

    reserved = list(base64.b64decode(client_id)[:3]) if client_id else [0, 0, 0]

    return {
        "private_key": priv_b64,
        "public_key": pub_b64,
        "address": address,
        "reserved": reserved,
        "client_id": client_id,
    }


def build_config(account: dict, log_path: str) -> dict:
    return {
        "log": {"level": "info", "timestamp": True, "output": log_path},
        "dns": {
            "servers": [
                {"type": "https", "tag": "doh-cf", "server": "1.1.1.1", "path": "/dns-query"},
                {"type": "local", "tag": "local"},
            ],
            "rules": [{"domain_suffix": DISCORD_SUFFIX, "server": "doh-cf"}],
            "final": "local",
            "strategy": "prefer_ipv4",
        },
        "inbounds": [{
            "type": "tun",
            "tag": "tun-in",
            "address": ["172.19.0.1/30"],
            "mtu": 1400,
            "auto_route": True,
            "strict_route": False,
            "stack": "mixed",
        }],
        "outbounds": [{"type": "direct", "tag": "direct"}],
        "endpoints": [{
            "type": "wireguard",
            "tag": "warp",
            "address": ["%s/32" % account["address"]],
            "private_key": account["private_key"],
            "mtu": 1280,
            "peers": [{
                "address": WARP_ENDPOINT,
                "port": WARP_PORT,
                "public_key": WARP_PEER_PUBLIC_KEY,
                "reserved": account["reserved"],
                "allowed_ips": ["0.0.0.0/0", "::/0"],
                "persistent_keepalive_interval": 25,
            }],
        }],
        "route": {
            "default_domain_resolver": "local",
            "auto_detect_interface": True,
            "rules": [
                {"action": "sniff"},
                {"protocol": "dns", "action": "hijack-dns"},
                {"domain_suffix": DISCORD_SUFFIX, "outbound": "warp"},
                {"process_name": DISCORD_PROCESSES, "outbound": "warp"},
            ],
            "final": "direct",
        },
    }


def main(argv=None):
    ap = argparse.ArgumentParser(description="Register a free Cloudflare WARP device and write a sing-box config.")
    ap.add_argument("--out", default="config.json", help="output config path (default: config.json)")
    ap.add_argument("--install-dir", default=r"C:\ProgramData\DiscordTunnel",
                    help="install directory, used for the log path")
    ap.add_argument("--insecure", action="store_true",
                    help="skip TLS verification (only if your trust store is broken)")
    args = ap.parse_args(argv)

    print("WARP cihazi kaydediliyor (ucretsiz, hesap gerekmez)...")
    account = register(allow_insecure=args.insecure)
    print("  cihaz IP'si : %s" % account["address"])
    print("  reserved    : %s" % account["reserved"])

    log_path = os.path.join(args.install_dir, "tunnel.log")
    cfg = build_config(account, log_path)

    with open(args.out, "w", encoding="utf-8") as fh:
        json.dump(cfg, fh, indent=2)

    print("  config      : %s" % os.path.abspath(args.out))
    print("\nHazir. Simdi install.bat calistir.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
