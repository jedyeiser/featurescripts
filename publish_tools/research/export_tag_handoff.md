# Export_Tag: design handoff (2026-10-28)

From a design session in `C:\Users\jed.yeiser\Projects\eocproductdata` (the eocDataTools Onshape app).
Status: DISCUSSION / DESIGN. Nothing is built. Next step is to flesh out the FeatureScript with a
FeatureScript expert; the app side (eocDataTools consuming the data) comes AFTER the feature exists.
Everything here is the user's decision unless marked *(finding)* or *(open)*.

## Why

The app's current Export tab (`backend/eoc_data_tools/`, `EXPORT_SOURCES.md`, `services/export_rules.py`,
`services/dxf/`) keeps a per-document "package" in Django and re-finds items by Onshape id, part number,
then name. It cannot save face/edge picks (ids do not survive edits), cannot say which constituents of a
composite to draw, and has per-item options scattered across the tab. Goal: a flexible, RULES-BASED export
system where the Part Studio itself carries the spec. The feature is versioned with the document, survives
Copy Document and renames, and is visible to anyone in the studio.

Related prior art: native Onshape export rules (naming only, from metadata; no geometry rules). Greg Brown has
a "Publish" custom feature (no details found). No known prior art for an export-spec feature.

The old Export tab stays as-is for reference and fallback; a NEW export tab will consume Export_Tag data.
Eventually Export_Tag replaces the old method.

## Feature: Export_Tag (working name)

**Main argument:** exactly ONE body or ONE composite part (query Filter enforces it).
- A single body that is a constituent of a composite is exported AS ITSELF (the body), not as the composite.
- A second Export_Tag on the composite makes the composite its own export unit; the first body is then just
  another constituent of it. Each tag's attribute sits on its own main body, so there is no conflict.
- The app needs the parent/child record: the composite's tag lists its constituent part ids, and which
  constituents carry their own tag.

**Independent modes (each optional, any combination, all-off is valid):**
1. Create export composites. Composites are OPTIONAL; without them the attribute sits on the main body.
2. Build drawing tables (key points etc.). Must work with NO file-export information at all.
3. Carry export info (formats, names, folder, ...). A tag without this block is simply not a file source.

**Name of the main body:** captured via editing logic into a hidden string parameter, and used (optionally)
to name exports, composites, tables.

**Naming rules:** custom user overrides, composite naming, conventions. "Add released-version prefix" with
choices V1 / V001 / R0 / R1 etc. FS cannot know release state or revision, so the feature stores the RULE
(format) and the APP resolves the actual prefix at export time from revisions data.

**Export origin (new requirement).** Shifts the origin of exports (dxf, stl, igs, ...). Two ways:
- a mate connector (explicit OR implicit), or
- "Insert into quadrant" (enum 1st-4th) with x, y, z buffers, defaults 10 mm, 10 mm, 0 mm.
Solve for the MINIMUM transformation to that location and SAVE it in the attribute map. Optionally shift the
input bodies (including export composites, if any) to the location.

**Plan / Profile DXF sources (new requirement).**
- Plan = XY, Profile = XZ. User either selects multiple faces explicitly, or says "use all faces normal to
  the XY / XZ plane" (duplicates are possible). What is wanted is the projection (ray-casting-like) onto the
  plane. ARCS from sketches/analytic edges must stay arcs.
- Other user overrides: what to use as profile or plan, whether to export all faces normal to a direction.

**Drawings:** the tag holds INTENT only (has a drawing; export it with the export). FS cannot see drawings.
The app finds them (`resolvereferences`) and may automate drawing work later.

## Storage / contract

- Resolved spec is written as a schema-versioned ATTRIBUTE (`exportTag/1`) on the main body, plus the same map
  in the standard output slot (`embedStandardOutputs`, as `station_geometry.fs:216`). Pattern:
  `primitive/primitive_output.fs:516`, constants `primitive_types.fs:264-265`; read in FS with
  `qHasAttribute` + `getAttribute` (`primitive_table.fs:47-50`).
- The app reads it with ONE `evaluateFeatureScript` per studio (the DXF engine already pays one per studio),
  gated by a cheap `GET features` microversion check (also the cache key). The eval must be run per source
  (Release / Main / Version) because the tag can differ between them.
- Use plain numbers and strings only (Transform as a 3x3 + translation in mm, frames as origin/xAxis/zAxis
  in mm), so the REST typed-tree parses cleanly. Every section of the schema is OPTIONAL; the app ignores
  unknown keys and reads per schema version.
