# SWRout Context & Refactoring Notes

This document records API findings, known bugs, and refactoring guidance for `createSWRoutSurf.fs`.
Generated from std library research on 2026-03-13.

---

## Feature Overview

`SWRout` generates a sidewall rout surface for a ski/snowboard given:
- A bottom surface and a side surface
- Rout angle, step-in distance, and height above bottom
- Optional: endpoint trimming with a reference wire and cutter radius

**Logical flow:**
1. Copy both surfaces as working bodies
2. Determine offset direction signs (up for bottom, outward for side) via `processFirstMoves`
3. Intersect copies → extract start wire (at `distAboveBottom`)
4. If step-in > 0: offset side inward, intersect again → extract step-in wire
5. Offset bottom up by `routHeight`, offset side out by `routOffset`, intersect → extract stop wire
6. Split all wires at front plane (keep one half — ski is symmetric)
7. Loft stop→stepIn and optionally stepIn→start; boolean union if two lofts
8. Find outside edges (on original side surface), extend them
9. (Incomplete) If endpoint-specified: trim at start/stop points using `trimSWRout`

---

## API Reference: Verified Signatures

### opPattern — Body Copy
```featurescript
opPattern(context, id + "copyBottom", {
    "entities" : definition.bottomSheet,
    "transforms" : [identityTransform()],   // prefer over transform(vector(0,0,0)*mm)
    "instanceNames" : ['1']
});
var copy = qCreatedBy(id + "copyBottom", EntityType.BODY);
```
- `opPattern` preserves original, creates copies. `opTransform` moves original — do NOT use for copies.
- `identityTransform()` is the idiomatic zero-transform (cleaner than `transform(vector(0,0,0)*millimeter)`).

### opIntersectFaces — Creates Edges AND Wire Bodies
```featurescript
opIntersectFaces(context, id + "intersect", {
    "tools" : faceQueryA,
    "targets" : faceQueryB
});
// Directly usable — opIntersectFaces creates BOTH edges and wire bodies:
var edges = qCreatedBy(id + "intersect", EntityType.EDGE);
var wires = qCreatedBy(id + "intersect", EntityType.BODY);
```
- **Key finding:** `opIntersectFaces` already creates wire bodies. The extra `opExtractWires` step
  in the current code is redundant — you can use `EntityType.BODY` directly from the intersection op,
  then delete when no longer needed.
- Neither `tools` nor `targets` bodies are modified.

### opExtractWires — Only Needed for Loose Edges
```featurescript
opExtractWires(context, id + "extractWire", {
    "edges" : someLooseEdgeQuery
});
var wire = qCreatedBy(id + "extractWire", EntityType.BODY);
```
- Use when you have edges from a non-intersection source (e.g., `qEdgeTopologyFilter`).
- Fails if edges overlap, cross, or more than 2 meet at a point.

### Correct Pattern — opPattern + transform for Sheet Body Copies

Both `opOffsetFace` and `opExtractSurface` with `offset` use the same direct-edit kernel internally
and fail with `DIRECT_EDIT_OFFSET_FACE_FAILED` on complex surface geometry.

The correct approach: get the face normal direction from `evFaceTangentPlane`, then create
translated copies with `opPattern`. `opPattern` is a pure geometric copy — no kernel offset call.

```featurescript
// 1. Get face normal (unit vector, direction of surface offset)
var faceNormal = evFaceTangentPlane(context, {
    "face"      : qNthElement(qOwnedByBody(sheetBodyQ, EntityType.FACE), 0),
    "parameter" : vector(0.5, 0.5)   // centre of parameter space
}).normal;

// 2. Create a translated copy in the normal direction
opPattern(context, id + "shiftedCopy", {
    "entities"      : sheetBodyQ,
    "transforms"    : [transform(distance * faceNormal)],  // unitless normal * length = translation vector
    "instanceNames" : ["1"]
});
var shiftedCopy = qCreatedBy(id + "shiftedCopy", EntityType.BODY);

// 3. Intersect, extract wire, delete copy
opIntersectFaces(...);
opExtractWires(...);
opDeleteBodies(context, id + "deleteShiftedCopy", { "entities" : shiftedCopy });
```

