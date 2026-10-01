"""Build the Move Along Edge test cases as real features in smallTools' "Move along edge tests" Part Studio
(created when missing; upsert by name). Fixtures are native features only (sketches, extrudes, composite curve,
mate connectors). Checked by check_move_along_edge_tests.py.

Cases sit 1000 mm apart along X. Cubes are 20 mm, centred on the path unless stated.

  M1  line, cube moved 100                                   -> cube centroid (100, 0, 0)
  M2  composite-curve WIRE (line 200 + R100 quarter arc), 278.54 -> on the arc at 45 deg (1270.71, 29.29, 0)
  M3  copy + names: main 100 "M3 main", copies 50 prefix "A_", 150 suffix "_B", 250 unnamed
                                                              -> cube stays; 4 copies; keys initial_copy, copy_1..3
  M3b prefix with no stored source names                     -> WARNING, copy named "P_" only
  M4  mate connector as points: 100 "M4 P0", copy 200 "M4 P1" -> 2 named points, connector stays at 4000
  M5  mate connector moved 100                               -> connector origin (5100, 0, 0)
  M6  closed square loop (perimeter 800), moved 900          -> wraps to a corner (6100 or 5900, -100)
  M7  S-curve (inflection), cube 30 above the plane, Orientation Frenet -> normal flips: centroid (7200, 200, -30)
  M8  same, Orientation Transported                          -> no twist: centroid (8200, 200, 30)
  M9  flip + past the path start: line 9000..9200, cube at 9050, 100 back -> (8950, 0, 0)
  M10 two sketch edges, second drawn reversed, 130          -> round the corner (10100, 50, 0)
  M12-M14 sketch edge -> wire, sketch point -> points, mixed selection names
  M15-M16 move to the path point nearest a target (a point; a line crossing the path)
  M17-M19 Apply: translate only, rotate only, rotate only to a nearest point (40 x 20 blocks show the turn)
  M11 disconnected edges (temporary instance, deleted)      -> ERROR

usage (repo root): PYTHONPATH=. python devtools/onshape/build_move_along_edge_tests.py
"""
import copy
import json
import math
import re

from sync.core.client import OnshapeClient
from devtools.onshape.fsapi import write_feature, delete_feature, feature_status  # noqa: E402  (API budget: one feature-tree GET per studio)

