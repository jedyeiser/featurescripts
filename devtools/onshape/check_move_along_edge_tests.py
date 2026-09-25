"""Check the Move Along Edge test cases in smallTools' "Move along edge tests" Part Studio (built by
build_move_along_edge_tests.py): each case is measured through the eval API and printed PASS / FAIL, and
every feature's status is checked (M3b must be a WARNING: that is its case).

usage (repo root): PYTHONPATH=. python devtools/onshape/check_move_along_edge_tests.py
"""
import json
import re
import sys

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("smallTools/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
E = [e["id"] for e in c.list_elements(D, W) if e["name"] == "Move along edge tests"][0]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"

HELPERS = r'''
    const pt = function(x, y, z) { return vector(x, y, z) * millimeter; };
    const fmt = function(v) { return toString(roundToPrecision(v / millimeter, 4)); };
    const fmtV = function(p) { return "(" ~ fmt(p[0]) ~ ", " ~ fmt(p[1]) ~ ", " ~ fmt(p[2]) ~ ")"; };
    const created = function(fid) { return qCreatedBy(makeId(fid), EntityType.BODY); };
    const embedded = function(fid) { return getVariable(context, toString(makeId(fid))); };
    const count = function(q) { return size(evaluateQuery(context, q)); };
    const centroid = function(q) { return evApproximateCentroid(context, { "entities" : q }); };
    const at = function(q, x, y, z) { return count(q) == 1 && norm(centroid(q) - pt(x, y, z)) < 0.001 * millimeter; };
    const name = function(q) { return count(q) == 1 ? getProperty(context, { "entity" : q, "propertyType" : PropertyType.NAME }) : "(" ~ count(q) ~ " bodies)"; };
    const describe = function(label, q) { return label ~ " " ~ (count(q) == 1 ? fmtV(centroid(q)) ~ " '" ~ name(q) ~ "'" : count(q) ~ " bodies"); };
    const mcOrigin = function(q) { return evMateConnector(context, { "mateConnector" : q }).origin; };
'''

CASES = []


def case(prefix, body, **names):
    CASES.append((prefix, body, names))


def moved_to(prefix, cube, x, y, z):
    case(prefix, r'''
        const cube = created(@CUBE@);
        return [at(cube, %g, %g, %g), describe("cube", cube) ~ ", expected (%g, %g, %g)"];''' % (x, y, z, x, y, z), CUBE=cube)


moved_to("M1 ", "M1 cube", 100, 0, 0)
moved_to("M2 ", "M2 cube", 1270.7107, 29.2893, 0)

case("M3 ", r'''
        const cube = created(@CUBE@);
        const cubeName = name(cube);
        const out = embedded(SELF);
        var keys = [];
        for (var entry in out.query)
        {
            keys = append(keys, entry.key);
        }
        var keysOk = size(keys) == 9;
        for (var k in ["copy_1", "copy_2", "copy_3", "initial_copy", "inputs", "output", "outputEdges", "outputFaces", "outputVertices"])
        {
            keysOk = keysOk && out.query[k] != undefined;
        }
        const q0 = out.query.initial_copy.value;
        const q1 = out.query.copy_1.value;
        const q2 = out.query.copy_2.value;
        const q3 = out.query.copy_3.value;
        const ok = at(cube, 2000, 0, 0) && keysOk && count(out.query.output.value) == 4
            && at(q0, 2100, 0, 0) && name(q0) == "M3 main"
            && at(q1, 2050, 0, 0) && name(q1) == ("A_" ~ cubeName)
            && at(q2, 2150, 0, 0) && name(q2) == (cubeName ~ "_B")
            && at(q3, 2250, 0, 0);
        return [ok, describe("source", cube) ~ "; " ~ describe("initial_copy", q0) ~ "; " ~ describe("copy_1", q1) ~ "; "
            ~ describe("copy_2", q2) ~ "; " ~ describe("copy_3", q3) ~ "; output " ~ count(out.query.output.value) ~ "; keys " ~ toString(keys)];''',
     CUBE="M3 cube")

case("M3b ", r'''
        const q0 = embedded(SELF).query.initial_copy.value;
        return [at(q0, 3100, 0, 0) && name(q0) == "P_", describe("initial_copy", q0) ~ ", expected (3100, 0, 0) 'P_'"];''')

case("M4 ", r'''
        const out = embedded(SELF);
        const q0 = out.query.initial_copy.value;
        const q1 = out.query.copy_1.value;
        const mc = created(@MC@);
        const points = count(qBodyType(out.query.output.value, BodyType.POINT));
        const ok = at(q0, 4100, 0, 0) && name(q0) == "M4 P0" && at(q1, 4200, 0, 0) && name(q1) == "M4 P1" && points == 2
            && norm(mcOrigin(mc) - pt(4000, 0, 0)) < 0.001 * millimeter;
        return [ok, describe("initial_copy", q0) ~ "; " ~ describe("copy_1", q1) ~ "; " ~ points ~ " point bodies; connector at " ~ fmtV(mcOrigin(mc))];''',
     MC="M4 mate connector")

case("M5 ", r'''
        const o = mcOrigin(created(@MC@));
        return [norm(o - pt(5100, 0, 0)) < 0.001 * millimeter, "connector at " ~ fmtV(o) ~ ", expected (5100, 0, 0)"];''',
     MC="M5 mate connector")

case("M6 ", r'''
        const cube = created(@CUBE@);
        return [at(cube, 6100, -100, 0) || at(cube, 5900, -100, 0), describe("cube", cube) ~ ", expected (6100 or 5900, -100, 0)"];''',
     CUBE="M6 cube")

moved_to("M7 ", "M7 cube", 7200, 200, -30)
moved_to("M8 ", "M8 cube", 8200, 200, 30)
moved_to("M9 ", "M9 cube", 8950, 0, 0)
moved_to("M10 ", "M10 cube", 10100, 50, 0)

case("M12 ", r'''
        const q0 = embedded(SELF).query.initial_copy.value;
        const wires = count(qBodyType(q0, BodyType.WIRE));
        const source = created(@SKETCH@);
        return [at(q0, 12100, 40, 0) && wires == 1 && name(q0) == "M12 wire" && at(source, 12000, 40, 0),
            describe("initial_copy", q0) ~ " (" ~ wires ~ " wire); " ~ describe("source sketch", source)];''', SKETCH="M12 source sketch")

case("M13 ", r'''
        const out = embedded(SELF);
        const q0 = out.query.initial_copy.value;
        const q1 = out.query.copy_1.value;
        const points = count(qBodyType(out.query.output.value, BodyType.POINT));
        return [at(q0, 13100, 30, 0) && name(q0) == "M13 P0" && at(q1, 13200, 30, 0) && name(q1) == "M13 P1" && points == 2,
            describe("initial_copy", q0) ~ "; " ~ describe("copy_1", q1) ~ "; " ~ points ~ " point bodies"];''')

case("M14 ", r'''
        const q0 = evaluateQuery(context, embedded(SELF).query.initial_copy.value);
        const cube = created(@CUBE@);
        const cubeName = name(cube);
        var wire = qNothing();
        var solid = qNothing();
        for (var b in q0)
        {
            if (count(qBodyType(b, BodyType.WIRE)) == 1) { wire = b; }
            if (count(qBodyType(b, BodyType.SOLID)) == 1) { solid = b; }
        }
        return [size(q0) == 2 && at(wire, 14100, 40, 0) && name(wire) == "_S" && at(solid, 14100, 0, 0) && name(solid) == (cubeName ~ "_S") && at(cube, 14000, 0, 0),
            describe("wire", wire) ~ "; " ~ describe("solid", solid) ~ "; " ~ describe("source cube", cube)];''', CUBE="M14 cube")

EXPECTED_WARNINGS = ["M3b "]


def strings(result):
    return [s for s in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(result)) if not s.startswith("BTFSValue")]