- `evFaceTangentPlane` `parameter` is a 2D vector in parameter-space [0,1]×[0,1]; `vector(0.5, 0.5)` is the face centre.
- `distance * faceNormal` — unitless normal × ValueWithUnits = Vector with units, valid for `transform()`.
- To shift **inward** (opposite normal), use `-distance * faceNormal`.
- **Normal direction is not guaranteed** — `evFaceTangentPlane` returns the face normal but the sign depends on surface creation order. Always correct before use:
  ```featurescript
  if (bottomNormal[2] < 0) { bottomNormal = -bottomNormal; }  // must point up (+Z)
  if (sideNormal[1]   < 0) { sideNormal   = -sideNormal; }    // must point outward (+Y for +Y half)
  ```
- Approximation note: all points translate by the same vector rather than along local normals. Accurate for gently curved surfaces like ski geometry.

### extendSurface — BLIND Requires Non-Zero extendDistance
```featurescript
extendSurface(context, id + "extend", {
    "entities" : edgeQuery,
    "tangentPropagation" : true,
    "endCondition" : ExtendBoundingType.BLIND,
    "oppositeDirection" : false,
    "extendDistance" : definition.bottomExtension,   // must be non-zero for BLIND
    "maintainCurvature" : true
});
```
- `ExtendBoundingType.BLIND` requires a nonzero `extendDistance`. Passing `0 * millimeter` is a bug.
- Entities can be boundary edges (ONE_SIDED) or the sheet body itself.

### constructPath / evDistancePath / evPathTangentLines
```featurescript
var path = constructPath(context, edgeQuery);
// path.edges    → array of Query (in traversal order)
// path.flipped  → array of boolean (true = traverse backwards)
// path.closed   → boolean

var distResult = evDistancePath(context, { "side0" : path, "side1" : pointOrQuery });
// distResult.distance      → ValueWithUnits
// distResult.pathParameter → number [0..1] along path
// distResult.sides[0/1]    → { index, point, parameter }

var tangentResult = evPathTangentLines(context, path, [0, 1]);
// tangentResult.tangentLines  → array of Line
// tangentResult.edgeIndices   → array of int (which path.edges entry)
```
- `evDistancePath` is a **custom function** (from footprintAnalytics), not std. It requires a Path on one side.
- Passing `refPath.edges` (an array of Queries) directly to `evDistance` is valid — it finds the nearest.

### qFrontPlane / qSplitBy
```featurescript
qFrontPlane(EntityType.FACE)   // XZ plane face — correct as opSplitPart tool
qSplitBy(id + "split", EntityType.BODY, true)   // back body (behind plane)
qSplitBy(id + "split", EntityType.BODY, false)  // front body (in front of plane)
```

