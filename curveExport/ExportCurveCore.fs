FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

// IMPORT: tools/arc_length.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/f88f68e9ff3cb3c30d4afffe", version : "561709ffbf7a138328bbffc4");
// IMPORT: tools/curve_operations.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/a7403d5f7f5a4fef8225b768", version : "8539ef748286f908313b6564");
// IMPORT: tools/bspline_data.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/b1c7f2116fb64e6b40bf53f4", version : "4fe0cca8e00a4cd812896a8c");
// IMPORT: tools/solvers.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/99e84dbe2a4e2350792fa693", version : "9e71a1ec81d7a22319fafe0e");
// IMPORT: tools/bspline_knots.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/dadb70c0a762573622fa609c", version : "2267a758e66498ac49f4601e");



// =============================================================================
// ENUMS
// =============================================================================

export enum AlongType
{
    annotation { "Name" : "Along Chain (Arc Length)" } CHAIN,
    annotation { "Name" : "Along Query Geometry" }     QUERY,
    annotation { "Name" : "Along World Axis" }         WORLD
}

export enum AlongAxis
{
    annotation { "Name" : "World X" } WORLD_X,
    annotation { "Name" : "World Y" } WORLD_Y,
    annotation { "Name" : "World Z" } WORLD_Z
}

export enum ExportUnits
{
    annotation { "Name" : "Millimeters" } MILLIMETER,
    annotation { "Name" : "Inches" }      INCH,
    annotation { "Name" : "Centimeters" } CENTIMETER
}


// =============================================================================
// BOUNDS CONSTANTS
// =============================================================================

export const NUM_POINTS_BOUNDS =
{
    (unitless) : [2, 20, 500]
} as IntegerBoundSpec;

export const SIG_FIGS_BOUNDS =
{
    (unitless) : [1, 4, 8]
} as IntegerBoundSpec;


// =============================================================================
// UTILITY FUNCTIONS
// =============================================================================

/**
 * Returns unit scale factor: converts meters to the requested unit.
 */
export function getUnitScaleFactor(units is ExportUnits) returns number
{
    if (units == ExportUnits.MILLIMETER)
    {
        return 1000;
    }
    else if (units == ExportUnits.INCH)
    {
        return 1 / 0.0254;
    }
    else
    {
        return 100; // CENTIMETER
    }
}

/**
 * Returns unit suffix string (with leading space), or "" if showUnits is false.
 */
export function getUnitSuffix(units is ExportUnits, showUnits is boolean) returns string
{
    if (!showUnits)
    {
        return "";
    }
    if (units == ExportUnits.MILLIMETER)
    {
        return " mm";
    }
    else if (units == ExportUnits.INCH)
    {
        return " in";
    }
    else
    {
        return " cm";
    }
}

/**
 * Round value to N significant figures.
 */
function roundToSigFigs(value is number, sigFigs is number) returns number
{
    if (abs(value) < 1e-15)
    {
        return 0;
    }
    var exp = sigFigs - floor(log10(abs(value)) + 1);
    var magnitude = 10 ^ exp;
    return round(value * magnitude) / magnitude;
}

/**
 * Format a ValueWithUnits coordinate as string using formatConfig.
 * formatConfig = { tableUnits, sigFigs, showUnits, addParameters, addSlopes }
 */
export function formatCoord(value is ValueWithUnits, formatConfig is map) returns string
{
    var scale = getUnitScaleFactor(formatConfig.tableUnits);
    var scaled = value.value * scale;
    var rounded = roundToSigFigs(scaled, formatConfig.sigFigs);
    return toString(rounded) ~ getUnitSuffix(formatConfig.tableUnits, formatConfig.showUnits);
}

/**
 * Format a dimensionless scalar as string (rounded to sigFigs significant figures).
 */
export function formatScalar(value is number, sigFigs is number) returns string
{
    var rounded = roundToSigFigs(value, sigFigs);
    return toString(rounded);
}


// =============================================================================
// CURVE REVERSAL HELPER
// =============================================================================

