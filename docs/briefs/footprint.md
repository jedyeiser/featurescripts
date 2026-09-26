# Research brief: Footprint family (Explore agent, 2026-09-25)

Working tree 2026-09-25; others have uncommitted footprint/ edits (~42 lines, mostly pins) -> line numbers may shift.

## 0 Overview
Vocabulary (footprint-domain-terms.md, predicates.fs:8-58): RSL = FCP (forebody contact point) to ACP (aftbody
contact point) distance; MRS = RSL midpoint; FB/AB forebody/aftbody; tip/tail = outline beyond contacts; Waist = min
half-width between the widest points; Taper angle = acute angle between centerline and line joining FB and AB widest
points, + when FB wider (fpt_analyze.fs:945-954, fpt_geometry.fs:1423-1431); Natural radius = arc through FB, waist,
AB ("widest" through widest points, "inflection" through inflection points); Average radius = mean of R = 1/|k| at 200
stations evenly spaced in X between FB and AB inflections (fpt_analyze.fs:842-937; user chose 2026-09-25, rejected
L/dtheta); sidecut radius = radius of curvature of the side outline; + curvature = concave waist region, negative
"reverse" = flank toward contacts and tip.
Conventions: footprint in world XY, X along ski, Y half-width, +Y primary side. Only Analyze uses chosen FCP/ACP for
direction; Points and Scale take lower-X RSL end as FCP; Integrate uses the X of the FCP selection.
Chain: 1 Integrate footprint designs a sidecut from a radius profile (sketch plot of radius vs X); 2 Analyze measures
any sidecut; 3 Scale derives a new length (accordion, keep taper, retarget radius); 4 Arc fit -> manufacturable
line/arc chains that show a radius; 5 Generate Footprint Points exports tip/RSL/tail tables (CNC/specs). Integrate and
Scale use approximateSplinesWithPolyArcs from arcFit.fs; Scale imports integrateFootprint.fs (forceQuadraticNurbs),
fpt_geometry.fs (linear solver), fpt_analyze.fs.
Libraries: fpt_analyze.fs (B-spline analysis: waist, widest, inflection finders, natural/average radius, taper,
tip/tail detection); fpt_geometry.fs (radius-profile sampling, exact ODE integrator, theta0 solvers, legacy linear
chain used by Scale Radius); footprint_math.fs (signed planar curvature); predicates.fs (read-only "Footprint Data"
group for Analyze); footprint_config.fs (unused constants; dead); footprintAnalytics.fs (legacy analyzer same name;
nothing imports it now; leave out of user docs).
Onshape: doc "footprint" k2-sports 52724f3a857fa52d3ecceb77, ws a013b85fd726af3c2bdaca71; tabs arcFit 66f4f03c,
fpt_analyze 71d853c0, fpt_geometry 67c190b8, integrateFootprint 5d198387, predicates a54a8297, footprint_math d3ad341f.
Test studio "Footprint tests" via devtools/onshape/build_footprint_tests.py, check_footprint_tests.py. FS 3083 (bumped
2026-09-25).
Family figures: annotated half-footprint (FCP, ACP, MRS, FB/AB widest, inflections, waist, taper line, tip/tail length
arrows); pipeline diagram of 5 features.

