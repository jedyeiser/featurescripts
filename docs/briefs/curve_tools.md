# Research brief: Curve tools family (Explore agent, 2026-09-25)

Doc curve_tools 2143812a99089658c704f0bc, ws 4ea63c7ef14610f212917c42 (k2-sports). Tabs: curve_core 02d7784437f621c76397f0d6,
clean_wire ad14cae7, map_curve 83aa07e2, merge_curve 58446952, evaluate_profiles 707d2062, fillet_wire c61139f6,
curve_tools_tests 84ae001c. Versions V2 9e831639 (curve_core pin used by driven_offset), V3 72e4d01b. Studios: "Tests"
974a3d1e, "Fillet wire tests", "Evaluate profiles tests" 33fb1352, "Part Studio 1" examples (Clean wire Auto SW_SHELF
13 runs; Map curve TO_EDGES SKI_PROFILE -> RSL zero at FCP vertex; Merge curve RD Baseline tip).
Theme: turn the curves a ski model produces (intersection wires, projections, outlines, hand-built chains) into clean,
light, well-behaved wires; lofts/fills/driven offsets fail or wiggle on slivers, near-tangent kinks, rational splines,
CP bloat.
Shared (curve_core.fs): Chain = G0 edges ordered as links, arc length increasing with world X (:10-19, buildChain
:488); batched eval; exact emitters: lines degree 1, arcs as SKETCH arcs so they keep a radius (emitArcCurve :1137),
else chord-length fit (approximateFamily :1215; end derivative scaled by total chord -- a unit vector bulges ~0.45 mm
:1222-1227); standard end outputs (wireEnds :1396): startVertex/endVertex/startEdge/endEdge/breakVertices/orderedEdges
(all except Fillet wire, Evaluate profiles); notice policy: only regenError turns red, expected outcomes Info, missing
shape Warning.
Family figure: one rout-intersection wire raw (32 edges, slivers, kinks) -> Clean wire (few runs, corners kept) -> a
Fillet wire corner.

