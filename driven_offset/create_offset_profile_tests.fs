FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

// IMPORT: create_offset_profile.fs
import(path : "3fccdcb24013c744bd0fd8a2", version : "");

/**
 * Offset profile tests: builds profiles with Create offset profile and checks values, pieces, breaks and
 * joint continuity against the design (research_create_offset_profile.md). PASS / FAIL lines go to the
 * console; the feature shows a warning when anything failed. Cases sit 1 m apart in X.
 */
annotation { "Feature Type Name" : "Offset profile tests", "UIHint" : UIHint.NO_PREVIEW_PROVIDED }
export const offsetProfileTests = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
    }
    {
        var results = [];
        results = concatenateArrays([results, fcpAcpTest(context, id + "fcp")]);
        results = concatenateArrays([results, bufferTest(context, id + "buffer")]);
        results = concatenateArrays([results, blendTest(context, id + "blend")]);
        results = concatenateArrays([results, pointsSmoothTest(context, id + "psmooth")]);
        results = concatenateArrays([results, pointsJumpTest(context, id + "pjump")]);
        results = concatenateArrays([results, errorTests(context, id + "errors")]);

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
    }, {});

// ============================================================================
// Fixtures and measures
// ============================================================================

function mm(x is number) returns ValueWithUnits
{
    return x * millimeter;
}

function result(name is string, ok is boolean, detail is string) returns map
{
    return { "name" : name, "ok" : ok, "detail" : detail };
}

function fmt(v) returns string
{
    if (v is ValueWithUnits)
    {
        return toString(roundToPrecision(v / millimeter, 5));
    }
    return toString(roundToPrecision(v, 6));
}

/** A region definition (stations and values in mm). */
function region(name is string, xs is number, xe is number, w0 is number, w1 is number, h0 is number, h1 is number,
    shape is OffsetProfileShape, b0 is number, b1 is number) returns map
{
    return { "regionName" : name, "startStation" : mm(xs), "endStation" : mm(xe), "startWidth" : mm(w0), "endWidth" : mm(w1),
            "startHeight" : mm(h0), "endHeight" : mm(h1), "shape" : shape, "startBuffer" : mm(b0), "endBuffer" : mm(b1) };
}

function noBlend(a is string, b is string) returns map
{
    return { "region1" : a, "region2" : b, "blend" : false, "startContinuity" : GeometricContinuity.G0, "startDistance" : mm(0),
            "endContinuity" : GeometricContinuity.G0, "endDistance" : mm(0) };
}

function pt(x is number, w is number, h is number, transition is OffsetPointTransition) returns map
{
    return { "station" : mm(x), "width" : mm(w), "height" : mm(h), "transition" : transition };
}

/** [width, height] of the profile at station x (mm): where the plane x = const meets the output wires. */
function valueAt(context is Context, wires is Query, x is number) returns array
{
    const hit = evDistance(context, { "side0" : plane(vector(x, 0, 0) * millimeter, vector(1, 0, 0)), "side1" : qOwnedByBody(wires, EntityType.EDGE) });
    if (hit.distance > 1e-7 * meter)
    {
        return [undefined, undefined];
    }
    return [hit.sides[1].point[1], hit.sides[1].point[2]];
}

function near(v, target is number, tol is number) returns boolean
{
    return v is ValueWithUnits && abs(v - mm(target)) <= mm(tol);
}

/**
 * Tangent and curvature of the output at the joint station x from each side: the two edges meeting there.
 * Returns { ok (two edges found), tangentAngle (between the sides), curvature0, curvature1 }.
 */
