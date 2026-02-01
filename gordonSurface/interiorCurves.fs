FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

//import tools/bspline_knots
import(path : "b1e8bfe71f67389ca210ed8b/fa0241a434caffbc394f0e00/dadb70c0a762573622fa609c", version : "acff88f740b64f7b03d722aa");
// import gordonCurveCompat
import(path : "b9e1608a507a242d87720d9b", version : "dba5bb1c924e9263b31c9b77");
// constEnums
export import(path : "050a4670bd42b2ca8da04540", version : "62f653b9c0d24817418e88e5");
//scaledCurve
import(path : "2dfee1d44e9bde0daba9d73e", version : "19db4dd577e5e91d45d21305");



annotation { "Feature Type Name" : "Interior curves", "Feature Type Description" : "Takes a set of U, V curves and creates interior curves" }
export const myFeature = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {

        annotation { "Name" : "U0", "Filter" : EntityType.EDGE}
        definition.u0 is Query;
        
        annotation { "Name" : "U1", "Filter" : EntityType.EDGE}
        definition.u1 is Query;
        
        annotation { "Name" : "V0", "Filter" : EntityType.EDGE}
        definition.v0 is Query;
        
        annotation { "Name" : "V1", "Filter" : EntityType.EDGE}
        definition.v1 is Query;
        
        annotation { "Name" : "Interior U cure count" }
        isInteger(definition.numIntU, interiorCurveCountBounds);
        
        annotation { "Name" : "Interior V cure count" }
        isInteger(definition.numIntV, interiorCurveCountBounds);
        
        annotation { "Name" : "Blend Mode", "Default": BlendMode.AUTO, "UIHint": UIHint.ALWAYS_HIDDEN }
        definition.blendMode is BlendMode;
        
        annotation { "Group Name" : "Debug & Details", "Collapsed By Default" : true }
        {
            
            annotation { "Name" : "Print BSplineCurve data", "Desription": "Prints bspline data for combined input groups and final curve", "Default": false }
            definition.printBsplines is boolean;
            
            if (definition.printBsplines)
            {
                annotation { "Name" : "Data Depth", "Description" : "Print metadata only, or all details for our Input and output curves" }
                definition.bsplineFormat is PrintFormat;
                   
            }
            
            annotation { "Group Name" : "Details", "Collapsed By Default" : true }
            {
                annotation { "Group Name" : "Output parameters", "Collapsed By Default" : true }
                {
                
                    annotation { "Name" : "Scaled tolerance", "Description": "Fit tolerance for output curve. Fits sampled data to within this tolerance. Onshape minimum of 1e-8 meter or 1e-5 millimeter"  }
                    isLength(definition.outputTol, FitToleranceBounds); 
                
                    annotation { "Name" : "Scaled curve degree", "Description": "Degree of scaled curve" }
                    isInteger(definition.outputDegree, curveDegreeBounds);
                }
                
                
            }
            
            
        }
        
        
    }
    {
        var u0_queries = evaluateQuery(context, qUnion([definition.u0]));
        var u1_queries = evaluateQuery(context, qUnion([definition.u1]));
        var v0_queries = evaluateQuery(context, qUnion([definition.v0]));
        var v1_queries = evaluateQuery(context, qUnion([definition.v1]));
        
        var u0_BSplineCurves = mapArray(u0_queries, function(x) {return evApproximateBSplineCurve(context, { "edge" : x}); });
        var u1_BSplineCurves = mapArray(u1_queries, function(x) {return evApproximateBSplineCurve(context, { "edge" : x}); });
        var v0_BSplineCurves = mapArray(v0_queries, function(x) {return evApproximateBSplineCurve(context, { "edge" : x}); });
        var v1_BSplineCurves = mapArray(v1_queries, function(x) {return evApproximateBSplineCurve(context, { "edge" : x}); });
        
        var resultMap = generateInteriorCurves(context, u0_BSplineCurves, u1_BSplineCurves, v0_BSplineCurves, v1_BSplineCurves, definition.numIntU, definition.numIntV, definition.blendMode);
        
        for (var i = 0; i < size(resultMap['interiorU']); i += 1)
        {
            opCreateBSplineCurve(context, id + ("interiorU_" ~ i), {
                    "bSplineCurve" : resultMap['interiorU'][i]
            });
        }
        
        for (var i = 0; i < size(resultMap['interiorV']); i += 1)
        {
            opCreateBSplineCurve(context, id + ("interiorV_" ~ i), {
                    "bSplineCurve" : resultMap['interiorV'][i]
            });
        }
        
        if (definition.printBsplines)
        {
            println(" * * * * * * Printing U Border BSplineCurves * * * * * * ");
            for (var i = 0; i < size(u0_BSplineCurves); i += 1)
            {
                printBSpline(u0_BSplineCurves[i], definition.bsplineFormat, ["<Boundary u0 " ~ i ~ ">"]);
            }
            for (var i = 0; i < size(u1_BSplineCurves); i += 1)
            {
                printBSpline(u1_BSplineCurves[i], definition.bsplineFormat, ["<Boundary u1 " ~ i ~ ">"]);
            }
            for (var i = 0; i < size(v0_BSplineCurves); i += 1)
            {
                printBSpline(v0_BSplineCurves[i], definition.bsplineFormat, ["<Boundary v0 " ~ i ~ " >"]);
            }
            for (var i = 0; i < size(v1_BSplineCurves); i += 1)
            {
                printBSpline(v1_BSplineCurves[i], definition.bsplineFormat, ["<Boundary v1 " ~ i ~ " >"]);
            }
             for (var i = 0; i < size(resultMap['interiorU']); i += 1)
            {
                printBSpline(resultMap['interiorU'][i], definition.bsplineFormat, ["<Interior u" ~ i ~ " >"]);
            }
            for (var i = 0; i < size(resultMap['interiorV']); i += 1)
            {
                printBSpline(resultMap['interiorV'][i], definition.bsplineFormat, ["<Interior v" ~ i ~ " >"]);
            }
        }
        
        
    });



