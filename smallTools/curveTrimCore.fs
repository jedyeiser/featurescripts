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
 *   - performing cuts : splitWireWithPlanes
 *   - selecting result: keepSegmentNearestPoint, unionSegments
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
 * Cut `targetWire` with every plane in `planes` in a single opSplitPart and
 * return the resulting segment bodies. One construction plane is built per cut
 * and all are unioned as the split tool; keepTools is false so the planes are
 * consumed by the split.
 *
 * The planes are geometrically infinite, so a plane also cuts the wire anywhere
 * else the curve happens to recross it. Callers that need one specific segment
 * select it afterward (keepSegmentNearestPoint); callers that split at points
 * intrinsic to the curve (arc length, even division) normally want every piece.
 */
export function splitWireWithPlanes(context is Context, id is Id, targetWire is Query, planes is array) returns Query
{
    var planeBodies = [];
    for (var i = 0; i < size(planes); i += 1)
    {
        opPlane(context, id + ("cutPlane" ~ i), { "plane" : planes[i] });
        planeBodies = append(planeBodies, qCreatedBy(id + ("cutPlane" ~ i), EntityType.BODY));
    }

    opSplitPart(context, id + "split", {
        "targets" : targetWire,
        "tool" : qUnion(planeBodies),
        "keepTools" : false,
        "keepType" : SplitOperationKeepType.KEEP_ALL
    });

    return qCreatedBy(id + "split", EntityType.BODY);
}

// ============================================================================
// SELECTING THE RESULT
// ============================================================================

/**
 * Keep exactly one of the `segments` and delete the rest - the TRIM operation.
 * The kept segment is the one nearest `point` when `keepNearest` is true, or the
 * farthest when false. Distances are body-to-point via evDistance. Before the
 * discarded side is deleted it is highlighted in magenta (addDebugEntities), so
 * the removed portion shows during feature preview/edit like the stock trim. A
 * single-segment input is left untouched.
 */
export function keepSegmentByPoint(context is Context, id is Id, segments is Query, point is Vector, keepNearest is boolean)
{
    var segs = evaluateQuery(context, segments);
    if (size(segs) <= 1)
    {
        return;
    }

    var bestIdx = 0;
    var bestDist = evDistance(context, { "side0" : point, "side1" : segs[0] }).distance;
    for (var i = 1; i < size(segs); i += 1)
    {
        var di = evDistance(context, { "side0" : point, "side1" : segs[i] }).distance;
        if ((keepNearest && di < bestDist) || (!keepNearest && di > bestDist))
        {
            bestDist = di;
            bestIdx = i;
        }
    }

    var discard = qSubtraction(segments, segs[bestIdx]);
    if (!isQueryEmpty(context, discard))
    {
        // Show the side about to be removed in magenta, then delete it.
        addDebugEntities(context, discard, DebugColor.MAGENTA);
        opDeleteBodies(context, id + "discard", { "entities" : discard });
    }
}

/**
 * Merge the `segments` into a single wire body - the optional "return a single
 * wire" output of a SPLIT. opBoolean does NOT combine wires, so this re-extracts
 * all the segment edges into one fresh wire: the edges stay connected at the cut
 * vertices (only two meet per cut, so opExtractWires does not hit its ">2 edges
 * at a point" failure), and opExtractWires stitches coincident endpoints into a
 * single connected wire body. The original split segments are then deleted so
 * only the combined wire remains.
 */
export function unionSegments(context is Context, id is Id, segments is Query)
{
    opExtractWires(context, id + "singleWire", {
        "edges" : qOwnedByBody(segments, EntityType.EDGE)
    });
    opDeleteBodies(context, id + "removeSegments", { "entities" : segments });
}
