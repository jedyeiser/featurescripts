FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

import(path : "b9e1608a507a242d87720d9b", version : "22c8651e5de4b01c4c98b37e");
import(path : "07dbc6069778f180fe33c140", version : "659c032b4a4a9949527be840");
import(path : "8d3469cb403ed076f2e3fbea", version : "c948bae217af15aa2557cdf4");
export import(path : "050a4670bd42b2ca8da04540", version : "310acbe540c302e20097f554");
import(path : "2dfee1d44e9bde0daba9d73e", version : "9923f9f7501f4858f23eec99");
import(path : "c6dca62049572faaa07ddd10", version : "5c2e8aec9a6fdd4f6604f668");
import(path : "3f40c735a406f3df927e0b13", version : "47415011a74c4d5ebd9d85db");

IconNamespace::import(path : "909b727cd95720b1666cbb41/4b9520a15e1873da9d46f579/583e87bcdf8f05533d82507e", version : "0e19bbdbe54691e6038ca94e");
ImageNamespace::import(path : "7c8c021bcbe65b0c83ce64f7", version : "9775783495730507203b8c88");



annotation { "Feature Type Name" : "Gordon Surface", "Icon" : IconNamespace::BLOB_DATA, "Description Image" : ImageNamespace::BLOB_DATA, "Feature Type Description" : "Create a Gordon Surface (supplied interior U and V Curves)" }
export const gordonSurface = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "U-Curves", "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 10, "UIHint": UIHint.ALLOW_QUERY_ORDER }
        definition.uCurves is Query;
        
        annotation { "Name" : "V-Curves", "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 10, "UIHint": UIHint.ALLOW_QUERY_ORDER }
        definition.vCurves is Query;
        
        annotation { "Group Name" : "Debug and details", "Collapsed By Default" : true }
        {
            annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
            {
                annotation { "Name" : "Create S_u", "Default": false }
                definition.create_s_u is boolean;
                
                annotation { "Name" : "Create S_v", "Default": false }
                definition.create_s_v is boolean;
                
                annotation { "Name" : "Create tensor surf", "Default": false }
                definition.create_s_t is boolean;
                
                annotation { "Name" : "Show curves", "Default": true }
                definition.showCurves is boolean;
                
                annotation { "Name" : "Print input curve data?", "Default" : false }
                definition.printInputCurves is boolean;
                
                if (definition.printInputCurves)
                {
                    annotation { "Name" : "Curve format", "Default": PrintFormat.METADATA }
                    definition.inputCurveFormat is PrintFormat;
                    
                }
                
                annotation { "Name" : "print S_u" }
                definition.print_S_u is boolean;
                
                annotation { "Name" : "print S_v" }
                definition.print_S_v is boolean;
                
                annotation { "Name" : "print_tensor" }
                definition.print_tensor is boolean;
                
                if (definition.print_S_u || definition.print_S_v || definition.print_tensor)
                {
                    annotation { "Name" : "Surface format", "Default": PrintFormat.METADATA }
                    definition.surfaceFormat is PrintFormat;
                    
                }
                
                
                
            }
            
            annotation { "Group Name" : "Details", "Collapsed By Default" : true }
            {
                annotation { "Name" : "u_degree" }
                isInteger(definition.uDegree, SurfDegreeBounds);
                
                annotation { "Name" : "v_degree" }
                isInteger(definition.vDegree, SurfDegreeBounds);
                
                annotation { "Name" : "Sample factor", "Default": 4 }
                isInteger(definition.sampleFactor, POSITIVE_COUNT_BOUNDS);
                
                annotation { "Name" : "Sampled spline tol." }
                isLength(definition.splineTol, TOLERANCE_BOUND);
            }
            
        }
        
        
    }
    {
        // Get ordered edge arrays from queries
        var uEdges = evaluateQuery(context, definition.uCurves);
        var vEdges = evaluateQuery(context, definition.vCurves);
        
        if (definition.showCurves)
        {
            addDebugEntities(context, definition.uCurves, DebugColor.CYAN);
            addDebugEntities(context, definition.vCurves, DebugColor.MAGENTA);
        }
        
        // Convert edges to B-splines
        var uBSplines = [];
        for (var i = 0; i < size(uEdges); i += 1)
        {
            var bSplineRep = evApproximateBSplineCurve(context, {
                    "edge" : uEdges[i]
            });
            uBSplines = append(uBSplines, bSplineRep);
            for (var j = 0; j < size(uBSplines); j += 1)
           {
               var endPt = evaluateSpline({ "spline" : uBSplines[j], "parameters" : [1.0] })[0][0];
               println("U-curve " ~ j ~ " endpoint: " ~ endPt);
           }
        }
        
        var vBSplines = [];
        for (var i = 0; i < size(vEdges); i += 1)
        {
            var bSplineRep = evApproximateBSplineCurve(context, {
                    "edge" : vEdges[i]
            });
            vBSplines = append(vBSplines, bSplineRep);
            for (var j = 0; j < size(vBSplines); j += 1)
           {
               var endPt = evaluateSpline({ "spline" : vBSplines[j], "parameters" : [1.0] })[0][0];
               println("V-curve " ~ j ~ " endpoint: " ~ endPt);
           }
        }
        
        // Make curves compatible (within each family)
        uBSplines = makeCurvesCompatible(context, id + "makeUcurvesCompatible", uBSplines);
        vBSplines = makeCurvesCompatible(context, id + "makeVcurvesCompatible", vBSplines);
        
        for (var i = 0; i < size(uBSplines); i += 1)
       {
           var lastCP = uBSplines[i].controlPoints[size(uBSplines[i].controlPoints) - 1];
           println("U-curve " ~ i ~ " last CP: " ~ lastCP);
       }
       for (var i = 0; i < size(vBSplines); i += 1)
       {
           var lastCP = vBSplines[i].controlPoints[size(vBSplines[i].controlPoints) - 1];
           println("V-curve " ~ i ~ " last CP: " ~ lastCP);
       }    
        
        // Compute intersection grid and parameters
        var intersectionData = computeIntersectionGrid(context, uBSplines, vBSplines);
        
        // Check if curve arrays need to be reversed based on parameter directions
        const MIN_PARAM_SPACING = 2e-6;
        var vParamResult = sanitizeParams(intersectionData.vParams, MIN_PARAM_SPACING);
        var uParamResult = sanitizeParams(intersectionData.uParams, MIN_PARAM_SPACING);
        
        var needRecompute = false;
        
        if (vParamResult.reversed)
        {
            println("Note: Reversing uBSplines array to match parameter direction");
            uBSplines = reverse(uBSplines);
            needRecompute = true;
        }
        if (uParamResult.reversed)
        {
            println("Note: Reversing vBSplines array to match parameter direction");
            vBSplines = reverse(vBSplines);
            needRecompute = true;
        }
        
        // Recompute intersections if we reversed any curves
        if (needRecompute)
        {
            intersectionData = computeIntersectionGrid(context, uBSplines, vBSplines);
            vParamResult = sanitizeParams(intersectionData.vParams, MIN_PARAM_SPACING);
            uParamResult = sanitizeParams(intersectionData.uParams, MIN_PARAM_SPACING);
        }
        
        var intersectionGrid = intersectionData.points;
        var vParams_intersection = vParamResult.params;
        var uParams_intersection = uParamResult.params;
        
        // Debug output
        println("vParams (for u-curves): " ~ vParams_intersection);
        println("uParams (for v-curves): " ~ uParams_intersection);
        
        // If parameters were reversed, we need to reverse the corresponding curves
        // so that curve[i] still corresponds to params[i]
        if (vParamResult.reversed)
        {
            println("Note: Reversing uBSplines to match parameter direction");
            uBSplines = reverse(uBSplines);
        }
        if (uParamResult.reversed)
        {
            println("Note: Reversing vBSplines to match parameter direction");
            vBSplines = reverse(vBSplines);
        }
        
        // Debug output
        println("Raw vParams: " ~ intersectionData.vParams);
        println("Sanitized vParams: " ~ vParams_intersection ~ " (reversed: " ~ vParamResult.reversed ~ ")");
        println("Raw uParams: " ~ intersectionData.uParams);
        println("Sanitized uParams: " ~ uParams_intersection ~ " (reversed: " ~ uParamResult.reversed ~ ")");

        
        // Debug: show what we're working with
        println("Raw vParams: " ~ intersectionData.vParams);
        println("Sanitized vParams: " ~ vParams_intersection);
        println("Raw uParams: " ~ intersectionData.uParams);
        println("Sanitized uParams: " ~ uParams_intersection);
        
        if (definition.printInputCurves)
        {
            printCurveArray(uBSplines, "U-Curves (after compatibility)", definition.inputCurveFormat);
            printCurveArray(vBSplines, "V-Curves (after compatibility)", definition.inputCurveFormat);
            printIntersectionGrid(intersectionGrid, "Intersection Grid");
        }
        
        // S_u: Skinning through u-curves
        // vParams_intersection tells us where each u-curve lives in the v-direction
        var Su = createSkinningSurface(context, id + "Su", uBSplines, definition.vDegree, vParams_intersection);
        if (definition.create_s_u)
        {
            opCreateBSplineSurface(context, id + "SubSUsurf", {
                    "bSplineSurface" : Su
            });
            
            setProperty(context, {
                    "entities" : qCreatedBy(id + "SubSUsurf", EntityType.BODY),
                    "propertyType" : PropertyType.NAME,
                    "value" : "S_u Surface"
            });
        }
        
        
        // S_v: Skinning through v-curves, then transpose
        // v-curves run in v-direction, but createSkinningSurface assumes curves run in u
        // So we must transpose the result to get correct orientation
        var SvRaw = createSkinningSurface(context, id + "Sv", vBSplines, definition.uDegree, uParams_intersection);
        var Sv = transposeSurface(SvRaw);
        
        
        if (definition.create_s_v)
        {
            opCreateBSplineSurface(context, id + "SubSVsurf", {
                    "bSplineSurface" : Sv
            });
            
            setProperty(context, {
                    "entities" : qCreatedBy(id + "SubSVsurf", EntityType.BODY),
                    "propertyType" : PropertyType.NAME,
                    "value" : "S_v Surface"
            });
        }
        
        println("vParams (from intersections, for u-curves): " ~ vParams_intersection);
        println("uParams (from intersections, for v-curves): " ~ uParams_intersection);
        
        
        // T: Tensor product through intersection grid
        // intersectionGrid[i][j] is at parameter (uParams_intersection[j], vParams_intersection[i])
        // So rowParams = vParams_intersection, colParams = uParams_intersection
        var T = createTensorProductSurface(context, id + "T", intersectionGrid, vParams_intersection, uParams_intersection, definition.uDegree, definition.vDegree);
        
        
        
        if (definition.create_s_t)
        {
            opCreateBSplineSurface(context, id + "SubTensorSurf", {
                    "bSplineSurface" : T
            });
            
            setProperty(context, {
                    "entities" : qCreatedBy(id + "SubTensorSurf", EntityType.BODY),
                    "propertyType" : PropertyType.NAME,
                    "value" : "Subtensor Surface"
            });
        }
        
        // TODO: Make surfaces compatible and assemble final Gordon surface
        var compatibleSurfaces = makeSurfacesCompatible(context, id + "compat", [Su, Sv, T]);
        
        var gordon = assembleGordonSurface(compatibleSurfaces[0], compatibleSurfaces[1], compatibleSurfaces[2]);
        
        if (definition.print_S_u || definition.print_S_v || definition.print_tensor)
        {
            println("");
            println("╔═══════════════════════════════════════════════════════════════╗");
            println("║ AFTER makeSurfacesCompatible                                  ║");
            println("╚═══════════════════════════════════════════════════════════════╝");
            
            if (definition.print_S_u)
            {
                printSurface(compatibleSurfaces[0], "S_u (compatible)", definition.surfaceFormat);
            }
            if (definition.print_S_v)
            {
                printSurface(compatibleSurfaces[1], "S_v (compatible)", definition.surfaceFormat);
            }
            if (definition.print_tensor)
            {
                printSurface(compatibleSurfaces[2], "T (compatible)", definition.surfaceFormat);
            }
            printSurface(gordon, "Final Gordon Surface", definition.surfaceFormat);
        }
        
        
        
        opCreateBSplineSurface(context, id + "gordonSurf", { "bSplineSurface" : gordon });
        
        
        setProperty(context, {
                "entities" : qCreatedBy(id + "gordonSurf", EntityType.BODY),
                "propertyType" : PropertyType.NAME,
                "value" : "Gordon surf"
        });
        
        
    });
    
    
