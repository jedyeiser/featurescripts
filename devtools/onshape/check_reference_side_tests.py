"""Check the Reference_Side test cases in the "Reference side tests" Part Studio (built by
build_reference_side_tests.py): every case is measured through the eval API and printed PASS / FAIL, and every
feature's status is checked (T4 must fail: that is its case).

usage (repo root): PYTHONPATH=. python devtools/onshape/check_reference_side_tests.py
"""
import json
import math
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

for n, (caps, nfaces, area, zgone) in enumerate([("TOWARD", 5, 13200, 55), ("AWAY", 5, 13200, 47), ("BOTH", 4, 3200, None)]):
    case("TD%d " % (1 + n), r'''
        const sheets = evaluateQuery(context, qBodyType(created(SELF), BodyType.SHEET));
        const solids = evaluateQuery(context, qBodyType(created(SELF), BodyType.SOLID));
        const faces = evaluateQuery(context, qOwnedByBody(created(SELF), EntityType.FACE));
        const area = size(faces) > 0 ? evArea(context, { "entities" : qUnion(faces) }) : 0 * meter ^ 2;
        var gone = true;
        for (var f in faces)
        {
            const z = centroid(f)[2];
            gone = gone && (%s);
        }
        const out = embedded(SELF);
        const tw = count(out.query.towardFaces.value);
        const aw = count(out.query.awayFaces.value);
        return [size(sheets) == 1 && size(solids) == 0 && size(faces) == %d && abs(area - %d * millimeter ^ 2) < 0.01 * millimeter ^ 2 && gone,
            size(sheets) ~ " sheet, " ~ size(solids) ~ " solid, " ~ size(faces) ~ " faces (%d), area " ~ roundToPrecision(area / millimeter ^ 2, 3) ~ " (%d), cap check " ~ gone
            ~ ", towardFaces " ~ tw ~ ", awayFaces " ~ aw ~ " (deleted caps resolve to 0)"];''' % (
        ("!near(z, mm(%d), mm(0.0001))" % zgone) if zgone else "true", nfaces, area, nfaces, area))

# Draft and side face offset (2026-11-23). Volumes are integrated here: at height h from the input face (toward is +) an
# outline wall sits shift(h) = offset + (h - hN) * tan(angle of its side) outward of the face outline, hN the neutral
# height (0 input face, +5 toward cap, -3 away cap); undrafted edges only carry the offset.
DR_SQUARE_SRC = r'''
        const result = qBodyType(created(SELF), BodyType.SOLID);
        const n = count(result);
        const v = n == 1 ? evVolume(context, { "entities" : result }) : 0 * meter ^ 3;
        const faces = n == 1 ? count(qOwnedByBody(result, EntityType.FACE)) : 0;
        var vol = 0;
        const steps = 4000;
        for (var i = 0; i < steps; i += 1)
        {
            const h = -3 + 8 * (i + 0.5) / steps;
            const shift = (__OFF__) + (h >= (__HN__) ? (h - (__HN__)) * (__KT__) : (h - (__HN__)) * (__KA__));
            const sx = 100 + 2 * (__XD__ ? shift : (__OFF__));
            const sy = 100 + 2 * (__YD__ ? shift : (__OFF__));
            vol += sx * sy * 8 / steps;
        }
        const out = embedded(SELF);
        const tw = evaluateQuery(context, out.query.towardFaces.value);
        const aw = evaluateQuery(context, out.query.awayFaces.value);
        const zt = size(tw) == 1 ? centroid(tw[0])[2] : 0 * meter;
        const za = size(aw) == 1 ? centroid(aw[0])[2] : 0 * meter;
        var ok = n == 1 && faces == __FACES__ && abs(v - vol * millimeter ^ 3) < 0.5 * millimeter ^ 3 && near(zt, mm(55), mm(0.001)) && near(za, mm(47), mm(0.001));
        var extra = "";
        __SHEETCHECK__
        return [ok, n ~ " solid (1), faces " ~ faces ~ " (__FACES__), volume " ~ roundToPrecision(v / millimeter ^ 3, 3) ~ " (" ~ roundToPrecision(vol, 3) ~ "), toward cap z " ~ fmt(zt)
            ~ " (55), away cap z " ~ fmt(za) ~ " (47)" ~ extra];'''
