//copied from : https://k2-sports.onshape.com/documents/c3fe41e654ffc2f052a38c8f/v/a81f3c4d03a809b4b6bea87e/e/3c37750af0cf716cb0ede1e0

// Updated: 2026-09-06 22:41:09 CT
FeatureScript 3044;

export import(path : "onshape/std/common.fs", version : "3044.0");

/**
 * AUTHOR INSTRUCTIONS
 *
 * Import this Feature Studio into the Feature Studio that will create and embed values and queries.
 *
 * For a curated set of outputs, build one map with `variable` and `query` collections,
 * then embed it once during feature regeneration:
 *
 * ```
 * var extractableValues = {
 *         "variable" : {
 *             "length" : extractableVariable(definition.length,
 *                     "Length produced by this feature.")
 *         },
 *         "query" : {
 *             "result" : extractableQuery(qCreatedBy(id, EntityType.BODY),
 *                     "Bodies produced by this feature.", DebugColor.BLUE)
 *         }
 *     } as EmbeddedVariables;
 *
 * embedVariableMap(context, id, extractableValues);
 * ```
 *
 * Add or omit map entries conditionally before calling [embedVariableMap] when an
 * output is only meaningful in some feature states. Use [extractableVariable] and
 * [extractableQuery] to supply descriptions; query overloads may also supply a
 * [DebugColor]. Plain values and plain queries are valid when no metadata is needed.
 * The wrappers return [ExtractableVariable] and [ExtractableQuery]. Identify them
 * with `is`; call their `canBe...` predicates to validate the current fields.
 * Untagged maps are ordinary data and are never interpreted as metadata wrappers.
 * Cast the completed map with `as EmbeddedVariables` for typecheck diagnostics at its
 * construction site. A cast attaches a type tag and may warn without stopping execution.
 * [embedVariableMap] explicitly checks the predicate before storing either cast or
 * uncast maps, so malformed data fails during the source feature's regeneration.
 *
 * To expose every defined feature parameter instead, call:
 *
 * ```
 * embedFeatureDefinition(context, id, definition);
 * ```
 *
 * That convenience call places [Query] fields under `query`, places all other fields
 * under `variable`, and skips fields set to `undefined`. Choose either
 * [embedVariableMap] or [embedFeatureDefinition] for a feature regeneration; a later
 * call would replace the map written by the earlier call.
 *
 * Users must insert Extract Variables after the compatible feature in the feature tree.
 * Extract Variables can then print the available keys and publish selected entries as
 * ordinary variables and query variables.
 */

/**
 * An ordinary value with optional description metadata. The type tag distinguishes
 * this descriptor from an ordinary map-valued variable. Use [extractableVariable]
 * to construct it. `value is ExtractableVariable` checks the tag;
 * [canBeExtractableVariable] checks the current field structure.
 *
 * @type {{
 *      @field value : A defined ordinary value; Queries and nested extraction descriptors are excluded.
 *      @field description {string} : @optional Description written to the extracted variable.
 * }}
 */
export type ExtractableVariable typecheck canBeExtractableVariable;

/**
 * Checks the complete field structure of an [ExtractableVariable], independently of
 * its type tag. Also accepts untagged descriptor maps for use in preconditions.
 *
 * @param value : The descriptor to validate.
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
 * to construct it. `value is ExtractableQuery` identifies the descriptor by its tag;
 * [canBeExtractableQuery] checks the current field structure without evaluating the query.
 *
 * @type {{
 *      @field value {Query} : Query to publish; an empty query is valid.
 *      @field description {string} : @optional Description written to the query variable.
 *      @field debugColor {DebugColor} : @optional Color used when Show selection is enabled.
 * }}
 */
export type ExtractableQuery typecheck canBeExtractableQuery;

/**
 * Checks the complete field structure of an [ExtractableQuery], independently of its
 * type tag. Accepts untagged descriptor maps and does not resolve the query's entities.
 *
 * @param value : The descriptor to validate.
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
 * Validated collection of values exposed by a feature to Extract Variables.
 * Both collections are required, even when empty. Only the two documented top-level
 * fields are accepted, so misspelled or misplaced collections cannot be ignored.
 * Entries use nonempty string keys. Plain ordinary values, including nested maps and
 * arrays, belong in `variable`; plain [Query] values belong in `query`. Descriptors
 * from [extractableVariable] and [extractableQuery] are also accepted in their
 * respective collections. Descriptions are optional strings; query debug colors
 * are optional [DebugColor] values. An empty query is valid.
 *
 * Cast a completed map with `as EmbeddedVariables` for typecheck diagnostics. The cast
 * alone does not guarantee rejection: [embedVariableMap] enforces the predicate even
 * on already tagged maps. Neither check evaluates queries or verifies entity existence.
 *
 * @type {{
 *      @field variable {map} : Ordinary values or ordinary-value descriptors keyed by output name.
 *      @field query {map} : Queries or query descriptors keyed by output name.
 * }}
 */
