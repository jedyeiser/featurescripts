FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");
export import(path : "onshape/std/geometriccontinuity.gen.fs", version : "2892.0");

// real import path for refSurfUtils (managed by sync)
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
export const ShelfDebugStepBounds       = {(unitless)   : [1,     1,   6]}  as IntegerBoundSpec;


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
        annotation { "Name" : "Outside surface",
                     "Filter" : EntityType.BODY && BodyType.SHEET, "MaxNumberOfPicks" : 1,
                     "Description" : "Outer side surface of the ski (may be multi-face)" }
        definition.outsideSurface is Query;

        annotation { "Name" : "Bottom wire",
                     "Filter" : BodyType.WIRE, "MaxNumberOfPicks" : 1,
                     "Description" : "Reference wire for X-coordinate mapping" }
        definition.bottomWire is Query;

        annotation { "Name" : "Reference point",
                     "Filter" : EntityType.VERTEX || GeometryType.PLANE || BodyType.MATE_CONNECTOR,
                     "MaxNumberOfPicks" : 1,
                     "Description" : "Defines X = 0 along the wire" }
        definition.refPoint is Query;

        annotation { "Name" : "Sidewall width",
                     "Description" : "Thickness of sidewall material; inside surface = shelf - sidewallWidth" }
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
                             "Description" : "Distance along wire from reference point (negative = toward tail)" }
                isLength(region.regionStart, LENGTH_BOUNDS);

                annotation { "Name" : "Region end" }
                isLength(region.regionEnd, LENGTH_BOUNDS);
            }

            annotation { "Name" : "Start shelf depth",
                         "Description" : "Distance outward from outside surface at start of region" }
            isLength(region.startShelfDepth, ShelfDepthBounds);

            annotation { "Name" : "End shelf depth",
                         "Description" : "Distance outward from outside surface at end of region" }
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
                         "Description" : "Points sampled per rail (more = smoother surface, slower)" }
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
                annotation { "Name" : "Step (1-6)", "UIHint" : UIHint.SHOW_LABEL,
                             "Description" : "1=path  2=rails  3=sample points  4=rail wires  5=shelf surface  6=inside surface" }
                isInteger(definition.debugStep, ShelfDebugStepBounds);
            }

            annotation { "Name" : "Keep intermediate bodies", "Default" : false,
                         "Description" : "Retain rail wire bodies after lofting" }
            definition.debugKeepAllBodies is boolean;

            annotation { "Name" : "Print debug", "Default" : false }
            definition.debugPrint is boolean;

            annotation { "Name" : "Show rails", "Default" : false,
                         "Description" : "Highlight extracted top (green) and bottom (red) rail wires" }
            definition.debugShowRails is boolean;

            annotation { "Name" : "Show shelf sample points", "Default" : false,
                         "Description" : "Draw shelf top (green) and bottom (blue) offset points" }
            definition.debugShowShelfPoints is boolean;

            annotation { "Name" : "Show inside sample points", "Default" : false,
                         "Description" : "Draw inside top (yellow) and bottom (magenta) offset points" }
            definition.debugShowInsidePoints is boolean;
        }
    }
    {
        // ── Step 1: Path ──────────────────────────────────────────────────────
        var pathInfo = processShelfPath(context, id + "path", definition);

        if (size(definition.shelfRegions) == 0)
        {
            reportFeatureWarning(context, id, "No regions defined — nothing to generate");
            return;
        }

        var processedRegions = processShelfRegions(context, definition, pathInfo);
        validateShelfNoOverlap(context, id, processedRegions);
        var sortedRegions = sortShelfRegionsByTStart(processedRegions);

        if (definition.debugPrint)
        {
            println("=== SWShelf: pathLength = " ~ toString(pathInfo.length / millimeter) ~ " mm");
            println("  refParam = " ~ toString(pathInfo.refParam));
            println("  stdDir   = " ~ toString(pathInfo.stdDir));
            for (var reg in sortedRegions)
            {
                println("  Region '" ~ reg.regionName ~ "': tStart=" ~ toString(reg.tStart)
                    ~ "  tEnd=" ~ toString(reg.tEnd)
                    ~ "  start=" ~ toString(reg.startShelfDepth / millimeter) ~ "mm"
                    ~ "  end="   ~ toString(reg.endShelfDepth   / millimeter) ~ "mm");
            }
        }

        if (definition.debugStepThrough && definition.debugStep == 1) return;

        // ── Step 2: Extract rails from outside surface ────────────────────────
        var railInfo       = extractShelfRails(context, id + "rails", definition.outsideSurface);
        var topRailPath    = railInfo.topPath;
        var bottomRailPath = railInfo.bottomPath;
        var topRailWire    = railInfo.topWire;
        var bottomRailWire = railInfo.bottomWire;

        if (definition.debugPrint)
        {
            println("  topRail edges    = " ~ toString(size(topRailPath.edges)));
            println("  bottomRail edges = " ~ toString(size(bottomRailPath.edges)));
        }

        if (definition.debugShowRails)
        {
            debug(context, topRailWire,    DebugColor.GREEN);
            debug(context, bottomRailWire, DebugColor.RED);
        }

        if (definition.debugStepThrough && definition.debugStep == 2) return;

        // ── Step 3: Collect blend zones; sample rail points ───────────────────
        var blendZones = collectShelfBlendZones(context, id, definition, pathInfo, sortedRegions);

        var topShelfPts     = buildShelfRailPoints(context, definition, pathInfo, topRailPath,    sortedRegions, blendZones, 0 * meter);
        var bottomShelfPts  = buildShelfRailPoints(context, definition, pathInfo, bottomRailPath, sortedRegions, blendZones, 0 * meter);
        var topInsidePts    = buildShelfRailPoints(context, definition, pathInfo, topRailPath,    sortedRegions, blendZones, -definition.sidewallWidth);
        var bottomInsidePts = buildShelfRailPoints(context, definition, pathInfo, bottomRailPath, sortedRegions, blendZones, -definition.sidewallWidth);

        if (definition.debugShowShelfPoints)
        {
            for (var pt in topShelfPts)    debug(context, pt, DebugColor.GREEN);
            for (var pt in bottomShelfPts) debug(context, pt, DebugColor.BLUE);
        }
        if (definition.debugShowInsidePoints)
        {
            for (var pt in topInsidePts)    debug(context, pt, DebugColor.YELLOW);
            for (var pt in bottomInsidePts) debug(context, pt, DebugColor.MAGENTA);
        }

        if (definition.debugStepThrough && definition.debugStep == 3) return;

        // ── Step 4: Build spline rail wires ───────────────────────────────────
        var topShelfWire     = buildShelfWire(context, id + "topShelf",     definition, topShelfPts);
        var bottomShelfWire  = buildShelfWire(context, id + "bottomShelf",  definition, bottomShelfPts);
        var topInsideWire    = buildShelfWire(context, id + "topInside",    definition, topInsidePts);
        var bottomInsideWire = buildShelfWire(context, id + "bottomInside", definition, bottomInsidePts);

        setProperty(context, { "entities" : topShelfWire,     "propertyType" : PropertyType.NAME, "value" : "SW Shelf top rail" });
        setProperty(context, { "entities" : bottomShelfWire,  "propertyType" : PropertyType.NAME, "value" : "SW Shelf bottom rail" });
        setProperty(context, { "entities" : topInsideWire,    "propertyType" : PropertyType.NAME, "value" : "SW Inside top rail" });
        setProperty(context, { "entities" : bottomInsideWire, "propertyType" : PropertyType.NAME, "value" : "SW Inside bottom rail" });

        if (definition.debugStepThrough && definition.debugStep == 4) return;

        // ── Step 5: Loft shelf surface ────────────────────────────────────────
        opLoft(context, id + "shelfLoft", {
            "profileSubqueries" : [topShelfWire, bottomShelfWire],
            "connections"       : shelfLoftConnection(context, topShelfWire, bottomShelfWire),
            "bodyType"          : ToolBodyType.SURFACE
        });
        setProperty(context, { "entities" : qCreatedBy(id + "shelfLoft", EntityType.BODY),
                                "propertyType" : PropertyType.NAME, "value" : "SW Shelf surface" });

        if (definition.debugStepThrough && definition.debugStep == 5) return;

        // ── Step 6: Loft inside surface ───────────────────────────────────────
        opLoft(context, id + "insideLoft", {
            "profileSubqueries" : [topInsideWire, bottomInsideWire],
            "connections"       : shelfLoftConnection(context, topInsideWire, bottomInsideWire),
            "bodyType"          : ToolBodyType.SURFACE
        });
        setProperty(context, { "entities" : qCreatedBy(id + "insideLoft", EntityType.BODY),
                                "propertyType" : PropertyType.NAME, "value" : "SW Inside surface" });

        // ── Cleanup ───────────────────────────────────────────────────────────
        if (!definition.debugKeepAllBodies)
        {
            opDeleteBodies(context, id + "deleteRailWires", {
                "entities" : qUnion([topShelfWire, bottomShelfWire, topInsideWire, bottomInsideWire])
            });
            opDeleteBodies(context, id + "deleteExtractedRails", {
                "entities" : qUnion([topRailWire, bottomRailWire])
            });
        }
    });


