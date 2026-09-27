"""GJ oracle for xSection/section/xSect_GJ.fs.

Three torsional-stiffness calculations for a cross-section made of material regions:

  exact_gj()  - exact 2D Saint-Venant torsion (warping function, linear-triangle FEM on a fine grid).
                Validated against the rectangle series solution to ~0.01%.
  width_gj()  - line-for-line mirror of the FeatureScript implementation: per width bin, material
                intervals from the outlines (even-odd), D = int G (z-c)^2 dz about the bin G-centroid,
                S = 5/6 t^2 / int dz/G, then a 1D free-edge solve across the width.
  thin_gj()   - the old production formula, 4 * sum(G * Iz) about the global G-centroid.

Units: mm and GPa in, N*m^2 out. exact_gj/thin_gj take axis-aligned rectangles (y0, y1, z0, z1, G);
width_gj takes outlines so holes can be tested.

usage:  python devtools/xsection/gj_oracle.py
"""
import numpy as np
import scipy.sparse as sp
import scipy.sparse.linalg as spl

GRID = 0.1  # mm, exact solve


def _raster(rects):
    y0 = min(r[0] for r in rects); y1 = max(r[1] for r in rects)
    z0 = min(r[2] for r in rects); z1 = max(r[3] for r in rects)
    ny, nz = int(round((y1 - y0) / GRID)), int(round((z1 - z0) / GRID))
    yc = y0 + (np.arange(ny) + 0.5) * GRID
    zc = z0 + (np.arange(nz) + 0.5) * GRID
    Y, Z = np.meshgrid(yc, zc, indexing="ij")
    G = np.zeros((ny, nz))
    for (a, b, c, d, g) in rects:  # later rectangles overwrite earlier ones (g = 0 cuts a void)
        G[(Y >= a) & (Y < b) & (Z >= c) & (Z < d)] = g
    return G, y0, z0


def exact_gj(rects):
    G, y0, z0 = _raster(rects)
    h = GRID
    ny, nz = G.shape
    idx = lambda i, j: i * (nz + 1) + j
    ys = y0 + np.arange(ny + 1) * h
    zs = z0 + np.arange(nz + 1) * h
    rows, cols, vals = [], [], []
    f = np.zeros((ny + 1) * (nz + 1)); E0 = 0.0
    for i in range(ny):
        for j in range(nz):
            g = G[i, j]
            if g == 0:
                continue
            for tri in (((i, j), (i + 1, j), (i + 1, j + 1)), ((i, j), (i + 1, j + 1), (i, j + 1))):
                p = np.array([[ys[a], zs[b]] for a, b in tri]); n = [idx(a, b) for a, b in tri]
                M = np.column_stack([np.ones(3), p]); ar = abs(np.linalg.det(M)) / 2
                B = np.linalg.inv(M)[1:]
                K = g * ar * (B.T @ B)
                yb, zb = p.mean(0)
                fl = g * ar * (B[0] * zb - B[1] * yb)
                for k in range(2):
                    E0 += g * (ar / 6) * ((p[:, k] ** 2).sum() + p[0, k] * p[1, k] + p[0, k] * p[2, k] + p[1, k] * p[2, k])
                for a in range(3):
                    f[n[a]] += fl[a]
                    for b in range(3):
                        rows.append(n[a]); cols.append(n[b]); vals.append(K[a, b])
    K = sp.csr_matrix((vals, (rows, cols)), shape=(len(f), len(f)))
    act = np.where(K.diagonal() > 0)[0]
    K = K[act][:, act] + sp.eye(len(act)) * 1e-12 * K.diagonal().max()
    psi = spl.spsolve(K.tocsc(), f[act])
    return (E0 - f[act] @ psi) * 1e-3


def thin_gj(rects):
    G, y0, z0 = _raster(rects)
    zc = z0 + (np.arange(G.shape[1]) + 0.5) * GRID
    c = (G * zc).sum() / G.sum()
    return 4 * (G * ((zc - c) ** 2 + GRID ** 2 / 12)).sum() * GRID * GRID * 1e-3


def rect_loop(y0, y1, z0, z1):
    return [(z0, y0), (z0, y1), (z1, y1), (z1, y0)]  # points are (z, y) like point2D


