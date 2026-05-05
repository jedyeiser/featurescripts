FeatureScript 2892;

import(path : "onshape/std/common.fs", version : "2892.0");
import(path : "onshape/std/math.fs", version : "2892.0");
import(path : "onshape/std/vector.fs", version : "2892.0");
import(path : "onshape/std/sketch.fs", version : "2892.0");
import(path : "onshape/std/surfaceGeometry.fs", version : "2892.0");
import(path : "onshape/std/containers.fs", version : "2892.0");
export import(path : "onshape/std/nurbsUtils.fs", version : "2892.0");

export const PositionTolBounds = {(millimeter) : [0.00001, 0.0001, 1]} as LengthBoundSpec;
export const PlaneTolBounds = {(millimeter) : [0.00001, 0.00001, 1]} as LengthBoundSpec;
export const MinLengthBounds = {(millimeter) : [1, 10, 100]} as LengthBoundSpec;
export const TanTolBounds = {(degree) : [0.00001, 0.1, 1]} as AngleBoundSpec;
export const NumSamplesBounds = {(unitless) : [10, 16, 200]} as IntegerBoundSpec;
export const MaxDepthBounds = {(unitless) : [3, 8, 12]} as IntegerBoundSpec;

export enum ArcFitOutputType
{
    annotation { "Name" : "Curves" }
    CURVES,
    annotation { "Name" : "Sketch" }
    SKETCH,
    annotation { "Name" : "Curves and sketch" }
    BOTH
}


annotation { "Feature Type Name" : "Arc fit", "Feature Type Description" : "Takes coplanar edges as input and outputs an arc fit representation of the edges. Lines collapse to lines. Continuity is not preserved if input edges are not within a given range" }
export const arcFit = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Edges to fit", "Filter" : EntityType.EDGE }
        definition.selEdges is Query;

        annotation { "Name" : "Position tolerance", "Description" : "Max allowable point-to-arc/line deviation (also the join tolerance for G0 continuity)", "Default" : 0.00001 * millimeter }
        isLength(definition.posTol, PositionTolBounds);

        annotation { "Name" : "Plane tolerance", "Description" : "Allowable out-of-plane error for input edges (coplanarity gate)", "Default" : 0.01 * millimeter }
        isLength(definition.planeTol, PlaneTolBounds);

        annotation { "Name" : "Minimum segment length", "Description" : "Minimum allowable length of an individual arc/line", "Default" : 1 * millimeter }
        isLength(definition.minLength, MinLengthBounds);

        annotation { "Name" : "Tangency tol", "Description" : "Ensure G1 tangency within this angle", "Default" : 0.1 * degree }
        isAngle(definition.tanTol, TanTolBounds);

        annotation { "Name" : "Validation samples per arc", "Description" : "Number of samples per arc/line used to validate the fit against the original spline" }
        isInteger(definition.numSamples, NumSamplesBounds);

        annotation { "Name" : "Max subdivision depth", "Description" : "Maximum recursion depth when bisecting arcs to satisfy position tolerance" }
        isInteger(definition.maxDepth, MaxDepthBounds);

        annotation { "Name" : "Output type" }
        definition.outputType is ArcFitOutputType;

        annotation { "Name" : "Group output", "Default" : false, "Description" : "When true, uses opExtractWires to group the curve output of this feature" }
        definition.extractWires is boolean;
    }
    {
        var evEdges = evaluateQuery(context, qUnion([definition.selEdges]));

        if (size(evEdges) == 0)
        {
            throw regenError("Arc fit: no edges selected.");
        }

        var bSplines = mapArray(evEdges, function(e)
                {
                    return evApproximateBSplineCurve(context, {
                                "edge" : e,
                                "forceNonRational" : true
                            });
                });

        println("arcFit: evEdges=" ~ size(evEdges) ~ " bSplines=" ~ size(bSplines));
        println("arcFit: outputType=" ~ definition.outputType ~ " posTol=" ~ toString(definition.posTol / millimeter) ~ "mm minLength=" ~ toString(definition.minLength / millimeter) ~ "mm");

        // 1) Sample input curves once for both plane fit and coplanarity check.
        const allSamples = sampleAllSplines(bSplines, definition.numSamples);
        println("arcFit: sampled " ~ size(allSamples) ~ " points across all input curves");

        // 2) Best-fit plane via centroid + two principal in-plane directions.
        const fitPlane = computeBestFitPlane(allSamples);

        // 3) Coplanarity gate. Fail loudly with deviation/tolerance in the message.
        assertCoplanar(allSamples, fitPlane, definition.planeTol);

        // 4) Approximate with poly-arcs (subdivide-to-convergence, then merge).
        const dotTol = cos(definition.tanTol);
        const polyArcs = approximateSplinesWithPolyArcs(
                bSplines,
                definition.posTol,
                definition.planeTol,
                dotTol,
                definition.minLength,
                definition.numSamples,
                definition.maxDepth);

        const segments = polyArcs.segments;
        println("arcFit: pipeline produced " ~ size(segments) ~ " segments");

        // Defensive: existing feature instances saved before outputType was added will
        // have definition.outputType == undefined. undefined == anything is false in FS,
        // so without this fallback BOTH output branches silently skip.
        const outputType = (definition.outputType != undefined) ? definition.outputType : ArcFitOutputType.CURVES;
        println("arcFit: outputType=" ~ outputType);

        // 5) Curve output (CURVES or BOTH)
        if (outputType == ArcFitOutputType.CURVES || outputType == ArcFitOutputType.BOTH)
        {
            const NURBS = primitivesToBSplines(segments);
            println("arcFit: primitivesToBSplines produced " ~ size(NURBS) ~ " NURBS curves");
            var validCount = 0;
            for (var i = 0; i < size(NURBS); i += 1)
            {
                if (canBeBSplineCurve(NURBS[i]))
                {
                    validCount += 1;
                }
            }
            println("arcFit: " ~ validCount ~ " of " ~ size(NURBS) ~ " NURBS pass canBeBSplineCurve");
            emitCurves(context, id, NURBS, definition.extractWires);
        }

        // 6) Sketch output (SKETCH or BOTH)
        if (outputType == ArcFitOutputType.SKETCH || outputType == ArcFitOutputType.BOTH)
        {
            emitSketchFromPrimitives(context, id + "arcSketch", fitPlane, segments);
        }
    });

