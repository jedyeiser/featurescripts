FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// IMPORT: xSectBeamAnalysis.fs
import(path : "ebac109589e3bf405d3f3ae7", version : "9e8676f449d3bc6ce225a1b8");

/**
 * GENERATE BASELINE SOLVER
 * ========================
 *
 * Beam-bending and geometry solvers for the Generate Baseline feature.
 * Imported by generateBaseline.fs.
 *
 * Call hierarchy (feature body entry point → leaves):
 *   buildBaseline
 *     ├─ solveCamberBeam   (uses interpolateEI from xSectBeamAnalysis)
 *     │    OR solveCamberCubic
 *     ├─ computeTipZ
 *     ├─ solveRockerQuadratic
 *     ├─ interpZ
 *     ├─ slopeAt
 *     ├─ measureCamberHeight
 *     └─ rotateTranslate
 *
 * Dependencies:
 *   - onshape/std/common.fs  — standard FeatureScript library
 *   - xSectBeamAnalysis.fs   — interpolateEI
 */


// =============================================================================
// CAMBER POCKET — BEAM BENDING  (off-center simply-supported)
// =============================================================================

/**
 * Solve deflection shape of a simply-supported beam with variable EI,
 * loaded off-center at xLoad with P = 1 N.
 *
 * Returns dense 2D samples [{x, z}] with max(z) = H * meter.
 *
 * All internal arithmetic uses plain numbers (SI units stripped).
 * κ values are stored as plain numbers where the physical κ[1/m] = kappa_plain[i] / m.
 * dx_m is the step size in meters (plain).
 * slope[i] = ∫₀ˣ κ dx   (dimensionless, angle in rad)
 * defl[i]  = ∫₀ˣ slope dx  (plain meters)
 *
 * BCs enforced by subtracting the chord between endpoints.
 *
 * @param eiData {array} : Sorted [{ "x", "EI" }] from xSectBeamAnalysis.getEIFromEdges
 * @param xFRCP {ValueWithUnits} : Fore rocker contact point X
 * @param xARCP {ValueWithUnits} : Aft rocker contact point X
 * @param xLoad {ValueWithUnits} : Load application X
 * @param H {number} : Target max deflection in plain meters
 * @returns {array} : [{x : ValueWithUnits, z : ValueWithUnits}]
 */
export function solveCamberBeam(eiData is array, xFRCP is ValueWithUnits, xARCP is ValueWithUnits, xLoad is ValueWithUnits, H) returns array
{
    var N   = size(eiData);
    var L   = xARCP - xFRCP;
    var dx  = L / N;
    var dx_m = dx / meter;   // plain number

    // Reactions for P = 1 N at xLoad (RA and RB are dimensionless ratios)
    var b_r = (xARCP - xLoad) / L;   // b/L  (dimensionless)
    var a_r = (xLoad - xFRCP) / L;   // a/L  (dimensionless)

    // Build x positions and compute κ (plain numbers; κ_physical[1/m] = k/m)
    var xs    = [];
    var kappa = [];

    for (var i = 0; i <= N; i += 1)
    {
        var x = xFRCP + i * dx;
        xs = append(xs, x);

        // Moment for P = 1 N:  M(x) = RA * (x - xFRCP) or RB * (xARCP - x)
        // M has ValueWithUnits of meters (P factor is dimensionless here)
        var M_plain;
        if (x <= xLoad)
        {
            M_plain = b_r * ((x - xFRCP) / meter);   // plain (meters of moment / 1N)
        }
        else
        {
            M_plain = a_r * ((xARCP - x) / meter);
        }

        var EI = interpolateEI(eiData, x);
        var k = 0.0;
        if (EI >= 1e-10 * newton * meter * meter)
        {
            // κ_plain = M_plain_m / EI_Nm2
            // Physical κ[1/m] = (M[N·m] / EI[N·m²]) = (1N * M_plain_m) / (EI_Nm2 * N·m²)
            //                  = M_plain_m / (EI_Nm2 * m) → kappa_plain / m ✓
            var EI_plain = EI / (newton * meter * meter);
            k = M_plain / EI_plain;
        }
        kappa = append(kappa, k);
    }

    // Integrate κ → slope (trapz)
    // ds = κ[1/m] * dx[m] = κ_plain * dx_m  (dimensionless)
    var slope = [];
    slope = append(slope, 0.0);
    for (var i = 1; i <= N; i += 1)
    {
        var ds = (kappa[i - 1] + kappa[i]) / 2 * dx_m;
        slope = append(slope, slope[i - 1] + ds);
    }

    // Integrate slope → deflection (trapz)
    // dd = slope[dimensionless] * dx[m] = slope * dx_m  → plain meters
    var defl = [];
    defl = append(defl, 0.0);
    for (var i = 1; i <= N; i += 1)
    {
        var dd = (slope[i - 1] + slope[i]) / 2 * dx_m;
        defl = append(defl, defl[i - 1] + dd);
    }

    // Enforce BCs: y(0) = 0, y(N) = 0  (subtract chord)
    var yN = defl[N];
    var corrected = [];
    for (var i = 0; i <= N; i += 1)
    {
        corrected = append(corrected, defl[i] - yN * (i / N));
    }

    // Find minimum (largest downward deflection; negative value)
    var yMin = 0.0;
    for (var i = 0; i <= N; i += 1)
    {
        if (corrected[i] < yMin)
        {
            yMin = corrected[i];
        }
    }

    // Scale: corrected[i]/yMin is positive when corrected[i] is negative
    var result = [];
    for (var i = 0; i <= N; i += 1)
    {
        var zVal = 0.0;
        if (yMin < -1e-15)
        {
            zVal = corrected[i] / yMin * H;
        }
        result = append(result, { "x" : xs[i], "z" : zVal * meter });
    }

    return result;
}


