"""Check the Reference_Side test cases in the "Reference side tests" Part Studio (built by
build_reference_side_tests.py): every case is measured through the eval API and printed PASS / FAIL, and every
feature's status is checked (T4 must fail: that is its case).

usage (repo root): PYTHONPATH=. python devtools/onshape/check_reference_side_tests.py
"""
import json
import re
import sys

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("reference_side/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
E = [e["id"] for e in c.list_elements(D, W) if e["name"] == "Reference side tests"][0]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"

HELPERS = r'''
    const mm = function(x) { return x * millimeter; };
    const pt = function(x, y, z) { return vector(x, y, z) * millimeter; };
    const near = function(v, target, tol) { return abs(v - target) <= tol; };
    const fmt = function(v) { return toString(roundToPrecision(v / millimeter, 4)); };
    const fmtV = function(p) { return "(" ~ fmt(p[0]) ~ ", " ~ fmt(p[1]) ~ ", " ~ fmt(p[2]) ~ ")"; };
    const created = function(fid) { return qCreatedBy(makeId(fid), EntityType.BODY); };
    const embedded = function(fid) { return getVariable(context, toString(makeId(fid))); };
    const count = function(q) { return size(evaluateQuery(context, q)); };
    const centroid = function(q) { return evApproximateCentroid(context, { "entities" : q }); };
    const totalLength = function(bodies) { return evLength(context, { "entities" : qOwnedByBody(bodies, EntityType.EDGE) }); };
    // min / max distance from points along a wire's edges to the source
    const distanceRange = function(wire, source)
        {
            var lo = inf * meter;
            var hi = 0 * meter;
            for (var e in evaluateQuery(context, qOwnedByBody(wire, EntityType.EDGE)))
            {
                for (var l in evEdgeTangentLines(context, { "edge" : e, "parameters" : [0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1] }))
                {
                    const dist = evDistance(context, { "side0" : l.origin, "side1" : source }).distance;
                    lo = min(lo, dist);
                    hi = max(hi, dist);
                }
            }
            return { "lo" : lo, "hi" : hi };
        };
    const wireEdges = function(fid) { return qOwnedByBody(qBodyType(qCreatedBy(makeId(fid), EntityType.BODY), BodyType.WIRE), EntityType.EDGE); };
    // the largest-volume body of a set
    const largest = function(bodies)
        {
            var best = bodies[0];
            for (var b in bodies)
            {
                if (evVolume(context, { "entities" : b }) > evVolume(context, { "entities" : best }))
                {
                    best = b;
                }
            }
            return best;
        };
    // z range of a body
    const zRange = function(b)
        {
            const bb = evBox3d(context, { "topology" : b, "tight" : true });
            return [bb.minCorner[2], bb.maxCorner[2]];
        };
'''

CASES = []


def case(prefix, body, **names):
    CASES.append((prefix, body, names))


# Split+ v2 (2026-09-27): start / end tools, inside reference, Keep both / inside / outside.
for n, kept in enumerate(["INSIDE", "INSIDE", "OUTSIDE"]):
    want_z = 25 if kept == "INSIDE" else -25
    case("S%d " % (n + 1), r'''
        const bodies = evaluateQuery(context, qUnion([created(@CUBE@), created(SELF)]));
        const cc = size(bodies) == 1 ? centroid(bodies[0]) : pt(0, 0, 0);
        const out = embedded(SELF);
        const region = count(out.query.%s.value);
        return [size(bodies) == 1 && near(cc[2], mm(%d), mm(0.01)) && region == 1,
            size(bodies) ~ " bodies (1), centroid z " ~ fmt(cc[2]) ~ " (%d), %s " ~ region ~ " (1)"];''' % (
        kept.lower(), want_z, want_z, kept.lower()), CUBE="S%d cube" % (n + 1))

case("S4 ", r'''
        const out = embedded(SELF);
        const ins = evaluateQuery(context, out.query.inside.value);
        const st = evaluateQuery(context, out.query.start.value);
        const zi = size(ins) == 1 ? centroid(ins[0])[2] : 0 * meter;
        const zs = size(st) == 1 ? centroid(st[0])[2] : 0 * meter;
        const cut = count(out.query.startCut.value);
        const endCount = count(out.query.end.value) + count(out.query.endCut.value);
        const ok = size(ins) == 1 && size(st) == 1 && near(zi, mm(25), mm(0.01)) && near(zs, mm(-25), mm(0.01)) && cut == 4 && endCount == 0;
        return [ok, "inside z " ~ fmt(zi) ~ " (25), start z " ~ fmt(zs) ~ " (-25), startCut " ~ cut ~ " (4), end + endCut " ~ endCount ~ " (0)"];''')

case("S5 ", r'''
        const out = embedded(SELF);
        var ok = count(out.query.output.value) == 3;
        var detail = count(out.query.output.value) ~ " pieces (3); ";
        for (var region in [["inside", 1600], ["start", 1565], ["end", 1635]])
        {
            const bodies = evaluateQuery(context, out.query[region[0]].value);
            const x = size(bodies) == 1 ? centroid(bodies[0])[0] : 0 * meter;
            ok = ok && size(bodies) == 1 && near(x, mm(region[1]), mm(0.01));
            detail ~= region[0] ~ " " ~ size(bodies) ~ " at x " ~ fmt(x) ~ "; ";
        }
        const startCut = count(out.query.startCut.value);
        const endCut = count(out.query.endCut.value);
        const outside = count(out.query.outside.value);
        const insideEdges = count(out.query.insideEdges.value);
        ok = ok && startCut == 1 && endCut == 1 && outside == 2 && insideEdges == 2;
        return [ok, detail ~ "startCut " ~ startCut ~ " (1), endCut " ~ endCut ~ " (1), outside " ~ outside ~ " (2), insideEdges " ~ insideEdges ~ " (2)"];''')

case("S6 ", r'''
        const out = embedded(SELF);
        const kept = evaluateQuery(context, out.query.output.value);
        const x = size(kept) == 1 ? centroid(kept[0])[0] : 0 * meter;
        return [size(kept) == 1 && near(x, mm(2000), mm(0.01)), size(kept) ~ " piece(s) (1) at x " ~ fmt(x) ~ " (2000)"];''')

case("S7 ", r'''
        const out = embedded(SELF);
        const kept = count(out.query.output.value);
        const st = evaluateQuery(context, out.query.start.value);
        const en = evaluateQuery(context, out.query.end.value);
        const xs = size(st) == 1 ? centroid(st[0])[0] : 0 * meter;
        const xe = size(en) == 1 ? centroid(en[0])[0] : 0 * meter;
        // keep outside: the cut edges must be on the KEPT (outside) pieces, not the deleted inside one
        const sc = count(out.query.startCut.value);
        const ec = count(out.query.endCut.value);
        return [kept == 2 && near(xs, mm(2365), mm(0.01)) && near(xe, mm(2435), mm(0.01)) && sc == 1 && ec == 1,
            kept ~ " pieces (2), start x " ~ fmt(xs) ~ " (2365), end x " ~ fmt(xe) ~ " (2435), startCut " ~ sc ~ " (1), endCut " ~ ec ~ " (1)"];''')

case("S8 ", r'''
        const bodies = evaluateQuery(context, qUnion([created(@CUBE@), created(SELF)]));
        const cc = size(bodies) == 1 ? centroid(bodies[0]) : pt(0, 0, 0);
        const toolGone = isQueryEmpty(context, created(@TOOL@));
        return [size(bodies) == 1 && near(cc[2], mm(-25), mm(0.01)) && toolGone,
            size(bodies) ~ " bodies, centroid z " ~ fmt(cc[2]) ~ " (-25), tool deleted " ~ toolGone];''', CUBE="S8 cube", TOOL="S8 tool sheet z 0, x 2700..2900 (deleted by the split)")

case("S9 ", r'''
        const out = embedded(SELF);
        const cube = created(@CUBE@);
        const faces = count(qOwnedByBody(cube, EntityType.FACE));
        var ok = count(cube) == 1 && faces == 10;
        var detail = "";
        for (var region in [["start", 3165], ["inside", 3200], ["end", 3235]])
        {
            const found = evaluateQuery(context, out.query[region[0]].value);
            for (var f in found)
            {
                ok = ok && near(centroid(f)[0], mm(region[1]), mm(0.01));
            }
            ok = ok && size(found) == 2;
            detail ~= region[0] ~ " " ~ size(found) ~ " faces; ";
        }
        const insideEdges = count(out.query.insideEdges.value);
        const startCut = count(out.query.startCut.value);
        const splitEdges = count(out.query.splitEdges.value);
        ok = ok && insideEdges == 2 && startCut == 2 && splitEdges == 4 && out.variable.pieceCount.value == 6;
        return [ok, detail ~ "insideEdges " ~ insideEdges ~ " (2), startCut " ~ startCut ~ " (2), splitEdges " ~ splitEdges ~ " (4), pieceCount "
            ~ out.variable.pieceCount.value ~ " (6), cube faces " ~ faces ~ " (10)"];''', CUBE="S9 cube")

case("S10 ", r'''
        const out = embedded(SELF);
        const insideFaces = evaluateQuery(context, out.query.inside.value);
        const startFaces = evaluateQuery(context, out.query.start.value);
        const x = size(insideFaces) == 1 ? centroid(insideFaces[0])[0] : 0 * meter;
        const toolGone = isQueryEmpty(context, created(@TOOL@));
        return [size(insideFaces) == 1 && size(startFaces) == 1 && near(x, mm(3575), mm(0.01)) && toolGone,
            "inside " ~ size(insideFaces) ~ " at x " ~ fmt(x) ~ " (1 at 3575), start " ~ size(startFaces) ~ " (1), tool deleted " ~ toolGone];''',
     TOOL="S10 tool sheet x = 3600 (deleted by the split)")

for n, toward in enumerate([True, False]):
    tag = "O%d" % (n + 1)
    case(tag + " ", r'''
        // the two inputs really have opposite normals
        const nTop = evPlane(context, { "face" : qOwnedByBody(created(@TOP@), EntityType.FACE) }).normal;
        const nBottom = evPlane(context, { "face" : qOwnedByBody(created(@BOTTOM@), EntityType.FACE) }).normal;
        const opposite = dot(nTop, vector(0, 0, 1)) * dot(nBottom, vector(0, 0, 1)) > 0;
        var zs = [];
        for (var b in evaluateQuery(context, created(SELF)))
        {
            zs = append(zs, centroid(b)[2]);
        }
        const want = mm(%d);
        const boundary = count(embedded(SELF).query.boundaryEdges.value);
        const ok = size(zs) == 2 && near(abs(zs[0]), want, mm(0.001)) && near(abs(zs[1]), want, mm(0.001)) && zs[0] * zs[1] < 0 * meter * meter;
        return [ok && boundary == 8 && !opposite, "z " ~ (size(zs) == 2 ? fmt(zs[0]) ~ ", " ~ fmt(zs[1]) : size(zs) ~ " bodies") ~ " (+-%d), boundaryEdges "
            ~ boundary ~ " (8), input normals opposite in z " ~ !opposite];''' % ((40, 40) if toward else (60, 60)),
         TOP=tag + " top sheet z 50", BOTTOM=tag + " bottom sheet z -50 (drawn reversed: opposite normal)")

n = 0
for mode in ["PLANE", "TRANSPORT"]:
    for toward in [True, False]:
        n += 1
        tag = "C%d" % n
        x0 = 5000 if mode == "PLANE" else 5400
        y0 = 0 if toward else 500
        case(tag + " ", r'''
        const wire = created(SELF);
        const length = totalLength(wire);
        const range = distanceRange(wire, wireEdges(@SQUARE@));
        return [count(wire) == 1 && near(length, mm(%s), mm(0.01)) && near(range.lo, mm(10), mm(0.002)) && near(range.hi, mm(10), mm(0.002)),
            count(wire) ~ " wire, length " ~ fmt(length) ~ " (%s), distance to the square " ~ fmt(range.lo) ~ " .. " ~ fmt(range.hi) ~ " (10)"];''' % (
            "400 + 20 * PI" if toward else "320", "462.8319" if toward else "320"), SQUARE="%s square 100 at (%d, %d)" % (tag, x0, y0))

for n, direction in enumerate(["NORMAL", "TANGENT"]):
    x0 = 6000 + 400 * n
    want = (x0, 0, 60) if direction == "NORMAL" else (x0, 10, 50)
    case("C%d " % (5 + n), r'''
        const bb = evBox3d(context, { "topology" : created(SELF) });
        const mid = (bb.minCorner + bb.maxCorner) / 2;
        return [norm(mid - pt(%g, %g, %g)) < mm(0.002), "centre " ~ fmtV(mid) ~ " (%g, %g, %g)"];''' % (*want, *want))

case("C7 ", r'''
        const helix = qCreatedBy(makeId(@HELIX@), EntityType.EDGE);
        const wire = created(SELF);
        const range = distanceRange(wire, helix);
        const start = evEdgeTangentLine(context, { "edge" : helix, "parameter" : 0 }).origin;
        const onWire = evDistance(context, { "side0" : start, "side1" : wire }).sides[1].point;
        const r = norm(vector(onWire[0] - mm(6800), onWire[1], 0 * meter));
        return [near(range.lo, mm(10), mm(0.005)) && near(range.hi, mm(10), mm(0.005)) && r < mm(50),
            "distance to the helix " ~ fmt(range.lo) ~ " .. " ~ fmt(range.hi) ~ " (10), start radius " ~ fmt(r) ~ " (< 50: toward the axis)"];''',
     HELIX="C7 helix")

case("C8 ", r'''
        const wire = created(SELF);
        const length = totalLength(wire);
        const range = distanceRange(wire, wireEdges(@CHAIN@));
        const out = embedded(SELF);
        const starts = evaluateQuery(context, out.query.startVertex.value);
        const ends = evaluateQuery(context, out.query.endVertex.value);
        const arcs = evaluateQuery(context, out.query.cornerArcs.value);
        const startEdges = evaluateQuery(context, out.query.startEdge.value);
        const startAt = size(starts) == 1 ? evVertexPoint(context, { "vertex" : starts[0] }) : pt(0, 0, 0);
        const endAt = size(ends) == 1 ? evVertexPoint(context, { "vertex" : ends[0] }) : pt(0, 0, 0);
        const arcLength = size(arcs) == 1 ? evLength(context, { "entities" : arcs[0] }) : 0 * meter;
        const startEdgeOk = size(startEdges) == 1 && !isQueryEmpty(context, qIntersection([qAdjacent(startEdges[0], AdjacencyType.VERTEX, EntityType.VERTEX), qUnion(starts)]));
        return [count(wire) == 1 && near(length, mm(280 + 5 * PI), mm(0.01)) && near(range.lo, mm(10), mm(0.002))
                && norm(startAt - pt(7200, -10, 0)) < mm(0.01) && norm(endAt - pt(7400, 90, 0)) < mm(0.01) && near(arcLength, mm(5 * PI), mm(0.01)) && startEdgeOk,
            "length " ~ fmt(length) ~ " (295.708), start " ~ fmtV(startAt) ~ ", end " ~ fmtV(endAt) ~ ", " ~ size(arcs) ~ " arc of " ~ fmt(arcLength)
            ~ " (15.708), start edge at the start " ~ startEdgeOk];''', CHAIN="C8 Z chain")

for n, near1 in enumerate([True, False]):
    tag = "M%d" % (n + 1)
    x0 = 7800 + 400 * n
    want = x0 + 12.5 if near1 else x0 - 12.5
    want1 = x0 + 25 if near1 else x0 - 25
    case(tag + " ", r'''
        const surfaces = qUnion([created(@H@), created(@V@)]);
        const faces = qOwnedByBody(surfaces, EntityType.FACE);
        const area = evArea(context, { "entities" : faces });
        const cc = centroid(faces);
        const out = embedded(SELF);
        const c1 = centroid(out.query.keptFaces1.value);
        const c2 = centroid(out.query.keptFaces2.value);
        const a1 = evArea(context, { "entities" : out.query.keptFaces1.value });
        const a2 = evArea(context, { "entities" : out.query.keptFaces2.value });
        const sq = millimeter * millimeter;
        // trimEdges: only the trim line (100 mm), not the perimeter halves the extended imprint split
        const trim = evaluateQuery(context, out.query.trimEdges.value);
        const trimLen = size(trim) == 1 ? evLength(context, { "entities" : trim[0] }) : 0 * meter;
        return [near(area, 10000 * sq, 0.01 * sq) && norm(cc - pt(%g, 0, 12.5)) < mm(0.01)
                && near(a1, 5000 * sq, 0.01 * sq) && near(a2, 5000 * sq, 0.01 * sq) && norm(c1 - pt(%g, 0, 0)) < mm(0.01) && norm(c2 - pt(%d, 0, 25)) < mm(0.01)
                && size(trim) == 1 && near(trimLen, mm(100), mm(0.01)) && out.variable.trimEdgeCount.value == 1,
            "area " ~ roundToPrecision(area / sq, 3) ~ " (10000), centroid " ~ fmtV(cc) ~ "; kept 1 at " ~ fmtV(c1) ~ ", kept 2 at " ~ fmtV(c2)
            ~ ", areas " ~ roundToPrecision(a1 / sq, 2) ~ " / " ~ roundToPrecision(a2 / sq, 2) ~ " (5000 each), trimEdges " ~ size(trim)
            ~ " of " ~ fmt(trimLen) ~ " (1 of 100)"];''' % (want, want1, x0),
         H="%s horizontal sheet" % tag, V="%s vertical sheet" % tag)

for n, (toward, away, swap) in enumerate([(5, 0, False), (2, 3, False), (2, 3, True)]):
    t_in, t_out = (away, toward) if swap else (toward, away)
    case("T%d " % (n + 1), r'''
        const bodies = evaluateQuery(context, qBodyType(created(SELF), BodyType.SOLID));
        var ok = size(bodies) == 2;
        var detail = "";
        for (var b in bodies)
        {
            const z = zRange(b);
            const isTop = z[1] > 0 * meter;
            const want = isTop ? [mm(%g), mm(%g)] : [mm(%g), mm(%g)];
            ok = ok && near(z[0], want[0], mm(0.0001)) && near(z[1], want[1], mm(0.0001));
            detail ~= (isTop ? "top" : "bottom") ~ " z " ~ fmt(z[0]) ~ ".." ~ fmt(z[1]) ~ "; ";
        }
        const out = embedded(SELF);
        var towardZ = [];
        for (var f in evaluateQuery(context, out.query.towardFaces.value))
        {
            towardZ = append(towardZ, fmt(abs(centroid(f)[2])));
        }
        var awayZ = [];
        for (var f in evaluateQuery(context, out.query.awayFaces.value))
        {
            awayZ = append(awayZ, fmt(abs(centroid(f)[2])));
        }
        const sides = count(out.query.sideFaces.value);
        ok = ok && towardZ == ["%s", "%s"] && awayZ == ["%s", "%s"] && sides == 8;
        return [ok, detail ~ "towardFaces at |z| " ~ join(towardZ, ", ") ~ " (%s), awayFaces at |z| " ~ join(awayZ, ", ") ~ " (%s), sideFaces " ~ sides ~ " (8)"];''' % (
        50 - t_in, 50 + t_out, -50 - t_out, -50 + t_in,
        50 - t_in, 50 - t_in, 50 + t_out, 50 + t_out, 50 - t_in, 50 + t_out))

case("T5 ", r'''
        const bodies = evaluateQuery(context, qBodyType(created(SELF), BodyType.SOLID));
        const v = size(bodies) == 1 ? evVolume(context, { "entities" : bodies[0] }) : 0 * meter ^ 3;
        const want = PI * (100 - 25) / 2 * 100 * millimeter ^ 3;
        return [size(bodies) == 1 && abs(v - want) < 0.01 * millimeter ^ 3, size(bodies) ~ " solid, volume " ~ roundToPrecision(v / millimeter ^ 3, 3) ~ " (" ~ roundToPrecision(want / millimeter ^ 3, 3) ~ ")"];''')

case("T6 ", r'''
        const cube = created(@CUBE@);
        const v = count(cube) == 1 ? evVolume(context, { "entities" : cube }) : 0 * meter ^ 3;
        const out = embedded(SELF);
        const toward = evaluateQuery(context, out.query.towardFaces.value);
        const z = size(toward) == 1 ? centroid(toward[0])[2] : 0 * meter;
        return [count(cube) == 1 && abs(v - 1018000 * millimeter ^ 3) < 0.01 * millimeter ^ 3 && size(toward) == 1 && near(z, mm(55), mm(0.0001)),
            count(cube) ~ " part, volume " ~ roundToPrecision(v / millimeter ^ 3, 3) ~ " (1018000), towardFaces " ~ size(toward) ~ " at z " ~ fmt(z) ~ " (1 at 55)"];''',
     CUBE="T6 cube")

for n, toward in enumerate([True, False]):
    case("R%d " % (n + 1), r'''
        const normalZ = function(b) { return evFaceTangentPlane(context, { "face" : evaluateQuery(context, qOwnedByBody(b, EntityType.FACE))[0], "parameter" : vector(0.5, 0.5) }).normal[2]; };
        const top = normalZ(created(@TOP@));
        const bottom = normalZ(created(@BOTTOM@));
        const out = embedded(SELF);
        const moved = count(out.query.flipped.value) + count(out.query.unchanged.value);
        const ok = (%s) && moved == 2;
        return [ok, "top normal z " ~ roundToPrecision(top, 3) ~ ", bottom " ~ roundToPrecision(bottom, 3) ~ " (%s); flipped + unchanged " ~ moved ~ " (2)"];''' % (
        "top < -0.99 && bottom > 0.99" if toward else "top > 0.99 && bottom < -0.99", "top -1, bottom +1" if toward else "top +1, bottom -1"),
        TOP="R%d top sheet" % (n + 1), BOTTOM="R%d bottom sheet" % (n + 1))

case("R3 ", r'''
        const f = evaluateQuery(context, qOwnedByBody(created(@HALF@), EntityType.FACE))[0];
        var ok = true;
        var worst = 1;
        for (var tp in evFaceTangentPlanes(context, { "face" : f, "parameters" : [vector(0.2, 0.5), vector(0.5, 0.5), vector(0.8, 0.5)] }))
        {
            const toAxis = vector(11800 * millimeter, tp.origin[1], 0 * meter) - tp.origin;
            const c = dot(tp.normal, normalize(toAxis));
            worst = min(worst, c);
            ok = ok && c > 0.999;
        }
        return [ok, "normal . (toward the axis) >= " ~ roundToPrecision(worst, 4) ~ " (1)"];''', HALF="R3 half cylinder")

for n, (top_z, bottom_z) in enumerate([(40, -40), (60, -60)]):
    case("F%d " % (n + 1), r'''
        const t = zRange(created(@TOP@));
        const b = zRange(created(@BOTTOM@));
        const ok = near(t[0], mm(%d), mm(0.001)) && near(t[1], mm(%d), mm(0.001)) && near(b[0], mm(%d), mm(0.001)) && near(b[1], mm(%d), mm(0.001));
        return [ok, "top sheet at z " ~ fmt(t[0]) ~ " (%d), bottom sheet at z " ~ fmt(b[0]) ~ " (%d)"];''' % (top_z, top_z, bottom_z, bottom_z, top_z, bottom_z),
         TOP="F%d top sheet" % (n + 1), BOTTOM="F%d bottom sheet" % (n + 1))

for tag, top_z in (("F3", 60), ("F4", 40)):
    case(tag + " ", r'''
        const z = zRange(created(@CUBE@));
        return [near(z[0], mm(-50), mm(0.001)) && near(z[1], mm(%d), mm(0.001)), "cube z " ~ fmt(z[0]) ~ ".." ~ fmt(z[1]) ~ " (-50..%d)"];''' % (top_z, top_z),
         CUBE="%s cube" % tag)

# Signed / zero distances (2026-09-27)
for tag, want in (("O3", 60), ("O4", 50)):
    case(tag + " ", r'''
        var zs = [];
        for (var b in evaluateQuery(context, created(SELF)))
        {
            zs = append(zs, centroid(b)[2]);
        }
        zs = sort(zs, function(a, b) { return a - b; });
        const ok = size(zs) == 2 && near(zs[0], mm(-%d), mm(0.001)) && near(zs[1], mm(%d), mm(0.001));
        return [ok, size(zs) ~ " sheets at z " ~ (size(zs) == 2 ? fmt(zs[0]) ~ ", " ~ fmt(zs[1]) : "?") ~ " (-%d, %d)"];''' % (want, want, want, want))

case("C9 ", r'''
        const n = count(created(SELF));
        const len = totalLength(created(SELF));
        return [n == 1 && near(len, mm(400), mm(0.001)), n ~ " wire(s), length " ~ fmt(len) ~ " (1, 400)"];''')

case("F5 ", r'''
        const t = zRange(created(@TOP@));
        const b = zRange(created(@BOTTOM@));
        const ok = near(t[0], mm(60), mm(0.001)) && near(b[0], mm(-60), mm(0.001));
        return [ok, "top sheet at z " ~ fmt(t[0]) ~ " (60), bottom sheet at z " ~ fmt(b[0]) ~ " (-60)"];''',
     TOP="F5 top sheet", BOTTOM="F5 bottom sheet")

case("S11 ", r'''
        const bodies = evaluateQuery(context, qUnion([created(@PLATE@), created(SELF)]));
        const v = size(bodies) == 1 ? evVolume(context, { "entities" : bodies[0] }) : 0 * meter ^ 3;
        const want = (200000 - 8000 * PI) * millimeter ^ 3;
        return [size(bodies) == 1 && abs(v - want) < 0.01 * millimeter ^ 3,
            size(bodies) ~ " body (1), volume " ~ roundToPrecision(v / millimeter ^ 3, 2) ~ " (" ~ roundToPrecision(want / millimeter ^ 3, 2) ~ ": the ring)"];''', PLATE="S11 plate")

EXPECTED_ERRORS = ["T4 Thicken+", "R4 Orient to reference"]


def strings(result):
    return [s for s in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(result)) if s not in ("BTFSValueString", "BTFSValueArray")]


