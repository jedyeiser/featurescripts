FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: primitive_frame.fs
export import(path : "PRIMITIVE_FRAME_EID", version : "PRIMITIVE_FRAME_MV");
// IMPORT: footprint V32 fpt_analyze.fs (prepareFootprintCurves, analyzeFootprintCurves, computeAverageRadius)
import(path : "52724f3a857fa52d3ecceb77/b105c12d5094d99215293e38/71d853c0fd2f10ca3bb20a4b", version : "3b2e4e5538b5a476b5486c1d");

/**
 * Export Primitive -- the unwrapped footprint and the radius plot.
 *
 * Unwrapping: a footprint point at x (in plan) moves to u = x(MRS) + s, where s is the arc length along the bottom
 * wire (zero at MRS, + towards FCP) at that x, taken along the tip direction; y (across) is kept. Where the bottom
 * wire is flat and straight u = x, and such edges are copied exactly (arcs stay arcs); elsewhere the edge is
 * sampled, mapped and fitted (1 um). A FLAT input wire (constant z) is taken as already unwrapped (u = x): the
 * primitive aligns it at MRS.
 *
 * Radius: from the exact source curve and the bottom wire's derivatives (chain rule, no fitting): with x', y',
 * x'', y'' the plan derivatives along the edge and u'(x), u''(x) the unwrap,
 *     X' = u' x',  X'' = u'' x'^2 + u' x'',  k = (X' y'' - y' X'') / (X'^2 + y'^2)^1.5,
 * and the SIGN: positive when the centre of curvature lies away from the centreline (sidecut), negative towards it
 * (taper, tip, tail). |R| above the radius limit (flat, or next to an inflection) is "no value": the plot breaks.
 */

/** Sample spacing along footprint edges, and per-edge sample limits. */
const UNWRAP_SPACING = 5 * millimeter;
const UNWRAP_MIN_SAMPLES = 17;
const UNWRAP_MAX_SAMPLES = 401;
/** Fit tolerance of unwrapped (mapped) edges; resolves sidecut curvature (8 tol / L^2). */
const UNWRAP_FIT_TOLERANCE = 1e-6 * meter;
/** Mapped edges whose u - x varies less than this are copied exactly. */
const UNWRAP_EXACT_TOLERANCE = 1e-7 * meter;
/** Tolerance handed to fpt_analyze's B-spline approximation of the unwrapped edges. */
const FPT_TOLERANCE = 1e-6 * meter;
/** Two radius-plot runs meeting at an edge junction are joined when their plot heights differ by less than this. */
const PLOT_JOIN_TOLERANCE = 0.005 * millimeter;
const OUTLINE_PLANE_SIZE = 20 * meter;
const OUTLINE_CLEARANCE = 10 * millimeter;

/**
 * The footprint's source edges in the LOCAL frame: the volume's plan outline (on a plane below it, normal to the
 * datum Z), or copies of the input wires. Returns { bodies (temporary copies), edges }.
 */
export function primitiveFootprintSource(context is Context, id is Id, fromVolume is boolean, volume is Query, inputEdges is Query,
    datum is CoordSystem, toLocal is Transform, isIdentity is boolean) returns map
{
    var bodies = qNothing();
    var edges = qNothing();
    if (fromVolume)
    {
        const bb = evBox3d(context, { "topology" : volume, "cSys" : datum, "tight" : true });
        const origin = toWorld(datum, vector(0 * meter, 0 * meter, bb.minCorner[2] - OUTLINE_CLEARANCE));
        opPlane(context, id + "outlinePlane", { "plane" : plane(origin, datum.zAxis, datum.xAxis),
                    "width" : OUTLINE_PLANE_SIZE, "height" : OUTLINE_PLANE_SIZE });
        opCreateOutline(context, id + "outline", { "tools" : volume, "target" : qCreatedBy(id + "outlinePlane", EntityType.FACE) });
        opDeleteBodies(context, id + "deleteOutlinePlane", { "entities" : qCreatedBy(id + "outlinePlane", EntityType.BODY) });
        bodies = qCreatedBy(id + "outline", EntityType.BODY);
        primitiveMove(context, id + "outlineToLocal", bodies, toLocal, isIdentity);
        edges = qLoopEdges(qOwnedByBody(bodies, EntityType.FACE));
    }
    else
    {
        if (isQueryEmpty(context, inputEdges))
        {
            throw regenError("Select the footprint wire(s).", ["footprintWires"]);
        }
        opExtractWires(context, id + "inputCopy", { "edges" : inputEdges });
        bodies = qCreatedBy(id + "inputCopy", EntityType.BODY);
        primitiveMove(context, id + "inputToLocal", bodies, toLocal, isIdentity);
        edges = qOwnedByBody(bodies, EntityType.EDGE);
    }
    const list = evaluateQuery(context, edges);
    if (size(list) == 0)
    {
        throw regenError("The footprint has no edges.", [fromVolume ? "volume" : "footprintWires"]);
    }
    return { "bodies" : bodies, "edges" : list };
}

