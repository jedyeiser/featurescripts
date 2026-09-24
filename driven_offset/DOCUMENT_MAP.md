# driven_offset: document map and working rules (read first)

Last updated 2026-09-23, after generic features were split out of this document into their
own Onshape documents. Anyone (human or agent) working in `driven_offset/` should read this
before editing or pushing.

## The document family

| Onshape document | sync project / local dir | Owns | Doc id |
|---|---|---|---|
| driven_offset (Design_Master lives here) | `driven_offset` | Driven edge offset, Driven offset surface, offset run treatment, offset debug, `edge_offset_utils`, `bspline_compat`, unwrap / evaluate_offset stubs | `f61d2c000ab2d1240776342e` |
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
- **Legacy tabs, kept only until Design_Master's instances are migrated -- do not develop them here:**
  - `clean_wire`, `map_curve`, `merge_curve`, `evaluate_profiles` -> develop in `curve_tools/`.
    Design_Master uses 1 Clean wire (and no Map curve / Merge curve / Evaluate profiles).
  - `mutual_trim_ref` (Mutual Trim+) -> develop in `reference_side/mutual_trim_plus.fs`.
    Design_Master has 7 instances.
  - `design_map` (Extract variables) and `design_map_query_utils` -> develop in `variable_tools/`.
    The producers here (Driven edge offset, Driven offset surface, and the legacy tabs) still
    import `design_map_query_utils` at microversion `5ab212db97cb46b643862b40`. Re-pinning them
    to Variable_tools V1 `extract_outputs` is pending. The embedded map is recognised
    structurally, so both versions are readable by either Extract variables.
- `query_varialble_ref.fs`, `Reese_*.fs`, `export_package_manager.fs`: third-party / reference copies.

## Import chain (who pins whom) -- re-pin in this order after changing a callee

```
curve_core (Curve_tools, by VERSION)
  <- edge_offset_utils (a2665e22..., export import)          current mv eb23f15e06a23d0fd2e2197e
       <- offset_run_treatment (d009ddf4...)                  current mv bd81293ec01682880717eb76
       <- offset_debug (6479d7fb...)                          current mv 3d13f8cddcd8668502860216
       <- driven_edge_offset (786f62f4..., export import eou; imports ORT + debug)
       <- driven_offset_surface (c47cbd04..., export import eou; imports ORT, debug, DEO, bspline_compat)
       <- legacy: map_curve, clean_wire, mutual_trim_ref
```

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
- Before pushing: `python fscheck.py driven_offset/*.fs curve_tools/curve_core.fs` (pass
  curve_core so names resolve); push with `--check`.
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

## Known open items touching this document

- Re-pin the producers here from `design_map_query_utils` to Variable_tools `extract_outputs`.
- Migrate Design_Master's 1 Clean wire and 7 Mutual Trim+ to the new documents, then retire the
  legacy tabs.
- `Pinch_Fillet` (std fillet) shows ERROR in Default on 2026-09-23; not caused by the rewire
  (identical geometry) -- check whether it was already red.
- `chainStations` (now in curve_core) still computes offset frames the curve tools never read; a
  plain sampler in curve_core with the frame step back in `edge_offset_utils` would be cleaner.
- Extract variables pilot blocks in Design_Master (see `extract-variables-v2.md`).
