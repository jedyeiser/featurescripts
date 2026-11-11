FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
import(path : "onshape/std/approximationUtils.fs", version : "3083.0");

// The core the map functions come from (the same version deform.fs pins: one core version in scope).
import(path : "683d867c35fdab9c98d47556", version : "1d8139232bc3475c93706cac");

/**
 * canonical_edges: the functions that make the faces of a deformed body meet on EXACTLY the same edges.
 *
 * Every source edge has ONE shared curve: its wrapped edge (the exact image under the map, fitted to Edge accuracy / 2, end points at the merged vertices); every
 * mapped face is then pinned onto the shared curves of all its edges, so two faces that share an edge reproduce the same curve and weld. A sliver face is
 * consumed: its two long edges share the midline between them. Used by deform.fs (mappedSurfaceFace and the face loop).
 */

/**
 * Mapped vertices closer than this are one vertex: edges ending at them then share exact end points, and an edge whose two ends
 * merge is not rebuilt at all (its faces close their loops through the merged vertex). 10 microns: the old 5 micron value let a
 * 5.05 micron edge through to opCreateBSplineCurve, which refused it (BAD_GEOMETRY), and every face that had the edge then lost
 * a boundary curve and was not rebuilt. NOT wider: at 20 microns the 15.24 micron edges of cd_surf (which rebuilt fine) were
 * collapsed too and kt_bs_deform broke (EXTRACT_WIRES_OVERLAPPING_EDGES).
 */
export const VERTEX_MERGE_TOLERANCE = 1e-5 * meter;

/** The non-zero basis functions of a clamped B-spline at u: { "first" : index of the first, "values" : degree + 1 values }. */
export function basisNonzero(knots is array, degree is number, n is number, u is number) returns map
{
    var span = degree;
    while (span < n - 1 && u >= knots[span + 1])
        span += 1;
    var left = makeArray(degree + 1, 0);
    var right = makeArray(degree + 1, 0);
    var basis = makeArray(degree + 1, 0);
    basis[0] = 1;
    for (var j = 1; j <= degree; j += 1)
    {
        left[j] = u - knots[span + 1 - j];
        right[j] = knots[span + j] - u;
        var saved = 0;
        for (var r = 0; r < j; r += 1)
        {
            const denominator = right[r + 1] + left[j - r];
            const ratio = denominator == 0 ? 0 : basis[r] / denominator;
            basis[r] = saved + right[r + 1] * ratio;
            saved = left[j - r] * ratio;
        }
        basis[j] = saved;
    }
    return { "first" : span - degree, "values" : basis };
}

/** The point of a mappedGrid surface at (u, v), from its non-zero basis functions only. */
export function mappedGridPoint(grid is map, u is number, v is number) returns Vector
{
    const bu = basisNonzero(grid.knotsU, grid.degreeU, size(grid.us), u);
    const bv = basisNonzero(grid.knotsV, grid.degreeV, size(grid.vs), v);
    var sum = [0, 0, 0];
    for (var a = 0; a <= grid.degreeU; a += 1)
        for (var b = 0; b <= grid.degreeV; b += 1)
        {
            const w = bu.values[a] * bv.values[b];
            for (var c = 0; c < 3; c += 1)
                sum[c] += w * grid.coordinates[c][bu.first + a][bv.first + b];
        }
    return vector(sum[0], sum[1], sum[2]) * meter;
}

/** A face whose boundary probes are all within this of the exact map is not pinned (see pinFaceToCanonicalEdges). */
export const PIN_BOUNDARY_THRESHOLD = 3e-7 * meter;

/** The largest move of a control point (meters) that a pin solution may make; beyond it the pins are rejected. */
export const PIN_MAX_MOVE = 1e-4;

/**
 * The grid surface moved so that it passes through every pin: the smallest change of the control points (minimum norm, K x K system) that
 * makes the surface at (u, v) of each pin equal its image. The change is a few microns at most, so the shape between the pins is the map's.
 * @param pins {array} : [{ "u", "v", "image" }, ...]
 * @returns {map} the grid, with new control points and surface
 */
