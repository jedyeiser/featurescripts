FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
import(path : "onshape/std/geometriccontinuity.gen.fs", version : "3083.0");

//import CurveWrapping_full/curveMappingCore
import(path : "08e8748f2ef24eea16072b75/1e6c50f8b5c1c4c48b5e745d/683d867c35fdab9c98d47556", version : "be9fd598630c331b5bc74e36");
//import CurveWrapping_full/Utils
import(path : "08e8748f2ef24eea16072b75/1e6c50f8b5c1c4c48b5e745d/ad98c7f43a25a4c0e8a428e7", version : "223c53d12a83984c4c62e354");
// IMPORT: offset_edges_frames.fs (same document)
import(path : "cf322190768efbd668185c8c", version : "6f365484ed2b8f83fb60ee41");

/**
 * Offset edges -- regions, profile and output wire (split out of offsetEdges.fs 2026-09-26).
 *
 * The enums here are parameter types of the Offset edges feature: offsetEdges.fs export-imports this tab so
 * they stay reachable from the feature's exports.
 */

// --- Enums --------------------------------------------------------------------

export enum RegionType
{
    LINEAR,
    QUADRATIC,
    SMOOTH
}

export enum RegionExtentType
{
    QUERY,
    X_EXTENTS
}

export enum QuadraticZeroSlope
{
    AT_START,
    AT_END
}

// LABEL-PROPOSAL (review item 16; display names only, values unchanged): was "Normal" / "Binormal". NORMAL
// moves along T x N (out of the plane of a planar path), BINORMAL along the transported normal (in plane).
export enum OffsetType
{
    annotation { "Name" : "Out of plane" }   // LABEL-PROPOSAL was "Normal"
    NORMAL,
    annotation { "Name" : "In plane" }       // LABEL-PROPOSAL was "Binormal"
    BINORMAL,
    annotation { "Name" : "Both" }
    BOTH
}

export enum VaryingArcMode
{
    annotation { "Name" : "Convert to splines" }
    SPLINE,
    annotation { "Name" : "Keep as arcs" }
    BIARC
}

export enum OffsetMode
{
    annotation { "Name" : "Multiple regions" }
    MULTI_REGION,
    annotation { "Name" : "Single region" }
    SINGLE_REGION
}

// --- Region processing --------------------------------------------------------
/**
 * Region processing
 * @param context {Context} : context
 * @param definition {{
 *      @field regions {array} : regions from defi
 *          }}
 * @param pathInfo {map} : frenetPath
 */
export function processRegions(context is Context, definition is map, pathInfo is map) returns array
{
    var processed = [];

    for (var i = 0; i < size(definition.regions); i += 1)
    {
        var region = definition.regions[i];
        region.regionNum = i;

        var tStart;
        var tEnd;

        if (region.extentType == RegionExtentType.X_EXTENTS)
        {
            tStart = pathInfo.refParam + region.regionStart / pathInfo.length;
            tEnd   = pathInfo.refParam + region.regionEnd   / pathInfo.length;
        }
        else // QUERY
        {
            var queryPts = evaluateQuery(context, region.extentQueries);
            if (size(queryPts) != 2)
            {
                throw regenError("Region '" ~ region.regionName ~ "': extent query must resolve to exactly 2 points");
            }

            var pt0 = getRefPoint(context, queryPts[0]);
            var pt1 = getRefPoint(context, queryPts[1]);

            var r0 = projectOntoFrenetPath(pathInfo.frenetPath, pt0, undefined);
            var r1 = projectOntoFrenetPath(pathInfo.frenetPath, pt1, undefined);

            tStart = min(r0.arcLength, r1.arcLength) / pathInfo.length;
            tEnd   = max(r0.arcLength, r1.arcLength) / pathInfo.length;
        }

        // Keep tStart < tEnd. When the ends swap (Region start past Region end along the path) every
        // start/end pair swaps with them -- offsets, dwells and the quadratic's zero-slope end -- so each
        // value stays at the end the user entered it for. (Interior stations are positions: unaffected.)
        tStart = min(max(tStart, 0), 1);
        tEnd   = min(max(tEnd,   0), 1);
        if (tStart > tEnd)
        {
            var tmp = tStart;
            tStart = tEnd;
            tEnd = tmp;

            var tmpN = region.startNormalOffset;
            region.startNormalOffset = region.endNormalOffset;
            region.endNormalOffset = tmpN;

            var tmpB = region.startBinormalOffset;
            region.startBinormalOffset = region.endBinormalOffset;
            region.endBinormalOffset = tmpB;

            var tmpD = region.startDelay;
            region.startDelay = region.endDelay;
            region.endDelay = tmpD;

            if (region.regionType == RegionType.QUADRATIC)
            {
                region.quadZeroSlope = (region.quadZeroSlope == QuadraticZeroSlope.AT_END)
                    ? QuadraticZeroSlope.AT_START : QuadraticZeroSlope.AT_END;
            }
        }

        region.tStart = tStart;
        region.tEnd   = tEnd;
        region.length = (tEnd - tStart) * pathInfo.length;

        var span = tEnd - tStart;
        var ot = (region.offsetType != undefined) ? region.offsetType : OffsetType.BOTH;
        var useNormal   = (ot == OffsetType.NORMAL   || ot == OffsetType.BOTH);
        var useBinormal = (ot == OffsetType.BINORMAL || ot == OffsetType.BOTH);

        // --- Resolve interior stations (fixed per-region slots) into {alpha, normalOff, binormalOff} ---
        var warnings  = [];
        var resolved  = [];

        for (var si = 1; si <= 5; si += 1)
        {
            var pfx = "station" ~ toString(si);
            if (region[pfx ~ "Enabled"] != true)
            {
                continue;   // slot disabled (or legacy region without this field)
            }
            var locType = region[pfx ~ "LocationType"];

            var stT;
            if (locType == RegionExtentType.QUERY)
            {
                var stPts = evaluateQuery(context, region[pfx ~ "Query"]);
                if (size(stPts) != 1)
                {
                    warnings = append(warnings, "Region '" ~ region.regionName ~
                        "' station " ~ toString(si) ~ ": location must resolve to exactly 1 point; skipped.");
                    continue;
                }
                var stPt  = getRefPoint(context, stPts[0]);
                var stRes = projectOntoFrenetPath(pathInfo.frenetPath, stPt, undefined);
                stT = stRes.arcLength / pathInfo.length;
            }
            else // X_EXTENTS (also the default when locType is unset)
            {
                stT = pathInfo.refParam + region[pfx ~ "Position"] / pathInfo.length;
            }

            var alpha = (span > 1e-10) ? (stT - tStart) / span : 0.0;
            if (alpha < -1e-6 || alpha > 1 + 1e-6)
            {
                warnings = append(warnings, "Region '" ~ region.regionName ~
                    "' station " ~ toString(si) ~ " lies outside the region extent; skipped.");
                continue;
            }
            alpha = min(max(alpha, 0.0), 1.0);

            var nOff = (useNormal   && region[pfx ~ "NormalOffset"]   != undefined) ? region[pfx ~ "NormalOffset"]   : undefined;
            var bOff = (useBinormal && region[pfx ~ "BinormalOffset"] != undefined) ? region[pfx ~ "BinormalOffset"] : undefined;

            resolved = append(resolved, { "alpha" : alpha, "normalOff" : nOff, "binormalOff" : bOff });
        }

        // Sort interior stations by alpha (std sort is a stable merge sort: equal alphas keep their order,
        // as the insertion sort this replaced did)
        resolved = sort(resolved, function(p, q) { return p.alpha - q.alpha; });

        for (var a = 1; a < size(resolved); a += 1)
        {
            if (abs(resolved[a].alpha - resolved[a - 1].alpha) < 1e-4)
            {
                warnings = append(warnings, "Region '" ~ region.regionName ~
                    "': coincident interior stations detected.");
            }
        }

        region.stations = resolved;

        // --- Dwell fractions (alpha-space), clamped ---
        var startDelay = (region.startDelay != undefined) ? region.startDelay : 0 * meter;
        var endDelay   = (region.endDelay   != undefined) ? region.endDelay   : 0 * meter;
        var d0 = (region.length / meter > 1e-12) ? startDelay / region.length : 0.0;
        var d1 = (region.length / meter > 1e-12) ? endDelay   / region.length : 0.0;
        d0 = min(max(d0, 0.0), 1.0);
        d1 = min(max(d1, 0.0), 1.0);
        if (d0 + d1 > 1.0 + 1e-9)
        {
            warnings = append(warnings, "Region '" ~ region.regionName ~
                "': start + end dwell exceed the region length; dwell clamped.");
        }
        region.startDelayFrac = d0;
        region.endDelayFrac   = d1;

        region.stationWarnings = warnings;

        processed = append(processed, region);
    }

    return processed;
}


// Builds one synthetic region for SINGLE_REGION mode from the top-level interiorOffsets array.
// The region spans the whole path (tStart 0, tEnd 1); each offset point becomes a resolved
// station, and the per-component endpoint values are the first/last pinning point's value so
// the profile holds flat beyond the outer points. Returns [region] for the normal pipeline.
export function assembleSingleRegion(context is Context, definition is map, pathInfo is map) returns array
{
    var offsets  = (definition.interiorOffsets != undefined) ? definition.interiorOffsets : [];
    var resolved = [];

    for (var i = 0; i < size(offsets); i += 1)
    {
        var off  = offsets[i];
        var ot   = off.pointOffsetType;
        var useN = (ot == OffsetType.NORMAL   || ot == OffsetType.BOTH);
        var useB = (ot == OffsetType.BINORMAL || ot == OffsetType.BOTH);

        var t;
        if (off.locationType == RegionExtentType.QUERY)
        {
            var pts = evaluateQuery(context, off.locationQuery);
            if (size(pts) != 1)
            {
                continue;
            }
            var res = projectOntoFrenetPath(pathInfo.frenetPath, getRefPoint(context, pts[0]), undefined);
            t = res.arcLength / pathInfo.length;
        }
        else // X_EXTENTS (also default)
        {
            t = pathInfo.refParam + off.position / pathInfo.length;
        }
        t = min(max(t, 0), 1);

        var nOff = (useN && off.normalOffset   != undefined) ? off.normalOffset   : undefined;
        var bOff = (useB && off.binormalOffset != undefined) ? off.binormalOffset : undefined;
        resolved = append(resolved, { "alpha" : t, "normalOff" : nOff, "binormalOff" : bOff });
    }

    // Sort by alpha (stable: equal alphas keep dialog order)
    resolved = sort(resolved, function(p, q) { return p.alpha - q.alpha; });

    // Per-component flat-hold endpoint values: first/last point that pins that component.
    var startN = 0 * meter; var endN = 0 * meter; var haveN = false;
    var startB = 0 * meter; var endB = 0 * meter; var haveB = false;
    for (var i = 0; i < size(resolved); i += 1)
    {
        if (resolved[i].normalOff != undefined)
        {
            if (!haveN) { startN = resolved[i].normalOff; haveN = true; }
            endN = resolved[i].normalOff;
        }
        if (resolved[i].binormalOff != undefined)
        {
            if (!haveB) { startB = resolved[i].binormalOff; haveB = true; }
            endB = resolved[i].binormalOff;
        }
    }

    var qzs = (definition.singleQuadZeroSlope != undefined) ? definition.singleQuadZeroSlope : QuadraticZeroSlope.AT_START;

    var region = {
        "regionNum"           : 0,
        "regionName"          : "Region 1",
        "regionType"          : definition.singleTransfer,
        "quadZeroSlope"       : qzs,
        "offsetType"          : OffsetType.BOTH,
        "tStart"              : 0,
        "tEnd"                : 1,
        "length"              : pathInfo.length,
        "startNormalOffset"   : startN,
        "endNormalOffset"     : endN,
        "startBinormalOffset" : startB,
        "endBinormalOffset"   : endB,
        "startDelayFrac"      : 0,
        "endDelayFrac"        : 0,
        "stations"            : resolved,
        "stationWarnings"     : []
    };

    return [region];
}


