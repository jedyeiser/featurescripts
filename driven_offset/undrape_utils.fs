FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");
// IMPORT: edge_offset_utils.fs (same document)
export import(path : "a2665e22c07b7a6929ce4e80", version : "941e620c8511448a358a762b");

/**
 * UNDRAPE MAP: the flat outline of a constant-thickness plate draped over the target.
 *
 * Design and measurements: research_undrape_map.md. In one paragraph: at every outline sample's own
 * station (the plane normal to the reference wire W through it), the plate's mid-surface section is
 * measured from the EDGES of one side of the plate. The station plane is intersected with every edge
 * of that side (each edge sampled once: positions, edge tangents, side-face normals), the crossings
 * are ordered across the section, and each piece between consecutive crossings is taken as the circular
 * arc with the two end tangents (tangent = station normal x face normal). The side's arc becomes the
 * mid-surface arc with the turning correction sideSign * (t' / 2) * turn, t' = t / |in-plane part of
 * the normal|. The flat point is x = length along the target (W offset by d) and
 * y = chart.alignV - (signed mid arc from the centreline w = 0).
 *
 * Stations where the circle-per-piece model is not trusted (an edge crossed obliquely on a face that
 * the plane cuts lengthwise: the tail and tip U-turns of a pressed step, overhangs) are REFUSED and
 * measured by undrapeRefusedSection (the U-turn rule, kept in one place; see there).
 *
 * All inner loops run on plain numbers (metres, radians): FeatureScript unit arithmetic is operator
 * overloading and costs ~40x plain arithmetic; a 3-Vector expression costs ~20 us.
 *
 * Coordinates used throughout
 *   arc        chart arc of a station (metres along W, the packed chart's `arcs`)
 *   frame      a station: origin a on the offset reference, plane normal t (the reference tangent),
 *              width axis w (= the chart's planeNormal, the chart's v) and height axis h = w x t
 *              (the chart's surface normal, NOT flipped towards +Z)
 *   (w, h)     section coordinates of a point in its station plane, relative to a
 */

// ============================================================================
// Constants
// ============================================================================

/**
 * Spacing of the samples along each side edge, metres. Positions and edge tangents (evEdgeTangentLines,
 * cheap) are taken every UNDRAPE_EDGE_SAMPLE: the Hermite spans must follow the edge to ~0.1 um where W's
 * curvature changes (40 mm spans were 5 um off in height on the test part; research 4.2: 15 mm gave
 * 0.015 mm). The side normals (evFaceTangentPlanesAtEdge, ~7x dearer per point) are read at every
 * UNDRAPE_NORMAL_STEP-th sample, and at every sample of a stretch over which they turn more than
 * UNDRAPE_NORMAL_REFINE (radians); in between they are interpolated.
 */
export const UNDRAPE_EDGE_SAMPLE = 0.010;
export const UNDRAPE_NORMAL_STEP = 4;
export const UNDRAPE_NORMAL_REFINE = 0.035;

/** Largest turn of the edge tangent between two edge samples, radians (keeps closed/curled edges resolved). */
export const UNDRAPE_EDGE_TURN = 0.5;

/**
 * A station is refused (left to undrapeRefusedSection) where a crossed non-rim edge runs within
 * acos(UNDRAPE_MIN_CROSSING) = 18 deg of the station plane AND the face there is not normal to the plane
 * (in-plane part of its normal below UNDRAPE_MIN_INPLANE): the plane then cuts the face lengthwise and
 * one circular arc per face no longer fits. Face-end edges running straight across the part (normal in
 * the station plane) are crossed obliquely too but cut their faces across, and are kept.
 */
export const UNDRAPE_MIN_CROSSING = 0.95;
export const UNDRAPE_MIN_INPLANE = 0.995;

/** A crossing tangent with less width component than this is (nearly) vertical: overhangs are refused. */
export const UNDRAPE_OVERHANG = 0.05;

/** Crossings closer than this along a section are one point (a vertex met by several edges), metres. */
export const UNDRAPE_SAME_POINT = 1e-8;

/** An edge sample this close to a station plane lies in it (an edge running straight across), metres. */
export const UNDRAPE_IN_PLANE = 5e-8;

/** Two side faces whose normals along their common edge agree to within this cosine (0.06 deg) meet tangentially. */
export const UNDRAPE_CREASE_COS = 0.9999995;

/** Below this turning (radians) a piece is a straight chord. */
export const UNDRAPE_STRAIGHT = 1e-9;

/** A rim edge whose tangent keeps |t . u| >= this is sampled at the shared station grid ("lengthwise"). */
export const UNDRAPE_LENGTHWISE = 0.5;

/** Station arcs closer than this share one station, metres. */
export const UNDRAPE_STATION_MERGE = 1e-8;

/** Outline end points of rim edges closer than this are one vertex, metres. */
export const UNDRAPE_VERTEX_TOL = 1e-7;

/** Step between the three stations inside each edge end that give the flat edge its end tangent, metres. */
export const UNDRAPE_TANGENT_STEP = 2e-4;

/** A request point farther than this from every section node is interpolated rather than read, metres. */
export const UNDRAPE_NODE_TOL = 1e-7;

/** Samples per kernel-section edge in the refused-station fallback (non-arc-length, circle-corrected). */
export const UNDRAPE_KERNEL_SAMPLES = 9;

/** Station shifts tried (metres, in order) when the kernel cannot section a refused station. */
export const UNDRAPE_KERNEL_RETRIES = [0, 1e-6, -1e-6, 5e-5, -5e-5];

// Crossing record: an array of numbers, indexed
//   0 w, 1 h                   section coordinates (metres)
//   2 twA, 3 thA               unit section tangent of the piece ARRIVING from lower w (crease: that face's)
//   4 twB, 5 thB               unit section tangent of the piece LEAVING towards higher w
//   6 inPlane                  length of the side normal's in-plane part (1 / the plate's obliquity)
//   7 cosCross                 |edge tangent . t|: 1 when the edge runs straight through the plane
//   8 rim                      1 on a rim edge (the section may end here), else 0
//   9 edge                     edge-table index
//   10 nx, 11 ny, 12 nz        unit outward side normal (world)
//   13 valid                   1 when the section tangent is defined
//   14 px, 15 py, 16 pz        world point (metres)
//   17 sample                  1 when emitted as an edge sample lying in the plane (not a transverse crossing)
//   18 span                    the edge-table span it lies in (a warm start for the next station)

// ============================================================================
// Chart access (plain numbers)
// ============================================================================

/**
 * The packed chart at arc a: [ax, ay, az, tx, ty, tz, theta, kappa, scale]. Same cubic as
 * edge_offset_utils' chartEval (whose foot chartFoot solves), with a binary span search so that
 * stations in any order cost O(log n). Past either end the reference runs on straight.
 */
export function undrapeChartAt(c is map, a is number) returns array
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
    var low = 0;
    var high = last - 1;
    while (low < high)
    {
        const half = floor((low + high) / 2);
        if (a <= c.arcs[half + 1])
        {
            high = half;
        }
        else
        {
            low = half + 1;
        }
    }
    const i = low;
    const k = i + 1;
    const hs = c.arcs[k] - c.arcs[i];
    const f = (a - c.arcs[i]) / hs;
    const f2 = f * f;
    const f3 = f2 * f;
    const h00 = 2 * f3 - 3 * f2 + 1;
    const h10 = (f3 - 2 * f2 + f) * hs;
    const h01 = 3 * f2 - 2 * f3;
    const h11 = (f3 - f2) * hs;
    const g0 = (6 * f2 - 6 * f) / hs;
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
 * A station: its plane and the flat x of everything on it (before the alignment is subtracted).
 * x = arc - delta * theta(arc) is the length along the target (W offset by delta), as in unwrapFast.
 */
export function undrapeFrame(c is map, a is number) returns map
{
    const e = undrapeChartAt(c, a);
    return {
        "arc" : a,
        "x" : a - c.delta * e[6],
        "a" : [e[0], e[1], e[2]],
        "t" : [e[3], e[4], e[5]],
        "w" : [c.nx, c.ny, c.nz],
        "h" : [c.ny * e[5] - c.nz * e[4], c.nz * e[3] - c.nx * e[5], c.nx * e[4] - c.ny * e[3]],
        "kappa" : e[7],
        "scale" : e[8]
    };
}

/** Seed arc for chartFoot from a world X: linear in the packed samples (W runs in +X, unwrapChart checks). */
export function undrapeSeedArc(c is map, qx is number) returns number
{
    const last = c.count - 1;
    if (qx <= c.px[0])
    {
        return c.arcs[0] + (qx - c.px[0]);
    }
    if (qx >= c.px[last])
    {
        return c.arcs[last] + (qx - c.px[last]);
    }
    var low = 0;
    var high = last - 1;
    while (low < high)
    {
        const half = floor((low + high) / 2);
        if (qx <= c.px[half + 1])
        {
            high = half;
        }
        else
        {
            low = half + 1;
        }
    }
    const span = c.px[low + 1] - c.px[low];
    if (span < 1e-15)
    {
        return c.arcs[low];
    }
    return c.arcs[low] + (c.arcs[low + 1] - c.arcs[low]) * (qx - c.px[low]) / span;
}

/** Span of the packed chart holding arc a (binary search): the hint chartFoot walks from. */
export function undrapeSpanOf(c is map, a is number) returns number
{
    var low = 0;
    var high = c.count - 2;
    while (low < high)
    {
        const half = floor((low + high) / 2);
        if (a <= c.arcs[half + 1])
        {
            high = half;
        }
        else
        {
            low = half + 1;
        }
    }
    return low;
}

/** Chart arc of a world point (plain metres): the station whose plane holds it. */
export function undrapeFootArc(c is map, p is array, seed) returns number
{
    const a0 = (seed == undefined) ? undrapeSeedArc(c, p[0]) : seed;
    return chartFoot(c, p[0], p[1], p[2], a0, undrapeSpanOf(c, a0))[0];
}

// ============================================================================
// Edge sampling and tables (kernel, once per side)
// ============================================================================

/** Arc estimate of an edge from a few evEdgeTangentLines samples: circle-corrected chords. Also the total turn. */
export function undrapeRoughLength(lines is array) returns array
{
    var length = 0;
    var turn = 0;
    for (var i = 0; i + 1 < size(lines); i += 1)
    {
        const d = (lines[i + 1].origin - lines[i].origin) / meter;
        const chord = sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
        const phi = acos(clamp(dot(lines[i].direction, lines[i + 1].direction), -1, 1)) / radian;
        length += chord * (1 + phi * phi / 24);
        turn += phi;
    }
    return [length, turn];
}

/**
 * One edge of a plate side as plain-number tables for the per-station plane-crossing search.
 *
 * @param lines {array} : evEdgeTangentLines at the samples (origin, direction); the tangents are re-signed
 *      to run with the samples. Each span's Hermite tangent length is its chord corrected for the turn
 *      between its end tangents, chord * (1 + phi^2 / 24): the arc of the circle through both ends with
 *      those tangents, so non-arc-length sampling is harmless.
 * @param normals {array} : unit outward side-face normal [x, y, z] at each sample.
 * @param others : undefined, or the second face's normals at each sample for a crease edge.
 * @param intoSign {number} : for a crease, +1 when cross(normal, tangent) points into the first face.
 * @returns {map} : { count, px py pz, ux uy uz, nx ny nz, [mx my mz, ix iy iz], mags, cum (arc at each
 *      sample), length, turn, cx cy cz / hx hy hz (padded box centre / half-extents), onRim, creased }
 */
