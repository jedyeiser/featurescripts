FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

//import tools/bspline_knots
import(path : "b1e8bfe71f67389ca210ed8b/fa0241a434caffbc394f0e00/dadb70c0a762573622fa609c", version : "acff88f740b64f7b03d722aa");
//import constEnums (export/import)

export import(path : "050a4670bd42b2ca8da04540", version : "62f653b9c0d24817418e88e5");
//import modifyCurveEnd
import(path : "c6dca62049572faaa07ddd10", version : "8ec5fa2cc24fd66922b57005");
//import gordonCurveCompat
import(path : "b9e1608a507a242d87720d9b", version : "dba5bb1c924e9263b31c9b77");
//import gordonSurface
import(path : "b3c74a9035256a2ff6bd0004", version : "5285ae6a25233c643afff38e");



/**
 * Sample points along an iso-curve on a face.
 * 
 * @param direction {"U" or "V"} : Which parameter to hold fixed
 * @param fixedParam {number} : The fixed parameter value (0-1)
 * @param numSamples {number} : Number of points to sample
 * @returns {array} : Array of 3D points (Vectors with length units)
 */
export function sampleSurfaceIsoCurve(context is Context, faceQuery is Query, 
                                       direction is string, fixedParam is number, 
                                       numSamples is number) returns array
{
    var uvParams = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        var t = i / (numSamples - 1);
        if (direction == "U")
        {
            // Fix U, vary V
            uvParams = append(uvParams, vector(fixedParam, t));
        }
        else
        {
            // Fix V, vary U
            uvParams = append(uvParams, vector(t, fixedParam));
        }
    }
    
    var points = [];
    for (var uv in uvParams)
    {
        var tangentPlane = evFaceTangentPlane(context, {
            "face" : faceQuery,
            "parameter" : uv
        });
        points = append(points, tangentPlane.origin);
    }
    
    return points;
}

/**
 * Get cross-tangent direction at a boundary point using finite difference.
 * Cross-tangent is the surface derivative perpendicular to the boundary.
 *
 * @param boundaryEdge {"U0", "U1", "V0", "V1"} : Which boundary
 * @param param {number} : Parameter along the boundary (0-1)
 */
export function getCrossTangent(context is Context, faceQuery is Query,
                                 boundaryEdge is string, param is number) returns Vector
{
    const epsilon = 1e-6;
    var uv0;
    var uvEps;
    
    if (boundaryEdge == "V0")
    {
        // At v=0, cross-tangent is ∂S/∂v direction
        uv0 = vector(param, 0);
        uvEps = vector(param, epsilon);
    }
    else if (boundaryEdge == "V1")
    {
        // At v=1, cross-tangent is -∂S/∂v direction (pointing inward)
        uv0 = vector(param, 1);
        uvEps = vector(param, 1 - epsilon);
    }
    else if (boundaryEdge == "U0")
    {
        // At u=0, cross-tangent is ∂S/∂u direction
        uv0 = vector(0, param);
        uvEps = vector(epsilon, param);
    }
    else // U1
    {
        // At u=1, cross-tangent is -∂S/∂u direction (pointing inward)
        uv0 = vector(1, param);
        uvEps = vector(1 - epsilon, param);
    }
    
    var p0 = evFaceTangentPlane(context, { "face" : faceQuery, "parameter" : uv0 }).origin;
    var pEps = evFaceTangentPlane(context, { "face" : faceQuery, "parameter" : uvEps }).origin;
    
    return normalize(pEps - p0);
}

/**
 * Estimate surface curvature in cross direction at boundary using finite difference.
 * Returns curvature magnitude (1/radius).
 */
export function getCrossCurvature(context is Context, faceQuery is Query,
                                   boundaryEdge is string, param is number) returns ValueWithUnits
{
    const epsilon = 1e-5;
    var uv0; 
    var uvNeg; 
    var uvPos;
    
    if (boundaryEdge == "V0")
    {
        uv0 = vector(param, 0);
        uvNeg = vector(param, 0);  // Can't go negative, use one-sided
        uvPos = vector(param, 2 * epsilon);
    }
    else if (boundaryEdge == "V1")
    {
        uv0 = vector(param, 1);
        uvNeg = vector(param, 1 - 2 * epsilon);
        uvPos = vector(param, 1);
    }
    else if (boundaryEdge == "U0")
    {
        uv0 = vector(0, param);
        uvNeg = vector(0, param);
        uvPos = vector(2 * epsilon, param);
    }
    else // U1
    {
        uv0 = vector(1, param);
        uvNeg = vector(1 - 2 * epsilon, param);
        uvPos = vector(1, param);
    }
    
    // Use central difference where possible, one-sided at boundaries
    var p0 = evFaceTangentPlane(context, { "face" : faceQuery, "parameter" : uv0 }).origin;
    var pNeg = evFaceTangentPlane(context, { "face" : faceQuery, "parameter" : uvNeg }).origin;
    var pPos = evFaceTangentPlane(context, { "face" : faceQuery, "parameter" : uvPos }).origin;
    
    // Second derivative approximation: (f(x+h) - 2f(x) + f(x-h)) / h²
    // At boundary, use one-sided: (f(x+2h) - 2f(x+h) + f(x)) / h²
    var d2;
    if (boundaryEdge == "V0" || boundaryEdge == "U0")
    {
        var pMid = evFaceTangentPlane(context, { "face" : faceQuery, "parameter" : 
            (boundaryEdge == "V0") ? vector(param, epsilon) : vector(epsilon, param) }).origin;
        d2 = (pPos - 2 * pMid + p0);
    }
    else
    {
        var pMid = evFaceTangentPlane(context, { "face" : faceQuery, "parameter" :
            (boundaryEdge == "V1") ? vector(param, 1 - epsilon) : vector(1 - epsilon, param) }).origin;
        d2 = (p0 - 2 * pMid + pNeg);
    }
    
    // Curvature ≈ |d²S/dt²| / |dS/dt|² for arc-length parameterized curve
    // This is approximate since we're not arc-length parameterized
    var d1 = pPos - pNeg;
    var d1Mag = norm(d1);
    
    if (d1Mag < 1e-12 * meter)
    {
        return 0 / meter;
    }
    
    var d2Mag = norm(d2);
    return d2Mag / (d1Mag * d1Mag) * (epsilon * epsilon);
}

