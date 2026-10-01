# Extract variables: data contract (manifest schema extractManifest/2)

Written by "Extract variables" (`driven_offset/design_map.fs` tab, one per block of the tree);
producers embed via `driven_offset/design_map_query_utils.fs`. Scope: the Part Studio that
wrote the variables. Decision 2026-09-11: queries are NEVER stored inside map variables.
Selections are published as native query variables; maps hold scalars only.

Revised 2026-09-23: standard keys, sources without an embedded map, entry types, per-source
addressing. `extractManifest/1` (merged sources, first-wins) is retired.

## Standard keys (every source offers them)

| key | what |
|---|---|
| `output` | bodies the feature made or modified |
| `outputFaces` / `outputEdges` / `outputVertices` | entities of those bodies |
| `inputs` | what the feature consumed (producers only; empty otherwise) |

A producer embeds them with `embedStandardOutputs(context, id, { output, outputDescription,
inputs, variables, queries })`. A source that embeds nothing (any std or third-party feature)
offers `output*` as `qCreatedBy(featureId, BODY/FACE/EDGE/VERTEX)`. A feature that only
MODIFIES bodies (Mutual Trim+, Merge curve editing in place) must embed `output` itself.
Producer keys are always present (`qNothing()`, `0`, `"none"`, `[]` when not applicable).

## Producer keys (2026-09-23)

| feature | variables | extra queries |
|---|---|---|
| Driven edge offset | curveCount, cornerArcCount, cornerArcRadii, start/end Action (`none` = no plane), Distance, Squareness, Kink | |
| Driven offset surface | faceCount, offsetCount | offsetWire1..N (empty unless Keep offset wires) |
| Clean wire | curveCount, inputCount, maxDeviation | |
| Evaluate profiles | length, trimmedStart, trimmedEnd | top, bottom, middle, periphery, connectors |
| Mutual Trim+ | trimEdgeCount | trimEdges (the intersection the trim left; tracked through the merge) |
| Map curve | spanStart, spanEnd, toStart, toEnd, stationCount | |
| Merge curve | degree, controlPointCount | mergedEdge |

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

Composed types are always frozen (std robust freeze) at the Extract variables feature.

## What Extract variables publishes

- Ordinary variables `<prefix>_<name>`: `#name`, `getVariable(context, name)`.
- Query variables, same naming: pickable in any selection dropdown.
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
