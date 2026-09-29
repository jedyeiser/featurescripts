FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

// IMPORT: edge_offset_utils.fs (same document; export-imports curve_core: chains, classifyPoints, emitters)
export import(path : "a2665e22c07b7a6929ce4e80", version : "3356bea0c847dcdb94230682");
// IMPORT: undrape_utils.fs (same document; the undrape map)
import(path : "283b8f7562a16e9c9ccc01b7", version : "79fd4945bcc0c6a0ab087fa3");
// IMPORT: unwrap_part.fs (same document; solid unwrap)
import(path : "fc976128871c5b4b2d33a91c", version : "3c9cb25ab3cef8319e1ac969");
// IMPORT: Variable_tools V2 extract_outputs.fs (embedStandardOutputs, extractable wrappers)
import(path : "a47f90bfa6b17a59e20cebd0/eb9b32c556ff036c3dd19f73/3cac74f0bc2b98272db13cd3", version : "cffacd73d80aa6dc1a2c4273");

/**
 * Takes edges to unwrap, a wrapped reference curve, a wrapped alignment point (mate connector), unwrapped origin (mate connector)
 * Unwraps the edges to unwrap, preserving length along the wrapped reference curve. 
 * Tests final edges for circularity. 
 *      Painful extraction process in this case, but a value added one
 *          Test - does chaing to an arc affect the tangent vector? How 'arclike' is each edge? We'd like to test for this quickly - filter results. Can we do this by simply unwrapping control points? That would be much faster. 
 * 
 * This will be both a more general and more specific version of our deform feature. 
 * More general because it will accept wires, surfaces, composite parts, parts and mate connectors as input. 
 * More specific because we add the restriction that our to/from curves (or geometry/input type, really) be planar tangent chains, and that the planes of our respective curves are parallel (they will most often be coplanar)
 * More general because we support general deformation/unwrapping, similar to deform (with the restrictions above) as well as some specific cases
 * More specific because we support how we unwrap specific geometries {Thickened: Part is constant thickness. Find 'center surface', deform that surface and rethicken
 *                                                                      Preserve Neutral Axis/Length_curve:
 *                                                                              (preserve the length of the neutral axis of the part, but the neutral axis may itself not be flat in our new geometry. 
 *                                                                              Need to select faces which will then be wrapped flat - with their shapes being driven by preserving the neutral axis}
 *                                                                              Need to write a seperate feature that calculates neutral axis. Users should be able to use this feature. 
 *                                                                              Can rely on a mid-profile from our evaluate profiles feature rather than a neutral axis for speed. 
 * When unwrapping, we test to see if we can convert edges into lines or arcs while preserving continuity.
 * When unwrapping, we enforce that the edges/faces that unwrap onto the specified plane are planar
 * 
 * Users can supply mate connectors (allow implicit creation), planar faces or planes as an unwrap face. 
 * 
 * When a composite part is supplied, limit input to one composite part. Extract components of that composite part and populate an array variable with deformation types for each body, unless we can easily and robustly test for cases (I doubt it)
 * 
 * WIRE
 *      Allow projection onto a plane NORMAL to the plane onto which we are unwrapping to get the 'profile' of the wire to deform. Support cases where the wire turns back on itself - but if it does so, it must 'hold' the same profile as the other edges on the profile plane projection
 *      Support finding lines, arcs
 * 
 * MATE CONNECTOR
 *      Move/rotate the mate connector so that it retains the correct placement in the new coordinate system/flattend system
 * 
 * SURFACE
 *      Allow projection/intersection onto a plane NORMAL to the unwrap plane. Throw an error when there is no determinate result. 
 *      Can be type THICKEN
 *          Test for contsant thickness
 *          Solve for interior surface
 *          If the surface needs to be UNDDRAPED (deformed such that its edges all fall within an extruded surface (extrude direction is normal to unwrap plane)  (or a mathematical representation therof) of a supplied valid profile (to which the user can supply an offset - in which case show offset in magenta)
 *          Unwrap undraped 'extruded' (or the surface was that way to start) onto plane half thickness above (may need to flip/toggle) unwrap plane
 *          Test for lines/arcs. convert/sketch/extract where appropriate
 *          Thicken surface
 * PART
 *      Support 'planar deform' as default
 *      Support providing a 'preserve length profile' {MIDDLE, NEUTRAL_AXIS, QUERY (user provides a valid profile. Support offsetting. When offseting, show offset profile in CYAN)}
 *      Unwrap part, preserving length measured along the preserve_length_profile.
 *      Optionally, allow for selected faces to be the ones that get projected flat. We may be able to solve for these faces, as our parts will often be above our unwrap planes. If when conflicts exist, we can provide reference geometry on the 'top' side of our unwrap plane. I suppose this applies to all unwrap     types, not just Parts. 
 * 
 * Variables to export
 *      edges_on_plane (the edges that wrap to the offset plane. Note that in the case of a thickened part, these are not the same edges we originally unwrap. Or they could be - if we measured/required thickness)
 *      
 * 
 */

/*
 * AS BUILT (2026-09-25). Explained end to end (theory, use, code map): driven_offset/docs/unwrap_explained.md.
 * Design records: research_unwrap_perf.md (chart accuracy, packed evaluator, fitting), research_unwrap_part.md
 * (Part mode, as built 8 / 8b), research_undrape_map.md (undrape, as built 12-15), research_undrape_ops.md;
 * research_unwrap.md is the 09-10 pre-build plan, partly superseded.
 *
 * The map (edge_offset_utils.fs: unwrapChart / unwrapFast). The reference is ONE planar tangent chain (joints
 * within 1 deg) whose X rises steadily; swept along its plane's normal it is a surface that flattens without
 * stretching, so every point has exact chart coordinates (arc along the reference, v across the plane, height
 * off the surface). Unwrapping writes them out flat in the origin's frame (a mate connector, or a plane / planar
 * face): x = length along the reference offset by the preserve-length offset (s - offset * theta), y = across,
 * z = height. The alignment point, which must lie within the reference's X span, lands on the origin. A point
 * past the reference's centre of curvature has no foot and cannot be unwrapped.
 *
 * Three modes, one output (set of bodies) per source body:
 *
 * EDGES / WIRES: every selected edge is sampled, unwrapped, and emitted as a line or a (sketch) arc where the
 * points are one within tolerance AND the exactly-unwrapped end tangents agree with it (continuity is kept), a
 * fitted spline otherwise. The outputs are wires, one per body owning selected edges.
 *
 * CONSTANT-THICKNESS PART (undrape a plate such as a topsheet; undrape_utils.fs): the two sides and t come
 * from evOffsetDetection + tangent growth (a raycast seed as fallback), no reference needed for that. The
 * part's MID-SURFACE is mapped onto the TARGET = the wire's extrusion along its plane normal, offset by the
 * target offset; the wire is picked, or is the section of the mid-surface by a picked face. Where the part is
 * draped (curved across the wire's plane) this is a deformation: each station's section is unrolled flat
 * (width = arc length across the section), optionally measured, and reported; stations the undrape could not
 * solve are reported as a warning. The flat outline is rebuilt as a plate t/2 each side: a plane split by the
 * outline, material by loop parity -- exact planes, arcs stay arcs, holes and slots cut.
 *
 * PART (SOLID) (unwrap_part.fs): any solid along the reference, e.g. a core. Split where the reference
 * changes between straight and curved; straight pieces move rigidly, curved pieces are rebuilt through the
 * map; optionally with square walls.
 *
 * Every mode ends with a length check (the preserved curve's wrapped length over the source's extent vs the
 * flat result's X extent; flat / source volume for solids), optionally kept as two wires.
 *
 * Names & properties. The Outputs table is filled by the editing logic (getProperty cannot run in the body,
 * correction 36), ONLY when "Read names and properties" is pressed (the user's rule: nothing is copied behind
 * their back). A row whose source name matches an existing row keeps its edited output name and its
 * "Copy material and appearance" choice. The body pairs row i with source body i (it cannot read names), so a
 * source set that changes after the last press (a selection or upstream edit) can leave the rows stale: when the row
 * count differs from the body count the table is not applied and a warning says so; a same-count reorder
 * cannot be detected.
 *
 * Not built: surfaces, composite parts, mate connectors as inputs, neutral axis, projection of a wire onto a
 * plane normal to the unwrap plane.
 */

/** What is being unwrapped. */
export enum UnwrapType
{
    annotation { "Name" : "Edges / wires" }
    EDGES,
    annotation { "Name" : "Constant-thickness part" }
    THICKENED,
    annotation { "Name" : "Part (solid)" }
    PART
}

/** Which curve's length an edge unwrap preserves. */
export enum UnwrapPreserveLength
{
    annotation { "Name" : "Along the reference" }
    REFERENCE,
    annotation { "Name" : "Along an offset of the reference" }
    OFFSET
}

/** How Part mode builds the faces it has to rebuild (over curved reference spans). */
export enum UnwrapPartFaces
{
    annotation { "Name" : "Keep source faces (arcs preserved)" }
    KEEP,
    annotation { "Name" : "Simplify (merge tangent faces)" }
    MERGE
}

/**
 * How an undrape crosses the stations it cannot section across (the pressed step turning across the part at the tip
 * and tail U-turns: the station plane cuts the step wall lengthwise). research_undrape_map.md open decision 9.1.
 */
export enum UndrapeUTurn
{
    annotation { "Name" : "Section literally" }
    LITERAL,
    annotation { "Name" : "Blend across" }
    BLEND
}

/** Where a plate's undrape target (the wire whose extrusion the mid-surface maps onto) comes from. */
export enum UndrapeTargetSource
{
    annotation { "Name" : "Wire" }
    WIRE,
    annotation { "Name" : "Section by a face" }
    FACE
}

// ============================================================================
// Constants
// ============================================================================

/**
 * Edges mode samples adaptively (the user's rule: density from each edge's own structure, not a fixed spacing).
 * Seed: a line 3 samples, an arc one per UNWRAP_SEED_ARC_STEP of sweep, a B-spline UNWRAP_SEED_PER_CP per control
 * point, anything else UNWRAP_SEED_OTHER; and never a gap wider than UNWRAP_MAX_GAP. Then up to
 * UNWRAP_REFINE_PASSES passes map every unsettled span's midpoint (one batched kernel call per pass) and split the
 * spans whose midpoint misses the cubic predicted from its neighbours by more than a quarter of the fit tolerance.
 */
const UNWRAP_SEED_ARC_STEP = 15 * degree;
const UNWRAP_SEED_PER_CP = 3;
const UNWRAP_SEED_OTHER = 9;
const UNWRAP_MAX_GAP = 50 * millimeter;
const UNWRAP_REFINE_PASSES = 6;
const UNWRAP_MAX_SAMPLES = 401;

/** Unwrap's own fit defaults: 0.005 mm leaves half the 0.01 mm budget to everything else. */
const UNWRAP_FIT_TOLERANCE_BOUNDS = { (millimeter) : [1e-5, 0.005, 1] } as LengthBoundSpec;
const UNWRAP_MAX_CP_BOUNDS = { (unitless) : [4, 60, MAX_CONTROL_POINTS] } as IntegerBoundSpec;

const UNWRAP_LENGTH_BOUNDS = { (millimeter) : [-1e4, 0, 1e4] } as LengthBoundSpec;
const UNWRAP_SHAPE_TOLERANCE_BOUNDS = { (millimeter) : [0.0001, 0.005, 1] } as LengthBoundSpec;
const UNWRAP_SPACING_BOUNDS = { (millimeter) : [0.5, 50, 1000] } as LengthBoundSpec;

/** How far a supplied end tangent may disagree with the edge's own three end points. */
const UNWRAP_TANGENT_AGREE = 2 * degree;

/** Samples per reference edge for the "X rises along the reference" check (the chart re-checks densely). */
const UNWRAP_REFERENCE_X_SAMPLES = 9;

/** 5-point Gauss-Legendre nodes and weights on [-1, 1], for the length check's span integrals. */
const UNWRAP_GAUSS_X = [-0.9061798459386640, -0.5384693101056831, 0, 0.5384693101056831, 0.9061798459386640];
const UNWRAP_GAUSS_W = [0.2369268850561891, 0.4786286704993665, 0.5688888888888889, 0.4786286704993665, 0.2369268850561891];

/** Closest two points on a kept wrapped length curve may be. */
const UNWRAP_CHECK_MIN_STEP = 1e-5 * meter;

/** Points per table span on a kept wrapped length curve. */
const UNWRAP_CHECK_PER_SPAN = 6;

