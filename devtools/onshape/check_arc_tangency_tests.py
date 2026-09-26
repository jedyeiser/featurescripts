"""Check the "Arc tangency tests" studio (build_arc_tangency_tests.py): per case, the output wires' edge types, radii
and the worst joint angle, measured through the eval API, against the expectation in the feature's name.

usage (repo root): PYTHONPATH=. python devtools/onshape/check_arc_tangency_tests.py
"""
import json
import re
import sys

from devtools.onshape.fseval import run
from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("curve_tools/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
E = [e["id"] for e in c.list_elements(D, W) if e["name"] == "Arc tangency tests"][0]
FEATURES = c.get(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/features")["features"]

# label -> (bodies, edges, kinds in order or None, max joint angle deg or None)
EXPECT = {
    "R1": (1, 1, ["arcR100"], None),
    "R2": (1, 3, ["line", "arcR200", "line"], 0.05),   # a recognized arc keeps the spline's ends only to "Max end tangent change"
    "R3": (1, 1, ["spline"], None),
    "R4": (0, 0, [], None),
    "R5": (1, 1, ["spline"], None),
    "P12": (None, 2, None, None),
    "P13": (None, 1, None, None),
    "M1": (1, 1, ["spline"], None),
    "M2": (1, 1, ["arcR500"], None),
}

ids = {}
for f in FEATURES:
    label = f["name"].split(" ")[0]
    if label in EXPECT:
        ids[label] = f["featureId"]

pairs = ", ".join('["%s", "%s"]' % (k, v) for k, v in ids.items())
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    for (var pair in [%s])
    {
        const bodies = evaluateQuery(context, qBodyType(qCreatedBy(makeId(pair[1]), EntityType.BODY), BodyType.WIRE));
        const edges = evaluateQuery(context, qOwnedByBody(qUnion(bodies), EntityType.EDGE));
        var text = pair[0] ~ " bodies=" ~ size(bodies) ~ " edges=" ~ size(edges) ~ " kinds=";
        var ends = [];
        for (var e in edges)
        {
            const d = evCurveDefinition(context, { "edge" : e });
            if (d is Circle)
            {
                text = text ~ "arcR" ~ roundToPrecision(d.radius / millimeter, 3) ~ ",";
            }
            else if (d is Line)
            {
                text = text ~ "line,";
            }
            else
            {
                text = text ~ "spline,";
            }
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
                            const angle = atan2(norm(cross(a.direction, b.direction)), abs(dot(a.direction, b.direction))) / degree;
                            worst = max(worst, angle);
                        }
                    }
                }
            }
        }
        out = append(out, text ~ " joint=" ~ toString(roundToPrecision(worst, 7)));
    }
    return out;
}""" % pairs

result = run(SCRIPT, D, W, E)
lines = re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(result.get("result")))
lines = [s for s in lines if s not in ("BTFSValueString", "BTFSValueArray")]
for n in result.get("notices") or []:
    print("[%s] %s" % (n.get("level"), n.get("message")))

failed = 0
for line in lines:
    m = re.match(r"(\S+) bodies=(\d+) edges=(\d+) kinds=(\S*) joint=(\S+)", line)
    label, bodies, nedges, kinds, joint = m.group(1), int(m.group(2)), int(m.group(3)), m.group(4), float(m.group(5))
    kinds = [k for k in kinds.split(",") if k]
    kinds = [re.sub(r"arcR(\d+)\.\d+", lambda x: "arcR" + x.group(1), k) for k in kinds]
    eb, ee, ek, ej = EXPECT[label]
    ok = (eb is None or bodies == eb) and nedges == ee and (ek is None or sorted(kinds) == sorted(ek)) and (ej is None or joint <= ej)
    failed += not ok
    print("%-4s %-4s %s" % ("PASS" if ok else "FAIL", label, line))
print("%d/%d pass" % (len(lines) - failed, len(lines)))
sys.exit(1 if failed else 0)
