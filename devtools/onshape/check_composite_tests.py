"""Check the Composite boolean test cases built by build_composite_tests.py:

  composite_part_tools / "Composite tests"   C1-C7, L1 (E1, E2 are checked by the builder: temporary instances)

Each case is measured through the eval API and printed PASS / FAIL, and every feature's status is checked
(fixtures OK; a case's expected status from EXPECTED_STATUS, else OK). A case that documents a known bug goes in
EXPECT_FAIL: a failure there prints XFAIL and is not counted; a pass prints XPASS and IS counted.

usage (repo root): PYTHONPATH=. python devtools/onshape/check_composite_tests.py
"""
import json
import os
import re
import sys

from sync.core.client import OnshapeClient

DOC_JSON = "composite_part_tools/.document.json"
STUDIO = "Composite tests"

c = OnshapeClient()

HELPERS = r'''
    const created = function(fid) { return qCreatedBy(makeId(fid), EntityType.BODY); };
    const count = function(q) { return size(evaluateQuery(context, q)); };
    const mm3 = millimeter * millimeter * millimeter;
    const vol = function(q) { return count(q) == 0 ? 0 * mm3 : evVolume(context, { "entities" : q }); };
    const fmtV = function(v) { return toString(roundToPrecision(v / mm3, 3)); };
    const nearV = function(a, b) { return abs(a - b) < 0.01 * mm3; };
    const resultOf = function(fid) { return qBodyType(created(fid), BodyType.COMPOSITE); };
    const partsOf = function(q) { return qBodyType(qContainedInCompositeParts(q), BodyType.SOLID); };
    const summary = function(fid) { const r = resultOf(fid); return count(r) ~ " composite(s), " ~ count(partsOf(r)) ~ " bodies, volume " ~ fmtV(vol(partsOf(r))); };
    const clipped = function(fid, nBodies, volume) {
        const r = resultOf(fid);
        return count(r) == 1 && count(partsOf(r)) == nBodies && nearV(vol(partsOf(r)), volume * mm3);
    };
'''

CASES = []


def case(prefix, src, **fixtures):
    CASES.append((prefix, src, fixtures))


def clip_case(prefix, n, volume, extra_ok="true", extra_msg='""', **fixtures):
    case(prefix, r'''
        return [clipped(SELF, %d, %r) && %s, summary(SELF) ~ " (expected 1 composite, %d bodies, volume %g)" ~ %s];'''
         % (n, volume, extra_ok, n, volume, extra_msg), **fixtures)


# The inputs are never modified by a clip: the source composite keeps its 3 strips and the solid its volume.
INPUTS_KEPT_OK = 'count(partsOf(qBodyType(created(@COMP@), BodyType.COMPOSITE))) == 3 && nearV(vol(created(@SOLID@)), 200000 * mm3)'
INPUTS_KEPT_MSG = '"; inputs kept " ~ (count(partsOf(qBodyType(created(@COMP@), BodyType.COMPOSITE))) == 3 && nearV(vol(created(@SOLID@)), 200000 * mm3))'

clip_case("C1 ", 2, 4000, INPUTS_KEPT_OK, INPUTS_KEPT_MSG, COMP="C1 composite", SOLID="C1 solid")
clip_case("C2 ", 2, 5000, INPUTS_KEPT_OK, INPUTS_KEPT_MSG, COMP="C2 composite", SOLID="C2 solid")
clip_case("C3 ", 1, 8000)
clip_case("C4 ", 1, 4000)
case("C5 ", r'''
        const parts = evaluateQuery(context, partsOf(resultOf(SELF)));
        var each = [];
        var ok = size(parts) == 2;
        for (var p in parts)
        {
            each = append(each, fmtV(vol(p)));
            ok = ok && nearV(vol(p), 2000 * mm3);
        }
        return [ok, size(parts) ~ " bodies, volumes " ~ toString(each) ~ " (expected 2 bodies of 2000)"];''')
case("C6 ", r'''
        const r = resultOf(SELF);
        const closed = count(qCompositePartsContaining(partsOf(r), CompositePartType.CLOSED)) == 1;
        const n = count(r) == 1 ? getProperty(context, { "entity" : r, "propertyType" : PropertyType.NAME }) : "";
        return [clipped(SELF, 2, 4000) && closed && n == "C6 clipped",
            summary(SELF) ~ ", closed " ~ closed ~ ", name '" ~ n ~ "' (expected 2 bodies, 4000, closed, 'C6 clipped')"];''')
case("C7 ", r'''
        const v = vol(created(@BLOCK@));
        const strips = count(partsOf(qBodyType(created(@COMP@), BodyType.COMPOSITE)));
        return [nearV(v, 180000 * mm3) && strips == 2,
            "block volume " ~ fmtV(v) ~ ", composite strips " ~ strips ~ " (expected 180000, 2)"];''',
     BLOCK="C7 solid", COMP="C7 composite")
clip_case("L1 ", 2, 4000, 'count(qCreatedBy(makeId(SELF) + "result", EntityType.BODY)) >= 1',
          '"; op id result present " ~ (count(qCreatedBy(makeId(SELF) + "result", EntityType.BODY)) >= 1)')

# Every clip reports its summary as INFO; C4 turns WARNING when evCollision cannot place an abutting strip.
EXPECTED_STATUS = {t: "INFO" for t in ("C1 ", "C2 ", "C3 ", "C4 ", "C5 ", "C6 ", "C7 ", "L1 ")}

EXPECT_FAIL = {
}


def strings(result):
    return [s for s in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(result)) if not s.startswith("BTFSValue")]


def main():
    if not os.path.exists(DOC_JSON):
        print("FAIL %s not found (document not created yet)" % DOC_JSON)
        return 1
    failed = xfailed = 0
    doc = json.load(open(DOC_JSON))
    D, W = doc["document_id"], doc["workspace_id"]
    found = [e["id"] for e in c.list_elements(D, W) if e["name"] == STUDIO and e["elementType"] == "PARTSTUDIO"]
    if not found:
        print("FAIL studio %r not found (run build_composite_tests.py)" % STUDIO)
        return 1
    base = f"/api/v10/partstudios/d/{D}/w/{W}/e/{found[0]}"
    feats = c.get(f"{base}/features")
    states = feats["featureStates"]
    by_name = [(x["name"], x["featureId"]) for x in feats["features"]]
    print("== %s (%s)" % (STUDIO, found[0]))

    def fid(prefix):
        hits = [i for n, i in by_name if n.startswith(prefix) and "->" not in n and not n.endswith("(sketch)")]
        if len(hits) != 1:
            raise SystemExit("fixture prefix %r matches %d features" % (prefix, len(hits)))
        return hits[0]

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

    for prefix, src, fixtures in CASES:
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
