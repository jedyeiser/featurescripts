# Shared vocabulary for the driven_offset document -- research

Paths are repo-relative. `DEO` = driven_offset/driven_edge_offset.fs, `utils` =
driven_offset/edge_offset_utils.fs, `EP` = driven_offset/evaluate_profiles.fs. Companion docs:
research_map_curve.md, research_merge_curve.md, research_evaluate_offset.md (other writer).

## 1 Why one vocabulary

driven_edge_offset, evaluate_offset, unwrap, map_curve and merge_curve all: resolve a zero point
(utils:219), build an X-oriented chain with a zeroArc (utils:729), sample stations with transport
frames (utils:990), pick station spacing (utils:178 enum), fit freeform runs with one approximation
predicate (DEO:253), and name / join their output (DEO:891-931). Five copies of these drift; DEO and
EP already carry two sets of fit bounds (s7) and two `name*` functions (DEO:919, EP:1425).

## 2 Already exported (utils, re-exported by DEO:4 `export import`)

| symbol | file:line | used by |
|---|---|---|
| MeasureAlong, OffsetFrameAlignment | utils:125, :136 | DEO; evaluate_offset; unwrap (ALONG only) |
| CornerGapMode, CornerOverlapMode | utils:153, :170 | DEO only (offset_run_treatment.fs) |
| OffsetPointSpacing + CtrlPointMultiplierBounds, PointsPerEdgeBounds, PointSpacingBounds, OffsetHeightBounds | utils:178, :188-191 | all five except merge_curve |
| OFFSET_GEOM_TOL, G1_JUNCTION_ANGLE | utils:44, :81 | classify / junction tests everywhere |
| expandEdgeQuery, evZeroPoint | utils:205, :219 | all five |
| buildChain, arcLengthAtX, edgeParam | utils:729, :884, :870 | DEO, evaluate_offset, unwrap, map_curve |
| stationCount, chainStations, transportNormal, transportedNormals, resolveAxisRoles, offsetAxes, curvatureOn, offsetShrink | utils:948, :990, :456, :489, :524, :543, :1059, :560 | DEO, evaluate_offset, unwrap, map_curve |
| classifyPoints, circleThrough, emitLineCurve, emitArcCurve, emitSplineCurve | utils:380, :329, :2500, :2518, :2543 | every emitter |
| buildAlongReference, alongCoordinate, referenceSurfaceCoords, referenceSurfacePoint, surfaceOffset | utils:1703, :2123, :2009, :2035, :624 | DEO REFERENCE_WIRE; unwrap; map_curve PARALLEL_ARC |
| planeFromQuery, isConstrained, usesReferenceFrame, frameRates | utils:2340, :2690, :2704, :2768 | DEO; evaluate_offset end tangents |
| projectedProfile, ProfilePointsBounds, ProfileDegreeBounds, ProfileMaxCPBounds, ProfileToleranceBounds | EP:278, :88-96 | evaluate_offset PROJECTED_PLANE |

## 3 Move to utils and export (private in DEO today)

| symbol | file:line | needed by |
|---|---|---|
| OffsetMaxCPBounds | DEO:231 | every fitting feature |
| offsetApproximationPredicate | DEO:253 | evaluate_offset, unwrap, map_curve, merge_curve |
| spacingSettings, approximationSettings | DEO:269, :286 | same (definition -> settings maps) |
| resolveFrames, stationFrame, worldFrame | DEO:351, :370, :436 | evaluate_offset, unwrap |
| buildRuns, closeRun | DEO:536, :572 | evaluate_offset, map_curve FROM_EDGES |
| crossingStation | DEO:696 | seed for frameAtArc (s4) |
| edgeAtArc | DEO:736 | evaluate_offset, map_curve, unwrap |
| nameOutput | DEO:919 (duplicate of EP:1425 nameProfile) | all five; delete nameProfile |
| drawChainEnds, drawDebugSegments, debugStride | offset_debug.fs:213, :348, :86 | map_curve direction arrows; evaluate_offset debug |

offset_debug.fs already imports utils (offset_debug.fs:3) so moving helpers there is import-neutral.

## 4 New

