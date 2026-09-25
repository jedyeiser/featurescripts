# Track report: Curve & surface tools (gordonSurface doc)

Scope: Scaled Curve, Pull surface, Modify curve end (MCE), and the gordonSurface helper files. Everything in this review was read-only; nothing was edited, pushed or posted.

## 1. Bottom line

- **None of the three features is production quality, and in each one some parameters are silently ignored.** Scaled Curve ignores its degree and its Group 1 fit parameters, and by default it creates nothing. Pull surface has a G2 mode that nothing reads, and its G0/G1 hold only at grid nodes. MCE's "Exact" G2 does nothing, its FRENET frame gives the same result as WORLD, the end reference is still applied when its checkbox is off, and "Flip ref" is never read. I confirmed each of these in the code myself.
- **About two-thirds of the gordonSurface curve code duplicates something else or can be replaced by native calls.** The join/order code (`scaledCurve.fs:308-560`) can be replaced by native `constructPath` / `evPathTangentLines`, or by `curve_tools/merge_curve.fs:125/172`. The printing helpers are exact copies of `tools/printing.fs`. The files `curveOps.fs` and `continuityTools.fs` import things but re-export nothing, so they do nothing.
- **MCE can get all three requested capabilities without refitting.** Hold points and reference tangency/curvature can be exact: insert a knot at the hold point, lock the control points on the held side, blend the remaining control points, then set the end control points from a closed-form formula. The held part of the curve stays identical (to about 1e-15 m), and the end tangent and curvature match exactly. Wire input goes through `expandEdgeQuery` + `constructPaths`.
- **Every variable_tools adoption here needs FeatureScript 2892 -> 3070.** `extract_outputs.fs`, `curve_core.fs` and `merge_curve.fs` are all on 3070; gordonSurface files are on 2892.
- **Out of scope, but a real bug:** the G2 "jostle" formula in `curveMapping/curveMappingCore.fs:2022/2055` and in memory `g2-jostle-formulas.md` is wrong whenever knot spacing is uneven. The curvature it achieves is the target multiplied by dl2/dl1; on knots `[0,0,0,0,0.1,1,1,1,1]` that is 9 times the target. I re-derived the correct formula from Piegl & Tiller and confirmed the bug. This should go to the curveMapping owner.

## 2. Per-feature verdicts

| Feature | State | Biggest problems (all confirmed in code) | Effort |
|---|---|---|---|
| Scaled Curve (`scaledCurve.fs`, 684 lines) | Prototype | Group 1 is joined with Group 0's sample count and tolerance (`:124`). Degree is hard-coded to 3 (`:287`, `:406`). "Create curve?" defaults to false (`:41`) and the returned map is thrown away, so the feature produces nothing by default. Arcs are sampled through `evApproximateBSplineCurve` without `forceNonRational` (`:120-121`), which gives wrong points on arcs. No automatic orientation, no closed curves, no tests. | M (rewrite the body in about 150 lines) |
| Pull surface (`pullSurface.fs`) | UI and manipulators work; the geometry does not deliver what it claims | Rows are refit and then skinned (`:629-632`), so G0 holds only at grid nodes. G1 is only applied through iso-U derivatives (`:86`), so the u=0/u=1 edges are not G1. `g2Mode` is declared at `:291` and never read. `isPointLocked` (`:36-44`) locks all 16 points on the default 4x4 grid under G1/G2, and nothing tells the user. Offset indices can land on the wrong grid point. Trimmed and periodic faces are not handled. | L (control-net rewrite) |
| Modify curve end (`modifyCurveEnd.fs`, 661 lines) | Unsound | EXACT G2 is a no-op (`:613-642`: the loop never changes `newCPs`). FRENET converts world to Frenet and straight back (`:156` then `:249-253`), so it equals WORLD. `useRef` (`:158`) ignores `showModContinuity`. `flipREf` appears only at `:83`. End edits overwrite each other when the curve has fewer than 6-7 control points. Projection (`:163`) undoes all end constraints. Reference edges are read through a B-spline that drops arc weights. `splineCP` is never used. | L (new capability) |
| Helpers | Mixed | `curveOps.fs` and `continuityTools.fs` have no effect (plain `import`). Unused imports: `scaledCurve.fs:7`, `gordonSurface.fs:11`, `gordonCurveCompat.fs:24`. `debugTools.fs` has about 2 KB of non-ASCII. `constEnums.fs` is fine as the document's enum hub. | S |