## 1 Clean wire (clean_wire.fs, 2145 lines)
Rebuilds one OPEN wire with fewer edges/CPs: tangent stretches -> one spline each, real corners stay vertices, exact
lines/arcs pass through. Automates manual "Wire_Smoothing" (split, Edit curve Approximate per piece, Composite;
research_clean_wire.md 1). Native route makes everything rational, samples 200 uniform points (0.1 mm sliver gets
zero), ignores corners, under-fits silently at the CP cap.
Joint classification by turn angle (jointRecord :1373): <= G1_JUNCTION_ANGLE (0.01 rad ~0.57 deg) tangent, merged;
<= corner angle "near" (merged if Make nearly tangent joints tangent, else corner); larger corner, vertex kept.
Sliver rule (classifyJoints :1225-1350; cleanwire-sliver-rule.md): edge < sliver length judged by its own two joints:
corner+tangent -> absorbed into tangent side; both corners -> chamfer, kept; both tangent -> by turn across it: fillet
at a corner kept; neighbours that line up absorb it unless offset ("jog", kept). Exact LINE/CIRCLE >= sliver length
always own run (:1245-1252).
Adaptive sampling (runSamples :1644, spacingFor :1712): sagitta kappa s^2/8; half the tol -> s = sqrt(4 tol/kappa),
clamped [0.05 mm, min(10 mm, L/2)], 3-200 samples per edge at equal cumulative density.
Figures: joint-angle number line (tangent | near | corner) + the three sliver cases; sample density thickening at a bend.
Dialog (:131-310): Mode Manual (default) / Auto; Wire (wire body or edges of one chain); Name. Auto: Runs read-only
array (Edges label, Kind "exact copy"/"fit", Control points, Deviation; editing logic fills on wire/mode/corner
angle/tangency/tol/sliver change :553-616; regen writes CPs + deviation reportAutoRuns :755); Corner angle (3 deg,
0.1-90; keeps 4.8 deg ramp creases); Make nearly tangent joints tangent (true); Sliver length (0.5 mm, 0.01-20; not
tied to tolerance :91-97); Fit control points to tolerance button (fits every run uncapped, sets global max CP to
largest need :625). Manual: Auto-populate groups button (one MAX_CP group per fitted run, robust queries, "Run k @ x,
y" :678); Groups array (Edges, Name, Approximation Global / Max control points / Tolerance + value; group ends always
breaks; corners inside fitted through + reported); Break at (vertices or MCs). Always: Approximation parameters
(degree 3, tol 0.01 mm, max CPs 30); Maximum deviation read-only (evMaxPathDeviation vs source); Show deviation
(comb); Show runs; Delete input wire (whole wire bodies only); Projection: Project onto plane -> Plane, Plan degree 3,
Plan tol 0.01, Plan max CPs 30, Keep projected surface (true), Keep projected wire (true) -- cleans plan view, extrudes
a wall along plane normal, opDropCurve the 3D wire onto it (exactly on plan, keeps heights) (:350-438); Reduction
read-only (CPs before/after/%, edges before/after/%, tangency fixes); Debug: Show groups, Print joints, Print runs,
Show corners, Show control polygons, Keep pieces.
Outputs: one wire; run ids count from chain start (references survive upstream edits keeping the corner set). Keys
curveCount, inputCount, maxDeviation, start/end vertex+edge, breakVertices, run1..N (:486-509). WARNING "Short of
tolerance: ..." (deviation > 1.0001 tol; raise CPs or split, :1955); INFO "N edge(s) -> M; K corner(s) inside a group
were fitted through"; WARNING "...came out as N bodies instead of one. Gaps between runs: ..."; projection warnings
(:404, :421); errors closed loops (:321), not one chain (:1142), group not in wire / not contiguous / overlapping
(:1478-1495), "Break and group conflict" (:1532), break not on a vertex (:1434), rational fit (:1818); console NOTE
fragment absorbed.
Algorithm: describeChain (:1133) -> classifyJoints -> (Manual) applyBreaks (:1402), applyGroups (:1451) -> buildRuns
(:1548; all-sliver run joins previous unless leading break is corner/user break) -> emitRun (:1736: exact copy via
opExtractWires, or emitFittedRun :1799 with kernel end tangents) -> opExtractWires one wire (cleanChain :776) ->
deviation -> projection -> reports -> publish.
Examples: Pinch_intersection_Curve (tail_notch/Slotted/Baseline) Auto at 15 CPs: 32 edges -> 8 runs, six in tol, tail
bend and tip at cap (0.106 / 0.215 mm); manual recipe 11 edges CPs 8/20/15/6/8/15. Sliver fix: 0.0714 / 0.0834 mm
fragments caused Cap_Wall wiggle; seed 11 -> 9 edges, corners 11.3233 / 11.319 deg kept. Test "Clean wire: ends / runs
in order" (curve_tools_tests.fs:126-160): polyline (0,0)->(100,0)->(200,50)->(300,50) default Manual: ends (0,0,0),
(300,50,0), breaks = runs - 1.
Gotchas: closed loops refused; arc recognition inside splines not built; Manual groups reference source edges by
identity (stale after upstream renumbering; Auto-populate recovers); Runs array refreshes only via editing logic; in
Manual every joint is already a break (:1256-1262) so Break at mostly a conflict check -- say plainly; downstream
should reference the body or keys, never its edges.