/**
 * Compute intersection points and parameters between two arrays of curves.
 * 
 * @param context {Context}
 * @param uCurves {array} : Ordered array of BSplineCurves running in u-direction
 * @param vCurves {array} : Ordered array of BSplineCurves running in v-direction
 * @returns {map} with fields:
 *   - points: 2D array, points[i][j] = where uCurves[i] intersects vCurves[j]
 *   - uCurveParams: 2D array, uCurveParams[i][j] = parameter on uCurves[i] at intersection with vCurves[j]
 *   - vCurveParams: 2D array, vCurveParams[i][j] = parameter on vCurves[j] at intersection with uCurves[i]
 *   - uParams: array, averaged u-parameters for where each v-curve lives
 *   - vParams: array, averaged v-parameters for where each u-curve lives
 */
export function computeIntersectionGrid(context is Context, uCurves is array, vCurves is array) returns map
{
    var numU = size(uCurves);
    var numV = size(vCurves);
    
    // Initialize grids
    var points = makeArray(numU);
    var uCurveParams = makeArray(numU);  // Parameter ON u-curves
    var vCurveParams = makeArray(numU);  // Parameter ON v-curves
    
    for (var i = 0; i < numU; i += 1)
    {
        points[i] = makeArray(numV);
        uCurveParams[i] = makeArray(numV);
        vCurveParams[i] = makeArray(numV);
        
        for (var j = 0; j < numV; j += 1)
        {
            var intersection = findCurveIntersection(context, uCurves[i], vCurves[j]);
            
            points[i][j] = intersection.point;
            uCurveParams[i][j] = intersection.paramA;  // param on u-curve i
            vCurveParams[i][j] = intersection.paramB;  // param on v-curve j
        }
    }
    
    // Compute averaged parameters:
    // vParams[i] = average parameter on v-curves where u-curve i crosses them
    //            = average over j of vCurveParams[i][j]
    // This tells us where u-curve i "lives" in the v-direction
    var vParams = makeArray(numU);
    for (var i = 0; i < numU; i += 1)
    {
        var sum = 0;
        for (var j = 0; j < numV; j += 1)
        {
            sum += vCurveParams[i][j];
        }
        vParams[i] = sum / numV;
    }
    
    // uParams[j] = average parameter on u-curves where v-curve j crosses them
    //            = average over i of uCurveParams[i][j]
    // This tells us where v-curve j "lives" in the u-direction
    var uParams = makeArray(numV);
    for (var j = 0; j < numV; j += 1)
    {
        var sum = 0;
        for (var i = 0; i < numU; i += 1)
        {
            sum += uCurveParams[i][j];
        }
        uParams[j] = sum / numU;
    }
    
    return {
        "points" : points,
        "uCurveParams" : uCurveParams,
        "vCurveParams" : vCurveParams,
        "uParams" : uParams,
        "vParams" : vParams
    };
}


/**
 * Find intersection point between two curves.
 * Uses closest approach — assumes curves actually intersect (or nearly do).
 * 
 * @returns {map} with fields:
 *   - point: The intersection point (midpoint of closest approach)
 *   - paramA: Parameter value on curveA at intersection
 *   - paramB: Parameter value on curveB at intersection
 */
