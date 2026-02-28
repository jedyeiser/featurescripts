FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// IMPORT: xSectReferencePoints.fs
import(path : "08fddb59786b6bfee020ee05", version : "6505321b6c1234e60cb98341");
// IMPORT: xSectBeamAnalysis.fs
import(path : "ebac109589e3bf405d3f3ae7", version : "2101c3bf09f2c4da2845fca4");

// IMPORT: tools/curve_operations.fs
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/a7403d5f7f5a4fef8225b768", version : "e5b9e00c5a237415c89a66b7");

// IMPORT: tools/point_projection.fs
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/eb46317a27a44e391e11dfe6", version : "0cea3c8d27e4f7fd660aa69f");


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
 */
function computeTipZ(xAnchor is ValueWithUnits, zAnchor is ValueWithUnits,
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
 * Camber height above the contact-point elevation in [xFRCP, xARCP].
 * Measures relative to z(xFRCP), so the result is shift-invariant.
 * Returns plain number (meters).
 */
function measureCamberHeight(pts is array, xFRCP is ValueWithUnits,
                              xARCP is ValueWithUnits)
{
    // Measure height above the contact-point elevation (shift-invariant)
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
                         "Description" : "FCP -> FRCP distance. 0 = no forebody rocker." }
            isLength(definition.frcpl, LENGTH_BOUNDS);

            annotation { "Name" : "Aftbody rocker length",
                         "Description" : "ARCP -> ACP distance. 0 = no aftbody rocker." }
            isLength(definition.arcpl, LENGTH_BOUNDS);

            annotation { "Name" : "FCP height",
                         "Description" : "Normal-distance offset of FCP tip below camber tangent at FRCP." }
            isLength(definition.fcpHeight, LENGTH_BOUNDS);

            annotation { "Name" : "ACP height",
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
                         "Description" : "World Z in mm = EI in Nm^2." }
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

        // Needed by steps 4, 5, and 6
        var hasForeRocker = definition.frcpl > 0 * meter;
        var hasAftRocker  = definition.arcpl  > 0 * meter;

        // ----------------------------------------------------------------
        // 4. Solve — outer bisection on MCH_inner
        //
        // The inner bisection converges peak_z − z(xFRCP) = MCH_inner.
        // After rotateTranslate, z(xFRCP) = 0, so peak_z = MCH_inner.
        // The global shift then lifts everything by |zMinShift(MCH_inner)|,
        // making the final snow-relative camber:
        //
        //   actualCamber(MCH_inner) = MCH_inner + |zMinShift(MCH_inner)|
        //
        // This is monotone increasing in MCH_inner.  We bisect on
        // [MCH_lo, MCH_hi] ⊆ [0, MCH_m] to find the MCH_inner that
        // produces actualCamber = MCH_target (± 0.01 mm).
        // ----------------------------------------------------------------
        var MCH_m    = definition.camberHeight / meter;   // user target (plain m)
        var OUTER_TOL = 0.00001;                          // 0.01 mm in meters

        var finalPts  = [];
        var zMinShift = 0.0;   // plain meters (≤ 0)

        // Inline macro: solve for mchi and compute actualCamber.
        // Because FeatureScript does not support nested functions, this
        // block is repeated via a local variable pattern each time we need
        // to evaluate a candidate MCH_inner.
        //
        //   INPUT:  MCH_inner (number, plain meters)
        //   OUTPUT: finalPts, zMinShift updated; ac_cur = actualCamber

        if (MCH_m < 1e-9)
        {
            // Flat camber — skip bisection entirely
            finalPts = solveBaseline(context, eiData, hasEI,
                                     xFCP, xACP, xFRCP, xARCP, xMount,
                                     definition.fcpHeight, definition.acpHeight,
                                     definition.frcpl, definition.arcpl,
                                     0.0);
            zMinShift = 0.0;
        }
        else
        {
            // ---- Evaluate hi bracket: MCH_inner = MCH_m ----
            var MCH_hi = MCH_m;
            finalPts = solveBaseline(context, eiData, hasEI,
                                     xFCP, xACP, xFRCP, xARCP, xMount,
                                     definition.fcpHeight, definition.acpHeight,
                                     definition.frcpl, definition.arcpl,
                                     MCH_hi);
            zMinShift = 0.0;
            if (hasForeRocker)
            {
                var vertForeHi = interpZ(finalPts, xFCP) / meter - definition.fcpHeight / meter;
                if (vertForeHi < zMinShift) { zMinShift = vertForeHi; }
            }
            if (hasAftRocker)
            {
                var vertAftHi = interpZ(finalPts, xACP) / meter - definition.acpHeight / meter;
                if (vertAftHi < zMinShift) { zMinShift = vertAftHi; }
            }
            var ac_hi = MCH_hi - zMinShift;
            println("generateBaseline bisect hi: MCH_inner=" ~ round(MCH_hi * 1e6) / 1e3 ~
                    "  actual=" ~ round(ac_hi * 1e6) / 1e3 ~
                    "  target=" ~ round(MCH_m  * 1e6) / 1e3 ~ " mm");

            if (abs(ac_hi - MCH_m) >= OUTER_TOL)
            {
                // ---- Evaluate lo bracket: MCH_inner = 0 ----
                var MCH_lo = 0.0;
                finalPts = solveBaseline(context, eiData, hasEI,
                                         xFCP, xACP, xFRCP, xARCP, xMount,
                                         definition.fcpHeight, definition.acpHeight,
                                         definition.frcpl, definition.arcpl,
                                         MCH_lo);
                zMinShift = 0.0;
                if (hasForeRocker)
                {
                    var vertForeLo = interpZ(finalPts, xFCP) / meter - definition.fcpHeight / meter;
                    if (vertForeLo < zMinShift) { zMinShift = vertForeLo; }
                }
                if (hasAftRocker)
                {
                    var vertAftLo = interpZ(finalPts, xACP) / meter - definition.acpHeight / meter;
                    if (vertAftLo < zMinShift) { zMinShift = vertAftLo; }
                }
                var ac_lo = MCH_lo - zMinShift;
                println("generateBaseline bisect lo: MCH_inner=" ~ round(MCH_lo * 1e6) / 1e3 ~
                        "  actual=" ~ round(ac_lo * 1e6) / 1e3 ~
                        "  target=" ~ round(MCH_m  * 1e6) / 1e3 ~ " mm");

                if (ac_lo >= MCH_m)
                {
                    // Tip height alone exceeds target — use MCH_inner = 0
                    // (finalPts / zMinShift already set by lo solve above)
                    println("generateBaseline: tip height exceeds target; using MCH_inner=0");
                }
                else
                {
                    // ---- Bisect [MCH_lo, MCH_hi] ----
                    // Invariant: ac_lo < MCH_m ≤ ac_hi
                    for (var outerIter = 0; outerIter < 15; outerIter += 1)
                    {
                        var MCH_mid = (MCH_lo + MCH_hi) * 0.5;
                        finalPts = solveBaseline(context, eiData, hasEI,
                                                 xFCP, xACP, xFRCP, xARCP, xMount,
                                                 definition.fcpHeight, definition.acpHeight,
                                                 definition.frcpl, definition.arcpl,
                                                 MCH_mid);
                        zMinShift = 0.0;
                        if (hasForeRocker)
                        {
                            var vertForeMid = interpZ(finalPts, xFCP) / meter - definition.fcpHeight / meter;
                            if (vertForeMid < zMinShift) { zMinShift = vertForeMid; }
                        }
                        if (hasAftRocker)
                        {
                            var vertAftMid = interpZ(finalPts, xACP) / meter - definition.acpHeight / meter;
                            if (vertAftMid < zMinShift) { zMinShift = vertAftMid; }
                        }
                        var ac_mid = MCH_mid - zMinShift;

                        println("generateBaseline bisect " ~ outerIter ~
                                ": MCH_inner=" ~ round(MCH_mid * 1e6) / 1e3 ~
                                "  actual="    ~ round(ac_mid  * 1e6) / 1e3 ~
                                "  target="    ~ round(MCH_m   * 1e6) / 1e3 ~ " mm");

                        if (abs(ac_mid - MCH_m) < OUTER_TOL) { break; }

                        if (ac_mid < MCH_m)
                        {
                            MCH_lo = MCH_mid;
                        }
                        else
                        {
                            MCH_hi = MCH_mid;
                        }
                    }
                    // finalPts and zMinShift left from the last bisect iteration
                }
            }
            // else: hi bracket already within tolerance; finalPts / zMinShift set above
        }

        // ----------------------------------------------------------------
        // 5. Global shift: lift profile so no point is below snow (z = 0)
        // ----------------------------------------------------------------
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

        println("generateBaseline: done  shift=" ~ round(zMinShift * 1e6) / 1e3 ~
                " mm  camber(post-shift)=" ~
                round(measureCamberHeight(finalPts, xFRCP, xARCP) * 1e6) / 1e3 ~ " mm");

        // ----------------------------------------------------------------
        // 6. Fit one spline over the full span, split at rocker joints
        //    so that camber/forebody/aftbody share exact endpoints (G1).
        // ----------------------------------------------------------------
        // hasForeRocker / hasAftRocker already declared before step 4

        var outputPoints = [];
        for (var pt in finalPts)
        {
            outputPoints = append(outputPoints, vector(pt.x, 0 * meter, pt.z));
        }

        if (size(outputPoints) < 2)
        {
            throw regenError("Baseline solver produced insufficient points.");
        }

        try
        {
            var approxFull = approximateSpline(context, {
                "degree"           : definition.curveDegree,
                "tolerance"        : definition.approxTolerance,
                "isPeriodic"       : false,
                "targets"          : [{ "positions" : outputPoints }],
                "maxControlPoints" : definition.maxControlPoints
            });

            var fullBSpline = approxFull[0];

            // Project rocker contact points onto the fitted spline to get
            // exact split parameters (guarantees G1 across all segment joins).
            var splitParams = [];
            if (hasForeRocker)
            {
                var projFRCP = projectPointOnCurve(fullBSpline,
                    vector(xFRCP, 0 * meter, interpZ(finalPts, xFRCP)), {});
                splitParams = append(splitParams, projFRCP.parameter);
            }
            if (hasAftRocker)
            {
                var projARCP = projectPointOnCurve(fullBSpline,
                    vector(xARCP, 0 * meter, interpZ(finalPts, xARCP)), {});
                splitParams = append(splitParams, projARCP.parameter);
            }

            // Split the full spline at joint parameters.
            // splitCurveMultiple preserves tangency at all split points.
            var segments = splitCurveMultiple(context, fullBSpline, splitParams);

            // Create one body per segment.
            // Segment order: [fore?] [camber] [aft?]
            var segIdx = 0;

            if (hasForeRocker)
            {
                opCreateBSplineCurve(context, id + "forebody", {
                    "bSplineCurve" : segments[segIdx]
                });
                segIdx += 1;
            }

            opCreateBSplineCurve(context, id + "camber", {
                "bSplineCurve" : segments[segIdx]
            });
            segIdx += 1;

            if (hasAftRocker)
            {
                opCreateBSplineCurve(context, id + "aftbody", {
                    "bSplineCurve" : segments[segIdx]
                });
            }

            // Merge all segment edges into a single wire body.
            var allEdgeQueries = [qCreatedBy(id + "camber", EntityType.EDGE)];
            if (hasForeRocker)
            {
                allEdgeQueries = append(allEdgeQueries, qCreatedBy(id + "forebody", EntityType.EDGE));
            }
            if (hasAftRocker)
            {
                allEdgeQueries = append(allEdgeQueries, qCreatedBy(id + "aftbody", EntityType.EDGE));
            }

            opExtractWires(context, id + "baseline", {
                "edges" : qUnion(allEdgeQueries)
            });

            // Delete the now-redundant segment bodies.
            var segBodyQueries = [qCreatedBy(id + "camber", EntityType.BODY)];
            if (hasForeRocker)
            {
                segBodyQueries = append(segBodyQueries, qCreatedBy(id + "forebody", EntityType.BODY));
            }
            if (hasAftRocker)
            {
                segBodyQueries = append(segBodyQueries, qCreatedBy(id + "aftbody", EntityType.BODY));
            }
            opDeleteBodies(context, id + "deleteSegments", {
                "entities" : qUnion(segBodyQueries)
            });

            // Name the wire body if a name was provided.
            if (definition.outputCurveName != "")
            {
                var wireBody = evaluateQuery(context, qCreatedBy(id + "baseline", EntityType.BODY));
                if (size(wireBody) > 0)
                {
                    setProperty(context, {
                        "entities"     : wireBody[0],
                        "propertyType" : PropertyType.NAME,
                        "value"        : definition.outputCurveName
                    });
                }
            }
        }
        catch (e)
        {
            println("ERROR generateBaseline: spline fitting/splitting failed — " ~ e);
            println("  output point count = " ~ size(outputPoints));
            for (var i = 0; i < size(outputPoints) - 1; i += 1)
            {
                addDebugLine(context, outputPoints[i], outputPoints[i + 1], DebugColor.RED);
            }
            throw regenError("Baseline spline fitting failed — see console output.");
        }
    });
