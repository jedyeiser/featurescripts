FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");
// IMPORT: Curve_tools V2 curve_core.fs -- chains, stations, classification, fitting, emitters,
// formatting. Moved there 2026-09-23; export import so every feature importing this tab still
// sees them (and OffsetPointSpacing stays reachable as a parameter type).
export import(path : "2143812a99089658c704f0bc/9e83163997d1397b495d1bfe/02d7784437f621c76397f0d6", version : "9d6f0887be37c851829c40c3");

/**
 * Math and geometry utilities for driven_edge_offset and unwrap.
 *
 * The shared curve machinery lives in curve_core (Curve_tools document), imported above.
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
 * A direction vector this short carries no direction. Used wherever a cross product
 * or a sum of vectors may collapse, to decide whether normalizing it means anything.
 */
export const ZERO_DIRECTION = 1e-9;

/** A table span this small is a repeated abscissa: interpolating across it divides by zero. */
export const ZERO_SPAN = 1e-15;

/** No sideways step: the offset itself, in the displacement form of runTangent. */
export const ZERO_DISPLACEMENT = { "width" : 0 * meter, "height" : 0 * meter };

/**
 * How far a merged arc's radius may differ, relatively, from the radius of the run the merge
 * started at.
 *
 * Fitting tolerance cannot police this. At a 15 metre radius two arcs whose radii differ by
 * 0.03% are positionally indistinguishable over most of a metre, so "do these points all lie
 * within 0.01 mm of one circle" says yes and the merge replaces both radii with a third that
 * was never in the input. On a ski that third radius is not a rounding artefact, it is a
 * sidecut that nobody designed.
 *
 * Compared always against the ANCHOR run's own radius rather than the previous step's, so a
 * long chain of individually-tolerable steps cannot drift the radius away a little at a time.
 */
export const ARC_MERGE_RADIUS_REL = 1e-4;

/**
 * Whether the source geometry under a span of stations can legitimately produce an arc.
 *
 * True only where every station came off a LINE or a CIRCLE. The offset of a circle by a
 * constant amount in its plane is exactly a circle, so an arc there is the right answer; the
 * offset of a spline is not a circle however closely it fits one over a short span.
 */
export function sourceAllowsArc(stations is array, from is number, to is number) returns boolean
{
    return sourceShapeGates(stations, from, to).allowArc;
}

/**
 * What the source under a run permits the run to be classified as.
 *
 * The point cloud alone cannot decide: 42 mm of a wrapped spline sits 13 nm from a circle,
 * and 25 mm of a 15 m arc sits 5 um from its chord -- inside any tolerance that still admits
 * real arcs and lines. So the shape follows the source's own type. An arc is on the table only
 * where every station came off a LINE or a CIRCLE; a line only where every station came off
 * a LINE. A spline source is fitted, however short the run, and carries its curvature across
 * every run joint instead of dropping to zero for a run that happened to be short.
 *
 * Stations without a recorded type -- crossings, chain ends -- do not vote.
 *
 * @returns {map} : { "allowArc", "allowLine" }
 */
export function sourceShapeGates(stations is array, from is number, to is number) returns map
{
    var allowArc = true;
    var allowLine = true;

    for (var i = from; i <= to; i += 1)
    {
        if (i < 0 || i >= size(stations))
        {
            continue;
        }

        const t = stations[i].curveType;

        if (t == undefined)
        {
            continue;
        }

        if (t != CurveType.LINE)
        {
            allowLine = false;
        }

        if (t != CurveType.LINE && t != CurveType.CIRCLE)
        {
            allowArc = false;
        }
    }

    return { "allowArc" : allowArc, "allowLine" : allowLine };
}

/** Newton iterations for inverting x(u) on a profile edge. Three already reach 1e-12 m. */
export const NEWTON_ITERATIONS = 4;

/** Seed samples used to bracket an inversion before Newton refines it. */
export const SEED_SAMPLES = 9;

/**
 * Most Newton steps taken to walk a point's reference foot from "same world X" onto the foot
 * of the normal. The walk stops as soon as the tangential residual is below
 * REFERENCE_FOOT_TOL; a point lying on the surface converges in one, a point well off it
 * (an unwrap target, a source edge high above the core) in three or four.
 */
export const REFERENCE_FOOT_STEPS = 8;

/** Tangential residual at which a reference foot counts as found. */
export const REFERENCE_FOOT_TOL = 1e-10 * meter;

/** How close two profile edge ends must be to count as joined. */
export const PROFILE_JOIN_TOL = 1e-5 * meter;

/** Samples per edge used to build the turning-angle table on a reference chain. */
export const TURNING_SAMPLES = 25;

/** How long the drawn frame axes are: long enough to see, short enough not to clutter. */
export const DEBUG_AXIS_LENGTH = 5 * millimeter;

/** Most frames or offset vectors any debug view will draw. */
export const DEBUG_MAX_MARKERS = 60;

/**
 * How close, as a fraction of the local spacing, a regular station may sit to an inserted
 * crossing or to a run's exact end point before it is dropped. Half keeps every gap between
 * half and one and a half spacings.
 *
 * The fitter is why. approximateSpline sizes its end derivative from the end segment of the
 * point list unless told otherwise, and even with chord-length parameters a segment a few
 * microns long carries a direction that is pure noise: the wall's trimmed corners put the
 * exact crossing 6 to 20 um from the station beside it, and the fit's end tangent disagreed
 * with that chord by 45 to 135 degrees, hooked at 10^5 /m to reconcile the two, and ran out
 * of control points doing it.
 */
export const CROSSING_CLEARANCE = 0.5;

/**
 * How far, as a multiple of the local spacing, an exact end point (corner crossing, miter,
 * terminal) clears the stations beside it. A full spacing: the exact point stands in for
 * the station it displaces, so the chord from it to the next station is a normal one and
 * carries a real direction. Half a spacing left 0.11 mm chords to corner points on a
 * 0.195 mm grid, and those still read 5 degrees off the tangent.
 */
