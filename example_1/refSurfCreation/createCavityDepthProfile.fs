FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");
export import(path : "onshape/std/geometriccontinuity.gen.fs", version : "2892.0");

// real import path for refSurfUtils (managed by sync)
import(path : "d41884a96244793beb462449", version : "4ef3ccb7ad146786136be32b");


// ─── Enums ────────────────────────────────────────────────────────────────────

export enum CavityDepthInputType
{
    CAVITY_DEPTH,
    SIDEWALL
}

export enum RegionGenerationType
{
    PROVIDE_EDGES,
    GENERATE_PROCEDURALLY
}

export enum RegionType
{
    LINEAR,
    QUADRATIC,
    LOGISTIC
}

export enum RegionExtentType
{
    QUERY,
    X_EXTENTS
}


// ─── Bounds ───────────────────────────────────────────────────────────────────

export const SidewallBottomOffsetBounds = {(millimeter) : [0.1, 2, 5]}    as LengthBoundSpec;
export const SidewallTopOffsetBounds    = {(millimeter) : [0, 0, 5]}      as LengthBoundSpec;
export const RegionOffsetBounds         = {(millimeter) : [0, 1, 20]}     as LengthBoundSpec;
export const ApproxToleranceBounds      = {(millimeter) : [0.001, 0.01, 1]} as LengthBoundSpec;
export const ApproxDegreeBounds         = {(unitless) : [2, 3, 5]}        as IntegerBoundSpec;
export const ApproxMaxCPBounds          = {(unitless) : [10, 100, 500]}   as IntegerBoundSpec;
export const SamplingDensityBounds      = {(unitless) : [5, 50, 500]}     as IntegerBoundSpec;


// ─── Editing logic ────────────────────────────────────────────────────────────

export function generateCavityDepthProfileEditingLogic(context is Context, id is Id,
    oldDefinition is map, definition is map, isCreating is boolean,
    specifiedParameters is map, hiddenBodies is Query) returns map
{
    // Process path — bail silently if inputs are incomplete
    var pathInfo = undefined;
    try silent
    {
        pathInfo = processPath(context, id + "elPath", definition);
    }
    if (pathInfo == undefined)
        return definition;

    var sortedRegions = [];
    try silent
    {
        var processed = processRegions(context, definition, pathInfo);

        // Write computed length back to definition.regions
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

        // Default entry — will be overridden by preserved user settings below
        var entry = {
            "isValid"         : true,
            "intersectionNum" : i,
            "region1"         : regA.regionName,
            "region2"         : regB.regionName,
            "blend"           : false,
            "startContinuity" : GeometricContinuity.G0,
            "startDist"       : 10 * millimeter,
            "endContinuity"   : GeometricContinuity.G0,
            "endDist"         : 10 * millimeter
        };

        // Preserve user-configured settings for this region pair
        for (var existing in definition.intersections)
        {
            if (existing.region1 == regA.regionName && existing.region2 == regB.regionName)
            {
                entry = mergeMaps(entry, existing);
                entry.isValid         = true;
                entry.intersectionNum = i;
                break;
            }
        }
        newIntersections = append(newIntersections, entry);
    }
    definition.intersections = newIntersections;

    return definition;
}


// ─── Feature ──────────────────────────────────────────────────────────────────

annotation { "Feature Type Name" : "Generate cavity depth profile",
             "Feature Type Description" : "Generates a cavity depth profile based on user inputs",
             "Editing Logic Function" : "generateCavityDepthProfileEditingLogic" }
