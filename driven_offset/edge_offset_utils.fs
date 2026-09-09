FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

/**
 * Math and geometry utilities for driven_edge_offset and unwrap.
 *
 * Everything these features need lives in this document -- no cross-document imports.
 *
 * Vocabulary
 *   CHAIN     an ordered set of G0-connected LINKS; a link is one Path.
 *             Chains are oriented so increasing arc length means increasing world X.
 *   STATION   one evaluation point on a chain, carrying its frame and coordinate.
 *   COORD     the value looked up in the offset profile. What it measures is set
 *             by MeasureAlong: a world X coordinate, arc length along the edges
 *             being offset, or distance along a separate reference wire. All three
 *             are measured from the zero point.
 *   FRAME     tangent + width axis + height axis. A profile point (x, y, z) offsets
 *             by y along width and z along height at coordinate x.
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

/** Newton iterations for inverting x(u) on a profile edge. Three reaches 1e-12 m. */
export const NEWTON_ITERATIONS = 4;

/** Seed samples used to bracket an inversion before Newton refines it. */
export const SEED_SAMPLES = 9;

/** How close two profile edge ends must be to count as joined. */
export const PROFILE_JOIN_TOL = 1e-5 * meter;

/** Samples per edge used to build the turning-angle table on a reference chain. */
export const TURNING_SAMPLES = 25;

/** Most frames or offset vectors any debug view will draw. Each costs a sketch solve. */
export const DEBUG_MAX_MARKERS = 60;

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

export const MAX_STATIONS_PER_EDGE = 200;
export const MIN_STATIONS_PER_EDGE = 5;

// ============================================================================
// Enums and bounds
// ============================================================================

/**
 * What the offset profile's X axis measures.
 *
 * Labels are kept short so the horizontal enum fits on one row; nothing in
 * FeatureScript can widen the dialog, so label length is the only lever.
 */
export enum MeasureAlong
{
    annotation { "Name" : "World X" }
    WORLD_X,
    annotation { "Name" : "Along source" }
    OFFSET_EDGES,
    annotation { "Name" : "Along reference" }
    REFERENCE_WIRE
}

/** How the offset frame is oriented at each station. */
export enum OffsetFrameAlignment
{
    annotation { "Name" : "World" }
    WORLD,
    annotation { "Name" : "Along" }
    ALONG
}

/** How many points to evaluate along each edge. */
export enum OffsetPointSpacing
{
    annotation { "Name" : "Control points" }
    CTRL_POINT,
    annotation { "Name" : "Points per edge" }
    NUM_POINTS,
    annotation { "Name" : "Distance along" }
    DISTANCE_ALONG
}

export const OffsetHeightBounds = { (millimeter) : [-50, 0, 50] } as LengthBoundSpec;
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

// ============================================================================
// Pure math -- no Context, no kernel calls
// ============================================================================

/**
 * Invert x(u) = target on a B-spline, for many targets at once.
 *
 * A coarse seed table brackets each target by linear interpolation, then every
 * target takes its Newton steps together: one evaluateSpline call per iteration
 * for the whole batch, instead of a bracket-and-bisect search per target.
 * Requires x to be monotonic over the curve's parameter range.
 *
 * @param curve {BSplineCurve} : the curve to invert.
 * @param targets {array} : world X values (ValueWithUnits).
 * @returns {array} : one parameter per target, clamped to the knot range.
 */
export function paramsAtX(curve is BSplineCurve, targets is array) returns array
{
    const knots = curve.knots;
    const uMin = knots[0];
    const uMax = knots[size(knots) - 1];

    const seedParams = range(uMin, uMax, SEED_SAMPLES);
    const seedPoints = evaluateSpline({ "spline" : curve, "parameters" : seedParams })[0];

    var params = [];
    for (var target in targets)
    {
        params = append(params, seedParam(seedParams, seedPoints, target, uMin, uMax));
    }

    for (var iteration = 0; iteration < NEWTON_ITERATIONS; iteration += 1)
    {
        const evaluated = evaluateSpline({ "spline" : curve, "parameters" : params, "nDerivatives" : 1 });
        var converged = true;

        for (var i = 0; i < size(params); i += 1)
        {
            const residual = (evaluated[0][i][0] - targets[i]) / meter;
            const slope = evaluated[1][i][0] / meter;

            if (abs(slope) < 1e-12)
            {
                continue;
            }
            if (abs(residual) > 1e-12)
            {
                converged = false;
            }
            params[i] = clamp(params[i] - residual / slope, uMin, uMax);
        }

        if (converged)
        {
            break;
        }
    }

    return params;
}

/**
 * Linear-interpolation seed for paramsAtX, from the bracketing seed samples.
 * Targets beyond either end clamp to the nearer end of the curve.
 */
