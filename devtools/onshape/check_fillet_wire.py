"""Check the Fillet wire test cases in Curve_tools' "Fillet wire tests" Part Studio (W1..W6, real features).

Measures each case's output through the eval API and prints PASS / FAIL.
usage (repo root): PYTHONPATH=. python devtools/onshape/check_fillet_wire.py
"""
import json
import re
import sys

from sync.core.client import OnshapeClient

D = "2143812a99089658c704f0bc"
c = OnshapeClient()
W = json.load(open("curve_tools/.document.json"))["workspace_id"]
E = [e["id"] for e in c.list_elements(D, W) if e["name"] == "Fillet wire tests"][0]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"

HELPERS = r'''
    const fmt = function(v) returns string
        {
            if (v is ValueWithUnits && v.unit == LENGTH_UNITS) { return toString(roundToPrecision(v / millimeter, 5)); }
            if (v is ValueWithUnits) { return toString(roundToPrecision(v * millimeter, 6)); }
            return toString(v);
        };
    // at each end of `edge`: the other output edge meeting there -> tangent angle and curvature on both sides
    const joints = function(edge is Query, others is Query) returns array
        {
            var out = [];
            const ends = evEdgeTangentLines(context, { "edge" : edge, "parameters" : [0, 1] });
            for (var k in [0, 1])
            {
                const kk = evEdgeCurvature(context, { "edge" : edge, "parameter" : k }).curvature;
                for (var o in evaluateQuery(context, qSubtraction(others, edge)))
                {
                    const oe = evEdgeTangentLines(context, { "edge" : o, "parameters" : [0, 1] });
                    for (var m in [0, 1])
                    {
                        if (norm(oe[m].origin - ends[k].origin) < 1e-7 * meter)
                        {
                            const a = angleBetween(ends[k].direction, oe[m].direction);
                            out = append(out, { "angle" : min(a, 180 * degree - a), "k0" : kk,
                                        "k1" : evEdgeCurvature(context, { "edge" : o, "parameter" : m }).curvature });
                        }
                    }
                }
            }
            return out;
        };
    const curvatures = function(edge is Query) returns array
        {
            var ks = [];
            for (var t in [0.1, 0.3, 0.5, 0.7, 0.9])
            {
                ks = append(ks, evEdgeCurvature(context, { "edge" : edge, "parameter" : t }).curvature);
            }
            return ks;
        };
'''

