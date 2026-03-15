FeatureScript 2909;
import(path : "onshape/std/common.fs", version : "2909.0");
export import(path : "onshape/std/geometriccontinuity.gen.fs", version : "2909.0");

//import CurveWrapping_full/curveMappingCore
import(path : "08e8748f2ef24eea16072b75/f978f8e46256a09d3349266a/683d867c35fdab9c98d47556", version : "b6844b2ef23e11ceb42f1d75");
//import CurveWrapping_full/Utils
import(path : "08e8748f2ef24eea16072b75/f978f8e46256a09d3349266a/ad98c7f43a25a4c0e8a428e7", version : "1efe2444c4d02973fe212fcc");


// ─── Enums ────────────────────────────────────────────────────────────────────

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


// ─── Bounds ───────────────────────────────────────────────────────────────────

export const RegionPointsBounds    = {(unitless)   : [20, 50, 100]}        as IntegerBoundSpec;
export const OffsetBounds          = {(millimeter) : [-100, 0, 100]}       as LengthBoundSpec;
export const ApproxToleranceBounds = {(millimeter) : [0.001, 0.01, 1]}     as LengthBoundSpec;
export const ApproxDegreeBounds    = {(unitless)   : [2, 3, 5]}            as IntegerBoundSpec;
export const ApproxMaxCPBounds     = {(unitless)   : [10, 100, 500]}       as IntegerBoundSpec;


// ─── Editing logic ────────────────────────────────────────────────────────────

export function generateOffsetEdgesEditingLogic(context is Context, id is Id,
    oldDefinition is map, definition is map, isCreating is boolean,
    specifiedParameters is map, hiddenBodies is Query) returns map
{
    var pathInfo = undefined;
    try silent
    {
        pathInfo = processPath(context, id + "elPath", definition);
    }
    if (pathInfo == undefined)
        return definition;

    // Auto-name regions
    var namedRegions = [];
    for (var i = 0; i < size(definition.regions); i += 1)
    {
        var reg = definition.regions[i];
        if (reg.regionName == "" || reg.regionName == undefined)
            reg.regionName = "Region " ~ toString(i + 1);
        namedRegions = append(namedRegions, reg);
    }
    definition.regions = namedRegions;

    var sortedRegions = [];
    try silent
    {
        var processed = processRegions(context, definition, pathInfo);

        var newRegions = [];
        for (var i = 0; i < size(definition.regions); i += 1)
        {
            var reg = definition.regions[i];
            for (var pr in processed)
            {
                if (pr.regionNum == i)
                {
                    reg.length = pr.length;
                    break;
                }
            }
            newRegions = append(newRegions, reg);
        }
        definition.regions = newRegions;
        sortedRegions = sortRegionsByTStart(processed);
    }

    // Rebuild intersections for all consecutive sorted region pairs
    var newIntersections = [];
    for (var i = 0; i < size(sortedRegions) - 1; i += 1)
    {
        var regA = sortedRegions[i];
        var regB = sortedRegions[i + 1];

        var entry = {
            "isValid"         : true,
            "intersectionNum" : i + 1,
            "region1"         : regA.regionName,
            "region2"         : regB.regionName,
            "blend"           : false,
            "startContinuity" : GeometricContinuity.G0,
            "startDist"       : 10 * millimeter,
            "endContinuity"   : GeometricContinuity.G0,
            "endDist"         : 10 * millimeter
        };

        for (var existing in definition.intersections)
        {
            if (existing.region1 == regA.regionName && existing.region2 == regB.regionName)
            {
                entry = mergeMaps(entry, existing);
                entry.isValid         = true;
                entry.intersectionNum = i + 1;
                break;
            }
        }
        newIntersections = append(newIntersections, entry);
    }
    definition.intersections = newIntersections;

    return definition;
}


// ─── Feature ──────────────────────────────────────────────────────────────────