export function pinGrid(grid is map, pins is array) returns map
{
    const K = size(pins);
    if (K == 0)
        return grid;
    const nU = size(grid.us);
    const nV = size(grid.vs);
    const N = nU * nV;
    var rows = [];
    var residuals = [[], [], []];
    for (var pin in pins)
    {
        const bu = basisNonzero(grid.knotsU, grid.degreeU, nU, pin.u);
        const bv = basisNonzero(grid.knotsV, grid.degreeV, nV, pin.v);
        var row = makeArray(N, 0);
        for (var a = 0; a <= grid.degreeU; a += 1)
            for (var b = 0; b <= grid.degreeV; b += 1)
                row[(bu.first + a) * nV + (bv.first + b)] += bu.values[a] * bv.values[b];
        rows = append(rows, row);
        const there = mappedGridPoint(grid, pin.u, pin.v);
        for (var c = 0; c < 3; c += 1)
            residuals[c] = append(residuals[c], [(pin.image[c] - there[c]) / meter]);
    }
    const A = matrix(rows);
    const At = transpose(A);
    const solver = inverse(A * At + 1e-7 * identityMatrix(K));
    var coordinates = [];
    var maxDelta = 0;
    for (var c = 0; c < 3; c += 1)
    {
        const delta = At * (solver * matrix(residuals[c]));      // N x 1
        for (var q = 0; q < N; q += 1)
            maxDelta = max(maxDelta, abs(delta[q][0]));
        var plane = [];
        for (var i = 0; i < nU; i += 1)
        {
            var line = [];
            for (var j = 0; j < nV; j += 1)
                line = append(line, grid.coordinates[c][i][j] + delta[i * nV + j][0]);
            plane = append(plane, line);
        }
        coordinates = append(coordinates, matrix(plane));
    }
    var controlGrid = [];
    for (var i = 0; i < nU; i += 1)
    {
        var line = [];
        for (var j = 0; j < nV; j += 1)
            line = append(line, vector(coordinates[0][i][j], coordinates[1][i][j], coordinates[2][i][j]) * meter);
        controlGrid = append(controlGrid, line);
    }
    const surface = bSplineSurface({ "uDegree" : grid.degreeU, "vDegree" : grid.degreeV, "isUPeriodic" : false, "isVPeriodic" : false,
                                     "controlPoints" : controlPointMatrix(controlGrid), "uKnots" : knotArray(grid.knotsU), "vKnots" : knotArray(grid.knotsV) });
    return mergeMaps(grid, { "coordinates" : coordinates, "surface" : surface, "maxDelta" : maxDelta });
}

/**
 * Fits a mapped face to the SHARED edges of ALL its edges (collapsed edges, shorter than the vertex merge, are not edges any more). The shared
 * edge of a source edge is its WRAPPED edge, which the feature has already built and fitted to Edge accuracy / 2 with its end points at the merged
 * vertices: no further mapping is needed. Each edge is sampled at a number of fractions that grows with its length (capped by the control points of the
 * face's row, and kept below 0.8 of all its control points so the solve can reach them); the target of a pin is the point of the wrapped edge nearest
 * to the face's own boundary point there, and pinGrid moves the net the least it can to hit them. Two faces that share an edge are pinned onto the same
 * curve, so their boundaries agree on it whatever order they are built in. (The two long edges of a consumed sliver share ONE wrapped edge: deform.fs
 * aliases them, so the faces on the two sides of the sliver are pinned onto the same curve.)
 *
 * @param wrapped {map} : toString(source edge) -> its wrapped edge (Query)
 * @returns {map} { "grid", "pinned" : how many pins, "maxDelta" : the largest control point move (m), "rejected" }
 */
