# fillet_wire -- design (2026-09-24)

Curve_tools feature (stub tab `fillet_wire`, c61139f64181d988af590a94). Rounds chosen G0 corners of a curve.

## Inputs

- **Curves**: edges (sketch or body edges) and/or wire bodies, forming one or more chains (open or closed).
  Edges are the common case -- the feature never assumes it owns a wire body.
- **Continuity** (user choice): **Tangent** or **Curvature**.
- **Radius** (always required).
- **Corners**: picked with a `togglePointsManipulator` -- every detected corner is a point; click to include /
  exclude. Corners that cannot be filleted are shown as suppressed (not selectable).
  - **Apply to all** option: every valid corner is filleted with the radius (the picks are ignored; new corners
    upstream are included automatically).
  - **Per-corner radius override**: an array of picked corners, each with an optional radius.
  - Picks are stored as robust vertex queries (makeRobustQuery in the manipulator change function), so they
    survive upstream edits.
- **Corner angle**: tangent jump above which a vertex is a corner (advanced; default as Clean wire).

## Geometry

### Tangent mode -- exact circular arcs ONLY

- Solve the two tangent points: a circle of radius r tangent to both edges. Straight edges: setback
  `r tan(theta/2)`. Curved edges: 2D Newton on (sA, sB) intersecting the edges offset inward by r, in the
  corner plane.
- The arc is an exact rational quadratic (P_A, tangent-line intersection, P_B; middle weight cos(half angle)).
- If the edges are not coplanar at the corner (no circle can touch both), the corner is NOT filleted in this
  mode: notice naming it (arcs only, per user).

### Curvature mode -- G2 blend "as close to an arc as possible"

- G2 at both ends: `computeBridgingControlPoints` (std bridgingCurve.fs) with position, tangent, curvature
  direction and curvature from each edge at the tangent points.
- The tangent points are chosen so the blend's AVERAGE curvature equals 1/r: turning angle / length = 1/r
  (the length an arc of radius r would need for that turn). One-dimensional root find on the setback distance
  (same arc length into both edges). Report the achieved min / max curvature.

## Output

- **New wire** (default): the chain with trimmed edges + fillet curves, joined by opExtractWires; name and
  attributes copied from the input wire when there is one.
- In place: probed 2026-09-24 via the eval API --
  - opSplitEdges on a wire keeps the body (identity kept, edge count grows);
  - opEditCurve on a MULTI-edge wire keeps the body's identity (found by creation history) but collapses it to
    ONE edge (the given curve). So "in place" = the whole filleted wire as one NURBS (exact lines + arcs are
    representable rationally with G1 knots) -- edge structure lost. The stub's "split, edit sections,
    recombine" is not possible: opEditCurve acts on a whole wire body.
  - Decision pending with the user.

## Validation / notices

- Radius too large: setbacks overlap a neighbouring corner or run past an edge -> that corner fails (notice with
  its position); the others are filleted.
- Near-reversal corners (turn close to 180 deg) and tangent vertices are not offered.

## Published outputs (Extract variables)

filletEdges, filletCount, skippedCount (+ which), per-corner achieved radius (tangent) / average curvature
(curvature), standard ends startVertex / endVertex.

## Tests (in the feature tree, checked by devtools script)

Square polyline (exact arcs; length 4 a - 8 r + 2 pi r), planar curved edges (exact circle tangent to both),
3D corner in Curvature mode (G2 at both ends, average curvature 1/r), a skipped corner (radius too big), edges
input (sketch edges, not a wire), closed chain.