function emitCurves(context is Context, id is Id, NURBS is array, extractWires is boolean)
{
    println("arcFit: emitCurves entered with " ~ size(NURBS) ~ " NURBS");

    var edgeQueries = [];
    var bodyQueries = [];
    for (var i = 0; i < size(NURBS); i += 1)
    {
        const c = NURBS[i];
        if (!canBeBSplineCurve(c))
        {
            // LOUD failure instead of silent skip so we can see exactly what's wrong.
            throw regenError("arcFit: NURBS[" ~ i ~ "] failed canBeBSplineCurve check. "
                    ~ "degree=" ~ c.degree
                    ~ " dim=" ~ c.dimension
                    ~ " nCtrl=" ~ size(c.controlPoints)
                    ~ " nKnots=" ~ size(c.knots)
                    ~ " isRational=" ~ c.isRational
                    ~ " isPeriodic=" ~ c.isPeriodic);
        }

        println("arcFit: opCreateBSplineCurve i=" ~ i ~ " degree=" ~ c.degree ~ " nCtrl=" ~ size(c.controlPoints));
        opCreateBSplineCurve(context, id + ("arcNURBSFit" ~ i), {
                    "bSplineCurve" : c
                });
        edgeQueries = append(edgeQueries, qCreatedBy(id + ("arcNURBSFit" ~ i), EntityType.EDGE));
        bodyQueries = append(bodyQueries, qCreatedBy(id + ("arcNURBSFit" ~ i), EntityType.BODY));
    }

    println("arcFit: emitCurves created " ~ size(bodyQueries) ~ " bodies");

    if (size(bodyQueries) > 1 && extractWires)
    {
        opExtractWires(context, id + "extractNURBSWires", {
                    "edges" : qUnion(edgeQueries)
                });
        opDeleteBodies(context, id + "deleteNURBSWires", {
                    "entities" : qUnion(bodyQueries)
                });
    }
}

function isLineSegment(seg is map) returns boolean
{
    return any(keys(seg), function(x)
        {
            return x == "line";
        });
}

function isArcSegment(seg is map) returns boolean
{
    return any(keys(seg), function(x)
        {
            return x == "circle";
        });
}

/**
 * Convert an array of primitive segments (lines/arcs) into an array of BSplineCurve maps.
 *
 * Lines become non-rational degree-1 B-splines.
 * Arcs become rational degree-2 quadratic NURBS. Large arcs are split into <= 90 deg pieces.
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
            const arcs = arcSegmentToQuadraticNurbsPieces(seg);
            for (var j = 0; j < size(arcs); j += 1)
            {
                out = append(out, arcs[j]);
            }
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
            "knots" : knotArray([0, 0, 1, 1])
        };
}

/**
 * Convert an arc segment (Circle + theta bounds) into one or more quadratic rational NURBS pieces.
 */
