FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");
import(path : "onshape/std/extend.fs", version : "2892.0");
import(path : "onshape/std/faceIntersection.fs", version : "2892.0");

// IMPORT: tools/printing.fs
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/b02d6a2bac551b24347c983f", version : "c104606e8ffc8e0964404bbc");

// import swRoutRegions -- SWRoutExtentType, bounds, region processing functions
export import(path : "7e3b271854475bf6cf878b2b", version : "fc5e7fa037480fd2cbb69c0a");


export const DEBUG_STEP_BOUNDS = { (unitless) : [0, 1, 10]} as IntegerBoundSpec;


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
                    qOwnedByBody(definition.refWire, EntityType.EDGE),
                    { "referenceGeometry" : definition.refWireOrigin }).path;

            var totalLength = 0 * meter;
            for (var edge in refWirePath.edges)
            {
                totalLength += evLength(context, { "entities" : edge });
            }

            // Project refWireOrigin onto path using same edge-walk as QUERY case.
            var tOrigin     = 0;
            var bestOrigin  = undefined;
            var cumLenOrig  = 0 * meter;
            var originPts   = evaluateQuery(context, definition.refWireOrigin);
            if (size(originPts) > 0)
            {
                for (var ei = 0; ei < size(refWirePath.edges); ei += 1)
                {
                    var eLen = evLength(context, { "entities" : refWirePath.edges[ei] });
                    var drO  = evDistance(context, {
                            "side0" : refWirePath.edges[ei],
                            "side1" : originPts[0]
                    });
                    var epO = drO.sides[0].parameter;
                    if (refWirePath.flipped[ei]) { epO = 1 - epO; }
                    if (bestOrigin == undefined || drO.distance < bestOrigin)
                    {
                        bestOrigin = drO.distance;
                        tOrigin    = (cumLenOrig + epO * eLen) / totalLength;
                    }
                    cumLenOrig += eLen;
                }
            }
            // Determine dirSign from refWirePositiveEnd using same edge-walk.
            var tPositiveEnd = 1;
            var bestPos      = undefined;
            var cumLenPos    = 0 * meter;
            var posPts       = evaluateQuery(context, definition.refWirePositiveEnd);
            if (size(posPts) > 0)
            {
                for (var ei = 0; ei < size(refWirePath.edges); ei += 1)
                {
                    var eLen = evLength(context, { "entities" : refWirePath.edges[ei] });
                    var drP  = evDistance(context, {
                            "side0" : refWirePath.edges[ei],
                            "side1" : posPts[0]
                    });
                    var epP = drP.sides[0].parameter;
                    if (refWirePath.flipped[ei]) { epP = 1 - epP; }
                    if (bestPos == undefined || drP.distance < bestPos)
                    {
                        bestPos      = drP.distance;
                        tPositiveEnd = (cumLenPos + epP * eLen) / totalLength;
                    }
                    cumLenPos += eLen;
                }
            }
            var dirSign = (tPositiveEnd > tOrigin) ? 1 : -1;

            for (var i = 0; i < size(definition.swRoutRegions); i += 1)
            {
                var reg = definition.swRoutRegions[i];
                if (reg.extentType == SWRoutExtentType.ALONG_REF &&
                        reg.startX != undefined && reg.endX != undefined)
                {
                    reg.tStart       = min(max(tOrigin + dirSign * reg.startX / totalLength, 0), 1);
                    reg.tEnd         = min(max(tOrigin + dirSign * reg.endX   / totalLength, 0), 1);
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
                            // Project each query point onto the path by finding the closest
                            // edge, then computing arc-length to the closest point on it.
                            var t0Len  = 0 * meter;
                            var t1Len  = 0 * meter;
                            var best0  = undefined;
                            var best1  = undefined;
                            var cumLen = 0 * meter;
                            for (var ei = 0; ei < size(refWirePath.edges); ei += 1)
                            {
                                var eLen = evLength(context, { "entities" : refWirePath.edges[ei] });
                                var dr0  = evDistance(context, {
                                        "side0" : refWirePath.edges[ei],
                                        "side1" : queryPts[0]
                                });
                                var ep0 = dr0.sides[0].parameter;
                                if (refWirePath.flipped[ei]) { ep0 = 1 - ep0; }
                                if (best0 == undefined || dr0.distance < best0)
                                {
                                    best0 = dr0.distance;
                                    t0Len = cumLen + ep0 * eLen;
                                }
                                var dr1 = evDistance(context, {
                                        "side0" : refWirePath.edges[ei],
                                        "side1" : queryPts[1]
                                });
                                var ep1 = dr1.sides[0].parameter;
                                if (refWirePath.flipped[ei]) { ep1 = 1 - ep1; }
                                if (best1 == undefined || dr1.distance < best1)
                                {
                                    best1 = dr1.distance;
                                    t1Len = cumLen + ep1 * eLen;
                                }
                                cumLen += eLen;
                            }
                            var t0 = t0Len / totalLength;
                            var t1 = t1Len / totalLength;
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

        annotation { "Name" : "Positive end",
                     "Filter" : EntityType.VERTEX || (EntityType.FACE && GeometryType.PLANE) || BodyType.MATE_CONNECTOR,
                     "MaxNumberOfPicks" : 1,
                     "Description" : "A point at the positive-X end of the reference wire. Determines the sign convention for startX/endX." }
        definition.refWirePositiveEnd is Query;

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

        // Build refWire path anchored at refWireOrigin so t=0 is the user-
        // specified origin end and startX/endX distances are measured correctly.
        var refWirePath = constructPath(context,
                qOwnedByBody(definition.refWire, EntityType.EDGE),
                { "referenceGeometry" : definition.refWireOrigin }).path;

        // Determine sign convention: positive direction = toward refWirePositiveEnd.
        var tPositiveEnd = evDistancePath(context, {
                "side0" : refWirePath,
                "side1" : definition.refWirePositiveEnd
        }).sides[0].pathParam;
        var tOriginForSign = evDistancePath(context, {
                "side0" : refWirePath,
                "side1" : definition.refWireOrigin
        }).sides[0].pathParam;
        var dirSign = (tPositiveEnd > tOriginForSign) ? 1 : -1;

        // Process and sort regions
        var sortedRegions = processSwRoutRegions(context, id, definition, refWirePath, definition.refWireOrigin, dirSign);
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
        // Per (region, side): array of loft body Queries accumulated across steps 6-8.
        // Key: rKey ~ "_" ~ toString(s).  Used in step 9 to union into final surfaces.
        var regionSurfBodies   = {};

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

            // Pre-extend the side surface BEFORE offsetting so large offsets don't
            // create discontinuous geometry.  Extend all one-sided edges by
            // routSpanHeight plus current gap plus a small buffer.
            var preGapDist = evDistance(context, {
                    "side0" : sideQ,
                    "side1" : bottomQ
            }).distance;
            var preExtendDist = routSpanHeight + preGapDist + 2 * millimeter;
            var preFreeEdges  = qEdgeTopologyFilter(qOwnedByBody(sideQ, EntityType.EDGE),
                    EdgeTopology.ONE_SIDED);
            if (!isQueryEmpty(context, preFreeEdges) && preExtendDist > 0 * meter)
            {
                try
                {
                    extendSurface(context, id + ("preExtendStopSide" ~ r), {
                            "entities"           : preFreeEdges,
                            "tangentPropagation" : true,
                            "endCondition"       : ExtendBoundingType.BLIND,
                            "oppositeDirection"  : false,
                            "extendDistance"     : preExtendDist,
                            "maintainCurvature"  : true
                    });
                }
                catch {}
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

            // Fallback: if a gap still exists after the pre-extension and stop offsets,
            // extend all remaining free edges to close it.
            if (gapDist > 0 * meter)
            {
                var fallbackEdges = qEdgeTopologyFilter(qOwnedByBody(sideQ, EntityType.EDGE),
                        EdgeTopology.ONE_SIDED);
                if (!isQueryEmpty(context, fallbackEdges))
                {
                    try
                    {
                        extendSurface(context, id + ("extendStopSide" ~ r), {
                                "entities"           : fallbackEdges,
                                "tangentPropagation" : true,
                                "endCondition"       : ExtendBoundingType.BLIND,
                                "oppositeDirection"  : false,
                                "extendDistance"     : gapDist + 1 * millimeter,
                                "maintainCurvature"  : true
                        });
                    }
                    catch {}
                }
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
                    catch
                    {
                        addDebugEntities(context, startEdge,   DebugColor.RED);
                        addDebugEntities(context, initialEdge, DebugColor.GREEN);
                    }
                }
                if (size(loftedSurfs) > 1)
                {
                    opBoolean(context, id + ("combineInitStart" ~ r ~ "_" ~ s), {
                            "tools"         : qUnion(loftedSurfs),
                            "operationType" : BooleanOperationType.UNION
                    });
                }
                if (size(loftedSurfs) > 0)
                {
                    setProperty(context, {
                            "entities"     : qUnion(loftedSurfs),
                            "propertyType" : PropertyType.NAME,
                            "value"        : "Initial to Start [" ~ rName ~ "] " ~ sideNames[s]
                    });
                    var rsKey = rKey ~ "_" ~ toString(s);
                    var prev  = (regionSurfBodies[rsKey] != undefined) ? regionSurfBodies[rsKey] : [];
                    regionSurfBodies[rsKey] = append(prev, loftedSurfs[0]);
                }
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
                        catch
                        {
                            addDebugEntities(context, stepInEdge, DebugColor.RED);
                            addDebugEntities(context, startEdge,  DebugColor.GREEN);
                        }
                    }
                    if (size(loftedSurfs) > 1)
                    {
                        opBoolean(context, id + ("combineStartStepIn" ~ r ~ "_" ~ s), {
                                "tools"         : qUnion(loftedSurfs),
                                "operationType" : BooleanOperationType.UNION
                        });
                    }
                    if (size(loftedSurfs) > 0)
                    {
                        setProperty(context, {
                                "entities"     : qUnion(loftedSurfs),
                                "propertyType" : PropertyType.NAME,
                                "value"        : "Start to Step-In [" ~ rName ~ "] " ~ sideNames[s]
                        });
                        var rsKey = rKey ~ "_" ~ toString(s);
                        var prev  = (regionSurfBodies[rsKey] != undefined) ? regionSurfBodies[rsKey] : [];
                        regionSurfBodies[rsKey] = append(prev, loftedSurfs[0]);
                    }
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
                    catch
                    {
                        addDebugEntities(context, stopEdge,  DebugColor.RED);
                        addDebugEntities(context, lowerEdge, DebugColor.GREEN);
                    }
                }
                if (size(loftedSurfs) > 1)
                {
                    opBoolean(context, id + ("combineLowerStop" ~ r ~ "_" ~ s), {
                            "tools"         : qUnion(loftedSurfs),
                            "operationType" : BooleanOperationType.UNION
                    });
                }
                if (size(loftedSurfs) > 0)
                {
                    setProperty(context, {
                            "entities"     : qUnion(loftedSurfs),
                            "propertyType" : PropertyType.NAME,
                            "value"        : "Lower to Stop [" ~ rName ~ "] " ~ sideNames[s]
                    });
                    var rsKey = rKey ~ "_" ~ toString(s);
                    var prev  = (regionSurfBodies[rsKey] != undefined) ? regionSurfBodies[rsKey] : [];
                    regionSurfBodies[rsKey] = append(prev, loftedSurfs[0]);
                }
            }
        }

        if (stepThrough && step == 8) { return; }

        // =====================================================================
        // Step 9: combine per-step lofts into one final surface per (region, side)
        //   Union the step-6, step-7, and step-8 bodies for each (r, s) pair.
        //   These share edges so they form one connected body per side.
        // =====================================================================
        var regionFinalSurfs = {};
        for (var r = 0; r < nRegions; r += 1)
        {
            var region = sortedRegions[r];
            var rName  = region.name;
            var rKey   = toString(r);

            for (var s = 0; s < size(sideNames); s += 1)
            {
                var rsKey  = rKey ~ "_" ~ toString(s);
                var bodies = (regionSurfBodies[rsKey] != undefined) ? regionSurfBodies[rsKey] : [];

                if (size(bodies) == 0)
                {
                    continue;
                }

                if (size(bodies) > 1)
                {
                    opBoolean(context, id + ("combineFinal" ~ r ~ "_" ~ s), {
                            "tools"         : qUnion(bodies),
                            "operationType" : BooleanOperationType.UNION
                    });
                }

                var finalBody = bodies[0];
                setProperty(context, {
                        "entities"     : finalBody,
                        "propertyType" : PropertyType.NAME,
                        "value"        : "SW Rout [" ~ rName ~ "] " ~ sideNames[s]
                });
                regionFinalSurfs[rsKey] = finalBody;
            }
        }

        if (stepThrough && step == 9) { return; }

        // =====================================================================
        // Step 10: delete all reference bodies (copies and wires)
        // =====================================================================
        var refBodiesToDelete = [];
        for (var r = 0; r < nRegions; r += 1)
        {
            var rKey = toString(r);

            if (regionBottomCopyQ[rKey] != undefined)
            {
                refBodiesToDelete = append(refBodiesToDelete, regionBottomCopyQ[rKey]);
            }
            if (regionSideCopyQ[rKey] != undefined)
            {
                refBodiesToDelete = append(refBodiesToDelete, regionSideCopyQ[rKey]);
            }
            if (washedInitialWires[rKey] != undefined)
            {
                for (var wire in washedInitialWires[rKey])
                {
                    refBodiesToDelete = append(refBodiesToDelete, wire);
                }
            }
            if (washedStartWires[rKey] != undefined)
            {
                for (var wire in washedStartWires[rKey])
                {
                    refBodiesToDelete = append(refBodiesToDelete, wire);
                }
            }
            if (washedStepInWires[rKey] != undefined)
            {
                for (var wire in washedStepInWires[rKey])
                {
                    refBodiesToDelete = append(refBodiesToDelete, wire);
                }
            }
            if (washedStopWires[rKey] != undefined)
            {
                for (var wire in washedStopWires[rKey])
                {
                    refBodiesToDelete = append(refBodiesToDelete, wire);
                }
            }
        }

        if (size(refBodiesToDelete) > 0)
        {
            opDeleteBodies(context, id + "deleteRefBodies", {
                    "entities" : qUnion(refBodiesToDelete)
            });
        }
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
    catch {}

    // opSplitPart does not delete construction planes regardless of keepTools
    try silent(opDeleteBodies(context, id + "delPl", { "entities" : planeQ }));

    if (!splitOk)
    {
        return body;
    }

    var pieceA = qSplitBy(id + "split", EntityType.BODY, false);
    var pieceB = qSplitBy(id + "split", EntityType.BODY, true);

    var aEmpty = isQueryEmpty(context, pieceA);
    var bEmpty = isQueryEmpty(context, pieceB);

    // If the plane only grazed the body (one piece is empty), keep the non-empty piece.
    if (aEmpty && bEmpty) { return body; }
    if (aEmpty) { return pieceB; }
    if (bEmpty) { return pieceA; }

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
    const MIN_PTS        = 4;

    var allEdges = evaluateQuery(context, qOwnedByBody(wireBody, EntityType.EDGE));
    var n = size(allEdges);

    // --- Collect edge endpoints -------------------------------------------------
    var ePt0 = [];
    var ePt1 = [];
    for (var edge in allEdges)
    {
        ePt0 = append(ePt0, evEdgeTangentLine(context, { "edge" : edge, "parameter" : 0.0 }).origin);
        ePt1 = append(ePt1, evEdgeTangentLine(context, { "edge" : edge, "parameter" : 1.0 }).origin);
    }

    // --- Walk ALL disconnected chains ------------------------------------------
    // Each chain is an array of {idx, fwd} maps.
    var visited = {};
    var chains  = [];

    for (var seed = 0; seed < n; seed += 1)
    {
        if (visited[toString(seed)] == true) { continue; }

        // Find the actual start of this chain (the end with no predecessor).
        var chainStart = seed;
        for (var i = 0; i < n; i += 1)
        {
            if (visited[toString(i)] == true) { continue; }
            var hasPred = false;
            for (var j = 0; j < n; j += 1)
            {
                if (j == i || visited[toString(j)] == true) { continue; }
                if (norm(ePt0[i] - ePt1[j]) < CHAIN_TOL ||
                    norm(ePt0[i] - ePt0[j]) < CHAIN_TOL)
                {
                    hasPred = true;
                    break;
                }
            }
            if (!hasPred)
            {
                chainStart = i;
                break;
            }
        }

        // Walk from chainStart
        var chainIdx = [chainStart];
        var chainFwd = [true];
        visited[toString(chainStart)] = true;
        var currentPt = ePt1[chainStart];

        var keepWalking = true;
        while (keepWalking)
        {
            keepWalking = false;
            for (var j = 0; j < n; j += 1)
            {
                if (visited[toString(j)] == true) { continue; }
                if (norm(ePt0[j] - currentPt) < CHAIN_TOL)
                {
                    chainIdx   = append(chainIdx, j);
                    chainFwd   = append(chainFwd, true);
                    visited[toString(j)] = true;
                    currentPt  = ePt1[j];
                    keepWalking = true;
                    break;
                }
                else if (norm(ePt1[j] - currentPt) < CHAIN_TOL)
                {
                    chainIdx   = append(chainIdx, j);
                    chainFwd   = append(chainFwd, false);
                    visited[toString(j)] = true;
                    currentPt  = ePt0[j];
                    keepWalking = true;
                    break;
                }
            }
        }

        chains = append(chains, { "idx" : chainIdx, "fwd" : chainFwd });
    }

    // --- Sample each chain into a point array ----------------------------------
    var chainPts = [];
    for (var c = 0; c < size(chains); c += 1)
    {
        var chain = chains[c];
        var pts   = [];
        for (var i = 0; i < size(chain.idx); i += 1)
        {
            var ei  = chain.idx[i];
            var fwd = chain.fwd[i];
            var edge = allEdges[ei];
            if (norm(ePt1[ei] - ePt0[ei]) < MIN_CHORD) { continue; }
            var kStart = (size(pts) == 0) ? 0 : 1;
            for (var k = kStart; k <= RESAMPLE_COUNT; k += 1)
            {
                var t     = k / RESAMPLE_COUNT;
                var param = fwd ? t : (1.0 - t);
                pts = append(pts, evEdgeTangentLine(context, {
                        "edge" : edge, "parameter" : param
                }).origin);
            }
        }
        chainPts = append(chainPts, pts);
    }

    opDeleteBodies(context, id + "deleteWire", { "entities" : wireBody });

    // --- If more than one chain, treat each as a separate side wire ------------
    // Sort: highest mean Y first (+Y side = index 0, -Y side = index 1).
    if (size(chains) > 1)
    {
        var meanY = [];
        for (var c = 0; c < size(chainPts); c += 1)
        {
            var sum = 0 * meter;
            for (var pt in chainPts[c]) { sum = sum + pt[1]; }
            meanY = append(meanY, sum / max(size(chainPts[c]), 1));
        }
        // Insertion-sort chains by meanY descending
        var sortedPts  = [];
        var remaining  = chainPts;
        var remainingY = meanY;
        while (size(remaining) > 0)
        {
            var best = 0;
            for (var i = 1; i < size(remainingY); i += 1)
            {
                if (remainingY[i] > remainingY[best]) { best = i; }
            }
            sortedPts  = append(sortedPts, remaining[best]);
            var newRem = [];
            var newY   = [];
            for (var i = 0; i < size(remaining); i += 1)
            {
                if (i != best)
                {
                    newRem = append(newRem, remaining[i]);
                    newY   = append(newY,   remainingY[i]);
                }
            }
            remaining  = newRem;
            remainingY = newY;
        }

        var result = [];
        for (var c = 0; c < size(sortedPts); c += 1)
        {
            var pts = sortedPts[c];
            if (size(pts) < MIN_PTS) { continue; }
            var curve = approximateSpline(context, {
                    "targets"          : [approximationTarget({ "positions" : pts })],
                    "degree"           : 3,
                    "tolerance"        : 1e-5 * meter,
                    "isPeriodic"       : false,
                    "maxControlPoints" : 200
            })[0];
            opCreateBSplineCurve(context, id + ("rebuiltWire" ~ c), { "bSplineCurve" : curve });
            result = append(result, qCreatedBy(id + ("rebuiltWire" ~ c), EntityType.BODY));
        }
        return result;
    }

    // --- Single chain: return as one wire (tip wrap or single-sided section) ---
    var allPts = (size(chainPts) > 0) ? chainPts[0] : [];
    var nPts   = size(allPts);

    if (nPts < MIN_PTS) { return []; }

    var curve = approximateSpline(context, {
            "targets"          : [approximationTarget({ "positions" : allPts })],
            "degree"           : 3,
            "tolerance"        : 1e-5 * meter,
            "isPeriodic"       : false,
            "maxControlPoints" : 200
    })[0];
    opCreateBSplineCurve(context, id + "rebuiltWire0", { "bSplineCurve" : curve });
    return [qCreatedBy(id + "rebuiltWire0", EntityType.BODY)];
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
