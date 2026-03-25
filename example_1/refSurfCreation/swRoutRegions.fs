FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");
export import(path : "onshape/std/geometriccontinuity.gen.fs", version : "2892.0");

// IMPORT: refSurfUtils.fs
import(path : "d41884a96244793beb462449", version : "fc8a6dfccab240021ff23696");


// --- Enums -------------------------------------------------------------------

export enum SWRoutExtentType
{
    QUERY,
    ALONG_REF
}


// --- Bounds ------------------------------------------------------------------

export const SWRoutAngleBounds     = {(degree)     : [0,   20,  45]} as AngleBoundSpec;
export const DistAboveBottomBounds = {(millimeter) : [1,    4,  10]} as LengthBoundSpec;
export const SWRoutHeightBounds    = {(millimeter) : [5,   30, 100]} as LengthBoundSpec;
export const SWStepInBounds        = {(millimeter) : [0,    0,   3]} as LengthBoundSpec;
export const RegionNumBounds       = { (unitless)  : [0,    0, 100]} as IntegerBoundSpec;


// --- Region processing -------------------------------------------------------

/**
 * Converts definition.swRoutRegions extents to tStart/tEnd [0,1] arc-length
 * parameters along refWirePath.  Assigns regionNum and computes regionLength.
 * refWirePath must be a Path built from definition.refWire by the caller.
 * ALONG_REF: startX/endX are arc-length distances from the wire origin.
 * QUERY:     extentQueries must resolve to exactly 2 points.
 */
export function processSwRoutRegions(context is Context, id is Id,
        definition is map, refWirePath is Path, tOrigin is number) returns array
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

        if (region.extentType == SWRoutExtentType.ALONG_REF)
        {
            tStart = tOrigin + region.startX / totalLength;
            tEnd   = tOrigin + region.endX   / totalLength;
        }
        else // QUERY
        {
            var queryPts = evaluateQuery(context, region.extentQueries);
            if (size(queryPts) != 2)
            {
                throw regenError("Region '" ~ region.name ~
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
                throw regenError("Regions '" ~ a.name ~ "' and '" ~
                        b.name ~ "' overlap.");
            }
        }
    }
}


/**
 * Auto-generates one intersection entry per consecutive sorted region pair.
 * Preserves user-configured blend settings from definition.swRoutIntersections,
 * matching by regionAName/regionBName or legacy region1/region2 field names.
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
            "intersectionNum"       : i,
            "intersectionName"      : "Intersection " ~ (i + 1),
            "needsIntersectionName" : true,
            "regionANum"            : regA.regionNum,
            "regionBNum"            : regB.regionNum,
            "regionAName"           : regA.name,
            "regionBName"           : regB.name,
            "blend"                 : false,
            "startContinuity"       : GeometricContinuity.G0,
            "startDist"             : 10 * millimeter,
            "endContinuity"         : GeometricContinuity.G0,
            "endDist"               : 10 * millimeter
        };

        for (var existing in definition.swRoutIntersections)
        {
            var nameMatch   = (existing.regionAName == regA.name && existing.regionBName == regB.name);
            var legacyMatch = (existing.region1     == regA.name && existing.region2     == regB.name);
            if (nameMatch || legacyMatch)
            {
                entry = mergeMaps(entry, existing);
                entry.intersectionNum       = i;
                entry.regionANum            = regA.regionNum;
                entry.regionBNum            = regB.regionNum;
                entry.regionAName           = regA.name;
                entry.regionBName           = regB.name;
                entry.needsIntersectionName = (entry.intersectionName == undefined ||
                        entry.intersectionName == "" ||
                        startsWith(entry.intersectionName, "Intersection "));
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
