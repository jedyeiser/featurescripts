FeatureScript 2856;

import(path : "onshape/std/common.fs", version : "2856.0");
import(path : "onshape/std/math.fs", version : "2856.0");
import(path : "onshape/std/vector.fs", version : "2856.0");
export import(path : "onshape/std/nurbsUtils.fs", version : "2856.0");

export const PositionTolBounds = {(millimeter) : [0.00001, 0.0001, 1]} as LengthBoundSpec;
export const PlaneTolBounds = {(millimeter) : [0.00001, 0.00001, 1]} as LengthBoundSpec;
export const MinLengthBounds = {(millimeter) : [1, 10, 100]} as LengthBoundSpec;
export const TanTolBounds = {(degree) : [0.00001, 0.1, 1]} as AngleBoundSpec;


annotation { "Feature Type Name" : "Arc fit", "Feature Type Description" : "Takes edges as input and outputs an arc fit representation of the edges. Lines collapse to lines. Continuity is not preserved if input edges are not within a given range" }
export const arcFit = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Edges to fit", "Filter" : EntityType.EDGE}
        definition.selEdges is Query;
        
        annotation { "Name" : "Position tolerance", "Description": "Distance within which to ensure G0 continuity", "Default": 0.00001 * millimeter }
        isLength(definition.posTol, PositionTolBounds);
        
        annotation { "Name" : "Plane tolerance", "Description": "Allowable out-of-plane error", "Default": 0.01 * millimeter }
        isLength(definition.planeTol, PlaneTolBounds);
        
        annotation { "Name" : "Minimum segment length", "Description" : "Minimum allowable length of an individual arc/line", "Default": 1 * millimeter }
        isLength(definition.minLength, MinLengthBounds);
        
        annotation { "Name" : "Tangency tol", "Description": "Ensure G1 tangency within this angle", "Default": 0.1 * degree }
        isAngle(definition.tanTol, TanTolBounds);
        
        annotation { "Name" : "Group output", "Default": false, "Description": "When true, uses opExtractWires to group the output of this feature" }
        definition.extractWires is boolean;
        
        annotation { "Name" : "Create curves", "Default": true}
        definition.createCurves is boolean;

        
    }
    {
        var evEdges = evaluateQuery(context, qUnion([definition.selEdges]));
        
        var bSplines = mapArray(evEdges, function(e) {return evApproximateBSplineCurve(context, {
                "edge" : e,
                "forceNonRational" : true
        });});
        
        var dotTol = cos(definition.tanTol);
        var polyArcs = approximateSplinesWithPolyArcs(bSplines, definition.posTol, definition.planeTol, dotTol, definition.minLength);
        
        //const nLines = size(filter(polyArcs.segments, function(s){ return any(keys(s), function(x) {return x == "line";}); }));
        //const nArcs  = size(filter(polyArcs.segments, function(s){ return any(keys(s), function(x) {return x == "circle";}); }));
        //println("lines=" ~ nLines ~ " arcs=" ~ nArcs);
        
        //println(keys(polyArcs.orderedSplines[0]));
        var NURBS = primitivesToBSplines(polyArcs.segments);
        //println("nEdges=" ~ size(evEdges));
        //println("nSegments=" ~ size(polyArcs.segments));
        //println("nNURBS=" ~ size(NURBS));
        
        for (var i = 0; i < size(NURBS); i += 1)
        {
            const c = NURBS[i];
        
            if (!canBeBSplineCurve(c))
            {
                println("Skipping invalid BSplineCurve i=" ~ i ~ " keys=" ~ keys(c));
                continue;
            }
            
            //println("  isRational=" ~ c.isRational ~ " degree=" ~ c.degree
            //    ~ " nCtrl=" ~ size(c.controlPoints) ~ " nKnots=" ~ size(c.knots));
            
            if (c.isRational == true)
            {
                println("  weights=" ~ c.weights);
                println("  P0=" ~ c.controlPoints[0]);
                println("  P1=" ~ c.controlPoints[1]);
                println("  P2=" ~ c.controlPoints[2]);
            }
        
        }
        
        if (definition.createCurves)
        {
            var edgeQueries = [];
            var bodyQueries = [];
            for (var i = 0; i < size(NURBS); i += 1)
            {
                println('NURBS[i] -> ' ~ NURBS[i]);
                opCreateBSplineCurve(context, id + ("arcNURBSFit" ~ i), {
                        "bSplineCurve" : NURBS[i]
                });
                edgeQueries = append(edgeQueries, qCreatedBy(id + ("arcNURBSFit" ~ i), EntityType.EDGE));
                bodyQueries = append(bodyQueries, qCreatedBy(id + ("arcNURBSFit" ~ i), EntityType.BODY));
            }
            
            if (size(bodyQueries) > 1 && definition.extractWires)
            {
                opExtractWires(context, id + "extractNURBSWires", {
                        "edges" : qUnion(edgeQueries)
                });
                opDeleteBodies(context, id + "deleteNURBSWires", {
                        "entities" : qUnion(bodyQueries)
                });
            }
        }
        
    });

