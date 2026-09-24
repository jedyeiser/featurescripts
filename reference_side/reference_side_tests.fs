FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

// IMPORT: split_plus.fs, offset_plus.fs, mutual_trim_plus.fs
import(path : "637854639ad04840dc6f998d", version : "");
import(path : "742e5b3f04cc9115de8b6d8a", version : "");
import(path : "66c049c310f124d978645003", version : "");

/**
 * Reference side tests: builds its own fixtures, runs Split+, Offset+ and Mutual Trim+ on
 * them, and measures each result against what it must be. PASS / FAIL lines go to the
 * console; the feature shows a warning when anything failed. Every case places its
 * fixtures far from the others (X offsets of 1 m) so the measurements cannot interfere.
 */
annotation { "Feature Type Name" : "Reference side tests", "UIHint" : UIHint.NO_PREVIEW_PROVIDED }
export const referenceSideTests = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Split+", "Default" : true }
        definition.runSplit is boolean;
        annotation { "Name" : "Offset+ surfaces", "Default" : true }
        definition.runOffsetSurface is boolean;
        annotation { "Name" : "Offset+ curves", "Default" : true }
        definition.runOffsetCurve is boolean;
        annotation { "Name" : "Mutual Trim+", "Default" : true }
        definition.runTrim is boolean;
    }
    {
        var results = [];
        if (definition.runSplit)
        {
            results = concatenateArrays([results, splitTests(context, id + "split")]);
        }
        if (definition.runOffsetSurface)
        {
            results = concatenateArrays([results, offsetSurfaceTests(context, id + "osurf")]);
        }
        if (definition.runOffsetCurve)
        {
            results = concatenateArrays([results, offsetCurveTests(context, id + "ocurve")]);
        }
        if (definition.runTrim)
        {
            results = concatenateArrays([results, trimTests(context, id + "trim")]);
        }

        var failed = [];
        for (var r in results)
        {
            println((r.ok ? "PASS " : "FAIL ") ~ r.name ~ " -- " ~ r.detail);
            if (!r.ok)
            {
                failed = append(failed, r.name);
            }
        }
        if (size(failed) > 0)
        {
            reportFeatureWarning(context, id, size(failed) ~ " of " ~ size(results) ~ " failed: " ~ join(failed, ", "));
        }
        else
        {
            reportFeatureInfo(context, id, "All " ~ size(results) ~ " passed.");
        }
    }, { "runSplit" : true, "runOffsetSurface" : true, "runOffsetCurve" : true, "runTrim" : true });

// ============================================================================
// Fixtures and measures
// ============================================================================

function mm(x is number) returns ValueWithUnits
{
    return x * millimeter;
}

function pt(x is number, y is number, z is number) returns Vector
{
    return vector(x, y, z) * millimeter;
}

function makeCube(context is Context, id is Id, c1 is Vector, c2 is Vector) returns Query
{
    fCuboid(context, id, { "corner1" : c1, "corner2" : c2 });
    return qCreatedBy(id, EntityType.BODY);
}

/** A small cube at p: a reference body. */
function marker(context is Context, id is Id, p is Vector) returns Query
{
    return makeCube(context, id, p - pt(0.5, 0.5, 0.5), p + pt(0.5, 0.5, 0.5));
}

/** A sheet copied from the face of a new cube through point p (the cube is deleted). */
function sheetFromCube(context is Context, id is Id, c1 is Vector, c2 is Vector, p is Vector) returns Query
{
    const body = makeCube(context, id + "cube", c1, c2);
    opExtractSurface(context, id + "sheet", { "faces" : qContainsPoint(qOwnedByBody(body, EntityType.FACE), p) });
    opDeleteBodies(context, id + "delete", { "entities" : body });
    return qCreatedBy(id + "sheet", EntityType.BODY);
}

