// copied from: https://k2-sports.onshape.com/documents/c3fe41e654ffc2f052a38c8f/v/a81f3c4d03a809b4b6bea87e/e/60f131d05f7b221127ae936f

// Updated: 2026-09-06 22:41:52 CT
FeatureScript 3044;

export import(path : "onshape/std/common.fs", version : "3044.0");
import(path : "onshape/std/queryVariable.fs", version : "3044.0");
import(path : "3c37750af0cf716cb0ede1e0", version : "000000000000000000000000");


/**
 * Reads the extraction map embedded by a selected earlier feature and publishes selected
 * entries as ordinary variables and query variables. The available keys can also be
 * printed without interrupting extraction.
 *
 * @param definition {{
 *      @field sourceFeature {FeatureList} : Earlier features that embedded extraction maps.
 *      @field printAll {boolean} : Whether to print every available key to the FeatureScript Notices pane.
 *      @field prepend {string} : Literal text placed before every published key.
 *      // Reserved UI field for restoring symbolic query evaluation later:
 *      // @field evaluateQueryVariablesOnUse {boolean} : Whether query variables retain symbolic selection logic for later evaluation.
 *      @field getAllVariables {boolean} : Whether every available ordinary variable is extracted.
 *      @field variableSelections {array} : Maps containing ordinary-variable keys and optional output-name overrides.
 *      @field addAllVariables : Button that replaces the variable array with every available ordinary-variable key.
 *      @field asMap {boolean} : Whether ordinary values are published together as one map variable.
 *      @field mapName {string} : Name of the ordinary variable that receives the extracted map.
 *      @field getAllQueries {boolean} : Whether every available query variable is extracted.
 *      @field querySelections {array} : Maps containing `queryKey` strings selected for extraction.
 *      @field addAllQueries : Button that replaces the query array with every available query-variable key.
 *      @field showSelection {boolean} : Whether extracted query entities are highlighted in the viewport.
 * }}
 */
annotation {
        "Feature Type Name" : "Extract Variables",
        "Feature Type Description" : "Extract variables embedded by an earlier compatible feature and optionally print all available keys.",
        "Editing Logic Function" : "extractVariablesEditLogic",
        "UIHint" : UIHint.NO_PREVIEW_PROVIDED,
        "Icon" : icon::BLOB_DATA,
    }