function findCurveIntersection(context is Context, curveA is BSplineCurve, curveB is BSplineCurve) returns map
{
    // Sample both curves to find approximate closest point pair
    const numSamples = 20;
    var bestDist = inf * meter;
    var bestParamA = 0.5;
    var bestParamB = 0.5;
    
    for (var i = 0; i <= numSamples; i += 1)
    {
        var paramA = i / numSamples;
        var ptA = evaluateSpline({ "spline" : curveA, "parameters" : [paramA] })[0][0];
        
        for (var j = 0; j <= numSamples; j += 1)
        {
            var paramB = j / numSamples;
            var ptB = evaluateSpline({ "spline" : curveB, "parameters" : [paramB] })[0][0];
            
            var dist = norm(ptA - ptB);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestParamA = paramA;
                bestParamB = paramB;
            }
        }
    }
    
    // Refine with a few Newton-style iterations
    for (var iter = 0; iter < 10; iter += 1)
    {
        var result = refineIntersection(curveA, curveB, bestParamA, bestParamB);
        bestParamA = result.paramA;
        bestParamB = result.paramB;
    }
    
    // Return midpoint of the two closest points, plus both parameters
    var ptA = evaluateSpline({ "spline" : curveA, "parameters" : [bestParamA] })[0][0];
    var ptB = evaluateSpline({ "spline" : curveB, "parameters" : [bestParamB] })[0][0];
    
    return {
        "point" : (ptA + ptB) / 2,
        "paramA" : bestParamA,
        "paramB" : bestParamB
    };
}


/**
 * One iteration of intersection refinement.
 */
function refineIntersection(curveA is BSplineCurve, curveB is BSplineCurve, paramA is number, paramB is number) returns map
{
    const delta = 1e-6;
    
    var ptA = evaluateSpline({ "spline" : curveA, "parameters" : [paramA] })[0][0];
    var ptB = evaluateSpline({ "spline" : curveB, "parameters" : [paramB] })[0][0];
    
    // Tangent approximations
    var ptA_plus = evaluateSpline({ "spline" : curveA, "parameters" : [min(paramA + delta, 1)] })[0][0];
    var ptA_minus = evaluateSpline({ "spline" : curveA, "parameters" : [max(paramA - delta, 0)] })[0][0];
    var tanA = (ptA_plus - ptA_minus) / (2 * delta);
    
    var ptB_plus = evaluateSpline({ "spline" : curveB, "parameters" : [min(paramB + delta, 1)] })[0][0];
    var ptB_minus = evaluateSpline({ "spline" : curveB, "parameters" : [max(paramB - delta, 0)] })[0][0];
    var tanB = (ptB_plus - ptB_minus) / (2 * delta);
    
    // Vector from A to B
    var diff = ptB - ptA;
    
    // Project onto tangents to get parameter adjustments
    var tanANorm = squaredNorm(tanA);
    var tanBNorm = squaredNorm(tanB);
    
    var newParamA = paramA;
    var newParamB = paramB;
    
    if (tanANorm > 1e-20 * meter * meter)
    {
        newParamA = paramA + dot(diff, tanA) / tanANorm * 0.5;
    }
    if (tanBNorm > 1e-20 * meter * meter)
    {
        newParamB = paramB - dot(diff, tanB) / tanBNorm * 0.5;
    }
    
    // Clamp to [0, 1]
    newParamA = max(0, min(1, newParamA));
    newParamB = max(0, min(1, newParamB));
    
    return { "paramA" : newParamA, "paramB" : newParamB };
}

/**
 * Analyze parameters and ensure minimum spacing.
 * Does NOT sort - preserves index correspondence with curves.
 * 
 * @param params {array} : Input parameter values (in curve index order)
 * @param minSpacing {number} : Minimum required spacing between consecutive values
 * @returns {map} with fields:
 *   - params: Sanitized parameters with guaranteed minimum spacing
 *   - reversed: true if the input was in descending order and was flipped
 */
function sanitizeParams(params is array, minSpacing is number) returns map
{
    var n = size(params);
    if (n < 2)
    {
        return { "params" : params, "reversed" : false };
    }
    
    // Check if parameters are ascending or descending
    var ascending = params[n - 1] > params[0];
    var reversed = false;
    
    var working = makeArray(n);
    if (ascending)
    {
        // Already ascending, copy as-is
        for (var i = 0; i < n; i += 1)
        {
            working[i] = params[i];
        }
    }
    else
    {
        // Descending - flip the order (and remember we did this)
        reversed = true;
        for (var i = 0; i < n; i += 1)
        {
            working[i] = params[n - 1 - i];
        }
    }
    
    // Force endpoints to exact 0 and 1
    working[0] = 0;
    working[n - 1] = 1;
    
    // Check if we have enough room for minimum spacing
    var requiredRange = (n - 1) * minSpacing;
    if (requiredRange > 1)
    {
        // Not enough room - fall back to uniform spacing
        println("Warning: params too close together, using uniform spacing");
        var result = makeArray(n);
        for (var i = 0; i < n; i += 1)
        {
            result[i] = i / (n - 1);
        }
        return { "params" : result, "reversed" : reversed };
    }
    
    // Forward pass: ensure each param is at least minSpacing greater than previous
    var result = makeArray(n);
    result[0] = working[0];
    
    for (var i = 1; i < n; i += 1)
    {
        var minAllowed = result[i - 1] + minSpacing;
        result[i] = max(working[i], minAllowed);
    }
    
    // If last value exceeded 1, compress from the end
    if (result[n - 1] > 1)
    {
        result[n - 1] = 1;
        for (var i = n - 2; i >= 0; i -= 1)
        {
            var maxAllowed = result[i + 1] - minSpacing;
            result[i] = min(result[i], maxAllowed);
        }
    }
    
    return { "params" : result, "reversed" : reversed };
}

/**
 * Create a skinning surface through an array of curves.
 * Curves MUST already be compatible (same degree, same knots) via makeCurvesCompatible.
 *
 * @param context {Context}
 * @param id {Id} : Feature id for sub-operations
 * @param curves {array} : Array of compatible BSplineCurves to skin through
 * @param vDegree {number} : Desired degree in v-direction (will be clamped to numCurves-1)
 * @param vParams {array} : Parameter values where each curve lives in v-direction.
 *                          Length must equal size(curves). Values should span [0, 1].
 * @returns {BSplineSurface}
 */
