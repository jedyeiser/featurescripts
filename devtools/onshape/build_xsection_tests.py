"""Build the EI and Cross Section (eiXSect) test cases as real features in the xSection document's
"xSection tests" Part Studio (created when missing; upsert by name). Fixtures are native features only
(sketches, extrudes, composite curves, mate connectors). Checked by check_xsection_tests.py.

Every beam is sketched on Right (sketch x = world Y, sketch y = world Z) and extruded symmetrically along X,
so its section is the same at every station. The path of each case is a composite-curve wire of one sketch
line on Top (z = 0) at the case's y; the feature measures heights from the path, so the neutral axis is the
height above z = 0. Cases sit 300 mm apart in Y. Materials are OVERRIDES (Provide material data), never the
library CSV: isotropic E (nu fixed at 0.33 by the feature) or orthotropic E1, E2, G12, nu12 -- in MPa.

The editing logic that fills the "Bodies" array does not run through the REST API, so the builder writes
each array item itself (every item parameter, correction 38) with "Material name" = "Not assigned", which
keeps the CSV out of the lookup.

  X1  EI_iso_rect_100x10 (E 10 GPa)          -> EI 83.33 (beam; 93.52 on the plate Q11 basis), NA 5, GJ 125.31
  X2  ortho_rect_100x10 E1 20, E2 10 GPa, G12 1.5 GPa, nu12 0.45 -> EI 166.67 (Q11 basis 185.44), GJ 50.00
  X3  GJ_plate_100x2 E1 = E2 20 GPa, G12 12 GPa, nu12 0.45 -> GJ 3.200 (old (Q11-Q12)/2 rule 1.839), EI 1.333
  X4  stepped 50x10 + 50x4, one body          -> EI 57.19, NA 4.143, GJ 86.00 (global-centroid formula; strip-wise 66.67)
  X5  hollow 100x20, walls 10 (sides) / 5     -> EI 600.0, NA 10, area 1200 (GJ reported: holes are not subtracted)
  X6  circle r5 centred z 5                   -> area 78.54 within 0.5 %, EI 4.909, NA 5
  X7  bimetal 100x4 E 70 GPa under 100x6 E 10 GPa, two bodies -> NA 2.882, EI 178.86, GJ 268.97
  X8  two touching 100x5 bodies, E 10 GPa, composites on -> same as X1: EI 83.33, NA 5, area 1000
  X9  X1 with the path drawn backwards        -> EI 83.33 at every station, stations at the same x
  X10 FCP (mate connector, x 100) > ACP (sketch point, x -100) -> 7 stations ascending -150..150, EI 83.33, beam analysis
  X11 path ends exactly on the beam's end faces (tip grazing) -> no WARNING, interior EI 83.33
  X12 X1 plus a 100x5 IGNORE body on top      -> EI 83.33, NA 5 (the ignored body adds nothing)
  B1  Generate baseline FCP -800 < ACP 700, B2 its mirror (FCP 800 > ACP -700) -> B2 = mirror of B1 (baseline,
      weighted baseline, measurement sketch), FRCP / ARCP at the camber/rocker joins
  D1-D4 Estimate Deflection reaction moment: D2 (FCP picked, tip +X) = D1 (no FCP); D3 (mirrored ski, FCP picked) =
      mirror of D1; D4 (mirrored, no FCP) is not (the old moment always acts about +Y)

usage (repo root): PYTHONPATH=. python devtools/onshape/build_xsection_tests.py
       XS_ONLY="D" ... builds/updates only features whose name starts with D (others reused as they are)
"""
import copy
import json
import math
import os

from sync.core.client import OnshapeClient
from devtools.onshape.fsapi import write_feature, delete_feature, cached_tree  # noqa: E402  (API budget: one feature-tree GET per studio)