function isLineSegment(seg is map) returns boolean
{
    return any(keys(seg), function(x) {return x == "line";});

}

function isArcSegment(seg is map) returns boolean
{
    return any(keys(seg), function(x) {return x == "circle";});
}

/**
 * Convert an array of primitive segments (lines/arcs) into an array of BSplineCurve maps.
 *
 * Lines become non-rational degree-1 B-splines.
 * Arcs become rational degree-2 quadratic NURBS. Large arcs are split into <= 90° pieces.
 */
export function primitivesToBSplines(segments is array) returns array
{
    var out = [];
    for (var i = 0; i < size(segments); i += 1)
    {
        const seg = segments[i];
        if (isLineSegment(seg))
        {
            out = append(out, lineSegmentToBSpline(seg));
        }
        else if (isArcSegment(seg))
        {
            //println("ARC seg: p0=" ~ seg.p0 ~ " p1=" ~ seg.p1 ~ " th0=" ~ seg.theta0 ~ " th1=" ~ seg.theta1);
            //println("  circle r=" ~ seg.circle.radius ~ " cs.origin=" ~ seg.circle.coordSystem.origin);
            //println("  cs.xAxis=" ~ seg.circle.coordSystem.xAxis ~ " cs.zAxis=" ~ seg.circle.coordSystem.zAxis);
            const arcs = arcSegmentToQuadraticNurbsPieces(seg);
            for (var j = 0; j < size(arcs); j += 1)
                out = append(out, arcs[j]);
        }
        else
        {
            throw regenError("Edge not of recognized type");
        }
    }
    return out;
}

/**
 * Convert a line segment to a degree-1 non-rational BSplineCurve.
 *
 * Expected fields:
 *   seg.p0 : Vector (length units)
 *   seg.p1 : Vector (length units)
 */
export function lineSegmentToBSpline(seg is map) returns map
{
    const p0 = seg.p0;
    const p1 = seg.p1;

    return {
        "degree" : 1,
        "dimension" : 3,
        "isRational" : false,
        "isPeriodic" : false,
        "controlPoints" : [p0, p1],
        // Knot vector size must be 1 + degree + nCtrlPts = 1 + 1 + 2 = 4
        "knots" : [0, 0, 1, 1]
    };
}

/**
 * Convert an arc segment (Circle + theta bounds) into one or more quadratic rational NURBS pieces.
 *
 * Expected fields:
 *   seg.circle : Circle map { coordSystem, radius }
 *   seg.theta0 : number (radians)
 *   seg.theta1 : number (radians)
 *
 * Returns: array of BSplineCurve maps (each a single-span quadratic)
 */
export function arcSegmentToQuadraticNurbsPieces(seg is map) returns array
{
    const circle = seg.circle;
    var t0 = seg.theta0;
    var t1 = seg.theta1;

    // Normalize sweep direction to take the shorter/expected path:
    // If you have a separate "sweepSign" concept, apply it here instead.
    var d = t1 - t0;

    // Bring into (-2π, 2π) range; keep as user-intended sign
    while (d > 2 * PI) { d -= 2 * PI; }
    while (d < -2 * PI) { d += 2 * PI; }

    // If extremely small sweep, you could collapse to a line-like tiny segment,
    // but we'll still emit a tiny arc piece.
    const maxPiece = PI / 2; // 90 degrees per piece

    const nPieces = max(1, ceil(abs(d) / maxPiece));
    const step = d / nPieces;

    var out = [];
    var a = t0;
    for (var k = 0; k < nPieces; k += 1)
    {
        const b = (k == nPieces - 1) ? t1 : (a + step);
        out = append(out, makeQuadraticArcNurbs(circle, a, b));
        a = b;
    }
    return out;
}

/**
 * Build a single-span quadratic rational NURBS representation of a circular arc from angle a to b.
 *
 * This uses the standard construction:
 *   - endpoints are points on circle at a and b
 *   - middle control point is intersection of endpoint tangents (in the circle plane)
 *   - middle weight w = cos(Δ/2)
 *
 * Circle is defined by:
 *   - circle.coordSystem: circle lies in its local XY plane
 *   - circle.radius
 */
