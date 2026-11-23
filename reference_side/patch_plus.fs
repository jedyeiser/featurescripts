FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// enums used by opSplitFace: not part of common.fs (a missing std enum shows only as a generic REGEN_ERROR in the feature)
import(path : "onshape/std/projectiontype.gen.fs", version : "3083.0");
// IMPORT: patch_plus_icon.svg (feature icon)
IconNamespace::import(path : "86f42fa364d0d4d039b50d83", version : "8c0a8d77d66f6fd1e6c8c691");

/**
 * Patch+: replace the part of a surface inside a footprint with its own offset (or another surface), joined to the rest by walls.
 *
 * "Push or pull a region". The footprint is one of: curves (one closed outline), faces (their outline, every loop: islands and
 * holes), or a tool (its intersection with the faces). The faces inside the outline are replaced by the same faces offset
 * (+ along the face normal, Flip reverses it), by a Replacement surface, or by the faces moved along the
 * projection direction; walls connect the outline on the faces to the new surface, normal to the faces or along the projection
 * direction, optionally leaning by a draft angle.
 *
 * Works on a sheet (the result is the patched sheet; the original body keeps its identity) and on a solid (a positive
 * offset adds the plug of material, a negative one subtracts it; the original solid keeps its identity).
 *
 * Build (spikes: docs/research/patch_plus_spike*.fs.txt):
 *  1. The footprint is imprinted on a COPY of the faces (opSplitFace); the region is found by a flood from the imprint
 *     (curves, tool) or by rays against the footprint faces (faces).
 *  2. One ruled wall per outline edge (a loft of two curves running well past the base and the new surface), joined into
 *     a tube with mitred corners; the tube is cut by the base and by the new surface, and those are cut by the tube.
 *  3. Sheet: knit the outside of the base, the wall and the new region. Solid: knit base patch + wall + new region into
 *     a closed shell, enclose it into a plug, add or subtract it.
 *
 * A footprint that runs off the picked faces (faces input only) takes a separate straight path (the region is moved rigidly,
 * extruded on a solid): no draft, no replacement, a planar region or Walls along the projection direction.
 *
 * Pick stability: users pick on the result, keyed to the op ids and the union order below. Never rename an op id or reorder
 * the tools of the ordinary path.
 */
/** How the footprint curves are projected onto the faces. */
export enum PatchPlusProjection
{
    annotation { "Name" : "Normal to the faces" }
    NORMAL,
    annotation { "Name" : "Along a direction" }
    DIRECTION
}

/** What the footprint is made of. */
export enum PatchPlusFootprint
{
    annotation { "Name" : "Curves" }
    CURVES,
    annotation { "Name" : "Faces" }
    FACES,
    annotation { "Name" : "Tool (intersection)" }
    TOOL
}

/** The direction the walls run. */
export enum PatchPlusWalls
{
    annotation { "Name" : "Normal to the faces" }
    NORMAL,
    annotation { "Name" : "Along the projection direction" }
    PROJECTION
}

const PATCH_DRAFT_BOUNDS = { (degree) : [-60, 0, 60], (radian) : 0 } as AngleBoundSpec;

/** Samples along each outline edge for its wall. */
const PATCH_SAMPLES = 32;
/** Two outline edges meeting closer than this are joined at that vertex. */
const PATCH_VERTEX_TOLERANCE = 1e-6 * meter;
/** A corner whose mitre would be longer than 1 / this is refused (very sharp corner with a draft). */
const PATCH_MIN_MITRE_COS = 0.15;
/** The most faces the region search walks before giving up. */
const PATCH_FLOOD_LIMIT = 600;

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Patch+", "Manipulator Change Function" : "patchPlusManipulatorChange",
        "Feature Type Description" : "Push or pull the part of a surface inside a footprint: the faces inside the outline are replaced by their own offset, joined to the rest by walls (normal or along a direction, with draft). Sheets and solids." }
