FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

/**
 * Curve core: chains, stations, point classification, fitting, curve emitters and console
 * formatting shared by the curve tools (Clean wire, Map curve) and, through
 * edge_offset_utils (driven_offset document, imports this tab by Curve_tools version), by
 * Driven edge offset and Driven offset surface.
 *
 * Moved verbatim from edge_offset_utils.fs on 2026-09-23 -- the closure of what the curve
 * tools use, every symbol of which the offset features use too. Names keep their "offset"
 * flavour (OFFSET_GEOM_TOL, OffsetPointSpacing, drivenOffsetApproximationPredicate) so saved
 * parameter ids and every caller stay valid.
 *
 * Vocabulary
 *   CHAIN     an ordered set of G0-connected LINKS; a link is one Path.
 *             Chains are oriented so increasing arc length means increasing world X.
 *   STATION   one evaluation point on a chain, carrying its frame and coordinate.
 *   FRAME     tangent + width axis + height axis.
 *
 * Kernel calls are the runtime driver, so every evaluation here is batched: one
 * ev* call per edge with an array of parameters, never one call per point.
 */

// ============================================================================
// Constants
// ============================================================================

/**
 * Below this curvature the kernel's Frenet normal is documented as arbitrary
 * (see evEdgeCurvatures), so the frame normal is parallel-transported instead.
 */
export const ZERO_CURVATURE = 1e-3 / meter;

/** Position tolerance for geometric classification (line / arc detection). */
export const OFFSET_GEOM_TOL = 1e-6 * meter;

/**
 * How far out of its own plane a run may sit and still be called an arc.
 *
 * Separate from, and far tighter than, the fitting tolerance -- because it answers a
 * different question. Radial error is a fit residual: sampled points scatter either side of
 * the true radius and a micron of it is meaningless. Out-of-plane deviation is not scatter.
 * A circle is planar by definition, so a genuine arc's points are planar to numerical noise,
 * and a systematic bow means the curve is not an arc no matter how small the bow is.
 *
 * The case this exists for: source edges that are splines produced by wrapping arcs onto a
 * gently curved surface. They are within a hundredth of a millimetre of circular and were
 * being emitted as true arcs, which discards the wrap. Measured against one combined
 * distance budget the bow hid inside the radial allowance; measured separately it does not.
 */
export const ARC_PLANARITY_TOL = 1e-7 * meter;

/**
 * How well a circle must fit, as a fraction of the run's own sagitta, to be called an arc.
 *
 * An absolute tolerance cannot answer this. Every smooth curve is locally circular, so over a
 * short enough span ANY curve fits a circle to within any tolerance you like -- and the
 * shallower the span, the more meaningless the radius that comes out. Measured on a 42 mm run
 * of a wrapped spline: sagitta 12.7 um against a 10 um fitting tolerance, so it missed being a
 * line by 2.7 um and then passed the arc test trivially. Three runs of the same shape came out
 * at R = 17485.26, 17480.90 and 17496.21 mm -- a 15 mm spread, which is the radius being noise
 * rather than geometry.
 *
 * A genuine arc fits its circle essentially exactly however gentle it is, so its residual is a
 * vanishing fraction of its sagitta. A short sample of something else fits only to within a
 * residual comparable to the sagitta itself. That ratio separates them; the absolute tolerance
 * cannot, because both sit under it.
 */
export const ARC_FIT_SAGITTA_FRAC = 0.1;

/**
 * Smallest gap between consecutive fit parameters, as a fraction of the run's chord.
 *
 * approximateSpline refuses parameters closer than 1e-6; two points 0.1 um apart on a
 * 900 mm run are 1e-7 of it. Repeats are culled at twice the limit so the fit never sees
 * a step it will reject.
 */
export const PARAMETER_SEPARATION = 2e-6;

/**
 * Largest tangent break, in radians, that still counts as a tangent-continuous
 * junction between two source edges. Below it the two edges are welded: both sides
 * of the shared vertex take one averaged tangent, so the offset lands on one point
 * instead of two. Above it the junction is a real corner and both sides keep their
 * own frame, which is what makes G0 input stay G0.
 *
 * 1e-2 rad is 0.57 degrees. A sketch-solved or filleted tangency lands orders of
 * magnitude inside that; a corner drawn on purpose is well outside it.
 */
export const G1_JUNCTION_ANGLE = 1e-2;

/** Length and shaft radius of the arrow marking each end of the chain. */
export const DEBUG_END_ARROW = 25 * millimeter;
export const DEBUG_END_ARROW_RADIUS = 1.5 * millimeter;

export const MAX_STATIONS_PER_EDGE = 200;
export const MIN_STATIONS_PER_EDGE = 5;

/**
 * A source edge this short is a fragment, and carries two stations: its ends.
 *
 * The floor of five stations is right for an edge with shape to resolve and wrong for a
 * fragment, where it packs five stations into a tenth of a millimetre -- 25 um apart,
 * beside millimetre spacing on the edges either side. The fitter reads that jump as the
 * direction the curve leaves in and hooks: measured on a 0.07 mm fragment at a rout ramp
 * corner, 41 degrees between consecutive samples on one section and a 165 degree reversal
 * on its neighbour.
 *
 * Two stations lose nothing. The sagitta a chord of length L hides on a radius R is
 * L^2 / 8R, and at 0.1 mm on the tightest radius anything here ever has (2 mm) that is
 * 0.6 um -- under every tolerance in play, including the 1 um a ski fit is run at. Longer
 * than this an edge keeps whatever count its control points earn it, so nothing that works
 * today changes.
 */
export const FRAGMENT_EDGE = 0.1 * millimeter;

export enum OffsetPointSpacing
{
    annotation { "Name" : "Control points" }
    CTRL_POINT,
    annotation { "Name" : "Points per edge" }
    NUM_POINTS,
    annotation { "Name" : "Distance along" }
    DISTANCE_ALONG
}
export const CtrlPointMultiplierBounds = { (unitless) : [2, 3, 10] } as IntegerBoundSpec;
export const PointsPerEdgeBounds = { (unitless) : [10, 25, 50] } as IntegerBoundSpec;
export const PointSpacingBounds = { (millimeter) : [0.1, 10, 50] } as LengthBoundSpec;

// ============================================================================
// Selection
// ============================================================================

/**
 * Expand a mixed edge / wire-body / composite-part selection into a flat edge query.
 *
 * Construction geometry is dropped. Selecting a whole sketch otherwise drags in its
 * centrelines, and a construction line spanning the sketch looks exactly like a
 * profile edge that covers every coordinate -- it silently competes for the offset
 * at every X.
 */
export function expandEdgeQuery(selection is Query) returns Query
{
    const directEdges = qEntityFilter(selection, EntityType.EDGE);
    const bodies = qEntityFilter(selection, EntityType.BODY);
    const wireEdges = qOwnedByBody(qBodyType(bodies, BodyType.WIRE), EntityType.EDGE);
    const compositeWires = qBodyType(qContainedInCompositeParts(qBodyType(bodies, BodyType.COMPOSITE)), BodyType.WIRE);
    const allEdges = qUnion([directEdges, wireEdges, qOwnedByBody(compositeWires, EntityType.EDGE)]);

    return qConstructionFilter(allEdges, ConstructionObject.NO);
}

/**
 * Resolve a zero-point selection (mate connector or vertex) to a world position.
 */
export function evZeroPoint(context is Context, selection is Query) returns Vector
{
    if (isQueryEmpty(context, selection))
    {
        throw regenError("Select a zero point (mate connector or vertex).");
    }

    const connectors = qBodyType(qEntityFilter(selection, EntityType.BODY), BodyType.MATE_CONNECTOR);
    if (!isQueryEmpty(context, connectors))
    {
        return evMateConnector(context, { "mateConnector" : connectors }).origin;
    }

    return evVertexPoint(context, { "vertex" : qEntityFilter(selection, EntityType.VERTEX) });
}