export function undrapeEdgeTable(lines is array, normals is array, others, intoSign is number, onRim is boolean) returns map
{
    const count = size(lines);
    var px = makeArray(count);
    var py = makeArray(count);
    var pz = makeArray(count);
    var ux = makeArray(count);
    var uy = makeArray(count);
    var uz = makeArray(count);
    var nx = makeArray(count);
    var ny = makeArray(count);
    var nz = makeArray(count);
    for (var i = 0; i < count; i += 1)
    {
        const o = lines[i].origin;
        const u = lines[i].direction;
        px[i] = o[0].value;
        py[i] = o[1].value;
        pz[i] = o[2].value;
        ux[i] = u[0];
        uy[i] = u[1];
        uz[i] = u[2];
        nx[i] = normals[i][0];
        ny[i] = normals[i][1];
        nz[i] = normals[i][2];
    }

    // Tangents must run with the samples for the Hermite spans.
    var along = 0;
    for (var i = 0; i + 1 < count; i += 1)
    {
        along += (ux[i] + ux[i + 1]) * (px[i + 1] - px[i]) + (uy[i] + uy[i + 1]) * (py[i + 1] - py[i])
            + (uz[i] + uz[i + 1]) * (pz[i + 1] - pz[i]);
    }
    const flip = (along < 0) ? -1 : 1;
    if (flip < 0)
    {
        for (var i = 0; i < count; i += 1)
        {
            ux[i] = -ux[i];
            uy[i] = -uy[i];
            uz[i] = -uz[i];
        }
    }

    // A crease: faces meeting tangentially (evEdgeConvexity reports CONVEX / CONCAVE within its tolerance)
    // are none.
    var creased = false;
    if (others != undefined)
    {
        for (var i = 0; i < count; i += 1)
        {
            if (nx[i] * others[i][0] + ny[i] * others[i][1] + nz[i] * others[i][2] < UNDRAPE_CREASE_COS)
            {
                creased = true;
            }
        }
    }
    var mx = creased ? makeArray(count) : [];
    var my = creased ? makeArray(count) : [];
    var mz = creased ? makeArray(count) : [];
    var ix = creased ? makeArray(count) : [];
    var iy = creased ? makeArray(count) : [];
    var iz = creased ? makeArray(count) : [];
    if (creased)
    {
        // into the first face: sign * (normal x the edge's own direction); ux.. now carry `flip`
        const sg = intoSign * flip;
        for (var i = 0; i < count; i += 1)
        {
            mx[i] = others[i][0];
            my[i] = others[i][1];
            mz[i] = others[i][2];
            ix[i] = sg * (ny[i] * uz[i] - nz[i] * uy[i]);
            iy[i] = sg * (nz[i] * ux[i] - nx[i] * uz[i]);
            iz[i] = sg * (nx[i] * uy[i] - ny[i] * ux[i]);
        }
    }

    var mags = makeArray(max(count - 1, 0));
    var cum = makeArray(count, 0);
    var lx = px[0];
    var ly = py[0];
    var lz = pz[0];
    var hx = px[0];
    var hy = py[0];
    var hz = pz[0];
    var pad = 0;
    var turn = 0;
    for (var i = 0; i < count; i += 1)
    {
        lx = min(lx, px[i]);
        ly = min(ly, py[i]);
        lz = min(lz, pz[i]);
        hx = max(hx, px[i]);
        hy = max(hy, py[i]);
        hz = max(hz, pz[i]);
        if (i + 1 < count)
        {
            const dx = px[i + 1] - px[i];
            const dy = py[i + 1] - py[i];
            const dz = pz[i + 1] - pz[i];
            const chord = sqrt(dx * dx + dy * dy + dz * dz);
            // turn between the end tangents: 2 (1 - cos) + (2 (1 - cos))^2 / 12 = phi^2 to O(phi^6)
            const c = clamp(ux[i] * ux[i + 1] + uy[i] * uy[i + 1] + uz[i] * uz[i + 1], -1, 1);
            var phi2 = 2 * (1 - c);
            phi2 = (c > 0.9) ? phi2 + phi2 * phi2 / 12 : (acos(c) / radian) * (acos(c) / radian);
            const phi = sqrt(phi2);
            mags[i] = chord * (1 + phi2 / 24);
            cum[i + 1] = cum[i] + mags[i];
            // the span may bulge past its end samples by about its sagitta
            pad = max(pad, mags[i] * (phi / 4 + 0.02));
            turn += phi;
        }
    }
    pad += 1e-7;
    const lo = [lx, ly, lz];
    const hi = [hx, hy, hz];

    return {
        "count" : count,
        "px" : px, "py" : py, "pz" : pz,
        "ux" : ux, "uy" : uy, "uz" : uz,
        "nx" : nx, "ny" : ny, "nz" : nz,
        "mx" : mx, "my" : my, "mz" : mz,
        "ix" : ix, "iy" : iy, "iz" : iz,
        "mags" : mags, "cum" : cum, "length" : cum[count - 1], "turn" : turn,
        "cx" : 0.5 * (lo[0] + hi[0]), "cy" : 0.5 * (lo[1] + hi[1]), "cz" : 0.5 * (lo[2] + hi[2]),
        "hx" : 0.5 * (hi[0] - lo[0]) + pad, "hy" : 0.5 * (hi[1] - lo[1]) + pad, "hz" : 0.5 * (hi[2] - lo[2]) + pad,
        "onRim" : onRim,
        "creased" : creased
    };
}

/**
 * Sample every edge of one plate side (kernel, once). onRim: the edge also bounds a face that is not on
 * this side (a wall), so it is part of the side's outline.
 *
 * Crease edges between two side faces (not tangent-continuous) are sampled on both faces when they run
 * along the reference (|edge tangent . t| >= UNDRAPE_LENGTHWISE at their middle): there the two faces give
 * the section different tangents either side of the crossing. A crease running across the reference (a
 * face end kinked lengthwise, common on pressed parts) turns the surface about a line lying nearly in the
 * station plane, which leaves the section's tangent almost unchanged; the first face serves, and the
 * piece turns telescope in the mid-surface correction anyway.
 *
 * @param c {map} : the packed chart (chart.packed).
 * @returns {map} : { "tables" (undrapeEdgeTable maps, plus "edge" : the edge Query), "faceCount" }
 */
export function undrapeSampleSide(context is Context, side is Query, c is map) returns map
{
    const faces = evaluateQuery(context, side);
    // Edges and their side faces from the faces (fewer queries than asking every edge for its faces).
    // Every edge of a solid bounds two faces: an edge with one side face bounds a wall, i.e. is on the rim.
    var edges = [];
    var facesOf = {};
    for (var f in faces)
    {
        for (var edge in evaluateQuery(context, qAdjacent(f, AdjacencyType.EDGE, EntityType.EDGE)))
        {
            const key = edge.transientId;
            if (facesOf[key] == undefined)
            {
                facesOf[key] = [f];
                edges = append(edges, edge);
            }
            else
            {
                facesOf[key] = append(facesOf[key], f);
            }
        }
    }
    var tables = makeArray(size(edges));
    for (var k = 0; k < size(edges); k += 1)
    {
        const edge = edges[k];
        const sideFaces = facesOf[edge.transientId];
        const onRim = size(sideFaces) < 2;

        const rough3 = evEdgeTangentLines(context, { "edge" : edge, "parameters" : [0, 0.5, 1], "arcLengthParameterization" : false });
        const rough = undrapeRoughLength(rough3);
        const n = max([3, ceil(rough[0] / UNDRAPE_EDGE_SAMPLE) + 1, ceil(rough[1] / UNDRAPE_EDGE_TURN) + 1]);
        const params = range(0, 1, n);

        // positions and tangents at every sample
        var arcLength = false;
        var lines = (n == 3) ? rough3 : evEdgeTangentLines(context, { "edge" : edge, "parameters" : params, "arcLengthParameterization" : false });
        if (undrapeUneven(lines))
        {
            // a very uneven parameterization leaves long spans between the samples: resample by arc length
            arcLength = true;
            lines = evEdgeTangentLines(context, { "edge" : edge, "parameters" : params, "arcLengthParameterization" : true });
        }

        var creased = false;
        if (size(sideFaces) > 1)
        {
            const o = rough3[1].origin;
            const a0 = undrapeSeedArc(c, o[0].value);
            const f = chartFoot(c, o[0].value, o[1].value, o[2].value, a0, undrapeSpanOf(c, a0));
            const u = rough3[1].direction;
            creased = abs(u[0] * f[5] + u[1] * f[6] + u[2] * f[7]) >= UNDRAPE_LENGTHWISE
                && evEdgeConvexity(context, { "edge" : edge }) != EdgeConvexityType.SMOOTH;
        }

        const normals = undrapeSideNormals(context, edge, sideFaces[0], params, arcLength, lines);
        var others = undefined;
        var intoSign = 1;
        if (creased)
        {
            others = undrapeSideNormals(context, edge, sideFaces[1], params, arcLength, lines);
            // which way the first face lies: its loop direction at the edge's middle (the same point either way)
            const oriented = evFaceTangentPlanesAtEdge(context, { "edge" : edge, "face" : sideFaces[0], "parameters" : [0.5],
                        "arcLengthParameterization" : false, "usingFaceOrientation" : true });
            intoSign = (dot(oriented[0].x, rough3[1].direction) >= 0) ? 1 : -1;
        }
        var table = undrapeEdgeTable(lines, normals, others, intoSign, onRim);
        table.edge = edge;
        tables[k] = table;
    }
    return { "tables" : tables, "faceCount" : size(faces) };
}

/**
 * A face's unit outward normals at every sample of an edge: read at every UNDRAPE_NORMAL_STEP-th sample,
 * then at every sample between two reads that differ by more than UNDRAPE_NORMAL_REFINE; the rest
 * interpolated linearly in the parameter and made normal to the edge tangent there.
 * @returns {array} : [x, y, z] per sample
 */