export function createSkinningSurface(
    context is Context,
    id is Id,
    curves is array,
    vDegree is number,
    vParams is array
) returns BSplineSurface
{
    var numCurves = size(curves);
    
    if (numCurves < 2)
    {
        throw regenError("Need at least 2 curves for skinning");
    }
    
    if (size(vParams) != numCurves)
    {
        throw regenError("vParams length (" ~ size(vParams) ~ ") must match number of curves (" ~ numCurves ~ ")");
    }
    
    // Validate curves are compatible
    var numCPsU = size(curves[0].controlPoints);
    var uDegree = curves[0].degree;
    var uKnots = curves[0].knots;
    
    for (var i = 1; i < numCurves; i += 1)
    {
        if (curves[i].degree != uDegree)
        {
            throw regenError("Curve " ~ i ~ " has different degree. Run makeCurvesCompatible first.");
        }
        if (size(curves[i].controlPoints) != numCPsU)
        {
            throw regenError("Curve " ~ i ~ " has different control point count. Run makeCurvesCompatible first.");
        }
    }
    
    // Check if any curve is rational
    var anyRational = false;
    for (var curve in curves)
    {
        if (curve.isRational && curve.weights != undefined)
        {
            anyRational = true;
            break;
        }
    }
    
    // Clamp vDegree to what's achievable
    vDegree = min(vDegree, numCurves - 1);
    
    // Build column curves
    // For each control point index j, gather the j-th CP from all curves
    // and interpolate through them in the v-direction
    var columnCurves = [];
    
    for (var j = 0; j < numCPsU; j += 1)
    {
        var columnPoints = [];
        var columnWeights = [];
        
        for (var i = 0; i < numCurves; i += 1)
        {
            if (anyRational)
            {
                // For rational curves, work in homogeneous coordinates
                var w = getWeight(curves[i], j);
                var pt = curves[i].controlPoints[j];
                
                // Store weighted point and weight separately
                columnPoints = append(columnPoints, pt * w);
                columnWeights = append(columnWeights, w);
            }
            else
            {
                columnPoints = append(columnPoints, curves[i].controlPoints[j]);
            }
        }
        
        // Interpolate through column points
        var columnCurve = interpolateColumnCurve(context, columnPoints, vParams, vDegree);
        
        // If rational, also interpolate weights
        if (anyRational)
        {
            var weightCurve = interpolateWeightCurve(context, columnWeights, vParams, vDegree);
            columnCurve = attachWeightCurve(columnCurve, weightCurve);
        }
        
        columnCurves = append(columnCurves, columnCurve);
    }
    
    // Make column curves compatible
    // This ensures all columns have the same v-knots and v-CP count
    columnCurves = makeCurvesCompatible(context, id + "columnCompat", columnCurves);
    
    var vKnots = columnCurves[0].knots;
    var numCPsV = size(columnCurves[0].controlPoints);
    
    // Assemble surface control point grid
    // surfaceCPs[u][v] where u indexes along original curves, v indexes across curves
    var surfaceCPs = makeArray(numCPsU);
    var surfaceWeights = makeArray(numCPsU);
    
    for (var j = 0; j < numCPsU; j += 1)
    {
        surfaceCPs[j] = makeArray(numCPsV);
        surfaceWeights[j] = makeArray(numCPsV);
        
        for (var i = 0; i < numCPsV; i += 1)
        {
            if (anyRational)
            {
                var w = columnCurves[j].weights[i];
                var homogPt = columnCurves[j].controlPoints[i];
                
                surfaceCPs[j][i] = homogPt / w;
                surfaceWeights[j][i] = w;
            }
            else
            {
                surfaceCPs[j][i] = columnCurves[j].controlPoints[i];
                surfaceWeights[j][i] = 1.0;
            }
        }
    }
    
    var surfaceDef = {
        "uDegree" : uDegree,
        "vDegree" : columnCurves[0].degree,
        "isUPeriodic" : false,
        "isVPeriodic" : false,
        "isRational" : anyRational,
        "controlPoints" : controlPointMatrix(surfaceCPs),
        "uKnots" : knotArray(uKnots),
        "vKnots" : knotArray(vKnots)
    };
    
    // Only include weights if rational
    if (anyRational)
    {
        surfaceDef.weights = matrix(surfaceWeights);
    }
    
    return bSplineSurface(surfaceDef);
}


/**
 * Compute uniform parameters for n curves: [0, 1/(n-1), 2/(n-1), ..., 1]
 */
export function computeUniformParams(numCurves is number) returns array
{
    var params = [];
    for (var i = 0; i < numCurves; i += 1)
    {
        params = append(params, i / (numCurves - 1));
    }
    return params;
}


/**
 * Compute chord-length parameters based on average distance between curves.
 * 
 * Samples each curve at multiple points, computes average distance to next curve,
 * then normalizes cumulative distances to [0, 1].
 */
export function computeChordLengthParams(curves is array) returns array
{
    var numCurves = size(curves);
    if (numCurves < 2)
    {
        return [0];
    }
    
    const numSamples = 10;  // Sample points per curve for distance estimation
    
    // Compute average distance between consecutive curves
    var distances = [0 * meter];  // First curve at distance 0
    
    for (var i = 1; i < numCurves; i += 1)
    {
        var totalDist = 0 * meter;
        
        for (var s = 0; s < numSamples; s += 1)
        {
            var param = s / (numSamples - 1);
            
            var pt0 = evaluateSpline({
                "spline" : curves[i - 1],
                "parameters" : [param]
            })[0][0];
            
            var pt1 = evaluateSpline({
                "spline" : curves[i],
                "parameters" : [param]
            })[0][0];
            
            totalDist += norm(pt1 - pt0);
        }
        
        var avgDist = totalDist / numSamples;
        distances = append(distances, distances[i - 1] + avgDist);
    }
    
    // Normalize to [0, 1]
    var totalLength = distances[numCurves - 1];
    var params = [];
    
    for (var i = 0; i < numCurves; i += 1)
    {
        params = append(params, distances[i] / totalLength);
    }
    
    // Force exact endpoints
    params[0] = 0;
    params[numCurves - 1] = 1;
    
    return params;
}


/**
 * Interpolate a curve through column control points at given parameters.
 */
function interpolateColumnCurve(
    context is Context,
    points is array,
    params is array,
    degree is number
) returns BSplineCurve
{
    var result = approximateSpline(context, {
        "degree" : degree,
        "tolerance" : 1e-6 * meter,  
        "isPeriodic" : false,
        "targets" : [approximationTarget({ "positions" : points })],
        "parameters" : params,
        "interpolateIndices" : range(0, size(points) - 1)  // Interpolate ALL points
    });
    
    return result[0];
}


/**
 * Interpolate weights as a 1D "curve" (scalar values along parameter).
 * Returns a pseudo-curve where controlPoints are actually scalar weights.
 */
function interpolateWeightCurve(
    context is Context,
    weights is array,
    params is array,
    degree is number
) returns map
{
    // Convert scalar weights to 1D "points" for approximateSpline
    var points1D = [];
    for (var w in weights)
    {
        // Use a dummy 3D point where x = weight, y = z = 0
        points1D = append(points1D, vector(w, 0, 0) * meter);
    }
    
    var result = approximateSpline(context, {
        "degree" : degree,
        "tolerance" : 1e-6 * meter,
        "isPeriodic" : false,
        "targets" : [approximationTarget({ "positions" : points1D })],
        "parameters" : params,
        "interpolateIndices" : range(0, size(weights) - 1)
    });
    
    // Extract the x-coordinates as weights
    var interpWeights = [];
    for (var cp in result[0].controlPoints)
    {
        interpWeights = append(interpWeights, cp[0] / meter);  // Remove dummy units
    }
    
    return {
        "weights" : interpWeights,
        "knots" : result[0].knots,
        "degree" : result[0].degree
    };
}


/**
 * Attach interpolated weights to a column curve.
 * The weight curve must have the same knots/degree as the point curve
 * (which it will if interpolated with the same parameters).
 */
function attachWeightCurve(pointCurve is BSplineCurve, weightCurve is map) returns BSplineCurve
{
    // Verify compatibility
    if (size(pointCurve.controlPoints) != size(weightCurve.weights))
    {
        throw regenError("Weight curve has different CP count than point curve");
    }
    
    return bSplineCurve({
        "degree" : pointCurve.degree,
        "isPeriodic" : pointCurve.isPeriodic,
        "isRational" : true,
        "controlPoints" : pointCurve.controlPoints,
        "knots" : pointCurve.knots,
        "weights" : weightCurve.weights
    });
}


/**
 * Get weight at control point index, defaulting to 1.0 if not rational.
 */
function getWeight(curve is BSplineCurve, index is number) returns number
{
    if (curve.isRational && curve.weights != undefined)
    {
        return curve.weights[index];
    }
    return 1.0;
}


/**
 * Create a tensor product surface through a grid of points.
 * This is the "T" surface in the Gordon formula: S = S_u + S_v - T
 *
 * Grid indexing convention:
 *   pointGrid[i][j] is the point at surface parameter (colParams[j], rowParams[i])
 *   - i is the "row" index (outer dimension), mapped by rowParams
 *   - j is the "column" index (inner dimension), mapped by colParams
 *
 * For Gordon surfaces with u-curves and v-curves:
 *   - i = u-curve index, rowParams = vParams (where each u-curve lives in v)
 *   - j = v-curve index, colParams = uParams (where each v-curve lives in u)
 *
 * @param context {Context}
 * @param id {Id}
 * @param pointGrid {array} : 2D array of points, pointGrid[i][j]
 * @param rowParams {array} : Parameter values for row index (maps i to v-parameter)
 * @param colParams {array} : Parameter values for column index (maps j to u-parameter)
 * @param uDegree {number} : Desired degree in u-direction (along columns)
 * @param vDegree {number} : Desired degree in v-direction (along rows)
 * @returns {BSplineSurface} : Surface in correct [u][v] orientation, no transpose needed
 */
