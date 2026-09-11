FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");
// common.fs does not re-export queryVariable.fs; needed for setQueryVariable.
import(path : "onshape/std/queryVariable.fs", version : "3070.0");

/**
 * Design map support library.
 *
 * Two halves live here:
 *
 * 1. The embed library (ported from Reese's "IMPORT THIS" studio, FS 3044 -> 3070).
 *    A producer feature builds one map { "variable" : {...}, "query" : {...} } and calls
 *    [embedVariableMap] (or [embedFeatureDefinition]) at the end of its body. The map is
 *    stored in a hidden context variable named toString(id), which is not an identifier
 *    and therefore never shows up under `#` in expression fields. Queries in the map stay
 *    symbolic; nothing here evaluates them.
 *
 * 2. The consumer helpers used by the "Design map" feature (design_map.fs): reading and
 *    merging the hidden maps of a FeatureList of producers, the entry kind enum and the
 *    entry array predicate, kind dispatch, and query-variable publishing with the std
 *    robust freeze.
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

/** Value of the `schema` key written into every design map. Bump on breaking changes. */
export const DESIGN_MAP_SCHEMA = "designMap/1";

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
 * Embeds every value a producer feature wishes to expose to a later Design map feature.
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
// Consumer side: entry array
// ---------------------------------------------------------------------------------

/** What one Design map entry contributes to the map. */
export enum DesignMapKind
{
    annotation { "Name" : "Embedded" }
    EMBEDDED,
    annotation { "Name" : "Query" }
    QUERY,
    annotation { "Name" : "Length" }
    LENGTH,
    annotation { "Name" : "Angle" }
    ANGLE,
    annotation { "Name" : "Number" }
    NUMBER,
    annotation { "Name" : "Reference" }
    REFERENCE
}

/** Whether the Design map feature creates a new map variable or adds to an existing one. */
export enum DesignMapMode
{
    annotation { "Name" : "Create map" }
    CREATE,
    annotation { "Name" : "Add to existing map" }
    EXTEND
}

/**
 * Splits a dot-separated key path ("outputs.deo") into its segments. An empty or
 * whitespace-only string means the top level and yields []. Every segment must be an
 * identifier; the caller passes the parameter id used for the error.
 */
export function splitKeyPath(pathInMap is string, faultyParameter is string) returns array
{
    const trimmed = replace(pathInMap, "^\\s+|\\s+$", "");
    if (trimmed == "")
    {
        return [];
    }
    const segments = splitByPeriod(trimmed);
    for (var segment in segments)
    {
        verifyVariableNameIsValid(segment, faultyParameter);
    }
    return segments;
}

/** Splits on "."; empty segments are kept so they fail identifier validation. */
export function splitByPeriod(text is string) returns array
{
    return splitByRegexp(text, "[.]");
}

/** Renders a path for messages: "outputs.deo", or "the top level" for []. */
export function describeKeyPath(pathSegments is array) returns string
{
    if (size(pathSegments) == 0)
    {
        return "the top level";
    }
    return "'" ~ join(pathSegments, ".") ~ "'";
}

/**
 * Returns the map found at `pathSegments` inside `target`, or undefined when any segment
 * is missing or not a map. Used for REFERENCE lookups against an existing map.
 */
export function mapAtPath(target is map, pathSegments is array)
{
    var current = target;
    for (var segment in pathSegments)
    {
        if (!(current[segment] is map))
        {
            return undefined;
        }
        current = current[segment];
    }
    return current;
}

/**
 * Pure: returns a copy of `target` with every key of `entries` inserted at the nested
 * location named by `pathSegments` ([] = top level). Intermediate maps are created when
 * missing. Throws a regenError when an intermediate exists but is not a map, or when a key
 * of `entries` already exists at the target location (no silent overwrite).
 */
export function placeInMap(target is map, pathSegments is array, entries is map) returns map
{
    return placeInMapAt(target, pathSegments, 0, entries);
}

/** Recursive worker for [placeInMap]; `depth` is the index of the next segment to descend. */
export function placeInMapAt(target is map, pathSegments is array, depth is number, entries is map) returns map
{
    var out = target;
    if (depth >= size(pathSegments))
    {
        for (var item in entries)
        {
            if (out[item.key] != undefined)
            {
                throw regenError("Key '" ~ item.key ~ "' already exists at " ~ describeKeyPath(pathSegments) ~ " of the map.");
            }
            out[item.key] = item.value;
        }
        return out;
    }

    const segment = pathSegments[depth];
    var child = out[segment];
    if (child == undefined)
    {
        child = {};
    }
    else if (!(child is map))
    {
        throw regenError("'" ~ join(subArray(pathSegments, 0, depth + 1), ".") ~ "' exists in the map but is not a map.");
    }
    out[segment] = placeInMapAt(child, pathSegments, depth + 1, entries);
    return out;
}

/** Bounds for a NUMBER entry, matching the std Variable feature. */
export const DESIGN_MAP_NUMBER_BOUNDS = { (unitless) : [-1e12, 0, 1e12] } as RealBoundSpec;

/**
 * Predicate for one item of the Design map `entries` array. Item parameter ids are prefixed
 * `e_` so they cannot collide with any top-level parameter (array item ids must be unique
 * across the whole feature).
 *
 * EMBEDDED   : `e_sourceKey` names a key from the merged sources; `e_rename` + `e_key`
 *              store it under another name.
 * QUERY      : `e_selection` is stored symbolically under `e_key`.
 * LENGTH / ANGLE / NUMBER : the typed value is stored under `e_key`.
 * REFERENCE  : `result[e_ref]` (an earlier entry or an auto-added embedded key) is copied
 *              under `e_key`.
 * Every kind : `e_publish` also publishes a Query value as query variable `<map>_<key>`.
 */
