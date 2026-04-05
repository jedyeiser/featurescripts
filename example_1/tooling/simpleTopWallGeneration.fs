FeatureScript 2931;
import(path : "onshape/std/common.fs", version : "2931.0");

// IMPORT: refSurfCore.fs       (RegionExtentDef, RegionOffsetType, IntersectionContinuityType, all bounds)
export import(path : "828cc4108f1c8683bc0e59cf", version : "19939e975eb473ccfc23a8be");

// IMPORT: pathProcessing.fs    (processPath, frameAtPoint, queryRegionExtents)
import(path : "e9dd34f07820388a202cb620", version : "627abffa89a67d104b65a1e1");

// IMPORT: regionProcessing.fs  (processRegions)
import(path : "d1cf8af3d05964b44c3ab4c0", version : "2a69de4c258716430ef2bc97");

// IMPORT: refSurfUtils.fs      (computeOffsetMag, buildIntersectionJoins)
import(path : "d41884a96244793beb462449", version : "2311652bc87faaf67ce79c7d");


// ─── Bounds ───────────────────────────────────────────────────────────────────

const WALL_ANGLE_BOUNDS  = { (degree)     : [0,  10, 89]  } as AngleBoundSpec;
const WALL_HEIGHT_BOUNDS = { (millimeter) : [1,  20, 200] } as LengthBoundSpec;
const RADIUS_BOUNDS      = { (millimeter) : [0,   2,  50] } as LengthBoundSpec;
const CP_MULT_BOUNDS     = { (unitless)   : [2,   4,  10] } as IntegerBoundSpec;

// ─── Enums ────────────────────────────────────────────────────────────────────

export enum TopWallMode
{
    annotation { "Name" : "Full" }
    FULL,
    annotation { "Name" : "Wall only" }
    WALL_ONLY,
    annotation { "Name" : "Wall + pinch" }
    WALL_PINCH
}

// CONSTANT / LINEAR / SMOOTH only (no Quadratic).
// String values match RegionOffsetType so computeOffsetMag comparisons still work.
export enum SimpleOffsetType
{
    annotation { "Name" : "Constant" }
    CONSTANT,
    annotation { "Name" : "Linear" }
    LINEAR,
    annotation { "Name" : "Smooth" }
    SMOOTH
}

// ─── Pure geometry helpers ────────────────────────────────────────────────────

// wallAngle measured from the surface normal (0 = vertical wall).
// Returns the setback beyond pinchOffset so the pinch fillet of radius R
// terminates exactly at the pinchOffset location.
//   derivation: tangent length on CD surface from wall base = R * (1 - sin(a)) / cos(a)
function calcWallBottomOffset(wallRadius is ValueWithUnits, wallAngle is ValueWithUnits) returns ValueWithUnits
{
    var sinA = sin(wallAngle);
    var cosA = cos(wallAngle);
    if (cosA < 1e-6)
    {
        return 0 * millimeter;
    }
    return wallRadius * (1 - sinA) / cosA;
}

// t in [0,1] along the region start->end axis.
function computeRegionT(edgePt is Vector, startOrigin is Vector, endOrigin is Vector) returns number
{
    var axis   = (endOrigin - startOrigin) / meter;
    var axLen2 = dot(axis, axis);
    if (axLen2 < 1e-18)
    {
        return 0;
    }
    var t = dot((edgePt - startOrigin) / meter, axis) / axLen2;
    return min(max(t, 0), 1);
}

// Binormal = cross(faceNormal, edgeTangent), flipped if it points into the face.
// Probe 0.1 mm; if probe lands on face, the initial direction was inward.
function computeBinormal(context is Context, faceQ is Query, edgePt is Vector,
    faceNorm is Vector, edgeTan is Vector) returns Vector
{
    var bn = cross(faceNorm, edgeTan);
    var probePt   = edgePt + bn * 1e-4 * meter;
    var probeDist = evDistance(context, { "side0" : faceQ, "side1" : probePt }).distance;
    if (probeDist < 0.5e-4 * meter)
    {
        bn = -bn;
    }
    return bn;
}

// ─── CD surface setup ─────────────────────────────────────────────────────────

