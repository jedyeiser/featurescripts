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

export import(path : "050a4670bd42b2ca8da04540", version : "310acbe540c302e20097f554");

/**
 * Given a query for a curve and a knot parameter, return a new BSplineCurve (and optionally a query for
 * a wire body made with the BSplineCurve definition) with a new knot inserted at the parameter speciied. 
 * 
 * Implementing Boehm's knot insertion as covered in Piegl & Tiller Chapter 5
 * 
 * @param arg {{
 *      @field targetEdge{Query} : The edge to modify.
 *      @field createWire{boolean} : If true, feature will create a wire with the BSplineCurve object and return a query for the body  @optional
 *      @field knotParam{number} : Parameter at which to insert the knot
 *      
 * }}
 */


annotation { "Feature Type Name" : "insertSingleKnot", "Feature Type Description" : "Inserts a single knot into a single bspline curve without changing shape using Boehm's algorighm" }
export const insertSingleKnot = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Target edge or wire", "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 1 }
        definition.targetEdge is Query;
        
        annotation { "Name" : "New knot parameter"}
        isReal(definition.knotParam, paramBounds);
        
        annotation { "Name" : "Create Wire?", "Default" : false, "Description" : "create wire?" }
        definition.createWire is boolean;
        
        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Show new knot" }
            definition.showParamPoint is boolean;
            
            annotation { "Name" : "Show control points" }
            definition.showControlPoints is boolean;
            
            annotation { "Name" : "Show polygon" }
            definition.showPolygon is boolean;
            
            annotation { "Name" : "Show knots" }
            definition.showKnots is boolean;
            
            annotation { "Name": "Show endpoints", "Default": false, "Description": "Show endpoints in debug entities. Parameter 0 is shown in GREEN, parameter 1 is shown in RED"}
            definition.showEndpoints is boolean;
        }
        
        
    }
    {
        var edgeBSpline = evApproximateBSplineCurve(context, {
                "edge" : definition.targetEdge
        });
        
        var retBSpline = insertKnot(context, edgeBSpline, definition.knotParam);
        
        debugRoutine(context, definition, edgeBSpline);
        
        
        if (definition.createWire)
        {
            opCreateBSplineCurve(context, id + "newKnotBSpline", {
                    "bSplineCurve" : retBSpline
            });
            
            return {'query': qCreatedBy(id + "newKnotBSpline", EntityType.BODY), 'bspline': retBSpline};
        }
        else
        {
            return {'bspline': retBSpline};
        }
        
        
    });
    
