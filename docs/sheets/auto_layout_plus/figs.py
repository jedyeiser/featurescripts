"""Auto layout plus sheet figures (schematics, no live data). Run from the repo root:
    python docs/sheets/auto_layout_plus/figs.py
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "tooling"))
from figstyle import plt, save, bare, Rectangle, FancyArrowPatch, BLUE, ORANGE, AQUA, RED, INK, INK2, MUTED, GRID  # noqa: E402

OUT = "docs/sheets/auto_layout_plus/img/"


def rect(ax, x, y, w, h, fc="#e6effb", ec=BLUE, lw=1.2, ls="-", label=None, fs=8, col=INK):
    ax.add_patch(Rectangle((x, y), w, h, fc=fc, ec=ec, lw=lw, ls=ls))
    if label:
        ax.text(x + w / 2, y + h / 2, label, ha="center", va="center", fontsize=fs, color=col)


def fig_layout():
    fig, (a, b) = plt.subplots(1, 2, figsize=(10, 3.9), gridspec_kw={"width_ratios": [1, 1.7]})
    # before: scattered
    bare(a)
    a.set_title("Before: bodies anywhere", fontsize=10)
    parts = [(0, 6, 34, 12, "A"), (20, 40, 22, 10, "B"), (46, 14, 14, 26, "C"), (5, 24, 16, 8, "D"),
             (40, 2, 18, 8, "E"), (62, 40, 12, 16, "F")]
    for x, y, w, h, n in parts:
        rect(a, x, y, w, h, label=n)
    a.set_xlim(-3, 78)
    a.set_ylim(-2, 60)
    # after: packed
    bare(b)
    b.set_title("After: packed in the origin frame (X = length, Y = width)", fontsize=10)
    sw, sl = 30, 60
    for s, (y0, tag) in enumerate([(0, "sheet 1"), (33, "sheet 2 (+Y)")]):
        rect(b, 0, y0, sl, sw, fc="none", ec="#c2339b", lw=1.4, ls="--")
        b.text(sl + 1.5, y0 + sw - 2, tag, fontsize=8, color="#c2339b", va="top")
    # sheet 1 columns (fill Y first)
    gap = 1.5
    col1 = [("A", 22, 11), ("D", 22, 8), ("B", 22, 6)]
    y = gap
    for n, w, h in col1:
        rect(b, gap, y, w, h, label=n)
        y += h + gap
    col2 = [("C", 14, 13), ("E", 14, 6)]
    y = gap
    for n, w, h in col2:
        rect(b, 22 + 2 * gap, y, w, h, label=n)
        y += h + gap
    rect(b, 22 + 2 * gap + 14 + gap, gap, 10, 9, label="F")
    # sheet 2
    rect(b, gap, 33 + gap, 16, 10, label="G")
    # origin and axes
    b.plot([0], [0], "o", color=RED, ms=6)
    b.text(2, -7.5, "origin = mate connector or vertex", fontsize=8, color=RED, va="center")
    b.annotate("", (16, -3), (0, -3), arrowprops=dict(arrowstyle="-|>", color=INK2))
    b.text(17.5, -3, "+X (length)", fontsize=8, va="center", color=INK2)
    b.annotate("", (-3.5, 14), (-3.5, 0), arrowprops=dict(arrowstyle="-|>", color=INK2))
    b.text(-5.5, 7, "+Y", fontsize=8, ha="center", color=INK2)
    b.text(22, 52, "Fills Y first (columns),\nthen moves along X.\nBuffers: gaps and margin.\nSheet full: next at +Y.",
           fontsize=8, color=INK2, va="top")
    b.set_xlim(-8, 78)
    b.set_ylim(-13, 66)
    fig.tight_layout()
    save(fig, OUT + "fig_layout.png")


def fig_nesting():
    fig, ax = plt.subplots(figsize=(5.2, 2.6))
    bare(ax)
    ax.set_title("Nest bodies inside holes (opt-in)", fontsize=10)
    rect(ax, 0, 0, 60, 30, fc="#eef1f5", ec=INK2, lw=1.6)
    for x, w in [(6, 18), (32, 22)]:
        rect(ax, x, 6, w, 18, fc="white", ec=INK2, lw=1.2, ls="-")
    rect(ax, 8, 9, 7, 6, fc="#dff3ea", ec=AQUA, label="s1", fs=7)
    rect(ax, 16, 14, 6, 6, fc="#dff3ea", ec=AQUA, label="s2", fs=7)
    rect(ax, 36, 10, 10, 8, fc="#dff3ea", ec=AQUA, label="s3", fs=7)
    ax.text(30, 33.5, "large flat part with windows", ha="center", fontsize=8, color=INK2)
    ax.text(30, -4, "small bodies keep their orientation; 5 mm grid; spread evens the clearance", ha="center", fontsize=7.5, color=INK2)
    ax.set_xlim(-2, 62)
    ax.set_ylim(-7, 37)
    save(fig, OUT + "fig_nesting.png")


if __name__ == "__main__":
    fig_layout()
    fig_nesting()





