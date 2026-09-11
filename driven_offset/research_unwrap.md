# unwrap -- research

Abbreviations: utils = driven_offset/edge_offset_utils.fs, DEO = driven_offset/driven_edge_offset.fs,
CM = curveMapping/curveMappingCore.fs. Shared enums and predicates: research_shared_vocabulary.md.

## 1. Purpose and requirements (stub: driven_offset/unwrap.fs:4-11)

Inputs: edges to unwrap, a wrapped reference curve, a wrapped alignment point (mate connector),
an unwrapped origin (mate connector). Unwrap the edges preserving length along the wrapped
reference; test each unwrapped edge for circularity and report how arc-like it is, including
whether replacing it by an arc moves its end tangents. The stub asks whether unwrapping control
points would do for the test; section 4 says no and gives the cheap correct alternative.
utils:5 already names unwrap as a client of this document; no cross-document imports (utils:7).

## 2. Geometry

Forward wrap (wrapAndLoft/wrapOntoAndLoft.fs:277-294): flat (x, y, z) -> path parameter
s = sRef + (x - xRef)/L, then P = C(s) + y*Y + z*N(s) with N = (-Tz, 0, Tx). Generalised in
CM:1148 mapSinglePoint: project onto the from-path (projectOntoFrenetPath CM:1039), shift arc
by mappedArc (CM:536), rebuild in the to-frame. Unwrap is the same map with from = wrapped
reference and to = a straight line, i.e. the inverse of the wrap.

In utils vocabulary the unwrap of one point already exists: referenceSurfaceCoords(alongRef, P)
(utils:2009) returns (arc, v, height), and referenceSurfacePoint (:2035) is its stated inverse
(:2033). The reference surface is a planar curve swept along its plane normal, a cylinder
(:1994-2000), so (arc, v) is a flat chart and unwrapping is an isometry of the surface itself.
Flat point: F = O + X*Xmc + v*Ymc + height*Zmc, where (O, Xmc, Ymc, Zmc) is the unwrapped
origin's full CoordSystem (evMateConnector std/evaluate.fs:1106; evZeroPoint :219 keeps only
.origin and is not enough). Axis roles match OffsetFrameAlignment.ALONG: X along, Y width
(plane normal), Z height (:1832 flips height toward +Z, :533 width toward +Y).

Length semantic, enum LengthPreservation (D3):
- REFERENCE_ARC (default, per stub): X = arc(P) - arc(alignment point).
- PARALLEL_ARC: X = alongCoordinate-style s - h*theta(s) (utils:2123) with delta := the point's
  own h, minus the same at the alignment point. A point at normal distance h travels along the
  parallel curve, whose length is s - h*theta(s) (CM:494-519); the discrepancy is
  h*(theta(s) - theta(align)): 16 mm for a point 20 mm off a reference turning 0.8 rad.
  CM_PARALLEL_ARC (CM:520) ships off for the same reason the stub default is reference arc.
Invertibility: the map is one-to-one where 1 - h*kappa(s) > 0, the surface margin DEO checks
at :489-490 and the denominator surfaceOffsetTangent uses (utils:686). Below it the point is
past the centre of curvature and two arcs claim it; unwrap must throw with the arc position.
Normal sign: use referenceBasisAtArc.normal unflipped (:1970-1975), not the +Z flip of
referenceHeightAxisAt (:1832): at a vertical ski tip normal[2] crosses zero and the flip would
invert height mid-chain. planeNormal orientation is fixed once (:2106-2114).

## 3. Algorithm

1. Points. alignPoint = evZeroPoint(wrappedAlignment) (utils:219). originFrame =
   evMateConnector(unwrappedOrigin) (std/evaluate.fs:1106); a vertex falls back to world axes.
2. Reference chain. buildChain(wrappedReference, alignPoint) (:729) with zeroArc by projection
   (chainZeroArcAtPoint, D4: one evDistance, std/evaluate.fs:410, arc = startArc + (flipped ?
   1 - p : p)*length via edgeParam :870) instead of arcLengthAtX (:779, X-only, throws :936).
3. Tables. buildAlongReference(..., delta = 0) (:1703) in an arc-keyed mode (D4): arcs, thetas,
   curvatures, points, tangents, planeNormal; skip xs/arcSlopes and the X guard (:1764). Keep
   the "straight everywhere" guard (:2106): a straight reference has no plane and needs no
   unwrap. Rebase arcs and thetas at alignPoint's arc (:1783-1788 does it by X today).
4. Targets. buildChain(targetEdges) with zeroArc 0; chainStations (:990) for origin, tangent,
   linkIndex, edgeIndex. Keep the evApproximateBSplineCurve per edge (:970) for section 4.