export function arcSegmentToQuadraticNurbsPieces(seg is map) returns array
{
    const circle = seg.circle;
    var t0 = seg.theta0;
    var t1 = seg.theta1;

    var d = t1 - t0;
    while (d > 2 * PI)
    {
        d -= 2 * PI;
    }
    while (d < -2 * PI)
    {
        d += 2 * PI;
    }

    const maxPiece = PI / 2;
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

function makeQuadraticArcNurbs(circle is map, a is number, b is number) returns map
{
    const cs = circle.coordSystem;
    const R = circle.radius;

    const C = cs.origin;
    const X = cs.xAxis;
    const Y = cross(cs.zAxis, cs.xAxis);

    const aA = a * radian;
    const bA = b * radian;

    const p0_2 = vector([cos(aA), sin(aA)]);
    const p2_2 = vector([cos(bA), sin(bA)]);

    const t0_2 = vector([-sin(aA), cos(aA)]);
    const t2_2 = vector([-sin(bA), cos(bA)]);

    const p1_2 = intersectLines2D(p0_2, t0_2, p2_2, t2_2);

    const P0 = C + R * (p0_2[0] * X + p0_2[1] * Y);
    const P1 = C + R * (p1_2[0] * X + p1_2[1] * Y);
    const P2 = C + R * (p2_2[0] * X + p2_2[1] * Y);

    const delta = b - a;
    const w = cos((delta / 2) * radian);

    const wSafe = (abs(w) < 1e-9) ? (w >= 0 ? 1e-9 : -1e-9) : w;

    return {
            "degree" : 2,
            "dimension" : 3,
            "isRational" : true,
            "isPeriodic" : false,
            "controlPoints" : [P0, P1, P2],
            "weights" : [1, wSafe, 1],
            "knots" : knotArray([0, 0, 0, 1, 1, 1])
        };
}

function intersectLines2D(p0 is Vector, d0 is Vector, p1 is Vector, d1 is Vector) returns Vector
{
    const a00 = d0[0];
    const a01 = -d1[0];
    const a10 = d0[1];
    const a11 = -d1[1];

    const bx = p1[0] - p0[0];
    const by = p1[1] - p0[1];

    const det = a00 * a11 - a01 * a10;

    if (abs(det) < 1e-12)
    {
        return (p0 + p1) / 2;
    }

    const s = (bx * a11 - a01 * by) / det;
    return p0 + s * d0;
}

/**
 * ============================================================================
 * Coplanarity / best-fit plane
 * ============================================================================
 */

/**
 * Sample positions across all input splines for the coplanarity check and plane fit.
 */
export function sampleAllSplines(splines is array, numSamples is number) returns array
{
    var out = [];
    for (var i = 0; i < size(splines); i += 1)
    {
        const dom = getSplineDomain(splines[i]);
        const pts = sampleSplineSegmentPositions(splines[i], dom.uMin, dom.uMax, max(numSamples, 10));
        for (var j = 0; j < size(pts); j += 1)
        {
            out = append(out, pts[j]);
        }
    }
    return out;
}

/**
 * Compute centroid of a point cloud.
 */
function pointCentroid(points is array) returns Vector
{
    var sumX = 0 * meter;
    var sumY = 0 * meter;
    var sumZ = 0 * meter;
    const n = size(points);
    for (var i = 0; i < n; i += 1)
    {
        sumX += points[i][0];
        sumY += points[i][1];
        sumZ += points[i][2];
    }
    return vector([sumX / n, sumY / n, sumZ / n]);
}

/**
 * Compute the best-fit plane through a set of (assumed) coplanar points.
 *
 * Picks the centroid as plane origin, then chooses two principal in-plane
 * directions geometrically:
 *   d1 = direction from centroid to farthest sample
 *   d2 = perpendicular component of (centroid -> sample farthest from line through centroid in d1)
 *   normal = d1 x d2
 *
 * For truly coplanar input this recovers the exact plane. For near-coplanar input
 * the normal matches PCA's smallest-eigenvalue eigenvector to first order, and the
 * downstream coplanarity check will reject inputs whose deviation exceeds planeTol.
 */
export function computeBestFitPlane(points is array) returns Plane
{
    if (size(points) < 3)
    {
        throw regenError("Arc fit: need at least 3 sample points to fit a plane.");
    }

    const c = pointCentroid(points);

    // First principal direction: vector from centroid to farthest sample.
    var maxDist1 = 0 * meter;
    var farIdx1 = -1;
    for (var i = 0; i < size(points); i += 1)
    {
        const d = norm(points[i] - c);
        if (d > maxDist1)
        {
            maxDist1 = d;
            farIdx1 = i;
        }
    }
    if (farIdx1 < 0 || maxDist1 == 0 * meter)
    {
        throw regenError("Arc fit: input points are degenerate (all at same location).");
    }

    const v1 = points[farIdx1] - c;
    const d1 = v1 / norm(v1);

    // Second principal direction: maximize perpendicular distance to line {c, d1}.
    var maxPerp = 0 * meter;
    var farIdx2 = -1;
    for (var i = 0; i < size(points); i += 1)
    {
        const v = points[i] - c;
        const along = dot(v, d1) * d1;
        const perp = v - along;
        const pn = norm(perp);
        if (pn > maxPerp)
        {
            maxPerp = pn;
            farIdx2 = i;
        }
    }
    if (farIdx2 < 0 || maxPerp == 0 * meter)
    {
        throw regenError("Arc fit: input points are collinear; cannot determine a plane.");
    }

    const v2 = points[farIdx2] - c;
    const along2 = dot(v2, d1) * d1;
    const perp2 = v2 - along2;
    const d2 = perp2 / norm(perp2);

    const normalRaw = cross(d1, d2);
    const normalLen = norm(normalRaw);
    if (normalLen == 0)
    {
        throw regenError("Arc fit: principal directions are parallel; cannot determine plane normal.");
    }
    const normal = normalRaw / normalLen;

    return plane(c, normal, d1);
}

/**
 * Throw a regenError if any sample lies more than planeTol off the supplied plane.
 */
export function assertCoplanar(points is array, fitPlane is Plane, planeTol is ValueWithUnits)
{
    var maxDev = 0 * meter;
    for (var i = 0; i < size(points); i += 1)
    {
        const dev = abs(dot(points[i] - fitPlane.origin, fitPlane.normal));
        if (dev > maxDev)
        {
            maxDev = dev;
        }
    }
    if (maxDev > planeTol)
    {
        throw regenError("Arc fit: input edges are not coplanar. "
                ~ "Max out-of-plane deviation: " ~ toString(maxDev / millimeter) ~ " mm. "
                ~ "Plane tolerance: " ~ toString(planeTol / millimeter) ~ " mm. "
                ~ "Either supply coplanar edges or increase plane tolerance.");
    }
}

/**
 * ============================================================================
 * Sketch output
 * ============================================================================
 */

/**
 * Emit a sketch on the supplied plane containing the line/arc primitives.
 *
 * Lines become skLineSegment, arcs become 3-point skArc using start/mid/end
 * projected into the sketch's 2D coordinates.
 */
export function emitSketchFromPrimitives(context is Context, sketchId is Id, fitPlane is Plane, segments is array)
{
    const sk = newSketchOnPlane(context, sketchId, { "sketchPlane" : fitPlane });

    for (var i = 0; i < size(segments); i += 1)
    {
        const seg = segments[i];

        if (seg["type"] == "line")
        {
            const p0_2d = worldToPlane(fitPlane, seg.p0);
            const p1_2d = worldToPlane(fitPlane, seg.p1);
            if (norm(p1_2d - p0_2d) <= 0 * meter)
            {
                continue;
            }
            skLineSegment(sk, "line_" ~ i, {
                        "start" : p0_2d,
                        "end" : p1_2d
                    });
        }
        else if (seg["type"] == "arc")
        {
            // Use the stored mid-sample point (lies on the fitted circle by construction)
            // instead of (theta0+theta1)/2, which would pick the wrong half on arcs that
            // wrap the +/- pi branch cut.
            const pmid = (seg.pMid != undefined) ? seg.pMid : arcPointAt(seg.circle, (seg.theta0 + seg.theta1) / 2);

            const p0_2d = worldToPlane(fitPlane, seg.p0);
            const p1_2d = worldToPlane(fitPlane, seg.p1);
            const pm_2d = worldToPlane(fitPlane, pmid);

            skArc(sk, "arc_" ~ i, {
                        "start" : p0_2d,
                        "mid" : pm_2d,
                        "end" : p1_2d
                    });
        }
        // "unfit" segments are silently dropped from sketch output; they shouldn't
        // appear here in practice because subdivideUntilFit + fit always assign a type.
    }

    skSolve(sk);
}

/**
 * ============================================================================
 * Poly-Arc Approximation Pipeline (NURBS-only)
 * ============================================================================
 *
 * Pipeline:
 *   1) Order + orient input splines into a chain
 *   2) Classify joins as hard/soft
 *   3) Build initial segments from knot spans
 *   4) Fit line-or-arc per segment
 *   5) Subdivide-to-convergence: bisect any segment whose fit exceeds posTol
 *   6) Merge until stable (greedy left-to-right), honoring hard boundaries
 */
export function approximateSplinesWithPolyArcs(
        splines is array,
        posTol is ValueWithUnits,
        planeTol is ValueWithUnits,
        tanDotTol is number,
        minLength is ValueWithUnits,
        numSamples is number,
        maxDepth is number) returns map
{
    const joinTol = posTol;
    const tanBreakDotTol = tanDotTol;
    const initialSpansPerSeg = 2;

    // 1) Order + orient into a chain
    const ordered = orderAndOrientBSplines(splines, joinTol);

    // 2) Classify joins as hard/soft (hard => no merging across)
    const joins = classifyJoinsHardness(ordered.splines, joinTol, tanBreakDotTol);

    // 3) Initial segmentation from knot spans
    var segments = buildInitialSegmentsFromKnotSpans(ordered.splines, joins, initialSpansPerSeg, minLength);

    // 4) Initial fit per segment
    segments = fitAllSegments(ordered.splines, segments, posTol, planeTol, numSamples);

    // 5) Subdivide-to-convergence: any segment whose fit exceeds posTol gets bisected
    //    at the parameter of max error and re-fit recursively. Honors maxDepth + minLength floors.
    segments = subdivideUntilFit(ordered.splines, segments, posTol, planeTol, minLength, numSamples, maxDepth);

    // 6) Greedy merge (single pass after subdivision is converged)
    segments = mergeUntilStable(ordered.splines, segments, joins, posTol, planeTol, minLength, numSamples);

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
 * TODO: Real adjacency walk. Currently a pass-through; relies on caller-supplied
 * order being correct (true for current consumers).
 */
export function orderAndOrientBSplines(splines is array, joinTol is ValueWithUnits) returns map
{
    return { "splines" : splines, "flipFlags" : makeArray(size(splines), false) };
}

/** --------------------------------------------------------------------------
 * Step 2: Join hardness classification
 * -------------------------------------------------------------------------- */

export function classifyJoinsHardness(orderedSplines is array, joinTol is ValueWithUnits, tanBreakDotTol is number) returns array
{
    if (size(orderedSplines) < 2)
    {
        return [];
    }

    var joins = [];
    for (var i = 0; i < size(orderedSplines) - 1; i += 1)
    {
        const a = orderedSplines[i];
        const b = orderedSplines[i + 1];

        const pa = evalBSplineEndPoint(a, false);
        const pb = evalBSplineEndPoint(b, true);

        const gap = norm(pa - pb);
        const ta = evalBSplineEndTangent(a, false);
        const tb = evalBSplineEndTangent(b, true);

        const isHard = (gap > joinTol) || tangentMismatch(ta, tb, tanBreakDotTol);

        const da = norm(ta);
        const db = norm(tb);
        const tanAngle = (da == 0 || db == 0) ? PI : acos(clamp(dot(ta / da, tb / db), -1, 1));
        joins = append(joins, { "isHard" : isHard, "gap" : gap, "tanAngle" : tanAngle, "point" : (pa + pb) / 2 });
    }
    return joins;
}

/** --------------------------------------------------------------------------
 * Step 3: Knot-span based initial segmentation
 * -------------------------------------------------------------------------- */

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
        const spans = getUniqueKnotSpans(c);

        var blockStart = 0;
        while (blockStart < size(spans))
        {
            var blockEnd = min(blockStart + spansPerSeg - 1, size(spans) - 1);

            const u0 = spans[blockStart].u0;
            const u1 = spans[blockEnd].u1;

            const p0 = evalBSplineAtParam(c, u0);
            const p1 = evalBSplineAtParam(c, u1);

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
    }

    return segments;
}

function getSplineDomain(c is map) returns map
{
    const p = c.degree;
    const nCtrl = size(c.controlPoints);
    return { "uMin" : c.knots[p], "uMax" : c.knots[nCtrl] };
}

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
        {
            continue;
        }

        u0 = max(u0, dom.uMin);
        u1 = min(u1, dom.uMax);

        if (u1 > u0)
        {
            spans = append(spans, { "u0" : u0, "u1" : u1 });
        }
    }
    return spans;
}

