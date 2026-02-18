FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

// ============================================================================
// DEBUG PRINTING UTILITIES
// ============================================================================

// import tools/bspline_knots
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/dadb70c0a762573622fa609c", version : "2267a758e66498ac49f4601e");
// import tools/printing
export import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/b02d6a2bac551b24347c983f", version : "c104606e8ffc8e0964404bbc");


/**
 * Print B-spline surface data for debugging.
 * 
 * @param surface {BSplineSurface} : Surface to inspect
 * @param label {string} : Label to identify this surface in output
 * @param format {PrintFormat} : METADATA for summary, DETAILS for full data
 */
export function printSurface(surface is BSplineSurface, label is string, format is PrintFormat)
{
    println("═══════════════════════════════════════════════════════════════");
    println("SURFACE: " ~ label);
    println("═══════════════════════════════════════════════════════════════");
    
    var numU = size(surface.controlPoints);
    var numV = size(surface.controlPoints[0]);
    
    // Always print metadata
    println("  Dimensions:");
    println("    Control points: " ~ numU ~ " x " ~ numV ~ " (u x v)");
    println("    uDegree: " ~ surface.uDegree);
    println("    vDegree: " ~ surface.vDegree);
    println("    uKnots size: " ~ size(surface.uKnots) ~ " (expected: " ~ (surface.uDegree + numU + 1) ~ ")");
    println("    vKnots size: " ~ size(surface.vKnots) ~ " (expected: " ~ (surface.vDegree + numV + 1) ~ ")");
    println("  Flags:");
    println("    isRational: " ~ surface.isRational);
    println("    isUPeriodic: " ~ surface.isUPeriodic);
    println("    isVPeriodic: " ~ surface.isVPeriodic);
    
    if (surface.isRational && surface.weights != undefined)
    {
        println("    weights size: " ~ size(surface.weights) ~ " x " ~ size(surface.weights[0]));
    }
    
    // Corner points (always useful for quick sanity check)
    println("  Corner control points:");
    println("    CP[0][0]:           " ~ toString(surface.controlPoints[0][0]));
    println("    CP[" ~ (numU-1) ~ "][0]:           " ~ toString(surface.controlPoints[numU-1][0]));
    println("    CP[0][" ~ (numV-1) ~ "]:           " ~ toString(surface.controlPoints[0][numV-1]));
    println("    CP[" ~ (numU-1) ~ "][" ~ (numV-1) ~ "]:           " ~ toString(surface.controlPoints[numU-1][numV-1]));
    
    if (format == PrintFormat.DETAILS)
    {
        printSurfaceDetails(surface, numU, numV);
    }
    
    println("───────────────────────────────────────────────────────────────");
    println("");
}

/**
 * Print full surface data (knots, control points, weights).
 */
export function printSurfaceDetails(surface is BSplineSurface, numU is number, numV is number)
{
    // Knot vectors
    println("  uKnots:");
    println("    " ~ knotVectorToString(surface.uKnots));
    println("  vKnots:");
    println("    " ~ knotVectorToString(surface.vKnots));
    
    // Control points as grid
    println("  Control Points [u][v]:");
    for (var u = 0; u < numU; u += 1)
    {
        println("    Row u=" ~ u ~ ":");
        for (var v = 0; v < numV; v += 1)
        {
            var pt = surface.controlPoints[u][v];
            println("      [" ~ u ~ "][" ~ v ~ "]: " ~ pointToString(pt));
        }
    }
    
    // Weights if rational
    if (surface.isRational && surface.weights != undefined)
    {
        println("  Weights [u][v]:");
        for (var u = 0; u < numU; u += 1)
        {
            var rowStr = "    u=" ~ u ~ ": [";
            for (var v = 0; v < numV; v += 1)
            {
                if (v > 0) rowStr ~= ", ";
                rowStr ~= toString(roundToPrecision(surface.weights[u][v], 6));
            }
            rowStr ~= "]";
            println(rowStr);
        }
    }
}

/**
 * Print B-spline curve data for debugging.
 */