/**
 * Reverse the direction of a BSplineCurve by reversing control points
 * and flipping the knot vector (remapped to same [uMin, uMax] range).
 */
function reverseBSplineCurve(curve is BSplineCurve) returns BSplineCurve
{
    var knots = curve.knots;
    var cps = curve.controlPoints;
    var n = size(knots);
    var uMin = knots[0];
    var uMax = knots[n - 1];

    // Reverse control points
    var reversedCP = [];
    for (var i = size(cps) - 1; i >= 0; i -= 1)
    {
        reversedCP = append(reversedCP, cps[i]);
    }

    // Flip knots: new knot = uMin + uMax - knots[n-1-i], reversed order
    var reversedKnots = [];
    for (var i = n - 1; i >= 0; i -= 1)
    {
        reversedKnots = append(reversedKnots, uMin + uMax - knots[i]);
    }

    // Handle rational curves
    var reversedWeights = undefined;
    if (curve.isRational && curve.weights != undefined)
    {
        var wts = curve.weights;
        reversedWeights = [];
        for (var i = size(wts) - 1; i >= 0; i -= 1)
        {
            reversedWeights = append(reversedWeights, wts[i]);
        }
    }

    return {
        "degree"        : curve.degree,
        "isPeriodic"    : curve.isPeriodic,
        "controlPoints" : reversedCP,
        "knots"         : reversedKnots,
        "weights"       : reversedWeights,
        "isRational"    : curve.isRational,
        "dimension"     : curve.dimension
    } as BSplineCurve;
}


// =============================================================================
// CHAIN ASSEMBLY
// =============================================================================

/**
 * Evaluate edges and order them into a G0-continuous chain.
 *
 * Returns { curves: array[BSplineCurve], chainStart: Vector }
 */