export function validateNoOverlap(context is Context, id is Id, regions is array)
{
    for (var i = 0; i < size(regions) - 1; i += 1)
    {
        for (var j = i + 1; j < size(regions); j += 1)
        {
            var a = regions[i];
            var b = regions[j];
            var overlapStart = max(a.tStart, b.tStart);
            var overlapEnd   = min(a.tEnd,   b.tEnd);
            if (overlapEnd > overlapStart + 1e-6)
            {
                reportFeatureWarning(context, id,
                    "Regions '" ~ a.regionName ~ "' and '" ~ b.regionName ~
                    "' overlap. Results may be unexpected.");
            }
        }
    }
}


export function sortRegionsByTStart(regions is array) returns array
{
    // Stable: regions starting together keep their dialog order (as the insertion sort this replaced did)
    return sort(regions, function(p, q) { return p.tStart - q.tStart; });
}


// A region's identity for blends and trims: its stable hidden id, or -- for regions saved before ids existed
// (2026-09-26; the editing logic assigns ids on the next dialog edit, never on regeneration) -- its name.
export function regionKey(region is map) returns string
{
    if (region.regionId != undefined && region.regionId != "")
    {
        return "id:" ~ region.regionId;
    }
    return "name:" ~ region.regionName;
}


// True when side `which` (1 or 2) of an intersection refers to this region: by the stored region id when the
// intersection has one, else by name (intersections saved before ids existed resolve exactly as before).
export function intersectionRefersTo(intr is map, which is number, region is map) returns boolean
{
    var idKey   = (which == 1) ? intr.region1Id : intr.region2Id;
    var nameKey = (which == 1) ? intr.region1   : intr.region2;
    if (idKey != undefined && idKey != "")
    {
        return region.regionId == idKey;
    }
    return region.regionName == nameKey;
}


// --- Profile evaluation -------------------------------------------------------

/**
 * Returns the interpolated offset at normalized position t in [0,1] within a region.
 *   LINEAR    : linear ramp
 *   QUADRATIC : true quadratic -- one endpoint has zero slope
 *   SMOOTH    : smootherstep (6t^5 - 15t^4 + 10t^3) -- C2 at both endpoints
 */
export function profileValueAt(t is number, startVal is ValueWithUnits, endVal is ValueWithUnits,
    regionType, quadZeroSlope) returns ValueWithUnits
{
    var s;
    if (regionType == RegionType.LINEAR)
    {
        s = t;
    }
    else if (regionType == RegionType.QUADRATIC)
    {
        if (quadZeroSlope == QuadraticZeroSlope.AT_END)
        {
            s = 2 * t - t * t;
        }
        else // AT_START
        {
            s = t * t;
        }
    }
    else // SMOOTH
    {
        s = t * t * t * (10 + t * (6 * t - 15));
    }
    return startVal + (endVal - startVal) * s;
}


/**
 * Evaluates a single control-point segment with optional start/end dwell plateaus.
 *   u   : segment-local parameter in [0,1]
 *   dd0 : fraction of the segment (from u=0) held flat at startVal
 *   dd1 : fraction of the segment (up to u=1) held flat at endVal
 * The ramp is compressed into [dd0, 1-dd1] and the region transfer is applied there.
 */
export function segmentValueAt(u is number, startVal is ValueWithUnits, endVal is ValueWithUnits,
    regionType, quadZeroSlope, dd0 is number, dd1 is number) returns ValueWithUnits
{
    var a = min(max(dd0, 0), 1);
    var b = 1 - min(max(dd1, 0), 1);

    if (a >= b)
    {
        // Degenerate dwell (plateaus meet or overlap): collapse to a step at the crossover.
        var c = min(max(0.5 * (a + b), 0), 1);
        if (u <= c)
        {
            return startVal;
        }
        return endVal;
    }

    if (u <= a)
    {
        return startVal;
    }
    if (u >= b)
    {
        return endVal;
    }

    var uRamp = (u - a) / (b - a);
    return profileValueAt(uRamp, startVal, endVal, regionType, quadZeroSlope);
}


/**
 * Builds the ordered control-point list [{alpha, value}, ...] for one Frenet component,
 * pinning start (alpha 0), any interior stations that carry this component, and end (alpha 1).
 * component is "normal" or "binormal". Stations arrive pre-sorted by alpha from processRegions;
 * stations that do not pin this component (value undefined) are skipped.
 */
export function getComponentCPs(region is map, component is string) returns array
{
    var startVal;
    var endVal;
    if (component == "normal")
    {
        startVal = region.startNormalOffset;
        endVal   = region.endNormalOffset;
    }
    else
    {
        startVal = region.startBinormalOffset;
        endVal   = region.endBinormalOffset;
    }

    var cps = [{ "alpha" : 0, "value" : startVal }];
    var stations = (region.stations != undefined) ? region.stations : [];

    for (var st in stations)
    {
        var v = (component == "normal") ? st.normalOff : st.binormalOff;
        if (v == undefined)
        {
            continue;
        }
        var a = min(max(st.alpha, 0), 1);
        if (a <= 1e-9 || a >= 1 - 1e-9)
        {
            continue;   // coincident with an endpoint pin
        }
        var last = cps[size(cps) - 1];
        if (abs(a - last.alpha) < 1e-9)
        {
            cps[size(cps) - 1] = { "alpha" : a, "value" : v };   // collapse coincident, later wins
        }
        else
        {
            cps = append(cps, { "alpha" : a, "value" : v });
        }
    }

    cps = append(cps, { "alpha" : 1, "value" : endVal });
    return cps;
}


/**
 * Evaluates a multi-station piecewise profile at alpha in [0,1].
 * The region transfer is applied on each segment; start/end dwell (d0,d1, alpha-space
 * fractions of the whole region) become plateaus on the first/last segments only.
 */
export function evalComponentProfile(cps is array, alpha is number,
    regionType, quadZeroSlope, d0 is number, d1 is number) returns ValueWithUnits
{
    var m = size(cps);
    if (m == 0)
    {
        return 0 * meter;
    }
    if (m == 1)
    {
        return cps[0].value;
    }

    var nSeg = m - 1;

    // Locate the segment: largest i with cps[i].alpha <= alpha
    var i = 0;
    for (var k = 0; k < nSeg; k += 1)
    {
        if (alpha >= cps[k].alpha)
        {
            i = k;
        }
    }

    var a0      = cps[i].alpha;
    var a1      = cps[i + 1].alpha;
    var segSpan = a1 - a0;
    var u       = (segSpan > 1e-10) ? min(max((alpha - a0) / segSpan, 0), 1) : 0;

    // Dwell only on the outer segments; convert region-space fraction to segment-local fraction.
    var dd0 = 0;
    var dd1 = 0;
    if (i == 0 && segSpan > 1e-10)
    {
        dd0 = d0 / segSpan;
    }
    if (i == nSeg - 1 && segSpan > 1e-10)
    {
        dd1 = d1 / segSpan;
    }

    return segmentValueAt(u, cps[i].value, cps[i + 1].value, regionType, quadZeroSlope, dd0, dd1);
}


/**
 * Returns the sorted t-space breakpoints of the region profile: endpoints, dwell-plateau
 * edges, and station positions. Used to (a) keep finite differences inside a single smooth
 * segment and (b) land sample nodes on kinks during wire construction.
 */
export function getBreakpointsT(region is map) returns array
{
    var tS   = region.tStart;
    var tE   = region.tEnd;
    var span = tE - tS;

    var d0 = (region.startDelayFrac != undefined) ? min(max(region.startDelayFrac, 0), 1) : 0;
    var d1 = (region.endDelayFrac   != undefined) ? min(max(region.endDelayFrac,   0), 1) : 0;

    var alphas = [0, 1];
    if (d0 > 1e-9)
    {
        alphas = append(alphas, d0);
    }
    if (d1 > 1e-9)
    {
        alphas = append(alphas, 1 - d1);
    }

    var stations = (region.stations != undefined) ? region.stations : [];
    for (var st in stations)
    {
        alphas = append(alphas, min(max(st.alpha, 0), 1));
    }

    var ts = [];
    for (var av in alphas)
    {
        ts = append(ts, tS + av * span);
    }

    return sort(ts, function(p, q) { return p - q; });
}


/**
 * Returns { normalOff, binormalOff } at path parameter tPath from the region's profile,
 * supporting interior stations and start/end dwell. With no stations and zero dwell this
 * reduces exactly to the legacy single-transfer ramp.
 */