/**
 * Fit a circle through three points in space.
 *
 * @returns {map} : { "center", "radius", "normal" }, or undefined if the points
 *                  are collinear.
 */
export function circleThrough(p0 is Vector, p1 is Vector, p2 is Vector)
{
    const a = p1 - p0;
    const b = p2 - p0;
    const axb = cross(a, b);

    // Coincident points first: with |a| = 0 the collinearity test below reads 0 < 0 and
    // passes, and the division by |a x b|^2 makes NaN (met when a chain is projected onto
    // a plane and an edge normal to it collapses to a point).
    if (norm(a) < OFFSET_GEOM_TOL || norm(b) < OFFSET_GEOM_TOL || norm(p2 - p1) < OFFSET_GEOM_TOL)
    {
        return undefined;
    }

    if (norm(axb) < OFFSET_GEOM_TOL * norm(a))
    {
        return undefined;
    }

    // center = p0 + ((|a|^2 b - |b|^2 a) x n) / 2|n|^2, expanded. The pairing matters:
    // |a|^2 goes with (b x n) and |b|^2 with (n x a). Swapping them yields a circle
    // that does not pass through its own defining points except when |a| == |b|, and
    // classifyPoints always picks the midpoint, so |a| ~ |b|/2 and it never does.
    const toCenter = (dot(a, a) * cross(b, axb) + dot(b, b) * cross(axb, a)) / (2 * dot(axb, axb));

    return {
        "center" : p0 + toCenter,
        "radius" : norm(toCenter),
        "normal" : normalize(axb)
    };
}

/**
 * Classify an ordered point set as a whole line, a whole circular arc, or neither.
 *
 * Detection is geometric rather than metadata-based, so it works equally on an
 * existing edge and on points we have just constructed (an offset result has no
 * curve type to read). Tolerance is used as-is with no fudge factor: a near-arc
 * must not be silently promoted to an arc.
 *
 * @param points {array} : ordered sample positions, at least three.
 * @param tolerance {ValueWithUnits} : maximum allowed deviation.
 * @returns {map} : { "kind" : "line" | "arc" | "freeform" } plus geometry for line/arc.
 *          The key is "kind", not "type": "type" is a FeatureScript keyword and
 *          cannot be read back with dot access.
 */
export function classifyPoints(points is array, tolerance is ValueWithUnits) returns map
{
    return classifyPoints(points, tolerance, true);
}

/**
 * As above, but `allowArc` false forbids the arc answer outright.
 *
 * For callers that know the run came off something which is not a circle. Measured on a 42 mm
 * run of a wrapped spline: it sits within 13 NANOMETRES of a circle and 33 nanometres of a
 * plane, so every tolerance in this file passes it honestly and the emitted radius is whatever
 * the fit happened to land on -- three runs of the same shape gave 17485.26, 17480.90 and
 * 17496.21 mm. No threshold separates that from a real arc without also rejecting real arcs.
 * Knowing the source was a spline does.
 */
export function classifyPoints(points is array, tolerance is ValueWithUnits,
    allowArc is boolean) returns map
{
    return classifyPoints(points, tolerance, allowArc, true);
}

/**
 * @param allowArc, allowLine {boolean} : the sourceShapeGates. Where a line is allowed two
 *      points are one; where it is not, two points and their end tangents are still enough
 *      for the fitter to put a cubic through, and on a curved source that cubic carries the
 *      curvature a straight segment would drop.
 */
export function classifyPoints(points is array, tolerance is ValueWithUnits,
    allowArc is boolean, allowLine is boolean) returns map
{
    const count = size(points);
    const first = points[0];
    const last = points[count - 1];

    if (count < 3 && (allowLine || count < 2))
    {
        return { "kind" : "line", "start" : first, "end" : last };
    }

    // Deviation from the chord, which for an arc IS its sagitta. Computed once: the line test
    // asks whether it is small enough to ignore, and the arc test below asks whether the
    // circle fits far better than it -- see ARC_FIT_SAGITTA_FRAC.
    const chord = last - first;
    const chordIsReal = norm(chord) > OFFSET_GEOM_TOL;
    var sagitta = 0 * meter;

    if (chordIsReal)
    {
        const chordDir = normalize(chord);

        for (var i = 1; i < count - 1; i += 1)
        {
            const toPoint = points[i] - first;
            const lateral = norm(toPoint - dot(toPoint, chordDir) * chordDir);
            if (lateral > sagitta)
            {
                sagitta = lateral;
            }
        }

        if (sagitta <= tolerance && allowLine)
        {
            return { "kind" : "line", "start" : first, "end" : last };
        }
    }

    if (!allowArc)
    {
        return { "kind" : "freeform", "sagitta" : sagitta };
    }

    const midIndex = floor(count / 2);
    const fit = circleThrough(first, points[midIndex], last);
    if (fit == undefined)
    {
        return { "kind" : "freeform" };
    }

    // Two questions, two budgets. distanceToCircle used to return sqrt(radial^2 +
    // outOfPlane^2), so a run could be called an arc on the strength of a radial fit while
    // carrying a systematic bow out of the plane -- which is exactly what a spline wrapped
    // onto a curved surface does. Folding them together let the bow hide inside the radial
    // allowance. Planarity is checked on its own, much tighter, threshold.
    var maxRadial = 0 * meter;
    var maxOutOfPlane = 0 * meter;

    for (var point in points)
    {
        const toPoint = point - fit.center;
        const outOfPlane = abs(dot(toPoint, fit.normal));
        const radial = abs(norm(toPoint - dot(toPoint, fit.normal) * fit.normal) - fit.radius);

        if (radial > maxRadial)
        {
            maxRadial = radial;
        }
        if (outOfPlane > maxOutOfPlane)
        {
            maxOutOfPlane = outOfPlane;
        }
    }

    // Three gates, three different questions. Absolute radial fit: are the points on a circle.
    // Planarity: is it the same circle in the same plane. Relative fit: is the circle
    // MEANINGFUL, or is the run so shallow that the residual is as big as the curvature it
    // claims to have.
    // The relative test needs a chord to measure the sagitta against. A run whose ends meet --
    // a closed loop -- has none, and its curvature is not in doubt anyway, so it is exempt
    // rather than rejected for having a sagitta of zero.
    if (maxRadial > tolerance
        || maxOutOfPlane > ARC_PLANARITY_TOL
        || (chordIsReal && maxRadial > ARC_FIT_SAGITTA_FRAC * sagitta))
    {
        // Carried so a caller can say WHY a run that looks circular was not emitted as one.
        return {
            "kind" : "freeform",
            "radialError" : maxRadial,
            "outOfPlane" : maxOutOfPlane,
            "sagitta" : sagitta
        };
    }

    return {
        "kind" : "arc",
        "start" : first,
        "mid" : points[midIndex],
        "end" : last,
        "center" : fit.center,
        "radius" : fit.radius,
        "normal" : fit.normal,
        "radialError" : maxRadial,
        "outOfPlane" : maxOutOfPlane,
        "sagitta" : sagitta
    };
}

/**
 * Rotate a normal from one tangent to the next by the smallest rotation that
 * carries the old tangent onto the new one.
 *
 *     N' = N - (N.b / (1 + a.b)) * (a + b)
 *
 * Trig-free, and exactly a rotation: the result stays unit length and stays
 * perpendicular to the new tangent (verified to 1e-16 on a helix). This is the
 * step that keeps the frame from rolling about the tangent.
 */
