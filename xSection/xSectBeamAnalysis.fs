FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

/**
 * BEAM ANALYSIS MODULE
 * ====================
 *
 * Simply supported beam analysis for ski bending stiffness characterization.
 *
 * Beam Configuration:
 * -------------------
 *   - Simply supported at FCP (front contact point) and ACP (aft contact point)
 *   - Point load at MRS (midpoint of FCP-ACP)
 *   - Beam span L = xACP - xFCP
 *
 * Two Analysis Methods:
 * ---------------------
 *   1. Prismatic: Uses average EI between FCP and ACP (EI_bar).
 *      Closed-form: delta = P*L^3 / (48*EI_bar)
 *
 *   2. Numerical: Uses actual EI(x) profile via Mohr's integral.
 *      delta = integral of M(x)*m(x)/EI(x) dx
 *      where M(x) = moment from real load, m(x) = moment from unit load at MRS
 *
 * Four Output Stiffnesses:
 * ------------------------
 *   prismaticStiffness_lbin  : Load (lbs) to deflect 25.4mm at MRS, prismatic beam
 *   prismaticStiffness_mm    : Deflection (mm) at MRS under 30kg load, prismatic beam
 *   estimatedStiffness_lbin  : Load (lbs) to deflect 25.4mm at MRS, variable EI
 *   estimatedStiffness_mm    : Deflection (mm) at MRS under 30kg load, variable EI
 *
 * Moment Shape Function:
 * ----------------------
 * For a simply supported beam with point load at midpoint:
 *
 *   s(xi) = xi / 2              for 0 <= xi <= L/2
 *   s(xi) = (L - xi) / 2       for L/2 < xi <= L
 *
 * where xi = local coordinate (0 at FCP, L at ACP).
 *
 * For load P at midpoint: M(x) = P * s(xi)
 * Deflection at midpoint: delta = P * integral( s(xi)^2 / EI(xi) ) dxi
 *
 * The compliance C = integral( s(xi)^2 / EI(xi) ) dxi has units m/N,
 * so delta = P * C [meters] and P = delta / C [newtons].
 *
 * EI Data:
 * --------
 * Input is an array of { x, EI } pairs sorted by x position (world X coordinate).
 * EI is linearly interpolated between data points for integration.
 */


// =============================================================================
// CONSTANTS
// =============================================================================

/** Standard test load for deflection measurement */
export const STANDARD_LOAD_KG = 30;
export const STANDARD_LOAD = 30 * 9.80665 * newton;  // 294.2 N

/** Standard deflection for load measurement */
export const STANDARD_DEFLECTION = 25.4 * millimeter;  // 1 inch

/** Conversion: 1 lbf in Newtons */
export const NEWTONS_PER_LBF = 4.44822 * newton;

/** Number of integration segments for trapezoidal rule */
export const INTEGRATION_SEGMENTS = 200;


// =============================================================================
// MAIN ENTRY POINT
// =============================================================================

/**
 * Compute all four beam stiffness values.
 *
 * @param eiData {array} : Array of { x: ValueWithUnits, EI: ValueWithUnits } sorted by x.
 *                          x = world X position (along ski), EI in N*m^2.
 * @param xFCP {ValueWithUnits} : X position of front contact point (front support)
 * @param xACP {ValueWithUnits} : X position of aft contact point (rear support)
 * @returns {map} : {
 *     EI_bar: ValueWithUnits (N*m^2),
 *     L: ValueWithUnits (m),
 *     xMRS: ValueWithUnits (m),
 *     prismaticStiffness_lbin: number,
 *     prismaticStiffness_mm: number,
 *     estimatedStiffness_lbin: number,
 *     estimatedStiffness_mm: number
 * }
 */
export function computeBeamStiffness(eiData is array, xFCP is ValueWithUnits, 
                                      xACP is ValueWithUnits) returns map
{
    var L = xACP - xFCP;
    var xMRS = (xFCP + xACP) / 2;
    
    // =====================================================================
    // Average EI between supports
    // =====================================================================
    var EI_bar = computeEIBar(eiData, xFCP, xACP);
    
    // =====================================================================
    // Prismatic beam (closed-form, midpoint loading)
    //   delta = P * L^3 / (48 * EI)
    //   P     = 48 * EI * delta / L^3
    // =====================================================================
    var L3 = L * L * L;
    
    // Deflection under 30kg load
    var prismatic_delta = STANDARD_LOAD * L3 / (48 * EI_bar);
    var prismaticStiffness_mm = prismatic_delta / millimeter;
    
    // Load for 25.4mm deflection
    var prismatic_P = 48 * EI_bar * STANDARD_DEFLECTION / L3;
    var prismaticStiffness_lbin = prismatic_P / NEWTONS_PER_LBF;
    
    // =====================================================================
    // Variable EI beam (numerical, Mohr's integral)
    //   C = integral( s(xi)^2 / EI(xi) ) dxi    [m/N]
    //   delta = P * C
    //   P     = delta / C
    // =====================================================================
    var compliance = computeCompliance(eiData, xFCP, xACP);
    
    // Deflection under 30kg load
    var estimated_delta = STANDARD_LOAD * compliance;
    var estimatedStiffness_mm = estimated_delta / millimeter;
    
    // Load for 25.4mm deflection
    var estimated_P = STANDARD_DEFLECTION / compliance;
    var estimatedStiffness_lbin = estimated_P / NEWTONS_PER_LBF;
    
    return {
        "EI_bar" : EI_bar,
        "L" : L,
        "xMRS" : xMRS,
        "prismaticStiffness_lbin" : prismaticStiffness_lbin,
        "prismaticStiffness_mm" : prismaticStiffness_mm,
        "estimatedStiffness_lbin" : estimatedStiffness_lbin,
        "estimatedStiffness_mm" : estimatedStiffness_mm
    };
}