c = OnshapeClient()
DOC = json.load(open("xSection/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
STUDIO = "xSection tests"
ELEMENTS = c.list_elements(D, W)
studios = {e["name"]: e["id"] for e in ELEMENTS if e["elementType"] == "PARTSTUDIO"}
if STUDIO not in studios:
    studios[STUDIO] = c.post(f"/api/v10/partstudios/d/{D}/w/{W}", {"name": STUDIO})["id"]
E = studios[STUDIO]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TAB = [e for e in ELEMENTS if e["name"] == "xSect" and e["elementType"] == "FEATURESTUDIO"][0]
SPEC = [x for x in c.get(f"/api/v10/featurestudios/d/{D}/w/{W}/e/{TAB['id']}/featurespecs")["featureSpecs"]
        if x["featureType"] == "eiXSect"][0]
# Correction 38: custom enums (MaterialBehavior, MaterialType, ...) carry the namespace of the feature's studio.
NS = SPEC.get("namespace") or "e%s::m%s" % (TAB["id"], TAB["microversionId"])
ITEM_SPEC = [p for p in SPEC["parameters"] if p["parameterId"] == "bodyArray"][0]["parameters"]


def material_csv():
    """The Material library reference of an existing EI feature in this document (never matched: every body is
    'Not assigned'), so the TableData parameter is a real one; the spec's empty default otherwise."""
    for name in ("ROY_Test", "SKH_Trials", "Test"):
        if name not in studios:
            continue
        feats = c.get(f"/api/v10/partstudios/d/{D}/w/{W}/e/{studios[name]}/features")["features"]
        for f in feats:
            if f.get("featureType") == "eiXSect":
                p = copy.deepcopy([x for x in f["parameters"] if x["parameterId"] == "materialCSV"][0])
                p.pop("nodeId", None)
                print("Material library parameter copied from %s / %s" % (name, f["name"]))
                return p
    print("no existing EI feature: Material library left at the spec default (empty)")
    return None


MATERIAL_CSV = material_csv()

STATE = {"features": None}


def q(pid, *exprs):
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "query=%s;" % e} for e in exprs]}


def en(pid, enum, value, ns=""):
    return {"btType": "BTMParameterEnum-145", "parameterId": pid, "enumName": enum, "value": value, "namespace": ns}


def num(pid, expr, integer=False):
    return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": expr, "isInteger": integer}


def b(pid, v):
    return {"btType": "BTMParameterBoolean-144", "parameterId": pid, "value": v}


def s(pid, v):
    return {"btType": "BTMParameterString-149", "parameterId": pid, "value": v}


ONLY = [p for p in os.environ.get("XS_ONLY", "").split(",") if p]


_TREE = {}   # feature tree of the test Part Studio, kept in step with each write (API budget)


def upsert(feature):
    f = cached_tree(BASE, _TREE, c)
    existing = [x for x in f["features"] if x["name"] == feature["name"]]
    if ONLY and existing and not any(feature["name"].startswith(p) for p in ONLY):
        # XS_ONLY="D,B": leave every other existing feature untouched (reuse its id as a fixture).
        return existing[0]["featureId"]
    r = write_feature(BASE, feature, _TREE, client=c)
    print("%-110s %s" % (feature["name"][:110], r.get("featureState", {}).get("featureStatus")))
    return r["feature"]["featureId"]

def feature(name, ftype, params, ns=""):
    f = {"btType": "BTMFeature-134", "featureType": ftype, "name": name, "parameters": params}
    if ns:
        f["namespace"] = ns
    return upsert(f)


# ---- sketch geometry (metres) ----
def seg(eid, x0, y0, x1, y1):
    L = math.hypot(x1 - x0, y1 - y0)
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": 0.0, "endParam": L, "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryLine-117", "pntX": x0, "pntY": y0, "dirX": (x1 - x0) / L, "dirY": (y1 - y0) / L}}


def circle(eid, cx, cy, r):
    return {"btType": "BTMSketchCurve-4", "entityId": eid, "centerId": eid + ".center", "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryCircle-115", "radius": r, "xCenter": cx, "yCenter": cy, "xDir": 1.0, "yDir": 0.0, "clockwise": False}}


def point(eid, x, y):
    return {"btType": "BTMSketchPoint-158", "entityId": eid, "isConstruction": False, "x": x, "y": y}


def mmv(*xs):
    return [x / 1000.0 for x in xs]


