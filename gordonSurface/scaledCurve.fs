FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");



export import(path : "050a4670bd42b2ca8da04540", version : "310acbe540c302e20097f554");



annotation { "Feature Type Name" : "Scaled Curve", "Feature Type Description" : "Creates a new BSplineCurve as a scaled combination of the two input curves" }
export const createScaledCurve = defineFeature(function(context is Context, id is Id, definition is map) returns map
    precondition
    {
        annotation { "Name" : "Group 0", "Filter" : EntityType.EDGE}
        definition.group0 is Query;
        
        annotation { "Name" : "Flip?", "Defualt" : false, "UIHint": UIHint.OPPOSITE_DIRECTION, "Description": "Flip evaluation order of Group0" }
        definition.flip is boolean;
        
        annotation { "Name" : "Group 1", "Filter" : EntityType.EDGE}
        definition.group1 is Query;
        
        
        annotation { "Name" : "Initial curve scalefactor", "Description": "Where scaled curve should sit in [-0.5, 0.5] between Group 0 and Group 1. -.5 -> 100% Group 0. +.5 -> 100% Group 1"}
        isReal(definition.sf0, ScaledCurveParameterBounds);
        
        annotation { "Name" : "Final curve scalefactor", "Description": "Where scaled curve should sit in [-0.5, 0.5] between Group 0 and Group 1. -.5 -> 100% Group 0. +.5 -> 100% Group 1"}
        isReal(definition.sf1, ScaledCurveParameterBounds);
        
        annotation { "Name" : "Transition Type", "Default": TransitionType.LINEAR, "Description" : "How to transition from one scalefactor to another along our scaled curve" }
        definition.transitionType is TransitionType;
        
        annotation { "Name" : "Create curve?", "Default": false, "Description": "When true, creates a curve. Otherwise, just solves for the BSplineCurve" }
        definition.createCurve is boolean;
        
        if (definition.createCurve)
        {
            annotation { "Name" : "Curve name", "Description": "When not blank, the output wire body will get this name." }
            definition.curveName is string;
            
        }
        
        annotation { "Group Name" : "Debug & Details", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Show endpoints", "Description": "When true, shows spline startpoints in GREEN and endpoints in RED", "Default": false }
            definition.showEndpoints is boolean;
            
            annotation { "Name" : "Show curves", "Description": "When true, shows Group 0 in CYAN and Group 1 in MAGENTA", "Default": false }
            definition.showGroups is boolean;
            
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
                    annotation { "Name" : "Scaled samples",  "Description": "Number of samples to use along our output curve. The control point count will be less than or equal to this value" }
                    isInteger(definition.numScaledSamples, SampleCountBounds);
                
                    annotation { "Name" : "Scaled tolerance", "Description": "Fit tolerance for output curve. Fits sampled data to within this tolerance. Onshape minimum of 1e-8 meter or 1e-5 millimeter"  }
                    isLength(definition.scaledTol, FitToleranceBounds); 
                
                    annotation { "Name" : "Scaled curve degree", "Description": "Degree of scaled curve" }
                    isInteger(definition.scaledDegree, curveDegreeBounds);
                }
                
                 
                annotation { "Group Name" : "Input parameters", "Collapsed By Default" : true }
                {
                    annotation { "Name" : "Number of samples along Group 0",  "Description": "Number of samples to use along Group 0 when creating a unified BSplineCurve. The control point count will be less than or equal to this value" }
                    isInteger(definition.group0SampleCount, SampleCountBounds);
                
                    annotation { "Name" : "Group 0 fit tolerance", "Description": "Fit tolerance for Group 0. Onshape minimum of 1e-8 meter or 1e-5 millimeter"  }
                    isLength(definition.group0Tol, FitToleranceBounds); 
                
                    annotation { "Name" : "Number of samples along Group 1", "Description": "Number of samples to use along Group 1 when creating a unified BSplineCurve. The control point count will be less than or equal to this value"  }
                    isInteger(definition.group1SampleCount, SampleCountBounds);
                
                    annotation { "Name" : "Group 1 fit tolerance", "Description": "Fit tolerance for Group 0. Onshape minimum of 1e-8 meter or 1e-5 millimeter" }
                    isLength(definition.group1Tol, FitToleranceBounds); 
                }
                
                
            }
            
            
        }
        
    }
    {
        const shiftedScalefactors = [0.5 + definition.sf0, 0.5 + definition.sf1];
        var group0_arr = evaluateQuery(context, qUnion([definition.group0]));
        var group1_arr = evaluateQuery(context, qUnion([definition.group1]));
        
        var bSpline0_arr = mapArray(group0_arr, function(x) {return evApproximateBSplineCurve(context, { "edge" : x } ); });
        var bSpline1_arr = mapArray(group1_arr, function(x) {return evApproximateBSplineCurve(context, { "edge" : x } ); });
        
        println("len bSpline0_arr -> " ~ size(bSpline0_arr));
        println("len bSpline1_arr -> " ~ size(bSpline1_arr));
        
        const curve0 = joinCurveSegments(context, bSpline0_arr, definition.group0SampleCount, definition.group0Tol);
        const curve1 = joinCurveSegments(context, bSpline1_arr, definition.group0SampleCount, definition.group0Tol);
        
        if (definition.showEndpoints)
        {
            var curve0Points = evaluateSpline({
                    "spline" : curve0,
                    "parameters" : [0, 1]
            }); 
            var curve1Points = evaluateSpline({
                    "spline" : curve1,
                    "parameters" : [0, 1]
            });
            
            addDebugPoint(context, curve0Points[0][0], DebugColor.GREEN);
            addDebugPoint(context, curve1Points[0][0], DebugColor.GREEN);
            addDebugPoint(context, curve0Points[0][1], DebugColor.RED);
            addDebugPoint(context, curve1Points[0][1], DebugColor.RED);
        }
        
        if (definition.showGroups)
        {
            addDebugEntities(context, qUnion(group0_arr), DebugColor.CYAN); 
            addDebugEntities(context, qUnion(group1_arr), DebugColor.MAGENTA);
        }
        
        var retCurve = scaledCurve(context, curve0, curve1, definition.flip, shiftedScalefactors[0], shiftedScalefactors[1], definition.transitionType, definition.numScaledSamples, definition.scaledDegree, definition.scaledTol);
        
        if (definition.printBsplines)
        {
            println("---------------- BSPLINE DATA ----------------");
            printBSpline(curve0, definition.bsplineFormat, [" - - - - - - Group 0 BSplineCurve - - - - - - "]);
            printBSpline(curve1, definition.bsplineFormat, [" - - - - - - Group 1 BSplineCurve - - - - - - "]);
            printBSpline(retCurve, definition.bsplineFormat, [" - - - - - - Scaled BSplineCurve - - - - - - "]);
            println("---------------- / BSPLINE DATA ----------------");
        }
        
        var retMap = {"bspline": retCurve};
        
        if (definition.createCurve)
        {
            opCreateBSplineCurve(context, id + "createScaledBsplineCurve", {
                    "bSplineCurve" : retCurve
            });
            
            var splineQ = qCreatedBy(id + "createScaledBsplineCurve", EntityType.BODY);
            if (length(definition.curveName) > 0)
            {
                setProperty(context, {
                        "entities" : splineQ,
                        "propertyType" : PropertyType.NAME,
                        "value" : definition.curveName
                });
            }
            
            retMap['query']= splineQ;
        }
        
        
        return retMap;
   
        
    });



