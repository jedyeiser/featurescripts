"""Driven edge offset "Profile source: Regions" tests as real features in driven_offset's "DEO regions tests" Part
Studio (created if missing; upsert by name). Checked by check_deo_regions_tests.py.

Each case is a PAIR on the same reference edge with the same settings: a Driven edge offset whose profile is the
wire of a Create offset profile (the reference, "<case>-W ...") and a Driven edge offset with Profile source =
Regions holding the SAME regions (the case, "<case> ..."). The two must land on each other within 1e-3 mm, with the
same number of bodies and edges.

  D1 line x 0..500: 'Ramp' SMOOTH 0..250 w 2->8 h 0->2 (buffers 20 / 20), 'Hold' CONSTANT 250..450 w 8 h 2
     (touching, equal values: one piece) -> matches D1-W
  D2 arc R400 70 deg (488.7 long): same regions -> matches D2-W
  D3 arc: 'Low' CONSTANT 0..200 w 3, 'High' CONSTANT 200..400 w 6 (a step: the profile breaks at 200) -> matches D3-W
  D4 line, Measure along World X: 'Picked' LINEAR from a sketch point at x 100 to a mate connector at x 350 - 50 mm
     (station 300), w 2 -> 5 -> matches D4-W, and runs x 100 .. 300 with width 2 / 5 at its ends
  D5 arc: 'Quad' QUADRATIC flat at start 0..250 w 2->8 h 0->2 (buffers 20 / 20: C1 into the ramp, a kink out of
     it at 230), 'Hold' CONSTANT 250..450 w 8 h 2 -> matches D5-W

usage (repo root, Git Bash): PYTHONPATH=. MSYS_NO_PATHCONV=1 python devtools/onshape/build_deo_regions_tests.py
Needs the offset_profile_core tab and the Regions source pushed first (driven_edge_offset featurespecs must list
profileSource).
"""
import copy
import math

from sync.core.client import OnshapeClient

c = OnshapeClient()
D, W = "f61d2c000ab2d1240776342e", "5b11f323ab31b04cba8b36ef"
STUDIO = "DEO regions tests"
ELEMENTS = c.list_elements(D, W)
studios = {e["name"]: e["id"] for e in ELEMENTS if e["elementType"] == "PARTSTUDIO"}
if STUDIO not in studios:
    studios[STUDIO] = c.post(f"/api/v10/partstudios/d/{D}/w/{W}", {"name": STUDIO})["id"]
E = studios[STUDIO]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TOP = 'qCreatedBy(makeId("Top"), EntityType.FACE)'


def tab(name):
    t = [e for e in ELEMENTS if e["name"] == name][0]
    return t, "e%s::m%s" % (t["id"], t["microversionId"])


COP_NS = tab("create_offset_profile")[1]
DEO_NS = tab("driven_edge_offset")[1]


def spec(tab_name, feature_type):
    t, ns = tab(tab_name)
    s = [x for x in c.get(f"/api/v10/featurestudios/d/{D}/w/{W}/e/{t['id']}/featurespecs")["featureSpecs"]
         if x["featureType"] == feature_type][0]
    return s, ns


def q(pid, *exprs):
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "query=%s;" % e} for e in exprs]}


def num(pid, expr):
    return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": expr, "isInteger": False}


def s(pid, v):
    return {"btType": "BTMParameterString-149", "parameterId": pid, "value": v}


def b(pid, v):
    return {"btType": "BTMParameterBoolean-144", "parameterId": pid, "value": v}


def en(pid, enum, value, ns):
    return {"btType": "BTMParameterEnum-145", "parameterId": pid, "enumName": enum, "value": value, "namespace": ns}


def arr(pid, items):
    return {"btType": "BTMParameterArray-2025", "parameterId": pid, "items": list(items)}


def item(*params):
    return {"btType": "BTMArrayParameterItem-1843", "parameters": list(params)}


def upsert(feature):
    f = c.get(f"{BASE}/features")
    existing = [x for x in f["features"] if x["name"] == feature["name"]]
    body = {"btType": "BTFeatureDefinitionCall-1406", "feature": feature,
            "serializationVersion": f["serializationVersion"], "sourceMicroversion": f["sourceMicroversion"]}
    if existing:
        feature["featureId"] = existing[0]["featureId"]
        r = c.post(f"{BASE}/features/featureid/{existing[0]['featureId']}", body)
    else:
        r = c.post(f"{BASE}/features", body)
    print("%-100s %s" % (feature["name"][:100], r.get("featureState", {}).get("featureStatus")))
    return r["feature"]["featureId"]


