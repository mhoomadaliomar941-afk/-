#!/usr/bin/env python3
"""Automated client tests on the real Forge 1.12.2 client (headless, Xvfb + Mesa OpenGL).

singleplayer: the client creates a flat world (integrated server) and runs tests/client/singleplayer.txt
multiplayer:  a dedicated server is started, the client connects to it and the server builds a scene;
              the client must receive the physics sync packets and render them.
Screenshots: tests/results/screenshots/
"""
import os, re, shutil, subprocess, sys, time, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mcharness

ROOT = mcharness.ROOT
RES = os.path.join(ROOT, 'tests', 'results')
SHOTS = os.path.join(RES, 'screenshots')
os.makedirs(SHOTS, exist_ok=True)
RESULTS = []


def check(test, name, ok, detail):
    RESULTS.append({'test': test, 'check': name, 'pass': bool(ok), 'detail': detail})
    print(('  PASS ' if ok else '  FAIL ') + test + ' :: ' + name + ' :: ' + detail, flush=True)


def ensure_xvfb():
    if subprocess.run(['pgrep', 'Xvfb'], capture_output=True).returncode != 0:
        subprocess.Popen(['Xvfb', ':99', '-screen', '0', '1280x720x24', '-nolisten', 'tcp'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        time.sleep(2)
    os.environ['DISPLAY'] = ':99'


def client_args(env, extra_props, extra_args):
    cdir = os.path.join(ROOT, 'run', 'client')
    os.makedirs(os.path.join(cdir, 'assets'), exist_ok=True)
    # an unfocused (Xvfb) window would otherwise open the pause menu, which pauses singleplayer
    with open(os.path.join(cdir, 'options.txt'), 'w') as f:
        f.write('pauseOnLostFocus:false\nrenderDistance:8\nfancyGraphics:true\nao:2\nguiScale:2\nfov:0.25\nsoundCategory_master:0.0\n'
                'skipMultiplayerWarning:true\ntutorialStep:none\n')
    return [env['java8'], '-Xmx2G', '-Djavax.accessibility.assistive_technologies= ', '-Djava.library.path=' + env['client.natives'],
            '-Dfml.ignoreInvalidMinecraftCertificates=true'] + extra_props + ['-cp', env['client.classpath'],
            'net.minecraft.launchwrapper.Launch', '--tweakClass', 'net.minecraftforge.fml.common.launcher.FMLTweaker',
            '--username', 'Player', '--version', '1.12.2', '--gameDir', cdir, '--assetsDir', os.path.join(cdir, 'assets'),
            '--assetIndex', '1.12', '--accessToken', '0', '--userType', 'legacy', '--uuid', '00000000000000000000000000000001',
            '--width', '1280', '--height', '720'] + extra_args, cdir


def collect(prefix, cdir):
    shots = []
    sd = os.path.join(cdir, 'screenshots')
    if os.path.isdir(sd):
        for f in sorted(os.listdir(sd)):
            if f.startswith(prefix):
                shutil.copy(os.path.join(sd, f), os.path.join(SHOTS, f))
                shots.append(f)
    return shots


def chat(lines, key):
    return [l.split('[CHAT] ')[-1] for l in lines if '[CHAT]' in l and key in l]


def kv(line):
    return {k: v for k, v in re.findall(r'(\w+)=([^\s]+)', line)}


def singleplayer(env):
    T = 'singleplayer (integrated server)'
    cdir = os.path.join(ROOT, 'run', 'client')
    shutil.rmtree(os.path.join(cdir, 'saves', 'wptest'), ignore_errors=True)
    shutil.rmtree(os.path.join(cdir, 'screenshots'), ignore_errors=True)
    script = os.path.join(ROOT, 'tests', 'client', 'singleplayer.txt')
    args, cdir = client_args(env, ['-Dwaterphysics.autotest=' + script, '-Dwaterphysics.autotest.world=wptest'], [])
    p = mcharness.Proc(args, cdir, 'client', os.path.join(RES, 'client_singleplayer.log'))
    try:
        p.wait_for(r'\[WP-test\] done|Game crashed|FATAL', timeout=1500)
    finally:
        time.sleep(5)
        if p.p.poll() is None:
            p.p.terminate()
            time.sleep(5)
            p.p.kill()
        p.log.close()
    L = p.lines
    shots = collect('sp_', cdir)
    crashed = any('Game crashed' in l for l in L)
    check(T, 'client started, world created, scenario completed without crash', any('[WP-test] done' in l for l in L) and not crashed,
          'log lines=%d crashed=%s' % (len(L), crashed))
    use = [l for l in L if '[WP-test] use ->' in l]
    held = [l for l in L if '[WP-test] held' in l]
    check(T, 'player right-clicks with a water bucket: bucket becomes empty', bool(held) and 'minecraft:bucket' in held[0],
          (use[0].split('[WP-test] ')[-1] if use else 'no use') + '; ' + (held[0].split('[WP-test] ')[-1] if held else 'no held'))
    regions = [kv(l) for l in chat(L, '[WP] region')]
    if regions:
        check(T, 'the bucket placed exactly 1000 L of physical water', abs(float(regions[0]['volume_l']) - 1000) < 1, 'region: %s L' % regions[0]['volume_l'])
    if len(regions) >= 6:
        full, l1, l2, c1, c2 = [float(regions[i]['volume_l']) for i in (1, 2, 3, 4, 5)]
        check(T, 'tank filled (60000 L + displaced puddle), leak lowers the level, closing the hole stops it',
              60000 - 1 <= full < 61000 and l1 < full - 1000 and l2 < l1 and abs(c2 - c1) < 2,
              'full=%.0f after leak=%.0f -> %.0f; closed: %.1f -> %.1f' % (full, l1, l2, c1, c2))
    pipes = [kv(l) for l in chat(L, '[WP] pipe')]
    if pipes:
        check(T, 'pump pushes water up into glass pipes', float(pipes[0].get('pump_flow_lps', '0')) > 1 and float(pipes[1]['volume_l']) > 1,
              'pump flow=%s L/s, glass pipe holds %s L' % (pipes[0].get('pump_flow_lps'), pipes[1]['volume_l']))
    stats = [l.split('[WP-test] ')[-1] for l in L if 'client stats' in l]
    pk = [int(kv(s)['packets']) for s in stats]
    check(T, 'client receives physics detail packets from the (integrated) server', pk and max(pk) > 0, '; '.join(stats))
    ov = [int(kv(s)['overlayCells']) for s in stats]
    check(T, 'wave/ripple overlay rendered on moving water', ov and max(ov) > 0, 'overlay cells per frame: %s' % ov)
    st = chat(L, '[WP] stats')
    if st:
        d = kv(st[-1])
        check(T, 'mass conservation in singleplayer', abs(float(d['drift_l'])) < 5, 'drift=%s L of %s L' % (d['drift_l'], d['total_l']))
    errs = [l for l in L if ('Exception' in l or '/ERROR]' in l) and ('waterphysics' in l.lower() or 'WP-test' in l)]
    check(T, 'no exceptions from the mod in the client log', not errs, '%d lines%s' % (len(errs), (': ' + errs[0][:180]) if errs else ''))
    model_errs = [l for l in L if 'waterphysics' in l and ('Exception loading' in l or 'missing' in l.lower() and 'texture' in l.lower())]
    check(T, 'all mod models and textures load', not model_errs, '%d model/texture errors%s' % (len(model_errs), (': ' + model_errs[0][:160]) if model_errs else ''))
    check(T, 'screenshots captured', len(shots) >= 8, ', '.join(shots))


def multiplayer(env):
    T = 'multiplayer (dedicated server + client)'
    sdir = os.path.join(ROOT, 'run', 'server')
    shutil.rmtree(os.path.join(sdir, 'world'), ignore_errors=True)
    s = mcharness.start_server(env, os.path.join(RES, 'mp_server.log'))
    cl = None
    try:
        s.wait_for(r'Done \(', 300)
        from test_server import Game
        g = Game(s)
        cdir = os.path.join(ROOT, 'run', 'client')
        script = os.path.join(ROOT, 'tests', 'client', 'multiplayer.txt')
        with open(script, 'w') as f:
            f.write('closegui\nhidegui true\nwait 200\nstats\nscreenshot mp_01_leak\nwait 50\nstats\nscreenshot mp_02_leak_later\nwait 40\nstats\nlog scenario end\n')
        args, cdir = client_args(env, ['-Dwaterphysics.autotest=' + script], ['--server', 'localhost', '--port', '25565'])
        cl = mcharness.Proc(args, cdir, 'client', os.path.join(RES, 'client_multiplayer.log'))
        s.wait_for(r'Player joined the game', 600)
        g.cmd('/op Player', r'Made Player a server operator|Opped')
        # scene: glass tank, leaking; player looks at it
        g.mc('/time set 6000', r'Set the time')
        inner = g.tank(3, 6, 4, 3, 5, block='waterphysics:tank_glass')
        g.cmd('/water fill %s %s 1000' % (g.P(4, 1, 7), g.P(7, 4, 9)))
        g.mc('/gamemode spectator Player', r'game mode')
        g.mc('/tp Player %s 50 18' % g.P(15, 4, 3), r'Teleported')
        time.sleep(4)
        g.setblock(g.P(8, 2, 8), 'air')
        cl.wait_for(r'\[WP-test\] scenario end|Game crashed', 600)
        v, c, surf, t = g.region(*inner)
        check(T, 'server side: the tank leaks while a client is connected', v < 80000 - 1000, 'tank volume now %.0f L' % v)
    finally:
        if cl is not None:
            if cl.p.poll() is None:
                cl.p.terminate()
                time.sleep(5)
                cl.p.kill()
            cl.log.close()
        s.stop()
    L = cl.lines if cl else []
    joined = any('Player joined the game' in l for l in s.lines)
    check(T, 'client connects to the dedicated server (mod handshake ok)', joined, 'joined=%s' % joined)
    stats = [l.split('[WP-test] ')[-1] for l in L if 'client stats' in l]
    pk = [int(kv(x)['packets']) for x in stats]
    check(T, 'client receives physics sync packets over the network', pk and max(pk) > 0, '; '.join(stats))
    ov = [int(kv(x)['overlayCells']) for x in stats]
    check(T, 'client renders effects from synced data (overlay cells > 0)', ov and max(ov) > 0, 'overlay cells: %s' % ov)
    shots = collect('mp_', os.path.join(ROOT, 'run', 'client'))
    check(T, 'screenshots captured', len(shots) >= 2, ', '.join(shots))
    errs = [l for l in L + s.lines if ('Exception' in l or '/ERROR]' in l) and 'waterphysics' in l.lower()]
    check(T, 'no exceptions from the mod (client and server)', not errs, '%d lines%s' % (len(errs), (': ' + errs[0][:180]) if errs else ''))


def report():
    with open(os.path.join(RES, 'client_results.json'), 'w') as f:
        json.dump(RESULTS, f, indent=1)
    passed = sum(1 for r in RESULTS if r['pass'])
    lines = ['# Client test results', '', 'Real Forge 1.12.2-14.23.5.2860 client (Java 8, Xvfb + Mesa llvmpipe OpenGL). %d/%d checks passed.' % (passed, len(RESULTS)), '',
             '| Test | Check | Result | Measured |', '|---|---|---|---|']
    for r in RESULTS:
        lines.append('| %s | %s | %s | %s |' % (r['test'], r['check'], 'PASS' if r['pass'] else '**FAIL**', r['detail'].replace('|', '/')))
    with open(os.path.join(RES, 'client_results.md'), 'w') as f:
        f.write('\n'.join(lines) + '\n')
    print('%d/%d checks passed' % (passed, len(RESULTS)))


if __name__ == '__main__':
    ensure_xvfb()
    env = mcharness.load_env()
    which = sys.argv[1:] or ['singleplayer', 'multiplayer']
    try:
        if 'singleplayer' in which:
            singleplayer(env)
        if 'multiplayer' in which:
            multiplayer(env)
    finally:
        report()
