// Written for Elevate Outdoor Collective by Jed Yeiser.
// Needs to address complex surfacing needs - pecifically snowboard binding surfacing. 
// Extensive background and theory from Piegl and Tiller 'The NURBS Book'
// 1/15/2026



// References: Piegl & Tiller "The NURBS Book" 2nd Ed.
//   - Algorithm A2.1: FindSpan
//   - Algorithm A5.1: CurveKnotIns - Boehm's knot insertion
//   - Equation 5.15 - alpha calculation for control point interpolation
// P&T Eq. 5.15: α_i = (u_bar - u_i) / (u_{i+p} - u_i)
// where u_bar is the new knot, p is degree

FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

import(path : "8d3469cb403ed076f2e3fbea", version : "c948bae217af15aa2557cdf4");
import(path : "07dbc6069778f180fe33c140", version : "659c032b4a4a9949527be840");

annotation { "Feature Type Name" : "makeCurvesCompitable", "Feature Type Description" : "" }
export const makeCompatableCurves = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Edges to make compatible", "Filter" : EntityType.EDGE }
        definition.selEdges is Query;
        
        annotation { "Name" : "Print Report?" }
        definition.printReport is boolean;
        
        annotation { "Name" : "Create wire?" }
        definition.createWire is boolean;
        
        
        
    }
    {
        var edgeArray = evaluateQuery(context, qUnion([definition.selEdges]));
        
        var bSplineArray = mapArray(edgeArray, function(x) { return evApproximateBSplineCurve(context, {"edge" : x});});
        
        if (definition.printReport)
        {
            var before = compatibilityReport(bSplineArray);
            println(' ------ THE BEFORE ------ ');
            println(before);
        }
        
        var compatible = makeCurvesCompatible(context, id + 'curveCompatiblityLaunch', bSplineArray);
        
        if (definition.printReport)
        {
            var after = compatibilityReport(compatible);
            println('*********** THE AFTER ***********');
            println(after);
        }
        
        if (definition.createWire)
        {
            for (var i = 0; i < size(compatible); i += 1)
            {
                opCreateBSplineCurve(context, id + "compatibleBSpline_" ~ i, {
                        "bSplineCurve" : compatible[i]
                });
                
            }
            
        }
        
        
        
    });

/**
 * Analyze an array of B-spline curves and report on their compatibility.
 * Useful for debugging and verification.
 *
 * @param curves {array} : Array of BSplineCurve
 * @returns {map} : Compatibility report
 */
export function compatibilityReport(curves is array) returns map
{
    if (size(curves) == 0)
    {
        return { "error" : "No curves provided" };
    }
    
    var deg = [];
    var knotCounts = [];
    var cpCounts = [];
    var interiorKnotCounts = [];
    var isRational = [];
    
    for (var i = 0; i < size(curves); i += 1)
    {
        var curve = curves[i];
        deg = append(deg, curve.degree);
        knotCounts = append(knotCounts, size(curve.knots));
        cpCounts = append(cpCounts, size(curve.controlPoints));
        interiorKnotCounts = append(interiorKnotCounts, size(getInteriorKnots(curve)));
        isRational = append(isRational, curve.isRational);
    }
    
    // Check if all values match
    var degreesMatch = allEqual(deg);
    var knotsMatch = allEqual(knotCounts);
    var cpsMatch = allEqual(cpCounts);
    
    var isCompatible = degreesMatch && knotsMatch && cpsMatch;
    
    return {
        "numCurves" : size(curves),
        "isCompatible" : isCompatible,
        
        "degrees" : deg,
        "degreesMatch" : degreesMatch,
        "maxDegree" : max(deg),
        "minDegree" : min(deg),
        
        "controlPointCounts" : cpCounts,
        "cpsMatch" : cpsMatch,
        
        "knotCounts" : knotCounts,
        "knotsMatch" : knotsMatch,
        
        "interiorKnotCounts" : interiorKnotCounts,
        
        "isRational" : isRational,
        "anyRational" : any(isRational)
    };
}

function allEqual(arr is array) returns boolean
{
    if (size(arr) < 2) return true;
    
    var first = arr[0];
    for (var i = 1; i < size(arr); i += 1)
    {
        if (arr[i] != first) return false;
    }
    return true;
}



/**
 * Extract interior knots from a clamped B-spline knot vector.
 * Interior knots are everything between the clamped ends.
 * 
 * For knots [0,0,0,0, 0.3, 0.5, 0.7, 1,1,1,1] with degree 3:
 * Returns [0.3, 0.5, 0.7]
 */