/** --------------------------------------------------------------------------
 * Step 4: Fit primitives for segments
 * -------------------------------------------------------------------------- */

export function fitAllSegments(
        orderedSplines is array,
        segments is array,
        posTol is ValueWithUnits,
        planeTol is ValueWithUnits,
        numSamples is number) returns array
{
    var out = [];
    for (var i = 0; i < size(segments); i += 1)
    {
        const seg = segments[i];
        const fit = fitLineOrArcForSegment(orderedSplines, seg, posTol, planeTol, numSamples);
        out = append(out, fit);
    }
    return out;
}

/**
 * Fit a Line or Circle-arc to a segment. Computes the REAL max deviation
 * (point-to-line for lines; point-to-arc for arcs) and stores it in maxErr.
 */
export function fitLineOrArcForSegment(
        orderedSplines is array,
        seg is map,
        posTol is ValueWithUnits,
        planeTol is ValueWithUnits,
        numSamples is number) returns map
{
    const c = orderedSplines[seg.curveIndex0];
    const u0 = seg.u0;
    const u1 = seg.u1;

    const n = max(numSamples, 10);
    const pts = sampleSplineSegmentPositions(c, u0, u1, n);

    const p0 = pts[0];
    const p1 = pts[size(pts) - 1];
    const pm = pts[floor((size(pts) - 1) / 2)];

    // 1) Line test: if max deviation from chord <= posTol, treat as a line.
    const chordErr = maxDistanceToChord(pts, p0, p1);
    if (chordErr <= posTol)
    {
        var dir = p1 - p0;
        const len = norm(dir);
        if (len == 0 * meter)
        {
            return seg;
        }
        dir = dir / len;
        const lineDef = { "origin" : p0, "direction" : dir };

        return mergeMaps(seg, {
                    "type" : "line",
                    "p0" : p0, "p1" : p1,
                    "line" : lineDef,
                    "t0" : 0,
                    "t1" : len,
                    "maxErr" : chordErr
                });
    }

    // 2) Arc fit: 3-point circle through endpoints + midpoint sample.
    const circFit = circleThrough3Points(p0, pm, p1);
    if (circFit == undefined)
    {
        var dir2 = p1 - p0;
        const len2 = norm(dir2);
        if (len2 == 0 * meter)
        {
            return seg;
        }
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
    const th0 = circleAngle(circle, p0);
    const th1 = circleAngle(circle, p1);
    const thMid = circleAngle(circle, pm);

    // Real arc-error metric: point-to-arc distance over all samples.
    const arcStats = arcSamplesMaxError(pts, circle, th0, th1, u0, u1);

    return mergeMaps(seg, {
                "type" : "arc",
                "p0" : p0, "p1" : p1,
                "pMid" : pm,
                "circle" : circle,
                "theta0" : th0,
                "theta1" : th1,
                "thetaMid" : thMid,
                "maxErr" : arcStats.maxErr,
                "uMaxErr" : arcStats.uMax
            });
}

/** --------------------------------------------------------------------------
 * Step 5: Subdivide-to-convergence (bisect at parameter of max error)
 * -------------------------------------------------------------------------- */

/**
 * Walk every segment and recursively bisect any whose fit exceeds posTol,
 * splitting at the parameter of maximum error. Honors maxDepth and minLength
 * floors with a println warning when either fires.
 */
export function subdivideUntilFit(
        orderedSplines is array,
        segments is array,
        posTol is ValueWithUnits,
        planeTol is ValueWithUnits,
        minLength is ValueWithUnits,
        numSamples is number,
        maxDepth is number) returns array
{
    var out = [];
    for (var i = 0; i < size(segments); i += 1)
    {
        const sub = subdivideOne(orderedSplines, segments[i], posTol, planeTol, minLength, numSamples, 0, maxDepth);
        for (var j = 0; j < size(sub); j += 1)
        {
            out = append(out, sub[j]);
        }
    }
    return out;
}

function subdivideOne(
        orderedSplines is array,
        seg is map,
        posTol is ValueWithUnits,
        planeTol is ValueWithUnits,
        minLength is ValueWithUnits,
        numSamples is number,
        depth is number,
        maxDepth is number) returns array
{
    // If seg is already fitted from upstream, accept the existing fit's maxErr;
    // otherwise re-fit so we have a measured maxErr to act on.
    var fit = seg;
    if (seg["type"] == "unfit" || seg.maxErr == undefined)
    {
        fit = fitLineOrArcForSegment(orderedSplines, seg, posTol, planeTol, numSamples);
    }

    if (fit["type"] != "line" && fit["type"] != "arc")
    {
        // Couldn't fit anything (degenerate); return as-is.
        return [fit];
    }

    if (fit.maxErr <= posTol)
    {
        return [fit];
    }

    // Floor: depth limit
    if (depth >= maxDepth)
    {
        println("arcFit WARNING: max recursion depth (" ~ maxDepth ~ ") reached for segment "
                ~ "[u0=" ~ fit.u0 ~ ", u1=" ~ fit.u1 ~ "]. "
                ~ "Accepting fit with maxErr=" ~ toString(fit.maxErr / millimeter) ~ " mm "
                ~ "(posTol=" ~ toString(posTol / millimeter) ~ " mm).");
        return [fit];
    }

    // Floor: minimum length
    const segLen = norm(fit.p1 - fit.p0);
    if (segLen < 2 * minLength)
    {
        println("arcFit WARNING: segment length " ~ toString(segLen / millimeter) ~ " mm "
                ~ "too short to bisect (minLength=" ~ toString(minLength / millimeter) ~ " mm). "
                ~ "Accepting fit with maxErr=" ~ toString(fit.maxErr / millimeter) ~ " mm.");
        return [fit];
    }

    // Pick split parameter: max-error location, falling back to midpoint if too close to endpoint.
    const c = orderedSplines[fit.curveIndex0];
    var uSplit = (fit.uMaxErr != undefined) ? fit.uMaxErr : ((fit.u0 + fit.u1) / 2);
    const span = fit.u1 - fit.u0;
    if (span > 0)
    {
        const tSplit = (uSplit - fit.u0) / span;
        if (tSplit < 0.05 || tSplit > 0.95)
        {
            uSplit = (fit.u0 + fit.u1) / 2;
        }
    }
    else
    {
        return [fit];
    }

    const pSplit = evalBSplineAtParam(c, uSplit);

    const segA = {
            "type" : "unfit",
            "p0" : fit.p0, "p1" : pSplit,
            "curveIndex0" : fit.curveIndex0, "u0" : fit.u0,
            "curveIndex1" : fit.curveIndex0, "u1" : uSplit
        };
    const segB = {
            "type" : "unfit",
            "p0" : pSplit, "p1" : fit.p1,
            "curveIndex0" : fit.curveIndex0, "u0" : uSplit,
            "curveIndex1" : fit.curveIndex0, "u1" : fit.u1
        };

    const subA = subdivideOne(orderedSplines, segA, posTol, planeTol, minLength, numSamples, depth + 1, maxDepth);
    const subB = subdivideOne(orderedSplines, segB, posTol, planeTol, minLength, numSamples, depth + 1, maxDepth);

    return concatenateArrays(subA, subB);
}

/** --------------------------------------------------------------------------
 * Step 6: Merge until stable (honor hard joins; allow line collapse)
 * -------------------------------------------------------------------------- */

export function mergeUntilStable(
        orderedSplines is array,
        segments is array,
        joins is array,
        posTol is ValueWithUnits,
        planeTol is ValueWithUnits,
        minLength is ValueWithUnits,
        numSamples is number) returns array
{
    var changed = true;
    var current = segments;

    while (changed)
    {
        const pass = mergePassOnce(orderedSplines, current, joins, posTol, planeTol, minLength, numSamples);
        current = pass.segments;
        changed = pass.changed;
    }

    return current;
}

export function mergePassOnce(
        orderedSplines is array,
        segments is array,
        joins is array,
        posTol is ValueWithUnits,
        planeTol is ValueWithUnits,
        minLength is ValueWithUnits,
        numSamples is number) returns map
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
        const fit = fitLineOrArcForSegment(orderedSplines, unionSeg, posTol, planeTol, numSamples);

        if (isFitAcceptable(fit, posTol))
        {
            out = append(out, fit);
            changed = true;
            i += 2;
        }
        else
        {
            out = append(out, a);
            i += 1;
        }
    }

    return { "segments" : out, "changed" : changed };
}

