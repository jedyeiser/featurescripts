"""Snapshot of the DOWNSTREAM test studio: the bodies a Derive feature created (id, name, faces) and every feature's status.

    PYTHONPATH=. python import_cleaner/snapshot_downstream.py save <label> [derive feature name]
    PYTHONPATH=. python import_cleaner/snapshot_downstream.py compare <a> <b>
"""
import json
import re
import sys
from pathlib import Path

from sync.core.client import OnshapeClient

import os
D, W, E = "ad26c2fa8bc9abaf250c5e8a", "d713b8e6302cdb9bc9ad6308", os.environ.get("IDLAB_E", "d4491b1ced7471dc6f1d6ed9")   # IDLAB_E: another downstream studio
DIR = Path(__file__).parent / "snapshots"
c = OnshapeClient()
B = "/api/v10/partstudios/d/%s/w/%s/e/%s" % (D, W, E)


def save(label, derive_name):
    feats = c.get(B + "/features")
    derive = [f for f in feats["features"] if f["name"] == derive_name][0]
    script = """function(context is Context, queries)
{
    var out = [];
    for (var b in evaluateQuery(context, qCreatedBy(makeId("%s"), EntityType.BODY)))
    {
        var faces = [];
        for (var f in evaluateQuery(context, qOwnedByBody(b, EntityType.FACE))) { faces = append(faces, transientQueriesToStrings(f)); }
        out = append(out, transientQueriesToStrings(b) ~ "|" ~ join(faces, ","));
    }
    return out;
}
""" % derive["featureId"]
    r = c.post(B + "/featurescript", json_data={"script": script})
    ids = {}
    for s in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
        if "|" in s:
            body, faces = s.split("|")
            ids[body] = sorted(x for x in faces.split(",") if x)
    parts = {p["partId"]: p for p in c.get("/api/v10/parts/d/%s/w/%s/e/%s" % (D, W, E))}
    snap = {"derive": derive_name,
            "bodies": [{"partId": k, "name": parts.get(k, {}).get("name"), "bodyType": parts.get(k, {}).get("bodyType"), "faces": v} for k, v in ids.items()],
            "features": [{"name": f["name"], "status": feats["featureStates"].get(f["featureId"], {}).get("featureStatus")} for f in feats["features"]]}
    DIR.mkdir(exist_ok=True)
    (DIR / (label + ".json")).write_text(json.dumps(snap, indent=1))
    print("saved", label, "| bodies created by", derive_name, ":", len(snap["bodies"]))
    for b in snap["bodies"]:
        print("  %-26s %-6s %-6s %3d faces" % (b["name"], b["partId"], b["bodyType"], len(b["faces"])))
    print("  features:", [(f["name"], f["status"]) for f in snap["features"]])


def compare(a_label, b_label):
    a = json.loads((DIR / (a_label + ".json")).read_text())
    b = json.loads((DIR / (b_label + ".json")).read_text())
    bn = {}
    for x in b["bodies"]:
        bn.setdefault(x["name"], []).append(x)
    for x in a["bodies"]:
        cands = bn.get(x["name"], [])
        if not cands:
            print("%-26s %-6s -> MISSING" % (x["name"], x["partId"]))
            continue
        y = cands[0]
        print("%-26s part id %-6s -> %-6s (%s) | faces same %d/%d" % (x["name"], x["partId"], y["partId"],
              "SAME" if x["partId"] == y["partId"] else "CHANGED", len(set(x["faces"]) & set(y["faces"])), len(x["faces"])))
    for fa, fb in zip(a["features"], b["features"]):
        if fa["status"] != fb["status"]:
            print("feature status changed:", fa["name"], fa["status"], "->", fb["status"])
    print("body count %d -> %d" % (len(a["bodies"]), len(b["bodies"])))


if sys.argv[1] == "save":
    save(sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else "Derive_Deformed")
else:
    compare(sys.argv[2], sys.argv[3])

