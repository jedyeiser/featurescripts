FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// IMPORT: xSectReferencePoints.fs
import(path : "08fddb59786b6bfee020ee05", version : "61f33606f9384a2757e0729b");

// IMPORT: xSectUtils.fs
import(path : "c2c3edd39b85fde5e6062533", version : "e28c1b2ccec93ed1e9fe271b");

/**
 * This function takes a query of multiple edges (must be G1 continuous)
 * Takes an FCP location query
 * Takes an ACP location query
 *
 * Creates a path through query edges
 * Finds curve point at FCP
 * Finds curve point at ACP
 *
 * MRS = (FCP + ACP)/2
 *
 * Forebody minimum = point of minimum s of baseline curves starting at MRS and moving towards FCP. Record point(x, y, z)
 * Aftbody minimum = minimum z point of baseline curves starting at MRS and moving towards ACP. Record point (x, y, z).
 *
 * baseline_bottom_line = line that connects forebody and aftbody minima.
 *
 * Camber height = maximum height of baseline from baseline_bottom_line between forebody and aftbody minima.
 *
 * Find rocker points - or the outermost inflection points along our baseline edges. Thes inflection points might be at junctions between edges.
 *
 * Find tangent lines to baseline at inflection points (FB/Aftbody). Measure minimum distance between these lines and the curve intersection point at FCP/ACP.
 * These are our FCP/ACP heights.
 *
 * Function just runs edting logic. Logic can be used elsewhere.
 *
 */


// ============================================================================
// PRIVATE HELPERS
// ============================================================================

/**
 * Round a plain number to 2 decimal places.
 */
function round2(x is number) returns number
{
    return round(x * 100) / 100;
}

/**
 * Build an ordered chain of BSpline segments from a query of connected edges.
 * Returns [{bspline, reversed}, ...] in connected traversal order.
 * Uses evEdgeCurvatures (parameters 0 and 1) for endpoint detection — avoids
 * evaluateSpline at the knot boundary which can throw "outside knot vector".
 */
function buildEdgeChain(context is Context, edgesQ is Query) returns array
{
    var edges = evaluateQuery(context, edgesQ);
    if (size(edges) == 0)
    {
        return [];
    }

    // Gather bspline + edge endpoints for each edge
    var edgeData = [];
    for (var edge in edges)
    {
        var bspline = evApproximateBSplineCurve(context, {
            "edge"      : edge,
            "tolerance" : 1e-5
        });
        var curvStart = evEdgeCurvatures(context, { "edge" : edge, "parameters" : [0.0] });
        var curvEnd   = evEdgeCurvatures(context, { "edge" : edge, "parameters" : [1.0] });
        edgeData = append(edgeData, {
            "bspline"  : bspline,
            "startPt"  : curvStart[0].frame.origin,
            "endPt"    : curvEnd[0].frame.origin
        });
    }

    if (size(edgeData) == 1)
    {
        return [{ "bspline" : edgeData[0].bspline, "reversed" : false }];
    }

    // Greedy chain assembly using edge endpoints
    var used      = makeArray(size(edgeData), false);
    var chain     = [{ "bspline" : edgeData[0].bspline, "reversed" : false }];
    used[0]       = true;
    var chainEndPt = edgeData[0].endPt;

    for (var iter = 0; iter < size(edgeData) - 1; iter += 1)
    {
        var found = false;
        for (var j = 0; j < size(edgeData); j += 1)
        {
            if (used[j])
            {
                continue;
            }

            if (norm(edgeData[j].startPt - chainEndPt) < GEOM_TOL)
            {
                chain      = append(chain, { "bspline" : edgeData[j].bspline, "reversed" : false });
                used[j]    = true;
                chainEndPt = edgeData[j].endPt;
                found      = true;
                break;
            }
            else if (norm(edgeData[j].endPt - chainEndPt) < GEOM_TOL)
            {
                chain      = append(chain, { "bspline" : edgeData[j].bspline, "reversed" : true });
                used[j]    = true;
                chainEndPt = edgeData[j].startPt;
                found      = true;
                break;
            }
        }
        if (!found)
        {
            break;
        }
    }

    return chain;
}

