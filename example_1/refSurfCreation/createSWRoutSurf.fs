FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");
import(path : "onshape/std/extend.fs", version : "2892.0");
import(path : "onshape/std/faceIntersection.fs", version : "2892.0");

// IMPORT: tools/printing.fs
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/b02d6a2bac551b24347c983f", version : "c104606e8ffc8e0964404bbc");

// import swRoutRegions -- SWRoutExtentType, bounds, region processing functions
export import(path : "7e3b271854475bf6cf878b2b", version : "fc5e7fa037480fd2cbb69c0a");


export const DEBUG_STEP_BOUNDS = { (unitless) : [0, 1, 9]} as IntegerBoundSpec;


// --- Editing logic -----------------------------------------------------------

export function generateSWRoutEditingLogic(context is Context, id is Id,
        oldDefinition is map, definition is map, isCreating is boolean,
        specifiedParameters is map, hiddenBodies is Query) returns map
{
    // Migrate legacy fields: regionName -> name, X_EXTENTS -> ALONG_REF
    var migratedRegions = [];
    for (var i = 0; i < size(definition.swRoutRegions); i += 1)
    {
        var reg = definition.swRoutRegions[i];
        if ((reg.name == undefined || reg.name == "") && reg.regionName != undefined)
        {
            reg = mergeMaps(reg, { "name" : reg.regionName });
        }
        if (reg.extentType == undefined)
        {
            reg = mergeMaps(reg, { "extentType" : SWRoutExtentType.ALONG_REF });
        }
        migratedRegions = append(migratedRegions, reg);
    }
    definition.swRoutRegions = migratedRegions;

    if (size(definition.swRoutRegions) > 0)
    {
        var sortable   = [];
        var unsortable = [];

        try silent
        {
            var refWirePath = constructPath(context,
                    qOwnedByBody(definition.refWire, EntityType.EDGE));
            var totalLength = 0 * meter;
            for (var edge in refWirePath.edges)
            {
                totalLength += evLength(context, { "entities" : edge });
            }

            for (var i = 0; i < size(definition.swRoutRegions); i += 1)
            {
                var reg = definition.swRoutRegions[i];
                if (reg.extentType == SWRoutExtentType.ALONG_REF &&
                        reg.startX != undefined && reg.endX != undefined)
                {
                    reg.tStart       = min(max(reg.startX / totalLength, 0), 1);
                    reg.tEnd         = min(max(reg.endX   / totalLength, 0), 1);
                    reg.regionLength = (reg.tEnd - reg.tStart) * totalLength;
                    sortable = append(sortable, reg);
                }
                else if (reg.extentType == SWRoutExtentType.QUERY)
                {
                    var resolvedOk = false;
                    try silent
                    {
                        var queryPts = evaluateQuery(context, reg.extentQueries);
                        if (size(queryPts) == 2)
                        {
                            var t0 = evDistancePath(context, {
                                    "side0" : refWirePath,
                                    "side1" : queryPts[0]
                            }).sides[0].pathParam;
                            var t1 = evDistancePath(context, {
                                    "side0" : refWirePath,
                                    "side1" : queryPts[1]
                            }).sides[0].pathParam;
                            reg.tStart       = min(t0, t1);
                            reg.tEnd         = max(t0, t1);
                            reg.regionLength = (reg.tEnd - reg.tStart) * totalLength;
                            sortable = append(sortable, reg);
                            resolvedOk = true;
                        }
                    }
                    if (!resolvedOk)
                    {
                        unsortable = append(unsortable, reg);
                    }
                }
                else
                {
                    unsortable = append(unsortable, reg);
                }
            }

            sortable = sort(sortable, function(a, b)
            {
                return ((a.tStart + a.tEnd) / 2) - ((b.tStart + b.tEnd) / 2);
            });
        }

        // If outer try silent failed, nothing was sorted -- fall back
        if (size(sortable) == 0 && size(unsortable) == 0)
        {
            unsortable = definition.swRoutRegions;
        }

        // Assign regionNum and auto-names in sorted order
        var sizeCounter = 0;
        var newRegions  = [];

        for (var i = 0; i < size(sortable); i += 1)
        {
            var reg = sortable[i];
            reg.regionNum = sizeCounter;
            var nm = reg.name;
            reg.needsDefaultName = (nm == undefined || nm == "" || startsWith(nm, "Region "));
            if (reg.needsDefaultName)
            {
                reg.name = "Region " ~ sizeCounter;
            }
            newRegions = append(newRegions, reg);
            sizeCounter += 1;
        }
        for (var i = 0; i < size(unsortable); i += 1)
        {
            var reg = unsortable[i];
            reg.regionNum = sizeCounter;
            var nm = reg.name;
            reg.needsDefaultName = (nm == undefined || nm == "");
            if (reg.needsDefaultName)
            {
                reg.name = "Region " ~ sizeCounter;
            }
            newRegions = append(newRegions, reg);
            sizeCounter += 1;
        }

        definition.swRoutRegions       = newRegions;
        definition.swRoutIntersections = rebuildSwRoutIntersections(definition, newRegions);
    }
    else
    {
        definition.swRoutIntersections = [];
    }

    return definition;
}


// --- Feature -----------------------------------------------------------------

annotation { "Feature Type Name"        : "Sidewall rout surface",
             "Feature Type Description" : "Creates a SW rout surface based on region inputs",
             "Editing Logic Function"   : "generateSWRoutEditingLogic" }
