#!/usr/bin/env python3
"""Persistence stress test: leaking tanks create wide, slowly creeping floods (many tiny changes per
tick); the server is stopped and restarted 3 times while water is still moving. After every restart the
world-wide mass audit (/water stats, loaded + unloaded chunks + pipes) must still balance."""
import json, os, shutil, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mcharness
from test_server import Game, check, RESULTS, LOGDIR

def run():
    env = mcharness.load_env()
    sdir = os.path.join(mcharness.ROOT, 'run', 'server')
    shutil.rmtree(os.path.join(sdir, 'world'), ignore_errors=True)
    T = 'persistence stress (3 restarts while water moves)'
    for cycle in range(4):
        s = mcharness.start_server(env, os.path.join(LOGDIR, 'persist_%d.log' % cycle))
        try:
            s.wait_for(r'Done \(', 300)
            g = Game(s)
            time.sleep(2)
            if cycle == 0:
                for k, (dx, dz) in enumerate(((20, 20), (-30, 20), (20, -30))):
                    inner = g.tank(dx, dz, 4, 4, 5)
                    g.cmd('/water fill %s %s 1000' % (g.P(dx + 1, 1, dz + 1), g.P(dx + 4, 5, dz + 4)))
                for (dx, dz) in ((20, 20), (-30, 20), (20, -30)):
                    g.setblock(g.P(dx + 5, 1, dz + 2), 'air')
                time.sleep(4)
            else:
                d = g.stats()
                check(T, 'restart %d: world-wide mass audit balanced' % cycle, abs(float(d['drift_l'])) < 5,
                      'drift=%s L of %s L (loaded %s, unloaded %s, pipes %s)' % (d['drift_l'], d['total_l'], d['loaded_l'], d['unloaded_l'], d['pipes_l']))
                perf = g.kv(g.cmd('/water perf'))
                check(T, 'restart %d: water still moving after reload' % cycle, int(perf['active_sections']) > 0,
                      'active sections=%s' % perf['active_sections'])
                time.sleep(8)
            d = g.stats()
            print('cycle', cycle, 'before stop: drift', d['drift_l'], 'total', d['total_l'], flush=True)
        finally:
            s.stop()

if __name__ == '__main__':
    try:
        run()
    finally:
        with open(os.path.join(LOGDIR, 'persistence_results.json'), 'w') as f:
            json.dump(RESULTS, f, indent=1)
        print('%d/%d checks passed' % (sum(r['pass'] for r in RESULTS), len(RESULTS)))
