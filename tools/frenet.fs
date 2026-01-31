FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

/**
 * Frenet frame computation and coordinate transformations.
 *
 * Provides robust calculation of Frenet-Serret frames (tangent, normal, binormal)
 * for BSpline curves, along with transformations between Frenet frame coordinates
 * and world coordinates. Handles degenerate cases (zero curvature, straight lines).
 *
 * Frame Convention:
 * - zAxis = Tangent (direction of motion)
 * - xAxis = Normal (principal normal, points toward center of curvature)
 * - yAxis = Binormal (zAxis × xAxis, completes right-handed system)
 *
 * @source gordonSurface/modifyCurveEnd.fs
 */

/**
 * Epsilon offset from curve endpoints to avoid numerical degeneracy.
 * When evaluating at s=0 or s=1, offset by this amount inward.
 */
export const FRENET_EPSILON = 1e-5;

/**
 * Compute Frenet frame and curvature at a parameter on a BSpline curve.
 *
 * Calculates the Frenet-Serret frame (tangent, normal, binormal) and
 * curvature using the curve's first and second derivatives. Handles
 * degenerate cases where curvature is zero (straight lines).
 *
 * Frame definition:
 * - Tangent T = r'(t) / |r'(t)| (direction of motion) → zAxis
 * - Binormal B = (r'(t) × r''(t)) / |r'(t) × r''(t)| → yAxis
 * - Normal N = B × T (points toward center of curvature) → xAxis
 * - Curvature κ = |r'(t) × r''(t)| / |r'(t)|³
 *
 * For straight lines (κ ≈ 0), picks an arbitrary perpendicular normal.
 *
 * @param curve {BSplineCurve} : Curve to evaluate
 * @param s {number} : Parameter value (typically in [0,1])
 * @returns {EdgeCurvatureResult} : {frame: CoordSystem, curvature: ValueWithUnits}
 *                                   frame.origin = position on curve
 *                                   frame.zAxis = tangent
 *                                   frame.xAxis = normal
 *                                   (frame.yAxis = binormal, via cross)
 *
 * @example `computeFrenetFrame(myCurve, 0.5)` returns frame at curve midpoint
 *
 * @note Offsets parameters near 0 or 1 by FRENET_EPSILON to avoid endpoint degeneracy
 * @note For κ < 1e-10/meter, uses arbitrary normal perpendicular to tangent
 *
 * @source gordonSurface/modifyCurveEnd.fs:640-731
 */
export function computeFrenetFrame(curve is BSplineCurve, s is number) returns EdgeCurvatureResult
{
    // Offset slightly from exact endpoints to avoid degeneracy
    var evalParam = s;
    if (s < FRENET_EPSILON)
    {
        evalParam = FRENET_EPSILON;
    }
    else if (s > 1 - FRENET_EPSILON)
    {
        evalParam = 1 - FRENET_EPSILON;
    }

    // Get position, first derivative, second derivative
    var result = evaluateSpline({
        "spline" : curve,
        "parameters" : [evalParam],
        "nDerivatives" : 2
    });

    var position = result[0][0];      // r(t)
    var velocity = result[1][0];      // r'(t)
    var acceleration = result[2][0];  // r''(t)

    var speed = norm(velocity);  // |r'(t)|

    // Tangent: T = r'(t) / |r'(t)|  --> This becomes zAxis
    var tangent = normalize(velocity);

    // Cross product for binormal direction: r'(t) × r''(t)
    var crossProd = cross(velocity, acceleration);
    var crossNorm = norm(crossProd);

    // Curvature: κ = |r'(t) × r''(t)| / |r'(t)|³
    var curvature = crossNorm / (speed * speed * speed);

    // Handle degenerate case (straight line or near-zero curvature)
    var binormal;
    var normal;

    // Check for near-zero curvature (need to handle units in comparison)
    var isDegenerate = false;
    try silent
    {
        // crossNorm has units of length²/time² if velocity has length/time
        // For a unitless check, compare curvature to a small threshold
        isDegenerate = curvature < (1e-10 / meter);
    }
    catch
    {
        // If units don't work out, try direct comparison
        isDegenerate = crossNorm / meter / meter < 1e-12;
    }

    if (isDegenerate)
    {
        // Pick an arbitrary perpendicular direction for normal
        // Use the axis most perpendicular to tangent
        var absT = vector(abs(tangent[0]), abs(tangent[1]), abs(tangent[2]));
        var arbitrary;
        if (absT[0] <= absT[1] && absT[0] <= absT[2])
        {
            arbitrary = vector(1, 0, 0);
        }
        else if (absT[1] <= absT[2])
        {
            arbitrary = vector(0, 1, 0);
        }
        else
        {
            arbitrary = vector(0, 0, 1);
        }

        binormal = normalize(cross(tangent, arbitrary));
        normal = cross(binormal, tangent);
        curvature = 0 / meter;  // Explicitly zero curvature
    }
    else
    {
        // Standard Frenet frame
        binormal = normalize(crossProd);
        normal = cross(binormal, tangent);
    }

    // Build CoordSystem: zAxis = tangent, xAxis = normal, (yAxis = binormal via cross)
    var frame = coordSystem(position, normal, tangent);

    return {
        "frame" : frame,
        "curvature" : curvature
    } as EdgeCurvatureResult;
}

