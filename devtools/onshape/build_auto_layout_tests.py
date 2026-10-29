"""Auto layout plus tests as real features in the "Autolayout regression" Part Studio of Publish & Drawing tools
(doc 73271cfc; upsert by name). Checked by check_auto_layout_tests.py.

Fixture: a copy of the ski fixture ("Derived 1") of the user's Autolayout_Tests studio (never modified): 2 composites (a
1881.75 x 334 mm ski plate pair, a 1171.2 x 360 mm set of six thin plates), their 8 solids, and 3 mate connectors; the
origin connector is the one at world (120, 0, 0) mm. Every test COPIES the bodies, so the cases are independent.

Cases (names carry the expectation; sheets/rows are in the origin connector's frame, mm):
  T1 width 700            ski at (10,10); the 360 mm set does not fit above it -> a second column at x = 10 + 1881.75 + 10
  T2 no width limit       one Y column: the set above the ski at y = 10 + 334 + 10, x = 10
  T3 width 700, length 2000   the set fits nowhere on sheet 1 -> sheet 2, offset 1.1 x 700 = 770 in +Y: set at (10, 780)
  T4 8 solids -> composite    named "Layout test": exactly one composite is created, with the 8 solids' copies
  T5 2 composites -> composite   laid-out composites cannot be grouped: WARNING, no new composite
  T6 nesting, spread off  8 solids, width 800: the six thin plates go inside the ski plates' windows, no collisions
  T7 nesting, spread on   = T6 with spreading: same bodies nested, no collisions, some plate sits elsewhere than in T6
  T8 no nesting           = T6 without nesting: nothing is inside anything else

Not covered (checked by hand): 90 degree rotation (no fixture body is taller than wide), implicit mate connector (UI only).

usage (repo root): ONSHAPE_CALL_CAP=40 PYTHONPATH=. python devtools/onshape/build_auto_layout_tests.py
Re-run after changing auto_layout.fs (the features carry the studio's microversion).
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
import fsapi  # noqa: E402
from sync.core.client import OnshapeClient  # noqa: E402

c = OnshapeClient()
D, W = "73271cfcd708b3f5e3fc315c", "fea83d30106dba0a4e066761"
STUDIO = "Autolayout regression"
SOURCE_STUDIO = "Autolayout_Tests"
FEATURE_STUDIO = "auto_layout"
FIXTURE = os.path.join(os.path.dirname(__file__), "auto_layout_tests.json")

elements = c.list_elements(D, W)
by_name = {e["name"]: e for e in elements}
if STUDIO not in by_name:
    created = c.post(f"/api/v10/partstudios/d/{D}/w/{W}", {"name": STUDIO})
    by_name[STUDIO] = {"id": created["id"], "name": STUDIO}
    print("created Part Studio", STUDIO, created["id"])
E = by_name[STUDIO]["id"]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
fs = by_name[FEATURE_STUDIO]
NS = "e%s::m%s" % (fs["id"], fs["microversionId"])
cache = {}

# The ski fixture: copy the source studio's derive feature (parameters verbatim), named "Fixture"
source = fsapi.features(D, W, by_name[SOURCE_STUDIO]["id"])
derive = [f for f in source["features"] if f["featureType"] == "importDerived"][0]
fixture = json.loads(json.dumps(derive))
fixture.pop("featureId", None)
fixture["name"] = "Fixture"
r = fsapi.write_feature(BASE, fixture, cache, client=c)
DER = r["feature"]["featureId"]
print("%-40s %s %s" % ("Fixture", r["featureState"].get("featureStatus"), DER))

DER_ID = 'makeId("%s")' % DER
ALL = 'qCreatedBy(%s, EntityType.BODY)' % DER_ID
COMPS = 'qBodyType(%s, BodyType.COMPOSITE)' % ALL
SOLIDS = 'qBodyType(%s, BodyType.SOLID)' % ALL
ORIGIN = 'qNthElement(qBodyType(%s, BodyType.MATE_CONNECTOR), 1)' % ALL


def case(name, bodies, **kw):
    p = [fsapi.q("bodies", bodies), fsapi.q("origin", ORIGIN),
         fsapi.num("xBuffer", "10 mm"), fsapi.num("yBuffer", "10 mm"),
         fsapi.b("limitWidth", "width" in kw), fsapi.num("layoutWidth", "%s mm" % kw.get("width", 2000)),
         fsapi.b("limitLength", "length" in kw), fsapi.num("layoutLength", "%s mm" % kw.get("length", 2000)),
         fsapi.b("allowRotation", False), fsapi.b("allowNesting", kw.get("nest", False)),
         fsapi.b("spreadInHoles", kw.get("spread", False)), fsapi.b("dropToPlane", True), fsapi.b("copyBodies", True),
         fsapi.b("createComposite", kw.get("composite", False)), fsapi.s("compositeName", kw.get("title", "")),
         fsapi.b("showPreview", False)]
    feature = {"btType": "BTMFeature-134", "featureType": "autoLayoutPlus", "name": name, "namespace": NS, "parameters": p}
    res = fsapi.write_feature(BASE, feature, cache, client=c)
    fid = res["feature"]["featureId"]
    print("%-40s %s %s" % (name, res["featureState"].get("featureStatus"), fid))
    return {"name": name, "id": fid, "status": res["featureState"].get("featureStatus")}


tests = [
    case("T1 width 700: second column", COMPS, width=700),
    case("T2 no width limit: one Y column", COMPS),
    case("T3 width 700 length 2000: second sheet", COMPS, width=700, length=2000),
    case("T4 8 solids to a named composite", SOLIDS, width=1200, composite=True, title="Layout test"),
    case("T5 composites to composite: warning", COMPS, width=700, composite=True, title="Never made"),
    case("T6 nesting spread off", SOLIDS, width=800, nest=True, spread=False),
    case("T7 nesting spread on", SOLIDS, width=800, nest=True, spread=True),
    case("T8 no nesting", SOLIDS, width=800),
]
json.dump({"studio": E, "fixture": DER, "tests": tests}, open(FIXTURE, "w"), indent=2)
print("wrote", FIXTURE)
