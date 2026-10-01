# Waist Location Solver Fix

## Problem
The waist location solver was failing to converge, with the waist oscillating between indices 33 and 34 instead of moving to the target location of 0.065m (65mm).

## Root Cause
The current code had "improvements" added to the solver that weren't in the old working code:

1. **Seed multiplier**: Used `t1 = -average(base['k']) * 10.0` for waist mode instead of just `t1 = -average(base['k'])`
2. **Stall detection**: Added logic to detect when `abs(f1 - f0) < tol` and perturb with ±0.5
3. **Stall escape**: Added logic to escape stalls with large jumps during iteration

These "improvements" were causing the solver to diverge or oscillate instead of converging smoothly.

## Fix Applied
Reverted the solver code to match the old working implementation exactly:

### Changes in `solveTheta0ForDriver()` (fpt_geometry.fs:515-560)

**BEFORE (broken):**
```featurescript
var seed2Scale = (integrationDef.angleDriver == AngleDriver.WAIST) ? 10.0 : 1.0;
var t1 = -average(base['k']) * seed2Scale;
var f1 = residual(t1, base, integrationDef.angleDriver, targetVal);

// If initial seeds give same residual, try a much larger perturbation
if (abs(f1 - f0) < tol && abs(f0) > tol)
{
    println("  Initial stall detected - trying larger perturbation");
    t1 = (f0 > 0 * f0) ? -0.5 : 0.5;
    f1 = residual(t1, base, integrationDef.angleDriver, targetVal);
    println("  Perturbed t1=" ~ t1 ~ ", f1=" ~ toString(f1));
}

for (var it = 0; it < maxIter; it += 1)
{
    // ... convergence check ...

    if (denom == 0 * denom)
    {
        // Try to escape stall with a large jump
        if (it < maxIter - 1)
        {
            println("  Attempting to escape stall with large jump");
            var jumpDir = (f1 > 0 * f1) ? -1 : 1;
            t0 = t1;
            f0 = f1;
            t1 = t1 + jumpDir * 0.5;
            f1 = residual(t1, base, integrationDef.angleDriver, targetVal);
            continue;
        }
        return t1;
    }
    // ... secant update ...
}
```

**AFTER (fixed - matches old working code):**
```featurescript
// A second seed. Using average K gives a reasonable scale.
// OLD CODE: Just use -average(k) without multiplier
var t1 = -average(base['k']);
var f1 = residual(t1, base, integrationDef.angleDriver, targetVal);

// OLD CODE: Simple secant method without stall detection
for (var it = 0; it < maxIter; it += 1)
{
    if (abs(f1) <= tol)
    {
        println("  CONVERGED at iteration " ~ it ~ ": f1=" ~ toString(f1));
        return t1;
    }

    var denom = (f1 - f0);
    if (denom == 0 * denom) // unit-safe zero check pattern
    {
        println("  STALLED at iteration " ~ it ~ ": denom=0");
        return t1;
    }

    // secant update (unit-safe because f has units, denom has same units)
    var t2 = t1 - f1 * (t1 - t0) / denom;

    t0 = t1;
    f0 = f1;
    t1 = t2;
    f1 = residual(t1, base, integrationDef.angleDriver, targetVal);
}
```

## Debug Output Added

Also added debug output to help diagnose future issues:

1. **Base curve structure** (lines 495-505): Prints x and base.y values for indices 30-40 to understand the curve shape
2. **Y landscape around waist** (lines 657-665): Prints Y values around the minimum to see the local shape

## Testing

The user should now test in Onshape with waist location mode:
- Target waist location: 0.065m (65mm)
- Expected behavior: Solver should converge smoothly like the old code did
- Check console output for debug info about base curve structure

## Why This Works

The old code used a conservative initial guess (`-average(base['k'])`) that stayed close to the initial profile. The "improvements" that multiplied by 10 or jumped by ±0.5 were too aggressive and caused the solver to overshoot or oscillate.

The secant method is sensitive to initial conditions, and simpler/smaller perturbations often work better than large jumps.

## Files Modified
- `footprint_integration/fpt_geometry.fs`: Lines 515-560 (solveTheta0ForDriver function)
- Added debug output at lines 495-505 and 657-665