## 1 Integrate footprint (integrateFootprint.fs, engine fpt_geometry.fs)
Designer draws RADIUS vs X as sketch edges; default 10 mm plot height = R 1 m (horizontal line at y = 140 mm -> 14 m
constant radius). Integrates curvature twice to the outline; solver places it to hit a waist location or overall taper
angle, and the waist width. Plot above y=0 = positive (concave) waist curvature; below = reverse (flanks). Profile
edges may not cross y = 0 (fpt_geometry.fs:264-268).
Exact model in X: dphi/dx = 1/(R(x) cos phi), dy/dx = tan phi (phi = true tangent angle). Legacy y'' = 1/R is small-angle
-> parabola, not circle. Constant-R sections closed form (:552-560): sin phi = sin phi0 + (x - x0)/R, y = y0 + R(cos
phi0 - cos phi) -> EXACT circular arcs. Transitions: RK4 with R linearly interpolated per step (rk4Step :472-495). Arcs
emitted as analytic 3-point sketch arcs then extracted -> clicking shows a radius (designers require; rational NURBS
never reports a radius).
Dialog (integrateFootprint.fs:70-164): 1 Spline method APPROX (FIT opFitSpline / APPROX approximateSpline); 2 Build
Mode PER_REGION (one curve per same-sign region, or per input edge; arcs always own section :137-187); 3 Unify curves
(false; one curve; ignored with arcs); 4 Strict (false; forces arcs/lines -> rational NURBS, no radius); 5
Waist/Taper Angle Calculations WAIST; 5a Waist location (WAIST) 65 mm, bounds -100..100, ABSOLUTE world X of narrowest
point (fpt_geometry.fs:1437); 5b Overall taper angle (TAPER) 0.25 deg, bounds -0.2..0.5; 6 Waist width 95 mm, 35..300,
full width (min y = width/2 :779-782); 7 Radius Profile(s) (non-construction edges); 8 Contact points: FCP, ACP (FCP X
decides FB side for taper sign; ACP UNUSED); 9 Integration definition: Y-Axis Scaling 10 mm = 1 m ("Defualt" typo; 10,
20, 50, 100 mm per metre; factor 1000/choice :520-538); 10 Spline approximation (APPROX): degree, max CPs 100,
tolerance (also sets solver tol); 11 Debug & details: samples per edge 50 [5..200], Group output true (print options do
nothing); 12 Recalculate? (does nothing).
Outputs: with arcs one composite wire (emitFootprintWithArcs :437-517; arcs analytic sketch arcs, transitions splines;
two-stage extract: sketch arcs -> wire, then wire + splines). Without arcs B-spline bodies extracted to a wire when
Group output and >1 body. +Y side only. No variables, no notices.
Algorithm: sampleRadiusEdges (fpt_geometry.fs:206-400): order edges by X; horizontal line (|tangent.y| < 1e-3) = arc
with radius arcR0 (:270-284); fill X gaps half-and-half by tangent extrapolation; sample R at N even X per edge via
evDistance to X-planes. divideEdgeDataIntoRegions (:119-196). prepExactBase (:447-466) R = value x factor.
solveTheta0Exact (:744-751): TAPER secant on taper residual (:628-673); WAIST outer secant over taper with taper solve
nested until waist X hit (:679-739); each trial re-integrates everything (integrateExact :503-609). y0 so min(y) =
waist/2, integrate once more (:779-785). Arc sections return samples; transitions approximated with end-slope
constraints (createFootprintSplineFromPoints :408). No arcs: refineFootprintBSplines (:1103) shears CPs to close a
~1 mm discrete-vs-continuous gap to < 0.05 mm (skipped with arcs). Emit 3-point skArc through first/middle/last
samples -> two-stage opExtractWires -> delete temps. Stats footprintStatsFromDiscrete (:1339-1440): FB/AB split at
midpoint of X range, widest refined parabolically, waist = min y between widest.
Examples (build_footprint_tests.py:340-375): IF1 one line y = 140 x -550..550, waist 200, waist location 0 -> one exact
arc R 14000, centre (0, 14100); IF2 lines 250/140/250 -> 3 G1 arcs R 25/14/25 m, waist (0, 100); IF3 R14 core + reverse
R3 flanks (y = -30) -> widest at x = +-667.857; IF5 single R14 line centred X = 40000 driven by taper 0.25 deg; IF6
core + sloped 14 -> 25 m transitions -> exact R14000 core + 2 spline transitions.
Figures: radius-profile sketch next to the resulting sidecut (arc vs transition legend); phi/tangent geometry of an
arc step; WAIST solver diagram (outer taper loop containing inner theta0 loop).
Limits: single-section profile probably errors ("need at least 2 samples" when size(samples) < 2 :755-756; samples =
array of sections); memory lists IF1/4/5 XFAIL but EXPECT_FAIL doesn't -- UNCLEAR if fixed. Strict ->
forceQuadraticNurbs + opCreateBSplineCurve = rational NURBS no radius (IF7 XFAIL). Solvers fail to converge silently
(last iterate, no notice). samplingDef reads gapSampleDx, epsR with no dialog params (:200). Unify+Strict never groups.
Waist location default 65 mm absolute X (review asks whether measured from ski centre). Open: implement or remove ACP.
G1 at arc joints depends on end slopes from the solve.

