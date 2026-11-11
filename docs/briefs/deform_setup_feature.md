# Deform setup feature: context from the backend project, and what to build in curveMapping_public

Written 2026-11-11 by the eocProductData (Python backend) session. A brief for an expert FeatureScript agent: nothing
here changes a `.fs` file. Claims are labelled **verified** (read in code/docs), **inferred**, or **unverified**.
Backend project: `C:\Users\jed.yeiser\Projects\eocproductdata` (design notes: `backend\deform\DESIGN.md`).
Companion brief: `topology_preserving_deform.md` (the deform engine itself runs on that backend, not in FeatureScript).

## Why a setup feature

The eocDataTools Onshape app (Geometry > Deform) deforms geometry on our server. Today the user TYPES the names of
Part Studio **query variables** for: the From chain, the To chain, optional reference points (a vertex or mate
connector on each chain), and the Source (bodies/faces/wire bodies to deform). The backend resolves them in ONE
`evaluateFeatureScript` call (`backend\deform\services\onshape_chains.py`: `CHAINS_FS`, `_FS_REFS`, `_FS_SOURCE`).
Typing names is clumsy, clutters the studio with variables, and carries no names or options with the geometry.

Goal: a **Deform setup** feature in `curveMapping_public` (versioned; our test document will use that version). The
user picks the geometry with Onshape's native selection UI inside the feature dialog, and the Part Studio feature list
becomes the single source of truth. The feature does not deform anything; it TAGS geometry and records options. The
deform itself stays on the backend.

## Defaults I am proposing for the open design decisions (the user has not answered; change them if you know better)

1. One setup feature per deform group (a studio may hold several); no standalone tag features.
2. Query variables stay as a fallback in the backend; studios without a setup feature keep working.
3. No in-Onshape preview in v1 (the backend draws all previews). Revisit later.
4. Names carried: bodies, wire bodies, faces. Mate connectors only if they can take attributes (see experiment).
5. **The parameter list is frozen on day one** (see `export_tag_handoff.md`, correction 64: never add or retype
   parameters on a published feature). So put every option we will ever want in the first version, or keep the extensible
   part in ONE map-valued attribute (below) rather than in parameters.

## Interface contract (what our backend will read)

Pattern to reuse: `publish_tools\export\export_tag.fs` already sets a schema-versioned attribute (`exportTag/1`) and the
app reads it with one `evaluateFeatureScript`. Do the same:

- Attribute name `deformRole/1` (schema-versioned). Value is a MAP, so new keys never need new parameters:
  `{ group : string, role : string, ... }`.
  - `role` = `"from"` | `"to"` | `"source"` | `"refFrom"` | `"refTo"`.
  - On `from`/`to` entities (edges or wire bodies) also carry the options as keys: `prevailing` (`"+x"`..`"-z"` or
    `"none"`), `extension` (`"tangent"` | `"osculating"`), `flipFrom`, `flipTo` (booleans), `maxKinkDeg`,
    `smoothMaxDeviationMm`, `accuracy` (`"standard"` | `"fine"`), `outputName`.
  - On `source` entities: bodies, faces, or WIRE bodies (a wire body is read edge by edge by the backend).
  - On `refFrom`/`refTo`: a vertex (preferred). A mate connector if it can take an attribute (unverified).
- `group` is the user's group name (a string parameter), unique per feature. The backend keys its records on
  (documentId, elementId, group).
- The backend's lambda will do roughly `evaluateQuery(context, qHasAttribute(qEverything(EntityType.BODY), "deformRole/1"))`
  (and EDGE, FACE, VERTEX), read each attribute with `getAttribute`, and group by `group` and `role`. It needs
  nothing else from the feature.
- Also write the same map(s) to the feature's output via `embedStandardOutputs` (as `export_tag.fs` does) if that is
  cheaper to read than the attribute scan; the backend can use either. State which is the supported one.
- Original names: the backend already reads each body's name with `getProperty(..., PropertyType.NAME)`. Put the
  original body/wire name in the attribute too (`name`), so a rename of the source later does not lose the pairing.

## What to verify first (the smallest Onshape-side experiment, about 1 API call to read back)

A throwaway feature that calls `setAttribute` on: a face, an edge, a wire body, a body, a vertex, and (if allowed) a
mate connector. Then run, after it: a split, a boolean union, `opReplaceFace`, a pattern. Read everything back in ONE
`evaluateFeatureScript` with `qHasAttribute` / `getAttributes`. Record per entity type:

- does the attribute survive each downstream operation (the docs in `std\attributes.fs` cover splits/merges only);
- can wire bodies and mate connectors take attributes at all;
- the attribute value size limit (open item in `export_tag_handoff.md`);
- the time of `qHasAttribute(qEverything(...))` on a heavy studio (cold regeneration can take 60 s or more);
- what happens when the feature is suppressed, rolled back, or fails (stale or missing tags). The backend must know
  the tags are not current; consider a feature-level "valid" key.

## Behaviour requirements

- Native selection UI for From/To (edges or wire bodies), reference points, Source (bodies, faces, wire bodies).
- Several setup features in one studio, one per group, must not interfere.
- Rolling the feature list back past the feature must remove its tags (a consequence of using attributes).
- No operations that change geometry. Cheap to regenerate.
- Publish via the usual `curveMapping_public` flow (see `port_public.py` / the publish docs); then tell the backend
  session the published version, and the test document will switch to it.

## How the backend will use it (so you can see the other half)

- A new `fs_source`-style script reads the tags in one call and returns the same structure the query-variable path
  returns today (`from`, `to`, `refs`, `source`, plus `groups` and the options), so the rest of the pipeline is unchanged.
- A small record table (no geometry) keyed on the group remembers where each group's last output went, so updates
  pre-select their target. That lives in the backend, not in the feature (parameters are frozen; targets change).
- Mate connectors that belong to a deformed wire will be re-created by API after the deform (they cannot be named from
  FeatureScript). If they cannot take attributes, the backend will read their names and frames from the feature's
  output map instead: include them (name, owner body id, origin, axes) in the output if you can.

## Out of scope

The deform maths, the STEP export/import, renaming, and updating imports. Those are done or planned on the backend.
