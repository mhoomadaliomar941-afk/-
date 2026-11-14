#!/usr/bin/env python3
"""Automated in-game test suite on a real Forge 1.12.2 dedicated server.

Usage (from the repository root, after `gradle writeTestEnv`):
    python3 tests/test_server.py

Every scenario is built with real commands (/fill, /setblock, /water bucket uses the vanilla bucket
item through a FakePlayer) and measured with the machine-readable /water outputs. Rates use server tick
counts (20 ticks = 1 s of game time), so server lag does not distort the measurements.
Results are written to tests/results/server_results.json and .md
"""
import json
import math
import os
import re
import shutil
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mcharness  # noqa: E402

RESULTS = []
LOGDIR = os.path.join(mcharness.ROOT, 'tests', 'results')
os.makedirs(LOGDIR, exist_ok=True)


def check(test, name, ok, detail):
    RESULTS.append({'test': test, 'check': name, 'pass': bool(ok), 'detail': detail})
    print(('  PASS ' if ok else '  FAIL ') + test + ' :: ' + name + ' :: ' + detail, flush=True)


class Game:
    def __init__(self, server):
        self.s = server
        m = re.search(r'x=(-?\d+) y=(-?\d+) z=(-?\d+)', self.cmd('/water spawn'))
        self.X, self.Y, self.Z = int(m.group(1)), int(m.group(2)), int(m.group(3))

    def cmd(self, c, pat=None, t=60):
        if pat is None:
            # match the reply of this very sub-command (each [WP] line is printed twice: echo + logger)
            parts = c.split()
            pat = (r'DedicatedServer\]: \[WP\] ' + re.escape(parts[1]) + r'\b') if c.startswith('/water') and len(parts) > 1 else r'DedicatedServer\]: \[WP\]'
        m = self.s.command(c, pat, t)
        return m.string

    def mc(self, c, pat):
        return self.cmd(c, pat)

    def P(self, dx, dy, dz):
        return '%d %d %d' % (self.X + dx, self.Y + dy, self.Z + dz)

    @staticmethod
    def kv(line):
        return {k: v for k, v in re.findall(r'(\w+)=([^\s]+)', line)}

    def region(self, a, b):
        d = self.kv(self.cmd('/water region %s %s' % (a, b)))
        return float(d['volume_l']), int(d['cells']), float(d['surface']), int(d['tick'])

    def stats(self):
        return self.kv(self.cmd('/water stats'))

    def tank(self, dx, dz, ix, iz, h, block='stone', base=0):
        """Open-top tank with floor at Y+base, interior (dx+1..dx+ix, Y+base+1..Y+base+h, dz+1..dz+iz)."""
        self.mc('/fill %s %s %s 0 hollow' % (self.P(dx, base, dz), self.P(dx + ix + 1, base + h, dz + iz + 1), block), r'blocks filled')
        self.mc('/fill %s %s air' % (self.P(dx + 1, base + h, dz + 1), self.P(dx + ix, base + h, dz + iz)), r'blocks filled')
        inner = (self.P(dx + 1, base + 1, dz + 1), self.P(dx + ix, base + h, dz + iz))
        return inner

    def setblock(self, p, block):
        self.mc('/setblock %s %s' % (p, block), r'Block placed|The block couldn')



def total_drift(g):
    st = g.stats()
    return float(st['drift_l']), float(st['total_l']), st


# ------------------------------------------------------------------------------------------------
def test_bucket(g):
    T = '1 bucket on the ground'
    r = g.cmd('/water bucket %s empty' % g.P(0, 0, -40))
    check(T, 'vanilla bucket item used through the bucket event', 'result=SUCCESS item=minecraft:bucket' in r, r.split('[WP] ')[-1])
    time.sleep(4)
    vol, cells, surf, _ = g.region(g.P(-12, 0, -52), g.P(12, 1, -28))
    check(T, 'exactly 1000 L placed in the world', abs(vol - 1000) < 0.5, 'volume=%.3f L in %d blocks' % (vol, cells))
    check(T, 'water spread over the ground (not a single block)', cells > 5, '%d blocks wet, surface y=%.3f' % (cells, surf))
    r = g.cmd('/water bucket %s fill' % g.P(0, 0, -40))
    vol2, cells2, _, _ = g.region(g.P(-12, 0, -52), g.P(12, 1, -28))
    check(T, 'empty bucket picks up exactly 1000 L again (mass conserved)', 'item=minecraft:water_bucket' in r and vol2 < 0.5,
          'pickup: %s; remaining=%.3f L' % (r.split('[WP] ')[-1], vol2))
    r = g.cmd('/water bucket %s fill' % g.P(0, 0, -40))
    check(T, 'no water can be taken from nothing (no infinite water)', 'item=minecraft:bucket' in r or 'result=FAIL' in r, r.split('[WP] ')[-1])


