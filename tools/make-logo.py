#!/usr/bin/env python3
"""WarpCord logo generator.

Draws the app mark at a high master resolution, then renders every icon size
from scratch (small sizes get a simplified drawing so they stay legible) and
writes a multi-resolution .ico, PNG exports and the SVG source of the mark.

    python tools/make-logo.py                     # write gui/assets/*
    python tools/make-logo.py --variant b         # try another mark
    python tools/make-logo.py --preview out.png   # contact sheet of variants
"""

from __future__ import annotations

import argparse
import math
import os
import struct
import zlib

from PIL import Image, ImageChops, ImageDraw, ImageFilter

# ---------------------------------------------------------------- palette --
BG_HI = (26, 33, 68)
BG_LO = (7, 9, 15)
VIOLET = (139, 92, 246)
INDIGO = (99, 102, 241)
CYAN = (34, 211, 238)
ICE = (224, 242, 254)
WHITE = (255, 255, 255)

SS = 4  # supersampling factor on top of the icon size


# ------------------------------------------------------------------ utils --
def ease(t: float) -> float:
    return t * t * (3 - 2 * t)


def mix(c1, c2, t: float):
    return tuple(int(round(a + (b - a) * t)) for a, b in zip(c1, c2))


def vertical_gradient(w: int, h: int, c1, c2) -> Image.Image:
    img = Image.new("RGB", (1, h))
    px = img.load()
    for y in range(h):
        px[0, y] = mix(c1, c2, ease(y / max(1, h - 1)))
    return img.resize((w, h), Image.Resampling.NEAREST)


def diagonal_gradient(w: int, h: int, c1, c2) -> Image.Image:
    """Gradient along the top-right → bottom-left diagonal."""
    img = Image.new("RGB", (w, h))
    px = img.load()
    for y in range(h):
        for x in range(0, w):
            t = ease(((w - x) + y) / (w + h))
            px[x, y] = mix(c1, c2, t)
    return img


def squircle(size: int, inset: float, radius_ratio: float) -> Image.Image:
    m = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(m)
    pad = size * inset
    d.rounded_rectangle(
        [pad, pad, size - 1 - pad, size - 1 - pad],
        radius=size * radius_ratio,
        fill=255,
    )
    return m


def mask_arc(size: int, cx, cy, r, width, start=0, end=360) -> Image.Image:
    m = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(m)
    d.arc(
        [cx - r, cy - r, cx + r, cy + r],
        start=start,
        end=end,
        fill=255,
        width=max(1, int(round(width))),
    )
    return m


def mask_ring(size: int, cx, cy, r, width) -> Image.Image:
    m = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(m)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=255, width=max(1, int(round(width))))
    return m


def mask_line(size: int, p0, p1, width, round_caps=True) -> Image.Image:
    m = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(m)
    d.line([p0, p1], fill=255, width=max(1, int(round(width))), joint="curve")
    if round_caps:
        r = width / 2
        for (x, y) in (p0, p1):
            d.ellipse([x - r, y - r, x + r, y + r], fill=255)
    return m


def mask_poly(size: int, pts) -> Image.Image:
    m = Image.new("L", (size, size), 0)
    ImageDraw.Draw(m).polygon(pts, fill=255)
    return m


def paint(img: Image.Image, mask: Image.Image, color_img: Image.Image, opacity: float = 1.0):
    """Composite a colour image onto ``img`` through ``mask``."""
    if opacity < 1.0:
        mask = mask.point(lambda v: int(v * opacity))
    img.paste(color_img, (0, 0), mask)


def glow(size: int, mask: Image.Image, color, radius: float, strength: float) -> Image.Image:
    """Build an additive glow layer around ``mask``."""
    blurred = mask.filter(ImageFilter.GaussianBlur(radius))
    blurred = blurred.point(lambda v: int(v * strength))
    layer = Image.new("RGB", (size, size), color)
    out = Image.new("RGB", (size, size), (0, 0, 0))
    out.paste(layer, (0, 0), blurred)
    return out


def add(img: Image.Image, layer: Image.Image) -> Image.Image:
    return ImageChops.add(img, layer)


def screen(img: Image.Image, layer: Image.Image) -> Image.Image:
    return ImageChops.screen(img, layer)


