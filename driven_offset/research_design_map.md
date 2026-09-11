# Design map: research and design (2026-09-10)

Sources: raw reports 01/03/05/07 (scratchpad), `driven_offset/query_varialble_ref.fs` (Derek Van Allen,
"ref" below), std 2878 mirror. Line refs are repo-relative. Sibling doc: `research_driven_edge_offset.md`.

## 1. Problem

A ski Part Studio carries ~40 "Query Variable" features. Clutter is twofold: (a) 40 rows in the feature tree;
(b) 40 names in the variable-selection dropdown and the shared variable namespace. A "design map" must give:
one map variable per design (`#core`) whose keys autocomplete in expression fields; Query entries usable by
later custom features; a "hold for export" form that survives derive/split/boolean and is readable by an
external API tool; and no silent failure when an entry breaks.

## 2. Verified facts the design rests on

| # | Fact | Evidence |
|---|------|----------|
| 1 | `setVariable` stores any value (array, map); a map may hold Query and ValueWithUnits | std/context.fs:250-279; Reese_Test_Feature.fs:15-28 (Query round-trips getVariable -> setQueryVariable); std/variable.fs:382-385 |
| 2 | `getVariable` throws if absent; 3-arg overload returns default | std/context.fs:284-307 |
| 3 | Ordinary and query variable namespaces are disjoint; each feature rejects the other's names | std/variable.fs:810-823; std/queryVariable.fs:519-532 |
| 4 | `#name` works only in expression-typed params; selection params pick query variables from the "Variable selection" dropdown | std/context.fs:288-289; std/variable.fs:127-131; std/queryVariable.fs:303 |
| 5 | Variable/QV name regex `[a-zA-Z_][a-zA-Z_0-9]*`, max 10000 chars (so `core.bottom` is not a QV name) | std/variable.fs:798-805 |
| 6 | Map-variable key autocomplete: `#core.thickness` / `#core["thickness"]` | https://forum.onshape.com/discussion/31466/improvements-to-onshape-august-7-2026 |
| 7 | Custom features can reference Variable Studios directly | https://forum.onshape.com/discussion/31629/improvements-to-onshape-august-28-2026 |
| 8 | `getProperty` cannot be called on the current context inside a custom feature (select-by-NAME impossible) | std/properties.fs:7, :71-76 |
| 9 | Attributes survive split (both pieces), pattern (each copy), boolean merge (both kept; same-name clash -> primary wins) | std/attributes.fs:38-43 |
| 10 | Derive keeps attributes; only sheet-metal association attrs are stripped | std/derive.fs:138-145 |
| 11 | Variables do not cross derive (nothing in std/derive.fs copies context variables) | std/derive.fs (absence); raw 01 sec Limitations 6 |
| 12 | Robust freeze = per-entity `qUnion([transient, startTrackingIdentityFromOp])`; std QV feature uses it when Evaluate-on-use is off | std/feature.fs:643-652; std/queryVariable.fs:402-406 |
| 13 | `evaluateQuery` returns transient queries valid only until the context changes | std/query.fs:2162-2167 |
| 14 | Regen is tree-position based: the changed feature and everything after re-runs | https://cad.onshape.com/FsDoc/debugging-in-feature-studios.html |
| 15 | Array-item inner parameter names must be unique across the whole feature; arrays cannot nest | https://cad.onshape.com/FsDoc/uispec.html; ref:457-465 (`addQ` prefix) |
| 16 | Type tags are bound to the declaring module in the import graph; `is EmbeddedVariables` across different library elements is unverified | https://cad.onshape.com/FsDoc/relational.html; raw 01 FOLLOW-UP 1 |
| 17 | `@setVariable` / `@setQueryVariable` accept arbitrary names; only the std features validate | https://forum.onshape.com/discussion/17100/any-reason-we-cant-use-other-punctuation-in-variable-names; std/context.fs:260-263 |
| 18 | `common.fs` (2878) does NOT re-export `queryVariable.fs`; import it explicitly for `setQueryVariable` | raw 01 FOLLOW-UP 2; ref:30 imports attributes.fs separately too |

