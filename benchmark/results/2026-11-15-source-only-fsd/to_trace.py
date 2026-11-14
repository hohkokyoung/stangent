# Agent findings (findings.json) -> <root>/specs/trace.yaml, the shape FsdScore reads. Usage: to_trace.py findings.json root
import json,sys,os
f=json.load(open(sys.argv[1])); root=sys.argv[2]; os.makedirs(root+'/specs',exist_ok=True)
q=lambda s: json.dumps(s if s is not None else None)
L=['version: 4','entries:']
for i,x in enumerate(f['findings']):
    st=x['state']; sec=x.get('section'); quote=x.get('fsdQuote'); scr=x.get('screen')
    L+= [f'  - id: "S{i+1:03d}"', f'    state: {q(st)}']
    L+= ['    fsd:', '      doc: "sonarr-fsd"', f'      section: {q(sec)}', f'      quote: {q(quote or "")}'] if sec else ['    fsd: null']
    L+= ['    page:', f'      id: {q("page:"+scr)}', f'      quote: {q(x.get("note",""))}'] if scr else ['    page: null']
    L+= [f'    note: {q(x.get("note",""))}']
open(root+'/specs/trace.yaml','w').write('\n'.join(L)+'\n')
print(len(f['findings']),'findings written')
