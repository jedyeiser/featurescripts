FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");
import(path : "onshape/std/approximationUtils.fs", version : "2892.0");

//export import wrapCurve
export import(path : "6863116065bf5063633f30ac", version : "a2617201453235e14ae23dd7");

//import curveMappingCore
import(path : "683d867c35fdab9c98d47556", version : "0c37d55f61989bbc2d78b7bd");



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

// Local copy — mirrors debugDrawFrames in wrapCurve.fs.
// Needed because deform imports wrapCurve at a pinned version that predates the export.
function deformDebugDrawFrames(context is Context, frenetPath is map, numSamples is number)
{
    var totalLength = frenetPath.totalLength;
    var arrowLen    = totalLength / max([1, numSamples - 1]) / 3;
    var arrowRadius = arrowLen * 0.05;

    for (var i = 0; i < numSamples; i += 1)
    {
        var s      = totalLength * i / (numSamples - 1);
        var result = getFrameAtArcLength(context, frenetPath, s);
        var origin = result.frame.origin;

        addDebugArrow(context, origin, origin + arrowLen * result.frame.xAxis,  arrowRadius,           DebugColor.RED);
        addDebugArrow(context, origin, origin + arrowLen * yAxis(result.frame),  arrowRadius * (2 / 3), DebugColor.GREEN);
        addDebugArrow(context, origin, origin + arrowLen * result.frame.zAxis,   arrowRadius * 0.5,     DebugColor.BLUE);
    }
}

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

            annotation { "Name" : "Keep wires", "Default" : false, "Description" : "Retain wrapped edge bodies after reconstruction. Merged into one wire body if possible, otherwise a composite part." }
            definition.keepWires is boolean;
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

            annotation { "Name" : "Show from frames", "Description" : "Draw Frenet frame axes along the from reference path", "Default" : false }
            definition.debugShowFromFrames is boolean;

            annotation { "Name" : "Show to frames", "Description" : "Draw Frenet frame axes along the to reference path", "Default" : false }
            definition.debugShowToFrames is boolean;
        }
    }
    {
        // --- Setup (always runs) ---
        var toFrenetPath   = buildFrenetPath(context, id, definition.toEdges,   definition.flipTo);
        var fromFrenetPath = buildFrenetPath(context, id, definition.fromEdges, false);

        var fromRefArc = projectOntoFrenetPath(fromFrenetPath, getRefPoint(context, definition.fromRef), undefined).arcLength;
        var toRefArc   = projectOntoFrenetPath(toFrenetPath,   getRefPoint(context, definition.toRef),   undefined).arcLength;

        // Align isolated from-line xAxes with the to-path normal.
        // Lines adjacent to a curve already received a curve-context xAxis in buildFrenetPath
        // step 4.5; this handles the isolated-line case (no curve neighbour) by borrowing the
        // to-path normal at the corresponding arc position.
        var fromEdgeData = fromFrenetPath.edgeData;
        for (var i = 0; i < size(fromEdgeData); i += 1)
        {
            var ed = fromEdgeData[i];
            if (!ed.isLine) { continue; }

            var hasCurveCtx = (i > 0 && !fromEdgeData[i - 1].isLine) ||
                              (i + 1 < size(fromEdgeData) && !fromEdgeData[i + 1].isLine);
            if (hasCurveCtx) { continue; }

            var midFromArc = ed.startArcLength + ed.length / 2;
            var midToArc   = toRefArc + (midFromArc - fromRefArc);
            var toXAxis    = getFrameAtArcLength(context, toFrenetPath, midToArc).frame.xAxis;

            var tangent   = ed.lineFrame.zAxis;
            var perpXAxis = toXAxis - dot(toXAxis, tangent) * tangent;
            if (norm(perpXAxis) > 1e-6)
            {
                fromEdgeData[i] = mergeMaps(ed, {
                    "lineFrame": coordSystem(ed.lineFrame.origin, normalize(perpXAxis), tangent)
                });
            }
        }
        fromFrenetPath = mergeMaps(fromFrenetPath, { "edgeData": fromEdgeData });

        if (definition.debugShowFromFrames)
            deformDebugDrawFrames(context, fromFrenetPath, 10);
        if (definition.debugShowToFrames)
            deformDebugDrawFrames(context, toFrenetPath, 10);

        // Pre-transform all body vertices in one batch so every edge uses the same
        // canonical mapped position for each shared vertex.  This eliminates the
        // floating-point gap that arises when two adjacent edges sample the same
        // source vertex independently and get slightly different float values.
        var allVerts        = evaluateQuery(context, qOwnedByBody(definition.sourceBody, EntityType.VERTEX));
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
            "samplingDensity"        : definition.samplingDensity,
            "approximationDegree"    : definition.approximationDegree,
            "approximationMaxCPs"    : definition.approximationMaxCPs,
            "approximationTolerance" : definition.approximationTolerance,
            "vertexMap"              : vertexMap
        };

        // Accumulators declared before conditional blocks so all steps share scope
        var edgeMapping                    = [];
        var allFaces                       = [];   // source faces — needed in Step 3 for adjacency
        var reconstructedFaceBodyQueries   = [];
        var reconstructedFaceSourceIndices = [];   // parallel: which allFaces[i] each body came from

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
            allFaces = evaluateQuery(context, qOwnedByBody(definition.sourceBody, EntityType.FACE));

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

                var extractId = id + ("bdry" ~ fIdx);
                try
                {
                    opExtractWires(context, extractId, { "edges": qUnion(wrappedBoundaryEdgeQueries) });
                }
                catch (extractErr)
                {
                    println("ERROR stitching boundary for face " ~ fIdx ~ ": " ~ toString(extractErr));

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

                var fillId = id + ("fill" ~ fIdx);
                try
                {
                    opFillSurface(context, fillId, {
                        "edgesG0"      : bdryEdges,
                        "edgesG1"      : qNothing(),
                        "edgesG2"      : qNothing(),
                        "guideVertices": qNothing()
                    });
                    // Fill succeeded — boundary wire no longer needed
                    opDeleteBodies(context, id + ("deleteBdry" ~ fIdx), { "entities": bdryBody });
                    reconstructedFaceBodyQueries   = append(reconstructedFaceBodyQueries,
                        qCreatedBy(fillId, EntityType.BODY));
                    reconstructedFaceSourceIndices = append(reconstructedFaceSourceIndices, fIdx);
                }
                catch (fillErr)
                {
                    println("ERROR fill face " ~ fIdx ~ ": " ~ toString(fillErr));
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

                    opDeleteBodies(context, id + ("deleteBdry" ~ fIdx), { "entities": bdryBody });
                }
            }
        }

        // --- Step 3: Combine into one body, clean up intermediates ---
        if (runBodies)
        {
            var nRecon = size(reconstructedFaceBodyQueries);
            if (nRecon > 0)
            {
                // BFS union: start from face 0, expand outward through source-face adjacency.
                // This ensures we always attempt neighbours of already-merged geometry,
                // making failures easier to localise visually.

                // Lookup: toString(sourceFace) → index in allFaces
                var srcFaceIdxLookup = {};
                for (var fi = 0; fi < size(allFaces); fi += 1)
                    srcFaceIdxLookup[toString(allFaces[fi])] = fi;

                // Lookup: toString(srcFaceIdx) → index in reconstructedFaceBodyQueries
                var srcIdxToReconIdx = {};
                for (var ri = 0; ri < nRecon; ri += 1)
                    srcIdxToReconIdx[toString(reconstructedFaceSourceIndices[ri])] = ri;

                // Track which recon indices have been visited (merged or failed)
                var visited = {};
                visited["0"] = true;

                var targetQ  = reconstructedFaceBodyQueries[0];
                var opCount  = 0;

                // Seed frontier: neighbours of recon face 0
                var frontier = [];
                var seedAdj  = evaluateQuery(context,
                    qAdjacent(allFaces[reconstructedFaceSourceIndices[0]], AdjacencyType.EDGE, EntityType.FACE));
                for (var af in seedAdj)
                {
                    var adjSrcIdx = srcFaceIdxLookup[toString(af)];
                    if (adjSrcIdx == undefined) { continue; }
                    var adjReconIdx = srcIdxToReconIdx[toString(adjSrcIdx)];
                    if (adjReconIdx == undefined) { continue; }
                    var key = toString(adjReconIdx);
                    if (visited[key] != undefined) { continue; }
                    visited[key] = true;
                    frontier = append(frontier, adjReconIdx);
                }

                while (size(frontier) > 0)
                {
                    var nextFrontier = [];
                    for (var ri2 in frontier)
                    {
                        var toolQ   = reconstructedFaceBodyQueries[ri2];
                        var srcIdx2 = reconstructedFaceSourceIndices[ri2];
                        try
                        {
                            opBoolean(context, id + ("bfsUnion" ~ opCount), {
                                "target"              : targetQ,
                                "tools"               : toolQ,
                                "operationType"       : BooleanOperationType.UNION,
                                "allowSheets"         : true,
                                "makeSolid"           : false,
                                "eraseImprintedEdges" : true
                            });
                            // Merge succeeded — add this face's unvisited neighbours
                            var adjFaces2 = evaluateQuery(context,
                                qAdjacent(allFaces[srcIdx2], AdjacencyType.EDGE, EntityType.FACE));
                            for (var af2 in adjFaces2)
                            {
                                var nSrcIdx = srcFaceIdxLookup[toString(af2)];
                                if (nSrcIdx == undefined) { continue; }
                                var nReconIdx = srcIdxToReconIdx[toString(nSrcIdx)];
                                if (nReconIdx == undefined) { continue; }
                                var nKey = toString(nReconIdx);
                                if (visited[nKey] != undefined) { continue; }
                                visited[nKey] = true;
                                nextFrontier = append(nextFrontier, nReconIdx);
                            }
                        }
                        catch (boolErr)
                        {
                            println("BFS: recon " ~ ri2 ~ " (src " ~ srcIdx2 ~ ") failed: " ~ toString(boolErr));
                            addDebugEntities(context, toolQ, DebugColor.RED);
                        }
                        opCount += 1;
                    }
                    frontier = nextFrontier;
                }
            }

            // Wire cleanup / keep
            if (size(edgeMapping) > 0)
            {
                var allWrappedEdgeQueries = mapArray(edgeMapping, function(m) { return m.wrappedEdge; });
                var allWrappedBodyQueries = mapArray(edgeMapping, function(m) { return m.wrappedBody; });

                if (definition.keepWires)
                {
                    // Merge all wrapped edges into as few wire bodies as possible
                    opExtractWires(context, id + "keepWires", { "edges": qUnion(allWrappedEdgeQueries) });
                    // Delete the individual BSpline curve bodies now that wires are extracted
                    opDeleteBodies(context, id + "deleteWireIntermediates", { "entities": qUnion(allWrappedBodyQueries) });
                    // If extraction produced multiple disconnected wire bodies, wrap in a composite part
                    var wireBodies = evaluateQuery(context, qCreatedBy(id + "keepWires", EntityType.BODY));
                    if (size(wireBodies) > 1)
                    {
                        opCreateCompositePart(context, id + "compositePart", {
                            "bodies": qCreatedBy(id + "keepWires", EntityType.BODY)
                        });
                    }
                }
                else
                {
                    opDeleteBodies(context, id + "deleteWires", { "entities": qUnion(allWrappedBodyQueries) });
                }
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
        var edge    = edgeArray[i];
        var edgeLen = evLength(context, { "entities": edge });
        var numSamples = max([5, ceil(edgeLen / settings.samplingDensity) + 1]);

        var srcPoints = mapArray(evEdgeTangentLines(context, {
            "edge"       : edge,
            "parameters" : range(0, 1, numSamples)
        }), function(x) { return x.origin; });

        var mappedPoints = mapWorldPoints(context, fromMap, toMap,
            settings.fromRefArc, settings.toRefArc, settings.flipToNormal, srcPoints);

        // Snap endpoints to pre-computed vertex-mapped positions so that all edges
        // sharing a source vertex produce BSplines with bit-identical endpoints.
        if (settings.vertexMap != undefined)
        {
            var edgeVerts = evaluateQuery(context, qAdjacent(edge, AdjacencyType.VERTEX, EntityType.VERTEX));
            var nv = size(edgeVerts);
            if (nv == 2)
            {
                var vm0 = settings.vertexMap[toString(edgeVerts[0])];
                var vm1 = settings.vertexMap[toString(edgeVerts[1])];
                if (vm0 != undefined && vm1 != undefined)
                {
                    // Determine which pre-mapped vertex aligns with mappedPoints[0]
                    if (norm(mappedPoints[0] - vm0) <= norm(mappedPoints[0] - vm1))
                    {
                        mappedPoints[0]                    = vm0;
                        mappedPoints[size(mappedPoints) - 1] = vm1;
                    }
                    else
                    {
                        mappedPoints[0]                    = vm1;
                        mappedPoints[size(mappedPoints) - 1] = vm0;
                    }
                }
            }
            else if (nv == 1)
            {
                // Closed edge (full circle etc.) — same vertex at both ends
                var vm = settings.vertexMap[toString(edgeVerts[0])];
                if (vm != undefined)
                {
                    mappedPoints[0]                    = vm;
                    mappedPoints[size(mappedPoints) - 1] = vm;
                }
            }
            // nv == 0: degenerate edge, leave endpoints as-is
        }

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
                "wrappedEdge": qCreatedBy(wrappedId, EntityType.EDGE),
                "wrappedBody": qCreatedBy(wrappedId, EntityType.BODY)
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
