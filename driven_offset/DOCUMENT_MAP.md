# driven_offset: document map and working rules (read first)

Last updated 2026-09-25 (import pins re-read from the code, unwrap family added); 2026-09-29: import chain
re-read from the code again (curve_core now Curve_tools V9, extract_outputs V6, offset_profile_core added),
Create offset profile and arc-tangency entries brought up to date (docs agent). Split of generic
features into their own Onshape documents 2026-09-23. Anyone (human or agent) working in `driven_offset/` should read this
before editing or pushing.

## The document family

| Onshape document | sync project / local dir | Owns | Doc id |
|---|---|---|---|
| driven_offset (Design_Master lives here) | `driven_offset` | Driven edge offset, Driven offset surface, offset run treatment, offset debug, `edge_offset_utils`, `bspline_compat`, Create offset profile (+ `offset_profile_core`), Evaluate offset, Unwrap (+ `undrape_utils`, `unwrap_part`) | `f61d2c000ab2d1240776342e` |
| Curve_tools | `curve_tools` | `curve_core` (shared curve machinery), Clean wire, Map curve, Merge curve, Evaluate profiles | `2143812a99089658c704f0bc` |
| Variable_tools | `variable_tools` | `extract_outputs` (producer library), Extract variables feature + consumer library | `a47f90bfa6b17a59e20cebd0` |
| Reference_Side_Features | `reference_side` | Mutual Trim+, Split+, Offset+ (keep side named by a reference) | `22764764a00a7f607dbc1c4d` |

Design_Master: element `643b702dc551feb3cc001826` in workspace `5b11f323ab31b04cba8b36ef`.

## What changed in this document on 2026-09-23

- **`edge_offset_utils` no longer contains the curve machinery.** 59 top-level definitions
  (chains, stations, `classifyPoints`, `approximateFamily`, the emitters, `fmtMM` / `fmtNum` /
  `fmtVec`, `OFFSET_GEOM_TOL`, `G1_JUNCTION_ANGLE`, `OffsetPointSpacing`, the
  approximation predicate and bounds, ...) moved verbatim to `curve_core` in Curve_tools.
  `edge_offset_utils` now `export import`s `curve_core` from **Curve_tools V2**
  (`2143812a99089658c704f0bc/9e83163997d1397b495d1bfe/02d7784437f621c76397f0d6`, microversion
  `9d6f0887be37c851829c40c3`), so every feature importing `edge_offset_utils` still sees them.
  Design_Master regenerated **identically** afterwards (Default and the complex test configuration).
- **Legacy tabs: ALL GONE (2026-09-24).** Design_Master's 7 Mutual Trim+ now use Reference_Side V1
  `mutualTrimPlus`, its Clean wire uses Curve_tools V2 `cleanWire` (inserted in place, old deleted,
  query variables clean_pinch_wire / pinch_radius_edges / top_edge_Edges re-pointed via their
  createdByFeatures list). Fingerprint identical before/after in both configurations (baselines
  re-saved after the swap). Tabs clean_wire, mutual_trim_ref, design_map_query_utils, map_curve,
  merge_curve, evaluate_profiles, design_map, query_varialble_ref, export_package_manager deleted.
  Restore point: BeamBuilder V6. Driven edge offset / Driven offset surface import Variable_tools V1
  `extract_outputs`.
- Moving an instance to a feature in ANOTHER document cannot be done in place: the features API
  answers "Feature does not match" to any namespace change across elements, even for the same
  featureType. Insert the new feature at the old position (rollback bar), delete the old, re-point
  references. Deletes can 504 -- check state before retrying.

## Import chain (who pins whom) -- re-pin in this order after changing a callee

