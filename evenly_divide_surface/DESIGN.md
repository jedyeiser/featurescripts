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
- Performance: ~30 s regen per case (~400 ops: probes + sections). Ideas: fewer bisection probes, cheaper initial count, keep
  planes between refinement rounds, derive section points without building every intersection wire.
- Wire numbering / direction and naming of the output wires are not decided.
- The first/last section stops ~1.3 mm short of the surface end (complete-section margin).