// Copies cdSurf, splits the copy with swSurf, identifies inside (smaller bounding
// box) vs outside, and returns both as { inside, outside }.
// The inside piece's laminar edges are the swRout intersection -- the pinch edges.
function setupCDSplit(context is Context, id is Id, cdSurf is Query, swSurf is Query) returns map
{
    opPattern(context, id + "cdCopy", {
        "entities"      : cdSurf,
        "transforms"    : [identityTransform()],
        "instanceNames" : ["1"]
    });
    var cdCopyQ = qCreatedBy(id + "cdCopy", EntityType.BODY);

    opSplitPart(context, id + "splitCD", {
        "targets"   : cdCopyQ,
        "tool"      : swSurf,
        "keepTools" : true
    });

    var splitTrueQ  = qSplitBy(id + "splitCD", EntityType.BODY, true);
    var splitFalseQ = qSplitBy(id + "splitCD", EntityType.BODY, false);

    var trueBox  = evBox3d(context, { "topology" : splitTrueQ,  "tight" : true });
    var falseBox = evBox3d(context, { "topology" : splitFalseQ, "tight" : true });

    var trueSize  = norm(trueBox.maxCorner  - trueBox.minCorner);
    var falseSize = norm(falseBox.maxCorner - falseBox.minCorner);

    var insideQ;
    var outsideQ;
    if (trueSize < falseSize)
    {
        insideQ  = splitTrueQ;
        outsideQ = splitFalseQ;
    }
    else
    {
        insideQ  = splitFalseQ;
        outsideQ = splitTrueQ;
    }

    setProperty(context, { "entities" : insideQ,  "propertyType" : PropertyType.NAME, "value" : "CD_inside"  });
    setProperty(context, { "entities" : outsideQ, "propertyType" : PropertyType.NAME, "value" : "CD_outside" });

    return { "inside" : insideQ, "outside" : outsideQ };
}

// ─── Wall surface builder ─────────────────────────────────────────────────────

