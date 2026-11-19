"""Build the Mate connector + tests as real features in the "MCP tests" Part Studio of the multi_mate_connector document
(created when missing; every run deletes its features and rebuilds them). Check with check_mcp_tests.py.

Fixture (Top, millimetres):
  WALL  surface wall along (0,0) -> (200,0), 100 high (a plane, normal (0,-1,0))
  E1    straight edge (20,30) -> (220,30)
  P     point (100,80)        Q  point (300,30)        R  point (556.57,56.57)
  ARC   quarter circle, centre (500,0), r 50, from 0 to 90 degrees
  O     mate connector at the world origin             BOX  10 mm cube at the origin (for Transform)

C1  Point P, Z world Z, X automatic                       -> origin (100,80,0), Z (0,0,1), X (1,0,0)
C2  Edge E1 nearest to P                                  -> origin (100,30,0)
C3  Edge E1, 15 mm from the end nearest P                 -> origin (35,30,0)
C4  Edge E1, 50 mm from the point nearest P, TOWARD Q     -> origin (150,30,0)
C5  Edge E1, 50 mm from the point nearest P, AWAY from Q  -> origin (50,30,0)
C6  Face WALL nearest to P, Z = normal of the wall        -> origin (100,0,0), Z (0,-1,0)
C7  Point P, Z tangent of E1                              -> |Z| along X; X automatic goes to world Y
C8  Edge ARC nearest to R, Z = normal of the arc          -> origin (535.355,35.355,0), Z (-0.7071,-0.7071,0)
C9  Point P, moves (10,20,5), rotate 90 about Z           -> origin (110,100,5), X (0,1,0), Y (-1,0,0)
C10 Point P, Z world Z, X vector (0,1,0)                  -> X (0,1,0)
C11 Point P, Z world X, X vector (1,1,0)                  -> X squared to (0,1,0) (INFO)
C12 Point P, Z world Z, X vector (0,0,1)                  -> ERROR (X parallel to Z)
C13 Build at P, side 20                                   -> one surface body, 3 faces of 400 mm2
C14 Transform BOX from O to the frame at P                -> BOX bounding box (100,80,0)-(110,90,10)

usage (repo root): PYTHONPATH=. python devtools/onshape/build_mcp_tests.py
"""
import json
import math

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("multi_mate_connector/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
STUDIO = "MCP tests"

els = c.list_elements(D, W)
studios = {e["name"]: e["id"] for e in els if e["elementType"] == "PARTSTUDIO"}
if STUDIO not in studios:
    studios[STUDIO] = c.post(f"/api/v10/partstudios/d/{D}/w/{W}", {"name": STUDIO})["id"]
E = studios[STUDIO]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TAB = [e for e in els if e["elementType"] == "FEATURESTUDIO" and e["name"] == "mate_connector_plus"][0]
NS = "e%s::m%s" % (TAB["id"], TAB["microversionId"])


def q(pid, *exprs):
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "query=%s;" % e} for e in exprs]}


def en(pid, enum, value):
    return {"btType": "BTMParameterEnum-145", "parameterId": pid, "enumName": enum, "value": value, "namespace": NS}


def num(pid, expr):
    return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": expr, "isInteger": False}


def b(pid, v):
    return {"btType": "BTMParameterBoolean-144", "parameterId": pid, "value": v}


for f in reversed(c.get(f"{BASE}/features")["features"]):
    c._request("DELETE", f"{BASE}/features/featureid/{f['featureId']}")
tree = c.get(f"{BASE}/features")


def post(feature):
    body = {"btType": "BTFeatureDefinitionCall-1406", "feature": feature,
            "serializationVersion": tree["serializationVersion"], "sourceMicroversion": tree["sourceMicroversion"]}
    r = c.post(f"{BASE}/features", body)
    tree["sourceMicroversion"] = r.get("sourceMicroversion", tree["sourceMicroversion"])
    print("%-96s %s" % (feature["name"][:96], r.get("featureState", {}).get("featureStatus")))
    return r["feature"]["featureId"]


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


def mm(*xs):
    return [x / 1000.0 for x in xs]


def sketch(name, entities):
    return post({"btType": "BTMSketch-151", "featureType": "newSketch", "name": name,
                 "parameters": [q("sketchPlane", 'qCreatedBy(makeId("Top"), EntityType.FACE)')], "entities": entities, "constraints": []})


