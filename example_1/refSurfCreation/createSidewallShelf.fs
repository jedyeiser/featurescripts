FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");
export import(path : "onshape/std/geometriccontinuity.gen.fs", version : "2892.0");

// IMPORT: refSurfUtils.fs  (findPathParamAtX, evDistancePath, resolveQueryToPoint)
import(path : "d41884a96244793beb462449", version : "452a78e9338f921ea6ae9fb6");


// ─── Enums ────────────────────────────────────────────────────────────────────

export enum ShelfRegionType
{
    LINEAR,
    QUADRATIC,
    SMOOTH
}

export enum ShelfExtentType
{
    QUERY,
    X_EXTENTS
}

export enum ShelfQuadraticZeroSlope
{
    AT_START,
    AT_END
}


// ─── Bounds ───────────────────────────────────────────────────────────────────

export const ShelfDepthBounds           = {(millimeter) : [0,     3,  20]}  as LengthBoundSpec;
export const SidewallWidthBounds        = {(millimeter) : [0.5,   2,  10]}  as LengthBoundSpec;
export const ShelfSamplingDensityBounds = {(unitless)   : [5,    50, 500]}  as IntegerBoundSpec;
export const ShelfApproxDegreeBounds    = {(unitless)   : [2,     3,   5]}  as IntegerBoundSpec;
export const ShelfApproxToleranceBounds = {(millimeter) : [0.001, 0.01, 1]} as LengthBoundSpec;
export const ShelfApproxMaxCPBounds     = {(unitless)   : [10,  100, 500]}  as IntegerBoundSpec;
export const ShelfDebugStepBounds       = {(unitless)   : [1,     1,   4]}  as IntegerBoundSpec;


// ─── Editing logic ────────────────────────────────────────────────────────────

export function generateSidewallShelfEditingLogic(context is Context, id is Id,
    oldDefinition is map, definition is map, isCreating is boolean,
    specifiedParameters is map, hiddenBodies is Query) returns map
{
    var pathInfo = undefined;
    try silent { pathInfo = processShelfPath(context, id + "elPath", definition); }
    if (pathInfo == undefined)
        return definition;

    // Auto-name regions
    var namedRegions = [];
    for (var i = 0; i < size(definition.shelfRegions); i += 1)
    {
        var reg = definition.shelfRegions[i];
        if (reg.regionName == "" || reg.regionName == undefined)
            reg.regionName = "Region " ~ toString(i + 1);
        namedRegions = append(namedRegions, reg);
    }
    definition.shelfRegions = namedRegions;

    // Compute region lengths
    var sortedRegions = [];
    try silent
    {
        var processed = processShelfRegions(context, definition, pathInfo);
        var newRegions = [];
        for (var i = 0; i < size(definition.shelfRegions); i += 1)
        {
            var reg = definition.shelfRegions[i];
            for (var pr in processed)
            {
                if (pr.regionNum == i)
                {
                    reg.shelfLength = pr.length;
                    break;
                }
            }
            newRegions = append(newRegions, reg);
        }
        definition.shelfRegions = newRegions;
        sortedRegions = sortShelfRegionsByTStart(processed);
    }

    // Rebuild intersections for consecutive region pairs
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

        for (var existing in definition.shelfIntersections)
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
    definition.shelfIntersections = newIntersections;

    return definition;
}


// ─── Feature ──────────────────────────────────────────────────────────────────

annotation { "Feature Type Name" : "Sidewall shelf",
             "Editing Logic Function" : "generateSidewallShelfEditingLogic" }