annotation { "Feature Type Name" : "Offset edges",
             "Feature Type Description" : "Offsets a G1-continuous edge chain by per-region normal and binormal amounts in the Frenet frame",
             "Editing Logic Function" : "generateOffsetEdgesEditingLogic" }
export const offsetEdges = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Reference edges",
                     "Filter" : (EntityType.BODY && BodyType.WIRE) || EntityType.EDGE,
                     "Description" : "Edges to offset from. Must form a G1-continuous path" }
        definition.userSelection is Query;

        annotation { "Name" : "Reference point",
                     "Filter" : EntityType.VERTEX || GeometryType.PLANE || BodyType.MATE_CONNECTOR,
                     "MaxNumberOfPicks" : 1,
                     "Description" : "Defines X = 0 along the path" }
        definition.referencePoint is Query;

        annotation { "Name" : "Flip direction", "Default" : false,
                     "Description" : "Reverses the traversal direction of the reference path" }
        definition.flipDirection is boolean;

        annotation { "Name" : "Flip normal", "Default" : false,
                     "Description" : "Inverts the Frenet normal direction" }
        definition.flipNormal is boolean;

        annotation { "Name" : "Flip binormal", "Default" : false,
                     "Description" : "Inverts the Frenet binormal direction" }
        definition.flipBinormal is boolean;

        annotation { "Name" : "Sampling density",
                     "Description" : "Number of points evaluated per region (and per blend)" }
        isInteger(definition.numRegionPoints, RegionPointsBounds);

        annotation { "Name" : "Regions", "Item name" : "Region",
                     "Item label template" : "#regionName",
                     "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS }
        definition.regions is array;
        for (var region in definition.regions)
        {
            annotation { "Name" : "Region type", "Default" : RegionType.LINEAR,
                         "UIHint" : UIHint.HORIZONTAL_ENUM }
            region.regionType is RegionType;

            if (region.regionType == RegionType.QUADRATIC)
            {
                annotation { "Name" : "Zero slope at", "Default" : QuadraticZeroSlope.AT_START }
                region.quadZeroSlope is QuadraticZeroSlope;
            }

            annotation { "Name" : "Region number", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isInteger(region.regionNum, POSITIVE_COUNT_BOUNDS);

            annotation { "Name" : "Region name" }
            region.regionName is string;

            annotation { "Name" : "Extent type", "Default" : RegionExtentType.X_EXTENTS }
            region.extentType is RegionExtentType;

            if (region.extentType == RegionExtentType.QUERY)
            {
                annotation { "Name" : "Extent points",
                             "Filter" : EntityType.VERTEX || GeometryType.PLANE || BodyType.MATE_CONNECTOR,
                             "MaxNumberOfPicks" : 2 }
                region.extentQueries is Query;
            }

            if (region.extentType == RegionExtentType.X_EXTENTS)
            {
                annotation { "Name" : "Region start",
                             "Description" : "Distance along path from reference point (negative = behind reference)" }
                isLength(region.regionStart, LENGTH_BOUNDS);

                annotation { "Name" : "Region end",
                             "Description" : "Distance along path from reference point" }
                isLength(region.regionEnd, LENGTH_BOUNDS);
            }

            annotation { "Name" : "Start normal offset",
                         "Description" : "Frenet normal offset at the region start" }
            isLength(region.startNormalOffset, OffsetBounds);

            annotation { "Name" : "End normal offset",
                         "Description" : "Frenet normal offset at the region end" }
            isLength(region.endNormalOffset, OffsetBounds);

            annotation { "Name" : "Start binormal offset",
                         "Description" : "Frenet binormal offset at the region start" }
            isLength(region.startBinormalOffset, OffsetBounds);

            annotation { "Name" : "End binormal offset",
                         "Description" : "Frenet binormal offset at the region end" }
            isLength(region.endBinormalOffset, OffsetBounds);

            annotation { "Name" : "Region length", "UIHint" : UIHint.READ_ONLY }
            isLength(region.length, LENGTH_BOUNDS);
        }

        annotation { "Name" : "Intersections", "Item name" : "Intersection",
                     "Item label template" : "#intersectionNum",
                     "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS }
        definition.intersections is array;
        for (var intersection in definition.intersections)
        {
            annotation { "Name" : "Is valid", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
            intersection.isValid is boolean;

            annotation { "Name" : "Intersection number", "UIHint" : UIHint.READ_ONLY }
            isInteger(intersection.intersectionNum, POSITIVE_COUNT_BOUNDS);

            annotation { "Name" : "Region 1", "UIHint" : UIHint.READ_ONLY }
            intersection.region1 is string;

            annotation { "Name" : "Region 2", "UIHint" : UIHint.READ_ONLY }
            intersection.region2 is string;

            annotation { "Name" : "Blend regions?", "Default" : false }
            intersection.blend is boolean;

            if (intersection.blend)
            {
                annotation { "Name" : "Start continuity", "UIHint" : UIHint.SHOW_LABEL }
                intersection.startContinuity is GeometricContinuity;

                annotation { "Name" : "Start distance",
                             "Description" : "How far the blend reaches back into the first region from its endpoint (0 = connect at endpoint)" }
                isLength(intersection.startDist, LENGTH_BOUNDS);

                annotation { "Name" : "End continuity", "UIHint" : UIHint.SHOW_LABEL }
                intersection.endContinuity is GeometricContinuity;

                annotation { "Name" : "End distance",
                             "Description" : "How far the blend reaches into the second region from its start (0 = connect at endpoint)" }
                isLength(intersection.endDist, LENGTH_BOUNDS);
            }
        }

        annotation { "Group Name" : "Approximation", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Spline degree" }
            isInteger(definition.approxDegree, ApproxDegreeBounds);

            annotation { "Name" : "Approximation tolerance" }
            isLength(definition.approxTolerance, ApproxToleranceBounds);

            annotation { "Name" : "Max control points" }
            isInteger(definition.approxMaxCP, ApproxMaxCPBounds);
        }

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Show reference frames",
                         "Description" : "Draw Frenet frames along the reference path" }
            definition.showRefFrames is boolean;

            annotation { "Name" : "Show regions",
                         "Description" : "Highlight region output curves (cyan)" }
            definition.showRegions is boolean;

            annotation { "Name" : "Show blends",
                         "Description" : "Highlight blend output curves (yellow)" }
            definition.showBlends is boolean;

            annotation { "Name" : "Print curve details",
                         "Description" : "Print BSpline metadata to FeatureStudio console" }
            definition.printCurveDetails is boolean;

            annotation { "Name" : "Print path data",
                         "Description" : "Print path length, refParam, and edge count" }
            definition.printPathData is boolean;
        }
    }
    {
        var pathInfo = processPath(context, id + "refPath", definition);

        if (size(definition.regions) == 0)
        {
            reportFeatureWarning(context, id, "No regions defined — nothing to generate");
            return;
        }

        var processedRegions = processRegions(context, definition, pathInfo);
        validateNoOverlap(context, id, processedRegions);
        var sortedRegions = sortRegionsByTStart(processedRegions);
        buildOutputWire(context, id, definition, pathInfo, sortedRegions);

        if (definition.showRefFrames)
        {
            // Draw frames with flipNormal/flipBinormal applied so arrows match
            // the actual offset directions (RED = normal offset dir, GREEN = binormal offset dir, BLUE = tangent)
            var numF   = definition.numRegionPoints;
            var len    = pathInfo.length;
            var aLen   = len / max([1, numF - 1]) / 3;
            var aRad   = aLen * 0.05;
            for (var fi = 0; fi < numF; fi += 1)
            {
                var s   = len * fi / max([1, numF - 1]);
                var fr  = getFrameAtArcLength(context, pathInfo.frenetPath, s);
                var org = fr.frame.origin;
                var nD  = definition.flipNormal   ? -yAxis(fr.frame) : yAxis(fr.frame);
                var bD  = definition.flipBinormal ? -fr.frame.xAxis  : fr.frame.xAxis;
                addDebugArrow(context, org, org + aLen * nD,              aRad,          DebugColor.RED);
                addDebugArrow(context, org, org + aLen * bD,              aRad * (2/3),  DebugColor.GREEN);
                addDebugArrow(context, org, org + aLen * fr.frame.zAxis,  aRad * 0.5,   DebugColor.BLUE);
            }
        }

        if (definition.printPathData)
        {
            println("=== offsetEdges path ===");
            println("  total length: " ~ toString(pathInfo.length / millimeter) ~ " mm");
            println("  refParam:     " ~ toString(pathInfo.refParam));
            println("  edge count:   " ~ toString(size(pathInfo.frenetPath.edgeData)));
        }
    });


