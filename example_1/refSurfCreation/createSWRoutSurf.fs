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
export const DEBUG_STEP_BOUNDS      = {(unitless)   : [0,   0,  9]} as IntegerBoundSpec;

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
            annotation { "Name" : "Step through (0 = off)", "Default" : 0, "UIHint" : UIHint.SHOW_LABEL }
            isInteger(definition.debugStep, DEBUG_STEP_BOUNDS);

            annotation { "Name" : "Return wires only", "Default" : false }
            definition.debugReturnWires is boolean;

            annotation { "Name" : "Keep all bodies", "Default" : false }
            definition.debugKeepAllBodies is boolean;

            annotation { "Name" : "Print wire BSplines", "Default" : false }
            definition.debugPrintBSplines is boolean;

            annotation { "Name" : "Detailed output", "Default" : false, "UIHint" : UIHint.SHOW_LABEL }
            definition.debugDetailedBSplines is boolean;
        }
    }
    {
        var debugFmt = definition.debugDetailedBSplines ? PrintFormat.DETAILS : PrintFormat.METADATA;

        // --- Get face normals to determine offset directions ---
        // evFaceTangentPlane at parameter (0.5, 0.5) = centre of the face's parameter space.
        // The returned plane's normal is a unitless unit vector pointing away from the face.
        // Multiplying by a length gives a translation vector; opPattern copies the surface
        // without any direct-edit kernel call.
        var bottomNormal = evFaceTangentPlane(context, {
                "face"      : qNthElement(qOwnedByBody(definition.bottomSheet, EntityType.FACE), 0),
                "parameter" : vector(0.5, 0.5)
        }).normal;

        var sideNormal = evFaceTangentPlane(context, {
                "face"      : qNthElement(qOwnedByBody(definition.sideSheet, EntityType.FACE), 0),
                "parameter" : vector(0.5, 0.5)
        }).normal;

        // Guarantee correct orientation: bottomNormal must point up (+Z), sideNormal must point
        // outward (+Y) for the +Y ski half. evFaceTangentPlane direction depends on surface creation
        // order and may be flipped. Correcting here avoids wrong-direction translations downstream.
        if (bottomNormal[2] < 0) { bottomNormal = -bottomNormal; }
        if (sideNormal[1]   < 0) { sideNormal   = -sideNormal; }

        // Pure axis directions used when translating profile wires (step-in, stop).
        // Face normals have cross-axis contamination: the side surface is angled (normal has both Y
        // and Z components), so using sideNormal for a "purely inward" translation also moves the
        // wire vertically. For wire translation we want exact axis offsets.
        const upDir     = vector(0, 0, 1);   // +Z — vertical up
        const outwardDir = vector(0, 1, 0);  // +Y — outward on the +Y ski half

        // =====================================================================
        // Step 1: Offset bottom surface up, intersect with side → start wire
        // =====================================================================
        opPattern(context, id + "startBottom", {
                "entities"      : definition.bottomSheet,
                "transforms"    : [transform(definition.distAboveBottom * bottomNormal)],
                "instanceNames" : ["1"]
        });
        setProperty(context, { "entities" : qCreatedBy(id + "startBottom", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : "Start bottom (offset copy)" });

        opIntersectFaces(context, id + "startIntersect", {
                "tools"   : qOwnedByBody(qCreatedBy(id + "startBottom", EntityType.BODY), EntityType.FACE),
                "targets" : qOwnedByBody(definition.sideSheet, EntityType.FACE)
        });
        setProperty(context, { "entities" : qCreatedBy(id + "startIntersect", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : "Start intersection" });

        opExtractWires(context, id + "startWireExtract", {
                "edges" : qCreatedBy(id + "startIntersect", EntityType.EDGE)
        });
        var startWire = qCreatedBy(id + "startWireExtract", EntityType.BODY);
        setProperty(context, { "entities" : startWire, "propertyType" : PropertyType.NAME, "value" : "Start wire (pre-split)" });

        if (!definition.debugKeepAllBodies)
        {
            opDeleteBodies(context, id + "deleteStartCopies", {
                    "entities" : qUnion([qCreatedBy(id + "startBottom",    EntityType.BODY),
                                         qCreatedBy(id + "startIntersect", EntityType.BODY)])
            });
        }

        opSplitPart(context, id + "splitStartWire", {
                "targets"  : startWire,
                "tool"     : qFrontPlane(EntityType.FACE),
                "keepType" : SplitOperationKeepType.KEEP_BACK
        });
        setProperty(context, { "entities" : startWire, "propertyType" : PropertyType.NAME, "value" : "SWRout start wire" });

        if (definition.debugPrintBSplines)
            debugPrintWireBSplines(context, startWire, "Start wire", debugFmt);

        if (definition.debugStep == 1) return;

        // =====================================================================
        // Step 2: (Optional) offset side surface inward → step-in wire
        // =====================================================================
        var hasStepIn = definition.swRoutStepin > 0 * millimeter;
        var stepInWire = qNothing();
        if (hasStepIn)
        {
            opPattern(context, id + "stepInWirePat", {
                    "entities"      : startWire,
                    "transforms"    : [transform(-definition.swRoutStepin * outwardDir)],
                    "instanceNames" : ["1"]
            });
            stepInWire = qCreatedBy(id + "stepInWirePat", EntityType.BODY);
            setProperty(context, { "entities" : stepInWire, "propertyType" : PropertyType.NAME, "value" : "SWRout step-in wire" });

            if (definition.debugPrintBSplines)
                debugPrintWireBSplines(context, stepInWire, "Step-in wire", debugFmt);
        }

        if (definition.debugStep == 2) return;

        // =====================================================================
        // Step 3: Measure side height, offset start wire up+inward → stop wire
        // =====================================================================
        var dummyTopSurf = generateDummyTopSurf(context, id + "dummyTop", definition.sideSheet, definition.debugKeepAllBodies);
        setProperty(context, { "entities" : dummyTopSurf, "propertyType" : PropertyType.NAME, "value" : "Side top boundary wire" });
        var gapDist = evDistance(context, {
                "side0" : dummyTopSurf,
                "side1" : definition.bottomSheet
        }).distance;

        if (definition.debugPrintBSplines)
        {
            debugPrintWireBSplines(context, dummyTopSurf, "Dummy top wire", debugFmt);
            println("  gapDist = " ~ toString(gapDist));
        }

        if (!definition.debugKeepAllBodies)
        {
            opDeleteBodies(context, id + "deleteDummyTop", { "entities" : dummyTopSurf });
        }

        var routHeight = gapDist - definition.distAboveBottom + 2 * millimeter;
        var routOffset = routHeight * tan(definition.swRoutAngle);

        opPattern(context, id + "stopWirePat", {
                "entities"      : startWire,
                "transforms"    : [transform(routHeight * upDir - routOffset * outwardDir)],
                "instanceNames" : ["1"]
        });
        var stopWire = qCreatedBy(id + "stopWirePat", EntityType.BODY);
        setProperty(context, { "entities" : stopWire, "propertyType" : PropertyType.NAME, "value" : "SWRout stop wire" });

        if (definition.debugPrintBSplines)
            debugPrintWireBSplines(context, stopWire, "Stop wire", debugFmt);

        // debugReturnWires: leave all three profile wires in place and exit.
        if (definition.debugReturnWires || definition.debugStep == 3) return;

        // =====================================================================
        // Step 4: First loft (stop → stepIn, or stop → start if no step-in)
        // =====================================================================
        var profile1B = hasStepIn ? stepInWire : startWire;
        opLoft(context, id + "loft1", {
                "profileSubqueries" : [stopWire, profile1B],
                "connections"       : buildLoftConnection(context, stopWire, profile1B),
                "bodyType"          : ToolBodyType.SURFACE
        });
        setProperty(context, { "entities" : qCreatedBy(id + "loft1", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : "SWRout loft 1" });

        if (definition.debugStep == 4) return;

        // =====================================================================
        // Step 5: Second loft (stepIn → start) + union; no-op if no step-in
        // =====================================================================
        if (hasStepIn)
        {
            opLoft(context, id + "loft2", {
                    "profileSubqueries" : [stepInWire, startWire],
                    "connections"       : buildLoftConnection(context, stepInWire, startWire),
                    "bodyType"          : ToolBodyType.SURFACE
            });
            setProperty(context, { "entities" : qCreatedBy(id + "loft2", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : "SWRout loft 2" });
            // loft1 body identity is preserved through the union (first tool survives opBoolean UNION)
            opBoolean(context, id + "combineSurfs", {
                    "tools"         : qUnion([qCreatedBy(id + "loft1", EntityType.BODY), qCreatedBy(id + "loft2", EntityType.BODY)]),
                    "operationType" : BooleanOperationType.UNION
            });
        }

        var loftBody = qCreatedBy(id + "loft1", EntityType.BODY);
        setProperty(context, { "entities" : loftBody, "propertyType" : PropertyType.NAME, "value" : "SWRout surface" });

        if (!definition.debugKeepAllBodies)
        {
            var wireBodiesToDelete = hasStepIn
                ? qUnion([startWire, stepInWire, stopWire])
                : qUnion([startWire, stopWire]);
            opDeleteBodies(context, id + "deleteProfileWires", { "entities" : wireBodiesToDelete });
        }

        // --- Find outside edges and extend ---
        var loftOneSidedEdges = evaluateQuery(context, qEdgeTopologyFilter(qOwnedByBody(loftBody, EntityType.EDGE), EdgeTopology.ONE_SIDED));
        var outsideEdges = [];
        for (var edge in loftOneSidedEdges)
        {
            var midPoint   = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 0.5 }).origin;
            var distToSide = evDistance(context, { "side0" : midPoint, "side1" : definition.sideSheet }).distance;
            if (distToSide < 1e-5 * meter)
                outsideEdges = append(outsideEdges, edge);
        }
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

        if (definition.debugStep == 5) return;

        // =====================================================================
        // Steps 6–9: Endpoint trimming (only if specSWRoutEndpoints)
        // Step 6: trim at start point  Step 7: revolve cap at start
        // Step 8: trim at stop point   Step 9: revolve cap at stop
        // =====================================================================
        if (definition.specSWRoutEndpoints)
        {
            var refWirePath = constructPath(context, qOwnedByBody(definition.refWire, EntityType.EDGE));

            if (!isQueryEmpty(context, definition.startPointQ))
            {
                // Step 6 stops before the revolve; step 7+ includes it.
                var doStartRevolve = (definition.debugStep == 0 || definition.debugStep >= 7);
                trimSWRout(context, id + "trimStart", loftBody, refWirePath, definition.startPointQ, definition.sideSheet, definition.cutterBottomRadius, doStartRevolve);
                if (definition.debugStep == 6 || definition.debugStep == 7) return;
            }

            if (!isQueryEmpty(context, definition.stopPointQ))
            {
                // Step 8 stops before the revolve; step 9+ includes it.
                var doStopRevolve = (definition.debugStep == 0 || definition.debugStep >= 9);
                trimSWRout(context, id + "trimStop", loftBody, refWirePath, definition.stopPointQ, definition.sideSheet, definition.cutterBottomRadius, doStopRevolve);
                // step 8 returns; step 9 (or 0) falls through to end.
                if (definition.debugStep == 8) return;
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
        return [];

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
