"""Insert the Clean surfaces test features into the clean_surfaces document's test Part Studio (upsert by name).

The Part Studio 09b7801b... holds the user's challenging test surface (4 sheet bodies, 196 faces; survey 2026-11-07).
Cases (feature names say the expectation):
  D1 ski diagnose -> copy + coloured faces (counts printed by check_clean_surfaces_tests.py)

usage (repo root): ONSHAPE_CALL_CAP=12 PYTHONPATH=. python devtools/onshape/build_clean_surfaces_tests.py
"""
import json

from sync.core.client import OnshapeClient
from devtools.onshape.fsapi import write_feature, cached_tree, q, num

c = OnshapeClient()
DOC = json.load(open("clean_surfaces/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
E = "09b7801b1b6f28c0221acf7d"
BASE = "/api/v10/partstudios/d/%s/w/%s/e/%s" % (D, W, E)
TAB = [e for e in c.list_elements(D, W) if e["name"] == "clean_surfaces" and e["elementType"] == "FEATURESTUDIO"][0]
NS = "e%s::m%s" % (TAB["id"], TAB["microversionId"])
CACHE = {}


def case(name, params):
    feature = {"btType": "BTMFeature-134", "featureType": "cleanSurfaces", "name": name, "namespace": NS, "parameters": params}
    r = write_feature(BASE, feature, CACHE, client=c)
    print("%-40s %s" % (name, r.get("featureState", {}).get("featureStatus")))
    return r


tree = cached_tree(BASE, CACHE, c)
print("existing features:", [(x["name"], x.get("featureType")) for x in tree["features"]])
ALL_SHEETS = "qBodyType(qEverything(EntityType.BODY), BodyType.SHEET)"
case("D1 ski diagnose -> copy + coloured faces",
     [q("surfaces", ALL_SHEETS), num("sliverWidth", "0.1 mm"), num("shortEdge", "0.01 mm")])
