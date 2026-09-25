# Track report: variable_tools policy, icons, whole-repo triage

## 1. Bottom line
- **None of the 14 KEY feature files uses variable_tools, and 10 of them have no icon** (all checked by grep). The 4 KEY files that do have icons are Analyze, Integrate and Scale footprint plus Modify curve end, and all four icons break house style. Composite intersection does not exist anywhere in the repo, sync state, settings or memory. We need its Onshape URL.
- **variable_tools should be adopted sparingly.** If a feature creates new bodies and publishes only the standard keys, the consumer gets almost nothing: it already falls back to `qCreatedBy` (`extract_variables_utils.fs:224-231`). The real cost of publishing is that every published query key becomes a query variable that cannot be deleted once the user presses "Add keys".
  - Real adoption targets: **Analyze footprint** (highest value), **Generate baseline**, **Scale footprint**, and **Modify curve end** as part of its rewrite.
  - Do not embed: Scaled curve, Join wires, Extrude edge, EI & Cross Section, Solve GJ.
- **Several current producers publish too many keys.** Driven edge offset publishes 11 diagnostic scalars. Clean wire (`run1..N`), Evaluate offset and Create offset profile (`piece1..N`) publish numbered keys whose count depends on geometry, which breaks the library's own "keys always present" rule.
- **Do not run `icons/install_icons.py` on the KEY files as it stands.** I confirmed at lines 44-57 and 61 that it:
  - can leave a file empty when the file contains non-ASCII characters;
  - crashes on `gordonSurface/` because that folder has no `.document.json`;
  - converts CRLF line endings to LF across the whole file.

  None of this needs an FS version bump. Icons already work at FS 2892.
- **Most KEY code dates from March to July and runs FS 2892**, with brace-less statements, non-ASCII characters, missing SHOW_LABEL, and a swallowed try/catch in `xSect.fs`. The FS bump should be done per document, as its own step, bundled with the variable_tools adoption.

## 2. Per-feature verdict (KEY features)

| Feature | File (FS, last change) | State | Biggest problems | Effort |
|---|---|---|---|---|
| Analyze footprint | footprint/analyzeFootprint.fs (2892, 03-05) | Works; off-style icon | Results exist only as READ_ONLY dialog fields filled by editing logic (l.76-84), so they go stale and cannot be used downstream; radii are stored as strings; `"Decription"` typo (l.110) | M |
| Arc fit | footprint/arcFit.fs (2892, 07-03) | Newest footprint feature; no icon | 2042 lines; 15 prints; `qCreatedBy` mixes the wire with the sketch | M |
| Generate footprint points | footprint/getFootprintPoints.fs (2892, 05-22) | No icon | Its attribute contract must stay stable; 12 non-ASCII bytes | S |
| Integrate footprint | footprint/integrateFootprint.fs (2892, 07-03) | Cleanest of the suite; off-style icon | The solved complement (waist location or taper angle) is not returned | S |
| Scale footprint | footprint/scaleFootprint.fs (2892, 07-03) | Off-style icon | 2817 lines; 29 brace-less statements; 21 non-ASCII lines; no SHOW_LABEL on 8 enums | L |
| Scaled curve | gordonSurface/scaledCurve.fs (2892, 03-02) | No icon | `createCurve` defaults to false (l.41, confirmed), so a new instance builds nothing; 3 brace-less `continue`s; 20 prints; typos "Defualt"/"Desription"; imported by gordonSurface.fs | M |
| Modify curve end | gordonSurface/modifyCurveEnd.fs (2892, 03-02) | Icon off-style and probably clipped; feature request pending | Edge-only filter (l.43, confirmed); no hold point; `flipREf` typo; the continuity reference already exists (l.79-90, 158-161) | L (rewrite) |
| Pull surface | gordonSurface/pullSurface.fs (2892, 03-02) | No icon | 32 non-ASCII lines (would blank the file under install_icons); 8 brace-less statements; no SHOW_LABEL | M |
| Join wires | example_1/joinWires.fs (2892, 03-13) | 49 lines; no icon | Labelled "Edges and wires" but the filter is `EntityType.BODY && BodyType.WIRE` (l.10, confirmed); overlaps with Merge curve / Clean wire | S (or retire) |
| Extrude edge | example_1/extrudeEdge.fs (2892, 03-13) | No icon (the brief was wrong; confirmed at l.34) | 1 SHOW_LABEL for 5 enums; "exrude" typo in the description | S |
| Offset edges | example_1/refSurfCreation/offsetEdges.fs (2909, 09-23) | Recently ported to the curveMapping core, waiting for the user to version it; no icon | 2514 lines; 9 warnings; 2 try blocks; 17 prints; may be superseded by Driven edge offset / Offset+ | M |
| Simple body rename | bodyRename/Simple_Rename.fs (3008, 07-09) | Clean, 52 lines; no icon | Icon only | S |
| EI and Cross Section | xSection/features/xSect.fs (2892, 09-23) | No icon (an unused EI_Icon.svg tab sits in the doc) | 6 try blocks, including an empty `catch` around `storeAnalysisData` (l.457-467), which silently starves Solve GJ, Estimate Deflection and Update profile | M |
| Solve GJ | xSection/features/GJ_Feature.fs (2892, 04-28) | 72-line wrapper; no icon | Icon only | S |
| Composite intersection | not found | - | Location unknown | ? |

