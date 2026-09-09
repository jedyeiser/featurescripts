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

/** Samples per edge used to build the turning-angle table on a reference chain. */
export const TURNING_SAMPLES = 25;

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
    annotation { "Name" : "Offset edges" }
    OFFSET_EDGES,
    annotation { "Name" : "Reference" }
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
 */
export function expandEdgeQuery(selection is Query) returns Query
{
    const directEdges = qEntityFilter(selection, EntityType.EDGE);
    const bodies = qEntityFilter(selection, EntityType.BODY);
    const wireEdges = qOwnedByBody(qBodyType(bodies, BodyType.WIRE), EntityType.EDGE);
    const compositeWires = qBodyType(qContainedInCompositeParts(qBodyType(bodies, BodyType.COMPOSITE)), BodyType.WIRE);

    return qUnion([directEdges, wireEdges, qOwnedByBody(compositeWires, EntityType.EDGE)]);
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

    const toCenter = (dot(a, a) * cross(axb, a) + dot(b, b) * cross(b, axb)) / (2 * dot(axb, axb));

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
 * Exact tangent of an offset curve.
 *
 *   P(s)  = C(s) + w(s) * W + h(s) * H
 *   P'(s) = (1 - w * kappaW - h * kappaH) * T + w' * W + h' * H
 *
 * where kappaW and kappaH are the curvatures signed about the width and height
 * axes. Verified against finite differences to 4e-11 on a planar curve. Torsion
 * cross-terms are dropped; they are identically zero for a planar chain, which
 * is what the offset frame assumes elsewhere.
 *
 * The leading factor is also the degeneracy test: at or below zero the offset
 * has reached the centre of curvature and the result would fold back on itself.
 *
 * @returns {map} : { "direction" : unit Vector, "shrink" : number }
 */
export function offsetTangent(frame is map, offsets is map, slopes is map) returns map
{
    const shrink = 1 - offsets.width * frame.curvatureWidth - offsets.height * frame.curvatureHeight;
    const direction = shrink * frame.tangent + slopes.width * frame.widthAxis + slopes.height * frame.heightAxis;

    return {
        "direction" : (norm(direction) < 1e-9) ? frame.tangent : normalize(direction),
        "shrink" : shrink
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
    catch (error)
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

    const startX = edges[0].startPoint[0];
    const endX = edges[size(edges) - 1].endPoint[0];

    if (endX < startX)
    {
        pathToUse = reverse(pathToUse);
        edges = describeEdges(context, pathToUse);
    }

    var linkLength = 0 * meter;
    for (var edgeData in edges)
    {
        linkLength += edgeData.length;
    }

    return { "path" : pathToUse, "edges" : edges, "length" : linkLength, "startArc" : 0 * meter };
}

/**
 * Per-edge metadata for one path: two kernel calls per edge, both batched.
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

    return finishStations(raw, chain.zeroArc);
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

    for (var edge in edges)
    {
        const curve = evApproximateBSplineCurve(context, { "edge" : edge });
        const knots = curve.knots;
        const samples = evaluateSpline({
                    "spline" : curve,
                    "parameters" : range(knots[0], knots[size(knots) - 1], SEED_SAMPLES)
                })[0];

        const xStart = samples[0][0];
        const xEnd = samples[size(samples) - 1][0];
        const edgeMin = min(xStart, xEnd) - zeroX;
        const edgeMax = max(xStart, xEnd) - zeroX;

        // A profile edge with no X extent is a step: the offset jumps there. It is
        // not a function of X, so it must never be used for a lookup -- inverting
        // x(u) on it returns an arbitrary point on the jump. Record where it is and
        // let the two neighbouring edges own the coordinates on either side.
        if (edgeMax - edgeMin <= OFFSET_GEOM_TOL)
        {
            steps = append(steps, 0.5 * (edgeMin + edgeMax));
            continue;
        }

        checkMonotonicX(samples, edge);

        described = append(described, {
                    "query" : edge,
                    "curve" : curve,
                    "minCoord" : edgeMin,
                    "maxCoord" : edgeMax
                });
    }

    if (size(described) == 0)
    {
        throw regenError("Every offset profile edge is vertical, so the profile never defines an offset.", selection);
    }

    described = sort(described, function(a, b)
        {
            return (a.minCoord - b.minCoord) / meter;
        });

    return {
        "edges" : described,
        "steps" : steps,
        "overlaps" : overlappingEdges(described),
        "zeroX" : zeroX,
        "minCoord" : described[0].minCoord,
        "maxCoord" : largestCoord(described)
    };
}

/**
 * Largest coordinate any profile edge reaches. Not simply the last edge's, since
 * edges are sorted by where they start.
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
 * Pairs of profile edges whose coordinate ranges overlap.
 *
 * Overlap means two different offsets are defined at the same coordinate, so the
 * profile is not a function and any lookup there is a coin toss. Reported rather
 * than thrown, because a hair of overlap at a shared vertex is normal.
 */
function overlappingEdges(described is array) returns array
{
    var found = [];

    for (var i = 0; i < size(described) - 1; i += 1)
    {
        const overlap = described[i].maxCoord - described[i + 1].minCoord;
        if (overlap > OFFSET_GEOM_TOL)
        {
            found = append(found, { "first" : i, "second" : i + 1, "overlap" : overlap });
        }
    }

    return found;
}

/**
 * Coordinates at which the profile has a slope discontinuity: the shared X of two
 * profile edges. A station landing here has two valid offset vectors, so the
 * caller splits the output there instead of averaging them into a smooth lie.
 */
export function profileJunctions(profile is map) returns array
{
    var junctions = profile.steps;

    for (var i = 0; i < size(profile.edges) - 1; i += 1)
    {
        const gap = abs(profile.edges[i + 1].minCoord - profile.edges[i].maxCoord);
        if (gap < OFFSET_GEOM_TOL)
        {
            junctions = append(junctions, profile.edges[i].maxCoord);
        }
    }

    return junctions;
}

/**
 * Look up offsets and slopes at many coordinates at once.
 *
 * Coordinates are grouped by the profile edge that owns them, so each edge is
 * inverted and evaluated once for its whole group.
 *
 * @param coords {array} : coordinates (ValueWithUnits), profile X minus zeroX.
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

    for (var i = 0; i < size(coords); i += 1)
    {
        const edgeIndex = profileEdgeFor(profile, coords[i], preferLower);
        if (edgeIndex == undefined)
        {
            results[i] = undefined;
            continue;
        }
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
function profileEdgeFor(profile is map, coord is ValueWithUnits, preferLower is boolean)
{
    var containing = [];

    for (var i = 0; i < size(profile.edges); i += 1)
    {
        const profileEdge = profile.edges[i];
        if (coord >= profileEdge.minCoord - OFFSET_GEOM_TOL && coord <= profileEdge.maxCoord + OFFSET_GEOM_TOL)
        {
            containing = append(containing, i);
        }
    }

    if (size(containing) == 0)
    {
        return undefined;
    }

    // A coordinate lands on two edges only at a shared end, which is exactly where
    // the two sides of a slope break must be told apart. Taking the first or last
    // deliberately is what gives each side of a break its own tangent.
    return preferLower ? containing[0] : containing[size(containing) - 1];
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
 * @returns {map} : { "chain", "arcs", "thetas", "delta" }
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

    // Rebase so theta is zero at the zero station, matching the coordinate origin.
    const thetaAtZero = interpolate(arcs, thetas, 0 * meter);
    for (var i = 0; i < size(thetas); i += 1)
    {
        thetas[i] = thetas[i] - thetaAtZero;
    }

    // Table for turning world X into arc length on this chain. The samples already
    // carry tangents, so d(arc)/dX is known and the lookup can be cubic Hermite.
    var xs = [];
    var arcSlopes = [];
    for (var sample in samples)
    {
        if (sample.tangent[0] < 1e-6)
        {
            throw regenError("The reference wire doubles back in X, so a position along it is ambiguous.", selection);
        }
        xs = append(xs, sample.x);
        arcSlopes = append(arcSlopes, 1 / sample.tangent[0]);
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
        "tangents" : tangents,
        "delta" : delta,
        "planeNormal" : planeNormal
    };
}

/**
 * The frame of the reference surface at a world X.
 *
 * The reference surface is the reference wire extruded along its plane normal.
 * Its own normal is planeNormal x tangent, so a height offset moves off the
 * surface while tangent and width offsets slide along it -- which is what keeps
 * a point's height above the reference fixed when it moves in length or width.
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

    for (var i = 0; i < count - 1; i += 1)
    {
        if (x > xs[i + 1])
        {
            continue;
        }

        const span = (xs[i + 1] - xs[i]) / meter;
        if (abs(span) < 1e-15)
        {
            return vectors[i];
        }

        const t = ((x - xs[i]) / meter) / span;
        const blended = (1 - t) * vectors[i] + t * vectors[i + 1];

        return (norm(blended) < 1e-9) ? vectors[i] : normalize(blended);
    }

    return vectors[count - 1];
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

    for (var i = 0; i < count - 1; i += 1)
    {
        if (x > xs[i + 1])
        {
            continue;
        }

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

    return ys[count - 1];
}

/**
 * Sample tangent, curvature and curvature direction along a reference chain.
 * One kernel call per edge.
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
 * Linear interpolation in a monotonically increasing table, clamped at both ends.
 */
export function interpolate(xs is array, ys is array, x) returns number
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

    for (var i = 0; i < count - 1; i += 1)
    {
        if (x <= xs[i + 1])
        {
            const span = (xs[i + 1] - xs[i]) / meter;
            if (abs(span) < 1e-15)
            {
                return ys[i];
            }
            return ys[i] + (ys[i + 1] - ys[i]) * ((x - xs[i]) / meter) / span;
        }
    }

    return ys[count - 1];
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
    var target = { "positions" : points };
    if (startDerivative != undefined)
    {
        target.startDerivative = startDerivative;
    }
    if (endDerivative != undefined)
    {
        target.endDerivative = endDerivative;
    }

    const curves = approximateSpline(context, {
                "degree" : approximation.approximationDegree,
                "tolerance" : approximation.approximationTolerance,
                "isPeriodic" : false,
                "targets" : [approximationTarget(target)],
                "maxControlPoints" : approximation.approximationMaxCPs
            });

    opCreateBSplineCurve(context, id, { "bSplineCurve" : curves[0] });
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
