# Clean_Wire -- research and design note (2026-09-19)

Synthesis of four read-only research passes (std API survey, in-repo reuse inventory,
algorithm design, feature/UI conventions) plus a read of the user's manual
`Wire_Smoothing` folder in `Design_Master`. Nothing here is implemented yet. The file to
create is `clean_wire.fs` in the `driven_offset` document.

---

## 1. What the feature replaces: the manual recipe

The `Wire_Smoothing` folder (features 105-119 on 2026-09-19) turned the 32-edge
`Pinch_intersection_Curve` wire (0.09-0.11 mm slivers, 0.5 deg-class joints, 4.8 deg
pitch kinks at the ramp ends, notch and slot corners) into an 11-edge composite wire:

1. **Split** the wire at 7 chosen points (`splitPart`, implicit mate connectors ON_ENTITY /
   POINT on vertices) -> 9 wire pieces. The split points are the vertices the user wanted
   to KEEP as vertices: the notch corners (45 deg), the slot corners (90 deg), the ramp
   ends at 332.4 / 392.4 mm (4.8 deg design creases), and two G1 joints at 1255.8 /
   1359.6 mm split purely to control the control-point budget per region.
2. **Edit curve -> Approximate** on 6 of the 9 pieces: degree 3, tolerance 0.01 mm, keep
   start and end derivative, and a per-piece **Maximum control points** of 8 / 20 / 15 /
   6 / 8 / 15. Every internal joint of a piece (slivers, near-tangent joints) is smoothed
   away by the fit; each piece becomes ONE spline.
3. Three pieces left untouched (the two notch lines, the slot end).
4. **Composite curve** (or our JoinWires -- identical: both are `dissolveWires` +
   `opExtractWires`) over the 9 pieces -> one 11-edge wire.

So the feature is, literally: *choose the break vertices (automatic corners plus any the
user adds), fit each stretch between breaks to one spline under a per-stretch override
with end derivatives kept, pass exact pieces through, join into one wire whose identity
is owned by this feature.*

Facts about the manual tools that shape the design (std sources, v2878 mirror):

- `Edit curve` forces `isRational = true` on EVERY output (`std/editCurve.fs:375-380`).
  That is where the "RATIONAL" splines came from; the weights are all 1.