export function computeOffsetsAt(region is map, tPath is number) returns map
{
    var span  = region.tEnd - region.tStart;
    var alpha = (span > 1e-10) ? min(max((tPath - region.tStart) / span, 0), 1) : 0;
    var quadZS = region.quadZeroSlope;

    // Offset type gates which components are applied; the hidden ones contribute 0. Fall
    // back to BOTH for regions created before offsetType existed (preserves old behavior).
    var ot = (region.offsetType != undefined) ? region.offsetType : OffsetType.BOTH;
    var useNormal   = (ot == OffsetType.NORMAL   || ot == OffsetType.BOTH);
    var useBinormal = (ot == OffsetType.BINORMAL || ot == OffsetType.BOTH);

    var d0 = (region.startDelayFrac != undefined) ? min(max(region.startDelayFrac, 0), 1) : 0;
    var d1 = (region.endDelayFrac   != undefined) ? min(max(region.endDelayFrac,   0), 1) : 0;

    var normalOff = 0 * meter;
    if (useNormal)
    {
        var nCPs  = getComponentCPs(region, "normal");
        normalOff = evalComponentProfile(nCPs, alpha, region.regionType, quadZS, d0, d1);
    }

    var binormalOff = 0 * meter;
    if (useBinormal)
    {
        var bCPs    = getComponentCPs(region, "binormal");
        binormalOff = evalComponentProfile(bCPs, alpha, region.regionType, quadZS, d0, d1);
    }

    return { "normalOff" : normalOff, "binormalOff" : binormalOff };
}


/**
 * Blend-end derivatives from the region's polynomial pieces (true, 2026-09-26) or from the finite differences
 * used before (false: bit-for-bit the old blends). They differ by the differences' truncation error only:
 * O(h^2) inside a piece, O(h) where a blend starts exactly on a kink of a quadratic / smooth region.
 */
export const ANALYTIC_PROFILE_DERIVATIVES = true;

/**
 * Returns { normalOff, binormalOff, normalSlope, binormalSlope, normalCurv, binormalCurv } in t-space
 * (VWU per t, VWU per t^2). Scale to s-space before blendOffsetAt: slope * L, curv * L^2.
 */
export function computeOffsetDerivativesAt(region is map, tPath is number) returns map
{
    if (ANALYTIC_PROFILE_DERIVATIVES)
    {
        var exact = analyticOffsetDerivativesAt(region, tPath);
        if (exact != undefined)
        {
            return exact;
        }
    }
    return finiteDifferenceOffsetDerivativesAt(region, tPath);
}


// [S'(u), S''(u)] of the region transfer S used by profileValueAt.
export function transferDerivatives(u is number, regionType, quadZeroSlope) returns array
{
    if (regionType == RegionType.LINEAR)
    {
        return [1, 0];
    }
    if (regionType == RegionType.QUADRATIC)
    {
        if (quadZeroSlope == QuadraticZeroSlope.AT_END)
        {
            return [2 - 2 * u, -2];
        }
        return [2 * u, 2];   // AT_START
    }
    // SMOOTH: S = 10u^3 - 15u^4 + 6u^5
    return [30 * u * u * (1 - u) * (1 - u), 60 * u * (1 - u) * (1 - 2 * u)];
}


// [df/dalpha, d2f/dalpha2] of one component's profile (getComponentCPs + evalComponentProfile) at alpha, on the
// polynomial piece that contains alphaProbe (alphaProbe = alpha inside a piece; a point just to one side when
// alpha sits on a kink, which picks the side). undefined where the piece is not a plain polynomial here: a
// collapsed dwell (the profile steps), a zero-length segment.
export function componentDerivatives(cps is array, alpha is number, alphaProbe is number,
    regionType, quadZeroSlope, d0 is number, d1 is number)
{
    var m = size(cps);
    if (m < 2)
    {
        return [0 * meter, 0 * meter];   // constant (or empty: 0)
    }
    var nSeg = m - 1;
    var i = 0;
    for (var k = 0; k < nSeg; k += 1)
    {
        if (alphaProbe >= cps[k].alpha)
        {
            i = k;
        }
    }
    var a0      = cps[i].alpha;
    var segSpan = cps[i + 1].alpha - a0;
    if (segSpan <= 1e-10)
    {
        return undefined;
    }
    var dd0 = (i == 0)        ? d0 / segSpan : 0;
    var dd1 = (i == nSeg - 1) ? d1 / segSpan : 0;
    var a = min(max(dd0, 0), 1);
    var b = 1 - min(max(dd1, 0), 1);
    if (a >= b)
    {
        return undefined;
    }
    var uProbe = (alphaProbe - a0) / segSpan;
    if (uProbe <= a || uProbe >= b)
    {
        return [0 * meter, 0 * meter];   // on a dwell plateau
    }
    var u     = min(max((alpha - a0) / segSpan, 0), 1);
    var uRamp = min(max((u - a) / (b - a), 0), 1);
    var sd    = transferDerivatives(uRamp, regionType, quadZeroSlope);
    var scale = 1 / ((b - a) * segSpan);      // d uRamp / d alpha
    var delta = cps[i + 1].value - cps[i].value;
    return [delta * sd[0] * scale, delta * sd[1] * scale * scale];
}


// The derivatives computeOffsetDerivativesAt returns, exactly, from the region's polynomial pieces. The side
// at a kink is the one the finite differences step into (the roomier neighbouring segment). undefined where
// the profile is not known in closed form here (outside the region, degenerate pieces) -- the caller then
// falls back to the finite differences.
export function analyticOffsetDerivativesAt(region is map, tPath is number)
{
    var span = region.tEnd - region.tStart;
    if (span <= 1e-10 || tPath < region.tStart - 1e-9 || tPath > region.tEnd + 1e-9)
    {
        return undefined;
    }

    // Same neighbouring breakpoints and side choice as finiteDifferenceOffsetDerivativesAt.
    var segLo   = region.tStart;
    var segHi   = region.tEnd;
    var onBreak = false;
    for (var bp in getBreakpointsT(region))
    {
        if (abs(bp - tPath) < 1e-9)
        {
            onBreak = true;
        }
        else if (bp < tPath && bp > segLo)
        {
            segLo = bp;
        }
        else if (bp > tPath && bp < segHi)
        {
            segHi = bp;
        }
    }
    var tProbe = tPath;
    if (onBreak || min(1e-4, min(tPath - segLo, segHi - tPath)) <= 1e-9)
    {
        var roomHi = segHi - tPath;
        var roomLo = tPath - segLo;
        var dir    = (roomHi >= roomLo) ? 1 : -1;
        var room   = (dir > 0) ? roomHi : roomLo;
        if (room <= 1e-12)
        {
            return undefined;
        }
        tProbe = tPath + dir * room / 2;
    }

    var alpha      = min(max((tPath  - region.tStart) / span, 0), 1);
    var alphaProbe = min(max((tProbe - region.tStart) / span, 0), 1);
    var ot = (region.offsetType != undefined) ? region.offsetType : OffsetType.BOTH;
    var d0 = (region.startDelayFrac != undefined) ? min(max(region.startDelayFrac, 0), 1) : 0;
    var d1 = (region.endDelayFrac   != undefined) ? min(max(region.endDelayFrac,   0), 1) : 0;

    var nD = [0 * meter, 0 * meter];
    if (ot == OffsetType.NORMAL || ot == OffsetType.BOTH)
    {
        nD = componentDerivatives(getComponentCPs(region, "normal"), alpha, alphaProbe, region.regionType, region.quadZeroSlope, d0, d1);
    }
    var bD = [0 * meter, 0 * meter];
    if (ot == OffsetType.BINORMAL || ot == OffsetType.BOTH)
    {
        bD = componentDerivatives(getComponentCPs(region, "binormal"), alpha, alphaProbe, region.regionType, region.quadZeroSlope, d0, d1);
    }
    if (nD == undefined || bD == undefined)
    {
        return undefined;
    }

    var off = computeOffsetsAt(region, tPath);
    return {
        "normalOff"     : off.normalOff,
        "binormalOff"   : off.binormalOff,
        "normalSlope"   : nD[0] / span,
        "binormalSlope" : bD[0] / span,
        "normalCurv"    : nD[1] / (span * span),
        "binormalCurv"  : bD[1] / (span * span)
    };
}


/**
 * The derivatives by finite differences in t-space (the only method before 2026-09-26).
 *
 * The step is confined to the smooth segment containing tPath (bounded by getBreakpointsT),
 * so kinks at stations and dwell-plateau edges never contaminate the slope/curvature that the
 * blend logic consumes.
 */
export function finiteDifferenceOffsetDerivativesAt(region is map, tPath is number) returns map
{
    var dt  = 1e-4;
    var bps = getBreakpointsT(region);

    // Open segment (segLo, segHi) strictly containing tPath.
    var segLo   = region.tStart;
    var segHi   = region.tEnd;
    var onBreak = false;
    for (var bp in bps)
    {
        if (abs(bp - tPath) < 1e-9)
        {
            onBreak = true;
        }
        else if (bp < tPath && bp > segLo)
        {
            segLo = bp;
        }
        else if (bp > tPath && bp < segHi)
        {
            segHi = bp;
        }
    }

    var offMid = computeOffsetsAt(region, tPath);
    var h      = min(dt, min(tPath - segLo, segHi - tPath));

    if (onBreak || h <= 1e-9)
    {
        // tPath sits on a kink; step one-sided into the roomier neighbor segment.
        var roomHi = segHi - tPath;
        var roomLo = tPath - segLo;
        var dir    = (roomHi >= roomLo) ? 1 : -1;
        var hh     = min(dt, (dir > 0) ? roomHi : roomLo);
        if (hh <= 1e-12)
        {
            hh = dt;   // fully degenerate region; fall back so we never divide by ~0
        }
        var offA = offMid;
        var offB = computeOffsetsAt(region, tPath + dir * hh);
        var offC = computeOffsetsAt(region, tPath + dir * 2 * hh);
        return {
            "normalOff"     : offMid.normalOff,
            "binormalOff"   : offMid.binormalOff,
            "normalSlope"   : dir * (offB.normalOff   - offA.normalOff)   / hh,
            "binormalSlope" : dir * (offB.binormalOff - offA.binormalOff) / hh,
            "normalCurv"    : (offC.normalOff   - 2 * offB.normalOff   + offA.normalOff)   / (hh * hh),
            "binormalCurv"  : (offC.binormalOff - 2 * offB.binormalOff + offA.binormalOff) / (hh * hh)
        };
    }

    var offHi = computeOffsetsAt(region, tPath + h);
    var offLo = computeOffsetsAt(region, tPath - h);
    return {
        "normalOff"     : offMid.normalOff,
        "binormalOff"   : offMid.binormalOff,
        "normalSlope"   : (offHi.normalOff   - offLo.normalOff)   / (2 * h),
        "binormalSlope" : (offHi.binormalOff - offLo.binormalOff) / (2 * h),
        "normalCurv"    : (offHi.normalOff   - 2 * offMid.normalOff   + offLo.normalOff)   / (h * h),
        "binormalCurv"  : (offHi.binormalOff - 2 * offMid.binormalOff + offLo.binormalOff) / (h * h)
    };
}

// --- Point arrays -------------------------------------------------------------

