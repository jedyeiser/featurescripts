FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

// IMPORT: fpt_math.fs (export import)
// IMPORT: fpt_analyze.fs (export import)
// IMPORT: fpt_constants.fs (export import)




/**
 * =============================================================================
 * FPT_ANALYZE.FS
 *
 * Core analysis functions for extracting dimensional and geometric data from
 * footprint curves. Refactored to work with BSplineCurve definitions instead
 * of edge queries, enabling use in editing logic.
 *
 * Main entry points:
 *   - prepareFootprintCurves() - converts edges to BSplines, filters Y>=0
 *   - analyzeFootprintCurves() - analyzes BSpline data
 *
 * Config parameters (passed via args.config):
 *   - tangentSolveTol : number (default 1e-9) - Tolerance for tangent.y = 0
 *   - paramBracketSamples : number (default 50) - Samples for initial bracketing
 *   - maxSolverIterations : number (default 30) - Max iterations for hybrid solver
 *   - xTolerance : ValueWithUnits (default 0.001mm) - Tolerance for X comparisons
 *   - yTolerance : ValueWithUnits (default 0.001mm) - Tolerance for Y=0 detection
 * =============================================================================
 */

/**
 * Build config map with defaults for any missing values.
 */
export function buildConfig(userConfig) returns map
{
    var config = (userConfig == undefined) ? {} : userConfig;

    return {
        "tangentSolveTol" : (config.tangentSolveTol == undefined) ? 1e-12 : config.tangentSolveTol,
        "paramBracketSamples" : (config.paramBracketSamples == undefined) ? 50 : config.paramBracketSamples,
        "maxSolverIterations" : (config.maxSolverIterations == undefined) ? 30 : config.maxSolverIterations,
        "xTolerance" : (config.xTolerance == undefined) ? (0.001 * millimeter) : config.xTolerance,
        "yTolerance" : (config.yTolerance == undefined) ? (0.001 * millimeter) : config.yTolerance
    };
}

// =============================================================================
// BSPLINE CONVERSION & PREPARATION (requires context - call once)
// =============================================================================

/**
 * Convert edge queries to BSplineCurve definitions.
 * This is one of the few functions that needs context.
 */
export function edgesToBSplines(context is Context, edges is Query, tolerance is ValueWithUnits) returns array
{
    var edgeArray = evaluateQuery(context, edges);
    var bsplines = [];

    for (var edge in edgeArray)
    {
        var bspline = evApproximateBSplineCurve(context, {
            "edge" : edge,
            "tolerance" : tolerance / meter  // tolerance param is unitless (meters)
        });
        bsplines = append(bsplines, bspline);
    }

    return bsplines;
}

/**
 * Get parameter range for a BSpline from its knot vector.
 */
export function getBSplineParamRange(bspline is BSplineCurve) returns map
{
    var knots = bspline.knots;
    return {
        "uMin" : knots[0],
        "uMax" : knots[size(knots) - 1]
    };
}

/**
 * Get bounding box of a BSpline by sampling.
 * Pure math - no context needed.
 */
export function getBSplineBounds(bspline is BSplineCurve) returns map
{
    var range = getBSplineParamRange(bspline);
    var uMin = range.uMin;
    var uMax = range.uMax;

    var numSamples = 25;
    var params = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        params = append(params, uMin + (uMax - uMin) * i / (numSamples - 1));
    }

    var result = evaluateSpline({ "spline" : bspline, "parameters" : params });
    var positions = result[0];

    var xMin = inf * meter;
    var xMax = -inf * meter;
    var yMin = inf * meter;
    var yMax = -inf * meter;

    for (var pt in positions)
    {
        xMin = min([xMin, pt[0]]);
        xMax = max([xMax, pt[0]]);
        yMin = min([yMin, pt[1]]);
        yMax = max([yMax, pt[1]]);
    }

    return {
        "xMin" : xMin,
        "xMax" : xMax,
        "yMin" : yMin,
        "yMax" : yMax
    };
}

/**
 * Find parameter(s) where BSpline crosses Y = yTarget.
 * Returns array of parameters (could be multiple crossings).
 */
export function findBSplineYCrossings(bspline is BSplineCurve, yTarget is ValueWithUnits, tolerance is ValueWithUnits) returns array
{
    var range = getBSplineParamRange(bspline);
    var uMin = range.uMin;
    var uMax = range.uMax;

    // Sample to find brackets
    var numSamples = 50;
    var params = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        params = append(params, uMin + (uMax - uMin) * i / (numSamples - 1));
    }

    var result = evaluateSpline({ "spline" : bspline, "parameters" : params });
    var positions = result[0];

    // Build samples for bracket finding
    var samples = [];
    for (var i = 0; i < size(params); i += 1)
    {
        samples = append(samples, {
            "u" : params[i],
            "f" : (positions[i][1] - yTarget) / meter  // unitless for solver
        });
    }

    // Find all sign changes and refine each
    var crossings = [];
    for (var i = 0; i < size(samples) - 1; i += 1)
    {
        if (samples[i].f * samples[i + 1].f < 0)
        {
            // Refine with solver
            var spline = bspline;  // capture for closure
            var target = yTarget;
            var f = function(u)
            {
                var pt = evaluateSpline({ "spline" : spline, "parameters" : [u] })[0][0];
                return (pt[1] - target) / meter;
            };

            var solveResult = solveRootHybrid(f, samples[i].u, samples[i + 1].u, tolerance / meter, 30);
            crossings = append(crossings, solveResult.u);
        }
    }

    return crossings;
}

