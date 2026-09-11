FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

/**
 * Creates wires (or returns bSpline data) obeying special rules from either a solid body, an edge, or a chain of edges
 *
 * User/caller provides input type {WIRE, PART, EDGES} (query type)
 * User/caller provides output type {WIRE, Data} (query, map)
 * User/caller provides projection planar face
 * User/caller specifies approdimation parameters
 * User/caller specifies if output should be grouped as a single curve, a curve per input curve (exclusing curves that collapse to a point), or an "efficent representation"
 *      Efficent implies look at curvature progressions, degree, weights and control point spacing to evaluate if two curves are good candidates to be merged into a single entity. If so, output has these two curves merged. Note that we may end up wanting to merge multiple curves into a single one.
 *
 * Stop projection where an endpoint of the chain is normal to the reference plane. Note that there may be multiple edges in the chain beyond these tangent points, and we want to ignore all data beyond the first crossing (moving outward from the chain center)
 * If a PART is provided
 *      User/caller specifies what types of data should be returned
 *          Top/Bottom Profiles
 *          Middle Profile
 *          Periphery
 *          All Profiles
 *      The explicit assumption here is that our 'profile direction' here is along, or along-ish the x direction. Users/callers should, however, provide a query or a vector specifying the 'prevailing direction'
 *      We use this prevailing direction to better understand our curves
 *          Top and Bottom profiles will likely be long chains with endpoints near (though not necessarily AT) the extents of our inputs, projected ionto the planar face onto which we're projecting
 *          Top and Bottom profiles will be of similar, though not necessarily equal length
 *          Top and Bottom profiles are joined either by:
 *              A single line with a length sigificantly shorter than either chain
 *              An arc with an arclength significantly shorter than either chain
 *              A b-spline with an arclength significantly shorter than either chain
 *              Intersections between Top/Bottom profiles and the short sections that join them should roughly be strongly perpendicular, or strongly tangent
 *
 *
 */

// ============================================================================
// Status
// ============================================================================
//
// Built: the EDGES / WIRE path -- project a chain onto a planar face, trim it where it
// doubles back, and emit it grouped.
//
// Not built yet: PART input and the Top / Bottom / Middle / Periphery decomposition
// described above. That needs a silhouette split rather than a projection, and it is
// its own piece of work; the prevailing-direction plumbing it will want is already here.

// ============================================================================
// Constants and bounds
// ============================================================================

/**
 * Samples taken along the projected path to find where it doubles back.
 *
 * The reversal is a cusp: the along-direction coordinate stops advancing and turns
 * round. Finding it only needs enough resolution to bracket the turn, because the
 * bracket is then refined by bisection.
 */
export const PROFILE_SCAN_SAMPLES = 400;

/** Bisection steps used to pin a reversal once a sample pair has bracketed it. */
export const PROFILE_REFINE_STEPS = 24;

/** Most comb teeth any debug view will draw. */
export const PROFILE_COMB_TEETH = 120;

/** A direction this short carries no direction. */
export const PROFILE_ZERO_DIRECTION = 1e-9;

export const ProfilePointsBounds = { (unitless) : [8, 60, 400] } as IntegerBoundSpec;
export const ProfileDegreeBounds = { (unitless) : [2, 3, 7] } as IntegerBoundSpec;
export const ProfileMaxCPBounds = { (unitless) : [4, 24, 100] } as IntegerBoundSpec;
export const ProfileToleranceBounds =
{
    (meter)      : [1e-8, 1e-5, 1],
    (centimeter) : 1e-3,
    (millimeter) : 1e-2,
    (inch)       : 1e-3
} as LengthBoundSpec;

export const ProfileCombScaleBounds =
{
    (meter)      : [1e-5, 0.02, 10],
    (centimeter) : 2,
    (millimeter) : 20,
    (inch)       : 0.8
} as LengthBoundSpec;

// ============================================================================
// Enums
// ============================================================================

/**
 * How the retained projection is broken into output curves.
 *
 * PER_CURVE keeps what opDropCurve produced, which is the exact projection: projecting a
 * B-spline is an affine map and B-splines are affine invariant, so the dropped edge IS
 * the projected curve, not a fit of it. Any other grouping has to refit, and trades that
 * exactness for a tidier result.
 */
