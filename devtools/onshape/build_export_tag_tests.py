"""Build the Export tag test cases (export_tag_cases.py) as real features in "Export Tests" (upsert by name).

The cases are appended to the feature tree as "ZT<n> <title>"; the user's own tags are never touched. Re-running
replaces the cases in place. Station table with a Station definition is NOT built here (it needs a FeatureList
selection): it is checked by hand, see publish_tools/README.md "Export tag".

usage (repo root):
    PYTHONPATH=. ONSHAPE_CALLER=export_tag_build ONSHAPE_CALL_CAP=40 python devtools/onshape/build_export_tag_tests.py [ZT1 ...] [--clean]

--clean deletes every "ZT" case instead of building. Cost: 1 features GET + 1 parts GET + one POST per case.
This writes to the studio: do not run it while the user is editing there (memory: playwright-and-live-safety).
"""
import copy
import sys

from sync.core.client import OnshapeClient
from devtools.onshape.fsapi import write_feature, delete_feature, feature_status, q, qids, b, s, num, en, integer
from devtools.onshape.export_tag_cases import CASES, PARTS, STUDIO, PREFIX

c = OnshapeClient()
D, W, E = STUDIO
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"


def main():
    only = [a for a in sys.argv[1:] if not a.startswith("--")]
    cache = {}
    tree = c.get(f"{BASE}/features")
    cache["tree"] = tree
    if "--clean" in sys.argv:
        for x in list(tree["features"]):
            if x["name"].startswith(PREFIX):
                delete_feature(BASE, x["featureId"], cache)
                print("deleted", x["name"])
        return

    template = [x for x in tree["features"] if x["featureType"] == "exportTag"]
    if not template:
        raise SystemExit("no existing Export tag feature to take the namespace from")
    namespace = template[0].get("namespace", "")
    by_name = {x["name"]: x for x in tree["features"]}

    part_ids = {}
    for p in c.get(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/parts"):
        part_ids[p["name"]] = p["partId"]

    failed = 0
    for case in CASES:
        if only and case["key"] not in only:
            continue
        name = "%s %s" % (case["key"], case["title"])
        part_name = PARTS[case["part"]]
        if part_name not in part_ids:
            print("%-8s SKIP: part '%s' not found" % (case["key"], part_name))
            continue
        params = [qids("part", [part_ids[part_name]]), s("bodyName", part_name), s("exportName", case["stem"])]
        for spec in case["params"]:
            kind = spec[0]
            if kind == "b":
                params.append(b(spec[1], spec[2]))
            elif kind == "s":
                params.append(s(spec[1], spec[2]))
            elif kind == "num":
                params.append(num(spec[1], spec[2]))
            elif kind == "int":
                params.append(integer(spec[1], spec[2]))
            elif kind == "enum":
                params.append(en(spec[1], spec[2], spec[3], namespace))
            elif kind == "copy":
                src = by_name[spec[2]]
                params.append(copy.deepcopy([p for p in src["parameters"] if p["parameterId"] == spec[1]][0]))
        feature = {"btType": "BTMFeature-134", "featureType": "exportTag", "name": name, "namespace": namespace, "parameters": params}
        r = write_feature(BASE, feature, cache)
        status = r.get("featureState", {}).get("featureStatus")
        want = case["expect"]["status"]
        print("%-8s %-8s (expect %s) %s" % (case["key"], status, want, name))
        if status != want:
            failed += 1
    print("built; %d case(s) with an unexpected status (the check script reads the details)" % failed)


if __name__ == "__main__":
    main()