export const extractVariables = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Features", "Description" : "Select one or more earlier features that embed extractable variables and queries." }
        definition.sourceFeature is FeatureList;

        annotation { "Name" : "Print all", "Default" : false, "Description" : "Prints every available variable and query-variable key to the FeatureScript Notices pane. Turn this off after reviewing the list." }
        definition.printAll is boolean;

        annotation { "Name" : "Prepend", "Description" : "Adds this text and an underscore before every published name." }
        definition.prepend is string;

        annotation { "Group Name" : "Variables", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Get all variables", "Default" : true, "Description" : "Extracts every ordinary variable made available by the selected features." }
            definition.getAllVariables is boolean;

            if (!definition.getAllVariables)
            {
                annotation {
                        "Name" : "Variables",
                        "Item name" : "Variable",
                        "Item label template" : "#variableKey"
                    }
                definition.variableSelections is array;

                for (var variableSelection in definition.variableSelections)
                {
                    annotation { "Name" : "Key", "Description" : "The embedded ordinary-variable key to extract." }
                    variableSelection.variableKey is string;

                    annotation { "Name" : "Override name", "Description" : "Publishes this value under a different name." }
                    variableSelection.overrideName is boolean;

                    if (variableSelection.overrideName)
                    {
                        annotation { "Name" : "Name", "UIHint" : [UIHint.UNCONFIGURABLE, UIHint.VARIABLE_NAME], "MaxLength" : 10000, "Description" : "The output name for this extracted value." }
                        variableSelection.newName is string;
                    }
                }

                annotation { "Name" : "Add all variables", "Description" : "Adds every currently available ordinary-variable key to the list." }
                isButton(definition.addAllVariables);
            }

            annotation { "Name" : "As map", "Description" : "Publishes the selected ordinary values as entries in one top-level map variable.", "Default" : true, "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
            definition.asMap is boolean;

            if (definition.asMap)
            {
                annotation { "Name" : "Name", "UIHint" : [UIHint.UNCONFIGURABLE, UIHint.VARIABLE_NAME], "MaxLength" : 10000, "Description" : "The name of the variable containing the extracted map." }
                definition.mapName is string;
            }
        }

        annotation { "Group Name" : "Query Variables", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Get all queries", "Default" : true, "Description" : "Extracts every query variable made available by the selected features." }
            definition.getAllQueries is boolean;

            if (!definition.getAllQueries)
            {
                annotation {
                        "Name" : "Query variables",
                        "Item name" : "Query variable",
                        "Item label template" : "#queryKey"
                    }
                definition.querySelections is array;

                for (var querySelection in definition.querySelections)
                {
                    annotation { "Name" : "Key", "Description" : "The embedded query-variable key to extract." }
                    querySelection.queryKey is string;
                }

                annotation { "Name" : "Add all queries", "Description" : "Adds every currently available query-variable key to the list." }
                isButton(definition.addAllQueries);
            }

            annotation { "Name" : "Show selection", "Default" : false, "Description" : "Highlights the entities contained in the extracted query variables." }
            definition.showSelection is boolean;
        }

        /* Reserved until evaluate-on-use behavior can be exposed without confusing users.
        annotation {
                "Name" : "Evaluate query variables on use",
                "Description" : "When enabled, each query is reevaluated where it is used and may include entities created after this feature. When disabled, each query captures the entities present at this feature.",
                "Default" : false
            }
        definition.evaluateQueryVariablesOnUse is boolean;
        */
    }
    {
        const embeddedVariableMap = getEmbeddedVariableMap(context, definition.sourceFeature);

        if (embeddedVariableMap == undefined)
        {
            reportFeatureWarning(context, id,
                    "Select at least one compatible earlier feature that embeds an extraction map.");
            return;
        }

        if (definition.printAll)
        {
            printAvailableVariables(embeddedVariableMap);
            reportFeatureWarning(context, id,
                    "Print all is on. Check the FeatureScript Notices pane, then turn Print all off. Extract Variables must come after every selected source feature.");
        }

        const availableVariables = embeddedVariableMap.variable;
        const availableQueries = embeddedVariableMap.query;
        var unavailableKeys = [];
        var extractedVariables = {};
        const variableSelections = definition.getAllVariables ? selectionsForAllKeys(availableVariables, "variableKey") : definition.variableSelections;
        for (var variableSelection in variableSelections)
        {
            const variableKey = variableSelection.variableKey;
            if (variableKey == "")
                continue;

            if (availableVariables[variableKey] == undefined ||
                    normalizeEmbeddedValue(availableVariables[variableKey]).value is Query)
            {
                unavailableKeys = append(unavailableKeys, variableKey);
                continue;
            }

            const outputKey = variableSelection.overrideName == true && variableSelection.newName != "" ?
                    variableSelection.newName : variableKey;
            if (definition.asMap)
                extractedVariables[outputKey] = normalizeEmbeddedValue(availableVariables[variableKey]).value;
            else
                publishEmbeddedValue(context, {
                            "name" : prependVariableName(definition.prepend, outputKey),
                            "embeddedValue" : availableVariables[variableKey],
                            "evaluateQueryOnUse" : false
                        });
        }

        if (definition.asMap)
            setVariable(context, prependVariableName(definition.prepend, definition.mapName), extractedVariables,
                    "Ordinary variables extracted from the selected features.");

        const querySelections = definition.getAllQueries ? selectionsForAllKeys(availableQueries, "queryKey") : definition.querySelections;
        for (var querySelection in querySelections)
        {
            const queryKey = querySelection.queryKey;
            if (queryKey == "")
                continue;

            if (availableQueries[queryKey] == undefined ||
                    !(normalizeEmbeddedValue(availableQueries[queryKey]).value is Query))
            {
                unavailableKeys = append(unavailableKeys, queryKey);
                continue;
            }

            publishEmbeddedValue(context, {
                        "name" : prependVariableName(definition.prepend, queryKey),
                        "embeddedValue" : availableQueries[queryKey],
                        // Restore user control with:
                        // "evaluateQueryOnUse" : definition.evaluateQueryVariablesOnUse
                        "evaluateQueryOnUse" : false,
                        "showSelection" : definition.showSelection
                    });
        }

        if (size(unavailableKeys) > 0)
            reportFeatureWarning(context, id,
                    "Some keys are missing or are in the wrong Variables/Query variables list: " ~
                    join(unavailableKeys, ", "));

        if (size(embeddedVariableMap.duplicateVariableKeys) > 0)
            reportFeatureWarning(context, id,
                    "Multiple source features provide these ordinary-variable keys; the first selected feature takes priority: " ~
                    join(embeddedVariableMap.duplicateVariableKeys, ", "));
    }, {
            "sourceFeature" : featureList({}),
            "printAll" : false,
            "prepend" : "",
            // "evaluateQueryVariablesOnUse" : false,
            "getAllVariables" : true,
            "variableSelections" : [],
            "asMap" : true,
            "mapName" : "variables",
            "getAllQueries" : true,
            "querySelections" : [],
            "showSelection" : false
        });