## 3. Prioritized recommendations

**P0: fix or guard before doing anything else**
1. **Make `install_icons.py` safe.**
   - Why: line 56 does `open(path,"w",encoding="ascii").write(src)`. The open truncates the file, then the ASCII encode raises, so the file is left empty. The SVG upload (l.63) has already run by then, so an unused tab is also left behind.
   - Fix: encode before opening, preserve the file's line endings, and support a project that has no `.document.json` (or wire gordonSurface by hand).
   - Alternative: first ASCII-clean `xSect.fs`, `pullSurface.fs` and `getFootprintPoints.fs`.
   - Effort: S.
2. **Remove the empty catch in `xSect.fs:457-467`.** Raise a warning when the storage write fails, because requested output is then missing. Effort: S.
3. **Check the consumer's "Name contains" filter** (`extract_variables_utils.fs:511`). It calls `getProperty(NAME)` in the feature body, which Correction 36 says throws. This is likely broken but not tested live. Fix: resolve names in `extractVariablesEditLogic`, or drop the option. Effort: S-M.

**P1**
4. **Icons for the KEY features, batched one document at a time** so each document needs one push and one version:
   - footprint: Arc fit and Footprint points, then restyle Analyze, Integrate and Scale;
   - example_1: Join wires, Extrude edge, Offset edges;
   - xSection: EI, GJ;
   - bodyRename: Simple rename;
   - gordonSurface: Scaled curve, Pull surface, Modify curve end, after the `.document.json` question is resolved.

   Effort: about 1 hour per icon plus one review pass per document.
5. **Modify curve end rewrite.**
   - Widen the filter to `EntityType.EDGE || BodyType.WIRE`.
   - Add a hold point.
   - Build the requested "match tangency/curvature of a reference" option on the existing `modContinuityRef`.
   - Line 161 passes `definition.modEndContinuity` where the computed `useContinuity` (l.159) should go, so `useContinuity` is dead code. Check this is intended.
   - Keep the exported `modifyCurveEnd(...)` signature, because `gordonSurface.fs:13` imports it.
   - Bundle the FS bump, icon redraw and embed into this work.
   - Effort: L.
6. **Analyze footprint embed.** Publish the design dimensions as ValueWithUnits variables computed in the body. They already exist in the body's `footprintData` (l.126). Requires the footprint doc to move to FS ≥3070. Effort: M.
7. **Trim the producers that over-publish.** Do this only after a read-only survey of live Extract variables entries that reference the keys.
   - Driven edge offset: remove 10 scalars and add start/end vertices.
   - Unwrap: remove 6 QC keys.
   - Evaluate offset: remove 3 keys.
   - Offset+: remove 3 count keys.

   Effort: M.

