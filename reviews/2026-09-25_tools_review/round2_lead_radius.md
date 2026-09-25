## Footprint radius definitions: why you see "average 17 m, natural 14 m"

### Bottom line
- **Summary:** a 17 m average next to a 14 m natural radius is usually expected, and there is also a real bug in how the average is computed.
  - **Why the split is expected:** the natural radius comes from 3 points and mostly reflects the middle of the sidecut. Any sidecut whose flanks are flatter than its centre will give a bigger average. A synthetic arc chain (14 m core, 25 m flanks) gives an average of exactly 17.0 m and a natural radius of 14.5 m.
  - **The bug:** the code does not compute the distance-averaged radius. It takes 50 samples per B-spline edge, spaced evenly in parameter, and averages them without weighting by distance. So the answer depends on how the sidecut is split into edges and where the samples fall.
- **The definition itself has a problem.** At a true inflection the curvature goes to 0, so the radius goes to infinity. For any smooth (G2) sidecut, the distance-averaged radius between inflection points is mathematically infinite. The integral grows like log(1/distance-to-inflection). The code returns a finite number only because of where the last sample happens to land. It is well defined only when the inflection is a junction where curvature jumps from one sign to the other (for example an arc-to-reverse-arc chain).
- **Recommendation:** make "average radius" = arc length / total turning angle between the inflections, L/Δθ.
  - This is the same as distance-averaging the curvature and inverting it. It is also the radius of the single arc with the same length and the same total turn.
  - It is finite, gives exactly R for a pure arc, is barely affected near inflections, and needs no sampling: Δθ is just the angle between the tangents at the two inflection points.
  - Scale Footprint should use the same library function.

### 1. How each radius is computed today (verified in code)

| Output | Where | Formula |
|---|---|---|
| Average radius (Analyze) | `fpt_analyze.fs:843-876`, called at `:1020-1023` | `mean(1/|k_i|)` over samples. For each B-spline edge that overlaps the interval, 50 samples evenly spaced in **parameter** (`sampleBSplineWithCurvature` `:414-439`, `paramBracketSamples`=50). Keeps samples with x in [xInflMin − 0.001 mm, xInflMax + 0.001 mm]. Drops `|k| ≤ 1e-9 /m`. **No arc-length weighting.** Uses `curvatureMag`, the absolute value, so the "only averages where curvature is positive" description at `predicates.fs:31` is not what the code does. |
| Interval | `fpt_analyze.fs:988-1014` | FB and AB inflections from `findInflectionPoint`, searching from the widest point inward toward the waist and taking the first sign change. If no inflection is found, it falls back to the widest point. The interval is not tied to FCP/ACP. The waist is searched within MRS ± 20 % of the curve's X extent (`:944`). |
| Natural radius, widest | `:1017`, `arcThroughThreePoints` `:820-837` | Circumradius of the circle through FB widest point, waist and AB widest point. |
| Natural radius, inflection | `:1018` | Circumradius through FB inflection, waist and AB inflection. |
| Curvature | `footprint_math.fs:31-73` | `(x'y''−y'x'')/|r'|³` from `evaluateSpline` derivatives. The sign is 0 only when `|k| < 1e-12`. |
| Scale Footprint, reference radius | `scaleFootprint.fs:848-860` | Same library `computeAverageRadius`, but run on the categorized and contact-split `sidecutCurves`, which are a different edge partition from Analyze's. If nothing is found it falls back to **1000 m** (`:860`). |
| Scale Radius, convergence check | `scaleFootprint.fs:1651-1683`, used at `:1920` | A local copy: 50 parameter-uniform samples on **one** temporary curve (`buildSingleCurveFromPoints` `:1530`, max 30 control points), strict x bounds. Inflections come from `findInflectionInTempCurve` (`:1557`), which assumes the ski is centred on X=0 (`:1582`). Converges to 1 cm (`:1765`). The output is then split back into the original edges (`:1942+`). |