export const generateSidewallShelf = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Footprint wire",
                     "Filter" : BodyType.WIRE, "MaxNumberOfPicks" : 1,
                     "Description" : "Closed wire on the bottom surface defining the ski outline" }
        definition.footprintWire is Query;

        annotation { "Name" : "Bottom surface",
                     "Filter" : EntityType.BODY && BodyType.SHEET, "MaxNumberOfPicks" : 1,
                     "Description" : "Surface on which the footprint wire lies" }
        definition.bottomSurface is Query;

        annotation { "Name" : "Reference wire",
                     "Filter" : BodyType.WIRE, "MaxNumberOfPicks" : 1,
                     "Description" : "Reference path for region parameterization" }
        definition.refWire is Query;

        annotation { "Name" : "Reference point",
                     "Filter" : EntityType.VERTEX || GeometryType.PLANE || BodyType.MATE_CONNECTOR,
                     "MaxNumberOfPicks" : 1,
                     "Description" : "Defines X = 0 along the reference wire" }
        definition.refPoint is Query;

        annotation { "Name" : "Sidewall width",
                     "Description" : "Thickness of sidewall material; inside curve = shelf - sidewallWidth" }
        isLength(definition.sidewallWidth, SidewallWidthBounds);

        annotation { "Name" : "Regions", "Item name" : "Region",
                     "Item label template" : "#regionName",
                     "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS }
        definition.shelfRegions is array;
        for (var region in definition.shelfRegions)
        {
            annotation { "Name" : "Region type", "Default" : ShelfRegionType.LINEAR,
                         "UIHint" : UIHint.HORIZONTAL_ENUM }
            region.regionType is ShelfRegionType;

            if (region.regionType == ShelfRegionType.QUADRATIC)
            {
                annotation { "Name" : "Zero slope at",
                             "Default" : ShelfQuadraticZeroSlope.AT_START }
                region.quadZeroSlope is ShelfQuadraticZeroSlope;
            }

            annotation { "Name" : "Region number", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isInteger(region.regionNum, POSITIVE_COUNT_BOUNDS);

            annotation { "Name" : "Region name" }
            region.regionName is string;

            annotation { "Name" : "Extent type", "Default" : ShelfExtentType.X_EXTENTS }
            region.extentType is ShelfExtentType;

            if (region.extentType == ShelfExtentType.QUERY)
            {
                annotation { "Name" : "Extent points",
                             "Filter" : EntityType.VERTEX || GeometryType.PLANE || BodyType.MATE_CONNECTOR,
                             "MaxNumberOfPicks" : 2 }
                region.extentQueries is Query;
            }

            if (region.extentType == ShelfExtentType.X_EXTENTS)
            {
                annotation { "Name" : "Region start",
                             "Description" : "Distance along ref wire from reference point (negative = toward tail)" }
                isLength(region.regionStart, LENGTH_BOUNDS);

                annotation { "Name" : "Region end" }
                isLength(region.regionEnd, LENGTH_BOUNDS);
            }

            annotation { "Name" : "Start shelf depth",
                         "Description" : "Distance outward from footprint at start of region" }
            isLength(region.startShelfDepth, ShelfDepthBounds);

            annotation { "Name" : "End shelf depth",
                         "Description" : "Distance outward from footprint at end of region" }
            isLength(region.endShelfDepth, ShelfDepthBounds);

            annotation { "Name" : "Region length", "UIHint" : UIHint.READ_ONLY }
            isLength(region.shelfLength, LENGTH_BOUNDS);
        }

        annotation { "Name" : "Intersections", "Item name" : "Intersection",
                     "Item label template" : "#intersectionNum",
                     "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS }
        definition.shelfIntersections is array;
        for (var intersection in definition.shelfIntersections)
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
                             "Description" : "How far the blend reaches back into the first region from its endpoint" }
                isLength(intersection.startDist, LENGTH_BOUNDS);

                annotation { "Name" : "End continuity", "UIHint" : UIHint.SHOW_LABEL }
                intersection.endContinuity is GeometricContinuity;

                annotation { "Name" : "End distance",
                             "Description" : "How far the blend reaches into the second region from its start" }
                isLength(intersection.endDist, LENGTH_BOUNDS);
            }
        }

        annotation { "Group Name" : "Approximation", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Sampling density",
                         "Description" : "Points sampled along footprint (more = smoother curve, slower)" }
            isInteger(definition.samplingDensity, ShelfSamplingDensityBounds);

            annotation { "Name" : "Spline degree" }
            isInteger(definition.approxDegree, ShelfApproxDegreeBounds);

            annotation { "Name" : "Approximation tolerance" }
            isLength(definition.approxTolerance, ShelfApproxToleranceBounds);

            annotation { "Name" : "Max control points" }
            isInteger(definition.approxMaxCP, ShelfApproxMaxCPBounds);
        }

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Step through", "Default" : false,
                         "Description" : "Return early at a chosen step to inspect intermediate geometry" }
            definition.debugStepThrough is boolean;

            annotation { "Group Name" : "Step through options",
                         "Driving Parameter" : "debugStepThrough",
                         "Collapsed By Default" : false }
            {
                annotation { "Name" : "Step (1-4)", "UIHint" : UIHint.SHOW_LABEL,
                             "Description" : "1=ref path  2=outward check  3=offset points  4=output curves" }
                isInteger(definition.debugStep, ShelfDebugStepBounds);
            }

            annotation { "Name" : "Print debug", "Default" : false }
            definition.debugPrint is boolean;

            annotation { "Name" : "Show shelf points", "Default" : false,
                         "Description" : "Draw shelf (green) and inside (blue) offset points" }
            definition.debugShowShelfPoints is boolean;
        }
    }
    {
        // ── Step 1: Reference path ────────────────────────────────────────────
        var pathInfo = processShelfPath(context, id + "path", definition);

        if (size(definition.shelfRegions) == 0)
        {
            reportFeatureWarning(context, id, "No regions defined - nothing to generate");
            return;
        }

        var processedRegions = processShelfRegions(context, definition, pathInfo);
        validateShelfNoOverlap(context, id, processedRegions);
        var sortedRegions = sortShelfRegionsByTStart(processedRegions);

        if (definition.debugPrint)
        {
            println("=== SWShelf: refPathLength = " ~ toString(pathInfo.length / millimeter) ~ " mm");
            println("  refParam = " ~ toString(pathInfo.refParam));
            for (var reg in sortedRegions)
                println("  Region '" ~ reg.regionName ~ "': tStart=" ~ toString(reg.tStart) ~ "  tEnd=" ~ toString(reg.tEnd));
        }

        if (definition.debugStepThrough && definition.debugStep == 1) return;

        // ── Step 2: Footprint path + outward direction check ──────────────────
        var footprintEdges = qOwnedByBody(definition.footprintWire, EntityType.EDGE);
        var footprintPath;
        try
        {
            footprintPath = constructPath(context, footprintEdges);
        }
        catch
        {
            throw regenError("Footprint wire edges must form a continuous closed path");
        }

        var surfFaces   = evaluateQuery(context, qOwnedByBody(definition.bottomSurface, EntityType.FACE));
        var flipOutward = determineOutwardFlip(context, footprintPath, surfFaces);

        if (definition.debugStepThrough && definition.debugStep == 2) return;

        // ── Step 3: Sample footprint, compute offset points ───────────────────
        var blendZones = collectShelfBlendZones(context, id, definition, pathInfo, sortedRegions);

        var shelfPts  = buildFootprintOffsetPoints(context, definition, pathInfo,
                            footprintPath, sortedRegions, blendZones, surfFaces, flipOutward,
                            0 * meter);
        var insidePts = buildFootprintOffsetPoints(context, definition, pathInfo,
                            footprintPath, sortedRegions, blendZones, surfFaces, flipOutward,
                            -definition.sidewallWidth);

        if (definition.debugShowShelfPoints)
        {
            for (var pt in shelfPts)  debug(context, pt, DebugColor.GREEN);
            for (var pt in insidePts) debug(context, pt, DebugColor.BLUE);
        }

        if (definition.debugStepThrough && definition.debugStep == 3) return;

        // ── Step 4: Fit and output curves ─────────────────────────────────────
        var shelfWire  = buildShelfWire(context, id + "shelfWire",  definition, shelfPts);
        var insideWire = buildShelfWire(context, id + "insideWire", definition, insidePts);

        setProperty(context, { "entities" : shelfWire,
                                "propertyType" : PropertyType.NAME, "value" : "SW Shelf wire" });
        setProperty(context, { "entities" : insideWire,
                                "propertyType" : PropertyType.NAME, "value" : "SW Inside wire" });
    });


