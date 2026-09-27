"""Build the Reference_Side test cases as real features in the "Reference side tests" Part Studio (upsert by
name). Replaces the old reference_side_tests.fs harness: every fixture and every case is a feature in the tree,
named with its expected result. Checked by check_reference_side_tests.py.

Cases sit far apart along X so they cannot interfere. Fixtures: cubes = sketch rectangle on Top, extruded 100 mm
symmetric; sheets = a line on Front (y = 0; sketch x = world X, sketch y = world Z) extruded as a surface
symmetric in Y (a reversed line gives the opposite normal); references = sketch points.

usage (repo root): PYTHONPATH=. python devtools/onshape/build_reference_side_tests.py
"""
import json
import math

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("reference_side/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
STUDIO = "Reference side tests"
ELEMENTS = c.list_elements(D, W)
studios = {e["name"]: e["id"] for e in ELEMENTS if e["elementType"] == "PARTSTUDIO"}
E = studios[STUDIO]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TABS = {e["name"]: e for e in ELEMENTS if e["elementType"] == "FEATURESTUDIO"}
NS = {name: "e%s::m%s" % (TABS[name]["id"], TABS[name]["microversionId"])
      for name in ["split_plus", "offset_plus", "mutual_trim_plus", "thicken_plus", "orient_to_reference", "move_face_plus", "enclose_plus", "join_profile_surfaces"]}

STATE = {"features": None}


def q(pid, *exprs):
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "query=%s;" % e} for e in exprs]}


def en(pid, enum, value, ns=""):
    return {"btType": "BTMParameterEnum-145", "parameterId": pid, "enumName": enum, "value": value, "namespace": ns}


def num(pid, expr):
    return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": expr, "isInteger": False}


def b(pid, v):
    return {"btType": "BTMParameterBoolean-144", "parameterId": pid, "value": v}


def upsert(feature):
    if STATE["features"] is None:
        STATE["features"] = c.get(f"{BASE}/features")
    f = STATE["features"]
    existing = [x for x in f["features"] if x["name"] == feature["name"]]
    body = {"btType": "BTFeatureDefinitionCall-1406", "feature": feature,
            "serializationVersion": f["serializationVersion"], "sourceMicroversion": f["sourceMicroversion"]}
    if existing:
        feature["featureId"] = existing[0]["featureId"]
        r = c.post(f"{BASE}/features/featureid/{existing[0]['featureId']}", body)
    else:
        r = c.post(f"{BASE}/features", body)
    STATE["features"] = None
    print("%-100s %s" % (feature["name"][:100], r.get("featureState", {}).get("featureStatus")))
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


def arc(eid, cx, cy, r, a0, a1):
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": a0, "endParam": a1, "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryCircle-115", "radius": r, "xCenter": cx, "yCenter": cy, "xDir": 1.0, "yDir": 0.0, "clockwise": False}}


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


def sketch(name, plane_expr, entities):
    return upsert({"btType": "BTMSketch-151", "featureType": "newSketch", "name": name,
                   "parameters": [q("sketchPlane", plane_expr)], "entities": entities, "constraints": []})


TOP = 'qCreatedBy(makeId("Top"), EntityType.FACE)'
FRONT = 'qCreatedBy(makeId("Front"), EntityType.FACE)'
RIGHT = 'qCreatedBy(makeId("Right"), EntityType.FACE)'


def edges(fid):
    """A sketch's curves: its wire body's edges (a closed sketch's region face has a second copy of each)."""
    return 'qConstructionFilter(qOwnedByBody(qBodyType(qCreatedBy(makeId("%s"), EntityType.BODY), BodyType.WIRE), EntityType.EDGE), ConstructionObject.NO)' % fid


def body(fid):
    return 'qCreatedBy(makeId("%s"), EntityType.BODY)' % fid


def vertex(fid):
    return 'qCreatedBy(makeId("%s"), EntityType.VERTEX)' % fid


def face_at(fid, x, y, z):
    return 'qContainsPoint(qOwnedByBody(qCreatedBy(makeId("%s"), EntityType.BODY), EntityType.FACE), vector(%g, %g, %g) * millimeter)' % (fid, x, y, z)


def plane_face(fid):
    return 'qCreatedBy(makeId("%s"), EntityType.FACE)' % fid