// ─── Path processing ──────────────────────────────────────────────────────────

function fixFrenetPathSigns(context is Context, frenetPath is map) returns map
{
    var edgeData = frenetPath.edgeData;
    var n = size(edgeData);
    if (n < 2)
    {
        return frenetPath;
    }

    for (var i = 0; i < n - 1; i += 1)
    {
        var lenA = edgeData[i].length;
        var lenB = edgeData[i + 1].length;
        var eps  = (lenA < lenB ? lenA : lenB) * 0.01;
        var junctionArc = edgeData[i + 1].startArcLength;

        var fBefore = getFrameAtArcLength(context, frenetPath, junctionArc - eps);
        var fAfter  = getFrameAtArcLength(context, frenetPath, junctionArc + eps);

        if (dot(fBefore.frame.xAxis, fAfter.frame.xAxis) < 0)
        {
            for (var j = i + 1; j < n; j += 1)
            {
                edgeData[j] = mergeMaps(edgeData[j], {
                    "startSign": -1 * edgeData[j].startSign
                });
            }
            frenetPath = mergeMaps(frenetPath, { "edgeData": edgeData });
        }
    }

    return frenetPath;
}


function processPath(context is Context, id is Id, definition is map) returns map
{
    var edges       = expandEdgeQuery(definition.userSelection);
    var frenetPath  = buildFrenetPath(context, id, edges, definition.flipDirection);
    frenetPath      = fixFrenetPathSigns(context, frenetPath);
    var totalLength = frenetPath.totalLength;

    var refPt     = getRefPoint(context, definition.referencePoint);
    var refResult = projectOntoFrenetPath(frenetPath, refPt, undefined);
    var refParam  = refResult.arcLength / totalLength;

    return {
        "frenetPath" : frenetPath,
        "length"     : totalLength,
        "refParam"   : refParam
    };
}


