FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

//import tools/bspline_knots
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/dadb70c0a762573622fa609c", version : "2267a758e66498ac49f4601e");
//import constEnums (export/import)

export import(path : "050a4670bd42b2ca8da04540", version : "12d448b531f4133be59a1a61");
//import modifyCurveEnd
import(path : "c6dca62049572faaa07ddd10", version : "08f67685092465b852f9e65e");
//import gordonCurveCompat
import(path : "b9e1608a507a242d87720d9b", version : "7725b8caf230860c44ca2ae2");
//import gordonSurface
import(path : "b3c74a9035256a2ff6bd0004", version : "2c35626cef2707cee449fcbf");


// Explicit LengthBoundSpec constant — inline map literals are not auto-typed in this context
const SIMPLIFY_TOLERANCE_BOUNDS = { (millimeter) : [0.01, 1, 100] } as LengthBoundSpec;


annotation { "Feature Type Name" : "Simplify surface", "Feature Type Description" : "Takes a face and approximation parameters as input and returns a simplified 'cleaned' face" }
export const simplifySurface = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Face", "Filter" : EntityType.FACE, "MaxNumberOfPicks" : 1 }
        definition.face is Query;

        annotation { "Name" : "Tolerance" }
        isLength(definition.tolerance, SIMPLIFY_TOLERANCE_BOUNDS);

        annotation { "Name" : "Continuity" }
        definition.continuityType is GeometricContinuity;

        if (definition.continuityType == GeometricContinuity.G2)
        {
            annotation { "Name" : "G2 mode" }
            definition.g2Mode is G2Mode;
        }

        annotation { "Name" : "Mode" }
        definition.mode is CleanupMode;

        if (definition.mode == CleanupMode.MANUAL)
        {
            annotation { "Name" : "U curve count" }
            isInteger(definition.uCurveCount, { (unitless) : [2, 5, 20] });
            annotation { "Name" : "V curve count" }
            isInteger(definition.vCurveCount, { (unitless) : [2, 5, 20] });
        }

        annotation { "Name" : "Replace face" }
        definition.replaceFace is boolean;

        annotation { "Name" : "Debug print" }
        definition.debugPrint is boolean;
    }
    {
        // uCurveCount and vCurveCount are only defined in MANUAL mode
        var uCount = 0;
        var vCount = 0;
        if (definition.mode == CleanupMode.MANUAL)
        {
            uCount = definition.uCurveCount;
            vCount = definition.vCurveCount;
        }

        var newSurface = cleanupSurface(context, id, definition.face, definition.tolerance,
            definition.continuityType,
            definition.continuityType == GeometricContinuity.G2 ? definition.g2Mode : G2Mode.BEST_EFFORT,
            definition.mode, uCount, vCount, definition.debugPrint);

        opCreateBSplineSurface(context, id + "simplified", { "bSplineSurface" : newSurface });

        if (definition.replaceFace)
        {
            var newFace = qCreatedBy(id + "simplified", EntityType.FACE);
            opReplaceFace(context, id + "replace", {
                "replaceFaces"  : definition.face,
                "templateFace"  : newFace
            });
            opDeleteBodies(context, id + "cleanup", {
                "bodies" : qCreatedBy(id + "simplified", EntityType.BODY)
            });
        }
    });


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
 * Kept for external compatibility; not used by the main cleanupSurface pipeline.
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


// ============================================================
// HELPERS
// ============================================================

/**
 * Compute maximum deviation between a fitted BSplineCurve and the face iso-curve
 * at a fixed U parameter, sampled over V in [0, 1].
 *
 * Uses evaluateSpline to batch-evaluate the fitted curve, then compares each
 * point to the corresponding face point via evFaceTangentPlane.
 */
