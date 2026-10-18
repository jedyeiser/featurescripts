FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
import(path : "onshape/std/approximationUtils.fs", version : "3083.0");
import(path : "onshape/std/projectiontype.gen.fs", version : "3083.0");

// curveMappingCore, EXPORT-imported: the dialog's enums (SamplingMode, FrameNormalMode,
// BinormalSource) come from it. wrapCurve is not imported: nothing here uses it, and its own
// export-import of the core could bring a second version of the core into this scope.
// Pinned to the core at curveMapping V28 (adds mapCoordSystems).
export import(path : "683d867c35fdab9c98d47556", version : "87e3a5183c3a065393776f5c");



/**
 * This feature extends the concepts from wrapCurve to deform an entire solid or surface body.
 *
 * Given a source body and two reference edge chains (from/to), every point on the body is
 * mapped from the from-path Frenet frame to the to-path Frenet frame.
 *
 * Debug mode steps through three cumulative sub-operations:
 *   1. createWires  — transform all edges (creates wire bodies)
 *   2. createFaces  — reconstruct all faces using opFillSurface with interior guide points
 *   3. createBodies — union faces into a single body, delete intermediate wire/vertex bodies
 */

export const FaceControlMultiplierBounds = {(unitless) : [2, 3, 10]} as IntegerBoundSpec;

/** A mate connector within this distance of a deformed source body is attached to that body's result. */
const MC_ON_BODY_TOLERANCE = 1e-5 * meter;

const NAME_SEPARATOR ="\n"; // also used as a regexp by splitByRegexp

function splitNames(joined is string) returns array
{
    return joined == "" ? [] : splitByRegexp(joined, NAME_SEPARATOR);
}

/**
 * Editing logic: record the source bodies' names (getProperty works here, not in the feature
 * body) so the feature can name each deformed result "<source name><suffix>".
 */
export function deformEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map, hiddenBodies is Query) returns map
{
    if (definition.deformMode == DeformMode.FACES)
    {
        definition.sourceNames = "";
        return definition;
    }
    var names = [];
    for (var body in evaluateQuery(context, definition.sourceBody))
    {
        var name = getProperty(context, { "entity" : body, "propertyType" : PropertyType.NAME });
        names = append(names, (name is string && name != "") ? name : "Part");
    }
    definition.sourceNames = (size(names) == 0) ? "" : join(names, NAME_SEPARATOR);
    return definition;
}

/** Whether to deform a whole body or a user-selected set of faces. */
export enum DeformMode
{
    annotation { "Name" : "Bodies" }
    BODIES,
    annotation { "Name" : "Faces" }
    FACES
}


/** Vertices of a face closer than this along the from path lie in one cross-section; the cut across the face runs through them. */
const SPLIT_CLUSTER_GAP = 1e-3 * meter;
/** A cluster of vertices must span at least this much to cut across the face (two vertices a hair apart on one side do not). */
const SPLIT_MIN_SPAN = 1e-3 * meter;
/** A face with no vertex clusters to cut at (only its two ends) is cut in the middle of the gap, when the gap is at least this wide. */
const SPLIT_MIN_GAP = 2e-3 * meter;
/** At most this many cuts are made across one face. */
const SPLIT_MAX_CUTS = 40;

/**
 * Arc length along a path of a point, extended past either end along the end tangent (projectOntoFrenetPath clamps
 * to the path, but a face can reach well past the end of the reference).
 */
function pathArcOf(context is Context, path is map, point is Vector) returns ValueWithUnits
{
    const arc = projectOntoFrenetPath(path, point, undefined).arcLength;
    const atStart = arc <= 1e-9 * meter;
    const atEnd = arc >= path.totalLength - 1e-9 * meter;
    if (!atStart && !atEnd)
        return arc;
    const endArc = atStart ? 0 * meter : path.totalLength;
    const endFrame = getFrameAtArcLength(context, path, endArc).frame;
    return endArc + dot(point - endFrame.origin, endFrame.zAxis);
}

/**
 * The cuts that divide a face into pieces, found from its own vertices.
 *
 * Vertices that lie in one cross-section (within SPLIT_CLUSTER_GAP of each other along the from path) are joined by a
 * curve across the face: it starts and ends exactly on existing vertices, so the cut can never leave an edge a few
 * microns long beside a vertex (a plane through one vertex does, and slivers that small make a later union invalid).
 * The two clusters at the ends of the face are not cut. A face with no cluster in between is cut by a plane in the
 * middle of its widest gap instead.
 *
 * @returns {array} of { "points" : array of Vector } (a curve through these vertices) or { "point" : Vector, "arc" : ValueWithUnits } (a plane there)
 */