function makeQuadraticArcNurbs(circle is map, a is number, b is number) returns map
{
    const cs = circle.coordSystem;
    const R = circle.radius;

    const C = cs.origin;
    const X = cs.xAxis;
    const Y = cross(cs.zAxis, cs.xAxis);

    // Convert unitless radians -> Angle ValueWithUnits for trig
    const aA = a * radian;
    const bA = b * radian;

    // 2D endpoint points on unit circle in local XY
    const p0_2 = vector([cos(aA), sin(aA)]);
    const p2_2 = vector([cos(bA), sin(bA)]);

    // 2D tangents (unit) on unit circle
    const t0_2 = vector([-sin(aA), cos(aA)]);
    const t2_2 = vector([-sin(bA), cos(bA)]);

    // Intersect tangents in 2D to get middle control point
    const p1_2 = intersectLines2D(p0_2, t0_2, p2_2, t2_2);

    // Lift 2D -> 3D: P = C + R*(x*X + y*Y)
    const P0 = C + R * (p0_2[0] * X + p0_2[1] * Y);
    const P1 = C + R * (p1_2[0] * X + p1_2[1] * Y);
    const P2 = C + R * (p2_2[0] * X + p2_2[1] * Y);

    const delta = b - a;                 // unitless radians
    const w = cos((delta / 2) * radian); // <-- trig needs Angle

    const wSafe = (abs(w) < 1e-9) ? (w >= 0 ? 1e-9 : -1e-9) : w;
    
    

    return {
        "degree" : 2,
        "dimension" : 3,
        "isRational" : true,
        "isPeriodic" : false,
        "controlPoints" : [P0, P1, P2],
        "weights" : [1, wSafe, 1],
        "knots" : [0, 0, 0, 1, 1, 1]
    };
}

/**
 * Intersection of two infinite 2D lines:
 *   L0: p = p0 + s*d0
 *   L1: p = p1 + t*d1
 *
 * Returns the intersection point in 2D.
 *
 * NOTE:
 * - If lines are nearly parallel, this returns a fallback (midpoint) to avoid blowing up.
 * - For circular arcs with small sweep, tangents are well-behaved.
 */
function intersectLines2D(p0 is Vector, d0 is Vector, p1 is Vector, d1 is Vector) returns Vector
{
    // Solve: p0 + s*d0 = p1 + t*d1
    // In 2D: [d0, -d1] [s, t]^T = (p1 - p0)
    const a00 = d0[0];
    const a01 = -d1[0];
    const a10 = d0[1];
    const a11 = -d1[1];

    const bx = p1[0] - p0[0];
    const by = p1[1] - p0[1];

    const det = a00 * a11 - a01 * a10;

    if (abs(det) < 1e-12)
    {
        // Nearly parallel; fallback to something reasonable.
        return (p0 + p1) / 2;
    }

    const s = (bx * a11 - a01 * by) / det;
    return p0 + s * d0;
}


/**
 * ============================================================================
 * Poly-Arc Approximation Utilities (NURBS-only)
 * ============================================================================
 *
 * Goal:
 *   - Input: an array of non-rational BSplineCurve maps (unordered; may contain 1)
 *   - Output: an ordered chain approximated by primitives:
 *       - Line segments (Line map)
 *       - Arc segments (Circle map + angle bounds)
 *
 * Constraints:
 *   - Never create bodies or edges.
 *   - Work strictly with curve definitions (BSplineCurve maps) and primitives.
 *
 * Philosophy:
 *   - Order and orient splines into a polycurve chain.
 *   - Build initial over-segmentation using knot spans (grouped).
 *   - Fit line-or-arc per segment.
 *   - Merge adjacent segments greedily until stable, BUT:
 *       - Never merge across "hard" boundaries (discontinuity above tolerance).
 *       - Do not enforce tangency at hard boundaries.
 */

/** --------------------------------------------------------------------------
 * Types (represented as maps)
 * ---------------------------------------------------------------------------
 *
 * Segment map (returned):
 *   {
 *     "type" : "line" | "arc",
 *     "p0" : Vector,         // endpoint, length units
 *     "p1" : Vector,         // endpoint, length units
 *     "curveIndex0" : number, "u0" : number,  // start location in source polycurve
 *     "curveIndex1" : number, "u1" : number,  // end location in source polycurve
 *
 *     // If type == "line":
 *     "line" : Line,         // { origin, direction }
 *     "t0" : number, "t1" : number, // optional scalar params along line
 *
 *     // If type == "arc":
 *     "circle" : Circle,     // { coordSystem, radius }
 *     "theta0" : number, "theta1" : number, // arc bounds in circle CS
 *
 *     // Error stats (optional):
 *     "maxErr" : ValueWithUnits
 *   }
 *
 * Join metadata (between ordered splines):
 *   { "isHard" : boolean, "gap" : ValueWithUnits, "tanAngle" : number, "point" : Vector }
 */