export function undrapeSideNormals(context is Context, edge is Query, face is Query, params is array, arcLength is boolean,
    lines is array) returns array
{
    const n = size(params);
    // a short edge is read whole: its few samples would be refined anyway
    const step = (n <= UNDRAPE_NORMAL_STEP + 1) ? 1 : UNDRAPE_NORMAL_STEP;
    var read = [];
    for (var i = 0; i < n - 1; i += step)
    {
        read = append(read, i);
    }
    read = append(read, n - 1);
    var normals = makeArray(n);
    var known = makeArray(n, false);
    var ask = makeArray(size(read));
    for (var j = 0; j < size(read); j += 1)
    {
        ask[j] = params[read[j]];
    }
    const planes = evFaceTangentPlanesAtEdge(context, { "edge" : edge, "face" : face, "parameters" : ask,
                "arcLengthParameterization" : arcLength });
    for (var j = 0; j < size(read); j += 1)
    {
        const nn = planes[j].normal;
        normals[read[j]] = [nn[0], nn[1], nn[2]];
        known[read[j]] = true;
    }

    // refine where the normal turns
    const limit = cos(UNDRAPE_NORMAL_REFINE * radian);
    var more = [];
    for (var j = 0; j + 1 < size(read); j += 1)
    {
        const a = normals[read[j]];
        const b = normals[read[j + 1]];
        if (read[j + 1] - read[j] > 1 && a[0] * b[0] + a[1] * b[1] + a[2] * b[2] < limit)
        {
            for (var i = read[j] + 1; i < read[j + 1]; i += 1)
            {
                more = append(more, i);
            }
        }
    }
    if (size(more) > 0)
    {
        var askMore = makeArray(size(more));
        for (var j = 0; j < size(more); j += 1)
        {
            askMore[j] = params[more[j]];
        }
        const extra = evFaceTangentPlanesAtEdge(context, { "edge" : edge, "face" : face, "parameters" : askMore,
                    "arcLengthParameterization" : arcLength });
        for (var j = 0; j < size(more); j += 1)
        {
            const nn = extra[j].normal;
            normals[more[j]] = [nn[0], nn[1], nn[2]];
            known[more[j]] = true;
        }
    }

    // interpolate the rest
    var lo = 0;
    for (var i = 1; i < n; i += 1)
    {
        if (known[i])
        {
            lo = i;
            continue;
        }
        var hi = i + 1;
        while (!known[hi])
        {
            hi += 1;
        }
        const f = (params[i] - params[lo]) / (params[hi] - params[lo]);
        const a = normals[lo];
        const b = normals[hi];
        var x = a[0] + f * (b[0] - a[0]);
        var y = a[1] + f * (b[1] - a[1]);
        var z = a[2] + f * (b[2] - a[2]);
        const u = lines[i].direction;
        const d = x * u[0] + y * u[1] + z * u[2];
        x = x - d * u[0];
        y = y - d * u[1];
        z = z - d * u[2];
        const len = sqrt(x * x + y * y + z * z);
        normals[i] = [x / len, y / len, z / len];
    }
    return normals;
}

/** Whether some span of the samples is much longer than their average (an uneven parameterization). */
export function undrapeUneven(planes is array) returns boolean
{
    const count = size(planes);
    var chords = makeArray(count - 1);
    var total = 0;
    for (var i = 0; i + 1 < count; i += 1)
    {
        chords[i] = norm(planes[i + 1].origin - planes[i].origin).value;
        total += chords[i];
    }
    const limit = 2.5 * total / (count - 1) + 1e-6;
    for (var c in chords)
    {
        if (c > limit)
        {
            return true;
        }
    }
    return false;
}

/**
 * Point and outward normal on an edge table at arc length s from its first sample (Hermite span, linear
 * normal). @returns {array} : [x, y, z, nx, ny, nz]
 */
export function undrapeEdgePoint(tb is map, s is number) returns array
{
    const last = tb.count - 1;
    var i = 0;
    while (i < last - 1 && s > tb.cum[i + 1])
    {
        i += 1;
    }
    const m = tb.mags[i];
    const f = (m < 1e-15) ? 0 : clamp((s - tb.cum[i]) / m, 0, 1);
    const f2 = f * f;
    const f3 = f2 * f;
    const b0 = 2 * f3 - 3 * f2 + 1;
    const b1 = (f3 - 2 * f2 + f) * m;
    const b2 = -2 * f3 + 3 * f2;
    const b3 = (f3 - f2) * m;
    var nx = (1 - f) * tb.nx[i] + f * tb.nx[i + 1];
    var ny = (1 - f) * tb.ny[i] + f * tb.ny[i + 1];
    var nz = (1 - f) * tb.nz[i] + f * tb.nz[i + 1];
    const nn = sqrt(nx * nx + ny * ny + nz * nz);
    return [b0 * tb.px[i] + b1 * tb.ux[i] + b2 * tb.px[i + 1] + b3 * tb.ux[i + 1],
            b0 * tb.py[i] + b1 * tb.uy[i] + b2 * tb.py[i + 1] + b3 * tb.uy[i + 1],
            b0 * tb.pz[i] + b1 * tb.uz[i] + b2 * tb.pz[i + 1] + b3 * tb.uz[i + 1],
            nx / nn, ny / nn, nz / nn];
}

// ============================================================================
// Candidate edges per station
// ============================================================================

/** Signed distance of an edge table's box centre from a station plane, minus / plus how far the box reaches. */
export function undrapeBoxRange(tb is map, fr is map) returns array
{
    const t = fr.t;
    const d = (tb.cx - fr.a[0]) * t[0] + (tb.cy - fr.a[1]) * t[1] + (tb.cz - fr.a[2]) * t[2];
    const reach = tb.hx * abs(t[0]) + tb.hy * abs(t[1]) + tb.hz * abs(t[2]);
    return [d - reach, d + reach];
}

/**
 * For each station (frames sorted by arc), the indices of the edge tables whose boxes its plane crosses.
 * A fixed point's distance to the station plane falls monotonically with the arc wherever the chart is
 * valid (1 - kappa * height > 0), so each box is crossed by one contiguous run of stations, found by two
 * binary searches: O(edges * log(stations)).
 */
export function undrapeCandidateEdges(tables is array, frames is array) returns array
{
    const count = size(frames);
    var lists = makeArray(count, []);
    for (var e = 0; e < size(tables); e += 1)
    {
        const tb = tables[e];
        // first station with the box's far corner at or behind its plane
        var low = 0;
        var high = count;
        while (low < high)
        {
            const half = floor((low + high) / 2);
            if (undrapeBoxRange(tb, frames[half])[0] <= 0)
            {
                high = half;
            }
            else
            {
                low = half + 1;
            }
        }
        const first = low;
        // first station whose plane has passed the box's near corner
        high = count;
        while (low < high)
        {
            const half = floor((low + high) / 2);
            if (undrapeBoxRange(tb, frames[half])[1] < 0)
            {
                high = half;
            }
            else
            {
                low = half + 1;
            }
        }
        for (var i = first; i < low; i += 1)
        {
            lists[i] = append(lists[i], e);
        }
    }
    return lists;
}

// ============================================================================
// Crossings and sections (pure math, plain metres)
// ============================================================================

/** Unit section tangent in (w, h) of a face with unit normal n: t x n, oriented tw >= 0. [tw, th, inPlane, valid] */
export function undrapeTangent(fr is map, nx is number, ny is number, nz is number) returns array
{
    const t = fr.t;
    const sx = t[1] * nz - t[2] * ny;
    const sy = t[2] * nx - t[0] * nz;
    const sz = t[0] * ny - t[1] * nx;
    var tw = sx * fr.w[0] + sy * fr.w[1] + sz * fr.w[2];
    var th = sx * fr.h[0] + sy * fr.h[1] + sz * fr.h[2];
    const tn = sqrt(tw * tw + th * th);
    if (tn < 1e-9)
    {
        return [1, 0, 0, 0];
    }
    tw = tw / tn;
    th = th / tn;
    if (tw < 0)
    {
        tw = -tw;
        th = -th;
    }
    return [tw, th, tn, 1];
}

/**
 * The crossing record (see the table at the top of the file) of edge table `tb` (index e) at span i,
 * fraction f of the span, in station `fr`.
 */
export function undrapeCrossingAt(tb is map, e is number, i is number, f is number, fr is map, sample is number) returns array
{
    const a = fr.a;
    const t0 = fr.t[0];
    const t1 = fr.t[1];
    const t2 = fr.t[2];
    const w0 = fr.w[0];
    const w1 = fr.w[1];
    const w2 = fr.w[2];
    const h0 = fr.h[0];
    const h1 = fr.h[1];
    const h2 = fr.h[2];
    const px = tb.px;
    const py = tb.py;
    const pz = tb.pz;
    const ux = tb.ux;
    const uy = tb.uy;
    const uz = tb.uz;
    var qx = 0;
    var qy = 0;
    var qz = 0;
    // interpolation between samples j and k with weight g on k
    var j = i;
    var k = i + 1;
    var g = f;
    if (f <= 0)
    {
        qx = px[i];
        qy = py[i];
        qz = pz[i];
        k = i;
        g = 0;
    }
    else if (f >= 1)
    {
        qx = px[i + 1];
        qy = py[i + 1];
        qz = pz[i + 1];
        j = i + 1;
        g = 0;
    }
    else
    {
        const m = tb.mags[i];
        const f2 = f * f;
        const f3 = f2 * f;
        const b0 = 2 * f3 - 3 * f2 + 1;
        const b1 = (f3 - 2 * f2 + f) * m;
        const b2 = -2 * f3 + 3 * f2;
        const b3 = (f3 - f2) * m;
        qx = b0 * px[i] + b1 * ux[i] + b2 * px[i + 1] + b3 * ux[i + 1];
        qy = b0 * py[i] + b1 * uy[i] + b2 * py[i + 1] + b3 * uy[i + 1];
        qz = b0 * pz[i] + b1 * uz[i] + b2 * pz[i + 1] + b3 * uz[i + 1];
    }
    const dx = qx - a[0];
    const dy = qy - a[1];
    const dz = qz - a[2];
    const r = 1 - g;
    const cosCross = abs(r * (ux[j] * t0 + uy[j] * t1 + uz[j] * t2) + g * (ux[k] * t0 + uy[k] * t1 + uz[k] * t2));

    var nx = r * tb.nx[j] + g * tb.nx[k];
    var ny = r * tb.ny[j] + g * tb.ny[k];
    var nz = r * tb.nz[j] + g * tb.nz[k];
    const nn = sqrt(nx * nx + ny * ny + nz * nz);
    nx = nx / nn;
    ny = ny / nn;
    nz = nz / nn;

    // section tangent t x n in (w, h), oriented tw >= 0
    const sx = t1 * nz - t2 * ny;
    const sy = t2 * nx - t0 * nz;
    const sz = t0 * ny - t1 * nx;
    var tw = sx * w0 + sy * w1 + sz * w2;
    var th = sx * h0 + sy * h1 + sz * h2;
    var inPlane = sqrt(tw * tw + th * th);
    var valid = 1;
    if (inPlane < 1e-9)
    {
        tw = 1;
        th = 0;
        valid = 0;
    }
    else
    {
        tw = tw / inPlane;
        th = th / inPlane;
        if (tw < 0)
        {
            tw = -tw;
            th = -th;
        }
    }
    var twA = tw;
    var thA = th;
    var twB = tw;
    var thB = th;
    if (tb.creased)
    {
        var mx = r * tb.mx[j] + g * tb.mx[k];
        var my = r * tb.my[j] + g * tb.my[k];
        var mz = r * tb.mz[j] + g * tb.mz[k];
        const mn = sqrt(mx * mx + my * my + mz * mz);
        const s2 = undrapeTangent(fr, mx / mn, my / mn, mz / mn);
        // Face 1 lies on the +w side of the crossing when its inward direction runs with its section tangent.
        const ix = r * tb.ix[j] + g * tb.ix[k];
        const iy = r * tb.iy[j] + g * tb.iy[k];
        const iz = r * tb.iz[j] + g * tb.iz[k];
        const into = ix * (tw * w0 + th * h0) + iy * (tw * w1 + th * h1) + iz * (tw * w2 + th * h2);
        const intoNorm = sqrt(ix * ix + iy * iy + iz * iz);
        if (abs(into) < 0.1 * intoNorm)
        {
            // The faces lie before and after the station plane, not either side of the crossing (a face end
            // running across, kinked lengthwise): both give nearly the section's own tangent. Average them.
            const aw = tw + s2[0];
            const ah = th + s2[1];
            const an = sqrt(aw * aw + ah * ah);
            twA = aw / an;
            thA = ah / an;
            twB = twA;
            thB = thA;
        }
        else if (into > 0)
        {
            twA = s2[0];
            thA = s2[1];
        }
        else
        {
            twB = s2[0];
            thB = s2[1];
        }
        inPlane = 0.5 * (inPlane + s2[2]);
        valid = min(valid, s2[3]);
    }
    return [dx * w0 + dy * w1 + dz * w2, dx * h0 + dy * h1 + dz * h2,
            twA, thA, twB, thB, inPlane, cosCross, tb.onRim ? 1 : 0, e, nx, ny, nz, valid, qx, qy, qz, sample, i];
}