/**
 * Extract a sub-curve from a BSpline between two parameters.
 * Creates a new BSpline by sampling and using interpolating spline.
 */
export function extractBSplineSubcurve(context is Context, bspline is BSplineCurve, uStart is number, uEnd is number, numPoints is number) returns BSplineCurve
{
    var params = [];
    for (var i = 0; i < numPoints; i += 1)
    {
        params = append(params, uStart + (uEnd - uStart) * i / (numPoints - 1));
    }

    var result = evaluateSpline({ "spline" : bspline, "parameters" : params });
    var positions = result[0];

    // Create new BSpline through these points
    return approximateSpline(context, {
            "degree" : 3,
            "tolerance" : 1e-5,
            "isPeriodic" : false,
            "targets" : [approximationTarget({ 'positions' : positions })]
    });
}

/**
 * Filter and trim BSplines to keep only Y >= 0 portions.
 * Pure math - no context needed.
 */
export function filterAndTrimBSplines(context is Context, bsplines is array, tolerance is ValueWithUnits) returns array
{
    var result = [];

    for (var bspline in bsplines)
    {
        var bounds = getBSplineBounds(bspline);

        // Case 1: Entirely below Y=0 - skip
        if (bounds.yMax < -tolerance)
        {
            continue;
        }

        // Case 2: Entirely above Y=0 - keep as-is
        if (bounds.yMin >= -tolerance)
        {
            result = append(result, bspline);
            continue;
        }

        // Case 3: Crosses Y=0 - need to trim
        var crossings = findBSplineYCrossings(bspline, 0 * meter, tolerance);

        if (size(crossings) == 0)
        {
            // No crossings found but bounds suggest it crosses - keep whole curve
            result = append(result, bspline);
            continue;
        }

        var range = getBSplineParamRange(bspline);
        var uMin = range.uMin;
        var uMax = range.uMax;

        // For each segment, check if it's above Y=0 and extract
        var boundaries = concatenateArrays([[uMin], crossings, [uMax]]);

        for (var i = 0; i < size(boundaries) - 1; i += 1)
        {
            var segStart = boundaries[i];
            var segEnd = boundaries[i + 1];
            var midU = (segStart + segEnd) / 2;

            // Check if midpoint is above Y=0
            var midPt = evaluateSpline({ "spline" : bspline, "parameters" : [midU] })[0][0];

            if (midPt[1] >= -tolerance)
            {
                // This segment is in Y >= 0 region - extract it
                var subCurve = extractBSplineSubcurve(context, bspline, segStart, segEnd, 30);
                result = append(result, subCurve);
            }
        }
    }

    return result;
}

/**
 * Build curve data array from BSplines with bounds.
 */
export function buildCurveDataArray(bsplines is array) returns array
{
    var curveData = [];

    for (var i = 0; i < size(bsplines); i += 1)
    {
        var bspline = bsplines[i];
        var bounds = getBSplineBounds(bspline);
        curveData = append(curveData, {
            "bspline" : bspline,
            "index" : i,
            "xMin" : bounds.xMin,
            "xMax" : bounds.xMax,
            "yMin" : bounds.yMin,
            "yMax" : bounds.yMax
        });
    }

    return curveData;
}

/**
 * Detect tip and tail points from curve data.
 * Tip/tail are where Y � 0 and X extends beyond FCP/ACP.
 */
export function detectTipTail(curveData is array, fcpX is ValueWithUnits, acpX is ValueWithUnits, tolerance is ValueWithUnits) returns map
{
    var fbIsNegativeX = fcpX < acpX;

    var minX = inf * meter;
    var maxX = -inf * meter;

    // Find X extent where Y � 0
    for (var cd in curveData)
    {
        var bspline = cd.bspline;
        var range = getBSplineParamRange(bspline);

        // Sample more densely to find Y � 0 points
        var params = [];
        for (var i = 0; i < 20; i += 1)
        {
            params = append(params, range.uMin + (range.uMax - range.uMin) * i / 19);
        }

        var positions = evaluateSpline({ "spline" : bspline, "parameters" : params })[0];

        for (var pt in positions)
        {
            if (abs(pt[1]) < tolerance)
            {
                minX = min([minX, pt[0]]);
                maxX = max([maxX, pt[0]]);
            }
        }
    }

    var tipX = undefined;
    var tailX = undefined;
    var tipLength = undefined;
    var tailLength = undefined;
    var hasTipTail = false;

    if (fbIsNegativeX)
    {
        // FB is negative X side, tip is at minX (if beyond FCP)
        if (minX < fcpX - tolerance)
        {
            tipX = minX;
            tipLength = abs(fcpX - tipX);
            hasTipTail = true;
        }
        // AB is positive X side, tail is at maxX (if beyond ACP)
        if (maxX > acpX + tolerance)
        {
            tailX = maxX;
            tailLength = abs(tailX - acpX);
            hasTipTail = true;
        }
    }
    else
    {
        // FB is positive X side, tip is at maxX (if beyond FCP)
        if (maxX > fcpX + tolerance)
        {
            tipX = maxX;
            tipLength = abs(tipX - fcpX);
            hasTipTail = true;
        }
        // AB is negative X side, tail is at minX (if beyond ACP)
        if (minX < acpX - tolerance)
        {
            tailX = minX;
            tailLength = abs(acpX - tailX);
            hasTipTail = true;
        }
    }

    return {
        "hasTipTail" : hasTipTail,
        "tipX" : tipX,
        "tailX" : tailX,
        "tipLength" : tipLength,
        "tailLength" : tailLength
    };
}