/**
 * Approximate a chain of (unordered) non-rational BSplineCurves with line/arc primitives.
 *
 * @param splines          array of BSplineCurve (non-rational, dimension 3 recommended)
 * @param posTol           maximum allowed deviation (distance)
 * @param planeTol         maximum allowed out-of-plane deviation for arc fits (distance)
 * @param tanDotTol           dot product based tangency
 * @param minLength        minimum segment length; shorter segments are discouraged/merged/ignored
 *
 * @returns map:
 *   {
 *     "orderedSplines" : array,
 *     "joins" : array,        // join metadata between splines (size = n-1, empty if n<2)
 *     "segments" : array      // primitive segments
 *   }
 */
export function approximateSplinesWithPolyArcs(
    splines is array, posTol is ValueWithUnits, planeTol is ValueWithUnits, tanDotTol is number, minLength is ValueWithUnits) returns map
{
    // ---- Defaults ----
    const joinTol = posTol;
    const tanBreakDotTol = tanDotTol;           
    const initialSpansPerSeg = 2;
    const minSamples = 9;
    const maxSamples = 33;

    // 1) Order + orient into a chain
    const ordered = orderAndOrientBSplines(splines, joinTol);

    // 2) Classify joins as hard/soft (hard => no merging across)
    const joins = classifyJoinsHardness(ordered.splines, joinTol, tanBreakDotTol);

    // 3) Build initial segments from knot spans (over-segmented by design)
    var segments = buildInitialSegmentsFromKnotSpans(ordered.splines, joins, initialSpansPerSeg, minLength);

    // 4) Fit primitives for each segment (line or arc)
    segments = fitAllSegments(ordered.splines, segments, posTol, planeTol, minSamples, maxSamples);

    // 5) Merge until stable (greedy left-to-right), honoring hard boundaries
    segments = mergeUntilStable(ordered.splines, segments, joins, posTol, planeTol, minLength, minSamples, maxSamples);

    return {
        "orderedSplines" : ordered.splines,
        "joins" : joins,
        "segments" : segments
    };
}

/** --------------------------------------------------------------------------
 * Step 1: Ordering & orientation
 * -------------------------------------------------------------------------- */

/**
 * Order and orient splines into a continuous chain using endpoint proximity.
 *
 * Notes:
 * - If input contains only one spline, it is returned as-is.
 * - This is intended to be reusable for other NURBS tools downstream.
 *
 * @returns map: { "splines" : array, "flipFlags" : array }
 */
export function orderAndOrientBSplines(splines is array, joinTol is ValueWithUnits) returns map
{
    // TODO: Implement robust ordering:
    //   - Extract endpoints per spline
    //   - Build adjacency by endpoint distance <= joinTol
    //   - Walk chain (open: start at degree-1 endpoint; closed: arbitrary)
    //   - Flip splines so end(i) matches start(i+1)
    //
    // For now: return input as-is (placeholder).
    return { "splines" : splines, "flipFlags" : makeArray(size(splines), false) };
}

/** --------------------------------------------------------------------------
 * Step 2: Join hardness classification (critical for your "no enforced continuity")
 * -------------------------------------------------------------------------- */

/**
 * Compute join metadata between consecutive ordered splines.
 * A join is HARD if:
 *   - endpoint gap > joinTol, OR
 *   - tangent angle mismatch > tanBreakTol
 *
 * HARD join behavior:
 *   - never create segments that cross this boundary
 *   - never attempt merges across it
 *
 * SOFT join behavior:
 *   - merges may cross it (if tolerance allows)
 *   - still no requirement that final primitives be tangent if merge fails
 */
export function classifyJoinsHardness(orderedSplines is array, joinTol is ValueWithUnits, tanBreakDotTol is number) returns array
{
    if (size(orderedSplines) < 2)
        return [];

    var joins = [];
    for (var i = 0; i < size(orderedSplines) - 1; i += 1)
    {
        const a = orderedSplines[i];
        const b = orderedSplines[i + 1];

        // TODO: Replace with exact endpoint evaluation for your BSplineCurve definition.
        // Placeholder endpoint getters:
        const pa = evalBSplineEndPoint(a, /*isStart*/ false);
        const pb = evalBSplineEndPoint(b, /*isStart*/ true);

        const gap = norm(pa - pb);
        const ta = evalBSplineEndTangent(a, /*isStart*/ false);
        const tb = evalBSplineEndTangent(b, /*isStart*/ true);

        const isHard = (gap > joinTol) || tangentMismatch(ta, tb, tanBreakDotTol);

        const da = norm(ta);
        const db = norm(tb);
        const tanAngle = (da == 0 || db == 0) ? PI : acos(clamp(dot(ta/da, tb/db), -1, 1));
        joins = append(joins, { "isHard" : isHard, "gap" : gap, "tanAngle" : tanAngle, "point" : (pa + pb) / 2 });
    }
    return joins;
}