/**
 * Unwraps the source edges into the LOCAL z = 0 plane. Returns
 *     bodies    the unwrapped wires (exact copies and fitted edges)
 *     samples   per source edge { u : [], y : [], R : [] } (m for R; undefined = no radius there)
 *     exact / fitted   edge counts
 */
export function primitiveUnwrap(context is Context, id is Id, frame is map, edges is array, flatInput is boolean, radiusLimit is ValueWithUnits) returns map
{
    // Pass 1: sample every edge; collect the x of every sample that needs the bottom wire.
    var data = [];
    var xs = [];
    for (var i = 0; i < size(edges); i += 1)
    {
        const len = evLength(context, { "entities" : edges[i] });
        if (len < TOLERANCE.zeroLength * meter)
        {
            continue;
        }
        const n = max(UNWRAP_MIN_SAMPLES, min(UNWRAP_MAX_SAMPLES, ceil(len / UNWRAP_SPACING) + 1));
        var ts = [];
        for (var k = 0; k < n; k += 1)
        {
            ts = append(ts, k / (n - 1));
        }
        const results = evEdgeCurvatures(context, { "edge" : edges[i], "parameters" : ts });
        const z0 = results[0].frame.origin[2];
        var planarZ = true;
        for (var r in results)
        {
            if (abs(r.frame.origin[2] - z0) > UNWRAP_EXACT_TOLERANCE)
            {
                planarZ = false;
            }
        }
        const mapped = !(flatInput && planarZ);
        data = append(data, { "edge" : edges[i], "len" : len, "results" : results, "planarZ" : planarZ, "z0" : z0,
                    "mapped" : mapped, "first" : size(xs) });
        if (mapped)
        {
            for (var r in results)
            {
                xs = append(xs, r.frame.origin[0]);
            }
        }
    }
    const bottomAt = size(xs) > 0 ? primitiveChainAtX(context, frame.chain, frame.lookup, xs) : [];
    const limitK = 1 / (radiusLimit / meter);

    // Pass 2: u, y, radius and the unwrap derivative at every sample; exact or fitted.
    var samples = [];
    var exactGroups = [];
    var fitted = [];
    for (var e in data)
    {
        var us = [];
        var ys = [];
        var radii = [];
        var points = [];
        var derivs = [];
        var exact = e.planarZ;
        var shift = undefined;
        for (var k = 0; k < size(e.results); k += 1)
        {
            const r = e.results[k];
            const p = r.frame.origin;
            const tangent = r.frame.zAxis;
            const kc = r.curvature * meter;
            var u = p[0];
            var up = 1;
            var upp = 0;
            var usable = true;
            if (e.mapped)
            {
                const b = bottomAt[e.first + k];
                u = primitiveU(frame, b.a);
                const tx = b.tangent[0];
                if (abs(tx) < 1e-6)
                {
                    usable = false;
                }
                else
                {
                    up = frame.dirSign / tx;
                    upp = -frame.dirSign * (b.curvature * meter) * b.normal[0] / (tx * tx * tx);
                }
            }
            const xp = up * tangent[0];
            const xpp = upp * tangent[0] * tangent[0] + up * kc * r.frame.xAxis[0];
            const yp = tangent[1];
            const ypp = kc * r.frame.xAxis[1];
            const speed2 = xp * xp + yp * yp;
            var radius = undefined;
            if (usable && speed2 > 1e-24 && abs(p[1]) > TOLERANCE.zeroLength * meter)
            {
                const ku = (xp * ypp - yp * xpp) / (speed2 * sqrt(speed2));
                if (abs(ku) > limitK)
                {
                    radius = ((ku * xp * (p[1] / meter)) > 0 ? 1 : -1) / abs(ku);
                }
            }
            us = append(us, u);
            ys = append(ys, p[1]);
            radii = append(radii, radius);
            points = append(points, vector(u, p[1], 0 * meter));
            derivs = append(derivs, vector(xp, yp, 0));
            const c = u - p[0];
            if (shift == undefined)
            {
                shift = c;
            }
            else if (abs(c - shift) > UNWRAP_EXACT_TOLERANCE)
            {
                exact = false;
            }
        }
        samples = append(samples, { "u" : us, "y" : ys, "R" : radii });
        if (exact)
        {
            var placed = false;
            for (var g = 0; g < size(exactGroups); g += 1)
            {
                if (abs(exactGroups[g].shift - shift) < 1e-9 * meter && abs(exactGroups[g].z0 - e.z0) < 1e-9 * meter)
                {
                    exactGroups[g].edges = append(exactGroups[g].edges, e.edge);
                    placed = true;
                    break;
                }
            }
            if (!placed)
            {
                exactGroups = append(exactGroups, { "shift" : shift, "z0" : e.z0, "edges" : [e.edge] });
            }
        }
        else
        {
            fitted = append(fitted, { "points" : points, "derivs" : derivs });
        }
    }

    // Build: exact copies moved onto z = 0 at u, and fitted edges.
    var bodies = [];
    for (var g = 0; g < size(exactGroups); g += 1)
    {
        const gid = id + ("exact" ~ g);
        opExtractWires(context, gid, { "edges" : qUnion(exactGroups[g].edges) });
        const moved = qCreatedBy(gid, EntityType.BODY);
        const offset = vector(exactGroups[g].shift, 0 * meter, -exactGroups[g].z0);
        if (norm(offset) > 1e-12 * meter)
        {
            opTransform(context, id + ("exactMove" ~ g), { "bodies" : moved, "transform" : transform(offset) });
        }
        bodies = append(bodies, moved);
    }
    for (var f = 0; f < size(fitted); f += 1)
    {
        const curve = fitUnwrapped(context, fitted[f].points, fitted[f].derivs);
        if (curve != undefined)
        {
            opCreateBSplineCurve(context, id + ("fit" ~ f), { "bSplineCurve" : curve });
            bodies = append(bodies, qCreatedBy(id + ("fit" ~ f), EntityType.BODY));
        }
    }
    return { "bodies" : qUnion(bodies), "samples" : samples, "exact" : size(exactGroups), "fitted" : size(fitted) };
}