/**
 * Samples points along [tSegStart, tSegEnd], evaluating the region profile at each, and
 * returns { points, interpolateIndices }. Sample nodes are forced to land exactly on every
 * interior profile breakpoint (station or dwell-plateau edge) inside the segment, and those
 * node indices are returned so approximateSpline pins them. Stations only SHAPE and CONSTRAIN
 * the fit here; they do not split the output curve (edge-boundary splitting is separate).
 * tSegStart/tSegEnd may differ from region.tStart/tEnd when trimmed by an adjacent blend.
 */
export function generateSegmentPoints(context is Context, pathInfo is map, definition is map,
    region is map, tSegStart is number, tSegEnd is number) returns map
{
    // At least one sample every SAMPLE_STEP of path: a fixed count left fits up to ~0.02 mm off the
    // true offset between samples on long regions (tests OE10 / OE20, 2026-09-25).
    var nTotal = samplesFor(definition, (tSegEnd - tSegStart) * pathInfo.length);
    var minPer = max([2, definition.approxDegree + 1]);

    // Interior breakpoints strictly inside this sub-curve (getBreakpointsT is sorted ascending).
    var bounds = [tSegStart];
    for (var bp in getBreakpointsT(region))
    {
        if (bp > tSegStart + 1e-9 && bp < tSegEnd - 1e-9 && bp - bounds[size(bounds) - 1] > 1e-9)
        {
            bounds = append(bounds, bp);
        }
    }
    bounds = append(bounds, tSegEnd);

    var nSub      = size(bounds) - 1;
    var totalSpan = tSegEnd - tSegStart;

    var ts        = [];
    var offs      = [];
    var interpIdx = [];

    for (var s = 0; s < nSub; s += 1)
    {
        var a    = bounds[s];
        var b    = bounds[s + 1];
        var frac = (totalSpan > 1e-12) ? (b - a) / totalSpan : (1.0 / nSub);
        var nSeg = max([minPer, floor(nTotal * frac + 0.5)]);

        // First sub-interval contributes its start node; later ones share the previous end node.
        var startI = (s == 0) ? 0 : 1;
        for (var i = startI; i < nSeg; i += 1)
        {
            var t = a + (b - a) * i / (nSeg - 1);
            ts   = append(ts, t);
            offs = append(offs, computeOffsetsAt(region, t));
        }

        // The node just added at b is an interior breakpoint (pin it) unless it is segEnd.
        if (s < nSub - 1)
        {
            interpIdx = append(interpIdx, size(ts) - 1);
        }
    }
    // All nodes lie on the piece's source edge: one kernel call for the lot (2026-09-26).
    var points = computeOffsetPoints(context, pathInfo, definition, ts, offs);

    var finalInterp = [0];
    for (var idx in interpIdx)
    {
        finalInterp = append(finalInterp, idx);
    }
    finalInterp = append(finalInterp, size(points) - 1);

    return { "points" : points, "interpolateIndices" : finalInterp };
}


/**
 * Evaluates the Hermite blend polynomial at s in [0,1] for a single scalar offset component.
 *
 * All slope/curvature args are in s-space (multiply region dOffset/dt by L, d^2Offset/dt^2 by L^2
 * before calling, where L = tBlendEnd - tBlendStart).
 *
 * Continuity dispatch:
 *   G0+G0 -> linear          G1+G0 / G0+G1 -> quadratic
 *   G1+G1 -> cubic Hermite   G2+G0 / G0+G2 -> cubic
 *   G2+G1 / G1+G2 -> quartic               G2+G2 -> quintic Hermite
 */
export function blendOffsetAt(s is number,
    h0 is ValueWithUnits, m0 is ValueWithUnits, k0 is ValueWithUnits,
    h1 is ValueWithUnits, m1 is ValueWithUnits, k1 is ValueWithUnits,
    contStart, contEnd) returns ValueWithUnits
{
    var matchSlopeStart = (contStart == GeometricContinuity.G1 || contStart == GeometricContinuity.G2);
    var matchCurvStart  = (contStart == GeometricContinuity.G2);
    var matchSlopeEnd   = (contEnd   == GeometricContinuity.G1 || contEnd   == GeometricContinuity.G2);
    var matchCurvEnd    = (contEnd   == GeometricContinuity.G2);

    var s2 = s * s;
    var s3 = s2 * s;
    var s4 = s3 * s;
    var s5 = s4 * s;

    if (!matchSlopeStart && !matchSlopeEnd)
    {
        // G0+G0: linear
        return h0 * (1 - s) + h1 * s;
    }
    else if (matchSlopeStart && !matchCurvStart && !matchSlopeEnd)
    {
        // G1+G0: quadratic -- h0, m0, h1
        return h0 + m0 * s + (h1 - h0 - m0) * s2;
    }
    else if (!matchSlopeStart && matchSlopeEnd && !matchCurvEnd)
    {
        // G0+G1: quadratic -- h0, h1, m1
        var dh = h1 - h0;
        return h0 + (2 * dh - m1) * s + (m1 - dh) * s2;
    }
    else if (matchSlopeStart && !matchCurvStart && matchSlopeEnd && !matchCurvEnd)
    {
        // G1+G1: cubic Hermite -- h0, m0, h1, m1
        return h0 * (2*s3 - 3*s2 + 1) + m0 * (s3 - 2*s2 + s)
             + h1 * (-2*s3 + 3*s2)    + m1 * (s3 - s2);
    }
    else if (matchCurvStart && !matchSlopeEnd)
    {
        // G2+G0: cubic -- h0, m0, k0, h1
        return h0 + m0 * s + (k0 / 2) * s2 + (h1 - h0 - m0 - k0 / 2) * s3;
    }
    else if (!matchSlopeStart && matchCurvEnd)
    {
        // G0+G2: cubic -- h0, h1, m1, k1
        var dh = h1 - h0;
        var d  = dh - m1 + k1 / 2;
        var c  = -(k1 + 3 * (dh - m1));
        var b  = 3 * dh - 2 * m1 + k1 / 2;
        return h0 + b * s + c * s2 + d * s3;
    }
    else if (matchCurvStart && matchSlopeEnd && !matchCurvEnd)
    {
        // G2+G1: quartic -- h0, m0, k0, h1, m1
        var dh = h1 - h0;
        var a4 = m1 + 2 * m0 + k0 / 2 - 3 * dh;
        var a3 = 4 * dh - 3 * m0 - k0 - m1;
        return h0 + m0 * s + (k0 / 2) * s2 + a3 * s3 + a4 * s4;
    }
    else if (matchSlopeStart && !matchCurvStart && matchCurvEnd)
    {
        // G1+G2: quartic -- h0, m0, h1, m1, k1
        var H  = h1 - h0 - m0;
        var M  = m1 - m0;
        var K  = k1;
        var e  = (K - 4 * M + 6 * H) / 2;
        var d  = 5 * M - 8 * H - K;
        var c  = 6 * H - 3 * M + K / 2;
        return h0 + m0 * s + c * s2 + d * s3 + e * s4;
    }
    else
    {
        // G2+G2: quintic Hermite -- h0, m0, k0, h1, m1, k1
        return h0 * (1 - 10*s3 + 15*s4 - 6*s5)
             + m0 * (s - 6*s3 + 8*s4 - 3*s5)
             + k0 * (s2/2 - 3*s3/2 + 3*s4/2 - s5/2)
             + h1 * (10*s3 - 15*s4 + 6*s5)
             + m1 * (-4*s3 + 7*s4 - 3*s5)
             + k1 * (s3/2 - s4 + s5/2);
    }
}


/**
 * The offsets across a blend zone [tBlendStart, tBlendEnd] as a zone (see [regionZone]):
 * normal and binormal offsets blended independently via blendOffsetAt, from the heights (and
 * slopes / curvatures the continuity asks for) of the two regions at the blend ends -- the
 * actual tPath positions, not the region endpoints, so the blend meets the trimmed regions.
 */
export function blendZone(pathInfo is map, definition is map, bz is map) returns map
{
    var tBlendStart = bz.tBlendStart;
    var tBlendEnd   = bz.tBlendEnd;
    var regA = bz.regA;
    var regB = bz.regB;
    var L = tBlendEnd - tBlendStart;
    var contStart = bz.intr.startContinuity;
    var contEnd   = bz.intr.endContinuity;

    var needSlopeStart = (contStart == GeometricContinuity.G1 || contStart == GeometricContinuity.G2);
    var needSlopeEnd   = (contEnd   == GeometricContinuity.G1 || contEnd   == GeometricContinuity.G2);
    var needCurvStart  = (contStart == GeometricContinuity.G2);
    var needCurvEnd    = (contEnd   == GeometricContinuity.G2);

    var nOff0 = 0 * meter; var nSlope0 = 0 * meter; var nCurv0 = 0 * meter;
    var bOff0 = 0 * meter; var bSlope0 = 0 * meter; var bCurv0 = 0 * meter;
    var nOff1 = 0 * meter; var nSlope1 = 0 * meter; var nCurv1 = 0 * meter;
    var bOff1 = 0 * meter; var bSlope1 = 0 * meter; var bCurv1 = 0 * meter;

    if (needSlopeStart || needCurvStart)
    {
        var dA   = computeOffsetDerivativesAt(regA, tBlendStart);
        nOff0    = dA.normalOff;
        bOff0    = dA.binormalOff;
        nSlope0  = dA.normalSlope   * L;
        bSlope0  = dA.binormalSlope * L;
        if (needCurvStart)
        {
            nCurv0 = dA.normalCurv   * L * L;
            bCurv0 = dA.binormalCurv * L * L;
        }
    }
    else
    {
        var offA = computeOffsetsAt(regA, tBlendStart);
        nOff0 = offA.normalOff;
        bOff0 = offA.binormalOff;
    }

    if (needSlopeEnd || needCurvEnd)
    {
        var dB   = computeOffsetDerivativesAt(regB, tBlendEnd);
        nOff1    = dB.normalOff;
        bOff1    = dB.binormalOff;
        nSlope1  = dB.normalSlope   * L;
        bSlope1  = dB.binormalSlope * L;
        if (needCurvEnd)
        {
            nCurv1 = dB.normalCurv   * L * L;
            bCurv1 = dB.binormalCurv * L * L;
        }
    }
    else
    {
        var offB = computeOffsetsAt(regB, tBlendEnd);
        nOff1 = offB.normalOff;
        bOff1 = offB.binormalOff;
    }

    return {
        "offsetsAt" : function(t)
            {
                var s = (t - tBlendStart) / L;
                return {
                    "normalOff"   : blendOffsetAt(s, nOff0, nSlope0, nCurv0, nOff1, nSlope1, nCurv1, contStart, contEnd),
                    "binormalOff" : blendOffsetAt(s, bOff0, bSlope0, bCurv0, bOff1, bSlope1, bCurv1, contStart, contEnd)
                };
            },
        "tLo" : tBlendStart,
        "tHi" : tBlendEnd
    };
}


