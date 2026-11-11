# Deform tag: contract for the API developer

Feature: `Deform tag` (`curveMapping_public/deformTag.fs`, FeatureScript 3083, document curveMapping_public d12583277c42b00c63a9b7b7).
It changes no geometry. It writes attributes; the app reads them with one `evaluateFeatureScript`.
Answers the brief `deform_setup_feature.md`. Status labels: **verified** (read in code), **inferred**, **unverified** (needs the live test).

## Where to place it: at the BOTTOM of the feature tree

Put every Deform tag as the LAST features in the Part Studio. A tag only sees the geometry that exists above it, and
attributes follow entities through later features only partly (std documents splits and merges; nothing was tested for
booleans, replace face or patterns, and no live read-back was done). Tags at the bottom have no later features to lose
them. The feature description says this too. Rolling the feature list back past a tag removes its tags, which is intended.

## What the user fills in

| Dialog field | Accepts | Tag effect |
|---|---|---|
| Deform name | text, unique in the Part Studio | `group` in every tag. It is the record key AND the name of the result (step file etc). Renaming starts a new record. |
| From chain / To chain | edges or wire bodies (several allowed) | role `from` / `to` on each entity, with the options |
| Reference point on From / To | one vertex or mate connector each | roles `refFrom` / `refTo`, with `originMm` |
| Source (array of items) | each item: ONE body (solid, surface, wire) + a list of mate connectors | role `source` on the body; connectors in `mateConnectors` |
| Options (collapsed) | prevailing direction (default +X), end extension, flip From, flip To, max kink angle, smooth max deviation, accuracy | keys on the `from` / `to` / `refFrom` / `refTo` tags |

Empty connector list = that deformed body owns no connectors. A body may appear in two items (connectors merge, tagged once).

## The attribute (the thing to read)

- Attribute name: `deformRole` (NOT `deformRole/1`; the schema is inside the value).
- Value: an ARRAY of tag maps (an entity can belong to several deforms/roles). Scan: `qHasAttribute(qEverything(EntityType.BODY | EDGE | FACE | VERTEX), "deformRole")`, then `getAttribute`, loop the array, filter on `group` and `role`. Mate connector bodies are BODY entities (`BodyType.MATE_CONNECTOR`); whether they carry a tag is **unverified**, see below.
- A later Deform tag with the same (group, role) on the same entity replaces the earlier entry; other entries are kept.

Tag map keys (all roles): `schema` ("deformRole/1"), `group`, `role` ("from" | "to" | "source" | "refFrom" | "refTo"), `index` (pick order within the role; sources: item order), `name` (owner body name captured when the dialog was last edited; empty if the feature was inserted by API), `feature` (the feature id string), `extra` (hidden free text, reserved for future options).

On `from`, `to`, `refFrom`, `refTo` also: `prevailing` ("+x".."-z" | "none"), `extension` ("tangent" | "osculating"), `flipFrom`, `flipTo`, `accuracy` ("standard" | "fine"), and only when the user set them: `maxKinkDeg` (1-30), `smoothMaxDeviationMm` (absent = default / off; the engine must not read a missing key as 0). On `from` / `to` tags: `refFromOriginMm`, `refToOriginMm` ([x, y, z] mm) when a reference point was picked, carried there so a connector that cannot take a tag still reaches you.
On `refFrom` / `refTo`: `originMm`.
On `source`: `mateConnectors`: [{ `id`, `originMm`, `xAxis`, `zAxis` }]. `xAxis`/`zAxis` are unitless world directions; `id` is the transient id string, which equals the REST part id (**inferred** from Export tag's body ids; confirm for connectors).

Semantics of the options (from `backend/deform/services/preflight.py`, **verified**): `prevailing` turns the From chain so its start-to-end chord points along the axis; the To chain is then matched to From; `flipFrom` / `flipTo` reverse a chain AFTER that. `prevailing: "none"` keeps Onshape's order.

## Mate connectors: how they are meant to work

- FeatureScript cannot read which body owns a connector (`qOwnerBody` returns the connector itself) and cannot name one. That is why ownership is explicit: the user lists the connectors under the body.
- Names: the tag carries only `id`. The app reads the name through the API (**unverified**: confirm the API returns a mate connector's name for that id).
- Mapping frames: the existing in-Onshape Deform maps origin like a point and rotates the axes with the From->To frame (`mapCoordSystems`). The backend engine is free to map axes by the deformation's pushforward instead, then orthonormalise.
- The app re-creates each connector by API on the deformed body and names it.

## Known limits and open items

1. **Unverified, and no live read-back is planned**: attributes on mate connector bodies (the feature does not fail if refused; it warns and carries the origin on the chain tags), on sketch edges, and survival across split / boolean / replace face / pattern. Mitigation: place tags at the bottom of the feature tree (section above).
   The feature's info message in the tree lists the deform name and the body names of the From chain, the To chain and the sources, with a mate connector count after a source that brings some. Names are captured by the editing logic (empty if inserted by API; the message then falls back to a pick count).
2. Duplicate deform names are warned only against EARLIER tags in the feature tree.
3. The group-name scan (`qEverything` x4) runs on every regen of the feature; cost on a heavy studio is unmeasured.
4. Names are captured by the editing logic only; API-inserted features carry empty names. Pick order is assumed stable between editing and regen.
5. The feature is not in a failed-state marker for stale tags: the app should check the feature's own status in the feature list and the `feature` id in the tag.
6. Parameters are frozen once a version is published (correction 64): new options go through `extra`.
7. Faces are not a source (bodies only); composites are not a chain.
8. Output: no `embedStandardOutputs`; the attribute scan is the supported read.
