FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

// IMPORT: xSectReferencePoints.fs
import(path : "08fddb59786b6bfee020ee05", version : "16a3259bfc59aee2fbc2c1dd");

// IMPORT: xSectUtils.fs
import(path : "c2c3edd39b85fde5e6062533", version : "9cf03ed1102fc8e100e8e3f1");

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
 * Forebody minimum = point of minimum z of baseline curves starting at MRS and moving towards FCP. Record point(x, y, z)
 * Aftbody minimum = minimum z point of baseline curves starting at MRS and moving towards ACP. Record point (x, y, z).
 *
 * baseline_bottom_line = line that connects forebody and aftbody minima.
 *
 * Camber height = maximum height of baseline from baseline_bottom_line between forebody and aftbody minima.
 *
 * Rocker contact points (FRCP / ARCP): the contact point when the ski is weighted (camber
 * straightened). Definition (user, 2026-09-28): on each half, the baseline INFLECTION point
 * closest to that half's lowest point (forebody / aftbody minimum), on the side of the
 * minimum towards MRS. On a Generate baseline curve this is the camber/rocker join.
 *
 * Find tangent lines to baseline at inflection points (FB/Aftbody). Measure minimum distance between these lines and the curve intersection point at FCP/ACP.
 * These are our FCP/ACP heights.
 *
 * Lengths (all |dx| in world X; checked 2026-09-28 on RD 20TAC / 20FOU: |dx| is within
 * 0.09 mm of the arc length for every one of them):
 *   FRCPL / ARCPL   : FCP -> FRCP, ACP -> ARCP
 *   FB_Roll/AB_Roll : forebody / aftbody minimum -> FRCP / ARCP
 * MCl: position of the max camber point -- world X, and arc length along the baseline from
 *      MRS (positive towards FCP, i.e. the tip).
 *
 * Direction: FCP may lie on either side of ACP in world X; every range below is min/max.
 *
 * Function just runs editing logic. Logic can be used elsewhere.
 */


// ============================================================================
// PRIVATE HELPERS
// ============================================================================

/**
 * Round a plain (unitless) number to 2 decimal places.
 * Used for display formatting in editing-logic output fields.
 *
 * @param x {number} : Value to round
 * @returns {number} : Value rounded to nearest 0.01
 */
function round2(x is number) returns number
{
    return round(x * 100) / 100;
}

/**
 * Build an ordered, direction-consistent chain from a set of G1-connected edges.
 *
 * Performs greedy traversal: starts from the first edge, then at each step finds the
 * unused edge whose endpoint matches the current chain tip (within GEOM_TOL). Edges
 * are marked as reversed if they must be traversed end->start to maintain chain direction.
 *
 * @param context {Context}
 * @param edgesQ {Query} : Query returning a set of connected (G1 continuous) edges
 * @returns {array} : Array of { edge: Query, reversed: boolean } in traversal order.
 *                    Returns [] if no edges match the query.
 */
function buildEdgeChain(context is Context, edgesQ is Query) returns array
{
    var edges = evaluateQuery(context, edgesQ);
    if (size(edges) == 0)
    {
        return [];
    }

    // Gather edge + endpoints for each edge
    var edgeData = [];
    for (var edge in edges)
    {
        var curvStart = evEdgeCurvatures(context, { "edge" : edge, "parameters" : [0.0] });
        var curvEnd   = evEdgeCurvatures(context, { "edge" : edge, "parameters" : [1.0] });
        edgeData = append(edgeData, {
            "edge"    : edge,
            "startPt" : curvStart[0].frame.origin,
            "endPt"   : curvEnd[0].frame.origin
        });
    }

    if (size(edgeData) == 1)
    {
        return [{ "edge" : edgeData[0].edge, "reversed" : false }];
    }

    // Greedy chain assembly using edge endpoints
    var used         = makeArray(size(edgeData), false);
    var chain        = [{ "edge" : edgeData[0].edge, "reversed" : false }];
    used[0]          = true;
    var chainEndPt   = edgeData[0].endPt;
    var chainStartPt = edgeData[0].startPt;

    // Forward pass: extend from the chain's current end
    for (var iter = 0; iter < size(edgeData) - 1; iter += 1)
    {
        var found = false;
        for (var j = 0; j < size(edgeData); j += 1)
        {
            if (used[j])
                continue;

            if (norm(edgeData[j].startPt - chainEndPt) < GEOM_TOL)
            {
                chain      = append(chain, { "edge" : edgeData[j].edge, "reversed" : false });
                used[j]    = true;
                chainEndPt = edgeData[j].endPt;
                found      = true;
                break;
            }
            else if (norm(edgeData[j].endPt - chainEndPt) < GEOM_TOL)
            {
                chain      = append(chain, { "edge" : edgeData[j].edge, "reversed" : true });
                used[j]    = true;
                chainEndPt = edgeData[j].startPt;
                found      = true;
                break;
            }
        }
        if (!found)
            break;
    }

    // Backward pass: extend from the chain's current start (catches edges like the
    // forebody rocker whose connection point is the *start* of the seed edge, not the end)
    var backSegments = [];
    for (var iter = 0; iter < size(edgeData) - 1; iter += 1)
    {
        var found = false;
        for (var j = 0; j < size(edgeData); j += 1)
        {
            if (used[j])
                continue;

            if (norm(edgeData[j].endPt - chainStartPt) < GEOM_TOL)
            {
                backSegments = append(backSegments, { "edge" : edgeData[j].edge, "reversed" : false });
                used[j]      = true;
                chainStartPt = edgeData[j].startPt;
                found        = true;
                break;
            }
            else if (norm(edgeData[j].startPt - chainStartPt) < GEOM_TOL)
            {
                backSegments = append(backSegments, { "edge" : edgeData[j].edge, "reversed" : true });
                used[j]      = true;
                chainStartPt = edgeData[j].endPt;
                found        = true;
                break;
            }
        }
        if (!found)
            break;
    }

    // Prepend back segments in reverse discovery order so the chain runs tip-to-tip
    if (size(backSegments) > 0)
    {
        var fullChain = [];
        for (var i = size(backSegments) - 1; i >= 0; i -= 1)
            fullChain = append(fullChain, backSegments[i]);
        for (var i = 0; i < size(chain); i += 1)
            fullChain = append(fullChain, chain[i]);
        chain = fullChain;
    }

    return chain;
}

