"""Harness for driving a real Forge 1.12.2 dedicated server (and client) from Python tests.

The server is started from run/server (created by `gradle prepareServer`), commands are written to
its console (stdin) and assertions are made on console output lines.
"""
import os, re, subprocess, threading, time, queue, collections

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def load_env():
    env = {}
    p = os.path.join(ROOT, 'run', 'testenv.properties')
    with open(p) as f:
        for line in f:
            if '=' in line:
                k, v = line.rstrip('\n').split('=', 1)
                env[k] = v
    return env


class Proc:
    def __init__(self, args, cwd, name, logfile):
        self.name = name
        self.lines = []
        self.q = queue.Queue()
        self.log = open(logfile, 'w', encoding='utf-8')
        e = dict(os.environ)
        e['JAVA_TOOL_OPTIONS'] = ''
        self.p = subprocess.Popen(args, cwd=cwd, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  stderr=subprocess.STDOUT, env=e, bufsize=1, universal_newlines=True,
                                  encoding='utf-8', errors='replace')
        self.t = threading.Thread(target=self._read, daemon=True)
        self.t.start()

    def _read(self):
        for line in self.p.stdout:
            line = line.rstrip('\n')
            self.lines.append(line)
            self.log.write(line + '\n'); self.log.flush()
            self.q.put(line)
        self.q.put(None)

    def send(self, cmd):
        self.log.write('>>> ' + cmd + '\n'); self.log.flush()
        self.p.stdin.write(cmd + '\n')
        self.p.stdin.flush()

    def wait_for(self, pattern, timeout=60, since=None):
        """Waits for a line matching pattern; returns the re.Match. `since` = index into self.lines."""
        rx = re.compile(pattern)
        start = time.time()
        idx = since if since is not None else len(self.lines)
        while time.time() - start < timeout:
            while idx < len(self.lines):
                m = rx.search(self.lines[idx])
                idx += 1
                if m:
                    return m
            if self.p.poll() is not None and idx >= len(self.lines):
                raise RuntimeError('%s exited (code %s) while waiting for %r' % (self.name, self.p.returncode, pattern))
            time.sleep(0.05)
        raise TimeoutError('%s: timeout waiting for %r' % (self.name, pattern))

    def mark(self):
        return len(self.lines)

    def command(self, cmd, pattern, timeout=30):
        m = self.mark()
        self.send(cmd)
        return self.wait_for(pattern, timeout, since=m)

    def stop(self, cmd='stop', timeout=120):
        if self.p.poll() is None:
            try:
                self.send(cmd)
            except Exception:
                pass
            try:
                self.p.wait(timeout)
            except subprocess.TimeoutExpired:
                self.p.kill()
        self.log.close()


def start_server(env, logfile, extra_jvm=()):
    sdir = os.path.join(ROOT, 'run', 'server')
    args = [env['java8'], '-Xmx2G'] + list(extra_jvm) + ['-jar', env['server.jar'], 'nogui']
    return Proc(args, sdir, 'server', logfile)