- Correction 64 trap: adding parameters to a published feature/table breaks placed instances. Keep the
  parameter list small and stable, never remove/retype; open-ended options in an array or one hidden JSON
  string; NEVER add parameters to tables (they read from the attribute). Decide the parameter set up front.
  Corrections 25 and 62 govern enum/length defaults.

## Name capture, the "Get name" button, stale name

- **Button:** `annotation{...} isButton(definition.getName);` (NOT a boolean flipped back; that is the old
  wrong idiom). Editing logic receives `clickedButton is string`; test only `clickedButton == "getName"`
  (correction 27, log:1386). Examples: `xSection/features/xSect.fs:186-196`, `driven_offset/unwrap.fs:2663-2668`,
  `case_pattern/case_pattern.fs:764-772`, `footprint/analyzeFootprint.fs:38-41`.
- **Read name in editing logic:** `getProperty(context, {"entity": q, "propertyType": PropertyType.NAME})`
  (`station_geometry.fs:247-264`, `export_primitive.fs:1289,1465-1480`). Check `isQueryEmpty` first, check
  `got is string`; a mate connector returns undefined (correction 44). For a composite call it on the
  composite body.
- **Write:** hidden string parameter, `UIHint.ALWAYS_HIDDEN` (`Move_Along_Edge.fs:165-166,257`).
- **FINDING (corrects an earlier assumption): the feature BODY cannot read the live name.** `getProperty`
  throws "Cannot get properties during feature regeneration" (correction 36, log:1550; critical rule 7). So a
  regen-time stale check is not possible. `getProperty` DOES work in the eval API.
- **Stale-name warning (user decision: warn in the feature, not in the app):**
  enum `StaleNameAction {IGNORE, WARN, ERROR}` (needs `SHOW_LABEL`, correction 43). Editing logic compares
  stored name with the current name and writes a hidden boolean `nameStale`; the body then does nothing,
  `reportFeatureWarning`, or `throw regenError`. LIMIT: this only refreshes when the dialog is edited, not on
  a rename elsewhere, so ERROR is only as fresh as the last edit. Prefer WARN (fs-style-rules: partly-met
  intent is a warning). *(open)* the app could compare for free in its existing eval; the user said not to
  build an app warning, so it is NOT planned.
- *(open, live test)* does `getProperty` work inside a `defineTable` function (std `holeTable.fs:123` calls it)?
  If yes a table could show a stale flag. Fallback idea: a mode "rename body to stored name" via `setProperty`
  (works in the body: `Move_Along_Edge.fs:412`).
- Naming rules should key on part ids, not display names.

## Export origin: technical notes

- Mate connector input: `"Filter": BodyType.MATE_CONNECTOR`, standalone (fs-style-rules); do NOT set
  `PREVENT_CREATING_NEW_MATE_CONNECTORS` so implicit ones are allowed. Resolve with
  `evaluateQuery(qBodyType(qOwnerBody(q), BodyType.MATE_CONNECTOR))` (correction 44, `station_geometry.fs:275`);
  frame via `evMateConnector` (`evaluate.fs:1106`; `deform.fs:2358`; std `extrudeCommon.fs:434`).
- Bounding box: `evBox3d(context, {"topology": q, "tight": true})` (`evaluate.fs:195`; world frame by default,
  optional `cSys`). Box the flattened members for composites:
  `qUnion([qBodyType(q,[SOLID,SHEET]), qFlattenedCompositeParts(q)])` (`station_geometry.fs:353`).
  Include export composites in the box.
- Proposed meaning of "minimal":
  - Quadrant mode = translation only, never rotation. 1st: box min x,y at +bufX,+bufY. 2nd: max x at -bufX,
    min y at +bufY. 3rd: max x,y at -bufX,-bufY. 4th: min x at +bufX, max y at -bufY. Z: min z at +zBuf.
  - Mate connector mode = `fromWorld(cs)` (`coordSystem.fs:151`); pure translation when axes already match
    world (`TOLERANCE.zeroAngle`). Suggested enum "Origin only / Origin + axes", default origin only.
  - Buffer defaults as length bounds in mm (correction 62, rule 25). *(open)* confirm the quadrant sign
    convention with the user.
- Store in the attribute: matrix, translation (mm), origin frame (origin, xAxis, zAxis in mm).
- Applying the shift: IN PLACE `opTransform` (`Move_Along_Edge.fs:395`, `unwrap.fs:1560`) moves the body and
  everything downstream, but OPEN composites cannot be moved (`station_geometry.fs:50-52`) and members of a
  composite must not be moved alone; a CLOSED composite can move. COPY via `opPattern` with a one-element
  transform list (`Move_Along_Edge.fs:388`, `station_geometry.fs:362`) copies attributes (`attributes.fs:38-41`),
  giving two tagged bodies; clear the original's attribute with `setAttribute({"attribute": undefined})`.
  Recommendation: COPY. *(open)* patterning composites.
