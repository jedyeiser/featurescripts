FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// enums used by opSplitFace: not part of common.fs (a missing std enum shows only as a generic REGEN_ERROR in the feature)
import(path : "onshape/std/projectiontype.gen.fs", version : "3083.0");
// IMPORT: patch_plus_icon.svg (feature icon)
IconNamespace::import(path : "86f42fa364d0d4d039b50d83", version : "8c0a8d77d66f6fd1e6c8c691");

/**
 * Patch+: replace the part of a surface inside a footprint with its own offset, joined to the rest by walls.
 *
 * "Push or pull a region": the footprint curves are projected onto the faces; the faces inside the outline are removed
 * and replaced by the same faces offset by a distance (+ outward along the face normal, - inward), and walls connect
 * the outline on the faces to the offset surface. The walls run normal to the faces, or along the projection direction,
 * and may lean by a draft angle (positive: the wall opens toward the offset surface).
 *
 * Works on a sheet body (the result is the patched sheet, the original body keeps its identity) and on a solid (a
 * positive offset adds the plug of material, a negative offset subtracts it, the original solid keeps its identity).
 *
 * How it is built (spikes: docs/research/patch_plus_spike*.fs.txt, 2026-11-23): the footprint is imprinted on a COPY of
 * the faces (opSplitFace, native projection), the face bounded only by imprint edges is the region; one ruled wall per
 * outline edge (a lofted pair of curves running well past the base and the offset surface on both sides), joined into
 * one closed tube with mitred corners; the tube is cut by the base and by the offset surface and the middle piece is
 * the wall; the offset surface and the base are cut by the tube. Sheet: knit the outside of the base, the wall and the
 * offset region (base first, so it keeps its identity). Solid: knit base patch + wall + offset region into a closed
 * shell, enclose it into a plug, add or subtract the plug.
 *
 * Limits (v1): faces of ONE body, one closed footprint loop strictly inside the faces; the offset is along the face
 * normal (no arbitrary replacement surface yet).
 */

/** How the footprint curves are projected onto the faces. */
export enum PatchPlusProjection
{
    annotation { "Name" : "Normal to the faces" }
    NORMAL,
    annotation { "Name" : "Along a direction" }
    DIRECTION
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

        annotation { "Name" : "Footprint curves",
                    "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE),
                    "Description" : "Sketch curves or edges forming one closed outline. Projected onto the faces; the faces inside it are replaced." }
        definition.footprint is Query;

        annotation { "Name" : "Footprint faces",
                    "Filter" : EntityType.FACE || (BodyType.SHEET && EntityType.BODY),
                    "Description" : "Instead of curves: faces (or a sheet body) whose outline is projected onto the faces. Every loop counts: several islands, and the holes of a face (the region is the face itself, a ring where it has a hole). Pick curves OR faces. A footprint that runs off the edge of the picked faces is built as a straight move of the region (Walls along the projection direction, or a planar region; no draft, no replacement surface)." }
        definition.footprintFaces is Query;

        annotation { "Name" : "Projection" }
        definition.projection is PatchPlusProjection;

        if (definition.projection == PatchPlusProjection.DIRECTION)
        {
            annotation { "Name" : "Projection direction", "Filter" : QueryFilterCompound.ALLOWS_DIRECTION, "MaxNumberOfPicks" : 1 }
            definition.projectionDirection is Query;

            annotation { "Name" : "Flip direction", "UIHint" : UIHint.OPPOSITE_DIRECTION }
            definition.flipDirection is boolean;
        }

        annotation { "Name" : "Replacement surface",
                    "Filter" : EntityType.FACE || (BodyType.SHEET && EntityType.BODY),
                    "Description" : "Optional. A surface that goes where the region is: it can lie in front of or behind the faces, but must not cross them inside the footprint. Empty: the faces' own offset." }
        definition.replacement is Query;

        annotation { "Name" : "Offset",
                    "Description" : "How far the new surface is from the faces. Direction of a POSITIVE offset: toward the Side reference if there is one; otherwise along the face normal (outward on a solid; Flip reverses it). With a Replacement surface: + moves it further from the faces, - closer, and the Side reference and Flip are ignored. With Walls = along the projection direction the surface is moved by this distance along that direction instead of being offset." }
        isLength(definition.offset, ZERO_DEFAULT_LENGTH_BOUNDS);

