"""Figures for docs/explainers/solvers/solvers_explained.md and the Iterative Solve / Part Volume decks.

fig01 uses the three Stray_Weight measurements verified live 2026-09-22 (flat length 5 / 20 / 21 mm -> mass
10.18 / 12.57 / 12.73 g); between them the curve is interpolated (illustrative), and the solver steps drawn are the
ones Iterative Solve's "Start from current" would take on it (secant from x0 and x0 + 5 %, then Brent once bracketed).
fig02 is a schematic of what happens to each trial's bodies.
usage (repo root): python docs/explainers/solvers/img/src/figs.py
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "..", "tooling"))
from figstyle import *  # noqa: F401,F403,E402

OUT = os.path.join(HERE, "..")
XS = np.array([5.0, 20.0, 21.0])
MS = np.array([10.18, 12.57, 12.73])


def mass(x):
    x = np.asarray(x, dtype=float)
    lo = MS[0] + (x - XS[0]) * (MS[1] - MS[0]) / (XS[1] - XS[0])
    hi = MS[2] + (x - XS[2]) * 0.16
    return np.where(x < XS[0], lo, np.where(x > XS[2], hi, np.interp(x, XS, MS)))


def fig01():
    target = 11.5
    x0 = 20.0
    xs = [x0, x0 * 1.05]
    for _ in range(1):
        a, b = xs[-2], xs[-1]
        ra, rb = mass(a) - target, mass(b) - target
        if abs(rb) < 1e-4:
            break
        xs.append(b - rb * (b - a) / (rb - ra))
    fig, ax = plt.subplots(figsize=(10, 4.6))
    g = np.linspace(2, 24, 200)
    ax.plot(g, mass(g), color=INK2, lw=2, label="mass of created solids (illustrative between measured points)")
    ax.plot(XS, MS, "s", color=INK, ms=6, label="measured live (5, 20, 21 mm)")
    ax.axhline(target, color=RED, lw=1.2, ls="--")
    ax.text(2.2, target + 0.08, "target 11.5 g", color=RED, fontsize=9)
    for i, x in enumerate(xs):
        y = mass(x)
        ax.plot(x, y, "o", color=BLUE if i < 2 else ORANGE, ms=8, zorder=5)
        ax.text(x + 0.3, y - 0.22, "trial %d" % i, fontsize=9, color=BLUE if i < 2 else ORANGE)
    ax.plot([xs[0], xs[2]], [mass(xs[0]), target], color=ORANGE, lw=1, ls=":")
    ax.text(2.2, 12.95, "trial 0 = current value (20 mm), trial 1 = +5 %;\na secant through them lands trial 2 on 11.5 g (the relation is nearly linear);\nfurther secant steps, then Brent once bracketed, when it is not",
            fontsize=9, color=INK2)
    ax.set_xlabel("#flat_length (mm)")
    ax.set_ylabel("mass (g)")
    ax.legend(loc="lower right", fontsize=8.5)
    ax.set_title("Iterative Solve, Reach target from current: each trial re-runs the listed features")
    save(fig, os.path.join(OUT, "fig01_secant.png"))


def fig02():
    fig, ax = plt.subplots(figsize=(10.5, 3.2))
    bare(ax, equal=False)
    ax.set_xlim(-0.5, 10.5)
    ax.set_ylim(-1.5, 2.5)
    rows = [("originals", [0], DROP, "the listed features: deleted once a trial is accepted"),
            ("trial 0  x = 20", [1.8], DROP, "rejected: bodies deleted"),
            ("trial 1  x = 21", [3.6], DROP, "rejected: bodies deleted"),
            ("trial 2  x = 13.3", [5.4], KEEP, "accepted: kept, #flat_length = 13.3")]
    for i, (lab, xs, col, note) in enumerate(rows):
        y = 1.8 - i * 0.95
        ax.add_patch(FancyBboxPatch((xs[0], y - 0.3), 1.5, 0.6, boxstyle="round,pad=0.02,rounding_size=0.1", fc=col, ec=INK2))
        ax.text(xs[0] + 0.75, y, lab, ha="center", va="center", fontsize=8.5)
        ax.text(7.3, y, note, va="center", fontsize=9, color=GREEN if col == KEEP else INK2)
    ax.text(-0.4, 2.3, "each trial: set the variable, re-run every listed feature under its own id, measure", fontsize=9)
    save(fig, os.path.join(OUT, "fig02_trials.png"))


if __name__ == "__main__":
    fig01()
    fig02()
