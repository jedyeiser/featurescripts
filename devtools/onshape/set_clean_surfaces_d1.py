"""Set D1's Faces to keep to the pinch-fillet face (area 155.263 mm2, user 2026-11-06) by a point-on-face query."""
import json, re, os
from sync.core.client import OnshapeClient
from devtools.onshape.fsapi import write_feature, q, num, en, b, s
from devtools.onshape.fseval import run
c = OnshapeClient()
DOC = json.load(open("clean_surfaces/.document.json"))
D, W, E = DOC["document_id"], DOC["workspace_id"], "09b7801b1b6f28c0221acf7d"
BASE = "/api/v10/partstudios/d/%s/w/%s/e/%s" % (D, W, E)
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    var params = [];
    for (var i = 1; i < 10; i += 1)
    {
        for (var j = 1; j < 10; j += 1)
        {
            params = append(params, vector(i / 10, j / 10));
        }
    }
    for (var f in evaluateQuery(context, qEverything(EntityType.FACE)))
    {
        var a = evArea(context, { "entities" : f }) / millimeter ^ 2;
        if (abs(a - 155.263) < 0.01)
        {
            for (var pl in evFaceTangentPlanes(context, { "face" : f, "parameters" : params, "returnUndefinedOutsideFace" : true }))
            {
                if (pl != undefined)
                {
                    var p = pl.origin / millimeter;
                    out = append(out, "PT " ~ p[0] ~ " " ~ p[1] ~ " " ~ p[2]);
                    break;
                }
            }
        }
    }
    return out;
}"""
x, y, z = "1643.6499958755026", "64.50023964494905", "13.310977432416358"
TAB = [e for e in c.list_elements(D, W) if e["name"] == "clean_surfaces" and e["elementType"] == "FEATURESTUDIO"][0]
f = {"btType": "BTMFeature-134", "featureType": "cleanSurfaces", "name": "D1 ski diagnose -> copy + coloured faces",
     "namespace": "e%s::m%s" % (TAB["id"], TAB["microversionId"]),
     "parameters": [q("surfaces", "qBodyType(qEverything(EntityType.BODY), BodyType.SHEET)"),
                    q("cleanFaces"), q("keepFaces", "qContainsPoint(qEverything(EntityType.FACE), vector(%s, %s, %s) * millimeter)" % (x, y, z)),
                    num("sliverWidth", "0.1 mm"), num("shortEdge", "0.01 mm"), num("maxTangent", "0.5 deg"),
                    en("mode", "CleanMode", os.environ.get("CS_CLEAN", "DIAGNOSE"), "e%s::m%s" % (TAB["id"], TAB["microversionId"])),
                    num("maxChange", os.environ.get("CS_MAXCHANGE", "0.05") + " mm"), b("useCache", os.environ.get("CS_USECACHE", "0") == "1"), q("cachedFaces"), s("cacheKey", ""), s("cacheStatus", "Not resolved yet."),
                    en("colorBy", "CleanColorBy", os.environ.get("CS_MODE", "SEVERITY"), "e%s::m%s" % (TAB["id"], TAB["microversionId"])),
                    num("offsetDistance", os.environ.get("CS_DIST", "0.5") + " mm"), b("offsetFlip", os.environ.get("CS_FLIP", "0") == "1"), en("offsetSide", "OffsetSide", os.environ.get("CS_SIDE", "BOTH"), "e%s::m%s" % (TAB["id"], TAB["microversionId"])), q("sideReference", *([os.environ["CS_REF"]] if os.environ.get("CS_REF") else [])), b("towardReference", os.environ.get("CS_TOWARD", "1") == "1"), b("offsetTrim", os.environ.get("CS_TRIM", "0") == "1"), b("verifyResult", os.environ.get("CS_VERIFY", "0") == "1"), b("markEdges", os.environ.get("CS_MARK", "1") == "1"), b("debugOutput", os.environ.get("CS_DEBUG", "1") == "1")]}
resp = write_feature(BASE, f, {}, client=c)
print(resp.get("featureState", {}).get("featureStatus"))









