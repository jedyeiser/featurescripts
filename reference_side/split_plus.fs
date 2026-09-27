FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

// IMPORT: reference_side_utils.fs
import(path : "9aebe5ead538258b285aec19", version : "afc5b762b2b4bf44f321ebce");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/78504463aa9ea7fa3cce2789/3cac74f0bc2b98272db13cd3", version : "b8c80ac05dcfd9f3cc172ffc");
// IMPORT: split_plus_icon.svg (feature icon)
IconNamespace::import(path : "3dba0e19457f30c7c2f9dddd", version : "0f63f7efa33cc8032cc45c9e");

/**
 * Split+: the standard part split, with several targets and several tools, and -- when one
 * side is kept -- the side named by a reference instead of by a flip.
 *
 * Every target is split by every tool in turn (opSplitPart, one call per tool, the pieces of
 * one split being the targets of the next). With "Keep both sides" that is the built-in
 * split repeated. With it off, each tool keeps the side of itself the REFERENCE is on
 * ("Keep side nearest reference" on) or the other side (off): what survives is the region
 * on the reference's side of every tool, whatever the tools' orientations.
 *
 * The side is read per tool as the reference's signed distance to that tool (positive on
 * the side its normal points to), and opSplitPart's KEEP_FRONT keeps exactly that side
 * (verified 2026-09-23: a +Z plane tool keeps z > 0 with KEEP_FRONT). Without a reference
 * the box is the built-in's own: on keeps the front of every tool, off the back.
 *
 * Tools: surfaces, faces (construction planes included) and mate connectors (their XY
 * plane). A multi-face surface splits by its faces as they are; "Trim to face boundaries"
 * applies to single faces, as in the built-in.
 *
 * FACE split type: the faces picked are split by every tool in turn (opSplitFace; planes
 * and mate connectors infinite, surfaces and faces as they are); nothing is removed, and
 * the pieces and regions below are FACES. The side reference, when given, only names the
 * regions of a one-tool split (near / far). Edge tools are not offered: the side rule needs
 * a surface; use the built-in Split for projected edges.
 *
 * REGIONS. Every resulting piece is placed by which side of each tool it lies on (its
 * centroid's signed distance), and named by position along the tools, in the order picked:
 *     1 tool      near / far (from the keep side reference), or front / back (the sides the
 *                 tool's normal points to and from) when there is no reference
 *     2 tools     start (beyond the first tool, away from the second), middle, end
 *     3+ tools    start, middle1 .. middleN-1, end
 * "Forward" for a tool is the side the next tool is on (for the last: away from the one
 * before), so orientation never matters. When the tools cross inside the part (a normal
 * way to keep a corner) or were picked out of order, no piece fits a band: the split is
 * made as asked and the region keys are published empty, with an info notice saying why.
 *
 * Publishes (Extract variables), every key always present (empty when not applicable):
 *     output                 all resulting pieces; pieceCount, regionCount
 *     <region>               the pieces of each region (bodies)
 *     <region>Edges          their edges except the cuts (boundary edges of a surface)
 *     outside / outsideEdges start + end;   inside / insideEdges  every middle region
 *     splitEdges             every cut, one edge per cut (the edge on the piece in front of
 *                            its tool); per tool: cut (1 tool), startCut / endCut (2),
 *                            cut1 .. cutN (3+)
 *     splitFaces             faces the splits created (the caps on solids)
 */
annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Split+",
        "Feature Type Description" : "Split parts, surfaces, curves or faces with several tools; when one side is kept, the side is the one a reference is on, whatever the tools' orientations.",
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

        annotation { "Name" : "Entities to split with",
                    "Filter" : (EntityType.BODY && BodyType.SHEET && SketchObject.NO) || EntityType.FACE || BodyType.MATE_CONNECTOR,
                    "Description" : "Surfaces, faces, planes or mate connectors. Each splits everything the ones before it left, in the order picked." }
        definition.tools is Query;

        annotation { "Name" : "Keep tools", "Default" : false }
        definition.keepTools is boolean;

        if (definition.splitType == SplitPlusType.PART)
        {
            annotation { "Name" : "Trim to face boundaries", "Default" : false }
            definition.useTrimmed is boolean;

            annotation { "Name" : "Keep both sides", "Default" : true }
            definition.keepBothSides is boolean;
        }

        if (definition.splitType == SplitPlusType.FACE || !definition.keepBothSides)
        {
            annotation { "Name" : "Side reference", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                        "Description" : "Geometry on one side of the tools. Part split keeping one side: the side of every tool to keep (empty = the built-in front / back choice). With one tool it also names the regions near / far." }
            definition.keepReference is Query;
        }

        if (definition.splitType == SplitPlusType.PART && !definition.keepBothSides)
        {
            annotation { "Name" : "Keep side nearest reference", "Default" : true, "UIHint" : UIHint.OPPOSITE_DIRECTION,
                        "Description" : "On: keep the side of each tool the reference is on. Off: keep the other side. Without a reference: on keeps each tool's front (the side its normal points to), off its back." }
            definition.keepNear is boolean;
        }

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print sides", "Default" : false, "Description" : "Which side of each tool the reference is on, and what was kept." }
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
        verifyNonemptyQuery(context, definition, "tools", ErrorStringEnum.SPLIT_SELECT_TOOL);
        const clash = faceMode ? qIntersection([definition.faceTargets, facesOf(definition.tools)])
                               : qIntersection([definition.targets, qOwnerBody(definition.tools)]);
        if (!isQueryEmpty(context, clash))
        {
            throw regenError("A tool is also a target; pick it only once.", ["tools"]);
        }

        // A face split removes nothing; there the reference only names the sides.
        const probe = (faceMode || !definition.keepBothSides) ? referenceProbe(context, definition.keepReference) : undefined;
        const tools = evaluateQuery(context, definition.tools);

        var tempPlanes = [];
        var resolvedTools = [];
        var pieces = faceMode ? qEntityFilter(definition.faceTargets, EntityType.FACE) : definition.targets;
        for (var i = 0; i < size(tools); i += 1)
        {
            var tool = tools[i];
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
            resolvedTools = append(resolvedTools, tool);

            if (faceMode)
            {
                const faceSplitId = id + ("splitFace" ~ i);
                opSplitFace(context, faceSplitId, faceSplitDefinition(context, pieces, tool, isPlane));
                // A split face loses its id and qCreatedBy holds no faces: the halves are
                // qSplitBy's two sides; faces the tool missed keep theirs.
                pieces = qUnion(evaluateQuery(context, qUnion([pieces, qSplitBy(faceSplitId, EntityType.FACE, false), qSplitBy(faceSplitId, EntityType.FACE, true)])));
                continue;
            }

            const keepType = keepTypeFor(context, definition, probe, tool, i);
            const splitId = id + ("split" ~ i);
            opSplitPart(context, splitId, {
                        "targets" : pieces,
                        "tool" : tool,
                        "keepTools" : true,
                        "useTrimmed" : definition.useTrimmed,
                        "keepType" : keepType
                    });
            pieces = qUnion([pieces, qCreatedBy(splitId, EntityType.BODY)]);
        }

        // Cuts and regions, read while the tools (and the temporary planes) still exist.
        const pieceEdges = faceMode ? qAdjacent(pieces, AdjacencyType.EDGE, EntityType.EDGE)
                                    : qOwnedByBody(qUnion(evaluateQuery(context, pieces)), EntityType.EDGE);
        const allCuts = qIntersection([pieceEdges, qCreatedBy(id, EntityType.EDGE)]);
        const splitEdges = oneEdgePerCut(context, allCuts, resolvedTools);
        const regions = classifyRegions(context, evaluateQuery(context, pieces), resolvedTools, probe);
        const cutsByTool = cutsPerTool(context, splitEdges, resolvedTools);

        if (size(tempPlanes) > 0)
        {
            opDeleteBodies(context, id + "deletePlanes", { "entities" : qUnion(tempPlanes) });
        }
        if (!definition.keepTools)
        {
            const toolBodies = qBodyType(qEntityFilter(definition.tools, EntityType.BODY), BodyType.SHEET);
            if (!isQueryEmpty(context, toolBodies))
            {
                opDeleteBodies(context, id + "deleteTools", { "entities" : toolBodies });
            }
        }

        const kept = qUnion(evaluateQuery(context, pieces));
        if (isQueryEmpty(context, kept))
        {
            throw regenError("Nothing is left: no part of the targets lies on the kept side of every tool.", ["keepReference"]);
        }
        const edgesOf = function(q is Query) returns Query
            {
                return faceMode ? faceRegionEdges(context, q, allCuts) : regionEdges(q, allCuts);
            };

        var queries = {
            "splitFaces" : extractableQuery(faceMode ? qNothing() : qIntersection([qOwnedByBody(kept, EntityType.FACE), qCreatedBy(id, EntityType.FACE)]),
                    "Faces the splits created (the caps on solids; empty in a face split).", DebugColor.MAGENTA),
            "splitEdges" : extractableQuery(splitEdges,
                    "The cuts: one edge per cut, the one on the piece in front of its tool (the side the tool's normal points to).",
                    DebugColor.MAGENTA)
        };
        const cutNames = cutKeyNames(size(resolvedTools));
        for (var k = 0; k < size(cutNames); k += 1)
        {
            queries[cutNames[k]] = extractableQuery(cutsByTool[k], "The cut made by tool " ~ (k + 1) ~ ".", DebugColor.MAGENTA);
        }
        var outside = [];
        var inside = [];
        for (var r = 0; r < size(regions.names); r += 1)
        {
            const name = regions.names[r];
            const bodies = qUnion(regions.bodies[r]);
            queries[name] = extractableQuery(bodies, "Region " ~ name ~ ": " ~ regions.descriptions[r] ~ ".", DebugColor.CYAN);
            queries[name ~ "Edges"] = extractableQuery(edgesOf(bodies),
                    "Edges of region " ~ name ~ " except the cuts (a surface's boundary edges).", DebugColor.CYAN);
            if (regions.outside[r])
            {
                outside = append(outside, bodies);
            }
            else if (regions.inside[r])
            {
                inside = append(inside, bodies);
            }
        }
        queries["outside"] = extractableQuery(qUnion(outside), "The regions beyond the first and last tools (start and end).", DebugColor.CYAN);
        queries["outsideEdges"] = extractableQuery(edgesOf(qUnion(outside)), "Edges of the outside regions except the cuts.", DebugColor.CYAN);
        queries["inside"] = extractableQuery(qUnion(inside), "Every region between the first and last tools.", DebugColor.CYAN);
        queries["insideEdges"] = extractableQuery(edgesOf(qUnion(inside)), "Edges of the inside regions except the cuts.", DebugColor.CYAN);

        if (regions.problem != undefined)
        {
            reportFeatureInfo(context, id, "Split done; regions not published: " ~ regions.problem ~ ".");
        }
        if (definition.debugPrintSides)
        {
            for (var r = 0; r < size(regions.names); r += 1)
            {
                println("[split+] region " ~ regions.names[r] ~ ": " ~ size(regions.bodies[r]) ~ " piece(s).");
            }
        }

        embedStandardOutputs(context, id, {
                    "output" : kept,
                    "outputDescription" : faceMode ? "The split faces" : "The split pieces",
                    "inputs" : qUnion([faceMode ? definition.faceTargets : definition.targets, definition.tools]),
                    "variables" : {
                        "pieceCount" : extractableVariable(size(evaluateQuery(context, kept)), "Pieces the split left (faces, in a face split)."),
                        "regionCount" : extractableVariable(size(regions.names), "Regions the tools define: 2 for one tool, N + 1 for N tools.")
                    },
                    "queries" : queries
                });
    }, {
        "splitType" : SplitPlusType.PART,
        "targets" : qNothing(),
        "faceTargets" : qNothing(),
        "keepTools" : false,
        "useTrimmed" : false,
        "keepBothSides" : true,
        "keepReference" : qNothing(),
        "keepNear" : true,
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
 * The point that stands for a piece when reading its side of a tool: a face's point nearest
 * its centroid (a curved face's centroid can lie off it, even across a tool), else the
 * body's centroid.
 */
function piecePoint(context is Context, piece is Query) returns Vector
{
    const centroid = evApproximateCentroid(context, { "entities" : piece });
    if (isQueryEmpty(context, qEntityFilter(piece, EntityType.FACE)))
    {
        return centroid;
    }
    return evDistance(context, { "side0" : centroid, "side1" : piece }).sides[1].point;
}

/**
 * The cut edges with each coincident pair reduced to one. Keeping both sides leaves every cut
 * as two edges on top of each other, one per piece, and anything built on both -- an
 * extrude, a fillet -- fails on the overlap. Of a pair, the edge kept is the one on the
 * piece in front of the tool that made it (the side the tool's normal points to).
 */
function oneEdgePerCut(context is Context, edges is Query, tools is array) returns Query
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

        const tool = nearestTool(context, middles[i], tools);
        var chosen = all[group[0]];
        for (var g in group)
        {
            if (edgeSide(context, all[g], tool) > 0 * meter)
            {
                chosen = all[g];
                break;
            }
        }
        result = append(result, chosen);
    }
    return qUnion(result);
}