// ─── Reference path ───────────────────────────────────────────────────────────

function processShelfPath(context is Context, id is Id, definition is map) returns map
{
    var pathEdges = qOwnedByBody(definition.refWire, EntityType.EDGE);
    var refPath;
    try
    {
        refPath = constructPath(context, pathEdges);
    }
    catch
    {
        throw regenError("Reference wire edges must form a continuous path");
    }

    var endpoints  = evPathTangentLines(context, refPath, [0, 1]);
    var stdDir     = endpoints.tangentLines[0].origin[0] < endpoints.tangentLines[1].origin[0];
    var pathLength = evPathLength(context, refPath);
    var refX       = resolveShelfPoint(context, definition.refPoint)[0];
    var refParam   = findPathParamAtX(context, refPath, refX);

    return { "path" : refPath, "length" : pathLength, "refParam" : refParam, "stdDir" : stdDir };
}


// ─── Outward direction ────────────────────────────────────────────────────────

/**
 * Samples 20 points on the footprint, finds the highest-Y one, and checks
 * whether cross(surfaceNormal, tangent) points toward +Y at that location.
 * Returns true if the cross product must be negated to get the outward direction.
 */
function determineOutwardFlip(context is Context, footprintPath is Path, surfFaces is array) returns boolean
{
    var params = [];
    for (var i = 0; i < 20; i += 1)
        params = append(params, i / 20.0);
    var tls = evPathTangentLines(context, footprintPath, params).tangentLines;

    var maxY    = -1e10 * meter;
    var maxYIdx = 0;
    for (var i = 0; i < 20; i += 1)
    {
        if (tls[i].origin[1] > maxY)
        {
            maxY    = tls[i].origin[1];
            maxYIdx = i;
        }
    }

    var testPt      = tls[maxYIdx].origin;
    var testTangent = tls[maxYIdx].direction;
    var testNormal  = surfaceNormalAt(context, surfFaces, testPt);
    var candidate   = cross(testNormal, testTangent);

    // At the highest-Y point on the footprint, outward must have positive Y
    return candidate[1] < 0;
}


