"""Check the utility-feature test cases built by build_utility_tests.py:

  example_1 / "Utility tests"  Join wires J1-J5, Extrude edge E1, E2, E4, E7, E8
  bodyRename / "Rename tests"  Simple Body Rename R1-R6

Each case is measured through the eval API and printed PASS / FAIL, and every feature's status is checked
(fixtures OK; a case's expected status from EXPECTED_STATUS, else OK). The expectations are the DESIRED
behaviour from reviews/2026-09-25_tools_review/utility.md. Cases that document a bug present today are in
EXPECT_FAIL: a failure there prints XFAIL and is not counted; a pass prints XPASS and IS counted, so the entry
gets removed once the bug is fixed. The error cases (E3, E6, E9) are checked by the builder (temporary instances).

usage (repo root): PYTHONPATH=. python devtools/onshape/check_utility_tests.py
"""
import json
import re
import sys

from sync.core.client import OnshapeClient

c = OnshapeClient()

HELPERS = r'''
    const pt = function(x, y, z) { return vector(x, y, z) * millimeter; };
    const fmt = function(v) { return toString(roundToPrecision(v / millimeter, 4)); };
    const fmtA = function(v) { return toString(roundToPrecision(v / (millimeter * millimeter), 3)); };
    const created = function(fid) { return qCreatedBy(makeId(fid), EntityType.BODY); };
    const count = function(q) { return size(evaluateQuery(context, q)); };
    const near = function(a, b) { return abs(a - b) < 0.001 * millimeter; };
    const wires = function(fid) { return qBodyType(created(fid), BodyType.WIRE); };
    const sheets = function(fid) { return qBodyType(created(fid), BodyType.SHEET); };
    const edgesOf = function(q) { return qOwnedByBody(q, EntityType.EDGE); };
    const lengthOf = function(q) { return count(q) == 0 ? 0 * millimeter : evLength(context, { "entities" : edgesOf(q) }); };
    const areaOf = function(q) { return count(q) == 0 ? 0 * millimeter * millimeter : evArea(context, { "entities" : qOwnedByBody(q, EntityType.FACE) }); };
    const zRange = function(q) { const bb = evBox3d(context, { "topology" : q, "tight" : true }); return [bb.minCorner[2], bb.maxCorner[2]]; };
    const name = function(q) { return count(q) == 1 ? getProperty(context, { "entity" : q, "propertyType" : PropertyType.NAME }) : "(" ~ count(q) ~ " bodies)"; };
    const wireSummary = function(q) { return count(q) ~ " wire(s), " ~ count(edgesOf(q)) ~ " edge(s), length " ~ fmt(lengthOf(q)); };
'''

# (studio key, tag prefix, FS body returning [ok, message], fixtures {placeholder: fixture-name prefix})
CASES = []


def case(studio, prefix, src, **fixtures):
    CASES.append((studio, prefix, src, fixtures))


# ---- Join wires ----
case("U", "J1 ", r'''
        const out = wires(SELF);
        const seedsGone = count(created(@WA@)) == 0 && count(created(@WB@)) == 0;
        return [count(out) == 1 && count(edgesOf(out)) == 2 && near(lengthOf(out), 170.7107 * millimeter) && seedsGone,
            wireSummary(out) ~ "; seeds deleted " ~ seedsGone ~ " (expected 1 wire, 2 edges, 170.7107, true)"];''',
     WA="J1 wire A", WB="J1 wire B")

case("U", "J2 ", r'''
        const out = wires(SELF);
        const blockKept = count(created(@BLOCK@)) == 1;
        const seedGone = count(created(@WIRE@)) == 0;
        return [count(out) == 1 && count(edgesOf(out)) == 2 && near(lengthOf(out), 100 * millimeter) && blockKept && seedGone,
            wireSummary(out) ~ "; block kept " ~ blockKept ~ ", seed deleted " ~ seedGone ~ " (expected 1 wire, 2 edges, 100, true, true)"];''',
     BLOCK="J2 block", WIRE="J2 wire")

