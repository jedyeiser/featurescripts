FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

// IMPORT: reference_side_utils.fs
import(path : "9aebe5ead538258b285aec19", version : "51d2aab9634029141009803d");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/78504463aa9ea7fa3cce2789/3cac74f0bc2b98272db13cd3", version : "b8c80ac05dcfd9f3cc172ffc");
// IMPORT: split_plus_icon.svg (feature icon)
IconNamespace::import(path : "3dba0e19457f30c7c2f9dddd", version : "0f63f7efa33cc8032cc45c9e");

/**
 * Split+ (v2, 2026-09-27): split parts, surfaces, curves or faces by a START tool and, optionally,
 * an END tool, and name what results by INSIDE and OUTSIDE instead of by tool order or normals.
 *
 * INSIDE is the region on the inside of every tool:
 *     with an inside reference   the side of each tool the reference is on;
 *     two tools, no reference    the side of each tool the other tool is on (between the cuts);
 *     one tool, no reference     the tool's front (the side its normal points to) -- a notice says so.
 * OUTSIDE is everything else: START (beyond the start tool) and END (beyond the end tool).
 * A piece beyond both tools (crossing tools) counts as outside only, and a notice says so.
 *
 * The split keeps every piece (opSplitPart KEEP_ALL, start tool first; opSplitFace in a face split);
 * "Keep" then deletes the outside or inside pieces. A face split removes nothing.
 *
 * Every cut is left as two coincident edges (one per piece); the one published is the edge on a
 * piece that is KEPT: the inside piece, or the outside piece when keeping only the outside.
 *
 * Tools: a surface, a face (construction planes included) or a mate connector (its XY plane).
 *
 * Publishes (Extract variables), every key always present (empty when not applicable):
 *     output                        the pieces kept; pieceCount
 *     inside, outside, start, end   the pieces of each region (faces in a face split)
 *     <region>Edges                 their edges except the cuts (boundary edges of a surface)
 *     startCut, endCut              the cut each tool made (edges on the inside piece)
 *     splitEdges                    every cut; splitFaces  faces the splits created (the caps on solids)
 */
annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Split+",
        "Feature Type Description" : "Split parts, surfaces, curves or faces at a start tool and an optional end tool; the pieces are named inside / outside (start, end) from a reference, and you keep both, the inside or the outside.",
        "Filter Selector" : "allparts" }
