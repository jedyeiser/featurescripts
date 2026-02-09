FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

// import bspline_data (for getControlPoints, getBSplineParamRange, etc.)
import(path : "b1e8bfe71f67389ca210ed8b/e13e99b75ba5ce6d6380ddd5/b1c7f2116fb64e6b40bf53f4", version : "4fe0cca8e00a4cd812896a8c");
// import bspline_knots (for reverseCurve, findSpan)
import(path : "b1e8bfe71f67389ca210ed8b/e13e99b75ba5ce6d6380ddd5/dadb70c0a762573622fa609c", version : "2267a758e66498ac49f4601e");
// import curve_operations (for splitCurve, extractSubcurve)
import(path : "b1e8bfe71f67389ca210ed8b/e13e99b75ba5ce6d6380ddd5/a7403d5f7f5a4fef8225b768", version : "8539ef748286f908313b6564");
// import math_utils (for lerp, clamp)
import(path : "b1e8bfe71f67389ca210ed8b/e13e99b75ba5ce6d6380ddd5/280a24d76f52bdbf44cd941d", version : "d9e09196718b914b96e84924");


/**
 * UNIQUE CURVES - B-Spline Consolidation
 * ======================================
 * 
 * Takes an array of potentially overlapping B-spline curves and returns
 * a minimal set of non-overlapping segments covering the same geometry.
 *
 * OVERLAP CASES:
 * | Case              | Input   | Output                              |
 * |-------------------|---------|-------------------------------------|
 * | Non-overlapping   | A, B    | A, B (unchanged)                    |
 * | Full containment  | A ⊂ B   | B_left, A, B_right (A wins)         |
 * | Partial overlap   | A ∩ B   | A_trim, Overlap, B_trim             |
 *
 * ASSUMPTIONS:
 * - Curves from same source (cross-sections) have similar control polygon density
 * - Overlapping regions have nearly identical control polygons
 * - Clamped (non-periodic) knot vectors
 */

// =============================================================================
// CONSTANTS
// =============================================================================

export const OVERLAP_TOLERANCE = 1e-6 * meter;  // Distance tolerance for control point matching
export const PARAM_TOLERANCE = 1e-10;            // Parameter space tolerance

/**
 * Overlap detection result types
 */
export enum OverlapType
{
    NONE,
    FULL_CONTAINMENT,  // One curve fully inside the other
    PARTIAL_OVERLAP    // Endpoints overlap but neither contains the other
}

// =============================================================================
// MAIN ENTRY POINT
// =============================================================================

/**
 * Get unique, non-overlapping curves from input array.
 *
 * @param context {Context} : Onshape context
 * @param inputCurves {array} : Array of {'BSplineCurve': BSplineCurve, 'bodies': array of {Query}}
 * @param tolerance {ValueWithUnits} : Distance tolerance for overlap detection
 * @returns {array} : Array of non-overlapping {'BSplineCurve': BSplineCurve, 'bodies': array of {Query} covering same geometry
 */
export function getUniqueCurves(context is Context, inputCurves is array, tolerance) returns array
{
    if (tolerance == undefined)
        tolerance = OVERLAP_TOLERANCE;
    
    if (size(inputCurves) == 0)
        return [];
    
    if (size(inputCurves) == 1)
        return inputCurves;
    
    var toProcess = inputCurves;  // Working queue
    var unique = [];               // Verified non-overlapping curves
    
    while (size(toProcess) > 0)
    {
        // Pop the last curve from toProcess
        var current = toProcess[size(toProcess) - 1];
        toProcess = subArray(toProcess, 0, size(toProcess) - 1);
        
        // Search for overlap (iteration only - no mutation)
        var overlapIndex = -1;
        var overlapResult = undefined;
        
        for (var i = 0; i < size(unique); i += 1)
        {
            var candidate = unique[i];
            var result = detectOverlap(current, candidate, tolerance); // result will be {"type": overlapType from classifyOverlap, "runs" : matchingRuns result.runs, 'reveresed, 'matchcount'. 
            
            if (result['type'] != OverlapType.NONE) // no overlaps found. Add to the unique bucket and move on. 
            {
                overlapIndex = i;
                overlapResult = result;
                break;
            }
        }
        
        // Handle overlap (mutation separated from iteration)
        if (overlapIndex >= 0)
        {
            var candidate = unique[overlapIndex];
            var pieces = splitAtOverlap(context, current, candidate, overlapResult, tolerance);
            
            // Remove candidate from unique (it's been split)
            unique = removeArrayIndex(unique, overlapIndex);
            
            // Add all pieces to unique (single-pass: no re-checking)
            for (var piece in pieces)
            {
                unique = append(unique, piece); // in general, we'd want to add each peice to toProcess. However, we're adding curves from new bodies. unique after this operation should be the unique curves of the processed bodies, which it will be. 
            }
        }
        else
        {
            // No overlap found - add to unique set
            unique = append(unique, current);
        }
    }
    
    return unique;
}

