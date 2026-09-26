# Research brief: Driven offset family (Explore agent, 2026-09-25)

Paths under driven_offset/. DEO driven_edge_offset.fs, DOS driven_offset_surface.fs, EO evaluate_offset.fs, CoP
create_offset_profile.fs, EOU edge_offset_utils.fs, ORT offset_run_treatment.fs, CC ../curve_tools/curve_core.fs.
unwrap_explained.md already explains the chart, s - d*theta, flat charts (1.2, 1.3, fig01/02) -> LINK, don't re-derive.

## 1 Purpose
Ski needs many curves/surfaces offset from one seed chain (sidewall, cap wall, rout surfaces, top edge); offset VARIES
along the ski (e.g. 0 in the tip, 3 mm in the running surface), often relative to core bottom. Native Offset Curve /
Offset Surface are constant only, no distance-along-a-reference, no transported frame on a 3D chain. A PROFILE CURVE
drives the offset: profile X = station, Y = width offset, Z = height offset (DEO:16-17, EOU:13-22). Use native for a
constant offset of a planar sketch curve or a face offset.
- DEO: "Offset edges by a profile curve" (DEO:51): offset wires with real lines/arcs where the source allows, G1 kept
  at junctions, corners rounded/trimmed, ends landed on planes.
- DOS: sheet surfaces from one or more driven offsets of the same seed (DOS:107-108): rule sideways from an offset,
  connect source to offset, or loft between several profiles (production: SW_Rout_Surface, Cap_Wall).
- EO: DEO backwards (EO:9-19): given reference edges and target edges, output the profile wire DEO would need, on DEO's
  own stations.
- CoP: builds that profile wire from a table of Regions or Points as exact Beziers, breaking where the offset jumps
  (CoP:9-35); replaces hand-sketched profiles.

## 2 Concepts
1. Chain and stations: seed edges ordered into G0 chains, oriented so arc length increases with world X (EOU:13-22);
   stations per edge = CP count x multiplier / fixed count / spacing (CC:118-129), clamped 5..200 (CC:97-98); batched
   one kernel call per edge (EOU:24-25).
2. Transport frame not Frenet: T, W (width), H (height); normal carried by minimal rotation
   N' = N - (N.b/(1+a.b))(a+b) (CC:360-380, transportedNormals CC:404), seeded once at the station nearest the zero
   point. Frenet rolled 86.9 deg at a junction with identical tangents (CC:386-392). Width axis = frame axis with
   larger world-Y component (DEO:29-34). Along alignment uses this frame; World uses X/Y/Z (DEO:654-668); with a
   reference wire H = reference surface normal, W = H x T in the surface (DEO:597-634). Figure: twisted 3D chain,
   Frenet flipping vs transported smooth.
3. Profile X meanings (MeasureAlong EOU:211-219; stationCoordinates DEO:512-543), from the Zero point: World X
   (station.x - zero.x); Along source (default; arc length); Along reference (distance along reference wire offset by
   delta = s - delta*theta(s), EOU:935-953; no offset curve built). Offset delta only changes WHERE each station reads,
   never moves a point (needs +-50 mm to see, mainly at tip).
4. Surface-following placement with a reference: slide in the chart: arc += w*alpha/scale, v += w*beta, height += h
   (surfaceOffset EOU:419-477). Straight step leaves surface by ~w^2/2R ("half a millimetre on a 200 mm tip kick"
   EOU:426-428); exact slide 0.0000 mm off vs 0.196 mm. Figure: tip cross-section straight step vs geodesic slide.
5. Profile as a chain lookup: edges ordered by endpoint connectivity, not sorted by X (keeps direction of approach)
   (EOU:609-623); X monotonic per edge (checkMonotonicX); zero-X-extent edge = step (EOU:570-577); construction geometry
   filtered (a sketch centreline once zeroed a third of a chain); batched Newton inversion x(u) (paramsAtX EOU:299,
   profileAt EOU:830).
