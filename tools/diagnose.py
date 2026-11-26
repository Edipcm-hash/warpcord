#!/usr/bin/env python3
"""
diagnose.py — figure out what your network is ACTUALLY doing to Discord.

It answers, layer by layer:
  * is DNS poisoned?           (system resolver vs DNS-over-HTTPS)
  * is plain UDP/53 hijacked?   (query 1.1.1.1 directly)
  * is the IP range blackholed? (TCP connect to the real IP)
  * is the block SNI-based?     (same IP, two different SNI values)
  * is it a MITM or a redirect? (inspect the certificate the block page serves)

No dependencies beyond the standard library.

Usage:
    python tools/diagnose.py
"""

import json
import socket
import ssl
import struct
import sys

DOMAIN = "discord.com"
CONTROL_SNI = "example.com"
DOH_URL = "https://cloudflare-dns.com/dns-query?name={}&type=A"


def system_dns(name, timeout=5):
    socket.setdefaulttimeout(timeout)
    try:
        return [ip for _, _, _, _, (ip, _) in socket.getaddrinfo(name, 443, socket.AF_INET)]
    except Exception as exc:  # noqa: BLE001
        return ["<error: %s>" % exc]


def doh(name):
    import urllib.request

    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    try:
        req = urllib.request.Request(DOH_URL.format(name), headers={"accept": "application/dns-json"})
        with urllib.request.urlopen(req, timeout=12, context=ctx) as resp:
            payload = json.load(resp)
        return [a["data"] for a in payload.get("Answer", []) if a.get("type") == 1]
    except Exception as exc:  # noqa: BLE001
        return ["<error: %s>" % exc]


def udp53_works(server="1.1.1.1", name=DOMAIN):
    """Minimal DNS query over UDP/53 — detects transparent DNS hijacking."""
    labels = b"".join(bytes([len(p)]) + p.encode() for p in name.split("."))
    query = struct.pack(">HHHHHH", 0x1234, 0x0100, 1, 0, 0, 0) + labels + b"\x00" + struct.pack(">HH", 1, 1)
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.settimeout(4)
    try:
        sock.sendto(query, (server, 53))
        data, _ = sock.recvfrom(512)
        return data[:2] == b"\x12\x34"
    except Exception:  # noqa: BLE001
        return False
    finally:
        sock.close()


def tls_handshake(ip, sni, timeout=6):
    """Returns ('ok', version) or ('fail', reason)."""
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    try:
        sock = socket.create_connection((ip, 443), timeout)
        try:
            tls = ctx.wrap_socket(sock, server_hostname=sni)
            version = tls.version()
            tls.close()
            return "ok", version
        finally:
            sock.close()
    except Exception as exc:  # noqa: BLE001
        return "fail", "%s: %s" % (type(exc).__name__, exc)


def peer_cert_cn(ip, sni):
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    try:
        sock = socket.create_connection((ip, 443), 6)
        tls = ctx.wrap_socket(sock, server_hostname=sni)
        der = tls.getpeercert(binary_form=True)
        tls.close()
        pem = ssl.DER_cert_to_PEM_cert(der)
        path = "_peer.pem"
        with open(path, "w", encoding="ascii") as fh:
            fh.write(pem)
        info = ssl._ssl._test_decode_cert(path)  # type: ignore[attr-defined]
        issuer = info.get("issuer", ())
        return next((value for rdn in issuer for key, value in rdn if key == "commonName"), "?")
    except Exception as exc:  # noqa: BLE001
        return "<%s>" % type(exc).__name__


def main():
    print("=" * 66)
    print(" Discord network diagnosis")
    print("=" * 66)
    print(" (!) Run this with WarpCord STOPPED (kaldir.bat) — otherwise the")
    print("     tunnel is already active and the network will look clean.")

    sys_ips = system_dns(DOMAIN)
    real_ips = doh(DOMAIN)

    print("\n[1] DNS")
    print("    system resolver : %s" % ", ".join(sys_ips))
    print("    DoH (1.1.1.1)   : %s" % ", ".join(real_ips))

    poisoned = bool(real_ips) and real_ips[0] not in sys_ips and not real_ips[0].startswith("<")
    print("    -> %s" % ("POISONED (system answer differs from DoH)" if poisoned else "consistent"))

    print("\n[2] Plain UDP/53 to 1.1.1.1")
    udp_ok = udp53_works()
    print("    -> %s" % ("reachable" if udp_ok else "HIJACKED/BLOCKED (encrypted DNS needed)"))

    real_ip = real_ips[0] if real_ips and not real_ips[0].startswith("<") else None
    if real_ip:
        print("\n[3] TCP/443 to the real IP (%s)" % real_ip)
        try:
            sock = socket.create_connection((real_ip, 443), 6)
            sock.close()
            print("    -> open (no IP-level blackhole)")
        except Exception as exc:  # noqa: BLE001
            print("    -> %s: %s" % (type(exc).__name__, exc))

        print("\n[4] SNI filter test (same IP, two hostnames)")
        ok_discord, reason_discord = tls_handshake(real_ip, DOMAIN)
        ok_control, reason_control = tls_handshake(real_ip, CONTROL_SNI)
        print("    SNI=%-14s -> %s" % (DOMAIN, "ok, TLS %s" % reason_discord if ok_discord == "ok" else "FAILED (%s)" % reason_discord))
        print("    SNI=%-14s -> %s" % (CONTROL_SNI, "ok, TLS %s" % reason_control if ok_control == "ok" else "FAILED (%s)" % reason_control))
        if ok_control == "ok" and ok_discord != "ok":
            print("    -> SNI-BASED BLOCK confirmed (a tunnel or ClientHello splitting is required)")

    if sys_ips and not sys_ips[0].startswith("<"):
        print("\n[5] What the system-resolved IP serves")
        cn = peer_cert_cn(sys_ips[0], DOMAIN)
        print("    issuer CN: %s" % cn)
        if "localhost" in cn.lower() or cn == "?":
            print("    -> self-signed/block page: an ISP redirect, not a MITM")

    print("\n" + "=" * 66)
    if poisoned or not udp_ok:
        print(" Verdict: traffic to Discord is interfered with at DNS/SNI level.")
        print(" WarpCord solves exactly this: DoH for name resolution + a real tunnel.")
    else:
        print(" Verdict: no interference detected on this network.")
    print("=" * 66)
    return 0


if __name__ == "__main__":
    sys.exit(main())