export function createTensorProductSurface(
    context is Context,
    id is Id,
    pointGrid is array,
    rowParams is array,
    colParams is array,
    uDegree is number,
    vDegree is number
) returns BSplineSurface
{
    var numRows = size(pointGrid);         // Outer dimension (i index)
    var numCols = size(pointGrid[0]);      // Inner dimension (j index)
    
    // Clamp degrees
    uDegree = min(uDegree, numCols - 1);
    vDegree = min(vDegree, numRows - 1);
    
    // Sanitize parameters to ensure minimum spacing
    // Note: We only extract .params here because curve ordering is already handled by caller
    const MIN_PARAM_SPACING = 2e-6;
    rowParams = sanitizeParams(rowParams, MIN_PARAM_SPACING).params;
    colParams = sanitizeParams(colParams, MIN_PARAM_SPACING).params;
    
    // Step 1: Build column curves (curves that run in v-direction, one per column j)
    // For each column j, gather points across rows i and interpolate with rowParams
    var colCurves = makeArray(numCols);
    for (var j = 0; j < numCols; j += 1)
    {
        var colPoints = makeArray(numRows);
        for (var i = 0; i < numRows; i += 1)
        {
            colPoints[i] = pointGrid[i][j];
        }
        colCurves[j] = interpolateThroughPoints(context, colPoints, rowParams, vDegree);
    }
    
    // Step 2: Make column curves compatible (same knots in v-direction)
    colCurves = makeCurvesCompatible(context, id + "colCompat", colCurves);
    
    var vKnots = colCurves[0].knots;
    var numCPsV = size(colCurves[0].controlPoints);
    
    // Step 3: Build row curves from column curve control points
    // For each v control point index, gather across columns and interpolate with colParams
    var rowCurves = makeArray(numCPsV);
    for (var vi = 0; vi < numCPsV; vi += 1)
    {
        var rowPoints = makeArray(numCols);
        for (var j = 0; j < numCols; j += 1)
        {
            rowPoints[j] = colCurves[j].controlPoints[vi];
        }
        rowCurves[vi] = interpolateThroughPoints(context, rowPoints, colParams, uDegree);
    }
    
    // Step 4: Make row curves compatible (same knots in u-direction)
    rowCurves = makeCurvesCompatible(context, id + "rowCompat", rowCurves);
    
    var uKnots = rowCurves[0].knots;
    var numCPsU = size(rowCurves[0].controlPoints);
    
    // Step 5: Assemble control points in Onshape's [u][v] order
    // surfaceCPs[ui][vi] where ui is u-index, vi is v-index
    var surfaceCPs = makeArray(numCPsU);
    
    for (var ui = 0; ui < numCPsU; ui += 1)
    {
        surfaceCPs[ui] = makeArray(numCPsV);
        for (var vi = 0; vi < numCPsV; vi += 1)
        {
            surfaceCPs[ui][vi] = rowCurves[vi].controlPoints[ui];
        }
    }
    
    var surfaceDef = {
        "uDegree" : rowCurves[0].degree,
        "vDegree" : colCurves[0].degree,
        "isUPeriodic" : false,
        "isVPeriodic" : false,
        "isRational" : false,
        "controlPoints" : controlPointMatrix(surfaceCPs),
        "uKnots" : knotArray(uKnots),
        "vKnots" : knotArray(vKnots)
    };
    
    surfaceDef = normalizeSurfaceDef(surfaceDef);
    
    return bSplineSurface(surfaceDef);
}


// ============================================================================
// SURFACE COMPATIBILITY
// ============================================================================

/**
 * Make multiple B-spline surfaces compatible (same degrees, same knot vectors).
 * Required before Gordon assembly.
 *
 * @param context {Context}
 * @param id {Id}
 * @param surfaces {array} : Array of BSplineSurface
 * @returns {array} : Array of compatible BSplineSurface
 */
export function makeSurfacesCompatible(
    context is Context,
    id is Id,
    surfaces is array
) returns array
{
    if (size(surfaces) < 2)
    {
        return surfaces;
    }
    
    // Find maximum degrees
    var maxUDegree = 0;
    var maxVDegree = 0;
    
    for (var surf in surfaces)
    {
        if (surf.uDegree > maxUDegree) maxUDegree = surf.uDegree;
        if (surf.vDegree > maxVDegree) maxVDegree = surf.vDegree;
    }
    
    // Step 1: Elevate all surfaces to max degrees
    var elevated = [];
    var surfIndex = 0;
    for (var surf in surfaces)
    {
        var elev = surf;
        
        if (elev.uDegree < maxUDegree)
        {
            elev = elevateSurfaceUDegree(context, id + ("elevateFaceUDegree_" ~ surfIndex),elev, maxUDegree);
        }
        if (elev.vDegree < maxVDegree)
        {
            elev = elevateSurfaceVDegree(context, id + ("elevateFaceVDegree_" ~ surfIndex), elev, maxVDegree);
        }
        
        elevated = append(elevated, elev);
        
        surfIndex += 1;
    }
    
    // Step 2: Merge knot vectors
    var mergedUKnots = [];
    var mergedVKnots = [];
    const tolerance = 1e-10;
    
    for (var surf in elevated)
    {
        mergedUKnots = mergeKnotVectors(getInteriorKnotsFromVector(surf.uKnots, maxUDegree), mergedUKnots, tolerance);
        mergedVKnots = mergeKnotVectors(getInteriorKnotsFromVector(surf.vKnots, maxVDegree), mergedVKnots, tolerance);
    }
    
    // Step 3: Insert missing knots into each surface
    var compatible = [];
    for (var surf in elevated)
    {
        var refined = surf;
        
        var myUInterior = getInteriorKnotsFromVector(refined.uKnots, maxUDegree);
        var uToInsert = getKnotsToInsertArray(myUInterior, mergedUKnots, tolerance);
        if (size(uToInsert) > 0)
        {
            refined = refineSurfaceUKnots(context, refined, uToInsert);
        }
        
        var myVInterior = getInteriorKnotsFromVector(refined.vKnots, maxVDegree);
        var vToInsert = getKnotsToInsertArray(myVInterior, mergedVKnots, tolerance);
        if (size(vToInsert) > 0)
        {
            refined = refineSurfaceVKnots(context, refined, vToInsert);
        }
        
        compatible = append(compatible, refined);
    }
    
    return compatible;
}


/**
 * Elevate surface degree in u-direction.
 * Treats each row of control points as a curve, elevates it, reassembles.
 */
export function elevateSurfaceUDegree(context is Context, id is Id, surface is BSplineSurface, targetDegree is number) returns BSplineSurface
{
    if (surface.uDegree >= targetDegree)
    {
        return surface;
    }
    
    var numU = size(surface.controlPoints);
    var numV = size(surface.controlPoints[0]);
    
    // Elevate each row (constant v)
    var elevatedRows = [];
    for (var v = 0; v < numV; v += 1)
    {
        var rowCurve = surfaceRowToCurve(surface, v);
        var elevated = elevate(context, id + ("elevRow" ~ v), rowCurve, targetDegree);
        elevatedRows = append(elevatedRows, elevated);
    }
    
    // Reassemble: newCPs[u][v]
    var newNumU = size(elevatedRows[0].controlPoints);
    var newCPs = makeArray(newNumU);
    var newWeights = makeArray(newNumU);
    
    for (var u = 0; u < newNumU; u += 1)
    {
        newCPs[u] = makeArray(numV);
        newWeights[u] = makeArray(numV);
        for (var v = 0; v < numV; v += 1)
        {
            newCPs[u][v] = elevatedRows[v].controlPoints[u];
            newWeights[u][v] = surface.isRational ? elevatedRows[v].weights[u] : 1.0;
        }
    }
    
    var surfaceDef = {
        "uDegree" : targetDegree,
        "vDegree" : surface.vDegree,
        "isUPeriodic" : surface.isUPeriodic,
        "isVPeriodic" : surface.isVPeriodic,
        "isRational" : surface.isRational,
        "controlPoints" : controlPointMatrix(newCPs),
        "uKnots" : elevatedRows[0].knots,
        "vKnots" : surface.vKnots
    };
    
    if (surface.isRational)
    {
        surfaceDef.weights = matrix(newWeights);
    }
    
    /*
    surfaceDef["uKnots"] = knotArray(getInteriorKnotsFromVector(rowCurves[0].knots, targetDegree));
    surfaceDef["vKnots"] = knotArray(getInteriorKnotsFromVector(surface.vKnots, surface.vDegree));
    */
    
    surfaceDef = normalizeSurfaceDef(surfaceDef);
    
    return bSplineSurface(surfaceDef);
}


