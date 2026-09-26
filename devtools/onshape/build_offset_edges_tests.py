"""Build the Offset edges (example_1/refSurfCreation/offsetEdges.fs) test cases as real features in example_1's
"Offset edges tests" Part Studio (created when missing; upsert by name). Fixtures are native features only
(sketches, a helix, a composite curve). Checked -- and baselined -- by check_offset_edges_tests.py.

Cases from reviews/2026-09-25_tools_review/utility.md section 4 (OE1-OE17) and round2_lead_offset.md (OE18-OE22).
This suite is primarily a BASELINE for the shared-library refactor: the checker records edge count, curve type,
length and radius per edge, max deviation from the analytic offset, end tangents, joint angles and curvature jumps.

Frame convention (read from the code, labels swapped as the review notes): "Normal offset" moves along
yAxis = T x N, "Binormal offset" along the transported normal N. For a planar XY arc N points to the centre, so
"Binormal" is in-plane and "Normal" is out of plane. The checker measures distance from the source, so it does
not depend on which side a line's world-axis seed picks.

The editing logic does not run through the REST API: region names, the hidden regionNum, the read-only region
length and the Intersections array (blends) are all written here by hand. Cases sit 1000 mm apart along X
(station s = mm along the source from the reference point).

  OE1   line 200, Normal 10                                 -> 10 from the line, s 0..200
  OE2   R100 quarter arc, Binormal 10, Keep as arcs         -> CIRCLE R90 (or R110)
  OE3   R100 quarter arc, Binormal 10, Convert to splines   -> BSPLINE within 0.01
  OE4   3 x R20 m arcs (100 each), Binormal 10              -> builds, 3 edges within 0.01
  OE5   R100 quarter arc, Binormal 0 -> 10 linear, Keep as arcs -> INFO, 2 CIRCLE edges (arc pair)
  OE6   line 400, A 0..150 at 0, B 250..400 at 20, G1 blend 10/10 -> 1 wire, 3 edges, joints tangent
  OE7   R300 quarter arc, A 0..150 at 0, B 300..end at 20, G2 blend 10/10 -> curvature jump < 1 %
  OE8   line 200, Normal 0 -> 20 linear, start dwell 20      -> flat 0 over the first 20
  OE9a  line 200, Quadratic (zero slope at start) 0 -> 20    -> 5 at s 100
  OE9b  line 200, Smooth 0 -> 20                             -> 10 at s 100
  OE10  line 300, Single region, Smooth pins 50:0, 150:20, 250:5 -> through the pins, flat outside
  OE11a line 300, reference at the middle, 0..100 linear 0 -> 10 -> s 150..250 ramping up
  OE11b same, Flip direction                                -> s 50..150 ramping up toward s 50
  OE12  helix R100, 1 turn, height 50, Normal 10            -> 10 from the helix; roll recorded
  OE18  same helix, Normal 0 -> 10 linear                    -> frame-roll baseline
  OE13  overlapping regions                                 -> WARNING
  OE14  no regions                                          -> WARNING, no output
  OE15  blend start distance larger than its region         -> WARNING today (clamped; review item 6 wants INFO)
  OE17  region consumed by blends                           -> WARNING
  OE19  reference point off the chain and before its start  -> baseline of where s = 0 lands
  OE20  Single region, a pin that sets only one component   -> binormal constant 10, normal smooth 0 -> 20
  OE21  region extent from two points projected onto an arc -> s 52.3599..104.7198
  OE22  native composite curve of OE6's blend edge, picked by its transient id -> reference survives
  OE16  two lines at a corner (not G1; temporary instance, deleted) -> ERROR

usage (repo root): PYTHONPATH=. python devtools/onshape/build_offset_edges_tests.py
"""
import copy
import json
import math
import re

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("example_1/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
STUDIO = "Offset edges tests"
ELEMENTS = c.list_elements(D, W)
studios = {e["name"]: e["id"] for e in ELEMENTS if e["elementType"] == "PARTSTUDIO"}
if STUDIO not in studios:
    studios[STUDIO] = c.post(f"/api/v10/partstudios/d/{D}/w/{W}", {"name": STUDIO})["id"]
E = studios[STUDIO]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TAB = [e for e in ELEMENTS if e["name"] == "offsetEdges"][0]
NS = "e%s::m%s" % (TAB["id"], TAB["microversionId"])
SPEC = [x for x in c.get(f"/api/v10/featurestudios/d/{D}/w/{W}/e/{TAB['id']}/featurespecs")["featureSpecs"]
        if x["featureType"] == "offsetEdges"][0]["parameters"]
TOP = 'qCreatedBy(makeId("Top"), EntityType.FACE)'
STATE = {"features": None}
STD_SPECS = {}


# ---- parameter helpers ----
def q(pid, *exprs):
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "query=%s;" % e} for e in exprs]}