case("U", "J3 ", r'''
        const out = wires(SELF);
        return [count(out) == 2 && near(lengthOf(out), 200 * millimeter), wireSummary(out) ~ " (expected 2 wires, 200)"];''')

case("U", "J4 ", r'''
        const out = wires(SELF);
        const nv = count(qOwnedByBody(out, EntityType.VERTEX));
        return [count(out) == 1 && count(edgesOf(out)) == 3 && nv == 3, wireSummary(out) ~ ", " ~ nv ~ " vertices (expected 1 wire, 3 edges, 3 vertices)"];''')

case("U", "J5 ", r'''
        const out = wires(SELF);
        const kept = count(created(@WA@)) == 1 && count(created(@WB@)) == 1;
        return [count(out) == 1 && count(edgesOf(out)) == 2 && kept, wireSummary(out) ~ "; seeds kept " ~ kept];''',
     WA="J5 wire A", WB="J5 wire B")


# ---- Extrude edge ----
def extrusion(prefix, area, z0, z1, nsheets=1):
    case("U", prefix, (r'''
        const out = sheets(SELF);
        if (count(out) != NSHEETS)
        {
            return [false, count(out) ~ " sheet bodies (expected NSHEETS)"];
        }
        const z = zRange(out);
        return [abs(areaOf(out) - %r * millimeter * millimeter) < 0.01 * millimeter * millimeter && near(z[0], %r * millimeter) && near(z[1], %r * millimeter),
            count(out) ~ " sheet(s), area " ~ fmtA(areaOf(out)) ~ ", z " ~ fmt(z[0]) ~ ".." ~ fmt(z[1]) ~ " (expected area %g, z %g..%g)"];''' % (area, z0, z1, area, z0, z1)).replace("NSHEETS", str(nsheets)))


extrusion("E1 ", 178.5398163 * 30, 0, 30)
extrusion("E2 ", 1000, 10, 35, nsheets=2)  # opposite top edges of the block: not connected, two sheets
extrusion("E4 ", 2500, -5, 20)
extrusion("E7 ", 2000, -20, 0)
extrusion("E8 ", 2000, 0, 20)


# ---- Simple Body Rename ----
def renamed(prefix, target, expected):
    case("R", prefix, r'''
        const n = name(created(@TARGET@));
        return [n == "%s", "name '" ~ n ~ "' (expected '%s')"];''' % (expected, expected), TARGET=target)


renamed("R1 ", "R1 block", "R1 solid")
renamed("R3 ", "R3 block", "P_mid_S")
renamed("R6 ", "R6 wire", "R6 wire")
case("R", "R2 ", r'''
        const n = name(qBodyType(created(@COMP@), BodyType.COMPOSITE));
        return [n == "R2 composite", "composite name '" ~ n ~ "' (expected 'R2 composite')"];''', COMP="R2 composite part")
case("R", "R4 ", r'''
        const n = name(created(@BLOCK@));
        return [count(created(@BLOCK@)) == 1 && n != "PRE_" && n != "", "name '" ~ n ~ "' (expected the default name, not 'PRE_')"];''', BLOCK="R4 block")
case("R", "R5 ", r'''
        const n = name(created(@BLOCK@));
        return [n == "R5 kept", "name '" ~ n ~ "' (expected 'R5 kept')"];''', BLOCK="R5 block")

# Simple rename reports "Renamed N bodies." as INFO (2026-09-25 guards), so every rename case is INFO except R5.
EXPECTED_STATUS = {"J3 ": "WARNING", "J4 ": "INFO", "R1 ": "INFO", "R2 ": "INFO", "R3 ": "INFO", "R4 ": "INFO", "R5 ": "WARNING", "R6 ": "INFO"}

# J2, J3, R4, R5 fixed 2026-09-25 (quick fixes after the tools review) -- they now guard against regressions.
EXPECT_FAIL = {
    # J4 fixed 2026-09-25 (closed-loop INFO).
}