export function collectAndOrderEdges(context is Context, edgeQuery is Query) returns map
{
    var edgeArray = evaluateQuery(context, edgeQuery);
    var curves = [];

    // Approximate each edge as BSplineCurve
    for (var edge in edgeArray)
    {
        var curve = evApproximateBSplineCurve(context, {
            "edge"      : edge,
            "tolerance" : 1e-5  // unitless (meters)
        });
        curves = append(curves, curve);
    }

    if (size(curves) == 0)
    {
        throw regenError("ExportCurve: No edges found in selection.");
    }

    if (size(curves) == 1)
    {
        var ep = getBSplineEndpoints(curves[0]);
        return { "curves" : curves, "chainStart" : ep.start };
    }

    // Greedily order into chain
    var connTolerance = 1e-6 * meter;

    // Pick the first curve as head; try to find a chain from it
    var pool = [];
    for (var i = 1; i < size(curves); i += 1)
    {
        pool = append(pool, curves[i]);
    }

    var orderedCurves = [curves[0]];

    while (size(pool) > 0)
    {
        var found = false;

        for (var pi = 0; pi < size(pool); pi += 1)
        {
            var candidate = pool[pi];
            var conn = checkEndpointConnection(orderedCurves[size(orderedCurves) - 1], candidate, connTolerance);

            if (conn.connected)
            {
                var connType = conn.connectionType;

                // We need A.end to connect to B.start for forward chain extension.
                // A_END_B_START: A.end == B.start → correct, no reversal needed.
                // A_END_B_END:   A.end == B.end   → reverse B so new B.start == A.end.
                // A_START_*:     connects to A.start, not A.end → not a valid forward extension.
                if (connType == "A_END_B_START")
                {
                    // Already correct orientation
                    orderedCurves = append(orderedCurves, candidate);
                }
                else if (connType == "A_END_B_END")
                {
                    // Reverse candidate so its new start == A.end
                    orderedCurves = append(orderedCurves, reverseBSplineCurve(candidate));
                }
                else
                {
                    // A_START_B_START or A_START_B_END: connects to A.start, not A.end.
                    // Not a valid forward extension of the chain — skip to next candidate.
                    continue;
                }

                // Remove from pool
                var newPool = [];
                for (var pj = 0; pj < size(pool); pj += 1)
                {
                    if (pj != pi)
                    {
                        newPool = append(newPool, pool[pj]);
                    }
                }
                pool = newPool;
                found = true;
                break;
            }
        }

        if (!found)
        {
            // Could not extend chain - try starting from the other end of orderedCurves[0]
            // Reverse the entire accumulated chain and try again
            if (size(orderedCurves) == 1)
            {
                // Only first curve so far - flip it and retry
                orderedCurves[0] = reverseBSplineCurve(orderedCurves[0]);

                var found2 = false;

                for (var pi2 = 0; pi2 < size(pool); pi2 += 1)
                {
                    var candidate2 = pool[pi2];
                    var conn2 = checkEndpointConnection(orderedCurves[0], candidate2, connTolerance);

                    if (conn2.connected)
                    {
                        var connType2 = conn2.connectionType;
                        if (connType2 == "A_END_B_START")
                        {
                            orderedCurves = append(orderedCurves, candidate2);
                        }
                        else if (connType2 == "A_END_B_END")
                        {
                            orderedCurves = append(orderedCurves, reverseBSplineCurve(candidate2));
                        }
                        else
                        {
                            continue;
                        }

                        var newPool2 = [];
                        for (var pj2 = 0; pj2 < size(pool); pj2 += 1)
                        {
                            if (pj2 != pi2)
                            {
                                newPool2 = append(newPool2, pool[pj2]);
                            }
                        }
                        pool = newPool2;
                        found2 = true;
                        break;
                    }
                }

                if (!found2)
                {
                    throw regenError("ExportCurve: Edges are not G0-connected. Could not build chain.");
                }
            }
            else
            {
                // Forward extension failed with 2+ curves in chain.
                // Try backward extension: prepend a pool curve to the chain's front.
                var foundBack = false;

                for (var pb = 0; pb < size(pool); pb += 1)
                {
                    var candidateBack = pool[pb];
                    var connBack = checkEndpointConnection(orderedCurves[0], candidateBack, connTolerance);

                    if (connBack.connected)
                    {
                        var connTypeBack = connBack.connectionType;

                        // We need a curve that touches orderedCurves[0].start.
                        // A_START_B_END:   chain.start == cand.end → prepend cand as-is.
                        // A_START_B_START: chain.start == cand.start → prepend reversed(cand).
                        // A_END_*:         connects to chain tail, not head — skip.
                        var toPrepend = candidateBack;
                        if (connTypeBack == "A_START_B_START")
                        {
                            toPrepend = reverseBSplineCurve(candidateBack);
                        }
                        else if (connTypeBack != "A_START_B_END")
                        {
                            continue;
                        }

                        // Prepend toPrepend to front of orderedCurves
                        var newOrderedBack = [toPrepend];
                        for (var kb = 0; kb < size(orderedCurves); kb += 1)
                        {
                            newOrderedBack = append(newOrderedBack, orderedCurves[kb]);
                        }
                        orderedCurves = newOrderedBack;

                        // Remove from pool
                        var newPoolBack = [];
                        for (var pjb = 0; pjb < size(pool); pjb += 1)
                        {
                            if (pjb != pb)
                            {
                                newPoolBack = append(newPoolBack, pool[pjb]);
                            }
                        }
                        pool = newPoolBack;
                        foundBack = true;
                        break;
                    }
                }

                if (!foundBack)
                {
                    throw regenError("ExportCurve: Edges are not G0-connected. Could not build chain.");
                }
            }
        }
    }

    var chainStart = getBSplineEndpoints(orderedCurves[0]).start;

    return {
        "curves"     : orderedCurves,
        "chainStart" : chainStart
    };
}

/**
 * Local C0 join: concatenates two BSplineCurves at their shared endpoint.
 * Fixes the off-by-one knot count bug in the shared joinCurves function.
 * Junction knot multiplicity = degree → C0 continuity.
 */
