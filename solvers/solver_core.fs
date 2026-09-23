FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

/**
 * Scalar search over one iteration variable, driven by a trial callback.
 *
 * The callback is `trial(x is number, kind is string) returns map` -- kind names the step
 * ("start", "step", "secant", "retry", "lower bound", "upper bound", "brent", "midpoint", "sweep") -- with
 *     { "ok" : boolean,        // false when the trial could not be built or measured
 *       "accepted" : boolean,  // the trial meets the condition; the search stops here
 *       "residual" : number }  // result - target, SI; only read when ok
 * The callback owns the geometry: it keeps an accepted trial and rolls back every other one,
 * so nothing a rejected trial built is left in the context. The search never re-runs a value.
 *
 * Every function returns
 *     { "found" : boolean, "x" : number, "trials" : number, "history" : array, "message" : string }
 * with history entries { "x", "ok", "residual" } in the order they were run.
 */

/** Relative bracket width below which the bracketed search gives up. */
export const SOLVER_X_TOLERANCE = 1e-12;

/** Retries, each halving the step, when a secant trial fails to build. */
const SECANT_RETRIES = 3;

/**
 * Secant search from a starting value, then Brent once two trials straddle the target.
 * `x0` is the starting value, `step` the first perturbation; every trial is held inside
 * [lower, upper] -- the bounds are limits, not trials.
 */
export function solveTargetFromStart(trial is function, x0 is number, step is number, lower is number, upper is number, maxTrials is number) returns map
{
    const lo = min(lower, upper);
    const hi = max(lower, upper);
    var history = [];

    var xa = clamp(x0, lo, hi);
    var ra = trial(xa, "start");
    history = append(history, historyEntry(xa, ra));
    if (ra.ok && ra.accepted)
    {
        return solveResult(true, xa, history, "");
    }
    if (!ra.ok)
    {
        return solveResult(false, xa, history, "The trial at the starting value failed.");
    }

    var xb = xa + step;
    if (xb > hi || xb < lo)
    {
        xb = xa - step;
    }
    xb = clamp(xb, lo, hi);
    var rb = trial(xb, "step");
    history = append(history, historyEntry(xb, rb));
    if (rb.ok && rb.accepted)
    {
        return solveResult(true, xb, history, "");
    }
    if (!rb.ok)
    {
        return solveResult(false, xb, history, "The trial at the first perturbation failed; try a smaller step.");
    }
    if (rb.residual == ra.residual)
    {
        return solveResult(false, xb, history, insensitiveMessage());
    }

    while (size(history) < maxTrials)
    {
        if (oppositeSigns(ra.residual, rb.residual))
        {
            return brent(trial, xa, ra.residual, xb, rb.residual, history, maxTrials);
        }
        if (rb.residual == ra.residual)
        {
            return solveResult(false, xb, history, "Two trials gave the same result; the secant step is undefined.");
        }

        // Secant step from the two latest trials, held inside the bounds.
        const secant = xb - rb.residual * (xb - xa) / (rb.residual - ra.residual);
        var xn = clamp(secant, lo, hi);
        if (abs(xn - xb) <= SOLVER_X_TOLERANCE * max(abs(xb), 1))
        {
            return solveResult(false, xb, history, "The target lies beyond the " ~ ((xn == hi) ? "upper" : "lower")
                ~ " bound: the residual there is " ~ toString(rb.residual) ~ ".");
        }

        var rn = trial(xn, "secant");
        history = append(history, historyEntry(xn, rn));
        var retries = 0;
        while (!rn.ok && retries < SECANT_RETRIES && size(history) < maxTrials)
        {
            xn = 0.5 * (xn + xb);
            rn = trial(xn, "retry");
            history = append(history, historyEntry(xn, rn));
            retries += 1;
        }
        if (!rn.ok)
        {
            return solveResult(false, xn, history, "Trials failed to build near " ~ toString(xn) ~ ".");
        }
        if (rn.accepted)
        {
            return solveResult(true, xn, history, "");
        }
        xa = xb;
        ra = rb;
        xb = xn;
        rb = rn;
    }
    return solveResult(false, xb, history, "No trial met the tolerance within " ~ toString(maxTrials) ~ " trials.");
}

/**
 * Bracketed root finding on the residual, starting with a trial at each bound.
 * The bounds must give residuals of opposite sign.
 */
export function solveTargetBracketed(trial is function, lower is number, upper is number, maxTrials is number) returns map
{
    var history = [];

    const ra = trial(lower, "lower bound");
    history = append(history, historyEntry(lower, ra));
    if (ra.ok && ra.accepted)
    {
        return solveResult(true, lower, history, "");
    }
    if (!ra.ok)
    {
        return solveResult(false, lower, history, "The trial at the lower bound failed.");
    }

    const rb = trial(upper, "upper bound");
    history = append(history, historyEntry(upper, rb));
    if (rb.ok && rb.accepted)
    {
        return solveResult(true, upper, history, "");
    }
    if (!rb.ok)
    {
        return solveResult(false, upper, history, "The trial at the upper bound failed.");
    }
    if (rb.residual == ra.residual)
    {
        return solveResult(false, upper, history, insensitiveMessage());
    }
    if (!oppositeSigns(ra.residual, rb.residual))
    {
        return solveResult(false, upper, history, "The bounds do not bracket the target: residual "
            ~ toString(ra.residual) ~ " at the lower bound, " ~ toString(rb.residual) ~ " at the upper.");
    }
    return brent(trial, lower, ra.residual, upper, rb.residual, history, maxTrials);
}