/**
 * Main preparation function - converts edges to analyzed BSpline data.
 * This is the ONLY entry point that needs context for curve work.
 */
export function prepareFootprintCurves(context is Context, edges is Query, fcpX is ValueWithUnits, acpX is ValueWithUnits, tolerance is ValueWithUnits) returns map
{
    // 1. Convert edges to BSplines (requires context)
    var bsplines = edgesToBSplines(context, edges, tolerance);

    // 2. Filter and trim for Y >= 0 (pure math)
    var filtered = filterAndTrimBSplines(context, bsplines, tolerance);

    // 3. Build curve data with bounds (pure math)
    var curveData = buildCurveDataArray(filtered);

    // 4. Detect tip/tail (pure math)
    var tipTail = detectTipTail(curveData, fcpX, acpX, tolerance);

    return {
        "curveData" : curveData,
        "hasTipTail" : tipTail.hasTipTail,
        "tipX" : tipTail.tipX,
        "tailX" : tipTail.tailX,
        "tipLength" : tipTail.tipLength,
        "tailLength" : tipTail.tailLength
    };
}

// =============================================================================
// BSPLINE SAMPLING UTILITIES (pure math - no context needed)
// =============================================================================

/**
 * Sample a BSpline uniformly by parameter.
 * Returns array of sample maps with u, point, tangent, x, y.
 */
export function sampleBSplineUniform(bspline is BSplineCurve, numSamples is number) returns array
{
    var range = getBSplineParamRange(bspline);
    var uMin = range.uMin;
    var uMax = range.uMax;

    var params = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        params = append(params, uMin + (uMax - uMin) * i / (numSamples - 1));
    }

    // Get positions and first derivatives
    var result = evaluateSpline({ "spline" : bspline, "parameters" : params, "nDerivatives" : 1 });
    var positions = result[0];
    var derivatives = result[1];

    var samples = [];
    for (var i = 0; i < size(params); i += 1)
    {
        var tangent = normalize(derivatives[i]);
        samples = append(samples, {
            "u" : params[i],
            "point" : positions[i],
            "tangent" : tangent,
            "x" : positions[i][0],
            "y" : positions[i][1]
        });
    }

    return samples;
}

/**
 * Compute signed curvature at a BSpline parameter.
 * Uses ? = (x'*y'' - y'*x'') / (x'� + y'�)^(3/2)
 * Sign: positive = concave (curving toward centerline/+Y), negative = convex
 */
export function getBSplineCurvatureAtParam(bspline is BSplineCurve, u is number) returns map
{
    var result = evaluateSpline({ "spline" : bspline, "parameters" : [u], "nDerivatives" : 2 });
    var point = result[0][0];
    var d1 = result[1][0];  // first derivative
    var d2 = result[2][0];  // second derivative

    var xP = d1[0] / meter;   // unitless
    var yP = d1[1] / meter;
    var xPP = d2[0] / meter;
    var yPP = d2[1] / meter;

    var speedSquared = xP * xP + yP * yP;
    var denom = speedSquared * sqrt(speedSquared);

    var kSigned;
    var kMag;
    var sgn;

    if (abs(denom) < 1e-15)
    {
        kSigned = 0 / meter;
        kMag = 0 / meter;
        sgn = 0;
    }
    else
    {
        var kValue = (xP * yPP - yP * xPP) / denom;  // unitless (1/meter in real terms)
        kSigned = kValue / meter;
        kMag = abs(kValue) / meter;
        sgn = safeSign(kValue, 1e-12);
    }

    var tangent = (sqrt(xP * xP + yP * yP) > 1e-12) ? normalize(d1) : vector(1, 0, 0);

    return {
        "point" : point,
        "tangent" : tangent,
        "curvatureMag" : kMag,
        "curvatureSigned" : kSigned,
        "sign" : sgn
    };
}

/**
 * Sample a BSpline with curvature data for inflection point detection.
 */
export function sampleBSplineWithCurvature(bspline is BSplineCurve, numSamples is number) returns array
{
    var range = getBSplineParamRange(bspline);
    var uMin = range.uMin;
    var uMax = range.uMax;

    var samples = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        var u = uMin + (uMax - uMin) * i / (numSamples - 1);
        var curv = getBSplineCurvatureAtParam(bspline, u);

        samples = append(samples, {
            "u" : u,
            "point" : curv.point,
            "tangent" : curv.tangent,
            "x" : curv.point[0],
            "y" : curv.point[1],
            "curvatureSigned" : curv.curvatureSigned,
            "curvatureMag" : curv.curvatureMag,
            "sign" : curv.sign
        });
    }

    return samples;
}

// =============================================================================
// CRITICAL POINT SOLVERS (pure math - no context needed)
// =============================================================================

/**
 * Find the widest point (maximum Y) on curve data within an X range.
 * Solves for where tangent.y = 0 (horizontal tangent at extremum).
 */