/**
 * Sample n points uniformly distributed across a multi-edge chain.
 *
 * Uses midpoint sampling within each edge's native [0,1] parameterization
 * (avoids B-spline knot-range issues). Each edge gets `ceil(n / numCurves)` samples.
 * Reversed edges have their sample order flipped to maintain chain direction.
 *
 * @param context {Context}
 * @param chain {array} : Edge chain from buildEdgeChain -- [{ edge, reversed }, ...]
 * @param n {number} : Approximate total number of samples (actual may be slightly higher)
 * @returns {array} : Array of { pt: Vector, curveIdx: number, u: number }
 *                    where curveIdx is the edge index in chain and u is the native parameter.
 */
function sampleChain(context is Context, chain is array, n is number) returns array
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
        var edge     = chain[ci].edge;
        var reversed = chain[ci].reversed;

        // Midpoint sampling: strictly inside (0, 1)
        var params = [];
        for (var i = 0; i < samplesPerCurve; i += 1)
        {
            params = append(params, (i + 0.5) / samplesPerCurve);
        }

        var curvatures = evEdgeCurvatures(context, { "edge" : edge, "parameters" : params });

        var pts = [];
        for (var c in curvatures)
        {
            pts = append(pts, c.frame.origin);
        }

        // For reversed curves, reverse points so samples follow chain direction
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
 * Return {idx, pt, curveIdx, u} of the sample with minimum Z in [xLow, xHigh].
 * Returns {idx: -1, pt: undefined} if no samples fall in range.
 */
function findMinZInXRange(samples is array, xLow, xHigh) returns map
{
    var bestIdx      = -1;
    var bestZ        = undefined;
    var bestCurveIdx = -1;
    var bestU        = undefined;

    for (var i = 0; i < size(samples); i += 1)
    {
        var px = samples[i].pt[0];
        if (px >= xLow && px <= xHigh)
        {
            var pz = samples[i].pt[2];
            if (bestZ == undefined || pz < bestZ)
            {
                bestZ        = pz;
                bestIdx      = i;
                bestCurveIdx = samples[i].curveIdx;
                bestU        = samples[i].u;
            }
        }
    }

    if (bestIdx < 0)
    {
        return { "idx" : -1, "pt" : undefined };
    }

    return { "idx" : bestIdx, "pt" : samples[bestIdx].pt, "curveIdx" : bestCurveIdx, "u" : bestU };
}

/**
 * Ternary search for the minimum-Z point on an edge within a native [0,1] parameter bracket.
 * Uses evEdgeCurvatures so it works on any edge type without a BSpline handle.
 */