function faceCuts(context is Context, fromFrenetPath is map, face is Query) returns array
{
    const points = mapArray(evaluateQuery(context, qAdjacent(face, AdjacencyType.VERTEX, EntityType.VERTEX)),
        function(v) { return evVertexPoint(context, { "vertex" : v }); });
    var items = mapArray(points, function(p) { return { "arc" : pathArcOf(context, fromFrenetPath, p), "point" : p }; });
    items = sort(items, function(a, b) { return (a.arc - b.arc) / meter; });

    var clusters = [];
    var lastArc = undefined;
    for (var item in items)
    {
        if (lastArc == undefined || item.arc - lastArc >= SPLIT_CLUSTER_GAP)
            clusters = append(clusters, [item]);
        else
            clusters[size(clusters) - 1] = append(clusters[size(clusters) - 1], item);
        lastArc = item.arc;
    }

    var cuts = [];
    for (var c = 1; c < size(clusters) - 1 && size(cuts) < SPLIT_MAX_CUTS; c += 1)
    {
        const cluster = clusters[c];
        // The curve runs through the cluster's vertices in order along its longest extent.
        var far = cluster[0].point;
        for (var item in cluster)
            if (norm(item.point - cluster[0].point) > norm(far - cluster[0].point))
                far = item.point;
        const axis = far - cluster[0].point;
        if (norm(axis) >= SPLIT_MIN_SPAN)
        {
            const ordered = sort(cluster, function(a, b) { return dot(a.point - cluster[0].point, axis) / (meter ^ 2) - dot(b.point - cluster[0].point, axis) / (meter ^ 2); });
            cuts = append(cuts, { "points" : mapArray(ordered, function(i) { return i.point; }) });
        }
        else
        {
            cuts = append(cuts, { "point" : cluster[0].point, "arc" : cluster[0].arc });
        }
    }
    if (size(cuts) == 0 && size(clusters) >= 2)
    {
        var widest = 0;
        for (var c = 1; c < size(clusters); c += 1)
            if (clusters[c][0].arc - clusters[c - 1][size(clusters[c - 1]) - 1].arc > clusters[widest + 1][0].arc - clusters[widest][size(clusters[widest]) - 1].arc)
                widest = c - 1;
        const before = clusters[widest][size(clusters[widest]) - 1].arc;
        const after = clusters[widest + 1][0].arc;
        if (after - before >= SPLIT_MIN_GAP)
        {
            const arc = (before + after) / 2;
            cuts = append(cuts, { "point" : getFrameAtArcLength(context, fromFrenetPath, arc).frame.origin, "arc" : arc });
        }
    }
    return cuts;
}

/**
 * A work copy of `body` in which each face in `faces` is cut across (see faceCuts). A face with a long, many-edged
 * outline (a band running the length of the part, say) can defeat opFillSurface; the pieces between two such cuts
 * are short and have few edges, and fill well.
 *
 * @returns {map} { "body" : the copy (a sheet body made from all of `body`'s faces), or `undefined` when there was nothing to
 *          cut, "ranOps" : whether any modelling operation was run (a failed cut leaves some behind) }
 */
function splitFacesBetweenVertices(context is Context, id is Id, fromFrenetPath is map, body is Query, faces is array)
{
    var cutsOfFace = [];
    var anyCut = false;
    for (var face in faces)
    {
        const cuts = faceCuts(context, fromFrenetPath, face);
        cutsOfFace = append(cutsOfFace, cuts);
        anyCut = anyCut || size(cuts) > 0;
    }
    if (!anyCut)
        return { "body" : undefined, "ranOps" : false };

    opExtractSurface(context, id + "copy", { "faces" : qOwnedByBody(body, EntityType.FACE) });
    const copy = qCreatedBy(id + "copy", EntityType.BODY);

    var totalCuts = 0;
    for (var f = 0; f < size(faces); f += 1)
    {
        if (size(cutsOfFace[f]) == 0)
            continue;

        // The face on the copy: the one with the same area and corner. Looked up afresh for every face, because cutting
        // one face changes the edges (and so the handles) of the faces beside it.
        const face = faces[f];
        const area = evArea(context, { "entities" : face });
        const corner = evBox3d(context, { "topology" : face }).minCorner;
        var target = undefined;
        var targetGap = 1e-4 * meter;      // box corners of the same face agree to far better than this
        for (var candidate in evaluateQuery(context, qOwnedByBody(copy, EntityType.FACE)))
        {
            const gap = norm(evBox3d(context, { "topology" : candidate }).minCorner - corner);
            if (gap < targetGap && abs(evArea(context, { "entities" : candidate }) - area) < 1e-2 * area)
            {
                target = candidate;
                targetGap = gap;
            }
        }
        if (target == undefined)
        {
            println("WARNING: a face that failed to fill was not found on the work copy; it is not cut");
            continue;
        }

        // The tools: a curve through each cluster of vertices (projected onto the face, so it ends exactly on them), or a plane.
        var curveBodies = [];
        var planeFaces = [];
        for (var k = 0; k < size(cutsOfFace[f]); k += 1)
        {
            const cut = cutsOfFace[f][k];
            const toolId = id + ("tool" ~ f ~ "_" ~ k);
            if (cut.points != undefined)
            {
                opPolyline(context, toolId, { "points" : cut.points });
                curveBodies = append(curveBodies, qCreatedBy(toolId, EntityType.BODY));
            }
            else
            {
                const frame = getFrameAtArcLength(context, fromFrenetPath, cut.arc).frame;
                opPlane(context, toolId, { "plane" : plane(cut.point, frame.zAxis) });
                planeFaces = append(planeFaces, qCreatedBy(toolId, EntityType.FACE));
            }
        }
        var splitDefinition = {
                "faceTargets" : target,
                "projectionType" : ProjectionType.NORMAL_TO_TARGET,
                "extendToCompletion" : true,
                "keepToolSurfaces" : false
            };
        if (size(curveBodies) > 0)
            splitDefinition.edgeTools = qOwnedByBody(qUnion(curveBodies), EntityType.EDGE);
        if (size(planeFaces) > 0)
            splitDefinition.planeTools = qUnion(planeFaces);
        try
        {
            opSplitFace(context, id + ("split" ~ f), splitDefinition);
            totalCuts += size(cutsOfFace[f]);
        }
        catch (splitErr)
        {
            println("WARNING: could not cut a face that failed to fill: " ~ toString(splitErr) ~ " (" ~ size(curveBodies) ~ " curve cut(s), " ~ size(planeFaces) ~ " plane cut(s))");
        }
        if (size(curveBodies) > 0 || size(planeFaces) > 0)
        {
            try silent
            {
                opDeleteBodies(context, id + ("dropTools" ~ f), { "entities" : qUnion([qUnion(curveBodies), qOwnerBody(qUnion(planeFaces))]) });
            }
        }
    }
    if (totalCuts == 0)
    {
        try silent
        {
            opDeleteBodies(context, id + "dropWork", { "entities" : copy });
        }
        return { "body" : undefined, "ranOps" : true };
    }
    println("Deform: " ~ size(faces) ~ " face(s) failed to fill; retrying with " ~ totalCuts ~ " cut(s) across them");
    return { "body" : copy, "ranOps" : true };
}