/**
 * Compute the applied scale factor at parameter S.
 * Transitions from sf_0 to sf_1 across the parameter range [0, 1].
 * default steepness of k=10. A second version of this function is available that accepts k as an argument
 */
export function computeAppliedSF(s is number, sf_0 is number, sf_1 is number, transition is TransitionType) returns number
{
    var t;  // normalized transition value [0, 1]
    
    if (transition == TransitionType.LINEAR)
    {
        t = s;
    }
    else if (transition == TransitionType.SINUSOIDAL)
    {
        t = (1 - cos(s * PI * radian)) / 2;
    }
    else if (transition == TransitionType.LOGISTIC)
    {
        var k = 10;
        var raw = 1 / (1 + exp(-k * (s - 0.5)));
        var raw_0 = 1 / (1 + exp(-k * (0 - 0.5)));  // Value at s=0
        var raw_1 = 1 / (1 + exp(-k * (1 - 0.5)));  // Value at s=1
        
        // Normalize so t=0 at s=0 and t=1 at s=1
        t = (raw - raw_0) / (raw_1 - raw_0);
    }
    
    return sf_0 + (sf_1 - sf_0) * t;
}

/**
 * Compute the applied scale factor at parameter S.
 * Transitions from sf_0 to sf_1 across the parameter range [0, 1].
 * Logistic component uses a user specified steepness, k.
 */
