FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");
import(path : "onshape/std/approximationUtils.fs", version : "2892.0");

//export import wrapCurve
export import(path : "6863116065bf5063633f30ac", version : "a2617201453235e14ae23dd7");


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

export const FaceControlMultiplierBounds = {(unitless) : [2, 5, 10]} as IntegerBoundSpec;

annotation { "Feature Type Name" : "Deform", "Feature Type Description" : "Takes a solid or surface body, from edges and to edges as input. Wraps the body from the from edges to the to edges" }
export const deform = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Source body", "Filter" : EntityType.BODY && (BodyType.SOLID || BodyType.SHEET), "MaxNumberOfPicks" : 1, "Description" : "Source body to deform" }
        definition.sourceBody is Query;

        annotation { "Group Name" : "From data", "Collapsed By Default" : true }
        {
            annotation { "Name" : "From edge(s)",
                "Filter" : EntityType.EDGE,
                "MaxNumberOfPicks" : 10,
                "Description" : "Reference edge(s) to map from (source reference)" }
            definition.fromEdges is Query;

            annotation { "Name" : "From reference", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1, "Description" : "reference point on from curve" }
            definition.fromRef is Query;
        }

        annotation { "Group Name" : "To data", "Collapsed By Default" : true }
        {
            annotation { "Name" : "To edge(s)",
                    "Filter" : EntityType.EDGE,
                    "MaxNumberOfPicks" : 10,
                    "Description" : "Reference edge(s) to map to (target reference)" }
            definition.toEdges is Query;

            annotation { "Name" : "Flip", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false }
            definition.flipTo is boolean;

            annotation { "Name" : "To reference", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1, "Description" : "reference point on to curve" }
            definition.toRef is Query;

            annotation { "Name" : "Flip normal", "Default" : false, "Description" : "When true, flips the frenet frame normal vector on the to chain" }
            definition.flipToNormal is boolean;
        }

        annotation { "Group Name" : "Setup", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Sampling density", "Description" : "Distance between sample points along source edges" }
            isLength(definition.samplingDensity, samplingDensityBounds);

            annotation { "Name" : "Target degree", "Column Name" : "Approximation target degree" }
            isInteger(definition.approximationDegree, DEGREE_BOUND);

            annotation { "Name" : "Maximum control points" }
            isInteger(definition.approximationMaxCPs, { (unitless) : [4, 15, MAX_CONTROL_POINTS] } as IntegerBoundSpec);

            annotation { "Name" : "Tolerance" }
            isLength(definition.approximationTolerance, TOLERANCE_BOUND);

            annotation { "Name" : "uSampling multiplier", "Description" : "Number of u iso curves = N * u control-point count" }
            isInteger(definition.faceUsamplingMultiplier, FaceControlMultiplierBounds);

            annotation { "Name" : "vSampling multiplier", "Description" : "Number of v iso curves = N * v control-point count" }
            isInteger(definition.faceVsamplingMultiplier, FaceControlMultiplierBounds);
        }

        annotation { "Name" : "Debug", "Default" : false }
        definition.debug is boolean;

        annotation { "Group Name" : "Debug", "Collapsed By Default" : false, "Driving Parameter" : "debug" }
        {
            annotation { "Name" : "Create wires", "Default" : false }
            definition.createWires is boolean;

            annotation { "Name" : "Create faces", "Default" : false }
            definition.createFaces is boolean;

            annotation { "Name" : "Create bodies", "Default" : false }
            definition.createBodies is boolean;
        }
    }
    {
        // --- Setup (always runs) ---
        var toFrenetPath   = buildFrenetPath(context, id, definition.toEdges,   definition.flipTo);
        var fromFrenetPath = buildFrenetPath(context, id, definition.fromEdges, false);

        var fromRefArc = projectOntoFrenetPath(fromFrenetPath, getRefPoint(context, definition.fromRef), undefined).arcLength;
        var toRefArc   = projectOntoFrenetPath(toFrenetPath,   getRefPoint(context, definition.toRef),   undefined).arcLength;

        var settings = {
            "fromRefArc"             : fromRefArc,
            "toRefArc"               : toRefArc,
            "flipToNormal"           : definition.flipToNormal,
            "samplingDensity"        : definition.samplingDensity,
            "approximationDegree"    : definition.approximationDegree,
            "approximationMaxCPs"    : definition.approximationMaxCPs,
            "approximationTolerance" : definition.approximationTolerance
        };

        // Accumulators declared before conditional blocks so all steps share scope
        var edgeMapping                  = [];
        var reconstructedFaceBodyQueries = [];
        var allGuideVertexQueries        = [];

        // Cumulative debug conditions: each step implies all prior steps ran
        var runWires  = !definition.debug || definition.createWires  || definition.createFaces || definition.createBodies;
        var runFaces  = !definition.debug || definition.createFaces  || definition.createBodies;
        var runBodies = !definition.debug || definition.createBodies;

        // --- Step 1: Transform edges ---
        if (runWires)
        {
            var allEdges = evaluateQuery(context, qOwnedByBody(definition.sourceBody, EntityType.EDGE));
            edgeMapping  = transformEdges(context, id + "wires", allEdges, fromFrenetPath, toFrenetPath, settings);
        }

        // --- Step 2: Reconstruct faces ---
        if (runFaces)
        {
            var allFaces = evaluateQuery(context, qOwnedByBody(definition.sourceBody, EntityType.FACE));
            for (var fIdx = 0; fIdx < size(allFaces); fIdx += 1)
            {
                var face      = allFaces[fIdx];
                var faceEdges = evaluateQuery(context, qAdjacent(face, AdjacencyType.EDGE, EntityType.EDGE));

                // Gather the wrapped counterpart for each boundary edge of this face
                var wrappedBoundaryEdgeQueries = [];
                for (var eIdx = 0; eIdx < size(faceEdges); eIdx += 1)
                {
                    for (var mIdx = 0; mIdx < size(edgeMapping); mIdx += 1)
                    {
                        if (size(evaluateQuery(context, qIntersection(edgeMapping[mIdx].sourceEdge, faceEdges[eIdx]))) > 0)
                        {
                            wrappedBoundaryEdgeQueries = append(wrappedBoundaryEdgeQueries, edgeMapping[mIdx].wrappedEdge);
                            break;
                        }
                    }
                }

                // Get mapped interior guide points for this face
                var guidePoints = transformFacepoints(context,
                    id + ("faceGuides" ~ fIdx),
                    face,
                    definition.faceUsamplingMultiplier,
                    definition.faceVsamplingMultiplier,
                    fromFrenetPath, toFrenetPath, settings);

                // Create a point body for each guide point
                var faceGuideVertexQueries = [];
                for (var gIdx = 0; gIdx < size(guidePoints); gIdx += 1)
                {
                    var vtxId = id + ("guideVtx" ~ fIdx ~ "_" ~ gIdx);
                    opPoint(context, vtxId, { "point": guidePoints[gIdx] });
                    faceGuideVertexQueries = append(faceGuideVertexQueries, qCreatedBy(vtxId, EntityType.BODY));
                    allGuideVertexQueries  = append(allGuideVertexQueries,  qCreatedBy(vtxId, EntityType.BODY));
                }

                // Fill surface from wrapped boundary edges + interior guide vertices
                var fillId = id + ("fill" ~ fIdx);
                var guideVtxQuery = size(faceGuideVertexQueries) > 0 ? qUnion(faceGuideVertexQueries) : qNothing();
                try
                {
                    opFillSurface(context, fillId, {
                        "edgesG0"      : qUnion(wrappedBoundaryEdgeQueries),
                        "edgesG1"      : qNothing(),
                        "edgesG2"      : qNothing(),
                        "guideVertices": guideVtxQuery
                    });
                    reconstructedFaceBodyQueries = append(reconstructedFaceBodyQueries,
                        qCreatedBy(fillId, EntityType.BODY));
                }
                catch (e) { println("ERROR fill face " ~ fIdx ~ ": " ~ toString(e)); }
            }
        }

        // --- Step 3: Combine into one body, clean up intermediates ---
        if (runBodies)
        {
            var inputIsSolid = size(evaluateQuery(context,
                qBodyType(definition.sourceBody, BodyType.SOLID))) > 0;

            if (size(reconstructedFaceBodyQueries) > 0)
            {
                opBoolean(context, id + "unionFaces", {
                    "tools"               : qUnion(reconstructedFaceBodyQueries),
                    "operationType"       : BooleanOperationType.UNION,
                    "allowSheets"         : true,
                    "makeSolid"           : inputIsSolid,
                    "eraseImprintedEdges" : true
                });
            }

            // Delete all intermediate wire bodies (wrapped edges)
            if (size(edgeMapping) > 0)
            {
                var allWrappedEdgeQueries = mapArray(edgeMapping, function(m) { return m.wrappedEdge; });
                opDeleteBodies(context, id + "deleteWires", { "entities": qUnion(allWrappedEdgeQueries) });
            }

            // Delete all guide vertex bodies
            if (size(allGuideVertexQueries) > 0)
            {
                opDeleteBodies(context, id + "deleteGuideVtx", { "entities": qUnion(allGuideVertexQueries) });
            }
        }
    });


