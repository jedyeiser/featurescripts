FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");
// IMPORT: extract_variables_utils.fs (re-exports extract_outputs.fs)
export import(path : "a4dcd70ce9ceec588536fb0c", version : "");
// IMPORT: extract_variables_icon.svg (feature icon)
IconNamespace::import(path : "1fec12e049522ab98f8177ff", version : "9c7ddae6a84d0131481b57c4");


/**
 * Extract variables.
 *
 * One per block of the tree. Takes the block's features as sources and publishes what later
 * features need -- ordinary variables (`#name`) and query variables (pickable in any native
 * dialog) -- so the block's hand-made Query Variable features can go.
 *
 * Sources need not be producers: every source offers the standard keys output /
 * outputFaces / outputEdges / outputVertices (embedded by a producer, else what the feature
 * created). Producers add their own keys. "Add keys from sources" lists them all.
 *
 * Entry types: a source key as is; filtered (entity type, body type, name, largest N);
 * closest to a point; edges shared by two sources; a chain end; the edges of a chain
 * between two points; bridging curve input (the end edge and its vertex); a region (the faces
 * around a point, flood-filled up to boundary edges, or that region's boundary). Composed entries
 * hold the entities present now (std robust freeze); a source key may re-evaluate on use.
 *
 * Names: `<prefix>_<name>`, the name defaulting to the key ("output@2" -> "output_2").
 * Notices: one per regeneration; a warning only when an entry could not be published.
 */
annotation { "Icon" : IconNamespace::BLOB_DATA,
        "Feature Type Name" : "Extract variables",
        "Feature Type Description" : "Publish variables and query variables from a block of features: their standard outputs, embedded keys, or composed selections.",
        "UIHint" : UIHint.NO_PREVIEW_PROVIDED,
        "Editing Logic Function" : "extractVariablesEditLogic"
    }