/**
 * Returns the surface normal of the bottom surface at pt (which lies on or near it).
 */
function surfaceNormalAt(context is Context, surfFaces is array, pt is Vector) returns Vector
{
    var bestDist = 1e10 * meter;
    var bestFace = surfFaces[0];
    var bestUV   = vector(0.5, 0.5);

    for (var face in surfFaces)
    {
        var d = evDistance(context, { "side0" : pt, "side1" : face });
        if (d.distance < bestDist)
        {
            bestDist = d.distance;
            bestFace = face;
            bestUV   = d.sides[1].parameter;
        }
    }

    return evFaceTangentPlane(context, { "face" : bestFace, "parameter" : bestUV }).normal;
}


// ─── Region processing ────────────────────────────────────────────────────────

function processShelfRegions(context is Context, definition is map, pathInfo is map) returns array
{
    var sign      = pathInfo.stdDir ? 1 : -1;
    var processed = [];

    for (var i = 0; i < size(definition.shelfRegions); i += 1)
    {
        var region = definition.shelfRegions[i];
        region.regionNum = i;

        var tStart;
        var tEnd;

        if (region.extentType == ShelfExtentType.X_EXTENTS)
        {
            tStart = pathInfo.refParam + sign * (region.regionStart / pathInfo.length);
            tEnd   = pathInfo.refParam + sign * (region.regionEnd   / pathInfo.length);
        }
        else // QUERY
        {
            var queryPts = evaluateQuery(context, region.extentQueries);
            if (size(queryPts) != 2)
                throw regenError("Region '" ~ region.regionName ~ "': extent query must resolve to exactly 2 points");

            var pt0 = resolveShelfPoint(context, queryPts[0]);
            var pt1 = resolveShelfPoint(context, queryPts[1]);

            var d0 = evDistancePath(context, { "side0" : pathInfo.path, "side1" : pt0 });
            var d1 = evDistancePath(context, { "side0" : pathInfo.path, "side1" : pt1 });

            tStart = min(d0.sides[0].pathParam, d1.sides[0].pathParam);
            tEnd   = max(d0.sides[0].pathParam, d1.sides[0].pathParam);
        }

        tStart = min(max(tStart, 0), 1);
        tEnd   = min(max(tEnd,   0), 1);
        if (tStart > tEnd) { var tmp = tStart; tStart = tEnd; tEnd = tmp; }

        region.tStart = tStart;
        region.tEnd   = tEnd;
        region.length = (tEnd - tStart) * pathInfo.length;

        processed = append(processed, region);
    }
    return processed;
}