export function findWidestPoint(curveDataArray is array, xMin, xMax, config is map) returns map
{
    var numSamples = config.paramBracketSamples;
    var maxIter = config.maxSolverIterations;
    var tangentTol = config.tangentSolveTol;
    var xTol = config.xTolerance;

    if (size(curveDataArray) == 0)
        return { "found" : false };

    // Phase 1: Find approximate widest by sampling all curves
    var bestY = -inf * meter;
    var bestCurve = undefined;
    var bestU = 0;

    for (var cd in curveDataArray)
    {
        var samples = sampleBSplineUniform(cd.bspline, numSamples);

        for (var s in samples)
        {
            var inBounds = true;
            if (xMin != undefined && s.x < xMin - xTol)
                inBounds = false;
            if (xMax != undefined && s.x > xMax + xTol)
                inBounds = false;

            if (inBounds && s.y > bestY)
            {
                bestY = s.y;
                bestCurve = cd;
                bestU = s.u;
            }
        }
    }

    if (bestCurve == undefined)
        return { "found" : false };

    // Phase 2: Refine by solving tangent.y = 0
    var range = getBSplineParamRange(bestCurve.bspline);
    var uLo = max([range.uMin, bestU - 0.1 * (range.uMax - range.uMin)]);
    var uHi = min([range.uMax, bestU + 0.1 * (range.uMax - range.uMin)]);

    // Build refinement samples
    var params = [];
    var nRefine = 20;
    for (var i = 0; i < nRefine; i += 1)
    {
        params = append(params, uLo + (uHi - uLo) * i / (nRefine - 1));
    }

    var evalResult = evaluateSpline({ "spline" : bestCurve.bspline, "parameters" : params, "nDerivatives" : 1 });
    var positions = evalResult[0];
    var derivatives = evalResult[1];

    var samples = [];
    for (var i = 0; i < size(params); i += 1)
    {
        var inBounds = true;
        if (xMin != undefined && positions[i][0] < xMin - xTol)
            inBounds = false;
        if (xMax != undefined && positions[i][0] > xMax + xTol)
            inBounds = false;

        if (inBounds)
        {
            samples = append(samples, { "u" : params[i], "f" : derivatives[i][1] / meter });  // tangent.y
        }
    }

    if (size(samples) < 2)
    {
        var pt = evaluateSpline({ "spline" : bestCurve.bspline, "parameters" : [bestU] })[0][0];
        return {
            "found" : true,
            "point" : pt,
            "curveIndex" : bestCurve.index,
            "param" : bestU,
            "width" : pt[1],
            "x" : pt[0]
        };
    }

    var bracket = bracketFromSamples(samples);

    if (!bracket.found)
    {
        var pt = evaluateSpline({ "spline" : bestCurve.bspline, "parameters" : [bestU] })[0][0];
        return {
            "found" : true,
            "point" : pt,
            "curveIndex" : bestCurve.index,
            "param" : bestU,
            "width" : pt[1],
            "x" : pt[0]
        };
    }

    // Solver function
    var spline = bestCurve.bspline;
    var f = function(u)
    {
        var d = evaluateSpline({ "spline" : spline, "parameters" : [u], "nDerivatives" : 1 })[1][0];
        return d[1] / meter;  // tangent.y, unitless
    };

    var result = solveRootHybrid(f, bracket.a, bracket.b, tangentTol, maxIter);
    var finalU = result.u;
    var finalPt = evaluateSpline({ "spline" : bestCurve.bspline, "parameters" : [finalU] })[0][0];

    return {
        "found" : true,
        "point" : finalPt,
        "curveIndex" : bestCurve.index,
        "param" : finalU,
        "width" : finalPt[1],
        "x" : finalPt[0]
    };
}

/**
 * Find the waist point (minimum Y) between two X bounds.
 */
export function findWaistPoint(curveDataArray is array, xMin is ValueWithUnits, xMax is ValueWithUnits, config is map) returns map
{
    var numSamples = config.paramBracketSamples;
    var maxIter = config.maxSolverIterations;
    var tangentTol = config.tangentSolveTol;
    var xTol = config.xTolerance;

    if (size(curveDataArray) == 0)
        return { "found" : false };

    // Phase 1: Find approximate waist (min Y) by sampling
    var bestY = inf * meter;
    var bestCurve = undefined;
    var bestU = 0;

    for (var cd in curveDataArray)
    {
        if (cd.xMax < xMin - xTol || cd.xMin > xMax + xTol)
            continue;

        var samples = sampleBSplineUniform(cd.bspline, numSamples);

        for (var s in samples)
        {
            if (s.x < xMin - xTol || s.x > xMax + xTol)
                continue;

            if (s.y < bestY)
            {
                bestY = s.y;
                bestCurve = cd;
                bestU = s.u;
            }
        }
    }

    if (bestCurve == undefined)
        return { "found" : false };

    // Phase 2: Refine by solving tangent.y = 0
    var range = getBSplineParamRange(bestCurve.bspline);
    var uLo = max([range.uMin, bestU - 0.1 * (range.uMax - range.uMin)]);
    var uHi = min([range.uMax, bestU + 0.1 * (range.uMax - range.uMin)]);

    var params = [];
    var nRefine = 20;
    for (var i = 0; i < nRefine; i += 1)
    {
        params = append(params, uLo + (uHi - uLo) * i / (nRefine - 1));
    }

    var evalResult = evaluateSpline({ "spline" : bestCurve.bspline, "parameters" : params, "nDerivatives" : 1 });
    var positions = evalResult[0];
    var derivatives = evalResult[1];

    var samples = [];
    for (var i = 0; i < size(params); i += 1)
    {
        if (positions[i][0] < xMin - xTol || positions[i][0] > xMax + xTol)
            continue;

        samples = append(samples, { "u" : params[i], "f" : derivatives[i][1] / meter });
    }

    if (size(samples) < 2)
    {
        var pt = evaluateSpline({ "spline" : bestCurve.bspline, "parameters" : [bestU] })[0][0];
        return {
            "found" : true,
            "point" : pt,
            "curveIndex" : bestCurve.index,
            "param" : bestU,
            "width" : pt[1],
            "x" : pt[0]
        };
    }

    var bracket = bracketFromSamples(samples);

    if (!bracket.found)
    {
        var pt = evaluateSpline({ "spline" : bestCurve.bspline, "parameters" : [bestU] })[0][0];
        return {
            "found" : true,
            "point" : pt,
            "curveIndex" : bestCurve.index,
            "param" : bestU,
            "width" : pt[1],
            "x" : pt[0]
        };
    }

    var spline = bestCurve.bspline;
    var f = function(u)
    {
        var d = evaluateSpline({ "spline" : spline, "parameters" : [u], "nDerivatives" : 1 })[1][0];
        return d[1] / meter;
    };

    var result = solveRootHybrid(f, bracket.a, bracket.b, tangentTol, maxIter);
    var finalU = result.u;
    var finalPt = evaluateSpline({ "spline" : bestCurve.bspline, "parameters" : [finalU] })[0][0];

    return {
        "found" : true,
        "point" : finalPt,
        "curveIndex" : bestCurve.index,
        "param" : finalU,
        "width" : finalPt[1],
        "x" : finalPt[0]
    };
}

