"""D330 crude scanner: candidate controls by plain text patterns, independent of stangent's extractors.

Usage: python3 scan.py <clone> <out.json>
Prints counts per kind; writes every hit (file, line, kind, tag, snippet) to out.json.
Deliberately dumb and generic: no app's own names, no parsing beyond finding a tag's end.
Kinds:
  field         an element with a form binding (formControlName, [formField], [formControl], ngModel, asp-for),
                a native <input>/<select>/<textarea> (not hidden/submit/button), or a JSX element with name= and onChange=
  action        an element with an event attribute ((click), onClick, onPress) or a submit button
  server-field  a C# attribute carrying `Label =` on a property (`{ get;` within 3 lines)
  server-choice the same on anything else (an enum member: one of a field's choices)
  model-label   a C# line with a display-name attribute ([DisplayName(...)], [Display(Name...)], [...DisplayName(...)]):
                the label of a model property a view shows; overlaps `field` (asp-for), so it is not added to the total
  data-action   a handler-like property (callback:, onClick:, action: X.Y) with a name-like property within 6 lines,
                one per object (a second handler property within 4 lines of the last hit is the same entry)
A hit is also flagged `likely-skip` when its text names close/cancel/dismiss/sort/paging/collapse/expand: a rough
guess at D326's skip list, for expected sizes only.
"""
import json, os, re, sys

clone, out = sys.argv[1], sys.argv[2]
SKIP_DIRS = {'node_modules', 'bin', 'obj', 'dist', 'lib', '.git', '.stangent', 'specs', 'wwwroot', 'coverage', '.angular'}
TEST = re.compile(r'(\.spec\.|\.test\.|[\\/]tests?[\\/]|Tests?[\\/]|\.Tests?\.|[\\/]e2e[\\/]|__tests__|\.stories\.)', re.I)
MARKUP = ('.html', '.cshtml', '.tsx', '.jsx', '.js')
BINDING = re.compile(r'(formControlName\s*=|\[formField\]|\[formControl\]|\[\(ngModel\)\]|\[ngModel\]|\basp-for\s*=)')
EVENT = re.compile(r'(\(click\)\s*=|\bonClick\s*=|\bonPress\s*=)')
EVENT_HTML = re.compile(r'(\(click\)\s*=|\bonclick\s*=)', re.I)  # plain HTML/Razor writes onclick in any case
NAME_ATTR = re.compile(r'\bname\s*=')
CHANGE_ATTR = re.compile(r'\bonChange\s*=')
SUBMIT = re.compile(r'type\s*=\s*["\']submit["\']', re.I)
NONCONTROL_TYPE = re.compile(r'type\s*=\s*["\'](hidden|submit|button|reset)["\']', re.I)
TAG_START = re.compile(r'<([A-Za-z][\w.:-]*)')
LABEL_ATTR_CS = re.compile(r'^\s*\[[A-Za-z_][\w.]*\(.*\bLabel\s*=')
DISPLAY_CS = re.compile(r'^\s*\[(DisplayName|Display|[A-Za-z_]\w*DisplayName)\s*\(')
HANDLER_PROP = re.compile(r'^\s*(callback|onClick|handler|onSelect)\s*:\s*\S|^\s*action\s*:\s*[A-Z]\w*\.\w+')
NAME_PROP = re.compile(r'^\s*(title|label|text|name|caption)\s*:\s*\S')


def tag_end(s, i):
    """Index just past the '>' closing the opening tag that starts at s[i] ('<'), skipping quotes and {...}."""
    depth, q, n = 0, None, len(s)
    j = i + 1
    while j < n:
        c = s[j]
        if q:
            if c == q: q = None
        elif c in '"\'`' and depth >= 0:
            q = c
        elif c == '{': depth += 1
        elif c == '}': depth -= 1
        elif c == '>' and depth <= 0:
            return j + 1
        elif c == '<' and depth <= 0 and j > i + 1 and s[j + 1:j + 2] != '=':
            return j  # unterminated: stop at the next tag
        j += 1
    return n


