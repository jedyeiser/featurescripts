FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");
import(path : "onshape/std/extend.fs", version : "2892.0");
import(path : "onshape/std/faceIntersection.fs", version : "2892.0");
import(path : "onshape/std/loft.fs", version : "2892.0");

// IMPORT: tools/printing.fs
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/b02d6a2bac551b24347c983f", version : "c104606e8ffc8e0964404bbc");

// import swRoutRegions -- SWRoutExtentType, bounds, region processing functions
export import(path : "7e3b271854475bf6cf878b2b", version : "7a7ace076afa389075c27fee");


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

        annotation { "Name" : "SW rout height" }
        isLength(definition.swRoutHeight, SWRoutHeightBounds);

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
                
                annotation { "Name" : "End continuity", "UIHint" : UIHint.SHOW_LABEL }
                intr.endContinuity is SWRoutContinuityType;

                annotation { "Name" : "Start distance" }
                isLength(intr.startDist, LENGTH_BOUNDS);

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
        var step = definition.debugStep;
        var sideNames = ["+Y", "-Y"];
        var debugFmt = definition.debugDetailedBSplines ? PrintFormat.DETAILS : PrintFormat.METADATA;

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
            var routSpanHeight = definition.swRoutHeight - region.distAboveBottom;
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
            var lowerWires = (region.swRoutStepin > 0 * millimeter) ? washedStepInWires[rKey] : washedStartWires[rKey];
            regionSurfBodies = loftWireStep(context, id,lowerWires, washedStopWires[rKey], "lowerStopLoft", r, rKey, region.name, sideNames, "Lower to Stop [", regionSurfBodies);
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
        
        var blendedBodies = [];
        
        
        for (var ix = 0; ix < size(definition.swRoutIntersections); ix += 1)
        {
            var intr  = definition.swRoutIntersections[ix];
            var rAIdx = intr.intersectionNum;
            var rBIdx = intr.intersectionNum + 1;
            if (rAIdx < 0 || rBIdx >= nRegions) { continue; }
            if (!intr.join) { continue; }

            var regA = sortedRegions[rAIdx];

            // Boundary plane at end of region A (== start of region B)
            var boundaryPlane = createRegionBoundingPlane(context, refWirePath, regA.tEnd);

            var bodiesA = getBodiesForRegion(rAIdx, regionMergedBody, regionFinalSurfs, sideNames);
            var bodiesB = getBodiesForRegion(rBIdx, regionMergedBody, regionFinalSurfs, sideNames);

            var edgesA = mapArray(bodiesA, function(x) {return qEdgeTopologyFilter(qOwnedByBody(x, EntityType.EDGE), EdgeTopology.ONE_SIDED);});
            var edgesB = mapArray(bodiesB, function(x) {return qEdgeTopologyFilter(qOwnedByBody(x, EntityType.EDGE), EdgeTopology.ONE_SIDED);});
            
            edgesA = mapArray(edgesA, function(x) {return qEdgesOnPlane(context, x, boundaryPlane, false, 3 * millimeter);});
            edgesB = mapArray(edgesB, function(x) {return qEdgesOnPlane(context, x, boundaryPlane, false, 3 * millimeter);});

            var aTrackers = mapArray(edgesA, function(x) {return startTracking(context, x);});
            var bTrackers = mapArray(edgesB, function(x) {return startTracking(context, x);});
            

            
            //addDebugEntities(context, qUnion(edgesA), DebugColor.RED);
            //addDebugEntities(context, qUnion(edgesB), DebugColor.BLUE);
            
            var extendEdgesA = qUnion(edgesA);
            if (intr.startDist > 0 * millimeter && !isQueryEmpty(context, extendEdgesA))
            {
                extendSurface(context, id + ("extendAIntersection" ~ ix), {
                    "entities"           : extendEdgesA,
                    "tangentPropagation" : true,
                    "endCondition"       : ExtendBoundingType.BLIND,
                    "oppositeDirection"  : true,
                    "extendDistance"     : intr.startDist,
                    "maintainCurvature"  : true
                });
            }

            var extendEdgesB = qUnion(edgesB);
            if (intr.endDist > 0 * millimeter && !isQueryEmpty(context, extendEdgesB))
            {
                extendSurface(context, id + ("extendBIntersection" ~ ix), {
                    "entities"           : extendEdgesB,
                    "tangentPropagation" : true,
                    "endCondition"       : ExtendBoundingType.BLIND,
                    "oppositeDirection"  : true,
                    "extendDistance"     : intr.endDist,
                    "maintainCurvature"  : true
                });
            }
            
            var aChains = chainEdges(context, aTrackers);
            var bChains = chainEdges(context, bTrackers);
            
            //addDebugEntities(context, qUnion(aTrackers), DebugColor.MAGENTA);
            //addDebugEntities(context, qUnion(bTrackers), DebugColor.CYAN);
            
            for (var ch = 0; ch < size(aChains); ch += 1)
            {
                var aChain = aChains[ch];
                
                var tempBChains = mapArray(bChains, function(x) {return mergeMaps(x, {'aChainDist' : norm(x.midPoint - aChain.midPoint)});});
                var bChain = sort(tempBChains, function(a, b) {return a.aChainDist - b.aChainDist;})[0];
                
                if (aChain.numEdges == bChain.numEdges)
                {
                    addDebugEntities(context, qUnion(aChain.edgeArray), DebugColor.MAGENTA);
                    addDebugEntities(context, qUnion(bChain.edgeArray), DebugColor.CYAN);
                    
                    loft(context, id + ("equalEdgeloft1" ~ ix ~ "chain" ~ ch), {
                        "bodyType" : ExtendedToolBodyType.SURFACE,
                        "surfaceOperationType" : NewSurfaceOperationType.NEW,
                        "wireProfilesArray" : [{'wireProfileEntities' : qUnion(aChain.edgeArray)}, {'wireProfileEntities' : qUnion(bChain.edgeArray)}],
                        "startCondition" : (intr.startContinuity == SWRoutContinuityType.G0) ? LoftEndDerivativeType.DEFAULT : LoftEndDerivativeType.MATCH_TANGENT,
                        "endCondition" : (intr.endContinuity == SWRoutContinuityType.G0) ? LoftEndDerivativeType.DEFAULT : LoftEndDerivativeType.MATCH_TANGENT,
                        "startMagnitude" : 1, 
                        "endMagnitude" : 1, 
                        "adjacentFacesStart" : qAdjacent(qUnion(aChain.edgeArray), AdjacencyType.EDGE, EntityType.FACE),
                        "adjacentFacesEnd" : qAdjacent(qUnion(bChain.edgeArray), AdjacencyType.EDGE, EntityType.FACE), 
                        "trimProfiles" : false,
                        "matchConnections" : false,
                        "showIsocurves" : false,
                        
                        });
                        
                        blendedBodies = append(blendedBodies, qCreatedBy(id + ("equalEdgeloft1" ~ ix ~ "chain" ~ ch), EntityType.BODY));
                }
                else //need to establish connections. 
                {
                    //addDebugEntities(context, qUnion(aChain.edgeArray), DebugColor.MAGENTA);
                    //addDebugEntities(context, qUnion(bChain.edgeArray), DebugColor.CYAN);
                    
                    var aGreater = (aChain.numEdges > bChain.numEdges);
                    var greaterChain = (aChain.numEdges > bChain.numEdges) ? aChain : bChain;
                    var lesserChain = (aChain.numEdges > bChain.numEdges) ? bChain : aChain;
                    
                    var orphanEdge = qNothing();
                    
                    for (var gc = 0; gc < size(greaterChain.edgeArray); gc += 1)
                    {
                        var edgeQ = greaterChain.edgeArray[gc];
                        var adjQ = evaluateQuery(context, qIntersection([qUnion(greaterChain.edgeArray), qAdjacent(edgeQ, AdjacencyType.VERTEX, EntityType.EDGE)]));
                        if (size(adjQ) == 2)
                        {
                            orphanEdge = edgeQ;
                            break;
                        }
                    }
                    
                    var orphanPoint = qIntersection([qAdjacent(lesserChain.edgeArray[0], AdjacencyType.VERTEX, EntityType.VERTEX), qAdjacent(lesserChain.edgeArray[1], AdjacencyType.VERTEX, EntityType.VERTEX)]);
                    var orphanEdgeStart = qEdgeVertex(orphanEdge, true);
                    var orphanEdgeEnd = qEdgeVertex(orphanEdge, false);
                    
                    
                    var updatedGreater = qSubtraction(qUnion(greaterChain.edgeArray), orphanEdge);
                    
                    var iterEdges = evaluateQuery(context, updatedGreater);
                    var createdEdges = [];
                    var createdBodies = [];
                    
                    for (var ie = 0; ie < size(iterEdges); ie += 1)
                    {
                        var iterEdge = iterEdges[ie];
                        var midPoint = evEdgeTangentLine(context, {
                                "edge" : iterEdge,
                                "parameter" : .5
                        }).origin;
                        var loftEdge = qClosestTo(qUnion(lesserChain.edgeArray), midPoint);
                        
                        loft(context, id + ("unequalEdgeloft1" ~ ix ~ "chain" ~ ch ~ "iterEdge" ~ ie), {
                        "bodyType" : ExtendedToolBodyType.SURFACE,
                        "surfaceOperationType" : NewSurfaceOperationType.NEW,
                        "wireProfilesArray" : [{'wireProfileEntities' : (aGreater) ? qUnion([iterEdge]) : qUnion([loftEdge])}, {'wireProfileEntities' : (aGreater) ? qUnion([loftEdge]) : qUnion([iterEdge])}],
                        "startCondition" : (intr.startContinuity == SWRoutContinuityType.G0) ? LoftEndDerivativeType.DEFAULT : LoftEndDerivativeType.MATCH_TANGENT,
                        "endCondition" : (intr.endContinuity == SWRoutContinuityType.G0) ? LoftEndDerivativeType.DEFAULT : LoftEndDerivativeType.MATCH_TANGENT,
                        "startMagnitude" : 1, 
                        "endMagnitude" : 1, 
                        "adjacentFacesStart" : qAdjacent((aGreater) ? qUnion([iterEdge]) : qUnion([loftEdge]), AdjacencyType.EDGE, EntityType.FACE),
                        "adjacentFacesEnd" : qAdjacent((aGreater) ? qUnion([loftEdge]) : qUnion([iterEdge]), AdjacencyType.EDGE, EntityType.FACE), 
                        "trimProfiles" : false,
                        "matchConnections" : false,
                        "showIsocurves" : false,
                        
                        });
                        
                        createdEdges = append(createdEdges, qUnion([qCreatedBy(id + ("unequalEdgeloft1" ~ ix ~ "chain" ~ ch ~ "iterEdge" ~ ie), EntityType.EDGE)]));
                        createdBodies = append(createdBodies, qUnion([qCreatedBy(id + ("unequalEdgeloft1" ~ ix ~ "chain" ~ ch ~ "iterEdge" ~ ie), EntityType.BODY)]));
                        
                    }
                    
                    var edgesQ = qUnion(createdEdges);
                    
                    var orphanPointVector = evVertexPoint(context, {
                            "vertex" : orphanPoint
                    });
                    
                    var orphanEdgeStartPointVector = evVertexPoint(context, {
                            "vertex" : orphanEdgeStart
                    });
                    
                    var orphanEdgeEndPointVector = evVertexPoint(context, {
                            "vertex" : orphanEdgeEnd
                    });
                    
                    var allEdges = evaluateQuery(context, edgesQ);
                    var keepEdges = [];
                    
                    for (var ae = 0; ae < size(allEdges); ae += 1)
                    {
                        var startPoint = evVertexPoint(context, {
                                "vertex" : qEdgeVertex(allEdges[ae], true)
                        });
                        
                        var endPoint = evVertexPoint(context, {
                                "vertex" : qEdgeVertex(allEdges[ae], false)
                        });
                        
                        if (abs(norm(startPoint - orphanPointVector)) < 0.1 * millimeter || abs(norm(endPoint - orphanPointVector)) < 0.1 * millimeter) // one endpoint touches our orphan point
                        {
                            if (abs(norm(startPoint - orphanEdgeStartPointVector)) < 0.1 * millimeter || abs(norm(endPoint - orphanEdgeStartPointVector)) < 0.1 * millimeter) // start or endpoint touches orphan edge startpoint
                            {
                                keepEdges = append(keepEdges, allEdges[ae]);
                            }
                            else if (abs(norm(startPoint - orphanEdgeEndPointVector)) < 0.1 * millimeter || abs(norm(endPoint - orphanEdgeEndPointVector)) < 0.1 * millimeter) //start or endpoint touches orphan edge endpoint
                            {
                                keepEdges = append(keepEdges, allEdges[ae]);
                            }
                        }
                    }
                    
                    
                    var fillEdges = append(keepEdges, orphanEdge);
                
                    opFillSurface(context, id + ("intr" ~ ix ~ "ch" ~ ch ~ "fill"), {
                            "edgesG0" : qUnion(fillEdges),
                            "edgesG1" : qNothing(),
                            "edgesG2" : qNothing(),
                            
                    });
                    
                    var combineBodies = qUnion([qCreatedBy(id + ("intr" ~ ix ~ "ch" ~ ch ~ "fill"), EntityType.BODY), qUnion(createdBodies)]);
                    
                    opBoolean(context, id + ("intr" ~ ix ~ "ch" ~ ch ~ "boolean"), {
                            "tools" : combineBodies,
                            "operationType" : BooleanOperationType.UNION
                    });
                    
                    
                    blendedBodies = append(blendedBodies, qCreatedBy(id + ("intr" ~ ix ~ "ch" ~ ch ~ "fill"), EntityType.BODY));
                        
                        

                }
            }

        }

        // =====================================================================
        // Step 9.6: union all region surfaces with blended bridge bodies
        // =====================================================================
        var allSurfBodies = [];
        for (var r = 0; r < nRegions; r += 1)
        {
            var rKey = toString(r);
            for (var s = 0; s < size(sideNames); s += 1)
            {
                var rsKey = rKey ~ "_" ~ toString(s);
                var b = regionFinalSurfs[rsKey];
                if (b != undefined)
                {
                    allSurfBodies = append(allSurfBodies, b);
                }
            }
        }
        for (var bb in blendedBodies)
        {
            if (!isQueryEmpty(context, bb))
            {
                allSurfBodies = append(allSurfBodies, bb);
            }
        }

        if (size(blendedBodies) > 0 && size(allSurfBodies) > 1)
        {
            opBoolean(context, id + "finalSurfUnion", {
                    "tools"         : qUnion(allSurfBodies),
                    "operationType" : BooleanOperationType.UNION
            });
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

// Returns array of {pos, degree} for all vertices in the edge set.
// Degree = number of cap edges that share that vertex position.
// Degree 1 = free endpoint; degree 2+ = interior junction (e.g. step-riser bend).


function chainEdges(context is Context, queryArray is array) returns array
{
    var allEdges = [];
    
    for (var i = 0; i < size(queryArray); i += 1)
    {
        var edgeQueries = evaluateQuery(context, queryArray[i]);
        allEdges = concatenateArrays(allEdges, edgeQueries);
    }
    
    var chains = [];
    
    while(size(allEdges) > 0)
    {
        var seedEdge = allEdges[0];
        
        var thisChain = [seedEdge];
        
        var adjacentEdges = evaluateQuery(context, qIntersection([qUnion(allEdges), qAdjacent(qUnion(thisChain), AdjacencyType.VERTEX, EntityType.EDGE)]));
        
        var addedEdges = !isQueryEmpty(context, qUnion(adjacentEdges));
        
        while(addedEdges)
        {
            thisChain = concatenateArrays(thisChain, adjacentEdges); // add edges
            allEdges = filter(allEdges, function(x) {return !any(thisChain, function(q) {return areQueriesEquivalent(context, x, q);});}); //remove added edges from allEdges
            
            if (size(allEdges) > 0)
            {
                adjacentEdges = evaluateQuery(context, qIntersection([qUnion(allEdges), qAdjacent(qUnion(thisChain), AdjacencyType.VERTEX, EntityType.EDGE)]));
                
                addedEdges = !isQueryEmpty(context, qUnion(adjacentEdges));
            }
            else
            {
                addedEdges = false;
                break;
            }
        }
        
        //all edges that COULD be part of a chain with our seed edge should now be in the chain
        
        chains = append(chains, thisChain);
    }
    
    var mappedChains = [];
    for (var i = 0; i < size(chains); i += 1)
    {
        var chainMap = {'edgeArray' : evaluateQuery(context, qUnion(chains[i]))};
        var chainBox = evBox3d(context, {
                "topology" : qUnion(chains[i]),
                "tight" : true
        });
        chainMap['boundingBox'] = chainBox;
        chainMap['midPoint'] = (chainBox.minCorner + chainBox.maxCorner)/2;
        chainMap['numEdges'] = size(chainMap['edgeArray']);
        
        mappedChains = append(mappedChains, chainMap);
    }
    
    return mappedChains;
}

/**
 * Returns the signed distance from a point to a plane.
 * Positive = same side as normal, negative = opposite, zero = on plane.
 */
function pointToPlaneDistance(pt is Vector, plane is Plane) returns ValueWithUnits
{
    return dot(pt - plane.origin, plane.normal);
}

/**
 * Filter a pre-evaluated edge query to those lying on a given plane.
 *
 * @param edges      : Query  — already-filtered edge candidates
 * @param testPlane  : Plane  — plane(origin, normal)
 * @param coplanar   : boolean — true = all sampled points must be on plane
 *                               false = midpoint only
 * @param tolerance  : ValueWithUnits — distance tolerance (e.g. 1e-8 * meter)
 */
function qEdgesOnPlane(context is Context, edges is Query, testPlane is Plane,
                       coplanar is boolean, tolerance is ValueWithUnits) returns Query
{
    // Sample parameters: midpoint-only uses just [0.5];
    // coplanar check uses 5 points to catch curved edges
    const params = coplanar ? [0, 0.25, 0.5, 0.75, 1.0] : [0.5];

    var result = qNothing();

    for (var edge in evaluateQuery(context, edges))
    {
        var onPlane = true;

        for (var t in params)
        {
            const tangentLine = evEdgeTangentLine(context, {
                "edge"      : edge,
                "parameter" : t
            });

            if (abs(pointToPlaneDistance(tangentLine.origin, testPlane)) > tolerance)
            {
                onPlane = false;
                break;  // early exit — no need to check remaining samples
            }
        }

        if (onPlane)
            result = qUnion([result, edge]);
    }

    return result;
}

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