def custom(tab_name, feature_type, name, given, enums):
    """Every parameter from the spec's defaults (a partial feature fails its precondition), then ours.
    Enums keep the namespace the spec gives them; only their value is replaced."""
    sp, ns = spec(tab_name, feature_type)
    given = {p["parameterId"]: p for p in given}
    params = []
    for p in sp["parameters"]:
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
    return upsert({"btType": "BTMFeature-134", "featureType": feature_type, "name": name, "namespace": ns, "parameters": params})


def sketch(name, entities):
    return upsert({"btType": "BTMSketch-151", "featureType": "newSketch", "name": name,
                   "parameters": [q("sketchPlane", TOP)], "entities": entities, "constraints": []})


def sketch_edges(fid):
    return 'qCreatedBy(makeId("%s"), EntityType.EDGE)' % fid


def edges_of(fid):
    return 'qOwnedByBody(qCreatedBy(makeId("%s"), EntityType.BODY), EntityType.EDGE)' % fid


def origin_vertex(fid):
    return 'qContainsPoint(qCreatedBy(makeId("%s"), EntityType.VERTEX), vector(0, 0, 0) * meter)' % fid


def vertex(fid):
    return 'qCreatedBy(makeId("%s"), EntityType.VERTEX)' % fid


def body(fid):
    return 'qCreatedBy(makeId("%s"), EntityType.BODY)' % fid


# ---- references -------------------------------------------------------------------------------
line = {"btType": "BTMSketchCurveSegment-155", "entityId": "l", "startPointId": "l.start", "endPointId": "l.end",
        "startParam": 0.0, "endParam": 0.5, "isConstruction": False,
        "geometry": {"btType": "BTCurveGeometryLine-117", "pntX": 0.0, "pntY": 0.0, "dirX": 1.0, "dirY": 0.0}}
ref_line = sketch("Reference line: x 0..500 on Top", [line])

R = 0.4
arc = {"btType": "BTMSketchCurveSegment-155", "entityId": "a", "startPointId": "a.start", "endPointId": "a.end",
       "startParam": -math.pi / 2, "endParam": -math.pi / 2 + math.radians(70), "isConstruction": False,
       "geometry": {"btType": "BTCurveGeometryCircle-115", "radius": R, "xCenter": 0.0, "yCenter": R,
                    "xDir": 1.0, "yDir": 0.0, "clockwise": False}}
ref_arc = sketch("Reference arc: R400 from the origin, 70 deg (488.7 long) on Top", [arc])

# Station picks for D4 (world X 100 and a mate connector at world X 350; the region ends 50 mm before it).
p100 = sketch("D4 sketch point at x 100", [{"btType": "BTMSketchPoint-158", "entityId": "p", "isConstruction": False, "x": 0.1, "y": 0.05}])
mc_sketch = sketch("D4 mate connector point at x 350 (sketch)", [{"btType": "BTMSketchPoint-158", "entityId": "p", "isConstruction": False, "x": 0.35, "y": 0.05}])
mc350 = upsert({"btType": "BTMFeature-134", "featureType": "mateConnector", "name": "D4 mate connector at x 350", "parameters": [
    en("originType", "OriginCreationType", "ON_ENTITY", ""), q("originQuery", vertex(mc_sketch)),
    en("entityInferenceType", "EntityInferenceType", "POINT", ""), b("allowOwnerEntity", True), b("requireOwnerPart", False)]})


# ---- regions (identical items for Create offset profile and Driven edge offset; enum namespace per feature) ----
def station(prefix, ns, value=None, pick=None, offset="0 mm"):
    ids = {"start": ("startSource", "startPoint", "startPointOffset", "startStation"),
           "end": ("endSource", "endPoint", "endPointOffset", "endStation")}[prefix]
    return [en(ids[0], "OffsetStationSource", "POINT" if pick else "VALUE", ns), q(ids[1], *([pick] if pick else [])),
            num(ids[2], offset), num(ids[3], value or "0 mm")]


