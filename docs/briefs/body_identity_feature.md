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
