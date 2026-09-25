# Undrape: which Onshape ops to use (research, 2026-09-24)

Research for adding UNDRAPE to `driven_offset/unwrap.fs`: find a constant-thickness part's two
sides without assuming they follow the reference, build its mid-surface, get the profile W from a
user face, and rebuild the flat plate. Every number below was measured through the FS eval API
(scratch context, nothing written to the document) on the Design test Part Studio
(doc f61d2c000ab2d1240776342e / element 80c1e329f99a05e224058526), topsheet solid RnRD.
Scratch scripts: `%TEMP%\claude\...\scratchpad\ops\*.fs` (timing runner `tev.py`).

**Timing method.** Wall-clock time of the eval HTTP call, taken from repeated runs, minus a
baseline. A trivial script (evaluate the 252 faces) takes 0.13-0.15 s. The first call after a
regen can be slow (up to 25 s); those runs are ignored.

## Test body (not what the brief said)

The brief said RnRD had slots and a tail notch. It does not. It has 252 faces: two sides of 124
faces each (G1 across all internal edges; types 199 OTHER, 40 SPLINE, 6 CYLINDER, 3 EXTRUDED) and
only **4 wall faces** (planes y = +-75 and the two ends). There are slivers down to 0.01 mm^2
(at x = 145). To test holes, every robustness case also ran on a copy cut inside the eval:
- a 100 x 10 mm slot at x 1400-1500, y 55-65. Its vertical walls run through the steep
  outer region, so each wall is about 3 mm tall.
- a 120 x 15 mm slot at x 300-420, y -70 to -55.
- an 8 mm round hole at (800, 47), which sits on a 30 deg region.
- a tail notch at x 1760-1810, y -15 to 15.

The cut body has 265 faces and 17 walls.

## Summary

| Step | Recommended op sequence | Time | Accuracy / result |
|---|---|---|---|
| 1 sides + t | `evOffsetDetection` seed pair -> `qTangentConnectedFaces` per side | **~0.05-0.1 s** | 124 / 124 / 4 walls (cut: 124 / 124 / 17); t = 0.400000 mm |
| 1 fallback | inward `evRaycast` against the BODY per face | 2.2 s | 246 of 248 sides (2 slivers had no interior sample) |
| 2 mid-surface | `opExtractSurface(side0, offset -t/2, useFacesAroundToTrimOffset true)` | **~1.25 s** | distance to both sides = t/2 within 5.7e-5 mm (1001 samples); boundary exactly on the walls |
| 3 W from face | big `opPlane` -> `opSplitFace(mid, faceTools)` -> pick edges within 1 um of the plane -> `opExtractWires` | **~0.25 s** | one chain, 11 edges, 0 mm off the mid-surface, <= 4.8e-4 mm off the plane |
| 3 W as wire | `evPlanarEdges` + joint-angle check | 0.01 s | RjRP: planar, 3 edges, one 0.403 deg joint |
| 4 rebuild | `opPlane` -> `opSplitFace(edgeTools = outline)` -> parity by adjacency -> `opExtrude` (or `opThicken`) | **~0.05 s** | 1 solid; caps PLANE, arcs -> CYLINDER, spline -> EXTRUDED; holes cut |

Undrape overhead before the map itself: about **1.6 s** in total, and 1.25 s of that is the offset.

**Bug found in unwrap.fs v1 (`unwrapPlate`).** `opSplitPart` uses only ONE tool body. The
outline has one closed wire per loop, and the extrude gives one wall sheet per wire. With a
multi-body `tool`, only one sheet is used and the others are ignored without any error. In the
test, 4 wall bodies went in and the result was 1 solid carrying the outer walls only: **no slot or
hole was cut**. Splitting once per wall body does cut them, but then the plugs inside the holes are
left behind as extra solids (6 solids). Use the section 4 route instead.

---

## 1. The two sides and t, found without a reference

### What was tried