def qids(pid, ids):
    """A selection by transient entity ids, stored by Onshape as a persistent query (like a click)."""
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "", "deterministicIds": list(ids)}]}


def default_param(p):
    """The spec's default for one parameter, without its nodeId, with an expression filled in (correction 38)."""
    d = copy.deepcopy(p.get("defaultValue"))
    if not isinstance(d, dict):
        return None
    d.pop("nodeId", None)
    d["parameterId"] = p["parameterId"]
    if d.get("btType", "").startswith("BTMParameterQuantity") and not d.get("expression"):
        kind = p.get("quantityType", "")
        v = d.get("value", 0.0)
        units = d.get("units") or ""
        if kind == "LENGTH":
            # The spec states its unit ("millimeter", "meter", ...); a bare length default is in meters.
            d["expression"] = "%.10g %s" % (v, units) if units else "%.10g mm" % (v * 1000)
        elif kind == "ANGLE":
            d["expression"] = "%.10g %s" % (v, units) if units else "%.10g rad" % v
        elif kind == "INTEGER":
            d["expression"] = "%d" % round(v)
        else:
            d["expression"] = "%.10g" % v
    return d


def from_spec(spec_params, given):
    """Every parameter of a spec (array items included, hidden ones too) from its default, with `given` on top.
    given: parameterId -> enum value / expression / bool / string / list of query expressions / list of item maps.
    Enums keep the namespace the spec gives them (the feature tab's, correction 38)."""
    out = []
    for p in spec_params:
        pid = p["parameterId"]
        d = default_param(p)
        if pid not in given:
            if d is not None:
                out.append(d)
            continue
        v = given[pid]
        if isinstance(v, dict) and "btType" in v:
            out.append(dict(v, parameterId=pid))
            continue
        kind = d["btType"]
        if kind.startswith("BTMParameterArray"):
            d["items"] = [{"btType": "BTMArrayParameterItem-1843", "parameters": from_spec(p["parameters"], item)} for item in v]
        elif kind.startswith("BTMParameterQueryList"):
            d = q(pid, *([v] if isinstance(v, str) else v))
        elif kind.startswith("BTMParameterQuantity"):
            d["expression"] = v
            d.pop("value", None)
        else:
            d["value"] = v
        out.append(d)
    return out


def upsert(feature):
    if STATE["features"] is None:
        STATE["features"] = c.get(f"{BASE}/features")
    f = STATE["features"]
    existing = [x for x in f["features"] if x["name"] == feature["name"]]
    body_ = {"btType": "BTFeatureDefinitionCall-1406", "feature": feature,
             "serializationVersion": f["serializationVersion"], "sourceMicroversion": f["sourceMicroversion"]}
    if existing:
        feature["featureId"] = existing[0]["featureId"]
        r = c.post(f"{BASE}/features/featureid/{existing[0]['featureId']}", body_)
    else:
        r = c.post(f"{BASE}/features", body_)
    STATE["features"] = None
    print("%-110s %s" % (feature["name"][:110], r.get("featureState", {}).get("featureStatus")))
    return r["feature"]["featureId"]


def feature(name, ftype, params, ns=""):
    f = {"btType": "BTMFeature-134", "featureType": ftype, "name": name, "parameters": params}
    if ns:
        f["namespace"] = ns
    return upsert(f)


def std(name, ftype, given):
    if ftype not in STD_SPECS:
        STD_SPECS[ftype] = [x for x in c.get(f"{BASE}/featurespecs")["featureSpecs"] if x["featureType"] == ftype][0]["parameters"]
    return feature(name, ftype, from_spec(STD_SPECS[ftype], given))


# ---- sketch geometry (metres) ----
def mmv(*xs):
    return [x / 1000.0 for x in xs]


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


def sketch(name, entities):
    return upsert({"btType": "BTMSketch-151", "featureType": "newSketch", "name": name,
                   "parameters": [q("sketchPlane", TOP)], "entities": entities, "constraints": []})


def edges(fid):
    return 'qConstructionFilter(qOwnedByBody(qBodyType(qCreatedBy(makeId("%s"), EntityType.BODY), BodyType.WIRE), EntityType.EDGE), ConstructionObject.NO)' % fid


def vertex(fid):
    return 'qCreatedBy(makeId("%s"), EntityType.VERTEX)' % fid