def test_fill_tank(g):
    T = '2 fill a 10000 L tank with buckets'
    inner = g.tank(-30, -40, 2, 1, 5)
    r = g.cmd('/water tank %s' % g.P(-29, 1, -39))
    cap = int(g.kv(r)['capacity_l'])
    check(T, 'tank capacity detected', cap == 10000, r.split('[WP] ')[-1])
    for i in range(9):
        g.cmd('/water bucket %s empty' % g.P(-29, 1, -39))
        time.sleep(0.8)
    time.sleep(3)
    v9, _, s9, _ = g.region(*inner)
    check(T, '9 buckets = exactly 9000 L in the tank (nothing lost)', abs(v9 - 9000) < 1, 'volume=%.3f L, surface y=%.3f' % (v9, s9))
    g.cmd('/water bucket %s empty' % g.P(-29, 1, -39))
    time.sleep(5)
    vol, cells, surf, _ = g.region(*inner)
    out, _, _, _ = g.region(g.P(-40, 0, -50), g.P(-20, 7, -30))
    check(T, '10th bucket fills the 10000 L tank to the brim (>= 99.5 %)', vol > 9950,
          'inside=%.2f L, splashed over the rim=%.2f L, surface y=%.3f' % (vol, out - vol, surf))
    check(T, 'all 10000 L accounted for (inside + spilled)', abs(out - 10000) < 1, 'total=%.3f L' % out)
    d = g.kv(g.cmd('/water info %s' % g.P(-29, 1, -39)))
    p = float(d['pressure_kpa'])
    check(T, 'hydrostatic pressure at the bottom ~ rho*g*h (5 m -> ~49 kPa)', 40 < p < 55, 'bottom pressure=%.2f kPa' % p)
    g.cmd('/water bucket %s empty' % g.P(-29, 1, -39))
    time.sleep(4)
    vol2, _, _, _ = g.region(*inner)
    alltot, _, _, _ = g.region(g.P(-40, 0, -50), g.P(-20, 7, -30))
    out = alltot - vol2
    check(T, 'an 11th bucket overflows: the tank stays full, the excess spills out', vol2 > 9800 and out > 900 and abs(alltot - 11000) < 1,
          'inside=%.1f L, outside=%.1f L, total=%.2f L' % (vol2, out, alltot))


def drain_series(g, inner, seconds, step=1.0):
    pts = []
    t_end = time.time() + seconds
    while time.time() < t_end:
        v, c, s, t = g.region(*inner)
        pts.append((t, v, s))
        time.sleep(step)
    return pts