export const extractVariables = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Sources", "Description" : "The features of this block. Any feature works; producers add their own keys." }
        definition.sources is FeatureList;

        annotation { "Name" : "Prefix", "Default" : "", "MaxLength" : 256, "Description" : "Put in front of every published name, joined with an underscore. Empty = none." }
        definition.prefix is string;

        annotation { "Name" : "Add keys from sources", "Description" : "Add a Source key entry for every key the sources offer that is not listed yet (faces, vertices and inputs excepted). Existing entries are kept." }
        isButton(definition.addKeys);

        annotation { "Name" : "Entries", "Item name" : "entry", "Item label template" : "#x_name #x_sourceKey" }
        definition.extractEntries is array;
        for (var entry in definition.extractEntries)
        {
            annotation { "Name" : "Type", "Default" : ExtractEntryType.SOURCE_KEY }
            entry.x_type is ExtractEntryType;

            annotation { "Name" : "Source key", "MaxLength" : 256, "Description" : "A key the sources offer: output, outputEdges, ... or a producer's own. key@n picks source n when several offer it." }
            entry.x_sourceKey is string;

            if (entry.x_type == ExtractEntryType.SHARED_EDGES || entry.x_type == ExtractEntryType.REGION)
            {
                annotation { "Name" : "Second source key", "MaxLength" : 256,
                            "Description" : "Shared edges: the other key. Region: the key holding the boundary edges (empty = the seed face's whole connected patch)." }
                entry.x_secondKey is string;
            }

            annotation { "Name" : "Published name", "Default" : "", "MaxLength" : 256, "Description" : "Empty = the key." }
            entry.x_name is string;

            annotation { "Name" : "Show", "Default" : false,
                        "Description" : "Highlight what this entry resolves to while the feature is being edited (in the producer's colour when it gives one), and print the count or value to the FeatureScript notices." }
            entry.x_show is boolean;

            if (entry.x_type == ExtractEntryType.FILTERED || entry.x_type == ExtractEntryType.CLOSEST)
            {
                annotation { "Name" : "Entity type", "Default" : ExtractEntityType.EDGE }
                entry.x_entityType is ExtractEntityType;
            }

            if (entry.x_type == ExtractEntryType.FILTERED)
            {
                annotation { "Name" : "Body type", "Default" : ExtractBodyType.ANY }
                entry.x_bodyType is ExtractBodyType;

                annotation { "Name" : "Body name contains", "Default" : "", "MaxLength" : 256, "Description" : "Keep only entities whose body name contains this (any case). Empty = all." }
                entry.x_nameContains is string;

                annotation { "Name" : "Keep only the largest", "Default" : false }
                entry.x_keepLargest is boolean;

                if (entry.x_keepLargest)
                {
                    annotation { "Name" : "How many" }
                    isInteger(entry.x_largestCount, { (unitless) : [1, 1, 1000] } as IntegerBoundSpec);
                }
            }

            if (entry.x_type == ExtractEntryType.CLOSEST || entry.x_type == ExtractEntryType.CHAIN_END ||
                entry.x_type == ExtractEntryType.BRIDGING || entry.x_type == ExtractEntryType.EDGES_BETWEEN ||
                entry.x_type == ExtractEntryType.REGION)
            {
                annotation { "Name" : "Point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
                entry.x_point is Query;
            }

            if (entry.x_type == ExtractEntryType.EDGES_BETWEEN)
            {
                annotation { "Name" : "Second point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
                entry.x_point2 is Query;

                annotation { "Name" : "Other side", "Default" : false, "Description" : "Closed chains only: take the longer way round." }
                entry.x_otherSide is boolean;
            }

            if (entry.x_type == ExtractEntryType.REGION)
            {
                annotation { "Name" : "Publish", "Default" : ExtractRegionOutput.FACES }
                entry.x_regionOutput is ExtractRegionOutput;
            }

            if (entry.x_type == ExtractEntryType.CHAIN_END)
            {
                annotation { "Name" : "End", "Default" : ExtractEndRule.NEAREST }
                entry.x_endRule is ExtractEndRule;

                annotation { "Name" : "Publish", "Default" : ExtractEndOutput.VERTEX }
                entry.x_endOutput is ExtractEndOutput;
            }

            if (entry.x_type == ExtractEntryType.SOURCE_KEY)
            {
                annotation { "Name" : "Evaluate on use", "Default" : false, "Description" : "Queries only. Off: hold the entities present now. On: re-resolve wherever used." }
                entry.x_evaluateOnUse is boolean;
            }
        }

        annotation { "Name" : "Print keys", "Default" : false, "Description" : "Print every key the sources offer to the FeatureScript notices." }
        definition.printKeys is boolean;

        annotation { "Name" : "Manifest", "Default" : "", "MaxLength" : 256, "Description" : "Optional. Name of one ordinary map variable recording what this feature published, for API readers. Empty = none." }
        definition.manifest is string;
    }
    {
        const available = readSources(context, definition.sources);
        var problems = [];
        if (definition.printKeys)
        {
            printAvailableKeys(available);
        }

        var published = {};
        var manifest = { "schema" : EXTRACT_MANIFEST_SCHEMA, "variables" : {}, "queries" : [] };
        var emptyEntries = [];
        for (var i = 0; i < size(definition.extractEntries); i += 1)
        {
            const entry = definition.extractEntries[i];
            const label = "Entry " ~ (i + 1);
            if (entry.x_sourceKey == "")
            {
                problems = append(problems, label ~ ": no source key.");
                continue;
            }
            const resolved = resolveEntry(context, available, entry);
            if (resolved.error != undefined)
            {
                problems = append(problems, label ~ " (" ~ entry.x_sourceKey ~ "): " ~ resolved.error ~ ".");
                continue;
            }

            const base = entry.x_name != "" ? entry.x_name : addressToName(entry.x_sourceKey);
            const name = definition.prefix == "" ? base : definition.prefix ~ "_" ~ base;
            const nameParameter = faultyArrayParameterId("extractEntries", i, "x_name");
            if (published[name] == true)
            {
                throw regenError(label ~ ": '" ~ name ~ "' is already published by an earlier entry.", [nameParameter]);
            }
            published[name] = true;
            if (entry.x_show == true)
            {
                showEntry(context, name, resolved, i);
            }

            if (resolved.kind == "query")
            {
                if (isQueryEmpty(context, resolved.value))
                {
                    emptyEntries = append(emptyEntries, name);
                }
                publishQueryVariable(context, name, resolved.description, resolved.value, resolved.evaluateOnUse, nameParameter);
                manifest.queries = append(manifest.queries, name);
            }
            else
            {
                verifyVariableName(context, name, nameParameter);
                setVariable(context, name, resolved.value, resolved.description);
                manifest.variables[name] = resolved.value;
            }
        }

        if (definition.manifest != "")
        {
            if (published[definition.manifest] == true)
            {
                throw regenError("The manifest name '" ~ definition.manifest ~ "' is also a published name.", ["manifest"]);
            }
            verifyVariableName(context, definition.manifest, "manifest");
            setVariable(context, definition.manifest, manifest, "Extract variables manifest");
        }

        // One notice: a warning only when something asked for was not published.
        if (size(problems) > 0)
        {
            reportFeatureWarning(context, id, join(problems, " "));
        }
        else if (size(published) == 0)
        {
            reportFeatureInfo(context, id, "Nothing published yet: press Add keys from sources, or add entries.");
        }
        else
        {
            reportFeatureInfo(context, id, "Published " ~ size(published) ~ " name(s)"
                ~ (size(emptyEntries) > 0 ? "; empty at this point in the tree: " ~ join(emptyEntries, ", ") : "") ~ ".");
        }
    }, {
            "sources" : featureList({}),
            "prefix" : "",
            "extractEntries" : [],
            "printKeys" : false,
            "manifest" : ""
        });