function canAttemptMergeAcrossBoundary(a is map, b is map, joins is array) returns boolean
{
    if (a.curveIndex1 == b.curveIndex0)
    {
        if (a.curveIndex1 == a.curveIndex0 && b.curveIndex0 == b.curveIndex1)
        {
            return true;
        }

        const joinIndex = a.curveIndex1;
        if (joinIndex >= 0 && joinIndex < size(joins))
        {
            return !joins[joinIndex].isHard;
        }

        return false;
    }
    return false;
}

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
 * A fit is acceptable iff it has a known type and its max deviation is within posTol.
 * The maxErr is the REAL point-to-primitive distance (computed in fitLineOrArcForSegment),
 * not the chord error.
 */
function isFitAcceptable(fit is map, posTol is ValueWithUnits) returns boolean
{
    if (fit["type"] != "line" && fit["type"] != "arc")
    {
        return false;
    }
    if (fit.maxErr == undefined)
    {
        return false;
    }
    return fit.maxErr <= posTol;
}

/** --------------------------------------------------------------------------
 * Low-level evaluation helpers (real implementations using evaluateSpline)
 * -------------------------------------------------------------------------- */

/**
 * Evaluate a BSplineCurve at parameter u.
 */
function evalBSplineAtParam(c is map, u is number) returns Vector
{
    const dom = getSplineDomain(c);
    const uu = clamp(u, dom.uMin, dom.uMax);

    const result = evaluateSpline({
                "spline" : c,
                "parameters" : [uu],
                "nDerivatives" : 0
            });

    return result[0][0];
}