// =============================================================================
// AVERAGE EI
// =============================================================================

/**
 * Compute average EI between two x positions using trapezoidal integration.
 *
 *   EI_bar = (1/L) * integral_{xFCP}^{xACP} EI(x) dx
 *
 * Uses dense uniform sampling with linear interpolation between data points.
 *
 * @param eiData {array} : Sorted array of { x, EI }
 * @param xFCP {ValueWithUnits} : Start of range
 * @param xACP {ValueWithUnits} : End of range
 * @returns {ValueWithUnits} : Average EI (N*m^2)
 */
export function computeEIBar(eiData is array, xFCP is ValueWithUnits, 
                              xACP is ValueWithUnits) returns ValueWithUnits
{
    var L = xACP - xFCP;
    if (L <= 0 * meter)
        return 0 * newton * meter * meter;
    
    var numSegs = INTEGRATION_SEGMENTS;
    var dx = L / numSegs;
    
    // Trapezoidal integration
    var integral = 0 * newton * meter * meter * meter;  // EI * length
    var prevEI = interpolateEI(eiData, xFCP);
    
    for (var i = 1; i <= numSegs; i += 1)
    {
        var x = xFCP + i * dx;
        var currEI = interpolateEI(eiData, x);
        
        integral += (prevEI + currEI) / 2 * dx;
        prevEI = currEI;
    }
    
    return integral / L;
}


// =============================================================================
// COMPLIANCE (MOHR'S INTEGRAL)
// =============================================================================

/**
 * Compute beam compliance at midpoint via Mohr's integral.
 *
 *   C = integral_{0}^{L} s(xi)^2 / EI(xi) dxi
 *
 * where s(xi) is the moment shape function for midpoint loading.
 *
 * Units: s [m], s^2 [m^2], EI [N*m^2], s^2/EI [1/N], dx [m]
 *   => C [m/N] (inverse spring rate: deflection per unit force)
 *
 * @param eiData {array} : Sorted array of { x, EI } in world coordinates
 * @param xFCP {ValueWithUnits} : Front support (world X)
 * @param xACP {ValueWithUnits} : Rear support (world X)
 * @returns {ValueWithUnits} : Compliance in m/N
 */
export function computeCompliance(eiData is array, xFCP is ValueWithUnits, 
                                   xACP is ValueWithUnits) returns ValueWithUnits
{
    var L = xACP - xFCP;
    if (L <= 0 * meter)
        return 0 * meter / newton;
    
    var halfL = L / 2;
    var numSegs = INTEGRATION_SEGMENTS;
    var dx = L / numSegs;
    
    var integral = 0 * meter / newton;
    
    // First evaluation point (xi = 0 => s = 0 => integrand = 0)
    var prevXi = 0 * meter;
    var prevEI = interpolateEI(eiData, xFCP);
    var prevF = evaluateIntegrand(prevXi, halfL, prevEI);
    
    for (var i = 1; i <= numSegs; i += 1)
    {
        var xi = i * dx;
        var x_world = xFCP + xi;
        
        var EI = interpolateEI(eiData, x_world);
        var f = evaluateIntegrand(xi, halfL, EI);
        
        // Trapezoidal rule
        integral += (prevF + f) / 2 * dx;
        
        prevF = f;
    }
    
    return integral;
}

/**
 * Evaluate the compliance integrand: s(xi)^2 / EI
 *
 * Returns zero for negligible EI (avoids division by zero for
 * bodies with no material data).
 *
 * @param xi {ValueWithUnits} : Local position from front support
 * @param halfL {ValueWithUnits} : Half of beam span
 * @param EI {ValueWithUnits} : Bending stiffness at this position
 * @returns {ValueWithUnits} : Integrand value in 1/N
 */