/** A cubic through the mapped points (chord-length parameters, end tangents kept -- correction 23). */
function fitUnwrapped(context is Context, points is array, derivs is array)
{
    var kept = [points[0]];
    var keptIdx = [0];
    for (var k = 1; k < size(points); k += 1)
    {
        if (norm(points[k] - kept[size(kept) - 1]) > 1e-8 * meter)
        {
            kept = append(kept, points[k]);
            keptIdx = append(keptIdx, k);
        }
    }
    if (size(kept) < 2)
    {
        return undefined;
    }
    var chord = [0 * meter];
    for (var k = 1; k < size(kept); k += 1)
    {
        chord = append(chord, chord[k - 1] + norm(kept[k] - kept[k - 1]));
    }
    const total = chord[size(chord) - 1];
    var params = [];
    for (var c in chord)
    {
        params = append(params, c / total);
    }
    const d0 = derivs[keptIdx[0]];
    const d1 = derivs[keptIdx[size(keptIdx) - 1]];
    var target = { "positions" : kept };
    if (norm(d0) > 1e-9 && norm(d1) > 1e-9)
    {
        target.startDerivative = normalize(d0) * total;
        target.endDerivative = normalize(d1) * total;
    }
    return approximateSpline(context, {
                    "degree" : 3,
                    "tolerance" : UNWRAP_FIT_TOLERANCE,
                    "isPeriodic" : false,
                    "targets" : [approximationTarget(target)],
                    "parameters" : params,
                    "maxControlPoints" : 400,
                    "suppressInterpolationNotice" : true
                })[0];
}

