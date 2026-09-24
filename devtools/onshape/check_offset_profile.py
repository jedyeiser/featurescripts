"""Check the Create offset profile test cases in BeamBuilder Testbed's "Offset profile tests" Part Studio.

The cases are real feature instances in the tree (T1..T5, named by case and expected result), so anyone can open
them. This script measures their output geometry and published values through the eval API and prints PASS / FAIL.
Error cases are checked with temporary instances that are inserted, read and deleted again.

usage (repo root, Git Bash):
    PYTHONPATH=. python devtools/onshape/check_offset_profile.py
"""
import json
import re
import sys

from sync.core.client import OnshapeClient

D, W = "f61d2c000ab2d1240776342e", "5b11f323ab31b04cba8b36ef"
TAB = "3fccdcb24013c744bd0fd8a2"

c = OnshapeClient()
E = [e["id"] for e in c.list_elements(D, W) if e["name"] == "Offset profile tests"][0]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"

# FeatureScript helpers shared by every check (std only -- the eval API cannot import our tabs).
HELPERS = r'''
    const valueAt = function(wires is Query, x is number) returns array
        {
            const hit = evDistance(context, { "side0" : plane(vector(x, 0, 0) * millimeter, vector(1, 0, 0)), "side1" : qOwnedByBody(wires, EntityType.EDGE) });
            if (hit.distance > 1e-7 * meter)
            {
                return [undefined, undefined];
            }
            return [hit.sides[1].point[1], hit.sides[1].point[2]];
        };
    const near = function(v, target is number, tol is number) returns boolean
        {
            return v is ValueWithUnits && abs(v - target * millimeter) <= tol * millimeter;
        };
    const jointAt = function(wires is Query, x is number) returns map
        {
            var sides = [];
            for (var e in evaluateQuery(context, qOwnedByBody(wires, EntityType.EDGE)))
            {
                const ends = evEdgeTangentLines(context, { "edge" : e, "parameters" : [0, 1] });
                for (var k in [0, 1])
                {
                    if (abs(ends[k].origin[0] - x * millimeter) < 1e-9 * meter)
                    {
                        sides = append(sides, { "tangent" : ends[k].direction, "curvature" : evEdgeCurvature(context, { "edge" : e, "parameter" : k }).curvature });
                    }
                }
            }
            if (size(sides) != 2)
            {
                return { "ok" : false };
            }
            const angle = min(angleBetween(sides[0].tangent, sides[1].tangent), angleBetween(sides[0].tangent, -sides[1].tangent));
            return { "ok" : true, "angle" : angle, "k0" : sides[0].curvature, "k1" : sides[1].curvature };
        };
    const fmt = function(v) returns string
        {
            return v is ValueWithUnits ? toString(roundToPrecision(v / millimeter, 5)) : toString(v);
        };
'''

CHECKS = {
    "T1": r'''
        const b = out.variable.breakStations.value;
        const w = [valueAt(wires, -50)[0], valueAt(wires, 500)[0], valueAt(wires, 1050)[0]];
        const bv = size(evaluateQuery(context, out.query.breakVertices.value));
        const ok = pieces == 3 && size(b) == 2 && near(b[0], 0, 1e-6) && near(b[1], 1000, 1e-6)
            && near(w[0], 0, 1e-6) && near(w[1], 3, 1e-6) && near(w[2], 0, 1e-6) && bv == 4;
        return [ok, pieces ~ " pieces, breaks " ~ join(mapArray(b, fmt), " / ") ~ ", width at -50 / 500 / 1050 = "
            ~ fmt(w[0]) ~ " / " ~ fmt(w[1]) ~ " / " ~ fmt(w[2]) ~ ", " ~ bv ~ " break vertices"];''',
    "T2": r'''
        const edges = size(evaluateQuery(context, qOwnedByBody(wires, EntityType.EDGE)));
        const a = valueAt(wires, 2005);
        const m = valueAt(wires, 2045);
        const e = valueAt(wires, 2090);
        var jointsOk = true;
        var jt = "";
        for (var x in [2010, 2080])
        {
            const j = jointAt(wires, x);
            jointsOk = jointsOk && j.ok && j.angle < 1e-6 * radian && abs(j.k0) < 1e-6 / meter && abs(j.k1) < 1e-6 / meter;
            jt ~= " C2 at " ~ x ~ ": " ~ (j.ok ? "angle " ~ toString(j.angle) ~ ", curvature " ~ toString(j.k0) ~ " / " ~ toString(j.k1) : "joint not found") ~ ";";
        }
        const ok = edges == 3 && near(a[0], 0, 1e-6) && near(m[0], 2, 1e-6) && near(e[0], 4, 1e-6) && near(m[1], 1, 1e-6) && jointsOk;
        return [ok, edges ~ " edges, width at 2005 / 2045 / 2090 = " ~ fmt(a[0]) ~ " / " ~ fmt(m[0]) ~ " / " ~ fmt(e[0]) ~ ", height " ~ fmt(m[1]) ~ ";" ~ jt];''',
    "T3": r'''
        const a = valueAt(wires, 3080);
        const b = valueAt(wires, 3120);
        var jointsOk = true;
        var jt = "";
        for (var x in [3080, 3120])
        {
            const j = jointAt(wires, x);
            jointsOk = jointsOk && j.ok && j.angle < 1e-6 * radian && abs(j.k0 - j.k1) < 1e-4 / meter;
            jt ~= " G2 at " ~ x ~ ": " ~ (j.ok ? "angle " ~ toString(j.angle) ~ ", curvature " ~ toString(j.k0) ~ " / " ~ toString(j.k1) : "joint not found") ~ ";";
        }
        const ok = pieces == 1 && near(a[0], 1.6, 1e-6) && near(b[0], 5, 1e-6) && jointsOk;
        return [ok, pieces ~ " piece, width at 3080 / 3120 = " ~ fmt(a[0]) ~ " / " ~ fmt(b[0]) ~ ";" ~ jt];''',
    "T4": r'''
        var overshoot = false;
        for (var i = 0; i <= 20; i += 1)
        {
            const x = 4000 + 5 * i;
            const v = valueAt(wires, x);
            if (v[0] == undefined || v[0] < ((x <= 4050 ? 0 : 1) - 1e-6) * millimeter || v[0] > (3 + 1e-6) * millimeter)
            {
                overshoot = true;
            }
        }
        const m = valueAt(wires, 4050);
        const j = jointAt(wires, 4050);
        const flat = j.ok && j.angle < 1e-6 * radian && abs(j.k0 - j.k1) < 1e-4 / meter;
        return [pieces == 1 && !overshoot && near(m[0], 3, 1e-6) && near(m[1], 2, 1e-6) && flat,
            pieces ~ " piece, at 4050 width " ~ fmt(m[0]) ~ " height " ~ fmt(m[1]) ~ ", overshoot " ~ overshoot
            ~ ", joint " ~ (j.ok ? "angle " ~ toString(j.angle) ~ ", curvature " ~ toString(j.k0) ~ " / " ~ toString(j.k1) : "not found")];''',
    "T5": r'''
        const b = out.variable.breakStations.value;
        return [pieces == 2 && size(b) == 1 && near(b[0], 5050, 1e-6), pieces ~ " pieces, breaks " ~ join(mapArray(b, fmt), " / ")];''',
}


