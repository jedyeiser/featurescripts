FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: extrude_edge_icon.svg (feature icon)
IconNamespace::import(path : "767e9ea707a86fb82e710e5a", version : "8038a27c525caa815e3bf7da");

// Legacy (hidden): saved instances picked a wire body OR edges through this switch.
export enum ExtrudeEdgeInputType
{
    BODIES,
    EDGES
}

export enum ExtrudeEdgeDirectionType
{
    QUERY,
    VECTOR
}

export enum ExtrudeEdgeEndType
{
    annotation { "Name" : "Blind" }
    BLIND,
    annotation { "Name" : "Symmetric" }
    SYMMETRIC,
    annotation { "Name" : "Up to face" }
    UP_TO_SURFACE,
    annotation { "Name" : "Up to vertex" }
    UP_TO_VERTEX
}

// Smallest depth NONNEGATIVE_LENGTH_BOUNDS accepts.
const MIN_DEPTH = 1e-5 * meter;

export function extrudeEdgeEditingLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map, hiddenBodies is Query) returns map
{
    if (definition.extrudeDirectionFrom == ExtrudeEdgeDirectionType.VECTOR)
    {
        if (definition.vectorX != 0 || definition.vectorY != 0 || definition.vectorZ != 0)
        {
            var vec = normalize(vector(definition.vectorX, definition.vectorY, definition.vectorZ));
            definition.normalizedVectorString = "[" ~ toString(round(vec[0], .01)) ~ ", " ~ toString(round(vec[1], .01)) ~ ", " ~ toString(round(vec[2], .01)) ~ "]";
        }
    }

    // A saved instance picked through the legacy input-type switch: move its pick into the one selection.
    if (isQueryEmpty(context, definition.extrudeEntities))
    {
        const legacy = legacyEntities(definition);
        if (!isQueryEmpty(context, legacy))
        {
            definition.extrudeEntities = legacy;
            definition.wireBody = qNothing();
            definition.selEdges = qNothing();
        }
    }

    if (definition.legacyDepths)
    {
        definition = convertLegacyDepths(definition);
    }

    return definition;
}