/**
 * Find inflection point (curvature sign change) between xInner and xOuter.
 * Checks both within-curve sign changes and between-curve junction sign changes.
 */
export function findInflectionPoint(curveDataArray is array, xInner is ValueWithUnits,
    xOuter is ValueWithUnits, searchFromOuter is boolean, config is map) returns map
{
    var numSamples = config.paramBracketSamples;
    var maxIter = config.maxSolverIterations;
    var xTol = config.xTolerance;

    var xMin = min([xInner, xOuter]);
    var xMax = max([xInner, xOuter]);

    // Search direction: +1 if xOuter > xInner (searching toward +X), -1 otherwise
    var searchDir = (xOuter > xInner) ? 1 : -1;

    // Filter to curves that overlap our search region
    var relevantCurves = [];
    for (var cd in curveDataArray)
    {
        if (cd.xMax < xMin - xTol || cd.xMin > xMax + xTol)
            continue;
        relevantCurves = append(relevantCurves, cd);
    }

    if (size(relevantCurves) == 0)
        return { "found" : false };

    // Sort curves in search direction (starting from xOuter)
    relevantCurves = sort(relevantCurves, function(a, b)
    {
        if (searchDir > 0)
            return b.xMax - a.xMax;  // Start from highest X, work down
        else
            return a.xMin - b.xMin;  // Start from lowest X, work up
    });

    // Track curvature from previous curve
    var prevSample = undefined;

    for (var cd in relevantCurves)
    {
        var samples = sampleBSplineWithCurvature(cd.bspline, numSamples);

        // Filter to in-bounds samples
        var inBoundsSamples = [];
        for (var s in samples)
        {
            if (s.x >= xMin - xTol && s.x <= xMax + xTol)
            {
                inBoundsSamples = append(inBoundsSamples, s);
            }
        }

        if (size(inBoundsSamples) == 0)
            continue;

        // Sort samples in search direction
        inBoundsSamples = sort(inBoundsSamples, function(a, b)
        {
            if (searchDir > 0)
                return b.x - a.x;  // Decreasing X (from xOuter toward xInner)
            else
                return a.x - b.x;  // Increasing X (from xOuter toward xInner)
        });

        // Check for sign change at JUNCTION with previous curve
        var firstSample = inBoundsSamples[0];
        if (prevSample != undefined &&
            prevSample.sign != 0 && firstSample.sign != 0 &&
            prevSample.sign != firstSample.sign)
        {
            // Inflection at junction - return the boundary point
            return {
                "found" : true,
                "point" : firstSample.point,
                "curveIndex" : cd.index,
                "param" : firstSample.u,
                "x" : firstSample.x,
                "y" : firstSample.point[1],
                "type" : "junction"
            };
        }

        // Check for sign change WITHIN this curve
        for (var i = 0; i < size(inBoundsSamples) - 1; i += 1)
        {
            var s1 = inBoundsSamples[i];
            var s2 = inBoundsSamples[i + 1];

            if (s1.sign != 0 && s2.sign != 0 && s1.sign != s2.sign)
            {
                // Refine with solver
                var spline = cd.bspline;
                var uLo = min([s1.u, s2.u]);
                var uHi = max([s1.u, s2.u]);

                var f = function(u)
                {
                    var curv = getBSplineCurvatureAtParam(spline, u);
                    return curv.curvatureSigned * meter;
                };

                var result = solveRootHybrid(f, uLo, uHi, 1e-9, maxIter);
                var finalU = result.u;
                var finalCurv = getBSplineCurvatureAtParam(cd.bspline, finalU);

                return {
                    "found" : true,
                    "point" : finalCurv.point,
                    "curveIndex" : cd.index,
                    "param" : finalU,
                    "x" : finalCurv.point[0],
                    "y" : finalCurv.point[1],
                    "type" : "within"
                };
            }
        }

        // Update prevSample to this curve's last in-bounds sample
        prevSample = inBoundsSamples[size(inBoundsSamples) - 1];
    }

    return { "found" : false };
}

// =============================================================================
// DERIVED CALCULATIONS
// =============================================================================

/**
 * Calculate the radius of a circle passing through three points (circumradius).
 */
