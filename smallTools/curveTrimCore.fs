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
 *   - performing cuts : splitWireIntoPieces (iterative, returns ordered pieces)
 *   - selecting result: keepOnePiece, combineSplitToSingleWire
 */

// A resulting segment shorter than this is treated as a degenerate zero-length
// offcut (e.g. a cut landing on an endpoint) and should be filtered by callers.
export const CTC_MIN_SEGMENT = 1e-5 * meter;

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

/**
 * Fractions for `count` cuts spaced `spacing` apart along the curve, starting one
 * step from `fromPoint` (cuts at distance spacing, 2*spacing, ... count*spacing).
 * By default the cuts run toward the curve midpoint so they stay on the curve;
 * `reverse` flips that direction. Fractions that fall past an end are dropped by
 * the caller (cleanFractions). spacing / length is unitless, so it scales the
 * fraction directly.
 */
export function spacedFromPointFractions(context is Context, edge is Query, fromPoint is Vector, spacing is ValueWithUnits, count is number, reverse is boolean) returns array
{
    var f0 = fractionOfPointOnEdge(context, edge, fromPoint);
    var df = spacing / evLength(context, { "entities" : edge });
    var dir = (f0 <= 0.5) ? 1 : -1;
    if (reverse)
    {
        dir = -dir;
    }

    var fractions = [];
    for (var k = 1; k <= count; k += 1)
    {
        fractions = append(fractions, f0 + dir * k * df);
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

/**
 * Mark each cut location with a red debug point so the user can preview where the
 * curve will be cut (visible while the edit dialog is open). The cut plane origin
 * sits on the curve at its cut fraction, so it doubles as the cut point.
 */
export function markCutPoints(context is Context, planes is array)
{
    for (var i = 0; i < size(planes); i += 1)
    {
        addDebugPoint(context, planes[i].origin, DebugColor.RED);
    }
}

/**
 * If `worldPoint` is off the curve, draw a dashed red line from it to the point
 * on the curve the measurement is referenced from (its projection). addDebugLine
 * has no dotted style, so the dashes are a series of short segments. Nothing is
 * drawn when the point already lies on the curve.
 */
export function markProjectionToCurve(context is Context, edge is Query, worldPoint is Vector)
{
    var d = evDistance(context, { "side0" : worldPoint, "side1" : edge });
    if (d.distance <= CTC_ON_CURVE_TOL)
    {
        return;
    }

    var onCurve = d.sides[1].point;
    var nDashes = 12;
    for (var i = 0; i < nDashes; i += 1)
    {
        var p0 = worldPoint + (onCurve - worldPoint) * (i / nDashes);
        var p1 = worldPoint + (onCurve - worldPoint) * ((i + 0.5) / nDashes);
        addDebugLine(context, p0, p1, DebugColor.RED);
    }
}

// ============================================================================
// PERFORMING CUTS
// ============================================================================

/**
 * Split `wire` at every plane in `planes` and return the resulting pieces as an
 * array ordered along the curve (start to end). A single opSplitPart cannot cut a
 * wire at several planes at once - it yields only one front/back partition, so
 * with N planes it produces 2 pieces, not N+1. We therefore split iteratively:
 * each pass cuts the current remainder with ONE plane, finalizes the back piece
 * (smaller arc length) and carries the front remainder forward to the next plane.
 * Every pass is a single-plane split, so qSplitBy's front/back stays unambiguous.
 *
 * `planes` must be ordered along the curve (the caller sorts the cut fractions),
 * and each plane's normal is the curve tangent, so its "front" is the larger-arc-
 * length side. Each construction plane is deleted after its split (opSplitPart
 * does not remove plane tools itself).
 */
export function splitWireIntoPieces(context is Context, id is Id, wire is Query, planes is array) returns array
{
    var pieces = [];
    var remainder = wire;

    for (var i = 0; i < size(planes); i += 1)
    {
        opPlane(context, id + ("cutPlane" ~ i), { "plane" : planes[i] });
        var planeBody = qCreatedBy(id + ("cutPlane" ~ i), EntityType.BODY);

        var splitId = id + ("split" ~ i);
        opSplitPart(context, splitId, {
            "targets" : remainder,
            "tool" : planeBody,
            "keepTools" : true,
            "keepType" : SplitOperationKeepType.KEEP_ALL
        });

        opDeleteBodies(context, id + ("deletePlane" ~ i), { "entities" : planeBody });

        // Back side (smaller arc length) is done; front side is cut by the next
        // plane, which lands within it because the planes ascend along the curve.
        pieces = append(pieces, qSplitBy(splitId, EntityType.BODY, true));
        remainder = qSplitBy(splitId, EntityType.BODY, false);
    }

    pieces = append(pieces, remainder);
    return pieces;
}

// ============================================================================
// SELECTING THE RESULT
// ============================================================================

/**
 * TRIM: keep exactly one of the ordered `pieces` (by index) and delete the rest.
 * The discarded pieces are traced in magenta (shown while the edit dialog is open,
 * like the stock trim) just before deletion. Nothing is removed when there is only
 * one piece (a grazing/near-endpoint cut that did not divide the wire).
 */
export function keepOnePiece(context is Context, id is Id, pieces is array, keepIndex is number)
{
    var discard = [];
    for (var i = 0; i < size(pieces); i += 1)
    {
        if (i != keepIndex)
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
 * SPLIT "return single wire": recombine the `pieces` into one wire body. opBoolean
 * does NOT combine wires, so all piece edges are re-extracted into one connected
 * wire with opExtractWires (edges stay joined at the cut vertices, only two per
 * cut, so no ">2 edges at a point" failure), then the pieces are deleted. The
 * pieces come straight from splitWireIntoPieces, so there is no created-vs-modified
 * guessing; the empty guard just avoids opExtractWires' cryptic error if the split
 * produced nothing.
 */
export function combineSplitToSingleWire(context is Context, id is Id, pieces is array)
{
    var all = qUnion(pieces);
    var edges = qOwnedByBody(all, EntityType.EDGE);
    if (isQueryEmpty(context, edges))
    {
        return;
    }

    opExtractWires(context, id + "singleWire", { "edges" : edges });
    opDeleteBodies(context, id + "removeSegments", { "entities" : all });
}