def region(ns, name, start, end, shape, cw="0 mm", ch="0 mm", w0="0 mm", w1="0 mm", h0="0 mm", h1="0 mm", b0="0 mm", b1="0 mm",
           flat="START"):
    """start / end: (value, pick, offset) of each station. Every item parameter is sent, hidden ones included
    (correction 38); `flat` is QUADRATIC's "Flat at"."""
    return item(s("regionName", name), *station("start", ns, *start), *station("end", ns, *end), en("shape", "OffsetProfileShape", shape, ns),
                en("quadraticFlat", "OffsetQuadraticFlat", flat, ns),
                num("constantWidth", cw), num("constantHeight", ch), num("startWidth", w0), num("endWidth", w1),
                num("startHeight", h0), num("endHeight", h1), num("startBuffer", b0), num("endBuffer", b1))


def ramp_hold(ns):
    return [region(ns, "Ramp", ("0 mm", None), ("250 mm", None), "SMOOTH", w0="2 mm", w1="8 mm", h0="0 mm", h1="2 mm", b0="20 mm", b1="20 mm"),
            region(ns, "Hold", ("250 mm", None), ("450 mm", None), "CONSTANT", cw="8 mm", ch="2 mm")]


def step(ns):
    return [region(ns, "Low", ("0 mm", None), ("200 mm", None), "CONSTANT", cw="3 mm"),
            region(ns, "High", ("200 mm", None), ("400 mm", None), "CONSTANT", cw="6 mm")]


def picked(ns):
    return [region(ns, "Picked", (None, vertex(p100)), (None, body(mc350), "-50 mm"), "LINEAR", w0="2 mm", w1="5 mm")]


def quad_hold(ns):
    return [region(ns, "Quad", ("0 mm", None), ("250 mm", None), "QUADRATIC", w0="2 mm", w1="8 mm", h0="0 mm", h1="2 mm",
                   b0="20 mm", b1="20 mm", flat="START"),
            region(ns, "Hold", ("250 mm", None), ("450 mm", None), "CONSTANT", cw="8 mm", ch="2 mm")]


# No intersections: no blends (touching regions with equal values join; a jump breaks). The editing logic does not
# run for REST inserts, so the list is given (empty) rather than derived.
def cop(name, regions):
    return custom("create_offset_profile", "createOffsetProfile", name,
                  [arr("regions", regions), arr("intersections", []), arr("points", []), b("debugPrint", False)], {"mode": "REGIONS"})


SPACING = num("targetPointSpacing", "5 mm")


def deo(name, ref, measure, profile_params, source):
    return custom("driven_edge_offset", "drivenEdgeOffset", name,
                  [SPACING, q("offsetEdges", sketch_edges(ref)), q("offsetRefPoint", origin_vertex(ref)), s("outputName", "")] + profile_params,
                  {"measureAlong": measure, "frameAlignment": "ALONG", "edgeOffsetSpacingDef": "DISTANCE_ALONG", "profileSource": source})


def pair(case, ref, ref_label, regions_of, label, measure="OFFSET_EDGES"):
    wire = cop("P-%s Create offset profile: %s" % (case, label), regions_of(COP_NS))
    deo("%s-W %s: Driven edge offset by the wire of P-%s (reference)" % (case, ref_label, case), ref, measure,
        [q("offsetProfile", edges_of(wire))], "WIRE")
    deo("%s %s: Driven edge offset, Profile source Regions = P-%s -> matches %s-W within 1e-3 mm" % (case, ref_label, case, case), ref, measure,
        [arr("regions", regions_of(DEO_NS)), arr("intersections", [])], "REGIONS")


pair("D1", ref_line, "line", ramp_hold, "Ramp SMOOTH 0..250 w 2->8 h 0->2 (buffers 20), Hold 250..450 w 8 h 2 -> 1 piece")
pair("D2", ref_arc, "arc R400", ramp_hold, "same regions as P-D1 (arc)")
pair("D3", ref_arc, "arc R400 step", step, "Low 0..200 w 3 | High 200..400 w 6 -> 2 pieces, step at 200")
pair("D4", ref_line, "line World X, point / MC stations", picked,
     "Picked LINEAR pt x100 .. MC x350 - 50 (300) w 2->5 -> x 100..300", measure="WORLD_X")
pair("D5", ref_arc, "arc R400 quadratic", quad_hold,
     "Quad QUADRATIC flat at start 0..250 w 2->8 h 0->2 (buffers 20), Hold 250..450 w 8 h 2 -> 1 piece")
print("studio", E)
