"""Check the footprint-suite test cases in the footprint document's "Footprint tests" Part Studio (built by
build_footprint_tests.py, which also holds the case table, the fixture geometry and the expected values). Every
case is measured through the eval API and printed PASS / FAIL; cases listed in build_footprint_tests.EXPECT_FAIL
document a known bug and print XFAIL (not counted) until they pass, then XPASS (counted: remove them from the list).
Every feature's status is checked too: fixtures must be OK, case features OK or INFO.

What each kind of case verifies (all numbers come from the analytic fixture geometry, never from a capture):

  AF  Analyze footprint writes its dialog values from editing logic, which the REST API never runs, so the
      checker reads what the feature BODY makes: the "Sketch key points" sketch. From its construction lines
      (waistLine, fb/abWidestLine, fb/abInflection) it checks the waist, both widest points and both inflection
      points (a missing inflection line = no inflection found), then computes from THOSE points the natural radius
      (circle through widest-waist-widest and inflection-waist-inflection) and the taper (widest to widest). The
      average radius is recomputed by the checker with the feature's definition (mean of 1/k at 200 stations at the
      midpoints of equal x intervals between the feature's inflection x, each station on whichever fixture edge
      spans its x, curvature from evEdgeCurvatures) -- so it verifies the interval the feature found and that the
      definition is independent of the edge split (AF2 vs AF3), not the feature's own arithmetic. If the dialog
      has been populated (open the feature, Recalculate), its "Average radius" / "Natural radius - inflection"
      strings are compared as well.
  GP  Generate Footprint Points with "Create Sketch From Points": the tip / RSL / tail point sketches -- point
      count, x range (region bounds: tip end, FCP, ACP, tail end), every point on the footprint (0.001 mm), equal
      arc-length spacing (chord max / min <= 1.002).
  AR  Arc fit (Curves output): one wire body, every output edge an analytic Circle or Line, output within the
      tolerance of the input AND the input within the tolerance of the output (a gap fails the second test and
      splits the wire); AR1 also checks the preserved radii.
  IF  Integrate footprint: one wire; edges sorted by x are exact arcs (Circle, radius +-0.01 mm, centre) or
      splines as expected; tangent continuity between consecutive edges; waist (lowest sampled point); IF3 widest
      points, IF5 taper from the end heights, IF7 every edge Circle / Line.
  SF  Scale Footprint: one +Y and one -Y wire; tip / tail ends translated; contact points on the output; waist
      and widest points (accordion keeps y); taper (keep taper); ACP width (pin ACP); -Y is the mirror of +Y;
      scale radius: the checker finds the output's inflections (sign of the curvature normal's y, sampled along
      x) and recomputes the 200-station average radius between them; SF5 the output is the exact x-scaled image of
      the reference spline (the knot-preservation symptom).

usage (repo root): PYTHONPATH=. python devtools/onshape/check_footprint_tests.py
(FS_SYNC_TIMEOUT defaults to 120 s here: the first regeneration of the studio is slow)
"""
import json
import os
import re
import sys

os.environ.setdefault("FS_SYNC_TIMEOUT", "120")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import build_footprint_tests as B  # noqa: E402  (geometry + case table only; its Onshape code is in main())
from sync.core.client import OnshapeClient  # noqa: E402