/**
 * Uniformly sample n points distributed across the chain.
 * Returns [{pt, curveIdx, u}, ...].
 */
function sampleChain(chain is array, n is number) returns array
{
    if (size(chain) == 0)
    {
        return [];
    }

    var numCurves       = size(chain);
    var samplesPerCurve = max(3, ceil(n / numCurves));
    var samples         = [];

    for (var ci = 0; ci < numCurves; ci += 1)
    {
        var bspline  = chain[ci].bspline;
        var reversed = chain[ci].reversed;
        var pRange   = getBSplineParamRange(bspline);
        var uMin     = pRange.uMin;
        var uMax     = pRange.uMax;

        // Always build params in ascending order — evaluateSpline requires ascending.
        // For reversed curves, reverse the resulting points AFTER evaluation so samples
        // follow chain direction (needed for correct chord-length arc length sums).
        // Midpoint sampling (i + 0.5) / n keeps all params strictly inside (uMin, uMax).
        var params = [];
        for (var i = 0; i < samplesPerCurve; i += 1)
        {
            var t = (i + 0.5) / samplesPerCurve;
            params = append(params, uMin + t * (uMax - uMin));
        }

        var evalResult = evaluateSpline({
            "spline"     : bspline,
            "parameters" : params
        });
        var pts = evalResult[0];

        // Reverse point/param arrays for reversed-traversal curves
        if (reversed)
        {
            var revPts    = [];
            var revParams = [];
            for (var ri = size(pts) - 1; ri >= 0; ri -= 1)
            {
                revPts    = append(revPts,    pts[ri]);
                revParams = append(revParams, params[ri]);
            }
            pts    = revPts;
            params = revParams;
        }

        for (var i = 0; i < size(pts); i += 1)
        {
            samples = append(samples, {
                "pt"       : pts[i],
                "curveIdx" : ci,
                "u"        : params[i]
            });
        }
    }

    return samples;
}

/**
 * Return the index of the sample whose world X is closest to x.
 */
function findSampleAtX(samples is array, x) returns number
{
    var bestIdx  = 0;
    var bestDist = undefined;

    for (var i = 0; i < size(samples); i += 1)
    {
        var d = abs(samples[i].pt[0] - x);
        if (bestDist == undefined || d < bestDist)
        {
            bestDist = d;
            bestIdx  = i;
        }
    }

    return bestIdx;
}

/**
 * Return {idx, pt} of the sample with minimum Z in [xLow, xHigh].
 * Returns {idx: -1, pt: undefined} if no samples fall in range.
 */
function findMinZInXRange(samples is array, xLow, xHigh) returns map
{
    var bestIdx = -1;
    var bestZ   = undefined;

    for (var i = 0; i < size(samples); i += 1)
    {
        var px = samples[i].pt[0];
        if (px >= xLow && px <= xHigh)
        {
            var pz = samples[i].pt[2];
            if (bestZ == undefined || pz < bestZ)
            {
                bestZ   = pz;
                bestIdx = i;
            }
        }
    }

    if (bestIdx < 0)
    {
        return { "idx" : -1, "pt" : undefined };
    }

    return { "idx" : bestIdx, "pt" : samples[bestIdx].pt };
}

/**
 * Perpendicular distance from pt to an infinite line (lineOrigin, lineDir).
 * lineDir must be a unit direction vector.
 */
function perpDistToLine(pt is Vector, lineOrigin is Vector, lineDir is Vector) returns ValueWithUnits
{
    var diff    = pt - lineOrigin;
    var proj    = dot(diff, lineDir) * lineDir;
    return norm(diff - proj);
}

/**
 * Find inflection points (curvature sign changes) on a single BSpline.
 * Returns [{pt, u}, ...].
 */
