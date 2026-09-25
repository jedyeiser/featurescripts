# Undrape map -- research and design (2026-09-24)

Status: RESEARCH / PROTOTYPE. Nothing pushed. The core section math below was run inside Onshape (FeatureScript
eval API, as local lambdas) against the real test part and checked against kernel sections. Scratch work (python
prototypes, data dumps, FS harness) is in the session scratchpad folder `.../scratchpad/undrape/` (section 11).

Test data: doc f61d2c000ab2d1240776342e / ws 5b11f323ab31b04cba8b36ef / element 80c1e329f99a05e224058526
("Unwrap_Testing Copy 1"): topsheet RnRD (solid, 252 faces, t = 0.4 mm), reference wire Wrapped_profile RjRP
(3 spline edges, XZ plane), mate connector StjLB at (885, 0, 0).

---

## 0. Recommendation in one paragraph

Undrape = at every station s along W, measure the mid-surface section by the plate's own EDGES. Intersect the
station plane with every edge of ONE side of the plate (sampled once: positions, edge tangents and the side
face's normals from `evFaceTangentPlanesAtEdge`). Order the crossings across the section. Take each piece between
consecutive crossings as the circular arc with the two end tangents (tangent = station normal x face normal).
Convert the side's arc to the mid-surface with the turning correction `+ sideSign * (t'/2) * turn`,
t' = t / |in-plane part of the face normal|. Zero the arc at the centreline w = 0. The undraped point is
`x = alongCoordinate(s)` (target = W offset by d), `y = -(signed mid arc)`, `z = offset through the thickness`.
Evaluate a section AT EVERY OUTLINE SAMPLE's own station. Do not interpolate the section field along s: it has a
kink at every face end and square-root singularities where the step runs across the stations. A station where a
crossed edge runs within 18 deg of the station plane (tip/tail U-turns of the step) goes to a kernel section
(`opIntersectFaces`, both sides averaged).
Measured: rim arc within 0.0014 mm of the kernel (223 stations, in FS) and 0.0043 mm (915 stations, python, 10 mm
edge sampling); interior points within 0.011 mm; ~2.3 ms of FS per station as written (target ~1 ms); ~0.6 s of
kernel sampling for the 338 edges of one side.

---

## 1. The map

Chart (existing, edge_offset_utils.fs "The reference surface as a chart"): W planar, plane normal `pn`, arc s,
tangent T(s), surface normal N(s) = pn x T (NOT flipped to +Z), signed curvature kappa with T' = kappa N. Every
point is P = A(s) + w pn + h N(s): s = foot on the swept surface (referenceSurfaceCoords), w = the chart's v,
h = height. The station plane at s is {P : (P - A(s)) . T(s) = 0}; it is normal to W and to every offset of W.

Plate: constant thickness t, sides S+ and S-, mid-surface M = either side offset by t/2 inward.

Map (confirmed with the user). A point P of the plate with foot m on M (P = m + z nM):

    s(P) = chart arc of m (equal to P's own when nM lies in the station plane; see 8.6)
    x    = alongCoordinate(s) - alignCoord           = s - d * theta(s) - ...    (length along the target W+d)
    y    = align.v - Lsigned(s, m)     (Lsigned: mid-surface arc in the station plane from the centreline w = 0
                                        to m, signed with w)
    z    = z (offset from M; +-t/2 on the sides)  [+ the layOnPlane shift as in v1]

The y sign follows unwrapCoords (y = -v keeps the output right-handed). With a flat section Lsigned = w and this
is exactly v1's unwrap of the mid-surface.

Deformation (what to measure and report). M in chart coordinates: M(s, w) = A(s) + w pn + h(s, w) N(s).

    metric of M:     E  = (1 - kappa h)^2 + h_s^2    F  = h_s h_w    G  = 1 + h_w^2
    metric of image: E' = (1 - kappa d)^2 + L_s^2    F' = L_s L_w    G' = L_w^2,  L_w = sqrt(1 + h_w^2) exactly

Lengths across the section are exact by construction. Lengthwise stretch is
`sqrt(E'/E) = sqrt((1 - kappa d)^2 + L_s^2) / sqrt((1 - kappa h)^2 + h_s^2)`. The s- and w-lines, orthogonal on a
flap (F = 0), are sheared in the flat by about atan(L_s). Principal stretches = sqrt(eig(g^-1 g')). A practical
measure in FS: along any tracked curve (rim edge, bend-line edge), compare the 3D mid-surface length element with
the flat one between consecutive stations. Both come free with the sections.

---

## 2. What the test part actually is (measured)

The kernel sectioned the part every 2 mm (913 stations) and on both sides of 53 face-end "kinks" (1018 sections,
top and bottom sides). Figures `secs.png`, `plan.png`, `E.png` are in the scratch folder.

* 252 faces: 124 top-side, 124 bottom-side, 4 rim faces (v = +-75 planes, tip and tail end planes). 678 edges.
  94 of the 124 top faces slope across the section (|normal . pn| > 1e-3). Many are tiny (85 top edges < 1 mm).
* Cross-section: a centre plateau (top h = 0, i.e. ON the reference surface; mid h = -0.2). Then a pressed STEP on
  each side -- fillet (r ~ 1-2 mm), wall 60-75 deg, fillet -- down to a FLAT flap (flat across the section) out to
  the rim at v = +-75. Flap drop (top side): 2.24 .. 7.95 mm. In plan, the step follows the ski's sidecut
  (v 41..65 mm).
* The flap depth changes over ~25 mm at s = 511-536 and 1306-1331 ("transition zones"; the flap slopes lengthwise
  up to ~18 deg there). The tail (s 20-48) and tip (s 1747-1845) flaps sit a constant 2.44 mm (top) below.
* The step wraps round the plateau's ends (U-turns): tail s 40-58 (with a notch at |v| < 20) and tip s 1785-1825,
  plus a centre channel |v| < ~6 from s ~1800 to the tip. There the step runs ACROSS the stations.
* No through-holes: every section is one continuous chain (gaps < 0.3 micron between kernel pieces).
* Mid-surface excess per side E(s) = (mid arc centre->rim) - 75: 0 .. 4.33 mm. Flat width 150.000 .. 158.639 mm.
  |dE/ds| reaches 0.32 (tip U-turn) and 0.17 in the transition zones.
* The TOP side's arc equals the mid arc to 0.003-0.006 mm everywhere except the U-turn zones, where it is off by up
  to 0.118 mm (the plane cuts the plate obliquely: in-plane thickness up to 0.76 mm). One side is enough ONLY with
  the turning correction of section 4, step 6 (then within 0.004 mm).

---

## 3. Costs that decide the design (measured through the eval API on this Part Studio)

Eval-call baseline: 0.11 s. FS arithmetic costs about 0.3 us/op on plain numbers and 0.5 us/op on ValueWithUnits
scalars. One `a - dot(a, t) * t` on 3-Vectors costs 17 us unitless and 28 us with units (!). A function call is
~2 us, and `append` copies the array (O(n) per call). So the per-station math must run on plain numbers, never on
Vectors.

| kernel query | cost |
|---|---|
| evFaceTangentPlanes | ~0.1 ms/call + 7.5 us/point; with `returnUndefinedOutsideFace` ~30 us/point |
| evEdgeTangentLines, default (arc-length) params | 1.7 ms/edge |
| evEdgeTangentLines, `arcLengthParameterization : false` | 0.17 ms/edge |
| evFaceTangentPlanesAtEdge, `arcLengthParameterization : false` | ~1.6 ms/edge at 9 points (~0.18 ms/point); about 2x with arc-length params |
| evLength | ~0.3 ms/edge |
| evPointsDeviation (bulk closest point) | ~2 ms per POINT: useless in bulk |
| opIntersectFaces, station plane x faces | ~2 ms per plane-face pair. Box-filtered sloped faces of both sides: ~25 ms/station, plus ~1 ms per result edge read, so ~37 ms/station |
| opPlane | ~1 ms |

Consequences:
* Kernel sections at every station are exact and robust, but cost 3.7 s per 100 stations. Too slow as the main
  path; fine as a fallback for a handful of stations.
* The first idea, uv-grid tables h(s, w) from evFaceTangentPlanes, fails on trimming. 86 of the 94 sloped top
  faces are trimmed: their uv-box grids run up to 7 mm off the face. The walls are larger spline surfaces trimmed
  by their fillets, and their grid points reach 4 mm ABOVE the plateau. `returnUndefinedOutsideFace` fixes that at
  30 us/point, but then junctions are known only to grid resolution. On top of that, the chart coordinates of every
  sample cost a Newton each (~100 us), and the per-station column search costs ~5 ms/station. Rejected.
* Interpolating the section field along s from a coarse station set also fails. E(s) has a slope kink at every
  face end and sqrt-type singularities at the U-turns. With kink stations and cubic Hermite, the error outside the
  U-turns is 0.004 mm at 6 mm spacing, 0.011 at 10 mm and 0.036 at 20 mm. Without kink stations it is 0.25-1.1 mm
  at any spacing, and in the U-turns it is 0.06 mm even at 4 mm. So: no interpolation along s. Each outline sample
  gets a section at its own station.
* The EDGES carry the exact junctions (bend lines) of the section, and normals along them give the section's
  tangents there. Between two junctions a face's section is a fillet, a flat or a wall. The circle through the two
  end points with the two end tangents reproduces it. Per piece, this is within 1 micron of the kernel's evLength
  everywhere except the U-turns (up to 0.15 mm on 17 mm pieces cut lengthwise). This is the recommended path.

---

## 4. Algorithm (step by step)

Inputs: the part, the chart (unwrapChart, delta = d), the thickness t and the two side face sets (plateSides, v1),
and the outline.

1. **Side choice.** Use the side with fewer edges (either works). sideSign = +1 when the side's outward normal has
   a positive component along N (the chart normal, unflipped), else -1.
2. **Edge sampling (kernel, once).** Sample every edge of that side's faces (edges between two rim faces
   excluded). onRim = the edge also bounds a non-side face. Call `evFaceTangentPlanesAtEdge(edge, sideFace,
   range(0, 1, n), arcLengthParameterization : false)` with n = max(3, ceil(L / 10 mm) + 1). The 10 mm spacing is
   required: 15 mm gives 0.015 mm and 25 mm gives 0.030 mm in the transition zones, where the wall angle changes
   fast along the edge. Take L from `evLength`, or more cheaply from a 3-point
   `evEdgeTangentLines(..., arcLengthParameterization : false)`. Crease edges (not tangent-continuous between two
   side faces) need BOTH faces' normals: two calls (section 8.4).
3. **Edge tables (FS, once).** undrapeEdgeTable strips everything to plain metres. Each span's Hermite tangent
   length is m_i = chord_i (1 + phi_i^2 / 24), where phi_i = the angle between neighbouring edge tangents. This is
   the circle-arc length, so non-arc-length sample spacing is harmless. The table also holds the box centre and
   half-extents.
4. **Stations.** One per outline sample, sorted by s. Rim edges running along the part are sampled AT stations
   (both long sides share them). Edges running across (tip/tail ends) are sampled normally, and each sample's
   station is its own chart arc (referenceSurfaceCoords). Add a station just outside each face-end kink (the chart
   arcs of the end vertices of edges bounding sloped faces) so the output curve fit sees the slope breaks. Each
   station's frame (undrapeStationFrame) holds a, t, w (= pn) and h (= N) as plain numbers.
5. **Candidates.** undrapeCandidateEdges uses two binary searches per edge. A fixed point's plane distance falls
   monotonically with s wherever the chart is valid, so each edge box is crossed by one contiguous run of stations.
   Measured: 9.7 candidate edges per station, out of 338.
6. **Section per station.** For each candidate edge, undrapeEdgeCrossings:
   - bisects on the samples' plane distances (monotone for edges running through the plane; a full scan otherwise);
   - runs Newton on the Hermite span to |g| < 1e-12 m;
   - interpolates the normal linearly and renormalises it;
   - takes section tangent = t x n, projected on (w, h) and oriented tw >= 0;
   - records inPlane = |n projected on (w, h)| and cosCross = |edge tangent . t|.

   undrapeSection then:
   - refuses the station (-> step 7) if any non-rim crossing has cosCross < 0.95 (edge within 18 deg of the plane)
     or no tangent;
   - drops rim crossings with cosCross < 0.95: the part's end lying in the plane is a boundary, not a crossing;
   - sorts by w and merges coincident crossings (vertices);
   - measures each piece:

         chord c, turn phi = atan2(ta x tb, ta . tb)
         side arc = c (phi/2) / sin(phi/2)
         mid arc  = side arc + sideSign * (t / (inPlane_a + inPlane_b)) * phi

   The arcs are summed, then the mid arc at w = 0 is subtracted. That point comes from undrapePieceToWidth on the
   piece containing w = 0: the circle a + R (sin psi ta + (1 - cos psi) na), R = c / (2 sin(phi/2)), Newton on psi.
   The result is the mid arc at each crossing. undrapeMidArcAtWidth gives a point at any width w0 on the section
   (same circle, partial turn psi).
7. **Fallback (refused stations).** Build a kernel section: opPlane at the station, then opIntersectFaces with the
   box-filtered faces of BOTH sides. Take evLength and the end tangents of the result edges, chain them, and average
   the two sides' arcs. This is exact to ~1e-5 mm: the sides are offsets of M, so their arcs differ by
   +-(t'/2) * turn. Wrap it in `try`. A plane exactly through a vertex fails with "Failed to completely disambiguate
   created topology" (seen at 1 of 1071 stations, even after a 0.01 mm nudge); retry at s +- 0.05 mm. Cost:
   ~60-70 ms per station (two sides).
8. **Map.** x = alongCoordinate(alongRef, arc) - alignCoord, y = align.v - Lsigned, z as in v1. Outline samples at
   the rim map from their crossings directly: a rim crossing IS the outline sample.
9. **Deformation report.** Between consecutive stations, for the rim and for each bend-line edge (crossings tracked
   by edge index), compare the 3D mid length element sqrt(((1 - kappa h) ds)^2 + dh^2) with the flat one
   sqrt(((1 - kappa d) ds)^2 + dL^2). Report the max/min stretch and where, plus the rim totals. The flat width per
   station is mid(+rim) - mid(-rim).

---

## 5. Data structures

* Edge table (map, plain numbers, metres): `px py pz` (samples), `ux uy uz` (unit edge tangents), `nx ny nz`
  (unit side-face normals), `mags` (Hermite tangent lengths per span), `centre` and `half` (box), `lo`, `hi`,
  `hasNormals`, `onRim`. In production, build the arrays with makeArray + index assignment (append is O(n)).
* Station frame (map): `arc` (ValueWithUnits), `a t w h` (3-arrays of numbers).
* Crossing (map): `w h tw th inPlane cosCross rim hasTangent` (numbers/booleans). For speed, production code can
  hold crossings as parallel arrays.
* Section (map): `ok why ws hs mid rims points thickness sideSign`.

---

## 6. Accuracy and speed (prototype numbers)

Truth = kernel sections: opIntersectFaces on both sides, 9 samples + evLength per piece, mid = the mean of the two
sides' arcs from the centreline.

| check | result |
|---|---|
| per-piece circle model vs kernel evLength (18,420 pieces) | < 0.001 mm, except 718 pieces, all in the tail/tip U-turns (max 0.154 mm) |
| rim mid arc, python, 10 mm edge samples, 915 unrefused stations | max 0.0043 mm (99th pct 0.0012) |
| same, 5 mm edge samples | 0.0036 mm; 15 mm: 0.015; 25 mm: 0.030 |
| top-only arc WITHOUT the turning correction | up to 0.006 mm (transitions), 0.24 mm (U-turns) |
| FS harness (this code, run in Onshape), 229 stations every 8 mm | 221 evaluated, 8 refused (s = 44.5, 52.5, 60.5, 1788.5-1820.5); rim max 0.0014 mm, mean 0.00007 |
| FS interior points (w = -40, -47, -55, 47, 65 mm at each station, 1115 points) | max 0.0109 mm at s = 1780.5 (crossing cos 0.947), so the threshold was raised to 0.95; next worst 0.004 |
| undraped width = section arc | holds by construction (width = mid(+rim) - mid(-rim)); matches the kernel to the numbers above |

Refusal threshold vs error (python, 1018 stations): cosCross >= 0.9 -> max 0.0036 mm (103 refused, most within
0.01 mm of a face end); >= 0.8 -> 0.0100; >= 0.5 -> 0.024; >= 0.3 -> 0.178.

Speed (FS harness, one side = 338 edges):
* Edge sampling + tables: 0.80 s = adjacency queries 0.09 + evLength 0.10 + evFaceTangentPlanesAtEdge 0.42 + table
  building 0.18. The table building is append-bound; makeArray should roughly halve it. Dropping evLength saves 0.1 s.
* Stations: 2.3 ms each as written, with 9.7 candidate edges each. Before the candidate index and the bisection it
  was 15 ms. That is an estimated 5-7k FS ops per station. Leaner code (parallel arrays, no concatenateArrays, no
  map per crossing) should reach ~1 ms.
* Fallback: ~65 ms per refused station.

Budget for this part (outline: 2 long rim sides of 1825 mm + ends):

| item | 5 mm rim spacing (~365 stations) | 8 mm + kink stations (~290) |
|---|---|---|
| side classification (v1 plateSides, 252 faces) | 0.15 s | 0.15 s |
| edge sampling + tables | 0.6 s | 0.5 s (no evLength, makeArray) |
| stations at 2.3 / ~1 ms | 0.84 / 0.37 s | 0.67 / 0.29 s |
| refused stations (U-turns: ~12 / ~8) | 0.8 s | 0.5 s |
| total | 2.4 / 1.9 s | 1.8 / 1.4 s |

The refused-station fallback is the biggest lever left; see open decision 9.3.

Station spacing along the rim is set by the output curve fit, not by the section math. A cubic through samples of
E(s) stays within 0.004 mm at 6 mm spacing with kink stations, and within 0.011 mm at 10 mm.

---

## 7. Deformation of the section-wise map on the test part (d = -0.2 mm, the plateau's mid-height)

From the kernel truth sections (2 mm stations; values within 2 mm of a face-end kink excluded):

| zone | lengthwise strain | shear (s-/w-line angle change) | max principal strain | area strain |
|---|---|---|---|---|
| plateau | 0 (isometric: mid at h = d) | 0 | 0 | 0 |
| regular flaps / walls | walls: -4.8 % (s 1780); flaps: kappa (h - d) = -1.04 % at s 1762 (h = -2.63, R ~ 235) | 6.7 deg (s 62) | 7.1 % | -3.1 % |
| transition zones s 505-542, 1300-1337 | -1.0 % | 9.2 deg (s 1328) | 8.5 % | -1.6 % |
| tail U-turn s < 60 | -8.6 % | 10.1 deg | 11.6 % | -3.9 % |
| tip U-turn s > 1785 | -13.7 % (s 1818) | 12.5 deg | 15.2 % | -12.1 % |

* The lengthwise flap figure matches the expected kappa (h - d): 2.4 mm below at R 400 gives 0.6 %; here 2.63 mm
  at R ~ 235 gives 1.04 %.
* Rim (v = +75, mid-surface): 3D length 1826.993 mm, flat 1825.152 mm (-1.842 mm, -0.10 %). Local stretch runs from
  -1.03 % (s 1762) to +6.9 % (s 1822, U-turn).
* Centreline: 3D 1826.043 mm, flat 1824.169 mm (-1.874 mm). The tip and tail centre lies 2.44 mm below d.
* Transition zones: the flap is flat, but the step's arc grows by ~3 mm over 25 mm of length. So the flat rim jogs
  outward by 3 mm (L_s up to 0.17), and the flap is SHEARED ~9 deg in the flat pattern. That is what "unroll each
  section" means; it is real, not numerical.
* U-turns: where the step runs across the stations, the section cuts the wall lengthwise. The map is least
  defensible there (15 % principal strain). See open decision 9.1.

---

## 8. Edge cases

1. **Slots / notches / holes.** A hole in a section shows as two interior rim crossings. No special case is needed:
   the piece between them is the circle with the gap's two end tangents, i.e. the surface continued smoothly (G1)
   through the gap. In flat regions that is the chord. Report the bridged length per station. A notch open to the
   rim simply ends the section earlier (its edge is the last rim crossing). The test part has no holes.
2. **Centreline.** If w = 0 falls inside a piece, use the partial circle (undrapePieceToWidth); inside a gap, the
   bridge. A part that does not reach w = 0 (e.g. a side panel) is measured from its innermost crossing, which keeps
   its own w (open 9.4). For an unsymmetric section with a sloped centre, the mid-surface's centre point is not on
   the side's w = 0 normal. The error is of order (t'/2) * slope * (1 - cos), negligible for symmetric parts; flag
   it if |th| > 0.1 there.
3. **Overhangs** (wall past vertical) break the ordering by w. Detect tw < ~0.05 at any crossing and refuse (kernel
   fallback), or order by face adjacency instead of w.
4. **Crease edges** between two side faces (not G1) have one normal per face. Store both and use the left face's
   tangent for the piece to the left. With usingFaceOrientation, the face lies on the +w side of the crossing when
   (n x edge tangent) . section tangent > 0. The test part has none (all side-side edges are smooth; the errors
   confirm it).
5. **Stations through vertices / along short end edges.** A face-end edge (short, running across) crossed by a
   station is oblique, so the station is refused. Place kink stations just OUTSIDE the s-extent of such edges; the
   section is continuous there. Kernel sections exactly through a vertex fail (see step 7).
6. **Foot on M vs foot of P.** An outline sample on the rim wall at mid-thickness takes the station of the side
   crossing it came from. This assumes the rim wall is normal to the plate, as v1's slab walls are. If it is not
   (bevelled trim), sample both sides' rim edges and map each by its own foot.
7. **Chart validity.** Requires 1 - kappa h > 0 for every part point (h > -R). True here (R >= 230, |h| < 8).
8. **Target from a face instead of W** ("wire = user face intersected with the mid-surface"). Do one
   opIntersectFaces of the face with the chosen side (box-filtered), then use the result as W with
   d = -sideSign * t/2. This is exact where the face is the plate's symmetry plane, i.e. where the side's normal lies
   in the face. Everything else is unchanged.
9. **d.** Only x depends on d (the station planes are normal to every offset of W). d = plateau mid-height makes the
   centre strip isometric; any other d adds kappa (h - d) lengthwise strain everywhere.

---

## 9. Open decisions

1. **U-turn zones** (tail s 40-60, tip s 1785-1825 here): keep the literal section-wise unroll (15 % principal
   strain, a rim bulge of ~0.8 mm at the tip), or blend to another rule near the ends (e.g. sections normal to the
   step's own plan direction, or a least-distortion fill between the regular zone and the part's end).
2. **Transition-zone shear** (~9 deg on the flap): accept it (pure bending across, as defined) or spread it.
3. **Refused stations**: the kernel fallback is exact but ~65 ms each. A cheaper, untested option: subdivide oblique
   pieces with interior points from `evFaceTangentPlanes(..., returnUndefinedOutsideFace : true)` grids of the faces
   involved. Or refuse with a clear notice when the outline has no samples there.
4. **Parts not reaching the centreline**: measure from the innermost point keeping its w (proposed), or refuse.
5. **Gap bridging**: G1 circle (proposed; it is the piece formula) or straight chord.
6. **Which side** to sample: the one with fewer edges (proposed), or always the inner/outer.
7. **Rim spacing**: 5 mm (v1 default, ~2.4 s here as written) vs 8 mm + kink stations (~1.4-1.8 s).
8. **Deformation report**: rim + bend lines (proposed), or a full (s, w) grid of principal strains as a debug view.

---

## 10. Recommended functions for edge_offset_utils.fs

Pure math on plain numbers, except undrapeStationFrame (which reads the chart). Validated in Onshape as local
lambdas (harness in section 11); `fscheck.py` is clean together with edge_offset_utils.fs. Still to write: the
kernel fallback (`undrapeKernelSection(context, id, sideFaces, frame, thickness)`), the edge sampler
(`undrapeSideEdges(context, sideFaces) returns array`, step 2), and the deformation tally.

```featurescript
// ============================================================================
// Undrape: sections of a draped plate by edge crossings (pure math, unitless metres)
// ============================================================================

/** Crossings closer than this (metres) along a section are one point (a vertex met by two edges). */
const UNDRAPE_SAME_POINT = 1e-9;

/**
 * A section is refused (left to the kernel) where a crossed edge runs within acos(this) = 18 degrees of the
 * section plane: there the plane cuts faces lengthwise and one circular arc per face no longer fits. Measured on
 * the test topsheet: 0.9 keeps rim arcs within 0.004 mm but one interior point at 0.011 mm; 0.95 keeps both.
 */
const UNDRAPE_MIN_CROSSING = 0.95;

/** Below this turning (radians) a piece is a straight chord. */
const UNDRAPE_STRAIGHT = 1e-9;

/**
 * One edge of a plate side, sampled for plane-crossing searches: positions, unit edge tangents and the side
 * face's unit outward normals, all stripped to plain numbers (metres) so the per-station search does no unit
 * arithmetic. `planes` is what evFaceTangentPlanesAtEdge returns (origin, x = edge tangent, normal).
 *
 * The samples need not be spaced by arc length: each span's Hermite tangent length is its chord corrected for
 * the turn between its end tangents, chord * (1 + phi^2 / 24), which is the arc of the circle through both ends
 * with those tangents -- exact for a circular span, fourth order otherwise.
 *
 * @param hasNormals {boolean} : false when `planes` came from evEdgeTangentLines (a Line: origin, direction);
 *      a crossing of such an edge carries no section tangent and marks the station for the kernel.
 * @param onRim {boolean} : the edge borders the plate's rim (a section may end here).
 */
export function undrapeEdgeTable(planes is array, hasNormals is boolean, onRim is boolean) returns map
{
    var px = [];
    var py = [];
    var pz = [];
    var ux = [];
    var uy = [];
    var uz = [];
    var nx = [];
    var ny = [];
    var nz = [];
    for (var pl in planes)
    {
        const o = pl.origin / meter;
        const u = hasNormals ? pl.x : pl.direction;
        px = append(px, o[0]);
        py = append(py, o[1]);
        pz = append(pz, o[2]);
        ux = append(ux, u[0]);
        uy = append(uy, u[1]);
        uz = append(uz, u[2]);
        if (hasNormals)
        {
            nx = append(nx, pl.normal[0]);
            ny = append(ny, pl.normal[1]);
            nz = append(nz, pl.normal[2]);
        }
    }

    var mags = [];
    var lo = [px[0], py[0], pz[0]];
    var hi = [px[0], py[0], pz[0]];
    for (var i = 0; i < size(px); i += 1)
    {
        lo = [min(lo[0], px[i]), min(lo[1], py[i]), min(lo[2], pz[i])];
        hi = [max(hi[0], px[i]), max(hi[1], py[i]), max(hi[2], pz[i])];
        if (i + 1 < size(px))
        {
            const dx = px[i + 1] - px[i];
            const dy = py[i + 1] - py[i];
            const dz = pz[i + 1] - pz[i];
            const chord = sqrt(dx * dx + dy * dy + dz * dz);
            const c = clamp(ux[i] * ux[i + 1] + uy[i] * uy[i + 1] + uz[i] * uz[i + 1], -1, 1);
            const phi = acos(c) / radian;
            mags = append(mags, chord * (1 + phi * phi / 24));
        }
    }

    return {
        "centre" : [0.5 * (lo[0] + hi[0]), 0.5 * (lo[1] + hi[1]), 0.5 * (lo[2] + hi[2])],
        "half" : [0.5 * (hi[0] - lo[0]), 0.5 * (hi[1] - lo[1]), 0.5 * (hi[2] - lo[2])],
        "px" : px, "py" : py, "pz" : pz,
        "ux" : ux, "uy" : uy, "uz" : uz,
        "nx" : nx, "ny" : ny, "nz" : nz,
        "mags" : mags,
        "lo" : lo, "hi" : hi,
        "hasNormals" : hasNormals,
        "onRim" : onRim
    };
}

/**
 * A station's section plane as plain numbers: origin a (metres), plane normal t (the reference tangent),
 * width axis w (the reference plane's normal, the chart's v) and height axis h (the chart's surface normal,
 * cross(w, t), NOT flipped towards +Z). Built from the chart once per station.
 */
export function undrapeStationFrame(alongRef is map, arc is ValueWithUnits) returns map
{
    const basis = referenceBasisAtArc(alongRef, arc);
    const a = referencePointAtArc(alongRef, arc) / meter;
    const w = alongRef.planeNormal;

    return {
        "arc" : arc,
        "a" : [a[0], a[1], a[2]],
        "t" : [basis.tangent[0], basis.tangent[1], basis.tangent[2]],
        "w" : [w[0], w[1], w[2]],
        "h" : [basis.normal[0], basis.normal[1], basis.normal[2]]
    };
}

/**
 * Signed distance of an edge table's box centre from a station plane, and how far its corners reach from it.
 */
export function undrapeBoxDistance(edge is map, frame is map) returns array
{
    const t = frame.t;
    const d = (edge.centre[0] - frame.a[0]) * t[0] + (edge.centre[1] - frame.a[1]) * t[1] + (edge.centre[2] - frame.a[2]) * t[2];
    const reach = edge.half[0] * abs(t[0]) + edge.half[1] * abs(t[1]) + edge.half[2] * abs(t[2]);
    return [d, reach];
}

/** Signed distance of sample i of an edge table from a station plane. */
export function undrapePlaneDistance(edge is map, i is number, frame is map) returns number
{
    return (edge.px[i] - frame.a[0]) * frame.t[0] + (edge.py[i] - frame.a[1]) * frame.t[1] + (edge.pz[i] - frame.a[2]) * frame.t[2];
}

/** Whether a station plane can cross an edge table's box. */
export function undrapeBoxStraddles(edge is map, frame is map) returns boolean
{
    const dr = undrapeBoxDistance(edge, frame);
    return abs(dr[0]) <= dr[1];
}

/**
 * For each station (frames sorted along the reference), the indices of the edge tables whose boxes its plane
 * crosses. A fixed point's distance to the station plane falls monotonically along the reference wherever the
 * chart is valid (1 - kappa * height > 0), so each box is crossed by one contiguous run of stations, found by
 * two binary searches: O(edges * log(stations)) instead of testing every edge at every station.
 */
export function undrapeCandidateEdges(tables is array, frames is array) returns array
{
    const count = size(frames);
    var lists = makeArray(count, []);
    for (var e = 0; e < size(tables); e += 1)
    {
        // first station whose plane has the box's far corner at or behind it: d - reach <= 0
        var low = 0;
        var high = count;
        while (low < high)
        {
            const mid = floor((low + high) / 2);
            const dr = undrapeBoxDistance(tables[e], frames[mid]);
            if (dr[0] - dr[1] <= 0)
            {
                high = mid;
            }
            else
            {
                low = mid + 1;
            }
        }
        const first = low;
        // first station whose plane has passed the box's near corner: d + reach < 0
        high = count;
        while (low < high)
        {
            const mid = floor((low + high) / 2);
            const dr = undrapeBoxDistance(tables[e], frames[mid]);
            if (dr[0] + dr[1] < 0)
            {
                high = mid;
            }
            else
            {
                low = mid + 1;
            }
        }
        for (var i = first; i < low; i += 1)
        {
            lists[i] = append(lists[i], e);
        }
    }
    return lists;
}

/**
 * Every point where one sampled edge crosses a station plane.
 *
 * Signed distances to the plane bracket each crossing between two samples; Newton on the Hermite span then
 * puts it on the plane to 1e-12 m. The side face's normal is interpolated linearly and renormalised, and the
 * section's tangent there is t x normal: the direction in the station plane that stays on the face.
 *
 * @returns {array} : maps { "w", "h" (section coordinates, metres), "tw", "th" (unit section tangent, tw >= 0),
 *      "inPlane" (length of the normal's in-plane part: 1 / the obliquity of the plate), "cosCross" (|edge
 *      tangent . t|: 1 when the edge runs straight through the plane), "rim", "hasTangent" }
 */
export function undrapeEdgeCrossings(edge is map, frame is map) returns array
{
    const a = frame.a;
    const t = frame.t;

    // Box first: the box straddles the plane when its centre is no farther from it than its half-extents reach.
    if (!undrapeBoxStraddles(edge, frame))
    {
        return [];
    }

    // Spans to solve in: one found by bisection when the edge's ends lie on opposite sides (an edge running
    // through the plane), every sign change by a scan otherwise (an edge that turns back, or lies along it).
    const count = size(edge.px);
    var spans = [];
    const gFirst = undrapePlaneDistance(edge, 0, frame);
    const gLast = undrapePlaneDistance(edge, count - 1, frame);
    if ((gFirst > 0) != (gLast > 0))
    {
        var low = 0;
        var high = count - 1;
        var gLow = gFirst;
        var gHigh = gLast;
        while (high - low > 1)
        {
            const mid = floor((low + high) / 2);
            const gMid = undrapePlaneDistance(edge, mid, frame);
            if ((gMid > 0) == (gLow > 0))
            {
                low = mid;
                gLow = gMid;
            }
            else
            {
                high = mid;
                gHigh = gMid;
            }
        }
        spans = [[low, gLow, gHigh]];
    }
    else
    {
        var gPrev = gFirst;
        for (var i = 1; i < count; i += 1)
        {
            const gi = undrapePlaneDistance(edge, i, frame);
            if ((gPrev > 0) != (gi > 0))
            {
                spans = append(spans, [i - 1, gPrev, gi]);
            }
            gPrev = gi;
        }
    }

    var result = [];
    for (var span in spans)
    {
        const i = span[0];
        // Hermite span i: P(f) = h00 P0 + h10 m T0 + h01 P1 + h11 m T1.
        const m = edge.mags[i];
        const g0 = span[1];
        const g1 = span[2];
        const d0 = m * (edge.ux[i] * t[0] + edge.uy[i] * t[1] + edge.uz[i] * t[2]);
        const d1 = m * (edge.ux[i + 1] * t[0] + edge.uy[i + 1] * t[1] + edge.uz[i + 1] * t[2]);
        var f = g0 / (g0 - g1);
        for (var step = 0; step < 8; step += 1)
        {
            const f2 = f * f;
            const f3 = f2 * f;
            const gf = (2 * f3 - 3 * f2 + 1) * g0 + (f3 - 2 * f2 + f) * d0 + (-2 * f3 + 3 * f2) * g1 + (f3 - f2) * d1;
            const df = (6 * f2 - 6 * f) * g0 + (3 * f2 - 4 * f + 1) * d0 + (-6 * f2 + 6 * f) * g1 + (3 * f2 - 2 * f) * d1;
            if (abs(df) < 1e-15)
            {
                break;
            }
            f = clamp(f - gf / df, 0, 1);
            if (abs(gf) < 1e-12)
            {
                break;
            }
        }

        const f2 = f * f;
        const f3 = f2 * f;
        const b0 = 2 * f3 - 3 * f2 + 1;
        const b1 = (f3 - 2 * f2 + f) * m;
        const b2 = -2 * f3 + 3 * f2;
        const b3 = (f3 - f2) * m;
        const qx = b0 * edge.px[i] + b1 * edge.ux[i] + b2 * edge.px[i + 1] + b3 * edge.ux[i + 1] - a[0];
        const qy = b0 * edge.py[i] + b1 * edge.uy[i] + b2 * edge.py[i + 1] + b3 * edge.uy[i + 1] - a[1];
        const qz = b0 * edge.pz[i] + b1 * edge.uz[i] + b2 * edge.pz[i + 1] + b3 * edge.uz[i + 1] - a[2];

        var crossing = {
            "w" : qx * frame.w[0] + qy * frame.w[1] + qz * frame.w[2],
            "h" : qx * frame.h[0] + qy * frame.h[1] + qz * frame.h[2],
            "cosCross" : abs((1 - f) * (edge.ux[i] * t[0] + edge.uy[i] * t[1] + edge.uz[i] * t[2])
                    + f * (edge.ux[i + 1] * t[0] + edge.uy[i + 1] * t[1] + edge.uz[i + 1] * t[2])),
            "rim" : edge.onRim,
            "hasTangent" : edge.hasNormals,
            "tw" : 1, "th" : 0, "inPlane" : 1
        };

        if (edge.hasNormals)
        {
            var nx = (1 - f) * edge.nx[i] + f * edge.nx[i + 1];
            var ny = (1 - f) * edge.ny[i] + f * edge.ny[i + 1];
            var nz = (1 - f) * edge.nz[i] + f * edge.nz[i + 1];
            const nn = sqrt(nx * nx + ny * ny + nz * nz);
            nx = nx / nn;
            ny = ny / nn;
            nz = nz / nn;
            // section tangent = t x n
            const sx = t[1] * nz - t[2] * ny;
            const sy = t[2] * nx - t[0] * nz;
            const sz = t[0] * ny - t[1] * nx;
            var tw = sx * frame.w[0] + sy * frame.w[1] + sz * frame.w[2];
            var th = sx * frame.h[0] + sy * frame.h[1] + sz * frame.h[2];
            const tn = sqrt(tw * tw + th * th);
            tw = tw / tn;
            th = th / tn;
            if (tw < 0)
            {
                tw = -tw;
                th = -th;
            }
            const nw = nx * frame.w[0] + ny * frame.w[1] + nz * frame.w[2];
            const nh = nx * frame.h[0] + ny * frame.h[1] + nz * frame.h[2];
            crossing.tw = tw;
            crossing.th = th;
            crossing.inPlane = sqrt(nw * nw + nh * nh);
        }
        result = append(result, crossing);
    }
    return result;
}

/**
 * Length of the section between two consecutive crossings, taken as the circular arc with their tangents:
 * chord * (phi / 2) / sin(phi / 2), phi the signed turn from a's tangent to b's (counter-clockwise in (w, h)).
 * Exact for a fillet or a flat, within 1 micron of the kernel's section on the test topsheet wherever the
 * crossed edges run through the station plane at less than 26 degrees from its normal.
 *
 * @returns {map} : { "length", "turn" (radians), "chord" }
 */
export function undrapePiece(a is map, b is map) returns map
{
    const dw = b.w - a.w;
    const dh = b.h - a.h;
    const chord = sqrt(dw * dw + dh * dh);
    const phi = atan2(a.tw * b.th - a.th * b.tw, a.tw * b.tw + a.th * b.th) / radian;
    const half = 0.5 * abs(phi);
    const factor = (half < UNDRAPE_STRAIGHT) ? 1 : half / sin(half * radian);

    return { "length" : chord * factor, "turn" : phi, "chord" : chord };
}

/**
 * Where on the circular piece a -> b the section crosses width w0, as the arc from a and the turn from a.
 * The arc is a + R (sin(psi) ta + (1 - cos(psi)) na), na = ta turned a quarter counter-clockwise,
 * R = chord / (2 sin(phi / 2)); psi is found by Newton from the chord's proportion.
 */
export function undrapePieceToWidth(a is map, b is map, w0 is number) returns map
{
    const piece = undrapePiece(a, b);
    const span = b.w - a.w;
    var share = (abs(span) < 1e-15) ? 0 : (w0 - a.w) / span;
    if (abs(piece.turn) < UNDRAPE_STRAIGHT)
    {
        return { "length" : share * piece.length, "turn" : 0 };
    }

    const radius = piece.chord / (2 * sin(0.5 * piece.turn * radian));
    var psi = share * piece.turn;
    for (var step = 0; step < 12; step += 1)
    {
        const s = sin(psi * radian);
        const c = cos(psi * radian);
        const pw = a.w + radius * (s * a.tw - (1 - c) * a.th);
        const dpw = radius * (c * a.tw - s * a.th);
        if (abs(dpw) < 1e-15)
        {
            break;
        }
        const next = psi - (pw - w0) / dpw;
        psi = (piece.turn > 0) ? clamp(next, 0, piece.turn) : clamp(next, piece.turn, 0);
        if (abs(pw - w0) < 1e-13)
        {
            break;
        }
    }
    return { "length" : abs(radius * psi), "turn" : psi };
}

/**
 * The section of one plate side at one station, unrolled onto the MID-surface.
 *
 * Crossings are ordered by width (no overhangs: every wall steeper than vertical is refused), vertices met by
 * two edges are merged, and the pieces between them measured as circular arcs. The mid-surface is the side
 * offset inward by half the in-plane thickness, thickness / inPlane, so each piece's mid length is its side
 * length plus sideSign * (thickness' / 2) * turn: a convex bend of the outer side is longer than the mid-surface.
 *
 * Gaps (a slot or notch through the plate) need no special case: the piece between two rim crossings is the
 * circular bridge with the gap's end tangents -- the surface continued smoothly across the gap.
 *
 * @param crossings {array} : undrapeEdgeCrossings results for every edge of the side.
 * @param thickness {number} : plate thickness, metres.
 * @param sideSign {number} : +1 when the sampled side's outward normal points along frame.h, else -1.
 * @returns {map} : { "ok" (false: leave the station to the kernel), "why", "ws", "hs" (crossings),
 *      "mid" (signed mid-surface arc from the centreline w = 0 at each crossing, metres), "rims" (indices of
 *      crossings on rim edges) }
 */
export function undrapeSection(crossings is array, thickness is number, sideSign is number) returns map
{
    if (size(crossings) < 2)
    {
        return { "ok" : false, "why" : "fewer than two crossings" };
    }

    // Insertion sort by width: a dozen or two crossings.
    var sorted = [];
    for (var c in crossings)
    {
        // A rim edge lying along the station plane is the end of the plate there (the tail or tip edge), a
        // boundary of the section rather than a crossing of it: the section is measured up to it by width.
        if (c.rim && c.cosCross < UNDRAPE_MIN_CROSSING)
        {
            continue;
        }
        if (!c.hasTangent || c.cosCross < UNDRAPE_MIN_CROSSING)
        {
            return { "ok" : false, "why" : "an edge runs obliquely through the station plane" };
        }
        var j = size(sorted);
        sorted = append(sorted, c);
        while (j > 0 && sorted[j - 1].w > c.w)
        {
            sorted[j] = sorted[j - 1];
            j -= 1;
        }
        sorted[j] = c;
    }

    var pts = [sorted[0]];
    for (var i = 1; i < size(sorted); i += 1)
    {
        const last = pts[size(pts) - 1];
        const dw = sorted[i].w - last.w;
        const dh = sorted[i].h - last.h;
        if (dw * dw + dh * dh > UNDRAPE_SAME_POINT * UNDRAPE_SAME_POINT)
        {
            pts = append(pts, sorted[i]);
        }
        else if (sorted[i].rim)
        {
            pts[size(pts) - 1] = sorted[i];
        }
    }

    // Cumulative mid-surface arc from the first crossing.
    const n = size(pts);
    var mid = [0];
    var zeroIndex = -1;
    for (var i = 0; i + 1 < n; i += 1)
    {
        const piece = undrapePiece(pts[i], pts[i + 1]);
        const halfT = 0.5 * thickness * 2 / (pts[i].inPlane + pts[i + 1].inPlane);
        mid = append(mid, mid[i] + piece.length + sideSign * halfT * piece.turn);
        if (pts[i].w <= 0 && pts[i + 1].w > 0)
        {
            zeroIndex = i;
        }
    }

    // Arc at the centreline. A plate that does not reach w = 0 is measured from its innermost crossing, which
    // keeps its own width (an open decision; see the design note).
    var zero = 0;
    if (zeroIndex >= 0)
    {
        const a = pts[zeroIndex];
        const b = pts[zeroIndex + 1];
        const part = undrapePieceToWidth(a, b, 0);
        const halfT = 0.5 * thickness * 2 / (a.inPlane + b.inPlane);
        zero = mid[zeroIndex] + part.length + sideSign * halfT * part.turn;
    }
    else if (pts[0].w > 0)
    {
        zero = -pts[0].w;
    }
    else
    {
        zero = mid[n - 1] - pts[n - 1].w;
    }

    var ws = [];
    var hs = [];
    var rims = [];
    for (var i = 0; i < n; i += 1)
    {
        mid[i] = mid[i] - zero;
        ws = append(ws, pts[i].w);
        hs = append(hs, pts[i].h);
        if (pts[i].rim)
        {
            rims = append(rims, i);
        }
    }

    return { "ok" : true, "ws" : ws, "hs" : hs, "mid" : mid, "rims" : rims, "points" : pts,
            "thickness" : thickness, "sideSign" : sideSign };
}

/**
 * Signed mid-surface arc from the centreline to the section point of the sampled side at width w0 (metres),
 * i.e. the undraped transverse coordinate of that point and of everything on its normal through the plate.
 * Between crossings the section is the circular piece, so this is exact wherever undrapeSection is.
 */
export function undrapeMidArcAtWidth(section is map, w0 is number) returns number
{
    const pts = section.points;
    const n = size(pts);
    if (w0 <= pts[0].w)
    {
        return section.mid[0] - (pts[0].w - w0);
    }
    if (w0 >= pts[n - 1].w)
    {
        return section.mid[n - 1] + (w0 - pts[n - 1].w);
    }
    var i = 0;
    while (i + 2 < n && pts[i + 1].w < w0)
    {
        i += 1;
    }
    const part = undrapePieceToWidth(pts[i], pts[i + 1], w0);
    const halfT = section.thickness / (pts[i].inPlane + pts[i + 1].inPlane);
    return section.mid[i] + part.length + section.sideSign * halfT * part.turn;
}
```

---

## 11. Reproduction (scratch folder)

* `chart.py`: numpy chart of Wrapped_profile (801 samples/edge, circle-corrected arc, Newton foot). Also `data.py`,
  `faces.txt` (9x9 grids of all 252 faces) and `edges5.txt` (evFaceTangentPlanesAtEdge every <= 5 mm on all edges).
* `dense_*.txt` / `dense.pkl` / `midsec.pkl`: kernel sections every 2 mm + kink stations, both sides; `mid.py`.
* `study.py` (interpolation along s), `xsec.py` + `run_x.py` (edge-crossing prototype), `deform.py`.
* `undrape_core.fs` (the code above). `mkharness.py` -> `harness_full.fs` runs it in Onshape on RnRD. `tev.py`
  does the timing. `bench*.fs`, `b2*/b3*/e_*/ftp_*/pd_*.fs` are the cost measurements of section 3.
* Figures: `secs.png` (sections), `plan.png` (edges in the chart plan), `E.png` (excess per side vs s).

---

## 12. As built (2026-09-24): `driven_offset/undrape_utils.fs`

Not pushed. `undrapeOutline(context, id, chart, side0, side1, thickness, spacing)` implements sections 1-10, with
the U-turn rule in one marked function (`undrapeRefusedSection`, currently literal: the station's own kernel
section). Interface as agreed, plus two extras: each edge carries `arcs` (the chart arc of each point's
station) and the report carries `failed` (stations nothing could measure). Loop 0 is the outer loop and runs
counter-clockwise in (x, y); holes run clockwise. Shared vertices are computed once, so joints are exact.

**Changes from sections 4 and 10 (each found on RnRD):**

1. *Edge sampling.* Positions and tangents come from `evEdgeTangentLines` every 10 mm, which is about 7x cheaper
   per point than `evFaceTangentPlanesAtEdge`. Normals come from `evFaceTangentPlanesAtEdge` at every 4th sample
   and at every sample of a stretch where they turn more than 2 deg; the rest are interpolated and made normal to
   the edge tangent. Positions need 10 mm spans: 40 mm Hermite spans were 5 um off in height where W's curvature
   changes, which gave 0.0035 mm on the rim. Normals do not.
2. *Creases.* `evEdgeConvexity` reports 137 of the 336 side edges as not smooth. 118 of them have real normal
   jumps of 0.2 to 77 deg. All of these are face ends kinked lengthwise (for example the transition flap's 11.3 deg
   lengthwise slope), not creases across the section, so 8.4's "the test part has none" is wrong. The second face
   is sampled only for kinked edges that run along the reference. With `usingFaceOrientation` the second face
   runs the edge in its own loop direction (samples reversed), so its samples are matched by position. When the
   face side is ambiguous at a crossing (|into-face . section tangent| < 0.1), the two tangents are averaged.
3. *Edges lying in a station plane.* Face-end edges running straight across (any line along Y lies in a station
   plane) contribute their samples as section points when |distance| < 5e-8 m. The tail and tip end edges are
   such edges, so their outline samples map through their own in-plane section.
4. *Refusal rule.* An oblique non-rim crossing refuses the station only if inPlane < 0.995, i.e. the face is cut
   lengthwise. In-plane samples never refuse, and rim crossings are never dropped. Under the rule of section 4,
   every vertex station (vertices sit on face ends) went to the kernel.
5. *Stations.* Rim edges running along the reference (|t . u| >= 0.5) use the shared grid arc = k * spacing,
   which both long sides share. Other rim edges (ends, notches, holes) are sampled every `spacing` along their
   length, each sample at its own station. Each vertex gets one station. Three more stations sit at 0.2, 0.4 and
   0.6 mm inside each edge end; the end tangent is the derivative of the quadratic through them, and the vertex
   is left out (see finding c).
6. *Kernel fallback.* Faces are box-filtered (loose `evBox3d`, 1e-4 m pad). Each result edge is read at
   9 samples and scaled to its exact `evLength`. At a face-end plane the kernel returns the same piece twice,
   so already-covered widths are skipped. Gaps are bridged by the chord. Lookups are lazy: pairing every node
   with the other side cost 1.3 s. The rim end pairs with the other side's rim end, not with its nearest point.
   Retries at s + 1e-6, -1e-6, +5e-5, -5e-5 m, with `try silent` only there. Temporary bodies are created under
   `id` and deleted before return.

**Accuracy on RnRD** (W = RjRP, align StjLB, d = -0.2 mm, t = 0.4 mm, spacing 6 mm; truth = kernel sections of
both sides, `evLength` per piece, mean of the sides):
- 124 stations (every third grid station, plus every station in the transitions, the U-turns and at the
  vertices), both rims: at edge-method stations the max error is 0.0015 mm (mean 0.00016). Kernel-fallback
  stations are within 0.0010 mm, except s = 1818 at 0.0088 mm. There the truth itself leaves out two ~10 um gaps
  (a sliver the intersection does not return); the fallback bridges them.
- 7 vertex arcs, each at 0, +-0.2, +-0.4 and +-1.0 mm (49 stations): vertices are within 0.0002 mm, except
  s = 536.22 on one side at 0.0018 mm (finding c). End tangents are within 0.3 deg of the truth's one-sided
  slope, and within 0.05 deg away from the transition vertices.
- Tail end: 150.000 mm. Tip end edges: 75.000 mm each.

**Speed** (eval API, wall clock minus the setup baseline): about 2.8 s for the whole call.

| part of the call | time |
|---|---|
| side edge sampling (336 edges, 2773 samples) | 0.89 s |
| requests and stations | 0.19 s |
| candidates and crossings (358 stations, 10.3 candidate edges per station) | 0.48 s |
| sections | 0.18 s |
| mapping and assembly | 0.06 s |
| deformation tally | 0.20 s |
| 11 kernel fallbacks (tail U-turn s 42-60, tip U-turn s 1788-1824), ~68 ms each; the kernel floor is ~63 ms | 0.84 s |

Batching every refused plane into one `opIntersectFaces` per side saved only ~0.08 s. Tight boxes cost 0.6 s to
compute. Neither was adopted.

**Deformation on RnRD (report):**
- Stretch along the rim and the bend lines runs from -6.48 % to +7.53 %. The maximum is on a tip U-turn bend
  line at x 896.9 mm, y -26.5 mm. Outside the U-turns it runs from -1.04 % to +0.41 %.
- Shear (the change in angle between a tracked chord and its section) reaches 22.6 deg in the U-turns and
  9.5 deg outside them (the transition zones; section 7 gives 9.2).
- Rim length: 3956.419 mm in 3D, 3952.478 mm flat.

**Findings:**
- (a) Plane-through-vertex kernel failures do occur: the truth run gave `WIRE_CREATION_FAILED` at s = 1663.70.
  Edge-method stations are unaffected.
- (b) `evEdgeConvexity` is no crease test on pressed parts: see change 2.
- (c) At the transition vertices the true outline has a sharp ~0.01 mm feature within ~0.2 mm of the vertex.
  The step's face ends are slanted up to 0.09 mm in s against the rim's face end, and the lengthwise kink makes
  the in-plane thickness at exactly s_v ambiguous. The vertex point is right to 0.002 mm. A smooth fit through
  the vertex with the one-sided tangent can deviate by ~0.005-0.01 mm over the first 0.2 mm. The caller should
  either accept this or place the edge end 0.2 mm inside.

**Harness** (scratchpad `undrape_impl/`): `conv.py` + `mk.py` turn this file into eval lambdas (dependency-ordered)
with `setup.fsx`, which holds the sides and a chart built like `buildAlongReference`. `t_run.fsx` dumps the
outline. `truth_tmpl.fsx` + `mktruth.py` compute the kernel truth. `cmp.py` (rims) and `vcheck.py` (vertices and
tangents) compare. `tev.py` times, and `mkstage.py` cuts the call at each stage for the breakdown.