## 3. Query Variable + (Derek Van Allen), in brief

Architecture. One feature = one query variable. `SelectionType` enum (ref:43-95, 25 cases) drives a
~200-line predicate (ref:246-454); an array of "additional queries" reuses the same predicate with every
field prefixed `addQ` (ref:457-465) because inner names must be unique (fact 15). Body (ref:800-823):
`mapSelectionTypeToQuery` (ref:874-906) on the root, then a left fold over the array applying
`qUnion`/`qSubtraction`/`qIntersection` per item; if not evaluate-on-use, `qUnion(makeRobustQueriesBatched)`;
`setQueryVariable`. `checkQueryVariableName` (ref:1419-1432) enforces the regex and namespace disjointness.
Derek's addition (ref:826-862): tag every evaluated entity with attribute `queryVariableName` = array of QV
names (read-modify-write per entity); `LOAD_FROM_DERIVE` (ref:1444-1527) rebuilds QVs from a derive feature
by bucketing entities per name and `setQueryVariable(name, qUnion(transients))`.

Keep: the dispatcher shape (enum -> Query), the boolean fold, the freeze branch, the name check, the
`addQ` remap (ref:1332-1346), and the attribute-tag idea.

Drop: evaluating selection types (SIZE_COMPARISON, MATCHING_BODIES, TOLERANT_PARALLEL,
POSITIONAL_DIRECTIONAL: ev*-based, 100-150 lines each, frozen at build time anyway);
the single-array `queryVariableName` attribute (same-name attrs collide on boolean merge, fact 9, and
needs per-entity RMW); LOAD_FROM_DERIVE's frozen-transient QVs (no identity tracking, ref:1516-1517);
the second predicate copy (the composer lives inside one array, so one prefix suffices).

Lacks for skis: select-by-NAME (impossible, fact 8) -> use attributes set by the producer;
`qCreatedBy(id + "profile")` sub-ids (evaluate_profiles.fs:344, :435) need a FeatureList pick plus a
sub-id string; no `qHasAttribute*` selection type at all.

## 4. Options considered

| Option | Shape | Tree | Namespace | Derive-safe | Verdict | Reason |
|--------|-------|------|-----------|-------------|---------|--------|
| A | One "Design map" feature per design, array of entries, writes `#core` | 1/map | 1 var + opt. QVs | via per-key attrs | ADOPT | Smallest tree and namespace; one place to rename; regen cost equal to today (fact 14) |
| B | One "Map entry" feature per entry, read-modify-write `#core` | 1/entry (= today) | 1 var | per entry | REJECT | Same clutter as today; a root writing `{}` wipes earlier entries; order-sensitive |
| C | Producers embed maps; one "Publish" feature per map | 0 + 1/map | 1 private var/producer | producer attrs | REJECT | Requires touching every producer and a FeatureList-driven publish; discovery breaks on reorder/suppress |
| Reese | Typed `EmbeddedVariables` in `toString(id)` variables + Extract feature | 0 + 1 | private vars + published | none (variables only) | REJECT | Type-tag risk across elements (fact 16); silent `try` on missing sources; evaluateQuery freeze without tracking; FS 3044 import breakage |

Decision (fixed). One custom feature "Design map" per design, placed after the geometry it names.
Entries are `{key, kind, selection|value, publish}`. Each QUERY entry is robust-frozen, its entities tagged
with attribute `dm:<map>:<key>`, and optionally published as query variable `<map>_<key>`. The feature writes
one plain map variable `{"schema": "designMap/1", ...}` -- no type tag, no FeatureList discovery, no publish
feature, no Reese library. Consumers name the key: `qHasAttribute("dm:core:bottom")` (symbolic, derive-safe,
API-safe) or `getVariable(context, "core", {})["thickness"]`. Scalars that must cross studios go to a
Variable Studio (fact 7), not to attribute-hosted anchor bodies.

## 5. Design map feature spec

### Parameters

