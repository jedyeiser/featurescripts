"""Add the Create offset profile cases that need fixtures to BeamBuilder Testbed's "Offset profile tests" Part Studio
(upsert by name; T1..T5 are plain-value cases already in the tree). Checked by check_offset_profile.py.

  T6  Regions: 'Flat' CONSTANT w 2 h 1 from 6000 (value) to a sketch point at x 6100;
      'Ramp' LINEAR w 2 -> 4, h 1 from that point to a mate connector at x 6300 + 50 mm
      -> one piece 6000 .. 6350, width 2 at 6050, 3 at 6225
  T7  Points: 7000 (value) w 0, smooth to a mate connector at x 7150 - 50 mm (station 7100) w 3
      -> one piece 7000 .. 7100, width 3 at 7100

usage (repo root, Git Bash): PYTHONPATH=. MSYS_NO_PATHCONV=1 python devtools/onshape/build_offset_profile_tests.py
"""
from sync.core.client import OnshapeClient

c = OnshapeClient()
D, W = "f61d2c000ab2d1240776342e", "5b11f323ab31b04cba8b36ef"
ELEMENTS = c.list_elements(D, W)
E = [e["id"] for e in ELEMENTS if e["name"] == "Offset profile tests"][0]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TAB = [e for e in ELEMENTS if e["name"] == "create_offset_profile"][0]
NS = "e%s::m%s" % (TAB["id"], TAB["microversionId"])
TOP = 'qCreatedBy(makeId("Top"), EntityType.FACE)'


def q(pid, *exprs):
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "query=%s;" % e} for e in exprs]}


def en(pid, enum, value, ns=NS):
    return {"btType": "BTMParameterEnum-145", "parameterId": pid, "enumName": enum, "value": value, "namespace": ns}


def num(pid, expr):
    return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": expr, "isInteger": False}


def s(pid, v):
    return {"btType": "BTMParameterString-149", "parameterId": pid, "value": v}


def b(pid, v):
    return {"btType": "BTMParameterBoolean-144", "parameterId": pid, "value": v}


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


def feature(name, ftype, params, ns=""):
    f = {"btType": "BTMFeature-134", "featureType": ftype, "name": name, "parameters": params}
    if ns:
        f["namespace"] = ns
    return upsert(f)


def sketch_point(name, x):
    return upsert({"btType": "BTMSketch-151", "featureType": "newSketch", "name": name, "parameters": [q("sketchPlane", TOP)],
                   "entities": [{"btType": "BTMSketchPoint-158", "entityId": "p", "isConstruction": False, "x": x / 1000.0, "y": 0.0}],
                   "constraints": []})


def vertex(fid):
    return 'qCreatedBy(makeId("%s"), EntityType.VERTEX)' % fid


def connector(name, x):
    sk = sketch_point(name + " (sketch)", x)
    return feature(name, "mateConnector", [
        en("originType", "OriginCreationType", "ON_ENTITY", ""), q("originQuery", vertex(sk)),
        en("entityInferenceType", "EntityInferenceType", "POINT", ""), b("allowOwnerEntity", True), b("requireOwnerPart", False)])


def station(prefix, value=None, pick=None, offset="0 mm"):
    """[source, point, distance, value] parameters of one station; prefix 'start' / 'end' (regions) or 'station' (points)."""
    ids = {"start": ("startSource", "startPoint", "startPointOffset", "startStation"),
           "end": ("endSource", "endPoint", "endPointOffset", "endStation"),
           "station": ("stationSource", "stationPoint", "stationPointOffset", "station")}[prefix]
    return [en(ids[0], "OffsetStationSource", "POINT" if pick else "VALUE"), q(ids[1], *([pick] if pick else [])),
            num(ids[2], offset), num(ids[3], value or "0 mm")]


def region(name, start, end, shape, cw="0 mm", ch="0 mm", w0="0 mm", w1="0 mm", h0="0 mm", h1="0 mm"):
    return item(s("regionName", name), *start, *end, en("shape", "OffsetProfileShape", shape),
                num("constantWidth", cw), num("constantHeight", ch), num("startWidth", w0), num("endWidth", w1),
                num("startHeight", h0), num("endHeight", h1), num("startBuffer", "0 mm"), num("endBuffer", "0 mm"))


def profile(name, mode, regions=(), points=()):
    return feature(name, "createOffsetProfile", [
        en("mode", "OffsetProfileMode", mode),
        {"btType": "BTMParameterArray-2025", "parameterId": "regions", "items": list(regions)},
        {"btType": "BTMParameterArray-2025", "parameterId": "intersections", "items": []},
        {"btType": "BTMParameterArray-2025", "parameterId": "points", "items": list(points)},
        b("debugPrint", False)], NS)


# T6
p6 = sketch_point("T6 sketch point at x 6100", 6100)
mc6 = connector("T6 mate connector at x 6300", 6300)
profile("T6 Regions constant 6000..pt 6100 w2 h1, linear pt..MC+50 w 2->4 -> one piece 6000..6350, w 3 at 6225", "REGIONS", regions=[
    region("Flat", station("start", "6000 mm"), station("end", pick=vertex(p6)), "CONSTANT", cw="2 mm", ch="1 mm"),
    region("Ramp", station("start", pick=vertex(p6)), station("end", pick='qCreatedBy(makeId("%s"), EntityType.BODY)' % mc6, offset="50 mm"),
           "LINEAR", w0="2 mm", w1="4 mm", h0="1 mm", h1="1 mm")])

# T7
mc7 = connector("T7 mate connector at x 7150", 7150)
profile("T7 Points 7000 w0 smooth to MC-50 (7100) w3 -> one piece 7000..7100", "POINTS", points=[
    item(*station("station", "7000 mm"), num("width", "0 mm"), num("height", "0 mm"), en("transition", "OffsetPointTransition", "SMOOTH")),
    item(*station("station", pick='qCreatedBy(makeId("%s"), EntityType.BODY)' % mc7, offset="-50 mm"), num("width", "3 mm"),
         num("height", "0 mm"), en("transition", "OffsetPointTransition", "SMOOTH"))])
