# Arc / line fitting and corner preservation -- research handoff (2026-09-25)

Read-only research. Nothing here is implemented. Line numbers are as of 2026-09-25 and will drift;
search for the function name if a line has moved.

Companion to `reviews/2026-09-25_tools_review/` (same review cycle).

## 0. Summary for the implementing team

1. `classifyPoints` (`curve_tools/curve_core.fs:252`) decides "arc" / "line" / "freeform" on
   POSITIONS only. The arc it emits has whatever end tangents the 3-point circle happens to have;
   the spline next to it is pinned to the TRUE tangent. Result: small G1 kinks at every
   approximate arc or line. Lines have the same flaw (chord accepted on a slightly curved run).
2. The driven_offset callers gate arcs by SOURCE curve type only (`sourceShapeGates`,
   `driven_offset/edge_offset_utils.fs:85`), never by "is the offset constant over the run".
   A varying offset on an arc source is therefore offered as an arc, and the result is an
   approximation with unconstrained tangents.
3. User decisions (below): add a user choice **Biarc fit / Spline** for arc sources with a varying
   offset; G1 between arcs is acceptable; add a tangent check to arc/line acceptance.
4. A working tangent-constrained biarc already exists: `example_1/refSurfCreation/offsetEdges.fs:1867-2101`.
5. Corner preservation is mostly fine; two real issues: Clean wire groups fit through any corner,
   and Evaluate profiles "Efficient" merges freeform edges up to ~45 deg (bug vs its docstring).

## 1. The case that started it

Document `9732a1a905d6ac91d8990105`, workspace `4b70b4378f844dff881b5282`, Part Studio
`b8ccc6d4f6f771560ff7fd07` (default configuration). Rollback bar after feature 45.

- `Create offset profile 1` -> `Clean wire 1` (MANUAL) -> `SW_Shelf_offset` (Driven edge offset,
  `runBreakMode` DISCONTINUITIES, `joinTangentRuns` false, approximation tol 0.01 mm, 15 CPs,
  offset edges = query variable `FPT_Edges`, 13 edges).
- Source: 11 exact arcs between two tip/tail splines. The edge across MRS (x 880) runs
  x 626.5 -> 1248.2, L 621.7 mm, R 14177.9 mm.
- Offset profile is NOT constant: -0.80 at x 145, -0.57 at 626, min ~-0.50 near x 800,
  -0.65 at 1248, -0.80 at 1615.

Measured on the output (eval API):

| Item | Value |
|---|---|
| MRS run: best 3-point circle | R 14544.6 mm |
| MRS run: max radial error | 0.021 mm (tol 0.01) -> rejected, emitted as spline (deg 3, 6 CPs) |
| MRS run: radial residual shape | smooth S: 0, -0.013, -0.020, -0.020, -0.012, 0, +0.012, +0.020, +0.020, +0.013, 0 |
| MRS run: local radius along | 14876 -> 14965 -> 14548 -> 14138 -> 14220 mm |
| Other runs (77-101 mm) | passed as arcs (same effect, under 0.01 mm over the shorter span) |
| Joint kink x 1615.0 (arc -> tip spline) | 0.023 deg |
| Joint kink x 626.5 (arc -> MRS spline) | 0.0012 deg |
| Joint kink x 424.0 (arc -> arc) | 0.001 deg |

Why: the offset of a circle by a varying distance is not a circle. The code never tries to fit
arcs with tangency; it only asks "is this already an arc within tol", else falls back to a spline.

## 2. Tangency audit -- where arcs/lines are emitted

Risk: KINK = end tangents can disagree with the neighbour; SAFE = tangent by construction.

### 2a. curve_tools + driven_offset