/**
 * Generate interior curves for a 4-sided boundary.
 * Derives numSamples and tolerance internally.
 */
export function generateInteriorCurves(
    context is Context,
    u0_curves is array,
    u1_curves is array,
    v0_curves is array,
    v1_curves is array,
    numInteriorU is number,
    numInteriorV is number,
    blendMode is BlendMode
) returns map
{
    // Derive reasonable defaults
    var tolerance = 1e-6 * meter;  // Could make smarter based on bounding box
    var numSamples = estimateSampleCount([u0_curves, u1_curves, v0_curves, v1_curves]);
    
    // Join boundary segments
    var u0 = joinCurveSegments(context, u0_curves, numSamples, tolerance);
    var u1 = joinCurveSegments(context, u1_curves, numSamples, tolerance);
    var v0 = joinCurveSegments(context, v0_curves, numSamples, tolerance);
    var v1 = joinCurveSegments(context, v1_curves, numSamples, tolerance);
    
    var interiorU = [];
    var interiorV = [];
    
    if (blendMode == BlendMode.AUTO)
    {
        var result = generateIntersectingCurves(context, u0, u1, v0, v1, 
                                                  numInteriorU, numInteriorV, 
                                                  numSamples, tolerance);
        interiorU = result.interiorU;
        interiorV = result.interiorV;
    }
    else if (blendMode == BlendMode.LINEAR_BLEND)
    {

        interiorU = generateBlendedCurves(context, u0, u1, v0, v1, numInteriorU, numSamples, tolerance);
        interiorV = generateBlendedCurves(context, v0, v1, u0, u1, numInteriorV, numSamples, tolerance);
    }
    else if (blendMode == BlendMode.CROSS_SAMPLE)
    {
        interiorU = generateCrossSampledCurves(context, v0, v1, u0, u1, numInteriorU, numSamples, tolerance);
        interiorV = generateCrossSampledCurves(context, u0, u1, v0, v1, numInteriorV, numSamples, tolerance);
    }
    
    return {
        "interiorU" : interiorU,
        "interiorV" : interiorV,
        "u0" : u0,
        "u1" : u1,
        "v0" : v0,
        "v1" : v1
    };
}