- Corrections that apply: 19, 37 (composite members), 44, 55 (`startTracking` before splits), 52 (producers
  publish untracked), 36.

## Plan / Profile projection: technical notes

- Normal test (as the current DXF engine, `backend/eoc_data_tools/services/dxf/fetch.py:145`): `evPlane(...).normal`,
  `norm(cross(n, axis)) <= sin(0.5 deg)`, accepts both signs. Curved faces: `evFaceTangentPlane(f, vector(0.5,0.5))`
  at the centre, threshold |n.axis| > 0.02. `qParallelPlanes(q, normal, true)` is a cheap pre-filter (planar only).
- Do NOT use `opCreateOutline` for arcs: it refits silhouettes to splines (correction 57, log:1915; the app
  once got 1334 LINEs from it; and it fails on sheets).
- Recommended: the feature PUBLISHES RESOLVED FACE IDS (`transientQueriesToStrings`, per `fetch.py:68,103`) in the
  attribute map; the backend does the projection in Python where analytic circles are already kept as arcs
  (`fetch.py:183-199` `evCurveDefinition`; `model.py:461-494`; coincident handling `coincide.py:split_coincident`).
  Transient ids are valid only for that microversion.
- Dedupe in FS only exact duplicates (key: owner body, plane distance, `evArea`, `evApproximateCentroid`, 1e-6 m);
  leave projection-coincident stacks to the backend.
- FS-side projection alternatives: `opExtractWires` keeps analytic types (`footprint/integrateFootprint.fs:561`);
  `opDropCurve` NORMAL_TO_TARGET (`station_geometry.fs:405`) has unknown circle behaviour (correction 77 shows
  line refits). Sketch arcs via `skArc` are the only guaranteed arcs (correction 39).
- Profile (XZ) on a curved top: `opPlane` + `opIntersectFaces` (`fetch.py:134-167`).

## Split of work

FeatureScript (Export_Tag): resolved profile/plan face sets and overrides, naming stem and composite names,
key-point list, drawing-intent flags, version-prefix RULE, `exportTag/1` attribute + output-slot map, key-point
fstable for drawings (reuse `station_geometry.fs` / `station_table.fs`), export origin transform.

App (eocDataTools, LATER): crawl studios (features-GET gate, then one eval per studio, cached by microversion),
resolve the release/revision prefix, drawing discovery/staleness/export, DXF generation from the tag's face ids,
zip layout, manifest. Cross-studio structure (folders, layout sheets), assemblies and drawings stay app-side.

Known fallback: if a studio has no tag or the feature failed, the app shows "no tag" and does not guess; the old
Export tab remains.

## Risks

- A suppressed/failed feature leaves a stale or missing tag.
- An attribute on a body inside an open composite may be lost if the composite is recreated: test a rename cycle.
- Two Export_Tag on one body: precedence rule (last wins) + warning.
- Cold regen on heavy studios (60 s+): keep the eval thin.
- Per-source evals (Release / Main / Version) can disagree.

## Live tests to run (Playwright, scratch studio only; read-only on live docs)

1. `getProperty` inside a `defineTable`.
2. `opTransform` on a body that belongs to an open composite.
3. `evBox3d` on a composite (and on wire/sheet members).
4. Implicit mate connector in this feature's own dialog.
5. `opDropCurve`/projection of a circle: expect circle when parallel, spline (ellipse) when tilted.
6. Attribute size limit (try ~100 KB).
7. Does a pattern copy carry the name and the attribute?
8. Does an attribute on a body survive recreation/rename of its composite?

## Playwright note

Playwright is configured for the Onshape login in `featurescripts/.mcp.json` (isolated, storage state
`~/.fs-sync/onshape-state.json`). The same file was copied to the eocproductdata root. Obey
`memory/playwright-and-live-safety.md`.

## Decisions log (user)

- Export_Tag works without composites if the user chooses; main argument = one body or one composite.
- Works without any file-export info (composites + drawing tables only).
- Body name retrieved via editing logic (a "Get name" button), optionally used for naming; stale-name warning in
  the feature, not in the app.
- Body-within-composite rule above (body exported as itself; composite gets its own tag).
- Export origin (mate connector or quadrant + buffers 10/10/0 mm), minimum transform saved in the attribute,
  optional shift of input bodies and export composites.
- Plan/Profile: multi-face selection or "all faces normal to XY / XZ"; projections; arcs preserved.
- The existing Export tab is kept as reference and fallback; the new export system replaces it eventually.
- Order of work: FeatureScript first, then the app consumes it.
