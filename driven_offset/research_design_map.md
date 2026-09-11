# Design map: research and design (rev 2, 2026-09-10)

Sources: raw reports 01/05/07 (scratchpad), `driven_offset/Reese_IMPORT_THIS.fs` (LIB),
`driven_offset/Reese_Extract_Variables.fs` (EV), `driven_offset/query_varialble_ref.fs` (Derek Van Allen, "ref"),
std 2878 mirror. Line refs are repo-relative. Sibling doc: `research_driven_edge_offset.md` (producer side).

## 1. Problem

A ski Part Studio carries ~40 "Query Variable" features: 40 tree rows, 40 dropdown names, one flat namespace.
A "design map" must give one map variable per design (`#core`) whose keys autocomplete in expression fields,
Query entries usable by later custom features and (a few) native dialogs, and no silent failure.
Scope: the map only has to work INSIDE the Part Studio that wrote it. Derive survival and cross-studio use
are out of scope (see "Later" at the end of sec 2).

## 2. Verified facts the design rests on

| # | Fact | Evidence |
|---|------|----------|
| 1 | `setVariable` stores any value (array, map); a map may hold Query and ValueWithUnits | std/context.fs:250-279; Reese_Test_Feature.fs:15-28; std/variable.fs:382-385 |
| 2 | `getVariable` throws if absent; 3-arg overload returns default; `try silent(expr)` yields undefined on throw | std/context.fs:284-307; EV:294 |
| 3 | Ordinary and query variable namespaces are disjoint; each std feature rejects the other's names | std/variable.fs:810-823; std/queryVariable.fs:519-532 |
| 4 | `#name` works only in expression-typed params; selection params pick query variables from the "Variable selection" dropdown | std/context.fs:288-289; std/variable.fs:127-131; std/queryVariable.fs:303 |
| 5 | Variable/QV name regex `[a-zA-Z_][a-zA-Z_0-9]*`, max 10000 chars (so `core.bottom` is not a QV name) | std/variable.fs:798-805 |
| 6 | Map-variable key autocomplete: `#core.thickness` / `#core["thickness"]` | https://forum.onshape.com/discussion/31466/improvements-to-onshape-august-7-2026 |
| 7 | Custom features can reference Variable Studios directly | https://forum.onshape.com/discussion/31629/improvements-to-onshape-august-28-2026 |
| 8 | `getProperty` cannot be called on the current context inside a custom feature (select-by-NAME impossible) | std/properties.fs:7, :71-76 |
| 9 | Robust freeze = per-entity `qUnion([transient, startTrackingIdentityFromOp])`; std QV feature uses it when Evaluate-on-use is off | std/feature.fs:643-652; std/queryVariable.fs:402-406 |
| 10 | `evaluateQuery` returns transient queries valid only until the context changes | std/query.fs:2162-2167 |
| 11 | Regen is tree-position based: the changed feature and everything after re-runs | https://cad.onshape.com/FsDoc/debugging-in-feature-studios.html |
| 12 | Array-item inner parameter names must be unique across the whole feature; arrays cannot nest | https://cad.onshape.com/FsDoc/uispec.html; ref:457-465 (`addQ` prefix) |
| 13 | Type tags are bound to the declaring module in the import graph; `is EmbeddedVariables` across different library elements is unverified -> gate on the predicate, never on the tag | https://cad.onshape.com/FsDoc/relational.html; raw 01 FOLLOW-UP 1; EV:295 vs LIB:151 |
| 14 | `@setVariable` / `@setQueryVariable` accept any name; only the std features validate. A hidden `toString(id)` slot is legal | https://forum.onshape.com/discussion/17100/any-reason-we-cant-use-other-punctuation-in-variable-names; std/context.fs:260-263; LIB:319 |
| 15 | `toString(Id)` renders `[ F... ]`, which fails the regex in fact 5 -> unreachable from `#`; visibility in the variables table UNVERIFIED | std/string.fs:26-38; std/variable.fs:802 |
| 16 | `common.fs` (2878) does NOT re-export `queryVariable.fs`; import it explicitly for `setQueryVariable`. It does export feature.fs, featureList.fs, variable.fs | std/common.fs:16,17,63 (no queryVariable line); raw 01 FOLLOW-UP 2 |
| 17 | A FeatureList is a map Id -> function; only `keys()` is needed to address sources | std/featureList.fs:15-34; EV:292 |

