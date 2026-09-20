FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");
// ProjectionType is not reached by common.fs (corrections log 17).
import(path : "onshape/std/projectiontype.gen.fs", version : "3070.0");

// edge_offset_utils: the fitter (approximateFamily), emitters, classifiers, the
// approximation predicate and bounds, formatting helpers. export import so the enums
// declared there are reachable as feature parameter types.
export import(path : "a2665e22c07b7a6929ce4e80", version : "");
// design_map_query_utils: embedVariableMap and the extractable wrappers.
import(path : "2b6b313ac740a0146d5bef7c", version : "");

/**
 * Clean wire: one wire in, one wire out, the same shape with fewer edges and fewer
 * control points.
 *
 * This is the manual Wire_Smoothing recipe as a feature: split a wire at the vertices
 * worth keeping, Edit-curve each piece to one spline under its own control-point budget
 * with the end tangents held, leave the exact pieces alone, composite the lot. Here the
 * pieces are found for you -- every joint of the chain is classified by the angle the
 * two edges meet at -- and a GROUP (contiguous edges, its own approximation) is how you
 * say "fit this stretch as one, with this budget", which is what the manual splits at
 * tangent joints were for.
 *
 * Joints
 *   <= G1_JUNCTION_ANGLE (0.57 deg)          tangent: merged into one run
 *   up to the corner angle                    nearly tangent: merged if "Make nearly
 *                                             tangent joints tangent", else a corner
 *   > corner angle                            a corner: the vertex is kept
 *   A sliver edge is judged by its neighbours across it: absorbed into the run when they
 *   are tangent, kept as its own exact piece (it is a chamfer or a fillet) when not.
 *
 * Runs
 *   A run of LINE / CIRCLE source edges is copied exactly, so it keeps its type and its
 *   radius. Any other run is sampled at a curvature-adaptive spacing and fitted as one
 *   cubic with chord-length parameters and the kernel's own end tangents, then measured.
 *
 * Output
 *   One wire body, its edges created under ids that count runs from the chain's start,
 *   so downstream references survive an upstream edit that does not change the corner
 *   set. Reference the BODY (or the published query variable), never its edges.
 *
 * Not yet: closed loops, arc recognition inside splines, exact-merge fallback, the
 * "Suggest groups" editing logic. See research_clean_wire.md.
 */

// ============================================================================
// Bounds and enums
// ============================================================================

/**
 * Joints sharper than this are corners. Below it, down to G1_JUNCTION_ANGLE, a joint is
 * "nearly tangent". 3 degrees keeps the 4.8 degree pitch creases at the ramp ends of the
 * ski's rout intersection as corners, which is what the manual splits did.
 */
export const CleanWireCornerBounds = { (degree) : [0.1, 3, 90], (radian) : 0.05 } as AngleBoundSpec;

/**
 * Manual: only the groups are fitted, every other edge is copied as it is, and the
 * classifier's opinion of the joints is printed but not acted on. Auto: the classifier
 * forms the runs from the joint angles, and breaks and groups override it.
 */
export enum CleanWireMode
{
    annotation { "Name" : "Manual" }
    MANUAL,
    annotation { "Name" : "Auto" }
    AUTO
}

/** Per-group approximation override. */
export enum CleanApproximation
{
    annotation { "Name" : "Global" }
    GLOBAL,
    annotation { "Name" : "Max control points" }
    MAX_CP,
    annotation { "Name" : "Tolerance" }
    TOLERANCE
}

/**
 * Edges shorter than the sliver length are fragments whose own end tangents mean nothing;
 * they are judged by the neighbours across them. A length of its own, NOT a multiple of
 * the tolerance: tied to the tolerance it grew to 25 mm at 0.5 mm, swallowed the 5.6 mm
 * notch jog between two parallel lines, and drove one spline through both 45 degree
 * corners.
 */
export const CleanWireSliverBounds = { (millimeter) : [0.01, 0.5, 20] } as LengthBoundSpec;

/** Curvature stations per edge for the sampling density. */
export const CURVATURE_STATIONS = 32;

/** Bounds on the sample spacing, see runSamples. */
export const SAMPLE_MIN_SPACING = 0.05 * millimeter;
export const SAMPLE_MAX_SPACING = 10 * millimeter;
export const SAMPLE_MIN_PER_EDGE = 3;
export const SAMPLE_MAX_PER_EDGE = 200;

/** The chain is ordered by endpoint coincidence at this tolerance (merge_curve's value). */
export const CLEAN_CHAIN_TOLERANCE = 1e-5 * meter;

/**
 * The wall is extruded past the wire's own extent along the plane normal by this fraction
 * of that extent plus a millimetre, so the projection onto it cannot fall off an edge.
 */
export const WALL_MARGIN_FRACTION = 0.1;
export const WALL_MARGIN_MIN = 1 * millimeter;

/** Read-only counts and percentages in the Reduction group. */
export const CleanWireCountBounds = { (unitless) : [0, 0, 1e9] } as IntegerBoundSpec;
export const CleanWirePercentBounds = { (unitless) : [-1e9, 0, 1e9] } as RealBoundSpec;

// ============================================================================
// Feature
// ============================================================================

annotation { "Feature Type Name" : "Clean wire",
        "Feature Type Description" : "Rebuild a wire with fewer edges and control points: tangent stretches become one spline each, corners are kept, exact lines and arcs pass through.",
        "Editing Logic Function" : "cleanWireEditLogic" }