/**
 * Endpoint convenience: evaluates at the actual domain endpoints (knots[degree] / knots[n]),
 * not control points.
 */
function evalBSplineEndPoint(c is map, isStart is boolean) returns Vector
{
    const dom = getSplineDomain(c);
    const u = isStart ? dom.uMin : dom.uMax;
    return evalBSplineAtParam(c, u);
}

/**
 * Evaluate first-derivative tangent at start/end via evaluateSpline.
 */
function evalBSplineEndTangent(c is map, isStart is boolean) returns Vector
{
    const dom = getSplineDomain(c);
    const u = isStart ? dom.uMin : dom.uMax;

    const result = evaluateSpline({
                "spline" : c,
                "parameters" : [u],
                "nDerivatives" : 1
            });

    return result[1][0];
}

export function tangentMismatch(a is Vector, b is Vector, dotTol is number) returns boolean
{
    const na = norm(a);
    const nb = norm(b);
    if (na == 0 || nb == 0)
    {
        return true;
    }

    const ua = a / na;
    const ub = b / nb;

    return dot(ua, ub) < dotTol;
}

/**
 * Sample positions along a single BSplineCurve segment [u0, u1].
 */
function sampleSplineSegmentPositions(c is map, u0 is number, u1 is number, n is number) returns array
{
    var a = u0;
    var b = u1;
    if (b < a)
    {
        const tmp = a;
        a = b;
        b = tmp;
    }

    var nClamped = n;
    if (nClamped < 2)
    {
        nClamped = 2;
    }

    var params = [];
    for (var i = 0; i < nClamped; i += 1)
    {
        const t = i / (nClamped - 1);
        params = append(params, a + (b - a) * t);
    }

    const res = evaluateSpline({
                "spline" : c,
                "parameters" : params,
                "nDerivatives" : 0
            });

    return res[0];
}