export const splitPlus = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Split type", "UIHint" : UIHint.HORIZONTAL_ENUM, "Default" : SplitPlusType.PART }
        definition.splitType is SplitPlusType;

        if (definition.splitType == SplitPlusType.PART)
        {
            annotation { "Name" : "Parts, surfaces, or curves to split",
                        "Filter" : EntityType.BODY && (BodyType.SOLID || BodyType.SHEET || BodyType.WIRE) && ModifiableEntityOnly.YES && SketchObject.NO }
            definition.targets is Query;
        }
        else
        {
            annotation { "Name" : "Faces to split", "Filter" : EntityType.FACE && SketchObject.NO && ConstructionObject.NO && ModifiableEntityOnly.YES }
            definition.faceTargets is Query;
        }

        annotation { "Name" : "Start tool", "MaxNumberOfPicks" : 1,
                    "Filter" : (EntityType.BODY && BodyType.SHEET && SketchObject.NO) || EntityType.FACE || BodyType.MATE_CONNECTOR,
                    "Description" : "A surface, face, plane or mate connector. Its cut is published as startCut; what lies beyond it is the start region." }
        definition.startTool is Query;

        annotation { "Name" : "End tool", "MaxNumberOfPicks" : 1,
                    "Filter" : (EntityType.BODY && BodyType.SHEET && SketchObject.NO) || EntityType.FACE || BodyType.MATE_CONNECTOR,
                    "Description" : "Optional second cut (endCut). Inside is then the part between the two tools." }
        definition.endTool is Query;

        annotation { "Name" : "Inside reference", "MaxNumberOfPicks" : 1,
                    "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR,
                    "Description" : "Geometry in the region you call inside. Needed with one tool to name the sides; with two tools inside is between them unless a reference says otherwise." }
        definition.insideReference is Query;

        if (definition.splitType == SplitPlusType.PART)
        {
            annotation { "Name" : "Bodies to keep", "UIHint" : [UIHint.SHOW_LABEL], "Default" : SplitPlusKeep.BOTH }
            definition.keep is SplitPlusKeep;

            annotation { "Name" : "Trim to face boundaries", "Default" : false }
            definition.useTrimmed is boolean;
        }

        annotation { "Name" : "Keep tools", "Default" : false }
        definition.keepTools is boolean;

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print sides", "Default" : false, "Description" : "Which side of each tool is inside, and the pieces of each region." }
            definition.debugPrintSides is boolean;
        }
    }
    {
        const faceMode = definition.splitType == SplitPlusType.FACE;
        if (faceMode)
        {
            verifyNonemptyQuery(context, definition, "faceTargets", ErrorStringEnum.SPLIT_SELECT_TARGETS);
        }
        else
        {
            verifyNonemptyQuery(context, definition, "targets", ErrorStringEnum.SPLIT_SELECT_TARGETS);
        }
        verifyNonemptyQuery(context, definition, "startTool", ErrorStringEnum.SPLIT_SELECT_TOOL);
        const hasEnd = !isQueryEmpty(context, definition.endTool);
        if (hasEnd && !isQueryEmpty(context, qIntersection([definition.startTool, definition.endTool])))
        {
            throw regenError("The start and end tools are the same; pick a different end tool.", ["endTool"]);
        }
        const allTools = qUnion([definition.startTool, definition.endTool]);
        const clash = faceMode ? qIntersection([definition.faceTargets, facesOf(allTools)])
                               : qIntersection([definition.targets, qOwnerBody(allTools)]);
        if (!isQueryEmpty(context, clash))
        {
            throw regenError("A tool is also a target; pick it only once.", ["startTool"]);
        }

        const probe = referenceProbe(context, definition.insideReference);
        const toolInputs = hasEnd ? [definition.startTool, definition.endTool] : [definition.startTool];
        const toolNames = ["start tool", "end tool"];

        // The splits: every piece kept; Keep removes pieces afterwards, by region.
        var tempPlanes = [];
        var tools = [];
        // Part targets are tracked: a split's pieces are attributed to the feature that made the original body, and
        // an EVALUATED target (a Case pattern input, a composite feature's query) resolves to nothing once split
        // (2026-09-28, Referenced part).
        var pieces = faceMode ? qEntityFilter(definition.faceTargets, EntityType.FACE)
                              : qUnion([definition.targets, startTracking(context, definition.targets)]);
        for (var i = 0; i < size(toolInputs); i += 1)
        {
            var tool = toolInputs[i];
            var isPlane = false;
            const cSys = mateConnectorFrame(context, tool);
            if (cSys != undefined)
            {
                const planeId = id + ("plane" ~ i);
                opPlane(context, planeId, { "plane" : plane(cSys) });
                tool = qCreatedBy(planeId, EntityType.FACE);
                tempPlanes = append(tempPlanes, qCreatedBy(planeId, EntityType.BODY));
                isPlane = true;
            }
            tools = append(tools, tool);

            if (faceMode)
            {
                const faceSplitId = id + ("splitFace" ~ i);
                opSplitFace(context, faceSplitId, faceSplitDefinition(context, pieces, tool, isPlane));
                // A split face loses its id and qCreatedBy holds no faces: the halves are
                // qSplitBy's two sides; faces the tool missed keep theirs.
                pieces = qUnion(evaluateQuery(context, qUnion([pieces, qSplitBy(faceSplitId, EntityType.FACE, false), qSplitBy(faceSplitId, EntityType.FACE, true)])));
                continue;
            }
            const splitId = id + ("split" ~ i);
            opSplitPart(context, splitId, {
                        "targets" : pieces,
                        "tool" : tool,
                        "keepTools" : true,
                        "useTrimmed" : definition.useTrimmed,
                        "keepType" : SplitOperationKeepType.KEEP_ALL
                    });
            pieces = qUnion([pieces, qCreatedBy(splitId, EntityType.BODY)]);
        }

        // Which side of each tool is inside, then every piece placed in a region -- read while
        // the tools (and the temporary planes) still exist.
        const sides = insideSides(context, tools, probe, toolNames);
        if (definition.debugPrintSides)
        {
            for (var k = 0; k < size(tools); k += 1)
            {
                println("[split+] " ~ toolNames[k] ~ ": inside is its " ~ (sides.signs[k] > 0 ? "front" : "back") ~ " (" ~ sides.how ~ ").");
            }
        }
        const regions = classifyPieces(context, evaluateQuery(context, pieces), tools, sides.signs);
        const pieceEdges = faceMode ? qAdjacent(pieces, AdjacencyType.EDGE, EntityType.EDGE)
                                    : qOwnedByBody(qUnion(evaluateQuery(context, pieces)), EntityType.EDGE);
        const allCuts = qIntersection([pieceEdges, qCreatedBy(id, EntityType.EDGE)]);
        // The published cut edge must be on a piece that is KEPT: the inside one, or the outside one
        // when keeping outside (the inside pieces are deleted then -- 2026-09-27, RD ext_split).
        const cutsFromInside = faceMode || definition.keep != SplitPlusKeep.OUTSIDE;
        const splitEdges = oneEdgePerCut(context, allCuts, tools, sides.signs, cutsFromInside);
        const cutsByTool = cutsPerTool(context, splitEdges, tools);

        if (size(tempPlanes) > 0)
        {
            opDeleteBodies(context, id + "deletePlanes", { "entities" : qUnion(tempPlanes) });
        }
        if (!definition.keepTools)
        {
            const toolBodies = qBodyType(qEntityFilter(allTools, EntityType.BODY), BodyType.SHEET);
            if (!isQueryEmpty(context, toolBodies))
            {
                opDeleteBodies(context, id + "deleteTools", { "entities" : toolBodies });
            }
        }

        const insideQ = qUnion(regions.inside);
        const startQ = qUnion(regions.start);
        const endQ = qUnion(regions.end);
        const outsideQ = qUnion(concatenateArrays([regions.start, regions.end, regions.beyondBoth]));
        if (!faceMode && definition.keep != SplitPlusKeep.BOTH)
        {
            const removed = definition.keep == SplitPlusKeep.INSIDE ? outsideQ : insideQ;
            if (!isQueryEmpty(context, removed))
            {
                opDeleteBodies(context, id + "deleteSide", { "entities" : removed });
            }
        }
        const kept = qUnion(evaluateQuery(context, pieces));
        if (isQueryEmpty(context, kept))
        {
            throw regenError("Nothing is left: no piece lies " ~ (definition.keep == SplitPlusKeep.INSIDE ? "inside" : "outside") ~ ".", ["keep"]);
        }

        var notes = [];
        if (sides.note != undefined)
        {
            notes = append(notes, sides.note);
        }
        if (size(regions.beyondBoth) > 0)
        {
            notes = append(notes, size(regions.beyondBoth) ~ " piece(s) lie beyond both tools (the tools cross inside the part): counted as outside, in neither start nor end.");
        }
        if (size(regions.onTool) > 0)
        {
            notes = append(notes, size(regions.onTool) ~ " piece(s) lie on a tool, so their side cannot be read: in no region.");
        }
        if (size(notes) > 0)
        {
            reportFeatureInfo(context, id, join(notes, " "));
        }
        if (definition.debugPrintSides)
        {
            println("[split+] inside " ~ size(regions.inside) ~ ", start " ~ size(regions.start) ~ ", end " ~ size(regions.end)
                    ~ ", beyond both " ~ size(regions.beyondBoth) ~ ", on a tool " ~ size(regions.onTool) ~ " piece(s).");
        }

        // Keys are the entities as this feature leaves them, NOT tracked: following them through later
        // edits (a loft merged onto a cut replaces the edge -- correction 52) is the consumer's explicit
        // choice, Extract variables' Track option.
        const edgesOf = function(q is Query) returns Query
            {
                return faceMode ? faceRegionEdges(context, q, allCuts) : regionEdges(q, allCuts);
            };
        const regionKeys = [["inside", insideQ, "The pieces on the inside of every tool."],
                            ["outside", outsideQ, "Every piece not inside: start + end (and any piece beyond both tools)."],
                            ["start", startQ, "The pieces beyond the start tool."],
                            ["end", endQ, "The pieces beyond the end tool (empty with one tool)."]];
        var queries = {
            "splitFaces" : extractableQuery(faceMode ? qNothing() : qIntersection([qOwnedByBody(kept, EntityType.FACE), qCreatedBy(id, EntityType.FACE)]),
                    "Faces the splits created (the caps on solids; empty in a face split).", DebugColor.MAGENTA),
            "splitEdges" : extractableQuery(splitEdges, "Every cut: one edge per cut, on a kept piece (inside, or outside when keeping outside).", DebugColor.MAGENTA),
            "startCut" : extractableQuery(cutsByTool[0], "The cut the start tool made (the edge on a kept piece). A merge onto it replaces the edge: use Track to follow it.", DebugColor.MAGENTA),
            "endCut" : extractableQuery(size(cutsByTool) > 1 ? cutsByTool[1] : qNothing(), "The cut the end tool made (the edge on a kept piece). A merge onto it replaces the edge: use Track to follow it.", DebugColor.MAGENTA)
        };
        for (var key in regionKeys)
        {
            queries[key[0]] = extractableQuery(key[1], key[2], DebugColor.CYAN);
            queries[key[0] ~ "Edges"] = extractableQuery(edgesOf(key[1]), "Edges of the " ~ key[0] ~ " pieces except the cuts (a surface's boundary edges).", DebugColor.CYAN);
        }

        embedStandardOutputs(context, id, settledOutputs(context, {
                    "output" : kept,
                    "outputDescription" : faceMode ? "The split faces" : "The pieces kept",
                    "inputs" : qUnion([faceMode ? definition.faceTargets : definition.targets, allTools]),
                    "variables" : {
                        "pieceCount" : extractableVariable(size(evaluateQuery(context, kept)), "Pieces kept (faces, in a face split).")
                    },
                    "queries" : queries
                }));
    }, {
        "splitType" : SplitPlusType.PART,
        "targets" : qNothing(),
        "faceTargets" : qNothing(),
        "endTool" : qNothing(),
        "insideReference" : qNothing(),
        "keep" : SplitPlusKeep.BOTH,
        "keepTools" : false,
        "useTrimmed" : false,
        "debugPrintSides" : false
    });