def test_leak(g):
    T = '3-5,7 hole in a tank wall'
    inner = g.tank(30, 0, 4, 4, 6)
    g.cmd('/water fill %s %s 1000' % (g.P(31, 1, 1), g.P(34, 5, 4)))
    time.sleep(5)
    v0, _, s0, _ = g.region(*inner)
    check(T, 'tank filled', abs(v0 - 80000) < 10, 'volume=%.1f L surface=%.3f' % (v0, s0))
    hole = g.P(35, 1, 2)
    g.setblock(hole, 'air')
    pts = drain_series(g, inner, 8, 0.7)
    vols = [p[1] for p in pts]
    mono = all(vols[i + 1] <= vols[i] + 1 for i in range(len(vols) - 1))
    check(T, '3) breaking a wall block creates a hole: water starts leaving', vols[-1] < v0 - 1000, 'V: %.0f -> %.0f L' % (v0, vols[-1]))
    outside, ocells, _, _ = g.region(g.P(36, 0, -10), g.P(60, 3, 15))
    check(T, '4) water flows out through the hole onto the ground', outside > 500 and ocells > 5, 'outside=%.0f L in %d blocks' % (outside, ocells))
    check(T, '5) the level inside decreases gradually (monotonic)', mono and pts[-1][2] < s0, 'surface %.3f -> %.3f; samples=%d' % (s0, pts[-1][2], len(pts)))
    # Torricelli: Q ~ sqrt(h) with h = surface - hole centre
    rates = []
    for i in range(1, len(pts)):
        dt = (pts[i][0] - pts[i - 1][0]) / 20.0
        if dt <= 0:
            continue
        q = (pts[i - 1][1] - pts[i][1]) / dt
        h = (pts[i - 1][2] + pts[i][2]) / 2 - (g.Y + 1.5)
        if h > 0.2 and q > 0:
            rates.append((h, q, q / math.sqrt(h)))
    if len(rates) >= 4:
        k = [r[2] for r in rates[1:]]
        mean = sum(k) / len(k)
        spread = (max(k) - min(k)) / mean
        theo = 0.62 * 1.0 * math.sqrt(2 * 9.81) * 1000
        check(T, 'outflow follows Torricelli (Q/sqrt(h) roughly constant)', spread < 0.6,
              'Q/sqrt(h): mean=%.0f L/s/m^0.5, spread=%.0f%%, ideal free orifice=%.0f; samples (h m, Q L/s): %s' % (
                  mean, spread * 100, theo, ', '.join('(%.2f, %.0f)' % (r[0], r[1]) for r in rates)))
    # close the hole
    g.setblock(hole, 'stone')
    time.sleep(3)
    a, _, sa, _ = g.region(*inner)
    time.sleep(5)
    b, _, sb, _ = g.region(*inner)
    check(T, '7) closing the hole stops the leak', abs(a - b) < 2.0, 'after closing: %.2f L -> %.2f L in 5 s' % (a, b))
    return inner


def test_hole_sizes(g):
    T = '6 leak speed depends on hole size'
    tanks = []
    specs = [('1 block hole (1 m^2)', 'hole1'), ('2 block hole (2 m^2)', 'hole2'), ('spigot 100 cm^2', 3), ('spigot 20 cm^2', 2)]
    for k, (label, kind) in enumerate(specs):
        dx = -70 + k * 12
        inner = g.tank(dx, 30, 3, 3, 6)
        g.cmd('/water fill %s %s 1000' % (g.P(dx + 1, 1, 31), g.P(dx + 3, 5, 33)))
        tanks.append((label, kind, dx, inner))
    time.sleep(6)
    # open all holes at once
    for label, kind, dx, inner in tanks:
        east = dx + 4
        if kind == 'hole1':
            g.setblock(g.P(east, 1, 32), 'air')
        elif kind == 'hole2':
            g.setblock(g.P(east, 1, 32), 'air')
            g.setblock(g.P(east, 1, 31), 'air')
        else:
            g.setblock(g.P(east, 1, 32), 'waterphysics:spigot 5')
            g.cmd('/water spigot %s %d' % (g.P(east, 1, 32), kind))
    t0 = {}
    v0 = {}
    for label, kind, dx, inner in tanks:
        v, _, _, t = g.region(*inner)
        v0[label], t0[label] = v, t
    time.sleep(2.5)
    rates = {}
    for label, kind, dx, inner in tanks:
        v, _, s, t = g.region(*inner)
        rates[label] = (v0[label] - v) / max(1e-6, (t - t0[label]) / 20.0)
    det = ', '.join('%s: %.1f L/s' % (k, v) for k, v in rates.items())
    r1, r2, r100, r20 = rates['1 block hole (1 m^2)'], rates['2 block hole (2 m^2)'], rates['spigot 100 cm^2'], rates['spigot 20 cm^2']
    check(T, 'bigger hole drains faster (2 m^2 > 1 m^2 > 100 cm^2 > 20 cm^2)', r2 > r1 > r100 > r20 > 0, det)
    check(T, '2 m^2 hole drains about twice as fast as 1 m^2 (ratio 1.4..2.6)', 1.4 < r2 / max(r1, 1e-6) < 2.6, 'ratio=%.2f' % (r2 / max(r1, 1e-6)))
    check(T, 'small orifices follow area ratio 100/20 = 5 (ratio 4..6)', 4.0 < r100 / max(r20, 1e-6) < 6.0, 'ratio=%.2f' % (r100 / max(r20, 1e-6)))
    h = 5.0 - 0.5  # approx head over the orifice centre at start
    theo100 = 0.62 * 0.01 * math.sqrt(2 * 9.81 * h) * 1000
    check(T, 'spigot flow matches Torricelli Q = Cd*A*sqrt(2gh) (within 25%)', abs(r100 - theo100) / theo100 < 0.25,
          'measured %.2f L/s, theory %.2f L/s' % (r100, theo100))
    return tanks


