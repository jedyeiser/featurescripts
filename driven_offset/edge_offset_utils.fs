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

/**
 * A direction vector this short carries no direction. Used wherever a cross product
 * or a sum of vectors may collapse, to decide whether normalizing it means anything.
 */
export const ZERO_DIRECTION = 1e-9;

/** A table span this small is a repeated abscissa: interpolating across it divides by zero. */
export const ZERO_SPAN = 1e-15;

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

/** Newton iterations for inverting x(u) on a profile edge. Three already reach 1e-12 m. */
export const NEWTON_ITERATIONS = 4;

/** Seed samples used to bracket an inversion before Newton refines it. */
export const SEED_SAMPLES = 9;

/**
 * Steps taken to walk a reference station from "same world X" onto the foot of the
 * normal. Two holds a ski's standing height to well under a micron; the seed is
 * already exact for a point lying on the surface, which most of them do.
 */
export const REFERENCE_FOOT_STEPS = 2;

/** How close two profile edge ends must be to count as joined. */
export const PROFILE_JOIN_TOL = 1e-5 * meter;

/** Samples per edge used to build the turning-angle table on a reference chain. */
export const TURNING_SAMPLES = 25;

/** How long the drawn frame axes are: long enough to see, short enough not to clutter. */
export const DEBUG_AXIS_LENGTH = 5 * millimeter;

/** Most frames or offset vectors any debug view will draw. */
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

/**
 * How far back along each run to hunt for the crossing on the inside of a corner.
 *
 * The overlap runs w*tan(theta/2) deep, so the crossing is always close to the corner;
 * searching the whole run would be quadratic for nothing.
 */
export const CORNER_TRIM_WINDOW = 40;

/**
 * How far an extension may reach, as a fraction of the run it extends.
 *
 * An extension fabricates geometry past where the source data stops, so a long one is
 * almost always a setup error rather than an intention. Past this the run is left alone
 * and the reason is printed.
 */
export const TERMINAL_MAX_EXTENSION = 0.25;

/** Length and shaft radius of the arrow marking each end of the chain. */
export const DEBUG_END_ARROW = 25 * millimeter;
export const DEBUG_END_ARROW_RADIUS = 1.5 * millimeter;

/**
 * Newton steps used to walk a straight-line plane crossing onto the osculating arc.
 *
 * The seed is already within a fraction of a percent for any extension short enough to be
 * worth making, and Newton squares the error each step, so this is generous.
 */
export const TERMINAL_NEWTON_STEPS = 4;

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
/**
 * What to do where a G0 corner in the source opens a gap in the offset.
 *
 * Offsetting a corner by w separates the two ends by 2*w*sin(theta/2). Both ends sit
 * exactly w from the shared vertex, so the natural filler is a circular arc centred on
 * that vertex -- and because the offset tangent at each end is perpendicular to its own
 * radius, that arc is tangent to both runs for free, with no fitting.
 */
export enum CornerGapMode
{
    annotation { "Name" : "Round with an arc" }
    ARC,
    annotation { "Name" : "Extend to a sharp corner" }
    EXTEND,
    annotation { "Name" : "Leave open" }
    OPEN
}

/**
 * What to do where a G0 corner makes the offset cross itself.
 *
 * The same corner that gaps on its outside overlaps on its inside, by w*tan(theta/2)
 * along each run. Trimming both back to where they actually cross is the only treatment
 * that leaves a single, non-self-intersecting wire.
 */