def polyline(prefix, pts, closed=False):
    n = len(pts) if closed else len(pts) - 1
    return [seg("%s%d" % (prefix, i), *mmv(pts[i][0], pts[i][1], pts[(i + 1) % len(pts)][0], pts[(i + 1) % len(pts)][1])) for i in range(n)]


def rect(prefix, y0, y1, z0, z1):
    """Rectangle on Right: sketch x = world Y, sketch y = world Z."""
    return polyline(prefix, [(y0, z0), (y1, z0), (y1, z1), (y0, z1)], closed=True)


def sketch(name, plane_expr, entities):
    return upsert({"btType": "BTMSketch-151", "featureType": "newSketch", "name": name,
                   "parameters": [q("sketchPlane", plane_expr)], "entities": entities, "constraints": []})


TOP = 'qCreatedBy(makeId("Top"), EntityType.FACE)'
RIGHT = 'qCreatedBy(makeId("Right"), EntityType.FACE)'


def edges(fid):
    return 'qConstructionFilter(qOwnedByBody(qBodyType(qCreatedBy(makeId("%s"), EntityType.BODY), BodyType.WIRE), EntityType.EDGE), ConstructionObject.NO)' % fid


def body(fid):
    return 'qCreatedBy(makeId("%s"), EntityType.BODY)' % fid


def extrude(name, entities, length):
    """Solid from sketch regions on Right, symmetric about x = 0: x from -length/2 to +length/2."""
    return feature(name, "extrude", [
        en("bodyType", "ExtendedToolBodyType", "SOLID"), en("operationType", "NewBodyOperationType", "NEW"),
        q("entities", entities), en("endBound", "BoundingType", "BLIND"),
        num("depth", "%g mm" % length), b("symmetric", True)])


def beam(name, section, length=220, region=None):
    """A beam of `section` (Right-plane sketch entities), `length` long in X, centred on x = 0.
    `region` (y, z) picks one sketch region (e.g. the ring of a hollow section) instead of all of them."""
    sk = sketch(name + " (sketch)", RIGHT, section)
    regions = 'qSketchRegion(makeId("%s"))' % sk
    if region is not None:
        regions = 'qContainsPoint(%s, vector(0, %g, %g) * millimeter)' % (regions, region[0], region[1])
    return extrude(name, regions, length)


def path(name, y, x0=-100, x1=100):
    """Composite-curve wire of one line on Top from (x0, y) to (x1, y)."""
    sk = sketch(name + " (sketch)", TOP, polyline("l", [(x0, y), (x1, y)]))
    return feature(name, "compositeCurve", [q("edges", edges(sk))])


def connector(name, x, y):
    sk = sketch(name + " (sketch)", TOP, [point("p", *mmv(x, y))])
    return feature(name, "mateConnector", [
        en("originType", "OriginCreationType", "ON_ENTITY"), q("originQuery", 'qCreatedBy(makeId("%s"), EntityType.VERTEX)' % sk),
        en("entityInferenceType", "EntityInferenceType", "POINT"), b("allowOwnerEntity", True), b("requireOwnerPart", False)])


# ---- materials (MPa, kg/m3) ----
def iso(E, density=1000):
    return {"materialBehavior": "PROVIDE_DATA", "materialType": "ISOTROPIC", "youngsModulus": E, "overrideDensity": density}


def ortho(E1, E2, G12, nu12, density=1000):
    return {"materialBehavior": "PROVIDE_DATA", "materialType": "ORTHOTROPIC", "E1": E1, "E2": E2, "G12": G12, "nu12": nu12,
            "overrideDensity": density}


IGNORE = {"materialBehavior": "IGNORE"}


# ---- the feature under test ----
def body_item(n, fid, label, mat):
    """One 'Bodies' array item, as the editing logic would write it, carrying every item parameter."""
    given = {"bodyQuery": q("bodyQuery", body(fid)), "bodyName": s("bodyName", label), "bodyNum": num("bodyNum", str(n), True),
             "hasMaterialData": b("hasMaterialData", False), "materialName": s("materialName", "Not assigned"),
             "overrideName": s("overrideName", "%s override" % label)}
    for key in ("materialBehavior", "materialType"):
        if key in mat:
            given[key] = en(key, "MaterialBehavior" if key == "materialBehavior" else "MaterialType", mat[key], NS)
    for key in ("overrideDensity", "youngsModulus", "E1", "E2", "G12", "nu12"):
        if key in mat:
            given[key] = num(key, repr(float(mat[key])))
    params = []
    for p in ITEM_SPEC:
        pid = p["parameterId"]
        if pid in given:
            params.append(given[pid])
        elif isinstance(p.get("defaultValue"), dict):
            d = copy.deepcopy(p["defaultValue"])
            d.pop("nodeId", None)
            params.append(dict(d, parameterId=pid))
    return {"btType": "BTMArrayParameterItem-1843", "parameters": params}


