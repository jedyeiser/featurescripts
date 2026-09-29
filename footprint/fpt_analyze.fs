FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

// IMPORT: tools/math_utils.fs (for safeSign)
import(path : "b1e8bfe71f67389ca210ed8b/82e98a4cc11d1d3bbe2adf53/280a24d76f52bdbf44cd941d", version : "43549bf2d5a2bb2e92fb44bd");

// IMPORT: tools/solvers.fs (for bracketFromSamples, solveRootHybrid)
import(path : "b1e8bfe71f67389ca210ed8b/82e98a4cc11d1d3bbe2adf53/99e84dbe2a4e2350792fa693", version : "91ebe2327e2b0654bb603e52");

// IMPORT: tools/bspline_data.fs (for getBSplineParamRange, getBSplineBounds)
import(path : "b1e8bfe71f67389ca210ed8b/82e98a4cc11d1d3bbe2adf53/b1c7f2116fb64e6b40bf53f4", version : "afe2c4279f26bf0b7e587d71");

//import fpt_geometry
import(path : "67c190b80e8b74dcee72e7ff", version : "d76dff5e3a45ad1f43439664");

// IMPORT: footprint_math.fs (for getBSplineCurvatureAtParam)
import(path : "d3ad341f5b87924b36b5aba8", version : "b67dd4ede072b1ec3f088772");







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
function buildConfig(userConfig) returns map
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
        // forceNonRational: evaluateSpline ignores weights, so an exact (rational) arc would be
        // evaluated off the circle (correction 39).
        var bspline = evApproximateBSplineCurve(context, {
            "edge" : edge,
            "tolerance" : tolerance / meter,  // tolerance param is unitless (meters)
            "forceNonRational" : true
        });
        bsplines = append(bsplines, bspline);
    }
    
    return bsplines;
}

/**
 * Exact curvature magnitude of each edge (same order as evaluateQuery): 1 / radius for a circle, 0 for a line,
 * undefined for anything else (its B-spline is exact enough, or the edge is a spline).
 */
export function edgeExactCurvatures(context is Context, edges is Query) returns array
{
    var out = [];
    for (var edge in evaluateQuery(context, edges))
    {
        var definition = evCurveDefinition(context, { "edge" : edge });
        var k = undefined;
        if (definition is Circle)
        {
            k = 1 / definition.radius;
        }
        else if (definition is Line)
        {
            k = 0 / meter;
        }
        out = append(out, k);
    }
    return out;
}

// NOTE: getBSplineParamRange() and getBSplineBounds() removed (Phase 2)
// Now using tools/bspline_data.fs versions

/**
 * Find parameter(s) where BSpline crosses Y = yTarget.
 * Returns array of parameters (could be multiple crossings).
 */
function findBSplineYCrossings(bspline is BSplineCurve, yTarget is ValueWithUnits, tolerance is ValueWithUnits) returns array
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

    // approximateSpline returns an array — take first element
    return approximateSpline(context, {
            "degree" : 3,
            "tolerance" : 1e-5,
            "isPeriodic" : false,
            "targets" : [approximationTarget({ 'positions' : positions })]
    })[0];
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
 * Tip/tail are where Y ≈ 0 and X extends beyond FCP/ACP.
 */
