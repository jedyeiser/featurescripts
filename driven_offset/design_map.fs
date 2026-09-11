FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");
// IMPORT: design_map_query_utils.fs
export import(path : "2b6b313ac740a0146d5bef7c", version : "000000000000000000000000");


/**
 * Extract variables.
 *
 * Reads the hidden maps embedded by the selected producer features and publishes chosen
 * keys as ordinary variables (`#name`) and query variables (pickable in native dialogs),
 * with no map in between. Scalars that should live together under one `#map` belong in a
 * std Variable feature holding a map literal; the optional Manifest records what was
 * published here so an API reader can find it.
 *
 * Names: `<prefix>_<key>` when a prefix is given, else `<key>`; an entry may rename.
 * Queries are published with the std robust freeze unless "Evaluate on use" is on.
 * Errors: invalid or duplicate names, a name already held by the other variable kind.
 * Warnings: unreadable sources, keys not found, cross-source duplicates.
 */
annotation {
        "Feature Type Name" : "Extract variables",
        "Feature Type Description" : "Publish values embedded by producer features as variables and query variables.",
        "UIHint" : UIHint.NO_PREVIEW_PROVIDED
    }
export const extractVariables = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Sources", "Description" : "Earlier features that embed values (driven_edge_offset, ...)." }
        definition.sources is FeatureList;

        annotation { "Name" : "Prefix", "Default" : "", "MaxLength" : 256, "Description" : "Put in front of every published name, joined with an underscore. Empty = none." }
        definition.prefix is string;

        annotation { "Name" : "Extract every key", "Default" : false, "Description" : "Off: only the entries listed below are published. On: every key the sources published is, under its own name." }
        definition.extractAll is boolean;

        annotation { "Name" : "Entries", "Item name" : "entry", "Item label template" : "#x_sourceKey #x_name" }
        definition.extractEntries is array;
        for (var entry in definition.extractEntries)
        {
            annotation { "Name" : "Key in source feature", "MaxLength" : 256, "Description" : "Name of a value a source feature published (turn on Print keys to list them)." }
            entry.x_sourceKey is string;

            annotation { "Name" : "Rename", "Default" : false }
            entry.x_rename is boolean;

            if (entry.x_rename)
            {
                annotation { "Name" : "Variable name", "MaxLength" : 256 }
                entry.x_name is string;
            }

            annotation { "Name" : "Evaluate on use", "Default" : false, "Description" : "Queries only. Off: hold the entities present now. On: re-resolve wherever used." }
            entry.x_evaluateOnUse is boolean;
        }

        annotation { "Name" : "Print keys", "Default" : false, "Description" : "Print every available key to the FeatureScript notices. Turn off after reading." }
        definition.printKeys is boolean;

        annotation { "Name" : "Manifest", "Default" : "", "MaxLength" : 256, "Description" : "Optional. Name of one ordinary map variable that records what this feature published (values by name, query variable names) for API readers. Empty = none." }
        definition.manifest is string;
    }
    {
        const available = readEmbeddedSources(context, definition.sources);
        for (var slotName in available.unreadableSources)
        {
            reportFeatureWarning(context, id, "Source " ~ slotName ~ " has no readable embedded map (suppressed, deleted, or not a producer); skipped.");
        }
        if (definition.printKeys)
        {
            printEmbeddedKeys(available);
            reportFeatureWarning(context, id, "Print keys is on: " ~ (size(available.variable) + size(available.query)) ~ " keys listed in the FeatureScript notices. Turn it off after reading.");
        }

        // key -> { "name", "evaluateOnUse" }; "Extract every key" first, entries override.
        var selected = {};
        if (definition.extractAll)
        {
            for (var item in available.variable)
            {
                selected[item.key] = { "name" : item.key, "evaluateOnUse" : false };
            }
            for (var item in available.query)
            {
                selected[item.key] = { "name" : item.key, "evaluateOnUse" : false };
            }
        }
        var missingKeys = [];
        for (var i = 0; i < size(definition.extractEntries); i += 1)
        {
            const entry = definition.extractEntries[i];
            if (findEmbeddedKey(available, entry.x_sourceKey) == undefined)
            {
                missingKeys = append(missingKeys, entry.x_sourceKey);
                continue;
            }
            const name = entry.x_rename == true ? entry.x_name : entry.x_sourceKey;
            selected[entry.x_sourceKey] = { "name" : name, "evaluateOnUse" : entry.x_evaluateOnUse == true };
        }

        var published = {};
        var manifest = { "schema" : EXTRACT_MANIFEST_SCHEMA, "variables" : {}, "queries" : [] };
        for (var item in selected)
        {
            const name = definition.prefix == "" ? item.value.name : definition.prefix ~ "_" ~ item.value.name;
            if (published[name] == true)
            {
                throw regenError("Name '" ~ name ~ "' would be published twice.", ["extractEntries"]);
            }
            published[name] = true;

            const resolved = findEmbeddedKey(available, item.key);
            if (resolved.value is Query)
            {
                publishQueryVariable(context, name, resolved.description, resolved.value, item.value.evaluateOnUse);
                manifest.queries = append(manifest.queries, name);
            }
            else
            {
                verifyVariableName(context, name, "extractEntries");
                setVariable(context, name, resolved.value, resolved.description);
                manifest.variables[name] = resolved.value;
            }
        }
        if (definition.manifest != "")
        {
            verifyVariableName(context, definition.manifest, "manifest");
            setVariable(context, definition.manifest, manifest, "Extract variables manifest");
        }

        if (size(missingKeys) > 0)
        {
            reportFeatureWarning(context, id, "Keys not found in the sources: " ~ join(missingKeys, ", "));
        }
        if (size(available.duplicateVariableKeys) > 0)
        {
            reportFeatureWarning(context, id, "Several sources embed these variable keys; the first source wins: " ~ join(available.duplicateVariableKeys, ", "));
        }
        if (size(published) == 0)
        {
            reportFeatureWarning(context, id, "Nothing published: list entries or turn on Extract every key.");
        }
    }, {
            "sources" : featureList({}),
            "prefix" : "",
            "extractAll" : false,
            "extractEntries" : [],
            "printKeys" : false,
            "manifest" : ""
        });