/** Split whole parts (bodies), or only faces (nothing removed). */
export enum SplitPlusType
{
    annotation { "Name" : "Part" }
    PART,
    annotation { "Name" : "Face" }
    FACE
}

/** Which pieces a part split keeps. */
export enum SplitPlusKeep
{
    annotation { "Name" : "Both" }
    BOTH,
    annotation { "Name" : "Inside" }
    INSIDE,
    annotation { "Name" : "Outside" }
    OUTSIDE
}

/**
 * The inside side of each tool (+1 its front, -1 its back), how it was decided, and a notice when
 * it is only the start tool's normal.
 */
function insideSides(context is Context, tools is array, probe, toolNames is array) returns map
{
    var signs = [];
    if (probe != undefined)
    {
        for (var k = 0; k < size(tools); k += 1)
        {
            const side = sideSign(context, probe, tools[k]);
            if (side == 0)
            {
                throw regenError("The inside reference lies on the " ~ toolNames[k] ~ " (or no side of it can be read there); pick something clearly inside.",
                    ["insideReference"]);
            }
            signs = append(signs, side);
        }
        return { "signs" : signs, "how" : "from the inside reference" };
    }
    if (size(tools) == 2)
    {
        for (var k = 0; k < 2; k += 1)
        {
            const other = tools[1 - k];
            const side = sideSign(context, evApproximateCentroid(context, { "entities" : other }), tools[k]);
            if (side == 0)
            {
                throw regenError("The start and end tools meet or cross, so 'between' is not defined: pick an inside reference.", ["insideReference"]);
            }
            signs = append(signs, side);
        }
        return { "signs" : signs, "how" : "between the two tools" };
    }
    return { "signs" : [1], "how" : "the start tool's front",
            "note" : "Inside is the start tool's front (the side its normal points to); pick an inside reference to name the sides." };
}

