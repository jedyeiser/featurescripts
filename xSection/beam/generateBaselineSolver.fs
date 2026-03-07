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
 *   runMCHBisection
 *     └─ solveBaseline
 *          └─ innerSolve
 *               ├─ solveCamberBeam   (uses interpolateEI from xSectBeamAnalysis)
 *               ├─ solveCamberCubic
 *               ├─ computeTipZ
 *               ├─ solveRockerQuadratic
 *               ├─ interpZ
 *               ├─ slopeAt
 *               ├─ measureCamberHeight
 *               └─ rotateTranslate
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
    var N   = 200;
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
    var N = 200;

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
    // Discriminant = t*(t-2k) ≥ t² > 0 since k ≤ 0
    var T = ((k + t) + sqrt(t * (t - 2 * k))) / 2;
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
// INNER SOLVE  (one iteration for a given H)
// =============================================================================

/**
 * Assemble camber pocket + rocker sections for a given inner height H (plain, meters).
 * Returns the assembled, rotated, and translated [{x,z}] point array.
 *
 * Assembly order (by increasing x):  xFCP … xFRCP … xARCP … xACP
 *
 * @param context {Context}
 * @param eiData {array} : Sorted [{ "x", "EI" }]; may be empty if hasEI is false
 * @param hasEI {boolean} : Whether to use beam-bending solver (true) or cubic (false)
 * @param xFCP {ValueWithUnits}
 * @param xACP {ValueWithUnits}
 * @param xFRCP {ValueWithUnits}
 * @param xARCP {ValueWithUnits}
 * @param xLoad {ValueWithUnits} : Load application point (mount)
 * @param fcpHeight {ValueWithUnits} : Target FCP tip snow height
 * @param acpHeight {ValueWithUnits} : Target ACP tip snow height
 * @param frcpl {ValueWithUnits} : Forebody rocker length
 * @param arcpl {ValueWithUnits} : Aftbody rocker length
 * @param H {number} : Target camber height (plain meters)
 * @returns {array} : [{x : ValueWithUnits, z : ValueWithUnits}] sorted by x
 */
export function innerSolve(context is Context,
                    eiData is array, hasEI is boolean,
                    xFCP is ValueWithUnits, xACP is ValueWithUnits,
                    xFRCP is ValueWithUnits, xARCP is ValueWithUnits,
                    xLoad is ValueWithUnits,
                    fcpHeight is ValueWithUnits, acpHeight is ValueWithUnits,
                    frcpl is ValueWithUnits, arcpl is ValueWithUnits,
                    H) returns array
{
    var hasForeRocker = frcpl > 0 * meter;
    var hasAftRocker  = arcpl > 0 * meter;

    // --- Camber pocket (xFRCP → xARCP) ---
    var camberPts = [];
    if (hasEI && size(eiData) >= 2)
    {
        var eiMin = eiData[0].EI / (newton * meter * meter);
        var eiMax = eiMin;
        for (var eid in eiData)
        {
            var v = eid.EI / (newton * meter * meter);
            if (v < eiMin) { eiMin = v; }
            if (v > eiMax) { eiMax = v; }
        }
        camberPts = solveCamberBeam(eiData, xFRCP, xARCP, xLoad, H);
    }
    else
    {
        camberPts = solveCamberCubic(xFRCP, xARCP, xLoad, H);
    }

    var allPts = camberPts;

    // --- Forebody rocker (xFCP → xFRCP) ---
    if (hasForeRocker)
    {
        var zFRCP     = interpZ(camberPts, xFRCP);
        var slopeFore = slopeAt(camberPts, xFRCP);
        var zFCPtip   = computeTipZ(xFRCP, zFRCP, slopeFore, xFCP, fcpHeight);
        var foreRocker = solveRockerQuadratic(xFRCP, zFRCP, slopeFore, xFCP, zFCPtip, 51);

        // Exclude the xFRCP point (index 0) to avoid duplicate with camberPts[0];
        // include the xFCP tip point (index 50).
        var foreOnly = [];
        for (var i = 1; i < size(foreRocker); i += 1)
        {
            foreOnly = append(foreOnly, foreRocker[i]);
        }
        allPts = concatenateArrays([foreOnly, allPts]);
    }

    // --- Aftbody rocker (xARCP → xACP) ---
    if (hasAftRocker)
    {
        var zARCP    = interpZ(camberPts, xARCP);
        var slopeAft = slopeAt(camberPts, xARCP);
        var zACPtip  = computeTipZ(xARCP, zARCP, slopeAft, xACP, acpHeight);
        var aftRocker = solveRockerQuadratic(xARCP, zARCP, slopeAft, xACP, zACPtip, 51);

        // Exclude xARCP (index 0); include xACP (index 50).
        var aftOnly = [];
        for (var i = 1; i < size(aftRocker); i += 1)
        {
            aftOnly = append(aftOnly, aftRocker[i]);
        }
        allPts = concatenateArrays([allPts, aftOnly]);
    }

    // Sort by X (defensive; arrays should already be in order)
    for (var i = 1; i < size(allPts); i += 1)
    {
        var key = allPts[i];
        var j = i - 1;
        while (j >= 0 && allPts[j].x > key.x)
        {
            allPts[j + 1] = allPts[j];
            j -= 1;
        }
        allPts[j + 1] = key;
    }

    // Rotate + translate so contact points are at Z = 0
    allPts = rotateTranslate(allPts, xFRCP, xARCP);

    return allPts;
}