def test_multi_holes(g):
    T = '6b several holes share the outflow by depth'
    inner = g.tank(-70, 50, 3, 3, 6)
    g.cmd('/water fill %s %s 1000' % (g.P(-69, 1, 51), g.P(-67, 6, 53)))
    time.sleep(4)
    # two identical 100 cm^2 openings: low one (centre Y+1.5) in the east wall, high one (Y+4.5) in the west wall
    g.setblock(g.P(-66, 1, 52), 'waterphysics:spigot 5')
    g.cmd('/water spigot %s 3' % g.P(-66, 1, 52))
    g.setblock(g.P(-70, 4, 52), 'waterphysics:spigot 4')
    g.cmd('/water spigot %s 3' % g.P(-70, 4, 52))
    time.sleep(2)
    lo = float(g.kv(g.cmd('/water spigot %s' % g.P(-66, 1, 52)))['flow_lps'])
    hi = float(g.kv(g.cmd('/water spigot %s' % g.P(-70, 4, 52)))['flow_lps'])
    _, _, surf, _ = g.region(*inner)
    h_lo, h_hi = surf - (g.Y + 1.5), surf - (g.Y + 4.5)
    expect = math.sqrt(h_lo / h_hi) if h_hi > 0 else 0
    ratio = lo / hi if hi > 0 else 0
    check(T, 'the deeper hole leaks faster (higher pressure)', lo > hi > 0, 'low hole %.2f L/s, high hole %.2f L/s' % (lo, hi))
    check(T, 'flow ratio matches Torricelli sqrt(h_low/h_high) within 10%', abs(ratio - expect) / max(expect, 1e-6) < 0.10,
          'ratio=%.3f expected=%.3f (h_low=%.2f m, h_high=%.2f m)' % (ratio, expect, h_lo, h_hi))


def test_gauge(g):
    T = 'tank gauge + comparator'
    inner = g.tank(-40, 50, 3, 3, 4)
    g.setblock(g.P(-40, 1, 52), 'waterphysics:tank_gauge 1')      # dial facing west, tank behind (east)
    g.setblock(g.P(-41, 0, 52), 'stone')                           # comparators need a block below
    g.setblock(g.P(-41, 1, 52), 'unpowered_comparator 3')          # comparator facing east reads the gauge
    g.cmd('/water fill %s %s 1000' % (g.P(-39, 1, 51), g.P(-37, 2, 53)))  # 18000 L of 36000 L
    time.sleep(5)
    t = g.kv(g.cmd('/water tank %s' % g.P(-39, 1, 52)))
    ok = None
    for sig in (8, 7):
        r = g.mc('/testforblock %s unpowered_comparator -1 {OutputSignal:%d}' % (g.P(-41, 1, 52), sig), r'Successfully found|is |did not')
        if 'Successfully' in r:
            ok = sig
            break
    check(T, 'comparator reads the fill level from the gauge (50 %% -> signal 7..8)', ok is not None,
          'tank %s / %s L, comparator signal=%s' % (t.get('volume_l'), t.get('capacity_l'), ok))


