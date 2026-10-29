"""Check the Auto layout plus tests built by build_auto_layout_tests.py ("Autolayout regression" studio).

One FeatureScript eval measures, in the origin connector's frame, every body each test created (bounding boxes, kind,
composite names) plus pairwise collisions for the nesting cases; the feature statuses come from one features GET.
Prints PASS/FAIL per expectation; exit code 1 on any FAIL. Two API calls in all.

usage (repo root): ONSHAPE_CALL_CAP=10 PYTHONPATH=. python devtools/onshape/check_auto_layout_tests.py
"""
import json
import os
import sys

from sync.core.client import OnshapeClient

c = OnshapeClient()
D, W = "73271cfcd708b3f5e3fc315c", "fea83d30106dba0a4e066761"
FIXTURE = json.load(open(os.path.join(os.path.dirname(__file__), "auto_layout_tests.json")))
E = FIXTURE["studio"]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TESTS = {t["name"].split(" ")[0]: t for t in FIXTURE["tests"]}

# [label, feature id, collisions wanted]; ORIG = the fixture's own bodies
SETS = [["ORIG", FIXTURE["fixture"], False]] + [[k, t["id"], k in ("T6", "T7", "T8")] for k, t in TESTS.items()]

SCRIPT = '''function(context is Context, queries)
{
  var der = makeId("DERIVE");
  var mc = qNthElement(qBodyType(qCreatedBy(der, EntityType.BODY), BodyType.MATE_CONNECTOR), 1);
  var cs = evMateConnector(context, { "mateConnector" : mc });
  var out = [{ "label" : "origin", "origin" : cs.origin / millimeter }];
  for (var s in SETS)
  {
    var rows = [];
    var solids = [];
    var made = s[0] == "ORIG" ? qCreatedBy(der, EntityType.BODY) : qCreatedBy(makeId(s[1]), EntityType.BODY);
    for (var b in evaluateQuery(context, made))
    {
      var kind = "OTHER";
      if (size(evaluateQuery(context, qBodyType(b, BodyType.COMPOSITE))) > 0) { kind = "COMP"; }
      else if (size(evaluateQuery(context, qBodyType(b, BodyType.SOLID))) > 0) { kind = "SOLID"; solids = append(solids, b); }
      else if (size(evaluateQuery(context, qBodyType(b, BodyType.MATE_CONNECTOR))) > 0) { kind = "MC"; }
      if (kind == "COMP" || kind == "SOLID")
      {
        const bb = evBox3d(context, { "topology" : b, "cSys" : cs });
        rows = append(rows, { "kind" : kind, "min" : bb.minCorner / millimeter, "max" : bb.maxCorner / millimeter,
            "name" : getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }) });
      }
    }
    var collisions = 0;
    if (s[2])
    {
      for (var i = 0; i < size(solids); i += 1)
      {
        for (var j = i + 1; j < size(solids); j += 1)
        {
          if (size(evCollision(context, { "tools" : solids[i], "targets" : solids[j] })) > 0) { collisions += 1; }
        }
      }
    }
    out = append(out, { "label" : s[0], "rows" : rows, "collisions" : collisions });
  }
  return out;
}'''.replace("DERIVE", FIXTURE["fixture"]).replace("SETS", json.dumps(SETS))


def fs_value(v):
    """Unwrap the eval API's typed value tree into plain Python."""
    if v is None:
        return None
    bt = v.get("btType", "")
    if bt.endswith("BTFSValueMap"):
        return {e["key"]["value"]: fs_value(e["value"]) for e in v["value"]}
    if bt.endswith("BTFSValueArray"):
        return [fs_value(x) for x in v["value"]]
    return v.get("value")


features = c.get(f"{BASE}/features")
status = {f["featureId"]: features["featureStates"][f["featureId"]]["featureStatus"] for f in features["features"]}
reply = c.post(f"{BASE}/featurescript", json_data={"script": SCRIPT})
for n in reply.get("notices", []):
    if n["level"] in ("ERROR", "WARNING"):
        print("EVAL", n["level"], n["message"])
data = {row["label"]: row for row in fs_value(reply["result"])}

fails = 0


def check(label, ok, detail=""):
    global fails
    print(("PASS  " if ok else "FAIL  ") + label + ("" if ok else "   " + detail))
    if not ok:
        fails += 1


def near(a, b, tol=0.6):
    return all(abs(x - y) <= tol for x, y in zip(a, b))


def comps(label):
    return sorted([r for r in data[label]["rows"] if r["kind"] == "COMP"], key=lambda r: (r["min"][0], r["min"][1]))


def solids(label):
    return [r for r in data[label]["rows"] if r["kind"] == "SOLID"]


def inside(a, b):
    """Is body a's box inside body b's box (a is the smaller)?"""
    return all(a["min"][i] >= b["min"][i] - 0.01 and a["max"][i] <= b["max"][i] + 0.01 for i in (0, 1))