Pins as they stand in the code on 2026-09-29 (the `version` of each `import`; `""` = same-workspace tab,
always current). Two generations of `edge_offset_utils` are pinned at once: the unwrap family is on the
current one (3356bea0..., the tab's microversion on 2026-09-29); DEO / DOS / ORT / debug are on an older one
(ff3d2909...) -- re-pin them together the next time the chain is walked.

```
curve_core (Curve_tools V9, by VERSION: path 2143812a.../75b53dea4a5f869da4b28caa/02d77844..., mv 186e92dbecd6f2c72288eef8)
  <- edge_offset_utils (a2665e22c07b7a6929ce4e80, export import curve_core)
       <- offset_run_treatment (d009ddf4a8dd9534fc4dc4b5)   pins eou at ff3d2909e89ded4e32a5c49b
       <- offset_debug (6479d7fbd0ec7d11e0ae6c69)           pins eou at ff3d2909e89ded4e32a5c49b
       <- driven_edge_offset (786f62f4d67ed8d9c7d56d16)     export import eou at ff3d2909...;
                                                            ORT at 05e1f78ac7e7a8682dbc8e1a, debug at 4a442f1960c3ece8170129bc,
                                                            export import offset_profile_core at 6d80d27932aecb1b7241739f
       <- driven_offset_surface (c47cbd0497baf3011c60ecf8)  export import eou at ff3d2909...; ORT 05e1f78a..., debug 4a442f19...,
                                                            DEO at 660678a63631fbafbc92e2dd, bspline_compat (6b635e74c92bd23387e850c1)
                                                            at e91083bba71297456c5ca3d0
            evaluate_offset (a2ebb5abc7ddda01f64ff8df)      export import DEO at 660678a63631fbafbc92e2dd
       <- undrape_utils (283b8f7562a16e9c9ccc01b7)          export import eou at 3356bea0c847dcdb94230682
       <- unwrap_part (fc976128871c5b4b2d33a91c)            export import eou at 3356bea0c847dcdb94230682
       <- unwrap (a84cdaa8963f2a55db1c016b)                 export import eou at 3356bea0...;
                                                            undrape_utils at 79fd4945..., unwrap_part at dfac1a47...
offset_profile_core (9553c095d4d77c83c34a0a36)              std only: the profile machinery (regions / points -> exact pieces)
  <- create_offset_profile (3fccdcb24013c744bd0fd8a2)       export import at 6d80d27932aecb1b7241739f
  <- driven_edge_offset                                     (above: its "Profile source: Regions")
```

Variable_tools `extract_outputs` (by VERSION): every tab here now pins **V6**
(`a47f90bf.../eb9b32c556ff036c3dd19f73/3cac74f0...`, mv `cffacd73d80aa6dc1a2c4273`) -- DEO, DOS, unwrap,
evaluate_offset and create_offset_profile alike.

**Keep unwrap, undrape_utils and unwrap_part on the SAME edge_offset_utils microversion**, or two chart
versions meet in one feature. undrape_utils / unwrap_part are imported at `""`, so their edits are live in
unwrap at once; a change to edge_offset_utils reaches unwrap only after a re-pin. (2026-09-29: undrape_utils
and unwrap_part are now pinned by microversion too, see the chain above.) Never push a placeholder
import path (the whole unwrap tab failed to compile).

- Same-document imports pin an ELEMENT microversion (`GET documents/d/{did}/w/{wid}/elements`,
  field `microversionId`). Procedure: push the callee, read its new microversion, re-pin the
  callers, push them. Feature instances in a Part Studio of the SAME workspace follow the
  latest tab automatically.
- Cross-document imports pin a VERSION: `path : "<doc>/<version>/<element>", version :
  "<that element's microversion IN that version>"` (`GET documents/d/{did}/v/{vid}/elements`).
- **Changing curve code** (anything in `curve_core`): edit `curve_tools/curve_core.fs`, push the
  curve_tools project, ask the user to version Curve_tools, then re-pin `edge_offset_utils`'
  export import to that version and walk the chain above. Never re-add curve functions to
  `edge_offset_utils`: two definitions of one name visible to a feature is a compile error
  ("Multiple visible overloads with identical signature").

## Working rules

- **The user creates versions, never an agent.** Ask once when a version is needed.
- **Never POST feature changes into Design_Master while the user is working in it**
  (memory: feedback_no_feature_writes_while_live). Pushing Feature Studio code is fine and is how
  changes land; every same-workspace instance picks it up.
- **A new parameter's annotation Default is migrated into every saved instance** (correction
  25): make the default reproduce the old behaviour, or a topology-changing option defaults off.