HELPERS = r'''
    const mm = millimeter;
    const pt = function(x, y) { return vector(x, y, 0) * millimeter; };
    const fmt = function(v) { return toString(roundToPrecision(v / millimeter, 3)); };
    const fpt = function(p) { return p == undefined ? "none" : "(" ~ fmt(p[0]) ~ ", " ~ fmt(p[1]) ~ ")"; };
    const fm = function(v) { return toString(roundToPrecision(v / meter, 4)) ~ " m"; };
    const fdeg = function(a) { return toString(roundToPrecision(a / degree, 4)) ~ " deg"; };
    const count = function(q) { return size(evaluateQuery(context, q)); };
    const bodies = function(fid) { return qCreatedBy(makeId(fid), EntityType.BODY); };
    const wireBodies = function(q) { return qBodyType(q, BodyType.WIRE); };
    const edgesOf = function(q) { return qOwnedByBody(q, EntityType.EDGE); };
    const near = function(p, x, y, tx, ty)
        {
            if (p == undefined)
            {
                return false;
            }
            return abs(p[0] - x * mm) <= tx * mm && abs(p[1] - y * mm) <= ty * mm;
        };
    // top end of an analysis-sketch line (x, 0) -> (x, y); undefined when the feature did not draw it
    const lineTop = function(fid, name)
        {
            const lq = sketchEntityQuery(makeId(fid) + "footprintAnalysisSketch", EntityType.EDGE, name);
            if (count(lq) != 1)
            {
                return undefined;
            }
            const ends = evEdgeTangentLines(context, { "edge" : lq, "parameters" : [0, 1] });
            return ends[0].origin[1] > ends[1].origin[1] ? ends[0].origin : ends[1].origin;
        };
    const circumR = function(a, b, c)
        {
            const cr = norm(cross(b - a, c - a));
            if (cr < 1e-12 * meter * meter)
            {
                return inf * meter;
            }
            return norm(b - a) * norm(c - b) * norm(a - c) / (2 * cr);
        };
    const params = function(n, ends)
        {
            var ps = [];
            for (var i = 0; i < n; i += 1)
            {
                ps = append(ps, ends ? i / (n - 1) : (i + 0.5) / n);
            }
            return ps;
        };
    // points along edges with the signed bend b (1/m; + = concave toward +Y, from the curvature normal), sorted by x
    const samples = function(edgesQ, n, ends)
        {
            var out = [];
            for (var e in evaluateQuery(context, edgesQ))
            {
                for (var r in evEdgeCurvatures(context, { "edge" : e, "parameters" : params(n, ends) }))
                {
                    const k = r.curvature * meter;
                    var bend = 0;
                    if (k >= 1e-7)
                    {
                        bend = curvatureFrameNormal(r)[1] > 0 ? k : -k;
                    }
                    out = append(out, { "p" : r.frame.origin, "b" : bend });
                }
            }
            return sort(out, function(a, b) { return a.p[0] - b.p[0]; });
        };
    const extreme = function(s, xlo, xhi, lowest)
        {
            var best = undefined;
            for (var sm in s)
            {
                if (sm.p[0] >= xlo && sm.p[0] <= xhi)
                {
                    if (best == undefined || (lowest ? sm.p[1] < best[1] : sm.p[1] > best[1]))
                    {
                        best = sm.p;
                    }
                }
            }
            return best;
        };
    // x of the first bend sign change walking from x = xw outward (dir +1 / -1); linear in b between samples
    const crossing = function(s, xw, dir)
        {
            var prev = undefined;
            const n = size(s);
            for (var j = 0; j < n; j += 1)
            {
                const sm = dir > 0 ? s[j] : s[n - 1 - j];
                if ((sm.p[0] - xw) * dir >= 0 * meter && sm.b != 0)
                {
                    if (prev != undefined && prev.b * sm.b < 0)
                    {
                        return prev.p[0] + (sm.p[0] - prev.p[0]) * prev.b / (prev.b - sm.b);
                    }
                    prev = sm;
                }
            }
            return undefined;
        };
    // fpt_analyze computeAverageRadius: mean 1/k at 200 stations at the midpoints of equal x intervals of
    // [xa, xb], each on the first edge that spans its x (parameter by linear interpolation of dense samples)
    const avgR = function(edgesQ, xa, xb)
        {
            const N = 200;
            const dx = (xb - xa) / N;
            var filled = makeArray(N, false);
            var total = 0 * meter;
            var n = 0;
            const ps = params(401, true);
            for (var e in evaluateQuery(context, edgesQ))
            {
                const tl = evEdgeTangentLines(context, { "edge" : e, "parameters" : ps });
                var sp = [];
                for (var i = 0; i < size(ps) - 1; i += 1)
                {
                    const x0 = tl[i].origin[0];
                    const x1 = tl[i + 1].origin[0];
                    if (abs(x1 - x0) > 1e-12 * meter)
                    {
                        const lo = max(0, ceil((min(x0, x1) - xa) / dx - 0.5));
                        const hi = min(N - 1, floor((max(x0, x1) - xa) / dx - 0.5));
                        for (var st = lo; st <= hi; st += 1)
                        {
                            if (!filled[st])
                            {
                                filled[st] = true;
                                const x = xa + (st + 0.5) * dx;
                                sp = append(sp, ps[i] + (ps[i + 1] - ps[i]) * (x - x0) / (x1 - x0));
                            }
                        }
                    }
                }
                if (size(sp) > 0)
                {
                    for (var r in evEdgeCurvatures(context, { "edge" : e, "parameters" : sp }))
                    {
                        if (r.curvature > 1e-9 / meter)
                        {
                            total += 1 / r.curvature;
                            n += 1;
                        }
                    }
                }
            }
            return { "n" : n, "avg" : n > 0 ? total / n : 0 * meter };
        };
    // largest distance from n + 1 points along each edge of fromEdges to the toEdges
    const maxDev = function(fromEdges, toEdges, n)
        {
            var worst = 0 * meter;
            for (var e in evaluateQuery(context, fromEdges))
            {
                for (var l in evEdgeTangentLines(context, { "edge" : e, "parameters" : params(n + 1, true) }))
                {
                    worst = max(worst, evDistance(context, { "side0" : l.origin, "side1" : toEdges }).distance);
                }
            }
            return worst;
        };
    const byX = function(q)
        {
            var arr = [];
            for (var e in evaluateQuery(context, q))
            {
                arr = append(arr, { "e" : e, "x" : evEdgeTangentLine(context, { "edge" : e, "parameter" : 0.5 }).origin[0] });
            }
            arr = sort(arr, function(a, b) { return a.x - b.x; });
            var out = [];
            for (var m in arr)
            {
                out = append(out, m.e);
            }
            return out;
        };
    // largest tangent-direction break at the touching ends of consecutive edges
    const g1 = function(es)
        {
            var worst = 0 * degree;
            for (var i = 0; i < size(es) - 1; i += 1)
            {
                const a = evEdgeTangentLines(context, { "edge" : es[i], "parameters" : [0, 1] });
                const c = evEdgeTangentLines(context, { "edge" : es[i + 1], "parameters" : [0, 1] });
                var dmin = inf * meter;
                var ang = 0 * degree;
                for (var la in a)
                {
                    for (var lc in c)
                    {
                        if (norm(la.origin - lc.origin) < dmin)
                        {
                            dmin = norm(la.origin - lc.origin);
                            ang = acos(min(1, abs(dot(la.direction, lc.direction))));
                        }
                    }
                }
                worst = max(worst, ang);
            }
            return worst;
        };
    const pointsOf = function(q)
        {
            var out = [];
            for (var v in evaluateQuery(context, q))
            {
                out = append(out, evVertexPoint(context, { "vertex" : v }));
            }
            return sort(out, function(a, b) { return a[0] - b[0]; });
        };
'''


