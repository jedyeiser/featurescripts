FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");
import(path : "onshape/std/extend.fs", version : "2892.0");
// IMPORT: tools/printing.fs

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

        // --- Working copies (deleted before feature exits) ---
        opPattern(context, id + "copyBottom", {
                "entities"      : definition.bottomSheet,
                "transforms"    : [identityTransform()],
                "instanceNames" : ["1"]
        });
        opPattern(context, id + "copySide", {
                "entities"      : definition.sideSheet,
                "transforms"    : [identityTransform()],
                "instanceNames" : ["1"]
        });

        var bottomCopy = qCreatedBy(id + "copyBottom", EntityType.BODY);
        var sideCopy   = qCreatedBy(id + "copySide",   EntityType.BODY);

        // Determine correct offset directions; bottom copy is left at distAboveBottom.
        var dirMap        = processFirstMoves(context, id + "firstMoves", definition, bottomCopy, sideCopy);
        var bottomDirSign = dirMap.bottomDirSign;
        var sideDirSign   = dirMap.sideDirSign;

        // --- Start wire: intersection at distAboveBottom (bottom already there after processFirstMoves) ---
        opIntersectFaces(context, id + "startIntersect", {
                "tools"   : qOwnedByBody(bottomCopy, EntityType.FACE),
                "targets" : qOwnedByBody(sideCopy,   EntityType.FACE)
        });
        opExtractWires(context, id + "startWireExtract", {
                "edges" : qCreatedBy(id + "startIntersect", EntityType.EDGE)
        });
        var startWire = qCreatedBy(id + "startWireExtract", EntityType.BODY);
        opDeleteBodies(context, id + "deleteStartIntersect", {
                "entities" : qCreatedBy(id + "startIntersect", EntityType.BODY)
        });

        if (definition.debugPrintBSplines)
        {
            debugPrintWireBSplines(context, startWire, "Start wire", debugFmt);
        }

        // --- Measure total side height via dummy top surface, then delete it ---
        var dummyTopSurf = generateDummyTopSurf(context, id + "dummyTop", definition.sideSheet, bottomDirSign);
        var gapDist = evDistance(context, {
                "side0" : dummyTopSurf,
                "side1" : definition.bottomSheet
        }).distance;

        if (definition.debugPrintBSplines)
        {
            var dummyEdges = evaluateQuery(context, qOwnedByBody(dummyTopSurf, EntityType.EDGE));
            println("=== Dummy top spline (" ~ size(dummyEdges) ~ " edge(s)) ===");
            for (var i = 0; i < size(dummyEdges); i += 1)
            {
                var curve = evApproximateBSplineCurve(context, { "edge" : dummyEdges[i] });
                printBSpline(curve, debugFmt, ["  Edge " ~ toString(i) ~ ":"]);
            }
            println("  gapDist = " ~ toString(gapDist));
        }

        opDeleteBodies(context, id + "deleteDummyTop", {
                "entities" : dummyTopSurf
        });

        // --- Optional step-in wire ---
        var stepInWire = qNothing();
        if (definition.swRoutStepin > 0 * millimeter)
        {
            opOffsetFace(context, id + "stepInOffset", {
                    "moveFaces"      : qOwnedByBody(sideCopy, EntityType.FACE),
                    "offsetDistance" : sideDirSign * definition.swRoutStepin
            });
            opIntersectFaces(context, id + "stepInIntersect", {
                    "tools"   : qOwnedByBody(sideCopy,   EntityType.FACE),
                    "targets" : qOwnedByBody(bottomCopy, EntityType.FACE)
            });
            opExtractWires(context, id + "stepInWireExtract", {
                    "edges" : qCreatedBy(id + "stepInIntersect", EntityType.EDGE)
            });
            opDeleteBodies(context, id + "deleteStepInIntersect", {
                    "entities" : qCreatedBy(id + "stepInIntersect", EntityType.BODY)
            });
            stepInWire = qCreatedBy(id + "stepInWireExtract", EntityType.BODY);

            if (definition.debugPrintBSplines)
            {
                debugPrintWireBSplines(context, stepInWire, "Step-in wire", debugFmt);
            }
        }

        // --- Stop wire: offset to full rout height + angle ---
        // routHeight overshoots by 2 mm; trimmed later by external top reference surface.
        var routHeight = gapDist - definition.distAboveBottom + 2 * millimeter;
        var routOffset = routHeight * tan(definition.swRoutAngle);

        opOffsetFace(context, id + "finalBottomOffset", {
                "moveFaces"      : qOwnedByBody(bottomCopy, EntityType.FACE),
                "offsetDistance" : bottomDirSign * routHeight
        });
        opOffsetFace(context, id + "finalSideOffset", {
                "moveFaces"      : qOwnedByBody(sideCopy, EntityType.FACE),
                "offsetDistance" : sideDirSign * routOffset
        });
        opIntersectFaces(context, id + "stopIntersect", {
                "tools"   : qOwnedByBody(bottomCopy, EntityType.FACE),
                "targets" : qOwnedByBody(sideCopy,   EntityType.FACE)
        });
        opExtractWires(context, id + "stopWireExtract", {
                "edges" : qCreatedBy(id + "stopIntersect", EntityType.EDGE)
        });
        var stopWire = qCreatedBy(id + "stopWireExtract", EntityType.BODY);
        opDeleteBodies(context, id + "deleteStopIntersect", {
                "entities" : qCreatedBy(id + "stopIntersect", EntityType.BODY)
        });

        if (definition.debugPrintBSplines)
        {
            debugPrintWireBSplines(context, stopWire, "Stop wire", debugFmt);
        }

        // --- Clean up working copies ---
        opDeleteBodies(context, id + "deleteWorkerCopies", {
                "entities" : qUnion([bottomCopy, sideCopy])
        });

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
 * Finds the top boundary wire of the side surface (highest average Z), projects its
 * points onto the XZ plane (Y = 0), and returns an approximated spline wire body.
 * Used to measure gapDist (total side height) and as a future top-trim reference.
 */