// ─── Path processing ──────────────────────────────────────────────────────────

function processShelfPath(context is Context, id is Id, definition is map) returns map
{
    var pathEdges = qUnion([qOwnedByBody(definition.bottomWire, EntityType.EDGE)]);
    var refPath;
    try
    {
        refPath = constructPath(context, pathEdges);
    }
    catch
    {
        throw regenError("Bottom wire edges must form a continuous path");
    }

    var endpoints  = evPathTangentLines(context, refPath, [0, 1]);
    var stdDir     = endpoints.tangentLines[0].origin[0] < endpoints.tangentLines[1].origin[0];
    var pathLength = evPathLength(context, refPath);
    var refX       = resolveShelfPoint(context, definition.refPoint)[0];
    var refParam   = findPathParamAtX(context, refPath, refX);

    return { "path" : refPath, "length" : pathLength, "refParam" : refParam, "stdDir" : stdDir };
}


// ─── Rail extraction ──────────────────────────────────────────────────────────

/**
 * Splits wireBody at the front plane (Y = 0) and returns the half with the
 * highest max-Y bounding box (the +Y ski side). If the wire lies entirely on
 * one side it is returned unchanged.
 */
function splitWireAndKeepPlusY(context is Context, id is Id, wireBody is Query) returns Query
{
    try
    {
        opSplitPart(context, id + "split", {
            "targets" : wireBody,
            "tool"    : qFrontPlane(EntityType.FACE)
        });
    }
    catch
    {
        // Wire does not intersect the front plane — already fully on one side
        return wireBody;
    }

    var trueBody  = qSplitBy(id + "split", EntityType.BODY, true);
    var falseBody = qSplitBy(id + "split", EntityType.BODY, false);

    var trueEmpty  = isQueryEmpty(context, trueBody);
    var falseEmpty = isQueryEmpty(context, falseBody);

    if (trueEmpty && falseEmpty) return wireBody;
    if (trueEmpty)  return falseBody;
    if (falseEmpty) return trueBody;

    var trueMaxY  = evBox3d(context, { "topology" : trueBody,  "tight" : true }).maxCorner[1];
    var falseMaxY = evBox3d(context, { "topology" : falseBody, "tight" : true }).maxCorner[1];

    var kept    = trueMaxY >= falseMaxY ? trueBody  : falseBody;
    var deleted = trueMaxY >= falseMaxY ? falseBody : trueBody;

    opDeleteBodies(context, id + "del", { "entities" : deleted });
    return kept;
}


