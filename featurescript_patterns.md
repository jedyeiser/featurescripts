# FeatureScript API Patterns

Verified patterns from std library source and production code in this project.

---

## approximateSpline

**Signature:** `approximateSpline(context is Context, definition is map) returns array`

Returns an **array** of `BSplineCurve` values — one per target. Always index `[0]` when fitting a single curve.

### ApproximationTarget
Wrap point arrays with `approximationTarget({...})` — do NOT pass raw maps.

```featurescript
// Minimal — points only
var curve = approximateSpline(context, {
    "targets"          : [approximationTarget({ "positions" : myPointsArray })],
    "degree"           : 3,
    "tolerance"        : 1e-4 * meter,
    "isPeriodic"       : false,
    "maxControlPoints" : 200
})[0];  // [0] — returns array, extract first
```

```featurescript
// With endpoint derivatives (e.g. to preserve tangency)
var target = approximationTarget({
    "positions"      : myPointsArray,
    "startDerivative": startTangentDirection,  // unitless Vector
    "endDerivative"  : endTangentDirection
});
var curve = approximateSpline(context, {
    "targets"          : [target],
    "degree"           : 3,
    "tolerance"        : 1e-4 * meter,
    "isPeriodic"       : false,
    "maxControlPoints" : 200,
    "interpolateIndices" : [0, size(myPointsArray) - 1]  // exact at endpoints
})[0];
```

### Key parameters
| Parameter | Type | Notes |
|-----------|------|-------|
| `targets` | array | Each element must be `approximationTarget({...})`, not a raw map |
| `degree` | number | Desired degree; output may differ if too few points |
| `tolerance` | ValueWithUnits | Min 1e-8 m. Use 1e-4 m for reference geometry, 1e-5 m for precise fits |
| `isPeriodic` | boolean | Set `path.closed` for wire-derived curves |
| `maxControlPoints` | number | Default 10000. Set explicitly to prevent runaway fits |
| `interpolateIndices` | array | Indices to interpolate exactly. Non-periodic only. Use `[0, size(pts)-1]` for endpoints |

**Reference:** `approximationUtils.fs` lines 148–168; `wrapAndLoft.fs` lines 363–374, 617–625

---

## opCreateBSplineCurve

Creates a wire body from a `BSplineCurve` value returned by `approximateSpline` or constructed directly.

```featurescript
opCreateBSplineCurve(context, id + "myCurve", {
    "bSplineCurve" : splineCurve   // BSplineCurve value from approximateSpline(...)[0]
});

var curveEdge = qCreatedBy(id + "myCurve", EntityType.EDGE);
var curveBody = qCreatedBy(id + "myCurve", EntityType.BODY);
```

**Reference:** `geomOperations.fs` lines 188–195; `wrapAndLoft.fs` lines 373–376

---

## evaluateSpline

**Signature:** `evaluateSpline(definition is map) returns array`
Note: no `context` argument.

Returns nested array: `result[derivativeOrder][parameterIndex]`

```featurescript
// Single point at parameter t
var pt = evaluateSpline({
    "spline"     : myCurve,         // BSplineCurve
    "parameters" : [t]              // must be array
})[0][0];                           // [0]=positions, [0]=first param → Vector

// Multiple points
var positions = evaluateSpline({
    "spline"     : myCurve,
    "parameters" : [0, 0.25, 0.5, 0.75, 1.0]
})[0];                              // array of Vectors

// With first derivative
var result = evaluateSpline({
    "spline"       : myCurve,
    "parameters"   : [t],
    "nDerivatives" : 1
});
var pt      = result[0][0];        // position
var tangent = result[1][0];        // first derivative (not normalized)
```

**Reference:** corrections log 2026-03-01; `std/splineUtils.fs`

---

## approximateSpline — common mistakes

| Mistake | Fix |
|---------|-----|
| `approximateSpline({ "points": pts, ... })` — no context, wrong key | `approximateSpline(context, { "targets": [approximationTarget({"positions": pts})], ... })[0]` |
| Raw map in targets: `"targets": [{"positions": pts}]` | Must use `approximationTarget({...})` constructor |
| `"closed": false` | Correct key is `"isPeriodic": false` |
| Using result directly as BSplineCurve | Returns array — always index `[0]` |
| Missing `"tolerance"` | Required field — min 1e-8 m |