/** Vertices read for a body's extent in the length check, and samples on the wrapped length curve. */
const UNWRAP_CHECK_VERTICES = 400;

/** Edges touching a vertex within this arc (metres) of either end of the extent are sampled along their length. */
const UNWRAP_CHECK_END_ZONE = 0.03;
const UNWRAP_CHECK_EDGE_SAMPLES = 40;

/** The two sides and their plate area must cover this fraction of the part's area. */
const UNWRAP_SIDE_AREA_FRACTION = 0.6;

/** A number as stored in the Outputs table's data strings: optionally signed, decimal, with an exponent. */
const UNWRAP_NUMBER_PATTERN = "^[-+]?[0-9]*\\.?[0-9]+([eE][-+]?[0-9]+)?$";

annotation { "Feature Type Name" : "Unwrap",
        "Feature Type Description" : "Unwrap edges or a constant-thickness part from along a planar reference chain onto a plane, preserving length.",
        "Editing Logic Function" : "unwrapEditLogic" }
export const unwrap = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Unwrap", "Default" : UnwrapType.EDGES, "UIHint" : UIHint.HORIZONTAL_ENUM }
        definition.unwrapType is UnwrapType;

        if (definition.unwrapType == UnwrapType.EDGES)
        {
            annotation { "Name" : "Edges to unwrap", "Filter" : (EntityType.EDGE || BodyType.WIRE) && ConstructionObject.NO }
            definition.edges is Query;
        }
        else
        {
            annotation { "Name" : "Parts to unwrap", "Filter" : EntityType.BODY && BodyType.SOLID,
                        "Description" : "Constant-thickness: plates, draped or not, e.g. a topsheet. Part: any solid along the reference, e.g. a core." }
            definition.parts is Query;
        }

        if (definition.unwrapType == UnwrapType.THICKENED)
        {
            annotation { "Name" : "Undrape target from", "Default" : UndrapeTargetSource.WIRE, "UIHint" : UIHint.HORIZONTAL_ENUM,
                        "Description" : "The part's mid-surface is mapped onto the extrusion of a wire along its plane normal: a wire you pick, or the section of the mid-surface by a face you pick." }
            definition.targetFrom is UndrapeTargetSource;
        }

        if (definition.unwrapType != UnwrapType.THICKENED || definition.targetFrom == UndrapeTargetSource.WIRE)
        {
            annotation { "Name" : "Wrapped reference", "Filter" : (EntityType.EDGE || BodyType.WIRE) && ConstructionObject.NO,
                        "Description" : "A planar tangent chain the geometry is wrapped along, e.g. the ski's top-surface profile." }
            definition.reference is Query;
        }
        else
        {
            annotation { "Name" : "Section face", "Filter" : EntityType.FACE && GeometryType.PLANE, "MaxNumberOfPicks" : 1,
                        "Description" : "A planar face (e.g. the Front plane) that cuts the part's mid-surface along the profile to unwrap along." }
            definition.profileFace is Query;
        }

        if (definition.unwrapType != UnwrapType.THICKENED)
        {
            annotation { "Name" : "Preserve length", "Default" : UnwrapPreserveLength.REFERENCE, "UIHint" : UIHint.SHOW_LABEL,
                        "Description" : "Which curve keeps its length when unwrapped." }
            definition.preserveLength is UnwrapPreserveLength;

            if (definition.preserveLength == UnwrapPreserveLength.OFFSET)
            {
                annotation { "Name" : "Offset", "Description" : "Distance off the reference, along its surface normal." }
                isLength(definition.lengthOffset, UNWRAP_LENGTH_BOUNDS);

                annotation { "Name" : "Flip offset", "Default" : false, "UIHint" : UIHint.OPPOSITE_DIRECTION }
                definition.flipLengthOffset is boolean;
            }
        }
        else
        {
            annotation { "Name" : "Target offset", "Description" : "The mid-surface maps onto the extruded wire offset by this along its surface normal; that offset's length is the one preserved. E.g. minus half the thickness for a topsheet whose top face lies on the wire's extrusion." }
            isLength(definition.targetOffset, UNWRAP_LENGTH_BOUNDS);

            annotation { "Name" : "Flip target offset", "Default" : false, "UIHint" : UIHint.OPPOSITE_DIRECTION }
            definition.flipTargetOffset is boolean;
        }

        annotation { "Name" : "Wrapped alignment point", "Filter" : BodyType.MATE_CONNECTOR || EntityType.VERTEX, "MaxNumberOfPicks" : 1,
                    "Description" : "The wrapped point that lands on the unwrapped origin. Must lie within the reference's X span." }
        definition.alignPoint is Query;

        annotation { "Name" : "Unwrapped origin", "Filter" : BodyType.MATE_CONNECTOR || (EntityType.FACE && GeometryType.PLANE), "MaxNumberOfPicks" : 1,
                    "Description" : "Frame of the flat result: X along the reference, Z along its surface normal. A mate connector (implicit ones included) or a plane / planar face (its own axes)." }
        definition.origin is Query;

        if (definition.unwrapType != UnwrapType.EDGES)
        {
            annotation { "Name" : "Lay the part on the origin plane", "Default" : true,
                        "Description" : "The flat part's lowest face on the origin's XY plane. Off, it keeps its height relative to the alignment point." }
            definition.layOnPlane is boolean;
        }

        if (definition.unwrapType == UnwrapType.PART)
        {
            annotation { "Name" : "Rebuilt faces", "Default" : UnwrapPartFaces.KEEP, "UIHint" : UIHint.SHOW_LABEL,
                        "Description" : "Over curved stretches of the reference the part is rebuilt. Keep: one face per source face, true arcs and planes wherever the flattened geometry is one. Simplify: tangent-connected faces merged into fitted surfaces -- fewer faces, faster, splines only." }
            definition.partFaces is UnwrapPartFaces;

            annotation { "Name" : "Shape tolerance", "Description" : "How far a face may depart from a pure side-view shape (constant across the width) or plan-view wall and still be built as one. Beyond it the face is rebuilt exactly (slower). Every result is checked against the source afterwards and the worst deviation is reported; exceeding this tolerance (or 0.01 mm, whichever is larger) is an error." }
            isLength(definition.shapeTolerance, UNWRAP_SHAPE_TOLERANCE_BOUNDS);

            annotation { "Name" : "Square walls", "Default" : false,
                        "Description" : "Make walls normal to the flat plane (a machined blank). Off, walls keep the exact mapped lean (e.g. 1.2 deg at a core extension's nose)." }
            definition.squareWalls is boolean;
        }

        if (definition.unwrapType == UnwrapType.THICKENED)
        {
            annotation { "Name" : "Maximum sample gap", "Description" : "Outline samples are placed adaptively (per edge from its structure, refined until the outline is within a quarter of the fit tolerance); this only caps the largest gap." }
            isLength(definition.sampleSpacing, UNWRAP_SPACING_BOUNDS);

            annotation { "Name" : "U-turn zones", "Default" : UndrapeUTurn.LITERAL, "UIHint" : UIHint.SHOW_LABEL,
                        "Description" : "Where a station plane cuts the part lengthwise (a pressed step turning across the part at the tip or tail), unrolling section by section is singular. Section literally: those stations are sectioned by the kernel like any other (slower; the outline keeps a sharp bump where the plane turns tangent to the step). Blend across: they are not measured and the outline crosses the zone as a smooth cubic between the measured stations either side (faster; the flat outline there is a blend, not a measurement)." }
            definition.uTurnRule is UndrapeUTurn;
        }

        annotation { "Group Name" : "Lines, arcs & fitting", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Recognise lines and arcs", "Default" : true,
                        "Description" : "Emit an unwrapped edge as a line or an arc when its points are one within tolerance and its end tangents agree." }
            definition.recogniseShapes is boolean;

            annotation { "Name" : "Target degree", "Description" : "Degree the fit aims for on unwrapped curves that are not lines or arcs" }
            isInteger(definition.approximationDegree, DEGREE_BOUND);

            annotation { "Name" : "Tolerance", "Description" : "How far a fitted curve, a line or an arc may sit from the exactly unwrapped points" }
            isLength(definition.approximationTolerance, UNWRAP_FIT_TOLERANCE_BOUNDS);

            annotation { "Name" : "Maximum control points" }
            isInteger(definition.approximationMaxCPs, UNWRAP_MAX_CP_BOUNDS);
        }

        annotation { "Group Name" : "Names & properties", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Name suffix", "Default" : "_UNWRAPPED", "MaxLength" : 64 }
            definition.nameSuffix is string;

            annotation { "Name" : "Read names and properties",
                        "Description" : "Read the source bodies' names, materials and appearances into the table. Only this button updates it; output names you edited are kept for sources whose name still matches." }
            isButton(definition.readProperties);

            annotation { "Name" : "Outputs", "Item name" : "Output", "Item label template" : "#outputName",
                        "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS }
            definition.outputs is array;
            for (var output in definition.outputs)
            {
                annotation { "Name" : "Source", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                output.sourceName is string;

                annotation { "Name" : "Output name", "Default" : "" }
                output.outputName is string;

                annotation { "Name" : "Material", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                output.materialLabel is string;

                annotation { "Name" : "Copy material and appearance", "Default" : true }
                output.copyProperties is boolean;

                annotation { "Name" : "Material data", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
                output.materialData is string;

                annotation { "Name" : "Appearance data", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
                output.appearanceData is string;
            }

            annotation { "Name" : "Copy attributes", "Default" : true, "Description" : "Copy the source bodies' attributes (kept current on every regeneration)." }
            definition.copyAttributes is boolean;
        }

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print edge table", "Default" : false }
            definition.debugPrintEdges is boolean;

            annotation { "Name" : "Keep length curves", "Default" : false,
                        "Description" : "Keep the two curves the unwrap preserves length along: the wrapped one (the reference offset by the preserve-length offset, over the part's extent) and its flat image. A good unwrap leaves their lengths equal." }
            definition.debugKeepLengthCurves is boolean;

            if (definition.unwrapType == UnwrapType.THICKENED)
            {
                annotation { "Name" : "Measure deformation", "Default" : true,
                            "Description" : "Measure the undrape's lengthwise stretch and shear for the report. Off is faster." }
                definition.measureDeformation is boolean;
            }
        }
    }
    {
        const sources = sourceBodies(context, definition);
        if (size(sources) == 0)
        {
            throw regenError(definition.unwrapType == UnwrapType.EDGES ? "Select the edges to unwrap." : "Select the parts to unwrap.",
                [definition.unwrapType == UnwrapType.EDGES ? "edges" : "parts"]);
        }
        if ((definition.unwrapType != UnwrapType.THICKENED || definition.targetFrom == UndrapeTargetSource.WIRE)
            && isQueryEmpty(context, definition.reference))
        {
            throw regenError("Select the wrapped reference.", ["reference"]);
        }
        if (definition.unwrapType == UnwrapType.THICKENED && definition.targetFrom == UndrapeTargetSource.FACE
            && isQueryEmpty(context, definition.profileFace))
        {
            throw regenError("Select the section face.", ["profileFace"]);
        }
        if (isQueryEmpty(context, definition.alignPoint) || isQueryEmpty(context, definition.origin))
        {
            throw regenError("Select the wrapped alignment point and the unwrapped origin.", ["alignPoint", "origin"]);
        }

        const alignPoint = pointOf(context, definition.alignPoint);
        const cs = frameOf(context, definition.origin);
        const settings = {
                "recognise" : definition.recogniseShapes,
                "approximation" : {
                    "approximationDegree" : definition.approximationDegree,
                    "approximationTolerance" : definition.approximationTolerance,
                    "approximationMaxCPs" : definition.approximationMaxCPs
                },
                "print" : definition.debugPrintEdges
            };

        var outputs = [];
        var outputBodies = [];
        var tally = { "line" : 0, "arc" : 0, "freeform" : 0 };
        var records = [];
        var edgesOnPlane = [];

        const edgeChart = (definition.unwrapType != UnwrapType.THICKENED)
            ? checkedChart(context, definition.reference, "reference", alignPoint, lengthOffset(definition))
            : undefined;

        for (var i = 0; i < size(sources); i += 1)
        {
            const bodyId = id + ("body" ~ i);
            var result;
            var extent; // the edges whose extent along the reference the length check measures
            if (definition.unwrapType == UnwrapType.EDGES)
            {
                const edges = evaluateQuery(context, qIntersection([qOwnedByBody(sources[i], EntityType.EDGE), expandEdgeQuery(definition.edges)]));
                result = unwrapEdgesToWires(context, bodyId, edgeChart, cs, edges, settings);
                extent = qUnion(edges);
            }
            else if (definition.unwrapType == UnwrapType.PART)
            {
                result = unwrapPart(context, bodyId, definition, edgeChart, cs, sources[i], settings);
                extent = qOwnedByBody(sources[i], EntityType.EDGE);
            }
            else
            {
                result = unwrapPlate(context, bodyId, definition, sources[i], alignPoint, cs, settings);
                edgesOnPlane = append(edgesOnPlane, result.edgesOnPlane);
                extent = qOwnedByBody(sources[i], EntityType.EDGE);
            }

            const check = lengthAndVolume(context, bodyId + "lengthCurves", result.chart, cs, sources[i], extent, result.bodies,
                result.lengthZ, definition.debugKeepLengthCurves, result.arcRange);

            tally = addTally(tally, result.tally);
            records = append(records, mergeMaps(result.record, { "check" : check }));
            outputs = append(outputs, { "source" : sources[i], "bodies" : result.bodies });
            outputBodies = append(outputBodies, result.bodies);
        }

        // Rows are paired with bodies by index (the body cannot read names, correction 36); the editing logic
        // keeps them in step while the dialog is open. A count mismatch means the sources changed without it:
        // naming by index would then name the wrong bodies, so the table is skipped.
        var warnings = [];
        const tableUsable = size(definition.outputs) == size(sources);
        if (size(definition.outputs) > 0 && !tableUsable)
        {
            warnings = append(warnings, "The Outputs table has " ~ size(definition.outputs) ~ " row(s) for " ~ size(sources)
                ~ " source bod" ~ (size(sources) == 1 ? "y" : "ies") ~ ", so names and properties were not applied: edit the feature to refresh it.");
        }
        applyNamesAndProperties(context, definition, outputs, tableUsable);
        reportSummary(context, id, definition, tally, records, warnings);

        embedStandardOutputs(context, id, {
                    "output" : qUnion(outputBodies),
                    "outputDescription" : "The unwrapped bodies",
                    "inputs" : qUnion(sources),
                    "queries" : {
                        "edgesOnPlane" : extractableQuery(qUnion(edgesOnPlane), "Edges of the part sides laid on the unwrap plane.", DebugColor.MAGENTA)
                    }
                });
    }, {
            // Parameters added after the first release: a saved feature that lacks one regenerates with the
            // old behaviour instead of failing its precondition (corrections 16.3, 25). No buttons (correction 27).
            "squareWalls" : false,
            "partFaces" : UnwrapPartFaces.KEEP,
            "shapeTolerance" : 0.005 * millimeter,
            "debugKeepLengthCurves" : false,
            "targetFrom" : UndrapeTargetSource.WIRE,
            "sampleSpacing" : 50 * millimeter,
            "uTurnRule" : UndrapeUTurn.LITERAL,
            "recogniseShapes" : true,
            "approximationDegree" : 3,
            "approximationTolerance" : 0.005 * millimeter,
            "approximationMaxCPs" : 60,
            "layOnPlane" : true,
            "nameSuffix" : "_UNWRAPPED",
            "copyAttributes" : true,
            "debugPrintEdges" : false,
            "targetOffset" : 0 * millimeter,
            "flipTargetOffset" : false,
            "preserveLength" : UnwrapPreserveLength.REFERENCE,
            "lengthOffset" : 0 * millimeter,
            "flipLengthOffset" : false,
            "measureDeformation" : true
        });

// ============================================================================
// Inputs
// ============================================================================

/**
 * The bodies being unwrapped, one output each: the owners of the selected edges, or the parts.
 * Shared by the body and the editing logic, so table row i always belongs to body i.
 */
function sourceBodies(context is Context, definition is map) returns array
{
    if (definition.unwrapType == UnwrapType.EDGES)
    {
        if (definition.edges == undefined)
        {
            return [];
        }
        return evaluateQuery(context, qOwnerBody(expandEdgeQuery(definition.edges)));
    }
    if (definition.parts == undefined)
    {
        return [];
    }
    return evaluateQuery(context, qBodyType(definition.parts, BodyType.SOLID));
}

/**
 * The unwrapped frame: a mate connector's, or a plane's (origin, normal as Z, its X).
 */
function frameOf(context is Context, selection is Query) returns CoordSystem
{
    const connector = mateConnectorOf(context, selection);
    if (connector != undefined)
    {
        return evMateConnector(context, { "mateConnector" : connector });
    }
    return planeToCSys(evPlane(context, { "face" : selection }));
}

function pointOf(context is Context, selection is Query) returns Vector
{
    const connector = mateConnectorOf(context, selection);
    if (connector != undefined)
    {
        return evMateConnector(context, { "mateConnector" : connector }).origin;
    }
    return evVertexPoint(context, { "vertex" : selection });
}

/**
 * The mate connector body a selection belongs to, or undefined. The dialog can hand over the connector's
 * VERTEX rather than its body (correction 44), so resolve through the owner body.
 */
function mateConnectorOf(context is Context, selection is Query)
{
    const connectors = evaluateQuery(context, qBodyType(qOwnerBody(selection), BodyType.MATE_CONNECTOR));
    return (size(connectors) > 0) ? connectors[0] : undefined;
}

/**
 * unwrapChart behind the checks it cannot make with a clear message: the reference's X must rise (or fall)
 * steadily along it, and the alignment point's X must lie within the reference's X span (it seeds the chart's
 * zero). unwrapChart itself refuses a reference that is not one tangent chain.
 *
 * @param referenceKey {string} : the parameter to highlight for a bad reference ("reference", "profileFace").
 */
/** How far two reference edges may overlap in X before the reference counts as doubling back. */
const UNWRAP_REFERENCE_X_OVERLAP = 1e-5 * meter;

function checkedChart(context is Context, reference is Query, referenceKey is string, alignPoint is Vector,
    offset is ValueWithUnits) returns map
{
    const edges = evaluateQuery(context, expandEdgeQuery(reference));
    if (size(edges) == 0)
    {
        throw regenError("The wrapped reference has no edges.", [referenceKey]);
    }

    // Each edge monotonic in X, and the edges' X intervals end to end: then the chain is. The overlap tolerance is
    // UNWRAP_REFERENCE_X_OVERLAP, not kernel zero: a section of a real part carries sub-micron overlaps at its
    // edge joins (the topsheet's Front-plane section: 0.5 um at X 1624.5), while a reference that doubles back
    // overlaps by millimetres.
    const tol = UNWRAP_REFERENCE_X_OVERLAP / meter;
    var spans = [];
    for (var edge in edges)
    {
        const tangentLines = evEdgeTangentLines(context, { "edge" : edge, "parameters" : range(0, 1, UNWRAP_REFERENCE_X_SAMPLES) });
        const x0 = tangentLines[0].origin[0].value;
        const x1 = tangentLines[UNWRAP_REFERENCE_X_SAMPLES - 1].origin[0].value;
        const sense = (x1 >= x0) ? 1 : -1;
        for (var tl in tangentLines)
        {
            if (sense * tl.direction[0] < 1e-6)
            {
                throw regenError("The wrapped reference must run along world X: X must rise steadily along it, but it turns back (or runs across X) near X = "
                        ~ roundToPrecision(tl.origin[0] / millimeter, 1) ~ " mm.", [referenceKey], edge);
            }
        }
        spans = append(spans, [min(x0, x1), max(x0, x1)]);
    }
    spans = sort(spans, function(a, b)
        {
            return a[0] - b[0];
        });
    for (var k = 1; k < size(spans); k += 1)
    {
        if (spans[k][0] < spans[k - 1][1] - tol)
        {
            throw regenError("The wrapped reference must run along world X: it covers X = "
                    ~ roundToPrecision(spans[k][0] * 1000, 1) ~ " mm more than once.", [referenceKey]);
        }
    }

    const xLow = spans[0][0];
    var xHigh = spans[0][1];
    for (var s in spans)
    {
        xHigh = max(xHigh, s[1]);
    }
    const ax = alignPoint[0].value;
    if (ax < xLow - tol || ax > xHigh + tol)
    {
        throw regenError("The wrapped alignment point (X = " ~ roundToPrecision(ax * 1000, 1) ~ " mm) must lie within the reference's X span ("
                ~ roundToPrecision(xLow * 1000, 1) ~ " .. " ~ roundToPrecision(xHigh * 1000, 1) ~ " mm).", ["alignPoint"]);
    }

    return unwrapChart(context, reference, alignPoint, offset);
}

/**
 * The offset of the reference whose length an edge unwrap preserves.
 */
function lengthOffset(definition is map) returns ValueWithUnits
{
    if (definition.preserveLength == UnwrapPreserveLength.OFFSET)
    {
        return definition.flipLengthOffset ? -definition.lengthOffset : definition.lengthOffset;
    }
    return 0 * meter;
}

// ============================================================================
// Edges
// ============================================================================

/**
 * Unwrap edges and extract them as wires.
 * @returns {map} : { "bodies" (Query), "tally", "record", "chart", "lengthZ" }
 */
function unwrapEdgesToWires(context is Context, id is Id, chart is map, cs is CoordSystem, edges is array, settings is map) returns map
{
    const emitted = unwrapEdges(context, id, chart, cs, edges, settings);
    if (size(emitted.curves) == 0)
    {
        return { "bodies" : qNothing(), "tally" : emitted.tally, "record" : emitted.record,
                "chart" : chart, "lengthZ" : -chart.alignHeight * meter };
    }

    const curveBodies = qUnion(emitted.curves);
    opExtractWires(context, id + "wires", { "edges" : qOwnedByBody(curveBodies, EntityType.EDGE) });
    opDeleteBodies(context, id + "deleteCurves", { "entities" : curveBodies });

    return { "bodies" : qCreatedBy(id + "wires", EntityType.BODY), "tally" : emitted.tally, "record" : emitted.record,
            "chart" : chart, "lengthZ" : -chart.alignHeight * meter };
}

/**
 * @returns {map} : { "curves" : array of body Queries, "tally", "record" }
 */
function unwrapEdges(context is Context, id is Id, chart is map, cs is CoordSystem, edges is array, settings is map) returns map
{
    var curves = [];
    var tally = { "line" : 0, "arc" : 0, "freeform" : 0 };
    var lines = [];

    // The frame as plain numbers: each point is built unitless and given metres once.
    const o = cs.origin / meter;
    const xa = cs.xAxis;
    const ya = cross(cs.zAxis, cs.xAxis);
    const za = cs.zAxis;

    var ids = [];
    var entries = [];
    var facts = [];
    for (var e = 0; e < size(edges); e += 1)
    {
        const edgeId = id + ("edge" ~ e);
        const length = evLength(context, { "entities" : edges[e] });
        const sampled = adaptiveEdgeSamples(context, chart, edges[e], length, settings.approximation.approximationTolerance / 4);
        const count = size(sampled.params);
        const feet = sampled.feet;
        const tangentLines = [sampled.first, sampled.last];
        var points = makeArray(count);
        for (var j = 0; j < count; j += 1)
        {
            const u = feet[j];
            points[j] = vector(o[0] + u[0] * xa[0] + u[1] * ya[0] + u[2] * za[0],
                        o[1] + u[0] * xa[1] + u[1] * ya[1] + u[2] * za[1],
                        o[2] + u[0] * xa[2] + u[1] * ya[2] + u[2] * za[2]) * meter;
        }
        const startTangent = unwrapDirection(chart, cs, feet[0], tangentLines[0].direction);
        const endTangent = unwrapDirection(chart, cs, feet[count - 1], tangentLines[1].direction);

        ids = append(ids, edgeId);
        entries = append(entries, flatCurveItem(points, startTangent, endTangent, settings));
        facts = append(facts, { "length" : length, "chord" : norm(points[count - 1] - points[0]), "count" : count, "seed" : sampled.seed });
    }

    const emittedAll = emitFlatCurves(context, ids, entries, settings);
    for (var e = 0; e < size(ids); e += 1)
    {
        const shape = emittedAll[e].shape;
        const gate = emittedAll[e].gate;

        tally[shape.kind] += 1;
        curves = append(curves, qCreatedBy(ids[e], EntityType.BODY));
        lines = append(lines, "    edge " ~ e ~ ": " ~ shape.kind
            ~ (shape.kind == "arc" ? " R " ~ fmtMM(shape.radius, 4, 0) : "")
            ~ ", length " ~ fmtMM(facts[e].length, 3, 0) ~ " -> chord " ~ fmtMM(facts[e].chord, 3, 0)
            ~ ", " ~ facts[e].count ~ " samples (seed " ~ facts[e].seed ~ ")" ~ gate);
    }

    if (settings.print)
    {
        for (var text in lines)
        {
            println(text);
        }
    }

    return {
        "curves" : curves,
        "tally" : tally,
        "record" : { "edges" : size(edges) }
    };
}

/**
 * One edge's samples, mapped through the chart, as dense as the flat curve needs (see UNWRAP_REFINE_PASSES).
 * @returns {map} : { "params" (edge parameters, ascending), "feet" (unwrapFast results), "first", "last" (the end
 *      tangent lines), "seed" (seed sample count) }
 */
function adaptiveEdgeSamples(context is Context, chart is map, edge is Query, length is ValueWithUnits, tolerance is ValueWithUnits) returns map
{
    // Seed from the edge's own structure.
    const def = evCurveDefinition(context, { "edge" : edge });
    var n = UNWRAP_SEED_OTHER;
    if (def is Line)
    {
        n = 3;
    }
    else if (def is Circle)
    {
        n = max(3, ceil((length / def.radius) * radian / UNWRAP_SEED_ARC_STEP) + 1);
    }
    else if (def is BSplineCurve)
    {
        n = max(5, UNWRAP_SEED_PER_CP * (size(def.controlPoints) - 1) + 1);
    }
    n = min(UNWRAP_MAX_SAMPLES, max(n, ceil(length / UNWRAP_MAX_GAP) + 1));
    const seedCount = n;

    var params = range(0, 1, n);
    const lines0 = evEdgeTangentLines(context, { "edge" : edge, "parameters" : params });
    var feet = makeArray(n);
    var previous = undefined;
    for (var j = 0; j < n; j += 1)
    {
        feet[j] = checkedFoot(chart, lines0[j].origin, previous, edge);
        previous = feet[j];
    }
    const first = lines0[0];
    const last = lines0[n - 1];

    // Refine: a span is settled once its midpoint agrees with the cubic through its neighbours.
    const tol = tolerance / meter;
    var open = makeArray(n - 1, true);
    for (var pass = 0; pass < UNWRAP_REFINE_PASSES; pass += 1)
    {
        var spans = [];
        for (var j = 0; j + 1 < size(params); j += 1)
        {
            if (open[j])
            {
                spans = append(spans, j);
            }
        }
        if (size(spans) == 0 || size(params) + size(spans) > UNWRAP_MAX_SAMPLES)
        {
            break;
        }
        var mids = makeArray(size(spans));
        for (var k = 0; k < size(spans); k += 1)
        {
            mids[k] = 0.5 * (params[spans[k]] + params[spans[k] + 1]);
        }
        const midLines = evEdgeTangentLines(context, { "edge" : edge, "parameters" : mids });

        var newParams = [];
        var newFeet = [];
        var newOpen = [];
        var k = 0;
        for (var j = 0; j < size(params); j += 1)
        {
            newParams = append(newParams, params[j]);
            newFeet = append(newFeet, feet[j]);
            if (j + 1 == size(params))
            {
                break;
            }
            if (k < size(spans) && spans[k] == j)
            {
                const mid = checkedFoot(chart, midLines[k].origin, feet[j], edge);
                const miss = spanMidMiss(params, feet, j, mid);
                k += 1;
                if (miss > tol)
                {
                    newOpen = append(newOpen, true);
                    newParams = append(newParams, mids[k - 1]);
                    newFeet = append(newFeet, mid);
                    newOpen = append(newOpen, true);
                    continue;
                }
            }
            newOpen = append(newOpen, false);
        }
        params = newParams;
        feet = newFeet;
        open = newOpen;
    }

    return { "params" : params, "feet" : feet, "first" : first, "last" : last, "seed" : seedCount };
}

/**
 * The chart foot of one edge sample, warm-started, retried cold, and refused with the edge highlighted.
 */
function checkedFoot(chart is map, point is Vector, previous, edge is Query) returns array
{
    var u = unwrapFast(chart, point, previous);
    if (!chartFootConverged(u) && previous != undefined)
    {
        // A warm start can overshoot where the reference turns sharply: retry from X.
        u = unwrapFast(chart, point, undefined);
    }
    if (!chartFootConverged(u))
    {
        throw regenError("An edge cannot be unwrapped (highlighted): a point on it lies past the reference's centre of curvature, so it has no foot on the reference.",
            ["edges"], edge);
    }
    return u;
}

/**
 * How far the mapped midpoint of span j misses the cubic Hermite through the span's ends, with slopes estimated from
 * the neighbouring samples (one-sided at the edge ends). Plain metres, flat chart coordinates u[0..2].
 */
function spanMidMiss(params is array, feet is array, j is number, mid is array) returns number
{
    const n = size(params);
    const h = params[j + 1] - params[j];
    var miss = 0;
    for (var c = 0; c < 3; c += 1)
    {
        const p0 = feet[j][c];
        const p1 = feet[j + 1][c];
        const m0 = (j > 0)
            ? (feet[j + 1][c] - feet[j - 1][c]) / (params[j + 1] - params[j - 1])
            : (p1 - p0) / h;
        const m1 = (j + 2 < n)
            ? (feet[j + 2][c] - feet[j][c]) / (params[j + 2] - params[j])
            : (p1 - p0) / h;
        const predicted = 0.5 * (p0 + p1) + h / 8 * (m0 - m1);
        miss = max(miss, abs(mid[c] - predicted));
    }
    return miss;
}

/**
 * Unwrap a solid (unwrap_part.fs, research_unwrap_part.md, research_unwrap_curved_ref.md): split where the reference
 * changes between straight and curved, straight pieces moved rigidly, curved pieces rebuilt (band rebuild, exact cell
 * fallback, every rebuilt piece reverse-checked). Laid on the origin plane by a translation.
 */
function unwrapPart(context is Context, id is Id, definition is map, chart is map, cs is CoordSystem, part is Query,
    settings is map) returns map
{
    const result = unwrapSolid(context, id, chart, cs, part, {
                "squareWalls" : definition.squareWalls,
                "faceMode" : (definition.partFaces == UnwrapPartFaces.MERGE) ? "merge" : "keep",
                "shapeTolerance" : definition.shapeTolerance,
                "keepFailed" : definition.nameSuffix == "__keepfail"
            });
    if (settings.print)
    {
        for (var text in result.lines)
        {
            println(text);
        }
    }

    var dz = 0 * meter;
    if (definition.layOnPlane)
    {
        const bb = evBox3d(context, { "topology" : result.bodies, "tight" : true, "cSys" : cs });
        dz = -bb.minCorner[2];
        opTransform(context, id + "layOnPlane", {
                    "bodies" : result.bodies,
                    "transform" : transform(dz * cs.zAxis)
                });
    }

    return {
        "bodies" : result.bodies,
        "tally" : { "line" : 0, "arc" : 0, "freeform" : 0 },
        "record" : { "part" : result.report },
        "chart" : chart,
        "lengthZ" : -chart.alignHeight * meter + dz
    };
}

/**
 * Length and volume check for one unwrapped body.
 *
 * Wrapped: the preserved curve -- the reference offset by the preserve-length offset -- measured in 3D (a dense
 * polyline on the chart's packed position tables) between the stations of the source's extreme points. Flat: the
 * flat result's extent along the unwrapped X. The unwrap maps length along exactly that curve to X, so the two agree
 * for a good unwrap; a difference means the ends moved (a leaning wall, undrape stretch at the ends, a rebuild error).
 * Volume: flat / source, solids only.
 *
 * @param extent {Query} : the edges whose extent is measured -- the selected edges in Edges mode, all of the
 *      part's edges otherwise. Points with no foot on the reference are skipped.
 *
 * With `keep`, both curves are left in the model as wires: the wrapped one in the reference plane, the flat one at
 * the height the preserved curve lands at. They are not part of the feature's "output".
 */
function lengthAndVolume(context is Context, id is Id, chart is map, cs is CoordSystem, source is Query, extent is Query,
    bodies is Query, lengthZ is ValueWithUnits, keep is boolean, arcRange) returns map
{
    if (isQueryEmpty(context, bodies))
    {
        return { "wrapped" : 0 * meter, "flat" : 0 * meter, "volumeRatio" : 0 };
    }

    // A plate's undrape already knows the station of every outline sample: its extent is exact, no vertex search.
    // (The vertex search came out 1.12 mm short on the topsheet: its tip and tail U-turns reach furthest part way
    // along edges whose feet do not converge from a cold seed.)
    if (arcRange != undefined)
    {
        return lengthFromArcs(context, id, chart, cs, source, bodies, lengthZ, keep, arcRange[0], arcRange[1]);
    }

    // Extent in the chart: the extreme stations over the extent's vertices (at most UNWRAP_CHECK_VERTICES of them),
    // each mapped once from a cold seed (vertices are unrelated, so no warm start).
    const vertices = evaluateQuery(context, qAdjacent(extent, AdjacencyType.VERTEX, EntityType.VERTEX));
    const stride = max(1, ceil(size(vertices) / UNWRAP_CHECK_VERTICES));
    var arcLo = undefined;
    var arcHi = undefined;
    var sampled = [];
    for (var k = 0; k < size(vertices); k += stride)
    {
        const u = unwrapFast(chart, evVertexPoint(context, { "vertex" : vertices[k] }), undefined);
        if (!chartFootConverged(u))
        {
            continue;
        }
        sampled = append(sampled, { "vertex" : vertices[k], "arc" : u[3] });
        arcLo = (arcLo == undefined) ? u[3] : min(arcLo, u[3]);
        arcHi = (arcHi == undefined) ? u[3] : max(arcHi, u[3]);
    }

    // A rounded end or a U-turn reaches furthest part way along an edge, not at a vertex: sample the extent's edges
    // that touch a vertex near either end (all of them when no vertex could be placed).
    var endEdges = extent;
    if (arcLo != undefined)
    {
        var nearEnds = [];
        for (var s in sampled)
        {
            if (s.arc - arcLo < UNWRAP_CHECK_END_ZONE || arcHi - s.arc < UNWRAP_CHECK_END_ZONE)
            {
                nearEnds = append(nearEnds, s.vertex);
            }
        }
        endEdges = qIntersection([qAdjacent(qUnion(nearEnds), AdjacencyType.VERTEX, EntityType.EDGE), extent]);
    }
    const params = range(0, 1, UNWRAP_CHECK_EDGE_SAMPLES + 1);
    for (var edge in evaluateQuery(context, endEdges))
    {
        var previous = undefined;
        for (var tl in evEdgeTangentLines(context, { "edge" : edge, "parameters" : params, "arcLengthParameterization" : false }))
        {
            var u = unwrapFast(chart, tl.origin, previous);
            if (!chartFootConverged(u) && previous != undefined)
            {
                u = unwrapFast(chart, tl.origin, undefined);
            }
            if (!chartFootConverged(u))
            {
                previous = undefined;
                continue;
            }
            previous = u;
            arcLo = (arcLo == undefined) ? u[3] : min(arcLo, u[3]);
            arcHi = (arcHi == undefined) ? u[3] : max(arcHi, u[3]);
        }
    }
    if (arcLo == undefined)
    {
        return { "wrapped" : 0 * meter, "flat" : 0 * meter, "volumeRatio" : 0 };
    }

    return lengthFromArcs(context, id, chart, cs, source, bodies, lengthZ, keep, arcLo, arcHi);
}

/**
 * The chart arcs (plain metres) the undraped outline spans: the stations of its first and last samples.
 */
function outlineArcRange(edges is array)
{
    var lo = undefined;
    var hi = undefined;
    for (var edge in edges)
    {
        if (edge.arcs == undefined)
        {
            return undefined;
        }
        for (var a in edge.arcs)
        {
            lo = (lo == undefined) ? a : min(lo, a);
            hi = (hi == undefined) ? a : max(hi, a);
        }
    }
    return (lo == undefined) ? undefined : [lo, hi];
}

/**
 * The length / volume check between two chart arcs (plain metres): see lengthAndVolume.
 */
function lengthFromArcs(context is Context, id is Id, chart is map, cs is CoordSystem, source is Query, bodies is Query,
    lengthZ is ValueWithUnits, keep is boolean, arcLo is number, arcHi is number) returns map
{
    // The preserved curve's length on the packed tables: integrated span by span (breakpoints at every table sample,
    // 5-point Gauss on the curve's speed |dA/da|, chartEval's e[8]). Not a uniform polyline: 400 even chords (4.6 mm)
    // cut across the topsheet's two ~2.4 mm-radius S-bends and lost 1.119 mm.
    const c = chart.packed;
    var wrappedM = 0;
    var points = [];
    var span = chartSpanOf(c, arcLo);
    var a0 = arcLo;
    while (a0 < arcHi - 1e-12)
    {
        // Step past zero-length spans (the repeated sample at an edge join) to the next real breakpoint.
        while (span + 2 < c.count && c.arcs[span + 1] <= a0 + 1e-12)
        {
            span += 1;
        }
        var a1 = arcHi;
        if (span + 1 < c.count && c.arcs[span + 1] > a0 + 1e-12 && c.arcs[span + 1] < arcHi)
        {
            a1 = c.arcs[span + 1];
        }
        const half = 0.5 * (a1 - a0);
        const middle = 0.5 * (a1 + a0);
        for (var g = 0; g < 5; g += 1)
        {
            const a = middle + half * UNWRAP_GAUSS_X[g];
            wrappedM += UNWRAP_GAUSS_W[g] * half * chartEval(c, a, chartSpan(c, a, span))[8];
        }
        if (keep)
        {
            for (var k = 0; k < UNWRAP_CHECK_PER_SPAN; k += 1)
            {
                const a = a0 + (a1 - a0) * k / UNWRAP_CHECK_PER_SPAN;
                const e = chartEval(c, a, chartSpan(c, a, span));
                points = append(points, vector(e[0], e[1], e[2]) * meter);
            }
        }
        a0 = a1;
        span += 1;
        if (span > c.count - 2)
        {
            span = c.count - 2;
        }
    }
    if (keep)
    {
        const e = chartEval(c, arcHi, chartSpan(c, arcHi, span));
        points = append(points, vector(e[0], e[1], e[2]) * meter);
    }
    const wrapped = wrappedM * meter;

    const bb = evBox3d(context, { "topology" : bodies, "tight" : true, "cSys" : cs });
    const flat = bb.maxCorner[0] - bb.minCorner[0];

    var volumeRatio = 0;
    if (!isQueryEmpty(context, qBodyType(bodies, BodyType.SOLID)))
    {
        volumeRatio = evVolume(context, { "entities" : bodies }) / evVolume(context, { "entities" : source });
    }

    if (keep)
    {
        // Per-span points crowd together on the tiny edges of a section (0.12 mm on the topsheet); the fitter
        // refuses points closer than 1e-6 of the chord, so drop those within UNWRAP_CHECK_MIN_STEP.
        emitSplineCurve(context, id + "wrapped", withoutRepeats(points, UNWRAP_CHECK_MIN_STEP), undefined, undefined, {
                    "approximationDegree" : 3,
                    "approximationTolerance" : 1e-7 * meter,
                    "approximationMaxCPs" : MAX_CONTROL_POINTS
                });
        const yAxis = cross(cs.zAxis, cs.xAxis);
        const y = chart.alignV * meter;
        emitLineCurve(context, id + "flat",
            cs.origin + bb.minCorner[0] * cs.xAxis + y * yAxis + lengthZ * cs.zAxis,
            cs.origin + bb.maxCorner[0] * cs.xAxis + y * yAxis + lengthZ * cs.zAxis);
        setProperty(context, { "entities" : qCreatedBy(id + "wrapped", EntityType.BODY), "propertyType" : PropertyType.NAME,
                    "value" : "Length curve (wrapped)" });
        setProperty(context, { "entities" : qCreatedBy(id + "flat", EntityType.BODY), "propertyType" : PropertyType.NAME,
                    "value" : "Length curve (flat)" });
    }

    return { "wrapped" : wrapped, "flat" : flat, "volumeRatio" : volumeRatio };
}

/**
 * Unit tangent at p0 of the parabola through p0, p1, p2 (chord spacing), pointing from p0 towards p1.
 */
function threePointTangent(p0 is Vector, p1 is Vector, p2 is Vector) returns Vector
{
    const d1 = norm(p1 - p0) / meter;
    const d2 = norm(p2 - p1) / meter;
    if (d1 < 1e-12 || d2 < 1e-12)
    {
        return normalize(p2 - p0);
    }
    const derivative = -(2 * d1 + d2) / (d1 * (d1 + d2)) * p0 + (d1 + d2) / (d1 * d2) * p1 - d1 / (d2 * (d1 + d2)) * p2;
    return normalize(derivative);
}

/**
 * How far, in radians, a line's or arc's own end direction may be from an edge's unwrapped end tangent
 * and still be emitted as a line or arc (about 0.057 degrees). The unwrapped tangents come through the
 * chart, so they are estimates; inside this slack the exact piece's own tangent is what its neighbours
 * are pinned to (shapeRuns), so the joint carries no kink either way. Until 2026-09-25 the limit was
 * G1_JUNCTION_ANGLE (0.57 degrees) and an accepted arc was emitted with its ends where they fell
 * (reviews/2026-09-25_arc_line_fitting).
 */
const UNWRAP_ARC_SLACK = 1e-3;

/**
 * One unwrapped edge as a shapeRuns item: its points and end tangents, the supplied tangents checked
 * against the edge's own ends first.
 * @returns {map} : { "item", "gate" (a note on a replaced tangent, or "") }
 */
function flatCurveItem(points is array, startTangentIn is Vector, endTangentIn is Vector, settings is map) returns map
{
    return flatCurveItem(points, startTangentIn, endTangentIn, settings, true, true);
}

/**
 * flatCurveItem with the end check per end: an end where an edge was cut into pieces (kernelZonePieces) keeps the
 * slope both pieces share.
 */
function flatCurveItem(points is array, startTangentIn is Vector, endTangentIn is Vector, settings is map, checkStart is boolean,
    checkEnd is boolean) returns map
{
    var startTangent = startTangentIn;
    var endTangent = endTangentIn;
    const count = size(points);
    var gate = "";

    // A supplied end tangent must agree with the edge's own end: checked against the tangent of the parabola
    // through the last three points (non-uniform spacing). More than UNWRAP_TANGENT_AGREE off and it is
    // replaced -- the undrape map's tangent at a pointed tip (section shrinking to a point) came out 58 deg
    // wrong on a base's tail and the fit, held to it, wobbled (curvature sign flipping seven times).
    if (count >= 3)
    {
        const startEstimate = threePointTangent(points[0], points[1], points[2]);
        const endEstimate = -threePointTangent(points[count - 1], points[count - 2], points[count - 3]);
        if (checkStart && angleBetween(startTangent, startEstimate) > UNWRAP_TANGENT_AGREE)
        {
            gate = gate ~ " (start tangent off by " ~ toString(roundToPrecision(angleBetween(startTangent, startEstimate) / degree, 2)) ~ " deg: replaced)";
            startTangent = startEstimate;
        }
        if (checkEnd && angleBetween(endTangent, endEstimate) > UNWRAP_TANGENT_AGREE)
        {
            gate = gate ~ " (end tangent off by " ~ toString(roundToPrecision(angleBetween(endTangent, endEstimate) / degree, 2)) ~ " deg: replaced)";
            endTangent = endEstimate;
        }
    }

    return {
            "item" : {
                "points" : points,
                "startTangent" : startTangent,
                "endTangent" : endTangent,
                "allowArc" : settings.recognise,
                "allowLine" : settings.recognise,
                "tangentSlack" : UNWRAP_ARC_SLACK
            },
            "gate" : gate
        };
}

/**
 * Emit every unwrapped edge of one call together: a line or an (exact, sketch) arc where the points are one
 * within tolerance AND its ends run along the unwrapped tangents (to UNWRAP_ARC_SLACK), a fit otherwise. The
 * edges are shaped as a set (shapeRuns, joints found by coinciding ends), so where an edge meets its neighbour
 * smoothly both are built to one tangent; a line keeps its own.
 * @returns {array} : per edge { "shape", "gate" (why a line / arc was refused, or a replaced tangent) }
 */
function emitFlatCurves(context is Context, ids is array, entries is array, settings is map) returns array
{
    var items = [];
    for (var entry in entries)
    {
        items = append(items, entry.item);
    }

    const shapes = shapeRuns(items, { "tolerance" : settings.approximation.approximationTolerance, "findJoints" : true });

    var out = [];
    for (var k = 0; k < size(items); k += 1)
    {
        // Unit tangents: approximateFamily scales them by the run's chord itself. Pre-scaling them made the end
        // speed a chord squared and no fit could reach tolerance.
        // A freeform run is fitted through its samples AND points of the cubic the sampling was refined against
        // (fitPoints): through the samples alone the fit came back interpolating and rang between them (325 um on the
        // topsheet's tip outline, 19 um at 4305's wing roots, against the 0.005 mm tolerance).
        const fitted = (shapes[k].kind == "freeform")
            ? fitPoints(items[k].points, shapes[k].startTangent, shapes[k].endTangent, items[k].monotone)
            : items[k].points;
        emitRunShape(context, ids[k], shapes[k], fitted, settings.approximation);
        const note = shapes[k].note;
        out = append(out, { "shape" : shapes[k], "gate" : entries[k].gate ~ ((note == undefined) ? "" : " (" ~ note ~ ")") });
    }
    return out;
}

/**
 * Interior points added per sample span to the points a freeform flat edge is fitted through (fitPoints), and the
 * spans too short for them: shorter than UNWRAP_FIT_MIN_SPAN metres or UNWRAP_FIT_MIN_FRACTION of the run's chord
 * (approximateSpline refuses parameters closer than 1e-6).
 */
const UNWRAP_FIT_DENSIFY = 3;
const UNWRAP_FIT_MIN_SPAN = 2e-5;
const UNWRAP_FIT_MIN_FRACTION = 8e-6;

/**
 * The points a freeform flat edge is fitted through: its samples, plus UNWRAP_FIT_DENSIFY points inside every span on
 * the cubic Hermite the sampling was refined against (chord length; at each sample the slope of the quadratic through
 * it and its neighbours, the unit end tangents at the ends -- undrapeMiss checks the outline samples against this
 * curve to a quarter of the fit tolerance, spanMidMiss the edge samples against its parameter-space twin). At samples
 * flagged in `monotone` the slope is shape-preserving (Fritsch-Carlson, per coordinate), so a sparse, spiky stretch
 * (a U-turn under the literal rule) gets no overshoot. Fitted through the samples alone, approximateSpline adds knots
 * until it passes every sample and then interpolates them: between samples it rang by up to 325 um (2026-09-29).
 */
function fitPoints(points is array, startTangent, endTangent, monotone) returns array
{
    const n = size(points);
    if (n < 2)
    {
        return points;
    }
    var P = makeArray(n);
    var ts = makeArray(n, 0);
    for (var i = 0; i < n; i += 1)
    {
        P[i] = points[i] / meter;
        if (i > 0)
        {
            ts[i] = ts[i - 1] + norm(P[i] - P[i - 1]);
        }
    }
    const total = ts[n - 1];
    if (total < 1e-12)
    {
        return points;
    }
    var S = makeArray(n);
    for (var i = 0; i < n; i += 1)
    {
        if (i == 0)
        {
            S[i] = (startTangent != undefined) ? startTangent : quadraticSlope(P, ts, i);
        }
        else if (i == n - 1)
        {
            S[i] = (endTangent != undefined) ? endTangent : quadraticSlope(P, ts, i);
        }
        else if (monotone != undefined && monotone[i] == true)
        {
            S[i] = monotoneSlope(P, ts, i);
        }
        else
        {
            S[i] = quadraticSlope(P, ts, i);
        }
    }
    const minSpan = max(UNWRAP_FIT_MIN_SPAN, UNWRAP_FIT_MIN_FRACTION * total);
    var out = [points[0]];
    for (var i = 0; i + 1 < n; i += 1)
    {
        const h = ts[i + 1] - ts[i];
        if (h > minSpan)
        {
            for (var k = 1; k <= UNWRAP_FIT_DENSIFY; k += 1)
            {
                const f = k / (UNWRAP_FIT_DENSIFY + 1);
                const f2 = f * f;
                const f3 = f2 * f;
                const q = (2 * f3 - 3 * f2 + 1) * P[i] + ((f3 - 2 * f2 + f) * h) * S[i] + (-2 * f3 + 3 * f2) * P[i + 1]
                    + ((f3 - f2) * h) * S[i + 1];
                out = append(out, q * meter);
            }
        }
        out = append(out, points[i + 1]);
    }
    return out;
}

/**
 * Slope d(point)/d(chord length) at sample i of plain points P (chord positions ts): the derivative of the quadratic
 * through it and its two neighbours; one-sided at the ends (the next two samples); the chord when only two samples exist
 * or samples coincide.
 */
function quadraticSlope(P is array, ts is array, i is number) returns Vector
{
    const n = size(P);
    var j = i - 1;
    var k = i + 1;
    if (i == 0)
    {
        j = 1;
        k = 2;
    }
    else if (i == n - 1)
    {
        j = n - 2;
        k = n - 3;
    }
    if (k < 0 || k >= n || j < 0 || j >= n || abs(ts[i] - ts[j]) < 1e-12 || abs(ts[i] - ts[k]) < 1e-12 || abs(ts[j] - ts[k]) < 1e-12)
    {
        const a = (i == n - 1) ? n - 2 : i;
        const b = a + 1;
        const dt = ts[b] - ts[a];
        return (dt < 1e-15) ? normalize(P[n - 1] - P[0]) : (P[b] - P[a]) / dt;
    }
    const t0 = ts[i];
    const t1 = ts[j];
    const t2 = ts[k];
    const w0 = (2 * t0 - t1 - t2) / ((t0 - t1) * (t0 - t2));
    const w1 = (t0 - t2) / ((t1 - t0) * (t1 - t2));
    const w2 = (t0 - t1) / ((t2 - t0) * (t2 - t1));
    return w0 * P[i] + w1 * P[j] + w2 * P[k];
}

/**
 * Shape-preserving slope at interior sample i (Fritsch-Carlson weighted harmonic mean of the two secants, per
 * coordinate, 0 where a coordinate turns): the cubic between samples then stays within their range.
 */
function monotoneSlope(P is array, ts is array, i is number) returns Vector
{
    const h0 = ts[i] - ts[i - 1];
    const h1 = ts[i + 1] - ts[i];
    if (h0 < 1e-12 || h1 < 1e-12)
    {
        return quadraticSlope(P, ts, i);
    }
    var m = [0, 0, 0];
    for (var c = 0; c < 3; c += 1)
    {
        const d0 = (P[i][c] - P[i - 1][c]) / h0;
        const d1 = (P[i + 1][c] - P[i][c]) / h1;
        if (d0 * d1 > 0)
        {
            m[c] = 3 * (h0 + h1) / ((2 * h1 + h0) / d0 + (h1 + 2 * h0) / d1);
        }
    }
    return vector(m[0], m[1], m[2]);
}

/**
 * The unit slope at sample i of an outline edge (quadraticSlope on chord length): the tangent two pieces of one edge
 * share where kernelZonePieces cuts it.
 */
function sampleSlope(points is array, i is number) returns Vector
{
    const n = size(points);
    var P = makeArray(n);
    var ts = makeArray(n, 0);
    for (var k = 0; k < n; k += 1)
    {
        P[k] = points[k] / meter;
        if (k > 0)
        {
            ts[k] = ts[k - 1] + norm(P[k] - P[k - 1]);
        }
    }
    return normalize(quadraticSlope(P, ts, i));
}

/**
 * An outline edge cut into pieces at the measured samples bounding each run of kernel-measured samples (undrape's
 * "kernel" flags), so the run is fitted on its own. Each piece { "from", "to" } (sample indices, inclusive) shares
 * its end sample with the next; "monotone" flags the kernel-measured samples (for the whole edge). One piece when the
 * edge has none.
 */
function kernelZonePieces(points is array, kernel) returns array
{
    const n = size(points);
    var monotone = makeArray(n, false);
    var cuts = [];
    if (kernel != undefined && size(kernel) == n)
    {
        var i = 0;
        while (i < n)
        {
            if (kernel[i] != true)
            {
                i += 1;
                continue;
            }
            var j = i;
            while (j + 1 < n && kernel[j + 1] == true)
            {
                j += 1;
            }
            for (var m = i; m <= j; m += 1)
            {
                monotone[m] = true;
            }
            for (var cut in [i - 1, j + 1])
            {
                if (cut > 0 && cut < n - 1 && (size(cuts) == 0 || cuts[size(cuts) - 1] != cut))
                {
                    cuts = append(cuts, cut);
                }
            }
            i = j + 1;
        }
    }
    var pieces = [];
    var from = 0;
    for (var cut in concatenateArrays([cuts, [n - 1]]))
    {
        if (cut > from)
        {
            pieces = append(pieces, { "from" : from, "to" : cut, "monotone" : monotone });
            from = cut;
        }
    }
    return pieces;
}

function addTally(a is map, b is map) returns map
{
    return { "line" : a.line + b.line, "arc" : a.arc + b.arc, "freeform" : a.freeform + b.freeform };
}

// ============================================================================
// Constant-thickness parts
// ============================================================================

/**
 * The two sides, walls and thickness of a constant-thickness solid, found without a reference
 * (research_undrape_ops.md 1): evOffsetDetection gives exactly-offset seed pairs and t; each side is
 * grown from its seed by tangency (45 deg: sides are G1 or gently creased, walls meet them at
 * 72-90 deg). ~0.1 s on a 252-face topsheet.
 */
function plateSidesGeneral(context is Context, part is Query) returns map
{
    const allFaces = qOwnedByBody(part, EntityType.FACE);

    // Every offset pair evOffsetDetection finds, grown by tangency; keep the one covering the most faces (areas
    // are measured once, for the winner only: evArea on a draped plate's ~250 spline faces costs ~1.5 s a call,
    // and this used to run it once per group plus once for the whole part). The first pair is not always the
    // plate's sides: on a narrow strip it pairs the two edge walls (145 mm apart) and misses the procedural
    // top / bottom faces altogether.
    var best = undefined;
    for (var g in evOffsetDetection(context, { "bodies" : part }))
    {
        // Grow from EVERY pair of the group: a side whose faces are not all tangent-connected is only
        // reached in full from several seeds.
        const candidate = grownSides(context, qUnion(g.side0), qUnion(g.side1), 0.5 * (g.offsetLow + g.offsetHigh),
            g.offsetHigh - g.offsetLow);
        if (candidate != undefined && (best == undefined || candidate.faceCount > best.faceCount))
        {
            best = candidate;
        }
    }
    if (best != undefined)
    {
        best = withSideAreas(context, part, allFaces, best);
    }

    // No pair covering most of the part: seed from an inward raycast on the largest faces instead.
    if (best == undefined || best.coverage < UNWRAP_SIDE_AREA_FRACTION)
    {
        var raycast = raycastSides(context, part);
        if (raycast != undefined)
        {
            raycast = withSideAreas(context, part, allFaces, raycast);
            if (best == undefined || raycast.coverage > best.coverage)
            {
                best = raycast;
            }
        }
    }
    if (best == undefined || best.coverage < UNWRAP_SIDE_AREA_FRACTION)
    {
        throw regenError("Could not find the two sides of a constant-thickness plate on this part: it looks like a solid block. Use Unwrap = Part (solid) for it.", ["parts"]);
    }

    return best;
}

/**
 * A side candidate with its areas: each side's, and the fraction of the part's area the two cover (the part's area
 * as sides + walls, the walls being the few faces left over -- cheaper than the whole part again).
 *
 * evArea on a draped side costs ~1.9 s (the topsheet's 124 spline faces), so the side areas come from the volume
 * where the topology shows both sides complete: two sides offset t apart bound area0 + area1 = 2 V / t (to t^2 times
 * the total Gaussian curvature, ~1 mm^2 in 282000 on the topsheet), and when every leftover (wall) face touches BOTH
 * sides neither side can be missing a face (a missing side face would be a leftover touching one side only). Then
 * both areas are V / t: equal, so undrapeOutline picks the side with fewer edges, as it did when the two measured
 * areas agreed. Otherwise one side (the one with fewer edges) is measured and the other is 2 V / t minus it: when the
 * measured side misses faces the estimate is larger by what is missing and the larger-side rule picks the other.
 */
function withSideAreas(context is Context, part is Query, allFaces is Query, candidate is map) returns map
{
    const walls = qSubtraction(allFaces, qUnion([candidate.side0, candidate.side1]));
    const wallFaces = evaluateQuery(context, walls);
    var complete = true;
    for (var w in wallFaces)
    {
        const around = qAdjacent(w, AdjacencyType.EDGE, EntityType.FACE);
        if (isQueryEmpty(context, qIntersection([around, candidate.side0])) || isQueryEmpty(context, qIntersection([around, candidate.side1])))
        {
            complete = false;
            break;
        }
    }
    const volume = evVolume(context, { "entities" : part });
    var area0;
    var area1;
    if (complete && size(wallFaces) > 0)
    {
        area0 = volume / candidate.thickness;
        area1 = area0;
    }
    else
    {
        const count0 = size(evaluateQuery(context, qAdjacent(candidate.side0, AdjacencyType.EDGE, EntityType.EDGE)));
        const count1 = size(evaluateQuery(context, qAdjacent(candidate.side1, AdjacencyType.EDGE, EntityType.EDGE)));
        const measureFirst = count0 <= count1;
        const measured = evArea(context, { "entities" : measureFirst ? candidate.side0 : candidate.side1 });
        const other = max(0 * meter * meter, 2 * volume / candidate.thickness - measured);
        area0 = measureFirst ? measured : other;
        area1 = measureFirst ? other : measured;
    }
    const wallArea = (size(wallFaces) == 0) ? 0 * meter ^ 2 : evArea(context, { "entities" : walls });
    return mergeMaps(candidate, {
                "walls" : walls,
                "area0" : area0,
                "area1" : area1,
                "area" : area0 + area1,
                "coverage" : (area0 + area1) / (area0 + area1 + wallArea)
            });
}

/**
 * Two seed faces grown by tangency into the plate's sides (45 deg: sides are G1 or gently creased,
 * walls meet them at 72-90 deg). undefined when the two grow into each other.
 * @param spread : how far the seed pairs' offsets disagree (offsetHigh - offsetLow; 0 for a raycast seed).
 */
function grownSides(context is Context, seed0 is Query, seed1 is Query, thickness is ValueWithUnits, spread is ValueWithUnits)
{
    const faces0 = evaluateQuery(context, qTangentConnectedFaces(seed0, 45 * degree));
    const faces1 = evaluateQuery(context, qTangentConnectedFaces(seed1, 45 * degree));
    const side0 = qUnion(faces0);
    const side1 = qUnion(faces1);
    if (!isQueryEmpty(context, qIntersection([side0, side1])))
    {
        return undefined;
    }
    return {
        "side0" : side0,
        "side1" : side1,
        "thickness" : thickness,
        "spread" : spread,
        "faceCount" : size(faces0) + size(faces1)
    };
}

/**
 * Fallback seed (research_undrape_ops.md 1): from an interior point of each of the largest faces, cast
 * a ray inward through the BODY (a face query is ~50 ms per ray); a hit on an antiparallel face is the
 * opposite side, the hit distance the thickness. Stops at the first face that yields a plate.
 */
function raycastSides(context is Context, part is Query)
{
    var grid = [];
    for (var a = 0; a < 3; a += 1)
    {
        for (var b = 0; b < 3; b += 1)
        {
            grid = append(grid, vector((a + 0.5) / 3, (b + 0.5) / 3));
        }
    }

    // Largest faces first: a plate's sides are its biggest faces.
    var faces = [];
    for (var f in evaluateQuery(context, qOwnedByBody(part, EntityType.FACE)))
    {
        faces = append(faces, { "face" : f, "area" : evArea(context, { "entities" : f }) });
    }
    faces = sort(faces, function(x, y)
        {
            return (y.area - x.area) / (meter * meter);
        });

    for (var k = 0; k < min(size(faces), 6); k += 1)
    {
        const f = faces[k].face;
        var tp = undefined;
        for (var q in evFaceTangentPlanes(context, { "face" : f, "parameters" : grid, "returnUndefinedOutsideFace" : true }))
        {
            if (q != undefined)
            {
                tp = q;
                break;
            }
        }
        if (tp == undefined)
        {
            continue;
        }
        const hits = evRaycast(context, { "entities" : part, "ray" : line(tp.origin - 1e-6 * meter * tp.normal, -tp.normal) });
        if (size(hits) == 0 || hits[0].entityType != EntityType.FACE)
        {
            continue;
        }
        const hn = evFaceTangentPlane(context, { "face" : hits[0].entity, "parameter" : hits[0].parameter }).normal;
        if (dot(hn, tp.normal) > -0.999)
        {
            continue;
        }
        const candidate = grownSides(context, f, hits[0].entity, hits[0].distance + 1e-6 * meter, 0 * meter);
        if (candidate != undefined)
        {
            return candidate;
        }
    }
    return undefined;
}

/**
 * W = the section of the mid-surface by a face's plane, as ONE wire body (research_undrape_ops.md 3a).
 * A big plane splits the mid-surface; the mid-surface edges lying within 1 um of the plane are the
 * section (qCoincidesWithPlane is too strict: pre-existing edges the split merged into sit ~0.5 um off).
 */
function profileFromFace(context is Context, id is Id, mid is Query, userFace is Query) returns Query
{
    const pl = evPlane(context, { "face" : userFace });
    if (!isQueryEmpty(context, qCoincidesWithPlane(qOwnedByBody(mid, EntityType.FACE), pl)))
    {
        throw regenError("The section face lies in the part's mid-surface: the section is not a curve.", ["profileFace"]);
    }
    opPlane(context, id + "sectionPlane", { "plane" : pl, "width" : 10 * meter, "height" : 10 * meter });
    opSplitFace(context, id + "section", {
                "faceTargets" : qOwnedByBody(mid, EntityType.FACE),
                "faceTools" : qOwnedByBody(qCreatedBy(id + "sectionPlane", EntityType.BODY), EntityType.FACE)
            });
    opDeleteBodies(context, id + "deleteSectionPlane", { "entities" : qCreatedBy(id + "sectionPlane", EntityType.BODY) });

    var onPlane = [];
    for (var e in evaluateQuery(context, qIntersectsPlane(qOwnedByBody(mid, EntityType.EDGE), pl)))
    {
        var ok = true;
        for (var tl in evEdgeTangentLines(context, { "edge" : e, "parameters" : [0, 0.25, 0.5, 0.75, 1] }))
        {
            if (abs(dot(tl.origin - pl.origin, pl.normal)) > 1e-6 * meter)
            {
                ok = false;
                break;
            }
        }
        if (ok)
        {
            onPlane = append(onPlane, e);
        }
    }
    if (size(onPlane) == 0)
    {
        throw regenError("The section face does not cross the part's mid-surface.", ["profileFace"]);
    }
    opExtractWires(context, id + "profile", { "edges" : qUnion(onPlane) });
    const chains = qCreatedBy(id + "profile", EntityType.BODY);
    if (size(evaluateQuery(context, chains)) != 1)
    {
        throw regenError("The section crosses a hole, slot or notch and breaks into several pieces. Pick a face that misses them, or supply the wire.", ["profileFace"]);
    }
    return chains;
}

/**
 * Flat plate from closed planar outline wires (outer + holes, any nesting), thickness/2 either side of
 * flatPlane (research_undrape_ops.md 4, route e): a plane sheet split by the outline, material by
 * parity of loops crossed from the sheet's border, extruded both ways. Arcs stay arcs (walls are
 * cylinders), splines stay the same spline, caps are true planes.
 */
function plateFromOutline(context is Context, id is Id, wireEdges is Query, flatPlane is Plane, thickness is ValueWithUnits) returns Query
{
    const bb = evBox3d(context, { "topology" : wireEdges, "tight" : true, "cSys" : planeToCSys(flatPlane) });
    // The sheet is a line extruded across, NOT an opPlane: opPlane makes a construction body, and a
    // solid extruded from a construction body's faces is itself construction -- translucent, and not
    // a part.
    const margin = 10 * millimeter;
    const across = cross(flatPlane.normal, flatPlane.x);
    opCreateBSplineCurve(context, id + "sheetEdge", {
                "bSplineCurve" : bSplineCurve({
                            "degree" : 1,
                            "isPeriodic" : false,
                            "controlPoints" : [planeToWorld(flatPlane, vector(bb.minCorner[0] - margin, bb.minCorner[1] - margin)),
                                    planeToWorld(flatPlane, vector(bb.maxCorner[0] + margin, bb.minCorner[1] - margin))]
                        })
            });
    opExtrude(context, id + "sheet", {
                "entities" : qCreatedBy(id + "sheetEdge", EntityType.EDGE),
                "direction" : across,
                "endBound" : BoundingType.BLIND,
                "endDepth" : bb.maxCorner[1] - bb.minCorner[1] + 2 * margin
            });
    opDeleteBodies(context, id + "deleteSheetEdge", { "entities" : qCreatedBy(id + "sheetEdge", EntityType.BODY) });
    const sheet = qCreatedBy(id + "sheet", EntityType.BODY);
    opSplitFace(context, id + "cut", { "faceTargets" : qOwnedByBody(sheet, EntityType.FACE), "edgeTools" : wireEdges });

    // Parity: faces on the sheet's own (laminar) border are depth 0; every loop crossed adds one.
    // Each step's query is re-evaluated -- nesting queries across iterations blows up (13 s measured).
    const faces = qOwnedByBody(sheet, EntityType.FACE);
    var labelled = qUnion(evaluateQuery(context, qAdjacent(qEdgeTopologyFilter(qOwnedByBody(sheet, EntityType.EDGE), EdgeTopology.LAMINAR),
                    AdjacencyType.EDGE, EntityType.FACE)));
    var frontier = labelled;
    var depth = 0;
    var material = [];
    while (true)
    {
        const next = evaluateQuery(context, qSubtraction(qIntersection([qAdjacent(frontier, AdjacencyType.EDGE, EntityType.FACE), faces]), labelled));
        if (size(next) == 0)
        {
            break;
        }
        depth += 1;
        if (depth % 2 == 1)
        {
            material = concatenateArrays([material, next]);
        }
        frontier = qUnion(next);
        labelled = qUnion(evaluateQuery(context, qUnion([labelled, frontier])));
    }
    if (size(material) == 0)
    {
        throw regenError("The unwrapped outline does not close.", ["parts"]);
    }
    opExtrude(context, id + "plate", {
                "entities" : qUnion(material),
                "direction" : flatPlane.normal,
                "endBound" : BoundingType.BLIND,
                "endDepth" : 0.5 * thickness,
                "startBound" : BoundingType.BLIND,
                "startDepth" : 0.5 * thickness
            });
    opDeleteBodies(context, id + "deleteSheet", { "entities" : sheet });
    return qCreatedBy(id + "plate", EntityType.BODY);
}


/**
 * Undrape one constant-thickness part: its mid-surface onto the target (the wire's extrusion,
 * offset by the target offset), unwrapped flat and re-thickened.
 */
function unwrapPlate(context is Context, id is Id, definition is map, part is Query, alignPoint is Vector,
    cs is CoordSystem, settings is map) returns map
{
    const sides = plateSidesGeneral(context, part);
    const thickness = sides.thickness;

    var wire = definition.reference;
    var temporary = [];
    if (definition.targetFrom == UndrapeTargetSource.FACE)
    {
        // Only the part of the mid-surface the section plane crosses is needed: offset just those side faces and
        // their neighbours (~1 s per plate for all of them). The kernel refuses the trimmed offset of one side on
        // some parts (4305: side 0 fails with DIRECT_EDIT_OFFSET_FACE_FAILED, side 1 works); either side gives the
        // same mid-surface.
        try silent
        {
            opExtractSurface(context, id + "mid", {
                        "faces" : sides.side0,
                        "offset" : -0.5 * thickness,
                        "useFacesAroundToTrimOffset" : true
                    });
        }
        catch
        {
            // Its own id: a failed operation can still hold the first one ("Duplicate id").
            opExtractSurface(context, id + "midAlt", {
                        "faces" : sides.side1,
                        "offset" : -0.5 * thickness,
                        "useFacesAroundToTrimOffset" : true
                    });
        }
        const mid = qUnion([qCreatedBy(id + "mid", EntityType.BODY), qCreatedBy(id + "midAlt", EntityType.BODY)]);
        wire = profileFromFace(context, id + "profile", mid, definition.profileFace);
        temporary = [mid, wire];
    }

    const offset = definition.flipTargetOffset ? -definition.targetOffset : definition.targetOffset;
    const chart = checkedChart(context, wire, (definition.targetFrom == UndrapeTargetSource.FACE) ? "profileFace" : "reference",
        alignPoint, offset);
    const undraped = undrapeOutline(context, id + "undrape", chart, sides.side0, sides.side1, thickness, {
                "tolerance" : definition.approximationTolerance / 4,
                "spacing" : definition.sampleSpacing,
                "deformation" : definition.measureDeformation,
                "sideAreas" : [sides.area0, sides.area1],
                "uTurn" : (definition.uTurnRule == UndrapeUTurn.BLEND) ? "blend" : "literal"
            });

    // The mid-surface lands on the target, chart height 0 on it: cs z = -alignHeight. Laid on the plane,
    // the plate's lower face is on cs's XY plane instead.
    const zMid = definition.layOnPlane ? 0.5 * thickness : -chart.alignHeight * meter;
    const yAxis = cross(cs.zAxis, cs.xAxis);
    const flatPlane = plane(cs.origin + zMid * cs.zAxis, cs.zAxis, cs.xAxis);

    if (settings.print)
    {
        for (var text in undraped.lines)
        {
            println(text);
        }
    }

    var curves = [];
    var tally = { "line" : 0, "arc" : 0, "freeform" : 0 };
    var outlineIds = [];
    var outlineEntries = [];
    var outlineNames = [];
    for (var k = 0; k < size(undraped.edges); k += 1)
    {
        const edge = undraped.edges[k];
        var points = [];
        for (var xy in edge.points)
        {
            points = append(points, flatPlane.origin + (xy[0] * meter) * cs.xAxis + (xy[1] * meter) * yAxis);
        }
        const startTangent = normalize(edge.startTangent[0] * cs.xAxis + edge.startTangent[1] * yAxis);
        const endTangent = normalize(edge.endTangent[0] * cs.xAxis + edge.endTangent[1] * yAxis);
        const edgeId = id + ("outline" ~ k);
        if (settings.print)
        {
            // Printed BEFORE emitting, so an edge the kernel refuses names itself.
            println("    loop " ~ edge.loop ~ " edge " ~ edge.index ~ ": " ~ size(points) ~ " points,"
                ~ fmtVec(points[0] / millimeter, 3, 11) ~ " ->" ~ fmtVec(points[size(points) - 1] / millimeter, 3, 11) ~ " mm, chord "
                ~ fmtMM(norm(points[size(points) - 1] - points[0]), 4, 0) ~ " mm, end tangents"
                ~ fmtVec(startTangent, 4, 8) ~ " /" ~ fmtVec(endTangent, 4, 8));
            var pts = "";
            for (var i = 0; i < size(edge.points); i += 1)
            {
                pts = pts ~ " " ~ roundToPrecision(edge.points[i][0] * 1000, 4) ~ "," ~ roundToPrecision(edge.points[i][1] * 1000, 4)
                    ~ (edge.arcs != undefined ? "@" ~ roundToPrecision(edge.arcs[i] * 1000, 3) : "")
                    ~ ((edge.kernel != undefined && edge.kernel[i]) ? "k" : "");
            }
            println("        points:" ~ pts);
        }
        // Samples measured by kernel sections of refused stations (the U-turns under the literal rule) are sparse
        // and carry the rule's spike: that stretch is fitted as its own piece, with shape-preserving slopes, so it
        // cannot bend the measured outline either side of it.
        const pieces = kernelZonePieces(points, edge.kernel);
        for (var p = 0; p < size(pieces); p += 1)
        {
            const piece = pieces[p];
            const first = (piece.from == 0);
            const last = (piece.to == size(points) - 1);
            // a cut keeps the slope both pieces share (flatCurveItem's end check is for the edge's true ends)
            var entry = flatCurveItem(subArray(points, piece.from, piece.to + 1),
                first ? startTangent : sampleSlope(points, piece.from), last ? endTangent : sampleSlope(points, piece.to), settings,
                first, last);
            entry.item.monotone = subArray(piece.monotone, piece.from, piece.to + 1);
            outlineIds = append(outlineIds, (size(pieces) == 1) ? edgeId : edgeId + ("piece" ~ p));
            outlineEntries = append(outlineEntries, entry);
            outlineNames = append(outlineNames, (size(pieces) == 1) ? toString(k) : toString(k) ~ "." ~ toString(p));
        }
    }

    const outlineEmitted = emitFlatCurves(context, outlineIds, outlineEntries, settings);
    for (var k = 0; k < size(outlineIds); k += 1)
    {
        const emitted = outlineEmitted[k];
        tally[emitted.shape.kind] += 1;
        curves = append(curves, qCreatedBy(outlineIds[k], EntityType.BODY));
        if (settings.print)
        {
            println("    outline edge " ~ outlineNames[k] ~ " -> " ~ emitted.shape.kind
                ~ (emitted.shape.kind == "arc" ? " R " ~ fmtMM(emitted.shape.radius, 4, 0) : "") ~ emitted.gate);
        }
    }

    const curveBodies = qUnion(curves);
    opExtractWires(context, id + "outlineWires", { "edges" : qOwnedByBody(curveBodies, EntityType.EDGE) });
    const outlineWires = qCreatedBy(id + "outlineWires", EntityType.BODY);
    const plates = plateFromOutline(context, id + "plate", qOwnedByBody(outlineWires, EntityType.EDGE), flatPlane, thickness);
    opDeleteBodies(context, id + "deleteTemp", { "entities" : qUnion(concatenateArrays([[curveBodies, outlineWires], temporary])) });

    const lowerPlane = plane(flatPlane.origin - 0.5 * thickness * cs.zAxis, cs.zAxis, cs.xAxis);
    const lowerFace = qCoincidesWithPlane(qOwnedByBody(plates, EntityType.FACE), lowerPlane);

    return {
        "bodies" : plates,
        "edgesOnPlane" : qAdjacent(lowerFace, AdjacencyType.EDGE, EntityType.EDGE),
        "chart" : chart,
        "lengthZ" : zMid,
        "arcRange" : outlineArcRange(undraped.edges),
        "tally" : tally,
        "record" : {
            "edges" : size(undraped.edges),
            "thickness" : thickness,
            "spread" : sides.spread,
            "offset" : offset,
            "report" : undraped.report,
            "pieces" : size(evaluateQuery(context, plates))
        }
    };
}

// ============================================================================
// Names, properties, attributes
// ============================================================================

/**
 * Fill the Outputs table from the source bodies (getProperty works in editing logic, never in the feature
 * body; correction 36). The table changes ONLY when "Read names and properties" is pressed; any other edit
 * (a selection, the name suffix, an upstream change) leaves it as it is, and the feature body warns when
 * the row count no longer matches the bodies.
 *
 * On a press every row is rebuilt from its source body (name, material, appearance re-read). A rebuilt row
 * whose source name matches an existing row keeps that row's "Copy material and appearance" choice, and its
 * edited output name; an output name that was still automatic (empty, or source name + the old or new
 * suffix) becomes source name + the current suffix.
 */
export function unwrapEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map, hiddenBodies is Query, clickedButton is string) returns map
{
    // Only the button updates the table (the user's rule: metadata is copied on request, never behind their back).
    // A press keeps a row's edited output name and copy choice when its source name still matches.
    const pressed = clickedButton == "readProperties";
    if (!pressed)
    {
        return definition;
    }
    const oldRows = (definition.outputs == undefined) ? [] : definition.outputs;
    const oldSuffix = (oldDefinition.nameSuffix == undefined) ? definition.nameSuffix : oldDefinition.nameSuffix;

    const sources = sourceBodies(context, definition);
    var names = [];
    for (var body in sources)
    {
        names = append(names, propertyText(getProperty(context, { "entity" : body, "propertyType" : PropertyType.NAME })));
    }

    var used = makeArray(size(oldRows), false);
    var rows = [];
    for (var i = 0; i < size(names); i += 1)
    {
        var previous = undefined;
        for (var k = 0; k < size(oldRows); k += 1)
        {
            if (!used[k] && oldRows[k].sourceName == names[i])
            {
                used[k] = true;
                previous = oldRows[k];
                break;
            }
        }

        var row = readRow(context, sources[i], names[i], definition.nameSuffix);
        if (previous != undefined)
        {
            row.copyProperties = previous.copyProperties;
            const automatic = previous.outputName == "" || previous.outputName == previous.sourceName ~ oldSuffix
                || previous.outputName == previous.sourceName ~ definition.nameSuffix;
            row.outputName = automatic ? names[i] ~ definition.nameSuffix : previous.outputName;
        }
        rows = append(rows, row);
    }
    definition.outputs = rows;
    return definition;
}

/** One Outputs row read from a source body: material stored as "density|name" (a name may hold a bar). */
function readRow(context is Context, body is Query, name is string, suffix is string) returns map
{
    const mat = getProperty(context, { "entity" : body, "propertyType" : PropertyType.MATERIAL });
    const look = getProperty(context, { "entity" : body, "propertyType" : PropertyType.APPEARANCE });

    var materialData = "";
    var materialLabel = "(none)";
    if (mat != undefined && mat.density != undefined)
    {
        const density = mat.density / (kilogram / meter ^ 3);
        const matName = propertyText(mat.name);
        materialData = toString(density) ~ "|" ~ matName;
        materialLabel = matName ~ " (" ~ toString(roundToPrecision(density, 3)) ~ " kg/m^3)";
    }
    var appearanceData = "";
    if (look != undefined)
    {
        appearanceData = look.red ~ "|" ~ look.green ~ "|" ~ look.blue ~ "|" ~ look.alpha;
    }

    return {
        "sourceName" : name,
        "outputName" : name ~ suffix,
        "materialLabel" : materialLabel,
        "copyProperties" : true,
        "materialData" : materialData,
        "appearanceData" : appearanceData
    };
}

/** A property read back as text: "" when undefined (a sketch-owned wire can have no name). */
function propertyText(value) returns string
{
    if (value == undefined)
    {
        return "";
    }
    return (value is string) ? value : toString(value);
}

/**
 * A row's material data as { "name", "density" (kg/m^3 number) }, or undefined when it cannot be read.
 * Current format "density|name"; the first release wrote "name|density", still read.
 */
function parseMaterialData(data is string)
{
    const current = match(data, "^([^|]*)\\|(.*)$");
    if (current.hasMatch && match(current.captures[1], UNWRAP_NUMBER_PATTERN).hasMatch)
    {
        return { "density" : stringToNumber(current.captures[1]), "name" : current.captures[2] };
    }
    const legacy = match(data, "^(.*)\\|([^|]*)$");
    if (legacy.hasMatch && match(legacy.captures[2], UNWRAP_NUMBER_PATTERN).hasMatch)
    {
        return { "density" : stringToNumber(legacy.captures[2]), "name" : legacy.captures[1] };
    }
    return undefined;
}

/** A row's appearance data "r|g|b|a" as four numbers, or undefined when it cannot be read. */
function parseAppearanceData(data is string)
{
    const parts = splitByRegexp(data, "\\|");
    if (size(parts) != 4)
    {
        return undefined;
    }
    var values = [];
    for (var p in parts)
    {
        if (!match(p, UNWRAP_NUMBER_PATTERN).hasMatch)
        {
            return undefined;
        }
        values = append(values, stringToNumber(p));
    }
    return values;
}

/**
 * Name each output from its table row and copy what the row carries; attributes straight from the source.
 * @param useTable : false when the rows no longer pair with the sources (then only attributes are copied).
 */
function applyNamesAndProperties(context is Context, definition is map, outputs is array, useTable is boolean)
{
    for (var i = 0; i < size(outputs); i += 1)
    {
        const bodies = outputs[i].bodies;
        if (isQueryEmpty(context, bodies))
        {
            continue;
        }

        if (definition.copyAttributes)
        {
            for (var attribute in getAttributes(context, { "entities" : outputs[i].source }))
            {
                setAttribute(context, { "entities" : bodies, "attribute" : attribute });
            }
        }

        if (!useTable || i >= size(definition.outputs))
        {
            continue;
        }
        const row = definition.outputs[i];
        if (row.outputName != "")
        {
            setProperty(context, { "entities" : bodies, "propertyType" : PropertyType.NAME, "value" : row.outputName });
        }
        if (!row.copyProperties)
        {
            continue;
        }
        const mat = parseMaterialData(row.materialData);
        if (mat != undefined)
        {
            setProperty(context, { "entities" : bodies, "propertyType" : PropertyType.MATERIAL,
                        "value" : material(mat.name, mat.density * kilogram / meter ^ 3) });
        }
        const look = parseAppearanceData(row.appearanceData);
        if (look != undefined)
        {
            setProperty(context, { "entities" : bodies, "propertyType" : PropertyType.APPEARANCE,
                        "value" : color(look[0], look[1], look[2], look[3]) });
        }
    }
}

// ============================================================================
// Reporting
// ============================================================================

/**
 * One line for the feature's notice: info, or a warning when `warnings` (or an undrape's failed stations /
 * dropped points) are present -- the requested output then exists but is known to be incomplete or unnamed.
 */
function reportSummary(context is Context, id is Id, definition is map, tally is map, records is array, warningsIn is array)
{
    var warnings = warningsIn;
    var text = "Unwrapped " ~ size(records) ~ " bod" ~ (size(records) == 1 ? "y" : "ies") ~ ": "
        ~ tally.line ~ " line(s), " ~ tally.arc ~ " arc(s), " ~ tally.freeform ~ " spline(s).";
    for (var r in records)
    {
        if (r.thickness != undefined)
        {
            const u = r.report;
            text = text ~ " Thickness " ~ fmtMM(r.thickness, 4, 0) ~ " mm (offset pairs within " ~ fmtMM(r.spread, 5, 0)
                ~ " mm); mid-surface mapped to the target offset " ~ fmtMM(r.offset, 4, 0) ~ " mm; "
                ~ u.stations ~ " stations, " ~ u.fallbacks ~ " kernel sections.";
            if (definition.measureDeformation != false && u.stretchMin != undefined && u.stretchMax != undefined && u.shearMax != undefined)
            {
                text = text ~ " Deformation: lengthwise stretch "
                    ~ toString(roundToPrecision(u.stretchMin * 100, 3)) ~ "% .. " ~ toString(roundToPrecision(u.stretchMax * 100, 3))
                    ~ "%, shear up to " ~ toString(roundToPrecision(u.shearMax * 180 / PI, 2)) ~ " deg;";
            }
            text = text ~ " rim " ~ fmtMM(u.rim3d, 3, 0) ~ " mm draped -> " ~ fmtMM(u.rimFlat, 3, 0) ~ " mm flat"
                ~ (r.pieces > 1 ? "; " ~ r.pieces ~ " solids" : "") ~ ".";

            const failed = countOf(u.failed);
            const dropped = countOf(u.droppedPoints);
            if (failed > 0 || dropped > 0)
            {
                var where = "";
                if (u.failedArcs is array)
                {
                    for (var k = 0; k < size(u.failedArcs); k += 1)
                    {
                        where = where ~ (k > 0 ? ", " : "") ~ toString(roundToPrecision(u.failedArcs[k], 1));
                    }
                }
                warnings = append(warnings, "Undrape: " ~ failed ~ " station(s) could not be solved"
                    ~ (where != "" ? " (at arc " ~ where ~ " mm)" : "")
                    ~ (dropped > 0 ? ", " ~ dropped ~ " outline point(s) dropped" : "")
                    ~ "; the flat outline is fitted across the gaps.");
            }
        }
    }
    for (var r in records)
    {
        if (r.part != undefined)
        {
            text = text ~ " Part: " ~ r.part.pieces ~ " piece(s), " ~ r.part.rigidPieces ~ " moved rigidly, "
                ~ r.part.rebuiltPieces ~ " rebuilt"
                ~ (r.part.approximatedFaces != undefined && r.part.approximatedFaces > 0
                    ? "; " ~ r.part.approximatedFaces ~ " face(s) built as the nearest pure shape, max "
                        ~ fmtMM(r.part.approximationMax, 4, 0) ~ " mm" : "")
                ~ (r.part.reverseCheckMax != undefined ? "; checked against the source: max " ~ fmtMM(r.part.reverseCheckMax, 4, 0) ~ " mm" : "")
                ~ ".";
        }
    }
    for (var r in records)
    {
        if (r.check != undefined)
        {
            const k = r.check;
            text = text ~ " Length along the preserved curve: " ~ fmtMM(k.wrapped, 3, 0) ~ " mm wrapped -> " ~ fmtMM(k.flat, 3, 0)
                ~ " mm flat (" ~ fmtMM(k.flat - k.wrapped, 4, 0) ~ " mm)"
                ~ ((k.volumeRatio > 0) ? "; volume x" ~ toString(roundToPrecision(k.volumeRatio, 6)) : "") ~ ".";
        }
    }
    if (definition.debugPrintEdges)
    {
        println("[unwrap] " ~ text);
    }
    if (size(definition.outputs) == 0)
    {
        text = text ~ " Press 'Read names and properties' to name the outputs.";
    }

    if (size(warnings) > 0)
    {
        var head = "";
        for (var w in warnings)
        {
            head = head ~ w ~ " ";
        }
        reportFeatureWarning(context, id, head ~ text);
    }
    else
    {
        reportFeatureInfo(context, id, text);
    }
}

/** A count that may arrive as a number, an array or undefined. */
function countOf(value) returns number
{
    if (value == undefined)
    {
        return 0;
    }
    return (value is array) ? size(value) : value;
}
