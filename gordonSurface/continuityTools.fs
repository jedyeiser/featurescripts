FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// import tools/frenet
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/a19a275a032ee47f4dbcc83c", version : "65e923a8d375058271c92fbc");
//import constEnums
import(path : "050a4670bd42b2ca8da04540", version : "b463eaf5c39ae77152ed2484");


/**
 * Compute tangent and curvature constraints from a reference edge or face.
 *
 * @param context {Context}
 * @param ref {Query} : Reference edge or face
 * @param curve {BSplineCurve} : The curve being constrained (used for approach direction on faces)
 * @param endParam {number} : 0 or 1 — which endpoint
 * @returns {map} : { "tangent": Vector, "curvature": ValueWithUnits }
 */
export function computeRefContinuityConstraints(context is Context, ref is Query, curve is BSplineCurve, endParam is number) returns map
{
    // Get endpoint position
    var endPoint = evaluateSpline({
        "spline" : curve,
        "parameters" : [endParam]
    })[0][0];

    // Check if reference is edge or face
    var edgeQuery = qEntityFilter(ref, EntityType.EDGE);
    var faceQuery = qEntityFilter(ref, EntityType.FACE);

    if (!isQueryEmpty(context, edgeQuery))
    {
        return computeEdgeContinuityConstraints(context, edgeQuery, endPoint);
    }
    else if (!isQueryEmpty(context, faceQuery))
    {
        return computeFaceContinuityConstraints(context, faceQuery, curve, endParam);
    }
    else
    {
        // Fallback — return curve's own tangent/curvature (no constraint)
        var frame = computeFrenetFrame(curve, endParam);
        return {
            "tangent" : frame.frame.zAxis,
            "curvature" : frame.curvature
        };
    }
}

/**
 * Compute tangent and curvature from a reference edge at a point.
 */
export function computeEdgeContinuityConstraints(context is Context, edge is Query, point is Vector) returns map
{
    // Find parameter on edge closest to point
    var distResult = evDistance(context, {
        "side0" : edge,
        "side1" : point
    });

    var edgeParam = distResult.sides[0].parameter;

    // Get edge as BSpline and compute Frenet frame
    var edgeCurve = evApproximateBSplineCurve(context, { "edge" : edge });
    var frame = computeFrenetFrame(edgeCurve, edgeParam);

    return {
        "tangent" : frame.frame.zAxis,
        "curvature" : frame.curvature
    };
}

/**
 * Compute tangent and curvature for a curve meeting a face.
 * Projects curve's approach direction onto tangent plane,
 * then computes surface curvature in that direction.
 */
export function computeFaceContinuityConstraints(context is Context, face is Query, curve is BSplineCurve, endParam is number) returns map
{
    // Get endpoint position
    var endPoint = evaluateSpline({
        "spline" : curve,
        "parameters" : [endParam]
    })[0][0];

    // Get curve's approach direction (tangent at endpoint)
    var curveFrame = computeFrenetFrame(curve, endParam);
    var approachDirection = curveFrame.frame.zAxis;

    // Find UV parameter on face
    var distResult = evDistance(context, {
        "side0" : face,
        "side1" : endPoint
    });
    var uvParam = distResult.sides[0].parameter;

    // Get face normal at that point
    var tangentPlane = evFaceTangentPlane(context, {
            "face" : face,
            "parameter" : uvParam
    });
    var faceNormal = tangentPlane.normal;


    // Project approach direction onto tangent plane
    var projected = approachDirection - dot(approachDirection, faceNormal) * faceNormal;
    var projNorm = norm(projected);

    var tangent;
    if (projNorm < 1e-10)
    {
        // Approach is perpendicular to face — pick arbitrary direction in tangent plane
        // Use face's principal direction as fallback
        var faceCurvature = evFaceCurvature(context, {
            "face" : face,
            "parameter" : uvParam
        });
        tangent = faceCurvature.minDirection;
    }
    else
    {
        tangent = projected / projNorm;
    }

    // Get face curvature and compute curvature in tangent direction (Euler's formula)
    var faceCurvature = evFaceCurvature(context, {
        "face" : face,
        "parameter" : uvParam
    });

    var cosTheta = dot(tangent, faceCurvature.minDirection);
    var sinTheta = dot(tangent, faceCurvature.maxDirection);
    var curvature = faceCurvature.minCurvature * cosTheta * cosTheta
                  + faceCurvature.maxCurvature * sinTheta * sinTheta;

    return {
        "tangent" : tangent,
        "curvature" : curvature
    };
}

/**
 * Adjust control points to enforce tangent direction at an endpoint.
 *
 * @param curve {BSplineCurve}
 * @param endParam {number} : 0 or 1
 * @param targetTangent {Vector} : Desired tangent direction (will be normalized)
 * @returns {BSplineCurve} : Modified curve
 */