SHEET_CHECK = r'''
        const gone = isQueryEmpty(context, created(@SHEET@));
        const goneRef = isQueryEmpty(context, created(@T1SHEET@));
        ok = ok && gone == goneRef;
        extra = ", input sheet deleted " ~ gone ~ " (as for plain T1: " ~ goneRef ~ ")";'''
k10, k5, kn10, kn5 = (math.tan(math.radians(d)) for d in (10, 5, -10, -5))
for tag, kT, kA, hN, off, xd, yd in [
        ("DR1", k10, k10, 0, 0, True, True), ("DR2", kn10, kn10, 5, 0, True, True), ("DR3", k10, k10, -3, 0, True, True),
        ("DR4", k10, kn5, 0, 0, True, True), ("DR5", k10, k10, 0, 0, True, False), ("DR6", k10, k10, 0, 0, True, False),
        ("DR7", 0, 0, 0, 2, True, True), ("DR8", k5, k5, 0, -2, True, True)]:
    sheet_src = SHEET_CHECK if tag == "DR1" else ""
    src = DR_SQUARE_SRC
    for key, value in {"__KT__": repr(kT), "__KA__": repr(kA), "__HN__": repr(hN), "__OFF__": repr(off), "__XD__": "true" if xd else "false",
                       "__YD__": "true" if yd else "false", "__SHEETCHECK__": sheet_src, "__FACES__": "10" if tag == "DR4" else "6"}.items():
        src = src.replace(key, value)
    names = {"SHEET": "DR1 sheet z 50", "T1SHEET": "T1 top sheet z 50"} if tag == "DR1" else {}
    case(tag + " ", src, **names)

case("DR9 ", r'''
        const result = qBodyType(created(SELF), BodyType.SOLID);
        const n = count(result);
        const v = n == 1 ? evVolume(context, { "entities" : result }) : 0 * meter ^ 3;
        const faces = n == 1 ? count(qOwnedByBody(result, EntityType.FACE)) : 0;
        var vol = 0;
        const steps = 4000;
        for (var i = 0; i < steps; i += 1)
        {
            const h = -3 + 8 * (i + 0.5) / steps;
            const r = 40 + h * ''' + repr(k10) + r''';
            vol += PI * r * r * 8 / steps;
        }
        // the wall is a spline fitted through 21 points of the circle: a few mm3 of 40570 (0.01 %) is the fit, not the draft
        return [n == 1 && faces == 3 && abs(v - vol * millimeter ^ 3) < 6 * millimeter ^ 3,
            n ~ " solid (1), faces " ~ faces ~ " (3), volume " ~ roundToPrecision(v / millimeter ^ 3, 3) ~ " (" ~ roundToPrecision(vol, 3) ~ ", a cone frustum)"];''')

case("DR12 ", r'''
        const result = qBodyType(created(SELF), BodyType.SOLID);
        const n = count(result);
        const v = n == 1 ? evVolume(context, { "entities" : result }) : 0 * meter ^ 3;
        const out = embedded(SELF);
        const tw = count(out.query.towardFaces.value);
        const aw = count(out.query.awayFaces.value);
        const sd = count(out.query.sideFaces.value);
        // two 50 x 100 faces (the second 0.5 deg-ish folded down 10 mm over 50 -> 11.3 deg): about 100 x ~100.5 x 8, grown by the draft
        const ok = n == 1 && tw == 2 && aw == 2 && v > 79500 * millimeter ^ 3 && v < 84000 * millimeter ^ 3;
        return [ok, n ~ " solid (1), towardFaces " ~ tw ~ " (2), awayFaces " ~ aw ~ " (2), sideFaces " ~ sd ~ ", volume " ~ roundToPrecision(v / millimeter ^ 3, 1) ~ " (79500..84000)"];''')

