#!/usr/bin/env python3
"""Generate the add-on's PNG textures with the Python standard library only.

Outputs:
  RealisticWorld_BP/pack_icon.png, RealisticWorld_RP/pack_icon.png   128x128 globe icon
  RealisticWorld_RP/textures/items/*.png    16x16 item icons (control center, water gauge)
  RealisticWorld_RP/textures/blocks/*.png   16x16 block textures (valve, pipe, drain, puddle)

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


def art_to_pixels(art, palette):
    return [[palette[ch] for ch in line] for line in art]


def valve(open_):
    """Iron block with a red (closed) or green (open) wheel."""
    M = (120, 124, 130, 255)
    D = (82, 86, 92, 255)
    W = (60, 170, 70, 255) if open_ else (190, 45, 40, 255)
    H = (40, 40, 44, 255)
    art = [
        "DDDDDDDDDDDDDDDD",
        "DMMMMMMMMMMMMMMD",
        "DMMMMMWWWWMMMMMD",
        "DMMMMWMMMMWMMMMD",
        "DMMMWMMMMMMWMMMD",
        "DMMWMMMHHMMMWMMD",
        "DMMWMMHHHHMMWMMD",
        "DMWWWWHHHHWWWWMD",
        "DMWWWWHHHHWWWWMD",
        "DMMWMMHHHHMMWMMD",
        "DMMWMMMHHMMMWMMD",
        "DMMMWMMMMMMWMMMD",
        "DMMMMWMMMMWMMMMD",
        "DMMMMMWWWWMMMMMD",
        "DMMMMMMMMMMMMMMD",
        "DDDDDDDDDDDDDDDD",
    ]
    return art_to_pixels(art, {"D": D, "M": M, "W": W, "H": H})


def pipe():
    """Grey metal with an opening in the middle."""
    M = (154, 160, 166, 255)
    L = (190, 196, 202, 255)
    D = (96, 100, 106, 255)
    H = (30, 32, 36, 255)
    rows = []
    for y in range(16):
        row = []
        for x in range(16):
            r = ((x - 7.5) ** 2 + (y - 7.5) ** 2) ** 0.5
            row.append(H if r < 4 else D if r < 5 else L if x + y < 12 else M)
        rows.append(row)
    return rows


def drain():
    """Dark iron grate."""
    D = (58, 63, 68, 255)
    H = (18, 20, 24, 255)
    return [[D if x % 4 in (0, 1) or y in (0, 15) else H for x in range(16)] for y in range(16)]


def puddle():
    """Semi-transparent water surface."""
    import random
    rnd = random.Random(7)
    return [[(63 + rnd.randint(-6, 6), 118 + rnd.randint(-6, 6), 228, 150) for _ in range(16)] for _ in range(16)]


def water_gauge_item():
    """Round gauge with a blue water level."""
    T = (0, 0, 0, 0)
    F = (70, 74, 80, 255)
    G = (205, 225, 235, 255)
    B = (45, 110, 210, 255)
    R = (200, 40, 40, 255)
    art = [
        "................",
        ".....FFFFFF.....",
        "...FFGGGGGGFF...",
        "..FGGGGGGGGGGF..",
        "..FGGGGGGRGGGF..",
        ".FGGGGGGRGGGGGF.",
        ".FGGGGGRGGGGGGF.",
        ".FGGGGRGGGGGGGF.",
        ".FBBBBBBBBBBBBF.",
        ".FBBBBBBBBBBBBF.",
        "..FBBBBBBBBBBF..",
        "..FBBBBBBBBBBF..",
        "...FFBBBBBBFF...",
        ".....FFFFFF.....",
        "......FFFF......",
        "................",
    ]
    return art_to_pixels(art, {".": T, "F": F, "G": G, "B": B, "R": R})


def main():
    icon = globe(128)
    write_png(ROOT / "RealisticWorld_BP" / "pack_icon.png", 128, 128, icon)
    write_png(ROOT / "RealisticWorld_RP" / "pack_icon.png", 128, 128, icon)
    write_png(ROOT / "RealisticWorld_RP" / "textures" / "items" / "control_center.png", 16, 16, control_center_item())
    write_png(ROOT / "RealisticWorld_RP" / "textures" / "items" / "water_gauge.png", 16, 16, water_gauge_item())
    blocks = ROOT / "RealisticWorld_RP" / "textures" / "blocks"
    write_png(blocks / "valve_closed.png", 16, 16, valve(False))
    write_png(blocks / "valve_open.png", 16, 16, valve(True))
    write_png(blocks / "pipe.png", 16, 16, pipe())
    write_png(blocks / "drain.png", 16, 16, drain())
    write_png(blocks / "puddle.png", 16, 16, puddle())
    print("textures written")


if __name__ == "__main__":
    main()