- **Near-zero curvature:** the only guard is `|k| > 1e-9 /m`, which lets through radii up to 10⁹ m. Samples near an inflection go straight into the mean.
- **Rational arcs** (`fpt_analyze.fs:77`): `edgesToBSplines` does not pass `forceNonRational`, and Correction 39 says `evaluateSpline` drops the weights.
  - I computed the effect: a quadratic arc with R = 14 m and a 1.1 m sweep (4.5°), evaluated without its weights, has a local radius between 13.989 and 14.022 m, a mean of exactly 14.000 m, and a position error of 4 µm.
  - **So this is not the cause of the 17 vs 14.** It matters only for large sweeps, such as a 90° arc, where the error is 30 %. Adding `"forceNonRational": true`, as `arcFit.fs:72` already does, is still the right fix.
  - Unverified: whether `evApproximateBSplineCurve` actually returns a rational curve for Circle edges when the flag is absent.
- **Inferred side issues:**
  - An exactly straight edge between arcs has sign 0. `findInflectionPoint` then sets `prevSample` to a sign-0 sample (`:807`), so an arc–line–reverse-arc inflection is never found, and the interval falls back to the widest points.
  - For a junction-type inflection, the outer curve's endpoint sample falls inside the 0.001 mm tolerance and adds one reverse-curvature |k| to the mean. This is a small effect.

### 2. How the candidate definitions compare mathematically (same interval, from the waist out to inflection points ±a)
- **(a) Distance-averaged radius, (1/L)∫R ds.** It weights the flat ends most heavily. If k → 0 linearly at the inflection, the 1/k term makes the integral diverge logarithmically, so the result depends on how close the nearest sample is to the inflection. It is finite only when curvature jumps at the inflection.
- **(b) L/Δθ = 1/mean_s(k).** This is the harmonic mean of R over distance. Near-inflection regions contribute k ≈ 0, which is harmless. It is exact for a single arc.
- **(c) 3-point circumradius.** In the small-slope limit, for a symmetric sidecut, R₃ ≈ a² / (2∫₀ᵃ (a−x)k dx). That is a harmonic mean weighted toward the waist, so it reads lowest when curvature is concentrated in the middle. The widest-point version also takes in the reverse-curvature zone, so it always reads higher than the inflection version.
- **(d) Least-squares circle fit.** It behaves close to (c), a little lower.
- **Ordering for a sidecut that is tightest at the waist:** (d) ≈ (c) < (b) < (a).

### 3. Synthetic sidecuts (Python; exact arc-length integration; inflections at ±550 mm; widest points about ±690 mm; sidecut depth 11-14 mm per side, 22-28 mm across both sides)

| Case | (a) continuous, cut off at 1 mm / 0.01 mm from inflection | (a) as the code computes it (50 samples per edge, 40 sample offsets) | (b) L/Δθ | (c) natural, inflection | (c) natural, widest | (d) least squares |
|---|---|---|---|---|---|---|
| P1 pure R14 arc, curvature jumps at inflection | 14.0 / 14.0 | 14.00 | 14.00 | 14.00 | 18.00 | 14.00 |
| P5 arc chain: 14 m core (±400 mm), 25 m flanks, curvature jumps | 17.0 / 17.0 | 16.9-17.2 | 15.91 | 14.47 | 18.45 | 14.26 |
| P2 14 m core, linear ramp to k=0 at inflection (G2) | 35.9 / 59.4 (diverges) | median 31, range 23.8-**4636** | 17.11 | 14.65 | 18.79 | 14.34 |
| P4 k₀(1−(s/a)²) (G2) | 40.3 / 66.8 | median 41, range 26-**9497** | 17.25 | 13.80 | 17.93 | 13.42 |
| P3 k linear from k₀ to 0 (G2) | 66 / 115 | median 70, range 41-1631 | 21.00 | 15.75 | 19.91 | 15.27 |