function refineMinZOnEdge(context is Context, edge is Query, uLo is number, uHi is number) returns Vector
{
    var lo        = uLo;
    var hi        = uHi;
    var prevBestZ = undefined;

    for (var iter = 0; iter < 60; iter += 1)
    {
        const m1 = lo + (hi - lo) / 3;
        const m2 = hi - (hi - lo) / 3;

        const z1 = evEdgeCurvatures(context, { "edge" : edge, "parameters" : [m1] })[0].frame.origin[2];
        const z2 = evEdgeCurvatures(context, { "edge" : edge, "parameters" : [m2] })[0].frame.origin[2];

        if (z1 < z2)
            hi = m2;
        else
            lo = m1;

        const bestZ = (z1 < z2) ? z1 : z2;
        if (prevBestZ != undefined && abs(bestZ - prevBestZ) < 1e-9 * millimeter)
            break;

        prevBestZ = bestZ;
    }

    return evEdgeCurvatures(context, { "edge" : edge, "parameters" : [(lo + hi) / 2] })[0].frame.origin;
}

/**
 * Perpendicular distance from pt to an infinite line (lineOrigin, lineDir).
 * lineDir must be a unit direction vector.
 */
function perpDistToLine(pt is Vector, lineOrigin is Vector, lineDir is Vector) returns ValueWithUnits
{
    var diff = pt - lineOrigin;
    var proj = dot(diff, lineDir) * lineDir;
    return norm(diff - proj);
}

/**
 * Find inflection points (curvature sign changes) on a single edge.
 * Uses evEdgeCurvatures with native [0,1] parameterization and finite
 * differences for curvature sign -- no B-spline evaluation required.
 * Returns [{pt, u, edge}, ...].
 */
function findInflectionsOnCurve(context is Context, edge is Query) returns array
{
    var nSamples = 40;
    var params   = [];
    for (var i = 0; i < nSamples; i += 1)
    {
        params = append(params, (i + 0.5) / nSamples);
    }

    var curvatures = evEdgeCurvatures(context, { "edge" : edge, "parameters" : params });

    var pts = [];
    for (var c in curvatures)
    {
        pts = append(pts, c.frame.origin);
    }

    // Curvature sign via finite differences.
    // Y-component of d1 x d2 captures curvature sign for XZ-plane curves:
    //   crossY = d1[0]*d2[2] - d1[2]*d2[0]
    // d1 = central first difference, d2 = second difference (both in meters).
    var signs = makeArray(nSamples, 0);
    for (var i = 1; i < nSamples - 1; i += 1)
    {
        var d1     = pts[i + 1] - pts[i - 1];
        var d2     = pts[i + 1] - 2 * pts[i] + pts[i - 1];
        var crossY = d1[0] * d2[2] - d1[2] * d2[0];
        signs[i]   = (crossY > 0 * meter * meter) ? 1 : ((crossY < 0 * meter * meter) ? -1 : 0);
    }
    signs[0]            = signs[1];
    signs[nSamples - 1] = signs[nSamples - 2];

    var inflections = [];
    for (var i = 1; i < nSamples; i += 1)
    {
        if (signs[i] != 0 && signs[i - 1] != 0 && signs[i] != signs[i - 1])
        {
            var uInf    = (params[i - 1] + params[i]) / 2;
            var infCurv = evEdgeCurvatures(context, { "edge" : edge, "parameters" : [uInf] });
            inflections = append(inflections, {
                "pt"   : infCurv[0].frame.origin,
                "u"    : uInf,
                "edge" : edge
            });
        }
    }

    return inflections;
}

/**
 * Format a 3D position vector as a millimeter string (2 decimal places).
 */
export function formatVec(v is Vector) returns string
{
    var xMM = round2(v[0] / millimeter);
    var yMM = round2(v[1] / millimeter);
    var zMM = round2(v[2] / millimeter);
    return "(" ~ toString(xMM) ~ ", " ~ toString(yMM) ~ ", " ~ toString(zMM) ~ ") mm";
}

/**
 * Format an infinite line (origin, unit direction) as a compact string.
 */
export function formatLine(origin is Vector, dir is Vector) returns string
{
    return "org:" ~ formatVec(origin) ~ " dir:("
        ~ toString(round2(dir[0])) ~ ","
        ~ toString(round2(dir[1])) ~ ","
        ~ toString(round2(dir[2])) ~ ")";
}


/**
 * Rocker contact point of one half (FRCP or ARCP): the inflection closest to that half's
 * minimum point, on the side of the minimum towards MRS. Only inflections inside the half
 * [xHalfLow, xHalfHigh] count. Returns undefined when there is none.
 */
function selectRockerContact(inflections is array, minPt is Vector, mrsX, xHalfLow, xHalfHigh)
{
    const xLo = min(minPt[0], mrsX);
    const xHi = max(minPt[0], mrsX);
    var best     = undefined;
    var bestDist = undefined;
    for (var infl in inflections)
    {
        const px = infl.pt[0];
        if (px >= xHalfLow && px <= xHalfHigh && px >= xLo && px <= xHi)
        {
            const d = abs(px - minPt[0]);
            if (bestDist == undefined || d < bestDist)
            {
                best     = infl;
                bestDist = d;
            }
        }
    }
    return best;
}

