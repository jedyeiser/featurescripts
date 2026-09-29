"""Export primitive tests as real features in the "Primitive tests" Part Studio of Publish & Drawing tools
(doc 73271cfc; upsert by name). Checked by check_primitive_tests.py.

Fixture (derived by VERSION, never written to the RD doc):
  Derive_DM_V1        RD 20TAC Design Master @ V1: VOLUME, FULL_BASELINE, REF_WIRE, FPT_L, FPT_R + mate connectors
                      (FCP x 1625, ACP x 145, MRS x 885, MP x 807.97; tip toward +X)
  Mirror_TAC          the derived solid + wires mirrored in the Right (YZ) plane: tip toward -X
  Datum_x500_z10      a mate connector at world (500, 0, 10) mm, world axes
  EI_const_150        a sketch line on Front at z 150 mm, x 0..1800 mm: a constant EI of 150 N*m^2 in the xSection
                      EI-curve convention (x = world X, 1 mm = 1 N*m^2)

Cases (names carry the expectation):
  P1  volume / volume, MC picks, MP, world datum
  P2  FULL_BASELINE / FPT_L+R (flat input), MC picks
  P3  FULL_BASELINE / volume, REF_WIRE vertices
  P4  = P3 on the mirrored fixture (tip -X): same values, x negated
  P5  = P1 with the datum Datum_x500_z10: same values, x - 500, z - 10
  P6  = P1, average radius between the contact points
  P7  = P1 with the datum = the derived MRS connector (Z along the ski), Datum uses ORIGIN: same values, x - 885
  P8  = P1 + Target EI EI_const_150 + typed tip / tail block names: deflection = P L^3 / 48 EI (L = RSL), block rows
  P9  = P5 with Datum uses COORDINATE_SYSTEM (the connector is world-aligned): P5 values
  P10 = P1 with the dashed radius grid on and labels off: P1 values, grid dashes at every 10 m, no text

Studio: "Primitive tests" by default; PRIMITIVE_STUDIO=<name> builds (and creates) another one. NEW=0 leaves out
the 2026-09-28 parameters (datumUses, targetEI, tip / tail block), for a run against the phase-1 code.

usage (repo root): PYTHONPATH=. python devtools/onshape/build_primitive_tests.py [P1 P3 ...]
"""
import json
import os
import sys

from sync.core.client import OnshapeClient

c = OnshapeClient()
D, W = "73271cfcd708b3f5e3fc315c", "fea83d30106dba0a4e066761"
STUDIO = os.environ.get("PRIMITIVE_STUDIO", "Primitive tests")
NEW = os.environ.get("NEW", "1") != "0"
SRC_D, SRC_V, SRC_E, SRC_M = "6212b76fdc6e7eca7cc56d8e", "2a679ea0cd06d118ae47d0e5", "512aefb5494db48a1c058b03", "25245f3384cbcc4242e60112"
# Design Master @ V1 part ids: VOLUME, FULL_BASELINE, REF_WIRE, FPT_L, FPT_R
SRC_PARTS = ["RxKH", "RNGD", "RLCD", "J9D", "RDBD"]

ELEMENTS = c.list_elements(D, W)
studios = {e["name"]: e["id"] for e in ELEMENTS if e["elementType"] == "PARTSTUDIO"}
if STUDIO not in studios:
    studios[STUDIO] = c.post(f"/api/v10/partstudios/d/{D}/w/{W}", {"name": STUDIO})["id"]
    print("created Part Studio", STUDIO, studios[STUDIO])
E = studios[STUDIO]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"


def ns_of(tab_name):
    t = [e for e in ELEMENTS if e["name"] == tab_name][0]
    return "e%s::m%s" % (t["id"], t["microversionId"])


def q(pid, *exprs):
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "query=%s;" % e} for e in exprs]}


def num(pid, expr, integer=False):
    return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": expr, "isInteger": integer}


def s(pid, v):
    return {"btType": "BTMParameterString-149", "parameterId": pid, "value": v}


def b(pid, v):
    return {"btType": "BTMParameterBoolean-144", "parameterId": pid, "value": bool(v)}


def en(pid, enum_name, value, namespace=""):
    return {"btType": "BTMParameterEnum-145", "parameterId": pid, "enumName": enum_name, "value": value, "namespace": namespace}


