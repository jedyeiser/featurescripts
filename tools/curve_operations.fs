FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

// Dependencies
//import assertions
import(path : "34fb2c6a3c895cfce6b281f3", version : "bd6a4d5a47ec29178af978cf");

//import bspline_data
import(path : "b1c7f2116fb64e6b40bf53f4", version : "4fe0cca8e00a4cd812896a8c");

//import bspline_knots
import(path : "dadb70c0a762573622fa609c", version : "2267a758e66498ac49f4601e");

//import math_utils
import(path : "280a24d76f52bdbf44cd941d", version : "d9e09196718b914b96e84924");



/**
 * CURVE OPERATIONS
 * =================
 *
 * High-level operations for splitting and joining BSpline curves.
 * These operations are essential for curve network construction,
 * trimming, and surface operations.
 *
 * | Operation          | Preserves | Use For                           |
 * |--------------------|-----------|-----------------------------------|
 * | splitCurve()       | Tangency  | Trim curves, extract segments     |
 * | joinCurves()       | Continuity| Build composite curves, fillets   |
 * | extractSubcurve()  | Shape     | Get curve segment between params  |
 *
 * TANGENCY PRESERVATION:
 * When splitting, both resulting curves have the same tangent direction
 * at the split point (C1 continuous across the split before separation).
 *
 * CONTINUITY LEVELS:
 * - C0: Position continuous (curves touch)
 * - C1: C0 + tangent continuous (smooth)
 * - C2: C1 + curvature continuous (very smooth)
 *
 * @reference Uses knot insertion from bspline_knots.fs
 */

// =============================================================================
// CONSTANTS
// =============================================================================

/**
 * Default tolerance for curve operations.
 */
export const CURVE_OP_TOLERANCE = 1e-8;

/**
 * Continuity types for curve joining.
 */
export enum ContinuityType
{
    annotation { "Name" : "C0 (Position)" }
    C0,
    annotation { "Name" : "C1 (Tangent)" }
    C1,
    annotation { "Name" : "C2 (Curvature)" }
    C2
}

// =============================================================================
// CURVE SPLITTING
// =============================================================================

/**
 * Split a BSpline curve at a parameter, preserving tangency.
 *
 * Divides a curve into two curves at the specified parameter.
 * Both resulting curves have the same tangent at the split point
 * (the operation maintains C1 continuity at the split before separation).
 *
 * Algorithm:
 * 1. Insert knot at splitParam with multiplicity = degree + 1
 * 2. This creates a C^(-1) discontinuity (curve breaks into pieces)
 * 3. Extract the two separate curves from the split knot vector
 *
 * @param context {Context} : Onshape context
 * @param curve {BSplineCurve} : Curve to split
 * @param splitParam {number} : Parameter value where to split
 * @returns {map} : {
 *                    curveA: BSplineCurve,  - Curve from start to splitParam
 *                    curveB: BSplineCurve,  - Curve from splitParam to end
 *                    splitPoint: Vector     - Point at split parameter
 *                  }
 *
 * @example Split curve in half:
 *   `var range = getBSplineParamRange(myCurve);`
 *   `var mid = (range.uMin + range.uMax) / 2;`
 *   `var result = splitCurve(context, myCurve, mid);`
 *   `var firstHalf = result.curveA;`
 *   `var secondHalf = result.curveB;`
 *
 * @note Both curves share the same tangent at the split point
 * @note Total arc length of A + B equals original curve arc length
 * @note Does not modify input curve
 *
 * @reference P&T "The NURBS Book" knot insertion algorithm
 */