def test_bucket_into_pipe(g):
    T = 'bucket poured into a pipe'
    # vertical pipe standing on the ground (closed bottom); its open top is the centre of a small
    # stone funnel (3x3 floor at the pipe top level, rim one block higher)
    for y in range(0, 3):
        g.setblock(g.P(-20, y, 70), 'waterphysics:pipe_medium')
    for dx in (-1, 0, 1):
        for dz in (-1, 0, 1):
            if dx or dz:
                g.setblock(g.P(-20 + dx, 2, 70 + dz), 'stone')
    g.mc('/fill %s %s stone 0 hollow' % (g.P(-22, 3, 68), g.P(-18, 3, 72)), r'blocks filled')
    g.mc('/fill %s %s air' % (g.P(-21, 3, 69), g.P(-19, 3, 71)), r'blocks filled')
    time.sleep(1)
    r = g.cmd('/water bucket %s empty' % g.P(-20, 3, 70))
    time.sleep(6)
    vols = [float(g.kv(g.cmd('/water pipe %s' % g.P(-20, y, 70)))['volume_l']) for y in range(0, 3)]
    funnel, _, _, _ = g.region(g.P(-21, 3, 69), g.P(-19, 3, 71))
    check(T, 'water poured into a funnel on a pipe flows down and fills the pipe', sum(vols) > 140,
          '%s; pipe contents bottom..top = %s L (capacity 3 x 49.1 L)' % (r.split('[WP] ')[-1], ', '.join('%.1f' % v for v in vols)))
    check(T, 'funnel + pipe contents = the 1000 L poured', abs(funnel + sum(vols) - 1000) < 1,
          'funnel %.2f L + pipe %.2f L = %.2f L' % (funnel, sum(vols), funnel + sum(vols)))


def test_vanilla(g):
    T = 'vanilla water interaction'
    # a natural-like lake: 8x8, 2 deep, vanilla still water placed without block updates
    g.mc('/fill %s %s water' % (g.P(40, -2, 40), g.P(47, -1, 47)), r'blocks filled')
    st0 = g.stats()
    # dig a 3-block trench next to the lake (2 deep): this disturbs the lake edge
    # dig like a player (block updates notify the lake edge): /setblock, not /fill
    for x in range(48, 51):
        for z in range(42, 45):
            for y in (-1, -2):
                g.setblock(g.P(x, y, z), 'air')
    time.sleep(6)
    r = g.cmd('/water info %s' % g.P(48, -2, 43))
    vol, cells, surf, _ = g.region(g.P(48, -2, 42), g.P(50, -1, 44))
    st1 = g.stats()
    conv = float(st1['in_converted']) - float(st0['in_converted'])
    res = float(st1['in_reservoir']) - float(st1['out_reservoir']) - (float(st0['in_reservoir']) - float(st0['out_reservoir']))
    check(T, 'disturbed vanilla water becomes physical water and flows into the trench', vol > 4000 and 'waterphysics:water' in r,
          'trench holds %.0f L in %d blocks (surface y=%.2f); converted %.0f L' % (vol, cells, surf, conv))
    check(T, 'the undisturbed lake acts as a reservoir: it refills the trench to its own level', res > 0 and surf > g.Y - 1.3,
          'net inflow from the lake %.0f L, trench surface y=%.2f, lake surface y=%.2f' % (res, surf, g.Y - 1 + 0.89))
    d = g.stats()
    check(T, 'reservoir exchange is accounted (mass ledger balanced)', abs(float(d['drift_l'])) < 5, 'drift=%s L' % d['drift_l'])


def test_pipes(g):
    T = '8 two tanks connected by a pipe'
    a = g.tank(0, 60, 3, 3, 5)
    b = g.tank(8, 60, 3, 3, 5)
    # large pipe (50 cm) through A's east wall (x=4), gap x=5..7, B's west wall (x=8), bottom layer
    for x in range(4, 9):
        g.setblock(g.P(x, 1, 62), 'waterphysics:pipe_large')
    filled = float(g.kv(g.cmd('/water fill %s %s 1000' % (g.P(1, 1, 61), g.P(3, 4, 63))))['liters'])
    time.sleep(1)
    va0, _, sa0, t0 = g.region(*a)
    vb0, _, sb0, _ = g.region(*b)
    check(T, 'initial: tank A full of water, tank B empty', va0 > 30000 and vb0 < 2000, 'A=%.0f L (surface %.2f) B=%.0f L' % (va0, sa0, vb0))
    t_start = time.time()
    series = []
    while time.time() - t_start < 90:
        time.sleep(3)
        va, _, sa, t = g.region(*a)
        vb, _, sb, _ = g.region(*b)
        series.append((t, va, vb, sa, sb))
        if abs(sa - sb) < 0.05 and len(series) > 3:
            break
    va, vb, sa, sb = series[-1][1], series[-1][2], series[-1][3], series[-1][4]
    first = series[0]
    rate = (first[2] - vb0) / max(1e-6, (first[0] - t0) / 20.0)
    check(T, 'water flows through the pipe from A to B', vb > 10000, 'B received %.0f L; initial flow %.0f L/s' % (vb, rate))
    check(T, 'levels equalise (communicating vessels)', abs(sa - sb) < 0.1,
          'surface A=%.3f B=%.3f after %.0f s' % (sa, sb, (series[-1][0] - t0) / 20.0))
    pipes = 0.0
    for x in range(4, 9):
        d = g.kv(g.cmd('/water pipe %s' % g.P(x, 1, 62)))
        pipes += float(d['volume_l'])
    check(T, 'total water conserved (A + B + pipe contents = poured amount)', abs(va + vb + pipes - filled) < 5,
          'A+B+pipes=%.2f L vs poured %.2f L (pipes hold %.1f L)' % (va + vb + pipes, filled, pipes))
    return a, b


