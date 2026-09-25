"""Check the Evaluate offset round-trip cases in driven_offset's "Evaluate offset tests" Part Studio.

The cases are real feature instances (E1..E4, built by build_evaluate_offset_tests.py). For each, the MEASURED
profile (the Evaluate offset output) is compared with the ORIGINAL profile (the Create offset profile the target
was offset by) at stations across the measured range: width and height must agree within TOL_MM, the piece
count must be as named, and the measured profile must start and end where the target does.

usage (repo root, Git Bash):
    PYTHONPATH=. python devtools/onshape/check_evaluate_offset.py
"""
import json
import re
import sys

from sync.core.client import OnshapeClient

D, W = "f61d2c000ab2d1240776342e", "5b11f323ab31b04cba8b36ef"
c = OnshapeClient()
E = [e["id"] for e in c.list_elements(D, W) if e["name"] == "Evaluate offset tests"][0]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"

TOL_MM = 0.02
SAMPLES = 41

# case prefix -> (original profile prefix, expected pieces, expected start X, expected end X (mm))
CASES = {
    "E1": ("P-smooth", 1, 0.0, 450.0),
    "E2": ("P-smooth", 1, 0.0, 450.0),
    "E3": ("P-jump", 2, 0.0, 400.0),
    "E4": ("P-smooth", 1, 0.0, 400 * 0.9396926207859084),
    "E5": ("P-smooth", 1, 0.0, 400.0),
    "E6": ("P-smooth", 1, 0.0, 400.0),
}

SCRIPT = r'''
function(context is Context, queries)
{
    const measured = qCreatedBy(makeId("%(measured)s"), EntityType.BODY);
    const original = qCreatedBy(makeId("%(original)s"), EntityType.BODY);
    const valueAt = function(wires is Query, x is ValueWithUnits) returns array
        {
            const hit = evDistance(context, { "side0" : plane(vector(x, 0 * meter, 0 * meter), vector(1, 0, 0)), "side1" : qOwnedByBody(wires, EntityType.EDGE) });
            if (hit.distance > 1e-7 * meter)
            {
                return [undefined, undefined];
            }
            return [hit.sides[1].point[1], hit.sides[1].point[2]];
        };
    const bb = evBox3d(context, { "topology" : measured, "tight" : true });
    const x0 = bb.minCorner[0];
    const x1 = bb.maxCorner[0];
    var worst = 0 * meter;
    var worstAt = x0;
    var missing = 0;
    for (var i = 0; i < %(samples)d; i += 1)
    {
        // Stay 0.05 mm inside each end, and off an exact jump station.
        const x = x0 + 0.05 * millimeter + (x1 - x0 - 0.1 * millimeter) * i / (%(samples)d - 1) + 0.013 * millimeter;
        const m = valueAt(measured, x);
        const o = valueAt(original, x);
        if (m[0] == undefined || o[0] == undefined)
        {
            missing += 1;
            continue;
        }
        const d = max(abs(m[0] - o[0]), abs(m[1] - o[1]));
        if (d > worst)
        {
            worst = d;
            worstAt = x;
        }
    }
    return [toString(size(evaluateQuery(context, measured))), toString(roundToPrecision(x0 / millimeter, 4)),
        toString(roundToPrecision(x1 / millimeter, 4)), toString(roundToPrecision(worst / millimeter, 6)),
        toString(roundToPrecision(worstAt / millimeter, 3)), toString(missing)];
}
'''


def strings(result):
    return [s for s in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(result)) if s not in ("BTFSValueString", "BTFSValueArray")]


feats = c.get(f"{BASE}/features")["features"]
state = c.get(f"{BASE}/features")
by_prefix = {}
for f in feats:
    by_prefix[f["name"].split(" ")[0].rstrip(":")] = f

