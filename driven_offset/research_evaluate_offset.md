# evaluate_offset -- research

## 0. AS BUILT (v1, 2026-09-24) -- read this first; sections 1-9 are the 2026-09-10 research

`driven_offset/evaluate_offset.fs` (tab a2ebb5abc7ddda01f64ff8df), feature "Evaluate offset". Inputs: Reference
edges (key `offsetEdges`, what DEO would offset), Target edges, and DEO's own Measure along / Offset alignment /
Zero point / spacing / approximation keys, so one definition means the same in both features.

Changed from the phase-1 plan (section 3) -- STATION-driven, not target-driven:
- Stations and frames are DEO's own: DEO now exports `offsetStationBase` (zero point, chain, alongRef, regular
  stations, coords), `offsetStationFrames` (resolveFrames) and `offsetStationsWithBreaks` (insertCrossings +
  resolveFrames); `sharedOffsetContext` calls them (pure refactor, Design_Master fingerprint identical per feature).
  So the measured stations ARE the stations DEO will sample -- no projection, no arc(u) table, no frame roll.
- At each station the offset lies in the section plane (normal = frame tangent: chain tangent for Along, X for
  World). The target is cut by that plane: non-rational B-spline per target edge (correction 39), coarse samples,
  bracket by PLANE side (bracketStation; a 3D-nearest walk fails under World frames), safeguarded Newton batched
  per target edge. Zero kernel calls per station. w = dot(P - O, W), h = dot(P - O, H); offsetShrink <= 0 dropped.
- Every target vertex gets an exact crossing pair (coordinate from an osculating-model Newton at the bracketing
  station), so target corners are profile corners. At a crossing pair each half takes the target edge on its own
  side (a jump = two ends at one plane).
- Profile BREAKS (one wire per piece) at a step (two stations at one coordinate disagree) and at a gap the target
  does not run across. A gap the target DOES run across (connected target edges) is bridged by the run's fit.