| Method | Time | Result |
|---|---|---|
| `evOffsetDetection({bodies})` (internal, used by sheetMetalRecognize) | ~0.02 s | 3 groups. Each is a single exact face pair, offset 0.40000 mm. It does NOT pair the other 118 pairs, because OTHER/procedural offset faces are not recognised. **Good for seeds and t, not a full classifier.** |
| `qTangentConnectedFaces(seed, tol)` from `side0[0]` / `side1[0]` | ~0.05 s | 124 + 124, no overlap, 4 walls left (cut body: 17). The result was identical for tol = 1, 10, 30, 45, 60 and 75 deg. The walls meet the sides at 72-90 deg. |
| Inward raycast per face, target = the face query | **12.6 s** | Raycasting against a 252-face query is about 50 ms per ray. Do not do this. |
| Inward raycast per face, target = the BODY | 2.2 s | About 9 ms per ray. 246 sides with antiparallel hit normals. 2 slivers had no interior grid point. |
| Centroid + `evDistance` for a sample point | 1.0 s | The point landed on face edges. The hits were wrong for 38 faces (they hit neighbours). Use the `returnUndefinedOutsideFace` grid instead. |
| Flood fill with `qAdjacent`, nesting the query each iteration | 13 s | Query nesting blows up. If you flood fill, re-`evaluateQuery` each iteration. |

### Recommended

```featurescript
/**
 * Two sides, walls and thickness of a constant-thickness solid. Sides are grown by tangency from an
 * exactly-offset seed pair; walls are everything else. No reference needed.
 */
function plateSidesGeneral(context is Context, part is Query) returns map
{
    const groups = evOffsetDetection(context, { "bodies" : part });
    if (size(groups) == 0)
    {
        // Fallback: inward raycast against the BODY (2.2 s / 250 faces), see "Fallback" below.
        throw regenError("Could not find two offset faces on the part.", ["parts"]);
    }
    const g = groups[0];
    const thickness = 0.5 * (g.offsetLow + g.offsetHigh);
    // 45 deg: sides are G1 or gently creased; walls meet them at ~72-90 deg (vertical cut on an 18 deg drape = 72).
    const side0 = qUnion(evaluateQuery(context, qTangentConnectedFaces(g.side0[0], 45 * degree)));
    const side1 = qUnion(evaluateQuery(context, qTangentConnectedFaces(g.side1[0], 45 * degree)));
    if (!isQueryEmpty(context, qIntersection(side0, side1)))
    {
        throw regenError("The part's two sides are tangent-connected (not a plate).", ["parts"]);
    }
    const walls = qSubtraction(qOwnedByBody(part, EntityType.FACE), qUnion(side0, side1));
    return { "side0" : side0, "side1" : side1, "walls" : walls, "thickness" : thickness,
             "offsetSpread" : g.offsetHigh - g.offsetLow };
}
```

Checks worth adding. Each is cheap because it only evaluates queries:
- Every other group's `side0[i]` / `side1[i]` should fall in `side0` / `side1`. If a group comes
  back the other way round, the pair has been swapped. If it is in neither, a side is creased by
  more than the tolerance.
- Every wall should be adjacent to both sides: `qAdjacent(wall, EDGE, FACE)` hits both sets.
- A constant-thickness check, if wanted, costs about 9 ms per sample. `evDistance` from one point
  to the 124-face side query costs about 6 ms. One sample per face is about 1 s. As a cheaper
  alternative, trust the offset from step 2 and check only a few points.

### Fallback (no exact offset pair)