Later, if needed (NOT built now): derive keeps attributes and strips only sheet-metal ones (std/derive.fs:138-145);
attributes survive split/pattern/boolean (std/attributes.fs:38-43); variables never cross a derive. The known
route for derive/API use is one attribute per key (`dm:<map>:<key>`) on the evaluated entities (raw 07 sec 2).

## 3. Reese's Extract Variables and Derek's Query Variable +, in brief

Reese, producer (LIB). A feature calls `embedVariableMap(context, id, m)` (LIB:313-320): precondition
`canBeEmbeddedVariables` (LIB:151-185: exactly `{ "variable" : {...}, "query" : {...} }`, nonempty string keys,
Queries only under `query`), then `setVariable(context, toString(id), m as EmbeddedVariables)` (LIB:319).
`embedFeatureDefinition` (LIB:338-358) splits a definition map by `is Query`. Descriptors `extractableVariable`
/ `extractableQuery` (LIB:214-295) carry description and DebugColor; plain values are also legal.
Reese, consumer (EV). One feature, FeatureList `sourceFeature` (EV:43-44). For each source id:
`try silent(getVariable(context, toString(featureId)))`, gate `is EmbeddedVariables && canBeEmbeddedVariables`
(EV:294-296). Merge (EV:283-319): variable keys first-wins with a warning listing duplicates, query keys
`qUnion`. Keys are chosen by two arrays (`variableKey`, `queryKey`) or "Get all" booleans (EV:56-62,
:104-116); "Add all" buttons in editing logic REPLACE the arrays with every available key (EV:244-269).
Publish: variables into one map `setVariable(prepend_mapName, ...)` (EV:179-181) or individually; queries via
`setQueryVariable(name, desc, qUnion(evaluateQuery(context, q)))` (EV:422-424) -- frozen transients, no
identity tracking. "Print all" lists `[Variable] key -- desc` (EV:387-402). Missing keys -> one warning
(EV:207-209).

Derek, Query Variable + (ref). One feature = one query variable. `SelectionType` enum (ref:43-95) drives a
~200-line predicate (ref:246-454); "additional queries" reuse it with every field prefixed `addQ`
(ref:457-465, fact 12). Body (ref:800-823): dispatcher `mapSelectionTypeToQuery` (ref:874-906), left fold of
`qUnion`/`qSubtraction`/`qIntersection`, then `qUnion(makeRobustQueriesBatched)` unless evaluate-on-use,
then `setQueryVariable`. `checkQueryVariableName` (ref:1419-1432) copies the private std check.

Keep. From Reese: the producer contract (hidden `toString(id)` variable, `variable`/`query` split, optional
descriptors, `embedVariableMap` + `embedFeatureDefinition`), the FeatureList read with `try silent`, the
merge rules and their warnings, "Print all", the "Add all" buttons. From Derek: `checkQueryVariableName`,
the std freeze branch (fact 9), and -- only if the composer is built (sec 9) -- the dispatcher shape, the
boolean fold, and the `addQ` remap (ref:1332-1346).

Drop. Typed gate `is EmbeddedVariables` (fact 13; predicate only). Reese's `qUnion(evaluateQuery)` freeze
(fact 10; use fact 9). Extract Variables as a separate feature (absorbed into Design map). Reese's
`prepend`/`asMap`/individual-variable modes (one map, always). Derek's ev*-evaluating selection types
(SIZE_COMPARISON, MATCHING_BODIES, TOLERANT_PARALLEL, POSITIONAL_DIRECTIONAL). `LOAD_FROM_DERIVE` and the
`queryVariableName` attribute (out of scope). The second `addQ` predicate copy (one array suffices).

