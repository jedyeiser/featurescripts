FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

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

annotation { "Feature Type Name" : "Extrude edge", "Feature Type Description" : "Takes a wire body or a set of edges as input with standard exrude parameters and creates an extruded surface", "Editing Logic Function" : "extrudeEdgeEditingLogic" }
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
    if (definition.extrudeDirectionFrom == ExtrudeEdgeDirectionType.VECTOR)
    {
        return normalize(vector(definition.vectorX, definition.vectorY, definition.vectorZ));
    }   
    else if (definition.extrudeDirectionFrom == ExtrudeEdgeDirectionType.QUERY)
    {
        if (!isQueryEmpty(context, qBodyType(definition.directionQuery, BodyType.MATE_CONNECTOR)))
        {
            var mcDef = evMateConnector(context, {
                    "mateConnector" : definition.directionQuery
            });
            
            return (definition.flipDir) ? -1 * mcDef.zAxis : mcDef.zAxis;
        }
        else if (!isQueryEmpty(context, qGeometry(definition.directionQuery, GeometryType.LINE)))
        {
            var dirLine = evLine(context, {
                    "edge" : definition.directionQuery
            });
            
            return (definition.flipDir) ? -1 * dirLine.direction : dirLine.direction;
        }
        else if (!isQueryEmpty(context, qGeometry(definition.directionQuery, GeometryType.PLANE)))
        {
            var dirPlane = evPlane(context, {
                    "face" : definition.directionQuery
            });
            
            return (definition.flipDir) ? -1 * dirPlane.normal : dirPlane.normal;
        }
    }
}