export const generateCavityDepthProfile = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Input type", "Default" : CavityDepthInputType.SIDEWALL,
                     "UIHint" : UIHint.HORIZONTAL_ENUM }
        definition.cdInputType is CavityDepthInputType;

        annotation { "Name" : "Bottom wire", "Filter" : BodyType.WIRE, "MaxNumberOfPicks" : 1,
                     "Description" : "Bottom wire of ski or snowboard" }
        definition.bottomWire is Query;

        annotation { "Name" : "Reference point",
                     "Filter" : EntityType.VERTEX || GeometryType.PLANE || BodyType.MATE_CONNECTOR,
                     "MaxNumberOfPicks" : 1,
                     "Description" : "Defines X = 0 along the wire" }
        definition.refPoint is Query;

        if (definition.cdInputType == CavityDepthInputType.CAVITY_DEPTH)
        {
            annotation { "Name" : "Top wire", "Filter" : BodyType.WIRE, "MaxNumberOfPicks" : 1,
                         "Description" : "Top wire of the ski or snowboard profile" }
            definition.topWire is Query;
        }

        if (definition.cdInputType == CavityDepthInputType.SIDEWALL)
        {
            annotation { "Name" : "Sidewall bottom offset",
                         "Description" : "Fixed offset above the bottom wire where the sidewall region begins" }
            isLength(definition.swBottomOffset, SidewallBottomOffsetBounds);

            annotation { "Name" : "Sidewall top offset",
                         "Description" : "Fixed offset added above the sidewall height" }
            isLength(definition.swTopOffset, SidewallTopOffsetBounds);
        }

        annotation { "Name" : "Regions", "Item name" : "Region", "Item label template" : "#regionName" }
        definition.regions is array;
        for (var region in definition.regions)
        {
            annotation { "Name" : "Region type", "Default" : RegionType.LINEAR,
                         "UIHint" : UIHint.HORIZONTAL_ENUM }
            region.regionType is RegionType;

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
                             "Description" : "Distance along wire from reference point (negative = toward tail)" }
                isLength(region.regionStart, LENGTH_BOUNDS);

                annotation { "Name" : "Region end",
                             "Description" : "Distance along wire from reference point" }
                isLength(region.regionEnd, LENGTH_BOUNDS);
            }

            annotation { "Name" : "Start offset",
                         "Description" : "Sidewall height or cavity depth at the start of this region" }
            isLength(region.startOffset, RegionOffsetBounds);

            annotation { "Name" : "End offset",
                         "Description" : "Sidewall height or cavity depth at the end of this region" }
            isLength(region.endOffset, RegionOffsetBounds);

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
                annotation { "Name" : "Start continuity" }
                intersection.startContinuity is GeometricContinuity;

                annotation { "Name" : "Start distance",
                             "Description" : "Distance before junction where blend begins" }
                isLength(intersection.startDist, LENGTH_BOUNDS);

                annotation { "Name" : "End continuity" }
                intersection.endContinuity is GeometricContinuity;

                annotation { "Name" : "End distance",
                             "Description" : "Distance after junction where blend ends" }
                isLength(intersection.endDist, LENGTH_BOUNDS);
            }
        }

        annotation { "Group Name" : "Approximation", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Sampling density", "Description" : "Points sampled per region (and per blend)" }
            isInteger(definition.samplingDensity, SamplingDensityBounds);

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
                         "Description" : "Draw XYZ coordinate frame at each region start and end point" }
            definition.showRefFrames is boolean;

            annotation { "Name" : "Show input wires",
                         "Description" : "Highlight bottom wire (green) and top wire if provided (blue)" }
            definition.showInputWires is boolean;

            annotation { "Name" : "Show regions",
                         "Description" : "Highlight region output curves (cyan)" }
            definition.showRegions is boolean;

            annotation { "Name" : "Show blends",
                         "Description" : "Highlight blend output curves (yellow)" }
            definition.showBlends is boolean;

            annotation { "Name" : "Print curve details",
                         "Description" : "Print BSpline metadata (degree, CP count, knots) to FeatureStudio console" }
            definition.printCurveDetails is boolean;

            annotation { "Name" : "Print reference data",
                         "Description" : "Print path length, refParam, stdDir, and 3D positions at path endpoints" }
            definition.printRefData is boolean;

            annotation { "Name" : "Print region points",
                         "Description" : "Print computed tStart/tEnd and their 3D positions for each region" }
            definition.printRegionPoints is boolean;
        }
    }
    {
        var pathInfo = processPath(context, id + "processRefPath", definition);

        if (size(definition.regions) == 0)
        {
            reportFeatureWarning(context, id, "No regions defined — nothing to generate");
            return;
        }

        var processedRegions = processRegions(context, definition, pathInfo);
        validateNoOverlap(context, id, processedRegions);

        var sortedRegions = sortRegionsByTStart(processedRegions);
        buildOutputWire(context, id, definition, pathInfo, sortedRegions);

        // ── Debug ──────────────────────────────────────────────────────────────
        if (definition.printRefData)
        {
            var ep0    = evPathTangentLines(context, pathInfo.path, [0]).tangentLines[0].origin;
            var ep1    = evPathTangentLines(context, pathInfo.path, [1]).tangentLines[0].origin;
            var refPt3d = evPathTangentLines(context, pathInfo.path, [pathInfo.refParam]).tangentLines[0].origin;
            println("=== Reference Path Data ===");
            println("  path edges:  " ~ toString(size(pathInfo.path.edges)));
            println("  pathLength:  " ~ toString(pathInfo.length / millimeter) ~ " mm");
            println("  stdDir:      " ~ toString(pathInfo.stdDir));
            println("  refParam:    " ~ toString(pathInfo.refParam));
            println("  refPoint3d:  [" ~ toString(refPt3d[0] / millimeter) ~ ", "
                                       ~ toString(refPt3d[1] / millimeter) ~ ", "
                                       ~ toString(refPt3d[2] / millimeter) ~ "] mm");
            println("  t=0 point:   [" ~ toString(ep0[0] / millimeter) ~ ", "
                                       ~ toString(ep0[1] / millimeter) ~ ", "
                                       ~ toString(ep0[2] / millimeter) ~ "] mm (x=" ~ toString(ep0[0] / millimeter) ~ ")");
            println("  t=1 point:   [" ~ toString(ep1[0] / millimeter) ~ ", "
                                       ~ toString(ep1[1] / millimeter) ~ ", "
                                       ~ toString(ep1[2] / millimeter) ~ "] mm (x=" ~ toString(ep1[0] / millimeter) ~ ")");
        }

        if (definition.printRegionPoints)
        {
            var sign = pathInfo.stdDir ? 1 : -1;
            println("=== Region Points ===");
            for (var reg in sortedRegions)
            {
                var ptStart = evPathTangentLines(context, pathInfo.path, [reg.tStart]).tangentLines[0].origin;
                var ptEnd   = evPathTangentLines(context, pathInfo.path, [reg.tEnd]).tangentLines[0].origin;
                println("  Region: '" ~ reg.regionName ~ "'");
                println("    tStart:       " ~ toString(reg.tStart)
                    ~ "  →  [" ~ toString(ptStart[0] / millimeter) ~ ", "
                               ~ toString(ptStart[1] / millimeter) ~ ", "
                               ~ toString(ptStart[2] / millimeter) ~ "] mm");
                println("    tEnd:         " ~ toString(reg.tEnd)
                    ~ "  →  [" ~ toString(ptEnd[0] / millimeter) ~ ", "
                               ~ toString(ptEnd[1] / millimeter) ~ ", "
                               ~ toString(ptEnd[2] / millimeter) ~ "] mm");
                println("    length:       " ~ toString(reg.length / millimeter) ~ " mm");
                println("    startOffset:  " ~ toString(reg.startOffset / millimeter) ~ " mm");
                println("    endOffset:    " ~ toString(reg.endOffset / millimeter) ~ " mm");
                if (reg.extentType == RegionExtentType.X_EXTENTS)
                {
                    var rawTStart = pathInfo.refParam + sign * (reg.regionStart / pathInfo.length);
                    var rawTEnd   = pathInfo.refParam + sign * (reg.regionEnd   / pathInfo.length);
                    println("    input xStart: " ~ toString(reg.regionStart / millimeter) ~ " mm  →  rawT=" ~ toString(rawTStart));
                    println("    input xEnd:   " ~ toString(reg.regionEnd   / millimeter) ~ " mm  →  rawT=" ~ toString(rawTEnd));
                }
            }
        }

        if (definition.showInputWires)
        {
            debug(context, definition.bottomWire, DebugColor.GREEN);
            if (definition.cdInputType == CavityDepthInputType.CAVITY_DEPTH)
                debug(context, definition.topWire, DebugColor.BLUE);
        }

        if (definition.showRefFrames)
        {
            var axisLen = 20 * millimeter;
            for (var reg in sortedRegions)
            {
                for (var tp in [reg.tStart, reg.tEnd])
                {
                    var tl     = evPathTangentLines(context, pathInfo.path, [tp]).tangentLines[0];
                    var origin = tl.origin;
                    var xAxis  = tl.direction[0] < 0 ? -tl.direction : tl.direction;
                    var zAxis  = computeEdgeNormal(tl.direction);
                    debug(context, coordSystem(origin, xAxis, zAxis));
                }
            }
        }
    });