export const patchPlus = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Faces to modify",
                    "Filter" : EntityType.FACE || (BodyType.SHEET && EntityType.BODY),
                    "Description" : "Faces of one sheet or solid body (or a sheet body). The footprint must lie inside them." }
        definition.baseFaces is Query;

        annotation { "Name" : "Footprint type" }
        definition.footprintType is PatchPlusFootprint;

        if (definition.footprintType == PatchPlusFootprint.CURVES)
        {
            annotation { "Name" : "Footprint curves",
                        "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE),
                        "Description" : "Sketch curves or edges forming one closed outline. Projected onto the faces; the faces inside it are replaced." }
            definition.footprint is Query;
        }
        else if (definition.footprintType == PatchPlusFootprint.FACES)
        {
            annotation { "Name" : "Footprint faces",
                        "Filter" : EntityType.FACE || (BodyType.SHEET && EntityType.BODY),
                        "Description" : "Faces (or a sheet body) whose outline is projected onto the faces. Islands and holes both count. May run off the edge of the picked faces (then: straight walls only, see the feature description)." }
            definition.footprintFaces is Query;
        }
        else
        {
            annotation { "Name" : "Footprint tool",
                        "Filter" : EntityType.FACE || EntityType.BODY && (BodyType.SHEET || BodyType.SOLID),
                        "Description" : "Surfaces or a solid that cut the picked faces; the intersection (one closed loop inside them) is the footprint. Nothing is projected." }
            definition.footprintTool is Query;
        }

        if (definition.footprintType != PatchPlusFootprint.TOOL)
        {
            annotation { "Name" : "Projection" }
            definition.projection is PatchPlusProjection;

            if (definition.projection == PatchPlusProjection.DIRECTION)
            {
                annotation { "Name" : "Projection direction", "Filter" : QueryFilterCompound.ALLOWS_DIRECTION, "MaxNumberOfPicks" : 1,
                            "Description" : "A line: its sign does not matter." }
                definition.projectionDirection is Query;
            }
        }
        annotation { "Name" : "Replacement surface",
                    "Filter" : EntityType.FACE || (BodyType.SHEET && EntityType.BODY),
                    "Description" : "Optional. A surface that goes where the region is: it can lie in front of or behind the faces, but must not cross them inside the footprint. Empty: the faces' own offset." }
        definition.replacement is Query;

        annotation { "Name" : "Offset",
                    "Description" : "How far the new surface is from the faces. Positive goes along the face normal (outward on a solid: an emboss), negative the other way (a pocket); the arrow shows positive. With a Replacement surface, + is further from the faces." }
        isLength(definition.offset, ZERO_DEFAULT_LENGTH_BOUNDS);

        annotation { "Name" : "Flip offset direction", "UIHint" : UIHint.OPPOSITE_DIRECTION,
                    "Description" : "Reverse the direction of a positive offset: emboss <-> pocket; with a Replacement surface, a positive offset moves it closer to the faces." }
        definition.flipOffset is boolean;

        annotation { "Name" : "Walls" }
        definition.walls is PatchPlusWalls;

        annotation { "Name" : "Draft angle",
                    "Description" : "Positive: the wall is wider toward the new surface; negative: narrower (the usual release draft). 0 = straight walls." }
        isAngle(definition.draft, PATCH_DRAFT_BOUNDS);
    }
    {
        verifyNoMesh(context, definition, "baseFaces");
        const offset = definition.offset;
        // a footprint tool projects nothing: the projection inputs are hidden and ignored
        const projectionMode = definition.footprintType == PatchPlusFootprint.TOOL ? PatchPlusProjection.NORMAL : definition.projection;
        const replacementFaces = qUnion([qEntityFilter(definition.replacement, EntityType.FACE),
                    qOwnedByBody(qEntityFilter(definition.replacement, EntityType.BODY), EntityType.FACE)]);
        const hasReplacement = !isQueryEmpty(context, replacementFaces);
        if (!hasReplacement && abs(offset) < 1e-9 * meter)
        {
            throw regenError("Set a non-zero offset (positive pushes the region out, negative pulls it in), or pick a replacement surface.", ["offset"]);
        }
        if (definition.walls == PatchPlusWalls.PROJECTION && projectionMode != PatchPlusProjection.DIRECTION)
        {
            throw regenError(definition.footprintType == PatchPlusFootprint.TOOL
                    ? "Walls along the projection direction need a projected footprint (curves or faces with Projection set to \"Along a direction\"): a footprint tool projects nothing."
                    : "Walls along the projection direction need the projection set to \"Along a direction\".", ["walls"]);
        }

        const baseFaces = qUnion([qEntityFilter(definition.baseFaces, EntityType.FACE),
                    qOwnedByBody(qEntityFilter(definition.baseFaces, EntityType.BODY), EntityType.FACE)]);
        const owners = evaluateQuery(context, qOwnerBody(baseFaces));
        if (size(owners) != 1)
        {
            throw regenError("Pick faces of ONE sheet or solid body.", ["baseFaces"]);
        }
        const owner = owners[0];
        const isSolid = !isQueryEmpty(context, qBodyType(owner, BodyType.SOLID));
        // only the picked type counts: the other inputs are hidden and may hold stale picks
        const footprintType = definition.footprintType;
        const footprintFaces = footprintType != PatchPlusFootprint.FACES ? qNothing() : qUnion([qEntityFilter(definition.footprintFaces, EntityType.FACE),
                    qOwnedByBody(qEntityFilter(definition.footprintFaces, EntityType.BODY), EntityType.FACE)]);
        const hasFootprintFaces = !isQueryEmpty(context, footprintFaces);
        // the faces input classifies every picked face: refuse an oversized pick before any kernel op
        if (hasFootprintFaces && size(evaluateQuery(context, baseFaces)) > PATCH_FLOOD_LIMIT)
        {
            throw regenError("Too many faces picked for a footprint made of faces (more than " ~ PATCH_FLOOD_LIMIT ~ "): pick only the faces around the footprint.", ["baseFaces"]);
        }
        const footprintCurves = footprintType != PatchPlusFootprint.CURVES ? qNothing() : qUnion([qEntityFilter(definition.footprint, EntityType.EDGE),
                    qOwnedByBody(qEntityFilter(definition.footprint, EntityType.BODY), EntityType.EDGE)]);
        const footprintTool = footprintType != PatchPlusFootprint.TOOL ? qNothing() : qUnion([qEntityFilter(definition.footprintTool, EntityType.FACE),
                    qOwnedByBody(qEntityFilter(definition.footprintTool, EntityType.BODY), EntityType.FACE)]);
        const hasFootprintTool = !isQueryEmpty(context, footprintTool);
        // Faces: their outline (the edges with exactly one picked face: every loop, holes and islands included) is projected.
        const footEdges = hasFootprintFaces ? outlineOfFaces(context, footprintFaces) : footprintCurves;
        if (isQueryEmpty(context, footEdges) && !hasFootprintTool)
        {
            throw regenError(footprintType == PatchPlusFootprint.CURVES ? "Pick the footprint curves."
                    : (footprintType == PatchPlusFootprint.FACES ? "Pick the footprint faces (a closed shell has no outline)." : "Pick the footprint tool."),
                    [footprintType == PatchPlusFootprint.CURVES ? "footprint" : (footprintType == PatchPlusFootprint.FACES ? "footprintFaces" : "footprintTool")]);
        }
        var projectionDirection = undefined;
        if (projectionMode == PatchPlusProjection.DIRECTION)
        {
            if (isQueryEmpty(context, definition.projectionDirection))
            {
                throw regenError("Pick the projection direction.", ["projectionDirection"]);
            }
            // only the line matters, not its sign: the footprint projects both ways and the walls take the sign toward the new surface
            projectionDirection = normalize(extractDirection(context, definition.projectionDirection));
        }
        const theta = definition.draft / radian;       // a number, radians
        const reach = 3 * abs(offset) + 5 * millimeter;

        const tracked = function(q is Query) returns Query
            {
                return qUnion([q, startTracking(context, q)]);
            };
        // the piece (of a query of bodies) nearest a point
        const nearest = function(pieces is Query, point is Vector) returns Query
            {
                var best = undefined;
                var bestD = inf * meter;
                for (var piece in evaluateQuery(context, pieces))
                {
                    const d = evDistance(context, { "side0" : point, "side1" : piece }).distance;
                    if (d < bestD)
                    {
                        bestD = d;
                        best = piece;
                    }
                }
                return best == undefined ? qNothing() : best;
            };
        const dropAllBut = function(dropId is Id, pieces is Query, keep is Query)
            {
                const rest = qSubtraction(pieces, keep);
                if (!isQueryEmpty(context, rest))
                {
                    opDeleteBodies(context, dropId, { "entities" : rest });
                }
            };
        const nakedCount = function(bodies is Query) returns number
            {
                return size(evaluateQuery(context, qEdgeTopologyFilter(qOwnedByBody(bodies, EntityType.EDGE), EdgeTopology.ONE_SIDED)));
            };

        // a point well inside a face (away from its boundary), its parameter and normal: the centroid's nearest point, or the
        // best of a coarse grid when that is close to the boundary (a ring's centroid lies in its hole)
        const interiorPoint = function(face is Query) returns map
            {
                const faceEdges = qAdjacent(face, AdjacencyType.EDGE, EntityType.EDGE);
                const scale = sqrt(evArea(context, { "entities" : face }));
                const centre = evApproximateCentroid(context, { "entities" : face });
                const dc = evDistance(context, { "side0" : centre, "side1" : face });
                var best = { "point" : dc.sides[1].point, "parameter" : dc.sides[1].parameter };
                var bestMargin = evDistance(context, { "side0" : best.point, "side1" : faceEdges }).distance;
                if (bestMargin < 0.05 * scale)
                {
                    var grid = [];
                    for (var i = 1; i <= 5; i += 1)
                    {
                        for (var j = 1; j <= 5; j += 1)
                        {
                            grid = append(grid, vector(i / 6, j / 6));
                        }
                    }
                    const planes = evFaceTangentPlanes(context, { "face" : face, "parameters" : grid, "returnUndefinedOutsideFace" : true });
                    for (var k = 0; k < size(grid); k += 1)
                    {
                        if (planes[k] != undefined)
                        {
                            const margin = evDistance(context, { "side0" : planes[k].origin, "side1" : faceEdges }).distance;
                            if (margin > bestMargin)
                            {
                                bestMargin = margin;
                                best = { "point" : planes[k].origin, "parameter" : grid[k] };
                            }
                        }
                    }
                }
                best.normal = evFaceTangentPlane(context, { "face" : face, "parameter" : best.parameter }).normal;
                return best;
            };        // the largest face of a body (the one to probe a piece with)
        const largestFaceOf = function(body is Query) returns Query
            {
                var largest = undefined;
                var largestArea = -1 * meter ^ 2;
                for (var candidate in evaluateQuery(context, qOwnedByBody(body, EntityType.FACE)))
                {
                    const candidateArea = evArea(context, { "entities" : candidate });
                    if (candidateArea > largestArea)
                    {
                        largestArea = candidateArea;
                        largest = candidate;
                    }
                }
                return largest;
            };

        var stage = "reading the inputs";
        var resultQuery = qNothing();
        try
        {
            // ---- 1. the footprint on a copy of the faces: the region and its outline ----
            stage = "copying the faces";
            opExtractSurface(context, id + "copy", { "faces" : baseFaces, "tangentPropagation" : false });
            const copyFaces = qOwnedByBody(qCreatedBy(id + "copy", EntityType.BODY), EntityType.FACE);
            stage = "projecting the footprint onto the faces";
            var splitDefinition = { "faceTargets" : copyFaces, "edgeTools" : footEdges, "projectionType" : ProjectionType.NORMAL_TO_TARGET };
            if (hasFootprintTool)
            {
                // the footprint is where the tool meets the faces: imprint that, nothing is projected
                splitDefinition = { "faceTargets" : copyFaces, "faceTools" : footprintTool };
            }
            else if (projectionDirection != undefined)
            {
                splitDefinition.projectionType = ProjectionType.DIRECTION;
                splitDefinition.direction = projectionDirection;
            }
            opSplitFace(context, id + "imprint", splitDefinition);
            const imprintEdges = qCreatedBy(id + "imprint", EntityType.EDGE);
            // The region: the faces inside the outline. Flood outward from the two sides of an imprint edge across the faces'
            // own edges, stopping at imprint edges; the side that closes up without reaching a free edge of the surface is
            // the inside (the other reaches the surface's boundary). Both sides grow together, so the inside, which is
            // the small one, finishes first and the rest of the surface is not walked. Works across any number of faces.
            stage = "finding the region inside the footprint";
            const imprintList = evaluateQuery(context, imprintEdges);
            if (size(imprintList) == 0)
            {
                stage = ""; throw regenError("The footprint does not land on the faces: check the projection direction, and that the footprint lies over them.", ["footprint"]);
            }
            var regionFaces = [];
            var offEdge = false;      // the footprint runs off the picked faces: the straight path below builds it
            var offEdgeMarks = [];
            if (hasFootprintFaces)
            {
                // The region: every face of the imprinted copy that lies under the footprint faces, found by a ray from a point
                // well inside it along the projection direction (or the face normal) in both senses. Works for holes, islands
                // and any number of components, which a flood from one imprint edge cannot tell apart.
                stage = "finding the region under the footprint faces";
                const copyList = evaluateQuery(context, copyFaces);
                if (size(copyList) > PATCH_FLOOD_LIMIT)
                {
                    stage = ""; throw regenError("Too many faces to classify (" ~ size(copyList) ~ " after the footprint is projected): pick fewer base faces.", ["baseFaces"]);
                }
                for (var copyFace in copyList)
                {
                    const probe = interiorPoint(copyFace);
                    const rayDirection = projectionDirection != undefined ? projectionDirection : probe.normal;
                    if (size(evRaycast(context, { "entities" : footprintFaces, "ray" : line(probe.point, rayDirection) })) > 0
                        || size(evRaycast(context, { "entities" : footprintFaces, "ray" : line(probe.point, -rayDirection) })) > 0)
                    {
                        regionFaces = append(regionFaces, copyFace);
                    }
                }
                if (size(regionFaces) == 0)
                {
                    stage = ""; throw regenError("The footprint faces do not lie over the picked faces: check the projection direction.", ["footprintFaces"]);
                }
                // a region boundary that is not an imprint edge is the edge of the picked faces: the footprint runs off them
                const regionOnly = qUnion(regionFaces);
                const runsOff = qSubtraction(outlineOfFaces(context, regionOnly), imprintEdges);
                if (!isQueryEmpty(context, runsOff))
                {
                    offEdge = true;
                    for (var offEdgeEdge in evaluateQuery(context, runsOff))
                    {
                        if (size(offEdgeMarks) < 4)
                        {
                            offEdgeMarks = append(offEdgeMarks, evEdgeTangentLines(context, { "edge" : offEdgeEdge, "parameters" : [0.5] })[0].origin);
                        }
                    }
                }
            }
            else
            {
                const sideFaces = evaluateQuery(context, qAdjacent(imprintList[0], AdjacencyType.EDGE, EntityType.FACE));
                if (size(sideFaces) < 2)
                {
                    stage = ""; throw regenError("The footprint does not enclose a region inside the faces: it must be one closed outline that lies inside them.", ["footprint"]);
                }
                // One flood per side of an imprint edge. A flood's state: "open" (still walking), "closed" (ran out of faces
                // inside the outline: the inside) or "boundary" (met a free edge of the surface: the outside).
                const floodStep = function(flood is map) returns map
                    {
                        if (size(flood.queue) == 0)
                        {
                            flood.state = "closed";
                            return flood;
                        }
                        const current = flood.queue[0];
                        flood.queue = subArray(flood.queue, 1);
                        // the face's edges that are not the outline: a free one means the outside; the others lead to neighbours
                        const crossable = qSubtraction(qAdjacent(current, AdjacencyType.EDGE, EntityType.EDGE), imprintEdges);
                        if (!isQueryEmpty(context, qEdgeTopologyFilter(crossable, EdgeTopology.ONE_SIDED)))
                        {
                            flood.state = "boundary";
                            return flood;
                        }
                        for (var other in evaluateQuery(context, qSubtraction(qAdjacent(crossable, AdjacencyType.EDGE, EntityType.FACE), current)))
                        {
                            const key = transientQueriesToStrings(other);
                            if (flood.seen[key] == undefined)
                            {
                                flood.seen[key] = true;
                                flood.faces = append(flood.faces, other);
                                flood.queue = append(flood.queue, other);
                            }
                        }
                        return flood;
                    };
                var floods = [];
                for (var s = 0; s < 2; s += 1)
                {
                    var seen0 = {};
                    seen0[transientQueriesToStrings(sideFaces[s])] = true;
                    floods = append(floods, { "faces" : [sideFaces[s]], "queue" : [sideFaces[s]], "seen" : seen0, "state" : "open" });
                }
                // both sides grow together, so the inside (the small one) finishes first and the rest of the surface is not walked
                var floodSteps = 0;
                while (floodSteps < PATCH_FLOOD_LIMIT && floods[0].state != "closed" && floods[1].state != "closed"
                       && (floods[0].state == "open" || floods[1].state == "open"))
                {
                    floodSteps += 1;
                    for (var s = 0; s < 2; s += 1)
                    {
                        if (floods[s].state == "open")
                        {
                            floods[s] = floodStep(floods[s]);
                        }
                    }
                }
                const insideSide = floods[0].state == "closed" ? 0 : (floods[1].state == "closed" ? 1 : -1);
                if (insideSide < 0)
                {
                    stage = ""; throw regenError("The footprint does not enclose a region inside the faces: its outline must close up and stay inside the surface (a footprint reaching the edge of the picked faces needs Footprint faces; or the surface is too large to follow). Check the projection direction.", ["footprint"]);
                }
                regionFaces = floods[insideSide].faces;
            }
            const region = qUnion(regionFaces);
            const regionList = evaluateQuery(context, region);
            const underRegion = function(p is Vector) returns boolean
                {
                    return evDistance(context, { "side0" : p, "side1" : region }).distance < 1e-5 * meter;
                };
            // the pieces (bodies) whose interior lies under the region (faces input: classified, not nearest)
            const piecesUnderRegion = function(pieces is Query) returns Query
                {
                    var keep = [];
                    for (var piece in evaluateQuery(context, pieces))
                    {
                        if (underRegion(interiorPoint(largestFaceOf(piece)).point))
                        {
                            keep = append(keep, piece);
                        }
                    }
                    return qUnion(keep);
                };
            // cut a body by each tube body in turn (several loops = several tubes). The first cut keeps the op id `baseName` of the
            // one-loop build (picks on the result are keyed to it), later ones get the index. `followed` tracks the pieces.
            const splitByTubes = function(baseName is string, first is Query, followed is Query, tubeList is array)
                {
                    for (var tk = 0; tk < size(tubeList); tk += 1)
                    {
                        opSplitPart(context, id + (tk == 0 ? baseName : baseName ~ tk),
                                { "targets" : tk == 0 ? first : qBodyType(followed, BodyType.SHEET), "tool" : tubeList[tk], "keepTools" : true });
                    }
                };
            const centroid = evApproximateCentroid(context, { "entities" : region });
            const dIn = evDistance(context, { "side0" : centroid, "side1" : region });
            const pIn = dIn.sides[1].point;
            const regionNormal = evFaceTangentPlane(context, { "face" : regionList[dIn.sides[1].index], "parameter" : dIn.sides[1].parameter }).normal;
            // Which side of the faces the new surface is on (side = +1: along the face normal). A POSITIVE offset goes along the face
            // normal (outward on a solid: an emboss; a negative offset is a pocket); Flip reverses that. With a replacement surface
            // the side is found by rays below (a ray from the region along the normal or the projection direction); Flip then reverses
            // the offset applied to the replacement (replacementOffset).
            const positiveSide = definition.flipOffset ? -1 : 1;
            var side = (offset > 0 * meter ? 1 : -1) * positiveSide;
            var replacementHit = undefined;
            var hitDirection = regionNormal;
            var replacementSign = 1;
            if (hasReplacement)
            {
                stage = "locating the replacement surface";
                const castDirection = definition.walls == PatchPlusWalls.PROJECTION ? projectionDirection : regionNormal;
                const hitsFront = evRaycast(context, { "entities" : replacementFaces, "ray" : line(pIn, castDirection) });
                const hitsBack = evRaycast(context, { "entities" : replacementFaces, "ray" : line(pIn, -castDirection) });
                if (size(hitsFront) == 0 && size(hitsBack) == 0)
                {
                    stage = ""; throw regenError("The replacement surface is not in front of or behind the region along the " ~ (definition.walls == PatchPlusWalls.PROJECTION ? "projection direction" : "face normal") ~ ".", ["replacement"]);
                }
                const front = size(hitsFront) > 0 && (size(hitsBack) == 0 || hitsFront[0].distance <= hitsBack[0].distance);
                replacementHit = front ? hitsFront[0] : hitsBack[0];
                hitDirection = front ? castDirection : -castDirection;
                side = dot(hitDirection, regionNormal) >= 0 ? 1 : -1;
                // a positive offset moves the replacement FURTHER from the faces: along its own normal when that points away from them
                const hitNormal = evFaceTangentPlane(context, { "face" : replacementHit.entity, "parameter" : replacementHit.parameter }).normal;
                replacementSign = dot(hitNormal, hitDirection) >= 0 ? 1 : -1;
            }
            // the direction toward the new surface at the region
            const toward = regionNormal * side;

            // ---- the arrow: the direction of a POSITIVE offset, in every mode; clicking it flips the offset ----
            // Unflipped, it is the face normal (the projection direction on its side when the faces move along it), or from the
            // faces toward the replacement surface (positive = further from the faces).
            var unflippedDirection = regionNormal;
            if (hasReplacement)
            {
                unflippedDirection = hitDirection;
            }
            else if (definition.walls == PatchPlusWalls.PROJECTION)
            {
                unflippedDirection = dot(projectionDirection, regionNormal) >= 0 ? projectionDirection : -projectionDirection;
            }
            addManipulators(context, id, { "offsetFlip" : flipManipulator({ "base" : pIn, "direction" : unflippedDirection, "flipped" : definition.flipOffset }) });            // walls along the projection direction: the faces are MOVED by the offset along that direction (exact, and
            // free of the kernel offset that fails on patchworks of small freeform faces); otherwise they are offset along the normal
            const moveAlongDirection = definition.walls == PatchPlusWalls.PROJECTION;
            const uIn = moveAlongDirection ? (dot(projectionDirection, toward) >= 0 ? projectionDirection : -projectionDirection) : toward;

            // ---- 1b. the footprint runs off the picked faces: a straight move of the region ----
            if (offEdge)
            {
                for (var mark in offEdgeMarks)
                {
                    addDebugPoint(context, mark, DebugColor.CYAN);
                }
                stage = "";
                if (hasReplacement)
                {
                    throw regenError("A footprint that runs off the edge of the picked faces (marked) cannot be combined with a replacement surface yet: keep the footprint inside the faces, or remove the replacement.", ["footprintFaces"]);
                }
                if (abs(theta) > 1e-9)
                {
                    throw regenError("A footprint that runs off the edge of the picked faces (marked) cannot be drafted yet: set the draft angle to 0, or keep the footprint inside the faces.", ["draft"]);
                }
                // the whole region moves rigidly by one vector: the same direction for every face, so it must be planar and parallel
                // to the first one, unless the walls run along the projection direction (then the direction is the projection)
                if (!moveAlongDirection)
                {
                    for (var regionFace in regionList)
                    {
                        const plane = try silent(evPlane(context, { "face" : regionFace }));
                        const faceNormal = evFaceTangentPlane(context, { "face" : regionFace, "parameter" : vector(0.5, 0.5) }).normal;
                        if (plane == undefined || dot(faceNormal, regionNormal) < 1 - 1e-6)
                        {
                            stage = ""; throw regenError("A footprint that runs off the edge of the picked faces (marked) needs a planar region, or Walls set to along the projection direction: the region is moved as a whole.", ["walls"]);
                        }
                    }
                }
                const shift = uIn * abs(offset);
                if (isSolid)
                {
                    stage = "extruding the region";
                    opExtrude(context, id + "boss", { "entities" : region, "direction" : uIn, "endBound" : BoundingType.BLIND, "endDepth" : abs(offset) });
                    stage = side > 0 ? "adding the region to the solid" : "cutting the region from the solid";
                    if (side > 0)
                    {
                        opBoolean(context, id + "apply", { "tools" : qUnion([owner, qCreatedBy(id + "boss", EntityType.BODY)]), "operationType" : BooleanOperationType.UNION });
                    }
                    else
                    {
                        opBoolean(context, id + "apply", { "tools" : qCreatedBy(id + "boss", EntityType.BODY), "targets" : owner, "operationType" : BooleanOperationType.SUBTRACTION });
                    }
                    resultQuery = owner;
                }
                else
                {
                    // a sheet: the faces outside the region + the region moved + the walls between them. All of the sheet must be picked.
                    if (size(evaluateQuery(context, qOwnedByBody(owner, EntityType.FACE))) != size(evaluateQuery(context, baseFaces)))
                    {
                        stage = ""; throw regenError("A footprint that runs off the edge of a sheet (marked) needs all of the sheet's faces picked.", ["baseFaces"]);
                    }
                    stage = "moving the region";
                    opExtractSurface(context, id + "moved", { "faces" : region, "tangentPropagation" : false });
                    opTransform(context, id + "movedMove", { "bodies" : qCreatedBy(id + "moved", EntityType.BODY), "transform" : transform(shift) });
                    stage = "building the walls";
                    const outlineQuery = qIntersection([qAdjacent(region, AdjacencyType.EDGE, EntityType.EDGE), imprintEdges]);
                    opExtrude(context, id + "straightWall", { "entities" : outlineQuery, "direction" : uIn, "endBound" : BoundingType.BLIND, "endDepth" : abs(offset),
                                "bodyType" : ToolBodyType.SURFACE });
                    stage = "cutting the region out of the sheet";
                    // the sheet itself is cut (so that it stays the result body): the same footprint imprinted on it, its faces
                    // under the region deleted
                    var ownerSplit = splitDefinition;
                    ownerSplit.faceTargets = qOwnedByBody(owner, EntityType.FACE);
                    opSplitFace(context, id + "imprintOwner", ownerSplit);
                    var ownerRegion = [];
                    for (var ownerFace in evaluateQuery(context, qOwnedByBody(owner, EntityType.FACE)))
                    {
                        if (underRegion(interiorPoint(ownerFace).point))
                        {
                            ownerRegion = append(ownerRegion, ownerFace);
                        }
                    }
                    if (size(ownerRegion) == 0)
                    {
                        stage = ""; throw regenError("The footprint region was not found on the sheet.", ["footprintFaces"]);
                    }
                    // deleting the region can split the sheet into several bodies (a footprint across it): follow every piece
                    const ownerFollowed = tracked(owner);
                    opDeleteFace(context, id + "cutRegion", { "deleteFaces" : qUnion(ownerRegion), "includeFillet" : false, "capVoid" : false, "leaveOpen" : true });
                    stage = "joining the pieces";
                    const knitList = qUnion([owner, qBodyType(ownerFollowed, BodyType.SHEET), qCreatedBy(id + "straightWall", EntityType.BODY), qCreatedBy(id + "moved", EntityType.BODY)]);
                    const followedStraight = tracked(knitList);
                    opBoolean(context, id + "knit", { "tools" : knitList, "operationType" : BooleanOperationType.UNION });
                    resultQuery = qBodyType(followedStraight, BodyType.SHEET);
                    if (size(evaluateQuery(context, resultQuery)) != 1)
                    {
                        stage = ""; throw regenError("The moved region did not join the sheet into one body.", ["offset"]);
                    }
                }
                stage = "cleaning up";
                const strayBodies = qSubtraction(qCreatedBy(id, EntityType.BODY), resultQuery);
                if (!isQueryEmpty(context, strayBodies))
                {
                    opDeleteBodies(context, id + "cleanup", { "entities" : strayBodies });
                }
                return;
            }

            // ---- 2. the outline edges, sampled ----
            stage = "sampling the outline";
            const outlineEdges = evaluateQuery(context, qIntersection([qAdjacent(region, AdjacencyType.EDGE, EntityType.EDGE), imprintEdges]));
            if (!hasFootprintFaces && size(outlineEdges) != size(imprintList))
            {
                stage = ""; throw regenError("The footprint curves must form ONE closed outline (some of the projected curves are not on the boundary of the region). Use Footprint faces for several islands or for holes.", ["footprint"]);
            }
            var edges = [];
            for (var edge in outlineEdges)
            {
                var params = [];
                for (var i = 0; i <= PATCH_SAMPLES; i += 1)
                {
                    params = append(params, i / PATCH_SAMPLES);
                }
                const lines = evEdgeTangentLines(context, { "edge" : edge, "parameters" : params });
                var ps = [];
                var us = [];
                var cs = [];
                var ts = [];
                // the region face under each sample and the face normal there: one evFaceTangentPlanes call per face, not per sample
                var hits = [];
                for (var i = 0; i <= PATCH_SAMPLES; i += 1)
                {
                    hits = append(hits, evDistance(context, { "side0" : lines[i].origin, "side1" : region }).sides[1]);
                }
                var faceNormals = makeArray(PATCH_SAMPLES + 1);
                for (var fi = 0; fi < size(regionList); fi += 1)
                {
                    var onFace = [];
                    var onFaceParameters = [];
                    for (var i = 0; i <= PATCH_SAMPLES; i += 1)
                    {
                        if (hits[i].index == fi)
                        {
                            onFace = append(onFace, i);
                            onFaceParameters = append(onFaceParameters, hits[i].parameter);
                        }
                    }
                    if (size(onFace) > 0)
                    {
                        const planes = evFaceTangentPlanes(context, { "face" : regionList[fi], "parameters" : onFaceParameters });
                        for (var n = 0; n < size(onFace); n += 1)
                        {
                            faceNormals[onFace[n]] = planes[n].normal;
                        }
                    }
                }
                for (var i = 0; i <= PATCH_SAMPLES; i += 1)
                {
                    var nrm = faceNormals[i] * side;
                    ps = append(ps, lines[i].origin);
                    ts = append(ts, lines[i].direction);
                    // the wall's base direction at this point: the normal toward the offset, or the projection direction
                    var u = nrm;
                    if (definition.walls == PatchPlusWalls.PROJECTION)
                    {
                        u = dot(projectionDirection, nrm) >= 0 ? projectionDirection : -projectionDirection;
                    }
                    us = append(us, u);
                }
                const closed = norm(ps[PATCH_SAMPLES] - ps[0]) < PATCH_VERTEX_TOLERANCE;
                if (closed)
                {
                    ps[PATCH_SAMPLES] = ps[0];
                }
                // the outward conormal: along tangent x normal, turned round when a step that way stays on the region
                const im = floor(PATCH_SAMPLES / 2);
                const guess = normalize(cross(ts[im], us[im]));
                const stepOut = evDistance(context, { "side0" : ps[im] + guess * 0.5 * millimeter, "side1" : region }).distance;
                const stepIn = evDistance(context, { "side0" : ps[im] - guess * 0.5 * millimeter, "side1" : region }).distance;
                for (var i = 0; i <= PATCH_SAMPLES; i += 1)
                {
                    var c = normalize(cross(ts[i], us[i])) * (stepOut < stepIn ? -1 : 1);
                    // lean sideways only: perpendicular to the wall's base direction
                    c = normalize(c - us[i] * dot(c, us[i]));
                    cs = append(cs, c);
                }
                edges = append(edges, { "ps" : ps, "us" : us, "cs" : cs, "ts" : ts, "closed" : closed,
                        // mitre corrections at the start and the end, blended along the edge when the walls are built
                        "dc" : [vector(0, 0, 0), vector(0, 0, 0)], "du" : [vector(0, 0, 0), vector(0, 0, 0)] });
            }
            const nE = size(edges);

            // ---- 3. corners: where two outline edges meet, both walls share one mitred end curve ----
            stage = "joining the walls at the corners";
            for (var i = 0; i < nE; i += 1)
            {
                if (edges[i].closed)
                {
                    continue;
                }
                for (var a = 0; a < 2; a += 1)
                {
                    const ia = a == 0 ? 0 : PATCH_SAMPLES;
                    const pa = edges[i].ps[ia];
                    var partner = undefined;
                    var matches = 0;
                    for (var j = 0; j < nE; j += 1)
                    {
                        if (j == i || edges[j].closed)
                        {
                            continue;
                        }
                        for (var b = 0; b < 2; b += 1)
                        {
                            if (norm(pa - edges[j].ps[b == 0 ? 0 : PATCH_SAMPLES]) < PATCH_VERTEX_TOLERANCE)
                            {
                                partner = { "j" : j, "b" : b, "ib" : b == 0 ? 0 : PATCH_SAMPLES };
                                matches += 1;
                            }
                        }
                    }
                    if (matches != 1)
                    {
                        addDebugPoint(context, pa, DebugColor.RED);
                        stage = ""; throw regenError("The footprint outline is not a simple closed loop at the marked point (it must close up, with two curves meeting at each corner).", ["footprint"]);
                    }
                    const ci = edges[i].cs[ia];
                    const cj = edges[partner.j].cs[partner.ib];
                    const along = 1 + dot(ci, cj);
                    if (along < PATCH_MIN_MITRE_COS)
                    {
                        addDebugPoint(context, pa, DebugColor.RED);
                        stage = ""; throw regenError("The corner at the marked point is too sharp for the wall draft: reduce the draft angle or round the corner.", ["draft"]);
                    }
                    // the mitre conormal: its component along each edge's own conormal is 1. Only the CORRECTION is stored
                    // (cm - own conormal runs along the edge's own wall plane); the walls blend it linearly along the edge,
                    // so the sampled curves stay monotone (moving just the last sample doubles the curve back at full reach).
                    const cm = (ci + cj) / along;
                    const um = normalize(edges[i].us[ia] + edges[partner.j].us[partner.ib]);
                    var ei = edges[i];
                    ei.dc[a] = cm - ci;
                    ei.du[a] = um - edges[i].us[ia];
                    edges[i] = ei;
                    var ej = edges[partner.j];
                    ej.dc[partner.b] = cm - cj;
                    ej.du[partner.b] = um - edges[partner.j].us[partner.ib];
                    edges[partner.j] = ej;
                }
            }

            // ---- 4. the walls: one ruled sheet per edge, joined into one tube ----
            // With a replacement surface the walls must reach it everywhere along the outline: measure the distance by rays
            // along the wall direction, and run the walls 1.5 x past the farthest hit (on both sides of the faces).
            var wallReach = reach;
            if (hasReplacement)
            {
                stage = "measuring the distance to the replacement surface";
                var farthest = replacementHit.distance;
                for (var i = 0; i < nE; i += 1)
                {
                    for (var k = 0; k <= PATCH_SAMPLES; k += 4)
                    {
                        // both ways along the wall: where the new surface crosses the faces it is behind them for part of the outline
                        const hitsAhead = evRaycast(context, { "entities" : replacementFaces, "ray" : line(edges[i].ps[k], edges[i].us[k]) });
                        const hitsBehind = evRaycast(context, { "entities" : replacementFaces, "ray" : line(edges[i].ps[k], -edges[i].us[k]) });
                        var hitDistance = inf * meter;
                        if (size(hitsAhead) > 0)
                        {
                            hitDistance = min(hitDistance, hitsAhead[0].distance);
                        }
                        if (size(hitsBehind) > 0)
                        {
                            hitDistance = min(hitDistance, hitsBehind[0].distance);
                        }
                        if (hitDistance == inf * meter)
                        {
                            addDebugPoint(context, edges[i].ps[k], DebugColor.RED);
                            stage = ""; throw regenError("The replacement surface does not cover the whole outline along the wall direction (marked): it must lie in front of (or behind) every part of the footprint.", ["replacement"]);
                        }
                        farthest = max(farthest, hitDistance);
                    }
                }
                wallReach = 1.5 * (farthest + abs(offset)) + 5 * millimeter;
            }
            stage = "building the walls";
            var walls = [];
            var wires = [];
            for (var i = 0; i < nE; i += 1)
            {
                const e = edges[i];
                var lo = [];
                var hi = [];
                for (var k = 0; k <= PATCH_SAMPLES; k += 1)
                {
                    const s = k / PATCH_SAMPLES;
                    const uk = e.us[k] + e.du[0] * (1 - s) + e.du[1] * s;
                    const ck = e.cs[k] + e.dc[0] * (1 - s) + e.dc[1] * s;
                    const d = uk * cos(theta * radian) + ck * sin(theta * radian);
                    lo = append(lo, e.ps[k] - d * wallReach);
                    hi = append(hi, e.ps[k] + d * wallReach);
                }
                const wid = id + ("wall" ~ i);
                opFitSpline(context, wid + "lo", { "points" : lo });
                opFitSpline(context, wid + "hi", { "points" : hi });
                opLoft(context, wid + "loft", { "profileSubqueries" : [qCreatedBy(wid + "lo", EntityType.EDGE), qCreatedBy(wid + "hi", EntityType.EDGE)],
                            "bodyType" : ToolBodyType.SURFACE });
                wires = append(wires, qCreatedBy(wid + "lo", EntityType.BODY));
                wires = append(wires, qCreatedBy(wid + "hi", EntityType.BODY));
                walls = append(walls, qCreatedBy(wid + "loft", EntityType.BODY));
            }
            opDeleteBodies(context, id + "delcurves", { "entities" : qUnion(wires) });
            var tubeBodies = qUnion(walls);
            if (nE > 1)
            {
                const followTube = tracked(qUnion(walls));
                opBoolean(context, id + "tubeJoin", { "tools" : qUnion(walls), "operationType" : BooleanOperationType.UNION });
                tubeBodies = qBodyType(followTube, BodyType.SHEET);
            }
            // a copy of the whole tube cuts the base and the offset surface; the original is cut down to the wall
            opExtractSurface(context, id + "tubeCopy", { "faces" : qOwnedByBody(tubeBodies, EntityType.FACE) });
            const tubeFull = qCreatedBy(id + "tubeCopy", EntityType.BODY);
            const tube = tracked(tubeBodies);
            // the tube's free rims (frozen now, then tracked through the cuts): a piece that touches one lies beyond the faces
            // or beyond the new surface; the pieces that do not are the wall, however many the cuts make (several where the
            // new surface crosses the faces along the outline)
            const rimNow = qUnion(evaluateQuery(context, qEdgeTopologyFilter(qOwnedByBody(tubeBodies, EntityType.EDGE), EdgeTopology.ONE_SIDED)));
            const rimEdges = qUnion([rimNow, startTracking(context, rimNow)]);

            // ---- 5. the new surface: the replacement (offset), or the same faces offset ----
            stage = hasReplacement ? "preparing the replacement surface (if the kernel cannot offset it, set Walls to along the projection direction: it is moved instead)"
                : "offsetting the faces (if the kernel cannot offset this surface, set Walls to along the projection direction: the faces are moved instead)";
            var replacementPoint = pIn;
            if (hasReplacement)
            {
                // + moves the replacement further from the faces; Flip reverses that (the one meaning of Flip in every mode)
                const replacementOffset = definition.flipOffset ? -offset : offset;
                const shifted = abs(offset) > 1e-9 * meter;
                var extractDefinition = { "faces" : replacementFaces, "tangentPropagation" : false };
                if (shifted && !moveAlongDirection)
                {
                    extractDefinition.offset = replacementOffset * replacementSign;
                    extractDefinition.useFacesAroundToTrimOffset = false;
                }
                opExtractSurface(context, id + "offset", extractDefinition);
                if (shifted && moveAlongDirection)
                {
                    opTransform(context, id + "offsetMove", { "bodies" : qCreatedBy(id + "offset", EntityType.BODY), "transform" : transform(uIn * replacementOffset) });
                }
                replacementPoint = replacementHit.intersection + hitDirection * replacementOffset;
            }
            else if (moveAlongDirection)
            {
                opExtractSurface(context, id + "offset", { "faces" : baseFaces, "tangentPropagation" : false });
                opTransform(context, id + "offsetMove", { "bodies" : qCreatedBy(id + "offset", EntityType.BODY), "transform" : transform(uIn * abs(offset)) });
                replacementPoint = pIn + uIn * abs(offset);
            }
            else
            {
                // offset only the faces near the region (within the walls' reach), not the whole surface: a kernel offset of a
                // large patchwork of small freeform faces fails, and the offset surface only has to cover the walls' crossing
                var nearFaces = [];
                for (var face in evaluateQuery(context, baseFaces))
                {
                    if (evDistance(context, { "side0" : face, "side1" : region }).distance < reach)
                    {
                        nearFaces = append(nearFaces, face);
                    }
                }
                opExtractSurface(context, id + "offset", { "faces" : qUnion(nearFaces), "offset" : abs(offset) * side, "tangentPropagation" : false, "useFacesAroundToTrimOffset" : false });
                replacementPoint = pIn + regionNormal * (abs(offset) * side);
            }
            const replacement = qCreatedBy(id + "offset", EntityType.BODY);
            // the base's surface, to cut the tube: the sheet itself, or a copy of the PICKED faces of the solid (not all
            // its faces: the walls reach well past the surface and meet the solid's other faces, and cutting a large
            // multi-face shell with the tube fails with SPLIT_FAILED on a real part)
            var baseSurface = owner;
            if (isSolid)
            {
                opExtractSurface(context, id + "baseSurface", { "faces" : baseFaces, "tangentPropagation" : false });
                baseSurface = qCreatedBy(id + "baseSurface", EntityType.BODY);
            }

            // ---- 6. mutual trim ----
            stage = "cutting the walls to the base and the offset surface";
            opSplitPart(context, id + "tubeByBase", { "targets" : tube, "tool" : baseSurface, "keepTools" : true });
            opSplitPart(context, id + "tubeByOffset", { "targets" : tube, "tool" : replacement, "keepTools" : true });
            const wallPieces = qBodyType(tube, BodyType.SHEET);
            if (isQueryEmpty(context, wallPieces))
            {
                stage = ""; throw regenError("The walls do not cross the faces and the offset surface: check the projection direction and the offset.", ["offset"]);
            }
            var wallParts = [];
            for (var piece in evaluateQuery(context, wallPieces))
            {
                if (isQueryEmpty(context, qIntersection([qOwnedByBody(piece, EntityType.EDGE), rimEdges])))
                {
                    wallParts = append(wallParts, piece);
                }
            }
            if (size(wallParts) == 0)
            {
                stage = ""; throw regenError("No part of the walls lies between the faces and the new surface: check the projection direction, the offset and the replacement surface.", ["offset"]);
            }
            const wall = qUnion(wallParts);
            dropAllBut(id + "dropWall", wallPieces, wall);
            stage = "cutting the offset surface to the outline (a creased or patchwork surface does not offset cleanly along its normals: set Walls to along the projection direction, which moves the faces instead)";
            const replTracked = tracked(replacement);
            // one cut per tube body (several loops = several tubes); the first keeps the op id of the one-loop build
            const tubeList = evaluateQuery(context, tubeFull);
            splitByTubes("offsetByTube", replacement, replTracked, tubeList);
            var replInside = qNothing();
            if (!hasFootprintFaces)
            {
                replInside = nearest(qBodyType(replTracked, BodyType.SHEET), replacementPoint);
                if (isQueryEmpty(context, replInside))
                {
                    stage = ""; throw regenError("The new surface does not lie over the region: check the offset and the projection direction.", ["offset"]);
                }
            }
            else
            {
                // the pieces of the new surface that lie over the region: a ray from a point well inside the piece, along the
                // wall direction (or the piece's normal), both ways, reaches the region within the walls' reach
                var replKeep = [];
                for (var replPiece in evaluateQuery(context, qBodyType(replTracked, BodyType.SHEET)))
                {
                    const replProbe = interiorPoint(largestFaceOf(replPiece));
                    const replDirection = definition.walls == PatchPlusWalls.PROJECTION ? projectionDirection : replProbe.normal;
                    const hitsAhead = evRaycast(context, { "entities" : region, "ray" : line(replProbe.point, replDirection) });
                    const hitsBehind = evRaycast(context, { "entities" : region, "ray" : line(replProbe.point, -replDirection) });
                    if ((size(hitsAhead) > 0 && hitsAhead[0].distance < 2 * wallReach) || (size(hitsBehind) > 0 && hitsBehind[0].distance < 2 * wallReach))
                    {
                        replKeep = append(replKeep, replPiece);
                    }
                }
                if (size(replKeep) == 0)
                {
                    stage = ""; throw regenError("The new surface does not lie over the region: check the offset and the projection direction.", ["offset"]);
                }
                replInside = qUnion(replKeep);
            }
            dropAllBut(id + "dropOffset", qBodyType(replTracked, BodyType.SHEET), replInside);

            if (!isSolid)
            {
                // ---- 7a. sheet: cut the hole in the base, knit [outside of the base first, wall, offset region] ----
                stage = "cutting the hole in the sheet";
                const baseTracked = tracked(owner);
                splitByTubes("baseByTube", owner, baseTracked, tubeList);
                const pieces = qBodyType(baseTracked, BodyType.SHEET);
                const inside = hasFootprintFaces ? piecesUnderRegion(pieces) : nearest(pieces, pIn);
                if (isQueryEmpty(context, inside))
                {
                    stage = ""; throw regenError("No part of the sheet lies under the footprint region.", ["footprint"]);
                }
                const outside = qSubtraction(pieces, inside);
                if (isQueryEmpty(context, outside))
                {
                    stage = ""; throw regenError("The footprint covers the whole sheet: nothing is left to join the patch to.", ["footprint"]);
                }
                const outsideList = evaluateQuery(context, outside);
                opDeleteBodies(context, id + "dropInside", { "entities" : inside });
                stage = "joining the patch to the sheet";
                const knitTools = qUnion(concatenateArrays([outsideList, [wall, replInside]]));
                const followed = tracked(knitTools);
                opBoolean(context, id + "knit", { "tools" : knitTools, "operationType" : BooleanOperationType.UNION });
                resultQuery = qBodyType(followed, BodyType.SHEET);
                if (size(evaluateQuery(context, resultQuery)) != 1)
                {
                    stage = ""; throw regenError("The patch did not join the sheet into one body (the wall may not meet the faces cleanly there).", ["offset"]);
                }
            }
            else
            {
                // ---- 7b. solid: patch + wall + offset region -> closed shell -> plug; add or subtract ----
                stage = "cutting the base surface to the outline";
                const surfTracked = tracked(baseSurface);
                splitByTubes("surfByTube", baseSurface, surfTracked, tubeList);
                const pieces = qBodyType(surfTracked, BodyType.SHEET);
                const patch = hasFootprintFaces ? piecesUnderRegion(pieces) : nearest(pieces, pIn);
                if (isQueryEmpty(context, patch))
                {
                    stage = ""; throw regenError("No part of the base surface lies under the footprint region.", ["footprint"]);
                }
                dropAllBut(id + "dropSurf", pieces, patch);
                stage = "closing the plug";
                // Where the new surface crosses the base patch inside the outline, the two sheets intersect each other and
                // cannot be joined as they are (BOOLEAN_INVALID): cut each by a copy of the other, so they share the crossing curve.
                var patchList = evaluateQuery(context, patch);
                var replList = evaluateQuery(context, replInside);
                var shellPieces = concatenateArrays([patchList, replList]);
                var patchPieceCount = size(patchList);
                if (size(evCollision(context, { "tools" : patch, "targets" : replInside })) > 0)
                {
                    stage = "cutting the base patch and the new surface at their crossing";
                    opExtractSurface(context, id + "crossPatch", { "faces" : qOwnedByBody(patch, EntityType.FACE), "tangentPropagation" : false });
                    opExtractSurface(context, id + "crossRepl", { "faces" : qOwnedByBody(replInside, EntityType.FACE), "tangentPropagation" : false });
                    const patchFollow = tracked(patch);
                    const replFollow = tracked(replInside);
                    try silent
                    {
                        opSplitPart(context, id + "patchByRepl", { "targets" : patch, "tool" : qCreatedBy(id + "crossRepl", EntityType.BODY), "keepTools" : true });
                        opSplitPart(context, id + "replByPatch", { "targets" : replInside, "tool" : qCreatedBy(id + "crossPatch", EntityType.BODY), "keepTools" : true });
                    }
                    const patchPieceList = evaluateQuery(context, qBodyType(patchFollow, BodyType.SHEET));
                    patchPieceCount = size(patchPieceList);
                    shellPieces = concatenateArrays([patchPieceList, evaluateQuery(context, qBodyType(replFollow, BodyType.SHEET))]);
                    opDeleteBodies(context, id + "dropCross", { "entities" : qUnion([qCreatedBy(id + "crossPatch", EntityType.BODY), qCreatedBy(id + "crossRepl", EntityType.BODY)]) });
                    stage = "closing the plug";
                }
                // The pieces form one closed shell per lens: where the new surface crosses the base there are several lenses
                // that touch along the crossing curve (four sheets at one edge: not joinable as one body), so each wall part
                // is grouped with the pieces its free edges lie on, walls sharing a piece are one lens, and each lens is
                // closed and enclosed on its own.
                const nW = size(wallParts);
                const nP = size(shellPieces);
                var attachedTo = [];
                for (var w = 0; w < nW; w += 1)
                {
                    var attached = [];
                    for (var freeEdge in evaluateQuery(context, qEdgeTopologyFilter(qOwnedByBody(wallParts[w], EntityType.EDGE), EdgeTopology.ONE_SIDED)))
                    {
                        const mid = evEdgeTangentLines(context, { "edge" : freeEdge, "parameters" : [0.5] })[0].origin;
                        for (var p = 0; p < nP; p += 1)
                        {
                            if (evDistance(context, { "side0" : mid, "side1" : shellPieces[p] }).distance < 1e-5 * meter)
                            {
                                if (!isIn(p, attached))
                                {
                                    attached = append(attached, p);
                                }
                            }
                        }
                    }
                    attachedTo = append(attachedTo, attached);
                }
                var lensOf = [];
                for (var w = 0; w < nW; w += 1)
                {
                    lensOf = append(lensOf, w);
                }
                for (var w1 = 0; w1 < nW; w1 += 1)
                {
                    for (var w2 = w1 + 1; w2 < nW; w2 += 1)
                    {
                        var sharing = false;
                        for (var p1 in attachedTo[w1])
                        {
                            sharing = sharing || isIn(p1, attachedTo[w2]);
                        }
                        if (sharing && lensOf[w2] != lensOf[w1])
                        {
                            // merge into the SMALLER label: wall 0 keeps label 0, so the main lens keeps the op ids "shell" / "enclose"
                            const keepLabel = min(lensOf[w1], lensOf[w2]);
                            const dropLabel = max(lensOf[w1], lensOf[w2]);
                            for (var w3 = 0; w3 < nW; w3 += 1)
                            {
                                if (lensOf[w3] == dropLabel)
                                {
                                    lensOf[w3] = keepLabel;
                                }
                            }
                        }
                    }
                }
                var plugList = [];
                for (var lens = 0; lens < nW; lens += 1)
                {
                    var lensWalls = [];
                    var lensPieces = [];
                    for (var w = 0; w < nW; w += 1)
                    {
                        if (lensOf[w] == lens)
                        {
                            lensWalls = append(lensWalls, wallParts[w]);
                            for (var p in attachedTo[w])
                            {
                                if (!isIn(p, lensPieces))
                                {
                                    lensPieces = append(lensPieces, p);
                                }
                            }
                        }
                    }
                    // a union keeps the identity of its first tool: base patch pieces, then the walls, then the new surface's
                    // pieces (the order of the one-plug build, so faces picked on the result keep resolving)
                    var lensTools = [];
                    for (var p in lensPieces)
                    {
                        if (p < patchPieceCount)
                        {
                            lensTools = append(lensTools, shellPieces[p]);
                        }
                    }
                    lensTools = concatenateArrays([lensTools, lensWalls]);
                    for (var p in lensPieces)
                    {
                        if (p >= patchPieceCount)
                        {
                            lensTools = append(lensTools, shellPieces[p]);
                        }
                    }
                    if (size(lensTools) == 0)
                    {
                        continue;
                    }
                    const lensTools1 = qUnion(lensTools);
                    var lensShell = lensTools1;
                    if (size(lensTools) > 1)
                    {
                        const shellFollow = tracked(lensTools1);
                        // (the first lens keeps the op ids "shell" / "enclose" of the one-plug build: picks made on the faces it creates stay valid)
                        opBoolean(context, id + (lens == 0 ? "shell" : "shell" ~ lens), { "tools" : lensTools1, "operationType" : BooleanOperationType.UNION });
                        lensShell = qBodyType(shellFollow, BodyType.SHEET);
                    }
                    if (nakedCount(lensShell) != 0)
                    {
                        stage = ""; throw regenError("The patch, the walls and the new surface do not close into a solid plug (the walls do not meet the surfaces cleanly). Try another draft angle or offset.", ["offset"]);
                    }
                    const encloseId = id + (lens == 0 ? "enclose" : "enclose" ~ lens);
                    opEnclose(context, encloseId, { "entities" : lensShell });
                    plugList = concatenateArrays([plugList, evaluateQuery(context, qBodyType(qCreatedBy(encloseId, EntityType.BODY), BodyType.SOLID))]);
                }
                if (size(plugList) == 0)
                {
                    stage = ""; throw regenError("The plug could not be made into a solid.", ["offset"]);
                }
                // One plug normally. Where the new surface crosses the faces along the outline the shell encloses several
                // lenses, some outside the solid (added) and some inside (subtracted): each by its side of the base surface.
                var adds = [];
                var subtracts = [];
                if (size(plugList) == 1)
                {
                    if (side > 0)
                    {
                        adds = plugList;
                    }
                    else
                    {
                        subtracts = plugList;
                    }
                }
                else
                {
                    // by the side of the region faces (the imprinted copy: still there, outward like the solid's faces)
                    for (var lens in plugList)
                    {
                        const lc = evApproximateCentroid(context, { "entities" : lens });
                        const dl = evDistance(context, { "side0" : lc, "side1" : region });
                        const nl = evFaceTangentPlane(context, { "face" : regionList[dl.sides[1].index], "parameter" : dl.sides[1].parameter }).normal;
                        if (dot(lc - dl.sides[1].point, nl) >= 0 * meter)
                        {
                            adds = append(adds, lens);
                        }
                        else
                        {
                            subtracts = append(subtracts, lens);
                        }
                    }
                }
                stage = "adding the plug to the solid";
                if (size(adds) > 0)
                {
                    opBoolean(context, id + "apply", { "tools" : qUnion(concatenateArrays([[owner], adds])), "operationType" : BooleanOperationType.UNION });
                }
                stage = "subtracting the plug from the solid";
                if (size(subtracts) > 0)
                {
                    opBoolean(context, id + (size(adds) > 0 ? "applySub" : "apply"), { "tools" : qUnion(subtracts), "targets" : owner, "operationType" : BooleanOperationType.SUBTRACTION });
                }
                resultQuery = owner;
            }

            // ---- 8. clean up every helper body ----
            stage = "cleaning up";
            const leftovers = qSubtraction(qCreatedBy(id, EntityType.BODY), resultQuery);
            if (!isQueryEmpty(context, leftovers))
            {
                opDeleteBodies(context, id + "cleanup", { "entities" : leftovers });
            }
        }
        catch (error)
        {
            if (stage == "")
            {
                throw error;
            }
            throw regenError("Patch+ failed while " ~ stage ~ ": " ~ toString(error));
        }
    }, {
        "footprintType" : PatchPlusFootprint.CURVES,
        "projection" : PatchPlusProjection.NORMAL,
        "offset" : 0 * meter,
        "replacement" : qNothing(),
        "footprintFaces" : qNothing(),
        "footprintTool" : qNothing(),
        "footprint" : qNothing(),
        "flipOffset" : false,
        "walls" : PatchPlusWalls.NORMAL,
        "draft" : 0 * degree,
        "projectionDirection" : qNothing()
    });

/** The flip arrow of a positive offset (shown without a replacement surface). */
export function patchPlusManipulatorChange(context is Context, definition is map, newManipulators is map) returns map
{
    if (newManipulators["offsetFlip"] is map)
    {
        definition.flipOffset = newManipulators["offsetFlip"].flipped;
    }
    return definition;
}

/**
 * The outline of a set of faces: the edges with exactly one of the faces on them, every loop (islands, and the inner loop of a
 * face with a hole). An edge shared by two picked faces is not on it.
 */
function outlineOfFaces(context is Context, faces is Query) returns Query
{
    var outline = [];
    for (var edge in evaluateQuery(context, qAdjacent(faces, AdjacencyType.EDGE, EntityType.EDGE)))
    {
        if (size(evaluateQuery(context, qIntersection([qAdjacent(edge, AdjacencyType.EDGE, EntityType.FACE), faces]))) == 1)
        {
            outline = append(outline, edge);
        }
    }
    return qUnion(outline);
}