function findInflectionsOnCurve(bspline is BSplineCurve) returns array
{
    var pRange   = getBSplineParamRange(bspline);
    var uMin     = pRange.uMin;
    var uMax     = pRange.uMax;
    var nCPs     = size(bspline.controlPoints);
    var numSpans = max(1, nCPs - bspline.degree);
    var nSamples = max(20, 15 * numSpans);

    // Midpoint sampling: t = (i + 0.5) / nSamples → strictly inside (uMin, uMax).
    var params = [];
    for (var i = 0; i < nSamples; i += 1)
    {
        var t = (i + 0.5) / nSamples;
        params = append(params, uMin + t * (uMax - uMin));
    }

    // Batch evaluate 2nd derivatives
    var derivsResult = evaluateSpline({
        "spline"       : bspline,
        "parameters"   : params,
        "nDerivatives" : 2
    });
    var d1s = derivsResult[1];
    var d2s = derivsResult[2];

    var inflections = [];
    var prevSign    = 0;
    var prevU       = params[0];

    for (var i = 0; i < nSamples; i += 1)
    {
        var d1 = d1s[i];
        var d2 = d2s[i];
        // Y-component of d1 × d2 captures curvature sign for XZ-plane curves
        var crossY  = d1[0] * d2[2] - d1[2] * d2[0];
        var signNow = (crossY > 0 * meter * meter) ? 1 : ((crossY < 0 * meter * meter) ? -1 : 0);

        if (i > 0 && signNow != 0 && prevSign != 0 && signNow != prevSign)
        {
            // Bisect to refine inflection parameter
            var ua   = prevU;
            var ub   = params[i];
            var uInf = (ua + ub) / 2;

            for (var bisIter = 0; bisIter < 30; bisIter += 1)
            {
                uInf = (ua + ub) / 2;
                var bisResult = evaluateSpline({
                    "spline"       : bspline,
                    "parameters"   : [uInf],
                    "nDerivatives" : 2
                });
                var bd1     = bisResult[1][0];
                var bd2     = bisResult[2][0];
                var bisCY   = bd1[0] * bd2[2] - bd1[2] * bd2[0];
                var bisSign = (bisCY > 0 * meter * meter) ? 1 : -1;

                if (bisSign == prevSign)
                {
                    ua = uInf;
                }
                else
                {
                    ub = uInf;
                }

                if (abs(ub - ua) < 1e-8)
                {
                    break;
                }
            }

            var infEval = evaluateSpline({
                "spline"     : bspline,
                "parameters" : [uInf]
            });

            inflections = append(inflections, {
                "pt" : infEval[0][0],
                "u"  : uInf
            });
        }

        if (signNow != 0)
        {
            prevSign = signNow;
        }
        prevU = params[i];
    }

    return inflections;
}

/**
 * Approximate arc length between two sample indices by summing chord lengths.
 */
function approximateChainArcLength(samples is array, idxA is number, idxB is number) returns ValueWithUnits
{
    var length = 0 * meter;
    var iStart = min(idxA, idxB);
    var iEnd   = max(idxA, idxB);

    for (var i = iStart; i < iEnd; i += 1)
    {
        length += norm(samples[i + 1].pt - samples[i].pt);
    }

    return length;
}

/**
 * Format a 3D position vector as a millimeter string (2 decimal places).
 */
function formatVec(v is Vector) returns string
{
    var xMM = round2(v[0] / millimeter);
    var yMM = round2(v[1] / millimeter);
    var zMM = round2(v[2] / millimeter);
    return "(" ~ toString(xMM) ~ ", " ~ toString(yMM) ~ ", " ~ toString(zMM) ~ ") mm";
}

/**
 * Format an infinite line (origin, unit direction) as a compact string.
 */
function formatLine(origin is Vector, dir is Vector) returns string
{
    return "org:" ~ formatVec(origin) ~ " dir:("
        ~ toString(round2(dir[0])) ~ ","
        ~ toString(round2(dir[1])) ~ ","
        ~ toString(round2(dir[2])) ~ ")";
}


// ============================================================================
// PRIMARY EXPORT
// ============================================================================

