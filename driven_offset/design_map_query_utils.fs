FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");
// common.fs does not re-export queryVariable.fs; needed for setQueryVariable.
import(path : "onshape/std/queryVariable.fs", version : "3070.0");

/**
 * Extract variables support library (Evan Reese's "Extract Variables" approach, ported to
 * FS 3070 and kept as our own code).
 *
 * Two halves live here:
 *
 * 1. The embed library (producer side). A producer feature builds one map
 *    { "variable" : {...}, "query" : {...} } and calls [embedVariableMap] (or
 *    [embedFeatureDefinition]) at the end of its body. The map is stored in a hidden context
 *    variable named toString(id), which is not an identifier and therefore never shows up
 *    under `#` in expression fields. Queries in the map stay symbolic; nothing here
 *    evaluates them.
 *
 * 2. The consumer helpers used by the "Extract variables" feature (design_map.fs tab):
 *    reading and merging the hidden maps of a FeatureList of producers, printing the
 *    available keys, and query-variable publishing with the std robust freeze.
 *
 * Type tags (ExtractableVariable, ExtractableQuery, EmbeddedVariables) are kept for
 * producer-side typecheck diagnostics only. The consumer path gates on the structural
 * predicates (canBeEmbeddedVariables etc.), never on `is EmbeddedVariables`, because a
 * tag is bound to the declaring module and may not match across library elements.
 *
 * Producer usage:
 *
 *     embedVariableMap(context, id, {
 *         "variable" : {
 *             "chainLength" : extractableVariable(totalLength, "Summed length of the offset edges"),
 *             "linkCount" : 3
 *         },
 *         "query" : {
 *             "output" : extractableQuery(qCreatedBy(id, EntityType.BODY), "All output wires"),
 *             "offsetEdges" : definition.offsetEdges
 *         }
 *     });
 *
 * Conditional keys are added or omitted, never set to undefined (undefined entries are
 * rejected by the predicate).
 */

/** Value of the `schema` key written into an Extract variables manifest. Bump on breaking changes. */
export const EXTRACT_MANIFEST_SCHEMA = "extractManifest/1";

// ---------------------------------------------------------------------------------
// Embed library (producer side)
// ---------------------------------------------------------------------------------

/**
 * An ordinary value with optional description metadata. Use [extractableVariable] to
 * construct it. `value is ExtractableVariable` checks the tag; [canBeExtractableVariable]
 * checks the current field structure.
 *
 * @type {{
 *      @field value : A defined ordinary value; Queries and nested descriptors are excluded.
 *      @field description {string} : @optional Description shown by "Print keys" and written to a published query variable.
 * }}
 */
export type ExtractableVariable typecheck canBeExtractableVariable;

/**
 * Checks the complete field structure of an [ExtractableVariable], independently of its
 * type tag. Also accepts untagged descriptor maps for use in preconditions.
 */
export predicate canBeExtractableVariable(value)
{
    value is map;
    value.value != undefined;
    !(value.value is Query);
    !(value.value is ExtractableVariable);
    !(value.value is ExtractableQuery);
    value.description == undefined || value.description is string;
    for (var field in value)
    {
        isIn(field.key, ["value", "description"]);
    }
}

/**
 * A query with optional description and viewport color metadata. Use [extractableQuery]
 * to construct it. [canBeExtractableQuery] checks the field structure without evaluating
 * the query.
 *
 * @type {{
 *      @field value {Query} : Query to expose; an empty query is valid.
 *      @field description {string} : @optional Description written to the published query variable.
 *      @field debugColor {DebugColor} : @optional Color hint for consumers that highlight the query.
 * }}
 */
export type ExtractableQuery typecheck canBeExtractableQuery;

/**
 * Checks the complete field structure of an [ExtractableQuery], independently of its type
 * tag. Accepts untagged descriptor maps and does not resolve the query's entities.
 */
export predicate canBeExtractableQuery(value)
{
    value is map;
    value.value is Query;
    value.description == undefined || value.description is string;
    value.debugColor == undefined || value.debugColor is DebugColor;
    for (var field in value)
    {
        isIn(field.key, ["value", "description", "debugColor"]);
    }
}

