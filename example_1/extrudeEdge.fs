FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: extrude_edge_icon.svg (feature icon)
IconNamespace::import(path : "767e9ea707a86fb82e710e5a", version : "8038a27c525caa815e3bf7da");

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
    
    return definition;
}

export const VectorInputBounds = {(unitless) : [-1, 0, 1]} as RealBoundSpec;

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Extrude edge", "Feature Type Description" : "Takes a wire body or a set of edges as input with standard extrude parameters and creates an extruded surface", "Editing Logic Function" : "extrudeEdgeEditingLogic" }
export const extrudeEdge = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Input type", "Default" : ExtrudeEdgeInputType.BODIES, "UIHint" : UIHint.HORIZONTAL_ENUM }
        definition.inputType is ExtrudeEdgeInputType;
        
        if (definition.inputType == ExtrudeEdgeInputType.BODIES)
        {
            annotation { "Name" : "Wire body", "Filter" : EntityType.BODY && BodyType.WIRE, "MaxNumberOfPicks" : 1 }
            definition.wireBody is Query;
            
        }
        
        else if (definition.inputType == ExtrudeEdgeInputType.EDGES)
        {
            annotation { "Name" : "Edges", "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 10 }
            definition.selEdges is Query;
            
        }
        
        annotation { "Name" : "Extrude direction from", "Default" : ExtrudeEdgeDirectionType.QUERY, "UIHint" : UIHint.SHOW_LABEL }
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
            
        }
        
        annotation { "Name" : "Extrude length" }
        isLength(definition.extrudeLength, LENGTH_BOUNDS);
        
        annotation { "Name" : "Second direction", "Default" : false }
        definition.secondDirection is boolean;
        
        if (definition.secondDirection)
        {
            annotation { "Name" : "Direction 2 length" }
            isLength(definition.dir2Len, LENGTH_BOUNDS);
            
        } 
        
    }
    {
        var extrudeDir = extractDir(context, definition);
        var extrudeEdges = (definition.inputType == ExtrudeEdgeInputType.BODIES) ? qUnion([qOwnedByBody(definition.wireBody, EntityType.EDGE)]) : qUnion([definition.selEdges]);
        if (isQueryEmpty(context, extrudeEdges))
        {
            throw regenError("Select the edges or wire body to extrude.", [definition.inputType == ExtrudeEdgeInputType.BODIES ? "wireBody" : "selEdges"]);
        }
        checkDirectionAcrossEdges(context, extrudeEdges, extrudeDir);
        
        var extrudeDef = {
                "entities" : extrudeEdges,
                "direction" : extrudeDir,
                "endBound" : BoundingType.BLIND,
                "endDepth" : definition.extrudeLength
        };
        
        if (definition.secondDirection)
        {
            extrudeDef = mergeMaps(extrudeDef, {'startDepth' : definition.dir2Len, 'startBound' : BoundingType.BLIND});
        }
        
        opExtrude(context, id + "extrude1", extrudeDef);
    
    
    });

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
        return normalize(v);
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

// Extruding along an edge's own tangent makes a zero-area face and the kernel fails with EXTRUDE_FAILED;
// catch it up front with a message that says what to change.
function checkDirectionAcrossEdges(context is Context, edges is Query, dir is Vector)
{
    for (var edge in evaluateQuery(context, edges))
    {
        for (var line in evEdgeTangentLines(context, { "edge" : edge, "parameters" : [0, 0.5, 1] }))
        {
            if (abs(dot(line.direction, dir)) > 1 - 1e-6)
            {
                throw regenError("The extrude direction runs along the selected edges; choose a direction across them.", ["directionQuery"], edge);
            }
        }
    }
}