# ------------------------------------------------------------- the marks --
def draw_base(size: int, inset=0.045, radius=0.265) -> Image.Image:
    """The dark squircle tile with its inner light."""
    img = Image.new("RGB", (size, size), BG_LO)
    grad = vertical_gradient(size, size, BG_HI, BG_LO)
    img.paste(grad, (0, 0), squircle(size, inset, radius))

    # soft light from the top-left, like a screen reflection
    light = Image.new("L", (size, size), 0)
    ld = ImageDraw.Draw(light)
    cx, cy = size * 0.30, size * 0.22
    for i in range(28, 0, -1):
        rr = size * (0.10 + 0.030 * i)
        a = int(9 * (1 - i / 28) ** 0.7)
        ld.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], fill=a)
    light = light.filter(ImageFilter.GaussianBlur(size * 0.02))
    img = screen(img, Image.merge("RGB", (light, light, light)))

    # 1px rim: brighter on top, fading down
    rim = Image.new("L", (size, size), 0)
    rd = ImageDraw.Draw(rim)
    pad = size * inset
    rd.rounded_rectangle(
        [pad, pad, size - 1 - pad, size - 1 - pad],
        radius=size * radius,
        outline=255,
        width=max(1, size // 256),
    )
    rim = ImageChops.multiply(rim, vertical_gradient(size, size, (255, 255, 255), (40, 40, 40)).convert("L"))
    paint(img, rim, Image.new("RGB", (size, size), (150, 165, 255)), 0.55)
    return img


def mark_portal(size: int) -> Image.Image:
    """A tunnel mouth: three rings and one warp streak through it."""
    s = size
    img = draw_base(s)
    cx, cy = s * 0.5, s * 0.505
    unit = s / 512.0

    rings = [
        (0.300, 0.070, VIOLET, 0.30, 0.55),   # r, width, colour, opacity, glow
        (0.212, 0.058, CYAN,   0.85, 0.75),
        (0.128, 0.046, ICE,    1.00, 0.55),
    ]
    for r, w, color, alpha, gl in rings:
        m = mask_ring(s, cx, cy, r * s, w * s)
        grad = diagonal_gradient(s, s, mix(color, WHITE, 0.15), mix(color, BG_LO, 0.25))
        paint(img, m, grad, alpha)
        img = add(img, glow(s, m, color, 0.035 * s, 0.55 * gl))

    # warp streak: thin white core with a cyan halo, 45° through the centre
    p0 = (cx - 0.255 * s, cy + 0.255 * s)
    p1 = (cx + 0.255 * s, cy - 0.255 * s)
    core = mask_line(s, p0, p1, 0.026 * s)
    halo = mask_line(s, p0, p1, 0.075 * s)
    img = add(img, glow(s, halo, CYAN, 0.030 * s, 0.85))
    paint(img, halo, Image.new("RGB", (s, s), CYAN), 0.30)
    paint(img, core, Image.new("RGB", (s, s), WHITE), 0.96)
    return img


def _ribbon_mask(s: int, width_ratio: float, pts_norm) -> Image.Image:
    pts = [(x * s, y * s) for x, y in pts_norm]
    m = Image.new("L", (s, s), 0)
    d = ImageDraw.Draw(m)
    width = width_ratio * s
    for a, b in zip(pts, pts[1:]):
        d.line([a, b], fill=255, width=int(round(width)), joint="curve")
    r = width / 2
    for (x, y) in pts:
        d.ellipse([x - r, y - r, x + r, y + r], fill=255)
    return m


W_PTS = [
    (0.134, 0.212), (0.292, 0.775), (0.500, 0.470),
    (0.708, 0.775), (0.866, 0.212),
]


def _node(s: int, cx: float, cy: float, r: float, img: Image.Image):
    """A bright connection node with a halo."""
    dot = Image.new("L", (s, s), 0)
    ImageDraw.Draw(dot).ellipse(
        [cx * s - r * s, cy * s - r * s, cx * s + r * s, cy * s + r * s], fill=255
    )
    halo = Image.new("L", (s, s), 0)
    ImageDraw.Draw(halo).ellipse(
        [cx * s - 2.2 * r * s, cy * s - 2.2 * r * s, cx * s + 2.2 * r * s, cy * s + 2.2 * r * s],
        fill=255,
    )
    img = add(img, glow(s, halo, CYAN, 0.035 * s, 0.75))
    paint(img, dot, Image.new("RGB", (s, s), ICE), 1.0)
    return img


def mark_ribbon(size: int) -> Image.Image:
    """A 'W' cut from a cable, with one cord loop threaded behind it."""
    s = size
    img = draw_base(s)

    # cord loop behind the W
    loop_m = mask_ring(s, s * 0.5, s * 0.475, 0.352 * s, 0.028 * s)
    paint(img, loop_m, diagonal_gradient(s, s, (120, 140, 230), (60, 80, 160)), 0.38)

    grad = diagonal_gradient(s, s, VIOLET, CYAN)
    m = _ribbon_mask(s, 0.104, W_PTS)
    img = add(img, glow(s, m, CYAN, 0.045 * s, 0.60))
    paint(img, m, grad, 1.0)

    # soft highlight along the upper half of the ribbon
    hl = ImageChops.multiply(
        m, vertical_gradient(s, s, (255, 255, 255), (40, 40, 40)).convert("L")
    )
    paint(img, hl, Image.new("RGB", (s, s), WHITE), 0.20)

    return _node(s, 0.795, 0.300, 0.030, img)


def mark_ringw(size: int) -> Image.Image:
    """The W inside a tunnel ring: traffic entering a portal."""
    s = size
    img = draw_base(s)

    # portal ring, dimmer at the top so the W owns the light
    ring = mask_ring(s, s * 0.5, s * 0.505, 0.350 * s, 0.032 * s)
    ring_tone = diagonal_gradient(s, s, (167, 139, 250), (34, 211, 238))
    paint(img, ring, ring_tone, 0.72)
    img = add(img, glow(s, ring, CYAN, 0.038 * s, 0.34))

    # dark separation so the ribbon reads on top of the portal
    sep = _ribbon_mask(s, 0.104, W_PTS).filter(
        ImageFilter.MaxFilter(max(3, int(s * 0.020) | 1))
    )
    paint(img, sep, diagonal_gradient(s, s, (17, 20, 38), (9, 11, 20)), 0.93)

    m = _ribbon_mask(s, 0.094, W_PTS)
    grad = diagonal_gradient(s, s, (167, 139, 250), (56, 220, 240))
    img = add(img, glow(s, m, CYAN, 0.042 * s, 0.42))
    paint(img, m, grad, 1.0)

    # specular highlight running along the upper-left of the cable
    hl = ImageChops.multiply(
        m, diagonal_gradient(s, s, (255, 255, 255), (30, 30, 30)).convert("L")
    )
    paint(img, hl, Image.new("RGB", (s, s), WHITE), 0.26)

    return _node(s, 0.866, 0.212, 0.027, img)


def mark_shield(size: int) -> Image.Image:
    """Split shield: one half tunnel, one half direct, bolt through the gap."""
    s = size
    img = draw_base(s)
    u = s / 512.0

    def shield_pts(inset=0.0):
        x0, x1 = (0.20 + inset) * s, (0.80 - inset) * s
        top, shoulder = (0.155 + inset) * s, (0.53) * s
        bottom = (0.845 - inset) * s
        return [
            (x0, top + 0.02 * s), (0.5 * s, top), (x1, top + 0.02 * s),
            (x1, shoulder), (0.5 * s, bottom), (x0, shoulder),
        ]

    outer = mask_poly(s, shield_pts())
    inner = mask_poly(s, shield_pts(0.045))
    body = ImageChops.subtract(outer, inner)
    grad = diagonal_gradient(s, s, (40, 48, 92), (14, 18, 34))
    paint(img, body, grad, 1.0)

    # left half filled = the tunnelled side, faded toward the split line
    left = Image.new("L", (s, s), 0)
    ImageDraw.Draw(left).polygon(shield_pts(0.045), fill=255)
    cut = Image.new("L", (s, s), 0)
    ImageDraw.Draw(cut).polygon([(0, 0), (0.5 * s, 0), (0.5 * s, s), (0, s)], fill=255)
    left = ImageChops.multiply(left, cut).filter(ImageFilter.GaussianBlur(0.004 * s))
    paint(img, left, diagonal_gradient(s, s, VIOLET, (60, 60, 140)), 0.30)
    paint(img, body, Image.new("RGB", (s, s), (160, 175, 255)), 0.10)

    # bolt through the split
    bolt = mask_poly(s, [
        (0.545, 0.255), (0.400, 0.520), (0.505, 0.520),
        (0.455, 0.760), (0.625, 0.470), (0.515, 0.470),
        (0.575, 0.255),
    ])
    img = add(img, glow(s, bolt, CYAN, 0.045 * s, 0.7))
    paint(img, bolt, diagonal_gradient(s, s, WHITE, ICE), 1.0)
    return img


MARKS = {
    "a": mark_portal,
    "b": mark_ribbon,
    "c": mark_shield,
    "d": mark_ringw,
}
DEFAULT = "d"


def render(variant: str, size: int) -> Image.Image:
    return MARKS[variant](size * SS).resize((size, size), Image.Resampling.LANCZOS)


# ----------------------------------------------------------------- output --
def png_bytes(img: Image.Image) -> bytes:
    import io

    buf = io.BytesIO()
    img.save(buf, format="PNG", optimize=True)
    return buf.getvalue()


def write_ico(path: str, variant: str, sizes):
    """Write an ICO whose entries are PNG-compressed, each drawn natively."""
    entries, blobs = [], []
    for size in sizes:
        data = png_bytes(render(variant, size))
        blobs.append(data)
        entries.append((size, len(blobs) - 1))

    header = struct.pack("<HHH", 0, 1, len(entries))
    offset = 6 + 16 * len(entries)
    directory, payload = b"", b""
    for size, idx in entries:
        data = blobs[idx]
        directory += struct.pack(
            "<BBBBHHII",
            0 if size >= 256 else size,
            0 if size >= 256 else size,
            0,
            0,
            1,
            32,
            len(data),
            offset,
        )
        offset += len(data)
        payload += data
    with open(path, "wb") as fh:
        fh.write(header + directory + payload)


SVG = """<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" width="512" height="512">
  <defs>
    <linearGradient id="tile" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#1A2144"/><stop offset="1" stop-color="#07090F"/>
    </linearGradient>
    <linearGradient id="portal" x1="1" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#A78BFA"/><stop offset="1" stop-color="#22D3EE"/>
    </linearGradient>
    <linearGradient id="cable" x1="1" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#A78BFA"/><stop offset="1" stop-color="#38DCF0"/>
    </linearGradient>
    <filter id="soft" x="-40%" y="-40%" width="180%" height="180%">
      <feGaussianBlur stdDeviation="9"/>
    </filter>
  </defs>
  <rect x="23" y="23" width="466" height="466" rx="136" fill="url(#tile)"/>
  <circle cx="256" cy="258" r="179" fill="none" stroke="url(#portal)"
          stroke-width="16" opacity="0.72" filter="url(#soft)"/>
  <circle cx="256" cy="258" r="179" fill="none" stroke="url(#portal)"
          stroke-width="16" opacity="0.72"/>
  <path d="M69 109 L150 397 L256 241 L362 397 L443 109" fill="none"
        stroke="#0A0C14" stroke-width="60" stroke-linecap="round" stroke-linejoin="round"/>
  <path d="M69 109 L150 397 L256 241 L362 397 L443 109" fill="none"
        stroke="url(#cable)" stroke-width="48" stroke-linecap="round" stroke-linejoin="round"/>
  <path d="M69 109 L150 397 L256 241 L362 397 L443 109" fill="none"
        stroke="#FFFFFF" stroke-width="48" stroke-linecap="round" stroke-linejoin="round"
        opacity="0.13"/>
  <circle cx="443" cy="109" r="26" fill="#22D3EE" opacity="0.45" filter="url(#soft)"/>
  <circle cx="443" cy="109" r="14" fill="#E0F2FE"/>
</svg>
"""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--variant", default=DEFAULT, choices=sorted(MARKS))
    ap.add_argument("--preview", help="write a contact sheet of every variant here")
    ap.add_argument("--out", default=os.path.join(os.path.dirname(__file__), "..", "gui", "assets"))
    args = ap.parse_args()

    if args.preview:
        cols = len(MARKS)
        sheet = Image.new("RGB", (256 * cols + 40, 256 + 96), (12, 14, 22))
        d = ImageDraw.Draw(sheet)
        for i, key in enumerate(sorted(MARKS)):
            x = 20 + i * 256
            sheet.paste(render(key, 256), (x, 20))
            sheet.paste(render(key, 32), (x + 84, 286))
            sheet.paste(render(key, 16), (x + 134, 294))
            d.text((x + 4, 284 - 4), key.upper(), fill=(220, 225, 240))
        os.makedirs(os.path.dirname(os.path.abspath(args.preview)), exist_ok=True)
        sheet.save(args.preview)
        print("preview:", os.path.abspath(args.preview))
        return

    out = os.path.abspath(args.out)
    os.makedirs(out, exist_ok=True)
    write_ico(os.path.join(out, "warpcord.ico"), args.variant,
              [16, 20, 24, 32, 40, 48, 64, 96, 128, 256])
    for size in (16, 24, 32, 48, 64, 128, 256, 512):
        render(args.variant, size).save(os.path.join(out, f"warpcord-{size}.png"))
    # a wide mark for the header row and docs
    render(args.variant, 512).save(os.path.join(out, "warpcord.png"))
    with open(os.path.join(out, "warpcord.svg"), "w", encoding="utf-8") as fh:
        fh.write(SVG)
    print(f"wrote {out} (variant {args.variant})")


if __name__ == "__main__":
    main()
