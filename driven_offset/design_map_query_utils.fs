FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");
// common.fs does not re-export queryVariable.fs; needed for setQueryVariable.
import(path : "onshape/std/queryVariable.fs", version : "3070.0");

/**
 * Extract variables support library (Evan Reese's "Extract Variables" approach, ported to
 * FS 3070 and grown into our own).
 *
 * 1. Producer side. A producer feature ends its body with [embedStandardOutputs] (or the
 *    lower-level [embedVariableMap]). The map is stored in a hidden context variable named
 *    toString(id) -- "[ Fxxx ]", not an identifier, so it never shows under `#`. Queries in
 *    the map stay symbolic; nothing here evaluates them.
 *
 * 2. Consumer side, used by the "Extract variables" feature (design_map.fs tab): reading a
 *    FeatureList of sources -- with or without an embedded map -- into one table of
 *    addressable keys, resolving the entry types (source key, filtered, closest to point,
 *    shared edges, chain end, edges between points, bridging curve input), and publishing
 *    ordinary variables and query variables with the std robust freeze.
 *
 * STANDARD KEYS. Every source offers these, embedded or not:
 *     output          bodies the feature made or modified
 *     outputFaces     faces of those bodies
 *     outputEdges     edges of those bodies
 *     outputVertices  vertices of those bodies
 * A producer that embeds may add `inputs` (what it consumed), counts and feature-specific
 * keys. A source that embeds nothing falls back to qCreatedBy(featureId) for each: "filter
 * the output of any feature". A feature that only MODIFIES bodies must embed `output`,
 * since qCreatedBy does not find a body it did not create.
 *
 * Descriptors are recognised STRUCTURALLY (an `extractable` marker field), never by type
 * tag: a tag is bound to the declaring module and does not match across library versions,
 * which would make a producer pinned to another version of this tab unreadable.
 *
 * Keys a producer publishes are ALWAYS present (use qNothing(), 0, "none"); a key that
 * appears only in some cases turns an extraction into "key not found" after an edit.
 */

/** Value of the `schema` key written into an Extract variables manifest. Bump on breaking changes. */
export const EXTRACT_MANIFEST_SCHEMA = "extractManifest/2";

/** The standard query keys every source offers, in display order. */
export const STANDARD_OUTPUT_KEYS = ["output", "outputFaces", "outputEdges", "outputVertices"];

// ---------------------------------------------------------------------------------
// Entry types (enums used by the Extract variables precondition)
// ---------------------------------------------------------------------------------

/** What one Extract variables entry publishes. */
export enum ExtractEntryType
{
    annotation { "Name" : "Source key" }
    SOURCE_KEY,
    annotation { "Name" : "Filtered" }
    FILTERED,
    annotation { "Name" : "Closest to point" }
    CLOSEST,
    annotation { "Name" : "Shared edges" }
    SHARED_EDGES,
    annotation { "Name" : "Chain end" }
    CHAIN_END,
    annotation { "Name" : "Edges between points" }
    EDGES_BETWEEN,
    annotation { "Name" : "Bridging curve input" }
    BRIDGING
}

export enum ExtractEntityType
{
    annotation { "Name" : "Bodies" }
    BODY,
    annotation { "Name" : "Faces" }
    FACE,
    annotation { "Name" : "Edges" }
    EDGE,
    annotation { "Name" : "Vertices" }
    VERTEX
}

export enum ExtractBodyType
{
    annotation { "Name" : "Any" }
    ANY,
    annotation { "Name" : "Solid" }
    SOLID,
    annotation { "Name" : "Sheet" }
    SHEET,
    annotation { "Name" : "Wire" }
    WIRE
}

export enum ExtractEndRule
{
    annotation { "Name" : "Nearest the point" }
    NEAREST,
    annotation { "Name" : "Farthest from the point" }
    FARTHEST
}

export enum ExtractEndOutput
{
    annotation { "Name" : "Vertex" }
    VERTEX,
    annotation { "Name" : "Edge" }
    EDGE,
    annotation { "Name" : "Edge and vertex" }
    BOTH
}