// For each periphery edge of the region CD slice:
//   bottomPt = edgePt + totalBottomOffset * binormal
//   topPt    = bottomPt + wallHeight * refWireNormal
//              where refWireNormal = frameAtPoint(processedPath, bottomPt).frame.xAxis
// Both curves are co-fitted via approximateSpline and lofted.
// cpMult: sample count = max(degree+1, nControlPoints * cpMult)
function buildSimpleWallSurface(context is Context, id is Id,
    regionSurf is Query,
    peripheryEdges is Query,
    offsetDef is map,
    wallHeight is ValueWithUnits,
    processedPath is map,
    cpMult is number,
    splineDegree is number,
    tolerance is ValueWithUnits,
    maxCP is number,
    debug is boolean) returns Query
{
    var allSurfs = [];
    var edgeList = evaluateQuery(context, peripheryEdges);

    if (debug)
    {
        println("buildSimpleWallSurface: region=" ~ offsetDef.regionName ~
                "  edges=" ~ size(edgeList));
    }

    for (var ei = 0; ei < size(edgeList); ei += 1)
    {
        var edgeQ    = edgeList[ei];
        var adjFaces = evaluateQuery(context, qAdjacent(edgeQ, AdjacencyType.EDGE, EntityType.FACE));
        if (size(adjFaces) == 0)
        {
            if (debug) { println("  edge " ~ ei ~ ": no adjacent face, skipping"); }
            continue;
        }
        var faceQ = adjFaces[0];

        // Derive sample count from the edge's own control-point count.
        var bsCurve  = evApproximateBSplineCurve(context, { "edge" : edgeQ });
        var numPts   = max(splineDegree + 1, size(bsCurve.controlPoints) * cpMult);

        if (debug)
        {
            println("  edge " ~ ei ~ ": bsCPs=" ~ size(bsCurve.controlPoints) ~
                    "  numPts=" ~ numPts);
        }

        var sampleParams = [];
        for (var k = 0; k < numPts; k += 1)
        {
            sampleParams = append(sampleParams, k / (numPts - 1));
        }

        var tangentLines = evEdgeTangentLines(context, {
            "edge"                      : edgeQ,
            "parameters"                : sampleParams,
            "arcLengthParameterization" : true
        });

        var bottomPts = [];
        var topPts    = [];

        for (var k = 0; k < size(tangentLines); k += 1)
        {
            var tl   = tangentLines[k];
            var ePt  = tl.origin;
            var eTan = tl.direction;

            var uvParam  = evDistance(context, { "side0" : faceQ, "side1" : ePt }).sides[0].parameter;
            var faceNorm = evFaceTangentPlane(context, { "face" : faceQ, "parameter" : uvParam }).normal;
            var binorm   = computeBinormal(context, faceQ, ePt, faceNorm, eTan);

            var tReg      = computeRegionT(ePt, offsetDef.startFrameOrigin, offsetDef.endFrameOrigin);
            var offsetMag = computeOffsetMag(offsetDef, tReg);

            var bottomPt = ePt + offsetMag * binorm;
            bottomPts = append(bottomPts, bottomPt);

            var ptFrame = frameAtPoint(context, processedPath, bottomPt);
            topPts = append(topPts, bottomPt + wallHeight * ptFrame.frame.xAxis);
        }

        if (size(bottomPts) < splineDegree + 1)
        {
            if (debug)
            {
                println("  edge " ~ ei ~ ": only " ~ size(bottomPts) ~
                        " pts, need " ~ (splineDegree + 1) ~ " -- skipping");
            }
            continue;
        }

        var approxResult = approximateSpline(context, {
            "degree"           : splineDegree,
            "tolerance"        : tolerance,
            "isPeriodic"       : false,
            "maxControlPoints" : maxCP,
            "targets"          : [{ "points" : bottomPts }, { "points" : topPts }],
            "parameters"       : sampleParams
        });

        if (approxResult == undefined || size(approxResult) < 2)
        {
            if (debug) { println("  edge " ~ ei ~ ": approximateSpline returned nothing, skipping"); }
            continue;
        }

        var bottomId = id + ("bCurve" ~ ei);
        var topId    = id + ("tCurve" ~ ei);
        var loftId   = id + ("loft"   ~ ei);
        var cleanId  = id + ("clean"  ~ ei);

        try silent
        {
            opCreateBSplineCurve(context, bottomId, { "bSplineCurve" : approxResult[0] });
            opCreateBSplineCurve(context, topId,    { "bSplineCurve" : approxResult[1] });

            opLoft(context, loftId, {
                "bodyType"          : ToolBodyType.SURFACE,
                "profileSubqueries" : [qCreatedBy(bottomId, EntityType.EDGE),
                                       qCreatedBy(topId,    EntityType.EDGE)]
            });

            allSurfs = append(allSurfs, qCreatedBy(loftId, EntityType.BODY));

            if (debug) { println("  edge " ~ ei ~ ": loft OK"); }
        }
        catch
        {
            if (debug) { println("  edge " ~ ei ~ ": loft FAILED (caught by try silent)"); }
        }

        try silent
        {
            opDeleteBodies(context, cleanId, {
                "entities" : qUnion([qCreatedBy(bottomId, EntityType.BODY),
                                     qCreatedBy(topId,    EntityType.BODY)])
            });
        }
    }

    if (size(allSurfs) == 0)
    {
        return qNothing();
    }
    return qUnion(allSurfs);
}

// ─── Editing logic ────────────────────────────────────────────────────────────

