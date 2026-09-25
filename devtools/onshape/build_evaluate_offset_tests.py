"""Evaluate offset round-trip tests as real features in driven_offset's "Evaluate offset tests" Part Studio
(upsert by name). Checked by check_evaluate_offset.py.

Each case: a Create offset profile (the ORIGINAL profile) -> Driven edge offset of a reference sketch edge by
it (the TARGET) -> Evaluate offset between the reference and that target (the MEASURED profile). The measured
profile must reproduce the original wherever the original is defined.

  E1 line reference, Along / Offset edges, smooth + linear profile -> 1 piece, matches the original
  E2 arc R400 reference, Along / Offset edges, same profile -> 1 piece, matches the original
  E3 arc reference, profile with a jump at 200 -> 2 pieces, matches the original on both
  E4 arc reference, World / World X -> 1 piece, matches the original
  R2 / R3 / R4 Driven edge offset of the reference by E2 / E3 / E4's MEASURED profile -> lands on T-E2 / T-E3 / T-E4

usage (repo root): PYTHONPATH=. python devtools/onshape/build_evaluate_offset_tests.py
"""
import copy
import math

from sync.core.client import OnshapeClient

c = OnshapeClient()
D, W = "f61d2c000ab2d1240776342e", "5b11f323ab31b04cba8b36ef"
STUDIO = "Evaluate offset tests"
ELEMENTS = c.list_elements(D, W)
studios = {e["name"]: e["id"] for e in ELEMENTS if e["elementType"] == "PARTSTUDIO"}
if STUDIO not in studios:
    studios[STUDIO] = c.post(f"/api/v10/partstudios/d/{D}/w/{W}", {"name": STUDIO})["id"]
E = studios[STUDIO]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"


def tab(name):
    t = [e for e in ELEMENTS if e["name"] == name][0]
    return t, "e%s::m%s" % (t["id"], t["microversionId"])


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


def sketch(name, plane, entities):
    return upsert({"btType": "BTMSketch-151", "featureType": "newSketch", "name": name,
                   "parameters": [q("sketchPlane", 'qCreatedBy(makeId("%s"), EntityType.FACE)' % plane)], "entities": entities, "constraints": []})


def edges_of(fid):
    return 'qOwnedByBody(qCreatedBy(makeId("%s"), EntityType.BODY), EntityType.EDGE)' % fid


def sketch_edges(fid):
    return 'qCreatedBy(makeId("%s"), EntityType.EDGE)' % fid


def origin_vertex(fid):
    return 'qContainsPoint(qCreatedBy(makeId("%s"), EntityType.VERTEX), vector(0, 0, 0) * meter)' % fid


# ---- references ------------------------------------------------------------------------------
line = {"btType": "BTMSketchCurveSegment-155", "entityId": "l", "startPointId": "l.start", "endPointId": "l.end",
        "startParam": 0.0, "endParam": 0.5, "isConstruction": False,
        "geometry": {"btType": "BTCurveGeometryLine-117", "pntX": 0.0, "pntY": 0.0, "dirX": 1.0, "dirY": 0.0}}
ref_line = sketch("Reference line: x 0..500 on Top", "Top", [line])

R = 0.4
arc = {"btType": "BTMSketchCurveSegment-155", "entityId": "a", "startPointId": "a.start", "endPointId": "a.end",
       "startParam": -math.pi / 2, "endParam": -math.pi / 2 + math.radians(70), "isConstruction": False,
       "geometry": {"btType": "BTCurveGeometryCircle-115", "radius": R, "xCenter": 0.0, "yCenter": R,
                    "xDir": 1.0, "yDir": 0.0, "clockwise": False}}
ref_arc = sketch("Reference arc: R400 from the origin, 70 deg (488.7 long) on Top", "Top", [arc])


# ---- original profiles (Create offset profile, Points) -----------------------------------------
def point(station, width, height, transition, ns):
    return {"btType": "BTMArrayParameterItem-1843", "parameters": [
        num("station", "%g mm" % station), num("width", "%g mm" % width), num("height", "%g mm" % height),
        {"btType": "BTMParameterEnum-145", "namespace": ns, "enumName": "OffsetPointTransition", "value": transition, "parameterId": "transition"}]}


def profile(name, pts):
    _, ns = tab("create_offset_profile")
    items = [point(*p, ns=ns) for p in pts]
    return custom("create_offset_profile", "createOffsetProfile", name,
                  [{"btType": "BTMParameterArray-2025", "parameterId": "points", "items": items}], {"mode": "POINTS"})


smooth = profile("P-smooth: points 0 (w5 h0) smooth 250 (w15 h3) linear 450 (w8 h1)",
                 [(0, 5, 0, "SMOOTH"), (250, 15, 3, "LINEAR"), (450, 8, 1, "LINEAR")])
