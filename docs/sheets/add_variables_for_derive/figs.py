"""Add variables for derive figure (data flow). Run from repo root: python docs/sheets/add_variables_for_derive/figs.py"""
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "tooling"))
from figstyle import plt, save, bare, arrow, FancyBboxPatch, BLUE, ORANGE, AQUA, INK, INK2, MUTED  # noqa: E402

OUT = "docs/sheets/add_variables_for_derive/img/"


def box(ax, x, y, w, h, text, fc="#f3f5f8", ec="#52514e", size=9, weight="normal", color=INK):
    ax.add_patch(FancyBboxPatch((x, y), w, h, boxstyle="round,pad=0.02,rounding_size=0.15", fc=fc, ec=ec, lw=1.2))
    ax.text(x + w / 2, y + h / 2, text, ha="center", va="center", fontsize=size, fontweight=weight, color=color)


def main():
    fig, ax = plt.subplots(figsize=(10, 3.6))
    bare(ax, equal=False); ax.set_xlim(0, 20); ax.set_ylim(0, 7.2)
    ax.add_patch(FancyBboxPatch((0.1, 0.2), 8.9, 6.4, boxstyle="round,pad=0.02,rounding_size=0.2", fc="white", ec=BLUE, lw=1.6))
    ax.text(0.4, 6.15, "SOURCE Part Studio", color=BLUE, fontweight="bold", fontsize=10)
    box(ax, 0.5, 4.3, 4.0, 1.2, "Variables\n#length, #angle ...")
    box(ax, 0.5, 2.6, 4.0, 1.2, "Variable +\n[x] Export for derive")
    box(ax, 5.0, 3.3, 3.7, 1.7, "Add variables\nfor derive\nlength, angle", fc="#e8f0fb", weight="bold")
    box(ax, 1.5, 0.5, 6.2, 1.2, "exportVariables = {length: 50 mm, angle: 12 deg}", fc="#fdf3e1", ec=ORANGE, size=8.5)
    arrow(ax, (4.5, 4.9), (5.0, 4.5)); arrow(ax, (2.5, 2.6), (3.0, 1.7), col=ORANGE); arrow(ax, (6.8, 3.3), (6.0, 1.7), col=ORANGE)
    ax.text(9.5, 3.9, "Derived\nfeature", ha="center", fontsize=9, color=INK2)
    arrow(ax, (9.0, 3.1), (11.0, 3.1), col=INK2, lw=1.6)
    ax.add_patch(FancyBboxPatch((11.0, 0.2), 8.9, 6.4, boxstyle="round,pad=0.02,rounding_size=0.2", fc="white", ec=AQUA, lw=1.6))
    ax.text(11.3, 6.15, "DERIVING Part Studio", color=AQUA, fontweight="bold", fontsize=10)
    box(ax, 11.4, 3.3, 4.2, 1.9, "Extract variables\n[x] Get variables from\na derived Part Studio", fc="#fdf3e1", weight="bold")
    box(ax, 16.4, 4.4, 3.2, 0.9, "#length = 50 mm", fc="#e6f4ee")
    box(ax, 16.4, 3.1, 3.2, 0.9, "#angle = 12 deg", fc="#e6f4ee")
    arrow(ax, (15.6, 4.6), (16.4, 4.8)); arrow(ax, (15.6, 4.0), (16.4, 3.6))
    ax.text(11.5, 1.6, "Same names; an existing variable of\nthat name is overwritten.", fontsize=8.5, color=INK2)
    save(fig, OUT + "derive_flow.png")


if __name__ == "__main__":
    main()