export function transportNormal(normal is Vector, fromTangent is Vector, toTangent is Vector) returns Vector
{
    const denominator = 1 + dot(fromTangent, toTangent);

    // Tangent reversed on itself: no minimal rotation exists, so re-project instead.
    if (denominator < 1e-9)
    {
        const projected = normal - dot(normal, toTangent) * toTangent;

        return (norm(projected) < 1e-9) ? normal : normalize(projected);
    }

    return normalize(normal - (dot(normal, toTangent) / denominator) * (fromTangent + toTangent));
}

/**
 * Build a roll-free normal field along a chain by transporting one seed normal
 * outwards in both directions from the seed station.
 *
 * Why not the kernel's Frenet normal: it points wherever the curve happens to be
 * bending, which on a 3D chain swings about the tangent from edge to edge. Measured
 * on a real ski chain, the width axis rotated 86.9 degrees across a junction whose
 * tangent was identical on both sides -- and because the two normals were still
 * within 90 degrees of each other, sign-flip parity could not even detect it.
 * Transport has no such failure mode, needs no inflection handling, and reduces to
 * the in-plane normal exactly when the chain is planar. This is also what the
 * feature's own specification asked for: "a special transport frame".
 *
 * @param tangents {array} : unit tangents in station order.
 * @param seedIndex {number} : station the seed normal belongs to.
 * @param seedNormal {Vector} : starting direction; perpendicularized against its tangent.
 * @returns {array} : unit normals, each perpendicular to its own tangent.
 */
export function transportedNormals(tangents is array, seedIndex is number, seedNormal is Vector) returns array
{
    const count = size(tangents);
    var normals = makeArray(count, seedNormal);

    var seed = seedNormal - dot(seedNormal, tangents[seedIndex]) * tangents[seedIndex];
    if (norm(seed) < 1e-9)
    {
        seed = perpendicularVector(tangents[seedIndex]);
    }
    normals[seedIndex] = normalize(seed);

    for (var i = seedIndex + 1; i < count; i += 1)
    {
        normals[i] = transportNormal(normals[i - 1], tangents[i - 1], tangents[i]);
    }

    for (var i = seedIndex - 1; i >= 0; i -= 1)
    {
        normals[i] = transportNormal(normals[i + 1], tangents[i + 1], tangents[i]);
    }

    return normals;
}

/**
 * Decide, once for a whole chain, which frame axis carries width and which
 * carries height: width is the axis with the larger world-Y component, height
 * is the other, each signed positive along its world direction.
 *
 * Deciding once and reusing the roles is what keeps the assignment continuous.
 * Re-deciding per station would jump wherever the two components are close.
 *
 * @returns {map} : { "widthIsNormal" : boolean, "widthSign" : number, "heightSign" : number }
 */
export function resolveAxisRoles(tangent is Vector, normal is Vector) returns map
{
    const binormal = cross(tangent, normal);
    const widthIsNormal = abs(normal[1]) >= abs(binormal[1]);
    const widthAxis = widthIsNormal ? normal : binormal;
    const heightAxis = widthIsNormal ? binormal : normal;

    return {
        "widthIsNormal" : widthIsNormal,
        "widthSign" : (widthAxis[1] < 0) ? -1 : 1,
        "heightSign" : (heightAxis[2] < 0) ? -1 : 1
    };
}

/**
 * Apply the chain's axis roles to one station's tangent and normal.
 *
 * @returns {map} : { "widthAxis" : Vector, "heightAxis" : Vector }
 */
export function offsetAxes(tangent is Vector, normal is Vector, roles is map) returns map
{
    const binormal = cross(tangent, normal);

    return {
        "widthAxis" : roles.widthSign * (roles.widthIsNormal ? normal : binormal),
        "heightAxis" : roles.heightSign * (roles.widthIsNormal ? binormal : normal)
    };
}

// ============================================================================
// Chains
// ============================================================================

/**
 * Build a chain from an edge selection.
 *
 * Edges are grouped into links by connectivity, each link is oriented so its
 * traversal runs in increasing world X, and links are ordered by their start X.
 * Arc length accumulates across links in that order; gaps between links are not
 * counted, so a chain with gaps measures shorter than the distance it spans.
 *
 * @param zeroPoint {Vector} : world position whose X defines the zero station.
 * @returns {map} :
 *   "links"       {array}          - each { path, edges, length, startArc }
 *   "totalLength" {ValueWithUnits} - summed edge length
 *   "zeroArc"     {ValueWithUnits} - arc length, from the chain start, of the zero station
 * Each entry of a link's "edges" carries:
 *   query, flipped, length, startArc, curveType, startPoint, endPoint
 */
export function buildChain(context is Context, selection is Query, zeroPoint is Vector) returns map
{
    const edges = expandEdgeQuery(selection);
    if (isQueryEmpty(context, edges))
    {
        throw regenError("No edges found in the selection.", selection);
    }

    var paths;
    try silent
    {
        paths = constructPaths(context, edges, { "adjacentSeedFaces" : qNothing() });
    }
    catch
    {
        throw regenError("Could not order these edges into a chain. Edges must connect end to end without branching.", selection);
    }

    var links = [];
    for (var candidate in paths)
    {
        links = append(links, buildLink(context, candidate));
    }

    links = sort(links, function(a, b)
        {
            return (a.edges[0].startPoint[0] - b.edges[0].startPoint[0]) / meter;
        });

    // Accumulate arc length in the sorted link order, so it runs with world X.
    var runningArc = 0 * meter;
    for (var i = 0; i < size(links); i += 1)
    {
        var link = links[i];
        var linkEdges = link.edges;

        for (var j = 0; j < size(linkEdges); j += 1)
        {
            linkEdges[j] = mergeMaps(linkEdges[j], { "startArc" : runningArc + linkEdges[j].startArc });
        }

        links[i] = mergeMaps(link, { "startArc" : runningArc, "edges" : linkEdges });
        runningArc += link.length;
    }

    var chain = {
        "links" : links,
        "totalLength" : runningArc,
        "zeroArc" : 0 * meter
    };
    chain.zeroArc = arcLengthAtX(context, chain, zeroPoint[0]);

    return chain;
}

/**
 * Build one link (a single Path) with per-edge metadata, oriented ascending in X.
 */
function buildLink(context is Context, candidate is Path) returns map
{
    var pathToUse = candidate;
    var edges = describeEdges(context, pathToUse);

    // Reverse the description in place rather than re-describing. Each edge costs
    // kernel calls to describe, and a chain that happens to run descending in X
    // would otherwise pay for every one of them twice.
    if (edges[size(edges) - 1].endPoint[0] < edges[0].startPoint[0])
    {
        pathToUse = reverse(pathToUse);
        edges = reverseDescribed(edges);
    }

    var linkLength = 0 * meter;
    for (var edgeData in edges)
    {
        linkLength += edgeData.length;
    }

    return { "path" : pathToUse, "edges" : edges, "length" : linkLength, "startArc" : 0 * meter };
}

/**
 * Reverse an ordered edge description: reverse the order, flip each edge's
 * traversal sense, swap its endpoints, and restate the running arc length.
 */
function reverseDescribed(edges is array) returns array
{
    var reversed = makeArray(size(edges));
    var runningArc = 0 * meter;

    for (var i = 0; i < size(edges); i += 1)
    {
        const source = edges[size(edges) - 1 - i];

        reversed[i] = mergeMaps(source, {
                    "flipped" : !source.flipped,
                    "startPoint" : source.endPoint,
                    "endPoint" : source.startPoint,
                    "startArc" : runningArc
                });
        runningArc += source.length;
    }

    return reversed;
}

/**
 * Per-edge metadata for one path: three kernel calls per edge, each batched.
 */
