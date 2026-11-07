"""Unwrap sheet figures (schematics, no live data). Run from the repo root:
    python docs/sheets/unwrap/figs.py
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "tooling"))
from figstyle import (plt, np, save, bare, FancyBboxPatch, BLUE, ORANGE, AQUA, VIOLET, RED, INK, INK2,  # noqa: E402
                      MUTED)

OUT = "docs/sheets/unwrap/img/"


def ref_curve(n=400):
    """A planar, cambered/rockered reference W: x rises steadily, z = profile."""
    x = np.linspace(0, 100, n)
    z = 0.0018 * (x - 15) ** 2 * (x < 15) + 0.0030 * (x - 82) ** 2 * (x > 82)
    z = z * 1.0
    z = z + 6 * np.exp(-((x - 50) / 38) ** 2) * 0  # keep W simple
    return x, z


def arc_len(x, z):
    return np.concatenate([[0], np.cumsum(np.hypot(np.diff(x), np.diff(z)))])


def fig_hero():
    x, z = ref_curve()
    # exaggerate the bend so it reads: rocker at both ends
    z = 0.0042 * np.clip(18 - x, 0, None) ** 2 + 0.0060 * np.clip(x - 78, 0, None) ** 2
    s = arc_len(x, z)
    dx, dz = np.gradient(x), np.gradient(z)
    tx, tz = dx / np.hypot(dx, dz), dz / np.hypot(dx, dz)
    nx, nz = -tz, tx                    # surface normal (up)
    t = 4.0                             # plate thickness (schematic)
    d = 0.0
    fig, (a, b) = plt.subplots(2, 1, figsize=(8.6, 3.5), gridspec_kw={"height_ratios": [1.1, 0.6], "hspace": 0.28})
    bare(a, equal=False)
    bare(b, equal=False)
    # wrapped plate: mid-surface at height h above W, thickness band
    h = 7.0
    for k, (lo, hi, c) in enumerate([(h - t / 2, h + t / 2, "#cfe0f6")]):
        xs = np.concatenate([x + nx * lo, (x + nx * hi)[::-1]])
        zs = np.concatenate([z + nz * lo, (z + nz * hi)[::-1]])
        a.fill(xs, zs, color=c, ec=BLUE, lw=1.2)
    a.plot(x, z, color=INK, lw=2.2)
    a.text(52, -3.2, "W: wrapped reference (one planar tangent chain)", ha="center", fontsize=9, color=INK)
    # normal ticks
    for i in range(0, len(x), 40):
        a.plot([x[i], x[i] + nx[i] * (h + 2)], [z[i], z[i] + nz[i] * (h + 2)], color=MUTED, lw=0.7, ls=":")
    a.text(28, 18.5, "wrapped part (plate, solid, edges or faces)", ha="center", fontsize=9, color=BLUE,
           fontweight="bold")
    a.annotate("alignment point\n(lands on the origin)", xy=(x[200], z[200]), xytext=(76, 21),
               fontsize=8.5, color=ORANGE, arrowprops=dict(arrowstyle="-|>", color=ORANGE, lw=1.2), ha="center")
    a.plot([x[200]], [z[200]], "o", color=ORANGE, ms=6)
    a.set_title("Before: wrapped along W (side view)", fontsize=10, loc="left")
    a.set_xlim(-3, 103)
    a.set_ylim(-6, 27)
    # flat
    L = s[-1]
    b.fill([0, L, L, 0], [h - t / 2, h - t / 2, h + t / 2, h + t / 2], color="#cfe0f6", ec=BLUE, lw=1.2)
    b.plot([0, L], [0, 0], color=INK, lw=2.2)
    b.plot([s[200]], [0], "o", color=ORANGE, ms=6)
    b.annotate("", xy=(0, -2.6), xytext=(L, -2.6), arrowprops=dict(arrowstyle="<->", color=AQUA, lw=1.6))
    b.text(L / 2, -5.4, "x = s - d * theta   (length along the offset curve you chose is kept)", ha="center",
           fontsize=9, color=AQUA, fontweight="bold")
    b.text(L / 2, h + 3.4, "flat part, laid on the origin plane", ha="center", fontsize=9, color=BLUE, fontweight="bold")
    b.set_title("After: flat, in the origin's frame", fontsize=10, loc="left")
    b.set_xlim(-3, L + 3)
    b.set_ylim(-8, 15)
    save(fig, OUT + "fig_hero.png")


def fig_modes():
    fig, axs = plt.subplots(1, 4, figsize=(8.8, 2.1))
    for ax in axs:
        bare(ax, equal=False)
        ax.set_xlim(0, 10)
        ax.set_ylim(0, 8)
    titles = ["Edges / wires", "Constant-thickness part", "Part (solid)", "Faces / surfaces"]
    for ax, t in zip(axs, titles):
        ax.set_title(t, fontsize=9)
    xs = np.linspace(0.5, 9.5, 60)
    # edges: a wavy curve
    axs[0].plot(xs, 4 + 1.4 * np.sin(xs * 0.9), color=BLUE, lw=2)
    axs[0].text(5, 0.8, "curve -> line / arc / spline", ha="center", fontsize=7.5, color=INK2)
    # plate: mid-surface band
    axs[1].fill_between(xs, 3.4 + 0.6 * np.sin(xs * 0.7), 4.2 + 0.6 * np.sin(xs * 0.7), color="#cfe0f6", ec=BLUE)
    axs[1].plot(xs, 3.8 + 0.6 * np.sin(xs * 0.7), color=VIOLET, lw=1.2, ls="--")
    axs[1].text(5, 0.8, "mid-surface flattened,\nplate rebuilt t/2 each side", ha="center", fontsize=7.5, color=INK2)
    # solid: rigid + rebuilt
    axs[2].fill_between([0.5, 3.5], 2.5, 5.5, color="#d8d6d0", ec=INK2)
    axs[2].fill_between([3.5, 9.5], 2.5, 5.5, color="#cfe0f6", ec=BLUE)
    axs[2].text(2, 4, "rigid", ha="center", fontsize=7.5)
    axs[2].text(6.5, 4, "rebuilt", ha="center", fontsize=7.5, color=BLUE)
    axs[2].text(5, 0.8, "straight spans move rigidly,\ncurved spans rebuilt", ha="center", fontsize=7.5, color=INK2)
    # faces: three faces sewn
    for i, c in enumerate(["#cfe0f6", "#d6efe4", "#fbe3d6"]):
        axs[3].fill([0.8 + 2.9 * i, 3.7 + 2.9 * i, 3.7 + 2.9 * i, 0.8 + 2.9 * i], [2.6, 2.6, 5.4, 5.4], color=c, ec=INK2)
    axs[3].text(5, 0.8, "edges once, faces rebuilt on\nthem, sewn into one sheet", ha="center", fontsize=7.5, color=INK2)
    save(fig, OUT + "fig_modes.png")


if __name__ == "__main__":
    fig_hero()

