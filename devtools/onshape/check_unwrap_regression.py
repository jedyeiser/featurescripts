"""Unwrap regression: feature status + published lengthWrapped / lengthFlat / volumeRatio of every Unwrap feature in
the two test studios ("Unwrap_Testing Copy 2" = all production parts, "Unwrap_Testing Copy 1" = U1-U3).

usage (repo root, Git Bash):
    FS_SYNC_TIMEOUT=300 PYTHONPATH=. python devtools/onshape/check_unwrap_regression.py [--save]
Compares with devtools/onshape/fingerprints/unwrap_baseline.json; --save writes the current values as the baseline.
"""
import json, re, sys, time
from sync.core.client import OnshapeClient
SP = "devtools/onshape/fingerprints"
SAVE = "--save" in sys.argv
c = OnshapeClient()
D, W = "f61d2c000ab2d1240776342e", "5b11f323ab31b04cba8b36ef"
def features(E):
    for a in range(4):
        try:
            return c.get(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/features")
        except Exception as e:
            time.sleep(5)
    raise SystemExit("features failed")
def evaluate(E, fids):
    arr = ", ".join('"%s"' % f for f in fids)
    script = '''function(context is Context, queries)
{
    var out = [];
    for (var fid in [%s])
    {
        const o = getVariable(context, toString(makeId(fid)), {});
        const v = (o.variable == undefined) ? {} : o.variable;
        var text = fid;
        for (var key in ["lengthWrapped", "lengthFlat", "volumeRatio"])
        {
            var vals = "";
            if (v[key] != undefined)
            {
                for (var x in v[key].value)
                {
                    vals = vals ~ " " ~ toString(x is ValueWithUnits ? roundToPrecision(x / millimeter, 4) : roundToPrecision(x, 6));
                }
            }
            text = text ~ " | " ~ key ~ ":" ~ vals;
        }
        out = append(out, text);
    }
    return out;
}''' % arr
    for a in range(4):
        try:
            r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": script})
            for n in r.get("notices") or []:
                if n.get("level") == "ERROR": print("   eval:", n.get("message"))
            found = []
            def walk(o):
                if isinstance(o, dict):
                    if isinstance(o.get("value"), str) and "String" in o.get("btType", ""):
                        found.append(o["value"])
                    for v in o.values():
                        walk(v)
                elif isinstance(o, list):
                    for v in o:
                        walk(v)
            walk(r.get("result"))
            return [x for x in found if " | " in x]
        except Exception as e:
            print("   eval exception:", str(e)[:300])
            time.sleep(5)
    return []
def report(E, label):
    f = features(E)
    un = [x for x in f["features"] if x["featureType"] == "unwrap"]
    states = f.get("featureStates", {})
    rows = evaluate(E, [x["featureId"] for x in un])
    byid = {r.split(" | ")[0]: r for r in rows}
    print(label)
    out = []
    for x in un:
        st = states.get(x["featureId"], {}).get("featureStatus")
        r = byid.get(x["featureId"], "")
        vals = " | ".join(r.split(" | ")[1:])
        print("  %-7s %-55s %s" % (st, x["name"][:55], vals))
        out.append((x["name"], st, vals))
    return out
now = report("681a5825e353d376c88224eb", "Copy 2") + report("80c1e329f99a05e224058526", "Copy 1")
path = SP + "/unwrap_baseline.json"
if SAVE:
    json.dump(now, open(path, "w"), indent=1)
    print("saved", path)
else:
    try:
        base = json.load(open(path))
        changed = [(a, b) for a, b in zip(base, now) if list(a) != list(b)]
        print("vs baseline:", "IDENTICAL" if not changed and len(base) == len(now) else "%d changed" % len(changed))
        for a, b in changed:
            print("  CHANGED", a[0][:50], "|", a[1], a[2], "->", b[1], b[2])
    except FileNotFoundError:
        print("no baseline yet: run with --save")