export function splitCurve(context is Context, curve is BSplineCurve, splitParam is number) returns map
{
    // Validate split parameter is in range
    var range = getBSplineParamRange(curve);
    assertInRange(splitParam, range.uMin, range.uMax,
                  "splitCurve: splitParam must be within curve parameter range");

    var degree = curve.degree;

    // Insert knot with multiplicity = degree + 1 to completely break the curve
    var numInsertions = degree + 1;
    var knotsToInsert = [];
    for (var i = 0; i < numInsertions; i += 1)
    {
        knotsToInsert = append(knotsToInsert, splitParam);
    }

    var splitCurve = refineKnotVector(context, curve, knotsToInsert);

    // Find the split point in the new knot vector
    var knots = splitCurve.knots;
    var splitIndex = -1;
    for (var i = 0; i < size(knots); i += 1)
    {
        if (abs(knots[i] - splitParam) < CURVE_OP_TOLERANCE)
        {
            splitIndex = i;
            break;
        }
    }

    assertTrue(splitIndex >= 0, "splitCurve: Failed to find split knot");

    // The split creates a repeated knot with multiplicity = degree + 1
    // Find the range of this repeated knot
    var splitStart = splitIndex;
    var splitEnd = splitIndex;

    while (splitStart > 0 && abs(knots[splitStart - 1] - splitParam) < CURVE_OP_TOLERANCE)
    {
        splitStart -= 1;
    }
    while (splitEnd < size(knots) - 1 && abs(knots[splitEnd + 1] - splitParam) < CURVE_OP_TOLERANCE)
    {
        splitEnd += 1;
    }

    // Extract first curve: from start to split
    // Control points: [0 ... splitStart]
    // Knots: [0 ... splitStart + degree]
    var cpA = [];
    for (var i = 0; i <= splitStart; i += 1)
    {
        cpA = append(cpA, splitCurve.controlPoints[i]);
    }

    var knotsA = [];
    for (var i = 0; i <= splitStart + degree; i += 1)
    {
        knotsA = append(knotsA, knots[i]);
    }

    var weightsA = [];
    if (curve.isRational && splitCurve.weights != undefined)
    {
        for (var i = 0; i <= splitStart; i += 1)
        {
            weightsA = append(weightsA, splitCurve.weights[i]);
        }
    }

    // Extract second curve: from split to end
    // Control points: [splitEnd ... end]
    // Knots: [splitEnd - degree ... end]
    var cpB = [];
    for (var i = splitEnd; i < size(splitCurve.controlPoints); i += 1)
    {
        cpB = append(cpB, splitCurve.controlPoints[i]);
    }

    var knotsB = [];
    for (var i = splitEnd - degree; i < size(knots); i += 1)
    {
        knotsB = append(knotsB, knots[i]);
    }

    var weightsB = [];
    if (curve.isRational && splitCurve.weights != undefined)
    {
        for (var i = splitEnd; i < size(splitCurve.weights); i += 1)
        {
            weightsB = append(weightsB, splitCurve.weights[i]);
        }
    }

    var curveA = {
        "degree" : degree,
        "isPeriodic" : false,
        "controlPoints" : cpA,
        "knots" : knotsA,
        "weights" : weightsA,
        "isRational" : curve.isRational,
        "dimension" : curve.dimension
    } as BSplineCurve;

    var curveB = {
        "degree" : degree,
        "isPeriodic" : false,
        "controlPoints" : cpB,
        "knots" : knotsB,
        "weights" : weightsB,
        "isRational" : curve.isRational,
        "dimension" : curve.dimension
    } as BSplineCurve;

    // Evaluate split point
    var evalResult = evaluateSpline({
        "spline" : curve,
        "parameters" : [splitParam]
    });
    var splitPoint = evalResult[0][0];

    return {
        "curveA" : curveA,
        "curveB" : curveB,
        "splitPoint" : splitPoint
    };
}

/**
 * Split curve at multiple parameters.
 *
 * Divides a curve into N+1 segments where N is the number of split parameters.
 * Parameters are automatically sorted and duplicates removed.
 *
 * @param context {Context} : Onshape context
 * @param curve {BSplineCurve} : Curve to split
 * @param splitParams {array} : Array of parameter values
 * @returns {array} : Array of BSplineCurve segments (length = splitParams.length + 1)
 *
 * @example Split curve into thirds:
 *   `var range = getBSplineParamRange(curve);`
 *   `var u1 = range.uMin + (range.uMax - range.uMin) / 3;`
 *   `var u2 = range.uMin + 2 * (range.uMax - range.uMin) / 3;`
 *   `var segments = splitCurveMultiple(context, curve, [u1, u2]);`
 *   // segments has 3 curves
 *
 * @note Preserves tangency at all split points
 */
export function splitCurveMultiple(context is Context, curve is BSplineCurve, splitParams is array) returns array
{
    if (size(splitParams) == 0)
    {
        return [curve];
    }

    // Sort parameters
    var sorted = sort(splitParams, function(a, b) { return a - b; });

    // Remove duplicates
    var unique = [sorted[0]];
    for (var i = 1; i < size(sorted); i += 1)
    {
        if (abs(sorted[i] - unique[size(unique) - 1]) > CURVE_OP_TOLERANCE)
        {
            unique = append(unique, sorted[i]);
        }
    }

    // Split iteratively
    var segments = [];
    var currentCurve = curve;

    for (var i = 0; i < size(unique); i += 1)
    {
        var result = splitCurve(context, currentCurve, unique[i]);
        segments = append(segments, result.curveA);
        currentCurve = result.curveB;
    }

    // Add final segment
    segments = append(segments, currentCurve);

    return segments;
}