case("DR13 ", r'''
        const cube = created(@CUBE@);
        const v = count(cube) == 1 ? evVolume(context, { "entities" : cube }) : 0 * meter ^ 3;
        var vol = 1000000;
        const k = ''' + repr(k10) + r''';
        const steps = 4000;
        for (var i = 0; i < steps; i += 1)
        {
            const h = 5 * (i + 0.5) / steps;
            const s = 60 + 2 * h * k;
            vol += s * s * 5 / steps;
        }
        const out = embedded(SELF);
        const toward = evaluateQuery(context, out.query.towardFaces.value);
        const z = size(toward) == 1 ? centroid(toward[0])[2] : 0 * meter;
        return [count(cube) == 1 && abs(v - vol * millimeter ^ 3) < 0.5 * millimeter ^ 3 && size(toward) == 1 && near(z, mm(55), mm(0.001)),
            count(cube) ~ " part (1), volume " ~ roundToPrecision(v / millimeter ^ 3, 3) ~ " (" ~ roundToPrecision(vol, 3) ~ "), towardFaces " ~ size(toward) ~ " (1) at z " ~ fmt(z) ~ " (55)"];''',
     CUBE="DR13 cube")

# Patch+ (2026-11-23). A region of 10 mm push or pull; k = tan(10 deg). Volumes of the box (1e6 mm3) plus the plug.
PP_SOLID_SRC = r'''
        const cube = created(@CUBE@);
        const n = count(cube);
        const v = n == 1 ? evVolume(context, { "entities" : cube }) : 0 * meter ^ 3;
        const k = __K__;
        var plug = 0;
        const steps = 2000;
        for (var i = 0; i < steps; i += 1)
        {
            const t = __DEPTH__ * (i + 0.5) / steps;
            const delta = t * k;
            plug += (__AREA__) * __DEPTH__ / steps;
        }
        const want = 1000000 + __SIGN__ * plug;
        const helpers = count(created(SELF));
        return [n == 1 && abs(v - want * millimeter ^ 3) < 80 * millimeter ^ 3 && helpers == 0,
            n ~ " solid (1), volume " ~ roundToPrecision(v / millimeter ^ 3, 2) ~ " (" ~ roundToPrecision(want, 2) ~ "), helper bodies left " ~ helpers ~ " (0)"];'''
k10 = math.tan(math.radians(10))
for tag, area, sign, k in [("PP1", "PI * 400", 1, 0), ("PP2", "PI * 400", -1, 0), ("PP4", "PI * (20 + delta) * (20 + delta)", 1, k10),
                           ("PP5", "2700", 1, 0), ("PP6", "2700 + 240 * delta + 4 * delta * delta", 1, k10), ("PP7", "PI * 400", 1, 0)]:
    src = PP_SOLID_SRC.replace("__K__", repr(k)).replace("__DEPTH__", "10").replace("__AREA__", area).replace("__SIGN__", str(sign))
    case(tag + " ", src, CUBE="%s cube" % tag)

# Patch+ with a replacement surface: a plug of height DEPTH over the circle (r 20), walls straight or drafted.
for tag, depth, sign, area in [("RP1", 20, 1, "PI * 400"), ("RP2", 25, 1, "PI * 400"), ("RP3", 10, -1, "PI * 400"),
                               ("RP5", 20, 1, "PI * (20 + delta) * (20 + delta)"), ("RP6", 20, 1, "PI * 400")]:
    k = k10 if tag == "RP5" else 0
    src = PP_SOLID_SRC.replace("__K__", repr(k)).replace("__DEPTH__", str(depth)).replace("__AREA__", area).replace("__SIGN__", str(sign))
    case(tag + " ", src, CUBE="%s cube" % tag)

case("RP4 ", r'''
        const sheet = created(@SHEET@);
        const faces = qOwnedByBody(sheet, EntityType.FACE);
        const area = count(sheet) >= 1 ? evArea(context, { "entities" : faces }) : 0 * meter ^ 2;
        const bb = count(sheet) >= 1 ? evBox3d(context, { "topology" : sheet }) : undefined;
        const zlo = bb == undefined ? 0 * meter : bb.minCorner[2];
        const zhi = bb == undefined ? 0 * meter : bb.maxCorner[2];
        const want = 10000 + 2 * PI * 20 * 15;
        return [count(sheet) == 1 && abs(area - want * millimeter ^ 2) < 3 * millimeter ^ 2 && near(zlo, mm(50), mm(0.001)) && near(zhi, mm(65), mm(0.001)),
            count(sheet) ~ " sheet (1), area " ~ roundToPrecision(area / millimeter ^ 2, 2) ~ " (" ~ roundToPrecision(want, 2) ~ "), z " ~ fmt(zlo) ~ ".." ~ fmt(zhi) ~ " (50..65)"];''',
     SHEET="RP4 sheet")