/**
 * Transform a direction vector from Frenet frame to world coordinates.
 *
 * Converts a direction expressed in Frenet coordinates [tangent, normal, binormal]
 * to world coordinates using the frame's basis vectors. No translation applied.
 *
 * @param localVector {Vector} : Direction in Frenet coordinates
 *                                [tangentComponent, normalComponent, binormalComponent]
 * @param frenetResult {map} : Result from computeFrenetFrame
 * @returns {Vector} : Direction in world coordinates
 *
 * @example Transform offset vector from Frenet to world:
 *   `frenetVectorToWorld(vector(1*mm, 0.5*mm, 0*mm), frameResult)`
 *
 * @source gordonSurface/modifyCurveEnd.fs:764-775
 */
export function frenetVectorToWorld(localVector is Vector, frenetResult is map) returns Vector
{
    var frame = frenetResult.frame;

    // Frame axes in world coordinates
    var tangent = frame.zAxis;
    var normal = frame.xAxis;
    var binormal = yAxis(frame);

    // localVector = [tangentComponent, normalComponent, binormalComponent]
    return localVector[0] * tangent + localVector[1] * normal + localVector[2] * binormal;
}

/**
 * Transform a direction vector from world coordinates to Frenet frame.
 *
 * Projects a world direction onto the Frenet frame's basis vectors.
 *
 * @param worldVector {Vector} : Direction in world coordinates
 * @param frenetResult {map} : Result from computeFrenetFrame
 * @returns {Vector} : Direction in Frenet coordinates [tangent, normal, binormal]
 *
 * @example Project world offset to Frenet frame:
 *   `worldVectorToFrenet(worldOffset, frameResult)`
 *
 * @source gordonSurface/modifyCurveEnd.fs:784-797
 */
export function worldVectorToFrenet(worldVector is Vector, frenetResult is map) returns Vector
{
    var frame = frenetResult.frame;

    var tangent = frame.zAxis;
    var normal = frame.xAxis;
    var binormal = yAxis(frame);

    return vector(
        dot(worldVector, tangent),
        dot(worldVector, normal),
        dot(worldVector, binormal)
    );
}

/**
 * Transform a point from Frenet frame to world coordinates.
 *
 * Converts a point expressed relative to the Frenet frame origin
 * in [tangent, normal, binormal] coordinates to world coordinates.
 *
 * @param localPoint {Vector} : Point in Frenet coordinates [tangent, normal, binormal]
 * @param frenetResult {map} : Result from computeFrenetFrame
 * @returns {Vector} : Point in world coordinates
 *
 * @example Place point 1mm along normal from curve:
 *   `frenetPointToWorld(vector(0*mm, 1*mm, 0*mm), frameResult)`
 *
 * @source gordonSurface/modifyCurveEnd.fs:743-752
 */
export function frenetPointToWorld(localPoint is Vector, frenetResult is map) returns Vector
{
    var frame = frenetResult.frame;

    // Rearrange [tangent, normal, binormal] to [x, y, z] for CoordSystem convention
    // CoordSystem: xAxis=normal, yAxis=binormal, zAxis=tangent
    var localXYZ = vector(localPoint[1], localPoint[2], localPoint[0]);

    return toWorld(frame) * localXYZ;
}

/**
 * Transform a point from world coordinates to Frenet frame.
 *
 * Converts a world point to coordinates relative to the Frenet frame.
 *
 * @param worldPoint {Vector} : Point in world coordinates
 * @param frenetResult {map} : Result from computeFrenetFrame
 * @returns {Vector} : Point in Frenet coordinates [tangent, normal, binormal]
 *
 * @example Get Frenet coordinates of a point:
 *   `worldPointToFrenet(somePoint, frameResult)`
 *
 * @source gordonSurface/modifyCurveEnd.fs:806-824
 */
export function worldPointToFrenet(worldPoint is Vector, frenetResult is map) returns Vector
{
    var frame = frenetResult.frame;

    // Get vector from frame origin to the point
    var relativePos = worldPoint - frame.origin;

    // Project onto frame axes
    var tangent = frame.zAxis;
    var normal = frame.xAxis;
    var binormal = yAxis(frame);

    return vector(
        dot(relativePos, tangent),
        dot(relativePos, normal),
        dot(relativePos, binormal)
    );
}