def width_gj(bodies, nbins=400):
    """bodies: list of (G, [loop, ...]); loop = list of (z, y). Mirrors xSect_GJ.fs."""
    ys = [p[1] for _, loops in bodies for l in loops for p in l]
    zs = [p[0] for _, loops in bodies for l in loops for p in l]
    ymin, ymax, zref = min(ys), max(ys), min(zs)
    n = nbins; h = (ymax - ymin) / n
    S0 = np.zeros(n); S1 = np.zeros(n); S2 = np.zeros(n); T = np.zeros(n); R = np.zeros(n)
    for g, loops in bodies:
        cr = [[] for _ in range(n)]
        for l in loops:
            for k in range(len(l)):
                (za, ya), (zb, yb) = l[k], l[(k + 1) % len(l)]
                if ya == yb:
                    continue
                lo, hi = min(ya, yb), max(ya, yb)
                j0 = max(int(np.ceil((lo - ymin) / h - 0.5)), 0)
                j1 = min(int(np.ceil((hi - ymin) / h - 0.5)) - 1, n - 1)
                for j in range(j0, j1 + 1):
                    yc = ymin + (j + 0.5) * h
                    cr[j].append(za + (yc - ya) / (yb - ya) * (zb - za) - zref)
        for j in range(n):
            c = sorted(cr[j])
            for k in range(0, len(c) - 1, 2):
                a, b = c[k], c[k + 1]
                S0[j] += g * (b - a); S1[j] += g * (b * b - a * a) / 2; S2[j] += g * (b ** 3 - a ** 3) / 3
                T[j] += b - a; R[j] += (b - a) / g
    D = np.where(S0 > 0, S2 - S1 ** 2 / np.where(S0 > 0, S0, 1), 0)
    S = np.where(R > 0, 5 / 6 * T ** 2 / np.where(R > 0, R, 1), 0)
    yn = -(ymax - ymin) / 2 + np.arange(n + 1) * h
    dg = np.zeros(n + 1); off = np.zeros(n); f = np.zeros(n + 1)
    for e in range(n):
        ya, yb = yn[e], yn[e + 1]
        dg[e] += D[e] / h + S[e] * h / 3; dg[e + 1] += D[e] / h + S[e] * h / 3
        off[e] += -D[e] / h + S[e] * h / 6
        f[e] += D[e] + S[e] * h * (2 * ya + yb) / 6
        f[e + 1] += -D[e] + S[e] * h * (ya + 2 * yb) / 6
    dg += 1e-12 * dg.max()
    cp = np.zeros(n + 1); dp = np.zeros(n + 1); beta = np.zeros(n + 1)
    for i in range(n + 1):
        m = dg[i] - (off[i - 1] * cp[i - 1] if i > 0 else 0)
        cp[i] = off[i] / m if i < n else 0
        dp[i] = (f[i] - (off[i - 1] * dp[i - 1] if i > 0 else 0)) / m
    for i in range(n, -1, -1):
        beta[i] = dp[i] - (cp[i] * beta[i + 1] if i < n else 0)
    gj = 0.0
    for e in range(n):
        bp = (beta[e + 1] - beta[e]) / h
        ra, rb = yn[e] - beta[e], yn[e + 1] - beta[e + 1]
        gj += D[e] * h * (bp + 1) ** 2 + S[e] * h * (ra * ra + ra * rb + rb * rb) / 3
    return gj * 1e-3


def ski_section(core_g, skin_g, steel=True):
    """Representative section from reviews/2026-09-25_tools_review/round2_lead_materials.md s4."""
    edge_g = 80.0 if steel else 0.27
    return [(-50, -48, 0, 2, edge_g), (-48, 48, 0, 2, 0.27), (48, 50, 0, 2, edge_g),
            (-50, 50, 2, 3, skin_g), (-50, -45, 3, 13, 0.85), (-45, 45, 3, 13, core_g),
            (45, 50, 3, 13, 0.85), (-50, 50, 13, 14, skin_g), (-50, 50, 14, 14.6, 0.45)]


def as_bodies(rects):
    return [(g, [rect_loop(a, b, c, d)]) for (a, b, c, d, g) in rects]


CASES = [
    ("rect 100x14.6 G=1", [(-50, 50, 0, 14.6, 1.0)]),
    ("stepped 50x10 + 50x4", [(-50, 0, 0, 10, 1.0), (0, 50, 0, 4, 1.0)]),
    ("ski, triax G6, aspen iso 3.67", ski_section(3.67, 6.0)),
    ("ski, triax G6, aspen 0.62", ski_section(0.62, 6.0)),
    ("ski, triax G6, aspen 0.62, no steel", ski_section(0.62, 6.0, False)),
    ("ski, biax G2.6, aspen 0.62", ski_section(0.62, 2.6)),
]

if __name__ == "__main__":
    print("GJ [N*m^2]  (analytic rect series: %.1f)" % (100 * 14.6 ** 3 / 3 * (1 - 0.630 * 14.6 / 100) * 1e-3))
    print("%-38s %9s %9s %9s %8s" % ("case", "thin(old)", "width", "exact", "width/ex"))
    for name, rects in CASES:
        ex = exact_gj(rects)
        wd = width_gj(as_bodies(rects))
        print("%-38s %9.1f %9.1f %9.1f %8.3f" % (name, thin_gj(rects), wd, ex, wd / ex))
    # Open slot through the section (void from face to face): width solve must treat it as two plates.
    slot = [(-50, 50, 0, 10, 1.0), (-2, 2, 0, 10, 0.0)]
    slot_bodies = [(1.0, [rect_loop(-50, -2, 0, 10)]), (1.0, [rect_loop(2, 50, 0, 10)])]
    print("%-38s %9s %9.1f %9.1f" % ("open slot (two plates)", "-", width_gj(slot_bodies), exact_gj(slot)))
    # Interior void inside one body (outer loop + reversed inner loop, even-odd).
    # Limitation: a CLOSED cell carries Bredt shear flow the width solve cannot see, so exact >> width.
    hole = [(1.0, [rect_loop(-50, 50, 0, 10), rect_loop(-20, 20, 2, 8)[::-1]])]
    print("%-38s %9s %9.1f %9.1f   (closed cell: not modelled)" % ("interior void 40x6", "-", width_gj(hole),
          exact_gj([(-50, 50, 0, 10, 1.0), (-20, 20, 2, 8, 0.0)])))
