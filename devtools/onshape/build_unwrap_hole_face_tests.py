"""Build the Unwrap hole and Faces-mode tests (2026-09-30) as real features in the Part Studio "Unwrap hole & face tests"
(a copy of "Unwrap_Testing Copy 2" with its Unwrap features deleted: it keeps the derived FULL_BASELINE, REF_WIRE,
CORE, topsheet and Ski_Top_Surf). Every run deletes the features after "Boolean 1" and rebuilds them. Names carry the
expected result. Check with check_unwrap_regression.py (it reads this studio too).

Fixtures (standard features):
  HA block      Ski_Top_Surf's centre face thickened 10 mm (down), trimmed to x 450..750, y 5..55 (an extrude
                intersect): its top is Wrapped_profile's extrusion, curved over FULL_BASELINE, straight over REF_WIRE.
  HA block holes  world-vertical, cut from the Top plane: round (500, 30) r4, slot (600..630, 30) r4, counterbore
                through hole (690, 30) r3; pockets 4 mm below the lowest top point: counterbore (690, 30) r6 and a
                blind pocket (730, 30) r6.
  HA CORE holes world-vertical through the CORE: (141, 0) r3 in REF_WIRE's tail arc, (600, 0) r4 (straight on
                REF_WIRE, curved on FULL_BASELINE).

usage (repo root): FS_SYNC_TIMEOUT=300 PYTHONPATH=. python devtools/onshape/build_unwrap_hole_face_tests.py
"""
import copy
import json
import math
import re

from sync.core.client import OnshapeClient

c = OnshapeClient()
D, W = "f61d2c000ab2d1240776342e", "5b11f323ab31b04cba8b36ef"
E = "56c29f15db6047862561aa7b"   # "Unwrap hole & face tests"
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
ELEMENTS = c.list_elements(D, W)


def tab(name):
    t = [e for e in ELEMENTS if e["name"] == name][0]
    return t, "e%s::m%s" % (t["id"], t["microversionId"])


def run(script):
    r = c.post(f"{BASE}/featurescript", json_data={"script": script})
    return re.findall(r'"value":\s*"([^"]+)"', json.dumps(r.get("result")))


def eval_ids(expr):
    script = ("function(context is Context, queries) { var out = []; for (var e in evaluateQuery(context, %s)) "
              "{ out = append(out, e.transientId); } return out; }" % expr)
    return [x for x in run(script) if x not in ("BTFSValueString", "BTFSValueArray")]


def eval_number(expr):
    script = "function(context is Context, queries) { return toString(%s); }" % expr
    vals = [x for x in run(script) if x not in ("BTFSValueString",)]
    return float(vals[0])


def sel(pid, expr):
    ids = eval_ids(expr)
    if not ids:
        raise RuntimeError("selection for %s is empty: %s" % (pid, expr))
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "", "deterministicIds": ids}]}


def q(pid, expr):
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "query=%s;" % expr}]}


def en(pid, enum, value, ns=""):
    return {"btType": "BTMParameterEnum-145", "parameterId": pid, "enumName": enum, "value": value, "namespace": ns}


def num(pid, expr):
    return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": expr, "isInteger": False}


def b(pid, v):
    return {"btType": "BTMParameterBoolean-144", "parameterId": pid, "value": v}


def post(feature):
    f = c.get(f"{BASE}/features")
    body = {"btType": "BTFeatureDefinitionCall-1406", "feature": feature,
            "serializationVersion": f["serializationVersion"], "sourceMicroversion": f["sourceMicroversion"]}
    r = c.post(f"{BASE}/features", body)
    print("%-110s %s" % (feature["name"][:110], r.get("featureState", {}).get("featureStatus")))
    return r["feature"]["featureId"]


def feature(name, ftype, params):
    return post({"btType": "BTMFeature-134", "featureType": ftype, "name": name, "parameters": params})


def circle(eid, cx, cy, r):
    return {"btType": "BTMSketchCurve-4", "entityId": eid, "centerId": eid + ".center", "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryCircle-115", "radius": r / 1000, "xCenter": cx / 1000, "yCenter": cy / 1000,
                         "xDir": 1.0, "yDir": 0.0, "clockwise": False}}


def seg(eid, x0, y0, x1, y1):
    L = math.hypot(x1 - x0, y1 - y0) / 1000
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": 0.0, "endParam": L, "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryLine-117", "pntX": x0 / 1000, "pntY": y0 / 1000,
                         "dirX": (x1 - x0) / 1000 / L, "dirY": (y1 - y0) / 1000 / L}}


