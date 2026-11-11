# Topology-preserving deform: context from the backend project, and a FeatureScript experiment to try

Written 2026-11-10 by the eocProductData (Python backend) session. Read-only context for the curveMapping
work; nothing here changes any `.fs` file. Claims are labelled **verified** (docs/code), **inferred**, or
**unverified**. The backend project is `C:\Users\jed.yeiser\Projects\eocproductdata`; its design notes are in
`backend\deform\DESIGN.md`.

## Why this exists

The FeatureScript deform builds every face independently (a grid-interpolated B-spline surface trimmed by the
source face's UV outline), then sews. Each face's boundary only approximates its neighbour's, so seams are
0.1-13 um apart and sewing is unreliable (pins made it worse; `sewSeams` repairs afterwards). The backend does
it the other way: **preserve the source topology**. Map each unique vertex once and each unique edge once (one
canonical curve per edge), and build every face against those shared edges, so two faces that share an edge in
the source share the identical edge in the result, by construction. No booleans, no sewing.

## Can FeatureScript do the same? (the honest answer)

Not directly. FeatureScript has no operation that builds a face from given edge instances. Parasolid also has
no tolerant edges (**inferred**: edge geometry must lie on its faces to about 1e-8 m), so two independently
built faces can only share an edge if **the kernel computes that edge itself**, as the intersection of the two
surfaces, or by sewing. That is why every approach that builds faces separately ends in seams.

The one route that matches the backend's idea is to let the kernel compute the shared edges, using a **direct
edit that keeps the existing topology**: `opReplaceFace` (**verified** it exists; params `replaceFaces`,
`templateFace`, `offset`, `oppositeSense`; the std feature `std/replaceFace.fs` just forwards them).

### The experiment

Deform the *underlying surface of each face* and replace the face's geometry with it, so the solid keeps its
faces and edges and Parasolid re-intersects the neighbours:

1. For each face of the source solid, take its surface (`evSurfaceDefinition`, or the face's own approximation).
   Build a mapped B-spline surface over the face's UV box **plus a margin** (the template must extend past the
   face so it can intersect its neighbours: **inferred**, test it), using the existing point map.
2. `opCreateBSplineSurface` it as a sheet (no boundary curves = the whole parameter range).
3. `opReplaceFace` the original face with that sheet's face. One template per call (**inferred**: `replaceFaces`
   is a query but all of them get the one template's geometry). Set `oppositeSense` from the normal dot-product
   test in `.claude/featurescript-corrections.md` (~line 187; `DIRECT_EDIT_REPLACE_FACE_FAILED` otherwise).
4. Repeat per face. Expect disambiguation trouble after each call (a forum thread reports it for multi-face
   replacement; **unverified**): query faces by `qCreatedBy`/attributes, not by transient id.

If it works the result is the same faces and edges as the source, edges computed once by the kernel, a closed
solid with no sewing.

### Minimal test and what to measure

Box 100 x 20 x 10 mm bent onto a circle with the closed-form map below (so the exact answer is known), mapped
surfaces from a refined control net. Variants: (A) all six faces in one call, to confirm the one-template limit;
(B) sequential calls; (C) a body with a cylindrical hole.

Measure: does each call succeed (error code if not); closed solid (`evVolume`, naked-edge count via
`qEdgeTopologyType`); face and edge counts and identities kept; **edge position error against the exact map, in um**;
step count and time per face; behaviour with tangent neighbours, small faces, planar faces replaced by
non-planar surfaces.

Risks: neighbours that do not extend far enough to meet the new surface (failure or topology change); a coarse
control net giving a bend error above the 0.1-2 um you care about; analytic faces (planes, cylinders, cones)
needing conversion to B-spline first; faces trimmed by complex outlines.

## The exact oracle (closed form, no fitting)

From chain: a straight line along +X in the XZ plane. To chain: a circle of radius R in the XZ plane, starting at
the origin tangent to +X, bending toward +Z. Reference arc lengths equal (shift 0), `up` = +Y. Then

    (x, y, z)  ->  ( (R - z) * sin(x / R),   y,   R - (R - z) * cos(x / R) )

Layers offset toward the circle's centre (+z) shorten by (1 - z/R); away from it they lengthen. The backend's
`backend\deform\tests\test_mapping.py` (`polar`) matches this to 1e-6 mm, and the FeatureScript map should too.
The map is the same arc-length-shift (isometric, no parallel-arc correction) the FeatureScript already uses.

## What the backend can give you

- **A checker for results**: eocDataTools > Geometry > **Check**. Export a result body as STEP, drop it in:
  validity, free / non-manifold / flipped edges, tolerances, slivers, **near-tangent creases** (normal angle
  across interior edges; the Thicken-killing 0.05-5 deg band), surface/curve census. Same report for FeatureScript
  output and backend output, so the two methods can be compared on identical measures.
- **The reference map**: `backend\deform\services\chain.py` and `mapping.py` (anchored rotation-minimising
  frames, fold check, selectable end extension). Differences from the FeatureScript map are listed in
  `backend\deform\DESIGN.md`; the main one is that the backend's frame is anchored at the reference arc length
  with an explicit `up`, not seeded at the most-curved sample.
- **Pre-flight** (Geometry > Deform): chain checks, direction/flip, reference points, and a mapped preview of the
  source points with fold detection, for the same chains and bodies the feature takes.

## What we would like back

A short note (a section appended here, or a new brief) saying whether the replace-face experiment works, on what
test cases, with the measurements above. If `opReplaceFace` cannot do it, record why (so the backend does not
re-investigate), and the fallback is the one already in use: backend builds the deformed B-rep and Onshape
imports/derives it.

Related, later: bodies may carry **mate connectors**; the plan is to optionally deform those as well and
add/update them (named) in the import part studio.
