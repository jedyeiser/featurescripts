## Trimming the producers that publish too many keys: plan (read-only)

**Headline:** Across the documents I surveyed, only **one** Extract variables instance uses any key flagged for trimming. It is the example block in **curve_tools / Test_1** ("Extract variables: shelf wire + RSL profile keys"), which uses `curveCount`, `maxDeviation` and `run1` from Clean wire. That block was built as a demo on 2026-09-24; it is not user design work. None of the RD 20FOU 28 Extract variables instances, and nothing in Design_Master, references a flagged key. Two tests also read flagged keys and must be updated in the same push as the trim: `curve_tools_tests.fs:137-158` (`curveCount`, `run k`) and `check_trim_curve_plus_tests.py` (`cut_k`, `piece_k`, which is out of scope).

---

### 1. Inventory

Verified by reading the code. The standard keys (`output`, `outputFaces`, `outputEdges`, `outputVertices`, `inputs`, added by `extract_outputs.fs:245-280`) are KEEP everywhere and are not repeated below.

**In scope (flagged in round 1)**

| Producer | Embed call | Key | Proposal | Reason / downstream use |
|---|---|---|---|---|
| Clean wire | curve_tools/clean_wire.fs:499 (keys 488-507) | `startVertex`, `endVertex`, `startEdge`, `endEdge` (q) | KEEP | Bridging-curve / Chain end inputs; checked by curve_tools_tests:110-116 |
| | | `breakVertices` (q) | KEEP | Used live by Test_1; split points for later features |
| | | `run1..N` (q, l.495-497) | DROP, replace with `outputEdges` + a consumer entry (see section 2) | Count depends on geometry. Used by Test_1 (`run1`) and curve_tools_tests:144 |
| | | `curveCount` (v) | DROP | Count. Used by Test_1 and by the test at l.137 only. Rewrite the test as size(outputEdges) |
| | | `inputCount` (v) | DROP | Count, no consumer |
| | | `maxDeviation` (v) | DROP | QC value. Already a computed parameter (l.440). Used only by Test_1 |
| Create offset profile | driven_offset/create_offset_profile.fs:1071 | `startVertex`, `endVertex`, `breakVertices` (q) | KEEP | Profile ends and breaks, used to join the profile downstream |
| | | `startStation`, `endStation`, `breakStations` (v) | KEEP (the user must name a use) | Design stations. check_offset_profile.py:67,120-132 reads them. No live Extract variables use |
| | | `pieceCount`, `breakCount` (v) | DROP | Derived: size of the output, and size minus 1 |
| | | `piece1..N` (q, l.1066-1069) | REPLACE, optional | The count follows table rows and **values** (equal values join pieces), so it is borderline under the policy. A change to a #variable renumbers the pieces silently. Replace the same way as Evaluate offset if you want consistency |
| Driven edge offset | driven_offset/driven_edge_offset.fs:1387 (publishOutputs 1350; withTerminalVariables 1399) | `curveCount`, `start`/`end` × `Action`, `Distance`, `Squareness`, `Kink` (8 keys), `cornerArcCount`, `cornerArcRadii` (11 v) | DROP all 11 | Diagnostics: squareness and kink are solver QC, and `Action` is a string. They belong in offset_debug or reportFeatureInfo. No checker reads them (grep of devtools, verified). There are 13 live instances (2 in Design_Master, 11 in Evaluate offset tests) and no Extract variables reference them |
| | | *(new)* `startVertex`, `endVertex`, `startEdge`, `endEdge`, `cornerArcs` (q) | ADD | The crosscut recommendation. Use the standard vocabulary, as Offset+ does at l.314-330 |
| Evaluate offset | driven_offset/evaluate_offset.fs:950 | `startVertex`, `endVertex`, `breakVertices` (q) | KEEP | Profile ends and breaks |
| | | `startStation`, `endStation`, `breakStations` (v) | KEEP (the user must name a use) | They match Create offset profile, so a measured profile can feed a new one |
| | | `measuredStations`, `stationCount` (v) | DROP | QC |
| | | `pieceCount`, `breakCount` (v) | DROP | Derived. Crosscut said "3". I count 4 to drop if `pieceN` also goes |
| | | `piece1..N` (q, l.945-948) | REPLACE | The count comes from measurement, so it depends on geometry |
| Unwrap | driven_offset/unwrap.fs:439 | `lineCount`, `arcCount`, `splineCount`, `lengthWrapped`, `lengthFlat`, `volumeRatio` (v) | DROP all 6 | QC. reportSummary already reports them (l.435). 19 live instances in the Unwrap_Testing copies; no Extract variables reference them |
| | | `edgesOnPlane` (q) | KEEP | The sides laid on the plane; a sketch or drawing reference |
| Offset+ | reference_side/offset_plus.fs:228 (surface), :296 (curve) | `roundedCorners`, `trimmedCorners`, `openCorners` (v) | DROP | Counts. Already reported by the info/warning at l.283-290. In the surface branch they are constants 0. There are 2 live instances in RD Parts; no Extract variables reference them |
| | | `startVertex`, `endVertex`, `startEdge`, `endEdge`, `cornerArcs`, `boundaryEdges` (q) | KEEP | Read by check_reference_side_tests:171, 216-219 |