/** Newton on Hermite span i of `tb` for the plane crossing, bracketed by distances g0, g1. Returns f. */
export function undrapeSpanRoot(tb is map, i is number, g0 is number, g1 is number, t is array) returns number
{
    const m = tb.mags[i];
    const d0 = m * (tb.ux[i] * t[0] + tb.uy[i] * t[1] + tb.uz[i] * t[2]);
    const d1 = m * (tb.ux[i + 1] * t[0] + tb.uy[i + 1] * t[1] + tb.uz[i + 1] * t[2]);
    var f = g0 / (g0 - g1);
    for (var step = 0; step < 8; step += 1)
    {
        const f2 = f * f;
        const f3 = f2 * f;
        const gf = (2 * f3 - 3 * f2 + 1) * g0 + (f3 - 2 * f2 + f) * d0 + (-2 * f3 + 3 * f2) * g1 + (f3 - f2) * d1;
        if (abs(gf) < 1e-12)
        {
            break;
        }
        const df = (6 * f2 - 6 * f) * g0 + (3 * f2 - 4 * f + 1) * d0 + (-6 * f2 + 6 * f) * g1 + (3 * f2 - 2 * f) * d1;
        if (abs(df) < 1e-15)
        {
            break;
        }
        f = clamp(f - gf / df, 0, 1);
    }
    return f;
}

/**
 * Every point where one sampled edge meets a station plane, as crossing records. The caller passes only
 * edges whose boxes the plane crosses (undrapeCandidateEdges).
 *
 * @param hint {number} : the span the edge was crossed in at the previous station, or -1. Consecutive
 *      stations cross an edge running along the reference in the same span or the next: tried first.
 *
 * An edge running through the plane (end samples on opposite sides) is bisected on its samples and solved
 * by Newton on the Hermite span to 1e-12 m. Otherwise every sample is scanned: sign changes are solved the
 * same way, and samples lying IN the plane (|distance| < UNDRAPE_IN_PLANE: an edge running straight
 * across the part, such as a face end or the tail edge, whose station this is) are emitted as section
 * points themselves.
 */
export function undrapeEdgeCrossings(tb is map, e is number, fr is map, hint is number) returns array
{
    const t = fr.t;
    const t0 = t[0];
    const t1 = t[1];
    const t2 = t[2];
    const g0 = fr.a[0] * t0 + fr.a[1] * t1 + fr.a[2] * t2;
    const px = tb.px;
    const py = tb.py;
    const pz = tb.pz;
    const count = tb.count;
    const gFirst = px[0] * t0 + py[0] * t1 + pz[0] * t2 - g0;
    const gLast = px[count - 1] * t0 + py[count - 1] * t1 + pz[count - 1] * t2 - g0;

    if ((gFirst > 0) != (gLast > 0) && abs(gFirst) >= UNDRAPE_IN_PLANE && abs(gLast) >= UNDRAPE_IN_PLANE)
    {
        for (var j = max(hint, 0); hint >= 0 && j <= hint + 1 && j + 1 < count; j += 1)
        {
            const gj = px[j] * t0 + py[j] * t1 + pz[j] * t2 - g0;
            const gk = px[j + 1] * t0 + py[j + 1] * t1 + pz[j + 1] * t2 - g0;
            if ((gj > 0) != (gk > 0) && abs(gj) >= UNDRAPE_IN_PLANE && abs(gk) >= UNDRAPE_IN_PLANE)
            {
                return [undrapeCrossingAt(tb, e, j, undrapeSpanRoot(tb, j, gj, gk, t), fr, 0)];
            }
        }
        var low = 0;
        var high = count - 1;
        var gLow = gFirst;
        var gHigh = gLast;
        while (high - low > 1)
        {
            const half = floor((low + high) / 2);
            const gMid = px[half] * t0 + py[half] * t1 + pz[half] * t2 - g0;
            if (abs(gMid) < UNDRAPE_IN_PLANE)
            {
                return [undrapeCrossingAt(tb, e, half, 0, fr, 0)];
            }
            if ((gMid > 0) == (gLow > 0))
            {
                low = half;
                gLow = gMid;
            }
            else
            {
                high = half;
                gHigh = gMid;
            }
        }
        return [undrapeCrossingAt(tb, e, low, undrapeSpanRoot(tb, low, gLow, gHigh, t), fr, 0)];
    }

    var result = [];
    var gPrev = gFirst;
    if (abs(gFirst) < UNDRAPE_IN_PLANE)
    {
        result = append(result, undrapeCrossingAt(tb, e, 0, 0, fr, 1));
    }
    for (var i = 1; i < count; i += 1)
    {
        const gi = px[i] * t0 + py[i] * t1 + pz[i] * t2 - g0;
        if (abs(gi) < UNDRAPE_IN_PLANE)
        {
            result = append(result, undrapeCrossingAt(tb, e, i - 1, 1, fr, 1));
        }
        else if (abs(gPrev) >= UNDRAPE_IN_PLANE && (gPrev > 0) != (gi > 0))
        {
            result = append(result, undrapeCrossingAt(tb, e, i - 1, undrapeSpanRoot(tb, i - 1, gPrev, gi, t), fr, 0));
        }
        gPrev = gi;
    }
    return result;
}

/**
 * Length and turn of the section piece from crossing a to crossing b: the circular arc with a's leaving
 * and b's arriving tangents, chord * (phi / 2) / sin(phi / 2), phi the signed turn (counter-clockwise in
 * (w, h)). Exact for a fillet or a flat. @returns {array} : [length, turn, chord]
 */
export function undrapePiece(a is array, b is array) returns array
{
    const dw = b[0] - a[0];
    const dh = b[1] - a[1];
    const chord = sqrt(dw * dw + dh * dh);
    const phi = atan2(a[4] * b[3] - a[5] * b[2], a[4] * b[2] + a[5] * b[3]) / radian;
    const half = 0.5 * abs(phi);
    const factor = (half < UNDRAPE_STRAIGHT) ? 1 : half / sin(half * radian);
    return [chord * factor, phi, chord];
}

/**
 * Where on the circular piece a -> b the section reaches width w0: [arc from a, turn from a].
 * The arc is a + R (sin(psi) ta + (1 - cos(psi)) na), na = ta turned a quarter counter-clockwise,
 * R = chord / (2 sin(phi / 2)); psi by Newton from the chord's proportion.
 */
export function undrapePieceToWidth(a is array, b is array, w0 is number) returns array
{
    const piece = undrapePiece(a, b);
    const span = b[0] - a[0];
    const share = (abs(span) < 1e-15) ? 0 : (w0 - a[0]) / span;
    if (abs(piece[1]) < UNDRAPE_STRAIGHT)
    {
        return [share * piece[0], 0];
    }
    const radius = piece[2] / (2 * sin(0.5 * piece[1] * radian));
    var psi = share * piece[1];
    for (var step = 0; step < 12; step += 1)
    {
        const s = sin(psi * radian);
        const c = cos(psi * radian);
        const pw = a[0] + radius * (s * a[4] - (1 - c) * a[5]);
        if (abs(pw - w0) < 1e-13)
        {
            break;
        }
        const dpw = radius * (c * a[4] - s * a[5]);
        if (abs(dpw) < 1e-15)
        {
            break;
        }
        const next = psi - (pw - w0) / dpw;
        psi = (piece[1] > 0) ? clamp(next, 0, piece[1]) : clamp(next, piece[1], 0);
    }
    return [abs(radius * psi), psi];
}

/**
 * The section of the sampled side at one station, unrolled onto the MID-surface.
 *
 * Crossings are ordered by width (overhangs are refused), coincident ones merged, and the pieces between
 * them measured as circular arcs. The mid-surface is the side offset inward by half the in-plane
 * thickness t / inPlane, so each piece's mid length is its side length + sideSign * (t' / 2) * turn.
 * A gap (slot, notch) needs no special case: the piece between its two rim crossings is the circular
 * bridge with the gap's end tangents (the surface continued smoothly across).
 *
 * @returns {map} : { "ok", "why", "kernel" : false, "pts" (merged crossings by w), "mid" (signed
 *      mid-surface arc from the centreline w = 0 at each), "nodeOf" (the node each input crossing became),
 *      "thickness", "sideSign" }
 */
export function undrapeSection(crossings is array, thickness is number, sideSign is number) returns map
{
    const count = size(crossings);
    if (count < 2)
    {
        return { "ok" : false, "why" : "fewer than two crossings" };
    }
    for (var c in crossings)
    {
        if (c[13] == 0)
        {
            return { "ok" : false, "why" : "a side normal lies along the reference" };
        }
        if (c[8] == 0 && c[17] == 0 && c[7] < UNDRAPE_MIN_CROSSING && c[6] < UNDRAPE_MIN_INPLANE)
        {
            return { "ok" : false, "why" : "a face is cut lengthwise" };
        }
        if (c[2] < UNDRAPE_OVERHANG || c[4] < UNDRAPE_OVERHANG)
        {
            return { "ok" : false, "why" : "overhang" };
        }
    }

    // insertion sort of the crossing indices by width (a dozen or two crossings)
    var order = makeArray(count);
    for (var i = 0; i < count; i += 1)
    {
        const w = crossings[i][0];
        var j = i;
        while (j > 0 && crossings[order[j - 1]][0] > w)
        {
            order[j] = order[j - 1];
            j -= 1;
        }
        order[j] = i;
    }

    // merge coincident crossings; nodeOf[i] = the node crossing i became
    var pts = makeArray(count);
    var nodeOf = makeArray(count);
    pts[0] = crossings[order[0]];
    nodeOf[order[0]] = 0;
    var n = 1;
    for (var k = 1; k < count; k += 1)
    {
        const c = crossings[order[k]];
        const last = pts[n - 1];
        const dw = c[0] - last[0];
        const dh = c[1] - last[1];
        if (dw * dw + dh * dh > UNDRAPE_SAME_POINT * UNDRAPE_SAME_POINT)
        {
            pts[n] = c;
            n += 1;
        }
        else if (c[8] == 1 && last[8] == 0)
        {
            var merged = last;
            merged[8] = 1;
            pts[n - 1] = merged;
        }
        nodeOf[order[k]] = n - 1;
    }
    if (n < 2)
    {
        return { "ok" : false, "why" : "fewer than two crossings" };
    }
    pts = resize(pts, n);

    var mid = makeArray(n, 0);
    var zeroIndex = -1;
    for (var i = 0; i + 1 < n; i += 1)
    {
        const piece = undrapePiece(pts[i], pts[i + 1]);
        mid[i + 1] = mid[i] + piece[0] + sideSign * thickness / (pts[i][6] + pts[i + 1][6]) * piece[1];
        if (pts[i][0] <= 0 && pts[i + 1][0] > 0)
        {
            zeroIndex = i;
        }
    }

    // Arc at the centreline. A plate that does not reach w = 0 is measured from its innermost crossing,
    // which keeps its own width (open decision 9.4).
    var zero = 0;
    if (zeroIndex >= 0)
    {
        const a = pts[zeroIndex];
        const b = pts[zeroIndex + 1];
        const part = undrapePieceToWidth(a, b, 0);
        zero = mid[zeroIndex] + part[0] + sideSign * thickness / (a[6] + b[6]) * part[1];
    }
    else if (pts[0][0] > 0)
    {
        zero = -pts[0][0];
    }
    else
    {
        zero = mid[n - 1] - pts[n - 1][0];
    }
    for (var i = 0; i < n; i += 1)
    {
        mid[i] = mid[i] - zero;
    }
    return { "ok" : true, "kernel" : false, "pts" : pts, "mid" : mid, "nodeOf" : nodeOf, "thickness" : thickness, "sideSign" : sideSign };
}

