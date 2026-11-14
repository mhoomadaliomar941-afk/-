#!/usr/bin/env python3
"""Generates the mod's textures (procedurally, Pillow), blockstates and models.

Run from the repository root:  python3 tools/gen_assets.py
All output goes to src/main/resources/assets/waterphysics/.
"""
import json
import math
import os
import random

from PIL import Image

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', 'main', 'resources', 'assets', 'waterphysics')
TEX = os.path.join(ROOT, 'textures')


def save(img, rel):
    p = os.path.join(TEX, rel)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    img.save(p)


def clamp(v):
    return max(0, min(255, int(v)))


def noise_tex(base, var, seed, size=16):
    rnd = random.Random(seed)
    img = Image.new('RGBA', (size, size))
    for y in range(size):
        for x in range(size):
            n = rnd.uniform(-var, var)
            img.putpixel((x, y), (clamp(base[0] + n), clamp(base[1] + n), clamp(base[2] + n), 255))
    return img


def bevel(img, light=28, dark=-34):
    w, h = img.size
    for i in range(w):
        for (x, y, d) in ((i, 0, light), (0, i, light), (i, h - 1, dark), (w - 1, i, dark)):
            r, g, b, a = img.getpixel((x, y))
            img.putpixel((x, y), (clamp(r + d), clamp(g + d), clamp(b + d), a))
    return img


def rivets(img, color=(200, 205, 210)):
    for (x, y) in ((2, 2), (13, 2), (2, 13), (13, 13)):
        img.putpixel((x, y), color + (255,))
        img.putpixel((x + 1, y + 1), (60, 62, 66, 255))
    return img


def tank_wall():
    img = noise_tex((128, 134, 140), 10, 1)
    for y in range(16):
        for x in range(16):
            if y in (7, 8):
                r, g, b, a = img.getpixel((x, y))
                img.putpixel((x, y), (clamp(r - 25), clamp(g - 25), clamp(b - 25), a))
    return rivets(bevel(img))


def tank_glass():
    img = Image.new('RGBA', (16, 16), (0, 0, 0, 0))
    for i in range(16):
        for (x, y) in ((i, 0), (0, i), (i, 15), (15, i)):
            img.putpixel((x, y), (150, 160, 170, 255))
    for (x, y) in ((3, 4), (4, 3), (5, 2), (11, 10), (12, 9)):
        img.putpixel((x, y), (235, 245, 255, 170))
    for y in range(1, 15):
        for x in range(1, 15):
            if img.getpixel((x, y))[3] == 0:
                img.putpixel((x, y), (200, 225, 240, 40))
    return img


def copper(seed=3):
    img = noise_tex((184, 110, 64), 14, seed)
    for y in range(16):
        for x in range(16):
            r, g, b, a = img.getpixel((x, y))
            s = 18 * math.sin(y / 15.0 * math.pi) - 9
            img.putpixel((x, y), (clamp(r + s), clamp(g + s), clamp(b + s), a))
    for x in range(16):
        if x % 8 == 0:
            for y in range(16):
                r, g, b, a = img.getpixel((x, y))
                img.putpixel((x, y), (clamp(r - 30), clamp(g - 25), clamp(b - 20), a))
    return img


def pipe_glass():
    img = Image.new('RGBA', (16, 16), (190, 225, 245, 70))
    for x in range(16):
        img.putpixel((x, 0), (170, 190, 205, 220))
        img.putpixel((x, 15), (120, 140, 155, 220))
        img.putpixel((x, 3), (240, 250, 255, 140))
    return img


def machine_side(seed):
    img = noise_tex((92, 98, 108), 9, seed)
    for y in range(4, 12):
        for x in range(4, 12):
            if (x + y) % 3 == 0:
                img.putpixel((x, y), (60, 64, 70, 255))
    return rivets(bevel(img))


def pump_front():
    img = machine_side(11)
    cx, cy = 7.5, 7.5
    for y in range(16):
        for x in range(16):
            d = math.hypot(x - cx, y - cy)
            if d < 4.6:
                img.putpixel((x, y), (40, 70, 120, 255) if d < 3.2 else (184, 110, 64, 255))
    # arrow (outlet)
    for i in range(3):
        img.putpixel((7 + i - 1, 5 + i), (230, 230, 240, 255))
        img.putpixel((7 - i + 1, 5 + i), (230, 230, 240, 255))
    return img


