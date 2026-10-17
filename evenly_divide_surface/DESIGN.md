# evenly_divide_surface - design and status (2026-10-17)

Document: `evenly_divide_surface` (69ab3696.../c176011b...). `Part Studio 1` holds the user's fixtures and is only READ
(the user works in it); tests live in the separate "Divide tests" Part Studio, which derives everything from Part Studio 1.
Feature file: `evenly_divide_surface.fs` (feature `evenlyDivideSurface`, FS 3083). Tests:
`devtools/onshape/build_evenly_divide_tests.py` / `check_evenly_divide_tests.py` (E1-E4, 4/4 pass 2026-10-17).

## Fixtures
| Body | Notes |
|---|---|
| `roving_seed_surf` full | sheet, 47 faces, x -875..875, y -54.9..54.9, z 1.8..40.6 -> closed sections |
| `roving_seed_surf` half | sheet, 27 faces, y 0..54.9 -> open sections ending on the symmetry plane |
| `ctr_wire` | one edge along X, x -902..902: longer than the surface; tilts up at the tips |
| Front plane (y = 0) | symmetry plane. Its normal points to -Y, so "positive side" = -Y (wire 1 runs toward -Y) |

## Decisions (user, 2026-10-17)
- A: N divisions around the PERIMETER. Closed (full) 0-360: N wires at i/N (on plane) or (i+0.5)/N. Open (half) 0-180: N wires
  at i/(N-1) (on plane; both ends) or (i+0.5)/N (half step). N odd + half step on a closed loop puts one wire at 50% = the
  bottom crossing of the symmetry plane (correct, not a bug).
- B: planes evenly spaced over the part of the wire that meets the surface. C: start = the uppermost (world Z) crossing of the
  symmetry plane. D: spline through the section points, "on the surface" -- see below for what was actually built.

## Algorithm (built)
1. Surface extent along the wire: project the surface's vertices and edge samples onto the wire.
2. COMPLETE-section range: near a trimmed end the wire tilts and a normal plane clips only a sliver of the surface (79 / 39 mm
   fragments of a 222 / 111 mm loop). Bisection (12 steps per end) finds where sections become complete (closed loop, or an open
   arc with both ends on the symmetry plane); planes are spaced over that range (1 mm margin).
3. Per plane: `opPlane` + `opIntersectFaces`, `constructPath` (tolerance 1e-5 m), crossings with the symmetry plane (sampled sign
   changes + regula falsi), start = uppermost, divide by arc-length fractions.
4. One `opFitSpline` per division through its point on every section (chord-length parameters).
5. Refine sections (default on): where a wire strays > 0.01 mm from the surface between two sections, add a section midway and
   refit (<= 4 rounds, <= 600 sections). Result: gap < 0.01 mm everywhere (E1-E4: 86-99 sections from a start of 60).

## Rejected
- `opDropCurve` of the fitted spline onto the surface: projects onto every face the curve can reach and returns 34-83 fragments
  for 5 divisions. Replaced by step 5.

## Open / next
- Phase 2: strip width, thickness (mid-plane / second direction), overrides array, "consume wires" boolean (default true) --
  not built; with no strips the wires are the output.
- Performance: see "Performance" below; remaining ideas: cheaper end probes, keep planes between refinement rounds, derive
  section points without building every intersection wire, 1 opFitSpline per round for all wires.
- Wire numbering / direction and naming of the output wires are not decided.
- The first/last section stops ~1.3 mm short of the surface end (complete-section margin).

## Performance (2026-10-17, `devtools/onshape/bench_evenly_divide.py`, full surface, seconds per regen)
Two reviewers (op count; evaluation cost) were briefed read-only; their lists overlapped and were merged. Done, in order:
1. Refinement re-checks: 2 samples inside each interval placed with the spline's own parameters (no nearest-point search),
   `arcLengthParameterization:false`, face-local `evDistance` (bounding boxes, exact within the inflation), only intervals
   next to new sections re-measured after round 0, weak intervals split by sqrt(error/tol) (square law: near a face seam the
   error only halves per round; rounds cost far more than sections).
2. Local path evaluator (`pathEvaluator`/`evaluatePath`): edge lengths once per section, one `evEdgeTangentLines` per edge touched
   (std `evPathTangentLines` recomputes lengths every call and does one kernel call per parameter), plus regula-falsi early exit.
| case | before | after |
|---|---|---|
| 5 wires, 60 sections, refine, 0.01 mm | 17.4 | ~10 (12.4 off-plane) |
| 16 wires, same | 46.1 | ~15 |
| 16 wires, 0.1 mm tolerance | | 10.3 (8.7 with 30 sections, 6.3 without refine) |
Breakdown at 5 wires: end probes ~3.6 s (fixed), 0.063 s per section (~3.9 s for 60), refinement ~5 s. Not done: reviewer item
"cheaper probe completeness test / fewer bisection steps" (~0.5-1 s), incremental final verification pass.
## Per-face wires, naming, phase 2 UI (2026-10-17, later)
- A wire is ONE BODY OF SEVERAL EDGES, one curve per face run (user's point: one smooth curve cannot follow faces that meet at
  creases). Each division point records its face (`nearestFaceIndex`). `buildRuns` cuts a wire into runs of consecutive sections
  on the same face; at a face change `seamPoint` finds the crossing (closest point between the faces' shared edge and the chord
  through the two section points; a wire running ALONG the seam is not split; non-adjacent faces / no usable seam keep one run
  over both faces). Each run: `opFitSpline` (`opPolyline` for 2 points) through its points + seam points, projected with
  `opDropCurve` onto its ONE face (multi-face runs are not projected: a projection onto both faces of a seam crashed
  regeneration), accepted only if its length is within 1% of the fitted curve, then `opExtractWires` joins the pieces.
  Refinement now measures the per-run curves (no creases to bridge). Result: exactly on the faces (0.00000 mm) for projected runs.