// ---------------------------------------------------------------------------------
// Embed library (producer side)
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

// ---------------------------------------------------------------------------------
// Consumer side: reading sources into addressable keys
// ---------------------------------------------------------------------------------

/**
 * `{ value, description, debugColor? }` from a descriptor or a plain value. Structural.
 */
export function normalizeEmbeddedValue(embeddedValue) returns map
{
    if (!isExtractDescriptor(embeddedValue))
    {
        return { "value" : embeddedValue, "description" : "" };
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
 * Reads every source (a FeatureList; keys only) into one table of addressable keys.
 *
 * Each source contributes its embedded keys (when it embedded a valid map) and, for any
 * standard key it did not embed, the implicit qCreatedBy(featureId) fallback -- so a source
 * that publishes nothing still offers output / outputFaces / outputEdges / outputVertices.
 *
 * Sources are numbered 1.. in tree order. A key offered by exactly one source is addressed
 * by its name ("output"); a key offered by several is addressed "key@n" for each ("output@2"),
 * and the bare name is ambiguous. Never merged silently.
 *
 * @returns {map} : {
 *      "keys" : { address : { key, source, kind ("variable" | "query"), value, description, implicit } },
 *      "order" : [address] in source order, then embedded before implicit,
 *      "ambiguous" : { key : [addresses] },
 *      "sources" : [{ index, slot, embedded }]
 * }
 */
export function readSources(context is Context, sourceFeatures is map) returns map
{
    var idOf = {};
    for (var featureId in keys(sourceFeatures))
    {
        idOf[featureId] = featureId;
    }
    const ordered = valuesSortedById(context, idOf);

    // First pass: every (source, key) offered.
    var offers = [];
    var sources = [];
    for (var n = 0; n < size(ordered); n += 1)
    {
        const featureId = ordered[n];
        const slot = toString(featureId);
        const embedded = getVariable(context, slot, undefined);
        const valid = embedded != undefined && canBeEmbeddedVariables(embedded);
        sources = append(sources, { "index" : n + 1, "slot" : slot, "embedded" : valid });

        var offeredHere = {};
        if (valid)
        {
            for (var entry in embedded.variable)
            {
                offers = append(offers, mergeMaps(normalizeEmbeddedValue(entry.value),
                            { "key" : entry.key, "source" : n + 1, "kind" : "variable", "implicit" : false }));
                offeredHere[entry.key] = true;
            }
            for (var entry in embedded.query)
            {
                offers = append(offers, mergeMaps(normalizeEmbeddedValue(entry.value),
                            { "key" : entry.key, "source" : n + 1, "kind" : "query", "implicit" : false }));
                offeredHere[entry.key] = true;
            }
        }
        const implicit = implicitOutputs(featureId);
        for (var key in STANDARD_OUTPUT_KEYS)
        {
            if (offeredHere[key] != true)
            {
                offers = append(offers, { "key" : key, "source" : n + 1, "kind" : "query", "implicit" : true,
                            "value" : implicit[key],
                            "description" : "Created by source " ~ (n + 1) ~ " (" ~ key ~ ")." });
            }
        }
    }

    // Second pass: addresses.
    var count = {};
    for (var offer in offers)
    {
        count[offer.key] = (count[offer.key] == undefined ? 0 : count[offer.key]) + 1;
    }
    var table = {};
    var order = [];
    var ambiguous = {};
    for (var offer in offers)
    {
        const address = count[offer.key] == 1 ? offer.key : offer.key ~ "@" ~ offer.source;
        table[address] = offer;
        order = append(order, address);
        if (count[offer.key] > 1)
        {
            ambiguous[offer.key] = append(ambiguous[offer.key] == undefined ? [] : ambiguous[offer.key], address);
        }
    }
    return { "keys" : table, "order" : order, "ambiguous" : ambiguous, "sources" : sources };
}

/** The qCreatedBy fallback for a source that did not embed a standard key. */
function implicitOutputs(featureId is Id) returns map
{
    return {
            "output" : qCreatedBy(featureId, EntityType.BODY),
            "outputFaces" : qCreatedBy(featureId, EntityType.FACE),
            "outputEdges" : qCreatedBy(featureId, EntityType.EDGE),
            "outputVertices" : qCreatedBy(featureId, EntityType.VERTEX)
        };
}

/**
 * Looks up an address. Returns the key record, or `{ "error" : message }` when it is unknown
 * or ambiguous (with the addresses to choose from).
 */
export function lookupKey(available is map, address is string) returns map
{
    const found = available.keys[address];
    if (found != undefined)
    {
        return found;
    }
    if (available.ambiguous[address] != undefined)
    {
        return { "error" : "'" ~ address ~ "' is offered by several sources; use one of " ~ join(available.ambiguous[address], ", ") };
    }
    return { "error" : "no source offers '" ~ address ~ "'" };
}

/** The default published name for an address: "key@2" becomes "key_2". */
export function addressToName(address is string) returns string
{
    return replace(address, "@", "_");
}

/**
 * Prints every address to the FeatureScript notices:
 * `[query] outputEdges (source 1, implicit) -- description`.
 */
export function printAvailableKeys(available is map)
{
    for (var source in available.sources)
    {
        println("Source " ~ source.index ~ " " ~ source.slot ~ (source.embedded ? " embeds a map" : " embeds nothing (standard outputs only)"));
    }
    for (var address in available.order)
    {
        const k = available.keys[address];
        println("[" ~ k.kind ~ "] " ~ address ~ " (source " ~ k.source ~ (k.implicit ? ", implicit" : "") ~ ")"
            ~ describeSuffix(k.description));
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
// Consumer side: entry types
// ---------------------------------------------------------------------------------

/**
 * Resolves one Extract variables entry to what it publishes.
 *
 * @returns {map} : { kind ("variable" | "query"), value, description, evaluateOnUse }
 *                  or { error : message, parameter : inner parameter id }.
 */
export function resolveEntry(context is Context, available is map, entry is map) returns map
{
    const input = lookupKey(available, entry.x_sourceKey);
    if (input.error != undefined)
    {
        return { "error" : input.error, "parameter" : "x_sourceKey" };
    }
    if (entry.x_type == ExtractEntryType.SOURCE_KEY)
    {
        return {
                "kind" : input.kind,
                "value" : input.value,
                "description" : input.description,
                "evaluateOnUse" : input.kind == "query" && entry.x_evaluateOnUse == true
            };
    }

    if (input.kind != "query")
    {
        return { "error" : "'" ~ entry.x_sourceKey ~ "' is a value, not geometry", "parameter" : "x_sourceKey" };
    }
    const q = input.value;
    var result;
    var description;
    if (entry.x_type == ExtractEntryType.FILTERED)
    {
        result = filteredEntities(context, q, entry);
        description = "Filtered from " ~ entry.x_sourceKey ~ ".";
    }
    else if (entry.x_type == ExtractEntryType.CLOSEST)
    {
        const point = pointLocation(context, entry.x_point);
        if (point == undefined)
        {
            return { "error" : "pick a vertex or mate connector", "parameter" : "x_point" };
        }
        result = qClosestTo(entitiesOfType(q, entry.x_entityType), point);
        description = "Closest to a point, from " ~ entry.x_sourceKey ~ ".";
    }
    else if (entry.x_type == ExtractEntryType.SHARED_EDGES)
    {
        const other = lookupKey(available, entry.x_secondKey);
        if (other.error != undefined || other.kind != "query")
        {
            return { "error" : other.error != undefined ? other.error : "'" ~ entry.x_secondKey ~ "' is not geometry", "parameter" : "x_secondKey" };
        }
        result = qIntersection([edgesOf(q), edgesOf(other.value)]);
        description = "Edges shared by " ~ entry.x_sourceKey ~ " and " ~ entry.x_secondKey ~ ".";
    }
    else if (entry.x_type == ExtractEntryType.CHAIN_END || entry.x_type == ExtractEntryType.BRIDGING)
    {
        const point = pointLocation(context, entry.x_point);
        if (point == undefined)
        {
            return { "error" : "pick a vertex or mate connector", "parameter" : "x_point" };
        }
        const bridging = entry.x_type == ExtractEntryType.BRIDGING;
        const found = chainEnd(context, edgesOf(q), point, bridging ? ExtractEndRule.NEAREST : entry.x_endRule);
        if (found.error != undefined)
        {
            return { "error" : found.error, "parameter" : "x_sourceKey" };
        }
        const output = bridging ? ExtractEndOutput.BOTH : entry.x_endOutput;
        result = output == ExtractEndOutput.VERTEX ? found.vertex
            : (output == ExtractEndOutput.EDGE ? found.edge : qUnion([found.edge, found.vertex]));
        description = bridging ? "Bridging curve input (end edge and vertex) of " ~ entry.x_sourceKey ~ "."
            : "Chain end of " ~ entry.x_sourceKey ~ ".";
    }
    else
    {
        const a = pointLocation(context, entry.x_point);
        const b = pointLocation(context, entry.x_point2);
        if (a == undefined || b == undefined)
        {
            return { "error" : "pick two vertices or mate connectors", "parameter" : a == undefined ? "x_point" : "x_point2" };
        }
        const found = edgesBetween(context, edgesOf(q), a, b, entry.x_otherSide == true);
        if (found.error != undefined)
        {
            return { "error" : found.error, "parameter" : "x_sourceKey" };
        }
        result = found.edges;
        description = "Edges of " ~ entry.x_sourceKey ~ " between two points.";
    }
    // Composed from what exists now: always held, never re-evaluated.
    return { "kind" : "query", "value" : result, "description" : description, "evaluateOnUse" : false };
}

/** Entities of a type, from a query holding bodies and/or entities. */
export function entitiesOfType(q is Query, entityType is ExtractEntityType) returns Query
{
    if (entityType == ExtractEntityType.BODY)
    {
        return qUnion([qEntityFilter(q, EntityType.BODY), qOwnerBody(q)]);
    }
    const t = entityType == ExtractEntityType.FACE ? EntityType.FACE
        : (entityType == ExtractEntityType.EDGE ? EntityType.EDGE : EntityType.VERTEX);
    return qUnion([qEntityFilter(q, t), qOwnedByBody(qEntityFilter(q, EntityType.BODY), t)]);
}

/** Edges held by a query directly, owned by its bodies, or bounding its faces. */
export function edgesOf(q is Query) returns Query
{
    return qUnion([qEntityFilter(q, EntityType.EDGE),
                qOwnedByBody(qEntityFilter(q, EntityType.BODY), EntityType.EDGE),
                qAdjacent(qEntityFilter(q, EntityType.FACE), AdjacencyType.EDGE, EntityType.EDGE)]);
}

/**
 * The FILTERED entry: entity type, then body type, then owner-body name, then the largest N
 * (length, area or volume).
 */
function filteredEntities(context is Context, q is Query, entry is map) returns Query
{
    var result = entitiesOfType(q, entry.x_entityType);
    if (entry.x_bodyType != ExtractBodyType.ANY)
    {
        const bt = entry.x_bodyType == ExtractBodyType.SOLID ? BodyType.SOLID
            : (entry.x_bodyType == ExtractBodyType.SHEET ? BodyType.SHEET : BodyType.WIRE);
        result = qBodyType(result, bt);
    }
    if (entry.x_nameContains != "" || entry.x_keepLargest == true)
    {
        var kept = [];
        for (var e in evaluateQuery(context, result))
        {
            if (entry.x_nameContains != "")
            {
                const owner = entry.x_entityType == ExtractEntityType.BODY ? e : qOwnerBody(e);
                const name = getProperty(context, { "entity" : owner, "propertyType" : PropertyType.NAME });
                // Case-insensitive, and the typed text is quoted so it is never read as a pattern.
                if (!(name is string) || !match(name, "(?i).*\\Q" ~ entry.x_nameContains ~ "\\E.*").hasMatch)
                {
                    continue;
                }
            }
            kept = append(kept, e);
        }
        if (entry.x_keepLargest == true && entry.x_entityType != ExtractEntityType.VERTEX)
        {
            kept = largest(context, kept, entry.x_entityType, entry.x_largestCount);
        }
        result = qUnion(kept);
    }
    return result;
}

/** The `count` largest entities by length, area or volume. */
function largest(context is Context, entities is array, entityType is ExtractEntityType, count is number) returns array
{
    var sized = [];
    for (var e in entities)
    {
        sized = append(sized, { "entity" : e, "size" : entitySize(context, e, entityType) });
    }
    sized = sort(sized, function(a, b) { return b.size - a.size; });
    var out = [];
    for (var i = 0; i < min(count, size(sized)); i += 1)
    {
        out = append(out, sized[i].entity);
    }
    return out;
}

/** Length, area or volume as a plain SI number, so bodies of different types compare. */
function entitySize(context is Context, e is Query, entityType is ExtractEntityType) returns number
{
    if (entityType == ExtractEntityType.EDGE)
    {
        return evLength(context, { "entities" : e }).value;
    }
    if (entityType == ExtractEntityType.FACE)
    {
        return evArea(context, { "entities" : e }).value;
    }
    if (!isQueryEmpty(context, qBodyType(e, BodyType.SOLID)))
    {
        return evVolume(context, { "entities" : e }).value;
    }
    if (!isQueryEmpty(context, qBodyType(e, BodyType.SHEET)))
    {
        return evArea(context, { "entities" : qOwnedByBody(e, EntityType.FACE) }).value;
    }
    return evLength(context, { "entities" : qOwnedByBody(e, EntityType.EDGE) }).value;
}

/** World position of a picked vertex or mate connector; undefined when nothing is picked. */
export function pointLocation(context is Context, pick)
{
    if (!(pick is Query) || isQueryEmpty(context, pick))
    {
        return undefined;
    }
    const connector = qBodyType(pick, BodyType.MATE_CONNECTOR);
    if (!isQueryEmpty(context, connector))
    {
        return evMateConnector(context, { "mateConnector" : connector }).origin;
    }
    return evVertexPoint(context, { "vertex" : pick });
}

/**
 * The end of a chain of edges nearest (or farthest from) a point: its free vertex and the
 * edge it ends. Free vertices are those bounding exactly one edge of the set.
 *
 * @returns {map} : { vertex, edge } or { error }.
 */
function chainEnd(context is Context, edges is Query, point is Vector, rule is ExtractEndRule) returns map
{
    const edgeList = evaluateQuery(context, edges);
    if (size(edgeList) == 0)
    {
        return { "error" : "the source has no edges" };
    }
    var uses = {};
    var owner = {};
    var vertexOf = {};
    for (var edge in edgeList)
    {
        for (var vertex in evaluateQuery(context, qAdjacent(edge, AdjacencyType.VERTEX, EntityType.VERTEX)))
        {
            const key = toString(vertex);
            uses[key] = (uses[key] == undefined ? 0 : uses[key]) + 1;
            owner[key] = edge;
            vertexOf[key] = vertex;
        }
    }
    var best = undefined;
    var bestDistance = undefined;
    for (var entry in uses)
    {
        if (entry.value != 1)
        {
            continue;
        }
        const d = norm(evVertexPoint(context, { "vertex" : vertexOf[entry.key] }) - point);
        if (bestDistance == undefined || (rule == ExtractEndRule.NEAREST ? d < bestDistance : d > bestDistance))
        {
            bestDistance = d;
            best = entry.key;
        }
    }
    if (best == undefined)
    {
        return { "error" : "the chain is closed; it has no end" };
    }
    return { "vertex" : vertexOf[best], "edge" : owner[best] };
}

/**
 * Whole edges of a chain between the chain vertices nearest two points. On a closed chain
 * the shorter side is returned, or the other side when `otherSide` is set.
 *
 * @returns {map} : { edges : Query } or { error }.
 */
function edgesBetween(context is Context, edges is Query, a is Vector, b is Vector, otherSide is boolean) returns map
{
    if (isQueryEmpty(context, edges))
    {
        return { "error" : "the source has no edges" };
    }
    var path;
    try
    {
        path = constructPath(context, edges);
    }
    catch
    {
        return { "error" : "the edges do not form one connected chain" };
    }
    const n = size(path.edges);
    // Vertex k is the start of edge k along the path; vertex n closes an open chain.
    var points = [];
    var lengths = [];
    for (var i = 0; i < n; i += 1)
    {
        const ends = evEdgeTangentLines(context, { "edge" : path.edges[i], "parameters" : [0, 1] });
        const startPoint = path.flipped[i] ? ends[1].origin : ends[0].origin;
        points = append(points, startPoint);
        if (i == n - 1 && !path.closed)
        {
            points = append(points, path.flipped[i] ? ends[0].origin : ends[1].origin);
        }
        lengths = append(lengths, evLength(context, { "entities" : path.edges[i] }));
    }
    const ia = nearestIndex(points, a);
    const ib = nearestIndex(points, b);
    if (ia == ib)
    {
        return { "error" : "both points snap to the same vertex of the chain" };
    }
    var lo = min(ia, ib);
    var hi = max(ia, ib);
    var picked = [];
    if (!path.closed)
    {
        for (var i = lo; i < hi; i += 1)
        {
            picked = append(picked, path.edges[i]);
        }
        return { "edges" : qUnion(picked) };
    }
    // Closed: the side lo..hi-1, or its complement.
    var inside = [];
    var outside = [];
    var insideLength = 0 * meter;
    var outsideLength = 0 * meter;
    for (var i = 0; i < n; i += 1)
    {
        if (i >= lo && i < hi)
        {
            inside = append(inside, path.edges[i]);
            insideLength += lengths[i];
        }
        else
        {
            outside = append(outside, path.edges[i]);
            outsideLength += lengths[i];
        }
    }
    const shorterIsInside = insideLength <= outsideLength;
    return { "edges" : qUnion((shorterIsInside != otherSide) ? inside : outside) };
}

function nearestIndex(points is array, target is Vector) returns number
{
    var best = 0;
    var bestDistance = inf * meter;
    for (var i = 0; i < size(points); i += 1)
    {
        const d = norm(points[i] - target);
        if (d < bestDistance)
        {
            bestDistance = d;
            best = i;
        }
    }
    return best;
}

// ---------------------------------------------------------------------------------
// Consumer side: publishing
// ---------------------------------------------------------------------------------

/**
 * Throws if `name` is not a valid identifier or is already held by an ordinary variable
 * (copy of the private std check used by the Query variable feature).
 */
export function checkQueryVariableName(context is Context, name is string, faultyParameter is string)
{
    verifyVariableNameIsValid(name, faultyParameter);
    if (getVariable(context, name, undefined) != undefined)
    {
        throw regenError(ErrorStringEnum.QUERY_VARIABLE_NAME_ALREADY_USED_IN_NON_QUERY_VARIABLE, [faultyParameter]);
    }
}

/**
 * Publishes `q` as query variable `name`. With `evaluateOnUse` off the stored query is the
 * std robust freeze `qUnion(makeRobustQueriesBatched(context, q))` -- the entities present
 * now, each tracked through identity-preserving edits (what the std Query variable feature
 * stores). With it on, `q` is stored symbolic and re-resolves wherever it is used.
 */
export function publishQueryVariable(context is Context, name is string, description is string, q is Query,
    evaluateOnUse is boolean, faultyParameter is string)
{
    checkQueryVariableName(context, name, faultyParameter);
    const stored = evaluateOnUse ? q : qUnion(makeRobustQueriesBatched(context, q));
    setQueryVariable(context, name, description, stored);
}