export function arcThroughThreePoints(p1 is Vector, p2 is Vector, p3 is Vector) returns map
{
    var A = p1[0] * (p2[1] - p3[1]) - p1[1] * (p2[0] - p3[0]) + p2[0] * p3[1] - p3[0] * p2[1];
    var B = (p1[0]^2 + p1[1]^2) * (p3[1] - p2[1]) + (p2[0]^2 + p2[1]^2) * (p1[1] - p3[1]) + (p3[0]^2 + p3[1]^2) * (p2[1] - p1[1]);
    var C = (p1[0]^2 + p1[1]^2) * (p2[0] - p3[0]) + (p2[0]^2 + p2[1]^2) * (p3[0] - p1[0]) + (p3[0]^2 + p3[1]^2) * (p1[0] - p2[0]);
    var D = (p1[0]^2 + p1[1]^2) * (p3[0] * p2[1] - p2[0] * p3[1]) + (p2[0]^2 + p2[1]^2) * (p1[0] * p3[1] - p3[0] * p1[1]) + (p3[0]^2 + p3[1]^2) * (p2[0] * p1[1] - p1[0] * p2[1]);

    if (abs(A.value) < 1e-18)
    {
        return { "valid" : false, "center" : undefined, "R" : inf * meter };
    }

    var xC = -B / (2 * A);
    var yC = -C / (2 * A);
    var R = sqrt((B^2 + C^2 - 4 * A * D) / (4 * A^2));

    return { "valid" : true, "center" : vector(xC, yC, 0 * meter), "R" : R };
}

/**
 * Compute average radius in the sidecut region.
 * Only averages positive curvature (concave) sections.
 */
export function computeAverageRadius(curveDataArray is array, xMin is ValueWithUnits,
    xMax is ValueWithUnits, config is map) returns map
{
    var numSamples = config.paramBracketSamples;
    var xTol = config.xTolerance;

    var radiusSum = 0 * meter;
    var count = 0;

    for (var cd in curveDataArray)
    {
        if (cd.xMax < xMin - xTol || cd.xMin > xMax + xTol)
            continue;

        var samples = sampleBSplineWithCurvature(cd.bspline, numSamples);

        for (var s in samples)
        {
            if (s.x < xMin - xTol || s.x > xMax + xTol)
                continue;

            if (s.curvatureSigned > 0 / meter && s.curvatureMag > 1e-9 / meter)
            {
                radiusSum += 1 / s.curvatureMag;
                count += 1;
            }
        }
    }

    if (count == 0)
        return { "valid" : false, "avgRadius" : 0 * meter };

    return { "valid" : true, "avgRadius" : radiusSum / count };
}

/**
 * Compute taper angle between two points.
 * Returns the acute angle (0 to 90 degrees).
 */
export function computeTaperAngle(p1 is Vector, p2 is Vector) returns ValueWithUnits
{
    var deltaY = p1[1] - p2[1];
    var deltaX = p1[0] - p2[0];

    if (abs(deltaX.value) < 1e-12)
        return 0 * degree;

    // Use absolute values to get angle in 0-90 range
    var angle = atan2(abs(deltaY), abs(deltaX));

    // Ensure we have the minimum angle (acute angle)
    if (angle > 90 * degree)
        angle = 180 * degree - angle;

    return angle;
}

// =============================================================================
// MAIN ENTRY POINT
// =============================================================================

/**
 * Main analysis function for footprint curves.
 * Works with BSpline curve data (no context needed).
 *
 * @param args : map {
 *     curveData : array of curveData maps (from prepareFootprintCurves or buildCurveDataArray)
 *     fcpPoint : Vector - Forebody contact point
 *     acpPoint : Vector - Aftbody contact point
 *     mrsPoint : Vector - Midpoint of RSL line
 *     config : map (optional) - Configuration overrides
 * }
 *
 * @returns map with all analysis results
 */
