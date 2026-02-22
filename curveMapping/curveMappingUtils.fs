FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

//import curveChain
import(path : "670e82ad72abc97906ec9038", version : "a90138f4a3661f5d6800a1bc");
//import tools/bspline_data
import(path : "b1e8bfe71f67389ca210ed8b/e13e99b75ba5ce6d6380ddd5/b1c7f2116fb64e6b40bf53f4", version : "4fe0cca8e00a4cd812896a8c");

/**
 * Approximate BSpline through points with guaranteed endpoint interpolation.
 *
 * Uses interpolateIndices to guarantee endpoint interpolation.
 *
 * @param context : Onshape context
 * @param points : Points to fit (MUST include endpoints)
 * @param degree : Spline degree
 * @param options : {
 *                    tolerance: ValueWithUnits (default 1e-5 m)
 *                    maxControlPoints: number (default 50)
 *                    isPeriodic: boolean (default false)
 *                  }
 * @returns BSplineCurve fitted curve
 */
export function approximateWithEndpoints(context is Context, points is array,
                                         degree is number, options is map)
    returns BSplineCurve
{
    const tolerance = options.tolerance ?? (1e-5 * meter);
    const maxControlPoints = options.maxControlPoints ?? 50;
    const isPeriodic = options.isPeriodic ?? false;

    if (size(points) < degree + 1)
    {
        throw regenError("Not enough points for degree " ~ degree ~
                        " spline (need at least " ~ (degree + 1) ~ ")");
    }

    const target = approximationTarget({
        "positions" : points
    });

    // Ensure minimum degree requirement for approximateSpline (degree must be >= 2)
    const adjustedDegree = max(degree, 2);

    // Call standard approximation - returns ARRAY
    const curves = approximateSpline(context, {
        "targets" : [target],
        "degree" : adjustedDegree,
        "tolerance" : tolerance,
        "maxControlPoints" : maxControlPoints,
        "isPeriodic" : isPeriodic,
        "interpolateIndices" : [0, size(points) - 1]  // Force endpoint interpolation
    });

    // Extract first (and only) curve from array
    if (size(curves) == 0)
    {
        throw regenError("Failed to approximate spline through points");
    }
    const curve = curves[0];

    // Verify endpoint accuracy
    const paramRange = getBSplineParamRange(curve);
    const startEval = evaluateSpline({
        "spline" : curve,
        "parameters" : [paramRange.uMin]
    })[0][0];
    const endEval = evaluateSpline({
        "spline" : curve,
        "parameters" : [paramRange.uMax]
    })[0][0];

    const startError = norm(startEval - points[0]);
    const endError = norm(endEval - points[size(points) - 1]);

    const errorTol = 1e-7 * meter;

    if (startError > errorTol || endError > errorTol)
    {
        // Warning: endpoints not exact
        // This shouldn't happen with interpolateIndices, but log if it does
        println("WARNING: Endpoint approximation not exact.");
        println("  Start error: " ~ toString(startError));
        println("  End error: " ~ toString(endError));
    }

    return curve;
}

/**
 * Approximate BSpline through points with endpoint tangent constraints.
 * Uses Onshape's approximateSpline with derivative constraints to enforce
 * G1 continuity at endpoints.
 *
 * @param context : Onshape context
 * @param points : Points to fit (MUST include endpoints)
 * @param degree : Spline degree
 * @param options : {
 *                    tolerance: ValueWithUnits (default 1e-5 m)
 *                    maxControlPoints: number (default 50)
 *                    startTangent: Vector (required, unitless direction)
 *                    endTangent: Vector (required, unitless direction)
 *                  }
 * @returns BSplineCurve fitted curve with tangent constraints
 */
export function approximateWithTangents(context is Context,
                                       points is array,
                                       degree is number,
                                       options is map) returns BSplineCurve
{
    const tolerance = options.tolerance ?? (1e-5 * meter);
    const maxControlPoints = options.maxControlPoints ?? 50;

    // Validate inputs
    if (size(points) < degree + 1)
    {
        throw regenError("approximateWithTangents: Not enough points for degree " ~ degree);
    }

    // Extract tangents (required)
    if (options.startTangent == undefined || options.endTangent == undefined)
    {
        throw regenError("approximateWithTangents: startTangent and endTangent required");
    }

    // Estimate derivative magnitude from chord length
    // Per splineUtils.fs line 50: "magnitude is ignored" when parameters not specified
    // But we still need reasonable scale for numerical stability
    const chordLength = norm(points[size(points) - 1] - points[0]);
    const avgSegmentLength = chordLength / (size(points) - 1);

    const startDerivative = options.startTangent * avgSegmentLength;
    const endDerivative = options.endTangent * avgSegmentLength;

    // Build ApproximationTarget with derivatives
    const target = approximationTarget({
        "positions" : points,
        "startDerivative" : startDerivative,
        "endDerivative" : endDerivative
    });

    // Call approximateSpline with derivative constraints
    const curves = approximateSpline(context, {
        "targets" : [target],
        "degree" : max(degree, 2),  // Minimum degree 2
        "tolerance" : tolerance,
        "maxControlPoints" : maxControlPoints,
        "isPeriodic" : false,
        "interpolateIndices" : [0, size(points) - 1]  // Force endpoint interpolation
    });

    if (size(curves) == 0)
    {
        throw regenError("approximateWithTangents: approximateSpline returned no curves");
    }

    // Verify endpoints match within tolerance
    const curve = curves[0];
    const endpoints = getBSplineEndpoints(curve);

    const startError = norm(endpoints.start - points[0]);
    const endError = norm(endpoints.end - points[size(points) - 1]);

    if (startError > 1e-7 * meter || endError > 1e-7 * meter)
    {
        println("WARNING: approximateWithTangents endpoint mismatch");
        println("  Start error: " ~ startError);
        println("  End error: " ~ endError);
    }

    return curve;
}