/** --------------------------------------------------------------------------
 * Step 3: Knot-span based initial segmentation
 * -------------------------------------------------------------------------- */

/**
 * Build an initial list of segments by grouping knot spans.
 * - Never crosses hard joins.
 * - Starts "over-segmented" so merge can collapse quickly.
 */
export function buildInitialSegmentsFromKnotSpans(
    orderedSplines is array,
    joins is array,
    spansPerSeg is number,
    minLength is ValueWithUnits) returns array
{
    var segments = [];

    for (var curveIndex = 0; curveIndex < size(orderedSplines); curveIndex += 1)
    {
        const c = orderedSplines[curveIndex];
        const spans = getUniqueKnotSpans(c); // array of {u0, u1}
        
       /* println("curveIndex=" ~ curveIndex
            ~ " degree=" ~ c.degree
            ~ " nCtrl=" ~ size(c.controlPoints)
            ~ " nKnots=" ~ size(c.knots)
            ~ " nSpans=" ~ size(spans)); */
            
        // Group spans into blocks of spansPerSeg
        var blockStart = 0;
        while (blockStart < size(spans))
        {
            
            var blockEnd = min(blockStart + spansPerSeg - 1, size(spans) - 1);

            const u0 = spans[blockStart].u0;
            const u1 = spans[blockEnd].u1;
            /*
            println("  blockStart=" ~ blockStart
                ~ " blockEnd=" ~ blockEnd
                ~ " u0=" ~ u0
                ~ " u1=" ~ u1);
                */

            // Segment references a single source spline for now.
            // Merge step may later create segments spanning across soft joins.
            const p0 = evalBSplineAtParam(c, u0);
            const p1 = evalBSplineAtParam(c, u1);
            
            const L = norm(p1 - p0);
            /*
            println("    L=" ~ L ~ "  minLength=" ~ minLength);
            println("    p0=" ~ p0);
            println("    p1=" ~ p1);*/

            if (norm(p1 - p0) >= minLength)
            {
                segments = append(segments, {
                    "type" : "unfit",
                    "p0" : p0,
                    "p1" : p1,
                    "curveIndex0" : curveIndex, "u0" : u0,
                    "curveIndex1" : curveIndex, "u1" : u1
                });
            }

            blockStart = blockEnd + 1;
        }

        // IMPORTANT: we do not create a segment that crosses from curveIndex -> curveIndex+1 here.
        // That is handled (optionally) during merge, and only if joins[curveIndex] is soft.
    }

    return segments;
}

function getSplineDomain(c is map) returns map
{
    // Active domain for (typical) clamped B-splines:
    // uMin = knots[degree]
    // uMax = knots[nCtrl]
    const p = c.degree;
    const nCtrl = size(c.controlPoints);
    return { "uMin" : c.knots[p], "uMax" : c.knots[nCtrl] };
}

/**
 * Return unique knot spans (intervals between consecutive distinct knot values).
 *
 * @returns array of { "u0" : number, "u1" : number }
 */
export function getUniqueKnotSpans(c is map) returns array
{
    const dom = getSplineDomain(c);
    const knots = c.knots;

    var spans = [];
    for (var i = 0; i < size(knots) - 1; i += 1)
    {
        var u0 = knots[i];
        var u1 = knots[i + 1];
        if (u1 <= u0)
            continue;

        u0 = max(u0, dom.uMin);
        u1 = min(u1, dom.uMax);

        if (u1 > u0)
            spans = append(spans, { "u0" : u0, "u1" : u1 });
    }
    return spans;
}

/** --------------------------------------------------------------------------
 * Step 4: Fit primitives for segments
 * -------------------------------------------------------------------------- */

/**
 * Fit line-or-arc for every segment in the list.
 * This is a pure function over NURBS definitions + intervals.
 */
export function fitAllSegments(
    orderedSplines is array,
    segments is array,
    posTol is ValueWithUnits,
    planeTol is ValueWithUnits,
    minSamples is number,
    maxSamples is number) returns array
{
    var out = [];
    for (var i = 0; i < size(segments); i += 1)
    {
        const seg = segments[i];
        const fit = fitLineOrArcForSegment(orderedSplines, seg, posTol, planeTol, minSamples, maxSamples);
        out = append(out, fit);
    }
    return out;
}