def main():
    feats = c.get(f"{BASE}/features")
    states = feats["featureStates"]
    by_name = [(x["name"], x["featureId"]) for x in feats["features"]]
    failed = 0

    def fid(prefix):
        found = [i for n, i in by_name if n.startswith(prefix) and not n.endswith("(sketch)")]
        if len(found) != 1:
            raise SystemExit("feature prefix %r matches %d features" % (prefix, len(found)))
        return found[0]

    for name, i in by_name:
        status = states.get(i, {}).get("featureStatus")
        want = "WARNING" if any(name.startswith(p) for p in EXPECTED_WARNINGS) and "->" in name and not name.startswith("M3b path") else "OK"
        if status != want:
            failed += 1
            print("FAIL", name, "-- status", status, "expected", want)

    for prefix, body, names in CASES:
        cases = [(n, i) for n, i in by_name if n.startswith(prefix) and "->" in n and not n.startswith(prefix + "path") and not n.startswith(prefix + "source")]
        if len(cases) != 1:
            print("FAIL", prefix, "-- case feature not found")
            failed += 1
            continue
        name, self_id = cases[0]
        src = body.replace("SELF", '"%s"' % self_id)
        for key, value in names.items():
            src = src.replace("@%s@" % key, '"%s"' % fid(value))
        script = ("function(context is Context, queries)\n{\n" + HELPERS + "    const check = function() returns array\n        {\n"
                  + src + "\n        };\n    const r = check();\n    return [r[0] ? \"PASS\" : \"FAIL\", r[1]];\n}\n")
        r = c.post(f"{BASE}/featurescript", json_data={"script": script})
        errors = [x.get("message") for x in (r.get("notices") or []) if x.get("level") == "ERROR"]
        got = strings(r.get("result"))
        if errors or len(got) < 2:
            print("FAIL", name, "-- check did not run:", "; ".join(str(e) for e in (errors or ["no result"])))
            failed += 1
            continue
        failed += got[0] != "PASS"
        print(got[0], name, "--", got[1])
    print("%d failed" % failed if failed else "all passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
