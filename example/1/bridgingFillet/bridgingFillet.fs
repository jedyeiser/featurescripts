FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// IMPORT: bridgingFilletUtils.fs
// IMPORT: tools/bspline_data.fs
// IMPORT: tools/arc_length.fs
import(path : "onshape/std/bridgingCurve.fs", version : "2892.0");

/**
 * Bridging fillet creates a bridging curve or surface between two input curves or two faces.
 *
 * Two queries for each entity type, each with a max limit of 1.
 * Choose input type {CURVES, FACES} in a horizontal enum.
 *
 * Find intersection vertex of curves, or intersection edge(s) of faces.
 * User specifies an offset distance — where bridging begins for each element.
 * User can toggle a boolean to add a different side 2 offset.
 *
 * User specifies side 1 and side 2 continuity (G1 or G2).
 * Feature auto-solves for bridging curve direction/flip (bridging entrances face each other).
 *
 * Method:
 *   BRIDGING — Hermite bridging curve (cubic G1, quintic G2) via computeBridgingControlPoints.
 *   CONIC    — Rational quadratic Bézier arc or rho-conic.
 *
 * If inputs are faces: return either 1) bridging surface only, 2) trimmed faces + bridging, or 3) joined.
 * If inputs are curves: return new wire with bridge + trimmed input wires.
 *
 * Debug options: print intersection/bridge metadata, show offsets, show junction frame.
 */


// ---------------------------------------------------------------------------
// Enums
// ---------------------------------------------------------------------------

/**
 * Selects whether inputs are edge/wire curves or solid/sheet faces.
 * @value CURVES : Input is two edge or wire body curves.
 * @value FACES : Input is two solid or sheet faces.
 */
export enum BridgingFilletInputType
{
    annotation { "Name" : "Curves" }
    CURVES,
    annotation { "Name" : "Faces" }
    FACES
}

/**
 * Selects the bridging construction method.
 * @value BRIDGING : Hermite bridging curve (cubic for G1, quintic for G2) via computeBridgingControlPoints.
 * @value CONIC : Rational quadratic Bézier arc or rho-conic.
 */
export enum BridgingFilletMethod
{
    annotation { "Name" : "Bridging curve" }
    BRIDGING,
    annotation { "Name" : "Conic" }
    CONIC
}

/**
 * Controls the conic section type used for conic bridging profiles.
 * @value ARC : Rational quadratic Bézier with w = cos(θ/2), producing a true circular arc (P&T §7.3).
 * @value RHO : User-specified shape factor ρ ∈ (0,1): ρ=0.5 → parabola, ρ<0.5 → ellipse, ρ>0.5 → hyperbola (Farin CAGD §8.5).
 */
export enum BridgingFilletConicType
{
    annotation { "Name" : "Arc" }
    ARC,
    annotation { "Name" : "Rho" }
    RHO
}

/**
 * Specifies geometric continuity at each bridging boundary.
 * @value G1 : Tangent continuity — requires cubic Hermite (4 DOF, P&T §9.1).
 * @value G2 : Curvature-matched continuity — requires degree ≥ 5 (6 DOF, P&T §9.1).
 */
export enum BridgingFilletContinuityType
{
    annotation { "Name" : "G1 (tangent)" }
    G1,
    annotation { "Name" : "G2 (curvature)" }
    G2
}

/**
 * Controls what geometry is produced and how seed faces are handled.
 * @value BRIDGE_ONLY : Return only the bridging surface; seed faces are unchanged.
 * @value TRIMMED_FACES : Trim each seed face back to its offset curve and return all three surfaces.
 * @value JOINED_FACES : Trim seed faces and boolean-join with bridging surface into one body.
 */
export enum BridgingFilletOutputType
{
    annotation { "Name" : "Bridge only" }
    BRIDGE_ONLY,
    annotation { "Name" : "Trimmed faces" }
    TRIMMED_FACES,
    annotation { "Name" : "Joined faces" }
    JOINED_FACES
}


// ---------------------------------------------------------------------------
// Bounds constants
// ---------------------------------------------------------------------------

/**
 * Zero-inclusive offset distance: offset=0 means bridging starts exactly at the reference edge itself.
 */
export const OFFSET_DISTANCE_BOUNDS =
{
    (millimeter) : [0, 5, 1000]
} as LengthBoundSpec;

