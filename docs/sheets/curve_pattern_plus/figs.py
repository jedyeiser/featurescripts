import sys, os
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "tooling"))
from figstyle import *

def path(ax, y0, L=10.0, amp=0.35):
    x = np.linspace(0, L, 300); y = y0 + amp*np.sin(x*0.7)
    ax.plot(x, y, color=MUTED, lw=2.2, solid_capstyle="round")
    return x, y
def at(s, y0, amp=0.35): return s, y0 + amp*np.sin(s*0.7)

# ---- Fig 1: spacing modes (rows)
fig, ax = plt.subplots(figsize=(9.2, 3.2)); bare(ax, equal=False)
rows = [
 ("Spacing + count:  spacing 2, count 4", [0, 2, 4, 6], None),
 ("Start offset 1, spacing 2, count 4", [1, 3, 5, 7], (0, 1, "start offset")),
 ("Total length 6, count 4, Centered", [2, 4, 6, 8], None),
 ("Fill path, target spacing 2.4, Maximum spacing -> 5 gaps? no: Nearest", None, None),
]
rows = [
 ("Spacing 2, count 4", [0, 2, 4, 6], None, None),
 ("Start offset 1, end offset 1.5, spacing 2.5, count 4", [1, 3.5, 6, 8.5], (0, 1, ORANGE), (8.5, 10, ORANGE)),
 ("Total length 6, count 4, Centered (offsets 0)", [2, 4, 6, 8], None, None),
 ("Fill the path, target spacing 3, Nearest value (3 gaps of 3.33)", [0, 10/3, 20/3, 10], None, None),
]
ys = [3.6, 2.4, 1.2, 0.0]
for (lab, st, a, b), y0 in zip(rows, ys):
    path(ax, y0)
    for k, s in enumerate(st):
        px, py = at(s, y0)
        ax.plot(px, py, "o", ms=8, color=RED if k == 0 else BLUE, zorder=5)
    for seg in (a, b):
        if seg:
            xs = np.linspace(seg[0], seg[1], 50)
            ax.plot(xs, y0 + 0.35*np.sin(xs*0.7), color=ORANGE, lw=4, alpha=0.7, zorder=3)
    ax.text(10.5, y0, lab, va="center", fontsize=8.5, color=INK2)
ax.text(0, 4.25, "red = seed (first station);  blue = copies;  orange = offsets kept clear", fontsize=8.5, color=INK2)
ax.set_xlim(-0.5, 20); ax.set_ylim(-0.5, 4.5)
save(fig, os.path.join(HERE, "img", "cpp_spacing.png"))

# ---- Fig 2: orientation (flag-shaped seed so rotation is visible)
fig, axs = plt.subplots(1, 2, figsize=(9.2, 2.3))
titles = ["Follow path (no roll): each copy turns with the path", "Keep seed orientation: copies are only moved"]
flag = np.array([[0, 0], [0, 1.3], [0.8, 0.95], [0, 0.6]])
def rot(P, a):
    R = np.array([[np.cos(a), -np.sin(a)], [np.sin(a), np.cos(a)]]); return P @ R.T
for k, ax in enumerate(axs):
    bare(ax, equal=True)
    x = np.linspace(0, 10, 300); y = 1.3*np.sin(x*0.55)
    ax.plot(x, y, color=MUTED, lw=2.2)
    for j, s in enumerate(np.linspace(0.6, 9.4, 6)):
        yy = 1.3*np.sin(s*0.55); a = np.arctan2(1.3*0.55*np.cos(s*0.55), 1)
        P = rot(flag, a if k == 0 else 0.0) + np.array([s, yy])
        ax.add_patch(Polygon(P, closed=True, fc=RED if j == 0 else BLUE, ec=INK2, alpha=0.9, zorder=4))
    ax.set_title(titles[k], fontsize=9)
    ax.set_xlim(-0.8, 11.0); ax.set_ylim(-2.2, 3.0)
save(fig, os.path.join(HERE, "img", "cpp_orient.png"))