# ---- fixtures ----
def cube(name, x, y, z0=-50, z1=50, half=50, hy=None):
    """A box x +- half, y +- hy (default half), z z0..z1."""
    hy = half if hy is None else hy
    s = sketch(name + " (sketch)", TOP if z0 == -z1 else plane_at(name + " base plane", "Top", (z0 + z1) / 2.0),
               polyline("r", [(x - half, y - hy), (x + half, y - hy), (x + half, y + hy), (x - half, y + hy)], closed=True))
    return feature(name, "extrude", [
        en("bodyType", "ExtendedToolBodyType", "SOLID"), en("operationType", "NewBodyOperationType", "NEW"),
        q("entities", 'qSketchRegion(makeId("%s"))' % s), en("endBound", "BoundingType", "BLIND"),
        num("depth", "%g mm" % (z1 - z0)), b("symmetric", True)])


PLANES = {}


def plane_at(name, base, offset, flip=False):
    key = (base, offset, flip)
    if key in PLANES:
        return plane_face(PLANES[key])
    fid = feature(name, "cPlane", [
        q("entities", 'qCreatedBy(makeId("%s"), EntityType.FACE)' % base), en("cplaneType", "CPlaneType", "OFFSET"),
        num("offset", "%g mm" % abs(offset)), b("oppositeDirection", offset < 0), b("flipNormal", flip)])
    PLANES[key] = fid
    return plane_face(fid)


def sheet(name, pts, width=100):
    """A surface: the Front-plane polyline pts [(x, z), ...] extruded symmetric `width` in Y."""
    s = sketch(name + " (sketch)", FRONT, polyline("l", pts))
    return feature(name, "extrude", [
        en("bodyType", "ExtendedToolBodyType", "SURFACE"), en("surfaceOperationType", "NewSurfaceOperationType", "NEW"),
        q("surfaceEntities", edges(s)), en("endBound", "BoundingType", "BLIND"), num("depth", "%g mm" % width), b("symmetric", True)])


def ref_front(name, x, z):
    return vertex(sketch(name, FRONT, [point("p", *mmv(x, z))]))


def ref_top(name, x, y, z=0):
    return vertex(sketch(name, TOP if z == 0 else plane_at("Plane z = %g" % z, "Top", z), [point("p", *mmv(x, y))]))


# ============================================================================
# Split+
# ============================================================================
def split(name, params):
    return feature(name, "splitPlus", params, NS["split_plus"])


def keep(value):
    return en("keep", "SplitPlusKeep", value, NS["split_plus"])


# v2 (2026-09-27): start / end tools, inside reference, Keep both / inside / outside.
for n, (flip, kept) in enumerate([(False, "INSIDE"), (True, "INSIDE"), (False, "OUTSIDE"), (False, "BOTH")]):
    x0 = n * 400
    tag = "S%d" % (n + 1)
    target = cube("%s cube at x %d" % (tag, x0), x0, 0)
    pz = plane_at("%s tool z = 0%s" % (tag, ", normal flipped" if flip else ""), "Top", 0, flip)
    ref = ref_front("%s reference point (%d, 0, 30)" % (tag, x0), x0, 30)
    want = {"INSIDE": "1 body, centroid z 25", "OUTSIDE": "1 body, centroid z -25", "BOTH": "inside z 25, start z -25, startCut 4"}[kept]
    split("%s Split+ one tool z = 0%s, reference above, keep %s -> %s" % (tag, " (normal down)" if flip else "", kept.lower(), want), [
        q("targets", body(target)), q("startTool", pz), q("insideReference", ref), keep(kept)])

x0 = 1600
for tag, kept, x0 in (("S5", "BOTH", 1600), ("S6", "INSIDE", 2000), ("S7", "OUTSIDE", 2400)):
    sh = sheet("%s sheet z 0, x %d..%d" % (tag, x0 - 50, x0 + 50), [(x0 - 50, 0), (x0 + 50, 0)])
    split("%s Split+ sheet, start x %d (+X) / end x %d (-X), no reference, keep %s" % (tag, x0 - 20, x0 + 20, kept.lower()), [
        q("targets", body(sh)), q("startTool", plane_at("%s start tool x = %d" % (tag, x0 - 20), "Right", x0 - 20)),
        q("endTool", plane_at("%s end tool x = %d, normal -X" % (tag, x0 + 20), "Right", x0 + 20, True)), keep(kept)])

t = cube("S8 cube at x 2800", 2800, 0)
tool = sheet("S8 tool sheet z 0, x 2700..2900 (deleted by the split)", [(2700, 0), (2900, 0)], 200)
split("S8 Split+ by a sheet, reference below, keep inside -> 1 body, centroid z -25, tool deleted", [
    q("targets", body(t)), q("startTool", body(tool)),
    q("insideReference", ref_front("S8 reference point (2800, 0, -30)", 2800, -30)), keep("INSIDE")])

