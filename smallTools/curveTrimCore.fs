FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

/**
 * curveTrimCore - shared engine for the betterCurveTrim feature.
 *
 * Trims and splits wire curves (single- or multi-edge) at arc-length locations. A
 * wire is read as one ordered path (constructPath); every cut location is a fraction
 * of the path length. Cuts are made exactly with opSplitEdges at arc-length
 * parameters (evEdge* / evDistance / opSplitEdges are all arc-length parameterized
 * by default), so no knot handling and no cutting planes - a plane would also cut the
 * curve anywhere else it crossed.
 *
 * The exported helpers are grouped as:
 *   - wire path       : wirePath, pathTangentAtFraction, pathPointAtFraction
 *   - locating cuts   : projectToPath, fractionOfPoint, fractionNearestEntity,
 *                       fractionAtSignedDistance, evenDivisionFractions,
 *                       spacedFromPointFractions
 *   - preview marks   : markCutPoints, markProjectionToCurve, markPositiveDirection
 *   - performing cuts : splitEdgesAtFractions (in place, one wire),
 *                       splitWireIntoPieces (separate wires), edgesBetweenCuts
 *   - selecting result: keepPieceRange / keepOnePiece
 */

// A picked point closer than this to the curve is treated as already "on" it, so
// no projection line is drawn.
export const CTC_ON_CURVE_TOL = 1e-5 * meter;

// Keep cut fractions strictly inside the curve so a cut never lands exactly on
// an endpoint (which produces a zero-length segment or no split at all).
export const CTC_FRACTION_EPS = 1e-6;

// Number of equal segments for the even-division mode (this many pieces, so
// this-minus-one interior cuts).
export const CTC_DIVISION_BOUNDS =
{
    (unitless) : [2, 4, 100]
} as IntegerBoundSpec;

// Fixed spacing between cuts for the "every distance from point" division style.
export const CTC_SPACING_BOUNDS =
{
    (millimeter) : [0.1, 10, 1000]
} as LengthBoundSpec;

// Number of equally spaced cuts for the "every distance from point" style.
export const CTC_SPACING_COUNT_BOUNDS =
{
    (unitless) : [1, 5, 100]
} as IntegerBoundSpec;

// Inflection solver: samples for the curvature sign-change scan, and the
// fraction-space tolerance each root is refined to.
export const CTC_INFLECTION_SAMPLES = 200;
export const CTC_INFLECTION_TOL = 1e-5;

// Skip the extreme ends of the curve when scanning for inflections: the curvature
// frame is unreliable at the exact endpoints and reports phantom inflections there
// (a real inflection this close to an end is not meaningful). Fraction of the
// curve ignored at each end - increase if phantom near-end inflections persist.
export const CTC_INFLECTION_END_MARGIN = 1e-3;

// Bound for the hidden chosen-inflection index items (0-based into the solved
// inflection list; "none selected" is simply the empty array).
export const CTC_INFLECTION_INDEX_BOUNDS =
{
    (unitless) : [0, 0, 100000]
} as IntegerBoundSpec;

// ============================================================================
// WIRE PATH (arc-length bookkeeping over a single- or multi-edge wire)
// ============================================================================

/**
 * A wire's edges as one ordered path (constructPath: the first edge's own direction
 * is "forward"), with each edge's arc length and the arc length at which it starts.
 * Every cut "fraction" in this module is a fraction of this path's total length, so a
 * multi-edge wire behaves exactly like a single edge.
 *
 * @returns {{
 *      @field path {Path}
 *      @field lengths {array} : Arc length of each path edge.
 *      @field starts {array} : Path arc length at the start of each edge.
 *      @field total {ValueWithUnits} : Path length.
 * }}
 */
export function wirePath(context is Context, wire is Query) returns map
{
    return wirePathInfo(context, constructPath(context, qOwnedByBody(wire, EntityType.EDGE)));
}

function wirePathInfo(context is Context, path is Path) returns map
{
    var lengths = [];
    var starts = [];
    var total = 0 * meter;
    for (var e in path.edges)
    {
        var len = evLength(context, { "entities" : e });
        starts = append(starts, total);
        lengths = append(lengths, len);
        total += len;
    }
    return { "path" : path, "lengths" : lengths, "starts" : starts, "total" : total };
}

/**
 * The wire's path re-read after its edges were split, oriented to start at
 * `startPoint` (the old path's start) so piece order stays start->end.
 */