## 3. Prioritized recommendations

**P0: fixes that don't change the design (about 1 day total; no push without the user)**
1. **MCE correctness, before anything else.** Record fingerprints of existing instances first (§4.6, T01-T09), because some of these fixes change saved geometry.
   - Gate the reference on `definition.showModContinuity && !isQueryEmpty(...)`.
   - Make EXACT behave like BEST_EFFORT inside `enforceG2AtEnd`. Keep the function's signature, because `simplifySurface.fs:298` calls it.
   - Rename `flipREf` to `flipRef` and actually use it. This is safe: the old key was never read.
   - Pass `splineCP` as `maxControlPoints`.
   - Add `UIHint.SHOW_LABEL` to the enum parameters, fix the `"Definition"` typo at `:117`, and remove the em-dashes.
   - Effort: 2-3 h.
2. **Scaled Curve.**
   - `:124`: use the Group 1 parameters.
   - `:287`: use `degree`.
   - Add `"forceNonRational": true` at `:120-121`.
   - Always create the curve; drop the toggle.
   - Fix the typos at `:25`, `:68`, `:104`.
   - Add SHOW_LABEL.
   - Delete the duplicate printers at `:562-685`.
   - Effort: 1-2 h.
3. **Pull surface.**
   - Editing logic: remove offset items whose u/v is out of range, and merge duplicates.
   - Post a `reportFeatureInfo` when no grid point is free ("G1 needs at least 5 U and V curves, G2 at least 7").
   - Clamp the fit degree to count-1.
   - Remove `g2Mode` and the shim import.
   - Correct the header and the Feature Type Description so they stop claiming control-point editing with preserved continuity.
   - Add braces, ASCII, Descriptions and a defaults map.
   - Effort: about half a day.
4. **Helpers.** Delete `curveOps.fs` and `continuityTools.fs` and their 5 import sites. Remove the 3 unused imports. Effort: 30 min, and the document has to be re-pinned afterwards.
5. **Outside this track.** Fix `curveMappingCore.fs:2022/2055` and the memory file `g2-jostle-formulas.md`.
   - Correct start formula: `P2 = P1 + (dl1+dl2)*(d2t*dl1/(p(p-1)) + (P1-P0)/dl1)`.
   - Mirror formula at the end.
   - Also flag that `tools/debug.fs` is a stale copy of `curve_operations.fs` from before the `splitCurve` fix, and it is still imported by `xSection/core/xSectDebug.fs:7`.

**P1: modernization (about 5-7 days)**
1. **Bump gordonSurface to FeatureScript 3070** (MCE, Scaled Curve, Pull surface, and anything that imports them). This unlocks curve_tools and extract_outputs. Effort: M, including checking behaviour before and after.
2. **MCE rework as designed in §4:** wire input, then references, then hold points. Effort: 3-4 days.
3. **Scaled Curve rewrite on native paths.**
   - For each group: `constructPath(..., {tolerance: 1e-5 m})`, then sample with `evPathTangentLines` at equal arc-length fractions.
   - Orient automatically; keep Flip as an override.
   - Closed curves: require both groups to be closed or both open.
   - Fit with the user's degree and chord-length parameters.
   - Replace the 4 input-fit parameters with one "Sample spacing" parameter.
   - Add a deviation check reported with `reportFeatureInfo`.
   - Migrate `interiorCurves.fs:158-161` off `joinCurveSegments`, then delete `joinCurveSegments`, `orderCurveSegments` and `countConnections`.
   - Effort: 1 day.
4. **Pull surface control-net rewrite.**
   - Source surface from `evApproximateBSplineSurface`. Do not use `forceNonRational`; per `simplifySurface.md` it causes C0 kinks.
   - Lock 1/2/3 control-point rows at each edge for G0/G1/G2 (only in non-periodic directions), and keep the knots.
   - Move the free rows by a scalar B-spline displacement field that interpolates the existing handle grid, along the normal at the Greville points.
   - Pass `boundaryBSplineCurves` for trimmed faces.
   - Result: G0/G1/G2 exact along whole edges, while the existing parameters and manipulators stay as they are.
   - Effort: 2-3 days. Open question: should it sit behind a legacy Method enum?
