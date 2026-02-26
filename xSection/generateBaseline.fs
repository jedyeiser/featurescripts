FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// IMPORT: xSectReferencePoints.fs
import(path : "08fddb59786b6bfee020ee05", version : "ef4a95bef1c88e594c76dedb");
// IMPORT: xSectBeamAnalysis.fs
import(path : "ebac109589e3bf405d3f3ae7", version : "8984e1ab14c99a11a8b53f23");

/**
 * GENERATE BASELINE
 * =================
 *
 * Produces the baseline (camber/rocker) curve for a ski or snowboard.
 *
 * The user provides:
 *   - FCP, ACP, and mount (load) point geometry references
 *   - Camber height (MCH), forebody/aftbody rocker lengths, tip heights
 *   - Optional EI profile edge(s)
 *   - Spline approximation parameters
 *
 * Algorithm:
 *   1. Resolve xFCP, xACP, xMount
 *   2. Derive xFRCP = xFCP + FRCPL,  xARCP = xACP - ARCPL
 *   3. Inner solve (camber pocket + rocker sections) for inner target height H
 *   4. Outer bisection on H until actualCamber = MCH_target ± 0.01 mm
 *   5. Fit approximateSpline through assembled points, create curve body
 *
 * Coordinate convention:
 *   - All geometry in XZ plane (Y = 0)
 *   - X = along-ski axis;  xFCP < xACP
 *   - Z = vertical; positive = up = camber
 */


// =============================================================================
// BOUND CONSTANTS
// =============================================================================

export const ApproxToleranceBounds  = { (meter) : [1e-7, 1e-4, 1e-2] } as LengthBoundSpec;
export const MaxControlPointsBounds = { (unitless) : [4, 50, 500] }    as IntegerBoundSpec;
export const ApproxDegreeBounds     = { (unitless) : [1, 3, 9] }       as IntegerBoundSpec;


// =============================================================================
// EDITING LOGIC
// =============================================================================

export function generateBaselineEditLogic(context is Context, id is Id,
    oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map,
    clickedButton is string) returns map
{
    definition.showEIQuery = definition.hasEIProfile;
    return definition;
}


// =============================================================================
// INLINE EI HELPER  (copied from updateProfile.fs — not exported there)
// =============================================================================

/**
 * Sample 100 parametric points per EI edge, decode EI from world Z (1 mm = 1 N·m²),
 * sort by X, and linearly extrapolate to FCP/ACP boundaries if needed.
 */
function getEIFromEdges(context is Context, eiEdges is Query,
                         xFCP is ValueWithUnits, xACP is ValueWithUnits) returns array
{
    var edges = evaluateQuery(context, eiEdges);
    var points = [];
    var numSamples = 100;

    for (var edge in edges)
    {
        for (var i = 0; i < numSamples; i += 1)
        {
            var t = i / (numSamples - 1);
            try
            {
                var tangentLine = evEdgeTangentLine(context, { "edge" : edge, "parameter" : t });
                var pt = tangentLine.origin;
                var EI = (pt[2] / millimeter) * newton * meter * meter;
                points = append(points, { "x" : pt[0], "EI" : EI });
            }
            // skip failed evaluations
        }
    }

    if (size(points) < 2)
    {
        return points;
    }

    // Insertion sort by x
    for (var i = 1; i < size(points); i += 1)
    {
        var key = points[i];
        var j = i - 1;
        while (j >= 0 && points[j].x > key.x)
        {
            points[j + 1] = points[j];
            j -= 1;
        }
        points[j + 1] = key;
    }

    var n = size(points);

    // Linear extrapolation at front boundary
    if (points[0].x > xFCP && n >= 2)
    {
        var dx = points[1].x - points[0].x;
        if (abs(dx) > 1e-10 * meter)
        {
            var slope = (points[1].EI - points[0].EI) / dx;
            var extEI = points[0].EI + slope * (xFCP - points[0].x);
            if (extEI < 0 * newton * meter * meter)
            {
                extEI = 0 * newton * meter * meter;
            }
            points = concatenateArrays([[{ "x" : xFCP, "EI" : extEI }], points]);
            n = size(points);
        }
    }

    // Linear extrapolation at rear boundary
    if (points[n - 1].x < xACP && n >= 2)
    {
        var dx2 = points[n - 1].x - points[n - 2].x;
        if (abs(dx2) > 1e-10 * meter)
        {
            var slope2 = (points[n - 1].EI - points[n - 2].EI) / dx2;
            var extEI2 = points[n - 1].EI + slope2 * (xACP - points[n - 1].x);
            if (extEI2 < 0 * newton * meter * meter)
            {
                extEI2 = 0 * newton * meter * meter;
            }
            points = append(points, { "x" : xACP, "EI" : extEI2 });
        }
    }

    return points;
}


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
 */
