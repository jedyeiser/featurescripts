"""Check -- and baseline -- the Offset edges test cases in example_1's "Offset edges tests" Part Studio
(built by build_offset_edges_tests.py).

For every case the eval API measures the output wire and returns one metrics record:
  status, wires, edges, per-edge curve type / length / radius (edges sorted by the station of their midpoint),
  station extent of the output, max deviation from the analytic offset (distance from the source vs the profile
  at that station; blend zones skipped), end-tangent angles against the source tangent, joint angles, joint gaps
  and relative curvature jumps between consecutive edges, and for the helix cases the roll of the offset
  direction against the source's curvature normal at 10/30/50/70/90 % of the output.
The case is PASS / FAIL against the expectations in CASES (from utility.md section 4 and round2_lead_offset.md).

Baseline for the shared-library refactor (round2_lead_offset.md: identical topology, <= 1 um planar, <= 5 um 3D):
  PYTHONPATH=. python devtools/onshape/check_offset_edges_tests.py --save <baseline.json>      (before the change)
  PYTHONPATH=. python devtools/onshape/check_offset_edges_tests.py --baseline <baseline.json>  (after it)
The comparison reports every metric that moved: strings must match exactly, numbers (mm / deg, rounded to
1e-4) within --tol (default 0.001 mm; use 0.005 for the helix cases' 3D limit). Nothing is written unless --save.

usage (repo root): PYTHONPATH=. python devtools/onshape/check_offset_edges_tests.py [--save FILE | --baseline FILE] [--tol MM]
"""
import argparse
import json
import math
import re
import sys

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("example_1/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
STUDIO = "Offset edges tests"

# Station modes: ("x", x0) -> s = X - x0; ("arc", cx, cy, r, a0) -> s = r * CCW angle from a0; ("param",) -> arc length
# along the single source edge (with rev=True the profile may run either way; the smaller deviation is kept).
# prof: FeatureScript expression of s (mm) giving the distance (mm) from the source, or None (no analytic check).
# Helpers available in prof: smooth(u), pwSmooth(s, xs, vs), srcLen.
Q100 = 100 * math.pi / 2
CASES = [
    dict(tag="OE1", src="OE1 source", mode=("x", 0), prof="10", wires=1, edges=1, extent=(0, 200), dev=0.01),
    dict(tag="OE2", src="OE2 source", mode=("arc", 1000, 100, 100, -math.pi / 2), prof="10", wires=1, edges=1,
         types=["CIRCLE"], radius=(90, 110), extent=(0, Q100), dev=0.001),
    dict(tag="OE3", src="OE3 source", mode=("arc", 2000, 100, 100, -math.pi / 2), prof="10", wires=1, edges=1,
         types=["BSPLINE"], extent=(0, Q100), dev=0.01),
    dict(tag="OE4", src="OE4 source", mode=("arc", 3150, 20000, 20000, -math.pi / 2 - 0.0075), prof="10", wires=1, edges=3,
         extent=(0, 300), dev=0.01, joints=0.01),
    dict(tag="OE5", src="OE5 source", mode=("arc", 4000, 100, 100, -math.pi / 2), prof="10 * s / srcLen", wires=1, edges=2,
         types=["CIRCLE", "CIRCLE"], extent=(0, Q100), dev=0.05, joints=0.01),
    dict(tag="OE6", src="OE6 source", mode=("x", 5000), prof="s < 200 ? 0 : 20", skip=[(140, 260)], wires=1, edges=3,
         extent=(0, 400), dev=0.01, joints=0.01),
    dict(tag="OE7", src="OE7 source", mode=("arc", 6000, 300, 300, -math.pi / 2), prof="s < 225 ? 0 : 20", skip=[(140, 310)],
         wires=1, edges=3, extent=(0, 300 * math.pi / 2), dev=0.01, joints=0.01, kjump=0.01),
    dict(tag="OE8", src="OE8 source", mode=("x", 7000), prof="s <= 20 ? 0 : 20 * (s - 20) / 180", wires=1, edges=1, extent=(0, 200), dev=0.01),
    dict(tag="OE9a", src="OE9a source", mode=("x", 8000), prof="20 * (s / 200) * (s / 200)", wires=1, edges=1, extent=(0, 200), dev=0.01),
    dict(tag="OE9b", src="OE9b source", mode=("x", 9000), prof="20 * smooth(s / 200)", wires=1, edges=1, extent=(0, 200), dev=0.01),
    dict(tag="OE10", src="OE10 source", mode=("x", 10000), prof="pwSmooth(s, [0, 50, 150, 250, 300], [0, 0, 20, 5, 5])",
         wires=1, edges=1, extent=(0, 300), dev=0.01),
    dict(tag="OE11a", src="OE11a source", mode=("x", 11000), prof="(s - 150) / 10", wires=1, edges=1, extent=(150, 250), dev=0.01),
    dict(tag="OE11b", src="OE11b source", mode=("x", 12000), prof="(150 - s) / 10", wires=1, edges=1, extent=(50, 150), dev=0.01),
    dict(tag="OE12", src="OE12 helix R100", helix=True, mode=("param",), prof="10", wires=1, edges=1, dev=0.01),
    dict(tag="OE18", src="OE18 helix R100", helix=True, mode=("param",), prof="10 * s / srcLen", rev=True, wires=1, dev=0.01),
    dict(tag="OE13", src="OE13 source", mode=("x", 15000), prof=None),
    dict(tag="OE14", src="OE14 source", mode=("x", 16000), prof=None, wires=0),
    dict(tag="OE15", src="OE15 source", mode=("x", 17000), prof=None),
    dict(tag="OE17", src="OE17 source", mode=("x", 18000), prof=None),
    dict(tag="OE19", src="OE19 source", mode=("x", 19000), prof="10", wires=1, edges=1, dev=0.01),
    dict(tag="OE20", src="OE20 source", mode=("x", 20000), prof="sqrt(pwSmooth(s, [0, 100, 200, 300], [0, 0, 20, 20]) ^ 2 + 100)",
         wires=1, edges=1, extent=(0, 300), dev=0.01),
    dict(tag="OE21", src="OE21 source", mode=("arc", 21000, 100, 100, -math.pi / 2), prof="10", wires=1, edges=1,
         extent=(100 * math.pi / 6, 100 * math.pi / 3), dev=0.01),
]

# Expected statuses (today's code; see the notes). Every other case and every fixture must be OK.
EXPECTED_STATUS = {
    "OE5": "INFO",      # "N varying-offset source arc(s) built as tangent arc pairs" (offsetEdges.fs:2500)
    "OE13": "WARNING",  # validateNoOverlap (:1131); whether opExtractWires accepts the overlapping pieces is unverified
    "OE14": "WARNING",  # "No regions defined" (:482)
    "OE15": "WARNING",  # blend clamp (:2304). utility.md P0 item 6 proposes INFO -- update here when that lands
    "OE17": "WARNING",  # "fully consumed by adjacent blend zones" (:2363)
}

METRICS_FS = r'''
function(context is Context, queries)
{
    const SELF = "@SELF@";
    const src = @SRC@;
    const MODE = "@MODE@";
    const X0 = @X0@;
    const CX = @CX@;
    const CY = @CY@;
    const RAD = @RAD@;
    const A0 = @A0@;
    const HAS_PROFILE = @HAS@;
    const REV = @REV@;
    const SKIP = @SKIP@;
    const srcLen = evLength(context, { "entities" : src }) / millimeter;
    const smooth = function(u)
        {
            const t = min(max(u, 0), 1);
            return t * t * t * (10 + t * (6 * t - 15));
        };
    const pwSmooth = function(s, xs, vs)
        {
            if (s <= xs[0])
            {
                return vs[0];
            }
            for (var i = 0; i < size(xs) - 1; i += 1)
            {
                if (s <= xs[i + 1])
                {
                    return vs[i] + (vs[i + 1] - vs[i]) * smooth((s - xs[i]) / (xs[i + 1] - xs[i]));
                }
            }
            return vs[size(vs) - 1];
        };
    const prof = function(s) { return @PROF@; };
    const r4 = function(v) { return toString(roundToPrecision(v, 4)); };
    const station = function(p)
        {
            if (MODE == "x")
            {
                return p[0] / millimeter - X0;
            }
            if (MODE == "arc")
            {
                var d = atan2(p[1] / millimeter - CY, p[0] / millimeter - CX) / radian - A0;
                while (d < -0.5)
                {
                    d += 2 * PI;
                }
                while (d >= 2 * PI - 0.5)
                {
                    d -= 2 * PI;
                }
                return RAD * d;
            }
            return evDistance(context, { "side0" : p, "side1" : src }).sides[1].parameter * srcLen;
        };
    const skipped = function(s)
        {
            for (var r in SKIP)
            {
                if (s > r[0] && s < r[1])
                {
                    return true;
                }
            }
            return false;
        };
    const lineAngle = function(a, b) { return acos(min(1, abs(dot(a, b)))) / degree; };
    const srcTangent = function(p)
        {
            const dr = evDistance(context, { "side0" : p, "side1" : src });
            return evEdgeTangentLine(context, { "edge" : qNthElement(src, dr.sides[1].index), "parameter" : dr.sides[1].parameter }).direction;
        };
    const curvatureAt = function(e, t) { return evEdgeCurvature(context, { "edge" : e, "parameter" : t }).curvature * millimeter; };

    const out = qBodyType(qCreatedBy(makeId(SELF), EntityType.BODY), BodyType.WIRE);
    const es = evaluateQuery(context, qOwnedByBody(out, EntityType.EDGE));
    var params = [];
    for (var i = 0; i <= 40; i += 1)
    {
        params = append(params, i / 40);
    }
    var recs = [];
    var maxDev = -1;
    var maxDevAt = 0;
    var maxDevRev = -1;
    var sMin = 1e9;
    var sMax = -1e9;
    for (var e in es)
    {
        const lines = evEdgeTangentLines(context, { "edge" : e, "parameters" : params, "arcLengthParameterization" : true });
        var st = [];
        for (var tl in lines)
        {
            const s = station(tl.origin);
            st = append(st, s);
            sMin = min(sMin, s);
            sMax = max(sMax, s);
            if (HAS_PROFILE && !skipped(s))
            {
                const d = evDistance(context, { "side0" : tl.origin, "side1" : src }).distance / millimeter;
                const dev = abs(d - prof(s));
                if (dev > maxDev)
                {
                    maxDev = dev;
                    maxDevAt = s;
                }
                if (REV)
                {
                    maxDevRev = max(maxDevRev, abs(d - prof(srcLen - s)));
                }
            }
        }
        const def = evCurveDefinition(context, { "edge" : e });
        recs = append(recs, { "mid" : st[20], "kind" : toString(def.curveType), "len" : evLength(context, { "entities" : e }) / millimeter,
                    "radius" : def.curveType == CurveType.CIRCLE ? r4(def.radius / millimeter) : "-", "edge" : e,
                    "p" : [lines[0].origin, lines[40].origin], "t" : [lines[0].direction, lines[40].direction], "s" : [st[0], st[40]] });
    }
    recs = sort(recs, function(a, b) { return a.mid - b.mid; });

    var kinds = "";
    var lens = "";
    var radii = "";
    for (var i = 0; i < size(recs); i += 1)
    {
        const sep = i == 0 ? "" : ",";
        kinds = kinds ~ sep ~ recs[i].kind;
        lens = lens ~ sep ~ r4(recs[i].len);
        radii = radii ~ sep ~ recs[i].radius;
    }

    // joints between consecutive edges: closest pair of ends
    var joints = "";
    var gaps = "";
    var kjumps = "";
    for (var i = 0; i < size(recs) - 1; i += 1)
    {
        const a = recs[i];
        const b = recs[i + 1];
        var best = [0, 0];
        var bestD = 1e9 * meter;
        for (var ia = 0; ia < 2; ia += 1)
        {
            for (var ib = 0; ib < 2; ib += 1)
            {
                if (norm(a.p[ia] - b.p[ib]) < bestD)
                {
                    bestD = norm(a.p[ia] - b.p[ib]);
                    best = [ia, ib];
                }
            }
        }
        const ka = curvatureAt(a.edge, best[0]);
        const kb = curvatureAt(b.edge, best[1]);
        const sep = i == 0 ? "" : ",";
        joints = joints ~ sep ~ r4(lineAngle(a.t[best[0]], b.t[best[1]]));
        gaps = gaps ~ sep ~ r4(bestD / millimeter);
        kjumps = kjumps ~ sep ~ r4(abs(ka - kb) / max([abs(ka), abs(kb), 1e-9]));
    }

    // end tangents against the source tangent at the foot point
    var endTan = "-";
    if (size(recs) > 0)
    {
        const first = recs[0];
        const last = recs[size(recs) - 1];
        const i0 = first.s[0] <= first.s[1] ? 0 : 1;
        const i1 = last.s[1] >= last.s[0] ? 1 : 0;
        endTan = r4(lineAngle(first.t[i0], srcTangent(first.p[i0]))) ~ "," ~ r4(lineAngle(last.t[i1], srcTangent(last.p[i1])));
    }

    // helix: roll of the offset direction against the source curvature normal
    var roll = "-";
    if (MODE == "param" && size(recs) > 0)
    {
        roll = "";
        for (var f in [0.1, 0.3, 0.5, 0.7, 0.9])
        {
            const p = evEdgeTangentLine(context, { "edge" : recs[0].edge, "parameter" : f, "arcLengthParameterization" : true }).origin;
            const dr = evDistance(context, { "side0" : p, "side1" : src });
            const cv = evEdgeCurvature(context, { "edge" : qNthElement(src, dr.sides[1].index), "parameter" : dr.sides[1].parameter });
            const v = p - dr.sides[1].point;
            roll = roll ~ (f == 0.1 ? "" : ",") ~ r4(atan2(dot(v, yAxis(cv.frame)) / millimeter, dot(v, cv.frame.xAxis) / millimeter) / degree);
        }
    }

    return "wires=" ~ size(evaluateQuery(context, out)) ~ "|edges=" ~ size(recs) ~ "|types=" ~ kinds ~ "|lengths=" ~ lens
        ~ "|radii=" ~ radii ~ "|smin=" ~ (size(recs) > 0 ? r4(sMin) : "-") ~ "|smax=" ~ (size(recs) > 0 ? r4(sMax) : "-")
        ~ "|maxdev=" ~ (maxDev >= 0 ? r4(maxDev) : "-") ~ "|maxdev_at=" ~ (maxDev >= 0 ? r4(maxDevAt) : "-")
        ~ "|maxdev_reversed=" ~ (maxDevRev >= 0 ? r4(maxDevRev) : "-") ~ "|end_tangents_deg=" ~ endTan
        ~ "|joints_deg=" ~ joints ~ "|joint_gaps=" ~ gaps ~ "|curvature_jumps=" ~ kjumps ~ "|roll_deg=" ~ roll;
}
'''

OE22_FS = r'''
function(context is Context, queries)
{
    const comp = qOwnedByBody(qCreatedBy(makeId("@SELF@"), EntityType.BODY), EntityType.EDGE);
    var blendLen = -1 * millimeter;
    for (var e in evaluateQuery(context, qOwnedByBody(qBodyType(qCreatedBy(makeId("@OE6@"), EntityType.BODY), BodyType.WIRE), EntityType.EDGE)))
    {
        const mid = evEdgeTangentLine(context, { "edge" : e, "parameter" : 0.5 }).origin;
        if (abs(mid[0] - 5200 * millimeter) < 30 * millimeter)
        {
            blendLen = evLength(context, { "entities" : e });
        }
    }
    const n = size(evaluateQuery(context, comp));
    const len = n > 0 ? evLength(context, { "entities" : comp }) : 0 * millimeter;
    return "edges=" ~ n ~ "|length=" ~ toString(roundToPrecision(len / millimeter, 4)) ~ "|oe6_blend_length=" ~ toString(roundToPrecision(blendLen / millimeter, 4));
}
'''


def strings(result):
    return [s for s in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(result)) if not s.startswith("BTFSValue")]


