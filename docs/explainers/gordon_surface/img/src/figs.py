"""Figures for docs/explainers/gordon_surface/gordon_surface_explained.md and the Gordon-tools decks.

Data in data/ was read from the gordonSurface document (read-only eval API, 2026-09-26):
  gt.json       "Gordon tools tests": Modify curve end T01 / T03 / T10 / T30, Scaled Curve SC0 / SC3
                (docs/tooling/sample_feature_edges.py)
  gordon1.json  "Test Part Studio 1": Gordon Surface 1 sampled on a 25 x 25 parameter grid + the wires lying on it
fig01 (how a Gordon surface is built) is a schematic on an analytic network, computed here.
usage (repo root): python docs/explainers/gordon_surface/img/src/figs.py
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "..", "tooling"))
from figstyle import *  # noqa: F401,F403,E402

OUT = os.path.join(HERE, "..")
GT = json.load(open(os.path.join(HERE, "data", "gt.json")))


def lagrange(ts, t):
    """Lagrange basis values at t for nodes ts."""
    out = []
    for i, ti in enumerate(ts):
        v = 1.0
        for j, tj in enumerate(ts):
            if j != i:
                v *= (t - tj) / (ti - tj)
        out.append(v)
    return np.array(out)


# ---------------------------------------------------------------- fig01 Gordon construction
def fig01():
    f = lambda x, y: 0.35 * np.sin(2.6 * x) * np.cos(1.8 * y) + 0.25 * x * y + 0.2 * np.sin(8 * y) * x + 0.18 * np.sin(9 * x) * y
    us, vs = [0.0, 0.5, 1.0], [0.0, 0.45, 1.0]            # U curves at y = vs[i]; V curves at x = us[j]
    g = np.linspace(0, 1, 41)
    X, Y = np.meshgrid(g, g, indexing="ij")
    Lv = np.array([lagrange(vs, y) for y in g])          # (31, 3) blend across the U curves
    Lu = np.array([lagrange(us, x) for x in g])          # (31, 3) blend across the V curves
    Uc = np.array([f(g, v) for v in vs])                 # U curve values along x: (3, 31)
    Vc = np.array([f(u, g) for u in us])                 # V curve values along y: (3, 31)
    Su = np.einsum("yi,ix->xy", Lv, Uc)                  # skins the U curves
    Sv = np.einsum("xj,jy->xy", Lu, Vc)                  # skins the V curves
    grid = np.array([[f(u, v) for v in vs] for u in us])  # intersections (u, v)
    T = np.einsum("xj,jk,yk->xy", Lu, grid, Lv)          # through the grid points only
    S = Su + Sv - T
    panels = [(Su, "S_u: skin the U curves"), (Sv, "S_v: skin the V curves"),
              (T, "T: through the crossings only"), (S, "Gordon:  S = S_u + S_v - T")]
    iu = [int(round(u * 40)) for u in us]
    iv = [int(round(v * 40)) for v in vs]
    fig = plt.figure(figsize=(11.5, 8.4))
    for k, (Z, title) in enumerate(panels):
        ax = fig.add_subplot(2, 2, k + 1, projection="3d")
        ax.plot_surface(X, Y, Z, color="#9fc3ee" if k == 3 else "#e4e2dc", alpha=0.55, lw=0, rstride=1, cstride=1)
        for v, row, j in zip(vs, Uc, iv):
            ax.plot(g, np.full_like(g, v), row, color=BLUE, lw=2.4)
            ax.plot(g, np.full_like(g, v), Z[:, j], color=INK, lw=1, ls="--")
        for u, col, i in zip(us, Vc, iu):
            ax.plot(np.full_like(g, u), g, col, color=ORANGE, lw=2.4)
            ax.plot(np.full_like(g, u), g, Z[i, :], color=INK, lw=1, ls="--")
        ax.scatter([u for u in us for _ in vs], [v for _ in us for v in vs], grid.flatten(), color=INK, s=12)
        mu = max(np.abs(Z[:, j] - row).max() for row, j in zip(Uc, iv))
        mv = max(np.abs(Z[i, :] - col).max() for col, i in zip(Vc, iu))
        ax.set_title(title, fontsize=11)
        ax.text2D(0.5, 0.0, "misses the U curves by %.3f, the V curves by %.3f" % (mu, mv), transform=ax.transAxes,
                  ha="center", fontsize=9, color=RED if max(mu, mv) > 1e-6 else GREEN)
        ax.set_axis_off()
        ax.view_init(30, -56)
    fig.text(0.5, 0.985, "blue: U curves   orange: V curves   dashed: the surface along each curve   dots: crossings",
             ha="center", fontsize=9.5, color=INK2)
    save(fig, os.path.join(OUT, "fig01_gordon_construction.png"))


# ---------------------------------------------------------------- fig02 real network
def fig02():
    v = json.load(open(os.path.join(HERE, "data", "gordon1.json")))
    S = np.zeros((25, 25, 3))
    E = []
    for s in v:
        p = s.split("|")
        if p[0] == "S":
            S[int(p[1]), int(p[2])] = [float(x) for x in p[3].split(",")]
        else:
            E.append(np.array([[float(x) for x in q.split(",")] for q in p[1:]]))
    fig = plt.figure(figsize=(9, 6.2))
    ax = fig.add_subplot(projection="3d")
    ax.plot_surface(S[:, :, 0], S[:, :, 1], S[:, :, 2], color="#9fc3ee", alpha=0.55, lw=0.2, edgecolor="#ffffff")
    seen = []
    for e in E:
        key = tuple(np.round(np.concatenate([e[0], e[-1]]), 0))
        if key in seen or tuple(np.round(np.concatenate([e[-1], e[0]]), 0)) in seen:
            continue
        seen.append(key)
        ext = np.ptp(e, axis=0)
        if ext[2] < 0.05 and (abs(abs(e[0, 1]) - 32) < 0.5 and abs(abs(e[-1, 1]) - 32) < 0.5):
            continue    # the flat frame lines at z = 0 that happen to lie on the boundary
        col = BLUE if ext[0] > ext[1] else ORANGE
        ax.plot(e[:, 0], e[:, 1], e[:, 2], color=col, lw=2.2)
    ax.set_box_aspect((np.ptp(S[:, :, 0]), np.ptp(S[:, :, 1]), np.ptp(S[:, :, 2]) * 2.5))
    ax.view_init(30, -62)
    ax.set_axis_off()
    ax.set_title("Gordon Surface 1 (Test Part Studio 1): one B-spline face through 5 U curves (blue) and 3 V curves (orange)",
                 fontsize=10)
    save(fig, os.path.join(OUT, "fig02_real_network.png"))


# ---------------------------------------------------------------- Modify curve end
def splines(key, dx=0.0):
    out = []
    for e in GT[key]:
        if e["type"].startswith("SPLINE") or e["type"].startswith("CIRCLE"):
            p = np.array(e["pts"])[:, :2].copy()
            p[:, 0] -= dx
            out.append(p)
    return out


def fig03():
    fig, axs = plt.subplots(1, 3, figsize=(13, 3.9))
    cases = [("T01 curve", "T01 world*", 0, None, "T01: move the end to (200, 30), G0\nthe change fades toward the fixed end"),
             ("T30 curve", "T30 hold point G1*", 40000, None, "T30: hold point + G1\neverything past the hold is unchanged"),
             ("T10 curve", "T10 reference arc R25*", 20000, "T10 reference arc", "T10: match an R25 arc at the end (G2)\ntangent and curvature 40/m = the arc's")]
    for ax, (src, out, dx, refk, title) in zip(axs, cases):
        for p in splines(src, dx):
            ax.plot(p[:, 0], p[:, 1], color=MUTED, lw=3, ls="--", label="source")
        for p in splines(out, dx):
            ax.plot(p[:, 0], p[:, 1], color=BLUE, lw=2.2, label="result")
        if refk:
            for p in splines(refk, dx):
                ax.plot(p[:, 0], p[:, 1], color=ORANGE, lw=1.6, label="reference arc")
        ax.plot(200, 0, "o", color=MUTED, ms=6)
        ax.plot(200, 30, "o", color=RED, ms=6)
        arrow(ax, (200, 1.5), (200, 28.5), col=RED, lw=1.2, ms=9)
        ax.text(204, 15, "to", color=RED, fontsize=9)
        if "T30" in src:
            ax.plot(150, -30, "s", color=GREEN, ms=7)
            ax.text(150, -42, "hold (150, -30)", ha="center", color=GREEN, fontsize=9)
        ax.set_aspect("equal")
        ax.set_xlim(15, 235)
        ax.set_ylim(-50, 70)
        ax.set_title(title, fontsize=10)
        ax.legend(loc="upper left", fontsize=8)
    save(fig, os.path.join(OUT, "fig03_modify_curve_end.png"))


# ---------------------------------------------------------------- Scaled Curve
def fig04():
    fig, (a1, a2) = plt.subplots(1, 2, figsize=(12, 4.4), gridspec_kw={"width_ratios": [1, 1.2]})
    for key, col, lab in (("SC3 group 0", MUTED, "Group 0 (R100)"), ("SC3 group 1", INK2, "Group 1 (R200)")):
        for p in splines(key, 13000):
            a1.plot(p[:, 0], p[:, 1], color=col, lw=2.4, label=lab)
    for p in splines("SC3 arcs*", 13000):
        a1.plot(p[:, 0], p[:, 1], color=BLUE, lw=2.4, label="scale factor 0 -> R150, but only 1 rad of it (bug)")
    a1.set_aspect("equal")
    a1.legend(loc="lower left", fontsize=8.5)
    a1.set_title("SC3: halfway between two arcs is an exact R150 arc")
    th = np.linspace(1, np.pi / 2, 40)
    a1.plot(150 * np.cos(th), 150 * np.sin(th), color=BLUE, lw=1.2, ls=":")
    a1.text(40, 160, "expected: the full quarter", fontsize=8.5, color=BLUE)
    # transitions: a varying blend
    s = np.linspace(0, 1, 200)
    lin = s
    sinus = (1 - np.cos(np.pi * s)) / 2
    k = 10
    raw = lambda t: 1 / (1 + np.exp(-k * (t - 0.5)))
    logi = (raw(s) - raw(0)) / (raw(1) - raw(0))
    for y, col, lab in ((lin, INK2, "Linear"), (sinus, BLUE, "Sinusoidal  (1 - cos pi s) / 2"), (logi, ORANGE, "Logistic (k = 10, normalised)")):
        a2.plot(s, -0.4 + 0.8 * y, color=col, lw=2, label=lab)
    a2.axhline(-0.5, color=MUTED, lw=1, ls=":")
    a2.axhline(0.5, color=MUTED, lw=1, ls=":")
    a2.text(0.01, -0.47, "-0.5 = on Group 0", fontsize=8.5, color=INK2)
    a2.text(0.01, 0.53, "+0.5 = on Group 1", fontsize=8.5, color=INK2)
    a2.set_xlabel("position along the curve s")
    a2.set_ylabel("scale factor")
    a2.set_ylim(-0.6, 0.65)
    a2.legend(loc="center right", fontsize=8.5)
    a2.set_title("Initial -0.4 -> final +0.4: how the blend travels")
    save(fig, os.path.join(OUT, "fig04_scaled_curve.png"))


# ---------------------------------------------------------------- compatibility
def fig05():
    fig, ax = plt.subplots(figsize=(10.5, 3.4))
    bare(ax, equal=False)
    ax.set_xlim(-0.35, 1.12)
    ax.set_ylim(-0.6, 3.4)
    before = [("curve A: degree 3", [0.3, 0.6]), ("curve B: degree 2", [0.5]), ("curve C: degree 3", [0.3, 0.8])]
    union = sorted(set(k for _, ks in before for k in ks))
    for i, (lab, ks) in enumerate(before):
        y = 3 - i
        ax.plot([0, 1], [y, y], color=INK2, lw=1.4)
        ax.text(-0.33, y, lab, va="center", fontsize=9)
        for k in union:
            has = k in ks
            ax.plot(k, y, "o" if has else "o", ms=8, mfc=INK if has else "#ffffff", mec=INK if has else ORANGE, mew=1.6)
        ax.text(1.03, y, "+ %d knot(s)%s" % (len(union) - len(ks), ", degree 2 -> 3" if "2" in lab else ""),
                va="center", fontsize=8.5, color=ORANGE)
    ax.plot([0, 1], [0, 0], color=BLUE, lw=2)
    for k in union:
        ax.plot(k, 0, "o", ms=8, color=BLUE)
    ax.text(-0.33, 0, "all three after", va="center", fontsize=9, color=BLUE, weight="bold")
    ax.text(0.5, -0.45, "same degree, same knots, same control-point count -> corresponding control points can be blended; the curves' shapes do not change",
            ha="center", fontsize=8.5, color=INK2)
    for k in union:
        ax.text(k, 3.25, "%.1f" % k, ha="center", fontsize=8, color=INK2)
    ax.text(0.5, 3.45 - 0.05, "", fontsize=8)
    save(fig, os.path.join(OUT, "fig05_compatibility.png"))


# ---------------------------------------------------------------- interior curves (Coons)
def fig06():
    fig = plt.figure(figsize=(8.5, 5.2))
    ax = fig.add_subplot(projection="3d")
    t = np.linspace(0, 1, 60)
    U0 = lambda u: np.array([u * 100, 0 * u, 8 * np.sin(np.pi * u)])
    U1 = lambda u: np.array([u * 100, 70 + 6 * np.sin(np.pi * u), 3 * np.sin(2 * np.pi * u)])
    V0 = lambda v: np.array([0 * v - 4 * np.sin(np.pi * v), v * 70, 10 * np.sin(np.pi * v)])
    V1 = lambda v: np.array([100 + 5 * np.sin(np.pi * v), v * 70, 4 * np.sin(np.pi * v)])

    def P(u, v):
        return ((1 - v) * U0(u) + v * U1(u) + (1 - u) * V0(v) + u * V1(v)
                - ((1 - u) * (1 - v) * U0(0) + u * (1 - v) * U0(1) + (1 - u) * v * U1(0) + u * v * U1(1)))
    for c, col in ((U0, INK), (U1, INK), (V0, INK), (V1, INK)):
        p = np.array([c(s) for s in t])
        ax.plot(p[:, 0], p[:, 1], p[:, 2], color=col, lw=2.4)
    n = 3
    for i in range(1, n + 1):
        v = i / (n + 1)
        p = np.array([P(s, v) for s in t])
        ax.plot(p[:, 0], p[:, 1], p[:, 2], color=BLUE, lw=1.8)
        u = i / (n + 1)
        q = np.array([P(u, s) for s in t])
        ax.plot(q[:, 0], q[:, 1], q[:, 2], color=ORANGE, lw=1.8)
    pts = np.array([P(i / (n + 1), j / (n + 1)) for i in range(n + 2) for j in range(n + 2)])
    ax.scatter(pts[:, 0], pts[:, 1], pts[:, 2], color=INK, s=8)
    ax.set_box_aspect((100, 76, 30))
    ax.view_init(32, -60)
    ax.set_axis_off()
    ax.set_title("Interior curves: 4 boundaries (black) -> Coons grid -> 3 interior U (blue) and 3 V (orange), sharing the grid points",
                 fontsize=9.5)
    save(fig, os.path.join(OUT, "fig06_interior_curves.png"))


def fig07():
    """Pull surface P2 (Gordon tools tests): 5 x 5 handles, G1, +10 mm at (2, 2) on a cylinder patch."""
    v = json.load(open(os.path.join(HERE, "data", "pull_p2.json")))
    G = {"SRC": np.zeros((21, 21, 3)), "OUT": np.zeros((21, 21, 3))}
    d = np.zeros((21, 21))
    for s in v:
        p = s.split("|")
        if p[1] != "0":
            continue    # face 0 = the cylinder face (the patch is a solid) / the pulled sheet
        G[p[0]][int(p[2]), int(p[3])] = [float(x) for x in p[4].split(",")]
        if p[0] == "OUT":
            d[int(p[2]), int(p[3])] = float(p[5])    # distance to the source face
    src, out = G["SRC"], G["OUT"]
    fig = plt.figure(figsize=(12.5, 5.0))
    ax = fig.add_subplot(1, 2, 1, projection="3d")
    ax.plot_wireframe(src[:, :, 0], src[:, :, 1], src[:, :, 2], color=MUTED, lw=0.5)
    cmap = plt.get_cmap("Blues")
    ax.plot_surface(out[:, :, 0], out[:, :, 1], out[:, :, 2], facecolors=cmap(0.15 + 0.85 * d / 10), lw=0, alpha=0.9, shade=False)
    ext = np.ptp(np.vstack([src.reshape(-1, 3), out.reshape(-1, 3)]), axis=0)
    ax.set_box_aspect(ext)
    ax.view_init(18, -25)
    ax.set_axis_off()
    ax.set_title("P2: source cylinder patch (grey net), pulled sheet\ncoloured by distance from it", fontsize=10)
    a2 = fig.add_subplot(1, 2, 2)
    im = a2.imshow(d.T, origin="lower", extent=(0, 1, 0, 1), cmap="Blues", vmin=0, vmax=10)
    for i in range(5):
        for j in range(5):
            locked = i in (0, 1, 3, 4) or j in (0, 1, 3, 4)
            a2.plot(i / 4, j / 4, "o", ms=9, mfc="#ffffff" if locked else ORANGE, mec=INK)
    a2.text(0.5, 0.56, "+10 mm", ha="center", color=ORANGE, fontsize=9)
    a2.set_title("Distance from the source (mm) over the result's (u, v)\nhandles: white = locked by G1, orange = free", fontsize=10)
    a2.set_xlabel("u")
    a2.set_ylabel("v")
    a2.grid(False)
    fig.colorbar(im, ax=a2, fraction=0.046)
    save(fig, os.path.join(OUT, "fig07_pull_surface.png"))


if __name__ == "__main__":
    for fn in (fig01, fig02, fig03, fig04, fig05, fig06, fig07):
        fn()