/** A closed polygon or open polyline of sketch lines in the plane z = z0. */
function polyline(context is Context, id is Id, points is array, z0 is number, closed is boolean) returns Query
{
    const sketch = newSketchOnPlane(context, id, { "sketchPlane" : plane(pt(0, 0, z0), vector(0, 0, 1)) });
    const n = closed ? size(points) : size(points) - 1;
    for (var i = 0; i < n; i += 1)
    {
        skLineSegment(sketch, "line" ~ i, { "start" : points[i], "end" : points[(i + 1) % size(points)] });
    }
    skSolve(sketch);
    return qOwnedByBody(qBodyType(qCreatedBy(id, EntityType.BODY), BodyType.WIRE), EntityType.EDGE);
}

function result(name is string, ok is boolean, detail is string) returns map
{
    return { "name" : name, "ok" : ok, "detail" : detail };
}

function near(value is ValueWithUnits, target is ValueWithUnits, tol is ValueWithUnits) returns boolean
{
    return abs(value - target) <= tol;
}

function fmt(value is ValueWithUnits) returns string
{
    return toString(roundToPrecision(value / millimeter, 4));
}

function fmtV(p is Vector) returns string
{
    return "(" ~ fmt(p[0]) ~ ", " ~ fmt(p[1]) ~ ", " ~ fmt(p[2]) ~ ")";
}

function totalLength(context is Context, bodies is Query) returns ValueWithUnits
{
    return evLength(context, { "entities" : qOwnedByBody(bodies, EntityType.EDGE) });
}

/** Min and max distance from points along a wire's edges to the source. */
function distanceRange(context is Context, wire is Query, source is Query) returns map
{
    var lo = inf * meter;
    var hi = 0 * meter;
    for (var e in evaluateQuery(context, qOwnedByBody(wire, EntityType.EDGE)))
    {
        const lines = evEdgeTangentLines(context, { "edge" : e, "parameters" : [0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1] });
        for (var l in lines)
        {
            const dist = evDistance(context, { "side0" : l.origin, "side1" : source }).distance;
            lo = min(lo, dist);
            hi = max(hi, dist);
        }
    }
    return { "lo" : lo, "hi" : hi };
}

// ============================================================================
// Split+
// ============================================================================

