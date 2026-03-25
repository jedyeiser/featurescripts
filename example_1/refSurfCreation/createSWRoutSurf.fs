FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");
import(path : "onshape/std/extend.fs", version : "2892.0");
import(path : "onshape/std/faceIntersection.fs", version : "2892.0");

// IMPORT: tools/printing.fs
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/b02d6a2bac551b24347c983f", version : "c104606e8ffc8e0964404bbc");

// import swRoutRegions -- SWRoutExtentType, bounds, region processing functions
export import(path : "7e3b271854475bf6cf878b2b", version : "fc5e7fa037480fd2cbb69c0a");


export const DEBUG_STEP_BOUNDS = { (unitless) : [0, 1, 10]} as IntegerBoundSpec;

export enum SWRoutContinuityType
{
    annotation { "Name" : "G0" }
    G0,
    annotation { "Name" : "G1" }
    G1
}


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
            // Use natural edge ordering -- no referenceGeometry -- to match the
            // feature body's constructPath call and keep dirSign consistent.
            var refWirePath = constructPath(context,
                    qOwnedByBody(definition.refWire, EntityType.EDGE));

            var totalLength = 0 * meter;
            for (var edge in refWirePath.edges)
            {
                totalLength += evLength(context, { "entities" : edge });
            }

            // Project refWireOrigin onto path via edge-walk.
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
            // dirSign matches the feature body: flipRefWire inverts the convention.
            var dirSign = (definition.flipRefWire == true) ? -1 : 1;

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

        annotation { "Name" : "Flip wire direction", "Default" : false,
                     "UIHint" : UIHint.OPPOSITE_DIRECTION }
        definition.flipRefWire is boolean;

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

            annotation { "Name" : "Combine curves", "Default" : false,
                         "Description" : "Fit a single BSpline through all intersection edges per wire (smooths over face boundaries)" }
            region.combineCurves is boolean;

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

            annotation { "Name" : "Join regions?", "Default" : false }
            intr.join is boolean;

            if (intr.join)
            {
                annotation { "Name" : "Start continuity", "UIHint" : UIHint.SHOW_LABEL }
                intr.startContinuity is SWRoutContinuityType;

                annotation { "Name" : "Start distance" }
                isLength(intr.startDist, LENGTH_BOUNDS);

                annotation { "Name" : "End continuity", "UIHint" : UIHint.SHOW_LABEL }
                intr.endContinuity is SWRoutContinuityType;

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

        // Build path using natural edge ordering -- same convention as
        // variableSurfaceOffset's buildFrenetPath with flipDirection=false.
        // Do NOT pass referenceGeometry: when the origin is near the centre the
        // two endpoints are equidistant and constructPath picks an arbitrary end,
        // which breaks the startX/endX sign convention.
        var refWirePath = constructPath(context,
                qOwnedByBody(definition.refWire, EntityType.EDGE));

        var dirSign = definition.flipRefWire ? -1 : 1;

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
            var wires = sortBodiesByMeanY(context, evaluateQuery(context,
                    qCreatedBy(id + ("initialWire" ~ r), EntityType.BODY)));
            for (var s = 0; s < size(wires); s += 1)
            {
                setProperty(context, {
                        "entities"     : wires[s],
                        "propertyType" : PropertyType.NAME,
                        "value"        : "Initial wire [" ~ rName ~ "] " ~ sideNames[s]
                });
            }
            if (region.combineCurves == true)
            {
                var combined0 = [];
                for (var ci = 0; ci < size(wires); ci += 1)
                {
                    combined0 = append(combined0, combineWireEdges(context,
                            id + ("combineInit" ~ r ~ "_" ~ ci), wires[ci]));
                }
                wires = combined0;
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
            var wires = sortBodiesByMeanY(context, evaluateQuery(context,
                    qCreatedBy(id + ("startIntersect" ~ r), EntityType.BODY)));
            for (var s = 0; s < size(wires); s += 1)
            {
                setProperty(context, {
                        "entities"     : wires[s],
                        "propertyType" : PropertyType.NAME,
                        "value"        : "Start wire [" ~ rName ~ "] " ~ sideNames[s]
                });
            }
            if (region.combineCurves == true)
            {
                var combined1 = [];
                for (var ci = 0; ci < size(wires); ci += 1)
                {
                    combined1 = append(combined1, combineWireEdges(context,
                            id + ("combineSt" ~ r ~ "_" ~ ci), wires[ci]));
                }
                wires = combined1;
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
                var wires = sortBodiesByMeanY(context, evaluateQuery(context,
                        qCreatedBy(id + ("stepInIntersect" ~ r), EntityType.BODY)));
                for (var s = 0; s < size(wires); s += 1)
                {
                    setProperty(context, {
                            "entities"     : wires[s],
                            "propertyType" : PropertyType.NAME,
                            "value"        : "Step-in wire [" ~ rName ~ "] " ~ sideNames[s]
                    });
                }
                if (region.combineCurves == true)
                {
                    var combined2 = [];
                    for (var ci = 0; ci < size(wires); ci += 1)
                    {
                        combined2 = append(combined2, combineWireEdges(context,
                                id + ("combineSI" ~ r ~ "_" ~ ci), wires[ci]));
                    }
                    wires = combined2;
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
            // create discontinuous geometry.  Extend only the natural free edges
            // (those running ALONG the ski path), not the region-boundary cut edges
            // (which are perpendicular to the path).  Filter by aligning the edge
            // midpoint tangent with the path tangent at the region midpoint.
            var preGapDist = evDistance(context, {
                    "side0" : sideQ,
                    "side1" : bottomQ
            }).distance;
            var preExtendDist = routSpanHeight + preGapDist + 2 * millimeter;
            var preFreeEdges  = qEdgeTopologyFilter(qOwnedByBody(sideQ, EntityType.EDGE),
                    EdgeTopology.ONE_SIDED);
            var tMid4     = (region.tStart + region.tEnd) / 2;
            var pathTang  = evPathTangentLines(context, refWirePath, [tMid4]).tangentLines[0].direction;
            var freeList  = evaluateQuery(context, preFreeEdges);
            var naturalQs = [];
            for (var e in freeList)
            {
                var eTang = evEdgeTangentLine(context, { "edge" : e, "parameter" : 0.5 }).direction;
                if (abs(dot(eTang, pathTang)) > 0.3)
                {
                    naturalQs = append(naturalQs, e);
                }
            }
            if (size(naturalQs) > 0 && preExtendDist > 0 * meter)
            {
                try
                {
                    extendSurface(context, id + ("preExtendStopSide" ~ r), {
                            "entities"           : qUnion(naturalQs),
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
            var wires = sortBodiesByMeanY(context, evaluateQuery(context,
                    qCreatedBy(id + ("stopWire" ~ r), EntityType.BODY)));
            for (var s = 0; s < size(wires); s += 1)
            {
                setProperty(context, {
                        "entities"     : wires[s],
                        "propertyType" : PropertyType.NAME,
                        "value"        : "Stop wire [" ~ rName ~ "] " ~ sideNames[s]
                });
            }
            if (region.combineCurves == true)
            {
                var combined3 = [];
                for (var ci = 0; ci < size(wires); ci += 1)
                {
                    combined3 = append(combined3, combineWireEdges(context,
                            id + ("combineStop" ~ r ~ "_" ~ ci), wires[ci]));
                }
                wires = combined3;
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

            var step6Pairs = pairWiresByMeanY(context, initWires, startWires);
            for (var p in step6Pairs)
            {
                var s            = p.a;
                var initialEdges = qUnion([qOwnedByBody(initWires[p.a],  EntityType.EDGE)]);
                var startEdges   = qUnion([qOwnedByBody(startWires[p.b], EntityType.EDGE)]);
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

                var step7Pairs = pairWiresByMeanY(context, startWires, stepInWires);
                for (var p in step7Pairs)
                {
                    var s          = p.a;
                    var startEdges  = qUnion([qOwnedByBody(startWires[p.a],  EntityType.EDGE)]);
                    var stepInEdges = qUnion([qOwnedByBody(stepInWires[p.b], EntityType.EDGE)]);
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

            var step8Pairs = pairWiresByMeanY(context, lowerWires, stopWires);
            for (var p in step8Pairs)
            {
                var s           = p.a;
                var lowerEdges  = qUnion([qOwnedByBody(lowerWires[p.a], EntityType.EDGE)]);
                var stopEdges   = qUnion([qOwnedByBody(stopWires[p.b],  EntityType.EDGE)]);
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

        // =====================================================================
        // Step 9.5: join / blend adjacent region surfaces at their shared boundary
        //   For each intersection entry, either merge the surfaces (G0) or trim
        //   each region back and bridge the gap with a continuity loft (G1).
        // =====================================================================
        // regionMergedBody[toString(rIdx)] tracks the single merged body produced
        // when a region's surfaces were unioned with an adjacent region.  Subsequent
        // intersections must use this updated query instead of the original
        // regionFinalSurfs entries, which become stale once consumed by a union.
        var regionMergedBody = {};

        for (var ix = 0; ix < size(definition.swRoutIntersections); ix += 1)
        {
            var intr  = definition.swRoutIntersections[ix];
            var rAIdx = intr.intersectionNum;
            var rBIdx = intr.intersectionNum + 1;
            if (rAIdx < 0 || rBIdx >= nRegions) { continue; }

            var regA = sortedRegions[rAIdx];
            var regB = sortedRegions[rBIdx];

            // Boundary plane at end of region A (== start of region B)
            var boundaryPlane = createRegionBoundingPlane(context, refWirePath, regA.tEnd);
            var pathTang = evPathTangentLines(context, refWirePath,
                    [regA.tEnd]).tangentLines[0].direction;

            // Collect bodies for each region.  If a prior intersection already
            // merged this region into a single body, use that merged body directly
            // to avoid stale queries from consumed originals.
            var bodiesA = [];
            var bodiesB = [];
            var priorA  = regionMergedBody[toString(rAIdx)];
            var priorB  = regionMergedBody[toString(rBIdx)];
            if (priorA != undefined)
            {
                bodiesA = [priorA];
            }
            else
            {
                for (var s = 0; s < size(sideNames); s += 1)
                {
                    var ba = regionFinalSurfs[toString(rAIdx) ~ "_" ~ toString(s)];
                    if (ba != undefined) { bodiesA = append(bodiesA, ba); }
                }
            }
            if (priorB != undefined)
            {
                bodiesB = [priorB];
            }
            else
            {
                for (var s = 0; s < size(sideNames); s += 1)
                {
                    var bb = regionFinalSurfs[toString(rBIdx) ~ "_" ~ toString(s)];
                    if (bb != undefined) { bodiesB = append(bodiesB, bb); }
                }
            }
            if (size(bodiesA) == 0 || size(bodiesB) == 0) { continue; }

            if (!intr.join)
            {
                // Direct boolean union -- no transition geometry.
                const G0_TOL = 1e-3 * meter;

                var allBodies = bodiesA;
                for (var bb in bodiesB) { allBodies = append(allBodies, bb); }

                try
                {
                    opBoolean(context, id + ("joinG0" ~ ix), {
                            "tools"         : qUnion(allBodies),
                            "operationType" : BooleanOperationType.UNION
                    });
                }
                catch {}
                // opBoolean UNION on surfaces may merge into an existing body rather
                // than creating a new one, so qCreatedBy may return empty.  Check
                // both: prefer the created body, fall back to whichever original
                // body survived (the union target that was extended in-place).
                var g0Result = qCreatedBy(id + ("joinG0" ~ ix), EntityType.BODY);
                if (isQueryEmpty(context, g0Result))
                {
                    for (var b in allBodies)
                    {
                        if (!isQueryEmpty(context, b)) { g0Result = b; break; }
                    }
                }
                g0Result = tryFillBoundaryGaps(context,
                        id + ("g0Gap" ~ ix), g0Result, boundaryPlane, G0_TOL);
                if (!isQueryEmpty(context, g0Result))
                {
                    regionMergedBody[toString(rAIdx)] = g0Result;
                    regionMergedBody[toString(rBIdx)] = g0Result;
                }
            }
            else
            {
                // Join -- trim each region back and bridge the gap with lofts (G0 or G1).
                const BLEND_TOL = 1e-3 * meter;

                var trimPlA = plane(
                        boundaryPlane.origin - intr.startDist * pathTang,
                        boundaryPlane.normal);
                var trimPlB = plane(
                        boundaryPlane.origin + intr.endDist  * pathTang,
                        boundaryPlane.normal);

                var probeA = evPathTangentLines(context, refWirePath,
                        [(regA.tStart + regA.tEnd) / 2]).tangentLines[0].origin;
                var probeB = evPathTangentLines(context, refWirePath,
                        [(regB.tStart + regB.tEnd) / 2]).tangentLines[0].origin;

                // Trim all A bodies, collect cap edges and their single adjacent face.
                // Each cap edge is naked (1 adjacent face) so the G1 reference is
                // unambiguous -- we collect it here alongside the edge.
                var trimmedA  = [];
                var capEdgesA = [];
                var capFacesA = [];
                for (var ai = 0; ai < size(bodiesA); ai += 1)
                {
                    var ba = bodiesA[ai];
                    try { ba = splitAndKeep(context, id + ("blTrimA" ~ ix ~ "_" ~ ai), ba, trimPlA, probeA); }
                    catch {}
                    trimmedA = append(trimmedA, ba);
                    var ces = evaluateQuery(context, edgesNearPlane(context, ba, trimPlA, BLEND_TOL));
                    for (var ce in ces)
                    {
                        capEdgesA = append(capEdgesA, ce);
                        var adjF = evaluateQuery(context,
                                qAdjacent(ce, AdjacencyType.EDGE, EntityType.FACE));
                        if (size(adjF) == 1) { capFacesA = append(capFacesA, adjF[0]); }
                    }
                }

                // Trim all B bodies, collect cap edges and their single adjacent face.
                var trimmedB  = [];
                var capEdgesB = [];
                var capFacesB = [];
                for (var bi = 0; bi < size(bodiesB); bi += 1)
                {
                    var bb = bodiesB[bi];
                    try { bb = splitAndKeep(context, id + ("blTrimB" ~ ix ~ "_" ~ bi), bb, trimPlB, probeB); }
                    catch {}
                    trimmedB = append(trimmedB, bb);
                    var ces = evaluateQuery(context, edgesNearPlane(context, bb, trimPlB, BLEND_TOL));
                    for (var ce in ces)
                    {
                        capEdgesB = append(capEdgesB, ce);
                        var adjF = evaluateQuery(context,
                                qAdjacent(ce, AdjacencyType.EDGE, EntityType.FACE));
                        if (size(adjF) == 1) { capFacesB = append(capFacesB, adjF[0]); }
                    }
                }

                // Bridge the gap with opBoundarySurface.
                // uProfileSubqueries[0] = all A-side cap edges (at trimPlA),
                // uProfileSubqueries[1] = all B-side cap edges (at trimPlB).
                // uDerivativeInfo provides G1 tangency via the adjacent faces
                // collected above -- each cap edge has exactly one adjacent face
                // so the reference is unambiguous.
                // Fall back to per-edge opLoft if the boundary surface fails.
                var blendSurfs   = [];
                var boundSuccess = false;

                if (size(capEdgesA) > 0 && size(capEdgesB) > 0)
                {
                    var uDerivInfo = [];
                    if (intr.startContinuity == SWRoutContinuityType.G1 &&
                            size(capFacesA) > 0)
                    {
                        uDerivInfo = append(uDerivInfo, {
                                "profileIndex"  : 0,
                                "magnitude"     : 1.0,
                                "adjacentFaces" : qUnion(capFacesA)
                        });
                    }
                    if (intr.endContinuity == SWRoutContinuityType.G1 &&
                            size(capFacesB) > 0)
                    {
                        uDerivInfo = append(uDerivInfo, {
                                "profileIndex"  : 1,
                                "magnitude"     : 1.0,
                                "adjacentFaces" : qUnion(capFacesB)
                        });
                    }

                    var boundDef = {
                            "uProfileSubqueries" : [qUnion(capEdgesA), qUnion(capEdgesB)]
                    };
                    if (size(uDerivInfo) > 0) { boundDef.uDerivativeInfo = uDerivInfo; }

                    var boundId = id + ("blBound" ~ ix);
                    try
                    {
                        opBoundarySurface(context, boundId, boundDef);
                        var boundBody = qCreatedBy(boundId, EntityType.BODY);
                        if (!isQueryEmpty(context, boundBody))
                        {
                            blendSurfs   = append(blendSurfs, boundBody);
                            boundSuccess = true;
                        }
                    }
                    catch {}
                }

                if (!boundSuccess)
                {
                    // Fall back: iterate the larger cap-edge set and find the nearest
                    // match in the smaller set via qClosestTo.
                    var iterEdges   = capEdgesA;
                    var matchEdgesQ = qUnion(capEdgesB);
                    var iterIsA     = true;
                    if (size(capEdgesB) > size(capEdgesA))
                    {
                        iterEdges   = capEdgesB;
                        matchEdgesQ = qUnion(capEdgesA);
                        iterIsA     = false;
                    }

                    for (var i = 0; i < size(iterEdges); i += 1)
                    {
                        var iterEdge  = iterEdges[i];
                        var midPt     = evEdgeTangentLine(context,
                                { "edge" : iterEdge, "parameter" : 0.5 }).origin;
                        var matchEdge = qClosestTo(matchEdgesQ, midPt);

                        var edgeA = iterIsA ? iterEdge : matchEdge;
                        var edgeB = iterIsA ? matchEdge : iterEdge;

                        // Two distinct ids: a failed opLoft still registers its id
                        // in context, so the G0 retry needs a fresh id.
                        var blId   = id + ("blLoft"   ~ ix ~ "_" ~ i);
                        var blIdG0 = id + ("blLoftG0" ~ ix ~ "_" ~ i);

                        // opLoft reads "derivativeInfo" for G1 tangency.
                        var derivInfo = [];
                        if (intr.startContinuity == SWRoutContinuityType.G1)
                        {
                            derivInfo = append(derivInfo, {
                                    "profileIndex"             : 0,
                                    "magnitude"                : 1.0,
                                    "matchCurvature"           : false,
                                    "adjacentFaces"            : qAdjacent(edgeA,
                                            AdjacencyType.EDGE, EntityType.FACE),
                                    "userDefinedAdjacentFaces" : true
                            });
                        }
                        if (intr.endContinuity == SWRoutContinuityType.G1)
                        {
                            derivInfo = append(derivInfo, {
                                    "profileIndex"             : 1,
                                    "magnitude"                : 1.0,
                                    "matchCurvature"           : false,
                                    "adjacentFaces"            : qAdjacent(edgeB,
                                            AdjacencyType.EDGE, EntityType.FACE),
                                    "userDefinedAdjacentFaces" : true
                            });
                        }
                        var loftDef = {
                                "profileSubqueries" : [edgeA, edgeB],
                                "bodyType"          : ToolBodyType.SURFACE
                        };
                        if (size(derivInfo) > 0) { loftDef.derivativeInfo = derivInfo; }

                        var lSuccess = false;
                        try { opLoft(context, blId, loftDef); lSuccess = true; }
                        catch {}
                        if (!lSuccess)
                        {
                            try
                            {
                                opLoft(context, blIdG0, {
                                        "profileSubqueries" : [edgeA, edgeB],
                                        "bodyType"          : ToolBodyType.SURFACE
                                });
                            }
                            catch {}
                        }

                        var blBody = lSuccess
                                ? qCreatedBy(blId,   EntityType.BODY)
                                : qCreatedBy(blIdG0, EntityType.BODY);
                        if (!isQueryEmpty(context, blBody))
                        {
                            blendSurfs = append(blendSurfs, blBody);
                        }
                    }
                }

                // Union all trimmed A, trimmed B, and blend loft bodies.
                var toUnion = blendSurfs;
                for (var ba in trimmedA) { toUnion = append(toUnion, ba); }
                for (var bb in trimmedB) { toUnion = append(toUnion, bb); }
                if (size(toUnion) > 1)
                {
                    try
                    {
                        opBoolean(context, id + ("blendUnion" ~ ix), {
                                "tools"         : qUnion(toUnion),
                                "operationType" : BooleanOperationType.UNION
                        });
                    }
                    catch {}
                    var blendResult = qCreatedBy(id + ("blendUnion" ~ ix), EntityType.BODY);
                    if (isQueryEmpty(context, blendResult))
                    {
                        for (var b in toUnion)
                        {
                            if (!isQueryEmpty(context, b)) { blendResult = b; break; }
                        }
                    }
                    blendResult = tryFillBoundaryGaps(context,
                            id + ("blGap" ~ ix), blendResult, boundaryPlane, BLEND_TOL);
                    if (!isQueryEmpty(context, blendResult))
                    {
                        regionMergedBody[toString(rAIdx)] = blendResult;
                        regionMergedBody[toString(rBIdx)] = blendResult;
                    }
                }
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


// Returns the mean Y coordinate of all edge midpoints of a wire body.
function wireMeanY(context is Context, wire is Query) returns ValueWithUnits
{
    var edges = evaluateQuery(context, qOwnedByBody(wire, EntityType.EDGE));
    if (size(edges) == 0) { return 0 * meter; }
    var sum = 0 * meter;
    for (var e in edges)
    {
        sum += evEdgeTangentLine(context, { "edge" : e, "parameter" : 0.5 }).origin[1];
    }
    return sum / size(edges);
}


/**
 * Pairs each wire in wiresA with the closest unmatched wire in wiresB by
 * mean Y.  Returns an array of {"a":i, "b":j} index maps.
 * Wires in wiresA that have no unmatched counterpart in wiresB are skipped.
 * When counts match this is equivalent to index pairing (both arrays are
 * sorted highest-Y-first by sortBodiesByMeanY); when counts differ it gracefully
 * handles the case where one arm of a tip U-shape disappears after an offset.
 */
function pairWiresByMeanY(context is Context, wiresA is array, wiresB is array) returns array
{
    var yA = [];
    for (var w in wiresA) { yA = append(yA, wireMeanY(context, w)); }
    var yB = [];
    for (var w in wiresB) { yB = append(yB, wireMeanY(context, w)); }

    var pairs = [];
    var usedB = {};
    for (var i = 0; i < size(yA); i += 1)
    {
        var bestJ    = -1;
        var bestSq   = undefined;
        for (var j = 0; j < size(yB); j += 1)
        {
            if (usedB[toString(j)] == true) { continue; }
            var d  = yA[i] - yB[j];
            var sq = d * d;
            if (bestSq == undefined || sq < bestSq)
            {
                bestSq = sq;
                bestJ  = j;
            }
        }
        if (bestJ >= 0)
        {
            usedB[toString(bestJ)] = true;
            pairs = append(pairs, { "a" : i, "b" : bestJ });
        }
    }
    return pairs;
}


// Sorts an array of wire body queries by mean Y descending (+Y first).
function sortBodiesByMeanY(context is Context, bodies is array) returns array
{
    var sorted    = [];
    var remaining = bodies;
    while (size(remaining) > 0)
    {
        var best     = 0;
        var bestMean = wireMeanY(context, remaining[0]);
        for (var i = 1; i < size(remaining); i += 1)
        {
            var m = wireMeanY(context, remaining[i]);
            if (m > bestMean) { bestMean = m; best = i; }
        }
        sorted = append(sorted, remaining[best]);
        var newRem = [];
        for (var i = 0; i < size(remaining); i += 1)
        {
            if (i != best) { newRem = append(newRem, remaining[i]); }
        }
        remaining = newRem;
    }
    return sorted;
}


// Combines all edges of a wire body into a single approximated BSpline.
// Uses constructPath to order the edges and evPathTangentLines for uniform
// arc-length sampling.  Returns the new single-edge wire body; the original
// wireBody is deleted.  Falls back to returning the original on any failure.
function combineWireEdges(context is Context, id is Id, wireBody is Query) returns Query
{
    try
    {
        var pl = constructPath(context, qOwnedByBody(wireBody, EntityType.EDGE));
        const N = 60;
        var pts = [];
        for (var k = 0; k <= N; k += 1)
        {
            pts = append(pts, evPathTangentLines(context, pl, [k / N]).tangentLines[0].origin);
        }
        var curve = approximateSpline(context, {
                "targets"          : [approximationTarget({ "positions" : pts })],
                "degree"           : 3,
                "tolerance"        : 1e-5 * meter,
                "isPeriodic"       : false,
                "maxControlPoints" : 200
        })[0];
        opDeleteBodies(context, id + "Del", { "entities" : wireBody });
        opCreateBSplineCurve(context, id + "Crv", { "bSplineCurve" : curve });
        return qCreatedBy(id + "Crv", EntityType.BODY);
    }
    catch
    {
        return wireBody;
    }
}


// Returns a query for all edges of surfBody whose midpoints are within tol of pl.
// After joining two region surfaces, finds any open (single-face) boundary edges
// on mergedBody that are near the junction plane and fills them with opFill.
// This closes gaps left by step-in faces on one region that the adjacent region
// lacks (e.g. RSL has a step riser; Tip/Tail do not).  Returns the final body
// query, which may be updated if a fill+union succeeded.
function tryFillBoundaryGaps(context is Context, id is Id, mergedBody is Query,
        pl is Plane, tol is ValueWithUnits) returns Query
{
    if (isQueryEmpty(context, mergedBody)) { return mergedBody; }

    var candidates = evaluateQuery(context, edgesNearPlane(context, mergedBody, pl, tol));
    var gapEdges   = [];
    for (var e in candidates)
    {
        // An edge adjacent to exactly one face is an open (naked) boundary edge.
        var adjFaces = evaluateQuery(context,
                qAdjacent(e, AdjacencyType.EDGE, EntityType.FACE));
        if (size(adjFaces) == 1) { gapEdges = append(gapEdges, e); }
    }
    if (size(gapEdges) == 0) { return mergedBody; }

    // Each naked gap edge has exactly one adjacent face, so G1 tangency reference
    // is unambiguous.  Use edgesG1 for a smoother closure than edgesG0.
    // opFillSurface requires a closed loop; silently skip if not.
    try
    {
        opFillSurface(context, id + "fill", { "edgesG1" : qUnion(gapEdges) });
        var fillBody = qCreatedBy(id + "fill", EntityType.BODY);
        if (!isQueryEmpty(context, fillBody))
        {
            try
            {
                opBoolean(context, id + "fillUnion", {
                        "tools"         : qUnion([mergedBody, fillBody]),
                        "operationType" : BooleanOperationType.UNION
                });
            }
            catch {}
            var result = qCreatedBy(id + "fillUnion", EntityType.BODY);
            if (isQueryEmpty(context, result))
            {
                for (var b in [mergedBody, fillBody])
                {
                    if (!isQueryEmpty(context, b)) { result = b; break; }
                }
            }
            if (!isQueryEmpty(context, result)) { return result; }
        }
    }
    catch {}
    return mergedBody;
}


function edgesNearPlane(context is Context, surfBody is Query, pl is Plane,
        tol is ValueWithUnits) returns Query
{
    var allEdges = evaluateQuery(context, qOwnedByBody(surfBody, EntityType.EDGE));
    var matching = [];
    for (var e in allEdges)
    {
        var mid = evEdgeTangentLine(context, { "edge" : e, "parameter" : 0.5 }).origin;
        if (abs(dot(mid - pl.origin, pl.normal)) < tol)
        {
            matching = append(matching, e);
        }
    }
    if (size(matching) == 0) { return qNothing(); }
    return qUnion(matching);
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
