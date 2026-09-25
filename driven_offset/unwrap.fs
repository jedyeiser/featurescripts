FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

// IMPORT: edge_offset_utils.fs (same document; export-imports curve_core: chains, classifyPoints, emitters)
export import(path : "a2665e22c07b7a6929ce4e80", version : "941e620c8511448a358a762b");
// IMPORT: undrape_utils.fs (same document; the undrape map)
import(path : "283b8f7562a16e9c9ccc01b7", version : "");
// IMPORT: Variable_tools V2 extract_outputs.fs (embedStandardOutputs, extractable wrappers)
import(path : "a47f90bfa6b17a59e20cebd0/f4f872fe20d1498201fed64d/3cac74f0bc2b98272db13cd3", version : "b8c80ac05dcfd9f3cc172ffc");

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
 * v1 AS BUILT (2026-09-24). Design record: driven_offset/research_unwrap.md, section 0.
 *
 * The map. The reference is a planar tangent chain; swept along its plane's normal it is a surface that
 * flattens without stretching, so every point has exact chart coordinates (arc along the reference, v
 * across the plane, height off the surface) -- referenceSurfaceCoords. Unwrapping writes them out flat
 * in the origin mate connector's frame: x = length along the reference offset by the preserve-length
 * offset (s - offset * theta), y = across, z = height. The alignment point lands on the origin.
 *
 * EDGES: every selected edge is sampled, unwrapped, and emitted as a line or a (sketch) arc where the
 * points are one within tolerance AND the exactly-unwrapped end tangents agree with it (continuity is
 * kept), a fitted spline otherwise. One output per source body, named by the Names & properties table.
 *
 * CONSTANT-THICKNESS PART (undrape a plate such as a topsheet; research_undrape_*.md): the two sides and
 * t come from evOffsetDetection + tangent growth, no reference needed. The part's MID-SURFACE is mapped
 * onto the TARGET = the wire's extrusion along its plane normal, offset by the target offset; the wire is
 * picked, or is the section of the mid-surface by a picked face. Where the part is draped (curved across
 * the wire's plane) this is a deformation: each station's section is unrolled flat (width = arc length
 * across the section), measured and reported (undrape_utils.fs). The flat mid-surface outline is rebuilt
 * as a plate t/2 each side: a plane split by the outline, material by loop parity -- exact planes, arcs
 * stay arcs, holes and slots cut.
 *
 * Not in v1: surfaces, composite parts, mate connectors, general part deform, neutral axis, planes /
 * planar faces as the unwrap target, projection of a wire onto a plane normal to the unwrap plane.
 */

/** What is being unwrapped. */
export enum UnwrapType
{
    annotation { "Name" : "Edges / wires" }
    EDGES,
    annotation { "Name" : "Constant-thickness part" }
    THICKENED
}

/** Which curve's length an edge unwrap preserves. */
export enum UnwrapPreserveLength
{
    annotation { "Name" : "Along the reference" }
    REFERENCE,
    annotation { "Name" : "Along an offset of the reference" }
    OFFSET
}

/** Where a plate's undrape target (the wire whose extrusion the mid-surface maps onto) comes from. */
export enum UndrapeTargetSource
{
    annotation { "Name" : "Wire" }
    WIRE,
    annotation { "Name" : "Section by a face" }
    FACE
}

/** Samples per unwrapped edge: at least this many, and one per UNWRAP_SAMPLE_SPACING of length. */
const UNWRAP_MIN_SAMPLES = 17;
const UNWRAP_MAX_SAMPLES = 201;
const UNWRAP_SAMPLE_SPACING = 5 * millimeter;





/** Unwrap's own fit defaults: 0.005 mm leaves half the 0.01 mm budget to everything else. */
const UNWRAP_FIT_TOLERANCE_BOUNDS = { (millimeter) : [1e-5, 0.005, 1] } as LengthBoundSpec;
const UNWRAP_MAX_CP_BOUNDS = { (unitless) : [4, 60, MAX_CONTROL_POINTS] } as IntegerBoundSpec;

const UNWRAP_LENGTH_BOUNDS = { (millimeter) : [-1e4, 0, 1e4] } as LengthBoundSpec;
const UNWRAP_SPACING_BOUNDS = { (millimeter) : [0.5, 6, 50] } as LengthBoundSpec;

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
                        "Description" : "Constant-thickness parts, draped or not, e.g. a topsheet." }
            definition.parts is Query;

            annotation { "Name" : "Undrape target from", "Default" : UndrapeTargetSource.WIRE, "UIHint" : UIHint.HORIZONTAL_ENUM,
                        "Description" : "The part's mid-surface is mapped onto the extrusion of a wire along its plane normal: a wire you pick, or the section of the mid-surface by a face you pick." }
            definition.targetFrom is UndrapeTargetSource;
        }

        if (definition.unwrapType == UnwrapType.EDGES || definition.targetFrom == UndrapeTargetSource.WIRE)
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

        if (definition.unwrapType == UnwrapType.EDGES)
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

        annotation { "Name" : "Unwrapped origin", "Filter" : BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                    "Description" : "Frame of the flat result: X along the reference, Z along its surface normal." }
        definition.origin is Query;

        if (definition.unwrapType == UnwrapType.THICKENED)
        {
            annotation { "Name" : "Lay the plate on the origin plane", "Default" : true,
                        "Description" : "The flat plate's lower face on the origin's XY plane. Off, its mid-plane keeps its height relative to the alignment point." }
            definition.layOnPlane is boolean;

            annotation { "Name" : "Outline sample spacing", "Description" : "Spacing of the undraped outline samples. 6 mm holds the outline within 0.005 mm; wider is faster." }
            isLength(definition.sampleSpacing, UNWRAP_SPACING_BOUNDS);
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
                        "Description" : "Fill the table from the source bodies: name + suffix, material, appearance. Properties are copied only when this is pressed." }
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
        }
    }
    {
        const sources = sourceBodies(context, definition);
        if (size(sources) == 0)
        {
            throw regenError(definition.unwrapType == UnwrapType.EDGES ? "Select the edges to unwrap." : "Select the parts to unwrap.",
                [definition.unwrapType == UnwrapType.EDGES ? "edges" : "parts"]);
        }
        if ((definition.unwrapType == UnwrapType.EDGES || definition.targetFrom == UndrapeTargetSource.WIRE)
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
        const cs = evMateConnector(context, { "mateConnector" : definition.origin });
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
        var tally = { "line" : 0, "arc" : 0, "freeform" : 0 };
        var records = [];
        var edgesOnPlane = [];

        const edgeChart = (definition.unwrapType == UnwrapType.EDGES)
            ? unwrapChart(context, definition.reference, alignPoint, lengthOffset(definition))
            : undefined;

        for (var i = 0; i < size(sources); i += 1)
        {
            const bodyId = id + ("body" ~ i);
            var result;
            if (definition.unwrapType == UnwrapType.EDGES)
            {
                const edges = evaluateQuery(context, qIntersection([qOwnedByBody(sources[i], EntityType.EDGE), expandEdgeQuery(definition.edges)]));
                result = unwrapEdgesToWires(context, bodyId, edgeChart, cs, edges, settings);
            }
            else
            {
                result = unwrapPlate(context, bodyId, definition, sources[i], alignPoint, cs, settings);
                edgesOnPlane = append(edgesOnPlane, result.edgesOnPlane);
            }

            tally = addTally(tally, result.tally);
            records = append(records, result.record);
            outputs = append(outputs, { "source" : sources[i], "bodies" : result.bodies });
        }

        applyNamesAndProperties(context, definition, outputs);
        reportSummary(context, id, definition, tally, records);

        embedStandardOutputs(context, id, {
                    "output" : qCreatedBy(id, EntityType.BODY),
                    "outputDescription" : "The unwrapped bodies",
                    "inputs" : qUnion(sources),
                    "variables" : {
                        "lineCount" : extractableVariable(tally.line, "Unwrapped edges emitted as lines."),
                        "arcCount" : extractableVariable(tally.arc, "Unwrapped edges emitted as arcs."),
                        "splineCount" : extractableVariable(tally.freeform, "Unwrapped edges emitted as fitted splines.")
                    },
                    "queries" : {
                        "edgesOnPlane" : extractableQuery(qUnion(edgesOnPlane), "Edges of the part sides laid on the unwrap plane.", DebugColor.MAGENTA)
                    }
                });
    }, {});

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