def P(p):
    return "pt(%.6f, %.6f)" % (p[0], p[1])


def fs_list(items):
    return "[" + ", ".join(items) + "]"


# ---------------------------------------------------------------------------------------------------------------
# Per-kind check bodies. `fid(key)` resolves a fixture feature key (the part of its name before ':') to its id.
# ---------------------------------------------------------------------------------------------------------------
def body_analyze(case, fid):
    e = case["expect"]
    fx = case["fixture"]
    src = B.wire_edges(fid(B.fx_names(fx)["wire"])) if case["source"] == "wire" else B.edges(fid(B.fx_names(fx)["sketch"]))
    v = {"wx": e["waist"][0], "wy": e["waist"][1], "fwx": e["fbWidest"][0], "fwy": e["fbWidest"][1],
         "awx": e["abWidest"][0], "awy": e["abWidest"][1], "fix": e["fbInflection"][0], "fiy": e["fbInflection"][1],
         "aix": e["abInflection"][0], "aiy": e["abInflection"][1], "nw": e["naturalWidest"], "ni": e["naturalInflection"],
         "av": e["averageRadius"], "tp": e["taper"], "src": src}
    return r'''
        const w = lineTop(SELF, "waistLine");
        const fw = lineTop(SELF, "fbWidestLine");
        const aw = lineTop(SELF, "abWidestLine");
        const fi = lineTop(SELF, "fbInflection");
        const ai = lineTop(SELF, "abInflection");
        var ok = near(w, %(wx).6f, %(wy).6f, 0.5, 0.005) && near(fw, %(fwx).6f, %(fwy).6f, 0.5, 0.005)
            && near(aw, %(awx).6f, %(awy).6f, 0.5, 0.005) && near(fi, %(fix).6f, %(fiy).6f, 0.01, 0.01)
            && near(ai, %(aix).6f, %(aiy).6f, 0.01, 0.01);
        var msg = "waist " ~ fpt(w) ~ ", widest FB " ~ fpt(fw) ~ " AB " ~ fpt(aw) ~ ", inflection FB " ~ fpt(fi) ~ " AB " ~ fpt(ai);
        if (w != undefined && fw != undefined && aw != undefined)
        {
            const nw = circumR(fw, w, aw);
            const tp = atan2(fw[1] - aw[1], abs(fw[0] - aw[0]));
            ok = ok && abs(nw - %(nw).6f * meter) <= 0.02 * meter && abs(tp - %(tp).6f * degree) <= 0.001 * degree;
            msg = msg ~ ", natural (widest) " ~ fm(nw) ~ ", taper " ~ fdeg(tp);
        }
        if (w != undefined && fi != undefined && ai != undefined)
        {
            const ni = circumR(fi, w, ai);
            const av = avgR(%(src)s, min(fi[0], ai[0]), max(fi[0], ai[0]));
            ok = ok && abs(ni - %(ni).6f * meter) <= 0.02 * meter && av.n == 200 && abs(av.avg - %(av).6f * meter) <= 0.01 * meter;
            msg = msg ~ ", natural (inflection) " ~ fm(ni) ~ ", average radius (checker, feature's interval) " ~ fm(av.avg) ~ " at " ~ av.n ~ " stations";
        }
        else
        {
            ok = false;
            msg = msg ~ " -- inflection line missing (no inflection found)";
        }
        return [ok, msg ~ " | expected waist (%(wx).3f, %(wy).3f), widest (%(fwx).3f, %(fwy).3f) (%(awx).3f, %(awy).3f), inflections x %(fix).3f / %(aix).3f, natural %(nw).4f / %(ni).4f m, average %(av).4f m, taper %(tp).4f deg"];''' % v