/**
 * Elevate surface degree in v-direction.
 * Treats each column of control points as a curve, elevates it, reassembles.
 */
export function elevateSurfaceVDegree(context is Context, id is Id, surface is BSplineSurface, targetDegree is number) returns BSplineSurface
{
    if (surface.vDegree >= targetDegree)
        return surface;
    
    var numU = size(surface.controlPoints);
    var numV = size(surface.controlPoints[0]);
    
    // Elevate each column (constant u)
    var elevatedCols = [];
    for (var u = 0; u < numU; u += 1)
    {
        var colCurve = surfaceColumnToCurve(surface, u);
        var elevated = elevate(context, id + ("elevCol" ~ u), colCurve, targetDegree);
        elevatedCols = append(elevatedCols, elevated);
    }
    
    // Reassemble: newCPs[u][v]
    var newNumV = size(elevatedCols[0].controlPoints);
    var newCPs = makeArray(numU);
    var newWeights = makeArray(numU);
    
    for (var u = 0; u < numU; u += 1)
    {
        newCPs[u] = makeArray(newNumV);
        newWeights[u] = makeArray(newNumV);
        for (var v = 0; v < newNumV; v += 1)
        {
            newCPs[u][v] = elevatedCols[u].controlPoints[v];
            newWeights[u][v] = surface.isRational ? elevatedCols[u].weights[v] : 1.0;
        }
    }
    
    var surfaceDef = {
        "uDegree" : surface.uDegree,
        "vDegree" : targetDegree,
        "isUPeriodic" : surface.isUPeriodic,
        "isVPeriodic" : surface.isVPeriodic,
        "isRational" : surface.isRational,
        "controlPoints" : controlPointMatrix(newCPs),
        "weights" : newWeights,
        "uKnots" : surface.uKnots,
        "vKnots" : elevatedCols[0].knots
        };
    
    if (surface.isRational)
    {
        surfaceDef.weights = matrix(newWeights);
    }
    
    /*
    surfaceDef["uKnots"] = knotArray(getInteriorKnotsFromVector(surface.uKnots, surface.uDegree));
    surfaceDef["vKnots"] = knotArray(getInteriorKnotsFromVector(colCurves[0].knots, targetDegree));
    */
    
    surfaceDef = normalizeSurfaceDef(surfaceDef);
    
    return bSplineSurface(surfaceDef);
}


/**
 * Insert knots into surface in u-direction.
 * Applies knot insertion to each row.
 */
export function refineSurfaceUKnots(context is Context, surface is BSplineSurface, knotsToInsert is array) returns BSplineSurface
{
    if (size(knotsToInsert) == 0)
    {
        return surface;
    }
    
    var numU = size(surface.controlPoints);
    var numV = size(surface.controlPoints[0]);
    
    var refinedRows = [];
    for (var v = 0; v < numV; v += 1)
    {
        var rowCurve = surfaceRowToCurve(surface, v);
        var refined = refineKnotVector(context, rowCurve, knotsToInsert);
        refinedRows = append(refinedRows, refined);
    }
    
    var newNumU = size(refinedRows[0].controlPoints);
    var newCPs = makeArray(newNumU);
    var newWeights = makeArray(newNumU);
    
    for (var j = 0; j < newNumU; j += 1)
    {
        newCPs[j] = makeArray(numV);
        newWeights[j] = makeArray(numV);
        
        for (var i = 0; i < numV; i += 1)
        {
            newCPs[j][i] = refinedRows[i].controlPoints[j];
            newWeights[j][i] = surface.isRational ? refinedRows[i].weights[j] : 1.0;
        }
    }
    
    
    var surfaceDef = {
        "uDegree" : surface.uDegree,
        "vDegree" : surface.vDegree,
        "isUPeriodic" : surface.isUPeriodic,
        "isVPeriodic" : surface.isVPeriodic,
        "isRational" : surface.isRational,
        "controlPoints" : controlPointMatrix(newCPs),
        "weights" : newWeights,
        "uKnots" : refinedRows[0].knots,
        "vKnots" : surface.vKnots
    };
    
    if (surface.isRational)
    {
        surfaceDef.weights = matrix(newWeights);
    }
    
    //surfaceDef["uKnots"] = knotArray(getInteriorKnotsFromVector(refinedRows[0].knots, surface.uDegree));
    //surfaceDef["vKnots"] = knotArray(getInteriorKnotsFromVector(surface.vKnots, surface.vDegree));
    
    surfaceDef = normalizeSurfaceDef(surfaceDef);
    
    return bSplineSurface(surfaceDef);
}


/**
 * Insert knots into surface in v-direction.
 * Applies knot insertion to each column.
 */
export function refineSurfaceVKnots(context is Context, surface is BSplineSurface, knotsToInsert is array) returns BSplineSurface
{
    if (size(knotsToInsert) == 0)
    {
        return surface;
    }
    
    var numU = size(surface.controlPoints);
    var numV = size(surface.controlPoints[0]);
    
    var refinedCols = [];
    for (var u = 0; u < numU; u += 1)
    {
        var colCurve = surfaceColumnToCurve(surface, u);
        var refined = refineKnotVector(context, colCurve, knotsToInsert);
        refinedCols = append(refinedCols, refined);
    }
    
    var newNumV = size(refinedCols[0].controlPoints);
    var newCPs = makeArray(numU);
    var newWeights = makeArray(numU);
    
    for (var j = 0; j < numU; j += 1)
    {
        newCPs[j] = makeArray(newNumV);
        newWeights[j] = makeArray(newNumV);
        
        for (var i = 0; i < newNumV; i += 1)
        {
            newCPs[j][i] = refinedCols[j].controlPoints[i];
            newWeights[j][i] = surface.isRational ? refinedCols[j].weights[i] : 1.0;
        }
    }
    
    var surfaceDef = {
        "uDegree" : surface.uDegree,
        "vDegree" : surface.vDegree,
        "isUPeriodic" : surface.isUPeriodic,
        "isVPeriodic" : surface.isVPeriodic,
        "isRational" : surface.isRational,
        "controlPoints" : controlPointMatrix(newCPs),
        "weights" : newWeights,
        "uKnots" : surface.uKnots,
        "vKnots" : refinedCols[0].knots
    };
    
    if (surface.isRational)
    {
        surfaceDef.weights = matrix(newWeights);
    }
    /*
    surfaceDef["uKnots"] = knotArray(getInteriorKnotsFromVector(surface.uKnots, surface.uDegree));
    surfaceDef["vKnots"] = knotArray(getInteriorKnotsFromVector(refinedCols[0].knots, surface.vDegree));
    */
    
    surfaceDef = normalizeSurfaceDef(surfaceDef);
    
    return bSplineSurface(surfaceDef);
}


/**
 * Extract interior knots from a knot vector.
 */
function getInteriorKnotsFromVector(knots is array, degree is number) returns array
{
    var startIndex = degree + 1;
    var endIndex = size(knots) - degree - 1;
    
    if (endIndex <= startIndex)
    {
        return [];
    }
    
    var interior = [];
    for (var i = startIndex; i < endIndex; i += 1)
    {
        interior = append(interior, knots[i]);
    }
    
    return interior;
}


/**
 * Find knots in target that aren't in current (or have lower multiplicity).
 */
function getKnotsToInsertArray(currentInterior is array, targetInterior is array, tolerance is number) returns array
{
    var currentMap = {};
    for (var knot in currentInterior)
    {
        var key = round(knot / tolerance) * tolerance;
        currentMap[key] = (currentMap[key] == undefined) ? 1 : currentMap[key] + 1;
    }
    
    var targetMap = {};
    for (var knot in targetInterior)
    {
        var key = round(knot / tolerance) * tolerance;
        targetMap[key] = (targetMap[key] == undefined) ? 1 : targetMap[key] + 1;
    }
    
    var toInsert = [];
    for (var key, targetMult in targetMap)
    {
        var currentMult = (currentMap[key] == undefined) ? 0 : currentMap[key];
        var needed = targetMult - currentMult;
        
        for (var i = 0; i < needed; i += 1)
        {
            toInsert = append(toInsert, key);
        }
    }
    
    return sort(toInsert, function(a, b) { return a - b; });
}