export function getInteriorKnots(bspline is BSplineCurve) returns array
{
    var degree = bspline.degree;
    var knots = bspline.knots;
    
    var startIndex = degree + 1;
    var endIndex = size(knots) - degree - 1;
    
    if (endIndex <= startIndex)
    {
        return [];  // Bezier curve, no interior knots
    }
    
    var interior = [];
    for (var i = startIndex; i < endIndex; i += 1)
    {
        interior = append(interior, knots[i]);
    }
    
    return interior;
}

/**
 * Merge two arrays of interior knots, preserving maximum multiplicities.
 * Both input arrays should be sorted ascending.
 * Returns sorted array with all unique knots.
 */
export function mergeKnotVectors(knotsA is array, knotsB is array, tolerance is number) returns array
{
    if (size(knotsA) == 0) return knotsB;
    if (size(knotsB) == 0) return knotsA;
    
    // Count multiplicities in A
    var mapA = {};
    for (var knot in knotsA)
    {
        var key = round(knot / tolerance) * tolerance;
        mapA[key] = (mapA[key] == undefined) ? 1 : mapA[key] + 1;
    }
    
    // Count multiplicities in B
    var mapB = {};
    for (var knot in knotsB)
    {
        var key = round(knot / tolerance) * tolerance;
        mapB[key] = (mapB[key] == undefined) ? 1 : mapB[key] + 1;
    }
    
    // Merge taking MAX multiplicity
    var merged = {};
    for (var key, mult in mapA)
    {
        merged[key] = mult;
    }
    for (var key, mult in mapB)
    {
        if (merged[key] == undefined || mult > merged[key])
        {
            merged[key] = mult;
        }
    }
    
    // Convert to sorted array
    var keys = [];
    for (var key, value in merged)
    {
        keys = append(keys, key);
    }
    keys = sort(keys, function(a, b) { return a - b; });
    
    var result = [];
    for (var key in keys)
    {
        for (var i = 0; i < merged[key]; i += 1)
        {
            result = append(result, key);
        }
    }
    
    return result;
}



/**
 * Make an array of B-spline curves compatible (same degree, same knot vector).
 * After this, all curves have identical structure and can be used for skinning.
 *
 * @param curves {array} : Array of BSplineCurve
 * @returns {array} : Array of compatible BSplineCurve (same size as input)
 */
export function makeCurvesCompatible(context is Context, id is Id, curves is array) returns array
{
    if (size(curves) < 2)
    {
        return curves;
    }
    
    const tolerance = 1e-10;
    
    // Step 1: Find max degree
    var maxDegree = 0;
    for (var curve in curves)
    {
        if (curve.degree > maxDegree)
        {
            maxDegree = curve.degree;
        }
    }
    
    // Step 2: Elevate all curves to max degree
    var elevated = [];
    for (var curve in curves)
    {
        if (curve.degree < maxDegree)
        {

            elevated = append(elevated, elevate(context, id + 'elevating1', curve, maxDegree));
        }
        else
        {
            elevated = append(elevated, curve);
        }
    }
    
    // Step 3: Gather and merge all interior knots
    var mergedInterior = [];
    for (var curve in elevated)
    {
        var interior = getInteriorKnots(curve);
        mergedInterior = mergeKnotVectors(mergedInterior, interior, tolerance);
    }
    
    // Step 4: Insert missing knots into each curve
    var compatible = [];
    for (var curve in elevated)
    {
        var myInterior = getInteriorKnots(curve);
        var toInsert = getKnotsToInsert(myInterior, mergedInterior, tolerance);
        
        if (size(toInsert) > 0)
        {
            compatible = append(compatible, refineKnotVector(context, curve, toInsert));
        }
        else
        {
            compatible = append(compatible, curve);
        }
    }
    
    return compatible;
}

/**
 * Find knots in target that aren't in current (or have lower multiplicity).
 */
function getKnotsToInsert(currentInterior is array, targetInterior is array, tolerance is number) returns array
{
    // Count multiplicities in current
    var currentMap = {};
    for (var knot in currentInterior)
    {
        var key = round(knot / tolerance) * tolerance;
        currentMap[key] = (currentMap[key] == undefined) ? 1 : currentMap[key] + 1;
    }
    
    // Count multiplicities in target
    var targetMap = {};
    for (var knot in targetInterior)
    {
        var key = round(knot / tolerance) * tolerance;
        targetMap[key] = (targetMap[key] == undefined) ? 1 : targetMap[key] + 1;
    }
    
    // Find what's missing
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
    
    // Sort ascending (required by refineKnotVector)
    return sort(toInsert, function(a, b) { return a - b; });
}