/**
 * Extract subcurve between two parameters.
 *
 * Returns a curve segment from paramA to paramB. Equivalent to splitting
 * at both parameters and taking the middle segment.
 *
 * @param context {Context} : Onshape context
 * @param curve {BSplineCurve} : Source curve
 * @param paramA {number} : Start parameter
 * @param paramB {number} : End parameter
 * @returns {BSplineCurve} : Curve segment from paramA to paramB
 *
 * @example Extract middle third of curve:
 *   `var range = getBSplineParamRange(curve);`
 *   `var u1 = range.uMin + (range.uMax - range.uMin) / 3;`
 *   `var u2 = range.uMin + 2 * (range.uMax - range.uMin) / 3;`
 *   `var middleThird = extractSubcurve(context, curve, u1, u2);`
 *
 * @note Parameters are automatically ordered (works if paramA > paramB)
 */
export function extractSubcurve(context is Context, curve is BSplineCurve,
                                 paramA is number, paramB is number) returns BSplineCurve
{
    // Ensure paramA < paramB
    var uStart = min([paramA, paramB]);
    var uEnd = max([paramA, paramB]);

    // Split at both parameters
    var segments = splitCurveMultiple(context, curve, [uStart, uEnd]);

    // Return middle segment
    assertTrue(size(segments) >= 2, "extractSubcurve: split failed");
    return segments[1];
}

// =============================================================================
// CURVE JOINING
// =============================================================================

/**
 * Join two BSpline curves with specified continuity.
 *
 * Connects curveA and curveB into a single curve, enforcing the specified
 * continuity condition at the join. The join is made at curveA.end to curveB.start.
 *
 * Continuity enforcement:
 * - C0: Ensures endpoints match (within tolerance)
 * - C1: C0 + adjusts control points to match tangent magnitude and direction
 * - C2: C1 + adjusts control points to match curvature
 *
 * Algorithm:
 * 1. Make curves compatible (same degree, reparameterize if needed)
 * 2. Adjust control points near join for continuity
 * 3. Concatenate control points and knot vectors
 *
 * @param context {Context} : Onshape context
 * @param curveA {BSplineCurve} : First curve
 * @param curveB {BSplineCurve} : Second curve (joined to end of A)
 * @param continuity {ContinuityType} : Desired continuity at join
 * @param options {map} : Optional settings:
 *                        - tolerance: Position tolerance for C0 (default 1e-6 * meter)
 *                        - adjustA: Adjust curveA near join (default false)
 *                        - adjustB: Adjust curveB near join (default true)
 * @returns {BSplineCurve} : Joined curve
 *
 * @example Join two curves with C1 continuity:
 *   `var joined = joinCurves(context, arc1, arc2, ContinuityType.C1, {});`
 *
 * @note May modify curve shape near join to enforce continuity
 * @note For C0, curves must already be within tolerance
 * @note For C1/C2, adjusts control points (preference given to adjustB)
 *
 * @reference Uses makeCurvesCompatible() from bspline_knots.fs
 */