function computeCurveError(context is Context, curve is BSplineCurve,
                            faceQuery is Query, u is number,
                            numCheck is number) returns ValueWithUnits
{
    var checkParams = makeArray(numCheck);
    for (var i = 0; i < numCheck; i += 1)
    {
        checkParams[i] = i / (numCheck - 1);
    }

    // Batch-evaluate the fitted curve: result[0][i] = Vector at checkParams[i]
    var curvePositions = evaluateSpline({ "spline" : curve, "parameters" : checkParams })[0];

    var maxErr = 0 * meter;
    for (var i = 0; i < numCheck; i += 1)
    {
        var facePoint = evFaceTangentPlane(context, {
            "face" : faceQuery,
            "parameter" : vector(u, checkParams[i])
        }).origin;
        var err = norm(facePoint - curvePositions[i]);
        if (err > maxErr)
        {
            maxErr = err;
        }
    }
    return maxErr;
}

// ============================================================
// MAIN SURFACE CLEANUP
// ============================================================

/**
 * Main surface cleanup function.
 *
 * AUTO mode:
 *   Samples the face at 20 uniformly-spaced U parameters, fitting each
 *   V-direction iso-curve with approximateSpline driven purely by tolerance
 *   (no CP-count ceiling). The result is the minimum-CP surface that stays
 *   within tolerance. Always produces G1-continuous output.
 *
 * MANUAL mode:
 *   Samples the face densely along V-direction iso-curves at uCurveCount
 *   evenly-spaced U parameters. Fits each curve with approximateSpline using
 *   maxControlPoints = vCurveCount and derivative constraints for G1/G2.
 *   Skins the compatible curves into a surface.
 */
