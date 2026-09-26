"""Figures for docs/explainers/small_tools/small_tools_explained.md and the Move Along Edge / Trim curve + decks.

Data in data/ was read from the smallTools document (read-only eval API, 2026-09-26,
docs/tooling/sample_feature_edges.py): "Move along edge tests" M7 / M8 / M15, "Trim curve + tests" T1 / T9.
usage (repo root): python docs/explainers/small_tools/img/src/figs.py
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "..", "tooling"))
from figstyle import *  # noqa: F401,F403,E402

OUT = os.path.join(HERE, "..")
MAE = json.load(open(os.path.join(HERE, "data", "mae.json")))
TCP = json.load(open(os.path.join(HERE, "data", "tcp.json")))
PIECE = [BLUE, ORANGE, GREEN, VIOLET, MAGENTA]


def pts(e, dx=0.0, dy=0.0):
    p = np.array(e["pts"])
    p[:, 0] -= dx
    p[:, 1] -= dy
    return p


def cube_edges(ax, key, dx, col, lw=1.4, three=True):
    for e in MAE[key]:
        p = pts(e, dx)
        if three:
            ax.plot(p[:, 0], p[:, 1], p[:, 2], color=col, lw=lw)
        else:
            ax.plot(p[:, 0], p[:, 1], color=col, lw=lw)


# ---------------------------------------------------------------- fig01 orientation modes
def fig01():
    fig = plt.figure(figsize=(12, 5))
    for k, (path, cube, dx, title) in enumerate((("M7 path*", "M7 cube at (7000, 0, 30)", 7000, "Frenet (M7): follows the curvature normal -\nflips at the inflection, the block ends below (z = -30)"),
                                                  ("M8 path*", "M8 cube at (8000, 0, 30)", 8000, "Transported (M8): no twist -\nthe block stays on top (z = +30)"))):
        ax = fig.add_subplot(1, 2, k + 1, projection="3d")
        for e in MAE[path]:
            p = pts(e, dx)
            ax.plot(p[:, 0], p[:, 1], p[:, 2], color=INK, lw=2.2)
        # the start block: the moved block translated back to its start pose is not stored; draw its start box
        s = 10.0
        for a, b in [((-s, -s), (s, -s)), ((s, -s), (s, s)), ((s, s), (-s, s)), ((-s, s), (-s, -s))]:
            for z in (20, 40):
                ax.plot([a[0], b[0]], [a[1], b[1]], [z, z], color=MUTED, lw=1.2)
        for cx, cy in ((-s, -s), (s, -s), (s, s), (-s, s)):
            ax.plot([cx, cx], [cy, cy], [20, 40], color=MUTED, lw=1.2)
        cube_edges(ax, cube, dx, BLUE, lw=1.8)
        ax.text(0, -25, 45, "start", color=INK2, fontsize=9)
        ax.text(215, 200, 48 if k else -52, "end", color=BLUE, fontsize=9)
        ax.set_box_aspect((230, 230, 120))
        ax.set_zlim(-60, 60)
        ax.view_init(22, -70)
        ax.set_title(title, fontsize=10)
        ax.set_xticks([0, 100, 200])
        ax.set_yticks([0, 100, 200])
        ax.set_zticks([-30, 0, 30])
    save(fig, os.path.join(OUT, "fig01_orientation.png"))


# ---------------------------------------------------------------- fig02 nearest point + copies
def fig02():
    fig, ax = plt.subplots(figsize=(10.5, 3.4))
    for e in MAE["M15 path*"]:
        p = pts(e, 15000)
        ax.plot(p[:, 0], p[:, 1], color=INK, lw=2)
    ax.add_patch(Rectangle((-10, -10), 20, 20, fc="none", ec=MUTED, lw=1.4, ls="--"))
    ax.text(0, -22, "start", ha="center", fontsize=9, color=INK2)
    bodies = {}
    for e in MAE["M15 nearest*"]:
        bodies.setdefault(e["body"], []).append(pts(e, 15000))
    for b, es in bodies.items():
        for p in es:
            ax.plot(p[:, 0], p[:, 1], color=BLUE, lw=1.4)
    for x, y, t in ((250, 60, "target 1"), (320, -40, "target 2")):
        ax.plot(x, y, "o", color=RED, ms=6)
        ax.plot([x, x], [y, 0], color=RED, lw=1, ls=":")
        ax.text(x + 6, y, t, color=RED, fontsize=9, va="center")
    ax.annotate("", xy=(50, 16), xytext=(0, 16), arrowprops=dict(arrowstyle="->", color=ORANGE))
    ax.text(25, 19, "copy at distance 50", ha="center", color=ORANGE, fontsize=8.5)
    ax.set_aspect("equal")
    ax.set_xlim(-30, 420)
    ax.set_ylim(-55, 75)
    ax.set_xlabel("x (mm)")
    ax.set_title("M15: main move to the point nearest target 1, copies nearest target 2 and at 50 mm -> x = 250, 320, 50")
    save(fig, os.path.join(OUT, "fig02_nearest_copies.png"))


# ---------------------------------------------------------------- fig03 Trim curve + cut modes
def pieces(ax, src_key, out_key, dx):
    for e in TCP[src_key]:
        p = pts(e, dx)
        ax.plot(p[:, 0], p[:, 1], color=MUTED, lw=7, alpha=0.45, solid_capstyle="butt")
    for e in TCP[out_key]:
        p = pts(e, dx)
        ax.plot(p[:, 0], p[:, 1], color=PIECE[e["body"] % len(PIECE)], lw=2.4)
    ends = {}
    for e in TCP[out_key]:
        for q in (e["pts"][0], e["pts"][-1]):
            k = (round(q[0] - dx, 3), round(q[1], 3))
            ends[k] = ends.get(k, 0) + 1
    return ends


def fig03():
    fig, (a1, a2) = plt.subplots(1, 2, figsize=(12.5, 4.8))
    pieces(a1, "T1 wire sketch*", "T1 split*", 0)
    for x, y in ((100, -6), (300, 200)):
        a1.plot(x, y, "o", color=RED, ms=6)
    a1.plot([100, 100], [-6, 0], color=RED, ls="--", lw=1.2)
    a1.text(106, -14, "pick 6 mm off the curve:\nprojected onto it", color=RED, fontsize=8.5, va="top")
    a1.text(40, 12, "100", color=PIECE[0], fontsize=9)
    a1.text(190, 60, "357.08", color=PIECE[1], fontsize=9)
    a1.text(300, 245, "100", color=PIECE[2], fontsize=9)
    a1.set_title("At points (T1): split at 2 points -> 3 wires", fontsize=10)
    pieces(a2, "T9 wire sketch*", "T9 split*", 8000)
    a2.plot(100, 0, "s", color=INK, ms=7)
    a2.text(100, -16, "from point", ha="center", fontsize=8.5)
    arrow(a2, (100, 8), (140, 8), col=BLUE, lw=1.5, ms=10)
    a2.text(120, 13, "+", color=BLUE, fontsize=10, ha="center")
    a2.set_title("Distance from point (T9): 50, extra 150 and -80 -> 4 wires 20 / 130 / 100 / 307.08", fontsize=10)
    for ax in (a1, a2):
        ax.set_aspect("equal")
        ax.set_xlim(-20, 360)
        ax.set_ylim(-40, 280)
        ax.set_xlabel("x (mm)")
    fig.text(0.5, 0.005, "grey: the source (line, R100 arc, line)   colours: the pieces", ha="center", fontsize=9, color=INK2)
    save(fig, os.path.join(OUT, "fig03_trim_curve_plus.png"))


if __name__ == "__main__":
    for fn in (fig01, fig02, fig03):
        fn()