/**
 * Max distance from points to the infinite chord line through p0->p1.
 */
function maxDistanceToChord(points is array, p0 is Vector, p1 is Vector) returns ValueWithUnits
{
    var d = p1 - p0;
    const L = norm(d);
    if (L == 0 * meter)
    {
        return 0 * meter;
    }
    const u = d / L;

    var maxErr = 0 * meter;
    for (var i = 0; i < size(points); i += 1)
    {
        const v = points[i] - p0;
        const proj = dot(v, u) * u;
        const perp = v - proj;
        const e = norm(perp);
        if (e > maxErr)
        {
            maxErr = e;
        }
    }
    return maxErr;
}

/**
 * Fit a circle through 3 points (if non-collinear).
 */
function circleThrough3Points(p0 is Vector, pm is Vector, p1 is Vector) returns map
{
    const a = pm - p0;
    const b = p1 - p0;
    const nRaw = cross(a, b);
    const nLen = norm(nRaw);
    if (nLen == 0 * meter * meter)
    {
        return undefined;
    }

    const n = nRaw / nLen;

    const uRaw = a;
    const uLen = norm(uRaw);
    if (uLen == 0 * meter)
    {
        return undefined;
    }
    const u = uRaw / uLen;
    const v = cross(n, u);

    const Bx = dot(a, u);
    const By = dot(a, v);
    const Cx = dot(b, u);
    const Cy = dot(b, v);

    const B2 = Bx * Bx + By * By;
    const C2 = Cx * Cx + Cy * Cy;

    const D = 2 * (Bx * Cy - By * Cx);

    const scale2 = B2 + C2;
    const eps2 = scale2 * 1e-12;
    if (abs(D) < eps2)
    {
        return undefined;
    }

    const Ux = (B2 * Cy - By * C2) / D;
    const Uy = (Bx * C2 - B2 * Cx) / D;

    const center = p0 + Ux * u + Uy * v;
    const R = norm(p0 - center);

    var xAxis = p0 - center;
    const xLen = norm(xAxis);
    if (xLen == 0 * meter)
    {
        return undefined;
    }
    xAxis = xAxis / xLen;

    const cs = { "origin" : center, "xAxis" : xAxis, "zAxis" : n };

    return { "circle" : { "coordSystem" : cs, "radius" : R }, "normal" : n };
}