# Patch+ with a plate that CROSSES the face along a diameter of the circle (tilted 10 deg through z 50):
# sheet: area = 10000 - pi r^2 + pi r^2 / cos(10) + 4 r^2 tan(10); solid: the lens added equals the lens subtracted.
case("RP7 ", r'''
        const sheet = created(@SHEET@);
        const faces = qOwnedByBody(sheet, EntityType.FACE);
        const area = count(sheet) >= 1 ? evArea(context, { "entities" : faces }) : 0 * meter ^ 2;
        const naked = size(evaluateQuery(context, qEdgeTopologyFilter(qOwnedByBody(sheet, EntityType.EDGE), EdgeTopology.ONE_SIDED)));
        const want = 10000 - PI * 400 + PI * 400 / cos(10 * degree) + 4 * 400 * tan(10 * degree);
        return [count(sheet) == 1 && abs(area - want * millimeter ^ 2) < 6 * millimeter ^ 2 && naked == 4,
            count(sheet) ~ " sheet (1), area " ~ roundToPrecision(area / millimeter ^ 2, 2) ~ " (" ~ roundToPrecision(want, 2) ~ "), naked edges " ~ naked ~ " (4: the square's own outline, so the wall closes)"];''',
     SHEET="RP7 sheet")

case("RP8 ", r'''
        const cube = created(@CUBE@);
        const n = count(cube);
        const v = n == 1 ? evVolume(context, { "entities" : cube }) : 0 * meter ^ 3;
        const helpers = count(created(SELF));
        return [n == 1 && abs(v - 1000000 * millimeter ^ 3) < 120 * millimeter ^ 3 && helpers == 0,
            n ~ " solid (1), volume " ~ roundToPrecision(v / millimeter ^ 3, 2) ~ " (1000000: the added lens equals the subtracted one), helper bodies left " ~ helpers ~ " (0)"];''',
     CUBE="RP8 cube")

# Patch+ offset direction: toward the side reference (RF1 above the sheet, RF2 below), Flip on a solid without a reference (RF3);
# RF4 (reference in the plane of the sheet) must fail.
for tag, zlo, zhi in [("RF1", 50, 60), ("RF2", 40, 50)]:
    case(tag + " ", r'''
        const sheet = created(@SHEET@);
        const faces = qOwnedByBody(sheet, EntityType.FACE);
        const area = count(sheet) >= 1 ? evArea(context, { "entities" : faces }) : 0 * meter ^ 2;
        const bb = count(sheet) >= 1 ? evBox3d(context, { "topology" : sheet }) : undefined;
        const zlo = bb == undefined ? 0 * meter : bb.minCorner[2];
        const zhi = bb == undefined ? 0 * meter : bb.maxCorner[2];
        const want = 10000 + 2 * PI * 20 * 10;
        return [count(sheet) == 1 && abs(area - want * millimeter ^ 2) < 2 * millimeter ^ 2 && near(zlo, mm(%d), mm(0.001)) && near(zhi, mm(%d), mm(0.001)),
            count(sheet) ~ " sheet (1), area " ~ roundToPrecision(area / millimeter ^ 2, 2) ~ " (" ~ roundToPrecision(want, 2) ~ "), z " ~ fmt(zlo) ~ ".." ~ fmt(zhi) ~ " (%d..%d)"];''' % (zlo, zhi, zlo, zhi),
         SHEET="%s sheet" % tag)
case("RF3 ", PP_SOLID_SRC.replace("__K__", "0").replace("__DEPTH__", "10").replace("__AREA__", "PI * 400").replace("__SIGN__", "-1"), CUBE="RF3 cube")

