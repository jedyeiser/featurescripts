FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

/**
 * ESTIMATE DEFLECTION SOLVER
 * ===========================
 *
 * Enums, bounds, and computation helpers for the Estimate Deflection feature.
 * Imported by estimateDeflection.fs.
 *
 * Contents:
 *   - Bounds: LoadBalanceBounds, EvaluationPointBounds, AppliedLoadBounds,
 *             ApproxToleranceBounds, MaxControlPointsBounds, ApproxDegreeBounds
 *   - Helpers: interpEI, sampleEdgesForEI, resolveQueryX,
 *              loadIntensityAt, findNearestIndex
 */

// LoadType and LocationType enums are defined in estimateDeflection.fs
// (FeatureScript requires feature parameter enums to be exported from the feature file).

export const LoadBalanceBounds = {(unitless) : [0.001, .75, .999]} as RealBoundSpec;
export const EvaluationPointBounds = {(unitless) : [20, 100, 500]} as IntegerBoundSpec;
export const AppliedLoadBounds = {(unitless) : [3, 80, 600]} as RealBoundSpec;

// OutputSpan, CurveOutput, RegionType enums are defined in estimateDeflection.fs.

export const ApproxToleranceBounds  = {(meter) : [1e-7, 1e-4, 1e-2]} as LengthBoundSpec;
export const MaxControlPointsBounds = {(unitless) : [4, 50, 500]} as IntegerBoundSpec;
export const ApproxDegreeBounds     = {(unitless) : [1, 3, 9]} as IntegerBoundSpec;


// =============================================================================
// HELPER FUNCTIONS
// =============================================================================

/**
 * Linearly interpolate EI at x from a sorted eiData array.
 * Flat-extrapolates outside the data range.
 *
 * @param eiData {array} : Sorted array of { "x" : ValueWithUnits, "EI" : ValueWithUnits }
 * @param x {ValueWithUnits} : Query position
 * @returns {ValueWithUnits} : Interpolated EI in N·m²
 */
export function interpEI(eiData is array, x is ValueWithUnits) returns ValueWithUnits
{
    var n = size(eiData);
    if (n == 0)
    {
        return 0 * newton * meter * meter;
    }
    if (n == 1)
    {
        return eiData[0].EI;
    }
    if (x <= eiData[0].x)
    {
        return eiData[0].EI;
    }
    if (x >= eiData[n - 1].x)
    {
        return eiData[n - 1].EI;
    }
    for (var i = 0; i < n - 1; i += 1)
    {
        if (x >= eiData[i].x && x <= eiData[i + 1].x)
        {
            var xSpan = eiData[i + 1].x - eiData[i].x;
            if (abs(xSpan) < 1e-15 * meter)
            {
                return eiData[i].EI;
            }
            var t = (x - eiData[i].x) / xSpan;
            return eiData[i].EI + t * (eiData[i + 1].EI - eiData[i].EI);
        }
    }
    return eiData[n - 1].EI;
}

/**
 * Sample EI visualization edges and return a sorted array of { "x", "EI" } pairs.
 * EI is encoded as Z coordinate: Z [mm] → EI [N·m²].
 *
 * @param context {Context}
 * @param edgeQuery {Query} : Edges whose Z-height encodes EI
 * @param numSamples {number} : Samples per edge (uniform parameter spacing)
 * @returns {array} : Sorted array of { "x" : ValueWithUnits, "EI" : ValueWithUnits }
 */
export function sampleEdgesForEI(context is Context, edgeQuery is Query, numSamples is number) returns array
{
    var samples = [];
    var edges = evaluateQuery(context, edgeQuery);
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
                samples = append(samples, { "x" : pt[0], "EI" : EI });
            }
            catch
            {
                // Skip failed evaluations
            }
        }
    }
    // Insertion sort by x
    for (var i = 1; i < size(samples); i += 1)
    {
        var key = samples[i];
        var j = i - 1;
        while (j >= 0 && samples[j].x > key.x)
        {
            samples[j + 1] = samples[j];
            j -= 1;
        }
        samples[j + 1] = key;
    }
    return samples;
}