function joinCurvesC0(context is Context, curveA is BSplineCurve, curveB is BSplineCurve) returns BSplineCurve
{
    var tolerance = 1e-6 * meter;

    var endpointsA = getBSplineEndpoints(curveA);
    var endpointsB = getBSplineEndpoints(curveB);
    var gap = norm(endpointsA.end - endpointsB.start);
    if (gap > tolerance)
    {
        throw regenError("joinCurvesC0: Curves do not meet within tolerance. Gap = " ~ gap);
    }

    var compat = makeCurvesCompatible(context, curveA, curveB);
    var compatA = compat.curveA;
    var compatB = compat.curveB;
    var degree = compatA.degree;

    var rangeA = getBSplineParamRange(compatA);
    var rangeB = getBSplineParamRange(compatB);
    var paramShift = rangeA.uMax - rangeB.uMin;

    var knotsB = [];
    for (var knot in compatB.knots)
    {
        knotsB = append(knotsB, knot + paramShift);
    }

    // Concatenate control points (skip first of B — duplicate at junction)
    var joinedCP = [];
    for (var cp in compatA.controlPoints)
    {
        joinedCP = append(joinedCP, cp);
    }
    for (var i = 1; i < size(compatB.controlPoints); i += 1)
    {
        joinedCP = append(joinedCP, compatB.controlPoints[i]);
    }

    // Concatenate knots.
    // Drop A's last knot so junction multiplicity = degree (C0) not degree+1 (break).
    // Total = (nA + degree) + nB = nA + nB + degree = (nA + nB - 1) + degree + 1  ✓
    var joinedKnots = [];
    for (var i = 0; i < size(compatA.knots) - 1; i += 1)
    {
        joinedKnots = append(joinedKnots, compatA.knots[i]);
    }
    for (var i = degree + 1; i < size(knotsB); i += 1)
    {
        joinedKnots = append(joinedKnots, knotsB[i]);
    }

    // Weights (rational case)
    var joinedWeights = [];
    if (compatA.isRational && compatA.weights != undefined)
    {
        for (var w in compatA.weights)
        {
            joinedWeights = append(joinedWeights, w);
        }
        for (var i = 1; i < size(compatB.weights); i += 1)
        {
            joinedWeights = append(joinedWeights, compatB.weights[i]);
        }
    }

    return {
        "degree"        : degree,
        "isPeriodic"    : false,
        "controlPoints" : joinedCP,
        "knots"         : joinedKnots,
        "weights"       : joinedWeights,
        "isRational"    : compatA.isRational,
        "dimension"     : compatA.dimension
    } as BSplineCurve;
}

/**
 * Join an ordered array of BSplineCurves (each A.end must touch B.start)
 * into a single composite BSplineCurve via pairwise C0 join.
 */
export function assembleCurveChain(context is Context, orderedCurves is array) returns BSplineCurve
{
    if (size(orderedCurves) == 0)
    {
        throw regenError("assembleCurveChain: No curves to assemble.");
    }

    var result = orderedCurves[0];

    for (var i = 1; i < size(orderedCurves); i += 1)
    {
        result = joinCurvesC0(context, result, orderedCurves[i]);
    }

    return result;
}


// =============================================================================
// SAMPLING — CHAIN MODE
// =============================================================================

/**
 * Sample numPoints evenly-spaced (arc length) points along a chain curve.
 *
 * Returns array of sample maps:
 *   { point, param, arcLength, tangent?, normal? }
 */
