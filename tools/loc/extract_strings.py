#!/usr/bin/env python3
"""Lists the English display strings in the C# sources, as translation keys.

String literals, interpolated strings and "a" + x + "b" concatenations become keys; non-literal
parts become {0}, {1}... placeholders (the runtime translator matches them back with templates).
Log calls, native calls, comparisons, dictionary keys, identifiers and file names are skipped.

usage: extract_strings.py <repo-root> [--with-locations]   -> prints one key per line (escaped \\n)
"""
import os, re, sys

ROOT = sys.argv[1] if len(sys.argv) > 1 else '.'
WITH_LOC = '--with-locations' in sys.argv

INCLUDE_DIRS = ['src/OrganizationEmpire', 'src/OrganizationEmpire.Core']
EXCLUDE_PARTS = ['/obj/', '/bin/', '/Json/', '/Localization/', 'AI/Providers.cs', 'AI/Prompting.cs',
                 'Config/IniFile.cs', 'Util/Log.cs', 'Native/KeyText.cs', 'Util/NameGenerator.cs']
SKIP_CALLS = {'Call', 'GetHashKey', 'Combine', 'Exists', 'ReadAllText', 'WriteAllText', 'Equals', 'StartsWith', 'EndsWith',
              'Contains', 'IndexOf', 'Replace', 'Split', 'Parse', 'TryParse', 'Trace',
              'ConfigComment', 'HasTexture', 'Sprite', 'RequestModel', 'Request', 'Model', 'GetFiles', 'Sound', 'PlaySound',
              'TryGetValue', 'ContainsKey', 'Remove', 'Trim', 'TrimEnd', 'TrimStart', 'ToString', 'Matches', 'Match', 'IsMatch',
              'Regex', 'GetManifestResourceStream', 'Join', 'Animation', 'PlayAnimation', 'TaskPlayAnim', 'Scenario',
              'StartScenario', 'LoadAnimDict', 'WeaponHash', 'Hash', 'Value', 'Has', 'Get', 'SetProperty', 'Prop',
              'Write', 'WriteLine', 'Append', 'Section', 'ReadBool', 'ReadInt', 'ReadString', 'Key', 'Attr', 'Number', 'New',
              'Pct', 'F', 'Money', 'ParseKey', 'KeyName', 'Throw', 'ArgumentException', 'InvalidOperationException', 'Exception',
              'GetType', 'GetMethod', 'GetProperty', 'nameof', 'Assert', 'ApplyFlag', 'StableHash', 'Format_'}

TOKEN_RX = re.compile(r'''
   (?P<ws>\s+)
 | (?P<comment>//[^\n]*|/\*.*?\*/)
 | (?P<istr>\$@"(?:[^"]|"")*"|@\$"(?:[^"]|"")*"|\$"(?:[^"\\]|\\.)*")
 | (?P<vstr>@"(?:[^"]|"")*")
 | (?P<str>"(?:[^"\\\n]|\\.)*")
 | (?P<chr>'(?:[^'\\\n]|\\.)+')
 | (?P<num>\d[\w\.]*)
 | (?P<id>[A-Za-z_]\w*)
 | (?P<op>=>|==|!=|&&|\|\||\?\?|<=|>=|\+=|-=|\+\+|--|[{}()\[\];,.:?+\-*/%<>=!&|^~@#$])
''', re.S | re.X)

def tokenize(src):
    out, pos = [], 0
    while pos < len(src):
        m = TOKEN_RX.match(src, pos)
        if not m:
            out.append(('op', src[pos], pos)); pos += 1; continue
        kind = m.lastgroup
        if kind not in ('ws', 'comment'):
            out.append((kind, m.group(), pos))
        pos = m.end()
    return out

ESC = {'n': '\n', 't': '\t', '"': '"', '\\': '\\', "'": "'", '0': '', 'r': ''}

def unescape(body):
    res, i = [], 0
    while i < len(body):
        c = body[i]
        if c == '\\' and i + 1 < len(body):
            n = body[i + 1]
            if n == 'u' and i + 5 < len(body) + 0:
                try: res.append(chr(int(body[i + 2:i + 6], 16))); i += 6; continue
                except ValueError: pass
            res.append(ESC.get(n, n)); i += 2; continue
        res.append(c); i += 1
    return ''.join(res)

