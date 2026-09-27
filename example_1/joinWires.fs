FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: join_wires_icon.svg (feature icon)
IconNamespace::import(path : "18bb73fa2033d423c7d6871b", version : "69e0690da68b4bd87c85e127");

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Join wires", "Feature Type Description" : "Joins the selected edges and wires into one wire (exact geometry, edges kept). Optionally deletes the input wire bodies." }
export const joinWires = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Edges and wires", "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE) }
        definition.edgeWireSelection is Query;

        annotation { "Name" : "Keep seed bodies" }
        definition.keepSeeds is boolean;

        annotation { "Name" : "Name", "Default" : "", "MaxLength" : 128,
                    "Description" : "Name for the joined wire (numbered when there are several). Empty keeps the default name." }
        definition.wireName is string;
    }
    {
        // Selected edges plus every edge of the selected wire bodies.
        var wireBodies = qEntityFilter(definition.edgeWireSelection, EntityType.BODY);
        var edges = qUnion([qEntityFilter(definition.edgeWireSelection, EntityType.EDGE),
                    qOwnedByBody(wireBodies, EntityType.EDGE)]);
        if (isQueryEmpty(context, edges))
        {
            throw regenError("Select edges or wire bodies to join.", ["edgeWireSelection"]);
        }

        opExtractWires(context, id + "opExtractWires1", {
                "edges" : edges
        });

        var wires = qCreatedBy(id + "opExtractWires1", EntityType.BODY);
        var wireCount = size(evaluateQuery(context, wires));
        if (wireCount > 1)
        {
            reportFeatureWarning(context, id, "The selection does not form one chain: " ~ wireCount ~ " wires were created (gaps or branches in the input).");
        }
        else if (size(evaluateQuery(context, qOwnedByBody(wires, EntityType.EDGE))) == size(evaluateQuery(context, qOwnedByBody(wires, EntityType.VERTEX))))
        {
            // One wire with as many vertices as edges: a closed loop.
            reportFeatureInfo(context, id, "The joined wire is a closed loop.");
        }

        if (definition.wireName != "")
        {
            const joined = evaluateQuery(context, wires);
            for (var i = 0; i < size(joined); i += 1)
            {
                setProperty(context, {
                        "entities" : joined[i],
                        "propertyType" : PropertyType.NAME,
                        "value" : size(joined) == 1 ? definition.wireName : definition.wireName ~ " " ~ (i + 1)
                });
            }
        }

        // Only wire bodies picked directly are consumed; sketch bodies are never deleted.
        if (!definition.keepSeeds)
        {
            var seeds = qSketchFilter(wireBodies, SketchObject.NO);
            if (!isQueryEmpty(context, seeds))
            {
                opDeleteBodies(context, id + "deleteBodies1", {
                        "entities" : seeds
                });
            }
        }
    }, {
        "keepSeeds" : false,
        "wireName" : ""
    });