/**
 * Signed mid-surface arc from the centreline to the section point at (w0, h0) of the sampled side: the
 * undraped transverse coordinate of that point and of everything on its normal through the plate.
 * A node within UNDRAPE_NODE_TOL is read directly; otherwise the circular piece holding w0 (edge
 * sections) or the nearest chord of the sampled chain (kernel sections) is used.
 */
export function undrapeMidAt(section is map, w0 is number, h0 is number) returns number
{
    const pts = section.pts;
    const n = size(pts);
    if (section.kernel)
    {
        return undrapeChainMidAt(section, w0, h0);
    }
    // last node with w <= w0
    var low = 0;
    var high = n - 1;
    while (low < high)
    {
        const half = floor((low + high + 1) / 2);
        if (pts[half][0] <= w0)
        {
            low = half;
        }
        else
        {
            high = half - 1;
        }
    }
    const j = low;
    for (var k = max(j - 1, 0); k <= min(j + 1, n - 1); k += 1)
    {
        const dw = pts[k][0] - w0;
        const dh = pts[k][1] - h0;
        if (dw * dw + dh * dh < UNDRAPE_NODE_TOL * UNDRAPE_NODE_TOL)
        {
            return section.mid[k];
        }
    }
    if (w0 <= pts[0][0])
    {
        return section.mid[0] - (pts[0][0] - w0);
    }
    if (w0 >= pts[n - 1][0])
    {
        return section.mid[n - 1] + (w0 - pts[n - 1][0]);
    }
    const part = undrapePieceToWidth(pts[j], pts[j + 1], w0);
    return section.mid[j] + part[0] + section.sideSign * section.thickness / (pts[j][6] + pts[j + 1][6]) * part[1];
}

/**
 * Nearest point of a sampled chain to (w0, h0): [arc there, 1 when it is an end of the chain, else 0].
 */
export function undrapeChainNearest(chain is map, w0 is number, h0 is number) returns array
{
    const ws = chain.ws;
    const hs = chain.hs;
    const arcs = chain.arcs;
    const n = size(ws);
    // Chains increase strictly in w: start at the segment holding w0 and walk out both ways while the
    // width gap alone can still beat the best distance found.
    var low = 0;
    var high = n - 2;
    while (low < high)
    {
        const half = floor((low + high + 1) / 2);
        if (ws[half] <= w0)
        {
            low = half;
        }
        else
        {
            high = half - 1;
        }
    }
    var best = 1e30;
    var result = [arcs[0], 1];
    for (var dir = -1; dir <= 1; dir += 2)
    {
        var i = (dir < 0) ? low : low + 1;
        while (i >= 0 && i + 1 < n)
        {
            const dw = ws[i + 1] - ws[i];
            const dh = hs[i + 1] - hs[i];
            const gap = (dir < 0) ? w0 - ws[i + 1] : ws[i] - w0;
            if (gap > 0 && gap * gap > best)
            {
                break;
            }
            const l2 = dw * dw + dh * dh;
            const f = (l2 < 1e-30) ? 0 : clamp(((w0 - ws[i]) * dw + (h0 - hs[i]) * dh) / l2, 0, 1);
            const ew = ws[i] + f * dw - w0;
            const eh = hs[i] + f * dh - h0;
            const d2 = ew * ew + eh * eh;
            if (d2 < best)
            {
                best = d2;
                const atEnd = (i == 0 && f == 0) || (i == n - 2 && f == 1);
                result = [arcs[i] + f * (arcs[i + 1] - arcs[i]), atEnd ? 1 : 0];
            }
            i += dir;
        }
    }
    return result;
}

/**
 * Kernel-section lookup: the mid arc at (w0, h0) on the sampled side's chain. The other side's arc is read
 * at its nearest point, or at its matching end when (w0, h0) is an end of the chain (the rim: the rim
 * wall's section, whatever its angle to the plate). Each side's arc runs from its own centreline point.
 */
export function undrapeChainMidAt(section is map, w0 is number, h0 is number) returns number
{
    const a = undrapeChainNearest(section.chainA, w0, h0);
    var arcB = 0;
    const nA = size(section.chainA.ws);
    const nB = size(section.chainB.ws);
    if (a[1] == 1)
    {
        const first = abs(a[0] - section.chainA.arcs[0]) < abs(a[0] - section.chainA.arcs[nA - 1]);
        arcB = first ? section.chainB.arcs[0] : section.chainB.arcs[nB - 1];
    }
    else
    {
        arcB = undrapeChainNearest(section.chainB, w0, h0)[0];
    }
    return 0.5 * ((a[0] - section.zeroA) + (arcB - section.zeroB));
}

// ============================================================================
// Refused stations: kernel sections
// ============================================================================

/**
 * One side's kernel section at a station: the plane sheet `planeId` intersected with the side's faces
 * whose boxes straddle it, read back as a chain ordered by width.
 * @returns {map} : { "ws", "hs", "arcs" } (side arc along the chain, from its first point)
 */
export function undrapeKernelChain(context is Context, ixId is Id, planeId is Id, faces is array, boxes is array, fr is map) returns map
{
    var hits = [];
    for (var k = 0; k < size(faces); k += 1)
    {
        const r = undrapeBoxRange(boxes[k], fr);
        if (r[0] <= 0 && r[1] >= 0)
        {
            hits = append(hits, faces[k]);
        }
    }
    if (size(hits) == 0)
    {
        return { "ws" : [], "hs" : [], "arcs" : [] };
    }
    opIntersectFaces(context, ixId, { "tools" : qCreatedBy(planeId, EntityType.FACE), "targets" : qUnion(hits) });

    const a = fr.a;
    var pieces = [];
    for (var edge in evaluateQuery(context, qCreatedBy(ixId, EntityType.EDGE)))
    {
        const lines = evEdgeTangentLines(context, { "edge" : edge, "parameters" : range(0, 1, UNDRAPE_KERNEL_SAMPLES),
                    "arcLengthParameterization" : false });
        const m = size(lines);
        var ws = makeArray(m);
        var hs = makeArray(m);
        var tws = makeArray(m);
        var ths = makeArray(m);
        for (var j = 0; j < m; j += 1)
        {
            const o = lines[j].origin;
            const dx = o[0].value - a[0];
            const dy = o[1].value - a[1];
            const dz = o[2].value - a[2];
            const u = lines[j].direction;
            ws[j] = dx * fr.w[0] + dy * fr.w[1] + dz * fr.w[2];
            hs[j] = dx * fr.h[0] + dy * fr.h[1] + dz * fr.h[2];
            tws[j] = u[0] * fr.w[0] + u[1] * fr.w[1] + u[2] * fr.w[2];
            ths[j] = u[0] * fr.h[0] + u[1] * fr.h[1] + u[2] * fr.h[2];
        }
        if (ws[m - 1] < ws[0])
        {
            ws = reverse(ws);
            hs = reverse(hs);
            tws = reverse(tws);
            ths = reverse(ths);
            for (var j = 0; j < m; j += 1)
            {
                tws[j] = -tws[j];
                ths[j] = -ths[j];
            }
        }
        // arc along the piece at each sample: circle-corrected chords, scaled to the kernel's exact length
        var along = makeArray(m, 0);
        for (var j = 1; j < m; j += 1)
        {
            const dw = ws[j] - ws[j - 1];
            const dh = hs[j] - hs[j - 1];
            const phi = abs(atan2(tws[j - 1] * ths[j] - ths[j - 1] * tws[j], tws[j - 1] * tws[j] + ths[j - 1] * ths[j]) / radian);
            along[j] = along[j - 1] + sqrt(dw * dw + dh * dh) * ((phi < UNDRAPE_STRAIGHT) ? 1 : (0.5 * phi) / sin(0.5 * phi * radian));
        }
        if (along[m - 1] > 0)
        {
            const scale = evLength(context, { "entities" : edge }).value / along[m - 1];
            for (var j = 1; j < m; j += 1)
            {
                along[j] = along[j] * scale;
            }
        }
        pieces = append(pieces, [ws, hs, along]);
    }
    pieces = sort(pieces, function(p, q) { return p[0][0] - q[0][0]; });

    // A plane along a face end cuts both faces there along their common boundary: the same piece comes
    // back twice. Samples at widths the chain already covers are skipped.
    var ws = [];
    var hs = [];
    var arcs = [];
    var arc = 0;
    for (var piece in pieces)
    {
        var lastJ = -2;
        for (var j = 0; j < size(piece[0]); j += 1)
        {
            if (size(ws) > 0)
            {
                if (piece[0][j] <= ws[size(ws) - 1] + 1e-7)
                {
                    continue;
                }
                if (lastJ == j - 1)
                {
                    arc += piece[2][j] - piece[2][j - 1];
                }
                else
                {
                    // a gap or the start of the next piece: bridged by the chord
                    const dw = piece[0][j] - ws[size(ws) - 1];
                    const dh = piece[1][j] - hs[size(hs) - 1];
                    arc += sqrt(dw * dw + dh * dh);
                }
            }
            ws = append(ws, piece[0][j]);
            hs = append(hs, piece[1][j]);
            arcs = append(arcs, arc);
            lastJ = j;
        }
    }
    return { "ws" : ws, "hs" : hs, "arcs" : arcs };
}

/** Arc of a chain at its crossing of w = 0 (linear between samples); the innermost point keeps its w otherwise. */
export function undrapeChainZero(chain is map) returns number
{
    const ws = chain.ws;
    const n = size(ws);
    for (var i = 0; i + 1 < n; i += 1)
    {
        if (ws[i] <= 0 && ws[i + 1] > 0)
        {
            return chain.arcs[i] + (chain.arcs[i + 1] - chain.arcs[i]) * (0 - ws[i]) / (ws[i + 1] - ws[i]);
        }
    }
    return (ws[0] > 0) ? -ws[0] : chain.arcs[n - 1] - ws[n - 1];
}

/**
 * Kernel section of BOTH sides at a station, unrolled onto the mid-surface: each side's arc from its own
 * centreline point, averaged at corresponding points (the other side's nearest point). Exact to ~1e-5 mm
 * where the sides are offsets of the mid-surface (their arcs differ by +-(t'/2) * turn).
 * All geometry is created under `kid` and deleted by the caller.
 */