## 2 Analyze footprint (analyzeFootprint.fs, fpt_analyze.fs, predicates.fs)
Measures an existing sidecut; fills read-only "Footprint Data" panel: tip-waist-tail dimension string (full widths),
waist width + location, taper angle (widest points), tip and tail length, average radius, natural radius (widest),
natural radius (inflection), FB/AB widest and inflection distances from contacts.
Dialog (analyzeFootprint.fs:96-117, predicates.fs:6-61): hidden hasTipTail; Footprint edges; RSL line (straight edge);
FCP, ACP (plane/vertex/MC each; contact used = nearest point ON the RSL line to each selection, checkInputData
:215-233); Sketch key points (false). Read-only group: Sidecut dimensions, waist width, waist location, taper; tip and
tail length (only hasTipTail); collapsed "Radius calculations" (average, natural-widest, natural-inflection); collapsed
"Widest & Inflection points" (FB widest, FB inflection, FB inflection->widest, same for AB). Recalculate button
(resetData clears + re-runs).
Outputs: panel values written BY EDITING LOGIC (analyzeEditingLogic :34-90; only existing keys copied :77-87);
reportFeatureInfo recomputed every regen (:144-145) "Tip-waist-tail ... mm | average radius ... | natural radius
(inflection) ..."; optional sketch "Footprint Analysis Sketch" of construction lines (:147-211): waist, both widest,
widest connector, inflections, inflection connector. No variables yet (review proposes rsl, tipWidth, waistWidth,
tailWidth, waistLocation, taperAngle, naturalRadius, averageRadius).
Algorithm: prepareFootprintCurves (fpt_analyze.fs:348-370): edgesToBSplines forceNonRational (:70-88), trim Y >= 0,
curve data, detectTipTail (:262-342: sample for Y ~ 0 beyond FCP/ACP); FB side from FCP X vs MRS (:999-1002); waist
findWaistPoint within +-20% of global X span around MRS, full-range fallback (:1004-1020); widest findWidestPoint each
side of waist (:1022-1046); inflections findInflectionPoint from widest inward to waist for curvature sign change
(:693-814), within each curve (root refine) and at junctions, fallback widest; natural radii arcThroughThreePoints
(circumradius :823-840); average radius 200 midpoint stations in X on spanning curve, exact curvature at interpolated
u (:854-937) -- independent of edge splitting; taper computeTaperAngle(fbWidest, abWidest).
Example (S14 fixture, build_footprint_tests.py:41-45, 196-203): half-waist 50 at x=0; R14 m core to +-550, reverse R3 m
flank to +-750 (contacts), then R0.3 m to y = 0. Hand check: x=550: y = 50 + 14000(1 - cos(asin(550/14000))) = 60.808;
widest at x = 550 + 3000 x 0.03929 = +-667.857, y = 63.124; contact y = 61.999. Expected: inflections +-550; average
14.00 m; natural (inflection) ~14.00 m ((550^2 + 10.808^2)/(2 x 10.808) ~ 14000); taper 0; dimensions ~126.2-100-126.2.
AF3 same chain split into 13 edges -> identical. AF6 asymmetric flanks R2/R4 -> tail wider, taper negative.
Figures: annotated key-point sketch; average radius explainer (stations between inflections, R(x)); natural radius as
3-point circle over the sidecut.
Limits: panel values STALE until dialog edited or Recalculate (info line always current); AF7 XFAIL: raw sketch arcs --
junction inflection test compares curvature signs in each edge's own parameterisation, sketch arcs always CCW (+k),
junction missed, both inflections fall back to widest (workaround: extract to wire first); average vs natural gap
expected (user saw 17 m average vs 14 m natural; natural = waist-weighted 3-point fit); waist search window +-20%
around MRS; prepareFootprintEdges (:256+) and rest after :235 probably dead.