/**
 * Validated collection of values exposed by a producer feature. Both collections are
 * required, even when empty, and only the two documented top-level fields are accepted.
 * Entries use nonempty string keys. Plain ordinary values (including nested maps and
 * arrays) belong in `variable`; plain [Query] values belong in `query`. Descriptors from
 * [extractableVariable] and [extractableQuery] are also accepted in their respective
 * collections.
 *
 * Neither the tag nor the predicate evaluates queries or verifies entity existence, and
 * the predicate does not inspect nested containers: a Query buried inside a nested map in
 * `variable` passes and is then useless to consumers. Keep the `query` half flat.
 *
 * @type {{
 *      @field variable {map} : Ordinary values or ordinary-value descriptors keyed by output name.
 *      @field query {map} : Queries or query descriptors keyed by output name.
 * }}
 */
export type EmbeddedVariables typecheck canBeEmbeddedVariables;

/**
 * Tests whether a value has the structure required by [EmbeddedVariables]. This is the
 * only gate the consumer side uses.
 */
export predicate canBeEmbeddedVariables(value)
{
    annotation { "Message" : "EmbeddedVariables must be a map." }
    value is map;
    annotation { "Message" : "EmbeddedVariables requires a variable map and a query map." }
    value.variable is map;
    value.query is map;
    annotation { "Message" : "Only variable and query are allowed at the top level of EmbeddedVariables." }
    size(value) == 2;

    for (var entry in value.variable)
    {
        annotation { "Message" : "Embedded variable keys must be nonempty strings." }
        entry.key is string;
        entry.key != "";
        annotation { "Message" : "The variable collection requires ordinary values or valid ordinary-value descriptors." }
        canBeEmbeddedEntry(entry.value, false);
    }
    for (var entry in value.query)
    {
        annotation { "Message" : "Embedded query keys must be nonempty strings." }
        entry.key is string;
        entry.key != "";
        annotation { "Message" : "The query collection requires Queries or valid query descriptors." }
        canBeEmbeddedEntry(entry.value, true);
    }
}

/**
 * Validates one embedded entry without evaluating its contents. Descriptor type tags
 * distinguish metadata wrappers from ordinary map-valued outputs; untagged maps remain
 * ordinary data even when they contain fields named `value` or `description`.
 *
 * @param value : A plain value or typed descriptor.
 * @param queryEntry : Whether this entry belongs to the query collection.
 */
export predicate canBeEmbeddedEntry(value, queryEntry is boolean)
{
    value != undefined;
    if (value is ExtractableVariable)
    {
        !queryEntry;
        canBeExtractableVariable(value);
    }
    else if (value is ExtractableQuery)
    {
        queryEntry;
        canBeExtractableQuery(value);
    }
    else
    {
        (value is Query) == queryEntry;
    }
}

/**
 * Wraps an ordinary value for inclusion in a producer's embedded map, with an empty
 * description.
 */
export function extractableVariable(value) returns ExtractableVariable
{
    return extractableVariable(value, "");
}

/**
 * Wraps an ordinary value and description for inclusion in a producer's embedded map.
 *
 * @param value : Any immutable FeatureScript value accepted by [setVariable], except a Query.
 * @param description : Text shown by "Print keys".
 */
export function extractableVariable(value, description is string) returns ExtractableVariable
precondition
{
    canBeExtractableVariable({ "value" : value, "description" : description });
}
{
    return {
                "value" : value,
                "description" : description
            } as ExtractableVariable;
}

/** Wraps a query for inclusion in a producer's embedded map, with no description or color. */
export function extractableQuery(value is Query) returns ExtractableQuery
{
    return extractableQuery(value, "");
}

/** Wraps a query and description for inclusion in a producer's embedded map. */
export function extractableQuery(value is Query, description is string) returns ExtractableQuery
{
    return {
                "value" : value,
                "description" : description
            } as ExtractableQuery;
}

/**
 * Wraps a query, description, and debug color for inclusion in a producer's embedded map.
 * (Reese's (Query, DebugColor) overload is not ported: same arity as the description
 * overload, which fscheck flags; pass "" as the description instead.)
 */
export function extractableQuery(value is Query, description is string, color is DebugColor) returns ExtractableQuery
{
    return {
                "value" : value,
                "description" : description,
                "debugColor" : color
            } as ExtractableQuery;
}

