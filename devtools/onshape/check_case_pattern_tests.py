"""Check the Case Pattern v2 tests built by build_case_pattern_tests.py ("Case pattern tests" studio).

Reads feature statuses over REST and the geometry, part names and published output variables through
the FeatureScript eval API, then prints PASS/FAIL per expectation. Exit code 1 on any FAIL.

usage (repo root): PYTHONPATH=. python devtools/onshape/check_case_pattern_tests.py
"""
import json
import sys

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("case_pattern/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
E = [e for e in c.list_elements(D, W) if e["name"] == "Case pattern tests"][0]["id"]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"

OUTPUTS = ["A_bossFaces", "B_bossFaces", "C_bossFaces", "A_stud", "B_stud", "D_stud"]
SCRIPT = '''function(context is Context, queries) {
  var bodies = [];
  for (var b in evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.SOLID))) {
    const bb = evBox3d(context, { "topology" : b });
    const faces = qOwnedByBody(b, EntityType.FACE);
    const blends = size(evaluateQuery(context, qUnion([qGeometry(faces, GeometryType.CYLINDER), qGeometry(faces, GeometryType.TORUS)])));
    bodies = append(bodies, [bb.minCorner[0] / millimeter, bb.maxCorner[0] / millimeter, bb.minCorner[1] / millimeter,
        bb.maxCorner[1] / millimeter, bb.minCorner[2] / millimeter, bb.maxCorner[2] / millimeter, blends,
        getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME })]);
  }
  var outputs = {};
  for (var n in NAMES) {
    const q = try silent(getQueryVariable(context, n));
    if (q == undefined) { outputs[n] = "unset"; continue; }
    const owners = evaluateQuery(context, qUnion([qOwnerBody(q), qEntityFilter(q, EntityType.BODY)]));
    var names = [];
    for (var o in owners) { names = append(names, getProperty(context, { "entity" : o, "propertyType" : PropertyType.NAME })); }
    outputs[n] = size(evaluateQuery(context, q)) ~ " on " ~ join(names, ",");
  }
  return toString({ "bodies" : bodies, "outputs" : outputs });
}'''.replace("NAMES", json.dumps(OUTPUTS))


def fs_value(v):
    """Unwrap the eval API's typed value tree into plain Python."""
    if isinstance(v, dict):
        if "value" in v and v.get("btType", "").endswith(("BTFSValueString", "BTFSValueNumber", "BTFSValueBoolean")):
            return v["value"]
        if "value" in v:
            return fs_value(v["value"])
    if isinstance(v, list):
        return [fs_value(x) for x in v]
    return v


features = c.get(f"{BASE}/features")
status = {f["name"]: features["featureStates"][f["featureId"]]["featureStatus"] for f in features["features"]}
raw = c.post(f"{BASE}/featurescript", json_data={"script": SCRIPT})["result"]["value"]

# toString of the map, parsed loosely: bodies "[ x0 , x1 , ... , name ]" and outputs "name : text".
import re
bodies = []
for m in re.finditer(r"\[ (-?[\d.e+-]+) , (-?[\d.e+-]+) , (-?[\d.e+-]+) , (-?[\d.e+-]+) , (-?[\d.e+-]+) , (-?[\d.e+-]+) , (\d+) , ([^\]]+?) \]", raw):
    g = m.groups()
    bodies.append({"x0": float(g[0]), "x1": float(g[1]), "y0": float(g[2]), "y1": float(g[3]), "z0": float(g[4]),
                   "z1": float(g[5]), "blends": int(g[6]), "name": g[7].strip()})
outputs = {}
for n in OUTPUTS:
    m = re.search(re.escape(n) + r" : ([^,}]+(?:,[^:}]+)*?)(?= , [A-Z]_|\s*\})", raw)
    outputs[n] = m.group(1).strip() if m else "?"

fails = 0


def check(label, ok, detail=""):
    global fails
    print("%s  %-70s %s" % ("PASS" if ok else "FAIL", label, detail))
    if not ok:
        fails += 1


def near(a, b, tol=0.3):
    return abs(a - b) <= tol


def row(y):
    """Bodies whose y-centre is within 60 mm of row y."""
    return [b for b in bodies if abs((b["y0"] + b["y1"]) / 2 - y) < 60]


def at_x(bs, x):
    return [b for b in bs if b["x0"] - 1 <= x <= b["x1"] + 1]


def status_of(prefix):
    return [v for k, v in status.items() if k.startswith(prefix)]


# ---- statuses ----
for prefix in ["T1 Case pattern", "T2 Case pattern", "T6 Case pattern", "T7 Case pattern", "T8 Case pattern"]:
    st = status_of(prefix)
    check(prefix + " statuses OK/INFO", st and all(s in ("OK", "INFO") for s in st), str(st))
check("T3 Case pattern status ERROR (clicked in-list edge)", status_of("T3 Case pattern") == ["ERROR"], str(status_of("T3 Case pattern")))
check("T11 Case pattern status ERROR (value left empty)", status_of("T11 Case pattern") == ["ERROR"], str(status_of("T11 Case pattern")))
check("T9a Case pattern status ERROR (reference clicked inside the body)", status_of("T9a Case pattern") == ["ERROR"], str(status_of("T9a Case pattern")))
st12 = status_of("T12 Case pattern")
check("T12 Case pattern status OK/INFO", st12 and all(x in ("OK", "INFO") for x in st12), str(st12))
for prefix in ["T9b Case pattern", "T10 Case pattern"]:
    st = status_of(prefix)
    check(prefix + " status OK/INFO", st and all(x in ("OK", "INFO") for x in st), str(st))

# ---- T1 ----
r1 = row(0)
boss_b = [b for b in r1 if b["name"] == "B_bossFaces"]
boss_c = [b for b in r1 if b["name"] == "C_bossFaces"]
check("T1 boss B (part B_bossFaces) exists, 25 mm tall (z 20..45)", len(boss_b) == 1 and near(boss_b[0]["z0"], 20) and near(boss_b[0]["z1"], 45),
      str(boss_b))
check("T1 boss B edges filleted (12 blends)", len(boss_b) == 1 and boss_b[0]["blends"] == 12, str(boss_b and boss_b[0]["blends"]))
check("T1 boss C (part C_bossFaces) exists, 8 mm tall (z 20..28)", len(boss_c) == 1 and near(boss_c[0]["z0"], 20) and near(boss_c[0]["z1"], 28), str(boss_c))
check("T1 boss C edges filleted (15 blends)", len(boss_c) == 1 and boss_c[0]["blends"] == 15, str(boss_c and boss_c[0]["blends"]))
blocks_b = [b for b in at_x(r1, 200) if near(b["z1"], 20)]
blocks_c = [b for b in at_x(r1, 400) if near(b["z1"], 20)]
check("T1 block B rim filleted (outside-list edit, 4 blends)", len(blocks_b) == 1 and blocks_b[0]["blends"] == 4, str(blocks_b))
check("T1 block C rim filleted (outside-list edit, 5 blends)", len(blocks_c) == 1 and blocks_c[0]["blends"] == 5, str(blocks_c))
for case, owner in (("A", None), ("B", "B_bossFaces"), ("C", "C_bossFaces")):
    text = outputs["%s_bossFaces" % case]
    ok = text not in ("unset", "?") and not text.startswith("0 ") and (owner is None or text.endswith("on " + owner))
    check("T1 #%s_bossFaces published on case %s's boss" % (case, case), ok, text)

# ---- T2 ----
r2 = row(100)
b2 = [b for b in at_x(r2, 200) if near(b["z1"], 20)]
c2 = [b for b in at_x(r2, 400) if near(b["z1"], 20)]
check("T2 block B +X face moved 5 mm (x max 250)", len(b2) == 1 and near(b2[0]["x1"], 250), str(b2))
check("T2 block C 54 deg face moved (x max > 433.4)", len(c2) == 1 and c2[0]["x1"] > 433.4, str(c2))

# ---- T6 ----
r6 = row(300)
for x, case, pin in ((0, "A", True), (200, "B", False), (400, "C", True)):
    bs = at_x(r6, x)
    check("T6 case %s post (z -5..0)" % case, any(near(b["z0"], -5) and near(b["z1"], 0) for b in bs))
    has_pin = any(near(b["z0"], -12) for b in bs)
    check("T6 case %s pin %s (#pin %s)" % (case, "present" if pin else "absent", str(pin).lower()), has_pin == pin)

# ---- T7 ----
r7 = row(400)
b7 = at_x(r7, 200)
check("T7 case B stud on block B (z 20..30)", any(near(b["z0"], 20) and near(b["z1"], 30) for b in b7))
check("T7 case D stud on B's stud (z 30..40, chained)", any(near(b["z0"], 30) and near(b["z1"], 40) for b in b7))
for n in ("A_stud", "B_stud", "D_stud"):
    check("T7 #%s = one body" % n, outputs[n].startswith("1 "), outputs[n])

# ---- T8 ----
r8 = row(500)
a8 = [b for b in at_x(r8, 0) if near(b["z0"], 20) and near(b["z1"], 30)]
b8 = [b for b in at_x(r8, 200) if near(b["z0"], 20) and near(b["z1"], 30)]
c8 = [b for b in at_x(r8, 400) if near(b["z0"], 20) and near(b["z1"], 30)]
check("T8 names ON: case 1 part named after its output (A_stud)", len(a8) >= 1 and "A_stud" in [b["name"] for b in a8], str([b["name"] for b in a8]))
check("T8 names ON: case B part named after its output (B_stud)", len(b8) == 1 and b8[0]["name"] == "B_stud", str(b8 and b8[0]["name"]))
check("T8 names OFF: case C part keeps a default name", len(c8) == 1 and c8[0]["name"].startswith("Part "), str(c8 and c8[0]["name"]))

# ---- T9b / T10: offset sheets (Offset+ toward a reference at -X) ----
SHEETS = '''function(context is Context, queries) {
  var out = [];
  for (var b in evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.SHEET))) {
    const bb = evBox3d(context, { "topology" : b, "tight" : true });
    out = append(out, [bb.minCorner[0] / millimeter, bb.maxCorner[0] / millimeter, bb.minCorner[1] / millimeter, bb.maxCorner[1] / millimeter]);
  }
  return toString(out);
}'''
raw_sheets = c.post(f"{BASE}/featurescript", json_data={"script": SHEETS})["result"]["value"]
sheets = [tuple(float(v) for v in m.groups())
          for m in re.finditer(r"\[ (-?[\d.e+-]+) , (-?[\d.e+-]+) , (-?[\d.e+-]+) , (-?[\d.e+-]+) \]", raw_sheets)]


def sheet_near(x0, x1, y_centre):
    return any(near(a, x0, 0.05) and near(b, x1, 0.05) and abs((y0 + y1) / 2 - y_centre) < 60 for a, b, y0, y1 in sheets)


check("T9b case C 54 deg face offset toward the reference (x 398.82..432.11)", sheet_near(398.82, 432.11, 600))
check("T10 case B +X face offset toward the shared reference (x 243)", sheet_near(243, 243, 700))
check("T10 case C 54 deg face offset toward the shared reference (x 398.82..432.11)", sheet_near(398.82, 432.11, 700))

# ---- T12: Move face+ with a shared reference shrinks both blocks toward -X ----
r12 = row(900)
a12 = [b for b in at_x(r12, 0) if near(b["z1"], 20)]
b12 = [b for b in at_x(r12, 200) if near(b["z1"], 20)]
c12 = [b for b in at_x(r12, 400) if near(b["z1"], 20)]
check("T12 case A (tree) +X face moved toward the reference (x max 28)", len(a12) == 1 and near(a12[0]["x1"], 28), str(a12 and a12[0]["x1"]))
check("T12 case B +X face moved toward the reference (x max 243)", len(b12) == 1 and near(b12[0]["x1"], 243), str(b12 and b12[0]["x1"]))
check("T12 case C 54 deg face moved toward the reference (x max < 433.2)", len(c12) == 1 and c12[0]["x1"] < 433.2, str(c12 and c12[0]["x1"]))

print("\n%d failure(s)" % fails)
sys.exit(1 if fails else 0)