function splitTests(context is Context, id is Id) returns array
{
    var out = [];

    // Two tools (z = 0 and x = 0 planes) on a 100 mm cube, keep the reference's corner.
    // Tool normals flipped in the second pass: the result must not change.
    for (var flip in [false, true])
    {
        for (var keepNear in [true, false])
        {
            const cid = id + ("c" ~ (flip ? "f" : "n") ~ (keepNear ? "k" : "o"));
            const target = makeCube(context, cid + "t", pt(-50, -50, -50), pt(50, 50, 50));
            const missed = makeCube(context, cid + "m", pt(-200, -10, -10), pt(-180, 10, 10));
            opPlane(context, cid + "pz", { "plane" : plane(pt(0, 0, 0), vector(0, 0, flip ? -1 : 1)), "width" : mm(400), "height" : mm(400) });
            opPlane(context, cid + "px", { "plane" : plane(pt(0, 0, 0), vector(flip ? 1 : -1, 0, 0)), "width" : mm(400), "height" : mm(400) });
            const reference = marker(context, cid + "r", pt(30, 0, 30));
            splitPlus(context, cid + "split", {
                        "targets" : qUnion([target, missed]),
                        "tools" : qUnion([qCreatedBy(cid + "pz", EntityType.FACE), qCreatedBy(cid + "px", EntityType.FACE)]),
                        "keepBothSides" : false,
                        "keepReference" : reference,
                        "keepNear" : keepNear
                    });
            const kept = qUnion([target, missed, qCreatedBy(cid + "split", EntityType.BODY)]);
            const bodies = evaluateQuery(context, kept);
            const expected = keepNear ? pt(25, 0, 25) : pt(-25, 0, -25);
            const name = "Split+ two tools, " ~ (flip ? "flipped normals" : "normals as drawn") ~ ", keep " ~ (keepNear ? "reference side" : "other side");
            if (size(bodies) != (keepNear ? 1 : 2))
            {
                out = append(out, result(name, false, size(bodies) ~ " bodies left, expected " ~ (keepNear ? 1 : 2)));
                continue;
            }
            // The quarter cube; with keepNear off the missed cube (x < 0, z around 0) is on
            // the kept side of the x tool but straddles z = 0 and is split -- its lower half
            // stays. Measure the quarter as the body with the largest volume.
            var quarter = bodies[0];
            for (var b in bodies)
            {
                if (evVolume(context, { "entities" : b }) > evVolume(context, { "entities" : quarter }))
                {
                    quarter = b;
                }
            }
            const c = evApproximateCentroid(context, { "entities" : quarter });
            out = append(out, result(name, norm(c - expected) < mm(0.01),
                        "quarter centroid " ~ fmtV(c) ~ ", expected " ~ fmtV(expected) ~ ", " ~ size(bodies) ~ " bodies"));
        }
    }

    // Keep both sides, two tools: four pieces.
    {
        const cid = id + "both";
        const target = makeCube(context, cid + "t", pt(950, -50, -50), pt(1050, 50, 50));
        opPlane(context, cid + "pz", { "plane" : plane(pt(1000, 0, 0), vector(0, 0, 1)), "width" : mm(400), "height" : mm(400) });
        opPlane(context, cid + "px", { "plane" : plane(pt(1000, 0, 0), vector(1, 0, 0)), "width" : mm(400), "height" : mm(400) });
        splitPlus(context, cid + "split", {
                    "targets" : target,
                    "tools" : qUnion([qCreatedBy(cid + "pz", EntityType.FACE), qCreatedBy(cid + "px", EntityType.FACE)]),
                    "keepBothSides" : true
                });
        const n = size(evaluateQuery(context, qUnion([target, qCreatedBy(cid + "split", EntityType.BODY)])));
        out = append(out, result("Split+ keep both sides, two tools", n == 4, n ~ " pieces, expected 4"));
    }

    // Keep both sides on a SHEET cut by two planes: three pieces, and splitEdges publishes
    // one edge per cut (2), not the coincident pair each cut leaves (4).
    {
        const cid = id + "seams";
        const sheet = sheetFromCube(context, cid + "t", pt(1450, -50, -10), pt(1550, 50, 0), pt(1500, 0, 0));
        opPlane(context, cid + "p1", { "plane" : plane(pt(1480, 0, 0), vector(1, 0, 0)), "width" : mm(400), "height" : mm(400) });
        opPlane(context, cid + "p2", { "plane" : plane(pt(1520, 0, 0), vector(-1, 0, 0)), "width" : mm(400), "height" : mm(400) });
        splitPlus(context, cid + "split", {
                    "targets" : sheet,
                    "tools" : qUnion([qCreatedBy(cid + "p1", EntityType.FACE), qCreatedBy(cid + "p2", EntityType.FACE)]),
                    "keepBothSides" : true
                });
        const embedded = getVariable(context, toString(cid + "split"));
        const seams = evaluateQuery(context, embedded.query.splitEdges.value);
        const pieces = size(evaluateQuery(context, embedded.query.output.value));
        out = append(out, result("Split+ keep both sides on a sheet: one splitEdge per cut", size(seams) == 2 && pieces == 3,
                    size(seams) ~ " split edges (expected 2), " ~ pieces ~ " pieces (expected 3)"));
    }

    // A sheet tool that is deleted afterwards (Keep tools off).
    {
        const cid = id + "sheet";
        const target = makeCube(context, cid + "t", pt(1950, -50, -50), pt(2050, 50, 50));
        const tool = sheetFromCube(context, cid + "tool", pt(1900, -100, -100), pt(2100, 100, 0), pt(2000, 0, 0));
        const reference = marker(context, cid + "r", pt(2000, 0, -30));
        splitPlus(context, cid + "split", {
                    "targets" : target,
                    "tools" : tool,
                    "keepBothSides" : false,
                    "keepReference" : reference,
                    "keepNear" : true
                });
        const bodies = evaluateQuery(context, qUnion([target, qCreatedBy(cid + "split", EntityType.BODY)]));
        const c = size(bodies) == 1 ? evApproximateCentroid(context, { "entities" : bodies[0] }) : pt(0, 0, 0);
        const toolGone = isQueryEmpty(context, tool);
        out = append(out, result("Split+ sheet tool, reference below", size(bodies) == 1 && near(c[2], mm(-25), mm(0.01)) && toolGone,
                    size(bodies) ~ " bodies, centroid z " ~ fmt(c[2]) ~ " (expected -25), tool deleted " ~ toolGone));
    }
    return out;
}

