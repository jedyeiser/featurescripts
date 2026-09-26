"""Check the gordonSurface regression cases in the "Gordon tools tests" Part Studio (built by build_gordon_tests.py).
Each case is measured through the FeatureScript eval API and printed PASS / FAIL with its measurements; every
feature's status is checked too (all must be OK).

Recorded per Modify curve end case: modified-end position error vs the To point, end tangent (evEdgeTangentLine),
end curvature (evEdgeCurvature), fixed-end position / tangent angle / curvature vs the source curve, the deviation
of the part of the curve next to the fixed end (first 20 % of the output, distance to the source), created bodies.
Scaled Curve: body count, degree, distance to the expected curve. Pull surface: body count, the new sheet's
boundary distance to the source face's boundary and the normal angle between the two faces along the WHOLE
boundary edges, and how far the interior was pulled.

EXPECT_FAIL lists the cases that document today's bugs (reviews/2026-09-25_tools_review/curves.md): they print
XFAIL and count as passing while the bug is there. Once a fix makes one pass it prints XPASS and counts as a
failure until it is removed from EXPECT_FAIL, so the list stays honest.

usage (repo root): PYTHONPATH=. python devtools/onshape/check_gordon_tests.py
"""
import json
import re
import sys

from sync.core.client import OnshapeClient

c = OnshapeClient()
D = "d0950ed72a894fbe35c27d43"  # gordonSurface has no .document.json
W = c.get(f"/api/v10/documents/{D}")["defaultWorkspace"]["id"]
STUDIO = "Gordon tools tests"
E = [e["id"] for e in c.list_elements(D, W) if e["name"] == STUDIO and e["elementType"] == "PARTSTUDIO"][0]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"