function wirePathFrom(context is Context, wire is Query, startPoint is Vector) returns map
{
    var path = constructPath(context, qOwnedByBody(wire, EntityType.EDGE));
    var wp = wirePathInfo(context, path);
    if (!tolerantEquals(pathPointAtFraction(context, wp, 0), startPoint))
    {
        wp = wirePathInfo(context, reverse(path));
    }
    return wp;
}

/** Path fraction of arc-length parameter `edgeParameter` on path edge `index`. */
function pathFractionOnEdge(wp is map, index is number, edgeParameter is number) returns number
{
    var local = wp.path.flipped[index] ? 1 - edgeParameter : edgeParameter;
    return (wp.starts[index] + local * wp.lengths[index]) / wp.total;
}

/** The path edge holding path fraction `f`, and the arc-length parameter on that edge. */
function edgeAtFraction(wp is map, f is number) returns map
{
    var s = f * wp.total;
    var n = size(wp.path.edges);
    var index = n - 1;
    for (var i = 0; i < n; i += 1)
    {
        if (s <= wp.starts[i] + wp.lengths[i])
        {
            index = i;
            break;
        }
    }
    var local = max(0, min(1, (s - wp.starts[index]) / wp.lengths[index]));
    return { "index" : index, "parameter" : wp.path.flipped[index] ? 1 - local : local };
}

/** Tangent line at path fraction `f`, its direction along the path. */
export function pathTangentAtFraction(context is Context, wp is map, f is number) returns Line
{
    var at = edgeAtFraction(wp, f);
    var tl = evEdgeTangentLine(context, { "edge" : wp.path.edges[at.index], "parameter" : at.parameter });
    if (wp.path.flipped[at.index])
    {
        tl.direction = -tl.direction;
    }
    return tl;
}

export function pathPointAtFraction(context is Context, wp is map, f is number) returns Vector
{
    return pathTangentAtFraction(context, wp, f).origin;
}

// ============================================================================
// LOCATING CUTS (all return a path fraction in 0..1)
// ============================================================================

/**
 * The point on the wire closest to `worldPoint`: its path `fraction`, the on-curve
 * `point` and the `distance` off the curve. Edges are measured one at a time (evDistance
 * to a query of several edges does not say which one it used).
 */
export function projectToPath(context is Context, wp is map, worldPoint is Vector) returns map
{
    var best = undefined;
    for (var i = 0; i < size(wp.path.edges); i += 1)
    {
        var d = evDistance(context, { "side0" : worldPoint, "side1" : wp.path.edges[i] });
        if (best == undefined || d.distance < best.distance - TOLERANCE.zeroLength * meter)
        {
            best = { "fraction" : pathFractionOnEdge(wp, i, d.sides[1].parameter), "point" : d.sides[1].point, "distance" : d.distance };
        }
    }
    return best;
}

export function fractionOfPoint(context is Context, wp is map, worldPoint is Vector) returns number
{
    return projectToPath(context, wp, worldPoint).fraction;
}

/**
 * Path fraction where the wire is nearest to (or crosses) `toolEntity`. evDistance
 * yields a single extremum per edge, so this locates one boundary per call.
 */
export function fractionNearestEntity(context is Context, wp is map, toolEntity is Query) returns number
{
    var best = undefined;
    for (var i = 0; i < size(wp.path.edges); i += 1)
    {
        var d = evDistance(context, { "side0" : wp.path.edges[i], "side1" : toolEntity });
        if (best == undefined || d.distance < best.distance - TOLERANCE.zeroLength * meter)
        {
            best = { "fraction" : pathFractionOnEdge(wp, i, d.sides[0].parameter), "distance" : d.distance };
        }
    }
    return best.fraction;
}

/**
 * Cut fraction a signed arc-length `dist` from path fraction `f0` in direction `dir`
 * (+1 along the path, -1 against it). Not clamped - a cut past an end is dropped by
 * the caller.
 */
export function fractionAtSignedDistance(wp is map, f0 is number, dist is ValueWithUnits, dir is number) returns number
{
    return f0 + dir * dist / wp.total;
}

/**
 * Cut fractions dividing the span between `pointA` and `pointB` into `numParts`
 * equal arc-length pieces. The two endpoints are included as cuts (so the picked
 * points themselves are split, not just the interior divisions) - k runs 0 to
 * numParts inclusive. Any cut that lands on a curve end is dropped later by
 * cleanFractions. The two points define the span in either order.
 */
