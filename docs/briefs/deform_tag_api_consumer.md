# Reading the Deform tag from the API tool (brief for the agent that builds the consumer)

You are wiring the eocDataTools backend (`C:\Users\jed.yeiser\Projects\eocproductdata`, package `backend\deform`) to read the
data the **Deform tag** feature leaves in an Onshape Part Studio, instead of the typed query-variable names it reads today.
Read this file first, then `docs/briefs/deform_tag_contract.md` (the exact tag format; it is the source of truth for keys).
Both are in the featurescripts repo: `C:\Users\jed.yeiser\documents\featurescripts`. You change the backend, not the feature.
Labels: **verified** (read in code), **inferred**, **unverified** (check it yourself, cheaply).

## What exists on each side

- The feature (`curveMapping_public/deformTag.fs`, pushed, not yet versioned by the user) changes no geometry. It writes the
  attribute `deformRole` on the picked entities. The value is an ARRAY of tag maps (`group`, `role`, `index`, `name`, options ...).
  `group` is the user's **Deform name** and is also the name of the result (step file etc.). **verified**
- Today's backend path (`backend\deform\services\onshape_chains.py`, **verified**) resolves query-variable NAMES in ONE
  `evaluateFeatureScript` call (`CHAINS_FS`, `_FS_REFS`, `_FS_SOURCE`, `fs_source(...)`) and returns
  `{ "from": {"edges": [Edge]}, "to": {"edges": [...]}, "refs": {"from": [x,y,z] m, "to": ...}, "source": {"faces": [], "bodies": [{id, name, box}], "wires": [{id, name, edges}]} }`.
  Downstream code (`parse_refs`, `parse_source`, `curves_from_edges`, `engine_cli.py` options, `body_groups` / `wire_groups`) consumes that shape.

**Goal: a new script (`fs_source_tags(deform_name)` or similar) that returns the SAME shape, plus `options`, `mateConnectors`
and `groups`, so the rest of the pipeline is unchanged. Keep the query-variable path as the fallback.**

## Step 1: find the tags (one call)

Scan, in one `evaluateFeatureScript`:

```
qHasAttribute(qEverything(EntityType.BODY), "deformRole")   // source bodies, wire bodies (chains), mate connectors (maybe)
qHasAttribute(qEverything(EntityType.EDGE), "deformRole")   // chain edges
qHasAttribute(qEverything(EntityType.VERTEX), "deformRole") // reference vertices
```
then `getAttribute(context, {"entity": e, "name": "deformRole"})` on each hit: an array of maps; loop it; keep maps whose
`group == <deform name>`. Group by `role`: `from`, `to`, `source`, `refFrom`, `refTo`; sort by `index`.
A list of the distinct `group` values (the "groups" the user has) comes from the same scan: return it so the UI can offer a picker.
The attribute NAME is `deformRole` (not `deformRole/1`; `schema: "deformRole/1"` is a key inside each map). Reject maps with another schema.

## Step 2: build the old payload from the tags

| Old key | From tags |
|---|---|
| `from.edges` / `to.edges` | the tagged EDGE entities of role from/to, plus the member edges of tagged WIRE bodies of that role (`qOwnedByBody(wireBody, EntityType.EDGE)`). Reuse the per-edge block of `CHAINS_FS` (`evCurveDefinition`, `evEdgeTangentLine`, `evApproximateBSplineCurve`) as a helper. |
| `refs.from` / `refs.to` | vertex tag: `evVertexPoint`. Otherwise the tag's `originMm` (mm; the old code expects METRES, divide by 1000). If a mate connector could not take a tag, `refFromOriginMm` / `refToOriginMm` ride on every from/to tag: use them. |
| `source.bodies` | tagged BODY entities of role `source` that are not wire bodies: `{id, name, box}` exactly as `_FS_SOURCE` builds it (`transientQueriesToStrings`, `getProperty NAME` live, `evBox3d`). Prefer the live name; the tag's `name` is the fallback (it is empty for API-inserted features). `faces` is `[]` now (faces are no longer a source); `body_groups` needs `bodies` + `box`. |
| `source.wires` | tagged WIRE bodies of role `source`, edges as in `_FS_SOURCE`. |
| `options` | the keys on any from/to tag map, to `engine_cli` names: `prevailing`, `extension`, `flipFrom`, `flipTo`, `accuracy` ("standard" -> engine defaults, "fine" -> the fine edge/face accuracy you choose), and ONLY if present `maxKinkDeg`, `smoothMaxDeviationMm` (a missing key means "engine default", never 0). All from/to tags of one deform carry the same options; take the first and warn if they differ. |
| `mateConnectors` | per source body: `source` tag `mateConnectors` list: `{id, originMm, xAxis, zAxis}`; absent/empty = that body owns none. |

