"""Measure curve distance sheet figures (schematics, no live data). Run from the repo root:
    python docs/sheets/measure_curve_distance/figs.py
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "tooling"))
from figstyle import plt, np, save, bare, BLUE, ORANGE, AQUA, VIOLET, INK, INK2, MUTED  # noqa: E402

OUT = "docs/sheets/measure_curve_distance/img/"


def curves():
    x = np.linspace(0, 10, 200)
    frm = 2.2 + 0.45 * np.sin(x * 0.7)
    to = 5.0 + 0.4 * np.sin(x * 0.45 + 1.0) + 0.1 * x
    return x, frm, to


def fig_modes():
    x, frm, to = curves()
    fig, axs = plt.subplots(1, 3, figsize=(9.0, 2.7))
    names = ["Closest", "Normal dist (default)", "Along axis (here World Z)"]
    sx = np.linspace(0.8, 9.2, 7)
    for k, ax in enumerate(axs):
        bare(ax, equal=False)
        ax.set_xlim(-0.3, 10.3)
        ax.set_ylim(0.3, 7.9)
        ax.plot(x, frm, color=BLUE, lw=2.2)
        ax.plot(x, to, color=ORANGE, lw=2.2)
        ax.set_title(names[k], fontsize=9.5)
        ax.text(0.1, 0.7, "Measure from (G1 chain)", color=BLUE, fontsize=8, fontweight="bold")
        ax.text(0.1, 7.3, "Measure to (G0 set)", color=ORANGE, fontsize=8, fontweight="bold")
        for xi in sx:
            fy = np.interp(xi, x, frm)
            if k == 0:
                d = np.hypot(x - xi, to - fy)
                j = int(np.argmin(d))
                qx, qy = x[j], to[j]
            elif k == 1:
                # perpendicular to the from curve's tangent, in the curve plane
                i = int(np.argmin(abs(x - xi)))
                tx, ty = 1.0, np.gradient(frm, x)[i]
                n = np.array([-ty, tx]) / np.hypot(tx, ty)
                ts = np.linspace(0, 8, 800)
                px, py = xi + n[0] * ts, fy + n[1] * ts
                gap = py - np.interp(px, x, to)
                j = int(np.argmax(gap > 0)) if (gap > 0).any() else len(ts) - 1
                qx, qy = px[j], py[j]
            else:
                qx, qy = xi, np.interp(xi, x, to)
            ax.plot([xi, qx], [fy, qy], color=AQUA, lw=1.1)
            ax.plot([xi], [fy], "o", color=BLUE, ms=3.8)
            ax.plot([qx], [qy], "o", color=ORANGE, ms=3.8)
    # axis arrow in the third panel
    axs[2].annotate("", xy=(9.9, 3.9), xytext=(9.9, 2.6), arrowprops=dict(arrowstyle="-|>", color=VIOLET, lw=1.6))
    axs[2].text(9.75, 4.1, "Z", color=VIOLET, fontsize=9, fontweight="bold", ha="right")
    save(fig, OUT + "fig_modes.png")


def fig_flow():
    x, frm, to = curves()
    fig, ax = plt.subplots(figsize=(9.0, 2.1))
    bare(ax, equal=False)
    ax.set_xlim(-0.3, 20.3)
    ax.set_ylim(1.2, 7.8)
    # left: curves with numbered points
    ax.plot(x * 0.9, frm + 0.4, color=BLUE, lw=2)
    ax.plot(x * 0.9, to - 0.5, color=ORANGE, lw=2)
    sx = np.linspace(0.8, 9.2, 5)
    for i, xi in enumerate(sx):
        fy = np.interp(xi, x, frm) + 0.4
        ty = np.interp(xi, x, to) - 0.5
        ax.plot([xi * 0.9] * 2, [fy, ty], color=AQUA, lw=1)
        ax.plot([xi * 0.9], [fy], "o", color=BLUE, ms=4)
        ax.text(xi * 0.9, fy - 0.55, str(i), ha="center", fontsize=8, color=BLUE)
    ax.text(4.1, 7.4, "1. points on From (count or spacing)\n2. measure each to To", ha="center", fontsize=8.5,
            va="top", color=INK2)
    ax.annotate("", xy=(10.6, 3.2), xytext=(9.5, 3.2), arrowprops=dict(arrowstyle="-|>", color=INK2, lw=1.4))
    # right: table
    rows = [("Point #", "From point", "To point", "Distance"), ("0", "(x, y, z)", "(x, y, z)", "d0"),
            ("1", "(x, y, z)", "(x, y, z)", "d1"), ("2", "...", "...", "...")]
    cx = [11.0, 12.7, 15.6, 18.6]
    for r, row in enumerate(rows):
        yy = 5.6 - r * 0.95
        for c, t in enumerate(row):
            ax.text(cx[c], yy, t, fontsize=8.5, color=INK if r else VIOLET, fontweight="bold" if r == 0 else "normal",
                    va="center")
        ax.plot([10.9, 20.1], [yy - 0.45] * 2, color=MUTED, lw=0.6)
    ax.text(15.5, 7.4, "3. rows appear in the feature's table: one per point", ha="center", fontsize=8.5, color=INK2)
    save(fig, OUT + "fig_flow.png")


if __name__ == "__main__":
    fig_modes()
    fig_flow()

