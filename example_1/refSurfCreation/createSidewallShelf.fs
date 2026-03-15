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
export const SidewallHeightBounds       = {(millimeter) : [0.5,   5,  50]}  as LengthBoundSpec;
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

        annotation { "Name" : "Surface height",
                     "Description" : "Height of sidewall surface above the bottom surface, along the surface normal" }
        isLength(definition.surfaceHeight, SidewallHeightBounds);

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
                annotation { "Name" : "Step (1-6)", "UIHint" : UIHint.SHOW_LABEL,
                             "Description" : "1=ref path  2=outward  3=edge classification  4=wires  5=loft surfaces  6=boolean" }
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

        // ── Step 2: Footprint edges + outward direction ───────────────────────
        var footprintEdges = evaluateQuery(context, qOwnedByBody(definition.footprintWire, EntityType.EDGE));
        var surfFaces      = evaluateQuery(context, qOwnedByBody(definition.bottomSurface, EntityType.FACE));
        var flipOutward    = determineOutwardFlip(context, footprintEdges, surfFaces);

        if (definition.debugStepThrough && definition.debugStep == 2) return;

        // ── Step 3: Region boundary planes; classify + split footprint edges ──
        var blendZones = collectShelfBlendZones(context, id, definition, pathInfo, sortedRegions);
        var tRefStart  = sortedRegions[0].tStart;
        var tRefEnd    = sortedRegions[size(sortedRegions) - 1].tEnd;

        var startTl = evPathTangentLines(context, pathInfo.path, [tRefStart]).tangentLines[0];
        var endTl   = evPathTangentLines(context, pathInfo.path, [tRefEnd  ]).tangentLines[0];

        // Separate classified edges by Y side
        var plusEdges  = [];
        var minusEdges = [];
        for (var edge in footprintEdges)
        {
            var cls = classifyEdgeAgainstPlanes(context, edge,
                          startTl.origin, startTl.direction,
                          endTl.origin,   endTl.direction);
            if (!cls.include) continue;

            var midPt = evEdgeTangentLines(context, { "edge" : edge, "parameters" : [0.5] })[0].origin;
            var entry = { "edge" : edge, "paramStart" : cls.paramStart, "paramEnd" : cls.paramEnd };
            if (midPt[1] >= 0)
                plusEdges  = append(plusEdges,  entry);
            else
                minusEdges = append(minusEdges, entry);
        }

        if (definition.debugStepThrough && definition.debugStep == 3) return;

        // ── Step 4: Sample each classified edge; build all 4 wire types ───────
        // Each entry produces { shelf, inside, shelfTop, insideTop } wire bodies.
        var spShelf  = []; var spInside  = []; var spShelfTop  = []; var spInsideTop  = [];
        var smShelf  = []; var smInside  = []; var smShelfTop  = []; var smInsideTop  = [];

        for (var i = 0; i < size(plusEdges); i += 1)
        {
            var e = plusEdges[i];
            var w = buildEdgeOffsetWires(context, id + ("ep" ~ i), definition, pathInfo,
                        sortedRegions, blendZones, surfFaces, flipOutward,
                        e.edge, e.paramStart, e.paramEnd);
            spShelf     = append(spShelf,     w.shelf);
            spInside    = append(spInside,    w.inside);
            spShelfTop  = append(spShelfTop,  w.shelfTop);
            spInsideTop = append(spInsideTop, w.insideTop);
        }
        for (var i = 0; i < size(minusEdges); i += 1)
        {
            var e = minusEdges[i];
            var w = buildEdgeOffsetWires(context, id + ("em" ~ i), definition, pathInfo,
                        sortedRegions, blendZones, surfFaces, flipOutward,
                        e.edge, e.paramStart, e.paramEnd);
            smShelf     = append(smShelf,     w.shelf);
            smInside    = append(smInside,    w.inside);
            smShelfTop  = append(smShelfTop,  w.shelfTop);
            smInsideTop = append(smInsideTop, w.insideTop);
        }

        setProperty(context, { "entities" : qUnion(spShelf),    "propertyType" : PropertyType.NAME, "value" : "SW Shelf +Y wire" });
        setProperty(context, { "entities" : qUnion(smShelf),    "propertyType" : PropertyType.NAME, "value" : "SW Shelf -Y wire" });
        setProperty(context, { "entities" : qUnion(spInside),   "propertyType" : PropertyType.NAME, "value" : "SW Inside +Y wire" });
        setProperty(context, { "entities" : qUnion(smInside),   "propertyType" : PropertyType.NAME, "value" : "SW Inside -Y wire" });
        setProperty(context, { "entities" : qUnion(spShelfTop), "propertyType" : PropertyType.NAME, "value" : "SW Shelf +Y top" });
        setProperty(context, { "entities" : qUnion(smShelfTop), "propertyType" : PropertyType.NAME, "value" : "SW Shelf -Y top" });
        setProperty(context, { "entities" : qUnion(spInsideTop),"propertyType" : PropertyType.NAME, "value" : "SW Inside +Y top" });
        setProperty(context, { "entities" : qUnion(smInsideTop),"propertyType" : PropertyType.NAME, "value" : "SW Inside -Y top" });

        if (definition.debugStepThrough && definition.debugStep == 4) return;

        // ── Step 5: Loft each bottom↔top pair (index-matched from step 4) ────
        var shelfPlusSurfs   = loftMatchedWires(context, id + "loftSP", spShelf,   spShelfTop);
        var shelfMinusSurfs  = loftMatchedWires(context, id + "loftSM", smShelf,   smShelfTop);
        var insidePlusSurfs  = loftMatchedWires(context, id + "loftIP", spInside,  spInsideTop);
        var insideMinusSurfs = loftMatchedWires(context, id + "loftIM", smInside,  smInsideTop);

        setProperty(context, { "entities" : qUnion(shelfPlusSurfs),   "propertyType" : PropertyType.NAME, "value" : "SW Shelf +Y surface" });
        setProperty(context, { "entities" : qUnion(shelfMinusSurfs),  "propertyType" : PropertyType.NAME, "value" : "SW Shelf -Y surface" });
        setProperty(context, { "entities" : qUnion(insidePlusSurfs),  "propertyType" : PropertyType.NAME, "value" : "SW Inside +Y surface" });
        setProperty(context, { "entities" : qUnion(insideMinusSurfs), "propertyType" : PropertyType.NAME, "value" : "SW Inside -Y surface" });

        if (definition.debugStepThrough && definition.debugStep == 5) return;

        // ── Step 6: Boolean unite each side's segments ────────────────────────
        var finalSP = booleanSurfaces(context, id + "boolSP", qUnion(shelfPlusSurfs));
        var finalSM = booleanSurfaces(context, id + "boolSM", qUnion(shelfMinusSurfs));
        var finalIP = booleanSurfaces(context, id + "boolIP", qUnion(insidePlusSurfs));
        var finalIM = booleanSurfaces(context, id + "boolIM", qUnion(insideMinusSurfs));

        setProperty(context, { "entities" : finalSP, "propertyType" : PropertyType.NAME, "value" : "SW Shelf +Y surface" });
        setProperty(context, { "entities" : finalSM, "propertyType" : PropertyType.NAME, "value" : "SW Shelf -Y surface" });
        setProperty(context, { "entities" : finalIP, "propertyType" : PropertyType.NAME, "value" : "SW Inside +Y surface" });
        setProperty(context, { "entities" : finalIM, "propertyType" : PropertyType.NAME, "value" : "SW Inside -Y surface" });

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
 * Finds the footprint edge whose midpoint has the highest Y, samples it there,
 * and checks whether cross(surfNormal, tangent)[1] >= 0.
 * Returns true if the cross product must be negated to get the outward (+Y) direction.
 */
