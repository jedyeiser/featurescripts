FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");
import(path : "onshape/std/extend.fs", version : "2892.0");
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

export const SWRoutAngleBounds      = {(degree)     : [0,  20, 45]} as AngleBoundSpec;
export const DistAboveBottomBounds  = {(millimeter) : [1,   4, 10]} as LengthBoundSpec;
export const SWStepInBounds         = {(millimeter) : [0,   0,  3]} as LengthBoundSpec;
export const cutterRadiusBounds     = {(millimeter) : [2,  10, 20]} as LengthBoundSpec;

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

        annotation { "Name" : "SW rout step-in" }
        isLength(definition.swRoutStepin, SWStepInBounds);

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
            annotation { "Name" : "Print wire BSplines", "Default" : false }
            definition.debugPrintBSplines is boolean;

            annotation { "Name" : "Detailed output", "Default" : false, "UIHint" : UIHint.SHOW_LABEL }
            definition.debugDetailedBSplines is boolean;
        }
    }
    {
        var debugFmt = definition.debugDetailedBSplines ? PrintFormat.DETAILS : PrintFormat.METADATA;

        // --- Determine offset direction signs via probe extracts ---
        // opExtractSurface creates a fresh offset body without modifying the original.
        // Bottom: correct direction = increasing Z (upward).
        opExtractSurface(context, id + "bottomDirProbe", {
                "faces"  : qOwnedByBody(definition.bottomSheet, EntityType.FACE),
                "offset" : 1 * millimeter,
                "useFacesAroundToTrimOffset" : false
        });
        var initialBottomZ = evBox3d(context, { "topology" : definition.bottomSheet,                              "tight" : true }).minCorner[2];
        var probeBottomZ   = evBox3d(context, { "topology" : qCreatedBy(id + "bottomDirProbe", EntityType.BODY), "tight" : true }).minCorner[2];
        var bottomDirSign  = (probeBottomZ > initialBottomZ) ? 1 : -1;
        opDeleteBodies(context, id + "deleteBottomDirProbe", { "entities" : qCreatedBy(id + "bottomDirProbe", EntityType.BODY) });

        // Side: correct direction = increasing Y span (outward).
        opExtractSurface(context, id + "sideDirProbe", {
                "faces"  : qOwnedByBody(definition.sideSheet, EntityType.FACE),
                "offset" : 1 * millimeter,
                "useFacesAroundToTrimOffset" : false
        });
        var initialSideBox  = evBox3d(context, { "topology" : definition.sideSheet,                            "tight" : true });
        var initialSideSpan = initialSideBox.maxCorner[1] - initialSideBox.minCorner[1];
        var probeSideBox    = evBox3d(context, { "topology" : qCreatedBy(id + "sideDirProbe", EntityType.BODY), "tight" : true });
        var probeSideSpan   = probeSideBox.maxCorner[1] - probeSideBox.minCorner[1];
        var sideDirSign     = (probeSideSpan > initialSideSpan) ? 1 : -1;
        opDeleteBodies(context, id + "deleteSideDirProbe", { "entities" : qCreatedBy(id + "sideDirProbe", EntityType.BODY) });

        // --- Start wire: bottom at distAboveBottom, side at original position ---
        opExtractSurface(context, id + "startBottom", {
                "faces"  : qOwnedByBody(definition.bottomSheet, EntityType.FACE),
                "offset" : bottomDirSign * definition.distAboveBottom,
                "useFacesAroundToTrimOffset" : false
        });
        opIntersectFaces(context, id + "startIntersect", {
                "tools"   : qOwnedByBody(qCreatedBy(id + "startBottom", EntityType.BODY), EntityType.FACE),
                "targets" : qOwnedByBody(definition.sideSheet, EntityType.FACE)
        });
        opExtractWires(context, id + "startWireExtract", {
                "edges" : qCreatedBy(id + "startIntersect", EntityType.EDGE)
        });
        var startWire = qCreatedBy(id + "startWireExtract", EntityType.BODY);
        opDeleteBodies(context, id + "deleteStartExtract", {
                "entities" : qUnion([qCreatedBy(id + "startBottom",    EntityType.BODY),
                                     qCreatedBy(id + "startIntersect", EntityType.BODY)])
        });

        if (definition.debugPrintBSplines)
        {
            debugPrintWireBSplines(context, startWire, "Start wire", debugFmt);
        }

        // --- Measure total side height via top boundary wire ---
        var dummyTopSurf = generateDummyTopSurf(context, id + "dummyTop", definition.sideSheet);
        var gapDist = evDistance(context, {
                "side0" : dummyTopSurf,
                "side1" : definition.bottomSheet
        }).distance;

        if (definition.debugPrintBSplines)
        {
            debugPrintWireBSplines(context, dummyTopSurf, "Dummy top wire", debugFmt);
            println("  gapDist = " ~ toString(gapDist));
        }

        opDeleteBodies(context, id + "deleteDummyTop", {
                "entities" : dummyTopSurf
        });

        // --- Optional step-in wire: side inward by swRoutStepin, bottom at distAboveBottom ---
        var stepInWire = qNothing();
        if (definition.swRoutStepin > 0 * millimeter)
        {
            opExtractSurface(context, id + "stepInSide", {
                    "faces"  : qOwnedByBody(definition.sideSheet, EntityType.FACE),
                    "offset" : -sideDirSign * definition.swRoutStepin,
                    "useFacesAroundToTrimOffset" : false
            });
            opExtractSurface(context, id + "stepInBottom", {
                    "faces"  : qOwnedByBody(definition.bottomSheet, EntityType.FACE),
                    "offset" : bottomDirSign * definition.distAboveBottom,
                    "useFacesAroundToTrimOffset" : false
            });
            opIntersectFaces(context, id + "stepInIntersect", {
                    "tools"   : qOwnedByBody(qCreatedBy(id + "stepInSide",   EntityType.BODY), EntityType.FACE),
                    "targets" : qOwnedByBody(qCreatedBy(id + "stepInBottom", EntityType.BODY), EntityType.FACE)
            });
            opExtractWires(context, id + "stepInWireExtract", {
                    "edges" : qCreatedBy(id + "stepInIntersect", EntityType.EDGE)
            });
            stepInWire = qCreatedBy(id + "stepInWireExtract", EntityType.BODY);
            opDeleteBodies(context, id + "deleteStepInExtract", {
                    "entities" : qUnion([qCreatedBy(id + "stepInSide",      EntityType.BODY),
                                         qCreatedBy(id + "stepInBottom",    EntityType.BODY),
                                         qCreatedBy(id + "stepInIntersect", EntityType.BODY)])
            });

            if (definition.debugPrintBSplines)
            {
                debugPrintWireBSplines(context, stepInWire, "Step-in wire", debugFmt);
            }
        }

        // --- Stop wire: bottom at distAboveBottom + routHeight, side outward by routOffset ---
        // routHeight overshoots by 2 mm; trimmed later by external top reference surface.
        var routHeight = gapDist - definition.distAboveBottom + 2 * millimeter;
        var routOffset = routHeight * tan(definition.swRoutAngle);

        opExtractSurface(context, id + "stopBottom", {
                "faces"  : qOwnedByBody(definition.bottomSheet, EntityType.FACE),
                "offset" : bottomDirSign * (definition.distAboveBottom + routHeight),
                "useFacesAroundToTrimOffset" : false
        });
        opExtractSurface(context, id + "stopSide", {
                "faces"  : qOwnedByBody(definition.sideSheet, EntityType.FACE),
                "offset" : sideDirSign * routOffset,
                "useFacesAroundToTrimOffset" : false
        });
        opIntersectFaces(context, id + "stopIntersect", {
                "tools"   : qOwnedByBody(qCreatedBy(id + "stopBottom", EntityType.BODY), EntityType.FACE),
                "targets" : qOwnedByBody(qCreatedBy(id + "stopSide",   EntityType.BODY), EntityType.FACE)
        });
        opExtractWires(context, id + "stopWireExtract", {
                "edges" : qCreatedBy(id + "stopIntersect", EntityType.EDGE)
        });
        var stopWire = qCreatedBy(id + "stopWireExtract", EntityType.BODY);
        opDeleteBodies(context, id + "deleteStopExtract", {
                "entities" : qUnion([qCreatedBy(id + "stopBottom",    EntityType.BODY),
                                     qCreatedBy(id + "stopSide",      EntityType.BODY),
                                     qCreatedBy(id + "stopIntersect", EntityType.BODY)])
        });

        if (definition.debugPrintBSplines)
        {
            debugPrintWireBSplines(context, stopWire, "Stop wire", debugFmt);
        }

        // --- Split all wires at front plane; keep +Y half ---
        opSplitPart(context, id + "wireSplit", {
                "targets"  : qUnion([startWire, stepInWire, stopWire]),
                "tool"     : qFrontPlane(EntityType.FACE),
                "keepType" : SplitOperationKeepType.KEEP_FRONT
        });

        // --- Loft the rout surface ---
        var hasStepIn = definition.swRoutStepin > 0 * millimeter;
        opLoft(context, id + "loft1", {
                "profileSubqueries" : [stopWire, hasStepIn ? stepInWire : startWire],
                "connections"       : [],
                "bodyType"          : ToolBodyType.SURFACE
        });
        if (hasStepIn)
        {
            opLoft(context, id + "loft2", {
                    "profileSubqueries" : [stepInWire, startWire],
                    "connections"       : [],
                    "bodyType"          : ToolBodyType.SURFACE
            });
            // loft1 body identity is preserved through the union (first tool survives opBoolean UNION)
            opBoolean(context, id + "combineSurfs", {
                    "tools"         : qUnion([qCreatedBy(id + "loft1", EntityType.BODY), qCreatedBy(id + "loft2", EntityType.BODY)]),
                    "operationType" : BooleanOperationType.UNION
            });
        }

        var loftBody = qCreatedBy(id + "loft1", EntityType.BODY);

        // --- Find outside edges (those coincident with the original side surface) ---
        var loftOneSidedEdges = evaluateQuery(context, qEdgeTopologyFilter(qOwnedByBody(loftBody, EntityType.EDGE), EdgeTopology.ONE_SIDED));
        var outsideEdges = [];
        for (var edge in loftOneSidedEdges)
        {
            var midPoint   = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 0.5 }).origin;
            var distToSide = evDistance(context, { "side0" : midPoint, "side1" : definition.sideSheet }).distance;
            if (distToSide < 1e-5 * meter)
            {
                outsideEdges = append(outsideEdges, edge);
            }
        }

        // Extend outside edges — by cutter radius if endpoints are specified, else by bottomExtension.
        var extendDistance = definition.specSWRoutEndpoints ? definition.cutterBottomRadius : definition.bottomExtension;
        if (extendDistance > 0 * millimeter)
        {
            extendSurface(context, id + "extendOutside", {
                    "entities"           : qUnion(outsideEdges),
                    "tangentPropagation" : true,
                    "endCondition"       : ExtendBoundingType.BLIND,
                    "oppositeDirection"  : false,
                    "extendDistance"     : extendDistance,
                    "maintainCurvature"  : true
            });
        }

        // --- Endpoint trimming ---
        if (definition.specSWRoutEndpoints)
        {
            var refWirePath = constructPath(context, qOwnedByBody(definition.refWire, EntityType.EDGE));

            if (!isQueryEmpty(context, definition.startPointQ))
            {
                trimSWRout(context, id + "trimStart", loftBody, refWirePath, definition.startPointQ, definition.sideSheet, definition.cutterBottomRadius);
            }
            if (!isQueryEmpty(context, definition.stopPointQ))
            {
                trimSWRout(context, id + "trimStop", loftBody, refWirePath, definition.stopPointQ, definition.sideSheet, definition.cutterBottomRadius);
            }
        }

        // Top trim deferred — a top reference surface (projection of side top edges onto XZ plane)
        // will be used to remove the 2 mm overshoot once available.
    });