/**
 * Simplify a curve with boundary continuity constraints.
 * Approximates points as a lower-complexity spline, then adjusts
 * endpoint control points to enforce G1/G2 continuity.
 *
 * @param points {array} : Sampled points along the iso-curve
 * @param tolerance {ValueWithUnits} : Approximation tolerance
 * @param startTangent {Vector} : Desired tangent direction at v=0 (cross-boundary)
 * @param endTangent {Vector} : Desired tangent direction at v=1 (cross-boundary)
 * @param startCurvature {ValueWithUnits} : Desired curvature at v=0
 * @param endCurvature {ValueWithUnits} : Desired curvature at v=1
 * @param continuityType {GeometricContinuity} : G0, G1, or G2
 * @param g2Mode {G2Mode} : EXACT or BEST_EFFORT
 */
export function simplifyCurveWithConstraints(context is Context, 
                                              points is array,
                                              tolerance is ValueWithUnits,
                                              startTangent is Vector,
                                              endTangent is Vector,
                                              startCurvature is ValueWithUnits,
                                              endCurvature is ValueWithUnits,
                                              continuityType is GeometricContinuity,
                                              g2Mode is G2Mode) returns BSplineCurve
{
    var numPoints = size(points);
    var approxResult = approximateSpline(context, {
        "degree" : 3,
        "tolerance" : tolerance,
        "isPeriodic" : false,
        "targets" : [approximationTarget({ "positions" : points })],
        "interpolateIndices" : [0, numPoints - 1]
    });
    
    var curve = approxResult[0];
    
    if (continuityType == GeometricContinuity.G0)
    {
        return curve;
    }
    
    // Enforce G1 at both ends
    curve = enforceG1AtEnd(curve, 0, startTangent);
    curve = enforceG1AtEnd(curve, 1, endTangent);
    
    if (continuityType == GeometricContinuity.G1)
    {
        return curve;
    }
    
    // Enforce G2 at both ends
    curve = enforceG2AtEnd(curve, 0, startCurvature, g2Mode);
    curve = enforceG2AtEnd(curve, 1, endCurvature, g2Mode);
    
    return curve;
}

/**
 * Build a BSplineSurface by skinning through an array of compatible iso-curves.
 * Each curve represents a V-direction iso-curve sampled at a known U parameter.
 * Interpolates through corresponding control points in the U direction.
 *
 * @param context {Context}
 * @param id {Id}
 * @param curves {array} : Array of compatible BSplineCurves (same degree, knots, CP count)
 * @param uParams {array} : Parameter values where each curve was extracted
 * @returns {BSplineSurface}
 */