/**
 * First value meeting the condition, stepping from `lower` to `upper` in `steps` equal steps
 * (steps + 1 values, both bounds included). A failed trial is skipped.
 */
export function solveFirstMatch(trial is function, lower is number, upper is number, steps is number, maxTrials is number) returns map
{
    var history = [];
    for (var i = 0; i <= steps; i += 1)
    {
        if (size(history) >= maxTrials)
        {
            return solveResult(false, lower, history, "No trial met the condition within " ~ toString(maxTrials) ~ " trials.");
        }
        const x = lower + (upper - lower) * i / steps;
        const r = trial(x, "sweep");
        history = append(history, historyEntry(x, r));
        if (r.ok && r.accepted)
        {
            return solveResult(true, x, history, "");
        }
        if (size(history) == 2 && history[0].ok && r.ok && history[0].residual == r.residual)
        {
            return solveResult(false, x, history, insensitiveMessage());
        }
    }
    return solveResult(false, upper, history, "No value between the bounds met the condition.");
}

/**
 * Brent (inverse quadratic / secant steps, bisection fallback), after Numerical Recipes zbrent,
 * on a bracket [a, b] whose residuals fa, fb have opposite signs and are already in `history`.
 * A failed trial is replaced by the midpoint of the current bracket.
 */
function brent(trial is function, a0 is number, fa0 is number, b0 is number, fb0 is number, history0 is array, maxTrials is number) returns map
{
    var history = history0;
    var a = a0;
    var fa = fa0;
    var b = b0;
    var fb = fb0;
    var c = a;
    var fc = fa;
    var d = b - a;
    var e = d;
    while (size(history) < maxTrials)
    {
        if (!oppositeSigns(fb, fc))
        {
            c = a;
            fc = fa;
            d = b - a;
            e = d;
        }
        if (abs(fc) < abs(fb))
        {
            a = b;
            b = c;
            c = a;
            fa = fb;
            fb = fc;
            fc = fa;
        }
        const tol1 = SOLVER_X_TOLERANCE * max(abs(b), 1);
        const xm = 0.5 * (c - b);
        if (abs(xm) <= tol1)
        {
            return solveResult(false, b, history, "The bracket closed on " ~ toString(b)
                ~ " without meeting the tolerance; the result may jump there.");
        }
        if (abs(e) >= tol1 && abs(fa) > abs(fb))
        {
            const s = fb / fa;
            var p;
            var q;
            if (a == c)
            {
                p = 2 * xm * s;
                q = 1 - s;
            }
            else
            {
                const qa = fa / fc;
                const r = fb / fc;
                p = s * (2 * xm * qa * (qa - r) - (b - a) * (r - 1));
                q = (qa - 1) * (r - 1) * (s - 1);
            }
            if (p > 0)
            {
                q = -q;
            }
            p = abs(p);
            if (2 * p < min(3 * xm * q - abs(tol1 * q), abs(e * q)))
            {
                e = d;
                d = p / q;
            }
            else
            {
                d = xm;
                e = d;
            }
        }
        else
        {
            d = xm;
            e = d;
        }
        a = b;
        fa = fb;
        var next = b + ((abs(d) > tol1) ? d : ((xm > 0) ? tol1 : -tol1));

        var rn = trial(next, "brent");
        history = append(history, historyEntry(next, rn));
        if (!rn.ok)
        {
            // Retry once at the midpoint of the bracket, which always narrows it.
            const mid = b + xm;
            if (abs(mid - next) <= tol1 || size(history) >= maxTrials)
            {
                return solveResult(false, next, history, "The trial at " ~ toString(next) ~ " failed.");
            }
            next = mid;
            rn = trial(next, "midpoint");
            history = append(history, historyEntry(next, rn));
            if (!rn.ok)
            {
                return solveResult(false, next, history, "The trials at the next estimate and at the bracket midpoint both failed.");
            }
            d = next - b;
            e = d;
        }
        if (rn.accepted)
        {
            return solveResult(true, next, history, "");
        }
        b = next;
        fb = rn.residual;
    }
    return solveResult(false, b, history, "No trial met the tolerance within " ~ toString(maxTrials) ~ " trials.");
}

function oppositeSigns(f1 is number, f2 is number) returns boolean
{
    return (f1 > 0 && f2 < 0) || (f1 < 0 && f2 > 0);
}

function insensitiveMessage() returns string
{
    return "The first two trials gave exactly the same result: the result does not depend on the iteration variable. Do the listed features read it?";
}

function historyEntry(x is number, r is map) returns map
{
    return { "x" : x, "ok" : r.ok, "residual" : r.residual };
}

function solveResult(found is boolean, x is number, history is array, message is string) returns map
{
    return { "found" : found, "x" : x, "trials" : size(history), "history" : history, "message" : message };
}
