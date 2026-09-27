"""Compare GJ on real exported sections: FS mirror (width solve) vs exact 2D Saint-Venant vs the old formula.

Run export_sections.py first.  usage: python devtools/xsection/real_section_check.py
"""
import os, sys, time
import numpy as np
from matplotlib.path import Path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gj_oracle as O
import scipy.sparse as sp, scipy.sparse.linalg as spl

HERE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "sections")


def load(name):
    G = {}; st = {}; loops = {}
    for line in open(os.path.join(HERE, "sec_%s.txt" % name)):
        f = line.rstrip("\n").split("|")
        if f[0] == "B":
            G[int(float(f[1]))] = float(f[2])
        elif f[0] == "S":
            st[int(float(f[1]))] = (float(f[2]), float(f[3]))
        elif f[0] == "L":
            si, bi, depth = int(float(f[1])), int(float(f[2])), int(float(f[3]))
            pts = [tuple(map(float, p.split(","))) for p in f[4].split(";") if p]
            loops.setdefault(si, {}).setdefault(bi, []).append((depth, pts))
    return G, st, loops


def raster(bodies, h):
    ys = [p[1] for _, ls in bodies for l in ls for p in l]; zs = [p[0] for _, ls in bodies for l in ls for p in l]
    y0, y1, z0, z1 = min(ys), max(ys), min(zs), max(zs)
    ny, nz = int(np.ceil((y1 - y0) / h)), int(np.ceil((z1 - z0) / h))
    yc = y0 + (np.arange(ny) + 0.5) * h; zc = z0 + (np.arange(nz) + 0.5) * h
    Y, Z = np.meshgrid(yc, zc, indexing="ij"); P = np.column_stack([Y.ravel(), Z.ravel()])
    Gm = np.zeros(ny * nz)
    for g, ls in bodies:
        inside = np.zeros(ny * nz, bool)
        for l in ls:
            inside ^= Path([(p[1], p[0]) for p in l]).contains_points(P)
        Gm[inside] = np.maximum(Gm[inside], g)  # overlap: keep stiffer (flag below)
    return Gm.reshape(ny, nz), y0, z0, yc, zc


def exact(Gm, y0, z0, h):
    ny, nz = Gm.shape
    idx = lambda i, j: i * (nz + 1) + j
    N = (ny + 1) * (nz + 1)
    ii, jj = np.nonzero(Gm)
    # vectorised P1 assembly on the two triangles of each cell (right triangles, legs h)
    rows = []; cols = []; vals = []; f = np.zeros(N); E0 = 0.0
    for tri in (((0, 0), (1, 0), (1, 1)), ((0, 0), (1, 1), (0, 1))):
        pl = np.array([[a * h, b * h] for a, b in tri])
        M = np.column_stack([np.ones(3), pl]); ar = abs(np.linalg.det(M)) / 2; B = np.linalg.inv(M)[1:]
        Kl = ar * (B.T @ B)
        g = Gm[ii, jj]
        ny0 = y0 + ii * h; nz0 = z0 + jj * h
        py = ny0[:, None] + pl[None, :, 0]; pz = nz0[:, None] + pl[None, :, 1]
        yb = py.mean(1); zb = pz.mean(1)
        nodes = np.stack([idx(ii + a, jj + b) for a, b in tri], 1)
        for a in range(3):
            np.add.at(f, nodes[:, a], g * ar * (B[0, a] * zb - B[1, a] * yb))
            for b in range(3):
                rows.append(nodes[:, a]); cols.append(nodes[:, b]); vals.append(g * Kl[a, b])
        for pk in (py, pz):
            E0 += (g * (ar / 6) * ((pk ** 2).sum(1) + pk[:, 0] * pk[:, 1] + pk[:, 0] * pk[:, 2] + pk[:, 1] * pk[:, 2])).sum()
    K = sp.csr_matrix((np.concatenate(vals), (np.concatenate(rows), np.concatenate(cols))), shape=(N, N))
    act = np.where(K.diagonal() > 0)[0]
    K = K[act][:, act] + sp.eye(len(act)) * 1e-12 * K.diagonal().max()
    psi = spl.spsolve(K.tocsc(), f[act])
    return (E0 - f[act] @ psi) * 1e-3


for name in ("ROY", "Test"):
    G, st, loops = load(name)
    for si, bl in loops.items():
        bodies = [(G[b], [l for _, l in ls]) for b, ls in bl.items() if G.get(b, -1) > 0]
        wd = O.width_gj(bodies)
        t = time.time()
        h = 0.05 if name == "ROY" and si == 41 else 0.1
        Gm, y0, z0, yc, zc = raster(bodies, h)
        ex = exact(Gm, y0, z0, h)
        c = (Gm * zc).sum() / Gm.sum()
        thin = 4 * (Gm * ((zc - c) ** 2)).sum() * h * h * 1e-3
        print("%s st%d x=%.0f  FS=%.1f  mirror=%.1f  exact=%.1f  thin(raster)=%.1f  width %.1fmm thick %.1fmm  (%.0fs)" % (
            name, si, st[si][0], st[si][1], wd, ex, thin, Gm.shape[0] * h, Gm.shape[1] * h, time.time() - t))
        depths = sorted(set(dd for ls in bl.values() for dd, _ in ls))
        print("   loop depths present:", depths, " bodies:", {b: len(ls) for b, ls in bl.items()})