// ─── Path processing ──────────────────────────────────────────────────────────

function processPath(context is Context, id is Id, definition is map) returns map
{
    var wireBody = definition.bottomWire; // X is always measured along the bottom wire
    var pathEdges = qUnion([qOwnedByBody(wireBody, EntityType.EDGE)]);
    var refPath;
    try
    {
        refPath = constructPath(context, pathEdges);
    }
    catch (error)
    {
        throw regenError("Reference wire edges must form a continuous path");
    }

    var pathRefPoint = evDistancePath(context, {
        "side0" : refPath,
        "side1" : definition.refPoint
    });

    var endpoints = evPathTangentLines(context, refPath, [0, 1]);
    var stdDir = endpoints.tangentLines[0].origin[0] < endpoints.tangentLines[1].origin[0];
    var pathLength = evPathLength(context, refPath);

    return {
        "path"     : refPath,
        "length"   : pathLength,
        "refParam" : pathRefPoint.sides[0].pathParam,
        "stdDir"   : stdDir
    };
}


// ─── Region processing ────────────────────────────────────────────────────────

function processRegions(context is Context, definition is map, pathInfo is map) returns array
{
    var sign = pathInfo.stdDir ? 1 : -1;
    var processed = [];

    for (var i = 0; i < size(definition.regions); i += 1)
    {
        var region = definition.regions[i];
        region.regionNum = i;

        var tStart;
        var tEnd;

        if (region.extentType == RegionExtentType.X_EXTENTS)
        {
            tStart = pathInfo.refParam + sign * (region.regionStart / pathInfo.length);
            tEnd   = pathInfo.refParam + sign * (region.regionEnd   / pathInfo.length);
        }
        else // QUERY
        {
            var queryPts = evaluateQuery(context, region.extentQueries);
            if (size(queryPts) != 2)
                throw regenError("Region '" ~ region.regionName ~ "': extent query must resolve to exactly 2 points");

            var pt0 = resolveQueryToPoint(context, queryPts[0]);
            var pt1 = resolveQueryToPoint(context, queryPts[1]);

            var d0 = evDistancePath(context, { "side0" : pathInfo.path, "side1" : pt0 });
            var d1 = evDistancePath(context, { "side0" : pathInfo.path, "side1" : pt1 });

            tStart = min(d0.sides[0].pathParam, d1.sides[0].pathParam);
            tEnd   = max(d0.sides[0].pathParam, d1.sides[0].pathParam);
        }

        // Clamp and ensure start < end
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
    var eps = 1e-6;
    for (var i = 0; i < size(regions); i += 1)
    {
        for (var j = i + 1; j < size(regions); j += 1)
        {
            var a = regions[i];
            var b = regions[j];
            if (a.tStart < b.tEnd - eps && b.tStart < a.tEnd - eps)
                throw regenError("Regions '" ~ a.regionName ~ "' and '" ~ b.regionName ~ "' overlap. Regions must not overlap.");
        }
    }
}


function sortRegionsByTStart(regions is array) returns array
{
    var sorted = [];
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

        // Remove minIdx from remaining
        var next = [];
        for (var j = 0; j < size(remaining); j += 1)
        {
            if (j != minIdx)
                next = append(next, remaining[j]);
        }
        remaining = next;
    }
    return sorted;
}