def edges_expr(fid, helix):
    if helix:
        return 'qCreatedBy(makeId("%s"), EntityType.EDGE)' % fid
    return 'qConstructionFilter(qOwnedByBody(qBodyType(qCreatedBy(makeId("%s"), EntityType.BODY), BodyType.WIRE), EntityType.EDGE), ConstructionObject.NO)' % fid


def metrics_script(cs, self_id, src_id):
    mode = cs["mode"]
    consts = {"X0": 0, "CX": 0, "CY": 0, "RAD": 0, "A0": 0}
    if mode[0] == "x":
        consts["X0"] = mode[1]
    elif mode[0] == "arc":
        consts.update(CX=mode[1], CY=mode[2], RAD=mode[3], A0=mode[4])
    src = METRICS_FS
    for k, v in consts.items():
        src = src.replace("@%s@" % k, repr(float(v)))
    return (src.replace("@SELF@", self_id).replace("@SRC@", edges_expr(src_id, cs.get("helix")))
            .replace("@MODE@", mode[0]).replace("@HAS@", "true" if cs["prof"] else "false")
            .replace("@REV@", "true" if cs.get("rev") else "false")
            .replace("@SKIP@", "[" + ", ".join("[%r, %r]" % (float(a), float(b)) for a, b in cs.get("skip", [])) + "]")
            .replace("@PROF@", cs["prof"] or "0"))