t = cube("S9 cube at x 3200", 3200, 0)
split("S9 Split+ faces (top, front) by start x 3180 / end x 3220 -> inside / start / end 2 faces each at x 3200 / 3165 / 3235", [
    en("splitType", "SplitPlusType", "FACE", NS["split_plus"]), q("faceTargets", face_at(t, 3200, 0, 50), face_at(t, 3200, -50, 0)),
    q("startTool", plane_at("S9 start tool x = 3180", "Right", 3180)), q("endTool", plane_at("S9 end tool x = 3220", "Right", 3220)),
    b("keepTools", True)])

t = cube("S10 cube at x 3600", 3600, 0)
tool = sheet("S10 tool sheet x = 3600 (deleted by the split)", [(3600, -100), (3600, 100)], 200)
split("S10 Split+ top face by a sheet, reference at x 3500 -> inside 1 face at x 3575, start 1, tool deleted", [
    en("splitType", "SplitPlusType", "FACE", NS["split_plus"]), q("faceTargets", face_at(t, 3600, 0, 50)), q("startTool", body(tool)),
    q("insideReference", ref_front("S10 reference point (3500, 0, 0)", 3500, 0))])


# ============================================================================
# Offset+
# ============================================================================
def offset(name, params):
    return feature(name, "offsetPlus", params, NS["offset_plus"])


for n, toward in enumerate([True, False]):
    x0 = 4000 + 400 * n
    tag = "O%d" % (n + 1)
    top = sheet("%s top sheet z 50" % tag, [(x0 - 50, 50), (x0 + 50, 50)])
    bottom = sheet("%s bottom sheet z -50 (drawn reversed: opposite normal)" % tag, [(x0 + 50, -50), (x0 - 50, -50)])
    offset("%s Offset+ surfaces of opposite normals %s the centre -> z +-%d, 8 boundaryEdges" % (tag, "toward" if toward else "away from", 40 if toward else 60), [
        en("offsetType", "OffsetPlusType", "SURFACE", NS["offset_plus"]), q("surfaces", body(top), body(bottom)), num("distance", "10 mm"),
        q("sideReference", ref_front("%s reference point (%d, 0, 0)" % (tag, x0), x0, 0)), b("towardReference", toward)])

n = 0
for mode in ["PLANE", "TRANSPORT"]:
    for toward in [True, False]:
        n += 1
        tag = "C%d" % n
        x0 = 5000 if mode == "PLANE" else 5400
        y0 = 0 if toward else 500
        sq = sketch("%s square 100 at (%d, %d)" % (tag, x0, y0), TOP, polyline("s", [(x0, y0), (x0 + 100, y0), (x0 + 100, y0 + 100), (x0, y0 + 100)], closed=True))
        offset("%s Offset+ square, %s frame, %s -> 1 wire, length %s, 10 from the square" % (
            tag, mode.lower(), "outward" if toward else "inward", "462.8319 (400 + 20 pi)" if toward else "320"), [
            en("offsetType", "OffsetPlusType", "CURVE", NS["offset_plus"]), q("curves", edges(sq)),
            en("frameMode", "OffsetFrameMode", mode, NS["offset_plus"]), num("distance", "10 mm"),
            q("sideReference", ref_top("%s reference point (%d, %d, 0)" % (tag, x0 + 300, y0 + 50), x0 + 300, y0 + 50)),
            b("towardReference", toward)])

for n, direction in enumerate(["NORMAL", "TANGENT"]):
    tag = "C%d" % (5 + n)
    x0 = 6000 + 400 * n
    sh = sheet("%s sheet z 50" % tag, [(x0 - 50, 50), (x0 + 50, 50)])
    ln = sketch("%s line on the sheet, x %d..%d at z 50" % (tag, x0 - 40, x0 + 40), plane_at("Plane z = 50", "Top", 50),
                [seg("l", *mmv(x0 - 40, 0, x0 + 40, 0))])
    ref = ref_front("%s reference point (%d, 0, 120)" % (tag, x0), x0, 120) if direction == "NORMAL" else ref_top("%s reference point (%d, 40, 50)" % (tag, x0), x0, 40, 50)
    offset("%s Offset+ line, surface-normal frame, %s -> centred at %s" % (tag, "along the normal" if direction == "NORMAL" else "along the surface",
                                                                           "(%d, 0, 60)" % x0 if direction == "NORMAL" else "(%d, 10, 50)" % x0), [
        en("offsetType", "OffsetPlusType", "CURVE", NS["offset_plus"]), q("curves", edges(ln)),
        en("frameMode", "OffsetFrameMode", "SURFACE", NS["offset_plus"]), q("normalSurface", body(sh)),
        en("surfaceDirection", "SurfaceOffsetDirection", direction, NS["offset_plus"]), num("distance", "10 mm"),
        q("sideReference", ref), b("towardReference", True)])