def body_points(case, fid):
    regions = fs_list(['{ "name" : "%s", "count" : %d, "xlo" : %.6f, "xhi" : %.6f }' % (r["name"], r["count"], r["xlo"], r["xhi"])
                       for r in case["expect"]["regions"]])
    want = "; ".join("%s %d x %.3f..%.3f" % (r["name"], r["count"], r["xlo"], r["xhi"]) for r in case["expect"]["regions"])
    return r'''
        const fptEdges = %s;
        var ok = true;
        var msg = "";
        for (var rg in %s)
        {
            const pts = pointsOf(qCreatedBy(makeId(SELF) + ("sketch" ~ rg.name), EntityType.VERTEX));
            var dev = 0 * meter;
            var cmin = inf * meter;
            var cmax = 0 * meter;
            for (var i = 0; i < size(pts); i += 1)
            {
                dev = max(dev, evDistance(context, { "side0" : pts[i], "side1" : fptEdges }).distance);
                if (i > 0)
                {
                    cmin = min(cmin, norm(pts[i] - pts[i - 1]));
                    cmax = max(cmax, norm(pts[i] - pts[i - 1]));
                }
            }
            var good = size(pts) == rg.count;
            msg = msg ~ rg.name ~ ": " ~ size(pts) ~ " points";
            if (good)
            {
                good = abs(pts[0][0] - rg.xlo * mm) <= 0.01 * mm && abs(pts[size(pts) - 1][0] - rg.xhi * mm) <= 0.01 * mm
                    && dev <= 0.001 * mm && cmax <= cmin * 1.002;
                msg = msg ~ " x " ~ fmt(pts[0][0]) ~ ".." ~ fmt(pts[size(pts) - 1][0]) ~ ", off curve " ~ fmt(dev) ~ ", spacing " ~ fmt(cmin) ~ ".." ~ fmt(cmax);
            }
            ok = ok && good;
            msg = msg ~ "; ";
        }
        return [ok, msg ~ "| expected %s"];''' % (B.wire_edges(fid(B.fx_names(case["fixture"])["wire"])), regions, want)