- **Two agents, one sync tool:** `.sync-state.json` is read at the start of a push and written at
  the end, so two pushes at the same moment can lose one side's entries (e.g. a newly created
  tab's element id). Don't push concurrently with another session; if one did, check
  `python -m sync.main project status <project>`.
- Before pushing: `python fscheck.py driven_offset/*.fs curve_tools/curve_core.fs variable_tools/extract_outputs.fs`
  (pass curve_core and extract_outputs so names resolve); push with `--check`.
- **Regression check for any change that can move geometry:**
  ```
  PYTHONPATH=. MSYS_NO_PATHCONV=1 python devtools/onshape/fingerprint.py compare design_master_default
  PYTHONPATH=. MSYS_NO_PATHCONV=1 python devtools/onshape/fingerprint.py compare design_master_complex --complex
  ```
  Baselines saved 2026-09-23 after the rewire. A difference is either the intended change
  (then `save` a new baseline) or a regression. The first regen after a push may time out
  (30 s) -- rerun.
- Other API helpers: `devtools/onshape/fseval.py` (run a std-only FS snippet against any Part
  Studio, optional configuration), `fsapi.py` (insert/update feature instances; selections by
  deterministic ids), `fsblocks.py` (split a .fs file into top-level definition blocks).

## Where the context is

- Memory (shared by every session in this project dir): `driven-offset.md` (architecture,
  frames, bugs), `driven-offset-surface.md`, `driven-offset-research-decisions.md`,
  `extract-variables-v2.md`, `clean_wire` notes, `reference-side-and-variable-tools.md` (the
  split, the other documents' state), `onshape-live-inspection.md` (eval API, configurations,
  follow mode), `.claude/featurescript-corrections.md` (read before coding).
- Research / design notes in this folder: `research_*.md`, `extract_variables_schema.md`
  (the producer / Extract variables contract -- a copy lives in `variable_tools/`).
- Unwrap explained end to end (theory, use, code map, test studios, open items): `docs/unwrap_explained.md`.

## In design

- **create_offset_profile** (tab 3fccdcb24013c744bd0fd8a2, BUILT 2026-09-24; the profile machinery moved to
  tab `offset_profile_core` 9553c095d4d77c83c34a0a36, shared with Driven edge offset): builds DEO/DOS offset
  profiles from Regions or Points; profile BREAKS instead of zero-length connectors; exact Beziers.
  Region shapes CONSTANT / LINEAR / SMOOTH / QUADRATIC ("Flat at" Start / End); stations typed ("Enter X") or
  "At point" (vertex / mate connector world X + signed offset along +X); a region may be entered toward -X
  (start > end: mirrored onto itself). Driven edge offset takes the same regions directly ("Profile source:
  Regions", no wire; test studio "DEO regions tests" 5efbe29c46094eba1499c4fb, D1-D5, built / checked by
  `devtools/onshape/build_deo_regions_tests.py` / `check_deo_regions_tests.py`).
  Tests are REAL instances T1..T12 in Part Studio "Offset profile tests" (e18678532ec07b057b372dbd), named by
  case and expected result; check them with `PYTHONPATH=. python devtools/onshape/check_offset_profile.py`
  (T1-T12 plus 3 error cases via temporary instances; fixtures for T6-T12 built by
  `devtools/onshape/build_offset_profile_tests.py`). No test tabs (user preference). Not yet done: consumer-side
  break handling in DEO / DOS (None / Line per break) -- today a break always splits the output.
  Design: `research_create_offset_profile.md`.

- **arc / line tangency** (review `reviews/2026-09-25_arc_line_fitting/`, implemented 2026-09-25/26):
  `shapeRuns` + tangent biarc chains in curve_core (Curve_tools V9); DEO / DOS option "Varying offset on
  arcs" (Spline default / Biarc fit, in `edge_offset_utils`); callers DEO, DOS, Map curve, Unwrap, Evaluate
  offset. Test studios "Arc tangency tests" (Curve_tools) and "Arc tangency DEO tests" (380570afe5337a3782a8a662,
  D1-D4, `devtools/onshape/build_arc_tangency_deo_tests.py` / `check_arc_tangency_deo_tests.py`). Status and
  loose ends: top of `reviews/2026-09-25_arc_line_fitting/SUMMARY.md`.

