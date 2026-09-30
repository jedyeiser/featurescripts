# Unwrap, explained

*A living explainer for the Unwrap feature family (`unwrap.fs`, `undrape_utils.fs`, `unwrap_part.fs`, and the
chart in `edge_offset_utils.fs`). Written 2026-09-25 against the code as it stands that day. Revise freely.*

The document runs in three passes, each building on the one before:

1. **Concepts**: what is geometrically going on, with pictures. No code.
2. **Our feature**: the three modes, what every parameter means physically, how to read the checks,
   what the test studios show, what is still open, and the two questions you asked.
3. **The codebase**: files, call graph, data structures, import pins, how to test, where the notes are.

The appendix lists things in the code and notes that I found unclear or contradictory.

Figures are PNGs in `img/`. The scripts that make them are in `img/src/`, and most of them use real
geometry read from the test studio. See section 3.8 for how to regenerate them.

---

## Contents

- [Part 1: Concepts](#part-1-concepts)
  - [1.1 Developable surfaces](#11-developable-surfaces-what-can-be-flattened-without-stretching)
  - [1.2 The chart](#12-the-chart-coordinates-that-make-unwrapping-a-bookkeeping-exercise)
  - [1.3 Which length is kept: s - d*theta](#13-which-length-is-kept-s---dtheta)
  - [1.4 Why a bent plate keeps its length at mid-thickness](#14-why-a-bent-plate-keeps-its-length-at-mid-thickness)
  - [1.5 Handedness](#15-handedness-why-y---v)
  - [1.6 Undrape](#16-undrape-a-plate-draped-over-something-that-is-not-developable)
  - [1.7 Unwrapping a solid](#17-unwrapping-a-solid)
  - [1.8 Recognising lines and arcs](#18-recognising-lines-and-arcs-with-a-continuity-gate)
  - [1.9 Adaptive vs fixed sampling](#19-adaptive-vs-fixed-sampling)
  - [1.10 The 0.01 mm budget](#110-tolerances-and-the-001-mm-budget)
- [Part 2: Our feature](#part-2-our-feature)
  - [2.2 Parameters (the dialog tree)](#22-parameters-and-what-they-mean-physically)
  - [2.5 Known limits (incl. U-turns)](#25-known-limits-and-open-decisions)
  - [2.6 Curved references, walls, PRISM](#26-curved-references-walls-and-prism-2026-09-25)
  - [2.7 Your two questions](#27-your-two-questions)
  - [2.8 Holes in Part mode](#28-holes-in-part-mode-2026-09-30)
  - [2.9 Faces / surfaces mode](#29-faces--surfaces-mode-2026-09-30)
- [Part 3: The codebase](#part-3-the-codebase)
- [Appendix: unclear or contradictory items](#appendix-unclear-or-contradictory-items-found-while-writing-this)

---

# Part 1: Concepts

## 1.1 Developable surfaces: what can be flattened without stretching

A surface can be laid flat with no stretch, tear or overlap only when its **Gaussian curvature is zero
everywhere**. That means that at every point it curves in at most one direction. These are the *developable*
surfaces: planes, cylinders (any cross-section, not only circles), cones, and tangent-developables.
A sphere or a saddle cannot be flattened without deformation, which is why no map of the earth is right.

The one developable surface we care about is the simplest: **a planar curve swept straight along its
plane's normal**. For a ski, take the side profile in the XZ plane (the "reference" W) and sweep it along
Y. The result is a generalised cylinder. Bending a flat sheet around it is exactly how the flat laminate
takes on its camber and rocker, so unwrapping it is exact.

Everything in Unwrap is built on that one surface. What changes from mode to mode is **how far the real
part is from it**:

| Situation | Is the flat result exact? |
|---|---|
| A point on the swept surface | Yes: an isometry. |
| A point off the surface (height h), part moved as a solid | Yes, but only one height keeps its length (1.3, 1.4). |
| A plate that also curves ACROSS the reference plane (a topsheet draped down the sides) | No: the plate itself is not developable along W. Flattening it is a deformation. We measure the deformation and report it (1.6). |

## 1.2 The chart: coordinates that make unwrapping a bookkeeping exercise

![The chart](img/fig01_chart.png)

For any point P near the reference, drop a perpendicular onto the swept surface. The foot is A(s), the point
of W at arc length **s** (measured from the alignment point) shifted sideways by **v** along W's plane normal
`pn`. The distance off the surface along the surface normal N = pn x t is the height **h**:

    P = A(s) + v * pn + h * N(s)

Because the swept surface has zero Gaussian curvature, (s, v) is a **flat chart** on it. Distances measured
in s and v are true distances on the surface, and a straight line in (s, v) is a geodesic. Unwrapping is
then just writing the same three numbers out in the flat frame:

    x = s - d * theta(s)     (d = preserve-length offset, 1.3; x = s when d = 0)
    y = -v                   (the sign is explained in 1.5)
    z = h

and placing (x, y, z) in the unwrapped origin's frame, with the alignment point at the origin.

Finding the foot is a small Newton iteration on "P - A(s) is perpendicular to the tangent t(s)". Its slope is
`scale - kappa * h`, where kappa is W's curvature and `scale = 1 - kappa * d` (edge_offset_utils.fs
`chartFoot`). The slope goes to zero when h reaches the radius of curvature. **A point past W's centre of
curvature has no unique foot**, so the feature refuses it ("a point on it lies past the reference's centre
of curvature"). For ski geometry (R >= 230 mm at the tightest tip, parts within ~70 mm of W) this never
comes close.

The as-built chart also imposes three rules on the reference, each with a clear error message:

- **One tangent chain.** Joints are allowed at most 1 deg (`UNWRAP_REFERENCE_JOINT`, because real profiles
  carry 0.4 deg modelling creases). A corner's turn never enters theta, and points in the corner's wedge have
  two feet or none.
- **X rises steadily along it** (`checkedChart`). The foot search is seeded from the point's world X, so a
  reference that turns back, or runs vertical, is refused.
- **The alignment point's X lies within the reference's X span.** Its X seeds the chart's zero.

## 1.3 Which length is kept: s - d*theta

![Offset length](img/fig02_offset_length.png)

A curve offset by a constant distance d from W (towards the centre of curvature counts positive) is shorter
where W bends towards it and longer where W bends away. Exactly:

    length along the offset  =  s - d * theta(s)

Here theta(s) is W's **cumulative turning angle** (the integral of signed curvature). Two consequences carry
the whole design:

- **Only one curve keeps its length.** The chart maps arc length along *one* chosen curve (W offset by d) to
  flat X. Every other height is stretched or compressed in the flat by the factor
  `(1 - kappa*d) / (1 - kappa*h)`. The volume check in section 2.3 is this factor, averaged over the part.
- **One table serves every offset.** theta does not depend on d, so the chart stores theta(s) once and never
  builds an offset curve (`buildAlongReference`). On a straight span theta is constant, so all offsets have
  the same length there. That is why straight spans unwrap rigidly (1.7).

Scale on our test ski: REF_WIRE turns about 0.64 rad through the tip. 4802 (core tip extension) sits 1.8 to
5.8 mm above REF_WIRE, so its flat length changes by about 0.59 mm for every mm of d (see fig14 in 2.7).

## 1.4 Why a bent plate keeps its length at mid-thickness

![Neutral surface](img/fig03_neutral_surface.png)

Bend a flat plate of thickness t to radius R. The outer fibre stretches to (R + t/2) theta, the inner one
compresses to (R - t/2) theta, and for a section symmetric through its thickness the **mid-thickness
(neutral) surface keeps its length**. Run backwards, the correct flat blank of a bent part is the one that
keeps its **neutral** line's length. In chart terms, that means choosing d = the neutral line's height above
W.

For a single-material plate the neutral surface is the mid-surface. For a laminate or a core it is the
stiffness-weighted neutral axis, which sits nearer the stiff layers. The code works with a *constant* d or a
plate's own *mid*-surface. A variable "neutral line" for solids is not built yet (2.7 (b); for parts, 2.6).

## 1.5 Handedness: why y = -v

![Handedness](img/fig04_handedness.png)

The chart's frame at a foot is (t, pn, N), with N = pn x t. Writing that directly as the flat (x, y, z) would
be a left-handed frame, so every unwrapped solid would come out **mirrored**. The code avoids that by
measuring y along -pn (`unwrapFast`).

The plane normal pn is fixed **once per reference**, from the most curved sample, and oriented so that
height (N) points up (+Z). For a reference lying in a nearly horizontal plane, "up" is undefined, so pn itself
is oriented with its largest world component positive (`referencePlaneNormal`, `REFERENCE_HANDEDNESS_Z`).
Without that rule, an S-curve edit could flip height, width and every solid.

## 1.6 Undrape: a plate draped over something that is not developable

**Plates and sections: two different things.**

- A **plate** is a *part*: a body of constant thickness t whose two large faces (its **sides**) are exact offsets of
  each other, joined round the edge by thin **walls**. Topsheet (0.4 mm), laminates, base (1.2 mm), mats and shears
  are plates. So is 4802 (4.0 mm): its sides measure 3.996-4.001 mm apart. The core, 4803, the sidewalls and 4401
  are not; they are Part-mode solids.
- A plate is handled through its **mid-surface**, the surface halfway between its sides. That surface is flattened,
  and the flat plate is rebuilt t/2 either side of it. Its walls therefore come out exactly normal to the flat
  plane.
- A **section** is a *measurement*: the curve where the plane normal to W at one station cuts the mid-surface.
  Undrape unrolls each section across the width; the unrolled length from the centreline becomes flat y.

The topsheet is the case the chart alone cannot handle. Its top face lies *on* the ski top surface (W swept
along Y) over the centre, but outside a pressed **step** its flaps drape down the sides by 2.4 to 8 mm. In the
tip and tail U-turns the step even wraps round across the ski:

![Topsheet sections](img/fig06_topsheet_sections.png)

*Real kernel sections of the topsheet's mid-surface (Unwrap_Testing Copy 2, RnRD) on station planes normal
to W. W here is the mid-surface's own Front-plane section, so the centre sits at h = 0.*

A surface that bends *across* W as well as along it is not developable along W, so **no flat pattern keeps
every length**. Undrape picks one rule and applies it section by section. At each station (the plane normal to
W at arc s):

1. Take the plate's mid-surface section.
2. Unroll it: each point's flat y is its **arc length along the section** from the centreline (w = 0).
3. Its flat x is the station's length along the target: x = s - d*theta.

![Unroll one section](img/fig07_unroll_one_section.png)

*One station in a transition zone. Points 10 mm apart along the mid-surface arc land 10 mm apart in flat y.
The flaps therefore move outwards: the flat rim is at 76.23 mm while the plan rim is at 75.*

Widths across each section are exact **by construction**. What gives is the *lengthwise* direction. Along a
flap sitting h below the target, the 3D length element is (1 - kappa*h) ds, while the flat one is
(1 - kappa*d) ds. Where the flap depth changes along the ski, the section arc (and so the flat rim) changes
too, which **shears** the flat pattern. Along the whole topsheet the flat outline is 0 to 4.3 mm wider per
side than the plan:

![Widths](img/fig08_topsheet_widths.png)

*Blue: the flat half-width from the kernel sections (my own numpy undrape, `img/src/topsheet.py`). Orange: the
outline of the flat plate that the Unwrap feature actually built. They agree to the line width: an
independent check of the production undrape.*

**Measuring the deformation.** The feature compares, between consecutive stations, the 3D mid-surface chord
along the rim and along every bend-line edge with the flat chord. It reports **stretch** (flat/3D - 1) and
**shear** (how far the chord's angle to its section changes) (`undrapeDeformation`). The same idea over the
whole plate:

![Stretch and shear](img/fig09_topsheet_stretch_shear.png)

*Tracked along flat lines y = const between 4 mm stations. The plateau is isometric (|stretch| < 0.013 %).
The flaps compress by kappa(h - d), about 1 %. The transition zones (x of about -390 and +395) shear up to
about 9 deg. The tip and tail U-turns saturate the scale: tracking at constant y is not meaningful where the
section cuts the step lengthwise. The feature's own figures for this part are a stretch of -6.5 % to +7.5 %
(U-turns), -1.04 % to +0.41 % elsewhere, and shear of 22.6 deg (U-turns) and 9.5 deg elsewhere.*

How the section is measured, without a kernel call per station (research_undrape_map.md sections 0 and 4):

- The station plane is intersected with **the edges of one side** of the plate (sampled once).
- The crossings are ordered across the section (by w, or by face adjacency when a wall reaches vertical).
- Each piece between two crossings is taken as the **circular arc with the two end tangents**.
- The side's arc is converted to the mid-surface by the offset-turning correction sideSign * (t'/2) * turn.
- Stations where that model is not trusted (the plane cuts a face lengthwise, as in the U-turns) are
  **refused** and measured by a real kernel section. That is "the U-turn rule", `undrapeRefusedSection`.

The flat outline is fitted per rim edge (lines and arcs recognised, 1.8) and rebuilt as a plate: a plane sheet
is split by the outline, material is chosen by loop parity (so holes and slots cut), and the result is
extruded by +/- t/2 (`plateFromOutline`).

## 1.7 Unwrapping a solid

A solid is unwrapped **pointwise**: every point goes through the same chart map, P -> (x, y, z). The question is
how to rebuild a B-rep from that. Two facts do the work (unwrap_part.fs, research_unwrap_part.md):

- **Over a straight span of W the map is one rigid motion**, for every height and every d, because kappa = 0
  and theta is constant. A piece of the part over a line is moved by a single `opTransform`, and every native
  face (arcs, cylinders, B-splines) survives exactly.
- **Over a curved span**, a face whose normal has no component across W's plane (a "profile" face, extruded
  along Y) maps exactly to an extrusion along flat Y. A face whose normal has no component along N (a "wall",
  extruded along W's normal) maps exactly to an extrusion along flat Z. Ski parts consist of those two kinds,
  plus slightly leaning walls that are rebuilt as ruled surfaces.

![Part mode](img/fig10_part_mode.png)

So Part mode:

1. Splits the solid with planes normal to W wherever W changes between a **line edge** and a curved edge.
2. Moves the pieces over the line rigidly.
3. **Rebuilds** each curved piece:
   - one tool sheet per chain of tangent faces;
   - a box split by every tool, one at a time;
   - the cells whose interior point maps back *inside* the original piece are kept (`unwrapInverse` +
     `qContainsPoint`);
   - the kept cells are united.

For the test CORE this means 84 native faces moved rigidly plus two 10 mm end pieces rebuilt, in about
1.6 s. Accuracy is sub-micron (8.3 um at one vertex that is already 8 um off its face in the source).

## 1.8 Recognising lines and arcs with a continuity gate

An unwrapped edge is only as good as its fit. When the unwrapped points lie within tolerance of a line or a
circle (`classifyPoints` in curve_core.fs), emitting a true line or arc gives Onshape real geometry (a radius
you can dimension, cylinders when extruded). Being "one within tolerance" is not enough, though. Replacing a
curve by an arc changes its **end tangents**, and neighbouring edges would lose G1. So `emitFlatCurve` also
requires the line's or arc's end tangents to agree with the **exactly unwrapped** end tangents
(`unwrapDirection`, the chart's exact Jacobian) within `G1_JUNCTION_ANGLE` (0.01 rad). Anything else is emitted
as a fitted spline. The per-edge report says why a line or arc was refused.

A further guard applies to the end tangents themselves. A supplied end tangent more than 2 deg away from the
parabola through the last three points is replaced (`UNWRAP_TANGENT_AGREE`). The undrape's tangent at a
pointed tip once came out 58 deg wrong, and the fit then wobbled.

## 1.9 Adaptive vs fixed sampling

Sampling at a fixed spacing spends points evenly, but the flat curve's difficulty is not even. **The unwrapped
curve's curvature is roughly the source's minus the reference's.** So it inherits every curvature *jump* at the
reference's edge joins, and every kink at the source's knots.

![Adaptive sampling](img/fig11_adaptive_sampling.png)

*The real U1 case (Wrapped_profile edge 0 over FULL_BASELINE), replayed in numpy with the production rule. The
adaptive rule reaches the same interpolation accuracy as a fixed 5 mm spacing with about a quarter of the
points (80 vs 297), and clusters them at the joins.*

The rule (unwrap.fs `adaptiveEdgeSamples`):

- **Seed** from the edge's own structure: a line 3 samples, an arc one per 15 deg, a B-spline 3 per control
  point, never a gap over 50 mm.
- **Refine**, up to 6 passes: map every open span's midpoint and split the span if the midpoint misses the
  cubic predicted from the neighbours by more than a quarter of the fit tolerance.

The undrape does the same on the outline, with its own tolerance (1.25 um) and with "spacing" as a
**maximum-gap cap** only (`undrapeOutline` options).

## 1.10 Tolerances and the 0.01 mm budget

The requirement is **0.01 mm** on the final geometry, and speed matters. The budget is spent like this:

| Stage | Size | Source |
|---|---|---|
| Chart map (packed Hermite tables, Newton to 1e-10 m) | < 0.01 um (0.44 um before the 2026-09-24 fixes) | research_unwrap_perf.md 1 |
| Adaptive sampling (span midpoint test) | 1.25 um (tolerance / 4) | unwrap.fs, undrape_utils.fs |
| Undrape section model (circle per piece + correction) | 1.5 um at edge stations, <= 8.8 um at the worst kernel fallback | research_undrape_map.md 12 |
| Curve fit (Unwrap's own default) | **0.005 mm**, max 60 control points | `UNWRAP_FIT_TOLERANCE_BOUNDS` |
| Line/arc recognition | same tolerance as the fit | 1.8 |
| Part rebuild tool fits | cell tools 0.1 um; PRISM parts 0.5 um through row + span midpoints (2026-09-29); arcs to 0.1 x shape tolerance, "snap flat" 0.001 mm | unwrap_part.fs |
| Kernel tolerances seen in real parts | 0.5 um section overlaps, 0.6 um vertex gaps, one 8 um tolerant vertex | notes |

The fit takes half the budget on purpose: 0.005 mm leaves the other half for everything else.

**The fit is held between the samples too (2026-09-29).** approximateSpline only checks the points it is given.
Fed only the samples, it added knots until it passed every sample -- and then interpolated them, ringing between
them: up to **325 um** off the sampled curve on the topsheet's tip outline, 104 um at its tail, 19 um at 4305's wing
roots (all "within 0.005 mm" at the samples). Every freeform flat edge is now fitted through its samples plus three
points per span on the cubic the sampling was refined against (`fitPoints`: chord length, quadratic slopes, the end
tangents). Worst miss between samples on the ten Copy 2 plates: 325 um -> 5.5 um, and the fits need fewer control
points (topsheet edge 8: 47 -> 33, 3 -> 1 inflections).

The known weak spot is long freeform edges crossing reference curvature jumps. U1 edge 0 needs about 60 control points
for 5 um. A split-at-the-breaks-and-join fit (research_unwrap_perf.md 3.3) would get 2.7 um with 43 control
points, but it has not been built.

---

# Part 2: Our feature

## 2.1 The four modes

| Mode (`UnwrapType`) | Input | What happens | Output |
|---|---|---|---|
| **Edges / wires** (`EDGES`) | edges or wire bodies | Each edge is sampled adaptively, mapped through the chart, and emitted as a line, arc or spline. | one wire body per source body |
| **Constant-thickness part** (`THICKENED`) | plate-like solids (topsheet, laminates, base, mats) | Finds both sides and t. The mid-surface is mapped onto the TARGET (a wire's extrusion, offset). **Undrape** (1.6), then the plate is rebuilt t/2 each side. | one flat plate per source (several solids if the outline has several loops) |
| **Part (solid)** (`PART`) | any solid along the reference (core, core extensions, sidewalls) | Split at line/curve changes of W. Straight pieces are moved rigidly, curved pieces rebuilt (1.7); holes in curved pieces cut by exact tools (2.8). | one flat solid per source |
| **Faces / surfaces** (`FACES`, 2026-09-30) | faces of sheets or solids, or whole sheets | Every edge unwrapped once (as in Edges mode), each face rebuilt on its own flat edges, the faces of a body sewn into one sheet (2.9). | one flat sheet per source body |

Every mode ends with the **length and volume check** (2.3), is named from the Outputs table (2.2), and
publishes `lengthWrapped`, `lengthFlat` and `volumeRatio` (per body), plus line, arc and spline counts, through
Variable_tools' `embedStandardOutputs`.

Not built (compared with the docstring spec): the SURFACE branch's projection and THICKEN options (Faces mode unwraps
faces as they are), composite parts, mate connectors as input,
the "neutral axis" length curve, and projection of a wire onto a plane normal to the unwrap plane.

## 2.2 Parameters and what they mean physically

The dialog shows a parameter only when the choices above it make it meaningful. The tree below is the dialog,
top to bottom (unwrap.fs precondition): an indented item appears only under the choice it is nested in. Each
entry says what the parameter means physically.

**Unwrap** -- which algorithm (2.1).

- **Edges / wires**
  - **Edges to unwrap** -- the curves to flatten. Grouped by owner body: one output wire per source body.
  - **Wrapped reference** -- W, the planar tangent chain the edges are wrapped along. One chain, joints within
    1 deg, X rising steadily along it.
  - **Preserve length** -- which height above W keeps its length when flattened (1.3):
    - *Along the reference* -- d = 0: lengths are measured along W itself.
    - *Along an offset of the reference* -- d = **Offset** along W's surface normal (**Flip offset** reverses it).
      Pick the height the geometry actually lives at, or lengths there change by (h - d) * turn.
- **Constant-thickness part** (undrape)
  - **Parts to unwrap** -- constant-thickness plates, draped or not.
  - **Undrape target from** -- where the curve the plate's mid-surface is unrolled along comes from:
    - *Wire*
      - **Wrapped reference** -- W, a curve you pick (e.g. the ski's top-surface profile).
      - **Target offset** (+ **Flip target offset**) -- the plate's mid-surface is mapped onto W offset by this, and
        that offset's length is the one kept. Set it to the plate's mid height above W (a topsheet whose top face
        lies on W's extrusion: -t/2).
    - *Section by a face*
      - **Section face** -- a plane (e.g. Front) cutting the plate's own mid-surface; the cut is W, so the plate
        keeps its OWN mid-line length. The cut must be one piece (no hole, slot or notch on that plane).
      - **Target offset** -- leave at 0: W already is the mid line.
  - **Maximum sample gap** -- only a cap on the largest gap between outline samples; the density itself comes from
    the fit **Tolerance** / 4.
  - **U-turn zones** (2026-09-29) -- where a station plane cuts the part lengthwise (the pressed step turning across
    the part at tip and tail, 2.5). *Section literally* (default, the old behaviour): kernel sections there; the
    outline keeps the rule's spike, and that stretch is fitted as its own piece so it cannot bend the measured
    outline either side. *Blend across*: those stations are not measured; the outline crosses the zone as a smooth
    cubic between the measured stations either side (topsheet: 0 instead of 18 kernel sections, about 1.6 s less).
- **Faces / surfaces** (2026-09-30)
  - **Faces to unwrap** -- faces of sheets or of solids, or whole sheets. Grouped by owner body: one output sheet per
    source body. Faces that share an edge share it in the flat and are sewn together.
  - **Wrapped reference**, **Preserve length** (+ **Offset**, **Flip offset**) -- as in Edges mode.
  - The **Tolerance** of *Lines, arcs & fitting* also bounds each rebuilt face's check against its source (max(Tolerance,
    0.01 mm)); the flat edges themselves are fitted to 0.25 um in this mode (so neighbours sew, 2.9).
- **Part (solid)**
  - **Parts to unwrap** -- any solid along the reference (a core, an extension, a sidewall).
  - **Wrapped reference** -- W, as in Edges mode.
  - **Preserve length** (+ **Offset**, **Flip offset** under *Along an offset*) -- as in Edges mode. For a part
    sitting above W, set the offset to its height, or its length changes (4802: x1.0126 volume at d = 0).
  - **Rebuilt faces** -- only matters where the reference is curved (straight spans move rigidly and keep every
    face). *Keep source faces (arcs preserved)*: one rebuilt face per source face, a true line or arc wherever the
    mapped face is one (base over a camber: all 14 cylinders survive). *Simplify (merge tangent faces)*: each
    tangent-connected wall chain becomes one spline face (core 90 -> ~41-47 faces), a little slower on the core.
  - **Shape tolerance** -- how far a face may depart from a pure side-view extrusion (a profile face) or plan-view wall
    before the band rebuild hands the piece to the exact cell rebuild. Default 0.005 mm. Raise it to accept faces that
    are "essentially" pure (4401's end caps, 17-25 um); the departure is reported. Every rebuilt piece is reverse-checked
    against the source and refused beyond max(Shape tolerance, 0.01 mm).
  - **Square walls** -- off: walls keep their exact mapped lean (1.2 deg at 4802's nose); on: walls stand normal to
    the flat plane, as a CNC blank would (up to 43.6 um at 4802's nose corners).

**Always shown, every mode:**

- **Wrapped alignment point** -- the wrapped point that lands on the unwrapped origin; it fixes x = 0. Must lie
  within W's X span. A mate connector (or its vertex) or a vertex.
- **Unwrapped origin** -- the flat frame: a mate connector (implicit ones included) or a plane / planar face (its own
  axes). Flat X runs along W, flat Z along W's surface normal.
- **Lay the part on the origin plane** -- *Constant-thickness part and Part only* (not Edges, not Faces). On: the flat part's lowest face
  sits on the origin's XY plane. Off: it keeps its height relative to the alignment point.

**Lines, arcs & fitting** (group; all modes, used for every flat curve except Part mode's rebuilt faces)

- **Recognise lines and arcs** -- emit a flat edge as a true line or arc when its points are one within tolerance
  AND its exactly-unwrapped end tangents agree (1.8).
- **Target degree**, **Tolerance**, **Maximum control points** -- the spline fit (1.10). Defaults 3, 0.005 mm, 60.
  The tolerance also sets sample density: samples are refined until the flat curve is predicted within Tolerance / 4.

**Names & properties** (group; all modes)

- **Name suffix** -- appended to the source body's name when the table is filled.
- **Read names and properties** (button) -- the ONLY thing that fills or updates the table (getProperty cannot run
  in the feature body, correction 36). Output names you edited are kept on a re-read.
- **Outputs** (table, one row per source body)
  - **Source** (read-only), **Output name**, **Material** (read-only)
  - **Copy material and appearance** -- per row.
- **Copy attributes** -- copies the source bodies' attributes on every regeneration (no button needed).

Rows pair with source bodies by position; if the count no longer matches, the table is skipped with a warning.

**Debug** (group)

- **Print edge table** -- per-edge kind, radius, samples and gate reasons (plus undrape / part lines).
- **Keep length curves** -- leaves the wrapped preserved curve and its flat image as wires; a good unwrap leaves
  their lengths equal (2.3).
- **Measure deformation** -- *Constant-thickness part only*: the undrape's stretch / shear report (about 0.2 s).

**What you must set, per mode**

| Mode | Must pick | Usually adjust |
|---|---|---|
| Edges / wires | edges, wrapped reference, alignment point, origin | preserve length (offset = the edges' height above W) |
| Constant-thickness part, target Wire | parts, wrapped reference, alignment point, origin | target offset = the plate's mid height above W |
| Constant-thickness part, target Section | parts, section face, alignment point, origin | nothing (target offset 0) |
| Part (solid) | parts, wrapped reference, alignment point, origin | preserve length offset = the part's height above W; square walls for blanks |
| Faces / surfaces | faces or sheets, wrapped reference, alignment point, origin | preserve length (offset = the faces' height above W) |

## 2.3 The length and volume check: how to read it

`lengthAndVolume` (unwrap.fs) prints, per body:

    Length along the preserved curve: <wrapped> mm wrapped -> <flat> mm flat (<diff> mm); volume x<ratio>.

- **wrapped**: the preserved curve (W offset by d), measured in 3D between the stations of the source's
  extreme points. For a plate the undrape's own outline stations give the extremes; otherwise they come from
  vertices, plus edges sampled within 30 mm of either end.
- **flat**: the flat result's bounding-box extent along the unwrapped X.

The unwrap maps length along exactly that curve to X, so **the two agree for a good unwrap**. A difference
means the ends moved: a leaning end wall, undrape stretch at the ends, a rebuild error, or a measurement miss
(appendix item 6, now fixed).

**volume x** is flat volume / source volume, for solids only. It is **not** expected to be 1. It measures the
average of (1 - kappa*d)/(1 - kappa*h) over the part: how far the part sits from the preserved height where W
curves. It is 1.000059 on the CORE (over the line almost everywhere), **1.0125 on 4802** (1.8 to 5.8 mm above
REF_WIRE on the R406 arc and the tip spline, with d = 0), and 1.0001 on 4802 with d = 3.8 mm. For a plate, a
ratio off 1 by more than a few 1e-4 means the undrape's area changed (the stretch report tells you where).

**What to expect, exactly (2026-09-29).** A normal plane of the reference maps rigidly onto the flat plane x = s, so the
flat volume is the source volume weighted by 1 / (1 - kappa h) (d = 0). Cutting the source into slabs between normal
planes and summing V_slab / (1 - kappa h_centroid) predicts the flat volume; on 4103 L over REF_WIRE the prediction and
the result agree to 2e-7. Over FULL_BASELINE the sidewall sits up to kappa h = 0.04 off the baseline and the
prediction is **x0.998739**: the "0.15 % lost" was 0.126 % geometry and 0.01 % a real miss (ringing, fixed, see 2.6
"Round 2"). The ratio is now read with `VolumeAccuracy.HIGH`: the default estimate moved by up to 2e-4 between
regenerations of bit-identical geometry (x0.998538, x0.998559, x0.998942 for the same body), which is where the
"non-deterministic" ratio came from.

## 2.4 The test studios and what each case shows

Document f61d2c000ab2d1240776342e, workspace 5b11f323ab31b04cba8b36ef.

![The test ski](img/fig05_test_ski.png)

*REF_WIRE (thick; blue = line, orange = arcs R900 / R406, violet = 10-CP spline) and the parts' y = 0
sections, read from "Unwrap_Testing Copy 2".*

**"Unwrap_Testing Copy 1"** (80c1e329f99a05e224058526), built by `devtools/onshape/build_unwrap_tests.py`:

| Feature | Shows |
|---|---|
| U1 Wrapped_profile over FULL_BASELINE (edges) | Edges mode on a 3-spline wire 5.95 mm above a 6-edge baseline. The tip/tail stretches come out straight at constant height (185.000 / 165.000, equal to FLAT_PROFILE's). |
| U2 Topsheet, Wire target (Wrapped_profile, offset -0.2 mm) | Undrape against an external wire: volume -0.13 %, stretch -6.6 to +7.7 % (U-turns), shear 22.5 deg, 11 kernel fallbacks. |
| U3 Topsheet, Section by the Front plane | Undrape along its own mid line. Flat length 1827.69 = the section's length. |

**"Unwrap_Testing Copy 2"** (681a5825e353d376c88224eb), a copy of the user's Unwrap_Testing including the CORE
boolean, built by `devtools/onshape/build_unwrap_parts.py`. The origin is the Top plane and the alignment is
the MC StjLB at (885, 0, 0). Latest measured, wrapped -> flat:

| Case | Mode / length curve | Wrapped -> flat (mm) | Volume x | Reading |
|---|---|---|---|---|
| 4101 base (RtjP) | plate, own section | 1789.587 -> 1789.589 | 0.999992 | exact |
| 4305 (RtjT), wings bent up 90 deg | plate, own section | 1788.848 -> 1788.850 | 0.999863 | face-ordered sections unfold the wings (half-width 83-84 mm) |
| 6005 (Rtjf) | plate, own section | 1825.164 -> 1825.159 | 0.999992 | top laminate kept along ITS OWN mid line |
| 4310 (Rtjj) | plate, own section | 1825.538 -> 1825.532 | 0.999991 | same |
| Topsheet (RnRD) | plate, own section | 1827.693 -> 1827.693 | 1.000218 | was 1826.574 -> 1827.693: the check was short, not the flat (appendix item 6, fixed) |
| Tip-Mat / Tail-Mat / Tip-Shear / Tail-Shear | plate, REF_WIRE + 1.88 / 1.8 / 1.96 / 1.9 mm | within 0.001 | 0.9996-0.9999 | These don't reach X = 885, so they cannot use their own section (2.5). A fixed offset at their mid height is used instead. |
| CORE (Rtjn) | part, d = 0 | 1500.000 -> 1500.000 | 1.000059 | 1 rigid + 2 rebuilt pieces |
| 4802 tip extension (Rtj7) | part, d = 0 | 180.710 -> 180.709 | **1.0125** | length kept along REF_WIRE, but the part sits 1.8-5.8 mm above it on the tip curve |
| 4803 tail extension (Rtj3) | part, d = 0 | 160.120 -> 160.120 | 1.0037 | same effect, flatter tail |
| 4103 L/R sidewalls, 4401 L/R | part, d = 0 | 1700.000 -> 1700.001, 1500.196 -> 1500.196 | 1.0003, 1.00004 | mostly over the line |

**"Unwrap hole & face tests"** (56c29f15db6047862561aa7b, 2026-09-30), built by `devtools/onshape/build_unwrap_hole_face_tests.py`:
hole fixtures (a block off Ski_Top_Surf with round hole, slot, pocket and counterbore; two holes through the CORE), four
Part-mode hole tests and five Faces-mode tests (2.8, 2.9).

The regression check (`devtools/onshape/check_unwrap_regression.py` + `devtools/onshape/fingerprints/unwrap_baseline.json`)
compares exactly these three numbers per feature and the Copy 1 statuses. See 3.5.

## 2.5 Known limits and open decisions

Limits of the chart and inputs:

- W must be one tangent chain, planar, with **X rising steadily**. A tip reaching vertical is refused, and the
  seeding is by X. The 09-10 research planned arc-keyed seeding to allow that; it was not built.
- The alignment X must lie within W's X span. **So "Section by a face" only works for plates that cross the
  alignment station.** The tip and tail mats and shears cannot use it.
- A Face target must give **one** section piece: no hole, slot or notch on the section plane.

**U-turns, 2026-09-29.** The literal rule is singular, not just under-resolved: where the station plane turns
tangent to the step wall (the apex of the U), the section's wall length -- and so the unrolled width -- grows like a
square root, so the literal outline has a spike with a vertical tangent there (0.8 mm at the topsheet's tip, 1.6 mm
from the plateau to its tip in the flat). No sampling resolves it and a spline cannot follow it: fitted through
the sparse kernel samples it rang by up to 325 um. Now (literal) the zone is fitted as its own piece with
shape-preserving slopes (no overshoot, within 22 um of its samples' curve next to the spike apex), and **Blend
across** is offered: a smooth transition from the last measured station before the zone to the first after it.
The default stays literal until you choose (open question in 2.7).

![Literal vs blend at the topsheet tip and tail](img/fig15_uturn_blend.png)

**U-turns: resolution and rule (2026-09-25 text).** On the topsheet the pressed step runs along the ski on each side, and at the tip and
tail it wraps round the end of the raised centre, crossing the ski (a U-turn in plan). There the step wall runs nearly
PARALLEL to the station planes, so a section slices along the wall instead of across it. Two separate issues follow:

- *Resolution (numerical).* Those stations need a slow kernel section (~0.1 s each), so adaptive refinement is capped
  there (0.01 mm / 3 mm spans). The flat outline in the last few tens of mm of tip and tail can be off by up to ~0.45 mm
  (fig08: the purple spike the feature's orange outline misses); everywhere else it is within ~1 um. Fix: lift the cap
  (~1-2 s more on the topsheet).
- *Rule (physical).* Section-by-section unrolling assumes the drape only needs unrolling across the width; in a U-turn
  the material is also folded lengthwise, so the literal rule puts large lengthwise stretch there (-9.6 % .. +10.9 %
  in the topsheet report vs about +-1 % elsewhere). The 0.5 mm bump may be an artefact of the rule. Decide the rule
  before spending time on resolution.

Undrape open decisions (research_undrape_map.md 9):

1. **U-turn rule.** The literal section-by-section unroll gives up to 15 % principal strain and a rim bulge of
   about 0.8 mm at the tip. The alternatives are a blend to sections normal to the step's plan direction, or a
   least-distortion fill. The rule lives in one function, `undrapeRefusedSection`.
2. **Transition-zone shear** (about 9 deg on the flaps): accept it or spread it.
3. **Kernel fallbacks** cost about 65 ms each (11 on the topsheet).
4. Parts not reaching the centreline are measured from their innermost node.
5. Gaps are bridged by a G1 circle vs a chord.
6. Fits near transition vertices: a 5-10 um feature within 0.2 mm of the vertex (finding c). An in-progress
   refactor adds end-of-edge checks (`UNDRAPE_END_CHECK`, uncommitted today).

Part mode limits (research_unwrap_part.md 7, 8b):

- **FAIL faces** (neither profile nor wall over a *curved* span, e.g. a fillet between a wall and a profile
  face, or a draped top) are reported, not handled.
- **Degenerate cell contact** (groove floors running out onto another face) makes the union fail. This is why
  the whole CORE cannot be rebuilt in one piece.
- Chains turning more than 180 deg are not split. The grazing-extension hazard is guarded, not removed. A closed chain
  around a HOLE is handled since 2026-09-30 (2.8); a closed chain around material (a boss, a round part) is not.
- "Straight" is decided by the **edge type** (`CurveType.LINE`), not by geometry.

Also open:

- The **preserve-length curve for parts** is one constant d.
- **Split-and-join fitting** (research_unwrap_perf.md 3.3).
- A tighter continuity gate than 0.57 deg (perf note 3.4 recommends 1e-3 rad).

## 2.6 Curved references, walls, and PRISM (2026-09-25)

The test parts happen to sit on a reference that is straight between the contact points. That is a special case: the
general case is a **baseline curved along its whole length**, and some parts will then have walls normal to the
baseline rather than vertical in world Z. Findings (research_unwrap_curved_ref.md):

- **Before PRISM, Part mode failed on a fully curved reference.** CORE and 4103 stopped with SPLIT_FAILED; the base,
  with exact walls, came out **silently wrong** (volume x0.94, 12 mm holes). The *reverse check* (map points of every
  result face back and measure the distance to the source) caught every silent failure, so it now always runs.
- **Walls: normal vs vertical.** A wall normal to the reference maps to a wall exactly normal to the flat plane. A wall
  vertical in world Z over a curved reference leans in the flat (over a 4 mm camber: 0.0014-0.0016 rad, ~6-11 um on
  a 12 mm core wall). Parts modelled with walls normal to the baseline therefore unwrap exactly; parts modelled with
  world-vertical walls carry that small lean (fudge room, if acceptable).
- **Thickened parts belong in the plate path.** 4802 is a 4.0 mm plate (thickness 3.996-4.001 mm). As a plate
  (Unwrap_Testing Copy 2, "Unwrap 4802 as plate", REF_WIRE offset 3.8 mm = its mid height) it unwraps to
  178.3968 -> 178.3967 mm, volume x1.000085, walls normal by construction. Its 1.2 deg "nose lean" is in the model:
  the nose-end walls meet the sides at 88.8 / 91.2 and 89.2 / 90.8 deg, while its long walls meet them at 90.00 deg.
  A true thicken would have none.
- **PRISM** (the band rebuild; the prototype numbers here, the as-built ones below): map every face into a *side view* (profile curves) and a *plan view*
  (wall curves), detect **bands** (regions of the plan with the same side profile: the core has 3 -- body, ledge ring,
  8 groove strips), build each band as plan-outline-extruded-vertically INTERSECT side-profile-extruded-sideways, then
  union. Core: 6-7 s on REF_WIRE, a camber, or FULL_BASELINE, within 3.4-4.7 um. Base 2.5 s, sidewall 3.1 s, 4803 1.0 s.
- **Arcs in PRISM.** In straight spans (rigid move) every original face survives, arcs included. In rebuilt spans
  the prototype *chained* tangent-connected wall faces into ONE fitted spline tool, so a run of sidecut arcs became
  one spline face (core 90 -> ~43 faces). It did not have to (now the **Rebuilt faces** choice): each source face can get its own tool, classified as an
  arc where its mapped curve is one (the cell method already does this: 4 true arcs on 4401). One face per source face,
  arcs kept, costs some speed.
- **Faces PRISM refuses.** PRISM can only build faces that are constant across the width (side-view extrusions) or
  vertical (plan-view walls). 4401's top faces are neither: across its ~23 mm width the top rises or falls by up to
  1.3e-3 (about 30 um edge to edge), a slight twist. They are refused rather than approximated: relaxing the test made a
  silently wrong body (volume x1.61), caught only by the reverse check. The cell method (ruled tools) handles them.

### As built (2026-09-25)

Part mode now does exactly that: rigid move over straight spans; over curved spans the band rebuild (PRISM) first,
the exact cell rebuild if PRISM refuses a face (beyond **Shape tolerance**) or misses, and a 16-points-per-face
reverse check on every rebuilt piece -- beyond max(Shape tolerance, 0.01 mm) the feature errors rather than return a
wrong body. Faces are kept one-per-source-face (arcs preserved) or merged, per **Rebuilt faces**.

| Part | Reference | Result | Time keep / merge | Reverse check |
|---|---|---|---|---|
| CORE | REF_WIRE | rigid + PRISM | 3.6 / 1.5 s | 0.22 um |
| CORE | camber | PRISM | 9.5 / 12.2 s | 4.5 um |
| CORE | FULL_BASELINE | PRISM | 9.7 / 12.6 s | 3.2 um |
| base (4101) | camber | PRISM, 14/14 cylinders kept | 2.5 s | 0.2 um |
| 4803 | camber / FULL_BASELINE | PRISM / cells | 1.2-2.0 s | < 1 um |
| 4103 | camber | PRISM | 3.3 s | 0.7 um |
| 4401 | camber, tol 0.03 mm | PRISM | 2.5 s | 25 um (its end caps) |

Refused cleanly (error, never a wrong body): 4802 and the base over FULL_BASELINE (their tips do not follow the raised
baseline tip -- they need their own reference or the plate path); 4401 below ~0.025 mm tolerance.

**4103 over FULL_BASELINE, diagnosed and fixed (2026-09-29).** The 83 um miss was a classification bug. Its tip cap
is a 6 mm^2 plane skewed in plan (flat normal y share 0.29) and leaning. `viewFit` chose the station direction by
the spread in the view; on a small, nearly square face that picked stations along the width and measured the face
across its height, so a skewed plane passed as a profile, was built as a vertical wall on its plan envelope, and
missed its lean by 83 um. Stations now run along the grid direction that travels least along the extrusion
direction (flat Y for a profile, Z for a wall). The cap is then (correctly) neither view within 0.005 mm (22.7 um
best), and a new class handles it: a **planar cap** (flat image a plane within the shape tolerance, facing along x)
closes both views on its outward envelope and the band is cut back to its plane by a box on its outer side. The exact
cell rebuild is still tried first where a piece has caps (4401 over REF_WIRE keeps its cells, exact). 4103 over
FULL_BASELINE: band rebuild + 1 cap cut, reverse check 5.5 um, length 1697.016 -> 1697.004 mm (-0.012 mm, the leaning
cap's extreme). 4401 over FULL_BASELINE at 0.005 mm still refuses (now "no plan cell holds material" with both caps as
planar caps; 0.03 mm works as before, 29 um) -- open. Test in the tree: "Unwrap 4103 L sidewall (part, along
FULL_BASELINE, keep faces) - expect OK, band rebuild + planar tip cap".

Also fixed: `partName` called getProperty in the feature body (illegal, correction 36), so every Part-mode refusal
surfaced as a getProperty warning and lost its own message. Messages now say "the part" and highlight it. **4401's twisted tops are not the blocker** -- they depart
by only 2.2 um. The blockers are its two tiny end caps (3 x 3-4 mm), which lean and are skewed in plan at once (best
single-view fit 17 / 25 um); the cell fallback then refuses the spline tops. So "essentially a perfect rectangle"
holds for the tops; the caps set the tolerance.

### Round 2 (2026-09-29): volume, determinism, speed, merged faces, joints

Measured with the eval-API harness (unwrapSolid called directly; forward check = 11 x 11 points per source face plus
10 per edge mapped forward, distance to the result) and the regression script; details in the corrections log 66-70.

- **4103 over FULL_BASELINE lost ~1 mm3 to ringing, not to the cap cut.** The spline tools were fitted through the
  adaptive rows at 1e-7 m, so approximateSpline interpolated the rows and rang between them: 45 um at the ledge taper
  (flat x 700-731), both inside and outside the source (correction 63's failure, now in Part mode). Fix: each freeform
  part is fitted through its row plus the midpoint of every span on the cubic the row was refined against (fitPoints,
  now shared from unwrap_part.fs), to 0.5 um (`UNWRAP_PART_ROW_FIT_TOL`; the rows are only known to a quarter of the
  shape tolerance). Volume x0.998639 -> x0.998739 = the predicted x0.998739; forward worst 45 -> 13.5 um (what is
  left is the tip walls, next item).
- **Faces sampled on their trim were measured at half their departure.** A face whose parameter box reaches past its
  trim is re-read on the trim only; on the 5 x 5 grid that left a few points. It is now read on 13 x 13: 4803's tail
  walls 6.3 -> 10.5 um, 4103's tip walls 4.4 -> 10.4 um. So 4103 over FULL_BASELINE at 0.005 mm is now **refused**
  (worst 0.0104 mm) instead of built 13.5 um off; the tree test runs it at 0.015 mm (renamed). 4803 over FULL_BASELINE
  stays on the exact cell rebuild (0.24 um); without this fix the ringing fix let its band rebuild through at 12.6 um.
- **Determinism.** Two full regressions with a full regeneration between them are identical (29 features). The
  geometry was always identical (4103: 80 vertices and 82 face points equal to 0.1 nm across regenerations); only the
  default-accuracy volume moved.
- **Speed** (harness request times, median of 3-5 interleaved runs, true original vs final): CORE over FULL_BASELINE
  keep 10.0 -> 8.8 s, merge 12.9 -> 8.6 s; 4803 3.9 -> 3.9 s; 4103 (0.015 mm) 7.6 -> 7.9 s (the 13 x 13 trim reads).
  Where it came from: `mergeOverlappingChains` stopped re-testing pairs that already failed and stopped copying
  reversed chains (~1 s of FS work), membership finds each probe's crossings by one sweep with binary search instead
  of every probe walking every segment (~1.3 s), and the merged chain is built, not fitted (below). Fitting through
  the samples alone at 0.5 um measured another 0.85 s faster with the same accuracy on all nine test cases; the
  midpoints were kept as the guard against ringing (a decision for the owner).
- **Merged faces are curvature-clean.** A merged chain is now the keep-mode parts joined EXACTLY into one rational
  cubic B-spline (lines as Beziers, arcs as rational quadratics raised to cubic, the spline parts as fitted), with a
  triple knot at each joint. The kernel needs G1 (BSPLINECURVE_NOT_G1 otherwise); where two parts meet at a small
  kink (0.001-0.04 deg on the CORE's outline, genuine creases in the source) the Bezier side is cut close to the joint
  and its short arm turned, moving the curve <= 0.1 um. CORE merge: curvature sign changes on the merged curves 69 -> 38
  (the parts themselves: 34; the 4 extra are those smoothed creases), off the exact parts 1.4 -> 0.1 um, control
  points 3086 -> 822. Faces 41 as before.
- **Joints.** Keep-mode parts are shaped as a set by curve_core's `shapeRuns` (as unwrap.fs's flat edges are): a line or
  arc is exact only where its ends run along the face's own end tangents (1e-3 rad slack), and its neighbours adopt its
  tangent. Kink = |result dihedral angle - source dihedral angle| at every near-tangent result edge (< 3 deg), worst,
  original -> now: 4803 over REF_WIRE 0.039 -> 0.005 deg (a line kinking against its arc neighbour), CORE over
  FULL_BASELINE 0.020 -> 0.020 (band boundaries, no source edge), 4103 over FULL_BASELINE 0.005 -> 0.005, 4103 over
  REF_WIRE 0.0006 -> 0.0006; base over REF_WIRE 0.003 -> 0.011, because a genuine 0.011 deg source crease between two
  spline parts is now one shared tangent (shapeRuns joins anything within G1_JUNCTION_ANGLE, 0.57 deg). Larger kinks
  (0.5-1.2 deg on 4401 at its cap cuts) are unchanged and not joint-related. The cell rebuild's tools (`chainTool`) are
  one curve per chain with no neighbours, so there is nothing for shapeRuns to settle there.
- **Slivers.** A chain end lying on another chain now continues IN ITS OWN CURVE past the junction (a line longer, an
  arc further round, a spline by a tangent line in the same B-spline) instead of a separate overshoot line: with 0.5 um
  fits a 0.53 deg crossing on the CORE's top landed 8 um past the end and the overshoot line became 8 sliver faces.
  The overshoot is also at least 4 x the end's distance from the chain it lies on: **4401 over FULL_BASELINE at
  0.005 mm now builds** (its plan outline had stayed open by 9 um, "no plan cell holds material"): forward worst
  3.6 um, volume x1.000051 against a predicted x1.000050. Tree test added.

Tests in the tree (Unwrap_Testing Copy 2): "Unwrap ... (part, along FULL_BASELINE, keep/merge faces) - expect ...",
"Unwrap 4103 L sidewall (part, along FULL_BASELINE, keep faces, 0.015 mm) - expect OK, band rebuild + planar tip cap",
"Unwrap 4401 L (part, along FULL_BASELINE, keep faces) - expect OK, band rebuild + 2 planar caps".
Open: 4103's tip walls (10.4 um lean: squareWalls or a looser tolerance), 4401's 3 sliver faces (0.6-5 um wide) at
its planar cap cuts and 0.5-1.2 deg kinks there, 4803 over FULL_BASELINE band-rebuild kinks (moot while cells take it);
walls-normal vs world-vertical modelling convention.

## 2.7 Your two questions

### (a) "Our first big test will be a reference curved along its whole length (a baseline). How will that affect evaluation times?"

**Edges and plate modes: about the same cost.** Both map every sample or station through the chart
individually, with a Newton foot that converges in 1-3 steps whether W is straight or curved (0.12 ms per
point on the packed tables). What does change:

- **Edges:** the flat curves inherit every curvature jump at the baseline's edge joins (1.9). Refinement adds a
  few samples there, and long freeform edges need more control points (about 60 for 5 um on U1's 1480 mm
  edge). Time barely moves; curve weight goes up.
- **Plates with a Face target** do not use the baseline at all. Their W is their own section.
- **Plates with a Wire target**: the mid-surface of a plate far above a curved baseline no longer sits at a
  constant offset. Every region away from d gets kappa*(h - d) lengthwise strain. That is question (b)
  territory, not a speed issue.

> **Update 2026-09-25 (measured, research_unwrap_curved_ref.md; see 2.6):** today's Part mode does fail on a fully
> curved reference (CORE, 4103: SPLIT_FAILED; base: silently wrong, volume x0.94). The piecewise-rigid idea below is
> ruled out (first-order end wedge; rigid moves of the core are not acceptable anyway). PRISM (band rebuild) does the
> core in 6-7 s within 3.4-4.7 um on a camber and on FULL_BASELINE. The analysis below is kept as the reasoning.

**Part mode: a big cost as built, and likely failures.** Part mode gets its speed from moving the pieces over
STRAIGHT spans of W rigidly and rebuilding only the curved spans. Today's CORE is 1480 of 1500 mm rigid, so
only two 10 mm ends are rebuilt (1.6 s). With a baseline curved everywhere:

- `referenceSpans` finds **no line edge**, so the whole part is one curved piece and goes through the cell
  rebuild. Straightness is decided by edge type, so even a geometrically straight *spline* span counts as
  curved.
- The measured whole-core rebuild was **31 tools, about 650 cells, 32 s, and then `BOOLEAN_NON_MANIFOLD_RESULT`**.
  The groove floors run out onto the bottom face and the cells touch degenerately (research_unwrap_part.md 3,
  C). So the CORE would most likely *fail*, not just be slow.
- Faces that are only approximately profile faces pass today because they lie over the line (4401's top
  faces, b up to 1.3e-3). Over a curve they would be classified **FAIL**.

**The piecewise-rigid hypothesis: I tested it and it does not hold as stated.** The idea was to cut the part
where W turns little and move each piece rigidly, with an error of about 0.5*h*dtheta^2. I computed the exact
error: the best least-squares rigid placement of a 14 mm-thick piece over a circular span, against the exact
chart map. It is **first order** in dtheta. Two terms add up:

- **A length wedge.** A rigid piece keeps *every* fibre's length, while the chart keeps only d's. At the piece
  ends, the fibres at h are off by about (h - d) * dtheta / 2. Adjacent pieces then gap or overlap in a wedge.
- **W's own sagitta** L^2 / (8R). A rigid piece stays curved: it is never flattened.

![Rigid piece error](img/fig12_rigid_piece_error.png)

For 0.01 mm, the longest rigid piece is:

| Camber radius R | d = 0 (length on W) | d = core mid (7 mm) |
|---|---|---|
| 10 m | **14 mm** | **29 mm** |
| 30 m | 43 mm | 60 mm |

The hypothesis predicts about 400 mm (dashed lines in the figure). It leaves out both terms above. Pieces of
15-60 mm would mean 25-100 splits, rigid moves and a faceted union per part. Neither fast nor clean (the flat
faces become facets kinked by dtheta at every joint). **Hypothesis rejected at 0.01 mm**, unless the numerical
check in `img/src/fig_concepts.py` (`rigid_error`) has a flaw. Please check the setup: a circle of radius R, a
rectangle 0..14 mm high, and a least-squares rigid fit.

**What I would test instead** (all hypotheses, in order of promise):

1. **Piecewise *rebuild*.** Cut the part by station planes into pieces of about 150-300 mm and cell-rebuild
   each piece separately. Cost grows with tools x cells *per piece*, and one piece of 4802's size (180 mm,
   12 tools, 108 cells) takes 2.7 s. **Estimate: 20-40 s for a 1.5 m core**, to be measured. Risk: a piece
   containing a groove run-out still hits the degenerate union. Cut *through* run-outs, or test it.
2. **Geometric straightness.** Treat a span as straight when its turning over the piece is below the rigid
   limit above, not when its edge type is LINE. This only helps if the baseline has really straight stretches.
3. **Build the flat core from its design inputs.** Thickness profile, plan outline and groove layout are
   usually defined flat anyway; unwrapping would then only be the *check*.

### (b) "Top parts (6005, 4310) should be unwrapped along the top surface, not the ref curve, otherwise offset differences are all over the map. Does our code account for that?"

![Which curve](img/fig14_which_curve.png)

Mostly yes for plates, not yet for solids.

- **Plates in the test studio use "Section by a face"** (base, 4305, 6005, 4310, topsheet). Each plate's W is
  *its own mid-surface's* Front-plane section, so each keeps **its own mid-line length**. That is the neutral
  surface of a symmetric laminate (1.4), and the reason for the 0.002-0.006 mm length agreement. The physically
  right choice is its *own* surface, in fact its mid-surface, rather than literally the top face. For a 0.6 mm
  laminate, top vs mid differs by 0.3 mm x (total turning), which is a few tenths of a millimetre over a ski.
- **Mats and shears** do not reach the alignment X, so they cannot use their own section (2.5). They use
  REF_WIRE plus a fixed target offset at their near-constant mid height (1.8-1.96 mm): lengths within 0.001 mm.
  This is correct only because they are thin and sit at nearly constant height.
- **Part mode keeps length along REF_WIRE offset by ONE constant d.** 4802 sits 1.8-5.8 mm above REF_WIRE on
  the tip curve, so with d = 0 its fibres are stretched by kappa*h: +1.25 % volume. The flat length changes by
  0.59 mm per mm of d, from 180.62 at d = 0 to 178.40 at d = 3.8 (its middle). **With d = 3.8 mm its volume is
  within 0.01 %.** So set Preserve length = Offset, 3.8 mm for 4802 today.
- **Natural next step** (the docstring's "preserve length profile {MIDDLE, NEUTRAL_AXIS, QUERY}").
  research_unwrap_part.md 6.2 has the maths already: for a length curve at variable height g(s),
  `X(s) = s - integral(kappa * g ds)`, which reduces to s - d*theta for constant g. Nothing else changes:
  profile and wall classes, rigid spans, and the inverse (a 1-D Newton on X) all carry over. MIDDLE = the
  part's own y = 0 section mid-line (the `profileFromFace` machinery already exists). QUERY = any curve.
- **Trade-off to decide.** With each part on its own curve, **joints between layers no longer coincide in the
  flat**: they are offset by (delta d) x theta, about 2 mm between the base (d = 0.6) and 4802 (d = 3.8). That is
  right for cutting blanks, and wrong if the flat set is used together (a flat assembly, press tooling, nesting
  that relies on relative positions). Composite-part unwrap should force one choice for all its members.

### (c) Open (2026-09-29): which U-turn rule should be the default?

"Section literally" keeps a 0.8-1.6 mm spike at the tip and tail of the topsheet and 6005 (the rule is singular at
the U apex; figure in 2.5). "Blend across" gives a smooth transition, measures nothing inside the zone, and saves
the 18 kernel sections (about 1.6 s on the topsheet). Volume x0.999924 literal vs x0.999624 blend on the topsheet.
Test in the tree: "Unwrap Topsheet (plate, own section, U-turns blended) - expect OK, smooth tip and tail".

## 2.8 Holes in Part mode (2026-09-30)

**Question:** does Unwrap support holes? Before this pass: Edges mode yes (each edge unwraps on its own; a closed edge
now too, see below); constant-thickness plates yes (the loop-parity rebuild cuts every inner loop); Part mode over a
STRAIGHT span yes (the piece moves rigidly and keeps every face). Part mode over a CURVED span: no, and in one case
silently wrong. Measured with the eval harness on a 300 x 60 x 10 mm block thickened off FULL_BASELINE (x 450-750,
camber) and on the CORE (d = 0, keep faces, shape tolerance 0.005 mm):

| Case (curved span unless noted) | Before | After | Forward / reverse (um) | Volume ratio vs slab prediction |
|---|---|---|---|---|
| Block, round through hole, world-vertical | refused ("degenerate ruling") | built, 1 hole cut | 0.04 / 0.02 | 0.9998990 vs 0.9998986 |
| Block, same hole, squareWalls on | **silently wrong: hole filled**, internal reverse check 0.02 um | built | 0.04 / 0.02 | as above |
| Block, round hole normal to the reference | **silently wrong: hole filled** (fwd miss 5.0 mm) | built | 0.04 / 0.02 | 0.9998990 vs 0.9998986 |
| Block, slot (stadium), world-vertical | built (cells), 4.1 / 1.9 um | built (band rebuild + tube) | 0.02 / 0.03 | 0.9998989 vs 0.9998985 |
| Block, blind pocket, world-vertical | refused | built | 0.17 / 0.16 | 0.9998992 vs 0.9998988 |
| Block, blind pocket normal to the reference | refused | built | 0.17 / 0.16 | 0.9998992 vs 0.9998988 |
| Block, counterbore (pocket + through hole) | refused | built, 2 holes cut | 0.17 / 0.16 | 0.9998991 vs 0.9998987 |
| Block, all five together (merge faces / squareWalls too) | refused | built | 0.17 / 0.17 | 0.9998993 vs 0.9998988 |
| CORE, round hole at x 600 over FULL_BASELINE | refused | built, 1 hole cut | 3.78 / 3.22 (the CORE's own level) | 1.000085 |
| CORE over REF_WIRE, holes at x 141 (tail arc piece) and x 600 (straight) + one on the x 145 junction | -- | tail hole cut, x 600 moved rigidly, junction half-holes as open chains | 5.2 / 4.4 | 1.000058 |
| Block over REF_WIRE (straight span): control | exact (rigid) | unchanged | 0 / 0 | 1.0000000 |

(The slab prediction cuts the source into 4 mm slabs normal to the reference and sums V (1 - kappa d) / (1 - kappa h);
1.7).

**Why it failed.** A round hole's wall is ONE periodic face (or a tangent ring of faces). Its plan row is a closed chain.
PRISM extended the chain's "free" ends to the box (BAD_GEOMETRY) and fell back to the cell rebuild, which either
refused the wall (a world-vertical hole over a camber leans by the slope, 17-20 um over its height, and its closed row
gave a degenerate ruling) or built the tube with no inside cell and **kept the hole filled -- and the reverse check
passed**, because a filled hole leaves no face to sample and the 16 samples of the top face rarely land over the hole.

**What it does now** (unwrap_part.fs, `rebuildCurvedPiece` -> `holeRings` / `holeVoids` / `rebuildWithHoles`):

1. **Find the holes** from the PRISM fits the piece is sampled for anyway (`prismFits`, no extra sampling when there
   are none): wall-like faces (vertical in the flat or leaning less than 0.05) chained like PRISM's rows; a chain that
   closes on itself with its outward normals pointing INTO the loop is a hole (material outside). The faces inside the
   loop are its floors (side-view faces only); a wall of another hole inside it (a counterbore's bore) is that hole's.
2. **Exact rows**: each wall face's rows at both ends of its rulings and in the middle, refined to a quarter of the
   shape tolerance; the rulings are checked straight (the middle row on the line between the end rows, to a quarter of
   the tolerance). World-vertical walls over a camber are straight lines in 3D and (to ~0.2 um) in the flat.
3. **The tube**: every ruling cut at two flat heights just outside the part, the two rows fitted with the same
   parameters (same knots) and written down as a ruled B-spline surface -- one closed periodic surface for a one-face
   hole; for a ring of faces (a slot) one patch per face, ends on the shared rulings with shared tangents, sewn by a
   union (one interpolation across the slot rang 8.6 um where the arc meets the line).
4. **Voids**: a box split by the tubes; the columns split by the floors; a column cell inside a hole's outline and on
   the open side of all its floors is a void (it may reach past the part: subtracting air is harmless). A void cell that
   maps back into the material is an error.
5. **Fill and rebuild**: the hole faces are deleted on a copy (the kernel heals the top and bottom across; measured
   identical to the unholed block to 1e-4 mm3), the filled copy goes through PRISM / cells as before, the voids are
   subtracted, and the result is checked against the real piece **both ways**.
6. **Every rebuilt piece is now checked forward too** (`pieceCheck` = `reverseCheck` + `forwardCheck`: 5 points per
   source face mapped forward, distance to the result). Cost on the CORE over FULL_BASELINE: 9.05 -> 9.8 s at 3 x 3 points,
   so 5 points are used. A hole adds about 2.4 s there (12.2 s): the filled copy is analysed and rebuilt again.

If anything in the hole path fails it is rolled back and the piece goes the old way, where the two-way check refuses
what cannot be built. **Not handled** (refused, never built wrong): walls that are not ruled, floors with a fillet or a
step, bosses (closed rings around material), closed side-view rings (a tunnel along Y), holes cut by a junction plane
(each half is an open chain: PRISM or cells as before).

**Closed edges in every mode.** An edge whose ends meet (a hole's circle) used to be emitted as a clamped spline closing
on itself, which the kernel refuses (BAD_GEOMETRY): Edges mode could not unwrap a hole's rim. It is now emitted on its
own (`emitClosedFlatCurve`): an exact circle when its flat points are one, else a periodic interpolation.

Tests ("Unwrap hole & face tests", `devtools/onshape/build_unwrap_hole_face_tests.py`): "Unwrap HA block, round hole +
slot + pocket + counterbore (part, along FULL_BASELINE) - expect OK, 5 holes cut", the same block along REF_WIRE
(straight: rigid), "Unwrap HA CORE with 2 holes (part, along FULL_BASELINE, keep faces) - expect OK, 2 holes cut",
"Unwrap HA CORE with 2 holes (part, along REF_WIRE) - expect OK, tail hole cut, mid hole moved rigidly".

## 2.9 Faces / surfaces mode (2026-09-30)

"Keep faces": unwrap the edges and rebuild the faces on them. `unwrapFacesToSheet` (unwrap.fs):

1. **Edges once.** Every edge of the selected faces goes through the Edges pipeline once, so two faces sharing an edge
   get the identical flat curve (shared vertices exact). Fitted to 0.25 um (not the 5 um Tolerance) and up to 300 control
   points: see sewing below.
2. **Each face on its own curves** (`flatFace`): the face's flat image sampled on its trim (13 x 13) decides the support:
   - a **plane** when the image is one within 0.5 um (a face on the reference's offset: flat exactly);
   - an **extrusion** along flat Y (a profile face: every face extruded across the reference plane, e.g. a ski's top
     surface over any baseline) or flat Z (a wall), within 0.5 um -- PRISM's rows and tools;
   - otherwise a **fitted B-spline surface** through the face's TRIM parameter range mapped flat (rows as one family,
     then the rows' control points across), fitted to 0.1 um and extended 2 mm.
   The support is split by the face's flat curves (the region holding its mapped interior point is kept) and the face is
   **checked both ways against its source face** (forward 5 x 5, reverse 4 x 4, denser when the trim fills little of
   the parameter box), limit max(Tolerance, 0.01 mm). Each support is a sub-feature attempt: if the fitted one misses, a
   plane / extrusion within the Tolerance is tried (accurate but it may not sew); if none passes, the face is refused.
3. **Sewing.** The faces of one source body are united into one sheet. Measured: the sheet union sews edges up to 1 um
   apart and fails outright (BOOLEAN_INVALID, the whole union) from 2 um. Each face imprints the shared curve on its own
   surface, so neighbours part by (a) their surfaces' differences -- why only exact planes / extrusions are used and the
   fitted surfaces go to 0.1 um -- and (b) the curve's own error where they meet at an angle -- why the edges are fitted
   to 0.25 um in this mode (5 um curves: 2-3 um gaps). If the union still fails, faces are joined one at a time; the
   ones that will not sew stay separate sheets and the notice warns ("N sheets where the source's faces make M groups").
4. The sheet is placed in the origin's frame; names, properties and the length check as in the other modes.

Measured (eval harness, FULL_BASELINE unless noted; forward = 11 x 11 source points per face mapped forward, reverse =
8 x 8 flat points mapped back):

| Input | Supports | Result | Forward / reverse (um) | Time (request) |
|---|---|---|---|---|
| Ski_Top_Surf sheet, 3 faces, 10 edges | 1 plane, 2 extruded | 1 sheet, 3 faces, 10 edges (8 laminar) = source | 0.22 / 0.21 | ~5 s |
| Topsheet top face (16 edges) | extruded | 1 face, 16 edges | 0.06 / 0.04 | 3.6 s |
| Topsheet step face (8 edges, 1-3 um end edges) | fitted | 1 face, 8 edges | 0 / 0 (below kernel resolution) | 1.6 s |
| Block top face, 4 inner loops (round, slot, pocket, counterbore) + pocket floor | planes | 2 sheets (the floor shares no edge: 2 groups) | 0.20 / 0.18 | ~1 s |
| CORE bottom face, 2 hole loops, REF_WIRE (tree test) | plane | 1 face, 56 edges | per-face check only | -- |
| CORE: 64 faces around its fillets and the hole (stress case) | 8 planes, 35 extruded, 21 fitted | **2 sheets** for 1 group (one face does not sew), 64 faces | 2.7 / 8.3 | 31 s |

Found on the way: a chart foot warm-started from a point far away can converge on the wrong span (the fitted support of
a CORE ledge wall came out 250 mm tall; it also produced a false 0.56 mm "notch" on the step face): the fit grid now
restarts the warm start on every row. Tests: "Unwrap HB Ski_Top_Surf sheet (faces, along FULL_BASELINE) - expect OK, 1
sheet, 3 faces, 10 edges", "Unwrap HB Topsheet top face ...", "Unwrap HB Topsheet step face ... fitted surface",
"Unwrap HB block top face with 4 hole loops ...", "Unwrap HB CORE bottom face with 2 hole loops (faces, along REF_WIRE)
- expect OK, planar, 2 inner loops".

Open: sewing faces whose surfaces are only within the Tolerance of their image (the stress case above); a fitted
surface for faces whose trim does not fill a rectangle in parameter space (it is fitted over the trim's bounding
range, and the per-face check refuses it when that fails).

---

# Part 3: The codebase

## 3.1 File map and call graph

![Call graph](img/fig13_call_graph.png)

| File (tab) | Role |
|---|---|
| `driven_offset/unwrap.fs` (a84cdaa8963f2a55db1c016b) | The feature: dialog, three modes, edge sampling and emission, plate side finding / section face / plate rebuild, length check, names, report, editing logic. The user's docstring spec is at the top; the AS BUILT block follows it. |
| `driven_offset/undrape_utils.fs` (283b8f7562a16e9c9ccc01b7) | The undrape map: `undrapeOutline` and everything under it. Plain-number inner loops. |
| `driven_offset/unwrap_part.fs` (fc976128871c5b4b2d33a91c) | Solid unwrap: `unwrapSolid`. Needs `extendendtype.gen.fs` and `extendsheetshapetype.gen.fs` imported explicitly (correction 17). |
| `driven_offset/edge_offset_utils.fs` (a2665e22c07b7a6929ce4e80) | The chart ("The reference surface as a chart", "Unwrapping through the chart", "Packed chart" sections, lines ~931-1760), shared with Driven edge offset / surface. |
| `curve_tools/curve_core.fs` (Curve_tools document) | Chains, `classifyPoints`, `emitLineCurve` / `emitArcCurve` / `emitSplineCurve`, `approximateFamily`, `G1_JUNCTION_ANGLE`. |
| Variable_tools `extract_outputs.fs` | `embedStandardOutputs`, `extractableVariable`, `extractableQuery`. |

Key entry points (line numbers as of 2026-09-25; they drift):

- unwrap.fs: `unwrap` :199, `checkedChart` :557, `unwrapEdges` :657, `adaptiveEdgeSamples` :720,
  `spanMidMiss` :835, `unwrapPart` :860, `lengthAndVolume` :907, `emitFlatCurve` :1126,
  `plateSidesGeneral` :1225, `profileFromFace` :1382, `plateFromOutline` :1432, `unwrapPlate` :1502,
  `unwrapEditLogic` :1642, `applyNamesAndProperties` :1775, `reportSummary` :1829.
- edge_offset_utils.fs: `buildAlongReference` :953, `referenceSurfaceCoords` :1265 (unit-based, older),
  `unwrapChart` :1330, `chartFromReference` :1341, `unwrapFast` :1368, `chartFootConverged` :1384,
  `unwrapInverse` :1454, `unwrapReferenceProblem` :1485, `unwrapDirection` :1514, `packChart` :1541,
  `chartEval` :1591, `chartFoot` :1634, `referencePlaneNormal` :1718.
- undrape_utils.fs: `undrapeOutline` :2508, `undrapeResolveBatch` :2754, `undrapeSection` :1329,
  `undrapeSectionMid` :1407, `undrapeSectionByFaces` :1494, `undrapeRefusedSection` :2320 (THE U-TURN RULE),
  `undrapeRefine` :3191, `undrapeAssemble` :3588, `undrapeDeformation` :3684.
- unwrap_part.fs: `unwrapSolid` :116, `referenceSpans` :231, `rigidTransform` :274, `flatNormal` :361,
  `rebuildPiece` :384, `faceKind` :646, `chainRows` :783, `chainTool` :1149. Added 2026-09-29 (search by name):
  `fitPoints` / `quadraticSlope` / `monotoneSlope` (moved here from unwrap.fs, which imports them), `chainShapes`,
  `emitChainParts`, `emitMergedChain` + `g1Runs` / `splitCubicPiece` / `joinedCubicPieces` (exact merged chains),
  `extendedPieces` / `extendedArc` (junction overshoot in the curve), `probeCrossings` (membership sweep).
  Added 2026-09-30: `prismFits` / `prismAnalysisOfFits` (prismAnalysis in two stages), `rebuildCurvedSolid` (the old
  rebuildCurvedPiece body), `rebuildWithHoles`, `holeRings` / `holeRing` / `coarseWallRow` / `ringFaceRows`,
  `holeVoids` / `holeTube` / `ruledPatch`, `forwardCheck` / `pieceCheck`.
- unwrap.fs, added 2026-09-30: `emitClosedFlatCurve`; Faces mode `unwrapFacesToSheet`, `flatFace` / `flatFaceOn`,
  `planeSheet`, `fittedSupport` / `faceTrimBox`, `flatFaceCheck`, `joinFaceSheets`, `edgeConnectedGroups`.

## 3.2 Key data structures

**The chart** (`unwrapChart` -> `chartFromReference`):

    chart = { alongRef,                       // unit-based tables (buildAlongReference)
              packed,                         // the same, plain numbers (packChart)
              alignX, alignV, alignHeight }       // the alignment point's x (arc - delta*theta), v, height; metres

    alongRef = { chain, arcs[], thetas[], curvatures[], xs[], arcSlopes[], points[], tangents[], delta, planeNormal }
      25 samples per reference edge (TURNING_SAMPLES); duplicate samples at joins, each carrying its own edge's
      curvature (essential: research_unwrap_perf 1.2). theta is trapezoid-integrated signed curvature.
      points / tangents / xs are of the reference OFFSET BY delta.

    packed = { count, arcs[], px[], py[], pz[], rx[], ry[], rz[], kappa[], theta[], delta, nx, ny, nz }
      r = (1 - delta*kappa) * t  (d(offset point)/d(wire arc)); n = planeNormal.
      chartEval(c, a, span) -> [ax, ay, az, tx, ty, tz, theta, kappa, scale]: position by cubic Hermite, tangent =
      that cubic's derivative (so the foot is the foot of ONE consistent curve), theta Hermite with kappa as its
      slope. Straight run-on past either end.

Plain numbers are used because FeatureScript unit arithmetic is operator overloading, which costs about 40x
plain arithmetic. The per-point map went from 5.4 ms to 0.117 ms.

**The per-point result** (`unwrapFast(chart, point, previous)`), an array indexed:

    0 x (flat, = arc - delta*theta - alignX)   1 y (= alignV - v)   2 z (= height - alignHeight)
    3 arc (on the wire)   4 span   5-7 tangent t   8 kappa   9 scale   10 height   11 residual
    y is measured along -pn so (x, y, z) is right-handed (1.5; the note is on unwrapFast's docstring).
    chartFootConverged(u): |residual| <= 1e-7 m.  Pass `previous` to warm-start along an edge.

`unwrapDirection(chart, cs, u, d)` maps a unit direction exactly: the along-component is scaled by
scale/(scale - kappa*h). `unwrapInverse(chart, x, y, z)` is the exact inverse on the same tables (used for Part
mode cell membership).

**The undrape** (`undrapeOutline(context, id, chart, side0, side1, thickness, {spacing, deformation})`):

- It returns `edges[]`, one per rim edge: `{ loop, index, points [[x, y]], startTangent, endTangent, arcs }`.
  Loop 0 is the outer loop (counter-clockwise); holes run clockwise. Shared vertices are exact.
- The `report` holds stations, samples, passes, fallbacks, failed / failedArcs / droppedPoints / shifted,
  stretchMin / Max / Where, shearMax, rim3d / rimFlat, plus `lines` for printing.
- The crossing record (a 25-number array: w, h, tangents either side, inPlane, cosCross, rim, edge, normal, ...)
  is documented at the top of undrape_utils.fs.

**Part mode** (`unwrapSolid(context, id, chart, cs, part, {squareWalls, flatTolerance})`) returns
`{ bodies, report {pieces, rigidPieces, rebuiltPieces, cells, keptCells, tools, planeTools, arcTools,
splineTools, ruledTools, snappedTools, squaredWalls, maxLean}, lines }`.

## 3.3 Walkthrough per mode

- **Edges:** `checkedChart` -> `unwrapChart(W, align, d)`. Then per source body `unwrapEdgesToWires` ->
  per edge `adaptiveEdgeSamples` (seed from evCurveDefinition, then refine by `spanMidMiss`; feet by
  `checkedFoot` = `unwrapFast` warm, cold retry) -> points in cs -> `unwrapDirection` end tangents ->
  `emitFlatCurve`. Finally `opExtractWires`, and the curves are deleted.
- **Plate:** `plateSidesGeneral` (evOffsetDetection pairs grown by 45 deg tangency, largest area wins;
  raycast fallback). For a Face target, `opExtractSurface(side, -t/2, trimmed)` gives the mid-surface
  (retrying with the other side), then `profileFromFace` gives W. Then `checkedChart(W, offset)` ->
  `undrapeOutline` -> `emitFlatCurve` per outline edge -> `plateFromOutline` (sheet split, parity, extrude
  +/- t/2).
- **Part:** `unwrapSolid`: copy -> junction splits -> rigid `opTransform` / `rebuildPiece` -> union ->
  fuse check -> place in cs. Then `layOnPlane` translates it.
- **All modes:** `lengthAndVolume`, `applyNamesAndProperties`, `reportSummary` (info, or a warning when rows
  are stale or undrape stations failed), `embedStandardOutputs`.
- **Editing logic** `unwrapEditLogic`: only on the button (`clickedButton == "readProperties"`). It rebuilds
  the rows by source name and keeps an edited output name and copy choice where the name still matches.

## 3.4 Import pins

| Importer | Imports | Pin | Meaning |
|---|---|---|---|
| unwrap.fs | edge_offset_utils.fs | `70dcbbcd66e91d6405084776` | a same-document **microversion** pin: frozen until someone re-pins it |
| unwrap.fs | undrape_utils.fs, unwrap_part.fs | `""` | same document, **always the current workspace** tab: edits there are live immediately |
| undrape_utils.fs, unwrap_part.fs | edge_offset_utils.fs | `70dcbbcd...` | the same microversion as unwrap.fs (**keep all three identical**, or two different chart versions meet) |
| edge_offset_utils.fs | curve_core.fs (Curve_tools) | version `9d6f0887...` (export import) | a cross-document **version**: only the user creates versions |
| unwrap.fs | extract_outputs.fs (Variable_tools) | version `b8c80ac0...` | same |
| DEO / DOS / ORT / debug | edge_offset_utils.fs | `904e3070...` (older) | the later utils changes only ADD functions; re-pin when the chain is next walked |

Rules learned the hard way:

- **Never push a placeholder import path.** The whole unwrap tab failed to compile for minutes.
- A change to edge_offset_utils.fs reaches unwrap only after a re-pin. Re-run the Design_Master fingerprint
  (`devtools/onshape/fingerprint.py`) whenever the shared chart changes.
- Procedure and the full pin table: `driven_offset/DOCUMENT_MAP.md` ("Import chain").

## 3.5 How to test

1. `python fscheck.py driven_offset/*.fs` (all tabs of the document together) before any push.
2. Push with `python -m sync.main pushproject driven_offset --files <f.fs> --check`. Only when asked.
3. **Test features live in the studios' feature trees**, named by case and expected result:
   - `devtools/onshape/build_unwrap_tests.py` (Copy 1: U1-U3)
   - `devtools/onshape/build_unwrap_parts.py` (Copy 2: 9 plates + 7 parts; `ONLY=<bodyId>` and
     `DEBUG=<bodyId>` env vars)
   - `devtools/onshape/build_unwrap_hole_face_tests.py` ("Unwrap hole & face tests" 56c29f15db6047862561aa7b: hole
     fixtures + 4 Part-mode hole tests + 5 Faces-mode tests; rebuilds everything after "Boolean 1")
   These upsert features by name.
4. **Regression:** `FS_SYNC_TIMEOUT=300 PYTHONPATH=. python devtools/onshape/check_unwrap_regression.py [--save]`
   reads every Unwrap feature's status in Copy 1 and Copy 2 and its published `lengthWrapped` / `lengthFlat` /
   `volumeRatio` through the eval API (`getVariable` on the feature id), and compares them with
   `devtools/onshape/fingerprints/unwrap_baseline.json` (`--save` rewrites the baseline). Evaluate offset:
   `PYTHONPATH=. python devtools/onshape/check_evaluate_offset.py`; the Design_Master fingerprint
   (`devtools/onshape/fingerprint.py`) whenever edge_offset_utils changes.
5. **Runtime notices:** `python -m sync.main notices driven_offset --monitor "Unwrap_Testing Copy 2"`.
6. **Read-only geometry probes:** the FS eval API (one anonymous function, std only, `qTransient("<id>")`).
   The scripts in `img/src/*.fs` are examples. They run ops in a throwaway context and never change the
   studio.

## 3.6 Where the design and research notes are

| Note | What is in it |
|---|---|
| unwrap.fs header | The user's spec (docstring), then the AS BUILT summary. |
| `driven_offset/research_unwrap.md` | The 09-10 pre-build plan (chart reuse, circularity test). **Partly superseded**; a note at its top says where the as-built record is. |
| `driven_offset/research_unwrap_perf.md` | Chart accuracy (weak links, fixes), the packed evaluator (46x), fitting: the chord-squared tangent bug, CP counts, split-and-join, defaults 0.005 mm / 60. |
| `driven_offset/research_undrape_ops.md` | Which Onshape ops: finding sides and t, mid-surface, W from a face, rebuilding the plate (sheet split + parity). |
| `driven_offset/research_undrape_map.md` | THE undrape design: map, measured part, costs, algorithm, accuracy, deformation, edge cases, open decisions (9), as built (12), 4305 wings (13), robustness pass (14). |
| `driven_offset/research_unwrap_part.md` | Part mode: face classes, strategies A-D, measured accuracy and time, decisions, variable length curve (6.2), as built (8, 8b). |
| `driven_offset/DOCUMENT_MAP.md` | Tabs, pins, test studios of the driven_offset document. |
| `.claude/featurescript-corrections.md` | Especially 17 (gen.fs imports), 22 (opLoft), 25 (defaults migrate into saved features), 27 (buttons), 36 (getProperty), 44 (MC as vertex). |
| memory `unwrap-undrape.md`, `evaluate-offset.md` | Session history and decisions. |

## 3.7 Glossary

- **W**: the wrapped reference. **d**: the preserve-length (or target) offset. **s / arc**: arc length along
  W. **theta**: cumulative turning. **kappa**: signed curvature (T' = kappa N).
- **pn**: W's plane normal. **N = pn x t**: the surface normal (height). **v**: across; **h**: height.
- **Station**: the plane normal to W at an arc. **Section**: the part cut by a station.
- **Rim**: the plate's outline edges. **Bend line**: an interior edge running along the plate.
- **Refused station**: a station measured by a kernel section. **U-turn**: where the pressed step runs across
  the stations.
- **Profile face / wall / ruled / FAIL**: Part mode face classes. **Cell**: a piece of the split box.

## 3.8 Regenerating the figures

From `driven_offset/docs/img/src/`, and only the fetch scripts touch Onshape (read-only eval API):

    # data (repo root; about 1.5 min for the sections)
    FS_SYNC_TIMEOUT=240 PYTHONPATH=. python driven_offset/docs/img/src/fetch_topsheet.py 4 80
    FS_SYNC_TIMEOUT=240 PYTHONPATH=. python driven_offset/docs/img/src/fetch_layup.py
    FS_SYNC_TIMEOUT=240 PYTHONPATH=. python driven_offset/docs/img/src/fetch_wires.py
    # figures (from img/src)
    python fig_concepts.py     # fig01-05, 10, 12, 13, 14
    python fig_topsheet.py     # fig06-09 (topsheet.py does the numpy undrape)
    python fig_adaptive.py     # fig11

The data files are in `img/src/data/`. fig05, fig08 and fig10 read the Copy 2 body ids (RtjD, RnRD, RIpD
= the flat topsheet output, ...). Transient ids can change if the studio is rebuilt; update them in
`layup.fs`.

---

# Appendix: unclear or contradictory items found while writing this

Status after the 2026-09-25 cleanup pass: **RESOLVED** items say what changed; the rest are still open.

1. **RESOLVED** (unwrap.fs' AS BUILT block now points to this document and the perf / part / undrape notes;
   research_unwrap.md carries a "superseded in part" note at its top). **research_unwrap.md "section 0" does not exist.** unwrap.fs' AS BUILT block points to it. The file is the
   09-10 pre-build plan and differs from the build. It proposed arc-keyed tables so a *vertical tip* could be a
   reference, seeding by projection, and Greville-abscissa sampling. The build seeds by X, **requires X to
   rise**, and samples adaptively.
2. **RESOLVED** (section 15, "As built: adaptive sampling", now exists). **undrape_utils.fs cites "research_undrape_map.md 15"** for adaptive edge sampling. The note ends at
   section 14.
3. **RESOLVED** (DOCUMENT_MAP.md now has the pins read from the code, the unwrap family with tab ids, the four
   test studios and the regression scripts). **DOCUMENT_MAP.md is stale for unwrap.** It says unwrap / undrape_utils pin edge_offset_utils at mv
   `941e620c`, while the code pins `70dcbbcd`. It does not list unwrap_part.fs or Copy 2. Its table still calls
   unwrap a "stub".
4. **RESOLVED** (docstring rewritten to what the code does; the dead branch removed, no behaviour change).
   **`unwrapEditLogic`'s docstring disagrees with its code.** The docstring says the table is also rebuilt when
   the source bodies change and that a name-suffix change renames automatic rows. The code returns at once
   unless the button was pressed, so neither happens (the feature header's "only this button" rule is what the
   code does). Also, `(pressed || previous == undefined)` is always true after that early return, so the
   "keep the material read before" branch is dead code.
5. **RESOLVED** before this pass (the parameter is now "Maximum sample gap" with accurate text, and unwrap.fs
   passes `tolerance : approximationTolerance / 4` to the undrape). **"Outline sample spacing" dialog text is stale.** It says "6 mm holds the outline within 0.005 mm; wider is
   faster". The undrape now sets density by its own tolerance (1.25 um) and uses spacing only as a max-gap cap.
   Also, unwrap.fs passes no `tolerance` option, so in plate mode the user's **Tolerance** parameter does not
   affect undrape sampling, unlike Edges mode (tolerance / 4).
6. **RESOLVED** (confirmed a check artifact: `lengthAndVolume` now takes a plate's extent from the undrape's
   own outline stations, `outlineArcRange`; the baseline reads 1827.693 -> 1827.693).
   **Topsheet +1.12 mm is probably a check artifact, not a wrong flat.** I checked this independently:
   - W (the topsheet's own mid-surface Front section) is 1827.695 mm long.
   - All 428 topsheet vertices have station arcs spanning **1827.696 mm**, even with every other vertex dropped
     (the check strides at 400 vertices).
   - The flat plate's X extent is 1827.693.

   So the *flat* matches, and it is `lengthAndVolume`'s **wrapped** value (1826.574) that is short by 1.12 mm.
   Hypothesis: feet of extreme vertices that do not converge from a cold seed are *silently skipped* (`continue`
   when `!chartFootConverged`), so arcLo / arcHi fall short. Test: print arcLo / arcHi and the number of
   skipped vertices. The memory note attributes this to the U-turn ends. The check, not the undrape, is the
   first suspect.
7. The **question (a) hypothesis** (0.5*h*dtheta^2, 400 mm pieces) disagrees with the exact computation by more
   than an order of magnitude in allowed length (2.7 (a), fig12). Worth a second pair of eyes on my setup.
8. **Straightness by edge type** (`referenceSpans`: `curveType == LINE`). A spline reference that is
   geometrically straight is rebuilt everywhere, and Clean-wire outputs are usually splines. This is not
   documented as a limit in the as-built notes.
9. **NOTED, not determined** (a dated note under research_unwrap_part.md 6, item 2, records both numbers and
   the most likely cause: how the prototype took the extent). **4802's flat length**: research_unwrap_part.md 6 gives 180.62 mm at d = 0, while the live check reports
   180.709 flat. That is 0.09 mm, possibly a different measure (the prototype vs the bbox extent of the
   production result) or the square/exact-wall difference. Worth one line of clarification in the note.
10. **RESOLVED** (not every file is 3070: `std/` and `tools/` are 2878, xSection 2892, fillet_wire 3083; CLAUDE.md
    now says the 2878 target is the std mirror and the active documents are on 3070). **Header version**: the .fs files are `FeatureScript 3070` while CLAUDE.md says the target is 2878.
    CLAUDE.md is likely stale.
11. **The continuity gate** `G1_JUNCTION_ANGLE` (0.57 deg) is looser than the perf note's recommendation
    (1e-3 rad) for Unwrap. Split-and-join fitting (3.3) is also deferred. Both are fine but undocumented
    trade-offs.
12. **RESOLVED** (`devtools/onshape/check_unwrap_regression.py` + `devtools/onshape/fingerprints/unwrap_baseline.json`).
    **The regression harness (`regress.py`, `regress2.py`, baselines) lives only in a session scratchpad.** It
    will disappear with the session. The in-tree test rule suggests `devtools/onshape/check_unwrap.py` plus a
    committed baseline.
13. **RESOLVED** (committed; the line numbers in 3.1 are exact again as of this pass). **undrape_utils.fs has uncommitted edits** today (`UNDRAPE_END_SPAN` -> `UNDRAPE_END_CHECK`, `undrapeMiss`
    replacing `undrapePredict` in `undrapeRefine`). Line numbers above for that file are approximate.
14. The Copy 2 outputs are named "Part 17".."Part 32" because the names button has not been pressed there.
    That is harmless, but it makes the studio harder to read.