/**
 * Estimate sample count based on curve complexity.
 */
function estimateSampleCount(curveArrays is array) returns number
{
    var maxCPs = 0;
    for (var curves in curveArrays)
    {
        for (var curve in curves)
        {
            var numCPs = size(curve.controlPoints);
            if (numCPs > maxCPs)
            {
                maxCPs = numCPs;
            }
        }
    }
    
    // At least 20, scale with complexity, cap at 100
    return min(max(maxCPs * 3, 20), 100);
}

/**
 * Simple linear blend between two curves.
 */
function generateBlendedCurves(
    context is Context,
    curve0 is BSplineCurve,
    curve1 is BSplineCurve,
    perpCurve0 is BSplineCurve,
    perpCurve1 is BSplineCurve,
    numInterior is number,
    numSamples is number,
    tolerance is ValueWithUnits
) returns array
{
    var result = [];
    
    for (var i = 1; i <= numInterior; i += 1)
    {
        var t = i / (numInterior + 1);
        
        // Endpoints come from perpendicular boundaries
        var startPt = evaluateSpline({ "spline" : perpCurve0, "parameters" : [t] })[0][0];
        var endPt = evaluateSpline({ "spline" : perpCurve1, "parameters" : [t] })[0][0];
        
        // Interior points are blends
        var points = [startPt];
        for (var j = 1; j < numSamples - 1; j += 1)
        {
            var s = j / (numSamples - 1);
            var pt0 = evaluateSpline({ "spline" : curve0, "parameters" : [s] })[0][0];
            var pt1 = evaluateSpline({ "spline" : curve1, "parameters" : [s] })[0][0];
            points = append(points, (1 - t) * pt0 + t * pt1);
        }
        points = append(points, endPt);
        
        // Build parameter array
        var params = [];
        for (var k = 0; k < size(points); k += 1)
        {
            params = append(params, k / (size(points) - 1));
        }
        
        // Fit curve through points
        var fitted = approximateSpline(context, {
            "degree" : 3,
            "tolerance" : tolerance,
            "isPeriodic" : false,
            "targets" : [approximationTarget({ "positions" : points })],
            "parameters" : params,
            "interpolateIndices" : [0, size(points) - 1]
        });
        
        result = append(result, fitted[0]);
    }
    
    return result;
}

/**
 * Generate interior curves with guaranteed intersections.
 * Uses a shared point grid.
 */