5. Per sample (pure math). (arc, v, h) = referenceSurfaceCoords variant (D4): seed at the
   nearest alongRef.points entry (warm-started from the previous sample, projectOntoFrenetPath
   CM:1047 pattern), Newton on dot(P - A(u), t(u)) = 0 with slope -(scale - curvature*h),
   iterate to |residual| < 1e-9 m. Check 1 - h*kappa > 0. X per LengthPreservation.
   F = O + X*Xmc + v*Ymc + h*Zmc.
6. Flat tangent (for the report and end derivatives): T_flat = dot(T, t)/(scale - kappa*h)*Xmc
   + dot(T, planeNormal)*Ymc + dot(T, n)*Zmc, the surfaceOffsetTangent decomposition
   (utils:686-698) with source slopes zero; normalize.
7. Runs. buildRuns (DEO:536, exported) at target edge boundaries; a target G0 corner stays G0.
8. Classify per run (section 4) -> emitLineCurve / emitArcCurve / emitSplineCurve (utils:2500,
   :2518, :2543) with the flat end tangents as derivatives; joinOutput: opExtractWires per link,
   delete curves (DEO:891-903); nameOutput (DEO:919, exported).
9. Report per run: kind, max deviation, end-tangent deviation (section 4) via println, or a
   debug table; batched segments only (offset_debug.fs:348).
Kernel budget: reference chain ~43 + sampleTurning 6 (:2062) + target chain ~43 + 1 evDistance
for the alignment point + emit ~15 = ~110, station-independent. evDistance per sample instead
of step 5 would add 180-1200 (raw report 06 follow-up); rejected.

## 4. Circularity test

Not on control points (D3): unwrap is not affine, and B-spline evaluation commutes only with
affine maps (evaluate_profiles.fs:114-117 relies on exactly that for plane projection). Even an
exact arc has its control polygon off the curve (rational quadratic middle CP at
R/cos(theta/2)), so classifyPoints on CPs calls a true arc freeform; conversely CPs that land
on a circle do not make the curve circular. The test must see on-curve points.
Greville sampling: for the kept per-edge curve (degree p, knots t), xi_i = (t_{i+1} + ... +
t_{i+p})/p, one abscissa per control point (~8 lines; nothing in tools/ provides it);
evaluateSpline (std/splineUtils.fs:87) at the xi_i plus both ends gives on-curve points with
the control-point density, dense where the curve is complex. Unwrap them with step 5 and run
classifyPoints(flatPoints, approximationTolerance) (utils:380): line if lateral deviation from
the chord <= tol (:391-411), else circle through first/middle/last (circleThrough :329) and arc
if every point is within tol of it (:413-434), else freeform. One tolerance, one vocabulary
with DEO; the run's own stations use the same tolerance for the fit.
Tangent deviation: an arc verdict replaces the curve by emitArcCurve (:2518), which has no
tangent constraint. Report angleBetween (std/vector.fs:230) of the arc end tangent
(cross(fit.normal, end - center), sign by run direction) against the flat end tangent from
step 6, at both ends; flag > G1_JUNCTION_ANGLE (utils:81, 1e-2 rad) the way terminateEnd
reports squareness (offset_run_treatment.fs:291). Bound: ~4*tol/chord rad per end.

## 5. Reuse map and required shared-code fixes

| Step | Helper | Where | Status |
|---|---|---|---|
| points | evZeroPoint, evMateConnector | utils:219, std/evaluate.fs:1106 | as-is; new evOriginFrame (~10 lines) |
| chains | buildChain, expandEdgeQuery, edgeParam | utils:729, :205, :870 | as-is + projection zero (D4) |
| tables | buildAlongReference, sampleTurning | utils:1703, :2062 | fix (D4: arc-keyed mode) |
| lookups | referencePointAtArc, referenceBasisAtArc, alongCoordinate | utils:1936, :1977, :2123 | as-is |
| point unwrap | referenceSurfaceCoords | utils:2009 | fix (D4: seed + denominator + iterate) |
| stations | chainStations, stationCount | utils:990, :948 | as-is; retain curve from :970 |
| runs, arc -> edge | buildRuns, edgeAtArc | DEO:536, :736 | export (D5) |
| Greville samples | grevilleAbscissae + evaluateSpline | -- | new (~25 lines) |
| classify, emit | classifyPoints, emitLine/Arc/SplineCurve | utils:380, :2500, :2518, :2543 | as-is |
| wires, naming | opExtractWires pattern, nameOutput | DEO:891-903, :919 | export nameOutput (D5) |
| debug | drawDebugSegments, debugStride | offset_debug.fs:348, :372 | export |
| prior art | mappedArc, projectOntoFrenetPath | CM:536, :1039 | reference only (no import, utils:7) |