def features():
    return c.get(f"{BASE}/features")


def upsert(name, feature_type, params, namespace=""):
    f = features()
    feature = {"btType": "BTMFeature-134", "featureType": feature_type, "name": name, "namespace": namespace, "parameters": params}
    body = {"btType": "BTFeatureDefinitionCall-1406", "feature": feature,
            "serializationVersion": f["serializationVersion"], "sourceMicroversion": f["sourceMicroversion"]}
    existing = [x for x in f["features"] if x["name"] == name]
    if existing:
        feature["featureId"] = existing[0]["featureId"]
        r = c.post(f"{BASE}/features/featureid/{existing[0]['featureId']}", body)
    else:
        r = c.post(f"{BASE}/features", body)
    print("%-72s %s" % (name, r.get("featureState", {}).get("featureStatus")))
    return r["feature"]["featureId"]


def derive():
    tmpl = [x for x in c.get(f"/api/v10/partstudios/d/{D}/w/{W}/e/bb2cddb24faf53d9d043c38e/features")["features"]
            if x["featureType"] == "importDerived"][0]
    params = json.loads(json.dumps(tmpl["parameters"]))
    for p in params:
        p.pop("nodeId", None)
        if p.get("parameterId") == "partStudio":
            p["namespace"] = "d%s::v%s::e%s::m%s" % (SRC_D, SRC_V, SRC_E, SRC_M)
            p["partQuery"].pop("nodeId", None)
            # The parts by id, plus every mate connector (they hang off sketches, so "include mate connectors"
            # alone does not bring them).
            p["partQuery"]["queries"] = [
                {"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "", "deterministicIds": SRC_PARTS},
                {"btType": "BTMIndividualQuery-138", "queryStatement": None,
                 "queryString": "query=qBodyType(qEverything(EntityType.BODY), BodyType.MATE_CONNECTOR);", "deterministicIds": []}]
        if p.get("parameterId") == "includeMateConnectors":
            p["value"] = True
    return upsert("Derive_DM_V1", "importDerived", params)


def mirror(derive_id):
    params = [
        en("patternType", "MirrorType", "PART"),
        q("entities", 'qBodyType(qCreatedBy(makeId("%s"), EntityType.BODY), [BodyType.SOLID, BodyType.WIRE])' % derive_id),
        q("mirrorPlane", 'qCreatedBy(makeId("Right"), EntityType.FACE)'),
        en("operationType", "NewBodyOperationType", "NEW"),
        b("defaultScope", True),
    ]
    return upsert("Mirror_TAC (tip -X)", "mirror", params)


def datum_mc():
    params = [
        en("originType", "OriginCreationType", "ON_ENTITY"),
        q("originQuery", 'qCreatedBy(makeId("Origin"), EntityType.VERTEX)'),
        en("entityInferenceType", "EntityInferenceType", "POINT"),
        b("realign", False),
        b("transform", True),
        num("translationX", "500 mm"),
        num("translationY", "0 mm"),
        num("translationZ", "10 mm"),
        en("rotationType", "RotationType", "ABOUT_Z"),
        num("rotation", "0 deg"),
        b("allowOwnerEntity", True),
        b("requireOwnerPart", False),
        b("specifyNormal", False),
        b("flipPrimary", False),
        en("secondaryAxisType", "MateConnectorAxisType", "PLUS_X"),
    ]
    return upsert("Datum_x500_z10", "mateConnector", params)


def mc_at(derive_id, x_mm):
    """The vertex of a derived mate connector at (x, 0, 0) mm."""
    return ('qContainsPoint(qOwnedByBody(qBodyType(qCreatedBy(makeId("%s"), EntityType.BODY), BodyType.MATE_CONNECTOR), '
            'EntityType.VERTEX), vector(%r, 0, 0) * millimeter)' % (derive_id, x_mm))


def wire_vertex(source_id, x_mm):
    """A wire vertex at (x, 0, 0) mm (REF_WIRE ends at FCP / ACP)."""
    return ('qContainsPoint(qOwnedByBody(qBodyType(qCreatedBy(makeId("%s"), EntityType.BODY), BodyType.WIRE), '
            'EntityType.VERTEX), vector(%r, 0, 0) * millimeter)' % (source_id, x_mm))


def wire_named(source_id, x_mm, y_mm, z_mm):
    """The wire body of `source_id` passing through (x, y, z) mm."""
    return ('qContainsPoint(qBodyType(qCreatedBy(makeId("%s"), EntityType.BODY), BodyType.WIRE), vector(%r, %r, %r) * millimeter)'
            % (source_id, x_mm, y_mm, z_mm))


def ei_sketch():
    """A constant EI of 150 N*m^2 in the xSection EI-curve convention: a line on Front (sketch x = world X, sketch y =
    world Z) at z 150 mm from x 0 to 1800 mm."""
    line = {"btType": "BTMSketchCurveSegment-155", "entityId": "ei", "startPointId": "ei.start", "endPointId": "ei.end",
            "startParam": 0.0, "endParam": 1.8, "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryLine-117", "pntX": 0.0, "pntY": 0.15, "dirX": 1.0, "dirY": 0.0}}
    f = features()
    feature = {"btType": "BTMSketch-151", "featureType": "newSketch", "name": "EI_const_150",
               "parameters": [q("sketchPlane", 'qCreatedBy(makeId("Front"), EntityType.FACE)')], "entities": [line], "constraints": []}
    body = {"btType": "BTFeatureDefinitionCall-1406", "feature": feature,
            "serializationVersion": f["serializationVersion"], "sourceMicroversion": f["sourceMicroversion"]}
    existing = [x for x in f["features"] if x["name"] == "EI_const_150"]
    if existing:
        feature["featureId"] = existing[0]["featureId"]
        r = c.post(f"{BASE}/features/featureid/{existing[0]['featureId']}", body)
    else:
        r = c.post(f"{BASE}/features", body)
    print("%-72s %s" % ("EI_const_150", r.get("featureState", {}).get("featureStatus")))
    return r["feature"]["featureId"]