def test_valve(g, a, b):
    T = 'valve controls the flow'
    # replace the middle pipe with a valve (x axis) and close it with redstone, then raise tank A
    g.setblock(g.P(6, 1, 62), 'waterphysics:valve 0')
    g.setblock(g.P(6, 2, 62), 'redstone_block')
    g.cmd('/water fill %s %s 1000' % (g.P(1, 3, 61), g.P(3, 4, 63)))
    time.sleep(3)
    va1, _, _, _ = g.region(*a)
    vb1, _, _, _ = g.region(*b)
    time.sleep(6)
    va2, _, _, _ = g.region(*a)
    vb2, _, _, _ = g.region(*b)
    check(T, 'closed valve: no flow between the tanks', abs(vb2 - vb1) < 40, 'B: %.0f -> %.0f L while A=%.0f L' % (vb1, vb2, va2))
    g.setblock(g.P(6, 2, 62), 'air')
    time.sleep(8)
    vb3, _, _, _ = g.region(*b)
    check(T, 'opened valve: flow resumes', vb3 > vb2 + 300, 'B: %.0f -> %.0f L' % (vb2, vb3))


def test_pump(g):
    T = 'pump lifts water to a higher tank'
    src = g.tank(40, -40, 3, 3, 4)
    dst = g.tank(47, -40, 3, 3, 3, base=7)  # floor at Y+7, interior Y+8..Y+10
    g.cmd('/water fill %s %s 1000' % (g.P(41, 1, -39), g.P(43, 3, -37)))
    # pump in the east wall of the source tank (intake = west side in the water), outlet east
    g.setblock(g.P(44, 1, -38), 'waterphysics:pump 5')
    for y in range(1, 9):
        g.setblock(g.P(45, y, -38), 'waterphysics:pipe_medium')
    g.setblock(g.P(46, 8, -38), 'waterphysics:pipe_medium')
    g.setblock(g.P(47, 8, -38), 'waterphysics:pipe_medium')  # replaces the west wall block of the upper tank
    time.sleep(2)
    s0, _, _, _ = g.region(*src)
    time.sleep(20)
    s1, _, _, _ = g.region(*src)
    d1, _, ds, _ = g.region(*dst)
    p = g.kv(g.cmd('/water pipe %s' % g.P(44, 1, -38)))
    check(T, 'water is pumped up 7 m into the upper tank', d1 > 300, 'upper tank=%.0f L (surface %.2f), pump flow=%s L/s' % (d1, ds, p.get('pump_flow_lps')))
    check(T, 'source tank loses what the upper tank gains (+ pipe contents)', s0 - s1 >= d1 - 1, 'source -%.0f L, upper +%.0f L' % (s0 - s1, d1))
    return src, dst


