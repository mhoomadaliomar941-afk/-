#!/usr/bin/env python3
"""Generate the add-on's PNG textures with the Python standard library only.

Outputs:
  RealisticWorld_BP/pack_icon.png                     128x128 globe icon
  RealisticWorld_RP/pack_icon.png                     same icon
  RealisticWorld_RP/textures/items/control_center.png 16x16 item texture

Run from the project root:  python3 scripts/make_textures.py
"""

import math
import struct
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def write_png(path, width, height, pixels):
    """pixels: list of rows, each a list of (r, g, b, a) tuples."""
    raw = bytearray()
    for row in pixels:
        raw.append(0)  # filter type: none
        for r, g, b, a in row:
            raw += bytes((r, g, b, a))

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += chunk(b"IEND", b"")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(png)


def land(x, y):
    """Deterministic blobby continents on the unit disc."""
    v = (
        math.sin(x * 5.1 + 0.7) * math.cos(y * 4.3 - 0.4)
        + 0.6 * math.sin((x + y) * 7.9 + 1.3)
        + 0.35 * math.cos(x * 11.0 - y * 9.0)
    )
    return v > 0.45


def globe(size):
    rows = []
    c = (size - 1) / 2
    r = size * 0.46
    for py in range(size):
        row = []
        for px in range(size):
            dx, dy = (px - c) / r, (py - c) / r
            d = math.hypot(dx, dy)
            if d > 1.0:
                # soft atmosphere ring
                if d < 1.07:
                    a = int(255 * (1.07 - d) / 0.07 * 0.6)
                    row.append((120, 200, 255, a))
                else:
                    row.append((0, 0, 0, 0))
                continue
            shade = 0.55 + 0.45 * max(0.0, 1 - math.hypot(dx + 0.35, dy + 0.35))
            if land(dx, dy):
                base = (64, 160, 72) if dy > -0.55 else (235, 240, 245)
            else:
                base = (30, 105, 200)
            row.append(tuple(min(255, int(ch * shade)) for ch in base) + (255,))
        rows.append(row)
    return rows


def control_center_item():
    """16x16 handheld device: dark frame, screen with a sun, cloud and water line."""
    T = (0, 0, 0, 0)
    F = (52, 58, 66, 255)    # frame
    E = (90, 98, 110, 255)   # frame edge highlight
    S = (40, 120, 200, 255)  # sky
    Y = (250, 210, 60, 255)  # sun
    W = (235, 240, 245, 255) # cloud
    G = (70, 170, 80, 255)   # grass
    B = (25, 80, 170, 255)   # water
    R = (220, 60, 50, 255)   # button
    art = [
        "................",
        "..EEEEEEEEEEEE..",
        "..EFFFFFFFFFFE..",
        "..EFSSSSSSSYFE..",
        "..EFSSSSSSYYFE..",
        "..EFSWWSSSSSFE..",
        "..EFWWWWSSSSFE..",
        "..EFSSSSSSSSFE..",
        "..EFGGGGBBBBFE..",
        "..EFGGGBBBBBFE..",
        "..EFFFFFFFFFFE..",
        "..EFFRFFFFFFFE..",
        "..EFFFFFFEEFFE..",
        "..EFFFFFFFFFFE..",
        "..EEEEEEEEEEEE..",
        "................",
    ]
    palette = {".": T, "F": F, "E": E, "S": S, "Y": Y, "W": W, "G": G, "B": B, "R": R}
    return [[palette[ch] for ch in line] for line in art]


def main():
    icon = globe(128)
    write_png(ROOT / "RealisticWorld_BP" / "pack_icon.png", 128, 128, icon)
    write_png(ROOT / "RealisticWorld_RP" / "pack_icon.png", 128, 128, icon)
    write_png(ROOT / "RealisticWorld_RP" / "textures" / "items" / "control_center.png", 16, 16, control_center_item())
    print("textures written")


if __name__ == "__main__":
    main()