HELPERS = r'''
    const pt = function(x, y, z) { return vector(x, y, z) * millimeter; };
    const fmt = function(v) { return toString(roundToPrecision(v / millimeter, 5)); };
    const num = function(x) { return toString(roundToPrecision(x, 6)); };
    const dirS = function(d) { return "(" ~ num(d[0]) ~ ", " ~ num(d[1]) ~ ", " ~ num(d[2]) ~ ")"; };
    const count = function(q) { return size(evaluateQuery(context, q)); };
    const bodies = function(fid) { return qCreatedBy(makeId(fid), EntityType.BODY); };
    const wireEdge = function(fid) { return qOwnedByBody(qBodyType(bodies(fid), BodyType.WIRE), EntityType.EDGE); };
    const sketchEdge = function(fid) { return qConstructionFilter(qCreatedBy(makeId(fid), EntityType.EDGE), ConstructionObject.NO); };
    const toPoint = function(fid) { return evVertexPoint(context, { "vertex" : qNthElement(qCreatedBy(makeId(fid), EntityType.VERTEX), 0) }); };
    const cylinder = function(fid) { return qGeometry(qCreatedBy(makeId(fid), EntityType.FACE), GeometryType.CYLINDER); };
    const tangentAt = function(e, t) { return evEdgeTangentLine(context, { "edge" : e, "parameter" : t }); };
    const curvAt = function(e, t) { return evEdgeCurvature(context, { "edge" : e, "parameter" : t }); };
    const dist = function(q, p) { return evDistance(context, { "side0" : q, "side1" : p }).distance; };
    // Unsigned angle between two lines (tangent orientation depends on edge direction).
    const angleBetween = function(a, b) { return acos(clamp(abs(dot(normalize(a), normalize(b))), 0, 1)) / radian; };
    // Signed angle between two directions (curvature normals).
    const angleSigned = function(a, b) { return acos(clamp(dot(normalize(a), normalize(b)), -1, 1)) / radian; };
    const faceNormalAt = function(f, p)
    {
        const d = evDistance(context, { "side0" : f, "side1" : p });
        return evFaceTangentPlane(context, { "face" : f, "parameter" : d.sides[0].parameter }).normal;
    };
    // Relative curvature error, floored at 1/m so a zero target does not divide by zero.
    const kRel = function(k, ref) { return abs(k - ref) / max(abs(ref), 1); };

    // Modify curve end: measure the output wire of feature outFid against the source sketch curve srcFid.
    const mceReport = function(outFid, srcFid, toP) returns map
    {
        const e = wireEdge(outFid);
        const src = sketchEdge(srcFid);
        const n = count(e);
        if (n != 1)
        {
            return { "ok" : false, "text" : n ~ " output edges, " ~ count(bodies(outFid)) ~ " bodies" };
        }
        const a0 = tangentAt(e, 0);
        const a1 = tangentAt(e, 1);
        const tm = norm(a0.origin - toP) < norm(a1.origin - toP) ? 0 : 1;
        const tf = 1 - tm;
        const modL = tm == 0 ? a0 : a1;
        const fixL = tf == 0 ? a0 : a1;
        const s0 = tangentAt(src, 0);
        const s1 = tangentAt(src, 1);
        const sf = norm(s0.origin - fixL.origin) < norm(s1.origin - fixL.origin) ? 0 : 1;
        const srcFix = sf == 0 ? s0 : s1;
        const modC = curvAt(e, tm);
        const fixC = curvAt(e, tf);
        const srcC = curvAt(src, sf);
        var dev = 0 * meter;
        for (var i = 0; i <= 10; i += 1)
        {
            const t = tf == 0 ? 0.02 * i : 1 - 0.02 * i;
            dev = max(dev, dist(src, tangentAt(e, t).origin));
        }
        return { "ok" : true, "edge" : e, "modP" : modL.origin, "modErr" : norm(modL.origin - toP), "fixErr" : norm(fixL.origin - srcFix.origin),
            "modT" : modL.direction, "modK" : modC.curvature * meter, "modN" : modC.frame.xAxis,
            "fixAngle" : angleBetween(fixL.direction, srcFix.direction), "fixK" : fixC.curvature * meter, "srcFixK" : srcC.curvature * meter,
            "fixNAngle" : angleSigned(fixC.frame.xAxis, srcC.frame.xAxis), "dev" : dev, "bodies" : count(bodies(outFid)) };
    };
    const mceText = function(r) returns string
    {
        if (!r.ok)
        {
            return r.text;
        }
        return "mod end err " ~ fmt(r.modErr) ~ " mm, tangent " ~ dirS(r.modT) ~ ", k " ~ num(r.modK) ~ "/m; fixed end err " ~ fmt(r.fixErr)
            ~ " mm, tangent angle " ~ num(r.fixAngle) ~ " rad, k " ~ num(r.fixK) ~ "/m (source " ~ num(r.srcFixK) ~ "/m, normal angle "
            ~ num(r.fixNAngle) ~ " rad); dev next to fixed end " ~ fmt(r.dev) ~ " mm; " ~ r.bodies ~ " bodies";
    };
    const basic = function(r) { return r.ok && r.modErr < 0.001 * millimeter && r.fixErr < 0.001 * millimeter && r.bodies == 1; };

    // MCE rework helpers.
    const embedded = function(fid) { return getVariable(context, toString(makeId(fid))); };
    const mcFrame = function(fid) { return evMateConnector(context, { "mateConnector" : qBodyType(bodies(fid), BodyType.MATE_CONNECTOR) }); };
    const nWires = function(fid) { return count(qBodyType(bodies(fid), BodyType.WIRE)); };
    const nPoints = function(fid) { return count(qBodyType(bodies(fid), BodyType.POINT)); };
    // Tangent angle / curvature ratio between the output (at its point nearest p) and a reference edge (at its point nearest p).
    const refMatch = function(out, refEdge, p) returns map
    {
        const to = evDistance(context, { "side0" : out, "side1" : p }).sides[0].parameter;
        const tr = evDistance(context, { "side0" : refEdge, "side1" : p }).sides[0].parameter;
        const co = curvAt(out, to);
        const cr = curvAt(refEdge, tr);
        return { "angle" : angleBetween(co.frame.zAxis, cr.frame.zAxis), "k" : co.curvature * meter, "refK" : cr.curvature * meter,
            "nAngle" : angleSigned(co.frame.xAxis, cr.frame.xAxis) };
    };
    // Held region: source points from source parameter 0 (the fixed end) to th, distance to the output edge.
    const heldDev = function(out, src, th) returns ValueWithUnits
    {
        var dev = 0 * meter;
        for (var i = 0; i <= 40; i += 1)
        {
            dev = max(dev, dist(out, tangentAt(src, th * i / 40).origin));
        }
        return dev;
    };
    // Both ends of the only output edge, against an expected modified end and fixed end.
    const endsReport = function(outFid, modP, fixP) returns map
    {
        const e = wireEdge(outFid);
        const n = count(e);
        if (n != 1)
        {
            return { "ok" : false, "text" : n ~ " output edges, " ~ count(bodies(outFid)) ~ " bodies" };
        }
        const p0 = tangentAt(e, 0).origin;
        const p1 = tangentAt(e, 1).origin;
        const modErr = min(norm(p0 - modP), norm(p1 - modP));
        const fixErr = min(norm(p0 - fixP), norm(p1 - fixP));
        return { "ok" : true, "edge" : e, "modErr" : modErr, "fixErr" : fixErr,
            "text" : "mod end err " ~ fmt(modErr) ~ " mm, fixed end err " ~ fmt(fixErr) ~ " mm; " ~ count(bodies(outFid)) ~ " bodies" };
    };
    // Embedded keys movedVertex / holdVertex: vertex count and position.
    const keyAt = function(fid, key, p) returns map
    {
        const out = embedded(fid);
        if (out == undefined || out.query == undefined || out.query[key] == undefined)
        {
            return { "ok" : false, "text" : key ~ " missing" };
        }
        const q = out.query[key].value;
        const n = count(q);
        const err = n == 1 ? norm(evVertexPoint(context, { "vertex" : q }) - p) : 1 * meter;
        return { "ok" : n == 1 && err < 0.001 * millimeter, "text" : key ~ " " ~ n ~ " vertex, err " ~ fmt(err) ~ " mm" };
    };

    // Pull surface: the new sheet of outFid against the source face.
    const pullReport = function(outFid, face) returns map
    {
        const sheet = qBodyType(bodies(outFid), BodyType.SHEET);
        const nSheets = count(sheet);
        if (nSheets != 1)
        {
            return { "ok" : false, "text" : nSheets ~ " sheet bodies (" ~ count(bodies(outFid)) ~ " bodies)" };
        }
        const newFace = qOwnedByBody(sheet, EntityType.FACE);
        const oldEdges = qAdjacent(face, AdjacencyType.EDGE, EntityType.EDGE);
        var g0 = 0 * meter;
        var g1 = 0;
        for (var e in evaluateQuery(context, qOwnedByBody(sheet, EntityType.EDGE)))
        {
            for (var t in range(0, 1, 25))
            {
                const p = tangentAt(e, t).origin;
                g0 = max(g0, dist(oldEdges, p));
                g1 = max(g1, angleBetween(faceNormalAt(newFace, p), faceNormalAt(face, p)));
            }
        }
        var pulled = 0 * meter;
        for (var u in range(0.1, 0.9, 9))
        {
            for (var v in range(0.1, 0.9, 9))
            {
                pulled = max(pulled, dist(face, evFaceTangentPlane(context, { "face" : newFace, "parameter" : vector(u, v) }).origin));
            }
        }
        return { "ok" : true, "g0" : g0, "g1" : g1, "pulled" : pulled, "bodies" : count(bodies(outFid)),
            "text" : "1 sheet (" ~ count(bodies(outFid)) ~ " bodies); boundary vs source boundary max " ~ fmt(g0) ~ " mm; normal angle along boundary max "
                ~ num(g1) ~ " rad; interior pulled " ~ fmt(pulled) ~ " mm" };
    };
'''