function jointAt(context is Context, wires is Query, x is number) returns map
{
    const p = vector(x, 0, 0) * millimeter;
    var sides = [];
    for (var e in evaluateQuery(context, qOwnedByBody(wires, EntityType.EDGE)))
    {
        const ends = evEdgeTangentLines(context, { "edge" : e, "parameters" : [0, 1] });
        for (var k in [0, 1])
        {
            if (abs(ends[k].origin[0] - p[0]) < 1e-9 * meter)
            {
                const curv = evEdgeCurvature(context, { "edge" : e, "parameter" : k });
                sides = append(sides, { "tangent" : ends[k].direction, "curvature" : curv.curvature });
            }
        }
    }
    if (size(sides) != 2)
    {
        return { "ok" : false, "count" : size(sides) };
    }
    return { "ok" : true, "tangentAngle" : angleBetween(sides[0].tangent, sides[1].tangent) < 1e-6 * radian ? 0 * radian
                : min(angleBetween(sides[0].tangent, sides[1].tangent), angleBetween(sides[0].tangent, -sides[1].tangent)),
            "curvature0" : sides[0].curvature, "curvature1" : sides[1].curvature };
}

function embedded(context is Context, id is Id) returns map
{
    return getVariable(context, toString(id));
}

// ============================================================================
// Cases
// ============================================================================

/** Tip [-100, 0] w 0, RSL [0, 1000] w 3, tail [1000, 1100] w 0: three pieces, breaks at 0 and 1000. */
function fcpAcpTest(context is Context, id is Id) returns array
{
    createOffsetProfile(context, id + "p", {
                "mode" : OffsetProfileMode.REGIONS,
                "regions" : [region("Tip", -100, 0, 0, 0, 0, 0, OffsetProfileShape.LINEAR, 0, 0),
                        region("RSL", 0, 1000, 3, 3, 0, 0, OffsetProfileShape.LINEAR, 0, 0),
                        region("Tail", 1000, 1100, 0, 0, 0, 0, OffsetProfileShape.LINEAR, 0, 0)],
                "intersections" : [noBlend("Tip", "RSL"), noBlend("RSL", "Tail")]
            });
    const out = embedded(context, id + "p");
    const wires = out.query.output.value;
    const pieces = size(evaluateQuery(context, wires));
    const b = out.variable.breakStations.value;
    const w1 = valueAt(context, wires, -50)[0];
    const w2 = valueAt(context, wires, 500)[0];
    const w3 = valueAt(context, wires, 1050)[0];
    const ok = pieces == 3 && size(b) == 2 && near(b[0], 0, 1e-6) && near(b[1], 1000, 1e-6) && near(w1, 0, 1e-6) && near(w2, 3, 1e-6) && near(w3, 0, 1e-6)
        && out.variable.pieceCount.value == 3 && size(evaluateQuery(context, out.query.breakVertices.value)) == 4;
    return [result("Regions FCP/ACP: 3 pieces, breaks at FCP and ACP", ok,
                pieces ~ " pieces, breaks " ~ (size(b) > 0 ? fmt(b[0]) : "-") ~ " / " ~ (size(b) > 1 ? fmt(b[1]) : "-")
                ~ ", width at -50 / 500 / 1050: " ~ fmt(w1) ~ " / " ~ fmt(w2) ~ " / " ~ fmt(w3)
                ~ ", breakVertices " ~ size(evaluateQuery(context, out.query.breakVertices.value)) ~ " (expected 4)")];
}