## 2 Map curve (map_curve.fs, 1234 lines)
Carries a span from FROM chain onto TO chain preserving arc length from a reference: s mm along from-chain from its
zero lands s mm along to-chain from its zero (:13-20). Ski: same stations on core bottom and sidecut from an MC at FCP.
No native equivalent. Span s0 = -zeroArc_from, s1 = L_from - zeroArc_from; t0,1 = zeroArc_to + s0,1 (:216-220).
World-X projection: zero lands where the chain crosses the point's X (arcLengthAtX), fallback closest point.
Figures: two ski curves of different length with FCP marker, equal arc-length ticks from zero on both; World-X vs
closest point at a tip.
Dialog (:121-185): Mode Deform from-edges onto to-edges (FROM_EDGES, default) / Trim to-edges to the from span
(TO_EDGES) / Deform and merge to one curve (SINGLE); From edges, To edges (to-edges never modified); Reference point
Shared (default: Zero point MC or vertex) / Separate (From zero point, To zero point); Projection World X (default) /
Closest point; Flip to-chain (false; needed when chains point opposite at the zero); Spacing & approximation (not
TO_EDGES): Offset spacing CP multiplier (3) / points per edge (25) / spacing (10 mm); Approximation degree 3, tol 0.01
mm, max CPs 15; Output (FROM_EDGES) One wire per link (default) / One body per run; Name; Debug: Show chain ends
(green/red, blue zero arrows), Print summary. Defaults map for API callers (:288-308).
Outputs: new wire(s); keys spanStart, spanEnd, toStart, toEnd, stationCount + end queries (:270-287). INFO "The
from/to-edges do not span the zero point's X; closest point used" (:480); INFO "Merged result is a spline; arc radius
... not preserved" (SINGLE :1052); errors "chains point in opposite directions ... Tick Flip to-chain" (:211),
"from-edges run X mm past the START/END of the to-edges..." (checkSpan :556), "map to no span" (:235), "No part of the
to-edges lies within the from span" (:1141).
Algorithm: resolveZeroPoints (:384), resolveChain (:417; World-X spanned test + fallback; reverseChain :489);
direction check dot of tangents at zero (frameAtArc :642); span check; TO_EDGES trimToEdges (:1074): copy to-edges
opExtractWires, opSplitEdges at arc fractions, keep pieces with mid-arc in [t0, t1], re-extract -- never refits, arcs
stay arcs; others: chainStations -> mapSamples (:719; to-chain vertices inserted as sample pairs) -> resolveSamples ->
buildRuns (:868) -> emitRuns (:920) line / sketch arc / spline; SINGLE mergeEmitted (:1021) via merge_curve core.
Example: test "Map curve (trim to-edges): ends" (curve_tools_tests.fs:192-217): to-chain (0,2000)->(100,2000)->
(200,2050); from-chain x 50..150 at y 2200; shared zero at x 100 -> output from (50,2000) to 50 mm along the diagonal
from (100,2000) ~ (144.72, 2022.36).
Gotchas: Phase 1 only (:22-24): from-edges taken on their own chain, (w,h) = 0 -> FROM_EDGES currently outputs the
to-chain over the from span, resampled/refitted = a refitted TO_EDGES; off-chain deformation with (1 - h kappa) and
LengthPreservation is phase 2. opSplitEdges on wire edges and World-X box over vertices marked UNVERIFIED (:429,
:1068). X in the gap between links errors.

