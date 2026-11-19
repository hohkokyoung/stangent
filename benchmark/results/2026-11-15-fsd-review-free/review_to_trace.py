# specs/fsd/<doc>/review.json (contract fsd-review v1) -> <root>/specs/trace.yaml, the shape FsdScore reads, as
# 2026-11-15-source-only-fsd/to_trace.py does for a findings file. Usage: review_to_trace.py review.json root
import json,sys,os
r=json.load(open(sys.argv[1])); root=sys.argv[2]; os.makedirs(root+'/specs',exist_ok=True)
q=lambda s: json.dumps(s)
L=['version: 4','entries:']
for i,x in enumerate(r['findings']):
    sec=x.get('section'); scr=x.get('screen')
    L+=[f'  - id: "S{i+1:03d}"', f'    state: {q(x["state"])}']
    L+=['    fsd:', f'      doc: {q(r["doc"])}', f'      section: {q(sec)}', f'      quote: {q(x.get("fsdQuote") or "")}'] if sec else ['    fsd: null']
    L+=['    page:', f'      id: {q("page:"+scr)}', f'      quote: {q(x.get("note",""))}'] if scr else ['    page: null']
    L+=[f'    note: {q(x.get("note",""))}']
open(root+'/specs/trace.yaml','w').write('\n'.join(L)+'\n')
print(len(r['findings']),'findings written')