| # | Where | Mechanism | Risk | Note |
|---|---|---|---|---|
| 1 | `driven_edge_offset.fs:1271-1281` | `classifyPoints` -> `emitLineCurve`/`emitArcCurve`; spline branch uses true `runEndTangent` | KINK | The measured case. Gates are source-type only. |
| 2 | same, line branch | chord when sagitta <= tol | KINK | Varying offset on a LINE source bends the run; chord tangent off by ~4*sagitta/L. |
| 3 | `driven_offset_surface.fs:734` -> `emitShape` ~2559 | same `classifyPoints` per loft section | KINK | |
| 4 | `edge_offset_utils.fs:2523, 2594` (`alignedRunMerges`/`startGroup`) | merge test: span still one line/arc | KINK | Longer merged spans, tangent drift larger. |
| 5 | `driven_offset_surface.fs:1269` -> `curveThrough` | seed curve on source stations | SAFE (practically) | Points on the source itself. |
| 6 | `offset_run_treatment.fs:153` -> `cornerArc` `edge_offset_utils.fs:1865` | arc about the corner vertex through p1/p2 | KINK (conditional) | Tangent only if w' = h' = 0 at the corner. With height offset h the arc plane tilts: normal.T1 ~ w*h*(1 - T1.T2) != 0. Co-radial check (1872) does not catch it. |
| 7 | `offset_run_treatment.fs:159` -> `arcLikeSpline` | cubic from t1/t2 | SAFE | |
| 8 | `map_curve.fs:936-947` | `classifyPoints` with NO gates | KINK (highest) | A mapped spline can become an arc or line. |
| 9 | `evaluate_offset.fs:884-893` | line if sagitta <= tol; splines fitted with undefined end tangents | UNCLEAR | Possibly G0 by design. |
| 10 | `evaluate_profiles.fs:1695-1714`, `onLine` ~1795, `onArc` ~1860, `arcThroughPoints` ~1895 | own greedy 3-point circle / line copy | KINK | Duplicate of the curve_core logic. |
| 11 | `evaluate_profiles.fs:738-769` (`mergeShortEdges`) | rebuilds neighbours to a moved vertex | KINK (small, likely intended) | |
| 12 | `unwrap.fs:1197-1216` | `classifyPoints` + `shapeEndTangents` check vs `G1_JUNCTION_ANGLE` | SAFE-ish | Only path with a tangent check, but 0.57 deg is too loose to catch 0.023 deg. |
| 13 | `unwrap_part.fs:1196-1199` | `classifyPoints` at 1e-7 m | KINK (negligible) | |
| 14 | `fillet_wire.fs:470-529`, `sketchArc` 762 | solved tangent arc | SAFE | |
| 15 | `clean_wire.fs:1747-1812` | exact copies; fits use source tangents | SAFE | |
| 16 | `clean_wire.fs:1246-1251` vs `forceTangency` 1387 | joints touching an exact line/arc always break; `forceTangency` never repairs them | KINK (pre-existing kink kept) | `tangencyFixes` (1889) does not count them. |
| 17 | `clean_wire.fs:1751-1769` (plan view slivers) | curved sliver -> chord | KINK (small) | |
| - | `merge_curve.fs:132-162` | `snapSplineEnds` moves end CP | UNCLEAR (minor) | |
| - | `create_offset_profile.fs:1031` | exact Bernstein pieces | N/A | |

Callers: `classifyPoints` -> rows 1, 3, 4, 5, 8, 9, 12, 13. `emitArcCurve` (`curve_core.fs:1137`)
-> 1, 3, 6, 8, 12, 13. `cornerArc` -> offset_run_treatment only.

### 2b. Other areas

| Where | Mechanism | Risk | Note |
|---|---|---|---|
| `footprint/arcFit.fs:868-896, 933` (`detectWholeCurveArcOrLine`) | per-edge whole-curve fast path: chord or 3-point circle | KINK | Marked `preserved`, so subdivide/merge never revisit it. |
| `footprint/arcFit.fs:1086-1106` (`fitLineOrArcForSegment` step 1) | line test BEFORE the biarc | KINK | Merges (1407) can also promote a union to a line. |
| `footprint/arcFit.fs:1108-1144` | Bolton biarc k=1 (`constructBiarc2D/3D` 1889-2061) | SAFE | |
| `footprint/arcFit.fs:1148-1206` | degenerate -> line; 3-point circle fallback | KINK | Only when the biarc fails. |
| `footprint/scaleFootprint.fs:1688-1932` (emit 2060) | 1-DOF analytic G1 arc chain; chain ends least-squares (`endpointWeight`=8, ~414) | KINK at chain ends | `startPhi`/`endPhi` returned but never used; comment at ~1676 says splines "absorb" the residual -- no code does. |
| `footprint/scaleFootprint.fs:2004-2011` (strict) | through arcFit | KINK | Inherits arcFit. |
| `footprint/integrateFootprint.fs:471` + `fpt_geometry.fs:549-557` | exact arcs from the constant-R ODE step | SAFE | Shares the ODE state with the neighbouring spline. |
| `footprint/integrateFootprint.fs:437` | FIT transitions via `opFitSpline` with no derivatives | KINK | Transition tangent not pinned to the arc. |
| `example_1/refSurfCreation/offsetEdges.fs:2164-2177` | constant-offset source arc, concentric | SAFE | O(h^2) finite-difference tangent on neighbours. |
| `example_1/refSurfCreation/offsetEdges.fs:2180-2207` | tangent-constrained biarc (`biarcWithRatio` 1951, ratio optimised 2039) | SAFE | **Reuse this.** |
| `curveMapping/curveMappingCore.fs:264-294, 1609-1655` (`alignIsolatedLineFrames`) | reference edge with CP deviation < 0.1% of length promoted to line, frame uses chord | KINK | Frame jumps up to ~0.23 deg at the boundary; mapped curves kink. `offsetEdges.frameAtArc` (632-647) handles this, curveMapping does not. Same in `curveMapping_public/`. |
| `curveMapping/curveMappingCore.fs:1511-1576` linear fast path | rigid transform | SAFE if the references really are straight | Also trusts the promoted near-lines above. |
| `reference_side/offset_plus.fs:770-800` | rolling-ball corner arc | SAFE planar / UNCLEAR 3D | |