/** The tool nearest a point. */
function nearestTool(context is Context, point is Vector, tools is array) returns Query
{
    var best = tools[0];
    var bestDistance = inf * meter;
    for (var tool in tools)
    {
        const d = evDistance(context, { "side0" : point, "side1" : tool }).distance;
        if (d < bestDistance)
        {
            bestDistance = d;
            best = tool;
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

/** The per-tool cut keys: cut (1 tool), startCut / endCut (2), cut1 .. cutN (3+). */
function cutKeyNames(toolCount is number) returns array
{
    if (toolCount == 1)
    {
        return ["cut"];
    }
    if (toolCount == 2)
    {
        return ["startCut", "endCut"];
    }
    var names = [];
    for (var k = 0; k < toolCount; k += 1)
    {
        names = append(names, "cut" ~ (k + 1));
    }
    return names;
}

/** The cut edges split by the tool that made each (the nearest tool). */
function cutsPerTool(context is Context, cuts is Query, tools is array) returns array
{
    var byTool = makeArray(size(tools), []);
    for (var e in evaluateQuery(context, cuts))
    {
        const middle = evEdgeTangentLine(context, { "edge" : e, "parameter" : 0.5 }).origin;
        var best = 0;
        var bestDistance = inf * meter;
        for (var k = 0; k < size(tools); k += 1)
        {
            const d = evDistance(context, { "side0" : middle, "side1" : tools[k] }).distance;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = k;
            }
        }
        byTool[best] = append(byTool[best], e);
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

/**
 * Every piece placed in a region by the side of each tool its centroid is on.
 *
 * @returns {map} : { names, descriptions, bodies (array of arrays of body queries), outside
 *      (booleans), inside (booleans) } -- one entry per region, in order along the tools.
 */
function classifyRegions(context is Context, pieces is array, tools is array, probe) returns map
{
    const n = size(tools);
    var sides = [];
    for (var piece in pieces)
    {
        const centroid = piecePoint(context, piece);
        var row = [];
        for (var k = 0; k < n; k += 1)
        {
            row = append(row, sideSign(context, centroid, tools[k]));
        }
        sides = append(sides, row);
    }

    if (n == 1)
    {
        // Two regions, named by the reference when there is one, else by the tool's normal.
        var refSide = 0;
        if (probe != undefined)
        {
            refSide = sideSign(context, probe, tools[0]);
        }
        const named = refSide != 0;
        var first = [];
        var second = [];
        for (var i = 0; i < size(pieces); i += 1)
        {
            if (sides[i][0] == 0)
            {
                return noRegions(named ? ["near", "far"] : ["front", "back"], "a piece lies on the tool, so its side cannot be read");
            }
            if (named ? sides[i][0] == refSide : sides[i][0] > 0)
            {
                first = append(first, pieces[i]);
            }
            else
            {
                second = append(second, pieces[i]);
            }
        }
        return {
                "names" : named ? ["near", "far"] : ["front", "back"],
                "descriptions" : named ? ["the side of the tool the reference is on", "the other side of the tool"]
                                       : ["the side the tool's normal points to", "the side the tool's normal points away from"],
                "bodies" : [first, second],
                "outside" : [false, false],
                "inside" : [false, false]
            };
    }

    // forward[k]: the side of tool k the next tool is on (the last: away from the one before).
    var forward = [];
    for (var k = 0; k < n; k += 1)
    {
        const other = k < n - 1 ? tools[k + 1] : tools[k - 1];
        const at = sideSign(context, evApproximateCentroid(context, { "entities" : other }), tools[k]);
        forward = append(forward, k < n - 1 ? at : -at);
    }

    var names = ["start"];
    var descriptions = ["beyond tool 1, away from tool 2"];
    for (var j = 1; j < n; j += 1)
    {
        names = append(names, n == 2 ? "middle" : "middle" ~ j);
        descriptions = append(descriptions, "between tools " ~ j ~ " and " ~ (j + 1));
    }
    names = append(names, "end");
    descriptions = append(descriptions, "beyond tool " ~ n ~ ", away from tool " ~ (n - 1));

    for (var k = 0; k < n; k += 1)
    {
        if (forward[k] == 0)
        {
            return noRegions(names, "tools " ~ (k + 1) ~ " and " ~ (k < n - 1 ? k + 2 : k) ~ " meet or cross");
        }
    }

    var bodies = makeArray(n + 1, []);
    for (var i = 0; i < size(pieces); i += 1)
    {
        // A piece in region j is forward of tools 1..j and behind tools j+1..n.
        var region = 0;
        var consistent = true;
        for (var k = 0; k < n; k += 1)
        {
            if (sides[i][k] == 0)
            {
                consistent = false;
            }
            else if (sides[i][k] == forward[k])
            {
                if (region != k)
                {
                    consistent = false;
                }
                region = k + 1;
            }
        }
        if (!consistent)
        {
            return noRegions(names, "a piece fits no band -- the tools cross inside the part, or were not picked in order along it");
        }
        bodies[region] = append(bodies[region], pieces[i]);
    }

    var outside = makeArray(n + 1, false);
    var inside = makeArray(n + 1, true);
    outside[0] = true;
    outside[n] = true;
    inside[0] = false;
    inside[n] = false;
    return { "names" : names, "descriptions" : descriptions, "bodies" : bodies, "outside" : outside, "inside" : inside };
}

/**
 * The region keys, all empty, and why: the tools do not define bands (they cross, or were
 * picked out of order). The split itself is unaffected -- crossing tools are a normal way
 * to keep a corner.
 */
function noRegions(names is array, why is string) returns map
{
    return {
            "names" : names,
            "descriptions" : makeArray(size(names), "not defined: " ~ why),
            "bodies" : makeArray(size(names), []),
            "outside" : makeArray(size(names), false),
            "inside" : makeArray(size(names), false),
            "problem" : why
        };
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

/**
 * The opSplitPart keep type for one tool. KEEP_FRONT keeps the side the tool's normal points
 * to, so with a reference the kept side is the reference's side (or the other one).
 */
function keepTypeFor(context is Context, definition is map, probe, tool is Query, index is number) returns SplitOperationKeepType
{
    if (definition.keepBothSides)
    {
        return SplitOperationKeepType.KEEP_ALL;
    }
    if (probe == undefined)
    {
        return definition.keepNear ? SplitOperationKeepType.KEEP_FRONT : SplitOperationKeepType.KEEP_BACK;
    }

    const side = sideSign(context, probe, tool);
    if (side == 0)
    {
        throw regenError("The keep side reference lies on tool " ~ (index + 1) ~ " (or no side of it can be read there); pick something clearly on the side to keep.",
            ["keepReference"]);
    }
    const keepFront = (side > 0) == definition.keepNear;
    if (definition.debugPrintSides)
    {
        println("[split+] tool " ~ (index + 1) ~ ": reference " ~ fmtMM(signedSideOf(context, probe, tool), 3)
            ~ " mm from it, on its " ~ (side > 0 ? "front" : "back") ~ "; keeping the " ~ (keepFront ? "front" : "back") ~ ".");
    }
    return keepFront ? SplitOperationKeepType.KEEP_FRONT : SplitOperationKeepType.KEEP_BACK;
}