export function computeAppliedSF(s is number, sf_0 is number, sf_1 is number, k is number, transition is TransitionType) returns number
{
    var t;  // normalized transition value [0, 1]
    
    if (transition == TransitionType.LINEAR)
    {
        t = s;
    }
    else if (transition == TransitionType.SINUSOIDAL)
    {
        t = (1 - cos(s * PI * radian)) / 2;
    }
    else if (transition == TransitionType.LOGISTIC)
    {
        var raw = 1 / (1 + exp(-k * (s - 0.5)));
        var raw_0 = 1 / (1 + exp(-k * (0 - 0.5)));  // Value at s=0
        var raw_1 = 1 / (1 + exp(-k * (1 - 0.5)));  // Value at s=1
        
        // Normalize so t=0 at s=0 and t=1 at s=1
        t = (raw - raw_0) / (raw_1 - raw_0);
    }
    
    return sf_0 + (sf_1 - sf_0) * t;
}

/**
 * Create a blended curve between two curves with variable scale factor.
 *
 * @param curve0 {BSplineCurve} : First boundary curve
 * @param curve1 {BSplineCurve} : Second boundary curve
 * @param flip {boolean} : If true, reverse parameterization of curve0
 * @param sf_0 {number} : Scale factor at S=0 (0 = all curve0, 1 = all curve1)
 * @param sf_1 {number} : Scale factor at S=1
 * @param transition {TransitionType} : How scale factor changes
 * @param numSamples {number} : Number of sample points for fitting
 * @param tolerance {ValueWithUnits} : Fitting tolerance for approximateSpline
 */
export function scaledCurve(context is Context, curve0 is BSplineCurve, curve1 is BSplineCurve, flip is boolean, sf_0 is number, sf_1 is number, transition is TransitionType, numSamples is number, degree is number, tolerance is ValueWithUnits) returns BSplineCurve
{
    // Sample both curves and blend
    var blendedPoints = [];
    
    for (var i = 0; i < numSamples; i += 1)
    {
        var S = i / (numSamples - 1);  // 0 to 1 inclusive   
        
        // Evaluate curve0 (with flip if needed)
        var S0 = flip ? (1 - S) : S;
        var pt0 = evaluateSpline({ 
            "spline" : curve0, 
            "parameters" : [S0] 
        })[0][0];
        
        // Evaluate curve1
        var pt1 = evaluateSpline({ 
            "spline" : curve1, 
            "parameters" : [S] 
        })[0][0];
        
        // Compute blended point
        var sf = computeAppliedSF(S, sf_0, sf_1, transition);
        var blendedPt = (1 - sf) * pt0 + sf * pt1;
        
        blendedPoints = append(blendedPoints, blendedPt);
    }
    
    // Fit curve through blended points
    // Use explicit parameters to ensure endpoints are exact
    var params = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        params = append(params, i / (numSamples - 1));
    }
    
    var result = approximateSpline(context, {
        "degree" : 3,
        "tolerance" : tolerance,
        "isPeriodic" : false,
        "targets" : [approximationTarget({ "positions" : blendedPoints })],
        "parameters" : params,
        "interpolateIndices" : [0, numSamples - 1]  // Force exact endpoints
    });
    
    return result[0];
}