## 4. Options considered

| Option | Shape | Verdict | Reason |
|--------|-------|---------|--------|
| A | One "Design map" feature per design; array of manually selected entries; writes `#core` | FOLD IN | Manual entries are still needed for geometry no producer names; but re-selecting producer outputs by hand is the clutter we are removing |
| B | One "Map entry" feature per entry, read-modify-write `#core` | REJECT | Same tree count as today; a root writing `{}` wipes earlier entries; order-sensitive |
| C | Producers embed Reese-style; one Design map feature per design reads them, adds manual entries, writes `#core` | ADOPT | Zero re-selection for producer outputs; one tree row and one variable per design; producers already know their outputs (research_driven_edge_offset.md) |
| D | Attributes `dm:<map>:<key>` as the primary carrier, `qHasAttribute` consumers (previous decision) | DEFER | Only pays off across derive/API, which is out of scope; costs a kernel tag per key per regen and a second identity system |

Decision (fixed). Option C with A's manual entries folded in. Producers (driven_edge_offset, evaluate_profiles,
...) call `embedVariableMap` from `design_map_query_utils.fs` with plain values; queries stay symbolic. One
"Design map" feature per design sits after its producers, reads their hidden maps, merges, adds manual
entries, writes one plain map variable `{ "schema" : "designMap/1", ... }` (no type tag), and publishes the
few Query entries the user marks `publish` as query variables `<map>_<key>` with the std robust freeze.
Consumers read keys by name (sec 6). No attributes, no derive loader, no separate publish feature.

## 5. Design map feature spec (`design_map.fs`)

Imports: `onshape/std/common.fs`, explicit `onshape/std/queryVariable.fs` (fact 16), `design_map_query_utils.fs`.

### Parameters

| Param | FS type / annotation | Notes |
|-------|----------------------|-------|
| `mapName` | `is string`, Name "Map", MaxLength 256 | `verifyVariableName(context, mapName, "mapName")` (regex + not a QV name), std/variable.fs:808-823 |
| `description` | `is string`, Default "" | stored on the map variable and on published QVs lacking a descriptor description |
| `sources` | `is FeatureList`, Name "Sources" | producers; only `keys()` used (fact 17); may be empty |
| `printKeys` | `is boolean`, Default false, Name "Print keys" | Reese "Print all": `println("[Variable] key -- desc")` / `[Query]` per available key, plus a warning to turn it off (EV:145-146, :387-402) |
| `getAll` | `is boolean`, Default false, Name "Get all embedded" | every embedded key not claimed by an entry lands in the map under its own name; nothing is published |
| `entries` | `is array`, Name "Entries", Item name "entry", Item label template `#e_key` | do NOT mark "Driven query" (removes the add button, raw 07 sec 1a) |
| `e_key` | `is string`, Name "Key" | output key; identifier (fact 5); unique within the feature; `schema` reserved |
| `e_kind` | `is DesignMapKind`, Default EMBEDDED | `enum DesignMapKind { EMBEDDED, QUERY, LENGTH, ANGLE, NUMBER, REFERENCE }` (utils) |
| `e_sourceKey` | `is string`, shown when kind == EMBEDDED | embedded key to take; the "Add all" buttons set it equal to `e_key` (rename = edit `e_key`) |
| `e_selection` | `is Query`, shown when kind == QUERY, Filter `AllowMeshGeometry.YES && AllowFlattenedGeometry.YES` | raw selection; the optional composer (sec 7) may replace it |
| `e_length` / `e_angle` / `e_number` | `isLength(..., LENGTH_BOUNDS)` / `isAngle(..., ANGLE_360_BOUNDS)` / `isReal(..., bounds)` | typed like std/variable.fs:184-198; free-form `isAnything` inside an array is untested (sec 8 test 1) |
| `e_ref` | `is string`, shown when kind == REFERENCE | another key of this map, already in `result` (earlier entry or `getAll`) |
| `e_publish` | `is boolean`, Default false | creates QV `<map>_<key>`; ignored with a warning when the value is not a Query |
| `addAllVariables` / `addAllQueries` | `isButton(...)`, Name "Add all variables" / "Add all queries" | editing logic (below) |