/**
 * Analyze baseline geometry given a set of G1-continuous edges and reference points.
 *
 * @param context
 * @param baselineEdgesQ {Query} : G1-continuous edges forming the baseline
 * @param fcpQ {Query} : Forebody contact point (vertex, mate connector, or planar face)
 * @param acpQ {Query} : Aftbody contact point
 * @returns {map} : Geometry results, or undefined if inputs are invalid
 */
export function analyzeBaselineGeometry(context is Context,
    baselineEdgesQ is Query, fcpQ is Query, acpQ is Query) returns map
{
    // Step 1: Build ordered BSpline chain from edge query
    var chain = buildEdgeChain(context, baselineEdgesQ);
    if (size(chain) == 0)
    {
        return undefined;
    }

    // Step 2: Uniform sample across entire chain (200 points)
    var samples = sampleChain(chain, 200);
    if (size(samples) == 0)
    {
        return undefined;
    }

    // Step 3: Resolve FCP / ACP world X coordinates
    var fcpX = resolveReferencePointX(context, fcpQ, baselineEdgesQ);
    var acpX = resolveReferencePointX(context, acpQ, baselineEdgesQ);
    if (fcpX == undefined || acpX == undefined)
    {
        return undefined;
    }

    var mrsX    = (fcpX + acpX) / 2;
    var fcpIdx  = findSampleAtX(samples, fcpX);
    var acpIdx  = findSampleAtX(samples, acpX);
    var mrsIdx  = findSampleAtX(samples, mrsX);
    var fcpPt   = samples[fcpIdx].pt;
    var acpPt   = samples[acpIdx].pt;
    var mrsPt   = samples[mrsIdx].pt;

    // Step 4: FB / AB minimum-Z points in each half
    var fbXLow  = (fcpX < mrsX) ? fcpX : mrsX;
    var fbXHigh = (fcpX > mrsX) ? fcpX : mrsX;
    var abXLow  = (mrsX < acpX) ? mrsX : acpX;
    var abXHigh = (mrsX > acpX) ? mrsX : acpX;

    var fbMinResult = findMinZInXRange(samples, fbXLow, fbXHigh);
    var abMinResult = findMinZInXRange(samples, abXLow, abXHigh);
    if (fbMinResult.idx < 0 || abMinResult.idx < 0)
    {
        return undefined;
    }

    var fbMinPt = fbMinResult.pt;
    var abMinPt = abMinResult.pt;

    // Step 5: Camber height — max perpendicular distance from FB-min→AB-min chord
    var chordDir    = normalize(abMinPt - fbMinPt);
    var chordOrigin = fbMinPt;
    var xChordLow   = (fbMinPt[0] < abMinPt[0]) ? fbMinPt[0] : abMinPt[0];
    var xChordHigh  = (fbMinPt[0] > abMinPt[0]) ? fbMinPt[0] : abMinPt[0];

    var maxCamberPt  = fbMinPt;
    var camberHeight = 0 * meter;

    for (var i = 0; i < size(samples); i += 1)
    {
        var px = samples[i].pt[0];
        if (px >= xChordLow && px <= xChordHigh)
        {
            var d = perpDistToLine(samples[i].pt, chordOrigin, chordDir);
            if (d > camberHeight)
            {
                camberHeight = d;
                maxCamberPt  = samples[i].pt;
            }
        }
    }

    // Step 6: Find all inflection points across the chain
    var allInflections = [];
    for (var ci = 0; ci < size(chain); ci += 1)
    {
        var curveInfs = findInflectionsOnCurve(chain[ci].bspline);
        for (var i in curveInfs)
        {
            allInflections = append(allInflections, {
                "pt"      : i.pt,
                "u"       : i.u,
                "bspline" : chain[ci].bspline
            });
        }
    }

    // Step 7: Select outermost inflection in each half (FRCP / ARCP)
    var frcpPt      = undefined;
    var frcpU       = undefined;
    var frcpBspline = undefined;
    var frcpDist    = undefined;

    var arcpPt      = undefined;
    var arcpU       = undefined;
    var arcpBspline = undefined;
    var arcpDist    = undefined;

    for (var i in allInflections)
    {
        var px = inf.pt[0];

        // Forebody region: between FCP and MRS
        if (px >= fbXLow && px <= fbXHigh)
        {
            var fbD = abs(px - mrsX);
            if (frcpDist == undefined || fbD > frcpDist)
            {
                frcpPt      = i.pt;
                frcpU       = i.u;
                frcpBspline = i.bspline;
                frcpDist    = fbD;
            }
        }

        // Aftbody region: between MRS and ACP
        if (px >= abXLow && px <= abXHigh)
        {
            var abD = abs(px - mrsX);
            if (arcpDist == undefined || abD > arcpDist)
            {
                arcpPt      = inf.pt;
                arcpU       = inf.u;
                arcpBspline = inf.bspline;
                arcpDist    = abD;
            }
        }
    }

    // Assemble core result
    var result = {
        "fcp_pt"        : fcpPt,
        "acp_pt"        : acpPt,
        "mrs_pt"        : mrsPt,
        "fb_min_pt"     : fbMinPt,
        "ab_min_pt"     : abMinPt,
        "max_camber_pt" : maxCamberPt,
        "camber_height" : camberHeight
    };

    // Steps 8–10: FRCP tangent, arc length, height
    if (frcpPt != undefined)
    {
        var frcpD1 = evaluateSpline({
            "spline"       : frcpBspline,
            "parameters"   : [frcpU],
            "nDerivatives" : 1
        });
        var frcpTangent = frcpD1[1][0];
        if (norm(frcpTangent) > 1e-10 * meter)
        {
            var frcpDir = normalize(frcpTangent);
            var frcpIdx = findSampleAtX(samples, frcpPt[0]);
            result.frcp_pt  = frcpPt;
            result.frcp_dir = frcpDir;
            result.frcpl    = approximateChainArcLength(samples, fcpIdx, frcpIdx);
            result.fcph     = perpDistToLine(fcpPt, frcpPt, frcpDir);
        }
    }

    // Steps 8–10: ARCP tangent, arc length, height
    if (arcpPt != undefined)
    {
        var arcpD1 = evaluateSpline({
            "spline"       : arcpBspline,
            "parameters"   : [arcpU],
            "nDerivatives" : 1
        });
        var arcpTangent = arcpD1[1][0];
        if (norm(arcpTangent) > 1e-10 * meter)
        {
            var arcpDir = normalize(arcpTangent);
            var arcpIdx = findSampleAtX(samples, arcpPt[0]);
            result.arcp_pt  = arcpPt;
            result.arcp_dir = arcpDir;
            result.arcpl    = approximateChainArcLength(samples, acpIdx, arcpIdx);
            result.acph     = perpDistToLine(acpPt, arcpPt, arcpDir);
        }
    }

    return result;
}