export function evenDivisionFractions(context is Context, wp is map, pointA is Vector, pointB is Vector, numParts is number) returns array
{
    var fA = fractionOfPoint(context, wp, pointA);
    var fB = fractionOfPoint(context, wp, pointB);
    var lo = min([fA, fB]);
    var hi = max([fA, fB]);

    var fractions = [];
    for (var k = 0; k <= numParts; k += 1)
    {
        fractions = append(fractions, lo + (hi - lo) * k / numParts);
    }
    return fractions;
}

/**
 * Direction sign (+1 along the path, -1 against it) for the "every distance from
 * point" style: toward the curve midpoint by default, `reverse` flips it.
 */
export function spacingDirection(f0 is number, reverse is boolean) returns number
{
    var dir = (f0 <= 0.5) ? 1 : -1;
    return reverse ? -dir : dir;
}

/**
 * Fractions for `count` cuts spaced `spacing` apart along the curve from path
 * fraction `f0` (cuts at spacing, 2*spacing, ... count*spacing) in direction `dir`
 * (+1 / -1, see [spacingDirection]). Fractions past an end are dropped by the caller.
 */
export function spacedFromPointFractions(wp is map, f0 is number, spacing is ValueWithUnits, count is number, dir is number) returns array
{
    var df = spacing / wp.total;
    var fractions = [];
    for (var k = 1; k <= count; k += 1)
    {
        fractions = append(fractions, f0 + dir * k * df);
    }
    return fractions;
}

// ============================================================================
// PREVIEW MARKS
// ============================================================================

/**
 * Mark each cut location with a red debug point so the user can preview where the
 * curve will be cut (visible while the edit dialog is open).
 */
export function markCutPoints(context is Context, points is array)
{
    for (var pt in points)
    {
        addDebugPoint(context, pt, DebugColor.RED);
    }
}

/**
 * If `worldPoint` is off the curve, draw a dashed red line from it to the point
 * on the curve the measurement is referenced from (its projection). addDebugLine
 * has no dotted style, so the dashes are a series of short segments. Nothing is
 * drawn when the point already lies on the curve.
 */
export function markProjectionToCurve(context is Context, wp is map, worldPoint is Vector)
{
    var proj = projectToPath(context, wp, worldPoint);
    if (proj.distance <= CTC_ON_CURVE_TOL)
    {
        return;
    }

    var onCurve = proj.point;
    var nDashes = 12;
    for (var i = 0; i < nDashes; i += 1)
    {
        var p0 = worldPoint + (onCurve - worldPoint) * (i / nDashes);
        var p1 = worldPoint + (onCurve - worldPoint) * ((i + 0.5) / nDashes);
        addDebugLine(context, p0, p1, DebugColor.RED);
    }
}

/**
 * Blue arrow at path fraction `f0` pointing the way positive distances run (`dir` +1
 * along the path, -1 against it): 5% of the curve length, at most 50 mm.
 */
export function markPositiveDirection(context is Context, wp is map, f0 is number, dir is number)
{
    var tl = pathTangentAtFraction(context, wp, max(0, min(1, f0)));
    var len = min(0.05 * wp.total, 50 * millimeter);
    addDebugArrow(context, tl.origin, tl.origin + dir * len * tl.direction, len / 10, DebugColor.BLUE);
}

// ============================================================================
// PERFORMING CUTS
// ============================================================================

/**
 * Split the wire's edges in place at every path fraction in `fractions` (opSplitEdges
 * at arc-length parameters): the wire stays ONE body, now with a vertex at each cut.
 * A cut on an existing vertex needs no split. Exact - no cutting planes, so a curve
 * that doubles back is only cut where asked.
 */
export function splitEdgesAtFractions(context is Context, id is Id, wp is map, fractions is array)
{
    var perEdge = makeArray(size(wp.path.edges), []);
    for (var f in fractions)
    {
        var at = edgeAtFraction(wp, f);
        if (at.parameter > CTC_FRACTION_EPS && at.parameter < 1 - CTC_FRACTION_EPS)
        {
            perEdge[at.index] = append(perEdge[at.index], at.parameter);
        }
    }
    for (var i = 0; i < size(perEdge); i += 1)
    {
        if (size(perEdge[i]) > 0)
        {
            opSplitEdges(context, id + ("splitEdge" ~ i), {
                        "edges" : wp.path.edges[i],
                        "parameters" : [sort(perEdge[i], function(a, b) { return a - b; })]
                    });
        }
    }
}