def xsect(name, wire, bodies, sections=5, fcp=None, acp=None, composites=False):
    """An EI and Cross Section instance. `bodies` = [(fixture feature id, label, material)]; parameters not
    given take the spec defaults (correction 38)."""
    given = {"xSectAlong": q("xSectAlong", body(wire)),
             "selBodies": q("selBodies", *[body(f) for f, _, _ in bodies]),
             "fcpQuery": q("fcpQuery", *([fcp] if fcp else [])),
             "acpQuery": q("acpQuery", *([acp] if acp else [])),
             "numSections": num("numSections", str(sections), True),
             "createComposites": b("createComposites", composites),
             "analysisName": s("analysisName", name.split(" ")[0])}
    if MATERIAL_CSV is not None:
        given["materialCSV"] = MATERIAL_CSV
    params = []
    for p in SPEC["parameters"]:
        pid = p["parameterId"]
        d = p.get("defaultValue")
        if pid == "bodyArray":
            params.append({"btType": "BTMParameterArray-2025", "parameterId": pid,
                           "items": [body_item(i + 1, f, label, mat) for i, (f, label, mat) in enumerate(bodies)]})
        elif pid in given:
            params.append(given[pid])
        elif isinstance(d, dict):
            d = copy.deepcopy(d)
            d.pop("nodeId", None)
            params.append(dict(d, parameterId=pid))
    return feature(name, "eiXSect", params, NS)


def case_y(k):
    return 300.0 * k


ISO10 = iso(10000)

# X1 isotropic rectangle 100 x 10
y = case_y(1)
b1 = beam("X1 beam 100x10 z 0..10", rect("r", y - 50, y + 50, 0, 10))
p1 = path("X1 path x -100..100", y)
xsect("X1 EI_iso_rect_100x10 E 10 GPa -> EI 83.33 (plate 93.52), NA 5, GJ 125.31", p1, [(b1, "X1 beam", ISO10)])

# X2 orthotropic: E1 != Q11 (beam basis)
y = case_y(2)
b2 = beam("X2 beam 100x10 z 0..10", rect("r", y - 50, y + 50, 0, 10))
p2 = path("X2 path x -100..100", y)
xsect("X2 ortho_rect_100x10 E1 20 E2 10 G12 1.5 GPa nu12 0.45 -> EI 166.67 (Q11 185.44), GJ 50.00", p2,
      [(b2, "X2 beam", ortho(20000, 10000, 1500, 0.45))])

# X3 thin plate GJ with G = Q66 = G12 (the old (Q11 - Q12)/2 rule gave 1.839)
y = case_y(3)
b3 = beam("X3 plate 100x2 z 0..2", rect("r", y - 50, y + 50, 0, 2))
p3 = path("X3 path x -100..100", y)
xsect("X3 GJ_plate_100x2 E1 = E2 20 G12 12 GPa nu12 0.45 -> GJ 3.200 (old rule 1.839), EI 1.333", p3,
      [(b3, "X3 plate", ortho(20000, 20000, 12000, 0.45))])

# X4 stepped section, one body: y -50..0 is 10 high, y 0..50 is 4 high
y = case_y(4)
b4 = beam("X4 stepped 50x10 + 50x4", polyline("s", [(y - 50, 0), (y + 50, 0), (y + 50, 4), (y, 4), (y, 10), (y - 50, 10)], closed=True))
p4 = path("X4 path x -100..100", y)
xsect("X4 GJ_step_50x10+50x4 E 10 GPa -> EI 57.19, NA 4.143, GJ 86.00 (strip-wise 66.67)", p4, [(b4, "X4 stepped", ISO10)])