Note: "Source arcs: Keep as arcs / Convert to splines" exists only in
`example_1/refSurfCreation/offsetEdges.fs`, not in `curveMapping/`.

## 3. Corner (G0) preservation

| Feature | Rule | Verdict |
|---|---|---|
| Driven edge offset / surface | `buildRuns` (`driven_edge_offset.fs:823-832`): fit through a source-edge boundary only if `welded` and neither side is a line/circle. `welded` = gap <= 1 um AND angle <= `G1_JUNCTION_ANGLE` 0.01 rad (`curve_core.fs:91, 942-944`), not user-visible. SOURCE_EDGES mode breaks at every edge. | DISCONTINUITIES: corners <= 0.57 deg smoothed (within approx tol). Otherwise preserved. |
| Clean wire AUTO | <= 0.57 deg merged; up to `cornerAngle` (3 deg) merged when `forceTangency`; sliver rule | Corners < 3 deg smoothed (fit cuts the corner) |
| Clean wire MANUAL | every joint breaks, except INSIDE a group (`clean_wire.fs:1511-1527`): all interior joints fitted through; > 3 deg gets an info notice, 0.57-3 deg silent. `cornerAngle`/`forceTangency` hidden and inert in MANUAL. | Outside groups preserved; inside groups ANY corner smoothed |
| Merge curve | one `approximateSpline` through the chain | Smoothed at any angle (by design) |
| Map curve | 0.57 deg weld | < 0.57 deg smoothed |
| Evaluate profiles "One curve" (default) | one spline per group | Smoothed |
| Evaluate profiles "Efficient" | `smoothAcross` (`evaluate_profiles.fs:1627-1630`) compares edge CHORD directions against `PROFILE_ALONG_COS` 0.7071 | **BUG**: freeform edges up to ~45 deg merged; docstring at ~1520 says "a G0 corner never merges". Should compare end tangents at the junction against the weld angle. Lines/arcs safe (`PROFILE_MERGE_COS`). |
| Create offset profile | exact pieces | Preserved |
| Evaluate offset | unwelded reference corners break runs | Reference corners preserved; a corner in the middle of a single TARGET edge is sampled through (likely smoothed) |
| wrapCurve / wrapAndLoft | to-path breaks > `CM_SPAN_MERGE_ANGLE` 0.5 deg; from-path corners only with "Break spans at curvature jumps and corners" (default off) | To-path < 0.5 deg smoothed; from-path unchecked by default |

Live case: Clean wire 1 is MANUAL, so its corner settings do nothing; the risk is corners inside
its groups. SW_Shelf_offset (DISCONTINUITIES) keeps every vertex sharper than 0.57 deg.

## 4. Decisions

Made by the user 2026-09-25:
- Arc source with a varying offset: user choice **Biarc fit / Spline**. No other options.
- G1 (curvature jump) between arcs is acceptable.
- Arc and line acceptance gets an end-tangent check.

Refined with the user (2026-09-25): the tolerance is a SNAP threshold, not an acceptance
tolerance.
- A joint within the threshold is DEFINED tangent: one shared tangent, and both sides are BUILT to
  it, so the emitted joint has zero kink by construction. Arc next to a spline: pin the spline's end
  derivative to the arc's tangent. Arc next to an arc (or an arc that cannot meet the shared
  tangent): tangent-constrained biarc. Re-check position deviation afterwards (reshaping is about
  angle x the length the correction is spread over; 0.1 deg ~ 0.0017 x that length); split or fall
  back to a spline if it exceeds the approximation tolerance.