ci = sketch("C7 circle r 50 at (6800, 0)", TOP, [circle("c", *mmv(6800, 0, 50))])

def with_spec_defaults(ftype, given):
    """Every parameter of a std feature from its spec's defaults, then `given` on top (a partial std helix fails its precondition)."""
    spec = [s for s in c.get(f"{BASE}/featurespecs")["featureSpecs"] if s["featureType"] == ftype][0]
    by_id = {p["parameterId"]: p for p in given}
    out = []
    for p in spec["parameters"]:
        if p["parameterId"] in by_id:
            out.append(by_id[p["parameterId"]])
        elif isinstance(p.get("defaultValue"), dict):
            out.append(dict(p["defaultValue"], parameterId=p["parameterId"]))
    return out


hx = feature("C7 helix r 50, 2 turns, height 80", "helix", with_spec_defaults("helix", [
    en("axisType", "AxisType", "CIRCLE"), q("edge", edges(ci)), en("pathType", "PathType", "TURNS"),
    num("revolutions", "2"), num("height", "80 mm"), en("endType", "EndType", "HEIGHT")]))
offset("C7 Offset+ helix, transport frame toward the axis -> 10 from the helix everywhere, inside radius 50", [
    en("offsetType", "OffsetPlusType", "CURVE", NS["offset_plus"]), q("curves", 'qCreatedBy(makeId("%s"), EntityType.EDGE)' % hx),
    en("frameMode", "OffsetFrameMode", "TRANSPORT", NS["offset_plus"]), num("distance", "10 mm"),
    q("sideReference", ref_top("C7 reference point (6800, 0, 0) on the axis", 6800, 0)), b("towardReference", True)])

z = sketch("C8 Z chain (7200,0) > (7300,0) > (7300,100) > (7400,100)", TOP, polyline("z", [(7200, 0), (7300, 0), (7300, 100), (7400, 100)]))
offset("C8 Offset+ Z chain, plane frame -> one round + one trim, length 295.7080 (280 + 5 pi), start (7200,-10), end (7400,90), 1 corner arc", [
    en("offsetType", "OffsetPlusType", "CURVE", NS["offset_plus"]), q("curves", edges(z)),
    en("frameMode", "OffsetFrameMode", "PLANE", NS["offset_plus"]), num("distance", "10 mm"),
    q("sideReference", ref_top("C8 reference point (7250, -30, 0)", 7250, -30)), b("towardReference", True)])


# ============================================================================
# Mutual Trim+
# ============================================================================
for n, near1 in enumerate([True, False]):
    tag = "M%d" % (n + 1)
    x0 = 7800 + 400 * n
    h = sheet("%s horizontal sheet z 0, x %d..%d" % (tag, x0 - 50, x0 + 50), [(x0 - 50, 0), (x0 + 50, 0)])
    v = sheet("%s vertical sheet x %d, z -50..50" % (tag, x0), [(x0, -50), (x0, 50)])
    feature("%s Mutual Trim+ %s -> L of 10000 mm2, centroid (%g, 0, 12.5); kept faces 5000 each" % (
        tag, "keep reference side on both" if near1 else "first keeps the far side", x0 + 12.5 if near1 else x0 - 12.5), "mutualTrimPlus", [
        q("body1", body(h)), q("body2", body(v)), b("keepNear1", near1), b("keepNear2", True),
        q("keepReference", ref_front("%s reference point (%d, 0, 20)" % (tag, x0 + 20), x0 + 20, 20)), b("merge", True)], NS["mutual_trim_plus"])


# ============================================================================
# Thicken+
# ============================================================================
def thicken(name, params):
    return feature(name, "thickenPlus", params, NS["thicken_plus"])