function generateIntersectingCurves(context is Context, u0 is BSplineCurve, u1 is BSplineCurve, v0 is BSplineCurve, v1 is BSplineCurve, numInteriorU is number, numInteriorV is number, numSamples is number, tolerance is ValueWithUnits) returns map
{
    // Normalize boundary orientation so corners match
    var normalized = normalizeBoundaryOrientation(context, u0, u1, v0, v1, tolerance);
    u0 = normalized.u0;
    u1 = normalized.u1;
    v0 = normalized.v0;
    v1 = normalized.v1;
    
    // Parameter arrays including boundaries
    var uParams = [0];
    for (var i = 1; i <= numInteriorV; i += 1)
    {
        uParams = append(uParams, i / (numInteriorV + 1));
    }
    uParams = append(uParams, 1);
    
    var vParams = [0];
    for (var i = 1; i <= numInteriorU; i += 1)
    {
        vParams = append(vParams, i / (numInteriorU + 1));
    }
    vParams = append(vParams, 1);
    
    // Build point grid using bilinear-ish interpolation from boundaries
    // grid[i][j] = point at (uParams[j], vParams[i])
    var numU = size(uParams);
    var numV = size(vParams);
    
    var grid = makeArray(numV);
    for (var i = 0; i < numV; i += 1)
    {
        grid[i] = makeArray(numU);
        var v = vParams[i];
        
        for (var j = 0; j < numU; j += 1)
        {
            var u = uParams[j];
            
            // Get boundary points
            var ptU0 = evaluateSpline({ "spline" : u0, "parameters" : [u] })[0][0];  // v=0
            var ptU1 = evaluateSpline({ "spline" : u1, "parameters" : [u] })[0][0];  // v=1
            var ptV0 = evaluateSpline({ "spline" : v0, "parameters" : [v] })[0][0];  // u=0
            var ptV1 = evaluateSpline({ "spline" : v1, "parameters" : [v] })[0][0];  // u=1
            
            // Coons-style bilinear blend
            var lerpU = (1 - v) * ptU0 + v * ptU1;
            var lerpV = (1 - u) * ptV0 + u * ptV1;
            
            // Corner points for correction
            var p00 = evaluateSpline({ "spline" : u0, "parameters" : [0] })[0][0];
            var p10 = evaluateSpline({ "spline" : u0, "parameters" : [1] })[0][0];
            var p01 = evaluateSpline({ "spline" : u1, "parameters" : [0] })[0][0];
            var p11 = evaluateSpline({ "spline" : u1, "parameters" : [1] })[0][0];
            
            var bilinear = (1 - u) * (1 - v) * p00 
                         + u * (1 - v) * p10 
                         + (1 - u) * v * p01 
                         + u * v * p11;
            
            // Coons formula: lerpU + lerpV - bilinear
            grid[i][j] = lerpU + lerpV - bilinear;
        }
    }
    
    // Build interior U curves (rows of grid, excluding boundaries)
    var interiorU = [];
    for (var i = 1; i < numV - 1; i += 1)
    {
        var rowPoints = grid[i];
        
        var densePoints = rowPoints;
   
        
        var params = [];
        for (var k = 0; k < size(densePoints); k += 1)
        {
            params = append(params, k / (size(densePoints) - 1));
        }
        
        var fitted = approximateSpline(context, {
            "degree" : 3,
            "tolerance" : tolerance,
            "isPeriodic" : false,
            "targets" : [approximationTarget({ "positions" : densePoints })],
            "parameters" : params,
            "interpolateIndices" : [0, size(densePoints) - 1]
        });
        
        interiorU = append(interiorU, fitted[0]);
    }
    
    // Build interior V curves (columns of grid, excluding boundaries)
    var interiorV = [];
    for (var j = 1; j < numU - 1; j += 1)
    {
        var colPoints = [];
        for (var i = 0; i < numV; i += 1)
        {
            colPoints = append(colPoints, grid[i][j]);
        }
        
        // var densePoints = densifyCurvePoints(colPoints, numSamples);
        var densePoints = colPoints;
        // need to implement catmull rom or hermite in a bit. 
        
        var params = [];
        for (var k = 0; k < size(densePoints); k += 1)
        {
            params = append(params, k / (size(densePoints) - 1));
        }
        
        var fitted = approximateSpline(context, {
            "degree" : 3,
            "tolerance" : tolerance,
            "isPeriodic" : false,
            "targets" : [approximationTarget({ "positions" : densePoints })],
            "parameters" : params,
            "interpolateIndices" : [0, size(densePoints) - 1]
        });
        
        interiorV = append(interiorV, fitted[0]);
    }
    
    return {
        "interiorU" : interiorU,
        "interiorV" : interiorV
    };
}


/**
 * Generate interior curves by cross-sampling between boundaries.
 * 
 * @param side0 {BSplineCurve} : First side boundary (e.g., u0 - runs in v direction)
 * @param side1 {BSplineCurve} : Second side boundary (e.g., u1 - runs in v direction)  
 * @param end0 {BSplineCurve} : First end boundary (e.g., v0 - runs in u direction)
 * @param end1 {BSplineCurve} : Second end boundary (e.g., v1 - runs in u direction)
 * @param numInterior {number} : How many interior curves to generate
 * @param numSamples {number} : How many points to sample along each curve
 * @param tolerance {ValueWithUnits} : Fitting tolerance
 */
