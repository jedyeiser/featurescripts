"""Solve GJ cases in the "xSection tests" Part Studio (upsert by name), then check them through the eval API.

  SG1 Solve GJ on X1                 -> OK; X1's stored sections (details) and table both hold GJ 125.31
  (An empty selection gives ERROR "Select the cross section feature." -- verified by hand once; not kept here because
   check_xsection_tests.py requires every feature in the studio to regenerate OK.)

Before 2026-09-25 Solve GJ wrote its GJ into a copy of the stored sections that was never saved back (only the table
column changed) and swallowed any write failure.

usage (repo root): PYTHONPATH=. python devtools/onshape/build_solve_gj_test.py
"""
import json

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("xSection/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
ELEMENTS = c.list_elements(D, W)
E = [e["id"] for e in ELEMENTS if e["name"] == "xSection tests" and e["elementType"] == "PARTSTUDIO"][0]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TAB = [e for e in ELEMENTS if e["name"] == "GJ_Feature" and e["elementType"] == "FEATURESTUDIO"][0]
NS = "e%s::m%s" % (TAB["id"], TAB["microversionId"])


def upsert(name, feature_ids):
    f = c.get(f"{BASE}/features")
    feature = {"btType": "BTMFeature-134", "featureType": "solveGJ", "name": name, "namespace": NS, "parameters": [
        {"btType": "BTMParameterFeatureList-1749", "parameterId": "xSectFeature", "featureIds": feature_ids}]}
    existing = [x for x in f["features"] if x["name"] == name]
    body = {"btType": "BTFeatureDefinitionCall-1406", "feature": feature,
            "serializationVersion": f["serializationVersion"], "sourceMicroversion": f["sourceMicroversion"]}
    if existing:
        feature["featureId"] = existing[0]["featureId"]
        r = c.post(f"{BASE}/features/featureid/{existing[0]['featureId']}", body)
    else:
        r = c.post(f"{BASE}/features", body)
    status = r.get("featureState", {}).get("featureStatus")
    print("%-60s %s" % (name, status))
    return status


feats = c.get(f"{BASE}/features")["features"]
x1 = [f["featureId"] for f in feats if f["name"].startswith("X1 EI_iso_rect")][0]
s13 = upsert("SG1 Solve GJ on X1 -> OK, stored GJ 125.31", [x1])

script = """function(context is Context, queries) {
    const data = getAttribute(context, { "entity" : qOrigin(EntityType.BODY), "name" : "CrossSectionAnalysis" })["%s"];
    var out = [];
    for (var s in data.details.crossSections) { out = append(out, toString(roundToPrecision(s.GJ_eff / (newton * meter * meter), 2))); }
    var table = [];
    for (var i = 1; i < size(data.tableData.crossSections); i += 1) { table = append(table, toString(data.tableData.crossSections[i][3])); }
    return ["details " ~ toString(out), "table " ~ toString(table)];
}""" % x1
r = c.post(f"{BASE}/featurescript", json_data={"script": script})


def unwrap(v):
    if isinstance(v, dict) and "value" in v:
        return unwrap(v["value"])
    if isinstance(v, list):
        return [unwrap(x) for x in v]
    return v


lines = unwrap(r.get("result", {}))
for line in lines:
    print(" ", line)
ok = s13 == "OK" and all("125.3" in line for line in lines) and len(lines) == 2
print("PASS" if ok else "FAIL")
