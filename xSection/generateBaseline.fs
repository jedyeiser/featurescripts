FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// IMPORT: xSectReferencePoints.fs
import(path : "08fddb59786b6bfee020ee05", version : "201c64079ee529cdd6609fe7");
// IMPORT: xSectBeamAnalysis.fs
import(path : "ebac109589e3bf405d3f3ae7", version : "6ad04ba6be4ab10b452261ce");

// IMPORT: tools/curve_operations.fs
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/a7403d5f7f5a4fef8225b768", version : "e5b9e00c5a237415c89a66b7");

// IMPORT: tools/point_projection.fs
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/eb46317a27a44e391e11dfe6", version : "0cea3c8d27e4f7fd660aa69f");

// IMPORT: analyzeBaseline.fs
import(path : "f0717a1116fee7304957da5b", version : "ab5e058352be20fa5756231b");



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
    hiddenBodies is Query, clickedButton is string) returns map
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

    // Clamp all sampled EI values to non-negative.
    // opFitSpline can produce negative Z near steep endpoints (cubic overshoot),
    // which decodes as negative EI — physically impossible and causes k=0 clamp
    // spikes in solveCamberBeam that introduce spurious inflections.
    for (var i = 0; i < size(points); i += 1)
    {
        if (points[i].EI < 0 * newton * meter * meter)
        {
            points[i] = { "x" : points[i].x, "EI" : 0 * newton * meter * meter };
        }
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
 * Returns { "finalPts" : array of {x,z}, "zMinShift" : number (plain meters, ≤ 0) }.
 */
function runMCHBisection(context is Context,
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

        annotation { "Name" : "Add baseline sketch" }
        definition.addBaselineSketch is boolean;

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
        // 4-5. MCH bisection + FCPH / ACPH convergence
        //
        // runMCHBisection converges the camber height to MCH_target and
        // applies the global Z-shift.  It accepts fcpH/acpH as VERTICAL
        // tip-height targets (Z above snow after the shift).
        //
        // The FCPH/ACPH loop corrects those effective vertical targets so
        // that the perpendicular distances from the FCP/ACP tips to the
        // FRCP/ARCP tangent lines match the user's spec inputs.
        //
        // Derivation of the perpendicular distance (2D cross product):
        //
        //   Forebody:
        //     FRCP→FCP = (xFCP−xFRCP, Z_FCP−Z_FRCP) = (−frcpl_m, Z_FCP−Z_FRCP)
        //     tangent  = (1, mFore) / ||...||   (mFore > 0, camber rises right)
        //     dist     = |−frcpl_m·mFore − (Z_FCP−Z_FRCP)| / √(1+mFore²)
        //              = (frcpl_m·mFore + Z_FCP − Z_FRCP) / √(1+mFore²)
        //
        //   Aftbody:
        //     ARCP→ACP = (xACP−xARCP, Z_ACP−Z_ARCP) = (+arcpl_m, Z_ACP−Z_ARCP)
        //     tangent  = (1, mAft) / ||...||    (mAft < 0, camber descends right)
        //     dist     = |arcpl_m·mAft − (Z_ACP−Z_ARCP)| / √(1+mAft²)
        //
        // Newton step (relationship is linear in the vertical tip height):
        //   fcpHeightEff_new = fcpHeightEff − err_fcph · √(1+mFore²)
        //   acpHeightEff_new = acpHeightEff − err_acph · √(1+mAft²)
        //
        // Converges in 1-2 iterations for typical rocker angles.
        // ----------------------------------------------------------------
        var FCH_TOL      = 5e-7;   // 0.5 µm in plain meters (< 0.001 mm)
        var fcpHeightEff = definition.fcpHeight;
        var acpHeightEff = definition.acpHeight;
        var MCH_m        = definition.camberHeight / meter;

        var mchResult = runMCHBisection(context, eiData, hasEI,
                                         xFCP, xACP, xFRCP, xARCP, xMount,
                                         fcpHeightEff, acpHeightEff,
                                         definition.frcpl, definition.arcpl,
                                         hasForeRocker, hasAftRocker, MCH_m);
        var finalPts = mchResult.finalPts;

        for (var rocIter = 0; rocIter < 8; rocIter += 1)
        {
            var fcph_err = 0.0;
            var acph_err = 0.0;

            if (hasForeRocker)
            {
                var mFore   = slopeAt(finalPts, xFRCP);
                var frcpl_m = (xFRCP - xFCP) / meter;
                var zFRCP_m = interpZ(finalPts, xFRCP) / meter;
                var zFCP_m  = interpZ(finalPts, xFCP)  / meter;
                var fcph_m  = abs(frcpl_m * mFore + zFCP_m - zFRCP_m) / sqrt(1 + mFore * mFore);
                fcph_err    = fcph_m - definition.fcpHeight / meter;
                println("rocIter=" ~ rocIter ~
                        " FCPH=" ~ round(fcph_m * 1e6) / 1e3 ~
                        " spec=" ~ round(definition.fcpHeight / millimeter * 1e3) / 1e3 ~
                        " err="  ~ round(fcph_err * 1e6) / 1e3 ~ " mm");
                if (abs(fcph_err) > FCH_TOL)
                {
                    fcpHeightEff = fcpHeightEff - fcph_err * sqrt(1 + mFore * mFore) * meter;
                }
            }

            if (hasAftRocker)
            {
                var mAft    = slopeAt(finalPts, xARCP);
                var arcpl_m = (xACP - xARCP) / meter;
                var zARCP_m = interpZ(finalPts, xARCP) / meter;
                var zACP_m  = interpZ(finalPts, xACP)  / meter;
                var acph_m  = abs(arcpl_m * mAft - (zACP_m - zARCP_m)) / sqrt(1 + mAft * mAft);
                acph_err    = acph_m - definition.acpHeight / meter;
                println("rocIter=" ~ rocIter ~
                        " ACPH=" ~ round(acph_m * 1e6) / 1e3 ~
                        " spec=" ~ round(definition.acpHeight / millimeter * 1e3) / 1e3 ~
                        " err="  ~ round(acph_err * 1e6) / 1e3 ~ " mm");
                if (abs(acph_err) > FCH_TOL)
                {
                    acpHeightEff = acpHeightEff - acph_err * sqrt(1 + mAft * mAft) * meter;
                }
            }

            if (abs(fcph_err) < FCH_TOL && abs(acph_err) < FCH_TOL)
            {
                println("generateBaseline: FCPH/ACPH converged at iter=" ~ rocIter);
                break;
            }

            mchResult = runMCHBisection(context, eiData, hasEI,
                                         xFCP, xACP, xFRCP, xARCP, xMount,
                                         fcpHeightEff, acpHeightEff,
                                         definition.frcpl, definition.arcpl,
                                         hasForeRocker, hasAftRocker, MCH_m);
            finalPts = mchResult.finalPts;
        }

        println("generateBaseline: done  fcpHeff=" ~
                round(fcpHeightEff / millimeter * 1000) / 1000 ~
                " mm  acpHeff=" ~ round(acpHeightEff / millimeter * 1000) / 1000 ~ " mm");

        // ----------------------------------------------------------------
        // 6. Fit camber spline separately, then build exact G1 Bézier
        //    rockers tangent to the fitted camber at the junction points.
        //
        //    Rationale: fitting one big spline over all ~300 points then
        //    splitting near FRCP/ARCP shares the approximation budget across
        //    the whole span and, when an EI profile is used, the k=0 clamp
        //    near the EI profile endpoints causes a curvature kink that the
        //    spline fitter absorbs by introducing a spurious inflection in
        //    the camber pocket.  Fitting the camber alone dedicates the full
        //    control-point budget to the camber shape.  The rockers become
        //    exact degree-2 Béziers whose G1 tangent at the junction is
        //    read directly from the fitted camber endpoint.
        // ----------------------------------------------------------------

        try
        {
            // Bucket finalPts into camber vs rocker regions.
            // Camber:     xFRCP ≤ x ≤ xARCP  (includes both junction points)
            // Fore rocker: x < xFRCP          (FCP tip side)
            // Aft rocker:  x > xARCP          (ACP tip side)
            var camberPts     = [];
            var foreRockerPts = [];
            var aftRockerPts  = [];
            var GEOM_TOL_BKT  = 1e-9 * meter;

            for (var pt in finalPts)
            {
                var inFore = hasForeRocker && (pt.x < xFRCP - GEOM_TOL_BKT);
                var inAft  = hasAftRocker  && (pt.x > xARCP + GEOM_TOL_BKT);
                if (inFore)
                {
                    foreRockerPts = append(foreRockerPts, vector(pt.x, 0 * meter, pt.z));
                }
                else if (inAft)
                {
                    aftRockerPts = append(aftRockerPts, vector(pt.x, 0 * meter, pt.z));
                }
                else
                {
                    camberPts = append(camberPts, vector(pt.x, 0 * meter, pt.z));
                }
            }

            if (size(camberPts) < 2)
            {
                throw regenError("Baseline solver produced insufficient camber points.");
            }

            // --- Fit the camber spline (full approximation budget) ---
            var approxCamber = approximateSpline(context, {
                "degree"           : definition.curveDegree,
                "tolerance"        : definition.approxTolerance,
                "isPeriodic"       : false,
                "targets"          : [{ "positions" : camberPts }],
                "maxControlPoints" : definition.maxControlPoints
            });

            opCreateBSplineCurve(context, id + "camber", {
                "bSplineCurve" : approxCamber[0]
            });

            var camberEdges = evaluateQuery(context, qCreatedBy(id + "camber", EntityType.EDGE));
            if (size(camberEdges) == 0)
            {
                throw regenError("Baseline: camber spline body produced no edges.");
            }
            var camberEdge = camberEdges[0];

            // Track which rocker bodies were actually created.
            var hasForeBody = false;
            var hasAftBody  = false;

            // --- Build G1 Bézier forebody rocker ---
            // Degree-2 Bézier: P0 = FCP tip, P1 = G1 control, P2 = fitted camber start.
            //
            //   G1 at P2: Bézier tangent at t=1 = 2*(P2-P1) ∝ camberStartTan
            //   => P1 = P2 - (α/2)*camberStartTan
            //      α  = (P2.x - P0.x) / camberStartTan.x
            //   => P1.x = midpoint(P0.x, P2.x)
            if (hasForeRocker && size(foreRockerPts) >= 1)
            {
                var camberStartCurv = evEdgeCurvatures(context, { "edge" : camberEdge, "parameters" : [0.0] });
                var camberStartPt   = camberStartCurv[0].frame.origin;
                var camberStartTan  = camberStartCurv[0].frame.zAxis;  // normalized tangent, points FCP→ACP

                var fTipPt = foreRockerPts[0];  // lowest-X point = FCP tip
                var dxFore = camberStartPt[0] - fTipPt[0];

                if (abs(camberStartTan[0]) > 0.01)
                {
                    var alphaFore = dxFore / camberStartTan[0];
                    var P1fore = vector(
                        camberStartPt[0] - (alphaFore / 2) * camberStartTan[0],
                        0 * meter,
                        camberStartPt[2] - (alphaFore / 2) * camberStartTan[2]
                    );
                    opCreateBSplineCurve(context, id + "forebody", {
                        "bSplineCurve" : bSplineCurve({
                            "degree"        : 2,
                            "isPeriodic"    : false,
                            "controlPoints" : [fTipPt, P1fore, camberStartPt]
                        })
                    });
                    hasForeBody = true;
                }
                else
                {
                    // Near-vertical junction tangent: approximate through fore points.
                    var forePoints = append(foreRockerPts, camberStartPt);
                    var approxFore = approximateSpline(context, {
                        "degree"           : definition.curveDegree,
                        "tolerance"        : definition.approxTolerance,
                        "isPeriodic"       : false,
                        "targets"          : [{ "positions" : forePoints }],
                        "maxControlPoints" : definition.maxControlPoints
                    });
                    opCreateBSplineCurve(context, id + "forebody", {
                        "bSplineCurve" : approxFore[0]
                    });
                    hasForeBody = true;
                }
            }

            // --- Build G1 Bézier aftbody rocker ---
            // Degree-2 Bézier: P0 = fitted camber end, P1 = G1 control, P2 = ACP tip.
            //
            //   G1 at P0: Bézier tangent at t=0 = 2*(P1-P0) ∝ camberEndTan
            //   => P1 = P0 + (β/2)*camberEndTan
            //      β  = (P2.x - P0.x) / camberEndTan.x
            //   => P1.x = midpoint(P0.x, P2.x)
            if (hasAftRocker && size(aftRockerPts) >= 1)
            {
                var camberEndCurv = evEdgeCurvatures(context, { "edge" : camberEdge, "parameters" : [1.0] });
                var camberEndPt   = camberEndCurv[0].frame.origin;
                var camberEndTan  = camberEndCurv[0].frame.zAxis;  // normalized tangent, points FCP→ACP

                var aTipPt = aftRockerPts[size(aftRockerPts) - 1];  // highest-X point = ACP tip
                var dxAft  = aTipPt[0] - camberEndPt[0];

                if (abs(camberEndTan[0]) > 0.01)
                {
                    var betaAft = dxAft / camberEndTan[0];
                    var P1aft = vector(
                        camberEndPt[0] + (betaAft / 2) * camberEndTan[0],
                        0 * meter,
                        camberEndPt[2] + (betaAft / 2) * camberEndTan[2]
                    );
                    opCreateBSplineCurve(context, id + "aftbody", {
                        "bSplineCurve" : bSplineCurve({
                            "degree"        : 2,
                            "isPeriodic"    : false,
                            "controlPoints" : [camberEndPt, P1aft, aTipPt]
                        })
                    });
                    hasAftBody = true;
                }
                else
                {
                    // Near-vertical junction tangent: approximate through aft points.
                    var aftPoints = concatenateArrays([[camberEndPt], aftRockerPts]);
                    var approxAft = approximateSpline(context, {
                        "degree"           : definition.curveDegree,
                        "tolerance"        : definition.approxTolerance,
                        "isPeriodic"       : false,
                        "targets"          : [{ "positions" : aftPoints }],
                        "maxControlPoints" : definition.maxControlPoints
                    });
                    opCreateBSplineCurve(context, id + "aftbody", {
                        "bSplineCurve" : approxAft[0]
                    });
                    hasAftBody = true;
                }
            }

            // --- Merge all segment edges into a single wire body ---
            var allEdgeQueries = [qCreatedBy(id + "camber", EntityType.EDGE)];
            if (hasForeBody)
            {
                allEdgeQueries = append(allEdgeQueries, qCreatedBy(id + "forebody", EntityType.EDGE));
            }
            if (hasAftBody)
            {
                allEdgeQueries = append(allEdgeQueries, qCreatedBy(id + "aftbody", EntityType.EDGE));
            }

            opExtractWires(context, id + "baseline", {
                "edges" : qUnion(allEdgeQueries)
            });

            // Delete the now-redundant segment bodies.
            var segBodyQueries = [qCreatedBy(id + "camber", EntityType.BODY)];
            if (hasForeBody)
            {
                segBodyQueries = append(segBodyQueries, qCreatedBy(id + "forebody", EntityType.BODY));
            }
            if (hasAftBody)
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

            // Baseline sketch — analyze the generated curve and output measurement geometry
            if (definition.addBaselineSketch)
            {
                var baselineEdges = qCreatedBy(id + "baseline", EntityType.EDGE);
                var result = analyzeBaselineGeometry(context, baselineEdges,
                                                     definition.fcpQuery, definition.acpQuery);
                if (result != undefined)
                {
                    var chordDir   = normalize(result.ab_min_pt - result.fb_min_pt);
                    var camberDiff = result.max_camber_pt - result.fb_min_pt;
                    var camberFoot = result.fb_min_pt + dot(camberDiff, chordDir) * chordDir;

                    var fbFoot = undefined;
                    if (result.frcp_pt != undefined)
                    {
                        var fcpDiff = result.fcp_pt - result.frcp_pt;
                        fbFoot = result.frcp_pt + dot(fcpDiff, result.frcp_dir) * result.frcp_dir;
                    }

                    var abFoot = undefined;
                    if (result.arcp_pt != undefined)
                    {
                        var acpDiff = result.acp_pt - result.arcp_pt;
                        abFoot = result.arcp_pt + dot(acpDiff, result.arcp_dir) * result.arcp_dir;
                    }

                    var sketchPl = plane(vector(0, 0, 0) * meter, vector(0, -1, 0), vector(1, 0, 0));
                    var sketch = newSketchOnPlane(context, id + "baselineMeasurementSketch", {
                        "sketchPlane" : sketchPl
                    });

                    skLineSegment(sketch, "minChord", {
                        "start"        : worldToPlane(sketchPl, result.fb_min_pt),
                        "end"          : worldToPlane(sketchPl, result.ab_min_pt),
                        "construction" : true
                    });

                    if (result.frcp_pt != undefined && result.arcp_pt != undefined)
                    {
                        skLineSegment(sketch, "inflChord", {
                            "start"        : worldToPlane(sketchPl, result.frcp_pt),
                            "end"          : worldToPlane(sketchPl, result.arcp_pt),
                            "construction" : true
                        });
                    }

                    if (result.frcp_pt != undefined && fbFoot != undefined)
                    {
                        skLineSegment(sketch, "fbTangentLeg", {
                            "start"        : worldToPlane(sketchPl, result.frcp_pt),
                            "end"          : worldToPlane(sketchPl, fbFoot),
                            "construction" : true
                        });
                        skLineSegment(sketch, "fbNormalLeg", {
                            "start"        : worldToPlane(sketchPl, result.fcp_pt),
                            "end"          : worldToPlane(sketchPl, fbFoot),
                            "construction" : true
                        });
                    }

                    if (result.arcp_pt != undefined && abFoot != undefined)
                    {
                        skLineSegment(sketch, "abTangentLeg", {
                            "start"        : worldToPlane(sketchPl, result.arcp_pt),
                            "end"          : worldToPlane(sketchPl, abFoot),
                            "construction" : true
                        });
                        skLineSegment(sketch, "abNormalLeg", {
                            "start"        : worldToPlane(sketchPl, result.acp_pt),
                            "end"          : worldToPlane(sketchPl, abFoot),
                            "construction" : true
                        });
                    }

                    skLineSegment(sketch, "camberNormal", {
                        "start"        : worldToPlane(sketchPl, result.max_camber_pt),
                        "end"          : worldToPlane(sketchPl, camberFoot),
                        "construction" : true
                    });

                    skSolve(sketch);
                }
            }
        }
        catch (e)
        {
            println("ERROR generateBaseline: spline fitting failed — " ~ e);
            throw regenError("Baseline spline fitting failed — see console output.");
        }
    });