# X5 hollow rectangle: outer 100 x 20 (z 0..20), inner 80 x 10 (z 5..15); the ring region only
y = case_y(5)
b5 = beam("X5 hollow 100x20, hole 80x10", rect("o", y - 50, y + 50, 0, 20) + rect("i", y - 40, y + 40, 5, 15), region=(y + 45, 10))
p5 = path("X5 path x -100..100", y)
xsect("X5 hollow_rect_100x20_wall_10_5 E 10 GPa -> EI 600.0, NA 10, area 1200", p5, [(b5, "X5 hollow", ISO10)])

# X6 circle r 5 centred at z 5
y = case_y(6)
b6 = beam("X6 rod r5 centre z 5", [circle("c", *mmv(y, 5, 5))])
p6 = path("X6 path x -100..100", y)
xsect("X6 circle_r5_area E 10 GPa -> area 78.54 (0.5 %), EI 4.909, NA 5", p6, [(b6, "X6 rod", ISO10)])

# X7 bimetal: 100 x 4 at E 70 GPa (z 0..4) under 100 x 6 at E 10 GPa (z 4..10), two bodies
y = case_y(7)
b7a = beam("X7 bottom 100x4 z 0..4", rect("r", y - 50, y + 50, 0, 4))
b7b = beam("X7 top 100x6 z 4..10", rect("r", y - 50, y + 50, 4, 10))
p7 = path("X7 path x -100..100", y)
xsect("X7 bimetal E 70 / 10 GPa -> NA 2.882, EI 178.86, GJ 268.97", p7,
      [(b7a, "X7 bottom", iso(70000, 2700)), (b7b, "X7 top", iso(10000, 500))])

# X8 two touching 100 x 5 bodies of the same material, composites on (the dedup path runs)
y = case_y(8)
b8a = beam("X8 lower 100x5 z 0..5", rect("r", y - 50, y + 50, 0, 5))
b8b = beam("X8 upper 100x5 z 5..10", rect("r", y - 50, y + 50, 5, 10))
p8 = path("X8 path x -100..100", y)
xsect("X8 two_touching_laminates_no_dup E 10 GPa -> EI 83.33, NA 5, area 1000", p8,
      [(b8a, "X8 lower", ISO10), (b8b, "X8 upper", ISO10)], composites=True)

# X9 path drawn backwards (x 100 -> -100)
y = case_y(9)
b9 = beam("X9 beam 100x10 z 0..10", rect("r", y - 50, y + 50, 0, 10))
p9 = path("X9 path drawn x 100 -> -100", y, 100, -100)
xsect("X9 path_reversed_same_EI -> EI 83.33 at x -100, -50, 0, 50, 100", p9, [(b9, "X9 beam", ISO10)])

# X10 FCP > ACP: FCP a mate connector at x 100, ACP a sketch point at x -100; path -150..150, beam -160..160
y = case_y(10)
b10 = beam("X10 beam 100x10 z 0..10, x -160..160", rect("r", y - 50, y + 50, 0, 10), length=320)
p10 = path("X10 path x -150..150", y, -150, 150)
mc10 = connector("X10 FCP mate connector at x 100", 100, y)
acp10 = sketch("X10 ACP sketch point at x -100", TOP, [point("p", *mmv(-100, y))])
xsect("X10 FCP_gt_ACP_same_EI (FCP 100, ACP -100) -> 7 stations -150..150 ascending, EI 83.33, beam analysis", p10,
      [(b10, "X10 beam", ISO10)], fcp=body(mc10), acp='qCreatedBy(makeId("%s"), EntityType.VERTEX)' % acp10)

# X11 tip grazing: the path ends on the beam's end faces (beam and path both x -100..100)
y = case_y(11)
b11 = beam("X11 beam 100x10 z 0..10, x -100..100", rect("r", y - 50, y + 50, 0, 10), length=200)
p11 = path("X11 path x -100..100 (= beam ends)", y)
xsect("X11 tip_grazing_no_warning -> not WARNING, EI 83.33 at x -50, 0, 50", p11, [(b11, "X11 beam", ISO10)])