export function analyzeFootprintCurves(context is Context, args is map) returns map
{
    var curveData = args.curveData;
    var fcpPoint = args.fcpPoint;
    var acpPoint = args.acpPoint;
    var mrsPoint = args.mrsPoint;

    var config = buildConfig(args.config);

    if (size(curveData) == 0)
    {
        throw regenError("No curve data provided to analyzeFootprintCurves");
    }

    // === Compute global bounds from curve data ===
    var globalXMin = inf * meter;
    var globalXMax = -inf * meter;

    for (var cd in curveData)
    {
        globalXMin = min([globalXMin, cd.xMin]);
        globalXMax = max([globalXMax, cd.xMax]);
    }

    // === Determine FB/AB orientation from FCP/ACP ===
    var mrsX = mrsPoint[0];
    var fcpX = fcpPoint[0];
    var fbSign = (fcpX < mrsX) ? -1 : 1;  // -1 means FB is on negative X side
    var abSign = -fbSign;

    // === STEP 1: Find waist (minimum Y) near MRS ===
    var searchMargin = (globalXMax - globalXMin) * 0.2;
    var waistSearchMin = mrsX - searchMargin;
    var waistSearchMax = mrsX + searchMargin;

    var waist = findWaistPoint(curveData, waistSearchMin, waistSearchMax, config);

    if (!waist.found)
    {
        // Fallback: search entire curve
        waist = findWaistPoint(curveData, globalXMin, globalXMax, config);
    }

    if (!waist.found)
    {
        throw regenError("Could not find waist point on footprint.");
    }

    // === STEP 2: Find widest points using waist.x as divider ===
    var fbWidest;
    if (fbSign < 0)
    {
        fbWidest = findWidestPoint(curveData, globalXMin, waist.x, config);
    }
    else
    {
        fbWidest = findWidestPoint(curveData, waist.x, globalXMax, config);
    }

    var abWidest;
    if (abSign < 0)
    {
        abWidest = findWidestPoint(curveData, globalXMin, waist.x, config);
    }
    else
    {
        abWidest = findWidestPoint(curveData, waist.x, globalXMax, config);
    }

    if (!fbWidest.found || !abWidest.found)
    {
        throw regenError("Could not find widest points. FB found: " ~ fbWidest.found ~ ", AB found: " ~ abWidest.found);
    }

    // === STEP 3: Find inflection points between waist and widest ===
    var fbInflection = findInflectionPoint(curveData, waist.x, fbWidest.x, true, config);
    var abInflection = findInflectionPoint(curveData, waist.x, abWidest.x, true, config);

    // Fallback: if no inflection found, use widest point
    if (!fbInflection.found)
    {
        fbInflection = {
            "found" : false,
            "point" : fbWidest.point,
            "curveIndex" : fbWidest.curveIndex,
            "param" : fbWidest.param,
            "x" : fbWidest.x,
            "y" : fbWidest.width
        };
    }

    if (!abInflection.found)
    {
        abInflection = {
            "found" : false,
            "point" : abWidest.point,
            "curveIndex" : abWidest.curveIndex,
            "param" : abWidest.param,
            "x" : abWidest.x,
            "y" : abWidest.width
        };
    }

    // === Compute Derived Values ===
    var natRadiusWidest = arcThroughThreePoints(fbWidest.point, waist.point, abWidest.point);
    var natRadiusInflection = arcThroughThreePoints(fbInflection.point, waist.point, abInflection.point);

    var avgRadiusResult = computeAverageRadius(curveData,
        min([fbInflection.x, abInflection.x]),
        max([fbInflection.x, abInflection.x]),
        config);

    var taperAngleWidest = computeTaperAngle(fbWidest.point, abWidest.point);
    var taperAngleInflection = computeTaperAngle(fbInflection.point, abInflection.point);

    // Dimension string (tip - waist - tail in mm, full width)
    var fbWidthMM = roundToPrecision(fbWidest.width / millimeter * 2, 1);
    var waistWidthMM = roundToPrecision(waist.width / millimeter * 2, 1);
    var abWidthMM = roundToPrecision(abWidest.width / millimeter * 2, 1);
    var dimensionStr = "" ~ fbWidthMM ~ " - " ~ waistWidthMM ~ " - " ~ abWidthMM;
    var radiusInfo = gatherRadiusInformation(context, curveData, fcpPoint, acpPoint);

    // === Build Result Map ===
    return {
        // Orientation
        "fbSign" : fbSign,
        "abSign" : abSign,
        "mrsX" : mrsX,

        // Critical points (full data)
        "fbWidestData" : fbWidest,
        "abWidestData" : abWidest,
        "waist" : waist,
        "fbInflectionData" : fbInflection,
        "abInflectionData" : abInflection,

        // Bounds
        "globalXMin" : globalXMin,
        "globalXMax" : globalXMax,
        "widestXMin" : min([fbWidest.x, abWidest.x]),
        "widestXMax" : max([fbWidest.x, abWidest.x]),
        "inflectionXMin" : min([fbInflection.x, abInflection.x]),
        "inflectionXMax" : max([fbInflection.x, abInflection.x]),

        // Derived measurements
        "dimensionStr" : dimensionStr,
        "foundWaistWidth" : waist.width * 2,
        "foundWaistLocation" : waist.x,
        "foundTaperAngle" : taperAngleWidest,
        "taperAngleInflection" : taperAngleInflection,

        // Natural radii
        "naturalRadiusWidest" : natRadiusWidest,
        "naturalRadiusInflection" : natRadiusInflection,
        "natRadiusWidestStr" : natRadiusWidest.valid ? ("" ~ roundToPrecision(natRadiusWidest.R / meter, 2) ~ " m") : "N/A",
        "natRadiusInflectionStr" : natRadiusInflection.valid ? ("" ~ roundToPrecision(natRadiusInflection.R / meter, 2) ~ " m") : "N/A",

        // Average radius
        "avgRadius" : avgRadiusResult.valid ? avgRadiusResult.avgRadius : (0 * meter),
        "avgRadiusStr" : avgRadiusResult.valid ? ("" ~ roundToPrecision(avgRadiusResult.avgRadius / meter, 2) ~ " m") : "N/A",

        // For predicate compatibility - distances from contact points
        "fbWidest" : abs(fbWidest.x - fcpPoint[0]),
        "abWidest" : abs(abWidest.x - acpPoint[0]),
        "fbInflection" : abs(fbInflection.x - fcpPoint[0]),
        "abInflection" : abs(abInflection.x - acpPoint[0]),
        "fbInflectionToWidest" : abs(fbWidest.x - fbInflection.x),
        "abInflectionToWidest" : abs(abWidest.x - abInflection.x),

        // Metadata
        "hasInflections" : fbInflection.found && abInflection.found,
        "gatheredRadiusInformation" : radiusInfo
    };

}

/**
 * Takes an array of curveData and contact points as input. Solves for radius
 * information between points of the curves in curveData.
 * @param context: Context
 * @param curveData: Array of curveData items
 *
 * @returns map: Map with points and edges as keys. All data will be returned in ascending x (lowest -> highest)
 *
 * edges is an array of bSplineCurves approximating the radius progression in this area. At this point, we have a 1:1 mm[y] to r[m] scale.
 * points is an array of points with each point -> [x, r(m) -> y(mm), z]
 */

