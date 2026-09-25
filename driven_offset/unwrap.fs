FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

// IMPORT: edge_offset_utils.fs (same document; export-imports curve_core: chains, classifyPoints, emitters)
export import(path : "a2665e22c07b7a6929ce4e80", version : "94bdc9f41a52d2b6f3665595");
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
 * CONSTANT-THICKNESS PART (undrape a plate such as a topsheet): the faces whose normal is the chart's
 * normal are the plate's two sides; their chart heights give the thickness and the constant-thickness
 * check. Length is preserved along the mid-thickness (a bent plate does not stretch there). The inner
 * side's outline is unwrapped onto the plane, a slab is split by walls through it and the waste cut away:
 * flat faces are exact planes, arcs in the outline become true cylinder walls.
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

/** Which curve's length the unwrap preserves. */
export enum UnwrapPreserveLength
{
    annotation { "Name" : "Along the reference" }
    REFERENCE,
    annotation { "Name" : "Along an offset of the reference" }
    OFFSET,
    annotation { "Name" : "Along the part's mid-thickness" }
    MID_THICKNESS
}

/** Samples per unwrapped edge: at least this many, and one per UNWRAP_SAMPLE_SPACING of length. */
const UNWRAP_MIN_SAMPLES = 17;
const UNWRAP_MAX_SAMPLES = 201;
const UNWRAP_SAMPLE_SPACING = 5 * millimeter;

/** Step used to unwrap an edge's end tangent by differencing. */
const UNWRAP_TANGENT_STEP = 1e-5 * meter;

/** Face samples (per direction) when looking for a plate's two sides. */
const UNWRAP_FACE_GRID = 4;

/** A face is one of a plate's sides when every sample's normal is within this of the chart normal. */
const UNWRAP_CAP_ALIGNMENT = 0.95;

/** Margin of the slab around the unwrapped outline. */
const UNWRAP_SLAB_MARGIN = 10 * millimeter;