// ============================================================================
// GORDON ASSEMBLY
// ============================================================================

/**
 * Assemble the Gordon surface from three compatible surfaces.
 * Gordon formula: S = S_u + S_v - T
 *
 * All three surfaces must have identical:
 *   - uDegree, vDegree
 *   - uKnots, vKnots
 *   - Control point grid dimensions
 *
 * @param Su {BSplineSurface} : Skinning surface through U-curves
 * @param Sv {BSplineSurface} : Skinning surface through V-curves
 * @param T {BSplineSurface} : Tensor product surface through intersection points
 * @returns {BSplineSurface} : The Gordon surface
 */
export function assembleGordonSurface(
    Su is BSplineSurface,
    Sv is BSplineSurface,
    T is BSplineSurface
) returns BSplineSurface
{
    // Onshape stores controlPoints[u][v], so first index is u
    var numU = size(Su.controlPoints);
    var numV = size(Su.controlPoints[0]);
    
    // Verify dimensions match
    if (size(Sv.controlPoints) != numU || size(Sv.controlPoints[0]) != numV)
    {
        throw regenError("Sv dimensions don't match Su");
    }
    if (size(T.controlPoints) != numU || size(T.controlPoints[0]) != numV)
    {
        throw regenError("T dimensions don't match Su");
    }
    
    // Compute Gordon control points: CP_gordon = CP_Su + CP_Sv - CP_T
    var gordonCPs = makeArray(numU);
    
    for (var j = 0; j < numU; j += 1)
    {
        gordonCPs[j] = makeArray(numV);
        
        for (var i = 0; i < numV; i += 1)
        {
            gordonCPs[j][i] = Su.controlPoints[j][i] + Sv.controlPoints[j][i] - T.controlPoints[j][i];
        }
    }
    
    var surfaceDef = {
        "uDegree" : Su.uDegree,
        "vDegree" : Su.vDegree,
        "isUPeriodic" : false,
        "isVPeriodic" : false,
        "isRational" : false,
        "controlPoints" : gordonCPs,
        "uKnots" : Su.uKnots,
        "vKnots" : Su.vKnots
    };
    
    // Let normalizeSurfaceDef handle type coercion and validation
    surfaceDef = normalizeSurfaceDef(surfaceDef);
    
    return bSplineSurface(surfaceDef);
}


/**
 * Full Gordon surface construction from curve network.
 *
 * @param context {Context}
 * @param id {Id}
 * @param uCurves {array} : Array of BSplineCurves running in u-direction
 * @param vCurves {array} : Array of BSplineCurves running in v-direction
 * @param intersectionGrid {array} : 2D array of intersection points, grid[i][j] = uCurves[i] ∩ vCurves[j]
 * @param uParams {array} : Parameter values for u-curves in v-direction
 * @param vParams {array} : Parameter values for v-curves in u-direction
 * @param uDegree {number} : Desired u-degree
 * @param vDegree {number} : Desired v-degree
 * @returns {BSplineSurface}
 */
export function createGordonSurface(
    context is Context,
    id is Id,
    uCurves is array,
    vCurves is array,
    intersectionGrid is array,
    uParams is array,
    vParams is array,
    uDegree is number,
    vDegree is number
) returns BSplineSurface
{
    // Step 1: Create S_u by skinning through u-curves
    var Su = createSkinningSurface(context, id + "Su", uCurves, vDegree, VParamMode.CHORD_LENGTH);
    
    // Step 2: Create S_v by skinning through v-curves
    // Note: v-curves run perpendicular, so we skin them and the result has u/v swapped
    var SvRaw = createSkinningSurface(context, id + "Sv", vCurves, uDegree, VParamMode.CHORD_LENGTH);
    var Sv = transposeSurface(SvRaw);
    
    // Step 3: Create tensor product surface T through intersection points
    var T = createTensorProductSurface(context, id + "T", intersectionGrid, vParams, uParams, uDegree, vDegree);
    
    // Step 4: Make all three surfaces compatible
    var compatibleSurfaces = makeSurfacesCompatible(context, id + "compat", [Su, Sv, T]);
    Su = compatibleSurfaces[0];
    Sv = compatibleSurfaces[1];
    T = compatibleSurfaces[2];
    
    // Step 5: Assemble Gordon surface
    return assembleGordonSurface(Su, Sv, T);
}


/**
 * Transpose a surface (swap u and v directions).
 */
export function transposeSurface(surface is BSplineSurface) returns BSplineSurface
{
    var numU = size(surface.controlPoints);
    var numV = size(surface.controlPoints[0]);
    
    // Input surface has controlPoints[u][v], output needs controlPoints[v][u] (new u = old v)
    // Since we swap degrees and knots, we also swap CP indexing
    var transposedCPs = makeArray(numV);
    var transposedWeights = makeArray(numV);
    
    for (var i = 0; i < numV; i += 1)
    {
        transposedCPs[i] = makeArray(numU);
        transposedWeights[i] = makeArray(numU);
        
        for (var j = 0; j < numU; j += 1)
        {
            transposedCPs[i][j] = surface.controlPoints[j][i];
            transposedWeights[i][j] = surface.isRational ? surface.weights[j][i] : 1.0;
        }
    }
    
    var surfaceDef = {
        "uDegree" : surface.vDegree,
        "vDegree" : surface.uDegree,
        "isUPeriodic" : surface.isVPeriodic,
        "isVPeriodic" : surface.isUPeriodic,
        "isRational" : surface.isRational,
        "controlPoints" : controlPointMatrix(transposedCPs),
        "uKnots" : surface.vKnots,
        "vKnots" : surface.uKnots
    };
    
    if (surface.isRational)
    {
        surfaceDef.weights = matrix(transposedWeights);
    }
    
    //surfaceDef["uKnots"] = knotArray(getInteriorKnotsFromVector(surface.vKnots, surface.vDegree));
    //surfaceDef["vKnots"] = knotArray(getInteriorKnotsFromVector(surface.uKnots, surface.uDegree));
    
    surfaceDef = normalizeSurfaceDef(surfaceDef);
    
    return bSplineSurface(surfaceDef);
}

function interpolateThroughPoints(context, points, params, degree)
{
    var interpIndices = [];
    for (var i = 0; i < size(points); i += 1)
    {
        interpIndices = append(interpIndices, i);
    }
    
    return approximateSpline(context, {
        "degree" : degree,
        "tolerance" : 1e-6 * meter,
        "isPeriodic" : false,
        "targets" : [approximationTarget({ "positions" : points })],
        "parameters" : params,
        "interpolateIndices" : interpIndices  // Forces interpolation at ALL points
    })[0];
}

/**
 * Validate and normalize a surface definition before passing to bSplineSurface.
 * Handles type coercion, default values, and consistency checks.
 * 
 * @param surfaceDef {map} : Raw surface definition
 * @returns {map} : Normalized surface definition ready for bSplineSurface()
 */