- **evaluate_offset** (tab a2ebb5abc7ddda01f64ff8df, BUILT 2026-09-24): DEO run backwards -- reference + target
  edges -> offset profile, on DEO's own stations (DEO exports `offsetStationBase` / `offsetStationFrames` /
  `offsetStationsWithBreaks`). Tests: studio "Evaluate offset tests" (0ce4ac09e693f8ecd18e8a7f), built by
  `devtools/onshape/build_evaluate_offset_tests.py`, checked by `PYTHONPATH=. python devtools/onshape/check_evaluate_offset.py`
  (11/11). Reference-wire measure not supported yet. As-built notes: section 0 of `research_evaluate_offset.md`.

- **unwrap** family, BUILT 2026-09-24/25 (explained: `docs/unwrap_explained.md`):

  | Tab | Element | Role |
  |---|---|---|
  | unwrap | a84cdaa8963f2a55db1c016b | the feature: Edges / Constant-thickness part / Part (solid); the user's docstring heads the file, the AS BUILT block follows |
  | undrape_utils | 283b8f7562a16e9c9ccc01b7 | the undrape map (constant-thickness plates) |
  | unwrap_part | fc976128871c5b4b2d33a91c | solid unwrap (`unwrapSolid`) |
  | edge_offset_utils | a2665e22c07b7a6929ce4e80 | the chart (`unwrapChart` / `unwrapFast`, packed plain-number tables), shared with DEO / DOS |
  | evaluate_offset | a2ebb5abc7ddda01f64ff8df | DEO run backwards (above) |
  | create_offset_profile | 3fccdcb24013c744bd0fd8a2 | offset profiles (above) |
  | offset_profile_core | 9553c095d4d77c83c34a0a36 | the profile machinery, shared by Create offset profile and DEO's Regions source |

  Test studios (tests are real instances in the feature tree, named by case and expected result):
  "Unwrap_Testing Copy 1" (80c1e329f99a05e224058526; U1-U3, built by `devtools/onshape/build_unwrap_tests.py`),
  "Unwrap_Testing Copy 2" (681a5825e353d376c88224eb; production plates and parts, built by
  `devtools/onshape/build_unwrap_parts.py`), "Evaluate offset tests" (0ce4ac09e693f8ecd18e8a7f),
  "Offset profile tests" (e18678532ec07b057b372dbd), "DEO regions tests" (5efbe29c46094eba1499c4fb),
  "Arc tangency DEO tests" (380570afe5337a3782a8a662).

  Regression (repo root, Git Bash):
  ```
  FS_SYNC_TIMEOUT=300 PYTHONPATH=. python devtools/onshape/check_unwrap_regression.py [--save]   # both Unwrap studios vs fingerprints/unwrap_baseline.json
  PYTHONPATH=. python devtools/onshape/check_evaluate_offset.py
  PYTHONPATH=. python devtools/onshape/check_offset_profile.py
  PYTHONPATH=. MSYS_NO_PATHCONV=1 python devtools/onshape/fingerprint.py compare design_master_default   # whenever edge_offset_utils changes
  ```
  Design notes: research_unwrap.md (09-10 pre-build plan, partly superseded), research_unwrap_perf.md,
  research_unwrap_part.md (as built 8 / 8b), research_undrape_ops.md, research_undrape_map.md (as built 12-15).

## Known open items touching this document

- Design_Master's Clean wire is on Curve_tools V2; when Curve_tools V3 exists, bump its namespace
  (same element -> in-place update works) to get the ends / runs / breaks outputs.
- `Pinch_Fillet` (std fillet) shows ERROR in Default on 2026-09-23; not caused by the rewire
  (identical geometry) -- check whether it was already red.
- `chainStations` (now in curve_core) still computes offset frames the curve tools never read; a
  plain sampler in curve_core with the frame step back in `edge_offset_utils` would be cleaner.
- Extract variables pilot blocks in Design_Master (see `extract-variables-v2.md`).