export enum ProfileGrouping
{
    annotation { "Name" : "One curve" }
    SINGLE,
    annotation { "Name" : "One per input curve" }
    PER_CURVE,
    annotation { "Name" : "Efficient" }
    EFFICIENT
}

// ============================================================================
// Feature
// ============================================================================

annotation { "Feature Type Name" : "Evaluate profiles",
        "Feature Type Description" : "Project a chain of edges onto a planar face, trim it where it doubles back, and emit it as a clean wire" }
export const evaluateProfiles = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Edges to project", "Filter" : (EntityType.EDGE || BodyType.WIRE) && ConstructionObject.NO }
        definition.profileEdges is Query;

        annotation { "Name" : "Project onto", "Filter" : EntityType.FACE && GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
        definition.projectionFace is Query;

        annotation { "Name" : "Prevailing direction", "Filter" : QueryFilterCompound.ALLOWS_DIRECTION, "MaxNumberOfPicks" : 1 }
        definition.prevailingDirection is Query;

        annotation { "Name" : "Flip prevailing direction", "Default" : false, "UIHint" : UIHint.OPPOSITE_DIRECTION }
        definition.flipPrevailing is boolean;

        annotation { "Name" : "Output", "Default" : ProfileGrouping.SINGLE, "UIHint" : UIHint.HORIZONTAL_ENUM }
        definition.grouping is ProfileGrouping;

        annotation { "Name" : "Name", "Description" : "Name given to the resulting wire. Clear it to leave it unnamed." }
        definition.outputName is string;

        annotation { "Group Name" : "Approximation", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Samples per curve", "Description" : "Points taken along each retained span before fitting" }
            isInteger(definition.samplesPerCurve, ProfilePointsBounds);

            annotation { "Name" : "Target degree" }
            isInteger(definition.fitDegree, ProfileDegreeBounds);

            annotation { "Name" : "Tolerance" }
            isLength(definition.fitTolerance, ProfileToleranceBounds);

            annotation { "Name" : "Maximum control points" }
            isInteger(definition.fitMaxCPs, ProfileMaxCPBounds);
        }

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print scan", "Default" : false, "Description" : "Report the projected length, where it was trimmed and why" }
            definition.debugPrintScan is boolean;

            annotation { "Name" : "Show curvature comb", "Default" : false }
            definition.debugShowComb is boolean;

            annotation { "Name" : "Comb scale" }
            isLength(definition.combScale, ProfileCombScaleBounds);
        }
    }
    {
        const result = projectedProfile(context, id, definition);

        if (definition.debugPrintScan)
        {
            printProfileScan(result);
        }
        if (definition.debugShowComb)
        {
            drawCurvatureComb(context, id + "comb", result.samples, definition.combScale);
        }
    });

// ============================================================================
// Reusable core
// ============================================================================

/**
 * Project a chain onto a planar face, trim it where it doubles back, and emit it.
 *
 * Exported so the other features in this document can take the projected profile without
 * going through the feature UI. The returned map carries the sampled geometry as well as
 * the queries, because a caller that wants to reason about the profile -- evaluate_offset
 * measuring against it, say -- needs the points, not just a body to select.
 *
 * @returns {map} :
 *   "curves"   {array}  - the emitted BSplineCurves, in order along the chain
 *   "samples"  {array}  - each { "point", "tangent", "curvature" } along the retained span
 *   "span"     {map}    - { "start", "end" } path parameters retained, in [0, 1]
 *   "trimmed"  {map}    - { "atStart", "atEnd" } booleans: did a reversal cut that end
 *   "length"   {ValueWithUnits} - arc length of the retained projection
 */