6. Breaks and crossings: every profile discontinuity (step, slope kink, profile ends) gets an exact station PAIR
   (insertCrossings DEO:912; ends DEO:173-181); each half reads its own side (sidedOffsets DEO:753-776); regular
   station within 0.5 spacing of a crossing dropped (CROSSING_CLEARANCE EOU:147-159; made fitter hook at 60 /m); break
   within 0.5 mm of a source vertex snapped with NOTE (DEO:881, 985-1004).
7. Runs: stations grouped into runs = one output curve each (buildRuns DEO:782-803); run ends where profile has no
   data, at crossing pairs, at G0 corners the welder declined, and (Every source edge) at every source edge boundary.
   Junctions < 1e-2 rad welded: one averaged tangent, one origin (CC:905-920); 1 mrad at 11.7 mm width = 11 um gap
   opExtractWires couldn't stitch.
8. Treatments (ORT), before geometry: CORNERS at angle theta, width w: outside gap 2w sin(theta/2), inside overlap
   w tan(theta/2) deep (EOU:248-279; ORT:16-31). Gap -> true arc centred on the vertex (tangent to both by
   construction); non-co-radial ends -> arc-like cubic with handle (4/3)tan(theta/4)R. Overlap -> both trimmed to the
   crossing. Figure: one corner, outside gap + arc, inside overlap + trim. FOLDS: 1 - w kW - h kH <= 0 (offsetShrink
   EOU:371-381) or reference mode scale - kappa*height <= 0 -> offset passed the centre of curvature: no point, runs
   trimmed back to each other (DEO:700-737, resolveFolds ORT:234). TERMINALS: end trimmed to a plane or extended along
   its osculating arc (or given direction) to reach it; extension folded into the run (no extra edge) (ORT:304-583);
   capped at 25% of run length (EOU:184-191).
9. Emission (emitRuns DEO:1215-1333): classifyPoints gated by SOURCE TYPE (EOU:59-116): arc only if every station
   came off LINE/CIRCLE; line only if all LINE; else spline fit with exact end tangents (runEndTangent/offsetTangent
   EOU:383-417 incl. frame-rate terms w W' + h H'). No tolerance separates "42 mm of a spline 13 nm from a circle" from
   a real arc.

## 3 Dialogs
DEO (DEO:53-82; predicates EOU:2795-2988): Measure along World X / Along source (default) / Along reference; Name;
Offset spacing definition group (Along reference only): Reference wire, Offset delta (-50..50 mm, 0), Flip offset
delta, Hold length in the reference surface (off); Offset alignment Along (default) / World; Break output at Every
source edge (default) / Corners and offset breaks; Offset edges; Offset profile; Corners group: Where the offset gaps
Round with an arc (default) / Extend to a sharp corner / Leave open; Where the offset crosses Trim to the crossing
(default) / Leave crossing; Ends group: Start plane (MC or planar face), Start direction, End plane, End direction,
Allow extension (on); Zero point (MC or vertex); Spacing & approximation: Offset spacing Control points (multiplier
2..10, 3) / Points per edge (10..50, 25) / Distance along (0.1..50 mm, 10); Join runs into one wire per link (on);
Join tangent segments into one edge (off); Approximation: degree, tolerance, max CPs (4..MAX, 15) (CC:1626-1636);
Debug: Print from chain, along chain, profile chain, Show offset frames, Show offsets, Show reference offset, Show
chain ends (green start red end), Visualize continuity, Print offset table, Print frame table. Defaults map
(DEO:91-96) back-fills joinTangentRuns, runBreakMode.
DOS (DOS:110-201): Surface Ruled from offset (default) / Connect source to offset / Loft between profiles; Name; Offset
edges, Zero point, Measure along, reference group, Offset alignment; Break output at (local, default Every source edge
-- changing once re-ran SW_Rout_Surface with 3 runs instead of 7, DOS:126-134, correction 25); Offsets array (Offset
profile, Name); Ruled: Rule along Width / Height, Distance (+-10 m, 10 mm), Both directions, Sections across the
ruling (2..25, 5; reference only); Loft: Blend through profiles (off -> ruled patches between adjacent profiles); Keep
offset wires (off); Corners, Ends, Spacing, Debug as DEO; Surface debug: Print surface, Only run (-1 all), Keep
section curves. Loft: Leave open refused (DOS:419-423), Leave crossing overridden to Trim with NOTE, joinOutput forced
(DOS:527-534).
EO (EO:66-122): Measure along (Reference wire refused EO:124-127), Name, reference group, Offset alignment, Reference
edges (key offsetEdges), Target edges, Zero point, Spacing & approximation (same keys as DEO), Debug (Show
measurements, Print profile table). Keys match DEO so one definition means the same; must match DEO's spacing (3D
frames depend on it) (EO:89).
CoP (CoP:88-250): Mode Regions (default) / Points. Regions array: Name (editing logic fills "Region n"); Start station
Enter X / At point (Start X, or Start point + Start offset along X); End station same; Shape Constant / Linear
(default) / Smooth; Constant -> Width, Height; else Start/End width, Start/End height, Start/End buffer (>=0).
Intersections array (auto, one per consecutive pair): First/Second region (read-only), Blend (off) -> Continuity with
first (G1) + Distance into first, Continuity with second (G1) + Distance into second. Points array: Station (Enter X /
At point), X or point + offset, Width, Height, To next point Smooth (default) / Linear / Hold. Debug: Print segments.
Editing logic (CoP:300-347): writes stations resolved from picks back, names regions, rebuilds intersections sorted by
start keeping settings when names match. Manipulators: two drag arrows per blended intersection (region A end -X,
region B start +X), clamped, magenta points (CoP:359-413).

