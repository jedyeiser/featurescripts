FeatureScript 3083;

/**
 * Torsional stiffness (GJ) of a ski cross-section.
 *
 * The section is treated as a plate twisting about the ski axis, reduced to one dimension across the
 * width (Reissner-Mindlin plate torsion). For each width bin:
 *   D(y) = int G (z - c)^2 dz        twisting stiffness about the bin's own G-weighted centroid c
 *   S(y) = 5/6 * t^2 / int dz / G    transverse shear stiffness (layers in series)
 * and the rotation of the normal beta(y) minimises, at unit twist and with free edges,
 *   GJ = min over beta of  int [ D (beta' + 1)^2 + S (y - beta)^2 ] dy
 * With S -> infinity this is the thin-plate 4 * int D dy. Finite S lets the twisting moment die away
 * within ~0.3 t of each free edge, which the old formula (4 * sum G Iz about one global centroid)
 * missed: it over-predicted a representative ski section 1.6-2x, mostly through the steel edges.
 * Checked against an exact 2D Saint-Venant warping solve: within 2% on ski sections, stepped and
 * voided sections, 0.04% on a homogeneous rectangle (devtools/xsection/gj_oracle.py mirrors this code).
 *
 * G per body is Q66, the in-plane shear stiffness in ski axes. The through-thickness shear G_xz is
 * taken equal to it.
 *
 * Material intervals come from the body outlines (outer loops, holes and islands, even-odd), not from
 * the triangulation, so voids are open and a poor ear clip cannot change GJ.
 *
 * Coordinates: point2D[0] = thickness direction z, point2D[1] = width direction y.
 * Plain numbers inside, implicit SI (m, Pa, N*m^2); units restored on return.
 */

import(path : "onshape/std/common.fs", version : "3083.0");

/** Width bins per section. The edge boundary layer (~0.3 t) must span several bins. */
const GJ_WIDTH_BINS = 400;

/** Transverse shear correction factor (Reissner-Mindlin). */
const SHEAR_FACTOR = 5 / 6;

/**
 * Compute torsional stiffness GJ for a cross-section.
 *
 * @param section : cross-section with bodyData[].groups (perimeterPointIndices, subgroups) and sectionPoints
 * @param bodies : body definitions with materialData.qMatrix
 * @returns : GJ_eff in N*m^2
 */
export function computeTorsionalStiffness(section is map, bodies is array) returns ValueWithUnits
{
    const materials = collectMaterialLoops(section, bodies);
    if (size(materials) == 0)
    {
        return 0 * newton * meter * meter;
    }

    // Width range and a thickness reference (keeps the per-bin moments well conditioned)
    var yMin = inf;
    var yMax = -inf;
    var zRef = inf;
    for (var material in materials)
    {
        for (var loop in material.loops)
        {
            for (var p in loop)
            {
                yMin = min(yMin, p[1]);
                yMax = max(yMax, p[1]);
                zRef = min(zRef, p[0]);
            }
        }
    }
    const width = yMax - yMin;
    if (width <= 0)
    {
        return 0 * newton * meter * meter;
    }
    const n = GJ_WIDTH_BINS;
    const h = width / n;

    // Through-thickness sums per bin: int G dz, int G z dz, int G z^2 dz, int dz, int dz / G
    var S0 = makeArray(n, 0);
    var S1 = makeArray(n, 0);
    var S2 = makeArray(n, 0);
    var T = makeArray(n, 0);
    var R = makeArray(n, 0);

    for (var material in materials)
    {
        const G = material.G;
        const crossings = binCrossings(material.loops, yMin, zRef, h, n);
        for (var j = 0; j < n; j += 1)
        {
            const c = sort(crossings[j], function(a, b) { return a - b; });
            // Even-odd: consecutive crossing pairs bound material
            for (var k = 0; k + 1 < size(c); k += 2)
            {
                const a = c[k];
                const b = c[k + 1];
                S0[j] += G * (b - a);
                S1[j] += G * (b * b - a * a) / 2;
                S2[j] += G * (b * b * b - a * a * a) / 3;
                T[j] += b - a;
                R[j] += (b - a) / G;
            }
        }
    }

    // Per-bin twisting stiffness D (about the bin centroid) and transverse shear stiffness S
    var D = makeArray(n, 0);
    var S = makeArray(n, 0);
    for (var j = 0; j < n; j += 1)
    {
        if (S0[j] > 0)
        {
            D[j] = max(S2[j] - S1[j] * S1[j] / S0[j], 0);
        }
        if (R[j] > 0)
        {
            S[j] = SHEAR_FACTOR * T[j] * T[j] / R[j];
        }
    }

    // 1D linear elements across the width, nodes at bin edges, y centred on the section
    var yNode = makeArray(n + 1, 0);
    for (var i = 0; i <= n; i += 1)
    {
        yNode[i] = -width / 2 + i * h;
    }
    var diag = makeArray(n + 1, 0);
    var off = makeArray(n, 0);
    var f = makeArray(n + 1, 0);
    for (var e = 0; e < n; e += 1)
    {
        const ya = yNode[e];
        const yb = yNode[e + 1];
        diag[e] += D[e] / h + S[e] * h / 3;
        diag[e + 1] += D[e] / h + S[e] * h / 3;
        off[e] += -D[e] / h + S[e] * h / 6;
        f[e] += D[e] + S[e] * h * (2 * ya + yb) / 6;
        f[e + 1] += -D[e] + S[e] * h * (ya + 2 * yb) / 6;
    }

    // Gaps with no material leave rows empty; a tiny diagonal keeps them solvable (their f is 0)
    var maxDiag = 0;
    for (var i = 0; i <= n; i += 1)
    {
        maxDiag = max(maxDiag, diag[i]);
    }
    if (maxDiag <= 0)
    {
        return 0 * newton * meter * meter;
    }
    for (var i = 0; i <= n; i += 1)
    {
        diag[i] += 1e-12 * maxDiag;
    }

    const beta = solveSymmetricTridiagonal(diag, off, f);

    // GJ as the energy at the minimum (a sum of squares, so no cancellation)
    var GJ = 0;
    for (var e = 0; e < n; e += 1)
    {
        const slope = (beta[e + 1] - beta[e]) / h;
        const ra = yNode[e] - beta[e];
        const rb = yNode[e + 1] - beta[e + 1];
        GJ += D[e] * h * (slope + 1) * (slope + 1) + S[e] * h * (ra * ra + ra * rb + rb * rb) / 3;
    }
    return GJ * newton * meter * meter;
}

