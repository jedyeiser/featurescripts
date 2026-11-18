"""Check the "Trim curve + tests" Part Studio built by build_trim_curve_plus_tests.py.

Reads every non-sketch wire body (edge count, length, case by x offset), the feature statuses, and the
Variable_tools keys embedded by T1 and T2 (cut_k vertices / piece_k), and compares with the expected results.

usage (repo root): PYTHONPATH=. MSYS_NO_PATHCONV=1 python devtools/onshape/check_trim_curve_plus_tests.py
"""
import json
import re

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("smallTools/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
E = [e["id"] for e in c.list_elements(D, W) if e["name"] == "Trim curve + tests"][0]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"

# case -> sorted wire lengths (mm), or "ERROR"
EXPECT = {
    "T1": [100, 100, 357.080], "T2": [557.080], "T3": [457.080], "T4": [100, 150, 307.080],
    "T5": [50, 507.080], "T6": [50, 507.080], "T7": [100, 457.080], "T8": [557.080],
    "T9": [20, 130, 100, 307.080], "T10": [407.080], "T11": [150], "T12": [50], "T13": [507.080], "T14": [150],
    # 2026-11-12 endpoint reference: from an END of the wire the distance runs INTO the wire, the flip is ignored
    "T15": [100], "T16": [457.080], "T17": [100], "T18": [457.080], "T19": [100],
    # 2026-11-12 in-place trims the old gates refused (ends equidistant; the cut plane crossing the wire twice)
    "T20": [150], "T21": [400], "T22": [107.080], "T23": [100],
    # 2026-11-18 distance from the ENDS of the wire
    "T24": [457.080], "T25": [120, 120, 317.080], "T26": [457.080], "T27": [457.080], "T28": [557.080], "T29": [557.080], "T30": [387.080], "T31": [447.080], "T32": [447.080], "T33": [637.080], "T34": [547.080], "T35": [557.080], "T36": [15, 15, 15, 15, 527.080, 527.080], "T37": [15, 15, 1450],
}
# Expected feature status. Since 2026-11-12 a single-cut TRIM reports its route: INFO = trimmed in place (the wire body is
# kept), WARNING = the wire was replaced (extract-and-delete fallback). T10, T11 and T14 took that fallback until the in-place
# trim was changed (2026-11-12) to try each cutter on a scratch copy and observe the result; no test case needs it now.
# Anything not listed must be OK.
WANT_STATUS = {"T8": "ERROR", "T3": "INFO", "T12": "INFO", "T13": "INFO", "T10": "INFO", "T11": "INFO", "T14": "INFO",
               "T15": "INFO", "T16": "INFO", "T17": "INFO", "T18": "INFO", "T19": "INFO",
               "T20": "INFO", "T21": "INFO", "T22": "INFO", "T23": "INFO",
               "T24": "INFO", "T26": "INFO", "T27": "INFO", "T28": "ERROR", "T29": "ERROR", "T30": "INFO", "T31": "INFO", "T32": "INFO", "T33": "INFO", "T34": "INFO", "T35": "ERROR"}
# Where the single trimmed wire starts, as an x offset from its case origin (T26: 30 from the start end, T27: 70 from the start end).
# T30 (hook wire, 60 from BOTH ends): the ends are 50 mm apart, so trimming the start end first made the second cut ambiguous and it ERRORed
# until 2026-11-18. The order that works is now chosen on a scratch copy first. T31/T32 are the one-end cases.
EXPECT_MIN_X = {"T24": 50, "T26": 30, "T27": 70, "T33": -40, "T34": 50}
EXPECT_EDGES = {"T2": 5}

SCRIPT = """function(context is Context, queries)
{
    var out = [];
    for (var w in evaluateQuery(context, qSketchFilter(qBodyType(qEverything(EntityType.BODY), BodyType.WIRE), SketchObject.NO)))
    {
        var bb = evBox3d(context, { "topology" : w });
        out = append(out, "W " ~ (bb.minCorner[0] / millimeter) ~ " " ~ size(evaluateQuery(context, qOwnedByBody(w, EntityType.EDGE)))
            ~ " " ~ (evLength(context, { "entities" : qOwnedByBody(w, EntityType.EDGE) }) / millimeter));
    }
    for (var fid in FIDS)
    {
        var m = getVariable(context, toString(makeId(fid)));
        for (var k in keys(m.query))
        {
            var ents = evaluateQuery(context, m.query[k].value);
            var desc = "";
            for (var e in ents)
            {
                if (size(evaluateQuery(context, qEntityFilter(e, EntityType.VERTEX))) > 0)
                {
                    var p = evVertexPoint(context, { "vertex" : e }) / millimeter;
                    desc = desc ~ " (" ~ roundToPrecision(p[0], 3) ~ "," ~ roundToPrecision(p[1], 3) ~ ")";
                }
            }
            out = append(out, "K " ~ fid ~ " " ~ k ~ " " ~ size(ents) ~ desc);
        }
        out = append(out, "K " ~ fid ~ " cutCount " ~ m.variable.cutCount.value);
    }
    return out;
}"""

feats = c.get(f"{BASE}/features")
status = {x["featureId"]: feats["featureStates"][x["featureId"]]["featureStatus"] for x in feats["features"]}
trims = {x["name"].split()[0]: x["featureId"] for x in feats["features"] if x["featureType"] == "betterCurveTrim"}
fids = [trims["T1"], trims["T2"]]
script = SCRIPT.replace("FIDS", json.dumps(fids))
r = c.post(f"{BASE}/featurescript", json_data={"script": script})
for n in r.get("notices") or []:
    print("[%s] %s" % (n.get("level"), n.get("message")))
lines = [v for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))) if v[:2] in ("W ", "K ")]

got, edges, minx = {}, {}, {}
for ln in lines:
    if ln.startswith("W "):
        _, x, ne, length = ln.split()
        case = "T%d" % (int(float(x) + 50) // 1000 + 1)
        got.setdefault(case, []).append(float(length))
        minx[case] = float(x) - (int(case[1:]) - 1) * 1000
        edges[case] = edges.get(case, 0) + int(ne)

fails = 0
for case, want in EXPECT.items():
    have = sorted(got.get(case, []))
    ok = len(have) == len(want) and all(abs(a - b) < 0.01 for a, b in zip(have, sorted(want)))
    ok = ok and (case not in EXPECT_EDGES or edges[case] == EXPECT_EDGES[case])
    ok = ok and (case not in EXPECT_MIN_X or abs(minx.get(case, -1) - EXPECT_MIN_X[case]) < 0.01)
    want_status = WANT_STATUS.get(case, "OK")
    ok = ok and status[trims[case]] == want_status
    fails += not ok
    print("%-4s %s  status %-5s wires %s edges %d" % (case, "PASS" if ok else "FAIL", status[trims[case]],
                                                      [round(x, 3) for x in have], edges.get(case, 0)))
print()
for ln in lines:
    if ln.startswith("K "):
        print(ln)
print("\n%d/%d pass" % (len(EXPECT) - fails, len(EXPECT)))