export function detectTipTail(curveData is array, fcpX is ValueWithUnits, acpX is ValueWithUnits, tolerance is ValueWithUnits) returns map
{
    var fbIsNegativeX = fcpX < acpX;
    
    var minX = inf * meter;
    var maxX = -inf * meter;
    
    // Find X extent where Y ≈ 0
    for (var cd in curveData)
    {
        var bspline = cd.bspline;
        var range = getBSplineParamRange(bspline);
        
        // Sample more densely to find Y ≈ 0 points
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
    // Exact curvature of analytic edges (arcs 1/R, lines 0), used by the average radius instead of the
    // curvature of their non-rational approximation.
    var exactCurvatures = edgeExactCurvatures(context, edges);

    // 2. Filter and trim for Y >= 0 (pure math), edge by edge so each piece keeps its edge's exact curvature
    var filtered = [];
    var filteredCurvatures = [];
    for (var i = 0; i < size(bsplines); i += 1)
    {
        var pieces = filterAndTrimBSplines(context, [bsplines[i]], tolerance);
        filtered = concatenateArrays([filtered, pieces]);
        for (var j = 0; j < size(pieces); j += 1)
        {
            filteredCurvatures = append(filteredCurvatures, exactCurvatures[i]);
        }
    }

    // 3. Build curve data with bounds (pure math)
    var curveData = buildCurveDataArray(filtered);
    for (var i = 0; i < size(curveData); i += 1)
    {
        curveData[i].exactCurvature = filteredCurvatures[i];
    }

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
function sampleBSplineUniform(bspline is BSplineCurve, numSamples is number) returns array
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
 * Sample a BSpline with curvature data for inflection point detection.
 * NOTE: Uses getBSplineCurvatureAtParam() from footprint_math.fs
 */
function sampleBSplineWithCurvature(bspline is BSplineCurve, numSamples is number) returns array
{
    var range = getBSplineParamRange(bspline);
    var uMin = range.uMin;
    var uMax = range.uMax;
    
    var samples = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        var u = uMin + (uMax - uMin) * i / (numSamples - 1);
        var curv = getBSplineCurvatureAtParam(bspline, u);
        // Signs are taken walking toward +X, whatever the edge's own direction: sketch arcs are always
        // counter-clockwise, so an arc-to-reverse-arc inflection between two raw sketch edges was invisible
        // to the junction test (footprint test AF7, 2026-09-25).
        var dirSign = curv.tangent[0] < 0 ? -1 : 1;

        samples = append(samples, {
            "u" : u,
            "point" : curv.point,
            "tangent" : curv.tangent,
            "x" : curv.point[0],
            "y" : curv.point[1],
            "curvatureSigned" : curv.curvatureSigned * dirSign,
            "curvatureMag" : curv.curvatureMag,
            "sign" : curv.sign * dirSign
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

        // Update prevSample to this curve's last in-bounds sample with a curvature sign; a straight edge
        // (sign 0 throughout) keeps the previous one, so arc - line - reverse arc is still an inflection.
        for (var k = size(inBoundsSamples) - 1; k >= 0; k -= 1)
        {
            if (inBoundsSamples[k].sign != 0)
            {
                prevSample = inBoundsSamples[k];
                break;
            }
        }
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

// -----------------------------------------------------------------------------------------------------------
// Sidecut radii. The definitions are beamBuilder's (eocProductData backend/beam_builder/api/analysis.py:
// _arc_length_weighted_radius and _natural_radius; user decision 2026-09-28), evaluated exactly on the curves
// rather than on beamBuilder's 900 samples. Every radius published here is a positive magnitude.
// -----------------------------------------------------------------------------------------------------------

// beamBuilder FLAT_CURVATURE = 1e-7 / mm: where |curvature| is at or below this (radius 10 km or more) the
// curve is straight and takes no part in the average radius, neither its radius nor its length.
export const SIDECUT_FLAT_CURVATURE = 1e-4 / meter;

// Samples per knot span used only to bracket where x reaches the bounds and where |k| crosses the threshold.
const AVG_RADIUS_SAMPLES_PER_SPAN = 6;
// Adaptive Gauss-Legendre: a panel is accepted when it agrees with its two halves to this relative tolerance.
const AVG_RADIUS_REL_TOL = 1e-9;
const AVG_RADIUS_MAX_DEPTH = 40;
// 7-point Gauss-Legendre rule on [-1, 1]
const GL7_NODES = [-0.9491079123427585, -0.7415311855993945, -0.4058451513773972, 0,
    0.4058451513773972, 0.7415311855993945, 0.9491079123427585];
const GL7_WEIGHTS = [0.1294849661688697, 0.2797053914892766, 0.3818300505051189, 0.4179591836734694,
    0.3818300505051189, 0.2797053914892766, 0.1294849661688697];

/**
 * Average radius of the sidecut between x = xMin and x = xMax (the inflection points): the ARC-LENGTH
 * weighted mean of the radius of curvature,
 *     avgRadius = integral(1 / |k| ds) / integral(ds),
 * both integrals over the parts of the curves with xMin <= x <= xMax and |k| > SIDECUT_FLAT_CURVATURE
 * (beamBuilder _arc_length_weighted_radius: straight samples are left out of both sums). An exact arc uses
 * 1 / R, an exact line is straight; any other curve uses its B-spline curvature.
 *
 * Evaluated by adaptive Gauss-Legendre quadrature on every knot span, split where x reaches xMin / xMax and
 * where |k| crosses the threshold (bisection), so the value depends neither on sampling nor on how the sidecut
 * is split into edges. Next to a SMOOTH inflection 1 / |k| grows without bound and the integral is finite
 * only because of the threshold (a logarithmic end contribution) -- the same cut-off beamBuilder applies.
 */
export function computeAverageRadius(curveDataArray is array, xMin is ValueWithUnits,
    xMax is ValueWithUnits, config is map) returns map
{
    if (xMax <= xMin)
    {
        return { "valid" : false, "avgRadius" : 0 * meter };
    }
    const flat = SIDECUT_FLAT_CURVATURE * meter;
    var num = 0;
    var den = 0;
    for (var cd in curveDataArray)
    {
        var exactR = undefined;
        if (cd.exactCurvature is ValueWithUnits)
        {
            const kExact = cd.exactCurvature * meter;
            if (kExact <= flat)
            {
                continue; // an exact line, or an arc of 10 km or more: straight
            }
            exactR = 1 / kExact;
        }
        const sums = sidecutRadiusIntegrals(cd.bspline, xMin / meter, xMax / meter, exactR, flat);
        num += sums[0];
        den += sums[1];
    }
    if (den <= 0)
    {
        return { "valid" : false, "avgRadius" : 0 * meter };
    }
    return { "valid" : true, "avgRadius" : num / den * meter };
}

/**
 * [integral(R ds), integral(ds)] (m^2, m) over the part of one curve with lo <= x <= hi (m) and |k| > flat
 * (1/m). exactR (m) replaces the B-spline curvature when the edge is an exact arc.
 */
function sidecutRadiusIntegrals(bspline is BSplineCurve, lo is number, hi is number, exactR, flat is number) returns array
{
    const range = getBSplineParamRange(bspline);
    const eps = 1e-12 * (range.uMax - range.uMin);
    var breaks = [range.uMin];
    for (var kn in bspline.knots)
    {
        if (kn > breaks[size(breaks) - 1] + eps && kn < range.uMax - eps)
        {
            breaks = append(breaks, kn);
        }
    }
    breaks = append(breaks, range.uMax);

    // Bracketing samples: every knot plus AVG_RADIUS_SAMPLES_PER_SPAN - 1 points inside each span.
    var params = [];
    for (var i = 0; i < size(breaks) - 1; i += 1)
    {
        for (var j = 0; j < AVG_RADIUS_SAMPLES_PER_SPAN; j += 1)
        {
            params = append(params, breaks[i] + (breaks[i + 1] - breaks[i]) * j / AVG_RADIUS_SAMPLES_PER_SPAN);
        }
    }
    params = append(params, range.uMax);
    const values = sidecutBoundaryValues(bspline, params, lo, hi, flat);

    // Cut every sample interval where x - lo, x - hi or |k| - flat changes sign.
    var cuts = [];
    for (var i = 0; i < size(params) - 1; i += 1)
    {
        var here = [params[i]];
        for (var f = 0; f < 3; f += 1)
        {
            if (f == 2 && exactR != undefined)
            {
                continue;
            }
            const f0 = values[i][f];
            const f1 = values[i + 1][f];
            if (f0 * f1 < 0)
            {
                here = append(here, bisectSidecutBoundary(bspline, lo, hi, flat, f, params[i], params[i + 1], f0));
            }
        }
        cuts = concatenateArrays([cuts, sort(here, function(a, b) { return a - b; })]);
    }
    cuts = append(cuts, range.uMax);

    // Keep the pieces whose middle is inside [lo, hi] and curved; integrate each one.
    var pieces = [];
    var mids = [];
    for (var i = 0; i < size(cuts) - 1; i += 1)
    {
        if (cuts[i + 1] > cuts[i])
        {
            pieces = append(pieces, [cuts[i], cuts[i + 1]]);
            mids = append(mids, (cuts[i] + cuts[i + 1]) / 2);
        }
    }
    if (size(pieces) == 0)
    {
        return [0, 0];
    }
    const midValues = sidecutBoundaryValues(bspline, mids, lo, hi, flat);
    var num = 0;
    var den = 0;
    for (var i = 0; i < size(pieces); i += 1)
    {
        const v = midValues[i];
        if (v[0] >= 0 && v[1] <= 0 && (exactR != undefined || v[2] > 0))
        {
            const sums = adaptiveRadiusIntegrals(bspline, pieces[i][0], pieces[i][1], exactR, flat, 0);
            num += sums[0];
            den += sums[1];
        }
    }
    return [num, den];
}

/**
 * [x - lo, x - hi, |k| - flat] (m, m, 1/m) at each parameter.
 */
function sidecutBoundaryValues(bspline is BSplineCurve, params is array, lo is number, hi is number, flat is number) returns array
{
    const d = evaluateSpline({ "spline" : bspline, "parameters" : params, "nDerivatives" : 2 });
    var out = [];
    for (var i = 0; i < size(params); i += 1)
    {
        const x = d[0][i][0] / meter;
        out = append(out, [x - lo, x - hi, planarCurvatureMagnitude(d[1][i], d[2][i]) - flat]);
    }
    return out;
}

/**
 * |x'y'' - y'x''| / (x'^2 + y'^2)^(3/2) in 1/m from first and second derivative vectors (length units); 0 where
 * the parametrisation stalls (as getBSplineCurvatureAtParam).
 */
function planarCurvatureMagnitude(d1 is Vector, d2 is Vector) returns number
{
    const xP = d1[0] / meter;
    const yP = d1[1] / meter;
    const speedSquared = xP * xP + yP * yP;
    const denom = speedSquared * sqrt(speedSquared);
    if (denom < 1e-15)
    {
        return 0;
    }
    return abs(xP * (d2[1] / meter) - yP * (d2[0] / meter)) / denom;
}

/**
 * Root in [a, b] of boundary value `which` (0: x - lo, 1: x - hi, 2: |k| - flat), by bisection to the parameter
 * resolution (fa = the value at a; the value changes sign over [a, b]).
 */
function bisectSidecutBoundary(bspline is BSplineCurve, lo is number, hi is number, flat is number, which is number,
    a is number, b is number, fa is number) returns number
{
    var uLo = a;
    var uHi = b;
    var fLo = fa;
    for (var i = 0; i < 64; i += 1)
    {
        const mid = (uLo + uHi) / 2;
        if (mid <= uLo || mid >= uHi)
        {
            break;
        }
        const fm = sidecutBoundaryValues(bspline, [mid], lo, hi, flat)[0][which];
        if (fm == 0)
        {
            return mid;
        }
        if ((fm > 0) == (fLo > 0))
        {
            uLo = mid;
            fLo = fm;
        }
        else
        {
            uHi = mid;
        }
    }
    return (uLo + uHi) / 2;
}

/**
 * [integral(R ds), integral(ds)] over [a, b] (inside one knot span): 7-point Gauss-Legendre on the panel and
 * on its two halves; accept the halves when both integrals agree to AVG_RADIUS_REL_TOL, else recurse.
 */
function adaptiveRadiusIntegrals(bspline is BSplineCurve, a is number, b is number, exactR, flat is number, depth is number) returns array
{
    const m = (a + b) / 2;
    const panels = [[a, b], [a, m], [m, b]];
    var params = [];
    for (var p in panels)
    {
        for (var t in GL7_NODES)
        {
            params = append(params, (p[0] + p[1]) / 2 + (p[1] - p[0]) / 2 * t);
        }
    }
    const d = evaluateSpline({ "spline" : bspline, "parameters" : params, "nDerivatives" : 1 + (exactR == undefined ? 1 : 0) });
    var sums = [[0, 0], [0, 0], [0, 0]];
    for (var pk = 0; pk < 3; pk += 1)
    {
        const half = (panels[pk][1] - panels[pk][0]) / 2;
        for (var j = 0; j < 7; j += 1)
        {
            const i = pk * 7 + j;
            const xP = d[1][i][0] / meter;
            const yP = d[1][i][1] / meter;
            const speed = sqrt(xP * xP + yP * yP);
            var radius = exactR;
            if (radius == undefined)
            {
                const k = planarCurvatureMagnitude(d[1][i], d[2][i]);
                radius = k > flat ? 1 / k : 0;
            }
            if (radius > 0)
            {
                sums[pk][0] += GL7_WEIGHTS[j] * half * radius * speed;
                sums[pk][1] += GL7_WEIGHTS[j] * half * speed;
            }
        }
    }
    const numH = sums[1][0] + sums[2][0];
    const denH = sums[1][1] + sums[2][1];
    if (depth >= AVG_RADIUS_MAX_DEPTH ||
        (abs(numH - sums[0][0]) <= AVG_RADIUS_REL_TOL * abs(numH) && abs(denH - sums[0][1]) <= AVG_RADIUS_REL_TOL * abs(denH)))
    {
        return [numH, denH];
    }
    const left = adaptiveRadiusIntegrals(bspline, a, m, exactR, flat, depth + 1);
    const right = adaptiveRadiusIntegrals(bspline, m, b, exactR, flat, depth + 1);
    return [left[0] + right[0], left[1] + right[1]];
}

/**
 * Natural radius: the arc through the two stations p1, p2 (the widest pair, or the inflection pair) that is
 * TANGENT to the waist line y = waistHalfWidth -- not a circumradius through three points (beamBuilder
 * _natural_radius, reproduced exactly, in mm with its absolute thresholds). With a_i = |y_i| - w and the centre
 * at (cx, w + R), each station gives (x_i - cx)^2 + a_i^2 = 2 a_i R; eliminating R leaves
 *     (a2 - a1) cx^2 - 2 (a2 x1 - a1 x2) cx + (a2 x1^2 - a1 x2^2) - a1 a2 (a2 - a1) = 0,
 * linear when a1 == a2 (the symmetric ski: the centre on the perpendicular bisector); the quadratic's roots are
 * computed in the cancellation-free form (the same roots, in the same order). Root choice as beamBuilder:
 * the first valid root, replaced by the second only when that one's centre lies within one station spacing of the
 * stations and its radius is smaller. Invalid (R = inf) when a station is not wider than the waist or the solve
 * degenerates. Returns { valid, center (on the +Y side), R }.
 */
export function naturalRadiusTangentToWaist(p1 is Vector, p2 is Vector, waistHalfWidth is ValueWithUnits) returns map
{
    const invalid = { "valid" : false, "center" : undefined, "R" : inf * meter };
    const w = abs(waistHalfWidth / millimeter);
    const x1 = p1[0] / millimeter;
    const x2 = p2[0] / millimeter;
    const a1 = abs(p1[1] / millimeter) - w;
    const a2 = abs(p2[1] / millimeter) - w;
    if (a1 <= 1e-9 || a2 <= 1e-9 || abs(x2 - x1) < 1e-9)
    {
        return invalid;
    }
    const A = a2 - a1;
    const B = -2 * (a2 * x1 - a1 * x2);
    const C = (a2 * x1 * x1 - a1 * x2 * x2) - a1 * a2 * (a2 - a1);
    var candidates = [];
    if (abs(A) < 1e-12)
    {
        if (abs(B) < 1e-12)
        {
            return invalid;
        }
        candidates = [-C / B];
    }
    else
    {
        const disc = B * B - 4 * A * C;
        if (disc < 0)
        {
            return invalid;
        }
        // The roots (-B + root) / 2A and (-B - root) / 2A, in that order, without the cancellation that the
        // textbook form suffers when A is tiny (a near-symmetric ski away from x = 0: 1e-11 mm of width
        // difference at x = 16 m moved the radius by 4 mm): q = -(B + sign(B) root) / 2, roots q / A and C / q.
        const root = sqrt(disc);
        const q = B >= 0 ? -(B + root) / 2 : -(B - root) / 2;
        if (q == 0)
        {
            candidates = [(-B + root) / (2 * A), (-B - root) / (2 * A)];
        }
        else if (B >= 0)
        {
            candidates = [C / q, q / A];
        }
        else
        {
            candidates = [q / A, C / q];
        }
    }
    var bestR = undefined;
    var bestCx = undefined;
    for (var cx in candidates)
    {
        const r = ((x1 - cx) * (x1 - cx) + a1 * a1) / (2 * a1);
        if (r <= 0 || r == inf)
        {
            continue;
        }
        const inside = min(x1, x2) - abs(x2 - x1) <= cx && cx <= max(x1, x2) + abs(x2 - x1);
        if (bestR == undefined || (inside && r < bestR))
        {
            bestR = r;
            bestCx = cx;
        }
    }
    if (bestR == undefined)
    {
        return invalid;
    }
    return { "valid" : true, "center" : vector(bestCx, w + bestR, 0) * millimeter, "R" : bestR * millimeter };
}

/**
 * Compute taper angle between two points.
 * p1 should be the FB (FCP-side) widest point; p2 the AB (ACP-side) widest point.
 * Returns a signed angle: positive = p1 (FB) wider, negative = p2 (AB) wider.
 * deltaX uses abs() so the sign comes only from the width difference (deltaY).
 */
export function computeTaperAngle(p1 is Vector, p2 is Vector) returns ValueWithUnits
{
    var deltaY = p1[1] - p2[1];
    var deltaX = abs(p1[0] - p2[0]);

    if (deltaX / meter < 1e-12)
        return 0 * degree;

    return atan2(deltaY, deltaX);
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
    // Natural radii: the arc through each pair of stations tangent to the waist line (beamBuilder definition).
    var natRadiusWidest = naturalRadiusTangentToWaist(fbWidest.point, abWidest.point, waist.width);
    var natRadiusInflection = naturalRadiusTangentToWaist(fbInflection.point, abInflection.point, waist.width);

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
         // Bug fix: was curveData[i], must be filteredCurveData[i]
         var thisCurve = filteredCurveData[i];

         var bSpline = thisCurve.bspline;
         var minClip = thisCurve.xMin < min(fcp[0], acp[0]);
         var maxClip = thisCurve.xMax > max(fcp[0], acp[0]);

         // Bug fix: use actual param range instead of hardcoded [0, 1]
         var paramBounds = getBSplineParamRange(bSpline);
         var edgeEnds = evaluateSpline({
                 "spline" : bSpline,
                 "parameters" : [paramBounds.uMin, paramBounds.uMax]
         });

         var standardDir = edgeEnds[0][0][0] < edgeEnds[0][1][0];

         var minParam = standardDir ? paramBounds.uMin : paramBounds.uMax;
         var maxParam = standardDir ? paramBounds.uMax : paramBounds.uMin;

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

        // Bug fix: approximateSpline returns array, take [0]
        var aprx = approximateSpline(context, {
                "degree" : 3,
                "tolerance" : 1e-5,
                "isPeriodic" : false,
                "targets" : [approximationTarget({ 'positions' : edgePoints })]
        })[0];

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
 * @returns number - The parameter u where curve X ≈ xTarget, or undefined if not found
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

        if ((curveData[i].xMax < min(fcp[0], acp[0])) || (curveData[i].xMin >max(fcp[0], acp[0])))
        {
            continue;   // outside points. Do nothing
        }
        
        retArray = append(retArray, curveData[i]);
    }
    return retArray;
}

