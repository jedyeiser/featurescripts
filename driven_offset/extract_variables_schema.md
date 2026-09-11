# Extract variables: data contract (manifest schema extractManifest/1)

Written by "Extract variables" (`driven_offset/design_map.fs` tab); producers embed via
`driven_offset/design_map_query_utils.fs`. Scope: the Part Studio that wrote the variables.
Decision 2026-09-11: queries are NEVER stored inside map variables. Selections are published
as native query variables; maps hold scalars only.

## What Extract variables publishes

- Ordinary variables: `<prefix>_<key>` (or `<key>` when the prefix is empty), one per
  non-Query value. Readable as `#name` in expression fields and `getVariable(context, name)`.
- Query variables: same naming, one per Query value. Pickable in any feature's selection
  dropdown (native and custom alike). Stored with the std robust freeze unless the entry's
  "Evaluate on use" is on (then symbolic, re-resolved where used).
- Manifest (optional, parameter "Manifest" = a variable name): one ordinary map variable

```
{ "schema" : "extractManifest/1",
  "variables" : { "<name>" : <value>, ... },     // the ordinary variables published, by name
  "queries"   : [ "<name>", ... ] }              // the query variable NAMES published
```

## Fetching (API, same Part Studio)

One `evalFeatureScript` call (POST .../partstudios/d/{did}/w|v|m/{wvm}/e/{eid}/featurescript):

```
function(context is Context, queries is map)
{
    const m = getVariable(context, "<manifest>");
    var out = { "manifest" : m, "entities" : {} };
    for (var name in m.queries)
    {
        out.entities[name] = evaluateQuery(context, getQueryVariable(context, name));
    }
    return out;
}
```

Transient entity ids are valid for that microversion only. Value serialisation of
ValueWithUnits / Vector in the response is UNVERIFIED: confirm with one call on a test manifest
before coding the viewer.

Alternative with no consumer feature in the tree (UNVERIFIED, relies on an `@internal` std
function): `getAllVariables(context)` and filter keys that look like `[ F... ]` -- those are
producer slots (below), each holding `{ "variable" : {...}, "query" : {...} }`.

## Provenance (producers)

Producers (driven_edge_offset, evaluate_profiles, ...) call
`embedVariableMap(context, id, { "variable" : {...}, "query" : {...} })`. The map is stored in a
hidden context variable named `toString(id)` (renders like `[ Fxxx ]`: not an identifier, so it
never appears under `#` or in a dropdown). `variable` holds scalars/containers (no Queries);
`query` holds flat symbolic Queries. Values may be plain or descriptors
`{ value, description[, debugColor] }`; the description feeds "Print keys" and the published
query variable.

Extract variables merges its sources (variable keys first-wins with a warning, query keys
qUnion), then publishes either every key ("Extract every key") or the listed entries (with
optional rename).

## Versioning

`schema` is `extractManifest/<n>`. Additive changes keep `n`; anything that changes the meaning
or shape of existing keys bumps `n`. Readers check the prefix and refuse or degrade on an
unknown `n`. Current: `extractManifest/1` (`EXTRACT_MANIFEST_SCHEMA` in utils).