CASES = []


def case(prefix, body, **names):
    """names: placeholder -> exact fixture feature name, or 'case:<prefix>' for another case's feature."""
    CASES.append((prefix, body, names))


# ---- Modify curve end ----
MCE_HEAD = r'''
        const r = mceReport(@SELF@, @SRC@, toPoint(@TO@));'''


def mce(prefix, body, **names):
    tag = prefix.strip()
    case(prefix, MCE_HEAD + body, SRC=tag + " curve", TO=tag + " to point", **names)


mce("T01 ", r'''
        return [basic(r), mceText(r)];''')

mce("T02 ", r'''
        var dmax = 1 * meter;
        if (r.ok)
        {
            dmax = 0 * meter;
            for (var i = 0; i <= 20; i += 1)
            {
                dmax = max(dmax, dist(wireEdge(@T01@), tangentAt(r.edge, i / 20).origin - pt(1000, 0, 0)));
            }
        }
        return [basic(r) && dmax < 0.001 * millimeter, mceText(r) ~ "; max distance to T01 (shifted) " ~ fmt(dmax) ~ " mm"];''',
    T01="case:T01 ")

mce("T03 ", r'''
        return [basic(r) && r.fixAngle < 1e-4, mceText(r)];''')

G2_FIXED = r'''
        const ok = basic(r) && r.fixAngle < 1e-4 && kRel(r.fixK, r.srcFixK) < 1e-3 && (abs(r.srcFixK) < 0.1 || r.fixNAngle < 1e-3);
        return [ok, mceText(r) ~ (r.ok ? "; dk/k " ~ num(kRel(r.fixK, r.srcFixK)) : "")];'''
