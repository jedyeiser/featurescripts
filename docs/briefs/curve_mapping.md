# Research brief: Curve mapping (public) + Offset edges (Explore agent, 2026-09-25)

## 0 Overview
Public doc curveMapping_public https://k2-sports.onshape.com/documents/d12583277c42b00c63a9b7b7/w/430e0dff82a5ed23ba89b7aa;
tabs wrapCurve 0e53e9b1, wrapAndLoft a3eddb37, deform 97242275, measureBetweenCurves 08af3fbb. Engine doc (internal)
08e8748f2ef24eea16072b75, core tab 683d867c imported by all three public features pinned to engine V23 d10f79db, core mv
f8390061 (wrapCurve.fs:20, wrapAndLoft.fs:12, deform.fs:7). Offset edges in example_1 7183868d32cdc7a6ad14410d, tab
5fdfa98d, imports same V23 core (offsetEdges.fs:6-8).
The bending map: From chain and To chain (each G1), each with a reference point (vertex, MC, plane origin) projected
onto its chain -> fromRefArc / toRefArc. Every point P: 1 project onto From -> s_from (projectOntoFrenetPath
curveMappingCore.fs:1117); 2 From frame at s_from: origin, T (zAxis), N (xAxis), B = T x N (getFrameAtArcLength core:965);
3 local (t, n, b) (worldPointToFrenet); 4 s_to = toRefArc + (s_from - fromRefArc) -- pure arc-length shift, no scaling
(wrapCurve.fs:422, core:1352); 5 P' = O_to + n N_to + b B_to + t T_to (wrapCurve.fs:437, core:1368).
Physically: length along the reference preserved (1500 mm flat station lands 1500 mm along the profile); width and
height coordinates carried unchanged (ribs stay perpendicular); layers at height h stretch/compress by (R - h)/R =
"physically correct bending" chosen 2026-09-23; "parallel-arc" correction CM_PARALLEL_ARC = false (core:557)
deliberately off, not exposed. No bending physics option in the dialog.
Frames: Frenet mode (default) no longer per-point Frenet: one normal seeded at the most-curved interior sample of the
first curved edge, parallel-transported (minimal rotation) forward and backward (core:327-473; cmTransportNormal
core:800) -> no flip at inflections. Binormal mode: user plane normal ref, N = normalize(ref x T) (core:113); every
reference edge must be planar with that normal within 1e-4 x edge length or throws (core:225-237).
Ends of To chain: s_to past either end continues along the OSCULATING CIRCLE at that end (core:970-1009): along =
sin(kappa D)/kappa, toward centre = (1 - cos kappa D)/kappa.
Straight-region fast path: both references straight over a source edge's span -> one rigid transform; edge copied and
moved exactly (opExtractWires + opTransform): arcs stay arcs, weights kept (CM_LINEAR_FASTPATH core:1460;
linearRegionMove core:1518).
vs native: Wrap only plane <-> cylinder/cone; Project/Split drop along a direction (foreshortens, no arc-length).
Family figures: From/To chains with references and a point P with (t, n, b) triad before/after; two layers h = 0 and
h = 20 bending around a tip radius ((R - h)/R stretch); frame-transport ribbon along a 3D chain vs flipping Frenet.