/**
 * Arc length along the chain to native parameter u of chain edge ci (edge parameters of
 * evEdge* calls are arc-length parameters, so the partial length is u * edge length).
 */
function chainArcLength(chain is array, lengths is array, ci is number, u is number) returns ValueWithUnits
{
    var s = 0 * meter;
    for (var k = 0; k < ci; k += 1)
    {
        s += lengths[k];
    }
    return s + (chain[ci].reversed ? 1 - u : u) * lengths[ci];
}

/**
 * Chain position with world X = x: { ci, u }, or undefined when no edge spans x.
 * Assumes X is monotonic along each edge (true for a baseline).
 */
function chainParamAtX(context is Context, chain is array, x)
{
    for (var ci = 0; ci < size(chain); ci += 1)
    {
        const ends = evEdgeTangentLines(context, { "edge" : chain[ci].edge, "parameters" : [0, 1] });
        const x0 = ends[0].origin[0];
        const x1 = ends[1].origin[0];
        if (x >= min(x0, x1) && x <= max(x0, x1))
        {
            var lo = 0.0;
            var hi = 1.0;
            for (var iter = 0; iter < 50; iter += 1)
            {
                const mid  = (lo + hi) / 2;
                const xMid = evEdgeTangentLine(context, { "edge" : chain[ci].edge, "parameter" : mid }).origin[0];
                if ((xMid < x) == (x1 > x0))
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }
            return { "ci" : ci, "u" : (lo + hi) / 2 };
        }
    }
    return undefined;
}

/**
 * Ternary search for the point of largest perpendicular distance from the chord line
 * (chordOrigin, chordDir) on an edge within a native [uLo, uHi] bracket.
 */
