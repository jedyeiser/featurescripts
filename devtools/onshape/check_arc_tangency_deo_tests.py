"""Check the "Arc tangency DEO tests" studio (build_arc_tangency_deo_tests.py) through the eval API: per case the
output's edge kinds (arcs with radii), the worst joint angle, and for D3 the largest distance from D3 to D2.

usage (repo root): PYTHONPATH=. MSYS_NO_PATHCONV=1 python devtools/onshape/check_arc_tangency_deo_tests.py
"""
import json
import re
import sys

from devtools.onshape.fseval import run
from sync.core.client import OnshapeClient

c = OnshapeClient()
D, W = "f61d2c000ab2d1240776342e", "5b11f323ab31b04cba8b36ef"
E = [e["id"] for e in c.list_elements(D, W) if e["name"] == "Arc tangency DEO tests"][0]
FEATURES = c.get(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/features")["features"]
ids = {f["name"].split(" ")[0]: f["featureId"] for f in FEATURES if re.match(r"D\d ", f["name"])}

pairs = ", ".join('["%s", "%s"]' % (k, v) for k, v in sorted(ids.items()))
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    for (var pair in [%s])
    {
        const bodies = qBodyType(qCreatedBy(makeId(pair[1]), EntityType.BODY), BodyType.WIRE);
        const edges = evaluateQuery(context, qOwnedByBody(bodies, EntityType.EDGE));
        var text = pair[0] ~ " edges=" ~ size(edges) ~ " kinds=";
        var ends = [];
        for (var e in edges)
        {
            const d = evCurveDefinition(context, { "edge" : e });
            text = text ~ ((d is Circle) ? ("arcR" ~ roundToPrecision(d.radius / millimeter, 2)) : ((d is Line) ? "line" : "spline")) ~ ",";
            ends = append(ends, evEdgeTangentLines(context, { "edge" : e, "parameters" : [0, 1] }));
        }
        var worst = 0;
        for (var i = 0; i < size(ends); i += 1)
        {
            for (var j = i + 1; j < size(ends); j += 1)
            {
                for (var a in ends[i])
                {
                    for (var b in ends[j])
                    {
                        if (norm(a.origin - b.origin) < 1e-6 * meter)
                        {
                            worst = max(worst, atan2(norm(cross(a.direction, b.direction)), abs(dot(a.direction, b.direction))) / degree);
                        }
                    }
                }
            }
        }
        text = text ~ " joint=" ~ toString(roundToPrecision(worst, 7));
        if (pair[0] == "D3")
        {
            var far = 0 * meter;
            const other = qBodyType(qCreatedBy(makeId("%s"), EntityType.BODY), BodyType.WIRE);
            for (var e in edges)
            {
                var params = [];
                for (var k = 0; k <= 20; k += 1)
                {
                    params = append(params, k / 20);
                }
                for (var tl in evEdgeTangentLines(context, { "edge" : e, "parameters" : params }))
                {
                    far = max(far, evDistance(context, { "side0" : tl.origin, "side1" : other }).distance);
                }
            }
            text = text ~ " toD2=" ~ toString(roundToPrecision(far / millimeter, 5));
        }
        out = append(out, text);
    }
    return out;
}""" % (pairs, ids.get("D2", ""))

result = run(SCRIPT, D, W, E)
for n in result.get("notices") or []:
    print("[%s] %s" % (n.get("level"), n.get("message")))
lines = [s for s in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"',json.dumps(result.get("result")))
         if s not in ("BTFSValueString", "BTFSValueArray")]

failed = 0
got = {}
for line in lines:
    label = line.split(" ")[0]
    kinds = [k for k in re.search(r"kinds=(\S*)", line).group(1).split(",") if k]
    joint = float(re.search(r"joint=(\S+)", line).group(1))
    got[label] = kinds
    arcs = [k for k in kinds if k.startswith("arc")]
    if label in ("D1", "D4"):
        ok = len(kinds) == 3 and len(arcs) == 2 and "line" in kinds and joint <= 1e-6
    elif label == "D2":
        ok = len(kinds) == 3 and len(arcs) == 0 and joint <= 1e-6
    elif label == "D3":
        ok = len(arcs) >= 4 and kinds.count("spline") == 1 and joint <= 1e-6 and float(re.search(r"toD2=(\S+)", line).group(1)) <= 0.02
    else:
        ok = False
    failed += not ok
    print("%-4s %s" % ("PASS" if ok else "FAIL", line))
if "D1" in got and "D4" in got and sorted(got["D1"]) != sorted(got["D4"]):
    failed += 1
    print("FAIL D4 differs from D1")
print("%d problem(s)" % failed)
sys.exit(1 if failed else 0)