for n, (toward, away, swap) in enumerate([(5, 0, False), (2, 3, False), (2, 3, True)]):
    tag = "T%d" % (n + 1)
    x0 = 8600 + 400 * n
    top = sheet("%s top sheet z 50" % tag, [(x0 - 50, 50), (x0 + 50, 50)])
    bottom = sheet("%s bottom sheet z -50 (drawn reversed: opposite normal)" % tag, [(x0 + 50, -50), (x0 - 50, -50)])
    t_in, t_out = (away, toward) if swap else (toward, away)
    thicken("%s Thicken+ two sheets of opposite normals, toward %g away %g%s -> top z %g..%g, bottom z %g..%g" % (
        tag, toward, away, ", swapped" if swap else "", 50 - t_in, 50 + t_out, -50 - t_out, -50 + t_in), [
        en("operationType", "NewBodyOperationType", "NEW", NS["thicken_plus"]), q("entities", body(top), body(bottom)),
        q("sideReference", ref_front("%s reference point (%d, 0, 0)" % (tag, x0), x0, 0)),
        num("thicknessToward", "%g mm" % toward), num("thicknessAway", "%g mm" % away), b("swapSides", swap)])

for n, depth in enumerate([15, 5]):
    tag = "T%d" % (4 + n)
    x0 = 9800 + 400 * n
    cyl = sketch("%s arc r 10 about (%d, 0, 0), half turn" % (tag, x0), FRONT, [arc("a", *mmv(x0, 0, 10), 0, math.pi)])
    cs = feature("%s half cylinder r 10" % tag, "extrude", [
        en("bodyType", "ExtendedToolBodyType", "SURFACE"), en("surfaceOperationType", "NewSurfaceOperationType", "NEW"),
        q("surfaceEntities", edges(cyl)), en("endBound", "BoundingType", "BLIND"), num("depth", "100 mm"), b("symmetric", True)])
    thicken("%s Thicken+ %d mm toward the axis of a r 10 half cylinder -> %s" % (
        tag, depth, "ERROR: concave radius 10 on the side thickened by 15" if depth == 15 else "OK, 5 mm shell"), [
        en("operationType", "NewBodyOperationType", "NEW", NS["thicken_plus"]), q("entities", body(cs)),
        q("sideReference", ref_front("%s reference point (%d, 0, 0) on the axis" % (tag, x0), x0, 0)),
        num("thicknessToward", "%d mm" % depth), num("thicknessAway", "0 mm")])

x0 = 10600
t = cube("T6 cube at x 10600", x0, 0)
top = sheet("T6 sheet on the cube's top, z 50, x 10570..10630", [(x0 - 30, 50), (x0 + 30, 50)], 60)
thicken("T6 Thicken+ 5 mm up from the cube top, Add into the cube -> 1 part, volume 1018000, towardFaces 1 face at z 55", [
    en("operationType", "NewBodyOperationType", "ADD", NS["thicken_plus"]), q("entities", body(top)),
    q("sideReference", ref_front("T6 reference point (10600, 0, 100)", x0, 100)),
    num("thicknessToward", "5 mm"), num("thicknessAway", "0 mm"),
    b("defaultScope", False), q("booleanScope", body(t))])


# ============================================================================
# Orient to reference (2026-09-25)
# ============================================================================
def orient(name, surfaces, reference, toward):
    return feature(name, "orientToReference", [
        q("surfaces", surfaces), q("reference", reference), b("towardReference", toward),
        b("showNormals", True), num("arrowLength", "10 mm"), b("debugPrint", False)], NS["orient_to_reference"])


for n, toward in enumerate([True, False]):
    x0 = 11000 + 400 * n
    tag = "R%d" % (n + 1)
    top = sheet("%s top sheet z 50" % tag, [(x0 - 50, 50), (x0 + 50, 50)])
    bottom = sheet("%s bottom sheet z -50 (drawn reversed: opposite normal)" % tag, [(x0 + 50, -50), (x0 - 50, -50)])
    orient("%s Orient to reference, sheets of opposite normals, %s the centre -> normals %s" % (
        tag, "toward" if toward else "away from", "top -Z, bottom +Z" if toward else "top +Z, bottom -Z"),
        "qUnion([%s, %s])" % (body(top), body(bottom)), ref_front("%s reference point (%d, 0, 0)" % (tag, x0), x0, 0), toward)

x0 = 11800
cyl = sketch("R3 arc r 10 about (%d, 0, 0), half turn" % x0, FRONT, [arc("a", *mmv(x0, 0, 10), 0, math.pi)])
half = feature("R3 half cylinder r 10", "extrude", [
    en("bodyType", "ExtendedToolBodyType", "SURFACE"), en("surfaceOperationType", "NewSurfaceOperationType", "NEW"),
    q("surfaceEntities", edges(cyl)), en("endBound", "BoundingType", "BLIND"), num("depth", "100 mm"), b("symmetric", True)])
orient("R3 Orient to reference, half cylinder, reference on the axis -> normals point to the axis",
       body(half), ref_front("R3 reference point (%d, 0, 0) on the axis" % x0, x0, 0), True)

