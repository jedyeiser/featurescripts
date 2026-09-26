# composite_part_tools

Local only. No Onshape document exists yet: creating it, pushing, and repointing any production
instance are the user's calls (utility.md P1 item 8).

| Tab | Feature types |
|---|---|
| `composite_boolean.fs` | `compositeBoolean` "Composite boolean" (new), `compositeIntersect` "Composite intersection" (legacy), `subtractComposite` "Subtract Composite" (legacy) |

Tests: `devtools/onshape/build_composite_tests.py` / `check_composite_tests.py` (need
`composite_part_tools/.document.json`, so they only run once the document exists).

## Composite boolean

| Operation | Result |
|---|---|
| Intersect with solid (default) | New composite of the constituents' parts inside the solid. Inputs untouched. |
| Subtract solid | New composite of the constituents' parts outside the solid. Inputs untouched. |
| Subtract composite from bodies | The old Subtract Composite: the constituents are cut out of the selected bodies (consumed unless "Keep composite"). |

Clip flow: `opPattern` the solid constituents (op id `allCopies`) -> `evCollision` per copy (tools = solid)
-> INSIDE / OUTSIDE / CROSSING / TOUCHING -> one `opBoolean` (keepTools) for the crossing copies -> one
`opDeleteBodies` for the dropped copies -> `opCreateCompositePart` (op id `result`), optionally named.

Placement rules (`placementInSolid`):
- INTERFERE or TOOL_IN_TARGET -> CROSSING (shared volume, so the boolean is valid).
- TARGET_IN_TOOL -> INSIDE. No clash (or EXISTS) -> OUTSIDE.
- ABUT_* only -> ask again with the copy as the tool: TOOL_IN_TARGET / ABUT_TOOL_IN_TARGET -> INSIDE;
  INTERFERE / TARGET_IN_TOOL -> CROSSING; ABUT_TOOL_OUT_TARGET -> OUTSIDE; still unknown -> TOUCHING
  (intersect leaves it out, subtract keeps it, both WARNING). Test C4 pins the kernel's real answer.

Closed vs open: the result is **open by default**. The old feature made an open composite, and Ski Cores
depends on that: `AllCores` nests the four results in another open composite, and the `*Bodies` query
variables pick the constituents individually. "Closed composite" is a parameter (default off, correction 25).

Non-solid constituents (sheets, wires) are left out and counted in the INFO summary.

variable_tools: not wired. No consumer of these features uses Extract variables (Ski Cores uses native
"Created by" query variables), and the import would make the tab depend on a pinned Variable_tools version.
To add it later: import extract_outputs.fs as `smallTools/Move_Along_Edge.fs` does, and at the end of
`clipComposite` call `embedStandardOutputs(context, id, { "output" : qCreatedBy(id + "result", EntityType.BODY),
"outputDescription" : "The clipped composite part", "inputs" : qUnion([compositePart, solidBody]) })`.

## Why the two legacy feature types are still exported

A saved instance cannot change feature type in the UI; moving a feature to a new document changes its
namespace. The cheapest repoint that keeps every downstream reference is a **namespace-only** change of the
saved feature (same featureType, same parameter ids, same feature id), because downstream references are
keyed on the feature id plus the operation id. So:

- `compositeIntersect` keeps `solidBody` / `compositePart`, op ids `allCopies` and `result`.
- `subtractComposite` keeps `composteBody` (the old typo is the saved id) / `targetBodies`, op id `subtractComposites`.

Both call the same code as Composite boolean. Once no instance uses them, delete them (or rename their
Feature Type Name to "... (legacy)").

Unverified: whether `POST .../features/featureid/{fid}` accepts a namespace pointing at a different
document. If it refuses, the fallback is inserting new features and re-picking downstream references.

## Instance survey (read-only GETs, 2026-09-25)

**Mindbender Cores** `bf5cecdcf161451efd4ecd4f`, version `ea22580840ba59155ec76b76` (the version was read,
not the workspace). Part Studio "Ski Cores" `15bcdf5599ce5e505bc55596`, 65 features, all 4 instances OK.
Namespace `e9fc4d0ad66c18db90874a5f1::m11c164cec18898d68fc8ed12`.

| # | Feature | Feature id | solidBody | compositePart |
|---|---|---|---|---|
| 15 | ETE_Core_Composite | FzOQ9v7Q2gIZkUY_20 | click (qCompressed) | QV `useCoreComposite` |
| 16 | MNS_Composite | FHEkGPsBK2ac7CM_36 | click | QV `useCoreComposite` |
| 17 | ONE_Composite | FbxSXGCEDOimQIo_52 | click | QV `useCoreComposite` |
| 18 | MOO_Composite | FglRHsUrR8twciW_68 | click | QV `useCoreComposite` |

Only the two parameters exist (the old feature had no others). Downstream:
- QVs `eteBodies` / `mnsBodies` / `oneBodies` / `mooBodies` (19-22): "Created by" feature, BODY. They feed
  the groove booleans `eteGrooves` / `mnsGrooves` / `ONE_Grooves` / `MOO_Groove` (51, 54, 57, 60).
  Independent of op ids; the created-by set (copies + clipped pieces + composite) keeps the same makeup.
- `ETE_Shift`, `ONE_Shift`, `MOO_Transform` (61-63) and `AllCores` composite (64, open) reference
  `<featureId>` + `result` / opCreateCompositePart. Kept.

**20BSS 25 XXX Design** `0286a6633a70d9f91ea53482` (workspace `2d395a8b395f63109c4b77f7`): Subtract Composite
lives in tab `subtractComposite` `718b7970766ff887e78f5ef6` (FS 2491). One instance: Part Studio "Tooling_Prep"
`84a30a209e3434a003885a33`, #193 "Subtract Composite 1" `FEyPBUhCXa4VNuQ_34`, status OK, namespace
`e718b7970766ff887e78f5ef6::m9f256e81e2090381e5aea592`. `composteBody` = click on composite
`FC4QqvtNflp2nDo_32` result; `targetBodies` = click. Downstream: "Mate connector 27" (#380) references an
edge created by `<featureId>` + `subtractComposites` / opBoolean. Kept. "Design Master" and
"Tooling_Generation" have no instances.

## Migration plan (each step needs the user's go-ahead)

1. Create the Onshape document composite_part_tools; `pushproject composite_part_tools --files
   composite_boolean.fs --check` (compile notices clean).
2. `build_composite_tests.py`, then `check_composite_tests.py`: all pass. C4 settles the ABUT classification;
   C5 settles that one INTERSECTION boolean clips several targets independently.
3. User versions composite_part_tools.
4. Baseline Ski Cores: fingerprint / `notices --monitor "Ski Cores"` on Mindbender Cores (volumes of the four
   result composites and of `AllCores`).
5. Repoint the four instances by namespace only, one at a time (featureType `compositeIntersect` unchanged,
   namespace -> the new tab at the new version). If the API refuses a cross-document namespace, stop and
   ask: the fallback is new Composite boolean features plus re-picking `ETE_Shift` / `ONE_Shift` /
   `MOO_Transform` / `AllCores` and the four QVs.
6. Re-run the fingerprint: same volumes, same feature statuses (the four become INFO -- the summary notice --
   instead of OK). Report any downstream feature that lost a reference (correction 47).
7. Same for 20BSS Tooling_Prep "Subtract Composite 1" (`subtractComposite`, check Mate connector 27).
8. Later, optionally, move the instances onto `compositeBoolean` itself (parameter ids match for the
   intersect instances; `composteBody` -> `compositePart` for the subtract one), then retire the legacy types.