/**
 * Join an array of curve segments into a single BSplineCurve.
 * Uses arc-length proportional sampling.
 *
 * @param context {Context}
 * @param segments {array} : Unordered array of BSplineCurve
 * @param totalSamples {number} : Total sample points across all segments
 * @param tolerance {ValueWithUnits} : Fitting tolerance
 */
export function joinCurveSegments(
    context is Context,
    segments is array,
    totalSamples is number,
    tolerance is ValueWithUnits
) returns BSplineCurve
{
    if (size(segments) == 0)
    {
        throw regenError("No segments provided");
    }
    
    if (size(segments) == 1)
    {
        return segments[0];
    }
    
    // Order the segments first
    var ordering = orderCurveSegments(context, segments, tolerance);
    var orderedSegs = ordering.ordered;
    var flips = ordering.flips;
    
    // Compute arc length of each segment
    const arcLengthSamples = 20;  // For approximation
    var lengths = [];
    var totalLength = 0 * meter;
    
    for (var seg in orderedSegs)
    {
        var len = approximateArcLength(seg, arcLengthSamples);
        lengths = append(lengths, len);
        totalLength += len;
    }
    
    // Allocate samples per segment proportionally
    var samplesPerSeg = [];
    var allocatedSamples = 0;
    
    for (var i = 0; i < size(orderedSegs); i += 1)
    {
        var fraction = lengths[i] / totalLength;
        var samples = round(fraction * totalSamples);
        
        // Ensure at least 2 samples per segment
        samples = max(samples, 2);
        
        samplesPerSeg = append(samplesPerSeg, samples);
        allocatedSamples += samples;
    }
    
    // Adjust last segment to hit exact total (account for rounding)
    var adjustment = totalSamples - allocatedSamples;
    samplesPerSeg[size(samplesPerSeg) - 1] = samplesPerSeg[size(samplesPerSeg) - 1] + adjustment;
    
    // Sample all segments, collecting points
    var allPoints = [];
    
    for (var segIdx = 0; segIdx < size(orderedSegs); segIdx += 1)
    {
        var segment = orderedSegs[segIdx];
        var flip = flips[segIdx];
        var numSamples = samplesPerSeg[segIdx];
        
        // Skip start point for subsequent segments (avoids duplicate)
        var startI = (segIdx == 0) ? 0 : 1;
        
        for (var i = startI; i < numSamples; i += 1)
        {
            var S = i / (numSamples - 1);
            var evalS = flip ? (1 - S) : S;
            
            var pt = evaluateSpline({
                "spline" : segment,
                "parameters" : [evalS]
            })[0][0];
            
            allPoints = append(allPoints, pt);
        }
    }
    
    // Create parameters proportional to cumulative arc length
    var params = [];
    var cumulativeLength = 0 * meter;
    var prevPt = allPoints[0];
    params = append(params, 0);
    
    for (var i = 1; i < size(allPoints); i += 1)
    {
        cumulativeLength += norm(allPoints[i] - prevPt);
        params = append(params, cumulativeLength / totalLength);  // Normalized to [0, 1]
        prevPt = allPoints[i];
    }
    
    // Force last param to exactly 1 (avoid floating point drift)
    params[size(params) - 1] = 1;
    
    // Fit single curve through all points
    var result = approximateSpline(context, {
        "degree" : 3,
        "tolerance" : tolerance,
        "isPeriodic" : false,
        "targets" : [approximationTarget({ "positions" : allPoints })],
        "parameters" : params,
        "interpolateIndices" : [0, size(allPoints) - 1]
    });
    
    return result[0];
}

/**
 * Approximate arc length of a BSplineCurve by summing chord lengths.
 */
export function approximateArcLength(curve is BSplineCurve, numSamples is number) returns ValueWithUnits
{
    var length = 0 * meter;
    var prevPt = evaluateSpline({ "spline" : curve, "parameters" : [0] })[0][0];
    
    for (var i = 1; i <= numSamples; i += 1)
    {
        var S = i / numSamples;
        var pt = evaluateSpline({ "spline" : curve, "parameters" : [S] })[0][0];
        length += norm(pt - prevPt);
        prevPt = pt;
    }
    
    return length;
}