function evaluateIntegrand(xi is ValueWithUnits, halfL is ValueWithUnits, 
                            EI is ValueWithUnits)
{
    if (abs(EI) < 1e-10 * newton * meter * meter)
        return 0 / newton;
    
    var s = momentShape(xi, halfL);
    return s * s / EI;
}


// =============================================================================
// MOMENT SHAPE FUNCTION
// =============================================================================

/**
 * Moment shape function for a simply supported beam with unit load at midpoint.
 *
 * For local coordinate xi (0 at FCP, L at ACP):
 *   s(xi) = xi / 2           for 0 <= xi <= L/2
 *   s(xi) = (L - xi) / 2     for L/2 < xi <= L
 *
 * where L = 2 * halfL.
 *
 * The returned value has units of length [m]. For a real load P at midpoint,
 * the moment at position xi is M(xi) = P * s(xi).
 *
 * @param xi {ValueWithUnits} : Local position from front support
 * @param halfL {ValueWithUnits} : Half of beam span (L/2)
 * @returns {ValueWithUnits} : Moment shape value [m]
 */
function momentShape(xi is ValueWithUnits, halfL is ValueWithUnits) returns ValueWithUnits
{
    if (xi <= halfL)
        return xi / 2;
    else
        return (2 * halfL - xi) / 2;
}


// =============================================================================
// EI INTERPOLATION
// =============================================================================

/**
 * Linearly interpolate EI at a given x position from discrete section data.
 *
 * Flat extrapolation at boundaries (uses nearest data point value).
 *
 * @param eiData {array} : Sorted array of { x: ValueWithUnits, EI: ValueWithUnits }
 * @param x {ValueWithUnits} : World X position to interpolate at
 * @returns {ValueWithUnits} : Interpolated EI value (N*m^2)
 */
export function interpolateEI(eiData is array, x is ValueWithUnits) returns ValueWithUnits
{
    var n = size(eiData);
    
    if (n == 0)
        return 0 * newton * meter * meter;
    
    if (n == 1)
        return eiData[0].EI;
    
    // Below data range: flat extrapolation
    if (x <= eiData[0].x)
        return eiData[0].EI;
    
    // Above data range: flat extrapolation
    if (x >= eiData[n - 1].x)
        return eiData[n - 1].EI;
    
    // Linear interpolation within bracketing interval
    for (var i = 0; i < n - 1; i += 1)
    {
        if (x >= eiData[i].x && x <= eiData[i + 1].x)
        {
            var dx = eiData[i + 1].x - eiData[i].x;
            
            if (abs(dx) < 1e-15 * meter)
                return eiData[i].EI;
            
            var t = (x - eiData[i].x) / dx;
            return eiData[i].EI + t * (eiData[i + 1].EI - eiData[i].EI);
        }
    }
    
    return eiData[n - 1].EI;
}


// =============================================================================
// EI DATA EXTRACTION FROM CROSS-SECTION RESULTS
// =============================================================================

/**
 * Extract sorted EI(x) data from cross-section results.
 *
 * For each cross-section, takes the world X component of the frame origin
 * and pairs it with the section's EI_eff from CLT analysis.
 *
 * @param crossSectionData {map} : Full data with mechanicalProperties on each section
 * @returns {array} : Sorted array of { x: ValueWithUnits, EI: ValueWithUnits }
 */
export function extractEIData(crossSectionData is map) returns array
{
    var eiData = [];
    
    for (var section in crossSectionData.crossSections)
    {
        eiData = append(eiData, {
            "x" : section.frame.origin[0],
            "EI" : section.mechanicalProperties.EI_eff
        });
    }
    
    return sortByX(eiData);
}

/**
 * Extract neutral axis height data from cross-section results.
 *
 * @param crossSectionData {map} : Full data with mechanicalProperties on each section
 * @returns {array} : Sorted array of { x: ValueWithUnits, naHeight: ValueWithUnits }
 */
export function extractNAData(crossSectionData is map) returns array
{
    var naData = [];
    
    for (var section in crossSectionData.crossSections)
    {
        naData = append(naData, {
            "x" : section.frame.origin[0],
            "naHeight" : section.mechanicalProperties.neutralAxisY
        });
    }
    
    return sortByX(naData);
}


// =============================================================================
// SORTING UTILITY
// =============================================================================

/**
 * Sort array of maps by x field. Insertion sort — efficient for < 200 elements.
 */
function sortByX(data is array) returns array
{
    var sorted = data;
    
    for (var i = 1; i < size(sorted); i += 1)
    {
        var key = sorted[i];
        var j = i - 1;
        
        while (j >= 0 && sorted[j].x > key.x)
        {
            sorted[j + 1] = sorted[j];
            j -= 1;
        }
        
        sorted[j + 1] = key;
    }
    
    return sorted;
}