def literal_text(kind, text):
    """Returns (template_text_with_HOLE_markers) for literal tokens."""
    if kind == 'str': return unescape(text[1:-1])
    if kind == 'vstr': return text[2:-1].replace('""', '"')
    if kind == 'istr':
        verbatim = '@' in text[:2]
        body = text[3:-1] if verbatim else text[2:-1]
        out, i, depth = [], 0, 0
        while i < len(body):
            c = body[i]
            if c == '{':
                if body[i:i + 2] == '{{': out.append('{'); i += 2; continue
                # skip to matching }
                d, j = 1, i + 1
                while j < len(body) and d:
                    if body[j] == '{': d += 1
                    elif body[j] == '}': d -= 1
                    elif body[j] == '"':  # nested string inside hole
                        j += 1
                        while j < len(body) and body[j] != '"': j += 2 if body[j] == '\\' else 1
                    j += 1
                out.append('\x00'); i = j; continue
            if c == '}' and body[i:i + 2] == '}}': out.append('}'); i += 2; continue
            if not verbatim and c == '\\' and i + 1 < len(body):
                out.append(unescape(body[i:i + 2])); i += 2; continue
            out.append(c); i += 1
        return ''.join(out)
    return None

OPEN = {'(': ')', '[': ']', '{': '}'}
CLOSE = {v: k for k, v in OPEN.items()}
STOP_RIGHT = {',', ';', ')', ']', '}', '?', ':', '+', '=>', '==', '!=', '&&', '||', '??', '='}
STOP_LEFT = {',', ';', '(', '[', '{', '?', ':', '+', '=>', '==', '!=', '&&', '||', '??', '=', 'return', '+='}

def matching(tokens):
    m, stack = {}, []
    for i, t in enumerate(tokens):
        if t[0] == 'op' and t[1] in OPEN: stack.append(i)
        elif t[0] == 'op' and t[1] in CLOSE and stack:
            j = stack.pop(); m[i] = j; m[j] = i
    return m

def is_lit(t): return t[0] in ('str', 'vstr', 'istr')

def operand_right(tokens, j, mt):
    k = j
    while k < len(tokens):
        t = tokens[k]
        if t[0] == 'op' and t[1] in OPEN: k = mt.get(k, k) + 1; continue
        if (t[0] == 'op' and t[1] in STOP_RIGHT) or t[1] in STOP_RIGHT: break
        k += 1
    return k  # exclusive

def operand_left(tokens, j, mt):
    k = j
    while k >= 0:
        t = tokens[k]
        if t[0] == 'op' and t[1] in CLOSE: k = mt.get(k, k) - 1; continue
        if t[1] in STOP_LEFT: break
        k -= 1
    return k + 1  # inclusive start

def enclosing_calls(tokens, i, mt):
    names, depth, k = [], 0, i - 1
    while k >= 0:
        t = tokens[k]
        if t[0] == 'op' and t[1] in CLOSE: k = mt.get(k, k) - 1; continue
        if t[0] == 'op' and t[1] in OPEN:
            if t[1] in '([' and k > 0 and tokens[k - 1][0] == 'id': names.append(tokens[k - 1][1])
            elif t[1] == '[': names.append('[')
            if t[1] == '{' : pass
        if t[0] == 'op' and t[1] == ';': break
        k -= 1
    return names

def is_log_call(tokens, i, mt):
    """True when the literal is an argument of Log.Info/Warn/Error/Debug (UI items also have Info())."""
    k = i - 1
    while k >= 0:
        t = tokens[k]
        if t[0] == 'op' and t[1] in CLOSE: k = mt.get(k, k) - 1; continue
        if t[0] == 'op' and t[1] == '(':
            if k >= 3 and tokens[k - 1][1] in ('Info', 'Warn', 'Error', 'Debug') and tokens[k - 2][1] == '.' and tokens[k - 3][1] == 'Log':
                return True
        if t[0] == 'op' and t[1] == ';': return False
        k -= 1
    return False

ident_rx = re.compile(r'^[a-z0-9_\.\-/]+$|^[A-Z0-9_]+_[A-Z0-9_]+$|^#[0-9A-Fa-f]{6}$|^[a-z]+[A-Z]\w*$')