| Param | FS type / annotation | Notes |
|-------|----------------------|-------|
| `mapName` | `is string`, Name "Map", MaxLength 256 | must pass `verifyVariableName` (regex + not a QV name), std/variable.fs:808-823 |
| `description` | `is string`, Default "" | stored on the variable |
| `entries` | `is array`, Name "Entries", Item name "entry", Item label template `#e_key` | do NOT mark "Driven query" (removes the add button) |
| `e_key` | `is string` | identifier; unique within the feature; `schema` reserved |
| `e_kind` | `is DesignMapKind` | `enum DesignMapKind { QUERY, LENGTH, ANGLE, NUMBER, ATTRIBUTE_REF, REFERENCE }` |
| `e_selection` | `is Query`, shown when kind == QUERY; Filter `AllowMeshGeometry.YES && AllowFlattenedGeometry.YES` | raw selection; composer helpers (sec 7) may replace this with kind-dispatched sub-params |
| `e_length` / `e_angle` / `e_number` | `isLength(..., LENGTH_BOUNDS)` / `isAngle(..., ANGLE_360_BOUNDS)` / `isReal(..., bounds)` | typed, mirrors std/variable.fs:184-198; avoids the untested free-form `isAnything` in arrays (Risk 1) |
| `e_ref` | `is string`, shown when kind == REFERENCE | another key of this map, must precede this entry |
| `e_publish` | `is boolean`, Default false, shown when kind in {QUERY, ATTRIBUTE_REF, REFERENCE-to-query} | creates QV `<map>_<key>` |
| `printKeys` | `is boolean`, Default false | Reese UX: `println` every key, kind, and entity count |
| `showSelection` | `is boolean`, Default true | `addDebugEntities` on the union of Query entries |

### Body algorithm

1. `verifyVariableName(context, mapName, "mapName")`; start `result = {"schema": "designMap/1"}`.
2. For each entry `i`: validate `e_key` (regex via `verifyVariableNameIsValid`, not `schema`, not already in
   `result`) -- error names `faultyArrayParameterId("entries", i, "e_key")` (std/error.fs:504).
3. Dispatch on `e_kind` (helper in design_map_query_utils.fs):
   - QUERY: `sel = e_selection`; empty -> error. `q = qUnion(makeRobustQueriesBatched(context, sel))`.
     `setAttribute({entities: q, name: "dm:"~map~":"~key, attribute: {map, key, v: 1}})`.
   - ATTRIBUTE_REF: `q = qHasAttribute("dm:"~map~":"~key)` kept symbolic (the attribute is the identity, set
     by the producer per `research_driven_edge_offset.md`); empty -> error; no re-tag.
   - REFERENCE: `result[e_ref]` must exist -> value copied (Query or scalar); else error on `e_ref`.
   - LENGTH / ANGLE / NUMBER: value with units as typed.
4. If `e_publish` and the value is a Query: `qvName = map ~ "_" ~ key`; copied `checkQueryVariableName`
   (ref:1419-1432); `setQueryVariable(context, qvName, description, q)`.
5. `result[key] = value`.
6. After the loop: `setVariable(context, mapName, result, description)`; optional `println` of keys;
   `setHighlightedEntities` / `addDebugEntities` on the union of Query values.

### Schemas and naming

- Attribute: name `"dm:<map>:<key>"`, value `{ "map" : "<map>", "key" : "<key>", "v" : 1 }`. One name per key so
  boolean merges keep all tags (fact 9). Never store a Query in an attribute.
- Map: `{ "schema" : "designMap/1", "<key>" : Query | ValueWithUnits | number, ... }`. Keys are identifiers so
  `#core.thickness` autocompletes (fact 6). No `as` type tag.
- Query variable name: `<map>_<key>`. Distinct string from `<map>`, so fact 3 does not bite.

### Error policy

Loud, no `try`. Errors: invalid map name; invalid/duplicate/reserved key; QUERY or ATTRIBUTE_REF resolving to
nothing; REFERENCE to an unknown or later key; publish name already an ordinary variable. One bad entry fails
the whole feature and every `#core.*` reader -- intended (raw 07 sec 1b).