export function topWalliiEditingLogic(context is Context, id is Id,
    oldDefinition is map, definition is map, isCreating is boolean,
    specifiedParameters is map, hiddenBodies is Query) returns map
{
    if (size(definition.regions) > 0)
    {
        var sortable   = [];
        var unsortable = [];

        var processedPath = processPath(context, id + "editingLogic", {
            "userSelection"  : definition.refWire,
            "flipDirection"  : definition.flipDirection,
            "referencePoint" : definition.refPoint,
            "numPoints"      : 20
        });

        for (var r = 0; r < size(definition.regions); r += 1)
        {
            var reg = definition.regions[r];
            if (reg.extentDef == RegionExtentDef.ALONG_REF &&
                reg.startX != undefined && reg.endX != undefined)
            {
                sortable = append(sortable, reg);
            }
            else if (reg.extentDef == RegionExtentDef.QUERY)
            {
                var extents = queryRegionExtents(context, processedPath, reg.startPoint, reg.endPoint);
                reg.startX  = extents.startX;
                reg.endX    = extents.endX;
                sortable = append(sortable, reg);
            }
            else
            {
                unsortable = append(unsortable, reg);
            }
        }

        sortable = sort(sortable, function(a, b)
            { return (a.startX + a.endX) / 2 - (b.startX + b.endX) / 2; });

        var counter    = 0;
        var newRegions = [];

        for (var i = 0; i < size(sortable); i += 1)
        {
            var reg = sortable[i];
            reg.regionNum        = counter;
            reg.needsDefaultName = (length(reg.name) == 0 || reg.name == undefined ||
                                    startsWith(reg.name, "Region "));
            if (reg.needsDefaultName) { reg.name = "Region " ~ counter; }
            newRegions = append(newRegions, reg);
            counter += 1;
        }
        for (var i = 0; i < size(unsortable); i += 1)
        {
            var reg = unsortable[i];
            reg.regionNum        = counter;
            reg.needsDefaultName = (length(reg.name) == 0 || reg.name == undefined);
            if (reg.needsDefaultName) { reg.name = "Region " ~ counter; }
            newRegions = append(newRegions, reg);
            counter += 1;
        }

        definition.regions = newRegions;

        var oldIntersections = definition.intersections;
        var newIntersections = [];
        for (var i = 0; i < size(newRegions) - 1; i += 1)
        {
            var ix = (i < size(oldIntersections)) ? oldIntersections[i] : {
                "intersectionNum"       : i,
                "intersectionName"      : "Intersection " ~ (i + 1),
                "needsIntersectionName" : true,
                "join"                  : false,
                "startContinuity"       : IntersectionContinuityType.G0,
                "endContinuity"         : IntersectionContinuityType.G0,
                "joinStartOffset"       : 0 * millimeter,
                "joinEndOffset"         : 0 * millimeter
            };
            ix.intersectionNum       = i;
            ix.needsIntersectionName = (length(ix.intersectionName) == 0 ||
                                        ix.intersectionName == undefined ||
                                        startsWith(ix.intersectionName, "Intersection "));
            if (ix.needsIntersectionName) { ix.intersectionName = "Intersection " ~ (i + 1); }
            ix.regionANum  = i;
            ix.regionBNum  = i + 1;
            ix.regionAName = newRegions[i].name;
            ix.regionBName = newRegions[i + 1].name;
            newIntersections = append(newIntersections, ix);
        }
        definition.intersections = newIntersections;
    }
    else
    {
        definition.intersections = [];
    }

    return definition;
}

// ─── Feature definition ───────────────────────────────────────────────────────

annotation { "Feature Type Name" : "Top Wall 2",
             "Feature Type Description" : "",
             "Editing Logic Function" : "topWalliiEditingLogic" }