// ============================================================================
// Offset+ surfaces
// ============================================================================

function offsetSurfaceTests(context is Context, id is Id) returns array
{
    var out = [];
    // Top face (normal +Z, z = 50) and bottom face (normal -Z, z = -50) of a cube; reference
    // at the centre: toward it both move inward to +-40, away both outward to +-60.
    for (var toward in [true, false])
    {
        const cid = id + (toward ? "in" : "out");
        const x0 = toward ? 3000 : 4000;
        const top = sheetFromCube(context, cid + "top", pt(x0 - 50, -50, -50), pt(x0 + 50, 50, 50), pt(x0, 0, 50));
        const bottom = sheetFromCube(context, cid + "bot", pt(x0 - 50, -50, -50), pt(x0 + 50, 50, 50), pt(x0, 0, -50));
        const reference = marker(context, cid + "r", pt(x0, 0, 0));
        offsetPlus(context, cid + "offset", {
                    "offsetType" : OffsetPlusType.SURFACE,
                    "surfaces" : qUnion([top, bottom]),
                    "distance" : mm(10),
                    "sideReference" : reference,
                    "towardReference" : toward
                });
        var zs = [];
        for (var b in evaluateQuery(context, qCreatedBy(cid + "offset", EntityType.BODY)))
        {
            zs = append(zs, evApproximateCentroid(context, { "entities" : b })[2]);
        }
        const want = toward ? mm(40) : mm(60);
        const ok = size(zs) == 2 && near(abs(zs[0]), want, mm(0.001)) && near(abs(zs[1]), want, mm(0.001)) && zs[0] * zs[1] < 0 * meter * meter;
        var shown = [];
        for (var z in zs)
        {
            shown = append(shown, fmt(z));
        }
        out = append(out, result("Offset+ surfaces of opposite normals, " ~ (toward ? "toward" : "away from") ~ " reference", ok,
                    "z = " ~ join(shown, ", ") ~ " (expected +-" ~ fmt(want) ~ ")"));
    }
    return out;
}

// ============================================================================
// Offset+ curves
// ============================================================================