def scan_markup(rel, text, hits):
    jsx = rel.endswith(('.tsx', '.jsx', '.js'))
    line_at = [0]
    for m in re.finditer('\n', text): line_at.append(m.end())
    import bisect
    for m in TAG_START.finditer(text):
        name = m.group(1)
        end = tag_end(text, m.start())
        tag = text[m.start():end]
        if len(tag) > 4000: tag = tag[:4000]
        low = name.lower()
        kind = None
        if low.endswith('label'):
            kind = None
        elif BINDING.search(tag) and not NONCONTROL_TYPE.search(tag):
            kind = 'field'
        elif low in ('input', 'select', 'textarea') and not NONCONTROL_TYPE.search(tag):
            kind = 'field'
        elif jsx and NAME_ATTR.search(tag) and CHANGE_ATTR.search(tag):
            kind = 'field'
        elif (EVENT if jsx else EVENT_HTML).search(tag):
            kind = 'action'
        elif low in ('button', 'input') and SUBMIT.search(tag):
            kind = 'action'
        if kind:
            ln = bisect.bisect_right(line_at, m.start())
            hits.append({'file': rel, 'line': ln, 'kind': kind, 'tag': name, 'snippet': ' '.join(tag.split())[:160]})


def scan_lines(rel, text, hits):
    lines = text.split('\n')
    if rel.endswith('.cs'):
        for i, l in enumerate(lines, 1):
            if LABEL_ATTR_CS.search(l):
                prop = any('get;' in x for x in lines[i:i + 3])
                hits.append({'file': rel, 'line': i, 'kind': 'server-field' if prop else 'server-choice', 'tag': '', 'snippet': l.strip()[:160]})
            elif DISPLAY_CS.search(l):
                hits.append({'file': rel, 'line': i, 'kind': 'model-label', 'tag': '', 'snippet': l.strip()[:160]})
    elif rel.endswith(('.ts', '.tsx', '.js', '.jsx')):
        last = -99
        for i, l in enumerate(lines, 1):
            if HANDLER_PROP.search(l) and i - last > 4:
                window = lines[max(0, i - 7):i + 6]
                if any(NAME_PROP.search(w) for w in window):
                    hits.append({'file': rel, 'line': i, 'kind': 'data-action', 'tag': '', 'snippet': l.strip()[:160]})
                    last = i


hits = []
for root, dirs, files in os.walk(clone):
    dirs[:] = [d for d in dirs if d not in SKIP_DIRS and not d.startswith('.')]
    for f in files:
        p = os.path.join(root, f)
        rel = os.path.relpath(p, clone)
        if TEST.search(rel): continue
        if not f.endswith(MARKUP + ('.cs', '.ts')): continue
        try:
            text = open(p, encoding='utf-8', errors='replace').read()
        except OSError:
            continue
        if f.endswith('.js') and ('min.js' in f or len(text) > 400_000): continue
        if f.endswith(MARKUP): scan_markup(rel, text, hits)
        scan_lines(rel, text, hits)

SKIPPISH = re.compile(r'close|cancel|dismiss|sort|page(size|number)?\b|paging|pagination|collapse|expand|showmore|show-more|loadmore', re.I)
for h in hits:
    h['likely_skip'] = bool(SKIPPISH.search(h['snippet']))
from collections import Counter
c = Counter(h['kind'] for h in hits)
counted = [h for h in hits if h['kind'] not in ('model-label', 'server-choice')]
skip = sum(h['likely_skip'] for h in counted)
json.dump({'clone': os.path.abspath(clone), 'counts': dict(c), 'hits': hits}, open(out, 'w'), indent=0)
print(os.path.basename(os.path.abspath(clone)), dict(sorted(c.items())), '| lines (fields, actions, server fields, data actions):', len(counted), '| likely skipped:', skip, '| after:', len(counted) - skip)