### Per-entry loop

```
var result = { "schema" : "designMap/1" };
for (var i = 0; i < size(definition.entries); i += 1)
{
    const e = definition.entries[i];
    const keyParam = faultyArrayParameterId("entries", i, "e_key");
    verifyVariableNameIsValid(e.e_key, keyParam);
    if (e.e_key == "schema" || result[e.e_key] != undefined)
    {
        throw regenError("Key '" ~ e.e_key ~ "' is reserved or duplicated.", [keyParam]);
    }
    const attrName = "dm:" ~ definition.mapName ~ ":" ~ e.e_key;
    var value = designMapEntryValue(context, definition.mapName, e, result); // utils: kind dispatch
    if (value is Query)
    {
        if (isQueryEmpty(context, value))
        {
            throw regenError("Entry '" ~ e.e_key ~ "' selects nothing.", [faultyArrayParameterId("entries", i, "e_selection")]);
        }
        if (e.e_kind == DesignMapKind.QUERY)
        {
            value = qUnion(makeRobustQueriesBatched(context, value));
            setAttribute(context, { "entities" : value, "name" : attrName, "attribute" : { "map" : definition.mapName, "key" : e.e_key, "v" : 1 } });
        }
        if (e.e_publish)
        {
            const qvName = definition.mapName ~ "_" ~ e.e_key;
            checkQueryVariableName(context, qvName);
            setQueryVariable(context, qvName, definition.description, value);
        }
    }
    result[e.e_key] = value;
}
setVariable(context, definition.mapName, result, definition.description);
```

## 6. Consumer patterns

| Consumer | Pattern | Notes |
|----------|---------|-------|
| Custom feature, geometry | `qHasAttribute("dm:core:bottom")` | symbolic; valid in the same studio, after derive, and via API. Preferred. |
| Custom feature, scalar | `getVariable(context, "core", {})["thickness"]` | default `{}` then check key -> clear error; never `definition.x is Query` for map entries |
| Custom feature, frozen Query | `getVariable(context, "core")["bottom"]` | robust-frozen at the Design map's tree position; same studio only |
| Native dialog (Extrude, Fillet) | pick `core_bottom` from the Variable selection dropdown | only entries with `e_publish`; expression fields get `#core.thickness` |
| Derived studio | `qHasAttribute("dm:core:bottom")` works as-is | optional "Design map loader": per entity created by the derive, `getAllAttributes` (std/attributes.fs:152), bucket `dm:*` names, rebuild `#core` + `core_*` (generalises ref:1444-1527, but with `makeRobustQueriesBatched`) |
| External API | one `evalFeatureScript` returning `getVariable(context, "core")` for scalars plus, per `dm:*` attribute, `evaluateQuery` transient ids to correlate with body-details / tessellation ids | attribute route is the Onshape-recommended one: https://forum.onshape.com/discussion/6912/how-to-access-data-written-to-parts-with-setattribute |

## 7. design_map_query_utils.fs scope

Belongs there (all `export`):
- `enum DesignMapKind` and the `entries` array predicate (so `design_map.fs` stays a thin feature).
- `designMapEntryValue(context, mapName, entry, resultSoFar)`: kind dispatch (sec 5 step 3), including
  REFERENCE-to-other-key lookup and the ATTRIBUTE_REF `qHasAttribute` construction.
- `designMapAttributeName(mapName, key)` and `tagDesignMapEntities` / `untagDesignMapEntities`
  (`setAttribute` with `undefined` unsets, std/attributes.fs:60-62).
