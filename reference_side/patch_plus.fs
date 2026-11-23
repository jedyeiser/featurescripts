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

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Patch+",
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

        annotation { "Name" : "Projection" }
        definition.projection is PatchPlusProjection;

        if (definition.projection == PatchPlusProjection.DIRECTION)
        {
            annotation { "Name" : "Projection direction", "Filter" : QueryFilterCompound.ALLOWS_DIRECTION, "MaxNumberOfPicks" : 1 }
            definition.projectionDirection is Query;

            annotation { "Name" : "Flip direction", "UIHint" : UIHint.OPPOSITE_DIRECTION }
            definition.flipDirection is boolean;
        }

        annotation { "Name" : "Offset",
                    "Description" : "Where the region goes: + outward along the face normal (a boss), - inward (a pocket). With Walls = along the projection direction the faces are moved by this distance along that direction instead of being offset." }
        isLength(definition.offset, ZERO_DEFAULT_LENGTH_BOUNDS);

        annotation { "Name" : "Walls" }
        definition.walls is PatchPlusWalls;

        annotation { "Name" : "Draft angle",
                    "Description" : "Positive: the wall opens (the region grows) toward the offset surface. 0 = straight walls." }
        isAngle(definition.draft, PATCH_DRAFT_BOUNDS);
    }
    {
        verifyNoMesh(context, definition, "baseFaces");
        const offset = definition.offset;
        if (abs(offset) < 1e-9 * meter)
        {
            throw regenError("Set a non-zero offset: positive pushes the region out, negative pulls it in.", ["offset"]);
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
        const footEdges = qUnion([qEntityFilter(definition.footprint, EntityType.EDGE),
                    qOwnedByBody(qEntityFilter(definition.footprint, EntityType.BODY), EntityType.EDGE)]);
        if (isQueryEmpty(context, footEdges))
        {
            throw regenError("Pick the footprint curves.", ["footprint"]);
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
            const sideFaces = evaluateQuery(context, qAdjacent(imprintList[0], AdjacencyType.EDGE, EntityType.FACE));
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
                stage = ""; throw regenError("The footprint does not enclose a region inside the faces: its outline must close up and stay inside the surface (it reaches the edge of the surface, or the surface is too large to follow). Check the projection direction.", ["footprint"]);
            }
            const region = qUnion(floodFaces[insideSide]);
            const regionList = evaluateQuery(context, region);
            const centroid = evApproximateCentroid(context, { "entities" : region });
            const dIn = evDistance(context, { "side0" : centroid, "side1" : region });
            const pIn = dIn.sides[1].point;
            const regionNormal = evFaceTangentPlane(context, { "face" : regionList[dIn.sides[1].index], "parameter" : dIn.sides[1].parameter }).normal;
            // the direction toward the offset surface at the region: the face normal times the offset's sign
            const toward = regionNormal * (offset > 0 * meter ? 1 : -1);
            // walls along the projection direction: the faces are MOVED by the offset along that direction (exact, and
            // free of the kernel offset that fails on patchworks of small freeform faces); otherwise they are offset along the normal
            const moveAlongDirection = definition.walls == PatchPlusWalls.PROJECTION;
            const uIn = moveAlongDirection ? (dot(projectionDirection, toward) >= 0 ? projectionDirection : -projectionDirection) : toward;

            // ---- 2. the outline edges, sampled ----
            stage = "sampling the outline";
            const outlineEdges = evaluateQuery(context, qIntersection([qAdjacent(region, AdjacencyType.EDGE, EntityType.EDGE), imprintEdges]));
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
                    var nrm = evFaceTangentPlane(context, { "face" : regionList[dd.sides[1].index], "parameter" : dd.sides[1].parameter }).normal * (offset > 0 * meter ? 1 : -1);
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
                    lo = append(lo, e.ps[k] - d * reach);
                    hi = append(hi, e.ps[k] + d * reach);
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

            // ---- 5. the offset surface: the same faces, offset ----
            stage = "offsetting the faces (if the kernel cannot offset this surface, set Walls to along the projection direction: the faces are moved instead)";
            if (moveAlongDirection)
            {
                opExtractSurface(context, id + "offset", { "faces" : baseFaces, "tangentPropagation" : false });
                opTransform(context, id + "offsetMove", { "bodies" : qCreatedBy(id + "offset", EntityType.BODY), "transform" : transform(uIn * abs(offset)) });
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
                opExtractSurface(context, id + "offset", { "faces" : qUnion(nearFaces), "offset" : offset, "tangentPropagation" : false, "useFacesAroundToTrimOffset" : false });
            }
            const replacement = qCreatedBy(id + "offset", EntityType.BODY);
            const replacementPoint = moveAlongDirection ? pIn + uIn * abs(offset) : pIn + regionNormal * offset;

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
            const wallProbe = firstEdge.ps[0] + firstEdge.us[0] * (abs(offset) / 2 / (moveAlongDirection ? 1 : abs(dot(firstEdge.us[0], toward))));
            const wallPieces = qBodyType(tube, BodyType.SHEET);
            if (isQueryEmpty(context, wallPieces))
            {
                stage = ""; throw regenError("The walls do not cross the faces and the offset surface: check the projection direction and the offset.", ["offset"]);
            }
            const wall = nearest(wallPieces, wallProbe);
            dropAllBut(id + "dropWall", wallPieces, wall);

            stage = "cutting the offset surface to the outline (a creased or patchwork surface does not offset cleanly along its normals: set Walls to along the projection direction, which moves the faces instead)";
            const replTracked = tracked(replacement);
            opSplitPart(context, id + "offsetByTube", { "targets" : replacement, "tool" : tubeFull, "keepTools" : true });
            const replInside = nearest(qBodyType(replTracked, BodyType.SHEET), replacementPoint);
            dropAllBut(id + "dropOffset", qBodyType(replTracked, BodyType.SHEET), replInside);

            if (!isSolid)
            {
                // ---- 7a. sheet: cut the hole in the base, knit [outside of the base first, wall, offset region] ----
                stage = "cutting the hole in the sheet";
                const baseTracked = tracked(owner);
                opSplitPart(context, id + "baseByTube", { "targets" : owner, "tool" : tubeFull, "keepTools" : true });
                const pieces = qBodyType(baseTracked, BodyType.SHEET);
                const inside = nearest(pieces, pIn);
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
                opSplitPart(context, id + "surfByTube", { "targets" : baseSurface, "tool" : tubeFull, "keepTools" : true });
                const pieces = qBodyType(surfTracked, BodyType.SHEET);
                const patch = nearest(pieces, pIn);
                dropAllBut(id + "dropSurf", pieces, patch);
                stage = "closing the plug";
                const shellTools = qUnion([patch, wall, replInside]);
                const shell = tracked(shellTools);
                opBoolean(context, id + "shell", { "tools" : shellTools, "operationType" : BooleanOperationType.UNION });
                const shellBodies = qBodyType(shell, BodyType.SHEET);
                if (nakedCount(shellBodies) != 0)
                {
                    stage = ""; throw regenError("The patch, the walls and the offset surface do not close into a solid plug (the walls do not meet the surfaces cleanly). Try another draft angle or offset.", ["offset"]);
                }
                opEnclose(context, id + "enclose", { "entities" : shellBodies });
                const plug = qBodyType(qCreatedBy(id + "enclose", EntityType.BODY), BodyType.SOLID);
                if (isQueryEmpty(context, plug))
                {
                    stage = ""; throw regenError("The plug could not be made into a solid.", ["offset"]);
                }
                stage = offset > 0 * meter ? "adding the plug to the solid" : "subtracting the plug from the solid";
                if (offset > 0 * meter)
                {
                    opBoolean(context, id + "apply", { "tools" : qUnion([owner, plug]), "operationType" : BooleanOperationType.UNION });
                }
                else
                {
                    opBoolean(context, id + "apply", { "tools" : plug, "targets" : owner, "operationType" : BooleanOperationType.SUBTRACTION });
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
        "walls" : PatchPlusWalls.NORMAL,
        "draft" : 0 * degree,
        "projectionDirection" : qNothing()
    });