def strings(result):
    return [s for s in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(result)) if s not in ("BTFSValueString", "BTFSValueArray")]


def run_case(fid, body):
    script = ("function(context is Context, queries)\n{\n" + HELPERS +
              "    const check = function() returns array\n        {\n"
              "            const out = getVariable(context, toString(makeId(\"%s\")));\n"
              "            const wires = qCreatedBy(makeId(\"%s\"), EntityType.BODY);\n"
              "            const pieces = size(evaluateQuery(context, wires));\n" % (fid, fid) + body + "\n        };\n"
              "    const r = check();\n    return [r[0] ? \"PASS\" : \"FAIL\", r[1]];\n}\n")
    r = c.post(f"{BASE}/featurescript", json_data={"script": script})
    notices = [n.get("message") for n in (r.get("notices") or []) if n.get("level") == "ERROR"]
    got = strings(r.get("result"))
    if notices or len(got) < 2:
        return "FAIL", "check did not run: " + "; ".join(notices or ["no result"])
    return got[0], got[1]


def error_case(name, feature):
    """Insert a temporary instance that must fail, read its status, delete it."""
    f = c.get(f"{BASE}/features")
    body = {"btType": "BTFeatureDefinitionCall-1406", "feature": feature,
            "serializationVersion": f["serializationVersion"], "sourceMicroversion": f["sourceMicroversion"]}
    r = c.post(f"{BASE}/features", body)
    status = r.get("featureState", {}).get("featureStatus")
    c._request("DELETE", f"{BASE}/features/featureid/{r['feature']['featureId']}")
    return ("PASS" if status == "ERROR" else "FAIL"), "%s -> status %s (expected ERROR)" % (name, status)


def main():
    feats = c.get(f"{BASE}/features")["features"]
    failed = 0
    for key, body in CHECKS.items():
        match = [x for x in feats if x["name"].startswith(key + " ") and x["featureType"] == "createOffsetProfile"]
        if not match:
            print("FAIL", key, "-- case feature not found")
            failed += 1
            continue
        verdict, detail = run_case(match[0]["featureId"], body)
        failed += verdict != "PASS"
        print(verdict, match[0]["name"], "--", detail)

    # error cases: clone T1's instance and break it
    t1 = [x for x in feats if x["name"].startswith("T1 ")][0]
    for name, mutate in (("overlapping regions", lambda regions: set_len(regions[1], "startStation", "-50 mm")),
                         ("buffers longer than the region", lambda regions: (set_len(regions[1], "startBuffer", "600 mm"), set_len(regions[1], "endBuffer", "600 mm")))):
        feature = json.loads(json.dumps(t1))
        feature.pop("featureId", None)
        feature["name"] = "zz temporary error case"
        regions = [p for p in feature["parameters"] if p["parameterId"] == "regions"][0]["items"]
        mutate(regions)
        verdict, detail = error_case(name, feature)
        failed += verdict != "PASS"
        print(verdict, "Error case:", detail)
    print("%d failed" % failed if failed else "all passed")
    return 1 if failed else 0


def set_len(item, pid, expression):
    for p in item["parameters"]:
        if p["parameterId"] == pid:
            p["expression"] = expression
            p.pop("value", None)


if __name__ == "__main__":
    sys.exit(main())