def rect(prefix, x0, y0, x1, y1):
    return [seg(prefix + "a", x0, y0, x1, y0), seg(prefix + "b", x1, y0, x1, y1), seg(prefix + "c", x1, y1, x0, y1), seg(prefix + "d", x0, y1, x0, y0)]


def sketch(name, plane_expr, entities):
    return post({"btType": "BTMSketch-151", "featureType": "newSketch", "name": name,
                 "parameters": [q("sketchPlane", plane_expr)], "entities": entities, "constraints": []})


def cut(name, sketch_id, scope_expr):
    return feature(name, "extrude", [
        en("bodyType", "ExtendedToolBodyType", "SOLID"), en("operationType", "NewBodyOperationType", "REMOVE"),
        q("entities", 'qSketchRegion(makeId("%s"))' % sketch_id), en("endBound", "BoundingType", "THROUGH_ALL"),
        b("defaultScope", False), sel("booleanScope", scope_expr)])


UNWRAP_SPEC = None


def unwrap(name, given, enums):
    global UNWRAP_SPEC
    t, ns = tab("unwrap")
    if UNWRAP_SPEC is None:
        UNWRAP_SPEC = [x for x in c.get(f"/api/v10/featurestudios/d/{D}/w/{W}/e/{t['id']}/featurespecs")["featureSpecs"]
                       if x["featureType"] == "unwrap"][0]
    given = {p["parameterId"]: p for p in given}
    params = []
    for p in UNWRAP_SPEC["parameters"]:
        d = p.get("defaultValue")
        if not isinstance(d, dict):
            continue
        pid = p["parameterId"]
        if pid in given:
            params.append(given[pid])
        elif pid in enums:
            e = copy.deepcopy(d)
            e["parameterId"] = pid
            e["value"] = enums[pid]
            params.append(e)
        else:
            params.append(dict(d, parameterId=pid))
    return post({"btType": "BTMFeature-134", "featureType": "unwrap", "name": name, "namespace": ns, "parameters": params})


# ---- clean slate: everything after "Boolean 1" ----
feats = c.get(f"{BASE}/features")["features"]
keep = [x["name"] for x in feats][: [x["name"] for x in feats].index("Boolean 1") + 1]
for f in reversed(feats):
    if f["name"] not in keep:
        c._request("DELETE", f"{BASE}/features/featureid/{f['featureId']}")

FULL_BASELINE = 'qTransient("RhRL")'
REF_WIRE = 'qTransient("RhRD")'
TOP = 'qCreatedBy(makeId("Top"), EntityType.FACE)'
MC = 'qTransient("SrjLB")'
CORE = 'qTransient("Rrjn")'
SKI_TOP_CENTRE = 'qTransient("RhRC")'

# ---- fixtures ----
thick = feature("HA block: Ski_Top_Surf centre face thickened 10 mm", "thicken", [
    sel("entities", SKI_TOP_CENTRE), b("midplane", False), num("thickness1", "10 mm"), b("oppositeDirection", False),
    num("thickness2", "0 mm"), b("keepTools", True), en("operationType", "NewBodyOperationType", "NEW")])
BLOCK = 'qCreatedBy(makeId("%s"), EntityType.BODY)' % thick
trim_sk = sketch("HA block trim (sketch)", TOP, rect("t", 450, 5, 750, 55))
feature("HA block: trimmed to x 450..750, y 5..55", "extrude", [
    en("bodyType", "ExtendedToolBodyType", "SOLID"), en("operationType", "NewBodyOperationType", "INTERSECT"),
    q("entities", 'qSketchRegion(makeId("%s"))' % trim_sk), en("endBound", "BoundingType", "BLIND"), num("depth", "200 mm"),
    b("symmetric", True), b("defaultScope", False), sel("booleanScope", BLOCK)])
holes_sk = sketch("HA block through holes (sketch)", TOP,
                  [circle("h1", 500, 30, 4), circle("s1", 600, 30, 4), circle("s2", 630, 30, 4)] + rect("sr", 600, 26, 630, 34)
                  + [circle("cb", 690, 30, 3)])
cut("HA block: through holes (round, slot, counterbore bore), world vertical", holes_sk, BLOCK)
top_z = min(eval_number('evRaycast(context, { "entities" : %s, "ray" : line(vector(%s, 0.03, 0.2) * meter, vector(0, 0, -1)) })[0].intersection[2] / meter'
                        % (BLOCK, x)) for x in (0.684, 0.696, 0.724, 0.736))