export function undrapeKernelSection(context is Context, kid is Id, kernel is map, fr is map, reach is number) returns map
{
    const size2 = 2 * reach + 0.02;
    opPlane(context, kid + "plane", { "plane" : plane(vector(fr.a[0], fr.a[1], fr.a[2]) * meter, vector(fr.t[0], fr.t[1], fr.t[2]),
                        vector(fr.w[0], fr.w[1], fr.w[2])), "width" : size2 * meter, "height" : size2 * meter });
    const chainA = undrapeKernelChain(context, kid + "ixA", kid + "plane", kernel.facesA, kernel.boxesA, fr);
    const chainB = undrapeKernelChain(context, kid + "ixB", kid + "plane", kernel.facesB, kernel.boxesB, fr);
    if (size(chainA.ws) < 2 || size(chainB.ws) < 2)
    {
        return { "ok" : false, "why" : "empty kernel section" };
    }
    return { "ok" : true, "kernel" : true, "pts" : [], "chainA" : chainA, "chainB" : chainB,
            "zeroA" : undrapeChainZero(chainA), "zeroB" : undrapeChainZero(chainB) };
}

// ----------------------------------------------------------------------------
// THE U-TURN RULE (open decision 9.1, research_undrape_map.md) -- change it HERE only.
// ----------------------------------------------------------------------------

/**
 * How a REFUSED station is measured: one where the edge sections are not trusted because the station
 * plane cuts a face lengthwise (the pressed step running across the stations at the tail and tip
 * U-turns), crosses an overhang, or meets an edge with no section tangent.
 *
 * CURRENT RULE: literal section-by-section. The station's own mid-surface section, from kernel sections
 * of both sides (undrapeKernelSection), exactly as everywhere else -- only measured by the kernel. The
 * user has NOT yet decided whether to blend to another rule near the ends (sections normal to the step's
 * plan direction, or a least-distortion fill); a blend would replace the body of this function.
 *
 * A plane exactly through a vertex can fail in the kernel ("Failed to completely disambiguate created
 * topology"); the station is retried shifted by UNDRAPE_KERNEL_RETRIES. The frame actually used is
 * returned so the caller maps with it.
 *
 * @returns {map} : { "ok", "why", "section", "frame", "attempts" }
 */
export function undrapeRefusedSection(context is Context, id is Id, kernel is map, c is map, fr is map, reach is number, tag is string) returns map
{
    var why = "";
    for (var r = 0; r < size(UNDRAPE_KERNEL_RETRIES); r += 1)
    {
        const shifted = (r == 0) ? fr : undrapeFrame(c, fr.arc + UNDRAPE_KERNEL_RETRIES[r]);
        var section = undefined;
        try silent
        {
            section = undrapeKernelSection(context, id + (tag ~ "_" ~ r), kernel, shifted, reach);
        }
        if (section != undefined && section.ok)
        {
            return { "ok" : true, "section" : section, "frame" : shifted, "attempts" : r + 1 };
        }
        why = (section == undefined) ? "kernel refused the section" : section.why;
    }
    return { "ok" : false, "why" : why, "attempts" : size(UNDRAPE_KERNEL_RETRIES) };
}

/** Faces and padded boxes of both sides for the kernel fallback (read once, on the first refusal). */
export function undrapeKernelFaces(context is Context, sideA is Query, sideB is Query) returns map
{
    var result = {};
    for (var pair in [["A", sideA], ["B", sideB]])
    {
        const faces = evaluateQuery(context, pair[1]);
        var boxes = makeArray(size(faces));
        for (var k = 0; k < size(faces); k += 1)
        {
            const bb = evBox3d(context, { "topology" : faces[k], "tight" : false });
            const lo = bb.minCorner;
            const hi = bb.maxCorner;
            boxes[k] = { "cx" : 0.5 * (lo[0] + hi[0]).value, "cy" : 0.5 * (lo[1] + hi[1]).value, "cz" : 0.5 * (lo[2] + hi[2]).value,
                    "hx" : 0.5 * (hi[0] - lo[0]).value + 1e-4, "hy" : 0.5 * (hi[1] - lo[1]).value + 1e-4,
                    "hz" : 0.5 * (hi[2] - lo[2]).value + 1e-4 };
        }
        result["faces" ~ pair[0]] = faces;
        result["boxes" ~ pair[0]] = boxes;
    }
    return result;
}

// ============================================================================
// Outline: rim loops and sample requests
// ============================================================================

/**
 * The side's rim edges chained into closed loops by their end points.
 * @returns {map} : { "loops" : array of arrays of [tableIndex, reversed], "vertexOf" : map table ->
 *      [startVertex, endVertex], "vertices" : array of [x, y, z], "open" : number of chains that did not close }
 */
export function undrapeRimLoops(tables is array, rim is array) returns map
{
    var vertices = [];
    var incident = [];
    var vertexOf = {};
    for (var r in rim)
    {
        const tb = tables[r];
        const last = tb.count - 1;
        var ends = [];
        for (var p in [[tb.px[0], tb.py[0], tb.pz[0]], [tb.px[last], tb.py[last], tb.pz[last]]])
        {
            var found = -1;
            for (var v = 0; v < size(vertices); v += 1)
            {
                const dx = vertices[v][0] - p[0];
                const dy = vertices[v][1] - p[1];
                const dz = vertices[v][2] - p[2];
                if (dx * dx + dy * dy + dz * dz < UNDRAPE_VERTEX_TOL * UNDRAPE_VERTEX_TOL)
                {
                    found = v;
                    break;
                }
            }
            if (found < 0)
            {
                found = size(vertices);
                vertices = append(vertices, p);
                incident = append(incident, []);
            }
            ends = append(ends, found);
            if (size(ends) == 1 || ends[1] != ends[0])
            {
                incident[found] = append(incident[found], r);
            }
        }
        vertexOf[r] = ends;
    }

    var used = {};
    var loops = [];
    var open = 0;
    for (var r in rim)
    {
        if (used[r] == true)
        {
            continue;
        }
        used[r] = true;
        var loop = [[r, false]];
        const start = vertexOf[r][0];
        var current = vertexOf[r][1];
        while (current != start)
        {
            var next = -1;
            for (var q in incident[current])
            {
                if (used[q] != true)
                {
                    next = q;
                    break;
                }
            }
            if (next < 0)
            {
                open += 1;
                break;
            }
            used[next] = true;
            const reversed = vertexOf[next][1] == current;
            loop = append(loop, [next, reversed]);
            current = reversed ? vertexOf[next][0] : vertexOf[next][1];
        }
        loops = append(loops, loop);
    }
    return { "loops" : loops, "vertexOf" : vertexOf, "vertices" : vertices, "open" : open };
}

// ============================================================================
// Undrape
// ============================================================================

/**
 * Undrape the outline of a constant-thickness plate onto the target.
 * @param chart : from unwrapChart(context, W, alignPoint, d).
 * @param side0, side1 {Query} : the plate's two side face sets (side0 outward normals point away from side1).
 * @param thickness {ValueWithUnits}
 * @param spacing {ValueWithUnits} : outline sample spacing (default the caller passes 6 mm).
 * @returns {map} : {
 *   "edges" : array, one per outline (rim) edge of the chosen side, each {
 *        "loop" : number (which closed loop), "index" : position in that loop (loops ordered, edges in order around the loop),
 *        "points" : array of [x, y] plain metres in the flat chart frame -- same convention as unwrapFast: x = length along
 *                   the target minus the alignment's, y = chart.alignV - (signed mid-surface arc from the centreline),
 *        "startTangent" : [tx, ty], "endTangent" : [tx, ty] unit, the way the points run,
 *        "arcs" : the chart arc (metres) of each point's station (extra, for checks) },
 *     consecutive edges of a loop share their end point EXACTLY (each shared vertex is computed once).
 *     Loop 0 is the outer loop (largest area) and runs counter-clockwise in (x, y); the others clockwise.
 *   "report" : { "stations", "fallbacks", "stretchMin", "stretchMax", "stretchWhere" ([x, y] metres), "shearMax" (radians),
 *                "rim3d", "rimFlat" (ValueWithUnits) },
 *   "lines" : array of strings for a debug print }
 */