/**
 * After [splitEdgesAtFractions]: the wire's edges grouped into the spans between cuts,
 * one array of edge queries per span (fractions.size + 1 groups, start to end; a group
 * is empty when two cuts coincide). `start` is the wire's start point before the split.
 */
export function edgesBetweenCuts(context is Context, wire is Query, start is Vector, fractions is array) returns array
{
    var after = wirePathFrom(context, wire, start);
    var groups = makeArray(size(fractions) + 1, []);
    for (var i = 0; i < size(after.path.edges); i += 1)
    {
        var mid = (after.starts[i] + after.lengths[i] / 2) / after.total;
        var k = 0;
        for (var f in fractions)
        {
            if (f < mid)
            {
                k += 1;
            }
        }
        groups[k] = append(groups[k], after.path.edges[i]);
    }
    return groups;
}

/**
 * Split `wire` at every path fraction in `fractions` (ascending) into separate wire
 * bodies, returned as an array of body queries ordered start to end. The edges are split
 * in place, grouped between cuts, and each group extracted as its own wire; the original
 * wire is then deleted. With no effective cut the array is just [wire].
 */
export function splitWireIntoPieces(context is Context, id is Id, wire is Query, wp is map, fractions is array) returns array
{
    var start = pathPointAtFraction(context, wp, 0);
    splitEdgesAtFractions(context, id, wp, fractions);
    var groups = edgesBetweenCuts(context, wire, start, fractions);

    var nonEmpty = 0;
    for (var g in groups)
    {
        if (size(g) > 0)
        {
            nonEmpty += 1;
        }
    }
    if (nonEmpty < 2)
    {
        return [wire];
    }

    var pieces = [];
    for (var k = 0; k < size(groups); k += 1)
    {
        if (size(groups[k]) > 0)
        {
            opExtractWires(context, id + ("piece" ~ k), { "edges" : qUnion(groups[k]) });
            pieces = append(pieces, qCreatedBy(id + ("piece" ~ k), EntityType.BODY));
        }
    }
    opDeleteBodies(context, id + "deleteSource", { "entities" : wire });
    return pieces;
}

// ============================================================================
// SELECTING THE RESULT
// ============================================================================

/**
 * TRIM: keep the contiguous ordered pieces keepLo..keepHi (inclusive) and delete
 * the rest. Discarded pieces are traced in magenta (shown while the edit dialog is
 * open, like the stock trim) just before deletion. Deletes nothing when the whole
 * array is inside the kept span. Callers must pass keepLo <= keepHi.
 */
export function keepPieceRange(context is Context, id is Id, pieces is array, keepLo is number, keepHi is number)
{
    var discard = [];
    for (var i = 0; i < size(pieces); i += 1)
    {
        if (i < keepLo || i > keepHi)
        {
            discard = append(discard, pieces[i]);
        }
    }
    if (size(discard) == 0)
    {
        return;
    }

    var discardQ = qUnion(discard);
    if (!isQueryEmpty(context, discardQ))
    {
        highlightRemovedSegment(context, discardQ);
        opDeleteBodies(context, id + "discard", { "entities" : discardQ });
    }
}

/**
 * TRIM: keep exactly one of the ordered `pieces` (by index), delete the rest.
 * Thin wrapper over keepPieceRange for the single-cut case. Nothing is removed
 * when there is only one piece (a grazing cut that did not divide the wire).
 */
export function keepOnePiece(context is Context, id is Id, pieces is array, keepIndex is number)
{
    keepPieceRange(context, id, pieces, keepIndex, keepIndex);
}

/**
 * Trace each edge of `bodies` as a magenta polyline sampled at equal arc-length
 * steps. addDebugEntities stores a QUERY re-evaluated at render time, so it would
 * draw nothing once the body is deleted; addDebugLine stores coordinates, which
 * survive the delete - so the removed side stays visible during the edit.
 */
function highlightRemovedSegment(context is Context, bodies is Query)
{
    var nSamples = 24;
    var edges = evaluateQuery(context, qOwnedByBody(bodies, EntityType.EDGE));
    for (var e = 0; e < size(edges); e += 1)
    {
        var prev = undefined;
        for (var s = 0; s <= nSamples; s += 1)
        {
            var pt = evEdgeTangentLine(context, { "edge" : edges[e], "parameter" : s / nSamples }).origin;
            if (prev != undefined)
            {
                addDebugLine(context, prev, pt, DebugColor.MAGENTA);
            }
            prev = pt;
        }
    }
}