function validateShelfNoOverlap(context is Context, id is Id, regions is array)
{
    var eps = 1e-6;
    for (var i = 0; i < size(regions); i += 1)
    {
        for (var j = i + 1; j < size(regions); j += 1)
        {
            var a = regions[i];
            var b = regions[j];
            if (a.tStart < b.tEnd - eps && b.tStart < a.tEnd - eps)
                throw regenError("Regions '" ~ a.regionName ~ "' and '" ~ b.regionName ~ "' overlap.");
        }
    }
}


function sortShelfRegionsByTStart(regions is array) returns array
{
    var sorted    = [];
    var remaining = regions;
    for (var i = 0; i < size(regions); i += 1)
    {
        var minIdx = 0;
        for (var j = 1; j < size(remaining); j += 1)
        {
            if (remaining[j].tStart < remaining[minIdx].tStart)
                minIdx = j;
        }
        sorted = append(sorted, remaining[minIdx]);
        var next = [];
        for (var j = 0; j < size(remaining); j += 1)
        {
            if (j != minIdx) next = append(next, remaining[j]);
        }
        remaining = next;
    }
    return sorted;
}


// ─── Query resolution ─────────────────────────────────────────────────────────

function resolveShelfPoint(context is Context, q is Query) returns Vector
{
    if (!isQueryEmpty(context, qBodyType(q, BodyType.MATE_CONNECTOR)))
        return evMateConnector(context, { "mateConnector" : q }).origin;
    else if (!isQueryEmpty(context, qGeometry(q, GeometryType.PLANE)))
        return evPlane(context, { "face" : q }).origin;
    else
        return evVertexPoint(context, { "vertex" : q });
}


// ─── Profile value ────────────────────────────────────────────────────────────

function shelfProfileValueAt(t is number, region is map) returns ValueWithUnits
{
    var s;
    if (region.regionType == ShelfRegionType.LINEAR)
    {
        s = t;
    }
    else if (region.regionType == ShelfRegionType.QUADRATIC)
    {
        if (region.quadZeroSlope == ShelfQuadraticZeroSlope.AT_END)
            s = 2 * t - t * t;
        else // AT_START
            s = t * t;
    }
    else // SMOOTH (smootherstep C2)
    {
        s = t * t * t * (10 + t * (6 * t - 15));
    }
    return region.startShelfDepth + (region.endShelfDepth - region.startShelfDepth) * s;
}


// ─── Shelf depth evaluation ───────────────────────────────────────────────────

function shelfRegionDepthAt(region is map, t is number) returns ValueWithUnits
{
    var tNorm = min(max((t - region.tStart) / (region.tEnd - region.tStart), 0), 1);
    return shelfProfileValueAt(tNorm, region);
}


function computeShelfDepthAtT(sortedRegions is array, t is number) returns ValueWithUnits
{
    if (size(sortedRegions) == 0)
        return 0 * meter;

    if (t <= sortedRegions[0].tStart)
        return sortedRegions[0].startShelfDepth;

    var last = sortedRegions[size(sortedRegions) - 1];
    if (t >= last.tEnd)
        return last.endShelfDepth;

    for (var reg in sortedRegions)
    {
        if (t >= reg.tStart && t <= reg.tEnd)
            return shelfRegionDepthAt(reg, t);
    }

    // Gap between regions - linear fill
    for (var i = 0; i < size(sortedRegions) - 1; i += 1)
    {
        var regA = sortedRegions[i];
        var regB = sortedRegions[i + 1];
        if (t > regA.tEnd && t < regB.tStart)
        {
            var span = regB.tStart - regA.tEnd;
            if (span < 1e-10) return regA.endShelfDepth;
            var s = (t - regA.tEnd) / span;
            return regA.endShelfDepth + (regB.startShelfDepth - regA.endShelfDepth) * s;
        }
    }

    return sortedRegions[0].startShelfDepth;
}