def wanted(text):
    plain = text.replace('\x00', '')
    if not re.search(r'[A-Za-z]{2,}', plain): return False
    stripped = plain.strip()
    if ident_rx.match(stripped): return False
    if re.search(r'\.(png|ini|json|txt|log|dll|asi|bak|tmp)\b', stripped, re.I) or '\\\\' in stripped: return False
    if stripped.startswith('http') or stripped.startswith('application/') or stripped.startswith('Bearer'): return False
    return True

def to_key(parts):
    """parts: list of ('lit', text) / ('hole', None) -> key with {n}."""
    flat = []
    # Named placeholders filled with Replace() ("{title}", "{name}") are holes too.
    parts = [(k, re.sub(r'\{[A-Za-z_][A-Za-z0-9_]*\}', '\x00', t) if k == 'lit' else t) for k, t in parts]
    for kind, txt in parts:
        if kind == 'lit':
            for idx, seg in enumerate(txt.split('\x00')):
                if idx > 0: flat.append(('hole', None))
                if seg: flat.append(('lit', seg))
        else: flat.append(('hole', None))
    merged = []
    for p in flat:
        if p[0] == 'hole' and merged and merged[-1][0] == 'hole': continue
        if p[0] == 'lit' and merged and merged[-1][0] == 'lit': merged[-1] = ('lit', merged[-1][1] + p[1]); continue
        merged.append(p)
    out, n = [], 0
    for kind, txt in merged:
        if kind == 'hole': out.append('{%d}' % n); n += 1
        else: out.append(txt)
    return ''.join(out)

def process(path, keys):
    src = open(path, encoding='utf-8').read()
    tokens = tokenize(src)
    mt = matching(tokens)
    used = set()
    for i, t in enumerate(tokens):
        if not is_lit(t) or i in used: continue
        # context filters
        prev = tokens[i - 1] if i > 0 else ('', '', 0)
        if prev[1] == 'case' or prev[1] in ('==', '!='): continue
        if i + 1 < len(tokens) and tokens[i + 1][1] in ('==', '!='): continue
        calls = enclosing_calls(tokens, i, mt)
        if calls and calls[0] == '[' : continue
        if any(c in SKIP_CALLS for c in calls[:2]): continue
        if 'Log' in [tokens[k][1] for k in range(max(0, i - 6), i)]: continue
        if is_log_call(tokens, i, mt): continue
        # build chain
        start = i
        while start > 1 and tokens[start - 1][0] == 'op' and tokens[start - 1][1] == '+':
            left = operand_left(tokens, start - 2, mt)
            if left >= start - 1: break
            start = left
        if start < i and any(c in SKIP_CALLS for c in enclosing_calls(tokens, start, mt)[:2]): continue
        k = start
        parts = []
        while True:
            end = operand_right(tokens, k, mt)
            seg = tokens[k:end]
            if len(seg) == 1 and is_lit(seg[0]):
                parts.append(('lit', literal_text(seg[0][0], seg[0][1]))); used.add(k)
            elif len(seg) == 0: break
            else: parts.append(('hole', None))
            if end < len(tokens) and tokens[end][1] == '+' and tokens[end][0] == 'op':
                k = end + 1; continue
            break
        if not any(p[0] == 'lit' for p in parts): continue
        key = to_key(parts).strip()
        if not wanted(key): continue
        line = src.count('\n', 0, t[2]) + 1
        keys.setdefault(key, []).append('%s:%d' % (os.path.relpath(path, ROOT), line))

def main():
    keys = {}
    for d in INCLUDE_DIRS:
        for base, _, files in os.walk(os.path.join(ROOT, d)):
            for f in files:
                if not f.endswith('.cs'): continue
                p = os.path.join(base, f).replace('\\', '/')
                if any(x in p for x in EXCLUDE_PARTS): continue
                process(p, keys)
    for k in sorted(keys, key=lambda s: (keys[s][0], s)):
        esc = k.replace('\\', '\\\\').replace('\t', '\\t').replace('\n', '\\n')
        print(esc + ('\t' + keys[k][0] if WITH_LOC else ''))

main()