/**
 * Responds to the Add all variables and Add all queries buttons by merging the maps
 * embedded by the selected features. Each button replaces only its corresponding array.
 *
 * @param context : The context immediately before the feature being edited.
 * @param id : The id of the Extract Variables feature being edited.
 * @param oldDefinition : The definition before the current edit.
 * @param definition : The definition after the current edit.
 * @param isCreating : Whether the feature is being created.
 * @param specifiedParameters : Parameters explicitly specified by the user.
 * @param hiddenQueries : Queries supplied to editing logic but hidden from the dialog.
 * @param clickedButton : Identifier of the button that initiated editing logic.
 * @returns {map} : The updated feature definition.
 */
export function extractVariablesEditLogic(context is Context, id is Id, oldDefinition is map,
        definition is map, isCreating is boolean, specifiedParameters is map,
        hiddenQueries is Query, clickedButton is string) returns map
{
    if (clickedButton != "addAllVariables" && clickedButton != "addAllQueries")
        return definition;

    const embeddedVariableMap = getEmbeddedVariableMap(context, definition.sourceFeature);
    if (embeddedVariableMap == undefined)
        return definition;

    if (clickedButton == "addAllVariables")
    {
        var variableSelections = [];
        for (var entry in embeddedVariableMap.variable)
            variableSelections = append(variableSelections, { "variableKey" : entry.key });
        definition.variableSelections = variableSelections;
        return definition;
    }

    var querySelections = [];
    for (var entry in embeddedVariableMap.query)
        querySelections = append(querySelections, { "queryKey" : entry.key });
    definition.querySelections = querySelections;
    return definition;
}

/**
 * Merges the private extraction maps written by the selected source features. The first
 * selected feature wins when ordinary-variable keys collide. Query values with the same
 * key are unioned so every contributing feature remains available through one query
 * variable. Incompatible selected features are ignored; `undefined` is returned when
 * none of the selected features provides a valid map.
 *
 * @param context : The active Part Studio or editing context.
 * @param sourceFeature : A [FeatureList] containing the selected source features.
 * @returns : A merged map with `variable`, `query`, and `duplicateVariableKeys` fields,
 *      or `undefined` when no compatible map can be found.
 */
function getEmbeddedVariableMap(context is Context, sourceFeature)
{
    var mergedVariableMap = {
            "variable" : {},
            "query" : {},
            "duplicateVariableKeys" : []
        };
    var compatibleFeatureCount = 0;

    for (var featureId in keys(sourceFeature))
    {
        const embeddedVariableMap = try silent(getVariable(context, toString(featureId)));
        if (!(embeddedVariableMap is EmbeddedVariables) || !canBeEmbeddedVariables(embeddedVariableMap))
            continue;

        compatibleFeatureCount += 1;
        for (var variableEntry in embeddedVariableMap.variable)
        {
            if (mergedVariableMap.variable[variableEntry.key] == undefined)
                mergedVariableMap.variable[variableEntry.key] = variableEntry.value;
            else if (!isIn(variableEntry.key, mergedVariableMap.duplicateVariableKeys))
                mergedVariableMap.duplicateVariableKeys = append(
                        mergedVariableMap.duplicateVariableKeys, variableEntry.key);
        }

        for (var queryEntry in embeddedVariableMap.query)
        {
            if (mergedVariableMap.query[queryEntry.key] == undefined)
                mergedVariableMap.query[queryEntry.key] = queryEntry.value;
            else
                mergedVariableMap.query[queryEntry.key] = unionEmbeddedQueries(
                        mergedVariableMap.query[queryEntry.key], queryEntry.value);
        }
    }

    return compatibleFeatureCount == 0 ? undefined : mergedVariableMap;
}

/**
 * Unions two embedded query entries while retaining the first entry's description and
 * debug color. If either entry is not a query, the first entry is retained so normal
 * extraction validation can report the malformed key.
 *
 * @param firstEmbeddedQuery : The query entry from the earlier selected feature.
 * @param nextEmbeddedQuery : Another entry stored under the same query key.
 * @returns : An [ExtractableQuery] containing the combined query.
 */
function unionEmbeddedQueries(firstEmbeddedQuery, nextEmbeddedQuery)
{
    const firstNormalizedQuery = normalizeEmbeddedValue(firstEmbeddedQuery);
    const nextNormalizedQuery = normalizeEmbeddedValue(nextEmbeddedQuery);
    if (!(firstNormalizedQuery.value is Query) || !(nextNormalizedQuery.value is Query))
        return firstEmbeddedQuery;

    var combinedQuery = extractableQuery(
            qUnion([firstNormalizedQuery.value, nextNormalizedQuery.value]),
            firstNormalizedQuery.description);
    if (firstNormalizedQuery.debugColor is DebugColor)
        combinedQuery.debugColor = firstNormalizedQuery.debugColor;
    else if (nextNormalizedQuery.debugColor is DebugColor)
        combinedQuery.debugColor = nextNormalizedQuery.debugColor;
    return combinedQuery;
}