# Pick stability: how many faces each internal operation of a Patch+ POCKET creates. A face a user picks on the result is keyed to
# the operation that created it: renaming an op id or reordering a union changes these counts and breaks such picks silently.
PS_OPS = ["copy", "imprint", "offset", "tubeCopy", "baseSurface", "shell", "enclose", "apply"]
PS_BASELINE = "copy=0 imprint=0 offset=0 tubeCopy=0 baseSurface=0 shell=0 enclose=0 apply=2 "   # the first run, 2026-11-23: only apply creates faces that survive (wall + floor of the pocket); a rename of apply shows as apply=0
case("PP2 ", r'''
        var counts = "";
        for (var op in %s)
        {
            counts ~= op ~ "=" ~ size(evaluateQuery(context, qCreatedBy(makeId(SELF) + op, EntityType.FACE))) ~ " ";
        }
        const baseline = %s;
        return [baseline == undefined || counts == baseline, "pick stability, faces created per internal operation: " ~ counts ~ (baseline == undefined ? "(no baseline yet)" : " (baseline " ~ baseline ~ ")")];''' % (
    json.dumps(PS_OPS).replace("[", "[").replace("]", "]"), "undefined" if PS_BASELINE is None else json.dumps(PS_BASELINE)))

# Patch+ with footprint FACES: boss +10 over the footprint area (volume = 1e6 + area * 10); FP4 must fail; FP5 on a sheet.
for tag, area in [("FP1", "1600"), ("FP2", "900 + 400"), ("FP3", "3600 - PI * 100")]:
    src = PP_SOLID_SRC.replace("__K__", "0").replace("__DEPTH__", "10").replace("__AREA__", area).replace("__SIGN__", "1")
    case(tag + " ", src, CUBE="%s cube" % tag)

# Footprint faces with a replacement plate (FP6), a drafted island (FP8) and a drafted ring (FP9; its hole closes in, so the area is
# (60 + 2 d)^2 - pi (10 - d)^2 with d = t tan(10 deg)); FP7: a sheet with the side reference below.
case("FP4 ", PP_SOLID_SRC.replace("__K__", "0").replace("__DEPTH__", "10").replace("__AREA__", "100 * 40").replace("__SIGN__", "1"), CUBE="FP4 cube")
case("FP10 ", r'''
        const sheet = created(@SHEET@);
        const area = count(sheet) >= 1 ? evArea(context, { "entities" : qOwnedByBody(sheet, EntityType.FACE) }) : 0 * meter ^ 2;
        const bb = count(sheet) >= 1 ? evBox3d(context, { "topology" : sheet }) : undefined;
        const zlo = bb == undefined ? 0 * meter : bb.minCorner[2];
        const zhi = bb == undefined ? 0 * meter : bb.maxCorner[2];
        const want = 10000 + 2 * 100 * 10;
        const high = near(zlo, mm(50), mm(0.001)) && near(zhi, mm(60), mm(0.001));
        const low = near(zlo, mm(40), mm(0.001)) && near(zhi, mm(50), mm(0.001));
        return [count(sheet) == 1 && abs(area - want * millimeter ^ 2) < 3 * millimeter ^ 2 && (high || low),
            count(sheet) ~ " sheet (1), area " ~ roundToPrecision(area / millimeter ^ 2, 2) ~ " (" ~ roundToPrecision(want, 2) ~ "), z " ~ fmt(zlo) ~ ".." ~ fmt(zhi) ~ " (50..60 or 40..50)"];''',
     SHEET="FP10 sheet")
case("FP6 ", PP_SOLID_SRC.replace("__K__", "0").replace("__DEPTH__", "20").replace("__AREA__", "1600").replace("__SIGN__", "1"), CUBE="FP6 cube")
case("FP8 ", PP_SOLID_SRC.replace("__K__", repr(k10)).replace("__DEPTH__", "10").replace("__AREA__", "(40 + 2 * delta) * (40 + 2 * delta)").replace("__SIGN__", "1"), CUBE="FP8 cube")
case("FP9 ", PP_SOLID_SRC.replace("__K__", repr(k10)).replace("__DEPTH__", "10").replace("__AREA__", "(60 + 2 * delta) * (60 + 2 * delta) - PI * (10 - delta) * (10 - delta)").replace("__SIGN__", "1"), CUBE="FP9 cube")
case("FP7 ", r'''
        const sheet = created(@SHEET@);
        const faces = qOwnedByBody(sheet, EntityType.FACE);
        const area = count(sheet) >= 1 ? evArea(context, { "entities" : faces }) : 0 * meter ^ 2;
        const bb = count(sheet) >= 1 ? evBox3d(context, { "topology" : sheet }) : undefined;
        const zlo = bb == undefined ? 0 * meter : bb.minCorner[2];
        const zhi = bb == undefined ? 0 * meter : bb.maxCorner[2];
        const want = 10000 + 4 * 40 * 10;
        const naked = size(evaluateQuery(context, qEdgeTopologyFilter(qOwnedByBody(sheet, EntityType.EDGE), EdgeTopology.ONE_SIDED)));
        return [count(sheet) == 1 && abs(area - want * millimeter ^ 2) < 3 * millimeter ^ 2 && near(zlo, mm(40), mm(0.001)) && near(zhi, mm(50), mm(0.001)) && naked == 4,
            count(sheet) ~ " sheet (1), area " ~ roundToPrecision(area / millimeter ^ 2, 2) ~ " (" ~ roundToPrecision(want, 2) ~ "), z " ~ fmt(zlo) ~ ".." ~ fmt(zhi) ~ " (40..50), naked edges " ~ naked ~ " (4)"];''',
     SHEET="FP7 sheet")