        annotation { "Name" : "Side reference", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR,
                    "MaxNumberOfPicks" : 1,
                    "Description" : "Optional. A positive offset moves toward this geometry (best a mate connector or vertex clearly to one side of the faces; an error if it lies in the plane of the faces at the region). Empty: along the face normal. Ignored with a Replacement surface." }
        definition.sideReference is Query;

        annotation { "Name" : "Flip offset direction", "UIHint" : UIHint.OPPOSITE_DIRECTION,
                    "Description" : "Reverse the face normal as the direction of a positive offset. Used only without a Side reference and without a Replacement surface; the arrow in the graphics area shows the direction." }
        definition.flipOffset is boolean;

        annotation { "Name" : "Walls" }
        definition.walls is PatchPlusWalls;

        annotation { "Name" : "Draft angle",
                    "Description" : "Positive: the wall is WIDER toward the new surface (the region grows as you move toward it); negative: NARROWER toward it, the usual release draft for a boss or a pocket. The new surface is the offset surface, the replacement surface, or the side the offset moves to (see the arrow). 0 = straight walls." }
        isAngle(definition.draft, PATCH_DRAFT_BOUNDS);
    }
    {
        verifyNoMesh(context, definition, "baseFaces");
        const offset = definition.offset;
        const replacementFaces = qUnion([qEntityFilter(definition.replacement, EntityType.FACE),
                    qOwnedByBody(qEntityFilter(definition.replacement, EntityType.BODY), EntityType.FACE)]);
        const hasReplacement = !isQueryEmpty(context, replacementFaces);
        if (!hasReplacement && abs(offset) < 1e-9 * meter)
        {
            throw regenError("Set a non-zero offset (positive pushes the region out, negative pulls it in), or pick a replacement surface.", ["offset"]);
        }
        if (definition.walls == PatchPlusWalls.PROJECTION && definition.projection != PatchPlusProjection.DIRECTION)
        {
            throw regenError("Walls along the projection direction need the projection set to \"Along a direction\".", ["walls"]);
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
        const footprintFaces = qUnion([qEntityFilter(definition.footprintFaces, EntityType.FACE),
                    qOwnedByBody(qEntityFilter(definition.footprintFaces, EntityType.BODY), EntityType.FACE)]);
        const hasFootprintFaces = !isQueryEmpty(context, footprintFaces);
        const footprintCurves = qUnion([qEntityFilter(definition.footprint, EntityType.EDGE),
                    qOwnedByBody(qEntityFilter(definition.footprint, EntityType.BODY), EntityType.EDGE)]);
        if (hasFootprintFaces && !isQueryEmpty(context, footprintCurves))
        {
            throw regenError("Pick the footprint as curves OR as faces, not both.", ["footprint", "footprintFaces"]);
        }
        // Faces: their outline (the edges with exactly one picked face: every loop, holes and islands included) is projected.
        const footEdges = hasFootprintFaces ? outlineOfFaces(context, footprintFaces) : footprintCurves;
        if (isQueryEmpty(context, footEdges))
        {
            throw regenError("Pick the footprint curves or the footprint faces.", ["footprint"]);
        }
        var projectionDirection = undefined;
        if (definition.projection == PatchPlusProjection.DIRECTION)
        {
            if (isQueryEmpty(context, definition.projectionDirection))
            {
                throw regenError("Pick the projection direction.", ["projectionDirection"]);
            }
            projectionDirection = normalize(extractDirection(context, definition.projectionDirection) * (definition.flipDirection ? -1 : 1));
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
                return best;
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

        // a point well inside a face (away from its boundary) and the parameter there: the centroid's nearest point, or the best of
        // a coarse grid when that is close to the boundary (a ring's centroid lies in its hole)
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
                    for (var i = 1; i <= 5; i += 1)
                    {
                        for (var j = 1; j <= 5; j += 1)
                        {
                            const prm = vector(i / 6, j / 6);
                            const origin = evFaceTangentPlane(context, { "face" : face, "parameter" : prm }).origin;
                            if (evDistance(context, { "side0" : origin, "side1" : face }).distance < 1e-6 * meter)
                            {
                                const margin = evDistance(context, { "side0" : origin, "side1" : faceEdges }).distance;
                                if (margin > bestMargin)
                                {
                                    bestMargin = margin;
                                    best = { "point" : origin, "parameter" : prm };
                                }
                            }
                        }
                    }
                }
                best.normal = evFaceTangentPlane(context, { "face" : face, "parameter" : best.parameter }).normal;
                return best;
            };
        // the largest face of a body (the one to probe a piece with)
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
            if (projectionDirection != undefined)
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
            {                const sideFaces = evaluateQuery(context, qAdjacent(imprintList[0], AdjacencyType.EDGE, EntityType.FACE));
                if (size(sideFaces) < 2)
                {
                    stage = ""; throw regenError("The footprint does not enclose a region inside the faces: it must be one closed outline that lies inside them.", ["footprint"]);
                }
                var floodFaces = [[sideFaces[0]], [sideFaces[1]]];
                var floodQueue = [[sideFaces[0]], [sideFaces[1]]];
                var floodSeen = [{}, {}];
                var floodState = ["open", "open"];
                for (var s = 0; s < 2; s += 1)
                {
                    var seen0 = floodSeen[s];
                    seen0[transientQueriesToStrings(sideFaces[s])] = true;
                    floodSeen[s] = seen0;
                }
                var floodSteps = 0;
                while (floodSteps < PATCH_FLOOD_LIMIT && floodState[0] != "closed" && floodState[1] != "closed" && (floodState[0] == "open" || floodState[1] == "open"))
                {
                    floodSteps += 1;
                    for (var s = 0; s < 2; s += 1)
                    {
                        if (floodState[s] != "open")
                        {
                            continue;
                        }
                        if (size(floodQueue[s]) == 0)
                        {
                            floodState[s] = "closed";
                            continue;
                        }
                        const current = floodQueue[s][0];
                        floodQueue[s] = subArray(floodQueue[s], 1);
                        for (var edgeOfFace in evaluateQuery(context, qAdjacent(current, AdjacencyType.EDGE, EntityType.EDGE)))
                        {
                            if (!isQueryEmpty(context, qIntersection([edgeOfFace, imprintEdges])))
                            {
                                continue;       // the outline: the region stops here
                            }
                            const across = evaluateQuery(context, qSubtraction(qAdjacent(edgeOfFace, AdjacencyType.EDGE, EntityType.FACE), current));
                            if (size(across) == 0)
                            {
                                floodState[s] = "boundary";     // a free edge of the surface: this side is the outside
                                break;
                            }
                            var seen = floodSeen[s];
                            for (var other in across)
                            {
                                const key = transientQueriesToStrings(other);
                                if (seen[key] == undefined)
                                {
                                    seen[key] = true;
                                    floodFaces[s] = append(floodFaces[s], other);
                                    floodQueue[s] = append(floodQueue[s], other);
                                }
                            }
                            floodSeen[s] = seen;
                        }
                    }
                }
                var insideSide = -1;
                if (floodState[0] == "closed")
                {
                    insideSide = 0;
                }
                else if (floodState[1] == "closed")
                {
                    insideSide = 1;
                }
                if (insideSide < 0)
                {
                    stage = ""; throw regenError("The footprint does not enclose a region inside the faces: its outline must close up and stay inside the surface (it reaches the edge of the picked faces, which the curves input does not support: use Footprint faces, or the surface is too large to follow). Keep the footprint inside the picked faces and check the projection direction.", ["footprint"]);
                }
                regionFaces = floodFaces[insideSide];
            }
            const region = qUnion(regionFaces);
            const regionList = evaluateQuery(context, region);
            const underRegion = function(p is Vector) returns boolean
                {
                    return evDistance(context, { "side0" : p, "side1" : region }).distance < 1e-5 * meter;
                };
            const centroid = evApproximateCentroid(context, { "entities" : region });
            const dIn = evDistance(context, { "side0" : centroid, "side1" : region });
            const pIn = dIn.sides[1].point;
            const regionNormal = evFaceTangentPlane(context, { "face" : regionList[dIn.sides[1].index], "parameter" : dIn.sides[1].parameter }).normal;
            // Which side of the faces the new surface is on (side = +1: along the face normal). Without a replacement surface
            // it is the offset's sign; with one, a ray from the region along the normal (or the projection direction) finds it.
            // The direction of a POSITIVE offset, as +1 / -1 along the face normal: toward the side reference when there is one (an
            // error if it lies in the plane of the faces: it then says nothing about the side), otherwise along the face normal
            // (Flip reverses it). With a replacement surface the side is found by rays below and neither applies.
            var positiveSide = definition.flipOffset ? -1 : 1;
            const hasReference = !isQueryEmpty(context, definition.sideReference);
            if (!hasReplacement && hasReference)
            {
                stage = "reading the side reference";
                var referencePoint = undefined;
                const referenceMate = qBodyType(definition.sideReference, BodyType.MATE_CONNECTOR);
                if (!isQueryEmpty(context, referenceMate))
                {
                    referencePoint = evMateConnector(context, { "mateConnector" : referenceMate }).origin;
                }
                else
                {
                    referencePoint = evDistance(context, { "side0" : pIn, "side1" : definition.sideReference }).sides[1].point;
                }
                const along = dot(referencePoint - pIn, regionNormal);
                if (abs(along) < 1e-5 * meter)
                {
                    addDebugPoint(context, referencePoint, DebugColor.RED);
                    stage = ""; throw regenError("The side reference lies in the plane of the faces at the region (marked), so it does not say which side is positive: pick something clearly to one side of the faces.", ["sideReference"]);
                }
                positiveSide = along > 0 ? 1 : -1;
            }
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

            // ---- the arrow: the direction of a POSITIVE offset, in every mode ----
            // Flippable (no side reference, no replacement surface): an interactive flip arrow. Otherwise a fixed arrow (visible
            // while the dialog is open): toward the side reference, or from the faces toward the replacement surface.
            var positiveDirection = regionNormal * positiveSide;
            if (hasReplacement)
            {
                positiveDirection = hitDirection;
            }
            else if (definition.walls == PatchPlusWalls.PROJECTION)
            {
                // the faces move along the projection direction: its sign that agrees with the positive side
                positiveDirection = dot(projectionDirection, positiveDirection) >= 0 ? projectionDirection : -projectionDirection;
            }
            if (!hasReplacement && !hasReference)
            {
                // the unflipped direction is the face normal (or the projection direction on its side); flipped reverses it
                const unflipped = definition.walls == PatchPlusWalls.PROJECTION ? (dot(projectionDirection, regionNormal) >= 0 ? projectionDirection : -projectionDirection) : regionNormal;
                addManipulators(context, id, { "offsetFlip" : flipManipulator({ "base" : pIn, "direction" : unflipped, "flipped" : definition.flipOffset }) });
            }
            else
            {
                const regionBox = evBox3d(context, { "topology" : region });
                const arrowLength = max(norm(regionBox.maxCorner - regionBox.minCorner) * 0.2, 2 * millimeter);
                addDebugArrow(context, pIn, pIn + positiveDirection * arrowLength, arrowLength * 0.08, DebugColor.GREEN);
            }
            // walls along the projection direction: the faces are MOVED by the offset along that direction (exact, and
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
                for (var i = 0; i <= PATCH_SAMPLES; i += 1)
                {
                    const dd = evDistance(context, { "side0" : lines[i].origin, "side1" : region });
                    var nrm = evFaceTangentPlane(context, { "face" : regionList[dd.sides[1].index], "parameter" : dd.sides[1].parameter }).normal * side;
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
            var firstHitDistance = abs(offset);
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
                        if (i == 0 && k == 0)
                        {
                            firstHitDistance = hitDistance;
                        }
                    }
                }
                wallReach = 1.5 * (farthest + abs(offset)) + 5 * millimeter;
            }
            stage = "building the walls";
            var walls = [];
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
                opDeleteBodies(context, wid + "delcurves", { "entities" : qUnion([qCreatedBy(wid + "lo", EntityType.BODY), qCreatedBy(wid + "hi", EntityType.BODY)]) });
                walls = append(walls, qCreatedBy(wid + "loft", EntityType.BODY));
            }
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
                const shifted = abs(offset) > 1e-9 * meter;
                var extractDefinition = { "faces" : replacementFaces, "tangentPropagation" : false };
                if (shifted && !moveAlongDirection)
                {
                    extractDefinition.offset = offset * replacementSign;
                    extractDefinition.useFacesAroundToTrimOffset = false;
                }
                opExtractSurface(context, id + "offset", extractDefinition);
                if (shifted && moveAlongDirection)
                {
                    opTransform(context, id + "offsetMove", { "bodies" : qCreatedBy(id + "offset", EntityType.BODY), "transform" : transform(uIn * offset) });
                }
                replacementPoint = replacementHit.intersection + hitDirection * offset;
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
            const firstEdge = edges[0];
            // u points toward the offset side: half the way to the offset surface (its distance along u is |offset| / |u . n|, or |offset| when the faces are moved along u)
            const wallProbe = hasReplacement ? firstEdge.ps[0] + firstEdge.us[0] * ((firstHitDistance + offset) / 2)
                : firstEdge.ps[0] + firstEdge.us[0] * (abs(offset) / 2 / (moveAlongDirection ? 1 : abs(dot(firstEdge.us[0], toward))));
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
            for (var tk = 0; tk < size(tubeList); tk += 1)
            {
                opSplitPart(context, id + (tk == 0 ? "offsetByTube" : "offsetByTube" ~ tk), { "targets" : tk == 0 ? replacement : qBodyType(replTracked, BodyType.SHEET),
                            "tool" : tubeList[tk], "keepTools" : true });
            }
            var replInside = undefined;
            if (!hasFootprintFaces)
            {
                replInside = nearest(qBodyType(replTracked, BodyType.SHEET), replacementPoint);
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
                for (var tk = 0; tk < size(tubeList); tk += 1)
                {
                    opSplitPart(context, id + (tk == 0 ? "baseByTube" : "baseByTube" ~ tk), { "targets" : tk == 0 ? owner : qBodyType(baseTracked, BodyType.SHEET),
                                "tool" : tubeList[tk], "keepTools" : true });
                }
                const pieces = qBodyType(baseTracked, BodyType.SHEET);
                var inside = undefined;
                if (!hasFootprintFaces)
                {
                    inside = nearest(pieces, pIn);
                }
                else
                {
                    var insideList = [];
                    for (var basePiece in evaluateQuery(context, pieces))
                    {
                        if (underRegion(interiorPoint(largestFaceOf(basePiece)).point))
                        {
                            insideList = append(insideList, basePiece);
                        }
                    }
                    if (size(insideList) == 0)
                    {
                        stage = ""; throw regenError("No part of the sheet lies under the footprint region.", ["footprintFaces"]);
                    }
                    inside = qUnion(insideList);
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
                for (var tk = 0; tk < size(tubeList); tk += 1)
                {
                    opSplitPart(context, id + (tk == 0 ? "surfByTube" : "surfByTube" ~ tk), { "targets" : tk == 0 ? baseSurface : qBodyType(surfTracked, BodyType.SHEET),
                                "tool" : tubeList[tk], "keepTools" : true });
                }
                const pieces = qBodyType(surfTracked, BodyType.SHEET);
                var patch = undefined;
                if (!hasFootprintFaces)
                {
                    patch = nearest(pieces, pIn);
                }
                else
                {
                    var patchKeep = [];
                    for (var surfPiece in evaluateQuery(context, pieces))
                    {
                        if (underRegion(interiorPoint(largestFaceOf(surfPiece)).point))
                        {
                            patchKeep = append(patchKeep, surfPiece);
                        }
                    }
                    if (size(patchKeep) == 0)
                    {
                        stage = ""; throw regenError("No part of the base surface lies under the footprint region.", ["footprintFaces"]);
                    }
                    patch = qUnion(patchKeep);
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
                                var known = false;
                                for (var q in attached)
                                {
                                    known = known || q == p;
                                }
                                if (!known)
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
                            for (var p2 in attachedTo[w2])
                            {
                                sharing = sharing || p1 == p2;
                            }
                        }
                        if (sharing && lensOf[w2] != lensOf[w1])
                        {
                            const from = lensOf[w2];
                            for (var w3 = 0; w3 < nW; w3 += 1)
                            {
                                if (lensOf[w3] == from)
                                {
                                    lensOf[w3] = lensOf[w1];
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
                                var have = false;
                                for (var q in lensPieces)
                                {
                                    have = have || q == p;
                                }
                                if (!have)
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
        "projection" : PatchPlusProjection.NORMAL,
        "flipDirection" : false,
        "offset" : 0 * meter,
        "replacement" : qNothing(),
        "footprintFaces" : qNothing(),
        "footprint" : qNothing(),
        "sideReference" : qNothing(),
        "flipOffset" : false,
        "walls" : PatchPlusWalls.NORMAL,
        "draft" : 0 * degree,
        "projectionDirection" : qNothing()
    });

/** The flip arrow of a positive offset (shown without a side reference or a replacement surface). */
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