function refineMaxCamberOnEdge(context is Context, edge is Query, uLo is number, uHi is number,
    chordOrigin is Vector, chordDir is Vector) returns map
{
    var lo = uLo;
    var hi = uHi;
    for (var iter = 0; iter < 40; iter += 1)
    {
        const m1 = lo + (hi - lo) / 3;
        const m2 = hi - (hi - lo) / 3;
        const d1 = perpDistToLine(evEdgeTangentLine(context, { "edge" : edge, "parameter" : m1 }).origin, chordOrigin, chordDir);
        const d2 = perpDistToLine(evEdgeTangentLine(context, { "edge" : edge, "parameter" : m2 }).origin, chordOrigin, chordDir);
        if (d1 > d2)
        {
            hi = m2;
        }
        else
        {
            lo = m1;
        }
    }
    const u = (lo + hi) / 2;
    return { "pt" : evEdgeTangentLine(context, { "edge" : edge, "parameter" : u }).origin, "u" : u };
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
    baselineEdgesQ is Query, fcpQ is Query, acpQ is Query, debug is boolean) returns map
{
    // Step 1: Build ordered edge chain
    var chain = buildEdgeChain(context, baselineEdgesQ);
    if (size(chain) == 0)
    {
        return undefined;
    }

    // Step 2: Uniform sample across entire chain (200 points)
    var samples = sampleChain(context, chain, 200);
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

    // Evaluate exact chain endpoints (unaffected by midpoint-sampling offset).
    // When FCP or ACP is an edge endpoint, the chain boundary parameter (0.0 or 1.0)
    // gives the exact position; midpoint samples are always offset by ~half interval.
    var lastCI         = size(chain) - 1;
    var chainStartCurv = evEdgeCurvatures(context, {
        "edge"       : chain[0].edge,
        "parameters" : [chain[0].reversed ? 1.0 : 0.0]
    });
    var chainEndCurv = evEdgeCurvatures(context, {
        "edge"       : chain[lastCI].edge,
        "parameters" : [chain[lastCI].reversed ? 0.0 : 1.0]
    });
    var chainStartPt = chainStartCurv[0].frame.origin;
    var chainEndPt   = chainEndCurv[0].frame.origin;

    var mrsX   = (fcpX + acpX) / 2;
    var fcpIdx = findSampleAtX(samples, fcpX);
    var acpIdx = findSampleAtX(samples, acpX);
    var mrsIdx = findSampleAtX(samples, mrsX);

    // Use exact chain endpoint for fcp_pt / acp_pt if it is closer in X than the
    // nearest midpoint sample (covers edge-endpoint FCP/ACP without regressing
    // the case where FCP/ACP are mate connectors in the middle of a span).
    var fcpPt         = samples[fcpIdx].pt;
    var distFcpSample = abs(fcpPt[0] - fcpX);
    if (abs(chainStartPt[0] - fcpX) < distFcpSample)
    {
        fcpPt = chainStartPt;
    }
    else if (abs(chainEndPt[0] - fcpX) < distFcpSample)
    {
        fcpPt = chainEndPt;
    }

    var acpPt         = samples[acpIdx].pt;
    var distAcpSample = abs(acpPt[0] - acpX);
    if (abs(chainStartPt[0] - acpX) < distAcpSample)
    {
        acpPt = chainStartPt;
    }
    else if (abs(chainEndPt[0] - acpX) < distAcpSample)
    {
        acpPt = chainEndPt;
    }

    var mrsPt = samples[mrsIdx].pt;

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

    var fbEdge  = chain[fbMinResult.curveIdx].edge;
    var abEdge  = chain[abMinResult.curveIdx].edge;
    var fbULo   = max(0, fbMinResult.u - 0.1);
    var fbUHi   = min(1, fbMinResult.u + 0.1);
    var abULo   = max(0, abMinResult.u - 0.1);
    var abUHi   = min(1, abMinResult.u + 0.1);
    var fbMinPt = refineMinZOnEdge(context, fbEdge, fbULo, fbUHi);
    var abMinPt = refineMinZOnEdge(context, abEdge, abULo, abUHi);

    // Step 5: Camber height -- max perpendicular distance from FB-min->AB-min chord
    var chordDir    = normalize(abMinPt - fbMinPt);
    var chordOrigin = fbMinPt;
    var xChordLow   = (fbMinPt[0] < abMinPt[0]) ? fbMinPt[0] : abMinPt[0];
    var xChordHigh  = (fbMinPt[0] > abMinPt[0]) ? fbMinPt[0] : abMinPt[0];

    var maxCamberPt  = fbMinPt;
    var maxCamberIdx = -1;
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
                maxCamberIdx = i;
            }
        }
    }

    // Step 6: Find all inflection points across the chain
    var allInflections = [];
    for (var ci = 0; ci < size(chain); ci += 1)
    {
        var curveInfs = findInflectionsOnCurve(context, chain[ci].edge);
        for (var infl in curveInfs)
        {
            allInflections = append(allInflections, infl);
        }
    }

    // Step 6b: Check for inflections at chain junctions (G1 joins).
    // For multi-segment curves (e.g., generateBaseline output), inflections may
    // occur exactly at the boundary between adjacent edges -- no single edge
    // contains the sign change, so the per-edge loop misses them.
    // Strategy: sample the principal-normal Z-component 20% inside each edge on
    // both sides of every junction. A sign change -> inflection at the junction.
    for (var ci = 0; ci < size(chain) - 1; ci += 1)
    {
        var edgeA    = chain[ci].edge;
        var edgeB    = chain[ci + 1].edge;
        var revA     = chain[ci].reversed;
        var revB     = chain[ci + 1].reversed;

        // 20% inside each edge from the shared junction end
        var tNearEndA   = revA ? 0.2 : 0.8;
        var tNearStartB = revB ? 0.8 : 0.2;

        var curvNearA = evEdgeCurvatures(context, { "edge" : edgeA, "parameters" : [tNearEndA] });
        var curvNearB = evEdgeCurvatures(context, { "edge" : edgeB, "parameters" : [tNearStartB] });

        // xAxis is the principal normal; its Z-component indicates curvature direction
        var xzA = curvNearA[0].frame.xAxis[2];
        var xzB = curvNearB[0].frame.xAxis[2];

        // Require a definite sign (ignore near-zero / flat sections)
        var CURV_THRESH = 0.01;
        var signA = (xzA > CURV_THRESH) ? 1 : ((xzA < -CURV_THRESH) ? -1 : 0);
        var signB = (xzB > CURV_THRESH) ? 1 : ((xzB < -CURV_THRESH) ? -1 : 0);

        if (signA != 0 && signB != 0 && signA != signB)
        {
            // Inflection is at the junction -- evaluate edgeA at its chain-end boundary
            var junctionParam = revA ? 0.0 : 1.0;
            var junctionCurv  = evEdgeCurvatures(context, { "edge" : edgeA, "parameters" : [junctionParam] });
            allInflections = append(allInflections, {
                "pt"   : junctionCurv[0].frame.origin,
                "u"    : junctionParam,
                "edge" : edgeA
            });
        }
    }

    if (debug)
    {
        println("=== analyzeBaselineGeometry ===");
        println("chain edges: " ~ size(chain));
        for (var ci = 0; ci < size(chain); ci += 1)
        {
            println("  edge[" ~ ci ~ "] reversed=" ~ (chain[ci].reversed ? "T" : "F"));
        }
        println("fcpX=" ~ toString(fcpX / millimeter) ~ "  acpX=" ~ toString(acpX / millimeter) ~ "  mrsX=" ~ toString(mrsX / millimeter) ~ " (mm)");
        println("FB region X=[" ~ toString(fbXLow / millimeter) ~ ", " ~ toString(fbXHigh / millimeter) ~ "]  AB region X=[" ~ toString(abXLow / millimeter) ~ ", " ~ toString(abXHigh / millimeter) ~ "] (mm)");
        println("fbMinPt=" ~ formatVec(fbMinPt) ~ "  abMinPt=" ~ formatVec(abMinPt));
        println("inflections found: " ~ size(allInflections));
        for (var infl in allInflections)
        {
            println("  infl " ~ formatVec(infl.pt));
        }
        println("curvature profile (u, x, z, N.z):");
        var profParams = [0.1, 0.25, 0.5, 0.75, 0.9];
        for (var ci = 0; ci < size(chain); ci += 1)
        {
            var profCurv = evEdgeCurvatures(context, { "edge" : chain[ci].edge, "parameters" : profParams });
            println("  edge[" ~ ci ~ "]:");
            for (var pi = 0; pi < size(profParams); pi += 1)
            {
                var pOrigin = profCurv[pi].frame.origin;
                var pNz     = profCurv[pi].frame.xAxis[2];
                println("    u=" ~ profParams[pi]
                    ~ " x=" ~ toString(pOrigin[0] / millimeter)
                    ~ " z=" ~ toString(pOrigin[2] / millimeter)
                    ~ " Nz=" ~ toString(pNz));
            }
        }
    }

    // Step 7: FRCP / ARCP -- in each half, the inflection closest to that half's minimum on
    // the side towards MRS (until 2026-09-28: the lowest-Z inflection of the half; the two
    // agree on Generate baseline curves, where both are the camber/rocker join).
    var frcpPt   = undefined;
    var frcpU    = undefined;
    var frcpEdge = undefined;
    var frcpInfl = selectRockerContact(allInflections, fbMinPt, mrsX, fbXLow, fbXHigh);
    if (frcpInfl != undefined)
    {
        frcpPt   = frcpInfl.pt;
        frcpU    = frcpInfl.u;
        frcpEdge = frcpInfl.edge;
    }

    var arcpPt   = undefined;
    var arcpU    = undefined;
    var arcpEdge = undefined;
    var arcpInfl = selectRockerContact(allInflections, abMinPt, mrsX, abXLow, abXHigh);
    if (arcpInfl != undefined)
    {
        arcpPt   = arcpInfl.pt;
        arcpU    = arcpInfl.u;
        arcpEdge = arcpInfl.edge;
    }

    if (debug)
    {
        println("frcpPt=" ~ (frcpPt == undefined ? "UNDEFINED" : formatVec(frcpPt)));
        println("arcpPt=" ~ (arcpPt == undefined ? "UNDEFINED" : formatVec(arcpPt)));
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

    // Steps 8-10: FRCP tangent (from evEdgeCurvatures), arc length, height
    if (frcpPt != undefined)
    {
        var frcpCurv = evEdgeCurvatures(context, { "edge" : frcpEdge, "parameters" : [frcpU] });
        var frcpDir  = frcpCurv[0].frame.zAxis;  // already normalized tangent
        result.frcp_pt  = frcpPt;
        result.frcp_dir = frcpDir;
        result.frcpl    = abs(frcpPt[0] - fcpPt[0]);
        result.fcph     = perpDistToLine(fcpPt, frcpPt, frcpDir);
        result.fb_roll  = abs(frcpPt[0] - fbMinPt[0]);
    }

    // Steps 8-10: ARCP tangent, arc length, height
    if (arcpPt != undefined)
    {
        var arcpCurv = evEdgeCurvatures(context, { "edge" : arcpEdge, "parameters" : [arcpU] });
        var arcpDir  = arcpCurv[0].frame.zAxis;  // already normalized tangent
        result.arcp_pt  = arcpPt;
        result.arcp_dir = arcpDir;
        result.arcpl    = abs(acpPt[0] - arcpPt[0]);
        result.acph     = perpDistToLine(acpPt, arcpPt, arcpDir);
        result.ab_roll  = abs(arcpPt[0] - abMinPt[0]);
    }

    // Step 11: MCl -- max camber position, refined on its edge (the 200-sample spacing is
    // ~20 mm on a long camber edge). camber_height / max_camber_pt above stay sample-based.
    if (maxCamberIdx >= 0)
    {
        const mcCi = samples[maxCamberIdx].curveIdx;
        var nOnEdge = 0;
        for (var smp in samples)
        {
            if (smp.curveIdx == mcCi)
            {
                nOnEdge += 1;
            }
        }
        const du    = 1 / max(nOnEdge, 1);
        const mcU0  = samples[maxCamberIdx].u;
        const mcRef = refineMaxCamberOnEdge(context, chain[mcCi].edge, max(0, mcU0 - du), min(1, mcU0 + du), chordOrigin, chordDir);
        result.mcl_pt = mcRef.pt;
        result.mcl_x  = mcRef.pt[0];

        // Signed arc length from MRS, positive towards FCP (the tip).
        const mrsPos = chainParamAtX(context, chain, mrsX);
        if (mrsPos != undefined)
        {
            var lengths = [];
            for (var chainLink in chain)
            {
                lengths = append(lengths, evLength(context, { "entities" : chainLink.edge }));
            }
            const sMcl = chainArcLength(chain, lengths, mcCi, mcRef.u);
            const sMrs = chainArcLength(chain, lengths, mrsPos.ci, mrsPos.u);
            const fcpAtChainStart = abs(chainStartPt[0] - fcpX) < abs(chainEndPt[0] - fcpX);
            result.mcl_s = (fcpAtChainStart ? -1 : 1) * (sMcl - sMrs);
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
    var result = analyzeBaselineGeometry(context, definition.baselineEdges, definition.fcpQ, definition.acpQ, false);
    if (result == undefined)
    {
        return definition;
    }

    definition.fbMinString           = formatVec(result.fb_min_pt);
    definition.abMinString           = formatVec(result.ab_min_pt);
    definition.maxCamberHeightString = formatVec(result.max_camber_pt);
    definition.camberHeight          = result.camber_height;
    if (result.mcl_x != undefined)
    {
        definition.mclX = result.mcl_x;
    }
    if (result.mcl_s != undefined)
    {
        definition.mclS = result.mcl_s;
    }

    if (result.frcp_pt != undefined)
    {
        definition.frcp     = formatVec(result.frcp_pt);
        definition.frcpl    = result.frcpl;
        definition.frcpLine = formatLine(result.frcp_pt, result.frcp_dir);
        definition.fcph     = result.fcph;
        definition.fbRoll   = result.fb_roll;
    }

    if (result.arcp_pt != undefined)
    {
        definition.arcp     = formatVec(result.arcp_pt);
        definition.arcpl    = result.arcpl;
        definition.arcpLine = formatLine(result.arcp_pt, result.arcp_dir);
        definition.acph     = result.acph;
        definition.abRoll   = result.ab_roll;
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
            annotation { "Name" : "FB min", "Description" : "String of vector in mm for minimum fb point", "UIHint" : UIHint.READ_ONLY }
            definition.fbMinString is string;

            annotation { "Name" : "AB min", "Description" : "String of vector in mm for minimum ab point", "UIHint" : UIHint.READ_ONLY }
            definition.abMinString is string;

            annotation { "Name" : "Max camber point", "Description" : "String of vector in mm for max camber point", "UIHint" : UIHint.READ_ONLY }
            definition.maxCamberHeightString is string;

            annotation { "Name" : "Camber height", "UIHint" : UIHint.READ_ONLY }
            isLength(definition.camberHeight, LENGTH_BOUNDS);

            annotation { "Name" : "MCl x", "Description" : "World X of the max camber point", "UIHint" : UIHint.READ_ONLY }
            isLength(definition.mclX, LENGTH_BOUNDS);

            annotation { "Name" : "MCl s", "Description" : "Arc length along the baseline from MRS to the max camber point; positive towards FCP (tip)", "UIHint" : UIHint.READ_ONLY }
            isLength(definition.mclS, LENGTH_BOUNDS);

            annotation { "Name" : "FRCP", "Description" : "Forebody rocker contact point: the inflection closest to the forebody minimum, towards MRS", "UIHint" : UIHint.READ_ONLY }
            definition.frcp is string;

            annotation { "Name" : "FRCPL", "Description" : "Forebody rocker contact point length. Distance between fcp and frcp", "UIHint" : UIHint.READ_ONLY }
            isLength(definition.frcpl, LENGTH_BOUNDS);

            annotation { "Name" : "FRCP tangent line", "Description" : "Forebody rocker tangent line", "UIHint" : UIHint.READ_ONLY }
            definition.frcpLine is string;

            annotation { "Name" : "FCPH", "Description" : "Height of FCP when baseline is weighted. Distance between FCP point and forebody rocker tangent line", "UIHint" : UIHint.READ_ONLY }
            isLength(definition.fcph, LENGTH_BOUNDS);

            annotation { "Name" : "FB roll", "Description" : "Forebody roll length. X distance between the forebody minimum and FRCP", "UIHint" : UIHint.READ_ONLY }
            isLength(definition.fbRoll, LENGTH_BOUNDS);

            annotation { "Name" : "ARCP", "Description" : "Aftbody rocker contact point: the inflection closest to the aftbody minimum, towards MRS", "UIHint" : UIHint.READ_ONLY }
            definition.arcp is string;

            annotation { "Name" : "ARCPL", "Description" : "Aftebody rocker contact point length. Distance between fcp and frcp", "UIHint" : UIHint.READ_ONLY }
            isLength(definition.arcpl, LENGTH_BOUNDS);

            annotation { "Name" : "ARCP tangent line", "Description" : "Aftbody rocker tangent line", "UIHint" : UIHint.READ_ONLY }
            definition.arcpLine is string;

            annotation { "Name" : "ACPH", "Description" : "Height of ACP when baseline is weighted. Distance between ACP point and aftbody rocker tangent line", "UIHint" : UIHint.READ_ONLY }
            isLength(definition.acph, LENGTH_BOUNDS);

            annotation { "Name" : "AB roll", "Description" : "Aftbody roll length. X distance between the aftbody minimum and ARCP", "UIHint" : UIHint.READ_ONLY }
            isLength(definition.abRoll, LENGTH_BOUNDS);
        }

        annotation { "Name" : "Output measurement sketch" }
        definition.outputMeasurementSketch is boolean;

        annotation { "Name" : "Recalculate" }
        isButton(definition.recalculate);
    }
    {
        if (!definition.outputMeasurementSketch)
        {
            return;
        }

        var result = analyzeBaselineGeometry(context, definition.baselineEdges, definition.fcpQ, definition.acpQ, false);
        if (result == undefined)
        {
            return;
        }

        // Foot of perp from max_camber_pt to fb_min->ab_min chord
        var chordDir   = normalize(result.ab_min_pt - result.fb_min_pt);
        var camberDiff = result.max_camber_pt - result.fb_min_pt;
        var camberFoot = result.fb_min_pt + dot(camberDiff, chordDir) * chordDir;

        // Foot of perp from FCP to FRCP tangent line
        var fbFoot = undefined;
        if (result.frcp_pt != undefined)
        {
            var fcpDiff = result.fcp_pt - result.frcp_pt;
            fbFoot = result.frcp_pt + dot(fcpDiff, result.frcp_dir) * result.frcp_dir;
        }

        // Foot of perp from ACP to ARCP tangent line
        var abFoot = undefined;
        if (result.arcp_pt != undefined)
        {
            var acpDiff = result.acp_pt - result.arcp_pt;
            abFoot = result.arcp_pt + dot(acpDiff, result.arcp_dir) * result.arcp_dir;
        }

        // Create sketch on world XZ plane.
        // normal = -Y so that worldToPlane gives: sketch x = world X, sketch y = world Z.
        // (worldToPlane computes planeY = cross(normal, x); with normal=-Y and x=+X,
        //  planeY = cross((0,-1,0),(1,0,0)) = (0,0,1) = +world Z)
        var sketchPl = plane(vector(0, 0, 0) * meter, vector(0, -1, 0), vector(1, 0, 0));
        var sketch = newSketchOnPlane(context, id + "baselineMeasurementSketch", {
            "sketchPlane" : sketchPl
        });

        // 1. Centerline: fb_min_pt -> ab_min_pt
        skLineSegment(sketch, "minChord", {
            "start"        : worldToPlane(sketchPl, result.fb_min_pt),
            "end"          : worldToPlane(sketchPl, result.ab_min_pt),
            "construction" : true
        });

        // 2. Centerline: FRCP -> ARCP (if both inflection points exist)
        if (result.frcp_pt != undefined && result.arcp_pt != undefined)
        {
            skLineSegment(sketch, "inflChord", {
                "start"        : worldToPlane(sketchPl, result.frcp_pt),
                "end"          : worldToPlane(sketchPl, result.arcp_pt),
                "construction" : true
            });
        }

        // 3a. FB triangle legs: FRCP <-> foot-of-perp, FCP <-> foot-of-perp
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

        // 3b. AB triangle legs: ARCP <-> foot-of-perp, ACP <-> foot-of-perp
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

        // 4. Camber normal: max_camber_pt -> camberFoot (perpendicular to min chord)
        skLineSegment(sketch, "camberNormal", {
            "start"        : worldToPlane(sketchPl, result.max_camber_pt),
            "end"          : worldToPlane(sketchPl, camberFoot),
            "construction" : true
        });

        skSolve(sketch);
    });