**P2**
8. Generate baseline and Scale footprint embeds; Integrate footprint should also return its solved values. Effort: M.
9. Settle Join wires: fix its filter, or retire it in favor of Merge curve / Clean wire. Also decide whether Offset edges or Driven edge offset is canonical. Effort: S.
10. Update `extract_variables_schema.md`: it still names design_map.fs and lists 7 producers; the real number is 17. Also correct CLAUDE.md, which calls footprint/ and gordonSurface/ "archived". Effort: S.
11. Style passes on the KEY files: braces, ASCII, SHOW_LABEL, prints, notice levels. Run `fscheck.py` first. Effort: M per document.

## 4. Designs

### variable_tools policy (for the schema doc)
1. **Embed only if at least one of these holds:**
   - (i) the feature modifies or merges bodies in place, so `qCreatedBy` misses the result;
   - (ii) `qCreatedBy(id)` includes helper bodies that must be excluded from `output`;
   - (iii) it computes a named scalar, or a meaningful sub-entity, that a later feature needs.

   Otherwise do not embed; the consumer's fallback is free.
2. **Every custom key must name its downstream use** (a feature, a parameter, or a #expression). Aim for about 0-6 keys, preferring queries over counts.
3. **Never publish** diagnostic counts, booleans, QC ratios, solver internals, or strings with units baked in. Those belong in `reportFeatureInfo`, the debug output or test checkers. Do not call `evaluateQuery` just to publish a count.
4. **Keys are always present** (`qNothing()`, 0 with units, `"none"`). The set of keys may vary with user parameters (array rows, tool picks) but never with geometry: no `run1..N`. Publish one query and let Extract variables pick the entity.
5. **Naming:**
   - camelCase; entity suffix on queries (`…Vertex`, `…Edges`, `…Faces`); bare numbers for numbered keys (`piece1`); no units in names.
   - No keyword, type or std-function names (`length`, `line`, `plane`, …), because with an empty prefix the key becomes the `#name`.
   - Standard vocabulary: `startVertex`, `endVertex`, `startEdge`, `endEdge`, `breakVertices`, `cornerArcs`, `boundaryEdges`.
6. **Call it once, last in the body, unconditionally**: no "publish" toggle (Correction 25). Pass `output` explicitly when the feature modifies or merges, or when helper bodies must be excluded. Pass `inputs` only if the inputs survive.
7. **Versions:**
   - The producer must be on FS ≥3070. Bump each document as a separate, fingerprinted step.
   - Pin `extract_outputs` microversion `b8c80ac05dcfd9f3cc172ffc` (V1 and V2 of the doc resolve to it) and never re-pin just for consistency.
   - `extract_outputs.fs` is frozen. Consumers validate by exact field set (`isIn` at l.73-76, `size(value)==2` at l.126), so any new descriptor field makes older consumers silently drop the whole map.
8. **Reserved slot:** the `toString(id)` slot belongs to `embedVariableMap`. Case template already uses it, so a Case template cannot also be a producer.
9. **Do not embed:**
   - analysis, table and attribute features (xSect, GJ, QC tables, Export);
   - tests and harnesses, examples and experiments;
   - helper modules called as functions;
   - features used inside Case pattern templates;
   - features that already write #variables directly (Variable +, Iterative solve, Station definition, Case template).

### Modify curve end (rewrite outline)
- **Input:** `EDGE || BodyType.WIRE`. Chain the input with `constructPath` or the existing unify step.
- **New "Hold point" vertex** (optional, 1 pick): the deformation applies only between the hold parameter and the moved end. The segment from the hold point to the fixed end is copied unchanged, with G1/G2 blending at the hold point (reuse `fixedEndContinuity` there).
- **"Match reference"**: expose the existing `modContinuityRef` + `modEndContinuity` at top level, with SHOW_LABEL.

### Icon pipeline recipe
1. Add a draft entry in `make_drafts.py`.
2. Preview with `make_preview.py` and let the user pick.
3. Copy the pick to `icons/final/<name>_icon.svg`.
4. Add a row to TARGETS in `icon_targets.py`.
5. Run the fixed `install_icons.py`. It uploads the SVG tab and inserts an `IconNamespace::import` line plus `"Icon" : IconNamespace::BLOB_DATA`.
6. Run `fscheck`.
7. Push, with the user's approval.
8. The user creates a new version, and other documents update their references.

Notes:
- No FS bump is needed.
- Sync ignores SVG tabs (`operations.py:296`).
- Replacing an icon needs a new tab name (the script matches tabs by name).
- Files with several features (Background.fs, betterMeasure.fs, case_pattern.fs) must be wired by hand, one namespace per feature.

## 5. variable_tools keys per feature

| Feature | Keys |
|---|---|
| Analyze footprint | Variables: `tipWidth`, `waistWidth`, `tailWidth`, `waistX`, `tipLength`, `tailLength` (length; 0 when not found), `taperAngle` (angle), `sidecutRadius` (length). No custom queries. |
| Generate baseline | `baseline`, `weightedBaseline` (empty unless created), `frcpVertex`, `arcpVertex`. The contact vertices at the region joins are inferred; verify them. |
| Scale footprint | Queries `positiveSide`, `negativeSide`; variable `lengthScale`. Leave `inputs` empty when the reference is deleted. |
| Modify curve end | `movedVertex`, `holdVertex` (`qNothing()` when there is no hold point). Adopt with the rewrite. |
| Integrate footprint | `waistX`, `taperAngle`, once the solver returns them. |
| Generate footprint points | `tipCurve`, `rslCurve`, `tailCurve` (empty unless Retain curves). The point arrays stay in the attributes. |
| Offset edges | `startVertex`, `endVertex`, only if it stays canonical. |
| Arc fit | Standard keys only, with `output = qCreatedBy(id + "compositeWire")` to exclude the sketch. |
| Pull surface | Standard keys only, with `output` = the surface body, to exclude the kept curves and points. |
| Simple body rename | Optional standard keys only, with `output` = the renamed bodies, since it modifies in place. |
| Scaled curve | None. It creates a single wire, so the fallback covers it (and nothing is created by default). |
| Join wires | None. The fallback plus Chain end entries cover it. |
| Extrude edge | None. The fallback covers it. |
| EI & Cross Section, Solve GJ | None. They use the attribute/table channel. |
| Composite intersection | Unknown until located. |

## 6. Icons: status and glyph concepts
House style: 20x20 viewBox; #333 outline; #999 secondary; #FFF fill; one #1651B0 accent. Shared motifs: a ski planform (footprint suite), a trapezoid cross-section slice (xSection), and a curve with end dots.

| Feature | Status | Priority | Glyph |
|---|---|---|---|
| Arc fit | None | P1 | Grey dashed sidecut with 3 tangent #333 arcs and white junction dots; blue center point plus radius line |
| Footprint points | None | P1 | Grey ski outline with #333 dots along one edge and a small table card; blue dots |
| Analyze / Integrate / Scale footprint | Off-style black 1200-viewBox SVGs (shoe sole, integral sign, arrows) | P2 restyle | Ski planform with a blue waist dimension arrow / half-ski plus a blue integral sign / nested grey and #333 skis with a blue length arrow |
| Scaled curve | None | P1 | Two grey rails with a #333 curve between them; blue double tick showing the scale position |
| Modify curve end | Off-style; arrows likely clipped (transform maths only, not rendered) | P1 with the rewrite | Grey dashed original curve; #333 modified curve; hollow hold-point dot; blue moved end plus arrow; grey reference stub |
| Pull surface | None | P1 | Grey patch with a 3x3 dot grid; blue center dot plus up arrow, with the patch bulged |
| Join wires | None | P1 | Three #333 segments; one joint drawn as a blue dot with a link bar |
| Extrude edge | None (brief was wrong) | P1 | #333 open curve with a thin grey sheet rising from it and a blue up arrow. Needs Onshape's extrude glyph exported, which is a browser step needing user OK. |
| Offset edges | None | P1 | #333 curve plus a grey offset copy; blue moving frame with 2 arrows |
| EI & Cross Section | None (unused EI_Icon.svg tab) | P1 | Thin #333 ski side profile with 3 grey slices; one slice blue |
| Solve GJ | None | P2 (same push as EI) | Large slice on a grey axis; blue twist arrow |
| Simple body rename | None | P1 | #333 cube with a white tag; blue I-beam cursor |
| Composite intersection | Not found | Blocked | If confirmed: Onshape intersection-curve glyph with a blue 2-segment composite result |

Secondary backlog: Evaluate offset, Unwrap, Station definition/geometry, the xSection siblings, and the private curveMapping copies (reuse the public SVGs).

## 7. Conflicts, spot-checks, open questions

**Conflicts between experts and how I resolved them**
- **Which KEY features should embed.** The triage analyst proposed keys for Scaled curve, Join wires, Arc fit and Offset edges, and said the Analyze features need none. The policy analyst proposed the reverse. I went with the policy analyst: the consumer's `qCreatedBy` fallback (confirmed in utils l.224-231) makes a single-wire embed worthless. Analyze footprint's values are otherwise unreachable, because the READ_ONLY fields are written only through editing logic (l.76-84).
- **Icon counts.** 30 of 72 files have an icon (icon analyst); 10 of 14 KEY files have none (triage). These are consistent; the icon analyst's "5 KEY with icons" counts Gordon Surface, which is not KEY.

**High findings I spot-checked**
- **Confirmed:**
  - `install_icons.py` can empty a file and uploads before wiring (l.56, 63-64);
  - `gordonSurface/.document.json` is absent;
  - Extrude edge has no icon;
  - Modify curve end filter is edge-only and the continuity reference exists;
  - Join wires filter mismatch;
  - Scaled curve `createCurve` defaults to false;
  - Analyze footprint recomputes `footprintData` in the body (l.126) and uses it only for the sketch;
  - Composite intersection: no match.
- **Upgraded:** the consumer "Name contains" `getProperty` issue, from unverified to likely. Correction 36 documents exactly this throw. It is still not tested live.
- **New finding (mine):** `modifyCurveEnd.fs:161` ignores `useContinuity`.
- **Still unverified:**
  - whether a 2892 module can import the 3070 library (assume a bump is needed);
  - that the Modify curve end icon is clipped;
  - Generate baseline contact vertices;
  - dark-mode rendering of #333.

**Open questions for the user**
1. Where does Composite intersection live? Please send the Onshape URL.
2. May footprint, gordonSurface, xSection and example_1 move to FS 3070 or 3083, each as its own fingerprinted step?
3. Analyze footprint: which radius is the sidecut radius (average, widest-natural or inflection-natural), and should widths be full or half?
4. Keep or replace the geometry-numbered keys (Clean wire `run1..N` was your 09-24 request, plus `piece1..N`)? May the diagnostic keys be cut after a survey of the live entries that use them?
5. Naming: camelCase everywhere, or keep snake_case in Move_Along_Edge and Trim curve+ (renaming breaks existing entries)?
6. gordonSurface: add a `.document.json`, or wire its icons by hand? `sync/config.yaml:36` says it is local only.
7. Are Join wires and Offset edges still canonical, or superseded by Merge curve / Clean wire and Driven edge offset? Which SW rout surface is canonical?
8. May we replace the existing footprint and Modify curve end icons? Should the unused EI_Icon.svg and length.svg tabs be deleted?
9. May we do a one-time CRLF-to-LF normalisation on the affected files?

### Critical Files for Implementation
- C:/Users/jed.yeiser/documents/featurescripts/icons/install_icons.py
- C:/Users/jed.yeiser/documents/featurescripts/gordonSurface/modifyCurveEnd.fs
- C:/Users/jed.yeiser/documents/featurescripts/footprint/analyzeFootprint.fs
- C:/Users/jed.yeiser/documents/featurescripts/variable_tools/extract_outputs.fs
- C:/Users/jed.yeiser/documents/featurescripts/variable_tools/extract_variables_utils.fs
- C:/Users/jed.yeiser/documents/featurescripts/xSection/features/xSect.fs