// =============================================================================
// CAMBER POCKET — CONSTRAINED CUBIC  (no EI)
// =============================================================================

/**
 * Solve a polynomial f(x) = a·x³ + b·x² + c·x + d satisfying:
 *   f(xFRCP) = 0,  f(xARCP) = 0,  f(xLoad) = H,  f'(xLoad) = 0
 *
 * Works in shifted local coordinates (u = x - xFRCP, in meters).
 * For load exactly at midpoint (L = 2uL), the cubic degenerates to a
 * parabola (a = 0); this case is handled explicitly.
 *
 * @param xFRCP {ValueWithUnits}
 * @param xARCP {ValueWithUnits}
 * @param xLoad {ValueWithUnits}
 * @param H {number} : Target height in plain meters
 * @returns {array} : [{x : ValueWithUnits, z : ValueWithUnits}]
 */
export function solveCamberCubic(xFRCP is ValueWithUnits, xARCP is ValueWithUnits,
                           xLoad is ValueWithUnits, H) returns array
{
    var N = 100;

    // Work in local coords (u = x - xFRCP, plain meters)
    var L_m  = (xARCP - xFRCP) / meter;
    var uL_m = (xLoad  - xFRCP) / meter;

    var a = 0.0;
    var b = 0.0;
    var c = 0.0;
    var d = 0.0;

    // d = f(0) = 0  always

    var denom1 = L_m - 2 * uL_m;   // L - 2uL

    if (abs(denom1) < 1e-9 * abs(L_m))
    {
        // Load near midpoint → a = 0, parabola
        // From: f(L) = bL² + cL = 0  →  c = -bL
        //       f(uL) = b·uL² + c·uL = b·uL² - bL·uL = b·uL·(uL - L) = H
        //       b = H / (uL·(uL - L))
        var denom_mid = uL_m * (uL_m - L_m);
        if (abs(denom_mid) < 1e-20)
        {
            // Degenerate — return flat
            var flatPts = [];
            for (var i = 0; i <= N; i += 1)
            {
                flatPts = append(flatPts, { "x" : xFRCP + (i / N) * (xARCP - xFRCP), "z" : 0 * meter });
            }
            return flatPts;
        }
        b = H / denom_mid;
        c = -b * L_m;
        a = 0.0;
    }
    else
    {
        // General case: full cubic
        // From (2) and (4), algebraically eliminate to get b/a and c/a,
        // then solve for a from constraint (3).
        var bOverA = -(L_m * L_m - 3 * uL_m * uL_m) / denom1;
        var cOverA = -3 * uL_m * uL_m - 2 * bOverA * uL_m;
        var denom2 = uL_m * uL_m * uL_m + bOverA * uL_m * uL_m + cOverA * uL_m;
        if (abs(denom2) < 1e-20)
        {
            // Degenerate — return flat
            var flatPts2 = [];
            for (var i = 0; i <= N; i += 1)
            {
                flatPts2 = append(flatPts2, { "x" : xFRCP + (i / N) * (xARCP - xFRCP), "z" : 0 * meter });
            }
            return flatPts2;
        }
        a = H / denom2;
        b = a * bOverA;
        c = a * cOverA;
    }

    // Sample
    var result = [];
    for (var i = 0; i <= N; i += 1)
    {
        var u = L_m * i / N;
        var z = a * u * u * u + b * u * u + c * u + d;
        result = append(result, {
            "x" : xFRCP + u * meter,
            "z" : z * meter
        });
    }

    return result;
}