/**
 * Fit a Line or Circle-arc to a segment.
 *
 * Implementation notes:
 * - First try to classify as line-like (max deviation from chord < posTol, etc.)
 * - Else fit a circle in best-fit plane (or assume planar).
 * - Return a segment map with "type" = "line" or "arc".
 */
export function fitLineOrArcForSegment(
    orderedSplines is array,
    seg is map,
    posTol is ValueWithUnits,
    planeTol is ValueWithUnits,
    minSamples is number,
    maxSamples is number) returns map
{
    // For now we only support segments that live on a single spline
    // (Your current segmentation produces this; cross-spline segments can come later.)
    const c = orderedSplines[seg.curveIndex0];
    const u0 = seg.u0;
    const u1 = seg.u1;

    const n = max(minSamples, 7);
    const pts = sampleSplineSegmentPositions(c, u0, u1, n);

    const p0 = pts[0];
    const p1 = pts[size(pts) - 1];
    const pm = pts[floor((size(pts) - 1) / 2)];

    // 1) Line test: if max deviation from chord <= posTol, treat as a line
    const chordErr = maxDistanceToChord(pts, p0, p1);
    if (chordErr <= posTol)
    {
        var dir = p1 - p0;
        const len = norm(dir);
        if (len == 0 * meter)
            return seg;

        dir = dir / len;
        const line = { "origin" : p0, "direction" : dir };

        return mergeMaps(seg, {
            "type" : "line",
            "p0" : p0, "p1" : p1,
            "line" : line,
            "t0" : 0,
            "t1" : len,
            "maxErr" : chordErr
        });
    }

    // 2) Arc fit: simple 3-point circle
    const circFit = circleThrough3Points(p0, pm, p1);
    if (circFit == undefined)
    {
        // fallback to line if points are collinear
        var dir2 = p1 - p0;
        const len2 = norm(dir2);
        if (len2 == 0 * meter) return seg;
        dir2 = dir2 / len2;
        return mergeMaps(seg, {
            "type" : "line",
            "p0" : p0, "p1" : p1,
            "line" : { "origin" : p0, "direction" : dir2 },
            "t0" : 0,
            "t1" : len2,
            "maxErr" : chordErr
        });
    }

    const circle = circFit.circle;

    // Optional: planeTol check (out-of-plane error)
    // Since circle is planar by construction, measure point distance to plane.
    // For now we skip this, but you can add it once you're ready.

    const th0 = circleAngle(circle, p0);
    const th1 = circleAngle(circle, p1);

    return mergeMaps(seg, {
        "type" : "arc",
        "p0" : p0, "p1" : p1,
        "circle" : circle,
        "theta0" : th0,
        "theta1" : th1,
        "maxErr" : chordErr // placeholder; later compute real circle error
    });
}

/** --------------------------------------------------------------------------
 * Step 5: Merge until stable (honor hard joins; allow line collapse)
 * -------------------------------------------------------------------------- */

/**
 * Repeatedly perform merge passes until no merges occur.
 */
export function mergeUntilStable(
    orderedSplines is array,
    segments is array,
    joins is array,
    posTol is ValueWithUnits,
    planeTol is ValueWithUnits,
    minLength is ValueWithUnits,
    minSamples is number,
    maxSamples is number) returns array
{
    var changed = true;
    var current = segments;

    while (changed)
    {
        const pass = mergePassOnce(orderedSplines, current, joins, posTol, planeTol, minLength, minSamples, maxSamples);
        current = pass.segments;
        changed = pass.changed;
    }

    return current;
}

/**
 * One greedy left-to-right merge pass.
 *
 * Merge rules:
 * - Only consider adjacent segments.
 * - Do not merge if the boundary crosses a HARD join.
 * - Try to fit union as a LINE first; if fails, try ARC.
 * - Accept merge only if union max error <= posTol (and plane error <= planeTol, if used).
 */