jump = profile("P-jump: points 0 (w5) linear 200 (w10) | 200 (w3) linear 400 (w3 h2) -> 2 pieces",
               [(0, 5, 0, "LINEAR"), (200, 10, 0, "HOLD"), (200, 3, 0, "LINEAR"), (400, 3, 2, "LINEAR")])


# ---- targets (Driven edge offset) and measurements (Evaluate offset) ---------------------------
SPACING = num("targetPointSpacing", "5 mm")   # both sides: the target must carry the profile's shape


def offset(name, ref, prof, measure="OFFSET_EDGES", frame="ALONG"):
    return custom("driven_edge_offset", "drivenEdgeOffset", name,
                  [SPACING, q("offsetEdges", sketch_edges(ref)), q("offsetProfile", edges_of(prof)), q("offsetRefPoint", origin_vertex(ref)),
                   s("outputName", "")],
                  {"measureAlong": measure, "frameAlignment": frame, "edgeOffsetSpacingDef": "DISTANCE_ALONG"})


def evaluate(name, ref, target, measure="OFFSET_EDGES", frame="ALONG"):
    return custom("evaluate_offset", "evaluateOffset", name,
                  [SPACING, q("offsetEdges", sketch_edges(ref)), q("targetEdges", edges_of(target)), q("offsetRefPoint", origin_vertex(ref)),
                   s("outputName", "")],
                  {"measureAlong": measure, "frameAlignment": frame, "edgeOffsetSpacingDef": "DISTANCE_ALONG"})


t1 = offset("T-E1 line offset by P-smooth (Along, Offset edges)", ref_line, smooth)
evaluate("E1 line, Along / Offset edges -> 1 piece, matches P-smooth", ref_line, t1)

t2 = offset("T-E2 arc offset by P-smooth (Along, Offset edges)", ref_arc, smooth)
e2 = evaluate("E2 arc R400, Along / Offset edges -> 1 piece, matches P-smooth", ref_arc, t2)

t3 = offset("T-E3 arc offset by P-jump (Along, Offset edges)", ref_arc, jump)
e3 = evaluate("E3 arc, jump at 200 -> 2 pieces, matches P-jump", ref_arc, t3)

t4 = offset("T-E4 arc offset by P-smooth (World, World X)", ref_arc, smooth, "WORLD_X", "WORLD")
e4 = evaluate("E4 arc, World / World X -> 1 piece, matches P-smooth", ref_arc, t4, "WORLD_X", "WORLD")

# Round trips: Driven edge offset driven by the MEASURED profile must land on the original target.
offset("R2 arc offset by E2's measured profile -> on T-E2", ref_arc, e2)
offset("R3 arc offset by E3's measured profile -> on T-E3", ref_arc, e3)
offset("R4 arc offset by E4's measured profile (World, World X) -> on T-E4", ref_arc, e4, "WORLD_X", "WORLD")


# ---- a G0 corner in the reference: 200 along X, then 200 at 30 deg ------------------------------
def polyline(name, turn_deg):
    a = math.radians(turn_deg)
    pts = [(0.0, 0.0), (0.2, 0.0), (0.2 + 0.2 * math.cos(a), 0.2 * math.sin(a))]
    segs = []
    for i in range(2):
        (x0, y0), (x1, y1) = pts[i], pts[i + 1]
        L = math.hypot(x1 - x0, y1 - y0)
        segs.append({"btType": "BTMSketchCurveSegment-155", "entityId": "s%d" % i, "startPointId": "s%d.start" % i, "endPointId": "s%d.end" % i,
                     "startParam": 0.0, "endParam": L, "isConstruction": False,
                     "geometry": {"btType": "BTCurveGeometryLine-117", "pntX": x0, "pntY": y0, "dirX": (x1 - x0) / L, "dirY": (y1 - y0) / L}})
    return sketch(name, "Top", segs)


ref_out = polyline("Reference corner turning -Y (away from the width offset): 200 + 200 at -30 deg on Top", -30)
t5 = offset("T-E5 corner offset by P-smooth (outside: arc fill)", ref_out, smooth)
e5 = evaluate("E5 G0 corner, offset outside the turn -> 1 piece, matches P-smooth", ref_out, t5)
offset("R5 corner offset by E5's measured profile -> on T-E5", ref_out, e5)

ref_in = polyline("Reference corner turning +Y (toward the width offset): 200 + 200 at +30 deg on Top", 30)
t6 = offset("T-E6 corner offset by P-smooth (inside: trimmed)", ref_in, smooth)
e6 = evaluate("E6 G0 corner, offset inside the turn -> 1 piece, matches P-smooth", ref_in, t6)
offset("R6 corner offset by E6's measured profile -> on T-E6", ref_in, e6)
print("studio", E)