function pointOf(context is Context, selection is Query) returns Vector
{
    if (!isQueryEmpty(context, qBodyType(selection, BodyType.MATE_CONNECTOR)))
    {
        return evMateConnector(context, { "mateConnector" : selection }).origin;
    }
    return evVertexPoint(context, { "vertex" : selection });
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
 *
 * @param flatZ : undefined, or the z (in cs) every unwrapped point is put on -- the plate path
 *      enforces that the outline it lays on the plane IS planar, and reports how far it was off.
 * @returns {map} : { "bodies" (Query), "curves" (Query of the emitted edges), "tally", "record" }
 */
function unwrapEdgesToWires(context is Context, id is Id, chart is map, cs is CoordSystem, edges is array, settings is map) returns map
{
    const emitted = unwrapEdges(context, id, chart, cs, 0 * meter, undefined, edges, settings);
    if (size(emitted.curves) == 0)
    {
        return { "bodies" : qNothing(), "tally" : emitted.tally, "record" : emitted.record };
    }

    const curveBodies = qUnion(emitted.curves);
    opExtractWires(context, id + "wires", { "edges" : qOwnedByBody(curveBodies, EntityType.EDGE) });
    opDeleteBodies(context, id + "deleteCurves", { "entities" : curveBodies });

    return { "bodies" : qCreatedBy(id + "wires", EntityType.BODY), "tally" : emitted.tally, "record" : emitted.record };
}

/**
 * @returns {map} : { "curves" : array of body Queries, "tally", "record", "points" (all unwrapped samples) }
 */
function unwrapEdges(context is Context, id is Id, chart is map, cs is CoordSystem, zShift is ValueWithUnits, flatZ,
    edges is array, settings is map) returns map
{
    var curves = [];
    var tally = { "line" : 0, "arc" : 0, "freeform" : 0 };
    var allPoints = [];
    var worstFlat = 0 * meter;
    var lines = [];

    for (var e = 0; e < size(edges); e += 1)
    {
        const edgeId = id + ("edge" ~ e);
        const length = evLength(context, { "entities" : edges[e] });
        const count = min(UNWRAP_MAX_SAMPLES, max(UNWRAP_MIN_SAMPLES, ceil(length / UNWRAP_SAMPLE_SPACING) + 1));

        var parameters = [];
        for (var j = 0; j < count; j += 1)
        {
            parameters = append(parameters, j / (count - 1));
        }
        const tangentLines = evEdgeTangentLines(context, { "edge" : edges[e], "parameters" : parameters });

        // Fast map, warm-started from the previous sample's foot; exact end tangents from the map's
        // derivative (research_unwrap_perf.md 2.3).
        var points = [];
        var feet = [];
        var previous = undefined;
        const yAxis = cross(cs.zAxis, cs.xAxis);
        for (var tl in tangentLines)
        {
            const u = unwrapFast(chart, tl.origin, previous);
            previous = u;
            var z = u[2] + zShift.value;
            if (flatZ != undefined)
            {
                const off = z - flatZ.value;
                worstFlat = max(worstFlat, abs(off) * meter);
                z = flatZ.value;
            }
            points = append(points, cs.origin + (u[0] * meter) * cs.xAxis + (u[1] * meter) * yAxis + (z * meter) * cs.zAxis);
            feet = append(feet, u);
        }
        const startTangent = flattened(cs, flatZ, unwrapDirection(chart, cs, feet[0], tangentLines[0].direction));
        const endTangent = flattened(cs, flatZ, unwrapDirection(chart, cs, feet[count - 1], tangentLines[count - 1].direction));

        const emitted = emitFlatCurve(context, edgeId, points, startTangent, endTangent, settings);
        const shape = emitted.shape;
        const gate = emitted.gate;

        tally[shape.kind] += 1;
        curves = append(curves, qCreatedBy(edgeId, EntityType.BODY));
        allPoints = concatenateArrays([allPoints, points]);
        lines = append(lines, "    edge " ~ e ~ ": " ~ shape.kind
            ~ (shape.kind == "arc" ? " R " ~ fmtMM(shape.radius, 4, 0) : "")
            ~ ", length " ~ fmtMM(length, 3, 0) ~ " -> chord " ~ fmtMM(norm(points[count - 1] - points[0]), 3, 0) ~ gate);
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
        "points" : allPoints,
        "record" : { "edges" : size(edges), "worstFlat" : worstFlat }
    };
}

/**
 * Emit one unwrapped edge: a line or an (exact, sketch) arc when the points are one within tolerance
 * AND the exactly-unwrapped end tangents agree with it -- so continuity is kept -- a fit otherwise.
 * @returns {map} : { "shape", "gate" (why a line / arc was refused, or "") }
 */
function emitFlatCurve(context is Context, id is Id, points is array, startTangent is Vector, endTangent is Vector,
    settings is map) returns map
{
    const count = size(points);
    var shape = { "kind" : "freeform" };
    var gate = "";
    if (settings.recognise)
    {
        shape = classifyPoints(points, settings.approximation.approximationTolerance, true, true);
        if (shape.kind != "freeform")
        {
            const tangents = shapeEndTangents(shape, startTangent, endTangent);
            const miss = max(angleBetween(tangents[0], startTangent), angleBetween(tangents[1], endTangent));
            if (miss > G1_JUNCTION_ANGLE * radian)
            {
                gate = " (" ~ shape.kind ~ " rejected: end tangent off by " ~ toString(roundToPrecision(miss / degree, 4)) ~ " deg)";
                shape = { "kind" : "freeform" };
            }
        }
    }

    if (shape.kind == "line")
    {
        emitLineCurve(context, id, points[0], points[count - 1]);
    }
    else if (shape.kind == "arc")
    {
        emitArcCurve(context, id, shape);
    }
    else
    {
        // Unit tangents: approximateFamily scales them by the run's chord itself. Pre-scaling
        // them here made the end speed a chord squared and no fit could reach tolerance.
        emitSplineCurve(context, id, points, startTangent, endTangent, settings.approximation);
    }
    return { "shape" : shape, "gate" : gate };
}

/**
 * A difference vector with its cs-Z component removed when the points are being laid flat, normalized.
 */
function flattened(cs is CoordSystem, flatZ, v is Vector) returns Vector
{
    if (flatZ == undefined)
    {
        return normalize(v);
    }
    return normalize(v - dot(v, cs.zAxis) * cs.zAxis);
}

/**
 * Unit tangents of a line or arc answer at its two ends, pointed the way the edge runs.
 */
function shapeEndTangents(shape is map, startHint is Vector, endHint is Vector) returns array
{
    if (shape.kind == "line")
    {
        const direction = normalize(shape.end - shape.start);
        return [direction, direction];
    }

    var a = normalize(cross(shape.normal, shape.start - shape.center));
    var b = normalize(cross(shape.normal, shape.end - shape.center));
    if (dot(a, startHint) < 0)
    {
        a = -a;
    }
    if (dot(b, endHint) < 0)
    {
        b = -b;
    }
    return [a, b];
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
    const groups = evOffsetDetection(context, { "bodies" : part });
    if (size(groups) == 0)
    {
        throw regenError("Could not find two offset faces on the part: it does not look like a constant-thickness plate.", ["parts"]);
    }
    const g = groups[0];
    const thickness = 0.5 * (g.offsetLow + g.offsetHigh);
    const side0 = qUnion(evaluateQuery(context, qTangentConnectedFaces(g.side0[0], 45 * degree)));
    const side1 = qUnion(evaluateQuery(context, qTangentConnectedFaces(g.side1[0], 45 * degree)));
    if (!isQueryEmpty(context, qIntersection([side0, side1])))
    {
        throw regenError("The part's two sides are tangent-connected, so it is not a plate.", ["parts"]);
    }

    var spread = g.offsetHigh - g.offsetLow;
    for (var other in groups)
    {
        spread = max(spread, max(abs(other.offsetHigh - thickness), abs(other.offsetLow - thickness)));
    }

    return {
        "side0" : side0,
        "side1" : side1,
        "walls" : qSubtraction(qOwnedByBody(part, EntityType.FACE), qUnion([side0, side1])),
        "thickness" : thickness,
        "spread" : spread
    };
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
        opExtractSurface(context, id + "mid", {
                    "faces" : sides.side0,
                    "offset" : -0.5 * thickness,
                    "useFacesAroundToTrimOffset" : true
                });
        wire = profileFromFace(context, id + "profile", qCreatedBy(id + "mid", EntityType.BODY), definition.profileFace);
        temporary = [qCreatedBy(id + "mid", EntityType.BODY), wire];
    }

    const offset = definition.flipTargetOffset ? -definition.targetOffset : definition.targetOffset;
    const chart = unwrapChart(context, wire, alignPoint, offset);
    const undraped = undrapeOutline(context, id + "undrape", chart, sides.side0, sides.side1, thickness, definition.sampleSpacing);

    // The mid-surface lands on the target, chart height 0 on it: cs z = -alignHeight. Laid on the plane,
    // the plate's lower face is on cs's XY plane instead.
    const zMid = definition.layOnPlane ? 0.5 * thickness : -chart.alignHeight * meter;
    const yAxis = cross(cs.zAxis, cs.xAxis);
    const flatPlane = plane(cs.origin + zMid * cs.zAxis, cs.zAxis, cs.xAxis);

    var curves = [];
    var tally = { "line" : 0, "arc" : 0, "freeform" : 0 };
    var lines = [];
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
        const emitted = emitFlatCurve(context, edgeId, points, startTangent, endTangent, settings);
        tally[emitted.shape.kind] += 1;
        curves = append(curves, qCreatedBy(edgeId, EntityType.BODY));
        lines = append(lines, "    loop " ~ edge.loop ~ " edge " ~ edge.index ~ ": " ~ emitted.shape.kind
            ~ (emitted.shape.kind == "arc" ? " R " ~ fmtMM(emitted.shape.radius, 4, 0) : "") ~ emitted.gate);
    }
    if (settings.print)
    {
        for (var text in concatenateArrays([undraped.lines, lines]))
        {
            println(text);
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
 * Read the source bodies' names, materials and appearances into the Outputs table -- only when the
 * button is pressed (getProperty works in editing logic, never in the feature body; correction 36).
 * Rows keep the user's output name when the source is unchanged.
 */
export function unwrapEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map, hiddenBodies is Query, clickedButton is string) returns map
{
    if (clickedButton != "readProperties")
    {
        return definition;
    }

    var rows = [];
    for (var body in sourceBodies(context, definition))
    {
        const name = getProperty(context, { "entity" : body, "propertyType" : PropertyType.NAME });
        const mat = getProperty(context, { "entity" : body, "propertyType" : PropertyType.MATERIAL });
        const look = getProperty(context, { "entity" : body, "propertyType" : PropertyType.APPEARANCE });

        var materialData = "";
        var materialLabel = "(none)";
        if (mat != undefined)
        {
            const density = mat.density / (kilogram / meter ^ 3);
            materialData = mat.name ~ "|" ~ toString(density);
            materialLabel = mat.name ~ " (" ~ toString(roundToPrecision(density, 3)) ~ " kg/m^3)";
        }
        var appearanceData = "";
        if (look != undefined)
        {
            appearanceData = look.red ~ "|" ~ look.green ~ "|" ~ look.blue ~ "|" ~ look.alpha;
        }

        rows = append(rows, {
                    "sourceName" : name,
                    "outputName" : name ~ definition.nameSuffix,
                    "materialLabel" : materialLabel,
                    "copyProperties" : true,
                    "materialData" : materialData,
                    "appearanceData" : appearanceData
                });
    }
    definition.outputs = rows;
    return definition;
}

/**
 * Name each output from its table row and copy what the row carries; attributes straight from the source.
 */
function applyNamesAndProperties(context is Context, definition is map, outputs is array)
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

        if (i >= size(definition.outputs))
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
        if (row.materialData != "")
        {
            const parts = splitByRegexp(row.materialData, "\\|");
            setProperty(context, { "entities" : bodies, "propertyType" : PropertyType.MATERIAL,
                        "value" : material(parts[0], stringToNumber(parts[1]) * kilogram / meter ^ 3) });
        }
        if (row.appearanceData != "")
        {
            const c = splitByRegexp(row.appearanceData, "\\|");
            setProperty(context, { "entities" : bodies, "propertyType" : PropertyType.APPEARANCE,
                        "value" : color(stringToNumber(c[0]), stringToNumber(c[1]), stringToNumber(c[2]), stringToNumber(c[3])) });
        }
    }
}