export const cleanWire = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Mode", "Default" : CleanWireMode.MANUAL, "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL], "Description" : "Manual: only the groups are fitted, everything else is copied as it is. Auto: tangent stretches are found and fitted; breaks and groups override." }
        definition.mode is CleanWireMode;

        annotation { "Name" : "Wire", "Filter" : (EntityType.EDGE || BodyType.WIRE) && ConstructionObject.NO, "Description" : "The wire body, or edges forming one chain, to clean." }
        definition.sourceEdges is Query;

        annotation { "Name" : "Name", "Description" : "Names the output wire. Clear it to leave it unnamed." }
        definition.outputName is string;

        if (definition.mode == CleanWireMode.AUTO)
        {
            // What the classifier found, one item per run, filled in by the editing logic
            // (edges and position) and by the regen (control points and deviation). Read
            // only: it is a report, and in Auto it is the only thing driving.
            annotation { "Name" : "Runs", "Item name" : "run", "Item label template" : "#ar_label", "UIHint" : [UIHint.READ_ONLY, UIHint.COLLAPSE_ARRAY_ITEMS, UIHint.PREVENT_ARRAY_REORDER] }
            definition.autoRuns is array;
            for (var run in definition.autoRuns)
            {
                annotation { "Name" : "Edges", "UIHint" : UIHint.READ_ONLY }
                run.ar_label is string;

                annotation { "Name" : "Kind", "UIHint" : UIHint.READ_ONLY }
                run.ar_kind is string;

                annotation { "Name" : "Control points", "UIHint" : UIHint.READ_ONLY }
                isInteger(run.ar_cps, CleanWireCountBounds);

                annotation { "Name" : "Deviation", "UIHint" : UIHint.READ_ONLY }
                isLength(run.ar_deviation, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);
            }
        }

        // FOCUS_INNER_QUERY: a new group's own Edges field takes the selection. Without it
        // the first empty query in the dialog does, and picks meant for the group went to
        // "Break at" (which is why that field now sits below the groups as well).
        if (definition.mode == CleanWireMode.MANUAL)
        {
        // Replaces the groups with one per fitted run the classifier would form, each
        // budgeted at what it needs at the tolerance -- the manual recipe, pre-filled.
        annotation { "Name" : "Auto-populate groups", "Description" : "Clear the groups and create one per stretch the classifier would fit, with Max control points set to what each needs at the tolerance. Edit them afterwards as you like." }
        isButton(definition.populateGroups);

        annotation { "Name" : "Groups", "Item name" : "group", "Item label template" : "#cw_name", "UIHint" : [UIHint.FOCUS_INNER_QUERY, UIHint.COLLAPSE_ARRAY_ITEMS], "Description" : "A stretch of contiguous edges fitted as one curve under its own approximation. The ends of a group are always kept as vertices." }
        definition.groups is array;
        for (var entry in definition.groups)
        {
            annotation { "Name" : "Edges", "Filter" : EntityType.EDGE && ConstructionObject.NO, "Description" : "Contiguous edges of the wire, fitted as one curve." }
            entry.cw_edges is Query;

            annotation { "Name" : "Name" }
            entry.cw_name is string;

            annotation { "Name" : "Approximation", "Default" : CleanApproximation.GLOBAL, "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL], "Description" : "Global: the approximation parameters below. Otherwise this group's own control-point cap or tolerance." }
            entry.cw_mode is CleanApproximation;

            if (entry.cw_mode == CleanApproximation.MAX_CP)
            {
                annotation { "Name" : "Maximum control points" }
                isInteger(entry.cw_maxCPs, DrivenOffsetMaxCPBounds);
            }

            if (entry.cw_mode == CleanApproximation.TOLERANCE)
            {
                annotation { "Name" : "Tolerance" }
                isLength(entry.cw_tolerance, TOLERANCE_BOUND);
            }
        }

        annotation { "Name" : "Break at", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "Description" : "Vertices of the wire to keep whatever the joint angle there. A break inside a group is a conflict and is reported." }
        definition.breakAt is Query;
        }

        if (definition.mode == CleanWireMode.AUTO)
        {
            annotation { "Name" : "Corner angle", "Description" : "Joints where the edges meet at more than this angle are corners and keep their vertex. Below it a joint is nearly tangent." }
            isAngle(definition.cornerAngle, CleanWireCornerBounds);

            annotation { "Name" : "Make nearly tangent joints tangent", "Default" : true, "Description" : "A joint between the tangent threshold (0.57 degrees) and the corner angle is fitted through, which makes it exactly tangent within the tolerance. Off, such joints are corners." }
            definition.forceTangency is boolean;

            annotation { "Name" : "Sliver length", "Description" : "Edges shorter than this are fragments: absorbed into the run when the edges either side of them line up, kept as their own piece when they sit at a corner." }
            isLength(definition.sliverLength, CleanWireSliverBounds);

            // A real button (std isButton): no value, no default; the press arrives in the
            // editing logic as clickedButton == "fitControlPoints".
            annotation { "Name" : "Fit control points to tolerance", "Description" : "Fit every run at the tolerance without a cap, and set Maximum control points to what the largest run needed." }
            isButton(definition.fitControlPoints);
        }

        annotation { "Group Name" : "Approximation parameters", "Collapsed By Default" : true }
        {
            drivenOffsetApproximationPredicate(definition);
        }

        annotation { "Name" : "Maximum deviation", "UIHint" : UIHint.READ_ONLY, "Description" : "Measured between the cleaned wire and the source wire." }
        isLength(definition.maxDeviation, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);

        annotation { "Name" : "Show deviation", "Default" : false, "Description" : "Draw the deviation comb between the cleaned wire and the source, with the maximum marked." }
        definition.showDeviation is boolean;

        annotation { "Name" : "Show runs", "Default" : false, "Description" : "Colour every edge of the cleaned wire by the run it came from, so the stretches the feature formed are visible." }
        definition.showRuns is boolean;

        annotation { "Name" : "Delete input wire", "Default" : false, "Description" : "Remove the source wire body once the cleaned wire exists. The deviation is measured first. Off, the source stays for comparison and for other features." }
        definition.deleteInput is boolean;

        annotation { "Group Name" : "Projection", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Project onto plane", "Default" : false, "Description" : "Also project the wire onto a plane, clean the plan view with its own approximation, extrude it normal to the plane into a wall, and constrain the cleaned wire onto that wall." }
            definition.projectOnPlane is boolean;

            if (definition.projectOnPlane)
            {
                annotation { "Name" : "Plane", "Filter" : BodyType.MATE_CONNECTOR || (EntityType.FACE && GeometryType.PLANE), "MaxNumberOfPicks" : 1, "Description" : "The plane the wire is projected onto; the wall is extruded along its normal." }
                definition.projectionPlane is Query;

                annotation { "Name" : "Plan degree" }
                isInteger(definition.planDegree, DEGREE_BOUND);

                annotation { "Name" : "Plan tolerance", "Description" : "For the plan-view fit; a projection has its own noise." }
                isLength(definition.planTolerance, TOLERANCE_BOUND);

                annotation { "Name" : "Plan maximum control points" }
                isInteger(definition.planMaxCPs, DrivenOffsetMaxCPBounds);

                annotation { "Name" : "Constrain wire to wall", "Default" : true, "Description" : "Project the cleaned wire onto the wall along the wall's normal, so it lies exactly on the plan view. Off, the wall is built and the wire is left free." }
                definition.constrainToWall is boolean;
            }
        }

        annotation { "Group Name" : "Reduction", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Control points before", "UIHint" : UIHint.READ_ONLY }
            isInteger(definition.cpBefore, CleanWireCountBounds);

            annotation { "Name" : "Control points after", "UIHint" : UIHint.READ_ONLY }
            isInteger(definition.cpAfter, CleanWireCountBounds);

            annotation { "Name" : "Control point reduction (%)", "UIHint" : UIHint.READ_ONLY }
            isReal(definition.cpReduction, CleanWirePercentBounds);

            annotation { "Name" : "Edges before", "UIHint" : UIHint.READ_ONLY }
            isInteger(definition.edgesBefore, CleanWireCountBounds);

            annotation { "Name" : "Edges after", "UIHint" : UIHint.READ_ONLY }
            isInteger(definition.edgesAfter, CleanWireCountBounds);

            annotation { "Name" : "Edge reduction (%)", "UIHint" : UIHint.READ_ONLY }
            isReal(definition.edgeReduction, CleanWirePercentBounds);

            annotation { "Name" : "Tangency fixes", "UIHint" : UIHint.READ_ONLY, "Description" : "Nearly tangent joints fitted through, so they are now exactly tangent." }
            isInteger(definition.tangencyFixes, CleanWireCountBounds);
        }

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Show groups", "Default" : false, "Description" : "Highlight every group's source edges and its fitted result in the group's own colour: red, green, blue, cyan, magenta, yellow, black, orange, repeating." }
            definition.debugShowGroups is boolean;

            annotation { "Name" : "Print joints", "Default" : false, "Description" : "Every edge of the chain with its length and type, and every joint with its angle and what was decided about it." }
            definition.debugPrintJoints is boolean;

            annotation { "Name" : "Print runs", "Default" : false, "Description" : "Every run: its edges, samples, the fit's control points and the measured deviation." }
            definition.debugPrintRuns is boolean;

            annotation { "Name" : "Show corners", "Default" : false, "Description" : "Mark every kept vertex." }
            definition.debugShowCorners is boolean;

            annotation { "Name" : "Show control polygons", "Default" : false, "Description" : "Draw the control polygon of every fitted run, colour cycling per run." }
            definition.debugShowPolygons is boolean;

            annotation { "Name" : "Keep pieces", "Default" : false, "Description" : "Leave the per-run curves in the result beside the wire." }
            definition.debugKeepPieces is boolean;
        }
    }
    {
        const edges = expandEdgeQuery(definition.sourceEdges);
        if (isQueryEmpty(context, edges))
        {
            throw regenError("Select a wire or the edges of one chain.", ["sourceEdges"]);
        }

        const chain = describeChain(context, edges);
        if (chain.closed)
        {
            throw regenError("Closed loops are not supported yet; split the wire once first.", ["sourceEdges"]);
        }

        const auto = definition.mode == CleanWireMode.AUTO;
        const main = cleanChain(context, id, id, definition, chain, approximationSettings(definition), "");
        const grouped = main.grouped;
        const runs = main.runs;
        const reports = main.reports;
        var wire = main.wire;

        if (definition.outputName != "")
        {
            setProperty(context, { "entities" : wire, "propertyType" : PropertyType.NAME, "value" : definition.outputName });
        }

        // Against the SOURCE, not the fit's own samples: the number the dialog shows is
        // how far the cleaned wire is from the wire it replaces, anywhere along it. Taken
        // on the free wire, which is one path by construction; a constrained wire can be
        // several pieces where the source folds against the plane.
        const measured = evMaxPathDeviation(context, {
                    "side1" : definition.sourceEdges,
                    "side2" : wire,
                    "showDeviation" : definition.showDeviation
                });

        // Plan view: the same pipeline over the chain projected onto the plane, with its
        // own approximation; the result extruded along the normal into a wall; the 3D
        // wire projected onto that wall along the wall's normal so it lies exactly on the
        // plan view while keeping its heights.
        if (definition.projectOnPlane)
        {
            if (isQueryEmpty(context, definition.projectionPlane))
            {
                throw regenError("Pick a plane to project onto.", ["projectionPlane"]);
            }

            const pl = planeFromQuery(context, definition.projectionPlane);
            const planar = projectChain(context, id, chain, pl);
            const plan = cleanChain(context, id, id + "plan", definition, planar, {
                        "approximationDegree" : definition.planDegree,
                        "approximationTolerance" : definition.planTolerance,
                        "approximationMaxCPs" : definition.planMaxCPs,
                        "debugFit" : definition.debugPrintRuns
                    }, "plan");
            if (definition.outputName != "")
            {
                setProperty(context, { "entities" : plan.wire, "propertyType" : PropertyType.NAME, "value" : definition.outputName ~ " plan" });
            }

            // A wall of no depth of its own: it reaches as far along the normal as the wire
            // does, either side, plus a margin, so the drop always lands on it.
            const margin = max(WALL_MARGIN_MIN, WALL_MARGIN_FRACTION * (planar.normalExtent.max - planar.normalExtent.min));
            const wallId = id + "wall";
            opExtrude(context, wallId, {
                        "entities" : qOwnedByBody(plan.wire, EntityType.EDGE),
                        "direction" : pl.normal,
                        "endBound" : BoundingType.BLIND,
                        "endDepth" : max(planar.normalExtent.max, 0 * meter) + margin,
                        "startBound" : BoundingType.BLIND,
                        "startDepth" : max(-planar.normalExtent.min, 0 * meter) + margin
                    });
            const wall = qCreatedBy(wallId, EntityType.BODY);
            if (definition.outputName != "")
            {
                setProperty(context, { "entities" : wall, "propertyType" : PropertyType.NAME, "value" : definition.outputName ~ " wall" });
            }

            if (definition.constrainToWall)
            {
                const dropId = id + "onWall";
                opDropCurve(context, dropId, {
                            "tools" : qOwnedByBody(wire, EntityType.EDGE),
                            "targets" : qOwnedByBody(wall, EntityType.FACE),
                            "projectionType" : ProjectionType.NORMAL_TO_TARGET
                        });
                const dropped = qCreatedBy(dropId, EntityType.EDGE);
                if (isQueryEmpty(context, dropped))
                {
                    reportFeatureWarning(context, id, "The cleaned wire could not be projected onto the wall; it was left free.");
                }
                else
                {
                    const droppedCount = size(evaluateQuery(context, dropped));
                    const constrainedId = id + "constrained";
                    opExtractWires(context, constrainedId, { "edges" : dropped });
                    opDeleteBodies(context, id + "dropCleanup", { "entities" : qUnion([qOwnerBody(dropped), wire]) });
                    wire = qCreatedBy(constrainedId, EntityType.BODY);
                    if (definition.outputName != "")
                    {
                        setProperty(context, { "entities" : wire, "propertyType" : PropertyType.NAME, "value" : definition.outputName });
                    }

                    const pieces = evaluateQuery(context, wire);
                    if (size(pieces) != 1)
                    {
                        reportFeatureWarning(context, id, "The constrained wire came out as " ~ toString(size(pieces))
                            ~ " bodies: " ~ toString(droppedCount) ~ " of " ~ toString(size(runs)) ~ " edges projected onto the wall. "
                            ~ "An edge running along the plane normal has no projection; pick a plane the wire lies across.");
                        println("[constrain] " ~ toString(droppedCount) ~ " edge(s) dropped onto the wall from "
                            ~ toString(size(runs)) ~ " run(s); " ~ toString(size(pieces)) ~ " wire body(ies) after extraction.");
                    }
                }
            }
        }

        setFeatureComputedParameter(context, id, { "name" : "maxDeviation", "value" : measured.deviation });
        reportReduction(context, id, chain, grouped.joints, reports);
        if (auto)
        {
            reportAutoRuns(context, id, definition, reports);
        }

        if (definition.debugShowCorners)
        {
            showCorners(context, chain, grouped.joints);
        }

        if (definition.debugShowPolygons)
        {
            showPolygons(context, id + "polygons", reports);
        }

        if (definition.showRuns)
        {
            showRuns(context, wire, chain, runs);
        }

        if (definition.debugShowGroups)
        {
            showGroups(context, definition, wire, chain, runs);
        }

        // Last, after the deviation was measured against it and the debug colouring of
        // the groups' source edges is done. Only whole bodies go: a selection of edges
        // from a larger body is left alone.
        if (definition.deleteInput)
        {
            const inputBodies = qBodyType(qEntityFilter(definition.sourceEdges, EntityType.BODY), BodyType.WIRE);
            if (!isQueryEmpty(context, inputBodies))
            {
                opDeleteBodies(context, id + "deleteInput", { "entities" : inputBodies });
            }
            else
            {
                reportFeatureInfo(context, id, "The input was selected as edges, not a wire body, so nothing was deleted.");
            }
        }

        reportOutcome(context, id, definition, chain, grouped.joints, runs, reports, measured.deviation);

        embedVariableMap(context, id, {
                    "variable" : {
                        "edgeCount" : extractableVariable(size(runs), "Edges in the cleaned wire."),
                        "sourceEdgeCount" : extractableVariable(size(chain.edges), "Edges in the wire that was cleaned.")
                    },
                    "query" : {
                        "cleanWire" : extractableQuery(wire, "The cleaned wire.", DebugColor.GREEN),
                        "cleanEdges" : extractableQuery(qOwnedByBody(wire, EntityType.EDGE), "Edges of the cleaned wire."),
                        "sourceEdges" : extractableQuery(definition.sourceEdges, "The wire that was cleaned.", DebugColor.BLUE)
                    }
                });
    }, {
        "outputName" : "",
        "groups" : [],
        "mode" : CleanWireMode.MANUAL,
        "autoRuns" : [],
        "breakAt" : qNothing(),
        "cornerAngle" : 3 * degree,
        "forceTangency" : true,
        "sliverLength" : 0.5 * millimeter,
        "maxDeviation" : 0 * meter,
        "cpBefore" : 0, "cpAfter" : 0, "cpReduction" : 0,
        "edgesBefore" : 0, "edgesAfter" : 0, "edgeReduction" : 0,
        "tangencyFixes" : 0,
        "showDeviation" : false,
        "showRuns" : false,
        "deleteInput" : false,
        "projectOnPlane" : false,
        "projectionPlane" : qNothing(),
        "planDegree" : 3,
        "planTolerance" : 1e-5 * meter,
        "planMaxCPs" : 30,
        "constrainToWall" : true,
        "debugShowPolygons" : false,
        "debugShowGroups" : false,
        "approximationDegree" : 3,
        "approximationTolerance" : 1e-5 * meter,
        "approximationMaxCPs" : 30,
        "debugPrintJoints" : false,
        "debugPrintRuns" : false,
        "debugShowCorners" : false,
        "debugKeepPieces" : false
    });

