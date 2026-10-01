"""Arc / line tangency (2026-09-25 review) in Driven edge offset: test cases as real features in driven_offset's
"Arc tangency DEO tests" Part Studio (upsert by name). Checked by check_arc_tangency_deo_tests.py.

Source (Top, one tangent chain from the origin): arc R300 30 deg -> arc R150 30 deg -> line 100 (335.6 long).
Profiles (Create offset profile, Points): P-const w 2 everywhere; P-vary w 2 -> 4 -> 2.5 -> 3, smooth.

  D1 P-const, Spline     -> 3 edges: arc, arc, line (exact, concentric); every joint 0 deg
  D2 P-vary, Spline      -> 3 edges, none an arc on the arc runs (they were 3-point arcs with kinked ends before);
                            every joint 0 deg
  D3 P-vary, Biarc fit   -> arc runs become tangent arc chains (>= 2 arcs each), the line run a spline; every joint
                            0 deg; within 0.02 mm of D2 everywhere (both within the 0.01 mm tolerance of the true offset)
  D4 P-const, Biarc fit  -> identical to D1 (a constant offset of an arc is exact either way)

usage (repo root): PYTHONPATH=. python devtools/onshape/build_arc_tangency_deo_tests.py
"""
import copy
import math

from sync.core.client import OnshapeClient
from devtools.onshape.fsapi import write_feature, delete_feature  # noqa: E402  (API budget: one feature-tree GET per studio)

c = OnshapeClient()
D, W = "f61d2c000ab2d1240776342e", "5b11f323ab31b04cba8b36ef"
STUDIO = "Arc tangency DEO tests"
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


_TREE = {}   # feature tree of the test Part Studio, kept in step with each write (API budget)


def upsert(feature):
    r = write_feature(BASE, feature, _TREE, client=c)
    print("%-100s %s" % (feature["name"][:100], r.get("featureState", {}).get("featureStatus")))
    return r["feature"]["featureId"]

def custom(tab_name, feature_type, name, given, enums):
    """Every parameter from the spec's defaults, then ours; enums keep the spec's namespace, only the value changes."""
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


def arc(eid, cx, cy, r, a0, a1):
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": a0, "endParam": a1, "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryCircle-115", "radius": r / 1000, "xCenter": cx / 1000, "yCenter": cy / 1000,
                         "xDir": 1.0, "yDir": 0.0, "clockwise": False}}


def seg(eid, x0, y0, x1, y1):
    L = math.hypot(x1 - x0, y1 - y0)
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": 0.0, "endParam": L / 1000, "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryLine-117", "pntX": x0 / 1000, "pntY": y0 / 1000, "dirX": (x1 - x0) / L, "dirY": (y1 - y0) / L}}


def edges_of(fid):
    return 'qOwnedByBody(qCreatedBy(makeId("%s"), EntityType.BODY), EntityType.EDGE)' % fid


def sketch_edges(fid):
    return 'qCreatedBy(makeId("%s"), EntityType.EDGE)' % fid


def origin_vertex(fid):
    return 'qContainsPoint(qCreatedBy(makeId("%s"), EntityType.VERTEX), vector(0, 0, 0) * meter)' % fid


# ---- source: arc R300 30 deg -> arc R150 30 deg -> line 100, tangent throughout, counter-clockwise ------------
a1 = math.radians(-60)
pA = (300 * math.cos(a1), 300 + 300 * math.sin(a1))
tA = (-math.sin(a1), math.cos(a1))
cB = (pA[0] - 150 * tA[1], pA[1] + 150 * tA[0])
b1 = math.radians(-30)
pB = (cB[0] + 150 * math.cos(b1), cB[1] + 150 * math.sin(b1))
tB = (-math.sin(b1), math.cos(b1))
source = sketch("Source: arc R300 30 deg, arc R150 30 deg, line 100, tangent chain from the origin on Top", "Top", [
    arc("a", 0, 300, 300, math.radians(-90), a1),
    arc("b", cB[0], cB[1], 150, a1, b1),
    seg("l", pB[0], pB[1], pB[0] + 100 * tB[0], pB[1] + 100 * tB[1])])


# ---- profiles (Create offset profile, Points) -------------------------------------------------------------------
def point(station, width, height, transition, ns):
    # Every item parameter must be present (correction 38), the station-from-point ones included.
    return {"btType": "BTMArrayParameterItem-1843", "parameters": [
        {"btType": "BTMParameterEnum-145", "namespace": ns, "enumName": "OffsetStationSource", "value": "VALUE", "parameterId": "stationSource"},
        q("stationPoint"), num("stationPointOffset", "0 mm"),
        num("station", "%g mm" % station), num("width", "%g mm" % width), num("height", "%g mm" % height),
        {"btType": "BTMParameterEnum-145", "namespace": ns, "enumName": "OffsetPointTransition", "value": transition, "parameterId": "transition"}]}


def profile(name, pts):
    _, ns = tab("create_offset_profile")
    items = [point(*p, ns=ns) for p in pts]
    return custom("create_offset_profile", "createOffsetProfile", name,
                  [{"btType": "BTMParameterArray-2025", "parameterId": "points", "items": items}], {"mode": "POINTS"})


const = profile("P-const: w 2 from 0 to 400", [(0, 2, 0, "LINEAR"), (400, 2, 0, "LINEAR")])
vary = profile("P-vary: w 2 (0) smooth 4 (120) smooth 2.5 (240) smooth 3 (400)",
               [(0, 2, 0, "SMOOTH"), (120, 4, 0, "SMOOTH"), (240, 2.5, 0, "SMOOTH"), (400, 3, 0, "LINEAR")])


# ---- cases --------------------------------------------------------------------------------------------------------
def offset(name, prof, fit, output):
    return custom("driven_edge_offset", "drivenEdgeOffset", name,
                  [num("targetPointSpacing", "5 mm"), q("offsetEdges", sketch_edges(source)), q("offsetProfile", edges_of(prof)),
                   q("offsetRefPoint", origin_vertex(source)), s("outputName", output)],
                  {"edgeOffsetSpacingDef": "DISTANCE_ALONG", "arcSourceFit": fit})


offset("D1 P-const, Spline -> arc, arc, line exact; joints 0 deg", const, "SPLINE", "D1")
offset("D2 P-vary, Spline -> 3 edges, no arc on the arc runs; joints 0 deg", vary, "SPLINE", "D2")
offset("D3 P-vary, Biarc fit -> arc chains (>= 2 per arc run) + spline; joints 0 deg; within 0.02 of D2", vary, "BIARC", "D3")
offset("D4 P-const, Biarc fit -> identical to D1", const, "BIARC", "D4")
print("studio", E)