mce("T04 ", G2_FIXED)
mce("T05 ", G2_FIXED)

mce("T06 ", r'''
        const lineDir = tangentAt(sketchEdge(@REF@), 0).direction;
        const a = r.ok ? angleBetween(r.modT, lineDir) : 1;
        return [basic(r) && a < 1e-4, mceText(r) ~ "; end tangent vs reference line " ~ num(a) ~ " rad"];''',
    REF="T06 reference line")

mce("T07 ", r'''
        var off = 1;
        if (r.ok)
        {
            off = abs(dot(r.modT, faceNormalAt(cylinder(@REF@), r.modP)));
        }
        return [basic(r) && off < 1e-4, mceText(r) ~ "; |end tangent . face normal| " ~ num(off)];''',
    REF="T07 reference cylinder")

mce("T08 ", r'''
        var onFace = 1 * meter;
        if (r.ok)
        {
            onFace = 0 * meter;
            for (var i = 0; i <= 40; i += 1)
            {
                onFace = max(onFace, dist(cylinder(@REF@), tangentAt(r.edge, i / 40).origin));
            }
        }
        return [r.ok && r.bodies == 1 && onFace < 0.02 * millimeter, mceText(r) ~ "; max distance to the face " ~ fmt(onFace) ~ " mm (splineTol 0.01)"];''',
    REF="T08 projection cylinder")

mce("T09 ", r'''
        const r1 = mceReport(@T01@, @T01SRC@, toPoint(@T01TO@));
        const lineDir = tangentAt(sketchEdge(@REF@), 0).direction;
        var a1 = 1;
        var aL = 1;
        if (r.ok && r1.ok)
        {
            a1 = angleBetween(r.modT, r1.modT);
            aL = angleBetween(r.modT, lineDir);
        }
        return [basic(r) && r1.ok && a1 < 1e-4, mceText(r) ~ "; end tangent vs T01 " ~ num(a1) ~ " rad, vs the stored (unchecked) reference line " ~ num(aL) ~ " rad"];''',
    T01="case:T01 ", T01SRC="T01 curve", T01TO="T01 to point", REF="T09 reference line")

# ---- Modify curve end rework: references ----
mce("T10 ", r'''
        var m = { "angle" : 1, "k" : 0, "refK" : 1, "nAngle" : 1 };
        if (r.ok)
        {
            m = refMatch(r.edge, sketchEdge(@REF@), r.modP);
        }
        const ok = basic(r) && m.angle < 1e-4 && kRel(m.k, m.refK) < 1e-3 && m.nAngle < 1e-3 && abs(m.refK - 40) < 0.01;
        return [ok, mceText(r) ~ "; vs arc: tangent " ~ num(m.angle) ~ " rad, k " ~ num(m.k) ~ " / " ~ num(m.refK) ~ " /m, normal "
            ~ num(m.nAngle) ~ " rad"];''', REF="T10 reference arc")

mce("T11 ", r'''
        const z = mcFrame(@MC@).zAxis;
        const a = r.ok ? angleBetween(r.modT, z) : 1;
        return [basic(r) && a < 1e-4, mceText(r) ~ "; end tangent vs connector Z " ~ num(a) ~ " rad"];''', MC="T11 reference connector")