- `checkQueryVariableName` copy (ref:1419-1432; std's is private).
- Composer (only if the user wants it, sec 9 Q3): per-item `booleanOperation is BooleanOperationType`
  + left fold (ref:802-815 shape), a reduced `SelectionType` {SELECTION, CREATED_BY, OWNED_BY, ATTRIBUTE,
  REFERENCE, GEOMETRY, EDGE_CONVEXITY} dispatcher, and the `addQ`-style remap (ref:1332-1346) so one
  predicate serves the operand list. This makes qUnion/qSubtraction/qIntersection "easier" without a
  second feature: the composer is the `kind = QUERY` entry's selection helper, not a feature of its own.

Not there: any ev*-based selection type; anything that calls `setVariable`/`setQueryVariable` (the feature
owns side effects); FeatureList discovery; Reese typed maps; a derive loader (separate feature if ever).

## 8. Risks and Onshape tests

1. Typed value fields inside an array item. Setup: minimal feature with `entries[]` holding `isLength` /
   `isAngle` / `isReal` (and one `isAnything` variant). Observe: type `12 mm`, `#foo`, `{a:1}`; `println`
   and `setVariable`; read back with `#m.k` in a Variable. Retires: Risk 1 (units round-trip inside array
   items; whether `isAnything` is usable at all or typed kinds are mandatory).
2. Attribute survival through ski downstream ops. Setup: tag core faces/edges `dm:core:bottom`; then fillet,
   boolean-union with sidewall, split, mirror, derive into a fresh studio. Observe:
   `println(size(evaluateQuery(context, qHasAttribute("dm:core:bottom"))))` after each op. Retires: Risk 2
   (attribute loss on downstream ops; fact 9/10 vs forum reports of loss).
3. Regen cost and blast radius. Setup: Design map with 40 entries, two `#a`/`#b` readers, one slow feature
   after it. Observe: edit one entry; feature-list performance readout shows what re-ran; break one entry
   and confirm the whole map errors loudly. Retires: Risk 3 (no hidden short-circuit assumed; failure mode
   is understood).
4. Dropdown pickability (raw 05 FOLLOW-UP 2). Setup: Variable `core` Any = `{ "t" : 12 mm }`; QV `bottom`;
   Variable `m` = `{ "q" : #bottom }`; publish `core_bottom`, `core_top`, `sidewall_x`. Observe: open Fillet
   -> Variable selection dropdown: is `m`/`m.q` listed (expected no); are `core_*` listed and in what order
   (alphabetical vs tree); does `#bottom` appear in a length-field autocomplete (expected no). Retires:
   whether map entries can ever be picked natively (expected: only published QVs) and whether 40 published
   QVs make the dropdown unusable (if so cap publish at ~10).
5. Namespace collision. Setup: ordinary variable `core_bottom` exists; publish entry `bottom` of map `core`.
   Observe: copied `checkQueryVariableName` throws `QUERY_VARIABLE_NAME_ALREADY_USED_IN_NON_QUERY_VARIABLE`.
   Retires: fact 3 enforcement in the custom feature.

## 9. Open decisions for the user

1. Map granularity: one Design map per design (`core`, `sidewall`, `topsheet`) vs per subsystem.
   Recommend per design; split only if a map exceeds ~30 entries.
2. Publish any QVs at all? Recommend default none; publish only entries picked in native dialogs, after test 4.
3. Build the composer now? Recommend no: ship kind = QUERY with a raw selection first; add the reduced
   composer in design_map_query_utils.fs only when re-selecting geometry proves painful.
4. Key naming: recommend lowerCamel identifiers (`bottomFaces`, `coreThickness`), no prefixes (the map is
   the namespace), and `schema` reserved.
5. Map naming: recommend the design noun in lowercase (`core`, `sidewall`, `topsheet`, `base`); QVs become
   `core_bottomFaces`.
6. Producer tagging (D3): confirm `nameOutput` (driven_edge_offset.fs:919-931) and `nameProfile`
   (evaluate_profiles.fs:1506-1518; neither sets attributes today) gain a `dm:` attribute and how the map
   name reaches them (a string param). Recommend yes, string param `designMap` defaulting to "".
7. Delete `query_varialble_ref.fs` and the three Reese files after copying `checkQueryVariableName`
   and the fold/remap shapes into design_map_query_utils.fs. Recommend yes.
8. Derive loader: build the optional loader feature or rely on `qHasAttribute` alone in derived studios.
   Recommend rely on `qHasAttribute` until a derived studio needs `#core.*` scalars (then Variable Studio).
