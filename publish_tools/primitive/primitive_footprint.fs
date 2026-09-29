FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: primitive_frame.fs
export import(path : "5808546b3b3d863d82796d24", version : "5933438d5f264793131ce0a6");
// IMPORT: footprint V32 fpt_analyze.fs (prepareFootprintCurves, analyzeFootprintCurves, computeAverageRadius)
import(path : "52724f3a857fa52d3ecceb77/b105c12d5094d99215293e38/71d853c0fd2f10ca3bb20a4b", version : "3b2e4e5538b5a476b5486c1d");

/**
 * Export Primitive -- the unwrapped footprint and the radius plot.
 *
 * Unwrapping (2026-09-28, the user's tail bite): a footprint point P moves to u = x(MRS) + s, where s is measured
 * along the BASE ITSELF at P's own transverse position y, towards the tip; y (across) is kept. The base is cut at
 * several y (0 and fractions of the periphery's half-widths, UNWRAP_SECTION_FRACTIONS) through the base faces only
 * (not the volume: its end walls are no base); on each section chain a point is mapped by its FOOT in XZ (nearest point,
 * Newton), and u is interpolated in y between the two sections around P that reach it. Every section is aligned at
 * x = x(MRS) (u = x(MRS) there on all of them). A point no section reaches is carried along the nearest section's end
 * tangent (last resort, beyond all base geometry). On a base extruded along y (a ski base) this is an isometry:
 * lengths and areas on the base are kept, a bite cut into the tail keeps its shape, and the steep ends are mapped by
 * the base's own slope (the old mapping took u from the mid-plane BOTTOM wire at the same x and clamped at its end:
 * a tail bite collapsed into a vertical line, and the section's rounded nose made the tip / tail corners noisy).
 * Where the base is flat and straight u = x, and such edges are copied exactly (arcs stay arcs); elsewhere the edge
 * is sampled, mapped and fitted (1 um). A FLAT input wire (constant z) is taken as already unwrapped (u = x): the
 * primitive aligns it at MRS; a wrapped input wire is mapped on the bottom wire (the only section there is).
 *
 * Radius / curvature: from the exact source curve and the mapping's derivatives (chain rule, no fitting). Along a
 * source edge (arc length t, P' = T, P'' = k N) the foot a on a section (tangent t, curvature kappa, normal n, height
 * h = n . (P - C)) moves with
 *     a' = (t . P') / D,   a'' = (N' D - (t . P') D') / D^2,   D = 1 - kappa h,  N' = t . P'' + kappa a' (n . P'),
 *     D' = -kappa (n . P')
 * and u between two sections at y0 < y1 (w = (y - y0) / (y1 - y0)) is u = (1 - w) u0 + w u1, differentiated with
 * w' = y' / (y1 - y0), w'' = y'' / (y1 - y0). Then with X' = u', X'' = u'', and y', y'' from the edge,
 *     k = (X' y'' - y' X'') / (X'^2 + y'^2)^1.5,
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
/** Clipped plot runs shorter than this along u are dropped (slivers at the region's ends). */
const CLIP_MIN_RUN = 0.1 * millimeter;
/** Where each bottom-wire edge is probed for the base faces (interior only: an end may sit on a cap edge). */
const BASE_PROBES = [0.1, 0.3, 0.5, 0.7, 0.9];
/** Base sections besides y = 0: these fractions of the periphery's largest +y and -y. */
const UNWRAP_SECTION_FRACTIONS = [0.45, 0.9];
/** Rows of a section's seed table. */
const SECTION_TABLE_ROWS = 200;
/** Newton steps for a point's foot on a section, and its convergence (tangential residual). */
const SECTION_FOOT_STEPS = 12;
const SECTION_FOOT_TOL = 1e-11 * meter;
/** A foot within this of a section's end still lies on the section (the point is "reached" by it). */
const SECTION_REACH_TOL = 1e-5 * meter;

/**
 * The footprint's source edges in the LOCAL frame: copies of the volume's BASE PERIPHERY (the boundary of the faces
 * the profile's bottom wire lies on -- the volume's own edges, so exact arcs stay exact), or copies of the input
 * wires. From the volume also the base SECTIONS the unwrap maps through (see primitiveBaseSections). Returns
 * { bodies (temporary copies, sections included), edges, sections (empty for input wires) }.
 *
 * Not opCreateOutline: it re-fits silhouette edges, and on RD 20TAC an exact R 1.02 m base arc came back as a spline
 * whose end curvature read R 3.93 m, and R 8.82 m on the mirrored copy (correction 57).
 */
