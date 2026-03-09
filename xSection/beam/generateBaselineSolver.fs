FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// IMPORT: xSectBeamAnalysis.fs
import(path : "ebac109589e3bf405d3f3ae7", version : "7e4fdcd1cd16322867bb23fc");

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
export function solveCamberBeam(eiData is array, xFRCP is ValueWithUnits,
                          xARCP is ValueWithUnits, xLoad is ValueWithUnits,
                          H) returns array
{
    var N   = 100;
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


// =============================================================================
// ROCKER TIP Z CALCULATION
// =============================================================================

/**
 * Compute the pre-shift z of the rocker tip so that, after the global shift,
 * the tip lands at exactly targetSnowHeight above snow (z = 0).
 *
 * Given the rocker quadratic anchored at (xAnchor, zAnchor=0) with slope `slope`
 * at xAnchor and tip at xTip, the parabola has a vertex below z = 0.
 * The global shift lifts the profile by |vertex_z|, so the tip ends up at
 *   zTip_final = zTip_pre_shift - vertex_z  =  targetSnowHeight
 *
 * Closed-form solution (derived from the vertex condition):
 *   k = slope * uB            where uB = (xTip - xAnchor) / meter  (< 0 fore, > 0 aft)
 *   t = targetSnowHeight / meter
 *   zTip_pre_shift = zAnchor + [(k + t) + sqrt(t * (t - 2*k))] / 2 * meter
 *
 * Both camber solvers guarantee zAnchor = 0 at xFRCP / xARCP, so the formula
 * reduces to: T = [(k + t) + sqrt(t * (t - 2*k))] / 2 * meter.
 *
 * Special cases:
 *   t = 0  →  zTip = vertex_z  (tip on snow; shift = 0)
 *   uB = 0 →  degenerate; return zAnchor
 *
 * @param xAnchor {ValueWithUnits} : Junction point X (FRCP or ARCP)
 * @param zAnchor {ValueWithUnits} : Junction point Z (= 0 from camber solver)
 * @param slope {number} : dz/dx at xAnchor (dimensionless)
 * @param xTip {ValueWithUnits} : Tip point X (FCP or ACP)
 * @param targetSnowHeight {ValueWithUnits} : Desired tip height above snow
 * @returns {ValueWithUnits} : Pre-shift Z of the tip
 */
export function computeTipZ(xAnchor is ValueWithUnits, zAnchor is ValueWithUnits,
                     slope, xTip is ValueWithUnits,
                     targetSnowHeight is ValueWithUnits) returns ValueWithUnits
{
    var uB = (xTip - xAnchor) / meter;
    if (abs(uB) < 1e-12)
    {
        return zAnchor;
    }

    var k = slope * uB;                             // < 0 for both fore and aft
    var t = (targetSnowHeight - zAnchor) / meter;   // target height above anchor (plain m)

    if (t <= 0)
    {
        // Zero or negative target: place tip at zAnchor + t (no dip involved)
        return zAnchor + t * meter;
    }

    // T = [(k + t) + sqrt(t * (t - 2k))] / 2
    // Discriminant ≥ 0 when k ≤ 0; clamp to 0 for degenerate slopes (k > 0)
    var T = ((k + t) + sqrt(max(0, t * (t - 2 * k)))) / 2;
    return zAnchor + T * meter;
}


// =============================================================================
// ROCKER QUADRATIC SOLVER
// =============================================================================

/**
 * Fit quadratic f(x) = p·x² + q·x + r satisfying:
 *   f(xA) = zA,   f'(xA) = slopeA,   f(xB) = zB
 *
 * Works in shifted coordinates (u = x - xA) to improve numerics.
 *
 * @param xA {ValueWithUnits} : Start X (junction point)
 * @param zA {ValueWithUnits} : Start Z
 * @param slopeA {number} : Slope dz/dx at xA (dimensionless)
 * @param xB {ValueWithUnits} : End X (tip point)
 * @param zB {ValueWithUnits} : End Z
 * @param nPoints {number} : Number of output samples
 * @returns {array} : [{x : ValueWithUnits, z : ValueWithUnits}] from xA to xB
 */
export function solveRockerQuadratic(xA is ValueWithUnits, zA is ValueWithUnits,
                               slopeA, xB is ValueWithUnits, zB is ValueWithUnits,
                               nPoints) returns array
{
    var uB   = (xB - xA) / meter;   // plain (negative for forebody: xB < xA)
    var zA_m = zA / meter;
    var zB_m = zB / meter;

    // f(u) = p·u² + q·u + r
    // r = f(0) = zA_m
    // q = f'(0) = slopeA
    // p = (zB_m - q*uB - r) / uB²
    var r = zA_m;
    var q = slopeA;
    var p = 0.0;
    if (abs(uB) >= 1e-12)
    {
        p = (zB_m - q * uB - r) / (uB * uB);
    }

    var result = [];
    for (var i = 0; i < nPoints; i += 1)
    {
        var u = uB * i / (nPoints - 1);
        var z = p * u * u + q * u + r;
        result = append(result, {
            "x" : xA + u * meter,
            "z" : z * meter
        });
    }

    return result;
}


// =============================================================================
// POINT-ARRAY UTILITIES
// =============================================================================

/**
 * Linearly interpolate Z at xTarget in a sorted [{x, z}] array.
 * Flat-extrapolates outside range.
 *
 * @param pts {array} : [{x : ValueWithUnits, z : ValueWithUnits}] sorted by x
 * @param xTarget {ValueWithUnits}
 * @returns {ValueWithUnits} : Interpolated Z
 */
export function interpZ(pts is array, xTarget is ValueWithUnits) returns ValueWithUnits
{
    var n = size(pts);
    if (n == 0)
    {
        return 0 * meter;
    }
    if (xTarget <= pts[0].x)
    {
        return pts[0].z;
    }
    if (xTarget >= pts[n - 1].x)
    {
        return pts[n - 1].z;
    }
    for (var i = 0; i < n - 1; i += 1)
    {
        if (xTarget >= pts[i].x && xTarget <= pts[i + 1].x)
        {
            var dx = (pts[i + 1].x - pts[i].x) / meter;
            if (abs(dx) < 1e-15)
            {
                return pts[i].z;
            }
            var t = (xTarget - pts[i].x) / meter / dx;
            return pts[i].z + t * (pts[i + 1].z - pts[i].z);
        }
    }
    return pts[n - 1].z;
}

/**
 * Compute dz/dx at xTarget in a sorted [{x, z}] array.
 * Uses central differences when available; one-sided at boundaries.
 *
 * @param pts {array} : [{x : ValueWithUnits, z : ValueWithUnits}] sorted by x
 * @param xTarget {ValueWithUnits}
 * @returns {number} : Slope dz/dx (dimensionless: m/m)
 */
export function slopeAt(pts is array, xTarget is ValueWithUnits)
{
    var n = size(pts);
    if (n < 2)
    {
        return 0.0;
    }

    // Find bracketing interval
    var idx = 0;
    for (var i = 0; i < n - 1; i += 1)
    {
        if (xTarget >= pts[i].x && xTarget <= pts[i + 1].x)
        {
            idx = i;
            break;
        }
    }

    // Central differences if interior
    if (idx > 0 && idx < n - 2)
    {
        var dx = (pts[idx + 1].x - pts[idx - 1].x) / meter;
        var dz = (pts[idx + 1].z - pts[idx - 1].z) / meter;
        if (abs(dx) < 1e-15)
        {
            return 0.0;
        }
        return dz / dx;
    }
    else
    {
        var dx2 = (pts[idx + 1].x - pts[idx].x) / meter;
        var dz2 = (pts[idx + 1].z - pts[idx].z) / meter;
        if (abs(dx2) < 1e-15)
        {
            return 0.0;
        }
        return dz2 / dx2;
    }
}

/**
 * Camber height above the contact-point elevation in [xFRCP, xARCP].
 * Measures relative to z(xFRCP), so the result is shift-invariant.
 *
 * @param pts {array} : [{x, z}] sorted by x
 * @param xFRCP {ValueWithUnits}
 * @param xARCP {ValueWithUnits}
 * @returns {number} : Camber height in plain meters
 */
export function measureCamberHeight(pts is array, xFRCP is ValueWithUnits,
                              xARCP is ValueWithUnits)
{
    var contactZ = interpZ(pts, xFRCP) / meter;
    var maxZ = contactZ;
    for (var pt in pts)
    {
        if (pt.x >= xFRCP && pt.x <= xARCP)
        {
            var z = pt.z / meter;
            if (z > maxZ)
            {
                maxZ = z;
            }
        }
    }
    return maxZ - contactZ;
}


// =============================================================================
// ROTATE + TRANSLATE  (tips to Z = 0)
// =============================================================================

/**
 * Rotate the assembled point array so that the chord FCP→ACP is horizontal,
 * then translate so both tips land at Z = 0.
 *
 * Rotation pivot is the FCP point. The minimum of the two tip Z values
 * is translated to 0 (handles non-perfectly-symmetric rocker).
 *
 * @param pts {array} : [{x, z}] sorted by x
 * @param xFCP {ValueWithUnits}
 * @param xACP {ValueWithUnits}
 * @returns {array} : Rotated and translated [{x, z}]
 */
export function rotateTranslate(pts is array, xFCP is ValueWithUnits,
                          xACP is ValueWithUnits) returns array
{
    var zFCP_m = interpZ(pts, xFCP) / meter;
    var zACP_m = interpZ(pts, xACP) / meter;

    // Chord angle
    var chordDz = zACP_m - zFCP_m;
    var chordDx = (xACP - xFCP) / meter;
    var theta   = atan2(chordDz, chordDx);
    var cosT    = cos(-theta);
    var sinT    = sin(-theta);

    // Pivot at FCP
    var xPiv = xFCP / meter;
    var zPiv = zFCP_m;

    // Rotate all points
    var rotated = [];
    for (var pt in pts)
    {
        var rx = pt.x / meter - xPiv;
        var rz = pt.z / meter - zPiv;
        var nx = cosT * rx - sinT * rz;
        var nz = sinT * rx + cosT * rz;
        rotated = append(rotated, {
            "x" : (nx + xPiv) * meter,
            "z" : nz * meter
        });
    }

    // Find minimum tip Z after rotation, translate so it = 0
    var zFCProt = interpZ(rotated, xFCP) / meter;
    var zACProt = interpZ(rotated, xACP) / meter;
    var tipZ    = zFCProt;
    if (zACProt < tipZ)
    {
        tipZ = zACProt;
    }

    var translated = [];
    for (var pt in rotated)
    {
        translated = append(translated, {
            "x" : pt.x,
            "z" : pt.z - tipZ * meter
        });
    }

    return translated;
}


// =============================================================================
// MAIN ENTRY POINT
// =============================================================================

/**
 * Solve the full baseline geometry:
 *   1. Solve camber pocket (beam or cubic) with initial H = MCH_m.
 *   2. Attach rocker quadratics at each end.
 *   3. Rotate/translate so tip minima land at z = 0.
 *   4. Measure actual camber; scale H proportionally and repeat (≤ 10 iters).
 *
 * The beam/cubic relationship is linear in H, so this converges in 1-2 iterations.
 * 10 iterations is a generous upper bound.
 *
 * @param context {Context}
 * @param eiData {array} : Sorted [{ "x", "EI" }]; empty if hasEI is false
 * @param hasEI {boolean}
 * @param xFCP {ValueWithUnits}
 * @param xACP {ValueWithUnits}
 * @param xFRCP {ValueWithUnits}
 * @param xARCP {ValueWithUnits}
 * @param xLoad {ValueWithUnits} : Load application point (mount)
 * @param fcpHeight {ValueWithUnits} : Target FCP tip snow height
 * @param acpHeight {ValueWithUnits} : Target ACP tip snow height
 * @param frcpl {ValueWithUnits} : Forebody rocker length
 * @param arcpl {ValueWithUnits} : Aftbody rocker length
 * @param MCH_m {number} : Target camber height in plain meters
 * @returns {array} : [{x : ValueWithUnits, z : ValueWithUnits}]
 */
export function buildBaseline(context is Context,
                        eiData is array, hasEI is boolean,
                        xFCP is ValueWithUnits, xACP is ValueWithUnits,
                        xFRCP is ValueWithUnits, xARCP is ValueWithUnits,
                        xLoad is ValueWithUnits,
                        fcpHeight is ValueWithUnits, acpHeight is ValueWithUnits,
                        frcpl is ValueWithUnits, arcpl is ValueWithUnits,
                        MCH_m) returns array
{
    var TOL           = 1e-5;   // 0.01 mm
    var hasForeRocker = frcpl > 0 * meter;
    var hasAftRocker  = arcpl > 0 * meter;
    var H_guess       = MCH_m;
    var finalPts      = [];

    for (var iter = 0; iter < 10; iter += 1)
    {
        // --- Step 1: Solve camber pocket ---
        var camberPts = [];
        if (hasEI && size(eiData) >= 2)
        {
            camberPts = solveCamberBeam(eiData, xFRCP, xARCP, xLoad, H_guess);
        }
        else
        {
            camberPts = solveCamberCubic(xFRCP, xARCP, xLoad, H_guess);
        }

        // --- Step 2: Attach rockers ---
        var zFRCP_v   = interpZ(camberPts, xFRCP);
        var zARCP_v   = interpZ(camberPts, xARCP);
        var slopeFore = slopeAt(camberPts, xFRCP);
        var slopeAft  = slopeAt(camberPts, xARCP);

        var allPts = camberPts;

        if (hasForeRocker)
        {
            var zFCPtip    = computeTipZ(xFRCP, zFRCP_v, slopeFore, xFCP, fcpHeight);
            var foreRocker = solveRockerQuadratic(xFRCP, zFRCP_v, slopeFore, xFCP, zFCPtip, 51);
            var foreOnly   = [];
            for (var i = 1; i < size(foreRocker); i += 1)
            {
                foreOnly = append(foreOnly, foreRocker[i]);
            }
            allPts = concatenateArrays([foreOnly, allPts]);
        }

        if (hasAftRocker)
        {
            var zACPtip   = computeTipZ(xARCP, zARCP_v, slopeAft, xACP, acpHeight);
            var aftRocker = solveRockerQuadratic(xARCP, zARCP_v, slopeAft, xACP, zACPtip, 51);
            var aftOnly   = [];
            for (var i = 1; i < size(aftRocker); i += 1)
            {
                aftOnly = append(aftOnly, aftRocker[i]);
            }
            allPts = concatenateArrays([allPts, aftOnly]);
        }

        // --- Step 3: Rotate/translate so tip minima are at z = 0 ---
        allPts   = rotateTranslate(allPts, xFCP, xACP);
        finalPts = allPts;

        // --- Step 4: Measure and scale H for next iteration ---
        if (MCH_m < TOL)
        {
            break;
        }
        var measured = measureCamberHeight(allPts, xFRCP, xARCP);
        if (measured < 1e-9 || abs(measured - MCH_m) < TOL)
        {
            break;
        }
        H_guess = H_guess * (MCH_m / measured);
    }

    return finalPts;
}
