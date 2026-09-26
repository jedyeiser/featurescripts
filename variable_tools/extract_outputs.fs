FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

/**
 * Extract variables -- PRODUCER library. The one tab a feature in any document imports to
 * publish its outputs to a later "Extract variables" feature (Variable_tools document).
 *
 * A producer ends its body with [embedStandardOutputs] (or the lower-level
 * [embedVariableMap]). The map is stored in a hidden context variable named toString(id) --
 * "[ Fxxx ]", not an identifier, so it never shows under `#`. Queries in the map stay
 * symbolic; nothing here evaluates them.
 *
 * STANDARD KEYS. Every source offers these, embedded or not:
 *     output          bodies the feature made or modified
 *     outputFaces     faces of those bodies
 *     outputEdges     edges of those bodies
 *     outputVertices  vertices of those bodies
 * A producer that embeds may add `inputs` (what it consumed), counts and feature-specific
 * keys. A source that embeds nothing falls back to qCreatedBy(featureId) for each. A feature
 * that only MODIFIES bodies must embed `output`, since qCreatedBy does not find a body it
 * did not create.
 *
 * Descriptors are recognised STRUCTURALLY (an `extractable` marker field), never by type
 * tag: a tag is bound to the declaring module and does not match across library versions,
 * so producers pinned to different versions of this tab stay readable. Keep this tab small
 * and stable -- every producer in every document pins a version of it.
 *
 * Keys a producer publishes are ALWAYS present (use qNothing(), 0, "none"); a key that
 * appears only in some cases turns an extraction into "key not found" after an edit.
 *
 * Contract: extract_variables_schema.md.
 */

/** The standard query keys every source offers, in display order. */
export const STANDARD_OUTPUT_KEYS = ["output", "outputFaces", "outputEdges", "outputVertices"];

/** Marks "no such variable" for [optionalVariable]; not a value any feature publishes. */
const MISSING_VARIABLE = "__missing_variable__";

/**
 * The variable `name`, or undefined when there is none. (getVariable with an undefined
 * default does NOT do this: `{ defaultValue : undefined }` is no default, and it throws.)
 */
export function optionalVariable(context is Context, name is string)
{
    const value = getVariable(context, name, MISSING_VARIABLE);
    return value == MISSING_VARIABLE ? undefined : value;
}

// ---------------------------------------------------------------------------------
// Producer side
// ---------------------------------------------------------------------------------

/**
 * An ordinary value with optional description metadata. Use [extractableVariable].
 *
 * @type {{
 *      @field value : A defined ordinary value; Queries and nested descriptors are excluded.
 *      @field description {string} : @optional Shown by "Print keys" and on the published variable.
 *      @field extractable {string} : The marker "variable".
 * }}
 */
export type ExtractableVariable typecheck canBeExtractableVariable;

export predicate canBeExtractableVariable(value)
{
    value is map;
    value.extractable == "variable";
    value.value != undefined;
    !(value.value is Query);
    !isExtractDescriptor(value.value);
    value.description == undefined || value.description is string;
    for (var field in value)
    {
        isIn(field.key, ["value", "description", "extractable"]);
    }
}

/**
 * A query with optional description and viewport color metadata. Use [extractableQuery].
 *
 * @type {{
 *      @field value {Query} : Query to expose; an empty query is valid.
 *      @field description {string} : @optional Written to the published query variable.
 *      @field debugColor {DebugColor} : @optional Color hint for consumers that highlight the query.
 *      @field extractable {string} : The marker "query".
 * }}
 */
export type ExtractableQuery typecheck canBeExtractableQuery;

export predicate canBeExtractableQuery(value)
{
    value is map;
    value.extractable == "query";
    value.value is Query;
    value.description == undefined || value.description is string;
    value.debugColor == undefined || value.debugColor is DebugColor;
    for (var field in value)
    {
        isIn(field.key, ["value", "description", "debugColor", "extractable"]);
    }
}

/** True for a descriptor map built by [extractableVariable] or [extractableQuery], by structure alone. */
export function isExtractDescriptor(value) returns boolean
{
    return value is map && (value.extractable == "variable" || value.extractable == "query");
}

/**
 * Validated collection of values exposed by a producer: exactly `{ variable : map, query : map }`
 * with nonempty string keys. Keep the `query` half flat -- a Query buried in a nested map in
 * `variable` passes and is then useless to consumers.
 */