/**
 * Editing logic: keep the read-only Runs array in step with what the classifier would do,
 * so the dialog in Auto shows the runs before the regen has fitted anything. Only the
 * chain and the joints are computed here (kernel evaluations, no fits, no ops); the
 * control points and deviations arrive from the regen as computed parameters.
 *
 * Runs only when something that changes the runs changed: the wire, the mode, the
 * corner angle, the tangency choice or the tolerance (which sets the sliver length).
 */
export function cleanWireEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map, hiddenBodies is Query, clickedButton is string) returns map
{
    if (clickedButton == "populateGroups")
    {
        return withPopulatedGroups(context, definition);
    }

    if (definition.mode != CleanWireMode.AUTO)
    {
        return definition;
    }

    if (clickedButton == "fitControlPoints")
    {
        return withFittedControlPoints(context, definition);
    }

    const relevant = ["sourceEdges", "mode", "cornerAngle", "forceTangency", "approximationTolerance", "sliverLength"];
    var changed = isCreating || size(definition.autoRuns) == 0;
    for (var key in relevant)
    {
        if (oldDefinition[key] != definition[key])
        {
            changed = true;
        }
    }
    if (!changed)
    {
        return definition;
    }

    var items = [];
    try silent
    {
        const edges = expandEdgeQuery(definition.sourceEdges);
        if (!isQueryEmpty(context, edges))
        {
            const chain = describeChain(context, edges);
            const joints = classifyJoints(context, definition, chain);
            const runs = buildRuns(definition, chain, joints, makeArray(size(chain.edges), undefined));
            for (var run in runs)
            {
                items = append(items, {
                            "ar_label" : autoRunLabel(chain, run),
                            "ar_kind" : (runIsExact(chain, run) || (run.length != undefined
                                    && run.length < definition.sliverLength))
                                ? "exact copy" : "fit",
                            "ar_cps" : 0,
                            "ar_deviation" : 0 * meter
                        });
            }
        }
    }
    catch
    {
        // A wire that does not chain yet (mid-selection) shows no runs rather than an error.
        items = [];
    }

    definition.autoRuns = items;
    return definition;
}

/**
 * Maximum control points set to what the largest run needs at the tolerance.
 *
 * Every fitted run is sampled and fitted exactly as the regen would, with the cap at the
 * library maximum, and the largest count wins. A run that needs more than the library
 * allows leaves the cap at the maximum and is reported by the regen's warning.
 */
function withFittedControlPoints(context is Context, definition is map) returns map
{
    var needed = 4;

    try silent
    {
        const edges = expandEdgeQuery(definition.sourceEdges);
        if (!isQueryEmpty(context, edges))
        {
            const chain = describeChain(context, edges);
            const joints = classifyJoints(context, definition, chain);
            const runs = buildRuns(definition, chain, joints, makeArray(size(chain.edges), undefined));
            const settings = mergeMaps(approximationSettings(definition), {
                        "approximationMaxCPs" : MAX_CONTROL_POINTS,
                        "debugFit" : false
                    });

            for (var run in runs)
            {
                if (runIsExact(chain, run) || (run.length != undefined && run.length < definition.sliverLength))
                {
                    continue;
                }

                const sampled = runSamples(context, chain, run, settings.approximationTolerance);
                const points = withoutRepeats(sampled.points, fitRepeatTolerance(sampled.points));
                const curves = approximateFamily(context, [{
                                    "points" : points,
                                    "startDerivative" : chain.edges[run.first].startTangent,
                                    "endDerivative" : chain.edges[run.last].endTangent
                                }], settings);
                needed = max(needed, size(curves[0].controlPoints));
            }
        }
    }
    catch
    {
        return definition;
    }

    definition.approximationMaxCPs = clamp(needed, 4, MAX_CONTROL_POINTS);
    return definition;
}

/**
 * The groups replaced by one per fitted run, each budgeted at what it needs.
 *
 * The runs come from the same classifier Auto uses, so Manual starts where Auto would
 * have ended and the user edits from there. Exact runs (lines, arcs, sliver pieces) get
 * no group: Manual copies ungrouped edges as they are. The edges are stored as robust
 * queries -- the chain's own are transient and would not survive the dialog.
 */