## 3 Merge curve (merge_curve.fs, 827 lines)
Absorbs one G0-adjacent edge into a seed edge as a single fitted spline, controlling which body survives. std Edit
curve / Composite + Approximate pipeline (constructPath -> makeApproximationTarget -> approximateSpline ->
opCreateBSplineCurve -> opEditCurve) + wire-identity logic (:10-49). No kernel join; every route is fit-then-replace;
opEditCurve replaces a whole wire body's curve, so in-place only when the seed is the sole edge of its wire.
Figure: 2x2 grid of membership scenarios and which body survives.
Dialog (:200-239): Seed edge (edited); Merge edge (meets seed end to end); Keep start derivative, Keep end derivative
(true); Approximation: Degree, Tolerance (std bounds), Max CPs (100 :60-63); Output name (editing logic prefills seed
wire's name, mergeCurveEditLogic :665); Note arc loss (true); Debug Print diagnostics.
Scenarios (planMergeTarget :535, placeMergedCurve :570): IN_PLACE (seed wire = only the seed: opEditCurve, body keeps
id and name; merge edge's own single-edge wire deleted); REBUILD (seed wire has other edges: those + merged curve
extracted into new wire(s), attributes copied, old body deleted; INFO "Rebuilt the seed wire ... original body was
replaced" :635); EXTRACT (seed not on a wire: fitted spline body is the output). Ends snapped to neighbours' exact
endpoints (opExtractWires chains only on exact coincidence; neighbourSnapPoints :748). Keys degree,
controlPointCount, mergedEdge + ends (:357-374). INFO zero-length edges absorbed (:289), arc radius not preserved
(:296); errors exactly one seed/merge, must differ, not G0 connected, fitted curve resolved to N edges.
Example: test (curve_tools_tests.fs:166-186): wire (0,1000)->(100,1000)->(200,1050), degree 3, 0.01 mm, 30 CPs -> ends
(0,1000), (200,1050), mergedEdge single edge. Ski example "RD Baseline tip" in Part Studio 1.
Gotchas: always NURBS (lines/arcs lose type); opEditCurve @internal; defaults map (:375-381) omits approximation
Degree/Tolerance/MaxCPs (API callers must pass).

## 4 Evaluate profiles (evaluate_profiles.fs, 2365 lines)
Two jobs: Edges = project a chain onto a planar face, trim where the projection doubles back, emit a clean wire;
Part/Surface = outline a body onto a face and split the silhouette into Top, Bottom, Middle (+ connectors) relative to
a prevailing direction. Ski: side-profile curves of a core/topsheet (SW_Profile feeding a Fill). Native Project/Outline
gives an undivided loop with slivers.
Reversal scan (scanForReversals :2043): 400 samples, dot(tangent, heading) outward from the middle, cut at first sign
change each side; refined by 24 interleaved bisection steps (refineReversals :2117); beyond the cusp the source
tangent is normal to the plane. Profile split (partProfiles :424): edge "along" when |cos| >= 0.7071 to heading;
contiguous edges -> groups; two longest along-groups = profiles; rest connectors; Top = higher along "up across" =
cross(normal, heading) oriented to world Z, then Y, then X (upwardAcross :882, pickProfiles :1050). Middle = average
of top and bottom at matching stations along the heading (not parameters), over overlap only (middleProfile :1307).
Figures: projected chain folding back at the tip with cut point; outline with top (green), bottom (red), middle (blue),
connectors (magenta) matching debug colours :257.
Dialog (:189-260): Profile from Edges (default) / Part; Name (Part outputs "<name> top" etc.); Edges: Edges to project;
Part: Part or surface to outline (solid/sheet body or sheet face), Return Top / Bottom / Middle / Periphery / All
profiles (default) / Full (profiles + each connector), Merge edges shorter than (0.01 mm, 0-10, 0 = off); Project onto
(planar face); Prevailing direction + Flip (hidden for Part + Periphery; fallback chord for Edges, longest in-plane
extent for Part); Output One curve (default) / One per input curve / Efficient; Approximation Samples per curve 60,
degree 3, tol 0.01 mm, max CPs 24; Debug Print scan, Print curve detail, Show profiles.
Outputs: id+"profile" (Edges) or top/bottom/middle/periphery/connectorN. Keys length, trimmedStart, trimmedEnd,
queries top, bottom, middle, periphery, connectors (empty when not requested) (publishProfiles :287). INFO "Merged N
outline edge(s) shorter than ...: <len> at (x,y,z)" (:487); errors "Nothing projected" (:366), "not a single connected
chain" (:376), edge-on surface "Project its edges instead..." (:439), "no outline" (:455), "does not separate into a
top and a bottom profile" (:1080), direction along projection normal (:2017), two merge failures telling user to set
merge length 0 (:764, :783).
Short-edge merge (mergeShortEdges :640): collapses a run of short edges to its midpoint and rebuilds neighbours: lines
stay lines, arcs stay arcs (sketch), splines get end CP moved.
Examples (build_evaluate_profiles_tests.py): P1 crowned sheet (arc 3 mm high across y +-50, 1000 long) by its face onto
Front, All -> top z 3, bottom z 0, middle z 1.5, each 1000 long; P2 same as body, Periphery -> one loop 2006 long; P3
flat sheet onto Front -> edge-on ERROR with message; P4 flat sheet onto Top, Periphery -> 1000x100 rectangle 2200 long;
P5 100x20 step part with 0.0017 mm step, default merge -> 5 edges; P6 Fill on P5 succeeds area 2000.0935; P7 merge 0 ->
6 edges; P8 Fill on P7 ERROR. Real: RD 20FOU 28 SW_Fill failed on a 1.7 um outline edge; bridged -> 11692.49 mm2. No
check script in repo (build script docstring names check_evaluate_profiles_tests.py).
Gotchas: Edges path emitGrouping (:2215) always fits one curve whatever Output says (comments :2210-2213 and enum doc
:123-128 claim otherwise); PER_CURVE / Efficient only in the Part path (emitFromEdges :1436, buildMergedRuns :1529);
no defaults map (:280); top/bottom with no overlap -> console WARNING only (:1323); opCreateOutline @internal;
multi-loop silhouette keeps only longest; header (:13-40) is original stub spec.

## 5 Fillet wire (fillet_wire.fs, 835 lines)
Rounds chosen G0 corners of a wire/edge chain with a radius. Tangent = exact arc emitted as sketch arc (Onshape
measures radius); Curvature = G2 blend with average curvature 1/r. Wire bodies edited in place; edges -> new wire. No
native wire fillet (sketch fillet only in sketches).
Tangent (solveArc :470): straight-edge setback d = r tan(theta/2); Gauss-Newton on (dA, dB) makes the two inward-offset
centres coincide; rational quadratic weights [1, cos(sweep/2), 1] rebuilt as skArc (sketchArc :762); non-coplanar
corners skipped. Curvature (solveBlend :538): std computeBridgingControlPoints matches position, tangent, curvature;
equal setback d bisected (60 iterations) until turn/length = 1/r.
Figures: corner with setbacks, centre r inward, tangent points; tangent arc vs curvature blend with comb (arc comb
jumps, blend rises smoothly).
Dialog (:49-96): Curves (edges or wires); Continuity Tangent (default) / Curvature; Radius (std BLEND_BOUNDS; not in
defaults map); Apply to all corners (false); Corners array (Corner vertex, Own radius (false), Radius cornerRadius)
filled by clicking points (TOGGLE_POINTS manipulator, filletWireManipulatorChange :264, robust vertex queries); Join
into one wire (false; wire input only); Advanced: Corner angle (0.5 deg, 0.001-90), Print corners. Corner detection
(chainCorners :391): turn > corner angle and < 179 deg; closed chain seam included. Display: fillets magenta; corner
points magenta when filleted, blue when not.
Outputs: in place: wire split at every tangent point by a tiny extruded sheet (half-width min(0.1 mm, 0.25 setback);
construction planes cut infinitely, cuttingSheet :803); each two-edge corner piece replaced via opEditCurve; untouched
pieces keep identity -> several wire bodies unless Join. Edge input: copied (opExtractWires), always joined (filletChain
:678). Keys cornerCount, filletCount, skippedCount, filletEdges (no start/end keys :195-207). WARNING "N corner(s) not
filleted: ..." (radius too large; no circle touches both edges; "edges do not lie in one plane ... (use Curvature)";
edges too short; "their fillets overlap on the edge between them" rejectOverlaps :641); INFO "N corner(s) found. Click
them..." when nothing filleted; errors wrong piece count (another part of the wire within the sheet half-width :714),
internal isolation.
Examples (check_fillet_wire.py, "Fillet wire tests" W1-W6): W1 1 wire, length 291.41593, 2 fillets curvature 0.1/mm
(r10), G1, true arcs (300 mm polyline with two 90 deg corners: 300 - 4r + pi r); W2 5 pieces, radii 5 and 10 (per-corner
override), corner (100,400) still sharp, all 5 traced to original; W3 line-to-arc corner r10 tangent to both; W4 radius
too big 0/1 skipped; W5 Curvature mode average curvature 0.1/mm, G2 both ends; W6 overlap 1 filleted / 1 refused. User
later edited W2 (join, 3 corners) and W6 (Curvature r30) -> those checks fail by design, realignment pending user.
Open: manipulator clicking not tried live; design promised unfilletable corners shown suppressed (suppressedIndices
always [] :137), per-corner achieved radius, start/end keys -- none; curvature fillets are B-splines; imports VT V2
(others V1); param named cornerRadius (duplicate-parameter compile error).

## Cross-cutting
1 research_clean_wire.md predates code: corner default 5 deg there, 3 in code; sliver max(0.5 mm, 50 tol) there, fixed
  0.5 param in code; Manual ungrouped edges auto-classified there, copied unchanged in code; header :52-53 lists
  "Suggest groups" not done but Auto-populate exists.
2 research_map_curve.md: direction mismatch warning (code errors); WireOutput SINGLE (code PER_LINK / PER_RUN; SINGLE is
  a mode).
3 research_merge_curve.md arc loss warning; code Info.
4 Design_Master Clean wire on Curve_tools V2; bump to V3 pending (DOCUMENT_MAP.md:168).
5 Tools review 2026-09-25 proposes trimming Clean wire run1..N / curveCount / inputCount / maxDeviation keys.
6 curve_tools_tests.fs is a harness (4 checks), conversion offered, no answer.
