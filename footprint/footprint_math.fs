FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// IMPORT: tools/math_utils.fs (for safeSign)
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/280a24d76f52bdbf44cd941d", version : "d9e09196718b914b96e84924");


/**
 * Shared mathematical utilities for footprint analysis.
 *
 * This module provides common geometric calculations used across
 * the footprint analysis and scaling features.
 */

/**
 * Compute curvature and geometric properties of a BSpline at a parameter value.
 *
 * Uses the planar curvature formula:
 *   κ = (x'y'' - y'x'') / (x'² + y'²)^(3/2)
 *
 * @param bspline : BSplineCurve to evaluate
 * @param u : Parameter value at which to compute curvature
 *
 * @returns map with:
 *   - point : Position vector at parameter u
 *   - tangent : Unit tangent vector at parameter u
 *   - curvatureMag : Magnitude of curvature (1/meter)
 *   - curvatureSigned : Signed curvature (1/meter)
 *   - sign : Sign of curvature (-1, 0, or 1)
 */
export function getBSplineCurvatureAtParam(bspline is BSplineCurve, u is number) returns map
{
    var result = evaluateSpline({ "spline" : bspline, "parameters" : [u], "nDerivatives" : 2 });
    var point = result[0][0];
    var d1 = result[1][0];  // first derivative
    var d2 = result[2][0];  // second derivative

    var xP = d1[0] / meter;   // unitless
    var yP = d1[1] / meter;
    var xPP = d2[0] / meter;
    var yPP = d2[1] / meter;

    var speedSquared = xP * xP + yP * yP;
    var denom = speedSquared * sqrt(speedSquared);

    var kSigned;
    var kMag;
    var sgn;

    if (abs(denom) < 1e-15)
    {
        kSigned = 0 / meter;
        kMag = 0 / meter;
        sgn = 0;
    }
    else
    {
        var kValue = (xP * yPP - yP * xPP) / denom;  // unitless (1/meter in real terms)
        kSigned = kValue / meter;
        kMag = abs(kValue) / meter;
        sgn = safeSign(kValue, 1e-12);
    }

    var tangent = (sqrt(xP * xP + yP * yP) > 1e-12) ? normalize(d1) : vector(1, 0, 0);

    return {
        "point" : point,
        "tangent" : tangent,
        "curvatureMag" : kMag,
        "curvatureSigned" : kSigned,
        "sign" : sgn
    };
}