def test_burst(g):
    T = 'over-pressure: pipes crack and fragile pipes burst'
    # raised stone basin (floor Y+19, rim 2 high) so the test is isolated from water on the ground
    g.mc('/fill %s %s stone' % (g.P(55, 19, -80), g.P(80, 19, -56)), r'blocks filled')
    g.mc('/fill %s %s stone 0 hollow' % (g.P(55, 20, -80), g.P(80, 21, -56)), r'blocks filled')
    g.mc('/fill %s %s air' % (g.P(56, 21, -79), g.P(79, 21, -57)), r'blocks filled')
    src = g.tank(60, -70, 3, 3, 4, base=20)
    g.cmd('/water fill %s %s 1000' % (g.P(61, 21, -69), g.P(63, 23, -67)))
    g.setblock(g.P(64, 21, -68), 'waterphysics:pump 5')
    g.setblock(g.P(65, 21, -68), 'waterphysics:pipe_medium')
    g.setblock(g.P(66, 21, -68), 'waterphysics:pipe_glass')
    g.setblock(g.P(67, 21, -68), 'waterphysics:valve 1')  # x axis, closed by redstone
    g.setblock(g.P(67, 22, -68), 'redstone_block')
    max_kpa = 0.0
    burst = False
    r = ''
    for k in range(25):
        time.sleep(1)
        r = g.cmd('/water info %s' % g.P(66, 21, -68))
        if 'block=minecraft:air' in r or 'block=waterphysics:water' in r:
            burst = True
            break
        try:
            d = g.kv(g.cmd('/water pipe %s' % g.P(66, 21, -68)))
            max_kpa = max(max_kpa, float(d['pressure_kpa']))
        except Exception:
            pass
    check(T, 'pump against a closed valve builds pressure above the glass rating (60 kPa)', max_kpa > 60 or burst, 'max pressure seen=%.1f kPa' % max_kpa)
    check(T, 'the fragile glass pipe bursts and releases its water', burst, 'glass pipe position now: %s' % r.split('block=')[-1])
    d = g.kv(g.cmd('/water pipe %s' % g.P(65, 21, -68)))
    check(T, 'copper pipe (rated 800 kPa) survives the same pressure', float(d.get('damage', '1')) < 0.5,
          'copper damage=%s at %s kPa' % (d.get('damage'), d.get('pressure_kpa')))
    # cracked (damaged) pressurised copper pipe leaks
    g.setblock(g.P(66, 21, -68), 'waterphysics:pipe_medium')
    time.sleep(3)
    g.cmd('/water pipedamage %s 0.6' % g.P(66, 21, -68))
    time.sleep(1)
    out0, _, _, _ = g.region(g.P(65, 20, -79), g.P(79, 21, -57))
    time.sleep(6)
    out1, oc, _, _ = g.region(g.P(65, 20, -79), g.P(79, 21, -57))
    p = g.kv(g.cmd('/water pipe %s' % g.P(66, 21, -68)))
    check(T, 'a cracked pipe leaks water into the world', out1 > out0 + 5,
          'water in the basin around the pipe: %.1f -> %.1f L (%d blocks); pipe at %s kPa' % (out0, out1, oc, p.get('pressure_kpa')))


def test_persist_setup(g):
    T = '9 restart persistence'
    inner = g.tank(-70, -60, 3, 3, 6)
    g.cmd('/water fill %s %s 1000' % (g.P(-69, 1, -59), g.P(-67, 5, -57)))
    time.sleep(3)
    g.setblock(g.P(-66, 1, -58), 'waterphysics:spigot 5')
    g.cmd('/water spigot %s 2' % g.P(-66, 1, -58))  # 20 cm^2: slow leak, still draining at restart
    time.sleep(4)
    v, c, s, t = g.region(*inner)
    return inner, v


