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

export enum QuadraticZeroSlope
{
    AT_START,
    AT_END
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
            "intersectionNum" : i + 1,
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

annotation { "Feature Type Name" : "Generate cavity depth profile",
             "Feature Type Description" : "Generates a cavity depth profile based on user inputs",
             "Editing Logic Function" : "generateCavityDepthProfileEditingLogic" }
export const generateCavityDepthProfile = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Bottom wire", "Filter" : BodyType.WIRE, "MaxNumberOfPicks" : 1,
                     "Description" : "Bottom wire of ski or snowboard" }
        definition.bottomWire is Query;

        annotation { "Name" : "Reference point",
                     "Filter" : EntityType.VERTEX || GeometryType.PLANE || BodyType.MATE_CONNECTOR,
                     "MaxNumberOfPicks" : 1,
                     "Description" : "Defines X = 0 along the wire" }
        definition.refPoint is Query;

        annotation { "Name" : "Top wire", "Filter" : BodyType.WIRE, "MaxNumberOfPicks" : 1,
                     "Description" : "Top wire of the ski or snowboard profile (required for Cavity Depth regions)" }
        definition.topWire is Query;

        annotation { "Name" : "Sidewall bottom offset",
                     "Description" : "Fixed offset above the bottom wire where the sidewall region begins" }
        isLength(definition.swBottomOffset, SidewallBottomOffsetBounds);

        annotation { "Name" : "Sidewall top offset",
                     "Description" : "Fixed offset added above the sidewall height" }
        isLength(definition.swTopOffset, SidewallTopOffsetBounds);

        annotation { "Name" : "Regions", "Item name" : "Region", "Item label template" : "#regionName",
                     "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS }
        definition.regions is array;
        for (var region in definition.regions)
        {
            annotation { "Name" : "Region type", "Default" : RegionType.LINEAR,
                         "UIHint" : UIHint.HORIZONTAL_ENUM }
            region.regionType is RegionType;

            if (region.regionType == RegionType.QUADRATIC)
            {
                annotation { "Name" : "Zero slope at", "Default" : QuadraticZeroSlope.AT_START,
                             "Description" : "Which end of the region has zero offset slope" }
                region.quadZeroSlope is QuadraticZeroSlope;
            }

            annotation { "Name" : "Region number", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isInteger(region.regionNum, POSITIVE_COUNT_BOUNDS);

            annotation { "Name" : "Region name" }
            region.regionName is string;

            annotation { "Name" : "Input type", "Default" : CavityDepthInputType.SIDEWALL,
                         "UIHint" : UIHint.HORIZONTAL_ENUM }
            region.regionInputType is CavityDepthInputType;

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
            if (!isQueryEmpty(context, definition.topWire))
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

    var endpoints = evPathTangentLines(context, refPath, [0, 1]);
    var stdDir = endpoints.tangentLines[0].origin[0] < endpoints.tangentLines[1].origin[0];
    var pathLength = evPathLength(context, refPath);

    // Find the parameter where the wire's X coordinate equals the reference point's X coordinate.
    // Binary search — more reliable than evDistancePath for a 3D reference entity whose closest
    // wire projection may not land at the intended X position (e.g. a mate connector at the origin
    // whose wire projection is offset in Z).
    var refX  = resolveQueryToPoint(context, definition.refPoint)[0];
    var ep0X  = endpoints.tangentLines[0].origin[0];
    var ep1X  = endpoints.tangentLines[1].origin[0];
    var tLo   = 0.0;
    var tHi   = 1.0;
    for (var iter = 0; iter < 30; iter += 1)
    {
        var tMid = (tLo + tHi) / 2;
        var midX = evPathTangentLines(context, refPath, [tMid]).tangentLines[0].origin[0];
        if ((midX < refX) == (ep0X < ep1X))
            tLo = tMid;
        else
            tHi = tMid;
    }
    var refParam = (tLo + tHi) / 2;

    return {
        "path"     : refPath,
        "length"   : pathLength,
        "refParam" : refParam,
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
        // True quadratic: one endpoint has zero slope, the other ramps freely.
        // AT_START (zero slope at t=0): f(t) = t²
        // AT_END   (zero slope at t=1): f(t) = 2t − t²
        if (region.quadZeroSlope == QuadraticZeroSlope.AT_END)
            s = 2 * t - t * t;
        else // AT_START (default)
            s = t * t;
    }
    else // LOGISTIC
    {
        s = t * t * t * (10 + t * (6 * t - 15));
    }
    return region.startOffset + (region.endOffset - region.startOffset) * s;
}


// ─── Offset point computation ─────────────────────────────────────────────────

/**
 * Returns the height above the bottom wire (along the outward normal) for a given region mode.
 *   SIDEWALL     : swBottomOffset + offsetValue + swTopOffset
 *   CAVITY_DEPTH : distance(bottomPoint → topWire along normal) − offsetValue
 *
 * By working in a common "height above bottom wire" space, mixed-mode blends
 * (e.g. SIDEWALL → CAVITY_DEPTH) interpolate consistently.
 */
function computeEffectiveHeight(context is Context, definition is map, region is map,
    basePoint is Vector, normal is Vector, offsetValue is ValueWithUnits) returns ValueWithUnits
{
    if (region.regionInputType == CavityDepthInputType.SIDEWALL)
    {
        return definition.swBottomOffset + offsetValue + definition.swTopOffset;
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
            throw regenError("No intersection with top wire. Verify the top wire is above the bottom wire along its full extent.");
        return norm(hits[0].intersection - basePoint) - offsetValue;
    }
}


/**
 * Computes the 3D output point for a given path parameter t and profile offset value.
 * Delegates to computeEffectiveHeight so all modes share a common height-from-bottom-wire basis.
 */
function computeOffsetPoint(context is Context, definition is map, pathInfo is map,
    region is map, t is number, offsetValue is ValueWithUnits) returns Vector
{
    var tl        = evPathTangentLines(context, pathInfo.path, [t]).tangentLines[0];
    var basePoint = tl.origin;
    var normal    = computeEdgeNormal(tl.direction);
    return basePoint + computeEffectiveHeight(context, definition, region, basePoint, normal, offsetValue) * normal;
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
        points = append(points, computeOffsetPoint(context, definition, pathInfo, region, t, profileValueAt(tNorm, region)));
    }
    return points;
}


/**
 * Height of a region at a given path parameter — combines profile evaluation with
 * mode dispatch. Used for both direct sampling and finite-difference slope/curvature.
 */
function computeHeightAtParam(context is Context, definition is map, region is map,
    pathInfo is map, tPath is number) returns ValueWithUnits
{
    var tNorm  = min(max((tPath - region.tStart) / (region.tEnd - region.tStart), 0), 1);
    var tl     = evPathTangentLines(context, pathInfo.path, [tPath]).tangentLines[0];
    var normal = computeEdgeNormal(tl.direction);
    return computeEffectiveHeight(context, definition, region, tl.origin, normal, profileValueAt(tNorm, region));
}


/**
 * Returns { h, slope, curv } at tPath using central finite differences.
 *   h     = height above bottom wire (ValueWithUnits)
 *   slope = dH/dt  (ValueWithUnits — meters per unit of path parameter)
 *   curv  = d²H/dt² (ValueWithUnits)
 *
 * Scaling to s-space before use: multiply slope by L, curv by L².
 */
function computeHeightDerivativesAtParam(context is Context, definition is map, region is map,
    pathInfo is map, tPath is number) returns map
{
    var dt     = 1e-4;
    var hPlus  = computeHeightAtParam(context, definition, region, pathInfo, tPath + dt);
    var hMid   = computeHeightAtParam(context, definition, region, pathInfo, tPath);
    var hMinus = computeHeightAtParam(context, definition, region, pathInfo, tPath - dt);
    return {
        "h"     : hMid,
        "slope" : (hPlus - hMinus) / (2 * dt),
        "curv"  : (hPlus - 2 * hMid + hMinus) / (dt * dt)
    };
}


/**
 * Evaluates the Hermite blend polynomial at s ∈ [0,1].
 *
 * All slope/curvature args are in s-space (multiply region dH/dt by L, d²H/dt² by L²
 * before calling, where L = tBlendEnd − tBlendStart).
 *
 * Continuity dispatch:
 *   G0+G0 → linear         G1+G0 / G0+G1 → quadratic
 *   G1+G1 → cubic Hermite  G2+G0 / G0+G2 → cubic
 *   G2+G1 / G1+G2 → quartic              G2+G2 → quintic Hermite
 */
function blendHeightAt(s is number,
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
 *
 * Heights at the blend boundaries are evaluated at the actual tNorm positions
 * (not at the region endpoints) so the blend wire meets the trimmed region wire.
 * Slopes and curvatures are matched via finite difference when the intersection's
 * continuity settings request it — accounting for the fact that the height function
 * changes with wire position, not just offset value.
 */
function generateBlendPoints(context is Context, definition is map, pathInfo is map,
    tBlendStart is number, tBlendEnd is number,
    regA is map, regB is map, intr is map) returns array
{
    var n    = definition.samplingDensity;
    var L    = tBlendEnd - tBlendStart;
    var contStart = intr.startContinuity;
    var contEnd   = intr.endContinuity;

    var needSlopeStart = (contStart == GeometricContinuity.G1 || contStart == GeometricContinuity.G2);
    var needSlopeEnd   = (contEnd   == GeometricContinuity.G1 || contEnd   == GeometricContinuity.G2);
    var needCurvStart  = (contStart == GeometricContinuity.G2);
    var needCurvEnd    = (contEnd   == GeometricContinuity.G2);

    // Initialise all derivatives to zero — unused ones won't affect the polynomial
    var h0 = 0 * meter;
    var m0 = 0 * meter;
    var k0 = 0 * meter;
    var h1 = 0 * meter;
    var m1 = 0 * meter;
    var k1 = 0 * meter;

    if (needSlopeStart || needCurvStart)
    {
        var dA = computeHeightDerivativesAtParam(context, definition, regA, pathInfo, tBlendStart);
        h0 = dA.h;
        m0 = dA.slope * L;
        if (needCurvStart)
            k0 = dA.curv * L * L;
    }
    else
    {
        h0 = computeHeightAtParam(context, definition, regA, pathInfo, tBlendStart);
    }

    if (needSlopeEnd || needCurvEnd)
    {
        var dB = computeHeightDerivativesAtParam(context, definition, regB, pathInfo, tBlendEnd);
        h1 = dB.h;
        m1 = dB.slope * L;
        if (needCurvEnd)
            k1 = dB.curv * L * L;
    }
    else
    {
        h1 = computeHeightAtParam(context, definition, regB, pathInfo, tBlendEnd);
    }

    var points = [];
    for (var i = 0; i < n; i += 1)
    {
        var t      = tBlendStart + L * i / (n - 1);
        var s      = i / (n - 1);
        var height = blendHeightAt(s, h0, m0, k0, h1, m1, k1, contStart, contEnd);
        var tl     = evPathTangentLines(context, pathInfo.path, [t]).tangentLines[0];
        var normal = computeEdgeNormal(tl.direction);
        points = append(points, tl.origin + height * normal);
    }
    return points;
}


// ─── Wire construction ────────────────────────────────────────────────────────

function buildOutputWire(context is Context, id is Id, definition is map,
    pathInfo is map, sortedRegions is array)
{
    // Collect active blend zones
    var blendZones = [];
    var allWireBodies = [];
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

        // startDist reaches back into regA from its endpoint; endDist reaches into regB from its start.
        // With both at 0 the blend spans exactly the gap between the two curve endpoints.
        var tBlendStart = regA.tEnd   - intr.startDist / pathInfo.length;
        var tBlendEnd   = regB.tStart + intr.endDist   / pathInfo.length;

        blendZones = append(blendZones, {
            "tBlendStart" : tBlendStart,
            "tBlendEnd"   : tBlendEnd,
            "regA"        : regA,
            "regB"        : regB,
            "intr"        : intr
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
        allWireBodies = append(allWireBodies, qCreatedBy(wireId, EntityType.BODY));

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
        var bz  = blendZones[bzi];
        var pts = generateBlendPoints(context, definition, pathInfo,
            bz.tBlendStart, bz.tBlendEnd, bz.regA, bz.regB, bz.intr);
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
        allWireBodies = append(allWireBodies, qCreatedBy(wireId, EntityType.BODY));

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

    // Collect all edges from individual wire bodies, extract into one wire body, delete originals
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