- Inside G0 reference corners: cuts ahead of the next edge's start plane / behind the previous edge's end plane are
  rejected (inCornerOverlap) -- they belong to the other side's trimmed target; the gap is bridged. Outside corners
  need nothing (the fill lies between the halves' planes).
- Output: lines where straight, else approximateSpline fits (no arcs, no end derivatives). Published (VT V2
  extract_outputs): pieceCount, breakCount, breakStations, start/endStation, measuredStations, stationCount,
  piece1..N, start/endVertex, breakVertices.

Tests: Part Studio "Evaluate offset tests" (0ce4ac09e693f8ecd18e8a7f), real instances built by
`devtools/onshape/build_evaluate_offset_tests.py`, checked by `devtools/onshape/check_evaluate_offset.py` (11/11 on
2026-09-24): E1 line, E2 arc R400, E3 jump -> 2 pieces, E4 World / World X, E5 outside G0 corner, E6 inside G0
corner -- measured vs original profile <= 5.5 um; R2-R6 DEO driven by the MEASURED profile vs the original target
<= 10.4 um, lengths within 1.5 um. All at 5 mm spacing (at 71 mm spacing the target itself only approximates the
original profile).

Not in v1 / open: Measure along = Reference wire (regenError; the placement is a walk in the reference surface --
its cut is (arc_P - arc0) * beta * scale = (v_P - v0) * alpha in the chart, same bracket/solve framework);
3D reference untested; end derivatives on fitted runs; timing on a real chain; icon.

Abbreviations: utils = driven_offset/edge_offset_utils.fs, DEO = driven_offset/driven_edge_offset.fs,
CM = curveMapping/curveMappingCore.fs. Shared enums and predicates: research_shared_vocabulary.md.

## 1. Purpose and requirements (stub: driven_offset/evaluate_offset.fs:4-12)

driven_edge_offset run backwards: given reference edges and target edges, produce the offset
profile DEO would need to regenerate the target from the reference. Stub requires: reference
edges; target edges; measure along the reference curve or along a plane projection of it; frame
alignment; zero point. Output must be a profile DEO consumes unchanged (D2).
Phase 1 (D1): MeasureAlong.OFFSET_EDGES and WORLD_X, no referenceWire. Plane projection
(projectedProfile, evaluate_profiles.fs:275, params :192-199) and REFERENCE_WIRE are phase 2.

## 2. Round-trip invariant with driven_edge_offset

Profile convention (buildProfile utils:1303-1345, profileAt :1605-1630): a world 3D curve with
X = zeroPoint.x + coordinate (:1318-1319), Y = width (:1625), Z = height (:1626); X monotone
per edge (checkMonotonicX :1520-1541, throws); a zero-X-extent edge is a step (:1323-1327);
edges ordered by connectivity (orderProfileEdges :1373). evaluate_offset must emit points
(zeroPoint[0] + coord_i, w_i, h_i) from the SAME zero-point selection, or X shifts.

Forward (DEO:514): P = C(s) + w*W(s) + h*H(s); coordinate = s - zeroArc (OFFSET_EDGES) or
C(s).x - zeroPoint.x (WORLD_X, DEO:321). W, H: transported station axes (ALONG, utils:1236-1252)
or (0,1,0), (0,0,1) (WORLD, DEO:436-449). Inverse for a target point P:
- ALONG: s solves dot(P - C(s), T(s)) = 0; w = dot(P - C(s), W(s)); h = dot(P - C(s), H(s)).
- WORLD: P.x = C(s).x, so s solves C(s).x = P.x (paramsAtX as-is, utils:251-294); w = P.y - C.y.
- Unique iff offsetShrink = 1 - w*kW - h*kH > 0 (utils:560); DEO throws below it (DEO:484-490),
  so every DEO output is invertible.
Where the round trip is inexact:
- Fit tolerance: freeform runs are approximateSpline fits (emitSplineCurve utils:2565-2573), up
  to approximationTolerance off; snapEnds :2584 pins endpoints only. Lines/arcs are exact.
- Frame roll on 3D chains: transport is seeded at the station nearest zeroArc (utils:1222-1236);
  a different station count moves the seed and rolls W, H. Planar chains are immune.
- Slope breaks/steps: insertCrossings (DEO:599) makes a left/right pair at one coordinate. Kink =
  two target edges meeting (recoverable); step = gap between links (needs a step edge, step 9).
- Corner fills (cornerArc utils:2208, offset_run_treatment.fs:102): one foot, many (w,h), so the
  inverse is a vertical segment; trims shorten the profile by w*tan(theta/2). Terminal extensions
  run past the chain: edgeAtArc clamps at fraction 1 (DEO:756).
- REFERENCE_WIRE: coordinate = s_ref - delta*theta (utils:2123), placement = surfaceOffset
  (:624); the normal-foot inverse is WRONG there. Phase 2.

## 3. Algorithm (phase 1)

1. Setup. zeroPoint = evZeroPoint (utils:219). ref = buildChain(referenceEdges, zeroPoint)
   (:729; zeroArc by arcLengthAtX :779). target = buildChain(targetEdges) with zeroArc forced
   to 0: the target need not span the zero X (arcLengthAtX throws, :936).
2. Reference stations. chainStations (:990) -> finishStations (:1209): arc, origin, tangent,
   normal, roles. resolveFrames (DEO:351, exported) with alongRef undefined leaves ALONG
   stations as they are (DEO:376-380) and swaps in worldFrame for WORLD.
3. Keep the per-edge B-spline. stationCount pays evApproximateBSplineCurve per spline edge
   (:970) and discards it; keep it as edgeData.curve. Line/arc edges (:961-966) get one call.
4. Arc table per reference edge. Project the edge's own stations (arc-parameterized, :1002-1010)
   onto edgeData.curve with the batch below -> (u_i, arc_i), slope |C'(u_i)|; arc(u) =
   hermiteAt(us, arcs, slopes, u) (utils:1885, the referenceArcAtX stencil :1870). Exact at
   stations. Flipped edges reverse the sign.
5. Target samples. chainStations(target, spacing): origin, linkIndex, edgeIndex are consumed.
6. Projection (batched). Seed each sample by nearest of the SEED_SAMPLES points per reference
   edge (:257, :251-260). Group by seed edge; one Newton batch per edge with residual
   f = dot(C(u) - P, C'(u)), slope f' = dot(C', C') + dot(C - P, C''), nDerivatives:2
   (std/splineUtils.fs:87), clamped to the knot range; done when |f|/|C'| < 1e-9 m. A foot
   pinned at uMin/uMax retries on edge -1/+1 and keeps the nearer (CM:1047-1062 pattern).
   WORLD frames use paramsAtX unchanged.
7. (s, w, h). s = edgeData.startArc + arc(u). Frame at s: tangent = normalize(C'(u)) (negated
   if flipped, :1008); normal = transportNormal(prev.normal, prev.tangent, tangent) (utils:456)
   from the station before s (crossingStation pattern DEO:704-705, minus the kernel call);
   axes = offsetAxes(tangent, normal, roles) (:543). coord per section 2; w, h by dot products.
8. Ordering. coord must increase along each target edge, else the target folds relative to the
   reference: throw with the arc position (DEO:484-490 wording).
9. Runs. buildRuns (DEO:536, exported) splits at target edge boundaries, so a G0 corner is two
   runs sharing a point (profile kink). Between two target links whose end coords agree within
   PROFILE_JOIN_TOL (utils:60), emit a zero-X-extent line: buildProfile :1323 reads a step.
10. Emit. points_i = vector(zeroPoint[0] + coord_i, w_i, h_i); classifyPoints(points,
    approximationTolerance) (:380) -> emitLineCurve / emitArcCurve / emitSplineCurve with
    undefined end derivatives (:2500, :2518, :2543, :2556-2563). joinOutput: opExtractWires per
    link then delete the curves (DEO:891-903). nameOutput (DEO:919, exported).

```
// One reference edge. targets: world points; seeds: parameters in the knot range.
// Production adds the converged-early exit paramsAtX has (utils:283-286).
function projectBatch(curve is BSplineCurve, targets is array, seeds is array) returns array
{
    const uMin = curve.knots[0];
    const uMax = curve.knots[size(curve.knots) - 1];
    var params = seeds;
    for (var iteration = 0; iteration < NEWTON_ITERATIONS; iteration += 1)
    {
        const ev = evaluateSpline({ "spline" : curve, "parameters" : params, "nDerivatives" : 2 });
        for (var i = 0; i < size(params); i += 1)
        {
            const d = ev[0][i] - targets[i];
            const residual = dot(d, ev[1][i]) / (meter * meter);
            const slope = (dot(ev[1][i], ev[1][i]) + dot(d, ev[2][i])) / (meter * meter);
            if (abs(slope) < 1e-12)
            {
                continue;
            }
            params[i] = clamp(params[i] - residual / slope, uMin, uMax);
        }
    }
    return params;
}
```

## 4. Kernel-call budget (6-edge chain, CTRL_POINT x3, ~180 stations, 6 freeform runs)

| Design | Per station | Total | Source |
|---|---|---|---|
| DEO today, OFFSET_EDGES | 0 | ~65 | raw report 06 follow-up; utils:740, :847-849, :910, :970, :1010, :1329, :1379, :2565-2573 |
| DEO + REFERENCE_WIRE | 0 | ~100 | second buildChain + sampleTurning :2062 |
| Station-driven (plane slice, evDistance per station) | 1 | ~245, worst 1215 | measureBetweenCurves.fs:823 pattern; std/evaluate.fs:410 |
| Target-driven (batched Newton, this doc) | 0 | ~100 | ref chain ~43 + target chain ~43 + emit ~15 |
| Any design + addDebugLine per sample | +1 sketch solve | +180 (2.17 s per 758) | .claude/featurescript-corrections.md:856 |

Verdict: target-driven. evDistance returns one minimum per call, no batching
(std/evaluate.fs:377-393), so station-driven cost scales with stations. Target-driven makes
zero kernel calls after setup: ~4 evaluateSpline passes per reference edge. Debug drawing only
through drawDebugSegments (offset_debug.fs:348, one id, one addDebugEntities).

## 5. Reuse map

| Step | Helper | Where | Status |
|---|---|---|---|
| selection, zero point | expandEdgeQuery, evZeroPoint | utils:205, :219 | as-is |
| chains | buildChain, describeEdges, edgeParam | utils:729, :840, :870 | as-is; add no-zero option (~10 lines) |
| stations | stationCount, chainStations, finishStations | utils:948, :990, :1209 | as-is; retain curve from :970 |
| frame resolution | resolveFrames, stationFrame, worldFrame | DEO:351, :370, :436 | export (D5) |
| frame at a foot | transportNormal, offsetAxes, curvatureOn | utils:456, :543, :1059 | as-is |
| X inversion (WORLD) | paramsAtX, seedParam | utils:251, :300 | as-is |
| closest-point inversion | projectBatch | -- | new (~40 lines) |
| arc(u) table | hermiteAt | utils:1885 | as-is |
| arc -> edge, runs | edgeAtArc, buildRuns, closeRun | DEO:736, :536, :569 | export (D5) |
| fold-back diagnostic | offsetShrink | utils:560 | as-is |
| classify, emit | classifyPoints, emitLine/Arc/SplineCurve | utils:380, :2500, :2518, :2543 | as-is |
| wires, naming | opExtractWires pattern, nameOutput | DEO:891-903, :919 | export nameOutput (D5) |
| debug | drawDebugSegments, debugStride | offset_debug.fs:348, :372 | export |
| self-check (debug) | buildProfile on own output | utils:1303 | as-is |

## 6. Parameters

| Name | Type | Shared with DEO | Notes |
|---|---|---|---|
| referenceEdges | Query (EDGE or WIRE, non-construction) | = offsetEdges (DEO:72) | same filter |
| targetEdges | Query | new; unwrap uses the same name | edges whose offset is measured |
| measureAlong | MeasureAlong (utils:125) | yes | phase 1 rejects REFERENCE_WIRE with regenError |
| frameAlignment | OffsetFrameAlignment (utils:136) | yes | default ALONG (DEO:78) |
| offsetRefPoint | Query (MC or VERTEX) | yes, same name (DEO:109) | the round-trip zero point |
| edgeOffsetSpacingDef + 3 fields | OffsetPointSpacing + bounds | yes | offsetSpacingPredicate (new, wraps DEO:113-141) |
| approximationDegree/Tolerance/MaxCPs | int, length, int | yes | offsetApproximationPredicate (DEO:253, export) |
| joinOutput, outputName | boolean, string | yes (DEO:135) | |
| debug: drawFeet, printRuns | boolean | partly | feet as batched segments |

## 7. New code estimate

projectBatch + seeding + neighbour fallback ~60 lines and arc(u) table ~25 lines (utils, pure
math); buildChain no-zero option + edgeData.curve ~15; evaluate_offset.fs parameters,
precondition, steps 5-10, debug ~300-350; D5 exports ~60 lines of moves. Total ~450-500.

## 8. Risks and Onshape tests

1. evApproximateBSplineCurve fit accuracy is undocumented (UNVERIFIED). Setup: DEO a planar
   spline, w = 3 mm, h = 0; evaluate. Observe |w - 3 mm| per sample. Retires: fit error.
2. Arc(u) table. Setup: NUM_POINTS 10 vs 50. Observe profile X at shared samples < 1 um apart.
3. Frame roll. Setup: 3D reference, DEO w = 5, h = 2; evaluate at same and doubled spacing.
   Observe w, h drift. Quantifies utils:1222-1236 seed sensitivity.
4. Round trip. Setup: DEO profile with one kink and one 2 mm step; evaluate; DEO again.
   Observe target reproduced within approximationTolerance. Retires D2.
5. Folding target. Setup: w > 1/kappa somewhere. Observe regenError naming the arc, never a
   non-monotone profile. Retires step 8.
6. Zero X outside the target. Setup: target trimmed short of the zero point. Retires step 1.
7. Corner fill and end plane. Setup: DEO CornerGapMode.ARC + endPlane. Observe vertical segment
   at the corner and clamped ends; document, do not fix.
8. Timing. Setup: 6-edge chain, CTRL_POINT x3. Observe regen < 1.5 s (DEO: 1.41 s with debug,
   memory driven-offset.md). Retires the budget claim.

## 9. Open decisions

1. Target sampling: reuse the DEO spacing predicate (recommended: identical frame seed) vs a
   separate samples-per-target-edge.
2. End derivatives on freeform runs: undefined (recommended, phase 1) vs exact w' = dot(P',W) +
   h*r, h' = dot(P',H) - w*r from frameRates (utils:2768), ~40 lines.
3. Line/arc reference edges: exact B-spline from evCurveDefinition vs one
   evApproximateBSplineCurve each (recommended: one code path, +1 call per edge).
4. Reference zero by X (arcLengthAtX) vs projection (chainZeroArcAtPoint, D4): keep X in phase 1
   so DEO and evaluate_offset share the identical zero station.
5. Step-edge join tolerance: PROFILE_JOIN_TOL (recommended, what buildProfile joins on) vs
   OFFSET_GEOM_TOL.
