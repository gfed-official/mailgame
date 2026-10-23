#!/usr/bin/env python3
"""Regenerate production/epics/implementation-order.md (playable-first, m1a-replan 2026-10-23).
Usage: python3 tools/planning/gen_impl_order.py <repo-root>. Docs tooling only; not part of the build."""
import os, re, sys, glob, collections
repo = sys.argv[1]; E = os.path.join(repo, 'production/epics')
EPICS = ['simulation-core','content-data','input-controls','godot-host-shell','grid-inventory','world-stub',
         'network-adapter-loopback','mail-destinations','player-character','manual-delivery','throwing-catching','hud-notifications']
epics = [e for e in EPICS if os.path.isdir(os.path.join(E, e))] + sorted(
    d for d in os.listdir(E) if os.path.isdir(os.path.join(E, d)) and d not in EPICS)
# Playable-first leaves (Mail Lead planning default, approved by Evan 2026-10-23 8:10 AM PT (TD-173)): the visible greybox path.
PLAYABLE_LEAVES = ['godot-host-shell/010','godot-host-shell/009','input-controls/028','player-character/022',
  'player-character/023','player-character/024','player-character/026','mail-destinations/016','manual-delivery/011',
  'manual-delivery/010','throwing-catching/018','throwing-catching/019','throwing-catching/020','throwing-catching/021',
  'grid-inventory/023','grid-inventory/019','grid-inventory/020','player-character/025','throwing-catching/012','throwing-catching/013','hud-notifications/001','hud-notifications/002','hud-notifications/003']
S = {}
for e in epics:
    for f in sorted(glob.glob(os.path.join(E, e, 'story-*.md'))):
        n = re.match(r'story-(\d+)', os.path.basename(f)).group(1)
        t = open(f).read()
        st = re.search(r'^> \*\*Status\*\*: (\S+(?: Progress)?)', t, re.M)
        st = st.group(1) if st else '?'
        if st.startswith('In') and 'Progress' not in st: st = 'In Progress' if 'In Progress' in t[:600] else st
        title = re.match(r'# Story \d+: (.*)', t.splitlines()[0]); title = title.group(1) if title else ''
        dep = re.search(r'^- Depends on:(.*)$', t, re.M)
        S[f'{e}/{n}'] = dict(file=os.path.relpath(f, E), status=st, title=title, deps_raw=dep.group(1) if dep else '')
def strip_paren(s):
    prev = None
    while prev != s: prev, s = s, re.sub(r'\([^()]*\)', '', s)
    return s
def parse(key, raw):
    own = key.split('/')[0]; out = []
    raw = re.split(r'(?i)not a dependency', raw)[0]
    for m in re.finditer(r'production/epics/([\w-]+)/story-(\d+)', raw): out.append(f'{m.group(1)}/{m.group(2)}')
    raw2 = re.sub(r'`?production/epics/[\w-]+/story-[\w.-]+`?', ' PATH ', raw)
    raw2 = strip_paren(raw2)
    # bare `story-NNN-...` -> epic of the nearest preceding path in the original text, else own
    ctx = own
    for m in re.finditer(r'production/epics/([\w-]+)/story-\d+|(?<![/\w])`story-(\d+)-', raw):
        if m.group(1): ctx = m.group(1)
        else: out.append(f'{ctx}/{m.group(2)}')
    ctx = own
    toks = re.finditer(r'([\w-]+)\s+Stor(?:y|ies)\s+([\d,\s–\-and]+)|Stor(?:y|ies)\s+([\d,\s–\-and]+)', raw2)
    for m in toks:
        if m.group(1) and m.group(1) in epics: ctx = m.group(1); nums = m.group(2)
        elif m.group(1): ctx = own; nums = m.group(2)
        else: nums = m.group(3)
        for a, b in re.findall(r'(\d+)\s*[–-]\s*(\d+)', nums):
            for i in range(int(a), int(b) + 1): out.append(f'{ctx}/{i:03d}')
        nums = re.sub(r'(\d+)\s*[–-]\s*(\d+)', '', nums)
        for x in re.findall(r'\d+', nums): out.append(f'{ctx}/{int(x):03d}')
    return sorted({d for d in out if d != key})
missing = []
for k, v in S.items():
    v['deps'] = [d for d in parse(k, v['deps_raw']) if d in S]
    missing += [(k, d) for d in parse(k, v['deps_raw']) if d not in S]