def parse(text):
    return dict(kv.split("=", 1) for kv in text.split("|"))


def number(v):
    try:
        return float(v)
    except ValueError:
        return None


def judge(cs, m):
    """PASS / FAIL messages of one case's metrics against its expectations."""
    bad = []
    if "wires" in cs and int(m["wires"]) != cs["wires"]:
        bad.append("wires %s, expected %d" % (m["wires"], cs["wires"]))
    if "edges" in cs and int(m["edges"]) != cs["edges"]:
        bad.append("edges %s, expected %d" % (m["edges"], cs["edges"]))
    if "types" in cs and m["types"] != ",".join(cs["types"]):
        bad.append("types %s, expected %s" % (m["types"], ",".join(cs["types"])))
    if "radius" in cs:
        radii = [number(r) for r in m["radii"].split(",") if r]
        if not radii or any(r is None or min(abs(r - x) for x in cs["radius"]) > 0.001 for r in radii):
            bad.append("radii %s, expected one of %s" % (m["radii"], cs["radius"]))
    if "extent" in cs:
        lo, hi = number(m["smin"]), number(m["smax"])
        if lo is None or abs(lo - cs["extent"][0]) > 0.01 or abs(hi - cs["extent"][1]) > 0.01:
            bad.append("extent s %s..%s, expected %.4f..%.4f" % (m["smin"], m["smax"], cs["extent"][0], cs["extent"][1]))
    if "dev" in cs:
        dev = number(m["maxdev"])
        if dev is not None and cs.get("rev") and number(m["maxdev_reversed"]) is not None:
            dev = min(dev, number(m["maxdev_reversed"]))
        if dev is None or dev > cs["dev"]:
            bad.append("max deviation %s (at s %s), limit %g" % (m["maxdev"], m["maxdev_at"], cs["dev"]))
    for key, metric, label in [("joints", "joints_deg", "joint angle"), ("kjump", "curvature_jumps", "curvature jump")]:
        if key in cs:
            vals = [number(v) for v in m[metric].split(",") if v]
            if any(v is None or v > cs[key] for v in vals):
                bad.append("%s %s, limit %g" % (label, m[metric], cs[key]))
    return bad