export function quadraticSplineFromTangent(startPoint is Vector, endPoint is Vector, startTangent is Vector, tension is number) returns BSplineCurve
{
    // Compute middle control point from start tangent
    const tHat = normalize(startTangent);
    const s = norm(endPoint - startPoint) * tension;
    const midControl = startPoint + s * tHat;

    // Degree-2 clamped knot vector: [0,0,0,1,1,1]
    // Size rule: 1 + degree + nControlPoints = 1 + 2 + 3 = 6
    const knots = [0, 0, 0, 1, 1, 1] as KnotArray;

    return bSplineCurve({
            "degree"        : 2,
            "dimension"     : 3,
            "isRational"    : false,
            "isPeriodic"    : false,
            "controlPoints" : [startPoint, midControl, endPoint],
            "knots"         : knots
    });
}

function refineMinZ(curve is BSplineCurve, tLo is number, tHi is number, tolerance is ValueWithUnits) returns Vector
{
    var lo = tLo;
    var hi = tHi;
    var prevBestZ = undefined;

    for (var iter = 0; iter < 100; iter += 1)
    {
        const m1 = lo + (hi - lo) / 3;
        const m2 = hi - (hi - lo) / 3;

        const result = evaluateSpline({
                "spline"      : curve,
                "parameters"  : [m1, m2, (lo + hi) / 2]
        });

        const z1      = result[0][0][2];
        const z2      = result[0][1][2];
        const bestZ   = result[0][2][2];

        if (z1 < z2)
            hi = m2;
        else
            lo = m1;

        if (prevBestZ != undefined && abs(bestZ - prevBestZ) < tolerance)
            break;

        prevBestZ = bestZ;
    }

    // Final evaluation at midpoint
    return evaluateSpline({
            "spline"     : curve,
            "parameters" : [(lo + hi) / 2]
    })[0][0];
}


export function findMinZBothSides(curves is array, xPosition is ValueWithUnits, stdDir is boolean) returns map
{
    const N = 50;
    const tolerance = 0.1 * millimeter;

    var fbFound     = false;
    var abFound     = false;
    var fbBestZ     = undefined;
    var abBestZ     = undefined;
    var fbBestCurve = undefined;
    var abBestCurve = undefined;
    var fbParamLo   = undefined;
    var fbParamHi   = undefined;
    var abParamLo   = undefined;
    var abParamHi   = undefined;

    for (var curveIdx = 0; curveIdx < size(curves); curveIdx += 1)
    {
        const curve  = curves[curveIdx];
        const knots  = curve.knots;
        const tStart = knots[0];
        const tEnd   = knots[size(knots) - 1];

        // Build parameter array for all N samples in one batch call
        var params = makeArray(N);
        for (var i = 0; i < N; i += 1)
            params[i] = tStart + (tEnd - tStart) * i / (N - 1);

        const result = evaluateSpline({
                "spline"     : curve,
                "parameters" : params
        });

        for (var i = 0; i < N; i += 1)
        {
            const pt  = result[0][i];
            const ptX = pt[0];
            const ptZ = pt[2];

            const onFbSide = stdDir ? (ptX < xPosition) : (ptX > xPosition);

            const tLo = params[i];
            const tHi = (i < N - 1) ? params[i + 1] : tEnd;

            if (onFbSide && (!fbFound || ptZ < fbBestZ))
            {
                fbFound     = true;
                fbBestZ     = ptZ;
                fbBestCurve = curve;
                fbParamLo   = tLo;
                fbParamHi   = tHi;
            }
            else if (!onFbSide && (!abFound || ptZ < abBestZ))
            {
                abFound     = true;
                abBestZ     = ptZ;
                abBestCurve = curve;
                abParamLo   = tLo;
                abParamHi   = tHi;
            }
        }
    }

    if (!fbFound)
        throw regenError("findMinZBothSides: no curve data on FB side of xPosition");
    if (!abFound)
        throw regenError("findMinZBothSides: no curve data on AB side of xPosition");

    return {
        "fbMin" : refineMinZ(fbBestCurve, fbParamLo, fbParamHi, tolerance),
        "abMin" : refineMinZ(abBestCurve, abParamLo, abParamHi, tolerance)
    };
}

