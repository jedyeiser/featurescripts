# evenly_divide_surface - design (draft, 2026-10-17)

Document: `evenly_divide_surface` (69ab3696.../c176011b...), Part Studio 1 has the fixtures. Local project folder is
`evenly_divide_surface/` (registered in featurescriptSettings.json).

## Test fixtures (read from the document, 2026-10-17)
| Body | Notes |
|---|---|
| `roving_seed_surf` (full) | sheet, 47 faces (12 cylinders, 35 free-form), x -875..875, y -54.9..54.9, z 1.8..40.6 |
| `roving_seed_surf` (half) | sheet, 27 faces (8 cylinders, 19 free-form), same but y 0..54.9 |
| `ctr_wire` | one edge along X, x -902..902, y = 0, z 3.9..57.6 -> longer than the surface at both ends |
| Front plane / Top / Right | symmetry plane candidate = XZ plane (y = 0); the half body ends on it |

## Inputs (from the brief)
1. Surface body (full / near-closed, or half). Must work for both.
2. Cross-section wire (wire body or edges; may be several edges; may extend past the surface).
3. Symmetry plane (defines the 0 / 180 degree locations). 3a. boolean: first/last division ON the plane, or a half step off it.
4. Number of divisions. 5. Number of cross sections along the wire. 6. Consume or keep the wires (default true).
Optional (phase 2): strip width, strip thickness (mid-plane / second direction), overrides array (wire #, skip, width, thickness).

## Algorithm
1. Build one path (`constructPath`) from the cross-section wire; place N_sec planes at even arc length, each normal to the
   path tangent (`opPlane`).
2. For each plane: `opIntersectFaces` (plane x surface faces) -> section curves; join them (`opExtractWires` / path) and measure.
3. Split each section at the symmetry plane into its 0..180 half-loops; the fractional position f along a half-loop (by arc length)
   is the "x%" in the brief.
4. Divisions: the f values come from (4) and (3a):
   - on-plane: f_i = i / N, i = 0..N (N + 1 wires per side)
   - half-step: f_i = (i + 0.5) / N, i = 0..N-1 (N wires per side)
5. Point at f_i on each section; one wire per i through the section points (`opFitSpline`), then pulled onto the surface
   (`opDropCurve`) so the wires lie on the faces.
6. Phase 2: strips by offsetting each wire +/- w/2 on the surface, thicken, apply overrides, consume the wires.

## Open questions (blocking the wire definition)
A. Divisions: N per half (0..180) with the full body mirrored, or N over the whole 360? And N divisions = N+1 wires (on-plane) / N wires (half-step)?
B. Plane range: spread N_sec planes over the whole wire length, or over the part of the wire that actually meets the surface (the wire sticks out ~27 mm each end)?
C. Direction of the 0 -> 180 measure on the full body (which of the two symmetry crossings is 0)?
D. Tolerance for "lies on the face": is spline-through-section-points + drop-curve acceptable, or must the wire be exact iso-lines of the faces?