/**
 * Transforms an array of edges from one Frenet path to another.
 * Returns an array of maps { "sourceEdge": Query, "wrappedEdge": Query }.
 *
 * @param context   {Context}
 * @param id        {Id}
 * @param edgeArray {array}  : array of edge Queries to transform
 * @param fromMap   {map}    : result from buildFrenetPath (source reference)
 * @param toMap     {map}    : result from buildFrenetPath (target reference)
 * @param settings  {map}    : {
 *   fromRefArc, toRefArc, flipToNormal,
 *   samplingDensity, approximationDegree, approximationMaxCPs, approximationTolerance
 * }
 * @returns {array} : [{ "sourceEdge": Query, "wrappedEdge": Query }, ...]
 */
export function transformEdges(context is Context, id is Id, edgeArray is array, fromMap is map, toMap is map, settings is map) returns array
{
    var result = [];
    for (var i = 0; i < size(edgeArray); i += 1)
    {
        var edge       = edgeArray[i];
        var edgeLen    = evLength(context, { "entities": edge });
        var numSamples = max([5, ceil(edgeLen / settings.samplingDensity) + 1]);

        var srcPoints = mapArray(evEdgeTangentLines(context, {
            "edge"       : edge,
            "parameters" : range(0, 1, numSamples)
        }), function(x) { return x.origin; });

        var mappedPoints = mapWorldPoints(context, fromMap, toMap,
            settings.fromRefArc, settings.toRefArc, settings.flipToNormal, srcPoints);

        var approxDef = {
            "targets"            : [approximationTarget({ "positions": mappedPoints })],
            "tolerance"          : settings.approximationTolerance,
            "maxControlPoints"   : settings.approximationMaxCPs,
            "degree"             : settings.approximationDegree,
            "isPeriodic"         : false,
            "interpolateIndices" : [0, size(mappedPoints) - 1]
        };
        var wrappedCurve = approximateSpline(context, approxDef)[0];

        var wrappedId = id + (toString(i) ~ "edge");
        try
        {
            opCreateBSplineCurve(context, wrappedId, { "bSplineCurve": wrappedCurve });
            result = append(result, {
                "sourceEdge" : edge,
                "wrappedEdge": qCreatedBy(wrappedId, EntityType.EDGE)
            });
        }
        catch (e) { println("ERROR transformEdge " ~ i ~ ": " ~ toString(e)); }
    }
    return result;
}