mce("T12 ", r'''
        const r11 = mceReport(@T11@, @T11SRC@, toPoint(@T11TO@));
        const z = mcFrame(@MC@).zAxis;
        var a = 1;
        var reversedVsT11 = false;
        if (r.ok && r11.ok)
        {
            a = angleBetween(r.modT, z);
            reversedVsT11 = dot(r.modT, r11.modT) < -0.9999;
        }
        return [basic(r) && a < 1e-4 && reversedVsT11, mceText(r) ~ "; end tangent vs connector Z " ~ num(a) ~ " rad, reversed vs T11 "
            ~ reversedVsT11];''',
    MC="T12 reference connector", T11="case:T11 ", T11SRC="T11 curve", T11TO="T11 to point")

mce("T13 ", r'''
        const f = mcFrame(@MC@);
        var a = 1;
        var nA = 1;
        if (r.ok)
        {
            a = angleBetween(r.modT, f.zAxis);
            nA = angleSigned(r.modN, f.xAxis);
        }
        const ok = basic(r) && a < 1e-4 && abs(r.modK - 20) / 20 < 1e-3 && nA < 1e-3;
        return [ok, mceText(r) ~ "; tangent vs Z " ~ num(a) ~ " rad, normal vs X " ~ num(nA) ~ " rad (k expected 20/m)"];''',
    MC="T13 reference connector")

mce("T14 ", r'''
        const lineDir = tangentAt(sketchEdge(@REF@), 0).direction;
        const a = r.ok ? angleBetween(r.modT, lineDir) : 1;
        return [basic(r) && a < 1e-4 && abs(r.modK) < 1e-3, mceText(r) ~ "; tangent vs line " ~ num(a) ~ " rad (k expected 0)"];''',
    REF="T14 reference line")

REF_LINE_G1 = r'''
        const lineDir = tangentAt(sketchEdge(@REF@), 0).direction;
        const a = r.ok ? angleBetween(r.modT, lineDir) : 1;
        return [basic(r) && a < 1e-4, mceText(r) ~ "; end tangent vs reference line " ~ num(a) ~ " rad"];'''
# T15 / T16: 'to point' is the EXPECTED end (not passed to the feature).
mce("T15 ", REF_LINE_G1, REF="T15 reference line")
mce("T16 ", r'''
        return [basic(r), mceText(r) ~ " (expected end: snapped onto the line at x + 210)"];''')
mce("T17 ", REF_LINE_G1, REF="T17 reference line")

# ---- Modify curve end rework: wires (fixed end of W3 at (x0, 0), To at (x0 + 200, 130)) ----
case("T20 ", r'''
        const r = endsReport(@SELF@, toPoint(@TO@), pt(30000, 0, 0));
        return [r.ok && r.modErr < 0.001 * millimeter && r.fixErr < 0.001 * millimeter, r.text];''', TO="T20 to point")

case("T21 ", r'''
        const r = endsReport(@SELF@, toPoint(@TO@), pt(31000, 0, 0));
        var d = 1 * meter;
        if (r.ok)
        {
            d = 0 * meter;
            for (var i = 0; i <= 40; i += 1)
            {
                d = max(d, dist(wireEdge(@T20@), tangentAt(r.edge, i / 40).origin - pt(1000, 0, 0)));
            }
        }
        return [r.ok && r.modErr < 0.001 * millimeter && r.fixErr < 0.001 * millimeter && d < 0.02 * millimeter,
            r.text ~ "; max distance to T20 (shifted) " ~ fmt(d) ~ " mm"];''', TO="T21 to point", T20="case:T20 ")

case("T22 ", r'''
        const r = endsReport(@SELF@, toPoint(@TO@), pt(32000, 0, 0));
        return [r.ok && r.modErr < 0.001 * millimeter, r.text ~ " (either chain end may be the moved one)"];''', TO="T22 to point")

ERROR_CASE = r'''
        const n = count(bodies(@SELF@));
        return [n == 0, n ~ " bodies (expected none: the feature fails)"];'''
case("T23 ", ERROR_CASE)
case("T24 ", ERROR_CASE)

