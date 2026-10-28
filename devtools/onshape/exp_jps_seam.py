"""Join profile surfaces on a SEAM where the two profiles have different edge counts (fixture: reference_side/jps_test_fixture.fs).

  ONSHAPE_CALL_CAP=20 PYTHONPATH=. python devtools/onshape/exp_jps_seam.py [fillet=5]

Writes (replacing by name) "K0 JPS fixture (inside z 0; outside z 20 with 3 faces left of x 30000, 1 right; plane x 30000; point at x 30050)" and "K1 Join profile surfaces, start split on a 3-edge / 1-edge seam -> 5 faces, area 22000, ONE loft face" into the "Reference side tests" Part Studio of the
reference_side document, then reports the faces of the joined surface that lie in the cut plane x = 30000 mm (the lofted strip):
before the fix there are three (trapezoid + two triangles), after it one. Also reports the feature status and any open joint.
"""
import json
import re
import sys

from sync.core.client import OnshapeClient
from devtools.onshape.fsapi import write_feature, namespace_of

c = OnshapeClient()
DOC = json.load(open("reference_side/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
studios = {e["name"]: e["id"] for e in c.list_elements(D, W) if e["elementType"] == "PARTSTUDIO"}
E = studios["Reference side tests"]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
args = dict(a.split("=", 1) for a in sys.argv[1:])

NS_FIX = namespace_of(D, W, "jps_test_fixture")
NS_JPS = namespace_of(D, W, "join_profile_surfaces")


def q(pid, *exprs):
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "query=%s;" % e} for e in exprs]}


def num(pid, expr):
    return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": expr, "isInteger": False}


def b(pid, v):
    return {"btType": "BTMParameterBoolean-144", "parameterId": pid, "value": v}


def en(pid, enum, value, ns=""):
    return {"btType": "BTMParameterEnum-145", "parameterId": pid, "enumName": enum, "value": value, "namespace": ns}


cache = {}
fx = write_feature(BASE, {"btType": "BTMFeature-134", "featureType": "jpsFixture", "name": "K0 JPS fixture (inside z 0; outside z 20 with 3 faces left of x 30000, 1 right; plane x 30000; point at x 30050)", "namespace": NS_FIX, "parameters": []}, cache, client=c)
fid = fx["feature"]["featureId"]
print("fixture:", fx.get("featureState", {}).get("featureStatus"), fid)


def of(name, entity):
    return 'qCreatedBy(makeId("%s") + "%s", EntityType.%s)' % (fid, name, entity)


params = [q("startSplit", of("seam", "FACE")), q("endSplit"), q("insidePoint", of("pt", "VERTEX")), q("insideProfile", of("inside", "BODY")), num("insideOffset", "0 mm"),
          en("outsideMode", "JoinOutsideMode", "SAME", NS_JPS), q("outsideProfile", of("outside", "BODY")), num("outsideOffset", "0 mm"), b("endFillet", False), b("keepInputs", True)]
if args.get("fillet"):
    params += [b("startFillet", True), num("startRadius", args["fillet"] + " mm"), en("startFilletEdge", "JoinFilletEdge", args.get("edge", "OUTSIDE"), NS_JPS), b("startKeepOpposite", True)]
else:
    params += [b("startFillet", False)]
jp = write_feature(BASE, {"btType": "BTMFeature-134", "featureType": "joinProfileSurfaces", "name": "K1 Join profile surfaces, start split on a 3-edge / 1-edge seam -> 5 faces, area 22000, ONE loft face", "namespace": NS_JPS, "parameters": params}, cache, client=c)
jid = jp["feature"]["featureId"]
print("join:", jp.get("featureState", {}).get("featureStatus"), jid)

S = """function(context is Context, queries)
{
    var out = [];
    const bodies = qCreatedBy(makeId("%s"), EntityType.BODY);
    for (var body in evaluateQuery(context, bodies))
    {
        const faces = evaluateQuery(context, qOwnedByBody(body, EntityType.FACE));
        var strip = [];
        for (var f in faces)
        {
            const bx = evBox3d(context, { "topology" : f });
            if (bx.maxCorner[0] - bx.minCorner[0] < 0.05 * millimeter && abs((bx.minCorner[0] - 30000 * millimeter) / millimeter) < 1)
                strip = append(strip, f);
        }
        var areas = "";
        for (var f in strip) areas = areas ~ " " ~ roundToPrecision(evArea(context, { "entities" : f }) / millimeter ^ 2, 1);
        out = append(out, "R body: faces " ~ size(faces) ~ ", edges " ~ size(evaluateQuery(context, qOwnedByBody(body, EntityType.EDGE))) ~ " | faces in the cut plane x = 30000: " ~ size(strip) ~ " (areas mm2:" ~ areas ~ ")");
    }
    out = append(out, "R bodies " ~ size(evaluateQuery(context, bodies)));
    return out;
}""" % jid
r = c.post(f"{BASE}/featurescript", json_data={"script": S})
for n in r.get("notices") or []:
    if n.get("level") == "ERROR":
        print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("R "):
        print(v)