export function primitiveFootprintSource(context is Context, id is Id, fromVolume is boolean, volume is Query, inputEdges is Query,
    frame is map, toDatum is Transform, toLocal is Transform, isIdentity is boolean) returns map
{
    var bodies = qNothing();
    var edges = qNothing();
    var sections = [];
    var sectionBodies = qNothing();
    if (fromVolume)
    {
        const base = basePeriphery(context, volume, frame.chain, toDatum);
        if (size(base.edges) == 0)
        {
            throw regenError("Could not find the volume's base periphery (the faces under the profile's bottom wire).", ["volume"]);
        }
        opExtractWires(context, id + "periphery", { "edges" : qUnion(base.edges) });
        bodies = qCreatedBy(id + "periphery", EntityType.BODY);
        primitiveMove(context, id + "peripheryToLocal", bodies, toLocal, isIdentity);
        edges = qOwnedByBody(bodies, EntityType.EDGE);
        const built = primitiveBaseSections(context, id + "sections", base.faces, bodies, frame, toDatum, toLocal, isIdentity);
        sections = built.sections;
        sectionBodies = built.bodies;
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
    return { "bodies" : qUnion([bodies, sectionBodies]), "edges" : list, "sections" : sections };
}

/**
 * The base periphery: the edges bounding exactly one of the BASE faces, where a base face is one the bottom wire lies
 * on (probed at interior points of every bottom edge, mapped back to the world with `toDatum`). Edges between two
 * base faces (split lines) are left out. Returns { faces (the base faces), edges }.
 */
function basePeriphery(context is Context, volume is Query, bottom is map, toDatum is Transform) returns map
{
    const volumeFaces = qOwnedByBody(volume, EntityType.FACE);
    var faces = {};
    for (var i = 0; i < size(bottom.edges); i += 1)
    {
        for (var tl in evEdgeTangentLines(context, { "edge" : bottom.edges[i], "parameters" : BASE_PROBES }))
        {
            for (var face in evaluateQuery(context, qContainsPoint(volumeFaces, toDatum * tl.origin)))
            {
                faces[toString(face)] = face;
            }
        }
    }
    var count = {};
    var edgeOf = {};
    var faceList = [];
    for (var entry in faces)
    {
        faceList = append(faceList, entry.value);
        for (var edge in evaluateQuery(context, qLoopEdges(entry.value)))
        {
            const key = toString(edge);
            count[key] = count[key] == undefined ? 1 : count[key] + 1;
            edgeOf[key] = edge;
        }
    }
    var out = [];
    for (var entry in count)
    {
        if (entry.value == 1)
        {
            out = append(out, edgeOf[entry.key]);
        }
    }
    return { "faces" : faceList, "edges" : out };
}

/**
 * The base cut at y = 0 and at UNWRAP_SECTION_FRACTIONS of the periphery's largest +y and -y (LOCAL frame): planes
 * through the base FACES only, in one opIntersectFaces. Per level the connected run that crosses x = x(MRS) (the
 * longest if several) becomes a section (unwrapSection); a level whose section misses x(MRS) is left out. Returns
 * { bodies (the section wires, LOCAL), sections (ordered by y) }.
 */
export function primitiveBaseSections(context is Context, id is Id, faces is array, periphery is Query, frame is map,
    toDatum is Transform, toLocal is Transform, isIdentity is boolean) returns map
{
    const box = evBox3d(context, { "topology" : periphery, "tight" : true });
    var levels = [0 * meter];
    for (var f in UNWRAP_SECTION_FRACTIONS)
    {
        if (box.maxCorner[1] > PRIMITIVE_CHAIN_TOLERANCE)
        {
            levels = append(levels, f * box.maxCorner[1]);
        }
        if (box.minCorner[1] < -PRIMITIVE_CHAIN_TOLERANCE)
        {
            levels = append(levels, f * box.minCorner[1]);
        }
    }
    const yAxis = toDatum.linear * vector(0, 1, 0);
    const xAxis = toDatum.linear * vector(1, 0, 0);
    var planes = [];
    for (var k = 0; k < size(levels); k += 1)
    {
        const pid = id + ("plane" ~ k);
        opPlane(context, pid, { "plane" : plane(toDatum * vector(0 * meter, levels[k], 0 * meter), yAxis, xAxis),
                    "width" : 20 * meter, "height" : 20 * meter });
        planes = append(planes, qCreatedBy(pid, EntityType.FACE));
    }
    opIntersectFaces(context, id + "cut", { "tools" : qUnion(planes), "targets" : qUnion(faces) });
    var planeBodies = [];
    for (var k = 0; k < size(levels); k += 1)
    {
        planeBodies = append(planeBodies, qCreatedBy(id + ("plane" ~ k), EntityType.BODY));
    }
    opDeleteBodies(context, id + "deletePlanes", { "entities" : qUnion(planeBodies) });
    const bodies = qCreatedBy(id + "cut", EntityType.BODY);
    primitiveMove(context, id + "cutToLocal", bodies, toLocal, isIdentity);

    // Edges by level (their midpoint's y), then one run per level.
    const records = primitiveUniqueRecords(primitiveEdgeRecords(context, evaluateQuery(context, qOwnedByBody(bodies, EntityType.EDGE))));
    var sections = [];
    for (var k = 0; k < size(levels); k += 1)
    {
        var mine = [];
        for (var r in records)
        {
            if (abs(r.mid[1] - levels[k]) < PRIMITIVE_CHAIN_TOLERANCE)
            {
                mine = append(mine, r);
            }
        }
        var best = undefined;
        var bestSpan = 0 * meter;
        for (var run in primitiveOrderRecords(mine))
        {
            var lo = run[0].start[0];
            var hi = lo;
            for (var link in run)
            {
                lo = min(lo, link.end[0]);
                hi = max(hi, link.end[0]);
            }
            if (lo <= frame.xMrs && hi >= frame.xMrs && hi - lo > bestSpan)
            {
                best = run;
                bestSpan = hi - lo;
            }
        }
        if (best == undefined)
        {
            continue;
        }
        // TAIL -> TIP, like the bottom wire.
        if (frame.dirSign * (best[size(best) - 1].end[0] - best[0].start[0]) < 0 * meter)
        {
            best = primitiveReverseRun(best);
        }
        const section = unwrapSection(context, primitiveChainFromRun(context, best), levels[k], frame);
        if (section != undefined)
        {
            sections = append(sections, section);
        }
    }
    sections = sort(sections, function(a, b) { return (a.y - b.y) / meter; });
    return { "bodies" : bodies, "sections" : sections };
}

/**
 * A mapping section: a chain in the plane y = `y` running TAIL -> TIP, with a seed table (a, x, z at
 * SECTION_TABLE_ROWS + 1 even arc positions; sense = +1 / -1 when x is strictly monotonic along it) and aMrs, its arc position at x = x(MRS). undefined when it has no point
 * at x(MRS).
 */
function unwrapSection(context is Context, chain is map, y is ValueWithUnits, frame is map)
{
    var arcs = [];
    for (var i = 0; i <= SECTION_TABLE_ROWS; i += 1)
    {
        arcs = append(arcs, chain.total * i / SECTION_TABLE_ROWS);
    }
    var xs = [];
    var zs = [];
    for (var r in primitiveChainEvaluate(context, chain, arcs))
    {
        xs = append(xs, r.point[0]);
        zs = append(zs, r.point[2]);
    }
    const at = primitiveChainAtX(context, chain, { "a" : arcs, "x" : xs }, [frame.xMrs])[0];
    if (abs(at.point[0] - frame.xMrs) > 1e-6 * meter || at.a < -SECTION_REACH_TOL || at.a > chain.total + SECTION_REACH_TOL)
    {
        return undefined;
    }
    // +1 / -1 when x rises / falls strictly along the table (bisection seeds), else 0.
    var sense = xs[size(xs) - 1] > xs[0] ? 1 : -1;
    for (var i = 0; i + 1 < size(xs); i += 1)
    {
        if (sense * (xs[i + 1] - xs[i]) <= 0 * meter)
        {
            sense = 0;
            break;
        }
    }
    return { "chain" : chain, "y" : y, "a" : arcs, "x" : xs, "z" : zs, "sense" : sense, "aMrs" : at.a };
}

/**
 * Chain points at arc positions `arcs` like primitiveChainEvaluate, but past an end the chain runs on straight along
 * that end's tangent (point moved, curvature 0).
 */
function sectionEvaluate(context is Context, chain is map, arcs is array) returns array
{
    var clamped = [];
    for (var a in arcs)
    {
        clamped = append(clamped, max(0 * meter, min(chain.total, a)));
    }
    var out = primitiveChainEvaluate(context, chain, clamped);
    for (var j = 0; j < size(arcs); j += 1)
    {
        const past = arcs[j] - clamped[j];
        if (abs(past) > 0 * meter)
        {
            out[j].point = out[j].point + past * out[j].tangent;
            out[j].curvature = 0 / meter;
        }
        out[j].a = arcs[j];
    }
    return out;
}

/** The seed table row of a point: the row nearest its x (bisection on a monotonic table), then downhill on the XZ distance. */
function sectionSeedRow(section is map, p is Vector) returns number
{
    const n = size(section.x);
    var row = 0;
    if (section.sense != 0)
    {
        const s = section.sense;
        if (s * (p[0] - section.x[0]) <= 0 * meter)
        {
            row = 0;
        }
        else if (s * (p[0] - section.x[n - 1]) >= 0 * meter)
        {
            row = n - 1;
        }
        else
        {
            var lo = 0;
            var hi = n - 1;
            while (hi - lo > 1)
            {
                const mid = floor((lo + hi) / 2);
                if (s * (section.x[mid] - p[0]) < 0 * meter)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }
            row = abs(section.x[lo] - p[0]) <= abs(section.x[hi] - p[0]) ? lo : hi;
        }
    }
    else
    {
        var best = abs(section.x[0] - p[0]);
        for (var i = 1; i < n; i += 1)
        {
            const d = abs(section.x[i] - p[0]);
            if (d < best)
            {
                best = d;
                row = i;
            }
        }
    }
    var d2 = (section.x[row] - p[0]) * (section.x[row] - p[0]) + (section.z[row] - p[2]) * (section.z[row] - p[2]);
    var moved = true;
    while (moved)
    {
        moved = false;
        for (var j in [row - 1, row + 1])
        {
            if (j < 0 || j >= n)
            {
                continue;
            }
            const dj = (section.x[j] - p[0]) * (section.x[j] - p[0]) + (section.z[j] - p[2]) * (section.z[j] - p[2]);
            if (dj < d2)
            {
                d2 = dj;
                row = j;
                moved = true;
            }
        }
    }
    return row;
}

/**
 * The foot of every point of `points` on `section` in XZ (Newton on g(a) = (P - C(a)) . t(a) = 0, slope 1 - kappa h,
 * all points batched per step): { a, reached (the foot lies on the section, not past an end), tangent, normal,
 * curvature (1/length), h (the point's height off the section along normal) }.
 */
function sectionFeet(context is Context, section is map, points is array) returns array
{
    var arcs = [];
    for (var p in points)
    {
        arcs = append(arcs, section.a[sectionSeedRow(section, p)]);
    }
    var results = makeArray(size(points));
    var active = size(points) == 0 ? [] : range(0, size(points) - 1);
    for (var step = 0; step <= SECTION_FOOT_STEPS && size(active) > 0; step += 1)
    {
        var activeArcs = [];
        for (var j in active)
        {
            activeArcs = append(activeArcs, arcs[j]);
        }
        const evaluated = sectionEvaluate(context, section.chain, activeArcs);
        var next = [];
        for (var k = 0; k < size(active); k += 1)
        {
            const j = active[k];
            const ev = evaluated[k];
            const d0 = points[j] - ev.point;
            const d = vector(d0[0], 0 * meter, d0[2]);
            const g = dot(d, ev.tangent);
            const h = dot(d, ev.normal);
            results[j] = { "a" : arcs[j], "tangent" : ev.tangent, "normal" : ev.normal, "curvature" : ev.curvature, "h" : h };
            if (step == SECTION_FOOT_STEPS || abs(g) < SECTION_FOOT_TOL)
            {
                continue;
            }
            var slope = 1 - ev.curvature * h;
            if (slope < 1e-3)
            {
                slope = 1;
            }
            arcs[j] = arcs[j] + g / slope;
            next = append(next, j);
        }
        active = next;
    }
    for (var j = 0; j < size(results); j += 1)
    {
        results[j].reached = results[j].a >= -SECTION_REACH_TOL && results[j].a <= section.chain.total + SECTION_REACH_TOL;
    }
    return results;
}

/**
 * u, u' and u'' (along the source edge's arc length) of a point from its foot on one section: P' = `d1` (unit tangent),
 * P'' = `d2` (curvature vector).
 */
function sectionU(frame is map, section is map, foot is map, d1 is Vector, d2 is Vector) returns map
{
    const t = foot.tangent;
    const n = foot.normal;
    const kappa = foot.curvature * meter;
    const h = foot.h / meter;
    const tp = dot(t, d1);
    const np = dot(n, d1);
    var den = 1 - kappa * h;
    if (den < 1e-3)
    {
        den = 1;
    }
    const ap = tp / den;
    const numP = dot(t, d2) + kappa * ap * np;
    const denP = -kappa * np;
    const app = (numP * den - tp * denP) / (den * den);
    return { "u" : frame.xMrs + frame.dirSign * (foot.a - section.aMrs), "up" : frame.dirSign * ap, "upp" : frame.dirSign * app };
}

/**
 * u, u', u'' of every point (see the header): the two sections around its y that reach it, interpolated in y; else
 * the nearest section in y that reaches it; else the nearest section's end tangent. `d1s` / `d2s` per point as in
 * sectionU, d2 in 1/m.
 */
function mapThroughSections(context is Context, frame is map, sections is array, points is array, d1s is array, d2s is array) returns array
{
    const nSec = size(sections);
    // The sections below / above each point's y.
    var below = [];
    var above = [];
    var askBy = makeArray(nSec, []);
    for (var j = 0; j < size(points); j += 1)
    {
        var lo = undefined;
        var hi = undefined;
        for (var k = 0; k < nSec; k += 1)
        {
            if (sections[k].y <= points[j][1])
            {
                lo = k;
            }
            if (hi == undefined && sections[k].y >= points[j][1])
            {
                hi = k;
            }
        }
        below = append(below, lo);
        above = append(above, hi);
        for (var k in [lo, hi])
        {
            if (k != undefined && (size(askBy[k]) == 0 || askBy[k][size(askBy[k]) - 1] != j))
            {
                askBy[k] = append(askBy[k], j);
            }
        }
    }
    var feet = makeArray(size(points), {});
    for (var k = 0; k < nSec; k += 1)
    {
        if (size(askBy[k]) == 0)
        {
            continue;
        }
        var pts = [];
        for (var j in askBy[k])
        {
            pts = append(pts, points[j]);
        }
        const got = sectionFeet(context, sections[k], pts);
        for (var i = 0; i < size(askBy[k]); i += 1)
        {
            feet[askBy[k][i]][k] = got[i];
        }
    }
    // Points neither neighbour reaches: every other section, nearest in y first.
    var retry = makeArray(nSec, []);
    for (var j = 0; j < size(points); j += 1)
    {
        const okLo = below[j] != undefined && feet[j][below[j]].reached;
        const okHi = above[j] != undefined && feet[j][above[j]].reached;
        if (!okLo && !okHi)
        {
            for (var k = 0; k < nSec; k += 1)
            {
                if (k != below[j] && k != above[j])
                {
                    retry[k] = append(retry[k], j);
                }
            }
        }
    }
    for (var k = 0; k < nSec; k += 1)
    {
        if (size(retry[k]) == 0)
        {
            continue;
        }
        var pts = [];
        for (var j in retry[k])
        {
            pts = append(pts, points[j]);
        }
        const got = sectionFeet(context, sections[k], pts);
        for (var i = 0; i < size(retry[k]); i += 1)
        {
            feet[retry[k][i]][k] = got[i];
        }
    }
    var out = [];
    for (var j = 0; j < size(points); j += 1)
    {
        const lo = below[j];
        const hi = above[j];
        const okLo = lo != undefined && feet[j][lo].reached;
        const okHi = hi != undefined && feet[j][hi].reached;
        if (okLo && okHi && lo != hi)
        {
            const m0 = sectionU(frame, sections[lo], feet[j][lo], d1s[j], d2s[j]);
            const m1 = sectionU(frame, sections[hi], feet[j][hi], d1s[j], d2s[j]);
            const span = (sections[hi].y - sections[lo].y) / meter;
            const w = (points[j][1] - sections[lo].y) / meter / span;
            const wp = d1s[j][1] / span;
            const wpp = d2s[j][1] / span;
            out = append(out, {
                        "u" : m0.u + (m1.u - m0.u) * w,
                        "up" : m0.up + (m1.up - m0.up) * w + wp * (m1.u - m0.u) / meter,
                        "upp" : m0.upp + (m1.upp - m0.upp) * w + 2 * wp * (m1.up - m0.up) + wpp * (m1.u - m0.u) / meter
                    });
            continue;
        }
        var use = okLo ? lo : (okHi ? hi : undefined);
        if (use == undefined)
        {
            // Nearest section in y that reaches the point; none: the nearest section's end tangent.
            var nearest = undefined;
            for (var k = 0; k < nSec; k += 1)
            {
                if (feet[j][k] == undefined)
                {
                    continue;
                }
                if (nearest == undefined || abs(sections[k].y - points[j][1]) < abs(sections[nearest].y - points[j][1]))
                {
                    nearest = k;
                }
                if (feet[j][k].reached && (use == undefined || abs(sections[k].y - points[j][1]) < abs(sections[use].y - points[j][1])))
                {
                    use = k;
                }
            }
            if (use == undefined)
            {
                use = nearest;
            }
        }
        out = append(out, sectionU(frame, sections[use], feet[j][use], d1s[j], d2s[j]));
    }
    return out;
}

/**
 * Unwraps the source edges into the LOCAL z = 0 plane through the base `sections` (primitiveBaseSections; empty =
 * the bottom wire, for input wires). Returns
 *     bodies    the unwrapped wires (exact copies and fitted edges)
 *     samples   per source edge { u : [], y : [], R : [], K : [] } (R in m, undefined = no radius there (|R| over the
 *               limit); K = signed curvature in 1/m, same sign as R, undefined only where the unwrap has no slope)
 *     exact / fitted   edge counts
 */
export function primitiveUnwrap(context is Context, id is Id, frame is map, edges is array, flatInput is boolean, radiusLimit is ValueWithUnits,
    sections is array) returns map
{
    // Pass 1: sample every edge; collect every sample that needs mapping.
    var data = [];
    var points = [];
    var d1s = [];
    var d2s = [];
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
                    "mapped" : mapped, "first" : size(points) });
        if (mapped)
        {
            for (var r in results)
            {
                points = append(points, r.frame.origin);
                d1s = append(d1s, r.frame.zAxis);
                d2s = append(d2s, r.frame.xAxis * (r.curvature * meter));
            }
        }
    }
    var maps = [];
    if (size(points) > 0)
    {
        var through = sections;
        if (size(through) == 0)
        {
            const bottom = unwrapSection(context, frame.chain, 0 * meter, frame);
            through = bottom == undefined ? [] : [bottom];
        }
        if (size(through) == 0)
        {
            throw regenError("The footprint cannot be unwrapped: the bottom wire has no point at the MRS.", ["volume"]);
        }
        maps = mapThroughSections(context, frame, through, points, d1s, d2s);
    }
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
        var curvatures = [];
        var points2 = [];
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
            var xp = tangent[0];
            var xpp = kc * r.frame.xAxis[0];
            if (e.mapped)
            {
                const m = maps[e.first + k];
                u = m.u;
                xp = m.up;
                xpp = m.upp;
            }
            const yp = tangent[1];
            const ypp = kc * r.frame.xAxis[1];
            const speed2 = xp * xp + yp * yp;
            var radius = undefined;
            var signed = undefined;
            if (speed2 > 1e-24 && abs(p[1]) > TOLERANCE.zeroLength * meter)
            {
                const ku = (xp * ypp - yp * xpp) / (speed2 * sqrt(speed2));
                const side = (ku * xp * (p[1] / meter)) > 0 ? 1 : -1;
                signed = side * abs(ku);
                if (abs(ku) > limitK)
                {
                    radius = side / abs(ku);
                }
            }
            us = append(us, u);
            ys = append(ys, p[1]);
            radii = append(radii, radius);
            curvatures = append(curvatures, signed);
            points2 = append(points2, vector(u, p[1], 0 * meter));
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
        samples = append(samples, { "u" : us, "y" : ys, "R" : radii, "K" : curvatures });
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
            fitted = append(fitted, { "points" : points2, "derivs" : derivs });
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
 * and the average radius, ALWAYS between the inflection points (user, 2026-09-28). Returns { result
 * (analyzeFootprintCurves map), average ({ valid, avgRadius }), lo, hi (the u range of the average) }.
 */