## 3 Scale Footprint (scaleFootprint.fs)
Derives a footprint for a new RSL from a reference (grow a ski family by length). Per side: Accordion (X only; pure X
stretch about FCP; widths unchanged unless target width), Keep taper angle (accordion + rotation about a pin to
restore reference taper), Scale radius (rebuild to hit a target AVERAGE radius keeping taper). Tip/tail translated to
new contacts and Y-scaled to new contact widths.
Dialog (:109-251): 1 Reference footprint edges (all, +Y and -Y); 2 Reference RSL line (FCP = lower-X end,
extractRslData :604); 3 New RSL line; 4 Symmetry Symmetric (mirror +Y) / Asymmetric (each side independently; errors
with no -Y data :300-303); 5 Scale mode Accordion; 5a Pin location (Keep taper) Pin ACP width / MRS; 5b Target average
radius (Scale radius) 21 m, 1..100 m; 6 Spline options: degree (annotation 3), Strict arcs false, tolerance 0.001 mm
[0.001..10], max CPs 30 [4..200] (matter only for Scale radius and Strict); 7 Specify target width (false); 7a Target
waist width (full; Accordion scales Y uniformly, Keep taper/Scale radius shift Y); 8 -Y block (Asymmetric) repeats 5-7;
9 Enforce tangency at tip (FCP) / tail (ACP) (false/false; fixG1 moves one tip/tail CP keeping its distance); 10 Keep
reference curves (true); 11 Debug: Show input/output curve type (green arc, blue spline), Skip wire merge, Print arc
chain.
Concepts: knot-preserving transforms (accordion, rotate, translate, mirror move only CPs; withControlPoints keeps
degree/knots/weights :1592-1607, fixed 2026-09-25). Keep-taper rotation: accordion changes taper tan tau' = dy/(k dx);
rotation R = tau_current - tau_ref about pivot (:1304-1467, sign note :1383-1388). Arc preservation "G1 arc chain":
edge is an arc only if evCurveDefinition is Circle on the raw edge (:279-284); accordion turns circle into ellipse ->
arc runs REFIT as circular arcs through the SCALED junction vertices; each arc through 2 fixed points with inherited
start tangent fully determined (arcFromStartTangent R = |AB|^2/(2 AB.n_A) :2246-2264); its end tangent seeds the next
-> G1 by construction (propagateArcChain :2290); remaining DOF = start tangent phi0 minimising endpoint-weighted squared
tangent deviation from scaled original (weight 8 :454); 41 coarse samples over +-0.5 rad then 40 ternary steps
(:2328-2359); splines absorb G1 at arc-spline seams; emitted as skArc + two-stage opExtractWires per side
(emitScaledSide :2586). Scale radius (scaleRadius :1668+): sample reference curvature uniformly (max(100, 30 x
boundaries)), multiply curvature BETWEEN THE INFLECTIONS ONLY by a factor (taper regions kept), map X to new RSL,
integrate LINEAR (cumTrapz twice), solve theta0 for reference taper (solveTheta0ForDriver :1813), shift y0 to waist,
fit temp curve, find inflections, measure average radius, factor *= R_actual/R_target; max 10 iterations, tol 1 cm
(:1702-1703, :1868-1878); split at mapped original boundaries, approximate each piece.
Outputs: two open wires id+"posOut"+"wire" and id+"negOut"+"wire" extracted separately (never close into a loop;
Analyze expects an open +Y wire). Arcs analytic in Accordion and Keep taper; Scale radius splines only. No variables or
notices (review proposes plusYEdges).
Algorithm (:253-518): RSL data ref/new; edgesToBSplines + Circle flags; categorizeCurvesWithSides (:982) tip/sidecut/
tail per side split at contacts; analyzeReferenceSidecut (:790) waist, widest, taper, average radius, contact widths;
scaleSidecut dispatch (:561-592); transformTipTail (:1988); mirror or asymmetric -Y; optional fixG1;
buildTaggedCurves (arc runs by X-midpoint order, fitArcRun; strict -> approximateSplinesWithPolyArcs analytic arcs);
emitScaledSide +Y/-Y; optional delete reference; debug colouring.
Examples: SF1 Accordion S14 RSL 1500 -> 1650 (k = 1.1): contacts +-825 y 61.999; tip end -924.567 -> -999.567
(translation); waist stays (0, 50); widest x = +-734.64, y 63.124 unchanged; -Y mirrored. SF2 Keep taper ACP pinned on
asymmetric fixture: reference taper and ACP width kept. SF3/SF4 Scale radius S14 -> 18 m. SF5 Accordion of a spline
sidecut = exact X-scaled image within 0.001 mm (knots kept).
Figures: overlay of the 3 modes (reference, accordion, keep-taper rotation about the pin, radius-scaled); G1 arc-chain
construction (fixed vertices, centres collinear with each junction, propagated tangents); Scale-radius iteration loop.
Limits: SF4 XFAIL findInflectionInTempCurve search direction from sign of X (assumes ski centred at X = 0); memory SF3
XFAIL (19.3 m for 18 m target) not in EXPECT_FAIL -- UNCLEAR; Scale radius linear small-angle integration, splines only
(analytic arcs "Phase B"), silent non-convergence; hard-wired world XY and FCP = lower X; findParamAtX falls back to
midpoint on miss (:1164-1209); fixG1 runs BEFORE the arc refit (small mismatch; off by default); arc crossing FCP/ACP
needs arc-aware split (deferred); chain drift toward the tail (tiny for mild scaling).