export enum CornerOverlapMode
{
    annotation { "Name" : "Trim to the crossing" }
    TRIM,
    annotation { "Name" : "Leave crossing" }
    KEEP
}

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

    if (maxRadial > tolerance || maxOutOfPlane > ARC_PLANARITY_TOL)
    {
        // Carried so a caller can say WHY a run that looks circular was not emitted as one.
        return {
            "kind" : "freeform",
            "radialError" : maxRadial,
            "outOfPlane" : maxOutOfPlane
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
        "outOfPlane" : maxOutOfPlane
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
 * @returns {Vector} : unit direction. The arc-length scale is offsetShrink's job.
 */
export function offsetTangent(frame is map, offsets is map, slopes is map, rates is map) returns Vector
{
    const direction = frame.tangent
        + slopes.width * frame.widthAxis
        + slopes.height * frame.heightAxis
        + offsets.width * rates.width
        + offsets.height * rates.height;

    return (norm(direction) < ZERO_DIRECTION) ? frame.tangent : normalize(direction);
}

/**
 * Offset point that follows the reference surface instead of stepping off it.
 *
 * The straight-line offset P = C + w W + h H is right only where the surface is
 * flat in the direction the width runs. Down the middle of a ski the width axis is
 * along the surface's rulings, which ARE straight, so the two agree to the last
 * bit. At the tip the source edge turns to run across the reference and the width
 * axis swings round onto the reference's own tangent, where a straight step leaves
 * the surface by w^2 / 2R -- half a millimetre on a 200 mm tip kick. A point that
 * started on the reference is supposed to slide along it and stay on it.
 *
 * So the width offset is applied IN the chart: the width axis is perpendicular to
 * the height axis, which is the surface normal, so it always lies in the tangent
 * plane and splits cleanly into a component along the reference and a component
 * along the rulings. Those two are added to the point's own surface coordinates and
 * the result is mapped back. Because the chart is flat, that is exactly a geodesic
 * of length w -- no integration, and no approximation beyond the sample tables.
 *
 * Height is unchanged in kind: it still moves along the surface normal, but at the
 * arc the point has slid to rather than the one it started at.
 *
 * @returns {map} : the placed surface coordinates, the decomposition used to get
 *          them, and "point", the world position.
 */
export function surfaceOffset(alongRef is map, frame is map, offsets is map) returns map
{
    // referenceSurfaceCoords is a two-step Newton foot-point solve over the reference
    // tables, and it depends on alongRef and frame.origin ONLY -- nothing about the offsets.
    // It was being redone for every station of every profile, again at every run end through
    // surfaceOffsetTangent, and again per station in ruled mode. resolveFrames now solves it
    // once per station and leaves it here; the fallback keeps every other caller working.
    const surf = (frame.surfCoords != undefined)
        ? frame.surfCoords
        : referenceSurfaceCoords(alongRef, frame.origin);
    const alpha = dot(frame.widthAxis, surf.tangent);
    const beta = dot(frame.widthAxis, alongRef.planeNormal);

    // The height axis is the surface normal up to the +Z sign referenceHeightAxisAt
    // applies, so this is exactly +1 or -1, never anything between.
    const heightSign = (dot(frame.heightAxis, surf.normal) < 0) ? -1 : 1;

    // alpha is a distance along the offset curve; the tables are keyed by arc along
    // the wire, and the two run at a ratio of scale.
    const advance = (abs(surf.scale) < 1e-9) ? 0 * meter : offsets.width * alpha / surf.scale;
    const arc = surf.arc + advance;
    const v = surf.v + offsets.width * beta;
    const height = surf.height + heightSign * offsets.height;

    return {
        "surf" : surf,
        "arc" : arc,
        "v" : v,
        "height" : height,
        "alpha" : alpha,
        "beta" : beta,
        "heightSign" : heightSign,
        "point" : referenceSurfacePoint(alongRef, arc, v, height)
    };
}

/**
 * Exact tangent of the surface-following offset.
 *
 * With the reference parameterized by arc u along the wire, the offset curve is
 * A(u), the surface is A(u) + v n, and dt/du = kappa N, dN/du = -kappa t. Writing
 * the offset point as A(u) + v n + H N(u),
 *
 *   P'(s) = u' (scale - kappa H) t  +  v' n  +  H' N        [t, N at the placed u]
 *
 * and each rate comes from the source point's own travel through the chart:
 *
 *   u0' = dot(T, t) / (scale - kappa h0)      v0' = dot(T, n)     h0' = dot(T, N)
 *   u'  = u0' + (w' alpha + w dot(W', t)) / scale
 *   v'  = v0' + w' beta + w dot(W', n)
 *   H'  = h0' + heightSign * h'
 *
 * The N component of W' drops out of both -- W stays perpendicular to the surface
 * normal -- which is why the scalar roll this used to carry was not enough and the
 * full dW/ds is wanted.
 *
 * This supersedes offsetTangent in reference-driven modes rather than extending it:
 * the two are tangents to different maps, and they agree only where the width
 * offset is zero or the reference is locally straight.
 *
 * @param rates {map} : { "width" : dW/ds, "height" : dH/ds }, per unit length.
 */
export function surfaceOffsetTangent(alongRef is map, frame is map, offsets is map, slopes is map,
    rates is map) returns map
{
    const placed = surfaceOffset(alongRef, frame, offsets);
    const surf = placed.surf;
    const target = referenceBasisAtArc(alongRef, placed.arc);

    const denominator = surf.scale - surf.curvature * surf.height;
    const sourceRate = (abs(denominator) < 1e-9) ? 0 : dot(frame.tangent, surf.tangent) / denominator;

    // d(scale)/du is dropped. It is -delta * d(kappa)/du: second order in delta, and
    // identically zero whenever the reference is not being measured at an offset.
    const arcRate = sourceRate
        + (slopes.width * placed.alpha + offsets.width * dot(rates.width, surf.tangent)) / surf.scale;
    const vRate = dot(frame.tangent, alongRef.planeNormal)
        + slopes.width * placed.beta
        + offsets.width * dot(rates.width, alongRef.planeNormal);
    const heightRate = dot(frame.tangent, surf.normal) + placed.heightSign * slopes.height;

    const direction = arcRate * (target.scale - target.curvature * placed.height) * target.tangent
        + vRate * alongRef.planeNormal
        + heightRate * target.normal;

    return {
        "direction" : (norm(direction) < 1e-9) ? frame.tangent : normalize(direction),
        "point" : placed.point,
        "surfaceShrink" : target.scale - target.curvature * placed.height
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
                            "edgeIndex" : edgeIndex
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

            welded[i] = withCurvatureVector(mergeMaps(left, { "tangent" : shared }), sharedCurvature);
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
 * @returns {map} : { "edges", "steps", "doublesBack", "minCoord", "maxCoord", "zeroX" }.
 *          "edges" is in traversal order along the profile chain, NOT sorted by X --
 *          see orderProfileEdges. That is why smallestCoord/largestCoord exist.
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
 * @returns {Vector} : the surface normal, signed towards +Z.
 */
export function referenceHeightAxisAt(alongRef is map, x is ValueWithUnits) returns Vector
{
    const tangent = interpolateVector(alongRef.xs, alongRef.tangents, x);
    const surfaceNormal = normalize(cross(alongRef.planeNormal, tangent));

    return (surfaceNormal[2] < 0) ? -1 * surfaceNormal : surfaceNormal;
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
    if (abs(span) < ZERO_SPAN)
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
 * Cubic Hermite lookup in an increasing table with known slopes.
 *
 * Outside the table it EXTRAPOLATES along the end slope -- it does not clamp. A
 * lookup that runs off the end therefore returns a straight-line continuation of
 * the curve, silently. interpolate() flat-clamps instead; the two are not
 * interchangeable at the ends.
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
    if (abs(span / meter) < ZERO_SPAN)
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

// ============================================================================
// The reference surface as a chart
// ============================================================================

/**
 * d(offset point)/d(wire arc) at one reference sample.
 *
 * The tables are keyed by arc length along the wire, but the curve they describe is
 * the wire offset by delta, and a parallel curve runs (1 - delta * kappa) times as
 * fast as its parent. So this is not the unit tangent unless delta is zero.
 */
function referenceRate(alongRef is map, index is number) returns Vector
{
    return (1 - alongRef.delta * alongRef.curvatures[index]) * alongRef.tangents[index];
}

/**
 * Position on the delta-offset reference at an arc length along the wire.
 *
 * Cubic Hermite through the sampled positions with the rate above as the slope --
 * the same stencil as hermiteAt, which only takes scalars.
 */
export function referencePointAtArc(alongRef is map, arc is ValueWithUnits) returns Vector
{
    const arcs = alongRef.arcs;
    const points = alongRef.points;
    const count = size(arcs);

    if (arc <= arcs[0])
    {
        return points[0] + (arc - arcs[0]) * referenceRate(alongRef, 0);
    }
    if (arc >= arcs[count - 1])
    {
        return points[count - 1] + (arc - arcs[count - 1]) * referenceRate(alongRef, count - 1);
    }

    const i = spanIndex(arcs, arc);
    const span = arcs[i + 1] - arcs[i];
    if (abs(span / meter) < ZERO_SPAN)
    {
        return points[i];
    }

    const f = (arc - arcs[i]) / span;
    const f2 = f * f;
    const f3 = f2 * f;

    return (2 * f3 - 3 * f2 + 1) * points[i]
        + (f3 - 2 * f2 + f) * span * referenceRate(alongRef, i)
        + (-2 * f3 + 3 * f2) * points[i + 1]
        + (f3 - f2) * span * referenceRate(alongRef, i + 1);
}

/**
 * Tangent, surface normal, curvature and parallel scale at an arc length.
 *
 * The normal is cross(planeNormal, tangent) with no sign correction applied, so
 * that dN/d(wire arc) = -kappa * t holds and the tangent formula below can rely on
 * it. referenceHeightAxisAt flips that normal toward +Z before handing it to a frame as
 * a height axis; that flip is a display convention, and it is reapplied where the
 * profile's height is added rather than being baked in here.
 */
export function referenceBasisAtArc(alongRef is map, arc is ValueWithUnits) returns map
{
    const tangent = interpolateVector(alongRef.arcs, alongRef.tangents, arc);
    const curvature = interpolate(alongRef.arcs, alongRef.curvatures, arc);

    return {
        "tangent" : tangent,
        "normal" : normalize(cross(alongRef.planeNormal, tangent)),
        "curvature" : curvature,
        "scale" : 1 - alongRef.delta * curvature
    };
}

/**
 * Surface coordinates of a world point: where it sits on the reference surface and
 * how far off it.
 *
 * The surface is a planar curve swept along that plane's own normal -- a cylinder.
 * Its Gaussian curvature is zero, so (arc, v) is a FLAT chart on it: distances
 * measured in the chart are true distances on the surface, and a straight line in
 * the chart is a geodesic. That is the whole reason a width offset can be applied
 * by adding to a coordinate rather than by integrating along the surface.
 *
 * Every point reconstructs exactly as A(arc) + v * planeNormal + height * N(arc).
 *
 * The seed station is the one at the same world X, which is already the foot of the
 * normal for a point lying on the surface. Each step then slides it by the leftover
 * tangential component, which is Newton on dot(P - A(u), t(u)) = 0 with the
 * curvature term dropped. The seed degenerates where the reference turns vertical
 * and the source curve stops advancing in X -- the ski tip, exactly where this
 * matters -- and the walk is what recovers from that.
 */
export function referenceSurfaceCoords(alongRef is map, point is Vector) returns map
{
    var arc = referenceArcAtX(alongRef, point[0]);
    var basis = referenceBasisAtArc(alongRef, arc);
    var toPoint = point - referencePointAtArc(alongRef, arc);

    for (var step = 0; step < REFERENCE_FOOT_STEPS; step += 1)
    {
        if (abs(basis.scale) > 1e-9)
        {
            arc = arc + dot(toPoint, basis.tangent) / basis.scale;
            basis = referenceBasisAtArc(alongRef, arc);
            toPoint = point - referencePointAtArc(alongRef, arc);
        }
    }

    return mergeMaps(basis, {
                "arc" : arc,
                "v" : dot(toPoint, alongRef.planeNormal),
                "height" : dot(toPoint, basis.normal)
            });
}

/**
 * The world point at given surface coordinates. Inverse of referenceSurfaceCoords.
 */
export function referenceSurfacePoint(alongRef is map, arc is ValueWithUnits, v is ValueWithUnits,
    height is ValueWithUnits) returns Vector
{
    const basis = referenceBasisAtArc(alongRef, arc);

    return referencePointAtArc(alongRef, arc) + v * alongRef.planeNormal + height * basis.normal;
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
    if (abs(span) < ZERO_SPAN)
    {
        return ys[i];
    }

    return ys[i] + (ys[i + 1] - ys[i]) * ((x - xs[i]) / meter) / span;
}

// ============================================================================
// Output
// ============================================================================

/**
 * A circular arc from p1 to p2 centred on the corner vertex, or undefined if no such
 * circle exists.
 *
 * This is the exact filler for an offset corner and needs no fitting: both ends lie at
 * the offset distance from the vertex, and an offset curve's tangent is perpendicular to
 * its own offset direction, so a circle centred there meets both runs tangentially by
 * construction.
 *
 * It stops being available when the two ends are NOT co-radial about the vertex -- a
 * width step exactly at the corner, or a height offset that differs across it. Then the
 * caller falls back to arcLikeSpline.
 */
export function cornerArc(vertex is Vector, p1 is Vector, p2 is Vector, tolerance is ValueWithUnits)
{
    const a = p1 - vertex;
    const b = p2 - vertex;
    const ra = norm(a);
    const rb = norm(b);

    if (ra < tolerance || rb < tolerance || abs(ra - rb) > tolerance)
    {
        return undefined;
    }

    const axis = cross(a, b);
    if (norm(axis) < ZERO_DIRECTION * ra * rb)
    {
        // Collinear: either no corner at all, or a full reversal with no unique plane.
        return undefined;
    }

    const bisector = normalize(a) + normalize(b);
    if (norm(bisector) < ZERO_DIRECTION)
    {
        return undefined;
    }

    return {
        "kind" : "arc",
        "center" : vertex,
        "normal" : normalize(axis),
        "radius" : ra,
        "start" : p1,
        "mid" : vertex + ra * normalize(bisector),
        "end" : p2
    };
}

/**
 * A cubic that is tangent to both ends and as close to a circular arc as a cubic gets.
 *
 * Handle length (4/3)*tan(theta/4)*R is the standard best cubic approximation to a
 * circular arc: it matches position and tangent at both ends and its radial error peaks
 * at about 0.03% of R for a quarter turn. Curvature is not exactly constant, but it
 * varies smoothly and stays within a fraction of a percent -- which is what "resembles
 * an arc" has to mean once the ends are not co-radial and a true arc is unavailable.
 *
 * @param t1 {Vector} : unit tangent leaving p1, in the direction of travel.
 * @param t2 {Vector} : unit tangent arriving at p2, in the direction of travel.
 */
export function arcLikeSpline(p1 is Vector, t1 is Vector, p2 is Vector, t2 is Vector) returns BSplineCurve
{
    const chord = norm(p2 - p1);
    const turn = angleBetween(t1, t2);

    // Degenerate turn: a straight cubic is the arc of infinite radius.
    var handle = chord / 3;
    if (turn / radian > ZERO_DIRECTION && chord > 0 * meter)
    {
        const radius = chord / (2 * sin(turn / 2));
        handle = (4.0 / 3.0) * tan(turn / 4) * radius;
    }

    return bSplineCurve({
                "degree" : 3,
                "isPeriodic" : false,
                "controlPoints" : [p1, p1 + handle * t1, p2 - handle * t2, p2]
            });
}

/**
 * Closest approach between two segments, as parameters on each plus the midpoint.
 *
 * Used to find where two offset runs cross on the inside of a corner. In 3D they rarely
 * meet exactly, so the midpoint of the closest approach is the crossing for our purposes;
 * both runs are trimmed to it, which is what makes them share an endpoint exactly.
 *
 * @returns {map} : { "distance", "point", "s", "t" } with s, t in [0, 1].
 */
export function segmentApproach(a0 is Vector, a1 is Vector, b0 is Vector, b1 is Vector) returns map
{
    const u = a1 - a0;
    const v = b1 - b0;
    const w0 = a0 - b0;

    const a = dot(u, u);
    const b = dot(u, v);
    const c = dot(v, v);
    const d = dot(u, w0);
    const e = dot(v, w0);

    const denominator = a * c - b * b;

    var sc = 0;
    var tc = 0;

    if (abs(denominator / (meter * meter * meter * meter)) < ZERO_SPAN)
    {
        // Parallel: pin one end and solve the other.
        sc = 0;
        tc = (c > 0 * meter * meter) ? clamp(e / c, 0, 1) : 0;
    }
    else
    {
        sc = clamp((b * e - c * d) / denominator, 0, 1);
        tc = clamp((a * e - b * d) / denominator, 0, 1);
    }

    const pa = a0 + sc * u;
    const pb = b0 + tc * v;

    return {
        "distance" : norm(pa - pb),
        "point" : 0.5 * (pa + pb),
        "s" : sc,
        "t" : tc
    };
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
 * How far along `direction` from `origin` the plane lies, or undefined when parallel.
 *
 * The sign is the whole answer to what a terminal end needs: positive means the plane is
 * ahead and the offset stopped short of it, negative means the offset already ran past.
 */
export function planeCrossingDistance(origin is Vector, direction is Vector, pl is Plane)
{
    const approach = dot(direction, pl.normal);

    if (abs(approach) < ZERO_DIRECTION)
    {
        return undefined;
    }

    return dot(pl.origin - origin, pl.normal) / approach;
}

/**
 * A point on the osculating circle, `distance` of arc beyond the origin.
 *
 * Straight-line extension is the wrong tool at a rockered tip or any other curved end --
 * it flies off the arc immediately. This matches position, tangent and curvature at the
 * join, so a short extension continues the curve the run was already describing.
 */
export function osculatingAt(origin is Vector, tangent is Vector, curvature is Vector,
    distance is ValueWithUnits) returns Vector
{
    const kappa = norm(curvature);

    if (kappa * meter < ZERO_DIRECTION)
    {
        return origin + distance * tangent;
    }

    const turn = (kappa * distance) * radian;

    return origin + (sin(turn) / kappa) * tangent + ((1 - cos(turn)) / kappa) * (curvature / kappa);
}

/**
 * The tangent of that same circle at that same distance.
 */
export function osculatingTangentAt(tangent is Vector, curvature is Vector,
    distance is ValueWithUnits) returns Vector
{
    const kappa = norm(curvature);

    if (kappa * meter < ZERO_DIRECTION)
    {
        return tangent;
    }

    const turn = (kappa * distance) * radian;

    return cos(turn) * tangent + sin(turn) * (curvature / kappa);
}

/**
 * Where the osculating extension of a curve end meets a plane.
 *
 * The straight-line crossing seeds it and Newton walks that onto the actual arc. The
 * derivative is the arc's own tangent rather than a difference, so each step is exact and
 * a handful of them is plenty.
 *
 * @returns {map} : { "distance", "point", "tangent" }, or undefined when the end runs
 *                  parallel to the plane and never reaches it.
 */
export function osculatingCrossing(origin is Vector, tangent is Vector, curvature is Vector,
    pl is Plane)
{
    var distance = planeCrossingDistance(origin, tangent, pl);

    if (distance == undefined)
    {
        return undefined;
    }

    for (var i = 0; i < TERMINAL_NEWTON_STEPS; i += 1)
    {
        const rate = dot(osculatingTangentAt(tangent, curvature, distance), pl.normal);

        if (abs(rate) < ZERO_DIRECTION)
        {
            break;
        }

        distance -= dot(osculatingAt(origin, tangent, curvature, distance) - pl.origin, pl.normal) / rate;
    }

    return {
        "distance" : distance,
        "point" : osculatingAt(origin, tangent, curvature, distance),
        "tangent" : osculatingTangentAt(tangent, curvature, distance)
    };
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
 * Where a segment pair straddling a plane crosses it, as a fraction from `a` to `b`.
 *
 * Returns undefined when both ends sit on the same side, which is how the caller knows it
 * has not yet walked far enough back along the run.
 */
export function planeStraddle(a is Vector, b is Vector, pl is Plane)
{
    const sideA = dot(a - pl.origin, pl.normal);
    const sideB = dot(b - pl.origin, pl.normal);

    if ((sideA > 0 * meter) == (sideB > 0 * meter))
    {
        return undefined;
    }

    const span = sideA - sideB;

    if (abs(span) < OFFSET_GEOM_TOL)
    {
        return a;
    }

    return a + (sideA / span) * (b - a);
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

// ============================================================================
// Offset tangents, moved here so the feature, treatment and debug tabs can all
// reach them. Pure computation over stations and coordinates; no context needed
// beyond what the caller already holds.
// ============================================================================

/**
 * Whether length and width are also taken from the reference, rather than only
 * height. The height axis follows the reference either way.
 */
export function isConstrained(definition is map, alongRef) returns boolean
{
    return alongRef != undefined && definition.constrainProfile == true;
}

/**
 * Whether this station is placed on the reference surface rather than by a straight
 * step from the source point.
 *
 * The one test that picks the map: surfaceOffset/surfaceOffsetTangent when true,
 * offsetPoints' straight step and offsetTangent when false. The two are tangents to
 * different maps, so placement and direction must agree on this or the fitted end
 * tangent describes a curve the points do not lie on.
 */
export function usesReferenceFrame(definition is map, alongRef) returns boolean
{
    return alongRef != undefined && definition.frameAlignment == OffsetFrameAlignment.ALONG;
}

/**
 * Offset position at every station. Stations the profile does not reach get undefined.
 *
 * The (1 - w * kappa) factor is checked here: at or below zero the offset has passed
 * the centre of curvature, and the result would fold back through itself.
 */

/**
 * Exact offset tangent at one end of a run, for the fit to interpolate.
 *
 * Everything needed is determined at a run end: the source curve has a tangent and
 * a curvature there, the profile has a value and a slope there, and the frame has a
 * rate of turn there. Only that turn used to be missing, so reference-driven frames
 * were handed back undefined rather than a tangent that quietly ignored it, and the
 * fit was left to guess from the point cloud. frameRates supplies it, so every run
 * end gets its exact tangent -- which is what holds a junction G1, and what pins the
 * last curve of a chain onto the mirror plane at the tip.
 */
export function runTangent(stations is array, coords is map, offsets is array, definition is map, alongRef,
    run is map, index is number)
{
    if (offsets[index] == undefined)
    {
        return undefined;
    }

    const frame = stations[index];
    const amounts = { "width" : offsets[index].width, "height" : offsets[index].height };
    const slopes = {
            "width" : offsets[index].widthSlope * coords.scales[index],
            "height" : offsets[index].heightSlope * coords.scales[index]
        };
    const rates = frameRates(stations, run, index);

    // The two are tangents to different maps, so which one applies follows exactly
    // the same test that decides which map placed the points.
    if (usesReferenceFrame(definition, alongRef))
    {
        return surfaceOffsetTangent(alongRef, frame, amounts, slopes, rates).direction;
    }

    return offsetTangent(frame, amounts, slopes, rates);
}

/**
 * How fast the offset frame's axes turn at one station, per unit length.
 *
 * Differenced from neighbouring stations rather than derived in closed form. The
 * frame is a composition of the source curve, a reference lookup and a projection;
 * differencing it is exact for whatever that composition turns out to be, costs no
 * kernel calls, assumes nothing about the axes being mutually perpendicular, and
 * stays correct if any part of the composition changes later.
 *
 * Samples are taken inwards from the run end, so they never cross an edge junction
 * or a profile break into a frame belonging to the other side. Second order where
 * the run has three stations to work with, first order where it has only two.
 *
 * @returns {map} : { "width" : dW/ds, "height" : dH/ds }
 */
export function frameRates(stations is array, run is map, index is number) returns map
{
    const still = { "width" : vector(0, 0, 0) / meter, "height" : vector(0, 0, 0) / meter };
    const step = (index == run.end) ? -1 : 1;
    const one = index + step;

    if (one < run.start || one > run.end)
    {
        return still;
    }

    const here = stations[index];
    const near = stations[one];
    const h1 = stations[one].arc - stations[index].arc;

    if (abs(h1) < TOLERANCE.zeroLength * meter)
    {
        return still;
    }

    const secant = {
            "width" : (near.widthAxis - here.widthAxis) / h1,
            "height" : (near.heightAxis - here.heightAxis) / h1
        };

    const two = index + 2 * step;
    if (two < run.start || two > run.end)
    {
        return secant;
    }

    const far = stations[two];
    const h2 = stations[two].arc - stations[index].arc;

    if (abs(h2) < TOLERANCE.zeroLength * meter || abs(h2 - h1) < TOLERANCE.zeroLength * meter)
    {
        return secant;
    }

    // Three-point one-sided derivative on uneven spacing, applied to each axis.
    // Stations inside one edge are evenly spaced in arc length, but an inserted
    // crossing can break that. Written on differences from this station because the
    // stencil's own coefficient for it is identically zero.
    const c1 = h2 / (h1 * (h2 - h1));
    const c2 = h1 / (h2 * (h2 - h1));

    return {
        "width" : c1 * (near.widthAxis - here.widthAxis) - c2 * (far.widthAxis - here.widthAxis),
        "height" : c1 * (near.heightAxis - here.heightAxis) - c2 * (far.heightAxis - here.heightAxis)
    };
}

// ==========================================================================
// Feature UI
// ==========================================================================

// Factored out of driven_edge_offset so driven_offset_surface can present the same
// controls without a second copy that drifts. Each is the exact block it replaced.

// These three carry a "driven" prefix because map_curve declares its own
// offsetSpacingPredicate and offsetApproximationPredicate with a different UI -- no
// group wrapper, no join control -- and export-imports this file. Same name, different
// parameters, so they are deliberately kept apart rather than merged.

/** Control-point budget for a fitted run. The floor of 4 is a cubic's minimum. */
/**
 * Consecutive duplicates removed.
 *
 * Stations shared between several profiles carry a crossing pair at EVERY profile's breaks,
 * not just this one's. Where a break is not this profile's the two halves land on the same
 * point, and a run that reads straight through such a crossing holds that point twice.
 * Nothing downstream wants it: an arc through three points with two of them equal has no
 * circumcentre, and a spline fitted across a zero-length span is degenerate.
 */
export function tangentRunMerges(pointsPerProfile is array, runs is array,
    tolerance is ValueWithUnits) returns array
{
    if (size(runs) < 2 || size(pointsPerProfile) == 0)
    {
        return makeArray(max(size(runs) - 1, 0), false);
    }

    var merges = [];

    // One deduped span per profile, EXTENDED as the group grows rather than rebuilt from the
    // anchor on every candidate. Rebuilding made this quadratic -- a group that swallows the
    // chain cost sum-of-spans appends plus the same again to dedup, ~92k element operations
    // per profile at 430 stations. Consecutive-duplicate removal is a streaming operation
    // (withoutRepeats(A ~ B) is withoutRepeats(A) extended by B filtered against the last
    // point kept), so accumulating is exact. classifyPoints still runs over the whole span
    // each time and has to: its chord and midpoint both move as the span grows, so there is
    // no incremental form of it.
    var spans = [];
    for (var points in pointsPerProfile)
    {
        spans = append(spans, extendSpan([], points, runs[0].start, runs[0].end));
    }

    for (var r = 0; r + 1 < size(runs); r += 1)
    {
        const here = runs[r];
        const next = runs[r + 1];

        // The end-side records come from runs[r], NOT from the run the group started at.
        // Reading them off the anchor missed a trimmed end or a terminal on any run absorbed
        // after the first, because the anchor's own records had already been checked and
        // found clear. Everything that made a boundary meaningful has to survive it: a
        // trimmed corner carries an exact crossing point, a terminal carries a plane it was
        // cut to, and a corner fill is a separate piece of geometry that belongs between.
        var ok = (next.start == here.end + 1)
            && here.endPoint == undefined && next.startPoint == undefined
            && here.terminalEnd == undefined && next.terminalStart == undefined
            && next.fill == undefined && next.cornerKind == undefined;

        var grown = [];

        if (ok)
        {
            for (var k = 0; k < size(pointsPerProfile); k += 1)
            {
                const span = (spans[k] == undefined)
                    ? undefined
                    : extendSpan(spans[k], pointsPerProfile[k], next.start, next.end);

                if (span == undefined)
                {
                    ok = false;
                    break;
                }

                // The question is not "are the tangents equal" -- two arcs of different radii
                // meet tangentially and are still two arcs, and fusing them into one spline is
                // the trade that puts a curvature swing into the result. It is the stronger
                // one: does the whole accumulated span still describe ONE line or ONE arc.
                const shape = classifyPoints(span, tolerance);

                if (shape.kind != "line" && shape.kind != "arc")
                {
                    ok = false;
                    break;
                }

                grown = append(grown, span);
            }
        }

        merges = append(merges, ok);

        if (ok)
        {
            spans = grown;
        }
        else
        {
            spans = [];
            for (var points in pointsPerProfile)
            {
                spans = append(spans, extendSpan([], points, next.start, next.end));
            }
        }
    }

    return merges;
}

/**
 * Append points[from..to] to an already-deduped array, dropping consecutive duplicates.
 *
 * Returns undefined if any point in the range is missing, which is how a coverage gap vetoes
 * a merge -- there is no single curve across a hole.
 */
function extendSpan(kept is array, points is array, from is number, to is number)
{
    var out = kept;

    for (var i = from; i <= to; i += 1)
    {
        if (points[i] == undefined)
        {
            return undefined;
        }

        if (size(out) > 0 && norm(points[i] - out[size(out) - 1]) < TOLERANCE.zeroLength * meter)
        {
            continue;
        }

        out = append(out, points[i]);
    }

    return out;
}

/**
 * Dissolve the boundaries `merges` marks, keeping each surviving run's outer records.
 */
export function applyRunMerges(runs is array, merges is array) returns array
{
    if (size(runs) < 2)
    {
        return runs;
    }

    var out = [];
    var current = runs[0];

    for (var r = 0; r + 1 < size(runs); r += 1)
    {
        if (merges[r])
        {
            const next = runs[r + 1];

            // The merged run starts where the first did and ends where the second did, so it
            // inherits the first run's start records and the second's end records.
            current = mergeMaps(current, {
                        "end" : next.end,
                        "endPoint" : next.endPoint,
                        "terminalEnd" : next.terminalEnd
                    });
        }
        else
        {
            out = append(out, current);
            current = runs[r + 1];
        }
    }

    return append(out, current);
}

export function withoutRepeats(points is array) returns array
{
    var kept = [];

    for (var point in points)
    {
        if (size(kept) > 0 && norm(point - kept[size(kept) - 1]) < TOLERANCE.zeroLength * meter)
        {
            continue;
        }

        kept = append(kept, point);
    }

    return kept;
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

/**
 * What the offset profile's X axis measures.
 */
export predicate offsetMeasurePredicate(definition is map)
{
    annotation { "Name" : "Measure along", "Default" : MeasureAlong.OFFSET_EDGES, "Description" : "What the offset profile's X axis measures: a world X coordinate, distance along the edges being offset, or distance along a separate reference wire. All three are measured from the zero point.", "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL] }
    definition.measureAlong is MeasureAlong;
}

/**
 * The reference wire and how the offset is measured along it.
 *
 * Only shown for MeasureAlong.REFERENCE_WIRE, so it is a predicate of its own rather than
 * part of the measure predicate: a caller offsetting several profiles along one reference
 * shows this once, not once per profile.
 */
export predicate offsetReferencePredicate(definition is map)
{
    if (definition.measureAlong == MeasureAlong.REFERENCE_WIRE)
    {
        annotation { "Group Name" : "Offset spacing definition", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Reference wire", "Filter" : EntityType.BODY && BodyType.WIRE && ConstructionObject.NO, "MaxNumberOfPicks" : 1, "Description" : "The wire that profile X is measured along" }
            definition.referenceWire is Query;

            annotation { "Name" : "Offset delta", "Description" : "Measure along a curve this far from the selected wire, so an offset stated along the core bottom stays stated along the core bottom" }
            isLength(definition.alongOffsetDelta, OffsetHeightBounds);

            annotation { "Name" : "Flip offset delta", "Default" : false, "UIHint" : UIHint.OPPOSITE_DIRECTION }
            definition.flipAlongOffsetDir is boolean;

            annotation { "Name" : "Hold length in the reference surface", "Default" : false, "Description" : "Project the length direction into the reference surface, so a length offset cannot change a point's height above it. Height is always measured normal to the reference, and width always lies in the surface, with or without this." }
            definition.constrainProfile is boolean;
        }
    }
}

/**
 * The edges being offset. Shared by every profile a caller drives.
 */
export predicate offsetEdgesPredicate(definition is map)
{
    annotation { "Name" : "Offset edges", "Filter" : (EntityType.EDGE || BodyType.WIRE) && ConstructionObject.NO }
    definition.offsetEdges is Query;
}

/**
 * How the offset frame is oriented at each station.
 */
export predicate offsetAlignmentPredicate(definition is map)
{
    annotation { "Name" : "Offset alignment", "Default" : OffsetFrameAlignment.ALONG, "Description" : "How the offset frame is oriented at each point along the offset edges" }
    definition.frameAlignment is OffsetFrameAlignment;
}

/**
 * What happens where a G0 corner gaps or crosses.
 */
export predicate offsetCornersPredicate(definition is map)
{
    annotation { "Group Name" : "Corners", "Collapsed By Default" : true }
    {
        annotation { "Name" : "Where the offset gaps", "Default" : CornerGapMode.ARC, "UIHint" : UIHint.SHOW_LABEL, "Description" : "A G0 corner in the offset edges separates the two offsets by 2 * width * sin(angle/2) on the outside of the turn. Rounding uses a true circular arc centred on the corner vertex wherever one exists, and an arc-like cubic where it does not." }
        definition.cornerGapMode is CornerGapMode;

        annotation { "Name" : "Where the offset crosses", "Default" : CornerOverlapMode.TRIM, "UIHint" : UIHint.SHOW_LABEL, "Description" : "The same corner overlaps on the inside of the turn, by width * tan(angle/2) along each side. Trimming cuts both back to where they actually cross." }
        definition.cornerOverlapMode is CornerOverlapMode;
    }
}

/**
 * Where the offset stops, and at what slope.
 */
export predicate offsetEndsPredicate(definition is map)
{
    annotation { "Group Name" : "Ends", "Collapsed By Default" : true }
    {
        annotation { "Name" : "Start plane", "Filter" : BodyType.MATE_CONNECTOR || (EntityType.FACE && GeometryType.PLANE), "MaxNumberOfPicks" : 1, "Description" : "Terminate the start of the offset on this plane. Offsetting moves an endpoint off wherever the source ended, by however far the source tangent is from square; this puts it back on a plane you choose. The offset is trimmed if it runs past and extended if it stops short." }
        definition.startPlane is Query;

        annotation { "Name" : "Start direction", "Filter" : QueryFilterCompound.ALLOWS_DIRECTION || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1, "Description" : "Optional. The slope the offset should have where it meets the start plane, for ends that arrive oblique -- a triangular swallowtail meeting the centreline, say. Left empty, the offset continues along its own curvature. Which way round the selection points does not matter." }
        definition.startDirection is Query;

        annotation { "Name" : "End plane", "Filter" : BodyType.MATE_CONNECTOR || (EntityType.FACE && GeometryType.PLANE), "MaxNumberOfPicks" : 1, "Description" : "Terminate the end of the offset on this plane. Independent of the start plane: a chain from FCP to ACP ends on two planes at different orientations." }
        definition.endPlane is Query;

        annotation { "Name" : "End direction", "Filter" : QueryFilterCompound.ALLOWS_DIRECTION || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1, "Description" : "Optional. As the start direction, for the other end." }
        definition.endDirection is Query;

        annotation { "Name" : "Allow extension", "Default" : true, "Description" : "Off, an offset that stops short of its terminal plane is left alone rather than extended. Extension fabricates geometry past where the source data stops, which is not always wanted." }
        definition.allowExtension is boolean;
    }
}

/**
 * The point every coordinate is measured from.
 */
export predicate offsetZeroPredicate(definition is map)
{
    annotation { "Name" : "Zero point", "Filter" : BodyType.MATE_CONNECTOR || EntityType.VERTEX, "MaxNumberOfPicks" : 1 }
    definition.offsetRefPoint is Query;
}

/**
 * How densely the offset is evaluated, and how it is fitted.
 */
export predicate drivenOffsetSpacingPredicate(definition is map)
{
    annotation { "Group Name" : "Spacing & approximation", "Collapsed By Default" : true }
    {
        annotation { "Name" : "Offset spacing", "Description" : "How many points to evaluate along each offset edge", "UIHint" : UIHint.HORIZONTAL_ENUM, "Default" : OffsetPointSpacing.CTRL_POINT }
        definition.edgeOffsetSpacingDef is OffsetPointSpacing;

        if (definition.edgeOffsetSpacingDef == OffsetPointSpacing.CTRL_POINT)
        {
            annotation { "Name" : "Control point multiplier" }
            isInteger(definition.ctrlPointMultiplier, CtrlPointMultiplierBounds);
        }
        if (definition.edgeOffsetSpacingDef == OffsetPointSpacing.NUM_POINTS)
        {
            annotation { "Name" : "Points per edge" }
            isInteger(definition.pointsPerEdge, PointsPerEdgeBounds);
        }
        if (definition.edgeOffsetSpacingDef == OffsetPointSpacing.DISTANCE_ALONG)
        {
            annotation { "Name" : "Point spacing" }
            isLength(definition.targetPointSpacing, PointSpacingBounds);
        }

        annotation { "Name" : "Join runs into one wire per link", "Default" : true, "Description" : "Extract the emitted curves into a single wire body per connected chain. Off leaves every run, corner fill and trimmed piece as its own curve body." }
        definition.joinOutput is boolean;

        annotation { "Name" : "Join tangent segments into one edge", "Default" : false, "Description" : "Where two adjacent runs together still describe a single line or a single arc, emit them as one edge instead of two. Runs split at every source edge boundary whether or not the offset actually breaks there, so this removes joints that are an artefact of the input rather than of the geometry. A pair that would only fit as a spline is left split, because fusing two tangent arcs of different radii into one spline loses the exact curvature both of them had." }
        definition.joinTangentRuns is boolean;

        annotation { "Group Name" : "Approximation parameters", "Collapsed By Default" : true }
        {
            drivenOffsetApproximationPredicate(definition);
        }
    }
}

/**
 * Reporting. Nothing here changes geometry.
 */
export predicate offsetDebugPredicate(definition is map)
{
    annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
    {
        annotation { "Name" : "Print from chain", "Default" : false }
        definition.debugPrintFromChain is boolean;

        annotation { "Name" : "Print along chain", "Default" : false }
        definition.debugPrintAlongChain is boolean;

        annotation { "Name" : "Print profile chain", "Default" : false }
        definition.debugPrintProfileChain is boolean;

        annotation { "Name" : "Show offset frames", "Default" : false, "Description" : "Draw the width and height axes at each station" }
        definition.debugShowOffsetFrames is boolean;

        annotation { "Name" : "Show offsets", "Default" : false, "Description" : "Draw each source point to its offset point" }
        definition.debugShowOffsets is boolean;

        annotation { "Name" : "Show reference offset", "Default" : false, "Description" : "Draw the curve the coordinate is actually measured along -- the reference wire moved by the offset delta. Invisible otherwise: it is neither the wire you picked nor anything in the output." }
        definition.debugShowReference is boolean;

        annotation { "Name" : "Show chain ends", "Default" : false, "Description" : "Arrow at each end of the chain pointing the way the offset is heading there: green for the start, red for the end. This is the sense the Ends group works in -- an extension travels along the arrow, and a terminal direction is flipped to agree with it." }
        definition.debugShowChainEnds is boolean;

        annotation { "Name" : "Visualize continuity", "Default" : false, "Description" : "Mark where the output is split into separate curves" }
        definition.debugVisualizeContinuity is boolean;

        annotation { "Name" : "Print offset table", "Default" : false, "Description" : "Every station's coordinate, offset and resulting point, grouped by source edge" }
        definition.debugPrintOffsetTable is boolean;

        annotation { "Name" : "Print frame table", "Default" : false, "Description" : "Every station's tangent, width axis and height axis, grouped by source edge" }
        definition.debugPrintFrameTable is boolean;
    }
}