/**
 * Extract world X coordinate from a vertex, mate connector, or planar face query.
 *
 * @param context {Context}
 * @param q {Query} : Vertex, mate connector, or planar face
 * @returns {ValueWithUnits} : World X coordinate
 * @throws regenError if none of the entity types can be resolved
 */
export function resolveQueryX(context is Context, q is Query) returns ValueWithUnits
{
    // Try vertex
    try
    {
        var verts = evaluateQuery(context, qEntityFilter(q, EntityType.VERTEX));
        if (size(verts) > 0)
        {
            return evVertexPoint(context, { "vertex" : verts[0] })[0];
        }
    }
    catch {}

    // Try mate connector
    try
    {
        var connectors = evaluateQuery(context, qBodyType(q, BodyType.MATE_CONNECTOR));
        if (size(connectors) > 0)
        {
            return evMateConnector(context, { "mateConnector" : connectors[0] }).origin[0];
        }
    }
    catch {}

    // Try planar face
    try
    {
        var faces = evaluateQuery(context, qGeometry(q, GeometryType.PLANE));
        if (size(faces) > 0)
        {
            return evPlane(context, { "face" : faces[0] }).origin[0];
        }
    }
    catch {}

    throw regenError("Could not resolve X position from the selected query. Use a vertex, mate connector, or planar face.");
}

/**
 * Distributed load intensity at x [N/m], positive = upward.
 * Returns 0 for POINT loads (those are handled as discrete shear jumps).
 * All non-POINT shapes are normalised so the integral over the full width equals totalForce.
 *
 * @param x {ValueWithUnits} : Evaluation position
 * @param center {ValueWithUnits} : Load center position
 * @param totalForce {ValueWithUnits} : Total resultant force [N]
 * @param shape {LoadType} : Load shape enum
 * @param width {ValueWithUnits} : Full load width (zero outside this range)
 * @returns {ValueWithUnits} : Load intensity [N/m]
 */
export function loadIntensityAt(x is ValueWithUnits, center is ValueWithUnits,
    totalForce is ValueWithUnits, shape, width is ValueWithUnits) returns ValueWithUnits
{
    if (shape == "POINT")
    {
        return 0 * newton / meter;
    }
    var halfW = width / 2;
    var relX = x - center;
    if (abs(relX) > halfW)
    {
        return 0 * newton / meter;
    }
    if (shape == "CONSTANT")
    {
        return totalForce / width;
    }
    else if (shape == "LINEAR")
    {
        // Triangle: peak at center, zero at edges. Integral = totalForce.
        var t = abs(relX) / halfW; // 0 at center, 1 at edge
        return (2 * totalForce / width) * (1 - t);
    }
    else if (shape == "QUADRATIC")
    {
        // Parabola: peak at center (zero slope), zero at edges. Integral = totalForce.
        var t = relX / halfW;
        return (1.5 * totalForce / width) * (1 - t * t);
    }
    else if (shape == "QUINTIC")
    {
        // C2-smooth bump: f(t)=1-6t^5+15t^4-10t^3, integral over [-1,1] = 1. Integral = totalForce.
        var t = abs(relX) / halfW; // 0 at center, 1 at edge
        var fq = 1 - 6 * (t ^ 5) + 15 * (t ^ 4) - 10 * (t ^ 3);
        return (totalForce / halfW) * fq;
    }
    else
    {
        // LOGISTIC: symmetric logistic pair. Integral over all x = totalForce (k=10).
        var t = relX / halfW;
        var u1 = 10.0 * (t + 0.5);
        var u2 = 10.0 * (t - 0.5);
        var shapeVal = 1 / (1 + exp(-u1)) - 1 / (1 + exp(-u2));
        return (totalForce / halfW) * shapeVal;
    }
}

/**
 * Return the index in x_eval whose value is closest to target.
 *
 * @param x_eval {array} : Array of ValueWithUnits positions
 * @param target {ValueWithUnits} : Target position
 * @returns {number} : Index of closest element
 */
export function findNearestIndex(x_eval is array, target is ValueWithUnits) returns number
{
    var best = 0;
    var bestDist = abs(x_eval[0] - target);
    for (var i = 1; i < size(x_eval); i += 1)
    {
        var d = abs(x_eval[i] - target);
        if (d < bestDist)
        {
            bestDist = d;
            best = i;
        }
    }
    return best;
}