// ─── Region processing ────────────────────────────────────────────────────────

function processRegions(context is Context, definition is map, pathInfo is map) returns array
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
                throw regenError("Region '" ~ region.regionName ~ "': extent query must resolve to exactly 2 points");

            var pt0 = getRefPoint(context, queryPts[0]);
            var pt1 = getRefPoint(context, queryPts[1]);

            var r0 = projectOntoFrenetPath(pathInfo.frenetPath, pt0, undefined);
            var r1 = projectOntoFrenetPath(pathInfo.frenetPath, pt1, undefined);

            tStart = min(r0.arcLength, r1.arcLength) / pathInfo.length;
            tEnd   = max(r0.arcLength, r1.arcLength) / pathInfo.length;
        }

        tStart = min(max(tStart, 0), 1);
        tEnd   = min(max(tEnd,   0), 1);
        if (tStart > tEnd)
        {
            var tmp = tStart;
            tStart = tEnd;
            tEnd = tmp;
        }

        region.tStart = tStart;
        region.tEnd   = tEnd;
        region.length = (tEnd - tStart) * pathInfo.length;

        processed = append(processed, region);
    }

    return processed;
}


function validateNoOverlap(context is Context, id is Id, regions is array)
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


function sortRegionsByTStart(regions is array) returns array
{
    // Insertion sort (region counts are expected to be small)
    var sorted = regions;
    for (var i = 1; i < size(sorted); i += 1)
    {
        var key = sorted[i];
        var j   = i - 1;
        while (j >= 0 && sorted[j].tStart > key.tStart)
        {
            sorted[j + 1] = sorted[j];
            j -= 1;
        }
        sorted[j + 1] = key;
    }
    return sorted;
}