const UNWRAP_LENGTH_BOUNDS = { (millimeter) : [-1e4, 0, 1e4] } as LengthBoundSpec;
const UNWRAP_THICKNESS_TOL_BOUNDS = { (millimeter) : [1e-6, 0.01, 10] } as LengthBoundSpec;

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
                        "Description" : "Constant-thickness parts draped along the reference, e.g. a topsheet." }
            definition.parts is Query;
        }

        annotation { "Name" : "Wrapped reference", "Filter" : (EntityType.EDGE || BodyType.WIRE) && ConstructionObject.NO,
                    "Description" : "A planar tangent chain the geometry is wrapped along, e.g. the ski's top-surface profile." }
        definition.reference is Query;

        annotation { "Name" : "Preserve length", "Default" : UnwrapPreserveLength.REFERENCE, "UIHint" : UIHint.SHOW_LABEL,
                    "Description" : "Which curve keeps its length when unwrapped. A plate bent over the reference keeps it at its mid-thickness." }
        definition.preserveLength is UnwrapPreserveLength;

        if (definition.preserveLength == UnwrapPreserveLength.OFFSET)
        {
            annotation { "Name" : "Offset", "Description" : "Distance off the reference, along its surface normal." }
            isLength(definition.lengthOffset, UNWRAP_LENGTH_BOUNDS);

            annotation { "Name" : "Flip offset", "Default" : false, "UIHint" : UIHint.OPPOSITE_DIRECTION }
            definition.flipLengthOffset is boolean;
        }

        annotation { "Name" : "Wrapped alignment point", "Filter" : BodyType.MATE_CONNECTOR || EntityType.VERTEX, "MaxNumberOfPicks" : 1,
                    "Description" : "The wrapped point that lands on the unwrapped origin. Must lie within the reference's X span." }
        definition.alignPoint is Query;

        annotation { "Name" : "Unwrapped origin", "Filter" : BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                    "Description" : "Frame of the flat result: X along the reference, Z along its surface normal." }
        definition.origin is Query;

        if (definition.unwrapType == UnwrapType.THICKENED)
        {
            annotation { "Name" : "Lay inner side on the origin plane", "Default" : true,
                        "Description" : "Off, the part keeps its height above the alignment point." }
            definition.layOnPlane is boolean;

            annotation { "Name" : "Thickness tolerance", "Description" : "How far the part may depart from constant thickness." }
            isLength(definition.thicknessTolerance, UNWRAP_THICKNESS_TOL_BOUNDS);
        }

        annotation { "Group Name" : "Lines, arcs & fitting", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Recognise lines and arcs", "Default" : true,
                        "Description" : "Emit an unwrapped edge as a line or an arc when its points are one within tolerance and its end tangents agree." }
            definition.recogniseShapes is boolean;

            drivenOffsetApproximationPredicate(definition);
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
        if (isQueryEmpty(context, definition.reference))
        {
            throw regenError("Select the wrapped reference.", ["reference"]);
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
            ? unwrapChart(context, definition.reference, alignPoint, lengthOffset(definition, undefined))
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
 * The offset of the reference whose length is preserved. `midThickness` is the plate's measured
 * mid-thickness height, undefined outside the plate path.
 */
function lengthOffset(definition is map, midThickness) returns ValueWithUnits
{
    if (definition.preserveLength == UnwrapPreserveLength.OFFSET)
    {
        return definition.flipLengthOffset ? -definition.lengthOffset : definition.lengthOffset;
    }
    if (definition.preserveLength == UnwrapPreserveLength.MID_THICKNESS)
    {
        if (midThickness == undefined)
        {
            throw regenError("Preserve length along the mid-thickness needs Unwrap = Constant-thickness part.", ["preserveLength"]);
        }
        return midThickness;
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

        var points = [];
        for (var tl in tangentLines)
        {
            var p = unwrapPoint(chart, cs, zShift, tl.origin);
            if (flatZ != undefined)
            {
                const off = dot(p - cs.origin, cs.zAxis) - flatZ;
                worstFlat = max(worstFlat, abs(off));
                p = p - off * cs.zAxis;
            }
            points = append(points, p);
        }

        // Exactly unwrapped end tangents, by differencing the map a step along the edge. These
        // are what the line / arc answer must agree with, and what a fitted spline is held to.
        const first = tangentLines[0];
        const last = tangentLines[count - 1];
        const startTangent = flattened(cs, flatZ, unwrapPoint(chart, cs, zShift, first.origin + UNWRAP_TANGENT_STEP * first.direction) - points[0]);
        const endTangent = flattened(cs, flatZ, points[count - 1] - unwrapPoint(chart, cs, zShift, last.origin - UNWRAP_TANGENT_STEP * last.direction));

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
            emitLineCurve(context, edgeId, points[0], points[count - 1]);
        }
        else if (shape.kind == "arc")
        {
            emitArcCurve(context, edgeId, shape);
        }
        else
        {
            const chord = norm(points[count - 1] - points[0]);
            emitSplineCurve(context, edgeId, points, chord * startTangent, chord * endTangent, settings.approximation);
        }

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
 * The plate's two sides and its thickness, from chart heights.
 *
 * A side is a face whose normal is the chart normal at every sample; the sides fall at two
 * heights, and the thickness is the difference. The INNER side is the one nearer the reference
 * -- for a topsheet, the face lying on the ski's top surface.
 */
function plateSides(context is Context, part is Query, chart is map) returns map
{
    var params = [];
    for (var a = 0; a < UNWRAP_FACE_GRID; a += 1)
    {
        for (var b = 0; b < UNWRAP_FACE_GRID; b += 1)
        {
            params = append(params, vector((a + 0.5) / UNWRAP_FACE_GRID, (b + 0.5) / UNWRAP_FACE_GRID));
        }
    }

    var caps = [];
    for (var face in evaluateQuery(context, qOwnedByBody(part, EntityType.FACE)))
    {
        const planes = evFaceTangentPlanes(context, { "face" : face, "parameters" : params, "returnUndefinedOutsideFace" : true });
        var heights = [];
        var aligned = true;
        for (var pl in planes)
        {
            if (pl == undefined)
            {
                continue;
            }
            const surf = referenceSurfaceCoords(chart.alongRef, pl.origin);
            if (abs(dot(pl.normal, surf.normal)) < UNWRAP_CAP_ALIGNMENT)
            {
                aligned = false;
                break;
            }
            heights = append(heights, surf.height);
        }
        if (aligned && size(heights) > 0)
        {
            caps = append(caps, { "face" : face, "heights" : heights });
        }
    }

    if (size(caps) < 2)
    {
        throw regenError("Could not find the two sides of the part along the reference: no pair of faces follows the reference surface.", ["parts"]);
    }

    var low = undefined;
    var high = undefined;
    for (var cap in caps)
    {
        for (var h in cap.heights)
        {
            low = (low == undefined) ? h : min(low, h);
            high = (high == undefined) ? h : max(high, h);
        }
    }
    const split = 0.5 * (low + high);

    var lower = { "faces" : [], "heights" : [] };
    var upper = { "faces" : [], "heights" : [] };
    for (var cap in caps)
    {
        var mean = 0 * meter;
        for (var h in cap.heights)
        {
            mean += h / size(cap.heights);
        }
        if (mean < split)
        {
            lower.faces = append(lower.faces, cap.face);
            lower.heights = concatenateArrays([lower.heights, cap.heights]);
        }
        else
        {
            upper.faces = append(upper.faces, cap.face);
            upper.heights = concatenateArrays([upper.heights, cap.heights]);
        }
    }

    const lowLevel = meanOf(lower.heights);
    const highLevel = meanOf(upper.heights);
    const spread = max(spreadOf(lower.heights, lowLevel), spreadOf(upper.heights, highLevel));
    const lowerIsInner = abs(lowLevel) <= abs(highLevel);

    return {
        "inner" : lowerIsInner ? lower.faces : upper.faces,
        "outer" : lowerIsInner ? upper.faces : lower.faces,
        "innerLevel" : lowerIsInner ? lowLevel : highLevel,
        "outerLevel" : lowerIsInner ? highLevel : lowLevel,
        "thickness" : highLevel - lowLevel,
        "spread" : spread
    };
}

function meanOf(values is array) returns ValueWithUnits
{
    var sum = 0 * meter;
    for (var v in values)
    {
        sum += v;
    }
    return sum / size(values);
}

function spreadOf(values is array, level is ValueWithUnits) returns ValueWithUnits
{
    var worst = 0 * meter;
    for (var v in values)
    {
        worst = max(worst, abs(v - level));
    }
    return worst;
}

/**
 * Undrape one constant-thickness part.
 */
function unwrapPlate(context is Context, id is Id, definition is map, part is Query, alignPoint is Vector,
    cs is CoordSystem, settings is map) returns map
{
    // Heights are read about the reference itself; the chart that is unwrapped through is then built
    // about the offset whose length is preserved.
    const probe = unwrapChart(context, definition.reference, alignPoint, 0 * meter);
    const sides = plateSides(context, part, probe);
    if (sides.spread > definition.thicknessTolerance)
    {
        throw regenError("The part is not of constant thickness along the reference: its sides depart from "
                ~ fmtMM(sides.thickness, 4, 0) ~ " mm by up to " ~ fmtMM(sides.spread, 4, 0) ~ " mm.", ["parts", "thicknessTolerance"]);
    }

    const offset = lengthOffset(definition, 0.5 * (sides.innerLevel + sides.outerLevel));
    const chart = unwrapChart(context, definition.reference, alignPoint, offset);

    // Where the inner side lands, before any shift: its chart height relative to the alignment point.
    const innerZ = sides.innerLevel - offset - chart.align.height;
    const zShift = definition.layOnPlane ? -innerZ : 0 * meter;
    const flatZ = innerZ + zShift;
    const outward = (sides.outerLevel > sides.innerLevel) ? 1 : -1;
    const thickness = abs(sides.thickness);

    const outline = evaluateQuery(context, qLoopEdges(qUnion(sides.inner)));
    const emitted = unwrapEdges(context, id + "outline", chart, cs, zShift, flatZ, outline, settings);

    // Slab: a rectangle on the flat plane around the outline, extruded outward by the thickness.
    const flatPlane = plane(cs.origin + flatZ * cs.zAxis, cs.zAxis, cs.xAxis);
    var lo = undefined;
    var hi = undefined;
    for (var p in emitted.points)
    {
        const q = worldToPlane(flatPlane, p);
        lo = (lo == undefined) ? q : vector(min(lo[0], q[0]), min(lo[1], q[1]));
        hi = (hi == undefined) ? q : vector(max(hi[0], q[0]), max(hi[1], q[1]));
    }
    lo = lo - vector(UNWRAP_SLAB_MARGIN, UNWRAP_SLAB_MARGIN);
    hi = hi + vector(UNWRAP_SLAB_MARGIN, UNWRAP_SLAB_MARGIN);

    const sketchId = id + "slabSketch";
    const sk = newSketchOnPlane(context, sketchId, { "sketchPlane" : flatPlane });
    skRectangle(sk, "slab", { "firstCorner" : lo, "secondCorner" : hi });
    skSolve(sk);

    opExtrude(context, id + "slab", {
                "entities" : qCreatedBy(sketchId, EntityType.FACE),
                "direction" : outward * cs.zAxis,
                "endBound" : BoundingType.BLIND,
                "endDepth" : thickness
            });

    // Walls through the outline, a little past both faces of the slab, split it. The curves are
    // joined into wires first so each closed outline extrudes to ONE sheet, and the split uses the
    // sheet as trimmed -- an untrimmed spline wall would run on past the outline and cut the slab.
    const curveBodies = qUnion(emitted.curves);
    opExtractWires(context, id + "outlineWire", { "edges" : qOwnedByBody(curveBodies, EntityType.EDGE) });
    opExtrude(context, id + "walls", {
                "entities" : qOwnedByBody(qCreatedBy(id + "outlineWire", EntityType.BODY), EntityType.EDGE),
                "direction" : outward * cs.zAxis,
                "endBound" : BoundingType.BLIND,
                "endDepth" : thickness + 1 * millimeter,
                "startBound" : BoundingType.BLIND,
                "startDepth" : 1 * millimeter
            });
    opSplitPart(context, id + "split", {
                "targets" : qCreatedBy(id + "slab", EntityType.BODY),
                "tool" : qCreatedBy(id + "walls", EntityType.BODY),
                "keepTools" : false,
                "useTrimmed" : true
            });

    // The piece holding the slab's corner is the waste around the outline.
    const pieces = qUnion([qCreatedBy(id + "slab", EntityType.BODY), qCreatedBy(id + "split", EntityType.BODY)]);
    const corner = planeToWorld(flatPlane, lo + vector(1 * millimeter, 1 * millimeter)) + 0.5 * thickness * outward * cs.zAxis;
    opDeleteBodies(context, id + "deleteWaste", { "entities" : qContainsPoint(qBodyType(pieces, BodyType.SOLID), corner) });
    opDeleteBodies(context, id + "deleteTemp", { "entities" : qUnion([curveBodies, qCreatedBy(sketchId, EntityType.BODY),
                        qCreatedBy(id + "outlineWire", EntityType.BODY), qCreatedBy(id + "walls", EntityType.BODY)]) });

    const plates = qBodyType(pieces, BodyType.SOLID);
    const plateCount = size(evaluateQuery(context, plates));
    if (plateCount == 0)
    {
        throw regenError("Unwrapping the outline left nothing: the unwrapped outline does not close.", ["parts"]);
    }

    const innerFace = qCoincidesWithPlane(qOwnedByBody(plates, EntityType.FACE), flatPlane);

    return {
        "bodies" : plates,
        "edgesOnPlane" : qAdjacent(innerFace, AdjacencyType.EDGE, EntityType.EDGE),
        "tally" : emitted.tally,
        "record" : {
            "edges" : size(outline),
            "worstFlat" : emitted.record.worstFlat,
            "thickness" : thickness,
            "spread" : sides.spread,
            "innerLevel" : sides.innerLevel,
            "offset" : offset,
            "pieces" : plateCount
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
            text = text ~ " Thickness " ~ fmtMM(r.thickness, 4, 0) ~ " mm (constant within " ~ fmtMM(r.spread, 4, 0)
                ~ " mm), inner side " ~ fmtMM(r.innerLevel, 4, 0) ~ " mm off the reference, length kept "
                ~ fmtMM(r.offset, 4, 0) ~ " mm off it; outline flat within " ~ fmtMM(r.worstFlat, 5, 0) ~ " mm"
                ~ (r.pieces > 1 ? "; " ~ r.pieces ~ " pieces (holes in the outline?)" : "") ~ ".";
        }
    }
    println("[unwrap] " ~ text);
    if (size(definition.outputs) != size(records))
    {
        text = text ~ " Press 'Read names and properties' to name the outputs.";
    }
    reportFeatureInfo(context, id, text);
}