### opLoft connections — Resolving LOFT_DIRECTION_ERROR
`opExtractWires` assigns an **arbitrary traversal direction** to each wire body. When two profile
wires end up traversed in opposite directions, `opLoft` throws `LOFT_DIRECTION_ERROR` ("Could not
determine loft direction.").

**Fix: vertex connection.** Find the nearest endpoint vertex pair across the two wires and pass
them as a connection entry. This gives the loft kernel an unambiguous direction reference.

```featurescript
// connections array entry — vertex-only (no edge params needed)
{
    "connectionEntities"       : qUnion([vertexOnWireA, vertexOnWireB]),
    "connectionEdgeQueries"    : qUnion([]),   // empty — no edge entries
    "connectionEdgeParameters" : []            // must match size of evaluated connectionEdgeQueries
}
```

**Finding the nearest vertex pair (robust to arbitrary opExtractWires orientation):**
```featurescript
var vertsA = evaluateQuery(context, qOwnedByBody(wireA, EntityType.VERTEX));
var vertsB = evaluateQuery(context, qOwnedByBody(wireB, EntityType.VERTEX));
// For open wires: 2 vertices each. For closed wires: 0 (return [] — no connection needed).
if (size(vertsA) == 0 || size(vertsB) == 0) return [];
var minDist = 1e10 * meter; var bestA = vertsA[0]; var bestB = vertsB[0];
for (var va in vertsA) {
    for (var vb in vertsB) {
        var d = evDistance(context, { "side0" : va, "side1" : vb }).distance;
        if (d < minDist) { minDist = d; bestA = va; bestB = vb; }
    }
}
```

**Structure notes (from loft.fs source):**
- `loft.fs` internally calls `evaluateQuery(context, connection.connectionEdgeQueries)` → stores as
  `connectionEdges`. Size check: `size(connectionEdges) == size(connectionEdgeParameters)`. Both
  empty → passes. ✓
- `connectionEntities` vertices are passed through to the `builtin_opLoft` kernel for alignment.
- The manipulator code only activates for non-empty `connectionEdgeQueries` — skipped for
  vertex-only connections.
- For edge connections (if needed): `connectionEntities` = union including the edge,
  `connectionEdgeQueries` = union of just the edges, `connectionEdgeParameters` = [paramValue] per edge.

**Why not guide curves?** Guide curves require creating additional wire geometry (line between
midpoints), which adds complexity. Vertex connections are cleaner for endpoint alignment.

---

## Confirmed Bugs (all fixed in refactor)

| # | Original Location | Bug | Fix |
|---|----------|-----|-----|
| 1 | Line 80 | `copySide` patterned `definition.bottomSheet` instead of `definition.sideSheet` | Fixed |
| 2 | Line 418 vs 90–91 | `processFirstMoves` returned `'bottomDir'`/`'sideDir'` but caller read `.bottomDirSign`/`.sideDirSign` | Fixed (then removed) |
| 3 | Lines 370, 374 | `opOffsetFace` used `EntityType.BODY` for `moveFaces` | Fixed (then removed — `opOffsetFace` replaced entirely) |
| 4 | Line 235 | `extendSurface` had `extendDistance: 0 * millimeter` | Fixed |
| 5 | All offset ops | `opOffsetFace` AND `opExtractSurface` with `offset` both use direct-edit kernel — fail on complex sheet geometry | Replaced with `opPattern` + `transform(distance * faceNormal)` |

## Non-Bugs (Previously Suspected)
- **`qCreatedBy(id + "loft1")` after `opBoolean` UNION** — valid. `boolean.fs` explicitly states "Owner body of matches[0].topology1 survives." First tool body identity is preserved. Current code is correct.
- **`opExtractWires` after `opIntersectFaces`** — intentional. Guarantees a single wire body for clean loft profiles.

---

## Refactoring Opportunities

### 1. Keep opExtractWires — Intentional
The intersect → extractWires → deleteIntersectionBodies pattern is **intentional**.
`opExtractWires` guarantees a single unified wire body even when `opIntersectFaces` produces
multiple disjoint edges. This is required for reliable loft profiles downstream. Do not collapse.

### 2. Consolidate Worker Body Cleanup
Currently piecemeal deletes after each intersection. Better: collect all temp bodies, delete in one pass at end.

### 3. processFirstMoves — Use EntityType.FACE, Fix Return Keys
```featurescript
// Fix moveFaces throughout processFirstMoves
"moveFaces" : qOwnedByBody(toDeleteBottomBody, EntityType.FACE),

// Fix return map to match caller
return { "bottomDirSign" : bottomDirSign, "sideDirSign" : sideDirSign };
```

### 4. processFirstMoves — Logic Clarification
The side face always returns to its original position (zero net offset) — the function is purely
direction sensing. Both branches correctly undo the initial probe offset:
- Wrong direction: sideDirSign flips to -1 → offset `−distAboveBottom` → returns to original ✓
- Correct direction: offset `−distAboveBottom` → returns to original ✓

The bottom body is NOT reset — it stays at `distAboveBottom` after the probe. This is intentional:
the start wire intersection fires immediately after `processFirstMoves`, and needs the bottom body
already at `distAboveBottom`. The final offset then adds `routHeight` on top of that accumulated offset.
Worth adding comments to make this accumulation explicit.

### 5. Complete Endpoint Trimming (Lines 241–255)

**Intent:**
- Each provided trim point is projected onto the ref wire to find the nearest point + tangent
- A plane is constructed at that point, normal to the wire tangent
- The loft surface is split by that plane
- We keep the **larger** of the two resulting bodies (the surface "inside" the points)
- Either start or stop point may be omitted (only one may be provided)

**Issues with current `trimSWRout`:**
- Uses distance to `keepRefPoint` to decide which body to keep — wrong. Should keep the larger body.
- `keepRefPoint` parameter is unnecessary given the "keep larger" rule.
- `sweepAxisSearchPoint` at the end of `trimSWRout` is computed but never used — dead code.

**Correct call site:**
```featurescript
if (definition.specSWRoutEndpoints)
{
    var startIsEmpty = isQueryEmpty(context, definition.startPointQ);
    var stopIsEmpty  = isQueryEmpty(context, definition.stopPointQ);
    if (!startIsEmpty)
        trimSWRout(context, id + "trimStart", loftBody, refWirePath, definition.startPointQ);
    if (!stopIsEmpty)
        trimSWRout(context, id + "trimStop",  loftBody, refWirePath, definition.stopPointQ);
}
```

**Correct `trimSWRout` keep logic** (replace distance comparison with size comparison):
```featurescript
var trueBox  = evBox3d(context, { "topology" : splitBodyTrue,  "tight" : true });
var falseBox = evBox3d(context, { "topology" : splitBodyFalse, "tight" : true });
var trueVol  = (trueBox.maxCorner  - trueBox.minCorner)[0] *
               (trueBox.maxCorner  - trueBox.minCorner)[1] *
               (trueBox.maxCorner  - trueBox.minCorner)[2];
var falseVol = (falseBox.maxCorner - falseBox.minCorner)[0] *
               (falseBox.maxCorner - falseBox.minCorner)[1] *
               (falseBox.maxCorner - falseBox.minCorner)[2];
if (trueVol < falseVol)
    opDeleteBodies(context, id + "deleteSWSplitTrue",  { "entities" : splitBodyTrue });
else
    opDeleteBodies(context, id + "deleteSWSplitFalse", { "entities" : splitBodyFalse });
```

**Post-trim cutter relief geometry** (after keeping the larger body):

The trim point represents where a router bit stops. The end geometry reflects the physical bit shape:

1. **Outside edge extend**: Find the ONE_SIDED edge of the kept loft body nearest `definition.sideSheet`
   (same logic as main feature's outside edge detection). Extend it by `cutterBottomRadius` using
   `extendSurface` — this traces the path the cutter bottom travels past the endpoint.

2. **Split edge cap**: Revolve the split edges (the cut boundary, `qCreatedBy(id + "splitSWRout", EntityType.EDGE)`)
   90° about the wire tangent axis at the trim point using `opRevolve`:
   ```featurescript
   opRevolve(context, id + "capRevolve", {
       "entities"     : qCreatedBy(id + "splitSWRout", EntityType.EDGE),
       "axis"         : line(edgeLine.origin, edgeLine.direction),
       "angleForward" : 90 * degree
       // angleBack defaults to 0
   });
   ```
   This creates a quarter-cylinder cap that closes the end of the rout cleanly — matching the
   cylindrical profile of a router bit — rather than leaving an open edge or overlapping.

3. Together: extend traces the bit's radial reach; the revolve caps the rout channel end.

The axis (`edgeLine.direction`) is already computed in `trimSWRout` — `sweepAxisSearchPoint` was
dead code reaching for this same axis. Remove it.

---

## Resolved Questions
- **KEEP_FRONT correctness**: Ski geometry always lives in +Y. Onshape front plane = XZ plane. KEEP_FRONT retains the +Y half. ✓
- **Top edge identification in `generateDummyTopSurf`**: Use `qEdgeTopologyFilter(sideSheetEdges, EdgeTopology.ONE_SIDED)` → `opExtractWires` → 2 wire bodies → pick the one with highest average Z. The side surface may have many edges on each boundary, so extract-wires is the right consolidation step.
- **`generateDummyTopSurf` refactored approach**:
  1. `qEdgeTopologyFilter` on side surface → ONE_SIDED edges
  2. `opExtractWires` → 2 wire bodies (top + bottom boundaries)
  3. Measure average Z of each wire body's edges via `evBox3d` midpoint Z → pick higher one as top wire
  4. Sample points from top wire, project onto XZ plane (set Y component = 0)
  5. `approximateSpline` to fit smooth curve through projected points
  6. Return the resulting spline body as the dummy top surface
- **Endpoint trimming keep logic**: Keep the **larger** body (by bounding box volume). `keepRefPoint` parameter removed.
- **Trim point optionality**: Either start or stop point may be omitted. Guard with `isQueryEmpty`.
- **`opExtractWires` after `opIntersectFaces`**: Intentional — single wire body guarantee for loft profiles.
- **`qCreatedBy(id + "loft1")` after union**: Valid — first tool body survives `opBoolean` UNION.
- **Top trim**: A top surface (projection of side surface top edges onto XZ plane) is created elsewhere in the workflow. The `+2mm` overshoot in `routHeight` is reserved for that future trim step. Do not implement yet.
- **Mirror**: Out of scope for this refactor.
- **Cap geometry**: `opRevolve` on split edges, 90° about `edgeLine.direction` (wire tangent at trim point).
