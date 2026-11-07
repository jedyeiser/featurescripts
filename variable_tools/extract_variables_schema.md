# Extract variables: data contract (manifest schema extractManifest/2)

Written by "Extract variables" (`variable_tools/extract_variables.fs` + `extract_variables_utils.fs`, one per
block of the tree); producers embed via `variable_tools/extract_outputs.fs` (`embedStandardOutputs`, pinned by
Variable_tools version). Refreshed 2026-11-06 (the files were `driven_offset/design_map*.fs` before the move). Scope: the Part Studio that
wrote the variables. Decision 2026-09-11: queries are NEVER stored inside map variables.
Selections are published as native query variables; maps hold scalars only.

Revised 2026-09-23: standard keys, sources without an embedded map, entry types, per-source
addressing. `extractManifest/1` (merged sources, first-wins) is retired. The schema string is still
`extractManifest/2` (`EXTRACT_MANIFEST_SCHEMA`).

## Standard keys (every source offers them)

| key | what |
|---|---|
| `output` | bodies the feature made or modified |
| `outputFaces` / `outputEdges` / `outputVertices` | entities of those bodies |
| `inputs` | what the feature consumed (producers only; empty otherwise) |
| `modifiedFaces` / `modifiedEdges` / `modifiedVertices` | what the feature created or last modified in place and nothing has changed since (offered by EVERY source, producer or not; resolved on first use) |

A producer embeds them with `embedStandardOutputs(context, id, { output, outputDescription,
inputs, variables, queries })`. A source that embeds nothing (any std or third-party feature)
offers `output*` as `qCreatedBy(featureId, BODY/FACE/EDGE/VERTEX)`. A feature that only
MODIFIES bodies (Mutual Trim+, Merge curve editing in place) must embed `output` itself.
Producer keys are always present (`qNothing()`, `0`, `"none"`, `[]` when not applicable).

## Producer keys

The 2026-09-23 table listed keys that have since changed (Clean wire lost curveCount / inputCount / maxDeviation,
Offset+ lost its corner counts, Mutual Trim+ gained keptFaces1/2, and there are new producers: Enclose+,
Join profile surfaces, Move face+, Referenced part). A hand-kept table goes stale, so the keys are no longer listed here:

- The source of truth is each feature's own `embedStandardOutputs(...)` call (`extractableVariable` / `extractableQuery` entries).
- Per-family lists: Reference_Side explainer 2.5, Curve_tools explainer 2.6, driven_offset explainer.
- In a model, **Print keys** lists every address with its description.

## Addressing

Sources are numbered 1.. in tree order. A key offered by one source is addressed by name
(`trimEdges`); a key offered by several is `key@n` (`output@2`). Never merged. The default
published name is the address with `@` -> `_` (`output_2`), under the feature's prefix.

## Entry types

| type | inputs | publishes |
|---|---|---|
| Source key | key | the value / query as is; queries frozen unless Evaluate on use |
| Filtered | key, entity type, body type, name contains, largest N | subset |
| Closest to point | key, entity type, point | nearest entity |
| Shared edges | key, second key | edges common to both (bounding faces count) |
| Chain end | key, point, nearest/farthest, vertex/edge/both | the free end of a chain |
| Edges between points | key, two points, other side | whole chain edges between the nearest vertices |
| Bridging curve input | key, point | the end edge and its free vertex, as one query |
| Region | key (faces / bodies), second key (boundary edges, optional), point, publish | faces flood-filled from the face nearest the point without crossing a boundary edge; or the region's boundary edges (one adjacent face in the region); or both |

Composed types are always frozen (std robust freeze) at the Extract variables feature.

Source key / queries have three keeping modes: **Hold** (default: the entities present now,
followed through edits that keep them), **Track** (also what later splits / merges make of them) and
**Evaluate on use** (the producer's query re-run where it is used). Producers publish settled queries
(`settledOutputs`, reference_side_utils): following later edits is the consumer's explicit choice.

Every entry has **Show** (default off): while the feature is being edited it highlights what
the entry resolves to (the producer's `debugColor` when the key has one, else a colour cycled
per entry) and prints `[show] <name>: N entities` -- or `<name> = <value>` -- to the notices.

## What Extract variables publishes

- Ordinary variables `<prefix>_<name>`: `#name`, `getVariable(context, name)`.
- Query variables, same naming: pickable in any selection dropdown.
- Variables from a derived Part Studio (option "Get variables from a derived Part Studio", 2026-11-02): one ordinary
  variable per entry of the picked studio's `exportVariables` map (written there by Add variables for derive or
  Variable + with Export for derive), same name, overwriting; also listed in the manifest `variables`. Pushed,
  not run live.
- Manifest (optional parameter "Manifest" = a variable name):

```
{ "schema" : "extractManifest/2",
  "variables" : { "<name>" : <value>, ... },
  "queries"   : [ "<name>", ... ] }
```

## Fetching (API, same Part Studio)

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

Transient ids are valid for that microversion only. ValueWithUnits / Vector serialisation in
the response is UNVERIFIED. Producer slots are also readable directly:
`getVariable(context, toString(makeId("Fxxx")))` -- the slot name renders `[ Fxxx ]`; use
the one-argument getVariable inside `try silent` (a default of `undefined` is no default).