def compare(tag, old, new, tol):
    moved = []
    for k in sorted(set(old) | set(new)):
        a, b = old.get(k), new.get(k)
        if a == b:
            continue
        pa, pb = (a or "").split(","), (b or "").split(",")
        if len(pa) == len(pb) and all(number(x) is not None and number(y) is not None for x, y in zip(pa, pb)) \
                and all(abs(number(x) - number(y)) <= tol for x, y in zip(pa, pb)):
            continue
        moved.append("%s: %s -> %s" % (k, a, b))
    return moved


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--save", help="write the metrics of every case to this JSON file")
    ap.add_argument("--baseline", help="compare the metrics against this JSON file")
    ap.add_argument("--tol", type=float, default=0.001, help="numeric tolerance for --baseline (mm / deg)")
    args = ap.parse_args()

    found = [e["id"] for e in c.list_elements(D, W) if e["name"] == STUDIO and e["elementType"] == "PARTSTUDIO"]
    if not found:
        raise SystemExit("studio %r not found (run build_offset_edges_tests.py)" % STUDIO)
    base = f"/api/v10/partstudios/d/{D}/w/{W}/e/{found[0]}"
    feats = c.get(f"{base}/features")
    states = feats["featureStates"]
    by_name = [(x["name"], x["featureId"]) for x in feats["features"]]
    failed = 0
    record = {}

    def fid(prefix):
        hits = [i for n, i in by_name if n.startswith(prefix) and "->" not in n]
        if len(hits) != 1:
            raise SystemExit("fixture prefix %r matches %d features" % (prefix, len(hits)))
        return hits[0]

    def case_feature(tag):
        hits = [(n, i) for n, i in by_name if n.startswith(tag + " ") and "->" in n]
        return hits[0] if len(hits) == 1 else (None, None)

    def evaluate(script):
        r = c.post(f"{base}/featurescript", json_data={"script": script})
        errors = [x.get("message") for x in (r.get("notices") or []) if x.get("level") == "ERROR"]
        got = strings(r.get("result"))
        return (None, "; ".join(str(e) for e in errors) or "no result") if errors or not got else (got[0], None)

    # fixtures must be OK
    for n, i in by_name:
        status = states.get(i, {}).get("featureStatus")
        if "->" not in n and status != "OK":
            failed += 1
            print("FAIL", n, "-- fixture status", status)

    for cs in CASES:
        tag = cs["tag"]
        name, self_id = case_feature(tag)
        if not self_id:
            print("FAIL", tag, "-- case feature not found")
            failed += 1
            continue
        status = states.get(self_id, {}).get("featureStatus")
        text, err = evaluate(metrics_script(cs, self_id, fid(cs["src"])))
        if err:
            print("FAIL", name, "-- check did not run:", err)
            failed += 1
            continue
        m = parse(text)
        m["status"] = status
        record[tag] = m
        bad = judge(cs, m)
        want = EXPECTED_STATUS.get(tag, "OK")
        if status != want:
            bad.insert(0, "status %s, expected %s" % (status, want))
        failed += bool(bad)
        print("PASS" if not bad else "FAIL", name, "--", "; ".join(bad) if bad else "")
        print("      " + " ".join("%s=%s" % kv for kv in m.items() if kv[1] not in ("", "-")))

    # OE22: a native composite curve picking OE6's blend edge by transient id
    name, self_id = case_feature("OE22")
    oe6_name, oe6_id = case_feature("OE6")
    if not self_id or not oe6_id:
        print("FAIL OE22 -- case feature or OE6 not found")
        failed += 1
    else:
        text, err = evaluate(OE22_FS.replace("@SELF@", self_id).replace("@OE6@", oe6_id))
        if err:
            print("FAIL", name, "-- check did not run:", err)
            failed += 1
        else:
            m = parse(text)
            m["status"] = states.get(self_id, {}).get("featureStatus")
            record["OE22"] = m
            ok = m["status"] == "OK" and m["edges"] == "1" and number(m["oe6_blend_length"]) > 0 \
                and abs(number(m["length"]) - number(m["oe6_blend_length"])) < 1e-3
            failed += not ok
            print("PASS" if ok else "FAIL", name, "--", " ".join("%s=%s" % kv for kv in m.items()))

    if args.save:
        json.dump(record, open(args.save, "w"), indent=1, sort_keys=True)
        print("baseline written to", args.save)
    if args.baseline:
        old = json.load(open(args.baseline))
        moved_total = 0
        for tag in sorted(set(old) | set(record)):
            moved = compare(tag, old.get(tag, {}), record.get(tag, {}), args.tol)
            if moved:
                moved_total += 1
                print("MOVED", tag, "--", "; ".join(moved))
        print("baseline: %s" % ("identical within %g" % args.tol if not moved_total else "%d case(s) moved" % moved_total))
        failed += moved_total
    print("%d failed" % failed if failed else "all passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
