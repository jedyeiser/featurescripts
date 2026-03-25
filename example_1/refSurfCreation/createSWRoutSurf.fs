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
        var sortedRegions = definition.swRoutRegions;  // fallback: unsorted
        try silent
        {
            var refWirePath = constructPath(context,
                    qOwnedByBody(definition.refWire, EntityType.EDGE));
            var dirSign = (definition.flipRefWire == true) ? -1 : 1;
            sortedRegions = processSwRoutRegions(context, id + "elSort",
                    definition, refWirePath, definition.refWireOrigin, dirSign);
            sortedRegions = sort(sortedRegions, function(a, b)
            {
                return ((a.tStart + a.tEnd) / 2) - ((b.tStart + b.tEnd) / 2);
            });
        }

        var newRegions = [];
        for (var i = 0; i < size(sortedRegions); i += 1)
        {
            var reg = sortedRegions[i];
            reg.regionNum = i;
            var nm = reg.name;
            reg.needsDefaultName = (nm == undefined || nm == "" || startsWith(nm, "Region "));
            if (reg.needsDefaultName) { reg.name = "Region " ~ i; }
            newRegions = append(newRegions, reg);
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
        // Steps 0-5: per region (combined loop)
        //   Step 0: copy and trim reference surfaces, compute signs
        //   Step 1: initial wire
        //   Step 2: offset bottom copy, start wire
        //   Step 3: offset side copy (if stepIn > 0), step-in wire
        //   Step 4: offset copies to stop position
        //   Step 5: extend stop side if gap, stop wire
        // =====================================================================
        for (var r = 0; r < nRegions; r += 1)
        {
            var region = sortedRegions[r];
            var rName  = region.name;
            var rKey   = toString(r);

            // --- Step 0: copy and trim reference surfaces, compute signs ---
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

            setBodyName(context, bottomQ, "Bottom copy [" ~ rName ~ "]");
            setBodyName(context, sideQ, "Side copy [" ~ rName ~ "]");

            regionBottomCopyQ[rKey] = bottomQ;
            regionSideCopyQ[rKey]   = sideQ;

            // --- Step 1: initial wire ---
            washedInitialWires[rKey] = intersectAndGetWires(context, id + ("initialWire" ~ r),
                    bottomQ, sideQ, "Initial wire [" ~ rName ~ "]", sideNames,
                    region.combineCurves, "combineInit" ~ r ~ "_");

            // --- Step 2: offset bottom copy, start wire ---
            var bSign = regionBottomSign[rKey];
            var sSign = regionSideSign[rKey];

            opOffsetFace(context, id + ("startBottomOffset" ~ r), {
                    "moveFaces"      : qUnion([qOwnedByBody(bottomQ, EntityType.FACE)]),
                    "offsetDistance" : bSign * region.distAboveBottom
            });
            setBodyName(context, bottomQ, "Bottom (offset) [" ~ rName ~ "]");

            washedStartWires[rKey] = intersectAndGetWires(context, id + ("startIntersect" ~ r),
                    sideQ, bottomQ, "Start wire [" ~ rName ~ "]", sideNames,
                    region.combineCurves, "combineSt" ~ r ~ "_");

            if (definition.debugPrint)
            {
                var startWiresDbg = washedStartWires[rKey];
                for (var s = 0; s < size(startWiresDbg); s += 1)
                {
                    debugPrintWireBSplines(context, startWiresDbg[s],
                            "Start wire [" ~ rName ~ "] " ~ sideNames[s], debugFmt);
                }
            }

            // --- Step 3: step-in wire (if applicable) ---
            if (region.swRoutStepin > 0 * millimeter)
            {
                opOffsetFace(context, id + ("stepInSideOffset" ~ r), {
                        "moveFaces"      : qUnion([qOwnedByBody(sideQ, EntityType.FACE)]),
                        "offsetDistance" : -sSign * region.swRoutStepin
                });
                setBodyName(context, sideQ, "Side (offset) [" ~ rName ~ "]");

                washedStepInWires[rKey] = intersectAndGetWires(context, id + ("stepInIntersect" ~ r),
                        sideQ, regionBottomCopyQ[rKey], "Step-in wire [" ~ rName ~ "]", sideNames,
                        region.combineCurves, "combineSI" ~ r ~ "_");

                if (definition.debugPrint)
                {
                    var siWiresDbg = washedStepInWires[rKey];
                    for (var s = 0; s < size(siWiresDbg); s += 1)
                    {
                        debugPrintWireBSplines(context, siWiresDbg[s],
                                "Step-in wire [" ~ rName ~ "] " ~ sideNames[s], debugFmt);
                    }
                }
            }

            // --- Step 4: offset copies to stop position ---
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
            setBodyName(context, bottomQ, "Stop bottom [" ~ rName ~ "]");

            opOffsetFace(context, id + ("stopSideOffset" ~ r), {
                    "moveFaces"      : qUnion([qOwnedByBody(sideQ, EntityType.FACE)]),
                    "offsetDistance" : -sSign * routSpanSide
            });
            setBodyName(context, sideQ, "Stop side [" ~ rName ~ "]");

            // --- Step 5: extend stop side if gap, stop wire ---
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

            washedStopWires[rKey] = intersectAndGetWires(context, id + ("stopWire" ~ r),
                    sideQ, bottomQ, "Stop wire [" ~ rName ~ "]", sideNames,
                    region.combineCurves, "combineStop" ~ r ~ "_");

            if (definition.debugPrint)
            {
                var stopWiresDbg = washedStopWires[rKey];
                for (var s = 0; s < size(stopWiresDbg); s += 1)
                {
                    debugPrintWireBSplines(context, stopWiresDbg[s],
                            "Stop wire [" ~ rName ~ "] " ~ sideNames[s], debugFmt);
                }
            }
        }

        if (stepThrough && step <= 5) { return; }

        // =====================================================================
        // Step 6: per region -- loft initial wire -> start wire
        // =====================================================================
        for (var r = 0; r < nRegions; r += 1)
        {
            var region = sortedRegions[r];
            var rKey   = toString(r);
            regionSurfBodies = loftWireStep(context, id,
                    washedInitialWires[rKey], washedStartWires[rKey],
                    "initStartLoft", r, rKey, region.name, sideNames,
                    "Initial to Start [", regionSurfBodies);
        }

        if (stepThrough && step == 6) { return; }

        // =====================================================================
        // Step 7: per region -- loft start -> step-in wire (if stepIn > 0)
        // =====================================================================
        for (var r = 0; r < nRegions; r += 1)
        {
            var region = sortedRegions[r];
            var rKey   = toString(r);
            if (region.swRoutStepin > 0 * millimeter)
            {
                regionSurfBodies = loftWireStep(context, id,
                        washedStartWires[rKey], washedStepInWires[rKey],
                        "startStepInLoft", r, rKey, region.name, sideNames,
                        "Start to Step-In [", regionSurfBodies);
            }
        }

        if (stepThrough && step == 7) { return; }

        // =====================================================================
        // Step 8: per region -- loft lower wire (step-in or start) -> stop wire
        // =====================================================================
        for (var r = 0; r < nRegions; r += 1)
        {
            var region     = sortedRegions[r];
            var rKey       = toString(r);
            var lowerWires = (region.swRoutStepin > 0 * millimeter) ?
                    washedStepInWires[rKey] : washedStartWires[rKey];
            regionSurfBodies = loftWireStep(context, id,
                    lowerWires, washedStopWires[rKey],
                    "lowerStopLoft", r, rKey, region.name, sideNames,
                    "Lower to Stop [", regionSurfBodies);
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

                var finalBody = bodies[0];
                if (size(bodies) > 1)
                {
                    try
                    {
                        opBoolean(context, id + ("combineFinal" ~ r ~ "_" ~ s), {
                                "tools"         : qUnion(bodies),
                                "operationType" : BooleanOperationType.UNION
                        });
                    }
                    catch {}
                    var unionResult = qCreatedBy(id + ("combineFinal" ~ r ~ "_" ~ s), EntityType.BODY);
                    if (!isQueryEmpty(context, unionResult))
                    {
                        finalBody = unionResult;
                    }
                    else
                    {
                        for (var b in bodies)
                        {
                            if (!isQueryEmpty(context, b)) { finalBody = b; break; }
                        }
                    }
                }

                setBodyName(context, finalBody, "SW Rout [" ~ rName ~ "] " ~ sideNames[s]);
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

            var bodiesA = getBodiesForRegion(rAIdx, regionMergedBody, regionFinalSurfs, sideNames);
            var bodiesB = getBodiesForRegion(rBIdx, regionMergedBody, regionFinalSurfs, sideNames);

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

                // Trim all A bodies; collect cap edges and their adjacent face PER BODY.
                // Keeping per-body arrays is critical: opBoundarySurface requires each
                // U-profile to be a single connected chain.  Mixing edges from separate
                // bodies (+Y and -Y) into one qUnion creates a disconnected set and
                // causes BSURF_OPEN_CHAIN.  Pairing one A body to one B body ensures
                // each profile is a connected chain through a single surface body.
                var trimmedA     = [];
                var capEdgesPerA = [];
                var capFacesPerA = [];
                for (var ai = 0; ai < size(bodiesA); ai += 1)
                {
                    var ba = bodiesA[ai];
                    try { ba = splitAndKeep(context, id + ("blTrimA" ~ ix ~ "_" ~ ai), ba, trimPlA, probeA); }
                    catch {}
                    trimmedA = append(trimmedA, ba);
                    var cesAi = [];
                    var cfAi  = [];
                    var ces = evaluateQuery(context, edgesNearPlane(context, ba, trimPlA, BLEND_TOL));
                    for (var ce in ces)
                    {
                        cesAi = append(cesAi, ce);
                        var adjF = evaluateQuery(context,
                                qAdjacent(ce, AdjacencyType.EDGE, EntityType.FACE));
                        if (size(adjF) == 1) { cfAi = append(cfAi, adjF[0]); }
                    }
                    capEdgesPerA = append(capEdgesPerA, cesAi);
                    capFacesPerA = append(capFacesPerA, cfAi);
                }

                // Trim all B bodies; same per-body collection.
                var trimmedB     = [];
                var capEdgesPerB = [];
                var capFacesPerB = [];
                for (var bi = 0; bi < size(bodiesB); bi += 1)
                {
                    var bb = bodiesB[bi];
                    try { bb = splitAndKeep(context, id + ("blTrimB" ~ ix ~ "_" ~ bi), bb, trimPlB, probeB); }
                    catch {}
                    trimmedB = append(trimmedB, bb);
                    var cesBi = [];
                    var cfBi  = [];
                    var ces = evaluateQuery(context, edgesNearPlane(context, bb, trimPlB, BLEND_TOL));
                    for (var ce in ces)
                    {
                        cesBi = append(cesBi, ce);
                        var adjF = evaluateQuery(context,
                                qAdjacent(ce, AdjacencyType.EDGE, EntityType.FACE));
                        if (size(adjF) == 1) { cfBi = append(cfBi, adjF[0]); }
                    }
                    capEdgesPerB = append(capEdgesPerB, cesBi);
                    capFacesPerB = append(capFacesPerB, cfBi);
                }

                // Bridge each A body to its closest B body (matched by mean-Y of cap
                // edge midpoints).  For each pair, try opBoundarySurface first (handles
                // step-riser topology differences naturally); fall back to per-edge loft.
                var blendSurfs = [];

                for (var ai = 0; ai < size(trimmedA); ai += 1)
                {
                    var edgesAi = capEdgesPerA[ai];
                    var facesAi = capFacesPerA[ai];
                    if (size(edgesAi) == 0) { continue; }

                    // Mean Y of A body's cap edge midpoints.
                    var meanYA = 0 * meter;
                    for (var e in edgesAi)
                    {
                        meanYA += evEdgeTangentLine(context,
                                { "edge" : e, "parameter" : 0.5 }).origin[1];
                    }
                    meanYA = meanYA / size(edgesAi);

                    // Find the B body whose cap edges are closest in Y.
                    var bestBi  = -1;
                    var bestDif = undefined;
                    for (var bi = 0; bi < size(trimmedB); bi += 1)
                    {
                        var edgesBi = capEdgesPerB[bi];
                        if (size(edgesBi) == 0) { continue; }
                        var meanYB = 0 * meter;
                        for (var e in edgesBi)
                        {
                            meanYB += evEdgeTangentLine(context,
                                    { "edge" : e, "parameter" : 0.5 }).origin[1];
                        }
                        meanYB = meanYB / size(edgesBi);
                        var dif = abs(meanYA - meanYB);
                        if (bestDif == undefined || dif < bestDif)
                        {
                            bestDif = dif;
                            bestBi  = bi;
                        }
                    }
                    if (bestBi < 0) { continue; }

                    var edgesBj = capEdgesPerB[bestBi];
                    var facesBj = capFacesPerB[bestBi];

                    // Build G1 derivative info for this A-B pair.
                    var uDerivPair = [];
                    if (intr.startContinuity == SWRoutContinuityType.G1 &&
                            size(facesAi) > 0)
                    {
                        uDerivPair = append(uDerivPair, {
                                "profileIndex"  : 0,
                                "magnitude"     : 1.0,
                                "adjacentFaces" : qUnion(facesAi)
                        });
                    }
                    if (intr.endContinuity == SWRoutContinuityType.G1 &&
                            size(facesBj) > 0)
                    {
                        uDerivPair = append(uDerivPair, {
                                "profileIndex"  : 1,
                                "magnitude"     : 1.0,
                                "adjacentFaces" : qUnion(facesBj)
                        });
                    }

                    // Try opBoundarySurface for this body pair.
                    var pairBoundDef = {
                            "uProfileSubqueries" : [qUnion(edgesAi), qUnion(edgesBj)]
                    };
                    if (size(uDerivPair) > 0) { pairBoundDef.uDerivativeInfo = uDerivPair; }

                    var pairBoundId  = id + ("blBound" ~ ix ~ "_" ~ ai);
                    var pairSuccess  = false;
                    try
                    {
                        opBoundarySurface(context, pairBoundId, pairBoundDef);
                        var pairBody = qCreatedBy(pairBoundId, EntityType.BODY);
                        if (!isQueryEmpty(context, pairBody))
                        {
                            blendSurfs  = append(blendSurfs, pairBody);
                            pairSuccess = true;
                        }
                    }
                    catch {}

                    if (!pairSuccess)
                    {
                        // Per-pair loft fallback.  Iterate the larger edge set; match
                        // each edge to the nearest in the smaller set.  Loft failures
                        // (e.g. incompatible step-riser edges) are silently skipped;
                        // any remaining open edges are caught by tryFillBoundaryGaps.
                        var iterEdges   = edgesAi;
                        var matchEdgesQ = qUnion(edgesBj);
                        var iterIsA     = true;
                        if (size(edgesBj) > size(edgesAi))
                        {
                            iterEdges   = edgesBj;
                            matchEdgesQ = qUnion(edgesAi);
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

                            var blId   = id + ("blLoft"   ~ ix ~ "_" ~ ai ~ "_" ~ i);
                            var blIdG0 = id + ("blLoftG0" ~ ix ~ "_" ~ ai ~ "_" ~ i);

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
                }

                // Union all trimmed A, trimmed B, and blend surfaces.
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
                    // Fill any remaining open edges at each trim plane and at the
                    // boundary.  Step-riser cap edges that had no loft partner survive
                    // the union as naked edges; tryFillBoundaryGaps closes them.
                    blendResult = tryFillBoundaryGaps(context,
                            id + ("blGapA" ~ ix), blendResult, trimPlA, BLEND_TOL);
                    blendResult = tryFillBoundaryGaps(context,
                            id + ("blGapB" ~ ix), blendResult, trimPlB, BLEND_TOL);
                    blendResult = tryFillBoundaryGaps(context,
                            id + ("blGap"  ~ ix), blendResult, boundaryPlane, BLEND_TOL);
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

    setBodyName(context, wireBodies[topIdx], "Side top boundary wire (raw)");
    setBodyName(context, wireBodies[bottomIdx], "Side bottom boundary wire");

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


// Sets the Name property of a body in one line.
function setBodyName(context is Context, body, name is string)
{
    setProperty(context, {
            "entities"     : body,
            "propertyType" : PropertyType.NAME,
            "value"        : name
    });
}


// Intersects two surface bodies, sorts the result wires by mean Y (highest first),
// optionally fits each wire to a single BSpline curve (combineCurves), names each
// wire, and returns the final wire array.
function intersectAndGetWires(context is Context, id is Id,
        surf1 is Query, surf2 is Query,
        label is string, sideNames is array,
        combineCurves is boolean, combinePrefix is string) returns array
{
    intersectionCurve(context, id, { "group1" : surf1, "group2" : surf2 });
    var wires = sortBodiesByMeanY(context,
            evaluateQuery(context, qCreatedBy(id, EntityType.BODY)));
    for (var s = 0; s < size(wires); s += 1)
    {
        setBodyName(context, wires[s], label ~ " " ~ sideNames[s]);
    }
    if (combineCurves)
    {
        var combined = [];
        for (var ci = 0; ci < size(wires); ci += 1)
        {
            combined = append(combined,
                    combineWireEdges(context, id + (combinePrefix ~ ci), wires[ci]));
        }
        wires = combined;
    }
    return wires;
}


// Lofts paired wire edges from wiresA to wiresB, accumulates each loft body into
// regionSurfBodies[rsKey] where rsKey = rKey ~ "_" ~ toString(s).
// idPrefix, rKey, and iteration index i are combined to form each opLoft id.
function loftWireStep(context is Context, id is Id,
        wiresA is array, wiresB is array,
        idPrefix is string, r, rKey is string,
        rName is string, sideNames is array,
        nameLabel is string,
        regionSurfBodies is map) returns map
{
    var pairs = pairWiresByMeanY(context, wiresA, wiresB);
    for (var p in pairs)
    {
        var s          = p.a;
        var aEdges     = qUnion([qOwnedByBody(wiresA[p.a], EntityType.EDGE)]);
        var bEdgesQ    = qUnion([qOwnedByBody(wiresB[p.b], EntityType.EDGE)]);
        var iterEdges  = evaluateQuery(context, aEdges);
        var loftedSurfs = [];

        for (var i = 0; i < size(iterEdges); i += 1)
        {
            var aEdge = iterEdges[i];
            var midPt = evEdgeTangentLine(context, { "edge" : aEdge, "parameter" : 0.5 }).origin;
            var bEdge = qClosestTo(bEdgesQ, midPt);
            var lId   = id + (idPrefix ~ r ~ "_" ~ s ~ "_" ~ i);
            try
            {
                opLoft(context, lId, {
                        "profileSubqueries" : [aEdge, bEdge],
                        "bodyType"          : ToolBodyType.SURFACE
                });
                loftedSurfs = append(loftedSurfs, qCreatedBy(lId, EntityType.BODY));
            }
            catch
            {
                addDebugEntities(context, bEdge,  DebugColor.RED);
                addDebugEntities(context, aEdge, DebugColor.GREEN);
            }
        }

        if (size(loftedSurfs) > 1)
        {
            var boolId = id + ("combine" ~ idPrefix ~ r ~ "_" ~ s);
            try
            {
                opBoolean(context, boolId, {
                        "tools"         : qUnion(loftedSurfs),
                        "operationType" : BooleanOperationType.UNION
                });
            }
            catch {}
            var boolResult = qCreatedBy(boolId, EntityType.BODY);
            if (!isQueryEmpty(context, boolResult)) { loftedSurfs[0] = boolResult; }
        }

        if (size(loftedSurfs) > 0)
        {
            setBodyName(context, loftedSurfs[0], nameLabel ~ rName ~ "] " ~ sideNames[s]);
            var rsKey = rKey ~ "_" ~ toString(s);
            var prev  = (regionSurfBodies[rsKey] != undefined) ? regionSurfBodies[rsKey] : [];
            regionSurfBodies[rsKey] = append(prev, loftedSurfs[0]);
        }
    }
    return regionSurfBodies;
}


// Returns the bodies to use for a region in step 9.5.
// If a prior intersection already merged this region, use that body.
// Otherwise collect from regionFinalSurfs (one per side).
function getBodiesForRegion(rIdx is number, regionMergedBody is map,
        regionFinalSurfs is map, sideNames is array) returns array
{
    var prior = regionMergedBody[toString(rIdx)];
    if (prior != undefined) { return [prior]; }
    var bodies = [];
    for (var s = 0; s < size(sideNames); s += 1)
    {
        var b = regionFinalSurfs[toString(rIdx) ~ "_" ~ toString(s)];
        if (b != undefined) { bodies = append(bodies, b); }
    }
    return bodies;
}