def body_arcfit(case, fid):
    how, fx = case["source"]
    inp = B.wire_edges(fid(B.fx_names(fx)["wire"])) if how == "wire" else B.edges(fid(fx + " sketch"))
    tol = case["tol"] + 0.0005
    radii = case["expect"].get("radii")
    radius_check = ""
    if radii:
        radius_check = r'''
        const want = %s;
        var rok = size(radii) == size(want);
        if (rok)
        {
            for (var i = 0; i < size(want); i += 1)
            {
                rok = rok && abs(radii[i] - want[i] * mm) <= want[i] * 1e-4 * mm;
            }
        }
        ok = ok && rok;
        msg = msg ~ ", radii";
        for (var rr in radii)
        {
            msg = msg ~ " " ~ fmt(rr);
        }
        msg = msg ~ " (expected %s)";''' % (fs_list(["%.6f" % r for r in radii]), " ".join("%g" % r for r in radii))
    return r'''
        const out = wireBodies(bodies(SELF));
        const oe = edgesOf(out);
        const inp = %s;
        if (count(oe) == 0)
        {
            return [false, "no output edges"];
        }
        var other = 0;
        var radii = [];
        for (var e in evaluateQuery(context, oe))
        {
            const cd = evCurveDefinition(context, { "edge" : e });
            if (cd is Circle)
            {
                radii = append(radii, cd.radius);
            }
            else if (!(cd is Line))
            {
                other += 1;
            }
        }
        radii = sort(radii, function(a, b) { return a - b; });
        const dOut = maxDev(oe, inp, 20);
        const dIn = maxDev(inp, oe, 400);
        var ok = count(out) == 1 && other == 0 && dOut <= %.6f * mm && dIn <= %.6f * mm;
        var msg = count(out) ~ " wire(s), " ~ count(oe) ~ " edges (" ~ size(radii) ~ " arcs, " ~ other ~ " neither arc nor line), output to input " ~ fmt(dOut) ~ " mm, input to output " ~ fmt(dIn) ~ " mm";%s
        return [ok, msg ~ " | expected 1 wire, arcs / lines only, both deviations <= %.4f mm"];''' % (inp, tol, tol, radius_check, tol)


def body_integrate(case, fid):
    e = case["expect"]
    if e.get("allAnalytic"):
        return r'''
        const es = evaluateQuery(context, edgesOf(wireBodies(bodies(SELF))));
        var other = 0;
        var msg = size(es) ~ " edges:";
        for (var ed in es)
        {
            const cd = evCurveDefinition(context, { "edge" : ed });
            if (!(cd is Circle) && !(cd is Line))
            {
                other += 1;
            }
            msg = msg ~ " " ~ (cd is Circle ? "arc" : (cd is Line ? "line" : "spline"));
        }
        return [size(es) > 0 && other == 0, msg ~ " | expected every edge an arc or a line"];'''
    want = fs_list(['{ "R" : %.6f, "cx" : %.6f, "cy" : %.6f }' % (w["R"], w["c"][0], w["c"][1]) if w["R"] else '{ "R" : 0 }'
                    for w in e["edges"]])
    ctx, cty = e["ctol"]
    g1tol = e.get("g1tol", 0.05 if any(not w["R"] for w in e["edges"]) else 0.01)
    extra = ""
    if "widest" in e:
        (fx_, fy_), (ax_, ay_) = e["widest"]
        extra += r'''
        const fw = extreme(s, -inf * meter, wst[0], false);
        const aw = extreme(s, wst[0], inf * meter, false);
        ok = ok && near(fw, %.6f, %.6f, 1.5, 0.01) && near(aw, %.6f, %.6f, 1.5, 0.01);
        msg = msg ~ ", widest " ~ fpt(fw) ~ " " ~ fpt(aw) ~ " (expected (%.3f, %.3f) (%.3f, %.3f))";''' % (fx_, fy_, ax_, ay_, fx_, fy_, ax_, ay_)
    if "taper" in e:
        # taper = forebody (FCP side) end height minus aftbody end height; the FCP side is the low-x end unless fbHighX
        fb, ab = ("s[size(s) - 1]", "s[0]") if e.get("fbHighX") else ("s[0]", "s[size(s) - 1]")
        extra += (r'''
        const tp = atan2(FB.p[1] - AB.p[1], abs(s[size(s) - 1].p[0] - s[0].p[0]));'''.replace("FB", fb).replace("AB", ab)) + r'''
        ok = ok && abs(tp - %.6f * degree) <= 0.002 * degree;
        msg = msg ~ ", taper from the end heights " ~ fdeg(tp) ~ " (expected %.4f deg)";''' % (e["taper"], e["taper"])
    wx, wy = e["waist"]
    return r'''
        const out = wireBodies(bodies(SELF));
        const es = byX(edgesOf(out));
        if (size(es) == 0)
        {
            return [false, "no output edges"];
        }
        const want = %s;
        var ok = count(out) == 1 && size(es) == size(want);
        var msg = count(out) ~ " wire(s), " ~ size(es) ~ " edges:";
        for (var i = 0; i < size(es); i += 1)
        {
            const cd = evCurveDefinition(context, { "edge" : es[i] });
            const wi = i < size(want) ? want[i] : { "R" : -1 };
            var good = false;
            if (cd is Circle)
            {
                msg = msg ~ " arc R" ~ fmt(cd.radius) ~ " centre " ~ fpt(cd.coordSystem.origin) ~ ";";
                if (wi.R > 0)
                {
                    good = abs(cd.radius - wi.R * mm) <= 0.01 * mm && near(cd.coordSystem.origin, wi.cx, wi.cy, %.6f, %.6f);
                }
            }
            else
            {
                msg = msg ~ " " ~ (cd is Line ? "line" : "spline") ~ ";";
                good = wi.R == 0;
            }
            ok = ok && good;
        }
        const brk = g1(es);
        ok = ok && brk <= %.4f * degree;
        const s = samples(edgesOf(out), 400, true);
        const wst = extreme(s, -inf * meter, inf * meter, true);
        ok = ok && near(wst, %.6f, %.6f, 2, 0.01);
        msg = msg ~ " tangent break " ~ fdeg(brk) ~ ", waist " ~ fpt(wst);%s
        return [ok, msg ~ " | expected %s, tangent break <= %.2f deg, waist (%.3f, %.3f)"];''' % (
        want, ctx, cty, g1tol, wx, wy, extra,
        "; ".join(("arc R%g centre (%.3f, %.3f)" % (w["R"], w["c"][0], w["c"][1])) if w["R"] else "spline" for w in e["edges"]),
        g1tol, wx, wy)