/**
 * Trims the SW rout surface at a point on the reference wire.
 * Keeps the larger of the two split bodies (the portion inside the endpoints).
 * Extends the outside edge by cutterRadius and caps the cut end with a 90° revolve.
 */
export function trimSWRout(context is Context, id is Id, swRoutSurface is Query, refPath is Path, trimPoint is Query, sideSheet is Query, cutterRadius is ValueWithUnits)
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
    opRevolve(context, id + "capRevolve", {
            "entities"     : qCreatedBy(id + "splitSWRout", EntityType.EDGE),
            "axis"         : line(edgeLine.origin, edgeLine.direction),
            "angleForward" : 90 * degree
    });
}

/**
 * Returns the top boundary wire of the side surface (highest average Z).
 * Used to measure gapDist via evDistance against the bottom sheet.
 * The bottom boundary wire is deleted; caller is responsible for deleting the returned wire.
 */
export function generateDummyTopSurf(context is Context, id is Id, sideSheet is Query) returns Query
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

    // Delete the bottom boundary wire; return the top wire for gapDist measurement.
    opDeleteBodies(context, id + "deleteBottomBoundaryWire", {
            "entities" : wireBodies[bottomIdx]
    });

    return wireBodies[topIdx];
}

/**
 * Prints the BSplineCurve for each edge in a wire body.
 * Intended for debug use only — gated by definition.debugPrintBSplines.
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
