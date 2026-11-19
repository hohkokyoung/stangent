"""D322 checklist by code: every screen and every click flow layer 3 found, grouped by area.
Usage: python3 checklist.py <clone> <out.json> <out.md>. Reads specs/flows/index.json and specs/areas.json only."""
import json, sys
clone, out_json, out_md = sys.argv[1:4]
flows = {f['id']: f for f in json.load(open(f'{clone}/specs/flows/index.json'))['flows']}
areas = json.load(open(f'{clone}/specs/areas.json'))['areas']
items, md = [], ['# Checklist: every screen and click the code has (layer 3, by code)', '',
                 'Answer every item: is it covered by the FSD (which section), left out of the FSD, or not something the FSD should describe (say why).', '']
n = 0
for a in sorted(areas, key=lambda a: a['name']):
    mine = [flows[i] for i in a['flows'] if i in flows and flows[i]['kind'] in ('screen-opens', 'click')]
    if not mine: continue
    md.append(f"## {a['name']}")
    for f in sorted(mine, key=lambda f: (f['kind'] != 'screen-opens', f['name'])):
        n += 1; iid = f'K{n:03d}'
        kind = 'screen' if f['kind'] == 'screen-opens' else 'click'
        items.append({'id': iid, 'kind': kind, 'name': f['name'], 'area': a['name'], 'start': f['start'], 'flow': f['id']})
        md.append(f"- {iid} · {kind} · {f['name']} · `{f['start']}`")
    md.append('')
json.dump({'items': items}, open(out_json, 'w'), indent=1)
open(out_md, 'w').write('\n'.join(md))
print(len(items), 'items;', sum(i['kind'] == 'screen' for i in items), 'screens,', sum(i['kind'] == 'click' for i in items), 'clicks')