- `Edit curve` / `Composite curve` "Approximate" = `constructPath` over all selected
  edges -> 200 uniform arc-length samples (`APPROXIMATION_SAMPLES`, `approximationUtils.fs:18`)
  -> one `approximateSpline` with unit end tangents and NO `parameters`
  -> `opEditCurve` replaces the wire in place. Consequences: no tangency check (a corner
  inside the selection is smoothed within tolerance, burning CPs); a 0.1 mm sliver on a
  long piece gets zero samples; derivative MAGNITUDES are ignored (correction 23); at the
  CP cap it silently under-fits ("Tolerance will not be satisfied if this limit is
  reached", no error). The 15-CP misses of 0.016-0.070 mm we measured in the wall came
  from exactly this.
- `opCreateBSplineCurve` refuses a C0 knot inside one spline (`BSPLINECURVE_NOT_G1`), so
  corners must be separate edges; `opExtractWires` is the only std way to assemble edges
  into one wire (fails on overlap / >2 edges at a vertex).
- `opSplineThroughEdges` merges a tangent-continuous chain natively but exposes no
  tolerance / degree / CP / derivative control and rejects non-tangent joints at an
  unexposed tolerance. Not usable as the engine; possibly useful as a cross-check.
- `evApproximateBSplineCurve(edge, forceNonRational, forceCubic, tolerance)` is the std
  rational -> non-rational converter for an EDGE. `evMaxPathDeviation(side1, side2,
  showDeviation)` is the std way to verify a result against the input.
- `ApproximationTarget` accepts `start2ndDerivative` / `end2ndDerivative`; with
  `parameters` given, first-derivative magnitude and the second derivative are honoured.
  Unused anywhere in the repo so far.

## 2. Acceptance test

Run on `Pinch_intersection_Curve` (config tail_notch / Slotted / Baseline) with the
same break vertices the user chose and the same per-piece overrides:

- 11 edges, same vertex set as `Composite curve 1`;
- per-piece control points <= the manual ones (8/20/15/6/8/15) at 0.01 mm, measured
  deviation <= 0.01 mm everywhere (not only at samples);
- exact lines / the slot end passed through unchanged;
- `Driven offset surface 1` (Cap_Wall) seeded on the output builds with the same 12 faces
  and no worse end curvature than the 2026-09-19 table;
- regenerating after an upstream edit that moves geometry by microns keeps every
  downstream reference (splits / mate connectors on the output's vertices) alive.

## 3. Pipeline (recommended)

```
1. Chain     order the wire's edges into a Path (constructPath, tol 1e-5 m -- merge_curve.fs
             chainEdges), orient every tangent along the chain; closed loops allowed.
2. Describe  per edge, batched: evCurveDefinition (type, isRational, weights),
             evEdgeTangentLines at [0,1], evLength, evEdgeCurvatures at ~32 stations.
             "Effectively non-rational" = isRational with max(w)-min(w) < 1e-9 max(w).
3. Slivers   length < sliverMax (default max(0.5 mm, 50 * tol)): classify by the tangents
             of the NEIGHBOURS across the sliver. Neighbours tangent -> absorb (its
             endpoints become ordinary samples); neighbours G0 -> keep it as its own
             exact piece (it is a chamfer/fillet), never extend curves to a computed
             intersection.
4. Joints    delta = angle(T_prev_end, T_next_start):
               delta <= 0.5 deg              G1     merge (weld threshold, 1e-2 rad today)
               0.5 < delta <= nearTangentMax near-G1 merge if "force tangency" on,
                                                     else corner   (default 5 deg)
               delta > nearTangentMax        G0     keep the vertex
             User-added break vertices are corners regardless (the manual splits at
             1255.8 / 1359.6 were budget splits on G1 joints).
5. Runs      cut the chain at every corner; closed loop with no corner -> one periodic
             spline (no end derivatives), with corners -> open at the sharpest corner.
6. Exact     a run whose edges are all LINE / CIRCLE (collinear / co-circular within tol)
             is emitted exactly (line; arc via sketch so it carries a radius). A run of
             spline edges is tested for arc/line with the ASYMMETRIC gate below.
7. Sample    curvature-adaptive: s(kappa) = sqrt(4 tol / kappa), clamped to
             [max(20 tol, 0.05 mm), min(chord/8, 10 mm)]; integrate the density along
             the run and place ceil(integral) samples at equal density increments,
             both run ends included -> every gap in [0.5, 1.5] spacings (the
             correction-23 requirement). Positions from one evEdgeTangentLines per edge.
8. Fit       approximateFamily (chord-length parameters, end tangents x total chord,
             fit tolerance tol/2, cap = the run's override or the global max). Optional
             end2ndDerivative = chord^2 * kappa * N where a run abuts a kept exact
             primitive (costs ~2 CPs per end).
9. Verify    evaluateSpline at the sample parameters AND at mid-sample parameters against
             kernel points at the same arc lengths; if max > tol, densify the offending
             spans and refit once; if still over, WARNING naming the run (never a silent
             under-fit). Optional "Optimize" mode: binary search on maxControlPoints per
             run with the measured error (simplifySurface's trick, ~8 fits per run).
10. Emit     one curve per run under id + ("run" ~ k) (ordinal from a deterministic
             chain start), endpoints snapped to the neighbour's kernel end point,
             opExtractWires over all new edges, delete the loose curves, name the wire,
             publish query variables (outputWire / outputEdges / sourceEdges).
```

Why sample-and-refit rather than exact merge + knot removal: rationals vanish for free,
the fitter relocates knots (knot removal can only delete), hand-edited inputs carry
every hand-placed knot, and P&T A5.8 with tolerance is O(n^2) interpreted work. Exact
merge (bspline_compat: elevate / joinChain / unifyKnots) + Lyche-Morken ranked knot
removal is the fallback for a run that will not reach tolerance under its cap, and the
only route for a "preserve exactly" mode. Not built for v1.

Arc/line gate for SPLINE sources (a false arc fabricates a radius downstream, a missed
one costs a few CPs): classifyPoints at min(tol, 1 um), planarity 1e-7 m, sagitta gate,
PLUS sweep >= 10 deg, PLUS circles through the first / middle / last thirds agree to
0.1 % in radius and tol in centre. Whole edge only, never sub-spans. This kills the
17.5 m / 42 mm false positive from 2026-09-15. Lines from splines: all samples within
tol of the chord AND both end tangents within 0.05 deg of it.

Forcing tangency at a near-tangent joint: merging across it (one fit) and averaging the
tangents then fitting both sides with the shared constraint give the same geometry
within tolerance; the boundary layer is s = 6.75 tol / delta_side with a curvature spike
4 delta / s. At 0.01 mm a 2 deg joint smooths over 3.9 mm with R = 56 mm; at 0.001 mm it
is 0.39 mm with R = 5.6 mm, a visible blip. Default: merge (one fit) for spline-spline
joints; average-and-constrain when one side is a kept exact primitive (never rotate a
line). Report every forced joint (delta, s, spike radius). Consider a `blendAllowance`
(default max(10 tol, 0.05 mm)) applied only inside that window.

## 4. Reuse map

Importable in the driven_offset document (no cross-document imports; `tools/` is NOT
importable -- copy if needed):

| need | reuse | where |
|---|---|---|
| edge/wire selection expansion | `expandEdgeQuery` | edge_offset_utils.fs |
| chain in path order, closed flag | `chainEdges`, `pathEndPoints` | merge_curve.fs (import `f531550fd26c49415f9c443a`) |
| per-edge description | pattern of `describeEdges` (private) -- reimplement, add isRational/weights | edge_offset_utils.fs:1071 |
| line/arc classification + gates | `classifyPoints` (3 arities), `sourceShapeGates`, `circleThrough` | edge_offset_utils.fs |
| fitter with chord parameters + cap warnings | `approximateFamily`, `emitFittedCurve`/`emitSplineCurve` | edge_offset_utils.fs |
| exact emission | `emitLineCurve`, `emitArcCurve` (sketch arc, real radius) | edge_offset_utils.fs |
| repeat culling, end clearance | `withoutRepeats`, `fitRepeatTolerance`, `runPointEntries` | edge_offset_utils.fs |
| corner helpers | `cornerArc`, `arcLikeSpline`, `lineApproach`, `segmentApproach` | edge_offset_utils.fs |
| exact non-rational algebra (fallback path) | `elevateToDegree`, `joinChain`, `unifyKnots`, `splitAtCreases`, `reverseBSpline` | bspline_compat.fs (`6b635e74c92bd23387e850c1`) |
| rational -> non-rational of an edge | `evApproximateBSplineCurve(forceNonRational)` | std |
| exact redundant-knot cleanup (homogeneous) | `removeKnots` | std/nurbsUtils.fs |
| output verification | `evMaxPathDeviation` | std |
| naming / publishing | `nameOutput` pattern, `embedVariableMap` + `extractableQuery` | driven_edge_offset.fs, design_map_query_utils.fs (`2b6b313ac740a0146d5bef7c`) |
| UI predicates and bounds | `drivenOffsetApproximationPredicate`, `DrivenOffsetMaxCPBounds`, `TOLERANCE_BOUND`, `DEGREE_BOUND` | edge_offset_utils.fs / std |

Needs generalisation: `approximateFamily` (add `isPeriodic` for closed loops and the
second derivatives); chain description (path order, not world-X order; `buildChain` needs a
zero point); joint classification (a second, wider threshold above `G1_JUNCTION_ANGLE`).

Missing entirely (v1 does without): tolerance-driven knot removal (std `removeKnots` is
exact-only; `tools/bspline_knots.fs removeKnot` blends weights non-homogeneously and
skips the even-case check), degree reduction, G2 enforcement at joins inside this
document (the closed-form CP formulas live in curveMapping; the native route is
`end2ndDerivative`), curvature-adaptive sampling, a "max distance new wire vs old wire"
helper beyond `evMaxPathDeviation`.

Do NOT reuse: `tools/curve_operations.fs joinCurves` (wrong seam knot multiplicity,
documented in research_merge_curve.md), `tools/debug.fs` (stale duplicate of
curve_operations, duplicate exports).

## 5. UI (draft, repo conventions) -- groups are the primary input

Decided 2026-09-19 with the user: no separate "Break at" picks. A GROUP is a contiguous
set of edges fitted as ONE spline under its own override; its two ends are breaks. That
is the manual recipe (split at the ends of a region, Edit-curve it with a budget)
expressed directly, and it lets critical and non-critical regions get different
approximations. Edges in no group are auto-classified (tangent stretches merged,
corners kept) and fitted with the global settings.

```
Wire                 Query  (EntityType.EDGE || BodyType.WIRE) && ConstructionObject.NO
Name                 string
Groups   array, "Item name" : "group", label "#cw_name"
   cw_edges   Query  EntityType.EDGE && ConstructionObject.NO   contiguous edges, one run
   cw_name    string
   cw_mode    enum CleanApproximation { NONE ("Global"), MAX_CP, TOL }   HORIZONTAL_ENUM
   cw_maxCPs  integer (DrivenOffsetMaxCPBounds)   if MAX_CP
   cw_tolerance length (TOLERANCE_BOUND)          if TOL
Suggest groups       boolean "button": editing logic reads it, walks the wire, classifies
                     joints, trial-fits each tangent stretch at the global tolerance, fills
                     the Groups array (edges + suggested MAX_CP), resets the boolean.
                     The user edits the suggestions and commits. Editing logic has a
                     context: ev* calls and approximateSpline are evaluations, not ops.
Corner angle         angle  default TBD (3 or 5 deg) -- joints sharper than this are
                     corners; between the weld threshold (0.5 deg) and this they are
                     "nearly tangent"
Make nearly tangent joints tangent   boolean, default true (nearly tangent joints are
                     merged, which makes them exactly tangent; off -> they are corners)
Recognize lines and arcs in splines  boolean, default true (asymmetric gate)
Approximation parameters (group, collapsed)   drivenOffsetApproximationPredicate:
                     degree (3), tolerance (0.01 mm), maximum control points (global)
Debug (group, collapsed)   Print joints / Show joints / Print fits / Keep pieces
```

Rules that bind (agent 4, all cited in the repo): array item names are GLOBAL across
the precondition -> prefix `cw_`; every Query needs a Filter; precondition `if` only on
enum equality / booleans; `size()` checks in the body with
`regenError(msg, [faultyArrayParameterId("groups", i, "cw_edges")])`; every parameter in
the defineFeature defaults map; any parameter added LATER must default to the old
behaviour (correction 25); ASCII, braces, typed params, quoted keys, `id + (k ~ "x")`,
no `box`/`line`/`plane`/`transform` variable names, no `type` map key.

A group whose edges are not contiguous in the chain is an error naming the group. A
group MAY contain a corner sharper than the corner angle: the user is then saying "fit
this as one anyway" (the fit smooths the corner within tolerance) -- report it as an
INFO rather than refusing, since that is a legitimate cleaning decision.

"Suggest groups" implementation note: the std pattern for editing logic is
`defineFeature(fn, defaults)` plus a separate `export function cleanWireEditLogic(context,
id, oldDefinition, definition, isCreating, specifiedParameters, hiddenBodies)` registered
with `annotation { "Editing Logic Function" : "cleanWireEditLogic" }`; it returns the
modified definition. Setting an array parameter from editing logic and clearing the
trigger boolean is the mechanism to verify first in a throwaway feature.

## 6. Output identity

- Deterministic chain start: the end with the lexicographically smaller (x, y, z) at
  10 tol, unless a "Start vertex" pick is given (recommended -- the only thing that
  survives a large upstream edit). Closed loop: the sharpest corner.
- Ids by run ordinal from that start: `id + ("run" ~ k)`; final wire `id + "wire"`.
  Small upstream changes that keep the corner set keep every id; a changed corner count
  renumbers only the runs after it, and a `reportFeatureInfo` says so.
- Endpoints snapped bit-for-bit to the neighbour's kernel end point (opExtractWires
  chains only on exact coincidence -- the 11 um lesson).
- Never `try` around a run: a silently missing run renumbers everything downstream.
- Downstream features should reference the OUTPUT BODY (or the published query
  variable), never its edges -- the composite-curve lesson from this session.

File placement: `sync/core/tabfolders.py` puts `driven_offset/clean_wire.fs` at the
document ROOT (where `bspline_compat` already sits) and
`driven_offset/featurescripts/clean_wire.fs` in the `featurescripts` tab folder beside
the other tabs. Decide before the first push; the local file location is what decides.

Imports for the new tab (copy the pins from driven_offset_surface.fs:7-12):
```
FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");
export import(path : "a2665e22c07b7a6929ce4e80", version : "d49252259f7b34bf7da639ec");  // edge_offset_utils
import(path : "6b635e74c92bd23387e850c1", version : "");                                  // bspline_compat
import(path : "f531550fd26c49415f9c443a", version : "38e6742293e654b12c3cffd4");         // merge_curve (chainEdges)
import(path : "2b6b313ac740a0146d5bef7c", version : "6e0b68b1f1ffa8bdf4921850");         // design_map_query_utils
```

## 7. Defaults

| parameter | default |
|---|---|
| shape tolerance | 0.01 mm (0.001 allowed) |
| fit tolerance handed to approximateSpline | tol / 2 |
| G1 threshold | 0.5 deg (existing 1e-2 rad weld) |
| corner angle (near-tangent max) | 5 deg |
| blend allowance at forced joints | max(10 tol, 0.05 mm) |
| sliver length | max(0.5 mm, 50 tol) |
| sample spacing | clamp(sqrt(4 tol / kappa), max(20 tol, 0.05 mm), min(chord / 8, 10 mm)) |
| arc gate (spline source) | fit min(tol, 1 um), planarity 1e-7 m, sweep >= 10 deg, thirds radii 0.1 % |
| global max control points | 30 (the wall needed more than 15 at 0.01 mm) |

## 8. Open decisions (for discussion)

1. DECIDED: groups are the input; no break picks. Automatic grouping via an
   editing-logic "Suggest groups" trigger (section 5).
2. **Corner angle default.** The ramp creases at 332.4 / 392.4 mm are 4.8 deg joints. If
   they are design intent the default must be below that (3 deg) so they stay corners;
   if they are noise, 5 deg merges them. Awaiting the user's answer.
3. **Closed loops in v1?** Periodic fit is a small extension of approximateFamily; the
   corner-cut rule is easy. Include, or reject closed input with a clear error?
4. **Exact-merge fallback in v1?** Skip. Report the shortfall and let the user raise the
   cap or add a break. Add later if a real case needs it.
5. **G2 at run ends** (`end2ndDerivative`): only where a run meets a kept exact primitive;
   verify the std behaviour in the eval sandbox first (unused anywhere so far).
6. **Rational output.** The fitter's rational-ness is undocumented; assert
   `isRational == false` on every fit and fail loudly if not.
7. DECIDED: create the file flat (document root); the user moves it into the
   `featurescripts` tab folder in Onshape.

## 9. Risks

1. Silent under-fit at the CP cap -- always measure (approximateFamily already warns).
2. Forced tangency at 3-5 deg / 0.001 mm producing R = 2-5 mm blips -- blend allowance,
   per-joint report, user-set corner angle.
3. False arcs from fitted splines -- the asymmetric gate; spline emission is the safe default.
4. Identity churn when a joint flips class across an edit -- start vertex, ordinal ids,
   an INFO when the corner count changes.
5. Interpreted runtime on long wires at 0.001 mm -- kernel-batched sampling, refit as the
   only v1 path, a hard sample budget (~5000) with a notice.

## 10. Build plan

1. `clean_wire.fs` skeleton: UI, chain, describe, joint table printed (Debug -> Print
   joints), no geometry. Verify the joint classification on Pinch_intersection_Curve
   against the manual split set.
2. Runs + exact pass-through + sampling + fit + verify + emit. Acceptance test (section 2).
3. Force-tangency and group overrides; `evMaxPathDeviation` report; query variables.
4. Re-seed Cap_Wall on the output; compare face table; delete the Wire_Smoothing folder.

References: Piegl & Tiller, The NURBS Book, 5.4 (A5.8), 5.5, 9.4; Lyche & Morken, CAGD 4
(1987) 217-230; Park & Lee, CAD 39 (2007); Jupp, SIAM J. Numer. Anal. 15 (1978).
