"""Check the "Divide tests" Part Studio built by build_evenly_divide_tests.py (one FeatureScript eval).

Per case: feature status; wire bodies created (count, edges, length, x extent, min/max |y|); and the worst distance of
sampled wire points from the surface (the wires must lie on it). Expected:
  E1 full / on plane   5 wires, one of them on y = 0 (start wire), others off it
  E2 full / half step  5 wires, none on y = 0
  E3 half / on plane   5 wires, first and last on y = 0
  E4 half / half step  5 wires, none on y = 0
All wires: within 1e-3 mm of the surface and spanning (nearly) the length of the surface.

usage (repo root): PYTHONPATH=. python devtools/onshape/check_evenly_divide_tests.py
"""
import json
import re

from sync.core.client import OnshapeClient

c = OnshapeClient()
D, W = "69ab3696c014c05409202108", "c176011bc60a3f918e673d98"
E = [e["id"] for e in c.list_elements(D, W) if e["name"] == "Divide tests"][0]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"

feats = c.get(f"{BASE}/features")
status = {x["name"]: feats["featureStates"][x["featureId"]]["featureStatus"] for x in feats["features"]}
cases = {x["name"].split()[0]: x["featureId"] for x in feats["features"] if x["featureType"] == "evenlyDivideSurface"}

SCRIPT = """function(context is Context, queries)
{
    var out = [];
    var sheets = qBodyType(qEverything(EntityType.BODY), BodyType.SHEET);
    for (var pair in __CASES__)
    {
        var made = qBodyType(qCreatedBy(makeId(pair[1]), EntityType.BODY), BodyType.WIRE);
        var surface = pair[0] == "E1" || pair[0] == "E2" ? __FULL__ : __HALF__;
        var surfaceFaces = qOwnedByBody(surface, EntityType.FACE);
        out = append(out, "C " ~ pair[0] ~ " wires " ~ size(evaluateQuery(context, made)));
        for (var w in evaluateQuery(context, made))
        {
            var edges = evaluateQuery(context, qOwnedByBody(w, EntityType.EDGE));
            var bb = evBox3d(context, { "topology" : w });
            var minAbsY = inf;
            var maxAbsY = 0;
            var worst = 0;
            for (var e in edges)
            {
                for (var l in evEdgeTangentLines(context, { "edge" : e, "parameters" : range(0, 1, 25) }))
                {
                    var y = abs(l.origin[1] / millimeter);
                    minAbsY = min(minAbsY, y);
                    maxAbsY = max(maxAbsY, y);
                    var dd = evDistance(context, { "side0" : l.origin, "side1" : surfaceFaces }).distance / millimeter;
                    worst = max(worst, dd);
                }
            }
            out = append(out, "W " ~ pair[0] ~ " edges " ~ size(edges) ~ " len " ~ (evLength(context, { "entities" : qOwnedByBody(w, EntityType.EDGE) }) / millimeter)
                ~ " x " ~ (bb.minCorner[0] / millimeter) ~ " " ~ (bb.maxCorner[0] / millimeter) ~ " absY " ~ minAbsY ~ " " ~ maxAbsY ~ " off " ~ worst);
        }
    }
    return out;
}"""
script = SCRIPT.replace("__CASES__", json.dumps(sorted(cases.items())))
script = script.replace("__FULL__", "pick(47)").replace("__HALF__", "pick(27)")
script = script.replace("    var out = [];", """    var out = [];
    var pick = function(n)
    {
        for (var b in evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.SHEET)))
            if (size(evaluateQuery(context, qOwnedByBody(b, EntityType.FACE))) == n)
                return b;
        return qNothing();
    };""", 1)
r = c.post(f"{BASE}/featurescript", json_data={"script": script})
for n in r.get("notices") or []:
    print("[%s] %s" % (n.get("level"), n.get("message")))
lines = [v for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))) if v[:2] in ("C ", "W ")]

by = {}
for ln in lines:
    t = ln.split(" ")
    by.setdefault(t[1], []).append(t)

fails = 0
for case in sorted(cases):
    name = [n for n in status if n.startswith(case + " ")][0]
    wires = [t for t in by.get(case, []) if t[0] == "W"]
    notes, ok = [], status[name] in ("OK", "INFO")
    notes.append("status %s, %d wire bodies" % (status[name], len(wires)))
    on_plane = 0
    for t in wires:
        edges, length, x0, x1, ymin, ymax, off = int(t[3]), float(t[5]), float(t[7]), float(t[8]), float(t[10]), float(t[11]), float(t[13])
        if ymax < 1e-3:
            on_plane += 1
        good = off < 1e-3
        ok = ok and good
        notes.append("  wire: %2d edges, len %8.2f mm, x %8.1f .. %8.1f, |y| %7.3f .. %7.3f, off-surface %.5f mm%s" % (
            edges, length, x0, x1, ymin, ymax, off, "" if good else "   <-- FAIL"))
    want_plane = {"E1": 1, "E2": 0, "E3": 2, "E4": 0}[case]
    ok = ok and on_plane == want_plane and len(wires) >= 5
    notes.append("  wires on y=0: %d (expected %d)" % (on_plane, want_plane))
    fails += not ok
    print("%-3s %s" % (case, "PASS" if ok else "FAIL"))
    for n in notes:
        print("    " + n)
print("\n%d/%d pass" % (len(cases) - fails, len(cases)))