export const VectorInputBounds = {(unitless) : [-1, 0, 1]} as RealBoundSpec;

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Extrude edge", "Feature Type Description" : "Takes a wire body or a set of edges as input with standard extrude parameters and creates an extruded surface", "Editing Logic Function" : "extrudeEdgeEditingLogic" }
export const extrudeEdge = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Edges or wires", "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE) }
        definition.extrudeEntities is Query;

        // Legacy selection (saved instances before the single selection); moved into "Edges or wires" when edited.
        annotation { "Name" : "Input type", "Default" : ExtrudeEdgeInputType.BODIES, "UIHint" : [UIHint.ALWAYS_HIDDEN] }
        definition.inputType is ExtrudeEdgeInputType;

        annotation { "Name" : "Wire body", "Filter" : EntityType.BODY && BodyType.WIRE, "MaxNumberOfPicks" : 1, "UIHint" : [UIHint.ALWAYS_HIDDEN] }
        definition.wireBody is Query;

        annotation { "Name" : "Edges", "Filter" : EntityType.EDGE, "UIHint" : [UIHint.ALWAYS_HIDDEN] }
        definition.selEdges is Query;

        annotation { "Name" : "Extrude direction from", "Default" : ExtrudeEdgeDirectionType.QUERY, "UIHint" : [UIHint.SHOW_LABEL] }
        definition.extrudeDirectionFrom is ExtrudeEdgeDirectionType;

        if (definition.extrudeDirectionFrom == ExtrudeEdgeDirectionType.QUERY)
        {
            annotation { "Name" : "Direction query", "Filter" : (EntityType.FACE && GeometryType.PLANE) || BodyType.MATE_CONNECTOR || GeometryType.LINE, "MaxNumberOfPicks" : 1 }
            definition.directionQuery is Query;

            annotation { "Name" : "flipDir", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false }
            definition.flipDir is boolean;
        }
        else if (definition.extrudeDirectionFrom == ExtrudeEdgeDirectionType.VECTOR)
        {
            annotation { "Group Name" : "Direction vector", "Collapsed By Default" : true }
            {
                annotation { "Name" : "X", "Icon" : Icon.ALONG_X, "Default" : 0  }
                isReal(definition.vectorX, VectorInputBounds);

                annotation { "Name" : "Y", "Icon" : Icon.ALONG_Y, "Default" : 0  }
                isReal(definition.vectorY, VectorInputBounds);

                annotation { "Name" : "Z", "Icon" : Icon.ALONG_Z, "Default" : 1  }
                isReal(definition.vectorZ, VectorInputBounds);

                annotation { "Name" : "Normalized vector", "UIHint" : UIHint.READ_ONLY }
                definition.normalizedVectorString is string;
            }

            annotation { "Name" : "Opposite direction", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false }
            definition.flipVector is boolean;
        }

        // true = the depth fields are the legacy signed "extrudeLength" / "dir2Len" (a negative depth extrudes
        // backwards). Default true so every SAVED instance keeps its signed depths (correction 25); the editing
        // logic converts to non-negative depth + flip as soon as the dialog changes.
        annotation { "Name" : "Legacy signed depths", "Default" : true, "UIHint" : [UIHint.ALWAYS_HIDDEN] }
        definition.legacyDepths is boolean;

        annotation { "Name" : "End type", "Default" : ExtrudeEdgeEndType.BLIND, "UIHint" : [UIHint.SHOW_LABEL],
                    "Description" : "Up to face / Up to vertex extrude toward the target whatever the direction flip says." }
        definition.endType is ExtrudeEdgeEndType;

        if (definition.endType == ExtrudeEdgeEndType.BLIND || definition.endType == ExtrudeEdgeEndType.SYMMETRIC)
        {
            if (definition.legacyDepths)
            {
                annotation { "Name" : "Depth" }
                isLength(definition.extrudeLength, LENGTH_BOUNDS);
            }
            else
            {
                annotation { "Name" : "Depth" }
                isLength(definition.depth, NONNEGATIVE_LENGTH_BOUNDS);
            }
        }
        else if (definition.endType == ExtrudeEdgeEndType.UP_TO_SURFACE)
        {
            annotation { "Name" : "Up to face", "Filter" : (EntityType.FACE && SketchObject.NO) || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            definition.endFace is Query;
        }
        else if (definition.endType == ExtrudeEdgeEndType.UP_TO_VERTEX)
        {
            annotation { "Name" : "Up to vertex or mate connector", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            definition.endVertex is Query;
        }

        if (definition.endType == ExtrudeEdgeEndType.BLIND || definition.endType == ExtrudeEdgeEndType.UP_TO_SURFACE ||
            definition.endType == ExtrudeEdgeEndType.UP_TO_VERTEX)
        {
            annotation { "Name" : "Second direction", "Default" : false }
            definition.secondDirection is boolean;

            if (definition.secondDirection)
            {
                if (definition.legacyDepths)
                {
                    annotation { "Name" : "Direction 2 length" }
                    isLength(definition.dir2Len, LENGTH_BOUNDS);
                }
                else
                {
                    annotation { "Name" : "Direction 2 depth" }
                    isLength(definition.secondDepth, NONNEGATIVE_LENGTH_BOUNDS);
                }
            }
        }

        annotation { "Name" : "Name", "Default" : "", "Description" : "Name for the extruded surface (numbered when there are several). Empty keeps the default name." }
        definition.bodyName is string;
    }
    {
        var extrudeEdges = definition.extrudeEntities;
        extrudeEdges = isQueryEmpty(context, extrudeEdges) ? legacyEntities(definition) :
                qUnion([qEntityFilter(extrudeEdges, EntityType.EDGE), qOwnedByBody(qEntityFilter(extrudeEdges, EntityType.BODY), EntityType.EDGE)]);
        if (isQueryEmpty(context, extrudeEdges))
        {
            throw regenError("Select the edges or wire bodies to extrude.", ["extrudeEntities"]);
        }
        var extrudeDir = extractDir(context, definition);
        checkDirectionAcrossEdges(context, extrudeEdges, extrudeDir);

        const legacy = definition.legacyDepths;
        var extrudeDef = {
                "entities" : extrudeEdges,
                "endBound" : BoundingType.BLIND
        };
        var boundPlane = false;
        if (definition.endType == ExtrudeEdgeEndType.BLIND)
        {
            extrudeDef.endDepth = legacy ? definition.extrudeLength : definition.depth;
            // A saved one-sided negative depth meant "the other way"; opExtrude refuses a negative end depth
            // (EXTRUDE_FAILED, also before 2026-09-26), so extrude the flipped direction instead (test E16).
            if (legacy && !definition.secondDirection && definition.extrudeLength < 0 * meter)
            {
                extrudeDir = -extrudeDir;
                extrudeDef.endDepth = -definition.extrudeLength;
            }
        }
        else if (definition.endType == ExtrudeEdgeEndType.SYMMETRIC)
        {
            const total = legacy ? abs(definition.extrudeLength) : definition.depth;
            extrudeDef.endDepth = total / 2;
            extrudeDef.startBound = BoundingType.BLIND;
            extrudeDef.startDepth = total / 2;
        }
        else
        {
            const bound = upToBound(context, id, definition, extrudeEdges, extrudeDir);
            extrudeDir = bound.direction;
            boundPlane = bound.boundPlane;
            extrudeDef.endBound = BoundingType.UP_TO_SURFACE;
            extrudeDef.endBoundEntity = bound.face;
        }
        extrudeDef.direction = extrudeDir;

        if (definition.secondDirection && definition.endType != ExtrudeEdgeEndType.SYMMETRIC)
        {
            extrudeDef = mergeMaps(extrudeDef, {'startDepth' : legacy ? definition.dir2Len : definition.secondDepth, 'startBound' : BoundingType.BLIND});
        }

        opExtrude(context, id + "extrude1", extrudeDef);

        if (boundPlane)
        {
            opDeleteBodies(context, id + "deleteBoundPlane", { "entities" : qCreatedBy(id + "boundPlane", EntityType.BODY) });
        }

        if (definition.bodyName != "")
        {
            const bodies = evaluateQuery(context, qCreatedBy(id + "extrude1", EntityType.BODY));
            for (var i = 0; i < size(bodies); i += 1)
            {
                setProperty(context, {
                        "entities" : bodies[i],
                        "propertyType" : PropertyType.NAME,
                        "value" : size(bodies) == 1 ? definition.bodyName : definition.bodyName ~ " " ~ (i + 1)
                });
            }
        }
    }, {
        "extrudeEntities" : qNothing(),
        "inputType" : ExtrudeEdgeInputType.BODIES,
        "wireBody" : qNothing(),
        "selEdges" : qNothing(),
        "flipDir" : false,
        "flipVector" : false,
        "legacyDepths" : true,
        "endType" : ExtrudeEdgeEndType.BLIND,
        "depth" : 25 * millimeter,
        "secondDirection" : false,
        "secondDepth" : 25 * millimeter,
        "endFace" : qNothing(),
        "endVertex" : qNothing(),
        "bodyName" : ""
    });