annotation { "Feature Type Name" : "Deform", "Editing Logic Function" : "deformEditLogic","Feature Type Description" : "Takes a solid or surface body, from edges and to edges as input. Wraps the body from the from edges to the to edges" }
export const deform = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Mode", "UIHint" : UIHint.HORIZONTAL_ENUM }
        definition.deformMode is DeformMode;

        if (definition.deformMode != DeformMode.FACES)
        {
            annotation { "Name" : "Source bodies", "Filter" : EntityType.BODY && (BodyType.SOLID || BodyType.SHEET), "Description" : "Bodies to deform. Each body is deformed separately and produces its own result." }
            definition.sourceBody is Query;
        }
        else
        {
            annotation { "Name" : "Source faces", "Filter" : EntityType.FACE, "Description" : "Faces to deform. Adjacent selected faces will be recombined into sheet bodies." }
            definition.sourceFaces is Query;
        }

        annotation { "Name" : "Mate connectors to deform", "Filter" : BodyType.MATE_CONNECTOR, "Description" : "Mate connectors carried through the same deform: each lands where it would end up if it were part of the source body, with its orientation rotated to match. A connector lying on a deformed source body (within 0.01 mm) is attached to that body's result; any other stays stand-alone." }
        definition.mateConnectors is Query;

        annotation { "Group Name" : "From data", "Collapsed By Default" : true }
        {
            annotation { "Name" : "From edge(s)",
                "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE) || (EntityType.BODY && BodyType.COMPOSITE),
                "MaxNumberOfPicks" : 10,
                "Description" : "Reference edge(s) to map from (source reference). Accepts edges, wire bodies, or composite parts containing wire bodies." }
            definition.fromEdges is Query;

            annotation { "Name" : "From reference", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1, "Description" : "reference point on from curve" }
            definition.fromRef is Query;
        }

        annotation { "Group Name" : "To data", "Collapsed By Default" : true }
        {
            annotation { "Name" : "To edge(s)",
                    "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE) || (EntityType.BODY && BodyType.COMPOSITE),
                    "MaxNumberOfPicks" : 10,
                    "Description" : "Reference edge(s) to map to (target reference). Accepts edges, wire bodies, or composite parts containing wire bodies." }
            definition.toEdges is Query;

            annotation { "Name" : "Flip", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false }
            definition.flipTo is boolean;

            annotation { "Name" : "To reference", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1, "Description" : "reference point on to curve" }
            definition.toRef is Query;

            annotation { "Name" : "Flip normal", "Default" : false, "Description" : "When true, flips the frenet frame normal vector on the to chain" }
            definition.flipToNormal is boolean;
        }

        annotation { "Group Name" : "Frame orientation", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Normal mode", "Default" : FrameNormalMode.FRENET, "UIHint" : UIHint.HORIZONTAL_ENUM, "Description" : "Frenet transports one normal along the whole reference; flip-free, and works on any G1 chain. Binormal builds an in-plane normal from a supplied plane normal; requires planar, coplanar references." }
            definition.frameNormalMode is FrameNormalMode;

            if (definition.frameNormalMode == FrameNormalMode.BINORMAL)
            {
                annotation { "Name" : "Binormal from", "Default" : BinormalSource.QUERY, "UIHint" : UIHint.HORIZONTAL_ENUM }
                definition.binormalSource is BinormalSource;

                if (definition.binormalSource == BinormalSource.QUERY)
                {
                    annotation { "Name" : "Binormal reference", "Filter" : (EntityType.FACE && GeometryType.PLANE) || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1, "Description" : "Planar face (uses its normal) or mate connector (uses its Z axis) defining the reference-path plane normal." }
                    definition.binormalQuery is Query;
                }
                else
                {
                    annotation { "Name" : "X", "Icon" : Icon.ALONG_X }
                    isReal(definition.binormalX, binormalCompBounds);

                    annotation { "Name" : "Y", "Icon" : Icon.ALONG_Y }
                    isReal(definition.binormalY, binormalCompBoundsY);

                    annotation { "Name" : "Z", "Icon" : Icon.ALONG_Z }
                    isReal(definition.binormalZ, binormalCompBounds);
                }
            }
        }

        annotation { "Group Name" : "Setup", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Source sampling mode", "Default" : SamplingMode.CP_BASED, "UIHint" : UIHint.SHOW_LABEL, "Description" : "Specifies if source edges should be sampled based on length between sampling points, or as an integer multiple of the edge control points" }
            definition.sourceSamplingMode is SamplingMode;

            if (definition.sourceSamplingMode == SamplingMode.CP_BASED)
            {
                annotation { "Name" : "Source CP multiplier", "Description" : "Samples per source edge control point" }
                isInteger(definition.sourceCPMultiplier, cpMultiplierBounds);
            }
            else
            {
                annotation { "Name" : "Sampling density", "Description" : "Distance between sample points along source edges" }
                isLength(definition.samplingDensity, samplingDensityBounds);
            }

            annotation { "Name" : "Target degree", "Column Name" : "Approximation target degree" }
            isInteger(definition.approximationDegree, DEGREE_BOUND);

            annotation { "Name" : "Keep source degree", "Default" : false, "Description" : "When true, uses the maximum of the source edge degree and the target degree" }
            definition.keepDegree is boolean;

            annotation { "Name" : "Maximum control points" }
            isInteger(definition.approximationMaxCPs, { (unitless) : [4, 15, MAX_CONTROL_POINTS] } as IntegerBoundSpec);

            annotation { "Name" : "Tolerance" }
            isLength(definition.approximationTolerance, TOLERANCE_BOUND);

            annotation { "Name" : "Use face guide points", "Default" : false, "Description" : "Sample interior points from each face and map them as guide vertices for opFillSurface. Improves shape accuracy on curved faces at the cost of extra computation." }
            definition.useFacePoints is boolean;
            
            if (definition.useFacePoints)
            {
                annotation { "Name" : "uSampling multiplier", "Description" : "Number of u iso curves = N * u control-point count", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE, "Driving Parameter" : "useFacePoints" }
                isInteger(definition.faceUsamplingMultiplier, FaceControlMultiplierBounds);

                annotation { "Name" : "vSampling multiplier", "Description" : "Number of v iso curves = N * v control-point count", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE, "Driving Parameter" : "useFacePoints" }
                isInteger(definition.faceVsamplingMultiplier, FaceControlMultiplierBounds);
            }

            annotation { "Name" : "Keep wires", "Default" : false, "Description" : "Retain wrapped edge bodies after reconstruction. Merged into one wire body if possible, otherwise a composite part." }
            definition.keepWires is boolean;
        }

        annotation { "Group Name" : "Naming", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Name suffix", "Default" : " (deformed)", "Description" : "Appended to each source body's name to name its deformed result. Blank leaves results unnamed." }
            definition.nameSuffix is string;

            // Filled by deformEditLogic: source body names, in selection order, joined by NAME_SEPARATOR.
            annotation { "Name" : "Source names", "UIHint" : UIHint.ALWAYS_HIDDEN }
            definition.sourceNames is string;
        }

        annotation { "Name" : "Debug", "Default" : false }
        definition.debug is boolean;
        
        if (definition.debug)
        {
            annotation { "Group Name" : "Debug", "Collapsed By Default" : false, "Driving Parameter" : "debug" }
            {
                annotation { "Name" : "Create wires", "Default" : false }
                definition.createWires is boolean;
    
                annotation { "Name" : "Create faces", "Default" : false }
                definition.createFaces is boolean;
    
                annotation { "Name" : "Create bodies", "Default" : false }
                definition.createBodies is boolean;
    
                annotation { "Name" : "Show from frames", "Description" : "Draw Frenet frame axes along the from reference path", "Default" : false }
                definition.debugShowFromFrames is boolean;
    
                annotation { "Name" : "Show to frames", "Description" : "Draw Frenet frame axes along the to reference path", "Default" : false }
                definition.debugShowToFrames is boolean;
            }
        }

    }
    {
        // --- Frame orientation ---
        // BINORMAL builds a flip-free in-plane normal from a supplied plane normal (shared by
        // both paths); FRENET is the legacy curvature normal.
        var frameOpts = defaultFrameNormalOptions();
        if (definition.frameNormalMode == FrameNormalMode.BINORMAL)
        {
            var planeRef;
            if (definition.binormalSource == BinormalSource.VECTOR)
            {
                planeRef = resolveBinormalRefFromVector(definition.binormalX, definition.binormalY, definition.binormalZ);
            }
            else
            {
                planeRef = resolveBinormalRefFromQuery(context, definition.binormalQuery);
            }
            frameOpts = planeNormalOptions(planeRef);
        }

        // --- Setup (always runs) ---
        var toFrenetPath   = buildFrenetPath(context, id, expandEdgeQuery(definition.toEdges),   definition.flipTo, frameOpts);
        var fromFrenetPath = buildFrenetPath(context, id, expandEdgeQuery(definition.fromEdges), false,             frameOpts);

        var fromRefArc = projectOntoFrenetPath(fromFrenetPath, getRefPoint(context, definition.fromRef), undefined).arcLength;
        var toRefArc   = projectOntoFrenetPath(toFrenetPath,   getRefPoint(context, definition.toRef),   undefined).arcLength;

        // Bilaterally align isolated line frames so from/to xAxes match regardless
        // of which path the line lives on (e.g. flattening: line is on the to-path).
        var aligned = alignLineFramesBilateral(context, fromFrenetPath, toFrenetPath, fromRefArc, toRefArc, 0.001);
        fromFrenetPath = aligned.fromFrenetPath;
        toFrenetPath   = aligned.toFrenetPath;

        if (definition.debugShowFromFrames)
            debugDrawFrames(context, fromFrenetPath, 10);
        if (definition.debugShowToFrames)
            debugDrawFrames(context, toFrenetPath, 10);

        var isFacesMode = definition.deformMode == DeformMode.FACES;

        if ((isFacesMode ? isQueryEmpty(context, definition.sourceFaces) : isQueryEmpty(context, definition.sourceBody))
            && isQueryEmpty(context, definition.mateConnectors))
        {
            throw regenError(isFacesMode ? "Select faces or mate connectors to deform." : "Select bodies or mate connectors to deform.",
                [isFacesMode ? "sourceFaces" : "sourceBody", "mateConnectors"]);
        }

        // One deform per source body (FACES mode is a single unit). Each unit works under its
        // own id so the ops, qCreatedBy lookups and the union in step 3 never mix bodies.
        var unitBodies = [];
        if (isFacesMode)
        {
            if (!isQueryEmpty(context, definition.sourceFaces))
                unitBodies = [undefined];
        }
        else
        {
            unitBodies = evaluateQuery(context, definition.sourceBody);
        }

        // toString(source body) -> query for its deformed result body; lets mate connectors
        // find the body they now belong to.
        var unitResults = {};

        for (var unitIdx = 0; unitIdx < size(unitBodies); unitIdx += 1)
        {
            var unitBody = unitBodies[unitIdx];
            var uid      = id + ("unit" ~ unitIdx);
            // The body whose faces are rebuilt: the source itself, or on the second attempt a copy in which the
            // faces that failed to fill are cut into pieces. The faces that failed are listed in failedFaces.
            var geomBody    = unitBody;
            var joinId      = uid;       // ids of the final join (step 3); differs from uid when work ops had to run in between
            var failedFaces = [];
            const workId    = id + ("work" ~ unitIdx);

            for (var attempt = 0; attempt < 2; attempt += 1)
            {
                failedFaces = [];
                if (attempt == 1)
                    uid = id + ("unit" ~ unitIdx ~ "retry");


                // Pre-transform all body vertices in one batch so every edge uses the same
                // canonical mapped position for each shared vertex.  This eliminates the
                // floating-point gap that arises when two adjacent edges sample the same
                // source vertex independently and get slightly different float values.
                var allVerts = evaluateQuery(context, isFacesMode
                    ? qAdjacent(definition.sourceFaces, AdjacencyType.VERTEX, EntityType.VERTEX)
                    : qOwnedByBody(geomBody, EntityType.VERTEX));
                var vertSrcPositions = mapArray(allVerts, function(v) {
                    return evVertexPoint(context, { "vertex": v });
                });
                var vertMappedPositions = mapWorldPoints(context, fromFrenetPath, toFrenetPath,
                    fromRefArc, toRefArc, definition.flipToNormal, vertSrcPositions);
                var vertexMap = {};
                for (var vIdx = 0; vIdx < size(allVerts); vIdx += 1)
                {
                    vertexMap[toString(allVerts[vIdx])] = vertMappedPositions[vIdx];
                }

                var settings = {
                    "fromRefArc"             : fromRefArc,
                    "toRefArc"               : toRefArc,
                    "flipToNormal"           : definition.flipToNormal,
                    "samplingMode"           : definition.sourceSamplingMode,
                    "samplingDensity"        : definition.samplingDensity,
                    "sourceCPMultiplier"     : definition.sourceCPMultiplier,
                    "keepDegree"             : definition.keepDegree,
                    "approximationDegree"    : definition.approximationDegree,
                    "approximationMaxCPs"    : definition.approximationMaxCPs,
                    "approximationTolerance" : definition.approximationTolerance,
                    "vertexMap"              : vertexMap
                };

                // Accumulators declared before conditional blocks so all steps share scope
                var edgeMapping = [];

                // Cumulative debug conditions: each step implies all prior steps ran
                var runWires  = !definition.debug || definition.createWires  || definition.createFaces || definition.createBodies;
                var runFaces  = !definition.debug || definition.createFaces  || definition.createBodies;
                var runBodies = !definition.debug || definition.createBodies;

                // --- Step 1: Transform edges ---
                if (runWires)
                {
                    var allEdges = evaluateQuery(context, isFacesMode
                        ? qAdjacent(definition.sourceFaces, AdjacencyType.EDGE, EntityType.EDGE)
                        : qOwnedByBody(geomBody, EntityType.EDGE));
                    edgeMapping  = transformEdges(context, uid + "wires", allEdges, fromFrenetPath, toFrenetPath, settings);
                }

                // --- Step 2: Reconstruct faces ---
                if (runFaces)
                {
                    var allFaces = evaluateQuery(context, isFacesMode
                        ? definition.sourceFaces
                        : qOwnedByBody(geomBody, EntityType.FACE));

                    // Build O(1) lookup: toString(sourceEdge) → wrappedEdge.
                    // toString on a transient query gives a stable unique string per entity,
                    // which is more reliable than == comparison across different evaluateQuery call sites.
                    var edgeLookupMap = {};
                    for (var m in edgeMapping)
                    {
                        edgeLookupMap[toString(m.sourceEdge)] = m.wrappedEdge;
                    }

                    // Pre-evaluate boundary edges for every face once — avoids repeated qAdjacent
                    // kernel calls inside the loop.
                    var faceBoundaryEdgeSets = [];
                    for (var fIdx = 0; fIdx < size(allFaces); fIdx += 1)
                    {
                        faceBoundaryEdgeSets = append(faceBoundaryEdgeSets,
                            evaluateQuery(context, qAdjacent(allFaces[fIdx], AdjacencyType.EDGE, EntityType.EDGE)));
                    }

                    for (var fIdx = 0; fIdx < size(allFaces); fIdx += 1)
                    {
                        var face      = allFaces[fIdx];
                        var faceEdges = faceBoundaryEdgeSets[fIdx];

                        // Gather wrapped counterparts via O(1) map lookup.
                        // Deduplicate by toString(wrappedEdge) to guard against opExtractWires
                        // OVERLAPPING_EDGES when the same wrapped edge resolves for two source keys.
                        var wrappedBoundaryEdgeQueries = [];
                        var seenWrappedKeys = {};
                        for (var eIdx = 0; eIdx < size(faceEdges); eIdx += 1)
                        {
                            var wrappedEdge = edgeLookupMap[toString(faceEdges[eIdx])];
                            if (wrappedEdge != undefined)
                            {
                                var wk = toString(wrappedEdge);
                                if (seenWrappedKeys[wk] == undefined)
                                {
                                    seenWrappedKeys[wk] = true;
                                    wrappedBoundaryEdgeQueries = append(wrappedBoundaryEdgeQueries, wrappedEdge);
                                }
                                // else: duplicate wrapped edge — skip to avoid OVERLAPPING_EDGES
                            }
                            else
                            {
                                println("WARNING: face " ~ fIdx ~ " boundary edge " ~ eIdx ~
                                        " has no wrapped counterpart — it likely failed in transformEdges");
                            }
                        }

                        // Each wrapped edge lives in its own wire body — topologically disconnected
                        // from its neighbours even when endpoints are geometrically coincident.
                        // opFillSurface needs a closed, topologically-connected boundary, so we
                        // stitch the edges into a proper wire first with opExtractWires.
                        if (size(wrappedBoundaryEdgeQueries) == 0)
                        {
                            println("WARNING: face " ~ fIdx ~ " has no wrapped boundary edges — skipping fill");
                            continue;
                        }

                        var extractId = uid + ("bdry" ~ fIdx);
                        try
                        {
                            opExtractWires(context, extractId, { "edges": qUnion(wrappedBoundaryEdgeQueries) });
                        }
                        catch (extractErr)
                        {
                            println("ERROR stitching boundary for face " ~ fIdx ~ ": " ~ toString(extractErr));
                            failedFaces = append(failedFaces, face);

                            // Cyan = undeformed source boundary edges
                            for (var seq in faceEdges)
                                addDebugEntities(context, seq, DebugColor.CYAN);
                            // Red = wrapped edges that failed to stitch
                            for (var beq in wrappedBoundaryEdgeQueries)
                                addDebugEntities(context, beq, DebugColor.RED);

                            // ── Source edges with expected mapped positions ──────────────────────
                            println("  face " ~ fIdx ~ ": " ~ size(faceEdges) ~ " src edges / " ~
                                    size(wrappedBoundaryEdgeQueries) ~ " wrapped:");
                            for (var dIdx = 0; dIdx < size(faceEdges); dIdx += 1)
                            {
                                var srcPts = evEdgeTangentLines(context, {
                                    "edge": faceEdges[dIdx], "parameters": [0, 1]
                                });
                                var expPts = mapWorldPoints(context, fromFrenetPath, toFrenetPath,
                                    settings.fromRefArc, settings.toRefArc, settings.flipToNormal,
                                    [srcPts[0].origin, srcPts[1].origin]);
                                println("  src " ~ dIdx ~ ": " ~ toString(srcPts[0].origin) ~
                                        " → " ~ toString(srcPts[1].origin));
                                println("  exp " ~ dIdx ~ ": " ~ toString(expPts[0]) ~
                                        " → " ~ toString(expPts[1]));
                            }

                            // ── Actual wrapped edge endpoints ────────────────────────────────────
                            var wrappedPtPairs = [];
                            for (var wIdx = 0; wIdx < size(wrappedBoundaryEdgeQueries); wIdx += 1)
                            {
                                var wPts = evEdgeTangentLines(context, {
                                    "edge": wrappedBoundaryEdgeQueries[wIdx], "parameters": [0, 1]
                                });
                                wrappedPtPairs = append(wrappedPtPairs, wPts);
                                // If counts match, also print the lookup error vs expected
                                if (size(faceEdges) == size(wrappedBoundaryEdgeQueries))
                                {
                                    var srcP = evEdgeTangentLines(context, {
                                        "edge": faceEdges[wIdx], "parameters": [0, 1]
                                    });
                                    var expP = mapWorldPoints(context, fromFrenetPath, toFrenetPath,
                                        settings.fromRefArc, settings.toRefArc, settings.flipToNormal,
                                        [srcP[0].origin, srcP[1].origin]);
                                    var errS = norm(expP[0] - wPts[0].origin);
                                    var errE = norm(expP[1] - wPts[1].origin);
                                    println("  wrapped " ~ wIdx ~ ": " ~ toString(wPts[0].origin) ~
                                            " → " ~ toString(wPts[1].origin) ~
                                            "  lookupErr start=" ~ toString(errS) ~
                                            " end=" ~ toString(errE));
                                }
                                else
                                {
                                    println("  wrapped " ~ wIdx ~ ": " ~ toString(wPts[0].origin) ~
                                            " → " ~ toString(wPts[1].origin));
                                }
                            }

                            // ── Open endpoint analysis ───────────────────────────────────────────
                            // Report any wrapped endpoint that has no matching endpoint within 1e-5 m.
                            var ETOL = 1e-5 * meter;
                            for (var wIdx = 0; wIdx < size(wrappedPtPairs); wIdx += 1)
                            {
                                for (var ep = 0; ep < 2; ep += 1)
                                {
                                    var pos = wrappedPtPairs[wIdx][ep].origin;
                                    var bestGap = 1e10 * meter;
                                    var bestJ   = -1;
                                    for (var jIdx = 0; jIdx < size(wrappedPtPairs); jIdx += 1)
                                    {
                                        if (jIdx == wIdx) { continue; }
                                        for (var jp = 0; jp < 2; jp += 1)
                                        {
                                            var d = norm(pos - wrappedPtPairs[jIdx][jp].origin);
                                            if (d < bestGap) { bestGap = d; bestJ = jIdx; }
                                        }
                                    }
                                    if (bestGap > ETOL)
                                    {
                                        println("  OPEN END: wrapped[" ~ wIdx ~ "]" ~
                                                (ep == 0 ? ".start" : ".end") ~
                                                " nearest=wrapped[" ~ bestJ ~ "] gap=" ~ toString(bestGap));
                                    }
                                }
                            }

                            continue;
                        }

                        var bdryEdges = qCreatedBy(extractId, EntityType.EDGE);
                        var bdryBody  = qCreatedBy(extractId, EntityType.BODY);

                        // Generate mapped interior guide points for this face.
                        // Wrapped boundary edges only constrain the perimeter; guide vertices
                        // pull the fill surface interior toward the correct deformed shape.
                        // Gated by definition.useFacePoints — skipped by default because
                        // opCreateCurvesOnFace adds significant computation per face.
                        var guideVerticesQuery = qNothing();
                        var guideVtxDeleteId   = undefined;
                        // Declared outside the try so a failure part-way through the loop can remove the
                        // points already made (they used to be left behind as stray point bodies).
                        var faceGuideVtxQueries = [];
                        if (definition.useFacePoints) try
                        {
                            var guidePoints = transformFacepoints(
                                context, uid + ("faceGuides" ~ fIdx), face,
                                definition.faceUsamplingMultiplier, definition.faceVsamplingMultiplier,
                                fromFrenetPath, toFrenetPath, settings);

                            for (var gIdx = 0; gIdx < size(guidePoints); gIdx += 1)
                            {
                                var vtxId = uid + ("guideVtx" ~ fIdx ~ "_" ~ gIdx);
                                opPoint(context, vtxId, { "point": guidePoints[gIdx] });
                                faceGuideVtxQueries = append(faceGuideVtxQueries,
                                    qCreatedBy(vtxId, EntityType.BODY));
                            }
                            if (size(faceGuideVtxQueries) > 0)
                            {
                                guideVerticesQuery = qUnion(faceGuideVtxQueries);
                                guideVtxDeleteId   = uid + ("deleteGuideVtx" ~ fIdx);
                            }
                        }
                        catch (guideErr)
                        {
                            println("WARNING: guide point generation failed for face " ~ fIdx ~
                                    ": " ~ toString(guideErr));
                            if (size(faceGuideVtxQueries) > 0)
                            {
                                opDeleteBodies(context, uid + ("discardGuideVtx" ~ fIdx), { "entities": qUnion(faceGuideVtxQueries) });
                            }
                        }

                        var fillId = uid + ("fill" ~ fIdx);
                        try
                        {
                            opFillSurface(context, fillId, {
                                "edgesG0"      : bdryEdges,
                                "edgesG1"      : qNothing(),
                                "edgesG2"      : qNothing(),
                                "guideVertices": guideVerticesQuery
                            });
                            // Fill succeeded — cleanup intermediates
                            opDeleteBodies(context, uid + ("deleteBdry" ~ fIdx), { "entities": bdryBody });
                            if (guideVtxDeleteId != undefined)
                                opDeleteBodies(context, guideVtxDeleteId, { "entities": guideVerticesQuery });
                        }
                        catch (fillErr)
                        {
                            println("ERROR fill face " ~ fIdx ~ ": " ~ toString(fillErr));
                            failedFaces = append(failedFaces, face);
                            // Cyan = undeformed source boundary, Red = stitched wrapped boundary
                            for (var seq in faceEdges)
                                addDebugEntities(context, seq, DebugColor.CYAN);
                            addDebugEntities(context, bdryEdges, DebugColor.RED);

                            // ── Stitched edge endpoints + open endpoint analysis ─────────────────
                            var stitchedList = evaluateQuery(context, bdryEdges);
                            println("  stitched boundary: " ~ size(stitchedList) ~ " edges");
                            var stPtPairs = [];
                            for (var sIdx = 0; sIdx < size(stitchedList); sIdx += 1)
                            {
                                var sPts = evEdgeTangentLines(context, {
                                    "edge": stitchedList[sIdx], "parameters": [0, 1]
                                });
                                stPtPairs = append(stPtPairs, sPts);
                                println("  stitched[" ~ sIdx ~ "]: " ~ toString(sPts[0].origin) ~
                                        " → " ~ toString(sPts[1].origin));
                            }
                            var ETOL = 1e-5 * meter;
                            for (var sIdx = 0; sIdx < size(stPtPairs); sIdx += 1)
                            {
                                for (var ep = 0; ep < 2; ep += 1)
                                {
                                    var pos = stPtPairs[sIdx][ep].origin;
                                    var bestGap = 1e10 * meter;
                                    var bestJ   = -1;
                                    for (var jIdx = 0; jIdx < size(stPtPairs); jIdx += 1)
                                    {
                                        if (jIdx == sIdx) { continue; }
                                        for (var jp = 0; jp < 2; jp += 1)
                                        {
                                            var d = norm(pos - stPtPairs[jIdx][jp].origin);
                                            if (d < bestGap) { bestGap = d; bestJ = jIdx; }
                                        }
                                    }
                                    if (bestGap > ETOL)
                                    {
                                        println("  OPEN END: stitched[" ~ sIdx ~ "]" ~
                                                (ep == 0 ? ".start" : ".end") ~
                                                " nearest=stitched[" ~ bestJ ~ "] gap=" ~ toString(bestGap));
                                    }
                                }
                            }

                            opDeleteBodies(context, uid + ("deleteBdry" ~ fIdx), { "entities": bdryBody });
                            if (guideVtxDeleteId != undefined)
                                opDeleteBodies(context, guideVtxDeleteId, { "entities": guideVerticesQuery });
                        }
                    }
                }

                // Faces that would not fill: rebuild them from pieces, but only when there is something to cut
                // (otherwise keep what this attempt built).
                if (attempt == 0 && size(failedFaces) > 0 && !isFacesMode)
                {
                    const cut = splitFacesBetweenVertices(context, workId, fromFrenetPath, unitBody, failedFaces);
                    const cutBody = cut.body;
                    if (cutBody == undefined && cut.ranOps)
                    {
                        // The failed cut left operations of its own in the history: the ids of this attempt cannot be
                        // used again (an id prefix must stay contiguous), so the join takes a prefix of its own.
                        joinId = id + ("join" ~ unitIdx);
                    }
                    if (cutBody != undefined)
                    {
                        try
                        {
                            opDeleteBodies(context, id + ("discard" ~ unitIdx), { "entities" : qCreatedBy(uid, EntityType.BODY) });
                        }
                        catch (discardErr)
                        {
                            println("WARNING: could not discard the first attempt: " ~ toString(discardErr));
                        }
                        geomBody = cutBody;
                        continue;
                    }
                }

                // --- Step 3: Combine into one body, clean up intermediates ---
                if (runBodies)
                {
                    // Wire cleanup first — so qCreatedBy(uid) only sees face surface bodies
                    // by the time we boolean.
                    if (size(edgeMapping) > 0)
                    {
                        var allWrappedEdgeQueries = mapArray(edgeMapping, function(m) { return m.wrappedEdge; });
                        var allWrappedBodyQueries = mapArray(edgeMapping, function(m) { return m.wrappedBody; });

                        if (definition.keepWires)
                        {
                            // Try to stitch all wrapped edges into clean wire bodies.
                            // This will fail with NON_MANIFOLD whenever 3+ faces share a vertex
                            // (which is normal for any solid) — fall back to a composite part
                            // of the raw per-edge wire bodies in that case.
                            try
                            {
                                opExtractWires(context, joinId + "keepWires", { "edges": qUnion(allWrappedEdgeQueries) });
                                opDeleteBodies(context, joinId + "deleteWireIntermediates", { "entities": qUnion(allWrappedBodyQueries) });
                                opCreateCompositePart(context, joinId + "compositePart", {
                                    "bodies" : qCreatedBy(joinId + "keepWires", EntityType.BODY),
                                    "closed" : true
                                });
                            }
                            catch
                            {
                                opCreateCompositePart(context, joinId + "compositePart", {
                                    "bodies" : qUnion(allWrappedBodyQueries),
                                    "closed" : true
                                });
                            }
                        }
                        else
                        {
                            opDeleteBodies(context, joinId + "deleteWires", { "entities": qUnion(allWrappedBodyQueries) });
                        }
                    }

                    // Boolean all reconstructed face surface bodies.
                    // Use qCreatedBy(uid) filtered to SHEET — this is equivalent to what
                    // the user confirmed works in manual Onshape testing.  Wire bodies
                    // have already been deleted above, so only fill surfaces remain.
                    // In FACES mode the result is always one or more sheet bodies — no makeSolid.
                    var inputIsSolid = !isFacesMode &&
                        !isQueryEmpty(context, qBodyType(unitBody, BodyType.SOLID));
                    var surfaceBodies     = qBodyType(qCreatedBy(uid, EntityType.BODY), BodyType.SHEET);
                    var surfaceBodiesList = evaluateQuery(context, surfaceBodies);
                    if (size(surfaceBodiesList) > 1)
                    {
                        opBoolean(context, joinId + "unionFaces", {
                            "tools"        : surfaceBodies,
                            "operationType": BooleanOperationType.UNION,
                            "makeSolid"    : inputIsSolid,
                        });
                    }
                    else if (size(surfaceBodiesList) == 1 && inputIsSolid)
                    {
                        // Single surface body (all fills already merged) — attempt to close into solid.
                        try
                        {
                            opBoolean(context, joinId + "closeSolid", {
                                "tools"        : surfaceBodies,
                                "operationType": BooleanOperationType.UNION,
                                "makeSolid"    : true,
                            });
                        }
                        catch (solidErr)
                        {
                            println("WARNING: could not close surface into solid: " ~ toString(solidErr));
                        }
                    }
                }
                break;
            }
            if (geomBody != unitBody)
            {
                try silent
                {
                    opDeleteBodies(context, workId + "drop", { "entities" : geomBody });
                }
            }
            if (size(failedFaces) > 0)
            {
                reportFeatureWarning(context, id, size(failedFaces) ~ " face(s) of a source body could not be rebuilt and are missing from the result (see the notices).");
            }

            // Record this unit's deformed body and name it. Source names cannot be read here
            // (getProperty is unavailable on the current context), so the name comes from the dialog.
            if (!isFacesMode)
            {
                var unitResult = qUnion([qBodyType(qCreatedBy(uid, EntityType.BODY), BodyType.SOLID),
                                         qBodyType(qCreatedBy(uid, EntityType.BODY), BodyType.SHEET),
                                         qBodyType(qCreatedBy(joinId, EntityType.BODY), BodyType.SOLID),
                                         qBodyType(qCreatedBy(joinId, EntityType.BODY), BodyType.SHEET)]);
                unitResults[toString(unitBody)] = unitResult;

                // Names come from deformEditLogic (getProperty is not allowed in a feature body);
                // only trust them when the count still matches the selection.
                var sourceNameList = splitNames(definition.sourceNames);
                if (definition.nameSuffix != "" && size(sourceNameList) == size(unitBodies) && !isQueryEmpty(context, unitResult))
                {
                    try silent
                    {
                        setProperty(context, {
                            "entities"     : unitResult,
                            "propertyType" : PropertyType.NAME,
                            "value"        : sourceNameList[unitIdx] ~ definition.nameSuffix
                        });
                    }
                }
            }
        }

        // --- Mate connectors: where do they end up under the same deform? ---
        if (!isQueryEmpty(context, definition.mateConnectors))
        {
            var mcQueries = evaluateQuery(context, definition.mateConnectors);
            var mcSystems = mapArray(mcQueries, function(mc) {
                return evMateConnector(context, { "mateConnector" : mc });
            });
            var mcMapped = mapCoordSystems(context, fromFrenetPath, toFrenetPath,
                fromRefArc, toRefArc, definition.flipToNormal, mcSystems);

            for (var mcIdx = 0; mcIdx < size(mcQueries); mcIdx += 1)
            {
                var mcId  = id + ("mateConnector" ~ mcIdx);
                var mcDef = { "coordSystem" : mcMapped[mcIdx] };

                // A connector on a deformed body goes onto that body's result; anything else stays stand-alone.
                // FeatureScript cannot read which body owns a mate connector (qOwnerBody returns the
                // connector itself), so a connector counts as belonging to the source body it lies on.
                for (var srcBody in unitBodies)
                {
                    var ownerResult = unitResults[toString(srcBody)];
                    if (ownerResult != undefined && size(evaluateQuery(context, ownerResult)) == 1 &&
                        evDistance(context, { "side0" : mcSystems[mcIdx].origin, "side1" : srcBody }).distance < MC_ON_BODY_TOLERANCE)
                    {
                        mcDef.owner = ownerResult;
                        break;
                    }
                }

                opMateConnector(context, mcId, mcDef);
            }
        }

    }, {
        // Saved features predate these parameters: keep their behaviour (no connectors, no naming).
        "mateConnectors" : qNothing(),
        "nameSuffix"     : "",
        "sourceNames"    : ""
    });