def pump_back():
    img = machine_side(12)
    for y in range(16):
        for x in range(16):
            d = math.hypot(x - 7.5, y - 7.5)
            if d < 4.6:
                img.putpixel((x, y), (25, 30, 35, 255) if d < 3.2 else (150, 90, 52, 255))
    return img


def spigot_front():
    img = tank_wall()
    for y in range(16):
        for x in range(16):
            d = math.hypot(x - 7.5, y - 9.5)
            if d < 3.2:
                img.putpixel((x, y), (20, 30, 45, 255))
            elif d < 4.4:
                img.putpixel((x, y), (184, 110, 64, 255))
    for x in range(4, 12):
        img.putpixel((x, 3), (200, 40, 40, 255))
        img.putpixel((x, 4), (160, 30, 30, 255))
    img.putpixel((7, 5), (120, 120, 125, 255))
    img.putpixel((8, 5), (120, 120, 125, 255))
    return img


def valve_body():
    img = noise_tex((70, 90, 150), 10, 21)
    return rivets(bevel(img), (210, 210, 220))


def valve_wheel():
    img = Image.new('RGBA', (16, 16), (0, 0, 0, 0))
    for y in range(16):
        for x in range(16):
            d = math.hypot(x - 7.5, y - 7.5)
            if 5.2 < d < 7.4 or abs(x - 7.5) < 1 or abs(y - 7.5) < 1 and d < 7.4:
                img.putpixel((x, y), (200, 40, 40, 255))
    return img