CHECKS = {
    "W1": r'''
        const edges = qOwnedByBody(output, EntityType.EDGE);
        const len = evLength(context, { "entities" : edges });
        const fillets = evaluateQuery(context, out.query.filletEdges.value);
        var ok = size(evaluateQuery(context, output)) == 1 && abs(len - 291.41593 * millimeter) < 1e-4 * millimeter && size(fillets) == 2;
        var txt = "";
        for (var f in fillets)
        {
            for (var k in curvatures(f)) { ok = ok && abs(k - 0.1 / millimeter) < 1e-6 / millimeter; }
            for (var j in joints(f, edges)) { ok = ok && j.angle < 1e-6 * radian; txt ~= " G1 angle " ~ toString(j.angle); }
        }
        return [ok, size(evaluateQuery(context, output)) ~ " wire, length " ~ fmt(len) ~ " (291.41593), " ~ size(fillets) ~ " fillets of curvature 0.1/mm;" ~ txt];''',
    "W2": r'''
        const edges = qOwnedByBody(output, EntityType.EDGE);
        const pieces = size(evaluateQuery(context, output));
        const fillets = evaluateQuery(context, out.query.filletEdges.value);
        var radii = [];
        var ok = pieces == 5 && size(fillets) == 2 && out.variable.filletCount.value == 2;
        for (var f in fillets)
        {
            const k = curvatures(f)[2];
            radii = append(radii, fmt(1 / k));
            for (var j in joints(f, edges)) { ok = ok && j.angle < 1e-6 * radian; }
        }
        ok = ok && isIn("5", radii) && isIn("10", radii);
        const sharp = !isQueryEmpty(context, qContainsPoint(qOwnedByBody(output, EntityType.VERTEX), vector(100, 400, 0) * millimeter));
        const traced = size(evaluateQuery(context, qBodyType(qCreatedBy(makeId(SOURCE), EntityType.BODY), BodyType.WIRE)));
        ok = ok && sharp && traced == 5;
        return [ok, pieces ~ " pieces (5), fillet radii " ~ join(radii, " / ") ~ " (5 and 10), corner 100,400 still sharp " ~ sharp
            ~ ", pieces traced to the original wire " ~ traced ~ " (5)"];''',
    "W3": r'''
        const edges = qOwnedByBody(output, EntityType.EDGE);
        const fillets = evaluateQuery(context, out.query.filletEdges.value);
        var ok = size(fillets) == 1;
        var txt = "";
        if (ok)
        {
            for (var k in curvatures(fillets[0])) { ok = ok && abs(k - 0.1 / millimeter) < 1e-6 / millimeter; }
            const js = joints(fillets[0], edges);
            ok = ok && size(js) == 2;
            for (var j in js) { ok = ok && j.angle < 1e-6 * radian; txt ~= " G1 angle " ~ toString(j.angle) ~ ";"; }
        }
        return [ok, size(fillets) ~ " fillet, constant curvature 0.1/mm, tangent to the line and the arc:" ~ txt];''',
    "W4": r'''
        return [out.variable.filletCount.value == 0 && out.variable.skippedCount.value == 1,
            "filleted " ~ out.variable.filletCount.value ~ " (0), skipped " ~ out.variable.skippedCount.value ~ " (1)"];''',
    "W5": r'''
        const edges = qOwnedByBody(output, EntityType.EDGE);
        const fillets = evaluateQuery(context, out.query.filletEdges.value);
        var ok = size(fillets) == 1;
        var txt = "";
        if (ok)
        {
            const ends = evEdgeTangentLines(context, { "edge" : fillets[0], "parameters" : [0, 1] });
            const turn = angleBetween(ends[0].direction, ends[1].direction);
            const avg = turn.value / evLength(context, { "entities" : fillets[0] });
            ok = ok && abs(avg - 0.1 / millimeter) < 1e-5 / millimeter;
            txt ~= " average curvature " ~ fmt(avg) ~ "/mm (0.1);";
            const js = joints(fillets[0], edges);
            ok = ok && size(js) == 2;
            for (var j in js)
            {
                ok = ok && j.angle < 1e-6 * radian && abs(j.k0 - j.k1) < 1e-4 / millimeter;
                txt ~= " G2: angle " ~ toString(j.angle) ~ ", curvature " ~ fmt(j.k0) ~ " / " ~ fmt(j.k1) ~ ";";
            }
        }
        return [ok, size(fillets) ~ " blend;" ~ txt];''',
    "W6": r'''
        return [out.variable.filletCount.value == 1 && out.variable.skippedCount.value == 1,
            "filleted " ~ out.variable.filletCount.value ~ " (1), refused " ~ out.variable.skippedCount.value ~ " (1: the second fillet would overlap the first)"];''',
}


def strings(result):
    return [s for s in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(result)) if s not in ("BTFSValueString", "BTFSValueArray")]


def main():
    feats = c.get(f"{BASE}/features")["features"]
    source = [x["featureId"] for x in feats if x["name"] == "W2 wire (composite curve)"][0]
    failed = 0
    for key, body in CHECKS.items():
        match = [x for x in feats if x["name"].startswith(key + " ") and x["featureType"] == "filletWire"]
        if not match:
            print("FAIL", key, "-- case feature not found")
            failed += 1
            continue
        fid = match[0]["featureId"]
        script = ("function(context is Context, queries)\n{\n" + HELPERS + "    const SOURCE = \"%s\";\n" % source +
                  "    const check = function() returns array\n        {\n"
                  "            const out = getVariable(context, toString(makeId(\"%s\")));\n"
                  "            const output = out.query.output.value;\n" % fid + body + "\n        };\n"
                  "    const r = check();\n    return [r[0] ? \"PASS\" : \"FAIL\", r[1]];\n}\n")
        r = c.post(f"{BASE}/featurescript", json_data={"script": script})
        errors = [n.get("message") for n in (r.get("notices") or []) if n.get("level") == "ERROR"]
        got = strings(r.get("result"))
        if errors or len(got) < 2:
            print("FAIL", match[0]["name"], "-- check did not run:", "; ".join(errors or ["no result"]))
            failed += 1
            continue
        failed += got[0] != "PASS"
        print(got[0], match[0]["name"], "--", got[1])
    print("%d failed" % failed if failed else "all passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
