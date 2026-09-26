# Research brief: Gordon surface family (Explore agent, 2026-09-25)

READ FIRST: working copy ahead of review and HEAD (all gordonSurface files modified, uncommitted: FS 2892->3083 bump +
MCE fixes withUnitDomain, forceNonRational). Several 09-25 review findings already fixed in working copy. Document the
code as it is now; re-check before publishing (files are being edited). tools/README.md does NOT exist.

## Family overview
NURBS curve/surface tools for freeform surfacing that native Onshape does poorly. Author note (gordonCurveCompat.fs:1-4):
written for Elevate Outdoor Collective, "specifically snowboard binding surfacing", following Piegl & Tiller.
vs native: Loft interpolates one family of sections (guides but no exact bi-directional network); Boundary surface =
edges only; Fit spline = curves. Gordon Surface passes through a whole UxV network incl. interior curves. Simplify
surface rebuilds heavy faces with fewer CPs within tolerance. Pull surface = missing CP/cage push-pull. Scaled Curve /
Modify curve end / Interior curves build and edit the networks.
Document: gordonSurface https://k2-sports.onshape.com/documents/d0950ed72a894fbe35c27d43/w/55351e2bc9e8b4ec141bf4b6/e/51c4e18aab64c557561f2b92
(featurescriptSettings.json:151-159); no .document.json ("local only"). Regression studio "Gordon tools tests" by
devtools/onshape/build_gordon_tests.py, check_gordon_tests.py.
Maturity: CLAUDE.md says "(archived)"; review curves.md:327 says "in heavy use", asks to return to active maintenance;
verdict curves.md:218 "None of the three features [Scaled Curve, Pull surface, MCE] is production quality." Tests all
pass; P1/P2 pull surface XFAIL.
Helpers: constEnums.fs (enums BlendMode, OffsetFrame, G2Mode, VParamMode, CleanupMode (no display names :79); bounds
SurfDegreeBounds [2,3,15], FitToleranceBounds [1e-5,1e-3,10] mm, SampleCountBounds [5,15,200], curveDegreeBounds
[2,3,10], interiorCurveCountBounds [1,3,15], ScaledCurveParameterBounds [-0.5,0,0.5]; re-exports GeometricContinuity
:9-11). continuityTools.fs / curveOps.fs: import only, no effect. debugTools.fs: print helpers. tools/: bspline_knots
(elevateDegree :420, refineKnotVector :273, Boehm), bspline_data (getInteriorKnots), arc_length (computeArcLength,
Gauss), frenet, transition_functions (LINEAR, SINUSOIDAL (1-cos pi t)/2 :74, LOGISTIC normalized sigmoid k=10
:97-105 -- doc wrongly claims f'=0 at ends; computeAppliedSF(s,a,b,type) = a + (b-a) f(s) :159), printing.
Shared engine:
1. Curve compatibility (makeCurvesCompatible gordonCurveCompat.fs:164-256): elevate all to max degree; max-multiplicity
   union of interior knots via tolerance consume-and-match 1e-7 (:208-220, getKnotsToInsert :262); Boehm insertion
   (P&T A5.1, alpha_i = (u - u_i)/(u_{i+p} - u_i) :12). Result: same degree, knots, CP count -> corresponding CPs can
   be blended. Domains NOT rescaled (assumes shared domain).
2. Skinning (createSkinningSurface gordonSurface.fs:618-763): for each CP index j interpolate the j-th CP of every
   section through the section parameters (approximateSpline interpolating, tol 1e-6 m :844-861), make columns
   compatible, assemble grid; rational handled homogeneous (:681-705); degree clamped to numCurves-1 (:667).
3. opCreateBSplineSurface needs G1 throughout.

## 1 Gordon Surface (gordonSurface.fs, 1904 lines)
Builds one B-spline surface interpolating two crossing curve families (Gordon / transfinite). S = S_u + S_v - T: S_u
skins the U-curves, S_v the V-curves, T = tensor-product interpolating only the intersection grid; each skin reproduces
its own family but not the other; subtracting T removes the double count. Once compatible: CP = CP_Su + CP_Sv - CP_T
(assembleGordonSurface :1521-1531). Generalized Coons patch.
Figures: (a) 3x4 network with S_u, S_v, T side by side then sum; (b) intersection grid with averaged u_j, v_i; (c)
before/after degree/knots/CP table for compatibility.
Dialog (:64-129): U-Curves (edges, max 10, ALLOW_QUERY_ORDER -- pick order = section order); V-Curves (same); Debug and
details (collapsed): Create S_u / S_v / tensor surf (false; bodies "S_u Surface", "S_v Surface", "Subtensor Surface");
Show curves (default TRUE; U cyan, V magenta); Print input curve data -> Curve format (METADATA); print S_u/S_v/tensor
-> Surface format; Details: u_degree, v_degree (3, 2-15); Sample factor (4) and Sampled spline tol. declared but NEVER
READ (:125, :128).
Output: sheet "Gordon surf" (:316-323). No notices; regenErrors ("Need at least 2 curves", dimension mismatches,
normalizeSurfaceDef :1626-1760); println warning params too close (:573).
Algorithm: evApproximateBSplineCurve each edge (:147-163); makeCurvesCompatible per family (:172-173) +
normalizeBSplineCurve (:25-58); computeIntersectionGrid (:342): findCurveIntersection (:416) 21x21 brute force on [0,1]
then 10 half-step tangent-projection refinements (refineIntersection :481), stores MIDPOINT of closest pair; averaged
params v_i = mean_j, u_j = mean_i (:368-395); sanitizeParams (:533): detect descending -> flip arrays, recompute grid
(:199-218), force 0/1 ends, min spacing 2e-6; S_u = skin(U, vDegree, v_i) (:233); S_v = transpose(skin(V, uDegree,
u_j)) (:251-252, transposeSurface :1555); T = createTensorProductSurface (:966) (interpolate columns, compat, rows
through column CPs, compat); makeSurfacesCompatible (:1070) (elevateSurfaceU/VDegree :1154/:1219, merge/refine knots
:1283/:1347); CP sum -> opCreateBSplineSurface.
Limits: curves must intersect (no distance check; miss silently replaced by midpoint); network must be consistent (each
U crosses every V, in order); averaged params -> only approximate interpolation when crossing params vary; rational
inputs: edges read without forceNonRational and evaluateSpline ignores weights (correction 39) -> arc intersection off;
assembly and T always non-rational (:1539, :1045) -> weights dropped (unclear if wrong in practice); [0,1] domain
assumed (:440, :517) -- trimmed sketch splines keep trimmed domains (MCE added withUnitDomain for this); 441 evals per
pair before refinement, max 10x10; degree clamped to curves-1; Show curves default true; no periodic/closed.

## 2 Interior curves (interiorCurves.fs, 628 lines; exported symbol myFeature)
Given 4 boundary chains U0, U1, V0, V1 forming a closed loop, generates interior U and V curves guaranteed to
intersect; seed a Gordon network. Bilinear Coons: P(u,v) = (1-v)U0(u) + vU1(u) + (1-u)V0(v) + uV1(v) - B(u,v) (:325-341);
interior curves = grid rows/cols sharing grid points.
Figure: 4-boundary loop, Coons grid dots, fitted interior rows/cols.
Dialog (:23-73): U0, U1, V0, V1 (edges, multi-segment joined); "Interior U cure count" / "Interior V cure count" (typo;
3, 1-15); Blend Mode ALWAYS_HIDDEN default AUTO (LINEAR_BLEND, CROSS_SAMPLE unreachable :174-184); Debug & Details:
Print BSplineCurve data -> Data Depth; Details -> Output parameters "Scaled tolerance" / "Scaled curve degree" NOT USED
(hard-coded tol 1e-6 m, degree 3 :154, :362; labels copied from Scaled Curve).
Output: wires interiorU_i / interiorV_i, unnamed. Error "Boundary curves don't form a closed loop at v0" (:585).
Algorithm: evApproximateBSplineCurve; joinCurveSegments per boundary (from scaledCurve.fs); estimateSampleCount =
clamp(3 maxCPs, 20, 100) (:199-216); normalizeBoundaryOrientation (:499); uniform grid params i/(n+1) (:289-302); Coons
grid (:309-343); cubic approximateSpline interpolating end points per row/col (:360-401).
Gotchas: each curve fit through only (count+2) points (densify commented out :382-384) -> 3x3 = 5 points per curve,
coarse; uniform in parameter, not arc length. Maturity: prototype.

## 3 Scaled Curve (scaledCurve.fs, 679 lines)
A curve between two rails (Group 0, Group 1) whose blend position varies along its length -- variable-percentage
mid-curve (e.g. 30% -> 70%). C(s) = (1-f(s)) A(s') + f(s) B(s), f(s) = sf0' + (sf1' - sf0') T(s); user values in
[-0.5, 0.5] shifted +0.5 (:110): -0.5 = 100% Group 0, +0.5 = 100% Group 1; Flip -> s' = 1-s on Group 0 (:254).
Figures: (a) two rails with a scaled curve drifting from near rail 0 to near rail 1; (b) Linear/Sinusoidal/Logistic plots.
Dialog (:22-106): Group 0 (edges); Flip? (OPPOSITE_DIRECTION, reverses Group 0); Group 1; Initial / Final curve
scalefactor ([-0.5, 0.5], 0); Transition Type (LINEAR); Curve name; Project onto surface? -> Projection face; Debug &
Details: Show endpoints (start green, end red), Show curves (G0 cyan, G1 magenta), Print BSplineCurve data -> Data
Depth, Details -> Output: Scaled samples (15), Scaled tolerance (1e-3 mm), Scaled curve degree (3); Input: samples/tol
per group.
Output: wire createScaledBsplineCurve; returns {bspline, query} (:161-182); no notices.
Algorithm: evApproximateBSplineCurve forceNonRational (:116-117); joinCurveSegments (:303): orderCurveSegments (:427)
chains endpoints within tol with flip flags; samples proportional to arc length (>=2 per segment); cumulative chord
params; one cubic approximateSpline ends interpolated; scaledCurve (:244): sample both at same normalized s, blend, fit
ends interpolated; optional projectCurveOnSurface (:201) evDistance per sample then refit.
Gotchas: correspondence by parameter, not arc length (uneven parameterization skews); orientation not automatic (use
Flip); no closed curves; single segment returned unchanged (:315-318) maybe non-[0,1] domain; projection can move
ends. Fixed since review: Create curve removed (:41), Group 1 params used (:120), degree used (:282), forceNonRational
applied. Still: "Desription" typo :62; Group 1 tolerance description says "Group 0" (:98). Maturity prototype. Tests
SC0-SC3.

## 4 Modify curve end (MCE) (modifyCurveEnd.fs, 700 lines)
Moves one end of a curve (or G1 chain) to a new vertex, fading the change toward the fixed end; optional G1/G2 at the
fixed end, match a reference edge/face at the moved end, project onto face. Family's only icon besides Gordon.
Main rewrite target (review 4: hold point, wire input, exact closed-form end conditions).
P'(s) = P(s) + f(s) Delta, Delta = To - From, f 0 at fixed end -> 1 at moved end per transition type (:268-291); refit.
G1: move second CP onto target tangent line keeping its distance (enforceG1AtEnd :480). G2: scale third CP's offset
perpendicular to tangent by kappa_target/kappa_current clamped [0.1, 10] (enforceG2AtEnd :544) -- heuristic. Face ref:
tangent = approach direction projected into tangent plane; curvature Euler kappa = kmin cos^2 + kmax sin^2 (:408-470).
Figures: (a) original dashed + modified, displacement arrow fading; (b) fixed-end G0/G1/G2 zoomed with curvature comb;
(c) moved end matching a reference line/face.
Dialog (:43-133): Edge(s) to modify (G1 chain); From point (nearer path end used :153); To point; Create curve? (true);
Parameters (collapsed): Offset frame (FRENET), Transition type (LOGISTIC), Fixed end continuity (G0; G2 -> G2 Mode
BEST_EFFORT), Endpoint continuity ref? -> Endpoint continuity ref. (edge/face), Flip ref (never read), hidden
showContinuity (editingLogic :24-36, unused), Endpoint continuity (G0); Project onto surface? -> Projection face;
Debug, Details: Spline degree, Sampled spline tol., Max control points (10, UNUSED), Sampling multiple (4; samples = N
x total input CPs :140), Print input/output -> BSpline Print Format.
Output: wire createModifiedEndpointBSpline; {bspline, query}; no notices.
Algorithm: forceNonRational read, join, withUnitDomain (:139-143, :674, new); moved end nearest From; offset world or
Frenet (to Frenet :159 and straight back :257); sample + displace; fit ends interpolated (:301); fixed-end G1/G2; if
reference and continuity != G0 compute constraints (:349) + apply at moved end; optional projection.
Limits: FRENET == WORLD (one rigid offset; test T02 documents); EXACT G2 == BEST_EFFORT (:565-567), else branch dead
(:621-650); G2 approximate; end edits overwrite each other below ~6-7 CPs; projection undoes end constraints; edge
reference evaluated with arc-length vs B-spline param mixed (:391-395); no wire bodies; flipREf typo unused; reference
now correctly gated by checkbox (:163). simplifyCurveWithConstraints (simplifySurface.fs:262-302) never called.
Tests T01-T09 (build_gordon_tests.py:7-19).

## 5 Pull surface (pullSurface.fs, 663 lines)
Interactive push/pull: N x M grid of normal-direction arrow manipulators on a face; drag free grid points; new surface
rebuilt through them with boundary rows locked. No native equivalent. Grid P_ij = S(u_i, v_j) uniform in normalized
params (:369-390); offset P' = P + d n (:432-434). Locks (isPointLocked :36-44): G0 boundary, G1 boundary+1, G2
boundary+2 rows each side. Each iso-U row fit with face-tangent end derivatives for G1/G2 (fitIsoCurve :77), made
compatible, skinned (:629-632).
Figures: (a) patch with 5x5 dots (locked grey, free blue) and one pulled bump; (b) lock patterns G0/G1/G2 on 7x7.
Dialog (:274-362): Face; U / V curve count ([2, 4, 20]); Continuity (G0; G2 -> G2 mode never read :291); Active offsets
array (U index, V index 0-19, Offset +-10 m; fills when dragging; 0 removes; edit U/V to move); Debug: Show
iso-curves, Keep U/V curves, Keep grid points, Show control point polygons (actually grid polygon), Show offset vectors
(green out, red in), Print curve data; Approximation: Degree (3), Tolerance; hidden mpOffsets flat i*vCount+j (dynamic
keys rejected :199-201, :349-362).
Output: new sheet pullSurf (source face NOT replaced/deleted); optional kept wires/points; blue debug points always
drawn (:446); no notices. Editing logic (:146-190): grid counts/continuity change resets offsets; face change
deliberately does not (:139-145 snap-back).
Limits: G0 only at grid nodes, not along edges (P1/P2 XFAIL check_gordon_tests.py:285-286); G1 only via iso-U end
derivatives (u=0/1 edges not G1); default 4x4 with G1/G2 locks all 16 points silently (G1 needs >=5, G2 >=7 per
direction); offset indices can land wrong after hand-edited grid change; trimmed/periodic faces not handled; source
resampled not edited. Planned control-net rewrite (review 3.4). Maturity: "UI and manipulators work; the geometry does
not deliver what it claims" (curves.md:229).

## 6 Simplify surface (simplifySurface.fs, 761 lines)
Rebuilds a face as a lighter cubic B-spline within tolerance, G0/G1/G2 end derivatives kept in v; for heavy
imported/derived/fit faces; or replace in place. Per-iso-curve CP reduction: sample v iso-curves at chosen u, find the
FEWEST CPs within tolerance of the true face, make compatible, skin in u. AUTO u stations = Greville abscissae of the
source g_i = (U_{i+1} + ... + U_{i+p})/p (:519-529) -> u CP count = source's.
Figures: (a) before/after control net (20x30 -> 10x8); (b) binary search lo/hi vs error; (c) Greville stations on a
knot vector.
Dialog (:28-58): Face; Tolerance ([0.01, 1, 100] mm); Continuity (G0; G2 -> G2 mode passed but never read); Mode AUTO /
MANUAL (no display names constEnums.fs:79); MANUAL -> U curve count, V curve count ([2, 5, 20]; V = max CPs per
iso-curve :742); Replace face (opReplaceFace then delete temp :77-87); Debug print ([simplify]).
Output: sheet simplified, or face replaced. No notices.
AUTO (:499-662): evApproximateBSplineSurface (NEVER forceNonRational -> C0 kinks -> CANNOT_MAKE_BSPLINESURFACE);
Greville stations; per station 50 samples along v (sampleSurfaceIsoCurve :99) + finite-difference end derivatives for
G1/G2 (eps 1e-5, h 1e-4); baseline fit at hi = source v-CPs; binary search lo=4..hi with maxControlPoints (try silent
catches "too small"); each candidate checked against the FACE at 50 points (computeCurveError :441);
makeCurvesCompatible; createSkinningSurface degree 3 in u. MANUAL (:663-760): uniform u stations, fixed max CPs.
Gotchas: final v-CP count >= most complex station's (knot union re-inflates); error checked only on station curves;
Greville from source knots but face evaluated normalized (unclear when knots not [0,1]); G1 minimum ~6 CPs; docstring
:477-481 contradicts code; buildSurfaceFromCurves / buildClampedKnotVector / simplifyCurveWithConstraints unused.
Untested (no cases).

## 7 makeCurvesCompitable (gordonCurveCompat.fs; feature makeCompatableCurves :29-80)
Utility/diagnostic: make selected edges share degree and knots, print report. Name misspelled, empty description.
Dialog: Edges to make compatible; Print Report? (before/after compatibilityReport :89: degrees, CP counts, knot counts,
interior knots, rational flags, match booleans); Create wire? (compatibleBSpline_i). Order follows query evaluation.
Geometry unchanged (elevation and insertion exact). Figure: before/after knot vectors for 3 curves. Developer utility;
COMPAT_DEBUG=false (:27).

## Examples (build_gordon_tests.py:7-30)
MCE curve: spline through (0,0), (50,30), (100,0), (150,-30), (200,0), trimmed; end moved (200,0) -> (200,30). SC0
lines y=0 and y=100 -> y=50. SC3 arcs R100 and R200 -> R150. Pull: 90 deg R100 cylinder patch +10 mm at (1,1) 4x4 G0;
5x5 G1; planar control. No Gordon or Simplify fixture.

## Unclear
1. "Archived" vs "heavy use" -- ask owner which maturity label to publish.
2. Review / HEAD / working copy differ; cite current code.
3. Dead params: Gordon sampleFactor/splineTol, Interior output tol/degree, MCE splineCP/flipREf, Pull/Simplify g2Mode.
4. Gordon with rational/arc inputs and non-[0,1] domains unchecked.
5. Simplify docstring contradicts AUTO.
6. No .document.json; icons missing except Gordon and MCE.
7. g2-jostle-formulas.md corrected G2 end formula (wrong one in curveMapping); MCE closed-form G2 should use corrected.