```featurescript
var grid = [];
for (var a = 0; a < 3; a += 1)
{
    for (var b = 0; b < 3; b += 1)
    {
        grid = append(grid, vector((a + 0.5) / 3, (b + 0.5) / 3));
    }
}
for (var f in evaluateQuery(context, qOwnedByBody(part, EntityType.FACE)))
{
    var tp = undefined;
    for (var q in evFaceTangentPlanes(context, { "face" : f, "parameters" : grid, "returnUndefinedOutsideFace" : true }))
    {
        if (q != undefined)
        {
            tp = q;
            break;
        }
    }
    if (tp == undefined)
    {
        continue; // sliver: left for the flood fill / tangent growth to pick up
    }
    const hits = evRaycast(context, { "entities" : part, "ray" : line(tp.origin - 1e-6 * meter * tp.normal, -tp.normal) });
    if (size(hits) == 0 || hits[0].entityType != EntityType.FACE || hits[0].entity == f)
    {
        continue; // edge hits have a number parameter, not a Vector
    }
    const hn = evFaceTangentPlane(context, { "face" : hits[0].entity, "parameter" : hits[0].parameter }).normal;
    if (dot(hn, tp.normal) < -0.999)
    {
        // side face; local thickness = hits[0].distance + 1e-6 m. Seed tangent growth from any one.
    }
}
```

The fallback measured t = 0.39998 to 0.40000 mm. Use it only to find one seed pair and t, then
grow the sides as in the recommended code. Classifying all 252 faces this way is slower and misses
the slivers.

---

## 2. Mid-surface

`opExtractSurface` accepts `offset` (and `useFacesAroundToTrimOffset`, default true). Offset
distances are measured along the face normal. For side faces of a solid that normal points out,
so **-t/2 goes into the plate**.

```featurescript
opExtractSurface(context, id + "mid", {
            "faces" : sides.side0,
            "offset" : -0.5 * sides.thickness,
            "useFacesAroundToTrimOffset" : true   // REQUIRED: trims the offset by the extended walls
        });
const mid = qCreatedBy(id + "mid", EntityType.BODY);
```

| Variant | Time over base | Result |
|---|---|---|
| offset -t/2, trim **true** | 1.25 s | 1 sheet, 124 faces (121 OTHER + 3 CYLINDER). Checked at 1001 points: max abs(d(side0) - t/2) = 5.7e-5 mm and max abs(d(side1) - t/2) = 3.5e-5 mm, which is the part's own thickness scatter. Mid-surface area 282096.84 against a side average of 282096.76 mm^2. Laminar boundary is 19 edges (cut body: 58, the same count as the side boundary), and every boundary sample lies exactly on a wall (0 mm). |
| offset -t/2, trim false | 1.25 s | Uncut body: the same result, because its walls happen to contain the offset direction. **Cut body: boundary up to 0.173 mm off the vertical slot walls.** |
| offset 0 + `opOffsetFace(-t/2)` | 1.25 s | The same geometry and the same cost. No gain. |
| offset 0 only | 0.05 s | Shows the time is all in offsetting 124 faces. |
| `redundancyType ALLOW_REDUNDANCY` | 1.25 s | No change. |
| offset from side1 instead | 1.25 s | Mid-surface within 3.3e-6 mm of t/2 from side1. Equivalent. |

The slivers (0.01 mm^2), the 199 OTHER faces, the 30 deg hole region and the steep slot all came
through with no failure. The mid-surface is oriented like side0 (its normal is side0's outward
normal).

Timing note: the check itself (evDistance to a 124-face query at 1000+ points) takes 12 s, so only
run it in a debug mode.

---

## 3. Profile W

### 3a. W from a user face (for example the Front plane JCD, y = 0)

**What goes wrong here:** the mid-surface faces are split along y = 0 at the tip and at the tail.
Near x 1754-1759 on y = 0 there is a small hub of short faces, and some of its existing edges lie
up to 0.48 um OFF the plane.