// ─── Point resolution ─────────────────────────────────────────────────────────

function resolveQueryToPoint(context is Context, q is Query) returns Vector
{
    if (!isQueryEmpty(context, qBodyType(q, BodyType.MATE_CONNECTOR)))
        return evMateConnector(context, { "mateConnector" : q }).origin;
    else if (!isQueryEmpty(context, qGeometry(q, GeometryType.PLANE)))
        return evPlane(context, { "face" : q }).origin;
    else
        return evVertexPoint(context, { "vertex" : q });
}


// ─── Normal convention ────────────────────────────────────────────────────────

/**
 * Convention: negate tangent so X component is positive, then cross with +Y.
 * cross([a, b, c], [0, 1, 0]) = [-c, 0, a]. With a > 0 and wire lying in XY,
 * result ≈ [0, 0, a] — positive Z (upward out of ski base).
 */
function computeEdgeNormal(tangentDir is Vector) returns Vector
{
    var dir = tangentDir;
    if (dir[0] < 0)
        dir = -dir;
    return normalize(cross(dir, vector(0, 1, 0)));
}


// ─── Profile value ────────────────────────────────────────────────────────────

/**
 * Returns the profile offset at normalized position t ∈ [0,1] within a region.
 *   LINEAR    : linear ramp
 *   QUADRATIC : smoothstep  (3t²−2t³)   — C1 at endpoints
 *   LOGISTIC  : smootherstep(6t⁵−15t⁴+10t³) — C2 at endpoints
 */
