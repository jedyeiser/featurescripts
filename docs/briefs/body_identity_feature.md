# Body identity: find bodies by NAME and give each a stable identity (brief for the FeatureScript agent)

Written 2026-11-12 by the eocProductData (Python backend) session. Read-only context: it changes no `.fs` file. Labels:
**verified** (read in code), **inferred**, **unverified**. Related: `deform_tag_contract.md`, `deform_setup_feature.md`.

## Why

The backend deforms geometry outside Onshape and brings it into a stand-alone IMPORT Part Studio. Updating the import swaps in new
data, so the imported bodies may get new ids. Downstream studios (features, mates, sketch planes, derives, released parts with part
numbers) should keep working. Part numbers and releases are keyed on part identity, so we want each deformed BODY (surface or part)
to keep a stable identity across updates. Faces and edges do not matter.

## The mechanism (verified from source)

The community feature "Assign Identity" is a thin wrapper (its whole source was pasted by the user, FS 1036):

```
opNameEntity(context, id, { "entity" : definition.entity, "entityName" : definition.identity });
```
with `identity` defaulting to the feature's id as a dotted string (`toIdentity(id)`), `entity` a picked FACE/BODY/EDGE/vertex query
(MaxNumberOfPicks 1). `opNameEntity` is internal/undocumented (`std/geomOperations.fs:1179-1195`: "Assigns name to the entity. This
will allow using qNamed() to query the entity. When historical queries are generated, qNamed() will be used as a shortcut"), and
`std/nameEntity.fs:17-37` says "This name becomes its identity. qNamed(name) will resolve to this entity or its modifications."
`qNamed` has `historyType: CREATION` and the name "should be unique in the part studio" (`std/query.fs:994-1004`).
A forum user built on it and wrote it "is only updating the Body id" (forum 20886).

**The weakness for us:** Assign Identity's `entity` is a STORED SELECTION. After an import update the body has a new id and the
selection must be re-picked by hand (Onshape's tech tip: repair after each update). Our backend cannot click.

## What to build: `Body identity` (in curveMapping_public)

A feature that FINDS the bodies by their current NAME each regeneration and names each one:

- Parameter `names`: the list of body names (one string parameter, newline- or `|`-separated, set by our REST calls; decide
  the separator and handle spaces/quotes). Parameters are FROZEN once published (correction 64): put anything extensible in a
  hidden `extra` string.
- For each name: find the body whose NAME property equals it (`getProperty(context, {entity, propertyType: PropertyType.NAME})`
  over `qEverything(EntityType.BODY)`; surfaces and parts; wire bodies if allowed). If exactly one: `opNameEntity(context,
  id + <stable per-name id component>, {entity: body, entityName: <identity string>})`. The identity string must be deterministic
  from the name (e.g. the name itself, or a fixed prefix + name) and unique in the Part Studio. Do NOT derive it from the
  feature's own id the way Assign Identity does, if that id can change when our REST layer recreates the feature; decide and say why.
- If a name matches zero or several bodies: report a feature WARNING/error naming it (do not silently skip), keep the rest.
- The per-name op id component must not depend on list order (the list can gain or lose names): derive it from the name.
- It changes no geometry. Place it right after the Import feature in the import studio.
- Our backend will add/update this feature by REST (`updateFeatures`), with the names from the Deform tag (the tag already names
  each source body); a name removed from the tag is removed from the list.

## What must be verified (UI-only experiment, ~30 min, scratch document; no REST calls)

Studio `Imp`: an Import feature with TWO named sheet bodies (and one wire body if possible), then `Body identity` listing the two
names. Studio `Down`: derive both bodies from `Imp`; add on each derived body an Extrude/Thicken, a Sketch plane, a Mate connector
and a mate in an assembly. CONTROL: derive the raw import bodies too (no identity). Then, in `Imp`, UPDATE the import with changed
geometry (a bent copy of the STEP; make a version first if the Update dialog requires it), and also test: swap the sheet order in
the file; delete and recreate the Import feature. After each, record in `Down` and the assembly:
1. Do the extrude/thicken, sketch plane, mate connector, mate and drawing resolve, or error? (control vs identity)
2. Do the PART IDS of the derived and downstream bodies change? Compare with the REST parts list (`GET /parts/d/w/e` partId) or
   the feature tree's id display, before and after.
3. Release a downstream part with a part number, update the import, release again: same part number, new revision, or a new part?
4. Does `qNamed(name)` still resolve after the update; does it work on SURFACE bodies and WIRE bodies; does a Derive into
   another studio keep resolving by name or by id (the forum warns the identity "will be different" after a derive)?
5. Does naming change the body's name/appearance/part metadata? Does it survive `opDeleteBodies` of other bodies?

Record the answers in this file under "Known limits" (not in a new file).

## Alternatives already researched (do not redo)

- `opPattern`/`opTransform` copies: the copy's id probably includes the source's identity (std copies several bodies under ONE
  instance name). `opBoolean`: identity stays with the FIRST tool; it creates no new body (a sheet cannot merge with a disjoint
  seed). `opCreateCompositePart`: a wrapper, SOLID/SHEET only. `opExtractSurface` + delete source: a new sheet whose id may or may
  not ignore the source: worth ONE extra variant in the experiment.
- Route "JSON read by a feature": transport is possible (`import(...)` of a blob tab, `BLOB_DATA.jsonData`; `.textData` under
  100 KB) but rebuilding faces in FeatureScript brings back seams and sewing; last resort.
- Route "custom feature calling opImportForeign": id scheme undocumented, getting a blob into a custom feature is the obstacle; dropped.

## Out of scope

The deform maths, the STEP, the import update, the Deform tag feature. The user versions curveMapping_public; do not create
versions or scratch documents without asking.

## Implementation (2026-11-12): Body identity, curveMapping_public/bodyIdentity.fs (pushed, compiles, NOT versioned, NOT tested live)

**Premise correction.** The brief assumed the feature can find a body by NAME during regeneration. It cannot: `getProperty` throws
"Cannot get properties during feature regeneration in current context" inside a feature body (corrections log 36; it works only in the
editing logic and the eval API). So the feature does not search by name. The BACKEND does the search (it reads part names over REST)
and writes the result into the feature; the feature only names what it is pointed at.

**Shape.**
- Parameter `bodies` (array). Each item: `body` (Query, one SOLID / SHEET / WIRE body, set as a deterministic id) and `identity`
  (string, <= 256, the stable name). Plus hidden `extraOptions` (text, reserved; the list is frozen once published, correction 64).
- Per item: `opNameEntity(id + <component from the identity>, {entity, entityName : identity})`. The component is sanitised
  letters/digits plus a length and checksum of the identity, so it never depends on list order or on the feature's own id, and a
  feature re-created by REST names the same bodies the same way. The identity string is used exactly as given (trimmed): the backend
  chooses it (the body name, or a fixed prefix + name) and must never change it once downstream features use it.
- An empty `body` (stale selection), an empty or duplicate `identity`, a body listed twice, a selection of several bodies, or an
  identity that already names something earlier in the tree is a feature WARNING naming the item; the other items are still named.
  An empty list is not an error ("No bodies named.").
- Dialog: picking a body with an empty identity fills the identity with the body's name (editing logic).
- Changes no geometry. Place it right after the Import feature.

**What the backend does after an import update.** (1) GET the parts of the import studio; map each tracked name -> its new part id.
(2) `updateFeatures` on the Body identity feature: for each item set `body` to the new deterministic id, leave `identity` alone;
add items for new names, remove items for names removed from the Deform tag. Items must carry BOTH item parameters (correction 38;
copy the array-item pattern from `fsapi.py` / Extract variables). (3) Read the feature's status: a WARNING lists any name that did
not resolve. Between the import regenerating and step (2), qNamed references are transiently unresolved: that is expected.

**Known limits / unverified (nothing below has been run in Onshape).**
1. Whether `opNameEntity` on a body from `opImportForeign` survives an Import update and keeps downstream features, mates,
   part ids and released part numbers resolving: this is the experiment in the section above, still to do (needs a scratch document).
2. `qNamed(unknown name)`: assumed to resolve to nothing; the duplicate check wraps it in `try silent` because that is unverified.
3. `opNameEntity` is internal and undocumented; a std change could break it (Assign Identity depends on it too).
4. A re-pointed selection must be written by the backend after EVERY update; if it is missed, the body keeps its old (dead) selection
   and the item shows a warning.
5. Order-based or geometry-based matching (the STEP body order, boxes) was not built: the update changes geometry, and the order is
   untested. Revisit only if a REST re-point after each update proves too slow.