// =============================================================================
// OVERLAP DETECTION
// =============================================================================

/**
 * Detect overlap between two curves using control polygon proximity.
 *
 * Strategy:
 * 1. For each control point in A, find distance to B's control polygon
 * 2. Track which points are "on" B (within tolerance)
 * 3. Identify contiguous runs of matching points
 * 4. Classify overlap type based on run pattern
 *
 * @param curveA {BSplineCurve} : First curve
 * @param curveB {BSplineCurve} : Second curve  
 * @param tolerance {ValueWithUnits} : Distance tolerance
 * @returns {map} : {
 *     type: OverlapType,
 *     runsA: array of {startIdx, endIdx, startParamB, endParamB},
 *     reversed: boolean (true if B was conceptually reversed for matching)
 * }
 */
export function detectOverlap(curveA is map, curveB is map, tolerance) returns map
{
    var cpA = curveA.BSplineCurve.controlPoints;
    var cpB = curveB.BSplineCurve.controlPoints;
    
    // Try forward direction
    var forwardResult = findMatchingRuns(cpA, cpB, tolerance);
    
    // Try reversed direction
    var cpBReversed = reverse(cpB);
    var reverseResult = findMatchingRuns(cpA, cpBReversed, tolerance);
    
    // Use whichever direction gives better match
    var bestResult = forwardResult;
    var reversed = false;
    
    if (reverseResult.matchedCount > forwardResult.matchedCount)
    {
        bestResult = reverseResult;
        reversed = true;
    }
    
    // Classify overlap type
    var overlapType = classifyOverlap(bestResult, size(cpA), size(cpB));
    
    return {
        "type" : overlapType,
        "runs" : bestResult.runs,
        "reversed" : reversed,
        "matchedCount" : bestResult.matchedCount
    };
}

/**
 * Find contiguous runs of A's control points that lie on B's control polygon.
 *
 * @param cpA {array} : Control points of curve A
 * @param cpB {array} : Control points of curve B (possibly reversed)
 * @param tolerance {ValueWithUnits} : Distance tolerance
 * @returns {map} : {runs: array, matchedCount: number}
 */
function findMatchingRuns(cpA is array, cpB is array, tolerance) returns map
{
    var nA = size(cpA);
    var nB = size(cpB);
    
    // For each point in A, find closest point on B's polygon and record distance
    var matches = [];  // Array of {distance, segmentIdx, t} or undefined
    
    for (var i = 0; i < nA; i += 1)
    {
        var ptA = cpA[i];
        var closest = closestPointOnPolyline(ptA, cpB);
        matches = append(matches, closest);
    }
    
    // Find contiguous runs where distance < tolerance
    var runs = [];
    var currentRun = undefined;
    var matchedCount = 0;
    
    for (var i = 0; i < nA; i += 1)
    {
        var isMatch = matches[i].distance < tolerance;
        
        if (isMatch)
        {
            matchedCount += 1;
            
            if (currentRun == undefined)
            {
                // Start new run
                currentRun = {
                    "startIdxA" : i,
                    "endIdxA" : i,
                    "startSegB" : matches[i].segmentIdx,
                    "startT" : matches[i].t,
                    "endSegB" : matches[i].segmentIdx,
                    "endT" : matches[i].t
                };
            }
            else
            {
                // Extend current run
                currentRun.endIdxA = i;
                currentRun.endSegB = matches[i].segmentIdx;
                currentRun.endT = matches[i].t;
            }
        }
        else
        {
            if (currentRun != undefined)
            {
                // Close current run
                runs = append(runs, currentRun);
                currentRun = undefined;
            }
        }
    }
    
    // Don't forget last run
    if (currentRun != undefined)
    {
        runs = append(runs, currentRun);
    }
    
    return {
        "runs" : runs,
        "matchedCount" : matchedCount
    };
}

