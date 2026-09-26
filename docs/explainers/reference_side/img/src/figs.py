"""Figures for reference_side_explained.md (concept diagrams, no Onshape data).
usage: python docs/explainers/reference_side/img/src/figs.py
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "..", "tooling"))
from figstyle import *  # noqa: E402,F403

OUT = os.path.dirname(HERE)


def ref_point(ax, p, label="reference", col=RED, dx=6, dy=4):
    ax.plot(*p, "o", color=col, ms=8, zorder=5)
    ax.text(p[0] + dx, p[1] + dy, label, color=col, fontsize=9)


# ---------------------------------------------------------------- fig01: the flip problem
def fig01():
    fig, axes = plt.subplots(1, 3, figsize=(12, 3.6))
    x = np.linspace(0, 100, 60)
    y = 18 * np.sin(x / 100 * np.pi)
    dy = np.gradient(y, x)
    nx, ny = -dy / np.hypot(1, dy), 1 / np.hypot(1, dy)
    P = (50, -22)
    titles = ["Built-in, normal up:\n'offset along normal' goes up",
              "Same model after an upstream change\nflips the normal: now it goes DOWN",
              "Offset+ 'toward reference':\ngoes toward the point, either way"]
    for i, ax in enumerate(axes):
        bare(ax)
        sgn = -1 if i == 1 else 1
        ax.plot(x, y, color=BLUE, lw=2.5)
        normals(ax, x, y, sgn * nx, sgn * ny, every=10, length=9)
        if i < 2:
            off = 10 * sgn
            ax.plot(x + off * nx, y + off * ny, color=ORANGE, lw=2)
            ax.text(2, 30, "offset", color=ORANGE)
        else:
            ax.plot(x - 10 * nx, y - 10 * ny, color=KEEP, lw=2.5)
            ref_point(ax, P)
            ax.text(2, 30, "offset toward reference", color=KEEP)
        ax.set_title(titles[i], fontsize=10)
        ax.set_xlim(-5, 110)
        ax.set_ylim(-30, 38)
    fig.suptitle("A flip names a side relative to a normal; a reference names it in the model", fontweight="bold")
    save(fig, os.path.join(OUT, "fig01_flip_vs_reference.png"))


# ---------------------------------------------------------------- fig02: signed distance
def fig02():
    fig, axes = plt.subplots(1, 2, figsize=(10, 3.8), gridspec_kw={"wspace": 0.25})
    x = np.linspace(-60, 60, 80)
    y = 0.004 * x ** 2
    P = np.array([15.0, 32.0])
    for i, ax in enumerate(axes):
        bare(ax)
        sgn = 1 if i == 0 else -1
        ax.plot(x, y, color=BLUE, lw=2.5)
        # closest point: brute force on the polyline
        k = np.argmin(np.hypot(x - P[0], y - P[1]))
        C = np.array([x[k], y[k]])
        t = np.array([1, 0.008 * x[k]]); t /= np.linalg.norm(t)
        n = sgn * np.array([-t[1], t[0]])
        arrow(ax, C, C + 18 * n, col=VIOLET, lw=2)
        ax.text(*(C + 20 * n + [2, 0]), "n", color=VIOLET, fontsize=11)
        ax.plot([C[0], P[0]], [C[1], P[1]], "--", color=INK2, lw=1)
        ax.plot(*C, "s", color=INK2, ms=5)
        ax.text(C[0] + 3, C[1] - 7, "closest point C", color=INK2, fontsize=8)
        ref_point(ax, P, "reference P")
        s = np.dot(P - C, n)
        ax.set_title("Normal %s\ns = (P - C) . n = %+.0f  ->  reference on the %s" %
                     ("up" if sgn > 0 else "down", s, "FRONT" if s > 0 else "BACK"), fontsize=10)
        ax.set_xlim(-65, 65)
        ax.set_ylim(-25, 45)
    fig.suptitle("The side is read, not assumed: the sign of s says where the reference is for THIS normal",
                 fontweight="bold", y=1.04)
    save(fig, os.path.join(OUT, "fig02_signed_distance.png"))


# ---------------------------------------------------------------- fig03: Split+ regions
def fig03():
    fig, axes = plt.subplots(1, 2, figsize=(12, 3.8), gridspec_kw={"width_ratios": [1.5, 1]})
    ax = bare(axes[0])
    xs = np.linspace(0, 1800, 200)
    half = 55 + 12 * np.cos(2 * np.pi * (xs - 900) / 1800) ** 2
    ax.fill_between(xs, -half, half, color=GRID)
    cuts = [300, 900, 1500]
    names = ["start", "middle1", "middle2", "end"]
    bounds = [0] + cuts + [1800]
    cols = [AQUA, YELLOW, ORANGE, VIOLET]
    for i in range(4):
        m = (xs >= bounds[i]) & (xs <= bounds[i + 1])
        ax.fill_between(xs[m], -half[m], half[m], color=cols[i], alpha=0.35)
        ax.text((bounds[i] + bounds[i + 1]) / 2, 0, names[i], ha="center", va="center", fontsize=10, fontweight="bold")
    flips = [1, -1, 1]
    for k, c in enumerate(cuts):
        ax.plot([c, c], [-95, 95], color=INK, lw=1.8)
        ax.text(c, 100, "tool %d" % (k + 1), ha="center", fontsize=9)
        arrow(ax, (c, -85), (c + 90 * flips[k], -85), col=MUTED, lw=1.2, ms=9)
        ax.text(c + 95 * flips[k], -95, "normal", color=MUTED, fontsize=7, ha="left" if flips[k] > 0 else "right")
    ax.set_title("Tools picked in order along the part -> regions named by position (normals don't matter)", fontsize=10)
    ax.set_xlim(-50, 1850)
    ax.set_ylim(-110, 115)

    ax = bare(axes[1])
    ax.add_patch(Rectangle((0, 0), 100, 100, color=GRID))
    ax.plot([-10, 110], [20, 90], color=INK, lw=1.8)
    ax.plot([30, 70], [-10, 110], color=INK, lw=1.8)
    ax.text(50, -25, "crossing tools: the split is made,\nregion keys published EMPTY + info notice",
            ha="center", fontsize=9, color=INK2)
    ax.set_xlim(-15, 115)
    ax.set_ylim(-40, 115)
    ax.set_title("No bands -> no regions", fontsize=10)
    save(fig, os.path.join(OUT, "fig03_split_regions.png"))


# ---------------------------------------------------------------- fig04: Mutual Trim+ and correction 28
def fig04():
    fig, axes = plt.subplots(1, 2, figsize=(11, 4.2))
    # left: plain mutual trim with a reference
    ax = bare(axes[0])
    ax.plot([-60, 0], [0, 0], color=KEEP, lw=3)
    ax.plot([0, 60], [0, 0], color=DROP, lw=3)
    ax.plot([0, 0], [0, 60], color=KEEP, lw=3)
    ax.plot([0, 0], [-60, 0], color=DROP, lw=3)
    ref_point(ax, (-30, 30), "reference")
    ax.text(-58, -12, "first surface", color=INK2, fontsize=9)
    ax.text(4, 52, "second surface", color=INK2, fontsize=9)
    ax.set_title("Each surface keeps the side the reference is on\n(green kept, grey removed)", fontsize=10)
    ax.set_xlim(-70, 75)
    ax.set_ylim(-70, 70)
    # right: leaning wall -- nearest piece is the wrong answer
    ax = bare(axes[1])
    ax.plot([-70, 70], [0, 0], color=BLUE, lw=2.5)
    ax.text(40, 4, "splitter B", color=BLUE, fontsize=9)
    top = np.array([[0, 0], [55, 55]])
    bot = np.array([[0, 0], [-25, -25]])
    ax.plot(*top.T, color=DROP, lw=3)
    ax.plot(*bot.T, color=KEEP, lw=3)
    P = np.array([38.0, -12.0])
    ref_point(ax, P, "reference (below B)", dx=-10, dy=-10)
    ax.plot([P[0], 12.5], [P[1], 12.5], ":", color=RED, lw=1.2)
    ax.text(26, 12, "nearest piece\n= the UPPER one", color=RED, fontsize=8)
    ax.text(-65, -40, "Decided instead by the SIGN against B:\nreference below B -> keep the piece below B",
            fontsize=9, color=INK2)
    ax.set_title("Why 'nearest piece' fails on a leaning wall (correction 28)", fontsize=10)
    ax.set_xlim(-72, 72)
    ax.set_ylim(-50, 62)
    save(fig, os.path.join(OUT, "fig04_mutual_trim.png"))


# ---------------------------------------------------------------- fig05: Offset+ curve frames + corners
def fig05():
    fig = plt.figure(figsize=(13, 4.2))
    # transport frame on a helix
    ax = fig.add_subplot(1, 3, 1, projection="3d")
    t = np.linspace(0, 4 * np.pi, 300)
    R, H = 50, 80
    x, y, z = R * np.cos(t), R * np.sin(t), H * t / (4 * np.pi)
    ax.plot(x, y, z, color=BLUE, lw=2)
    ax.plot((R - 10) * np.cos(t), (R - 10) * np.sin(t), z, color=KEEP, lw=2)
    ax.plot([0, 0], [0, 0], [0, H], color=RED, lw=1, ls="--")
    ax.set_title("Transport: toward the axis all the way\n(C7: 10 mm inside a r50 helix)", fontsize=10)
    ax.set_xticks([]); ax.set_yticks([]); ax.set_zticks([])
    ax.set_box_aspect((1, 1, 0.8))
    # surface normal vs tangent
    ax = bare(fig.add_subplot(1, 3, 2))
    ax.add_patch(Polygon([[-60, -20], [40, -20], [70, 20], [-30, 20]], color=GRID))
    ax.plot([-40, 45], [0, 0], color=BLUE, lw=2.5)
    ax.plot([-40, 45], [30, 30], color=VIOLET, lw=2)
    ax.plot([-40, 45], [-12, -12], color=ORANGE, lw=2)
    ax.text(48, 29, "Along surface normal\n(lifted off it)", color=VIOLET, fontsize=8, va="center")
    ax.text(48, -12, "Along surface\n(in its tangent plane)", color=ORANGE, fontsize=8, va="center")
    ax.set_title("Surface-normal frame: two directions", fontsize=10)
    ax.set_xlim(-65, 115)
    ax.set_ylim(-30, 45)
    # corners: Z chain C8
    ax = bare(fig.add_subplot(1, 3, 3))
    chain = np.array([[0, 0], [100, 0], [100, 100], [200, 100]])
    ax.plot(*chain.T, color=BLUE, lw=2.5)
    d = 10
    ax.plot([0, 100], [-d, -d], color=KEEP, lw=2)
    th = np.linspace(-np.pi / 2, 0, 30)
    ax.plot(100 + d * np.cos(th), d * np.sin(th), color=MAGENTA, lw=3)
    ax.plot([110, 110], [0, 90], color=KEEP, lw=2)
    ax.plot([110, 200], [90, 90], color=KEEP, lw=2)
    ax.plot([110, 110], [90, 100], ":", color=MUTED)
    ax.plot([100, 110], [90, 90], ":", color=MUTED)
    ax.text(118, 8, "gap -> arc\n(radius = distance)", color=MAGENTA, fontsize=8)
    ax.text(118, 70, "overlap -> trim\nto the crossing", color=KEEP, fontsize=8)
    ref_point(ax, (50, -30), "reference", dx=6, dy=-4)
    ax.set_title("Corners (C8: 1 rounded + 1 trimmed,\nlength 280 + 5 pi = 295.708)", fontsize=10)
    ax.set_xlim(-10, 215)
    ax.set_ylim(-40, 110)
    save(fig, os.path.join(OUT, "fig05_offset_curves.png"))


# ---------------------------------------------------------------- fig06: Thicken+
def fig06():
    fig, axes = plt.subplots(1, 2, figsize=(11, 3.8))
    ax = bare(axes[0])
    x = np.linspace(-60, 60, 80)
    for y0, sgn, label in [(25, 1, "normal up"), (-25, -1, "normal down")]:
        toward = -1 if y0 > 0 else 1      # the reference sits at y = 0
        ax.fill_between(x, y0, y0 + 5 * toward, color=KEEP, alpha=0.6)
        ax.fill_between(x, y0, y0 - 3 * toward, color=AQUA, alpha=0.3)
        ax.plot(x, 0 * x + y0, color=BLUE, lw=2)
        for xi in (-45, 45):
            arrow(ax, (xi, y0), (xi, y0 + 9 * sgn), col=MUTED, lw=1, ms=8)
        ax.text(62, y0, label, fontsize=8, color=MUTED, va="center")
    ref_point(ax, (0, 0), "reference", dx=4, dy=-8)
    ax.text(-60, 42, "Toward = 5 (green), Away = 3 (light): both sheets\nthicken toward the reference, whatever their normals",
            fontsize=9, color=INK2)
    ax.set_xlim(-65, 95)
    ax.set_ylim(-40, 55)
    ax.set_title("Toward / away decided per surface body", fontsize=10)
    ax = bare(axes[1])
    th = np.linspace(np.pi * 1.1, np.pi * 1.9, 60)
    r = 10
    ax.plot(r * np.cos(th), r * np.sin(th) + 10, color=BLUE, lw=2.5)
    for t_off, col, lab in [(6, KEEP, "t = 6 < r: fine"), (14, RED, "t = 14 > r: passes the centre\nand turns inside out (folds)")]:
        rr = r - t_off
        ax.plot(rr * np.cos(th), rr * np.sin(th) + 10, color=col, lw=2)
        ax.text(12, 10 - t_off * 0.6, lab, color=col, fontsize=9)
    ax.plot(0, 10, "+", color=INK2, ms=10)
    ax.text(1, 11.5, "centre", fontsize=8, color=INK2)
    ax.plot([0, r * np.cos(np.pi * 1.5)], [10, 10 + r * np.sin(np.pi * 1.5)], ":", color=BLUE, lw=1)
    ax.text(-4.5, 4, "r = 10", color=BLUE, fontsize=9)
    ax.text(-13, -3, "surface (blue), thickened toward its concave side", color=INK2, fontsize=8)
    ax.set_title("Curvature check: concave radius < thickness -> error with its location", fontsize=10)
    ax.set_xlim(-14, 30)
    ax.set_ylim(-4, 16)
    save(fig, os.path.join(OUT, "fig06_thicken.png"))


if __name__ == "__main__":
    fig01()
    fig02()
    fig03()
    fig04()
    fig05()
    fig06()