x0 = 12200
rc = cube("R4 cube at x 12200", x0, 0)
orient("R4 Orient to reference, a solid's face -> ERROR (solids cannot be flipped)",
       face_at(rc, x0, 0, 50), ref_front("R4 reference point (%d, 0, 100)" % x0, x0, 100), True)
# ============================================================================
# Move face+ (2026-09-26)
# ============================================================================
def move_face(name, params):
    return feature(name, "moveFacePlus", params, NS["move_face_plus"])


for n, toward in enumerate([True, False]):
    x0 = 13000 + 400 * n
    tag = "F%d" % (n + 1)
    top = sheet("%s top sheet z 50" % tag, [(x0 - 50, 50), (x0 + 50, 50)])
    bottom = sheet("%s bottom sheet z -50 (drawn reversed: opposite normal)" % tag, [(x0 + 50, -50), (x0 - 50, -50)])
    move_face("%s Move face+ faces of sheets of opposite normals %s the centre -> z +-%d" % (tag, "toward" if toward else "away from", 40 if toward else 60), [
        q("faces", "qOwnedByBody(qUnion([%s, %s]), EntityType.FACE)" % (body(top), body(bottom))), num("distance", "10 mm"),
        q("sideReference", ref_front("%s reference point (%d, 0, 0)" % (tag, x0), x0, 0)), b("towardReference", toward),
        b("reFillet", False), b("debugPrint", False)])

x0 = 13800
mc3 = cube("F3 cube at x 13800", x0, 0)
move_face("F3 Move face+ solid top face, no reference -> grows to z 60", [
    q("faces", face_at(mc3, x0, 0, 50)), num("distance", "10 mm"), q("sideReference"), b("towardReference", True),
    b("reFillet", False), b("debugPrint", False)])

x0 = 14200
mc4 = cube("F4 cube at x 14200", x0, 0)
move_face("F4 Move face+ solid top face toward a reference inside -> shrinks to z 40", [
    q("faces", face_at(mc4, x0, 0, 50)), num("distance", "10 mm"),
    q("sideReference", ref_front("F4 reference point (%d, 0, 0)" % x0, x0, 0)), b("towardReference", True),
    b("reFillet", False), b("debugPrint", False)])
print("studio", E)

# ============================================================================
# Signed / zero distances (2026-09-27): negative = the other way, 0 = an unmoved copy
# ============================================================================
for tag, x0, dist, want in (("O3", 14600, "-10 mm", 60), ("O4", 15000, "0 mm", 50)):
    top = sheet("%s top sheet z 50" % tag, [(x0 - 50, 50), (x0 + 50, 50)])
    bottom = sheet("%s bottom sheet z -50 (drawn reversed: opposite normal)" % tag, [(x0 + 50, -50), (x0 - 50, -50)])
    offset("%s Offset+ surfaces toward the centre, distance %s -> z +-%d" % (tag, dist, want), [
        en("offsetType", "OffsetPlusType", "SURFACE", NS["offset_plus"]), q("surfaces", body(top), body(bottom)), num("distance", dist),
        q("sideReference", ref_front("%s reference point (%d, 0, 0)" % (tag, x0), x0, 0)), b("towardReference", True)])

x0 = 15400
sq9 = sketch("C9 square 100 at (%d, 0)" % x0, TOP, polyline("s", [(x0, 0), (x0 + 100, 0), (x0 + 100, 100), (x0, 100)], closed=True))
offset("C9 Offset+ square, plane frame, distance 0 -> 1 wire, an exact copy, length 400", [
    en("offsetType", "OffsetPlusType", "CURVE", NS["offset_plus"]), q("curves", edges(sq9)),
    en("frameMode", "OffsetFrameMode", "PLANE", NS["offset_plus"]), num("distance", "0 mm"),
    q("sideReference", ref_top("C9 reference point (%d, 50, 0)" % (x0 + 300), x0 + 300, 50)), b("towardReference", True)])

x0 = 15800
top5 = sheet("F5 top sheet z 50", [(x0 - 50, 50), (x0 + 50, 50)])
bottom5 = sheet("F5 bottom sheet z -50 (drawn reversed: opposite normal)", [(x0 + 50, -50), (x0 - 50, -50)])
move_face("F5 Move face+ faces toward the centre, distance -10 mm -> z +-60", [
    q("faces", "qOwnedByBody(qUnion([%s, %s]), EntityType.FACE)" % (body(top5), body(bottom5))), num("distance", "-10 mm"),
    q("sideReference", ref_front("F5 reference point (%d, 0, 0)" % x0, x0, 0)), b("towardReference", True),
    b("reFillet", False), b("debugPrint", False)])
