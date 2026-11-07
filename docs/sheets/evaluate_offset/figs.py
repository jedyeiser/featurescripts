"""Figures for the Evaluate offset sheet (schematic, computed; not Onshape renders).
Run from repo root: python docs/sheets/evaluate_offset/figs.py
"""
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "tooling"))
from figstyle import *  # noqa

OUT = "docs/sheets/evaluate_offset/img/"


def fig_flow():
    x = np.linspace(0, 1000, 300)
    ref = 30 * np.sin(x / 1000 * np.pi * 0.9)
    wtrue = np.where(x < 200, 4 + 6 * x / 200, np.where(x < 700, 10, 10 - 6 * (x - 700) / 300))
    dy = np.gradient(ref, x); n = np.hypot(1, dy); nx, ny = -dy / n, 1 / n
    tx, ty = x + wtrue * nx, ref + wtrue * ny
    fig = plt.figure(figsize=(8.2, 3.5))
    ax = fig.add_axes([0.05, 0.36, 0.90, 0.60]); bare(ax, equal=False)
    ax.plot(x, ref, color=INK2, lw=2.4, label="Reference edges")
    ax.plot(tx, ty, color=ORANGE, lw=2.4, label="Target edges")
    st = np.arange(30, 1000, 70)
    for sx in st:
        i = np.argmin(abs(x - sx))
        ax.plot([x[i], tx[i]], [ref[i], ty[i]], color=BLUE, lw=1.2)
        ax.plot(tx[i], ty[i], "o", color=BLUE, ms=3.5)
        ax.plot(x[i], ref[i], "o", color=INK2, ms=3)
    ax.text(40, 62, "at every station of the reference: cut the target by the station's section plane,\nread the cut point in the frame (blue) = width, height", fontsize=8.3, color=BLUE, va="top")
    ax.legend(loc="lower left", fontsize=8.5, ncol=2)
    ax.set_ylim(-14, 70); ax.set_xlim(-10, 1010)
    ax2 = fig.add_axes([0.09, 0.09, 0.82, 0.20])
    ax2.plot(x, wtrue, color=BLUE, lw=2.4)
    ax2.plot(st, np.interp(st, x, wtrue), "o", color=BLUE, ms=3.5)
    ax2.set_ylim(0, 14); ax2.set_xlim(-10, 1010)
    ax2.set_ylabel("width", fontsize=9); ax2.set_xlabel("output: offset profile wire (X station, Y width, Z height) -> feed it to Driven edge offset", fontsize=8.5)
    save(fig, OUT + "eo_flow.png")


if __name__ == "__main__":
    fig_flow()