/**
 * Every piece placed by the side of each tool its point is on: inside (inside of every tool),
 * start (beyond the start tool only), end (beyond the end tool only), beyondBoth, onTool.
 */
function classifyPieces(context is Context, pieces is array, tools is array, signs is array) returns map
{
    var result = { "inside" : [], "start" : [], "end" : [], "beyondBoth" : [], "onTool" : [] };
    for (var piece in pieces)
    {
        const point = piecePoint(context, piece, tools);
        var beyond = [];
        var readable = true;
        for (var k = 0; k < size(tools); k += 1)
        {
            const side = sideSign(context, point, tools[k]);
            if (side == 0)
            {
                readable = false;
            }
            beyond = append(beyond, side != signs[k]);
        }
        var key = "inside";
        if (!readable)
        {
            key = "onTool";
        }
        else if (size(tools) == 2 && beyond[0] && beyond[1])
        {
            key = "beyondBoth";
        }
        else if (beyond[0])
        {
            key = "start";
        }
        else if (size(tools) == 2 && beyond[1])
        {
            key = "end";
        }
        result[key] = append(result[key], piece);
    }
    return result;
}

/**
 * The opSplitFace definition for one tool: construction planes and mate-connector planes
 * are infinite (planeTools), a surface body cuts as a body, any other face as a face.
 */