export function primitiveFootprintAnalysis(context is Context, unwrapped is Query, uFcp is ValueWithUnits, uAcp is ValueWithUnits,
    uMrs is ValueWithUnits) returns map
{
    const prepared = prepareFootprintCurves(context, qOwnedByBody(unwrapped, EntityType.EDGE), uFcp, uAcp, FPT_TOLERANCE);
    const zero = 0 * meter;
    const result = analyzeFootprintCurves(context, {
                "curveData" : prepared.curveData,
                "fcpPoint" : vector(uFcp, zero, zero),
                "acpPoint" : vector(uAcp, zero, zero),
                "mrsPoint" : vector(uMrs, zero, zero)
            });
    const lo = result.inflectionXMin;
    const hi = result.inflectionXMax;
    const average = computeAverageRadius(prepared.curveData, lo, hi, {});
    return { "result" : result, "average" : average, "lo" : lo, "hi" : hi };
}

/**
 * Where the unwrapped footprint crosses each u of `us` (from the samples; 5 mm spacing, < 1 um on sidecut radii): per
 * u { hit, yMax, yMin, radius (m, at the +y crossing; undefined = none) }. One pass per edge over only the u values
 * inside that edge's u range (an edge whose range misses a u cannot cross it); the same results as u by u.
 */
