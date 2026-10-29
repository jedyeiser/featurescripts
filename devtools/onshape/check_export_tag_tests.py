"""Check the Export tag test cases (export_tag_cases.py, built by build_export_tag_tests.py) in "Export Tests".

One features GET and ONE FeatureScript eval read everything: each case's status and message (getFeatureError /
getFeatureWarning / getFeatureInfo), and every body whose name starts with "ZT" (type, lowest corner, edge makeup:
lines / arcs / other, via evCurveDefinition), plus how many faces each case created. Cases that are not in the tree
are reported SKIP. Exit status 1 when a case fails.

usage (repo root):
    PYTHONPATH=. ONSHAPE_CALLER=export_tag_check ONSHAPE_CALL_CAP=10 python devtools/onshape/check_export_tag_tests.py [--dump]

--dump also prints every body found. Read-only.
"""
import sys

from sync.core.client import OnshapeClient
from devtools.onshape.export_tag_cases import CASES, STUDIO, PREFIX

c = OnshapeClient()
D, W, E = STUDIO
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TOL = 0.05   # mm

SCRIPT = '''function(context is Context, queries)
{
    var out = [];
    for (var fid in [%s])
    {
        const fId = makeId(fid);
        out = append(out, "msg | " ~ fid ~ " | " ~ toString(getFeatureError(context, fId)) ~ " | " ~ toString(getFeatureWarning(context, fId))
            ~ " | " ~ toString(getFeatureInfo(context, fId)) ~ " | faces " ~ size(evaluateQuery(context, qCreatedBy(fId, EntityType.FACE))));
    }
    for (var body in evaluateQuery(context, qEverything(EntityType.BODY)))
    {
        const name = getProperty(context, { "entity" : body, "propertyType" : PropertyType.NAME });
        if (name is string && indexOf(name, "%s") == 0)
        {
            var kind = "OTHER";
            if (size(evaluateQuery(context, qBodyType(body, BodyType.COMPOSITE))) > 0) { kind = "COMPOSITE"; }
            else if (size(evaluateQuery(context, qBodyType(body, BodyType.SHEET))) > 0) { kind = "SHEET"; }
            const bb = evBox3d(context, { "topology" : body });
            var lines = 0;
            var arcs = 0;
            var other = 0;
            if (kind == "SHEET")
            {
                for (var edge in evaluateQuery(context, qOwnedByBody(body, EntityType.EDGE)))
                {
                    const d = evCurveDefinition(context, { "edge" : edge });
                    if (d is Line) { lines += 1; }
                    else if (d is Circle) { arcs += 1; }
                    else { other += 1; }
                }
            }
            out = append(out, "body | " ~ name ~ " | " ~ kind ~ " | " ~ toString(bb.minCorner[0] / millimeter) ~ " | "
                ~ toString(bb.minCorner[1] / millimeter) ~ " | " ~ toString(bb.minCorner[2] / millimeter)
                ~ " | " ~ lines ~ " | " ~ arcs ~ " | " ~ other);
        }
    }
    return out;
}'''


def main():
    f = c.get(f"{BASE}/features")
    states = f.get("featureStates", {})
    feats = {}
    for x in f["features"]:
        if x["name"].startswith(PREFIX) and x["featureType"] == "exportTag":
            feats[x["name"].split(" ")[0]] = x
    if not feats:
        print("no ZT cases in the tree: run build_export_tag_tests.py first")
        return 1

    ids = ", ".join('"%s"' % x["featureId"] for x in feats.values())
    r = c.post(f"{BASE}/featurescript", json_data={"script": SCRIPT % (ids, PREFIX)},
               query_params={"rollbackBarIndex": len(f["features"])})
    for n in r.get("notices") or []:
        print("eval notice:", n.get("level"), n.get("message"))
    lines = []

    def walk(o):
        if isinstance(o, dict):
            if isinstance(o.get("value"), str) and " | " in o["value"]:
                lines.append(o["value"])
            for v in o.values():
                walk(v)
        elif isinstance(o, list):
            for v in o:
                walk(v)
    walk(r.get("result"))

    msgs, bodies = {}, {}
    for ln in lines:
        p = [t.strip() for t in ln.split(" | ")]
        if p[0] == "msg":
            msgs[p[1]] = {"error": p[2], "warning": p[3], "info": p[4], "faces": int(p[5].split()[1])}
        elif p[0] == "body":
            bodies[p[1]] = {"kind": p[2], "min": tuple(float(v) for v in p[3:6]), "lines": int(p[6]), "arcs": int(p[7]), "other": int(p[8])}
    if "--dump" in sys.argv:
        for n in sorted(bodies):
            print("  ", n, bodies[n])

    fails = 0
    for case in CASES:
        feat = feats.get(case["key"])
        if feat is None:
            print("%-5s SKIP (not in the tree)  %s" % (case["key"], case["title"]))
            continue
        ex = case["expect"]
        problems = []
        status = states.get(feat["featureId"], {}).get("featureStatus")
        if status != ex["status"]:
            problems.append("status %s, expected %s" % (status, ex["status"]))
        m = msgs.get(feat["featureId"], {})
        text = " ".join([m.get("error", ""), m.get("warning", ""), m.get("info", "")])
        if ex.get("message_has") and ex["message_has"] not in text:
            problems.append("message lacks '%s' (got: %s)" % (ex["message_has"], text.strip()[:160]))
        for n in ex.get("sheets", []):
            if bodies.get(n, {}).get("kind") != "SHEET":
                problems.append("sheet '%s' missing" % n)
        for n in ex.get("composites", []):
            if bodies.get(n, {}).get("kind") != "COMPOSITE":
                problems.append("composite '%s' missing" % n)
        for n in ex.get("absent", []):
            if n in bodies:
                problems.append("'%s' should not exist" % n)
        for n, want in ex.get("min", {}).items():
            got = bodies.get(n, {}).get("min")
            if got is None or any(abs(a - b) > TOL for a, b in zip(got, want)):
                problems.append("'%s' lowest corner %s, expected %s" % (n, got, want))
        for n, k in ex.get("arcs_min", {}).items():
            if bodies.get(n, {}).get("arcs", 0) < k:
                problems.append("'%s' has %s arcs, expected >= %d" % (n, bodies.get(n, {}).get("arcs"), k))
        for n, k in ex.get("edges_max", {}).items():
            d = bodies.get(n)
            total = None if d is None else d["lines"] + d["arcs"] + d["other"]
            if total is None or total > k:
                problems.append("'%s' has %s edges, expected <= %d" % (n, total, k))
        if ex.get("pitch_y"):
            ys = [bodies.get(n, {}).get("min", (0, None))[1] for n in ex["pitch_y"]]
            if None in ys or abs((ys[1] - ys[0]) - (ys[2] - ys[1])) > TOL:
                problems.append("uneven pattern steps in y: %s" % ys)
        if ex.get("faces_equal_sheets"):
            sheets = [n for n in ex.get("sheets", [])]
            if m.get("faces") != len(sheets):
                problems.append("created %s faces for %d sheets (a construction plane left behind?)" % (m.get("faces"), len(sheets)))
        if problems:
            fails += 1
            print("%-5s FAIL  %s" % (case["key"], case["title"]))
            for p in problems:
                print("        -", p)
        else:
            print("%-5s PASS  %s" % (case["key"], case["title"]))
    print("%d case(s) failed" % fails)
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