function describeEdges(context is Context, pathToDescribe is Path) returns array
{
    var edges = [];
    var runningArc = 0 * meter;

    for (var i = 0; i < size(pathToDescribe.edges); i += 1)
    {
        const edge = pathToDescribe.edges[i];
        const flipped = pathToDescribe.flipped[i];
        const ends = evEdgeTangentLines(context, { "edge" : edge, "parameters" : [0, 1] });
        const definition = evCurveDefinition(context, { "edge" : edge, "returnBSplinesAsOther" : true });
        const edgeLength = evLength(context, { "entities" : edge });

        edges = append(edges, {
                    "query" : edge,
                    "flipped" : flipped,
                    "length" : edgeLength,
                    "startArc" : runningArc,
                    "curveType" : definition.curveType,
                    "startPoint" : flipped ? ends[1].origin : ends[0].origin,
                    "endPoint" : flipped ? ends[0].origin : ends[1].origin
                });

        runningArc += edgeLength;
    }

    return edges;
}

/**
 * Convert a traversal fraction along an edge to the edge's own parameter.
 */
export function edgeParam(edgeData is map, fraction is number) returns number
{
    const clamped = clamp(fraction, 0, 1);

    return edgeData.flipped ? 1 - clamped : clamped;
}

/**
 * Arc length along the chain at which world X equals a target.
 *
 * Brackets the crossing on the edge whose endpoints span the target, then runs a
 * secant refinement on the arc-length parameter. Costs one kernel call per
 * refinement step, and the chain is short in edges, so this stays cheap.
 */
export function arcLengthAtX(context is Context, chain is map, targetX is ValueWithUnits) returns ValueWithUnits
{
    for (var link in chain.links)
    {
        for (var edgeData in link.edges)
        {
            const x0 = edgeData.startPoint[0];
            const x1 = edgeData.endPoint[0];

            if ((x0 - targetX).value * (x1 - targetX).value > 0)
            {
                continue;
            }
            if (abs((x1 - x0).value) < 1e-12)
            {
                return edgeData.startArc;
            }

            var lo = 0;
            var hi = 1;
            var fLo = (x0 - targetX) / meter;
            var fHi = (x1 - targetX) / meter;

            for (var iteration = 0; iteration < 12; iteration += 1)
            {
                const guess = clamp(lo + (hi - lo) * (-fLo) / (fHi - fLo), 0, 1);
                const point = evEdgeTangentLines(context, {
                                "edge" : edgeData.query,
                                "parameters" : [edgeParam(edgeData, guess)]
                            })[0].origin;
                const fGuess = (point[0] - targetX) / meter;

                if (abs(fGuess) < 1e-9)
                {
                    return edgeData.startArc + guess * edgeData.length;
                }
                if (fLo * fGuess <= 0)
                {
                    hi = guess;
                    fHi = fGuess;
                }
                else
                {
                    lo = guess;
                    fLo = fGuess;
                }
            }

            return edgeData.startArc + 0.5 * (lo + hi) * edgeData.length;
        }
    }

    throw regenError("The zero point's X value is not spanned by the selected edges.");
}

/**
 * Station count for one edge under the user's spacing rule.
 *
 * Straight and circular edges are deliberately NOT given a reduced count. The
 * shape of an offset is set by the profile as much as by the edge: a straight
 * edge under a curved profile offsets to a curve, and three points fit a circle
 * exactly, so a sparse sample would let arc detection succeed on anything.
 * Sampling costs one batched call per edge either way.
 */
export function stationCount(context is Context, edgeData is map, spacing is map) returns number
{
    // A fragment carries its ends and nothing between them, whatever the mode: there is
    // no shape in it to resolve, and stations 25 um apart beside millimetre spacing are
    // what makes a fit hook. See FRAGMENT_EDGE.
    if (edgeData.length < FRAGMENT_EDGE)
    {
        return 2;
    }

    var count;

    if (spacing.mode == OffsetPointSpacing.NUM_POINTS)
    {
        count = spacing.pointsPerEdge;
    }
    else if (spacing.mode == OffsetPointSpacing.DISTANCE_ALONG)
    {
        count = ceil(edgeData.length / spacing.targetSpacing) + 1;
    }
    else if (edgeData.curveType == CurveType.LINE || edgeData.curveType == CurveType.CIRCLE)
    {
        // A line or arc has no meaningful control-point count to multiply, and
        // evApproximateBSplineCurve is one of the more expensive evaluations. The
        // count still must not be small -- see the note above -- so it takes the
        // same floor as everything else.
        count = MIN_STATIONS_PER_EDGE * spacing.ctrlPointMultiplier;
    }
    else
    {
        const curve = evApproximateBSplineCurve(context, { "edge" : edgeData.query });
        count = size(curve.controlPoints) * spacing.ctrlPointMultiplier;
    }

    return clamp(round(count), MIN_STATIONS_PER_EDGE, MAX_STATIONS_PER_EDGE);
}

/**
 * Evaluate frames at every station on the chain.
 *
 * One evEdgeCurvatures call per edge covers all of that edge's stations. Normals
 * are made continuous across the whole chain afterwards, and the width/height
 * axis roles are decided once from the station nearest the zero point.
 *
 * @param spacing {map} : { mode, pointsPerEdge, targetSpacing, ctrlPointMultiplier }
 * @returns {array} : stations, each
 *   { arc, origin, tangent, rawNormal, curvature, normal, widthAxis, heightAxis,
 *     curvatureWidth, curvatureHeight, roles, linkIndex, edgeIndex }
 *   and, on the second half of a welded vertex, { junctionGap, junctionBreak, welded }.
 */
export function chainStations(context is Context, chain is map, spacing is map) returns array
{
    var raw = [];

    for (var linkIndex = 0; linkIndex < size(chain.links); linkIndex += 1)
    {
        const link = chain.links[linkIndex];

        for (var edgeIndex = 0; edgeIndex < size(link.edges); edgeIndex += 1)
        {
            const edgeData = link.edges[edgeIndex];
            const count = stationCount(context, edgeData, spacing);
            const fractions = range(0, 1, count);

            var parameters = [];
            for (var fraction in fractions)
            {
                parameters = append(parameters, edgeParam(edgeData, fraction));
            }

            const results = evEdgeCurvatures(context, { "edge" : edgeData.query, "parameters" : parameters });

            var edgeStations = [];
            for (var i = 0; i < count; i += 1)
            {
                // Both sides of a shared vertex are kept. At a G0 junction the two
                // frames genuinely differ, and each output edge needs its own ends.
                const frame = results[i].frame;
                const tangent = edgeData.flipped ? -1 * frame.zAxis : frame.zAxis;

                edgeStations = append(edgeStations, {
                            "arc" : edgeData.startArc + fractions[i] * edgeData.length,
                            "origin" : frame.origin,
                            "tangent" : tangent,
                            "rawNormal" : frame.xAxis,
                            "curvature" : results[i].curvature,
                            "linkIndex" : linkIndex,
                            "edgeIndex" : edgeIndex,
                            // What the source edge here actually IS. A tolerance cannot
                            // recover this: over a short span the offset of a spline is
                            // circular to nanometres, so the only way to know a circle is
                            // not the right answer is that the input was never a circle.
                            "curveType" : edgeData.curveType
                        });
            }

            raw = concatenateArrays([raw, relaxEndCurvature(edgeStations)]);
        }
    }

    return finishStations(weldJunctions(raw), chain.zeroArc);
}

/**
 * The curvature vector dT/ds at a station.
 *
 * This is what every downstream use actually wants: curvatureWidth and
 * curvatureHeight are just its components on the frame axes. Carrying magnitude
 * and direction together is what lets a station be averaged or extrapolated at
 * all -- the kernel normal can point either way about the tangent, so averaging
 * the scalar curvature of two stations whose normals disagree produces nonsense
 * while averaging the vectors is simply correct.
 */