function generateCrossSampledCurves(
    context is Context,
    side0 is map,    // v0 for U curves
    side1 is map,    // v1 for U curves
    end0 is map,     // u0 for U curves
    end1 is map,     // u1 for U curves
    numInterior is number,
    numSamples is number,
    tolerance is ValueWithUnits
) returns array
{
    // Get corner points once
    var p00 = evaluateSpline({ "spline" : end0, "parameters" : [0] })[0][0];
    var p10 = evaluateSpline({ "spline" : end0, "parameters" : [1] })[0][0];
    var p01 = evaluateSpline({ "spline" : end1, "parameters" : [0] })[0][0];
    var p11 = evaluateSpline({ "spline" : end1, "parameters" : [1] })[0][0];
    
    var interiorCurves = [];
    
    for (var i = 1; i <= numInterior; i += 1)
    {
        var t = i / (numInterior + 1);
        
        var samplePoints = [];
        
        for (var j = 0; j < numSamples; j += 1)
        {
            var s = j / (numSamples - 1);
            
            // Boundary evaluations
            var ptSide0 = evaluateSpline({ "spline" : side0, "parameters" : [t] })[0][0];  // v0(t)
            var ptSide1 = evaluateSpline({ "spline" : side1, "parameters" : [t] })[0][0];  // v1(t)
            var ptEnd0 = evaluateSpline({ "spline" : end0, "parameters" : [s] })[0][0];    // u0(s)
            var ptEnd1 = evaluateSpline({ "spline" : end1, "parameters" : [s] })[0][0];    // u1(s)
            
            // Coons blend
            var lerpSides = (1 - s) * ptSide0 + s * ptSide1;
            var lerpEnds = (1 - t) * ptEnd0 + t * ptEnd1;
            var bilinear = (1 - s) * (1 - t) * p00 
                         + s * (1 - t) * p10 
                         + (1 - s) * t * p01 
                         + s * t * p11;
            
            var pt = lerpSides + lerpEnds - bilinear;
            samplePoints = append(samplePoints, pt);
        }
        
        // Fit curve
        var params = [];
        for (var k = 0; k < size(samplePoints); k += 1)
        {
            params = append(params, k / (size(samplePoints) - 1));
        }
        
        var curve = approximateSpline(context, {
            "degree" : 3,
            "tolerance" : tolerance,
            "isPeriodic" : false,
            "targets" : [approximationTarget({ "positions" : samplePoints })],
            "parameters" : params,
            "interpolateIndices" : [0, size(samplePoints) - 1]
        })[0];
        
        interiorCurves = append(interiorCurves, curve);
    }
    
    return interiorCurves;
}

/**
 * Analyze boundary curves and return them in a consistent orientation.
 * Returns curves where:
 *   - u0(0) = v0(0) = p00
 *   - u0(1) = v1(0) = p10  
 *   - u1(0) = v0(1) = p01
 *   - u1(1) = v1(1) = p11
 */
