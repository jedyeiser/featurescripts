# Research brief: xSection family (Explore agent, 2026-09-25)

READ FIRST: memory notes partly stale; all 20 xSection files modified in git right now. Code now:
- EI uses each body's Young's modulus E_x, not Q11 (materials/xSectCLT.fs:334-442).
- GJ uses Q66 for every material; balanced/unbalanced ratio rule gone (section/xSect_GJ.fs:221-229).
- GJ computed INSIDE "EI and Cross Section" (features/xSect.fs:374-379), not 0 until Solve GJ.
- empty try/catch around GJ loop and attribute store removed (xSect.fs:373, 452).
- "Wire to cross section along" = one wire body only (core/xSectPredicates.fs:130-134).
- Generate baseline Newton/bisection loop (generateBaseline-convergence.md) gone; now one proportional loop
  (features/generateBaseline.fs:260-444).
Several file headers still describe old behaviour. Document the code.

## 0 Overview
Turns a CAD layup (one solid per ski layer, each with an Onshape material) into EI(x), GJ(x), neutral axis, lineal
density, flex numbers and weight; uses EI to design/check camber-rocker baseline, predict deflection under a load case,
and suggest a thickness profile hitting a target EI.
Data flow:
  Layer bodies + materialData.csv -> EI and Cross Section (xSect): attribute "CrossSectionAnalysis" on origin keyed by
  feature id; curves {name}_EI, _neutralAxis, _linealDensity, _GJ, _profileHeight
  -> Cross-Section Analysis table (tables/xSectionAnalysisTable.fs) reads the attribute
  -> Solve GJ (FeatureList) re-reads, recomputes, writes back (redundant)
  -> Update profile (FeatureList + target EI edges) -> thickness curve + attribute "profileUpdates" -> Profile Updates table
  EI curve edges (world Z mm = EI N m^2) -> Estimate Stiffness (dialog fields only); -> Estimate Deflection
  (deflection_curve, altered_EI); -> Generate baseline ("Use EI profile") -> Baseline wire -> Analze baseline.
Conventions: EI curve is the interchange format: XZ plane, world Z in mm = EI in N m^2 (section/xSectVisualization.fs:
113-131; decoded beam/xSectBeamAnalysis.fs:373-414). GJ 1 N m^2 = 1 mm; lineal density 1 kg/m = 100 mm (:91-92);
profileHeight Z = thickness true scale (:256-267). Ski axis world +X; FCP/ACP/mount resolve to world X only
(core/xSectReferencePoints.fs); planar face for FCP/ACP must be normal to X within 0.01. Vocabulary FCP/ACP, MRS,
FRCP/ARCP (fore/aft rocker contact points = inflections where camber meets rocker), FCPH/ACPH (tip heights normal to
rocker tangent), MCH camber height, RSL. xSectReferencePoints.fs says "Front Climbing Point" -- use "Contact".
Onshape: doc "xSection" k2-sports https://k2-sports.onshape.com/documents/f8deedeb1fbd819a8fa20113/w/33c32ba27726cb036d4d912b;
.document.json tabs xSect a430d49d, generateBaseline afbdbc9b, estimateDeflection 5b61f49a, updateProfile 9a4f5d9a,
estimateStiffness 5e67a21d, GJ_Feature 67a5669a; settings element e/489f267862a807871c8aa5f5; test studio "ROY_Test"
(b406721e). No icons; no test studio/checker.

