# Unwrap PART mode -- research + prototype (2026-09-25)

Subject: the PART branch of `driven_offset/unwrap.fs` (docstring: "planar deform" default,
preserve-length profile {MIDDLE, NEUTRAL_AXIS, QUERY}). Test studio: Unwrap_Testing
(doc f61d2c00, element 56e5b4d0), reference wire REF_WIRE RtjD, alignment MC at (885, 0, 0).
Bodies: CORE Rtjn, 4803_3D Rtj3 (tail ext.), 4802_3D Rtj7 (tip ext.), base 4101 RtjP, also
sidewall 4103 RtjH and 4401 Rtjv.

Nothing was pushed, posted or saved in Onshape. Everything ran through the FS eval API, in a throwaway
context. Scratch: `scratchpad/part/` of session 51e06872 (`chart_lib.fs` = chart, `piece_head.tmpl` +
`piece_tail.tmpl` = the prototype, `mk.py` builds `p_<body>_<check>_<exactWalls>_<d>.fs`, `run.sh`,
`ev2.py` = eval runner with a 300 s timeout; the stock client times out at 30 s).

## 0. Summary

| Question | Answer |
|---|---|
| Face types in the chart frame | **Every face** of core, 4802, 4803, base, 4103 and 4401 is either a **profile face** (its image is exactly an extrusion along flat Y of an XZ curve) or a **wall** (its image is within 0.0215 rad of vertical, an extrusion along flat Z of a plan curve). There are no "other" faces on these parts. |
| Recommended algorithm | **Piecewise rigid + cell rebuild.** Split the body by planes normal to W where W changes between a line and a curve. Pieces over a LINE move by one exact rigid `opTransform`, so every native face (arcs, cylinders, B-splines) is kept. Pieces over arcs/splines are rebuilt: one fitted tool sheet per face chain, a box split by all the tools, cells kept by an inverse-map containment test, then a union. |
| Accuracy (sampled faces + edges, mapped exactly, distance to the flat body) | core 8.3 um (a tolerant vertex in the original; faces < 1 um), 4802 0.79 um (exact walls) / 43.6 um (walls forced vertical), 4803 0.81 um, base < 1 nm, 4103 0.03 um, 4401 0.25 um |
| Time (eval API request, API overhead 0.2 s) | core 2.35 s, 4802 4.8 s, 4803 4.2 s, base 2.7 s, 4103 ~4 s, 4401 ~3 s |
| Strategy A (side profile x Y  INTERSECT  plan outline x Z) | Correct for 4802, 4803 and base (the cell rebuild keeps exactly 5 cells, which is A). **Wrong for the core** (ledge at h 2.24, 8 grooves whose floors run out to the bottom), so on its own it is not general. The cell rebuild covers A. |
| Strategy B (grid-fit each face, then `opEnclose`) | Not needed: no face needs a free-form fit. Enclose has a structural problem: untrimmed images close the groove openings and enclose fills them. B's per-face fit remains the fallback **tool** for a future "other" face inside the cell rebuild. |
| Cell rebuild of the WHOLE core, with no rigid split | 650 cells, 32 s, `BOOLEAN_NON_MANIFOLD_RESULT`: the groove floors run out onto the bottom face, so the cells touch each other in degenerate ways. The rigid split avoids this. |
| Length choice (d = preserve-length offset) | Core: at most 0.09 mm per end, because W is straight under 1480 of its 1500 mm. **4802: 2.4 mm** between d = 0 and d = 3.8 mm, 0.64 mm per mm of d. 4803: 0.18 mm per mm of d. Base: 0.34 mm per mm of d. |
| World-vertical walls in the rise | None on these parts: all walls are chart-normal to within 0.0017 rad, and to 0.0215 rad at the 4802 nose. A world-vertical wall at theta would lean by about Nx*sin(theta) in flat: up to 30 deg at the tip nose (theta = 0.59). |

## 1. The map, and what stays exactly planar or extruded

W is a planar tangent chain in XZ: tail arc R900 (x -82..145), line (145..1625), tip arc R406
(1625..1709.4), tip spline (1709.4..1808.8, nearly an arc, kappa ~3.94/m). Chart: p -> (s, y, h) with
foot on W, t = (cos th, 0, sin th), n = (-sin th, 0, cos th). Flat: X = s - sA - d(th - thA), Y = y,
Z = h (d = 0: length kept along W). The frame (t, Y, n) is right-handed, so there is no mirror.

