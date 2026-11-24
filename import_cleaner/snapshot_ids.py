"""Snapshot the identity of a Part Studio (part ids, names, face / edge ids, feature statuses) and compare two snapshots.

    PYTHONPATH=. python import_cleaner/snapshot_ids.py save <label> [document_id workspace_id element_id]
    PYTHONPATH=. python import_cleaner/snapshot_ids.py compare <labelA> <labelB>

Defaults to the 'lab_enclose' import studio of the import_cleaner document. Snapshots are JSON files in import_cleaner/snapshots/.
Cost: 3 REST calls per save (parts, a FeatureScript evaluation, features). Part ids are the deterministic ids a Derive picks parts by; face ids
are the deterministic ids of faces (what a mate connector or sketch references).
"""
import json
import os
import re
import sys
from pathlib import Path

from sync.core.client import OnshapeClient

D, W, E = "ad26c2fa8bc9abaf250c5e8a", "d713b8e6302cdb9bc9ad6308", "21efb3b681d28f70f0d1d256"
DIR = Path(__file__).parent / "snapshots"


EVAL = """function(context is Context, queries)
{
    var out = [];
    const bodies = qUnion([qBodyType(qEverything(EntityType.BODY), BodyType.SOLID), qBodyType(qEverything(EntityType.BODY), BodyType.SHEET), qBodyType(qEverything(EntityType.BODY), BodyType.WIRE)]);
    for (var b in evaluateQuery(context, bodies))
    {
        var faces = [];
        for (var f in evaluateQuery(context, qOwnedByBody(b, EntityType.FACE))) { faces = append(faces, transientQueriesToStrings(f)); }
        var edges = [];
        for (var x in evaluateQuery(context, qOwnedByBody(b, EntityType.EDGE))) { edges = append(edges, transientQueriesToStrings(x)); }
        out = append(out, transientQueriesToStrings(b) ~ "|" ~ join(faces, ",") ~ "|" ~ join(edges, ","));
    }
    return out;
}
"""


def take(c, d, w, e):
    import re
    parts = c.get("/api/v10/parts/d/%s/w/%s/e/%s" % (d, w, e))
    r = c.post("/api/v10/partstudios/d/%s/w/%s/e/%s/featurescript" % (d, w, e), json_data={"script": EVAL})
    by_id = {}
    for s in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
        if "|" in s:
            body, faces, edges = s.split("|")
            by_id[body] = {"faces": [x for x in faces.split(",") if x], "edges": [x for x in edges.split(",") if x]}
    # the RAW imported bodies: the studio evaluated with only its first feature (the Import) before the cleaner
    raw = {}
    r2 = c.post("/api/v10/partstudios/d/%s/w/%s/e/%s/featurescript" % (d, w, e), json_data={"script": EVAL}, query_params={"rollbackBarIndex": "1"})
    for s in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r2.get("result"))):
        if "|" in s:
            body, faces, edges = s.split("|")
            raw[body] = {"faces": [x for x in faces.split(",") if x], "edges": [x for x in edges.split(",") if x]}
    feats = c.get("/api/v10/partstudios/d/%s/w/%s/e/%s/features" % (d, w, e))
    out = {"parts": [], "features": [], "raw": [{"bodyId": k, "faces": sorted(v["faces"]), "edges": sorted(v["edges"])} for k, v in raw.items()]}
    for p in parts:
        body = by_id.get(p["partId"], {"faces": [], "edges": []})
        out["parts"].append({"name": p["name"], "partId": p["partId"], "bodyType": p.get("bodyType"),
                             "faces": sorted(body["faces"]), "edges": sorted(body["edges"])})
    for f in feats["features"]:
        out["features"].append({"name": f["name"], "type": f.get("featureType"),
                                "status": feats.get("featureStates", {}).get(f["featureId"], {}).get("featureStatus")})
    return out

def main():
    cmd = sys.argv[1]
    DIR.mkdir(exist_ok=True)
    if cmd == "save":
        label = sys.argv[2]
        d, w, e = (sys.argv[3:6] + [D, W, E][len(sys.argv[3:6]):])[:3] if len(sys.argv) > 3 else (D, W, E)
        snap = take(OnshapeClient(), d, w, e)
        (DIR / (label + ".json")).write_text(json.dumps(snap, indent=1))
        print("saved", label, ":", len(snap["parts"]), "parts,", sum(len(p["faces"]) for p in snap["parts"]), "faces")
        for p in snap["parts"]:
            print("  %-28s %-6s %-10s %3d faces %3d edges" % (p["name"], p["partId"], p["bodyType"], len(p["faces"]), len(p["edges"])))
    else:
        a = json.loads((DIR / (sys.argv[2] + ".json")).read_text())
        b = json.loads((DIR / (sys.argv[3] + ".json")).read_text())
        bn = {p["name"]: p for p in b["parts"]}
        for p in a["parts"]:
            q = bn.get(p["name"])
            if q is None:
                print("%-28s part id %-6s -> MISSING in %s" % (p["name"], p["partId"], sys.argv[3]))
                continue
            same_faces = len(set(p["faces"]) & set(q["faces"]))
            same_edges = len(set(p["edges"]) & set(q["edges"]))
            print("%-28s part id %-6s -> %-6s (%s) | faces same %d/%d | edges same %d/%d" % (
                p["name"], p["partId"], q["partId"], "SAME" if p["partId"] == q["partId"] else "CHANGED", same_faces, len(p["faces"]), same_edges, len(p["edges"])))
        ra = a.get("raw", [])
        rb = b.get("raw", [])
        if ra and rb:
            ids_a = {x["bodyId"] for x in ra}
            ids_b = {x["bodyId"] for x in rb}
            fa_set = {f for x in ra for f in x["faces"]}
            fb_set = {f for x in rb for f in x["faces"]}
            print("RAW import: body ids same %d/%d | face ids same %d/%d | edge ids same %d/%d" % (
                len(ids_a & ids_b), len(ids_a), len(fa_set & fb_set), len(fa_set),
                len({e for x in ra for e in x["edges"]} & {e for x in rb for e in x["edges"]}), len({e for x in ra for e in x["edges"]})))
        for fa, fb in zip(a["features"], b["features"]):
            if fa["status"] != fb["status"]:
                print("feature status changed:", fa["name"], fa["status"], "->", fb["status"])


main()
