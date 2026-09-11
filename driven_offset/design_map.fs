FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");
// IMPORT: design_map_query_utils.fs
export import(path : "2b6b313ac740a0146d5bef7c", version : "000000000000000000000000");

/**
 * Design map.
 *
 * One feature per design writes one ordinary map variable, `#<mapName>`, whose keys
 * autocomplete in expression fields (`#core.thickness`) and whose Query entries stay
 * symbolic for later custom features (`getVariable(context, "core").bottom`).
 *
 * Inputs are (a) the hidden maps embedded by producer features (driven_edge_offset,
 * evaluate_profiles, ...) via `embedVariableMap` in design_map_query_utils.fs, selected
 * through the `sources` FeatureList, and (b) manual entries: picked/renamed embedded keys,
 * raw selections, typed scalars, and references to keys already in the map.
 *
 * Map shape: `{ "schema" : "designMap/1", "<key>" : Query | ValueWithUnits | number | ... }`.
 * Entries marked "Publish" that hold a Query are also published as query variable
 * `<mapName>_<key>` (std robust freeze) so native dialogs can pick them.
 *
 * Mode "Create map" writes the variable from scratch (overwriting like the std Variable
 * feature). Mode "Add to existing map" reads `#<mapName>` (which must carry the schema
 * key), merges the new keys under the optional "Place under" path, and errors on any key
 * that already exists there. Published query variables are always `<mapName>_<key>`,
 * path excluded.
 *
 * The feature must sit after every producer it lists. Errors fail the feature (and every
 * `#<mapName>.*` reader after it) on purpose: invalid or duplicate key, a Query entry that
 * selects nothing, a reference to an unknown key, a publish name held by an ordinary
 * variable. Unreadable sources, missing embedded keys, duplicate keys across sources and
 * publish on a non-Query only warn.
 */
annotation {
        "Feature Type Name" : "Design map",
        "Feature Type Description" : "Collect embedded producer outputs and manual entries into one map variable.",
        "Feature Name Template" : "###mapName",
        "Editing Logic Function" : "designMapEditLogic",
        "UIHint" : UIHint.NO_PREVIEW_PROVIDED
    }