/**
 * Classify overlap type based on matching runs.
 *
 * @param matchResult {map} : Result from findMatchingRuns
 * @param nA {number} : Number of control points in A
 * @param nB {number} : Number of control points in B
 * @returns {OverlapType}
 */
function classifyOverlap(matchResult is map, nA is number, nB is number) returns OverlapType
{
    var runs = matchResult.runs;
    
    if (size(runs) == 0)
    {
        return OverlapType.NONE;
    }
    
    // Check if A is fully contained in B (all points match in one contiguous run)
    if (size(runs) == 1)
    {
        var run = runs[0];
        
        var runLength = run.endIdxA - run.startIdxA + 1;
        if (runLength < 2)
        {
            return OverlapType.NONE;
        }
        
        var allAMatched = (run.startIdxA == 0 && run.endIdxA == nA - 1);
        
        if (allAMatched)
        {
            // A is fully inside B
            return OverlapType.FULL_CONTAINMENT;
        }
        
        // Partial overlap: only some of A matches B
        // Check if it's at an endpoint of A
        var atStartOfA = (run.startIdxA == 0);
        var atEndOfA = (run.endIdxA == nA - 1);
        
        if (atStartOfA || atEndOfA)
        {
            return OverlapType.PARTIAL_OVERLAP;
        }
    }
    
    // Multiple runs or middle-only match: treat as no meaningful overlap
    // (This shouldn't happen with well-behaved cross-section curves)
    return OverlapType.NONE;
}

// =============================================================================
// CURVE SPLITTING AT OVERLAP
// =============================================================================

/**
 * Split curves based on detected overlap.
 *
 * @param context {Context} : Onshape context
 * @param curveA {map} : First curve (the "current" one being processed) {'BSplineCurve' : BSplineCurve, 'bodies' : array of {Query} }
 * @param curveB {BSplineCurve} : Second curve (from unique set) {'BSplineCurve' : BSplineCurve, 'body' : array of {Query}}
 * @param overlapResult {map} : Result from detectOverlap
 * @param tolerance {ValueWithUnits} : Tolerance
 * @returns {array} : Array of 1-3 non-overlapping curves
 */
function splitAtOverlap(context is Context, curveA is map, curveB is map,
                        overlapResult is map, tolerance) returns array
{
    // Handle reversed case: if B needed to be reversed for matching, reverse it now
    var workingB = curveB.BSplineCurve;
    if (overlapResult.reversed)
    {
        workingB = reverseCurve(curveB);
    }
    
    var runs = overlapResult.runs;
    
    if (size(runs) == 0)
    {
        // No overlap - return both unchanged
        return [curveA, curveB];
    }
    
    var run = runs[0];  // We only handle single-run overlaps
    
    if (overlapResult['type'] == OverlapType.FULL_CONTAINMENT)
    {
        return splitFullContainment(context, curveA, workingB, run, tolerance);
    }
    else if (overlapResult['type'] == OverlapType.PARTIAL_OVERLAP)
    {
        return splitPartialOverlap(context, curveA, workingB, run, tolerance);
    }
    
    // Fallback: return both unchanged
    return [curveA, curveB];
}

/**
 * Handle full containment case: A is inside B.
 * Returns: [B_left, A, B_right] (or fewer if A touches B's endpoints)
 */