export function mergePassOnce(
    orderedSplines is array,
    segments is array,
    joins is array,
    posTol is ValueWithUnits,
    planeTol is ValueWithUnits,
    minLength is ValueWithUnits,
    minSamples is number,
    maxSamples is number) returns map
{
    var out = [];
    var i = 0;
    var changed = false;

    while (i < size(segments))
    {
        if (i == size(segments) - 1)
        {
            out = append(out, segments[i]);
            break;
        }

        const a = segments[i];
        const b = segments[i + 1];

        if (!canAttemptMergeAcrossBoundary(a, b, joins))
        {
            out = append(out, a);
            i += 1;
            continue;
        }

        const unionSeg = makeUnionSegment(a, b);

        // Attempt union fit
        const fit = fitLineOrArcForSegment(orderedSplines, unionSeg, posTol, planeTol, minSamples, maxSamples);

        // Validate fit error (placeholder always succeeds right now)
        if (isFitAcceptable(orderedSplines, fit, posTol, planeTol, minSamples, maxSamples))
        {
            out = append(out, fit);
            changed = true;
            i += 2; // consumed two segments
        }
        else
        {
            out = append(out, a);
            i += 1;
        }
    }

    return { "segments" : out, "changed" : changed };
}

/**
 * Decide whether two segments are allowed to merge, based on HARD joins.
 *
 * IMPORTANT:
 * - We do NOT require tangency between primitives.
 * - We only prevent merges that cross joins that were classified as "hard"
 *   from the ORIGINAL ordered BSpline chain.
 */
function canAttemptMergeAcrossBoundary(a is map, b is map, joins is array) returns boolean
{
    // If segments come from the same source curve, always okay to attempt.
    if (a.curveIndex1 == b.curveIndex0)
    {
        if (a.curveIndex1 == a.curveIndex0 && b.curveIndex0 == b.curveIndex1)
        {
            // Same curve boundary
            return true;
        }

        // Crossing a curve-to-curve boundary. That boundary index is curveIndex1 (join between curveIndex1 and curveIndex1+1)
        const joinIndex = a.curveIndex1; // join between curveIndex1 and curveIndex1+1
        if (joinIndex >= 0 && joinIndex < size(joins))
            return !joins[joinIndex].isHard;

        // If we don't have join info, be conservative:
        return false;
    }

    // Non-adjacent in source indexing => do not merge (shouldn't happen if segments are well formed)
    return false;
}

/**
 * Create a union segment map from two adjacent segments.
 */
function makeUnionSegment(a is map, b is map) returns map
{
    return {
        "type" : "unfit",
        "p0" : a.p0,
        "p1" : b.p1,
        "curveIndex0" : a.curveIndex0, "u0" : a.u0,
        "curveIndex1" : b.curveIndex1, "u1" : b.u1
    };
}

/**
 * Placeholder acceptance test.
 * Replace with sampling-based max deviation to primitive.
 */
function isFitAcceptable(
    orderedSplines is array,
    seg is map,
    posTol is ValueWithUnits,
    planeTol is ValueWithUnits,
    minSamples is number,
    maxSamples is number) returns boolean
{
    // TODO: Evaluate curve points over [u0,u1] (across multiple curves if needed),
    // compute point-to-line or point-to-arc distance, track max.
    return true;
}

/** --------------------------------------------------------------------------
 * Low-level evaluation stubs (you will replace with your existing backend)
 * -------------------------------------------------------------------------- */

/**
 * Evaluate a BSplineCurve at parameter u.
 * NOTE: This is a stub. Implement with your NURBS evaluator.
 */
function evalBSplineAtParam(c is map, u is number) returns Vector
{
    const dom = getSplineDomain(c);

    // Clamp u to the valid domain to avoid weird behavior
    const uu = clamp(u, dom.uMin, dom.uMax);

    const result = evaluateSpline({
        "spline" : c,
        "parameters" : [uu],
        "nDerivatives" : 0
    });

    // result[0][0] is the position at parameters[0]
    return result[0][0];
}

/**
 * Endpoint convenience: start or end point.
 */
function evalBSplineEndPoint(c is map, isStart is boolean) returns Vector
{
    // TODO: evaluate at uMin/uMax using knot vector endpoints.
    // For clamped splines, uMin = knots[degree], uMax = knots[n+1] typically.
    // Placeholder uses control points.
    return isStart ? c.controlPoints[0] : c.controlPoints[size(c.controlPoints) - 1];
}

/**
 * Evaluate tangent (unit direction) at start/end.
 * Stub: you’ll implement using your curve derivative evaluator.
 */
function evalBSplineEndTangent(c is map, isStart is boolean) returns Vector
{
    // TODO: compute derivative at uMin/uMax and normalize.
    // Placeholder uses chord between first/last two control points.
    if (isStart)
    {
        var d = c.controlPoints[min(1, size(c.controlPoints)-1)] - c.controlPoints[0];
        const n = norm(d);
        return (n == 0 * meter) ? vector([1,0,0]) : d / n;
    }
    else
    {
        const ncp = size(c.controlPoints);
        var d = c.controlPoints[ncp - 1] - c.controlPoints[max(0, ncp - 2)];
        const n = norm(d);
        return (n == 0 * meter) ? vector([1,0,0]) : d / n;
    }
}