floor_mm = round((top_z - 0.004) * 1000, 3)
print("block top (lowest at the pockets) z = %.4f mm, pocket floor at %.3f mm" % (top_z * 1000, floor_mm))
pl = feature("HA pocket floor plane (Top + %.3f mm)" % floor_mm, "cPlane", [
    q("entities", TOP), en("cplaneType", "CPlaneType", "OFFSET"), num("offset", "%.3f mm" % floor_mm), b("oppositeDirection", False)])
pockets_sk = sketch("HA block pockets (sketch)", 'qCreatedBy(makeId("%s"), EntityType.FACE)' % pl,
                    [circle("cb2", 690, 30, 6), circle("p1", 730, 30, 6)])
cut("HA block: pockets (counterbore r6, blind r6), 4 mm below the top", pockets_sk, BLOCK)
core_sk = sketch("HA CORE holes (sketch)", TOP, [circle("c1", 141, 0, 3), circle("c2", 600, 0, 4)])
cut("HA CORE: holes at x 141 (REF_WIRE tail arc) and x 600", core_sk, CORE)

# ---- Task A: holes in Part mode ----
common = [sel("alignPoint", MC), sel("origin", TOP), b("debugKeepLengthCurves", False)]
unwrap("Unwrap HA block, round hole + slot + pocket + counterbore (part, along FULL_BASELINE) - expect OK, 5 holes cut",
       [sel("parts", BLOCK), sel("reference", FULL_BASELINE)] + common, {"unwrapType": "PART", "preserveLength": "REFERENCE"})
unwrap("Unwrap HA block with holes (part, along REF_WIRE, straight span) - expect OK, moved rigidly, holes kept",
       [sel("parts", BLOCK), sel("reference", REF_WIRE)] + common, {"unwrapType": "PART", "preserveLength": "REFERENCE"})
unwrap("Unwrap HA CORE with 2 holes (part, along FULL_BASELINE, keep faces) - expect OK, 2 holes cut",
       [sel("parts", CORE), sel("reference", FULL_BASELINE), num("shapeTolerance", "0.01 mm")] + common,
       {"unwrapType": "PART", "preserveLength": "REFERENCE", "partFaces": "KEEP"})
unwrap("Unwrap HA CORE with 2 holes (part, along REF_WIRE) - expect OK, tail hole cut, mid hole moved rigidly",
       [sel("parts", CORE), sel("reference", REF_WIRE)] + common, {"unwrapType": "PART", "preserveLength": "REFERENCE"})

# ---- Task B: faces ----
unwrap("Unwrap HB Ski_Top_Surf sheet (faces, along FULL_BASELINE) - expect OK, 1 sheet, 3 faces, 10 edges",
       [sel("faces", 'qTransient("RhRT")'), sel("reference", FULL_BASELINE)] + common, {"unwrapType": "FACES", "preserveLength": "REFERENCE"})
unwrap("Unwrap HB Topsheet top face (faces, along FULL_BASELINE) - expect OK, 1 face, 16 edges",
       [sel("faces", 'qTransient("SlRmH")'), sel("reference", FULL_BASELINE)] + common, {"unwrapType": "FACES", "preserveLength": "REFERENCE"})
unwrap("Unwrap HB Topsheet step face (faces, along FULL_BASELINE) - expect OK, fitted surface",
       [sel("faces", 'qTransient("SlR2E")'), sel("reference", FULL_BASELINE)] + common, {"unwrapType": "FACES", "preserveLength": "REFERENCE"})
unwrap("Unwrap HB block top face with 4 hole loops (faces, along FULL_BASELINE) - expect OK, 1 face, 4 inner loops",
       [sel("faces", 'qContainsPoint(qOwnedByBody(%s, EntityType.FACE), evRaycast(context, { "entities" : %s, "ray" : line(vector(0.55, 0.03, 0.2) * meter, vector(0, 0, -1)) })[0].intersection)'
            % (BLOCK, BLOCK)), sel("reference", FULL_BASELINE)] + common, {"unwrapType": "FACES", "preserveLength": "REFERENCE"})
unwrap("Unwrap HB CORE bottom face with 2 hole loops (faces, along REF_WIRE) - expect OK, planar, 2 inner loops",
       [sel("faces", 'qContainsPoint(qOwnedByBody(%s, EntityType.FACE), evRaycast(context, { "entities" : %s, "ray" : line(vector(0.62, 0.0, -0.2) * meter, vector(0, 0, 1)) })[0].intersection)'
            % (CORE, CORE)), sel("reference", REF_WIRE)] + common, {"unwrapType": "FACES", "preserveLength": "REFERENCE"})
print("studio", E)