- **Edge weighting bug (verified by arithmetic).** Take P5 as 3 edges: 150 mm at 25 m, 800 mm at 14 m, 150 mm at 25 m. The code returns (50·25 + 50·14 + 50·25)/150 = **21.3 m**, while the true distance average is **17.0 m**. Splitting the core into 3 edges gives **18.4 m**. The same geometry gives different numbers depending on how its edges are split.
- **Scale Radius smoothing.** I ran P5 through the Scale Radius pipeline: 100 samples of k, the linearised integration, then a cubic fit to a single curve.
  - With 26 interior knots, the evaluated average was 17.05 m.
  - With 60 knots, it was **51.6 m**, with individual samples up to 751 m.
  - The refit turns jump inflections into smooth ones, so Scale Radius is aiming at a different, unstable quantity than Analyze.
- **Is 17 vs 14 expected or a bug?** It is P5-like behaviour: an expected gap between definitions, since (a) and (c) measure different things. On smooth (G2) splines, though, the current code gives 24 m to thousands of metres, so a stable 17 suggests your footprints are mostly arc chains or have steep curvature ramps. I have not verified this against your documents.
- **What "natural radius" means here:** the single arc through the two end points (widest or inflection) at the measured waist. It is the industry chord/sagitta "sidecut radius" computed from tip, waist and tail widths, and it mostly reflects the curvature near the waist.

### 4. Recommendations
1. **"Average radius" = L/Δθ between the FB and AB inflections.**
   - Δθ = atan2(cross, dot) of the unit tangents at the two inflection params, which are already returned by `findInflectionPoint`.
   - L = arc length summed across the curves between those params, using Simpson or Gauss integration of |r'(u)| with `evaluateSpline` and `nDerivatives: 1`.
   - It is safe to call from editing logic, needs no curvature sampling, and does not depend on how the edges are split.
   - Name it "Equivalent radius (length / turn)" or keep "Average radius" and update its description.
2. **If you want to keep the distance-averaged R** as a secondary readout:
   - Weight it by ds, not by sample count.
   - For edges that are Circles (check with `evCurveDefinition` on the raw edges), use the exact R × length.
   - Cap R, for example at 100 m, or leave out the last few mm before each inflection. Without that it is undefined on G2 curves.
   - Show it only when both inflections are the junction type.
3. **Interval:** keep inflection to inflection. If an inflection is missing, report "N/A" rather than silently falling back to the widest point, or label the fallback clearly. Fix the sign-0 straight-line gap by skipping sign-0 samples instead of storing them as `prevSample`.
4. **Scale Footprint:**
   - Delete `evaluateRadiusBetweenInflections` and `findInflectionInTempCurve`, and call the library functions.
   - With L/Δθ and fixed inflections, scaling k by c changes R_eq by exactly 1/c, so the iteration converges in 1-2 passes.
   - Replace the 1000 m fallback with an error.
   - **Today Analyze and Scale do not agree:** they use different edge partitions (per-edge sampling) and Scale refits everything into one smoothed temporary curve.
5. **Also** add `forceNonRational: true` at `fpt_analyze.fs:77`, which is hygiene with less than 0.2 % effect on the radius, and fix the description at `predicates.fs:31`.

**What will change:**
- Pure-arc footprints: no change.
- Arc chains: the average drops toward L/Δθ. In P5 it goes from 17.0-21.3 to 15.9.
- G2 spline footprints: the average falls from erratic values (24 to thousands of m) to a stable value about 1.1-1.3 × the inflection natural radius (17.1 vs 14.65 in P2).
- Scale Radius targets currently express the old metric, so existing Scale Radius features will produce different geometry for the same target.
- Capture goldens of the current numbers first, as the round-1 P-1 recommends. The round-1 claim that "computeAverageRadius averages 1/|k| per sample, pulled by near-inflection samples" is **confirmed**. The per-edge weighting bug and the definition's divergence on smooth curves are **new**.

### Critical Files for Implementation
- C:/Users/jed.yeiser/documents/featurescripts/footprint/fpt_analyze.fs
- C:/Users/jed.yeiser/documents/featurescripts/footprint/scaleFootprint.fs
- C:/Users/jed.yeiser/documents/featurescripts/footprint/predicates.fs
- C:/Users/jed.yeiser/documents/featurescripts/footprint/footprint_math.fs
- C:/Users/jed.yeiser/documents/featurescripts/footprint/analyzeFootprint.fs