def primitive(name, ns, volume, fcp, acp, mp=None, datum=None, prefix="", baseline=None, footprint=None,
              between="INFLECTION", points=21, qv="", datum_uses="ORIGIN", ei=None, tip_block="", tail_block="",
              grid=False, labels=True):
    params = [
        q("volume", volume),
        q("fcp", fcp),
        q("acp", acp),
        q("mp", *([mp] if mp else [])),
        q("datum", *([datum] if datum else [])),
        en("datumUses", "PrimitiveDatumUse", datum_uses, ns),
        s("prefix", prefix),
        en("baselineFrom", "PrimitiveSource", "INPUT" if baseline else "VOLUME", ns),
        q("baselineWires", *([baseline] if baseline else [])),
        en("footprintFrom", "PrimitiveSource", "INPUT" if footprint else "VOLUME", ns),
        q("footprintWires", *(footprint if footprint else [])),
        en("radiusBetween", "PrimitiveRadiusBetween", between, ns),
        q("targetEI", *([ei] if ei else [])),
        num("dataPoints", str(points), True),
        b("forceStations", True),
        q("tipBlockWire"),
        s("tipBlock", tip_block),
        q("tailBlockWire"),
        s("tailBlock", tail_block),
        num("bandGap", "50 mm"),
        num("radiusLimit", "50 m"),
        num("tickLength", "10 mm"),
        b("dashedGrid", grid),
        b("labels", labels),
        num("textHeight", "20 mm"),
        s("queryVariable", qv),
    ]
    if not NEW:
        params = [p for p in params if p["parameterId"] not in
                  ("datumUses", "targetEI", "tipBlockWire", "tipBlock", "tailBlockWire", "tailBlock",
                   "dashedGrid", "labels", "textHeight")]
    return upsert(name, "exportPrimitive", params, ns)


# Points the Design Master V1 wires pass through (probed with the eval API on the version).
FULL_BASELINE_AT = (885, 0, 3.9462364577439866)
FPT_L_AT = (885, 48.72930096625616, 0)
FPT_R_AT = (885, -48.72930096625616, 0)
MP_X = 807.9668184775537