case("FP5 ", r'''
        const sheet = created(@SHEET@);
        const faces = qOwnedByBody(sheet, EntityType.FACE);
        const area = count(sheet) >= 1 ? evArea(context, { "entities" : faces }) : 0 * meter ^ 2;
        const bb = count(sheet) >= 1 ? evBox3d(context, { "topology" : sheet }) : undefined;
        const zlo = bb == undefined ? 0 * meter : bb.minCorner[2];
        const zhi = bb == undefined ? 0 * meter : bb.maxCorner[2];
        const want = 10000 + 4 * 40 * 10;
        const high = near(zlo, mm(50), mm(0.001)) && near(zhi, mm(60), mm(0.001));
        const low = near(zlo, mm(40), mm(0.001)) && near(zhi, mm(50), mm(0.001));
        const naked = size(evaluateQuery(context, qEdgeTopologyFilter(qOwnedByBody(sheet, EntityType.EDGE), EdgeTopology.ONE_SIDED)));
        return [count(sheet) == 1 && abs(area - want * millimeter ^ 2) < 3 * millimeter ^ 2 && (high || low) && naked == 4,
            count(sheet) ~ " sheet (1), area " ~ roundToPrecision(area / millimeter ^ 2, 2) ~ " (" ~ roundToPrecision(want, 2) ~ "), z " ~ fmt(zlo) ~ ".." ~ fmt(zhi) ~ " (50..60 or 40..50), naked edges " ~ naked ~ " (4)"];''',
     SHEET="FP5 sheet")

case("PP3 ", r'''
        const sheet = created(@SHEET@);
        const faces = qOwnedByBody(sheet, EntityType.FACE);
        const area = count(sheet) >= 1 ? evArea(context, { "entities" : faces }) : 0 * meter ^ 2;
        const bb = count(sheet) >= 1 ? evBox3d(context, { "topology" : sheet }) : undefined;
        const zlo = bb == undefined ? 0 * meter : bb.minCorner[2];
        const zhi = bb == undefined ? 0 * meter : bb.maxCorner[2];
        const want = 10000 + 2 * PI * 20 * 10;
        const high = near(zlo, mm(50), mm(0.001)) && near(zhi, mm(60), mm(0.001));
        const low = near(zlo, mm(40), mm(0.001)) && near(zhi, mm(50), mm(0.001));
        const naked = size(evaluateQuery(context, qEdgeTopologyFilter(qOwnedByBody(sheet, EntityType.EDGE), EdgeTopology.ONE_SIDED)));
        return [count(sheet) == 1 && abs(area - want * millimeter ^ 2) < 2 * millimeter ^ 2 && (high || low) && naked == 4,
            count(sheet) ~ " sheet (1), area " ~ roundToPrecision(area / millimeter ^ 2, 2) ~ " (" ~ roundToPrecision(want, 2) ~ "), z " ~ fmt(zlo) ~ ".." ~ fmt(zhi) ~ " (50..60 or 40..50), naked edges " ~ naked ~ " (4: the square's own outline)"];''',
     SHEET="PP3 sheet")

