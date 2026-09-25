"""Check the "Trim curve + tests" Part Studio built by build_trim_curve_plus_tests.py.

Reads every non-sketch wire body (edge count, length, case by x offset), the feature statuses, and the
Variable_tools keys embedded by T1 and T2 (cut_k vertices / piece_k), and compares with the expected results.

usage (repo root): PYTHONPATH=. MSYS_NO_PATHCONV=1 python devtools/onshape/check_trim_curve_plus_tests.py
"""
import json
import re

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("smallTools/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
E = [e["id"] for e in c.list_elements(D, W) if e["name"] == "Trim curve + tests"][0]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"

# case -> sorted wire lengths (mm), or "ERROR"
EXPECT = {
    "T1": [100, 100, 357.080], "T2": [557.080], "T3": [457.080], "T4": [100, 150, 307.080],
    "T5": [50, 507.080], "T6": [50, 507.080], "T7": [100, 457.080], "T8": [557.080],
    "T9": [20, 130, 100, 307.080],
}
EXPECT_EDGES = {"T2": 5}

SCRIPT = """function(context is Context, queries)
{
    var out = [];
    for (var w in evaluateQuery(context, qSketchFilter(qBodyType(qEverything(EntityType.BODY), BodyType.WIRE), SketchObject.NO)))
    {
        var bb = evBox3d(context, { "topology" : w });
        out = append(out, "W " ~ (bb.minCorner[0] / millimeter) ~ " " ~ size(evaluateQuery(context, qOwnedByBody(w, EntityType.EDGE)))
            ~ " " ~ (evLength(context, { "entities" : qOwnedByBody(w, EntityType.EDGE) }) / millimeter));
    }
    for (var fid in FIDS)
    {
        var m = getVariable(context, toString(makeId(fid)));
        for (var k in keys(m.query))
        {
            var ents = evaluateQuery(context, m.query[k].value);
            var desc = "";
            for (var e in ents)
            {
                if (size(evaluateQuery(context, qEntityFilter(e, EntityType.VERTEX))) > 0)
                {
                    var p = evVertexPoint(context, { "vertex" : e }) / millimeter;
                    desc = desc ~ " (" ~ roundToPrecision(p[0], 3) ~ "," ~ roundToPrecision(p[1], 3) ~ ")";
                }
            }
            out = append(out, "K " ~ fid ~ " " ~ k ~ " " ~ size(ents) ~ desc);
        }
        out = append(out, "K " ~ fid ~ " cutCount " ~ m.variable.cutCount.value);
    }
    return out;
}"""

feats = c.get(f"{BASE}/features")
status = {x["featureId"]: feats["featureStates"][x["featureId"]]["featureStatus"] for x in feats["features"]}
trims = {x["name"][:2]: x["featureId"] for x in feats["features"] if x["featureType"] == "betterCurveTrim"}
fids = [trims["T1"], trims["T2"]]
script = SCRIPT.replace("FIDS", json.dumps(fids))
r = c.post(f"{BASE}/featurescript", json_data={"script": script})
for n in r.get("notices") or []:
    print("[%s] %s" % (n.get("level"), n.get("message")))
lines = [v for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))) if v[:2] in ("W ", "K ")]

got, edges = {}, {}
for ln in lines:
    if ln.startswith("W "):
        _, x, ne, length = ln.split()
        case = "T%d" % (int(float(x) + 50) // 1000 + 1)
        got.setdefault(case, []).append(float(length))
        edges[case] = edges.get(case, 0) + int(ne)

fails = 0
for case, want in EXPECT.items():
    have = sorted(got.get(case, []))
    ok = len(have) == len(want) and all(abs(a - b) < 0.01 for a, b in zip(have, sorted(want)))
    ok = ok and (case not in EXPECT_EDGES or edges[case] == EXPECT_EDGES[case])
    want_status = "ERROR" if case == "T8" else "OK"
    ok = ok and status[trims[case]] == want_status
    fails += not ok
    print("%-4s %s  status %-5s wires %s edges %d" % (case, "PASS" if ok else "FAIL", status[trims[case]],
                                                      [round(x, 3) for x in have], edges.get(case, 0)))
print()
for ln in lines:
    if ln.startswith("K "):
        print(ln)
print("\n%d/%d pass" % (len(EXPECT) - fails, len(EXPECT)))