def run():
    env = mcharness.load_env()
    sdir = os.path.join(mcharness.ROOT, 'run', 'server')
    shutil.rmtree(os.path.join(sdir, 'world'), ignore_errors=True)
    cfg = os.path.join(sdir, 'config', 'waterphysics.cfg')
    if os.path.exists(cfg):
        os.remove(cfg)
    log1 = os.path.join(LOGDIR, 'server_run1.log')
    s = mcharness.start_server(env, log1)
    persisted = {}
    try:
        s.wait_for(r'Done \(', 300)
        g = Game(s)
        print('spawn at', g.X, g.Y, g.Z, flush=True)
        test_bucket(g)
        test_fill_tank(g)
        test_leak(g)
        test_hole_sizes(g)
        test_multi_holes(g)
        test_gauge(g)
        test_bucket_into_pipe(g)
        test_vanilla(g)
        a, b = test_pipes(g)
        test_valve(g, a, b)
        test_pump(g)
        test_burst(g)
        inner, v = test_persist_setup(g)
        drift, total, st = total_drift(g)
        check('mass conservation', 'stored water = sum(in) - sum(out) over the whole session',
              abs(drift) < max(5.0, 1e-4 * total), 'drift=%.3f L of %.0f L total (rounding %.4f L)' % (drift, total, float(st['rounding_l'])))
        perf = g.kv(g.cmd('/water perf'))
        check('performance', 'server tick cost of the simulation', float(perf['tick_ms']) < 50,
              'last tick %s ms, %s active sections, %s pipe nodes' % (perf['tick_ms'], perf['active_sections'], perf['pipe_nodes']))
        va, _, sa, _ = g.region(*a)
        vb, _, sb, _ = g.region(*b)
        pipe = g.kv(g.cmd('/water pipe %s' % g.P(5, 1, 62)))
        v_before, _, s_before, _ = g.region(*inner)
        persisted = {'inner': inner, 'v': v_before, 'a': va, 'b': vb, 'pipe': float(pipe['volume_l']), 'total': total, 'drift': drift,
                     'X': g.X, 'Y': g.Y, 'Z': g.Z, 'pa': a, 'pb': b}
    finally:
        s.stop()
    # ---- restart ----
    log2 = os.path.join(LOGDIR, 'server_run2.log')
    s = mcharness.start_server(env, log2)
    try:
        s.wait_for(r'Done \(', 300)
        g = Game(s)
        T = '9 restart persistence'
        time.sleep(1)
        v1, _, _, _ = g.region(*persisted['inner'])
        # one tick of draining may happen before the save; allow a small difference
        check(T, 'water volume of a draining tank is preserved across restart', abs(v1 - persisted['v']) < 60,
              'before stop=%.1f L, after restart=%.1f L' % (persisted['v'], v1))
        time.sleep(6)
        v2, _, _, _ = g.region(*persisted['inner'])
        check(T, 'the leak continues after restart (simulation state restored)', v2 < v1 - 5, '%.1f -> %.1f L in 6 s' % (v1, v2))
        va, _, _, _ = g.region(*persisted['pa'])
        vb, _, _, _ = g.region(*persisted['pb'])
        check(T, 'connected tanks keep their water (A+B)', abs((va + vb) - (persisted['a'] + persisted['b'])) < 50,
              'A+B %.0f -> %.0f L (A %.0f->%.0f, B %.0f->%.0f)' % (persisted['a'] + persisted['b'], va + vb, persisted['a'], va, persisted['b'], vb))
        pipe = g.kv(g.cmd('/water pipe %s' % g.P(5, 1, 62)))
        check(T, 'pipe contents are saved', abs(float(pipe['volume_l']) - persisted['pipe']) < 5,
              'pipe %.2f -> %s L' % (persisted['pipe'], pipe['volume_l']))
        drift, total, st = total_drift(g)
        check(T, 'mass accounting survives restart', abs(drift) < max(5.0, 1e-4 * total), 'drift=%.3f L of %.0f L' % (drift, total))
    finally:
        s.stop()
    errors = [l for l in open(log1, encoding='utf-8', errors='replace').read().splitlines() + open(log2, encoding='utf-8', errors='replace').read().splitlines()
              if ('Exception' in l or '/ERROR]' in l) and 'waterphysics' in l.lower()]
    check('stability', 'no exceptions from the mod in the server logs', not errors, '%d error lines' % len(errors) + ('' if not errors else ': ' + errors[0][:200]))


def write_report():
    with open(os.path.join(LOGDIR, 'server_results.json'), 'w') as f:
        json.dump(RESULTS, f, indent=1)
    passed = sum(1 for r in RESULTS if r['pass'])
    lines = ['# Server test results', '', 'Real Forge 1.12.2-14.23.5.2860 dedicated server, Java 8. %d/%d checks passed.' % (passed, len(RESULTS)), '',
             '| Test | Check | Result | Measured |', '|---|---|---|---|']
    for r in RESULTS:
        lines.append('| %s | %s | %s | %s |' % (r['test'], r['check'], 'PASS' if r['pass'] else '**FAIL**', r['detail'].replace('|', '/')))
    with open(os.path.join(LOGDIR, 'server_results.md'), 'w') as f:
        f.write('\n'.join(lines) + '\n')
    print('%d/%d checks passed' % (passed, len(RESULTS)))


if __name__ == '__main__':
    try:
        run()
    finally:
        write_report()
