"""Check the Mate connector + tests built by build_mcp_tests.py ("MCP tests" studio).

One features GET (statuses) and one FeatureScript eval: for each C-case its mate connectors (origin, Z, X), for C13 its
surface bodies and face areas, and the bounding box of the transformed BOX. Prints PASS/FAIL per expectation; exit 1 on FAIL.

usage (repo root): PYTHONPATH=. python devtools/onshape/check_mcp_tests.py
"""
import json
import re
import sys

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("multi_mate_connector/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
E = [e["id"] for e in c.list_elements(D, W) if e["name"] == "MCP tests"][0]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"

tree = c.get(f"{BASE}/features")
mine = [f for f in tree["features"] if f["featureType"] == "mateConnectorPlus"]
status = {f["name"].split()[0]: tree["featureStates"][f["featureId"]]["featureStatus"] for f in mine}
fids = {f["name"].split()[0]: f["featureId"] for f in mine}
box_id = [f["featureId"] for f in tree["features"] if f["name"].startswith("BOX 10 mm")][0]

SCRIPT = '''function(context is Context, queries) {
  var out = {};
  for (var name, fid in FIDS) {
    var rows = [];
    for (var mc in evaluateQuery(context, qBodyType(qCreatedBy(makeId(fid), EntityType.BODY), BodyType.MATE_CONNECTOR))) {
      const cs = evMateConnector(context, { "mateConnector" : mc });
      rows = append(rows, [roundToPrecision(cs.origin[0] / millimeter, 3), roundToPrecision(cs.origin[1] / millimeter, 3), roundToPrecision(cs.origin[2] / millimeter, 3),
                           roundToPrecision(cs.zAxis[0], 4), roundToPrecision(cs.zAxis[1], 4), roundToPrecision(cs.zAxis[2], 4),
                           roundToPrecision(cs.xAxis[0], 4), roundToPrecision(cs.xAxis[1], 4), roundToPrecision(cs.xAxis[2], 4)]);
    }
    out[name] = rows;
  }
  var sheets = [];
  for (var body in evaluateQuery(context, qBodyType(qCreatedBy(makeId(BUILDID), EntityType.BODY), BodyType.SHEET))) {
    var areas = [];
    for (var f in evaluateQuery(context, qOwnedByBody(body, EntityType.FACE))) { areas = append(areas, roundToPrecision(evArea(context, { "entities" : f }) / (millimeter ^ 2), 2)); }
    sheets = append(sheets, areas);
  }
  out["sheets"] = sheets;
  const bb = evBox3d(context, { "topology" : qCreatedBy(makeId(BOXID), EntityType.BODY) });
  out["box"] = [roundToPrecision(bb.minCorner[0] / millimeter, 3), roundToPrecision(bb.minCorner[1] / millimeter, 3), roundToPrecision(bb.minCorner[2] / millimeter, 3),
                roundToPrecision(bb.maxCorner[0] / millimeter, 3), roundToPrecision(bb.maxCorner[1] / millimeter, 3), roundToPrecision(bb.maxCorner[2] / millimeter, 3)];
  return toString(out);
}'''.replace("FIDS", "{" + ", ".join('"%s" : "%s"' % (k, v) for k, v in fids.items()) + "}").replace("BUILDID", '"%s"' % fids["C13"]).replace("BOXID", '"%s"' % box_id)

raw = c.post(f"{BASE}/featurescript", json_data={"script": SCRIPT})["result"]["value"]
result = json.loads(re.sub(r"(C\d+|sheets|box) :", r'"\1":', raw))

fails = 0


def check(label, ok, detail=""):
    global fails
    print("%s  %-66s %s" % ("PASS" if ok else "FAIL", label, detail))
    if not ok:
        fails += 1


def row(case):
    r = result.get(case) or []
    return r[0] if r else None


def close(a, b, tol=0.002):
    return a is not None and all(abs(p - q) <= tol for p, q in zip(a, b))


def frame(case, origin=None, z=None, x=None, tol=0.002):
    r = row(case)
    ok = r is not None
    if ok and origin is not None:
        ok = close(r[0:3], origin, 0.01)
    if ok and z is not None:
        ok = close(r[3:6], z, tol)
    if ok and x is not None:
        ok = close(r[6:9], x, tol)
    check(case, ok, str(r))


for case in sorted(status, key=lambda s: int(s[1:])):
    want = "ERROR" if case == "C12" else ("INFO" if case in ("C11",) else None)
    st = status[case]
    check("%s status" % case, (st == want) if want else (st in ("OK", "INFO")), st)

frame("C1", (100, 80, 0), (0, 0, 1), (1, 0, 0))
frame("C2", (100, 30, 0))
frame("C3", (35, 30, 0))
frame("C4", (150, 30, 0))
frame("C5", (50, 30, 0))
frame("C6", (100, 0, 0), (0, -1, 0))
r7 = row("C7")
check("C7 Z along X, X goes to world Y", r7 is not None and abs(abs(r7[3]) - 1) < 1e-3 and abs(abs(r7[7]) - 1) < 1e-3, str(r7))
frame("C8", (535.355, 35.355, 0), (-0.7071, -0.7071, 0))
frame("C9", (110, 100, 5), (0, 0, 1), (0, 1, 0))
frame("C10", None, (0, 0, 1), (0, 1, 0))
frame("C11", None, (1, 0, 0), (0, 1, 0))
check("C12 made no connector (X parallel to Z)", row("C12") is None)
sheets = result.get("sheets", [])
check("C13 one surface body with three faces of 400 mm2", len(sheets) == 1 and len(sheets[0]) == 3 and all(abs(a - 400) < 0.5 for a in sheets[0]), str(sheets))
check("C14 BOX moved to (100,80,0)-(110,90,10)", close(result.get("box"), (100, 80, 0, 110, 90, 10), 0.01), str(result.get("box")))
import math


def unit(v):
    return abs(math.sqrt(sum(a * a for a in v)) - 1) < 1e-3


r15 = row("C15")
check("C15 cylinder: origin on the cylinder, Z radial",
      r15 is not None and abs(math.hypot(r15[0] - 1000, r15[1]) - 100) < 0.05 and abs(abs(r15[3] * (r15[0] - 1000) / 100 + r15[4] * r15[1] / 100) - 1) < 1e-3 and abs(r15[5]) < 1e-3, str(r15))
r16 = row("C16")
ux, uy = ((r16[0] - 2000) / math.hypot(r16[0] - 2000, r16[1]), r16[1] / math.hypot(r16[0] - 2000, r16[1])) if r16 else (0, 0)
check("C16 cone: Z = 0.857 radial + 0.514 up (either sign)",
      r16 is not None and abs(abs(r16[3] * ux * 0.857 + r16[4] * uy * 0.857 + r16[5] * 0.514) - 1) < 0.01, str(r16))
r17 = row("C17")
check("C17 B-spline wall: unit Z, horizontal", r17 is not None and unit(r17[3:6]) and abs(r17[5]) < 1e-3, str(r17))
r18, r19 = row("C18"), row("C19")
check("C18 lofted B-spline surface: unit Z", r18 is not None and unit(r18[3:6]), str(r18))
check("C19 tangent with a hint: unit Z square to C18's normal",
      r19 is not None and r18 is not None and unit(r19[3:6]) and abs(sum(a * b for a, b in zip(r18[3:6], r19[3:6]))) < 0.01, str(r19))
print("%d failure(s)" % fails)
sys.exit(1 if fails else 0)