DONE = {'Complete', 'Done', 'In Progress'}
# cycle check (Tarjan)
idx, low, on, stack, sccs, c = {}, {}, set(), [], [], [0]
sys.setrecursionlimit(10000)
def sc(v):
    idx[v] = low[v] = c[0]; c[0] += 1; stack.append(v); on.add(v)
    for w in S[v]['deps']:
        if w not in idx: sc(w); low[v] = min(low[v], low[w])
        elif w in on: low[v] = min(low[v], idx[w])
    if low[v] == idx[v]:
        comp = []
        while True:
            w = stack.pop(); on.discard(w); comp.append(w)
            if w == v: break
        if len(comp) > 1: sccs.append(comp)
for v in S:
    if v not in idx: sc(v)
edges = sum(len(v['deps']) for v in S.values())
# playable closure
play = set()
def close(k):
    if k in play or k not in S: return
    play.add(k)
    for d in S[k]['deps']: close(d)
for l in PLAYABLE_LEAVES: close(l)
deferred = [k for k in S if S[k]['status'] == 'Deferred']
blocked = [k for k in S if S[k]['status'] == 'Blocked']
# stories (Ready/other) that depend transitively on Blocked/Deferred are gated
bad_dep = [(k, d) for k in S if S[k]['status'] not in {'Deferred'} | DONE for d in S[k]['deps'] if S[d]['status'] == 'Deferred']
def ancestors_status(k, seen=None):
    seen = seen or set(); res = set()
    for d in S[k]['deps']:
        if d in seen: continue
        seen.add(d)
        if S[d]['status'] in ('Blocked', 'Deferred'): res.add(d)
        res |= ancestors_status(d, seen)
    return res
sched = [k for k in S if S[k]['status'] not in DONE | {'Blocked', 'Deferred'}]
gated = {k: ancestors_status(k) for k in sched}; gated = {k: v for k, v in gated.items() if v}
order = {k: i for i, k in enumerate(S)}
def waves(keys, base_done):
    done = set(base_done); rem = [k for k in keys]; W = []
    while rem:
        w = [k for k in rem if all(d in done for d in S[k]['deps'])]
        if not w: break
        W.append(sorted(w, key=order.get)); done |= set(w); rem = [k for k in rem if k not in w]
    return W, done, rem
base = {k for k in S if S[k]['status'] in DONE}
trackA = [k for k in sched if k in play and k not in gated]
trackB = [k for k in sched if k not in play and k not in gated]
WA, doneA, remA = waves(trackA, base)
WB, doneB, remB = waves(trackB, doneA)
cnt = collections.Counter(v['status'] for v in S.values())
def link(k): return f"[{k}]({S[k]['file']})"
def short(t): return (t[:77] + '…') if len(t) > 78 else t
L = []
L.append('# Implementation Order — all epics (M-1a, playable-first)\n')
L.append(f"> **Generated**: 2026-10-23 (m1a-replan) by `python3 tools/planning/gen_impl_order.py .` from every story's `## Dependencies` → `- Depends on:` line in `production/epics/*/story-*.md`.")
L.append(f"> **Scope**: {len(S)} stories in {len(epics)} epics — " + ', '.join(f'{cnt[s]} {s}' for s in sorted(cnt, key=lambda s: -cnt[s])) + '.')
L.append('> **Authority for the playable-first order and the deferrals**: Evan, Mail Lead chat, 2026-10-23 7:13 AM PT: "Approve all the recommendations: reorder playable-first, add a minimal HUD, the deferrals, the one-page telemetry ADR, narrow manual-delivery 010, defer throwing-catching 022–024"; 7:15 AM PT: "This is a game among friends, so a lot of the extra work to make it \'production ready\' is not needed. Accessibility for example". See `production/session-logs/decision-log.md` 2026-10-23.')
L.append('> **Regenerate** whenever a story\'s Depends on or Status changes. The order is advisory; `/story-readiness` still gates each story.\n')
L.append('## How to read this\n')
L.append('- **Track A — playable greybox first.** Every not-yet-done story that the visible greybox path needs: the Godot host shell, input wiring, the player view and camera, deliver, throw, the mailbox view and the minimal HUD. It is the transitive dependency closure of these leaves (Mail Lead planning default, approved by Evan 2026-10-23 8:10 AM PT (TD-173)): ' + ', '.join(f'`{l}`' for l in PLAYABLE_LEAVES) + '.')
L.append('- **Track B — everything else that is still M-1a.** Infrastructure-only, test-only and ADR-required hardening stories that the playable path does not need. They start after Track A, or in parallel when an implementer is idle.')
L.append('- **Wave N** = stories whose dependencies are all in earlier waves (or Complete / In Progress). Stories inside one wave can run in parallel; check declared files for overlaps before running two at once.')
L.append('- **Gated** = Ready, but depends (transitively) on a Blocked story. **Deferred** = moved out of M-1a by Evan on 2026-10-23; not scheduled.\n')
L.append('## Dependency-cycle check\n')
L.append(f'- **Depends-on graph: {len(sccs)} cycles** (Tarjan SCC over {edges} edges, {len(S)} nodes).' + (' None found.' if not sccs else ' ' + '; '.join(', '.join(c) for c in sccs)))
if missing: L.append(f'- Unresolved references ({len(missing)}): ' + ', '.join(f'{a}→{b}' for a, b in missing[:20]))
L.append(f'- **Ready stories depending on a Deferred story: {len(bad_dep)}.**' + ('' if not bad_dep else ' ' + ', '.join(f'{a}→{b}' for a, b in bad_dep)))
L.append('')
def table(W, label):
    for i, w in enumerate(W, 1):
        L.append(f'### {label} wave {i} ({len(w)})\n')
        L.append('| Story | Title | Status | Open dependencies (earlier waves) |'); L.append('|---|---|---|---|')
        for k in w:
            od = [d for d in S[k]['deps'] if d not in base]
            L.append(f"| {link(k)} | {short(S[k]['title'])} | {S[k]['status']} | {', '.join(od) or '—'} |")
        L.append('')
