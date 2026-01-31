FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

/**
 * Formatted printing utilities for debugging BSpline curves and surfaces.
 *
 * Provides structured console output for inspecting BSpline data structures.
 * Useful for development, debugging, and understanding curve/surface properties.
 *
 * @source gordonSurface/scaledCurve.fs, gordonSurface/gordonSurface.fs
 */

/**
 * Print format options for BSpline data.
 *
 * - METADATA: Basic information only (degree, counts, flags)
 * - DETAILS: Full information including control points, knots, and weights
 *
 * @source gordonSurface/constEnums.fs:35-41
 */
export enum PrintFormat
{
    annotation { "Name" : "Metadata" }
    METADATA,
    annotation { "Name" : "Details" }
    DETAILS
}

/**
 * Print BSplineCurve data to console.
 *
 * Outputs structured information about a BSpline curve. In METADATA mode,
 * prints basic properties (degree, control point count, knot count, etc.).
 * In DETAILS mode, also prints all control points, the full knot vector,
 * and weights (for rational curves).
 *
 * @param curve {BSplineCurve} : Curve to print
 * @param format {PrintFormat} : Level of detail (METADATA or DETAILS)
 * @param tags {array} : Optional [startTag, endTag] strings to wrap output
 *
 * @example `printBSpline(myCurve, PrintFormat.METADATA, ["=== My Curve ==="])`
 *
 * @source gordonSurface/scaledCurve.fs:582-662
 */
export function printBSpline(curve is BSplineCurve, format is PrintFormat, tags is array)
{
    // Start tag
    if (size(tags) >= 1 && tags[0] != undefined && tags[0] != "")
    {
        println(tags[0]);
    }

    // Always print basic metadata
    println("  Degree: " ~ curve.degree);
    println("  Control Points: " ~ size(curve.controlPoints));
    println("  Knots: " ~ size(curve.knots));
    println("  Rational: " ~ curve.isRational);
    println("  Periodic: " ~ curve.isPeriodic);

    if (curve.dimension != undefined)
    {
        println("  Dimension: " ~ curve.dimension);
    }

    var numSpans = size(curve.knots) - 2 * curve.degree - 1;
    println("  Spans: " ~ numSpans);

    if (format == PrintFormat.DETAILS)
    {
        println("");
        println("  --- Control Points ---");
        for (var i = 0; i < size(curve.controlPoints); i += 1)
        {
            var pt = curve.controlPoints[i];
            println("    [" ~ i ~ "]: " ~ formatVector(pt));
        }

        println("");
        println("  --- Knot Vector ---");
        var knotStr = "    [";
        for (var i = 0; i < size(curve.knots); i += 1)
        {
            knotStr = knotStr ~ roundDecimal(curve.knots[i], 6);
            if (i < size(curve.knots) - 1)
            {
                knotStr = knotStr ~ ", ";
            }
            // Line break every 8 knots for readability
            if ((i + 1) % 8 == 0 && i < size(curve.knots) - 1)
            {
                knotStr = knotStr ~ "\n     ";
            }
        }
        knotStr = knotStr ~ "]";
        println(knotStr);

        if (curve.isRational && curve.weights != undefined)
        {
            println("");
            println("  --- Weights ---");
            var weightStr = "    [";
            for (var i = 0; i < size(curve.weights); i += 1)
            {
                weightStr = weightStr ~ roundDecimal(curve.weights[i], 6);
                if (i < size(curve.weights) - 1)
                {
                    weightStr = weightStr ~ ", ";
                }
                if ((i + 1) % 8 == 0 && i < size(curve.weights) - 1)
                {
                    weightStr = weightStr ~ "\n     ";
                }
            }
            weightStr = weightStr ~ "]";
            println(weightStr);
        }
    }

    // End tag
    if (size(tags) >= 2 && tags[1] != undefined && tags[1] != "")
    {
        println(tags[1]);
    }
}

/**
 * Print multiple curves with a common title.
 *
 * Convenience function for printing several curves in sequence with
 * numbered headers.
 *
 * @param curves {array} : Array of BSplineCurve objects
 * @param title {string} : Title prefix for each curve
 * @param format {PrintFormat} : Level of detail
 *
 * @example `printCurveArray([curve1, curve2], "U-Curve", PrintFormat.METADATA)`
 */
export function printCurveArray(curves is array, title is string, format is PrintFormat)
{
    for (var i = 0; i < size(curves); i += 1)
    {
        var header = "--- " ~ title ~ " [" ~ i ~ "] ---";
        printBSpline(curves[i], format, [header]);
    }
}

/**
 * Format a 3D vector for pretty printing.
 *
 * Converts a Vector to a readable string representation. Handles both
 * vectors with units (typical for control points in meters) and unitless
 * vectors. Rounds components to 6 decimal places.
 *
 * @param v {Vector} : Vector to format
 * @returns {string} : Formatted string representation
 *
 * @example `formatVector(vector(1.23456789, 2.5, 3.7) * meter)` returns `"(1.234568, 2.5, 3.7) m"`
 * @example `formatVector(vector(0, 0, 1))` returns `"(0, 0, 1)"`
 *
 * @source gordonSurface/scaledCurve.fs:667-685
 */
export function formatVector(v is Vector) returns string
{
    var x = v[0];
    var y = v[1];
    var z = v[2];

    // Try to extract value in meters for clean display
    try silent
    {
        x = x / meter;
        y = y / meter;
        z = z / meter;
        return "(" ~ roundDecimal(x, 6) ~ ", " ~ roundDecimal(y, 6) ~ ", " ~ roundDecimal(z, 6) ~ ") m";
    }

    // Fallback for unitless vectors
    return "(" ~ roundDecimal(x, 6) ~ ", " ~ roundDecimal(y, 6) ~ ", " ~ roundDecimal(z, 6) ~ ")";
}

/**
 * Round a number to specified decimal places.
 *
 * Simple rounding utility for clean numerical output. Multiplies by 10^places,
 * rounds to nearest integer, then divides back.
 *
 * @param value {number} : Number to round
 * @param places {number} : Number of decimal places
 * @returns {number} : Rounded value
 *
 * @example `roundDecimal(3.14159, 2)` returns `3.14`
 * @example `roundDecimal(1.23456, 4)` returns `1.2346`
 *
 * @source gordonSurface/scaledCurve.fs:690-698
 */
export function roundDecimal(value is number, places is number) returns number
{
    var factor = 1;
    for (var i = 0; i < places; i += 1)
    {
        factor = factor * 10;
    }
    return round(value * factor) / factor;
}
