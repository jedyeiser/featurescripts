"""Regression fingerprint of a Part Studio's custom features (default: driven_offset Design_Master).

For every feature from a Feature Studio (namespace "e..." = this workspace, "d..." = another
document) it records bodies / edges / total edge length / faces / total area created by that
feature and surviving at the end of the tree, plus the body count and total edge length of the
whole studio. Save before a change, compare after; identical output = no geometric change.

usage (repo root, Git Bash):
    PYTHONPATH=. MSYS_NO_PATHCONV=1 python devtools/onshape/fingerprint.py save    <label> [--complex]
    PYTHONPATH=. MSYS_NO_PATHCONV=1 python devtools/onshape/fingerprint.py compare <label> [--complex]
    options: --did/--wid/--eid to target another Part Studio; --config "List_x=V;..." for any configuration.

--complex = Design_Master's "intentionally complicated" test configuration
(tail_notch;Slotted;Baseline). Fingerprints are stored in devtools/onshape/fingerprints/.
Two baselines taken 2026-09-23 before the curve_core rewire: design_master_default.txt,
design_master_complex.txt (both matched afterwards).
"""
import argparse
import difflib
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from fsapi import features  # noqa: E402
from fseval import run  # noqa: E402

DESIGN_MASTER = ("f61d2c000ab2d1240776342e", "5b11f323ab31b04cba8b36ef", "643b702dc551feb3cc001826")
COMPLEX = "List_yCq3INuTP36uQ5=tail_notch;List_IKz63ayGm3aDc4=Slotted;List_zOpUeTVJ7l0NNs=Baseline"
STORE = os.path.join(os.path.dirname(__file__), "fingerprints")


def script_for(did, wid, eid):
    custom = [x for x in features(did, wid, eid)["features"] if x.get("namespace", "")[:1] in ("e", "d")]
    rows = []
    for x in custom:
        fid = x["featureId"]
        rows.append('''    {
        const b = qCreatedBy(makeId("%s"), EntityType.BODY);
        const e = qCreatedBy(makeId("%s"), EntityType.EDGE);
        const fc = qCreatedBy(makeId("%s"), EntityType.FACE);
        out = append(out, "%s | %s | bodies " ~ size(evaluateQuery(context, b)) ~ " | edges " ~ size(evaluateQuery(context, e))
            ~ " len " ~ toString(roundToPrecision(evLength(context, { "entities" : e }) / millimeter, 5))
            ~ " | faces " ~ size(evaluateQuery(context, fc)) ~ " area " ~ toString(roundToPrecision(evArea(context, { "entities" : fc }) / (millimeter * millimeter), 4)));
    }''' % (fid, fid, fid, x["name"].replace('"', "'"), x["featureType"]))
    return "function(context is Context, queries)\n{\n    var out = [];\n" + "\n".join(rows) + '''
    var total = 0 * meter;
    for (var body in evaluateQuery(context, qEverything(EntityType.BODY)))
    {
        total += evLength(context, { "entities" : qOwnedByBody(body, EntityType.EDGE) });
    }
    out = append(out, "ALL bodies " ~ size(evaluateQuery(context, qEverything(EntityType.BODY))) ~ " total edge length " ~ toString(roundToPrecision(total / millimeter, 4)));
    return out;
}'''


def fingerprint(did, wid, eid, config):
    import json
    import re
    result = run(script_for(did, wid, eid), did, wid, eid, config)
    blob = json.dumps(result.get("result"))
    return [s for s in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', blob) if s not in ("BTFSValueString", "BTFSValueArray")]


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("action", choices=["save", "compare"])
    ap.add_argument("label")
    ap.add_argument("--complex", action="store_true")
    ap.add_argument("--config")
    ap.add_argument("--did", default=DESIGN_MASTER[0])
    ap.add_argument("--wid", default=DESIGN_MASTER[1])
    ap.add_argument("--eid", default=DESIGN_MASTER[2])
    a = ap.parse_args()
    config = COMPLEX if a.complex else a.config
    os.makedirs(STORE, exist_ok=True)
    path = os.path.join(STORE, a.label + ".txt")
    lines = fingerprint(a.did, a.wid, a.eid, config)
    if a.action == "save":
        open(path, "w").write("\n".join(lines) + "\n")
        print("saved %d lines to %s" % (len(lines), path))
    else:
        before = open(path).read().splitlines()
        diff = list(difflib.unified_diff(before, lines, "saved", "now", lineterm=""))
        print("\n".join(diff) if diff else "IDENTICAL (%d lines)" % len(lines))