print("studio", E)

# ============================================================================
# Split+ piece side read ON the piece (2026-09-27 audit): a ring's centroid lies inside the cutter
# ============================================================================
x0 = 16200
plate = cube("S11 plate at x 16200 (100 x 100 x 20)", x0, 0, -10, 10)
circ = sketch("S11 circle r 20 at (16200, 0)", TOP, [circle("c", *mmv(x0, 0, 20))])
cyl = feature("S11 cylinder sheet r 20 (the cutter)", "extrude", [
    en("bodyType", "ExtendedToolBodyType", "SURFACE"), en("surfaceOperationType", "NewSurfaceOperationType", "NEW"),
    q("surfaceEntities", edges(circ)), en("endBound", "BoundingType", "BLIND"), num("depth", "60 mm"), b("symmetric", True)])
split("S11 Split+ plate by a cylinder, reference on the axis, keep outside -> the ring (1 body, volume 200000 - 8000 pi)", [
    q("targets", body(plate)), q("startTool", body(cyl)),
    q("insideReference", ref_top("S11 reference point (16200, 0, 0)", x0, 0)), keep("OUTSIDE")])
print("studio", E)

# ---- 2026-09-27 audit: outputs of Thicken+ Remove, Move face+ at distance 0 ----
x0 = 17000
t7 = cube("T7 cube at x 17000", x0, 0)
top7 = sheet("T7 sheet on the cube's top, z 50, x 16970..17030", [(x0 - 30, 50), (x0 + 30, 50)], 60)
thicken("T7 Thicken+ 5 mm down into the cube, Remove -> 1 part, volume 982000, output = the cube", [
    en("operationType", "NewBodyOperationType", "REMOVE", NS["thicken_plus"]), q("entities", body(top7)),
    q("sideReference", ref_front("T7 reference point (17000, 0, 0)", x0, 0)),
    num("thicknessToward", "5 mm"), num("thicknessAway", "0 mm"),
    b("defaultScope", False), q("booleanScope", body(t7))])

x0 = 17400
f6 = cube("F6 cube at x 17400", x0, 0)
move_face("F6 Move face+ distance 0 -> nothing moves, output still published (1 face)", [
    q("faces", face_at(f6, x0, 0, 50)), num("distance", "0 mm"), q("sideReference"), b("towardReference", True),
    b("reFillet", False), b("debugPrint", False)])
print("studio", E)

# ============================================================================
# Enclose+ (2026-09-27)
# ============================================================================
def enclose(name, params):
    return feature(name, "enclosePlus", params, NS["enclose_plus"])


def tube(name, pts, closed=True):
    """A surface: a Top-plane polyline extruded symmetric 100 mm in Z."""
    sk = sketch(name + " (sketch)", TOP, polyline("t", pts, closed=closed))
    return feature(name, "extrude", [
        en("bodyType", "ExtendedToolBodyType", "SURFACE"), en("surfaceOperationType", "NewSurfaceOperationType", "NEW"),
        q("surfaceEntities", edges(sk)), en("endBound", "BoundingType", "BLIND"), num("depth", "100 mm"), b("symmetric", True)])


def square(x0, half=50):
    return [(x0 - half, -half), (x0 + half, -half), (x0 + half, half), (x0 - half, half)]


cap_hi = plane_at("Enclose cap plane z = 20", "Top", 20)
cap_lo = plane_at("Enclose cap plane z = -20", "Top", -20)

x0 = 18000
e1 = tube("E1 square tube 100 at x 18000", square(x0))
enclose("E1 Enclose+ tube capped by planes z +-20, point inside -> 1 part, volume 400000, 6 faces (2 cap, 4 surface)", [
    q("surfaces", body(e1)), q("caps", cap_hi, cap_lo), q("insidePoint", ref_top("E1 inside point (18000, 0, 0)", x0, 0)), b("keepTools", False)])

x0 = 18400
e2 = tube("E2 half tube (U, open at x 18400)", [(x0, -50), (x0 + 50, -50), (x0 + 50, 50), (x0, 50)], closed=False)
enclose("E2 Enclose+ half tube, mirror at x 18400 -> 1 part x 18350..18450, volume 400000, 6 faces", [
    q("surfaces", body(e2)), q("caps", cap_hi, cap_lo), q("insidePoint", ref_top("E2 inside point (18425, 0, 0)", x0 + 25, 0)),
    q("mirrorPlane", plane_at("E2 mirror plane x = 18400", "Right", x0)), b("keepTools", False)])