/** Colours for shown entries whose producer gives none, cycled by entry. */
const SHOW_COLORS = [DebugColor.RED, DebugColor.GREEN, DebugColor.BLUE, DebugColor.MAGENTA, DebugColor.ORANGE, DebugColor.CYAN, DebugColor.YELLOW];

/**
 * An entry's "Show": its entities highlighted (debug entities are drawn while the feature is
 * being edited), plus one notices line with the count or the value.
 */
function showEntry(context is Context, name is string, resolved is map, index is number)
{
    if (resolved.kind != "query")
    {
        println("[show] " ~ name ~ " = " ~ toString(resolved.value));
        return;
    }
    const color = resolved.debugColor is DebugColor ? resolved.debugColor : SHOW_COLORS[index % size(SHOW_COLORS)];
    const entities = evaluateQuery(context, resolved.value);
    println("[show] " ~ name ~ ": " ~ size(entities) ~ " entit" ~ (size(entities) == 1 ? "y" : "ies") ~ " (" ~ toString(color) ~ ")");
    if (size(entities) > 0)
    {
        addDebugEntities(context, qUnion(entities), color);
    }
}

/**
 * "Add keys from sources": one Source key entry per key the sources offer that no entry
 * lists yet, in source order. Existing entries, names and choices are left as they are.
 * The context is the one before this feature, so the sources' embedded maps are readable.
 */
export function extractVariablesEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map, hiddenBodies is Query, clickedButton is string) returns map
{
    if (clickedButton != "addKeys")
    {
        return definition;
    }
    const available = readSources(context, definition.sources);
    var listed = {};
    for (var entry in definition.extractEntries)
    {
        if (entry.x_type == ExtractEntryType.SOURCE_KEY)
        {
            listed[entry.x_sourceKey] = true;
        }
    }
    var result = definition;
    for (var address in available.order)
    {
        // Faces, vertices and inputs are plumbing; add them by hand when a block needs them.
        const key = available.keys[address].key;
        if (listed[address] == true || isIn(key, ["outputFaces", "outputVertices", "inputs"]))
        {
            continue;
        }
        result.extractEntries = append(result.extractEntries, {
                    "x_type" : ExtractEntryType.SOURCE_KEY,
                    "x_sourceKey" : address,
                    "x_name" : "",
                    "x_evaluateOnUse" : false
                });
    }
    return result;
}