function seedParam(seedParams is array, seedPoints is array, target, uMin is number, uMax is number) returns number
{
    for (var i = 0; i < size(seedPoints) - 1; i += 1)
    {
        const f0 = (seedPoints[i][0] - target) / meter;
        const f1 = (seedPoints[i + 1][0] - target) / meter;

        if (f0 * f1 <= 0)
        {
            if (abs(f1 - f0) < 1e-15)
            {
                return seedParams[i];
            }
            return seedParams[i] + (seedParams[i + 1] - seedParams[i]) * (-f0) / (f1 - f0);
        }
    }

    const distToStart = abs((seedPoints[0][0] - target) / meter);
    const distToEnd = abs((seedPoints[size(seedPoints) - 1][0] - target) / meter);

    return (distToStart <= distToEnd) ? uMin : uMax;
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
 * Distance from a point to a circle in 3D.
 */
function distanceToCircle(point is Vector, circleData is map) returns ValueWithUnits
{
    const toPoint = point - circleData.center;
    const outOfPlane = dot(toPoint, circleData.normal);
    const inPlane = norm(toPoint - outOfPlane * circleData.normal);
    const radial = (inPlane - circleData.radius) / meter;

    return sqrt(radial * radial + (outOfPlane / meter) ^ 2) * meter;
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
    const count = size(points);
    const first = points[0];
    const last = points[count - 1];

    if (count < 3)
    {
        return { "kind" : "line", "start" : first, "end" : last };
    }

    const chord = last - first;
    if (norm(chord) > OFFSET_GEOM_TOL)
    {
        const chordDir = normalize(chord);
        var maxLineError = 0 * meter;

        for (var i = 1; i < count - 1; i += 1)
        {
            const toPoint = points[i] - first;
            const lateral = norm(toPoint - dot(toPoint, chordDir) * chordDir);
            if (lateral > maxLineError)
            {
                maxLineError = lateral;
            }
        }

        if (maxLineError <= tolerance)
        {
            return { "kind" : "line", "start" : first, "end" : last };
        }
    }

    const midIndex = floor(count / 2);
    const fit = circleThrough(first, points[midIndex], last);
    if (fit == undefined)
    {
        return { "kind" : "freeform" };
    }

    var maxArcError = 0 * meter;
    for (var point in points)
    {
        const error = distanceToCircle(point, fit);
        if (error > maxArcError)
        {
            maxArcError = error;
        }
    }

    if (maxArcError > tolerance)
    {
        return { "kind" : "freeform" };
    }

    return {
        "kind" : "arc",
        "start" : first,
        "mid" : points[midIndex],
        "end" : last,
        "center" : fit.center,
        "radius" : fit.radius,
        "normal" : fit.normal
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

/**
 * Arc-length scale factor of the offset at a station: how much faster or slower
 * the offset curve runs than the curve it is offset from.
 *
 * At or below zero the offset has reached the centre of curvature and would fold
 * back through itself, so this doubles as the degeneracy test.
 */
export function offsetShrink(frame is map, offsets is map) returns number
{
    return 1 - offsets.width * frame.curvatureWidth - offsets.height * frame.curvatureHeight;
}

/**
 * Exact tangent of an offset curve.
 *
 *   P(s)  = C(s) + w(s) * W(s) + h(s) * H(s)
 *   P'(s) = T + w' W + h' H + w W' + h H'
 *
 * The last two terms are the frame turning as the offset is carried along. They
 * vanish only for a frame that is constant; the chain's own transported frame is
 * not that, and a frame slaved to a reference surface is further from it still,
 * because the reference normal turns on its own schedule as the source curve runs
 * across it. Dropping them was the reason this function used to be refused in
 * reference-driven modes, with the fit left to guess its own end tangents.
 *
 * W' and H' are handed in rather than reconstructed. An earlier version carried
 * only the scalar r = dot(W', H) and rebuilt the rest from the curvatures as
 * W' = -kappaW T + r H. That expansion needs {T, W, H} orthonormal, and it is not:
 * stationFrame leaves the length axis unprojected unless "length and width along
 * reference" is on, so T and H are not perpendicular there and the reconstruction
 * came out up to 4 degrees wrong. The two forms agree to 1e-6 degrees wherever the
 * frame IS orthonormal, so this is a strict generalization, not a change of intent.
 *
 * @param rates {map} : { "width" : dW/ds, "height" : dH/ds }, per unit length.
 * @returns {map} : { "direction" : unit Vector, "shrink" : number }
 */
export function offsetTangent(frame is map, offsets is map, slopes is map, rates is map) returns map
{
    const direction = frame.tangent
        + slopes.width * frame.widthAxis
        + slopes.height * frame.heightAxis
        + offsets.width * rates.width
        + offsets.height * rates.height;

    return {
        "direction" : (norm(direction) < 1e-9) ? frame.tangent : normalize(direction),
        "shrink" : offsetShrink(frame, offsets)
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
 *   { arc, coordArc, origin, tangent, normal, widthAxis, heightAxis,
 *     curvatureWidth, curvatureHeight, linkIndex, edgeIndex, atEdgeEnd }
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

            for (var i = 0; i < count; i += 1)
            {
                // Both sides of a shared vertex are kept. At a G0 junction the two
                // frames genuinely differ, and each output edge needs its own ends.
                const frame = results[i].frame;
                const tangent = edgeData.flipped ? -1 * frame.zAxis : frame.zAxis;

                raw = append(raw, {
                            "arc" : edgeData.startArc + fractions[i] * edgeData.length,
                            "origin" : frame.origin,
                            "tangent" : tangent,
                            "rawNormal" : frame.xAxis,
                            "curvature" : results[i].curvature,
                            "linkIndex" : linkIndex,
                            "edgeIndex" : edgeIndex,
                            "atEdgeEnd" : (i == 0 || i == count - 1)
                        });
            }
        }
    }

    return finishStations(weldJunctions(raw), chain.zeroArc);
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

        if (weld)
        {
            const shared = normalize(sum);
            welded[i] = mergeMaps(left, { "tangent" : shared });
            welded[i + 1] = mergeMaps(welded[i + 1], { "tangent" : shared, "origin" : left.origin });
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
                        "curvatureWidth" : curvatures[i] * dot(towardCentre, axes.widthAxis),
                        "curvatureHeight" : curvatures[i] * dot(towardCentre, axes.heightAxis)
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
// Offset profile
// ============================================================================

/**
 * Build the offset profile: the curve whose X is a coordinate and whose Y and Z
 * are the width and height offsets to apply there.
 *
 * Every edge is stored as an exact B-spline once, so lookups are pure math from
 * then on. X must be monotonic along each edge, which is what makes the profile
 * a function rather than a curve.
 *
 * @param zeroPoint {Vector} : world position whose X is profile coordinate zero.
 * @returns {map} : { "edges" : array ordered by X, "minCoord", "maxCoord", "zeroX" }
 */
export function buildProfile(context is Context, selection is Query, zeroPoint is Vector) returns map
{
    const edges = evaluateQuery(context, expandEdgeQuery(selection));
    if (size(edges) == 0)
    {
        throw regenError("No edges found in the offset profile.", selection);
    }

    const zeroX = zeroPoint[0];
    var described = [];
    var steps = [];

    for (var piece in orderProfileEdges(context, edges))
    {
        const minCoord = piece.start[0] - zeroX;
        const maxCoord = piece.end[0] - zeroX;

        // No X extent: the offset steps here rather than sloping. Inverting x(u) on
        // it would return an arbitrary point on the jump, so it is recorded as a
        // boundary and never used for a lookup.
        if (abs((maxCoord - minCoord) / meter) <= OFFSET_GEOM_TOL / meter)
        {
            steps = append(steps, 0.5 * (minCoord + maxCoord));
            continue;
        }

        const curve = evApproximateBSplineCurve(context, { "edge" : piece.query });
        const knots = curve.knots;
        checkMonotonicX(evaluateSpline({
                        "spline" : curve,
                        "parameters" : range(knots[0], knots[size(knots) - 1], SEED_SAMPLES)
                    })[0], piece.query);

        described = append(described, {
                    "query" : piece.query,
                    "curve" : curve,
                    "minCoord" : min(minCoord, maxCoord),
                    "maxCoord" : max(minCoord, maxCoord)
                });
    }

    if (size(described) == 0)
    {
        throw regenError("Every offset profile edge is vertical, so the profile never defines an offset.", selection);
    }

    return {
        "edges" : described,
        "steps" : steps,
        "doublesBack" : doublingBacks(described),
        "zeroX" : zeroX,
        "minCoord" : smallestCoord(described),
        "maxCoord" : largestCoord(described)
    };
}

/**
 * Order profile edges the way the profile is drawn, and orient each one so it runs
 * in increasing X.
 *
 * Ordering follows endpoint connectivity, because that is what "the direction the
 * point is approached from" means when the profile doubles back. Where connectivity
 * runs out -- a disjoint piece, or a selection that also caught a baseline line --
 * the next run simply starts at the unused edge furthest back in X.
 *
 * Deliberately not constructPaths: that throws on a branching selection, and a
 * profile with an extra line touching it is a drawing to be read, not an error.
 *
 * @returns {array} : each { "query", "start", "end" }, in traversal order.
 */
function orderProfileEdges(context is Context, edges is array) returns array
{
    var starts = [];
    var ends = [];
    for (var edge in edges)
    {
        const tangentLines = evEdgeTangentLines(context, { "edge" : edge, "parameters" : [0, 1] });
        starts = append(starts, tangentLines[0].origin);
        ends = append(ends, tangentLines[1].origin);
    }

    var used = makeArray(size(edges), false);
    var ordered = [];

    for (var placed = 0; placed < size(edges); placed += 1)
    {
        var index = undefined;
        var flipped = false;

        // Continue from the tip of the run we are on, if anything joins it.
        if (size(ordered) > 0)
        {
            const tip = ordered[size(ordered) - 1].end;
            for (var i = 0; i < size(edges); i += 1)
            {
                if (used[i])
                {
                    continue;
                }
                if (norm(starts[i] - tip) < PROFILE_JOIN_TOL)
                {
                    index = i;
                    flipped = false;
                    break;
                }
                if (norm(ends[i] - tip) < PROFILE_JOIN_TOL)
                {
                    index = i;
                    flipped = true;
                    break;
                }
            }
        }

        // Otherwise start a new run at whatever is left that begins furthest back.
        if (index == undefined)
        {
            for (var i = 0; i < size(edges); i += 1)
            {
                if (used[i])
                {
                    continue;
                }
                const low = min(starts[i][0], ends[i][0]);
                if (index == undefined || low < min(starts[index][0], ends[index][0]))
                {
                    index = i;
                }
            }
            flipped = ends[index][0] < starts[index][0];
        }

        used[index] = true;
        ordered = append(ordered, {
                    "query" : edges[index],
                    "start" : flipped ? ends[index] : starts[index],
                    "end" : flipped ? starts[index] : ends[index]
                });
    }

    return ordered;
}

/**
 * Smallest coordinate any profile edge reaches. Not simply the first edge's: the
 * edges are in traversal order, and a profile that doubles back can reach further
 * back than where it starts.
 */
function smallestCoord(described is array) returns ValueWithUnits
{
    var result = described[0].minCoord;
    for (var profileEdge in described)
    {
        if (profileEdge.minCoord < result)
        {
            result = profileEdge.minCoord;
        }
    }

    return result;
}

/**
 * Largest coordinate any profile edge reaches. Not simply the last edge's, since a
 * profile that doubles back can reach its furthest point before its final edge.
 */
function largestCoord(described is array) returns ValueWithUnits
{
    var result = described[0].maxCoord;
    for (var profileEdge in described)
    {
        if (profileEdge.maxCoord > result)
        {
            result = profileEdge.maxCoord;
        }
    }

    return result;
}

/**
 * Places where the profile, walked in order, covers a coordinate it has already
 * covered. Informational: the chain walk resolves which offset applies, but it is
 * worth saying so, because it is also what an accidentally selected baseline edge
 * looks like.
 */
function doublingBacks(described is array) returns array
{
    var found = [];
    var reached = described[0].maxCoord;

    for (var i = 1; i < size(described); i += 1)
    {
        if (described[i].minCoord < reached - OFFSET_GEOM_TOL)
        {
            found = append(found, {
                        "edge" : i,
                        "overlap" : reached - described[i].minCoord
                    });
        }
        if (described[i].maxCoord > reached)
        {
            reached = described[i].maxCoord;
        }
    }

    return found;
}

/**
 * A profile edge that doubles back in X cannot define a single offset, so say so
 * against the offending edge rather than returning a silently wrong lookup.
 *
 * Vertical edges never reach here: buildProfile records them as steps first. That
 * matters, because a vertical edge's X-steps are all ~0, which leaves both the
 * increasing and decreasing flags true and would pass this check.
 */
function checkMonotonicX(samples is array, edge is Query)
{
    var increasing = true;
    var decreasing = true;

    for (var i = 0; i < size(samples) - 1; i += 1)
    {
        const step = (samples[i + 1][0] - samples[i][0]) / meter;
        if (step < -1e-12)
        {
            increasing = false;
        }
        if (step > 1e-12)
        {
            decreasing = false;
        }
    }

    if (!increasing && !decreasing)
    {
        throw regenError("An offset profile edge doubles back in X, so it does not define a single offset.", edge);
    }
}

/**
 * Coordinates where two profile edges meet, plus every vertical step. These are
 * the only places the offset can break.
 */
export function profileBoundaries(profile is map) returns array
{
    var boundaries = profile.steps;

    for (var i = 0; i < size(profile.edges) - 1; i += 1)
    {
        const gap = abs(profile.edges[i + 1].minCoord - profile.edges[i].maxCoord);
        if (gap < OFFSET_GEOM_TOL)
        {
            boundaries = append(boundaries, profile.edges[i].maxCoord);
        }
    }

    return sort(boundaries, function(a, b)
        {
            return (a - b) / meter;
        });
}

/**
 * Look up offsets and slopes at many coordinates at once.
 *
 * Coordinates are grouped by the profile edge that owns them, so each edge is
 * inverted and evaluated once for its whole group.
 *
 * @param coords {array} : coordinates (ValueWithUnits), profile X minus zeroX,
 *        in ascending order -- the walk relies on it.
 * @param preferLower {boolean} : at an exact junction, take the lower-X edge.
 * @returns {array} : one entry per coordinate, or undefined where the profile does
 *          not reach: { "profileEdge", "width", "height", "widthSlope", "heightSlope" }.
 *          Slopes are per unit coordinate (dy/dx, dz/dx), unitless.
 */
export function profileAt(profile is map, coords is array, preferLower is boolean) returns array
{
    var results = makeArray(size(coords));

    // One bucket per profile edge, so each edge is inverted once for its whole group.
    var groups = makeArray(size(profile.edges), []);
    var cursor = 0;

    for (var i = 0; i < size(coords); i += 1)
    {
        const edgeIndex = locateCoord(profile, cursor, coords[i], preferLower);
        if (edgeIndex == undefined)
        {
            results[i] = undefined;
            continue;
        }
        cursor = edgeIndex;
        groups[edgeIndex] = append(groups[edgeIndex], i);
    }

    for (var edgeIndex = 0; edgeIndex < size(groups); edgeIndex += 1)
    {
        const indices = groups[edgeIndex];
        if (size(indices) == 0)
        {
            continue;
        }
        const profileEdge = profile.edges[edgeIndex];

        var targets = [];
        for (var index in indices)
        {
            targets = append(targets, coords[index] + profile.zeroX);
        }

        const params = paramsAtX(profileEdge.curve, targets);
        const evaluated = evaluateSpline({ "spline" : profileEdge.curve, "parameters" : params, "nDerivatives" : 1 });

        for (var j = 0; j < size(indices); j += 1)
        {
            const position = evaluated[0][j];
            const derivative = evaluated[1][j];
            const dx = derivative[0] / meter;

            results[indices[j]] = {
                "profileEdge" : edgeIndex,
                "width" : position[1],
                "height" : position[2],
                "widthSlope" : (abs(dx) < 1e-12) ? 0 : (derivative[1] / meter) / dx,
                "heightSlope" : (abs(dx) < 1e-12) ? 0 : (derivative[2] / meter) / dx
            };
        }
    }

    return results;
}

/**
 * Index of the profile edge owning a coordinate, or undefined if none does.
 */
function locateCoord(profile is map, cursor is number, coord is ValueWithUnits, preferLower is boolean)
{
    const edges = profile.edges;

    // Two passes: forward from where the last lookup left off, then wrapping to the
    // start. The profile is walked in the order it is drawn, so a coordinate belongs
    // to the edge we have reached, not to whichever edge also happens to span it.
    // The wrap covers a profile running backwards relative to the source, and any
    // gap the walk stepped over.
    for (var pass = 0; pass < 2; pass += 1)
    {
        const from = (pass == 0) ? cursor : 0;
        const to = (pass == 0) ? size(edges) : cursor;

        for (var index = from; index < to; index += 1)
        {
            if (coord < edges[index].minCoord - OFFSET_GEOM_TOL || coord > edges[index].maxCoord + OFFSET_GEOM_TOL)
            {
                continue;
            }

            // At a shared end, the approach direction picks the side: coming up to
            // the boundary keeps the edge below it, leaving it takes the edge above.
            // This has to apply on both passes -- a coordinate resolved by the wrap
            // must get the same junction-side rule as one resolved by the walk, or
            // the upper/lower pair that splits a discontinuity stops agreeing.
            if (!preferLower && index + 1 < size(edges)
                && coord >= edges[index].maxCoord - OFFSET_GEOM_TOL
                && coord >= edges[index + 1].minCoord - OFFSET_GEOM_TOL
                && coord <= edges[index + 1].maxCoord + OFFSET_GEOM_TOL)
            {
                return index + 1;
            }

            return index;
        }
    }

    return undefined;
}

// ============================================================================
// Reference chain (ALONG_REF)
// ============================================================================

/**
 * Build the reference chain that profile coordinates are spaced along.
 *
 * Carries a cumulative turning-angle table, which is all that is needed to place
 * coordinates on a curve offset from this one:
 *
 *     distance along the curve offset by h  =  s - h * theta(s)
 *
 * theta is invariant under offsetting, so one table serves every offset distance
 * and no offset curve is ever constructed. Checked numerically against directly
 * measured offset curves to 1e-8 relative at h = +/-10 mm and +50 mm, including
 * across an inflection.
 *
 * @param delta {ValueWithUnits} : signed offset of the reference from the selected wire.
 * @returns {map} : { "chain", "arcs", "thetas", "curvatures", "xs", "arcSlopes",
 *          "points", "tangents", "delta", "planeNormal" }. "points" is the offset
 *          curve in world space, carried for the debug view only.
 */
export function buildAlongReference(context is Context, selection is Query, zeroPoint is Vector, delta is ValueWithUnits) returns map
{
    const chain = buildChain(context, selection, zeroPoint);
    const samples = sampleTurning(context, chain);
    const planeNormal = referencePlaneNormal(samples);

    var arcs = [];
    var thetas = [];
    var curvatures = [];
    var theta = 0;
    var previousArc = undefined;
    var previousCurvature = undefined;

    for (var sample in samples)
    {
        // Curvature signed so that T' = kappa * (planeNormal x T), which is the
        // convention under which offsetting by h scales arc length by (1 - h * kappa).
        const offsetDir = cross(planeNormal, sample.tangent);
        const curvature = sample.curvature * dot(sample.towardCentre, offsetDir);

        if (previousArc != undefined)
        {
            theta += 0.5 * (curvature + previousCurvature) * (sample.arc - previousArc);
        }

        arcs = append(arcs, sample.arc);
        thetas = append(thetas, theta);
        curvatures = append(curvatures, curvature);
        previousArc = sample.arc;
        previousCurvature = curvature;
    }

    // Table for turning world X into arc length on this chain. Everything the
    // stations ask of this reference is wanted on the reference OFFSET BY DELTA,
    // never on the wire itself, so the table is keyed by the offset curve's X.
    //
    // A parallel curve shares its parent's tangent and normal directions exactly,
    // so the frame at a given parameter does not move -- only the X that parameter
    // sits at does, by delta * offsetDir_x. That is nothing in the flat middle of a
    // ski base and tens of millimetres up the tip kick, which is precisely where
    // the frame was coming out wrong.
    //
    //   offset(s) = C(s) + delta * offsetDir(s),   d(offsetDir)/ds = -kappa * T
    //   so   dX_offset/ds = T_x * (1 - delta * kappa)
    //
    // arcSlopes stays d(arc along the wire)/dX, since arcs is what it interpolates.
    // points is the offset curve itself, in world space. Nothing in the offset
    // arithmetic needs it -- delta moves the coordinate, never a point -- so it
    // exists to be drawn. That the curve cannot be seen is exactly why a delta
    // that was landing in the wrong place was hard to catch.
    var xs = [];
    var arcSlopes = [];
    var points = [];
    for (var i = 0; i < size(samples); i += 1)
    {
        const sample = samples[i];
        const offsetDir = normalize(cross(planeNormal, sample.tangent));
        const slope = sample.tangent[0] * (1 - delta * curvatures[i]);

        if (slope < 1e-6)
        {
            throw regenError("The reference wire, offset by the offset delta, doubles back in X, "
                    ~ "so a position along it is ambiguous. Reduce the offset delta.", selection);
        }

        xs = append(xs, sample.x + delta * offsetDir[0]);
        arcSlopes = append(arcSlopes, 1 / slope);
        points = append(points, sample.point + delta * offsetDir);
    }

    // Coordinate zero is the point of the OFFSET reference at the zero point's X.
    // sampleTurning zeroed the arcs on the point of the wire itself at that X,
    // which is a different station whenever the offset direction leans in X, so
    // rebase both tables onto the offset curve's own origin.
    const originArc = hermiteAt(xs, arcs, arcSlopes, zeroPoint[0]);
    const thetaAtOrigin = interpolate(arcs, thetas, originArc);
    for (var i = 0; i < size(arcs); i += 1)
    {
        thetas[i] = thetas[i] - thetaAtOrigin;
        arcs[i] = arcs[i] - originArc;
    }

    var tangents = [];
    for (var sample in samples)
    {
        tangents = append(tangents, sample.tangent);
    }

    return {
        "chain" : chain,
        "arcs" : arcs,
        "thetas" : thetas,
        "curvatures" : curvatures,
        "xs" : xs,
        "arcSlopes" : arcSlopes,
        "points" : points,
        "tangents" : tangents,
        "delta" : delta,
        "planeNormal" : planeNormal
    };
}

/**
 * The frame of the reference surface at a world X.
 *
 * The reference surface is the reference wire, offset by delta, extruded along its
 * plane normal. Its own normal is planeNormal x tangent, so a height offset moves
 * off the surface while tangent and width offsets slide along it -- which is what
 * keeps a point's height above the reference fixed when it moves in length or width.
 *
 * The offset is already baked into alongRef.xs, so x is read on the offset curve
 * and no delta appears here: a parallel curve's tangent and normal at corresponding
 * points are its parent's.
 *
 * stationFrame deliberately takes only heightAxis from this. Length keeps following
 * the edge being offset, because where the source runs across the reference -- a tip
 * curling round while the reference runs fore-aft -- the reference's own tangent
 * would put width along the direction the source is travelling.
 *
 * The width axis is signed for +Y independently of planeNormal's own orientation,
 * which is pinned by the turning-angle sign convention and must not be flipped.
 *
 * @returns {map} : { "tangent", "widthAxis", "heightAxis" }
 */
export function referenceFrameAt(alongRef is map, x is ValueWithUnits) returns map
{
    const tangent = interpolateVector(alongRef.xs, alongRef.tangents, x);
    const surfaceNormal = normalize(cross(alongRef.planeNormal, tangent));
    const widthAxis = (alongRef.planeNormal[1] < 0) ? -1 * alongRef.planeNormal : alongRef.planeNormal;

    return {
        "tangent" : tangent,
        "widthAxis" : widthAxis,
        "heightAxis" : (surfaceNormal[2] < 0) ? -1 * surfaceNormal : surfaceNormal
    };
}

/**
 * Linear interpolation of a unit direction in an increasing table, renormalized.
 */
export function interpolateVector(xs is array, vectors is array, x) returns Vector
{
    const count = size(xs);
    if (x <= xs[0])
    {
        return vectors[0];
    }
    if (x >= xs[count - 1])
    {
        return vectors[count - 1];
    }

    const i = spanIndex(xs, x);
    const span = (xs[i + 1] - xs[i]) / meter;
    if (abs(span) < 1e-15)
    {
        return vectors[i];
    }

    const t = ((x - xs[i]) / meter) / span;
    const blended = (1 - t) * vectors[i] + t * vectors[i + 1];

    return (norm(blended) < 1e-9) ? vectors[i] : normalize(blended);
}

/**
 * Arc length along the reference chain at a world X.
 *
 * Cubic Hermite through the turning samples, using the tangents they already
 * carry. At 25 samples per edge this lands within 0.01 micron of the true arc
 * length; plain linear interpolation on the same table is off by 6 microns.
 */
export function referenceArcAtX(alongRef is map, x is ValueWithUnits) returns ValueWithUnits
{
    return hermiteAt(alongRef.xs, alongRef.arcs, alongRef.arcSlopes, x);
}

/**
 * Cubic Hermite lookup in an increasing table with known slopes, clamped at both ends.
 *
 * @param slopes {array} : dy/dx at each sample.
 */
export function hermiteAt(xs is array, ys is array, slopes is array, x)
{
    const count = size(xs);
    if (x <= xs[0])
    {
        return ys[0] + slopes[0] * (x - xs[0]);
    }
    if (x >= xs[count - 1])
    {
        return ys[count - 1] + slopes[count - 1] * (x - xs[count - 1]);
    }

    const i = spanIndex(xs, x);
    const span = xs[i + 1] - xs[i];
    if (abs(span / meter) < 1e-15)
    {
        return ys[i];
    }

    const t = (x - xs[i]) / span;
    const t2 = t * t;
    const t3 = t2 * t;

    return ys[i] * (2 * t3 - 3 * t2 + 1)
        + span * slopes[i] * (t3 - 2 * t2 + t)
        + ys[i + 1] * (-2 * t3 + 3 * t2)
        + span * slopes[i + 1] * (t3 - t2);
}

/**
 * Sample position, tangent, curvature and curvature direction along a reference
 * chain. One kernel call per edge.
 */
function sampleTurning(context is Context, chain is map) returns array
{
    var samples = [];

    for (var link in chain.links)
    {
        for (var edgeData in link.edges)
        {
            const fractions = range(0, 1, TURNING_SAMPLES);
            var parameters = [];
            for (var fraction in fractions)
            {
                parameters = append(parameters, edgeParam(edgeData, fraction));
            }

            const results = evEdgeCurvatures(context, { "edge" : edgeData.query, "parameters" : parameters });

            for (var i = 0; i < TURNING_SAMPLES; i += 1)
            {
                const frame = results[i].frame;

                samples = append(samples, {
                            "arc" : edgeData.startArc + fractions[i] * edgeData.length - chain.zeroArc,
                            "x" : frame.origin[0],
                            "point" : frame.origin,
                            "tangent" : edgeData.flipped ? -1 * frame.zAxis : frame.zAxis,
                            "towardCentre" : frame.xAxis,
                            "curvature" : results[i].curvature
                        });
            }
        }
    }

    return samples;
}

/**
 * Plane normal of a reference chain, fixed once for the whole chain.
 *
 * Taken from the most strongly curved sample, where the kernel normal is most
 * trustworthy, and oriented so that the positive offset direction (planeNormal x
 * tangent) points along +Z -- the "greatest Z" rule the frame convention uses.
 * A per-sample choice would be unstable: for an XZ-planar chain the binormal is
 * almost entirely Y, so its Z component carries no usable sign.
 */
function referencePlaneNormal(samples is array) returns Vector
{
    var best = undefined;
    var bestCurvature = 0 / meter;

    for (var sample in samples)
    {
        if (best == undefined || sample.curvature > bestCurvature)
        {
            best = sample;
            bestCurvature = sample.curvature;
        }
    }

    if (bestCurvature < ZERO_CURVATURE)
    {
        throw regenError("The reference wire is straight everywhere, so it has no offset plane. Use a different offset definition.");
    }

    var planeNormal = normalize(cross(best.tangent, best.towardCentre));
    if (cross(planeNormal, best.tangent)[2] < 0)
    {
        planeNormal = -1 * planeNormal;
    }

    return planeNormal;
}

/**
 * Coordinate of an arc-length position, measured along the reference offset by delta.
 */
export function alongCoordinate(alongRef is map, arc is ValueWithUnits) returns ValueWithUnits
{
    return arc - alongRef.delta * interpolate(alongRef.arcs, alongRef.thetas, arc);
}

/**
 * Index of the span containing x: the smallest i in [0, n-2] with x <= xs[i + 1].
 *
 * This reproduces a forward linear scan exactly, including its behaviour on the
 * duplicate values that appear wherever two edges meet -- both take the first span
 * of a duplicate pair. Verified against the scan over 73,318 probes on tables built
 * like sampleTurning's, with zero mismatches. At 350 samples it is 19x fewer
 * comparisons, and these tables are read several times per station.
 */
export function spanIndex(xs is array, x) returns number
{
    var low = 0;
    var high = size(xs) - 2;

    while (low < high)
    {
        const mid = floor((low + high) / 2);
        if (x <= xs[mid + 1])
        {
            high = mid;
        }
        else
        {
            low = mid + 1;
        }
    }

    return low;
}

/**
 * Linear interpolation in a monotonically increasing table, clamped at both ends.
 *
 * Deliberately untyped in its return: the tables it reads hold plain numbers
 * (turning angle, d(arc)/dX) in some places and ValueWithUnits (curvature) in
 * others, and a `returns number` here rejects the latter as a map.
 */
export function interpolate(xs is array, ys is array, x)
{
    const count = size(xs);
    if (x <= xs[0])
    {
        return ys[0];
    }
    if (x >= xs[count - 1])
    {
        return ys[count - 1];
    }

    const i = spanIndex(xs, x);
    const span = (xs[i + 1] - xs[i]) / meter;
    if (abs(span) < 1e-15)
    {
        return ys[i];
    }

    return ys[i] + (ys[i + 1] - ys[i]) * ((x - xs[i]) / meter) / span;
}

// ============================================================================
// Output
// ============================================================================

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
 * Emit a freeform curve through the offset points.
 *
 * End derivatives come from the exact offset tangent rather than from the fitted
 * points, so a junction that should stay G1 stays G1 by construction.
 *
 * @param approximation {map} : the feature's curveApproximationPredicate fields.
 */
export function emitSplineCurve(context is Context, id is Id, points is array, startDerivative, endDerivative, approximation is map)
{
    // approximateSpline parameterizes the fit over [0, 1], so the natural derivative
    // magnitude at an endpoint is the run's total chord, not 1. Handing it a unit
    // vector asks for near-zero velocity there, which bulges the curve near the
    // junction -- measured at ~0.45 mm in curveMapping/wrapCurve.fs:568.
    var chord = 0 * meter;
    for (var i = 0; i < size(points) - 1; i += 1)
    {
        chord += norm(points[i + 1] - points[i]);
    }

    var target = { "positions" : points };
    if (startDerivative != undefined)
    {
        target.startDerivative = startDerivative * chord;
    }
    if (endDerivative != undefined)
    {
        target.endDerivative = endDerivative * chord;
    }

    const curves = approximateSpline(context, {
                "degree" : approximation.approximationDegree,
                "tolerance" : approximation.approximationTolerance,
                "isPeriodic" : false,
                "targets" : [approximationTarget(target)],
                "maxControlPoints" : approximation.approximationMaxCPs
            });

    opCreateBSplineCurve(context, id, { "bSplineCurve" : snapEnds(curves[0], points) });
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
 * Right-align text in a fixed-width column.
 */
export function padLeft(text is string, width is number) returns string
{
    const deficit = width - length(text);

    return (deficit > 0) ? repeatString(" ", deficit) ~ text : text;
}

/**
 * Left-align text in a fixed-width column.
 */
export function padRight(text is string, width is number) returns string
{
    const deficit = width - length(text);

    return (deficit > 0) ? text ~ repeatString(" ", deficit) : text;
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

    return padLeft(toString(roundToPrecision(value / millimeter, decimals)), width);
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

    return padLeft(toString(roundToPrecision(value, decimals)), width);
}

/**
 * A unitless vector as three fixed-width, fixed-decimal columns.
 */
export function fmtVec(v is Vector, decimals is number, width is number) returns string
{
    return fmtNum(v[0], decimals, width) ~ fmtNum(v[1], decimals, width) ~ fmtNum(v[2], decimals, width);
}