check("fixture origin connector is world (120, 0, 0) mm", near(data["origin"]["origin"], [120, 0, 0], 0.01), str(data["origin"]["origin"]))
orig = sorted(comps("ORIG"), key=lambda r: -(r["max"][0] - r["min"][0]))
ski, plates = orig[0], orig[1]
wA, hA = ski["max"][0] - ski["min"][0], ski["max"][1] - ski["min"][1]
wB, hB = plates["max"][0] - plates["min"][0], plates["max"][1] - plates["min"][1]
check("fixture sizes: ski %.2f x %.2f, plate set %.2f x %.2f" % (wA, hA, wB, hB),
      abs(wA - 1881.75) < 0.1 and abs(hA - 334) < 0.1 and abs(wB - 1171.2) < 0.2 and abs(hB - 360) < 0.1)

# T1: width 700 -> ski at (10,10); the set starts a second column
t1 = {round(r["max"][0] - r["min"][0]): r for r in comps("T1")}
a, b = t1[round(wA)], t1[round(wB)]
check("T1 ski at (10, 10)", near(a["min"][:2], [10, 10]), str(a["min"]))
check("T1 plate set in a second column at x = 10 + ski + 10, y = 10", near(b["min"][:2], [10 + wA + 10, 10]), str(b["min"]))

# T2: no width limit -> one column along Y
t2 = {round(r["max"][0] - r["min"][0]): r for r in comps("T2")}
check("T2 ski at (10, 10)", near(t2[round(wA)]["min"][:2], [10, 10]))
check("T2 plate set above the ski at (10, 10 + ski + 10)", near(t2[round(wB)]["min"][:2], [10, 10 + hA + 10]), str(t2[round(wB)]["min"]))

# T3: width 700, length 2000 -> the set goes to sheet 2, offset 1.1 x 700 in Y
t3 = {round(r["max"][0] - r["min"][0]): r for r in comps("T3")}
check("T3 ski on sheet 1 at (10, 10)", near(t3[round(wA)]["min"][:2], [10, 10]))
check("T3 plate set on sheet 2 at (10, 770 + 10)", near(t3[round(wB)]["min"][:2], [10, 780]), str(t3[round(wB)]["min"]))

# T4: eight solids -> one named composite
made = data["T4"]["rows"]
check("T4 eight solids and one composite", len(solids("T4")) == 8 and len(comps("T4")) == 1, "%d solids, %d composites" % (len(solids("T4")), len(comps("T4"))))
check("T4 composite is named 'Layout test'", [r["name"] for r in comps("T4")] == ["Layout test"], str([r["name"] for r in comps("T4")]))

# T5: composites cannot be grouped
check("T5 two laid-out composites, no new composite", len(comps("T5")) == 2 and len([r for r in comps("T5") if r["name"] == "Never made"]) == 0)
check("T5 status is WARNING", status[TESTS["T5"]["id"]] == "WARNING", status[TESTS["T5"]["id"]])

# T6 / T7 / T8: nesting
for label in ("T6", "T7", "T8"):
    check("%s eight solids, no collisions" % label, len(solids(label)) == 8 and data[label]["collisions"] == 0,
          "%d solids, %d collisions" % (len(solids(label)), data[label]["collisions"]))
nested = {}
for label in ("T6", "T7", "T8"):
    s = solids(label)
    nested[label] = [i for i, x in enumerate(s) for j, y in enumerate(s) if i != j and inside(x, y)
                     and (y["max"][0] - y["min"][0]) * (y["max"][1] - y["min"][1]) > (x["max"][0] - x["min"][0]) * (x["max"][1] - x["min"][1])]
check("T6 at least five plates sit inside another plate's box", len(set(nested["T6"])) >= 5, str(sorted(set(nested["T6"]))))
check("T7 at least five plates sit inside another plate's box", len(set(nested["T7"])) >= 5, str(sorted(set(nested["T7"]))))
check("T8 (nesting off) nothing sits inside another body", len(nested["T8"]) == 0, str(nested["T8"]))
moved = max(abs(p["min"][1] - q["min"][1]) for p, q in zip(solids("T6"), solids("T7")))
check("T7 spreading moves some plate (largest y shift %.1f mm > 1)" % moved, moved > 1.0)
extent = lambda label: max(r["max"][0] for r in solids(label))
check("T6 is shorter along X than T8 (%.0f < %.0f mm)" % (extent("T6"), extent("T8")), extent("T6") < extent("T8"))

expected = {"T1": "OK", "T2": "OK", "T3": "INFO", "T4": "OK", "T5": "WARNING", "T6": "INFO", "T7": "INFO", "T8": "OK"}
for label, want in expected.items():
    got = status[TESTS[label]["id"]]
    check("%s status %s" % (label, want), got == want, got)

print("\n%d FAIL" % fails if fails else "\nall PASS")
sys.exit(1 if fails else 0)