/**
 * fpt_analyze on the unwrapped footprint (u along, y across): waist, widest, inflections, natural radii, taper,
 * and the average radius between the pair picked by `between`. Returns { result (analyzeFootprintCurves map),
 * average ({ valid, avgRadius }), lo, hi (the u range of the average) }.
 */
export function primitiveFootprintAnalysis(context is Context, unwrapped is Query, uFcp is ValueWithUnits, uAcp is ValueWithUnits,
    uMrs is ValueWithUnits, between is PrimitiveRadiusBetween) returns map
{
    const prepared = prepareFootprintCurves(context, qOwnedByBody(unwrapped, EntityType.EDGE), uFcp, uAcp, FPT_TOLERANCE);
    const zero = 0 * meter;
    const result = analyzeFootprintCurves(context, {
                "curveData" : prepared.curveData,
                "fcpPoint" : vector(uFcp, zero, zero),
                "acpPoint" : vector(uAcp, zero, zero),
                "mrsPoint" : vector(uMrs, zero, zero)
            });
    var lo = result.inflectionXMin;
    var hi = result.inflectionXMax;
    if (between == PrimitiveRadiusBetween.CONTACTS)
    {
        lo = min(uFcp, uAcp);
        hi = max(uFcp, uAcp);
    }
    else if (between == PrimitiveRadiusBetween.WIDEST)
    {
        lo = result.widestXMin;
        hi = result.widestXMax;
    }
    const average = computeAverageRadius(prepared.curveData, lo, hi, {});
    return { "result" : result, "average" : average, "lo" : lo, "hi" : hi };
}

/**
 * Where the unwrapped footprint crosses u (from the samples; 5 mm spacing, < 1 um on sidecut radii):
 * { hit, yMax, yMin, radius (m, at the +y crossing; undefined = none) }.
 */
export function primitiveFootprintAt(samples is array, u is ValueWithUnits) returns map
{
    var yMax = undefined;
    var yMin = undefined;
    var radius = undefined;
    for (var e in samples)
    {
        for (var k = 0; k < size(e.u) - 1; k += 1)
        {
            const d0 = e.u[k] - u;
            const d1 = e.u[k + 1] - u;
            if (!((d0 <= 0 * meter && d1 >= 0 * meter) || (d0 >= 0 * meter && d1 <= 0 * meter)))
            {
                continue;
            }
            const span = e.u[k + 1] - e.u[k];
            const f = abs(span) > 1e-12 * meter ? (u - e.u[k]) / span : 0;
            const y = e.y[k] + (e.y[k + 1] - e.y[k]) * f;
            var r = undefined;
            if (e.R[k] != undefined && e.R[k + 1] != undefined)
            {
                r = e.R[k] + (e.R[k + 1] - e.R[k]) * f;
            }
            if (yMax == undefined || y > yMax)
            {
                yMax = y;
                radius = r;
            }
            if (yMin == undefined || y < yMin)
            {
                yMin = y;
            }
        }
    }
    return { "hit" : yMax != undefined, "yMax" : yMax, "yMin" : yMin, "radius" : radius };
}

/**
 * The +y side's radius runs, ordered along u: each run is [[u, R], ...] (R in m) between breaks (no radius, or a
 * sign change). Runs meeting at an edge junction with (nearly) the same radius are joined end to end.
 */