function faceSplitDefinition(context is Context, faces is Query, tool is Query, isPlane is boolean) returns map
{
    var result = { "faceTargets" : faces, "keepToolSurfaces" : true };
    if (isPlane || !isQueryEmpty(context, qConstructionFilter(qEntityFilter(tool, EntityType.FACE), ConstructionObject.YES)))
    {
        result.planeTools = tool;
    }
    else if (!isQueryEmpty(context, qEntityFilter(tool, EntityType.BODY)))
    {
        result.bodyTools = tool;
    }
    else
    {
        result.faceTools = tool;
    }
    return result;
}

/** The boundary of a set of faces except the cuts: edges with exactly one adjacent face in the set. */
function faceRegionEdges(context is Context, faces is Query, cuts is Query) returns Query
{
    const set = qUnion(evaluateQuery(context, faces));
    var boundary = [];
    for (var e in evaluateQuery(context, qSubtraction(qAdjacent(set, AdjacencyType.EDGE, EntityType.EDGE), cuts)))
    {
        if (size(evaluateQuery(context, qIntersection([qAdjacent(e, AdjacencyType.EDGE, EntityType.FACE), set]))) == 1)
        {
            boundary = append(boundary, e);
        }
    }
    return qUnion(boundary);
}

/**
 * The point that stands for a piece when reading its side of the tools -- always a point ON the
 * piece (a body's centroid can lie off it, even across a tool: a ring cut out by a cylinder has
 * its centroid on the axis, inside the cutter -- 2026-09-27, test S11).
 *     face (face split)   its point nearest its centroid
 *     body                of the points nearest each face's centroid (each edge's middle for a
 *                         wire), the one farthest from the nearest tool -- so a solid's cap
 *                         faces, which lie on a tool, never decide
 */
function piecePoint(context is Context, piece is Query, tools is array) returns Vector
{
    if (!isQueryEmpty(context, qEntityFilter(piece, EntityType.FACE)))
    {
        return pointOnFace(context, piece);
    }
    var candidates = [];
    const faces = evaluateQuery(context, qOwnedByBody(piece, EntityType.FACE));
    for (var face in faces)
    {
        candidates = append(candidates, pointOnFace(context, face));
    }
    if (size(faces) == 0)
    {
        for (var edge in evaluateQuery(context, qOwnedByBody(piece, EntityType.EDGE)))
        {
            candidates = append(candidates, evEdgeTangentLine(context, { "edge" : edge, "parameter" : 0.5 }).origin);
        }
    }
    if (size(candidates) == 0)
    {
        return evApproximateCentroid(context, { "entities" : piece });
    }
    var best = candidates[0];
    var bestClearance = -1 * meter;
    for (var candidate in candidates)
    {
        var clearance = inf * meter;
        for (var tool in tools)
        {
            clearance = min(clearance, evDistance(context, { "side0" : candidate, "side1" : tool }).distance);
        }
        if (clearance > bestClearance)
        {
            bestClearance = clearance;
            best = candidate;
        }
    }
    return best;
}

/** A face's point nearest its centroid (a curved face's centroid can lie off it). */
function pointOnFace(context is Context, face is Query) returns Vector
{
    const centroid = evApproximateCentroid(context, { "entities" : face });
    return evDistance(context, { "side0" : centroid, "side1" : face }).sides[1].point;
}

/**
 * The cut edges with each coincident pair reduced to one. Keeping both sides leaves every cut
 * as two edges on top of each other, one per piece, and anything built on both -- an
 * extrude, a fillet -- fails on the overlap. Of a pair, the edge kept is the one on the
 * INSIDE piece of the tool that made it (`fromInside`), or on the OUTSIDE piece when the inside
 * pieces are about to be deleted.
 */
