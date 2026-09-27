"""Check the Driven edge offset "Profile source: Regions" cases in driven_offset's "DEO regions tests" Part Studio
(built by build_deo_regions_tests.py; real instances named by case and expected result).

For each case Dn: the Regions-sourced offset "Dn ..." against its wire-sourced reference "Dn-W ..." (same regions
through Create offset profile's wire): 21 points per edge of each measured against the other's edges, both ways,
max distance <= TOL_MM; same body count and edge count. D4 also checks its absolute placement: the profile runs
from the sketch point (x 100) to the mate connector minus 50 mm (x 300), width 2 there and 5 here.

usage (repo root, Git Bash):
    PYTHONPATH=. python devtools/onshape/check_deo_regions_tests.py
"""
import json
import re
import sys

from sync.core.client import OnshapeClient

D, W = "f61d2c000ab2d1240776342e", "5b11f323ab31b04cba8b36ef"
c = OnshapeClient()
E = [e["id"] for e in c.list_elements(D, W) if e["name"] == "DEO regions tests"][0]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"

TOL_MM = 1e-3
CASES = ["D1", "D2", "D3", "D4", "D5"]

SCRIPT = r'''
function(context is Context, queries)
{
    const a = qOwnedByBody(qCreatedBy(makeId("%(regions)s"), EntityType.BODY), EntityType.EDGE);
    const b = qOwnedByBody(qCreatedBy(makeId("%(wire)s"), EntityType.BODY), EntityType.EDGE);
    var params = [];
    for (var i = 0; i <= 20; i += 1)
    {
        params = append(params, i / 20);
    }
    const worstFrom = function(from is Query, to is Query) returns ValueWithUnits
        {
            var worst = 0 * meter;
            for (var e in evaluateQuery(context, from))
            {
                for (var tl in evEdgeTangentLines(context, { "edge" : e, "parameters" : params }))
                {
                    const d = evDistance(context, { "side0" : tl.origin, "side1" : to }).distance;
                    if (d > worst)
                    {
                        worst = d;
                    }
                }
            }
            return worst;
        };
    const worst = max(worstFrom(a, b), worstFrom(b, a));
    const bodiesA = size(evaluateQuery(context, qCreatedBy(makeId("%(regions)s"), EntityType.BODY)));
    const bodiesB = size(evaluateQuery(context, qCreatedBy(makeId("%(wire)s"), EntityType.BODY)));
    const edgesA = size(evaluateQuery(context, a));
    const edgesB = size(evaluateQuery(context, b));
    var extra = "";
    if (%(placement)s)
    {
        const bb = evBox3d(context, { "topology" : a, "tight" : true });
        const d0 = evDistance(context, { "side0" : vector(100, 2, 0) * millimeter, "side1" : a }).distance;
        const d1 = evDistance(context, { "side0" : vector(300, 5, 0) * millimeter, "side1" : a }).distance;
        extra = toString(roundToPrecision(bb.minCorner[0] / millimeter, 5)) ~ "," ~ toString(roundToPrecision(bb.maxCorner[0] / millimeter, 5))
            ~ "," ~ toString(roundToPrecision(d0 / millimeter, 6)) ~ "," ~ toString(roundToPrecision(d1 / millimeter, 6));
    }
    return [toString(roundToPrecision(worst / millimeter, 7)), toString(bodiesA), toString(bodiesB), toString(edgesA), toString(edgesB), extra];
}
'''


def strings(result):
    return [s for s in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(result)) if s not in ("BTFSValueString", "BTFSValueArray")]


feats = c.get(f"{BASE}/features")["features"]
by_prefix = {}
for f in feats:
    if f["featureType"] == "drivenEdgeOffset":
        by_prefix[f["name"].split(" ")[0]] = f

failed = 0
for case in CASES:
    a = by_prefix.get(case)
    b = by_prefix.get(case + "-W")
    if a is None or b is None:
        print("%s FAIL feature missing" % case)
        failed += 1
        continue
    placement = "true" if case == "D4" else "false"
    r = c.post(f"{BASE}/featurescript", json_data={"script": SCRIPT % {"regions": a["featureId"], "wire": b["featureId"], "placement": placement}})
    errors = [n.get("message") for n in (r.get("notices") or []) if n.get("level") == "ERROR"]
    got = strings(r.get("result"))
    if errors or len(got) < 5:
        print("%s FAIL check did not run: %s" % (case, "; ".join(errors or ["no result"])))
        failed += 1
        continue
    worst, ba, bb, ea, eb = float(got[0]), int(got[1]), int(got[2]), int(got[3]), int(got[4])
    ok = worst <= TOL_MM and ba == bb and ea == eb and ba > 0
    detail = "max distance %.7f mm (tol %g), bodies %d vs %d, edges %d vs %d" % (worst, TOL_MM, ba, bb, ea, eb)
    if case == "D4":
        x0, x1, d0, d1 = [float(v) for v in got[5].split(",")]
        placed = abs(x0 - 100) < 1e-3 and abs(x1 - 300) < 1e-3 and d0 < 1e-3 and d1 < 1e-3
        ok = ok and placed
        detail += ", x %.5f .. %.5f (want 100 .. 300), off (100, 2) %.6f, off (300, 5) %.6f mm" % (x0, x1, d0, d1)
    failed += 0 if ok else 1
    print("%s %s  %s  -- %s" % (case, "PASS" if ok else "FAIL", detail, a["name"]))

print("%d failed" % failed if failed else "all passed")
sys.exit(1 if failed else 0)