function determineOutwardFlip(context is Context, footprintEdges is array, surfFaces is array) returns boolean
{
    var maxY    = -1e10 * meter;
    var bestIdx = 0;
    for (var i = 0; i < size(footprintEdges); i += 1)
    {
        var midPt = evEdgeTangentLines(context, { "edge" : footprintEdges[i], "parameters" : [0.5] })[0].origin;
        if (midPt[1] > maxY)
        {
            maxY    = midPt[1];
            bestIdx = i;
        }
    }

    var tl         = evEdgeTangentLines(context, { "edge" : footprintEdges[bestIdx], "parameters" : [0.5] })[0];
    var testNormal = surfaceNormalAt(context, surfFaces, tl.origin);
    var candidate  = cross(testNormal, tl.direction);
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


// ─── Edge classification against region boundary planes ───────────────────────

/**
 * Binary-searches for the parameter t on 'edge' in [pLo, pHi] where
 * dot(pt(t) - planeOrigin, planeNormal) = 0.
 * dLoSign should be the sign of the dot product at pLo (pass as +1 or -1).
 */
function findEdgePlaneCrossing(context is Context, edge is Query,
    pLo is number, pHi is number,
    planeOrigin is Vector, planeNormal is Vector,
    dLoSign is number) returns number
{
    for (var iter = 0; iter < 30; iter += 1)
    {
        var pMid  = (pLo + pHi) / 2;
        var ptMid = evEdgeTangentLines(context, { "edge" : edge, "parameters" : [pMid] })[0].origin;
        var dMid  = dot(ptMid - planeOrigin, planeNormal) / meter;
        if (dMid * dLoSign > 0)
            pLo = pMid;
        else
            pHi = pMid;
    }
    return (pLo + pHi) / 2;
}


/**
 * Classifies a footprint edge against the two region boundary planes.
 * Returns { "include" : bool, "paramStart" : number, "paramEnd" : number }.
 *
 * "Inside" the region means: past the start plane AND before the end plane,
 * i.e. dot(pt - startOrigin, startNormal) >= 0 AND dot(pt - endOrigin, endNormal) <= 0.
 *
 * Edges fully outside are excluded.  Partial edges are clipped to the relevant
 * plane crossing found via binary search.
 */
function classifyEdgeAgainstPlanes(context is Context, edge is Query,
    startOrigin is Vector, startNormal is Vector,
    endOrigin   is Vector, endNormal   is Vector) returns map
{
    var tls  = evEdgeTangentLines(context, { "edge" : edge, "parameters" : [0.0, 1.0] });
    var pt0  = tls[0].origin;
    var pt1  = tls[1].origin;

    var d0s  = dot(pt0 - startOrigin, startNormal) / meter; // + = past start
    var d1s  = dot(pt1 - startOrigin, startNormal) / meter;
    var d0e  = dot(pt0 - endOrigin,   endNormal)   / meter; // - = before end
    var d1e  = dot(pt1 - endOrigin,   endNormal)   / meter;

    // Quick reject: both endpoints on the same outside side
    if (d0s < 0 && d1s < 0) return { "include" : false, "paramStart" : 0.0, "paramEnd" : 1.0 };
    if (d0e > 0 && d1e > 0) return { "include" : false, "paramStart" : 0.0, "paramEnd" : 1.0 };

    var pStart = 0.0;
    var pEnd   = 1.0;

    // Clip at start plane
    if      (d0s < 0 && d1s >= 0) pStart = findEdgePlaneCrossing(context, edge, 0.0, 1.0, startOrigin, startNormal,  1);
    else if (d1s < 0 && d0s >= 0) pEnd   = findEdgePlaneCrossing(context, edge, 0.0, 1.0, startOrigin, startNormal, -1);

    // Clip at end plane
    if      (d1e > 0 && d0e <= 0) pEnd   = findEdgePlaneCrossing(context, edge, pStart, 1.0, endOrigin, endNormal, -1);
    else if (d0e > 0 && d1e <= 0) pStart = findEdgePlaneCrossing(context, edge, 0.0, pEnd,   endOrigin, endNormal,  1);

    return { "include" : (pEnd > pStart + 1e-6), "paramStart" : pStart, "paramEnd" : pEnd };
}


// ─── Edge offset wire construction ────────────────────────────────────────────

/**
/**
 * Samples one classified footprint edge from paramStart to paramEnd and builds
 * four wire bodies simultaneously — shelf bottom, inside bottom, shelf top, and
 * inside top — all offset from the same footprint evaluation point using the
 * same outward and up vectors.
 *
 * outward = normalize(cross(surfNormal, edgeTangent)), flipped by flipOutward.
 * up      = surfNormal oriented to +Z (for the surfaceHeight offset).
 *
 * Returns a map: { "shelf", "inside", "shelfTop", "insideTop" } — each a Query
 * for one wire body.
 */
function buildEdgeOffsetWires(context is Context, id is Id, definition is map, pathInfo is map,
    sortedRegions is array, blendZones is array,
    surfFaces is array, flipOutward is boolean,
    edge is Query, paramStart is number, paramEnd is number) returns map
{
    var n      = definition.samplingDensity;
    var params = [];
    for (var i = 0; i < n; i += 1)
        params = append(params, paramStart + (paramEnd - paramStart) * i / (n - 1));

    var tls = evEdgeTangentLines(context, { "edge" : edge, "parameters" : params });

    var shelfPts    = [];
    var insidePts   = [];
    var shelfTopPts = [];
    var insideTopPts = [];

    for (var i = 0; i < n; i += 1)
    {
        var pt      = tls[i].origin;
        var tangent = tls[i].direction;

        var surfNormal = surfaceNormalAt(context, surfFaces, pt);

        // In-surface perpendicular (outward from ski edge)
        var outward = cross(surfNormal, tangent);
        if (flipOutward) outward = -outward;
        var outLen = norm(outward);
        if (outLen > 1e-10)
            outward = outward / outLen;
        else
            outward = vector(0.0, 1.0, 0.0);

        // Upward direction: surface normal oriented to +Z
        var up = surfNormal;
        if (up[2] < 0) up = -up;

        // Shelf depth from refWire parameter at this footprint point
        var refT  = evDistancePath(context, { "side0" : pathInfo.path, "side1" : pt }).sides[0].pathParam;
        var depth = shelfDepthAtT(sortedRegions, blendZones, refT);
        var swW   = definition.sidewallWidth;
        var swH   = definition.surfaceHeight;

        var basePt    = pt + depth       * outward;
        var insidePt  = pt + (depth - swW) * outward;

        shelfPts     = append(shelfPts,     basePt);
        insidePts    = append(insidePts,    insidePt);
        shelfTopPts  = append(shelfTopPts,  basePt   + swH * up);
        insideTopPts = append(insideTopPts, insidePt + swH * up);

        if (definition.debugShowShelfPoints)
        {
            debug(context, basePt,   DebugColor.GREEN);
            debug(context, insidePt, DebugColor.BLUE);
        }
    }

    return {
        "shelf"     : buildShelfWire(context, id + "shelf",     definition, shelfPts),
        "inside"    : buildShelfWire(context, id + "inside",    definition, insidePts),
        "shelfTop"  : buildShelfWire(context, id + "shelfTop",  definition, shelfTopPts),
        "insideTop" : buildShelfWire(context, id + "insideTop", definition, insideTopPts)
    };
}


/**
 * Lofts each wire in listA against the corresponding wire in listB (index-matched).
 * Returns an array of surface body Queries.
 */
function loftMatchedWires(context is Context, id is Id,
    listA is array, listB is array) returns array
{
    var surfs = [];
    for (var i = 0; i < size(listA); i += 1)
    {
        var segId = id + ("s" ~ i);
        opLoft(context, segId, {
            "profileSubqueries" : [qOwnedByBody(listA[i], EntityType.EDGE),
                                   qOwnedByBody(listB[i], EntityType.EDGE)],
            "bodyType"          : ToolBodyType.SURFACE
        });
        surfs = append(surfs, qCreatedBy(segId, EntityType.BODY));
    }
    return surfs;
}


/**
 * Unites all surface bodies in surfsQuery into one.  If there is only one body
 * (single footprint-edge segment) the boolean is skipped and the query is
 * returned as-is.
 */
function booleanSurfaces(context is Context, id is Id, surfsQuery is Query) returns Query
{
    var bodies = evaluateQuery(context, surfsQuery);
    if (size(bodies) <= 1)
        return surfsQuery;

    opBoolean(context, id, {
        "tools"         : surfsQuery,
        "operationType" : BooleanOperationType.UNION
    });
    return qCreatedBy(id, EntityType.BODY);
}


// ─── Wire construction (single segment) ──────────────────────────────────────

function buildShelfWire(context is Context, id is Id, definition is map, pts is array) returns Query
{
    if (size(pts) < 2)
        throw regenError("Too few sample points to build a wire");

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