// The edges picked through the legacy input-type switch (hidden parameters of saved instances).
function legacyEntities(definition is map) returns Query
{
    return (definition.inputType == ExtrudeEdgeInputType.BODIES) ? qUnion([qOwnedByBody(definition.wireBody, EntityType.EDGE)]) : qUnion([definition.selEdges]);
}

// Legacy signed depths -> non-negative depths + direction flip, when that describes the same extrusion
// (both depths on the same side of zero). Otherwise the instance stays on its legacy depths.
function convertLegacyDepths(definition is map) returns map
{
    const len = definition.extrudeLength;
    if (len == undefined || abs(len) < MIN_DEPTH)
    {
        return definition;
    }
    const usesSecond = definition.secondDirection == true && definition.endType != ExtrudeEdgeEndType.SYMMETRIC;
    if (usesSecond)
    {
        const len2 = definition.dir2Len;
        if (len2 == undefined || abs(len2) < MIN_DEPTH || (len < 0 * meter) != (len2 < 0 * meter))
        {
            return definition;
        }
        definition.secondDepth = abs(len2);
    }
    definition.depth = abs(len);
    if (len < 0 * meter)
    {
        if (definition.extrudeDirectionFrom == ExtrudeEdgeDirectionType.VECTOR)
        {
            definition.flipVector = definition.flipVector != true;
        }
        else
        {
            definition.flipDir = definition.flipDir != true;
        }
    }
    definition.legacyDepths = false;
    return definition;
}