/**
 * Rho shape factor: ρ=0.5 → parabola, ρ<0.5 → ellipse, ρ>0.5 → hyperbola (Farin CAGD §8.5).
 */
export const RHO_BOUNDS =
{
    (unitless) : [0.001, 0.5, 0.999]
} as RealBoundSpec;


// ---------------------------------------------------------------------------
// Feature definition
// ---------------------------------------------------------------------------

annotation { "Feature Type Name" : "Bridging fillet",
        "Feature Type Description" : "Creates a bridging filleted surface between two faces, or two curves",
        "Editing Logic Function" : "bridgingFilletEditingLogic",
        "UIHint" : UIHint.NO_PREVIEW_PROVIDED }
export const bridgingFillet = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        // --- Input type ---
        annotation { "Name" : "Input type", "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.REMEMBER_PREVIOUS_VALUE] }
        definition.inputType is BridgingFilletInputType;

        // --- Selectors (conditional on inputType) ---
        if (definition.inputType == BridgingFilletInputType.CURVES)
        {
            annotation { "Name" : "Side 1", "Filter" : EntityType.EDGE || BodyType.WIRE, "MaxNumberOfPicks" : 1 }
            definition.side1Curves is Query;

            annotation { "Name" : "Side 2", "Filter" : EntityType.EDGE || BodyType.WIRE, "MaxNumberOfPicks" : 1 }
            definition.side2Curves is Query;
        }
        if (definition.inputType == BridgingFilletInputType.FACES)
        {
            annotation { "Name" : "Side 1 face", "Filter" : EntityType.FACE && ConstructionObject.NO, "MaxNumberOfPicks" : 1 }
            definition.side1Face is Query;

            annotation { "Name" : "Side 1 reference edge",
                         "Description" : "Reference edge on face 1 - where offset is measured from. Auto-populated for adjacent faces.",
                         "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 1 }
            definition.side1Edge is Query;

            annotation { "Name" : "Side 2 face", "Column Name" : "Second face",
                         "Filter" : EntityType.FACE && ConstructionObject.NO, "MaxNumberOfPicks" : 1 }
            definition.side2Face is Query;

            annotation { "Name" : "Side 2 reference edge", "Column Name" : "Second reference edge",
                         "Description" : "Reference edge on face 2 - where offset is measured from. Auto-populated for adjacent faces.",
                         "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 1 }
            definition.side2Edge is Query;
        }

        // --- Offset ---
        annotation { "Name" : "Offset" }
        isLength(definition.offset, OFFSET_DISTANCE_BOUNDS);

        annotation { "Name" : "Different side 2 offset" }
        definition.side2OffsetDifferent is boolean;

        if (definition.side2OffsetDifferent)
        {
            annotation { "Name" : "Side 2 offset" }
            isLength(definition.offset2, OFFSET_DISTANCE_BOUNDS);
        }

        // --- Method ---
        annotation { "Name" : "Method", "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.REMEMBER_PREVIOUS_VALUE] }
        definition.method is BridgingFilletMethod;

        // --- Conic sub-parameters ---
        if (definition.method == BridgingFilletMethod.CONIC)
        {
            annotation { "Name" : "Conic type", "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.REMEMBER_PREVIOUS_VALUE] }
            definition.conicType is BridgingFilletConicType;

            if (definition.conicType == BridgingFilletConicType.RHO)
            {
                annotation { "Name" : "Rho" }
                isReal(definition.rho, RHO_BOUNDS);
            }
        }

        // --- Continuity ---
        annotation { "Name" : "Continuity 1", "UIHint" : UIHint.SHOW_LABEL }
        definition.continuity1 is BridgingFilletContinuityType;

        annotation { "Name" : "Continuity 2", "Column Name" : "Second continuity", "UIHint" : UIHint.SHOW_LABEL }
        definition.continuity2 is BridgingFilletContinuityType;

        // --- Flip (always shown for G1/G2) ---
        annotation { "Name" : "Opposite direction", "UIHint" : UIHint.OPPOSITE_DIRECTION }
        definition.flip1 is boolean;

        annotation { "Name" : "Opposite direction", "Column Name" : "Second opposite direction", "UIHint" : UIHint.OPPOSITE_DIRECTION }
        definition.flip2 is boolean;

        // --- Output options (FACES path) ---
        if (definition.inputType == BridgingFilletInputType.FACES)
        {
            annotation { "Name" : "Output type", "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.REMEMBER_PREVIOUS_VALUE] }
            definition.outputType is BridgingFilletOutputType;

            annotation { "Name" : "Keep junction wire" }
            definition.keepJunctionWire is boolean;

            annotation { "Name" : "Keep offset wires" }
            definition.keepOffsetWires is boolean;
        }

        // --- Hidden state (set by editing logic, never shown) ---
        annotation { "Name" : "side1IsWire", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.side1IsWire is boolean;

        annotation { "Name" : "side2IsWire", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.side2IsWire is boolean;

        // --- Keep input bodies (CURVES, wire inputs only — visibility driven by hidden booleans) ---
        if (definition.side1IsWire || definition.side2IsWire)
        {
            annotation { "Name" : "Keep input bodies" }
            definition.keepInputBodies is boolean;
        }

        // --- Debug group (collapsed by default) ---
        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Debug intersection" }
            definition.debugIntersection is boolean;

            annotation { "Name" : "Debug bridge" }
            definition.debugBridge is boolean;

            annotation { "Name" : "Show offsets" }
            definition.showOffsets is boolean;

            if (definition.inputType == BridgingFilletInputType.CURVES)
            {
                annotation { "Name" : "Show junction frame" }
                definition.showJunctionFrame is boolean;
            }

            if (definition.inputType == BridgingFilletInputType.FACES)
            {
                annotation { "Name" : "Show isocurves" }
                definition.showIsocurves is boolean;

                if (definition.showIsocurves)
                {
                    annotation { "Name" : "Isocurve count" }
                    isInteger(definition.isocurveCount, { (unitless) : [1, 5, 50] } as IntegerBoundSpec);
                }
            }
        }
    }
    {
        // 1. VALIDATION
        verifyNoMesh(context, definition, "side1Curves");
        verifyNoMesh(context, definition, "side2Curves");
        verifyNoMesh(context, definition, "side1Face");
        verifyNoMesh(context, definition, "side2Face");

        // 2. RESOLVE INPUT SELECTIONS TO EDGES (CURVES path)
        // Evaluate each selection and branch on entity type:
        //   bare edge  → use directly
        //   wire body  → extract owned edges
        var side1Edge = qNothing();
        var side2Edge = qNothing();
        if (definition.inputType == BridgingFilletInputType.CURVES)
        {
            var s1Entities = evaluateQuery(context, definition.side1Curves);
            if (size(s1Entities) > 0)
            {
                var s1Entity = s1Entities[0];
                if (!isQueryEmpty(context, qEntityFilter(s1Entity, EntityType.EDGE)))
                    side1Edge = s1Entity;
                else
                    side1Edge = qOwnedByBody(s1Entity, EntityType.EDGE);
            }
            var s2Entities = evaluateQuery(context, definition.side2Curves);
            if (size(s2Entities) > 0)
            {
                var s2Entity = s2Entities[0];
                if (!isQueryEmpty(context, qEntityFilter(s2Entity, EntityType.EDGE)))
                    side2Edge = s2Entity;
                else
                    side2Edge = qOwnedByBody(s2Entity, EntityType.EDGE);
            }
        }

        // 2b. RESOLVE REFERENCE EDGES
        if (definition.inputType == BridgingFilletInputType.FACES)
        {
            // VALIDATE: side1Edge must lie on side1Face, side2Edge on side2Face
            // TODO: qIntersection check — qIntersection([definition.side1Edge,
            //         qAdjacent(definition.side1Face, AdjacencyType.EDGE, EntityType.EDGE)])
            //   throw regenError("Side 1 reference edge must lie on side 1 face.", ["side1Edge"]) if empty
            //   throw regenError("Side 2 reference edge must lie on side 2 face.", ["side2Edge"]) if empty
        }

        // Resolve effective offset distances
        const offset1 = definition.offset;
        const offset2 = definition.side2OffsetDifferent ? definition.offset2 : definition.offset;

        // Outer-scope bridge data — set in CURVES path below, used in step 5 and debug
        var junctionPt = undefined;
        var junctionVertex1 = qNothing();
        var junctionVertex2 = qNothing();
        var junctionParam1 = undefined;
        var junctionParam2 = undefined;
        var offsetParam1 = undefined;
        var offsetParam2 = undefined;
        var offsetPt1 = undefined;
        var offsetPt2 = undefined;
        var offsetTan1 = undefined;
        var offsetTan2 = undefined;

        // 3. COMPUTE OFFSET CURVES
        if (definition.inputType == BridgingFilletInputType.FACES)
        {
            // TODO: call resolveOffsetCurve(context, id + "offset1", definition.side1Edge, definition.side1Face,
            //         offset1, definition.continuity1, definition.flip1)
            // TODO: call resolveOffsetCurve(context, id + "offset2", definition.side2Edge, definition.side2Face,
            //         offset2, definition.continuity2, definition.flip2)
        }
        else // CURVES
        {
            // Find junction vertex (shared endpoint between the two edges)
            var verts1 = qAdjacent(side1Edge, AdjacencyType.VERTEX, EntityType.VERTEX);
            var verts2 = qAdjacent(side2Edge, AdjacencyType.VERTEX, EntityType.VERTEX);
            var junctionVertexQ = qIntersection([verts1, verts2]);

            if (isQueryEmpty(context, junctionVertexQ))
            {
                // Fallback: closest endpoint pair (non-adjacent edges)
                var v1List = evaluateQuery(context, verts1);
                var v2List = evaluateQuery(context, verts2);
                var bestDist = undefined;
                for (var v1 in v1List)
                {
                    var p1 = evVertexPoint(context, { "vertex" : v1 });
                    for (var v2 in v2List)
                    {
                        var p2 = evVertexPoint(context, { "vertex" : v2 });
                        var d = norm(p1 - p2);
                        if (bestDist == undefined || d < bestDist)
                        {
                            bestDist = d;
                            junctionPt = p1;
                            junctionVertex1 = v1;
                            junctionVertex2 = v2;
                        }
                    }
                }
            }
            else
            {
                junctionPt = evVertexPoint(context, { "vertex" : junctionVertexQ });
                junctionVertex1 = junctionVertexQ;
                junctionVertex2 = junctionVertexQ;
            }

            // Determine which endpoint (param 0 or 1) of each edge is the junction
            var etl1 = evEdgeTangentLines(context, { "edge" : side1Edge, "parameters" : [0.0, 1.0],
                                                      "arcLengthParameterization" : false });
            var etl2 = evEdgeTangentLines(context, { "edge" : side2Edge, "parameters" : [0.0, 1.0],
                                                      "arcLengthParameterization" : false });
            junctionParam1 = norm(etl1[0].origin - junctionPt) < norm(etl1[1].origin - junctionPt) ? 0.0 : 1.0;
            junctionParam2 = norm(etl2[0].origin - junctionPt) < norm(etl2[1].origin - junctionPt) ? 0.0 : 1.0;

            // Compute arc-length offset parameters, walking away from the junction
            var len1 = evLength(context, { "entities" : side1Edge });
            var len2 = evLength(context, { "entities" : side2Edge });
            offsetParam1 = junctionParam1 == 0.0 ? offset1 / len1 : 1.0 - offset1 / len1;
            offsetParam2 = junctionParam2 == 0.0 ? offset2 / len2 : 1.0 - offset2 / len2;
            offsetParam1 = max(0.0, min(1.0, offsetParam1));
            offsetParam2 = max(0.0, min(1.0, offsetParam2));

            // Evaluate offset point position and tangent on each edge
            var otl1 = evEdgeTangentLines(context, { "edge" : side1Edge, "parameters" : [offsetParam1],
                                                      "arcLengthParameterization" : true });
            var otl2 = evEdgeTangentLines(context, { "edge" : side2Edge, "parameters" : [offsetParam2],
                                                      "arcLengthParameterization" : true });
            offsetPt1 = otl1[0].origin;
            offsetPt2 = otl2[0].origin;

            // Tangent must point AWAY from junction (into the bridge gap).
            // Arc-length parameterization tangent points from param=0 to param=1.
            // If junction is at param 1, negate to reverse direction.
            offsetTan1 = junctionParam1 == 1.0 ? -1 * otl1[0].direction : otl1[0].direction;
            offsetTan2 = junctionParam2 == 1.0 ? -1 * otl2[0].direction : otl2[0].direction;
            if (definition.flip1) offsetTan1 = -1 * offsetTan1;
            if (definition.flip2) offsetTan2 = -1 * offsetTan2;
        }

        // 4. AUTO-FLIP (geometric validation pass)
        // TODO: call computeAutoFlip(tangent1, tangent2) from bridgingFilletUtils.fs
        //   If dot(T0, T1) > 0 (same direction → back-to-back), flip side2 tangent.
        //   Override flip1/flip2 only when editing logic did not already set them.
        //   Apply flip by negating the tangent vector before Hermite assembly.

        // 5. BUILD BRIDGING ELEMENT
        if (definition.inputType == BridgingFilletInputType.CURVES)
        {
            if (definition.method == BridgingFilletMethod.BRIDGING)
            {
                // Map continuity setting to BridgingSideData degree
                var degree1 = definition.continuity1 == BridgingFilletContinuityType.G1 ? 1 : 2;
                var degree2 = definition.continuity2 == BridgingFilletContinuityType.G1 ? 1 : 2;

                // Build side data maps; add curvature fields for G2
                var side1Map = { "degree" : degree1, "position" : offsetPt1 };
                var side2Map = { "degree" : degree2, "position" : offsetPt2 };
                if (degree1 >= 1) side1Map = mergeMaps(side1Map, { "tangent" : offsetTan1 });
                if (degree2 >= 1) side2Map = mergeMaps(side2Map, { "tangent" : offsetTan2 });
                if (degree1 >= 2)
                {
                    var curv1 = evEdgeCurvature(context, { "edge" : side1Edge, "parameter" : offsetParam1,
                                                            "arcLengthParameterization" : true });
                    side1Map = mergeMaps(side1Map, { "curvatureDirection" : curvatureFrameNormal(curv1),
                                                     "curvature" : curv1.curvature });
                }
                if (degree2 >= 2)
                {
                    var curv2 = evEdgeCurvature(context, { "edge" : side2Edge, "parameter" : offsetParam2,
                                                            "arcLengthParameterization" : true });
                    side2Map = mergeMaps(side2Map, { "curvatureDirection" : curvatureFrameNormal(curv2),
                                                     "curvature" : curv2.curvature });
                }
                var side1 is BridgingSideData = side1Map as BridgingSideData;
                var side2 is BridgingSideData = side2Map as BridgingSideData;

                // Bridging curve
                var controlPts = computeBridgingControlPoints(context, side1, side2);
                var bridgeCurve = bSplineCurve({ "degree" : size(controlPts) - 1,
                                                 "isPeriodic" : false,
                                                 "controlPoints" : controlPts });
                opCreateBSplineCurve(context, id + "bridge", { "bSplineCurve" : bridgeCurve });

                // Trimmed wires — split each input edge at its offset parameter and keep the far sub-edge.
                opSplitEdges(context, id + "split1", {
                    "edges" : side1Edge,
                    "parameters" : [[offsetParam1]],
                    "arcLengthParameterization" : true
                });
                var subEdges1 = evaluateQuery(context, qCreatedBy(id + "split1", EntityType.EDGE));
                var keepEdge1 = qNothing();
                for (var e in subEdges1)
                {
                    var tl = evEdgeTangentLines(context, { "edge" : e, "parameters" : [0.0],
                                                            "arcLengthParameterization" : false });
                    if (norm(tl[0].origin - offsetPt1) < norm(tl[0].origin - junctionPt))
                        keepEdge1 = e;
                }
                opDeleteBodies(context, id + "delStub1", {
                    "entities" : qSubtraction(qCreatedBy(id + "split1", EntityType.EDGE), keepEdge1)
                });

                opSplitEdges(context, id + "split2", {
                    "edges" : side2Edge,
                    "parameters" : [[offsetParam2]],
                    "arcLengthParameterization" : true
                });
                var subEdges2 = evaluateQuery(context, qCreatedBy(id + "split2", EntityType.EDGE));
                var keepEdge2 = qNothing();
                for (var e in subEdges2)
                {
                    var tl = evEdgeTangentLines(context, { "edge" : e, "parameters" : [0.0],
                                                            "arcLengthParameterization" : false });
                    if (norm(tl[0].origin - offsetPt2) < norm(tl[0].origin - junctionPt))
                        keepEdge2 = e;
                }
                opDeleteBodies(context, id + "delStub2", {
                    "entities" : qSubtraction(qCreatedBy(id + "split2", EntityType.EDGE), keepEdge2)
                });
            }
            else // CONIC
            {
                if (definition.conicType == BridgingFilletConicType.ARC)
                {
                    // ARC — rational quadratic Bézier (P&T §7.3):
                    //   theta = angle between -T0 and T1
                    //   w = cos(theta / 2)
                    //   P_mid = intersection of tangent lines at P0 and P1
                    //   Rational quadratic: [P0, P_mid, P1], weights [1, w, 1]
                }
                else // RHO
                {
                    // RHO — conic shape factor (Farin CAGD §8.5):
                    //   w = definition.rho / (1 - definition.rho)
                    //   P_mid = intersection of tangent lines at P0 and P1 (same as ARC)
                    //   Rational quadratic: [P0, P_mid, P1], weights [1, w, 1]
                }
                // TODO: opCreateBSplineCurve with rational flag and computed weights
            }
        }
        else // FACES
        {
            if (definition.method == BridgingFilletMethod.BRIDGING)
            {
                // TODO: opLoft with profiles = [offsetCurve1, offsetCurve2]
                //   continuity1 → startCondition:
                //     G1 → LoftEndDerivativeType.MATCH_TANGENT, adjacentFacesStart = side1Face
                //     G2 → LoftEndDerivativeType.MATCH_CURVATURE, adjacentFacesStart = side1Face
                //   continuity2 → endCondition (same mapping with side2Face)
            }
            else // CONIC
            {
                // TODO: for each cross-section position, construct conic profile curve (ARC or RHO)
                //   using same formulas as CURVES conic path above
                // TODO: opLoft using conic cross-section curves as profiles
            }
        }

        // 6. OUTPUT CLEANUP
        if (definition.inputType == BridgingFilletInputType.FACES)
        {
            if (definition.outputType == BridgingFilletOutputType.TRIMMED_FACES)
            {
                // TODO: opTrimSurface — trim side1Face by offsetCurve1, side2Face by offsetCurve2
            }
            else if (definition.outputType == BridgingFilletOutputType.JOINED_FACES)
            {
                // TODO: opTrimSurface (same as TRIMMED_FACES)
                // TODO: opBoolean(context, id + "join", { "tools" : joinedQuery, "operationType" : BooleanOperationType.UNION })
            }
            // Delete junction wire unless user wants to keep it
            // TODO: opDeleteBodies(context, id + "deleteJunction", { "bodies" : junctionWireQuery })
            //   unless definition.keepJunctionWire

            // Delete offset wires unless user wants to keep them
            // TODO: opDeleteBodies(context, id + "deleteOffsets", { "bodies" : offsetWiresQuery })
            //   unless definition.keepOffsetWires
        }
        else // CURVES
        {
            if ((definition.side1IsWire || definition.side2IsWire) && !definition.keepInputBodies)
            {
                // TODO: opDeleteBodies(context, id + "deleteInputWires", { "bodies" : inputWireQuery })
            }
        }

        // 7. DEBUG OUTPUT
        if (definition.debugIntersection)
        {
            if (definition.inputType == BridgingFilletInputType.CURVES)
            {
                var edges1 = evaluateQuery(context, definition.side1Curves);
                var edges2 = evaluateQuery(context, definition.side2Curves);
                println("bridgingFillet [intersection]: inputType=CURVES");
                println("  side1Curves count=" ~ toString(size(edges1)));
                println("  side2Curves count=" ~ toString(size(edges2)));
                println("  offset=" ~ toString(definition.offset));
                println("  continuity1=" ~ toString(definition.continuity1));
                println("  continuity2=" ~ toString(definition.continuity2));
                println("  flip1=" ~ toString(definition.flip1) ~ "  flip2=" ~ toString(definition.flip2));
                var dtl1 = try(evEdgeTangentLines(context, { "edge" : side1Edge, "parameters" : [0.0, 1.0],
                                                              "arcLengthParameterization" : false }));
                var dtl2 = try(evEdgeTangentLines(context, { "edge" : side2Edge, "parameters" : [0.0, 1.0],
                                                              "arcLengthParameterization" : false }));
                if (dtl1 != undefined)
                {
                    println("  edge1 start: pos=" ~ toString(dtl1[0].origin) ~ "  tan=" ~ toString(dtl1[0].direction));
                    println("  edge1 end:   pos=" ~ toString(dtl1[1].origin) ~ "  tan=" ~ toString(dtl1[1].direction));
                }
                if (dtl2 != undefined)
                {
                    println("  edge2 start: pos=" ~ toString(dtl2[0].origin) ~ "  tan=" ~ toString(dtl2[0].direction));
                    println("  edge2 end:   pos=" ~ toString(dtl2[1].origin) ~ "  tan=" ~ toString(dtl2[1].direction));
                }
                var dverts1 = qAdjacent(side1Edge, AdjacencyType.VERTEX, EntityType.VERTEX);
                var dverts2 = qAdjacent(side2Edge, AdjacencyType.VERTEX, EntityType.VERTEX);
                var sharedV = qIntersection([dverts1, dverts2]);
                if (isQueryEmpty(context, sharedV))
                    println("  junction: no shared vertex — edges are non-adjacent (closest-endpoint fallback will be used)");
                else
                {
                    var sharedPt = evVertexPoint(context, { "vertex" : sharedV });
                    println("  junction: shared vertex found at pos=" ~ toString(sharedPt));
                }
            }
            else
            {
                var faces1 = evaluateQuery(context, definition.side1Face);
                var faces2 = evaluateQuery(context, definition.side2Face);
                println("bridgingFillet [intersection]: inputType=FACES");
                println("  side1Face count=" ~ toString(size(faces1)));
                println("  side2Face count=" ~ toString(size(faces2)));
                println("  offset=" ~ toString(definition.offset));
                println("  method=" ~ toString(definition.method));
            }
        }
        if (definition.debugBridge)
        {
            println("bridgingFillet [bridge]: CURVES path — bridge + trim wires created");
        }

        if (definition.showOffsets && definition.inputType == BridgingFilletInputType.CURVES &&
            offsetPt1 != undefined && offsetPt2 != undefined)
        {
            addDebugPoint(context, offsetPt1, DebugColor.GREEN);
            addDebugPoint(context, offsetPt2, DebugColor.BLUE);
            println("bridgingFillet [offsets]:");
            println("  side1 offset pt=" ~ toString(offsetPt1) ~ "  param=" ~ toString(offsetParam1));
            println("  side2 offset pt=" ~ toString(offsetPt2) ~ "  param=" ~ toString(offsetParam2));
        }

        if (definition.inputType == BridgingFilletInputType.CURVES && definition.showJunctionFrame &&
            junctionPt != undefined)
        {
            var curv1 = evEdgeCurvature(context, { "edge" : side1Edge, "parameter" : junctionParam1,
                                                    "arcLengthParameterization" : false });
            // xAxis=normal (CYAN), yAxis=binormal (MAGENTA), zAxis=tangent (YELLOW)
            debug(context, curv1.frame, DebugColor.CYAN, DebugColor.MAGENTA, DebugColor.YELLOW);
            addDebugPoint(context, junctionPt, DebugColor.RED);

            println("bridgingFillet [junction]:");
            println("  position=" ~ toString(junctionPt));
            println("  tangent=" ~ toString(curvatureFrameTangent(curv1)));
            println("  normal=" ~ toString(curvatureFrameNormal(curv1)));
            println("  curvature=" ~ toString(curv1.curvature));
            println("  edge1 junction param=" ~ toString(junctionParam1));
        }
        if (definition.inputType == BridgingFilletInputType.FACES && definition.showIsocurves)
        {
            // TODO: research @opExtractIsocurve in std
            //   extract definition.isocurveCount isocurves from bridging surface
        }
    }, {
        "inputType" : BridgingFilletInputType.CURVES,
        "method" : BridgingFilletMethod.BRIDGING,
        "continuity1" : BridgingFilletContinuityType.G1,
        "continuity2" : BridgingFilletContinuityType.G1,
        "flip1" : false,
        "flip2" : false,
        "side2OffsetDifferent" : false,
        "conicType" : BridgingFilletConicType.ARC,
        "rho" : 0.5,
        "outputType" : BridgingFilletOutputType.BRIDGE_ONLY,
        "keepJunctionWire" : false,
        "keepOffsetWires" : false,
        "keepInputBodies" : false,
        "side1IsWire" : false,
        "side2IsWire" : false,
        "debugIntersection" : false,
        "debugBridge" : false,
        "showOffsets" : false,
        "showJunctionFrame" : false,
        "showIsocurves" : false,
        "isocurveCount" : 5
    });


// ---------------------------------------------------------------------------
// Editing logic
// ---------------------------------------------------------------------------

/**
 * Editing logic for the bridgingFillet feature.
 *
 * @param context {Context} - active modeling context
 * @param id {Id} - feature id
 * @param oldDefinition {map} - definition before this edit
 * @param definition {map} - current definition map (mutated and returned)
 * @param isCreating {boolean} - true on initial feature creation
 * @param specifiedParameters {map} - set of parameter keys the user explicitly changed this edit
 * @param hiddenBodies {Query} - bodies hidden from the UI
 * @returns {map} - updated definition
 */
export function bridgingFilletEditingLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map, hiddenBodies is Query) returns map
{
    // 1. Wire detection — set hidden booleans that gate keepInputBodies visibility
    if (definition.inputType == BridgingFilletInputType.CURVES)
    {
        definition.side1IsWire = !isQueryEmpty(context, qBodyType(definition.side1Curves, BodyType.WIRE));
        definition.side2IsWire = !isQueryEmpty(context, qBodyType(definition.side2Curves, BodyType.WIRE));
    }
    else
    {
        definition.side1IsWire = false;
        definition.side2IsWire = false;
    }

    // 2. offset2 sync
    //   false→true transition: initialize offset2 = offset (if user hasn't explicitly set it)
    //   false: keep offset2 in sync with offset
    if (oldDefinition != {} && !oldDefinition.side2OffsetDifferent && definition.side2OffsetDifferent)
    {
        if (!specifiedParameters.offset2)
        {
            definition.offset2 = definition.offset;
        }
    }
    else if (!definition.side2OffsetDifferent)
    {
        definition.offset2 = definition.offset;
    }

    // 3. Auto-flip detection
    //   When both sides have reference edges, check tangent orientation.
    //   If dot(T1, T2) > 0 (vectors point in same direction → back-to-back approach),
    //   flip side2 so bridging entrances face each other.
    //   Only auto-set flip2 if the user has not overridden it.
    var refEdge1 = qNothing();
    var refEdge2 = qNothing();
    if (definition.inputType == BridgingFilletInputType.CURVES)
    {
        refEdge1 = definition.side1Curves;
        refEdge2 = definition.side2Curves;
    }
    else
    {
        refEdge1 = definition.side1Edge;
        refEdge2 = definition.side2Edge;
    }

    var hasBothSides = !isQueryEmpty(context, refEdge1) && !isQueryEmpty(context, refEdge2);
    var geometryChanged = isCreating || specifiedParameters.side1Curves || specifiedParameters.side2Curves ||
                          specifiedParameters.side1Edge || specifiedParameters.side2Edge ||
                          specifiedParameters.continuity1 || specifiedParameters.continuity2;
    if (hasBothSides && !specifiedParameters.flip2 && geometryChanged)
    {
        // Evaluate tangents at the far ends of each reference edge (parameter 1.0 and 0.0)
        var tl1 = try(evEdgeTangentLines(context, { "edge" : refEdge1, "parameters" : [1.0], "arcLengthParameterization" : false }));
        var tl2 = try(evEdgeTangentLines(context, { "edge" : refEdge2, "parameters" : [0.0], "arcLengthParameterization" : false }));
        if (tl1 != undefined && tl2 != undefined)
        {
            var t1 = tl1[0].direction;
            var t2 = tl2[0].direction;
            if (dot(t1, t2) > 0)
            {
                definition.flip2 = !definition.flip2;
            }
        }
    }

    // 4. Reference edge auto-populate (FACES path)
    //   When both faces are selected and user hasn't manually picked edges, try to find the shared edge.
    if (definition.inputType == BridgingFilletInputType.FACES &&
        !isQueryEmpty(context, definition.side1Face) && !isQueryEmpty(context, definition.side2Face) &&
        (specifiedParameters.side1Face || specifiedParameters.side2Face) &&
        !specifiedParameters.side1Edge && !specifiedParameters.side2Edge)
    {
        var sharedEdge = qIntersection([
            qAdjacent(definition.side1Face, AdjacencyType.EDGE, EntityType.EDGE),
            qAdjacent(definition.side2Face, AdjacencyType.EDGE, EntityType.EDGE)
        ]);
        if (!isQueryEmpty(context, sharedEdge))
        {
            definition.side1Edge = sharedEdge;
            definition.side2Edge = sharedEdge;
        }
    }

    // 5. inputType change — clear opposing selectors to avoid stale selections
    if (oldDefinition != {} && oldDefinition.inputType != definition.inputType)
    {
        if (definition.inputType == BridgingFilletInputType.CURVES)
        {
            definition.side1Face = qNothing();
            definition.side2Face = qNothing();
            definition.side1Edge = qNothing();
            definition.side2Edge = qNothing();
        }
        else
        {
            definition.side1Curves = qNothing();
            definition.side2Curves = qNothing();
        }
    }

    return definition;
}
