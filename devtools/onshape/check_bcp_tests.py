"""Check the Bridging curve + tests built by build_bcp_tests.py ("BCP tests" studio).

One features GET and one FeatureScript eval: for each B-case the bridge's end points, the curve direction at its start and
at its end (along the curve, start to end), the control degree, and its length. Prints PASS/FAIL; exit 1 on FAIL.

usage (repo root): PYTHONPATH=. python devtools/onshape/check_bcp_tests.py
"""
import json
import re
import sys

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("multi_mate_connector/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
E = [e["id"] for e in c.list_elements(D, W) if e["name"] == "BCP tests"][0]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"

tree = c.get(f"{BASE}/features")
mine = [f for f in tree["features"] if f["featureType"] == "bridgingCurvePlus"]
status = {f["name"].split()[0]: tree["featureStates"][f["featureId"]]["featureStatus"] for f in mine}
fids = {f["name"].split()[0]: f["featureId"] for f in mine}

SCRIPT = '''function(context is Context, queries) {
  var out = {};
  for (var name, fid in FIDS) {
    var rows = [];
    for (var e in evaluateQuery(context, qCreatedBy(makeId(fid), EntityType.EDGE))) {
      const t0 = evEdgeTangentLine(context, { "edge" : e, "parameter" : 0 });
      const t1 = evEdgeTangentLine(context, { "edge" : e, "parameter" : 1 });
      rows = append(rows, [roundToPrecision(t0.origin[0] / millimeter, 2), roundToPrecision(t0.origin[1] / millimeter, 2),
                           roundToPrecision(t1.origin[0] / millimeter, 2), roundToPrecision(t1.origin[1] / millimeter, 2),
                           roundToPrecision(t0.direction[0], 3), roundToPrecision(t0.direction[1], 3),
                           roundToPrecision(t1.direction[0], 3), roundToPrecision(t1.direction[1], 3),
                           roundToPrecision(evLength(context, { "entities" : e }) / millimeter, 2)]);
    }
    out[name] = rows;
  }
  return toString(out);
}'''.replace("FIDS", "{" + ", ".join('"%s" : "%s"' % (k, v) for k, v in fids.items()) + "}")

raw = c.post(f"{BASE}/featurescript", json_data={"script": SCRIPT})["result"]["value"]
result = json.loads(re.sub(r"(B\d+) :", r'"\1":', raw))

fails = 0


def check(label, ok, detail=""):
    global fails
    print("%s  %-70s %s" % ("PASS" if ok else "FAIL", label, detail))
    if not ok:
        fails += 1


def row(case):
    r = result.get(case) or []
    return r[0] if r else None


def ends(case, p0, p1, tol=0.05):
    r = row(case)
    ok = r is not None and all(abs(a - b) <= tol for a, b in zip(r[0:4], list(p0) + list(p1)))
    check("%s joins %s and %s" % (case, p0, p1), ok, str(r[0:4]) if r else "no curve")


def heading(case, start=None, end=None, tol=0.02):
    r = row(case)
    ok = r is not None
    if ok and start is not None:
        ok = all(abs(a - b) <= tol for a, b in zip(r[4:6], start))
    if ok and end is not None:
        ok = all(abs(a - b) <= tol for a, b in zip(r[6:8], end))
    check("%s heading start %s end %s" % (case, start, end), ok, str(r[4:8]) if r else "no curve")


for case, st in sorted(status.items(), key=lambda kv: int(kv[0][1:])):
    check("%s status" % case, (st == "ERROR") if case in ("B9", "B17") else (st in ("OK", "INFO")), st)

ends("B1", (100, 0), (200, 50))
heading("B1", (1, 0), (1, 0))
ends("B2", (100, 0), (200, 100))
heading("B2", (1, 0), (1, 0))
ends("B3", (100, 0), (200, 50))
r3 = row("B3")
check("B3 straight: length = 111.80", r3 is not None and abs(r3[8] - 111.8) < 0.05, str(r3[8] if r3 else None))
ends("B4", (100, 0), (200, 150))
heading("B4", (1, 0), None)
ends("B5", (100, 0), (200, 50))
heading("B5", (1, 0), (1, 0))
ends("B6", (100, 300), (200, 50))
heading("B6", (1, 0), (1, 0))
ends("B7", (100, 0), (400, 0))
heading("B7", (1, 0), None)
ends("B8", (100, 0), (400, 200))
heading("B8", (1, 0), None)
ends("B10", (100, 0), (200, 50))
heading("B10", (1, 0), (1, 0))
r1, r10 = row("B1"), row("B10")
check("B10 magnitudes change the shape (length differs from B1)", r1 is not None and r10 is not None and abs(r10[8] - r1[8]) > 0.5, "%s vs %s" % (r10[8] if r10 else None, r1[8] if r1 else None))
ends("B11", (100, 0), (200, 50))
heading("B11", (-1, 0), (1, 0))
ends("B12", (100, 0), (200, 50))
heading("B12", (1, 0), (1, 0))
ends("B13", (100, 0), (300, 50))
heading("B13", (1, 0), (-1, 0))
ends("B14", (0, 0), (200, 50))
heading("B14", (-1, 0), (1, 0))
ends("B15", (100, 0), (500, 50))
heading("B15", (1, 0), (-1, 0))
ends("B16", (100, 0), (200, 50))
r16 = row("B16")
check("B16 straight: length = 111.80", r16 is not None and abs(r16[8] - 111.8) < 0.05, str(r16[8] if r16 else None))
print("%d failure(s)" % fails)
sys.exit(1 if fails else 0)
