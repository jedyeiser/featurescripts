FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

/**
 * curveTrimCore - shared engine for the betterCurveTrim feature.
 *
 * Trims and splits wire curves by cutting them with construction planes built
 * perpendicular to the curve at each cut location, then letting opSplitPart do
 * the intersection. This delegates all cutting math to the kernel, so we never
 * touch BSpline knot vectors or parameter/arc-length conversion: evEdge* and
 * evDistance are already arc-length parameterized (their curve "parameter" is
 * normalized 0..1 arc length), which is all we need to place cuts and read
 * tangents.
 *
 * The exported helpers are grouped as:
 *   - locating cuts   : fractionOfPointOnEdge, fractionNearestEntity,
 *                       fractionAtDistanceTowardMid, evenDivisionFractions
 *   - building cuts   : cutPlaneAtFraction
 *   - performing cuts : cutWireWithPlanes (returns the split Id, for qSplitBy)
 *   - selecting result: keepSplitSide, combineSplitToSingleWire
 */

// A resulting segment shorter than this is treated as a degenerate zero-length
// offcut (e.g. a cut landing on an endpoint) and should be filtered by callers.
export const CTC_MIN_SEGMENT = 1e-5 * meter;

// Keep cut fractions strictly inside the curve so a cut never lands exactly on
// an endpoint (which produces a zero-length segment or no split at all).
export const CTC_FRACTION_EPS = 1e-6;

// Number of equal segments for the even-division mode (this many pieces, so
// this-minus-one interior cuts).
export const CTC_DIVISION_BOUNDS =
{
    (unitless) : [2, 4, 100]
} as IntegerBoundSpec;

// ============================================================================
// LOCATING CUTS (all return an arc-length fraction in 0..1 on the edge)
// ============================================================================

/**
 * Arc-length fraction of the point on `edge` closest to `worldPoint`. evDistance
 * reports the edge-side parameter as normalized arc length by default, so this
 * is exactly "how far along the curve" the projected point sits.
 */
export function fractionOfPointOnEdge(context is Context, edge is Query, worldPoint is Vector) returns number
{
    var d = evDistance(context, { "side0" : worldPoint, "side1" : edge });
    return d.sides[1].parameter;
}

/**
 * Arc-length fraction on `edge` where it is nearest to (or crosses) `toolEntity`.
 * For a true intersection the distance is ~0 and this is the crossing point.
 * evDistance yields a single extremum, so this locates one boundary per call.
 */
export function fractionNearestEntity(context is Context, edge is Query, toolEntity is Query) returns number
{
    var d = evDistance(context, { "side0" : edge, "side1" : toolEntity });
    return d.sides[0].parameter;
}

/**
 * Cut fraction a blind arc-length `dist` from `fromPoint`, measured toward the
 * curve midpoint (fraction 0.5). The result is clamped just inside the curve.
 * dist / length is unitless, so it adds directly to the fraction.
 */
export function fractionAtDistanceTowardMid(context is Context, edge is Query, fromPoint is Vector, dist is ValueWithUnits) returns number
{
    var f0 = fractionOfPointOnEdge(context, edge, fromPoint);
    var df = dist / evLength(context, { "entities" : edge });
    var signed = (f0 <= 0.5) ? (f0 + df) : (f0 - df);
    return max(CTC_FRACTION_EPS, min(1 - CTC_FRACTION_EPS, signed));
}

/**
 * Interior cut fractions that divide the span between `pointA` and `pointB` into
 * `numParts` equal arc-length pieces - i.e. numParts - 1 cuts. The two points
 * define the span in either order.
 */
export function evenDivisionFractions(context is Context, edge is Query, pointA is Vector, pointB is Vector, numParts is number) returns array
{
    var fA = fractionOfPointOnEdge(context, edge, pointA);
    var fB = fractionOfPointOnEdge(context, edge, pointB);
    var lo = min([fA, fB]);
    var hi = max([fA, fB]);

    var fractions = [];
    for (var k = 1; k < numParts; k += 1)
    {
        fractions = append(fractions, lo + (hi - lo) * k / numParts);
    }
    return fractions;
}