c = OnshapeClient()
DOC = json.load(open("smallTools/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
STUDIO = "Move along edge tests"
ELEMENTS = c.list_elements(D, W)
studios = {e["name"]: e["id"] for e in ELEMENTS if e["elementType"] == "PARTSTUDIO"}
if STUDIO not in studios:
    studios[STUDIO] = c.post(f"/api/v10/partstudios/d/{D}/w/{W}", {"name": STUDIO})["id"]
E = studios[STUDIO]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TAB = [e for e in ELEMENTS if e["name"] == "Move_Along_Edge"][0]
NS = "e%s::m%s" % (TAB["id"], TAB["microversionId"])
SPEC = [x for x in c.get(f"/api/v10/featurestudios/d/{D}/w/{W}/e/{TAB['id']}/featurespecs")["featureSpecs"]
        if x["featureType"] == "myFeature"][0]

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


def s(pid, v):
    return {"btType": "BTMParameterString-149", "parameterId": pid, "value": v}


_TREE = {}   # feature tree of the test Part Studio, kept in step with each write (API budget)


def upsert(feature):
    r = write_feature(BASE, feature, _TREE, client=c)
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


def edges(fid):
    return 'qConstructionFilter(qOwnedByBody(qBodyType(qCreatedBy(makeId("%s"), EntityType.BODY), BodyType.WIRE), EntityType.EDGE), ConstructionObject.NO)' % fid


def body(fid):
    return 'qCreatedBy(makeId("%s"), EntityType.BODY)' % fid


PLANES = {}


def plane_at(name, offset):
    if offset not in PLANES:
        PLANES[offset] = feature(name, "cPlane", [
            q("entities", TOP), en("cplaneType", "CPlaneType", "OFFSET"),
            num("offset", "%g mm" % abs(offset)), b("oppositeDirection", offset < 0), b("flipNormal", False)])
    return 'qCreatedBy(makeId("%s"), EntityType.FACE)' % PLANES[offset]


def cube(name, x, y, zc=0, half=10, hx=None):
    """A box centred at (x, y, zc): 2*hx (default 2*half) along X, 2*half along Y and Z."""
    hx = half if hx is None else hx
    plane = TOP if zc == 0 else plane_at("Plane z = %g" % zc, zc)
    sk = sketch(name + " (sketch)", plane, polyline("r", [(x - hx, y - half), (x + hx, y - half), (x + hx, y + half), (x - hx, y + half)], closed=True))
    return feature(name, "extrude", [
        en("bodyType", "ExtendedToolBodyType", "SOLID"), en("operationType", "NewBodyOperationType", "NEW"),
        q("entities", 'qSketchRegion(makeId("%s"))' % sk), en("endBound", "BoundingType", "BLIND"),
        num("depth", "%g mm" % (2 * half)), b("symmetric", True)])


def connector(name, x, y):
    sk = sketch(name + " (sketch)", TOP, [point("p", *mmv(x, y))])
    return feature(name, "mateConnector", [
        en("originType", "OriginCreationType", "ON_ENTITY"), q("originQuery", 'qCreatedBy(makeId("%s"), EntityType.VERTEX)' % sk),
        en("entityInferenceType", "EntityInferenceType", "POINT"), b("allowOwnerEntity", True), b("requireOwnerPart", False)])


# ---- the feature under test ----
def extra(distance, mode="NONE", text="", target=None):
    """One additional copy: `distance` along the path, or (target given) to the path point nearest it."""
    return {"btType": "BTMArrayParameterItem-1843", "parameters": [
        en("copyMoveMode", "MoveToMode", "NEAREST" if target else "DISTANCE", NS),
        q("copyTarget", *([target] if target else [])), num("copyDistance", "%g mm" % distance),
        en("copyNameMode", "MoveNameMode", mode, NS), s("copyName", text)]}


def move(name, bodies, path, dist, given=(), enums=None, extras=None):
    """An instance of Move Along Edge; parameters not given take the spec defaults (correction 38)."""
    given = {p["parameterId"]: p for p in [q("moveBodies", bodies), q("moveEdge", path), num("moveDist", dist)] + list(given)}
    enums = enums or {}
    params = []
    for p in SPEC["parameters"]:
        pid = p["parameterId"]
        d = p.get("defaultValue")
        if pid == "extraCopies":
            params.append({"btType": "BTMParameterArray-2025", "parameterId": pid, "items": extras or []})
        elif pid in given:
            params.append(given[pid])
        elif pid in enums:
            e = copy.deepcopy(d)
            e["parameterId"] = pid
            e["value"] = enums[pid]
            params.append(e)
        elif isinstance(d, dict):
            params.append(dict(d, parameterId=pid))
    return feature(name, "myFeature", params, NS)


def part_name(fid):
    script = ('function(context is Context, queries) { return getProperty(context, { "entity" : qCreatedBy(makeId("%s"), EntityType.BODY), '
              '"propertyType" : PropertyType.NAME }); }' % fid)
    r = c.post(f"{BASE}/featurescript", json_data={"script": script})
    found = [v for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))) if not v.startswith("BTFSValue")]
    if not found:
        raise SystemExit("could not read the name of %s: %s" % (fid, r.get("notices")))
    return found[0]


# M1 line
l1 = sketch("M1 path: line x -50..300", TOP, polyline("l", [(-50, 0), (300, 0)]))
c1 = cube("M1 cube at (0, 0, 0)", 0, 0)
move("M1 line, cube moved 100 -> centroid (100, 0, 0)", body(c1), edges(l1), "100 mm")

# M2 composite-curve wire
l2 = sketch("M2 path sketch: line x 1000..1200 + R100 arc to (1300, 100)", TOP,
            polyline("l", [(1000, 0), (1200, 0)]) + [arc("a", *mmv(1200, 100, 100), -math.pi / 2, 0)])