function offsetCurveTests(context is Context, id is Id) returns array
{
    var out = [];
    const d = mm(10);

    // Closed 100 mm square in XY, reference outside to +X. Outward: 4 sides + 4 quarter
    // arcs of radius 10 = 400 + 20 pi. Inward (away): an 80 mm square = 320. Plane and
    // transport frames must agree for a planar chain with an in-plane reference.
    for (var mode in [OffsetFrameMode.PLANE, OffsetFrameMode.TRANSPORT])
    {
        for (var toward in [true, false])
        {
            const tag = (mode == OffsetFrameMode.PLANE ? "plane" : "transport") ~ (toward ? "Out" : "In");
            const cid = id + tag;
            const x0 = mode == OffsetFrameMode.PLANE ? 5000 : 6000;
            const y0 = toward ? 0 : 500;
            const square = polyline(context, cid + "sq", [
                            vector(x0, y0) * millimeter, vector(x0 + 100, y0) * millimeter,
                            vector(x0 + 100, y0 + 100) * millimeter, vector(x0, y0 + 100) * millimeter], 0, true);
            const reference = marker(context, cid + "r", pt(x0 + 300, y0 + 50, 0));
            offsetPlus(context, cid + "offset", {
                        "offsetType" : OffsetPlusType.CURVE,
                        "curves" : square,
                        "frameMode" : mode,
                        "distance" : d,
                        "sideReference" : reference,
                        "towardReference" : toward
                    });
            const wire = qCreatedBy(cid + "offset", EntityType.BODY);
            const wires = size(evaluateQuery(context, wire));
            const length = totalLength(context, wire);
            const want = toward ? mm(400 + 20 * PI) : mm(320);
            const range = distanceRange(context, wire, square);
            out = append(out, result("Offset+ curve square, " ~ tag, wires == 1 && near(length, want, mm(0.01))
                            && near(range.lo, d, mm(0.002)) && near(range.hi, d, mm(0.002)),
                        wires ~ " wire(s), length " ~ fmt(length) ~ " (expected " ~ fmt(want) ~ "), distance to source "
                        ~ fmt(range.lo) ~ " .. " ~ fmt(range.hi)));
        }
    }

    // Surface normal frame: a straight line lying on a flat sheet at z = 50; reference above
    // -> the offset lifts to z = 60. Along the surface with a reference at +Y -> y = +10.
    for (var direction in [SurfaceOffsetDirection.NORMAL, SurfaceOffsetDirection.TANGENT])
    {
        const normalMode = direction == SurfaceOffsetDirection.NORMAL;
        const cid = id + (normalMode ? "snormal" : "stangent");
        const x0 = normalMode ? 7000 : 8000;
        const sheet = sheetFromCube(context, cid + "s", pt(x0 - 50, -50, 0), pt(x0 + 50, 50, 50), pt(x0, 0, 50));
        const seg = polyline(context, cid + "l", [vector(x0 - 40, 0) * millimeter, vector(x0 + 40, 0) * millimeter], 50, false);
        const reference = marker(context, cid + "r", normalMode ? pt(x0, 0, 120) : pt(x0, 40, 50));
        offsetPlus(context, cid + "offset", {
                    "offsetType" : OffsetPlusType.CURVE,
                    "curves" : seg,
                    "frameMode" : OffsetFrameMode.SURFACE,
                    "normalSurface" : sheet,
                    "surfaceDirection" : direction,
                    "distance" : d,
                    "sideReference" : reference,
                    "towardReference" : true
                });
        const box3 = evBox3d(context, { "topology" : qCreatedBy(cid + "offset", EntityType.BODY) });
        const mid = (box3.minCorner + box3.maxCorner) / 2;
        const want = normalMode ? pt(x0, 0, 60) : pt(x0, 10, 50);
        out = append(out, result("Offset+ curve on surface, " ~ (normalMode ? "along normal" : "along surface"), norm(mid - want) < mm(0.002),
                    "centre " ~ fmtV(mid) ~ ", expected " ~ fmtV(want)));
    }

    // Transport frame on a 3D helix (r 50, pitch 40, 2 turns), reference on the axis: runs,
    // one wire, and every point stays exactly 10 from the helix.
    {
        const cid = id + "helix";
        var points = [];
        for (var i = 0; i <= 48; i += 1)
        {
            const a = i / 24 * PI * radian;
            points = append(points, pt(9000 + 50 * cos(a), 50 * sin(a), 40 * i / 24));
        }
        opFitSpline(context, cid + "h", { "points" : points });
        const helix = qCreatedBy(cid + "h", EntityType.EDGE);
        const reference = marker(context, cid + "r", pt(9000, 0, 0));
        offsetPlus(context, cid + "offset", {
                    "offsetType" : OffsetPlusType.CURVE,
                    "curves" : helix,
                    "frameMode" : OffsetFrameMode.TRANSPORT,
                    "distance" : d,
                    "sideReference" : reference,
                    "towardReference" : true
                });
        const wire = qCreatedBy(cid + "offset", EntityType.BODY);
        const range = distanceRange(context, wire, helix);
        const start = evDistance(context, { "side0" : pt(9050, 0, 0), "side1" : wire }).sides[1].point;
        const startRadius = norm(vector(start[0] - mm(9000), start[1], 0 * meter));
        out = append(out, result("Offset+ curve helix, transport toward axis", near(range.lo, d, mm(0.005)) && near(range.hi, d, mm(0.005)) && startRadius < mm(50),
                    "distance to source " ~ fmt(range.lo) ~ " .. " ~ fmt(range.hi) ~ ", start radius " ~ fmt(startRadius) ~ " (< 50: went toward the axis)"));
    }

    // Open L with one inside and one outside corner (a Z): trims one, rounds the other.
    {
        const cid = id + "zig";
        const chain = polyline(context, cid + "z", [vector(10000, 0) * millimeter, vector(10100, 0) * millimeter,
                        vector(10100, 100) * millimeter, vector(10200, 100) * millimeter], 0, false);
        const reference = marker(context, cid + "r", pt(10050, -30, 0));
        offsetPlus(context, cid + "offset", {
                    "offsetType" : OffsetPlusType.CURVE,
                    "curves" : chain,
                    "frameMode" : OffsetFrameMode.PLANE,
                    "distance" : d,
                    "sideReference" : reference,
                    "towardReference" : true
                });
        const wire = qCreatedBy(cid + "offset", EntityType.BODY);
        // Offset toward -Y on the first leg (y = -10, x 0..100); the left turn at (100, 0) is
        // outside -> quarter arc r 10 to (110, 0); second leg x = 110 up to the crossing with
        // the third (y = 90) at (110, 90) -> 90; third leg 110..200 -> 90.
        const length = totalLength(context, wire);
        const want = mm(280 + 5 * PI);
        const range = distanceRange(context, wire, chain);
        out = append(out, result("Offset+ curve Z chain, one round + one trim", size(evaluateQuery(context, wire)) == 1 && near(length, want, mm(0.01)) && near(range.lo, d, mm(0.002)),
                    "length " ~ fmt(length) ~ " (expected " ~ fmt(want) ~ "), distance " ~ fmt(range.lo) ~ " .. " ~ fmt(range.hi)));
    }
    return out;
}