# ---- Modify curve end rework: holds ----
HOLD_CHECK = r'''
        const holdP = toPoint(@HOLD@);
        const src = sketchEdge(@SRC@);
        var dev = 1 * meter;
        var m = { "angle" : 1, "k" : 0, "refK" : 1, "nAngle" : 1 };
        if (r.ok)
        {
            dev = heldDev(r.edge, src, evDistance(context, { "side0" : src, "side1" : holdP }).sides[0].parameter);
            m = refMatch(r.edge, src, holdP);
        }
        const kv = keyAt(@SELF@, "movedVertex", toPoint(@TO@));
        const kh = keyAt(@SELF@, "holdVertex", holdP);
        const g2 = @G2@;
        const ok = r.ok && r.modErr < 0.001 * millimeter && nWires(@SELF@) == 1 && nPoints(@SELF@) == 1 && dev < 0.001 * millimeter
            && m.angle < 1e-4 && (!g2 || kRel(m.k, m.refK) < 1e-3) && kv.ok && kh.ok;
        return [ok, mceText(r) ~ "; held part max deviation " ~ fmt(dev) ~ " mm; at the hold: tangent " ~ num(m.angle) ~ " rad, k "
            ~ num(m.k) ~ " / " ~ num(m.refK) ~ " /m; " ~ kv.text ~ "; " ~ kh.text];'''


def hold(prefix, g2):
    tag = prefix.strip()
    mce(prefix, HOLD_CHECK.replace("@G2@", "true" if g2 else "false"), HOLD=tag + " hold point")


hold("T30 ", False)
hold("T31 ", True)
hold("T34 ", True)

case("T32 ", r'''
        const r = endsReport(@SELF@, toPoint(@TO@), pt(42000, 0, 0));
        const holdP = toPoint(@HOLD@);
        const lineEdge = qContainsPoint(sketchEdge(@SRC@), pt(42010, 0, 0));
        var dev = 1 * meter;
        var a = 1;
        if (r.ok)
        {
            dev = heldDev(r.edge, lineEdge, evDistance(context, { "side0" : lineEdge, "side1" : holdP }).sides[0].parameter);
            a = refMatch(r.edge, lineEdge, holdP).angle;
        }
        return [r.ok && r.modErr < 0.001 * millimeter && dev < 0.011 * millimeter && a < 1e-3 && nPoints(@SELF@) == 1,
            r.text ~ "; held part (line) max deviation " ~ fmt(dev) ~ " mm (splineTol 0.01); tangent at the hold vs line " ~ num(a) ~ " rad"];''',
     TO="T32 to point", HOLD="T32 hold point", SRC="T32 curve")

mce("T33 ", r'''
        const src = sketchEdge(@SRC@);
        const L = evLength(context, { "entities" : src });
        const th = (L - 60 * millimeter) / L;
        var dev = 1 * meter;
        var a = 1;
        if (r.ok)
        {
            dev = heldDev(r.edge, src, th);
            a = refMatch(r.edge, src, tangentAt(src, th).origin).angle;
        }
        return [r.ok && r.modErr < 0.001 * millimeter && nPoints(@SELF@) == 1 && dev < 0.001 * millimeter && a < 1e-4,
            mceText(r) ~ "; held part max deviation " ~ fmt(dev) ~ " mm; tangent at the hold " ~ num(a) ~ " rad"];''')

mce("T35 ", r'''
        return [basic(r) && r.fixAngle < 1e-4, mceText(r)];''')

case("T36 ", ERROR_CASE)
case("T37 ", ERROR_CASE)

mce("T38 ", r'''
        const holdP = toPoint(@HOLD@);
        const src = sketchEdge(@SRC@);
        var dev = 1 * meter;
        var a = 1;
        var z = 1 * meter;
        if (r.ok)
        {
            dev = heldDev(r.edge, src, evDistance(context, { "side0" : src, "side1" : holdP }).sides[0].parameter);
            a = refMatch(r.edge, sketchEdge(@REF@), r.modP).angle;
            z = 0 * meter;
            for (var i = 0; i <= 40; i += 1)
            {
                z = max(z, abs(tangentAt(r.edge, i / 40).origin[2]));
            }
        }
        return [r.ok && r.modErr < 0.001 * millimeter && dev < 0.001 * millimeter && a < 1e-4 && z < 0.01 * millimeter,
            mceText(r) ~ "; held part max deviation " ~ fmt(dev) ~ " mm; end tangent vs arc " ~ num(a) ~ " rad; max |z| " ~ fmt(z) ~ " mm"];''',
    HOLD="T38 hold point", REF="T38 reference arc")