5. **Tests, icons and embeds** (see §4.6, §5, §6).

**P2**
- Pull surface: a "Replace face" option (annotation default false), a "one handle per control point" mode, and poles/periodic faces.
- MCE: a TRANSPORT offset frame using `curve_core.transportedNormals`; add a SMOOTHERSTEP transition to `tools/transition_functions.fs`; fix the LOGISTIC doc, which claims derivatives vanish at the ends (they don't: f'(0)=0.067).
- Merge `getKnotsToInsert` (gordonCurveCompat) with `gordonSurface.fs:1434/1459` and `bspline_knots.mergeKnotVectors`.
- Move `projectCurveOnSurface` into a shared library.
- Replace Scaled Curve's [-0.5, 0.5] scale-factor parameters with a 0-1 "Position" parameter, migrating saved instances through editing logic.

## 4. Design: Modify curve end new capabilities

### 4.0 Ground rules
- The header moves to FeatureScript 3070.
- New parameters use defaults that reproduce today's behaviour (correction 25).
- New enums are defined locally in `modifyCurveEnd.fs`. Editing `constEnums.fs` would force a re-pin cascade.
- Keys removed: `createCurve`, `showContinuity`, `flipREf`, and `g2Mode` (from the MCE UI only; the G2 method is now closed-form).
- Keys kept: `selEdges`, `fromPoint`, `toPoint`, `offsetFrame`, `transitionType`, `fixedEndContinuity`, `showModContinuity`, `modContinuityRef`, `modEndContinuity`, `curveOnSurface`, `projectionFace`, `splineDegree`, `splineTol`, `splineCP`, `samplingMultiple`, and the print parameters.
- The editing logic function is renamed from the generic `editingLogic` to `modCurveEndEditingLogic`.

```
export enum HoldMode { annotation { "Name" : "Point" } POINT, annotation { "Name" : "Distance" } DISTANCE }
export enum EndCurvatureMode { annotation { "Name" : "Match reference" } MATCH, annotation { "Name" : "Zero" } ZERO, annotation { "Name" : "Radius" } RADIUS }
export const HOLD_DISTANCE_BOUNDS = { (millimeter) : [1e-3, 50, 1e6] } as LengthBoundSpec;
export const END_RADIUS_BOUNDS    = { (millimeter) : [1e-2, 100, 1e6] } as LengthBoundSpec;
```

### 4.1 Wires as input

**Parameters**
- `selEdges`: filter `(EntityType.EDGE || BodyType.WIRE) && ConstructionObject.NO`.
- `fromPoint` (label "Modified end") and `toPoint`: filter `EntityType.VERTEX || BodyType.MATE_CONNECTOR`, with implicit mate connectors allowed so the user can place a free point.
- An empty `toPoint` means zero offset, so the feature only edits tangency/curvature.

**Resolving the chain**
- `edges = expandEdgeQuery(sel)` (`curve_core.fs:143`). Drop zero-length edges and post an info notice.
- `constructPaths(context, edges, {})`, with these checks:
  - More than one path: regenError "The selection forms N separate chains. Select one chain."
  - Branching: one catch-and-rethrow with the message "Edges branch at a vertex; select one open chain." This is the only try/catch; it exists only to give a better message.
  - `path.closed`: regenError. Closed chains are rejected in version 1.
- Working curve:
  - One edge: `evApproximateBSplineCurve`. If the result is rational (an arc), use the `forceNonRational` fit.
  - Two or more edges: `mergedCurveThroughPath(context, path, {degree, tolerance: splineTol, maxControlPoints: splineCP})` (`merge_curve.fs:125`).
- Normalize the knots to [0,1].
- New edge case (not raised by either expert): a single line edge comes back as degree 1 with 2 control points. Elevate it to `max(splineDegree, 3)` with `tools/bspline_knots.elevateDegree` before editing.

**Which end is modified**
- The modified end is the path end nearest `fromPoint`. If `fromPoint` is empty, it is the path end, with an info notice.
- If both ends are within 1% of the length of being equally close: regenError "ambiguous; pick the end vertex".
- If the pick is more than `splineTol` from the end: info "X mm away".
- Orientation: reverse the curve so the modified end is always u=b, and reverse the result back afterwards.

### 4.2 Tangency/curvature equal to a reference
The reference type comes from what is picked, so no reference-type enum is needed.