| Method | Time over mid | Result |
|---|---|---|
| `opIntersectFaces(tools: JCD face, targets: mid faces)` | 0.25 s | It intersects the unbounded plane (the result spans the full part even though JCD's face is only 150 mm). It returns **18 separate wire bodies with 7 duplicated edges**, one per adjacent face wherever the plane runs along an existing edge. The curves are approximate (up to 5.5e-4 mm off the mid-surface) and there is a 0.55 um gap at x 1624.5. Dropping duplicates by midpoint gives one chain on the uncut body. **On the cut body, `opExtractWires` fails with EXTRACT_WIRES_OVERLAPPING_EDGES** because of sub-micron overlaps at the hub. Not robust. |
| `opPlane` 10 m + `opIntersectFaces` | 0.25 s | Identical to the above. |
| `opSplitFace(mid, planeTools : JCD)` | - | **INVALID_INPUT**. |
| `opSplitFace(mid, bodyTools : big plane)`, or `faceTools`, or `opSplitPart(mid, big plane)` | 0.2-0.4 s | Clean topology, 0 mm off the mid-surface. **However, `qCoincidesWithPlane` misses the hub edges (0.48 um off), so it returns 2 chains with a 4.9 mm gap.** |
| **big plane + `opSplitFace(faceTools)` + pick mid edges whose samples are within 1 um of the plane** | **0.25 s** | **1 chain, 11 edges, 1827.69 mm (cut body: 1785.73 mm, ending on the notch wall at x 1760), 0 mm off the mid-surface, at most 4.8e-4 mm off the plane.** |
| Evaluate the mid-surface along an isoline | - | Not general: the faces are not parameterised along the plane. Not pursued. |

```featurescript
/** W = the section of the mid-surface by a user face's plane, as ONE wire body (throws otherwise). */
function profileFromFace(context is Context, id is Id, mid is Query, userFace is Query) returns Query
{
    const pl = evPlane(context, { "face" : userFace });
    opPlane(context, id + "sectionPlane", { "plane" : pl, "width" : 10 * meter, "height" : 10 * meter });
    opSplitFace(context, id + "section", {
                "faceTargets" : qOwnedByBody(mid, EntityType.FACE),
                "faceTools" : qOwnedByBody(qCreatedBy(id + "sectionPlane", EntityType.BODY), EntityType.FACE)
            });
    opDeleteBodies(context, id + "deleteSectionPlane", { "entities" : qCreatedBy(id + "sectionPlane", EntityType.BODY) });
    // qCoincidesWithPlane is too strict: pre-existing mid edges that the split merged into can sit ~0.5 um off.
    var onPlane = [];
    for (var e in evaluateQuery(context, qIntersectsPlane(qOwnedByBody(mid, EntityType.EDGE), pl)))
    {
        var ok = true;
        for (var l in evEdgeTangentLines(context, { "edge" : e, "parameters" : [0, 0.25, 0.5, 0.75, 1] }))
        {
            if (abs(dot(l.origin - pl.origin, pl.normal)) > 1e-6 * meter)
            {
                ok = false;
                break;
            }
        }
        if (ok)
        {
            onPlane = append(onPlane, e);
        }
    }
    if (size(onPlane) == 0)
    {
        throw regenError("The selected face does not cross the part's mid-surface.", ["profileFace"]);
    }
    opExtractWires(context, id + "profile", { "edges" : qUnion(onPlane) });
    const chains = qCreatedBy(id + "profile", EntityType.BODY);
    if (size(evaluateQuery(context, chains)) != 1)
    {
        throw regenError("The section crosses a hole or slot: it breaks into several pieces.", ["profileFace"]);
    }
    return chains;
}
```

`opSplitFace` changes the mid-surface: it adds interior two-sided edges along W. The laminar
boundary does not change, so mapping and outline extraction are unaffected.

**Failure modes, all measured:**
- **The section crosses a hole or slot: several chains.** An extra 10 mm hole at (1000, 0) gave
  2 wire bodies and 4 free ends. Options: throw (as the snippet does); bridge each gap with a G1
  cubic between the end tangents (the bridge shape affects how the material beside the hole maps);
  or ask for a plane that misses the holes.
- **The section ends early at a notch.** Here W stops at x 1760 while the part runs to 1795 at
  y = +-75. The target surface must extend past W's ends. Straight tangent extension is what the
  unwrap chart already assumes.
- **W is not strictly G1:** a **0.403 deg kink at x = 145**. This is the part's own crease: the side
  faces meet at 0.4033 deg there (measured on the solid). It is also on the user wire RjRP (its
  joint is 0.403 deg). The default tolerance of `qTangentConnectedEdges` does NOT join those edges.
  Any "must be a tangent chain" check needs about 1 deg of slack, or it must report the kink.
- If the plane is tangent to the mid-surface or contains one of its faces, the section is not
  defined. Detect it with `evaluateQuery(qCoincidesWithPlane(mid faces, pl))` and throw.

### 3b. W as a user wire

```featurescript
const edgesQ = qOwnedByBody(definition.profileWire, EntityType.EDGE);
const wPlane = evPlanarEdges(context, { "edges" : edgesQ });   // throws if not planar
// then: pair coincident end points, worst angle between end tangents (allow ~1 deg, see 0.403 above)
```

This takes 0.01 s. On RjRP: 3 edges, plane normal (0, -1, 0), 0 off the plane, worst joint
0.403 deg.

---

## 4. Rebuilding the flat plate

Test fixture: the cut body's side outline was projected with `opDropCurve` onto z = 0. That gives
the outer loop (37 edges, with the notch), 2 slots, and the hole replaced by an exact sketch
circle. Two more loops were added: a closed fit spline, and a stadium slot (2 arcs + 2 lines).
Six closed wires in all, t = 0.4 mm.

| Route | Time over fixture | Result |
|---|---|---|
| (a) **v1 `unwrapPlate`**: slab + walls + ONE `opSplitPart` + delete the corner piece | 0.03 s | **Wrong.** 1 solid with the outer walls only. Holes and slots are NOT cut, because `opSplitPart` uses a single tool body (see the bug note above). |
| (a') v1 with one `opSplitPart` per wall body | 0.07 s | Holes are cut, but the plugs remain: 6 solids (1 plate + 5 plugs). It needs a plug classifier, and the solids are separate bodies with no shared adjacency to go by. |
| (b) `opFillSurface(edgesG0 = all loops)` + `opThicken` | - | **FILL_SURFACE_FAIL** with inner loops. |
| (b') `opFillSurface` on the outer loop only | 2-4 s | The face comes out as a true PLANE, but it is slow and has no holes. Rejected. |
| (c) sketch regions: lines -> `skLineSegment`, circles -> `skCircle`, others -> `skFitSpline` (25 samples), then `opExtrude(qSketchRegion(sk, true))` | 0.25 s | 1 solid, holes correct, CYLINDER x3, EXTRUDED x1, PLANE x20. Splines are REFIT, so this is only exact for lines, arcs and circles. |
| (d) `opEnclose(two plane sheets + wall sheets)` | 0.07 s | **Wrong.** 1 solid, and all enclosed regions are merged (no holes). |
| **(e) `opPlane` + `opSplitFace(edgeTools = outline)` + parity by adjacency + `opExtrude`** | **0.05 s** | **1 solid, caps PLANE, 3 CYLINDER (circle + slot arcs), 1 EXTRUDED (spline), holes cut.** Cap area 265753.2 mm^2, the same as route (c). |
| (e') the same with `opThicken(t, 0)` instead of `opExtrude` | 0.05 s | Same solid. Coplanar walls are merged, so 24 faces instead of 50. |

Route (e) keeps the outline curves exactly as they are: arcs stay arcs and splines stay the same
spline. Material is decided by parity, so islands inside holes work. There are no point-in-polygon
tests and no margins.

```featurescript
/**
 * Flat plate from closed planar outline wires (outer + holes, any nesting). flatPlane is the plane the
 * wires lie in (the flat mid-plane for undrape); the plate is thickness/2 each side of it.
 */
function plateFromOutline(context is Context, id is Id, wireEdges is Query, flatPlane is Plane, thickness is ValueWithUnits) returns Query
{
    const bb = evBox3d(context, { "topology" : wireEdges, "tight" : true, "cSys" : planeToCSys(flatPlane) });
    const c = planeToWorld(flatPlane, vector(0.5 * (bb.minCorner[0] + bb.maxCorner[0]), 0.5 * (bb.minCorner[1] + bb.maxCorner[1])));
    opPlane(context, id + "sheet", {
                "plane" : plane(c, flatPlane.normal, flatPlane.x),
                "width" : bb.maxCorner[0] - bb.minCorner[0] + 20 * millimeter,
                "height" : bb.maxCorner[1] - bb.minCorner[1] + 20 * millimeter
            });
    const sheet = qCreatedBy(id + "sheet", EntityType.BODY);
    opSplitFace(context, id + "cut", { "faceTargets" : qOwnedByBody(sheet, EntityType.FACE), "edgeTools" : wireEdges });

    // Parity: faces on the sheet's own (laminar) border are depth 0; every loop crossed adds one.
    const faces = qOwnedByBody(sheet, EntityType.FACE);
    var labelled = qAdjacent(qEdgeTopologyFilter(qOwnedByBody(sheet, EntityType.EDGE), EdgeTopology.LAMINAR),
        AdjacencyType.EDGE, EntityType.FACE);
    var frontier = labelled;
    var depth = 0;
    var material = [];
    while (true)
    {
        const next = evaluateQuery(context, qSubtraction(qIntersection(qAdjacent(frontier, AdjacencyType.EDGE, EntityType.FACE), faces), labelled));
        if (size(next) == 0)
        {
            break;
        }
        depth += 1;
        if (depth % 2 == 1)
        {
            material = concatenateArrays([material, next]);
        }
        frontier = qUnion(next);
        labelled = qUnion(evaluateQuery(context, qUnion(labelled, frontier)));   // flatten: nested queries blow up
    }
    if (size(material) == 0)
    {
        throw regenError("The unwrapped outline does not close.", ["parts"]);
    }
    opExtrude(context, id + "plate", {
                "entities" : qUnion(material),
                "direction" : flatPlane.normal,
                "endBound" : BoundingType.BLIND,
                "endDepth" : 0.5 * thickness,
                "startBound" : BoundingType.BLIND,
                "startDepth" : 0.5 * thickness
            });
    opDeleteBodies(context, id + "deleteSheet", { "entities" : sheet });
    return qCreatedBy(id + "plate", EntityType.BODY);
}
```

(The measured version had a z = 0 plane and a one-sided extrude of t. The two-sided extrude and
the plane-local bounding box have not been run yet. Check them on the first live run.)

Notes:
- `qCreatedBy(sketchId, EntityType.EDGE)` on a sketch returns BOTH the wire edge and the region
  face's edge. Passing that to `opExtractWires` gives EXTRACT_WIRES_OVERLAPPING_EDGES. Filter with
  `qOwnedByBody(qBodyType(qCreatedBy(sketchId, EntityType.BODY), BodyType.WIRE), EntityType.EDGE)`.
- Route (e) needs every loop to close within kernel tolerance (the v1 emitted curves meet
  end to end). If a loop does not close, the sheet stays one face and `material` is empty, which
  the snippet reports.

---

## Recommended undrape sequence

1. `plateSidesGeneral`: seeds, sides, walls and t (~0.1 s).
2. `opExtractSurface(side0, -t/2, trim true)`: the mid-surface (~1.25 s).
3. W: the user wire (check it is planar and G1 within about 1 deg), or `profileFromFace` (~0.25 s).
   Then the target = W extruded along W's plane normal, plus the optional offset.
4. Map the mid-surface's laminar boundary edges (`qEdgeTopologyFilter(qOwnedByBody(mid, EDGE),
   LAMINAR)`, the same count as the side outline) through the chart onto the flat plane. This is
   the existing `unwrapEdges` step, with no thickness offset, because the mid-surface is the
   neutral surface.
5. `plateFromOutline(outline wires, flat mid-plane, t)` (~0.05 s). Delete the mid-surface and the W
   temporaries.

The whole thing is about 1.6 s plus the edge map. The offset in step 2 is the only large cost. If
it ever matters, an alternative is to map the laminar edges of side0 (offset 0, 0.05 s) and shift
each sample by -t/2 along the side normal before mapping. That is exact for the outline, but W from
a face would then need the mid-surface anyway.