// --- Wire construction --------------------------------------------------------

// True when the source edge covering path parameter t is a circular arc.
export function sourceEdgeIsArc(context is Context, frenetPath is map, t is number, totalLength is ValueWithUnits) returns boolean
{
    var edgeData = frenetPath.edgeData;
    var n        = size(edgeData);
    var arcLen   = t * totalLength;

    var ei = n - 1;
    for (var i = 0; i < n - 1; i += 1)
    {
        if (edgeData[i + 1].startArcLength > arcLen)
        {
            ei = i;
            break;
        }
    }

    // Cached per edge by processPath when keeping arcs (2026-09-26; was one evCurveDefinition per piece).
    return edgeCurveInfo(context, edgeData[ei]).curveType == CurveType.CIRCLE;
}


// The offsets of one zone (a region or a blend) as a function of path parameter t:
// function(t) returns { "normalOff", "binormalOff" }, plus the zone's extent [tLo, tHi]
// (tangents are read one-sided at its ends).
export function regionZone(region is map) returns map
{
    return {
        "offsetsAt" : function(t) { return computeOffsetsAt(region, t); },
        "tLo"       : region.tStart,
        "tHi"       : region.tEnd
    };
}


// A zone's offsets at path parameter t.
export function zoneOffsets(zone is map, t is number) returns map
{
    const offsetsAt = zone.offsetsAt;
    return offsetsAt(t);
}


// True when both offset components of a zone are constant (within 1 um) across [tA, tB]; a
// constant offset over a circular source edge is exactly a concentric / translated arc.
export function zoneOffsetConstant(zone is map, tA is number, tB is number) returns boolean
{
    var o0 = zoneOffsets(zone, tA);
    for (var i = 1; i < 5; i += 1)
    {
        var o = zoneOffsets(zone, tA + (tB - tA) * i / 4);
        if (abs(o.normalOff - o0.normalOff) > 1e-6 * meter || abs(o.binormalOff - o0.binormalOff) > 1e-6 * meter)
        {
            return false;
        }
    }
    return true;
}


// World points of a zone's offset curve at path parameters ts (one kernel call per source edge touched).
export function zonePoints(context is Context, pathInfo is map, definition is map, zone is map, ts is array) returns array
{
    var offs = [];
    for (var t in ts)
    {
        offs = append(offs, zoneOffsets(zone, t));
    }
    return computeOffsetPoints(context, pathInfo, definition, ts, offs);
}


// World point of a zone's offset curve at path parameter t.
export function zonePoint(context is Context, pathInfo is map, definition is map, zone is map, t is number) returns Vector
{
    return zonePoints(context, pathInfo, definition, zone, [t])[0];
}


// Unit tangent of a zone's offset curve at t, in the direction of travel: central difference
// inside the zone, second-order one-sided (-3 P0 + 4 P1 - P2) at its ends, so the two pieces
// meeting at a joint read the same tangent to O(h^2); the frame tangent if the offset curve is
// momentarily stationary.
export function zoneTangent(context is Context, pathInfo is map, definition is map, zone is map, t is number) returns Vector
{
    var h = 1e-4;
    var dir;
    if (t - h >= zone.tLo && t + h <= zone.tHi)
    {
        var pc = zonePoints(context, pathInfo, definition, zone, [t + h, t - h]);
        dir = pc[0] - pc[1];
    }
    else
    {
        var sgn = (t + 2 * h <= zone.tHi) ? 1 : -1;
        var po  = zonePoints(context, pathInfo, definition, zone, [t, t + sgn * h, t + sgn * 2 * h]);
        dir = sgn * (4 * po[1] - 3 * po[0] - po[2]);
    }
    var dLen = norm(dir);
    if (dLen / meter < 1e-12)
    {
        return sampleParallelTransportFrame(context, pathInfo.frenetPath, pathInfo.ptTable, t * pathInfo.length, pathInfo.pinEdge).frame.zAxis;
    }
    return dir / dLen;
}


// Curvature vector (toward the centre, magnitude 1/R) of a zone's offset curve at t, from second
// differences taken inward from the zone's ends (central inside): (P'' - (P''.T) T) / |P'|^2.
export function zoneCurvatureVector(context is Context, pathInfo is map, definition is map, zone is map, t is number) returns Vector
{
    var h = 1e-4;
    var a;
    if (t - h >= zone.tLo && t + h <= zone.tHi)
    {
        a = t - h;
    }
    else if (t + 2 * h <= zone.tHi)
    {
        a = t;
    }
    else
    {
        a = t - 2 * h;
    }
    var pc = zonePoints(context, pathInfo, definition, zone, [a, a + h, a + 2 * h]);
    var p0 = pc[0];
    var p1 = pc[1];
    var p2 = pc[2];
    var d1 = (p2 - p0) / (2 * h);
    var d2 = (p2 - 2 * p1 + p0) / (h * h);
    var speed2 = dot(d1, d1);
    if (speed2 < 1e-24 * meter * meter)
    {
        return vector(0, 0, 0) / meter;
    }
    var tangent = d1 / sqrt(speed2);
    return (d2 - dot(d2, tangent) * tangent) / speed2;
}


// The circular arc that STARTS at pStart travelling along tStartOut (unit, outgoing tangent)
// and ENDS at pEnd. Returns { straight, center, radius, e0, yA, sweep, nTravel }: the arc is
// center + radius * (cos(a) e0 + sin(a) yA) for a in [0, sweep], travelled with increasing a.
// straight = true when the chord is parallel to the tangent (no finite arc).
export function arcFromStart(pStart is Vector, tStartOut is Vector, pEnd is Vector) returns map
{
    const c    = pEnd - pStart;
    const cLen = norm(c);
    if (cLen / meter < 1e-12)
    {
        return { "straight" : true };
    }
    const sinTheta = norm(cross(tStartOut, c / cLen));
    if (sinTheta < 1e-7)
    {
        return { "straight" : true };
    }

    // Plane of the arc, and the in-plane unit normal to the tangent.
    const nPlane = normalize(cross(tStartOut, c));
    const mPerp  = normalize(cross(nPlane, tStartOut));

    // Center on the perpendicular through pStart, equidistant from pStart and pEnd:
    //   |s*mPerp|^2 = |s*mPerp - c|^2  =>  s = |c|^2 / (2 * mPerp . c).
    const s      = dot(c, c) / (2 * dot(mPerp, c));
    const center = pStart + s * mPerp;
    const R      = abs(s);

    // Circle frame oriented so travel from pStart along tStartOut is CCW (+) about nTravel.
    const e0      = (pStart - center) / R;
    const nTravel = normalize(cross(e0, tStartOut));
    const yA      = cross(nTravel, e0);

    // Signed sweep from pStart (angle 0) to pEnd, on the positive (traveled) branch.
    const ve = pEnd - center;
    var sweep = atan2(dot(ve, yA) / meter, dot(ve, e0) / meter) / radian;
    if (sweep <= 0)
    {
        sweep = sweep + 2 * PI;
    }
    return { "straight" : false, "center" : center, "radius" : R, "e0" : e0, "yA" : yA,
             "sweep" : sweep, "nTravel" : nTravel };
}


// Point at the mid of the arc from [arcFromStart] -- on the travelled branch, so it uniquely
// reconstructs the intended arc via a 3-point skArc. Returns { "mid", "straight" }; a
// straight arc returns the chord midpoint.
export function biarcArcMid(pStart is Vector, tStartOut is Vector, pEnd is Vector) returns map
{
    const arc = arcFromStart(pStart, tStartOut, pEnd);
    if (arc.straight)
    {
        return { "mid" : pStart + 0.5 * (pEnd - pStart), "straight" : true };
    }
    const midAng = (arc.sweep / 2) * radian;
    return { "mid" : arc.center + arc.radius * (cos(midAng) * arc.e0 + sin(midAng) * arc.yA), "straight" : false };
}


// Distance from point p to an arc from [arcFromStart] (to its end points when p lies outside
// the swept angle).
export function distanceToArc(arc is map, pStart is Vector, pEnd is Vector, p is Vector) returns ValueWithUnits
{
    const v      = p - arc.center;
    const normal = dot(v, arc.nTravel);
    const inPl   = v - normal * arc.nTravel;
    var ang = atan2(dot(inPl, arc.yA) / meter, dot(inPl, arc.e0) / meter) / radian;
    if (ang < 0)
    {
        ang = ang + 2 * PI;
    }
    if (ang <= arc.sweep)
    {
        const radial = norm(inPl) - arc.radius;
        return sqrt(radial * radial + normal * normal);
    }
    return min(norm(p - pStart), norm(p - pEnd));
}


// The G1 biarc from p0 (tangent t0) to p1 (tangent t1), unit tangents in the direction of
// travel, with tangent-length ratio r = a / b (Q0 = p0 + a t0, Q1 = p1 - b t1,
// |Q1 - Q0| = a + b, joint J on Q0Q1 at a : b). Every such biarc matches both end points and
// both end tangents; r only moves the joint. r = 1 is the equal-tangent-length biarc.
//   |d - b (r t0 + t1)| = b (r + 1)  =>  2 r (1 - c) b^2 + 2 D b - |d|^2 = 0,
// with d = p1 - p0, c = t0 . t1, D = d . (r t0 + t1).
// Returns { ok, joint, tJoint }.
export function biarcWithRatio(p0 is Vector, t0 is Vector, p1 is Vector, t1 is Vector, r is number) returns map
{
    const fail = { "ok" : false };
    const d    = p1 - p0;
    const dd   = dot(d, d);
    if (sqrt(dd) / meter < 1e-9)
    {
        return fail;
    }
    const c  = dot(t0, t1);
    const qa = 2 * r * (1 - c);
    const D  = dot(d, r * t0 + t1);
    var b;
    if (qa < 1e-12)
    {
        if (D / meter < 1e-12)
        {
            return fail;
        }
        b = dd / (2 * D);
    }
    else
    {
        b = (-D + sqrt(D * D + qa * dd)) / qa;
    }
    if (b / meter < 1e-12)
    {
        return fail;
    }
    const a  = r * b;
    const Q0 = p0 + a * t0;
    const Q1 = p1 - b * t1;
    const jm = Q1 - Q0;
    if (norm(jm) / meter < 1e-12)
    {
        return fail;
    }
    const J = (b * Q0 + a * Q1) / (a + b);
    if (norm(J - p0) / meter < 1e-9 || norm(p1 - J) / meter < 1e-9)
    {
        return fail;
    }
    return { "ok" : true, "joint" : J, "tJoint" : normalize(jm) };
}


