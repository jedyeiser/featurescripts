"""Create (once) the "Deform lid dev" Part Studio in the curveMapping document: copies of the first four features of the
"CNT Testing" studio (Derived 1, query variable, Join wires 1, test_Deform) with their feature ids kept, so the saved
(compressed) queries keep resolving. A private sandbox for deform.fs experiments: the user edits CNT Testing live.

  PYTHONPATH=. python devtools/onshape/build_deform_lid_dev.py
API budget: ~8 calls.
"""
import json

from sync.core.client import OnshapeClient

c = OnshapeClient()
D, W = "08e8748f2ef24eea16072b75", "e24fbbae7712ef62dd256c89"
SRC_E = "d643cd8a21dfa72cb2ff26f8"      # CNT Testing
NAME = "Deform lid dev"
COPY = ["Derived 1", "###name", "Join wires 1", "test_Deform"]   # in tree order (first '###name' only)

elements = c.list_elements(D, W)
dev = [e["id"] for e in elements if e["name"] == NAME and e["elementType"] == "PARTSTUDIO"]
E = dev[0] if dev else c.post(f"/api/v10/partstudios/d/{D}/w/{W}", {"name": NAME})["id"]
print("studio", NAME, E, "(existing)" if dev else "(created)")

src = c.get(f"/api/v10/partstudios/d/{D}/w/{W}/e/{SRC_E}/features")
base = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
cur = c.get(f"{base}/features")
have = {f["featureId"] for f in cur["features"]}
sm, ser = cur["sourceMicroversion"], cur["serializationVersion"]
taken = set()
for f in src["features"][:4]:
    if f["featureId"] in have:
        continue
    body = {"btType": "BTFeatureDefinitionCall-1406", "feature": f, "serializationVersion": ser, "sourceMicroversion": sm}
    r = c.post(f"{base}/features", body)
    sm = r.get("sourceMicroversion", sm)
    print("%-14s %-20s %s  id %s" % (f["featureType"], f["name"], r.get("featureState", {}).get("featureStatus"), r["feature"]["featureId"]))