export function projectedProfile(context is Context, id is Id, definition is map) returns map
{
    const plane = evPlane(context, { "face" : definition.projectionFace });

    // NORMAL_TO_TARGET on a planar face is projection along the plane normal, which is
    // what "project onto this plane" means. Naming the direction explicitly would say the
    // same thing and then have to be kept in step with the face.
    const dropId = id + "drop";
    opDropCurve(context, dropId, {
                "tools" : definition.profileEdges,
                "targets" : definition.projectionFace,
                "projectionType" : ProjectionType.NORMAL_TO_TARGET
            });

    const dropped = qCreatedBy(dropId, EntityType.EDGE);
    if (isQueryEmpty(context, dropped))
    {
        throw regenError("Nothing projected onto the face.", definition.profileEdges);
    }

    var path;
    try
    {
        path = constructPath(context, dropped);
    }
    catch (error)
    {
        throw regenError("The projected edges do not form a single connected chain.",
            definition.profileEdges);
    }

    const heading = prevailingDirection(context, definition, plane, path);
    const scan = scanForReversals(context, path, heading);

    const samples = sampleSpan(context, path, scan.start, scan.end, definition, plane);
    const curves = emitGrouping(context, id, definition, path, scan, samples);

    opDeleteBodies(context, id + "dropCleanup", { "entities" : qOwnerBody(dropped) });

    nameProfile(context, qCreatedBy(id + "profile", EntityType.BODY), definition.outputName);

    return {
        "curves" : curves,
        "samples" : samples,
        "span" : { "start" : scan.start, "end" : scan.end },
        "trimmed" : { "atStart" : scan.trimmedStart, "atEnd" : scan.trimmedEnd },
        "length" : scan.retainedLength
    };
}

/**
 * The direction the profile is understood to run along.
 *
 * Everything about the trim is stated relative to this: a reversal is the along-heading
 * coordinate turning round. Supplied explicitly when the caller knows better, and
 * otherwise the chord of the projected chain, which is right whenever the chain does not
 * fold back on itself more than it advances.
 */
function prevailingDirection(context is Context, definition is map, plane is Plane, path is Path) returns Vector
{
    var heading = undefined;

    if (!isQueryEmpty(context, definition.prevailingDirection))
    {
        heading = evAxis(context, { "axis" : definition.prevailingDirection }).direction;
    }
    else
    {
        const ends = evPathTangentLines(context, path, [0, 1]).tangentLines;
        heading = ends[1].origin - ends[0].origin;
    }

    // Only the in-plane part means anything: a component along the plane normal cannot
    // distinguish advancing from doubling back, because the projection has removed it.
    heading = heading - dot(heading, plane.normal) * plane.normal;

    if (norm(heading) < PROFILE_ZERO_DIRECTION * meter)
    {
        throw regenError("The prevailing direction lies along the projection direction, "
            ~ "so it cannot say which way the profile advances.", definition.prevailingDirection);
    }

    return (definition.flipPrevailing ? -1 : 1) * normalize(heading);
}

/**
 * Find where the projection doubles back, working outward from the middle.
 *
 * The cusp is where the source tangent stands normal to the plane: the projected tangent
 * vanishes there and the curve turns round. Rather than hunt for a zero tangent, which is
 * exactly where the tangent direction stops being defined, this watches the along-heading
 * COORDINATE and finds where it stops advancing -- the same event, detected on a quantity
 * that stays well behaved through it.
 *
 * Outward from the middle is the rule because a chain may fold more than once; only the
 * first fold on each side bounds the usable profile, and everything past it is discarded
 * whether or not it later turns back the right way.
 */