WALL_SK = sketch("WALL (sketch)", [seg("w", *mm(0, 0, 200, 0))])
WALL = post({"btType": "BTMFeature-134", "featureType": "extrude", "name": "WALL surface (0,0)-(200,0), 100 high", "parameters": [
    en("bodyType", "ExtendedToolBodyType", "SURFACE") | {"namespace": ""}, en("operationType", "NewBodyOperationType", "NEW") | {"namespace": ""},
    q("surfaceEntities", 'qCreatedBy(makeId("%s"), EntityType.EDGE)' % WALL_SK), en("endBound", "BoundingType", "BLIND") | {"namespace": ""},
    num("depth", "100 mm")]})
E1_SK = sketch("E1 edge (20,30)-(220,30)", [seg("e", *mm(20, 30, 220, 30))])
ARC_SK = sketch("ARC centre (500,0) r 50, 0 to 90 deg", [arc("a", *mm(500, 0, 50), 0.0, math.pi / 2)])
PTS_SK = sketch("Points P (100,80), Q (300,30), R (556.57,56.57), O (0,0)",
                [point("P", *mm(100, 80)), point("Q", *mm(300, 30)), point("R", *mm(556.57, 56.57)), point("O", *mm(0, 0))])
BOX_SK = sketch("BOX (sketch) 10 x 10 at the origin", [seg("b%d" % i, *mm(*s)) for i, s in enumerate([(0, 0, 10, 0), (10, 0, 10, 10), (10, 10, 0, 10), (0, 10, 0, 0)])])
BOX = post({"btType": "BTMFeature-134", "featureType": "extrude", "name": "BOX 10 mm cube at the origin", "parameters": [
    en("bodyType", "ExtendedToolBodyType", "SOLID") | {"namespace": ""}, en("operationType", "NewBodyOperationType", "NEW") | {"namespace": ""},
    q("entities", 'qSketchRegion(makeId("%s"))' % BOX_SK), en("endBound", "BoundingType", "BLIND") | {"namespace": ""}, num("depth", "10 mm")]})
O_MC = post({"btType": "BTMFeature-134", "featureType": "mateConnector", "name": "O mate connector at the origin", "parameters": [
    en("originType", "OriginCreationType", "ON_ENTITY") | {"namespace": ""},
    q("originQuery", 'qContainsPoint(qCreatedBy(makeId("%s"), EntityType.VERTEX), vector(0, 0, 0) * millimeter)' % PTS_SK),
    en("entityInferenceType", "EntityInferenceType", "POINT") | {"namespace": ""}, b("allowOwnerEntity", True), b("requireOwnerPart", False)]})


def pt(name):
    pos = {"P": (100, 80), "Q": (300, 30), "R": (556.57, 56.57), "O": (0, 0)}[name]
    return 'qContainsPoint(qCreatedBy(makeId("%s"), EntityType.VERTEX), vector(%s, %s, 0) * millimeter)' % (PTS_SK, pos[0], pos[1])


E1 = 'qCreatedBy(makeId("%s"), EntityType.EDGE)' % E1_SK
ARC = 'qCreatedBy(makeId("%s"), EntityType.EDGE)' % ARC_SK
WALL_FACES = 'qCreatedBy(makeId("%s"), EntityType.FACE)' % WALL