export function gatherRadiusInformation(context is Context, curveData is array, fcp is Vector, acp is Vector) returns map
{
    var fcp_x = fcp[0];
    var acp_x = acp[0];

    var filteredCurveData = filterCurveData(curveData, fcp, acp);

    var points = [];
    var edges = [];

    for (var i = 0; i < size(filteredCurveData); i += 1)
    {
         var thisCurve = curveData[i];

         var bSpline = thisCurve.bspline;
         var minClip = thisCurve.xMin < min(fcp[0], acp[0]);
         var maxClip = thisCurve.xMax > max(fcp[0], acp[0]);


         var edgeEnds = evaluateSpline({
                 "spline" : bSpline,
                 "parameters" : [0, 1]
         });

         var standardDir = edgeEnds[0][0][0] < edgeEnds[0][1][0];

         var minParam = standardDir ? 0 : 1;
         var maxParam = standardDir ? 1 : 0;

         if (minClip || maxClip)
         {
             if (minClip)
             {
                 if (standardDir)
                 {
                     minParam = findParameterAtX(bSpline, min(fcp[0], acp[0]), 1e-5 * millimeter);
                 }
                 else
                 {
                     //println('trying to find ' ~ toString(min(fcp[0], acp[0])) ~ ' in a bspline with range from ' ~ toString(min(edgeEnds[0][0][0], edgeEnds[0][1][0])) ~ ' to ' ~ toString(max(edgeEnds[0][0][0], edgeEnds[0][1][0])));
                     maxParam = findParameterAtX(bSpline, min(fcp[0], acp[0]), 1e-5 * millimeter);
                 }
             }
             if (maxClip)
             {
                 if (standardDir)
                 {
                     maxParam = findParameterAtX(bSpline, max(fcp[0], acp[0]), 1e-5 * millimeter);
                 }
                 else
                 {
                     minParam = findParameterAtX(bSpline, max(fcp[0], acp[0]), 1e-5 * millimeter);
                 }
             }
         }

         var paramRange = standardDir ? range(minParam, maxParam, 20) : range(maxParam, minParam, 20);

         var edgePoints = evaluateSpline({
                 "spline" : bSpline,
                 "parameters" : paramRange
        })[0];

        points = concatenateArrays(points, edgePoints);

        var aprx = approximateSpline(context, {
                "degree" : 3,
                "tolerance" : 1e-5,
                "isPeriodic" : false,
                "targets" : [approximationTarget({ 'positions' : edgePoints })]
        });

        edges = append(edges, {'edgePoints': edgePoints, "bSpline" : aprx});

    }

    return {'edges': edges, 'points' : points};

}

/**
 * Find the parameter on a BSplineCurve where X equals a target value.
 * Samples the curve to bracket crossings, then refines with bisection.
 *
 * @param bspline is BSplineCurve - The curve to search
 * @param xTarget is ValueWithUnits - The X coordinate to find
 * @param tolerance is ValueWithUnits - How close the found X must be to xTarget
 * @returns number - The parameter u where curve X � xTarget, or undefined if not found
 */
export function findParameterAtX(bspline is BSplineCurve, xTarget is ValueWithUnits, tolerance is ValueWithUnits) returns number
{
    var knots = bspline.knots;
    var uMin = knots[0];
    var uMax = knots[size(knots) - 1];

    var endpoints = evaluateSpline({ "spline" : bspline, "parameters" : [uMin, uMax] })[0];

    if (abs(endpoints[0][0] - xTarget) < tolerance)
    {
        return uMin;
    }
    if (abs(endpoints[1][0] - xTarget) < tolerance)
    {
        return uMax;
    }

    // Sample to find bracket
    var numSamples = 50;
    var params = range(uMin, uMax, numSamples);

    var positions = evaluateSpline({ "spline" : bspline, "parameters" : params })[0];

    // Find first sign change in (X - xTarget)
    for (var i = 0; i < numSamples - 1; i += 1)
    {
        var f0 = positions[i][0] - xTarget;
        var f1 = positions[i + 1][0] - xTarget;

        if (abs(f0) < tolerance)
        {
            return params[i];
        }

        if (f0 * f1 <= 0)
        {
            // Bisection refinement
            var uLo = params[i];
            var uHi = params[i + 1];

            for (var iter = 0; iter < 50; iter += 1)
            {
                var uMid = (uLo + uHi) / 2;
                var pMid = evaluateSpline({ "spline" : bspline, "parameters" : [uMid] })[0][0];
                var fMid = pMid[0] - xTarget;

                if (abs(fMid) < tolerance)
                {
                    return uMid;
                }

                if (f0 * fMid < 0)
                {
                    uHi = uMid;
                }
                else
                {
                    uLo = uMid;
                    f0 = fMid;
                }
            }

            return (uLo + uHi) / 2;
        }
    }

    throw regenError("findParameterAtX: xTarget " ~ toString(xTarget) ~ " not found within curve bounds");
}

export function filterCurveData(curveData is array, fcp is Vector, acp is Vector) returns array
{
    var retArray = [];
    for (var i = 0; i < size(curveData); i += 1)
    {

        //println('fcp -> ' ~ toString(fcp) ~ '. acp -> ' ~ toString(acp));
        if ((curveData[i].xMax < min(fcp[0], acp[0])) || (curveData[i].xMin >max(fcp[0], acp[0])))
        {
            continue;   // outside points. Do nothing
        }

        retArray = append(retArray, curveData[i]);
    }
    return retArray;
}