/**
 * Builds the same array-item shape used by a manual selection list for every key in a
 * map. This keeps the get-all runtime path identical to the hand-selected path.
 *
 * @param availableValues {map} : Map whose keys should be selected.
 * @param keyField {string} : Array-item field that receives each key.
 * @returns {array} : Selection maps in the source map's iteration order.
 */
function selectionsForAllKeys(availableValues is map, keyField is string) returns array
{
    var selections = [];
    for (var entry in availableValues)
    {
        var selection = {};
        selection[keyField] = entry.key;
        selections = append(selections, selection);
    }
    return selections;
}

/**
 * Applies the optional prefix used by all published values. A separating underscore is
 * inserted only when the prefix is nonempty, preventing leading underscores by default.
 *
 * @param prepend {string} : Optional prefix supplied by the user.
 * @param variableName {string} : Unprefixed variable or query-variable name.
 * @returns {string} : The final published name.
 */
function prependVariableName(prepend is string, variableName is string) returns string
{
    return prepend == "" ? variableName : prepend ~ "_" ~ variableName;
}

/**
 * Prints every available key to the FeatureScript Notices pane. Query-valued entries
 * are labeled separately, and descriptions are included when the feature author has
 * supplied them.
 *
 * @param embeddedVariableMap : The map exposed by the selected source feature.
 */
function printAvailableVariables(embeddedVariableMap is map)
{
    for (var entry in embeddedVariableMap.variable)
    {
        const normalizedValue = normalizeEmbeddedValue(entry.value);
        const descriptionText = normalizedValue.description == "" ? "" : " — " ~ normalizedValue.description;
        println("[Variable] " ~ entry.key ~ descriptionText);
    }

    for (var entry in embeddedVariableMap.query)
    {
        const normalizedValue = normalizeEmbeddedValue(entry.value);
        const descriptionText = normalizedValue.description == "" ? "" : " — " ~ normalizedValue.description;
        println("[Query Variable] " ~ entry.key ~ descriptionText);
    }
}

/**
 * Publishes one normalized embedded entry. Queries become query variables and may be
 * highlighted; all other values become ordinary Part Studio variables. Missing
 * descriptions are intentionally written as empty strings.
 *
 * @param context : The active Part Studio context.
 * @param parameters {{
 *      @field name {string} : Final variable or query-variable name.
 *      @field embeddedValue : A typed descriptor created by the helper studio or a plain value.
 *      @field evaluateQueryOnUse {boolean} : Whether to preserve symbolic query logic instead of capturing current entities.
 *      @field showSelection {boolean} : Whether to highlight the published query in the viewport.
 * }}
 */
function publishEmbeddedValue(context is Context, parameters is map)
{
    const normalizedValue = normalizeEmbeddedValue(parameters.embeddedValue);
    if (normalizedValue.value is Query)
    {
        const queryToPublish = parameters.evaluateQueryOnUse ? normalizedValue.value :
                qUnion(evaluateQuery(context, normalizedValue.value));
        setQueryVariable(context, parameters.name, normalizedValue.description, queryToPublish);
        if (parameters.showSelection == true)
        {
            const fullQuery = qUnion([queryToPublish, qContainedInCompositeParts(queryToPublish)]);
            try silent
            {
                addDebugEntities(context, fullQuery,
                        normalizedValue.debugColor is DebugColor ? normalizedValue.debugColor : DebugColor.YELLOW);
            }
        }
        return;
    }

    setVariable(context, parameters.name, normalizedValue.value, normalizedValue.description);
}

/**
 * Converts typed helper descriptors and plain values into one internal shape. A
 * descriptor without a description is valid and receives an empty string.
 *
 * @param embeddedValue : An entry read from the selected feature's embedded map.
 * @returns {map} : A map with `value`, `description`, and optional `debugColor` fields.
 */
function normalizeEmbeddedValue(embeddedValue) returns map
{
    if (!(embeddedValue is ExtractableVariable) && !(embeddedValue is ExtractableQuery))
        return { "value" : embeddedValue, "description" : "" };

    // Check the fields as well as the tag: a descriptor can be changed after construction.
    if ((embeddedValue is ExtractableVariable && !canBeExtractableVariable(embeddedValue)) ||
            (embeddedValue is ExtractableQuery && !canBeExtractableQuery(embeddedValue)))
        throw "Invalid extraction descriptor in the source feature.";

    var normalizedValue = {
            "value" : embeddedValue.value,
            "description" : embeddedValue.description is string ? embeddedValue.description : ""
        };
    if (embeddedValue.debugColor is DebugColor)
        normalizedValue.debugColor = embeddedValue.debugColor;
    return normalizedValue;
}