- Section points within 0.2 x spacing of a seam point are dropped (a curve through two nearly coincident points kinks): worst
  turning rate on E2 fell 2823 -> ~215 /m (typical wire ~5-35 /m). A residual spike ~215 /m near x = -871 (tail) remains on the
  full surface; may be real surface curvature at the severe creases there.
- Wires are named "<prefix><i>" (prefix default `even_divide_curve_`), i = 0 at the start point; overrides use the same numbers.
- Phase 2 UI (no geometry yet): "Create strips" (driving boolean, group inside the `if`), strip width, strip thickness,
  mid-plane / opposite direction / second direction + second thickness, "Consume wires" (default on), overrides array
  (wire number, skip, override width + width, override thickness + thickness). `stripPlan` resolves the per-wire settings and
  warns about out-of-range or duplicate overrides; the banner reports it ("Strips: 4 of 5 wires, ...").
  Test cases S1/S2 (build with DIVIDE_PHASE2=1) check the arithmetic; messages were verified in the UI banner.
- Tools: compile notices for the Feature Studio can be read WITHOUT the sync login: Playwright -> open the Feature Studio tab ->
  click `.notice-pane-toggle-button` -> read `.notices-container table.feature-script-notice-table` (the sync tool's own check needs
  `! python -m sync.main login`, which expires within hours). Feature banners: double-click the feature and read the dialog banner.
## Strips (2026-10-17, built)
User decisions: width measured along the surface perpendicular to the wire; strips run to the surface boundary; an edge wire on a
half surface is clipped (strip = half width); overlapping strips stay SEPARATE bodies; "Consume wires" (default on) and
"Consume surface" (default off); thicken optional (mid-plane / opposite direction / second direction thickness).
- Strip edges = two more "tracks" per strip, computed in every section: the wire's division point +/- width/2 of ARC LENGTH along
  the section curve (open sections: clamped at the surface boundary; a track entirely clamped needs no cut). They go through the same
  per-face run builder, refinement and projection as the wires (refined with them: a strip edge fitted from sections refined only for the
  wire strayed 0.9 mm on the ridge).
- Per strip: `opExtractSurface` copy of the surface -> `opSplitFace` along the two strip-edge wires (each continued 6 mm straight
  beyond both ends so the cut reaches the surface boundary) -> cut edges found BY POSITION (edges of the copy lying on the strip-edge
  wires, 0.05 mm) -> flood from the faces the wire runs on across every edge that is not a cut (explicit face-by-face BFS; query-algebra
  flooding leaked) -> extract the faces (zero-area slivers dropped) as a sheet -> optional `opThicken` -> name `even_divide_strip_<i>`.
  A fresh copy per strip keeps overlapping strips apart. Attempt 2 also blocks the edges opSplitFace reports (they include every
  existing shared edge on the full surface, so they over-block there but rescued S4 strip 1).
- Guard: a strip whose area is more than 1.5 x (width x wire length) is rejected (an incomplete cut let the flood escape).
- Measured (devtools/onshape/build_evenly_divide_tests.py, DIVIDE_PHASE2=1; strips_eval-style area/volume checks): S3 half 12 mm
  sheets 5/5 exact (10,550 / 21,101 / 21,125 / 21,134 / 10,567 mm2 = width x 1760 mm; edge strips clipped to half); S4 full 10 mm
  mid-plane 2 mm: 5/5 built, volumes = area x 2; S6 full 30 mm 5/5 exact; S5 full 60 mm overlapping 5/5 separate bodies (strips 2, 3 about 7%
  under nominal); S1/S2 override arithmetic confirmed by volume (width/thickness overrides, skip, second direction); S8 consume wires +
  surface verified. Cost: ~30-50 s per 5-strip case at 5 wires.
- LIMITS: (1) thicken fails ("THICKEN_FAILED") on strips with ~60 degree creases between adjacent faces (S4/S2 strips 1 and 4 on the
  full surface); per-face thicken + union is non-manifold (slabs only touch along the crease): the strip stays a sheet with a warning.
  (2) S7 (45 mm on the full surface): strips 2 and 3 (wires on the lower half) rejected: the cut near the tail does not close;
  diagnosis in flood_eval-style replay: the flood leaves through a big face the cut enters but never fully crosses (per-face
  opSplitFace calls did NOT fix it). Next ideas: complete the cut with extra projected tool edges across the tail faces, or classify faces
  by section-arc coordinate instead of flooding.
- Debug aid: "Keep section curves" also keeps and names the strip edges (`strip_edge_<wire>_a|_b`).