// ─── Profile evaluation ───────────────────────────────────────────────────────

/**
 * Returns the interpolated offset at normalized position t ∈ [0,1] within a region.
 *   LINEAR    : linear ramp
 *   QUADRATIC : true quadratic — one endpoint has zero slope
 *   SMOOTH    : smootherstep (6t⁵ − 15t⁴ + 10t³) — C2 at both endpoints
 */
function profileValueAt(t is number, startVal is ValueWithUnits, endVal is ValueWithUnits,
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
            s = 2 * t - t * t;
        else // AT_START
            s = t * t;
    }
    else // SMOOTH
    {
        s = t * t * t * (10 + t * (6 * t - 15));
    }
    return startVal + (endVal - startVal) * s;
}


/**
 * Returns { normalOff, binormalOff } at path parameter tPath from the region's profile.
 */
function computeOffsetsAt(region is map, tPath is number) returns map
{
    var span  = region.tEnd - region.tStart;
    var alpha = (span > 1e-10) ? min(max((tPath - region.tStart) / span, 0), 1) : 0;
    var quadZS = region.quadZeroSlope;
    return {
        "normalOff"   : profileValueAt(alpha, region.startNormalOffset,   region.endNormalOffset,   region.regionType, quadZS),
        "binormalOff" : profileValueAt(alpha, region.startBinormalOffset, region.endBinormalOffset, region.regionType, quadZS)
    };
}


/**
 * Returns { normalOff, binormalOff, normalSlope, binormalSlope, normalCurv, binormalCurv }
 * using central finite differences in t-space.
 *
 * Slope and curv are in t-space (VWU/t, VWU/t²).
 * Scale to s-space before passing to blendOffsetAt: multiply slope × L, curv × L².
 */
function computeOffsetDerivativesAt(region is map, tPath is number) returns map
{
    var dt   = 1e-4;
    var tHi  = min(tPath + dt, 1.0);
    var tLo  = max(tPath - dt, 0.0);
    var h    = (tHi - tLo) / 2;  // effective half-step (dimensionless)

    var offMid = computeOffsetsAt(region, tPath);
    var offHi  = computeOffsetsAt(region, tHi);
    var offLo  = computeOffsetsAt(region, tLo);

    return {
        "normalOff"     : offMid.normalOff,
        "binormalOff"   : offMid.binormalOff,
        "normalSlope"   : (offHi.normalOff   - offLo.normalOff)   / (2 * h),
        "binormalSlope" : (offHi.binormalOff - offLo.binormalOff) / (2 * h),
        "normalCurv"    : (offHi.normalOff   - 2 * offMid.normalOff   + offLo.normalOff)   / (h * h),
        "binormalCurv"  : (offHi.binormalOff - 2 * offMid.binormalOff + offLo.binormalOff) / (h * h)
    };
}


// ─── Offset point computation ─────────────────────────────────────────────────

/**
 * Computes the 3D world point at path parameter t with the given Frenet-space offsets.
 * Frame convention from buildFrenetPath: xAxis = normal (sign-corrected), yAxis = binormal, zAxis = tangent.
 */