export function undrapeOutline(context is Context, id is Id, chart is map, side0 is Query, side1 is Query,
    thickness is ValueWithUnits, spacing is ValueWithUnits) returns map
{
    const c = chart.packed;
    const tk = thickness.value;
    const sp = max(spacing.value, 5e-4);

    // 1. Side: the one with fewer edges (either works); the other is only read by the kernel fallback.
    const count0 = size(evaluateQuery(context, qAdjacent(side0, AdjacencyType.EDGE, EntityType.EDGE)));
    const count1 = size(evaluateQuery(context, qAdjacent(side1, AdjacencyType.EDGE, EntityType.EDGE)));
    const useFirst = count0 <= count1;
    const sideA = useFirst ? side0 : side1;
    const sideB = useFirst ? side1 : side0;

    // 2-3. Edge sampling and tables.
    const sampled = undrapeSampleSide(context, sideA, c);
    const tables = sampled.tables;
    const sideSign = undrapeSideSign(c, tables);

    // 4. Rim loops and sample requests.
    var rim = [];
    for (var e = 0; e < size(tables); e += 1)
    {
        if (tables[e].onRim)
        {
            rim = append(rim, e);
        }
    }
    if (size(rim) == 0)
    {
        throw regenError("Undrape: the plate side has no rim edges.");
    }
    const loopData = undrapeRimLoops(tables, rim);
    const plan = undrapeRequests(c, tables, rim, loopData, sp);
    var requests = plan.requests;

    // 5. Stations: request arcs sorted and merged.
    const nReq = size(requests);
    var order = sort(range(0, nReq - 1), function(i, j) { return requests[i].arc - requests[j].arc; });
    var stationOf = makeArray(nReq);
    var arcs = [];
    for (var k in order)
    {
        const a = requests[k].arc;
        if (size(arcs) == 0 || a - arcs[size(arcs) - 1] > UNDRAPE_STATION_MERGE)
        {
            arcs = append(arcs, a);
        }
        stationOf[k] = size(arcs) - 1;
    }
    const nSt = size(arcs);
    var frames = makeArray(nSt);
    for (var s = 0; s < nSt; s += 1)
    {
        frames[s] = undrapeFrame(c, arcs[s]);
    }
    var atStation = makeArray(nSt, []);
    for (var k in order)
    {
        atStation[stationOf[k]] = append(atStation[stationOf[k]], k);
    }

    // 6. Candidates, crossings, sections.
    const candidates = undrapeCandidateEdges(tables, frames);
    var kernel = undefined;
    var fallbacks = 0;
    var fallbackArcs = [];
    var failed = [];
    var lines = [];
    var sections = makeArray(nSt);
    var crossingsAt = makeArray(nSt);
    var candidateTotal = 0;
    var results = makeArray(nReq);
    var lastSpan = makeArray(size(tables), -1);
    for (var s = 0; s < nSt; s += 1)
    {
        var fr = frames[s];
        var crossings = [];
        candidateTotal += size(candidates[s]);
        for (var e in candidates[s])
        {
            const found = undrapeEdgeCrossings(tables[e], e, fr, lastSpan[e]);
            if (size(found) == 1)
            {
                lastSpan[e] = found[0][18];
                crossings = append(crossings, found[0]);
            }
            else
            {
                crossings = concatenateArrays([crossings, found]);
            }
        }
        var section = undrapeSection(crossings, tk, sideSign);
        if (!section.ok)
        {
            if (kernel == undefined)
            {
                kernel = undrapeKernelFaces(context, sideA, sideB);
            }
            const refused = undrapeRefusedSection(context, id, kernel, c, fr, undrapeReach(tables, candidates[s], fr),
                "kernel" ~ s);
            fallbacks += 1;
            fallbackArcs = append(fallbackArcs, roundToPrecision(fr.arc * 1000, 2) ~ " (" ~ section.why ~ ")");
            if (!refused.ok)
            {
                failed = append(failed, fr.arc);
                lines = append(lines, "undrape: station at arc " ~ roundToPrecision(fr.arc * 1000, 3) ~ " mm could not be measured ("
                        ~ section.why ~ "; " ~ refused.why ~ ")");
                sections[s] = { "ok" : false };
                crossingsAt[s] = crossings;
                continue;
            }
            section = refused.section;
            if (refused.frame.arc != fr.arc)
            {
                fr = refused.frame;
                frames[s] = fr;
                crossings = [];
                for (var e in candidates[s])
                {
                    crossings = concatenateArrays([crossings, undrapeEdgeCrossings(tables[e], e, fr, -1)]);
                }
            }
        }
        sections[s] = section;
        crossingsAt[s] = crossings;

        // 8. Map the requests at this station.
        for (var k in atStation[s])
        {
            results[k] = undrapeResolve(requests[k], section, crossings, fr, chart, tk);
        }
    }
    if (kernel != undefined)
    {
        const temporary = qCreatedBy(id, EntityType.BODY);
        if (!isQueryEmpty(context, temporary))
        {
            opDeleteBodies(context, id + "deleteKernelSections", { "entities" : temporary });
        }
    }

    // Edges and loops.
    const assembled = undrapeAssemble(plan, results, loopData);

    // 9. Deformation report.
    const deform = undrapeDeformation(sections, crossingsAt, frames, chart, tk);
    var rim3d = 0;
    var rimFlat = 0;
    for (var er in plan.edgeRequests)
    {
        const pr = er.points;
        for (var i = 0; i + 1 < size(pr); i += 1)
        {
            const p = results[pr[i]];
            const q = results[pr[i + 1]];
            if (p == undefined || q == undefined)
            {
                continue;
            }
            rim3d += sqrt((q[2] - p[2]) * (q[2] - p[2]) + (q[3] - p[3]) * (q[3] - p[3]) + (q[4] - p[4]) * (q[4] - p[4]));
            rimFlat += sqrt((q[0] - p[0]) * (q[0] - p[0]) + (q[1] - p[1]) * (q[1] - p[1]));
        }
    }

    lines = concatenateArrays([[
                    "undrape: side " ~ (useFirst ? 0 : 1) ~ " (" ~ size(tables) ~ " edges, " ~ size(rim) ~ " rim edges, "
                        ~ sampled.faceCount ~ " faces), sideSign " ~ sideSign ~ ", t " ~ roundToPrecision(tk * 1000, 4) ~ " mm",
                    "undrape: " ~ size(assembled.edges) ~ " outline edges in " ~ assembled.loopCount ~ " loop(s)"
                        ~ (loopData.open > 0 ? " (" ~ loopData.open ~ " did not close)" : ""),
                    "undrape: " ~ nSt ~ " stations (" ~ nReq ~ " samples), " ~ roundToPrecision(candidateTotal / max(nSt, 1), 1)
                        ~ " candidate edges per station, " ~ fallbacks ~ " kernel fallbacks"
                        ~ (size(fallbackArcs) > 0 ? " at arc (mm) " ~ undrapeJoin(fallbackArcs) : ""),
                    "undrape: stretch along rim and bend lines " ~ roundToPrecision(deform.stretchMin * 100, 3) ~ " % .. "
                        ~ roundToPrecision(deform.stretchMax * 100, 3) ~ " % (max at x " ~ roundToPrecision(deform.where[0] * 1000, 1)
                        ~ ", y " ~ roundToPrecision(deform.where[1] * 1000, 1) ~ " mm), shear up to "
                        ~ roundToPrecision(deform.shearMax * 180 / PI, 2) ~ " deg",
                    "undrape: rim 3D " ~ roundToPrecision(rim3d * 1000, 3) ~ " mm, flat " ~ roundToPrecision(rimFlat * 1000, 3) ~ " mm"
                ], lines]);

    return {
        "edges" : assembled.edges,
        "report" : {
            "stations" : nSt,
            "fallbacks" : fallbacks,
            "failed" : size(failed),
            "stretchMin" : deform.stretchMin,
            "stretchMax" : deform.stretchMax,
            "stretchWhere" : deform.where,
            "shearMax" : deform.shearMax,
            "rim3d" : rim3d * meter,
            "rimFlat" : rimFlat * meter
        },
        "lines" : lines
    };
}

/** Strings joined with commas. */
export function undrapeJoin(items is array) returns string
{
    var text = "";
    for (var i = 0; i < size(items); i += 1)
    {
        text = text ~ (i > 0 ? ", " : "") ~ items[i];
    }
    return text;
}

/**
 * +1 when the sampled side's outward normal points along the chart's surface normal h, else -1: read at
 * the edge sample whose normal is most nearly along h.
 */
export function undrapeSideSign(c is map, tables is array) returns number
{
    var best = 0;
    for (var e = 0; e < min(size(tables), 60); e += 1)
    {
        const tb = tables[e];
        const a0 = undrapeSeedArc(c, tb.px[0]);
        const f = chartFoot(c, tb.px[0], tb.py[0], tb.pz[0], a0, undrapeSpanOf(c, a0));
        const hx = c.ny * f[7] - c.nz * f[6];
        const hy = c.nz * f[5] - c.nx * f[7];
        const hz = c.nx * f[6] - c.ny * f[5];
        const d = tb.nx[0] * hx + tb.ny[0] * hy + tb.nz[0] * hz;
        if (abs(d) > abs(best))
        {
            best = d;
        }
    }
    return (best >= 0) ? 1 : -1;
}

/** Half-size of a kernel section plane that holds every candidate edge of the station. */
export function undrapeReach(tables is array, candidates is array, fr is map) returns number
{
    var reach = 0.05;
    for (var e in candidates)
    {
        const tb = tables[e];
        const dx = tb.cx - fr.a[0];
        const dy = tb.cy - fr.a[1];
        const dz = tb.cz - fr.a[2];
        reach = max(reach, sqrt(dx * dx + dy * dy + dz * dz) + sqrt(tb.hx * tb.hx + tb.hy * tb.hy + tb.hz * tb.hz));
    }
    return reach;
}

/**
 * The outline samples as requests, one station each.
 *
 * Every loop vertex is one request (computed once, shared by both its edges). A rim edge running along
 * the reference (|t . u| >= UNDRAPE_LENGTHWISE everywhere, arcs monotone) is sampled at the SHARED station
 * grid arc = k * spacing: its crossing with that station ("edge" requests; both long sides of a part share
 * the stations). Any other rim edge (ends, notches, holes) is sampled every `spacing` along its length and
 * each sample takes its own station ("point" requests). Each edge end also gets three requests at one, two
 * and three UNDRAPE_TANGENT_STEP inside it, for its end tangent (undrapeEndTangent).
 *
 * @returns {map} : { "requests" : array of { "arc", "kind" ("edge" | "point"), "edge", "p" ([x, y, z, nx, ny, nz]) },
 *      "edgeRequests" : map table -> { "points" : request indices along the edge's own direction, vertices
 *      included, "startTangent", "endTangent" : [vertex, 1, 2, 3 steps] request indices } }
 */
export function undrapeRequests(c is map, tables is array, rim is array, loopData is map, sp is number) returns map
{
    var requests = [];
    var vertexRequest = makeArray(size(loopData.vertices));
    var seed = undefined;
    for (var v = 0; v < size(loopData.vertices); v += 1)
    {
        const p = loopData.vertices[v];
        // the vertex's normal is read from the first rim edge ending there (below); position is exact
        vertexRequest[v] = size(requests);
        requests = append(requests, { "arc" : undrapeFootArc(c, p, undefined), "kind" : "point", "p" : [p[0], p[1], p[2], 0, 0, 1] });
    }

    var edgeRequests = {};
    for (var r in rim)
    {
        const tb = tables[r];
        const last = tb.count - 1;
        const ends = loopData.vertexOf[r];
        // vertex normals from this edge
        for (var end = 0; end < 2; end += 1)
        {
            const i = (end == 0) ? 0 : last;
            var q = requests[vertexRequest[ends[end]]];
            if (q.p[5] == 1 && q.p[3] == 0 && q.p[4] == 0)
            {
                q.p = [q.p[0], q.p[1], q.p[2], tb.nx[i], tb.ny[i], tb.nz[i]];
                requests[vertexRequest[ends[end]]] = q;
            }
        }

        // feet of the samples: lengthwise or not
        var sampleArcs = makeArray(tb.count);
        var lengthwise = true;
        var hint = -1;
        for (var i = 0; i < tb.count; i += 1)
        {
            const a0 = (hint < 0) ? undrapeSeedArc(c, tb.px[i]) : sampleArcs[i - 1];
            const f = chartFoot(c, tb.px[i], tb.py[i], tb.pz[i], a0, (hint < 0) ? undrapeSpanOf(c, a0) : hint);
            sampleArcs[i] = f[0];
            hint = f[1];
            if (abs(tb.ux[i] * f[5] + tb.uy[i] * f[6] + tb.uz[i] * f[7]) < UNDRAPE_LENGTHWISE)
            {
                lengthwise = false;
            }
            if (i > 0 && lengthwise && (sampleArcs[i] - sampleArcs[i - 1]) * (sampleArcs[1] - sampleArcs[0]) <= 0)
            {
                lengthwise = false;
            }
        }
        if (ends[0] == ends[1])
        {
            lengthwise = false;
        }

        var inner = [];
        var tanStart = [];
        var tanEnd = [];
        if (lengthwise)
        {
            const s0 = requests[vertexRequest[ends[0]]].arc;
            const s1 = requests[vertexRequest[ends[1]]].arc;
            const dir = (s1 > s0) ? 1 : -1;
            const lo = min(s0, s1);
            const hi = max(s0, s1);
            const margin = 0.25 * sp;
            var gridArcs = [];
            for (var k = ceil((lo + margin) / sp); k * sp <= hi - margin; k += 1)
            {
                gridArcs = append(gridArcs, k * sp);
            }
            if (size(gridArcs) == 0)
            {
                gridArcs = [0.5 * (lo + hi)];
            }
            if (dir < 0)
            {
                gridArcs = reverse(gridArcs);
            }
            for (var a in gridArcs)
            {
                inner = append(inner, size(requests));
                requests = append(requests, { "arc" : a, "kind" : "edge", "edge" : r });
            }
            const step = min(UNDRAPE_TANGENT_STEP, (hi - lo) / 8);
            for (var k = 1; k <= 3; k += 1)
            {
                tanStart = append(tanStart, size(requests));
                requests = append(requests, { "arc" : s0 + dir * k * step, "kind" : "edge", "edge" : r });
                tanEnd = append(tanEnd, size(requests));
                requests = append(requests, { "arc" : s1 - dir * k * step, "kind" : "edge", "edge" : r });
            }
        }
        else
        {
            const length = tb.length;
            const n = max([2, ceil(length / sp), ceil(tb.turn / 0.25)]);
            seed = sampleArcs[0];
            for (var k = 1; k < n; k += 1)
            {
                const p = undrapeEdgePoint(tb, length * k / n);
                const a = undrapeFootArc(c, p, seed);
                seed = a;
                inner = append(inner, size(requests));
                requests = append(requests, { "arc" : a, "kind" : "point", "p" : p });
            }
            const step = min(UNDRAPE_TANGENT_STEP, length / 8);
            for (var k = 1; k <= 3; k += 1)
            {
                const p0 = undrapeEdgePoint(tb, k * step);
                tanStart = append(tanStart, size(requests));
                requests = append(requests, { "arc" : undrapeFootArc(c, p0, sampleArcs[0]), "kind" : "point", "p" : p0 });
                const p1 = undrapeEdgePoint(tb, length - k * step);
                tanEnd = append(tanEnd, size(requests));
                requests = append(requests, { "arc" : undrapeFootArc(c, p1, sampleArcs[last]), "kind" : "point", "p" : p1 });
            }
        }
        edgeRequests[r] = {
            "points" : concatenateArrays([[vertexRequest[ends[0]]], inner, [vertexRequest[ends[1]]]]),
            "startTangent" : concatenateArrays([[vertexRequest[ends[0]]], tanStart]),
            "endTangent" : concatenateArrays([[vertexRequest[ends[1]]], tanEnd]),
            "lengthwise" : lengthwise
        };
    }
    var list = [];
    for (var r in rim)
    {
        list = append(list, edgeRequests[r]);
    }
    return { "requests" : requests, "edgeRequests" : list, "byTable" : edgeRequests };
}