L.append(f'## Track A — playable greybox ({sum(map(len, WA))} stories, {len(WA)} waves)\n'); table(WA, 'A')
L.append(f'## Track B — remaining M-1a ({sum(map(len, WB))} stories, {len(WB)} waves)\n'); table(WB, 'B')
if remA or remB: L.append('## Unschedulable (dependency not satisfiable)\n\n' + '\n'.join(f'- {link(k)}' for k in remA + remB) + '\n')
L.append(f'## Blocked ({len(blocked)})\n')
L.append('| Story | Title | Story deps |'); L.append('|---|---|---|')
for k in blocked: L.append(f"| {link(k)} | {short(S[k]['title'])} | {', '.join(S[k]['deps']) or '—'} |")
L.append(f'\n## Ready but gated behind a Blocked story ({len(gated)})\n')
L.append('| Story | Track | Gated by |'); L.append('|---|---|---|')
for k, g in gated.items(): L.append(f"| {link(k)} | {'A' if k in play else 'B'} | {', '.join(sorted(g))} |")
L.append(f'\n## Deferred — out of the M-1a slice ({len(deferred)})\n')
L.append('Each story file carries the "Deferred (Evan 2026-10-23 7:13 AM PT)" status, both of Evan\'s quotes and its target milestone. Nothing was deleted.\n')
L.append('| Story | Title | Deferred to |'); L.append('|---|---|---|')
for k in deferred:
    t = open(os.path.join(E, S[k]['file'])).read(); m = re.search(r'^> \*\*Deferred to\*\*: (.*)$', t, re.M)
    L.append(f"| {link(k)} | {short(S[k]['title'])} | {m.group(1) if m else '?'} |")
L.append(f'\n## Wave 0 — Complete / In Progress ({len(base)})\n')
L.append(', '.join(f'{link(k)}' + (' (In Progress)' if S[k]['status'] == 'In Progress' else '') for k in sorted(base, key=order.get)))
open(os.path.join(E, 'implementation-order.md'), 'w').write('\n'.join(L) + '\n')
print(f'stories={len(S)} edges={edges} cycles={len(sccs)} missing={len(missing)} trackA={sum(map(len,WA))}/{len(WA)}w trackB={sum(map(len,WB))}/{len(WB)}w blocked={len(blocked)} gated={len(gated)} deferred={len(deferred)} unsched={len(remA+remB)}')
print('bad_dep', bad_dep); print('missing', missing[:30]); print('gated', gated)
import json
pos = {}
for i, w in enumerate(WA, 1):
    for k in w: pos[k] = ('A', i)
for i, w in enumerate(WB, 1):
    for k in w: pos[k] = ('B', i)
if os.environ.get('IMPL_ORDER_JSON'): json.dump({'pos': pos, 'status': {k: v['status'] for k, v in S.items()}, 'title': {k: v['title'] for k, v in S.items()}, 'gated': {k: sorted(v) for k, v in gated.items()}}, open(os.environ['IMPL_ORDER_JSON'], 'w'), indent=0)