| Fix (D4) | File:line | Why |
|---|---|---|
| Newton denominator scale - curvature*height; iterate to residual | utils:2019 (comment :2004-2006; full form :686) | d/du of dot(P - A(u), t(u)) is -(scale - kappa*h); dropping kappa*h leaves ~h*kappa/scale residual per step; REFERENCE_FOOT_STEPS = 2 (:57) is tuned for on-surface points |
| Arc-seeded projection (nearest table point, warm start) | utils:2011 (referenceArcAtX :1870) | X seed degenerates where the reference is vertical (ski tip), as :2005-2007 admits |
| buildAlongReference arc-keyed mode | utils:1764 (throw), :1783 (rebase by X) | vertical tip has tangent[0] = 0 -> slope < 1e-6 -> throw; rebase must use the alignment arc |
| chainZeroArcAtPoint | utils:779, :884-937 | alignment MC on a curved tip is not resolvable by X; projection is 1 evDistance |

## 6. Parameters

| Name | Type | Shared with DEO | Notes |
|---|---|---|---|
| targetEdges | Query (EDGE or WIRE) | new; same name as evaluate_offset | edges to unwrap |
| wrappedReference | Query | = referenceWire filter (DEO:59-60) | one chain, must be curved |
| wrappedAlignment | Query (MC or VERTEX) | = offsetRefPoint role (DEO:109) | arc zero on the reference |
| unwrappedOrigin | Query (MC; VERTEX fallback) | new | full frame: X along, Y width, Z height |
| lengthPreservation | LengthPreservation (new enum) | no | REFERENCE_ARC default |
| edgeOffsetSpacingDef + 3 fields | OffsetPointSpacing | yes | offsetSpacingPredicate (new) |
| approximationDegree/Tolerance/MaxCPs | int, length, int | yes | offsetApproximationPredicate (DEO:253, export); arc tol = approximationTolerance |
| joinOutput, outputName | boolean, string | yes (DEO:135) | |
| debug: drawFeet, printRuns | boolean | partly | |
frameAlignment is not exposed: only ALONG is meaningful (D3).

## 7. New code estimate

D4 fixes ~60 lines in utils (Newton ~15, arc-keyed tables ~40, chainZeroArcAtPoint ~30 shared
with map_curve); grevilleAbscissae + sampler ~25; evOriginFrame ~10; unwrap.fs parameters,
precondition, steps 1-9, report ~300-350. Total ~450-500 lines.

## 8. Risks and Onshape tests

1. Sign conventions (h, v, X direction) are the likeliest bug. Setup: wrap a flat sketch with
   wrapAndLoft, unwrap it with the same alignment. Observe overlay on the original flat sketch
   within approximationTolerance. Retires steps 2-5 and the D4 fixes together.
2. Vertical ski tip. Setup: reference whose tangent passes through vertical; alignment MC on
   the tip. Observe no throw, monotone X. Retires the arc-keyed tables and arc seeding.
3. Newton denominator. Setup: point 20 mm off a reference of radius 60 mm. Observe residual
   < 1e-9 m and one-to-one behaviour; then 70 mm off expects the margin regenError.
4. PARALLEL_ARC vs REFERENCE_ARC. Setup: 20 mm offset, 0.8 rad turn. Observe X differs by
   ~16 mm at the far end (CM:513-516 figure).
5. Circularity. Setup: unwrap a true wrapped arc and a near-arc spline. Observe arc / freeform
   verdicts and a tangent deviation < G1_JUNCTION_ANGLE for the true arc. Retires section 4.
6. evApproximateBSplineCurve may return rational or high-CP curves (UNVERIFIED); Greville count
   then differs from expectation but the test stays valid. Observe sample counts in the report.
7. Timing. Setup: 6-edge target, CTRL_POINT x3. Observe regen ~1 s; retires the budget line.

## 9. Open decisions

1. Unwrapped origin as MC full frame (recommended, D3) vs origin + world axes: world axes make
   Y-width fail for a reference not in the XZ plane.
2. Alignment MC off the reference: project (recommended) vs reject; report the projection
   distance so an off-surface MC is visible.
3. Report channel: println (recommended, phase 1) vs feature table; keep one record shape
   like withTerminalRecord (offset_run_treatment.fs) so a table can follow.
4. Emit arcs only when tangent deviation < G1_JUNCTION_ANGLE (recommended) vs whenever
   classifyPoints says arc; otherwise emit freeform and report "arc-like, tangents differ".
5. Share chainZeroArcAtPoint with evaluate_offset and map_curve behind a RefPointMode /
   ProjectionMode enum (research_shared_vocabulary.md) rather than an unwrap-only path.
