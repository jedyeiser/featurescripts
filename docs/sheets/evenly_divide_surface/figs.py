import sys, os
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "tooling"))
from figstyle import *

# ---- Fig 1: how the wires are made
fig = plt.figure(figsize=(9.2, 3.3))
ax = fig.add_axes([0.0, 0.0, 0.52, 1.0]); bare(ax, equal=False)
x = np.linspace(0, 10, 200)
top = 1.0 + 0.55*np.sin(x*0.55) + 0.12*x*0 ; 
# ski-like surface seen from the side: tapered tube outline in oblique view
w = 0.55 + 0.55*np.sin(np.pi*x/10)**0.6
ax.fill_between(x, -w, w, color="#eaf1fb", ec=MUTED, lw=1.2)
ax.plot([-0.6, 10.6], [0, 0], color=INK2, lw=1.2, ls="--")
ax.text(10.7, 0, "cross section wire\n(may overhang)", va="center", fontsize=8, color=INK2)
us = np.linspace(0.9, 9.1, 7)
for k, u in enumerate(us):
    wu = 0.55 + 0.55*np.sin(np.pi*u/10)**0.6
    ax.plot([u, u], [-wu-0.15, wu+0.15], color=BLUE, lw=1.4)
    for j in range(5):
        ax.plot(u, wu*np.cos(np.pi*j/4)*0.0 + (wu*(1-2*j/4)), "o", ms=3.2, color=ORANGE)
for j in range(5):
    ys = [ (0.55 + 0.55*np.sin(np.pi*u/10)**0.6)*(1-2*j/4) for u in us]
    ax.plot(us, ys, color=ORANGE, lw=1.3)
ax.text(5, 1.55, "section planes, evenly spaced along the wire", ha="center", color=BLUE, fontsize=8.5)
ax.text(5, -1.62, "same-numbered points on every section join into one wire", ha="center", color=ORANGE, fontsize=8.5)
ax.set_xlim(-0.8, 13.2); ax.set_ylim(-1.9, 1.9)
ax.set_title("1  Cut, divide, join", loc="left", fontsize=10, x=0.02)

def loop(ax, n, half, cx, label):
    t = np.linspace(0, 2*np.pi, 300)
    X = cx + 1.0*np.sin(t); Y = 0.75*np.cos(t)
    ax.plot(X, Y, color=MUTED, lw=1.4)
    ax.plot([cx, cx], [-1.15, 1.15], color=GREEN, lw=1, ls="--")
    fr = [(i + (0.5 if half else 0))/n for i in range(n)]
    for i, f in enumerate(fr):
        a = 2*np.pi*f
        ax.plot(cx+np.sin(a), 0.75*np.cos(a), "o", ms=6, color=ORANGE if i else RED, zorder=5)
        ax.text(cx+1.28*np.sin(a), 0.98*np.cos(a)*1.02+ (0.0), str(i), fontsize=8, ha="center", va="center", color=INK2)
    ax.text(cx, -1.45, label, ha="center", fontsize=8, color=INK2)
ax2 = fig.add_axes([0.54, 0.0, 0.46, 1.0]); bare(ax2, equal=True)
loop(ax2, 6, False, 0.0, "full surface, 6 wires\nwire 0 on the plane")
loop(ax2, 6, True, 3.2, "wires off the plane\n(half step)")
ax2.text(0.0, 1.35, "start = uppermost Z\non symmetry plane", ha="center", fontsize=8, color=GREEN)
ax2.set_xlim(-1.8, 5.0); ax2.set_ylim(-2.0, 1.7)
ax2.set_title("2  One section, N divisions by arc length", loc="left", fontsize=10)
save(fig, os.path.join(HERE, "img", "eds_how.png"))

# ---- Fig 2: strips
fig, axs = plt.subplots(1, 3, figsize=(9.2, 2.1))
t = np.linspace(0, 1, 200)
for ax in axs: bare(ax, equal=False)
ax = axs[0]
ax.plot(t*10, 0.5*np.sin(t*6)+1, color=MUTED, lw=1)
ax.plot(t*10, 0.5*np.sin(t*6)+1, color=ORANGE, lw=2.2)
ax.set_title("Wire", fontsize=9); ax.set_ylim(0, 2.2)
ax = axs[1]
ax.fill_between(t*10, 0.5*np.sin(t*6)+0.7, 0.5*np.sin(t*6)+1.3, color="#cfe9dd", ec=KEEP)
ax.plot(t*10, 0.5*np.sin(t*6)+1, color=ORANGE, lw=1.5)
ax.annotate("", xy=(5, 0.5*np.sin(3)+1.3), xytext=(5, 0.5*np.sin(3)+0.7), arrowprops=dict(arrowstyle="<->", color=INK2))
ax.text(5.3, 0.5*np.sin(3)+1.45, "width", fontsize=8, color=INK2)
ax.set_title("Strip: surface, centred on the wire", fontsize=9); ax.set_ylim(0, 2.2)
ax = axs[2]
for off, c in [(0.0, "#cfe9dd")]:
    ax.fill_between(t*10, 0.5*np.sin(t*6)+0.7, 0.5*np.sin(t*6)+1.3, color=c, ec=KEEP)
    ax.fill_between(t*10, 0.5*np.sin(t*6)+0.7-0.18, 0.5*np.sin(t*6)+1.3-0.18, color="none", ec=BLUE, hatch="///", lw=0.8)
ax.set_title("Thickened strip (solid)", fontsize=9); ax.set_ylim(0, 2.2)
save(fig, os.path.join(HERE, "img", "eds_strips.png"))