def main():
    feats = c.get(f"{BASE}/features")
    states = feats["featureStates"]
    by_name = [(x["name"], x["featureId"]) for x in feats["features"]]
    failed = 0

    def fid(prefix):
        # a fixture, not its sketch
        found = [i for n, i in by_name if n.startswith(prefix) and not n.endswith("(sketch)")]
        if len(found) != 1:
            raise SystemExit("feature prefix %r matches %d features" % (prefix, len(found)))
        return found[0]

    # every feature regenerates, except the cases that must fail
    for name, i in by_name:
        status = states.get(i, {}).get("featureStatus")
        must_fail = any(name.startswith(p) for p in EXPECTED_ERRORS)
        if (status == "ERROR") != must_fail:
            failed += 1
            print("FAIL", name, "-- status", status, "(expected ERROR)" if must_fail else "")
        elif must_fail:
            print("PASS", name, "-- fails as it must")

    for prefix, body, names in CASES:
        cases = [(n, i) for n, i in by_name if n.startswith(prefix) and states.get(i, {}).get("featureStatus") != "ERROR"
                 and any(k in n for k in ["Split+", "Offset+", "Mutual Trim+", "Thicken+", "Orient to reference", "Move face+"])]
        if len(cases) != 1:
            print("FAIL", prefix, "-- case feature not found")
            failed += 1
            continue
        name, self_id = cases[0]
        src = body.replace("SELF", '"%s"' % self_id)
        for key, value in names.items():
            src = src.replace("@%s@" % key, '"%s"' % fid(value))
        script = ("function(context is Context, queries)\n{\n" + HELPERS + "    const check = function() returns array\n        {\n"
                  + src + "\n        };\n    const r = check();\n    return [r[0] ? \"PASS\" : \"FAIL\", r[1]];\n}\n")
        r = c.post(f"{BASE}/featurescript", json_data={"script": script})
        errors = [x.get("message") for x in (r.get("notices") or []) if x.get("level") == "ERROR"]
        got = strings(r.get("result"))
        if errors or len(got) < 2:
            print("FAIL", name, "-- check did not run:", "; ".join(errors or ["no result"]))
            failed += 1
            continue
        failed += got[0] != "PASS"
        print(got[0], name, "--", got[1])
    print("%d failed" % failed if failed else "all passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
