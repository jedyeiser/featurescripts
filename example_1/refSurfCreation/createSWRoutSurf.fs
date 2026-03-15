FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");
import(path : "onshape/std/extend.fs", version : "2892.0");
import(path : "onshape/std/faceIntersection.fs", version : "2892.0");
// IMPORT: tools/printing.fs
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/b02d6a2bac551b24347c983f", version : "c104606e8ffc8e0964404bbc");


/**
 * Generates a sidewall rout surface given:
 *   1. Bottom surface — must extend beyond the side surface footprint
 *   2. Side surface   — footprint must be contained by the bottom surface
 *   3. Rout parameters: angle, height above bottom, optional step-in
 *   4. Optional endpoint trimming: reference wire, start/stop points, cutter radius
 *
 * The rout surface is built on the +Y half of the ski (front plane split).
 * A +2 mm overshoot above the side surface top is left intentionally for a future
 * top-trim step driven by an externally created top reference surface.
 */

export const SWRoutAngleBounds      = {(degree)     : [0,   20,  45]} as AngleBoundSpec;
export const DistAboveBottomBounds  = {(millimeter) : [1,    4,  10]} as LengthBoundSpec;
export const SWRoutHeightBounds     = {(millimeter) : [5,   30, 100]} as LengthBoundSpec;
export const SWStepInBounds         = {(millimeter) : [0,    0,   3]} as LengthBoundSpec;
export const cutterRadiusBounds     = {(millimeter) : [2,   10,  20]} as LengthBoundSpec;
export const DEBUG_STEP_BOUNDS      = { (unitless) : [0, 1, 9] } as IntegerBoundSpec;