/**
 * Map one request at its station: [x, y, mx, my, mz, arc] with (x, y) the flat point (plain metres) and
 * (mx, my, mz) the 3D mid-surface point it came from; undefined when the edge does not reach the station.
 */
export function undrapeResolve(request is map, section is map, crossings is array, fr is map, chart is map, tk is number)
{
    var p = undefined;
    if (request.kind == "edge")
    {
        for (var i = 0; i < size(crossings); i += 1)
        {
            const cr = crossings[i];
            if (cr[9] == request.edge)
            {
                if (!section.kernel)
                {
                    return [fr.x - chart.alignX, chart.alignV - section.mid[section.nodeOf[i]],
                            cr[14] - 0.5 * tk * cr[10], cr[15] - 0.5 * tk * cr[11], cr[16] - 0.5 * tk * cr[12], fr.arc];
                }
                p = [cr[14], cr[15], cr[16], cr[10], cr[11], cr[12]];
                break;
            }
        }
        if (p == undefined)
        {
            return undefined;
        }
    }
    else
    {
        p = request.p;
    }
    const dx = p[0] - fr.a[0];
    const dy = p[1] - fr.a[1];
    const dz = p[2] - fr.a[2];
    const w = dx * fr.w[0] + dy * fr.w[1] + dz * fr.w[2];
    const h = dx * fr.h[0] + dy * fr.h[1] + dz * fr.h[2];
    const mid = undrapeMidAt(section, w, h);
    return [fr.x - chart.alignX, chart.alignV - mid,
            p[0] - 0.5 * tk * p[3], p[1] - 0.5 * tk * p[4], p[2] - 0.5 * tk * p[5], fr.arc];
}

/**
 * End tangent of a flat edge from the points one, two and three steps inside it (indices [vertex, 1, 2, 3]
 * steps): the derivative at the end of the quadratic through them, -2.5 P1 + 4 P2 - 1.5 P3 (per step).
 * The vertex itself is left out: a vertex station lies exactly on a face end, where a lengthwise kink of the
 * side makes the in-plane thickness ambiguous (up to ~0.002 mm on the test part), and a one-sided
 * difference through it would carry that into the tangent.
 */
export function undrapeEndTangent(results is array, indices is array, atEnd is boolean)
{
    const p1 = results[indices[1]];
    const p2 = results[indices[2]];
    const p3 = results[indices[3]];
    if (p1 == undefined || p2 == undefined || p3 == undefined)
    {
        return undefined;
    }
    var tx = -2.5 * p1[0] + 4 * p2[0] - 1.5 * p3[0];
    var ty = -2.5 * p1[1] + 4 * p2[1] - 1.5 * p3[1];
    // that derivative points into the edge: along the run at its start, against it at its end
    if (atEnd)
    {
        tx = -tx;
        ty = -ty;
    }
    const tn = sqrt(tx * tx + ty * ty);
    if (tn < 1e-15)
    {
        return undefined;
    }
    return [tx / tn, ty / tn];
}

/**
 * Flat edges from the resolved requests, loop by loop, oriented: the outer loop (largest area)
 * counter-clockwise, the others clockwise.
 */
export function undrapeAssemble(plan is map, results is array, loopData is map) returns map
{
    var loops = [];
    for (var loop in loopData.loops)
    {
        var edges = [];
        var area = 0;
        for (var entry in loop)
        {
            const er = plan.byTable[entry[0]];
            var points = [];
            var arcs = [];
            for (var i = 0; i < size(er.points); i += 1)
            {
                const q = results[er.points[i]];
                if (q == undefined)
                {
                    if (i == 0 || i == size(er.points) - 1)
                    {
                        throw regenError("Undrape: a section through an outline vertex could not be measured.");
                    }
                    continue;
                }
                points = append(points, [q[0], q[1]]);
                arcs = append(arcs, q[5]);
            }
            var startTangent = undrapeEndTangent(results, er.startTangent, false);
            var endTangent = undrapeEndTangent(results, er.endTangent, true);
            const n = size(points);
            if (startTangent == undefined)
            {
                startTangent = undrapeUnit(points[1][0] - points[0][0], points[1][1] - points[0][1]);
            }
            if (endTangent == undefined)
            {
                endTangent = undrapeUnit(points[n - 1][0] - points[n - 2][0], points[n - 1][1] - points[n - 2][1]);
            }
            if (entry[1])
            {
                points = reverse(points);
                arcs = reverse(arcs);
                const swap = startTangent;
                startTangent = [-endTangent[0], -endTangent[1]];
                endTangent = [-swap[0], -swap[1]];
            }
            for (var i = 0; i + 1 < n; i += 1)
            {
                area += points[i][0] * points[i + 1][1] - points[i + 1][0] * points[i][1];
            }
            edges = append(edges, { "points" : points, "arcs" : arcs, "startTangent" : startTangent, "endTangent" : endTangent });
        }
        loops = append(loops, { "edges" : edges, "area" : 0.5 * area });
    }
    loops = sort(loops, function(p, q) { return abs(q.area) - abs(p.area); });

    var result = [];
    for (var l = 0; l < size(loops); l += 1)
    {
        var edges = loops[l].edges;
        const flip = (l == 0) ? loops[l].area < 0 : loops[l].area > 0;
        if (flip)
        {
            edges = reverse(edges);
        }
        for (var k = 0; k < size(edges); k += 1)
        {
            var edge = edges[k];
            if (flip)
            {
                const swap = edge.startTangent;
                edge = { "points" : reverse(edge.points), "arcs" : reverse(edge.arcs),
                        "startTangent" : [-edge.endTangent[0], -edge.endTangent[1]], "endTangent" : [-swap[0], -swap[1]] };
            }
            edge.loop = l;
            edge.index = k;
            result = append(result, edge);
        }
    }
    return { "edges" : result, "loopCount" : size(loops) };
}

export function undrapeUnit(x is number, y is number) returns array
{
    const n = sqrt(x * x + y * y);
    return (n < 1e-15) ? [1, 0] : [x / n, y / n];
}

/**
 * Deformation of the map along the rim and every bend line (each edge crossing both of two consecutive
 * measured stations exactly once): the 3D mid-surface chord between the two crossings (side point moved
 * t/2 inward along its normal) against the flat chord, and the change of the angle each chord makes with
 * its section (3D: the section tangent; flat: the section direction -y) -- the shear of the s- and w-lines.
 */
export function undrapeDeformation(sections is array, crossingsAt is array, frames is array, chart is map, tk is number) returns map
{
    var stretchMin = 1e9;
    var stretchMax = -1e9;
    var where = [0, 0];
    var shearSin = 0;
    var previous = [];
    for (var s = 0; s < size(sections); s += 1)
    {
        const section = sections[s];
        if (!section.ok)
        {
            previous = [];
            continue;
        }
        const fr = frames[s];
        const crossings = crossingsAt[s];
        const n = size(crossings);
        var current = makeArray(n);
        var count = 0;
        for (var i = 0; i < n; i += 1)
        {
            const cr = crossings[i];
            // one edge's crossings are consecutive, in rising edge index: an edge met twice is not tracked
            if (cr[17] == 1 || (i > 0 && crossings[i - 1][9] == cr[9]) || (i + 1 < n && crossings[i + 1][9] == cr[9]))
            {
                continue;
            }
            const mid = section.kernel ? undrapeMidAt(section, cr[0], cr[1]) : section.mid[section.nodeOf[i]];
            current[count] = [cr[9], fr.x - chart.alignX, chart.alignV - mid,
                    cr[14] - 0.5 * tk * cr[10], cr[15] - 0.5 * tk * cr[11], cr[16] - 0.5 * tk * cr[12],
                    cr[4] * fr.w[0] + cr[5] * fr.h[0], cr[4] * fr.w[1] + cr[5] * fr.h[1], cr[4] * fr.w[2] + cr[5] * fr.h[2]];
            count += 1;
        }
        current = resize(current, count);
        // merge join on the edge index (both lists rise in it)
        var j = 0;
        for (var q in current)
        {
            while (j < size(previous) && previous[j][0] < q[0])
            {
                j += 1;
            }
            if (j >= size(previous) || previous[j][0] != q[0])
            {
                continue;
            }
            const p = previous[j];
            const dx = q[3] - p[3];
            const dy = q[4] - p[4];
            const dz = q[5] - p[5];
            const l3 = sqrt(dx * dx + dy * dy + dz * dz);
            const fx = q[1] - p[1];
            const fy = q[2] - p[2];
            const lf = sqrt(fx * fx + fy * fy);
            if (l3 < 1e-5)
            {
                continue;
            }
            const stretch = lf / l3 - 1;
            if (stretch > stretchMax)
            {
                stretchMax = stretch;
                where = [0.5 * (p[1] + q[1]), 0.5 * (p[2] + q[2])];
            }
            stretchMin = min(stretchMin, stretch);
            // angle change: |sin(a3 - af)| = |sin a3 cos af - cos a3 sin af|, both angles in [0, pi]
            const c3 = clamp((dx * p[6] + dy * p[7] + dz * p[8]) / l3, -1, 1);
            const cf = clamp(-fy / lf, -1, 1);
            shearSin = max(shearSin, abs(sqrt(1 - c3 * c3) * cf - c3 * sqrt(1 - cf * cf)));
        }
        previous = current;
    }
    if (stretchMax < stretchMin)
    {
        stretchMin = 0;
        stretchMax = 0;
    }
    return { "stretchMin" : stretchMin, "stretchMax" : stretchMax, "where" : where, "shearMax" : asin(shearSin) / radian };
}