// ============================================================================
// INFLECTION SOLVING
// ============================================================================

/**
 * Arc-length fractions (0..1) of the curve's inflection points - where signed
 * curvature crosses zero. evEdgeCurvature returns UNSIGNED curvature (it only dips
 * toward zero, never flips), so we sign it by the curvature binormal against the
 * curve's plane normal - which is why the curve must be planar. The parameter is
 * arc-length fraction, the same space the cutter uses, so the returned fractions
 * feed straight into the cut path.
 *
 * Method: sample signed curvature across the curve interior (the exact endpoints
 * are skipped - see CTC_INFLECTION_END_MARGIN) in one evEdgeCurvatures call, then
 * bisect each sign-change bracket. Throws a clear error on a non-planar curve.
 */
export function solveInflectionFractions(context is Context, edge is Query) returns array
{
    var pl = planeOfEdge(context, edge);
    var n = pl.normal;

    // Scan the interior only - skip the degenerate curvature frames at the exact
    // endpoints, which otherwise report phantom near-end inflections that then get
    // picked as the "nearest each end" cut.
    var lo = CTC_INFLECTION_END_MARGIN;
    var hi = 1 - CTC_INFLECTION_END_MARGIN;
    var params = [];
    for (var i = 0; i <= CTC_INFLECTION_SAMPLES; i += 1)
    {
        params = append(params, lo + (hi - lo) * i / CTC_INFLECTION_SAMPLES);
    }

    var results = evEdgeCurvatures(context, { "edge" : edge, "parameters" : params });
    var signed = [];
    for (var i = 0; i < size(results); i += 1)
    {
        signed = append(signed, signedFromResult(results[i], n));
    }

    var roots = [];
    for (var i = 1; i < size(signed); i += 1)
    {
        if (signed[i - 1] * signed[i] < 0)
        {
            roots = append(roots, bisectInflection(context, edge, n, params[i - 1], params[i], signed[i - 1]));
        }
    }
    return roots;
}

/**
 * The curve's plane, converted from a raw evPlanarEdge failure into an actionable
 * message. This is a deliberate (rare) use of try/catch: turn a cryptic geometry
 * throw into "the curve must be planar" for the inflection mode.
 */
function planeOfEdge(context is Context, edge is Query) returns Plane
{
    try
    {
        return evPlanarEdge(context, { "edge" : edge });
    }
    catch (e)
    {
        throw regenError("Inflection cutting requires a planar curve", ["curves"]);
    }
}

/**
 * Signed curvature (unitless per-meter value) from a curvature result: the
 * unsigned magnitude times the sign of the Frenet binormal along the plane
 * normal. The curvature frame has zAxis = tangent and xAxis = normal, so the
 * binormal is cross(tangent, normal); for a planar curve it is +/- the plane
 * normal and flips sign across an inflection.
 */
function signedFromResult(cr is map, planeNormal is Vector) returns number
{
    var binormal = cross(cr.frame.zAxis, cr.frame.xAxis);
    var sgn = (dot(binormal, planeNormal) >= 0) ? 1 : -1;
    return cr.curvature.value * sgn;
}

/**
 * Signed curvature at a single arc-length fraction (used while refining a root).
 */
function signedCurvatureAt(context is Context, edge is Query, s is number, planeNormal is Vector) returns number
{
    return signedFromResult(evEdgeCurvature(context, { "edge" : edge, "parameter" : s }), planeNormal);
}

/**
 * Bisect a signed-curvature sign-change bracket [a,b] (ka = signed curvature at a)
 * down to CTC_INFLECTION_TOL in fraction space, returning the inflection fraction.
 */
function bisectInflection(context is Context, edge is Query, planeNormal is Vector, a is number, b is number, ka is number) returns number
{
    var lo = a;
    var hi = b;
    var klo = ka;
    for (var iter = 0; iter < 60; iter += 1)
    {
        if (hi - lo < CTC_INFLECTION_TOL)
        {
            break;
        }
        var mid = (lo + hi) / 2;
        var km = signedCurvatureAt(context, edge, mid, planeNormal);
        if (klo * km <= 0)
        {
            hi = mid;
        }
        else
        {
            lo = mid;
            klo = km;
        }
    }
    return (lo + hi) / 2;
}