| symbol | kind | definition sketch | needed by |
|---|---|---|---|
| RefPointMode | enum | `{ SHARED, SEPARATE }` -- one MC for both chains vs one per chain | map_curve; unwrap (wrapped vs flat origin); evaluate_offset |
| ProjectionMode | enum | `{ WORLD_X, CLOSEST_POINT }` -- how a zero point lands on a chain | map_curve, unwrap, evaluate_offset |
| LengthPreservation | enum | `{ REFERENCE_ARC, PARALLEL_ARC }` -- s on the reference vs s - h*theta(s) on the point's own parallel curve (utils:2123; CM mappedArc) | unwrap, map_curve off-chain |
| MapMode | enum | `{ FROM_EDGES, TO_EDGES, SINGLE }` | map_curve |
| WireOutput | enum | `{ PER_LINK, PER_RUN, SINGLE }` -- replaces `joinOutput` boolean (DEO:135); PER_LINK = today's true, PER_RUN = false, SINGLE = merge_curve path | DEO (migrate), evaluate_offset, map_curve |
| offsetSpacingPredicate | predicate | the "Spacing & approximation" group DEO:113-141 minus joinOutput: `edgeOffsetSpacingDef` + its three driven fields | all sampling features |
| offsetZeroPointPredicate | predicate | `offsetRefPoint` with filter `BodyType.MATE_CONNECTOR \|\| EntityType.VERTEX` (DEO:109); optional second point when RefPointMode.SEPARATE | all five |
| chainZeroArcAtPoint | function | `(context, chain, point, mode) -> arc`; WORLD_X -> arcLengthAtX; CLOSEST_POINT or X-not-spanned -> one evDistance(point, chain edges) (std/evaluate.fs:410), arc = startArc + (flipped ? 1-p : p)*length | buildChain (optional mode), map_curve, unwrap |
| projectOntoChain | function (batched) | `(chain, edgeSplines, points) -> [{ arc, edgeIndex, w, h, tangent }]`; Newton on dot(C(u)-P, C'(u)) with evaluateSpline over all points of an edge per pass (skeleton utils:251 paramsAtX); no evDistance | evaluate_offset, unwrap, map_curve off-chain |
| frameAtArc | function (batched per edge) | `(context, chain, stations, arcs) -> frames`; crossingStation generalised: one evEdgeTangentLines per edge, transportNormal from the nearest station | map_curve, evaluate_offset |
| grevilleAbscissae | function | `(curve) -> [xi_i]`, xi_i = (t_{i+1}+..+t_{i+p})/p; with evaluateSpline gives one sample per control point for classifyPoints | unwrap arc detection; stationCount CTRL_POINT |

## 5 Parameter-name alignment

| DEO (today) | evaluate_offset | unwrap | map_curve | merge_curve |
|---|---|---|---|---|
| offsetEdges | offsetEdges (reference) | referenceWire | fromEdges | seedEdge |
| offsetProfile | -- (output) | -- | toEdges | mergeEdge |
| -- | targetEdges | targetEdges | -- | -- |
| offsetRefPoint | offsetRefPoint | offsetRefPoint (+ unwrappedOrigin MC) | offsetRefPoint / fromRefPoint, toRefPoint | -- |
| -- | refPointMode | refPointMode | refPointMode | -- |
| -- (X only) | projectionMode | projectionMode | projectionMode | -- |
| measureAlong | measureAlong (+ PROJECTED_PLANE) | fixed REFERENCE_ARC | -- | -- |
| referenceWire, alongOffsetDelta, flipAlongOffsetDir | same | lengthPreservation | lengthPreservation | -- |
| frameAlignment | frameAlignment | fixed ALONG | fixed ALONG | -- |
| edgeOffsetSpacingDef + ctrlPointMultiplier / pointsPerEdge / targetPointSpacing | same predicate | same | same | -- |
| approximationDegree, approximationTolerance, approximationMaxCPs | same predicate | same | same | same (+ keepStartDerivative, keepEndDerivative) |
| joinOutput | wireOutput | wireOutput | wireOutput | -- (always one wire) |
| outputName | outputName | outputName | outputName | outputName |
| -- | -- | -- | mapMode, flipTo | -- |
| debugShowChainEnds etc. (DEO:143-174) | subset | subset | debugShowChainEnds, debugPrint{From,To}Chain | -- |

EP uses `fitDegree`, `fitTolerance`, `fitMaxCPs` (EP:213-219) for the same three fields; align to
the `approximation*` names when EP is next touched so one predicate serves it too.

## 6 Where it lives (D4)

Recommendation: everything above goes in utils. Its header already declares it the shared home
(utils:5 "utilities for driven_edge_offset and unwrap"), DEO:4 already `export import`s it, and the
user's workflow forbids creating .fs files locally. A separate `offset_vocabulary` tab (created in
Onshape first, then synced) would only be worth it if: (1) utils' ~2800 lines make enum/predicate
edits slow to review; (2) a feature wanted the enums without the geometry -- none does; (3) tab-level
version pinning of the vocabulary were wanted -- premature with five features in one document.

## 7 Required fixes to shared code

| fix | file:line | who needs it |
|---|---|---|
| referenceSurfaceCoords Newton denominator: `basis.scale` -> `basis.scale - basis.curvature * height` (surfaceOffsetTangent already does, utils:686); iterate to residual, not fixed REFERENCE_FOOT_STEPS = 2 (:57) | utils:2019 | unwrap (h != 0), map_curve PARALLEL_ARC |
| referenceSurfaceCoords seed by X (`referenceArcAtX`) -> seed by nearest alongRef.points / warm-start | utils:2011 | unwrap at a vertical tip; evaluate_offset REFERENCE_WIRE |
| buildAlongReference X-monotone guard throws (slope < 1e-6) and "straight everywhere" guard | utils:1760-1766, :2108 | unwrap on a wrapped reference; build tables without `xs` when ProjectionMode is CLOSEST_POINT |
| arcLengthAtX "not spanned" throw -> fall through to chainZeroArcAtPoint CLOSEST_POINT | utils:936 | map_curve, unwrap; DEO unchanged (still throws for WORLD_X) |
| stationCount discards the evApproximateBSplineCurve it pays for | utils:970 | keep it on edgeData for projectOntoChain / grevilleAbscissae |
| nameOutput / nameProfile duplicate | DEO:919, EP:1425 | one exported copy in utils |
| joinOutput boolean -> WireOutput | DEO:135, :891 | keep `PER_LINK` as the default so existing features regenerate unchanged |

## 8 Onshape workflow note

New tabs (map_curve, merge_curve, evaluate_offset, unwrap) already exist as stubs; do NOT create
any other .fs locally -- Onshape assigns element ids, so a new tab such as `offset_vocabulary` is
created blank in Onshape, synced down, then edited. Before each push run
`python fscheck.py driven_offset/*.fs` with ALL tabs of the document (CLAUDE.md "fscheck.py"); it
catches the duplicate-definition error that moving a helper from DEO into utils without deleting
the original would produce, and unresolved names left behind by the move.