export function printCurve(curve is BSplineCurve, label is string, format is PrintFormat)
{
    println("═══════════════════════════════════════════════════════════════");
    println("CURVE: " ~ label);
    println("═══════════════════════════════════════════════════════════════");
    
    var numCPs = size(curve.controlPoints);
    
    println("  Dimensions:");
    println("    Control points: " ~ numCPs);
    println("    Degree: " ~ curve.degree);
    println("    Knots size: " ~ size(curve.knots) ~ " (expected: " ~ (curve.degree + numCPs + 1) ~ ")");
    println("  Flags:");
    println("    isRational: " ~ curve.isRational);
    println("    isPeriodic: " ~ curve.isPeriodic);
    
    // Endpoints
    println("  Endpoint control points:");
    println("    CP[0]:   " ~ pointToString(curve.controlPoints[0]));
    println("    CP[" ~ (numCPs-1) ~ "]: " ~ pointToString(curve.controlPoints[numCPs-1]));
    
    if (format == PrintFormat.DETAILS)
    {
        printCurveDetails(curve, numCPs);
    }
    
    println("───────────────────────────────────────────────────────────────");
    println("");
}

/**
 * Print full curve data.
 */
export function printCurveDetails(curve is BSplineCurve, numCPs is number)
{
    println("  Knots:");
    println("    " ~ knotVectorToString(curve.knots));
    
    println("  Control Points:");
    for (var i = 0; i < numCPs; i += 1)
    {
        var pt = curve.controlPoints[i];
        var line = "    [" ~ i ~ "]: " ~ pointToString(pt);
        if (curve.isRational && curve.weights != undefined)
        {
            line ~= "  w=" ~ toString(roundToPrecision(curve.weights[i], 6));
        }
        println(line);
    }
}

/**
 * Print an array of curves (for debugging curve families).
 */
export function printCurveArray(curves is array, familyLabel is string, format is PrintFormat)
{
    println("╔═══════════════════════════════════════════════════════════════╗");
    println("║ CURVE FAMILY: " ~ familyLabel ~ " (" ~ size(curves) ~ " curves)");
    println("╚═══════════════════════════════════════════════════════════════╝");
    
    for (var i = 0; i < size(curves); i += 1)
    {
        printCurve(curves[i], familyLabel ~ "[" ~ i ~ "]", format);
    }
}

/**
 * Print intersection grid for debugging.
 */
export function printIntersectionGrid(grid is array, label is string)
{
    var numU = size(grid);
    var numV = size(grid[0]);
    
    println("═══════════════════════════════════════════════════════════════");
    println("INTERSECTION GRID: " ~ label);
    println("  Dimensions: " ~ numU ~ " x " ~ numV ~ " (u x v)");
    println("═══════════════════════════════════════════════════════════════");
    
    for (var i = 0; i < numU; i += 1)
    {
        println("  u=" ~ i ~ ":");
        for (var j = 0; j < numV; j += 1)
        {
            println("    [" ~ i ~ "][" ~ j ~ "]: " ~ pointToString(grid[i][j]));
        }
    }
    println("───────────────────────────────────────────────────────────────");
    println("");
}

// ============================================================================
// FORMATTING HELPERS
// ============================================================================

/**
 * Format a 3D point nicely with units stripped and reasonable precision.
 */
export function pointToString(pt is Vector) returns string
{
    // Assuming points are in meters, convert to mm for readability
    var x = pt[0] / millimeter;
    var y = pt[1] / millimeter;
    var z = pt[2] / millimeter;
    
    return "(" ~ 
           toString(roundToPrecision(x, 4)) ~ ", " ~
           toString(roundToPrecision(y, 4)) ~ ", " ~
           toString(roundToPrecision(z, 4)) ~ ") mm";
}

/**
 * Format a knot vector, collapsing repeated values.
 */
export function knotVectorToString(knots is array) returns string
{
    if (size(knots) == 0)
    {
        return "[]";
    }
    
    var result = "[";
    var i = 0;
    
    while (i < size(knots))
    {
        var knot = knots[i];
        var mult = 1;
        
        // Count multiplicity
        while (i + mult < size(knots) && abs(knots[i + mult] - knot) < 1e-10)
        {
            mult += 1;
        }
        
        if (i > 0) result ~= ", ";
        
        if (mult > 1)
        {
            result ~= toString(roundToPrecision(knot, 6)) ~ "(x" ~ mult ~ ")";
        }
        else
        {
            result ~= toString(roundToPrecision(knot, 6));
        }
        
        i += mult;
    }
    
    result ~= "]";
    return result;
    
}