function oneEdgePerCut(context is Context, edges is Query, tools is array, signs is array, fromInside is boolean) returns Query
{
    const all = evaluateQuery(context, edges);
    var middles = [];
    var lengths = [];
    for (var e in all)
    {
        middles = append(middles, evEdgeTangentLine(context, { "edge" : e, "parameter" : 0.5 }).origin);
        lengths = append(lengths, evLength(context, { "entities" : e }));
    }

    var used = makeArray(size(all), false);
    var result = [];
    for (var i = 0; i < size(all); i += 1)
    {
        if (used[i])
        {
            continue;
        }
        var group = [i];
        for (var j = i + 1; j < size(all); j += 1)
        {
            if (!used[j] && abs(lengths[i] - lengths[j]) < REFERENCE_SIDE_MARGIN && norm(middles[i] - middles[j]) < REFERENCE_SIDE_MARGIN)
            {
                group = append(group, j);
                used[j] = true;
            }
        }
        if (size(group) == 1)
        {
            result = append(result, all[i]);
            continue;
        }

        const k = nearestToolIndex(context, middles[i], tools);
        var chosen = all[group[0]];
        for (var g in group)
        {
            const side = edgeSide(context, all[g], tools[k]);
            const onInside = (side > 0 * meter && signs[k] > 0) || (side < 0 * meter && signs[k] < 0);
            if (onInside == fromInside)
            {
                chosen = all[g];
                break;
            }
        }
        result = append(result, chosen);
    }
    return qUnion(result);
}

/** The index of the tool nearest a point. */
function nearestToolIndex(context is Context, point is Vector, tools is array) returns number
{
    var best = 0;
    var bestDistance = inf * meter;
    for (var k = 0; k < size(tools); k += 1)
    {
        const d = evDistance(context, { "side0" : point, "side1" : tools[k] }).distance;
        if (d < bestDistance)
        {
            bestDistance = d;
            best = k;
        }
    }
    return best;
}

/**
 * Signed distance to the tool of the piece an edge bounds, read on its adjacent faces (a
 * point on each face; the largest reading wins, since a cap face lies on the tool itself).
 */
function edgeSide(context is Context, edge is Query, tool is Query) returns ValueWithUnits
{
    var best = 0 * meter;
    for (var face in evaluateQuery(context, qAdjacent(edge, AdjacencyType.EDGE, EntityType.FACE)))
    {
        const onFace = evDistance(context, { "side0" : evApproximateCentroid(context, { "entities" : face }), "side1" : face }).sides[1].point;
        const side = signedSideOf(context, onFace, tool);
        if (side != undefined && abs(side) > abs(best))
        {
            best = side;
        }
    }
    return best;
}

/** The cut edges split by the tool that made each (the nearest tool). */
function cutsPerTool(context is Context, cuts is Query, tools is array) returns array
{
    var byTool = makeArray(size(tools), []);
    for (var e in evaluateQuery(context, cuts))
    {
        const middle = evEdgeTangentLine(context, { "edge" : e, "parameter" : 0.5 }).origin;
        const k = nearestToolIndex(context, middle, tools);
        byTool[k] = append(byTool[k], e);
    }
    var result = [];
    for (var list in byTool)
    {
        result = append(result, qUnion(list));
    }
    return result;
}

/** A region's edges except the cuts; for surfaces only their boundary (one-sided) edges. */
function regionEdges(bodies is Query, cuts is Query) returns Query
{
    const sheetEdges = qEdgeTopologyFilter(qOwnedByBody(qBodyType(bodies, BodyType.SHEET), EntityType.EDGE), EdgeTopology.ONE_SIDED);
    const otherEdges = qOwnedByBody(qUnion([qBodyType(bodies, BodyType.SOLID), qBodyType(bodies, BodyType.WIRE)]), EntityType.EDGE);
    return qSubtraction(qUnion([sheetEdges, otherEdges]), cuts);
}

/** A mate connector's frame, or undefined for anything else. */
function mateConnectorFrame(context is Context, tool is Query)
{
    if (isQueryEmpty(context, qBodyType(tool, BodyType.MATE_CONNECTOR)))
    {
        return undefined;
    }
    return evMateConnector(context, { "mateConnector" : tool });
}
