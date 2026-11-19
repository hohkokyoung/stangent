import json, os, sqlite3, sys
root = sys.argv[1]
skip = {"node_modules",".git","bin","obj","dist",".stangent","specs","artifacts"}
names = {"en.json","en-us.json","en_us.json"}
maps = []
for d, ds, fs in os.walk(root):
    ds[:] = sorted(x for x in ds if x.lower() not in skip)
    for f in sorted(fs):
        if f.lower() in names:
            try: j = json.load(open(os.path.join(d, f)))
            except Exception: continue
            m = {}
            def flat(e, p):
                if isinstance(e, dict):
                    for k, v in e.items(): flat(v, f"{p}.{k}" if p else k)
                elif isinstance(e, str): m.setdefault(p, e)
            flat(j, ""); maps.append(m)
def text(keys):
    for k in (keys or "").split("|"):
        for m in maps:
            if k and k in m: return m[k]
db = sqlite3.connect(os.path.join(root, ".stangent/graph.sqlite"))
tests = {r[0] for r in db.execute("select id from nodes where is_test=1")}
BUT={"click","submit","onclick","onsubmit","onpress","dblclick","ondoubleclick"}
ev = [json.loads(d) for (d, o) in db.execute("select data, owner_node from facts where kind='ui-event'") if o not in tests and json.loads(d)["event"].lower() in BUT]
named = sum(1 for e in ev if text(e.get("keys")) or e.get("text"))
ff = [json.loads(d) for (d, o) in db.execute("select data, owner_node from facts where kind='form-field'") if o not in tests]
cf = [f for f in ff if f.get("via") != "server" and f.get("inside") != "wrapper"]
sf = [f for f in ff if f.get("via") == "server"]
fn = sum(1 for f in cf if text(f.get("label")) or f.get("labelText"))
sn = sum(1 for f in sf if text(f.get("label")))
pages = db.execute("select count(*) from nodes where kind='page' and is_test=0").fetchone()[0]
mvc = len({o for (o,) in db.execute("select owner_node from facts where kind='on-load' and json_extract(data,'$.framework')='mvc'")} - tests)
print(f"{os.path.basename(root)}: buttons {named}/{len(ev)} ({100*named/max(1,len(ev)):.1f}%), client fields {fn}/{len(cf)} ({100*fn/max(1,len(cf)):.1f}%), server fields {sn}/{len(sf)} ({100*sn/max(1,len(sf)):.1f}%), screens 0/{pages+mvc} (route only)")