export function pinFaceToCanonicalEdges(context is Context, face is Query, grid is map, wrapped is map, boundaryMiss is ValueWithUnits, forced is boolean) returns map
{
    var edges = [];
    var lengths = [];
    var hasCollapsedEdge = false;
    for (var candidate in evaluateQuery(context, qAdjacent(face, AdjacencyType.EDGE, EntityType.EDGE)))
    {
        const length = evLength(context, { "entities" : candidate });
        if (length < VERTEX_MERGE_TOLERANCE)
            hasCollapsedEdge = true;
        if (length >= VERTEX_MERGE_TOLERANCE && wrapped[toString(candidate)] != undefined)
        {
            edges = append(edges, candidate);
            lengths = append(lengths, length);
        }
    }
    // Only a face that needs it is pinned: its own boundary probes miss by more than PIN_BOUNDARY_THRESHOLD, it has a collapsed edge (the micro-vertex clusters), or
    // it is the face across an aliased sliver edge (`forced`). A face whose boundary is already within a fraction of a micron of the exact map meets its accurate
    // neighbours by itself (the kernel sews up to about a micron): pinning it only costs kernel calls, which the 193-face lid cannot afford.
    if (!forced && !hasCollapsedEdge && boundaryMiss <= PIN_BOUNDARY_THRESHOLD)
        return { "grid" : grid, "pinned" : 0, "maxDelta" : 0, "rejected" : false };
    const cells = size(grid.us) * size(grid.vs);
    // an edge cannot be given more pins than its row of the net has control points (a 260 mm edge got ~170 pins on a net with 91 columns: the solve then swung the net by millimetres)
    const pinCap = clamp(floor(0.6 * max(size(grid.us), size(grid.vs))), 4, 60);
    var counts = [];
    var total = 0;
    for (var length in lengths)
    {
        counts = append(counts, clamp(ceil(length / (1.5e-3 * meter)) + 1, 4, pinCap));
        total += counts[size(counts) - 1];
    }
    if (total > 0.8 * cells)
        for (var q = 0; q < size(counts); q += 1)
            counts[q] = max(3, floor(counts[q] * 0.8 * cells / total));
    var pins = [];
    var endUvs = [];
    for (var e = 0; e < size(edges); e += 1)
    {
        const wrappedEdge = wrapped[toString(edges[e])];
        const fractions = range(0, 1, counts[e]);
        const onEdge = mapArray(evEdgeTangentLines(context, { "edge" : edges[e], "parameters" : fractions }), function(l) { return l.origin; });
        for (var k = 0; k < size(onEdge); k += 1)
        {
            const uv = evDistance(context, { "side0" : onEdge[k], "side1" : face }).sides[1].parameter;
            // The ends of the edges that meet at a vertex (and at a collapsed edge) lie at the same place on the face: two pins there that ask for targets even a
            // micron apart make the system nearly singular and swing the net by millimetres. An end pin that is close in UV to an earlier end pin is dropped.
            if (k == 0 || k == size(onEdge) - 1)
            {
                var repeated = false;
                for (var other in endUvs)
                    if (norm(vector(uv[0] - other[0], uv[1] - other[1])) < 1e-3)
                        repeated = true;
                if (repeated)
                    continue;
                endUvs = append(endUvs, [uv[0], uv[1]]);
            }
            const u = clamp(uv[0], 0, 1);
            const v = clamp(uv[1], 0, 1);
            const target = evDistance(context, { "side0" : mappedGridPoint(grid, u, v), "side1" : wrappedEdge }).sides[1].point;
            pins = append(pins, { "u" : u, "v" : v, "image" : target });
        }
    }
    if (size(pins) == 0)
        return { "grid" : grid, "pinned" : 0, "maxDelta" : 0, "rejected" : false };
    const pinnedGrid = pinGrid(grid, pins);
    // A solution that moves a control point by more than PIN_MAX_MOVE is the sign of nearly dependent pins (two at almost the same place that ask for
    // different points): it would bend the face absurdly, so the unpinned net is kept.
    if (pinnedGrid.maxDelta > PIN_MAX_MOVE)
        return { "grid" : grid, "pinned" : size(pins), "maxDelta" : pinnedGrid.maxDelta, "rejected" : true };
    return { "grid" : pinnedGrid, "pinned" : size(pins), "maxDelta" : pinnedGrid.maxDelta, "rejected" : false };
}

/** A face smaller than this is a sliver: it is not built, and the two faces across it meet on one shared wrapped edge (0.05 mm2). */
export const SLIVER_AREA = 5e-8 * meter ^ 2;