Units: tag values are mm (`originMm`) or unitless (axes); the old payload is metres. FeatureScript returns ValueWithUnits divided by
`meter` in the existing code; keep one convention.

Safe names: the deform name is user text and goes into a FeatureScript string literal. Reuse `clean_name` (rejects `"`, `\`, newlines).

## Step 3 (after the deform): mate connectors

FeatureScript cannot name a mate connector and cannot read which body owns one, so ownership is explicit in the tag. The job:
1. Deform each connector's frame with the same deformation (origin like a point; axes by the pushforward of the map, then orthonormalise;
   the in-Onshape Deform `mapCoordSystems` in `curveMapping\curveMappingCore.fs:1500` rotates axes with the From->To Frenet frames, for comparison).
2. Read each connector's NAME through the API (**unverified**: confirm that the Part Studio features list or a body/mate-connector listing returns it for a tag `id`;
   the tag `id` is a transient id string that **inferred** equals the REST id).
3. Re-create each connector by API in the import Part Studio, owner = the deformed body that came from the tagged source body, with the same name.
   Match the deformed body to its source body the way `body_groups` already does (by name / box). Connector ids are NOT stable across the re-import.
Budget (read `CLAUDE.md` "Onshape API budget"): everything above should be ONE evaluate call plus the names lookup; do not poll; set `ONSHAPE_CALL_CAP`.

## Freshness and traps

- Tags are valid for the tree state you evaluate at. The user places Deform tags at the BOTTOM of the feature tree; evaluate with the full tree (no rollback) or at the tag feature's own index.
- Suppressing or rolling back a tag removes its attributes, so a missing group = "tag off / not there". Compare the tag's `feature` (feature id) with the feature list if you need to tell "stale" from "absent".
- Two Deform tag features with the same name merge in your scan; the feature warns about it, you should too (distinct `feature` values in one group).
- Entities picked in two tags carry two entries in the array; always filter on `group` AND `role`.
- A tag on a mate connector body may be absent (attributes on connector bodies are **unverified**); never require it, use the source tag's `mateConnectors` and the `*OriginMm` keys.
- Sketch edges may not carry attributes (**unverified**); the chain pickers accept edges, so if a scan finds no `from` edge for a group whose feature exists, say so instead of failing silently.
- No `println` in anything that ships; return data.

## What to verify yourself first (cheap, one evaluate each)

1. Create a Deform tag in a test Part Studio, then run the scan: do from/to edges, wire bodies, source bodies, vertices, mate connectors show up with tags?
2. Does a source tag's `mateConnectors[].id` match what the REST API lists for that connector, and does the API return its name?
3. Time `qHasAttribute(qEverything(...))` on a heavy studio (cold regeneration can take 60 s).
Record the answers in `deform_tag_contract.md` ("Known limits"), not in a new file.

## Out of scope

The deform maths, STEP export/import, the Onshape feature itself, versioning. The user creates the `curveMapping_public` version
(the test document then switches to it); do not create versions or scratch documents without asking.