export function sampleChain(chainCurve is BSplineCurve, numPoints is number, addSlopes is boolean) returns array
{
    var samplesResult = uniformArcLengthSamples(chainCurve, numPoints, {});
    var parameters  = samplesResult.parameters;
    var points      = samplesResult.points;
    var arcLengths  = samplesResult.arcLengths;
    var totalLength = samplesResult.totalLength;

    var samples = [];

    for (var i = 0; i < numPoints; i += 1)
    {
        var u  = parameters[i];
        var pt = points[i];
        var s  = arcLengths[i];

        var normalizedParam = (totalLength.value > 0) ? (s.value / totalLength.value) : 0;

        var sampleMap = {
            "point"     : pt,
            "param"     : normalizedParam,
            "arcLength" : s
        };

        if (addSlopes)
        {
            var evalResult = evaluateSpline({
                "spline"       : chainCurve,
                "parameters"   : [u],
                "nDerivatives" : 2
            });
            var velocity     = evalResult[1][0];
            var acceleration = evalResult[2][0];

            var tangent  = normalize(velocity);
            var crossVec = cross(velocity, acceleration);

            var binormal;
            if (norm(crossVec).value < 1e-10)
            {
                // Degenerate: curve is locally straight — pick arbitrary perpendicular
                var arb = vector(0, 0, 1);
                if (abs(dot(tangent, arb)) > 0.9)
                {
                    arb = vector(1, 0, 0);
                }
                binormal = normalize(cross(tangent, arb));
            }
            else
            {
                binormal = normalize(crossVec);
            }

            var normal = cross(binormal, tangent);

            sampleMap = {
                "point"     : pt,
                "param"     : normalizedParam,
                "arcLength" : s,
                "tangent"   : tangent,
                "normal"    : normal
            };
        }

        samples = append(samples, sampleMap);
    }

    return samples;
}


// =============================================================================
// SAMPLING — QUERY AND WORLD MODES
// =============================================================================

/**
 * Returns unitless world axis direction vector.
 */
export function getAxisDirection(axis is AlongAxis) returns Vector
{
    if (axis == AlongAxis.WORLD_X)
    {
        return vector(1, 0, 0);
    }
    else if (axis == AlongAxis.WORLD_Y)
    {
        return vector(0, 1, 0);
    }
    else
    {
        return vector(0, 0, 1);
    }
}

/**
 * Determine the slicing direction from a reference geometry query.
 * Edge → must be a Line; returns line.direction.
 * Face → must be a Plane; returns plane.normal.
 */
export function getQueryDirection(context is Context, geomQuery is Query) returns Vector
{
    // Filter to edges and faces separately
    var edgeEntities = evaluateQuery(context, qEntityFilter(geomQuery, EntityType.EDGE));
    var faceEntities = evaluateQuery(context, qEntityFilter(geomQuery, EntityType.FACE));

    if (size(edgeEntities) > 0)
    {
        var curveDef = evCurveDefinition(context, {
            "edge"                 : edgeEntities[0],
            "returnBSplinesAsOther" : true
        });
        if (curveDef is Line)
        {
            return curveDef.direction;
        }
        throw regenError("ExportCurve: Reference edge must be a straight line.");
    }
    else if (size(faceEntities) > 0)
    {
        var surfDef = evSurfaceDefinition(context, {
            "face" : faceEntities[0]
        });
        if (surfDef is Plane)
        {
            return surfDef.normal;
        }
        throw regenError("ExportCurve: Reference face must be planar.");
    }
    else
    {
        throw regenError("ExportCurve: Reference geometry must be a line edge or planar face.");
    }
}

/**
 * Find min and max projection of curves array onto direction vector.
 * Returns { minT: ValueWithUnits, maxT: ValueWithUnits }
 */
export function projectCurveBounds(curves is array, direction is Vector) returns map
{
    var numSamples = 20;
    var minT = undefined;
    var maxT = undefined;

    for (var curve in curves)
    {
        var range = getBSplineParamRange(curve);
        var uMin  = range.uMin;
        var uMax  = range.uMax;

        var params = [];
        for (var i = 0; i < numSamples; i += 1)
        {
            params = append(params, uMin + (uMax - uMin) * i / (numSamples - 1));
        }

        var evalResult = evaluateSpline({
            "spline"     : curve,
            "parameters" : params
        });
        var positions = evalResult[0];

        for (var pt in positions)
        {
            var t = dot(pt, direction);
            if (minT == undefined || t.value < minT.value)
            {
                minT = t;
            }
            if (maxT == undefined || t.value > maxT.value)
            {
                maxT = t;
            }
        }
    }

    return { "minT" : minT, "maxT" : maxT };
}

