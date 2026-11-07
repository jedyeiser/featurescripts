"""Variable + figure (frame and Map output). Run from repo root: python docs/sheets/variable_plus/figs.py"""
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "tooling"))
from figstyle import plt, np, save, bare, arrow, FancyBboxPatch, BLUE, ORANGE, AQUA, RED, GREEN, INK, INK2, MUTED  # noqa: E402

OUT = "docs/sheets/variable_plus/img/"


def main():
    fig, (a, b) = plt.subplots(1, 2, figsize=(10, 3.9), gridspec_kw={"width_ratios": [1.15, 1]})
    bare(a)
    # world axes
    arrow(a, (0, 0), (2.2, 0), col=MUTED); a.text(2.3, -0.05, "world X", color=MUTED, fontsize=9, va="center")
    arrow(a, (0, 0), (0, 2.2), col=MUTED); a.text(0.05, 2.35, "world Y", color=MUTED, fontsize=9)
    # frame rotated 90 deg about Z: frame X = world Y, frame Y = -world X; origin at (3.0, 0.6)
    o = np.array([3.4, 0.5])
    arrow(a, o, o + (0, 1.8), col=BLUE, lw=2); a.text(o[0] + 0.1, o[1] + 1.9, "frame X", color=BLUE, fontsize=9, fontweight="bold")
    arrow(a, o, o + (-1.8, 0), col=ORANGE, lw=2); a.text(o[0] - 1.8, o[1] - 0.3, "frame Y", color=ORANGE, fontsize=9, fontweight="bold")
    a.text(o[0] + 0.1, o[1] - 0.3, "mate connector", color=INK2, fontsize=8)
    p1, p2 = np.array([0.9, 1.0]), np.array([4.6, 2.9])
    a.plot(*zip(p1, p2), color=RED, lw=2)
    for p, t in ((p1, "first"), (p2, "second")):
        a.plot(*p, "o", color=INK, ms=6); a.text(p[0] + 0.1, p[1] + 0.15, t, fontsize=9)
    # delta in frame: dx_f = (p2-p1) . frameX(0,1) , dy_f = . frameY(-1,0)
    d = p2 - p1
    a.set_xlim(-0.3, 5.4); a.set_ylim(-0.6, 3.6)
    a.set_title("Distance, measured in a mate connector's frame", loc="left")
    a.text(0.0, -0.55, "delta = (%.1f, %.1f, 0) in the frame, signed; world X/Y/Z stay unsigned" % (d[1], -d[0]), fontsize=8, color=INK2)
    # right: map keys
    bare(b, equal=False); b.set_xlim(0, 10); b.set_ylim(0, 10)
    b.text(0, 9.4, "Output: Map  ->  one variable, read by key", fontsize=11, fontweight="bold")
    b.add_patch(FancyBboxPatch((0.1, 5.3), 9.6, 3.4, boxstyle="round,pad=0.02,rounding_size=0.2", fc="#f3f5f8", ec="#52514e"))
    rows = ["#d.distance", "#d.delta[0]  ..  [2]", "#d.first   #d.second"]
    for i, r in enumerate(rows):
        b.text(0.5, 7.9 - i * 1.0, r, family="monospace", fontsize=10, color=BLUE)
    b.text(0.5, 4.3, "Coordinate mode:  #p.point[0] .. [2]", family="monospace", fontsize=10, color=BLUE)
    b.text(0.5, 3.0, "Single value instead:", fontsize=9, color=INK2, fontweight="bold")
    b.text(0.5, 2.2, "Distance, Along X/Y/Z, Along custom, Delta (vector)", fontsize=9)
    b.text(0.5, 1.4, "Point: X, Y, Z or Vector (X, Y, Z)", fontsize=9)
    b.text(0.5, 0.4, "Vectors and keys carry units (lengths).", fontsize=9, color=INK2)
    save(fig, OUT + "frame_and_map.png")


if __name__ == "__main__":
    main()