export function curvatureVector(station is map) returns Vector
{
    return station.curvature * station.rawNormal;
}

/**
 * Resolve a station's curvature onto a pair of frame axes.
 *
 * dot(dT/ds, axis) for each. Four call sites used to spell this out; they all mean
 * the same thing and all have to agree, because offsetShrink subtracts them.
 */
export function curvatureOn(station is map, widthAxis is Vector, heightAxis is Vector) returns map
{
    const kVector = curvatureVector(station);

    return {
        "curvatureWidth" : dot(kVector, widthAxis),
        "curvatureHeight" : dot(kVector, heightAxis)
    };
}

/**
 * Split a curvature vector back into the magnitude and unit normal the rest of the
 * code expects. Any component along the tangent is dropped: dT/ds is perpendicular
 * to T by construction, so a nonzero one is interpolation error, not geometry.
 *
 * Below ZERO_CURVATURE the direction carries no information -- the kernel normal is
 * documented as arbitrary there -- so the station is left exactly as it was.
 */
function withCurvatureVector(station is map, kVector is Vector) returns map
{
    const perpendicular = kVector - dot(kVector, station.tangent) * station.tangent;
    const magnitude = norm(perpendicular);

    if (magnitude < ZERO_CURVATURE)
    {
        return station;
    }

    return mergeMaps(station, {
                "curvature" : magnitude,
                "rawNormal" : perpendicular / magnitude
            });
}

/**
 * Replace the curvature at an edge's two end stations with their nearest interior
 * neighbour's.
 *
 * evEdgeCurvatures evaluated exactly at parameter 0 or 1 is not trustworthy. On a
 * real ski tip the first station of an edge came back at +42.6/m while every other
 * station on that edge sat near -14/m: a sign flip, at 1.2 mm sampling, inside a
 * single spline. That is the endpoint evaluation, not the geometry.
 *
 * A hold, deliberately, and not an extrapolation. Extrapolating needs two clean
 * interior samples and on the same tip the SECOND station from the end was also
 * junk (-43.2/m between -7.2 and +7.0), which a two-point rule would have amplified
 * to -79/m -- worse than the value it replaced. A hold cannot amplify anything. Its
 * error is one station-gap of curvature change, which is far inside what the only
 * two consumers need.
 *
 * Those consumers are offsetShrink -- the fold-back guard, which throws a
 * user-facing error, and where a spurious value could reject a perfectly good
 * offset -- and the debug tables. No offset point and no tangent depends on the
 * source curvature: offsetTangent takes the -kappaW*T component from the
 * finite-differenced frame rates instead, and surfaceOffsetTangent uses the
 * reference's curvature, not the source's. So this buys a trustworthy guard and a
 * readable table, and changes no geometry at all.
 */
function relaxEndCurvature(stations is array) returns array
{
    const count = size(stations);
    if (count < 3)
    {
        return stations;
    }

    var relaxed = stations;
    relaxed[0] = withCurvatureVector(stations[0], curvatureVector(stations[1]));
    relaxed[count - 1] = withCurvatureVector(stations[count - 1], curvatureVector(stations[count - 2]));

    return relaxed;
}

/**
 * Make the two stations either side of a tangent-continuous edge junction agree.
 *
 * Every source edge contributes its own copy of a shared vertex, evaluated from
 * its own side, and the two tangents can differ slightly even where the source was
 * drawn tangent-continuous -- measured at 1 mrad on a real ski chain. That feeds
 * straight through to the result: the two frames rotate apart by the same angle, so
 * the two offset points land width * angle apart. At 11.7 mm of width, 1 mrad is an
 * 11 micron gap between two output curves that were meant to meet, and
 * opExtractWires cannot stitch across it.
 *
 * Below G1_JUNCTION_ANGLE both sides take one averaged tangent and one shared
 * origin, so their offset positions come out of identical arithmetic and agree bit
 * for bit. Above it the junction is a real corner and is left alone. Either way the
 * measured break is recorded, so the debug output can show what was decided.
 */
function weldJunctions(raw is array) returns array
{
    var welded = raw;

    for (var i = 0; i < size(welded) - 1; i += 1)
    {
        const left = welded[i];
        const right = welded[i + 1];

        // Adjacent stations from consecutive edges of one link are the two halves
        // of a shared vertex.
        if (left.linkIndex != right.linkIndex || right.edgeIndex != left.edgeIndex + 1)
        {
            continue;
        }

        // Both are recorded even when the weld is declined: a junction that stayed
        // open is exactly what a gap in the output looks like from here, and the
        // reason -- too far apart, or too sharp -- is the thing worth seeing.
        const gap = norm(right.origin - left.origin);
        const breakAngle = angleBetween(left.tangent, right.tangent);
        const sum = left.tangent + right.tangent;
        const weld = gap <= OFFSET_GEOM_TOL
            && breakAngle / radian <= G1_JUNCTION_ANGLE
            && norm(sum) > 1e-9;

        welded[i + 1] = mergeMaps(right, {
                    "junctionGap" : gap,
                    "junctionBreak" : breakAngle,
                    "welded" : weld
                });

        // The left half is marked too, so that a station can be recognised as one half
        // of a source vertex from either side: neither half may be dropped for a crossing
        // inserted beside it, or the vertex loses the record the corner treatment reads.
        welded[i] = mergeMaps(left, { "junctionEnd" : true });

        if (weld)
        {
            const shared = normalize(sum);

            // Curvature is welded along with tangent and origin, so the two halves
            // of one vertex report one curvature rather than two. Below
            // G1_JUNCTION_ANGLE the two sides are meant to be one curve, and the
            // curvature of a curve is single-valued.
            //
            // This is a guard-and-diagnostics fix, not a geometric one: nothing
            // downstream of offsetShrink reads the source curvature. Without it the
            // fold-back guard sees two different values at one point -- 7.0/m and
            // 14.2/m across a 0 mrad vertex on a real ski tip -- and either could be
            // the one that throws.
            const sharedCurvature = 0.5 * (curvatureVector(left) + curvatureVector(right));

            welded[i] = withCurvatureVector(mergeMaps(welded[i], { "tangent" : shared }), sharedCurvature);
            welded[i + 1] = withCurvatureVector(
                    mergeMaps(welded[i + 1], { "tangent" : shared, "origin" : left.origin }),
                    sharedCurvature);
        }
    }

    return welded;
}

/**
 * Turn raw station samples into frames: continuous normals, then fixed axis roles.
 */
function finishStations(raw is array, zeroArc is ValueWithUnits) returns array
{
    var tangents = [];
    var kernelNormals = [];
    var curvatures = [];

    for (var station in raw)
    {
        tangents = append(tangents, station.tangent);
        kernelNormals = append(kernelNormals, station.rawNormal);
        curvatures = append(curvatures, station.curvature);
    }

    // Seed the frame at the zero station, which is where the user's intent is
    // anchored, and where the roles are decided.
    var zeroIndex = 0;
    var bestDistance = undefined;
    for (var i = 0; i < size(raw); i += 1)
    {
        const distance = abs(raw[i].arc - zeroArc);
        if (bestDistance == undefined || distance < bestDistance)
        {
            bestDistance = distance;
            zeroIndex = i;
        }
    }

    const normals = transportedNormals(tangents, zeroIndex, seedNormalFor(tangents, kernelNormals, curvatures, zeroIndex));
    const roles = resolveAxisRoles(tangents[zeroIndex], normals[zeroIndex]);

    var stations = [];
    for (var i = 0; i < size(raw); i += 1)
    {
        const axes = offsetAxes(tangents[i], normals[i], roles);
        const resolved = curvatureOn(raw[i], axes.widthAxis, axes.heightAxis);

        // Curvature signed about each axis. The kernel normal points at the centre
        // of curvature, so a positive value means that axis points inward. Where the
        // curve is straight the kernel normal is arbitrary, but the curvature it is
        // multiplied by is ~0, so the product stays harmless.
        const towardCentre = kernelNormals[i];

        stations = append(stations, mergeMaps(raw[i], {
                        "roles" : roles,
                        "arc" : raw[i].arc - zeroArc,
                        "normal" : normals[i],
                        "widthAxis" : axes.widthAxis,
                        "heightAxis" : axes.heightAxis,
                        "curvatureWidth" : resolved.curvatureWidth,
                        "curvatureHeight" : resolved.curvatureHeight
                    }));
    }

    return stations;
}