def mcp(name, origin, z=None, x=None, mode="NEW", move=(0, 0, 0), rot=(0, 0, 0), extra=()):
    """origin: ('POINT', expr) | ('EDGE', edges, method, params...) | ('FACE', faces, near); z / x: ('WORLD', 'Z_AXIS') | ('VECTOR', x, y, z)
    | ('TANGENT', geometry) | ('NORMAL', geometry) | None (X: automatic)."""
    p = [en("mode", "McpMode", mode)]
    kind = origin[0]
    p.append(en("originType", "McpOriginType", kind))
    if kind == "POINT":
        p.append(q("originPoint", origin[1]))
    elif kind == "EDGE":
        p += [q("originEdges", origin[1]), en("edgeMethod", "McpEdgeMethod", origin[2])]
        if origin[2] == "NEAREST":
            p.append(q("edgeNearTo", origin[3]))
        elif origin[2] == "DISTANCE_FROM_END":
            p += [q("edgeEndNear", origin[3]), num("edgeEndDistance", origin[4])]
        else:
            p += [q("edgeFromEntity", origin[3]), num("edgeFromDistance", origin[4]), en("edgeDirection", "McpDirection", origin[5]), q("edgeDirectionEntity", origin[6])]
    else:
        p += [q("originFaces", origin[1]), en("faceMethod", "McpFaceMethod", "NEAREST"), q("faceNearTo", origin[2])]

    def axis(prefix, spec, enum):
        if spec is None:
            return [en(prefix + "Type", enum, "AUTOMATIC")]
        out = [en(prefix + "Type", enum, spec[0])]
        if spec[0] == "WORLD":
            out.append(en(prefix + "World", "McpWorldAxis", spec[1]))
        elif spec[0] == "VECTOR":
            out += [num(prefix + "VecX", str(spec[1])), num(prefix + "VecY", str(spec[2])), num(prefix + "VecZ", str(spec[3]))]
        else:
            out.append(q(prefix + "Geometry", spec[1]))
            out.append(q(prefix + "Hint"))
        return out

    p += axis("z", z or ("WORLD", "Z_AXIS"), "McpAxisType") + [b("zFlip", False)]
    p += axis("x", x, "McpXAxisType") + [b("xFlip", False)]
    p += [num("modOffsetX", "%g mm" % move[0]), num("modOffsetY", "%g mm" % move[1]), num("modOffsetZ", "%g mm" % move[2]),
          num("modRotX", "%g deg" % rot[0]), num("modRotY", "%g deg" % rot[1]), num("modRotZ", "%g deg" % rot[2])]
    p += list(extra)
    return post({"btType": "BTMFeature-134", "featureType": "mateConnectorPlus", "name": name, "namespace": NS, "parameters": p})


mcp("C1 point P, Z world Z, X automatic -> (100,80,0) Z (0,0,1) X (1,0,0)", ("POINT", pt("P")))
mcp("C2 edge E1 nearest P -> (100,30,0)", ("EDGE", E1, "NEAREST", pt("P")))
mcp("C3 edge E1 15 mm from the end nearest P -> (35,30,0)", ("EDGE", E1, "DISTANCE_FROM_END", pt("P"), "15 mm"))
mcp("C4 edge E1 50 mm from the point nearest P toward Q -> (150,30,0)", ("EDGE", E1, "DISTANCE_FROM_POINT", pt("P"), "50 mm", "TOWARD", pt("Q")))
mcp("C5 edge E1 50 mm from the point nearest P away from Q -> (50,30,0)", ("EDGE", E1, "DISTANCE_FROM_POINT", pt("P"), "50 mm", "AWAY_FROM", pt("Q")))
mcp("C6 face WALL nearest P, Z = wall normal -> (100,0,0) Z (0,-1,0)", ("FACE", WALL_FACES, pt("P")), z=("NORMAL", WALL_FACES))
mcp("C7 point P, Z tangent of E1 -> Z along X, X goes to Y", ("POINT", pt("P")), z=("TANGENT", E1))
mcp("C8 arc nearest R, Z = normal of the arc -> (535.355,35.355,0) Z (-0.7071,-0.7071,0)", ("EDGE", ARC, "NEAREST", pt("R")), z=("NORMAL", ARC))
mcp("C9 point P, move (10,20,5), rotate 90 about Z -> (110,100,5) X (0,1,0)", ("POINT", pt("P")), move=(10, 20, 5), rot=(0, 0, 90))
mcp("C10 point P, X vector (0,1,0) -> X (0,1,0)", ("POINT", pt("P")), x=("VECTOR", 0, 1, 0))
mcp("C11 point P, Z world X, X vector (1,1,0) -> X squared to (0,1,0)", ("POINT", pt("P")), z=("WORLD", "X_AXIS"), x=("VECTOR", 1, 1, 0))
mcp("C12 point P, Z world Z, X vector (0,0,1) -> ERROR (X parallel to Z)", ("POINT", pt("P")), x=("VECTOR", 0, 0, 1))
mcp("C13 build at P, side 20 -> 1 surface body, 3 faces of 400 mm2", ("POINT", pt("P")), mode="BUILD", extra=[num("buildLength", "20 mm")])
mcp("C14 transform BOX from O to the frame at P -> box at (100,80,0)-(110,90,10)", ("POINT", pt("P")), mode="TRANSFORM",
    extra=[q("fromConnector", 'qCreatedBy(makeId("%s"), EntityType.BODY)' % O_MC), q("transformBodies", 'qCreatedBy(makeId("%s"), EntityType.BODY)' % BOX)])