function withPopulatedGroups(context is Context, definition is map) returns map
{
    var groups = [];

    try silent
    {
        const edges = expandEdgeQuery(definition.sourceEdges);
        if (!isQueryEmpty(context, edges))
        {
            const chain = describeChain(context, edges);
            const joints = classifyJoints(context, definition, chain);
            const runs = buildRuns(definition, chain, joints, makeArray(size(chain.edges), undefined));
            const settings = mergeMaps(approximationSettings(definition), {
                        "approximationMaxCPs" : MAX_CONTROL_POINTS,
                        "debugFit" : false
                    });

            for (var k = 0; k < size(runs); k += 1)
            {
                const run = runs[k];
                if (runIsExact(chain, run) || (run.length != undefined && run.length < definition.sliverLength))
                {
                    continue;
                }

                var members = [];
                for (var i = run.first; i <= run.last; i += 1)
                {
                    members = append(members, chain.edges[i].query);
                }

                const sampled = runSamples(context, chain, run, settings.approximationTolerance);
                const points = withoutRepeats(sampled.points, fitRepeatTolerance(sampled.points));
                const curves = approximateFamily(context, [{
                                    "points" : points,
                                    "startDerivative" : chain.edges[run.first].startTangent,
                                    "endDerivative" : chain.edges[run.last].endTangent
                                }], settings);

                const start = chain.edges[run.first].startPoint / millimeter;
                groups = append(groups, {
                            "cw_edges" : qUnion(makeRobustQueriesBatched(context, qUnion(members))),
                            "cw_name" : "Run " ~ toString(k) ~ " @ " ~ toString(roundToPrecision(start[0], 1))
                                ~ ", " ~ toString(roundToPrecision(start[1], 1)),
                            "cw_mode" : CleanApproximation.MAX_CP,
                            "cw_maxCPs" : clamp(size(curves[0].controlPoints), 4, MAX_CONTROL_POINTS),
                            "cw_tolerance" : settings.approximationTolerance
                        });
            }
        }
    }
    catch
    {
        return definition;
    }

    definition.groups = groups;
    return definition;
}

/**
 * The label of one detected run: its edges and where it starts.
 */
function autoRunLabel(chain is map, run is map) returns string
{
    const start = chain.edges[run.first].startPoint / millimeter;
    return "edges " ~ toString(run.first) ~ ".." ~ toString(run.last) ~ " from "
        ~ toString(roundToPrecision(start[0], 1)) ~ ", " ~ toString(roundToPrecision(start[1], 1))
        ~ ", " ~ toString(roundToPrecision(start[2], 1)) ~ " mm";
}

/**
 * Fill the Runs items the editing logic created with what the regen measured. An item
 * that the editing logic has not created yet (the array is shorter than the run list)
 * cannot be written; the next dialog change re-creates them.
 */
function reportAutoRuns(context is Context, id is Id, definition is map, reports is array)
{
    const count = min(size(definition.autoRuns), size(reports));
    for (var k = 0; k < count; k += 1)
    {
        setFeatureComputedParameter(context, id, { "name" : "autoRuns[" ~ toString(k) ~ "].ar_cps", "value" : reports[k].controlPoints });
        setFeatureComputedParameter(context, id, { "name" : "autoRuns[" ~ toString(k) ~ "].ar_deviation", "value" : reports[k].deviation });
        setFeatureComputedParameter(context, id, { "name" : "autoRuns[" ~ toString(k) ~ "].ar_kind",
                "value" : reports[k].kind == "exact" ? "exact copy" : "fit" });
    }
}

/**
 * One chain into one wire: classify, group, run, emit, extract, and check that it came
 * out as one body. Shared by the 3D pass and the plan-view pass, which differ only in
 * the chain handed in (see projectChain) and the fitter settings.
 *
 * @param base {Id} : run ids are `base + ("run" ~ k)`, the wire `base + "wire"`.
 * @param label {string} : "" for the 3D pass, "plan" for the projected one (prints).
 * @returns {map} : { "wire" : Query, "runs", "reports", "grouped" : { joints, groupOfEdge } }
 */
function cleanChain(context is Context, id is Id, base is Id, definition is map, chain is map, approximation is map,
    label is string) returns map
{
    // Auto is the classifier and the global settings alone; groups and breaks are the
    // Manual definition and are not read there, so what the dialog shows is what drives.
    const auto = definition.mode == CleanWireMode.AUTO;
    const classified = classifyJoints(context, definition, chain);
    const joints = auto ? classified : applyBreaks(context, definition, chain, classified);
    const grouped = auto
        ? { "joints" : joints, "groupOfEdge" : makeArray(size(chain.edges), undefined) }
        : applyGroups(context, definition, chain, joints);
    const runs = buildRuns(definition, chain, grouped.joints, grouped.groupOfEdge);

    if (definition.debugPrintJoints)
    {
        if (label != "")
        {
            println("[" ~ label ~ "]");
        }
        printChain(chain, grouped.joints, grouped.groupOfEdge);
    }

    var created = [];
    var reports = [];
    for (var k = 0; k < size(runs); k += 1)
    {
        const runId = base + ("run" ~ k);
        const report = emitRun(context, runId, definition, chain, runs[k], approximation);
        created = append(created, qCreatedBy(runId, EntityType.EDGE));
        reports = append(reports, report);
    }

    if (definition.debugPrintRuns)
    {
        if (label != "")
        {
            println("[" ~ label ~ "]");
        }
        printRuns(definition, reports);
    }

    const wireId = base + "wire";
    opExtractWires(context, wireId, { "edges" : qUnion(created) });

    // One chain in, one wire out. More than one means two runs failed to meet at a
    // vertex, and the gap is worth naming: it is a snapping defect, not a user error.
    const wireCount = size(evaluateQuery(context, qCreatedBy(wireId, EntityType.BODY)));
    if (wireCount != 1)
    {
        var gaps = [];
        for (var k = 1; k < size(reports); k += 1)
        {
            const gap = runGap(context, base + ("run" ~ (k - 1)), base + ("run" ~ k));
            if (gap > OFFSET_GEOM_TOL)
            {
                gaps = append(gaps, "runs " ~ toString(k - 1) ~ "|" ~ toString(k) ~ ": " ~ fmtMM(gap, 4, 0) ~ " mm");
            }
        }
        reportFeatureWarning(context, id, "The " ~ (label == "" ? "cleaned" : label) ~ " wire came out as " ~ toString(wireCount)
            ~ " bodies instead of one. Gaps between runs: " ~ (size(gaps) == 0 ? "none found" : join(gaps, ", ")) ~ ".");
    }

    if (!definition.debugKeepPieces)
    {
        opDeleteBodies(context, base + "cleanup", { "entities" : qOwnerBody(qUnion(created)) });
    }

    return { "wire" : qCreatedBy(wireId, EntityType.BODY), "runs" : runs, "reports" : reports, "grouped" : grouped };
}

/**
 * The chain as it looks projected onto a plane: every point and tangent projected, every
 * length and curvature re-measured in the plane from projected stations.
 *
 * Projection is not a change of representation, it is a change of geometry: a pitch
 * kink vanishes (the ramp creases of the rout wire are corners in 3D and tangent in plan),
 * an edge climbing steeply shortens or becomes a sliver, a tilted circle becomes an
 * ellipse (so only LINE stays exact in plan), and curvature can go up as well as down,
 * which is why it is measured from projected stations rather than scaled from 3D. The
 * chain carries `plane`, and runSamples / emitRun read it.
 */
function projectChain(context is Context, id is Id, chain is map, pl is Plane) returns map
{
    var stationParams = [];
    for (var s = 0; s < CURVATURE_STATIONS; s += 1)
    {
        stationParams = append(stationParams, s / (CURVATURE_STATIONS - 1));
    }

    // Every edge into monotone pieces of the projection. A piece carries the fraction of
    // the source edge it covers (spanFrom / spanTo, along the chain), its projected
    // stations, and everything the pipeline reads off an edge.
    var pieces = [];
    var lowest = undefined;
    var highest = undefined;

    for (var k = 0; k < size(chain.edges); k += 1)
    {
        const edge = chain.edges[k];
        const lines = evEdgeTangentLines(context, { "edge" : edge.query, "parameters" : stationParams });

        var points = [];
        for (var s = 0; s < CURVATURE_STATIONS; s += 1)
        {
            const at = edge.flipped ? CURVATURE_STATIONS - 1 - s : s;
            const height = dot(lines[at].origin - pl.origin, pl.normal);
            lowest = (lowest == undefined) ? height : min(lowest, height);
            highest = (highest == undefined) ? height : max(highest, height);
            points = append(points, projectOnto(lines[at].origin, pl));
        }

        // Cusps: a station where the projected chord reverses against the one before.
        var cuts = [0];
        for (var s = 2; s < CURVATURE_STATIONS; s += 1)
        {
            const before = points[s - 1] - points[s - 2];
            const after = points[s] - points[s - 1];
            if (norm(before) > OFFSET_GEOM_TOL && norm(after) > OFFSET_GEOM_TOL && dot(before, after) < 0 * meter * meter)
            {
                cuts = append(cuts, s - 1);
            }
        }
        cuts = append(cuts, CURVATURE_STATIONS - 1);

        for (var c = 0; c + 1 < size(cuts); c += 1)
        {
            const from = cuts[c];
            const to = cuts[c + 1];
            if (to - from < 2)
            {
                continue;
            }
            pieces = append(pieces, planPiece(edge, k, points, from, to));
        }
    }

    // Monotone stretches: cut where consecutive pieces meet at more than a right angle in
    // plan (a fold at a joint or at a cusp). The longest stretch by projected length is
    // the extent; the rest retraces it and is dropped.
    var stretches = [];
    var current = [];
    for (var i = 0; i < size(pieces); i += 1)
    {
        if (i > 0)
        {
            const turn = angleBetween(pieces[i - 1].endTangent, pieces[i].startTangent) / radian;
            if (turn > PI / 2)
            {
                stretches = append(stretches, current);
                current = [];
            }
        }
        current = append(current, pieces[i]);
    }
    stretches = append(stretches, current);

    var best = 0;
    var bestLength = -1 * meter;
    for (var i = 0; i < size(stretches); i += 1)
    {
        var length = 0 * meter;
        for (var piece in stretches[i])
        {
            length += piece.length;
        }
        if (length > bestLength)
        {
            bestLength = length;
            best = i;
        }
    }

    if (size(stretches) > 1)
    {
        var dropped = [];
        for (var i = 0; i < size(stretches); i += 1)
        {
            if (i == best)
            {
                continue;
            }
            for (var piece in stretches[i])
            {
                dropped = append(dropped, toString(piece.sourceIndex));
            }
        }
        reportFeatureInfo(context, id, "Plan view trimmed to the extent of the projection: the wire folds back on itself "
            ~ "on this plane, and edge(s) " ~ join(dropped, ", ") ~ " retrace it, so they were left out of the plan view.");
    }

    return { "edges" : stretches[best], "closed" : false, "plane" : pl,
            "normalExtent" : { "min" : lowest, "max" : highest } };
}