export function primitiveRadiusRuns(samples is array) returns array
{
    var runs = [];
    for (var e in samples)
    {
        var pts = [];
        for (var k = 0; k < size(e.u); k += 1)
        {
            if (e.y[k] > TOLERANCE.zeroLength * meter)
            {
                pts = append(pts, { "u" : e.u[k], "R" : e.R[k] });
            }
        }
        pts = sort(pts, function(a, b) { return (a.u - b.u) / meter; });
        var run = [];
        for (var pt in pts)
        {
            if (pt.R == undefined || (size(run) > 0 && (run[size(run) - 1][1] > 0) != (pt.R > 0)))
            {
                if (size(run) >= 2)
                {
                    runs = append(runs, run);
                }
                run = [];
            }
            if (pt.R != undefined)
            {
                run = append(run, [pt.u, pt.R]);
            }
        }
        if (size(run) >= 2)
        {
            runs = append(runs, run);
        }
    }
    runs = sort(runs, function(a, b) { return (a[0][0] - b[0][0]) / meter; });
    // Continuous curvature across a junction: meet at the average point.
    for (var i = 0; i < size(runs) - 1; i += 1)
    {
        const a = runs[i][size(runs[i]) - 1];
        const b = runs[i + 1][0];
        if (abs(a[0] - b[0]) < 1e-6 * meter && abs(a[1] - b[1]) * PRIMITIVE_RADIUS_PLOT_SCALE * meter < PLOT_JOIN_TOLERANCE)
        {
            const mid = [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2];
            runs[i][size(runs[i]) - 1] = mid;
            runs[i + 1][0] = mid;
        }
    }
    return runs;
}

/**
 * The radius plot in the LOCAL XZ plane: one wire per run at (u, 0, zRef + R * 10 mm/m); a run of constant radius
 * (an arc) is a straight line. Returns the bodies.
 */
export function primitiveRadiusPlot(context is Context, id is Id, runs is array, zRef is ValueWithUnits) returns array
{
    var bodies = [];
    for (var i = 0; i < size(runs); i += 1)
    {
        var lo = runs[i][0][1];
        var hi = lo;
        var sum = 0;
        for (var p in runs[i])
        {
            lo = min(lo, p[1]);
            hi = max(hi, p[1]);
            sum += p[1];
        }
        var points = [];
        if ((hi - lo) * PRIMITIVE_RADIUS_PLOT_SCALE * meter < 1e-6 * meter)
        {
            const level = zRef + sum / size(runs[i]) * PRIMITIVE_RADIUS_PLOT_SCALE * meter;
            points = [vector(runs[i][0][0], 0 * meter, level), vector(runs[i][size(runs[i]) - 1][0], 0 * meter, level)];
        }
        else
        {
            for (var p in runs[i])
            {
                const q = vector(p[0], 0 * meter, zRef + p[1] * PRIMITIVE_RADIUS_PLOT_SCALE * meter);
                if (size(points) == 0 || norm(q - points[size(points) - 1]) > 1e-8 * meter)
                {
                    points = append(points, q);
                }
            }
        }
        if (size(points) < 2 || norm(points[size(points) - 1] - points[0]) < 1e-7 * meter)
        {
            continue;
        }
        const rid = id + ("run" ~ i);
        opFitSpline(context, rid, { "points" : points });
        bodies = append(bodies, qCreatedBy(rid, EntityType.BODY));
    }
    return bodies;
}

/** Lowest and highest plot height (relative to the reference line) of the runs. */
export function primitiveRadiusExtent(runs is array) returns map
{
    var lo = 0 * meter;
    var hi = 0 * meter;
    for (var run in runs)
    {
        for (var p in run)
        {
            const h = p[1] * PRIMITIVE_RADIUS_PLOT_SCALE * meter;
            lo = min(lo, h);
            hi = max(hi, h);
        }
    }
    return { "lo" : lo, "hi" : hi };
}
