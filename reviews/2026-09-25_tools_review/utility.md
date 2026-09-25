# Track Report: Utility features (Join wires, Extrude edge, Offset edges, Simple body rename / Name Bodies, Composite intersection)

## 1. Bottom line
- **Composite intersection has been found, but it lives only in Onshape and has never been synced.** It is doc "Mindbender Cores" `bf5cecdcf161451efd4ecd4f`, ws `091186f124e6aef40c57a36d`, Feature Studio tab `compositeIntersect` (e `9fc4d0ad66c18db90874a5f1`), FS 2892, 143 lines. It has 4 production instances in Part Studio "Ski Cores" (e `15bcdf5599ce5e505bc55596`), all regenerating OK. A sibling feature, "Subtract Composite", is tab `subtractComposite` in "20BSS 25 XXX Design" `0286a6633a70d9f91ea53482`. Nothing was pulled.
- **Name Bodies (`bodyRename/Body_Rename.fs`) is broken and should be retired.** Simple Body Rename (grade B) stays as the rename tool and gets small guards.
- **Keep Join wires and Extrude edge as thin tools, and modernize them.**
  - Join wires is native Composite curve plus a delete of the inputs.
  - Extrude edge is needed because native surface extrude only accepts sketch edges.
  - Neither should merge into Curve_tools. Clean wire rejects closed loops and multiple chains; Merge curve refits the geometry.
- **Offset edges (B-) is live in `example_1/refSurfCreation/offsetEdges.fs` on curveMapping V23.** Its biggest risks:
  - It has no tests.
  - The stale pre-port copy `pathProcessing.fs` is still used by 3 other features.
  - It keeps its own unbatched parallel-transport frame table.
  - The first step is a test Part Studio, before any refactor.
- **variable_tools:** none of the five needs custom keys. The standard keys cover them. Only Composite intersection needs an explicit `embedStandardOutputs` call, to narrow `output` to the result composite. **Icons:** none of the five has one, and none is in `icons/icon_targets.py`.

## 2. Per-feature verdict