// ============================================================================
// EDITING LOGIC
// ============================================================================

export function analyzeBaselineEditLogic(context is Context, id is Id, oldDefinition is map,
   definition is map, isCreating is boolean, specifiedParameters is map,
   hiddenBodies is Query, clickedButton is string) returns map
{
    if (clickedButton != "recalculate" && !(!oldDefinition.recalculate && definition.recalculate))
    {
        return definition;
    }

    var result = analyzeBaselineGeometry(context, definition.baselineEdges, definition.fcpQ, definition.acpQ);
    if (result == undefined)
    {
        return definition;
    }

    definition.fbMinString           = formatVec(result.fb_min_pt);
    definition.abMinString           = formatVec(result.ab_min_pt);
    definition.maxCamberHeightString = formatVec(result.max_camber_pt);
    definition.camberHeight          = result.camber_height;

    if (result.frcp_pt != undefined)
    {
        definition.frcp     = formatVec(result.frcp_pt);
        definition.frcpl    = result.frcpl;
        definition.frcpLine = formatLine(result.frcp_pt, result.frcp_dir);
        definition.fcph     = result.fcph;
    }

    if (result.arcp_pt != undefined)
    {
        definition.arcp     = formatVec(result.arcp_pt);
        definition.arcpl    = result.arcpl;
        definition.arcpLine = formatLine(result.arcp_pt, result.arcp_dir);
        definition.acph     = result.acph;
    }

    return definition;
}


