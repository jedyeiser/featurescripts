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

FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

//import tools/bspline_knots
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/dadb70c0a762573622fa609c", version : "2267a758e66498ac49f4601e");
//import tools/bspline_data (for getInteriorKnots)
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/b1c7f2116fb64e6b40bf53f4", version : "4fe0cca8e00a4cd812896a8c");

//import curve_operations
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/a7403d5f7f5a4fef8225b768", version : "e5b9e00c5a237415c89a66b7");
//import gordon_knot_ops

const COMPAT_DEBUG = false;

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
 * Make an array of B-spline curves compatible (same degree, same knot vector).
 * This is a wrapper around the tools library functions to handle arrays.
 *
 * @param context {Context}
 * @param id {Id}
 * @param curves {array} : Array of BSplineCurve
 * @returns {array} : Array of compatible BSplineCurve (same size as input)
 */
export function makeCurvesCompatible(context is Context, id is Id, curves is array) returns array
{
    if (size(curves) < 2)
    {
        return curves;
    }

    const tolerance = 1e-7;

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
            elevated = append(elevated, elevateDegree(context, curve, maxDegree - curve.degree));
        }
        else
        {
            elevated = append(elevated, curve);
        }
    }

    // Step 2 debug: print each curve's state after elevation
    if (COMPAT_DEBUG)
    {
        for (var i = 0; i < size(elevated); i += 1)
        {
            println("  [compat] Curve " ~ i ~ " after elevation: degree=" ~ elevated[i].degree ~
                    " CPs=" ~ size(elevated[i].controlPoints) ~
                    " interior=" ~ getInteriorKnots(elevated[i]));
        }
    }

    // Step 3: Build max-multiplicity union of all interior knots.
    // Uses getKnotsToInsert instead of mergeKnotVectors to avoid floating-point
    // map-key issues that cause mergeKnotVectors to return empty results.
    var mergedInterior = [];
    for (var curve in elevated)
    {
        var interior = getInteriorKnots(curve);
        // Add what's in interior but not yet represented in mergedInterior
        var toAdd = getKnotsToInsert(mergedInterior, interior, tolerance);
        for (var k in toAdd)
            mergedInterior = append(mergedInterior, k);
        mergedInterior = sort(mergedInterior, function(a, b) { return a - b; });
    }

    if (COMPAT_DEBUG)
    {
        println("  [compat] mergedInterior (" ~ size(mergedInterior) ~ " entries): " ~ mergedInterior);
    }

    // Step 4: Insert missing knots into each curve
    var compatible = [];
    for (var i = 0; i < size(elevated); i += 1)
    {
        var curve = elevated[i];
        var myInterior = getInteriorKnots(curve);
        var toInsert = getKnotsToInsert(myInterior, mergedInterior, tolerance);

        if (COMPAT_DEBUG)
        {
            println("  [compat] Curve " ~ i ~ " toInsert=" ~ toInsert ~ " CPs before=" ~ size(curve.controlPoints));
        }

        if (size(toInsert) > 0)
        {
            compatible = append(compatible, refineKnotVector(context, curve, toInsert));
        }
        else
        {
            compatible = append(compatible, curve);
        }

        if (COMPAT_DEBUG)
        {
            println("  [compat] Curve " ~ i ~ " CPs after=" ~ size(compatible[i].controlPoints));
        }
    }

    return compatible;
}

/**
 * Find knots in target that aren't in current (or have lower multiplicity).
 * Uses a consume-and-match loop to avoid floating-point map-key hashing issues.
 */
function getKnotsToInsert(currentInterior is array, targetInterior is array, tolerance is number) returns array
{
    // Work through targetInterior; consume matching entries from currentInterior one-by-one.
    // This correctly handles multiplicities without floating-point key hashing.
    var remaining = currentInterior;
    var toInsert = [];

    for (var ti = 0; ti < size(targetInterior); ti += 1)
    {
        var targetKnot = targetInterior[ti];
        var matchIdx = -1;

        for (var ri = 0; ri < size(remaining); ri += 1)
        {
            if (abs(targetKnot - remaining[ri]) <= tolerance)
            {
                matchIdx = ri;
                break;
            }
        }

        if (matchIdx >= 0)
        {
            // Consume this entry so multiplicity is respected
            var newRemaining = [];
            for (var ri = 0; ri < size(remaining); ri += 1)
            {
                if (ri != matchIdx)
                    newRemaining = append(newRemaining, remaining[ri]);
            }
            remaining = newRemaining;
        }
        else
        {
            // Not present in current — needs insertion
            toInsert = append(toInsert, targetKnot);
        }
    }

    // Sort ascending (required by refineKnotVector)
    return sort(toInsert, function(a, b) { return a - b; });
}