// Returns the X-distance from startPoint to the minimum-Z point on the curve,
// for a given tension value. Pure helper — no solver state.
function computeMinZXDist(startPoint is Vector, endPoint is Vector, startTangent is Vector, tension is number) returns ValueWithUnits
{
    const tHat    = normalize(startTangent);
    const s       = norm(endPoint - startPoint) * tension;
    const midCtrl = startPoint + s * tHat;

    const z0 = startPoint[2];
    const z1 = midCtrl[2];
    const z2 = endPoint[2];

    const denom = z0 - 2 * z1 + z2;

    if (abs(denom) < 1e-9 * meter)
        throw regenError("Curve Z profile is linear — no interior minimum exists");

    const tStar = (z0 - z1) / denom; // dimensionless: length / length

    if (tStar < 0 || tStar > 1)
        throw regenError("Z minimum falls outside curve domain [0, 1] at this tension");

    const x0 = startPoint[0];
    const x1 = midCtrl[0];
    const x2 = endPoint[0];

    const oneMinusT = 1 - tStar;
    const xAtMin = oneMinusT * oneMinusT * x0
                 + 2 * tStar * oneMinusT * x1
                 + tStar * tStar * x2;

    return abs(xAtMin - x0); // ValueWithUnits (length)
}


export function solveForTension(startPoint is Vector, endPoint is Vector, startTangent is Vector, distFromStart is ValueWithUnits) returns number
{
    var tLo = 0.01;
    var tHi = 0.99;

    var fLo = computeMinZXDist(startPoint, endPoint, startTangent, tLo) - distFromStart;
    const fHi = computeMinZXDist(startPoint, endPoint, startTangent, tHi) - distFromStart;

    if (fLo * fHi > 0)
        throw regenError("distFromStart is not achievable within tension range [0.01, 0.99]");

    const maxIter   = 60;
    const tolerance = 1e-9 * meter; // ValueWithUnits — matches fMid units

    for (var i = 0; i < maxIter; i += 1)
    {
        const tMid = (tLo + tHi) / 2;
        const fMid = computeMinZXDist(startPoint, endPoint, startTangent, tMid) - distFromStart;

        if (abs(fMid) < tolerance || (tHi - tLo) < 1e-12)
            return tMid;

        if (fLo * fMid < 0)
            tHi = tMid;
        else
        {
            tLo = tMid;
            fLo = fMid;
        }
    }

    return (tLo + tHi) / 2;
}

function rotatePointAboutY(pt is Vector, angle is ValueWithUnits) returns Vector
{
    const c = cos(angle);
    const s = sin(angle);
    return vector(
        pt[0] * c - pt[2] * s,
        pt[1],
        pt[0] * s + pt[2] * c
    );
}

export function transformCurves(curves is array, fbMin is Vector, abMin is Vector) returns array
{
    // Angle of the line fbMin->abMin in the XZ plane
    // Divide by millimeter to get unitless values for atan2
    const dx = (abMin[0] - fbMin[0]) / millimeter;
    const dz = (abMin[2] - fbMin[2]) / millimeter;
    const theta = atan2(dz, dx);

    // Rotate fbMin to find the Z offset we need to remove
    const rotatedFbMin = rotatePointAboutY(fbMin, -theta);
    const zOffset = rotatedFbMin[2];

    var result = [];
    for (var curveIdx = 0; curveIdx < size(curves); curveIdx += 1)
    {
        const curve = curves[curveIdx];

        var newControlPoints = [];
        for (var ptIdx = 0; ptIdx < size(curve.controlPoints); ptIdx += 1)
        {
            const rotated = rotatePointAboutY(curve.controlPoints[ptIdx], -theta);
            newControlPoints = append(newControlPoints, rotated - vector(0 * millimeter, 0 * millimeter, zOffset));
        }

        result = append(result, bSplineCurve({
                "degree"        : curve.degree,
                "dimension"     : curve.dimension,
                "isRational"    : curve.isRational,
                "isPeriodic"    : curve.isPeriodic,
                "controlPoints" : newControlPoints,
                "knots"         : curve.knots
        }));
    }

    return result;
}