export const END_CLEARANCE = 1.0;

/**
 * Turn below which a corner is treated as straight: the two ends are joined at their
 * midpoint instead of at a miter that lies gap / (2 sin(turn/2)) away.
 */
export const MITER_MIN_TURN = 1e-3;

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

/**
 * Newton steps used to walk a straight-line plane crossing onto the osculating arc.
 *
 * The seed is already within a fraction of a percent for any extension short enough to be
 * worth making, and Newton squares the error each step, so this is generous.
 */
export const TERMINAL_NEWTON_STEPS = 4;

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

/**
 * Where the output is split into separate edges.
 *
 * SOURCE_EDGES mirrors the input: one output edge per source edge, joint for joint, whether
 * or not the offset actually breaks there. DISCONTINUITIES splits only where the output is
 * genuinely not smooth -- a corner the weld declined (a real G0 vertex in the source) or a
 * step or kink in the offset profile -- and runs straight through a tangent-continuous
 * junction, so a seed that arrived as thirty tangent pieces comes out as the handful of
 * edges its corners and profile breaks actually define.
 */
export enum RunBreakMode
{
    annotation { "Name" : "Every source edge" }
    SOURCE_EDGES,
    annotation { "Name" : "Corners and offset breaks" }
    DISCONTINUITIES
}

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

export const OffsetHeightBounds = { (millimeter) : [-50, 0, 50] } as LengthBoundSpec;

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
    const velocity = frameVelocity(frame);
    const direction = velocity
        + slopes.width * frame.widthAxis
        + slopes.height * frame.heightAxis
        + offsets.width * rates.width
        + offsets.height * rates.height;

    return (norm(direction) < ZERO_DIRECTION) ? velocity : normalize(direction);
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

    const velocity = frameVelocity(frame);
    const denominator = surf.scale - surf.curvature * surf.height;
    const sourceRate = (abs(denominator) < 1e-9) ? 0 : dot(velocity, surf.tangent) / denominator;

    // d(scale)/du is dropped. It is -delta * d(kappa)/du: second order in delta, and
    // identically zero whenever the reference is not being measured at an offset.
    const arcRate = sourceRate
        + (slopes.width * placed.alpha + offsets.width * dot(rates.width, surf.tangent)) / surf.scale;
    const vRate = dot(velocity, alongRef.planeNormal)
        + slopes.width * placed.beta
        + offsets.width * dot(rates.width, alongRef.planeNormal);
    const heightRate = dot(velocity, surf.normal) + placed.heightSign * slopes.height;

    const direction = arcRate * (target.scale - target.curvature * placed.height) * target.tangent
        + vRate * alongRef.planeNormal
        + heightRate * target.normal;

    return {
        "direction" : (norm(direction) < 1e-9) ? velocity : normalize(direction),
        "point" : placed.point,
        "surfaceShrink" : target.scale - target.curvature * placed.height
    };
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
 * normal for a point lying on the surface. The walk is Newton on
 *
 *     g(u) = dot(P - A(u), t(u)) = 0,   g'(u) = -(scale - kappa * height)
 *
 * (dA/du = scale * t, dt/du = kappa * N, height = dot(P - A, N)). Until 2026-09-24 the
 * slope was taken as scale alone, dropping kappa * height, and the walk stopped after two
 * steps whatever the residual: fine on the surface, but a point a few millimetres off a
 * curved reference kept a tangential residual that referenceSurfacePoint then silently
 * dropped. It now iterates to REFERENCE_FOOT_TOL. Where scale - kappa * height is not
 * positive the point is past the reference's centre of curvature and its foot is not
 * unique; the step then falls back to scale, which still walks towards a foot.
 */