/**
 * Return true if two tangent vectors differ by more than the given dot tolerance.
 * Vectors do NOT need to be unit; normalization is handled internally.
 */
export function tangentMismatch(a is Vector, b is Vector, dotTol is number) returns boolean
{
    const na = norm(a);
    const nb = norm(b);
    if (na == 0 || nb == 0)
        return true; // treat undefined tangent as mismatch

    const ua = a / na;
    const ub = b / nb;

    return dot(ua, ub) < dotTol;
}

/**
 * Sample positions along a single BSplineCurve segment [u0, u1].
 * Uses evaluateSpline (no custom de Boor needed).
 */
function sampleSplineSegmentPositions(c is map, u0 is number, u1 is number, n is number) returns array
{
    // Ensure increasing order
    var a = u0;
    var b = u1;
    if (b < a)
    {
        const tmp = a; a = b; b = tmp;
    }

    var params = [];
    if (n < 2) n = 2;
    for (var i = 0; i < n; i += 1)
    {
        const t = i / (n - 1);
        params = append(params, a + (b - a) * t);
    }

    const res = evaluateSpline({
        "spline" : c,
        "parameters" : params,
        "nDerivatives" : 0
    });

    return res[0]; // positions
}

/**
 * Max distance from points to the infinite chord line through p0->p1.
 * (For short segments, infinite vs segment distance doesn't matter much; we can tighten later.)
 */
function maxDistanceToChord(points is array, p0 is Vector, p1 is Vector) returns ValueWithUnits
{
    var d = p1 - p0;
    const L = norm(d);
    if (L == 0 * meter)
        return 0 * meter;
    const u = d / L;

    var maxErr = 0 * meter;
    for (var i = 0; i < size(points); i += 1)
    {
        const v = points[i] - p0;
        const proj = dot(v, u) * u;
        const perp = v - proj;
        const e = norm(perp);
        if (e > maxErr) maxErr = e;
    }
    return maxErr;
}

/**
 * Fit a circle through 3 points (if non-collinear).
 * Returns undefined if points are nearly collinear.
 */
function circleThrough3Points(p0 is Vector, pm is Vector, p1 is Vector) returns map
{
    // Plane normal
    const a = pm - p0;
    const b = p1 - p0;
    const nRaw = cross(a, b);
    const nLen = norm(nRaw);
    if (nLen == 0 * meter * meter)
        return undefined; // collinear => no circle

    const n = nRaw / nLen; // unit normal (unitless)

    // Build plane basis (u,v) in the plane
    const uRaw = a;
    const uLen = norm(uRaw);
    if (uLen == 0 * meter)
        return undefined;
    const u = uRaw / uLen;      // unitless
    const v = cross(n, u);      // unitless

    // 2D coordinates in (u,v) with origin at p0
    const Bx = dot(a, u);  const By = dot(a, v);
    const Cx = dot(b, u);  const Cy = dot(b, v);

    const B2 = Bx*Bx + By*By;
    const C2 = Cx*Cx + Cy*Cy;
    
    const D = 2 * (Bx * Cy - By * Cx);
    
    // Scale-aware threshold: compares against typical magnitude of D.
    // B2 and C2 are length^2 already.
    const scale2 = B2 + C2;            // length^2
    const eps2 = scale2 * 1e-12;       // length^2 (tunable)
    if (abs(D) < eps2)
        return undefined;

    const Ux = (B2 * Cy - By * C2) / D;
    const Uy = (Bx * C2 - B2 * Cx) / D;

    const center = p0 + Ux * u + Uy * v;
    const R = norm(p0 - center);

    // Define circle coord system:
    // - origin at center
    // - zAxis = n
    // - xAxis = direction from center to p0
    var xAxis = p0 - center;
    const xLen = norm(xAxis);
    if (xLen == 0 * meter)
        return undefined;
    xAxis = xAxis / xLen;

    const cs = { "origin" : center, "xAxis" : xAxis, "zAxis" : n };

    return { "circle" : { "coordSystem" : cs, "radius" : R }, "normal" : n };
}

/**
 * Angle of a point on the circle in the circle's coordSystem.
 */
function circleAngle(circle is map, p is Vector) returns number
{
    const cs = circle.coordSystem;
    const c = cs.origin;
    const x = cs.xAxis;
    const y = cross(cs.zAxis, cs.xAxis);

    const v = p - c;
    const vx = dot(v, x);
    const vy = dot(v, y);

    // atan2 returns an Angle (ValueWithUnits). Convert to unitless radians.
    return atan2(vy, vx) / radian;
}