Editing logic (`designMapEditLogic`, "Editing Logic Function"): acts only on the two buttons (EV:248-249).
Reads the merged source map from the pre-feature context exactly as the body does, then APPENDS one
`{ "e_key" : k, "e_kind" : EMBEDDED, "e_sourceKey" : k }` per available key whose `e_sourceKey` is not already
in `entries`. Appending (not Reese's replace, EV:255-268) keeps manual and renamed entries.

### Body algorithm

1. `verifyVariableName`; `available = readSources(context, id, definition.sources)` (utils): per id,
   `try silent(getVariable(context, toString(featureId)))`; undefined or `!canBeEmbeddedVariables` -> warning
   naming the source (suppressed, deleted, or not a producer), skip. Normalize descriptors to
   `{ value, description }` on read (copy of EV:447-464). Merge: variable keys first-wins, duplicates collected;
   query keys `qUnion` of the symbolic values (EV:283-319).
2. `printKeys` -> println every available key with description; warning to turn it off.
3. `result = { "schema" : "designMap/1" }`. Per entry `i`: `verifyVariableNameIsValid(e_key, faultyArrayParameterId("entries", i, "e_key"))`;
   `schema` or already present -> error.
4. Kind dispatch (`designMapEntryValue`, utils) -> `{ value, description }` or undefined:
   EMBEDDED: `available.variable[e_sourceKey]` else `available.query[e_sourceKey]` else undefined (-> missing list).
   QUERY: `e_selection`. LENGTH/ANGLE/NUMBER: the typed value. REFERENCE: `result[e_ref]` else error on `e_ref`.
5. Query value that evaluates empty -> error on the entry (`e_selection` or `e_sourceKey`).
6. `e_publish && value is Query`: `qvName = mapName ~ "_" ~ e_key`; `checkQueryVariableName(context, qvName)`;
   `setQueryVariable(context, qvName, description, qUnion(makeRobustQueriesBatched(context, value)))` (fact 9).
7. `result[e_key] = value` -- the Query stays SYMBOLIC in the map; only the QV is frozen.
8. `getAll`: every available key not yet in `result` and not consumed as an `e_sourceKey` -> `result[key] = value`.
9. Warnings: missing embedded keys (one message listing them, EV:207-209); duplicate variable keys across
   sources (first source wins, EV:212-215). Then `setVariable(context, mapName, result, description)`.

### Schema, naming, merge, errors

- Map: `{ "schema" : "designMap/1", "<key>" : Query | ValueWithUnits | number | map, ... }`; keys are identifiers
  so `#core.thickness` autocompletes (fact 6); no `as` tag.
- Producer map (LIB:142-177): `{ "variable" : { key : value | descriptor }, "query" : { key : Query | descriptor } }`
  in variable `toString(id)`; producers may pass plain values (descriptors optional, sec 9 Q4).
- QV name `<map>_<key>` (fact 5; distinct string from `<map>`, so fact 3 does not bite).
- Merge: variable first-wins + warning; query `qUnion`; an entry key beats a `getAll` key; entries are ordered
  so REFERENCE can only look backwards.
- Errors (feature fails, every `#core.*` reader fails -- intended, raw 07 sec 1b): invalid map name; invalid,
  duplicate, or reserved key; Query entry selecting nothing; REFERENCE to unknown key; publish name held by an
  ordinary variable. Warnings only: unreadable source; missing embedded key; duplicate keys across sources;
  publish on a non-Query. No `try` beyond the one `try silent` read.

```
// design_map.fs body excerpt; readSources / designMapEntryValue / checkQueryVariableName from utils
var available = readSources(context, id, definition.sources);   // warns per unreadable source
var result = { "schema" : "designMap/1" };
var missingKeys = [];
var consumed = [];
for (var i = 0; i < size(definition.entries); i += 1)
{
    const e = definition.entries[i];
    const keyParam = faultyArrayParameterId("entries", i, "e_key");
    verifyVariableNameIsValid(e.e_key, keyParam);
    if (e.e_key == "schema" || result[e.e_key] != undefined)
    {
        throw regenError("Key '" ~ e.e_key ~ "' is reserved or duplicated.", [keyParam]);
    }
    const resolved = designMapEntryValue(context, e, available, result, i);   // { value, description } | undefined
    if (resolved == undefined)
    {
        missingKeys = append(missingKeys, e.e_sourceKey);
        continue;
    }
    if (resolved.value is Query && isQueryEmpty(context, resolved.value))
    {
        throw regenError("Entry '" ~ e.e_key ~ "' selects nothing.", [faultyArrayParameterId("entries", i, "e_selection")]);
    }
    if (e.e_publish && resolved.value is Query)
    {
        const qvName = definition.mapName ~ "_" ~ e.e_key;
        checkQueryVariableName(context, qvName);
        setQueryVariable(context, qvName, resolved.description, qUnion(makeRobustQueriesBatched(context, resolved.value)));
    }
    result[e.e_key] = resolved.value;   // symbolic Query or scalar
    consumed = append(consumed, e.e_sourceKey);
}
result = addUnclaimed(result, available, consumed, definition.getAll);   // step 8
reportMissing(context, id, missingKeys, available.duplicateKeys);        // step 9 warnings
setVariable(context, definition.mapName, result, definition.description);
```

## 6. Consumer patterns

| Consumer | Pattern | Notes |
|----------|---------|-------|
| Expression field (Variable, Extrude depth, ...) | `#core.thickness` | scalar keys only; autocompletes (fact 6) |
| Custom feature, geometry | `getVariable(context, "core").bottom` | symbolic Query, evaluated where used (fact 1); throws if the map is absent -- loud, preferred |
| Custom feature, tolerant | `getVariable(context, "core", {})["thickness"]` | default `{}`, then check the key and raise a clear error; never `definition.x is Query` for map entries |
| Native dialog (Fillet, Extrude) | pick `core_bottom` from the Variable selection dropdown | only entries with `e_publish`; robust-frozen at the Design map's tree position |
| External API tool (same studio) | one `evalFeatureScript` returning `getVariable(context, "core")`; for Query entries, `evaluateQuery` to transient ids in the same script | transient ids correlate with body-details / tessellation ids of that microversion only |

## 7. `design_map_query_utils.fs` scope

Belongs there (all `export`, FS 3070, imports common.fs + queryVariable.fs):
- Embed library, ported from LIB: `embedVariableMap`, `embedFeatureDefinition`, `extractableVariable`,
  `extractableQuery`, `canBeEmbeddedVariables`, `canBeExtractableVariable`, `canBeExtractableQuery`,
  `canBeEmbeddedEntry`; the type declarations may stay for producer-side diagnostics but no consumer path
  tests a tag.
- `enum DesignMapKind`, the `entries` array predicate, `readSources`, `normalizeEmbeddedValue` (EV:447-464),
  `designMapEntryValue` (kind dispatch, sec 5 step 4), `addUnclaimed`, `checkQueryVariableName` (ref:1419-1432).
- Optional composer (sec 9 Q3): reduced `SelectionType` {SELECTION, CREATED_BY, OWNED_BY, GEOMETRY,
  EDGE_CONVEXITY}, `mapSelectionTypeToQuery`, per-operand `BooleanOperationType` + left fold (ref:802-815),
  `addQ` remap (ref:1332-1346) -- as the kind = QUERY selection helper, never a feature of its own.

Not there: `setVariable` / `setQueryVariable` / `reportFeatureWarning` calls (the feature owns side
effects); ev*-based selection types; attribute tagging; a derive loader; anything from `Reese_Test_Feature.fs`.

## 8. Risks and Onshape tests

1. Expression field inside an array item (raw 07 risk 1). Setup: throwaway feature, `entries[]` with
   `isLength` / `isAngle` / `isReal` and one `isAnything`. Observe: type `12 mm`, `#foo`, `{ "a" : 1 }`;
   `println`, `setVariable`, read `#m.k` in a Variable. Retires: whether typed kinds are mandatory.
2. Hidden `toString(id)` variable. Setup: producer calls `embedVariableMap`. Observe: variables table /
   `#` autocomplete in an expression field. Retires: fact 15 (is the slot invisible, as LIB:299-301 intends?).
3. Predicate-only gate. Setup: producer imports utils from this document; consumer too; embed with and
   without `as EmbeddedVariables`. Observe: Design map reads both; `printKeys` lists them. Retires: fact 13
   as a design property (no tag test on the consumer path).
4. Suppressed / deleted producer in `sources`. Setup: suppress one producer, delete another. Observe: Design
   map yields a warning naming the source, the rest of the map still writes. Retires: warning-not-error policy.
5. Dropdown pickability (raw 05 FOLLOW-UP 2, exact test). Setup: Variable `core` Any = `{ "t" : 12 mm }`;
   QV `bottom`; Variable `m` = `{ "q" : #bottom }` (does the expression accept `#bottom`?). Observe: open
   Fillet -> Variable selection dropdown: is `m` / `m.q` listed (expected no); can `#m.q` be typed in the
   selection box; confirm `#bottom` absent from a length-field autocomplete. Retires: map Query entries are
   never pickable natively -> `e_publish` is the only route.
6. QV dropdown ordering. Setup: publish `core_bottom`, `core_top`, `sidewall_x`, then 40 names. Observe:
   order (alphabetical vs tree) and usability. Retires: publish cap (if unusable, cap at ~10).
7. Regen blast radius (raw 07 risk 3a). Setup: Design map with 40 entries, `#a`/`#b` readers, one slow
   feature after it. Observe: edit one entry; feature-list performance readout shows what re-ran; break one
   entry and confirm the whole map errors loudly. Retires: no hidden short-circuit assumed (fact 11).
8. Freeze survival. Setup: publish `core_bottom` via `makeRobustQueriesBatched`; downstream fillet, split, and
   move-face on the tagged body. Observe: `core_bottom` in a later Fillet still resolves. Retires: fact 9
   tracking through identity-preserving edits (vs Reese's EV:423 transient freeze, which is expected to fail).

## 9. Open decisions

1. Map granularity: one Design map per design (`core`, `sidewall`, `topsheet`) vs per subsystem. Recommend
   per design; split only past ~30 entries.
2. Publish any QVs at all? Recommend few: only entries picked in native dialogs, after tests 5-6.
3. Build the composer now? Recommend no: ship kind = QUERY with a raw selection; add the reduced composer only
   when re-selecting geometry proves painful.
4. Descriptors vs plain values in producers. Recommend plain values by default, descriptors only on the
   handful of keys `printKeys` should explain (its per-key text comes from producer descriptors, EV:387-402).
   Descriptor tags match because producer and consumer both import the same design_map_query_utils tab.
5. Accept a Variable Studio reference in Design map (fact 7) for cross-studio scalars? Recommend not now;
   revisit with the "Later" note if a derived studio needs `#core.*`.
6. Deletion of `Reese_*.fs` (3) and `query_varialble_ref.fs`. Recommend: delete immediately after the port
   compiles in Onshape and test 3 passes; nothing else imports them.
7. Key and map naming: recommend lowerCamel keys (`bottomFaces`), lowercase design nouns for maps (`core`),
   `schema` reserved; QVs become `core_bottomFaces`.
