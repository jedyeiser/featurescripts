# map_curve -- research

Paths are repo-relative. `DEO` = driven_offset/driven_edge_offset.fs, `utils` =
driven_offset/edge_offset_utils.fs, `CM` = curveMapping/curveMappingCore.fs. Companion docs:
research_merge_curve.md, research_shared_vocabulary.md, research_evaluate_offset.md (other writer;
owns the batched projection `projectOntoChain`).

## 1 Purpose and stub requirements

Stub: driven_offset/map_curve.fs:4-15. A length-preserving map of curve data from `fromEdges` onto
`toEdges`, measured from reference point(s). Requirements: ref-point enum {SHARED, SEPARATE} with
mate connector(s) (:7-8); visualise chain directions and flag when the two chains point opposite
ways (:9); three modes (:10-13): FROM_EDGES (deform fromEdges along toEdges, same distance from ref),
TO_EDGES (trim a toEdges wire at the arc-length span of fromEdges), SINGLE (one curve).

The distance-preserving core already exists twice: DEO's coordinate machinery (chains, zeroArc,
stations, transport frames) and CM's `mapSinglePoint` (CM:1148). map_curve is DEO vocabulary
(utils, no cross-document imports, utils:7) doing CM's job.

## 2 Mode-by-mode geometry and reuse

Shared preamble (all modes): resolve ref point(s) (s4) -> `buildChain` for from and to (utils:729;
expands wires/composites :205, orders with constructPaths :740, orients ascending world X :795,
`zeroArc` :779) -> direction check (s3).

### FROM_EDGES (D2)

Sample the from-chain at stations; for each, arc offset `s = station.arc - fromChain.zeroArc`; place
at the to-chain point with arc `toChain.zeroArc + s`, carrying the station's local (w, h) in the
transport frame. On-chain from-edges have (w, h) = (0, 0): the output is the to-chain re-cut to the
from-chain's span, and the map is an isometry along the curve.