function splitFullContainment(context is Context, curveA is map, curveB is map,
                               run is map, tolerance) returns array
{
    var pieces = [];
    var rangeB = getBSplineParamRange(curveB.BSplineCurve);
    var nB = size(curveB.BsplineCurve.controlPoints);
    
    // Convert control point indices to approximate parameters
    // (This is a simplification - for production, you might want proper projection)
    var paramStartB = controlPointIndexToParam(curveB.BSplineCurve, run.startSegB + run.startT);
    var paramEndB = controlPointIndexToParam(curveB.BSplineCurve, run.endSegB + run.endT);
    
    // Ensure proper ordering
    if (paramStartB > paramEndB)
    {
        var temp = paramStartB;
        paramStartB = paramEndB;
        paramEndB = temp;
    }
    
    // B_left: portion of B before overlap
    if (paramStartB > rangeB.uMin + PARAM_TOLERANCE)
    {
        var bLeft = {"BSplineCurve": extractSubcurve(context, curveB.BSplineCurve, rangeB.uMin, paramStartB), 'bodies' : curveB.bodies};
        pieces = append(pieces, bLeft);
    }
    
    // A: the contained curve (wins over B in overlap region)
    pieces = append(pieces, {"BSplineCurve" : curveA.BSplineCurve, 'bodies': concatenateArrays(curveA.bodies, curveB.bodies)});
    
    // B_right: portion of B after overlap
    if (paramEndB < rangeB.uMax - PARAM_TOLERANCE)
    {
        var bRight = {"BSplineCurve" : extractSubcurve(context, curveB, paramEndB, rangeB.uMax), 'bodies' : curveB.bodies};
        pieces = append(pieces, bRight);
    }
    
    return pieces;
}

/**
 * Handle partial overlap case: ends of A and B overlap.
 * Returns: [A_trimmed, Overlap, B_trimmed]
 */
function splitPartialOverlap(context is Context, curveA is map, curveB is map,
                              run is map, tolerance) returns array
{
    var pieces = [];
    var rangeA = getBSplineParamRange(curveA.BSplineCurve);
    var rangeB = getBSplineParamRange(curveB.BSplineCurve);
    var nA = size(curveA.BSplineCurve.controlPoints);
    
    // Determine which end of A overlaps
    var overlapAtStartA = (run.startIdxA == 0);
    var overlapAtEndA = (run.endIdxA == nA - 1);
    
    // Convert indices to parameters
    var paramOverlapStartA = controlPointIndexToParam(curveA.BSplineCurve, run.startIdxA);
    var paramOverlapEndA = controlPointIndexToParam(curveA.BSplineCurve, run.endIdxA);
    var paramOverlapStartB = controlPointIndexToParam(curveB.BSplineCurve, run.startSegB + run.startT);
    var paramOverlapEndB = controlPointIndexToParam(curveB.BSplineCurve, run.endSegB + run.endT);
    
    if (overlapAtStartA)
    {
        // Overlap at start of A: B_portion + Overlap (from A) + A_remainder
        // B trimmed (portion before overlap)
        if (paramOverlapStartB > rangeB.uMin + PARAM_TOLERANCE)
        {
            var bTrimmed = {"BSplineCurve" : extractSubcurve(context, curveB.BSplineCurve, rangeB.uMin, paramOverlapStartB), 'bodies' : curveB.bodies};
            pieces = append(pieces, bTrimmed);
        }
        
        // Overlap region (extracted from A)
        var overlap = {"BSplineCurve" : extractSubcurve(context, curveA.BSplineCurve, rangeA.uMin, paramOverlapEndA), 'bodies' : concatenateArrays(curveA.bodies, curveB.bodies)};
        pieces = append(pieces, overlap);
        
        // A trimmed (portion after overlap)
        if (paramOverlapEndA < rangeA.uMax - PARAM_TOLERANCE)
        {
            var aTrimmed = {"BSplineCurve" : extractSubcurve(context, curveA.BSplineCurve, paramOverlapEndA, rangeA.uMax), 'bodies' : curveA.bodies};
            pieces = append(pieces, aTrimmed);
        }
    }
    else if (overlapAtEndA)
    {
        // Overlap at end of A: A_remainder + Overlap (from A) + B_portion
        
        // A trimmed (portion before overlap)
        if (paramOverlapStartA > rangeA.uMin + PARAM_TOLERANCE)
        {
            var aTrimmed = {"BSplineCurve" : extractSubcurve(context, curveA.BSplineCurve, rangeA.uMin, paramOverlapStartA), 'bodies' : curveA.bodies};
            pieces = append(pieces, aTrimmed);
        }
        
        // Overlap region (extracted from A)
        var overlap = {"BSplineCurve" : extractSubcurve(context, curveA.BSplineCurve, paramOverlapStartA, rangeA.uMax), 'bodies' : concatenateArrays(curveA.bodies, curveB.bodies)};
        pieces = append(pieces, overlap);
        
        // B trimmed (portion after overlap)
        if (paramOverlapEndB < rangeB.uMax - PARAM_TOLERANCE)
        {
            var bTrimmed = {"BSplineCurve" : extractSubcurve(context, curveB.BSplineCurve, paramOverlapEndB, rangeB.uMax), 'bodies' : curveB.bodies};
            pieces = append(pieces, bTrimmed);
        }
    }
    
    return pieces;
}

