"""Concept figures for curve_tools_explained.md (no Onshape data).
usage: python docs/explainers/curve_tools/img/src/figs.py
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "..", "tooling"))
from figstyle import *  # noqa: E402,F403

OUT = os.path.dirname(HERE)


# ---------------------------------------------------------------- fig01: joints and slivers (Clean wire)
def fig01():
    fig = plt.figure(figsize=(12, 5.2))
    ax = bare(fig.add_axes([0.04, 0.62, 0.92, 0.3]), equal=False)
    ax.set_xlim(-0.5, 12)
    ax.set_ylim(-1, 1.6)
    ax.plot([0, 11.5], [0, 0], color=INK2, lw=1.5)
    # not to scale: three bands of equal width
    bands = [(0, 3.5, KEEP, "tangent (< 0.57 deg)\nmerged into one run"), (3.5, 7, YELLOW, "near-tangent (< corner angle)\nmerged if 'make tangent' is on"),
             (7, 11.5, RED, "corner\nthe vertex is kept")]
    for a, b, col, lab in bands:
        ax.add_patch(Rectangle((a, -0.25), b - a, 0.5, color=col, alpha=0.35))
        ax.text((a + b) / 2, 0.55, lab, ha="center", va="bottom", fontsize=9)
    for x, lab in [(3.5, "0.57 deg"), (7, "corner angle\n(default 3 deg)")]:
        ax.plot([x, x], [-0.35, 0.35], color=INK, lw=1.5)
        ax.text(x, -0.45, lab, ha="center", va="top", fontsize=8)
    ax.text(11.6, 0, "turn at the joint", va="center", fontsize=9, color=INK2)
    ax.set_title("How Clean wire reads each joint", fontsize=11)

    cases = [("one corner, one tangent joint", [(0, 0), (4, 0), (4.4, 0.05), (8, 1.5)], "absorbed into the tangent side"),
             ("corners at both ends", [(0, 0), (4, 0), (4.4, 0.4), (8, 0.4)], "a chamfer: kept"),
             ("tangent both ends, neighbours line up", [(0, 0), (4, 0), (4.4, 0.0), (8, 0)], "absorbed")]
    for k, (title, pts, verdict) in enumerate(cases):
        a2 = bare(fig.add_axes([0.04 + k * 0.32, 0.06, 0.28, 0.42]))
        p = np.array(pts)
        a2.plot(p[:2, 0], p[:2, 1], color=BLUE, lw=2.5)
        a2.plot(p[1:3, 0], p[1:3, 1], color=RED, lw=4)
        a2.plot(p[2:, 0], p[2:, 1], color=BLUE, lw=2.5)
        a2.annotate("sliver < 0.5 mm", p[1] + (p[2] - p[1]) / 2, xytext=(p[1][0] - 1.5, 1.2), fontsize=8, color=RED,
                    arrowprops=dict(arrowstyle="-", color=RED, lw=0.8))
        a2.set_title(title + "\n-> " + verdict, fontsize=9)
        a2.set_xlim(-0.5, 8.5)
        a2.set_ylim(-0.8, 2)
    save(fig, os.path.join(OUT, "fig01_joints_slivers.png"))


# ---------------------------------------------------------------- fig02: adaptive sampling
def fig02():
    fig, axes = plt.subplots(1, 2, figsize=(12, 3.6), gridspec_kw={"width_ratios": [1.6, 1]})
    ax = bare(axes[0], equal=False)
    t = np.linspace(0, 1, 2000)
    x = t * 400
    y = 40 * np.exp(-((x - 250) / 30) ** 2)
    ax.plot(x, y, color=MUTED, lw=1.2)
    dy, ddy = np.gradient(y, x), np.gradient(np.gradient(y, x), x)
    kappa = np.abs(ddy) / (1 + dy ** 2) ** 1.5
    tol = 0.01
    spacing = np.clip(np.sqrt(4 * tol / np.maximum(kappa, 1e-9)), 0.05, 10)
    s = [0.0]
    arc = np.concatenate([[0], np.cumsum(np.hypot(np.diff(x), np.diff(y)))])
    while s[-1] < arc[-1]:
        k = np.searchsorted(arc, s[-1])
        s.append(s[-1] + spacing[min(k, len(spacing) - 1)])
    pts = np.array([[np.interp(v, arc, x), np.interp(v, arc, y)] for v in s[:-1]])
    ax.plot(pts[:, 0], pts[:, 1], "o", color=BLUE, ms=3)
    ax.set_title("Samples crowd where the curve bends (tolerance 0.01 mm)", fontsize=10)
    ax2 = axes[1]
    k = np.logspace(-4, 0, 100)
    ax2.loglog(1 / k, np.clip(np.sqrt(4 * tol / k), 0.05, 10), color=BLUE)
    ax2.set_xlabel("radius of curvature (mm)")
    ax2.set_ylabel("sample spacing (mm)")
    ax2.set_title("s = sqrt(4 tol / kappa), clamped 0.05 .. 10 mm", fontsize=10)
    save(fig, os.path.join(OUT, "fig02_adaptive_sampling.png"))


# ---------------------------------------------------------------- fig03: Map curve arc length
def fig03():
    fig, ax = plt.subplots(figsize=(12, 3.8))
    bare(ax)
    xs = np.linspace(-200, 1400, 400)
    from_y = np.zeros_like(xs) - 40
    to_y = 25 * np.sin(xs / 300) + 0.00004 * (xs - 600) ** 2
    ax.plot(xs, from_y, color=ORANGE, lw=2)
    ax.plot(xs, to_y, color=BLUE, lw=2)
    zero_x = 0

    def arc_points(ys, targets):
        arc = np.concatenate([[0], np.cumsum(np.hypot(np.diff(xs), np.diff(ys)))])
        a0 = np.interp(zero_x, xs, arc)
        return [(np.interp(a0 + tv, arc, xs), np.interp(a0 + tv, arc, ys)) for tv in targets]

    ticks = [0, 300, 600, 900, 1200]
    for (fx, fy), (tx, ty), tv in zip(arc_points(from_y, ticks), arc_points(to_y, ticks), ticks):
        ax.plot([fx, fx], [fy - 6, fy + 6], color=ORANGE, lw=2)
        ax.plot([tx, tx], [ty - 6, ty + 6], color=BLUE, lw=2)
        ax.plot([fx, tx], [fy + 6, ty - 6], ":", color=MUTED, lw=1)
        ax.text(tx, ty + 10, "%d" % tv, ha="center", fontsize=8, color=BLUE)
    ax.plot([zero_x, zero_x], [-80, 60], "--", color=RED, lw=1)
    ax.text(zero_x + 8, 66, "zero point (e.g. the FCP)", color=RED, fontsize=9)
    ax.text(-190, -60, "from-chain (e.g. RSL, straight)", color=ORANGE, fontsize=9)
    ax.text(1150, 45, "to-chain (e.g. the ski profile)", color=BLUE, fontsize=9)
    ax.set_title("Map curve: s mm along the from-chain from its zero lands s mm along the to-chain from its zero", fontsize=10)
    ax.set_ylim(-90, 80)
    save(fig, os.path.join(OUT, "fig03_map_arc_length.png"))


# ---------------------------------------------------------------- fig04: Merge curve outcomes
def fig04():
    fig, axes = plt.subplots(1, 3, figsize=(12, 3.2))
    specs = [("In place", "the seed is the only edge of its wire:\nthe wire is edited, keeps its id and name", [["seed", "merge"]], True),
             ("Rebuild", "the seed wire has other edges:\nthey + the merged curve become a new wire", [["a", "seed", "merge"]], False),
             ("Extract", "the seed is not on a wire:\nthe fitted spline is a new wire", [["seed", "merge"]], False)]
    for ax, (title, sub, chains, same) in zip(axes, specs):
        bare(ax)
        x = 0
        for name in chains[0]:
            col = BLUE if name in ("seed", "merge") else MUTED
            ax.plot([x, x + 1], [1, 1], color=col, lw=3)
            ax.text(x + 0.5, 1.12, name, ha="center", fontsize=8)
            x += 1
        ax.annotate("", xy=(x / 2, 0.45), xytext=(x / 2, 0.85), arrowprops=dict(arrowstyle="-|>", color=INK2))
        x2 = 0
        if "a" in chains[0]:
            ax.plot([0, 1], [0.3, 0.3], color=MUTED, lw=3)
            x2 = 1
        ax.plot([x2, x], [0.3, 0.3], color=KEEP, lw=3)
        ax.text((x2 + x) / 2, 0.12, "one fitted spline", ha="center", fontsize=8, color=KEEP)
        ax.set_title(title + "\n" + sub, fontsize=9)
        ax.set_xlim(-0.3, 3.3)
        ax.set_ylim(-0.1, 1.4)
    save(fig, os.path.join(OUT, "fig04_merge_outcomes.png"))


# ---------------------------------------------------------------- fig05: Evaluate profiles
def fig05():
    fig, axes = plt.subplots(1, 2, figsize=(12, 3.8))
    ax = bare(axes[0])
    t = np.linspace(0, 1, 300)
    x = 400 * t
    y = 20 * np.sin(t * 3)
    hx = 400 + 40 * np.sin(np.linspace(0, np.pi * 1.1, 80))
    hy = 20 * np.sin(3) + 40 * (1 - np.cos(np.linspace(0, np.pi * 1.1, 80)))
    ax.plot(x, y, color=BLUE, lw=2)
    ax.plot(hx, hy, color=MUTED, lw=2, ls="--")
    k = np.argmax(hx)
    ax.plot(hx[:k + 1], hy[:k + 1], color=BLUE, lw=2)
    ax.plot(hx[k], hy[k], "o", color=RED)
    ax.text(hx[k] + 6, hy[k], "cut: the projection\nturns back here", color=RED, fontsize=9, va="center")
    ax.set_title("Edges: the projected chain is cut where it doubles back", fontsize=10)
    ax.set_xlim(-10, 560)
    ax2 = bare(axes[1], equal=False)
    xs = np.linspace(0, 1000, 200)
    top = 60 + 8 * np.cos(xs / 1000 * np.pi * 2)
    bot = 5 * np.sin(xs / 1000 * np.pi)
    ax2.plot(xs, top, color=KEEP, lw=3)
    ax2.plot(xs, bot, color=RED, lw=3)
    ax2.plot(xs, (top + bot) / 2, color=BLUE, lw=2, ls="--")
    for xe in (0, 1000):
        ax2.plot([xe, xe], [np.interp(xe, xs, bot), np.interp(xe, xs, top)], color=MAGENTA, lw=3)
    ax2.text(1010, 68, "top", color=KEEP)
    ax2.text(1010, 0, "bottom", color=RED)
    ax2.text(1010, 34, "middle", color=BLUE)
    ax2.text(-40, 30, "connector", color=MAGENTA, ha="right")
    ax2.set_title("Part: the outline split along the prevailing direction", fontsize=10)
    ax2.set_xlim(-200, 1150)
    ax2.set_ylim(-20, 90)
    save(fig, os.path.join(OUT, "fig05_evaluate_profiles.png"))


# ---------------------------------------------------------------- fig06: Fillet wire
def fig06():
    fig, axes = plt.subplots(1, 2, figsize=(12, 4))
    ax = bare(axes[0])
    r, th = 10, np.radians(70)
    d = r * np.tan(th / 2)
    A = np.array([-40.0, 0.0])
    V = np.array([0.0, 0.0])
    u = np.array([np.cos(th), np.sin(th)])
    B = V + 40 * u
    ax.plot(*zip(A, V), color=MUTED, lw=2, ls="--")
    ax.plot(*zip(V, B), color=MUTED, lw=2, ls="--")
    T1 = V + np.array([-d, 0])
    T2 = V + d * u
    C = T1 + np.array([0, r])
    ang = np.linspace(-np.pi / 2, np.arctan2(T2[1] - C[1], T2[0] - C[0]), 50)
    ax.plot(*zip(A, T1), color=BLUE, lw=2.5)
    ax.plot(C[0] + r * np.cos(ang), C[1] + r * np.sin(ang), color=MAGENTA, lw=3)
    ax.plot(*zip(T2, B), color=BLUE, lw=2.5)
    ax.plot(*C, "+", color=INK2, ms=10)
    ax.plot([C[0], T1[0]], [C[1], T1[1]], ":", color=INK2)
    ax.text(C[0] - 7, C[1] - 5, "r", color=INK2)
    ax.annotate("", xy=V, xytext=T1, arrowprops=dict(arrowstyle="<->", color=ORANGE))
    ax.text((T1[0] + V[0]) / 2, -3.5, "setback d = r tan(turn / 2)", color=ORANGE, fontsize=8, ha="center")
    ax.set_title("Tangent: an exact arc (a sketch arc, so Onshape shows its radius)", fontsize=10)
    ax2 = axes[1]
    s = np.linspace(-1, 1, 400)
    arc = np.where(np.abs(s) < 0.5, 1.0, 0.0)
    blend = np.clip(1 - (np.abs(s) / 0.75) ** 2, 0, None) * 1.5
    ax2.plot(s, arc, color=MAGENTA, lw=2, label="Tangent: curvature jumps to 1/r")
    ax2.plot(s, blend, color=BLUE, lw=2, label="Curvature: rises and falls smoothly (G2), average 1/r")
    ax2.set_xlabel("along the corner")
    ax2.set_ylabel("curvature (x 1/r)")
    ax2.legend(fontsize=8, loc="upper right")
    ax2.set_title("Tangent arc vs curvature blend", fontsize=10)
    save(fig, os.path.join(OUT, "fig06_fillet_wire.png"))


if __name__ == "__main__":
    fig01()
    fig02()
    fig03()
    fig04()
    fig05()
    fig06()