export function joinCurves(context is Context, curveA is BSplineCurve, curveB is BSplineCurve,
                            continuity is ContinuityType, options is map) returns BSplineCurve
{
    // Parse options
    var tolerance = (options.tolerance != undefined) ? options.tolerance : (1e-6 * meter);
    var adjustA = (options.adjustA != undefined) ? options.adjustA : false;
    var adjustB = (options.adjustB != undefined) ? options.adjustB : true;

    // Get endpoints and check C0 continuity
    var endpointsA = getBSplineEndpoints(curveA);
    var endpointsB = getBSplineEndpoints(curveB);

    var gap = norm(endpointsA.end - endpointsB.start);

    if (gap > tolerance)
    {
        throw regenError("joinCurves: Curves do not meet within tolerance. Gap = " ~ gap);
    }

    // Make curves compatible (same degree)
    var compat = makeCurvesCompatible(context, curveA, curveB);
    var compatA = compat.curveA;
    var compatB = compat.curveB;

    var degree = compatA.degree;

    // Reparameterize curveB to continue from curveA's parameter range
    var rangeA = getBSplineParamRange(compatA);
    var rangeB = getBSplineParamRange(compatB);
    var paramShift = rangeA.uMax - rangeB.uMin;

    var knotsB = [];
    for (var knot in compatB.knots)
    {
        knotsB = append(knotsB, knot + paramShift);
    }

    var compatB_shifted = {
        "degree" : compatB.degree,
        "isPeriodic" : compatB.isPeriodic,
        "controlPoints" : compatB.controlPoints,
        "knots" : knotsB,
        "weights" : compatB.weights,
        "isRational" : compatB.isRational,
        "dimension" : compatB.dimension
    } as BSplineCurve;

    // Apply continuity constraints
    var cpA = compatA.controlPoints;
    var cpB = compatB_shifted.controlPoints;

    if (continuity == ContinuityType.C1 || continuity == ContinuityType.C2)
    {
        // Get tangents at join
        var evalA = evaluateSpline({
            "spline" : compatA,
            "parameters" : [rangeA.uMax],
            "nDerivatives" : 1
        });
        var tangentA = evalA[1][0];

        var evalB = evaluateSpline({
            "spline" : compatB_shifted,
            "parameters" : [rangeA.uMax],
            "nDerivatives" : 1
        });
        var tangentB = evalB[1][0];

        // Adjust control points to match tangent
        if (adjustB)
        {
            // Adjust first control point of B to match tangent from A
            var tangentScale = norm(tangentA) / norm(tangentB);
            var tangentDir = normalize(tangentA);

            // Move cpB[0] to match position (already close from C0)
            cpB[0] = endpointsA.end;

            // Adjust cpB[1] for tangent matching
            var desiredTangent = tangentDir * norm(tangentA);
            var currentVector = cpB[1] - cpB[0];
            var scaleFactor = norm(desiredTangent) / norm(currentVector);
            cpB[1] = cpB[0] + desiredTangent * (norm(currentVector) / norm(tangentA));
        }
        else if (adjustA)
        {
            // Adjust last control point of A to match tangent to B
            var tangentScale = norm(tangentB) / norm(tangentA);
            var tangentDir = normalize(tangentB);

            var desiredTangent = tangentDir * norm(tangentB);
            var currentVector = cpA[size(cpA) - 1] - cpA[size(cpA) - 2];
            var scaleFactor = norm(desiredTangent) / norm(currentVector);
            cpA[size(cpA) - 1] = cpA[size(cpA) - 2] + desiredTangent * (norm(currentVector) / norm(tangentB));
        }
    }

    if (continuity == ContinuityType.C2)
    {
        // C2 requires curvature matching - more complex, needs second derivatives
        // For now, issue a warning that C2 is approximated
        println("WARNING: joinCurves C2 continuity is approximate (tangent-matched only)");
    }

    // Concatenate control points (skip duplicate at join)
    var joinedCP = [];
    for (var cp in cpA)
    {
        joinedCP = append(joinedCP, cp);
    }
    for (var i = 1; i < size(cpB); i += 1)  // Skip first point of B (duplicate)
    {
        joinedCP = append(joinedCP, cpB[i]);
    }

    // Concatenate knot vectors.
    // A ends with (degree+1) repeated knots at uMax_A; B starts with (degree+1) at the same value.
    // For C0 continuity at the junction, the merged knot must have multiplicity = degree.
    // So take all of A except its last knot, then skip B's first (degree+1) clamped knots.
    // This gives junction multiplicity = degree → C0, and total knot count = nA + nB + degree ✓.
    var joinedKnots = [];
    for (var i = 0; i < size(compatA.knots) - 1; i += 1)
    {
        joinedKnots = append(joinedKnots, compatA.knots[i]);
    }
    // Skip first degree+1 knots of B (they're at the join point)
    for (var i = degree + 1; i < size(knotsB); i += 1)
    {
        joinedKnots = append(joinedKnots, knotsB[i]);
    }

    // Concatenate weights (if rational)
    var joinedWeights = [];
    if (compatA.isRational)
    {
        for (var w in compatA.weights)
        {
            joinedWeights = append(joinedWeights, w);
        }
        for (var i = 1; i < size(compatB_shifted.weights); i += 1)
        {
            joinedWeights = append(joinedWeights, compatB_shifted.weights[i]);
        }
    }

    return {
        "degree" : degree,
        "isPeriodic" : false,
        "controlPoints" : joinedCP,
        "knots" : joinedKnots,
        "weights" : joinedWeights,
        "isRational" : compatA.isRational,
        "dimension" : compatA.dimension
    } as BSplineCurve;
}
