"""Extract variables sheet figure: sources and the derive input. Run from repo root: python docs/sheets/extract_variables/figs.py"""
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "tooling"))
from figstyle import plt, save, bare, arrow, FancyBboxPatch, BLUE, ORANGE, AQUA, INK, INK2, MUTED  # noqa: E402

OUT = "docs/sheets/extract_variables/img/"


def box(ax, x, y, w, h, text, fc="#f3f5f8", ec="#52514e", size=9, weight="normal"):
    ax.add_patch(FancyBboxPatch((x, y), w, h, boxstyle="round,pad=0.02,rounding_size=0.15", fc=fc, ec=ec, lw=1.2))
    ax.text(x + w / 2, y + h / 2, text, ha="center", va="center", fontsize=size, fontweight=weight)


def main():
    fig, ax = plt.subplots(figsize=(10, 3.4))
    bare(ax, equal=False); ax.set_xlim(0, 20); ax.set_ylim(0, 7)
    box(ax, 0.2, 5.0, 5.0, 1.5, "Features of this block\n(Sources): keys", fc="#e8f0fb")
    box(ax, 0.2, 2.6, 5.0, 1.5, "Derived Part Studio\n(exportVariables map)", fc="#e8f0fb", ec=ORANGE)
    ax.text(0.2, 1.2, "Second input: variables a derived studio exports\n(Add variables for derive / Variable +)", fontsize=8, color=ORANGE)
    box(ax, 6.8, 2.8, 6.4, 2.6, "Extract variables\nEntries (8 types):\nSource key, Filtered, Closest,\nShared edges, Chain end,\nEdges between, Bridging, Region", fc="#fdf3e1", weight="bold", size=8.5)
    arrow(ax, (5.2, 5.6), (6.8, 4.8)); arrow(ax, (5.2, 3.4), (6.8, 3.5), col=ORANGE)
    box(ax, 15.2, 5.2, 4.6, 1.1, "#prefix_name  (value)", fc="#e6f4ee")
    box(ax, 15.2, 3.7, 4.6, 1.1, "prefix_name (query)", fc="#e6f4ee")
    box(ax, 15.2, 2.2, 4.6, 1.1, "#name (from derive)", fc="#e6f4ee", ec=ORANGE)
    box(ax, 15.2, 0.7, 4.6, 1.1, "manifest map (optional)", fc="#e6f4ee")
    for y in (5.75, 4.25, 2.75, 1.25):
        arrow(ax, (13.2, 4.1), (15.2, y))
    save(fig, OUT + "sources_and_derive.png")


if __name__ == "__main__":
    main()