/**
 * The direction to seed the transported frame with.
 *
 * The kernel's curvature normal is a good seed where the chain is genuinely
 * curved, because it puts the frame in the plane the chain is bending in. Where
 * the seed station is straight that normal is arbitrary, so fall back to a
 * world-up reference and let transport carry it from there.
 */
function seedNormalFor(tangents is array, kernelNormals is array, curvatures is array, seedIndex is number) returns Vector
{
    if (abs(curvatures[seedIndex]) >= ZERO_CURVATURE)
    {
        return kernelNormals[seedIndex];
    }

    const up = vector(0, 0, 1);
    const across = cross(up, tangents[seedIndex]);

    return (norm(across) < 1e-6) ? perpendicularVector(tangents[seedIndex]) : normalize(across);
}

// ============================================================================
// Terminal planes
// ============================================================================

/**
 * The plane behind a face or mate connector selection.
 *
 * Both are accepted because the two live cases need different ones: a centreline or a
 * tooling datum is a face you can pick, but a plane at a computed FCP/ACP station exists
 * only as a mate connector the upstream feature emitted.
 *
 * The picked entity's own origin is used, not the chain endpoint. That is what lets a
 * plane positioned short of the source terminate everything early; put the plane through
 * the endpoint and you get the "stop where the source stopped" reading instead.
 */
export function planeFromQuery(context is Context, query is Query) returns Plane
{
    if (!isQueryEmpty(context, qBodyType(query, BodyType.MATE_CONNECTOR)))
    {
        return plane(evMateConnector(context, { "mateConnector" : query }));
    }

    return evPlane(context, { "face" : query });
}

/**
 * Curvature vector at `p2`, read from the circle through three consecutive points.
 *
 * The stations carry the SOURCE curvature, which is not the offset's: an offset by w has
 * curvature kappa / (1 - w * kappa). Reading it back off the emitted points sidesteps that
 * entirely and costs nothing, since the points are already in hand.
 */
export function curvatureThrough(p0 is Vector, p1 is Vector, p2 is Vector) returns Vector
{
    const circleData = circleThrough(p0, p1, p2);

    if (circleData == undefined)
    {
        return vector(0, 0, 0) / meter;
    }

    const toCenter = circleData.center - p2;
    const reach = norm(toCenter);

    if (reach < OFFSET_GEOM_TOL || circleData.radius < OFFSET_GEOM_TOL)
    {
        return vector(0, 0, 0) / meter;
    }

    return (toCenter / reach) / circleData.radius;
}

/**
 * Emit a straight edge as a degree-one B-spline with two control points.
 * Onshape reads this back as a line, so no sketch is needed.
 */
export function emitLineCurve(context is Context, id is Id, start is Vector, end is Vector)
{
    opCreateBSplineCurve(context, id, {
                "bSplineCurve" : bSplineCurve({
                            "degree" : 1,
                            "isPeriodic" : false,
                            "controlPoints" : [start, end]
                        })
            });
}

/**
 * Emit a circular arc through a sketch, so the result carries a real radius
 * rather than reading as a generic spline.
 *
 * The sketch is deleted after extraction; extracted wires are independent copies
 * and survive it.
 */
export function emitArcCurve(context is Context, id is Id, arcData is map)
{
    const sketchId = id + "arcSketch";
    const sketchPl = plane(arcData.center, arcData.normal, normalize(arcData.start - arcData.center));
    const sk = newSketchOnPlane(context, sketchId, { "sketchPlane" : sketchPl });

    skArc(sk, "arc", {
                "start" : worldToPlane(sketchPl, arcData.start),
                "mid" : worldToPlane(sketchPl, arcData.mid),
                "end" : worldToPlane(sketchPl, arcData.end)
            });
    skSolve(sk);

    opExtractWires(context, id + "wire", { "edges" : qCreatedBy(sketchId, EntityType.EDGE) });
    opDeleteBodies(context, id + "deleteSketch", { "entities" : qCreatedBy(sketchId, EntityType.BODY) });
}

/**
 * A knot vector as text, for comparing one fit against another.
 *
 * Two curves of the same degree with the same number of control points can still carry
 * completely different knots, and approximateSpline documents that lofting between curves
 * wants them consistently parameterized. Same count is not the same vector, so the values
 * are what has to be read.
 */
export function knotText(knots is array) returns string
{
    var text = "[";

    for (var k = 0; k < size(knots); k += 1)
    {
        text = text ~ (k == 0 ? "" : " ") ~ toString(roundToPrecision(knots[k], 5));
    }

    return text ~ "]";
}

/**
 * Emit a freeform curve through the offset points.
 *
 * End derivatives come from the exact offset tangent rather than from the fitted
 * points, so a junction that should stay G1 stays G1 by construction.
 *
 * @param approximation {map} : the feature's curveApproximationPredicate fields.
 */
export function emitSplineCurve(context is Context, id is Id, points is array, startDerivative, endDerivative, approximation is map)
{
    const curves = approximateFamily(context, [{
                        "points" : points,
                        "startDerivative" : startDerivative,
                        "endDerivative" : endDerivative
                    }], approximation);

    emitFittedCurve(context, id, curves[0], points, approximation);
}

/**
 * Fit one curve per point list, all in a single call, so the family shares a parameterization.
 *
 * approximateSpline documents multi-target output as "consistently parameterized, so that,
 * for example, lofting between them will match corresponding target positions". That is not
 * a nicety where the curves are going into a loft. Two curves fitted in SEPARATE calls can
 * come back with the same degree, the same control-point count and DIFFERENT knot vectors,
 * and opLoft rejects some of those pairs as LOFT_FAILED while accepting others that measure
 * identically -- same separation the whole way, tangents in agreement, no cusp or reversal
 * in either. Verified against the kernel: a loft built by hand between two such curves fails
 * the same way, so it is not a matter of how the operation is called. Fitting the family in
 * one call removes the difference by construction rather than hoping the fitter lands on
 * compatible knots twice.
 *
 * Every member must carry the same number of positions and corresponding derivative
 * information; std requires it, and the caller is expected to have checked.
 *
 * @param members {array} : maps of { "points" : array, "startDerivative", "endDerivative" },
 *      the derivatives optional but present or absent together across the family.
 * @param approximation {map} : the feature's curveApproximationPredicate fields.
 * @returns {array} : one BSplineCurve per member, in the order given.
 */