// ============================================================================
// FEATURE DEFINITION
// ============================================================================

annotation { "Feature Type Name" : "Analze baseline",
"Feature Type Description" : "Takes input edge queries, fcp and acp locations and generates ",
"Editing Logic Function" : "analyzeBaselineEditLogic"}
export const analyzeBaseline = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Baseline edges", "Filter" : EntityType.EDGE, "Description" : "Edges that make up the baseline to be analyzed. must be G1 continuous"}
        definition.baselineEdges is Query;

        annotation { "Name" : "FCP", "Filter" : (EntityType.FACE && GeometryType.PLANE) || GeometryType.PLANE || BodyType.MATE_CONNECTOR || EntityType.VERTEX, "MaxNumberOfPicks" : 1 }
        definition.fcpQ is Query;

        annotation { "Name" : "ACP", "Filter" : (EntityType.FACE && GeometryType.PLANE) || GeometryType.PLANE || BodyType.MATE_CONNECTOR || EntityType.VERTEX, "MaxNumberOfPicks" : 1 }
        definition.acpQ is Query;

        annotation { "Group Name" : "Calculated data", "Collapsed By Default" : true }
        {
            annotation { "Name" : "FB min", "Description" : "String of vector in mm for minimum fb point", "UIHint" : UIHint.READ_ONLY }// string vector
            definition.fbMinString is string;

            annotation { "Name" : "AB min", "Description" : "String of vector in mm for minimum ab point", "UIHint" : UIHint.READ_ONLY  }// string vector
            definition.abMinString is string;

            annotation { "Name" : "Max camber point", "Description" : "String of vector in mm for max camber point", "UIHint" : UIHint.READ_ONLY  }// string vector
            definition.maxCamberHeightString is string;

            annotation { "Name" : "Camber height" }
            isLength(definition.camberHeight, LENGTH_BOUNDS);

            annotation { "Name" : "FRCP", "Description" : "Forebody rocker contact point", "UIHint" : UIHint.READ_ONLY  } // string vector
            definition.frcp is string;

            annotation { "Name" : "FRCPL", "Description" : "Forebody rocker contact point length. Distance between fcp and frcp", "UIHint" : UIHint.READ_ONLY  }
            isLength(definition.frcpl, LENGTH_BOUNDS);

            annotation { "Name" : "FRCP tangent line", "Description" : "Forebody rocker tangent line", "UIHint" : UIHint.READ_ONLY  } // string description of line (origin on baseline, slope)
            definition.frcpLine is string;

            annotation { "Name" : "FCPH", "Description" : "Height of FCP when baseline is weighted. Distance between FCP point and forebody rocker tangent line", "UIHint" : UIHint.READ_ONLY  }
            isLength(definition.fcph, LENGTH_BOUNDS);

            annotation { "Name" : "ARCP", "Description" : "Aftbody rocker contact point", "UIHint" : UIHint.READ_ONLY  } // string vector
            definition.arcp is string;

            annotation { "Name" : "ARCPL", "Description" : "Aftebody rocker contact point length. Distance between fcp and frcp", "UIHint" : UIHint.READ_ONLY  }
            isLength(definition.arcpl, LENGTH_BOUNDS);

            annotation { "Name" : "ARCP tangent line", "Description" : "Aftbody rocker tangent line", "UIHint" : UIHint.READ_ONLY  } // string description of line (origin on baseline, slope)
            definition.arcpLine is string;

            annotation { "Name" : "ACPH", "Description" : "Height of ACP when baseline is weighted. Distance between ACP point and aftbody rocker tangent line", "UIHint" : UIHint.READ_ONLY  }
            isLength(definition.acph, LENGTH_BOUNDS);


        }

        annotation { "Name" : "Recalculate" }
       isButton(definition.recalculate);


    }
    {
        // Feature body intentionally empty — all computation in editing logic
    });
