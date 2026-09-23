FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

/**
 * Scalar search over one iteration variable, driven by a trial callback.
 *
 * The callback is `trial(x is number) returns map` with
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

/**
 * Bracketed root finding on the residual (Brent: inverse quadratic / secant steps, bisection
 * fallback). `lower` and `upper` must give residuals of opposite sign. A failed interior
 * trial is replaced by the midpoint of the current bracket.
 */
export function solveTargetBracketed(trial is function, lower is number, upper is number, maxTrials is number) returns map
{
    var history = [];
    var a = lower;
    var b = upper;

    var ra = trial(a);
    history = append(history, historyEntry(a, ra));
    if (ra.ok && ra.accepted)
    {
        return solveResult(true, a, history, "");
    }
    if (!ra.ok)
    {
        return solveResult(false, a, history, "The trial at the lower bound failed.");
    }

    var rb = trial(b);
    history = append(history, historyEntry(b, rb));
    if (rb.ok && rb.accepted)
    {
        return solveResult(true, b, history, "");
    }
    if (!rb.ok)
    {
        return solveResult(false, b, history, "The trial at the upper bound failed.");
    }

    var fa = ra.residual;
    var fb = rb.residual;
    if ((fa > 0 && fb > 0) || (fa < 0 && fb < 0))
    {
        return solveResult(false, b, history, "The bounds do not bracket the target: residual "
            ~ toString(fa) ~ " at the lower bound, " ~ toString(fb) ~ " at the upper.");
    }

    // Brent, after Numerical Recipes zbrent: b is the best estimate, [b, c] brackets the root.
    var c = a;
    var fc = fa;
    var d = b - a;
    var e = d;
    while (size(history) < maxTrials)
    {
        if ((fb > 0 && fc > 0) || (fb < 0 && fc < 0))
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

        var rn = trial(next);
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
            rn = trial(next);
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
        const r = trial(x);
        history = append(history, historyEntry(x, r));
        if (r.ok && r.accepted)
        {
            return solveResult(true, x, history, "");
        }
    }
    return solveResult(false, upper, history, "No value between the bounds met the condition.");
}

function historyEntry(x is number, r is map) returns map
{
    return { "x" : x, "ok" : r.ok, "residual" : r.residual };
}

function solveResult(found is boolean, x is number, history is array, message is string) returns map
{
    return { "found" : found, "x" : x, "trials" : size(history), "history" : history, "message" : message };
}
