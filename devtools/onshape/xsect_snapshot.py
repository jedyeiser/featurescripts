"""Snapshot / compare the EI and Cross Section results stored in xSection Part Studios.

Reads the "CrossSectionAnalysis" attribute on the origin (xSectStorage.fs) through the eval API and records,
per EI feature and station: x, EI_eff, GJ_eff, neutral axis. Save before a physics change, compare after.

usage (repo root, Git Bash):
    PYTHONPATH=. MSYS_NO_PATHCONV=1 python devtools/onshape/xsect_snapshot.py save    <label>
    PYTHONPATH=. MSYS_NO_PATHCONV=1 python devtools/onshape/xsect_snapshot.py compare <label>
Snapshots are stored in devtools/onshape/fingerprints/xsect_<label>.json.
"""
import json
import os
import re
import sys

from sync.core.client import OnshapeClient

DOC = json.load(open("xSection/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
STUDIOS = {"Test": "489f267862a807871c8aa5f5", "SKH_Trials": "10c071f677eb7c041f8c3e77", "ROY_Test": "b406721e2f4fa7a0e28b2810"}
STORE = os.path.join(os.path.dirname(__file__), "fingerprints")

SCRIPT = r'''
function(context is Context, queries)
{
    var out = [];
    const data = getAttribute(context, { "entity" : qOrigin(EntityType.BODY), "name" : "CrossSectionAnalysis" });
    if (data == undefined)
    {
        return ["none"];
    }
    for (var entry in data)
    {
        for (var s in entry.value.details.crossSections)
        {
            out = append(out, entry.key ~ "|" ~ toString(s.xCoord / millimeter) ~ "|" ~ toString(s.EI_eff / (newton * meter * meter))
                ~ "|" ~ toString(s.GJ_eff / (newton * meter * meter)) ~ "|" ~ toString(s.neutralAxisY / millimeter));
        }
    }
    return out;
}
'''


def snapshot():
    c = OnshapeClient()
    result = {}
    for name, eid in STUDIOS.items():
        r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{eid}/featurescript", json_data={"script": SCRIPT})
        rows = [s for s in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))) if "|" in s]
        stations = []
        for row in rows:
            key, x, ei, gj, na = row.split("|")
            stations.append({"feature": key, "x": float(x), "EI": float(ei), "GJ": float(gj), "NA": float(na)})
        result[name] = stations
        print("%-12s %d stations" % (name, len(stations)))
    return result


def main():
    mode, label = sys.argv[1], sys.argv[2]
    path = os.path.join(STORE, "xsect_%s.json" % label)
    now = snapshot()
    if mode == "save":
        json.dump(now, open(path, "w"), indent=1)
        print("saved", path)
        return 0
    before = json.load(open(path))
    for name in STUDIOS:
        old, new = before.get(name, []), now.get(name, [])
        if len(old) != len(new):
            print("%s: station count %d -> %d" % (name, len(old), len(new)))
            continue
        if not old:
            continue
        for key in ("EI", "GJ", "NA"):
            ratios = [n[key] / o[key] for o, n in zip(old, new) if abs(o[key]) > 1e-9]
            if ratios:
                print("%-12s %-3s new/old: min %.3f  median %.3f  max %.3f" % (
                    name, key, min(ratios), sorted(ratios)[len(ratios) // 2], max(ratios)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