/**
 * Extracts the top (highest Z) and bottom (lowest Z) free-edge rail paths from
 * the outside surface. Any extra boundary wires (tip/tail edges) are deleted.
 * Returns { topPath, bottomPath, topWire, bottomWire }.
 */
function extractShelfRails(context is Context, id is Id, outsideSurface is Query) returns map
{
    var oneSidedEdges = qEdgeTopologyFilter(qOwnedByBody(outsideSurface, EntityType.EDGE), EdgeTopology.ONE_SIDED);
    opExtractWires(context, id + "extract", { "edges" : oneSidedEdges });
    var wireBodies = evaluateQuery(context, qCreatedBy(id + "extract", EntityType.BODY));

    if (size(wireBodies) < 2)
        throw regenError("Outside surface must have at least 2 free boundary edges (top and bottom rails)");

    // Sort wire bodies by average Z (bounding box midpoint)
    var topIdx    = 0;
    var topMidZ   = -1e10 * meter;
    var bottomIdx = 0;
    var botMidZ   = 1e10 * meter;

    for (var i = 0; i < size(wireBodies); i += 1)
    {
        var bb   = evBox3d(context, { "topology" : wireBodies[i], "tight" : true });
        var midZ = (bb.minCorner[2] + bb.maxCorner[2]) / 2;
        if (midZ > topMidZ) { topMidZ = midZ; topIdx    = i; }
        if (midZ < botMidZ) { botMidZ = midZ; bottomIdx = i; }
    }

    // Delete any extra wires (tip/tail edges on a closed loop surface)
    var toDelete = [];
    for (var i = 0; i < size(wireBodies); i += 1)
    {
        if (i != topIdx && i != bottomIdx)
            toDelete = append(toDelete, wireBodies[i]);
    }
    if (size(toDelete) > 0)
        opDeleteBodies(context, id + "deleteExtra", { "entities" : qUnion(toDelete) });

    var topWire    = wireBodies[topIdx];
    var bottomWire = wireBodies[bottomIdx];

    // The boundary loops wrap around both +Y and -Y ski halves.
    // Split each at the front plane without a keepType, then keep whichever half
    // has the higher max Y (the +Y side used for the shelf feature).
    var topWireKept    = splitWireAndKeepPlusY(context, id + "splitTop",    topWire);
    var bottomWireKept = splitWireAndKeepPlusY(context, id + "splitBottom", bottomWire);

    var topPath    = constructPath(context, qOwnedByBody(topWireKept,    EntityType.EDGE));
    var bottomPath = constructPath(context, qOwnedByBody(bottomWireKept, EntityType.EDGE));

    return {
        "topPath"    : topPath,
        "bottomPath" : bottomPath,
        "topWire"    : topWireKept,
        "bottomWire" : bottomWireKept
    };
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
                throw regenError("Regions '" ~ a.regionName ~ "' and '" ~ b.regionName ~ "' overlap. Regions must not overlap.");
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

/**
 * Evaluates shelfDepth at normalized position t ∈ [0,1] within a region.
 */
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

/**
 * Returns shelfDepth for a region at path parameter t.
 */
function shelfRegionDepthAt(region is map, t is number) returns ValueWithUnits
{
    var tNorm = min(max((t - region.tStart) / (region.tEnd - region.tStart), 0), 1);
    return shelfProfileValueAt(tNorm, region);
}


/**
 * Returns shelfDepth for path parameter t, with tip/tail extrapolation (clamp to
 * first/last region endpoint) and linear gap fill between non-adjacent regions.
 * Does NOT handle blend zones — use shelfDepthAtT for blend-aware evaluation.
 */
function computeShelfDepthAtT(sortedRegions is array, t is number) returns ValueWithUnits
{
    if (size(sortedRegions) == 0)
        return 0 * meter;

    // Before first region — clamp to start of first region
    if (t <= sortedRegions[0].tStart)
        return sortedRegions[0].startShelfDepth;

    // After last region — clamp to end of last region
    var last = sortedRegions[size(sortedRegions) - 1];
    if (t >= last.tEnd)
        return last.endShelfDepth;

    // Inside a region
    for (var reg in sortedRegions)
    {
        if (t >= reg.tStart && t <= reg.tEnd)
            return shelfRegionDepthAt(reg, t);
    }

    // Gap between two adjacent regions — linear fill
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


/**
 * Blend-aware shelfDepth evaluation. Checks blend zones first; falls back to
 * computeShelfDepthAtT for non-blend regions and tip/tail extrapolation.
 */
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
            reportFeatureWarning(context, id, "Blend start distance exceeds extent of '" ~ regA.regionName ~ "'. Clamping.");
            tBlendStart = regA.tStart;
        }
        if (tBlendEnd > regB.tEnd)
        {
            reportFeatureWarning(context, id, "Blend end distance exceeds extent of '" ~ regB.regionName ~ "'. Clamping.");
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


// ─── Rail point sampling ──────────────────────────────────────────────────────

/**
 * Samples n points along railPath, offsets each by shelfDepth(X) + depthOffset
 * in the outward direction — perpendicular to the rail tangent in the XY plane,
 * pointing toward the +Y (outside) ski side.
 *
 * depthOffset: additional signed offset applied after shelfDepth (pass
 *   -sidewallWidth to get inside surface points, 0 for shelf surface points).
 */
function buildShelfRailPoints(context is Context, definition is map, pathInfo is map,
    railPath is Path, sortedRegions is array, blendZones is array,
    depthOffset is ValueWithUnits) returns array
{
    var n      = definition.samplingDensity;
    var params = range(0, 1, n);
    var tls    = evPathTangentLines(context, railPath, params).tangentLines;
    var pts    = [];

    for (var i = 0; i < n; i += 1)
    {
        var railPt  = tls[i].origin;
        var tangent = tls[i].direction; // unit vector along rail

        var X     = railPt[0];
        var t     = findPathParamAtX(context, pathInfo.path, X);
        var depth = shelfDepthAtT(sortedRegions, blendZones, t) + depthOffset;

        // Outward offset direction: perpendicular to rail tangent in XY plane,
        // pointing toward +Y (away from ski center axis).
        // For tangent = [tx, ty, tz], the XY-plane perpendicular is [-ty, tx, 0].
        var perpXY  = vector(-tangent[1], tangent[0], 0.0);
        var perpLen = norm(perpXY);
        var outward = vector(0.0, 1.0, 0.0); // default: +Y when tangent is nearly vertical
        if (perpLen >= 1e-8)
        {
            outward = perpXY / perpLen;
            if (outward[1] < 0) outward = -outward;
        }

        pts = append(pts, railPt + depth * outward);
    }
    return pts;
}


// ─── Wire construction ────────────────────────────────────────────────────────

function buildShelfWire(context is Context, id is Id, definition is map, pts is array) returns Query
{
    if (size(pts) < 2)
        throw regenError("Too few sample points to build a wire — increase sampling density or expand region extents");

    var bspline = approximateSpline(context, {
        "degree"             : definition.approxDegree,
        "tolerance"          : definition.approxTolerance,
        "isPeriodic"         : false,
        "maxControlPoints"   : definition.approxMaxCP,
        "targets"            : [approximationTarget({ "positions" : pts })],
        "interpolateIndices" : [0, size(pts) - 1]
    })[0];

    opCreateBSplineCurve(context, id + "curve", { "bSplineCurve" : bspline });
    return qCreatedBy(id + "curve", EntityType.BODY);
}


// ─── Loft connection helper ───────────────────────────────────────────────────

/**
 * Builds a loft connection aligning the nearest endpoint pair between wireA and wireB.
 * Prevents LOFT_DIRECTION_ERROR from mismatched wire traversal directions.
 */
function shelfLoftConnection(context is Context, wireA is Query, wireB is Query) returns array
{
    var vertsA = evaluateQuery(context, qOwnedByBody(wireA, EntityType.VERTEX));
    var vertsB = evaluateQuery(context, qOwnedByBody(wireB, EntityType.VERTEX));
    if (size(vertsA) == 0 || size(vertsB) == 0)
        return [];

    var minDist = 1e10 * meter;
    var bestA   = vertsA[0];
    var bestB   = vertsB[0];
    for (var va in vertsA)
    {
        for (var vb in vertsB)
        {
            var d = evDistance(context, { "side0" : va, "side1" : vb }).distance;
            if (d < minDist) { minDist = d; bestA = va; bestB = vb; }
        }
    }

    return [{
        "connectionEntities"       : qUnion([bestA, bestB]),
        "connectionEdges"          : [],
        "connectionEdgeParameters" : []
    }];
}