/**
 * One monotone piece of a projected edge, between stations `from` and `to`, described
 * the way the pipeline reads an edge. Length and curvature come from the projected
 * stations; the end tangents are the projected source tangents where the piece reaches
 * the source's ends, and the chord at a cusp otherwise.
 */
function planPiece(edge is map, sourceIndex is number, points is array, from is number, to is number) returns map
{
    var stations = [];
    var length = 0 * meter;
    for (var s = from; s <= to; s += 1)
    {
        stations = append(stations, points[s]);
        if (s > from)
        {
            length += norm(points[s] - points[s - 1]);
        }
    }

    const n = size(stations);
    var curvatures = [];
    for (var s = 0; s < n; s += 1)
    {
        const m = clamp(s, 1, n - 2);
        curvatures = append(curvatures, norm(curvatureThrough(stations[m - 1], stations[m], stations[m + 1])));
    }

    const startTangent = (from == 0)
        ? projectedDirection(edge.startTangent, undefined, stations[1] - stations[0])
        : normalize(stations[1] - stations[0]);
    const endTangent = (to == CURVATURE_STATIONS - 1)
        ? projectedDirection(edge.endTangent, undefined, stations[n - 1] - stations[n - 2])
        : normalize(stations[n - 1] - stations[n - 2]);

    return mergeMaps(edge, {
                "sourceIndex" : sourceIndex,
                "spanFrom" : from / (CURVATURE_STATIONS - 1),
                "spanTo" : to / (CURVATURE_STATIONS - 1),
                "length" : length,
                "curveType" : (edge.curveType == CurveType.LINE && from == 0 && to == CURVATURE_STATIONS - 1)
                    ? CurveType.LINE : CurveType.OTHER,
                "startPoint" : stations[0],
                "endPoint" : stations[n - 1],
                "startTangent" : startTangent,
                "endTangent" : endTangent,
                "curvatures" : curvatures
            });
}

function projectOnto(point is Vector, pl is Plane) returns Vector
{
    return point - dot(point - pl.origin, pl.normal) * pl.normal;
}

/**
 * A direction projected into the plane; where it was normal to the plane the projected
 * chord stands in for it.
 */
function projectedDirection(direction is Vector, pl, chord is Vector) returns Vector
{
    // The plan pieces are built from already-projected stations, so the chord is in the
    // plane; the source tangent is only consulted for its direction along that chord.
    if (pl == undefined)
    {
        return (norm(chord) > 0 * meter) ? normalize(chord) : direction;
    }
    const inPlane = direction - dot(direction, pl.normal) * pl.normal;
    if (norm(inPlane) > 1e-6)
    {
        return normalize(inPlane);
    }
    return (norm(chord) > 0 * meter) ? normalize(chord) : direction;
}

/**
 * The three fitter settings, as the utils fitter reads them.
 */
function approximationSettings(definition is map) returns map
{
    return {
            "approximationDegree" : definition.approximationDegree,
            "approximationTolerance" : definition.approximationTolerance,
            "approximationMaxCPs" : definition.approximationMaxCPs,
            "debugFit" : definition.debugPrintRuns
        };
}

// ============================================================================
// Chain
// ============================================================================

/**
 * The edges in chain order, each described once with batched kernel calls.
 *
 * Every direction below runs ALONG THE CHAIN: an edge the path traverses backwards has
 * its kernel tangents negated and its ends swapped, so a joint is always "the end of
 * the previous edge against the start of the next".
 *
 * @returns {map} : { "edges" : [{ query, flipped, length, curveType, startPoint, endPoint,
 *      startTangent, endTangent, curvatures (CURVATURE_STATIONS values along the chain) }],
 *      "closed" : boolean }
 */
function describeChain(context is Context, edges is Query) returns map
{
    var path;
    try silent
    {
        path = constructPath(context, edges, { "tolerance" : CLEAN_CHAIN_TOLERANCE }).path;
    }
    catch
    {
        throw regenError("The selected edges do not form one chain; they must meet end to end without branching.", edges);
    }

    var stationParams = [];
    for (var s = 0; s < CURVATURE_STATIONS; s += 1)
    {
        stationParams = append(stationParams, s / (CURVATURE_STATIONS - 1));
    }

    var described = [];
    for (var i = 0; i < size(path.edges); i += 1)
    {
        const edge = path.edges[i];
        const flipped = path.flipped[i];
        const ends = evEdgeTangentLines(context, { "edge" : edge, "parameters" : [0, 1] });
        const definition = evCurveDefinition(context, { "edge" : edge, "returnBSplinesAsOther" : true });
        const length = evLength(context, { "entities" : edge });
        const controlPoints = sourceControlPoints(context, edge, definition.curveType);
        const curvatureResults = evEdgeCurvatures(context, { "edge" : edge, "parameters" : stationParams });

        var curvatures = [];
        for (var s = 0; s < CURVATURE_STATIONS; s += 1)
        {
            const at = flipped ? CURVATURE_STATIONS - 1 - s : s;
            curvatures = append(curvatures, curvatureResults[at].curvature);
        }

        described = append(described, {
                    "query" : edge,
                    "flipped" : flipped,
                    "length" : length,
                    "curveType" : definition.curveType,
                    "controlPoints" : controlPoints,
                    "startPoint" : flipped ? ends[1].origin : ends[0].origin,
                    "endPoint" : flipped ? ends[0].origin : ends[1].origin,
                    "startTangent" : flipped ? -1 * ends[1].direction : ends[0].direction,
                    "endTangent" : flipped ? -1 * ends[0].direction : ends[1].direction,
                    "curvatures" : curvatures
                });
    }

    return { "edges" : described, "closed" : path.closed };
}

/**
 * How many control points a source edge is worth: 2 for a line, 3 for an arc, a spline's
 * own count, and for anything else the count the kernel's approximation of it needs.
 */
function sourceControlPoints(context is Context, edge is Query, curveType) returns number
{
    if (curveType == CurveType.LINE)
    {
        return 2;
    }
    if (curveType == CurveType.CIRCLE)
    {
        return 3;
    }

    const full = evCurveDefinition(context, { "edge" : edge });
    if (full is BSplineCurve)
    {
        return size(full.controlPoints);
    }
    return size(evApproximateBSplineCurve(context, { "edge" : edge }).controlPoints);
}

// ============================================================================
// Joints
// ============================================================================

/**
 * One record per joint (between edge j - 1 and edge j), decided from the angle the two
 * edges meet at. A sliver's own tangents are not consulted: the joints on either side of
 * it are decided together from the edges beyond it.
 *
 * @returns {array} : size(edges) - 1 records { "angle" (radians), "kind" ("tangent" /
 *      "near" / "corner"), "isBreak" : boolean, "sliver" : boolean, "why" : string }
 */
function classifyJoints(context is Context, definition is map, chain is map) returns array
{
    const edges = chain.edges;
    const sliverMax = definition.sliverLength;

    var joints = [];
    for (var j = 1; j < size(edges); j += 1)
    {
        joints = append(joints, jointRecord(definition, edges[j - 1].endTangent, edges[j].startTangent, false, ""));
    }

    // An exact source edge -- a line or an arc -- is a run of its own whatever its joints:
    // copied as it is, with its neighbours fitted up to it. Fitting a straight stretch
    // into a cubic with the bend beside it rings; keeping the line exact and handing the
    // spline its direction at the joint does not.
    for (var j = 0; j < size(joints); j += 1)
    {
        if (isExactEdge(edges[j]) || isExactEdge(edges[j + 1]))
        {
            joints[j] = mergeMaps(joints[j], { "isBreak" : true, "why" : "exact edge kept" });
        }
    }

    // Manual: the classification is printed for information, but every joint is kept
    // unless a group runs through it.
    if (definition.mode == CleanWireMode.MANUAL)
    {
        for (var j = 0; j < size(joints); j += 1)
        {
            joints[j] = mergeMaps(joints[j], { "isBreak" : true, "why" : "manual" });
        }
        return joints;
    }

    // Slivers: the joints either side of one are re-decided from the neighbours across it.
    for (var i = 1; i + 1 < size(edges); i += 1)
    {
        if (edges[i].length >= sliverMax)
        {
            continue;
        }

        var across = jointRecord(definition, edges[i - 1].endTangent, edges[i + 1].startTangent, true, "");

        // Tangent neighbours whose lines do not meet are a jog, not a fragment: the
        // sliver is the step between them and stays a piece of its own with corners.
        if (!across.isBreak)
        {
            const gapVector = edges[i + 1].startPoint - edges[i - 1].endPoint;
            const along = normalize(edges[i - 1].endTangent + edges[i + 1].startTangent);
            const lateral = norm(gapVector - dot(gapVector, along) * along);
            if (lateral > definition.approximationTolerance)
            {
                across = mergeMaps(across, { "kind" : "corner", "isBreak" : true });
            }
        }

        if (across.isBreak)
        {
            // A chamfer or a fillet at a corner: kept as its own piece, both joints corners.
            joints[i - 1] = mergeMaps(across, { "why" : "sliver at a corner, kept" });
            joints[i] = mergeMaps(across, { "why" : "sliver at a corner, kept" });
        }
        else
        {
            joints[i - 1] = mergeMaps(across, { "why" : "sliver absorbed" });
            joints[i] = mergeMaps(across, { "why" : "sliver absorbed" });
        }
    }

    return joints;
}