## 4 Outputs
DEO: one wire per chain link (joined) or loose curves, named (DEO:1313-1330). Keys (publishOutputs DEO:1350-1393):
curveCount, start/endAction, start/endDistance, start/endSquareness, start/endKink, cornerArcCount, cornerArcRadii
(+ standard output, outputFaces/Edges/Vertices, inputs). Notices: regenError "The offset profile does not reach any
of the offset edges" (DEO:324); NOTE break snapped to vertex, zero-length run not emitted (DEO:985-1004, 1262); ORT
WARNING corner / short run / "lies past its terminal" / extension over budget; NOTE folds, extension off; regenError
"runs parallel to its terminal plane", "direction lies in the terminal plane" (ORT:211-607).
DOS: sheets named Name; offset wires/sections kept only if asked (finishSurface DOS:2610-2690). Keys faceCount,
offsetCount, offsetWire1..N (empty unless Keep wires). regenErrors "Add at least one offset profile", "needs at least
two offsets", named break "inside a run of 'X'" (DOS:329), "No run is covered by every offset"; INFO "steps into N
separate stretches; this mode used the longest" (DOS:1106); WARNING "The loft refused k of n patches... hole at
run:profiles..." (DOS:1932); NOTE "kernel refused an exact patch... falling back to a loft".
EO: one wire per continuous piece; lines where straight else spline; no arcs, no end derivatives (EO:866-909). Keys
pieceCount, breakCount, breakStations, startStation, endStation, measuredStations, stationCount; piece1..N,
startVertex, endVertex, breakVertices (EO:911-961). NOTE dropped stations past the centre of curvature (EO:682).
CoP: one wire per piece; each segment its own exact Bezier edge joined by opExtractWires (CoP:998-1052); blend edges
magenta while editing. Keys pieceCount, breakCount, breakStations, start/endStation, piece1..N,
startVertex/endVertex, breakVertices (CoP:1057-1095). No Name param. Errors (CoP:269, 469, 554-559, 755, 776, 809,
838): duplicate names, overlap, end before start, buffers too long, blends reaching past each other, zero-length blend
over a jump, internal segment gap (CoP:1020).