w2 = feature("M2 composite curve (wire)", "compositeCurve", [q("edges", edges(l2))])
c2 = cube("M2 cube at (1000, 0, 0)", 1000, 0)
move("M2 wire, 278.54 -> on the arc at 45 deg (1270.71, 29.29, 0)", body(c2), body(w2), "278.5398 mm")

# M3 copies + names
l3 = sketch("M3 path: line x 2000..2400", TOP, polyline("l", [(2000, 0), (2400, 0)]))
c3 = cube("M3 cube at (2000, 0, 0)", 2000, 0)
m3_given = [b("copyBodies", True), s("nameText", "M3 main")]
m3_extras = [extra(50, "PREFIX", "A_"), extra(150, "SUFFIX", "_B"), extra(250)]
m3 = "M3 copies: main 100 'M3 main', 50 prefix 'A_', 150 suffix '_B', 250 unnamed -> cube stays, 4 copies"
move(m3, body(c3), edges(l3), "100 mm", m3_given, {"nameMode": "NEW_NAME"}, m3_extras)
# The editing logic that stores the source names does not run through the REST API; store what it would.
move(m3, body(c3), edges(l3), "100 mm", m3_given + [s("sourceNames", part_name(c3))], {"nameMode": "NEW_NAME"}, m3_extras)

# M3b prefix without stored names
l3b = sketch("M3b path: line x 3000..3400", TOP, polyline("l", [(3000, 0), (3400, 0)]))
c3b = cube("M3b cube at (3000, 0, 0)", 3000, 0)
move("M3b prefix 'P_' with no stored source names -> WARNING, copy named 'P_'", body(c3b), edges(l3b), "100 mm",
     [b("copyBodies", True), s("nameText", "P_")], {"nameMode": "PREFIX"})

# M4 mate connector as points
l4 = sketch("M4 path: line x 4000..4400", TOP, polyline("l", [(4000, 0), (4400, 0)]))
mc4 = connector("M4 mate connector at (4000, 0, 0)", 4000, 0)
move("M4 connector as points: 100 'M4 P0', copy 200 'M4 P1' -> 2 points, connector stays", body(mc4), edges(l4), "100 mm",
     [b("copyBodies", True), b("mcAsPoints", True), s("nameText", "M4 P0")], {"nameMode": "NEW_NAME"}, [extra(200, "NEW_NAME", "M4 P1")])

# M5 mate connector moved
l5 = sketch("M5 path: line x 5000..5400", TOP, polyline("l", [(5000, 0), (5400, 0)]))
mc5 = connector("M5 mate connector at (5000, 0, 0)", 5000, 0)
move("M5 connector moved 100 -> origin (5100, 0, 0)", body(mc5), edges(l5), "100 mm")

# M6 closed loop
l6 = sketch("M6 path: closed square 200 about (6000, 0)", TOP, polyline("s", [(5900, -100), (6100, -100), (6100, 100), (5900, 100)], closed=True))
c6 = cube("M6 cube at (6000, -100, 0)", 6000, -100)
move("M6 closed loop (perimeter 800), 900 -> wraps to a corner (6100 or 5900, -100)", body(c6), edges(l6), "900 mm")

# M7 / M8 S-curve: R100 arc turning left, then R100 arc turning right (inflection at (x0+100, 100))
for tag, x0, mode, want in [("M7", 7000, "FRENET", -30), ("M8", 8000, "TRANSPORT", 30)]:
    ls = sketch("%s path: S-curve (%d, 0) -> (%d, 200), inflection at (%d, 100)" % (tag, x0, x0 + 200, x0 + 100), TOP,
                [arc("a", *mmv(x0, 100, 100), -math.pi / 2, 0), arc("b", *mmv(x0 + 200, 100, 100), math.pi / 2, math.pi)])
    cs = cube("%s cube at (%d, 0, 30)" % (tag, x0), x0, 0, zc=30)
    move("%s S-curve, %s, 314.16 -> centroid (%d, 200, %d)" % (tag, mode.lower(), x0 + 200, want), body(cs), edges(ls), "314.159265 mm",
         [], {"orientation": mode})