function normalizeSurfaceDef(surfaceDef is map) returns map
{
    // ========================================================================
    // STEP 1: Extract dimensions from control points
    // ========================================================================
    
    if (surfaceDef.controlPoints == undefined)
    {
        throw regenError("Surface definition missing controlPoints");
    }
    
    var controlPoints = surfaceDef.controlPoints;
    
    // Check if already a ControlPointMatrix or raw 2D array
    var numCPsU;  // rows: outer dimension (first index = u)
    var numCPsV;  // columns: inner dimension (second index = v)
    
    try
    {
        // Try to get dimensions - works for both raw arrays and ControlPointMatrix
        numCPsU = size(controlPoints);
        numCPsV = size(controlPoints[0]);
    }
    catch
    {
        throw regenError("controlPoints must be a 2D array or ControlPointMatrix");
    }
    
    if (numCPsV < 2 || numCPsU < 2)
    {
        throw regenError("Control point grid must be at least 2x2, got " ~ numCPsV ~ "x" ~ numCPsU);
    }
    
    // Wrap in controlPointMatrix if needed
    if (!(controlPoints is ControlPointMatrix))
    {
        surfaceDef.controlPoints = controlPointMatrix(controlPoints);
    }
    
    // ========================================================================
    // STEP 2: Validate and default degrees
    // ========================================================================
    
    if (surfaceDef.uDegree == undefined)
    {
        throw regenError("Surface definition missing uDegree");
    }
    if (surfaceDef.vDegree == undefined)
    {
        throw regenError("Surface definition missing vDegree");
    }
    
    var uDegree = surfaceDef.uDegree;
    var vDegree = surfaceDef.vDegree;
    
    if (uDegree < 1 || uDegree >= numCPsU)
    {
        throw regenError("uDegree must be >= 1 and < numCPsU. Got uDegree=" ~ uDegree ~ ", numCPsU=" ~ numCPsU);
    }
    if (vDegree < 1 || vDegree >= numCPsV)
    {
        throw regenError("vDegree must be >= 1 and < numCPsV. Got vDegree=" ~ vDegree ~ ", numCPsV=" ~ numCPsV);
    }
    
    // ========================================================================
    // STEP 3: Validate and normalize knot arrays
    // ========================================================================
    
    if (surfaceDef.uKnots == undefined)
    {
        throw regenError("Surface definition missing uKnots");
    }
    if (surfaceDef.vKnots == undefined)
    {
        throw regenError("Surface definition missing vKnots");
    }
    
    // Type coercion
    if (!(surfaceDef.uKnots is KnotArray))
    {
        surfaceDef.uKnots = surfaceDef.uKnots as KnotArray;
    }
    if (!(surfaceDef.vKnots is KnotArray))
    {
        surfaceDef.vKnots = surfaceDef.vKnots as KnotArray;
    }
    
    // Size validation
    var expectedUKnots = uDegree + numCPsU + 1;
    var expectedVKnots = vDegree + numCPsV + 1;
    
    if (size(surfaceDef.uKnots) != expectedUKnots)
    {
        throw regenError("uKnots size mismatch: got " ~ size(surfaceDef.uKnots) ~ 
                        ", expected " ~ expectedUKnots ~ 
                        " (uDegree=" ~ uDegree ~ " + numCPsU=" ~ numCPsU ~ " + 1)");
    }
    if (size(surfaceDef.vKnots) != expectedVKnots)
    {
        throw regenError("vKnots size mismatch: got " ~ size(surfaceDef.vKnots) ~ 
                        ", expected " ~ expectedVKnots ~ 
                        " (vDegree=" ~ vDegree ~ " + numCPsV=" ~ numCPsV ~ " + 1)");
    }
    
    // ========================================================================
    // STEP 4: Handle rational/weights
    // ========================================================================
    
    var isRational = surfaceDef.isRational;
    
    // Default to false if not specified
    if (isRational == undefined)
    {
        isRational = false;
        surfaceDef.isRational = false;
    }
    
    if (isRational)
    {
        // Rational surface requires weights
        if (surfaceDef.weights == undefined)
        {
            throw regenError("isRational=true but no weights provided");
        }
        
        // Validate weights dimensions
        var weightsRows = size(surfaceDef.weights);
        var weightsCols = size(surfaceDef.weights[0]);
        
        if (weightsRows != numCPsU || weightsCols != numCPsV)
        {
            throw regenError("Weights grid dimensions (" ~ weightsRows ~ "x" ~ weightsCols ~ 
                            ") don't match control points (" ~ numCPsU ~ "x" ~ numCPsV ~ ")");
        }
        
        // Wrap in matrix() if needed
        if (!(surfaceDef.weights is Matrix))
        {
            surfaceDef.weights = matrix(surfaceDef.weights);
        }
    }
    else
    {
        // Non-rational: if weights provided, verify they're all 1.0 or remove them
        // bSplineSurface doesn't need weights for non-rational surfaces
        if (surfaceDef.weights != undefined)
        {
            // Could validate all weights == 1.0 here, but simpler to just remove
            // since isRational=false means weights are ignored anyway
            surfaceDef.weights = undefined;
        }
    }
    
    // ========================================================================
    // STEP 5: Handle periodic flags
    // ========================================================================
    
    if (surfaceDef.isUPeriodic == undefined)
    {
        surfaceDef.isUPeriodic = false;
    }
    if (surfaceDef.isVPeriodic == undefined)
    {
        surfaceDef.isVPeriodic = false;
    }
    
    // ========================================================================
    // STEP 6: Final summary (optional debug - can be removed once stable)
    // ========================================================================
    /*
    println("=== NORMALIZED SURFACE DEF ===");
    println("  CP grid: " ~ numCPsV ~ " x " ~ numCPsU);
    println("  Degrees: u=" ~ uDegree ~ ", v=" ~ vDegree);
    println("  Knots: u=" ~ size(surfaceDef.uKnots) ~ ", v=" ~ size(surfaceDef.vKnots));
    println("  Rational: " ~ isRational);
    println("  Periodic: u=" ~ surfaceDef.isUPeriodic ~ ", v=" ~ surfaceDef.isVPeriodic);
    */
    
    return surfaceDef;
}

/**
 * Extract a row from a surface (constant v, varying u).
 * Returns array of numU control points.
 */
function extractSurfaceRow(surface is BSplineSurface, vIndex is number) returns array
{
    var numU = size(surface.controlPoints);
    var rowPoints = [];
    for (var u = 0; u < numU; u += 1)
    {
        rowPoints = append(rowPoints, surface.controlPoints[u][vIndex]);
    }
    return rowPoints;
}

/**
 * Extract row weights (constant v, varying u).
 */
function extractSurfaceRowWeights(surface is BSplineSurface, vIndex is number) returns array
{
    if (!surface.isRational || surface.weights == undefined)
    {
        return undefined;
    }
    var numU = size(surface.controlPoints);
    var rowWeights = [];
    for (var u = 0; u < numU; u += 1)
    {
        rowWeights = append(rowWeights, surface.weights[u][vIndex]);
    }
    return rowWeights;
}

/**
 * Extract a column from a surface (constant u, varying v).
 * Returns array of numV control points.
 */
function extractSurfaceColumn(surface is BSplineSurface, uIndex is number) returns array
{
    // controlPoints[u] is already the full column
    return surface.controlPoints[uIndex];
}

/**
 * Extract column weights (constant u, varying v).
 */
function extractSurfaceColumnWeights(surface is BSplineSurface, uIndex is number) returns array
{
    if (!surface.isRational || surface.weights == undefined)
    {
        return undefined;
    }
    return surface.weights[uIndex];
}

/**
 * Build a curve from a surface row (for u-direction operations).
 */
function surfaceRowToCurve(surface is BSplineSurface, vIndex is number) returns BSplineCurve
{
    
    var curveDef = {
        "degree" : surface.uDegree,
        "isPeriodic" : surface.isUPeriodic,
        "isRational" : surface.isRational,
        "controlPoints" : extractSurfaceRow(surface, vIndex),
        "knots" : surface.uKnots
    };
    
    // Only include weights if rational
    if (surface.isRational && surface.weights != undefined)
    {
        curveDef.weights = extractSurfaceRowWeights(surface, vIndex);
    }
    
    return bSplineCurve(curveDef);
}

/**
 * Build a curve from a surface column (for v-direction operations).
 */
function surfaceColumnToCurve(surface is BSplineSurface, uIndex is number) returns BSplineCurve
{
    var curveDef = {
        "degree" : surface.vDegree,
        "isPeriodic" : surface.isVPeriodic,
        "isRational" : surface.isRational,
        "controlPoints" : extractSurfaceColumn(surface, uIndex),
        "knots" : surface.vKnots
    };
    
    // Only include weights if rational
    if (surface.isRational && surface.weights != undefined)
    {
        curveDef.weights = extractSurfaceColumnWeights(surface, uIndex);
    }
    
    return bSplineCurve(curveDef);
}