Jacobian: dp = (1 - kappa h) t ds + Y dy + n dh, dF = (1 - kappa d) ds e_X + dy e_Y + dh e_Z. A world
face normal N therefore maps to the flat normal

    N_flat ~ ( (N.t)(1 - kappa h)/(1 - kappa d),  N.Y,  N.n )

- **N.Y = 0 (the face is a world extrusion along Y)**: its image is exactly an extrusion along flat Y.
  Its profile is the image of any section y = const (a 2D map of the XZ plane). This holds for any d.
- **N.n = 0 (the wall is extruded along W's normal)**: its image is exactly vertical, an extrusion
  along flat Z of the plan curve (s, y). This also holds for any d, and for any length curve h = g(s)
  (section 6.2), because every such map is (s, y, h) -> (X(s), y, h).
- A world-VERTICAL wall (N.Z = 0) has N.n = -Nx sin(th). It is vertical in flat only where the plan
  normal has no X component or the rise angle is zero.
- Over a LINE span of W (theta constant) the map is one rigid motion, for any d. Split planes: the
  planes normal to W at the line/curve junctions. For points on the concave side of W (h < R) the
  foot stays on the line between those planes, so the split is exact.

Classification used (tested; sample on-face points of a 9x9 grid, normalised):
`PROFILE` if max|b| < 1e-6 and max|c| >= 1e-6. `WALL` if max|c| < 1e-4, which includes faces that are
both, such as planes normal to W. Walls with 1e-4 <= max|c| < 0.05 are `RULED` in exact mode and
`WALL` in forced-vertical mode. Anything else FAILS. Here a, b, c are the N_flat components above,
normalised.

## 2. Findings per body

The chart-frame census (`f_core.txt`, `f_ext.txt`, `f_side.txt`) gives these bounds:

| Body | Faces | Profile faces (b = 0) | Walls: max \|c\| | Notes |
|---|---|---|---|---|
| CORE Rtjn | 90 | 22: top thickness profile RvjC (h 6.0..13.7), bottom h 1.64, ledge h 2.24, 8 groove floors (h 2.59..8.72, run out to 1.64 at x 302/1463), end tops h 4.8 / 5.8 and end planes (normal to W, c = 0) | 60 walls exactly 0 (line span). 8 end-region walls <= 6.3e-5 (lean < 0.2 um) | Only 135..145 and 1625..1635 lie over arcs (theta <= 0.025). The 1500 mm flat length is exact with d = 0 (X -750..750). |
| 4803 Rtj3 (tail) | 18 | top h 4.8, bottom h 1.8: constant, so a **3.0 mm plate** in the chart; end planes | 12 walls <= 6.3e-4 (lean <= 1.6 um over 2.6 mm) | Entirely over the R900 arc (theta up to 0.178). Flat: 18 faces, top/bottom exact planes (4.80002 / 1.8). |
| 4802 Rtj7 (tip) | 17 | bottom h 1.8, top h 5.794..5.807 (**13 um from a constant offset**), nose plane Stjuj tilted 0.0217 rad from normal-to-W, end plane | 10 walls <= 0.0017. **Nose corners Stjej/StjGk: 0.0215** (lean 74 um over 3.5 mm) | Over R406 arc + spline (theta up to 0.64). Nearly a 4 mm plate. The nose and its two corner walls are the only faces not normal to W. |
| base 4101 RtjP | 40 | 8 (h 0 / 1.2, planes after unwrap) | 32 walls **exactly 0**, even in the tip/tail rise | Built through this very chart: flat length is exactly 1790.000 (X -885..905) with d = 0. |
| 4103 RtjH, 4401 Rtjv | 41 / 46 | yes | <= 0.002 | 4401's top faces are NOT exact Y-extrusions (b up to 1.3e-3), but they all lie over the line span, where the rigid move does not care. |

Hint from the numbers (not a finding): with d = 1.8 mm, the extensions' bottom face, the extensions
end at X = -899.90 (4803) and 919.90 (4802). With d = 0 they end at -900.22 and 920.96. Base and core
come out round with d = 0. The extensions may have been wrapped keeping length along their own
bottoms. Ask the user.

## 3. Strategies evaluated

**A. Two-profile intersection.** flat = extrude_Y(mapped side region) INTERSECT extrude_Z(mapped plan
region). It is exact when the body equals (side region x Y) INTERSECT (plan region x Z). 4802, 4803
and the base meet that condition: the cell rebuild keeps exactly the A cells, and its accuracy is the
table in section 5. The core does not: the ledge (lower 0.6 mm narrower) and the grooves (plan
rectangles lowered to a floor profile) are not in either region. A needs no membership test, but it
needs both regions as closed loops. In the chart, x-facing planes are both "profile" and "wall",
which makes building those loops fiddly. **Superseded by C, which contains it.**

**B. Face-by-face grid fit + `opEnclose`.** Not prototyped, by design. (1) No face needs it: every
face is a 1-D curve times a straight direction, and a 2-D fit only adds error. (2) `opEnclose` fills
every enclosed region. An untrimmed mapped face, such as the core top over the groove openings,
closes regions that must stay empty. Trimming each mapped face by its mapped boundary means
`opSplitFace` per face plus gap tolerance. (3) Watertightness depends on fit error at every shared
edge. Keep B only as the tool generator for an "other" face, fed into C (tools are just surfaces).

**C. Cell rebuild (prototyped, works).** One extended tool sheet per face chain, a box split by all
tools, and a membership test for each cell. The trimming comes from the arrangement, so fit error is
never a gap problem: a cell boundary is always a tool surface. Cost grows with the number of tools.
The whole core (31 tools, 650 cells) takes 32 s and fails the union on the groove run-outs. Curved
spans only (4802: 16 tools, 182 cells) take 4-5 s.

**D. Piecewise rigid (prototyped, works).** Split at the line/curve junctions, `opTransform` the line
pieces and run C on the curved pieces. The core becomes a 3-piece job: 84 native faces moved rigidly,
two 10-face end pieces rebuilt, 2.35 s. **Recommended: D + C.**

Not viable: `opWrap` (it unwraps faces lying ON a cylinder or cone, not solids or offset faces) and
sheet-metal flatten (it needs sheet-metal parts).

## 4. Recommended algorithm (tested in eval)

```
chart = unwrap chart of W (d, alignment)               // unwrapChart / unwrapFast in production
1. pieces = split body by planes normal to W at every line<->curve junction the body straddles
2. for each piece: foot of its centroid -> W edge kind
     LINE  -> opTransform(rigid)                         // exact, native faces kept
     curve -> cellRebuild(piece)
3. union all pieces
cellRebuild(piece):
   a. classify faces (PROFILE / WALL / RULED / FAIL) from on-face samples
   b. for each face: sample the row along its curve direction (spacing 0.5 mm, >= 17 points) at
      mid-param of the other direction; map exactly -> 2D points (XZ for PROFILE, XY for WALL);
      RULED: rows at the bottom and top params, 3D
   c. chain rows of the same kind whose ends meet (5e-5 m) with tangent dot > 0.99999;
      drop chains coincident with an earlier one (3 points within 2 um)
   d. tool per chain: fit (2 points if straight) at a plane just outside the box, extrude across the
      box (Y for PROFILE, Z for WALL), extend ONLY the side edges (the extrusion-direction lines) by
      the box diagonal. RULED: fit the rows pushed along their rulings to z below/above the box, loft
      the two curves, extend the side edges.
   e. box = flat bbox + 2 mm; opSplitPart(box cells, tool) for every tool (keepTools)
   f. keep a cell iff unflat(interior point) is inside the piece (qContainsPoint)
   g. union the kept cells
```

Key snippets as tested (the full script is in `piece_head.tmpl`, `piece_tail.tmpl` and `chart_lib.fs`):

```featurescript
// rigid move of a piece over a LINE edge m (th0 = its angle, s0 = arc at its start)
const t = vector(cos(m.th0 * radian), 0, sin(m.th0 * radian));
const n = vector(-sin(m.th0 * radian), 0, cos(m.th0 * radian));
const cs = coordSystem(vector(m.p0[0], 0, m.p0[1]) * meter, t, n);
const xoff = m.s0 - chart.sA - chart.d * (m.th0 - chart.thA);
opTransform(context, id + "rigid", { "bodies" : piece,
        "transform" : transform(vector(xoff, 0, 0) * meter) * fromWorld(cs) });

// junction split: plane through the junction point, normal = W tangent there
opSplitPart(context, id + "jsplit", { "targets" : mine, "tool" : plane(org, tangentAtJunction) });

// flat normal classification of a sample (N = world normal, th/kappa/h from the chart foot)
const a = dot(N, t) * (1 - kappa * h);   // / (1 - kappa * d)
const b = N[1];
const c = dot(N, n);                      // 0 <=> wall extruded along W's normal

// tool for a PROFILE / WALL chain (P = mapped points placed just outside the box)
opFitSpline(context, fid, { "points" : P });                  // or [P[0], P[last]] if straight
opExtrude(context, eid, { "entities" : qCreatedBy(fid, EntityType.EDGE), "direction" : ext,
        "endBound" : BoundingType.BLIND, "endDepth" : span * meter });
// extend ONLY the side edges (parallel to ext); extending all edges fails on curved chains
opExtendSheetBody(context, id + "xs", { "entities" : qUnion(sideEdges), "tangentPropagation" : false,
        "endCondition" : ExtendEndType.EXTEND_BLIND, "extendDistance" : boxDiagonal,
        "extensionShape" : ExtendSheetShapeType.LINEAR });

// RULED (leaning wall, exact): rows pushed along their rulings to z = lo-1mm / hi+1mm, then
opLoft(context, lid, { "profileSubqueries" : [qCreatedBy(rb, EntityType.EDGE), qCreatedBy(rt, EntityType.EDGE)],
        "bodyType" : ToolBodyType.SURFACE });      // BODY queries give LOFT_INVALID; use EDGE

// cells + membership (inverse map: s from X by fixed point s = X + sA + d(th(s) - thA))
fCuboid(context, id + "box", { "corner1" : lo * meter, "corner2" : hi * meter });
opSplitPart(context, id + ("split" ~ i), { "targets" : cellsQ, "tool" : toolQs[i], "keepTools" : true });
var pt = evApproximateCentroid(context, { "entities" : cell });
if (isQueryEmpty(context, qContainsPoint(cell, pt)))      // non-convex cell: inset a face point
{
    const tp = evFaceTangentPlane(context, { "face" : firstFace, "parameter" : vector(0.5, 0.5) });
    pt = tp.origin - 1e-6 * meter * tp.normal;
}
if (!isQueryEmpty(context, qContainsPoint(piece, unflat(pt / meter)))) { keep = append(keep, cell); }
opBoolean(context, id + "union", { "tools" : qUnion(keep), "operationType" : BooleanOperationType.UNION });
```

Findings from building it:
- A partially cutting tool does NOT split: with a 20 mm extension, 8 of 14 splits failed with
  `SPLIT_FAILED`. The tools must cross the whole box.
- `opExtendSheetBody` on ALL edges by the box diagonal failed (`EXTEND_FAILED`) on the curved tip
  walls. Extending only the side edges, with the extrusion itself spanning the box, always worked.
- Chaining is required. Tangent-continuous neighbours extended separately give near-coincident or
  tangent tools and sliver cells. The tangent test must be tight: with dot > 0.999 (2.5 deg), 4803
  merged a non-tangent corner, 16 um.
- Row density matters for tight corners: 9 points on the 4803 corner Stjuk gave 16 um. With 0.5 mm
  spacing and at least 17 points: 0.8 um.
- The prototype's chart uses the kernel foot (evDistance) on the spline edge. Production should use
  `unwrapFast` (0.12 ms per point), which is also where most of the classification time goes.

## 5. Measured accuracy and time

Accuracy check: 7x7 on-face samples per face (inside the trim) plus 5 points per edge, all mapped
exactly (analytic foot on lines/arcs, kernel foot on the spline, verified round trip 1.5e-9 m), then
`evDistance` to the faces of the flat body. Run sequentially, no check, exact walls, d = 0:

| Body | Pieces | Cells kept / total | Flat faces (orig) | Worst error | Request time |
|---|---|---|---|---|---|
| CORE | 2 rebuilt + 1 rigid | 4/60, 4/60 | 94 (90; 4 faces split at the junctions) | 8.29 um at 2 corner vertices (X 750, Y +/-60.76, Z 1.632: the original vertex sits 8 um off its h = 1.64 face, so it is a tolerant vertex). All faces < 1 um; histogram: 4978 samples < 1 nm, 88 < 0.1 um | **2.35 s** |
| 4802 exact walls | 1 rebuilt | 5/182 | 16 (17) | **0.79 um** | 4.8 s |
| 4802 walls forced vertical | 1 | 5/180 | 16 | **43.6 um** (nose corners: lean 0.0215 rad) | ~4.5 s |
| 4803 | 1 | 5/240 | 18 (18) | 0.81 um | 4.2 s |
| base 4101 | 2 + 1 rigid | 6/60, 6/60 | 30 (40) | < 1 nm, every sample | 2.7 s |
| 4103 sidewall | 2 + 1 rigid | 3/48 | 36 (41) | 0.03 um | ~4 s |
| 4401 | 2 + 1 rigid | 1/27 | 52 (46) | 0.25 um | ~3 s |
| d = 3.8 mm: 4802 / core | | | | 0.78 um / 8.29 um (same vertex) | 4.8 s / 2.4 s |

Output face types: rigid pieces keep theirs (base keeps its 10 sidecut CYLINDERs). In rebuilt pieces
a straight chain becomes a PLANE: 4803 top and bottom, the base's tip/tail top and bottom. A curved
chain becomes one EXTRUDED spline surface. 4802's bottom (1.8 exactly, but not straight to 1e-8)
and its top (13 um variation) stay EXTRUDED splines.

Volumes: flat volume is larger than world volume by the (1 - kappa h) Jacobian for d = 0 (4802:
91 802 vs 90 662 mm^3). With d = 3.8 (its mid-thickness) it matches to 0.01 % (90 671).

## 6. Decisions for the user

1. **Walls that are not normal to W: keep exact, or make them normal to the flat plane?** Only
   4802's nose corners (0.0215 rad, 74 um lean, 43.6 um error if forced) and the nose plane
   (0.0217 rad; a profile face, so it stays exact either way) are affected. Every other wall leans
   <= 0.0017 rad. Proposal: exact by default (RULED tools). Offer "square walls to the flat plane"
   for CNC blanks, and report the max lean it removes.
2. **Which curve keeps its length.** It changes nothing on the core (<= 0.09 mm per end). It matters
   on the extensions: 4802 is 180.62 mm long at d = 0 (W), 179.57 at d = 1.8 (its bottom), 178.40 at
   d = 3.8 (its middle), 177.22 at d = 5.8 (its top). A laminate unwrapped with a different d per
   layer no longer mates at the tip/tail joints: the offset between layers is delta_d * theta, for
   example 2 mm between base (d = 0.6) and 4802 (d = 3.8). Options: one d for the whole ski (joints
   consistent, default), or each part's own mid/neutral line (each blank correct in length).
   Composite-part unwrap should force one choice for all constituents.
3. **Snap near-constant profiles to planes?** 4802's top is 13 um off a constant offset, so exact
   output is a slightly curved top. A "planar tolerance" (e.g. 0.01 mm) would emit planes, as the
   docstring asks ("faces that unwrap onto the plane are planar"), with an error <= tol/2.
4. **Keep native face types in rebuilt pieces?** Chaining merges consecutive faces into one spline.
   Per-segment line/arc detection (the edge unwrap's circle test) could emit composite wires
   (line/arc/spline) before extruding. This is only worth doing if flat outline arcs are wanted as
   arcs in the curved spans. The line span keeps its arcs.

## 6.2 NEUTRAL_AXIS / MIDDLE / QUERY without a constant d

For a length curve C given in the chart as h = g(s), requiring |flat C'| = |world C'| gives
X'(s) = 1 - kappa(s) g(s), so

    X(s) = s - integral kappa(s) g(s) ds     (reduces to s - d*theta for g = d)

The map stays (s, y, h) -> (X(s), y, h). Profile and wall classes are unchanged, rigid spans stay
rigid (kappa = 0, so X' = 1), and only the table X(s) differs. MIDDLE = the mid-thickness line of the
part's y = 0 section, g(s) = (top(s) + bottom(s))/2. QUERY = any user curve, g from its chart foot.
Tabulate X(s) on the chart's stations and interpolate. The inverse (for membership) is a 1-D Newton
on X(s). Not prototyped; constant d was.

## 7. Limits, risks, follow-ups

- **FAIL faces** (neither profile nor wall, e.g. a fillet between a wall and a profile face, or a
  draped top over a curved span) are not handled. Fallback: a bi-directional fitted tool (strategy B)
  in the same cell pipeline. It is untested; its extension is the risk.
- **Degenerate cell contact** (a tool tangent to another, groove run-outs) gives
  `BOOLEAN_NON_MANIFOLD_RESULT`. That is why the core must go through the rigid split. A curved-span
  piece with such features would fail the same way: report it, don't hide it.
- **Chains turning > 180 deg** would make their straight end extensions cross (a self-intersecting
  tool). Split chains at ~150 deg of turn.
- The membership point is the centroid, with an inset face point as fallback. Thin cells are fine
  down to the 1 um inset.
- Junction splits add faces (core 90 -> 94). They are harmless, but the flat faces will not merge
  across the junction because they are different surfaces (native vs refit).
- **Mate connectors:** origin through the chart; axes through `unwrapDirection` (exact Jacobian,
  already in edge_offset_utils.fs).
- **Composite parts:** unwrap each constituent with the same chart. Consistency across joints is
  automatic because the map is one diffeomorphism (with one d; see decision 2).
- **Speed:** C's cost is split calls x cells. 4802: 16 splits, 182 cells, about 4.5 s, of which
  roughly 1-1.5 s is classification sampling with a kernel foot per sample. With `unwrapFast` and
  the evDistance on-face filter replaced by the classification of face boundaries, expect about 3 s.
  The core is 2.35 s because only 20 of its 1500 mm are rebuilt.

## 8. As built: `driven_offset/unwrap_part.fs` (2026-09-25)

`unwrapSolid(context, id, chart, cs, part, options)` as called by `unwrap.fs`' `unwrapPart`. The interface is
unchanged. Report keys: `pieces, rigidPieces, rebuiltPieces, cells, keptCells, tools, planeTools, arcTools,
splineTools, ruledTools, snappedTools, squaredWalls, maxLean`. `lines[0]` is a one-line summary; after it comes
one line per piece. The option `print` is accepted but nothing is printed here, because the caller prints `lines`.
Nothing is pushed yet: the tab does not exist in Onshape, and `unwrap.fs` still imports `UNWRAP_PART_EID`.

How it differs from the prototype:
- **Chart:** the production chart (`unwrapFast`, packed Hermite) replaces the prototype's analytic or kernel foot.
  Flat classification uses the chart's own frame `(t, -planeNormal, planeNormal x t)` and Jacobian
  `diag(scale / (scale - kappa h), 1, 1)` (`flatNormal`), so it holds for any delta.
- **Inverse map (`unflatPoint`):** Newton on `x = arc - delta theta(arc)`, then `referencePointAtArc` plus
  `v`/`height` offsets. It is used only for the cell membership test.
- **Work frame:** everything is built in the chart frame (identity). A single `opTransform(toWorld(cs))` at the
  end places the result. A rigid piece's transform is `toWorld(frame at its chart coords) * fromWorld(coordSystem(P,
  t, planeNormal x t))`, with `P` an interior point and `t` the exact line direction.
- **Spans:** they come from `chart.alongRef.chain` (per-edge `curveType`). Arc ranges are the chart arcs of the edge
  end points. A piece is classified by the foot of an interior point: the centroid, or a point inset 1 um from a
  face.
- **Classification:** it first runs on the whole 9x9 grid with no on-face filter. The `evDistance` trim filter runs
  only for faces that do not pass as PROFILE/WALL on the whole grid. That saves about 0.35 s on 4803.
- **Cell box:** it comes from the piece's EDGES, sampled every 2 mm and mapped, not from the classification grid.
  With grid points outside the trim, the box grew, and 4802 failed with `SPLIT_FAILED` (a lofted nose-corner tool
  pushed to a much larger z range). Keep the box tight.
- **`flatTolerance`:** a PROFILE or WALL chain within the tolerance of its chord becomes a plane. The line is moved
  to the middle of the band, so the error is at most tol/2. At 0.01 mm, 4802 has 5 snapped chains and a worst error
  of 5.15 um.
- **Arcs:** a curved chain within 0.1 um of a circle (`classifyPoints`, lines forbidden) is emitted through
  `emitArcCurve` and extruded, giving a real CYLINDER. 4401 got 4 arc tools. On 4802, 4803 and the core, the curved
  chains are not arcs in the chart (x is arc length, not world X), so they stay splines.
- **Single-split test:** a single `opSplitPart` with all tools as `tool` uses only the first tool (tested: 2 cells,
  wrong). Splitting one tool at a time is required.
- **Ids:** each piece has its own id component (`id + ("piece" ~ k)`). A shared `id + "piece" + ...` prefix
  interleaved with `deletePiece` fails with "Parent Id used at two non-contiguous points".
- **Error handling:** errors are `regenError`s that highlight the input part: a FAIL face, no kept cell, or a union
  failure (piece or whole). There is no try/catch around splits or extends.

Measured through the eval API on the "Unwrap_Testing Copy 2" studio (681a5825). Setup: W = REF_WIRE, align StjLB,
d = 0, cs = world, exact walls, flatTolerance 0.001 mm. Time is the request time without the accuracy check; the
API plus the chart cost 0.27 s. Error is the worst of 7x7 on-face samples per face plus 5 points per edge, mapped
with `unwrapFast` and measured with `evDistance` to the result faces:

| Body | Pieces (rigid / rebuilt) | Cells kept | Tools (plane/arc/spline/ruled) | Faces out (in) | Vol flat / world | Worst error | Time |
|---|---|---|---|---|---|---|---|
| CORE Rtjn | 1 / 2 | 8/120 | 10/0/8/0 | 94 (90) | 1.000059 | 8.29 um (the known tolerant vertex); all other samples < 0.1 um | 1.6-1.8 s |
| 4802 Rtj7 | 0 / 1 | 5/182 | 5/0/3/8 | 16 (17) | 1.012571 | 0.79 um | 4.3 s |
| 4803 Rtj3 | 0 / 1 | 5/240 | 7/0/4/6 | 18 (18) | 1.003707 | 0.82 um | 4.2-4.5 s |
| base RtjP | 1 / 2 | 12/120 | 10/0/6/0 | 30 (40) | 1.000206 | < 1 nm | 2.6-2.8 s |
| 4103 L RtjH | 1 / 2 | 6/96 | 8/0/1/7 | 36 (41) | 1.000380 | 0.034 um | 2.8-3.0 s |
| 4401 L Rtjv | 1 / 2 | 2/54 | 2/4/0/6 | 52 (46) | 1.000041 | 0.25 um | 1.7 s |

Other runs:
- 4802 with `squareWalls`: 8 walls squared, worst error 43.6 um (as predicted).
- d = 3.8 mm: 4802 has a volume ratio of 1.000103 and 0.78 um; the core has 0.999998 and 8.29 um.
- A rotated and translated `cs` on the base: < 1 nm.
- The input part is untouched in every run. Exactly one new solid is left, and no new sheet or wire.

Time split on 4803 (4.2 s): classification and rows about 0.6-1.0 s, tools 0.3 s, **17 sequential splits into 240
cells about 2.2 s**, membership and union about 0.7 s. The splits are the cost that remains. It grows with tools x
cells.

Open items:
- The junction split plane is infinite, and the "does it cut" test uses the part's box corners. A part that wraps
  round a curve far enough for the plane to cut it twice would be split in the wrong place. Ski parts do not.
- Chains turning more than 180 deg (their end extensions would cross) are not split yet.
- FAIL faces (fillets between walls and profile faces over a curved span) are reported, not handled.
- The row of each face is taken at mid-parameter of the other direction. On a face with a badly non-rectangular
  trim, that row lies on the surface's extension. That is exact for extrusions, but it is untested on other faces.

## 8b. As built: robustness and speed pass (2026-09-25, later)

Interface unchanged (`unwrapSolid`, same report keys; `UNWRAP_PART_COUNT_KEYS` + `emptyPartReport()` now define
them once). Local only, not pushed. Changes:

- **M7 fuse check:** after the union, more result solids than the input part's solids -> `regenError` naming the
  part ("the unwrapped pieces of "<name>" did not fuse (n solids from m)").
- **L5 ruling guard:** a RULED ruling rising less than half its length (`UNWRAP_PART_RULING_RISE`) throws, naming
  the flat x and highlighting the chain's faces (was a division by ~0).
- **Inverse map:** `unflatPoint` (unit-based, alongRef tables) is gone; cell membership uses
  `unwrapInverse(chart, x, y, z)` (plain numbers, same packed tables as `unwrapFast`).
- **Feet are checked:** every `unwrapFast` goes through `chartFootAt` (warm start, cold retry) and
  `chartFootConverged`. Unmapped grid points off the trim send the face to the on-face pass; an unmapped point on a
  face, row or edge throws naming that face/edge; an unmapped interior point throws naming the part.
  `rigidTransform` takes the foot it already has (one map call fewer).
- **Plane tools:** a straight PROFILE/WALL chain is a `Plane` passed straight to `opSplitPart` (no line, extrude,
  extend).
- **On-face filter:** one `evFaceTangentPlanes(..., returnUndefinedOutsideFace : true)` per face replaces an
  `evDistance` per grid point.
- **Tangency by exact normals:** rows carry the flat normal at their ends (`e0`/`e1`). Chaining (`joinRows`,
  `endsTangent`) compares normals, not end chords (chords at a tight corner differ by spacing x curvature, 0.9 deg
  at R 10 mm / 0.3 mm). 4802 now chains to 12 tools / 108 cells (was 16 / 182).
- **Spline tools:** PROFILE/WALL splines are `approximateSpline` fits (`emitSplineCurve`, 0.1 um, chord parameters,
  exact unit end tangents from the flat normals); points thinned so chord fractions rise by 2e-6. PROFILE/WALL rows
  are sampled every 1 mm (was 0.5). Measured: 1 mm leaves every body's worst error unchanged; 2 mm takes the base
  from 0.037 to 0.305 um, 4 mm to 0.529 um.
- **Grazing guard (`markGrazingEnds`):** an end whose extension ray passes within 0.2 mm of a parallel (2 deg) end of
  another same-plane chain, or of an x = const plane of the other kind, gets a free end (the fit picks its tangent).
  An exact tangent extended linearly grazes a G1 neighbour a face away, and `opSplitPart` fails there (4802 with
  squareWalls: SPLIT_FAILED without the guard).
- **RULED tools stay `opFitSpline` + `opLoft` at 0.5 mm rows.** Tried and rejected, all measured on 4802/4803:
  - A family fit (`approximateFamily`) lofted gives sheets that fail `opSplitPart` (correction 22's loft fragility).
  - Written down as a ruled `opCreateBSplineSurface`, the fit needs end tangents: without them the result is
    CANNOT_MAKE_BSPLINESURFACE.
  - With exact end tangents (near-end samples) the RULED extension grazes tangent neighbours. At the 4803 tip it
    meets the x = const PROFILE plane G1, leaving a 1.7 um sliver and 3.2 um error; 4802 shows 5.8 um.
  - Re-reading tangent WALL rows as RULED (one tool through the junction) fixes the wall junctions but chains the
    whole outline into a >180 deg loop once the tip plane joins.

Results (same setup as section 8; time = request time, no accuracy check, 3 interleaved runs; error = worst of the
7x7 face + 5-per-edge samples):

| Body | Time before -> after | Vol ratio | Worst error | Cells kept | Tools (plane/arc/spline/ruled) |
|---|---|---|---|---|---|
| CORE Rtjn | 1.80 -> 1.63 s | 1.000059 | 8.29 um (known vertex), rest < 0.1 um | 8/120 | 10/0/8/0 |
| 4802 Rtj7 | 4.32 -> 2.67 s | 1.012562 | 0.794 um | 5/108 (was 5/182) | 5/0/3/4 (was 5/0/3/8) |
| 4803 Rtj3 | 4.33 -> 3.93 s | 1.003707 | 0.821 um | 5/240 | 7/0/4/6 |
| base RtjP | 2.76 -> 2.27 s | 1.000211 | 0.037 um | 12/120 | 10/0/6/0 |
| 4103 L RtjH | 2.99 -> 2.83 s | 1.000346 | 0.048 um | 6/96 | 8/0/1/7 |
| 4401 L Rtjv | 1.75 -> 1.74 s | 1.000041 | 0.25 um | 2/54 | 2/4/0/6 |

Also: 4802 squareWalls 39.7 um (was 43.6); d = 3.8 mm: 4802 0.786 um, core 8.29 um; 4802 at flatTolerance
0.01 mm: 5.15 um (same as before). The input is untouched and exactly one new solid is left in every run.
evVolume moves at the 1e-5 relative level between tool representations with face areas equal to 0.001 mm^2, so it
is representation noise, not geometry.

Still open: the grazing hazard is guarded, not removed (a G1 junction with a neighbour farther than 0.2 mm off the
ray, or a RULED end, is not covered); chains turning > 180 deg; FAIL faces.