# M9 flip + extrapolation past the start
l9 = sketch("M9 path: line x 9000..9200", TOP, polyline("l", [(9000, 0), (9200, 0)]))
c9 = cube("M9 cube at (9050, 0, 0)", 9050, 0)
move("M9 flipped, 100 back past the path start -> centroid (8950, 0, 0)", body(c9), edges(l9), "100 mm", [b("flipDirection", True)])

# M10 two sketch edges, the second drawn toward the corner
l10 = sketch("M10 path: (10000, 0) -> (10100, 0), second line drawn (10100, 100) -> (10100, 0)", TOP,
             [seg("a", *mmv(10000, 0, 10100, 0)), seg("b", *mmv(10100, 100, 10100, 0))])
c10 = cube("M10 cube at (10020, 0, 0)", 10020, 0)
move("M10 two edges, second reversed, 130 -> centroid (10100, 50, 0)", body(c10), edges(l10), "130 mm")

# M12 sketch edge -> wire
l12 = sketch("M12 path: line x 12000..12400", TOP, polyline("l", [(12000, 0), (12400, 0)]))
s12 = sketch("M12 source sketch: line (12000, 30) -> (12000, 50)", TOP, polyline("l", [(12000, 30), (12000, 50)]))
move("M12 sketch edge, 100 'M12 wire' -> wire centred (12100, 40, 0), sketch stays", edges(s12), edges(l12), "100 mm", [s("nameText", "M12 wire")], {"nameMode": "NEW_NAME"})

# M13 sketch vertex -> points
l13 = sketch("M13 path: line x 13000..13400", TOP, polyline("l", [(13000, 0), (13400, 0)]))
s13 = sketch("M13 source sketch: point (13000, 30)", TOP, [point("p", *mmv(13000, 30))])
move("M13 sketch point, 100 'M13 P0', copy 200 'M13 P1' -> points (13100, 30) and (13200, 30)", 'qCreatedBy(makeId("%s"), EntityType.VERTEX)' % s13,
     edges(l13), "100 mm", [b("copyBodies", True), s("nameText", "M13 P0")], {"nameMode": "NEW_NAME"}, [extra(200, "NEW_NAME", "M13 P1")])

# M14 mixed selection: a sketch edge before a body; the body's suffix must use the body's own name
l14 = sketch("M14 path: line x 14000..14400", TOP, polyline("l", [(14000, 0), (14400, 0)]))
s14 = sketch("M14 source sketch: line (14000, 30) -> (14000, 50)", TOP, polyline("l", [(14000, 30), (14000, 50)]))
c14 = cube("M14 cube at (14000, 0, 0)", 14000, 0)
move("M14 sketch edge + cube, copy 100, suffix '_S' -> wire '_S' at (14100, 40), cube copy '<name>_S' at (14100, 0)",
     edges(s14), edges(l14), "100 mm", [q("moveBodies", edges(s14), body(c14)), b("copyBodies", True), s("nameText", "_S"), s("sourceNames", part_name(c14))],
     {"nameMode": "SUFFIX"})

# M15 nearest point to a sketch point (main) and to another (copy), plus a distance copy
l15 = sketch("M15 path: line x 15000..15400", TOP, polyline("l", [(15000, 0), (15400, 0)]))
t15 = sketch("M15 targets: points (15250, 60) and (15320, -40)", TOP, [point("p", *mmv(15250, 60)), point("q", *mmv(15320, -40))])
c15 = cube("M15 cube at (15000, 0, 0)", 15000, 0)
target = 'qContainsPoint(qCreatedBy(makeId("%s"), EntityType.VERTEX), vector(%g, %g, 0) * millimeter)'
move("M15 nearest to (15250, 60), copies nearest to (15320, -40) and 50 -> 15250, 15320, 15050",
     body(c15), edges(l15), "100 mm", [q("moveTarget", target % (t15, 15250, 60)), b("copyBodies", True)], {"moveMode": "NEAREST"},
     [extra(0, target=target % (t15, 15320, -40)), extra(50)])