// =============================================================================
// OUTER BISECTION  (converge actualCamber → MCH_target)
// =============================================================================

/**
 * Bisect on inner height H until the measured post-rotation camber height
 * matches MCH_target (plain meters) within BISECT_TOL.
 *
 * @param context {Context}
 * @param eiData {array}
 * @param hasEI {boolean}
 * @param xFCP {ValueWithUnits}
 * @param xACP {ValueWithUnits}
 * @param xFRCP {ValueWithUnits}
 * @param xARCP {ValueWithUnits}
 * @param xLoad {ValueWithUnits}
 * @param fcpHeight {ValueWithUnits}
 * @param acpHeight {ValueWithUnits}
 * @param frcpl {ValueWithUnits}
 * @param arcpl {ValueWithUnits}
 * @param MCH_target {number} : Target camber in plain meters
 * @returns {array} : [{x, z}] converged profile
 */
export function solveBaseline(context is Context,
                        eiData is array, hasEI is boolean,
                        xFCP is ValueWithUnits, xACP is ValueWithUnits,
                        xFRCP is ValueWithUnits, xARCP is ValueWithUnits,
                        xLoad is ValueWithUnits,
                        fcpHeight is ValueWithUnits, acpHeight is ValueWithUnits,
                        frcpl is ValueWithUnits, arcpl is ValueWithUnits,
                        MCH_target) returns array
{
    var BISECT_TOL = 0.00001;  // 0.01 mm in meters
    var MAX_ITER   = 20;

    // Zero-camber special case: no iteration needed
    if (abs(MCH_target) < BISECT_TOL)
    {
        return innerSolve(context, eiData, hasEI, xFCP, xACP,
                          xFRCP, xARCP, xLoad,
                          fcpHeight, acpHeight, frcpl, arcpl, 0.0);
    }

    // Bracket: start with [0.5*target, 3*target]
    var H_lo = 0.5 * MCH_target;
    var H_hi = 3.0 * MCH_target;

    // Evaluate bracket bounds
    var ptsLo  = innerSolve(context, eiData, hasEI, xFCP, xACP,
                             xFRCP, xARCP, xLoad,
                             fcpHeight, acpHeight, frcpl, arcpl, H_lo);
    var camLo  = measureCamberHeight(ptsLo, xFRCP, xARCP);

    var ptsHi  = innerSolve(context, eiData, hasEI, xFCP, xACP,
                             xFRCP, xARCP, xLoad,
                             fcpHeight, acpHeight, frcpl, arcpl, H_hi);
    var camHi  = measureCamberHeight(ptsHi, xFRCP, xARCP);

    // Expand upper bound if camHi is still below target
    var expandIter = 0;
    while (camHi < MCH_target && expandIter < 10)
    {
        H_hi  = H_hi * 2;
        ptsHi = innerSolve(context, eiData, hasEI, xFCP, xACP,
                            xFRCP, xARCP, xLoad,
                            fcpHeight, acpHeight, frcpl, arcpl, H_hi);
        camHi = measureCamberHeight(ptsHi, xFRCP, xARCP);
        expandIter += 1;
    }

    // Bisect
    var H_mid    = (H_lo + H_hi) / 2;
    var finalPts = innerSolve(context, eiData, hasEI, xFCP, xACP,
                               xFRCP, xARCP, xLoad,
                               fcpHeight, acpHeight, frcpl, arcpl, H_mid);

    for (var iter = 0; iter < MAX_ITER; iter += 1)
    {
        H_mid    = (H_lo + H_hi) / 2;
        finalPts = innerSolve(context, eiData, hasEI, xFCP, xACP,
                               xFRCP, xARCP, xLoad,
                               fcpHeight, acpHeight, frcpl, arcpl, H_mid);
        var camMid = measureCamberHeight(finalPts, xFRCP, xARCP);

        if (abs(camMid - MCH_target) < BISECT_TOL)
        {
            break;
        }
        if (camMid < MCH_target)
        {
            H_lo = H_mid;
        }
        else
        {
            H_hi = H_mid;
        }
    }

    return finalPts;
}


// =============================================================================
// MCH BISECTION WRAPPER  (outer camber-height bisection + global shift)
// =============================================================================