## 5 Walkthroughs
DEO: offsetStationBase (DEO:210): zero point, buildChain (CC:488), buildAlongReference (EOU:953; theta, xs,
arcSlopes), chainStations (CC:757; transported frames + weldJunctions CC:920), stationCoordinates (DEO:512);
buildProfile (EOU:553) + discontinuityCoords + ends -> break list (DEO:171-184); offsetStationsWithBreaks (DEO:241):
insertCrossings (DEO:912), resolveFrames (DEO:556) -> stationFrame (DEO:584), caches surfCoords; planOffset (DEO:296):
profileAt upper/lower, sidedOffsets, offsetPoints (DEO:682; straight step or surfaceOffset, fold tests); buildRuns
(DEO:803), resolveCorners (ORT:32), resolveFolds (ORT:234), resolveTerminals (ORT:304); optional tangent merges
(DEO:279-284); emitRuns (DEO:1215): line / arc (sketch) / spline exact end tangents; extract wires per link;
debugOutput; publishOutputs.
DOS: driveOffsets (DOS:371): one sharedOffsetContext with union of every profile's breaks (station i = same arc length
in every profile); planOffset per profile; alignRuns (DOS:293) clusters runs into shared CELLS by station overlap;
tangent joins decided once (alignedRunMerges); offset wire emitted only if read (DOS:552-566); modes ruleFromOffset
(DOS:597; sectionDisplacement), connectSourceToOffset (DOS:1246; longest span), loftAcrossOffsets (DOS:1288:
sectionPlan -> coupledFits shared knots -> emitSection -> loftColumns DOS:1681). Patch order: ruledPatch (DOS:2368;
exact B-surface when knots identical), unifiedPatch (DOS:2093; bspline_compat unify), cornerPatch (DOS:1498; rational
quadratic sectors), opLoft fallback; finishSurface (DOS:2610).
EO: describeTargets (EO:185; non-rational, correction 39) -> offsetStationBase/Frames -> targetVertexCoords (EO:293)
-> offsetStationsWithBreaks -> cutTargets (EO:407; bracket by section-plane side, safeguarded Newton), inCornerOverlap
(EO:545) rejects wrong-side cuts -> measuredRows (EO:643; w = (P-O).W, h = (P-O).H) -> buildPieces (EO:699) ->
emitPieces -> publish.
CoP: resolveStations (CoP:447) -> regionPieces (CoP:745) / pointPieces (CoP:873): segments = polynomial in x: constant
buffer deg 1; linear deg 1; smootherstep 6u^5 - 15u^4 + 10u^3 deg 5 (CoP:506-516); Hermite blend with analytic
derivatives deg n0+n1+1 (CoP:611-680) -> bernsteinOf (CoP:971; exact Bezier) -> buildPiece (CoP:998) -> publish.

## 6 Examples
Corners: test profile junction breaks 56.2, 123.5, 67.3, 90.0, 78.0 deg: stations 18, 54, 75, 87 gaps; station 36
(123.5 deg) overlap 9.311 mm deep; chord matched 2w sin(theta/2) to 3 decimals.
Surface slide: width-only offset from seed endpoint on REF_WIRE (R=200, w=10): 10.0000 mm along the arc vs chord
9.99979; straight step 0.196 mm off, slide 0.0000; height erected at wrong arc error h*kappa*ds = 0.30 mm at h=10.
Welding/frames: 1 mrad -> 11 um gap; Frenet rolled 86.9 deg; omitting frame-rate term 5.5 deg end-tangent error, with
it 1e-6 deg.
Crossing clearance: 0.158 mm first gap -> fit hooked at 60 /m (R 16 mm on a 15 m radius); after fix 0.058-0.061 /m
(correction 23).
DOS Design_Master: Cap_Wall (seed Pinch_Ref_Wire, 32 edges incl 0.1 mm slivers, two 9-segment profiles): 10 sheets ->
1 sheet 16 faces (13 runs + 3 corners) on "Corners and offset breaks". SW_Rout_Surface 7/7/9 runs, 16 faces. Arc merge
guard: radii 15763.1/15762.5/15767.2 averaged to 15775.2 before ARC_MERGE_RADIUS_REL = 1e-4 (EOU:44-57).
EO ("Evaluate offset tests", research_evaluate_offset.md 0): 11/11. E1-E6 measured vs original worst 5.5 um (E1 line,
E2 arc R400, E3 jump -> 2 pieces, E4 World frame / World X, E5 outside G0 corner, E6 inside G0 corner); R2-R6 drive DEO
with measured profile vs original target worst 10.4 um; all at 5 mm spacing; at 71 mm only approximate.
CoP ("Offset profile tests", check_offset_profile.py:66-135; T2-T4 descriptions inferred from checks): T1 FCP/ACP 3
pieces, breaks 0 and 1000, width 0/3/0 at -50/500/1050, 4 break vertices; T2 smooth region with buffers 3 edges, C2
joints at 2010 and 2080, width 0/2/4, height 1; T3 G2 blend 1 piece, width 1.6 at 3080 and 5 at 3120, G2 joints; T4
smooth points no overshoot 4000-4100, width 3 height 2 at 4050; T5 point jump 2 pieces break at 5050; T6 constant
region + point/MC stations one piece 6000..6350 width 3 at 6225; T7 points mode MC station minus 50 ends at 7100
width 3. Errors: overlap, buffers 600+600, 0/0 blend over a jump.