export type EmbeddedVariables typecheck canBeEmbeddedVariables;

/** The only gate the consumer side uses. Structural; no type tags. */
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
 * One embedded entry: a descriptor of the right kind, or a plain value (Query in the query
 * half, anything else in the variable half). Structural.
 */
export predicate canBeEmbeddedEntry(value, queryEntry is boolean)
{
    value != undefined;
    if (isExtractDescriptor(value))
    {
        if (queryEntry)
        {
            canBeExtractableQuery(value);
        }
        else
        {
            canBeExtractableVariable(value);
        }
    }
    else
    {
        (value is Query) == queryEntry;
    }
}

export function extractableVariable(value) returns ExtractableVariable
{
    return extractableVariable(value, "");
}

/**
 * Wraps an ordinary value and description for a producer's embedded map.
 *
 * @param value : Any immutable value accepted by [setVariable], except a Query.
 * @param description : Text shown by "Print keys" and on the published variable.
 */
export function extractableVariable(value, description is string) returns ExtractableVariable
precondition
{
    canBeExtractableVariable({ "value" : value, "description" : description, "extractable" : "variable" });
}
{
    return {
                "value" : value,
                "description" : description,
                "extractable" : "variable"
            } as ExtractableVariable;
}

export function extractableQuery(value is Query) returns ExtractableQuery
{
    return extractableQuery(value, "");
}

export function extractableQuery(value is Query, description is string) returns ExtractableQuery
{
    return {
                "value" : value,
                "description" : description,
                "extractable" : "query"
            } as ExtractableQuery;
}

/** Wraps a query, description and debug color. (No (Query, DebugColor) overload: same arity as the description one.) */
export function extractableQuery(value is Query, description is string, color is DebugColor) returns ExtractableQuery
{
    return {
                "value" : value,
                "description" : description,
                "debugColor" : color,
                "extractable" : "query"
            } as ExtractableQuery;
}

/**
 * Embeds the values a producer exposes to a later Extract variables feature, under the
 * hidden slot toString(id). Call once, at the end of the feature body.
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
 * The standard producer embed. Publishes the standard keys for `output` (bodies made or
 * modified) -- output, outputFaces, outputEdges, outputVertices -- plus `inputs`, then any
 * feature-specific variables and queries. Every key is always present.
 *
 * @param standard {{
 *      @field output {Query} : Bodies the feature made or modified.
 *      @field outputDescription {string} : @optional What the bodies are ("The offset wires").
 *      @field inputs {Query} : @optional What the feature consumed; qNothing() when omitted.
 *      @field variables {map} : @optional Feature-specific values (plain or [extractableVariable]).
 *      @field queries {map} : @optional Feature-specific queries (plain or [extractableQuery]).
 * }}
 */
export function embedStandardOutputs(context is Context, id is Id, standard is map)
precondition
{
    standard.output is Query;
    standard.outputDescription == undefined || standard.outputDescription is string;
    standard.inputs == undefined || standard.inputs is Query;
    standard.variables == undefined || standard.variables is map;
    standard.queries == undefined || standard.queries is map;
}
{
    const output = standard.output;
    const what = standard.outputDescription is string && standard.outputDescription != ""
        ? standard.outputDescription
        : "Bodies this feature made or modified";
    var queries = {
            "output" : extractableQuery(output, what ~ ".", DebugColor.GREEN),
            "outputFaces" : extractableQuery(qOwnedByBody(output, EntityType.FACE), "Faces of: " ~ what ~ "."),
            "outputEdges" : extractableQuery(qOwnedByBody(output, EntityType.EDGE), "Edges of: " ~ what ~ "."),
            "outputVertices" : extractableQuery(qOwnedByBody(output, EntityType.VERTEX), "Vertices of: " ~ what ~ "."),
            "inputs" : extractableQuery(standard.inputs is Query ? standard.inputs : qNothing(),
                    "What this feature consumed.", DebugColor.BLUE)
        };
    if (standard.queries is map)
    {
        for (var entry in standard.queries)
        {
            queries[entry.key] = entry.value;
        }
    }
    embedVariableMap(context, id, {
                "variable" : standard.variables is map ? standard.variables : {},
                "query" : queries
            });
}