**Out of scope, noted for later** (none of these is referenced by a live Extract variables instance):

- Fillet wire (fillet_wire.fs:187): `cornerCount`, `filletCount` and `skippedCount` are counts. check_fillet_wire.py reads the last two.
- Map curve (:270): `stationCount` should be dropped. `spanStart`, `spanEnd`, `toStart`, `toEnd` should be kept.
- Merge curve (:357): `degree` and `controlPointCount` are diagnostics.
- Evaluate profiles (:295): the key name `length` breaks naming rule 5, and `trimmedStart`/`trimmedEnd` are booleans. RD uses only `output`.
- Driven offset surface (:2679): `faceCount` calls evaluateQuery just to count (rule 3), and `offsetCount` should go too. `offsetWire1..N` is keyed by rows of the Offsets array, so it complies.
- Mutual trim+ (:140): `trimEdgeCount` calls evaluateQuery just to count.
- Split+ (:255): `pieceCount` and `regionCount`. The checker reads both.
- Station geometry (:143): `stationCount`.
- Trim curve+ (betterCurveTrim.fs:662): `cut_k` and `piece_k` depend on geometry whenever the cuts are not picked points. They are the same class of problem as `run1..N`, and the checker uses them.
- Move along edge (:233): `copy_j` is keyed by a user count, so it complies.

---

### 2. Replacing the geometry-numbered keys

**What Extract variables can do today** (verified in `extract_variables_utils.fs:351-468`):

- **Individual run of Clean wire:** a Closest to point entry (CLOSEST) on `outputEdges`, entity type Edges, plus a point pick, gives `qClosestTo(edges, point)` (l.383-391). This works.
- **A span of runs:** an Edges between points entry (EDGES_BETWEEN, l.451-464) with two picks. This works.
- **First or last run:** use the existing `startEdge`/`endEdge` keys, or a Chain end entry (CHAIN_END) with End output set to Edge. This works.
- **Individual piece:** CLOSEST on `output` with entity type Bodies. This works, because each piece is its own wire body.
- **Missing:** there is **no ordinal selection** ("the Nth edge/body counted from the chain start") that needs no point pick. The Filtered entry's "Keep only the largest N" (l.494-528) ranks by size, not by order. There is also no key@n-style index into a single query: `@n` selects the *source feature* (l.212), not an entity.

**Proposed replacement, which meets the policy:**

- Drop `runN` and `pieceN`. The standard `outputEdges` already holds every run, because Clean wire's `output` is the cleaned wire only (l.500). `output` already holds every piece.
- Optional, only if you want addressing without a pick: add an entry type **"Nth along chain"** to `extract_variables_utils`:
  - an index field `x_index`, plus the existing Point, used to choose the end the count starts from;
  - edges are ordered with the chain walk that `edgesBetween` already uses;
  - bodies are ordered by the distance of their nearest point along the chain.
- This is a change on the consumer side only. `extract_outputs.fs` stays frozen. Appending an enum value is backward compatible. It needs a new Variable_tools version, and each document's Extract variables import has to be bumped to use it.

**What you lose by dropping run1..N** (you asked for these keys on 2026-09-24; see memory reference-side-and-variable-tools.md:65,123):

1. **Addressing without a pick.** `run5` needs no point. CLOSEST needs a stable upstream vertex or mate connector. Picking a vertex of the cleaned wire itself works, but it is only as robust as a viewport pick.
2. **Discovery.** "Add keys from sources" no longer lists each run as a key with its own preview colour.
3. **Order from the source start.** The run index carries it. With CLOSEST it is implicit in which point you pick. The "Nth along chain" entry would restore it.
4. **Counter-point:** `runK` is not stable either. In Auto mode the run count follows geometry (the SW_SHELF example has 13 runs), so `run5` can quietly become a different edge, or vanish and trigger "no source offers 'run13'". CLOSEST stays tied to a location.

A middle option that also complies: in Manual mode, publish one key per **Groups row** (`group1..N`, following the array rows). User parameters then set the key set, not geometry. This covers the case where you care about particular fitted spans.

---

### 3. Live usage survey

**Method:** read-only GETs through `OnshapeClient.get`, 84 calls in total (70 in the first pass, 14 targeted).