export function primitiveFootprintAtMany(samples is array, us is array) returns array
{
    var yMax = makeArray(size(us));
    var yMin = makeArray(size(us));
    var radius = makeArray(size(us));
    for (var e in samples)
    {
        if (size(e.u) < 2)
        {
            continue;
        }
        var lo = e.u[0];
        var hi = lo;
        for (var v in e.u)
        {
            lo = min(lo, v);
            hi = max(hi, v);
        }
        for (var q = 0; q < size(us); q += 1)
        {
            const u = us[q];
            if (u < lo || u > hi)
            {
                continue;
            }
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
                if (yMax[q] == undefined || y > yMax[q])
                {
                    yMax[q] = y;
                    radius[q] = r;
                }
                if (yMin[q] == undefined || y < yMin[q])
                {
                    yMin[q] = y;
                }
            }
        }
    }
    var out = [];
    for (var q = 0; q < size(us); q += 1)
    {
        out = append(out, { "hit" : yMax[q] != undefined, "yMax" : yMax[q], "yMin" : yMin[q], "radius" : radius[q] });
    }
    return out;
}

/**
 * The +y side's radius runs, ordered along u: each run is [[u, R], ...] (R in m) between breaks (no radius, or a
 * sign change). Runs meeting at an edge junction with (nearly) the same radius are joined end to end.
 */