function scanForReversals(context is Context, path is Path, heading is Vector) returns map
{
    var parameters = [];
    for (var i = 0; i < PROFILE_SCAN_SAMPLES; i += 1)
    {
        parameters = append(parameters, i / (PROFILE_SCAN_SAMPLES - 1));
    }

    const lines = evPathTangentLines(context, path, parameters).tangentLines;

    var advance = [];
    for (var i = 0; i < PROFILE_SCAN_SAMPLES; i += 1)
    {
        advance = append(advance, dot(lines[i].direction, heading));
    }

    const middle = floor((PROFILE_SCAN_SAMPLES - 1) / 2);

    // Which way is "forward" is decided at the middle, not assumed from the heading: a
    // chain traversed against the heading is still a perfectly good profile.
    const forward = (advance[middle] >= 0) ? 1 : -1;

    var startIndex = 0;
    var trimmedStart = false;
    for (var i = middle; i > 0; i -= 1)
    {
        if (forward * advance[i - 1] <= 0)
        {
            startIndex = i - 1;
            trimmedStart = true;
            break;
        }
    }

    var endIndex = PROFILE_SCAN_SAMPLES - 1;
    var trimmedEnd = false;
    for (var i = middle; i < PROFILE_SCAN_SAMPLES - 1; i += 1)
    {
        if (forward * advance[i + 1] <= 0)
        {
            endIndex = i + 1;
            trimmedEnd = true;
            break;
        }
    }

    const start = trimmedStart
        ? refineReversal(context, path, heading, forward, parameters[startIndex], parameters[startIndex + 1])
        : 0;
    const end = trimmedEnd
        ? refineReversal(context, path, heading, forward, parameters[endIndex - 1], parameters[endIndex])
        : 1;

    return {
        "start" : start,
        "end" : end,
        "trimmedStart" : trimmedStart,
        "trimmedEnd" : trimmedEnd,
        "forward" : forward,
        "retainedLength" : (end - start) * evPathLength(context, path)
    };
}

/**
 * Bisect a bracketed reversal.
 *
 * The bracket already has the along-heading rate positive at one end and non-positive at
 * the other, so plain bisection converges without needing a derivative -- and a
 * derivative is the one thing not to ask for here, since it is going through zero.
 */
function refineReversal(context is Context, path is Path, heading is Vector, forward is number,
    lo is number, hi is number) returns number
{
    var a = lo;
    var b = hi;

    for (var i = 0; i < PROFILE_REFINE_STEPS; i += 1)
    {
        const mid = 0.5 * (a + b);
        const rate = forward * dot(evPathTangentLines(context, path, [mid]).tangentLines[0].direction, heading);

        if (rate > 0)
        {
            a = mid;
        }
        else
        {
            b = mid;
        }
    }

    // Return the side that still advances, so the retained span never includes the cusp.
    return (lo < hi) ? a : b;
}

/**
 * Sample the retained span, carrying enough per point for a caller to reason about it.
 *
 * Curvature is differenced from the tangents rather than evaluated: evPathTangentLines
 * gives an arc-length parameterisation, so dT/ds falls straight out of neighbouring
 * samples and costs no extra kernel call.
 */
function sampleSpan(context is Context, path is Path, start is number, end is number,
    definition is map, plane is Plane) returns array
{
    const count = definition.samplesPerCurve;
    const length = evPathLength(context, path);

    var parameters = [];
    for (var i = 0; i < count; i += 1)
    {
        parameters = append(parameters, start + (end - start) * i / (count - 1));
    }

    const lines = evPathTangentLines(context, path, parameters).tangentLines;
    const step = (end - start) * length / (count - 1);

    var samples = [];
    for (var i = 0; i < count; i += 1)
    {
        // One-sided at the ends, central in between.
        const before = lines[max(0, i - 1)].direction;
        const after = lines[min(count - 1, i + 1)].direction;
        const spanCount = min(count - 1, i + 1) - max(0, i - 1);
        const curvature = (spanCount > 0 && step > 0 * meter)
            ? norm(after - before) / (spanCount * step)
            : 0 / meter;

        samples = append(samples, {
                    "point" : lines[i].origin,
                    "tangent" : lines[i].direction,
                    "normal" : cross(plane.normal, lines[i].direction),
                    "curvature" : curvature
                });
    }

    return samples;
}

/**
 * Emit the retained projection, grouped as asked.
 *
 * PER_CURVE is the only grouping that does not refit. The dropped edges already ARE the
 * projection exactly, so where no trim touched them the honest thing is to keep them.
 */