/**
 * A source edge the kernel holds exactly.
 */
function isExactEdge(edge is map) returns boolean
{
    return edge.curveType == CurveType.LINE || edge.curveType == CurveType.CIRCLE;
}

/**
 * The decision for one pair of along-chain tangents.
 */
function jointRecord(definition is map, before is Vector, after is Vector, sliver is boolean, why is string) returns map
{
    const angle = angleBetween(before, after) / radian;
    var kind = "corner";
    var isBreak = true;

    if (angle <= G1_JUNCTION_ANGLE)
    {
        kind = "tangent";
        isBreak = false;
    }
    else if (angle <= definition.cornerAngle / radian)
    {
        kind = "near";
        isBreak = !definition.forceTangency;
    }

    return { "angle" : angle, "kind" : kind, "isBreak" : isBreak, "sliver" : sliver, "why" : why };
}

// ============================================================================
// Breaks
// ============================================================================

/**
 * The user's break picks laid onto the chain: each one must coincide with a vertex of
 * the chain, and marks the joint there as kept. A pick on the chain's own end is
 * nothing to do. In auto mode this is where computed breaks will arrive.
 */
function applyBreaks(context is Context, definition is map, chain is map, joints is array) returns array
{
    if (isQueryEmpty(context, definition.breakAt))
    {
        return joints;
    }

    var decided = joints;
    const edges = chain.edges;

    for (var pick in evaluateQuery(context, definition.breakAt))
    {
        const picked = isQueryEmpty(context, qBodyType(pick, BodyType.MATE_CONNECTOR))
            ? evVertexPoint(context, { "vertex" : pick })
            : evMateConnector(context, { "mateConnector" : pick }).origin;
        // On the plan chain the vertices are projected, so the pick is too.
        const point = (chain.plane == undefined) ? picked : projectOnto(picked, chain.plane);

        var found = false;
        for (var j = 0; j + 1 < size(edges); j += 1)
        {
            if (norm(edges[j].endPoint - point) < 10 * OFFSET_GEOM_TOL)
            {
                decided[j] = mergeMaps(decided[j], { "isBreak" : true, "userBreak" : true, "why" : "break" });
                found = true;
                break;
            }
        }

        if (!found && norm(edges[0].startPoint - point) >= 10 * OFFSET_GEOM_TOL
            && norm(edges[size(edges) - 1].endPoint - point) >= 10 * OFFSET_GEOM_TOL)
        {
            throw regenError("A break is not on a vertex of the wire.", pick);
        }
    }

    return decided;
}

// ============================================================================
// Groups
// ============================================================================

/**
 * Lay the user's groups over the chain: which edge belongs to which group, and the
 * joints re-decided so that a group's ends are breaks and its interior is not.
 *
 * @returns {map} : { "joints", "groupOfEdge" : per edge the group index or undefined }
 */
function applyGroups(context is Context, definition is map, chain is map, joints is array) returns map
{
    const edges = chain.edges;
    var groupOfEdge = makeArray(size(edges), undefined);
    var decided = joints;
    var conflicts = [];

    for (var g = 0; g < size(definition.groups); g += 1)
    {
        const entry = definition.groups[g];
        const label = entry.cw_name == "" ? "group " ~ toString(g + 1) : "'" ~ entry.cw_name ~ "'";
        if (isQueryEmpty(context, entry.cw_edges))
        {
            continue;
        }

        var indices = [];
        for (var i = 0; i < size(edges); i += 1)
        {
            if (!isQueryEmpty(context, qIntersection([edges[i].query, entry.cw_edges])))
            {
                indices = append(indices, i);
            }
        }

        if (size(indices) == 0)
        {
            throw regenError("The edges of " ~ label ~ " are not part of the wire.",
                [faultyArrayParameterId("groups", g, "cw_edges")]);
        }

        indices = sort(indices, function(a, b) { return a - b; });
        const first = indices[0];
        const last = indices[size(indices) - 1];
        if (last - first + 1 != size(indices))
        {
            throw regenError("The edges of " ~ label ~ " are not contiguous along the wire.",
                [faultyArrayParameterId("groups", g, "cw_edges")]);
        }

        for (var i = first; i <= last; i += 1)
        {
            if (groupOfEdge[i] != undefined)
            {
                throw regenError("An edge of " ~ label ~ " is already in another group.",
                    [faultyArrayParameterId("groups", g, "cw_edges")]);
            }
            groupOfEdge[i] = g;
        }

        // Ends are breaks; the interior is fitted through, corners included -- the user
        // said so, and it is reported.
        if (first > 0)
        {
            decided[first - 1] = mergeMaps(decided[first - 1], { "isBreak" : true, "why" : "group start" });
        }
        if (last + 1 < size(edges))
        {
            decided[last] = mergeMaps(decided[last], { "isBreak" : true, "why" : "group end" });
        }
        for (var j = first; j < last; j += 1)
        {
            const inside = decided[j];

            // A break the user placed inside a group is a contradiction: one says
            // "keep this vertex", the other "fit through it". Reported, not resolved.
            if (inside.userBreak == true)
            {
                conflicts = append(conflicts, "the break at " ~ fmtVec(edges[j].endPoint / millimeter, 1, 0)
                    ~ " mm lies inside " ~ label);
            }

            decided[j] = mergeMaps(inside, {
                        "isBreak" : false,
                        "why" : inside.kind == "corner" ? "corner fitted through in " ~ label : "inside " ~ label
                    });
        }
    }

    if (size(conflicts) > 0)
    {
        throw regenError("Break and group conflict: " ~ join(conflicts, "; ") ~ ". Remove the break or shorten the group.",
            ["breakAt"]);
    }

    return { "joints" : decided, "groupOfEdge" : groupOfEdge };
}

// ============================================================================
// Runs
// ============================================================================

/**
 * Contiguous edge ranges between breaks.
 *
 * @returns {array} : { "first", "last" : edge indices inclusive, "group" : index or undefined }
 */
function buildRuns(definition is map, chain is map, joints is array, groupOfEdge is array) returns array
{
    const sliverMax = definition.sliverLength;
    var runs = [];
    var first = 0;

    for (var i = 0; i < size(chain.edges); i += 1)
    {
        const lastOfRun = (i == size(chain.edges) - 1) || joints[i].isBreak;
        if (!lastOfRun)
        {
            continue;
        }

        var length = 0 * meter;
        for (var k = first; k <= i; k += 1)
        {
            length += chain.edges[k].length;
        }

        // A run of nothing but slivers is not a run. Where the break in front of it came
        // from a group end or the classifier -- not from a corner or the user -- it joins
        // the run before it: two groups that end and start either side of a 0.09 mm
        // fragment did not mean to leave it on its own.
        const before = (first == 0) ? undefined : joints[first - 1];
        if (length < sliverMax && size(runs) > 0 && before != undefined
            && before.kind != "corner" && before.userBreak != true)
        {
            const previous = runs[size(runs) - 1];
            runs[size(runs) - 1] = mergeMaps(previous, { "last" : i });
        }
        else
        {
            runs = append(runs, { "first" : first, "last" : i, "group" : groupOfEdge[first], "length" : length });
        }
        first = i + 1;
    }

    return runs;
}

/**
 * Whether every edge of a run is an exact primitive the kernel can copy as such.
 */
function runIsExact(chain is map, run is map) returns boolean
{
    for (var i = run.first; i <= run.last; i += 1)
    {
        const t = chain.edges[i].curveType;
        if (t != CurveType.LINE && t != CurveType.CIRCLE)
        {
            return false;
        }
    }
    return true;
}

/**
 * The fitter settings for one run: the group's override, else the global ones.
 */
function runApproximation(definition is map, run is map, approximation is map) returns map
{
    if (run.group == undefined)
    {
        return approximation;
    }

    const entry = definition.groups[run.group];
    if (entry.cw_mode == CleanApproximation.MAX_CP)
    {
        return mergeMaps(approximation, { "approximationMaxCPs" : entry.cw_maxCPs });
    }
    if (entry.cw_mode == CleanApproximation.TOLERANCE)
    {
        return mergeMaps(approximation, { "approximationTolerance" : entry.cw_tolerance });
    }
    return approximation;
}

// ============================================================================
// Sampling
// ============================================================================

/**
 * Sample points along one run at a curvature-adaptive spacing.
 *
 * The chord between samples at spacing s on curvature kappa misses the curve by
 * kappa s^2 / 8; spending half the tolerance on that gives s = sqrt(4 tol / kappa),
 * clamped so a straight stretch still carries shape samples and a tight fillet cannot
 * demand thousands. The density 1 / s is integrated along each edge from its curvature
 * stations, and ceil(integral) samples are placed at equal increments of the cumulative
 * density, both edge ends included -- so every gap is between half and one and a half
 * spacings and the fitter never sees a short end segment (correction 23).
 *
 * @returns {map} : { "points" : array, "perEdge" : sample counts }
 */
