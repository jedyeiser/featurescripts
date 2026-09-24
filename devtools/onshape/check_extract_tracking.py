"""Check the Extract variables Track option and modified* keys in Variable_tools' "Tracking tests"
Part Studio (built by build_extract_tracking_tests.py). Nothing after the move changes the sheet,
so the end of the tree is the state the Ruled surface sees.

usage (repo root): PYTHONPATH=. python devtools/onshape/check_extract_tracking.py
"""
import json
import re
import sys

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("variable_tools/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
E = [e["id"] for e in c.list_elements(D, W) if e["name"] == "Tracking tests"][0]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"

SCRIPT = r'''function(context is Context, queries)
{
    // "n: x0..x1 / z0..z1" per edge (mm), so each result can be read and compared
    const describe = function(q is Query) returns array
        {
            var out = [];
            for (var e in evaluateQuery(context, q))
            {
                const ends = evEdgeTangentLines(context, { "edge" : e, "parameters" : [0, 1] });
                out = append(out, "x " ~ roundToPrecision(ends[0].origin[0] / millimeter, 3) ~ ".." ~ roundToPrecision(ends[1].origin[0] / millimeter, 3)
                    ~ " z " ~ roundToPrecision(ends[0].origin[2] / millimeter, 3) ~ ".." ~ roundToPrecision(ends[1].origin[2] / millimeter, 3));
            }
            return out;
        };
    const isRightEdgeAt = function(q is Query, x) returns boolean
        {
            const found = evaluateQuery(context, q);
            if (size(found) != 1)
            {
                return false;
            }
            const ends = evEdgeTangentLines(context, { "edge" : found[0], "parameters" : [0, 1] });
            return abs(ends[0].origin[0] - x) < 1e-6 * meter && abs(ends[1].origin[0] - x) < 1e-6 * meter
                && abs(abs(ends[1].origin[2] - ends[0].origin[2]) - 50 * millimeter) < 1e-6 * meter;
        };
    const held = getQueryVariable(context, "edge_held");
    const tracked = getQueryVariable(context, "edge_tracked");
    const moved = getQueryVariable(context, "moved_edges");
    var rows = [];
    for (var v in [["held", held], ["tracked", tracked], ["moved", moved]])
    {
        rows = append(rows, "  " ~ v[0] ~ " at the end of the tree: " ~ size(evaluateQuery(context, v[1])) ~ " entities, edges "
            ~ toString(describe(qEntityFilter(v[1], EntityType.EDGE))));
    }
    // On the sheet itself (the Ruled surface after it is built from the tracked edge, so its edge is derived too).
    const sheetEdges = qOwnedByBody(qCreatedBy(makeId(SHEET), EntityType.BODY), EntityType.EDGE);
    const heldOk = isRightEdgeAt(held, 110 * millimeter);
    rows = append(rows, (heldOk ? "PASS" : "FAIL") ~ " held (no Track) follows the extend to the edge at x 110");
    const trackedOk = isRightEdgeAt(qIntersection([tracked, sheetEdges]), 110 * millimeter)
        && isQueryEmpty(context, qEntityFilter(tracked, EntityType.FACE)) && isQueryEmpty(context, qEntityFilter(tracked, EntityType.BODY));
    rows = append(rows, (trackedOk ? "PASS" : "FAIL") ~ " tracked: on the sheet exactly the moved edge at x 110, no faces or bodies");
    var movedHasRight = false;
    for (var e in evaluateQuery(context, moved))
    {
        movedHasRight = movedHasRight || isRightEdgeAt(e, 110 * millimeter);
    }
    rows = append(rows, (movedHasRight ? "PASS" : "FAIL") ~ " modifiedEdges of Move boundary includes the moved edge");
    return rows;
}'''


def main():
    feats = c.get(f"{BASE}/features")
    failed = 0
    states = feats.get("featureStates", {})
    for x in feats["features"]:
        status = states.get(x["featureId"], {}).get("featureStatus")
        if x["name"].startswith("R1 "):
            ok = status == "OK"
            failed += not ok
            print("PASS" if ok else "FAIL", x["name"], "--", status)
    sheet = [x["featureId"] for x in feats["features"] if x["name"].startswith("Surface 100 x 50")][0]
    r = c.post(f"{BASE}/featurescript", json_data={"script": SCRIPT.replace("SHEET", '"%s"' % sheet)})
    errors = [n.get("message") for n in (r.get("notices") or []) if n.get("level") == "ERROR"]
    if errors:
        print("FAIL check did not run:", "; ".join(errors))
        return 1
    for row in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
        if row.startswith("BTFS"):
            continue
        print(row)
        failed += row.startswith("FAIL")
    print("%d failed" % failed if failed else "all passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
