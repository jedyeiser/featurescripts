FeatureScript 3008;
import(path : "onshape/std/common.fs", version : "3008.0");

annotation { "Feature Type Name" : "Move Along Edge", "Feature Type Description" : "Takes a body and moves it a specified distance along an edge, keeping orientation " }
export const myFeature = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Body to move", "Filter" : EntityType.BODY || BodyType.MATE_CONNECTOR}
        definition.moveBodies is Query;
        
        annotation { "Name" : "Edge to move along", "Filter" : EntityType.EDGE}
        definition.moveEdge is Query;
        
        annotation { "Name" : "Provide Reference Point" }
        definition.provideRef is boolean;
        
        if (definition.provideRef)
        {
            annotation { "Name" : "Reference Vertex", "Filter" : EntityType.VERTEX, "MaxNumberOfPicks" : 1 }
            definition.refVertex is Query;
        }
        
        annotation { "Name" : "Distance to move" }
        isLength(definition.moveDist, LENGTH_BOUNDS);
        
        annotation { "Name" : "FlipDirection" }
        definition.flipDirection is boolean;
        
        annotation { "Name" : "Copy Bodies?", "Default" : false }
        definition.copyBodies is boolean;
        

    }
    {
        var moveBodies = evaluateQuery(context, qUnion([definition.moveBodies]));
        for (var i = 0; i < size(moveBodies); i += 1)
        {
            var retQ = moveBodyOnCurve(context, id + ('moveBody' ~ i), moveBodies[i], definition.moveEdge, definition);   
        }
        
    });

export function moveBodyOnCurve(context is Context, id is Id, body is Query, edge is Query, definition is map) returns Query
{
    var retQ = qNothing();
    
    var movePath = constructPath(context, qUnion([definition.moveEdge]));
    
    var startQ = body;
    if (definition.provideRef)
    {
        startQ = definition.refVertex;
    }
    
    var startDist = evDistance(context, {
            "side0" : movePath,
            "side1" : startQ
    });
    var startPoint = startDist.sides[0].point;
    var startParam = startDist.sides[0].parameter;
    var edgeLen = evLength(context, {
            "entities" : edge
    });
    var endParam = startParam + definition.moveDist/edgeLen;
    if (definition.flipDirection)
    {
        endParam = startParam - definition.moveDist/edgeLen;   
    }
    
    var edgeCurvatures = evEdgeCurvatures(context, {
            "edge" : edge,
            "parameters" : [startParam, endParam]
    });
    
    var cFrameStart = edgeCurvatures[0].frame;
    var cFrameEnd = edgeCurvatures[1].frame;
    println('cFrameStart = ' ~ cFrameStart);
    println('cFrameEnd = ' ~ cFrameEnd);
    
    var startLine = line(cFrameStart.origin, cFrameStart.zAxis);
    var endLine = line(cFrameEnd.origin, cFrameEnd.zAxis);
    
    var transformQ = body;
    
    if (definition.copyBodies)
    {
        opPattern(context, id + "copyBody", {
                "entities" : body,
                "transforms" : [transform(vector(0, 0, 0) * millimeter)],
                "instanceNames" : ['1']
        });
        
        transformQ = qCreatedBy(id + "copyBody", EntityType.BODY);
    }
    
    opTransform(context, id + "shiftBodyOnEdge", {
            "bodies" : transformQ,
            "transform" : transform(startLine, endLine)
    });
    
    return transformQ;
    
}