/**
 * Embeds every value a producer feature wishes to expose to a later Extract variables feature.
 * The feature id, rendered by toString, is used as the context-variable name; it is not an
 * identifier, so the slot is unreachable from `#` and adds no visible setup variable.
 *
 * Call this once, at the end of the feature body. A later call in the same regeneration
 * replaces the map written by the earlier one.
 *
 * @param context : The active Part Studio context.
 * @param id : The id of the feature exposing the values.
 * @param variableMap {{
 *      @field variable {map} : Ordinary values (or descriptors) keyed by output name.
 *      @field query {map} : Queries (or descriptors) keyed by output name.
 * }}
 */
export function embedVariableMap(context is Context, id is Id, variableMap is map)
precondition
{
    canBeEmbeddedVariables(variableMap);
}
{
    setVariable(context, toString(id), variableMap as EmbeddedVariables);
}

/**
 * Embeds every defined field of a feature definition. Query-valued fields land in the
 * `query` collection, everything else in `variable`; fields whose value is `undefined`
 * are skipped. Descriptions are blank. The supplied definition is never mutated.
 *
 * Choose either this or [embedVariableMap] for a regeneration, not both.
 */
export function embedFeatureDefinition(context is Context, id is Id, definition is map)
{
    var variableValues = {};
    var queryValues = {};

    for (var entry in definition)
    {
        if (entry.value == undefined)
        {
            continue;
        }
        if (entry.value is Query)
        {
            queryValues[entry.key] = extractableQuery(entry.value);
        }
        else
        {
            variableValues[entry.key] = extractableVariable(entry.value);
        }
    }

    embedVariableMap(context, id, {
                "variable" : variableValues,
                "query" : queryValues
            });
}

// ---------------------------------------------------------------------------------
// Consumer side: reading and merging producer maps
// ---------------------------------------------------------------------------------

/**
 * Converts a typed descriptor or a plain value into one shape. A descriptor without a
 * description receives an empty string. Throws if a descriptor carries a tag but no longer
 * has the matching field structure (it was edited after construction).
 *
 * @param embeddedValue : One entry read from a producer's embedded map.
 * @returns {map} : `{ "value", "description" }` plus `debugColor` when the descriptor had one.
 */
export function normalizeEmbeddedValue(embeddedValue) returns map
{
    if (!(embeddedValue is ExtractableVariable) && !(embeddedValue is ExtractableQuery))
    {
        return { "value" : embeddedValue, "description" : "" };
    }

    if ((embeddedValue is ExtractableVariable && !canBeExtractableVariable(embeddedValue)) ||
        (embeddedValue is ExtractableQuery && !canBeExtractableQuery(embeddedValue)))
    {
        throw "Invalid extraction descriptor in the source feature.";
    }

    var normalized = {
            "value" : embeddedValue.value,
            "description" : embeddedValue.description is string ? embeddedValue.description : ""
        };
    if (embeddedValue.debugColor is DebugColor)
    {
        normalized.debugColor = embeddedValue.debugColor;
    }
    return normalized;
}

/**
 * Unions two normalized query entries stored under the same key by different producers.
 * The first entry's description (and color) is retained. If either value is not a Query
 * the first entry is returned unchanged so the malformed key surfaces downstream.
 */
export function unionNormalizedQueries(first is map, next is map) returns map
{
    if (!(first.value is Query) || !(next.value is Query))
    {
        return first;
    }
    var combined = {
            "value" : qUnion([first.value, next.value]),
            "description" : first.description
        };
    if (first.debugColor is DebugColor)
    {
        combined.debugColor = first.debugColor;
    }
    else if (next.debugColor is DebugColor)
    {
        combined.debugColor = next.debugColor;
    }
    return combined;
}

/**
 * Reads the hidden map of every feature in `sourceFeatures` (a FeatureList; only its keys
 * are used) and merges them. Every stored entry is normalized to `{ value, description }`.
 *
 * Merge rules (Reese): variable keys first-wins, with later duplicates collected in
 * `duplicateVariableKeys`; query keys are `qUnion`ed across sources. A source whose slot is
 * absent (suppressed, deleted, not a producer) or fails [canBeEmbeddedVariables] is skipped
 * and listed in `unreadableSources`. The gate is the predicate only, never the type tag.
 *
 * This is the single place where a `try silent` read is used: [getVariable] throws when
 * the slot is absent.
 *
 * @param context : The current context, or the pre-feature context in editing logic.
 * @param sourceFeatures : The FeatureList parameter value (a map from Id to function).
 * @returns {map} : {
 *      "variable" : { key : { value, description } },
 *      "query" : { key : { value, description } },
 *      "duplicateVariableKeys" : [string],
 *      "unreadableSources" : [string],
 *      "sourceCount" : number of sources that yielded a valid map
 * }
 */