export function solveZAtX(curves is array, xPosition is ValueWithUnits) returns ValueWithUnits
{
    const N = 50;
    const tolerance = 0.1 * millimeter;

    var foundCurve = undefined;
    var bracketLo = undefined;
    var bracketHi = undefined;

    for (var curveIdx = 0; curveIdx < size(curves); curveIdx += 1)
    {
        const curve = curves[curveIdx];

        for (var i = 0; i < N - 1; i += 1)
        {
            const t0 = i / (N - 1);
            const t1 = (i + 1) / (N - 1);
            const x0 = evaluateSpline({ "spline": curve, "parameters": [t0] })[0][0][0];
            const x1 = evaluateSpline({ "spline": curve, "parameters": [t1] })[0][0][0];

            if ((x0 <= xPosition && x1 >= xPosition) || (x0 >= xPosition && x1 <= xPosition))
            {
                foundCurve = curve;
                bracketLo = t0;
                bracketHi = t1;
                break;
            }
        }

        if (foundCurve != undefined)
            break;
    }

    if (foundCurve == undefined)
        throw regenError("solveZAtX: no curve data found at provided x position");

    var lo = bracketLo;
    var hi = bracketHi;

    for (var iter = 0; iter < 100; iter += 1)
    {
        const mid = (lo + hi) / 2;
        const xMid = evaluateSpline({ "spline": foundCurve, "parameters": [mid] })[0][0][0];

        if (abs(xMid - xPosition) < tolerance)
            break;

        const xLo = evaluateSpline({ "spline": foundCurve, "parameters": [lo] })[0][0][0];

        if ((xLo <= xPosition && xMid >= xPosition) || (xLo >= xPosition && xMid <= xPosition))
            hi = mid;
        else
            lo = mid;
    }

    return evaluateSpline({ "spline": foundCurve, "parameters": [(lo + hi) / 2] })[0][0][2];
}

export function setControlPointX(curve is BSplineCurve, xPosition is ValueWithUnits) returns BSplineCurve
{
    // Find control point with X closest to xPosition
    var bestIdx = 0;
    var bestDist = abs(curve.controlPoints[0][0] - xPosition);

    for (var i = 1; i < size(curve.controlPoints); i += 1)
    {
        const dist = abs(curve.controlPoints[i][0] - xPosition);
        if (dist < bestDist)
        {
            bestDist = dist;
            bestIdx = i;
        }
    }

    // Replace that control point's X, leaving Y and Z unchanged
    var newControlPoints = curve.controlPoints;
    const oldPt = newControlPoints[bestIdx];
    newControlPoints[bestIdx] = vector(xPosition, oldPt[1], oldPt[2]);

    return bSplineCurve({
        "degree"        : curve.degree,
        "dimension"     : curve.dimension,
        "isRational"    : curve.isRational,
        "isPeriodic"    : curve.isPeriodic,
        "controlPoints" : newControlPoints,
        "knots"         : curve.knots
    });
}

export function sampleAndSortCurves(curves is array, nPoints is number) returns array
{
    var allPoints = [];

    for (var curveIdx = 0; curveIdx < size(curves); curveIdx += 1)
    {
        const curve = curves[curveIdx];

        const tStart = curve.knots[0];
        const tEnd   = curve.knots[size(curve.knots) - 1];

        const params = range(tStart, tEnd, nPoints);

        const evaluated = evaluateSpline({ "spline": curve, "parameters": params })[0];

        for (var i = 0; i < size(evaluated); i += 1)
            allPoints = append(allPoints, evaluated[i]);
    }
    
    allPoints = deduplicate(allPoints);
    allPoints = sort(allPoints, function(a, b) {return a[0] - b[0];});

    return allPoints;
}