function normalizeBoundaryOrientation(context is Context, u0 is BSplineCurve, u1 is BSplineCurve, v0 is BSplineCurve, v1 is BSplineCurve, tolerance is ValueWithUnits) returns map
{
    // Get all endpoints
    var u0_start = evaluateSpline({ "spline" : u0, "parameters" : [0] })[0][0];
    var u0_end = evaluateSpline({ "spline" : u0, "parameters" : [1] })[0][0];
    var u1_start = evaluateSpline({ "spline" : u1, "parameters" : [0] })[0][0];
    var u1_end = evaluateSpline({ "spline" : u1, "parameters" : [1] })[0][0];
    var v0_start = evaluateSpline({ "spline" : v0, "parameters" : [0] })[0][0];
    var v0_end = evaluateSpline({ "spline" : v0, "parameters" : [1] })[0][0];
    var v1_start = evaluateSpline({ "spline" : v1, "parameters" : [0] })[0][0];
    var v1_end = evaluateSpline({ "spline" : v1, "parameters" : [1] })[0][0];
    
    // We need to find which u-curve endpoint matches which v-curve endpoint
    // and potentially swap/reverse curves to make them consistent
    
    var result = {
        "u0" : u0,
        "u1" : u1,
        "v0" : v0,
        "v1" : v1,
        "swappedU" : false,
        "swappedV" : false,
        "reversedU0" : false,
        "reversedU1" : false,
        "reversedV0" : false,
        "reversedV1" : false
    };
    
    // Check if v0 connects u0 to u1 or needs to be swapped with v1
    // v0 should connect u0(0) to u1(0) (the "left" side)
    
    // First, figure out which u-curve is "v=0" and which is "v=1"
    // by checking which one shares endpoints with v0_start
    
    var tol = tolerance;
    
    // Does v0_start match any u0 endpoint?
    var v0_start_matches_u0_start = norm(v0_start - u0_start) < tol;
    var v0_start_matches_u0_end = norm(v0_start - u0_end) < tol;
    var v0_start_matches_u1_start = norm(v0_start - u1_start) < tol;
    var v0_start_matches_u1_end = norm(v0_start - u1_end) < tol;
    
    var v0_end_matches_u0_start = norm(v0_end - u0_start) < tol;
    var v0_end_matches_u0_end = norm(v0_end - u0_end) < tol;
    var v0_end_matches_u1_start = norm(v0_end - u1_start) < tol;
    var v0_end_matches_u1_end = norm(v0_end - u1_end) < tol;
    
    // Determine correct assignment:
    // We want: v0(0) on one u-curve, v0(1) on the other
    // Let's call the u-curve that v0(0) touches "uBottom" and v0(1) touches "uTop"
    
    var uBottom = undefined;
    var uTop = undefined;
    var needReverseV0 = false;
    var needReverseU0 = false;
    var needReverseU1 = false;
    
    if (v0_start_matches_u0_start || v0_start_matches_u0_end)
    {
        // v0(0) is on u0
        if (v0_end_matches_u1_start || v0_end_matches_u1_end)
        {
            // v0 connects u0 to u1
            uBottom = u0;
            uTop = u1;
            needReverseU0 = v0_start_matches_u0_end;  // Need u0(0) = v0(0)
            needReverseU1 = v0_end_matches_u1_end;    // Need u1(0) = v0(1)
        }
    }
    else if (v0_start_matches_u1_start || v0_start_matches_u1_end)
    {
        // v0(0) is on u1 - need to swap u0 and u1
        if (v0_end_matches_u0_start || v0_end_matches_u0_end)
        {
            uBottom = u1;
            uTop = u0;
            result.swappedU = true;
            needReverseU0 = v0_start_matches_u1_end;  // Need uBottom(0) = v0(0)
            needReverseU1 = v0_end_matches_u0_end;    // Need uTop(0) = v0(1)
        }
    }
    
    if (uBottom == undefined)
    {
        // v0 doesn't connect the u curves properly - maybe need to swap v0/v1
        // For now, report error
        throw regenError("Boundary curves don't form a closed loop at v0");
    }
    
    // Now check v1 similarly - it should connect uBottom(1) to uTop(1)
    var uBottom_end = needReverseU0 ? 
        evaluateSpline({ "spline" : uBottom, "parameters" : [0] })[0][0] :
        evaluateSpline({ "spline" : uBottom, "parameters" : [1] })[0][0];
    
    var v1_start_matches_uBottom_end = norm(v1_start - uBottom_end) < tol;
    var v1_end_matches_uBottom_end = norm(v1_end - uBottom_end) < tol;
    
    var needReverseV1 = v1_end_matches_uBottom_end;  // Want v1(0) at uBottom
    
    // Apply reversals by creating new curve definitions with flipped parameterization
    if (needReverseU0)
    {
        result.u0 = reverseCurve(uBottom);
        result.reversedU0 = true;
    }
    else
    {
        result.u0 = uBottom;
    }
    
    if (needReverseU1)
    {
        result.u1 = reverseCurve(uTop);
        result.reversedU1 = true;
    }
    else
    {
        result.u1 = uTop;
    }
    
    if (needReverseV1)
    {
        result.v1 = reverseCurve(v1);
        result.reversedV1 = true;
    }
    
    return result;
}

// Curve reversal now handled by tools/bspline_knots.fs
// Available function: reverseCurve(curve)