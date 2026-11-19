"""Build the Trim curve + test cases as real features in smallTools' "Trim curve + tests" Part Studio
(created when missing; upsert by name). Fixtures are native features only. Checked by
check_trim_curve_plus_tests.py.

Every case has its own copy of a 3-edge wire (composite curve), offset 1000 mm along X per case:
line (x0, 0)->(x0+200, 0), R100 quarter arc to (x0+300, 100), line up to (x0+300, 300). Length 557.080.
Path arc length s: 0..200 line 1, 200..357.080 arc, 357.080..557.080 line 3.

  T1  split, at points (x0+100, -6) [6 mm off the curve] and (x0+300, 200)  -> 3 wires 100 / 357.080 / 100
  T2  split in place (single wire), same points                             -> 1 wire, 5 edges, 557.080
  T3  trim at point (x0+100, 0)                                             -> 1 wire 457.080
  T4  split, 150 from (x0+100, 0), also at the reference                    -> 3 wires 100 / 150 / 307.080
  T5  split, -50 from (x0+100, 0)                                           -> 2 wires 50 / 507.080
  T6  split, 50 from (x0+100, 0), opposite direction                        -> 2 wires 50 / 507.080
  T7  split at a mate connector at (x0+100, 0)                              -> 2 wires 100 / 457.080
  T8  split, 600 from (x0+100, 0)                                           -> ERROR (runs past the end)
  T9  split, 50 + additional 150 and -80 from (x0+100, 0)                   -> 4 wires 20 / 130 / 100 / 307.080

usage (repo root): PYTHONPATH=. python devtools/onshape/build_trim_curve_plus_tests.py
"""
import copy
import json
import math

from sync.core.client import OnshapeClient
import os

from devtools.onshape.fsapi import write_feature, delete_feature, cached_tree  # noqa: E402  (API budget: one feature-tree GET per studio)