def body_scale(case, fid):
    e = case["expect"]
    fcpx, acpx = e["newFcp"], e["newAcp"]
    # the FCP may be at either x (tip toward +X: fcpx > acpx); samples run low x -> high x
    lox, hix = min(fcpx, acpx), max(fcpx, acpx)
    endLo, endHi = sorted([e["tipEnd"][0], e["tailEnd"][0]])
    parts = []
    parts.append(r'''
        ok = ok && near(s[0].p, %.6f, 0, 0.05, 0.01) && near(s[size(s) - 1].p, %.6f, 0, 0.05, 0.01);
        msg = msg ~ ", ends " ~ fpt(s[0].p) ~ " " ~ fpt(s[size(s) - 1].p) ~ " (expected x %.3f / %.3f)";''' % (
        endLo, endHi, endLo, endHi))
    for k, cp in enumerate(e.get("contacts", [])):
        parts.append(r'''
        const dC%d = evDistance(context, { "side0" : %s, "side1" : pe }).distance;
        ok = ok && dC%d <= 0.001 * mm;
        msg = msg ~ ", contact (%.3f, %.3f) off by " ~ fmt(dC%d);''' % (k, P(cp), k, cp[0], cp[1], k))
    if "waist" in e:
        parts.append(r'''
        ok = ok && near(wst, %.6f, %.6f, 2, 0.05);
        msg = msg ~ ", waist " ~ fpt(wst) ~ " (expected (%.3f, %.3f))";''' % (e["waist"] + e["waist"]))
    if "widest" in e:
        (fx_, fy_), (ax_, ay_) = e["widest"]
        parts.append(r'''
        ok = ok && near(fw, %.6f, %.6f, 3, 0.05) && near(aw, %.6f, %.6f, 3, 0.05);
        msg = msg ~ ", widest " ~ fpt(fw) ~ " " ~ fpt(aw) ~ " (expected (%.3f, %.3f) (%.3f, %.3f))";''' % (fx_, fy_, ax_, ay_, fx_, fy_, ax_, ay_))
    if "taper" in e:
        parts.append(r'''
        const tp = (fw == undefined || aw == undefined) ? 90 * degree : atan2(fw[1] - aw[1], abs(fw[0] - aw[0]));
        ok = ok && abs(tp - %.6f * degree) <= 0.001 * degree;
        msg = msg ~ ", taper " ~ fdeg(tp) ~ " (expected %.4f deg)";''' % (e["taper"], e["taper"]))
    if "acpWidth" in e:
        parts.append(r'''
        const dA = evDistance(context, { "side0" : %s, "side1" : pe }).distance;
        ok = ok && dA <= 0.01 * mm;
        msg = msg ~ ", ACP width point (%.3f, %.3f) off by " ~ fmt(dA);''' % (P(e["acpWidth"]), e["acpWidth"][0], e["acpWidth"][1]))
    if e.get("mirror"):
        parts.append(r'''
        const low = extreme(samples(edgesOf(neg), 400, true), -inf * meter, inf * meter, true);
        const high = extreme(s, -inf * meter, inf * meter, false);
        const mirrorGap = low == undefined ? inf * meter : abs(low[1] + high[1]);
        ok = ok && mirrorGap <= 0.01 * mm;
        msg = msg ~ ", -Y lowest " ~ fpt(low) ~ " vs +Y highest " ~ fpt(high);''')
    if "radius" in e:
        parts.append(r'''
            const sm = samples(pe, 400, false);
            const i1 = crossing(sm, wst[0], -1);
            const i2 = crossing(sm, wst[0], 1);
            if (i1 == undefined || i2 == undefined)
            {
                ok = false;
                msg = msg ~ ", output inflection not found";
            }
            else
            {
                const av = avgR(pe, min(i1, i2), max(i1, i2));
                ok = ok && abs(av.avg - %.6f * meter) <= 0.2 * meter;
                msg = msg ~ ", inflections x " ~ fmt(i1) ~ " / " ~ fmt(i2) ~ ", average radius " ~ fm(av.avg) ~ " (expected %.2f m +- 0.2)";
            }''' % (e["radius"], e["radius"]))
    if "affine" in e:
        ref = 'sketchEntityQuery(makeId("%s"), EntityType.EDGE, "s")' % fid(B.fx_names(case["fixture"])["sketch"])
        parts.append(r'''
            var worst = 0 * meter;
            for (var l in evEdgeTangentLines(context, { "edge" : %s, "parameters" : params(41, true) }))
            {
                const img = vector(%.6f * mm + (l.origin[0] - %.6f * mm) * %.6f, l.origin[1], 0 * mm);
                worst = max(worst, evDistance(context, { "side0" : img, "side1" : pe }).distance);
            }
            ok = ok && worst <= 0.001 * mm;
            msg = msg ~ ", x-scaled reference spline off the output by " ~ fmt(worst) ~ " mm (expected <= 0.001)";''' % (ref, e["affine"]["x0"], e["affine"]["x0"], e["affine"]["k"]))
    return r'''
        const pos = qCreatedBy(makeId(SELF) + "posOut" + "wire", EntityType.BODY);
        const neg = qCreatedBy(makeId(SELF) + "negOut" + "wire", EntityType.BODY);
        const pe = edgesOf(pos);
        if (count(pe) == 0)
        {
            return [false, "no +Y output wire"];
        }
        var ok = count(pos) == 1 && count(neg) == 1;
        var msg = count(pos) ~ " +Y / " ~ count(neg) ~ " -Y wire(s)";
        const s = samples(pe, 400, true);
        // the waist: lowest point in the middle half of the new RSL
        const wst = extreme(s, %.6f * mm, %.6f * mm, true);
        if (wst == undefined)
        {
            return [false, msg ~ ", no output between the contact points"];
        }
        const fw = %s;
        const aw = %s;%s
        return [ok, msg];''' % (lox + 0.25 * (hix - lox), hix - 0.25 * (hix - lox),
                                ("extreme(s, %.6f * mm, wst[0], false)" % fcpx) if fcpx <= acpx else ("extreme(s, wst[0], %.6f * mm, false)" % fcpx),
                                ("extreme(s, wst[0], %.6f * mm, false)" % acpx) if fcpx <= acpx else ("extreme(s, %.6f * mm, wst[0], false)" % acpx),
                                "".join(parts))


