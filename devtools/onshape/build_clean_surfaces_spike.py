"""Insert/refresh the throwaway 'Spike' instance (clean_surfaces_spike tab) in the clean_surfaces test Part Studio."""
import json
from sync.core.client import OnshapeClient
from devtools.onshape.fsapi import write_feature
c = OnshapeClient()
DOC = json.load(open("clean_surfaces/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
BASE = "/api/v10/partstudios/d/%s/w/%s/e/09b7801b1b6f28c0221acf7d" % (D, W)
TAB = [e for e in c.list_elements(D, W) if e["name"] == "clean_surfaces_spike"][0]
f = {"btType": "BTMFeature-134", "featureType": "cleanSurfacesSpike", "name": "SPIKE sliver ops",
     "namespace": "e%s::m%s" % (TAB["id"], TAB["microversionId"]), "parameters": []}
r = write_feature(BASE, f, {}, client=c)
print(r.get("featureState", {}).get("featureStatus"))
