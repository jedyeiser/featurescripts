FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");
export import(path : "onshape/std/geometriccontinuity.gen.fs", version : "2892.0");

// IMPORT: refSurfUtils.fs
import(path : "d41884a96244793beb462449", version : "42d2bcee7fa0f9fcf0143ccf");


// --- Enums -------------------------------------------------------------------

export enum SWRoutExtentType
{
    QUERY,
    X_EXTENTS
}


// --- Bounds ------------------------------------------------------------------

export const SWRoutAngleBounds     = {(degree)     : [0,   20,  45]} as AngleBoundSpec;
export const DistAboveBottomBounds = {(millimeter) : [1,    4,  10]} as LengthBoundSpec;
export const SWRoutHeightBounds    = {(millimeter) : [5,   30, 100]} as LengthBoundSpec;
export const SWStepInBounds        = {(millimeter) : [0,    0,   3]} as LengthBoundSpec;


// --- Region processing -------------------------------------------------------

/**
 * Converts definition.swRoutRegions extents to tStart/tEnd [0,1] arc-length
 * parameters along refWirePath.  Assigns regionNum and computes regionLength.
 * refWirePath must be a Path built from definition.refWire by the caller.
 */
export function processSwRoutRegions(context is Context, id is Id,
        definition is map, refWirePath is Path) returns array
{
    var totalLength = 0 * meter;
    for (var edge in refWirePath.edges)
    {
        totalLength += evLength(context, { "entities" : edge });
    }

    var processed = [];
    for (var i = 0; i < size(definition.swRoutRegions); i += 1)
    {
        var region = definition.swRoutRegions[i];
        region.regionNum = i;

        var tStart;
        var tEnd;

        if (region.extentType == SWRoutExtentType.X_EXTENTS)
        {
            tStart = findPathParamAtX(context, refWirePath, region.regionStart);
            tEnd   = findPathParamAtX(context, refWirePath, region.regionEnd);
        }
        else // QUERY
        {
            var queryPts = evaluateQuery(context, region.extentQueries);
            if (size(queryPts) != 2)
            {
                throw regenError("Region '" ~ region.regionName ~
                        "': extent query must resolve to exactly 2 points.");
            }
            var t0 = evDistancePath(context, {
                    "side0" : refWirePath,
                    "side1" : queryPts[0]
            }).sides[0].pathParam;
            var t1 = evDistancePath(context, {
                    "side0" : refWirePath,
                    "side1" : queryPts[1]
            }).sides[0].pathParam;
            tStart = min(t0, t1);
            tEnd   = max(t0, t1);
        }

        tStart = min(max(tStart, 0), 1);
        tEnd   = min(max(tEnd,   0), 1);
        if (tStart > tEnd)
        {
            var tmp = tStart;
            tStart  = tEnd;
            tEnd    = tmp;
        }

        region.tStart       = tStart;
        region.tEnd         = tEnd;
        region.regionLength = (tEnd - tStart) * totalLength;
        processed           = append(processed, region);
    }

    return processed;
}


// Sorts a region array by tStart (insertion sort).
export function sortSwRoutRegions(regions is array) returns array
{
    var sorted    = [];
    var remaining = regions;
    while (size(remaining) > 0)
    {
        var minIdx = 0;
        for (var i = 1; i < size(remaining); i += 1)
        {
            if (remaining[i].tStart < remaining[minIdx].tStart)
            {
                minIdx = i;
            }
        }
        sorted = append(sorted, remaining[minIdx]);
        var newRemaining = [];
        for (var i = 0; i < size(remaining); i += 1)
        {
            if (i != minIdx)
            {
                newRemaining = append(newRemaining, remaining[i]);
            }
        }
        remaining = newRemaining;
    }
    return sorted;
}


// Throws regenError if any two regions overlap (tStart/tEnd).
export function validateSwRoutRegionsNoOverlap(regions is array)
{
    const eps = 1e-6;
    for (var i = 0; i < size(regions); i += 1)
    {
        for (var j = i + 1; j < size(regions); j += 1)
        {
            var a = regions[i];
            var b = regions[j];
            if (a.tStart < b.tEnd - eps && b.tStart < a.tEnd - eps)
            {
                throw regenError("Regions '" ~ a.regionName ~ "' and '" ~
                        b.regionName ~ "' overlap.");
            }
        }
    }
}


/**
 * Auto-generates one intersection entry per consecutive sorted region pair.
 * Preserves user-configured blend settings from definition.swRoutIntersections
 * via mergeMaps.
 */
export function rebuildSwRoutIntersections(definition is map,
        sortedRegions is array) returns array
{
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

        for (var existing in definition.swRoutIntersections)
        {
            if (existing.region1 == regA.regionName &&
                existing.region2 == regB.regionName)
            {
                entry = mergeMaps(entry, existing);
                entry.isValid         = true;
                entry.intersectionNum = i + 1;
                break;
            }
        }

        newIntersections = append(newIntersections, entry);
    }
    return newIntersections;
}


/**
 * Returns a plane at parameter t [0,1] along refWirePath, with origin at the
 * path point and normal along the path tangent.  Used to split surface copies
 * at region boundaries.
 */
export function createRegionBoundingPlane(context is Context,
        refWirePath is Path, t is number) returns Plane
{
    var result = evPathTangentLines(context, refWirePath, [t]);
    var tl     = result.tangentLines[0];
    return plane(tl.origin, tl.direction);
}