**Parameters** (inside the existing `showModContinuity` group):
- `modContinuityRef`: filter `EDGE || FACE || BodyType.MATE_CONNECTOR`.
- `flipRef`: "Opposite direction", `UIHint.OPPOSITE_DIRECTION`.
- `modEndContinuity`: "Match", G0/G1/G2, SHOW_LABEL.
- When Match = G2: `modCurvatureMode` (EndCurvatureMode, default MATCH); when it is RADIUS, `modEndRadius`.

**Target tangent T and curvature vector K from the reference** (all native calls):
- **Edge:** `t = evDistance(edge, newEnd).sides[0].parameter`, then `evEdgeCurvature(context, {edge, parameter: t})`. T = `frame.zAxis`, K = `curvature * frame.xAxis`. A line gives K = 0.
  - This replaces `computeEdgeContinuityConstraints`, which mixes arc-length and B-spline parameters and drops rational weights.
  - Info notice if the reference passes more than `splineTol` from the new end.
- **Mate connector:** T = Z axis, K = 0. With RADIUS, X sets which side the curvature bends toward.
- **Face:** T = the approach direction projected into the tangent plane (existing code). Only the normal part of K is set, K·n = II(T,T); the geodesic part of the current curve is kept.
- **Curvature mode:** ZERO gives K = 0. RADIUS gives |K| = 1/r, on a side taken from the mate connector's X axis, else the edge's normal, else the current curve's normal. If none of those exists (straight end, no mate connector): regenError "curvature side undefined; use a mate connector".
- **Sign:** automatically orient T along the curve's natural direction of approach, then apply `flipRef`. Post an info notice if the final T points against the approach direction (hook risk).
- **Position:** To point wins. With an empty To point and Match >= G0, the end snaps to the nearest point on the reference.

**Math.** For a clamped, non-rational curve of degree p, with knot gaps read after all knot insertions:

    dl1 = u_{p+1}-u_p,   D2 = u_{p+2}-u_p        (at the end: el1, E2, mirrored)
    C'(a) = (p/dl1)(P1-P0)
    C''(a) = p(p-1)/dl1 [ (P2-P1)/D2 - (P1-P0)/dl1 ]
    K = (p-1)/p * (dl1/D2) * h/a^2,   where a = |P1-P0| and h = the part of P2-P1 perpendicular to T

Closed-form end overwrite (modified end is u=b):

    P_{N-2} = P_{N-1} - a*T
    P_{N-3} = P_{N-2} - beta*T + (p/(p-1))*(E2/el1)*a^2*K     (first remove any tangential part: K -= (K·T)T)

- a = the current |P_{N-1} - P_{N-2}| (the KEEP rule).
- beta = the tangential part of the old P_{N-3} - P_{N-2}, clamped to [0.25, 4] times a*E2/el1.
- If |P_{N-3} change| is more than 2 times the old spacing, reduce a and post an info notice.
- This is exact in one step, handles K = 0 exactly (collinear control points), matches the direction of the normal (not just its magnitude), and works for p >= 2. It replaces `enforceG1AtEnd`, `enforceG2AtEnd` and `G2Mode` inside MCE. Those functions stay, bug-fixed, for `simplifySurface`.