/**
 * Ensure sampling array includes t=0.0 and t=1.0 exactly.
 *
 * @param parameters : Parameter array (may or may not include endpoints)
 * @returns Modified array with 0.0 and 1.0 included
 */
export function ensureSamplingIncludesEndpoints(parameters is array) returns array
{
    var result = parameters;

    // Check if 0.0 is included
    var hasZero = false;
    var hasOne = false;

    for (var param in parameters)
    {
        if (abs(param - 0.0) < 1e-10)
            hasZero = true;
        if (abs(param - 1.0) < 1e-10)
            hasOne = true;
    }

    // Prepend 0.0 if missing
    if (!hasZero)
    {
        result = concatenateArrays([[0.0], result]);
    }

    // Append 1.0 if missing
    if (!hasOne)
    {
        result = append(result, 1.0);
    }

    return result;
}

/**
 * Compute arc length of a BSpline curve using numerical integration.
 *
 * @param curve : BSplineCurve to measure
 * @param startParam : Start parameter
 * @param endParam : End parameter
 * @param numSamples : Number of samples for integration (default 50)
 * @returns Arc length in meters
 */
function computeBSplineArcLength(curve is BSplineCurve, startParam is number,
                                 endParam is number, numSamples is number) returns ValueWithUnits
{
    var totalLength = 0 * meter;
    var prevPoint = undefined;

    for (var i = 0; i <= numSamples; i += 1)
    {
        const t = i / numSamples;
        const param = startParam + t * (endParam - startParam);

        const point = evaluateSpline({
            "spline" : curve,
            "parameters" : [param]
        })[0][0];

        if (prevPoint != undefined)
        {
            totalLength += norm(point - prevPoint);
        }

        prevPoint = point;
    }

    return totalLength;
}

/**
 * Validate mapped curve quality.
 *
 * Checks:
 * - Endpoint accuracy
 * - Arc length preservation (if checkLength enabled)
 *
 * @param context : Onshape context
 * @param sourceCurve : Original curve
 * @param mappedCurve : Result curve
 * @param mapping : Mapping data structure (with expected target points)
 * @param options : {
 *                    endpointTolerance: ValueWithUnits (default 1e-6 m)
 *                    checkLength: boolean (default false)
 *                    expectedStartPoint: Vector (if checking endpoints)
 *                    expectedEndPoint: Vector (if checking endpoints)
 *                  }
 * @returns {
 *            valid: boolean,
 *            endpointError: ValueWithUnits,
 *            lengthRatio: number (if checkLength mode)
 *            warnings: array of strings
 *          }
 */
export function validateMappedCurve(context is Context,
                                    sourceCurve is BSplineCurve,
                                    mappedCurve is BSplineCurve,
                                    mapping is map,
                                    options is map) returns map
{
    const endpointTol = options.endpointTolerance ?? (1e-6 * meter);
    var warnings = [];
    var valid = true;

    // Get parameter ranges
    const sourceRange = getBSplineParamRange(sourceCurve);
    const mappedRange = getBSplineParamRange(mappedCurve);

    // Evaluate endpoints
    const mappedStart = evaluateSpline({
        "spline" : mappedCurve,
        "parameters" : [mappedRange.uMin]
    })[0][0];
    const mappedEnd = evaluateSpline({
        "spline" : mappedCurve,
        "parameters" : [mappedRange.uMax]
    })[0][0];

    // Check endpoint accuracy against expected targets (if provided)
    var endpointError = 0 * meter;

    if (options.expectedStartPoint != undefined && options.expectedEndPoint != undefined)
    {
        const startError = norm(mappedStart - options.expectedStartPoint);
        const endError = norm(mappedEnd - options.expectedEndPoint);
        endpointError = max(startError, endError);

        if (endpointError > endpointTol)
        {
            warnings = append(warnings, "Endpoint error: " ~ toString(endpointError));
            valid = false;
        }
    }

    // Check arc length if requested
    var lengthRatio = undefined;
    const checkLength = options.checkLength ?? false;

    if (checkLength)
    {
        const sourceLength = computeBSplineArcLength(sourceCurve, sourceRange.uMin, sourceRange.uMax, 50);
        const mappedLength = computeBSplineArcLength(mappedCurve, mappedRange.uMin, mappedRange.uMax, 50);

        lengthRatio = mappedLength / sourceLength;

        // Warn if length ratio is extreme
        if (lengthRatio < 0.5 || lengthRatio > 2.0)
        {
            warnings = append(warnings,
                "Large length change: ratio = " ~ toString(lengthRatio));
        }
    }

    return {
        "valid" : valid,
        "endpointError" : endpointError,
        "lengthRatio" : lengthRatio,
        "warnings" : warnings
    };
}

