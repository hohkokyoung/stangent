"""D322 code checks on the agent's output. Usage: python3 check_output.py <findings.json> <checklist.json> <fsd.md> <clone>.
Prints every problem; exit 1 when any. Checks: each checklist item answered once with a known verdict; a 'covered' item quotes, word for word, the FSD sentence inside the named section that covers it; a 'left-out' item is
backed by an undocumented finding naming it; every fsdQuote is word for word in the FSD; every evidence file:line exists (a path with no line must exist)."""
import json, sys, re, os
fp, cp, fsdp, clone = sys.argv[1:5]
out = json.load(open(fp)); items = {i['id'] for i in json.load(open(cp))['items']}
norm = lambda s: re.sub(r'\s+', ' ', s or '').strip()
fsd = norm(re.sub(r'[*`|]', ' ', open(fsdp).read()))
problems = []
# Section texts, by the id in each heading's brackets: the heading's own lines and its subsections', up to the next heading
# of the same or a higher level.
lines = open(fsdp).read().split('\n'); heads = []
for k, l in enumerate(lines):
    m = re.match(r'^(#+) .*\[section id: (.+)\]\s*$', l)
    if m: heads.append((k, len(m.group(1)), m.group(2)))
sections = {}
for n_, (k, lvl, sid) in enumerate(heads):
    end = next((k2 for k2, l2, _ in heads[n_ + 1:] if l2 <= lvl), len(lines))
    sections[sid] = norm(re.sub(r'[*`|]', ' ', '\n'.join(lines[k + 1:end])))
answers = out.get('checklist', [])
seen = {}
for a in answers:
    i = a.get('id')
    if i not in items: problems.append(f'checklist: unknown item {i}'); continue
    if i in seen: problems.append(f'checklist: {i} answered twice')
    seen[i] = a
    if a.get('verdict') not in ('covered', 'left-out', 'not-for-fsd'): problems.append(f"checklist: {i} has verdict {a.get('verdict')!r}")
    if a.get('verdict') == 'covered':
        sec, q = a.get('section'), norm(re.sub(r'[*`|]', ' ', a.get('quote') or ''))
        if sec not in sections: problems.append(f'checklist: {i} covered by unknown section {sec!r}')
        elif not q: problems.append(f'checklist: {i} covered but quotes no FSD sentence')
        elif q not in sections[sec]: problems.append(f'checklist: {i} quote is not word for word in section {sec!r}: {q[:80]!r}')
    if a.get('verdict') == 'not-for-fsd' and not a.get('reason'): problems.append(f'checklist: {i} not-for-fsd with no reason')
for i in sorted(items - set(seen)): problems.append(f'checklist: {i} not answered')
backed = {k for f in out.get('findings', []) if f.get('state') == 'undocumented' for k in (f.get('checklist') or [])}
for i, a in seen.items():
    if a.get('verdict') == 'left-out' and i not in backed: problems.append(f'checklist: {i} left-out but no undocumented finding lists it')
for n, f in enumerate(out.get('findings', []), 1):
    q = f.get('fsdQuote')
    if f.get('state') in ('gap', 'conflict'):
        if not q: problems.append(f'finding {n}: {f.get("state")} with no fsdQuote')
        elif norm(re.sub(r'[*`|]', ' ', q)) not in fsd: problems.append(f'finding {n}: fsdQuote not word for word in the FSD: {q[:80]!r}')
    for e in f.get('evidence') or []:
        m = re.match(r'^(.+?):(\d+)(?:[-–]\d+)?$', e.strip())
        if not m:   # a path with no line (a gap points at where a thing would be): it must exist
            if not os.path.exists(os.path.join(clone, e.strip())): problems.append(f'finding {n}: no such path {e!r}')
            continue
        path = os.path.join(clone, m.group(1))
        if not os.path.isfile(path): problems.append(f'finding {n}: no such file {m.group(1)}'); continue
        if int(m.group(2)) > sum(1 for _ in open(path, errors='ignore')): problems.append(f'finding {n}: line {m.group(2)} past the end of {m.group(1)}')
print('\n'.join(problems) or 'all checks pass'); sys.exit(1 if problems else 0)