/**
 * Outlines of every body that has a shear modulus, as plain [z, y] points in meters.
 *
 * @returns : array of { G (Pa), loops : [[[z, y], ...], ...] } - outer loops, holes and islands
 */
function collectMaterialLoops(section is map, bodies is array) returns array
{
    var materials = [];
    for (var bodyData in section.bodyData)
    {
        const G = bodyShearModulus(bodies, bodyData.bodyIdx);
        if (G <= 0 || bodyData.groups == undefined)
        {
            continue;
        }
        var loops = [];
        for (var group in bodyData.groups)
        {
            loops = appendGroupLoops(loops, group, section.sectionPoints);
        }
        if (size(loops) > 0)
        {
            materials = append(materials, { "G" : G, "loops" : loops });
        }
    }
    return materials;
}

function appendGroupLoops(loops is array, group is map, sectionPoints is array) returns array
{
    if (group.perimeterPointIndices != undefined && size(group.perimeterPointIndices) >= 3)
    {
        var loop = [];
        for (var idx in group.perimeterPointIndices)
        {
            const p = sectionPoints[idx].point2D;
            loop = append(loop, [p[0] / meter, p[1] / meter]);
        }
        loops = append(loops, loop);
    }
    if (group.subgroups != undefined)
    {
        for (var subgroup in group.subgroups)
        {
            loops = appendGroupLoops(loops, subgroup, sectionPoints);
        }
    }
    return loops;
}

/**
 * Q66 of a body in Pa (0 when the body has no material data).
 * Torsion loads the ski-axis shear, so Q66 in ski axes is the stiffness for every material.
 */
function bodyShearModulus(bodies is array, bodyIdx) returns number
{
    for (var body in bodies)
    {
        if (body.bodyIdx == bodyIdx)
        {
            if (body.hasMaterialData == true && body.materialData != undefined && body.materialData.qMatrix != undefined)
            {
                return body.materialData.qMatrix[2][2] / (newton / (meter * meter));
            }
            return 0;
        }
    }
    return 0;
}

/**
 * For each width bin, the thickness coordinates (relative to zRef) where the loops cross the bin's
 * centre line. Half-open edge spans [lo, hi) count a vertex exactly once.
 */
function binCrossings(loops is array, yMin is number, zRef is number, h is number, n is number) returns array
{
    var crossings = makeArray(n, []);
    for (var loop in loops)
    {
        const m = size(loop);
        for (var k = 0; k < m; k += 1)
        {
            const pa = loop[k];
            const pb = loop[(k + 1) % m];
            if (pa[1] == pb[1])
            {
                continue;
            }
            const lo = min(pa[1], pb[1]);
            const hi = max(pa[1], pb[1]);
            const j0 = max(ceil((lo - yMin) / h - 0.5), 0);
            const j1 = min(ceil((hi - yMin) / h - 0.5) - 1, n - 1);
            for (var j = j0; j <= j1; j += 1)
            {
                const yc = yMin + (j + 0.5) * h;
                const z = pa[0] + (yc - pa[1]) / (pb[1] - pa[1]) * (pb[0] - pa[0]) - zRef;
                crossings[j] = append(crossings[j], z);
            }
        }
    }
    return crossings;
}

/**
 * Solve a symmetric tridiagonal system (Thomas algorithm).
 *
 * @param diag : n + 1 diagonal entries
 * @param off : n off-diagonal entries (row i, column i + 1)
 * @param rhs : n + 1 right-hand side entries
 */
function solveSymmetricTridiagonal(diag is array, off is array, rhs is array) returns array
{
    const n = size(diag);
    var cp = makeArray(n, 0);
    var dp = makeArray(n, 0);
    for (var i = 0; i < n; i += 1)
    {
        var m = diag[i];
        var r = rhs[i];
        if (i > 0)
        {
            m -= off[i - 1] * cp[i - 1];
            r -= off[i - 1] * dp[i - 1];
        }
        if (i < n - 1)
        {
            cp[i] = off[i] / m;
        }
        dp[i] = r / m;
    }
    var x = makeArray(n, 0);
    x[n - 1] = dp[n - 1];
    for (var i = n - 2; i >= 0; i -= 1)
    {
        x[i] = dp[i] - cp[i] * x[i + 1];
    }
    return x;
}