function runSamples(context is Context, chain is map, run is map, tolerance is ValueWithUnits) returns map
{
    var points = [];
    var perEdge = [];

    for (var i = run.first; i <= run.last; i += 1)
    {
        const edge = chain.edges[i];
        const stationCount = size(edge.curvatures);
        const step = edge.length / (stationCount - 1);

        // Cumulative density over the stations, along the chain.
        var cumulative = [0];
        for (var s = 1; s < stationCount; s += 1)
        {
            const density = 0.5 * (1 / spacingFor(edge.curvatures[s - 1], tolerance, edge.length)
                    + 1 / spacingFor(edge.curvatures[s], tolerance, edge.length));
            cumulative = append(cumulative, cumulative[s - 1] + density * step);
        }

        const total = cumulative[stationCount - 1];
        const count = clamp(ceil(total), SAMPLE_MIN_PER_EDGE, SAMPLE_MAX_PER_EDGE);

        // Along-chain fractions at equal density increments, inverted piecewise-linearly.
        var fractions = [];
        var station = 1;
        for (var k = 0; k < count; k += 1)
        {
            const target = total * k / (count - 1);
            while (station < stationCount - 1 && cumulative[station] < target)
            {
                station += 1;
            }
            const span = cumulative[station] - cumulative[station - 1];
            const within = (span <= 0) ? 0 : clamp((target - cumulative[station - 1]) / span, 0, 1);
            fractions = append(fractions, (station - 1 + within) / (stationCount - 1));
        }

        // Kernel parameters: a plan piece covers only part of its source edge, and an edge
        // traversed backwards reads its fractions from the far end.
        const spanFrom = (edge.spanFrom == undefined) ? 0 : edge.spanFrom;
        const spanTo = (edge.spanTo == undefined) ? 1 : edge.spanTo;
        var parameters = [];
        for (var fraction in fractions)
        {
            const along = spanFrom + fraction * (spanTo - spanFrom);
            parameters = append(parameters, edge.flipped ? 1 - along : along);
        }

        const lines = evEdgeTangentLines(context, { "edge" : edge.query, "parameters" : parameters });
        for (var tangentLine in lines)
        {
            points = append(points, chain.plane == undefined ? tangentLine.origin : projectOnto(tangentLine.origin, chain.plane));
        }
        perEdge = append(perEdge, count);
    }

    return { "points" : points, "perEdge" : perEdge };
}

/**
 * The sample spacing a curvature earns, see runSamples.
 */
function spacingFor(curvature, tolerance is ValueWithUnits, edgeLength is ValueWithUnits) returns ValueWithUnits
{
    const kappa = abs(curvature);
    var spacing = SAMPLE_MAX_SPACING;
    if (kappa * meter > 1e-9)
    {
        spacing = sqrt(4 * tolerance / kappa);
    }
    // A sliver shorter than twice the minimum spacing gets the minimum: the bounds must
    // not cross, and its sample count is floored to SAMPLE_MIN_PER_EDGE anyway.
    const upper = max(SAMPLE_MIN_SPACING, min(SAMPLE_MAX_SPACING, edgeLength / 2));
    return clamp(spacing, SAMPLE_MIN_SPACING, upper);
}

// ============================================================================
// Emission
// ============================================================================

/**
 * One run into geometry under `runId`: an exact copy of primitive edges, or one fitted
 * spline through the run's samples with the kernel's end tangents.
 *
 * @returns {map} : the report line's data.
 */
function emitRun(context is Context, runId is Id, definition is map, chain is map, run is map,
    approximation is map) returns map
{
    const first = chain.edges[run.first];
    const last = chain.edges[run.last];
    const edgeCount = run.last - run.first + 1;

    // Manual mode copies everything outside a group as it is, whatever its type; so is a
    // run that is nothing but slivers (a chamfer or fillet fragment at a corner), whose
    // three samples would fit to noise.
    const sliverMax = definition.sliverLength;
    if (runIsExact(chain, run)
        || (definition.mode == CleanWireMode.MANUAL && run.group == undefined)
        || (run.length != undefined && run.length < sliverMax))
    {
        if (chain.plane != undefined)
        {
            // Nothing in plan view can be copied from the source; a line is a line
            // between its projected ends, and anything else exact-in-3D but not a line
            // here is fitted instead.
            if (!runIsExact(chain, run) && !(run.length != undefined && run.length < sliverMax))
            {
                return emitFittedRun(context, runId, definition, chain, run, approximation);
            }
            var copied = 0;
            for (var i = run.first; i <= run.last; i += 1)
            {
                const edge = chain.edges[i];
                if (norm(edge.endPoint - edge.startPoint) > OFFSET_GEOM_TOL)
                {
                    emitLineCurve(context, runId + ("line" ~ i), edge.startPoint, edge.endPoint);
                    copied += 2;
                }
            }
            return { "kind" : "exact", "edges" : edgeCount, "first" : run.first, "last" : run.last,
                    "samples" : 0, "controlPoints" : copied, "deviation" : 0 * meter, "tolerance" : undefined,
                    "group" : run.group, "start" : first.startPoint };
        }

        var members = [];
        for (var i = run.first; i <= run.last; i += 1)
        {
            members = append(members, chain.edges[i].query);
        }
        opExtractWires(context, runId, { "edges" : qUnion(members) });

        var copied = 0;
        for (var i = run.first; i <= run.last; i += 1)
        {
            copied += chain.edges[i].controlPoints;
        }

        return { "kind" : "exact", "edges" : edgeCount, "first" : run.first, "last" : run.last,
                "samples" : 0, "controlPoints" : copied, "deviation" : 0 * meter, "tolerance" : undefined,
                "group" : run.group, "start" : first.startPoint };
    }

    return emitFittedRun(context, runId, definition, chain, run, approximation);
}

/**
 * One run fitted through its samples with the chain's end tangents.
 */
function emitFittedRun(context is Context, runId is Id, definition is map, chain is map, run is map,
    approximation is map) returns map
{
    const first = chain.edges[run.first];
    const last = chain.edges[run.last];
    const edgeCount = run.last - run.first + 1;
    const settings = runApproximation(definition, run, approximation);
    const sampled = runSamples(context, chain, run, settings.approximationTolerance);
    const points = withoutRepeats(sampled.points, fitRepeatTolerance(sampled.points));

    const curves = approximateFamily(context, [{
                        "points" : points,
                        "startDerivative" : first.startTangent,
                        "endDerivative" : last.endTangent
                    }], settings);
    const curve = curves[0];

    if (curve.isRational == true)
    {
        throw regenError("The fitter returned a rational curve, which this feature does not expect.");
    }

    emitFittedCurve(context, runId, curve, points, settings);

    return { "kind" : "fit", "edges" : edgeCount, "first" : run.first, "last" : run.last,
            "samples" : size(points), "controlPoints" : size(curve.controlPoints),
            "deviation" : fitDeviation(curve, points), "tolerance" : settings.approximationTolerance,
            "group" : run.group, "curve" : curve, "start" : first.startPoint };
}

/**
 * The fit measured at its own samples. approximateFamily pinned every sample to a
 * chord-length parameter, so the curve at those parameters IS the fit's claim about
 * each point.
 */
function fitDeviation(curve is BSplineCurve, points is array) returns ValueWithUnits
{
    var chords = [0 * meter];
    var chord = 0 * meter;
    for (var i = 1; i < size(points); i += 1)
    {
        chord += norm(points[i] - points[i - 1]);
        chords = append(chords, chord);
    }
    if (chord <= 0 * meter)
    {
        return 0 * meter;
    }

    var parameters = [];
    for (var i = 0; i < size(points); i += 1)
    {
        parameters = append(parameters, chords[i] / chord);
    }

    const evaluated = evaluateSpline({ "spline" : curve, "parameters" : parameters, "nDerivatives" : 0 })[0];
    var worst = 0 * meter;
    for (var i = 0; i < size(points); i += 1)
    {
        worst = max(worst, norm(evaluated[i] - points[i]));
    }
    return worst;
}

// ============================================================================
// Reporting
// ============================================================================

/**
 * The Reduction group: what the wire was and what it became, as read-only fields.
 */
function reportReduction(context is Context, id is Id, chain is map, joints is array, reports is array)
{
    var cpBefore = 0;
    for (var edge in chain.edges)
    {
        cpBefore += edge.controlPoints;
    }

    var cpAfter = 0;
    var edgesAfter = 0;
    for (var report in reports)
    {
        cpAfter += report.controlPoints;
        edgesAfter += (report.kind == "exact") ? report.edges : 1;
    }

    var fixes = 0;
    for (var joint in joints)
    {
        if (joint.kind == "near" && !joint.isBreak)
        {
            fixes += 1;
        }
    }

    const edgesBefore = size(chain.edges);

    setFeatureComputedParameter(context, id, { "name" : "cpBefore", "value" : cpBefore });
    setFeatureComputedParameter(context, id, { "name" : "cpAfter", "value" : cpAfter });
    setFeatureComputedParameter(context, id, { "name" : "cpReduction", "value" : reductionPercent(cpBefore, cpAfter) });
    setFeatureComputedParameter(context, id, { "name" : "edgesBefore", "value" : edgesBefore });
    setFeatureComputedParameter(context, id, { "name" : "edgesAfter", "value" : edgesAfter });
    setFeatureComputedParameter(context, id, { "name" : "edgeReduction", "value" : reductionPercent(edgesBefore, edgesAfter) });
    setFeatureComputedParameter(context, id, { "name" : "tangencyFixes", "value" : fixes });
}