export function referenceSurfaceCoords(alongRef is map, point is Vector) returns map
{
    var arc = referenceArcAtX(alongRef, point[0]);
    var basis = referenceBasisAtArc(alongRef, arc);
    var toPoint = point - referencePointAtArc(alongRef, arc);

    for (var step = 0; step < REFERENCE_FOOT_STEPS; step += 1)
    {
        const residual = dot(toPoint, basis.tangent);
        if (abs(residual) < REFERENCE_FOOT_TOL)
        {
            break;
        }

        var slope = basis.scale - basis.curvature * dot(toPoint, basis.normal);
        if (slope < 1e-3)
        {
            slope = basis.scale;
        }
        if (abs(slope) < 1e-9)
        {
            break;
        }

        arc = arc + residual / slope;
        basis = referenceBasisAtArc(alongRef, arc);
        toPoint = point - referencePointAtArc(alongRef, arc);
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

// ============================================================================
// Unwrapping through the chart
// ============================================================================

/**
 * The chart an unwrap maps through: the reference surface of `selection` offset by `delta`,
 * and where the alignment point sits on it.
 *
 * Length is preserved along the reference offset by delta (alongCoordinate): delta 0 keeps
 * lengths along the reference itself, delta = half a plate's thickness keeps them along the
 * plate's mid-surface, which is the surface a bent plate does not stretch.
 *
 * @param alignPoint {Vector} : the wrapped point that lands on the unwrapped origin. Its X also
 *      seeds buildAlongReference's zero, so it must lie within the reference's X span.
 * @returns {map} : { "alongRef", "align" (its surface coords), "alignCoord" }
 */
export function unwrapChart(context is Context, selection is Query, alignPoint is Vector, delta is ValueWithUnits) returns map
{
    const alongRef = buildAlongReference(context, selection, alignPoint, delta);
    const problem = unwrapReferenceProblem(alongRef);
    if (problem != undefined)
    {
        throw regenError(problem, ["reference"], selection);
    }
    return chartFromReference(alongRef, alignPoint);
}

export function chartFromReference(alongRef is map, alignPoint is Vector) returns map
{
    const packed = packChart(alongRef);
    const seed = referenceArcAtX(alongRef, alignPoint[0]).value;
    const f = chartFoot(packed, alignPoint[0].value, alignPoint[1].value, alignPoint[2].value, seed, 0);
    return {
        "alongRef" : alongRef,
        "packed" : packed,
        "align" : { "arc" : f[0] * meter, "v" : f[2] * meter, "height" : f[3] * meter },
        "alignCoord" : (f[0] - packed.delta * f[4]) * meter,
        "alignX" : f[0] - packed.delta * f[4],
        "alignV" : f[2],
        "alignHeight" : f[3]
    };
}

/**
 * Chart coordinates of a wrapped point in plain metres, plus its foot for the next call.
 * @param previous : undefined (seed from X), or the previous result along the same edge (warm start).
 * @returns {array} : [x, y, z, arc, span, tx, ty, tz, kappa, scale, height]
 */
export function unwrapFast(chart is map, point is Vector, previous) returns array
{
    const c = chart.packed;
    const a0 = (previous == undefined) ? chartSeedArc(c, point[0].value) : previous[3];
    const i0 = (previous == undefined) ? chartSpanOf(c, a0) : previous[4];
    const f = chartFoot(c, point[0].value, point[1].value, point[2].value, a0, i0);
    return [f[0] - c.delta * f[4] - chart.alignX, chart.alignV - f[2], f[3] - chart.alignHeight,
            f[0], f[1], f[5], f[6], f[7], f[8], f[9], f[3], f[10]];
}

/** A foot whose tangential residual is above this (metres) was not found. */
export const CHART_FOOT_FAIL = 1e-7;

/**
 * Whether an unwrapFast result (residual at index 11) or a chartFoot result (index 10) found its foot.
 */
export function chartFootConverged(result is array) returns boolean
{
    return abs(result[size(result) - 1]) <= CHART_FOOT_FAIL;
}

/**
 * Arc (plain metres) whose sample X is nearest qx: the cold seed for chartFoot, by binary search on the
 * packed X samples (no units). The tables are keyed by arc; px rises with it wherever the chart is usable.
 */
export function chartSeedArc(c is map, qx is number) returns number
{
    const last = c.count - 1;
    if (qx <= c.px[0])
    {
        return c.arcs[0] + (qx - c.px[0]);
    }
    if (qx >= c.px[last])
    {
        return c.arcs[last] + (qx - c.px[last]);
    }
    var low = 0;
    var high = last - 1;
    while (low < high)
    {
        const half = floor((low + high) / 2);
        if (qx <= c.px[half + 1])
        {
            high = half;
        }
        else
        {
            low = half + 1;
        }
    }
    const span = c.px[low + 1] - c.px[low];
    if (span < 1e-15)
    {
        return c.arcs[low];
    }
    return c.arcs[low] + (c.arcs[low + 1] - c.arcs[low]) * (qx - c.px[low]) / span;
}

/**
 * The span holding arc a, by binary search (chartSpan walks from a hint instead).
 */
export function chartSpanOf(c is map, a is number) returns number
{
    var low = 0;
    var high = c.count - 2;
    while (low < high)
    {
        const half = floor((low + high) / 2);
        if (a <= c.arcs[half + 1])
        {
            high = half;
        }
        else
        {
            low = half + 1;
        }
    }
    return low;
}

/**
 * Inverse of unwrapFast: the wrapped point (plain metres [x, y, z]) whose flat chart coordinates are
 * (fx, fy, fz). Newton on x(a) = a - delta * theta(a) - alignX (slope = scale), then
 * P = A(a) + v * planeNormal + h * N(a) with v = alignV - fy, h = fz + alignHeight, N = planeNormal x t.
 * Same packed tables as the forward map, so the round trip is exact to the Newton tolerance.
 */
export function unwrapInverse(chart is map, fx is number, fy is number, fz is number) returns array
{
    const c = chart.packed;
    var a = fx + chart.alignX;
    var i = chartSpanOf(c, a);
    var e = chartEval(c, a, i);
    for (var step = 0; step < REFERENCE_FOOT_STEPS; step += 1)
    {
        const g = a - c.delta * e[6] - chart.alignX - fx;
        if (abs(g) < CHART_FOOT_TOL)
        {
            break;
        }
        a = a - g / ((abs(e[8]) > 1e-9) ? e[8] : 1);
        i = chartSpan(c, a, i);
        e = chartEval(c, a, i);
    }
    const v = chart.alignV - fy;
    const h = fz + chart.alignHeight;
    const nx = c.ny * e[5] - c.nz * e[4];
    const ny = c.nz * e[3] - c.nx * e[5];
    const nz = c.nx * e[4] - c.ny * e[3];
    return [e[0] + v * c.nx + h * nx, e[1] + v * c.ny + h * ny, e[2] + v * c.nz + h * nz];
}

/**
 * Why a reference cannot be unwrapped along, or undefined when it can: it must be ONE planar chain whose
 * joints are tangent within UNWRAP_REFERENCE_JOINT (a corner's turn never enters theta, so lengths past it
 * would be wrong, and points in its wedge have two feet or none). 1 deg, not less: real references carry
 * small modelling creases (Wrapped_profile: 0.403 deg).
 */
export function unwrapReferenceProblem(alongRef is map)
{
    if (size(alongRef.chain.links) != 1)
    {
        return "The reference is not one connected chain (" ~ size(alongRef.chain.links) ~ " pieces).";
    }
    for (var i = 0; i + 1 < size(alongRef.arcs); i += 1)
    {
        if (abs((alongRef.arcs[i + 1] - alongRef.arcs[i]) / meter) < 1e-12)
        {
            const turn = angleBetween(alongRef.tangents[i], alongRef.tangents[i + 1]);
            if (turn > UNWRAP_REFERENCE_JOINT)
            {
                return "The reference has a corner of " ~ roundToPrecision(turn / degree, 2) ~ " deg at arc "
                    ~ roundToPrecision(alongRef.arcs[i] / millimeter, 1) ~ " mm; it must be a tangent chain.";
            }
        }
    }
    return undefined;
}

/** Largest joint angle a reference may carry (see unwrapReferenceProblem). */
export const UNWRAP_REFERENCE_JOINT = 1 * degree;

/**
 * Unwrapped direction of a wrapped unit direction d at a point whose unwrapFast result is u.
 * x = arc - delta * theta, d(arc) = dot(d, t) / (scale - kappa * height), d(x) = scale * d(arc).
 * Exact; replaces the two extra map calls and the 10 um finite difference per edge.
 */
export function unwrapDirection(chart is map, cs is CoordSystem, u is array, d is Vector) returns Vector
{
    const c = chart.packed;
    const nx = c.ny * u[7] - c.nz * u[6];
    const ny = c.nz * u[5] - c.nx * u[7];
    const nz = c.nx * u[6] - c.ny * u[5];
    const along = u[9] * (d[0] * u[5] + d[1] * u[6] + d[2] * u[7]) / (u[9] - u[8] * u[10]);
    const across = -(d[0] * c.nx + d[1] * c.ny + d[2] * c.nz);
    const up = d[0] * nx + d[1] * ny + d[2] * nz;
    return normalize(along * cs.xAxis + across * cross(cs.zAxis, cs.xAxis) + up * cs.zAxis);
}

/**
 * Chart coordinates of a wrapped point relative to the alignment point: x along the preserved
 * length, y across the reference plane, z off the offset reference.
 *
 * Right-handed with (tangent, -planeNormal, normal): the surface normal is planeNormal x
 * tangent, so (tangent, planeNormal, normal) would be LEFT-handed and every unwrapped solid a
 * mirror image. y is therefore measured along -planeNormal.
 */
export function unwrapCoords(chart is map, point is Vector) returns Vector
{
    const u = unwrapFast(chart, point, undefined);
    return vector(u[0], u[1], u[2]) * meter;
}

/**
 * A wrapped point, unwrapped into the frame `cs`: the alignment point lands on cs's origin, the
 * reference tangent there on cs's X, the reference surface normal on cs's Z.
 *
 * @param zShift {ValueWithUnits} : added to the height, e.g. to lay a plate's bottom on the plane.
 */
export function unwrapPoint(chart is map, cs is CoordSystem, zShift is ValueWithUnits, point is Vector) returns Vector
{
    const c = unwrapCoords(chart, point);
    return toWorld(cs, vector(c[0], c[1], c[2] + zShift));
}

// ----------------------------------------------------------------------------
// Packed chart: the same map in plain numbers (metres, radians, 1/m). Unit arithmetic is
// operator overloading in FeatureScript and costs ~40x plain arithmetic; the per-point map ran
// 46x faster this way (research_unwrap_perf.md 2.3). The tangent is the derivative of the SAME
// Hermite cubic as the position and theta is Hermite with curvature as its slope, so the foot is
// the foot of one consistent curve (0.44 um worst case before, sub-micron after).
// ----------------------------------------------------------------------------

/** Newton on the foot stops below this residual, metres (REFERENCE_FOOT_TOL as a number). */
const CHART_FOOT_TOL = 1e-10;

/**
 * The chart's tables as plain numbers (metres, radians, 1/m): unit arithmetic is FS-level operator
 * overloading and costs ~40x plain arithmetic in the inner loop. Rates are d(offset point)/d(wire arc).
 */
export function packChart(alongRef is map) returns map
{
    const count = size(alongRef.arcs);
    var arcs = makeArray(count);
    var px = makeArray(count);
    var py = makeArray(count);
    var pz = makeArray(count);
    var rx = makeArray(count);
    var ry = makeArray(count);
    var rz = makeArray(count);
    var kappa = makeArray(count);
    var theta = makeArray(count);
    for (var i = 0; i < count; i += 1)
    {
        const scale = 1 - alongRef.delta * alongRef.curvatures[i];
        arcs[i] = alongRef.arcs[i].value;
        px[i] = alongRef.points[i][0].value;
        py[i] = alongRef.points[i][1].value;
        pz[i] = alongRef.points[i][2].value;
        rx[i] = scale * alongRef.tangents[i][0];
        ry[i] = scale * alongRef.tangents[i][1];
        rz[i] = scale * alongRef.tangents[i][2];
        kappa[i] = alongRef.curvatures[i].value;
        theta[i] = alongRef.thetas[i];
    }
    return { "count" : count, "arcs" : arcs, "px" : px, "py" : py, "pz" : pz, "rx" : rx, "ry" : ry, "rz" : rz,
            "kappa" : kappa, "theta" : theta, "delta" : alongRef.delta.value,
            "nx" : alongRef.planeNormal[0], "ny" : alongRef.planeNormal[1], "nz" : alongRef.planeNormal[2] };
}

/** The span holding arc a, walked from the previous span. Never lands on a zero-length join span. */
export function chartSpan(c is map, a is number, hint is number) returns number
{
    var i = min(max(hint, 0), c.count - 2);
    while (i < c.count - 2 && a > c.arcs[i + 1])
    {
        i += 1;
    }
    while (i > 0 && a <= c.arcs[i])
    {
        i -= 1;
    }
    return i;
}

/**
 * Chart at arc a in span i: [ax, ay, az, tx, ty, tz, theta, kappa, scale]. The tangent is the
 * derivative of the same Hermite cubic as the position, so the foot is the foot of THIS curve.
 * Past either end the reference runs on straight (position linear, theta and tangent held).
 */
export function chartEval(c is map, a is number, i is number) returns array
{
    const last = c.count - 1;
    if (a <= c.arcs[0] || a >= c.arcs[last])
    {
        const j = (a <= c.arcs[0]) ? 0 : last;
        const d = a - c.arcs[j];
        const s = sqrt(c.rx[j] * c.rx[j] + c.ry[j] * c.ry[j] + c.rz[j] * c.rz[j]);
        return [c.px[j] + d * c.rx[j], c.py[j] + d * c.ry[j], c.pz[j] + d * c.rz[j],
                c.rx[j] / s, c.ry[j] / s, c.rz[j] / s, c.theta[j], 0, s];
    }
    const k = i + 1;
    const h = c.arcs[k] - c.arcs[i];
    const f = (a - c.arcs[i]) / h;
    const f2 = f * f;
    const f3 = f2 * f;
    const h00 = 2 * f3 - 3 * f2 + 1;
    const h10 = (f3 - 2 * f2 + f) * h;
    const h01 = 3 * f2 - 2 * f3;
    const h11 = (f3 - f2) * h;
    const g0 = (6 * f2 - 6 * f) / h;
    const g10 = 3 * f2 - 4 * f + 1;
    const g11 = 3 * f2 - 2 * f;
    const dx = g0 * (c.px[i] - c.px[k]) + g10 * c.rx[i] + g11 * c.rx[k];
    const dy = g0 * (c.py[i] - c.py[k]) + g10 * c.ry[i] + g11 * c.ry[k];
    const dz = g0 * (c.pz[i] - c.pz[k]) + g10 * c.rz[i] + g11 * c.rz[k];
    const s = sqrt(dx * dx + dy * dy + dz * dz);
    return [h00 * c.px[i] + h10 * c.rx[i] + h01 * c.px[k] + h11 * c.rx[k],
            h00 * c.py[i] + h10 * c.ry[i] + h01 * c.py[k] + h11 * c.ry[k],
            h00 * c.pz[i] + h10 * c.rz[i] + h01 * c.pz[k] + h11 * c.rz[k],
            dx / s, dy / s, dz / s,
            h00 * c.theta[i] + h10 * c.kappa[i] + h01 * c.theta[k] + h11 * c.kappa[k],
            c.kappa[i] + (c.kappa[k] - c.kappa[i]) * f,
            s];
}

/**
 * Foot of point (qx, qy, qz) on the packed chart, Newton on dot(Q - A(a), t(a)) = 0 with slope
 * scale - kappa * height (referenceSurfaceCoords' walk, plain numbers).
 * @returns {array} : [arc, span, v, height, theta, tx, ty, tz, kappa, scale, residual]. A residual above
 *      CHART_FOOT_FAIL means the walk did not find a foot: the point lies past the reference's centre of
 *      curvature, or was seeded from a far-away one (chartFootConverged).
 */
export function chartFoot(c is map, qx is number, qy is number, qz is number, a0 is number, hint is number) returns array
{
    var a = a0;
    var i = hint;
    var result = undefined;
    for (var step = 0; step <= REFERENCE_FOOT_STEPS; step += 1)
    {
        i = chartSpan(c, a, i);
        const e = chartEval(c, a, i);
        const dx = qx - e[0];
        const dy = qy - e[1];
        const dz = qz - e[2];
        const nx = c.ny * e[5] - c.nz * e[4];
        const ny = c.nz * e[3] - c.nx * e[5];
        const nz = c.nx * e[4] - c.ny * e[3];
        const residual = dx * e[3] + dy * e[4] + dz * e[5];
        const height = dx * nx + dy * ny + dz * nz;
        if (abs(residual) < CHART_FOOT_TOL || step == REFERENCE_FOOT_STEPS)
        {
            result = [a, i, dx * c.nx + dy * c.ny + dz * c.nz, height, e[6], e[3], e[4], e[5], e[7], e[8], residual];
            break;
        }
        var slope = e[8] - e[7] * height;
        if (slope < 1e-3)
        {
            slope = e[8];
        }
        a = a + residual / slope;
    }
    return result;
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

/** Below this |z| of the height direction the "height up" rule is ambiguous (see referencePlaneNormal). */
export const REFERENCE_HANDEDNESS_Z = 0.1;

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

    // A straight reference bends in no plane, so the plane is chosen the way the curved
    // case is signed anyway: height as close to world +Z as the line allows. planeNormal
    // = tangent x Z makes the height axis (planeNormal x tangent) the part of Z normal
    // to the line, and width the horizontal perpendicular -- a planar offset. A vertical
    // line has no such plane and takes Y for its normal instead.
    if (bestCurvature < ZERO_CURVATURE)
    {
        const tangent = samples[0].tangent;
        var planeNormal = cross(tangent, vector(0, 0, 1));
        if (norm(planeNormal) < 1e-6)
        {
            planeNormal = cross(tangent, vector(0, 1, 0));
        }
        println("NOTE: the reference wire is straight; its offset plane is taken with height along world Z"
            ~ " (plane normal " ~ fmtVec(normalize(planeNormal), 3, 7) ~ ").");
        return normalize(planeNormal);
    }

    var planeNormal = normalize(cross(best.tangent, best.towardCentre));
    if (abs(cross(planeNormal, best.tangent)[2]) > REFERENCE_HANDEDNESS_Z)
    {
        // The usual case (a profile in a vertical plane): height up.
        if (cross(planeNormal, best.tangent)[2] < 0)
        {
            planeNormal = -1 * planeNormal;
        }
    }
    else
    {
        // A reference in (or near) a horizontal plane: "height up" is undefined and the old rule followed the
        // way the most curved sample bent -- an S-curve edit could flip height, width and every unwrapped
        // solid. Orient the plane normal itself instead: its largest world component positive.
        var k = 0;
        for (var j = 1; j < 3; j += 1)
        {
            if (abs(planeNormal[j]) > abs(planeNormal[k]))
            {
                k = j;
            }
        }
        if (planeNormal[k] < 0)
        {
            planeNormal = -1 * planeNormal;
        }
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
 * Points with no station of their own, as run point entries. Nothing for no points.
 */
function exactEntries(lead) returns array
{
    var entries = [];
    if (lead is array)
    {
        for (var point in lead)
        {
            entries = append(entries, { "point" : point, "index" : undefined });
        }
    }
    return entries;
}

/**
 * The exact tangent for a run end, or nothing where prescribing one would do harm.
 *
 * A terminal extension folded into the run ends on its plane with the arrival direction
 * extendToPlane chose, and that is the tangent, whatever the station beneath it says.
 *
 * A corner treatment gives the run an exact end point where the two offset runs cross or
 * where their tangent lines meet. Two curves in space do not generally do either: where one
 * source edge climbs out of the reference surface and its neighbour does not, their offsets
 * miss each other by the climb -- 0.6 mm at a notch corner on a 7.7 mm offset -- and the
 * shared corner point lies off both curves by half of that. Prescribing the analytic
 * tangent at a point the curve does not pass through forces the fit to bend through the
 * last chord to reconcile the two: 5.8 degrees over 0.135 mm, 771 /m. The point is a G0
 * corner; nothing joins it tangentially. So where the recorded miss exceeds the fit
 * tolerance the end is left free, and the fit runs smoothly into the shared point.
 */
export function runEndTangent(stations is array, coords is map, offsets is array, definition is map, alongRef,
    run is map, index is number, displacement is map, atStart is boolean)
{
    const arrival = atStart
        ? ((index == run.start) ? run.startArrival : undefined)
        : ((index == run.end) ? run.endArrival : undefined);

    if (arrival != undefined)
    {
        return arrival;
    }

    // Only the run's own end carries a miss; a range clipped short of it has none.
    const miss = atStart
        ? ((index == run.start) ? run.startMiss : undefined)
        : ((index == run.end) ? run.endMiss : undefined);

    if (miss != undefined && miss > definition.approximationTolerance)
    {
        return undefined;
    }

    return runTangent(stations, coords, offsets, definition, alongRef, run, index, displacement);
}

/**
 * How the base point moves along the source, per unit arc length.
 *
 * The frame's `tangent` is its LENGTH AXIS -- the direction width and height are measured
 * against -- and two alignments deliberately turn it away from the source: the constrained
 * reference frame projects it into the reference surface, the world frame sets it to
 * world X. The point still travels along the source. The derivative of the offset starts
 * from the source tangent whatever the axes do; taking the length axis instead dropped the
 * component of travel normal to the reference, which on a seed climbing a ramp at 5 degrees
 * was a 4.8 degree error in every run-end tangent there -- both profiles alike, so it could
 * not have been a width term. Frames built before the alignment is stamped carry no
 * velocity and move along their tangent, which for them is the source tangent.
 */
export function frameVelocity(frame is map) returns Vector
{
    return (frame.velocity == undefined) ? frame.tangent : frame.velocity;
}

/**
 * Closest approach between two LINES, unclamped: the point on `a0 + s * u` nearest to
 * `b0 + t * v`, their parameters and the midpoint. Parallel lines return the midpoint of
 * the two origins. This is the miter construction; segmentApproach's clamp is right for
 * finding where two runs cross and wrong for extending two rays to where they would meet.
 */
export function lineApproach(a0 is Vector, u is Vector, b0 is Vector, v is Vector) returns map
{
    const w0 = a0 - b0;
    const a = dot(u, u);
    const b = dot(u, v);
    const c = dot(v, v);
    const d = dot(u, w0);
    const e = dot(v, w0);
    const denominator = a * c - b * b;

    if (abs(denominator) < ZERO_SPAN)
    {
        return { "distance" : norm(w0), "point" : 0.5 * (a0 + b0), "s" : 0, "t" : 0 };
    }

    const s = (b * e - c * d) / denominator;
    const t = (a * e - b * d) / denominator;
    const pa = a0 + s * u;
    const pb = b0 + t * v;

    return { "distance" : norm(pa - pb), "point" : 0.5 * (pa + pb), "s" : s, "t" : t };
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
    return runTangent(stations, coords, offsets, definition, alongRef, run, index, ZERO_DISPLACEMENT);
}

/**
 * The same tangent, for an offset displaced by a constant amount.
 *
 * A ruled section is the offset stepped sideways by a fixed reach, which is the same
 * offset with a constant added to one of its amounts. The profile's slopes are
 * untouched by that -- the derivative of a constant is zero -- so the displaced
 * tangent is the undisplaced one evaluated at the larger amount, and it comes out of
 * whichever map placed the points:
 *
 *   reference   d/ds surfaceOffset(w + reach, h)  = surfaceOffsetTangent(w + reach, h)
 *   straight    d/ds (P + reach W)                = P' + reach W'
 *
 * and the second is exactly what offsetTangent returns once reach is folded into the
 * width amount, since W enters it multiplied by the amount. So both are exact, not
 * approximations of the displaced curve.
 *
 * Without this a displaced section had to be fitted with no end constraints at all
 * while the section at zero reach got exact ones. That is wrong twice over: the two
 * are then different curves through corresponding points, so opLoft pairs sections
 * carrying different parameterizations; and an unconstrained fit is free to drop
 * control points, which on a short nearly-straight run takes the count below
 * degree + 1 and yields a B-spline the kernel rejects as BAD_GEOMETRY.
 *
 * @param displacement {map} : { "width" : ValueWithUnits, "height" : ValueWithUnits }
 *      added to the amounts before the tangent is taken.
 */
export function runTangent(stations is array, coords is map, offsets is array, definition is map, alongRef,
    run is map, index is number, displacement is map)
{
    if (offsets[index] == undefined)
    {
        return undefined;
    }

    const frame = stations[index];
    const amounts = {
            "width" : offsets[index].width + displacement.width,
            "height" : offsets[index].height + displacement.height
        };
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
 * Which run boundaries can be dissolved because the runs either side, taken together, still
 * describe one line or one arc -- in every profile at once.
 *
 * The one-run-list form: every profile is read over the same `runs`. For profiles whose
 * runs differ in extent, see alignedRunMerges.
 *
 * @returns {array} : one boolean per boundary, `size(runs) - 1` of them.
 */
export function tangentRunMerges(pointsPerProfile is array, runs is array, stations is array,
    tolerance is ValueWithUnits) returns array
{
    var runsPerProfile = [];
    for (var k = 0; k < size(pointsPerProfile); k += 1)
    {
        runsPerProfile = append(runsPerProfile, runs);
    }

    return alignedRunMerges(pointsPerProfile, runsPerProfile, stations, tolerance);
}

/**
 * tangentRunMerges over runs that have been aligned across profiles.
 *
 * `runsPerProfile[k]` is profile k's runs laid out over the shared cells, with `undefined`
 * in every cell the profile does not reach. A boundary dissolves only where each profile
 * either has a run on both sides that pass the test, or has no run on either side. A profile
 * with a run on one side only is a coverage edge, and a boundary that is a coverage edge in
 * one profile stays a boundary in all of them -- otherwise the merged cell would be covered
 * by part of a run in that profile, and the alignment would be lost.
 *
 * @param runsPerProfile {array} : per profile, an array over the shared cells of run or
 *      undefined; all the same length.
 * @returns {array} : one boolean per cell boundary.
 */
export function alignedRunMerges(pointsPerProfile is array, runsPerProfile is array,
    stations is array, tolerance is ValueWithUnits) returns array
{
    const cells = (size(runsPerProfile) == 0) ? 0 : size(runsPerProfile[0]);

    if (cells < 2 || size(pointsPerProfile) == 0)
    {
        return makeArray(max(cells - 1, 0), false);
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
    //
    // The group is kept per profile -- span, the shape it started as, the station it started
    // at -- because each profile's runs are its own: a run trimmed at a corner in one profile
    // spans fewer stations than the same cell in another.
    var spans = [];
    var bases = [];
    var anchorStart = [];

    for (var k = 0; k < size(pointsPerProfile); k += 1)
    {
        const group = startGroup(pointsPerProfile[k], runsPerProfile[k][0], stations, tolerance);
        spans = append(spans, group.span);
        bases = append(bases, group.base);
        anchorStart = append(anchorStart, group.anchorStart);
    }

    for (var r = 0; r + 1 < cells; r += 1)
    {
        var ok = true;
        var grown = [];

        for (var k = 0; k < size(pointsPerProfile); k += 1)
        {
            const here = runsPerProfile[k][r];
            const next = runsPerProfile[k][r + 1];

            // Absent on both sides: this profile has no say at this boundary.
            if (here == undefined && next == undefined)
            {
                grown = append(grown, undefined);
                continue;
            }

            // Present on one side only: a coverage edge, which no profile may merge across.
            if (here == undefined || next == undefined)
            {
                ok = false;
                break;
            }

            // The end-side records come from the run before the boundary, NOT from the run
            // the group started at. Reading them off the anchor missed a trimmed end or a
            // terminal on any run absorbed after the first, because the anchor's own records
            // had already been checked and found clear. Everything that made a boundary
            // meaningful has to survive it: a trimmed corner carries an exact crossing point,
            // a terminal carries a plane it was cut to, and a corner fill is a separate piece
            // of geometry that belongs between.
            if (!(next.start == here.end + 1
                    && here.endPoint == undefined && next.startPoint == undefined
                    && here.terminalEnd == undefined && next.terminalStart == undefined
                    && next.fill == undefined && next.cornerKind == undefined))
            {
                ok = false;
                break;
            }

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
            const gates = sourceShapeGates(stations, anchorStart[k], next.end);
            const shape = classifyPoints(span, tolerance, gates.allowArc, gates.allowLine);

            if (shape.kind != "line" && shape.kind != "arc")
            {
                ok = false;
                break;
            }

            // Same KIND of thing, and for an arc the same circle -- not merely a circle
            // that the points happen to sit on. Absorbing a neighbour must not change the
            // radius, or the merge is inventing geometry rather than recognising it.
            const base = bases[k];

            if (base == undefined || base.kind != shape.kind)
            {
                ok = false;
                break;
            }

            if (shape.kind == "arc"
                && abs(shape.radius - base.radius) > ARC_MERGE_RADIUS_REL * base.radius)
            {
                ok = false;
                break;
            }

            grown = append(grown, span);
        }

        merges = append(merges, ok);

        if (ok)
        {
            spans = grown;
        }
        else
        {
            spans = [];
            bases = [];
            anchorStart = [];

            for (var k = 0; k < size(pointsPerProfile); k += 1)
            {
                const group = startGroup(pointsPerProfile[k], runsPerProfile[k][r + 1], stations, tolerance);
                spans = append(spans, group.span);
                bases = append(bases, group.base);
                anchorStart = append(anchorStart, group.anchorStart);
            }
        }
    }

    return merges;
}

/**
 * A merge group opened on one run of one profile, or on nothing where the profile has no
 * run in that cell.
 */
function startGroup(points is array, run, stations is array, tolerance is ValueWithUnits) returns map
{
    if (run == undefined)
    {
        return { "span" : undefined, "base" : undefined, "anchorStart" : undefined };
    }

    const span = extendSpan([], points, run.start, run.end);
    const gates = sourceShapeGates(stations, run.start, run.end);

    return {
            "span" : span,
            "base" : span == undefined ? undefined
                : classifyPoints(span, tolerance, gates.allowArc, gates.allowLine),
            "anchorStart" : run.start
        };
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
 *
 * Works on an aligned layout as well as a plain run list: a cell the profile does not reach
 * is `undefined`, and alignedRunMerges never marks a boundary between a run and a hole, so
 * a merge only ever joins two runs or two holes.
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
            if (current != undefined && next != undefined)
            {
                current = mergeMaps(current, {
                            "end" : next.end,
                            "endPoint" : next.endPoint,
                            "terminalEnd" : next.terminalEnd
                        });
            }
        }
        else
        {
            out = append(out, current);
            current = runs[r + 1];
        }
    }

    return append(out, current);
}

/**
 * The points a run is emitted through, each with the station it came from.
 *
 * The stations from `from` to `to`, with the exact startPoint / endPoint that a corner trim,
 * a miter or a terminal plane put on the run wherever the range reaches the run's own end.
 * That exact point is what makes both sides of a trimmed corner share an endpoint, so
 * anything that rebuilds a run from its stations -- a section for a loft as much as the
 * offset wire itself -- has to include it, or the two rebuilds stop short of each other by
 * up to one station spacing.
 *
 * Stations within CROSSING_CLEARANCE of the local spacing of an exact end are dropped, the
 * same rule insertCrossings applies beside a crossing and for the same reason: a trim lands
 * its crossing wherever two segments happen to pass, which can be microns from the next
 * station, and the fitter reads that micron chord as the direction the curve leaves in.
 * Repeats closer than PARAMETER_SEPARATION of the chord go too, so a welded junction inside
 * the run contributes its vertex once and the fit's parameters stay strictly increasing.
 *
 * A terminal extension adds its lead -- the samples between the last station and the
 * landing on the plane -- between the stations and the exact end, so the run is one point
 * list from its first station to the plane.
 *
 * @returns {array} : `{ "point", "index" }` per entry, `index` undefined for an exact end
 *      or a lead sample.
 */
export function runPointEntries(points is array, run is map, from is number, to is number) returns array
{
    var stations = [];
    for (var i = from; i <= to; i += 1)
    {
        if (points[i] != undefined)
        {
            stations = append(stations, { "point" : points[i], "index" : i });
        }
    }

    const leads = (from == run.start && run.startPoint != undefined);
    const trails = (to == run.end && run.endPoint != undefined);

    if (leads)
    {
        stations = clearedOfExact(stations, run.startPoint, true);
        stations = concatenateArrays([[{ "point" : run.startPoint, "index" : undefined }],
                    exactEntries(run.startLead), stations]);
    }

    if (trails)
    {
        stations = clearedOfExact(stations, run.endPoint, false);
        stations = concatenateArrays([stations, exactEntries(run.endLead),
                    [{ "point" : run.endPoint, "index" : undefined }]]);
    }

    // Repeats, relative to the run's own length.
    var chord = 0 * meter;
    for (var k = 1; k < size(stations); k += 1)
    {
        chord += norm(stations[k].point - stations[k - 1].point);
    }
    const tolerance = max(TOLERANCE.zeroLength * meter, PARAMETER_SEPARATION * chord);

    var kept = [];
    for (var entry in stations)
    {
        if (size(kept) > 0 && norm(entry.point - kept[size(kept) - 1].point) < tolerance)
        {
            // Of two coincident entries the exact end is the one to keep: it is the point
            // the neighbouring run also ends on.
            if (entry.index == undefined)
            {
                kept[size(kept) - 1] = entry;
            }
            continue;
        }

        kept = append(kept, entry);
    }

    return kept;
}

/**
 * The station entries with those crowding an exact end removed.
 *
 * The local spacing is read off the next pair of stations inward, so a dense stretch keeps
 * its stations and a sparse one keeps its clearance. At least two stations always remain.
 */
function clearedOfExact(stations is array, exact is Vector, atStart is boolean) returns array
{
    var list = stations;

    while (size(list) > 2)
    {
        const last = size(list) - 1;
        const nearest = atStart ? list[0].point : list[last].point;
        const next = atStart ? list[1].point : list[last - 1].point;
        const spacing = norm(next - nearest);

        if (norm(nearest - exact) >= END_CLEARANCE * spacing)
        {
            break;
        }

        list = atStart ? subArray(list, 1, size(list)) : subArray(list, 0, last);
    }

    return list;
}

/**
 * The points alone. See runPointEntries.
 */
export function runPointList(points is array, run is map, from is number, to is number) returns array
{
    var list = [];
    for (var entry in runPointEntries(points, run, from, to))
    {
        list = append(list, entry.point);
    }
    return list;
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
 *
 * Shown with its label, directly under the measure and reference controls in both features:
 * it decides what "width" and "height" mean, which is the next thing to settle after what
 * the profile's X measures, and an unlabelled enum further down the dialog was being missed.
 */
export predicate offsetAlignmentPredicate(definition is map)
{
    annotation { "Name" : "Offset alignment", "Default" : OffsetFrameAlignment.ALONG, "UIHint" : UIHint.SHOW_LABEL, "Description" : "How the offset frame is oriented at each point along the offset edges. Along: the chain's own transported frame, width across it and height normal to it, taken from the reference wire where one is given. World: world X, Y, Z." }
    definition.frameAlignment is OffsetFrameAlignment;
}

/**
 * Where the output is split into edges. See RunBreakMode.
 *
 * The plain offset defaults to one edge per source edge, which is what it has always done;
 * the surface feature declares the same parameter itself with the other default, because a
 * face per tangent-continuous source piece is exactly what it does not want.
 */
export predicate offsetBreakPredicate(definition is map)
{
    annotation { "Name" : "Break output at", "Default" : RunBreakMode.SOURCE_EDGES, "UIHint" : UIHint.SHOW_LABEL, "Description" : "Where the output is split into separate edges. Every source edge: one output edge per source edge, joint for joint. Corners and offset breaks: only where the source turns through a real corner or the offset profile steps or kinks; tangent-continuous source edges run together into one output edge." }
    definition.runBreakMode is RunBreakMode;
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
