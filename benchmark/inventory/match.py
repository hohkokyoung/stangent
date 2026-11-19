"""D330 match: every scanner hit (scan.py) against the code graph's facts, by file and line. Free, no AI.

Usage: python3 match.py <scan.json> <graph.sqlite> [out.json]
A hit counts as found when a fact of its kind sits in the same file on a line the hit's element covers:
  field         a `form-field` fact on a line of the element's opening tag
  action        a `ui-event` fact on a line of the element's opening tag (Angular puts it on the event attribute's line)
  server-field  a `form-field` fact (via server) on the attribute's line
  server-choice a `field-choice` fact on the attribute's line (or a server `form-field`: the scanner can't always tell a property)
  data-action   a `menu-item` fact whose handlerLine is the hit's line, or whose object starts up to 8 lines before it
Prints found/total per kind, all hits and the hits not flagged likely-skip; writes the unmatched hits to out.json.
"""
import json, os, sqlite3, sys
from collections import defaultdict

scan_path, db_path = sys.argv[1], sys.argv[2]
out_path = sys.argv[3] if len(sys.argv) > 3 else None
scan = json.load(open(scan_path))
clone = scan['clone']

facts = defaultdict(list)  # (kind, file) -> [(line, data)]
db = sqlite3.connect(db_path)
for kind, file, line, data in db.execute("select kind, file, line, data from facts where kind in ('form-field','ui-event','field-choice','menu-item')"):
    facts[(kind, file)].append((line, json.loads(data)))


def tag_end(s, i):
    """As scan.py: index just past the '>' closing the opening tag at s[i]."""
    depth, q, n = 0, None, len(s)
    j = i + 1
    while j < n:
        c = s[j]
        if q:
            if c == q: q = None
        elif c in '"\'`' and depth >= 0: q = c
        elif c == '{': depth += 1
        elif c == '}': depth -= 1
        elif c == '>' and depth <= 0: return j + 1
        elif c == '<' and depth <= 0 and j > i + 1 and s[j + 1:j + 2] != '=': return j
        j += 1
    return n


texts = {}
def tag_lines(h):
    """How many lines the hit's opening tag spans."""
    if h['file'] not in texts:
        try: texts[h['file']] = open(os.path.join(clone, h['file']), encoding='utf-8', errors='replace').read()
        except OSError: texts[h['file']] = ''
    t = texts[h['file']]
    lines = t.split('\n')
    start = sum(len(x) + 1 for x in lines[:h['line'] - 1])
    i = t.find('<' + h['tag'], start) if h['tag'] else -1
    if i < 0: return 1
    return t.count('\n', i, tag_end(t, i)) + 1


def found(h):
    k, f, ln = h['kind'], h['file'], h['line']
    if k in ('field', 'action'):
        span = tag_lines(h)
        kind = 'form-field' if k == 'field' else 'ui-event'
        return any(ln <= l < ln + span for l, _ in facts[(kind, f)])
    if k == 'server-field':
        return any(l == ln and d.get('via') == 'server' for l, d in facts[('form-field', f)])
    if k == 'server-choice':  # the scanner's guess at "not a property" misses a property whose getter is more than 3 lines down
        return any(l == ln for l, _ in facts[('field-choice', f)]) or any(l == ln and d.get('via') == 'server' for l, d in facts[('form-field', f)])
    if k == 'data-action':
        return any(d.get('handler') and (d.get('handlerLine') == str(ln) or l <= ln <= l + 8) for l, d in facts[('menu-item', f)])
    return None


rows, unmatched = {}, []
for h in scan['hits']:
    if h['kind'] == 'model-label': continue
    ok = found(h)
    r = rows.setdefault(h['kind'], [0, 0, 0, 0])
    r[0] += ok; r[1] += 1
    if not h.get('likely_skip'):
        r[2] += ok; r[3] += 1
    if not ok: unmatched.append(h)

name = os.path.basename(clone.rstrip('/'))
print(f'{name}: kind  found/all  (not likely-skipped: found/all)')
for k in ('field', 'action', 'server-field', 'server-choice', 'data-action'):
    if k in rows:
        a, b, c, d = rows[k]
        print(f'  {k:14} {a:5}/{b:<5} {100 * a / max(b, 1):5.1f}%   ({c}/{d} {100 * c / max(d, 1):.1f}%)')
counted = [k for k in rows if k != 'server-choice']
a = sum(rows[k][2] for k in counted); b = sum(rows[k][3] for k in counted)
print(f'  lines (fields, actions, server fields, data actions; not likely-skipped): {a}/{b} {100 * a / max(b, 1):.1f}%')
if out_path: json.dump({'clone': clone, 'rows': rows, 'unmatched': unmatched}, open(out_path, 'w'), indent=0)
