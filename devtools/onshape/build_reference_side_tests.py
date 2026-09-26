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
      for name in ["split_plus", "offset_plus", "mutual_trim_plus", "thicken_plus", "orient_to_reference"]}

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


for n, (flip, keep_near) in enumerate([(False, True), (False, False), (True, True), (True, False)]):
    x0 = n * 400
    tag = "S%d" % (n + 1)
    target = cube("%s cube at x %d" % (tag, x0), x0, 0)
    missed = cube("%s small cube at x %d (missed by the x tool)" % (tag, x0 - 190), x0 - 190, 0, -10, 10, 10)
    pz = plane_at("%s tool z = 0%s" % (tag, ", normal flipped" if flip else ""), "Top", 0, flip)
    # as drawn the x tool's normal points -X (the harness's), flipped +X
    px = plane_at("%s tool x = %d%s" % (tag, x0, ", normal +X" if flip else ", normal -X"), "Right", x0, not flip)
    ref = ref_front("%s reference point (%d, 0, 30)" % (tag, x0 + 30), x0 + 30, 30)
    split("%s Split+ two tools, %s, keep %s -> %s" % (tag, "flipped normals" if flip else "normals as drawn",
                                                     "reference side" if keep_near else "other side",
                                                     "1 body, quarter at (%d, 0, 25)" % (x0 + 25) if keep_near else "2 bodies, quarter at (%d, 0, -25)" % (x0 - 25)), [
        q("targets", "qUnion([%s, %s])" % (body(target), body(missed))), q("tools", pz, px),
        b("keepBothSides", False), q("keepReference", ref), b("keepNear", keep_near)])

x0 = 1600
t = cube("S5 cube at x 1600", x0, 0)
split("S5 Split+ keep both sides, two tools -> 4 pieces", [
    q("targets", body(t)), q("tools", plane_at("S5 tool z = 0", "Top", 0), plane_at("S5 tool x = 1600", "Right", x0)), b("keepBothSides", True)])

s = sheet("S6 sheet z 0, x 1950..2050", [(1950, 0), (2050, 0)])
split("S6 Split+ sheet, both sides, planes x 1980 (+X) / 2020 (-X) -> 3 pieces, 2 splitEdges; regions start / middle / end at x 1965 / 2000 / 2035", [
    q("targets", body(s)), q("tools", plane_at("S6 tool x = 1980", "Right", 1980), plane_at("S6 tool x = 2020, normal -X", "Right", 2020, True)),
    b("keepBothSides", True)])

s = sheet("S7 sheet z 0, x 2350..2450", [(2350, 0), (2450, 0)])
split("S7 Split+ one tool x 2400, reference at x 2300 -> near 1 piece at x 2375, far 0", [
    q("targets", body(s)), q("tools", plane_at("S7 tool x = 2400", "Right", 2400)), b("keepBothSides", False),
    q("keepReference", ref_front("S7 reference point (2300, 0, 0)", 2300, 0)), b("keepNear", True)])

t = cube("S8 cube at x 2800", 2800, 0)
tool = sheet("S8 tool sheet z 0, x 2700..2900 (deleted by the split)", [(2700, 0), (2900, 0)], 200)
split("S8 Split+ by a sheet, reference below -> 1 body, centroid z -25, tool deleted", [
    q("targets", body(t)), q("tools", body(tool)), b("keepBothSides", False),
    q("keepReference", ref_front("S8 reference point (2800, 0, -30)", 2800, -30)), b("keepNear", True)])

t = cube("S9 cube at x 3200", 3200, 0)
split("S9 Split+ faces (top, front) by planes x 3180 / 3220 -> 2 faces per region at x 3165 / 3200 / 3235, cube 10 faces", [
    en("splitType", "SplitPlusType", "FACE", NS["split_plus"]), q("faceTargets", face_at(t, 3200, 0, 50), face_at(t, 3200, -50, 0)),
    q("tools", plane_at("S9 tool x = 3180", "Right", 3180), plane_at("S9 tool x = 3220", "Right", 3220)), b("keepTools", True)])

t = cube("S10 cube at x 3600", 3600, 0)
tool = sheet("S10 tool sheet x = 3600 (deleted by the split)", [(3600, -100), (3600, 100)], 200)
split("S10 Split+ top face by a sheet, reference at x 3500 -> near 1 face at x 3575, far 1, tool deleted", [
    en("splitType", "SplitPlusType", "FACE", NS["split_plus"]), q("faceTargets", face_at(t, 3600, 0, 50)), q("tools", body(tool)),
    q("keepReference", ref_front("S10 reference point (3500, 0, 0)", 3500, 0))])


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
print("studio", E)