## 1 Wrap Curve (curveMapping_public/wrapCurve.fs)
Wrap flat/2D curves onto a curved reference by arc length (planform outline, graphic/inlay lines, insert/edge lines
from flat layout onto rocker/camber profile).
Dialog (wrapCurve.fs:34-197; bounds core:16-57, std approximationUtils.fs:22-47): From data (collapsed): From edge(s)
(edges/wires/composites, <= 10, G1), From reference (vertex/MC/plane); To data (collapsed): To edge(s) (<= 10, G1), Flip
(reverses To traversal), To reference, Flip normal (inverts N_to, mirrors normal coordinate); Source curves; Frame
orientation: Normal mode Frenet (default) / Binormal -> Binormal from Query (planar face or MC Z) / Vector (X/Y/Z 0/1/0);
Details (always shown; hidden showAdvanced true :98-103): Source sampling mode CP-based (multiplier 3, 1-50) /
Length-based (density 10 mm, 0.1-200); Target degree 3 (min 2); Keep source degree (false; max(target, source));
Maximum control points 15 (4-100); Tolerance 1e-5 m (0.01 mm); Break spans at curvature jumps and corners (false;
changes topology); Debug: Print from/to/source/wrapped BSplines, Detailed BSpline output, Print wrap detail, Print
arc-length parameterization (public only), Show from/to frames, Show source (cyan) / wrapped (magenta) points.
Output: one wire (id+"wire") of spline spans + fast-path edges; intermediates deleted (:864-869). No reportFeature
notices; console: "span ... fitting at degree ... instead" (:748); "cannot be fitted; the output will have a gap"
(:837); ERROR BAD_GEOMETRY with red/magenta debug (:821-830).
Algorithm: buildFrenetPath (core:184; G1 check via constructPath, batched frame table 100 samples per curved edge,
near-straight (<0.1% deviation) promoted to lines, transport pass); project references (:249-252),
alignLineFramesBilateral (:270); flipToXAxis decided once (:288-291); per source curve try fast path (:345-369); else
sample uniformly IN PARAMETER (sampleSourceEdge core:1754; count max(10, mult x CPs) or max(10, length/density + 1)),
map every sample (:405-461); reverse if source runs against From (:478-485); group into spans (continue while To path
tangent-continuous within 0.5 deg, pathIsSmoothAcross core:682, CM_SPAN_MERGE_ANGLE core:606; Break spans also at
curvature jumps > 10% relative core:2473 and From-path corners); junctions: exact point + two oversample points + exact
tangents each side (exactTangentAt / mapTangentExact core:2563 incl (1 - h kappa) stretch); top up short spans
(mapBetweenSamples core:632), absorb lone trailing sample, dedupe < 1 um; approximateSpline with start/end derivative
constraints, snap end CPs to exact ends (:773-801); NO G2 jostle any more (:846-849).
Examples: tip wrap: From straight 1800 mm reference x = 0; To = 1200 mm flat + R600 tip arc, reference at start; source
x = 1500, b = 50, n = 0 -> s_to 1500, 300 mm into the arc, theta 0.5 rad -> 600 sin 0.5 = 287.6 mm along, 600(1 - cos
0.5) = 73.4 mm up; width 50 unchanged. Layer stretch: 100 mm curve 20 mm off a straight From reference onto R400 arc ->
95 mm on the concave side (400 - 20)/400. End overrun: 10 mm past end of R200 tip: straight extension off by d^2/2R =
0.25 mm; circular exact.
Figures: planform line wrapped onto rocker profile (samples + mapped); span splitting at line/arc joint Break spans
off/on; overrun past tip straight vs osculating circle.
Limits: only To chain flippable (From direction from constructPath :221); off-centre From reference or unequal lengths
push points past an end (extrapolated); existing models may need Flip normal toggled after the transport change;
CP-based sampling of high-degree source on short To edges used to gap -> now falls back to lower degree + warning
(workaround: rebuild source at degree 3); Break spans changes topology; public tab export-imports unused Utils at V23 (:10).
Internal copy (curveMapping/wrapCurve.fs): core same-document at newer f32212e7; maps via mapSinglePoint (would pick up
CM_PARALLEL_ARC); public computes shift inline; layout "Advanced options" > "Sampling options" / "Spline approximation
options", Break spans at top; no Print arc-length param option.