function profileValueAt(t is number, region is map) returns ValueWithUnits
{
    var s;
    if (region.regionType == RegionType.LINEAR)
    {
        s = t;
    }
    else if (region.regionType == RegionType.QUADRATIC)
    {
        s = t * t * (3 - 2 * t);
    }
    else // LOGISTIC
    {
        s = t * t * t * (10 + t * (6 * t - 15));
    }
    return region.startOffset + (region.endOffset - region.startOffset) * s;
}


// ─── Offset point computation ─────────────────────────────────────────────────

/**
 * Computes the 3D output point for a given path parameter t and profile offset value.
 *   SIDEWALL     : bottomPoint + (swBottomOffset + offsetValue + swTopOffset) * normal
 *   CAVITY_DEPTH : topWirePoint (found via raycast) - offsetValue * normal
 */
function computeOffsetPoint(context is Context, definition is map, pathInfo is map,
    t is number, offsetValue is ValueWithUnits) returns Vector
{
    var tl = evPathTangentLines(context, pathInfo.path, [t]).tangentLines[0];
    var basePoint = tl.origin;
    var normal    = computeEdgeNormal(tl.direction);

    if (definition.cdInputType == CavityDepthInputType.SIDEWALL)
    {
        var totalOffset = definition.swBottomOffset + offsetValue + definition.swTopOffset;
        return basePoint + totalOffset * normal;
    }
    else // CAVITY_DEPTH
    {
        var ray  = line(basePoint, normal);
        var hits = evRaycast(context, {
            "ray"      : ray,
            "entities" : qOwnedByBody(definition.topWire, EntityType.EDGE),
            "closest"  : true
        });
        if (size(hits) == 0)
            throw regenError("No intersection with top wire at path parameter " ~ toString(t) ~
                             ". Verify the top wire is above the bottom wire along its full extent.");
        return hits[0].intersection - offsetValue * normal;
    }
}


// ─── Point arrays ─────────────────────────────────────────────────────────────

/**
 * Samples n points along [tSegStart, tSegEnd], evaluating the profile value
 * from `region` at each. tSegStart/tSegEnd may differ from region.tStart/tEnd
 * when the segment is trimmed by an adjacent blend.
 */
function generateSegmentPoints(context is Context, definition is map, pathInfo is map,
    region is map, tSegStart is number, tSegEnd is number) returns array
{
    var n = definition.samplingDensity;
    var points = [];
    for (var i = 0; i < n; i += 1)
    {
        var t     = tSegStart + (tSegEnd - tSegStart) * i / (n - 1);
        var tNorm = min(max((t - region.tStart) / (region.tEnd - region.tStart), 0), 1);
        points = append(points, computeOffsetPoint(context, definition, pathInfo, t, profileValueAt(tNorm, region)));
    }
    return points;
}


/**
 * Samples n points across the blend zone [tBlendStart, tBlendEnd], interpolating
 * between valueStart (end of region A) and valueEnd (start of region B) using
 * smoothstep to guarantee C1 junctions with adjacent region curves.
 */