function solveCamberBeam(eiData is array, xFRCP is ValueWithUnits,
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
 * Returns dense samples [{x, z}].
 */
function solveCamberCubic(xFRCP is ValueWithUnits, xARCP is ValueWithUnits,
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
 * Compute the Z of the tip point (at xTip) such that the perpendicular
 * (normal) distance from the tip to the camber tangent line at (xAnchor, zAnchor)
 * with slope equals tipHeight.  The tip lands tipHeight below the tangent.
 *
 * Derivation:
 *   Tangent line: z = zAnchor + slope*(x - xAnchor)
 *   Normal distance from (xTip, zTip) to this line = tipHeight
 *   → zTip = zAnchor + slope*(xTip - xAnchor) - tipHeight * sqrt(1 + slope²)
 */
function computeTipZ(xAnchor is ValueWithUnits, zAnchor is ValueWithUnits,
                     slope, xTip is ValueWithUnits,
                     tipHeight is ValueWithUnits) returns ValueWithUnits
{
    var normT = sqrt(1 + slope * slope);
    return zAnchor + (xTip - xAnchor) * slope + tipHeight * normT;
}


// =============================================================================
// ROCKER QUADRATIC SOLVER
// =============================================================================

/**
 * Fit quadratic f(x) = p·x² + q·x + r satisfying:
 *   f(xA) = zA,   f'(xA) = slopeA,   f(xB) = zB
 *
 * Works in shifted coordinates (u = x - xA) to improve numerics.
 * Returns nPoints samples [{x, z}] from xA to xB.
 */
function solveRockerQuadratic(xA is ValueWithUnits, zA is ValueWithUnits,
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
 */
function interpZ(pts is array, xTarget is ValueWithUnits) returns ValueWithUnits
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
 * Returns plain number (dimensionless: m/m).
 */
function slopeAt(pts is array, xTarget is ValueWithUnits)
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
 * Max Z in [xFRCP, xARCP] of a sorted [{x, z}] array.
 * Returns plain number (meters).
 */
function measureCamberHeight(pts is array, xFRCP is ValueWithUnits,
                              xARCP is ValueWithUnits)
{
    var maxZ = 0.0;
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
    return maxZ;
}


// =============================================================================
// ROTATE + TRANSLATE  (tips to Z = 0)
// =============================================================================

/**
 * Rotate the assembled point array so that the chord FCP→ACP is horizontal,
 * then translate so both tips land at Z = 0.
 *
 * Rotation pivot is the FCP point.  The minimum of the two tip Z values
 * is translated to 0 (handles non-perfectly-symmetric rocker).
 */
function rotateTranslate(pts is array, xFCP is ValueWithUnits,
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
 */
function innerSolve(context is Context,
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
 */
function solveBaseline(context is Context,
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

        println("bisect iter=" ~ iter ~
                " H=" ~ round(H_mid * 1e6) / 1e3 ~
                " cam=" ~ round(camMid * 1e6) / 1e3 ~
                " tgt=" ~ round(MCH_target * 1e6) / 1e3 ~ " mm");

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
// FEATURE DEFINITION
// =============================================================================

annotation {
    "Feature Type Name"        : "Generate baseline",
    "Feature Type Description" : "Generates a camber/rocker baseline curve for a ski or snowboard",
    "Editing Logic Function"   : "generateBaselineEditLogic"
}
export const generateBaseline = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Output curve name", "Default" : "Baseline" }
        definition.outputCurveName is string;

        annotation { "Name" : "FCP",
                     "Filter" : (EntityType.FACE && GeometryType.PLANE) || EntityType.VERTEX || BodyType.MATE_CONNECTOR,
                     "MaxNumberOfPicks" : 1 }
        definition.fcpQuery is Query;

        annotation { "Name" : "ACP",
                     "Filter" : (EntityType.FACE && GeometryType.PLANE) || EntityType.VERTEX || BodyType.MATE_CONNECTOR,
                     "MaxNumberOfPicks" : 1 }
        definition.acpQuery is Query;

        annotation { "Name" : "Mount / load point",
                     "Filter" : (EntityType.FACE && GeometryType.PLANE) || EntityType.VERTEX || BodyType.MATE_CONNECTOR,
                     "MaxNumberOfPicks" : 1 }
        definition.mountQuery is Query;

        annotation { "Group Name" : "Camber targets", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Camber height (MCH)",
                         "Description" : "Maximum camber height after rocker rotation. 0 = flat ski." }
            isLength(definition.camberHeight, LENGTH_BOUNDS);

            annotation { "Name" : "Forebody rocker length",
                         "Description" : "FCP → FRCP distance. 0 = no forebody rocker." }
            isLength(definition.frcpl, LENGTH_BOUNDS);

            annotation { "Name" : "Aftbody rocker length",
                         "Description" : "ARCP → ACP distance. 0 = no aftbody rocker." }
            isLength(definition.arcpl, LENGTH_BOUNDS);

            annotation { "Name" : "FCP tip height",
                         "Description" : "Normal-distance offset of FCP tip below camber tangent at FRCP." }
            isLength(definition.fcpHeight, LENGTH_BOUNDS);

            annotation { "Name" : "ACP tip height",
                         "Description" : "Normal-distance offset of ACP tip below camber tangent at ARCP." }
            isLength(definition.acpHeight, LENGTH_BOUNDS);
        }

        annotation { "Name" : "Use EI profile",
                     "Default" : false,
                     "Description" : "When enabled, solve camber pocket using beam bending with the provided EI profile." }
        definition.hasEIProfile is boolean;

        annotation { "Name" : "showEIQuery", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : false }
        definition.showEIQuery is boolean;

        if (definition.showEIQuery)
        {
            annotation { "Name" : "EI profile edges",
                         "Filter" : EntityType.EDGE,
                         "Description" : "World Z in mm = EI in N·m²." }
            definition.eiEdgesQuery is Query;
        }

        annotation { "Group Name" : "Spline output", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Approximation tolerance" }
            isLength(definition.approxTolerance, ApproxToleranceBounds);

            annotation { "Name" : "Maximum control points" }
            isInteger(definition.maxControlPoints, MaxControlPointsBounds);

            annotation { "Name" : "Curve degree" }
            isInteger(definition.curveDegree, ApproxDegreeBounds);
        }
    }
    {
        // ----------------------------------------------------------------
        // 1. Resolve reference X coordinates
        // ----------------------------------------------------------------
        var dummyEdge = qNothing();
        var xFCP   = resolveReferencePointX(context, definition.fcpQuery,   dummyEdge);
        var xACP   = resolveReferencePointX(context, definition.acpQuery,   dummyEdge);
        var xMount = resolveReferencePointX(context, definition.mountQuery,  dummyEdge);

        if (xFCP == undefined || xACP == undefined || xMount == undefined)
        {
            throw regenError("Could not resolve FCP, ACP, or mount point.");
        }
        if (xFCP >= xACP)
        {
            throw regenError("FCP must have a smaller X coordinate than ACP.");
        }
        if (xMount < xFCP || xMount > xACP)
        {
            throw regenError("Mount / load point must lie between FCP and ACP.");
        }

        // ----------------------------------------------------------------
        // 2. Derive FRCP and ARCP
        // ----------------------------------------------------------------
        var xFRCP = xFCP + definition.frcpl;
        var xARCP = xACP - definition.arcpl;

        if (xFRCP >= xARCP)
        {
            throw regenError("Rocker lengths are too large — FRCP must be less than ARCP.");
        }

        println("generateBaseline: xFCP="   ~ round(xFCP   / millimeter) ~
                "  xFRCP=" ~ round(xFRCP  / millimeter) ~
                "  xLoad=" ~ round(xMount / millimeter) ~
                "  xARCP=" ~ round(xARCP  / millimeter) ~
                "  xACP="  ~ round(xACP   / millimeter) ~ " mm");

        // ----------------------------------------------------------------
        // 3. Load EI data
        // ----------------------------------------------------------------
        var eiData = [];
        var hasEI  = false;

        if (definition.hasEIProfile && definition.showEIQuery)
        {
            var edgeCount = size(evaluateQuery(context, definition.eiEdgesQuery));
            if (edgeCount > 0)
            {
                eiData = getEIFromEdges(context, definition.eiEdgesQuery, xFCP, xACP);
                hasEI  = (size(eiData) >= 2);
                println("generateBaseline: EI data loaded, " ~ size(eiData) ~ " points");
            }
        }

        // ----------------------------------------------------------------
        // 4. Solve
        // ----------------------------------------------------------------
        var MCH_m = definition.camberHeight / meter;

        var finalPts = solveBaseline(context, eiData, hasEI,
                                      xFCP, xACP, xFRCP, xARCP, xMount,
                                      definition.fcpHeight, definition.acpHeight,
                                      definition.frcpl, definition.arcpl,
                                      MCH_m);

        println("generateBaseline: solved " ~ size(finalPts) ~ " points, " ~
                "camber=" ~ round(measureCamberHeight(finalPts, xFRCP, xARCP) * 1e6) / 1e3 ~ " mm");

        // ----------------------------------------------------------------
        // 5. Partition finalPts into three sections
        //    Boundary points (xFRCP, xARCP) are included in both adjacent
        //    sections to ensure G0 continuity where the curves meet.
        // ----------------------------------------------------------------
        var forePoints   = [];
        var camberPoints = [];
        var aftPoints    = [];

        for (var pt in finalPts)
        {
            if (pt.x <= xFRCP)
            {
                forePoints = append(forePoints, pt);
            }
            if (pt.x >= xFRCP && pt.x <= xARCP)
            {
                camberPoints = append(camberPoints, pt);
            }
            if (pt.x >= xARCP)
            {
                aftPoints = append(aftPoints, pt);
            }
        }

        // ----------------------------------------------------------------
        // 6. Helper to build a 3D point array from a 2D section array
        // ----------------------------------------------------------------
        var hasForeRocker = definition.frcpl > 0 * meter;
        var hasAftRocker  = definition.arcpl  > 0 * meter;

        // --- Camber pocket (always created) ---
        var camberPts3D = [];
        for (var pt in camberPoints)
        {
            camberPts3D = append(camberPts3D, vector(pt.x, 0 * meter, pt.z));
        }

        if (size(camberPts3D) < 2)
        {
            throw regenError("Baseline solver produced insufficient camber points.");
        }

        try
        {
            var approxCamber = approximateSpline(context, {
                "degree"           : definition.curveDegree,
                "tolerance"        : definition.approxTolerance,
                "isPeriodic"       : false,
                "targets"          : [{ "positions" : camberPts3D }],
                "maxControlPoints" : definition.maxControlPoints
            });

            opCreateBSplineCurve(context, id + "camber", {
                "bSplineCurve" : approxCamber[0]
            });

            var camberBodies = evaluateQuery(context, qCreatedBy(id + "camber", EntityType.BODY));
            if (size(camberBodies) > 0)
            {
                setProperty(context, {
                    "entities"     : camberBodies[0],
                    "propertyType" : PropertyType.NAME,
                    "value"        : definition.outputCurveName
                });
            }
        }
        catch (e)
        {
            println("ERROR generateBaseline: camber spline failed — " ~ e);
            println("  camber point count = " ~ size(camberPts3D));
            for (var i = 0; i < size(camberPts3D) - 1; i += 1)
            {
                addDebugLine(context, camberPts3D[i], camberPts3D[i + 1], DebugColor.RED);
            }
            throw regenError("Camber spline fitting failed — see console output.");
        }

        // --- Forebody rocker (only when frcpl > 0) ---
        if (hasForeRocker && size(forePoints) >= 2)
        {
            var forePts3D = [];
            for (var pt in forePoints)
            {
                forePts3D = append(forePts3D, vector(pt.x, 0 * meter, pt.z));
            }

            try
            {
                var approxFore = approximateSpline(context, {
                    "degree"           : definition.curveDegree,
                    "tolerance"        : definition.approxTolerance,
                    "isPeriodic"       : false,
                    "targets"          : [{ "positions" : forePts3D }],
                    "maxControlPoints" : definition.maxControlPoints
                });

                opCreateBSplineCurve(context, id + "forebody", {
                    "bSplineCurve" : approxFore[0]
                });

                var foreBodies = evaluateQuery(context, qCreatedBy(id + "forebody", EntityType.BODY));
                if (size(foreBodies) > 0)
                {
                    setProperty(context, {
                        "entities"     : foreBodies[0],
                        "propertyType" : PropertyType.NAME,
                        "value"        : definition.outputCurveName ~ " (forebody)"
                    });
                }
            }
            catch (e)
            {
                println("ERROR generateBaseline: forebody spline failed — " ~ e);
                println("  forebody point count = " ~ size(forePts3D));
                for (var i = 0; i < size(forePts3D) - 1; i += 1)
                {
                    addDebugLine(context, forePts3D[i], forePts3D[i + 1], DebugColor.CYAN);
                }
                throw regenError("Forebody rocker spline fitting failed — see console output.");
            }
        }

        // --- Aftbody rocker (only when arcpl > 0) ---
        if (hasAftRocker && size(aftPoints) >= 2)
        {
            var aftPts3D = [];
            for (var pt in aftPoints)
            {
                aftPts3D = append(aftPts3D, vector(pt.x, 0 * meter, pt.z));
            }

            try
            {
                var approxAft = approximateSpline(context, {
                    "degree"           : definition.curveDegree,
                    "tolerance"        : definition.approxTolerance,
                    "isPeriodic"       : false,
                    "targets"          : [{ "positions" : aftPts3D }],
                    "maxControlPoints" : definition.maxControlPoints
                });

                opCreateBSplineCurve(context, id + "aftbody", {
                    "bSplineCurve" : approxAft[0]
                });

                var aftBodies = evaluateQuery(context, qCreatedBy(id + "aftbody", EntityType.BODY));
                if (size(aftBodies) > 0)
                {
                    setProperty(context, {
                        "entities"     : aftBodies[0],
                        "propertyType" : PropertyType.NAME,
                        "value"        : definition.outputCurveName ~ " (aftbody)"
                    });
                }
            }
            catch (e)
            {
                println("ERROR generateBaseline: aftbody spline failed — " ~ e);
                println("  aftbody point count = " ~ size(aftPts3D));
                for (var i = 0; i < size(aftPts3D) - 1; i += 1)
                {
                    addDebugLine(context, aftPts3D[i], aftPts3D[i + 1], DebugColor.YELLOW);
                }
                throw regenError("Aftbody rocker spline fitting failed — see console output.");
            }
        }
    });