# X12 an IGNORE body on top of X1's section
y = case_y(12)
b12a = beam("X12 beam 100x10 z 0..10", rect("r", y - 50, y + 50, 0, 10))
b12b = beam("X12 ignored 100x5 z 10..15", rect("r", y - 50, y + 50, 10, 15))
p12 = path("X12 path x -100..100", y)
xsect("X12 ignored_body_adds_nothing -> EI 83.33, NA 5, GJ 125.31", p12,
      [(b12a, "X12 beam", ISO10), (b12b, "X12 ignored", IGNORE)])

# ---- Generate baseline, both ski directions (2026-09-28) ----
# B1 FCP < ACP and B2 its mirror (FCP > ACP, tip toward +X): FCP 800 / ACP 700 mm from the origin, mount 30 mm
# off-centre towards FCP, MCh 4, FRCPL 130, ARCPL 50, FCPh 5, ACPh 0.5 (the RD 20TAC targets), constant EI 100 N m^2
# (a Front-plane line at z 100 mm), weighted baseline + measurement sketch on. check_xsection_tests.py checks that B2
# is the mirror image of B1 (baseline, weighted baseline, measurement sketch) and that FRCP / ARCP are the joins.
FRONT = 'qCreatedBy(makeId("Front"), EntityType.FACE)'
GB_TAB = [e for e in ELEMENTS if e["name"] == "generateBaseline" and e["elementType"] == "FEATURESTUDIO"][0]
GB_SPEC = [x for x in c.get(f"/api/v10/featurestudios/d/{D}/w/{W}/e/{GB_TAB['id']}/featurespecs")["featureSpecs"]
           if x["featureType"] == "generateBaseline"][0]
GB_NS = GB_SPEC.get("namespace") or "e%s::m%s" % (GB_TAB["id"], GB_TAB["microversionId"])
ei_line = sketch("B EI profile 100 N m^2 (Front, z 100 mm, x -900..900)", FRONT, polyline("l", [(-900, 100), (900, 100)]))


def baseline_case(name, sign, y):
    """sign +1: FCP at x -800 (FCP < ACP); sign -1: the mirror image, FCP at x +800."""
    fcp = connector(name + " FCP", -800 * sign, y)
    acp = connector(name + " ACP", 700 * sign, y)
    mount = connector(name + " mount", -30 * sign, y)
    given = {"outputCurveName": s("outputCurveName", name + " baseline"),
             "fcpQuery": q("fcpQuery", body(fcp)), "acpQuery": q("acpQuery", body(acp)), "mountQuery": q("mountQuery", body(mount)),
             "camberHeight": num("camberHeight", "4 mm"), "frcpl": num("frcpl", "130 mm"), "arcpl": num("arcpl", "50 mm"),
             "fcpHeight": num("fcpHeight", "5 mm"), "acpHeight": num("acpHeight", "0.5 mm"),
             "hasEIProfile": b("hasEIProfile", True), "showEIQuery": b("showEIQuery", True),
             "eiEdgesQuery": q("eiEdgesQuery", edges(ei_line)),
             "approxTolerance": num("approxTolerance", "0.01 mm"), "maxControlPoints": num("maxControlPoints", "50", True),
             "addBaselineSketch": b("addBaselineSketch", True), "createWeightedBaseline": b("createWeightedBaseline", True),
             "debugPrintAnalysis": b("debugPrintAnalysis", True)}
    params = []
    for p in GB_SPEC["parameters"]:
        pid = p["parameterId"]
        d = p.get("defaultValue")
        if pid in given:
            params.append(given[pid])
        elif isinstance(d, dict):
            d = copy.deepcopy(d)
            d.pop("nodeId", None)
            params.append(dict(d, parameterId=pid))
    return feature(name, "generateBaseline", params, GB_NS)


baseline_case("B1 generate_baseline FCP_lt_ACP (FCP -800, ACP 700, mount -30)", 1, case_y(13))
baseline_case("B2 generate_baseline FCP_gt_ACP mirror of B1 (FCP 800, ACP -700, mount 30)", -1, case_y(14))