- A joint above the threshold is a real corner and stays G0.
- Rejected alternative: "accept an arc whose own tangent is within X of the true one, emit it
  as is" -- that leaves up to X of kink in the output (measured kinks today: 0.001-0.023 deg).
- The same idea already exists for SOURCE joints (`G1_JUNCTION_ANGLE` 0.57 deg weld); it just
  does not carry through to emitted arcs/lines. Ideally one corner threshold covers both.

Open:
- **Corner threshold value.** User proposed 0.1 deg. Options: 0.1 deg everywhere (source joints
  between 0.1 and 0.57 deg then stay corners in the offset instead of being welded), keep 0.57 deg,
  or 0.1 deg for emitted pieces only. User to decide.
- Should Clean wire groups break at corners > 0.57 deg instead of fitting through?
- Should `G1_JUNCTION_ANGLE` (0.57 deg weld) stay as is? It is a separate question from the
  emission check: it decides what counts as a smooth SOURCE joint.

## 5. Proposed design

1. `classifyPoints` gains optional true start/end tangents (new overload; keep the old
   signatures). Joints within the corner threshold get one shared tangent (section 4), and
   every emitted piece is built to it: arcs via the tangent-constrained biarc (a single arc only
   when it already meets both shared tangents to numerical precision), lines only when both
   tangents are the chord direction, spline ends pinned to the shared tangent. Rejection reason
   carried in the returned map, as today.
2. Port `biarcWithRatio` + helpers from `offsetEdges.fs:1867-2101` into `curve_tools/curve_core.fs`.
   Add a recursive split: one arc -> biarc -> split at mid-arc-length and biarc each half, up to a
   cap; give up to a spline at the cap. Split point choice: max deviation, not fixed midpoint, if
   cheap.
3. Driven edge offset / surface: new enum parameter (default reproduces today's output, see
   correction 25 -- an annotation default is migrated into saved features), e.g.
   "Varying offset on arcs: Spline / Biarc fit". Constant-offset arc runs stay exact arcs.
   `sourceShapeGates` or its caller should know whether the offset is constant over the run.
4. Apply the tangent-checked `classifyPoints` to map_curve (add the source-type gates too),
   evaluate_profiles (delete its private copy, call curve_core), unwrap (replace the 0.57 deg
   limit).
5. arcFit: run the biarc before the line test, or accept a line only when both true tangents are
   within the limit of the chord; same check on the whole-curve fast path and the 3-point
   fallback. scaleFootprint: re-pin the neighbouring splines' end derivatives to
   `startPhi`/`endPhi`. integrateFootprint FIT: pass derivatives to the transition fit.
6. `cornerArc`: check actual tangency (not just co-radial); if it fails, use `arcLikeSpline`
   or a biarc between the two true tangents.
7. curveMapping: frame at a promoted near-line edge must not jump (port `frameAtArc` idea).
8. Evaluate profiles `smoothAcross`: compare junction end tangents against the weld angle.

Order suggested: 1-4 (curve_core + its callers), then 8 (small, clear bug), then 5-7.

## 6. Tests to add (in the feature tree, per project convention)

- A: constant offset on an arc chain -> all exact arcs, radii = R - w, joints 0 deg (no change).
- B: this document's case (arc chain + varying profile, 622 mm arc) with "Biarc fit" -> MRS run
  emitted as 2 arcs, every non-corner joint 0 deg (numerical precision), max deviation <= 0.01 mm.
- C: same with "Spline" -> today's output.
- D: varying offset on a LINE source -> no chord line with a kinked end; spline or exact line only
  when the offset is linear.
- E: corners: a 0.3 deg, 1 deg, 5 deg source corner through DEO DISCONTINUITIES (0.3 welded,
  others kept), Evaluate profiles Efficient (after fix: 1 and 5 deg kept).
- F: every emitted wire: measure every joint angle; joints below the corner threshold must be
  ~0 deg, joints above it must match the source corner (reusable checker in
  `devtools/onshape/`, same eval pattern as `check_*.py`).

Measurement scripts used for section 1 are simple eval-API snippets (one anonymous function,
std only): evaluate `evCurveDefinition` / `evEdgeTangentLines` per edge of
`qCreatedBy(makeId("FY8TcgdA1CkKahL_0"), EntityType.EDGE)`, 3-point circle residual, joint angles
by matching end points within 1 um.