export function primitiveRadiusRuns(samples is array) returns array
{
    return primitivePlotRuns(samples, "R", true, PRIMITIVE_RADIUS_PLOT_SCALE * meter);
}

/**
 * The +y side's plot runs of sample field `field` ("R" radius in m, "K" signed curvature in 1/m), ordered along u:
 * each run is [[u, value], ...] between breaks: no value, and with `breakOnSign` a sign change (the radius passes
 * infinity there; the curvature passes 0 and stays one run). Runs meeting at an edge junction whose plot heights
 * (value * perUnit) differ by less than PLOT_JOIN_TOLERANCE -- continuous curvature -- are joined at the average point.
 */
export function primitivePlotRuns(samples is array, field is string, breakOnSign is boolean, perUnit is ValueWithUnits) returns array
{
    var runs = [];
    for (var e in samples)
    {
        var pts = [];
        for (var k = 0; k < size(e.u); k += 1)
        {
            if (e.y[k] > TOLERANCE.zeroLength * meter)
            {
                pts = append(pts, { "u" : e.u[k], "v" : e[field][k] });
            }
        }
        pts = sort(pts, function(a, b) { return (a.u - b.u) / meter; });
        var run = [];
        for (var pt in pts)
        {
            if (pt.v == undefined || (breakOnSign && size(run) > 0 && (run[size(run) - 1][1] > 0) != (pt.v > 0)))
            {
                if (size(run) >= 2)
                {
                    runs = append(runs, run);
                }
                run = [];
            }
            if (pt.v != undefined)
            {
                run = append(run, [pt.u, pt.v]);
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
        if (abs(a[0] - b[0]) < 1e-6 * meter && abs(a[1] - b[1]) * perUnit < PLOT_JOIN_TOLERANCE)
        {
            const mid = [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2];
            runs[i][size(runs[i]) - 1] = mid;
            runs[i + 1][0] = mid;
        }
    }
    return runs;
}

/** The runs cut to lo <= u <= hi (a run crossing a bound ends on it, value interpolated); runs left with < 2 points dropped. */
export function primitiveClipRuns(runs is array, lo is ValueWithUnits, hi is ValueWithUnits) returns array
{
    var out = [];
    for (var run in runs)
    {
        var kept = [];
        for (var k = 0; k < size(run); k += 1)
        {
            const p = run[k];
            if (k > 0)
            {
                const q = run[k - 1];
                for (var bound in (p[0] >= q[0] ? [lo, hi] : [hi, lo]))
                {
                    // Crossing a bound between two samples: the point on it.
                    if ((q[0] < bound && p[0] > bound) || (q[0] > bound && p[0] < bound))
                    {
                        const f = (bound - q[0]) / (p[0] - q[0]);
                        kept = append(kept, [bound, q[1] + (p[1] - q[1]) * f]);
                    }
                }
            }
            if (p[0] >= lo && p[0] <= hi)
            {
                kept = append(kept, p);
            }
        }
        // A run that only touches the region (e.g. the tip arc starting at an inflection that is also an edge junction,
        // where the curvature jumps) leaves a sliver: dropped, it would stretch the scale for nothing visible.
        if (size(kept) >= 2 && abs(kept[size(kept) - 1][0] - kept[0][0]) >= CLIP_MIN_RUN)
        {
            out = append(out, kept);
        }
    }
    return out;
}

/**
 * The plot in the LOCAL XZ plane: one wire per run at (u, 0, zRef + value * perUnit); a run of constant value (an arc)
 * is a straight line. Returns the bodies.
 */
export function primitiveRadiusPlot(context is Context, id is Id, runs is array, zRef is ValueWithUnits, perUnit is ValueWithUnits) returns array
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
        if ((hi - lo) * perUnit < 1e-6 * meter)
        {
            const level = zRef + sum / size(runs[i]) * perUnit;
            points = [vector(runs[i][0][0], 0 * meter, level), vector(runs[i][size(runs[i]) - 1][0], 0 * meter, level)];
        }
        else
        {
            for (var p in runs[i])
            {
                const q = vector(p[0], 0 * meter, zRef + p[1] * perUnit);
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

/** Lowest and highest plot height (relative to the reference line) of the runs; 0 is always inside. */
export function primitiveRadiusExtent(runs is array, perUnit is ValueWithUnits) returns map
{
    var lo = 0 * meter;
    var hi = 0 * meter;
    for (var run in runs)
    {
        for (var p in run)
        {
            const h = p[1] * perUnit;
            lo = min(lo, h);
            hi = max(hi, h);
        }
    }
    return { "lo" : lo, "hi" : hi };
}

/**
 * The +y footprint's edge junctions, [{ u, y }] ordered along u: an unwrapped edge end shared with another edge's end
 * (within 1 um). Open ends (input wires) and ends on the centreline (y = 0) are not junctions.
 */
export function primitiveJunctions(samples is array) returns array
{
    var ends = [];
    for (var e in samples)
    {
        const n = size(e.u);
        if (n < 2)
        {
            continue;
        }
        for (var k in [0, n - 1])
        {
            if (e.y[k] > TOLERANCE.zeroLength * meter)
            {
                ends = append(ends, { "u" : e.u[k], "y" : e.y[k] });
            }
        }
    }
    var out = [];
    for (var i = 0; i < size(ends); i += 1)
    {
        var shared = false;
        for (var j = 0; j < size(ends); j += 1)
        {
            if (j != i && abs(ends[i].u - ends[j].u) < 1e-6 * meter && abs(ends[i].y - ends[j].y) < 1e-6 * meter)
            {
                shared = true;
                break;
            }
        }
        var seen = false;
        for (var have in out)
        {
            if (abs(have.u - ends[i].u) < 1e-6 * meter && abs(have.y - ends[i].y) < 1e-6 * meter)
            {
                seen = true;
                break;
            }
        }
        if (shared && !seen)
        {
            out = append(out, ends[i]);
        }
    }
    return sort(out, function(a, b) { return (a.u - b.u) / meter; });
}