export const designMap = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Map", "UIHint" : [UIHint.UNCONFIGURABLE, UIHint.VARIABLE_NAME], "MaxLength" : 256, "Description" : "Name of the map variable. Read it as #<map>.<key>." }
        definition.mapName is string;

        annotation { "Name" : "Mode", "Default" : DesignMapMode.CREATE, "UIHint" : UIHint.HORIZONTAL_ENUM }
        definition.mode is DesignMapMode;

        if (definition.mode == DesignMapMode.EXTEND)
        {
            annotation { "Name" : "Place under", "Default" : "", "MaxLength" : 256, "Description" : "Dot-separated key path inside the map, e.g. outputs.deo. Empty = top level. Missing intermediate maps are created." }
            definition.pathInMap is string;
        }

        annotation { "Name" : "Sources", "Description" : "Earlier features that embed a design map (driven_edge_offset, ...). May be empty." }
        definition.sources is FeatureList;

        annotation { "Name" : "Take every key from the sources", "Default" : false, "Description" : "Off: only the entries listed below go into the map. On: every key the source features published lands in the map under its own name; entries below override or rename individual keys." }
        definition.getAllEmbedded is boolean;

        annotation { "Name" : "Entries", "Item name" : "entry", "Item label template" : "#e_kind #e_sourceKey #e_key" }
        definition.entries is array;
        for (var entry in definition.entries)
        {
            designMapEntryPredicate(entry);
        }

        annotation { "Name" : "Add all variables", "Description" : "Append an Embedded entry for every variable key of the sources not already listed." }
        isButton(definition.addAllVariables);

        annotation { "Name" : "Add all queries", "Description" : "Append an Embedded entry for every query key of the sources not already listed." }
        isButton(definition.addAllQueries);

        annotation { "Name" : "Print keys", "Default" : false, "Description" : "Print every available embedded key to the FeatureScript notices. Turn off after reading." }
        definition.printKeys is boolean;

        annotation { "Name" : "Description", "Default" : "", "MaxLength" : 256, "Description" : "Stored on the map variable and on published query variables that have no embedded description." }
        definition.description is string;
    }
    {
        verifyVariableName(context, definition.mapName, "mapName");

        // Where the new keys go, and what is already there.
        var pathSegments = [];
        var target = { "schema" : DESIGN_MAP_SCHEMA };
        if (definition.mode == DesignMapMode.EXTEND)
        {
            pathSegments = splitKeyPath(definition.pathInMap, "pathInMap");
            const missingSentinel = { "designMapMissing" : true };
            target = getVariable(context, definition.mapName, missingSentinel);
            if (target == missingSentinel)
            {
                throw regenError("There is no map variable named '" ~ definition.mapName ~ "' before this feature. Use Create map, or move this feature after the one that creates it.", ["mapName"]);
            }
            if (!(target is map) || target.schema != DESIGN_MAP_SCHEMA)
            {
                throw regenError("Variable '" ~ definition.mapName ~ "' is not a design map (expected schema '" ~ DESIGN_MAP_SCHEMA ~ "').", ["mapName"]);
            }
        }
        var existingAtPath = mapAtPath(target, pathSegments);
        if (existingAtPath == undefined)
        {
            existingAtPath = {};
        }

        const available = readEmbeddedSources(context, definition.sources);
        for (var slotName in available.unreadableSources)
        {
            reportFeatureWarning(context, id, "Source " ~ slotName ~ " has no readable design map (suppressed, deleted, or not a producer); skipped.");
        }

        if (definition.printKeys)
        {
            printEmbeddedKeys(available);
            reportFeatureWarning(context, id, "Print keys is on: " ~ (size(available.variable) + size(available.query)) ~ " keys listed in the FeatureScript notices. Turn it off after reading.");
        }

        // New keys are collected here first, then placed into the target in one step so
        // collisions with existing keys are reported by name.
        var added = {};
        var autoKeys = {};      // keys added by getAllEmbedded, not yet claimed by an entry
        var entryKeys = {};     // keys claimed by entries
        var missingKeys = [];
        var nonQueryPublish = [];
        var overlapKeys = [];   // embedded key present in both the variable and the query half

        if (definition.getAllEmbedded)
        {
            for (var item in available.variable)
            {
                added[item.key] = item.value.value;
                autoKeys[item.key] = true;
            }
            for (var item in available.query)
            {
                if (added[item.key] != undefined)
                {
                    overlapKeys = append(overlapKeys, item.key);
                    continue;
                }
                added[item.key] = item.value.value;
                autoKeys[item.key] = true;
            }
        }

        for (var i = 0; i < size(definition.entries); i += 1)
        {
            const entry = definition.entries[i];
            const key = designMapEntryKey(entry);
            const keyParam = faultyArrayParameterId("entries", i, designMapEntryKeyParameter(entry));

            verifyVariableNameIsValid(key, keyParam);
            if (key == "schema")
            {
                throw regenError("Key 'schema' is reserved.", [keyParam]);
            }
            if (entryKeys[key] == true)
            {
                throw regenError("Key '" ~ key ~ "' is defined twice.", [keyParam]);
            }
            if (existingAtPath[key] != undefined)
            {
                throw regenError("Key '" ~ key ~ "' already exists at " ~ describeKeyPath(pathSegments) ~ " of '" ~ definition.mapName ~ "'.", [keyParam]);
            }

            // REFERENCE sees keys added so far, then keys already at the target location.
            const lookup = mergeMaps(existingAtPath, added);
            const resolved = designMapEntryValue(entry, available, lookup);
            if (resolved == undefined)
            {
                if (entry.e_kind == DesignMapKind.REFERENCE)
                {
                    throw regenError("Entry '" ~ key ~ "' references key '" ~ entry.e_ref ~ "', which is not in the map. Only earlier entries, embedded keys, and keys already at the target location can be referenced.",
                            [faultyArrayParameterId("entries", i, "e_ref")]);
                }
                missingKeys = append(missingKeys, entry.e_sourceKey);
                continue;
            }

            if (resolved.value is Query && isQueryEmpty(context, resolved.value))
            {
                const emptyParam = entry.e_kind == DesignMapKind.QUERY ? "e_selection" : designMapEntryKeyParameter(entry);
                throw regenError("Entry '" ~ key ~ "' selects nothing.", [faultyArrayParameterId("entries", i, emptyParam)]);
            }

            if (entry.e_publish == true)
            {
                if (resolved.value is Query)
                {
                    const qvDescription = resolved.description == "" ? definition.description : resolved.description;
                    publishQueryVariable(context, definition.mapName ~ "_" ~ key, qvDescription, resolved.value, entry.e_evaluateOnUse == true);
                }
                else
                {
                    nonQueryPublish = append(nonQueryPublish, key);
                }
            }

            // A renamed embedded key replaces its auto-added copy; an entry always beats
            // an auto-added key of the same name.
            if (entry.e_kind == DesignMapKind.EMBEDDED && autoKeys[entry.e_sourceKey] == true && entry.e_sourceKey != key)
            {
                added[entry.e_sourceKey] = undefined;
                autoKeys[entry.e_sourceKey] = undefined;
            }
            added[key] = resolved.value;
            entryKeys[key] = true;
            autoKeys[key] = undefined;
        }

        if (size(missingKeys) > 0)
        {
            reportFeatureWarning(context, id, "Embedded keys not found in the sources: " ~ join(missingKeys, ", "));
        }
        if (size(available.duplicateVariableKeys) > 0)
        {
            reportFeatureWarning(context, id, "Several sources embed these variable keys; the first source wins: " ~ join(available.duplicateVariableKeys, ", "));
        }
        if (size(overlapKeys) > 0)
        {
            reportFeatureWarning(context, id, "These keys are embedded both as a variable and as a query; the variable was kept: " ~ join(overlapKeys, ", "));
        }
        if (size(nonQueryPublish) > 0)
        {
            reportFeatureWarning(context, id, "Publish ignored on non-Query entries: " ~ join(nonQueryPublish, ", "));
        }

        // Errors by key name on any collision with keys already at the target location.
        const result = placeInMap(target, pathSegments, added);
        setVariable(context, definition.mapName, result, definition.description);
    }, {
            "mapName" : "design",
            "mode" : DesignMapMode.CREATE,
            "pathInMap" : "",
            "sources" : featureList({}),
            "getAllEmbedded" : false,
            "entries" : [],
            "printKeys" : false,
            "description" : ""
        });

