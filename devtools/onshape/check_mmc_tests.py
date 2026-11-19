"""Check the Multi mate connector tests built by build_mmc_tests.py ("MMC tests" studio).

One features GET (statuses) and one FeatureScript eval: for each test feature, its mate connectors in creation order
(origin x in mm, Z axis, X axis). Prints PASS/FAIL per expectation. Exit code 1 on any FAIL.

usage (repo root): PYTHONPATH=. python devtools/onshape/check_mmc_tests.py
"""
import json
import re
import sys

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("multi_mate_connector/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
E = [e["id"] for e in c.list_elements(D, W) if e["name"] == "MMC tests"][0]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"

tree = c.get(f"{BASE}/features")
status = {f["name"].split()[0]: tree["featureStates"][f["featureId"]]["featureStatus"] for f in tree["features"] if f["featureType"] == "multiMateConnector"}
fids = {f["name"].split()[0]: f["featureId"] for f in tree["features"] if f["featureType"] == "multiMateConnector"}

SCRIPT = '''function(context is Context, queries) {
  var out = {};
  for (var name, fid in FIDS) {
    var rows = [];
    for (var mc in evaluateQuery(context, qCreatedBy(makeId(fid), EntityType.BODY))) {
      const cs = evMateConnector(context, { "mateConnector" : mc });
      rows = append(rows, [roundToPrecision(cs.origin[0] / millimeter, 3), roundToPrecision(cs.origin[1] / millimeter, 3), roundToPrecision(cs.origin[2] / millimeter, 3),
                           roundToPrecision(cs.zAxis[0], 4), roundToPrecision(cs.zAxis[1], 4), roundToPrecision(cs.zAxis[2], 4),
                           roundToPrecision(cs.xAxis[0], 4), roundToPrecision(cs.xAxis[1], 4), roundToPrecision(cs.xAxis[2], 4)]);
    }
    out[name] = rows;
  }
  return toString(out);
}'''.replace("FIDS", "{" + ", ".join('"%s" : "%s"' % (k, v) for k, v in fids.items()) + "}")

raw = c.post(f"{BASE}/featurescript", json_data={"script": SCRIPT})["result"]["value"]
result = json.loads(re.sub(r'(M\d+) :', r'"\1":', raw))

fails = 0


def check(label, ok, detail=""):
    global fails
    print("%s  %-62s %s" % ("PASS" if ok else "FAIL", label, detail))
    if not ok:
        fails += 1


def xs(t):
    return [r[0] for r in result.get(t, [])]


def zs(t):
    return [r[3:6] for r in result.get(t, [])]


def dots(t):
    z = zs(t)
    return [sum(a * b for a, b in zip(z[i], z[i + 1])) for i in range(len(z) - 1)]


def near(a, b, tol=0.01):
    return len(a) == len(b) and all(abs(p - q) <= tol for p, q in zip(a, b))


for t, st in status.items():
    check("%s feature status" % t, st in ("OK", "INFO"), st)

check("M1 five connectors at x 10 105 200 295 390", near(xs("M1"), [10, 105, 200, 295, 390]), str(xs("M1")))
check("M1 every Z parallel to Y", all(abs(abs(z[1]) - 1) < 1e-3 for z in zs("M1")), str(zs("M1")))
check("M1 consecutive Z dots all positive (no flips)", all(d > 0 for d in dots("M1")), str([round(d, 2) for d in dots("M1")]))
check("M1 X axes follow the curve (+X)", all(r[6] > 0.99 for r in result.get("M1", [])))
check("M2 as the faces: the fixture's normals DO flip", any(d < 0 for d in dots("M2")), str([round(d, 2) for d in dots("M2")]))
check("M3 four centered connectors", near(xs("M3"), [57.5, 152.5, 247.5, 342.5]), str(xs("M3")))
check("M4 toward the reference point: every Z has +Y", all(z[1] > 0.99 for z in zs("M4")), str(zs("M4")))
check("M5 flip: every Z opposite to M1", len(zs("M5")) == len(zs("M1")) and all(abs(a[1] + b[1] - 0) < 1e-3 and a[1] * b[1] < 0 for a, b in zip(zs("M5"), zs("M1"))))
check("M6 fixed direction: every Z = (0,0,1)", all(abs(z[2] - 1) < 1e-3 for z in zs("M6")), str(zs("M6")))
check("M7 reversed: first connector at x = 390", near(xs("M7"), [390, 295, 200, 105, 10]), str(xs("M7")))
print("%d failure(s)" % fails)
sys.exit(1 if fails else 0)