/**
 * Find intersections of a BSplineCurve with a plane defined by:
 *   dot(point, planeNormal) = planeD
 *
 * planeNormal is unitless; planeD has ValueWithUnits (meters).
 * Returns array of { param: number, point: Vector }
 */
export function intersectCurveWithPlane(curve is BSplineCurve, planeNormal is Vector, planeD) returns array
{
    var numSamples = 50;
    var range = getBSplineParamRange(curve);
    var uMin  = range.uMin;
    var uMax  = range.uMax;

    // Build uniform parameter samples
    var params = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        params = append(params, uMin + (uMax - uMin) * i / (numSamples - 1));
    }

    // Batch evaluate positions
    var evalResult = evaluateSpline({
        "spline"     : curve,
        "parameters" : params
    });
    var positions = evalResult[0];

    // Compute signed distance for each sample (in meters)
    var fValues = [];
    for (var pt in positions)
    {
        var fVal = dot(pt, planeNormal) - planeD;
        fValues = append(fValues, fVal);
    }

    // Find sign-change brackets and refine each root
    var intersections = [];

    for (var i = 0; i < numSamples - 1; i += 1)
    {
        var fa = fValues[i].value;
        var fb = fValues[i + 1].value;

        if (fa * fb < 0)
        {
            var uA = params[i];
            var uB = params[i + 1];

            // Define function for root solver (strip units to get number)
            var rootFunc = function(u)
            {
                var er = evaluateSpline({
                    "spline"     : curve,
                    "parameters" : [u]
                });
                var fv = dot(er[0][0], planeNormal) - planeD;
                return fv.value;
            };

            var rootResult = solveRootHybrid(rootFunc, uA, uB, 1e-9, 60);
            var uRoot = rootResult.u;

            var ptResult = evaluateSpline({
                "spline"     : curve,
                "parameters" : [uRoot]
            });
            var ptRoot = ptResult[0][0];

            intersections = append(intersections, { "param" : uRoot, "point" : ptRoot });
        }
        else if (abs(fa) < 1e-7)
        {
            // Near-zero at left endpoint — add it directly (skip if already added)
            if (i == 0 || fValues[i - 1].value * fa >= 0)
            {
                var ptResult = evaluateSpline({
                    "spline"     : curve,
                    "parameters" : [params[i]]
                });
                intersections = append(intersections, { "param" : params[i], "point" : ptResult[0][0] });
            }
        }
    }

    // Check the last sample — the loop only tests it as fb, never as fa
    var fLast = fValues[numSamples - 1].value;
    if (abs(fLast) < 1e-7)
    {
        var prevF = fValues[numSamples - 2].value;
        if (prevF * fLast >= 0)  // not already captured by a sign-change bracket
        {
            var ptResult = evaluateSpline({
                "spline"     : curve,
                "parameters" : [params[numSamples - 1]]
            });
            intersections = append(intersections, { "param" : params[numSamples - 1], "point" : ptResult[0][0] });
        }
    }

    return intersections;
}

/**
 * Sample curve intersections with N evenly-spaced planes along direction.
 * Planes cover the projection bounds of all curves.
 *
 * Returns array of sample maps (same format as sampleChain output).
 */
