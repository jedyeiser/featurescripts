"""Shared look for every docs/ figure (matplotlib, PNG, white surface). Same palette as
driven_offset/docs/img/src/style.py so explainers read as one set.

usage in a figure script:
    import sys, os; sys.path.insert(0, <repo>/docs/tooling)
    from figstyle import *          # plt, np, colours, save(fig, path), bare(ax), arrow(ax, p, q)
"""
import os

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402,F401
import numpy as np  # noqa: E402,F401
from matplotlib.patches import FancyArrowPatch, Polygon, Circle, FancyBboxPatch, Rectangle  # noqa: E402,F401

BLUE, ORANGE, AQUA, YELLOW, MAGENTA, GREEN, VIOLET, RED = (
    "#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#4a3aa7", "#e34948")
INK, INK2, MUTED, GRID = "#0b0b0b", "#52514e", "#9a9893", "#e4e2dc"
SURFACE = "#ffffff"
KEEP, DROP = "#1baf7a", "#d9d6cf"      # kept geometry / discarded geometry

plt.rcParams.update({
    "figure.facecolor": SURFACE, "axes.facecolor": SURFACE, "savefig.facecolor": SURFACE,
    "font.size": 10, "axes.titlesize": 11, "axes.titleweight": "bold", "axes.labelsize": 10,
    "axes.edgecolor": MUTED, "axes.labelcolor": INK2, "xtick.color": INK2, "ytick.color": INK2,
    "text.color": INK, "axes.grid": True, "grid.color": GRID, "grid.linewidth": 0.6,
    "axes.spines.top": False, "axes.spines.right": False, "lines.linewidth": 2,
    "legend.frameon": False, "figure.dpi": 110,
})


def save(fig, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    fig.savefig(path, dpi=150, bbox_inches="tight")
    plt.close(fig)
    print("wrote", path)


def bare(ax, equal=True):
    """Axes for a diagram: no grid, ticks or spines."""
    ax.grid(False)
    ax.set_xticks([])
    ax.set_yticks([])
    for s in ax.spines.values():
        s.set_visible(False)
    if equal:
        ax.set_aspect("equal")
    return ax


def arrow(ax, p, q, col=INK2, lw=1.4, style="-|>", ms=12, **kw):
    ax.add_patch(FancyArrowPatch(p, q, arrowstyle=style, mutation_scale=ms, color=col, lw=lw, **kw))


def normals(ax, xs, ys, nx, ny, col=MUTED, length=8, every=1):
    """Short normal arrows along a polyline."""
    for i in range(0, len(xs), every):
        arrow(ax, (xs[i], ys[i]), (xs[i] + length * nx[i], ys[i] + length * ny[i]), col=col, lw=1.0, ms=8)