def cases(dv, mi, dm, ei):
    vol = 'qBodyType(qCreatedBy(makeId("%s"), EntityType.BODY), BodyType.SOLID)'
    fb = wire_named(dv, *FULL_BASELINE_AT)
    fb_m = wire_named(mi, -FULL_BASELINE_AT[0], FULL_BASELINE_AT[1], FULL_BASELINE_AT[2])
    return [
        ("P1 TAC volume-volume, MC picks, MP -> avg R 17.05 m, nat 17.48 / 16.18 m, RSL 1480, INFO",
         dict(volume=vol % dv, fcp=mc_at(dv, 1625), acp=mc_at(dv, 145), mp=mc_at(dv, MP_X), prefix="P1 TAC", qv="primitive")),
        ("P2 TAC FULL_BASELINE + FPT_L-R flat -> FRCPl 130, ARCPl 50, radii = P1, INFO",
         dict(volume=vol % dv, fcp=mc_at(dv, 1625), acp=mc_at(dv, 145), prefix="P2 TAC", baseline=fb,
              footprint=[wire_named(dv, *FPT_L_AT), wire_named(dv, *FPT_R_AT)])),
        ("P3 TAC FULL_BASELINE + volume, REF_WIRE vertices -> baseline = P2, footprint = P1, INFO",
         dict(volume=vol % dv, fcp=wire_vertex(dv, 1625), acp=wire_vertex(dv, 145), prefix="P3 TAC", baseline=fb)),
        ("P4 TAC mirrored tip -X (as P3) -> P3 values, x negated, INFO",
         dict(volume=vol % mi, fcp=wire_vertex(mi, -1625), acp=wire_vertex(mi, -145), prefix="P4 TAC mirrored", baseline=fb_m)),
        ("P5 TAC datum x500 z10 (as P1) -> P1 values, x - 500, z - 10, INFO",
         dict(volume=vol % dv, fcp=mc_at(dv, 1625), acp=mc_at(dv, 145), mp=mc_at(dv, MP_X),
              datum='qBodyType(qCreatedBy(makeId("%s"), EntityType.BODY), BodyType.MATE_CONNECTOR)' % dm, prefix="P5 TAC datum")),
        ("P6 TAC as P1, average radius between contacts -> avg R over FCP-ACP, INFO",
         dict(volume=vol % dv, fcp=mc_at(dv, 1625), acp=mc_at(dv, 145), prefix="P6 TAC contacts", between="CONTACTS")),
        ("P7 TAC datum = MRS connector, Datum uses ORIGIN (as P1) -> P1 values, x - 885, INFO",
         dict(volume=vol % dv, fcp=mc_at(dv, 1625), acp=mc_at(dv, 145), mp=mc_at(dv, MP_X), datum=mc_at(dv, 885),
              datum_uses="ORIGIN", prefix="P7 TAC MRS datum")),
        ("P8 TAC as P1 + target EI 150 N*m2 + block names -> deflection P L^3 / 48 EI = 132.46 mm, INFO",
         dict(volume=vol % dv, fcp=mc_at(dv, 1625), acp=mc_at(dv, 145), mp=mc_at(dv, MP_X), prefix="P8 TAC EI",
              ei='qCreatedBy(makeId("%s"), EntityType.EDGE)' % ei, tip_block="TIP_BLOCK_T1", tail_block="TAIL_BLOCK_T1")),
        ("P9 TAC as P5, Datum uses COORDINATE_SYSTEM (world-aligned MC) -> P5 values, INFO",
         dict(volume=vol % dv, fcp=mc_at(dv, 1625), acp=mc_at(dv, 145), mp=mc_at(dv, MP_X),
              datum='qBodyType(qCreatedBy(makeId("%s"), EntityType.BODY), BodyType.MATE_CONNECTOR)' % dm,
              datum_uses="COORDINATE_SYSTEM", prefix="P9 TAC datum CS")),
        ("P10 TAC as P1, dashed radius grid, no labels -> P1 values, GRID dashes, no text, INFO",
         dict(volume=vol % dv, fcp=mc_at(dv, 1625), acp=mc_at(dv, 145), mp=mc_at(dv, MP_X), prefix="P10 TAC grid",
              grid=True, labels=False)),
    ]


if __name__ == "__main__":
    only = set(sys.argv[1:])
    ns = ns_of("export_primitive")
    dv = derive()
    mi = mirror(dv)
    dm = datum_mc()
    ei = ei_sketch()
    for name, kw in cases(dv, mi, dm, ei):
        if only and name.split()[0] not in only:
            continue
        if not NEW and name.split()[0] in ("P7", "P8", "P9", "P10"):
            continue
        primitive(name, ns, **kw)