case("DR11 ", r'''
        const sheets = evaluateQuery(context, qBodyType(created(SELF), BodyType.SHEET));
        const solids = evaluateQuery(context, qBodyType(created(SELF), BodyType.SOLID));
        const faces = count(qOwnedByBody(created(SELF), EntityType.FACE));
        return [size(sheets) == 1 && size(solids) == 0 && faces == 4, size(sheets) ~ " sheet (1), " ~ size(solids) ~ " solid (0), faces " ~ faces ~ " (4 drafted walls)"];''')

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

case("T7 ", r'''
        const out = embedded(SELF);
        const bodies = evaluateQuery(context, out.query.output.value);
        const v = size(bodies) == 1 ? evVolume(context, { "entities" : bodies[0] }) : 0 * meter ^ 3;
        return [size(bodies) == 1 && abs(v - 982000 * millimeter ^ 3) < 0.01 * millimeter ^ 3,
            "output " ~ size(bodies) ~ " body (1), volume " ~ roundToPrecision(v / millimeter ^ 3, 2) ~ " (982000)"];''')

case("F6 ", r'''
        const out = embedded(SELF);
        const n = count(out.query.output.value);
        const b = count(out.query.boundaryEdges.value);
        return [n == 1 && b == 4, "output " ~ n ~ " face (1), boundaryEdges " ~ b ~ " (4)"];''')

# Enclose+ (2026-09-27)
for tag, want_faces, x_range in (("E1", 6, None), ("E2", 6, (18350, 18450)), ("E3", 6, None)):
    xr = "true" if x_range is None else "near(bb.minCorner[0], mm(%g), mm(0.01)) && near(bb.maxCorner[0], mm(%g), mm(0.01))" % x_range
    case(tag + " ", r'''
        const out = embedded(SELF);
        const bodies = evaluateQuery(context, out.query.output.value);
        if (size(bodies) != 1) { return [false, size(bodies) ~ " parts (1)"]; }
        const v = evVolume(context, { "entities" : bodies[0] });
        const faces = count(qOwnedByBody(bodies[0], EntityType.FACE));
        const caps = count(out.query.capFaces.value);
        const sides = count(out.query.surfaceFaces.value);
        const bb = evBox3d(context, { "topology" : bodies[0] });
        const ok = abs(v - 400000 * millimeter ^ 3) < 0.01 * millimeter ^ 3 && faces == %d && caps == 2 && sides == 4 && %s;
        return [ok, "volume " ~ roundToPrecision(v / millimeter ^ 3, 1) ~ " (400000), faces " ~ faces ~ " (%d), capFaces " ~ caps ~ " (2), surfaceFaces " ~ sides ~ " (4), x "
            ~ fmt(bb.minCorner[0]) ~ ".." ~ fmt(bb.maxCorner[0])];''' % (want_faces, xr, want_faces))

# Join profile surfaces (2026-09-27)
NO_END = ["endOutside", "endLoft", "endInsideEdge", "endOutsideEdge"]
NO_START = ["startOutside", "startLoft", "startInsideEdge", "startOutsideEdge"]
for tag, area, faces, absent in (("J1", 24000, 5, []), ("J2", 25000, 5, []), ("J3", 25000, 5, []), ("J4", None, 6, []),
                                 ("J5", 22000, 3, NO_END), ("J6", 22000, 3, NO_START), ("J7", 22000, 3, NO_END), ("J8", 22000, 3, NO_START), ("K1", 22000, 5, NO_END)):
    case(tag + " ", r'''
        const out = embedded(SELF);
        const bodies = evaluateQuery(context, out.query.output.value);
        if (size(bodies) != 1) { return [false, size(bodies) ~ " bodies (1)"]; }
        const faces = count(qOwnedByBody(bodies[0], EntityType.FACE));
        const area = evArea(context, { "entities" : qOwnedByBody(bodies[0], EntityType.FACE) });
        var keys = "";
        var keysOk = true;
        for (var k in ["insideSurface", "startOutside", "endOutside", "startLoft", "endLoft", "startInsideEdge", "startOutsideEdge", "endInsideEdge", "endOutsideEdge"])
        {
            const n = count(out.query[k].value);
            keysOk = keysOk && (isIn(k, ABSENT) ? n == 0 : n >= 1);
            keys ~= k ~ " " ~ n ~ ", ";
        }
        // the strip has ONE face (K1: a 3-edge cut against a 1-edge cut must not make three faces)
        const oneLoft = (count(out.query.startLoft.value) <= 1) && (count(out.query.endLoft.value) <= 1);
        keysOk = keysOk && oneLoft;
        keys ~= "loft faces start " ~ count(out.query.startLoft.value) ~ " end " ~ count(out.query.endLoft.value) ~ ", ";
        const sq = millimeter * millimeter;
        const areaOk = %s;
        return [faces == %d && areaOk && keysOk, "faces " ~ faces ~ " (%d), area " ~ roundToPrecision(area / sq, 2) ~ " (%s); " ~ keys];'''.replace("ABSENT", json.dumps(absent)) % (
        "true" if area is None else "abs(area - %d * sq) < 0.01 * sq" % area, faces, faces, "any" if area is None else area))