export function generateDummyTopSurf(context is Context, id is Id, sideSheet is Query, bottomDirSign is number) returns Query
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
    var topWireBody = (midZ0 > midZ1) ? wireBodies[0] : wireBodies[1];

    // Order the top wire edges and sample uniformly along the full path.
    // Using constructPath + evPathTangentLines avoids duplicate junction points
    // that would arise from sampling each edge independently at t=0 and t=1.
    var topPath = constructPath(context, qOwnedByBody(topWireBody, EntityType.EDGE));
    var numSamples = 30;
    var params = [];
    for (var i = 0; i <= numSamples; i += 1)
    {
        params = append(params, i / numSamples);
    }
    var tangentResult    = evPathTangentLines(context, topPath, params);
    var projectedPoints  = [];
    for (var tl in tangentResult.tangentLines)
    {
        var pt = tl.origin;
        projectedPoints = append(projectedPoints, vector(pt[0], 0 * meter, pt[2]));
    }

    // Clean up boundary wires.
    opDeleteBodies(context, id + "deleteBoundaryWires", {
            "entities" : qCreatedBy(id + "extractBoundaryWires", EntityType.BODY)
    });

    // Fit a spline through the projected points and create a wire body.
    var splineCurve = approximateSpline(context, {
            "targets"          : [approximationTarget({ "positions" : projectedPoints })],
            "degree"           : 3,
            "tolerance"        : 1e-4 * meter,
            "isPeriodic"       : false,
            "maxControlPoints" : 200
    })[0];

    opCreateBSplineCurve(context, id + "dummyTopSpline", {
            "bSplineCurve" : splineCurve
    });

    return qCreatedBy(id + "dummyTopSpline", EntityType.BODY);
}

/**
 * Determines correct offset direction signs for the bottom and side working copies.
 *
 * Probes each copy with a +1 offset, checks whether geometry moved in the intended
 * direction (bottom up = increasing Z, side outward = increasing Y span), flips if not.
 *
 * Side copy is returned to its original position — the start wire intersection fires
 * immediately after and needs the side at zero.
 *
 * Bottom copy is left at +distAboveBottom (correct direction) — the start wire
 * intersection fires immediately after and needs the bottom already elevated.
 * The final profile offset adds routHeight on top of this accumulated offset.
 */
export function processFirstMoves(context is Context, id is Id, definition is map, toDeleteBottomBody is Query, toDeleteSideBody is Query) returns map
{
    var bottomDirSign = 1;
    var sideDirSign   = 1;

    var initialBottomBoxZ = evBox3d(context, { "topology" : definition.bottomSheet, "tight" : true }).minCorner[2];
    var initialSideBox    = evBox3d(context, { "topology" : definition.sideSheet,   "tight" : true });
    var initialSideWidth  = initialSideBox.maxCorner[1] - initialSideBox.minCorner[1];

    // Probe both copies with a positive offset.
    opOffsetFace(context, id + "initialBottomOffset", {
            "moveFaces"      : qOwnedByBody(toDeleteBottomBody, EntityType.FACE),
            "offsetDistance" : definition.distAboveBottom
    });
    opOffsetFace(context, id + "initialSideOffset", {
            "moveFaces"      : qOwnedByBody(toDeleteSideBody, EntityType.FACE),
            "offsetDistance" : definition.distAboveBottom
    });

    var newBottomBoxZ = evBox3d(context, { "topology" : toDeleteBottomBody, "tight" : true }).minCorner[2];
    var newSideBox    = evBox3d(context, { "topology" : toDeleteSideBody,   "tight" : true });
    var newSideWidth  = newSideBox.maxCorner[1] - newSideBox.minCorner[1];

    // Bottom: if Z decreased, probe went downward. Flip and move 2x to end at +distAboveBottom.
    if (newBottomBoxZ < initialBottomBoxZ)
    {
        bottomDirSign = -1;
        opOffsetFace(context, id + "initialBottomOffsetFix", {
                "moveFaces"      : qOwnedByBody(toDeleteBottomBody, EntityType.FACE),
                "offsetDistance" : -2 * definition.distAboveBottom
        });
    }

    // Side: return to original position regardless. Flip sign if probe went inward.
    if (newSideWidth < initialSideWidth)
    {
        sideDirSign = -1;
        opOffsetFace(context, id + "initialSideOffsetFix", {
                "moveFaces"      : qOwnedByBody(toDeleteSideBody, EntityType.FACE),
                "offsetDistance" : -definition.distAboveBottom
        });
    }
    else
    {
        opOffsetFace(context, id + "initialSideOffsetRevert", {
                "moveFaces"      : qOwnedByBody(toDeleteSideBody, EntityType.FACE),
                "offsetDistance" : -definition.distAboveBottom
        });
    }

    return { "bottomDirSign" : bottomDirSign, "sideDirSign" : sideDirSign };
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
