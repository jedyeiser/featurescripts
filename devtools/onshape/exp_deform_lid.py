"""Experiment runner for the CNT Testing studio (curveMapping document): set parameters on the `test_Deform` feature,
regenerate, then report the deformed body's face count / area against the source's.

  PYTHONPATH=. python devtools/onshape/exp_deform_lid.py approximationMaxCPs=50 useFacePoints=true
  PYTHONPATH=. python devtools/onshape/exp_deform_lid.py            # no overrides: just measure

Values: true/false -> boolean; a number (optionally with a unit, e.g. "0.01 mm") -> quantity/expression; anything else -> enum value.
API budget: 1 features GET + 1 POST + 1 eval per run.
"""
import json
import os
import re
import sys
import time

from sync.core.client import OnshapeClient
from devtools.onshape.fsapi import write_feature

c = OnshapeClient()
D, W = "08e8748f2ef24eea16072b75", "e24fbbae7712ef62dd256c89"
E = os.environ.get("EXP_STUDIO", "d643cd8a21dfa72cb2ff26f8")   # EXP_STUDIO=3bc0438ef2ad392db2577808: CNT Testing Copy 1 (dev sandbox)
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
FEATURE = os.environ.get("EXP_FEATURE", "test_Deform")
SOURCE, RESULT = "filleted_top_surf", "filleted_top_surf (deformed)"

tree = {}
f = c.get(f"{BASE}/features")
tree["tree"] = f
feature = [x for x in f["features"] if x["name"] == FEATURE][0]
# a feature is bound to the microversion of the deform tab it was last written with; a pushed tab changes it
_tab = [e for e in c.list_elements(D, W) if e["name"] == "deform" and e["elementType"] == "FEATURESTUDIO"][0]
if os.environ.get("EXP_REBIND"):
    feature["namespace"] = "e%s::m%s" % (_tab["id"], _tab["microversionId"])
for arg in sys.argv[1:]:
    k, v = arg.split("=", 1)
    found = [x for x in feature["parameters"] if x["parameterId"] == k]
    if v.startswith("@"):                    # an enum: k=@EnumName:VALUE
        enum_name, enum_value = v[1:].split(":")
        if found:
            found[0]["value"] = enum_value
        else:
            feature["parameters"].append({"btType": "BTMParameterEnum-145", "parameterId": k, "enumName": enum_name, "value": enum_value,
                                          "namespace": feature["namespace"]})
        continue
    if not found and v.lower() not in ("true", "false") and any(ch.isdigit() for ch in v) and " " in v:      # a quantity the saved feature predates ("0.005 mm")
        feature["parameters"].append({"btType": "BTMParameterQuantity-147", "parameterId": k, "expression": v, "isInteger": False})
        continue
    if not found and v.lower() in ("true", "false"):      # a parameter the saved feature predates: add it
        feature["parameters"].append({"btType": "BTMParameterBoolean-144", "parameterId": k, "value": v.lower() == "true"})
        continue
    p = found[0]
    if "value" in p and isinstance(p["value"], bool):
        p["value"] = v.lower() == "true"
    elif "expression" in p:
        p["expression"] = v
    else:
        p["value"] = v
_t0 = time.perf_counter()
r = write_feature(BASE, feature, tree, client=c)
print("write+regen seconds: %.1f" % (time.perf_counter() - _t0))
print("status:", r.get("featureState", {}).get("featureStatus"))

SCRIPT = """function(context is Context, queries)
{
    var out = [];
    for (var b in evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.SHEET)))
    {
        var nm = "?";
        try silent { nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }); }
        if (nm != "%s" && nm != "%s")
            continue;
        var faces = evaluateQuery(context, qOwnedByBody(b, EntityType.FACE));
        out = append(out, "R " ~ nm ~ " | faces " ~ size(faces) ~ " | area " ~ roundToPrecision(evArea(context, { "entities" : qOwnedByBody(b, EntityType.FACE) }) / (millimeter ^ 2), 0));
    }
    return out;
}""" % (SOURCE, RESULT)
res = c.post(f"{BASE}/featurescript", json_data={"script": SCRIPT})
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(res.get("result"))):
    if v.startswith("R "):
        print(v)