export function enforceG1AtEnd(curve is BSplineCurve, endParam is number, targetTangent is Vector) returns BSplineCurve
{
    var cps = curve.controlPoints;
    var n = size(cps);

    // Get current tangent at endpoint
    var currentFrame = computeFrenetFrame(curve, endParam);
    var currentTangent = currentFrame.frame.zAxis;

    // Check sign - flip target if needed
    var normalizedTarget = normalize(targetTangent);
    if (dot(currentTangent, normalizedTarget) < 0)
    {
        normalizedTarget = -normalizedTarget;
    }

    // For a clamped B-spline, tangent at endpoint is proportional to
    // the vector from first to second control point (for param 0)
    // or second-to-last to last control point (for param 1)

    var newCPs = cps;  // Copy

    if (endParam == 0)
    {
        // Tangent at s=0 is along (cp[1] - cp[0])
        // Keep cp[0] fixed (it's the endpoint position)
        // Move cp[1] to enforce tangent direction while preserving distance
        var currentVec = cps[1] - cps[0];
        var dist = norm(currentVec);
        newCPs[1] = cps[0] + dist * normalizedTarget;
    }
    else  // endParam == 1
    {
        // Tangent at s=1 is along (cp[n-1] - cp[n-2])
        // Keep cp[n-1] fixed
        // Move cp[n-2] to enforce tangent direction
        var currentVec = cps[n - 1] - cps[n - 2];
        var dist = norm(currentVec);
        // Note: tangent points FROM cp[n-2] TO cp[n-1], so:
        newCPs[n - 2] = cps[n - 1] - dist * normalizedTarget;
    }

    return bSplineCurve({
        "degree" : curve.degree,
        "isPeriodic" : curve.isPeriodic,
        "isRational" : curve.isRational,
        "controlPoints" : newCPs,
        "knots" : curve.knots,
        "weights" : curve.weights
    });
}

/**
 * Adjust control points to approximate curvature at an endpoint.
 *
 * For a cubic B-spline with clamped ends, curvature at endpoint depends on
 * the first three control points (for param 0) or last three (for param 1).
 *
 * @param curve {BSplineCurve}
 * @param endParam {number} : 0 or 1
 * @param targetCurvature {ValueWithUnits} : Desired curvature (1/length units)
 * @param g2Mode {G2Mode} : EXACT (not implemented) or BEST_EFFORT
 * @returns {BSplineCurve} : Modified curve
 */
export function enforceG2AtEnd(curve is BSplineCurve, endParam is number, targetCurvature is ValueWithUnits, g2Mode is G2Mode) returns BSplineCurve
{
    var cps = curve.controlPoints;
    var n = size(cps);
    var degree = curve.degree;

    if (degree < 3)
    {
        // Can't enforce G2 on degree < 3 curve
        return curve;
    }

    if (g2Mode == G2Mode.EXACT)
    {
        throw regenError("G2 EXACT mode is not yet implemented");
    }

    var newCPs = cps;

    // Current curvature
    var currentFrame = computeFrenetFrame(curve, endParam);
    var currentCurvature = currentFrame.curvature;

    // BEST_EFFORT: adjust the third control point to influence curvature
    // This is approximate - curvature depends on the geometry in a nonlinear way
    if (endParam == 0)
    {
        // Move cp[2] toward/away from the tangent line to adjust curvature
        var tangentDir = normalize(cps[1] - cps[0]);
        var toCP2 = cps[2] - cps[1];

        // Component perpendicular to tangent affects curvature
        var perpComponent = toCP2 - dot(toCP2, tangentDir) * tangentDir;
        var perpDist = norm(perpComponent);

        if (perpDist > 1e-10 * meter)
        {
            var perpDir = perpComponent / perpDist;

            // Scale perpendicular distance to adjust curvature
            var curvatureRatio = (targetCurvature / currentCurvature);

            // Clamp ratio to avoid wild adjustments
            curvatureRatio = min(max(curvatureRatio, 0.1), 10);

            var newPerpDist = perpDist * curvatureRatio;
            var parallelComponent = dot(toCP2, tangentDir) * tangentDir;

            newCPs[2] = cps[1] + parallelComponent + newPerpDist * perpDir;
        }
    }
    else  // endParam == 1
    {
        var tangentDir = normalize(cps[n - 1] - cps[n - 2]);
        var toCP = cps[n - 3] - cps[n - 2];

        var perpComponent = toCP - dot(toCP, tangentDir) * tangentDir;
        var perpDist = norm(perpComponent);

        if (perpDist > 1e-10 * meter)
        {
            var perpDir = perpComponent / perpDist;
            var curvatureRatio = (targetCurvature / currentCurvature);
            curvatureRatio = min(max(curvatureRatio, 0.1), 10);

            var newPerpDist = perpDist * curvatureRatio;
            var parallelComponent = dot(toCP, tangentDir) * tangentDir;

            newCPs[n - 3] = cps[n - 2] + parallelComponent + newPerpDist * perpDir;
        }
    }

    return bSplineCurve({
        "degree" : curve.degree,
        "isPeriodic" : curve.isPeriodic,
        "isRational" : curve.isRational,
        "controlPoints" : newCPs,
        "knots" : curve.knots,
        "weights" : curve.weights
    });
}