annotation { "Feature Type Name" : "Sidewall rout surface", "Feature Type Description" : "Creates a SW rout surface based on inputs" }
export const SWRout = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Bottom surface", "Filter" : EntityType.BODY && BodyType.SHEET, "MaxNumberOfPicks" : 1, "Description" : "Bottom of ski. Surface must extend beyond side surface" }
        definition.bottomSheet is Query;

        annotation { "Name" : "Side surface", "Filter" : EntityType.BODY && BodyType.SHEET, "MaxNumberOfPicks" : 1, "Description" : "Side surface of ski. Footprint must be contained by the bottom surface" }
        definition.sideSheet is Query;

        annotation { "Name" : "Sidewall rout angle" }
        isAngle(definition.swRoutAngle, SWRoutAngleBounds);

        annotation { "Name" : "Bottom surface extension" }
        isLength(definition.bottomExtension, LENGTH_BOUNDS);

        annotation { "Name" : "Distance from bottom rout begins" }
        isLength(definition.distAboveBottom, DistAboveBottomBounds);

        annotation { "Name" : "SW Rout Surf Height" }
        isLength(definition.swRoutHeight, SWRoutHeightBounds);

        annotation { "Name" : "SW rout step-in" }
        isLength(definition.swRoutStepin, SWStepInBounds);

        annotation { "Name" : "Output profile wires", "Default" : false }
        definition.outputProfileWires is boolean;

        annotation { "Name" : "Spec rout endpoints?", "Default" : false }
        definition.specSWRoutEndpoints is boolean;

        annotation { "Group Name" : "SW rout endpoint data", "Driving Parameter" : "specSWRoutEndpoints", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Reference curve", "Filter" : BodyType.WIRE, "MaxNumberOfPicks" : 1 }
            definition.refWire is Query;

            annotation { "Name" : "Start point", "Filter" : EntityType.VERTEX || (EntityType.FACE && GeometryType.PLANE) || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            definition.startPointQ is Query;

            annotation { "Name" : "Stop point", "Filter" : EntityType.VERTEX || (EntityType.FACE && GeometryType.PLANE) || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            definition.stopPointQ is Query;

            annotation { "Name" : "Cutter bottom radius" }
            isLength(definition.cutterBottomRadius, cutterRadiusBounds);
        }

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Step through", "Default" : false }
            definition.debugStepThrough is boolean;

            annotation { "Group Name" : "Step through options", "Driving Parameter" : "debugStepThrough", "Collapsed By Default" : false }
            {
                annotation { "Name" : "Step (0-9)", "UIHint" : UIHint.SHOW_LABEL }
                isInteger(definition.debugStep, DEBUG_STEP_BOUNDS);
            }

            annotation { "Name" : "Print debug", "Default" : false }
            definition.debugPrint is boolean;

            annotation { "Name" : "Detailed BSplines", "Default" : false, "UIHint" : UIHint.SHOW_LABEL }
            definition.debugDetailedBSplines is boolean;
        }
    }
    {
        var debugFmt    = definition.debugDetailedBSplines ? PrintFormat.DETAILS : PrintFormat.METADATA;
        var stepThrough = definition.debugStepThrough;
        var step        = definition.debugStep;

        // --- Get face normals to determine offset directions ---
        var bottomNormal = evFaceTangentPlane(context, {
                "face"      : qNthElement(qOwnedByBody(definition.bottomSheet, EntityType.FACE), 0),
                "parameter" : vector(0.5, 0.5)
        }).normal;

        var sideNormal = evFaceTangentPlane(context, {
                "face"      : qNthElement(qOwnedByBody(definition.sideSheet, EntityType.FACE), 0),
                "parameter" : vector(0.5, 0.5)
        }).normal;

        // opOffsetFace moves faces in the direction of the raw face normal for positive distances.
        // Capture the sign before flipping so we can invert the offset distance when the raw
        // normal is anti-aligned with the intended direction.
        const bottomOffsetSign = bottomNormal[2] >= 0 ? 1 : -1;
        const sideOffsetSign   = sideNormal[1]   >= 0 ? 1 : -1;

        if (bottomNormal[2] < 0) { bottomNormal = -bottomNormal; }
        if (sideNormal[1]   < 0) { sideNormal   = -sideNormal; }

        if (definition.debugPrint)
        {
            println("=== SWRout debug ===");
            println("  bottomNormal    = " ~ toString(bottomNormal));
            println("  bottomOffsetSign= " ~ toString(bottomOffsetSign));
            println("  sideNormal      = " ~ toString(sideNormal));
            println("  sideOffsetSign  = " ~ toString(sideOffsetSign));
            println("  distAboveBottom = " ~ toString(definition.distAboveBottom));
            println("  swRoutAngle     = " ~ toString(definition.swRoutAngle));
            println("  swRoutStepin    = " ~ toString(definition.swRoutStepin));
        }

        // =====================================================================
        // Step 0: Intersect originals → initial wire, then copy both sheet bodies
        // =====================================================================
        if (stepThrough && step >= 0)
        {
            intersectionCurve(context, id + "initialWire", {
                    "group1" : definition.bottomSheet,
                    "group2" : definition.sideSheet
            });
            setProperty(context, { "entities" : qCreatedBy(id + "initialWire", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : "Initial wire" });

            opPattern(context, id + "bottomCopy", {
                    "entities"      : definition.bottomSheet,
                    "transforms"    : [transform(vector(0, 0, 0) * meter)],
                    "instanceNames" : ["1"]
            });
            setProperty(context, { "entities" : qCreatedBy(id + "bottomCopy", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : "Bottom (copy)" });

            opPattern(context, id + "sideCopy", {
                    "entities"      : definition.sideSheet,
                    "transforms"    : [transform(vector(0, 0, 0) * meter)],
                    "instanceNames" : ["1"]
            });
            setProperty(context, { "entities" : qCreatedBy(id + "sideCopy", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : "Side (copy)" });
        }

        // =====================================================================
        // Step 1: Offset copied bottom, intersect with copied side → start wire
        // =====================================================================
        if (stepThrough && step >= 1)
        {
            opOffsetFace(context, id + "startBottomOffset", {
                    "moveFaces"      : qUnion([qOwnedByBody(qCreatedBy(id + "bottomCopy", EntityType.BODY), EntityType.FACE)]),
                    "offsetDistance" : bottomOffsetSign * definition.distAboveBottom
            });
            
            setProperty(context, { "entities" : qCreatedBy(id + "bottomCopy", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : "Bottom (offset copy)" });
            
            intersectionCurve(context, id + 'startIntersect', {
                "group1"   : qCreatedBy(id + "sideCopy",    EntityType.BODY),
                "group2" : qCreatedBy(id + "bottomCopy",  EntityType.BODY)
                });
                
            setProperty(context, { "entities" : qCreatedBy(id + "startIntersect", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : "SWRout start wire" });

            if (definition.debugPrint)
            {
                debugPrintWireBSplines(context, qCreatedBy(id + "startIntersect", EntityType.BODY), "Start wire", debugFmt);
            }
        }

        // =====================================================================
        // Step 2: (Only if swRoutStepin > 0) offset copied side inward → step-in wire
        // =====================================================================
        if (stepThrough && step >= 2 && definition.swRoutStepin > 0 * millimeter)
        {
            opOffsetFace(context, id + "stepInSideOffset", {
                    "moveFaces"      : qUnion([qOwnedByBody(qCreatedBy(id + "sideCopy", EntityType.BODY), EntityType.FACE)]),
                    "offsetDistance" : -sideOffsetSign * definition.swRoutStepin
            });
            setProperty(context, { "entities" : qCreatedBy(id + "sideCopy", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : "Side (offset copy)" });
            
            //use Onshape builtin to help with processing. Feed in bodies - one wire out
            intersectionCurve(context, id + 'stepInIntersection', {
                "group1"   : qCreatedBy(id + "sideCopy",    EntityType.BODY),
                "group2" : qCreatedBy(id + "bottomCopy",  EntityType.BODY)
                });

            
            setProperty(context, { "entities" : qCreatedBy(id + "stepInIntersection", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : "SWRout step-in wire" });

            if (definition.debugPrint)
            {
                debugPrintWireBSplines(context, qCreatedBy(id + "stepInIntersection", EntityType.BODY), "Step-in wire", debugFmt);
            }
            
        }

        // =====================================================================
        // Step 3: Offset both copies to stop position (surfaces only, no wires yet)
        //
        //   routSpanHeight = swRoutHeight - distAboveBottom
        //     The rout surface spans this height vertically (from start wire to stop wire).
        //     bottomCopy is currently at distAboveBottom → add routSpanHeight to reach swRoutHeight.
        //
        //   routSpanSide = routSpanHeight * tan(swRoutAngle)
        //     The angle-derived inward offset of the stop side relative to the start side.
        //     sideCopy is currently at swRoutStepin (or 0) → add routSpanSide on top of that.
        //     The step-in shifts the whole rout inward; the span is independent of it.
        // =====================================================================
        if (stepThrough && step >= 3)
        {
            var routSpanHeight = definition.swRoutHeight - definition.distAboveBottom;
            var routSpanSide   = routSpanHeight * tan(definition.swRoutAngle);

            if (definition.debugPrint)
            {
                println("  swRoutHeight   = " ~ toString(definition.swRoutHeight));
                println("  routSpanHeight = " ~ toString(routSpanHeight));
                println("  routSpanSide   = " ~ toString(routSpanSide));
            }

            opOffsetFace(context, id + "stopBottomOffset", {
                    "moveFaces"      : qUnion([qOwnedByBody(qCreatedBy(id + "bottomCopy", EntityType.BODY), EntityType.FACE)]),
                    "offsetDistance" : bottomOffsetSign * routSpanHeight
            });
            setProperty(context, { "entities" : qCreatedBy(id + "bottomCopy", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : "Stop bottom (offset copy)" });

            opOffsetFace(context, id + "stopSideOffset", {
                    "moveFaces"      : qUnion([qOwnedByBody(qCreatedBy(id + "sideCopy", EntityType.BODY), EntityType.FACE)]),
                    "offsetDistance" : -sideOffsetSign * routSpanSide
            });
            setProperty(context, { "entities" : qCreatedBy(id + "sideCopy", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : "Stop side (offset copy)" });
        }

        // =====================================================================
        // Step 4: Ensure stop surfaces intersect — extend side copy if needed
        //   evDistance between the two offset bodies tells us if there is a gap.
        //   If gap > 0, find the boundary edges of sideCopy closest to bottomCopy
        //   and extend by gapDist + 1mm so the surfaces overlap cleanly.
        // =====================================================================
        if (stepThrough && step >= 4)
        {
            var sideCopyQ   = qCreatedBy(id + "sideCopy",   EntityType.BODY);
            var bottomCopyQ = qCreatedBy(id + "bottomCopy", EntityType.BODY);

            var gapDist = evDistance(context, {
                    "side0" : sideCopyQ,
                    "side1" : bottomCopyQ
            }).distance;

            if (definition.debugPrint)
            {
                println("  stop surface gap = " ~ toString(gapDist));
            }

            if (gapDist > 0 * meter)
            {
                // Find the one-sided boundary edges of sideCopy nearest to bottomCopy
                var oneSidedEdges = evaluateQuery(context, qEdgeTopologyFilter(
                        qOwnedByBody(sideCopyQ, EntityType.EDGE), EdgeTopology.ONE_SIDED));

                var minEdgeDist = 1e10 * meter;
                for (var edge in oneSidedEdges)
                {
                    var d = evDistance(context, { "side0" : edge, "side1" : bottomCopyQ }).distance;
                    if (d < minEdgeDist)
                    {
                        minEdgeDist = d;
                    }
                }

                var edgesToExtend = [];
                for (var edge in oneSidedEdges)
                {
                    var d = evDistance(context, { "side0" : edge, "side1" : bottomCopyQ }).distance;
                    if (d < minEdgeDist + 1e-4 * meter)
                    {
                        edgesToExtend = append(edgesToExtend, edge);
                    }
                }

                extendSurface(context, id + "extendSideToBottom", {
                        "entities"           : qUnion(edgesToExtend),
                        "tangentPropagation" : true,
                        "endCondition"       : ExtendBoundingType.BLIND,
                        "oppositeDirection"  : false,
                        "extendDistance"     : gapDist + 1 * millimeter,
                        "maintainCurvature"  : true
                });
            }

            intersectionCurve(context, id + "stopWire", {
                    "group1" : sideCopyQ,
                    "group2" : bottomCopyQ
            });
            setProperty(context, { "entities" : qCreatedBy(id + "stopWire", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : "SWRout stop wire" });

            if (definition.debugPrint)
            {
                debugPrintWireBSplines(context, qCreatedBy(id + "stopWire", EntityType.BODY), "Stop wire", debugFmt);
            }
        }

        // =====================================================================
        // Step 5: Loft initial wire → start wire
        // =====================================================================
        if (stepThrough && step >= 5)
        {
            var initialWireQ = qCreatedBy(id + "initialWire",   EntityType.BODY);
            var startWireQ   = qCreatedBy(id + "startIntersect", EntityType.BODY);

            opLoft(context, id + "loft1", {
                    "profileSubqueries" : [initialWireQ, startWireQ],
                    "connections"       : buildLoftConnection(context, initialWireQ, startWireQ),
                    "bodyType"          : ToolBodyType.SURFACE
            });
            setProperty(context, { "entities" : qCreatedBy(id + "loft1", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : "SWRout loft 1 (initial → start)" });
        }

        // =====================================================================
        // Step 6: Loft start wire → step-in wire (only if swRoutStepin > 0)
        // =====================================================================
        if (stepThrough && step >= 6 && definition.swRoutStepin > 0 * millimeter)
        {
            var startWireQ  = qCreatedBy(id + "startIntersect",   EntityType.BODY);
            var stepInWireQ = qCreatedBy(id + "stepInIntersection", EntityType.BODY);

            opLoft(context, id + "loft2", {
                    "profileSubqueries" : [startWireQ, stepInWireQ],
                    "connections"       : buildLoftConnection(context, startWireQ, stepInWireQ),
                    "bodyType"          : ToolBodyType.SURFACE
            });
            setProperty(context, { "entities" : qCreatedBy(id + "loft2", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : "SWRout loft 2 (start → step-in)" });
        }

        // =====================================================================
        // Step 7: Loft step-in wire (or start wire if no step-in) → stop wire
        // =====================================================================
        if (stepThrough && step >= 7)
        {
            var stopWireQ  = qCreatedBy(id + "stopWire", EntityType.BODY);
            var lowerWireQ = (definition.swRoutStepin > 0 * millimeter)
                ? qCreatedBy(id + "stepInIntersection", EntityType.BODY)
                : qCreatedBy(id + "startIntersect",     EntityType.BODY);

            opLoft(context, id + "loft3", {
                    "profileSubqueries" : [lowerWireQ, stopWireQ],
                    "connections"       : buildLoftConnection(context, lowerWireQ, stopWireQ),
                    "bodyType"          : ToolBodyType.SURFACE
            });
            setProperty(context, { "entities" : qCreatedBy(id + "loft3", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : "SWRout loft 3 (→ stop)" });
        }
    });

/**
 * Trims the SW rout surface at a point on the reference wire.
 * Keeps the larger of the two split bodies (the portion inside the endpoints).
 * Extends the outside edge by cutterRadius and caps the cut end with a 90° revolve.
 */
export function trimSWRout(context is Context, id is Id, swRoutSurface is Query, refPath is Path, trimPoint is Query, sideSheet is Query, cutterRadius is ValueWithUnits, doRevolve is boolean)
{
    // Project trim point onto ref wire to find tangent direction at that location.
    var distResult = evDistance(context, {
            "side0" : trimPoint,
            "side1" : refPath.edges
    });
    var refEdge  = refPath.edges[distResult.sides[1].index];
    var refParam = distResult.sides[1].parameter;
    var edgeLine = evEdgeTangentLine(context, { "edge" : refEdge, "parameter" : refParam });

    // Split the rout surface with a plane normal to the wire tangent at the trim point.
    var splitPlane = plane(edgeLine.origin, edgeLine.direction);
    opSplitPart(context, id + "splitSWRout", {
            "targets" : swRoutSurface,
            "tool"    : splitPlane
    });

    var splitBodyTrue  = qSplitBy(id + "splitSWRout", EntityType.BODY, true);
    var splitBodyFalse = qSplitBy(id + "splitSWRout", EntityType.BODY, false);

    // Keep the larger body (the portion inside the endpoints).
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

    // Extend the outside edge (edge nearest the side surface) by the cutter radius.
    var keptBody      = qCreatedBy(id + "splitSWRout", EntityType.BODY);
    var oneSidedEdges = evaluateQuery(context, qEdgeTopologyFilter(qOwnedByBody(keptBody, EntityType.EDGE), EdgeTopology.ONE_SIDED));
    var outsideEdges  = [];
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

    // Revolve the split edges 90° about the wire tangent axis to cap the rout end.
    if (doRevolve)
    {
        opRevolve(context, id + "capRevolve", {
                "entities"     : qCreatedBy(id + "splitSWRout", EntityType.EDGE),
                "axis"         : line(edgeLine.origin, edgeLine.direction),
                "angleForward" : 90 * degree
        });
        setProperty(context, { "entities" : qCreatedBy(id + "capRevolve", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : "SWRout trim cap" });
    }
}

/**
 * Returns the top boundary wire of the side surface (highest average Z).
 * Used to measure gapDist via evDistance against the bottom sheet.
 * The bottom boundary wire is deleted; caller is responsible for deleting the returned wire.
 */
export function generateDummyTopSurf(context is Context, id is Id, sideSheet is Query, keepBodies is boolean) returns Query
{
    // Extract the two boundary wires of the side surface.
    var oneSidedEdges = qEdgeTopologyFilter(qOwnedByBody(sideSheet, EntityType.EDGE), EdgeTopology.ONE_SIDED);
    opExtractWires(context, id + "extractBoundaryWires", {
            "edges" : oneSidedEdges
    });
    var wireBodies = evaluateQuery(context, qCreatedBy(id + "extractBoundaryWires", EntityType.BODY));

    // Identify top wire by highest average Z (bounding box midpoint).
    var box0  = evBox3d(context, { "topology" : wireBodies[0], "tight" : true });
    var box1  = evBox3d(context, { "topology" : wireBodies[1], "tight" : true });
    var midZ0 = (box0.minCorner[2] + box0.maxCorner[2]) / 2;
    var midZ1 = (box1.minCorner[2] + box1.maxCorner[2]) / 2;
    var topIdx    = (midZ0 > midZ1) ? 0 : 1;
    var bottomIdx = (midZ0 > midZ1) ? 1 : 0;

    setProperty(context, { "entities" : wireBodies[topIdx],    "propertyType" : PropertyType.NAME, "value" : "Side top boundary wire (raw)" });
    setProperty(context, { "entities" : wireBodies[bottomIdx], "propertyType" : PropertyType.NAME, "value" : "Side bottom boundary wire" });

    if (!keepBodies)
    {
        opDeleteBodies(context, id + "deleteBottomBoundaryWire", {
                "entities" : wireBodies[bottomIdx]
        });
    }

    return wireBodies[topIdx];
}

/**
 * Returns a single-entry connections array that aligns the nearest endpoint vertex pair
 * between wireA and wireB. Resolves LOFT_DIRECTION_ERROR caused by opExtractWires
 * assigning an arbitrary traversal direction to each wire body.
 *
 * The connection entry uses only vertex entities (no edge params needed). opLoft processes
 * connectionEdgeQueries/connectionEdgeParameters only for edge entities; empty arrays are
 * valid when connectionEntities contains only vertices.
 *
 * Returns [] if either wire is closed (no vertices) — caller should handle fallback.
 */
function buildLoftConnection(context is Context, wireA is Query, wireB is Query) returns array
{
    var vertsA = evaluateQuery(context, qOwnedByBody(wireA, EntityType.VERTEX));
    var vertsB = evaluateQuery(context, qOwnedByBody(wireB, EntityType.VERTEX));
    if (size(vertsA) == 0 || size(vertsB) == 0)
    {
        return [];
    }

    // Find the nearest vertex pair across the two wires — these are geometrically corresponding
    // endpoints (e.g. both at the tail end), giving the loft a consistent direction reference.
    var minDist = 1e10 * meter;
    var bestA   = vertsA[0];
    var bestB   = vertsB[0];
    for (var va in vertsA)
    {
        for (var vb in vertsB)
        {
            var d = evDistance(context, { "side0" : va, "side1" : vb }).distance;
            if (d < minDist)
            {
                minDist = d;
                bestA   = va;
                bestB   = vb;
            }
        }
    }
    return [{
        "connectionEntities"       : qUnion([bestA, bestB]),
        "connectionEdges"          : [],
        "connectionEdgeParameters" : []
    }];
}

/**
 * Prints the BSplineCurve for each edge in a wire body.
 * Intended for debug use only — gated by definition.debugPrint.
 */
function debugPrintWireBSplines(context is Context, wireBody is Query, label is string, format is PrintFormat)
{
    var edges = evaluateQuery(context, qOwnedByBody(wireBody, EntityType.EDGE));
    println("=== " ~ label ~ " (" ~ size(edges) ~ " edge(s)) ===");
    for (var i = 0; i < size(edges); i += 1)
    {
        var curve = evApproximateBSplineCurve(context, { "edge" : edges[i] });
        printBSpline(curve, format, ["  Edge " ~ toString(i) ~ ":"]);
    }
}