function generateBlendPoints(context is Context, definition is map, pathInfo is map,
    tBlendStart is number, tBlendEnd is number,
    valueStart is ValueWithUnits, valueEnd is ValueWithUnits) returns array
{
    var n = definition.samplingDensity;
    var points = [];
    for (var i = 0; i < n; i += 1)
    {
        var t     = tBlendStart + (tBlendEnd - tBlendStart) * i / (n - 1);
        var tNorm = i / (n - 1);
        var s     = tNorm * tNorm * (3 - 2 * tNorm); // smoothstep
        var offsetValue = valueStart + (valueEnd - valueStart) * s;
        points = append(points, computeOffsetPoint(context, definition, pathInfo, t, offsetValue));
    }
    return points;
}


// ─── Wire construction ────────────────────────────────────────────────────────

function buildOutputWire(context is Context, id is Id, definition is map,
    pathInfo is map, sortedRegions is array)
{
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
            if (reg.regionName == intr.region1)
                regA = reg;
            if (reg.regionName == intr.region2)
                regB = reg;
        }
        if (regA == undefined || regB == undefined)
            continue;

        var tJunction   = (regA.tEnd + regB.tStart) / 2.0;
        var tBlendStart = tJunction - intr.startDist / pathInfo.length;
        var tBlendEnd   = tJunction + intr.endDist   / pathInfo.length;

        blendZones = append(blendZones, {
            "tBlendStart" : tBlendStart,
            "tBlendEnd"   : tBlendEnd,
            "tJunction"   : tJunction,
            "regA"        : regA,
            "regB"        : regB
        });
    }

    // One BSpline wire per region (trimmed where blend zones border it)
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
            continue; // blend consumed entire region extent

        var pts = generateSegmentPoints(context, definition, pathInfo, reg, tSegStart, tSegEnd);
        if (size(pts) < 2)
            continue;

        var bspline = approximateSpline(context, {
            "degree"             : definition.approxDegree,
            "tolerance"          : definition.approxTolerance,
            "isPeriodic"         : false,
            "maxControlPoints"   : definition.approxMaxCP,
            "targets"            : [approximationTarget({ "positions" : pts })],
            "interpolateIndices" : [0, size(pts) - 1]
        })[0];

        var wireId = id + ("reg_" ~ toString(ri));
        opCreateBSplineCurve(context, wireId, { "bSplineCurve" : bspline });

        if (definition.printCurveDetails)
        {
            println("=== Region " ~ toString(ri) ~ " ('" ~ reg.regionName ~ "') ===");
            println("  degree:         " ~ toString(bspline.degree));
            println("  isPeriodic:     " ~ toString(bspline.isPeriodic));
            println("  control points: " ~ toString(size(bspline.controlPoints)));
            println("  knot count:     " ~ toString(size(bspline.knots)));
            println("  sample points:  " ~ toString(size(pts)));
            println("  t range:        [" ~ toString(tSegStart) ~ ", " ~ toString(tSegEnd) ~ "]");
        }
        if (definition.showRegions)
            addDebugEntities(context, qCreatedBy(wireId, EntityType.BODY), DebugColor.CYAN);
    }

    // One BSpline wire per active blend zone
    for (var bzi = 0; bzi < size(blendZones); bzi += 1)
    {
        var bz         = blendZones[bzi];
        var valueStart = profileValueAt(1.0, bz.regA);
        var valueEnd   = profileValueAt(0.0, bz.regB);

        var pts = generateBlendPoints(context, definition, pathInfo,
            bz.tBlendStart, bz.tBlendEnd, valueStart, valueEnd);
        if (size(pts) < 2)
            continue;

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

        if (definition.printCurveDetails)
        {
            println("=== Blend " ~ toString(bzi) ~ " ('" ~ bz.regA.regionName ~ "' → '" ~ bz.regB.regionName ~ "') ===");
            println("  degree:         " ~ toString(bspline.degree));
            println("  isPeriodic:     " ~ toString(bspline.isPeriodic));
            println("  control points: " ~ toString(size(bspline.controlPoints)));
            println("  knot count:     " ~ toString(size(bspline.knots)));
            println("  sample points:  " ~ toString(size(pts)));
        }
        if (definition.showBlends)
            addDebugEntities(context, qCreatedBy(wireId, EntityType.BODY), DebugColor.YELLOW);
    }
}