export const SWRout = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Bottom surface",
                     "Filter" : EntityType.BODY && BodyType.SHEET, "MaxNumberOfPicks" : 1,
                     "Description" : "Bottom of ski. Must extend beyond side surface." }
        definition.bottomSheet is Query;

        annotation { "Name" : "Side surface",
                     "Filter" : EntityType.BODY && BodyType.SHEET, "MaxNumberOfPicks" : 1,
                     "Description" : "Side surface of ski." }
        definition.sideSheet is Query;

        annotation { "Name" : "Reference wire",
                     "Filter" : BodyType.WIRE, "MaxNumberOfPicks" : 1,
                     "Description" : "Wire defining the ski path. Used for region extents and endpoint trimming." }
        definition.refWire is Query;

        annotation { "Name" : "Reference wire origin",
                     "Filter" : EntityType.VERTEX || (EntityType.FACE && GeometryType.PLANE) || BodyType.MATE_CONNECTOR,
                     "MaxNumberOfPicks" : 1,
                     "Description" : "Reference point defining t=0 (arc-length origin) along the reference wire." }
        definition.refWireOrigin is Query;

        annotation { "Name" : "Regions", "Item name" : "Region",
                     "Item label template" : "#name",
                     "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS }
        definition.swRoutRegions is array;
        for (var region in definition.swRoutRegions)
        {
            annotation { "Name" : "Region number", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isInteger(region.regionNum, RegionNumBounds);

            annotation { "Name" : "Name" }
            region.name is string;

            annotation { "Name" : "needsDefaultName", "Default" : true, "UIHint" : UIHint.ALWAYS_HIDDEN }
            region.needsDefaultName is boolean;

            annotation { "Name" : "Extent type", "Default" : SWRoutExtentType.ALONG_REF,
                         "UIHint" : UIHint.HORIZONTAL_ENUM }
            region.extentType is SWRoutExtentType;

            if (region.extentType == SWRoutExtentType.QUERY)
            {
                annotation { "Name" : "Extent points",
                             "Filter" : EntityType.VERTEX || GeometryType.PLANE || BodyType.MATE_CONNECTOR,
                             "MaxNumberOfPicks" : 2 }
                region.extentQueries is Query;
            }

            if (region.extentType == SWRoutExtentType.ALONG_REF)
            {
                annotation { "Name" : "Start",
                             "Description" : "Arc-length distance from ref wire origin to region start" }
                isLength(region.startX, LENGTH_BOUNDS);

                annotation { "Name" : "End",
                             "Description" : "Arc-length distance from ref wire origin to region end" }
                isLength(region.endX, LENGTH_BOUNDS);
            }

            annotation { "Name" : "Sidewall rout angle" }
            isAngle(region.swRoutAngle, SWRoutAngleBounds);

            annotation { "Name" : "Distance from bottom rout begins" }
            isLength(region.distAboveBottom, DistAboveBottomBounds);

            annotation { "Name" : "SW rout height" }
            isLength(region.swRoutHeight, SWRoutHeightBounds);

            annotation { "Name" : "SW rout step-in" }
            isLength(region.swRoutStepin, SWStepInBounds);

            annotation { "Name" : "Region length", "UIHint" : UIHint.READ_ONLY }
            isLength(region.regionLength, LENGTH_BOUNDS);
        }

        annotation { "Name" : "Intersections", "Item name" : "Intersection",
                     "Item label template" : "#intersectionName",
                     "UIHint" : [UIHint.PREVENT_ARRAY_REORDER, UIHint.COLLAPSE_ARRAY_ITEMS] }
        definition.swRoutIntersections is array;
        for (var intr in definition.swRoutIntersections)
        {
            annotation { "Name" : "Intersection number", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isInteger(intr.intersectionNum, RegionNumBounds);

            annotation { "Name" : "Name" }
            intr.intersectionName is string;

            annotation { "Name" : "needsIntersectionName", "Default" : true, "UIHint" : UIHint.ALWAYS_HIDDEN }
            intr.needsIntersectionName is boolean;

            annotation { "Name" : "RegionANum", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isInteger(intr.regionANum, RegionNumBounds);

            annotation { "Name" : "Region A", "UIHint" : UIHint.READ_ONLY }
            intr.regionAName is string;

            annotation { "Name" : "RegionBNum", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isInteger(intr.regionBNum, RegionNumBounds);

            annotation { "Name" : "Region B", "UIHint" : UIHint.READ_ONLY }
            intr.regionBName is string;

            annotation { "Name" : "Blend regions?", "Default" : false }
            intr.blend is boolean;

            if (intr.blend)
            {
                annotation { "Name" : "Start continuity", "UIHint" : UIHint.SHOW_LABEL }
                intr.startContinuity is GeometricContinuity;

                annotation { "Name" : "Start distance" }
                isLength(intr.startDist, LENGTH_BOUNDS);

                annotation { "Name" : "End continuity", "UIHint" : UIHint.SHOW_LABEL }
                intr.endContinuity is GeometricContinuity;

                annotation { "Name" : "End distance" }
                isLength(intr.endDist, LENGTH_BOUNDS);
            }
        }

        annotation { "Name" : "Output profile wires", "Default" : false }
        definition.outputProfileWires is boolean;

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Step through", "Default" : false }
            definition.debugStepThrough is boolean;

            annotation { "Group Name" : "Step through options",
                         "Driving Parameter" : "debugStepThrough",
                         "Collapsed By Default" : false }
            {
                annotation { "Name" : "Step (0-9)", "UIHint" : UIHint.SHOW_LABEL }
                isInteger(definition.debugStep, DEBUG_STEP_BOUNDS);
            }

            annotation { "Name" : "Print debug", "Default" : false }
            definition.debugPrint is boolean;

            annotation { "Name" : "Detailed BSplines", "Default" : false,
                         "UIHint" : UIHint.SHOW_LABEL }
            definition.debugDetailedBSplines is boolean;
        }
    }
    {
        var stepThrough = definition.debugStepThrough;
        var step        = definition.debugStep;
        var sideNames   = ["+Y", "-Y"];
        var debugFmt    = definition.debugDetailedBSplines ?
                PrintFormat.DETAILS : PrintFormat.METADATA;

        // Build refWire path for region parameterization and bounding planes
        var refWirePath = constructPath(context,
                qOwnedByBody(definition.refWire, EntityType.EDGE));

        // Process and sort regions
        var sortedRegions = processSwRoutRegions(context, id, definition, refWirePath);
        validateSwRoutRegionsNoOverlap(sortedRegions);
        var nRegions = size(sortedRegions);

        if (nRegions == 0)
        {
            throw regenError("Add at least one region to define the sidewall rout.");
        }

        if (definition.debugPrint)
        {
            println("=== SWRout debug ===");
            println("  nRegions = " ~ toString(nRegions));
        }

        // State maps keyed by toString(region index)
        var regionBottomCopyQ  = {};
        var regionSideCopyQ    = {};
        var regionBottomSign   = {};  // per-region bottomOffsetSign
        var regionSideSign     = {};  // per-region sideOffsetSign
        var washedInitialWires = {};
        var washedStartWires   = {};
        var washedStepInWires  = {};
        var washedStopWires    = {};

        // =====================================================================
        // Step 0: per region -- copy and trim bottom and side surfaces,
        //         compute per-region offset signs from trimmed copies.
        // =====================================================================
        for (var r = 0; r < nRegions; r += 1)
        {
            var region = sortedRegions[r];
            var rName  = region.name;
            var rKey   = toString(r);

            var tMid       = (region.tStart + region.tEnd) / 2;
            var midTL      = evPathTangentLines(context, refWirePath, [tMid]);
            var probePoint = midTL.tangentLines[0].origin;

            opPattern(context, id + ("bottomCopy" ~ r), {
                    "entities"      : definition.bottomSheet,
                    "transforms"    : [transform(vector(0, 0, 0) * meter)],
                    "instanceNames" : ["1"]
            });
            var bottomQ = qCreatedBy(id + ("bottomCopy" ~ r), EntityType.BODY);

            opPattern(context, id + ("sideCopy" ~ r), {
                    "entities"      : definition.sideSheet,
                    "transforms"    : [transform(vector(0, 0, 0) * meter)],
                    "instanceNames" : ["1"]
            });
            var sideQ = qCreatedBy(id + ("sideCopy" ~ r), EntityType.BODY);

            var startPl = createRegionBoundingPlane(context, refWirePath, region.tStart);
            bottomQ = splitAndKeep(context, id + ("trimBotStart" ~ r), bottomQ, startPl, probePoint);
            sideQ   = splitAndKeep(context, id + ("trimSideStart" ~ r), sideQ, startPl, probePoint);

            var endPl = createRegionBoundingPlane(context, refWirePath, region.tEnd);
            bottomQ = splitAndKeep(context, id + ("trimBotEnd" ~ r), bottomQ, endPl, probePoint);
            sideQ   = splitAndKeep(context, id + ("trimSideEnd" ~ r), sideQ, endPl, probePoint);

            // Compute offset signs from the trimmed copies for this region.
            // Using the trimmed geometry is more reliable than querying the full
            // surface at a fixed UV midpoint, especially for curved ski geometry.
            var bFace   = qNthElement(qOwnedByBody(bottomQ, EntityType.FACE), 0);
            var bNormal = evFaceTangentPlane(context, {
                    "face" : bFace, "parameter" : vector(0.5, 0.5) }).normal;
            regionBottomSign[rKey] = bNormal[2] >= 0 ? 1 : -1;

            var sFace   = qNthElement(qOwnedByBody(sideQ, EntityType.FACE), 0);
            var sNormal = evFaceTangentPlane(context, {
                    "face" : sFace, "parameter" : vector(0.5, 0.5) }).normal;
            regionSideSign[rKey] = sNormal[1] >= 0 ? 1 : -1;

            if (definition.debugPrint)
            {
                println("  [" ~ rName ~ "] bottomSign=" ~ toString(regionBottomSign[rKey]) ~
                        " sideSign=" ~ toString(regionSideSign[rKey]));
            }

            setProperty(context, {
                    "entities"     : bottomQ,
                    "propertyType" : PropertyType.NAME,
                    "value"        : "Bottom copy [" ~ rName ~ "]"
            });
            setProperty(context, {
                    "entities"     : sideQ,
                    "propertyType" : PropertyType.NAME,
                    "value"        : "Side copy [" ~ rName ~ "]"
            });

            regionBottomCopyQ[rKey] = bottomQ;
            regionSideCopyQ[rKey]   = sideQ;
        }

        if (stepThrough && step == 0) { return; }

        // =====================================================================
        // Step 1: per region -- intersect surfaces -> initial wire
        // =====================================================================
        for (var r = 0; r < nRegions; r += 1)
        {
            var region  = sortedRegions[r];
            var rName   = region.name;
            var rKey    = toString(r);
            var bottomQ = regionBottomCopyQ[rKey];
            var sideQ   = regionSideCopyQ[rKey];

            intersectionCurve(context, id + ("initialWire" ~ r), {
                    "group1" : bottomQ,
                    "group2" : sideQ
            });
            var wires = rebuildWire(context, id + ("rebuildInitial" ~ r),
                    qCreatedBy(id + ("initialWire" ~ r), EntityType.BODY));
            for (var s = 0; s < size(wires); s += 1)
            {
                setProperty(context, {
                        "entities"     : wires[s],
                        "propertyType" : PropertyType.NAME,
                        "value"        : "Initial wire [" ~ rName ~ "] " ~ sideNames[s]
                });
            }
            washedInitialWires[rKey] = wires;
        }

        if (stepThrough && step == 1) { return; }

        // =====================================================================
        // Step 2: per region -- offset bottom copy, intersect -> start wire
        // =====================================================================
        for (var r = 0; r < nRegions; r += 1)
        {
            var region  = sortedRegions[r];
            var rName   = region.name;
            var rKey    = toString(r);
            var bottomQ = regionBottomCopyQ[rKey];
            var sideQ   = regionSideCopyQ[rKey];
            var bSign   = regionBottomSign[rKey];

            opOffsetFace(context, id + ("startBottomOffset" ~ r), {
                    "moveFaces"      : qUnion([qOwnedByBody(bottomQ, EntityType.FACE)]),
                    "offsetDistance" : bSign * region.distAboveBottom
            });
            setProperty(context, {
                    "entities"     : bottomQ,
                    "propertyType" : PropertyType.NAME,
                    "value"        : "Bottom (offset) [" ~ rName ~ "]"
            });

            intersectionCurve(context, id + ("startIntersect" ~ r), {
                    "group1" : sideQ,
                    "group2" : bottomQ
            });
            var wires = rebuildWire(context, id + ("rebuildStart" ~ r),
                    qCreatedBy(id + ("startIntersect" ~ r), EntityType.BODY));
            for (var s = 0; s < size(wires); s += 1)
            {
                setProperty(context, {
                        "entities"     : wires[s],
                        "propertyType" : PropertyType.NAME,
                        "value"        : "Start wire [" ~ rName ~ "] " ~ sideNames[s]
                });
            }
            washedStartWires[rKey] = wires;

            if (definition.debugPrint)
            {
                for (var s = 0; s < size(wires); s += 1)
                {
                    debugPrintWireBSplines(context, wires[s],
                            "Start wire [" ~ rName ~ "] " ~ sideNames[s], debugFmt);
                }
            }
        }

        if (stepThrough && step == 2) { return; }

        // =====================================================================
        // Step 3: per region -- offset side copy (if stepIn > 0) -> step-in wire
        // =====================================================================
        for (var r = 0; r < nRegions; r += 1)
        {
            var region = sortedRegions[r];
            var rName  = region.name;
            var rKey   = toString(r);
            var sSign  = regionSideSign[rKey];

            if (region.swRoutStepin > 0 * millimeter)
            {
                var sideQ = regionSideCopyQ[rKey];

                opOffsetFace(context, id + ("stepInSideOffset" ~ r), {
                        "moveFaces"      : qUnion([qOwnedByBody(sideQ, EntityType.FACE)]),
                        "offsetDistance" : -sSign * region.swRoutStepin
                });
                setProperty(context, {
                        "entities"     : sideQ,
                        "propertyType" : PropertyType.NAME,
                        "value"        : "Side (offset) [" ~ rName ~ "]"
                });

                intersectionCurve(context, id + ("stepInIntersect" ~ r), {
                        "group1" : sideQ,
                        "group2" : regionBottomCopyQ[rKey]
                });
                var wires = rebuildWire(context, id + ("rebuildStepIn" ~ r),
                        qCreatedBy(id + ("stepInIntersect" ~ r), EntityType.BODY));
                for (var s = 0; s < size(wires); s += 1)
                {
                    setProperty(context, {
                            "entities"     : wires[s],
                            "propertyType" : PropertyType.NAME,
                            "value"        : "Step-in wire [" ~ rName ~ "] " ~ sideNames[s]
                    });
                }
                washedStepInWires[rKey] = wires;

                if (definition.debugPrint)
                {
                    for (var s = 0; s < size(wires); s += 1)
                    {
                        debugPrintWireBSplines(context, wires[s],
                                "Step-in wire [" ~ rName ~ "] " ~ sideNames[s], debugFmt);
                    }
                }
            }
        }

        if (stepThrough && step == 3) { return; }

        // =====================================================================
        // Step 4: per region -- offset copies to stop position
        //   routSpanHeight = swRoutHeight - distAboveBottom
        //   routSpanSide   = routSpanHeight * tan(swRoutAngle)
        //   bottomCopy is at distAboveBottom; add routSpanHeight to reach swRoutHeight
        //   sideCopy is at swRoutStepin (or 0); add routSpanSide inward
        // =====================================================================
        for (var r = 0; r < nRegions; r += 1)
        {
            var region  = sortedRegions[r];
            var rName   = region.name;
            var rKey    = toString(r);
            var bottomQ = regionBottomCopyQ[rKey];
            var sideQ   = regionSideCopyQ[rKey];
            var bSign   = regionBottomSign[rKey];
            var sSign   = regionSideSign[rKey];

            var routSpanHeight = region.swRoutHeight - region.distAboveBottom;
            var routSpanSide   = routSpanHeight * tan(region.swRoutAngle);

            if (definition.debugPrint)
            {
                println("  [" ~ rName ~ "] routSpanHeight = " ~ toString(routSpanHeight));
                println("  [" ~ rName ~ "] routSpanSide   = " ~ toString(routSpanSide));
            }

            opOffsetFace(context, id + ("stopBottomOffset" ~ r), {
                    "moveFaces"      : qUnion([qOwnedByBody(bottomQ, EntityType.FACE)]),
                    "offsetDistance" : bSign * routSpanHeight
            });
            setProperty(context, {
                    "entities"     : bottomQ,
                    "propertyType" : PropertyType.NAME,
                    "value"        : "Stop bottom [" ~ rName ~ "]"
            });

            opOffsetFace(context, id + ("stopSideOffset" ~ r), {
                    "moveFaces"      : qUnion([qOwnedByBody(sideQ, EntityType.FACE)]),
                    "offsetDistance" : -sSign * routSpanSide
            });
            setProperty(context, {
                    "entities"     : sideQ,
                    "propertyType" : PropertyType.NAME,
                    "value"        : "Stop side [" ~ rName ~ "]"
            });
        }

        if (stepThrough && step == 4) { return; }

        // =====================================================================
        // Step 5: per region -- extend stop side if gap, intersect -> stop wire
        // =====================================================================
        for (var r = 0; r < nRegions; r += 1)
        {
            var region  = sortedRegions[r];
            var rName   = region.name;
            var rKey    = toString(r);
            var bottomQ = regionBottomCopyQ[rKey];
            var sideQ   = regionSideCopyQ[rKey];

            var gapDist = evDistance(context, {
                    "side0" : sideQ,
                    "side1" : bottomQ
            }).distance;

            if (definition.debugPrint)
            {
                println("  [" ~ rName ~ "] stop surface gap = " ~ toString(gapDist));
            }

            if (gapDist > 0 * meter)
            {
                var oneSidedEdges = evaluateQuery(context,
                        qEdgeTopologyFilter(qOwnedByBody(sideQ, EntityType.EDGE),
                        EdgeTopology.ONE_SIDED));

                var minEdgeDist = 1e10 * meter;
                for (var edge in oneSidedEdges)
                {
                    var dd = evDistance(context, { "side0" : edge, "side1" : bottomQ }).distance;
                    if (dd < minEdgeDist) { minEdgeDist = dd; }
                }

                var edgesToExtend = [];
                for (var edge in oneSidedEdges)
                {
                    var dd = evDistance(context, { "side0" : edge, "side1" : bottomQ }).distance;
                    if (dd < minEdgeDist + 1e-4 * meter)
                    {
                        edgesToExtend = append(edgesToExtend, edge);
                    }
                }

                extendSurface(context, id + ("extendStopSide" ~ r), {
                        "entities"           : qUnion(edgesToExtend),
                        "tangentPropagation" : true,
                        "endCondition"       : ExtendBoundingType.BLIND,
                        "oppositeDirection"  : false,
                        "extendDistance"     : gapDist + 1 * millimeter,
                        "maintainCurvature"  : true
                });
            }

            intersectionCurve(context, id + ("stopWire" ~ r), {
                    "group1" : sideQ,
                    "group2" : bottomQ
            });
            var wires = rebuildWire(context, id + ("rebuildStop" ~ r),
                    qCreatedBy(id + ("stopWire" ~ r), EntityType.BODY));
            for (var s = 0; s < size(wires); s += 1)
            {
                setProperty(context, {
                        "entities"     : wires[s],
                        "propertyType" : PropertyType.NAME,
                        "value"        : "Stop wire [" ~ rName ~ "] " ~ sideNames[s]
                });
            }
            washedStopWires[rKey] = wires;

            if (definition.debugPrint)
            {
                for (var s = 0; s < size(wires); s += 1)
                {
                    debugPrintWireBSplines(context, wires[s],
                            "Stop wire [" ~ rName ~ "] " ~ sideNames[s], debugFmt);
                }
            }
        }

        if (stepThrough && step == 5) { return; }

        // =====================================================================
        // Step 6: per region -- loft initial wire -> start wire
        // =====================================================================
        for (var r = 0; r < nRegions; r += 1)
        {
            var region     = sortedRegions[r];
            var rName      = region.name;
            var rKey       = toString(r);
            var initWires  = washedInitialWires[rKey];
            var startWires = washedStartWires[rKey];

            for (var s = 0; s < size(initWires); s += 1)
            {
                var initialEdges = qUnion([qOwnedByBody(initWires[s],  EntityType.EDGE)]);
                var startEdges   = qUnion([qOwnedByBody(startWires[s], EntityType.EDGE)]);
                var loftedSurfs  = [];
                var iterEdges    = evaluateQuery(context, initialEdges);

                for (var i = 0; i < size(iterEdges); i += 1)
                {
                    var initialEdge = iterEdges[i];
                    var midPoint = evEdgeTangentLine(context, {
                            "edge"      : initialEdge,
                            "parameter" : 0.5
                    }).origin;
                    var startEdge = qClosestTo(startEdges, midPoint);
                    try
                    {
                        opLoft(context, id + ("initStartLoft" ~ r ~ "_" ~ s ~ "_" ~ i), {
                                "profileSubqueries" : [initialEdge, startEdge],
                                "bodyType"          : ToolBodyType.SURFACE
                        });
                        loftedSurfs = append(loftedSurfs,
                                qCreatedBy(id + ("initStartLoft" ~ r ~ "_" ~ s ~ "_" ~ i), EntityType.BODY));
                    }
                    catch (error)
                    {
                        addDebugEntities(context, startEdge,   DebugColor.RED);
                        addDebugEntities(context, initialEdge, DebugColor.GREEN);
                    }
                }
                opBoolean(context, id + ("combineInitStart" ~ r ~ "_" ~ s), {
                        "tools"         : qUnion(loftedSurfs),
                        "operationType" : BooleanOperationType.UNION
                });
                setProperty(context, {
                        "entities"     : qUnion(loftedSurfs),
                        "propertyType" : PropertyType.NAME,
                        "value"        : "Initial to Start [" ~ rName ~ "] " ~ sideNames[s]
                });
            }
        }

        if (stepThrough && step == 6) { return; }

        // =====================================================================
        // Step 7: per region -- loft start -> step-in wire (if stepIn > 0)
        // =====================================================================
        for (var r = 0; r < nRegions; r += 1)
        {
            var region = sortedRegions[r];
            var rName  = region.name;
            var rKey   = toString(r);

            if (region.swRoutStepin > 0 * millimeter)
            {
                var startWires  = washedStartWires[rKey];
                var stepInWires = washedStepInWires[rKey];

                for (var s = 0; s < size(startWires); s += 1)
                {
                    var startEdges  = qUnion([qOwnedByBody(startWires[s],  EntityType.EDGE)]);
                    var stepInEdges = qUnion([qOwnedByBody(stepInWires[s], EntityType.EDGE)]);
                    var loftedSurfs = [];
                    var iterEdges   = evaluateQuery(context, startEdges);

                    for (var i = 0; i < size(iterEdges); i += 1)
                    {
                        var startEdge = iterEdges[i];
                        var midPoint  = evEdgeTangentLine(context, {
                                "edge"      : startEdge,
                                "parameter" : 0.5
                        }).origin;
                        var stepInEdge = qClosestTo(stepInEdges, midPoint);
                        try
                        {
                            opLoft(context, id + ("startStepInLoft" ~ r ~ "_" ~ s ~ "_" ~ i), {
                                    "profileSubqueries" : [startEdge, stepInEdge],
                                    "bodyType"          : ToolBodyType.SURFACE
                            });
                            loftedSurfs = append(loftedSurfs,
                                    qCreatedBy(id + ("startStepInLoft" ~ r ~ "_" ~ s ~ "_" ~ i), EntityType.BODY));
                        }
                        catch (error)
                        {
                            addDebugEntities(context, stepInEdge, DebugColor.RED);
                            addDebugEntities(context, startEdge,  DebugColor.GREEN);
                        }
                    }
                    opBoolean(context, id + ("combineStartStepIn" ~ r ~ "_" ~ s), {
                            "tools"         : qUnion(loftedSurfs),
                            "operationType" : BooleanOperationType.UNION
                    });
                    setProperty(context, {
                            "entities"     : qUnion(loftedSurfs),
                            "propertyType" : PropertyType.NAME,
                            "value"        : "Start to Step-In [" ~ rName ~ "] " ~ sideNames[s]
                    });
                }
            }
        }

        if (stepThrough && step == 7) { return; }

        // =====================================================================
        // Step 8: per region -- loft lower wire (step-in or start) -> stop wire
        // =====================================================================
        for (var r = 0; r < nRegions; r += 1)
        {
            var region     = sortedRegions[r];
            var rName      = region.name;
            var rKey       = toString(r);
            var lowerWires = (region.swRoutStepin > 0 * millimeter) ?
                    washedStepInWires[rKey] : washedStartWires[rKey];
            var stopWires  = washedStopWires[rKey];

            for (var s = 0; s < size(lowerWires); s += 1)
            {
                var lowerEdges = qUnion([qOwnedByBody(lowerWires[s], EntityType.EDGE)]);
                var stopEdges  = qUnion([qOwnedByBody(stopWires[s],  EntityType.EDGE)]);
                var loftedSurfs = [];
                var iterEdges   = evaluateQuery(context, lowerEdges);

                for (var i = 0; i < size(iterEdges); i += 1)
                {
                    var lowerEdge = iterEdges[i];
                    var midPoint  = evEdgeTangentLine(context, {
                            "edge"      : lowerEdge,
                            "parameter" : 0.5
                    }).origin;
                    var stopEdge = qClosestTo(stopEdges, midPoint);
                    try
                    {
                        opLoft(context, id + ("lowerStopLoft" ~ r ~ "_" ~ s ~ "_" ~ i), {
                                "profileSubqueries" : [lowerEdge, stopEdge],
                                "bodyType"          : ToolBodyType.SURFACE
                        });
                        loftedSurfs = append(loftedSurfs,
                                qCreatedBy(id + ("lowerStopLoft" ~ r ~ "_" ~ s ~ "_" ~ i), EntityType.BODY));
                    }
                    catch (error)
                    {
                        addDebugEntities(context, stopEdge,  DebugColor.RED);
                        addDebugEntities(context, lowerEdge, DebugColor.GREEN);
                    }
                }
                opBoolean(context, id + ("combineLowerStop" ~ r ~ "_" ~ s), {
                        "tools"         : qUnion(loftedSurfs),
                        "operationType" : BooleanOperationType.UNION
                });
                setProperty(context, {
                        "entities"     : qUnion(loftedSurfs),
                        "propertyType" : PropertyType.NAME,
                        "value"        : "Lower to Stop [" ~ rName ~ "] " ~ sideNames[s]
                });
            }
        }

        if (stepThrough && step == 8) { return; }

        // =====================================================================
        // Step 9: blend between adjacent region surfaces (placeholder)
        //   G0: regions already share boundary planes -- no work needed.
        //   G1/G2: trim each region back by startDist/endDist and bridge.
        // =====================================================================
    });


// --- Helpers -----------------------------------------------------------------

/**
 * Splits body with boundPlane, keeps the piece whose bounding-box center is
 * closest to probePoint, deletes the other.  Returns the query of the kept
 * piece.  If the plane does not intersect the body, returns body unchanged.
 */
function splitAndKeep(context is Context, id is Id, body is Query,
        boundPlane is Plane, probePoint is Vector) returns Query
{
    // opSplitPart requires a Query tool -- create a temporary construction plane
    opPlane(context, id + "pl", { "plane" : boundPlane });
    var planeQ = qCreatedBy(id + "pl", EntityType.BODY);

    var splitOk = false;
    try
    {
        opSplitPart(context, id + "split", {
                "targets"    : body,
                "tool"       : planeQ,
                "keepTools"  : false
        });
        splitOk = true;
    }
    catch (e) {}

    // opSplitPart does not delete construction planes regardless of keepTools
    try silent(opDeleteBodies(context, id + "delPl", { "entities" : planeQ }));

    if (!splitOk)
    {
        return body;
    }

    var pieceA = qSplitBy(id + "split", EntityType.BODY, false);
    var pieceB = qSplitBy(id + "split", EntityType.BODY, true);

    var bbA = evBox3d(context, { "topology" : pieceA, "tight" : true });
    var bbB = evBox3d(context, { "topology" : pieceB, "tight" : true });
    var cA  = (bbA.minCorner + bbA.maxCorner) / 2;
    var cB  = (bbB.minCorner + bbB.maxCorner) / 2;

    if (norm(cA - probePoint) <= norm(cB - probePoint))
    {
        opDeleteBodies(context, id + "del", { "entities" : pieceB });
        return pieceA;
    }
    else
    {
        opDeleteBodies(context, id + "del", { "entities" : pieceA });
        return pieceB;
    }
}


/**
 * Rebuilds a multi-edge wire as single-edge BSpline bodies, split at the
 * front plane (y=0) if the wire spans both sides.
 *
 * Returns [posWire] if all points are on one side, or [posWire, negWire]
 * if the wire crosses y=0.  Deletes the original wire body.
 */
function rebuildWire(context is Context, id is Id, wireBody is Query) returns array
{
    const RESAMPLE_COUNT = 20;
    const CHAIN_TOL      = 1e-5 * meter;
    const MIN_CHORD      = 0.5 * millimeter;
    const MIN_PTS        = 4;  // minimum points for a valid approximateSpline

    var allEdges = evaluateQuery(context, qOwnedByBody(wireBody, EntityType.EDGE));
    var n = size(allEdges);

    // Collect edge endpoints for chain walking
    var ePt0 = [];
    var ePt1 = [];
    for (var edge in allEdges)
    {
        ePt0 = append(ePt0, evEdgeTangentLine(context, { "edge" : edge, "parameter" : 0.0 }).origin);
        ePt1 = append(ePt1, evEdgeTangentLine(context, { "edge" : edge, "parameter" : 1.0 }).origin);
    }

    // Find start edge: pt0 not matched by any other edge's pt1.
    // For closed loops, all edges connect so startIdx stays 0.
    var startIdx = 0;
    for (var i = 0; i < n; i += 1)
    {
        var isStart = true;
        for (var j = 0; j < n; j += 1)
        {
            if (i != j && norm(ePt0[i] - ePt1[j]) < CHAIN_TOL)
            {
                isStart = false;
                break;
            }
        }
        if (isStart) { startIdx = i; break; }
    }

    // Chain-walk the edges tip-to-tail
    var orderedIdx = [startIdx];
    var orderedFwd = [true];
    var visited    = {};
    visited[toString(startIdx)] = true;
    var currentPt = ePt1[startIdx];
    for (var step = 0; step < n - 1; step += 1)
    {
        for (var j = 0; j < n; j += 1)
        {
            if (visited[toString(j)] != true)
            {
                if (norm(ePt0[j] - currentPt) < CHAIN_TOL)
                {
                    orderedIdx = append(orderedIdx, j);
                    orderedFwd = append(orderedFwd, true);
                    visited[toString(j)] = true;
                    currentPt = ePt1[j];
                    break;
                }
                else if (norm(ePt1[j] - currentPt) < CHAIN_TOL)
                {
                    orderedIdx = append(orderedIdx, j);
                    orderedFwd = append(orderedFwd, false);
                    visited[toString(j)] = true;
                    currentPt = ePt0[j];
                    break;
                }
            }
        }
    }

    // Sample edges in order, skipping slivers
    var allPts = [];
    for (var i = 0; i < size(orderedIdx); i += 1)
    {
        var idx  = orderedIdx[i];
        var fwd  = orderedFwd[i];
        var edge = allEdges[idx];
        if (norm(ePt1[idx] - ePt0[idx]) < MIN_CHORD) { continue; }
        var kStart = (size(allPts) == 0) ? 0 : 1;
        for (var k = kStart; k <= RESAMPLE_COUNT; k += 1)
        {
            var t     = k / RESAMPLE_COUNT;
            var param = fwd ? t : (1.0 - t);
            allPts = append(allPts, evEdgeTangentLine(context, {
                    "edge"      : edge,
                    "parameter" : param
            }).origin);
        }
    }

    var nPts = size(allPts);

    // Check for any negative-Y points
    var hasNegY = false;
    for (var pt in allPts)
    {
        if (pt[1] < 0 * meter) { hasNegY = true; break; }
    }

    if (!hasNegY)
    {
        // Single side -- no split needed
        if (nPts < MIN_PTS)
        {
            opDeleteBodies(context, id + "deleteWire", { "entities" : wireBody });
            return [];
        }
        var curve = approximateSpline(context, {
                "targets"          : [approximationTarget({ "positions" : allPts })],
                "degree"           : 3,
                "tolerance"        : 1e-5 * meter,
                "isPeriodic"       : false,
                "maxControlPoints" : 200
        })[0];
        opDeleteBodies(context, id + "deleteWire", { "entities" : wireBody });
        opCreateBSplineCurve(context, id + "rebuiltWirePos", { "bSplineCurve" : curve });
        return [qCreatedBy(id + "rebuiltWirePos", EntityType.BODY)];
    }

    // Find both y=0 crossings (wrap-around)
    var crossingA = -1;  // last y<0 before y>=0
    var crossingB = -1;  // last y>=0 before y<0
    for (var i = 0; i < nPts; i += 1)
    {
        var j  = i + 1;
        if (j >= nPts) { j = 0; }
        var yi = allPts[i][1];
        var yj = allPts[j][1];
        if (yi < 0 * meter && yj >= 0 * meter) { crossingA = i; }
        else if (yi >= 0 * meter && yj < 0 * meter) { crossingB = i; }
    }

    // If only one crossing found, fall back to single wire
    if (crossingA == -1 || crossingB == -1)
    {
        if (nPts < MIN_PTS)
        {
            opDeleteBodies(context, id + "deleteWire", { "entities" : wireBody });
            return [];
        }
        var curve = approximateSpline(context, {
                "targets"          : [approximationTarget({ "positions" : allPts })],
                "degree"           : 3,
                "tolerance"        : 1e-5 * meter,
                "isPeriodic"       : false,
                "maxControlPoints" : 200
        })[0];
        opDeleteBodies(context, id + "deleteWire", { "entities" : wireBody });
        opCreateBSplineCurve(context, id + "rebuiltWirePos", { "bSplineCurve" : curve });
        return [qCreatedBy(id + "rebuiltWirePos", EntityType.BODY)];
    }

    // Interpolated y=0 boundary points
    var aNext = crossingA + 1;
    if (aNext >= nPts) { aNext = 0; }
    var tA  = (-allPts[crossingA][1]) / (allPts[aNext][1] - allPts[crossingA][1]);
    var ptA = allPts[crossingA] + tA * (allPts[aNext] - allPts[crossingA]);

    var bNext = crossingB + 1;
    if (bNext >= nPts) { bNext = 0; }
    var tB  = allPts[crossingB][1] / (allPts[crossingB][1] - allPts[bNext][1]);
    var ptB = allPts[crossingB] + tB * (allPts[bNext] - allPts[crossingB]);

    // Build +Y segment: ptA -> points from aNext through crossingB -> ptB
    var posPts = [ptA];
    for (var k = 1; k <= nPts; k += 1)
    {
        var idx = crossingA + k;
        if (idx >= nPts) { idx = idx - nPts; }
        if (idx == bNext) { break; }
        posPts = append(posPts, allPts[idx]);
    }
    posPts = append(posPts, ptB);

    // Build -Y segment: ptB -> points from bNext through crossingA -> ptA
    var negPts = [ptB];
    for (var k = 1; k <= nPts; k += 1)
    {
        var idx = crossingB + k;
        if (idx >= nPts) { idx = idx - nPts; }
        if (idx == aNext) { break; }
        negPts = append(negPts, allPts[idx]);
    }
    negPts = append(negPts, ptA);

    opDeleteBodies(context, id + "deleteWire", { "entities" : wireBody });

    var result = [];

    if (size(posPts) >= MIN_PTS)
    {
        var curvePos = approximateSpline(context, {
                "targets"          : [approximationTarget({ "positions" : posPts })],
                "degree"           : 3,
                "tolerance"        : 1e-5 * meter,
                "isPeriodic"       : false,
                "maxControlPoints" : 200
        })[0];
        opCreateBSplineCurve(context, id + "rebuiltWirePos", { "bSplineCurve" : curvePos });
        result = append(result, qCreatedBy(id + "rebuiltWirePos", EntityType.BODY));
    }

    if (size(negPts) >= MIN_PTS)
    {
        var curveNeg = approximateSpline(context, {
                "targets"          : [approximationTarget({ "positions" : negPts })],
                "degree"           : 3,
                "tolerance"        : 1e-5 * meter,
                "isPeriodic"       : false,
                "maxControlPoints" : 200
        })[0];
        opCreateBSplineCurve(context, id + "rebuiltWireNeg", { "bSplineCurve" : curveNeg });
        result = append(result, qCreatedBy(id + "rebuiltWireNeg", EntityType.BODY));
    }

    return result;
}


/**
 * Trims the SW rout surface at a point on the reference wire.
 * Keeps the larger of the two split bodies (the portion inside the endpoints).
 * Extends the outside edge by cutterRadius and caps the cut end with a 90-degree revolve.
 */
export function trimSWRout(context is Context, id is Id, swRoutSurface is Query,
        refPath is Path, trimPoint is Query, sideSheet is Query,
        cutterRadius is ValueWithUnits, doRevolve is boolean)
{
    var distResult = evDistance(context, {
            "side0" : trimPoint,
            "side1" : refPath.edges
    });
    var refEdge  = refPath.edges[distResult.sides[1].index];
    var refParam = distResult.sides[1].parameter;
    var edgeLine = evEdgeTangentLine(context, { "edge" : refEdge, "parameter" : refParam });

    var splitPlane = plane(edgeLine.origin, edgeLine.direction);
    opSplitPart(context, id + "splitSWRout", {
            "targets" : swRoutSurface,
            "tool"    : splitPlane
    });

    var splitBodyTrue  = qSplitBy(id + "splitSWRout", EntityType.BODY, true);
    var splitBodyFalse = qSplitBy(id + "splitSWRout", EntityType.BODY, false);

    var trueBox  = evBox3d(context, { "topology" : splitBodyTrue,  "tight" : true });
    var falseBox = evBox3d(context, { "topology" : splitBodyFalse, "tight" : true });

    var trueVol  = (trueBox.maxCorner[0]  - trueBox.minCorner[0])  *
                   (trueBox.maxCorner[1]  - trueBox.minCorner[1])  *
                   (trueBox.maxCorner[2]  - trueBox.minCorner[2]);
    var falseVol = (falseBox.maxCorner[0] - falseBox.minCorner[0]) *
                   (falseBox.maxCorner[1] - falseBox.minCorner[1]) *
                   (falseBox.maxCorner[2] - falseBox.minCorner[2]);

    if (trueVol < falseVol)
    {
        opDeleteBodies(context, id + "deleteSWSplitTrue",  { "entities" : splitBodyTrue });
    }
    else
    {
        opDeleteBodies(context, id + "deleteSWSplitFalse", { "entities" : splitBodyFalse });
    }

    var keptBody      = qCreatedBy(id + "splitSWRout", EntityType.BODY);
    var oneSidedEdges = evaluateQuery(context,
            qEdgeTopologyFilter(qOwnedByBody(keptBody, EntityType.EDGE), EdgeTopology.ONE_SIDED));
    var outsideEdges = [];
    for (var edge in oneSidedEdges)
    {
        var midPoint   = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 0.5 }).origin;
        var distToSide = evDistance(context, { "side0" : midPoint, "side1" : sideSheet }).distance;
        if (distToSide < 1e-5 * meter)
        {
            outsideEdges = append(outsideEdges, edge);
        }
    }
    if (size(outsideEdges) > 0)
    {
        extendSurface(context, id + "extendCutterRadius", {
                "entities"           : qUnion(outsideEdges),
                "tangentPropagation" : true,
                "endCondition"       : ExtendBoundingType.BLIND,
                "oppositeDirection"  : false,
                "extendDistance"     : cutterRadius,
                "maintainCurvature"  : true
        });
    }

    if (doRevolve)
    {
        opRevolve(context, id + "capRevolve", {
                "entities"     : qCreatedBy(id + "splitSWRout", EntityType.EDGE),
                "axis"         : line(edgeLine.origin, edgeLine.direction),
                "angleForward" : 90 * degree
        });
        setProperty(context, {
                "entities"     : qCreatedBy(id + "capRevolve", EntityType.BODY),
                "propertyType" : PropertyType.NAME,
                "value"        : "SWRout trim cap"
        });
    }
}