BODIES = {"analyze": body_analyze, "points": body_points, "arcfit": body_arcfit, "integrate": body_integrate, "scale": body_scale}
CASE_STATUS = {"OK", "INFO"}


def strings(result):
    return [s for s in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(result)) if not s.startswith("BTFSValue")]


def param_value(feature, pid):
    for p in feature.get("parameters", []):
        if p.get("parameterId") == pid:
            return p.get("value")
    return None


def dialog_check(case, feature):
    """Analyze footprint: compare the dialog strings when the editing logic has written them (after Recalculate)."""
    notes, ok = [], True
    for pid, key in [("avgRadiusStr", "averageRadius"), ("natRadiusInflectionStr", "naturalInflection")]:
        v = param_value(feature, pid)
        m = re.match(r"\s*([-0-9.]+)\s*m", v or "")
        if not m:
            continue
        good = abs(float(m.group(1)) - case["expect"][key]) <= 0.011
        ok = ok and good
        notes.append("dialog %s '%s'%s" % (pid, v, "" if good else " (expected %.2f m)" % case["expect"][key]))
    return ok, notes


def main():
    c = OnshapeClient()
    doc = json.load(open("footprint/.document.json"))
    D, W = doc["document_id"], doc["workspace_id"]
    found = [e["id"] for e in c.list_elements(D, W) if e["name"] == B.STUDIO and e["elementType"] == "PARTSTUDIO"]
    if not found:
        raise SystemExit("no '%s' Part Studio: run build_footprint_tests.py first" % B.STUDIO)
    base = f"/api/v10/partstudios/d/{D}/w/{W}/e/{found[0]}"
    feats = c.get(f"{base}/features")
    states = feats["featureStates"]
    by_name = [(x["name"], x["featureId"]) for x in feats["features"]]
    by_id = {x["featureId"]: x for x in feats["features"]}
    case_ids = [case["id"] for case in B.CASES]
    failed = 0

    def fid(name):
        key = name.split(":")[0] + ":"
        hits = [i for n, i in by_name if n.startswith(key)]
        if len(hits) != 1:
            raise SystemExit("fixture %r matches %d features" % (key, len(hits)))
        return hits[0]

    def is_case(name):
        return "->" in name and name.split(" ")[0] in case_ids

    # fixtures (sketches, composite curves, profile sketches) must regenerate cleanly
    for name, i in by_name:
        status = states.get(i, {}).get("featureStatus")
        if not is_case(name) and status != "OK":
            failed += 1
            print("FAIL", name, "-- status", status, "expected OK")

    xfail = 0
    for case in B.CASES:
        hits = [(n, i) for n, i in by_name if n.startswith(case["id"] + " ") and "->" in n]
        if len(hits) != 1:
            print("FAIL", case["id"], "-- case feature not found (%d matches)" % len(hits))
            failed += 1
            continue
        name, self_id = hits[0]
        status = states.get(self_id, {}).get("featureStatus")
        src = BODIES[case["kind"]](case, fid).replace("SELF", '"%s"' % self_id)
        script = ("function(context is Context, queries)\n{\n" + HELPERS + "    const check = function() returns array\n        {\n"
                  + src + "\n        };\n    const r = check();\n    return [r[0] ? \"PASS\" : \"FAIL\", r[1]];\n}\n")
        r = c.post(f"{base}/featurescript", json_data={"script": script})
        errors = [x.get("message") for x in (r.get("notices") or []) if x.get("level") == "ERROR"]
        got = strings(r.get("result"))
        if errors or len(got) < 2:
            verdict, detail = "FAIL", "check did not run: " + "; ".join(str(x) for x in (errors or ["no result"]))
        else:
            verdict, detail = got[0], got[1]
        if status not in CASE_STATUS:
            verdict, detail = "FAIL", "status %s (expected OK or INFO); %s" % (status, detail)
        if case["kind"] == "analyze" and verdict == "PASS":
            ok, notes = dialog_check(case, by_id[self_id])
            if notes:
                detail += "; " + "; ".join(notes)
            if not ok:
                verdict = "FAIL"
        if case["id"] in B.EXPECT_FAIL:
            if verdict == "PASS":
                verdict = "XPASS"
                detail += " -- known bug no longer reproduces: remove %s from EXPECT_FAIL (%s)" % (case["id"], B.EXPECT_FAIL[case["id"]])
                failed += 1
            else:
                verdict = "XFAIL"
                detail += " -- known bug: " + B.EXPECT_FAIL[case["id"]]
                xfail += 1
        else:
            failed += verdict != "PASS"
        print(verdict, name, "--", detail)
    print("%d failed, %d expected failures" % (failed, xfail) if failed else "all passed (%d expected failures)" % xfail)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