def gauge_front():
    img = tank_wall()
    for y in range(16):
        for x in range(16):
            d = math.hypot(x - 7.5, y - 7.5)
            if d < 5.5:
                img.putpixel((x, y), (235, 235, 225, 255))
            elif d < 6.5:
                img.putpixel((x, y), (40, 40, 45, 255))
    for k in range(9):
        a = math.pi * (0.75 + 1.5 * k / 8)
        x, y = 7.5 + 4.6 * math.cos(a), 7.5 + 4.6 * math.sin(a)
        img.putpixel((int(round(x)), int(round(y))), (30, 30, 30, 255))
    for t in range(5):
        img.putpixel((7 + t // 2, 7 - t), (200, 30, 30, 255))
    # blue level window
    for y in range(9, 12):
        for x in range(6, 10):
            img.putpixel((x, y), (60, 110, 210, 255))
    return img


def hydrometer():
    img = Image.new('RGBA', (16, 16), (0, 0, 0, 0))
    for y in range(1, 15):
        for x in range(6, 10):
            edge = x in (6, 9) or y in (1, 14)
            if edge:
                img.putpixel((x, y), (200, 220, 235, 255))
            elif y >= 7:
                img.putpixel((x, y), (60, 120, 220, 255))
            else:
                img.putpixel((x, y), (230, 240, 250, 140))
    for y in (4, 7, 10, 13):
        img.putpixel((10, y), (40, 40, 40, 255))
    for x in range(5, 11):
        img.putpixel((x, 0), (184, 110, 64, 255))
        img.putpixel((x, 15), (184, 110, 64, 255))
    return img


def wrench():
    img = Image.new('RGBA', (16, 16), (0, 0, 0, 0))
    for i in range(3, 14):
        for t in (0, 1):
            x, y = i, 15 - i + t
            if 0 <= y < 16:
                img.putpixel((x, y), (190, 40, 40, 255) if i < 9 else (150, 155, 160, 255))
    for (x, y) in ((12, 1), (13, 1), (14, 2), (14, 3), (11, 2), (13, 4), (12, 3)):
        img.putpixel((x, y), (170, 175, 180, 255))
    return img


def soft_blob(size, color, power=1.6, rings=False, seed=0):
    rnd = random.Random(seed)
    img = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    c = (size - 1) / 2.0
    for y in range(size):
        for x in range(size):
            d = math.hypot(x - c, y - c) / (size / 2.0)
            if rings:
                a = max(0.0, 1 - abs(d - 0.75) * 5)
            else:
                a = max(0.0, 1 - d) ** power
                a *= 0.75 + 0.25 * rnd.random()
            img.putpixel((x, y), color + (int(255 * min(1, a)),))
    return img


def foam():
    rnd = random.Random(5)
    img = Image.new('RGBA', (16, 16), (0, 0, 0, 0))
    for _ in range(9):
        cx, cy, r = rnd.uniform(3, 12), rnd.uniform(3, 12), rnd.uniform(1.5, 3.5)
        for y in range(16):
            for x in range(16):
                d = math.hypot(x - cx, y - cy)
                if d < r:
                    a = int(255 * (1 - d / r) ** 0.6)
                    old = img.getpixel((x, y))[3]
                    img.putpixel((x, y), (245, 250, 255, max(old, a)))
    return img


def textures():
    save(tank_wall(), 'blocks/tank_wall.png')
    save(tank_glass(), 'blocks/tank_glass.png')
    save(copper(), 'blocks/pipe_copper.png')
    save(pipe_glass(), 'blocks/pipe_glass.png')
    save(machine_side(10), 'blocks/machine_side.png')
    save(pump_front(), 'blocks/pump_front.png')
    save(pump_back(), 'blocks/pump_back.png')
    save(spigot_front(), 'blocks/spigot_front.png')
    save(valve_body(), 'blocks/valve_body.png')
    save(valve_wheel(), 'blocks/valve_wheel.png')
    save(gauge_front(), 'blocks/gauge_front.png')
    save(hydrometer(), 'items/hydrometer.png')
    save(wrench(), 'items/pipe_wrench.png')
    save(foam(), 'particle/foam.png')
    save(soft_blob(16, (235, 242, 250), 1.2, seed=2), 'particle/mist.png')
    save(soft_blob(8, (190, 220, 255), 0.8, seed=3), 'particle/drop.png')
    save(soft_blob(8, (230, 245, 255), rings=True), 'particle/bubble.png')


# ------------------------------------------------------------------------------------------------
def write_json(rel, obj):
    p = os.path.join(ROOT, rel)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, 'w') as f:
        json.dump(obj, f, indent=2)
        f.write('\n')


def box(fr, to, tex, faces=('north', 'south', 'east', 'west', 'up', 'down'), uv_full=False):
    fs = {}
    for f in faces:
        fs[f] = {'texture': tex}
        if uv_full:
            fs[f]['uv'] = [0, 0, 16, 16]
    return {'from': fr, 'to': to, 'faces': fs}


def models():
    m = 'waterphysics:'
    # simple cubes
    write_json('models/block/tank_wall.json', {'parent': 'block/cube_all', 'textures': {'all': m + 'blocks/tank_wall'}})
    write_json('models/block/tank_glass.json', {'parent': 'block/cube_all', 'textures': {'all': m + 'blocks/tank_glass'}})
    for name, front, back in (('pump', 'pump_front', 'pump_back'), ('spigot', 'spigot_front', 'tank_wall')):
        side = 'machine_side' if name == 'pump' else 'tank_wall'
        write_json('models/block/%s.json' % name, {
            'parent': 'block/cube', 'textures': {
                'particle': m + 'blocks/' + side, 'north': m + 'blocks/' + front, 'south': m + 'blocks/' + back,
                'east': m + 'blocks/' + side, 'west': m + 'blocks/' + side, 'up': m + 'blocks/' + side, 'down': m + 'blocks/' + side}})
    write_json('models/block/tank_gauge.json', {
        'parent': 'block/cube', 'textures': {
            'particle': m + 'blocks/tank_wall', 'north': m + 'blocks/gauge_front', 'south': m + 'blocks/tank_wall',
            'east': m + 'blocks/tank_wall', 'west': m + 'blocks/tank_wall', 'up': m + 'blocks/tank_wall', 'down': m + 'blocks/tank_wall'}})
    # valve: pipe along Z through a body with a hand wheel on top
    write_json('models/block/valve.json', {
        'textures': {'particle': m + 'blocks/valve_body', 'body': m + 'blocks/valve_body', 'pipe': m + 'blocks/pipe_copper',
                     'wheel': m + 'blocks/valve_wheel'},
        'elements': [box([5, 5, 0], [11, 11, 16], '#pipe'), box([3.5, 3.5, 3.5], [12.5, 12.5, 12.5], '#body'),
                     box([7.5, 12.5, 7.5], [8.5, 14, 8.5], '#body'),
                     {'from': [2, 14, 2], 'to': [14, 14.5, 14], 'faces': {'up': {'texture': '#wheel'}, 'down': {'texture': '#wheel'}}}]})
    # pipes
    pipes = {'pipe_small': (2, 'pipe_copper'), 'pipe_medium': (3, 'pipe_copper'), 'pipe_large': (6, 'pipe_copper'), 'pipe_glass': (3, 'pipe_glass')}
    for name, (r, tex) in pipes.items():
        t = {'particle': m + 'blocks/' + tex, 'pipe': m + 'blocks/' + tex}
        lo, hi = 8 - r, 8 + r
        write_json('models/block/%s_center.json' % name, {'textures': t, 'elements': [box([lo, lo, lo], [hi, hi, hi], '#pipe')]})
        write_json('models/block/%s_arm.json' % name, {'textures': t, 'elements': [box([lo, lo, 0], [hi, hi, lo], '#pipe', ('east', 'west', 'up', 'down', 'north'))]})
        write_json('models/block/%s_item.json' % name, {'parent': 'block/block', 'textures': t, 'elements': [box([lo, lo, 0], [hi, hi, 16], '#pipe')]})
        mp = [{'apply': {'model': m + name + '_center'}}]
        for side, rot in (('north', {}), ('south', {'y': 180}), ('east', {'y': 90}), ('west', {'y': 270}), ('up', {'x': 270}), ('down', {'x': 90})):
            a = {'model': m + name + '_arm'}
            a.update(rot)
            mp.append({'when': {side: 'true'}, 'apply': a})
        write_json('blockstates/%s.json' % name, {'multipart': mp})
        write_json('models/item/%s.json' % name, {'parent': m + 'block/' + name + '_item'})
    # blockstates
    write_json('blockstates/tank_wall.json', {'variants': {'normal': {'model': m + 'tank_wall'}}})
    write_json('blockstates/tank_glass.json', {'variants': {'normal': {'model': m + 'tank_glass'}}})
    facing6 = {'north': {}, 'south': {'y': 180}, 'east': {'y': 90}, 'west': {'y': 270}, 'up': {'x': 270}, 'down': {'x': 90}}
    for name in ('pump', 'spigot'):
        v = {}
        for f, rot in facing6.items():
            e = {'model': m + name}
            e.update(rot)
            v['facing=' + f] = e
        write_json('blockstates/%s.json' % name, {'variants': v})
    v = {}
    for f, rot in (('north', {}), ('south', {'y': 180}), ('east', {'y': 90}), ('west', {'y': 270})):
        e = {'model': m + 'tank_gauge'}
        e.update(rot)
        v['facing=' + f] = e
    write_json('blockstates/tank_gauge.json', {'variants': v})
    write_json('blockstates/valve.json', {'variants': {'axis=z': {'model': m + 'valve'}, 'axis=x': {'model': m + 'valve', 'y': 90},
                                                      'axis=y': {'model': m + 'valve', 'x': 90}}})
    # the physical water block is rendered by a custom baked model; this entry keeps the model loader quiet
    write_json('blockstates/water.json', {'forge_marker': 1, 'variants': {'normal': {'model': 'forge:fluid', 'custom': {'fluid': 'physical_water'}}}})
    # item models
    for name in ('tank_wall', 'tank_glass', 'pump', 'spigot', 'tank_gauge', 'valve'):
        write_json('models/item/%s.json' % name, {'parent': m + 'block/' + name})
    for name in ('hydrometer', 'pipe_wrench'):
        write_json('models/item/%s.json' % name, {'parent': 'item/handheld', 'textures': {'layer0': m + 'items/' + name}})


if __name__ == '__main__':
    textures()
    models()
    print('assets generated in', os.path.normpath(ROOT))