export function cleanupSurface(context is Context, id is Id,
                                faceQuery is Query,
                                tolerance is ValueWithUnits,
                                continuityType is GeometricContinuity,
                                g2Mode is G2Mode,
                                mode is CleanupMode,
                                uCurveCount is number,
                                vCurveCount is number,
                                debugPrint is boolean) returns BSplineSurface
{
    if (mode == CleanupMode.AUTO)
    {
        // ---- AUTO MODE: Binary search for minimum-CP representation ----
        // For each v-direction iso-curve, binary-search the CP count from minVCPs up
        // to srcNumVCPs.  Each candidate is evaluated against the face (not just the
        // sample points), so we find the true minimum CPs that stays within tolerance.
        //
        // U sampling: Greville abscissae of the source surface so the u-curve count
        // matches the source's u-CP count exactly (avoids inflating in u-direction).

        const numSamplesPerCurve = 50;

        // Read the source surface's u-knot structure to compute Greville abscissae.
        var sourceData = evApproximateBSplineSurface(context, { "face" : faceQuery });
        var srcSurf = sourceData.bSplineSurface;
        var srcUDeg = srcSurf.uDegree;
        var srcNumUKnots = size(srcSurf.uKnots);
        var srcNumUCPs = srcNumUKnots - srcUDeg - 1;   // n + 1 control points
        var srcNumVCPs = size(srcSurf.vKnots) - srcSurf.vDegree - 1;

        // Greville abscissae: g[i] = average of knots U[i+1..i+p] for i = 0..n
        var uParams = makeArray(srcNumUCPs);
        for (var i = 0; i < srcNumUCPs; i += 1)
        {
            var sum = 0;
            for (var k = 1; k <= srcUDeg; k += 1)
            {
                sum += srcSurf.uKnots[i + k];
            }
            uParams[i] = sum / srcUDeg;
        }

        // Minimum CPs for cubic given continuity: G0=4 (degree+1), G1=5, G2=6
        // Endpoint derivative constraints occupy 2 extra CPs (second and second-to-last).
        var minVCPs = 4;
        if (continuityType == GeometricContinuity.G1)
            minVCPs = 5;
        else if (continuityType == GeometricContinuity.G2)
            minVCPs = 6;

        if (debugPrint)
        {
            println("[simplify] AUTO source: deg=" ~ srcUDeg ~ "×" ~ srcSurf.vDegree ~
                    "  CPs=" ~ srcNumUCPs ~ "×" ~ srcNumVCPs);
            println("[simplify] AUTO binary search: " ~ minVCPs ~ " to " ~ srcNumVCPs ~ " v-CPs per curve");
        }

        var vCurves = [];
        for (var u in uParams)
        {
            var points = sampleSurfaceIsoCurve(context, faceQuery, "U", u, numSamplesPerCurve);

            var target;
            if (continuityType == GeometricContinuity.G0)
            {
                target = approximationTarget({
                    "positions" : points
                });
            }
            else
            {
                var eps = 1e-5;
                var startPt    = evFaceTangentPlane(context, { "face" : faceQuery, "parameter" : vector(u, 0) }).origin;
                var startPtEps = evFaceTangentPlane(context, { "face" : faceQuery, "parameter" : vector(u, eps) }).origin;
                var startDeriv = (startPtEps - startPt) / eps;

                var endPt    = evFaceTangentPlane(context, { "face" : faceQuery, "parameter" : vector(u, 1) }).origin;
                var endPtEps = evFaceTangentPlane(context, { "face" : faceQuery, "parameter" : vector(u, 1 - eps) }).origin;
                var endDeriv = (endPt - endPtEps) / eps;

                if (continuityType == GeometricContinuity.G1)
                {
                    target = approximationTarget({
                        "positions" : points,
                        "startDerivative" : startDeriv,
                        "endDerivative" : endDeriv
                    });
                }
                else // G2
                {
                    var h = 1e-4;
                    var startPt_h  = evFaceTangentPlane(context, { "face" : faceQuery, "parameter" : vector(u, h) }).origin;
                    var startPt_2h = evFaceTangentPlane(context, { "face" : faceQuery, "parameter" : vector(u, 2 * h) }).origin;
                    var start2ndDeriv = (startPt_2h - 2 * startPt_h + startPt) / (h * h);

                    var endPt_h  = evFaceTangentPlane(context, { "face" : faceQuery, "parameter" : vector(u, 1 - h) }).origin;
                    var endPt_2h = evFaceTangentPlane(context, { "face" : faceQuery, "parameter" : vector(u, 1 - 2 * h) }).origin;
                    var end2ndDeriv = (endPt_2h - 2 * endPt_h + endPt) / (h * h);

                    target = approximationTarget({
                        "positions" : points,
                        "startDerivative" : startDeriv,
                        "start2ndDerivative" : start2ndDeriv,
                        "endDerivative" : endDeriv,
                        "end2ndDerivative" : end2ndDeriv
                    });
                }
            }

            // Binary search for minimum v-CPs that keeps error within tolerance.
            // Baseline: fit at srcNumVCPs (matches source complexity).
            // Then halve the search range until we find the fewest CPs that still pass.
            var lo = minVCPs;
            var hi = srcNumVCPs;

            var bestCurve = approximateSpline(context, {
                "degree" : 3,
                "tolerance" : tolerance,
                "isPeriodic" : false,
                "maxControlPoints" : hi,
                "targets" : [target],
                "interpolateIndices" : [0, numSamplesPerCurve - 1]
            })[0];

            while (lo < hi)
            {
                var mid = floor((lo + hi) / 2);
                var candidate = approximateSpline(context, {
                    "degree" : 3,
                    "tolerance" : tolerance,
                    "isPeriodic" : false,
                    "maxControlPoints" : mid,
                    "targets" : [target],
                    "interpolateIndices" : [0, numSamplesPerCurve - 1]
                })[0];

                if (computeCurveError(context, candidate, faceQuery, u, 50) <= tolerance)
                {
                    bestCurve = candidate;
                    hi = mid;
                }
                else
                {
                    lo = mid + 1;
                }
            }

            vCurves = append(vCurves, bestCurve);
        }

        vCurves = makeCurvesCompatible(context, id + "compat", vCurves);
        var autoResult = createSkinningSurface(context, id + "skin", vCurves, 3, uParams);
        if (debugPrint)
        {
            println("[simplify] AUTO output: deg=" ~ autoResult.uDegree ~ "×" ~ autoResult.vDegree ~
                    "  CPs=" ~ size(autoResult.controlPoints) ~ "×" ~ size(autoResult.controlPoints[0]));
        }
        return autoResult;
    }
    else // MANUAL mode
    {
        // ---- MANUAL MODE: Sample iso-curves, fit with CP count constraint ----

        const numSamplesPerCurve = 50;
        var uParams = [];
        for (var i = 0; i < uCurveCount; i += 1)
        {
            uParams = append(uParams, i / (uCurveCount - 1));
        }

        if (debugPrint)
        {
            println("[simplify] MANUAL iso-curves=" ~ uCurveCount ~ "  maxCPs/curve=" ~ vCurveCount);
        }

        var vCurves = [];
        for (var u in uParams)
        {
            // Sample 50 points along this v-direction iso-curve
            var points = sampleSurfaceIsoCurve(context, faceQuery, "U", u, numSamplesPerCurve);

            var target;
            if (continuityType == GeometricContinuity.G0)
            {
                target = approximationTarget({
                    "positions" : points
                });
            }
            else
            {
                // G1 or G2: compute 1st-order boundary derivatives via finite difference.
                // startDeriv ≈ dS/dv at v=0 (pointing in +v direction)
                // endDeriv   ≈ dS/dv at v=1 (pointing in +v direction)
                var eps = 1e-5;
                var startPt    = evFaceTangentPlane(context, { "face" : faceQuery, "parameter" : vector(u, 0) }).origin;
                var startPtEps = evFaceTangentPlane(context, { "face" : faceQuery, "parameter" : vector(u, eps) }).origin;
                var startDeriv = (startPtEps - startPt) / eps;

                var endPt    = evFaceTangentPlane(context, { "face" : faceQuery, "parameter" : vector(u, 1) }).origin;
                var endPtEps = evFaceTangentPlane(context, { "face" : faceQuery, "parameter" : vector(u, 1 - eps) }).origin;
                var endDeriv = (endPt - endPtEps) / eps;

                if (continuityType == GeometricContinuity.G1)
                {
                    target = approximationTarget({
                        "positions" : points,
                        "startDerivative" : startDeriv,
                        "endDerivative" : endDeriv
                    });
                }
                else // G2
                {
                    // 2nd-order boundary derivatives via one-sided finite difference.
                    // start2ndDeriv ≈ d²S/dv² at v=0 (forward difference)
                    // end2ndDeriv   ≈ d²S/dv² at v=1 (backward difference)
                    var h = 1e-4;
                    var startPt_h  = evFaceTangentPlane(context, { "face" : faceQuery, "parameter" : vector(u, h) }).origin;
                    var startPt_2h = evFaceTangentPlane(context, { "face" : faceQuery, "parameter" : vector(u, 2 * h) }).origin;
                    var start2ndDeriv = (startPt_2h - 2 * startPt_h + startPt) / (h * h);

                    var endPt_h  = evFaceTangentPlane(context, { "face" : faceQuery, "parameter" : vector(u, 1 - h) }).origin;
                    var endPt_2h = evFaceTangentPlane(context, { "face" : faceQuery, "parameter" : vector(u, 1 - 2 * h) }).origin;
                    var end2ndDeriv = (endPt_2h - 2 * endPt_h + endPt) / (h * h);

                    target = approximationTarget({
                        "positions" : points,
                        "startDerivative" : startDeriv,
                        "start2ndDerivative" : start2ndDeriv,
                        "endDerivative" : endDeriv,
                        "end2ndDerivative" : end2ndDeriv
                    });
                }
            }

            var curve = approximateSpline(context, {
                "degree" : 3,
                "tolerance" : tolerance,
                "isPeriodic" : false,
                "maxControlPoints" : vCurveCount,
                "targets" : [target],
                "interpolateIndices" : [0, numSamplesPerCurve - 1]
            })[0];

            vCurves = append(vCurves, curve);
        }

        // Make all v-direction curves compatible (same degree and knots),
        // then skin them into a surface in the u-direction with cubic degree.
        vCurves = makeCurvesCompatible(context, id + "compat", vCurves);
        var manualResult = createSkinningSurface(context, id + "skin", vCurves, 3, uParams);
        if (debugPrint)
        {
            println("[simplify] MANUAL output: deg=" ~ manualResult.uDegree ~ "×" ~ manualResult.vDegree ~
                    "  CPs=" ~ size(manualResult.controlPoints) ~ "×" ~ size(manualResult.controlPoints[0]));
        }
        return manualResult;
    }
}