function extractDir(context is Context, definition is map) returns Vector
{
    var dir;
    if (definition.extrudeDirectionFrom == ExtrudeEdgeDirectionType.VECTOR)
    {
        var v = vector(definition.vectorX, definition.vectorY, definition.vectorZ);
        if (norm(v) < TOLERANCE.zeroLength)
        {
            throw regenError("The direction vector is zero; set X, Y or Z.", ["vectorX", "vectorY", "vectorZ"]);
        }
        return definition.flipVector ? -normalize(v) : normalize(v);
    }
    // A mate connector can arrive as its vertex (correction 44): resolve it to the connector body.
    var connector = qBodyType(qUnion([definition.directionQuery, qOwnerBody(definition.directionQuery)]), BodyType.MATE_CONNECTOR);
    if (!isQueryEmpty(context, connector))
    {
        dir = evMateConnector(context, { "mateConnector" : connector }).zAxis;
    }
    else if (!isQueryEmpty(context, qGeometry(definition.directionQuery, GeometryType.LINE)))
    {
        dir = evLine(context, { "edge" : definition.directionQuery }).direction;
    }
    else if (!isQueryEmpty(context, qGeometry(definition.directionQuery, GeometryType.PLANE)))
    {
        dir = evPlane(context, { "face" : definition.directionQuery }).normal;
    }
    else
    {
        throw regenError("Select a planar face, a line or a mate connector for the extrude direction.", ["directionQuery"]);
    }
    return definition.flipDir ? -dir : dir;
}

// The bounding face for Up to face / Up to vertex, and the extrude direction pointed at it (the flip is not
// used: the target decides the side). A mate connector or vertex target becomes a temporary plane
// (id + "boundPlane", deleted after the extrude), as std extrude does.
function upToBound(context is Context, id is Id, definition is map, edges is Query, dir is Vector) returns map
{
    const bb = evBox3d(context, { "topology" : edges, "tight" : true });
    const center = (bb.minCorner + bb.maxCorner) / 2;
    const isVertex = definition.endType == ExtrudeEdgeEndType.UP_TO_VERTEX;
    const target = isVertex ? definition.endVertex : definition.endFace;
    const field = isVertex ? "endVertex" : "endFace";
    if (isQueryEmpty(context, target))
    {
        throw regenError(isVertex ? "Select the vertex or mate connector to extrude up to." : "Select the face or mate connector to extrude up to.", [field]);
    }
    const connector = qBodyType(qUnion([target, qOwnerBody(target)]), BodyType.MATE_CONNECTOR);
    const hasConnector = !isQueryEmpty(context, connector);

    var reach;
    var boundPlane;
    if (isVertex)
    {
        const point = hasConnector ? evMateConnector(context, { "mateConnector" : connector }).origin :
                evVertexPoint(context, { "vertex" : qEntityFilter(target, EntityType.VERTEX) });
        reach = dot(point - center, dir);
        boundPlane = plane(point, dir);
    }
    else if (hasConnector)
    {
        const cs = evMateConnector(context, { "mateConnector" : connector });
        const across = dot(dir, cs.zAxis);
        if (abs(across) < TOLERANCE.zeroAngle)
        {
            throw regenError("The extrude direction lies in the mate connector's XY plane; it never reaches it.", [field]);
        }
        reach = dot(cs.origin - center, cs.zAxis) / across;
        boundPlane = plane(cs);
    }
    else
    {
        const closest = evDistance(context, { "side0" : center, "side1" : qEntityFilter(target, EntityType.FACE) }).sides[1].point;
        reach = dot(closest - center, dir);
    }
    const direction = reach < 0 * meter ? -dir : dir;

    if (boundPlane == undefined)
    {
        return { "face" : qEntityFilter(target, EntityType.FACE), "direction" : direction, "boundPlane" : false };
    }
    opPlane(context, id + "boundPlane", { "plane" : boundPlane });
    return { "face" : qCreatedBy(id + "boundPlane", EntityType.FACE), "direction" : direction, "boundPlane" : true };
}

// Extruding along an edge's own tangent makes a zero-area face and the kernel fails with EXTRUDE_FAILED;
// catch it up front with a message that says what to change.
function checkDirectionAcrossEdges(context is Context, edges is Query, dir is Vector)
{
    for (var edge in evaluateQuery(context, edges))
    {
        for (var tl in evEdgeTangentLines(context, { "edge" : edge, "parameters" : [0, 0.5, 1] }))
        {
            if (abs(dot(tl.direction, dir)) > 1 - 1e-6)
            {
                throw regenError("The extrude direction runs along the selected edges; choose a direction across them.", ["directionQuery"], edge);
            }
        }
    }
}