export type EmbeddedVariables typecheck canBeEmbeddedVariables;

/**
 * Tests whether a value has the structure required by [EmbeddedVariables]. Use this
 * predicate in an author function's precondition to accept structurally valid maps
 * without requiring callers to cast them first.
 *
 * @param value : The complete extraction map to validate.
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
 * Validates one extraction entry without evaluating its contents. Descriptor type tags
 * distinguish metadata wrappers from ordinary map-valued outputs. Untagged maps remain
 * ordinary data, even when they contain fields named `value` or `description`.
 *
 * @param value : A plain value or typed extraction descriptor.
 * @param queryEntry : Whether this entry belongs to the query collection.
 */
predicate canBeEmbeddedEntry(value, queryEntry is boolean)
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
 * Wraps an ordinary value for inclusion in a feature's embedded extraction map.
 * The wrapper makes room for metadata without changing the value eventually written
 * by Extract Variables.
 *
 * @param value : Any immutable FeatureScript value accepted by [setVariable].
 * @returns {ExtractableVariable} : An extraction descriptor with an empty description.
 */
export function extractableVariable(value) returns ExtractableVariable
{
    return extractableVariable(value, "");
}

/**
 * Wraps an ordinary value and description for inclusion in a feature's embedded
 * extraction map.
 *
 * @param value : Any immutable FeatureScript value accepted by [setVariable].
 * @param description : Text to write to the final Part Studio variable.
 * @returns {ExtractableVariable} : An extraction descriptor consumed by Extract Variables.
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

/**
 * Wraps a query for inclusion in a feature's embedded extraction map. Extract
 * Variables recognizes the stored [Query] and publishes it as a query variable.
 *
 * @param value : The query to publish later.
 * @returns {ExtractableQuery} : A query extraction descriptor with no description or debug color.
 */
export function extractableQuery(value is Query) returns ExtractableQuery
{
    return extractableQuery(value, "");
}

/**
 * Wraps a query and description for inclusion in a feature's embedded extraction map.
 *
 * @param value : The query to publish later.
 * @param description : Text to write to the final query variable.
 * @returns {ExtractableQuery} : A query extraction descriptor without a debug color.
 */
export function extractableQuery(value is Query, description is string) returns ExtractableQuery
{
    return {
                "value" : value,
                "description" : description
            } as ExtractableQuery;
}

/**
 * Wraps a query and debug color for inclusion in a feature's embedded extraction map.
 * The color is used to highlight the query when Extract Variables publishes it.
 *
 * @param value : The query to publish and highlight later.
 * @param color : The debug highlight color.
 * @returns {ExtractableQuery} : A query extraction descriptor with an empty description.
 */
export function extractableQuery(value is Query, color is DebugColor) returns ExtractableQuery
{
    return extractableQuery(value, "", color);
}

/**
 * Wraps a query, description, and debug color for inclusion in a feature's embedded
 * extraction map.
 *
 * @param value : The query to publish and highlight later.
 * @param description : Text to write to the final query variable.
 * @param color : The debug highlight color applied when the query is published.
 * @returns {ExtractableQuery} : A complete query extraction descriptor.
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
 * Embeds every value a feature author wishes to expose to a later Extract Variables
 * feature. The feature id is used as a private context-variable name, allowing a user
 * to select that feature without adding a visible setup variable to the Part Studio.
 *
 * The map must satisfy [EmbeddedVariables], with a `variable` map and a `query` map. Keys inside those maps
 * become the available ordinary-variable and query-variable output names. Values may
 * be created with [extractableVariable] or [extractableQuery].
 *
 * @param context : The active Part Studio context.
 * @param id : The id of the feature exposing the values.
 * @param variableMap {{
 *      @field variable {map} : Ordinary values keyed by available output name.
 *      @field query {map} : Queries keyed by available output name.
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
 * Embeds every defined field from a feature definition without requiring the author
 * to repeat each key. Query-valued fields are placed in the `query` collection and all
 * other fields are placed in the `variable` collection. Fields whose value is
 * `undefined` are omitted, allowing an author to suppress a small number of fields in
 * a copied or merged definition map.
 *
 * This convenience function writes blank descriptions and does not assign debug
 * colors. Authors who need that metadata should construct descriptors with
 * [extractableVariable] and [extractableQuery], then call [embedVariableMap]. The
 * supplied definition map is read but never mutated.
 *
 * @param context : The active Part Studio context.
 * @param id : The id of the feature exposing its definition values.
 * @param definition : Feature definition fields to expose. `undefined` fields are skipped.
 */
export function embedFeatureDefinition(context is Context, id is Id, definition is map)
{
    var variableValues = {};
    var queryValues = {};

    for (var entry in definition)
    {
        if (entry.value == undefined)
            continue;

        if (entry.value is Query)
            queryValues[entry.key] = extractableQuery(entry.value);
        else
            variableValues[entry.key] = extractableVariable(entry.value);
    }

    embedVariableMap(context, id, {
                "variable" : variableValues,
                "query" : queryValues
            });
}