/**
 * Order an array of curve segments into a connected path.
 * Returns ordered segments and flip flags.
 *
 * @param context {Context}
 * @param segments {array} : Unordered array of BSplineCurve
 * @param tolerance {ValueWithUnits} : Tolerance for endpoint matching
 * @returns {map} : { "ordered" : array, "flips" : array } or throws error
 */
export function orderCurveSegments(context is Context, segments is array, tolerance is ValueWithUnits) returns map
{
    if (size(segments) == 0)
    {
        throw regenError("No segments provided");
    }
    
    if (size(segments) == 1)
    {
        return { "ordered" : segments, "flips" : [false] };
    }
    
    // Get start and end points for each segment
    var endpoints = [];
    for (var i = 0; i < size(segments); i += 1)
    {
        var startPt = evaluateSpline({ "spline" : segments[i], "parameters" : [0] })[0][0];
        var endPt = evaluateSpline({ "spline" : segments[i], "parameters" : [1] })[0][0];
        endpoints = append(endpoints, { "start" : startPt, "end" : endPt });
    }
    
    // Track which segments are used
    var used = makeArray(size(segments), false);
    var ordered = [];
    var flips = [];
    
    // Start with segment 0, determine if it needs flipping later
    var currentIdx = 0;
    var currentFlip = false;
    
    // First, find a segment that's an endpoint of the chain (only one connection)
    // This ensures we start at a true endpoint, not the middle
    for (var i = 0; i < size(segments); i += 1)
    {
        var connections = countConnections(endpoints, i, tolerance);
        if (connections == 1)
        {
            currentIdx = i;
            break;
        }
    }
    
    // Determine if first segment needs flipping
    // (its "start" should be the unconnected end)
    var firstStart = endpoints[currentIdx].start;
    var hasConnectionAtStart = false;
    for (var i = 0; i < size(segments); i += 1)
    {
        if (i == currentIdx) continue;
        if (tolerantEquals(firstStart, endpoints[i].start) || 
            tolerantEquals(firstStart, endpoints[i].end))
        {
            hasConnectionAtStart = true;
            break;
        }
    }
    currentFlip = hasConnectionAtStart;  // Flip if start is connected (we want start to be free end)
    
    // Build the chain
    while (size(ordered) < size(segments))
    {
        ordered = append(ordered, segments[currentIdx]);
        flips = append(flips, currentFlip);
        used[currentIdx] = true;
        
        // Current endpoint we're continuing from
        var currentEnd = currentFlip ? endpoints[currentIdx].start : endpoints[currentIdx].end;
        
        // Find next segment
        var foundNext = false;
        for (var i = 0; i < size(segments); i += 1)
        {
            if (used[i]) continue;
            
            if (tolerantEquals(currentEnd, endpoints[i].start))
            {
                currentIdx = i;
                currentFlip = false;
                foundNext = true;
                break;
            }
            else if (tolerantEquals(currentEnd, endpoints[i].end))
            {
                currentIdx = i;
                currentFlip = true;
                foundNext = true;
                break;
            }
        }
        
        if (!foundNext && size(ordered) < size(segments))
        {
            throw regenError("Segments do not form a continuous path. " ~ 
                size(ordered) ~ " of " ~ size(segments) ~ " segments connected.");
        }
    }
    
    return { "ordered" : ordered, "flips" : flips };
}


/**
 * Count how many other segments connect to segment at index.
 */

export function countConnections(endpoints is array, index is number, tolerance is ValueWithUnits) returns number
{
    var count = 0;
    var myStart = endpoints[index].start;
    var myEnd = endpoints[index].end;
    
    for (var i = 0; i < size(endpoints); i += 1)
    {
        if (i == index) continue;
        
        if (tolerantEquals(myStart, endpoints[i].start) ||
            tolerantEquals(myStart, endpoints[i].end))
        {
            count += 1;
        }
        if (tolerantEquals(myEnd, endpoints[i].start) ||
            tolerantEquals(myEnd, endpoints[i].end))
        {
            count += 1;
        }
    }
    
    return count;
}