// ============================================================================
// Reporting
// ============================================================================

function reportSummary(context is Context, id is Id, definition is map, tally is map, records is array)
{
    var text = "Unwrapped " ~ size(records) ~ " bod" ~ (size(records) == 1 ? "y" : "ies") ~ ": "
        ~ tally.line ~ " line(s), " ~ tally.arc ~ " arc(s), " ~ tally.freeform ~ " spline(s).";
    for (var r in records)
    {
        if (r.thickness != undefined)
        {
            const u = r.report;
            text = text ~ " Thickness " ~ fmtMM(r.thickness, 4, 0) ~ " mm (offset pairs within " ~ fmtMM(r.spread, 5, 0)
                ~ " mm); mid-surface mapped to the target offset " ~ fmtMM(r.offset, 4, 0) ~ " mm; "
                ~ u.stations ~ " stations, " ~ u.fallbacks ~ " kernel sections. Deformation: lengthwise stretch "
                ~ toString(roundToPrecision((u.stretchMin - 1) * 100, 3)) ~ "% .. " ~ toString(roundToPrecision((u.stretchMax - 1) * 100, 3))
                ~ "%, shear up to " ~ toString(roundToPrecision(u.shearMax * 180 / PI, 2)) ~ " deg; rim "
                ~ fmtMM(u.rim3d, 3, 0) ~ " mm draped -> " ~ fmtMM(u.rimFlat, 3, 0) ~ " mm flat"
                ~ (r.pieces > 1 ? "; " ~ r.pieces ~ " solids" : "") ~ ".";
        }
    }
    println("[unwrap] " ~ text);
    if (size(definition.outputs) != size(records))
    {
        text = text ~ " Press 'Read names and properties' to name the outputs.";
    }
    reportFeatureInfo(context, id, text);
}