export predicate designMapEntryPredicate(entry is map)
{
    annotation { "Name" : "Kind", "Default" : DesignMapKind.EMBEDDED }
    entry.e_kind is DesignMapKind;

    if (entry.e_kind == DesignMapKind.EMBEDDED)
    {
        annotation { "Name" : "Source key", "MaxLength" : 256, "Description" : "A key embedded by one of the source features. Turn on Print keys to list them." }
        entry.e_sourceKey is string;

        annotation { "Name" : "Rename", "Default" : false, "Description" : "Store the embedded value under a different key." }
        entry.e_rename is boolean;
    }

    // Declared exactly once: a parameter may not appear in two branches of a precondition.
    if (entry.e_kind != DesignMapKind.EMBEDDED || entry.e_rename)
    {
        annotation { "Name" : "Key", "MaxLength" : 256, "Description" : "Key in the design map. Must be an identifier." }
        entry.e_key is string;
    }

    if (entry.e_kind != DesignMapKind.EMBEDDED)
    {
        if (entry.e_kind == DesignMapKind.QUERY)
        {
            annotation { "Name" : "Selection", "Filter" : EntityType.EDGE || EntityType.FACE || EntityType.VERTEX || EntityType.BODY }
            entry.e_selection is Query;
        }
        else if (entry.e_kind == DesignMapKind.LENGTH)
        {
            annotation { "Name" : "Length" }
            isLength(entry.e_length, LENGTH_BOUNDS);
        }
        else if (entry.e_kind == DesignMapKind.ANGLE)
        {
            annotation { "Name" : "Angle" }
            isAngle(entry.e_angle, ANGLE_360_BOUNDS);
        }
        else if (entry.e_kind == DesignMapKind.NUMBER)
        {
            annotation { "Name" : "Number" }
            isReal(entry.e_number, DESIGN_MAP_NUMBER_BOUNDS);
        }
        else if (entry.e_kind == DesignMapKind.REFERENCE)
        {
            annotation { "Name" : "Reference key", "MaxLength" : 256, "Description" : "A key already in this map: an earlier entry or an embedded key." }
            entry.e_ref is string;
        }
    }

    annotation { "Name" : "Publish as query variable", "Default" : false, "Description" : "Also create query variable <map>_<key> so native dialogs can pick it. Query values only." }
    entry.e_publish is boolean;
}

/** The key under which an entry lands in the map. */
export function designMapEntryKey(entry is map) returns string
{
    if (entry.e_kind == DesignMapKind.EMBEDDED && entry.e_rename != true)
    {
        return entry.e_sourceKey;
    }
    return entry.e_key;
}

/** The inner parameter id that carries an entry's key, for error reporting. */
export function designMapEntryKeyParameter(entry is map) returns string
{
    if (entry.e_kind == DesignMapKind.EMBEDDED && entry.e_rename != true)
    {
        return "e_sourceKey";
    }
    return "e_key";
}

/**
 * Kind dispatch for one entry.
 *
 * @param entry : One item of `entries`.
 * @param available : Result of [readEmbeddedSources].
 * @param result : The map built so far (REFERENCE looks here).
 * @returns : `{ value, description }`, or undefined when an EMBEDDED source key or a
 *      REFERENCE target does not exist.
 */
export function designMapEntryValue(entry is map, available is map, result is map)
{
    const kind = entry.e_kind;
    if (kind == DesignMapKind.EMBEDDED)
    {
        return findEmbeddedKey(available, entry.e_sourceKey);
    }
    if (kind == DesignMapKind.QUERY)
    {
        return { "value" : entry.e_selection, "description" : "" };
    }
    if (kind == DesignMapKind.LENGTH)
    {
        return { "value" : entry.e_length, "description" : "" };
    }
    if (kind == DesignMapKind.ANGLE)
    {
        return { "value" : entry.e_angle, "description" : "" };
    }
    if (kind == DesignMapKind.NUMBER)
    {
        return { "value" : entry.e_number, "description" : "" };
    }
    if (kind == DesignMapKind.REFERENCE)
    {
        if (result[entry.e_ref] == undefined)
        {
            return undefined;
        }
        return { "value" : result[entry.e_ref], "description" : "" };
    }
    return undefined;
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
    verifyVariableNameIsValid(name, "mapName");
    var exists = false;
    try silent
    {
        getVariable(context, name);
        exists = true;
    }
    if (exists)
    {
        throw regenError(ErrorStringEnum.QUERY_VARIABLE_NAME_ALREADY_USED_IN_NON_QUERY_VARIABLE, ["mapName"]);
    }
}

/**
 * Publishes `q` as query variable `name` after checking the name. The stored query is the
 * std robust freeze `qUnion(makeRobustQueriesBatched(context, q))`: the entities present
 * now, each tracked through identity-preserving edits. This is what the std Query variable
 * feature stores when "Evaluate on use" is off; it is NOT `qUnion(evaluateQuery(...))`,
 * whose transient ids die at the next context change.
 */
export function publishQueryVariable(context is Context, name is string, description is string, q is Query)
{
    checkQueryVariableName(context, name);
    setQueryVariable(context, name, description, qUnion(makeRobustQueriesBatched(context, q)));
}