export function sampleByPlanes(context is Context, orderedCurves is array, direction is Vector,
                                numPoints is number, chainStart is Vector, addSlopes is boolean) returns array
{
    var bounds = projectCurveBounds(orderedCurves, direction);
    var minT   = bounds.minT;
    var maxT   = bounds.maxT;

    var samples = [];

    for (var i = 0; i < numPoints; i += 1)
    {
        var t = (numPoints == 1) ? 0.5 : (i / (numPoints - 1));
        var planeD = minT + t * (maxT - minT);

        // Collect all intersections across all curves
        var allIntersections = [];
        for (var curve in orderedCurves)
        {
            var inters = intersectCurveWithPlane(curve, direction, planeD);
            for (var inter in inters)
            {
                allIntersections = append(allIntersections, {
                    "param" : inter.param,
                    "point" : inter.point,
                    "curve" : curve
                });
            }
        }

        if (size(allIntersections) == 0)
        {
            println("ExportCurve WARNING: No intersection found for plane " ~ i ~ ". Skipping.");
            continue;
        }

        // Pick intersection nearest to chainStart
        var bestInter = allIntersections[0];
        var bestDist  = norm(bestInter.point - chainStart).value;

        for (var inter in allIntersections)
        {
            var d = norm(inter.point - chainStart).value;
            if (d < bestDist)
            {
                bestDist  = d;
                bestInter = inter;
            }
        }

        var u    = bestInter.param;
        var pt   = bestInter.point;
        var owningCurve = bestInter.curve;

        // Arc length in QUERY/WORLD mode: projection offset from minT
        var arcLength = planeD - minT;

        var sampleMap = {
            "point"     : pt,
            "param"     : t,
            "arcLength" : arcLength
        };

        if (addSlopes)
        {
            var evalResult = evaluateSpline({
                "spline"       : owningCurve,
                "parameters"   : [u],
                "nDerivatives" : 2
            });
            var velocity     = evalResult[1][0];
            var acceleration = evalResult[2][0];

            var tangent  = normalize(velocity);
            var crossVec = cross(velocity, acceleration);

            var binormal;
            if (norm(crossVec).value < 1e-10)
            {
                var arb = vector(0, 0, 1);
                if (abs(dot(tangent, arb)) > 0.9)
                {
                    arb = vector(1, 0, 0);
                }
                binormal = normalize(cross(tangent, arb));
            }
            else
            {
                binormal = normalize(crossVec);
            }

            var normal = cross(binormal, tangent);

            sampleMap = {
                "point"     : pt,
                "param"     : t,
                "arcLength" : arcLength,
                "tangent"   : tangent,
                "normal"    : normal
            };
        }

        samples = append(samples, sampleMap);
    }

    return samples;
}


// =============================================================================
// TABLE DATA BUILDER
// =============================================================================

/**
 * Build array of row maps from samples, ready for use with tableRow().
 *
 * formatConfig = { tableUnits, sigFigs, showUnits, addParameters, addSlopes }
 */
export function buildTableRows(samples is array, formatConfig is map) returns array
{
    var rows = [];
    var idx  = 1;

    for (var sample in samples)
    {
        var pt = sample.point;

        var xNum = pt[0].value * getUnitScaleFactor(formatConfig.tableUnits);
        var yNum = pt[1].value * getUnitScaleFactor(formatConfig.tableUnits);
        var zNum = pt[2].value * getUnitScaleFactor(formatConfig.tableUnits);

        var rowMap = {
            "n"   : toString(idx),
            "x"   : formatCoord(pt[0], formatConfig),
            "y"   : formatCoord(pt[1], formatConfig),
            "z"   : formatCoord(pt[2], formatConfig),
            "csv" : formatScalar(xNum, formatConfig.sigFigs) ~ ", " ~
                    formatScalar(yNum, formatConfig.sigFigs) ~ ", " ~
                    formatScalar(zNum, formatConfig.sigFigs)
        };

        if (formatConfig.addParameters)
        {
            rowMap["param"]     = formatScalar(sample.param, formatConfig.sigFigs);
            rowMap["arclength"] = formatCoord(sample.arcLength, formatConfig);
        }

        if (formatConfig.addSlopes && sample.tangent != undefined)
        {
            rowMap["tangent"] = "[" ~ formatScalar(sample.tangent[0], formatConfig.sigFigs) ~ ", " ~
                                       formatScalar(sample.tangent[1], formatConfig.sigFigs) ~ ", " ~
                                       formatScalar(sample.tangent[2], formatConfig.sigFigs) ~ "]";
            rowMap["normal"]  = "[" ~ formatScalar(sample.normal[0], formatConfig.sigFigs) ~ ", " ~
                                       formatScalar(sample.normal[1], formatConfig.sigFigs) ~ ", " ~
                                       formatScalar(sample.normal[2], formatConfig.sigFigs) ~ "]";
        }

        rows = append(rows, rowMap);
        idx  += 1;
    }

    return rows;
}