function computeOffsetPoint(context is Context, pathInfo is map, definition is map,
    t is number, normalOff is ValueWithUnits, binormalOff is ValueWithUnits) returns Vector
{
    var fr   = getFrameAtArcLength(context, pathInfo.frenetPath, t * pathInfo.length);
    // Empirically: yAxis(frame) = visual normal direction, xAxis = visual binormal direction
    var nDir = definition.flipNormal   ? -yAxis(fr.frame) : yAxis(fr.frame);
    var bDir = definition.flipBinormal ? -fr.frame.xAxis  : fr.frame.xAxis;
    return fr.frame.origin + normalOff * nDir + binormalOff * bDir;
}


// ─── Point arrays ─────────────────────────────────────────────────────────────

/**
 * Samples n points along [tSegStart, tSegEnd], evaluating the region's profile at each.
 * tSegStart/tSegEnd may differ from region.tStart/tEnd when trimmed by an adjacent blend.
 */
function generateSegmentPoints(context is Context, pathInfo is map, definition is map,
    region is map, tSegStart is number, tSegEnd is number) returns array
{
    var n = definition.numRegionPoints;
    var points = [];
    for (var i = 0; i < n; i += 1)
    {
        var t    = tSegStart + (tSegEnd - tSegStart) * i / (n - 1);
        var offs = computeOffsetsAt(region, t);
        points = append(points, computeOffsetPoint(context, pathInfo, definition, t, offs.normalOff, offs.binormalOff));
    }
    return points;
}


/**
 * Evaluates the Hermite blend polynomial at s ∈ [0,1] for a single scalar offset component.
 *
 * All slope/curvature args are in s-space (multiply region dOffset/dt by L, d²Offset/dt² by L²
 * before calling, where L = tBlendEnd − tBlendStart).
 *
 * Continuity dispatch:
 *   G0+G0 → linear          G1+G0 / G0+G1 → quadratic
 *   G1+G1 → cubic Hermite   G2+G0 / G0+G2 → cubic
 *   G2+G1 / G1+G2 → quartic               G2+G2 → quintic Hermite
 */
function blendOffsetAt(s is number,
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
        // G1+G0: quadratic — h0, m0, h1
        return h0 + m0 * s + (h1 - h0 - m0) * s2;
    }
    else if (!matchSlopeStart && matchSlopeEnd && !matchCurvEnd)
    {
        // G0+G1: quadratic — h0, h1, m1
        var dh = h1 - h0;
        return h0 + (2 * dh - m1) * s + (m1 - dh) * s2;
    }
    else if (matchSlopeStart && !matchCurvStart && matchSlopeEnd && !matchCurvEnd)
    {
        // G1+G1: cubic Hermite — h0, m0, h1, m1
        return h0 * (2*s3 - 3*s2 + 1) + m0 * (s3 - 2*s2 + s)
             + h1 * (-2*s3 + 3*s2)    + m1 * (s3 - s2);
    }
    else if (matchCurvStart && !matchSlopeEnd)
    {
        // G2+G0: cubic — h0, m0, k0, h1
        return h0 + m0 * s + (k0 / 2) * s2 + (h1 - h0 - m0 - k0 / 2) * s3;
    }
    else if (!matchSlopeStart && matchCurvEnd)
    {
        // G0+G2: cubic — h0, h1, m1, k1
        var dh = h1 - h0;
        var d  = dh - m1 + k1 / 2;
        var c  = -(k1 + 3 * (dh - m1));
        var b  = 3 * dh - 2 * m1 + k1 / 2;
        return h0 + b * s + c * s2 + d * s3;
    }
    else if (matchCurvStart && matchSlopeEnd && !matchCurvEnd)
    {
        // G2+G1: quartic — h0, m0, k0, h1, m1
        var dh = h1 - h0;
        var a4 = m1 + 2 * m0 + k0 / 2 - 3 * dh;
        var a3 = 4 * dh - 3 * m0 - k0 - m1;
        return h0 + m0 * s + (k0 / 2) * s2 + a3 * s3 + a4 * s4;
    }
    else if (matchSlopeStart && !matchCurvStart && matchCurvEnd)
    {
        // G1+G2: quartic — h0, m0, h1, m1, k1
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
        // G2+G2: quintic Hermite — h0, m0, k0, h1, m1, k1
        return h0 * (1 - 10*s3 + 15*s4 - 6*s5)
             + m0 * (s - 6*s3 + 8*s4 - 3*s5)
             + k0 * (s2/2 - 3*s3/2 + 3*s4/2 - s5/2)
             + h1 * (10*s3 - 15*s4 + 6*s5)
             + m1 * (-4*s3 + 7*s4 - 3*s5)
             + k1 * (s3/2 - s4 + s5/2);
    }
}