mce("T40 ", r'''
        const kv = keyAt(@SELF@, "movedVertex", toPoint(@TO@));
        const src = sketchEdge(@SRC@);
        const kh = keyAt(@SELF@, "holdVertex", tangentAt(src, 0).origin);
        const out = embedded(@SELF@);
        const nOut = count(out.query.output.value);
        return [basic(r) && kv.ok && kh.ok && nOut == 1, mceText(r) ~ "; " ~ kv.text ~ "; " ~ kh.text ~ "; output " ~ nOut ~ " body"];''')

# ---- Scaled Curve ----
case("SC0 ", r'''
        const e = wireEdge(@SELF@);
        const nb = count(bodies(@SELF@));
        if (count(e) != 1)
        {
            return [false, count(e) ~ " output edges, " ~ nb ~ " bodies"];
        }
        var dy = 0 * meter;
        for (var i = 0; i <= 20; i += 1)
        {
            const p = tangentAt(e, i / 20).origin;
            dy = max(dy, abs(p[1] - 50 * millimeter) + abs(p[2]));
        }
        const p0 = tangentAt(e, 0).origin;
        const p1 = tangentAt(e, 1).origin;
        const endErr = min(norm(p0 - pt(10000, 50, 0)) + norm(p1 - pt(10200, 50, 0)), norm(p0 - pt(10200, 50, 0)) + norm(p1 - pt(10000, 50, 0)));
        return [nb == 1 && dy < 0.001 * millimeter && endErr < 0.001 * millimeter,
            nb ~ " bodies; max off the midline y = 50 " ~ fmt(dy) ~ " mm; end error " ~ fmt(endErr) ~ " mm"];''')

case("SC1 ", r'''
        const nw = count(qBodyType(bodies(@SELF@), BodyType.WIRE));
        return [nw == 1, nw ~ " wire bodies (" ~ count(bodies(@SELF@)) ~ " bodies), expected 1"];''')

case("SC2 ", r'''
        const e = wireEdge(@SELF@);
        if (count(e) != 1)
        {
            return [false, count(e) ~ " output edges, " ~ count(bodies(@SELF@)) ~ " bodies"];
        }
        const deg = evCurveDefinition(context, { "edge" : e }).degree;
        return [deg == 5, "output degree " ~ toString(deg) ~ ", expected 5"];''')

case("SC3 ", r'''
        const e = wireEdge(@SELF@);
        if (count(e) != 1)
        {
            return [false, count(e) ~ " output edges, " ~ count(bodies(@SELF@)) ~ " bodies"];
        }
        var rdev = 0 * meter;
        for (var i = 0; i <= 40; i += 1)
        {
            rdev = max(rdev, abs(norm(tangentAt(e, i / 40).origin - pt(13000, 0, 0)) - 150 * millimeter));
        }
        return [rdev < 0.002 * millimeter, "max |radius - 150| " ~ fmt(rdev) ~ " mm (fit tolerance 0.001)"];''')

# ---- Pull surface ----
case("P1 ", r'''
        const r = pullReport(@SELF@, cylinder(@PATCH@));
        return [r.ok && r.bodies == 1 && r.g0 < 0.001 * millimeter && r.pulled > 5 * millimeter, r.text];''', PATCH="P1 patch")

case("P2 ", r'''
        const r = pullReport(@SELF@, cylinder(@PATCH@));
        return [r.ok && r.bodies == 1 && r.g0 < 0.001 * millimeter && r.g1 < 1e-3 && r.pulled > 5 * millimeter, r.text];''', PATCH="P2 patch")

case("P3 ", r'''
        const r = pullReport(@SELF@, qContainsPoint(qCreatedBy(makeId(@BLOCK@), EntityType.FACE), pt(17000, 0, 10)));
        return [r.ok && r.bodies == 1 && r.g0 < 0.001 * millimeter && r.pulled > 5 * millimeter, r.text];''', BLOCK="P3 block")