## 1 EI and Cross Section (features/xSect.fs)
Slices the layup at stations; per station effective EI, NA height, GJ, width/thickness, lineal density; flex numbers,
weight; stores everything.
Stations: with FCP/ACP, N ("Number of cross sections") evenly between FCP and ACP with one exactly at each; tip and
tail get 0-4 extra at about the same spacing (core/xSectUtils.fs:320-407); outermost inset by max(0.5 mm, 2% of
spacing); numbers negative in tip, 0..N-1 reference, >= N tail. Without FCP/ACP: N uniform along path.
Cutting: one opIntersectFaces per body against all station planes (section/xSectProcessing.fs:187-236); outlines
cleaned: duplicate edges removed, micron gaps chained (20 um) or bridged (<= 0.2 mm) (section/xSect_Triangulation.fs:
54-63, 209, 343); holes/islands, ear-clip triangulation, area/centroid/I from outline by Green's theorem +
parallel axis (:746, 960, 1265, 1319).
EI "laminate theory for beams": each body k = one ply with A_k, centroid height ybar_k (from base edge), centroidal
I_k: I_ref,k = I_k + A_k ybar_k^2; NA = sum E A ybar / sum E A; EI_eff = sum E I_ref - (sum E A ybar)^2 / sum E A
(xSectCLT.fs:390-442) = D11 - B11^2/A11 with E_x for Q11. Comment: narrow ski bends like a beam; Q11 overstates by
1/(1 - nu12 nu21) ~12% for wood/Titanal, up to ~4x for +-45 fabric. Full A/B/D from Q still assembled/stored, unused for EI.
GJ thin plate Saint-Venant: GJ = 4 sum_e G_e Iz_e, Iz_e = (A_e/6)(y1^2 + y2^2 + y3^2 + y1y2 + y1y3 + y2y3), y = thickness
coordinate from G-weighted centroid, G_e = Q66 of element's body (xSect_GJ.fs:221-285); exact for b/t >> 1, ~10% off at
b/t ~ 7.
Flex numbers (beam/xSectBeamAnalysis.fs:93-292), simply supported FCP-ACP, load at MRS, L = span: prismatic
delta = P L^3 / (48 EI_bar) (EI_bar trapezoid average, 200 segments); variable-EI Mohr: C = integral s(xi)^2/EI dxi,
s = xi/2 front half, (L - xi)/2 rear; delta = P C. Four outputs: load in lbf for 25.4 mm ("lb/in") and deflection mm
under 30 kg (294.2 N), prismatic and variable-EI. Mass: volume x CSV density; lineal density = sum rho A.
Dialog (core/xSectPredicates.fs:124-347): Wire to cross section along (one wire; X monotonic); Analysis name ("");
FCP, ACP (vertex, planar face normal to X, MC; optional but flex + adaptive spacing need both); Cross section bodies
(solids/composites, unwrapped recursively xSect.fs:106-153); Material library (CSV table element materialData.csv:
Category, Name, density, nu, E, Q11/Q22/Q12/Q66/Q16/Q26 GPa, CTE, dimensions); Refresh CSV data (button); Number of
cross sections (3-200, 50); Bodies & Materials group (auto array "bodyName (materialName)"; Onshape material name must
EXACTLY match a CSV Name incl case/spaces; unmatched: Material behavior Ignore (default; geometry only) / Provide
material data -> Isotropic/Orthotropic, name override, Density (1000 kg/m3), Isotropic E (10000 MPa; nu fixed 0.33
xSectProcessing.fs:463-468), Orthotropic E1, E2, G12, nu12 (0.3)); Output Options (collapsed): Create composites (true;
one composite of section wires per station; SLOWEST), Language English/Deutsch, Material table (off), Body detail table
(off) -> Detail level Basic / EI only / Geometry only / Full; Debug (off) -> Debug type Edges / Points / Mesh, All
cross-sections (else list), All bodies (else selection), Print bodies?, Print triangles?
Outputs: curves EI_curve / {name}_EI, neutral_axis, linealDensity_curve, GJ_curve, profileHeight_curve
(xSectVisualization.fs:33-40), optional composites and debug. Attribute CrossSectionAnalysis (core/xSectStorage.fs:
62-160): per body name, material, volume, mass; per station stationNumber, xCoord, frame, EI_eff, GJ_eff,
neutralAxisY, boundingBox, linealDensity, A/B/D, sectionPoints, bodyData (mesh); beamAnalysis; tableData. Table
Summary (4 stiffness values + weight kg) + per station Station, X, EI, GJ, NA Height, NA %, Beam Width, Beam Height,
Lineal Density (xSectStorage.fs:182-282) + optional Materials, Body Detail. Notices (xSect.fs:486-518): WARNING "N body
section(s) between FCP and ACP could not be closed or came out with no area; EI is low there" only when lost inside
FCP-ACP; else INFO "Repaired N body section outline(s)..."; details console.
Algorithm (xSect.fs:347-473): resolve FCP/ACP X (resolveReferencePointX); stations getCrossSectionFramesAdaptive
(xSectUtils.fs:253); batch intersect + B-spline fit (processCrossSections xSectProcessing.fs:93); per body dedup, chain,
nest, triangulate, properties (processBodyCurves xSect_Triangulation.fs:100); E-based EI + NA (computeCLTProperties
xSectCLT.fs:279); GJ (computeTorsionalStiffness xSect_GJ.fs:57); masses, flex (computeBeamStiffness) if FCP and ACP;
curves, table data, composites/debug, diagnostics.
Examples: missed-geometry fix ROY_Test: EI dips at x = 885 (-17%), 145 (-26%), 1625 (-20%) were zero-area body
sections; after fix EI at 885 = 426.45 vs neighbours 427.2, 428.1. Measured vs calculated (before E_x change): calc/meas
0.76-0.94 near shovel, 1.33-1.44 transition, 1.21-1.26 centre; GJ peaked ~350 vs ~200-230 measured; E_x and Q66 changes
should move both, not re-measured. Analytic (reviews/2026-09-25_tools_review/xsection.md): 100x10 mm isotropic E 10 GPa:
EI = E b h^3/12 = 83.3 N m^2 (93.5 on old plate basis = 83.3/0.891); 100x2 mm TBX270 plate GJ = 4 Q66 b t^3/12 = 4 x
33.49 GPa x 6.67e-11 m^4 = 8.93 N m^2. Illustrative flex (brief author's numbers): L 1.2 m, EI_bar 300 -> delta(30 kg) =
294.2 x 1.728/(48 x 300) = 35.3 mm; 25.4 mm load 211.7 N = 47.6 lb/in.
Limits: exact-string material matching; wood cores (Aspen, Poplar) and some "Uni" rows isotropic placeholders (Aspen E
9.75 GPa, Q66 3.67 GPa) -> wood GJ overstated; Ignore bodies contribute no stiffness or mass; end stations can lose
P-Tex outline where plane grazes tip/tail (console only, open); tip/tail capped 4 per side; Mohr treats EI = 0 as no
contribution (rigid) (xSectBeamAnalysis.fs:258); Create composites default true + slow; every xSect feature stored in
one shared origin attribute (grows); stored boundingBox width = THICKNESS, height = WIDTH (xSectStorage.fs:231-236).
Figures: station layout (tip, reference, tail regions, numbers); layered section with ybar_k, NA, base edge; simply
supported beam with load at MRS and s(xi) moment triangle.

## 2 Solve GJ (features/GJ_Feature.fs)
Recomputes GJ for an existing xSect feature, writes back to attribute/table. REDUNDANT (xSect computes GJ inline with
same solver). Dialog: Cross section feature (FeatureList, one) (:65-66). First key (:70); computeAndStoreGJByFeatureKey
(section/gjAnalysis.fs:40-89) -> computeTorsionalStiffness per station -> updateXSectGJDataByKey (gjDataAccess.fs:
234-300). BUG: updated existingSections[i].GJ_eff written to a local copy never put back into featureData.details; only
table column 3 updated (:258-282). No curve (edit logic refers to nonexistent createGJCurve/curvePrefix). Write in empty
try/catch; empty selection throws on keys(...)[0]. Recommend: mark deprecated/legacy (review 3 P1-6 agrees).

## 3 Generate baseline (features/generateBaseline.fs + beam/generateBaselineSolver.fs, baselineCore.fs)
Builds the unweighted side profile (camber pocket + fore and aft rocker) from design targets; optionally camber shape =
real bending shape of the ski's own EI.
FRCP = FCP moved toward MRS by forebody rocker length; ARCP = ACP toward MRS by aftbody rocker length (:210-211). Camber
pocket FRCP-ARCP pinned both ends: with EI (solveCamberBeam solver :56-152) deflected shape of simply supported beam with
1 N point load at mount; kappa = M/EI integrated twice, end chord removed, scaled so peak = H. Without EI
(solveCamberCubic :173-248): cubic f(FRCP) = f(ARCP) = 0, f(mount) = H, f'(mount) = 0 (parabola when mount at midpoint).
Rocker (:305-393): quadratic B-spline leaving FRCP tangent to camber; end at FCP offset from camber tangent line by
"FCP height" measured normal to it; tension t (0.5) sets where rocker lowest point falls; optional minimum point distance
solves t (solveForTension), clamped to achievable (getAchievableXDistRange) with INFO when clamped. Leveling: rotate about
MRS so forebody and aftbody minimum points same Z (findMinZBothSides, transformCurves; :311, 487). Camber solve after
leveling: camber measured as Z at mount (solveZAtX); H <- H + err up to 20 passes until |err| < 0.001 mm (:260-444); no
notice if not converged.
Dialog (:81-172; bounds baselineCore.fs:4-7, :50-52): Output type Single curve / Curve per region (default); Output
curve name "Baseline"; FCP, ACP, Mount / load point (planar face, vertex, MC; mount between FCP and ACP else error);
Baseline targets: Camber height MCH 0-15 mm (5); Forebody rocker length 0-500 (150; 0 = none); Spec forebody minimum ->
Forebody inflection point to minimum point dist 20-300 (150); Aftbody rocker length + Spec aftbody minimum same; FCP
height, ACP height 0-35 (5); Use EI profile (off) -> EI profile edges; Spline output: Approximation tolerance 1e-7..1e-2 m
(0.1 mm), Max CPs 4-50 (15), Curve degree 3-9 (3); Add baseline sketch (measurement sketch like Analyze baseline);
Create weighted baseline (second wire "Baseline (Weighted)": zero camber straight FRCP->ARCP, rockers rotated rigidly
about FRCP/ARCP, :528-620); Debug Print setup / solver iterations / baseline analysis.
Outputs: wire named Output curve name (Curve per region = camber, fore rocker, aft rocker edges in one wire; Single = one
approximated spline sampled 20 per region); optional weighted wire + sketch; INFO when min-point distance clamped; no
attributes.
Example: FCPH/ACPH converged with ~0.07 mm systematic camber offset accepted (older loop -- re-verify).
Gotchas: camber target = height AT THE MOUNT; Analyze baseline reports MAX PERPENDICULAR distance to line between
minima -> differ with off-centre mount; FCP > ACP direction fixed 2026-07-03 (pivot about MRS, theta in (-90, 90], lo->hi
pocket :213-218); getEIFromEdges still called with descending X when FCP > ACP (:232) unverified; negative EI from spline
overshoot clamped to 0 (xSectBeamAnalysis.fs:401-411).
Figures: labelled baseline (FCP, FRCP, MRS, mount, ARCP, ACP, MCH, FCPH/ACPH, rocker lengths); rocker construction
(tangent line, normal offset, quadratic control polygon with tension); weighted vs unweighted overlay.

## 4 Analze baseline (sic) (beam/analyzeBaseline.fs:785-942)
Measures an existing baseline: camber height, rocker contact points, rocker lengths, tip heights (compare physical/CAD
ski with design targets). Dialog: Baseline edges (G1); FCP, ACP; Calculated data (read-only, filled by edit logic on
every change :746-778): FB min, AB min, Max camber point, Camber height, FRCP, FRCPL, FRCP tangent line, FCPH, ARCP,
ARCPL, ARCP tangent line, ACPH; Output measurement sketch; Recalculate (button).
Algorithm (analyzeBaselineGeometry :454-739): chain (buildEdgeChain :65); 200 samples; resolve FCP/ACP X, snap to exact
chain ends where closer; min Z in each half (FCP-MRS, MRS-ACP), refine on edge; camber = max perpendicular distance from
chord between the two minima; inflections within edges and at junctions (sign change in normal Z, threshold 0.01);
FRCP/ARCP = LOWEST inflection in each half; FRCPL = |x_FRCP - x_FCP|; FCPH = perpendicular distance from FCP to FRCP
tangent line; aft same. Outputs: read-outs + optional construction sketch on world XZ (min chord, inflection chord,
tangent and normal legs at each end, camber normal). Gotchas: misspelled name; description truncated ("...and generates
"); ARCPL description says "between fcp and frcp"; values only via edit logic; no inflection -> fields unset.

## 5 Estimate Deflection (features/estimateDeflection.fs + beam/estimateDeflectionSolver.fs)
General free-beam load-case solver: EI(x), up to 3 supports, 2 applied loads with pressure distributions, optional
binding-plate stiffness and mounting moment -> deflected shape (on-snow / bench scenarios).
Model (:776-1235): sample EI (N per edge), trim leading/trailing zero-EI; resolve support/load X; reactions by moment
balance (third support: R3 = F_total x balance); distributed load shapes integrating to the force (loadIntensityAt
solver :197-249): Constant, Linear triangle, Quadratic, Quintic (C2 bump), Logistic; shear and moment by trapezoid with
point-load jumps; optional concentrated moment; M forced 0 at beam ends (drift), V and M zeroed outside loaded span;
kappa = M/(EI + plate EI), EI floored 0.001; theta = integral kappa, delta = integral theta, linear term so delta = 0 at
supports 1 and 2; output true scale in XZ.
Dialog (:350-650): EI Edges; Number of evaluation points 20-500 (100); Show shear/moment diagrams; Alter deformed beam
curvature (draggable kappa manipulators -> altered_EI; Num. Manipulation points 10-50 (20); EI extension mode Smoothstep
/ Continue offset); Print load summary, Print full diagnostics; Add plate/mounting conditions? -> Plate stiffness (one
EI-format edge added over its X range), Reaction moment (N m, positive only), Moment x location; Applied load data:
Applied load 3-600 N (80), Applied load balance 0.001-0.999 (0.75; fraction on load 1; only with second load), Applied
load 1 (Location type Query / X value, Load shape (Point), Width (non-point), Query or X), Second applied load? ->
Applied load 2; Support load data: Add third support load (+ Support load balance), Support load 1, 2, (3) same four
fields; Output Options: Output span Full EI / Support span; Curve output type Fit / Approx (tol 0.1 mm, max CPs 50,
degree 3). Hidden disabled "Back out EI" (:695-773).
Outputs: deflection_curve (Z = deflection, true scale); optional altered_EI (EI format); debug overlays, console.
Errors (:783, 808, 845, 903, 1148, 1162): < 2 EI samples; all EI zero; supports 1 and 2 same X; zero span; supports on
same grid node.
Gotchas: reaction moment can't be negative; evaluation points also = EI samples per edge; delta = 0 only at supports 1
and 2, not 3; loadIntensityAt compares shape to strings ("POINT") while callers pass LoadType enums -- check; EI decode
copied, not shared.
Figures: free-body diagram (2 supports, 2 loads with shape icons); 5 load-shape profiles; V, M, kappa, delta stack.

## 6 Estimate Stiffness (features/estimateStiffness.fs)
Quick flex calculator for any EI curve (target, competitor data); same four numbers as xSect summary. Dialog: EI Edges;
FCP, ACP; Stiffness estimates read-only (Prismatic lb/in, Prismatic mm/30kg, Estimated lb/in, Estimated mm/30kg);
Recalculate button. All in edit logic on Recalculate (:31-68): getEIFromEdges 100 per edge, extended to FCP/ACP if short,
computeBeamStiffness. Body does nothing (:112). Gotchas: silently nothing if FCP >= ACP in X (:48); stored values don't
update with geometry; recalculate trigger treats button like boolean.

## 7 Update profile (features/updateProfile.fs + tables/profileUpdatesTable.fs)
Inverse design: from xSect model + target EI curve, propose new thickness t(x).
STD (full section model): at each station every section point ABOVE the original NA moved up by dt; points below stay;
EI recomputed from stored mesh with E_x (computeEIFromShiftedPoints :211-298); dt by bisection (<= 60 iterations, tol 1e-4
N m^2) between 2 mm min thickness and double current; out-of-range targets clamped; target = measured + scale x
(target - measured). DELTA: power law EI/b = alpha t^beta, alpha/beta from log-log regression over all stations
(:326-360, repeated :574-602); t_new = (EI_new/(alpha b))^(1/beta), EI_new = measured + scale x delta. PERCENT: t_new =
(EI_target/EI_meas x t_old^beta)^(1/beta). All modes: new thickness outside 0.1-200 mm reset to old (:781-787).
Dialog (:418-553): Solver type STD / DELTA / PERCENT (STD); Output curve name "Updated thickness"; Cross section feature
(FeatureList); Target EI profile (EI-format edges); Measured EI from Inherit / Query (+ edges); Measured thickness from
Inherit / Query (+ edges; NOT USED in body); STD: Scale delta -> Delta scale factor 0.001-1 (1); DELTA/PERCENT: Override
percentage delta -> Apply percentage to delta (only DELTA uses); FCP, ACP -> read-only Stiffness estimates of the TARGET
curve; Width, thickness, stiffness data (DELTA/PERCENT): read-only Alpha, Beta; Spline Output: Fit / Approximate (tol 0.1
mm, max CPs 50, degree 3); Recalculate (no effect :365-367).
Outputs: thickness wire (Z = thickness, true scale); attribute profileUpdates (x, measured t and EI, updated t and EI);
Profile Updates table (+ dt, dEI columns); spline fit failure -> red/magenta debug points, no error.
Gotchas: target EI read with only 11 samples per edge + linear interpolation (sampleEIEdgesAtX :102-170); stations
outside target range skipped; STD assumes thickness added only above NA (topsheet/core grows); tip/tail stations
included (affects alpha/beta). Figure: section before/after, points above NA shifted up by dt.

## 8 Tables
Cross-Section Analysis (xSectionAnalysisTable.fs:30): up to 4 tables per xSect feature (Summary, Details, Materials,
Body Detail) for EVERY xSect feature in the document; no inputs. Profile Updates (profileUpdatesTable.fs:21): one per
Update profile feature.

## 9 Contradictions to confirm with owner
1 EI basis: code E_x (xSectCLT.fs:334-442, updateProfile.fs:243); header xSectCLT.fs:64-66 says Q11; memory says Q11-vs-E
  "ruled out"; review proposed a selectable "Stiffness basis" (not in dialog) -> existing EI tables shift ~10%. Confirm.
2 GJ modulus Q66 in code; header (:10-12, :204-205) and memory describe ratio rule; "1.75x GJ" figure predates.
3 xSectCLT.fs :454-458 says GJ "moved to a separate gjAnalysis feature" yet computed inline.
4 generateBaseline.fs header (:32-37) "outer bisection +-0.01 mm"; solver header (:14-23) lists nonexistent functions.
5 Path input: memory says edges or wire; predicate single wire only; direct edges return? (review open q 6);
  xSectReferencePoints edgeQuery unused -> FCP/ACP always world X.
6 Solve GJ: review claimed wrong field name; stored section does have top-level GJ_eff (xSectStorage.fs:108) so review
  wrong; copy bug real (gjDataAccess.fs:258-265). Deprecate or document legacy?
7 Station 0 when FCP > ACP labels low-X boundary (maybe ACP).
8 Divide-by-zero guard in computeBeamStiffness when EI integral ~0? Camber definition (mount height vs max
  perpendicular) -- which to document?
9 Inert items: Measured thickness edges, Recalculate in Update profile and Analyze baseline, AlterMeshingPredicate
  (xSectPredicates.fs:350-376) unused.
10 Wood/core CSV isotropic placeholders (GJ wrong for cores); createComposites = true default intended?; exact-case names.
11 No test studio/checker; no icons.