/**
 * Converges the outer MCH bisection so that the post-shift camber height
 * (max Z in [xFRCP, xARCP] above Z(xFRCP), after the global Z-shift) matches
 * MCH_m (plain meters) within 0.01 mm.
 *
 * fcpH / acpH are the VERTICAL tip-height targets (ValueWithUnits).
 * The global shift is applied inside this function.
 *
 * @param context {Context}
 * @param eiData {array}
 * @param hasEI {boolean}
 * @param xFCP {ValueWithUnits}
 * @param xACP {ValueWithUnits}
 * @param xFRCP {ValueWithUnits}
 * @param xARCP {ValueWithUnits}
 * @param xLoad {ValueWithUnits}
 * @param fcpH {ValueWithUnits} : Vertical FCP tip-height target
 * @param acpH {ValueWithUnits} : Vertical ACP tip-height target
 * @param frcpl {ValueWithUnits}
 * @param arcpl {ValueWithUnits}
 * @param hasForeRocker {boolean}
 * @param hasAftRocker {boolean}
 * @param MCH_m {number} : Target camber height in plain meters
 * @returns {map} : { "finalPts" : array of {x,z}, "zMinShift" : number (plain meters, ≤ 0) }
 */
export function runMCHBisection(context is Context,
                          eiData is array, hasEI is boolean,
                          xFCP is ValueWithUnits, xACP is ValueWithUnits,
                          xFRCP is ValueWithUnits, xARCP is ValueWithUnits,
                          xLoad is ValueWithUnits,
                          fcpH is ValueWithUnits, acpH is ValueWithUnits,
                          frcpl is ValueWithUnits, arcpl is ValueWithUnits,
                          hasForeRocker is boolean, hasAftRocker is boolean,
                          MCH_m) returns map
{
    var OUTER_TOL = 0.00001;   // 0.01 mm in plain meters
    var finalPts  = [];
    var zMinShift = 0.0;

    if (MCH_m < 1e-9)
    {
        finalPts  = solveBaseline(context, eiData, hasEI,
                                  xFCP, xACP, xFRCP, xARCP, xLoad,
                                  fcpH, acpH, frcpl, arcpl, 0.0);
        zMinShift = 0.0;
    }
    else
    {
        // ---- Hi bracket: MCH_inner = MCH_m ----
        var MCH_hi = MCH_m;
        finalPts = solveBaseline(context, eiData, hasEI,
                                 xFCP, xACP, xFRCP, xARCP, xLoad,
                                 fcpH, acpH, frcpl, arcpl, MCH_hi);
        zMinShift = 0.0;
        if (hasForeRocker)
        {
            var vFH = interpZ(finalPts, xFCP) / meter - fcpH / meter;
            if (vFH < zMinShift) { zMinShift = vFH; }
        }
        if (hasAftRocker)
        {
            var vAH = interpZ(finalPts, xACP) / meter - acpH / meter;
            if (vAH < zMinShift) { zMinShift = vAH; }
        }
        var ac_hi = MCH_hi - zMinShift;

        if (abs(ac_hi - MCH_m) >= OUTER_TOL)
        {
            // ---- Lo bracket: MCH_inner = 0 ----
            var MCH_lo = 0.0;
            finalPts = solveBaseline(context, eiData, hasEI,
                                     xFCP, xACP, xFRCP, xARCP, xLoad,
                                     fcpH, acpH, frcpl, arcpl, MCH_lo);
            zMinShift = 0.0;
            if (hasForeRocker)
            {
                var vFL = interpZ(finalPts, xFCP) / meter - fcpH / meter;
                if (vFL < zMinShift) { zMinShift = vFL; }
            }
            if (hasAftRocker)
            {
                var vAL = interpZ(finalPts, xACP) / meter - acpH / meter;
                if (vAL < zMinShift) { zMinShift = vAL; }
            }
            var ac_lo = MCH_lo - zMinShift;

            if (ac_lo < MCH_m)
            {
                // Bisect [MCH_lo, MCH_hi]
                for (var outerIter = 0; outerIter < 15; outerIter += 1)
                {
                    var MCH_mid = (MCH_lo + MCH_hi) * 0.5;
                    finalPts = solveBaseline(context, eiData, hasEI,
                                             xFCP, xACP, xFRCP, xARCP, xLoad,
                                             fcpH, acpH, frcpl, arcpl, MCH_mid);
                    zMinShift = 0.0;
                    if (hasForeRocker)
                    {
                        var vFM = interpZ(finalPts, xFCP) / meter - fcpH / meter;
                        if (vFM < zMinShift) { zMinShift = vFM; }
                    }
                    if (hasAftRocker)
                    {
                        var vAM = interpZ(finalPts, xACP) / meter - acpH / meter;
                        if (vAM < zMinShift) { zMinShift = vAM; }
                    }
                    var ac_mid = MCH_mid - zMinShift;

                    if (abs(ac_mid - MCH_m) < OUTER_TOL) { break; }
                    if (ac_mid < MCH_m) { MCH_lo = MCH_mid; }
                    else               { MCH_hi = MCH_mid; }
                }
            }
            // else: tip height exceeds target; finalPts/zMinShift from lo solve
        }
        // else: hi bracket already within tolerance
    }

    // Apply global shift so no tip falls below its target snow height
    if (zMinShift < -1e-10)
    {
        var shiftedPts = [];
        for (var pt in finalPts)
        {
            shiftedPts = append(shiftedPts, {
                "x" : pt.x,
                "z" : pt.z - zMinShift * meter
            });
        }
        finalPts = shiftedPts;
    }

    return { "finalPts" : finalPts, "zMinShift" : zMinShift };
}