/**
 * Samples n points across the blend zone [tBlendStart, tBlendEnd].
 * Normal and binormal offsets are blended independently via blendOffsetAt.
 *
 * Heights at the blend boundaries are evaluated at the actual tPath positions
 * (not region endpoints) so the blend wire meets the trimmed region wire.
 */
function generateBlendPoints(context is Context, pathInfo is map, definition is map,
    tBlendStart is number, tBlendEnd is number,
    regA is map, regB is map, intr is map) returns array
{
    var n = definition.numRegionPoints;
    var L = tBlendEnd - tBlendStart;
    var contStart = intr.startContinuity;
    var contEnd   = intr.endContinuity;

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

    var points = [];
    for (var i = 0; i < n; i += 1)
    {
        var s    = i / (n - 1);
        var t    = tBlendStart + L * s;
        var nOff = blendOffsetAt(s, nOff0, nSlope0, nCurv0, nOff1, nSlope1, nCurv1, contStart, contEnd);
        var bOff = blendOffsetAt(s, bOff0, bSlope0, bCurv0, bOff1, bSlope1, bCurv1, contStart, contEnd);
        points = append(points, computeOffsetPoint(context, pathInfo, definition, t, nOff, bOff));
    }
    return points;
}


// ─── Wire construction ────────────────────────────────────────────────────────