# M16 nearest point to a sketch line crossing an arc path: the crossing
l16 = sketch("M16 path: R100 arc centre (16000, 100) from (16000, 0) to (16100, 100)", TOP, [arc("a", *mmv(16000, 100, 100), -math.pi / 2, 0)])
t16 = sketch("M16 target: line x = 16050 crossing the arc", TOP, polyline("l", [(16050, -50), (16050, 150)]))
c16 = cube("M16 cube at (16000, 0, 0)", 16000, 0)
move("M16 nearest to a line crossing the arc -> the crossing (16050, 13.3975, 0)", body(c16), edges(l16), "0 mm",
     [q("moveTarget", edges(t16))], {"moveMode": "NEAREST"})

# M17-M19 Apply modes on a quarter arc (R100, centre (x0, 100), from (x0, 0) to (x0 + 100, 100)); 40 x 20 blocks
QUARTER = "157.0796327 mm"
for tag, x0, apply_mode, expect in [("M17", 17000, "TRANSLATE", "translate only -> centroid (17100, 100, 0), still 40 along X"),
                                    ("M18", 18000, "ROTATE", "rotate only -> centroid stays (18000, 0, 0), turned 90 deg: 40 along Y")]:
    la = sketch("%s path: R100 quarter arc centre (%d, 100)" % (tag, x0), TOP, [arc("a", *mmv(x0, 100, 100), -math.pi / 2, 0)])
    blk = cube("%s block 40 x 20 at (%d, 0, 0)" % (tag, x0), x0, 0, hx=20)
    move("%s quarter arc, %s" % (tag, expect), body(blk), edges(la), QUARTER, [], {"applyMode": apply_mode})

l19 = sketch("M19 path: R100 quarter arc centre (19000, 100)", TOP, [arc("a", *mmv(19000, 100, 100), -math.pi / 2, 0)])
t19 = sketch("M19 target: point (19150, 100)", TOP, [point("p", *mmv(19150, 100))])
b19 = cube("M19 block 40 x 20 at (19000, 0, 0)", 19000, 0, hx=20)
move("M19 rotate only, nearest to (19150, 100) = arc end -> centroid stays (19000, 0, 0), 40 along Y", body(b19), edges(l19), "0 mm",
     [q("moveTarget", 'qCreatedBy(makeId("%s"), EntityType.VERTEX)' % t19)], {"applyMode": "ROTATE", "moveMode": "NEAREST"})

# M20 mate connector picked through its vertex (as the dialog hands it over), points OFF -> the connector moves
l20 = sketch("M20 path: line x 20000..20400", TOP, polyline("l", [(20000, 0), (20400, 0)]))
mc20 = connector("M20 mate connector at (20000, 0, 0)", 20000, 0)
move("M20 connector picked by its vertex, points off, named 'M20 MC' -> connector moves to (20100, 0, 0), no points",
     'qOwnedByBody(%s, EntityType.VERTEX)' % body(mc20), edges(l20), "100 mm", [s("nameText", "M20 MC")], {"nameMode": "NEW_NAME"})

# M11 disconnected edges: temporary instance, must fail
l11 = sketch("M11 path: two lines with a gap", TOP, polyline("l", [(11000, 0), (11100, 0)]) + polyline("m", [(11200, 0), (11300, 0)]))
c11 = cube("M11 cube at (11000, 0, 0)", 11000, 0)
tmp = move("M11 temporary", body(c11), edges(l11), "50 mm")
status = feature_status(BASE, _TREE, tmp, client=c)   # from the insert's own response
delete_feature(BASE, tmp, _TREE, client=c)
print("M11 disconnected edges -> %s (%s)" % (status, "PASS" if status == "ERROR" else "FAIL: expected ERROR"))
print("studio", E)