/**
 * Samples interior points from a face by creating isoparametric curves,
 * maps them through the Frenet transform, and returns the mapped positions
 * for use as guide vertices in opFillSurface.
 *
 * Interior-only sampling (parameters 0 and 1 excluded) avoids duplicating
 * points that are already captured by the transformed boundary edges.
 *
 * @param context      {Context}
 * @param id           {Id}
 * @param face         {Query}  : source face
 * @param uMultiplier  {number} : u iso curve count = uMultiplier * u control-point dimension
 * @param vMultiplier  {number} : v iso curve count = vMultiplier * v control-point dimension
 * @param fromMap      {map}    : result from buildFrenetPath (source reference)
 * @param toMap        {map}    : result from buildFrenetPath (target reference)
 * @param settings     {map}    : { fromRefArc, toRefArc, flipToNormal, samplingDensity, ... }
 * @returns {array} : array of mapped Vector positions (interior guide points)
 */
export function transformFacepoints(context is Context, id is Id, face is Query, uMultiplier is number, vMultiplier is number, fromMap is map, toMap is map, settings is map) returns array
{
    // 1. Get BSpline surface dimensions from the face approximation
    var surfData = evApproximateBSplineSurface(context, { "face": face });
    var bspl     = surfData.bSplineSurface;
    var uDim     = size(bspl.controlPoints);
    var vDim     = size(bspl.controlPoints[0]);
    var nU       = uMultiplier * uDim;
    var nV       = vMultiplier * vDim;

    // 2. Create isoparametric curves on the face
    var isoId  = id + "isoCurves";
    var uNames = [];
    var vNames = [];
    for (var k = 0; k < nU; k += 1) { uNames = append(uNames, "u" ~ k); }
    for (var k = 0; k < nV; k += 1) { vNames = append(vNames, "v" ~ k); }

    opCreateCurvesOnFace(context, isoId, {
        "curveDefinition" : [
            { "face": face, "creationType": FaceCurveCreationType.DIR1_AUTO_SPACED_ISO, "nCurves": nU, "names": uNames },
            { "face": face, "creationType": FaceCurveCreationType.DIR2_AUTO_SPACED_ISO, "nCurves": nV, "names": vNames }
        ]
    });
    var isoBodies = qCreatedBy(isoId, EntityType.BODY);

    // 3. Sample each iso curve at interior parameters, skip boundary endpoints
    var interiorPoints = [];
    var isoEdges = evaluateQuery(context, qCreatedBy(isoId, EntityType.EDGE));
    for (var e in isoEdges)
    {
        var elen  = evLength(context, { "entities": e });
        var nSamp = max([3, ceil(elen / settings.samplingDensity) + 1]);

        // Parameters strictly between 0 and 1 (endpoints lie on boundary edges)
        var params = [];
        for (var k = 1; k < nSamp - 1; k += 1)
        {
            params = append(params, k / (nSamp - 1));
        }
        if (size(params) == 0) { continue; }

        var tangentLines = evEdgeTangentLines(context, { "edge": e, "parameters": params });
        for (var tl in tangentLines)
        {
            interiorPoints = append(interiorPoints, tl.origin);
        }
    }

    // 4. Delete iso curve bodies — they were only needed for sampling
    opDeleteBodies(context, id + "deleteIso", { "entities": isoBodies });

    // 5. Map all collected interior points through the Frenet transform
    if (size(interiorPoints) == 0) { return []; }
    return mapWorldPoints(context, fromMap, toMap,
        settings.fromRefArc, settings.toRefArc, settings.flipToNormal, interiorPoints);
}