/** Smooth region [2000, 2100] w 0 -> 4 with buffers 10 / 20: holds, mid value, C2 joints at 2010 and 2080. */
function bufferTest(context is Context, id is Id) returns array
{
    createOffsetProfile(context, id + "p", {
                "mode" : OffsetProfileMode.REGIONS,
                "regions" : [region("Smooth", 2000, 2100, 0, 4, 1, 1, OffsetProfileShape.SMOOTH, 10, 20)],
                "intersections" : []
            });
    const wires = embedded(context, id + "p").query.output.value;
    const edges = size(evaluateQuery(context, qOwnedByBody(wires, EntityType.EDGE)));
    const a = valueAt(context, wires, 2005);
    const m = valueAt(context, wires, 2045);
    const e = valueAt(context, wires, 2090);
    const j0 = jointAt(context, wires, 2010);
    const j1 = jointAt(context, wires, 2080);
    const smoothJoint = function(j is map) returns boolean
        {
            return j.ok && j.tangentAngle < 1e-6 * radian && abs(j.curvature0) < 1e-6 / meter && abs(j.curvature1) < 1e-6 / meter;
        };
    return [result("Regions buffers: holds and mid value", edges == 3 && near(a[0], 0, 1e-6) && near(m[0], 2, 1e-6) && near(e[0], 4, 1e-6) && near(m[1], 1, 1e-6),
                edges ~ " edges (expected 3), width at 2005 / 2045 / 2090: " ~ fmt(a[0]) ~ " / " ~ fmt(m[0]) ~ " / " ~ fmt(e[0]) ~ ", height " ~ fmt(m[1])),
            result("Regions buffers: C2 into and out of the smooth ramp", smoothJoint(j0) && smoothJoint(j1),
                "at 2010: " ~ (j0.ok ? "angle " ~ toString(j0.tangentAngle) ~ ", curvature " ~ toString(j0.curvature0) ~ " / " ~ toString(j0.curvature1) : "joint not found")
                ~ "; at 2080: " ~ (j1.ok ? "angle " ~ toString(j1.tangentAngle) ~ ", curvature " ~ toString(j1.curvature0) ~ " / " ~ toString(j1.curvature1) : "joint not found"))];
}

/** A [3000, 3100] linear w 0 -> 2, B [3100, 3200] w 5; blend 20 / 20 G2 / G2: one piece, smooth joints. */
function blendTest(context is Context, id is Id) returns array
{
    var blend = noBlend("A", "B");
    blend.blend = true;
    blend.startContinuity = GeometricContinuity.G2;
    blend.endContinuity = GeometricContinuity.G2;
    blend.startDistance = mm(20);
    blend.endDistance = mm(20);
    createOffsetProfile(context, id + "p", {
                "mode" : OffsetProfileMode.REGIONS,
                "regions" : [region("A", 3000, 3100, 0, 2, 0, 0, OffsetProfileShape.LINEAR, 0, 0),
                        region("B", 3100, 3200, 5, 5, 0, 0, OffsetProfileShape.LINEAR, 0, 0)],
                "intersections" : [blend]
            });
    const out = embedded(context, id + "p");
    const wires = out.query.output.value;
    const pieces = size(evaluateQuery(context, wires));
    const a = valueAt(context, wires, 3080);
    const b = valueAt(context, wires, 3120);
    const j0 = jointAt(context, wires, 3080);
    const j1 = jointAt(context, wires, 3120);
    const g2 = function(j is map) returns boolean
        {
            return j.ok && j.tangentAngle < 1e-6 * radian && abs(j.curvature0 - j.curvature1) < 1e-4 / meter;
        };
    return [result("Regions blend G2/G2: one piece, values at the blend ends", pieces == 1 && near(a[0], 1.6, 1e-6) && near(b[0], 5, 1e-6),
                pieces ~ " piece(s), width at 3080 / 3120: " ~ fmt(a[0]) ~ " / " ~ fmt(b[0])),
            result("Regions blend G2/G2: tangent and curvature continuous at both ends", g2(j0) && g2(j1),
                "at 3080: " ~ (j0.ok ? "angle " ~ toString(j0.tangentAngle) ~ ", curvature " ~ toString(j0.curvature0) ~ " / " ~ toString(j0.curvature1) : "joint not found (" ~ j0.count ~ ")")
                ~ "; at 3120: " ~ (j1.ok ? "angle " ~ toString(j1.tangentAngle) ~ ", curvature " ~ toString(j1.curvature0) ~ " / " ~ toString(j1.curvature1) : "joint not found (" ~ j1.count ~ ")"))];
}