function reductionPercent(before is number, after is number) returns number
{
    return (before == 0) ? 0 : roundToPrecision(100 * (before - after) / before, 1);
}

/**
 * A run named the way the user thinks of it: by its group where it has one, otherwise by
 * its edges and where it starts.
 */
function runLabel(definition is map, report is map, k is number) returns string
{
    if (report.group != undefined)
    {
        const name = definition.groups[report.group].cw_name;
        return "'" ~ (name == "" ? "group " ~ toString(report.group + 1) : name) ~ "'";
    }
    return "run " ~ toString(k) ~ " (edges " ~ toString(report.first) ~ ".." ~ toString(report.last)
        ~ ", from " ~ fmtVec(report.start / millimeter, 1, 0) ~ " mm)";
}

/**
 * The feature's own notices: what was decided for the user, and anything short of the
 * tolerance -- that is missing shape, so a warning.
 */
function reportOutcome(context is Context, id is Id, definition is map, chain is map, joints is array, runs is array,
    reports is array, measured is ValueWithUnits)
{
    var short = [];
    for (var k = 0; k < size(reports); k += 1)
    {
        const report = reports[k];
        if (report.kind == "fit" && report.deviation > 1.0001 * report.tolerance)
        {
            short = append(short, runLabel(definition, report, k) ~ " by " ~ fmtMM(report.deviation, 4, 0) ~ " mm at "
                ~ toString(report.controlPoints) ~ " CPs");
        }
    }

    var cornersFitted = 0;
    for (var joint in joints)
    {
        if (joint.kind == "corner" && !joint.isBreak)
        {
            cornersFitted += 1;
        }
    }

    if (size(short) > 0)
    {
        reportFeatureWarning(context, id, "Short of tolerance: " ~ join(short, "; ")
            ~ ". Raise the control points there or split the stretch. Max deviation from the source " ~ fmtMM(measured, 4, 0) ~ " mm.");
        return;
    }

    if (cornersFitted > 0)
    {
        reportFeatureInfo(context, id, toString(size(chain.edges)) ~ " edge(s) -> " ~ toString(size(runs))
            ~ "; " ~ toString(cornersFitted) ~ " corner(s) inside a group were fitted through.");
    }
}

/**
 * Print every edge and every joint decision.
 */
function printChain(chain is map, joints is array, groupOfEdge is array)
{
    println("");
    println("========== clean wire: chain ==========");
    println("   edge        length  type     start (mm)                          group");
    for (var i = 0; i < size(chain.edges); i += 1)
    {
        const edge = chain.edges[i];
        println(padLeft(toString(i), 7) ~ fmtMM(edge.length, 3, 14) ~ "  " ~ padLeft(curveTypeName(edge.curveType), 7)
            ~ "  " ~ fmtVec(edge.startPoint / millimeter, 2, 10) ~ (edge.flipped ? "  (reversed)" : "            ")
            ~ (groupOfEdge[i] == undefined ? "" : "  group " ~ toString(groupOfEdge[i])));
    }
    println("");
    println("  joint   angle(deg)  kind      break  note");
    for (var j = 0; j < size(joints); j += 1)
    {
        const joint = joints[j];
        println(padLeft(toString(j) ~ "|" ~ toString(j + 1), 8) ~ fmtNum(joint.angle * 180 / PI, 3, 12)
            ~ "  " ~ padLeft(joint.kind, 8) ~ "  " ~ padLeft(joint.isBreak ? "yes" : "no", 5)
            ~ "  " ~ joint.why);
    }
    println("========== end chain ==========");
}

/**
 * Print every run as emitted.
 */
function printRuns(definition is map, reports is array)
{
    println("");
    println("========== clean wire: runs ==========");
    println("    run  edges      kind   samples  CPs   deviation (mm)   starts at (mm)               group");
    for (var k = 0; k < size(reports); k += 1)
    {
        const r = reports[k];
        const name = (r.group == undefined) ? "" : definition.groups[r.group].cw_name;
        println(padLeft(toString(k), 7) ~ padLeft(toString(r.first) ~ ".." ~ toString(r.last), 7) ~ "  " ~ padLeft(r.kind, 8)
            ~ padLeft(toString(r.samples), 9) ~ padLeft(toString(r.controlPoints), 5)
            ~ fmtMM(r.deviation, 4, 16) ~ "   " ~ fmtVec(r.start / millimeter, 1, 9)
            ~ (r.group == undefined ? "" : "   " ~ toString(r.group) ~ (name == "" ? "" : " " ~ name)));
    }
    println("========== end runs ==========");
}

function curveTypeName(curveType) returns string
{
    if (curveType == CurveType.LINE) { return "line"; }
    if (curveType == CurveType.CIRCLE) { return "circle"; }
    if (curveType == CurveType.ELLIPSE) { return "ellipse"; }
    return "other";
}

/**
 * The smallest distance between the end vertices of two consecutive run bodies.
 */
function runGap(context is Context, previousId is Id, nextId is Id) returns ValueWithUnits
{
    const before = evaluateQuery(context, qCreatedBy(previousId, EntityType.VERTEX));
    const after = evaluateQuery(context, qCreatedBy(nextId, EntityType.VERTEX));
    var best = undefined;
    for (var a in before)
    {
        for (var b in after)
        {
            const d = norm(evVertexPoint(context, { "vertex" : a }) - evVertexPoint(context, { "vertex" : b }));
            if (best == undefined || d < best)
            {
                best = d;
            }
        }
    }
    return best == undefined ? 0 * meter : best;
}

/**
 * The control polygon of every fitted run, one batched debug feature per colour so a
 * hundred polygon legs cost four calls and no sketch solves.
 */
function showPolygons(context is Context, id is Id, reports is array)
{
    const colours = [DebugColor.RED, DebugColor.BLUE, DebugColor.GREEN, DebugColor.MAGENTA];

    for (var c = 0; c < size(colours); c += 1)
    {
        const featureId = id + ("colour" ~ c);
        var drawn = 0;

        startFeature(context, featureId, {});
        for (var k = c; k < size(reports); k += size(colours))
        {
            const curve = reports[k].curve;
            if (curve == undefined)
            {
                continue;
            }
            for (var i = 1; i < size(curve.controlPoints); i += 1)
            {
                if (norm(curve.controlPoints[i] - curve.controlPoints[i - 1]) > OFFSET_GEOM_TOL)
                {
                    emitLineCurve(context, featureId + ("run" ~ k ~ "leg" ~ i), curve.controlPoints[i - 1], curve.controlPoints[i]);
                    drawn += 1;
                }
            }
        }
        if (drawn > 0)
        {
            addDebugEntities(context, qCreatedBy(featureId, EntityType.EDGE), colours[c]);
        }
        abortFeature(context, featureId);
    }
}

/**
 * Colour every edge of the finished wire by its run. The wire's edges are copies, so
 * each run's edge is found as the one nearest the run's own midpoint sample.
 */
function showRuns(context is Context, wire is Query, chain is map, runs is array)
{
    const colours = [DebugColor.RED, DebugColor.BLUE, DebugColor.GREEN, DebugColor.MAGENTA, DebugColor.CYAN, DebugColor.YELLOW];
    const edges = qOwnedByBody(wire, EntityType.EDGE);

    for (var k = 0; k < size(runs); k += 1)
    {
        const run = runs[k];
        const middle = chain.edges[floor((run.first + run.last) / 2)];
        const probe = evEdgeTangentLines(context, { "edge" : middle.query, "parameters" : [0.5] })[0].origin;
        addDebugEntities(context, qClosestTo(edges, probe), colours[k % size(colours)]);
    }
}

/**
 * Every group in its own colour: its source edges, and the output edge its run became.
 * The colour is the group's index in the array, so it matches the group's position in
 * the dialog and does not change when a run elsewhere is added or removed.
 */
function showGroups(context is Context, definition is map, wire is Query, chain is map, runs is array)
{
    const colours = [DebugColor.RED, DebugColor.GREEN, DebugColor.BLUE, DebugColor.CYAN,
            DebugColor.MAGENTA, DebugColor.YELLOW, DebugColor.BLACK, DebugColor.ORANGE];
    const outputEdges = qOwnedByBody(wire, EntityType.EDGE);

    for (var g = 0; g < size(definition.groups); g += 1)
    {
        const colour = colours[g % size(colours)];
        const entry = definition.groups[g];
        if (!isQueryEmpty(context, entry.cw_edges))
        {
            addDebugEntities(context, entry.cw_edges, colour);
        }

        for (var run in runs)
        {
            if (run.group != g)
            {
                continue;
            }
            const middle = chain.edges[floor((run.first + run.last) / 2)];
            const probe = evEdgeTangentLines(context, { "edge" : middle.query, "parameters" : [0.5] })[0].origin;
            addDebugEntities(context, qClosestTo(outputEdges, probe), colour);
        }
    }
}

/**
 * A debug point at every kept vertex.
 */
function showCorners(context is Context, chain is map, joints is array)
{
    for (var j = 0; j < size(joints); j += 1)
    {
        if (joints[j].isBreak)
        {
            addDebugPoint(context, chain.edges[j].endPoint, DebugColor.RED);
        }
    }
}