function shelfDepthAtT(sortedRegions is array, blendZones is array, t is number) returns ValueWithUnits
{
    for (var bz in blendZones)
    {
        if (t < bz.tBlendStart || t > bz.tBlendEnd)
            continue;

        var L  = bz.tBlendEnd - bz.tBlendStart;
        var s  = (t - bz.tBlendStart) / L;
        var h0 = shelfRegionDepthAt(bz.regA, bz.tBlendStart);
        var h1 = shelfRegionDepthAt(bz.regB, bz.tBlendEnd);
        var contStart = bz.intr.startContinuity;
        var contEnd   = bz.intr.endContinuity;

        var m0 = 0 * meter;
        var k0 = 0 * meter;
        var m1 = 0 * meter;
        var k1 = 0 * meter;

        if (contStart == GeometricContinuity.G1 || contStart == GeometricContinuity.G2)
        {
            var dt  = 1e-4;
            var tHi = min(bz.tBlendStart + dt, bz.regA.tEnd);
            var tLo = max(bz.tBlendStart - dt, bz.regA.tStart);
            var h   = (tHi - tLo) / 2;
            m0 = (shelfRegionDepthAt(bz.regA, tHi) - shelfRegionDepthAt(bz.regA, tLo)) / (2 * h) * L;
            if (contStart == GeometricContinuity.G2)
            {
                var dMid = shelfRegionDepthAt(bz.regA, bz.tBlendStart);
                k0 = (shelfRegionDepthAt(bz.regA, tHi) - 2 * dMid + shelfRegionDepthAt(bz.regA, tLo)) / (h * h) * L * L;
            }
        }

        if (contEnd == GeometricContinuity.G1 || contEnd == GeometricContinuity.G2)
        {
            var dt  = 1e-4;
            var tHi = min(bz.tBlendEnd + dt, bz.regB.tEnd);
            var tLo = max(bz.tBlendEnd - dt, bz.regB.tStart);
            var h   = (tHi - tLo) / 2;
            m1 = (shelfRegionDepthAt(bz.regB, tHi) - shelfRegionDepthAt(bz.regB, tLo)) / (2 * h) * L;
            if (contEnd == GeometricContinuity.G2)
            {
                var dMid = shelfRegionDepthAt(bz.regB, bz.tBlendEnd);
                k1 = (shelfRegionDepthAt(bz.regB, tHi) - 2 * dMid + shelfRegionDepthAt(bz.regB, tLo)) / (h * h) * L * L;
            }
        }

        return shelfHermiteAt(s, h0, m0, k0, h1, m1, k1, contStart, contEnd);
    }

    return computeShelfDepthAtT(sortedRegions, t);
}


// ─── Hermite blend polynomial ─────────────────────────────────────────────────

function shelfHermiteAt(s is number,
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
        return h0 * (1 - s) + h1 * s;
    else if (matchSlopeStart && !matchCurvStart && !matchSlopeEnd)
        return h0 + m0 * s + (h1 - h0 - m0) * s2;
    else if (!matchSlopeStart && matchSlopeEnd && !matchCurvEnd)
    {
        var dh = h1 - h0;
        return h0 + (2 * dh - m1) * s + (m1 - dh) * s2;
    }
    else if (matchSlopeStart && !matchCurvStart && matchSlopeEnd && !matchCurvEnd)
        return h0 * (2*s3 - 3*s2 + 1) + m0 * (s3 - 2*s2 + s)
             + h1 * (-2*s3 + 3*s2)    + m1 * (s3 - s2);
    else if (matchCurvStart && !matchSlopeEnd)
        return h0 + m0 * s + (k0 / 2) * s2 + (h1 - h0 - m0 - k0 / 2) * s3;
    else if (!matchSlopeStart && matchCurvEnd)
    {
        var dh = h1 - h0;
        var d  = dh - m1 + k1 / 2;
        var c  = -(k1 + 3 * (dh - m1));
        var b  = 3 * dh - 2 * m1 + k1 / 2;
        return h0 + b * s + c * s2 + d * s3;
    }
    else if (matchCurvStart && matchSlopeEnd && !matchCurvEnd)
    {
        var dh = h1 - h0;
        var a4 = m1 + 2 * m0 + k0 / 2 - 3 * dh;
        var a3 = 4 * dh - 3 * m0 - k0 - m1;
        return h0 + m0 * s + (k0 / 2) * s2 + a3 * s3 + a4 * s4;
    }
    else if (matchSlopeStart && !matchCurvStart && matchCurvEnd)
    {
        var H = h1 - h0 - m0;
        var M = m1 - m0;
        var K = k1;
        var e = (K - 4 * M + 6 * H) / 2;
        var d = 5 * M - 8 * H - K;
        var c = 6 * H - 3 * M + K / 2;
        return h0 + m0 * s + c * s2 + d * s3 + e * s4;
    }
    else // G2+G2 quintic
        return h0 * (1 - 10*s3 + 15*s4 - 6*s5)
             + m0 * (s - 6*s3 + 8*s4 - 3*s5)
             + k0 * (s2/2 - 3*s3/2 + 3*s4/2 - s5/2)
             + h1 * (10*s3 - 15*s4 + 6*s5)
             + m1 * (-4*s3 + 7*s4 - 3*s5)
             + k1 * (s3/2 - s4 + s5/2);
}