STUDIOS = {"U": ("example_1/.document.json", "Utility tests"), "R": ("bodyRename/.document.json", "Rename tests")}


def strings(result):
    return [s for s in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(result)) if not s.startswith("BTFSValue")]


def main():
    failed = xfailed = 0
    for key, (doc_json, studio) in STUDIOS.items():
        doc = json.load(open(doc_json))
        D, W = doc["document_id"], doc["workspace_id"]
        found = [e["id"] for e in c.list_elements(D, W) if e["name"] == studio and e["elementType"] == "PARTSTUDIO"]
        if not found:
            print("FAIL studio %r not found (run build_utility_tests.py)" % studio)
            failed += 1
            continue
        base = f"/api/v10/partstudios/d/{D}/w/{W}/e/{found[0]}"
        feats = c.get(f"{base}/features")
        states = feats["featureStates"]
        by_name = [(x["name"], x["featureId"]) for x in feats["features"]]
        print("== %s (%s)" % (studio, found[0]))

        def fid(prefix):
            hits = [i for n, i in by_name if n.startswith(prefix) and "->" not in n and not n.endswith("(sketch)")]
            if len(hits) != 1:
                raise SystemExit("fixture prefix %r matches %d features" % (prefix, len(hits)))
            return hits[0]

        # statuses: fixtures OK; cases their expected status
        status_bad = {}
        for n, i in by_name:
            status = states.get(i, {}).get("featureStatus")
            tag = n.split(" ")[0] + " "
            want = EXPECTED_STATUS.get(tag, "OK") if "->" in n else "OK"
            if status != want:
                if "->" in n:
                    status_bad[tag] = "status %s, expected %s" % (status, want)
                else:
                    failed += 1
                    print("FAIL", n, "-- fixture status", status)

        for studio_key, prefix, src, fixtures in CASES:
            if studio_key != key:
                continue
            hits = [(n, i) for n, i in by_name if n.startswith(prefix) and "->" in n]
            if len(hits) != 1:
                print("FAIL", prefix, "-- case feature not found (%d matches)" % len(hits))
                failed += 1
                continue
            name, self_id = hits[0]
            body = src.replace("SELF", '"%s"' % self_id)
            for k, v in fixtures.items():
                body = body.replace("@%s@" % k, '"%s"' % fid(v))
            script = ("function(context is Context, queries)\n{\n" + HELPERS + "    const check = function() returns array\n        {\n"
                      + body + "\n        };\n    const r = check();\n    return [r[0] ? \"PASS\" : \"FAIL\", r[1]];\n}\n")
            r = c.post(f"{base}/featurescript", json_data={"script": script})
            errors = [x.get("message") for x in (r.get("notices") or []) if x.get("level") == "ERROR"]
            got = strings(r.get("result"))
            if errors or len(got) < 2:
                verdict, msg = "FAIL", "check did not run: " + "; ".join(str(e) for e in (errors or ["no result"]))
            else:
                verdict, msg = got[0], got[1]
            if prefix in status_bad:
                verdict, msg = "FAIL", status_bad.pop(prefix) + "; " + msg
            if prefix in EXPECT_FAIL:
                if verdict == "FAIL":
                    xfailed += 1
                    print("XFAIL", name, "--", msg, "\n      known bug:", EXPECT_FAIL[prefix])
                else:
                    failed += 1
                    print("XPASS", name, "--", msg, "\n      bug fixed? remove it from EXPECT_FAIL:", EXPECT_FAIL[prefix])
                continue
            failed += verdict != "PASS"
            print(verdict, name, "--", msg)
        for tag, msg in status_bad.items():
            failed += 1
            print("FAIL", tag, "--", msg, "(no value check)")
    print("%d failed, %d expected failures" % (failed, xfailed) if failed else "all passed (%d expected failures)" % xfailed)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
