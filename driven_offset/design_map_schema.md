# Design map: data contract (schema designMap/1)

Written by "Design map" (`driven_offset/design_map.fs`); producers embed via
`driven_offset/design_map_query_utils.fs`. Scope: the Part Studio that wrote the map.

## Fetching (same Part Studio)

One `evalFeatureScript` call (POST .../partstudios/d/{did}/w|v|m/{wvm}/e/{eid}/featurescript) whose
script returns `getVariable(context, "<mapName>")`. Query entries come back as symbolic query maps
(below); for geometry, evaluate them IN THE SAME script: `return evaluateQuery(context,
getVariable(context, "<mapName>").bottom);` -> transient ids valid for that microversion only.
In FeatureScript: `getVariable(context, "<mapName>").<key>` (throws if absent) or the tolerant
`getVariable(context, "<mapName>", {})["<key>"]`. Expression fields: `#<mapName>.<key>`.

## Shape

```
{ "schema" : "designMap/1", "<key>" : <value>, ..., "<group>" : { "<key>" : <value>, ... } }
```
- `schema` appears ONLY at the top level; nested groups carry no schema key. A viewer must walk
  nested maps recursively. Keys are identifiers `[a-zA-Z_][a-zA-Z_0-9]*`; `schema` is reserved.
- Value kinds and their evalFeatureScript JSON (UNVERIFIED against a live call -- confirm the exact
  ValueWithUnits and Query serialisation with one evalFeatureScript on a test map before coding the viewer):
  - number / string / boolean: plain JSON scalars.
  - ValueWithUnits (Length/Angle entries, embedded scalars): a map with a numeric `value` in SI base
    units (meter, radian, ...) and a `unit` map. Convert client-side.
  - Vector (embedded points): array of ValueWithUnits. map / array: nested containers of the above
    (e.g. a producer's `runs` array of payload maps).
  - Query: a symbolic query map such as `{ queryType : "UNION", subqueries : [ { queryType :
    "CREATED_BY", featureId : [ "F..." ], entityType : "BODY" } ] }`; fields vary by query kind.
    Treat it as opaque: pass it to `evaluateQuery` in the same script; never rebuild it client-side.

## Provenance
- Producers (driven_edge_offset, evaluate_profiles, ...) call `embedVariableMap(context, id,
  { "variable" : {...}, "query" : {...} })`, stored in a hidden context variable named
  `toString(id)` (renders like `[ Fxxx ]`: not an identifier, unreachable from `#`). `variable`
  holds scalars/containers (no Queries); `query` holds flat symbolic Queries. Values may be plain
  or descriptors `{ value, description[, debugColor] }`, unwrapped on read; the description feeds
  "Print keys" and published query variables.
- Design map merges its `sources` (variable keys first-wins, query keys qUnion), takes every key
  when "Take every embedded key" is on, then applies entries in order (Embedded pick/rename, Query
  selection, Length/Angle/Number, Reference = copy of a key already in the map).

## Create vs Add to existing map
- CREATE writes `{ "schema" : "designMap/1", ...keys }`, overwriting a prior variable of that name.
- EXTEND reads `#<mapName>` (must exist earlier in the tree, be a map, carry the schema key) and
  inserts the new keys under "Place under" (dot path, e.g. `outputs.deo`; empty = top level;
  missing intermediates are created; a non-map intermediate is an error). Any key already present
  at that location is an error. No silent overwrite in either mode.
- UNVERIFIED: whether `#core.outputs.deo.chainLength` autocompletes past one level in the Onshape
  expression UI; FeatureScript and API reads of nested keys work regardless.

## Published query variables
Entries with "Publish" on and a Query value also create query variable `<mapName>_<key>` (path NOT
included, to keep names short) via the std robust freeze `qUnion(makeRobustQueriesBatched(context,
q))`: the entities at the Design map's tree position, identity-tracked through later edits.

## Versioning
`schema` is `designMap/<n>`. Additive changes keep `n`; anything that changes the meaning or shape
of existing keys bumps `n`. Readers check the `designMap/` prefix and refuse or degrade on an unknown
`n`. Current: `designMap/1` (`DESIGN_MAP_SCHEMA` in utils).