/**
 * Check if two chains are approximately coplanar.
 *
 * @param context : Onshape context
 * @param chain1 : First chain
 * @param chain2 : Second chain
 * @param options : {
 *                    numSamples: number (default 20)
 *                    tolerance: ValueWithUnits (default 1e-3 m)
 *                  }
 * @returns {
 *            coplanar: boolean,
 *            maxDeviation: ValueWithUnits,
 *            fittedPlane: Plane,
 *            angle: ValueWithUnits // Angle between normals if not coplanar
 *          }
 */
export function checkCoplanarity(context is Context,
                                 chain1 is CurveChain,
                                 chain2 is CurveChain,
                                 options is map) returns map
{
    const numSamples = options.numSamples ?? 20;
    const tolerance = options.tolerance ?? (1e-3 * meter);

    // Sample both chains
    const samples1 = sampleChainUniform(context, chain1, numSamples);
    const samples2 = sampleChainUniform(context, chain2, numSamples);

    // Combine point clouds
    var allPoints = concatenateArrays([samples1.points, samples2.points]);

    // Compute centroid of all points
    var centroid = vector(0, 0, 0) * meter;
    for (var pt in allPoints)
    {
        centroid = centroid + pt;
    }
    centroid = centroid / size(allPoints);

    // Simple plane fit: use cross product of first and last tangents from chain1
    // Note: This is a simplified approach; proper SVD would be better
    const tangent1 = samples1.tangents[0];
    const tangent2 = samples1.tangents[size(samples1.tangents) - 1];

    // Rough normal estimate
    var normal = cross(tangent1, tangent2);
    if (norm(normal) < (1e-10 * meter))
    {
        // Chains are nearly parallel, use arbitrary perpendicular
        normal = perpendicularVector(tangent1);
    }
    else
    {
        normal = normalize(normal);
    }

    const fittedPlane = plane(centroid, normal);

    // Measure max deviation from plane
    var maxDeviation = 0 * meter;
    for (var pt in allPoints)
    {
        const dev = abs(dot(pt - centroid, normal));
        if (dev > maxDeviation)
        {
            maxDeviation = dev;
        }
    }

    const coplanar = (maxDeviation < tolerance);

    // If not coplanar, compute angle between chain normals
    var angle = undefined;
    if (!coplanar)
    {
        // Get normals from Frenet frames at midpoints
        const frame1 = getChainFrenetFrame(context, chain1, 0.5);
        const frame2 = getChainFrenetFrame(context, chain2, 0.5);

        // Normal is xAxis in Frenet frame
        angle = angleBetween(frame1.frame.xAxis, frame2.frame.xAxis);
    }

    return {
        "coplanar" : coplanar,
        "maxDeviation" : maxDeviation,
        "fittedPlane" : fittedPlane,
        "angle" : angle
    };
}

/**
 * Validate tangent continuity between curves.
 * Returns angle error and whether curves are G1 continuous.
 *
 * @param context : Onshape context
 * @param curveA : First curve
 * @param curveB : Second curve
 * @param options : {
 *                    tolerance: ValueWithUnits (default 1e-3 rad)
 *                  }
 * @returns {
 *            continuous: boolean,
 *            angleError: ValueWithUnits,
 *            tangentA: Vector,
 *            tangentB: Vector
 *          }
 */
export function validateTangentContinuity(context is Context,
                                         curveA is BSplineCurve,
                                         curveB is BSplineCurve,
                                         options is map) returns map
{
    const tolerance = options.tolerance ?? (1e-3 * radian);

    // Get endpoints
    const rangeA = getBSplineParamRange(curveA);
    const rangeB = getBSplineParamRange(curveB);

    // Evaluate tangents at join (use evaluateSpline with nDerivatives: 1)
    const evalA = evaluateSpline({
        "spline" : curveA,
        "parameters" : [rangeA.uMax],
        "nDerivatives" : 1
    });
    const tangentA = normalize(evalA[1][0]);  // evalA[1] = array of derivatives

    const evalB = evaluateSpline({
        "spline" : curveB,
        "parameters" : [rangeB.uMin],
        "nDerivatives" : 1
    });
    const tangentB = normalize(evalB[1][0]);

    // Compute angle between tangents
    const dotProduct = dot(tangentA, tangentB);
    const angleError = acos(clamp(dotProduct, -1, 1));
    const continuous = (angleError < tolerance);

    return {
        "continuous" : continuous,
        "angleError" : angleError,
        "tangentA" : tangentA,
        "tangentB" : tangentB
    };
}