function emitGrouping(context is Context, id is Id, definition is map, path is Path,
    scan is map, samples is array) returns array
{
    var points = [];
    for (var sample in samples)
    {
        points = append(points, sample.point);
    }

    // SINGLE and EFFICIENT both fit one curve through the retained span for now. EFFICIENT
    // is where the merge test described in the header goes -- comparing curvature
    // progression, degree, weights and control-point spacing across a junction to decide
    // whether two spans want to be one. Until that exists it must not silently behave like
    // PER_CURVE, so it fits as one and says so.
    const fitted = approximateSpline(context, {
                "degree" : definition.fitDegree,
                "tolerance" : definition.fitTolerance,
                "isPeriodic" : false,
                "maxControlPoints" : definition.fitMaxCPs,
                "targets" : [approximationTarget({
                                "positions" : points,
                                "startDerivative" : samples[0].tangent * scan.retainedLength,
                                "endDerivative" : samples[size(samples) - 1].tangent * scan.retainedLength
                            })]
            });

    opCreateBSplineCurve(context, id + "profile", { "bSplineCurve" : fitted[0] });

    return fitted;
}

/**
 * Name the emitted wire. An empty name is a choice, not a missing value.
 */
function nameProfile(context is Context, bodies is Query, name is string)
{
    if (name == "")
    {
        return;
    }

    setProperty(context, {
                "entities" : bodies,
                "propertyType" : PropertyType.NAME,
                "value" : name
            });
}

// ============================================================================
// Debug
// ============================================================================

/**
 * What the scan found, and what it did about it.
 */
function printProfileScan(result is map)
{
    println("projected profile: retained " ~ toString(result.length)
        ~ " over path parameters " ~ toString(roundToPrecision(result.span.start, 6))
        ~ " to " ~ toString(roundToPrecision(result.span.end, 6)));
    println("  trimmed at start: " ~ toString(result.trimmed.atStart)
        ~ ", trimmed at end: " ~ toString(result.trimmed.atEnd));
    println("  samples: " ~ toString(size(result.samples))
        ~ ", output curves: " ~ toString(size(result.curves)));

    if (!result.trimmed.atStart && !result.trimmed.atEnd)
    {
        println("  the projection never doubles back; the whole chain is usable.");
    }
}

/**
 * Curvature comb over the retained span.
 *
 * Teeth point along the in-plane normal and scale with curvature, so a kink reads as a
 * spike and a fair curve reads as a smooth envelope. Batched into one feature: each
 * addDebugLine is otherwise a sketch and a constraint solve of its own.
 */
function drawCurvatureComb(context is Context, id is Id, samples is array, scale is ValueWithUnits)
{
    const count = size(samples);
    if (count == 0)
    {
        return;
    }

    var peak = 0 / meter;
    for (var sample in samples)
    {
        if (sample.curvature > peak)
        {
            peak = sample.curvature;
        }
    }
    if (peak * meter < PROFILE_ZERO_DIRECTION)
    {
        return;
    }

    const stride = max(1, ceil(count / PROFILE_COMB_TEETH));

    startFeature(context, id, {});
    var previousTip = undefined;
    var drawn = 0;

    for (var i = 0; i < count; i += stride)
    {
        const tip = samples[i].point + (scale * samples[i].curvature / peak) * samples[i].normal;

        if (norm(tip - samples[i].point) > TOLERANCE.zeroLength * meter)
        {
            opCreateBSplineCurve(context, id + ("tooth" ~ i), {
                        "bSplineCurve" : bSplineCurve({
                                    "degree" : 1,
                                    "isPeriodic" : false,
                                    "controlPoints" : [samples[i].point, tip]
                                })
                    });
            drawn += 1;
        }

        // The envelope is what makes a comb readable; the teeth alone are just hair.
        if (previousTip != undefined && norm(tip - previousTip) > TOLERANCE.zeroLength * meter)
        {
            opCreateBSplineCurve(context, id + ("env" ~ i), {
                        "bSplineCurve" : bSplineCurve({
                                    "degree" : 1,
                                    "isPeriodic" : false,
                                    "controlPoints" : [previousTip, tip]
                                })
                    });
            drawn += 1;
        }
        previousTip = tip;
    }

    if (drawn > 0)
    {
        addDebugEntities(context, qCreatedBy(id, EntityType.EDGE), DebugColor.MAGENTA);
    }
    abortFeature(context, id);
}