/** Points 4000 w 0, 4050 w 3, 4100 w 1, smooth: one piece, exact at the points, flat there, no overshoot. */
function pointsSmoothTest(context is Context, id is Id) returns array
{
    createOffsetProfile(context, id + "p", {
                "mode" : OffsetProfileMode.POINTS,
                "points" : [pt(4000, 0, 0, OffsetPointTransition.SMOOTH), pt(4050, 3, 2, OffsetPointTransition.SMOOTH),
                        pt(4100, 1, 2, OffsetPointTransition.SMOOTH)]
            });
    const wires = embedded(context, id + "p").query.output.value;
    const pieces = size(evaluateQuery(context, wires));
    var overshoot = false;
    for (var i = 0; i <= 20; i += 1)
    {
        const x = 4000 + 5 * i;
        const v = valueAt(context, wires, x);
        const lo = x <= 4050 ? 0 : 1;
        const hi = 3;
        if (v[0] == undefined || v[0] < mm(lo - 1e-6) || v[0] > mm(hi + 1e-6))
        {
            overshoot = true;
        }
    }
    const mid = valueAt(context, wires, 4050);
    const j = jointAt(context, wires, 4050);
    const flat = j.ok && abs(j.curvature0 - j.curvature1) < 1e-4 / meter && j.tangentAngle < 1e-6 * radian;
    return [result("Points smooth: one piece, exact at the points, no overshoot", pieces == 1 && !overshoot && near(mid[0], 3, 1e-6) && near(mid[1], 2, 1e-6),
                pieces ~ " piece(s), at 4050 width " ~ fmt(mid[0]) ~ " height " ~ fmt(mid[1]) ~ ", overshoot " ~ overshoot),
            result("Points smooth: flat and curvature-continuous at a point", flat,
                j.ok ? "angle " ~ toString(j.tangentAngle) ~ ", curvature " ~ toString(j.curvature0) ~ " / " ~ toString(j.curvature1) : "joint not found")];
}

/** Points 5000 w 0, 5050 w 0, 5050 w 3, 5100 w 3 (linear): a jump at 5050 -> two pieces. */
function pointsJumpTest(context is Context, id is Id) returns array
{
    createOffsetProfile(context, id + "p", {
                "mode" : OffsetProfileMode.POINTS,
                "points" : [pt(5000, 0, 0, OffsetPointTransition.LINEAR), pt(5050, 0, 0, OffsetPointTransition.LINEAR),
                        pt(5050, 3, 0, OffsetPointTransition.LINEAR), pt(5100, 3, 0, OffsetPointTransition.LINEAR)]
            });
    const out = embedded(context, id + "p");
    const pieces = size(evaluateQuery(context, out.query.output.value));
    const b = out.variable.breakStations.value;
    return [result("Points jump: two pieces, break at 5050", pieces == 2 && size(b) == 1 && near(b[0], 5050, 1e-6),
                pieces ~ " piece(s), breaks " ~ (size(b) > 0 ? fmt(b[0]) : "none"))];
}

/** Overlapping regions and too-long buffers are errors. */
function errorTests(context is Context, id is Id) returns array
{
    var overlapFailed = true;
    try silent
    {
        createOffsetProfile(context, id + "overlap", {
                    "mode" : OffsetProfileMode.REGIONS,
                    "regions" : [region("A", 6000, 6100, 0, 0, 0, 0, OffsetProfileShape.LINEAR, 0, 0),
                            region("B", 6050, 6200, 0, 0, 0, 0, OffsetProfileShape.LINEAR, 0, 0)],
                    "intersections" : [noBlend("A", "B")]
                });
        overlapFailed = false;
    }
    var bufferFailed = true;
    try silent
    {
        createOffsetProfile(context, id + "buffer", {
                    "mode" : OffsetProfileMode.REGIONS,
                    "regions" : [region("A", 7000, 7100, 0, 1, 0, 0, OffsetProfileShape.SMOOTH, 60, 50)],
                    "intersections" : []
                });
        bufferFailed = false;
    }
    return [result("Errors: overlapping regions and too-long buffers are refused", overlapFailed && bufferFailed,
                "overlap refused " ~ overlapFailed ~ ", buffers refused " ~ bufferFailed)];
}