## 4 Arc fit (arcFit.fs)
Coplanar edges (typically spline sidecut) -> chain of lines and circular arcs within a position tolerance (CAM,
radius callouts). Core approximateSplinesWithPolyArcs also used by Integrate (Strict) and Scale (Strict arcs).
Dialog (:33-58, bounds :11-16): Edges to fit (chain order required; no adjacency walk; orderAndOrientBSplines
pass-through :673-686); Position tolerance 0.01 mm [1e-5..1] (max deviation + G0 join tol); Plane tolerance 0.01 mm
(coplanarity gate, error states deviation, assertCoplanar :516); Minimum segment length 1 mm [0.1..100]; Tangency tol
0.1 deg (NO EFFECT); Validation samples per arc 16 [10..200]; Max subdivision depth 8 [3..12]; Output type Curves /
Sketch / Curves and sketch; Debug (println; description wrongly says "notices").
Algorithm: evApproximateBSplineCurve forceNonRational (:68-74); sample, best-fit plane, coplanarity gate (:107-118);
whole-curve fast path detectWholeCurveArcOrLine (:804) -> one PRESERVED primitive; else seed segments from blocks of 2
knot spans (short block carried into next, no gaps :915-983); fitLineOrArcForSegment (:1067) tries line (chord error <=
tol), BIARC (two G1 arcs matching source tangents at both ends), 3-point arc; subdivideUntilFit (bisect at max error,
maxDepth/minLength); mergeUntilStable (greedy merge of neighbours on same curve; preserved never merged);
emitSketchFromPrimitives (sketch arcs/lines :548) -> extract wire, optionally delete sketch (:147-174). Formulas:
3-point circle; biarc construction (constructBiarc2D :1889); error = real point-to-arc distance at samples.
Outputs: composite wire of analytic arcs and lines and/or sketch; no notices, no fit report.
Examples AR1-AR4: AR1 S14 wire of 5 exact arcs at 0.01 mm -> exactly 5 arcs R 0.3/3/14/3/0.3 m; AR2 spline through 9
points of an R14 arc -> arcs and lines only within 0.01 mm; AR3 S-curve (R14 core to R3 reverse flank); AR4 2 mm knot
spans at the inflection with minLength 10 mm -> no gap (guarded).
Figures: freeform curve with biarc chain + junction dots; biarc construction diagram; subdivision tree.
Limits: Tangency tol dead (cross-edge merge needs a.curveIndex1 == b.curveIndex0 :1425-1443, never holds; no G1 across
input edges; open: implement or delete); too-tight tol -> max depth everywhere ("runs forever"); forced non-rational ->
a true arc passes detection only when posTol loose enough; arc > 180 deg can't be kept whole; selection order matters;
no icon; "Curves and sketch" may return duplicate edges in standard output (untested).