export function approximateFamily(context is Context, members is array, approximation is map) returns array
{
    var targets = [];
    var parameters = [];

    for (var member in members)
    {
        // The fit is parameterized over [0, 1] by chord length -- see below -- so the
        // derivative magnitude at an endpoint is the run's total chord, not 1. Handing it a
        // unit vector asks for near-zero velocity there, which bulges the curve near the
        // junction -- measured at ~0.45 mm in curveMapping/wrapCurve.fs:568.
        var chords = [0 * meter];
        var chord = 0 * meter;
        for (var i = 0; i < size(member.points) - 1; i += 1)
        {
            chord += norm(member.points[i + 1] - member.points[i]);
            chords = append(chords, chord);
        }

        var target = { "positions" : member.points };
        if (member.startDerivative != undefined)
        {
            target.startDerivative = member.startDerivative * chord;
        }
        if (member.endDerivative != undefined)
        {
            target.endDerivative = member.endDerivative * chord;
        }

        targets = append(targets, approximationTarget(target));

        // One parameter list serves the whole family (std: all targets share it), so it is
        // the members' chord fractions averaged. Members of one family are the same
        // stations on profiles a few millimetres apart; their fractions differ in the
        // third decimal.
        for (var i = 0; i < size(chords); i += 1)
        {
            const fraction = (chord > 0 * meter) ? chords[i] / chord : i / max(1, size(chords) - 1);
            if (size(parameters) <= i)
            {
                parameters = append(parameters, fraction / size(members));
            }
            else
            {
                parameters[i] += fraction / size(members);
            }
        }
    }

    // Chord-length parameters, always. Without them approximateSpline discards the
    // magnitude of the end derivatives and sizes them from the first and last segments of
    // the point list (std doc; correction 23), so any short end segment -- a crossing near a
    // station, a dense edge merged into a sparse run, an exact corner point beside its
    // station -- hooks the curve and eats the control-point budget. Measured on 40 points of
    // a 200 mm arc with a dense start: 15 CPs (the cap) and 0.6 /m at the end without, 8 CPs
    // and 4.94 /m (true 5) with. The parameters have to be strictly increasing by 1e-6,
    // which is what PARAMETER_SEPARATION guarantees upstream.
    const curves = approximateSpline(context, {
                "degree" : approximation.approximationDegree,
                "tolerance" : approximation.approximationTolerance,
                "isPeriodic" : false,
                "targets" : targets,
                "parameters" : parameters,
                "maxControlPoints" : approximation.approximationMaxCPs,
                // A run of three points with two end tangents is interpolated by
                // construction; the std INFO for that is noise here, the error is measured
                // below either way.
                "suppressInterpolationNotice" : true
            });

    // Two ways the fitter can hand back a curve that is not what was asked for, neither of
    // them an error from its side. Out of control points it reports an INFO and returns an
    // interpolating spline through every point; AT the cap it returns its best curve of
    // that size and says nothing, "tolerance will not be satisfied" being in the doc only.
    // The parameters pin every point to a parameter, so the second is measurable exactly:
    // the curve at each parameter against the point it was meant to pass.
    for (var k = 0; k < size(curves); k += 1)
    {
        const first = members[k].points[0];
        const count = size(curves[k].controlPoints);

        if (count > approximation.approximationMaxCPs)
        {
            println("WARNING: a run of " ~ toString(size(members[k].points)) ~ " points starting at "
                ~ fmtVec(first / millimeter, 1, 8) ~ " mm could not be fitted to "
                ~ fmtMM(approximation.approximationTolerance, 4, 0) ~ " mm within "
                ~ toString(approximation.approximationMaxCPs) ~ " control points; an interpolating spline of "
                ~ toString(count) ~ " was used instead.");
            continue;
        }

        if (count >= approximation.approximationMaxCPs)
        {
            const evaluated = evaluateSpline({ "spline" : curves[k], "parameters" : parameters, "nDerivatives" : 0 })[0];
            var worst = 0 * meter;
            for (var i = 0; i < size(evaluated); i += 1)
            {
                worst = max(worst, norm(evaluated[i] - members[k].points[i]));
            }

            if (worst > approximation.approximationTolerance)
            {
                println("WARNING: a run of " ~ toString(size(members[k].points)) ~ " points starting at "
                    ~ fmtVec(first / millimeter, 1, 8) ~ " mm reached the cap of " ~ toString(approximation.approximationMaxCPs)
                    ~ " control points " ~ fmtMM(worst, 4, 0) ~ " mm from its points, against a tolerance of "
                    ~ fmtMM(approximation.approximationTolerance, 4, 0) ~ " mm. Raise the maximum control points.");
            }
        }
    }

    return curves;
}

/**
 * Turn a curve the fitter already produced into geometry.
 *
 * Separate from the fitting so that a family fitted together can still be emitted one
 * member at a time, under its own id.
 */
export function emitFittedCurve(context is Context, id is Id, curve is BSplineCurve, points is array,
    approximation is map)
{
    // What the fitter actually returned. Worth keeping: a BSplineCurve is malformed when
    // it has fewer than degree + 1 control points, and opCreateBSplineCurve reports that
    // only as BAD_GEOMETRY, naming neither the curve nor the reason. The knot VALUES are
    // printed too, not just the count -- two curves of the same degree with the same
    // control-point count can carry different knots, and that difference is invisible in
    // every other measure. Only the surface feature sets debugFit, so the plain offset's
    // own fits are unaffected.
    if (approximation.debugFit == true)
    {
        const lastCP = size(curve.controlPoints) - 1;

        println("[fit]       in " ~ toString(size(points)) ~ " pt"
                ~ "  degree " ~ toString(curve.degree)
                ~ "  CPs " ~ toString(size(curve.controlPoints))
                ~ "  knots " ~ toString(size(curve.knots))
                ~ " " ~ knotText(curve.knots)
                ~ "  weights " ~ toString(curve.weights == undefined ? 0 : size(curve.weights))
                ~ "  maxCPs " ~ toString(approximation.approximationMaxCPs)
                ~ "  snap start " ~ toString(roundToPrecision(norm(curve.controlPoints[0] - points[0]) / millimeter, 6))
                ~ " mm  snap end " ~ toString(roundToPrecision(norm(curve.controlPoints[lastCP] - points[size(points) - 1]) / millimeter, 6))
                ~ " mm");
    }

    opCreateBSplineCurve(context, id, { "bSplineCurve" : snapEnds(curve, points) });
}

/**
 * Pin the first and last control points to the exact input positions.
 *
 * A clamped B-spline already starts and ends at CP[0] and CP[-1], so this is
 * geometrically free -- but the fit only guarantees the endpoints to within
 * tolerance, and adjacent runs have to agree bit-for-bit for opExtractWires to
 * stitch them into one wire.
 */
function snapEnds(curve is BSplineCurve, points is array) returns BSplineCurve
{
    var controlPoints = curve.controlPoints;
    const last = size(controlPoints) - 1;

    controlPoints[0] = points[0];
    controlPoints[last] = points[size(points) - 1];

    return mergeMaps(curve, { "controlPoints" : controlPoints }) as BSplineCurve;
}

// ============================================================================
// Formatting (debug output)
// ============================================================================

/**
 * The END outputs of wires, named the same in every Curve_tools feature: for each open
 * chain of edges, its free vertex nearest `startPoint` (where the source starts) is the
 * start, the other free vertex the end, each with its edge; the vertices between (used by
 * two edges) are the breaks; the edges ordered from the start are the runs. Chains that
 * are closed or branch have no ends. Results are unions over the chains.
 *
 * @returns {map} : { startVertex, endVertex, startEdge, endEdge, breakVertices (Queries),
 *      orderedEdges (array of edge Queries of the FIRST open chain, from its start) }
 */