# Referenced part (2026-09-28): volume, x range (or None), face counts per key.
for tag, vol, x_range, keys in (
        ("P1", 100000, None, {"profileFaces": 1, "oppositeFaces": 1, "sideFaces": 0}),
        ("P2", 60000, None, {"profileFaces": 1, "oppositeFaces": 1}),
        ("P3", 10000, (24175, 24225), {"profileFaces": 1, "oppositeFaces": 1, "sideFaces": 4}),
        ("P4", 26400, (24745, 24855), {"profileFaces": 1, "oppositeFaces": 1, "sideFaces": 2, "cap1Faces": 2}),
        ("P5", 17500, (25375, 25425), {"topFaces": 1, "bottomFaces": 1, "sideFaces": 4}),
        ("P6", 12000, (25990, 26010), {"topFaces": 1, "bottomFaces": 1, "sideFaces": 2, "cap1Faces": 1, "cap2Faces": 1}),
        ("P7", 48000, (26560, 26640), {"topFaces": 1, "bottomFaces": 1, "sideFaces": 2, "cap1Faces": 1, "cap2Faces": 1}),
        ("P9", 26400, (27945, 28055), {"profileFaces": 1, "oppositeFaces": 1, "sideFaces": 2, "cap1Faces": 2}),
        ("P10", 26400, (28545, 28655), {"profileFaces": 1, "oppositeFaces": 1, "sideFaces": 2, "cap1Faces": 2}),
        ("P11", 26400, (29145, 29255), {"profileFaces": 1, "oppositeFaces": 1, "sideFaces": 2, "cap1Faces": 2})):
    xr = "true" if x_range is None else "near(bb.minCorner[0], mm(%g), mm(0.01)) && near(bb.maxCorner[0], mm(%g), mm(0.01))" % x_range
    key_src = "".join('\n        n = count(out.query.%s.value); keysOk = keysOk && n == %d; keys ~= "%s " ~ n ~ " (%d), ";' % (k, n, k, n) for k, n in keys.items())
    case(tag + " ", r"""
        const out = embedded(SELF);
        const bodies = evaluateQuery(context, out.query.output.value);
        if (size(bodies) != 1) { return [false, size(bodies) ~ " parts (1)"]; }
        const v = evVolume(context, { "entities" : bodies[0] });
        const bb = evBox3d(context, { "topology" : bodies[0] });
        var keysOk = true;
        var keys = "";
        var n = 0;%s
        const ok = abs(v - %d * millimeter ^ 3) < 0.01 * millimeter ^ 3 && %s && keysOk;
        return [ok, "volume " ~ roundToPrecision(v / millimeter ^ 3, 2) ~ " (%d), x " ~ fmt(bb.minCorner[0]) ~ ".." ~ fmt(bb.maxCorner[0]) ~ ", z "
            ~ fmt(bb.minCorner[2]) ~ ".." ~ fmt(bb.maxCorner[2]) ~ "; " ~ keys];""" % (key_src, vol, xr, vol))

EXPECTED_ERRORS = ["T4 Thicken+", "DR10 Thicken+", "RF4 Patch+", "R4 Orient to reference", "E4 Enclose+", "P8 Referenced part", "J9 Join profile surfaces"]


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
                 and any(k in n for k in ["Split+", "Offset+", "Mutual Trim+", "Thicken+", "Patch+", "Orient to reference", "Move face+", "Enclose+", "Join profile surfaces", "Referenced part"])]
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