/** Shortest biarc leg, as a fraction of the span's chord. */
export const BIARC_MIN_LEG = 0.2;

// Largest distance from the sample points to the biarc; inf when the biarc is degenerate
// (a straight leg cannot be emitted as an arc).
export function biarcDeviation(p0 is Vector, t0 is Vector, p1 is Vector, bi is map, samples is array) returns ValueWithUnits
{
    if (!bi.ok)
    {
        return inf * meter;
    }
    // Each leg at least BIARC_MIN_LEG of the span: on a flat arc the closest pair otherwise
    // puts the joint a few mm from one end, a leg too short and straight to be an arc.
    const span = norm(p1 - p0);
    if (norm(bi.joint - p0) < BIARC_MIN_LEG * span || norm(p1 - bi.joint) < BIARC_MIN_LEG * span)
    {
        return inf * meter;
    }
    const arcA = arcFromStart(p0, t0, bi.joint);
    const arcB = arcFromStart(bi.joint, bi.tJoint, p1);
    if (arcA.straight || arcB.straight)
    {
        return inf * meter;
    }
    var worst = 0 * meter;
    for (var p in samples)
    {
        const dist = min(distanceToArc(arcA, p0, bi.joint, p), distanceToArc(arcB, bi.joint, p1, p));
        if (dist > worst)
        {
            worst = dist;
        }
    }
    return worst;
}


// The G1 biarc from (p0, t0) to (p1, t1) that stays closest to the true offset curve (the
// sample points): the joint is placed by minimizing the largest deviation over the
// tangent-length ratio (coarse scan in log r, then golden-section refinement). Every
// candidate keeps both end points and both end tangents exact.
// Returns { ok, joint, midA, midB, deviation }.
export function computeBiarcPoints(p0 is Vector, t0 is Vector, p1 is Vector, t1 is Vector, samples is array) returns map
{
    const LOG_MIN = -3;
    const LOG_MAX = 3;
    const N_SCAN  = 13;

    var bestU   = 0;
    var bestDev = biarcDeviation(p0, t0, p1, biarcWithRatio(p0, t0, p1, t1, 1), samples);
    for (var i = 0; i < N_SCAN; i += 1)
    {
        const u   = LOG_MIN + (LOG_MAX - LOG_MIN) * i / (N_SCAN - 1);
        const dev = biarcDeviation(p0, t0, p1, biarcWithRatio(p0, t0, p1, t1, exp(u)), samples);
        if (dev < bestDev)
        {
            bestDev = dev;
            bestU   = u;
        }
    }
    if (bestDev == inf * meter)
    {
        var at1 = biarcWithRatio(p0, t0, p1, t1, 1);
        return { "ok" : false, "reason" : at1.ok ? "every joint placement leaves a straight leg" : "degenerate end points / tangents" };
    }

    // Golden-section refinement on the scan cell around the best sample.
    const cell   = (LOG_MAX - LOG_MIN) / (N_SCAN - 1);
    const golden = (sqrt(5) - 1) / 2;
    var lo = bestU - cell;
    var hi = bestU + cell;
    var x1 = hi - golden * (hi - lo);
    var x2 = lo + golden * (hi - lo);
    var f1 = biarcDeviation(p0, t0, p1, biarcWithRatio(p0, t0, p1, t1, exp(x1)), samples);
    var f2 = biarcDeviation(p0, t0, p1, biarcWithRatio(p0, t0, p1, t1, exp(x2)), samples);
    for (var k = 0; k < 16; k += 1)
    {
        if (f1 < f2)
        {
            hi = x2; x2 = x1; f2 = f1;
            x1 = hi - golden * (hi - lo);
            f1 = biarcDeviation(p0, t0, p1, biarcWithRatio(p0, t0, p1, t1, exp(x1)), samples);
        }
        else
        {
            lo = x1; x1 = x2; f1 = f2;
            x2 = lo + golden * (hi - lo);
            f2 = biarcDeviation(p0, t0, p1, biarcWithRatio(p0, t0, p1, t1, exp(x2)), samples);
        }
    }
    if (min(f1, f2) < bestDev)
    {
        bestU   = (f1 < f2) ? x1 : x2;
        bestDev = min(f1, f2);
    }

    const bi = biarcWithRatio(p0, t0, p1, t1, exp(bestU));
    return {
        "ok"        : true,
        "joint"     : bi.joint,
        "midA"      : biarcArcMid(p0, t0, bi.joint).mid,
        "midB"      : biarcArcMid(bi.joint, bi.tJoint, p1).mid,
        "deviation" : bestDev
    };
}


// True when three points define an arc: the angle at pStart between the chords to pMid and to
// pEnd is not ~0. Relative, so a short leg of a very flat arc (4 mm on R 15 m bends by
// ~7e-5) still counts.
export function arcPointsBend(pStart is Vector, pMid is Vector, pEnd is Vector) returns boolean
{
    var a = pMid - pStart;
    var b = pEnd - pStart;
    var la = norm(a);
    var lb = norm(b);
    if (la / meter < 1e-12 || lb / meter < 1e-12)
    {
        return false;
    }
    return norm(cross(a, b)) / (la * lb) > 1e-7;
}


// Builds a sketch arc through 3 world points on their common plane, under arcId. The sketch
// is what makes Onshape treat the output as an arc (clicking it shows a radius); a BSpline
// with the same shape reads as a spline. Returns qCreatedBy(arcId, EntityType.BODY), or
// undefined if the points are collinear (no unique arc) so the caller can fall back.
export function emitArc3Point(context is Context, arcId is Id, pStart is Vector, pMid is Vector, pEnd is Vector)
{
    var nrm = cross(pMid - pStart, pEnd - pStart);
    if (!arcPointsBend(pStart, pMid, pEnd))
    {
        return undefined;
    }

    var pl = plane(pStart, normalize(nrm));
    var sk = newSketchOnPlane(context, arcId, { "sketchPlane" : pl });
    skArc(sk, "arc", {
        "start" : worldToPlane(pl, pStart),
        "mid"   : worldToPlane(pl, pMid),
        "end"   : worldToPlane(pl, pEnd)
    });
    skSolve(sk);

    return qCreatedBy(arcId, EntityType.BODY);
}


/** Interior points on which a best-fit biarc's deviation from the true offset is measured. */
export const BIARC_DEVIATION_SAMPLES = 10;


// The arc output for [tA, tB] of a zone lying on one circular source edge:
//   - constant offset -> the exact concentric / translated arc (3-point sketch arc);
//   - varying offset  -> two tangent sketch arcs matching both end points and both end
//                        tangents of the true offset curve, the joint placed where the pair
//                        stays closest to it.
// Returns { ok, bodies, edges, isPair, deviation }; ok = false builds nothing (degenerate
// geometry), and the caller fits a spline instead.
export function emitSourceArc(context is Context, wireId is Id, pathInfo is map, definition is map,
    zone is map, tA is number, tB is number) returns map
{
    var none = { "ok" : false };
    var ends = zonePoints(context, pathInfo, definition, zone, [tA, tB]);
    var p0 = ends[0];
    var p1 = ends[1];

    if (zoneOffsetConstant(zone, tA, tB))
    {
        var pM   = zonePoint(context, pathInfo, definition, zone, (tA + tB) / 2);
        var body = emitArc3Point(context, wireId, p0, pM, p1);
        if (body == undefined)
        {
            if (definition.printCurveDetails)
            {
                println("  constant-offset arc: the three points are collinear -> spline");
            }
            return none;
        }
        return { "ok" : true, "bodies" : [body], "edges" : [qCreatedBy(wireId, EntityType.EDGE)],
                 "isPair" : false, "deviation" : 0 * meter };
    }

    var t0 = zoneTangent(context, pathInfo, definition, zone, tA);
    var t1 = zoneTangent(context, pathInfo, definition, zone, tB);
    var trueTs = [];
    for (var k = 1; k < BIARC_DEVIATION_SAMPLES; k += 1)
    {
        trueTs = append(trueTs, tA + (tB - tA) * k / BIARC_DEVIATION_SAMPLES);
    }
    var truePts = zonePoints(context, pathInfo, definition, zone, trueTs);
    var bi = computeBiarcPoints(p0, t0, p1, t1, truePts);

    // Both legs must be real arcs (same threshold emitArc3Point uses) before anything is built,
    // so a failure leaves no orphan sketch.
    var legsOk = bi.ok && arcPointsBend(p0, bi.midA, bi.joint) && arcPointsBend(bi.joint, bi.midB, p1);
    if (!legsOk)
    {
        if (definition.printCurveDetails)
        {
            println("  varying-offset arc pair not possible (" ~ (bi.ok ? "a leg is straight" : "no biarc: " ~ toString(bi.reason)) ~ ") -> spline");
        }
        return none;
    }
    var idA = wireId + "A";
    var idB = wireId + "B";
    emitArc3Point(context, idA, p0, bi.midA, bi.joint);
    emitArc3Point(context, idB, bi.joint, bi.midB, p1);
    return { "ok" : true,
             "bodies" : [qCreatedBy(idA, EntityType.BODY), qCreatedBy(idB, EntityType.BODY)],
             "edges"  : [qCreatedBy(idA, EntityType.EDGE), qCreatedBy(idB, EntityType.EDGE)],
             "isPair" : true, "deviation" : bi.deviation };
}