| Feature | State | Biggest problems | Effort |
|---|---|---|---|
| Composite intersection (remote) | Works on current inputs; C | try/catch used as control flow (2 blocks + 6 `try()` deletes); 3 blocks without braces; em dashes in comments; possible wrong-body branch (unverified); not synced; no icon or tests | M (rewrite around evCollision) + move |
| Offset edges | Live, B- | No tests; stale pre-port copy in `pathProcessing.fs`; own frame table with per-point `evEdgeTangentLine`; ~70 dead lines (station loop); 10 enums without SHOW_LABEL; blend intersections keyed by region name; Normal/Binormal labels swapped vs the math | L (P0 S-M, P1 M, P2 M-L) |
| Extrude edge | Needed, C+ | `extractDir` can fall off the end with no return (verified 124-157); no zero-vector check; no check for a direction along the edges (the user's live EXTRUDE_FAILED); MaxNumberOfPicks 10; bounds allow 0 and negatives; no SHOW_LABEL | S (P0) / M (P1) |
| Join wires | Live (RD FLAT_PROFILE), C | Filter is WIRE bodies only despite the label "Edges and wires" (verified :10); no empty check; no warning when the output is N wires (seeds still deleted); output unnamed | S |
| Simple Body Rename | B, keep | No empty-query guard; blank-name test is on prefix~name~suffix (verified :43), so an empty name plus a prefix renames to the bare prefix; no info notice | S |
| Name Bodies | D/F, retire | Find/replace never matches (query-map equality, verified :272); positional `qNthElement(qEverything)` binding (verified :35); println debug output; regex unescaped | S (freeze) |

## 3. Prioritized recommendations

**P0**
1. **Offset edges test Part Studio plus `devtools/onshape/build_/check_offset_edges_tests.py`, with a fingerprint baseline of Create_SW_Shelf (`fingerprint.py --did 9732a1a9…`).**
   - Why: there are no tests. `devtools/onshape` has `build_evaluate_offset_tests.py`/`check_offset_profile.py`, but those target the driven_offset features, not Offset edges (verified).
   - Effort: M, about 1.5 d. The expert's cases OE1-OE17 are in §4.
2. **Extrude edge direction resolution.**
   - Replace `extractDir` (`example_1/extrudeEdge.fs:124-157`) with a `QueryFilterCompound.ALLOWS_DIRECTION || BodyType.MATE_CONNECTOR` filter and std `extractDirection`, resolving mate connectors through `qOwnerBody`.
   - Add regenErrors for empty input, a zero vector, and a direction parallel to the edge tangents (sampled at 0 / 0.5 / 1).
   - Effort: S.
3. **Join wires input and feedback (`example_1/joinWires.fs`).**
   - Filter: `(EDGE || (BODY && WIRE)) && ConstructionObject.NO`, then `dissolveWires`.
   - Empty selection: regenError.
   - After `opExtractWires`, count `qCreatedBy(id, BODY)`; if it is more than 1, `reportFeatureWarning`.
   - Delete only seed wire bodies, never sketch bodies.
   - Effort: S, about 25 lines.
4. **Simple rename guards (`bodyRename/Simple_Rename.fs:40-51`).**
   - Skip an item if `isQueryEmpty` or its `renameString == ""`; list the skipped item numbers in one warning.
   - Add `reportFeatureInfo` "Renamed N bodies".
   - Report a body picked in two items with an info notice.
   - Leave the `myFeature` export name alone; renaming it breaks saved instances.
   - Effort: S.
5. **Name Bodies: freeze it and append "(legacy)" to its Feature Type Name.** Do not fix it in place. Count its live instances before deleting it. Effort: S.
6. **Offset edges style and dead-code pass.**
   - Add braces at 100-101, 108-109, 1189-1192.
   - Add SHOW_LABEL to the 10 enums.
   - Change the warnings at 475/2304/2311/2422 to info; add a warning at the silent skip at 1058; add faulty parameters at 912.
   - Delete the station loop at 943-1010. I verified that no `station1..5` parameter exists in the precondition, so every iteration `continue`s. Also delete the hidden `regionNum` parameter.
   - Do this after item 1 is in place. Effort: S.

**P1**
7. **Composite intersection rewrite.**
   - `opPattern` the constituents, then `evCollision` (tools = solid), then branch on ClashType: keep `TARGET_IN_TOOL`; one `opBoolean` INTERSECTION with keepTools for INTERFERE / `TOOL_IN_TARGET`; one `opDeleteBodies` for NONE/ABUT.
   - Remove all try/catch, add braces, make the source ASCII, and add empty-selection regenErrors plus an info summary.
   - Build a test Part Studio (6 cases, listed by the expert).
   - Effort: M. Any change to where it lives needs the user's approval (see item 8).
8. **Move Composite intersection and Subtract Composite into a synced tools doc (smallTools or a new composite_tools), then repoint the 4 Ski Cores instances.** Effort: M. This needs the user's approval: it means creating a doc, pushing, and changing production instances.
9. **Offset edges performance and cleanup.**
   - Give the editing logic a light path with no parallel-transport table; it currently rebuilds the full table on every dialog change.
   - Cache `curveType` per edge.
   - Batch `evEdgeTangentLines` per edge.
   - Use analytic profile derivatives.
   - Replace the 4 insertion sorts with std `sort()`.
   - Key blend intersections by a stable hidden region id instead of the name.
   - Split the file into 3 tabs.
   - Effort: M, about 2-3 d.
10. **Extrude edge P1.**
    - Replace the input-type enum with one EDGE||WIRE selection, keeping the legacy parameters through a defaults map.
    - End types BLIND / SYMMETRIC / UP_TO_SURFACE / UP_TO_VERTEX, with SHOW_LABEL.
    - `NONNEGATIVE_LENGTH_BOUNDS` plus a flip instead of negative depths.
    - Optional Name parameter; FS 3070.
    - Effort: M.
11. **Join wires and Simple rename P1:** a Name parameter (Join wires), defaults maps, FS 3070. Effort: S.

**P2**
12. **Add curveMapping core options `exactNearLinear` and `seedNormal`.** Offset edges can then drop about 250 lines: `frameAtArc`'s exact branch, its PT table, `sampleParallelTransportFrame` and `seedTreatsAsLine`. This needs a new curveMapping version from the user. Effort: M-L.
13. **Deal with the stale pre-port copy.** Re-pin `pathProcessing.fs` (currently old core `34dde8fb/08ced7a9`, verified) onto V23, or retire it. It is used by `variableSurfaceOffset.fs`, `regionProcessing.fs` and `tooling/simpleTopWallGeneration.fs` (verified), which likely still carry the flat-arc failure. Effort: M.
14. **Optional safe find/replace in Simple rename.** Capture the current names in the editing logic (correction 36) and replace literal text, not a regex. Effort: M.
15. **Optional merge of Composite intersection and Subtract Composite** into one "Composite boolean" feature with an enum, fixing the `composteBody` typo in the process.
16. **Offset edges display wording.** Replace "Frenet" with "path frame" and fix the Normal/Binormal labels (display text only; parameter ids stay). Needs the user's wording.

## 4. Designs

**Offset edges**
- The live copy is the example_1 workspace, element `5fdfa98df6da1c54c14273fe`. The local hash matches the last push; git is clean.
- The workspace is probably ahead of version f352eb88 (inference). Ask the user to version it before Design Master picks up the flat-arc fix.
- Remove the stale sync-state key `example_1\offsetEdges.fs`, which maps to the same element.
- Do not move the region/blend/arc logic into curveMapping core. If the arc-pair emitters are ever needed elsewhere, extract them into Curve_tools `curve_core` instead.
- Test cases, placed 1000 mm apart:

| Case | Setup | Expected |
|---|---|---|
| OE1 | Line, normal offset 10 | 10 mm out of plane, length 200 |
| OE2 | R100 arc, Keep as arcs | CIRCLE R90/110 |
| OE3 | R100 arc, Convert to splines | BSPLINE, deviation < 0.01 |
| OE4 | 3 x R20 m arcs | Builds |
| OE5 | Varying 0 to 10, Keep as arcs | 2 CIRCLE edges, INFO |
| OE6 | G1 blend | 1 wire |
| OE7 | G2 blend | Curvature jump < 1% |
| OE8 | Dwell | Constant over the first 20 mm |
| OE9 | Quadratic / smooth profiles | Values at alpha 0.5 |
| OE10 | Single-region pins | Passes through the pins |
| OE11 | Flip | Regions mirrored |
| OE12 | Helix | No twist |
| OE13 | Overlapping regions | WARNING |
| OE14 | No regions | WARNING |
| OE15 | Blend distance larger than region | INFO, clamped |
| OE16 | Non-G1 selection | ERROR |
| OE17 | Region consumed by blends | WARNING |

**Join wires:** "Composite curve that consumes its inputs and names the result". Flow: `dissolveWires`, then `opExtractWires`, then count the wires (warning if more than 1), then delete the consumed seed wires. Tests:

| Case | Setup | Expected |
|---|---|---|
| J1 | Two touching wires | 1 wire, OK |
| J2 | Solid edges plus a wire | OK |
| J3 | Gap between inputs | WARNING, 2 wires |
| J4 | Closed loop | INFO closed |
| J5 | Keep seeds | 3 bodies |

**Extrude edge:** as in P0 item 2 and P1 item 10. Tests:

| Case | Setup | Expected |
|---|---|---|
| E1 | Wire + mate connector | OK |
| E2 | Model edges + plane | OK |
| E3 | Direction along the edges | ERROR with message |
| E4 | Second direction | OK |
| E5 | Up to surface | OK |
| E6 | Empty selection | ERROR |

**Simple rename:** tests:

| Case | Setup | Expected |
|---|---|---|
| R1 | Solid renamed | OK |
| R2 | Composite renamed | OK |
| R3 | Prefix + suffix | OK |
| R4 | Blank name | Skipped, INFO |
| R5 | Lost reference | WARNING, others still renamed |
| R6 | Wire body | OK |

**Composite intersection:** see P1 item 7. Output: a named result composite. Whether it is closed or open is a question for the user.

## 5. variable_tools

| Feature | Keys | Reason |
|---|---|---|
| Offset edges | none (standard keys) | Standard `output` = merged wire and `inputs` = selection + reference point. Publish `startVertex` / `endVertex` only if the Modify curve end rework consumes them. Adding any variable_tools import requires FS 2909 → 3070 first (inference). |
| Join wires | none | The free `output` / `outputEdges` / `outputVertices` from `qCreatedBy` cover every downstream pick seen. |
| Extrude edge | none | Standard `outputFaces` / `outputEdges` are free. Add `endEdges` (`qCapEntity` END) only if a real consumer appears. |
| Simple rename | none | It only changes properties. |
| Name Bodies | none | Retiring. |
| Composite intersection | standard only, with `output` narrowed | `embedStandardOutputs(context, id, {output: qCreatedBy(id + "result", EntityType.BODY), outputDescription: "The intersected composite part", inputs: qUnion([definition.solidBody, definition.compositePart])})`. The default `output` would mix the composite with its copied constituents. |

## 6. Icons
All are absent. Styles: #333 for the main shape, #999 for ghosts and source geometry, blue #1651B0 for the accent.

| Feature | Glyph concept | `icon_targets.py` entry |
|---|---|---|
| Offset edges | #999 source curve with a #333 offset curve flaring away from it, plus two blue offset ticks of different length | `('example_1/refSurfCreation','offsetEdges','offset_edges')`. Must stay distinct from the Driven edge offset icon. |
| Join wires | Two #333 segments meeting at a blue link/dot joint, with #999 ghost seeds | `('example_1','joinWires','join_wires')`. Do not reuse the composite-curve glyph; Merge curve already uses it. |
| Extrude edge | #333 curved edge swept into a #999/white ribbon, with a blue direction arrow | `('example_1','extrudeEdge','extrude_edge')`. Export Onshape's extrude glyph into `icons/onshape_reference` first. |
| Simple rename | #333 part outline with a blue I-beam and "Aa" | `('bodyRename','Simple_Rename','simple_body_rename')` |
| Composite intersection | Composite-part glyph plus a blue intersect badge (two overlapping shapes, overlap filled) | Add after the move. |
| Name Bodies | none | Retiring. |

Deploying to example_1 (25+ tabs, with unversioned pushed work) needs the user's go-ahead.

## 7. Conflicts, verification, open questions

**Conflicts**
- Join wires vs Curve_tools: the brief suggested a possible overlap with Clean wire / Merge curve. The expert showed neither is a substitute, and I agree. Clean wire refuses closed loops and multiple chains; Merge curve refits the geometry. Resolution: keep Join wires as a thin tool. Do not port it: moving it to another document changes the feature type, and existing instances cannot be repointed.
- Offset edges vs curveMapping: memory implied the core was "ported into" curveMapping. Only the frame primitives are shared. The real duplicate is `pathProcessing.fs` inside example_1, which I verified.

**Spot-checks of the high-severity findings**

| Finding | Result |
|---|---|
| Name Bodies `bodyMap.query == searchQuery` at :272 and positional `qNthElement` at :35 | Confirmed |
| `pathProcessing.fs` pinned to old core 34dde8fb/08ced7a9 with the old frame functions; imported by 3 features | Confirmed |
| No Offset edges tests | Confirmed. The `offsetEdges` hits in `build_evaluate_offset_tests.py` are parameter names of another feature. |
| Offset edges dead station loop (medium) | Confirmed: no `station*` fields in the precondition. |
| Extrude edge fall-through without return | Confirmed |
| Composite intersection "no-split branch keeps the wrong body" (high) | Downgraded to medium / unverified. I did not read the remote source. When a solid lies entirely inside a constituent, the closed shell may split the constituent successfully, so the failing branch may never be reached. The evCollision rewrite makes this moot either way. |

**Unverified**
- Composite intersection dropping sheet/wire constituents.
- Behaviour when a mate connector arrives as its vertex (correction 44) in Extrude edge.
- Whether the FS 3070 bump changes geometry.
- Whether the Offset edges workspace is ahead of f352eb88.
- `opExtractWires` on T-junction inputs.

**Open questions for the user**
1. May Composite intersection and Subtract Composite move to a synced tools doc (which one?) and the 4 Ski Cores instances be repointed? Should the two merge into "Composite boolean"? Should the result be closed or open?
2. How many Name Bodies instances exist, and in which documents? Should Simple rename gain find/replace or "keep current name + prefix"?
3. Has example_1 been versioned since f352eb88, and which version does Design Master reference?
4. Are Variable surface offset and Top Wall 2 still used? This decides whether to port the frame fixes or retire `pathProcessing.fs`.
5. What wording for Offset edges' Normal/Binormal labels? Is Offset edges maintenance-only now that Create offset profile / Driven edge offset exist?
6. Will the Modify curve end rework consume Offset edges end vertices? This decides whether to publish `startVertex` / `endVertex`.

### Critical Files for Implementation
- C:/Users/jed.yeiser/documents/featurescripts/example_1/refSurfCreation/offsetEdges.fs
- C:/Users/jed.yeiser/documents/featurescripts/example_1/refSurfCreation/pathProcessing.fs
- C:/Users/jed.yeiser/documents/featurescripts/example_1/extrudeEdge.fs
- C:/Users/jed.yeiser/documents/featurescripts/example_1/joinWires.fs
- C:/Users/jed.yeiser/documents/featurescripts/bodyRename/Simple_Rename.fs (plus C:/Users/jed.yeiser/documents/featurescripts/bodyRename/Body_Rename.fs to retire)
- Remote: Onshape doc bf5cecdcf161451efd4ecd4f / tab compositeIntersect 9fc4d0ad66c18db90874a5f1