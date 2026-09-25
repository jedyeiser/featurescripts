# Unwrap v1 -- chart accuracy, speed, fitting (research 2026-09-24)

Subject: `driven_offset/unwrap.fs` (v1) and its chart in `edge_offset_utils.fs`
(`buildAlongReference`, `referenceSurfaceCoords`, `referencePointAtArc`, `referenceBasisAtArc`,
`alongCoordinate`, `unwrapChart/unwrapCoords/unwrapPoint`), fitting in `curve_tools/curve_core.fs`.
Test case: U1 in "Unwrap_Testing Copy 1" (Wrapped_profile RjRP, 3 splines, over FULL_BASELINE RjRL,
6 edges: 4 splines + arcs R406 / R900). Requirement: speed first; 0.01 mm accuracy is critical.

Nothing was pushed or changed in Onshape. Scratch scripts: `scratchpad/perf/` of session 51e06872
(geom.py / model.py = exact geometry + FS-table replica, exp1/exp2 = chart error, prof/pieces =
unwrapped RjRP, mkfit2/evalfit2 = approximateSpline through the eval API, bench_template.fs +
bench.py = FS timing through the eval API).

## 0. Summary

| Question | Measured | Verdict |
|---|---|---|
| Chart map error, U1 real points (heights 5.9..16 mm) | 0.37 um | OK |
| Chart map error, synthetic, h 0..10 mm, delta 0 / 5 mm | 0.44 / 0.51 um max | OK (under 1 um), but no margin for bigger heights / offsets |
| Chart map error after the fixes in section 1.3 | 0.005 um | 100x margin |
| FS cost per unwrapped point, current code | 5.4 ms | about 80 % of U1's time |
| FS cost per point, packed-table evaluator (section 2.3) | 0.117 ms | 46x faster, agrees with current code to 0.38 um (the current code's own error) |
| U1 edge 0 fit as shipped (tolerance 0.01 mm, 15 CPs) | 9.77 mm off at its parameters, **125 um off the true curve** | FAILS. Two causes: a bug, and too few CPs |
| Bug: end derivatives scaled by chord TWICE (unwrap.fs:461) | fitter can never meet tolerance, always runs to the cap | fix: pass unit tangents |
| Fixed, still one curve, 15 CPs | 108 um | fails |
| Fixed, one curve, 80 CPs | 2.4 um | passes, heavy |
| Fixed, **split at reference curvature breaks, joined back into one C1 B-spline** | **43 CPs, 2.7 um** | recommended |
| U1 edge 2 (tip) emitted as a line | true curve bows 8.9 um off the line | uses 89 % of the budget; see 3.4 |

## 1. Accuracy of the chart map

### 1.1 Method

`geom.py` rebuilds both wires from their `evCurveDefinition` (B-splines through scipy, arcs
analytically) with an exact arc-length parameterization. Checked against 401 kernel samples per
edge: position within 5 nm on every RjRL edge, lengths within 1e-8 m of `evLength`, curvature
within 1e-6 relative. Kernel edge parameters are arc length to 5 nm on RjRL (0.27 um on RjRP
edge 0 -- the kernel's own arc-length approximation; relevant to U2, whose reference is RjRP).

"Exact" unwrap: Newton foot on the true curve, exact turning angle (atan2 of the true tangent),
`x = s - delta * theta`, `y = v`, `z = height`, all relative to the alignment point (0.885, 0, 0).
"Table" unwrap: line-by-line replica of `sampleTurning` (25 per edge, each edge its own curvature at
the duplicate join samples), trapezoid theta, `hermiteAt` seed in X, Hermite position with
`referenceRate` slopes, linear-renormalized tangent, linear curvature, linear theta, the same Newton
(tolerance 1e-10 m, fallback slope, 8 steps). Test points: every 2 mm along the whole chain at
heights 0, 1, 5, 10 mm off the (offset) reference; plus the real RjRP samples.

### 1.2 Results (max error, micrometres)

| Table | delta | h = 0 | h = 1 mm | h = 5 mm | h = 10 mm | z (all h) |
|---|---|---|---|---|---|---|
| current: 25/edge, linear tangent, linear theta | 0 | 0.014 | 0.042 | 0.216 | 0.444 | 0.140 |
| current | 5 mm | 0.510 | 0.464 | 0.281 | 0.281 | 0.141 |
| 25/edge, Hermite-derivative tangent, angle theta + Hermite | 0 / 5 mm | 0.0002 | 0.008 | 0.042 | 0.085 | 0.140 |
| same, 25 mm spacing (edge 0: 54 samples) | 0 / 5 mm | 0.0001 | 0.0005 | 0.003 | 0.005 | 0.004 |
| current, 13/edge | 5 mm | 1.68 | 1.49 | 0.76 | 0.82 | 0.51 |
| current, 9/edge | 5 mm | 3.85 | 3.44 | 1.80 | 2.05 | 1.82 |

x-error in the current table grows by 0.044 um per mm of height and 0.1 um per mm of preserve-length
offset. Real U1 points: 0.37 um current, 0.14 um with the tangent/theta fixes. y is exact (the
reference is planar). Newton: 1.05..1.6 steps per point from the X seed, never more than 3.

**Verdict:** the chart meets the 1 um map budget on U1 today, with no margin for points 20+ mm off
the reference (plates with thick cores) or larger offsets.

**Weak links, in order:**
1. **Linear tangent** (`interpolateVector` in `referenceBasisAtArc`). Its angle is off by up to
   4.6e-5 rad on the 54 mm spans; the Newton foot is where `P - A` is perpendicular to THAT tangent,
   not to the Hermite curve the position comes from, so x is off by `height * angle error`.
2. **Trapezoid-integrated, linearly interpolated theta.** Only matters for delta != 0; 0.51 um at
   5 mm. (Duplicate join samples with each edge's own curvature are essential -- a replica that
   gave a join sample the neighbour's curvature was off by 58 um at 5 mm. The current code does
   this right; any rewrite must keep it.)
3. **25 samples on a 1.3 m edge** (54 mm spans): Hermite position error 0.14 um (the z column).
   Irrelevant to speed: the table costs one kernel call per edge whatever its size.

Curvature interpolation does NOT affect accuracy: it only enters the Newton slope, and Newton
converges to the same root with an approximate slope.

### 1.3 Fixes (edge_offset_utils.fs)

(a) Tangent = derivative of the Hermite position (consistent foot, 5x less x error). Done inside
the packed evaluator of section 2.3 at no extra cost -- the Hermite basis derivatives are four
multiplies. If the unit-carrying `referenceBasisAtArc` is kept for other callers, the equivalent is:

```
export function referenceTangentAtArc(alongRef is map, arc is ValueWithUnits) returns Vector
{
    const arcs = alongRef.arcs;
    const count = size(arcs);
    if (arc <= arcs[0] || arc >= arcs[count - 1])
    {
        return (arc <= arcs[0]) ? alongRef.tangents[0] : alongRef.tangents[count - 1];
    }
    const i = spanIndex(arcs, arc);
    const span = arcs[i + 1] - arcs[i];
    if (abs(span / meter) < ZERO_SPAN)
    {
        return alongRef.tangents[i];
    }
    const f = (arc - arcs[i]) / span;
    const f2 = f * f;
    const derivative = ((6 * f2 - 6 * f) / span) * (alongRef.points[i] - alongRef.points[i + 1])
        + (3 * f2 - 4 * f + 1) * referenceRate(alongRef, i)
        + (3 * f2 - 2 * f) * referenceRate(alongRef, i + 1);
    return normalize(derivative);
}
```

(b) Theta from the tangents themselves (exact at the samples, no quadrature error), in
`buildAlongReference`, replacing the trapezoid line; then interpolate theta by Hermite with the
curvature as slope (`hermiteAt(alongRef.arcs, alongRef.thetas, curvaturesPerMeter, arc)` -- or in the
packed evaluator, where it is already done):

```
    var previousTangent = undefined;
    for (var sample in samples)
    {
        const offsetDir = cross(planeNormal, sample.tangent);
        const curvature = sample.curvature * dot(sample.towardCentre, offsetDir);

        if (previousTangent != undefined)
        {
            // Exact turning between neighbouring samples: no quadrature error, and a curvature
            // ramp inside an edge's first span cannot be under-counted. Zero across a join.
            const previousNormal = cross(planeNormal, previousTangent);
            theta += atan2(dot(sample.tangent, previousNormal), dot(sample.tangent, previousTangent)) / radian;
        }

        arcs = append(arcs, sample.arc);
        thetas = append(thetas, theta);
        curvatures = append(curvatures, curvature);
        previousTangent = sample.tangent;
    }
```

(c) Samples by length in `sampleTurning` (54 instead of 25 on the 1.3 m edge; still one kernel call):

```
/** At most this far apart along a reference edge (TURNING_SAMPLES stays the minimum). */
export const TURNING_SPACING = 25 * millimeter;
...
            const count = max(TURNING_SAMPLES, ceil(edgeData.length / TURNING_SPACING) + 1);
            const fractions = range(0, 1, count);
            ...
            for (var i = 0; i < count; i += 1)
```

(a)-(c) change `buildAlongReference` for Driven edge offset / surface too (sub-micron; the
Design_Master fingerprint may move in the last digit -- re-baseline, do not "fix").

## 2. Speed

### 2.1 Measured (FS eval API, wall-clock differences, same Part Studio)

| Work | Cost |
|---|---|
| Current `referenceSurfaceCoords` + `alongCoordinate`, per point | **5.4 ms** (273 points: 1.47 s) |
| Packed unitless evaluator with warm start (2.3), per point | **0.117 ms** (273 points: 32 ms) |
| `evLength` + `evEdgeTangentLines` (201 params), per edge | ~5 ms |
| `approximateSpline`, 201 points, any cap 15..100 | ~7 ms |
| classify loops (sagitta + radial), 201 points | ~28 ms |

Why the current map is slow: every Vector and ValueWithUnits operation is an FS-level overload
(`operator+` on Vector loops over elements and each element add is a `ValueWithUnits` operator with a
unit precondition). Per point: 1 X-seed `hermiteAt` + (1 + Newton steps) x (`interpolateVector` +
`interpolate` + `referencePointAtArc`) + `interpolate` for theta = about 9 binary searches of ~8
unit comparisons each, plus ~60 unit-vector operations and a `mergeMaps`.

### 2.2 Where U1's time goes (estimate from the unit costs)

| Stage | Calls | Est. |
|---|---|---|
| `unwrapChart`: `buildChain` (constructPaths, 3 kernel calls/edge, arcLengthAtX secant) + `sampleTurning` (1/edge) | ~25 kernel calls | 0.1-0.2 s |
| map: 273 samples + 6 finite-difference tangent points | 279 x 5.4 ms | **1.5 s** |
| per edge `evLength`, `evEdgeTangentLines` | 6 | 0.03 s |
| `classifyPoints` | 3 edges | 0.05 s |
| fits + opCreateBSplineCurve + extract/delete | | ~0.1 s |

The map is ~80 % of U1. U2 (plate) is worse: `unwrapChart` runs twice (probe at delta 0, then the
offset chart -- the whole chain is re-described and re-sampled), `plateSides` maps up to 16 samples
of every face (36 candidates printed, ~450 map calls = ~2.4 s), then every outline edge is mapped.

### 2.3 Optimisations, in order of payoff

1. **Packed unitless evaluator + warm start** (46x on the map, measured). Pack the tables once
   per chart into plain-number arrays (metres, radians); one span lookup per evaluation, walked
   from the previous answer instead of a binary search; Hermite position, its derivative as the
   tangent, Hermite theta, linear curvature, all from the same span. Seed Newton from the previous
   sample's arc (one `referenceArcAtX` per edge). Includes fix 1.3(a) and the Hermite-theta half of
   1.3(b).

```
/** Newton on the foot stops below this residual, metres (REFERENCE_FOOT_TOL as a number). */
const CHART_FOOT_TOL = 1e-10;

/**
 * The chart's tables as plain numbers (metres, radians, 1/m): unit arithmetic is FS-level operator
 * overloading and costs ~40x plain arithmetic in the inner loop. Rates are d(offset point)/d(wire arc).
 */
export function packChart(alongRef is map) returns map
{
    const count = size(alongRef.arcs);
    var arcs = makeArray(count);
    var px = makeArray(count);
    var py = makeArray(count);
    var pz = makeArray(count);
    var rx = makeArray(count);
    var ry = makeArray(count);
    var rz = makeArray(count);
    var kappa = makeArray(count);
    var theta = makeArray(count);
    for (var i = 0; i < count; i += 1)
    {
        const scale = 1 - alongRef.delta * alongRef.curvatures[i];
        arcs[i] = alongRef.arcs[i].value;
        px[i] = alongRef.points[i][0].value;
        py[i] = alongRef.points[i][1].value;
        pz[i] = alongRef.points[i][2].value;
        rx[i] = scale * alongRef.tangents[i][0];
        ry[i] = scale * alongRef.tangents[i][1];
        rz[i] = scale * alongRef.tangents[i][2];
        kappa[i] = alongRef.curvatures[i].value;
        theta[i] = alongRef.thetas[i];
    }
    return { "count" : count, "arcs" : arcs, "px" : px, "py" : py, "pz" : pz, "rx" : rx, "ry" : ry, "rz" : rz,
            "kappa" : kappa, "theta" : theta, "delta" : alongRef.delta.value,
            "nx" : alongRef.planeNormal[0], "ny" : alongRef.planeNormal[1], "nz" : alongRef.planeNormal[2] };
}

/** The span holding arc a, walked from the previous span. Never lands on a zero-length join span. */
function chartSpan(c is map, a is number, hint is number) returns number
{
    var i = min(max(hint, 0), c.count - 2);
    while (i < c.count - 2 && a > c.arcs[i + 1])
    {
        i += 1;
    }
    while (i > 0 && a <= c.arcs[i])
    {
        i -= 1;
    }
    return i;
}

/**
 * Chart at arc a in span i: [ax, ay, az, tx, ty, tz, theta, kappa, scale]. The tangent is the
 * derivative of the same Hermite cubic as the position, so the foot is the foot of THIS curve.
 * Past either end the reference runs on straight (position linear, theta and tangent held).
 */
function chartEval(c is map, a is number, i is number) returns array
{
    const last = c.count - 1;
    if (a <= c.arcs[0] || a >= c.arcs[last])
    {
        const j = (a <= c.arcs[0]) ? 0 : last;
        const d = a - c.arcs[j];
        const s = sqrt(c.rx[j] * c.rx[j] + c.ry[j] * c.ry[j] + c.rz[j] * c.rz[j]);
        return [c.px[j] + d * c.rx[j], c.py[j] + d * c.ry[j], c.pz[j] + d * c.rz[j],
                c.rx[j] / s, c.ry[j] / s, c.rz[j] / s, c.theta[j], 0, s];
    }
    const k = i + 1;
    const h = c.arcs[k] - c.arcs[i];
    const f = (a - c.arcs[i]) / h;
    const f2 = f * f;
    const f3 = f2 * f;
    const h00 = 2 * f3 - 3 * f2 + 1;
    const h10 = (f3 - 2 * f2 + f) * h;
    const h01 = 3 * f2 - 2 * f3;
    const h11 = (f3 - f2) * h;
    const g0 = (6 * f2 - 6 * f) / h;
    const g10 = 3 * f2 - 4 * f + 1;
    const g11 = 3 * f2 - 2 * f;
    const dx = g0 * (c.px[i] - c.px[k]) + g10 * c.rx[i] + g11 * c.rx[k];
    const dy = g0 * (c.py[i] - c.py[k]) + g10 * c.ry[i] + g11 * c.ry[k];
    const dz = g0 * (c.pz[i] - c.pz[k]) + g10 * c.rz[i] + g11 * c.rz[k];
    const s = sqrt(dx * dx + dy * dy + dz * dz);
    return [h00 * c.px[i] + h10 * c.rx[i] + h01 * c.px[k] + h11 * c.rx[k],
            h00 * c.py[i] + h10 * c.ry[i] + h01 * c.py[k] + h11 * c.ry[k],
            h00 * c.pz[i] + h10 * c.rz[i] + h01 * c.pz[k] + h11 * c.rz[k],
            dx / s, dy / s, dz / s,
            h00 * c.theta[i] + h10 * c.kappa[i] + h01 * c.theta[k] + h11 * c.kappa[k],
            c.kappa[i] + (c.kappa[k] - c.kappa[i]) * f,
            s];
}

/**
 * Foot of point (qx, qy, qz) on the packed chart, Newton on dot(Q - A(a), t(a)) = 0 with slope
 * scale - kappa * height (referenceSurfaceCoords' walk, plain numbers).
 * @returns {array} : [arc, span, v, height, theta, tx, ty, tz, kappa, scale]
 */
export function chartFoot(c is map, qx is number, qy is number, qz is number, a0 is number, hint is number) returns array
{
    var a = a0;
    var i = hint;
    var result = undefined;
    for (var step = 0; step <= REFERENCE_FOOT_STEPS; step += 1)
    {
        i = chartSpan(c, a, i);
        const e = chartEval(c, a, i);
        const dx = qx - e[0];
        const dy = qy - e[1];
        const dz = qz - e[2];
        const nx = c.ny * e[5] - c.nz * e[4];
        const ny = c.nz * e[3] - c.nx * e[5];
        const nz = c.nx * e[4] - c.ny * e[3];
        const residual = dx * e[3] + dy * e[4] + dz * e[5];
        const height = dx * nx + dy * ny + dz * nz;
        if (abs(residual) < CHART_FOOT_TOL || step == REFERENCE_FOOT_STEPS)
        {
            result = [a, i, dx * c.nx + dy * c.ny + dz * c.nz, height, e[6], e[3], e[4], e[5], e[7], e[8]];
            break;
        }
        var slope = e[8] - e[7] * height;
        if (slope < 1e-3)
        {
            slope = e[8];
        }
        a = a + residual / slope;
    }
    return result;
}
```

   `unwrapChart` then packs once and stores the alignment point's foot as numbers:

```
export function unwrapChart(context is Context, selection is Query, alignPoint is Vector, delta is ValueWithUnits) returns map
{
    return chartFromReference(buildAlongReference(context, selection, alignPoint, delta), alignPoint);
}

export function chartFromReference(alongRef is map, alignPoint is Vector) returns map
{
    const packed = packChart(alongRef);
    const seed = referenceArcAtX(alongRef, alignPoint[0]).value;
    const f = chartFoot(packed, alignPoint[0].value, alignPoint[1].value, alignPoint[2].value, seed, 0);
    return {
        "alongRef" : alongRef,
        "packed" : packed,
        "align" : { "arc" : f[0] * meter, "v" : f[2] * meter, "height" : f[3] * meter },
        "alignCoord" : (f[0] - packed.delta * f[4]) * meter,
        "alignX" : f[0] - packed.delta * f[4],
        "alignV" : f[2],
        "alignHeight" : f[3]
    };
}

/**
 * Chart coordinates of a wrapped point in plain metres, plus its foot for the next call.
 * @param previous : undefined (seed from X), or the previous result along the same edge (warm start).
 * @returns {array} : [x, y, z, arc, span, tx, ty, tz, kappa, scale, height]
 */
export function unwrapFast(chart is map, point is Vector, previous) returns array
{
    const c = chart.packed;
    const a0 = (previous == undefined) ? referenceArcAtX(chart.alongRef, point[0]).value : previous[3];
    const i0 = (previous == undefined) ? 0 : previous[4];
    const f = chartFoot(c, point[0].value, point[1].value, point[2].value, a0, i0);
    return [f[0] - c.delta * f[4] - chart.alignX, chart.alignV - f[2], f[3] - chart.alignHeight,
            f[0], f[1], f[5], f[6], f[7], f[8], f[9], f[3]];
}

/**
 * Unwrapped direction of a wrapped unit direction d at a point whose unwrapFast result is u.
 * x = arc - delta * theta, d(arc) = dot(d, t) / (scale - kappa * height), d(x) = scale * d(arc).
 * Exact; replaces the two extra map calls and the 10 um finite difference per edge.
 */
export function unwrapDirection(chart is map, cs is CoordSystem, u is array, d is Vector) returns Vector
{
    const c = chart.packed;
    const nx = c.ny * u[7] - c.nz * u[6];
    const ny = c.nz * u[5] - c.nx * u[7];
    const nz = c.nx * u[6] - c.ny * u[5];
    const along = u[9] * (d[0] * u[5] + d[1] * u[6] + d[2] * u[7]) / (u[9] - u[8] * u[10]);
    const across = -(d[0] * c.nx + d[1] * c.ny + d[2] * c.nz);
    const up = d[0] * nx + d[1] * ny + d[2] * nz;
    return normalize(along * cs.xAxis + across * cross(cs.zAxis, cs.xAxis) + up * cs.zAxis);
}
```

   (Analytic direction checked against a 1e-7 m finite difference of the exact map: agrees to 1e-10.)

2. **unwrapEdges loop** -- warm start, one world conversion per point, analytic end tangents:

```
        var points = [];
        var feet = [];
        var previous = undefined;
        const yAxis = cross(cs.zAxis, cs.xAxis);
        for (var tl in tangentLines)
        {
            const u = unwrapFast(chart, tl.origin, previous);
            previous = u;
            var z = u[2] + zShift.value;
            if (flatZ != undefined)
            {
                const off = z - flatZ.value;
                worstFlat = max(worstFlat, abs(off) * meter);
                z = flatZ.value;
            }
            points = append(points, cs.origin + (u[0] * meter) * cs.xAxis + (u[1] * meter) * yAxis + (z * meter) * cs.zAxis);
            feet = append(feet, u);
        }
        const startTangent = flattened(cs, flatZ, unwrapDirection(chart, cs, feet[0], tangentLines[0].direction));
        const endTangent = flattened(cs, flatZ, unwrapDirection(chart, cs, feet[count - 1], tangentLines[count - 1].direction));
```

   (`unwrapPoint` is equivalent to this with z = height - align height + zShift; keep it for one-off
   callers.) Expected U1: map 1.5 s -> ~35 ms; whole feature ~1.9 s -> ~0.35 s (estimate; the rest is
   kernel calls and ops).

3. **Plate: sample the reference once.** Split `buildAlongReference` into
   `referenceSamples(context, selection, zeroPoint)` (buildChain + sampleTurning +
   referencePlaneNormal -- all the kernel work) and `alongReferenceFromSamples(sampled, zeroPoint,
   delta, selection)` (pure arithmetic). `unwrapPlate` builds the probe and the offset chart from the
   same samples: saves ~25 kernel calls per plate. The probe's `plateSides` loop uses `unwrapFast`
   with the previous face sample as the warm start (same face, neighbouring grid points).

4. **plateSides**: test the 2x2 inner grid points first and only sample the full 4x4 grid on faces
   that pass; side walls already bail on their first sample.

5. Minor, not worth doing before 1-3: `classifyPoints` (~28 ms per 201-point edge) could skip the
   radial loop when `allowArc` is false; `evLength` per edge (~2 ms) could be summed from the chain.
   Fewer unwrap samples would save little after (1) and costs fit quality -- keep 5 mm spacing.

## 3. Fitting accuracy

Defaults in play: `drivenOffsetApproximationPredicate` = std `DEGREE_BOUND` (default 3),
`TOLERANCE_BOUND` (default 1e-5 m = **0.01 mm** -- the whole budget), `DrivenOffsetMaxCPBounds`
(default **15**). U1 and U2 both carry 3 / 0.01 mm / 15. The same tolerance also decides line / arc
recognition.

Method: the exact unwrapped points of RjRP edge 0 (201 samples, exactly as unwrap.fs samples them)
and exact end tangents were fitted by `approximateSpline` through the eval API with the same
chord-fraction parameters and derivative scaling as `approximateFamily`; each fit was measured
against 4001 densely sampled exact unwrapped points (true normal distance).

### 3.1 Bug: end derivatives scaled by the chord twice

unwrap.fs:461 passes `chord * startTangent`, and `approximateFamily` multiplies by the polyline
chord again (every other caller passes unit tangents). The end derivative is 1.48 x too long (and in
m^2). Reproduced exactly: the live WARNING "reached the cap of 15 control points **9.7673 mm** from
its points" is this -- the fitter's error is measured at the parameters, and with the wrong end speed
the points slide along the curve, so it can never meet tolerance and always runs to the cap. True
shape error 125 um. With the bug, even 80/100 CPs report 1.7/1.3 mm "error".

```
-            emitSplineCurve(context, edgeId, points, chord * startTangent, chord * endTangent, settings.approximation);
+            emitSplineCurve(context, edgeId, points, startTangent, endTangent, settings.approximation);
```

(and drop the now-unused `chord`).

### 3.2 Why 15 CPs cannot fit it

| Fit of U1 edge 0 (bug fixed) | CPs | Error at samples | Error vs true curve |
|---|---|---|---|
| deg 3, 0.01 mm, cap 15 | 15 | 108.3 um | 108.5 um |
| deg 3, cap 50 | 50 | 7.8 um | 9.1 um |
| deg 3, 5 um, cap 60 | 60 | 4.5 um | 5.0 um |
| deg 3, 5 um, cap 80 | 78 | 2.2 um | 2.4 um |
| deg 3, 1 um, cap 100 | 100 | 1.6 um | 1.8 um |
| deg 5, cap 60 | 60 | 4.1 um | 4.5 um |

The unwrapped curve's curvature is roughly the source's minus the reference's, so it inherits every
**curvature jump of the reference at its edge joins** -- RjRL is only G1: kappa 1.11 -> 0.40 ->
0.004 | 0.002 -> 0.59 -> 2.46 -> 3.94 /m. Source edge 0 crosses two of them (x = 195 and 1495 mm):
the unwrapped curve's curvature jumps by ~0.4 and ~0.59 /m there, which a C2 cubic with uniform-ish
knots can only chase with many control points.

### 3.3 Fix: split at the reference's curvature breaks, join back into one curve

Fit the pieces between breaks separately (each smooth), then concatenate them into ONE B-spline:

| U1 edge 0 | CPs | Error vs true curve |
|---|---|---|
| piece 0 (129.5 mm, 27 samples, 5 um) | 6 | 0.04 um |
| piece 1 (1300.5 mm, 201 samples, 5 um, cap 40) | 33 | 2.7 um |
| piece 2 (49.9 mm, 17 samples) | 6 | 0.00 um |
| **joined** | **43** | **2.74 um** |

Joined curve: knots of multiplicity `degree` at the joins. It is C1 anyway: each piece's end
derivative is (unit tangent x piece chord) over a parameter width of (piece chord / total chord),
so in the joined parameter it is (unit tangent x total chord) on both sides. Measured: derivative
matches across the joins to the precision of the tangents supplied (exact when both pieces get the
same analytic tangent at the shared point). One edge per source edge, as today.

Procedure in `unwrapEdges`, freeform branch only (lines/arcs never need it):
1. Breaks = packed-table join arcs where the curvature jumps: `arcs[i] == arcs[i + 1]` and
   `abs(kappa[i] - kappa[i + 1]) > 1e-3` (1/m). Store them in `packChart` as `breaks`.
2. After the first sampling pass, for each sample span whose foot arcs straddle a break, estimate
   the source parameter linearly, evaluate all estimates in ONE `evEdgeTangentLines` call, one
   secant correction, a second call (2 kernel calls per edge that has breaks, none otherwise).
3. Re-sample each piece at the same 5 mm rule (min 9 per piece is plenty), in one kernel call;
   the break point is shared by both neighbours; the tangent there is `unwrapDirection`, identical
   on both sides.
4. Fit each piece with `approximateFamily` (unit end tangents), snap ends, join:

```
/**
 * One B-spline from pieces fitted over chord-fraction parameters with unit-tangent * chord end
 * derivatives (approximateFamily): C1 at the joins by construction, see research_unwrap_perf.md 3.3.
 * @param pieces {array} : BSplineCurves of one degree, knots on [0, 1], ends snapped so piece j's last
 *      control point IS piece j + 1's first.
 * @param chords {array} : each piece's polyline chord (what approximateFamily scaled its derivatives by).
 */
function joinFits(pieces is array, chords is array) returns BSplineCurve
{
    const degree = pieces[0].degree;
    var total = 0 * meter;
    for (var c in chords)
    {
        total += c;
    }
    var knots = makeArray(degree + 1, 0);
    var controlPoints = [];
    var start = 0;
    for (var j = 0; j < size(pieces); j += 1)
    {
        const width = chords[j] / total;
        const pieceKnots = pieces[j].knots;
        for (var m = degree + 1; m < size(pieceKnots) - degree - 1; m += 1)
        {
            knots = append(knots, start + pieceKnots[m] * width);
        }
        start += width;
        const isLast = j == size(pieces) - 1;
        for (var r = 0; r < (isLast ? degree + 1 : degree); r += 1)
        {
            knots = append(knots, isLast ? 1 : start);
        }
        const cps = pieces[j].controlPoints;
        controlPoints = concatenateArrays([controlPoints, (j == 0) ? cps : subArray(cps, 1)]);
    }
    return bSplineCurve({ "degree" : degree, "isPeriodic" : false, "controlPoints" : controlPoints, "knots" : knots });
}
```

   If the fitter returns pieces of different degree (only for very few points), emit the pieces as
   separate curves instead (they still extract into one wire). Put `joinFits` in unwrap.fs (or
   edge_offset_utils.fs), not curve_core: curve_core changes need a Curve_tools version + re-pin.

Without the split, the fallback is simply a higher cap: 80 CPs at 5 um gives 2.4 um. Fitting time
does not change with the cap (~7 ms per fit), only curve weight does.

### 3.4 Lines and arcs share the fit tolerance

U1 edge 2 (tip, heights 5.941..5.956 mm off the reference) unwraps to a curve that bows **8.9 um**
off its chord and is emitted as a line at 0.01 mm tolerance -- 89 % of the budget in recognition
alone. (Edge 1, the tail, is a true line: 0.2 um.) At 5 um it becomes a 6-CP spline, exact.
The end-tangent gate `G1_JUNCTION_ANGLE` (1e-2 rad, 0.57 deg) is also loose for this purpose; the
tip line's real end-tangent miss is 2e-4 rad, so 1e-3 rad would keep the lines that are lines.

### 3.5 Recommended defaults (Unwrap only)

- Degree 3 (4/5 bought little: 1.4 um vs 2.5 um at 40 CPs on the long piece).
- Tolerance **0.005 mm**; with the map at <= 0.01 um after section 1.3 and <= 0.5 um between
  samples measured, total <= ~5.5 um against the 10 um requirement.
- Max control points **60** (per piece when splitting; U1 needs 33 on the longest piece).
- Give Unwrap its own predicate with these defaults and the SAME parameter ids
  (`approximationDegree`, `approximationTolerance`, `approximationMaxCPs`) instead of changing
  `drivenOffsetApproximationPredicate`, which other features share. Saved U1/U2 keep 3 / 0.01 / 15
  (an existing parameter's default is not migrated into saved features), so set those two by hand.

## 4. Order of work

1. unwrap.fs:461 unit tangents (one line, fixes the 9.77 mm warning and the cap saturation).
2. Packed evaluator + warm start + analytic end tangents (speed x46 on the map; accuracy 0.37 -> 0.14 um).
3. Split-and-join fitting + Unwrap defaults 5 um / 60 (125 um -> 2.7 um on U1 edge 0).
4. Theta by tangent angle + samples by length in `buildAlongReference` (map -> 0.005 um; shared with
   the offset features, re-baseline the fingerprint).
5. Plate: sample the reference once; 2x2 pre-test in plateSides.