# Cases that document today's bugs (reviews/2026-09-25_tools_review/curves.md). Remove an entry once its fix lands.
EXPECT_FAIL = {
    # T05, T09, SC1-SC3 fixed 2026-09-25 (quick fixes after the tools review); P1, P2 fixed 2026-09-26 (Pull surface
    # control-net rewrite) -- they now guard against regressions.
}

# Feature statuses other than OK that are the expected outcome: a status or a tuple of allowed statuses.
# INFO = reportFeatureInfo (an expected outcome), ERROR = a regenError case (temporary instance, T23/T24/T36/T37).
# (OK, INFO) where an optional note (shortened end handle, merged-fit deviation, hold point a few um off) may appear.
MAYBE_INFO = ("OK", "INFO")
EXPECTED_STATUS = {
    "T07 ": "INFO",  # face reference: the tangent is the approach direction projected into the face
    "T10 ": MAYBE_INFO, "T12 ": MAYBE_INFO, "T13 ": MAYBE_INFO, "T14 ": MAYBE_INFO, "T16 ": "INFO", "T17 ": "INFO",
    "T20 ": MAYBE_INFO, "T21 ": MAYBE_INFO, "T22 ": "INFO", "T23 ": "ERROR", "T24 ": "ERROR",
    "T30 ": MAYBE_INFO, "T31 ": MAYBE_INFO, "T32 ": MAYBE_INFO, "T33 ": MAYBE_INFO, "T34 ": MAYBE_INFO,
    "T35 ": "INFO", "T36 ": "ERROR", "T37 ": "ERROR", "T38 ": "INFO",
}


def strings(result):
    return [s for s in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(result)) if not s.startswith("BTFSValue")]


def main():
    feats = c.get(f"{BASE}/features")
    states = feats["featureStates"]
    by_name = [(x["name"], x["featureId"]) for x in feats["features"]]
    failed = 0

    def case_id(prefix):
        found = [i for n, i in by_name if n.startswith(prefix) and "->" in n]
        return found[0] if len(found) == 1 else None

    def fixture_id(name):
        if name.startswith("case:"):
            found = case_id(name[len("case:"):])
            if found is None:
                raise SystemExit("case %r not found" % name)
            return found
        found = [i for n, i in by_name if n == name]
        if len(found) != 1:
            raise SystemExit("fixture %r matches %d features" % (name, len(found)))
        return found[0]

    for name, i in by_name:
        status = states.get(i, {}).get("featureStatus")
        want = EXPECTED_STATUS.get(name.split(" ")[0] + " ", "OK") if "->" in name else "OK"
        if status not in (want if isinstance(want, tuple) else (want,)):
            failed += 1
            print("FAIL", name, "-- status", status, "expected", want)

    for prefix, body, names in CASES:
        self_id = case_id(prefix)
        if self_id is None:
            print("FAIL", prefix, "-- case feature not found (or not unique)")
            failed += 1
            continue
        name = [n for n, i in by_name if i == self_id][0]
        src = body.replace("@SELF@", '"%s"' % self_id)
        for key, value in names.items():
            src = src.replace("@%s@" % key, '"%s"' % fixture_id(value))
        script = ("function(context is Context, queries)\n{\n" + HELPERS + "    const check = function() returns array\n        {\n"
                  + src + "\n        };\n    const r = check();\n    return [r[0] ? \"PASS\" : \"FAIL\", r[1]];\n}\n")
        r = c.post(f"{BASE}/featurescript", json_data={"script": script})
        errors = [x.get("message") for x in (r.get("notices") or []) if x.get("level") == "ERROR"]
        got = strings(r.get("result"))
        if errors or len(got) < 2:
            print("FAIL", name, "-- check did not run:", "; ".join(str(e) for e in (errors or ["no result"])))
            failed += 1
            continue
        expected_fail = EXPECT_FAIL.get(prefix)
        if expected_fail is None:
            failed += got[0] != "PASS"
            print(got[0], name, "--", got[1])
        elif got[0] == "FAIL":
            print("XFAIL", name, "--", got[1], "|| known bug:", expected_fail)
        else:
            failed += 1
            print("XPASS", name, "--", got[1], "|| fixed? remove it from EXPECT_FAIL:", expected_fail)
    print("%d failed" % failed if failed else "all passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