failed = 0
for case, (orig, pieces, xs, xe) in CASES.items():
    m = by_prefix.get(case)
    o = by_prefix.get(orig)
    if m is None or o is None:
        print("%s FAIL feature missing" % case)
        failed += 1
        continue
    r = c.post(f"{BASE}/featurescript", json_data={"script": SCRIPT % {"measured": m["featureId"], "original": o["featureId"], "samples": SAMPLES}})
    errors = [n.get("message") for n in (r.get("notices") or []) if n.get("level") == "ERROR"]
    got = strings(r.get("result"))
    if errors or len(got) < 6:
        print("%s FAIL check did not run: %s" % (case, "; ".join(errors or ["no result"])))
        failed += 1
        continue
    n, x0, x1, worst, worst_at, missing = int(got[0]), float(got[1]), float(got[2]), float(got[3]), float(got[4]), int(got[5])
    ok = n == pieces and worst <= TOL_MM and abs(x0 - xs) < 0.01 and abs(x1 - xe) < 0.01 and missing <= 2
    failed += 0 if ok else 1
    print("%s %s  %d piece(s) (want %d), x %.4f .. %.4f (want %.4f .. %.4f), max |dw|,|dh| %.6f mm at x %.3f, %d sample(s) off-profile  -- %s"
          % (case, "PASS" if ok else "FAIL", n, pieces, x0, x1, xs, xe, worst, worst_at, missing, m["name"]))

# Round trips: points along each R case's edges, distance to the original target's edges.
ROUND = {"R2": "T-E2", "R3": "T-E3", "R4": "T-E4", "R5": "T-E5", "R6": "T-E6"}
RSCRIPT = r'''
function(context is Context, queries)
{
    const again = qOwnedByBody(qCreatedBy(makeId("%(again)s"), EntityType.BODY), EntityType.EDGE);
    const target = qOwnedByBody(qCreatedBy(makeId("%(target)s"), EntityType.BODY), EntityType.EDGE);
    var worst = 0 * meter;
    var worstAt = vector(0, 0, 0) * meter;
    var params = [];
    for (var i = 0; i <= 20; i += 1)
    {
        params = append(params, i / 20);
    }
    for (var e in evaluateQuery(context, again))
    {
        for (var line in evEdgeTangentLines(context, { "edge" : e, "parameters" : params }))
        {
            const d = evDistance(context, { "side0" : line.origin, "side1" : target }).distance;
            if (d > worst)
            {
                worst = d;
                worstAt = line.origin;
            }
        }
    }
    const lengthAgain = evLength(context, { "entities" : again });
    const lengthTarget = evLength(context, { "entities" : target });
    return [toString(roundToPrecision(worst / millimeter, 6)), toString(roundToPrecision(worstAt[0] / millimeter, 2)) ~ "," ~ toString(roundToPrecision(worstAt[1] / millimeter, 2)),
        toString(roundToPrecision(lengthAgain / millimeter, 5)), toString(roundToPrecision(lengthTarget / millimeter, 5))];
}
'''
for case, target in ROUND.items():
    a = by_prefix.get(case)
    t = by_prefix.get(target)
    if a is None or t is None:
        print("%s FAIL feature missing" % case)
        failed += 1
        continue
    r = c.post(f"{BASE}/featurescript", json_data={"script": RSCRIPT % {"again": a["featureId"], "target": t["featureId"]}})
    errors = [n.get("message") for n in (r.get("notices") or []) if n.get("level") == "ERROR"]
    got = strings(r.get("result"))
    if errors or len(got) < 4:
        print("%s FAIL check did not run: %s" % (case, "; ".join(errors or ["no result"])))
        failed += 1
        continue
    worst, at, la, lt = float(got[0]), got[1], float(got[2]), float(got[3])
    ok = worst <= TOL_MM and abs(la - lt) <= TOL_MM
    failed += 0 if ok else 1
    print("%s %s  max distance to %s %.6f mm at %s, length %.5f vs %.5f mm  -- %s" % (case, "PASS" if ok else "FAIL", target, worst, at, la, lt, a["name"]))

sys.exit(1 if failed else 0)