| step | helper | file:line | status |
|---|---|---|---|
| stations on from-chain | stationCount, chainStations (welded, transported normals, axis roles) | utils:948, :990, :1148, :1209, :489, :524 | as-is |
| arc from ref | `station.arc - chain.zeroArc` | DEO:736 edgeAtArc inverse | trivial |
| locate arc on to-chain -> edge + fraction | edgeAtArc + edgeParam | DEO:736 (private), utils:870 | export edgeAtArc |
| frame at arbitrary arc on to-chain | crossingStation pattern (evEdgeTangentLines + transportNormal + offsetAxes) | DEO:696-712, utils:456, :543 | generalise to `frameAtArc(context, chain, stations, arc)`; batch all arcs of one edge in ONE evEdgeTangentLines call |
| off-chain from-edges: (w, h) of each sample relative to the from-chain | projectOntoChain (batched pure math, evaluateSpline over the edge's evApproximateBSplineCurve) | research_evaluate_offset.md; skeleton utils:251-294 paramsAtX | NEW, shared with evaluate_offset; never per-point evDistance |
| off-chain: (1 - h*kappa) arc correction | mappedArc | CM:536-556 | port only when h != 0 (s5) |
| group into runs at to-chain edge breaks / G0 corners | buildRuns | DEO:536 (private) | export |
| classify + emit | classifyPoints, emitLineCurve, emitArcCurve, emitSplineCurve (+snapEnds) | utils:380, :2500, :2518, :2543 | as-is; end tangents = from-tangent rotated into the to-frame |
| wires + naming | joinOutput branch, nameOutput | DEO:891-916, :919 (private) | export nameOutput; WireOutput enum (vocab s4) |

### TO_EDGES (D2)

`s0, s1` = arc of the from-chain's first/last station minus `fromChain.zeroArc`; on the to-chain
locate `toChain.zeroArc + s0` and `+ s1` with edgeAtArc -> (edge, fraction). Then:

| step | helper | file:line | status |
|---|---|---|---|
| copy to-edges into a fresh wire | opExtractWires | std/geomOperations.fs:564 | as-is; DEO:901 pattern (copies are independent) |
| split at the two arc fractions | opSplitEdges `{ edges : [e0, e1], parameters : [[f0], [f1]], arcLengthParameterization : true }` | :1392 | UNVERIFIED on wire-body edges (std's only caller is sheet metal); fraction is arc-length by default :1385-1389, so edgeParam's flip handling applies |
| delete the outer pieces | opDeleteBodies on qOwnedByBody pieces outside [s0, s1] | :406 | identify by midpoint arc via evEdgeTangentLines([0.5]) |
| fallback A: trim | opMoveCurveBoundary `{ wires, moveBoundaryType : TRIM, trimTo : plane }` twice | :74; feature std/moveCurveBoundary.fs:16 | needs a plane normal to the tangent at s0 / s1; ambiguous where the chain re-crosses the plane |
| fallback B: split body | opSplitPart with a plane tool (targets wire bodies) | :1524 | same plane ambiguity |
| span exceeds to-chain | opMoveCurveBoundary EXTEND (cf. DEO allowExtension :105) or error | :74 | decision s9 |

TO_EDGES keeps the to-edges' true types (arcs stay arcs) because it never re-fits.

### SINGLE (D2)

FROM_EDGES, then merge every emitted run into one curve by the merge_curve native path
(research_merge_curve.md s4: constructPath -> makeApproximationTarget -> approximateSpline ->
opCreateBSplineCurve -> opEditCurve -> opDeleteBodies). Arc type is lost; warn per merge_curve s8.
If every run junction is G1 (utils:81 G1_JUNCTION_ANGLE) opSplineThroughEdges (:1370) is a one-call
alternative; UNVERIFIED whether it accepts G0-only junctions, so keep the merge path as primary.

## 3 Direction check and visualisation

buildChain forces both chains ascending in world X (:795 via buildLink's end-vs-start test), so the
tangents at the two ref points agree in X-sense whenever both chains are X-monotone there. A
disagreement can still arise at a ski tip/tail where X reverses, or on a to-chain that is not a ski
(the deform use). Check: `dot(fromFrame.tangent, toFrame.tangent) < 0` at the two ref arcs (the
frames come from `frameAtArc`, s2). On failure: `reportFeatureWarning` naming both ends, and require
the user to set `flipTo` (boolean, UIHint.OPPOSITE_DIRECTION, as curveMapping/wrapCurve.fs:51-52).
No silent auto-flip. With `flipTo` the to-chain's link order and every edge's `flipped`/`startArc`
are reversed before zeroArc is computed (reverseDescribed, utils buildLink :797-798 pattern).

Visualise with the existing pattern: `drawChainEnds` (driven_offset/offset_debug.fs:213-264) puts a
green arrow at the start and red at the end, `DEBUG_END_ARROW` :257. Reuse it for both chains behind
one `debugShowChainEnds` boolean; add one arrow per chain at the ref point (tangent direction).
Two arrows per chain -- not per station -- keeps within the corrections-log rule on debug cost
(.claude/featurescript-corrections.md:856).

Normal sign: CM reconciles normal orientation per point (CM:1162-1173 flips xAxis when the from and
to frame signs differ). DEO's transported normals are seeded once at the zero station (utils:1224-
1236), so on planar ski chains both normals point the same side and no per-point flip is needed;
expose `flipNormal` only if the deform use (s5) needs it.

## 4 Reference-point semantics (D3)

`RefPointMode.SHARED`: one mate connector; both chains take `zeroArc = arcLengthAtX(chain,
zeroPoint[0])` (utils:779, :884-937). World-X projection is the ski convention: a mate connector at
the FCP is "the same station" on the core bottom and on the sidecut whatever its Y/Z.
`RefPointMode.SEPARATE`: `fromRef -> fromChain.zeroArc`, `toRef -> toChain.zeroArc`, exactly
curveMapping/wrapCurve.fs:236-237 (`fromRefArc`, `toRefArc` from two projections).

arcLengthAtX throws when the X is not spanned (utils:936) and secant-refines with one kernel call
per step (:910). Documented fallback (vocab s4 `chainZeroArcAtPoint`): when no edge straddles X, or
when `ProjectionMode.CLOSEST_POINT` is chosen, take the closest point instead -- `evDistance(point,
chain edges)` once (std/evaluate.fs:410; `sides[1].parameter` is arc-length :338-340), then
`edgeData.startArc + (flipped ? 1 - p : p) * length` (utils:870 edgeParam inverse). One call per
ref point, not per sample, so the kernel budget is unaffected. The two conventions disagree at a
vertical tip by exactly the tip's overhang; report which one was used in the debug print.

## 5 Relation to curveMapping wrap / deform and the (1 - h*kappa) factor

FROM_EDGES with on-chain from-edges is `mapSinglePoint` (CM:1148-1178) with `localCoords = (0, 0,
s)`: project onto from-path (CM:1039 projectOntoFrenetPath), arc shift `s_to = toRefArc + (s_from -
fromRefArc)` (CM:536 `mappedArc`, CM_PARALLEL_ARC off :520), reconstruct in the to-frame. The CM
frame is Frenet/FrameNormalMode (CM:39, xAxis = normal, zAxis = tangent); DEO's is parallel
transport with fixed axis roles (utils:489, :524). Same map, different normal field.

The factor matters only when a from-edge is OFF its own chain by h != 0 (deform of a whole set of
curves against a reference chain). Then "same distance along its own parallel curve" is
`f(s) = (s - toRefArc) - h*(theta_to(s) - theta_to(toRefArc)) - travelled`, `f'(s) = 1 - h*kappa_to(s)`
(CM:530-553). DEO already carries the same term as `scale = 1 - delta*kappa` (utils:1719, :1746,
:1927, :1986) and as `alongCoordinate` = s - h*theta(s) (utils:2123). Expose as
`LengthPreservation {REFERENCE_ARC, PARALLEL_ARC}` (vocab s4), default REFERENCE_ARC, since the stub
speaks of distance along the to-edges. Fold-back guard: `offsetShrink` (utils:560) > 0.

## 6 Parameters (names per research_shared_vocabulary.md s5)

| parameter | type | notes |
|---|---|---|
| mapMode | MapMode {FROM_EDGES, TO_EDGES, SINGLE} | new enum |
| fromEdges, toEdges | Query, filter DEO:73 | expandEdgeQuery |
| refPointMode | RefPointMode {SHARED, SEPARATE} | new enum |
| offsetRefPoint / fromRefPoint, toRefPoint | Query, filter DEO:109 | one or two, driven by refPointMode |
| projectionMode | ProjectionMode {WORLD_X, CLOSEST_POINT} | default WORLD_X |
| flipTo | boolean, OPPOSITE_DIRECTION | required when direction check fails |
| lengthPreservation | LengthPreservation | only shown when from-edges are off-chain (advanced) |
| edgeOffsetSpacingDef + fields | offsetSpacingPredicate | FROM_EDGES / SINGLE only |
| approximationDegree/Tolerance/MaxCPs | offsetApproximationPredicate | FROM_EDGES / SINGLE only |
| wireOutput | WireOutput {PER_LINK, PER_RUN, SINGLE} | SINGLE mode forces SINGLE |
| outputName | string | nameOutput |
| debugShowChainEnds, debugPrintFromChain, debugPrintToChain | booleans | DEO:143-174 group |

## 7 New code estimate

| unit | lines | where |
|---|---|---|
| ref-point resolver (SHARED/SEPARATE + X / closest-point fallback) | ~40 | utils (shared) |
| frameAtArc, batched per edge | ~50 | utils (shared with evaluate_offset) |
| direction check + flipTo chain reversal + arrows | ~50 | map_curve.fs |
| FROM_EDGES mapper + run grouping + emit | ~150 | map_curve.fs |
| TO_EDGES split/trim wrapper + piece cleanup | ~90 | map_curve.fs |
| SINGLE glue (call merge path) | ~20 | map_curve.fs |
| precondition + settings maps | ~110 | map_curve.fs |
| export/move of private DEO helpers | ~60 | DEO -> utils (vocab s3) |

Roughly 570 lines, ~350 new logic. Off-chain (w, h) support (projectOntoChain) is not counted.

## 8 Risks and Onshape tests

| risk | test |
|---|---|
| opSplitEdges rejects wire-body edges | split one edge of an extracted wire at [[0.3]]; expect 2 edges, 1 body |
| opSplitEdges parameter sense on flipped edges | split at 0.25 on an edge whose chain direction is reversed; check which piece is short |
| opSplitEdges at a fraction within 1e-6 of 0 or 1 | clamp to the vertex instead of splitting (zero-length sliver) |
| multi-link from-edges (two disjoint groups) | both links map; output grouped per link; zeroArc shared |
| from span exceeds to-chain | edgeAtArc clamps fraction to 1 (DEO:756) -- output silently short; must warn or extend (s9) |
| from-chain not X-monotone at the ref | direction check fires; verify warning text and flipTo recovers |
| SINGLE on arcs | warning from merge_curve s8 fires; radius lost is expected |
| kernel budget | count evEdgeTangentLines calls: one per to-edge that hosts stations, not one per station |

## 9 Open decisions (with recommendation)

1. Span exceeding the to-chain (TO_EDGES / FROM_EDGES): error vs extend. Recommend error with the
   overrun distance in the message; add `allowExtension` later if a ski case needs it.
2. Closest-point as the default instead of world X? Recommend no: keep WORLD_X to agree with DEO
   and evaluate_offset on the same mate connector; CLOSEST_POINT is the explicit alternative.
3. Direction mismatch: hard error vs warning + flipTo. D2 fixes warning + flipTo.
4. Off-chain from-edges in phase 1? Recommend no: phase 1 requires from-edges on their own chain
   ((w, h) = 0), which is the stub's wording. Phase 2 adds projectOntoChain and LengthPreservation.
5. TO_EDGES output identity: new wire (copy) vs editing the selected wire in place. Recommend copy;
   trimming the user's own wire is destructive and unlike every other feature in this document.
6. SINGLE via opSplineThroughEdges when all junctions are G1: test once; adopt only if it is exact
   on a two-arc input (it may also refit). Otherwise merge path.
