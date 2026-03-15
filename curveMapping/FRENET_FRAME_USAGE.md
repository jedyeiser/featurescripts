# Frenet Frame Conventions in curveMappingCore

A field guide for anyone writing features that consume `getFrameAtArcLength`.

---

## Documented vs. empirical axis directions

The `buildFrenetPath` / `getFrameAtArcLength` pipeline documents:

| Field | Documented meaning |
|---|---|
| `frame.xAxis` | Principal normal (toward center of curvature) |
| `yAxis(frame)` | Binormal |
| `frame.zAxis` | Tangent |

`debugDrawFrames` draws: **RED = xAxis**, **GREEN = yAxis(frame)**, **BLUE = zAxis**.

**However**, empirical testing in `offsetEdges` revealed the visual mapping is swapped:

| Field | Empirical visual direction |
|---|---|
| `frame.xAxis` | Visually appears as the **binormal** (GREEN-like) |
| `yAxis(frame)` | Visually appears as the **normal** (RED-like) |

### Root cause (hypothesis)

`buildFrenetPath` uses `computeFrenetFrame` (from `tools/frenet.fs`) for junction
continuity detection. That function computes:

```featurescript
binormal = normalize(cross(velocity, acceleration));   // = B  (correct)
normal   = cross(binormal, tangent);                   // = cross(B, T) = -N  (negated!)
var frame = coordSystem(position, normal, tangent);    // xAxis = -N
```

So `computeFrenetFrame.frame.xAxis = -N`, not `+N`.

`getFrameAtArcLength` evaluates the actual frame via `evEdgeCurvature`, which returns
`xAxis = +N`. The cumulative sign tracking (`startSign`) is seeded from the
`computeFrenetFrame`-based continuity check (`-N` convention). When applied to the
`+N` frames from `evEdgeCurvature`, the effective xAxis ends up pointing in the
direction that empirically looks like the binormal.

The mismatch is geometry-dependent. For the reference edge sets tested so far the
visual swap is consistent and always present.

---

## Correct usage pattern

When using `getFrameAtArcLength` to construct offset points, **use the empirical
mapping**, not the documented one:

```featurescript
var fr   = getFrameAtArcLength(context, frenetPath, arcLength);

// "Normal" offset  → yAxis(frame)  (empirically the visual normal direction)
// "Binormal" offset → frame.xAxis  (empirically the visual binormal direction)
// Tangent direction → frame.zAxis  (correct as documented)

var nDir = flipNormal   ? -yAxis(fr.frame) : yAxis(fr.frame);
var bDir = flipBinormal ? -fr.frame.xAxis  : fr.frame.xAxis;

var offsetPoint = fr.frame.origin + normalOff * nDir + binormalOff * bDir;
```

---

## Debug frame display

`debugDrawFrames` draws the **raw** frame axes (no flip flags applied). If your
feature has `flipNormal` / `flipBinormal` toggles, draw frames inline instead so
the arrows show the actual offset directions:

```featurescript
// RED   = direction normalOff will travel
// GREEN = direction binormalOff will travel
// BLUE  = tangent (unchanged)
var nD = flipNormal   ? -yAxis(fr.frame) : yAxis(fr.frame);
var bD = flipBinormal ? -fr.frame.xAxis  : fr.frame.xAxis;

addDebugArrow(context, origin, origin + len * nD,             rad,        DebugColor.RED);
addDebugArrow(context, origin, origin + len * bD,             rad * 0.67, DebugColor.GREEN);
addDebugArrow(context, origin, origin + len * fr.frame.zAxis, rad * 0.5,  DebugColor.BLUE);
```

---

## Summary

- Do not rely on `frame.xAxis = normal` when building offset geometry. Trust
  empirical testing over the docstring.
- `yAxis(cSys)` is defined as `cross(cSys.zAxis, cSys.xAxis)` in `std/coordSystem.fs`.
- The swap does not affect the curve-mapping (wrap/loft) pipeline, which round-trips
  through `worldPointToFrenet` → `toFrameResult` and is internally consistent.
- Only features that directly multiply an offset scalar by a frame axis are affected.

---

*Discovered and documented during `offsetEdges` development, 2026-03-14.*