export function readEmbeddedSources(context is Context, sourceFeatures is map) returns map
{
    var merged = {
            "variable" : {},
            "query" : {},
            "duplicateVariableKeys" : [],
            "unreadableSources" : [],
            "sourceCount" : 0
        };

    for (var featureId in keys(sourceFeatures))
    {
        const slotName = toString(featureId);
        const embedded = try silent(getVariable(context, slotName));
        if (embedded == undefined || !canBeEmbeddedVariables(embedded))
        {
            merged.unreadableSources = append(merged.unreadableSources, slotName);
            continue;
        }

        merged.sourceCount += 1;
        for (var variableEntry in embedded.variable)
        {
            if (merged.variable[variableEntry.key] == undefined)
            {
                merged.variable[variableEntry.key] = normalizeEmbeddedValue(variableEntry.value);
            }
            else if (!isIn(variableEntry.key, merged.duplicateVariableKeys))
            {
                merged.duplicateVariableKeys = append(merged.duplicateVariableKeys, variableEntry.key);
            }
        }

        for (var queryEntry in embedded.query)
        {
            const normalized = normalizeEmbeddedValue(queryEntry.value);
            if (merged.query[queryEntry.key] == undefined)
            {
                merged.query[queryEntry.key] = normalized;
            }
            else
            {
                merged.query[queryEntry.key] = unionNormalizedQueries(merged.query[queryEntry.key], normalized);
            }
        }
    }

    return merged;
}

/**
 * Looks up one embedded key in the merged sources: the variable half first, then the
 * query half.
 *
 * @returns : `{ value, description }` or undefined when the key is in neither half.
 */
export function findEmbeddedKey(available is map, sourceKey is string)
{
    if (available.variable[sourceKey] != undefined)
    {
        return available.variable[sourceKey];
    }
    return available.query[sourceKey];
}

/**
 * Prints every available embedded key to the FeatureScript notices, one line each:
 * `[Variable] key -- description` and `[Query] key -- description`.
 */
export function printEmbeddedKeys(available is map)
{
    for (var entry in available.variable)
    {
        println("[Variable] " ~ entry.key ~ describeSuffix(entry.value.description));
    }
    for (var entry in available.query)
    {
        println("[Query] " ~ entry.key ~ describeSuffix(entry.value.description));
    }
}

/** " -- description", or "" when the description is blank. */
export function describeSuffix(description) returns string
{
    if (!(description is string) || description == "")
    {
        return "";
    }
    return " -- " ~ description;
}

// ---------------------------------------------------------------------------------
// Consumer side: publishing
// ---------------------------------------------------------------------------------

/**
 * Throws if `name` is not a valid identifier or is already held by an ordinary variable
 * (copy of the private std check used by the Query variable feature).
 */
export function checkQueryVariableName(context is Context, name is string)
{
    verifyVariableNameIsValid(name, "extractEntries");
    var exists = false;
    try silent
    {
        getVariable(context, name);
        exists = true;
    }
    if (exists)
    {
        throw regenError(ErrorStringEnum.QUERY_VARIABLE_NAME_ALREADY_USED_IN_NON_QUERY_VARIABLE, ["extractEntries"]);
    }
}

/**
 * Publishes `q` as query variable `name` after checking the name. With `evaluateOnUse` off
 * the stored query is the std robust freeze `qUnion(makeRobustQueriesBatched(context, q))`:
 * the entities present now, each tracked through identity-preserving edits (what the std
 * Query variable feature stores when "Evaluate on use" is off; NOT `qUnion(evaluateQuery)`,
 * whose transient ids die at the next context change). With it on, `q` is stored symbolic
 * and re-resolves wherever it is used.
 */
export function publishQueryVariable(context is Context, name is string, description is string, q is Query, evaluateOnUse is boolean)
{
    checkQueryVariableName(context, name);
    const stored = evaluateOnUse ? q : qUnion(makeRobustQueriesBatched(context, q));
    setQueryVariable(context, name, description, stored);
}