/**
 * Pretty-print a BSplineCurve for debugging.
 *
 * @param curve {BSplineCurve} : The curve to print
 * @param format {PrintFormat} : METADATA for summary, DETAILS for full data
 * @param tags {array} : Optional [startTag, endTag] strings to wrap output
 */
export function printBSpline(curve is BSplineCurve, format is PrintFormat, tags is array)
{
    // Start tag
    if (size(tags) >= 1 && tags[0] != undefined && tags[0] != "")
    {
        println(tags[0]);
    }
    
    // Always print basic metadata
    println("  Degree: " ~ curve.degree);
    println("  Control Points: " ~ size(curve.controlPoints));
    println("  Knots: " ~ size(curve.knots));
    println("  Rational: " ~ curve.isRational);
    println("  Periodic: " ~ curve.isPeriodic);
    
    if (curve.dimension != undefined)
    {
        println("  Dimension: " ~ curve.dimension);
    }
    
    var numSpans = size(curve.knots) - 2 * curve.degree - 1;
    println("  Spans: " ~ numSpans);
    
    if (format == PrintFormat.DETAILS)
    {
        println("");
        println("  --- Control Points ---");
        for (var i = 0; i < size(curve.controlPoints); i += 1)
        {
            var pt = curve.controlPoints[i];
            println("    [" ~ i ~ "]: " ~ formatVector(pt));
        }
        
        println("");
        println("  --- Knot Vector ---");
        var knotStr = "    [";
        for (var i = 0; i < size(curve.knots); i += 1)
        {
            knotStr = knotStr ~ roundDecimal(curve.knots[i], 6);
            if (i < size(curve.knots) - 1)
            {
                knotStr = knotStr ~ ", ";
            }
            // Line break every 8 knots for readability
            if ((i + 1) % 8 == 0 && i < size(curve.knots) - 1)
            {
                knotStr = knotStr ~ "\n     ";
            }
        }
        knotStr = knotStr ~ "]";
        println(knotStr);
        
        if (curve.isRational && curve.weights != undefined)
        {
            println("");
            println("  --- Weights ---");
            var weightStr = "    [";
            for (var i = 0; i < size(curve.weights); i += 1)
            {
                weightStr = weightStr ~ roundDecimal(curve.weights[i], 6);
                if (i < size(curve.weights) - 1)
                {
                    weightStr = weightStr ~ ", ";
                }
                if ((i + 1) % 8 == 0 && i < size(curve.weights) - 1)
                {
                    weightStr = weightStr ~ "\n     ";
                }
            }
            weightStr = weightStr ~ "]";
            println(weightStr);
        }
    }
    
    
    // End tag
    if (size(tags) >= 2 && tags[1] != undefined && tags[1] != "")
    {
        println(tags[1]);
    }
}

/**
 * Format a 3D vector nicely.
 */
export function formatVector(v is Vector) returns string
{
    // Handle vectors with units (typical for control points)
    var x = v[0];
    var y = v[1];
    var z = v[2];
    
    // Try to extract value in meters for clean display
    try silent
    {
        x = x / meter;
        y = y / meter;
        z = z / meter;
        return "(" ~ roundDecimal(x, 6) ~ ", " ~ roundDecimal(y, 6) ~ ", " ~ roundDecimal(z, 6) ~ ") m";
    }
    
    // Fallback for unitless vectors
    return "(" ~ roundDecimal(x, 6) ~ ", " ~ roundDecimal(y, 6) ~ ", " ~ roundDecimal(z, 6) ~ ")";
}

/**
 * Round a number to specified decimal places.
 */
export function roundDecimal(value is number, places is number) returns number
{
    var factor = 1;
    for (var i = 0; i < places; i += 1)
    {
        factor = factor * 10;
    }
    return round(value * factor) / factor;
}