// ============================================================================
// BUILDING CUTS
// ============================================================================

/**
 * Plane through `edge` at arc-length fraction `f`, with its normal along the
 * curve tangent there - so the plane cuts the curve perpendicularly at that
 * point. evEdgeTangentLine is arc-length parameterized, so `f` maps directly to
 * fractional length along the edge.
 */
export function cutPlaneAtFraction(context is Context, edge is Query, f is number) returns Plane
{
    var tl = evEdgeTangentLine(context, { "edge" : edge, "parameter" : f });
    return plane(tl.origin, tl.direction);
}

// ============================================================================
// PERFORMING CUTS
// ============================================================================

/**
 * Cut `wire` with every plane in `planes` in a single opSplitPart and return the
 * split feature Id. Callers read the resulting sides with qSplitBy(splitId, ...):
 * opSplitPart on a wire does NOT register the pieces under qCreatedBy, so
 * qSplitBy - front = the tool-normal (curve tangent) side, back = the opposite -
 * is the correct way to get them.
 *
 * keepTools is true so the construction planes remain ours to remove: opSplitPart
 * does not delete plane tools on its own, so they are explicitly deleted here,
 * leaving only the curve pieces. The planes are geometrically infinite, so a
 * plane also cuts the wire anywhere else the curve recrosses it; intrinsic-cut
 * callers (arc length, even division) want every piece anyway, and TRIM selects
 * a side from the split rather than a specific segment.
 */
export function cutWireWithPlanes(context is Context, id is Id, wire is Query, planes is array) returns Id
{
    var planeBodies = [];
    for (var i = 0; i < size(planes); i += 1)
    {
        opPlane(context, id + ("cutPlane" ~ i), { "plane" : planes[i] });
        planeBodies = append(planeBodies, qCreatedBy(id + ("cutPlane" ~ i), EntityType.BODY));
    }

    var splitId = id + "split";
    opSplitPart(context, splitId, {
        "targets" : wire,
        "tool" : qUnion(planeBodies),
        "keepTools" : true,
        "keepType" : SplitOperationKeepType.KEEP_ALL
    });

    var cutPlanes = qUnion(planeBodies);
    if (!isQueryEmpty(context, cutPlanes))
    {
        opDeleteBodies(context, id + "deletePlanes", { "entities" : cutPlanes });
    }

    return splitId;
}

// ============================================================================
// SELECTING THE RESULT
// ============================================================================

/**
 * TRIM: keep one side of a single-plane split and delete the other. The sides
 * come from qSplitBy on the split feature - front is the tool-normal (curve
 * tangent) side, back is the opposite. `keepBackSide` chooses which to keep; the
 * discarded side is traced in magenta (shown while the edit dialog is open, like
 * the stock trim) just before it is deleted. If the cut produced only one side
 * (a grazing/near-endpoint cut) the discard query is empty and nothing is removed.
 */
export function keepSplitSide(context is Context, id is Id, splitId is Id, keepBackSide is boolean)
{
    var discard = qSplitBy(splitId, EntityType.BODY, !keepBackSide);
    if (!isQueryEmpty(context, discard))
    {
        highlightRemovedSegment(context, discard);
        opDeleteBodies(context, id + "discard", { "entities" : discard });
    }
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

/**
 * SPLIT "return single wire": recombine every piece of the split into one wire
 * body. opBoolean does NOT combine wires, so all piece edges are re-extracted
 * into one connected wire with opExtractWires (edges stay joined at the cut
 * vertices, only two per cut, so no ">2 edges at a point" failure), then the
 * pieces are deleted. Pieces = the survivor that kept the original wire's
 * identity (modified, so absent from qCreatedBy) plus the new offcuts (created by
 * the split), so the original `wire` is unioned with qCreatedBy(splitId).
 */
export function combineSplitToSingleWire(context is Context, id is Id, wire is Query, splitId is Id)
{
    var pieces = qUnion([wire, qCreatedBy(splitId, EntityType.BODY)]);

    opExtractWires(context, id + "singleWire", { "edges" : qOwnedByBody(pieces, EntityType.EDGE) });
    opDeleteBodies(context, id + "removeSegments", { "entities" : pieces });
}