def line_source(tag, x0, length):
    return edges(sketch("%s source line (%d, 0) to (%d, 0)" % (tag, x0, x0 + length), [seg("l", *mmv(x0, 0, x0 + length, 0))]))


def quarter_arc(tag, x0, r):
    """R r quarter arc, centre (x0, r), from (x0, 0) counter-clockwise to (x0 + r, r); station 0 at (x0, 0)."""
    return edges(sketch("%s source R%d quarter arc centre (%d, %d)" % (tag, r, x0, r), [arc("a", *mmv(x0, r, r), -math.pi / 2, 0)]))


def ref_point(tag, x, y):
    return vertex(sketch("%s reference point (%g, %g)" % (tag, x, y), [point("p", *mmv(x, y))]))


# ---- the feature under test ----
def region(name, start, end, shape="LINEAR", offset_type="NORMAL", normal=(0, 0), binormal=(0, 0), dwell=(0, 0),
           quad="AT_START", extent_points=None, number=1):
    """One Regions item. start / end in mm from the reference point (X_EXTENTS) unless extent_points is a query."""
    item = {"regionType": shape, "quadZeroSlope": quad, "regionNum": "%d" % number, "regionName": name,
            "offsetType": offset_type,
            "startNormalOffset": "%g mm" % normal[0], "endNormalOffset": "%g mm" % normal[1],
            "startBinormalOffset": "%g mm" % binormal[0], "endBinormalOffset": "%g mm" % binormal[1],
            "startDelay": "%g mm" % dwell[0], "endDelay": "%g mm" % dwell[1]}
    if extent_points:
        item.update({"extentType": "QUERY", "extentQueries": extent_points, "length": "0 mm"})
    else:
        item.update({"extentType": "X_EXTENTS", "regionStart": "%g mm" % start, "regionEnd": "%g mm" % end,
                     "length": "%g mm" % abs(end - start)})
    return item


def blend(number, region1, region2, c0="G1", d0=10, c1="G1", d1=10):
    """One Intersections item, as the editing logic would write it for consecutive sorted regions."""
    return {"isValid": True, "intersectionNum": "%d" % number, "region1": region1, "region2": region2, "blend": True,
            "startContinuity": c0, "startDist": "%g mm" % d0, "endContinuity": c1, "endDist": "%g mm" % d1}


def pin(name, position, offset_type="NORMAL", normal=0, binormal=0):
    return {"offsetName": name, "locationType": "X_EXTENTS", "position": "%g mm" % position, "pointOffsetType": offset_type,
            "normalOffset": "%g mm" % normal, "binormalOffset": "%g mm" % binormal}


def offset(name, source, ref, regions=(), blends=(), pins=None, **options):
    """An Offset edges instance; everything not given takes the spec default (correction 38)."""
    given = {"userSelection": source, "referencePoint": ref, "regions": list(regions), "intersections": list(blends)}
    if pins is not None:
        given.update({"offsetMode": "SINGLE_REGION", "interiorOffsets": list(pins)})
    given.update(options)
    return feature(name, "offsetEdges", from_spec(SPEC, given), NS)


QUARTER_100 = 100 * math.pi / 2

# OE1 line, normal 10
offset("OE1 line 200, Normal 10 -> 10 from the line, s 0..200", line_source("OE1", 0, 200), ref_point("OE1", 0, 0),
       [region("A", 0, 200, normal=(10, 10))])

# OE2 / OE3 R100 quarter arc, binormal 10, arcs vs splines
offset("OE2 R100 arc, Binormal 10, Keep as arcs -> CIRCLE R90 or R110", quarter_arc("OE2", 1000, 100), ref_point("OE2", 1000, 0),
       [region("A", 0, 1000, offset_type="BINORMAL", binormal=(10, 10))], arcMode="BIARC")
offset("OE3 R100 arc, Binormal 10, Convert to splines -> BSPLINE within 0.01", quarter_arc("OE3", 2000, 100), ref_point("OE3", 2000, 0),
       [region("A", 0, 1000, offset_type="BINORMAL", binormal=(10, 10))], arcMode="SPLINE")

# OE4 three consecutive flat arcs (R 20 m, 100 each): the old 'normals not coplanar' failure
R4, C4X, C4Y = 20000.0, 3150.0, 20000.0
A4 = [-math.pi / 2 - 0.0075 + 0.005 * k for k in range(4)]
s4 = sketch("OE4 source 3 x R20000 arcs centre (3150, 20000), 100 each", [arc("a%d" % k, *mmv(C4X, C4Y, R4), A4[k], A4[k + 1]) for k in range(3)])
offset("OE4 3 x R20 m arcs, Binormal 10 -> builds, 3 edges within 0.01", edges(s4),
       ref_point("OE4", C4X + R4 * math.cos(A4[0]), C4Y + R4 * math.sin(A4[0])),
       [region("A", -1000, 1000, offset_type="BINORMAL", binormal=(10, 10))])