// Fits and creates one spline piece with both ends pinned to the true offset tangent of its
// zone. Neighbouring pieces (the next source edge, a blend and its regions, an arc) each pin
// their own zone's tangent at the shared point, and those agree wherever the offset is meant
// to be smooth (G1 / G2 blends, G1 source edges), so the joint is tangent by construction
// rather than to fit accuracy (~0.05 deg unpinned).
export function emitSplinePiece(context is Context, wireId is Id, pathInfo is map, definition is map,
    zone is map, tA is number, tB is number, pts is array, interpolateIndices is array, maxCP is number) returns Query
{
    var chord = 0 * meter;
    for (var k = 0; k < size(pts) - 1; k += 1)
    {
        chord += norm(pts[k + 1] - pts[k]);
    }
    // End tangents / curvature are differenced INSIDE this piece: a piece ends where the profile
    // kinks (dwell edge, linear station), and a difference across the kink is meaningless there.
    var pieceZone = mergeMaps(zone, { "tLo" : max(zone.tLo, tA), "tHi" : min(zone.tHi, tB) });
    zone = pieceZone;
    // Ends pinned to the true offset's tangent AND curvature (second derivative ~ K * chord^2 for a
    // parameter running 0..1 over the chord), so pieces meeting at a G2 blend or a smooth source
    // joint agree in curvature too (they only matched tangents before: a 5-12% curvature jump at G2
    // blend joints, test OE7, 2026-09-25).
    var target = {
        "positions"       : pts,
        "startDerivative" : zoneTangent(context, pathInfo, definition, zone, tA) * chord,
        "endDerivative"   : zoneTangent(context, pathInfo, definition, zone, tB) * chord
    };
    // A straight piece has no curvature to match, and pinning zero second derivatives on collinear
    // points makes approximateSpline fail ("Failed to compute spline", test OE1): tangents only there.
    var kA = zoneCurvatureVector(context, pathInfo, definition, zone, tA);
    var kB = zoneCurvatureVector(context, pathInfo, definition, zone, tB);
    if (norm(kA) * chord > 1e-6 || norm(kB) * chord > 1e-6)
    {
        target.start2ndDerivative = kA * chord * chord;
        target.end2ndDerivative   = kB * chord * chord;
    }
    var bspline = approximateSpline(context, {
        "degree"             : definition.approxDegree,
        "tolerance"          : definition.approxTolerance,
        "isPeriodic"         : false,
        "maxControlPoints"   : maxCP,
        "targets"            : [approximationTarget(target)],
        "interpolateIndices" : interpolateIndices
    })[0];
    opCreateBSplineCurve(context, wireId, { "bSplineCurve" : bspline });
    if (definition.printCurveDetails)
    {
        println("  degree " ~ toString(bspline.degree) ~ "  CPs " ~ toString(size(bspline.controlPoints))
            ~ "  samples " ~ toString(size(pts)) ~ "  t [" ~ toString(tA) ~ ", " ~ toString(tB) ~ "]");
    }
    return qCreatedBy(wireId, EntityType.BODY);
}


// Fit samples for a stretch of path: the user's count, raised to one every SAMPLE_STEP, capped.
export const SAMPLE_STEP    = 2 * millimeter;
export const SAMPLE_MAX     = 400;

export function samplesFor(definition is map, length is ValueWithUnits) returns number
{
    return min([SAMPLE_MAX, max([definition.numRegionPoints, ceil(length / SAMPLE_STEP) + 1])]);
}


// Interior breakpoints of a region where its profile has a KINK (a slope jump in either
// component): dwell-plateau edges and stations between linear segments. The output splits
// there so each piece is smooth and the kink is exact (a single spline rounded a dwell corner
// by up to 0.08 mm -- test OE8, 2026-09-25). Smooth stations are not kinks and do not split.
export function profileKinksT(region is map) returns array
{
    var kinks = [];
    var span  = region.tEnd - region.tStart;
    var h     = 1e-5 * span;
    for (var bp in getBreakpointsT(region))
    {
        if (bp <= region.tStart + 2 * h || bp >= region.tEnd - 2 * h)
        {
            continue;
        }
        var fL = computeOffsetsAt(region, bp - h);
        var f0 = computeOffsetsAt(region, bp);
        var fR = computeOffsetsAt(region, bp + h);
        var isKink = false;
        for (var comp in ["normalOff", "binormalOff"])
        {
            var dL = (f0[comp] - fL[comp]) / h;
            var dR = (fR[comp] - f0[comp]) / h;
            if (abs(dR - dL) > max(1e-4 * meter, 1e-3 * (abs(dL) + abs(dR))))
            {
                isKink = true;
            }
        }
        if (isKink)
        {
            kinks = append(kinks, bp);
        }
    }
    return kinks;
}


// Sorted, de-duplicated split points of [tStart, tEnd]: its ends plus the given ts strictly inside.
export function splitPoints(tStart is number, tEnd is number, ts is array) returns array
{
    var inner = [];
    for (var tb in ts)
    {
        if (tb > tStart + 1e-6 && tb < tEnd - 1e-6)
        {
            inner = append(inner, tb);
        }
    }
    inner = sort(inner, function(a, b) { return a - b; });
    var out = [tStart];
    for (var tb in inner)
    {
        if (tb - out[size(out) - 1] > 1e-9)
        {
            out = append(out, tb);
        }
    }
    return append(out, tEnd);
}


// The path data for one piece: frames read on the piece's own source edge.
export function piecePath(pathInfo is map, piece is map) returns map
{
    return mergeMaps(pathInfo, { "pinEdge" : piece.pinEdge });
}


// Where the offset pieces on either side of an overlapping (inside) corner cross: (u, v) with
// prev(u) == next(v), u in [uLo, tJ], v in [tJ, vHi]. Coarse grid, then Gauss-Newton on
// |prev(u) - next(v)|^2. Returns { found, u, v, gap }.
export function cornerCrossing(context is Context, pathInfo is map, definition is map, prev is map, next is map,
    tJ is number, uLo is number, vHi is number) returns map
{
    var pp = piecePath(pathInfo, prev);
    var pn = piecePath(pathInfo, next);

    // Both grids evaluated once (2 kernel calls; the nested loop re-evaluated next(v) for every u: 650).
    var n  = 24;
    var us = [];
    var vs = [];
    for (var a = 0; a <= n; a += 1)
    {
        us = append(us, tJ - (tJ - uLo) * a / n);
        vs = append(vs, tJ + (vHi - tJ) * a / n);
    }
    var pGrid = zonePoints(context, pp, definition, prev.zone, us);
    var nGrid = zonePoints(context, pn, definition, next.zone, vs);

    var best = { "d" : inf * meter, "u" : tJ, "v" : tJ };
    for (var a = 0; a <= n; a += 1)
    {
        for (var c = 0; c <= n; c += 1)
        {
            var d = norm(pGrid[a] - nGrid[c]);
            if (d < best.d)
            {
                best = { "d" : d, "u" : us[a], "v" : vs[c] };
            }
        }
    }

    var u = best.u;
    var v = best.v;
    var h = 1e-7;
    for (var it = 0; it < 30; it += 1)
    {
        // prev at u, u + h, u - h and next at v, v + h, v - h: one call per side.
        var pSet = zonePoints(context, pp, definition, prev.zone, [u, u + h, u - h]);
        var nSet = zonePoints(context, pn, definition, next.zone, [v, v + h, v - h]);
        var F  = pSet[0] - nSet[0];
        if (norm(F) < 1e-10 * meter)
        {
            break;
        }
        var Pu = (pSet[1] - pSet[2]) / (2 * h);
        var Nv = (nSet[1] - nSet[2]) / (2 * h);
        // Normal equations of [Pu, -Nv] [du, dv]^T = -F
        var a11 = dot(Pu, Pu);
        var a12 = -dot(Pu, Nv);
        var a22 = dot(Nv, Nv);
        var b1  = -dot(Pu, F);
        var b2  = dot(Nv, F);
        var det = a11 * a22 - a12 * a12;
        if (abs(det) < 1e-30 * meter ^ 4)
        {
            break;
        }
        u = min(max(u + (b1 * a22 - a12 * b2) / det, uLo), tJ);
        v = min(max(v + (a11 * b2 - a12 * b1) / det, tJ), vHi);
    }
    var gap = norm(zonePoint(context, pp, definition, prev.zone, u) - zonePoint(context, pn, definition, next.zone, v));
    return { "found" : gap < 1e-6 * meter, "u" : u, "v" : v, "gap" : gap };
}


// Emits one piece (region or blend) and returns { bodies, edges } (empty when nothing was built).
export function emitPiece(context is Context, id is Id, definition is map, pathInfo is map, piece is map, keepArcs is boolean,
    stats is box) returns map
{
    var none = { "bodies" : [], "edges" : [] };
    var pp   = piecePath(pathInfo, piece);
    var tA   = piece.tA;
    var tB   = piece.tB;
    if (tB - tA < 1e-9)
    {
        return none;
    }

    if (keepArcs && sourceEdgeIsArc(context, pathInfo.frenetPath, (tA + tB) / 2, pathInfo.length))
    {
        var arcOut = emitSourceArc(context, piece.wireId, pp, definition, piece.zone, tA, tB);
        if (arcOut.ok)
        {
            if (arcOut.isPair)
            {
                stats[].biarcCount += 1;
                stats[].biarcWorst  = max(stats[].biarcWorst, arcOut.deviation);
            }
            if (definition.printCurveDetails)
            {
                println((arcOut.isPair ? "  ARC PAIR, deviation from true offset " ~ toString(arcOut.deviation / millimeter) ~ " mm" : "  ARC")
                    ~ "  t [" ~ toString(tA) ~ ", " ~ toString(tB) ~ "]");
            }
            return { "bodies" : arcOut.bodies, "edges" : arcOut.edges };
        }
    }

    if (piece.kind == "region")
    {
        var seg = generateSegmentPoints(context, pp, definition, piece.region, tA, tB);
        if (size(seg.points) < 2)
        {
            return none;
        }
        // Ensure the CP cap can accommodate all interior pins (endpoints + stations +
        // plateau edges); otherwise approximateSpline can fail rather than just warn.
        var effMaxCP = max([definition.approxMaxCP, size(seg.interpolateIndices) + 2]);
        var body = emitSplinePiece(context, piece.wireId, pp, definition, piece.zone, tA, tB,
            seg.points, seg.interpolateIndices, effMaxCP);
        return { "bodies" : [body], "edges" : [qCreatedBy(piece.wireId, EntityType.EDGE)] };
    }

    // Blend piece
    var n   = max([definition.approxDegree + 1, samplesFor(definition, (tB - tA) * pathInfo.length)]);
    var ts  = [];
    for (var i = 0; i < n; i += 1)
    {
        ts = append(ts, tA + (tB - tA) * i / (n - 1));
    }
    var pts = zonePoints(context, pp, definition, piece.zone, ts);
    var body = emitSplinePiece(context, piece.wireId, pp, definition, piece.zone, tA, tB,
        pts, [0, size(pts) - 1], definition.approxMaxCP);
    return { "bodies" : [body], "edges" : [qCreatedBy(piece.wireId, EntityType.EDGE)] };
}