### 4.3 Hold points
**Parameters** (top level, after To point):
- `useHold` (default false).
- `holdMode` (POINT/DISTANCE, SHOW_LABEL + HORIZONTAL_ENUM).
- `holdPoint` (vertex or mate connector, projected onto the curve), or `holdDistance` (arc length measured from the modified end; works with #variables).
- `fixedEndContinuity` is relabelled "Continuity at fixed end / hold point" (same key).

**Math: knot insertion plus a control-point blend, with no refit.**
1. Hold parameter: `t_h = projectPointOnCurve(...)` (`tools/point_projection.fs:102`). For DISTANCE, use `parameterAtArcLength(L - d)` (`arc_length.fs:273`). No hold means t_h = a, so the same code path also handles the fixed end.
2. Insert t_h until its multiplicity is p, using `refineKnotVector` (`bspline_knots.fs:273`) and `getKnotMultiplicity`. Let j = k - p, where k is the last knot index equal to t_h. Then C(t_h) = P_j exactly, and C on [a, t_h] depends only on P_0..P_j.
3. Lock P_0..P_{j+k_h}, where k_h = 0/1/2 for G0/G1/G2. This gives C^{k_h} continuity at the hold (stronger than G^{k_h}) and zero change on the held side.
4. Enough free control points: M = N - j must satisfy M >= k_h + k_e + 3 (default 8; G2 at both ends needs at least 7). If it is too small, insert midpoints of the widest spans in (t_h, b). Knot insertion does not change the shape.
5. Weights: w_i = f(sigma_i), where sigma_i is the Greville abscissa of P_i mapped onto [t_h, b] by arc length and f is `evaluateTransition`. Force w_i = 0 for locked indices and w_{N-1} = 1. Then P_i += w_i*D, with D = To - end. Because B-spline basis functions sum to one, the displacement along the curve is W(u)*D; monotone weights give monotone displacement with no overshoot. In a frame mode, each D is rotated by a rotation-minimizing frame, with D_loc computed so the end still lands exactly on To.
6. Apply the end overwrite from §4.2. The DOF rule in step 4 guarantees these control points do not overlap the locked ones.
7. Optional: `removeKnot(t_h, k_h, 1e-10 m)` to take the hold knot back down to multiplicity p-k_h.

**Guarantees:** the held region is unchanged to about 1e-15 m, To is hit exactly, and T and K at the end are exact. For multi-edge input, "held region unchanged" is measured against the merged curve, which itself sits within `splineTol` of the source edges.

**Rejected alternatives:**
- A sampled weight followed by a full refit (the current approach): the held region moves by up to the fit tolerance.
- Split + refit + join: this relies on `curve_operations.splitCurve`/`joinCurves`, which have suspected multiplicity and knot-count defects (unverified).

**Edge cases:**
- Hold at the modified end: regenError.
- d >= L - tol: info, then run as if there were no hold.
- d > L + tol: regenError.
- Hold point off the curve: info.
- Hold on an existing knot: handled by the multiplicity logic.
- G0 hold: this is a real corner. Recommend a G1 default and an info notice for G0. Open question: does the kernel accept a C0 knot?

### 4.4 Order of operations and how it combines with existing modes
1. Prepare: reject closed curves, remove weights, elevate degree, normalize the domain.
2. Orient so the modified end is at b.
3. Insert the hold knot and refine for DOF.
4. Weights and displacement (WORLD, or frame).
5. End reference overwrite.
6. Optional knot removal.
7. Optional projection.

How the modes combine:
- **Transitions** run over [t_h, b]. Continuity at the hold comes from the locks, not from the transition's own end slope.
- **FRENET frame:** user decision (§7). The recommendation is to keep it as it is and document it as constant offset, and add TRANSPORT in P2.
- **Hold and fixed-end continuity:** with a hold set, the fixed end is inside the held region, so `fixedEndContinuity` applies at the hold instead.
- **Projection** (the only step that refits):
  - Sample only [t_h, b], project with `evDistance`, and fit with `approximateSpline`, using start derivatives from the held curve at t_h.
  - Re-apply end G1 only if T lies in the face's tangent plane (within 1e-3 rad).
  - End G2 generally cannot hold on a surface. Skip it and post an info notice with the measured error.
  - Never project the held region.

### 4.5 Messages
- regenError: closed chain, separate chains, branch, ambiguous end, "G2 needs degree >= 2" (on `splineDegree`), hold problems.
- reportFeatureInfo: snapped end, reference far from the end, projected tangent, mate connector treated as zero curvature, possible loop or cusp near the modified end (detected when consecutive C' samples have dot product <= 0), "Nothing to change: the curve is copied".

### 4.6 Tests
Tests go in a Part Studio named "Modify curve end tests", checked by `devtools/onshape/check_modify_curve_end_tests.py`, following the pattern of `check_move_along_edge_tests.py`.

**Fixtures:** S1 (200 mm S-shaped sketch spline), W3 (line + arc + spline composite), L1 (closed loop), an arc R25, a line, MC1 (mate connector), F1 (curved sheet face).

**Legacy cases.** Fingerprint these before the refactor:
- T01 world G0
- T02 frenet, equal to T01
- T03 fixed G1
- T04 fixed G2 best effort
- T05 G2 exact (fails today)
- T06 reference line G1
- T07 reference face G1
- T08 project
- T09 reference stored but unchecked, so no reference applied (fails today)

**Reference cases:**
- T10 arc G2: tangent and curvature vector equal the arc's (kappa 40/m, tolerance 1e-4)
- T11 mate connector G1
- T12 mate connector with flip
- T13 mate connector G2 radius 50 toward X
- T14 line G2 zero: collinear control points
- T15 no To point, reference G1
- T16 no To point, reference G0: end snaps to the reference
- T17 far reference: info notice

**Wire cases:**
- T20 W3 as a wire
- T21 unordered edges, same result as T20
- T22 no From point: info notice
- T23 closed chain: error (temporary instance)
- T24 two chains: error (temporary instance)

**Hold cases:**
- T30 hold mid G1
- T31 hold mid G2
- T32 W3 with the hold on edge 1
- T33 hold distance 60
- T34 hold at a knot
- T35 hold at the fixed end: info notice
- T36 hold at the modified end: error
- T37 hold distance beyond the length: error
- T38 hold + arc G2 + project
- T39 transport frame (P2)

**Output case:** T40 `modifiedEnd` resolves to exactly 1 vertex at To.

**Unit math cases** (devtools evaluation, from the math expert):
- Bezier: kappa = 10/m.
- Knot factor, knots `[0,0,0,0,0.1,1,…]`: expect h = 15 mm. The old formula gives 135 mm, so this doubles as a regression test for the curveMapping bug.
- Hold exactness: held region deviation < 1e-9 m.
- Overlap: a 4-control-point curve is refined to at least 7.

**Checks the script makes:**
- End positions < 1 um.
- Tangent angle < 1e-4 rad, using `evEdgeCurvature` on the created edge.
- |dk|/k < 1e-3, and normal angle < 1e-3 rad.
- Held region Hausdorff distance < 1 um (single edge) or < `splineTol` (multi-edge).
- Maximum curvature < 10 times the source maximum.
- Feature status and notice text.

**Order of work:** fingerprints, then the P0 fixes, then the 3070 bump with wire input and the embed, then references, then holds, then the icon.

## 5. variable_tools keys

| Feature | Keys | Reason |
|---|---|---|
| MCE | Standard keys, plus **`modifiedEnd`** (vertex at the new end, `DebugColor.BLUE`) | Downstream consumers (Bridging curve, another MCE, Extract variables' bridging input) need this specific end. Its position moves with To, and `outputVertices` returns both ends, so it can't be recovered robustly otherwise. `inputs = qUnion([selEdges, fromPoint, toPoint, modContinuityRef, holdPoint])`. |
| Scaled Curve | **Standard keys only.** `output = qCreatedBy(id + "curve", BODY)`, `inputs = qUnion([group0, group1, projectionFace])`. | The expert proposed `startVertex`/`endVertex`, but nobody consumes them today, and they are just the blended ends of the group ends. Add them only if a consumer appears (see §7). |
| Pull surface | **Standard keys only**, with `output` set explicitly to the pulled sheet (or the modified body in Replace mode). | Without an explicit output, the default `qCreatedBy` would also pick up the keepPoints points and the debug wires. The iso curves, handles and offsets have no consumers. |

All three need the 3070 bump and a pin to `extract_outputs`.

## 6. Icons

A feature counts as having an icon if its annotation has `"Icon" : ...BLOB_DATA` with an IconNamespace import.

- **Scaled Curve: none.** No `Icon` key and no entry in `icon_targets.py`.
  - Concept: two #999 guide curves; a #333 blended curve that leans from the lower guide toward the upper one; blue #1651B0 ticks from the lower guide up to the blend, getting longer left to right (a "factor ramp").
  - Target entry: `("gordonSurface", "scaledCurve", "scaled_curve")`.
- **Pull surface: none.**
  - Concept: a perspective sheet with a #333 fixed boundary, #999 iso grid, a raised bump, and a blue manipulator arrow and dot. The expert drafted an SVG, based on `move-face-button.svg` and `isoparametric-curve-button.svg`.
  - Target entry: `("gordonSurface", "pullSurface", "pull_surface")`.
- **MCE: has a legacy icon** (`modifyCurveEnd.fs:22`, from before the current house style).
  - Concept for a restyle: the original curve in #999, the modified tail in #333, a blue arrow from the old end to the new end, and a blue tangent tick at the new end. Base: `edit-curve-button.svg`.
  - `install_icons.py` skips files that already have an IconNamespace import, so this one has to be swapped by hand, not through `TARGETS`.

## 7. Conflicts, unverified claims, open questions

**How I resolved disagreements between the experts**
1. **Hold method.** The integration designer proposed split + refit the tail + exact join. The math designer proposed knot insertion + control-point blend. **I chose the math designer's approach:** it keeps one curve, needs no refit, and avoids the suspect `splitCurve`/`joinCurves`.
2. **How to enforce the end conditions.** Constrained `approximateSpline` derivatives versus the closed-form control-point overwrite. **I chose closed-form.** The derivative approach is used only in the projection path, which has to refit anyway.
3. **G2Mode.** It is removed from the MCE UI. `enforceG2AtEnd` stays for simplifySurface, with EXACT routed to BEST_EFFORT.
4. **Multi-edge join.** Exact concatenation of the edge B-splines (math designer) versus `mergedCurveThroughPath` (integration designer). **Use `mergedCurveThroughPath` for now,** and document that "held region unchanged" is measured against the merged curve. Exact concatenation is a P2 item, and arcs need their weights removed anyway.
5. **Scaled Curve keys.** Reduced to the standard keys (see §5).

**Checks I made myself**
- Confirmed in code: every high finding for Scaled Curve (`:124`, `:287`, the create-curve default, missing `forceNonRational`), for MCE (EXACT no-op, FRENET round trip, reference gate, `flipREf`), and for Pull surface (`g2Mode` unused, locking rule, refit + skin at `:629-632`).
- Confirmed by my own derivation: the curveMapping knot-factor bug, `C''(a) = p(p-1)/dl1[(P2-P1)/(dl1+dl2) - (P1-P0)/dl1]`. The code scales the achieved curvature by dl2/dl1.
- **Downgraded:** the math expert said the memory formula "gives 6(P2-P1)" on a Bezier split. On those knots dl2 = 0 and the code skips the formula entirely, so that particular check is moot. The bug is still real for any interior knot spacing with dl1 ≠ dl2.

**Unverified (need a read-only eval)**
- The knot domain `evApproximateBSplineCurve` returns for lines, arcs and splines.
- Whether a 2892 module can import a 3070 module. Assumed not.
- Whether `evEdgeCurvature`'s `xAxis` points toward the centre of curvature.
- Whether `opCreateBSplineCurve` accepts a multiplicity-p interior knot, or a G0 corner.
- Whether `splitCurve` produces unclamped pieces.
- Whether Onshape flags the duplicate `printBSpline` overloads.
- MCE's `"Default": 4` on an integer parameter, which may be ignored in favour of the bound's default.

**Open questions for the user**
1. **MCE FRENET:** fix it to rotate with the curve (this changes every saved default instance), or keep it as constant offset and add TRANSPORT? My recommendation: keep it and add TRANSPORT.
2. **Pull surface control-net rewrite:** replace the geometry for saved instances, or put it behind a Method enum that defaults to legacy? And should Replace face be the default for new instances?
3. **FeatureScript bump:** OK to move gordonSurface from 2892 to 3070? CLAUDE.md marks gordonSurface as archived, but these features are in heavy use. Should the document move back to active maintenance, including test Part Studios there?
4. **Saved instances whose geometry would change:** do any exist with a stored reference and the checkbox off, with G2 Exact, or with Scaled Curve's sf/sample parameters?
5. **Where should the shared path-sampling and end-condition helpers live:** gordonSurface, `tools/`, or `curve_tools/curve_core`?

### Critical Files for Implementation
- C:/Users/jed.yeiser/documents/featurescripts/gordonSurface/modifyCurveEnd.fs
- C:/Users/jed.yeiser/documents/featurescripts/gordonSurface/scaledCurve.fs
- C:/Users/jed.yeiser/documents/featurescripts/gordonSurface/pullSurface.fs
- C:/Users/jed.yeiser/documents/featurescripts/tools/bspline_knots.fs
- C:/Users/jed.yeiser/documents/featurescripts/curve_tools/merge_curve.fs
- C:/Users/jed.yeiser/documents/featurescripts/variable_tools/extract_outputs.fs
- C:/Users/jed.yeiser/documents/featurescripts/curveMapping/curveMappingCore.fs