# OE5 varying offset over an arc, Keep as arcs -> arc pair
offset("OE5 R100 arc, Binormal 0 to 10 linear, Keep as arcs -> INFO, 2 CIRCLE edges", quarter_arc("OE5", 4000, 100), ref_point("OE5", 4000, 0),
       [region("A", 0, 1000, offset_type="BINORMAL", binormal=(0, 10))], arcMode="BIARC")

# OE6 G1 blend on a line
oe6 = offset("OE6 line 400, A 0..150 at 0, B 250..400 at 20, G1 blend 10/10 -> 1 wire, 3 edges, tangent joints",
             line_source("OE6", 5000, 400), ref_point("OE6", 5000, 0),
             [region("A", 0, 150, normal=(0, 0), number=1), region("B", 250, 400, normal=(20, 20), number=2)],
             [blend(1, "A", "B", "G1", 10, "G1", 10)])

# OE7 G2 blend on a R300 arc
offset("OE7 R300 arc, A 0..150 at 0, B 300..end at 20, G2 blend 10/10 -> curvature jump < 1 pct",
       quarter_arc("OE7", 6000, 300), ref_point("OE7", 6000, 0),
       [region("A", 0, 150, offset_type="BINORMAL", number=1), region("B", 300, 1000, offset_type="BINORMAL", binormal=(20, 20), number=2)],
       [blend(1, "A", "B", "G2", 10, "G2", 10)])

# OE8 dwell
offset("OE8 line 200, Normal 0 to 20 linear, start dwell 20 -> flat 0 over the first 20", line_source("OE8", 7000, 200), ref_point("OE8", 7000, 0),
       [region("A", 0, 200, normal=(0, 20), dwell=(20, 0))])

# OE9 quadratic / smooth
offset("OE9a line 200, Quadratic zero slope at start 0 to 20 -> 5 at s 100", line_source("OE9a", 8000, 200), ref_point("OE9a", 8000, 0),
       [region("A", 0, 200, shape="QUADRATIC", normal=(0, 20), quad="AT_START")])
offset("OE9b line 200, Smooth 0 to 20 -> 10 at s 100", line_source("OE9b", 9000, 200), ref_point("OE9b", 9000, 0),
       [region("A", 0, 200, shape="SMOOTH", normal=(0, 20))])

# OE10 single region pins
offset("OE10 line 300, Single region Smooth pins 50:0, 150:20, 250:5 -> through the pins, flat outside",
       line_source("OE10", 10000, 300), ref_point("OE10", 10000, 0),
       pins=[pin("P1", 50, normal=0), pin("P2", 150, normal=20), pin("P3", 250, normal=5)], singleTransfer="SMOOTH")

# OE11 flip direction, reference point at the middle of the line
offset("OE11a line 300, reference at s 150, 0..100 linear 0 to 10 -> s 150..250 rising", line_source("OE11a", 11000, 300),
       ref_point("OE11a", 11150, 0), [region("A", 0, 100, normal=(0, 10))])
offset("OE11b same, Flip direction -> s 50..150 rising toward s 50", line_source("OE11b", 12000, 300),
       ref_point("OE11b", 12150, 0), [region("A", 0, 100, normal=(0, 10))], flipDirection=True)

# OE12 / OE18 helix R100, 1 turn, height 50 (the round-2 roll check)
for tag, x0, prof, text in [("OE12", 13000, (10, 10), "Normal 10 -> 10 from the helix, roll recorded"),
                            ("OE18", 14000, (0, 10), "Normal 0 to 10 linear -> frame-roll baseline")]:
    ci = sketch("%s helix axis circle R100 at (%d, 0)" % (tag, x0), [circle("c", *mmv(x0, 0, 100))])
    hx = std("%s helix R100, 1 turn, height 50" % tag, "helix", {
        "axisType": "CIRCLE", "edge": edges(ci), "pathType": "TURNS", "revolutions": "1", "height": "50 mm", "endType": "HEIGHT"})
    offset("%s helix, %s" % (tag, text), 'qCreatedBy(makeId("%s"), EntityType.EDGE)' % hx,
           'qNthElement(qCreatedBy(makeId("%s"), EntityType.VERTEX), 0)' % hx, [region("A", -1000, 1000, normal=prof)])