/**
 * Angle of a point on the circle in the circle's coordSystem (radians as number).
 */
function circleAngle(circle is map, p is Vector) returns number
{
    const cs = circle.coordSystem;
    const ctr = cs.origin;
    const xa = cs.xAxis;
    const ya = cross(cs.zAxis, cs.xAxis);

    const v = p - ctr;
    const vx = dot(v, xa);
    const vy = dot(v, ya);

    return atan2(vy, vx) / radian;
}

/**
 * Return the 3D point on a circle at the given angle (radians as number).
 */
function arcPointAt(circle is map, theta is number) returns Vector
{
    const cs = circle.coordSystem;
    const ya = cross(cs.zAxis, cs.xAxis);
    const a = theta * radian;
    return cs.origin + circle.radius * (cos(a) * cs.xAxis + sin(a) * ya);
}

/**
 * True iff theta lies inside the swept arc range from theta0 to theta1.
 * The sweep direction is the sign of (theta1 - theta0). Handles |sweep| up to ~2 PI.
 */
function angleInArcRange(theta is number, theta0 is number, theta1 is number) returns boolean
{
    const d = theta1 - theta0;
    if (abs(d) >= 2 * PI - 1e-9)
    {
        return true;
    }

    var phi = theta - theta0;
    while (phi > 2 * PI)
    {
        phi -= 2 * PI;
    }
    while (phi < -2 * PI)
    {
        phi += 2 * PI;
    }

    if (d >= 0)
    {
        if (phi < 0)
        {
            phi += 2 * PI;
        }
        return phi <= d + 1e-9;
    }
    else
    {
        if (phi > 0)
        {
            phi -= 2 * PI;
        }
        return phi >= d - 1e-9;
    }
}

/**
 * Distance from a 3D point to a circular arc.
 *
 * If the point's projection onto the circle plane has angle inside [theta0, theta1],
 * distance = sqrt((radial - R)^2 + outOfPlane^2).
 * Otherwise, distance = min(distance to arc start, distance to arc end).
 */
export function pointToArcDistance3D(p is Vector, circle is map, theta0 is number, theta1 is number) returns ValueWithUnits
{
    const cs = circle.coordSystem;
    const ctr = cs.origin;
    const xa = cs.xAxis;
    const za = cs.zAxis;
    const ya = cross(za, xa);
    const R = circle.radius;

    const v = p - ctr;
    const outOfPlane = dot(v, za);
    const vx = dot(v, xa);
    const vy = dot(v, ya);
    const radial = sqrt(vx * vx + vy * vy);

    const theta = atan2(vy, vx) / radian;

    if (angleInArcRange(theta, theta0, theta1))
    {
        const radialErr = radial - R;
        return sqrt(radialErr * radialErr + outOfPlane * outOfPlane);
    }
    else
    {
        const pStart = arcPointAt(circle, theta0);
        const pEnd = arcPointAt(circle, theta1);
        const dStart = norm(p - pStart);
        const dEnd = norm(p - pEnd);
        return (dStart < dEnd) ? dStart : dEnd;
    }
}

/**
 * Compute max point-to-arc deviation across pre-sampled points and report the
 * source-curve parameter at which the max occurred (for split-point selection).
 */
function arcSamplesMaxError(pts is array, circle is map, theta0 is number, theta1 is number, u0 is number, u1 is number) returns map
{
    var maxErr = 0 * meter;
    var maxIdx = 0;
    const n = size(pts);
    for (var i = 0; i < n; i += 1)
    {
        const e = pointToArcDistance3D(pts[i], circle, theta0, theta1);
        if (e > maxErr)
        {
            maxErr = e;
            maxIdx = i;
        }
    }

    const t = (n > 1) ? (maxIdx / (n - 1)) : 0;
    const uMax = u0 + (u1 - u0) * t;
    return { "maxErr" : maxErr, "uMax" : uMax };
}