export function buildSurfaceFromCurves(context is Context, id is Id,
                                        curves is array, uParams is array) returns BSplineSurface
{
    if (size(curves) == 0)
    {
        throw regenError("No curves provided");
    }
    if (size(curves) != size(uParams))
    {
        throw regenError("curves and uParams must have same length");
    }
    
    var numCurves = size(curves);
    var numCPsV = size(curves[0].controlPoints);
    var vDegree = curves[0].degree;
    var vKnots = curves[0].knots;
    
    // U-direction degree (clamped to what's achievable)
    var uDegree = min(3, numCurves - 1);
    
    // For each control point index along the curve (v-direction),
    // gather the corresponding CP from each curve and interpolate
    // through them in the u-direction at the known uParams
    var columnCurves = [];
    for (var j = 0; j < numCPsV; j += 1)
    {
        var columnPoints = makeArray(numCurves);
        for (var i = 0; i < numCurves; i += 1)
        {
            columnPoints[i] = curves[i].controlPoints[j];
        }
        
        var columnCurve = approximateSpline(context, {
            "degree" : uDegree,
            "tolerance" : 1e-6 * meter,
            "isPeriodic" : false,
            "targets" : [approximationTarget({ "positions" : columnPoints })],
            "parameters" : uParams,
            "interpolateIndices" : range(0, numCurves - 1)
        })[0];
        
        columnCurves = append(columnCurves, columnCurve);
    }
    
    // Make column curves compatible (same u-knots and u-CP count)
    columnCurves = makeCurvesCompatible(context, id + "uCompat", columnCurves);
    
    var uKnots = columnCurves[0].knots;
    var numCPsU = size(columnCurves[0].controlPoints);
    
    // Assemble control point grid: surfaceCPs[u][v]
    var surfaceCPs = makeArray(numCPsU);
    for (var ui = 0; ui < numCPsU; ui += 1)
    {
        surfaceCPs[ui] = makeArray(numCPsV);
        for (var vi = 0; vi < numCPsV; vi += 1)
        {
            surfaceCPs[ui][vi] = columnCurves[vi].controlPoints[ui];
        }
    }
    
    var surfaceDef = {
        "uDegree" : columnCurves[0].degree,
        "vDegree" : vDegree,
        "isUPeriodic" : false,
        "isVPeriodic" : false,
        "isRational" : false,
        "controlPoints" : controlPointMatrix(surfaceCPs),
        "uKnots" : knotArray(uKnots),
        "vKnots" : knotArray(vKnots)
    };
    
    surfaceDef = normalizeSurfaceDef(surfaceDef);
    
    return bSplineSurface(surfaceDef);
}

/**
 * Build a clamped knot vector for interpolating through given parameter values.
 * Uses knot averaging for interior knots (P&T eq. 9.8).
 */
export function buildClampedKnotVector(params is array, degree is number) returns array
{
    var n = size(params) - 1;  // n+1 points → n+1 control points for interpolation
    var m = n + degree + 1;    // m+1 knots
    
    var knots = makeArray(m + 1);
    
    // Clamped start: degree+1 copies of first param
    for (var i = 0; i <= degree; i += 1)
    {
        knots[i] = params[0];
    }
    
    // Clamped end: degree+1 copies of last param
    for (var i = m - degree; i <= m; i += 1)
    {
        knots[i] = params[n];
    }
    
    // Interior knots via averaging (P&T eq. 9.8)
    for (var j = 1; j <= n - degree; j += 1)
    {
        var sum = 0;
        for (var i = j; i <= j + degree - 1; i += 1)
        {
            sum += params[i];
        }
        knots[j + degree] = sum / degree;
    }
    
    return knots;
}

/**
 * Main surface cleanup function.
 */
export function cleanupSurface(context is Context, id is Id, 
                                faceQuery is Query,
                                tolerance is ValueWithUnits,
                                continuityType is GeometricContinuity,
                                g2Mode is G2Mode,
                                mode is CleanupMode,
                                uCurveCount is number,
                                vCurveCount is number) returns BSplineSurface
{
    // Step 1: Determine iso-parameters to sample
    var uParams;
    var vParams;
    
    if (mode == CleanupMode.AUTO)
    {
        // Use uniform spacing based on curve counts
        uParams = [];
        for (var i = 0; i < uCurveCount; i += 1)
        {
            uParams = append(uParams, i / (uCurveCount - 1));
        }
        vParams = [];
        for (var i = 0; i < vCurveCount; i += 1)
        {
            vParams = append(vParams, i / (vCurveCount - 1));
        }
    }
    else // MANUAL - could extend to take explicit arrays
    {
        uParams = [];
        for (var i = 0; i < uCurveCount; i += 1)
        {
            uParams = append(uParams, i / (uCurveCount - 1));
        }
        vParams = [];
        for (var i = 0; i < vCurveCount; i += 1)
        {
            vParams = append(vParams, i / (vCurveCount - 1));
        }
    }
    
    const numSamplesPerCurve = 50;  // Points to sample along each iso-curve
    
    // Step 2: Extract and simplify V-direction iso-curves (one for each U)
    var vCurves = [];
    for (var u in uParams)
    {
        // Sample points along this iso-curve
        var points = sampleSurfaceIsoCurve(context, faceQuery, "U", u, numSamplesPerCurve);
        
        // Get boundary constraints at v=0 and v=1
        var startTangent = getCrossTangent(context, faceQuery, "V0", u);
        var endTangent = getCrossTangent(context, faceQuery, "V1", u);
        var startCurvature = getCrossCurvature(context, faceQuery, "V0", u);
        var endCurvature = getCrossCurvature(context, faceQuery, "V1", u);
        
        // Simplify with constraints
        var curve = simplifyCurveWithConstraints(context, points, tolerance,
            startTangent, endTangent, startCurvature, endCurvature,
            continuityType, g2Mode);
        
        vCurves = append(vCurves, curve);
    }
    
    // Step 3: Make all curves compatible
    var compatibleCurves = makeCurvesCompatible(context, id + "compat", vCurves);
    
    // Step 4: Build surface from compatible curves
    var newSurface = buildSurfaceFromCurves(context, id + "buildSurf", compatibleCurves, uParams);
    
    return newSurface;
}