export function buildOutputWire(context is Context, id is Id, definition is map,
    pathInfo is map, sortedRegions is array)
{
    var keepArcs = definition.arcMode == VaryingArcMode.BIARC;
    var stats    = new box({ "biarcCount" : 0, "biarcWorst" : 0 * meter });

    // Collect active blend zones (SINGLE_REGION mode declares no intersections)
    var blendZones = [];
    var intersections = (definition.intersections != undefined) ? definition.intersections : [];
    for (var intr in intersections)
    {
        if (!intr.isValid || !intr.blend)
        {
            continue;
        }

        // By the regions' hidden ids; intersections saved before ids existed match by name, as before.
        var regA = undefined;
        var regB = undefined;
        for (var reg in sortedRegions)
        {
            if (intersectionRefersTo(intr, 1, reg))
            {
                regA = reg;
            }
            if (intersectionRefersTo(intr, 2, reg))
            {
                regB = reg;
            }
        }
        if (regA == undefined || regB == undefined)
        {
            continue;
        }

        var tBlendStart = regA.tEnd   - intr.startDist / pathInfo.length;
        var tBlendEnd   = regB.tStart + intr.endDist   / pathInfo.length;

        if (tBlendStart < regA.tStart)
        {
            reportFeatureWarning(context, id, "Blend start distance between '" ~ regA.regionName ~
                "' and '" ~ regB.regionName ~ "' exceeds the extent of '" ~ regA.regionName ~
                "'. Clamping to region start.");
            tBlendStart = regA.tStart;
        }
        if (tBlendEnd > regB.tEnd)
        {
            reportFeatureWarning(context, id, "Blend end distance between '" ~ regA.regionName ~
                "' and '" ~ regB.regionName ~ "' exceeds the extent of '" ~ regB.regionName ~
                "'. Clamping to region end.");
            tBlendEnd = regB.tEnd;
        }
        if (tBlendStart >= tBlendEnd)
        {
            reportFeatureWarning(context, id, "Blend zone between '" ~ regA.regionName ~
                "' and '" ~ regB.regionName ~ "' has zero or negative length after clamping. Skipping blend.");
            continue;
        }

        blendZones = append(blendZones, {
            "tBlendStart" : tBlendStart,
            "tBlendEnd"   : tBlendEnd,
            "regA"        : regA,
            "regB"        : regB,
            "intr"        : intr
        });
    }

    // Edge-boundary t-values (one per inter-edge junction) and the G0 corners among them.
    var edgeBoundaryTs = [];
    var edgeData = pathInfo.frenetPath.edgeData;
    for (var ei = 1; ei < size(edgeData); ei += 1)
    {
        edgeBoundaryTs = append(edgeBoundaryTs, edgeData[ei].startArcLength / pathInfo.length);
    }
    var corners  = pathCorners(context, pathInfo);
    var cornerTs = mapArray(corners, function(c) { return c.t; });

    // --- 1. Plan the pieces. Regions split at every source-edge boundary and at profile kinks;
    //        blends at G0 corners (and every edge boundary when keeping arcs). Every piece lies
    //        on one source edge (pinEdge) and reads its frames there.
    var pieces = [];
    for (var ri = 0; ri < size(sortedRegions); ri += 1)
    {
        var reg       = sortedRegions[ri];
        var tSegStart = reg.tStart;
        var tSegEnd   = reg.tEnd;

        for (var bz in blendZones)
        {
            if (regionKey(bz.regA) == regionKey(reg))
            {
                tSegEnd   = min(tSegEnd,   bz.tBlendStart);
            }
            if (regionKey(bz.regB) == regionKey(reg))
            {
                tSegStart = max(tSegStart, bz.tBlendEnd);
            }
        }

        if (tSegEnd - tSegStart < 1e-6)
        {
            reportFeatureWarning(context, id, "Region '" ~ reg.regionName ~
                "' was fully consumed by adjacent blend zones and produced no output. " ~
                "Reduce blend distances or expand the region extent.");
            continue;
        }

        var splitTs = splitPoints(tSegStart, tSegEnd, concatenateArrays([edgeBoundaryTs, profileKinksT(reg)]));
        for (var si = 0; si < size(splitTs) - 1; si += 1)
        {
            var tA = splitTs[si];
            var tB = splitTs[si + 1];
            if (tB - tA < 1e-6)
            {
                continue;
            }
            pieces = append(pieces, { "kind" : "region", "region" : reg, "zone" : regionZone(reg),
                        "tA" : tA, "tB" : tB, "pinEdge" : edgeIndexAt(pathInfo, (tA + tB) / 2),
                        "wireId" : id + ("reg_" ~ toString(ri) ~ "_" ~ toString(si)),
                        "colour" : (ri % 2 == 0) ? DebugColor.CYAN : DebugColor.MAGENTA, "show" : definition.showRegions });
        }
    }
    for (var bzi = 0; bzi < size(blendZones); bzi += 1)
    {
        var bz      = blendZones[bzi];
        var zone    = blendZone(pathInfo, definition, bz);
        var splitTs = splitPoints(bz.tBlendStart, bz.tBlendEnd, keepArcs ? edgeBoundaryTs : cornerTs);
        for (var si = 0; si < size(splitTs) - 1; si += 1)
        {
            var tA = splitTs[si];
            var tB = splitTs[si + 1];
            if (tB - tA < 1e-6)
            {
                continue;
            }
            pieces = append(pieces, { "kind" : "blend", "zone" : zone, "tA" : tA, "tB" : tB,
                        "pinEdge" : edgeIndexAt(pathInfo, (tA + tB) / 2),
                        "wireId" : (size(splitTs) > 2) ? id + ("blend_" ~ toString(bzi) ~ "_" ~ toString(si)) : id + ("blend_" ~ toString(bzi)),
                        "colour" : DebugColor.YELLOW, "show" : definition.showBlends });
        }
    }

    // --- 2. G0 corners: the offset opens a gap on the outside of the turn and overlaps on the
    //        inside. Gaps get a filler arc centred on the source vertex (a tangent bridge when the
    //        ends are not co-radial); overlaps trim both pieces back to their crossing.
    var fillers = [];
    for (var ci = 0; ci < size(corners); ci += 1)
    {
        var corner = corners[ci];
        var iPrev = undefined;
        var iNext = undefined;
        for (var k = 0; k < size(pieces); k += 1)
        {
            if (abs(pieces[k].tB - corner.t) < 1e-9 && pieces[k].pinEdge == corner.edge - 1)
            {
                iPrev = k;
            }
            if (abs(pieces[k].tA - corner.t) < 1e-9 && pieces[k].pinEdge == corner.edge)
            {
                iNext = k;
            }
        }
        if (iPrev == undefined || iNext == undefined)
        {
            continue;   // no output on one side of this corner
        }
        var prev = pieces[iPrev];
        var next = pieces[iNext];
        var p1 = zonePoint(context, piecePath(pathInfo, prev), definition, prev.zone, corner.t);
        var p2 = zonePoint(context, piecePath(pathInfo, next), definition, next.zone, corner.t);
        if (norm(p2 - p1) < 1e-7 * meter)
        {
            continue;   // the offset does not open or cross this corner (e.g. out of its plane)
        }

        if (dot(p2 - p1, corner.tIn + corner.tOut) > 0 * meter)
        {
            // Gap: circular arc centred on the vertex when both ends are at the same distance from it.
            var a  = p1 - corner.vertex;
            var bb = p2 - corner.vertex;
            var fillId = id + ("corner_" ~ toString(ci));
            if (abs(norm(a) - norm(bb)) < 1e-6 * meter && norm(cross(normalize(a), normalize(bb))) > 1e-9)
            {
                var mid = corner.vertex + norm(a) * normalize(normalize(a) + normalize(bb));
                if (emitArc3Point(context, fillId, p1, mid, p2) != undefined)
                {
                    fillers = append(fillers, { "bodies" : [qCreatedBy(fillId, EntityType.BODY)], "edges" : [qCreatedBy(fillId, EntityType.EDGE)] });
                    continue;
                }
            }
            // Not co-radial (the offset changes across the corner): a cubic bridge tangent to both pieces.
            var chord = norm(p2 - p1);
            var t1 = zoneTangent(context, piecePath(pathInfo, prev), definition, prev.zone, corner.t);
            var t2 = zoneTangent(context, piecePath(pathInfo, next), definition, next.zone, corner.t);
            var bridge = approximateSpline(context, {
                    "degree" : 3, "tolerance" : definition.approxTolerance, "isPeriodic" : false,
                    "targets" : [approximationTarget({ "positions" : [p1, p2], "startDerivative" : t1 * chord, "endDerivative" : t2 * chord })],
                    "interpolateIndices" : [0, 1] })[0];
            opCreateBSplineCurve(context, fillId, { "bSplineCurve" : bridge });
            fillers = append(fillers, { "bodies" : [qCreatedBy(fillId, EntityType.BODY)], "edges" : [qCreatedBy(fillId, EntityType.EDGE)] });
        }
        else
        {
            // Overlap: trim both pieces back to where they cross.
            var crossing = cornerCrossing(context, pathInfo, definition, prev, next, corner.t, prev.tA, next.tB);
            if (crossing.found)
            {
                pieces[iPrev].tB = crossing.u;
                pieces[iNext].tA = crossing.v;
            }
            else
            {
                reportFeatureWarning(context, id, "The offset overlaps itself at a corner (" ~ toString(roundToPrecision(corner.angle / degree, 1))
                    ~ " deg) but the two sides do not cross within their pieces; left overlapping.");
            }
        }
    }

    // --- 3. Emit.
    var allWireBodies = [];
    var allWireEdges  = [];
    for (var piece in pieces)
    {
        if (definition.printCurveDetails)
        {
            println("=== " ~ piece.kind ~ " piece t [" ~ toString(piece.tA) ~ ", " ~ toString(piece.tB) ~ "] on source edge " ~ toString(piece.pinEdge));
        }
        var out = emitPiece(context, id, definition, pathInfo, piece, keepArcs, stats);
        allWireBodies = concatenateArrays([allWireBodies, out.bodies]);
        allWireEdges  = concatenateArrays([allWireEdges,  out.edges]);
        if (piece.show && size(out.bodies) > 0)
        {
            addDebugEntities(context, qUnion(out.bodies), piece.colour);
        }
    }
    for (var f in fillers)
    {
        allWireBodies = concatenateArrays([allWireBodies, f.bodies]);
        allWireEdges  = concatenateArrays([allWireEdges,  f.edges]);
    }

    if (stats[].biarcCount > 0)
    {
        reportFeatureInfo(context, id, toString(stats[].biarcCount) ~ " varying-offset source arc(s) built as tangent arc pairs; " ~
            "largest deviation from the true offset " ~ toString(roundToPrecision(stats[].biarcWorst / millimeter, 4)) ~ " mm.");
    }

    // Collect all edges from individual wire bodies into one combined wire, then delete originals
    if (size(allWireBodies) > 0)
    {
        opExtractWires(context, id + "mergeWires", {
            "edges" : qUnion(allWireEdges)
        });
        opDeleteBodies(context, id + "deleteSourceWires", {
            "entities" : qUnion(allWireBodies)
        });
    }
}
