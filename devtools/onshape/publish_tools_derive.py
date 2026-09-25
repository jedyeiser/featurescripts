"""One-off: insert Derive_Parts_V1 (importDerived of RD 20TAC 28 Parts @V1, all bodies + MCs) into the
publish_tools test doc. usage: python devtools/onshape/publish_tools_derive.py <derived-feature-template.json>
The template is a derived feature JSON read from the source Parts studio (features API)."""
import json, sys
sys.path.insert(0, r"C:/Users/jed.yeiser/Documents/featurescripts")
from devtools.onshape.fsapi import c, features
SRC_D, SRC_V, SRC_E, SRC_M = "6212b76fdc6e7eca7cc56d8e", "2a679ea0cd06d118ae47d0e5", "780daf2cb652c08dba07e881", "db658073e57dee222b4dbec8"
D, W, E = "73271cfcd708b3f5e3fc315c", "fea83d30106dba0a4e066761", "bb2cddb24faf53d9d043c38e"
tmpl = json.load(open(sys.argv[1]))[0]
params = json.loads(json.dumps(tmpl["parameters"]))
for p in params:
    p.pop("nodeId", None)
    if p.get("parameterId") == "partStudio":
        p["namespace"] = "d%s::v%s::e%s::m%s" % (SRC_D, SRC_V, SRC_E, SRC_M)
        p["partQuery"].pop("nodeId", None)
        p["partQuery"]["queries"] = [{"btType": "BTMIndividualQuery-138", "queryStatement": None,
                                      "queryString": "query=qEverything(EntityType.BODY);", "deterministicIds": []}]
f = features(D, W, E)
feature = {"btType": "BTMFeature-134", "featureType": "importDerived", "name": "Derive_Parts_V1",
           "namespace": "", "parameters": params}
body = {"btType": "BTFeatureDefinitionCall-1406", "feature": feature,
        "serializationVersion": f["serializationVersion"], "sourceMicroversion": f["sourceMicroversion"]}
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/features", body)
print(r.get("featureState"))