export const topWallii = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Mode", "Default" : TopWallMode.FULL, "UIHint" : UIHint.HORIZONTAL_ENUM }
        definition.mode is TopWallMode;

        annotation { "Name" : "SW rout surface",
                     "Filter" : EntityType.BODY && BodyType.SHEET && ModifiableEntityOnly.YES,
                     "MaxNumberOfPicks" : 1 }
        definition.swRoutSurf is Query;

        annotation { "Name" : "CD surface",
                     "Filter" : EntityType.BODY && BodyType.SHEET && ModifiableEntityOnly.YES,
                     "MaxNumberOfPicks" : 1 }
        definition.cdSurf is Query;

        annotation { "Name" : "Ref. wire",
                     "Filter" : EntityType.BODY && BodyType.WIRE && ModifiableEntityOnly.YES,
                     "MaxNumberOfPicks" : 1 }
        definition.refWire is Query;

        annotation { "Name" : "Flip direction", "Default" : false, "UIHint" : UIHint.OPPOSITE_DIRECTION }
        definition.flipDirection is boolean;

        annotation { "Name" : "Ref. point",
                     "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE,
                     "MaxNumberOfPicks" : 1 }
        definition.refPoint is Query;

        if (definition.mode == TopWallMode.FULL)
        {
            annotation { "Name" : "Top surface",
                         "Filter" : EntityType.BODY && BodyType.SHEET && ModifiableEntityOnly.YES,
                         "MaxNumberOfPicks" : 1 }
            definition.topSurf is Query;
        }

        annotation { "Name" : "Wall height" }
        isLength(definition.wallHeight, WALL_HEIGHT_BOUNDS);

        annotation { "Name" : "Regions", "Item name" : "Region",
                     "Item label template" : "#name",
                     "Collapsed By Default" : true,
                     "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS }
        definition.regions is array;
        for (var region in definition.regions)
        {
            annotation { "Name" : "regionNum", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isInteger(region.regionNum, RegionNumBounds);

            annotation { "Name" : "Extents from", "Default" : RegionExtentDef.ALONG_REF,
                         "UIHint" : UIHint.HORIZONTAL_ENUM }
            region.extentDef is RegionExtentDef;

            annotation { "Name" : "Name" }
            region.name is string;

            annotation { "Name" : "needsDefaultName", "Default" : true, "UIHint" : UIHint.ALWAYS_HIDDEN }
            region.needsDefaultName is boolean;

            if (region.extentDef == RegionExtentDef.ALONG_REF)
            {
                annotation { "Name" : "Start x" }
                isLength(region.startX, LENGTH_BOUNDS);

                annotation { "Name" : "End x" }
                isLength(region.endX, LENGTH_BOUNDS);
            }
            else if (region.extentDef == RegionExtentDef.QUERY)
            {
                annotation { "Name" : "Start",
                             "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE,
                             "MaxNumberOfPicks" : 1 }
                region.startPoint is Query;

                annotation { "Name" : "End",
                             "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE,
                             "MaxNumberOfPicks" : 1 }
                region.endPoint is Query;
            }

            annotation { "Name" : "Pinch offset type", "Default" : SimpleOffsetType.CONSTANT,
                         "UIHint" : UIHint.HORIZONTAL_ENUM }
            region.pinchOffsetType is SimpleOffsetType;

            if (region.pinchOffsetType == SimpleOffsetType.CONSTANT)
            {
                annotation { "Name" : "Pinch offset" }
                isLength(region.pinchOffset, REGION_OFFSET_BOUNDS);
            }
            else
            {
                annotation { "Name" : "Pinch offset (start)" }
                isLength(region.startPinchOffset, REGION_OFFSET_BOUNDS);

                annotation { "Name" : "Pinch offset (end)" }
                isLength(region.endPinchOffset, REGION_OFFSET_BOUNDS);
            }

            annotation { "Name" : "Wall angle (from normal)" }
            isAngle(region.wallAngle, WALL_ANGLE_BOUNDS);

            annotation { "Name" : "Pinch radius (start)" }
            isLength(region.pinchRadiusStart, RADIUS_BOUNDS);

            annotation { "Name" : "Pinch radius (end)" }
            isLength(region.pinchRadiusEnd, RADIUS_BOUNDS);

            annotation { "Name" : "Top radius (start)" }
            isLength(region.topRadiusStart, RADIUS_BOUNDS);

            annotation { "Name" : "Top radius (end)" }
            isLength(region.topRadiusEnd, RADIUS_BOUNDS);
        }

        annotation { "Name" : "Intersections", "Item name" : "Intersection",
                     "Item label template" : "#intersectionName",
                     "Collapsed By Default" : true,
                     "UIHint" : [UIHint.PREVENT_ARRAY_REORDER, UIHint.COLLAPSE_ARRAY_ITEMS] }
        definition.intersections is array;
        for (var ix in definition.intersections)
        {
            annotation { "Name" : "Intersection number", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isInteger(ix.intersectionNum, { (unitless) : [0, 0, 100] } as IntegerBoundSpec);

            annotation { "Name" : "Name" }
            ix.intersectionName is string;

            annotation { "Name" : "needsIntersectionName", "Default" : true, "UIHint" : UIHint.ALWAYS_HIDDEN }
            ix.needsIntersectionName is boolean;

            annotation { "Name" : "RegionANum", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isInteger(ix.regionANum, { (unitless) : [0, 0, 100] } as IntegerBoundSpec);

            annotation { "Name" : "Region A", "UIHint" : UIHint.READ_ONLY }
            ix.regionAName is string;

            annotation { "Name" : "RegionBNum", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isInteger(ix.regionBNum, { (unitless) : [0, 0, 100] } as IntegerBoundSpec);

            annotation { "Name" : "Region B", "UIHint" : UIHint.READ_ONLY }
            ix.regionBName is string;

            annotation { "Name" : "Join", "Default" : false }
            ix.join is boolean;

            if (ix.join)
            {
                annotation { "Name" : "Start continuity", "Default" : IntersectionContinuityType.G0,
                             "UIHint" : UIHint.SHOW_LABEL }
                ix.startContinuity is IntersectionContinuityType;

                annotation { "Name" : "End continuity", "Default" : IntersectionContinuityType.G0,
                             "UIHint" : UIHint.SHOW_LABEL }
                ix.endContinuity is IntersectionContinuityType;

                annotation { "Name" : "Start offset" }
                isLength(ix.joinStartOffset, INTERSECTION_OFFSET_BOUNDS);

                annotation { "Name" : "End offset" }
                isLength(ix.joinEndOffset, INTERSECTION_OFFSET_BOUNDS);
            }
        }

        annotation { "Group Name" : "Approximation", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Sampling multiplier (x CPs)", "Description" : "Sample count = edge control points x this multiplier" }
            isInteger(definition.cpMultiplier, CP_MULT_BOUNDS);

            annotation { "Name" : "Spline degree" }
            isInteger(definition.approxDegree, ApproxDegreeBounds);

            annotation { "Name" : "Approximation tolerance" }
            isLength(definition.approxTolerance, ApproxToleranceBounds);

            annotation { "Name" : "Max control points" }
            isInteger(definition.approxMaxCP, ApproxMaxCPBounds);
        }

        annotation { "Name" : "Debug", "Default" : false }
        definition.debug is boolean;

        if (definition.debug)
        {
            annotation { "Group Name" : "Debug", "Collapsed By Default" : false, "Driving Parameter" : "debug" }
            {
                annotation { "Name" : "Step through?", "Default" : false }
                definition.stepThrough is boolean;

                if (definition.stepThrough)
                {
                    annotation { "Group Name" : "Step through", "Collapsed By Default" : false,
                                 "Driving Parameter" : "stepThrough" }
                    {
                        annotation { "Name" : "Step 0 - CD split + path", "Default" : false }
                        definition.step0 is boolean;

                        annotation { "Name" : "Step 1 - Region slices", "Default" : false }
                        definition.step1 is boolean;

                        annotation { "Name" : "Step 2 - Wall surfaces", "Default" : false }
                        definition.step2 is boolean;
                    }
                }

                annotation { "Name" : "Show ref frames", "Default" : false }
                definition.showRefFrames is boolean;

                annotation { "Name" : "Show wall bottom wire", "Default" : false }
                definition.showWallBottom is boolean;

                annotation { "Name" : "Show wall top wire", "Default" : false }
                definition.showWallTop is boolean;
            }
        }
    }
    {
        // ── Input validation ──────────────────────────────────────────────────
        verifyNonemptyQuery(context, definition, "swRoutSurf", "Select a sidewall rout surface");
        verifyNonemptyQuery(context, definition, "cdSurf",     "Select a CD surface");
        verifyNonemptyQuery(context, definition, "refWire",    "Select a reference wire");
        if (definition.mode == TopWallMode.FULL)
        {
            verifyNonemptyQuery(context, definition, "topSurf", "Select a top surface");
        }

        // ── Step 0: Process path ───────────────────────────────────────────────
        var processedPath = processPath(context, id + "path", {
            "userSelection"  : definition.refWire,
            "flipDirection"  : definition.flipDirection,
            "referencePoint" : definition.refPoint,
            "numPoints"      : 20
        });

        if (definition.debug)
        {
            println("[TWG2] processPath done  totalLength=" ~
                    processedPath.totalLength / millimeter ~ " mm");
        }

        if (definition.debug && definition.showRefFrames)
        {
            showRefFrames(context, processedPath, false, false);
        }

        // ── Step 0: Split CD surface with swRout ───────────────────────────────
        if (definition.debug) { println("[TWG2] setupCDSplit..."); }
        var cdSplit = setupCDSplit(context, id + "setup", definition.cdSurf, definition.swRoutSurf);
        if (definition.debug) { println("[TWG2] setupCDSplit done"); }
        // cdSplit.inside  = CD strip inside swRout cavity; its laminar edges are the pinch edges.
        // cdSplit.outside = remaining CD surface, retained for bottom-trim (Step 4 stub).

        if (definition.debug && definition.stepThrough && !definition.step1)
        {
            return;
        }

        // ── Step 1: Overlap validation + process regions ───────────────────────
        var regionsForOverlapCheck = [];
        for (var r = 0; r < size(definition.regions); r += 1)
        {
            var reg = definition.regions[r];
            if (reg.extentDef == RegionExtentDef.ALONG_REF)
            {
                regionsForOverlapCheck = append(regionsForOverlapCheck, {
                    "name"   : reg.name,
                    "startX" : reg.startX,
                    "endX"   : reg.endX
                });
            }
        }
        regionsForOverlapCheck = sort(regionsForOverlapCheck, function(a, b)
            { return (a.startX - b.startX) / meter; });
        for (var i = 0; i < size(regionsForOverlapCheck) - 1; i += 1)
        {
            var gap = regionsForOverlapCheck[i + 1].startX - regionsForOverlapCheck[i].endX;
            if (gap < -1e-6 * meter)
            {
                throw regenError("Regions overlap: \"" ~ regionsForOverlapCheck[i].name ~
                                 "\" and \"" ~ regionsForOverlapCheck[i + 1].name ~ "\"");
            }
        }

        var processedRegions = processRegions(context, { "regions" : definition.regions }, processedPath);

        if (definition.debug)
        {
            println("[TWG2] processRegions done  count=" ~ size(processedRegions));
            for (var r = 0; r < size(processedRegions); r += 1)
            {
                var rr = processedRegions[r];
                println("  region " ~ r ~ " '" ~ definition.regions[r].name ~ "'" ~
                        "  tStart=" ~ rr.tStart ~ "  tEnd=" ~ rr.tEnd ~
                        "  length=" ~ rr.length / millimeter ~ " mm");
            }
        }

        if (definition.debug && definition.stepThrough && !definition.step2)
        {
            return;
        }

        // ── Step 2-3: Per-region wall surface ──────────────────────────────────
        for (var r = 0; r < size(processedRegions); r += 1)
        {
            var region = processedRegions[r];
            var reg    = definition.regions[r];

            if (definition.debug)
            {
                println("[TWG2] region " ~ r ~ " '" ~ reg.name ~ "' start...");
            }

            // Copy the inside CD strip for this region.
            opPattern(context, id + ("cdReg" ~ r ~ "copy"), {
                "entities"      : cdSplit.inside,
                "transforms"    : [identityTransform()],
                "instanceNames" : ["1"]
            });
            var regionCopy = qCreatedBy(id + ("cdReg" ~ r ~ "copy"), EntityType.BODY);
            setProperty(context, {
                "entities"     : regionCopy,
                "propertyType" : PropertyType.NAME,
                "value"        : reg.name ~ " cd strip"
            });

            // Trim to region extent.
            var startPl = plane(region.startFrame.origin, region.startFrame.zAxis);
            var endPl   = plane(region.endFrame.origin,   region.endFrame.zAxis);

            var startTrims = qIntersectsPlane(regionCopy, startPl);
            var endTrims   = qIntersectsPlane(regionCopy, endPl);
            if (!isQueryEmpty(context, startTrims))
            {
                opSplitPart(context, id + ("rStart" ~ r), { "targets" : startTrims, "tool" : startPl });
            }
            if (!isQueryEmpty(context, endTrims))
            {
                opSplitPart(context, id + ("rEnd" ~ r), { "targets" : endTrims, "tool" : endPl });
            }

            var midPt  = (region.startFrame.origin + region.endFrame.origin) / 2;
            var midDir = normalize((region.startFrame.origin - region.endFrame.origin) / millimeter);
            var midPl  = plane(midPt, midDir);
            opDeleteBodies(context, id + ("rDelete" ~ r), {
                "entities" : qSubtraction(regionCopy, qIntersectsPlane(regionCopy, midPl))
            });

            // Periphery = one-sided edges, excluding region-boundary plane edges.
            var splitEdges = qUnion([
                qCreatedBy(id + ("rStart" ~ r), EntityType.EDGE),
                qCreatedBy(id + ("rEnd"   ~ r), EntityType.EDGE)
            ]);
            var boundaryEdges = qUnion([
                qCoincidesWithPlane(qOwnedByBody(regionCopy, EntityType.EDGE), startPl),
                qCoincidesWithPlane(qOwnedByBody(regionCopy, EntityType.EDGE), endPl)
            ]);
            var peripheryEdges = qEdgeTopologyFilter(
                qSubtraction(qOwnedByBody(regionCopy, EntityType.EDGE),
                             qUnion([splitEdges, boundaryEdges])),
                EdgeTopology.ONE_SIDED
            );

            // Build offsetDef.
            // totalOffset = pinchOffset + wallBottomOffset(wallRadius, wallAngle)
            // wallBottomOffset is constant per region (depends only on radius + angle).
            var isConst        = reg.pinchOffsetType == SimpleOffsetType.CONSTANT;
            var wallBotOffStart = calcWallBottomOffset(reg.pinchRadiusStart, reg.wallAngle);
            var wallBotOffEnd   = calcWallBottomOffset(reg.pinchRadiusEnd,   reg.wallAngle);

            var totalStart = (isConst ? reg.pinchOffset : reg.startPinchOffset) + wallBotOffStart;
            var totalEnd   = (isConst ? reg.pinchOffset : reg.endPinchOffset)   + wallBotOffEnd;

            if (definition.debug)
            {
                println("  wallBotOffStart=" ~ wallBotOffStart / millimeter ~ " mm" ~
                        "  wallBotOffEnd="   ~ wallBotOffEnd   / millimeter ~ " mm" ~
                        "  totalStart="      ~ totalStart      / millimeter ~ " mm" ~
                        "  totalEnd="        ~ totalEnd        / millimeter ~ " mm");
            }

            var offsetDef = {
                "startFrameOrigin" : region.startFrame.origin,
                "endFrameOrigin"   : region.endFrame.origin,
                "offsetType"       : reg.pinchOffsetType,
                "offset"           : isConst ? totalStart : (0 * millimeter),
                "startOffset"      : totalStart,
                "endOffset"        : totalEnd,
                "zeroSlopeAtStart" : true,
                "regionName"       : reg.name,
                "singleCurve"      : false
            };

            // ── Step 3: Build wall surface ─────────────────────────────────────
            var wallSurfQ = buildSimpleWallSurface(
                context,
                id + ("wall" ~ r),
                regionCopy,
                peripheryEdges,
                offsetDef,
                definition.wallHeight,
                processedPath,
                definition.cpMultiplier,
                definition.approxDegree,
                definition.approxTolerance,
                definition.approxMaxCP,
                definition.debug
            );

            processedRegions[r] = mergeMaps(processedRegions[r], { "loftBodyQuery" : wallSurfQ });

            opDeleteBodies(context, id + ("cdRegClean" ~ r), { "entities" : regionCopy });

            // ── Step 4 (STUB): Trim bottom of wall against CD surface ──────────
            // TODO: copy cdSplit.outside, extend bottom edge of wallSurfQ to meet it,
            //       mutual trim, delete inside of CD copy.

            // ── Step 5 (STUB): Trim top of wall against topSurf ───────────────
            // TODO (mode == FULL): copy topSurf, extend top edge of wallSurfQ,
            //       mutual trim, delete outside of topSurf copy.

            // ── Step 6 (STUB): Apply wall radius fillet ───────────────────────
            // TODO: opFillet on bottom edges with interpolated pinchRadius(t),
            //       on top edges with interpolated topRadius(t).
        }

        // Clean up the split CD bodies.
        opDeleteBodies(context, id + "cleanupCDSplit", {
            "entities" : qUnion([cdSplit.inside, cdSplit.outside])
        });

        // ── Step 7: Join region intersections ──────────────────────────────────
        if (size(definition.intersections) > 0)
        {
            buildIntersectionJoins(context, id,
                mergeMaps(definition, { "returnLoftSurface" : true }),
                processedRegions);
        }
    }, {
        "flipDirection"   : false,
        "mode"            : TopWallMode.FULL,
        "wallHeight"      : 20 * millimeter,
        "cpMultiplier"    : 4,
        "approxDegree"    : 3,
        "approxTolerance" : 0.01 * millimeter,
        "approxMaxCP"     : 100,
        "regions"         : [],
        "intersections"   : [],
        "debug"           : false,
        "stepThrough"     : false,
        "step0"           : false,
        "step1"           : false,
        "step2"           : false,
        "showRefFrames"   : false,
        "showWallBottom"  : false,
        "showWallTop"     : false
    });