c = OnshapeClient()
DOC = json.load(open("smallTools/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
STUDIO = "Trim curve + tests"
ELEMENTS = c.list_elements(D, W)
studios = {e["name"]: e["id"] for e in ELEMENTS if e["elementType"] == "PARTSTUDIO"}
if STUDIO not in studios:
    studios[STUDIO] = c.post(f"/api/v10/partstudios/d/{D}/w/{W}", {"name": STUDIO})["id"]
E = studios[STUDIO]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TAB = [e for e in ELEMENTS if e["name"] == "betterCurveTrim"][0]
NS = "e%s::m%s" % (TAB["id"], TAB["microversionId"])
SPEC = [x for x in c.get(f"/api/v10/featurestudios/d/{D}/w/{W}/e/{TAB['id']}/featurespecs")["featureSpecs"]
        if x["featureType"] == "betterCurveTrim"][0]

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


_TREE = {}   # feature tree of the test Part Studio, kept in step with each write (API budget)


def upsert(feature):
    if os.environ.get("BUILD_ONLY_NEW"):   # API budget: leave features that already exist untouched
        have = [x for x in cached_tree(BASE, _TREE, c)["features"] if x["name"] == feature["name"]]
        if have:
            return have[0]["featureId"]
    r = write_feature(BASE, feature, _TREE, client=c)
    print("%-100s %s" % (feature["name"][:100], r.get("featureState", {}).get("featureStatus")))
    return r["feature"]["featureId"]

def feature(name, ftype, params, ns=""):
    f = {"btType": "BTMFeature-134", "featureType": ftype, "name": name, "parameters": params}
    if ns:
        f["namespace"] = ns
    return upsert(f)


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


def sketch(name, entities):
    return upsert({"btType": "BTMSketch-151", "featureType": "newSketch", "name": name,
                   "parameters": [q("sketchPlane", TOP)], "entities": entities, "constraints": []})


TOP = 'qCreatedBy(makeId("Top"), EntityType.FACE)'


def sketch_edges(fid):
    return 'qConstructionFilter(qOwnedByBody(qBodyType(qCreatedBy(makeId("%s"), EntityType.BODY), BodyType.WIRE), EntityType.EDGE), ConstructionObject.NO)' % fid


def body(fid):
    return 'qCreatedBy(makeId("%s"), EntityType.BODY)' % fid


def vertices(fid):
    return 'qCreatedBy(makeId("%s"), EntityType.VERTEX)' % fid


def wire(tag, x0):
    sk = sketch("%s wire sketch: line, R100 arc, line (x0 %d)" % (tag, x0),
                [seg("l1", *mmv(x0, 0, x0 + 200, 0)), arc("a", *mmv(x0 + 200, 100, 100), -math.pi / 2, 0),
                 seg("l2", *mmv(x0 + 300, 100, x0 + 300, 300))])
    return feature("%s 3-edge wire (composite curve)" % tag, "compositeCurve", [q("edges", sketch_edges(sk))])


def points(tag, pts):
    return sketch("%s points %s" % (tag, " ".join("(%g, %g)" % p for p in pts)),
                  [point("p%d" % i, *mmv(*p)) for i, p in enumerate(pts)])


def connector(name, x, y):
    sk = sketch(name + " (sketch)", [point("p", *mmv(x, y))])
    return feature(name, "mateConnector", [
        en("originType", "OriginCreationType", "ON_ENTITY"), q("originQuery", vertices(sk)),
        en("entityInferenceType", "EntityInferenceType", "POINT"), b("allowOwnerEntity", True), b("requireOwnerPart", False)])


def extra_distances(pid, *mm):
    return {"btType": "BTMParameterArray-2025", "parameterId": pid, "items": [
        {"btType": "BTMArrayParameterItem-1843", "parameters": [num("extraDistance", "%g mm" % d)]} for d in mm]}


def trim(name, curves, given=(), enums=None):
    """An instance of Trim curve +; parameters not given take the spec defaults (correction 38)."""
    given = {p["parameterId"]: p for p in [q("curves", curves)] + list(given)}
    enums = enums or {}
    params = []
    for p in SPEC["parameters"]:
        pid = p["parameterId"]
        d = p.get("defaultValue")
        if pid in ("inflectionIndices", "extraDistances"):
            items = given[pid]["items"] if pid in given else []
            params.append({"btType": "BTMParameterArray-2025", "parameterId": pid, "items": items})
        elif pid in given:
            params.append(given[pid])
        elif pid in enums:
            e = copy.deepcopy(d)
            e["parameterId"] = pid
            e["value"] = enums[pid]
            params.append(e)
        elif isinstance(d, dict):
            params.append(dict(d, parameterId=pid))
    return feature(name, "betterCurveTrim", params, NS)


SPLIT = {"operation": "SPLIT"}

x0 = 0
w = wire("T1", x0)
p = points("T1", [(x0 + 100, -6), (x0 + 300, 200)])
trim("T1 split at 2 points (one 6 mm off) -> 3 wires 100 / 357.080 / 100", body(w), [q("atPoints", vertices(p))],
     dict(SPLIT, cutBy="AT_POINTS"))

x0 = 1000
w = wire("T2", x0)
p = points("T2", [(x0 + 100, -6), (x0 + 300, 200)])
trim("T2 split in place at 2 points -> 1 wire, 5 edges, 557.080", body(w), [q("atPoints", vertices(p)), b("returnSingleWire", True)],
     dict(SPLIT, cutBy="AT_POINTS"))

x0 = 2000
w = wire("T3", x0)
p = points("T3", [(x0 + 100, 0)])
trim("T3 trim at point -> 1 wire 457.080", body(w), [q("atPoints", vertices(p))], {"operation": "TRIM", "cutBy": "AT_POINTS"})

x0 = 3000
w = wire("T4", x0)
p = points("T4", [(x0 + 100, 0)])
trim("T4 split 150 from point, also at reference -> 3 wires 100 / 150 / 307.080", body(w),
     [q("fromPoint", vertices(p)), num("distance", "150 mm"), b("splitAtReference", True)], dict(SPLIT, cutBy="ARC_LENGTH_FROM_POINT"))

x0 = 4000
w = wire("T5", x0)
p = points("T5", [(x0 + 100, 0)])
trim("T5 split -50 from point -> 2 wires 50 / 507.080", body(w), [q("fromPoint", vertices(p)), num("distance", "-50 mm")],
     dict(SPLIT, cutBy="ARC_LENGTH_FROM_POINT"))

x0 = 5000
w = wire("T6", x0)
p = points("T6", [(x0 + 100, 0)])
trim("T6 split 50 from point, opposite direction -> 2 wires 50 / 507.080", body(w),
     [q("fromPoint", vertices(p)), num("distance", "50 mm"), b("distanceFlip", True)], dict(SPLIT, cutBy="ARC_LENGTH_FROM_POINT"))

x0 = 6000
w = wire("T7", x0)
mc = connector("T7 mate connector at (%d, 0)" % (x0 + 100), x0 + 100, 0)
trim("T7 split at a mate connector -> 2 wires 100 / 457.080", body(w), [q("atPoints", body(mc))], dict(SPLIT, cutBy="AT_POINTS"))

x0 = 8000
w = wire("T9", x0)
p = points("T9", [(x0 + 100, 0)])
trim("T9 split 50, extra 150 and -80 from point -> 4 wires 20 / 130 / 100 / 307.080", body(w),
     [q("fromPoint", vertices(p)), num("distance", "50 mm"), extra_distances("extraDistances", 150, -80)],
     dict(SPLIT, cutBy="ARC_LENGTH_FROM_POINT"))

x0 = 7000
w = wire("T8", x0)
p = points("T8", [(x0 + 100, 0)])
trim("T8 split 600 from point -> ERROR runs past the end", body(w), [q("fromPoint", vertices(p)), num("distance", "600 mm")],
     dict(SPLIT, cutBy="ARC_LENGTH_FROM_POINT"))


def distance_trim(tag, x0, extra, keep, label):
    w = wire(tag, x0)
    p = points(tag, [(x0 + 100, 0)])
    trim("%s %s" % (tag, label), body(w), [q("fromPoint", vertices(p)), num("distance", "50 mm")] + extra,
         {"operation": "TRIM", "cutBy": "ARC_LENGTH_FROM_POINT", "trimKeep": keep})


distance_trim("T10", 9000, [], "FAR_SIDE", "trim 50 from point, keep beyond the cut -> 1 wire 407.080")
distance_trim("T11", 10000, [], "POINT_SIDE", "trim 50 from point, keep reference point side -> 1 wire 150")
distance_trim("T12", 11000, [b("distanceFlip", True)], "FAR_SIDE", "trim 50 opposite, keep beyond the cut -> 1 wire 50")
distance_trim("T13", 12000, [b("distanceFlip", True)], "POINT_SIDE", "trim 50 opposite, keep reference point side -> 1 wire 507.080")


w = wire("T14", 13000)
p = points("T14", [(13100, 0)])
trim("T14 trim 50 from point, default side to keep (toward point) -> 1 wire 150", body(w), [q("fromPoint", vertices(p)), num("distance", "50 mm")],
     {"operation": "TRIM", "cutBy": "ARC_LENGTH_FROM_POINT"})



# ---- Endpoint reference (2026-11-12): from an END of the wire the distance runs INTO the wire, whatever the path direction ----
# The wire runs (x0, 0) -> (x0+300, 300), length 557.080. Reference = the wire's own end vertex (picked by position).
def end_vertex(wid, x, y):
    return 'qContainsPoint(qCreatedBy(makeId("%s"), EntityType.VERTEX), vector(%s, %s, 0) * meter)' % (wid, x / 1000.0, y / 1000.0)


def end_case(tag, x0, label, at_start, keep, flip):
    wid = wire(tag, x0)
    ref = end_vertex(wid, x0, 0) if at_start else end_vertex(wid, x0 + 300, 300)
    trim("%s %s" % (tag, label), body(wid),
         [q("fromPoint", ref), num("distance", "100 mm"), b("distanceFlip", flip)],
         {"operation": "TRIM", "cutBy": "ARC_LENGTH_FROM_POINT", "trimKeep": keep})


end_case("T15", 14000, "trim 100 from the START vertex, keep toward point -> 1 wire 100", True, "POINT_SIDE", False)
end_case("T16", 15000, "trim 100 from the START vertex, keep away from point -> 1 wire 457.080", True, "FAR_SIDE", False)
end_case("T17", 16000, "trim 100 from the END vertex, keep toward point -> 1 wire 100", False, "POINT_SIDE", False)
end_case("T18", 17000, "trim 100 from the END vertex, keep away from point -> 1 wire 457.080", False, "FAR_SIDE", False)
end_case("T19", 18000, "trim 100 from the END vertex, Opposite direction ON (ignored) -> 1 wire 100", False, "POINT_SIDE", True)


# ---- In-place trim without gates (2026-11-12): the trim is tried on a scratch copy and the result observed ----
# HOOK wire: line (x0,0)->(x0+200,0), R50 half circle up to (x0+200,100), line back to (x0+50,100). Length 507.080.
# A cut on the return leg is crossed twice by the plane normal to the path there (the old "more than once" refusal).
def hook_wire(tag, x0):
    sk = sketch("%s hook wire sketch: line, R50 U-turn, line back (x0 %d)" % (tag, x0),
                [seg("l1", *mmv(x0, 0, x0 + 200, 0)), arc("a", *mmv(x0 + 200, 50, 50), -math.pi / 2, math.pi / 2),
                 seg("l2", *mmv(x0 + 200, 100, x0 + 50, 100))])
    return feature("%s hook wire (composite curve)" % tag, "compositeCurve", [q("edges", sketch_edges(sk))])


def gate_case(tag, x0, label, make_wire, ref_xy, distance, keep):
    wid = make_wire(tag, x0)
    trim("%s %s" % (tag, label), body(wid),
         [q("fromPoint", end_vertex(wid, x0 + ref_xy[0], ref_xy[1])), num("distance", "%g mm" % distance), b("distanceFlip", False)],
         {"operation": "TRIM", "cutBy": "ARC_LENGTH_FROM_POINT", "trimKeep": keep})


# 3-edge wire cut at its midpoint: both ends are equally far from the cut plane (the old "ambiguous side" refusal).
gate_case("T20", 19000, "3-edge wire, trim 150 from the START vertex (ends equidistant from the cut plane), keep toward point -> 1 wire 150",
          wire, (0, 0), 150, "POINT_SIDE")
gate_case("T21", 20000, "hook wire, trim 400 from the START vertex (cut on the return leg), keep toward point -> 1 wire 400",
          hook_wire, (0, 0), 400, "POINT_SIDE")
gate_case("T22", 21000, "hook wire, trim 400 from the START vertex, keep away from point -> 1 wire 107.080",
          hook_wire, (0, 0), 400, "FAR_SIDE")
gate_case("T23", 22000, "hook wire, trim 100 from the END vertex (cut on the return leg), keep toward point -> 1 wire 100",
          hook_wire, (50, 100), 100, "POINT_SIDE")

# NOTE T30 (hook wire, trim 60 from BOTH ends) ERRORed until 2026-11-18; the feature now tries the other end order on a scratch copy and it passes (387.080).
# ---- Distance from ends (2026-11-18): trim or cut a distance in from the ENDS of the wire (not of its edges) ----
# The 3-edge wire runs (x0, 0) -> (x0+300, 300), length 557.080.
def ends_case(tag, x0, label, given, enums, make_wire=None):
    wid = (make_wire or wire)(tag, x0)
    trim("%s %s" % (tag, label), body(wid), given(wid, x0), dict({"cutBy": "DISTANCE_FROM_ENDS"}, **enums))


ends_case("T24", 23000, "trim 50 from BOTH ends -> 1 wire 457.080 (starts at x0+50)",
          lambda wid, x0: [num("endsDistance", "50 mm")], {"operation": "TRIM", "endsMode": "SAME_AT_BOTH"})
ends_case("T25", 24000, "split 120 in from both ends -> 3 wires 120 / 317.080 / 120",
          lambda wid, x0: [num("endsDistance", "120 mm")], {"operation": "SPLIT", "endsMode": "SAME_AT_BOTH"})
ends_case("T26", 25000, "trim, start point = START vertex, 30 from the start and 70 from the end -> 457.080 (starts at x0+30)",
          lambda wid, x0: [q("endsStartPoint", end_vertex(wid, x0, 0)), num("endsStartDistance", "30 mm"), num("endsEndDistance", "70 mm")],
          {"operation": "TRIM", "endsMode": "DIFFERENT_AT_EACH"})
ends_case("T27", 26000, "trim, start point = END vertex, 30 from it and 70 from the other end -> 457.080 (starts at x0+70)",
          lambda wid, x0: [q("endsStartPoint", end_vertex(wid, x0 + 300, 300)), num("endsStartDistance", "30 mm"), num("endsEndDistance", "70 mm")],
          {"operation": "TRIM", "endsMode": "DIFFERENT_AT_EACH"})
ends_case("T28", 27000, "start point NOT at an end of the curve -> ERROR",
          lambda wid, x0: [q("endsStartPoint", end_vertex(wid, x0 + 200, 0)), num("endsStartDistance", "30 mm"), num("endsEndDistance", "70 mm")],
          {"operation": "TRIM", "endsMode": "DIFFERENT_AT_EACH"})
ends_case("T29", 28000, "300 mm from each end of a 557.080 mm curve -> ERROR (not shorter than the curve)",
          lambda wid, x0: [num("endsDistance", "300 mm")], {"operation": "TRIM", "endsMode": "SAME_AT_BOTH"})
ends_case("T30", 29000, "hook wire, trim 60 from BOTH ends -> 1 wire 387.080",
          lambda wid, x0: [num("endsDistance", "60 mm")], {"operation": "TRIM", "endsMode": "SAME_AT_BOTH"}, make_wire=hook_wire)
# Isolating T30: the same hook wire, one end at a time (start point = START vertex, the other distance 0).
ends_case("T31", 30000, "hook wire, trim 60 from the START end only -> 1 wire 447.080",
          lambda wid, x0: [q("endsStartPoint", end_vertex(wid, x0, 0)), num("endsStartDistance", "60 mm"), num("endsEndDistance", "0 mm")],
          {"operation": "TRIM", "endsMode": "DIFFERENT_AT_EACH"}, make_wire=hook_wire)
ends_case("T32", 31000, "hook wire, trim 60 from the END end only -> 1 wire 447.080",
          lambda wid, x0: [q("endsStartPoint", end_vertex(wid, x0, 0)), num("endsStartDistance", "0 mm"), num("endsEndDistance", "60 mm")],
          {"operation": "TRIM", "endsMode": "DIFFERENT_AT_EACH"}, make_wire=hook_wire)
# ---- Negative distance EXTENDS the end (2026-11-18) ----
ends_case("T33", 32000, "extend BOTH ends by 40 mm (distance -40) -> 1 wire 637.080 (starts at x0-40)",
          lambda wid, x0: [num("endsDistance", "-40 mm")], {"operation": "TRIM", "endsMode": "SAME_AT_BOTH"})
ends_case("T34", 33000, "trim 50 from the START end and extend the other end 40 -> 1 wire 547.080 (starts at x0+50)",
          lambda wid, x0: [q("endsStartPoint", end_vertex(wid, x0, 0)), num("endsStartDistance", "50 mm"), num("endsEndDistance", "-40 mm")],
          {"operation": "TRIM", "endsMode": "DIFFERENT_AT_EACH"})
ends_case("T35", 34000, "split with a negative distance -> ERROR (Split cannot extend)",
          lambda wid, x0: [num("endsDistance", "-40 mm")], {"operation": "SPLIT", "endsMode": "SAME_AT_BOTH"})

# ---- Reported failure (2026-11-18, RD 25ONE studio): Split, 15 mm from both ends, on a long single-edge intersection curve /
# several near-identical wires selected together. Two suspects: coincident wires, and one long edge. ----
def long_line_wire(tag, x0):
    sk = sketch("%s long single-edge wire (line, 1480 mm) (x0 %d)" % (tag, x0), [seg("l1", *mmv(x0, 0, x0 + 1480, 0))])
    return feature("%s 1-edge wire (composite curve)" % tag, "compositeCurve", [q("edges", sketch_edges(sk))])


w1 = wire("T36", 35000)
w2 = wire("T36b", 35000)     # an identical wire on top of the first
trim("T36 split 15 from both ends, TWO COINCIDENT wires selected -> 6 wires 15 x4 / 527.080 x2", "qUnion([%s, %s])" % (body(w1), body(w2)), [num("endsDistance", "15 mm")],
     {"cutBy": "DISTANCE_FROM_ENDS", "operation": "SPLIT", "endsMode": "SAME_AT_BOTH"})
ends_case("T37", 36000, "long single-edge wire (1480), split 15 from both ends -> 3 wires 15 / 1450 / 15",
          lambda wid, x0: [num("endsDistance", "15 mm")], {"operation": "SPLIT", "endsMode": "SAME_AT_BOTH"}, make_wire=long_line_wire)

# ---- A 0.003 mm micro-edge at one end (the reported wire has one: 13 edges, 1660.6 mm, std constructPath refused it at 1e-8 m) ----
def micro_edge_wire(tag, x0):
    sk = sketch("%s wire with a 0.003 mm edge at the start (x0 %d)" % (tag, x0),
                [seg("m1", *mmv(x0, 0, x0 + 0.003, 0)), seg("m2", *mmv(x0 + 0.003, 0, x0 + 500, 0)), seg("m3", *mmv(x0 + 500, 0, x0 + 900, 0))])
    return feature("%s 3-edge wire with a micro-edge (composite curve)" % tag, "compositeCurve", [q("edges", sketch_edges(sk))])


ends_case("T38", 37000, "wire with a 0.003 mm edge, split 15 from both ends -> 3 wires 15 / 870 / 15",
          lambda wid, x0: [num("endsDistance", "15 mm")], {"operation": "SPLIT", "endsMode": "SAME_AT_BOTH"}, make_wire=micro_edge_wire)
ends_case("T39", 38000, "wire with a 0.003 mm edge, trim 15 from both ends -> 1 wire 870",
          lambda wid, x0: [num("endsDistance", "15 mm")], {"operation": "TRIM", "endsMode": "SAME_AT_BOTH"}, make_wire=micro_edge_wire)
