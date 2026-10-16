"""Check the "Deform tests" Part Studio built by build_deform_tests.py (one FeatureScript eval).

Per Deform case: feature status, result solids (count, volume, name), and every mate connector it created
(origin, X/Z axes, whether it belongs to a result solid, distance to the result solids). Mapped connectors are
compared with the analytic wrap of the source connector (line (0,0)-(300,0) -> R300 quarter arc about (0,300)):
    theta = x / 300;  T = (cos, sin, 0);  N = (-sin, cos, 0);  B = (0, 0, 1)
    point (x, y, z) -> 300*(sin, 1 - cos, 0) + y*N + z*B ;  axis (a, b, c) -> a*T + b*N + c*B

usage (repo root): PYTHONPATH=. python devtools/onshape/check_deform_tests.py
"""
import json
import math
import re

from sync.core.client import OnshapeClient

c = OnshapeClient()
D, W = "08e8748f2ef24eea16072b75", "e24fbbae7712ef62dd256c89"
E = [e["id"] for e in c.list_elements(D, W) if e["name"] == "Deform tests"][0]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
R = 300.0

feats = c.get(f"{BASE}/features")
status = {x["name"]: feats["featureStates"][x["featureId"]]["featureStatus"] for x in feats["features"]}
fid = {x["name"].split()[0]: x["featureId"] for x in feats["features"] if x["featureType"] == "deform"}
src = {x["name"].split()[0]: x["featureId"] for x in feats["features"] if x["name"].startswith("MC_") and x["featureType"] == "mateConnector"}

SCRIPT = """function(context is Context, queries)
{
    var out = [];
    for (var pair in __SRC__)
    {
        var cs = evMateConnector(context, { "mateConnector" : qCreatedBy(makeId(pair[1]), EntityType.BODY) });
        var o = cs.origin / millimeter;
        out = append(out, "SRC " ~ pair[0] ~ " " ~ o[0] ~ " " ~ o[1] ~ " " ~ o[2] ~ " " ~ cs.xAxis[0] ~ " " ~ cs.xAxis[1] ~ " " ~ cs.xAxis[2]
            ~ " " ~ cs.zAxis[0] ~ " " ~ cs.zAxis[1] ~ " " ~ cs.zAxis[2]);
    }
    for (var pair in __DEF__)
    {
        var created = qCreatedBy(makeId(pair[1]), EntityType.BODY);
        var solids = qBodyType(created, BodyType.SOLID);
        for (var sd in evaluateQuery(context, solids))
        {
            var nm = "?";
            try silent { nm = getProperty(context, { "entity" : sd, "propertyType" : PropertyType.NAME }); }
            out = append(out, "S " ~ pair[0] ~ " " ~ (evVolume(context, { "entities" : sd }) / (millimeter ^ 3)) ~ " [" ~ nm ~ "]");
        }
        for (var mc in evaluateQuery(context, qBodyType(created, BodyType.MATE_CONNECTOR)))
        {
            var cs = evMateConnector(context, { "mateConnector" : mc });
            var o = cs.origin / millimeter;
            var owned = !isQueryEmpty(context, qIntersection([qOwnerBody(mc), solids]));
            var dist = -1;
            if (!isQueryEmpty(context, solids))
                dist = evDistance(context, { "side0" : cs.origin, "side1" : solids }).distance / millimeter;
            out = append(out, "M " ~ pair[0] ~ " " ~ o[0] ~ " " ~ o[1] ~ " " ~ o[2] ~ " " ~ cs.xAxis[0] ~ " " ~ cs.xAxis[1] ~ " " ~ cs.xAxis[2]
                ~ " " ~ cs.zAxis[0] ~ " " ~ cs.zAxis[1] ~ " " ~ cs.zAxis[2] ~ " " ~ owned ~ " " ~ dist);
        }
    }
    return out;
}""".replace("__SRC__", json.dumps(sorted(src.items()))).replace("__DEF__", json.dumps(sorted(fid.items())))

r = c.post(f"{BASE}/featurescript", json_data={"script": SCRIPT})
for n in r.get("notices") or []:
    print("[%s] %s" % (n.get("level"), n.get("message")))
lines = [v for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))) if v.split(" ")[0] in ("SRC", "S", "M")]


def wrap_point(p):
    x, y, z = p
    th = x / R
    return [R * math.sin(th) - y * math.sin(th), R - R * math.cos(th) + y * math.cos(th), z]


def wrap_axis(p, a):
    th = p[0] / R
    T, N, B = [math.cos(th), math.sin(th), 0], [-math.sin(th), math.cos(th), 0], [0, 0, 1]
    return [a[0] * T[i] + a[1] * N[i] + a[2] * B[i] for i in range(3)]


def cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


srcs, solids, mcs = {}, {}, {}
for ln in lines:
    t = ln.split(" ")
    if t[0] == "SRC":
        srcs[t[1]] = [float(v) for v in t[2:11]]
    elif t[0] == "S":
        solids.setdefault(t[1], []).append((float(t[2]), ln[ln.index("["):]))
    elif t[0] == "M":
        mcs.setdefault(t[1], []).append(([float(v) for v in t[2:11]], t[11] == "true", float(t[12])))

# expected mapped connectors: origin, X axis, Z axis
exp = {}
for k, v in srcs.items():
    o, xa, za = v[0:3], v[3:6], v[6:9]
    exp[k] = (wrap_point(o), wrap_axis(o, xa), wrap_axis(o, za))


def match(got):
    """Which source connector a mapped one came from: nearest expected origin."""
    best = min(exp, key=lambda k: sum((a - b) ** 2 for a, b in zip(exp[k][0], got[0:3])))
    return best, math.sqrt(sum((a - b) ** 2 for a, b in zip(exp[best][0], got[0:3])))


def axes_err(k, got):
    return max(max(abs(a - b) for a, b in zip(exp[k][1], got[3:6])), max(abs(a - b) for a, b in zip(exp[k][2], got[6:9])))


# case -> (solid count, [(source connector, owned by a result solid)])
EXPECT = {"D1": (2, {"MC_A": True, "MC_B": False}), "D2": (0, {"MC_A": False, "MC_B": False}),
          "D3": (1, {}), "D4": (2, {})}
fails = 0
for case, (nsol, want_mc) in EXPECT.items():
    notes, ok = [], status[[n for n in status if n.startswith(case + " ")][0]] == "OK"
    notes.append("status %s" % ("OK" if ok else "NOT OK"))
    got_s = solids.get(case, [])
    ok = ok and len(got_s) == nsol
    notes.append("solids %s" % [(round(v, 1), nm) for v, nm in got_s])
    got_m = mcs.get(case, [])
    ok = ok and len(got_m) == len(want_mc)
    seen = {}
    for g, owned, dist in got_m:
        k, perr = match(g)
        aerr = axes_err(k, g)
        seen[k] = owned
        good = perr < 1e-3 and aerr < 1e-4 and owned == want_mc.get(k)
        if case == "D1" and k == "MC_A":
            good = good and dist < 1e-3
        ok = ok and good
        notes.append("%s origin err %.5f mm, axes err %.2e, owned %s, dist to result %.4f%s" % (k, perr, aerr, owned, dist, "" if good else "  <-- FAIL"))
    ok = ok and set(seen) == set(want_mc)
    fails += not ok
    print("%-3s %s" % (case, "PASS" if ok else "FAIL"))
    for n in notes:
        print("      " + n)
print("\n%d/%d pass" % (len(EXPECT) - fails, len(EXPECT)))