export function wireEnds(context is Context, chains is array, startPoint is Vector) returns map
{
    var result = { "startVertex" : [], "endVertex" : [], "startEdge" : [], "endEdge" : [], "breakVertices" : [] };
    var orderedEdges = undefined;
    for (var chainEdges in chains)
    {
        var uses = {};
        var edgesAt = {};
        var vertexOf = {};
        const edgeList = evaluateQuery(context, chainEdges);
        for (var edge in edgeList)
        {
            for (var vertex in evaluateQuery(context, qAdjacent(edge, AdjacencyType.VERTEX, EntityType.VERTEX)))
            {
                const key = toString(vertex);
                uses[key] = (uses[key] == undefined ? 0 : uses[key]) + 1;
                edgesAt[key] = append(edgesAt[key] == undefined ? [] : edgesAt[key], edge);
                vertexOf[key] = vertex;
            }
        }
        var free = [];
        var branching = false;
        for (var entry in uses)
        {
            if (entry.value == 1)
            {
                free = append(free, entry.key);
            }
            else if (entry.value == 2)
            {
                result.breakVertices = append(result.breakVertices, vertexOf[entry.key]);
            }
            else
            {
                branching = true;
            }
        }
        if (size(free) != 2 || branching)
        {
            continue;
        }
        const d0 = norm(evVertexPoint(context, { "vertex" : vertexOf[free[0]] }) - startPoint);
        const d1 = norm(evVertexPoint(context, { "vertex" : vertexOf[free[1]] }) - startPoint);
        const s = d0 <= d1 ? free[0] : free[1];
        const e = d0 <= d1 ? free[1] : free[0];
        result.startVertex = append(result.startVertex, vertexOf[s]);
        result.endVertex = append(result.endVertex, vertexOf[e]);
        result.startEdge = append(result.startEdge, edgesAt[s][0]);
        result.endEdge = append(result.endEdge, edgesAt[e][0]);

        if (orderedEdges == undefined)
        {
            // Walk from the start: each vertex hands over to the edge not yet taken.
            orderedEdges = [];
            var taken = {};
            var edge = edgesAt[s][0];
            var at = s;
            while (edge != undefined)
            {
                orderedEdges = append(orderedEdges, edge);
                taken[toString(edge)] = true;
                var next = undefined;
                for (var vertex in evaluateQuery(context, qAdjacent(edge, AdjacencyType.VERTEX, EntityType.VERTEX)))
                {
                    const key = toString(vertex);
                    if (key != at)
                    {
                        at = key;
                        for (var candidate in edgesAt[key])
                        {
                            if (taken[toString(candidate)] != true)
                            {
                                next = candidate;
                            }
                        }
                        break;
                    }
                }
                edge = next;
            }
        }
    }
    return {
            "startVertex" : qUnion(result.startVertex),
            "endVertex" : qUnion(result.endVertex),
            "startEdge" : qUnion(result.startEdge),
            "endEdge" : qUnion(result.endEdge),
            "breakVertices" : qUnion(result.breakVertices),
            "orderedEdges" : orderedEdges == undefined ? [] : orderedEdges
        };
}

/**
 * Right-align text in a fixed-width column.
 */
export function padLeft(text is string, width is number) returns string
{
    const deficit = width - length(text);

    return (deficit > 0) ? repeatString(" ", deficit) ~ text : text;
}

/**
 * A length in millimetres, fixed decimals, right-aligned. Undefined prints as "--".
 */
export function fmtMM(value, decimals is number, width is number) returns string
{
    if (value == undefined)
    {
        return padLeft("--", width);
    }

    return padLeft(fixedDecimals(toString(roundToPrecision(value / millimeter, decimals)), decimals), width);
}

/**
 * Pad a number's text out to a fixed number of decimals.
 *
 * toString drops trailing zeros, so 345.2 and 345.213 right-align with their decimal
 * points in different columns. In a table read by scanning a column for the place a
 * value stops behaving, that is not cosmetic -- it is the whole job of the table.
 *
 * Scientific notation is left alone: padding it would produce nonsense, and a value
 * small enough to trigger it is telling you something on its own.
 */
function fixedDecimals(text is string, decimals is number) returns string
{
    if (indexOf(text, "e") >= 0 || indexOf(text, "E") >= 0)
    {
        return text;
    }

    const point = indexOf(text, ".");

    if (decimals <= 0)
    {
        return (point < 0) ? text : substring(text, 0, point);
    }
    if (point < 0)
    {
        return text ~ "." ~ repeatString("0", decimals);
    }

    const have = length(text) - point - 1;

    return (have >= decimals) ? text : text ~ repeatString("0", decimals - have);
}

/**
 * A plain number, fixed decimals, right-aligned. Undefined prints as "--".
 */
export function fmtNum(value, decimals is number, width is number) returns string
{
    if (value == undefined)
    {
        return padLeft("--", width);
    }

    return padLeft(fixedDecimals(toString(roundToPrecision(value, decimals)), decimals), width);
}

/**
 * A unitless vector as three fixed-width, fixed-decimal columns.
 */
export function fmtVec(v is Vector, decimals is number, width is number) returns string
{
    return fmtNum(v[0], decimals, width) ~ fmtNum(v[1], decimals, width) ~ fmtNum(v[2], decimals, width);
}

export function withoutRepeats(points is array) returns array
{
    return withoutRepeats(points, TOLERANCE.zeroLength * meter);
}

/**
 * Consecutive points closer than `tolerance` collapsed to the first of them.
 */
export function withoutRepeats(points is array, tolerance is ValueWithUnits) returns array
{
    var kept = [];

    for (var point in points)
    {
        if (size(kept) > 0 && norm(point - kept[size(kept) - 1]) < tolerance)
        {
            continue;
        }

        kept = append(kept, point);
    }

    return kept;
}

/**
 * The repeat tolerance a point list needs before it is handed to the fitter: the larger of
 * the kernel's zero length and PARAMETER_SEPARATION of the list's own chord.
 */
export function fitRepeatTolerance(points is array) returns ValueWithUnits
{
    var chord = 0 * meter;
    for (var k = 1; k < size(points); k += 1)
    {
        chord += norm(points[k] - points[k - 1]);
    }
    return max(TOLERANCE.zeroLength * meter, PARAMETER_SEPARATION * chord);
}

export const DrivenOffsetMaxCPBounds = { (unitless) : [4, 15, MAX_CONTROL_POINTS] } as IntegerBoundSpec;

/**
 * The approximation controls this feature actually uses.
 *
 * Replaces std's curveApproximationPredicate, five of whose eight fields were dead or
 * actively misleading here:
 *
 *   "Keep start derivative" / "Keep end derivative" were never read. We compute the exact
 *   offset tangent at every run end ourselves and hand it to the solver as a hard
 *   constraint, so there is nothing for the user to keep or discard.
 *
 *   "Maximum deviation" is declared READ_ONLY by that predicate on the understanding that
 *   the feature writes the measured value back. This one never did, so the field sat
 *   permanently blank.
 *
 *   "Approximate" did not switch approximation on or off. Unchecked, it swapped the
 *   user's three numbers for hard-coded ones and fitted exactly the same runs.
 *
 * Worth knowing while reading these: only freeform runs reach the solver at all. Lines,
 * arcs and corner fills are exact constructions and ignore every field here.
 */
export predicate drivenOffsetApproximationPredicate(definition is map)
{
    annotation { "Name" : "Target degree", "Description" : "Degree the fit aims for on freeform runs" }
    isInteger(definition.approximationDegree, DEGREE_BOUND);

    annotation { "Name" : "Tolerance", "Description" : "How far a fitted run may sit from the computed offset points" }
    isLength(definition.approximationTolerance, TOLERANCE_BOUND);

    annotation { "Name" : "Maximum control points", "Description" : "Cap on a fitted run. The fit stops as soon as tolerance is met, so this only binds on a run that cannot reach it." }
    isInteger(definition.approximationMaxCPs, DrivenOffsetMaxCPBounds);
}