// ─── Blend zone collection ────────────────────────────────────────────────────

function collectShelfBlendZones(context is Context, id is Id, definition is map,
    pathInfo is map, sortedRegions is array) returns array
{
    var blendZones = [];
    for (var intr in definition.shelfIntersections)
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
            reportFeatureWarning(context, id, "Blend start exceeds extent of '" ~ regA.regionName ~ "'. Clamping.");
            tBlendStart = regA.tStart;
        }
        if (tBlendEnd > regB.tEnd)
        {
            reportFeatureWarning(context, id, "Blend end exceeds extent of '" ~ regB.regionName ~ "'. Clamping.");
            tBlendEnd = regB.tEnd;
        }
        if (tBlendStart >= tBlendEnd)
        {
            reportFeatureWarning(context, id, "Zero-length blend between '" ~ regA.regionName ~ "' and '" ~ regB.regionName ~ "'. Skipping.");
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
    return blendZones;
}


// ─── Footprint offset sampling ────────────────────────────────────────────────

/**
 * Samples n points uniformly around the closed footprint wire and offsets each
 * point outward by shelfDepth + depthOffset along the bottom surface.
 *
 * Outward direction at each sample: cross(surfaceNormal, wireTangent), with sign
 * determined by flipOutward (from determineOutwardFlip).
 *
 * Depth is found by projecting each footprint sample onto the refWire to get a
 * refWire parameter, then evaluating shelfDepthAtT.
 */
function buildFootprintOffsetPoints(context is Context, definition is map, pathInfo is map,
    footprintPath is Path, sortedRegions is array, blendZones is array,
    surfFaces is array, flipOutward is boolean,
    depthOffset is ValueWithUnits) returns array
{
    var n = definition.samplingDensity;

    // Sample uniformly around the closed loop, excluding the repeated endpoint
    var params = [];
    for (var i = 0; i < n; i += 1)
        params = append(params, i / n);

    var tls = evPathTangentLines(context, footprintPath, params).tangentLines;
    var pts = [];

    for (var i = 0; i < n; i += 1)
    {
        var footPt  = tls[i].origin;
        var tangent = tls[i].direction;

        // Surface normal at this footprint point
        var surfNormal = surfaceNormalAt(context, surfFaces, footPt);

        // Outward direction in the surface tangent plane
        var outward = cross(surfNormal, tangent);
        if (flipOutward) outward = -outward;
        var outLen = norm(outward);
        if (outLen > 1e-10)
            outward = outward / outLen;
        else
            outward = vector(0.0, 1.0, 0.0);

        // Map footprint point to refWire parameter via closest-point projection
        var refDist = evDistancePath(context, { "side0" : pathInfo.path, "side1" : footPt });
        var refT    = refDist.sides[0].pathParam;

        // Evaluate shelf depth at this refWire parameter
        var depth = shelfDepthAtT(sortedRegions, blendZones, refT) + depthOffset;

        pts = append(pts, footPt + depth * outward);
    }
    return pts;
}


// ─── Wire construction ────────────────────────────────────────────────────────

function buildShelfWire(context is Context, id is Id, definition is map, pts is array) returns Query
{
    if (size(pts) < 3)
        throw regenError("Too few sample points to build a wire");

    var bspline = approximateSpline(context, {
        "degree"           : definition.approxDegree,
        "tolerance"        : definition.approxTolerance,
        "isPeriodic"       : true,
        "maxControlPoints" : definition.approxMaxCP,
        "targets"          : [approximationTarget({ "positions" : pts })]
    })[0];

    opCreateBSplineCurve(context, id + "curve", { "bSplineCurve" : bspline });
    return qCreatedBy(id + "curve", EntityType.BODY);
}