# ---- Estimate Deflection reaction moment sense, both ski directions (2026-09-28) ----
# One asymmetric EI profile (Front-plane polyline, z mm = EI N m^2) and its X mirror. Supports at the FCP (x 800 s)
# and ACP (-700 s), 500 N at the mount (-30 s), reaction moment 30 N m at the mount; s = +1 tip toward +X, -1 mirror.
#   D1 tip +X, no FCP pick (old behaviour)          D2 = D1 + FCP picked at x 800 -> identical to D1
#   D3 mirror, FCP picked at x -800 -> mirror of D1  D4 mirror, no FCP -> NOT the mirror (old: moment always +Y)
# check_xsection_tests.py compares the deflection curves. Run only these with XS_ONLY="D" (other features untouched).
ED_TAB = [e for e in ELEMENTS if e["name"] == "estimateDeflection" and e["elementType"] == "FEATURESTUDIO"][0]
ED_SPEC = [x for x in c.get(f"/api/v10/featurestudios/d/{D}/w/{W}/e/{ED_TAB['id']}/featurespecs")["featureSpecs"]
           if x["featureType"] == "estimateDeflection"][0]
ED_NS = ED_SPEC.get("namespace") or "e%s::m%s" % (ED_TAB["id"], ED_TAB["microversionId"])
EI_PTS = [(-900, 40), (-300, 120), (200, 150), (900, 60)]
ei_tip_pos = sketch("D EI profile tip +X (Front, z mm = EI)", FRONT, polyline("l", EI_PTS))
ei_tip_neg = sketch("D EI profile mirrored (Front, z mm = EI)", FRONT, polyline("l", [(-x, z) for x, z in EI_PTS[::-1]]))


def deflection_case(name, sign, fcp_y=None):
    """sign +1: tip toward +X; -1: the X mirror. fcp_y: place an FCP mate connector at (800 sign, fcp_y)."""
    given = {"selEI": q("selEI", edges(ei_tip_pos if sign > 0 else ei_tip_neg)),
             "numEvalPoints": num("numEvalPoints", "500", True),
             "addPlateConditions": b("addPlateConditions", True),
             "reactionMoment": num("reactionMoment", "30"), "reactionMomentX": num("reactionMomentX", "%g mm" % (-30 * sign)),
             "fcpReference": q("fcpReference", *([body(connector(name + " FCP", 800 * sign, fcp_y))] if fcp_y is not None else [])),
             "appliedLoad": num("appliedLoad", "500"),
             "applied1LocationType": en("applied1LocationType", "LocationType", "X_VAL", ED_NS),
             "applied1IsQuery": b("applied1IsQuery", False), "applied1NeedsWidth": b("applied1NeedsWidth", False),
             "applied1X": num("applied1X", "%g mm" % (-30 * sign)),
             "support1LocationType": en("support1LocationType", "LocationType", "X_VAL", ED_NS),
             "support1IsQuery": b("support1IsQuery", False), "support1NeedsWidth": b("support1NeedsWidth", False),
             "support1X": num("support1X", "%g mm" % (800 * sign)),
             "support2LocationType": en("support2LocationType", "LocationType", "X_VAL", ED_NS),
             "support2IsQuery": b("support2IsQuery", False), "support2NeedsWidth": b("support2NeedsWidth", False),
             "support2X": num("support2X", "%g mm" % (-700 * sign))}
    params = []
    for p in ED_SPEC["parameters"]:
        pid = p["parameterId"]
        d = p.get("defaultValue")
        if pid in given:
            params.append(given[pid])
        elif isinstance(d, dict):
            d = copy.deepcopy(d)
            d.pop("nodeId", None)
            params.append(dict(d, parameterId=pid))
    return feature(name, "estimateDeflection", params, ED_NS)


deflection_case("D1 deflection tip +X, no FCP (old behaviour)", 1)
deflection_case("D2 deflection tip +X, FCP at x 800 -> same as D1", 1, case_y(15))
deflection_case("D3 deflection mirrored, FCP at x -800 -> mirror of D1", -1, case_y(16))
deflection_case("D4 deflection mirrored, no FCP -> not the mirror (old +Y moment)", -1)

print("studio", E)
