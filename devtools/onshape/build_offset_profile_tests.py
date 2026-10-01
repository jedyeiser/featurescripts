"""Add the Create offset profile cases that need fixtures to BeamBuilder Testbed's "Offset profile tests" Part Studio
(upsert by name; T1..T5 are plain-value cases already in the tree). Checked by check_offset_profile.py.

  T6  Regions: 'Flat' CONSTANT w 2 h 1 from 6000 (value) to a sketch point at x 6100;
      'Ramp' LINEAR w 2 -> 4, h 1 from that point to a mate connector at x 6300 + 50 mm
      -> one piece 6000 .. 6350, width 2 at 6050, 3 at 6225
  T7  Points: 7000 (value) w 0, smooth to a mate connector at x 7150 - 50 mm (station 7100) w 3
      -> one piece 7000 .. 7100, width 3 at 7100
  T8  Regions: 'Quad' QUADRATIC flat at START 8000 .. 8100, w 0 -> 4, h 0 (no buffers)
      -> one piece, one degree-2 edge; w 0.25 at 8025, 1 at 8050; slope 0 at 8000, 0.08 at 8100
  T9  Regions: 'Quad' QUADRATIC flat at END 9000 .. 9100, w 0 -> 4, h 0 (no buffers)
      -> one piece, one degree-2 edge; w 1.75 at 9025, 3 at 9050; slope 0.08 at 9000, 0 at 9100
  T10 Regions entered toward -X (tip at +X, 2026-09-28): 'Rev' LINEAR start 10100 -> end 10000, w 0 -> 4, h 1,
      start buffer 20 -> one piece 10000 .. 10100; w 0 at 10090, 2 at 10040, 4 at 10000
  T11 Regions toward -X: 'Quad' QUADRATIC flat at START, start 11100 -> end 11000, w 0 -> 4
      -> one degree-2 edge; w 0.25 at 11075, 1 at 11050; slope 0 at 11100, -0.08 at 11000
  T12 Regions toward -X, two touching: 'A' CONSTANT w 1 from 12200 -> 12100, 'B' LINEAR w 1 -> 3 from 12100 -> 12000
      -> one piece 12000 .. 12200; w 1 at 12150, 2 at 12050

usage (repo root, Git Bash): PYTHONPATH=. MSYS_NO_PATHCONV=1 python devtools/onshape/build_offset_profile_tests.py
"""
from sync.core.client import OnshapeClient
from devtools.onshape.fsapi import write_feature, delete_feature  # noqa: E402  (API budget: one feature-tree GET per studio)

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


def region(name, start, end, shape, cw="0 mm", ch="0 mm", w0="0 mm", w1="0 mm", h0="0 mm", h1="0 mm", flat="START", b0="0 mm", b1="0 mm"):
    """Every item parameter, hidden ones included (correction 38); `flat` is QUADRATIC's "Flat at"."""
    return item(s("regionName", name), *start, *end, en("shape", "OffsetProfileShape", shape),
                en("quadraticFlat", "OffsetQuadraticFlat", flat),
                num("constantWidth", cw), num("constantHeight", ch), num("startWidth", w0), num("endWidth", w1),
                num("startHeight", h0), num("endHeight", h1), num("startBuffer", b0), num("endBuffer", b1))


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

# T8 / T9
profile("T8 Regions QUADRATIC flat at start 8000..8100 w 0->4 -> w 0.25 at 8025, 1 at 8050, slope 0 at 8000", "REGIONS", regions=[
    region("Quad", station("start", "8000 mm"), station("end", "8100 mm"), "QUADRATIC", w0="0 mm", w1="4 mm", flat="START")])
profile("T9 Regions QUADRATIC flat at end 9000..9100 w 0->4 -> w 1.75 at 9025, 3 at 9050, slope 0 at 9100", "REGIONS", regions=[
    region("Quad", station("start", "9000 mm"), station("end", "9100 mm"), "QUADRATIC", w0="0 mm", w1="4 mm", flat="END")])

# T10 / T11 / T12: regions entered toward -X (start station above end station), e.g. FCP -> ACP with the tip at +X
profile("T10 Regions toward -X linear 10100->10000 w 0->4 start buffer 20 -> w 0 at 10090, 2 at 10040, 4 at 10000", "REGIONS", regions=[
    region("Rev", station("start", "10100 mm"), station("end", "10000 mm"), "LINEAR", w0="0 mm", w1="4 mm", h0="1 mm", h1="1 mm", b0="20 mm")])
profile("T11 Regions toward -X QUADRATIC flat at start 11100->11000 w 0->4 -> w 0.25 at 11075, slope 0 at 11100", "REGIONS", regions=[
    region("Quad", station("start", "11100 mm"), station("end", "11000 mm"), "QUADRATIC", w0="0 mm", w1="4 mm", flat="START")])
profile("T12 Regions toward -X constant 12200->12100 w1, linear 12100->12000 w 1->3 -> one piece, w 2 at 12050", "REGIONS", regions=[
    region("A", station("start", "12200 mm"), station("end", "12100 mm"), "CONSTANT", cw="1 mm"),
    region("B", station("start", "12100 mm"), station("end", "12000 mm"), "LINEAR", w0="1 mm", w1="3 mm")])