/**
 * Editing logic. Acts only on the two buttons. Reads the sources from the pre-feature
 * context and APPENDS one Embedded entry per available key whose source key is not
 * already listed, so manual and renamed entries survive.
 */
export function designMapEditLogic(context is Context, id is Id, oldDefinition is map,
        definition is map, isCreating is boolean, specifiedParameters is map,
        hiddenQueries is Query, clickedButton is string) returns map
{
    if (clickedButton != "addAllVariables" && clickedButton != "addAllQueries")
    {
        return definition;
    }
    if (!(definition.sources is map))
    {
        return definition;
    }

    const available = readEmbeddedSources(context, definition.sources);
    const pool = clickedButton == "addAllVariables" ? available.variable : available.query;

    var entries = definition.entries is array ? definition.entries : [];
    var listed = {};
    for (var entry in entries)
    {
        if (entry.e_kind == DesignMapKind.EMBEDDED && entry.e_sourceKey is string)
        {
            listed[entry.e_sourceKey] = true;
        }
    }

    for (var item in pool)
    {
        if (listed[item.key] == true)
        {
            continue;
        }
        entries = append(entries, {
                    "e_kind" : DesignMapKind.EMBEDDED,
                    "e_sourceKey" : item.key,
                    "e_rename" : false,
                    "e_key" : item.key,
                    "e_publish" : false,
                    "e_evaluateOnUse" : false
                });
        listed[item.key] = true;
    }

    definition.entries = entries;
    return definition;
}

// ============================================================================
// Extract variables: publish producer values directly, without a map
// ============================================================================

/**
 * Extract variables.
 *
 * Reads the hidden maps embedded by the selected producer features and publishes chosen
 * keys as ordinary variables (`#name`) and query variables (pickable in native dialogs),
 * with no map in between. Use it when a value is wanted under its own name; use Design map
 * when a design's values should live together under one `#map`.
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
            }
            else
            {
                verifyVariableName(context, name, "extractEntries");
                setVariable(context, name, resolved.value, resolved.description);
            }
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
            "printKeys" : false
        });
