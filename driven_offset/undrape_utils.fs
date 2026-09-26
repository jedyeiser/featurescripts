FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");
// IMPORT: edge_offset_utils.fs (same document)
export import(path : "a2665e22c07b7a6929ce4e80", version : "ff3d2909e89ded4e32a5c49b");

/**
 * UNDRAPE MAP: the flat outline of a constant-thickness plate draped over the target.
 *
 * Design and measurements: research_undrape_map.md. In one paragraph: at every outline sample's own
 * station (the plane normal to the reference wire W through it), the plate's mid-surface section is
 * measured from the EDGES of one side of the plate. The station plane is intersected with every edge
 * of that side (each edge sampled once, adaptively: positions, edge tangents, side-face normals), the crossings
 * are ordered across the section, and each piece between consecutive crossings is taken as the circular
 * arc with the two end tangents (tangent = station normal x face normal). The side's arc becomes the
 * mid-surface arc with the turning correction sideSign * (t' / 2) * turn, t' = t / |in-plane part of
 * the normal|. The flat point is x = length along the target (W offset by d) and
 * y = chart.alignV - (signed mid arc from the centreline w = 0). The outline is sampled adaptively to
 * options.tolerance (undrapeOutline, UNDRAPE_TOLERANCE); the edge tables likewise (UNDRAPE_SEED_TURN).
 *
 * A section that is not single-valued across the width (a wall at or past vertical: the folded wings of
 * a pressed channel) is ordered by the side's face adjacency instead of by width and unrolled by arc
 * length whatever it turns through (undrapeSectionByFaces). Stations where the circle-per-piece model
 * is not trusted (an edge crossed obliquely on a face that the plane cuts lengthwise: the tail and tip
 * U-turns of a pressed step) are REFUSED and measured by undrapeRefusedSection (the U-turn rule, kept in
 * one place; see there).
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
 * Side edge tables are sampled ADAPTIVELY (research_undrape_map.md 15). Seeds come from the edge's own structure
 * (undrapeSeedSpans): a line one span, a B-spline one span per control-point interval, an arc or any other curve
 * one span per UNDRAPE_SEED_TURN of turn; never a span turning more than UNDRAPE_EDGE_TURN, never seeds closer
 * than UNDRAPE_SEED_MIN_GAP. Positions and tangents (evEdgeTangentLines, ~15 us per point) at the seeds and every
 * span's midpoint, then per pass at the midpoints of the spans whose midpoint missed the table's Hermite span by
 * more than 4 * UNDRAPE_TABLE_TOL * the outline tolerance (undrapePositionSplit: the refined table is then ~1/16 of
 * that off) or turned more than UNDRAPE_EDGE_TURN. Side normals (evFaceTangentPlanesAtEdge, ~0.19 ms per point)
 * on their own knots: the seeds and their midpoints, then the midpoints of knot spans whose midpoint missed the
 * linearly interpolated normal by more than 4 * UNDRAPE_NORMAL_TOL radians (undrapeNormalKnots). Normal errors
 * telescope between neighbouring section pieces, so UNDRAPE_NORMAL_TOL can be loose. Checked midpoints stay; at
 * most UNDRAPE_EDGE_PASSES passes and UNDRAPE_EDGE_MAX_SAMPLES samples per edge.
 */
export const UNDRAPE_SEED_TURN = 0.26;
export const UNDRAPE_SEED_MIN_GAP = 0.002;
export const UNDRAPE_TABLE_TOL = 0.1;
export const UNDRAPE_NORMAL_TOL = 5e-4;
export const UNDRAPE_EDGE_PASSES = 8;
export const UNDRAPE_EDGE_MAX_SAMPLES = 513;

/** Largest turn of the edge tangent between two edge samples, radians (keeps closed/curled edges resolved). */
export const UNDRAPE_EDGE_TURN = 0.5;

/**
 * Outline sampling (undrapeSeeds, undrapeOutline): seeds from each rim edge's structure (as above) plus the
 * chart's curvature breaks on lengthwise edges, gaps capped by options.spacing (default UNDRAPE_MAX_GAP); then
 * up to UNDRAPE_REFINE_PASSES passes map the midpoint of every unsettled span (one batched station pass each)
 * and split the spans whose midpoint misses the curve a fit through the samples would draw (undrapeMiss) by more
 * than options.tolerance (default UNDRAPE_TOLERANCE). Spans shorter than 2 * UNDRAPE_MIN_SPAN are not checked.
 * Lengthwise edges ask for stations by arc; the two long sides of a symmetric part ask for the same arcs and
 * share them. (Reading every station on every lengthwise edge that spans it was tried: it saved no stations on
 * the topsheet, and a foreign sample changes an edge's curve where nothing checked it.)
 */
export const UNDRAPE_TOLERANCE = 1.25e-6;
export const UNDRAPE_MAX_GAP = 0.05;
export const UNDRAPE_REFINE_PASSES = 10;
export const UNDRAPE_MIN_SPAN = 5e-4;

/**
 * An edge's first and last spans (when wider than 4 * UNDRAPE_END_CHECK) are also checked UNDRAPE_END_CHECK from
 * the vertex, metres: next to an outline vertex the outline can bend within a millimetre or so (research_undrape_map.md
 * 12, finding c: face ends slanted against the rim's; the wing roots of 4305), which the span's midpoint misses.
 */
export const UNDRAPE_END_CHECK = 1e-3;

/**
 * Spans touching a station measured by the kernel fallback (the U-turns) are refined only to UNDRAPE_KERNEL_TOL,
 * metres (the fallback's own accuracy: 9 samples per section edge, circle-corrected; 0.001 mm typical, 0.009 mm
 * worst against the kernel truth on the topsheet) and not below UNDRAPE_KERNEL_MIN_SPAN: each such station costs
 * ~0.1 s, and under the literal U-turn rule the outline there has sub-millimetre features (a 0.5 mm bump within
 * 4 mm at the topsheet's tail) that no affordable sampling resolves to the tolerance.
 */
export const UNDRAPE_KERNEL_TOL = 1e-5;
export const UNDRAPE_KERNEL_MIN_SPAN = 3e-3;

/** A zero-length join of the packed chart where the curvature jumps by more than this (1/m) is a break. */
export const UNDRAPE_BREAK_KAPPA = 1e-3;

/**
 * A station is refused (left to undrapeRefusedSection) where a crossed non-rim edge runs within
 * acos(UNDRAPE_MIN_CROSSING) = 18 deg of the station plane AND the face there is not normal to the plane
 * (in-plane part of its normal below UNDRAPE_MIN_INPLANE): the plane then cuts the face lengthwise and
 * one circular arc per face no longer fits. Face-end edges running straight across the part (normal in
 * the station plane) are crossed obliquely too but cut their faces across, and are kept.
 */
export const UNDRAPE_MIN_CROSSING = 0.95;
export const UNDRAPE_MIN_INPLANE = 0.995;

/**
 * A crossing tangent with less width component than this is (nearly) vertical: the section is then ordered by
 * face adjacency, not by width (undrapeSectionByFaces).
 */
export const UNDRAPE_OVERHANG = 0.05;

/** Crossings closer than this along a section are one point (a vertex met by several edges), metres. */
export const UNDRAPE_SAME_POINT = 1e-8;

/**
 * The same for sections ordered by face adjacency, metres: there a vertex split in two by the model's
 * tolerance (edge ends ~20 nm apart on 4305) would leave a face with three nodes.
 */
export const UNDRAPE_FACE_MERGE = 2e-6;

/** An edge sample this close to a station plane lies in it (an edge running straight across), metres. */
export const UNDRAPE_IN_PLANE = 5e-8;

/** An edge that misses a station plane by less than this at one end meets it there (a vertex station), metres. */
export const UNDRAPE_END_SNAP = 1e-6;

/** Two side faces whose normals along their common edge agree to within this cosine (0.06 deg) meet tangentially. */
export const UNDRAPE_CREASE_COS = 0.9999995;

/** Below this turning (radians) a piece is a straight chord. */
export const UNDRAPE_STRAIGHT = 1e-9;

/** A rim edge whose tangent keeps |t . u| >= this is sampled by station arc ("lengthwise", undrapeSeeds). */
export const UNDRAPE_LENGTHWISE = 0.5;

/** Station arcs closer than this share one station, metres. */
export const UNDRAPE_STATION_MERGE = 1e-8;

/**
 * Rim edge ends closer than this are one outline vertex where the topological vertex keys miss
 * (undrapeRimLoops), metres: tolerant models leave ends up to ~0.6 um apart (4305).
 */
export const UNDRAPE_VERTEX_MATCH = 1e-6;

/** Step between the three stations inside each edge end that give the flat edge its end tangent, metres. */
export const UNDRAPE_TANGENT_STEP = 2e-4;

/** A request point farther than this from every section node is interpolated rather than read, metres. */
export const UNDRAPE_NODE_TOL = 1e-7;

/** Kernel section pieces whose ends are closer than this join (steep sections are chained by end points), metres. */
export const UNDRAPE_KERNEL_JOIN = 1e-5;

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
//   19 tw0, 20 th0             unit section tangent on the edge's first side face (tw >= 0)
//   21 tw1, 22 th1             the same on its second side face (= first unless creased)
//   23 face0, 24 face1         the edge's side faces (indices into the side's faces; -1: none, a rim edge)

// ============================================================================
// Chart access (plain numbers; chartEval / chartSeedArc / chartSpanOf / chartFoot are edge_offset_utils')
// ============================================================================

/**
 * A station: its plane and the flat x of everything on it (before the alignment is subtracted).
 * x = arc - delta * theta(arc) is the length along the target (W offset by delta), as in unwrapFast.
 */