// =============================================================================
// GEOMETRY HELPERS
// =============================================================================

/**
 * Find closest point on a polyline (control polygon) to a given point.
 *
 * @param pt {Vector} : Query point
 * @param polyline {array} : Array of Vectors defining polyline vertices
 * @returns {map} : {distance, segmentIdx, t}
 *     - distance: closest distance to polyline
 *     - segmentIdx: index of closest segment (0 to n-2)
 *     - t: parameter along that segment [0,1]
 */
function closestPointOnPolyline(pt, polyline is array) returns map
{
    var bestDist = undefined;
    var bestSeg = 0;
    var bestT = 0;
    
    for (var i = 0; i < size(polyline) - 1; i += 1)
    {
        var segStart = polyline[i];
        var segEnd = polyline[i + 1];
        var result = closestPointOnSegment(pt, segStart, segEnd);
        
        if (bestDist == undefined || result.distance < bestDist)
        {
            bestDist = result.distance;
            bestSeg = i;
            bestT = result.t;
        }
    }
    
    return {
        "distance" : bestDist,
        "segmentIdx" : bestSeg,
        "t" : bestT
    };
}

/**
 * Find closest point on a line segment to a given point.
 *
 * @param pt {Vector} : Query point
 * @param segStart {Vector} : Segment start
 * @param segEnd {Vector} : Segment end
 * @returns {map} : {distance, t, closestPt}
 */
function closestPointOnSegment(pt, segStart, segEnd) returns map
{
    var segVec = segEnd - segStart;
    var segLenSq = squaredNorm(segVec);
    
    if (segLenSq < 1e-20 * meter * meter)
    {
        // Degenerate segment
        return {
            "distance" : norm(pt - segStart),
            "t" : 0,
            "closestPt" : segStart
        };
    }
    
    // Project pt onto line, clamp to segment
    var t = dot(pt - segStart, segVec) / segLenSq;
    t = clamp(t, 0, 1);
    
    var closestPt = segStart + t * segVec;
    var distance = norm(pt - closestPt);
    
    return {
        "distance" : distance,
        "t" : t,
        "closestPt" : closestPt
    };
}

/**
 * Squared norm (avoids sqrt for comparison purposes).
 */
function squaredNorm(v) returns number
{
    return dot(v, v);
}

/**
 * Convert control point index (fractional) to approximate curve parameter.
 *
 * This is a simplification that works well when control points are roughly
 * evenly distributed along the curve. For production use with uneven 
 * parameterization, you'd want proper curve projection.
 *
 * @param curve {BSplineCurve} : Curve
 * @param cpIndex {number} : Fractional control point index
 * @returns {number} : Approximate parameter value
 */
function controlPointIndexToParam(curve is BSplineCurve, cpIndex is number) returns number
{
    var range = getBSplineParamRange(curve);
    var nCP = size(curve.controlPoints);
    
    // Linear interpolation: index 0 -> uMin, index (nCP-1) -> uMax
    var t = cpIndex / (nCP - 1);
    return range.uMin + t * (range.uMax - range.uMin);
}

/**
 * Remove element at index from array.
 */
function removeArrayIndex(arr is array, index is number) returns array
{
    var result = [];
    for (var i = 0; i < size(arr); i += 1)
    {
        if (i != index)
        {
            result = append(result, arr[i]);
        }
    }
    return result;
}