// ============================================================================
// Mutual Trim+
// ============================================================================

function trimTests(context is Context, id is Id) returns array
{
    var out = [];
    // Horizontal sheet z = 0 (x -50..50) and vertical sheet x = 0 (z -50..50) crossing on
    // the Y axis; reference in the +X +Z quadrant: keep x > 0 of the first, z > 0 of the
    // second -- an L of 2 x 5000 mm^2 -- whatever the sheets' normals.
    for (var near1 in [true, false])
    {
        const cid = id + (near1 ? "near" : "far");
        const x0 = near1 ? 11000 : 12000;
        const horizontal = sheetFromCube(context, cid + "h", pt(x0 - 50, -50, -10), pt(x0 + 50, 50, 0), pt(x0, 0, 0));
        const vertical = sheetFromCube(context, cid + "v", pt(x0 - 10, -50, -50), pt(x0, 50, 50), pt(x0, 0, 0));
        const reference = marker(context, cid + "r", pt(x0 + 20, 0, 20));
        mutualTrimPlus(context, cid + "trim", {
                    "body1" : horizontal,
                    "body2" : vertical,
                    "keepNear1" : near1,
                    "keepNear2" : true,
                    "keepReference" : reference,
                    "merge" : true
                });
        const surfaces = qUnion([horizontal, vertical]);
        const area = evArea(context, { "entities" : qOwnedByBody(surfaces, EntityType.FACE) });
        const c = evApproximateCentroid(context, { "entities" : qOwnedByBody(surfaces, EntityType.FACE) });
        // near: faces at x 0..50 (z 0) and z 0..50 (x 0) -> centroid (12.5, 0, 12.5) relative.
        // far on the first: x -50..0 (z 0) and z 0..50 -> centroid (-12.5, 0, 12.5).
        const want = near1 ? pt(x0 + 12.5, 0, 12.5) : pt(x0 - 12.5, 0, 12.5);
        out = append(out, result("Mutual Trim+ " ~ (near1 ? "keep reference side on both" : "first surface keeps the far side"),
                    near(area, 10000 * millimeter * millimeter, 0.01 * millimeter * millimeter) && norm(c - want) < mm(0.01),
                    "area " ~ toString(roundToPrecision(area / (millimeter * millimeter), 3)) ~ " mm^2 (expected 10000), centroid " ~ fmtV(c) ~ ", expected " ~ fmtV(want)));
    }
    return out;
}