function buildOutputWire(context is Context, id is Id, definition is map,
    pathInfo is map, sortedRegions is array)
{
    var allWireBodies = [];

    // Collect active blend zones
    var blendZones = [];
    for (var intr in definition.intersections)
    {
        if (!intr.isValid || !intr.blend)
            continue;

        var regA = undefined;
        var regB = undefined;
        for (var reg in sortedRegions)
        {
            if (reg.regionName == intr.region1) regA = reg;
            if (reg.regionName == intr.region2) regB = reg;
        }
        if (regA == undefined || regB == undefined)
            continue;

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

    // Collect edge-boundary t-values (one per inter-edge junction in the FrenetPath)
    var edgeBoundaryTs = [];
    var edgeData = pathInfo.frenetPath.edgeData;
    for (var ei = 1; ei < size(edgeData); ei += 1)
        edgeBoundaryTs = append(edgeBoundaryTs, edgeData[ei].startArcLength / pathInfo.length);

    // One or more BSpline wires per region — split at edge boundaries so each
    // output curve spans at most one source edge.
    for (var ri = 0; ri < size(sortedRegions); ri += 1)
    {
        var reg       = sortedRegions[ri];
        var tSegStart = reg.tStart;
        var tSegEnd   = reg.tEnd;

        for (var bz in blendZones)
        {
            if (bz.regA.regionName == reg.regionName)
                tSegEnd   = min(tSegEnd,   bz.tBlendStart);
            if (bz.regB.regionName == reg.regionName)
                tSegStart = max(tSegStart, bz.tBlendEnd);
        }

        if (tSegEnd - tSegStart < 1e-6)
        {
            reportFeatureWarning(context, id, "Region '" ~ reg.regionName ~
                "' was fully consumed by adjacent blend zones and produced no output. " ~
                "Reduce blend distances or expand the region extent.");
            continue;
        }

        // Build the ordered list of split points: region endpoints + any edge
        // boundaries that fall strictly inside the trimmed segment
        var splitTs = [tSegStart];
        for (var tb in edgeBoundaryTs)
        {
            if (tb > tSegStart + 1e-6 && tb < tSegEnd - 1e-6)
                splitTs = append(splitTs, tb);
        }
        splitTs = append(splitTs, tSegEnd);

        for (var si = 0; si < size(splitTs) - 1; si += 1)
        {
            var tA = splitTs[si];
            var tB = splitTs[si + 1];
            if (tB - tA < 1e-6) continue;

            var pts = generateSegmentPoints(context, pathInfo, definition, reg, tA, tB);
            if (size(pts) < 2) continue;

            var bspline = approximateSpline(context, {
                "degree"             : definition.approxDegree,
                "tolerance"          : definition.approxTolerance,
                "isPeriodic"         : false,
                "maxControlPoints"   : definition.approxMaxCP,
                "targets"            : [approximationTarget({ "positions" : pts })],
                "interpolateIndices" : [0, size(pts) - 1]
            })[0];

            var wireId = id + ("reg_" ~ toString(ri) ~ "_" ~ toString(si));
            opCreateBSplineCurve(context, wireId, { "bSplineCurve" : bspline });
            allWireBodies = append(allWireBodies, qCreatedBy(wireId, EntityType.BODY));

            if (definition.printCurveDetails)
            {
                println("=== Region " ~ toString(ri) ~ " ('" ~ reg.regionName ~ "') sub " ~ toString(si) ~ " ===");
                println("  degree:  " ~ toString(bspline.degree));
                println("  CPs:     " ~ toString(size(bspline.controlPoints)));
                println("  samples: " ~ toString(size(pts)));
                println("  t range: [" ~ toString(tA) ~ ", " ~ toString(tB) ~ "]");
            }

            if (definition.showRegions)
                addDebugEntities(context, qCreatedBy(wireId, EntityType.BODY), DebugColor.CYAN);
        }
    }

    // One BSpline wire per active blend zone
    for (var bzi = 0; bzi < size(blendZones); bzi += 1)
    {
        var bz  = blendZones[bzi];
        var pts = generateBlendPoints(context, pathInfo, definition,
            bz.tBlendStart, bz.tBlendEnd, bz.regA, bz.regB, bz.intr);
        if (size(pts) < 2) continue;

        var bspline = approximateSpline(context, {
            "degree"             : definition.approxDegree,
            "tolerance"          : definition.approxTolerance,
            "isPeriodic"         : false,
            "maxControlPoints"   : definition.approxMaxCP,
            "targets"            : [approximationTarget({ "positions" : pts })],
            "interpolateIndices" : [0, size(pts) - 1]
        })[0];

        var wireId = id + ("blend_" ~ toString(bzi));
        opCreateBSplineCurve(context, wireId, { "bSplineCurve" : bspline });
        allWireBodies = append(allWireBodies, qCreatedBy(wireId, EntityType.BODY));

        if (definition.printCurveDetails)
        {
            println("=== Blend " ~ toString(bzi) ~ " ('" ~ bz.regA.regionName ~ "' → '" ~ bz.regB.regionName ~ "') ===");
            println("  degree:  " ~ toString(bspline.degree));
            println("  CPs:     " ~ toString(size(bspline.controlPoints)));
            println("  samples: " ~ toString(size(pts)));
        }

        if (definition.showBlends)
            addDebugEntities(context, qCreatedBy(wireId, EntityType.BODY), DebugColor.YELLOW);
    }

    // Collect all edges from individual wire bodies into one combined wire, then delete originals
    if (size(allWireBodies) > 0)
    {
        opExtractWires(context, id + "mergeWires", {
            "edges" : qOwnedByBody(qUnion(allWireBodies), EntityType.EDGE)
        });
        opDeleteBodies(context, id + "deleteSourceWires", {
            "entities" : qUnion(allWireBodies)
        });
    }
}
