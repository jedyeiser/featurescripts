FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");



annotation { "Feature Type Name" : "Join wires", "Feature Type Description" : "Takes a selection of wires and joins them" }
export const joinWires = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Edges and wires", "Filter" : (EntityType.BODY && BodyType.WIRE)}
        definition.edgeWireSelection is Query;
        
        annotation { "Name" : "Keep seed bodies" }
        definition.keepSeeds is boolean;
        
        
    }
    {
        
        var inputBodies = evaluateQuery(context, qUnion([qEntityFilter(definition.edgeWireSelection, EntityType.BODY)]));
        var extractEdges = [];
        
        for (var i = 0; i < size(inputBodies); i += 1)
        {
            var bodyEdges = evaluateQuery(context, qUnion([qOwnedByBody(inputBodies[i], EntityType.EDGE)]));
            for (var j = 0; j < size(bodyEdges); j += 1)
            {
                extractEdges = append(extractEdges, bodyEdges[j]);
            }
        }
        
        extractEdges = qUnion(extractEdges);
        
        opExtractWires(context, id + "opExtractWires1", {
                "edges" : extractEdges
        });
        
        if (!definition.keepSeeds)
        {
            var bodyQ = qUnion([qEntityFilter(definition.edgeWireSelection, EntityType.BODY)]);
            if (!isQueryEmpty(context, bodyQ))
            {
                opDeleteBodies(context, id + "deleteBodies1", {
                        "entities" : bodyQ
                });
            }
        }
        
    });