export function undrapeFrame(c is map, a is number) returns map
{
    const e = chartEval(c, a, chartSpanOf(c, a));
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

/** Chart arc of a world point (plain metres): the station whose plane holds it. */
export function undrapeFootArc(c is map, p is array, seed) returns number
{
    const a0 = (seed == undefined) ? chartSeedArc(c, p[0]) : seed;
    return chartFoot(c, p[0], p[1], p[2], a0, chartSpanOf(c, a0))[0];
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
 * @param samples {array} : per sample [param, px, py, pz, ux, uy, uz, nx, ny, nz] (undrapeAdaptiveEdge),
 *      ascending in the edge parameter: position, unit edge tangent, unit outward side-face normal. The tangents
 *      are re-signed to run with the samples. Each span's Hermite tangent length is its chord corrected for the
 *      turn between its end tangents, chord * (1 + phi^2 / 24): the arc of the circle through both ends with
 *      those tangents, so non-arc-length sampling is harmless.
 * @param others : undefined, or the second face's normals at each sample for a crease edge.
 * @param intoSign {number} : for a crease, +1 when cross(normal, tangent) points into the first face.
 * @returns {map} : { count, params, px py pz, ux uy uz, nx ny nz, [mx my mz, ix iy iz], mags, cum (arc at each
 *      sample), length, turn, cx cy cz / hx hy hz (padded box centre / half-extents), onRim, creased }
 */
export function undrapeEdgeTable(samples is array, others, intoSign is number, onRim is boolean) returns map
{
    const count = size(samples);
    var params = makeArray(count);
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
        const q = samples[i];
        params[i] = q[0];
        px[i] = q[1];
        py[i] = q[2];
        pz[i] = q[3];
        ux[i] = q[4];
        uy[i] = q[5];
        uz[i] = q[6];
        nx[i] = q[7];
        ny[i] = q[8];
        nz[i] = q[9];
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
        "params" : params,
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
 * Sample every edge of one plate side (kernel, once), adaptively (see UNDRAPE_SEED_TURN). onRim: the edge
 * also bounds a face that is not on this side (a wall), so it is part of the side's outline.
 *
 * Crease edges between two side faces (not tangent-continuous) are sampled on both faces when they run
 * along the reference (|edge tangent . t| >= UNDRAPE_LENGTHWISE at their middle): there the two faces give
 * the section different tangents either side of the crossing. A crease running across the reference (a
 * face end kinked lengthwise, common on pressed parts) turns the surface about a line lying nearly in the
 * station plane, which leaves the section's tangent almost unchanged; the first face serves, and the
 * piece turns telescope in the mid-surface correction anyway.
 *
 * @param c {map} : the packed chart (chart.packed).
 * @param tolerance {number} : how far (metres) a table's Hermite spans may miss the edge.
 * @returns {map} : { "tables" (undrapeEdgeTable maps, plus "edge" : the edge Query and "seedSpans" : the
 *      edge's structural span count), "faceCount", "samples" (total table samples) }
 */
export function undrapeSampleSide(context is Context, side is Query, c is map, tolerance is number) returns map
{
    const faces = evaluateQuery(context, side);
    // Edges and their side faces from the faces (fewer queries than asking every edge for its faces).
    // Every edge of a solid bounds two faces: an edge with one side face bounds a wall, i.e. is on the rim.
    var edges = [];
    var facesOf = {};
    var faceIndicesOf = {};
    for (var fi = 0; fi < size(faces); fi += 1)
    {
        const f = faces[fi];
        for (var edge in evaluateQuery(context, qAdjacent(f, AdjacencyType.EDGE, EntityType.EDGE)))
        {
            const key = edge.transientId;
            if (facesOf[key] == undefined)
            {
                facesOf[key] = [f];
                faceIndicesOf[key] = [fi];
                edges = append(edges, edge);
            }
            else
            {
                facesOf[key] = append(facesOf[key], f);
                faceIndicesOf[key] = append(faceIndicesOf[key], fi);
            }
        }
    }
    var tables = makeArray(size(edges));
    var total = 0;
    var seedTotal = 0;
    for (var k = 0; k < size(edges); k += 1)
    {
        const edge = edges[k];
        const sideFaces = facesOf[edge.transientId];
        const onRim = size(sideFaces) < 2;

        const rough3 = evEdgeTangentLines(context, { "edge" : edge, "parameters" : [0, 0.5, 1], "arcLengthParameterization" : false });
        const rough = undrapeRoughLength(rough3);
        const spans = undrapeSeedSpans(evCurveDefinition(context, { "edge" : edge }), rough[0], rough[1]);
        const samples = undrapeAdaptiveEdge(context, edge, sideFaces[0], spans, tolerance);
        total += size(samples);
        seedTotal += spans + 1;

        var creased = false;
        if (size(sideFaces) > 1)
        {
            const o = rough3[1].origin;
            const a0 = chartSeedArc(c, o[0].value);
            const f = chartFoot(c, o[0].value, o[1].value, o[2].value, a0, chartSpanOf(c, a0));
            const u = rough3[1].direction;
            creased = abs(u[0] * f[5] + u[1] * f[6] + u[2] * f[7]) >= UNDRAPE_LENGTHWISE
                && evEdgeConvexity(context, { "edge" : edge }) != EdgeConvexityType.SMOOTH;
        }

        var others = undefined;
        var intoSign = 1;
        if (creased)
        {
            // the second face's normals at the same samples, from its own knots
            var pts = makeArray(size(samples));
            for (var j = 0; j < size(samples); j += 1)
            {
                const q = samples[j];
                pts[j] = [q[0], q[1], q[2], q[3], q[4], q[5], q[6]];
            }
            others = undrapeFillNormals(pts, undrapeNormalKnots(context, edge, sideFaces[1], undrapeSeedParams(spans)));
            // which way the first face lies: its loop direction at the edge's middle (the same point either way)
            const oriented = evFaceTangentPlanesAtEdge(context, { "edge" : edge, "face" : sideFaces[0], "parameters" : [0.5],
                        "arcLengthParameterization" : false, "usingFaceOrientation" : true });
            intoSign = (dot(oriented[0].x, rough3[1].direction) >= 0) ? 1 : -1;
        }
        var table = undrapeEdgeTable(samples, others, intoSign, onRim);
        table.edge = edge;
        table.seedSpans = spans;
        // the side faces as indices (section ordering by face adjacency): first, second (-1 on the rim)
        const fis = faceIndicesOf[edge.transientId];
        table.faces = [fis[0], (size(fis) > 1) ? fis[1] : -1];
        if (onRim)
        {
            table.vertexKeys = undrapeVertexKeys(context, edge, table);
        }
        tables[k] = table;
    }
    return { "tables" : tables, "faceCount" : size(faces), "samples" : total, "seeds" : seedTotal };
}

/**
 * How many spans an edge is seeded with, from its own structure (evCurveDefinition): a line 1, a B-spline one
 * per control-point interval, anything else (arcs included) one per UNDRAPE_SEED_TURN of turn; at least one
 * per UNDRAPE_EDGE_TURN, and seeds no closer than UNDRAPE_SEED_MIN_GAP (a long B-spline trimmed to a short edge).
 * @param length {number}, turn {number} : rough length (metres) and turn (radians), undrapeRoughLength.
 */
export function undrapeSeedSpans(definition, length is number, turn is number) returns number
{
    var spans = 2;
    if (definition is Line)
    {
        spans = 1;
    }
    else if (definition is BSplineCurve)
    {
        spans = max(2, size(definition.controlPoints) - 1);
    }
    else
    {
        spans = max(2, ceil(turn / UNDRAPE_SEED_TURN));
    }
    spans = max(spans, ceil(turn / UNDRAPE_EDGE_TURN));
    return max(1, min(spans, ceil(length / UNDRAPE_SEED_MIN_GAP)));
}

/**
 * One edge read on one side face, as dense as the table needs (see UNDRAPE_SEED_TURN). Positions and tangents
 * (evEdgeTangentLines, ~15 us per point) are refined by undrapeRefineEdge. Side normals (evFaceTangentPlanesAtEdge,
 * ~13x dearer per point) are read at their own, coarser knots (undrapeNormalKnots); every table sample takes its
 * normal by linear interpolation in the parameter between the knots, made normal to its tangent (exact at a knot).
 * @returns {array} : samples [param, px, py, pz, ux, uy, uz, nx, ny, nz], ascending in the parameter
 */
export function undrapeAdaptiveEdge(context is Context, edge is Query, face is Query, spans is number, tolerance is number) returns array
{
    const seeds = undrapeSeedParams(spans);
    const pts = undrapeRefineEdge(context, edge, seeds, tolerance);
    const normals = undrapeFillNormals(pts, undrapeNormalKnots(context, edge, face, seeds));
    var samples = makeArray(size(pts));
    for (var i = 0; i < size(pts); i += 1)
    {
        const q = pts[i];
        const n = normals[i];
        samples[i] = [q[0], q[1], q[2], q[3], q[4], q[5], q[6], n[0], n[1], n[2]];
    }
    return samples;
}

/** `spans` equal parameter steps over [0, 1]: spans + 1 parameters. */
export function undrapeSeedParams(spans is number) returns array
{
    var seeds = makeArray(spans + 1);
    for (var j = 0; j <= spans; j += 1)
    {
        seeds[j] = j / spans;
    }
    return seeds;
}

/** The seeds with every span's midpoint between them: 2 * spans + 1 parameters. */
export function undrapeWithMidpoints(seeds is array) returns array
{
    const spans = size(seeds) - 1;
    var ask = makeArray(2 * spans + 1);
    for (var j = 0; j < spans; j += 1)
    {
        ask[2 * j] = seeds[j];
        ask[2 * j + 1] = 0.5 * (seeds[j] + seeds[j + 1]);
    }
    ask[2 * spans] = seeds[spans];
    return ask;
}

/**
 * An edge's positions and unit tangents (its own direction), as dense as the Hermite table needs: the seeds plus
 * every seed span's midpoint in one evEdgeTangentLines call, then one call per pass for the midpoints of the spans
 * whose midpoint failed (undrapePositionSplit). Checked midpoints stay.
 * @returns {array} : [param, px, py, pz, ux, uy, uz], ascending in the parameter
 */
export function undrapeRefineEdge(context is Context, edge is Query, seeds is array, tolerance is number) returns array
{
    const spans = size(seeds) - 1;
    const read = undrapeEdgeLines(context, edge, undrapeWithMidpoints(seeds));
    var pts = makeArray(spans + 1);
    var mids = makeArray(spans);
    for (var j = 0; j <= spans; j += 1)
    {
        pts[j] = read[2 * j];
        if (j < spans)
        {
            mids[j] = read[2 * j + 1];
        }
    }
    var open = makeArray(spans, true);
    var openCount = spans;
    for (var pass = 0; pass < UNDRAPE_EDGE_PASSES; pass += 1)
    {
        // check the midpoints of the open spans; checked midpoints stay
        const n = size(pts);
        var next = makeArray(n + openCount);
        var nextOpen = makeArray(n + openCount - 1, false);
        var at = 0;
        var k = 0;
        var more = [];
        for (var j = 0; j < n; j += 1)
        {
            next[at] = pts[j];
            if (j + 1 == n)
            {
                break;
            }
            if (open[j])
            {
                const m = mids[k];
                k += 1;
                next[at + 1] = m;
                if (undrapePositionSplit(pts[j], m, pts[j + 1], tolerance))
                {
                    if (m[0] - pts[j][0] > 1e-9)
                    {
                        nextOpen[at] = true;
                        more = append(more, 0.5 * (pts[j][0] + m[0]));
                    }
                    if (pts[j + 1][0] - m[0] > 1e-9)
                    {
                        nextOpen[at + 1] = true;
                        more = append(more, 0.5 * (m[0] + pts[j + 1][0]));
                    }
                }
                at += 2;
            }
            else
            {
                at += 1;
            }
        }
        pts = next;
        open = nextOpen;
        openCount = size(more);
        if (openCount == 0 || pass + 1 == UNDRAPE_EDGE_PASSES || size(pts) + openCount > UNDRAPE_EDGE_MAX_SAMPLES)
        {
            break;
        }
        mids = undrapeEdgeLines(context, edge, more);
    }
    return pts;
}

/** Positions and unit tangents (the edge's own direction) at edge parameters: [param, px, py, pz, ux, uy, uz]. */
export function undrapeEdgeLines(context is Context, edge is Query, params is array) returns array
{
    const lines = evEdgeTangentLines(context, { "edge" : edge, "parameters" : params, "arcLengthParameterization" : false });
    var result = makeArray(size(lines));
    for (var j = 0; j < size(lines); j += 1)
    {
        const o = lines[j].origin;
        const u = lines[j].direction;
        result[j] = [params[j], o[0].value, o[1].value, o[2].value, u[0], u[1], u[2]];
    }
    return result;
}

/**
 * A face's unit outward normals along an edge at knots: the seeds and their midpoints in one
 * evFaceTangentPlanesAtEdge call, then per pass the midpoints of every knot span whose midpoint misses the normal
 * interpolated linearly in the parameter by more than 4 * UNDRAPE_NORMAL_TOL (radians; the midpoint stays, so the
 * interpolation is then about a quarter of that off).
 * @returns {array} : [param, nx, ny, nz], ascending in the parameter
 */
export function undrapeNormalKnots(context is Context, edge is Query, face is Query, seeds is array) returns array
{
    const spans = size(seeds) - 1;
    const read = undrapeEdgeNormals(context, edge, face, undrapeWithMidpoints(seeds));
    var knots = makeArray(spans + 1);
    var mids = makeArray(spans);
    for (var j = 0; j <= spans; j += 1)
    {
        knots[j] = read[2 * j];
        if (j < spans)
        {
            mids[j] = read[2 * j + 1];
        }
    }
    const limit = 16 * UNDRAPE_NORMAL_TOL * UNDRAPE_NORMAL_TOL;
    var open = makeArray(spans, true);
    var openCount = spans;
    for (var pass = 0; pass < UNDRAPE_EDGE_PASSES; pass += 1)
    {
        const n = size(knots);
        var next = makeArray(n + openCount);
        var nextOpen = makeArray(n + openCount - 1, false);
        var at = 0;
        var k = 0;
        var more = [];
        for (var j = 0; j < n; j += 1)
        {
            next[at] = knots[j];
            if (j + 1 == n)
            {
                break;
            }
            if (open[j])
            {
                const a = knots[j];
                const m = mids[k];
                const b = knots[j + 1];
                k += 1;
                next[at + 1] = m;
                // |interpolated x read|^2 / |interpolated|^2 = sin^2 of the miss
                const x = a[1] + b[1];
                const y = a[2] + b[2];
                const z = a[3] + b[3];
                const sx = y * m[3] - z * m[2];
                const sy = z * m[1] - x * m[3];
                const sz = x * m[2] - y * m[1];
                if ((sx * sx + sy * sy + sz * sz) > limit * (x * x + y * y + z * z) && b[0] - a[0] > 2e-9)
                {
                    nextOpen[at] = true;
                    nextOpen[at + 1] = true;
                    more = append(more, 0.5 * (a[0] + m[0]));
                    more = append(more, 0.5 * (m[0] + b[0]));
                }
                at += 2;
            }
            else
            {
                at += 1;
            }
        }
        knots = next;
        open = nextOpen;
        openCount = size(more);
        if (openCount == 0 || pass + 1 == UNDRAPE_EDGE_PASSES || size(knots) + openCount > UNDRAPE_EDGE_MAX_SAMPLES)
        {
            break;
        }
        mids = undrapeEdgeNormals(context, edge, face, more);
    }
    return knots;
}

/** A face's unit outward normals at edge parameters: [param, nx, ny, nz]. */
export function undrapeEdgeNormals(context is Context, edge is Query, face is Query, params is array) returns array
{
    const planes = evFaceTangentPlanesAtEdge(context, { "edge" : edge, "face" : face, "parameters" : params,
                "arcLengthParameterization" : false });
    var result = makeArray(size(planes));
    for (var j = 0; j < size(planes); j += 1)
    {
        const n = planes[j].normal;
        result[j] = [params[j], n[0], n[1], n[2]];
    }
    return result;
}

/**
 * The normal at every sample of `pts` ([param, px, py, pz, ux, uy, uz], ascending) from `knots` ([param, nx, ny, nz],
 * ascending, the same end parameters): linear in the parameter between the bracketing knots, made normal to the
 * sample's tangent and unit; exactly the knot's where the parameters agree. @returns {array} : [x, y, z] per sample
 */
export function undrapeFillNormals(pts is array, knots is array) returns array
{
    var result = makeArray(size(pts));
    var k = 0;
    for (var i = 0; i < size(pts); i += 1)
    {
        const q = pts[i];
        const p = q[0];
        while (k + 2 < size(knots) && knots[k + 1][0] < p)
        {
            k += 1;
        }
        const a = knots[k];
        const b = knots[k + 1];
        if (abs(p - a[0]) < 1e-15)
        {
            result[i] = [a[1], a[2], a[3]];
            continue;
        }
        if (abs(p - b[0]) < 1e-15)
        {
            result[i] = [b[1], b[2], b[3]];
            continue;
        }
        const w = b[0] - a[0];
        const f = (w < 1e-15) ? 0 : clamp((p - a[0]) / w, 0, 1);
        var x = a[1] + f * (b[1] - a[1]);
        var y = a[2] + f * (b[2] - a[2]);
        var z = a[3] + f * (b[3] - a[3]);
        const d = x * q[4] + y * q[5] + z * q[6];
        x = x - d * q[4];
        y = y - d * q[5];
        z = z - d * q[6];
        const len = sqrt(x * x + y * y + z * z);
        result[i] = [x / len, y / len, z / len];
    }
    return result;
}

/**
 * Whether the table span from sample a to sample b ([param, px, py, pz, ux, uy, uz], tangents along the parameter)
 * needs its midpoint sample m: m lies farther than 4 * `tolerance` from the Hermite span (nearest point, Gauss-Newton
 * from the chord fraction), or either half turns more than UNDRAPE_EDGE_TURN. The midpoint stays in the table, and a
 * Hermite span's miss falls with the fourth power of its length: the refined table misses by about 1/16 of the
 * checked miss, i.e. by `tolerance` / 4 at most.
 */
export function undrapePositionSplit(a is array, m is array, b is array, tolerance is number) returns boolean
{
    const cam = clamp(a[4] * m[4] + a[5] * m[5] + a[6] * m[6], -1, 1);
    const cmb = clamp(m[4] * b[4] + m[5] * b[5] + m[6] * b[6], -1, 1);
    const limit = cos(UNDRAPE_EDGE_TURN * radian);
    if (cam < limit || cmb < limit)
    {
        return true;
    }
    const dx = b[1] - a[1];
    const dy = b[2] - a[2];
    const dz = b[3] - a[3];
    const chord2 = dx * dx + dy * dy + dz * dz;
    if (chord2 < 1e-24)
    {
        return false;
    }
    const c = clamp(a[4] * b[4] + a[5] * b[5] + a[6] * b[6], -1, 1);
    var phi2 = 2 * (1 - c);
    phi2 = (c > 0.9) ? phi2 + phi2 * phi2 / 12 : (acos(c) / radian) * (acos(c) / radian);
    const mag = sqrt(chord2) * (1 + phi2 / 24);
    var f = clamp(((m[1] - a[1]) * dx + (m[2] - a[2]) * dy + (m[3] - a[3]) * dz) / chord2, 0, 1);
    var ex = 0;
    var ey = 0;
    var ez = 0;
    for (var step = 0; step < 3; step += 1)
    {
        const f2 = f * f;
        const f3 = f2 * f;
        const b0 = 2 * f3 - 3 * f2 + 1;
        const b1 = (f3 - 2 * f2 + f) * mag;
        const b2 = -2 * f3 + 3 * f2;
        const b3 = (f3 - f2) * mag;
        ex = b0 * a[1] + b1 * a[4] + b2 * b[1] + b3 * b[4] - m[1];
        ey = b0 * a[2] + b1 * a[5] + b2 * b[2] + b3 * b[5] - m[2];
        ez = b0 * a[3] + b1 * a[6] + b2 * b[3] + b3 * b[6] - m[3];
        const g0 = 6 * f2 - 6 * f;
        const g1 = (3 * f2 - 4 * f + 1) * mag;
        const g3 = (3 * f2 - 2 * f) * mag;
        const tx = g0 * a[1] + g1 * a[4] - g0 * b[1] + g3 * b[4];
        const ty = g0 * a[2] + g1 * a[5] - g0 * b[2] + g3 * b[5];
        const tz = g0 * a[3] + g1 * a[6] - g0 * b[3] + g3 * b[6];
        const tt = tx * tx + ty * ty + tz * tz;
        if (tt < 1e-30)
        {
            break;
        }
        const df = (ex * tx + ey * ty + ez * tz) / tt;
        f = clamp(f - df, 0, 1);
        if (abs(df) < 1e-12)
        {
            break;
        }
    }
    return ex * ex + ey * ey + ez * ez > 16 * tolerance * tolerance;
}

/**
 * The topological vertices at the first and last sample of a rim edge, as transient ids: rim loops are
 * chained by these, not by position. Tolerant models leave neighbouring edges' end points apart by up to a
 * micron or so (0.4 and 0.6 um on the 0.44 mm wing plate 4305, which split its outline into 7 open chains).
 * A closed edge has one vertex (or none): both ends get the same key.
 */
export function undrapeVertexKeys(context is Context, edge is Query, tb is map) returns array
{
    const vertices = evaluateQuery(context, qAdjacent(edge, AdjacencyType.VERTEX, EntityType.VERTEX));
    if (size(vertices) == 0)
    {
        const key = "closed:" ~ edge.transientId;
        return [key, key];
    }
    if (size(vertices) == 1)
    {
        return [vertices[0].transientId, vertices[0].transientId];
    }
    const last = tb.count - 1;
    const p = evVertexPoint(context, { "vertex" : vertices[0] });
    const d0 = (p[0].value - tb.px[0]) * (p[0].value - tb.px[0]) + (p[1].value - tb.py[0]) * (p[1].value - tb.py[0])
        + (p[2].value - tb.pz[0]) * (p[2].value - tb.pz[0]);
    const d1 = (p[0].value - tb.px[last]) * (p[0].value - tb.px[last]) + (p[1].value - tb.py[last]) * (p[1].value - tb.py[last])
        + (p[2].value - tb.pz[last]) * (p[2].value - tb.pz[last]);
    return (d0 <= d1) ? [vertices[0].transientId, vertices[1].transientId] : [vertices[1].transientId, vertices[0].transientId];
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
    var tw1 = tw;
    var th1 = th;
    if (tb.creased)
    {
        var mx = r * tb.mx[j] + g * tb.mx[k];
        var my = r * tb.my[j] + g * tb.my[k];
        var mz = r * tb.mz[j] + g * tb.mz[k];
        const mn = sqrt(mx * mx + my * my + mz * mz);
        const s2 = undrapeTangent(fr, mx / mn, my / mn, mz / mn);
        tw1 = s2[0];
        th1 = s2[1];
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
            twA, thA, twB, thB, inPlane, cosCross, tb.onRim ? 1 : 0, e, nx, ny, nz, valid, qx, qy, qz, sample, i,
            tw, th, tw1, th1, tb.faces[0], tb.faces[1]];
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
    // An edge that just misses the plane at an end: a station through a vertex whose foot came out a
    // fraction of a micron off (the tip of a pointed end, past the reference's end, sat 0.12 um off on 4305).
    if (size(result) == 0 && min(abs(gFirst), abs(gLast)) < UNDRAPE_END_SNAP)
    {
        result = [(abs(gFirst) <= abs(gLast)) ? undrapeCrossingAt(tb, e, 0, 0, fr, 1) : undrapeCrossingAt(tb, e, count - 2, 1, fr, 1)];
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
 * Crossings are ordered by width, coincident ones merged, and the pieces between them measured as circular
 * arcs. The mid-surface is the side offset inward by half the in-plane thickness t / inPlane, so each
 * piece's mid length is its side length + sideSign * (t' / 2) * turn; a crease (two side faces meeting at
 * an angle across the section) adds the same for its own turn. A gap (slot, notch) needs no special case:
 * the piece between its two rim crossings is the circular bridge with the gap's end tangents (the surface
 * continued smoothly across).
 *
 * A section that is not single-valued in width -- a wall at or past vertical, such as the 90 deg wings of a
 * pressed channel -- cannot be ordered by width: it is ordered by the side's face adjacency instead
 * (undrapeSectionByFaces), which unrolls it by arc length whatever it turns through.
 *
 * @returns {map} : { "ok", "why", "kernel" : false, "pts" (merged crossings in section order), "mid"
 *      (signed mid-surface arc from the centreline w = 0 at each), "crease" (half the mid correction of each
 *      node's own turn), "nodeOf" (the node each input crossing became, -1: none), "byFaces", "thickness",
 *      "sideSign" }
 */
export function undrapeSection(crossings is array, thickness is number, sideSign is number) returns map
{
    const count = size(crossings);
    if (count < 1)
    {
        return { "ok" : false, "why" : "no crossings" };
    }
    var steep = false;
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
            steep = true;
        }
    }
    if (steep)
    {
        return undrapeSectionByFaces(crossings, thickness, sideSign);
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
    pts = resize(pts, n);
    return undrapeSectionMid(pts, nodeOf, thickness, sideSign, false);
}

/**
 * Mid-surface arcs along ordered section nodes (tangents [2, 3] arriving and [4, 5] leaving, both along
 * the section's direction), zeroed at the centreline: at the piece crossing w = 0 nearest the reference
 * (smallest |h|). A plate that does not reach w = 0 is measured from its innermost node, which keeps its
 * own width (open decision 9.4). One node alone (a station through the tip of a pointed end) is a section
 * of zero extent.
 */
export function undrapeSectionMid(pts is array, nodeOf is array, thickness is number, sideSign is number, byFaces is boolean) returns map
{
    const n = size(pts);
    var mid = makeArray(n, 0);
    var crease = makeArray(n, 0);
    for (var i = 0; i < n; i += 1)
    {
        const p = pts[i];
        const turn = atan2(p[2] * p[5] - p[3] * p[4], p[2] * p[4] + p[3] * p[5]) / radian;
        if (abs(turn) < UNDRAPE_STRAIGHT)
        {
            continue;
        }
        const half = thickness / (2 * p[6]);
        // A crease one short leg from the rim (a wall rising from nothing at a wing's root) takes its corner
        // arc in over the first t'/2 of the leg, not all at once: the exact offset would jump by
        // (t'/2) * turn -- 0.35 mm on 4305 -- within the first micron of wall.
        var ramp = 1;
        for (var j in [0, n - 1])
        {
            if (abs(i - j) == 1 && pts[j][8] == 1)
            {
                const dw = pts[j][0] - p[0];
                const dh = pts[j][1] - p[1];
                ramp = min(ramp, sqrt(dw * dw + dh * dh) / half);
            }
        }
        crease[i] = 0.5 * sideSign * half * turn * ramp;
    }
    var zeroIndex = -1;
    var zeroH = 1e30;
    for (var i = 0; i + 1 < n; i += 1)
    {
        const piece = undrapePiece(pts[i], pts[i + 1]);
        mid[i + 1] = mid[i] + crease[i] + piece[0] + sideSign * thickness / (pts[i][6] + pts[i + 1][6]) * piece[1] + crease[i + 1];
        const wa = pts[i][0];
        const wb = pts[i + 1][0];
        if ((wa <= 0 && wb > 0) || (wa > 0 && wb <= 0))
        {
            const h = abs(pts[i][1] + (pts[i + 1][1] - pts[i][1]) * (0 - wa) / (wb - wa));
            if (h < zeroH)
            {
                zeroH = h;
                zeroIndex = i;
            }
        }
    }

    var zero = 0;
    if (zeroIndex >= 0)
    {
        const a = pts[zeroIndex];
        const b = pts[zeroIndex + 1];
        const part = undrapePieceToWidth(a, b, 0);
        zero = mid[zeroIndex] + crease[zeroIndex] + part[0] + sideSign * thickness / (a[6] + b[6]) * part[1];
    }
    else if (pts[0][0] > 0)
    {
        zero = mid[0] - pts[0][0];
    }
    else
    {
        zero = mid[n - 1] - pts[n - 1][0];
    }
    for (var i = 0; i < n; i += 1)
    {
        mid[i] = mid[i] - zero;
    }
    return { "ok" : true, "kernel" : false, "byFaces" : byFaces, "pts" : pts, "mid" : mid, "crease" : crease, "nodeOf" : nodeOf,
            "thickness" : thickness, "sideSign" : sideSign };
}

/**
 * A section that is not single-valued in width (a wall at or past vertical: tangents with tw below
 * UNDRAPE_OVERHANG), ordered by the side's face adjacency instead of by width.
 *
 * Coincident crossings merge into nodes; each node knows the side faces its edges bound and the section's
 * tangent on each. The piece of the section inside a face joins the two nodes on that face's edges, so a
 * face with exactly two nodes is a link; a face touching the plane at one node only (a face ending at a
 * vertex on the plane) is none. The links form paths, walked from their ends; several paths (a slot) are
 * joined in order of width by bridge pieces. Each tangent is then oriented along the walk, which is what
 * lets the section turn past 90 deg: every piece is still the circular arc between its end tangents,
 * measured by arc length across the plate (the unfold of a folded wing).
 *
 * Refused (left to the kernel) when a face holds three or more nodes, a node joins three or more faces'
 * pieces, or the links close into a loop: the section is not a simple chain of face pieces there.
 */
export function undrapeSectionByFaces(crossings is array, thickness is number, sideSign is number) returns map
{
    const count = size(crossings);
    // nodes: merged crossings
    var nodes = [];
    var nodeOf = makeArray(count, -1);
    for (var i = 0; i < count; i += 1)
    {
        const c = crossings[i];
        var found = -1;
        for (var k = 0; k < size(nodes); k += 1)
        {
            const dw = nodes[k][0] - c[0];
            const dh = nodes[k][1] - c[1];
            if (dw * dw + dh * dh <= UNDRAPE_FACE_MERGE * UNDRAPE_FACE_MERGE)
            {
                found = k;
                break;
            }
        }
        if (found < 0)
        {
            found = size(nodes);
            nodes = append(nodes, c);
        }
        else if (c[8] == 1 && nodes[found][8] == 0)
        {
            var merged = nodes[found];
            merged[8] = 1;
            nodes[found] = merged;
        }
        nodeOf[i] = found;
    }
    const m = size(nodes);

    // faces -> nodes, and each node's section tangent on each face. A node reached on a face only by edges
    // ENDING on the plane (samples) may be a vertex where the face merely touches the plane.
    var faceNodes = {};
    var tangentOn = makeArray(m, {});
    var crossesOn = makeArray(m, {});
    for (var i = 0; i < count; i += 1)
    {
        const c = crossings[i];
        const k = nodeOf[i];
        for (var side = 0; side < 2; side += 1)
        {
            const f = c[23 + side];
            if (f < 0)
            {
                continue;
            }
            if (tangentOn[k][f] == undefined)
            {
                tangentOn[k][f] = [c[19 + 2 * side], c[20 + 2 * side]];
                faceNodes[f] = (faceNodes[f] == undefined) ? [k] : append(faceNodes[f], k);
            }
            if (c[17] == 0)
            {
                crossesOn[k][f] = true;
            }
        }
    }

    // links: faces with exactly two nodes. A face with more keeps only the nodes where its edges run
    // through the plane: the others are vertices where the face touches the plane and ends (a face end
    // slanting away from a rim vertex on the plane).
    var links = makeArray(m, []);
    var linkFace = makeArray(m, []);
    for (var f, all in faceNodes)
    {
        var list = all;
        if (size(list) > 2)
        {
            list = [];
            for (var k in all)
            {
                if (crossesOn[k][f] == true)
                {
                    list = append(list, k);
                }
            }
        }
        if (size(list) == 2)
        {
            links[list[0]] = append(links[list[0]], list[1]);
            linkFace[list[0]] = append(linkFace[list[0]], f);
            links[list[1]] = append(links[list[1]], list[0]);
            linkFace[list[1]] = append(linkFace[list[1]], f);
        }
        else if (size(list) > 2)
        {
            return { "ok" : false, "why" : "a face is crossed more than once (steep section)" };
        }
    }
    for (var k = 0; k < m; k += 1)
    {
        if (size(links[k]) > 2)
        {
            return { "ok" : false, "why" : "the section branches (steep section)" };
        }
    }

    // walk the paths from their ends
    var used = makeArray(m, false);
    var paths = [];
    for (var k = 0; k < m; k += 1)
    {
        if (used[k] || size(links[k]) != 1)
        {
            continue;
        }
        var path = [k];
        used[k] = true;
        var current = k;
        var previous = -1;
        while (true)
        {
            var next = -1;
            for (var q in links[current])
            {
                if (q != previous && !used[q])
                {
                    next = q;
                }
            }
            if (next < 0)
            {
                break;
            }
            used[next] = true;
            path = append(path, next);
            previous = current;
            current = next;
        }
        if (nodes[path[size(path) - 1]][0] < nodes[path[0]][0])
        {
            path = reverse(path);
        }
        paths = append(paths, path);
    }
    for (var k = 0; k < m; k += 1)
    {
        if (!used[k] && size(links[k]) > 0)
        {
            return { "ok" : false, "why" : "the section closes on itself (steep section)" };
        }
    }
    if (size(paths) == 0)
    {
        // no piece at all: a single point (the tip of a pointed end), else nothing usable
        if (m != 1)
        {
            return { "ok" : false, "why" : "no section pieces (steep section)" };
        }
        paths = [[0]];
    }
    paths = sort(paths, function(p, q) { return nodes[p[0]][0] - nodes[q[0]][0]; });
    var sequence = [];
    for (var path in paths)
    {
        sequence = concatenateArrays([sequence, path]);
    }

    // ordered nodes with tangents oriented along the walk: arriving on the face shared with the previous
    // node, leaving on the face shared with the next (a bridge between paths: the node's first tangent)
    const n = size(sequence);
    var position = makeArray(m, -1);
    for (var j = 0; j < n; j += 1)
    {
        position[sequence[j]] = j;
    }
    var pts = makeArray(n);
    for (var j = 0; j < n; j += 1)
    {
        const k = sequence[j];
        var p = nodes[k];
        var arrive = undefined;
        var leave = undefined;
        for (var side = 0; side < 2; side += 1)
        {
            const other = (side == 0) ? j - 1 : j + 1;
            if (other < 0 || other >= n)
            {
                continue;
            }
            const q = nodes[sequence[other]];
            var t = undefined;
            for (var l = 0; l < size(links[k]); l += 1)
            {
                if (links[k][l] == sequence[other])
                {
                    t = tangentOn[k][linkFace[k][l]];
                }
            }
            if (t == undefined)
            {
                t = [p[19], p[20]];
            }
            // along the walk: from the previous node to this one, or from this one to the next
            const cw = (side == 0) ? p[0] - q[0] : q[0] - p[0];
            const ch = (side == 0) ? p[1] - q[1] : q[1] - p[1];
            if (t[0] * cw + t[1] * ch < 0)
            {
                t = [-t[0], -t[1]];
            }
            if (side == 0)
            {
                arrive = t;
            }
            else
            {
                leave = t;
            }
        }
        if (arrive == undefined)
        {
            arrive = (leave == undefined) ? [p[19], p[20]] : leave;
        }
        if (leave == undefined)
        {
            leave = arrive;
        }
        p[2] = arrive[0];
        p[3] = arrive[1];
        p[4] = leave[0];
        p[5] = leave[1];
        pts[j] = p;
    }
    for (var i = 0; i < count; i += 1)
    {
        nodeOf[i] = position[nodeOf[i]];
    }
    return undrapeSectionMid(pts, nodeOf, thickness, sideSign, true);
}

/**
 * Nearest point of the circular piece a -> b to (w0, h0): [squared distance, arc from a, turn from a,
 * clamped: -1 before a, 1 past b, 0 inside]. The circle is a + R (sin(psi) ta + (1 - cos(psi)) na) about the
 * centre a + R na, so psi is the signed angle from (a - centre) to (p - centre).
 */
export function undrapePieceNearest(a is array, b is array, w0 is number, h0 is number) returns array
{
    const piece = undrapePiece(a, b);
    if (abs(piece[1]) < 1e-6 || piece[2] < 1e-15)
    {
        const dw = b[0] - a[0];
        const dh = b[1] - a[1];
        const l2 = dw * dw + dh * dh;
        var f = (l2 < 1e-30) ? 0 : ((w0 - a[0]) * dw + (h0 - a[1]) * dh) / l2;
        const clamped = (f < 0) ? -1 : ((f > 1) ? 1 : 0);
        f = clamp(f, 0, 1);
        const ew = a[0] + f * dw - w0;
        const eh = a[1] + f * dh - h0;
        return [ew * ew + eh * eh, f * piece[0], f * piece[1], clamped];
    }
    const radius = piece[2] / (2 * sin(0.5 * piece[1] * radian));
    const cw = a[0] - radius * a[5];
    const ch = a[1] + radius * a[4];
    const uw = a[0] - cw;
    const uh = a[1] - ch;
    const vw = w0 - cw;
    const vh = h0 - ch;
    var psi = atan2(uw * vh - uh * vw, uw * vw + uh * vh) / radian;
    var clamped = 0;
    // outside the piece's angular range: the nearer end
    const lo = min(0, piece[1]);
    const hi = max(0, piece[1]);
    if (psi < lo || psi > hi)
    {
        const dLo = (abs(psi - lo) < PI) ? abs(psi - lo) : 2 * PI - abs(psi - lo);
        const dHi = (abs(psi - hi) < PI) ? abs(psi - hi) : 2 * PI - abs(psi - hi);
        psi = (dLo <= dHi) ? lo : hi;
        clamped = (psi == 0) ? -1 : 1;
    }
    const sp = sin(psi * radian);
    const cp = cos(psi * radian);
    const pw = a[0] + radius * (sp * a[4] - (1 - cp) * a[5]);
    const ph = a[1] + radius * (sp * a[5] + (1 - cp) * a[4]);
    return [(pw - w0) * (pw - w0) + (ph - h0) * (ph - h0), abs(radius * psi), psi, clamped];
}

/**
 * Signed mid-surface arc from the centreline to the section point at (w0, h0) of the sampled side: the
 * undraped transverse coordinate of that point and of everything on its normal through the plate.
 * A node within UNDRAPE_NODE_TOL is read directly; otherwise the circular piece holding w0 (edge
 * sections ordered by width), the nearest circular piece (sections ordered by faces) or the nearest chord
 * of the sampled chain (kernel sections) is used.
 */
export function undrapeMidAt(section is map, w0 is number, h0 is number) returns number
{
    const pts = section.pts;
    const n = size(pts);
    if (section.kernel)
    {
        return undrapeChainMidAt(section, w0, h0);
    }
    if (section.byFaces || n == 1)
    {
        return undrapeMidNearest(section, w0, h0);
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
    return section.mid[j] + section.crease[j] + part[0] + section.sideSign * section.thickness / (pts[j][6] + pts[j + 1][6]) * part[1];
}

/** undrapeMidAt on a section ordered by faces: the nearest node or circular piece; past an end, along its tangent. */
export function undrapeMidNearest(section is map, w0 is number, h0 is number) returns number
{
    const pts = section.pts;
    const n = size(pts);
    for (var k = 0; k < n; k += 1)
    {
        const dw = pts[k][0] - w0;
        const dh = pts[k][1] - h0;
        if (dw * dw + dh * dh < UNDRAPE_NODE_TOL * UNDRAPE_NODE_TOL)
        {
            return section.mid[k];
        }
    }
    if (n == 1)
    {
        return section.mid[0] + (w0 - pts[0][0]);
    }
    var best = undefined;
    var bestIndex = 0;
    for (var j = 0; j + 1 < n; j += 1)
    {
        const r = undrapePieceNearest(pts[j], pts[j + 1], w0, h0);
        if (best == undefined || r[0] < best[0])
        {
            best = r;
            bestIndex = j;
        }
    }
    const j = bestIndex;
    if (j == 0 && best[3] < 0)
    {
        return section.mid[0] + (w0 - pts[0][0]) * pts[0][4] + (h0 - pts[0][1]) * pts[0][5];
    }
    if (j == n - 2 && best[3] > 0)
    {
        return section.mid[n - 1] + (w0 - pts[n - 1][0]) * pts[n - 1][2] + (h0 - pts[n - 1][1]) * pts[n - 1][3];
    }
    return section.mid[j] + section.crease[j] + best[1] + section.sideSign * section.thickness / (pts[j][6] + pts[j + 1][6]) * best[2];
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
            if (chain.steep != true && gap > 0 && gap * gap > best)
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
 * @returns {map} : { "ws", "hs", "arcs" } (side arc along the chain, from its first point); "refused" : true
 *      when the kernel could not intersect.
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
    // only the kernel op may fail here (a plane exactly through a vertex: "Failed to completely
    // disambiguate created topology"); everything after it is ours and must not be silenced
    var cut = false;
    try silent
    {
        opIntersectFaces(context, ixId, { "tools" : qCreatedBy(planeId, EntityType.FACE), "targets" : qUnion(hits) });
        cut = true;
    }
    if (!cut)
    {
        return { "ws" : [], "hs" : [], "arcs" : [], "refused" : true };
    }

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
    // A wall at or past vertical: the pieces do not follow each other in width; chain them by end points.
    for (var piece in pieces)
    {
        for (var j = 1; j < size(piece[0]); j += 1)
        {
            if (piece[0][j] - piece[0][j - 1] < UNDRAPE_OVERHANG * (piece[2][j] - piece[2][j - 1]))
            {
                return undrapeChainByEnds(pieces);
            }
        }
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

/**
 * Arc of a chain at its crossing of w = 0 (linear between samples; of several, the one nearest the
 * reference, smallest |h|); the innermost point keeps its w otherwise.
 */
export function undrapeChainZero(chain is map) returns number
{
    const ws = chain.ws;
    const hs = chain.hs;
    const n = size(ws);
    var best = 1e30;
    var result = undefined;
    for (var i = 0; i + 1 < n; i += 1)
    {
        if ((ws[i] <= 0 && ws[i + 1] > 0) || (ws[i] > 0 && ws[i + 1] <= 0))
        {
            const f = (0 - ws[i]) / (ws[i + 1] - ws[i]);
            const h = abs(hs[i] + f * (hs[i + 1] - hs[i]));
            if (h < best)
            {
                best = h;
                result = chain.arcs[i] + (chain.arcs[i + 1] - chain.arcs[i]) * f;
            }
        }
    }
    if (result != undefined)
    {
        return result;
    }
    return (ws[0] > 0) ? -ws[0] : chain.arcs[n - 1] - ws[n - 1];
}

/** Whether two (w, h) points are within UNDRAPE_KERNEL_JOIN: kernel section pieces meeting there join. */
export function undrapeJoins(p is array, q is array) returns boolean
{
    return (p[0] - q[0]) * (p[0] - q[0]) + (p[1] - q[1]) * (p[1] - q[1]) < UNDRAPE_KERNEL_JOIN * UNDRAPE_KERNEL_JOIN;
}

/**
 * Kernel section pieces ([ws, hs, arc along]) of a steep section chained by their end points instead of by
 * width: from the free end of least width, each next piece is the one starting where the last ended
 * (within UNDRAPE_KERNEL_JOIN). A piece returned twice (a plane along a face end) is used once; pieces
 * left over (a slot) start a new run from their own free end, bridged by the chord.
 * @returns {map} : { "ws", "hs", "arcs", "steep" : true }
 */
export function undrapeChainByEnds(pieces is array) returns map
{
    const n = size(pieces);
    var ends = makeArray(n);
    for (var i = 0; i < n; i += 1)
    {
        const last = size(pieces[i][0]) - 1;
        ends[i] = [[pieces[i][0][0], pieces[i][1][0]], [pieces[i][0][last], pieces[i][1][last]]];
    }
    var used = makeArray(n, false);
    for (var i = 0; i < n; i += 1)
    {
        for (var j = i + 1; j < n; j += 1)
        {
            if (!used[j] && ((undrapeJoins(ends[i][0], ends[j][0]) && undrapeJoins(ends[i][1], ends[j][1]))
                        || (undrapeJoins(ends[i][0], ends[j][1]) && undrapeJoins(ends[i][1], ends[j][0]))))
            {
                used[j] = true;
            }
        }
    }

    var ws = [];
    var hs = [];
    var arcs = [];
    var arc = 0;
    while (true)
    {
        // the free end (touching no other unused piece) of least width; any end if the rest is closed
        var start = undefined;
        var fallback = undefined;
        for (var i = 0; i < n; i += 1)
        {
            if (used[i])
            {
                continue;
            }
            for (var e = 0; e < 2; e += 1)
            {
                var free = true;
                for (var j = 0; j < n; j += 1)
                {
                    if (j != i && !used[j] && (undrapeJoins(ends[i][e], ends[j][0]) || undrapeJoins(ends[i][e], ends[j][1])))
                    {
                        free = false;
                    }
                }
                if (free && (start == undefined || ends[i][e][0] < ends[start[0]][start[1]][0]))
                {
                    start = [i, e];
                }
                if (fallback == undefined || ends[i][e][0] < ends[fallback[0]][fallback[1]][0])
                {
                    fallback = [i, e];
                }
            }
        }
        if (start == undefined)
        {
            start = fallback;
        }
        if (start == undefined)
        {
            break;
        }
        var i = start[0];
        var e = start[1];
        while (true)
        {
            used[i] = true;
            const piece = pieces[i];
            const m = size(piece[0]);
            const total = piece[2][m - 1];
            for (var k = 0; k < m; k += 1)
            {
                const j = (e == 0) ? k : m - 1 - k;
                const along = (e == 0) ? piece[2][j] : total - piece[2][j];
                if (size(ws) > 0 && k == 0)
                {
                    // the joint (a repeated point) or a gap bridged by the chord
                    const dw = piece[0][j] - ws[size(ws) - 1];
                    const dh = piece[1][j] - hs[size(hs) - 1];
                    arc += sqrt(dw * dw + dh * dh);
                    if (dw * dw + dh * dh < UNDRAPE_KERNEL_JOIN * UNDRAPE_KERNEL_JOIN)
                    {
                        continue;
                    }
                }
                else if (k > 0)
                {
                    arc += along - ((e == 0) ? piece[2][j - 1] : total - piece[2][j + 1]);
                }
                ws = append(ws, piece[0][j]);
                hs = append(hs, piece[1][j]);
                arcs = append(arcs, arc);
            }
            const tail = ends[i][1 - e];
            var next = undefined;
            for (var q = 0; q < n && next == undefined; q += 1)
            {
                if (!used[q])
                {
                    if (undrapeJoins(tail, ends[q][0]))
                    {
                        next = [q, 0];
                    }
                    else if (undrapeJoins(tail, ends[q][1]))
                    {
                        next = [q, 1];
                    }
                }
            }
            if (next == undefined)
            {
                break;
            }
            i = next[0];
            e = next[1];
        }
    }
    return { "ws" : ws, "hs" : hs, "arcs" : arcs, "steep" : true };
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
    var made = false;
    try silent
    {
        opPlane(context, kid + "plane", { "plane" : plane(vector(fr.a[0], fr.a[1], fr.a[2]) * meter, vector(fr.t[0], fr.t[1], fr.t[2]),
                            vector(fr.w[0], fr.w[1], fr.w[2])), "width" : size2 * meter, "height" : size2 * meter });
        made = true;
    }
    if (!made)
    {
        return { "ok" : false, "why" : "kernel refused the section plane" };
    }
    const chainA = undrapeKernelChain(context, kid + "ixA", kid + "plane", kernel.facesA, kernel.boxesA, fr);
    if (chainA.refused == true)
    {
        return { "ok" : false, "why" : "kernel refused the section" };
    }
    const chainB = undrapeKernelChain(context, kid + "ixB", kid + "plane", kernel.facesB, kernel.boxesB, fr);
    if (chainB.refused == true)
    {
        return { "ok" : false, "why" : "kernel refused the section" };
    }
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
 * returned: edge crossings are read on it (their x is the shifted station's), but a "point" request keeps
 * its own station's x (the shifted frame only finds the section; up to 5e-5 m of x error otherwise).
 *
 * @returns {map} : { "ok", "why", "section", "frame", "attempts" }
 */
export function undrapeRefusedSection(context is Context, id is Id, kernel is map, c is map, fr is map, reach is number, tag is string) returns map
{
    var why = "";
    for (var r = 0; r < size(UNDRAPE_KERNEL_RETRIES); r += 1)
    {
        const shifted = (r == 0) ? fr : undrapeFrame(c, fr.arc + UNDRAPE_KERNEL_RETRIES[r]);
        // the kernel ops inside are guarded there (try silent around opPlane / opIntersectFaces only)
        const section = undrapeKernelSection(context, id + (tag ~ "_" ~ r), kernel, shifted, reach);
        if (section.ok)
        {
            return { "ok" : true, "section" : section, "frame" : shifted, "attempts" : r + 1 };
        }
        why = section.why;
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
 *      [startVertex, endVertex], "vertices" : array of [x, y, z], "open" : number of chains that did not close,
 *      "openAt" : array of [x, y, z], the vertex where each open chain stopped }
 */
export function undrapeRimLoops(tables is array, rim is array) returns map
{
    var vertices = [];
    var incident = [];
    var vertexOf = {};
    var byKey = {};
    for (var r in rim)
    {
        const tb = tables[r];
        const last = tb.count - 1;
        var ends = [];
        for (var end = 0; end < 2; end += 1)
        {
            const i = (end == 0) ? 0 : last;
            const p = [tb.px[i], tb.py[i], tb.pz[i]];
            // topological vertex first (tolerant models leave edge ends apart); whenever that lookup misses
            // (no key, or a key not met yet: the model has two vertices where the outline has one), the
            // nearest vertex within UNDRAPE_VERTEX_MATCH that is not already met by two rim edges
            const key = (tb.vertexKeys == undefined) ? undefined : tb.vertexKeys[end];
            var found = (key == undefined || byKey[key] == undefined) ? -1 : byKey[key];
            if (found < 0)
            {
                var nearest = UNDRAPE_VERTEX_MATCH * UNDRAPE_VERTEX_MATCH;
                for (var v = 0; v < size(vertices); v += 1)
                {
                    const dx = vertices[v][0] - p[0];
                    const dy = vertices[v][1] - p[1];
                    const dz = vertices[v][2] - p[2];
                    const d2 = dx * dx + dy * dy + dz * dz;
                    if (d2 < nearest && size(incident[v]) < 2)
                    {
                        nearest = d2;
                        found = v;
                    }
                }
            }
            if (found < 0)
            {
                found = size(vertices);
                vertices = append(vertices, p);
                incident = append(incident, []);
            }
            if (key != undefined && byKey[key] == undefined)
            {
                byKey[key] = found;
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
    var openAt = [];
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
                openAt = append(openAt, vertices[current]);
                break;
            }
            used[next] = true;
            const reversed = vertexOf[next][1] == current;
            loop = append(loop, [next, reversed]);
            current = reversed ? vertexOf[next][0] : vertexOf[next][1];
        }
        loops = append(loops, loop);
    }
    return { "loops" : loops, "vertexOf" : vertexOf, "vertices" : vertices, "open" : open, "openAt" : openAt };
}

// ============================================================================
// Undrape
// ============================================================================

/**
 * Undrape the outline of a constant-thickness plate onto the target.
 *
 * The outline is sampled adaptively (see UNDRAPE_TOLERANCE): seeds from each rim edge's structure, then passes
 * that map the midpoint of every unsettled span through the same station machinery (one batch of stations per
 * pass) and split the spans whose midpoint misses the cubic predicted from their neighbours by more than
 * options.tolerance. Vertices are exact and shared.
 *
 * @param chart : from unwrapChart(context, W, alignPoint, d).
 * @param side0, side1 {Query} : the plate's two side face sets (side0 outward normals point away from side1).
 * @param thickness {ValueWithUnits}
 * @param options {{
 *      @field tolerance {ValueWithUnits} : how far the cubic through the outline samples may miss the true outline
 *          at a span's midpoint (default UNDRAPE_TOLERANCE, 0.00125 mm: a quarter of a 0.005 mm fit tolerance).
 *          The side edge tables follow their edges to UNDRAPE_TABLE_TOL of it.
 *      @field spacing {ValueWithUnits} : the largest gap between outline samples (default UNDRAPE_MAX_GAP, 50 mm): a
 *          safety cap only, the tolerance sets the density.
 *      @field deformation {boolean} : compute the deformation report (stretch / shear along the rim and bend
 *          lines); false leaves stretchMin / stretchMax / shearMax at 0.
 * }}
 * @returns {map} : {
 *   "edges" : array, one per outline (rim) edge of the chosen side, each {
 *        "loop" : number (which closed loop), "index" : position in that loop (loops ordered, edges in order around the loop),
 *        "points" : array of [x, y] plain metres in the flat chart frame -- same convention as unwrapFast: x = length along
 *                   the target minus the alignment's, y = chart.alignV - (signed mid-surface arc from the centreline),
 *        "startTangent" : [tx, ty], "endTangent" : [tx, ty] unit, the way the points run,
 *        "arcs" : the chart arc (metres) of each point's station (extra, for checks) },
 *     consecutive edges of a loop share their end point EXACTLY (each shared vertex is computed once).
 *     Loop 0 is the outer loop (largest area) and runs counter-clockwise in (x, y); the others clockwise.
 *   "report" : { "stations" (all passes), "samples" (outline points), "passes", "tableSamples" (side edge samples),
 *                "unsettled" (spans still missing the tolerance where refinement stopped: UNDRAPE_MIN_SPAN or the pass
 *                limit), "fallbacks", "stretchMin", "stretchMax", "stretchWhere" ([x, y] metres), "shearMax" (radians),
 *                "rim3d", "rimFlat" (ValueWithUnits), "failed" (stations that could not be measured), "failedArcs" (their
 *                chart arcs, mm), "droppedPoints" (outline samples left out because their station failed),
 *                "shifted" (refused stations the kernel sectioned only at a shifted arc) },
 *   Throws when a rim loop does not close, or when a station through an outline vertex cannot be measured.
 *   "lines" : array of strings for a debug print }
 */
export function undrapeOutline(context is Context, id is Id, chart is map, side0 is Query, side1 is Query,
    thickness is ValueWithUnits, options is map) returns map
{
    const c = chart.packed;
    const tk = thickness.value;
    const tol = (options.tolerance == undefined) ? UNDRAPE_TOLERANCE : max(options.tolerance.value, 1e-9);
    const cap = (options.spacing == undefined) ? UNDRAPE_MAX_GAP : max(options.spacing.value, 5e-4);
    const withDeformation = options.deformation != false;

    // 1. Side: the one with fewer edges (either works); the other is only read by the kernel fallback.
    // Unless the two sides' areas disagree by more than 1 %: then one of them was found incomplete (its faces
    // are not all tangent-connected -- seen on a 0.44 mm laminate, 18380 of 221152 mm^2) and only the larger
    // one carries the whole outline.
    const count0 = size(evaluateQuery(context, qAdjacent(side0, AdjacencyType.EDGE, EntityType.EDGE)));
    const count1 = size(evaluateQuery(context, qAdjacent(side1, AdjacencyType.EDGE, EntityType.EDGE)));
    // Areas from the caller when it has them (evArea on a draped side is ~1.5 s): options.sideAreas = [side0, side1].
    const area0 = (options.sideAreas != undefined) ? options.sideAreas[0] : evArea(context, { "entities" : side0 });
    const area1 = (options.sideAreas != undefined) ? options.sideAreas[1] : evArea(context, { "entities" : side1 });
    const useFirst = (abs(area0 - area1) > 0.01 * max(area0, area1)) ? (area0 > area1) : (count0 <= count1);
    const sideA = useFirst ? side0 : side1;
    const sideB = useFirst ? side1 : side0;

    // 2-3. Edge sampling and tables.
    const sampled = undrapeSampleSide(context, sideA, c, UNDRAPE_TABLE_TOL * tol);
    const tables = sampled.tables;
    const sideSign = undrapeSideSign(c, tables);

    // 4. Rim loops and seed requests.
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
    if (loopData.open > 0)
    {
        const q = loopData.openAt[0];
        throw regenError("Undrape: the plate's outline does not close: " ~ loopData.open ~ " rim chain(s) end open, the first at ("
                ~ roundToPrecision(q[0] * 1000, 3) ~ ", " ~ roundToPrecision(q[1] * 1000, 3) ~ ", " ~ roundToPrecision(q[2] * 1000, 3)
                ~ ") mm.");
    }
    const seeds = undrapeSeeds(c, tables, rim, loopData, cap, undrapeChartBreaks(c));
    var requests = seeds.requests;
    var eps = seeds.edges;
    var shareArcs = seeds.shareArcs;

    // 5-8. Passes: stations for the new requests, then each edge's checks and its next midpoints.
    const env = { "chart" : chart, "tables" : tables, "tk" : tk, "sideSign" : sideSign, "sideA" : sideA, "sideB" : sideB };
    var results = [];
    var records = [];
    var lost = [];
    var kernel = undefined;
    var fallbacks = 0;
    var fallbackArcs = [];
    var failed = [];
    var shifted = 0;
    var candidateTotal = 0;
    var lines = [];
    var passes = 0;
    var unsettled = 0;
    for (var pass = 0; pass <= UNDRAPE_REFINE_PASSES; pass += 1)
    {
        const shared = undrapeArcRequests(shareArcs, eps, requests);
        requests = shared.requests;
        eps = shared.edges;
        if (size(requests) == size(results))
        {
            break;
        }
        const batch = undrapeResolveBatch(context, id + ("pass" ~ pass), env, requests, size(results), kernel);
        results = concatenateArrays([results, batch.results]);
        records = concatenateArrays([records, batch.records]);
        lost = concatenateArrays([lost, batch.lost]);
        kernel = batch.kernel;
        fallbacks += batch.fallbacks;
        fallbackArcs = concatenateArrays([fallbackArcs, batch.fallbackArcs]);
        failed = concatenateArrays([failed, batch.failed]);
        shifted += batch.shifted;
        candidateTotal += batch.candidateTotal;
        lines = concatenateArrays([lines, batch.lines]);
        passes += 1;

        shareArcs = [];
        unsettled = 0;
        for (var r in rim)
        {
            const refined = undrapeRefine(r, eps[r], c, tables[r], requests, results, tol, pass == 0,
                pass < UNDRAPE_REFINE_PASSES);
            eps[r] = refined.edge;
            requests = concatenateArrays([requests, refined.requests]);
            shareArcs = concatenateArrays([shareArcs, refined.arcs]);
            unsettled += refined.unsettled;
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

    // Each edge's samples in order along it, vertices included.
    var byTable = {};
    var edgeRequests = [];
    var sampleCount = 0;
    for (var r in rim)
    {
        const ep = eps[r];
        var points = makeArray(size(ep.samples) + 2);
        points[0] = ep.v0;
        for (var i = 0; i < size(ep.samples); i += 1)
        {
            points[i + 1] = ep.samples[i][1];
        }
        points[size(points) - 1] = ep.v1;
        const er = { "points" : points, "startTangent" : ep.tanStart, "endTangent" : ep.tanEnd, "lengthwise" : ep.lengthwise };
        byTable[r] = er;
        edgeRequests = append(edgeRequests, er);
        sampleCount += size(points);
    }
    const plan = { "requests" : requests, "edgeRequests" : edgeRequests, "byTable" : byTable };

    // Outline samples lost with their station (a vertex's throws in undrapeAssemble).
    var isLost = {};
    for (var k in lost)
    {
        isLost[k] = true;
    }
    var dropped = 0;
    for (var er in edgeRequests)
    {
        for (var i = 1; i + 1 < size(er.points); i += 1)
        {
            if (isLost[er.points[i]] == true)
            {
                dropped += 1;
            }
        }
    }
    if (size(failed) > 0)
    {
        lines = append(lines, "undrape: " ~ size(failed) ~ " station(s) could not be measured, " ~ dropped
                ~ " outline sample(s) dropped");
    }

    // Edges and loops.
    const assembled = undrapeAssemble(plan, results, loopData);

    // 9. Deformation report, over every station of every pass in arc order.
    const nSt = size(records);
    var deform = { "stretchMin" : 0, "stretchMax" : 0, "where" : [0, 0], "shearMax" : 0 };
    if (withDeformation)
    {
        const sorted = sort(records, function(p, q) { return p[0] - q[0]; });
        var sections = makeArray(nSt);
        var crossingsAt = makeArray(nSt);
        var frames = makeArray(nSt);
        for (var s = 0; s < nSt; s += 1)
        {
            frames[s] = sorted[s][1];
            sections[s] = sorted[s][2];
            crossingsAt[s] = sorted[s][3];
        }
        deform = undrapeDeformation(sections, crossingsAt, frames, chart, tk);
    }
    var rim3d = 0;
    var rimFlat = 0;
    for (var er in edgeRequests)
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
                    "undrape: side " ~ (useFirst ? 0 : 1) ~ " (" ~ size(tables) ~ " edges, " ~ sampled.samples ~ " table samples, "
                        ~ size(rim) ~ " rim edges, " ~ sampled.faceCount ~ " faces), sideSign " ~ sideSign ~ ", t "
                        ~ roundToPrecision(tk * 1000, 4) ~ " mm",
                    "undrape: " ~ size(assembled.edges) ~ " outline edges in " ~ assembled.loopCount ~ " loop(s)"
                        ~ (loopData.open > 0 ? " (" ~ loopData.open ~ " did not close)" : ""),
                    "undrape: " ~ nSt ~ " stations in " ~ passes ~ " passes, " ~ sampleCount ~ " outline samples (tolerance "
                        ~ roundToPrecision(tol * 1000, 5) ~ " mm, max gap " ~ roundToPrecision(cap * 1000, 1) ~ " mm"
                        ~ (unsettled > 0 ? ", " ~ unsettled ~ " span(s) left unsettled" : "") ~ "), "
                        ~ roundToPrecision(candidateTotal / max(nSt, 1), 1) ~ " candidate edges per station, " ~ fallbacks
                        ~ " kernel fallbacks" ~ (size(fallbackArcs) > 0 ? " at arc (mm) " ~ undrapeJoin(fallbackArcs) : "")
                        ~ (shifted > 0 ? " (" ~ shifted ~ " sectioned at a shifted arc)" : ""),
                    withDeformation ? ("undrape: stretch along rim and bend lines " ~ roundToPrecision(deform.stretchMin * 100, 3)
                            ~ " % .. " ~ roundToPrecision(deform.stretchMax * 100, 3) ~ " % (max at x "
                            ~ roundToPrecision(deform.where[0] * 1000, 1) ~ ", y " ~ roundToPrecision(deform.where[1] * 1000, 1)
                            ~ " mm), shear up to " ~ roundToPrecision(deform.shearMax * 180 / PI, 2) ~ " deg")
                        : "undrape: deformation report skipped",
                    "undrape: rim 3D " ~ roundToPrecision(rim3d * 1000, 3) ~ " mm, flat " ~ roundToPrecision(rimFlat * 1000, 3) ~ " mm"
                ], lines]);

    return {
        "edges" : assembled.edges,
        "report" : {
            "stations" : nSt,
            "samples" : sampleCount,
            "passes" : passes,
            "tableSamples" : sampled.samples,
            "unsettled" : unsettled,
            "fallbacks" : fallbacks,
            "failed" : size(failed),
            "failedArcs" : failed,
            "droppedPoints" : dropped,
            "shifted" : shifted,
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

/**
 * One batch of requests (requests[first ..]) measured at their stations: request arcs sorted and merged into
 * stations, candidate edges, crossings, sections (kernel fallback for refused stations), each request mapped.
 * @param env {map} : { chart, tables, tk, sideSign, sideA, sideB }
 * @param kernel : the kernel fallback's faces (undrapeKernelFaces) or undefined (read on the first refusal).
 * @returns {map} : { "results" (one per request of the batch, undrapeResolve, with a trailing 1 where the kernel
 *      fallback measured it; undefined where not measured),
 *      "records" (per station [arc, frame, section, crossings]), "lost" (request indices whose station failed),
 *      "kernel", "fallbacks", "fallbackArcs", "failed", "shifted", "candidateTotal", "lines" }
 */
export function undrapeResolveBatch(context is Context, id is Id, env is map, requests is array, first is number, kernel) returns map
{
    const chart = env.chart;
    const c = chart.packed;
    const tables = env.tables;
    const tk = env.tk;
    const nReq = size(requests) - first;
    const order = sort(range(0, nReq - 1), function(i, j) { return requests[first + i].arc - requests[first + j].arc; });
    var stationOf = makeArray(nReq);
    var arcs = [];
    for (var k in order)
    {
        const a = requests[first + k].arc;
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

    const candidates = undrapeCandidateEdges(tables, frames);
    var kernelFaces = kernel;
    var fallbacks = 0;
    var fallbackArcs = [];
    var failed = [];
    var shifted = 0;
    var lines = [];
    var lost = [];
    var records = makeArray(nSt);
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
        var section = undrapeSection(crossings, tk, env.sideSign);
        if (!section.ok)
        {
            if (kernelFaces == undefined)
            {
                kernelFaces = undrapeKernelFaces(context, env.sideA, env.sideB);
            }
            const refused = undrapeRefusedSection(context, id, kernelFaces, c, fr, undrapeReach(tables, candidates[s], fr),
                "kernel" ~ s);
            fallbacks += 1;
            fallbackArcs = append(fallbackArcs, roundToPrecision(fr.arc * 1000, 2) ~ " (" ~ section.why ~ ")");
            if (!refused.ok)
            {
                failed = append(failed, roundToPrecision(fr.arc * 1000, 3));
                lines = append(lines, "undrape: station at arc " ~ roundToPrecision(fr.arc * 1000, 3) ~ " mm could not be measured ("
                        ~ section.why ~ "; " ~ refused.why ~ ")");
                records[s] = [arcs[s], fr, { "ok" : false }, crossings];
                for (var k in atStation[s])
                {
                    lost = append(lost, first + k);
                }
                continue;
            }
            section = refused.section;
            if (refused.frame.arc != fr.arc)
            {
                shifted += 1;
                fr = refused.frame;
                crossings = [];
                for (var e in candidates[s])
                {
                    crossings = concatenateArrays([crossings, undrapeEdgeCrossings(tables[e], e, fr, -1)]);
                }
            }
        }
        records[s] = [arcs[s], fr, section, crossings];

        // Map the requests at this station. A "point" request keeps its own (unshifted) station's x and arc:
        // a shifted frame only finds the section.
        for (var k in atStation[s])
        {
            const request = requests[first + k];
            results[k] = undrapeResolve(request, section, crossings, fr, chart, tk);
            if (results[k] != undefined && section.kernel == true)
            {
                // measured by the kernel fallback (refinement is held to UNDRAPE_KERNEL_TOL there)
                results[k] = append(results[k], 1);
            }
            if (fr.arc != arcs[s] && request.kind == "point")
            {
                var q = results[k];
                q[0] = undrapeFrame(c, arcs[s]).x - chart.alignX;
                q[5] = arcs[s];
                results[k] = q;
            }
        }
    }
    return { "results" : results, "records" : records, "lost" : lost, "kernel" : kernelFaces, "fallbacks" : fallbacks,
            "fallbackArcs" : fallbackArcs, "failed" : failed, "shifted" : shifted, "candidateTotal" : candidateTotal,
            "lines" : lines };
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
        const a0 = chartSeedArc(c, tb.px[0]);
        const f = chartFoot(c, tb.px[0], tb.py[0], tb.pz[0], a0, chartSpanOf(c, a0));
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
 * The chart's curvature breaks: arcs of its zero-length joins (arcs[i] == arcs[i + 1]) where the curvature
 * jumps by more than UNDRAPE_BREAK_KAPPA. The flat outline's second derivative jumps there.
 */
export function undrapeChartBreaks(c is map) returns array
{
    var breaks = [];
    for (var i = 0; i + 1 < c.count; i += 1)
    {
        if (abs(c.arcs[i + 1] - c.arcs[i]) < 1e-12 && abs(c.kappa[i + 1] - c.kappa[i]) > UNDRAPE_BREAK_KAPPA)
        {
            breaks = append(breaks, c.arcs[i]);
        }
    }
    return breaks;
}

/** Arc length along an edge table at edge parameter p (linear between the table's samples). */
export function undrapeParamToS(tb is map, p is number) returns number
{
    const params = tb.params;
    var low = 0;
    var high = tb.count - 2;
    while (low < high)
    {
        const half = floor((low + high) / 2);
        if (p <= params[half + 1])
        {
            high = half;
        }
        else
        {
            low = half + 1;
        }
    }
    const w = params[low + 1] - params[low];
    const f = (w < 1e-15) ? 0 : clamp((p - params[low]) / w, 0, 1);
    return tb.cum[low] + f * (tb.cum[low + 1] - tb.cum[low]);
}

/**
 * Sorted interior positions in (0, extent) with every gap (the ends included) no wider than `cap`: `us` plus
 * evenly spaced fillers.
 */
export function undrapeFillGaps(us is array, extent is number, cap is number) returns array
{
    var inner = [];
    for (var u in sort(us, function(a, b) { return a - b; }))
    {
        if (u > 1e-9 && u < extent - 1e-9 && (size(inner) == 0 || u - inner[size(inner) - 1] > 1e-9))
        {
            inner = append(inner, u);
        }
    }
    var all = concatenateArrays([[0], inner, [extent]]);
    var result = [];
    for (var i = 0; i + 1 < size(all); i += 1)
    {
        if (i > 0)
        {
            result = append(result, all[i]);
        }
        const gap = all[i + 1] - all[i];
        const n = ceil(gap / cap - 1e-9);
        for (var k = 1; k < n; k += 1)
        {
            result = append(result, all[i] + gap * k / n);
        }
    }
    return result;
}

/**
 * The outline's seed requests, one station each, and each rim edge's sampling state.
 *
 * Every loop vertex is one request (computed once, shared by both its edges). A rim edge running along the
 * reference (|t . u| >= UNDRAPE_LENGTHWISE everywhere, arcs monotone) is sampled by station arc: its seeds (its
 * structural spans, undrapeSeedSpans, the chart's curvature breaks inside it, gaps capped at `cap`) become station
 * arcs (undrapeArcRequests; symmetric sides share them). Any other rim edge (ends,
 * notches, holes) is sampled by arc length s along its table, each sample at its own station ("point"
 * requests). Each edge end also gets three requests at one, two and three UNDRAPE_TANGENT_STEP inside it, for its
 * end tangent (undrapeEndTangent).
 *
 * @returns {map} : { "requests" : array of { "arc", "kind" ("edge" | "point"), "edge", "p" ([x, y, z, nx, ny, nz]) },
 *      "edges" : map table -> edge state (see undrapeRefine), "shareArcs" : array of [arc, table, -1] seed arcs of
 *      the lengthwise edges }
 */
export function undrapeSeeds(c is map, tables is array, rim is array, loopData is map, cap is number, breaks is array) returns map
{
    var requests = [];
    var vertexRequest = makeArray(size(loopData.vertices));
    for (var v = 0; v < size(loopData.vertices); v += 1)
    {
        const p = loopData.vertices[v];
        // the vertex's normal is read from the first rim edge ending there (below); position is exact
        vertexRequest[v] = size(requests);
        requests = append(requests, { "arc" : undrapeFootArc(c, p, undefined), "kind" : "point", "p" : [p[0], p[1], p[2], 0, 0, 1] });
    }

    var edges = {};
    var shareArcs = [];
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
            const a0 = (hint < 0) ? chartSeedArc(c, tb.px[i]) : sampleArcs[i - 1];
            const f = chartFoot(c, tb.px[i], tb.py[i], tb.pz[i], a0, (hint < 0) ? chartSpanOf(c, a0) : hint);
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

        // structural seeds as arc lengths along the table
        var structural = [];
        for (var j = 1; j < tb.seedSpans; j += 1)
        {
            structural = append(structural, undrapeParamToS(tb, j / tb.seedSpans));
        }

        var ep = { "lengthwise" : lengthwise, "v0" : vertexRequest[ends[0]], "v1" : vertexRequest[ends[1]], "samples" : [],
                "fresh" : [], "checks" : [], "checkReq" : [], "dir" : 1, "a0" : 0 };
        var tanStart = [];
        var tanEnd = [];
        if (lengthwise)
        {
            const s0 = requests[ep.v0].arc;
            const s1 = requests[ep.v1].arc;
            const dir = (s1 > s0) ? 1 : -1;
            const extent = abs(s1 - s0);
            var us = [];
            for (var s in structural)
            {
                us = append(us, dir * (undrapeFootArc(c, undrapeEdgePoint(tb, s), undefined) - s0));
            }
            for (var b in breaks)
            {
                // a break next to a vertex is that vertex
                const u = dir * (b - s0);
                if (u > UNDRAPE_END_CHECK && u < extent - UNDRAPE_END_CHECK)
                {
                    us = append(us, u);
                }
            }
            for (var u in undrapeFillGaps(us, extent, cap))
            {
                shareArcs = append(shareArcs, [s0 + dir * u, r, -1]);
            }
            const step = min(UNDRAPE_TANGENT_STEP, extent / 8);
            for (var k = 1; k <= 3; k += 1)
            {
                tanStart = append(tanStart, size(requests));
                requests = append(requests, { "arc" : s0 + dir * k * step, "kind" : "edge", "edge" : r });
                tanEnd = append(tanEnd, size(requests));
                requests = append(requests, { "arc" : s1 - dir * k * step, "kind" : "edge", "edge" : r });
            }
            ep.dir = dir;
            ep.a0 = s0;
            ep.extent = extent;
            ep.step = step;
        }
        else
        {
            const length = tb.length;
            var seed = sampleArcs[0];
            var fresh = [];
            for (var s in undrapeFillGaps(structural, length, cap))
            {
                const p = undrapeEdgePoint(tb, s);
                const a = undrapeFootArc(c, p, seed);
                seed = a;
                fresh = append(fresh, [s, size(requests)]);
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
            ep.extent = length;
            ep.step = step;
            ep.fresh = fresh;
        }
        ep.tanStart = concatenateArrays([[ep.v0], tanStart]);
        ep.tanEnd = concatenateArrays([[ep.v1], tanEnd]);
        edges[r] = ep;
    }
    return { "requests" : requests, "edges" : edges, "shareArcs" : shareArcs };
}

/**
 * "edge" requests of lengthwise rim edges at the station arcs they asked for (their seeds and their check points:
 * [arc, table, check index or -1]). Requests of different edges at one arc (the two long sides of a symmetric
 * part) share their station. The new samples wait in each edge's "fresh" list ([u, request]).
 */
export function undrapeArcRequests(arcs is array, edges is map, requests is array) returns map
{
    var result = requests;
    var out = edges;
    for (var entry in arcs)
    {
        const r = entry[1];
        var ep = out[r];
        ep.fresh = append(ep.fresh, [ep.dir * (entry[0] - ep.a0), size(result)]);
        if (entry[2] >= 0)
        {
            ep.checkReq[entry[2]] = size(result);
        }
        result = append(result, { "arc" : entry[0], "kind" : "edge", "edge" : r });
        out[r] = ep;
    }
    return { "requests" : result, "edges" : out };
}

/**
 * One rim edge after a pass: each check point against the curve a fit through the edge's samples before this
 * pass would draw (undrapeMiss); the midpoints join the samples, passed or not, end checks only when they failed;
 * then the next checks: every span after the seed pass, afterwards the spans inside a failed span and the span
 * either side of it (the new samples changed their curve too); spans narrower than 2 * UNDRAPE_MIN_SPAN
 * (2 * UNDRAPE_KERNEL_MIN_SPAN next to a kernel-measured station) are not checked. A span is checked at its
 * midpoint, an end span also UNDRAPE_END_CHECK from its vertex. A lengthwise edge asks for its check points as
 * shared arcs, any other edge as its own "point" requests (appended after `requests`).
 *
 * Edge state: { lengthwise, v0, v1 (vertex requests), tanStart, tanEnd ([vertex, 1, 2, 3 steps] requests), step,
 *      extent (u at the far end), dir, a0 (lengthwise: u = dir * (arc - a0); else u = arc length along the table),
 *      samples ([u, request], ascending), fresh (this pass's, not yet joined), checks (this pass's: [u0, u1, request0,
 *      request1, u of the check point, 1 for an end check]), checkReq (the check points' requests) }
 * @returns {map} : { "edge", "requests" (new point requests), "arcs" (new shared arcs), "unsettled" (failed spans
 *      that get no further check) }
 */
export function undrapeRefine(r is number, edge is map, c is map, tb is map, requests is array, results is array, tol is number,
    firstPass is boolean, more is boolean) returns map
{
    var ep = edge;
    if (!firstPass && size(ep.checks) == 0 && size(ep.fresh) == 0)
    {
        // settled
        return { "edge" : ep, "requests" : [], "arcs" : [], "unsettled" : 0 };
    }
    var failedSpans = [];
    var drop = {};
    if (size(ep.checks) > 0)
    {
        const known = undrapeKnown(ep, results);
        for (var k = 0; k < size(ep.checks); k += 1)
        {
            const idx = ep.checkReq[k];
            const q = (idx == undefined) ? undefined : results[idx];
            if (q == undefined)
            {
                continue;
            }
            const span = ep.checks[k];
            const miss = undrapeMiss(known, span[4], q);
            // a span touching a kernel-measured station is held to that measurement's own accuracy
            var limit = tol;
            for (var idx2 in [idx, span[2], span[3]])
            {
                const r2 = results[idx2];
                if (r2 != undefined && size(r2) > 6)
                {
                    limit = max(tol, UNDRAPE_KERNEL_TOL);
                }
            }
            if (miss <= limit && span[5] == 1)
            {
                // a passed end check leaves: a sample a millimetre from the vertex would bend the fit where
                // nothing checked it (a passed midpoint stays: halving a span only tightens its curve)
                drop[idx] = true;
            }
            const last = size(failedSpans) - 1;
            if (miss > limit && (last < 0 || failedSpans[last][0] != span[0] || failedSpans[last][1] != span[1]))
            {
                failedSpans = append(failedSpans, span);
            }
        }
    }
    var fresh = [];
    for (var entry in ep.fresh)
    {
        if (drop[entry[1]] != true)
        {
            fresh = append(fresh, entry);
        }
    }
    ep.samples = sort(concatenateArrays([ep.samples, fresh]), function(p, q) { return p[0] - q[0]; });
    ep.fresh = [];
    ep.checks = [];
    ep.checkReq = [];

    var newRequests = [];
    var arcs = [];
    var unsettled = 0;
    var us = makeArray(size(ep.samples) + 2);
    us[0] = 0;
    for (var i = 0; i < size(ep.samples); i += 1)
    {
        us[i + 1] = ep.samples[i][0];
    }
    us[size(us) - 1] = ep.extent;
    var reqs = concatenateArrays([[ep.v0], mapArray(ep.samples, function(x) { return x[1]; }), [ep.v1]]);
    // spans to check: all after the seed pass; afterwards every span inside a failed span and the span either side
    // of it (the new samples changed their curve too)
    var dirty = makeArray(size(us) - 1, firstPass);
    for (var span in failedSpans)
    {
        for (var i = 0; i + 1 < size(us); i += 1)
        {
            if (us[i + 1] >= span[0] - 1e-12 && us[i] <= span[1] + 1e-12)
            {
                dirty[i] = true;
            }
        }
    }
    for (var i = 0; i + 1 < size(us); i += 1)
    {
        if (!dirty[i] || !more)
        {
            continue;
        }
        // near kernel-measured stations spans stop at UNDRAPE_KERNEL_MIN_SPAN
        const r0 = results[reqs[i]];
        const r1 = results[reqs[i + 1]];
        const kernelSpan = (r0 != undefined && size(r0) > 6) || (r1 != undefined && size(r1) > 6);
        if (us[i + 1] - us[i] < 2 * (kernelSpan ? UNDRAPE_KERNEL_MIN_SPAN : UNDRAPE_MIN_SPAN))
        {
            continue;
        }
        // the midpoint; an edge's first and last spans also UNDRAPE_END_CHECK from the vertex
        var at = [0.5 * (us[i] + us[i + 1])];
        if (us[i + 1] - us[i] > 4 * UNDRAPE_END_CHECK)
        {
            if (i == 0)
            {
                at = append(at, us[0] + UNDRAPE_END_CHECK);
            }
            if (i + 2 == size(us))
            {
                at = append(at, us[i + 1] - UNDRAPE_END_CHECK);
            }
        }
        for (var j = 0; j < size(at); j += 1)
        {
            const m = at[j];
            ep.checks = append(ep.checks, [us[i], us[i + 1], reqs[i], reqs[i + 1], m, (j > 0) ? 1 : 0]);
            if (ep.lengthwise)
            {
                ep.checkReq = append(ep.checkReq, undefined);
                arcs = append(arcs, [ep.a0 + ep.dir * m, r, size(ep.checks) - 1]);
            }
            else
            {
                const p = undrapeEdgePoint(tb, m);
                const near = results[reqs[i]];
                const a = undrapeFootArc(c, p, (near == undefined) ? undefined : near[5]);
                ep.checkReq = append(ep.checkReq, size(requests) + size(newRequests));
                ep.fresh = append(ep.fresh, [m, size(requests) + size(newRequests)]);
                newRequests = append(newRequests, { "arc" : a, "kind" : "point", "p" : p });
            }
        }
    }
    // failed spans that got no check (too narrow, or the last pass)
    for (var span in failedSpans)
    {
        var checked = false;
        for (var ck in ep.checks)
        {
            if (ck[0] >= span[0] - 1e-12 && ck[1] <= span[1] + 1e-12)
            {
                checked = true;
                break;
            }
        }
        if (!checked)
        {
            unsettled += 1;
        }
    }
    return { "edge" : ep, "requests" : newRequests, "arcs" : arcs, "unsettled" : unsettled };
}

/**
 * What a fit of the edge sees, for the checks: its output samples in order along it (vertices included; unmeasured
 * ones and repeats left out) as { "us" (edge coordinate u), "ts" (cumulative chord in the flat), "xs", "ys" (plain
 * metres) }, and the unit flat end tangents "tangent0", "tangent1" (undrapeEndTangent, from the points one, two and
 * three UNDRAPE_TANGENT_STEP inside each end, along the edge; undefined where not measured).
 */
export function undrapeKnown(ep is map, results is array) returns map
{
    const entries = concatenateArrays([[[0, ep.v0]], ep.samples, [[ep.extent, ep.v1]]]);
    var us = [];
    var ts = [];
    var xs = [];
    var ys = [];
    for (var entry in entries)
    {
        const q = results[entry[1]];
        if (q == undefined || (size(us) > 0 && entry[0] - us[size(us) - 1] < 1e-12))
        {
            continue;
        }
        const n = size(us);
        const t = (n == 0) ? 0 : ts[n - 1] + sqrt((q[0] - xs[n - 1]) * (q[0] - xs[n - 1]) + (q[1] - ys[n - 1]) * (q[1] - ys[n - 1]));
        if (n > 0 && t - ts[n - 1] < 1e-12)
        {
            continue;
        }
        us = append(us, entry[0]);
        ts = append(ts, t);
        xs = append(xs, q[0]);
        ys = append(ys, q[1]);
    }
    return { "us" : us, "ts" : ts, "xs" : xs, "ys" : ys, "tangent0" : undrapeEndTangent(results, ep.tanStart, false),
            "tangent1" : undrapeEndTangent(results, ep.tanEnd, true) };
}

/**
 * How far the flat point q ([x, y, ...]) of the check point at u misses the curve a fit through the known samples
 * would draw there: the cubic Hermite on chord length (slopes from the quadratic through each sample and its
 * neighbours, Catmull-Rom on uneven spacing; the unit end tangents at the edge's ends), nearest point on the span
 * bracketing u (Newton from u's fraction of it), or on a neighbouring span when the nearest point is an end of it.
 * Metres; 0 when fewer than two samples are known.
 */
export function undrapeMiss(known is map, u is number, q is array) returns number
{
    const us = known.us;
    const n = size(us);
    if (n < 2)
    {
        return 0;
    }
    var low = 0;
    var high = n - 2;
    while (low < high)
    {
        const half = floor((low + high) / 2);
        if (u <= us[half + 1])
        {
            high = half;
        }
        else
        {
            low = half + 1;
        }
    }
    const f0 = clamp((u - us[low]) / (us[low + 1] - us[low]), 0, 1);
    const hit = undrapeSpanDistance(known, low, q, f0);
    var best = hit[0];
    if (hit[1] <= 0 && low > 0)
    {
        best = min(best, undrapeSpanDistance(known, low - 1, q, 1)[0]);
    }
    if (hit[1] >= 1 && low + 2 < n)
    {
        best = min(best, undrapeSpanDistance(known, low + 1, q, 0)[0]);
    }
    return best;
}

/**
 * Distance from the flat point q to the fit's cubic span i of `known` (see undrapeMiss): Newton on the nearest point
 * from fraction f0. @returns {array} : [distance, fraction of the nearest point]
 */
export function undrapeSpanDistance(known is map, i is number, q is array, f0 is number) returns array
{
    const n = size(known.ts);
    const h = known.ts[i + 1] - known.ts[i];
    const d0 = (i == 0 && known.tangent0 != undefined) ? known.tangent0 : undrapeSlope(known, i, 1);
    const d1 = (i + 2 == n && known.tangent1 != undefined) ? known.tangent1 : undrapeSlope(known, i + 1, -1);
    const x0 = known.xs[i];
    const y0 = known.ys[i];
    const x1 = known.xs[i + 1];
    const y1 = known.ys[i + 1];
    var f = f0;
    var e = undrapeHermite2(x0, y0, x1, y1, d0, d1, h, f);
    for (var step = 0; step < 4; step += 1)
    {
        const tt = e[2] * e[2] + e[3] * e[3];
        if (tt < 1e-30)
        {
            break;
        }
        const df = ((e[0] - q[0]) * e[2] + (e[1] - q[1]) * e[3]) / tt;
        f = clamp(f - df, 0, 1);
        e = undrapeHermite2(x0, y0, x1, y1, d0, d1, h, f);
        if (abs(df) < 1e-9)
        {
            break;
        }
    }
    return [sqrt((e[0] - q[0]) * (e[0] - q[0]) + (e[1] - q[1]) * (e[1] - q[1])), f];
}

/** The cubic Hermite from (x0, y0) to (x1, y1), slopes d0, d1 per unit of a parameter spanning h, at fraction f: [x, y, dx/df, dy/df]. */
export function undrapeHermite2(x0 is number, y0 is number, x1 is number, y1 is number, d0 is array, d1 is array, h is number, f is number) returns array
{
    const f2 = f * f;
    const f3 = f2 * f;
    const b0 = 2 * f3 - 3 * f2 + 1;
    const b1 = (f3 - 2 * f2 + f) * h;
    const b2 = -2 * f3 + 3 * f2;
    const b3 = (f3 - f2) * h;
    const g0 = 6 * f2 - 6 * f;
    const g1 = (3 * f2 - 4 * f + 1) * h;
    const g3 = (3 * f2 - 2 * f) * h;
    return [b0 * x0 + b1 * d0[0] + b2 * x1 + b3 * d1[0], b0 * y0 + b1 * d0[1] + b2 * y1 + b3 * d1[1],
            g0 * (x0 - x1) + g1 * d0[0] + g3 * d1[0], g0 * (y0 - y1) + g1 * d0[1] + g3 * d1[1]];
}

/**
 * Slope [dx/dt, dy/dt] (t = chord length) at known sample i: the derivative of the quadratic through it and its two
 * neighbours, or through it and the next two towards `side` (+1 / -1) at an end; a chord with only two samples.
 */
export function undrapeSlope(known is map, i is number, side is number) returns array
{
    const ts = known.ts;
    const n = size(ts);
    var j = -1;
    var k = -1;
    if (i > 0 && i + 1 < n)
    {
        j = i - 1;
        k = i + 1;
    }
    else if (i + 2 * side >= 0 && i + 2 * side < n)
    {
        j = i + side;
        k = i + 2 * side;
    }
    else if (i - 2 * side >= 0 && i - 2 * side < n)
    {
        j = i - side;
        k = i - 2 * side;
    }
    if (j < 0)
    {
        const o = (i + side >= 0 && i + side < n) ? i + side : i - side;
        const dt = ts[o] - ts[i];
        return [(known.xs[o] - known.xs[i]) / dt, (known.ys[o] - known.ys[i]) / dt];
    }
    const t0 = ts[i];
    const t1 = ts[j];
    const t2 = ts[k];
    const w0 = (2 * t0 - t1 - t2) / ((t0 - t1) * (t0 - t2));
    const w1 = (t0 - t2) / ((t1 - t0) * (t1 - t2));
    const w2 = (t0 - t1) / ((t2 - t0) * (t2 - t1));
    return [w0 * known.xs[i] + w1 * known.xs[j] + w2 * known.xs[k], w0 * known.ys[i] + w1 * known.ys[j] + w2 * known.ys[k]];
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
                if (!section.kernel && section.nodeOf[i] >= 0)
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
                        const p = plan.requests[er.points[i]].p;
                        throw regenError("Undrape: the section through the outline vertex at (" ~ roundToPrecision(p[0] * 1000, 3)
                                ~ ", " ~ roundToPrecision(p[1] * 1000, 3) ~ ", " ~ roundToPrecision(p[2] * 1000, 3)
                                ~ ") mm could not be measured.");
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
            const node = section.kernel ? -1 : section.nodeOf[i];
            const mid = (node < 0) ? undrapeMidAt(section, cr[0], cr[1]) : section.mid[node];
            // the section's tangent along increasing arc (a face-ordered section orients its nodes' own)
            const tw = (node >= 0 && section.byFaces) ? section.pts[node][4] : cr[4];
            const th = (node >= 0 && section.byFaces) ? section.pts[node][5] : cr[5];
            current[count] = [cr[9], fr.x - chart.alignX, chart.alignV - mid,
                    cr[14] - 0.5 * tk * cr[10], cr[15] - 0.5 * tk * cr[11], cr[16] - 0.5 * tk * cr[12],
                    tw * fr.w[0] + th * fr.h[0], tw * fr.w[1] + th * fr.h[1], tw * fr.w[2] + th * fr.h[2]];
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