x0 = 18800
e3 = tube("E3 square tube 100 at x 18800", square(x0))
e3s = tube("E3 small tube 20 at x 19000 (a stray pocket)", square(x0 + 200, 10))
enclose("E3 Enclose+ two tubes, point in the big one -> 1 part, volume 400000 (the stray pocket dropped)", [
    q("surfaces", body(e3), body(e3s)), q("caps", cap_hi, cap_lo), q("insidePoint", ref_top("E3 inside point (18800, 0, 0)", x0, 0)), b("keepTools", False)])

x0 = 19400
e4 = tube("E4 square tube 100 at x 19400", square(x0))
enclose("E4 Enclose+ inside point outside the tube -> ERROR", [
    q("surfaces", body(e4)), q("caps", cap_hi, cap_lo), q("insidePoint", ref_top("E4 point outside (19600, 0, 0)", x0 + 200, 0)), b("keepTools", False)])
print("studio", E)

# ============================================================================
# Join profile surfaces (2026-09-27)
# ============================================================================
def join(name, params):
    return feature(name, "joinProfileSurfaces", params, NS["join_profile_surfaces"])


def join_params(start, end, point, inside, inside_off, outside=None, outside_off="0 mm", start_prof=None, start_off="0 mm",
                end_prof=None, end_off="0 mm", start_fillet=None):
    p = [q("startSplit", start), q("endSplit", end), q("insidePoint", point), q("insideProfile", body(inside)), num("insideOffset", inside_off)]
    if outside is not None:
        p += [en("outsideMode", "JoinOutsideMode", "SAME", NS["join_profile_surfaces"]), q("outsideProfile", body(outside)), num("outsideOffset", outside_off)]
    else:
        p += [en("outsideMode", "JoinOutsideMode", "DIFFERENT", NS["join_profile_surfaces"]), q("startProfile", body(start_prof)),
              num("startOffset", start_off), q("endProfile", body(end_prof)), num("endOffset", end_off)]
    if start_fillet is not None:
        p += [b("startFillet", True), num("startRadius", start_fillet[0]),
              en("startFilletEdge", "JoinFilletEdge", start_fillet[1], NS["join_profile_surfaces"]), b("startKeepOpposite", start_fillet[2])]
    else:
        p += [b("startFillet", False)]
    return p + [b("endFillet", False), b("merge", True), b("keepInputs", True)]


for tag, x0, inside_off, want in (("J1", 21000, "0 mm", "5 faces, area 24000"), ("J2", 21400, "5 mm", "inside at z -5, area 25000"),
                                  ("J4", 22200, "0 mm", "start fillet r 5 on the inside edge, 6 faces")):
    ins = sheet("%s inside profile z 0, x %d..%d" % (tag, x0 - 100, x0 + 100), [(x0 - 100, 0), (x0 + 100, 0)])
    out = sheet("%s outside profile z 20" % tag, [(x0 - 100, 20), (x0 + 100, 20)])
    st = plane_at("%s start split x = %d" % (tag, x0 - 50), "Right", x0 - 50)
    en_ = plane_at("%s end split x = %d" % (tag, x0 + 50), "Right", x0 + 50)
    pt = ref_front("%s inside point (%d, 0, -10)" % (tag, x0), x0, -10)
    join("%s Join profile surfaces, same outside -> %s" % (tag, want),
         join_params(st, en_, pt, ins, inside_off, outside=out, start_fillet=("5 mm", "INSIDE", False) if tag == "J4" else None))

x0 = 21800
ins = sheet("J3 inside profile z 0, x %d..%d" % (x0 - 100, x0 + 100), [(x0 - 100, 0), (x0 + 100, 0)])
so = sheet("J3 start outside profile z 20, x %d..%d" % (x0 - 100, x0), [(x0 - 100, 20), (x0, 20)])
eo = sheet("J3 end outside profile z 30, x %d..%d" % (x0, x0 + 100), [(x0, 30), (x0 + 100, 30)])
join("J3 Join profile surfaces, different start (z 20) / end (z 30) -> area 25000", join_params(
    plane_at("J3 start split x = %d" % (x0 - 50), "Right", x0 - 50), plane_at("J3 end split x = %d" % (x0 + 50), "Right", x0 + 50),
    ref_front("J3 inside point (%d, 0, -10)" % x0, x0, -10), ins, "0 mm", start_prof=so, end_prof=eo))
print("studio", E)