## 7 Limits
Consumer-side break handling (None / Line per break) for CoP breaks not built in DEO/DOS (DOCUMENT_MAP:130). Length
follows source tangent; reference-driven length/width = open user decision. DOS: CONNECTED lofts longest covered
stretch only, seed trimming and corner fans not wired (DOS:44-46); ruled and single-profile ignore corner fills; Blend
still opLoft. Fit tolerance: curvature resolution ~8 tol/L^2 -> at 10 um default a 53 mm run wobbles +-0.03 /m;
recommend 1 um for ski radii; runs capped at 15 CPs print WARNING -> raise to 25-30. Line gate (curved source never
emits a line) proposed only. EO: no reference-wire measure, 3D reference untested, no icon, no end derivatives. CoP:
one shape for width and height channels; user edited T6 so it fails by design. Correction 25 (annotation default
migrated into saved instances); 20 (no < > in visibility conditions); 21-24, 26 (coupled fits, opLoft refusals,
derivative magnitude, creased B-surface, velocity vs length axis). evEdgeCurvatures unreliable at edge ends
(relaxEndCurvature HOLD). Station count N must be stable or transport seed moves.

## 8 Location/tests
Doc driven_offset f61d2c000ab2d1240776342e, ws 5b11f323ab31b04cba8b36ef, Design_Master element
643b702dc551feb3cc001826. Tabs: DEO 786f62f4d67ed8d9c7d56d16, DOS c47cbd0497baf3011c60ecf8, EO a2ebb5abc7ddda01f64ff8df,
CoP 3fccdcb24013c744bd0fd8a2, EOU a2665e22c07b7a6929ce4e80, ORT d009ddf4a8dd9534fc4dc4b5, offset_debug
6479d7fbd0ec7d11e0ae6c69, bspline_compat 6b635e74c92bd23387e850c1. Curve machinery: Curve_tools V2 curve_core
(DOCUMENT_MAP:9-26, 41-62). Studios: "Evaluate offset tests" 0ce4ac09e693f8ecd18e8a7f, "Offset profile tests"
e18678532ec07b057b372dbd. Tooling: check_offset_profile.py, check_evaluate_offset.py, build_*_tests.py,
fingerprint.py compare design_master_default | design_master_complex --complex (tail_notch;Slotted;Baseline).
create_offset_profile_tests.fs does NOT exist (tests are real instances).

## 9 Unclear
1 DOCUMENT_MAP import pins stale (says EOU 904e3070, unwrap 70dcbbcd; code all on 19bb4e2dc00faa5df759cf24).
2 CoP design says one B-spline with knot multiplicities; built = one Bezier edge per segment joined (continuity at
  edge joints). 3 CoP "exact only when consumer measures along World X from x = 0" vs DEO reading profile X relative
  to the zero point's X. 4 DOS docstring aspirational (DOS:18-34 vs status 44-46). 5 memory says MULTIPROFILE forces
  EXTEND+TRIM and hides Corners (09-19); code (09-20) only refuses OPEN, overrides KEEP. 6 DEO header width-axis rule
  vs reference-mode sign follows chain's own width axis (DEO:619-633). 7 DEO/DOS on VT V1, EO/CoP on V2. 8 T1-T5
  names inferred -- read studio instance names.