## 2 Wrap and Loft (curveMapping_public/wrapAndLoft.fs)
Wrap source edges as Wrap Curve, then offset the wrapped curve along N_to and LOFT a surface strip (through-thickness
direction; sidewall and core-edge reference surfaces following rocker/camber).
Planar shortcut: editing logic (:76-134) samples 4 points per source edge; all within 0.1 mm of a plane -> hidden
sourceEdgesArePlanar, From edge(s) DISAPPEARS; From path built by projecting each To edge onto the source plane, fitted
with max(10, 3 x CPs) samples (:388-455); projected curves deleted at end (:1521-1524).
Dialog (:140-328): Wrap edges; From data (From edge(s) hidden when planar, From reference); To data (To edge(s), Flip,
To reference, Flip normal); Frame orientation as Wrap Curve; Offset (expanded): Primary offset 20 mm (0-100), Flip offset
direction, Second direction -> Second offset 0 (0-100, opposite side); Keep output curves (false) -> Output curve mode
Keep all (enum default) / Keep wrapped only; Details = Wrap Curve Details + Fix control-point clustering (true; merges
runs < 0.25 mm CLUSTER_MERGE_MIN_SPAN :67); Break spans (false); Exact ruled surface (false; wrapped + offset curves
with shared knots -> exact degree-(p, 1) ruled B-spline; falls back to loft if any span fast-path or kernel refuses;
changes surface IDs); Debug as Wrap Curve minus wrap-detail/arc-param/point options.
Outputs: one surface per source curve, unioned into one sheet with N faces (:1383-1491); lofted between wrapped and
primary offset; with secondary, between PRIMARY and SECONDARY (width primary + second) (:1410-1416); kept curves:
wrapped, primary, secondary wires (:1496-1509). reportFeatureInfo recommending clustering fix only when off and slivers
found (:1370-1375); console span warnings, loft failure, union failure.
Algorithm differences: planar From path; fast path rigid copies: primary = transform(sign offset N) * xf (:519-587);
mapSinglePoint + offsetDir = N_to (:622-635); CROSS-CURVE WELD: an endpoint coinciding (1 um) with an already-seen
neighbour endpoint adopts that neighbour's offset direction (:656-682) (fixed offset-edge gaps ~ offset x dAngle);
clustering merge/detect (:692-760); offset points segPoints +- offset segOffsetDirs (:1119-1127); offset end tangents
offsetCurveTangent (core:2596, normal turning); loft or ruled per source curve, union, cleanup.
Examples: width primary 20 + second 5 -> strip +20 to -5, 25 wide, wrapped curve inside. Weld: 1 deg normal jump at a
line/arc seam -> 20 x 0.01745 ~ 0.35 mm gap before. Planar foreshortening: 300 mm R600 tip arc projects to 287.6 mm in
plan; a plan point 287.6 mm past arc start lands at arc length 287.6, 12.4 mm short of the tip end (wraps, doesn't drape).
Figures: strip cross-section (wrapped, primary, secondary, N_to arrows); planar-mode auto From path; weld before/after.
Limits: primary can't be negative (use Flip offset direction); second direction with second 0 = off; planar check needs
non-collinear samples (single straight edge leaves flag at previous value); weld "first neighbour processed wins",
rigid fast-path curve can't bend to meet earlier neighbour; exact ruled changes IDs, silent loft fallback; union failure
console only; planar projection sampling fixed max(10, 3 x CPs).
Internal (wrapAndLoft_i.fs): Output curves enum None (default) / Keep wrapped only / Keep all; Reference sampling mode
/ multiplier / density for projected From curves; Advanced grouping, Break spans + Exact ruled at top; else in sync.

## 3 Deform (curveMapping_public/deform.fs)
Same bending map on a whole solid/sheet body or selected faces (bend a flat-modelled insert, core feature, tip protector
onto the profile). Native only Transform / Bend (flex around an axis).
Dialog (:42-181): Mode Bodies (default; Source body, 1 solid or sheet) / Faces (Source faces; adjacent recombined); From,
To, Frame orientation as Wrap Curve; Setup: sampling mode, multiplier/density, degree, keep source degree, max CPs,
tolerance, Use face guide points (false) -> u/v Sampling multiplier 3 (2-10, capped 25 points per face core:2375), Keep
wires (false); Debug -> Create wires / faces / bodies (cumulative stages), Show from/to frames.
Output: new body; source NOT deleted; solid input closed with makeSolid where possible; Faces mode always sheets
(:587-614); Keep wires adds a composite (:553-575); diagnostics console only (WARNING/ERROR per face, cyan/red debug,
"OPEN END" gap reports :322-539).
Algorithm: paths, references, align line frames (:204-214); map ALL vertices in one batch (mapWorldPoints core:1400) =
canonical ends (:227-239); per edge transformEdges (core:2200): fast path, or sample (length min 5), map, fit, snap ends
to vertex map (core:2269-2322); per face stitch mapped boundary with opExtractWires, optional mapped iso-curve guide
points (transformFacepoints core:2395), opFillSurface G0 (:337-540); union fills, makeSolid for solid input (:593-608).
Example: 100 x 20 x 5 insert at x 1400-1500 on From line bent onto R600 tip: top at n = 5 convex side grows (600 + 5)/600
-> ~100.8 mm; bottom follows reference arc length 100 mm; width 20 unchanged.
Figures: flat block and bent block over profile; three debug stages; face with vs without guide points.
Limits: EVERY face becomes a fill surface (planes, cylinders lost); fill quality depends on boundary + guides, inner
loops may fail; heaviest feature (vertex projection full scan O(N x E)); failed face console only -> check for missing
face / non-solid. Internal copy essentially identical (no icon, longer descriptions, newer core pin).

## 4 Measure curve distance (curveMapping_public/measureBetweenCurves.fs)
Standalone inspection tool, no core. Dialog (:606-733): Measurement Type Closest / Normal dist (default) / Along axis ->
Measurement direction World X / Y / Z (Z) / Custom (XYZ) or normal to plane/vertex/MC; Measurement Name
("MeasureBetweenCurves"); Measure from (G1), Measure to (G0); Point Spacing Number of points (20, 2-200) / Point
spacing (Spacing reference + Distance between points 10 mm, 0.5-100); Spacing type Along curve / Along axis (World X or
Custom); Table formatting: units (mm), include units (false), decimals (3), reverse order, start numbering at 1; Clear
accumulated data; Debug show from/to points and connections.
Measures: Normal / Along axis build a plane through the sample containing the measurement direction (normal =
cross(dir, fitted from-plane normal)), evDistance(plane, toEdges), report dot(Q - P, dir) (:814-832); stations where the
plane misses the To curve skipped; Closest = evDistance(point, toEdges). Output: rows in origin attribute keyed by
feature ID (:862-891); "Measure between curves" table (:894-937) point number, from, to, distance. Errors: straight From
curve in directional modes (:596), axis parallel to plane normal (:820), > 1000 points (:437, :481). Gotchas: deleted
measure feature's rows stay until Clear accumulated data toggled on, regenerated, off; directional modes need curved
planar From; Phase 2/3 unverified in Onshape. Figure: cross-section plane through a From station cutting the To curve.

## 5 Offset edges (example_1/refSurfCreation/offsetEdges.fs)
Offset one G1 chain (sidecut, edge line) by amounts varying along its length in two directions of a transported frame;
regions or control points with blends. Native Offset curve = sketch plane, constant.
FRAME NAMING (important): own parallel-transport table (buildParallelTransportTable :746; seed ptSeedNormal :700 =
curvature normal at s = 0, toward centre for a circle) stored as frame xAxis. In computeOffsetPoint (:1511-1519):
"Normal offset" moves along yAxis = T x N = Frenet BINORMAL (perpendicular to the curve's plane); "Binormal offset"
moves along the CURVATURE NORMAL (in the curve's plane). Code comment "Empirically: yAxis = visual normal". Describe the
result physically, not by the names.
Dialog (:182-449): Reference edges; Reference point (X = 0); Flip direction; Show direction indicator (true; green +,
red -); Flip normal, Flip binormal; Source arcs Convert to splines (default) / Keep as arcs (BIARC); Sampling density
20 (20-100, points per region/blend); Offset layout Multiple regions (default) / Single region. Multiple: Regions array:
Region type Linear (default) / Quadratic (+ Zero slope at start/end) / Smooth; Region name; Extent type X extents
(default; Region start/end signed distances from reference) / Query (2 extent points); Offset type Normal (default) /
Binormal / Both; Start/End normal and/or binormal offset (-100..100 mm, 0); Start/End dwell (0-1000 mm); Region length
(read-only). Intersections array (auto by editing logic :137-168 per consecutive pair): Blend regions? (true) -> Start/End
continuity G0/G1/G2 (G1), Start/End distance (10 mm). Single: Transfer type Smooth (default) / Linear / Quadratic (+ Zero
slope at); Interior offsets array (name, location Query (default) / Position, offset type + values); profile holds flat
beyond first/last points. Approximation: degree 3 (2-5), tolerance 0.01 mm, max CPs 100. Debug: show frames / regions /
blends, print curve / path / edge / frame data.
Profile formulas (:1179-1237): Linear s = t; Quadratic t^2 (zero slope at start) or 2t - t^2 (at end); Smooth 6t^5 -
15t^4 + 10t^3; dwell compresses the ramp into [d0, 1 - d1]; blends Hermite matched to continuities (G1+G1 cubic,
G2+G2 quintic :1600-1680).
Algorithm: processPath (V23 core Frenet path, exact frames on non-line edges; transport table max(100, 4 x density);
reference parameter :852-878); regions -> t-ranges, clamp, overlap warnings (:890, :1131); single-region synthetic
(:1042); blend zones, trim regions, clamp warnings (:2272-2330); split region pieces at source-edge boundaries (:2369);
Keep-as-arcs: piece over a circular edge -> concentric arc if offset constant, else best-fit BIARC (joint by log scan +
golden section, each leg >= 20% of chord, 10 deviation samples :1998-2215); else sample + spline fit with stations and
dwell edges pinned, end derivatives pinned to true tangent (:1532, :2216); blends same way; merge into one wire
opExtractWires (:2505-2512); biarcs -> reportFeatureInfo count + worst deviation (:2500).
Warnings (reportFeatureWarning): no regions / no interior offsets; station outside region / coincident; dwell clamped;
overlapping regions; blend clamped/skipped; region consumed by blends; max CPs raised.
Example: one region 0-300 mm normal offset 0 -> 5: at t = 0.25 Linear 1.25, Quadratic (zero at start) 0.31, Smooth 0.52;
at t = 0.5 Smooth 2.5; 50 mm start dwell d0 = 0.167, ramp over remaining 250 mm; blend default 10 mm into each neighbour
at G1 = cubic Hermite.
Figures: path with direction arrows + offset curve, regions shaded; offset-vs-t of the three types + dwell; biarc on a
source arc with varying offset.
Limits: direction names swapped vs behaviour; gaps between regions -> gaps in output; blending off -> step; output split
at every source-edge boundary; slivers (0.03-0.5 mm) where a region boundary is within ~1 mm of an edge boundary (open);
pre-2026-09-23 "normals not coplanar at junction N" with three consecutive flat arcs fixed on V23 (Design Master untested
until example_1 versioned).

## 9 Unclear
1 Wrap Curve / Wrap and Loft UI text says Frenet "can flip at inflections" (wrapCurve.fs:71, wrapAndLoft.fs:183) -- no
  longer true; deform.fs:88 corrected. 2 public Wrap Curve comments still describe inflection-sign correction (:282-287);
  FRENET_FRAME_USAGE.md actively wrong. 3 Offset edges axis naming swapped; description says "Frenet frame" but uses
  parallel transport -- confirm with user which physical direction each option means on a ski edge. 4 processRegions
  reads station1..5 fields (:947-988) with no dialog controls. 5 public tabs pinned to engine V23; batch 2 pushed but
  public doc not yet versioned -> published may lag. 6 approach.txt PARAM/LENGTH scaling enum, AUTO/MANUAL source points
  not built. 7 "Sampling density" = count only; samples uniform in parameter (core:1768-1771). 8 Wrap and Loft doc comment
  outdated (:54-57; min 5 vs code 10). 9 Measure curve distance Phase 2/3 unverified. 10 G2 jostle in core still wrong
  formula (core:2022/2055) but unused.