# OE13 overlapping regions
offset("OE13 line 300, regions 0..150 and 100..250 overlap -> WARNING", line_source("OE13", 15000, 300), ref_point("OE13", 15000, 0),
       [region("A", 0, 150, normal=(5, 5), number=1), region("B", 100, 250, normal=(10, 10), number=2)])

# OE14 no regions
offset("OE14 line 200, no regions -> WARNING, no output", line_source("OE14", 16000, 200), ref_point("OE14", 16000, 0))

# OE15 blend start distance larger than region A (100) -> clamped with a warning today
offset("OE15 line 300, A 0..100, B 200..300, blend start 150 -> WARNING, clamped", line_source("OE15", 17000, 300), ref_point("OE15", 17000, 0),
       [region("A", 0, 100, number=1), region("B", 200, 300, normal=(10, 10), number=2)], [blend(1, "A", "B", "G1", 150, "G1", 10)])

# OE17 region B (10 long) consumed by the blend
offset("OE17 line 300, A 0..100, B 100..110, blend 10/10 consumes B -> WARNING", line_source("OE17", 18000, 300), ref_point("OE17", 18000, 0),
       [region("A", 0, 100, number=1), region("B", 100, 110, normal=(10, 10), number=2)], [blend(1, "A", "B", "G1", 10, "G1", 10)])

# OE19 reference point off the chain, before its start
offset("OE19 line 200, reference point (18950, 30) off and before the chain, 0..100 Normal 10 -> baseline extent",
       line_source("OE19", 19000, 200), ref_point("OE19", 18950, 30), [region("A", 0, 100, normal=(10, 10))])

# OE20 single region, a pin that sets only one component
offset("OE20 line 300, Single region Smooth N pins 100:0, 200:20, B pin 150:10 only -> B constant 10",
       line_source("OE20", 20000, 300), ref_point("OE20", 20000, 0),
       pins=[pin("N1", 100, normal=0), pin("B1", 150, offset_type="BINORMAL", binormal=10), pin("N2", 200, normal=20)], singleTransfer="SMOOTH")

# OE21 region extent from two points projected onto a curved chain (angles 30 and 60 deg from the start, radius 130)
p21 = sketch("OE21 extent points at 30 and 60 deg from the arc start, radius 130", [
    point("p", *mmv(21000 + 130 * math.cos(-math.pi / 3), 100 + 130 * math.sin(-math.pi / 3))),
    point("q", *mmv(21000 + 130 * math.cos(-math.pi / 6), 100 + 130 * math.sin(-math.pi / 6)))])
offset("OE21 R100 arc, extent from 2 projected points, Binormal 10 -> s 52.3599..104.7198", quarter_arc("OE21", 21000, 100),
       ref_point("OE21", 21000, 0), [region("A", 0, 0, offset_type="BINORMAL", binormal=(10, 10), extent_points=vertex(p21))])

# OE22 a downstream native feature that picks OE6's blend edge by its transient id (as a click would)
script = ('function(context is Context, queries) {\n'
          '    for (var e in evaluateQuery(context, qOwnedByBody(qBodyType(qCreatedBy(makeId("%s"), EntityType.BODY), BodyType.WIRE), EntityType.EDGE)))\n'
          '    {\n'
          '        const mid = evEdgeTangentLine(context, { "edge" : e, "parameter" : 0.5 }).origin;\n'
          '        if (abs(mid[0] - 5200 * millimeter) < 30 * millimeter) { return transientQueriesToStrings(e); }\n'
          '    }\n'
          '    return "";\n}\n' % oe6)
r = c.post(f"{BASE}/featurescript", json_data={"script": script})
ids = [v for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))) if v and not v.startswith("BTFSValue")]
if ids:
    feature("OE22 composite curve of OE6's blend edge picked by id -> OK, same length as that edge", "compositeCurve", [qids("edges", ids[:1])])
else:
    print("OE22 NOT BUILT: OE6 has no output edge around x 5200 (%s)" % r.get("notices"))

# OE16 a G0 corner: sharp-corner paths are legitimate input (user, 2026-09-25) -- kept as a real case so its
# output at the corner can be inspected and, once corners are handled, checked.
offset("OE16 G0 corner (22000,0)-(22100,0)-(22100,100), normal 10 -> offset follows both legs, corner handled",
       edges(sketch("OE16 source corner (22000, 0) to (22100, 0) to (22100, 100)",
                    [seg("a", *mmv(22000, 0, 22100, 0)), seg("b", *mmv(22100, 0, 22100, 100))])),
       ref_point("OE16", 22000, 0), [region("A", 0, 200, normal=(10, 10))])
print("studio", E)