/**
 * Returns the top boundary wire of the side surface (highest average Z).
 * Deletes the bottom boundary wire unless keepBodies is true.
 */
export function generateDummyTopSurf(context is Context, id is Id,
        sideSheet is Query, keepBodies is boolean) returns Query
{
    var oneSidedEdges = qEdgeTopologyFilter(
            qOwnedByBody(sideSheet, EntityType.EDGE), EdgeTopology.ONE_SIDED);
    opExtractWires(context, id + "extractBoundaryWires", { "edges" : oneSidedEdges });
    var wireBodies = evaluateQuery(context,
            qCreatedBy(id + "extractBoundaryWires", EntityType.BODY));

    var bb0   = evBox3d(context, { "topology" : wireBodies[0], "tight" : true });
    var bb1   = evBox3d(context, { "topology" : wireBodies[1], "tight" : true });
    var midZ0 = (bb0.minCorner[2] + bb0.maxCorner[2]) / 2;
    var midZ1 = (bb1.minCorner[2] + bb1.maxCorner[2]) / 2;
    var topIdx    = (midZ0 > midZ1) ? 0 : 1;
    var bottomIdx = (midZ0 > midZ1) ? 1 : 0;

    setProperty(context, {
            "entities"     : wireBodies[topIdx],
            "propertyType" : PropertyType.NAME,
            "value"        : "Side top boundary wire (raw)"
    });
    setProperty(context, {
            "entities"     : wireBodies[bottomIdx],
            "propertyType" : PropertyType.NAME,
            "value"        : "Side bottom boundary wire"
    });

    if (!keepBodies)
    {
        opDeleteBodies(context, id + "deleteBottomBoundaryWire",
                { "entities" : wireBodies[bottomIdx] });
    }

    return wireBodies[topIdx];
}


/**
 * Prints the BSplineCurve for each edge in a wire body.
 * Intended for debug use only -- gated by definition.debugPrint.
 */
function debugPrintWireBSplines(context is Context, wireBody is Query,
        label is string, format is PrintFormat)
{
    var edges = evaluateQuery(context, qOwnedByBody(wireBody, EntityType.EDGE));
    println("=== " ~ label ~ " (" ~ size(edges) ~ " edge(s)) ===");
    for (var i = 0; i < size(edges); i += 1)
    {
        var curve = evApproximateBSplineCurve(context, { "edge" : edges[i] });
        printBSpline(curve, format, ["  Edge " ~ toString(i) ~ ":"]);
    }
}