## 5 Generate Footprint Points (getFootprintPoints.fs)
Samples the sidecut into tip, RSL and tail point tables at equal arc-length spacing (CNC/specs/spreadsheets); registers
table type "Footprint Points".
Dialog (:47-111): Geometry Selection Type FACE (default; Footprint face, edges collected by editing logic and body) /
EDGES (edges or wires); RSL Line; Include Points (vertices; DOES NOTHING); Table Formatting (collapsed): units mm / inch
/ cm, sig figs 4 [3..10] (actually DECIMAL PLACES, roundToPrecision), Remove units from table? (true), Number of Tip
Points 23 [3..50] + Flip, Number of RSL Points 80 [10..200] + Flip, Number of Tail Points 23 [3..50] + Flip; Additional
options: Retain Curves ("Tip Curve", "RSL Curve", "Tail Curve"), Retain FCP/ACP Planes, Create Sketch From Points (one
sketch per region at its reference origin).
Algorithm: resolve edges (FACE -> adjacent edges), dissolveWires; FCP/ACP = RSL endpoints swapped so FCP is LOWER X
(:142-147), MRS = RSL at 0.5; FCP/ACP planes (normal X); classify each edge tip/RSL/tail by bbox midpoint (:181-284):
edges at or below Y = 0 skipped; edge spanning Y = 0, FCP or ACP extracted and split (opSplitPart), -Y part
discarded; per-region wires; genPointArray (:408-467) samples each region path (constructPath at range(0, 1, n)), sorts
by X, stores points RELATIVE to region reference (tip -> FCP, RSL -> MRS, tail -> ACP); writes origin attributes
tipEdgePoints, rslEdgePoints, tailEdgePoints, tableFormat (OUTPUT CONTRACT: names/format/order stable; last instance
wins); debug points red/blue/green always; cleanup.
Example GP1: S14 wire, RSL -750..750: 23 tip points x -924.567..-750, 80 RSL points -750..750, 23 tail points; equal
spacing along curve; tip offsets -174.567..0 relative to FCP.
Figures: half-footprint split into 3 regions with sample dots and each region's origin; table screenshot.
Limits: GP2 XFAIL tip at +X -> lower X still FCP, tip and tail swapped; bbox region tests; no icon; ~12 non-ASCII
bytes; question: any consumer outside the repo reads the attributes?

## 6 Cross-cutting unclear
1 FCP convention differs by feature (Analyze selections; Points and Scale lower X; Integrate X of FCP selection with
  "negative X = FB" fallback). 2 XFAIL set: memory AF7, GP2, IF1/4/5, IF7, SF3, SF4; build file only AF7, GP2, IF7, SF4;
  confirm IF1/4/5 and SF3 (code :755 suggests single-section still throws). 3 Scale degree annotation Default 3 but
  POSITIVE_COUNT_BOUNDS controls; unused in Accordion/Keep taper. 4 Integrate taper bounds [-0.2, 0.25, 0.5] deg; review
  proposes [-1, 0.25, 1]. 5 footprintAnalytics.fs still present, import removed. 6 two-stage extract moot. 7 pending
  user decisions: VT keys; tip at +X and non-XY planes; Arc fit G1 vs delete Tangency tol; Integrate ACP; Analyze tip/
  tail length publish.