- **Covered:** 20 documents, every Part Studio in each, from `featurescriptSettings.json` projects, the 15 `.document.json` files, and RD 20FOU 28 (did 9732a1a9…, ws 4b70b4378f844dff881b5282). Design_Master is `driven_offset` e/643b702d…
- **Not covered:** `case_pattern` (skipped when I hit the call cap), any user documents not in these lists, and any workspaces or versions other than the ones listed.
- The featureType `extractVariables` was confirmed from `extract_variables.fs:39` and from the API.

| Document / Part Studio | Producer instances | Extract variables: sources, then keys used |
|---|---|---|
| curve_tools / Test_1 | cleanWire 1, mapCurve 1, mergeCurve 1 | "shelf wire + RSL profile keys" (VT ns vf4f872fe20d1): sources Clean wire, Map curve. Keys: **`curveCount`, `maxDeviation`, `run1`**, `output@1`, `breakVertices`, `startVertex@1`, `endVertex@1`, `output@2`, `startVertex@2`, `endVertex@2` |
| RD 20FOU 28 / Parts (306 features) | splitPlus 1, offsetPlus 2, evaluateProfiles 1 | 4 instances (ns v15f6dcf73e30): `insideEdges` (Split+), `output` (Evaluate profiles SW_Profile), `output` (thicken), `output` (mirror). **None flagged** |
| RD 20FOU 28 / Design Master | betterCurveTrim 4 | none |
| driven_offset / Design_Master (171) | drivenEdgeOffset 2, drivenOffsetSurface 3, mutualTrimPlus 7, cleanWire 1 | none |
| driven_offset / test studios | unwrap 19, createOffsetProfile 9, drivenEdgeOffset 11, evaluateOffset 6 | none |
| reference_side / topsheet_surf | offsetPlus 4, splitPlus 3, mutualTrimPlus 1 | 1 instance: Split+ region keys only (`inside`, `insideEdges`, `middle`, `middleEdges`, `outsideEdges`, `splitEdges`, `splitFaces`) |
| variable_tools / Examples, Tracking tests, Part Studio 1 | none | standard keys and `modifiedEdges` only |
| smallTools, publish_tools, curve_tools test studios, the other 11 documents | a few test instances | none |

---

### 4. Safe trim order and what it means for versions

**Mechanics (verified):**
- Removing a key only changes the producer's tab. Consumers validate the map's field *shape*, not the key set, so the extract_outputs pin (b8c80ac0…) stays as it is.
- Documents that use a producer see the change only after they bump that producer document's import version. Every trim is therefore opt-in per consuming document.
- **Pitfall:** addresses depend on how many sources in the same Extract variables instance offer a key (l.207-213). Adding keys can turn a bare address ambiguous: if DEO gains `startVertex`, an instance that has DEO plus another end-publishing source must switch to `startVertex@n`. Removing a key can rename another source's `key@n` back to the bare `key`. No live instance has DEO or Offset+ as a source, so today this is only a risk to design around.

**Order:**
1. **Now, zero consumers (verified):**
   - Driven edge offset: drop the 11 scalars and add the end keys.
   - Unwrap: drop the 6 keys.
   - Evaluate offset: drop `measuredStations`, `stationCount`, `pieceCount`, `breakCount`.
   - All three are in driven_offset, so this is one push and one version.
   - Check first: `check_evaluate_offset.py` reads geometry, not keys (verified).
2. **Now, zero consumers:** Offset+ drops its 3 counts (reference_side, one push). The RD Offset+ instances are unaffected until RD bumps the reference_side import.
3. **After migration:**
   - Clean wire: drop `run1..N`, `curveCount`, `inputCount`, `maxDeviation`.
   - Before that: rewrite curve_tools_tests:137-158 to walk `outputEdges` from `startVertex`, and replace the 3 Test_1 entries (`run1` becomes a CLOSEST entry on `outputEdges`; delete `curveCount` and `maxDeviation`).
   - Both live in curve_tools itself, so the migration and the trim can go in the same push.
   - driven_offset imports Curve_tools by version, and Design_Master's one Clean wire instance changes only when that import is bumped.
4. **Your decision:** `piece1..N` in Evaluate offset (replace) and in Create offset profile (optional). Decide whether you want the "Nth along chain" entry first. If you do, it lands in Variable_tools and is versioned before either producer drops `pieceN`.
5. **Later:** the out-of-scope counts listed in section 1, each together with its checker.

**Separating verified from inferred:**
- **Verified:** the key lists, line numbers, consumer mechanics, and survey results within the stated coverage.
- **Inferred:** whether the stations should be KEPT (a downstream use is plausible but not named), and whether documents outside the survey use these keys.