annotation { "Feature Type Name" : "insertMultipleKnots", "Feature Type Description" : "" }
export const insertMultipleKnots = defineFeature(function(context is Context, id is Id, definition is map) returns map
    precondition
    {
        annotation { "Name" : "Target edge or wire", "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 1 }
        definition.targetEdge is Query;
        
        annotation { "Name" : "Start param"}
        isReal(definition.startParam, paramBounds);
        
        annotation { "Name" : "End param"}
        isReal(definition.endParam, paramBounds);
        
        annotation { "Name" : "Points to insert" }
        isInteger(definition.numNewPoints, POSITIVE_COUNT_BOUNDS);
        
        annotation { "Name" : "Create Wire?", "Default" : false, "Description" : "create wire?" }
        definition.createWire is boolean;
    }
    {
     
     var bSpline = evApproximateBSplineCurve(context, {
             "edge" : definition.targetEdge
     });
     
     var updatedBSpline = refineKnotVector(context, bSpline, range(definition.startParam, definition.endParam, definition.numNewPoints));
     
     var retMap = {'bspline': updatedBSpline as BSplineCurve};
     
     if (definition.createWire)
     {
         opCreateBSplineCurve(context, id + "knottifiedBspline", {
                 "bSplineCurve" : retMap['bspline']
         });
         
         retMap['query'] = qCreatedBy(id + "knottifiedBspline", EntityType.BODY);
     }
     
     return retMap;
        
    });

    

export function insertKnot(context is Context, bSpline is BSplineCurve, knotParam is number) returns BSplineCurve
{
         //get data on curve
        var degree = bSpline.degree; // spline degree --> (cubic, quartic, etc, generally a number). Describes how the curves moves between points. One less than the number of control points per knot segment
        var oldPoints = bSpline.controlPoints; // control points
        var oldKnots = bSpline.knots; // knot vector. In general we're always going to be looking at 'clamped' bSplines, which have n repeated knots (n = degree) on either end of the knot vector. Must be
                                            //monotonically increasing. Specifies continuity and 'parameter space divisions' between knot segments. 
        var n = size(oldPoints) - 1; 
        
        // Find knot span k where oldKnots[k] <= newKnot < oldKnots[k+1]
        // This is Algorithm A2.1 "FindSpan" (P&T p.68)
        //
        // We start at k = degree because the first (degree+1) knots are clamped at 0
        // (they don't define interior spans).
        //
        // The condition "k < size(oldKnots) - degree - 2" keeps us from running past
        // the last valid interior span (the final degree+1 knots are clamped at 1).
        //
        // We increment k while the NEXT knot is still <= our target parameter.
        // When we exit, oldKnots[k] <= knotParam < oldKnots[k+1]
        
        var k = degree;
        while (k < size(oldKnots) - degree - 2 && oldKnots[k+1] <= knotParam) {  
            k += 1; // trying a new way of doing this.    
        }
        ///// POINTS --- > 
        var newPoints = makeArray(n + 2);
        
        for (var i = 0; i <= n + 1; i += 1)
        {
            if (i <= k - degree)
            {
                //before we hit our param
                newPoints[i] = oldPoints[i];
            }
            else if (i > k)
            {
                //after insertion. shift index. 
                newPoints[i] = oldPoints[i -1];
            }
            else
            {
                // at insertion
                var alpha = (knotParam - oldKnots[i]) / (oldKnots[i + degree] - oldKnots[i]);
                newPoints[i] = (1- alpha) * oldPoints[i - 1] + alpha * oldPoints[i];
            }
        }
        
        // insert new knot at position k + 1
        ////// KNOTS --->
        var newKnots = makeArray(size(oldKnots) + 1);
        for (var i = 0; i <= k; i += 1)
        {
            newKnots[i] = oldKnots[i];
        }
        newKnots[k+1] = knotParam;
        for (var i = k + 1; i < size(oldKnots); i += 1)
        {
            newKnots[i + 1] = oldKnots[i];
        }
        
        
        ///// WEIGHTS --->
        var newWeights = undefined;
        if (bSpline.isRational && bSpline.weights != undefined) 
        {
            var oldWeights = bSpline.weights;
            newWeights = makeArray(n + 2);
            
            for (var i = 0; i <= n + 1; i += 1)
            {
                if (i <= k - degree)
                {
                    newWeights[i] = oldWeights[i];
                }
                else if (i > k)
                {
                    newWeights[i] = oldWeights[i - 1];
                }
                else
                {
                    var alpha = (knotParam - oldKnots[i]) / (oldKnots[i + degree] - oldKnots[i]);
                    newWeights[i] = (1 - alpha) * oldWeights[i - 1] + alpha * oldWeights[i];
                }
            }
        }
        else
        {
            newWeights = makeArray(n + 2, 1.0);
        }
        
        const retBspline = {
                "degree": degree,
                "isPeriodic": bSpline.isPeriodic,
                "controlPoints": newPoints,
                "knots": newKnots,
                "weights": newWeights,
                "isRational": bSpline.isRational,
                "dimension" : bSpline.dimension
                };
        return retBspline as BSplineCurve;
}

/**
 * Insert multiple knots into a B-spline curve in a single pass.
 * P&T Algorithm A5.4 "RefineKnotVectCurve" (p.164-165)
 *
 * @param bspline {BSplineCurve} : BSplineCurve definition
 * @param knotsToInsert {array} : Knot values to insert (must be sorted ascending, 
 *                                 values in valid range, respecting max multiplicity)
 */
 
 
 export function refineKnotVector(context is Context, bSpline is BSplineCurve, knotsToInsert is array) returns BSplineCurve
 {
     if (size(knotsToInsert) == 0)
     {
         return bSpline;
     }
     
     
     var p = bSpline.degree;
     var U = bSpline.knots;
     var Pw = bSpline.controlPoints;
     var weights = bSpline.weights;
     var isRational = bSpline.isRational && weights != undefined;
     
     const tolerance = 1e-8;
     var cleanedParams = sanitizeKnotsToInsert(U, knotsToInsert, p, tolerance);
     
     if(size(cleanedParams) == 0)
     {
         return bSpline;
     }
     
     var n = size(Pw) - 1; // Original n+1 control points
     var m = size(U) - 1; // Original m+1 knots
     var X = cleanedParams;
     var r = size(X) - 1; // r+1 knots to insert. 
     
     var newNumCPs = n + r + 2;  //n + 1 + r + 1 control points
     var newNumKnots = m + r + 2; //m + 1 + r + 1 knots
     
     //find spans of first and last knots to insert
     var a = findSpan(p, X[0], U);
     var b = findSpan(p, X[r], U) + 1;
     
     var Qw = makeArray(newNumCPs);
     var Ubar = makeArray(newNumKnots);
     var newWeights = makeArray(newNumCPs);
     
     //copy unchanged points
     for (var j = 0; j <= a - p; j += 1)
     {
         Qw[j] = Pw[j];
         newWeights[j] = isRational ? weights[j] : 1;
     }
     
    // Copy unaffected control points at END
    for (var j = b - 1; j <= n; j += 1)
    {
        Qw[j + r + 1] = Pw[j];
        newWeights[j + r + 1] = isRational ? weights[j] : 1.0;
    }
     
     // Copy unaffected knots at start
    for (var j = 0; j <= a; j += 1)
    {
        Ubar[j] = U[j];
    }
    
    // Copy unaffected knots at end
    for (var j = b + p; j <= m; j += 1)
    {
        Ubar[j + r + 1] = U[j];
    }
    
    //prepare for main loop
    var i = b + p - 1;
    var k = b + p + r;
    
    for (var j = r; j >= 0; j -= 1)
    {
        while (X[j] <= U[i] && i > a) //while the knot vector at j is less than the ith original knot (and we haven't hit the last knot yet)
        {
            Qw[k - p - 1] = Pw[i -p - 1];
            newWeights[k - p - 1] = isRational ? weights[i - p - 1] : 1;
            Ubar[k] = U[i];
            k -= 1;
            i -= 1;
        }
        
        //copy last computed point
        Qw[k - p - 1] = Qw[k - p];
        newWeights[k - p - 1] = newWeights[k - p];
        
        //compute new control points for this knot insertion
        for (var l = 1; l <= p; l += 1)
        {
            var ind = k - p + l;
            var alpha = Ubar[k+l] - X[j];
            
            if (abs(alpha) < 1e-14)
            {
                Qw[ind - 1] = Qw[ind];
                newWeights[ind - 1] = newWeights[ind];
            }
            else
            {
                alpha = alpha / (Ubar[k + l] - U[i - p + l]);
                Qw[ind - 1] = alpha * Qw[ind - 1] + (1 - alpha) * Qw[ind]; 
                newWeights[ind - 1] = alpha * newWeights[ind - 1] + (1 - alpha) * newWeights[ind];
                
            }
        }
        
        Ubar[k] = X[j];
        k -= 1;
    }
    const retBSpline = {
            "degree" : p,
            "isPeriodic" : bSpline.isPeriodic,
            "controlPoints" : Qw,
            "knots" : Ubar,
            "weights" : newWeights,
            "isRational" : bSpline.isRational,
            "dimension" : bSpline.dimension
            };
            
    return retBSpline as BSplineCurve;
     
 }
 
/**
 * Filter and validate knots before insertion.
 * Removes duplicates and knots that would exceed max multiplicity.
 * Returns sorted array of valid knots to insert.
 */
export function sanitizeKnotsToInsert(existingKnots is array, knotsToInsert is array, degree is number, tolerance is number) returns array
{
    if (size(knotsToInsert) == 0)
    {
        return [];
    }
    
    // Sort ascending
    var sorted = sort(knotsToInsert, function(a, b) { return a - b; });
    
    // Count multiplicities in existing knot vector
    var multiplicities = {};  // knot value -> count
    for (var knot in existingKnots)
    {
        var key = roundToTolerance(knot, tolerance);
        if (multiplicities[key] == undefined)
        {
            multiplicities[key] = 1;
        }
        else
        {
            multiplicities[key] += 1;
        }
    }
    
    // Filter: keep only knots that won't exceed max multiplicity
    var valid = [];
    for (var knot in sorted)
    {
        var key = roundToTolerance(knot, tolerance);
        var currentMult = multiplicities[key];
        
        if (currentMult == undefined)
        {
            currentMult = 0;
        }
        
        if (currentMult < degree)  // Can still insert
        {
            valid = append(valid, knot);
            multiplicities[key] = currentMult + 1;
        }
        // else: skip, would exceed multiplicity
    }
    
    return valid;
}

function roundToTolerance(value is number, tolerance is number) returns number
{
    return round(value / tolerance) * tolerance;
}
 
 
 
/**
 * Find knot span index. P&T Algorithm A2.1 (p.68)
 */
export function findSpan(degree is number, u is number, knots is array) returns number
{
    var n = size(knots) - degree - 2;  // Last valid index for control points
    
    // Special case: u at end of knot vector
    if (u >= knots[n + 1])
    {
        return n;
    }
    
    // Binary search
    var low = degree;
    var high = n + 1;
    var mid = floor((low + high) / 2);
    
    while (u < knots[mid] || u >= knots[mid + 1])
    {
        if (u < knots[mid])
        {
            high = mid;
        }
        else
        {
            low = mid;
        }
        mid = floor((low + high) / 2);
    }
    
    return mid;
}

    
export function debugRoutine(context is Context, definition is map, edgeBSpline is BSplineCurve)
{
    if (definition.showParamPoint)
    {
        showParamOnCurve(context, definition.targetEdge, definition.knotParam, DebugColor.RED);
    }
    if (definition.showPolygon)
    {
        showPolyline(context, edgeBSpline, DebugColor.MAGENTA);
    }
    if (definition.showControlPoints)
    {
        addDebugPoints(context, edgeBSpline.controlPoints, DebugColor.CYAN);
    }
    if (definition.showKnots)
    {
        var knotLines = evEdgeTangentLines(context, {
                "edge" : definition.targetEdge,
                "parameters" : edgeBSpline.knots
        });
        var knotPoints = mapArray(knotLines, function(x) {return x.origin;});
        addDebugPoints(context, knotPoints, DebugColor.BLACK);
    }
    if (definition.showEndpoints)
    {
        var endLines = evEdgeTangentLines(context, {
                "edge" : definition.targetEdge,
                "parameters" : [0, 1]
        });
        addDebugPoint(context, endLines[0].origin, DebugColor.GREEN);
        addDebugPoint(context, endLines[1].origin, DebugColor.RED);
    }
}
    
export function showParamOnCurve(context is Context, curveQuery is Query, param is number, debugColor is DebugColor)
{
    var point = evEdgeTangentLine(context, {
            "edge" : curveQuery,
            "parameter" : param
    }).origin;
    
    addDebugPoint(context, point, debugColor);
}


export function addDebugPoints(context is Context, pointArray is array, debugColor is DebugColor)
{
    for (var i = 0; i < size(pointArray); i += 1)
    {
        addDebugPoint(context, pointArray[i], debugColor);
    }
}
    
    
export function showPolyline(context is Context, bspline is map, debugColor is DebugColor)
{
    for (var i = 0; i < size(bspline.controlPoints) - 1; i += 1)
    {
        if (tolerantEquals(bspline.controlPoints[i], bspline.controlPoints[i + 1]))
        {
            continue;
        }
        addDebugLine(context, bspline.controlPoints[i], bspline.controlPoints[i + 1], debugColor);
    }
    if (bspline.isPeriodic && !firstAndLastCPShouldOverlap(bspline))
    {
        addDebugLine(context, bspline.controlPoints[size(bspline.controlPoints) - 1], bspline.controlPoints[0], debugColor);
    }
}

function firstAndLastCPShouldOverlap(bspline is map) returns boolean
{
    return bspline.isPeriodic && bspline.knots[0] == 0;
}