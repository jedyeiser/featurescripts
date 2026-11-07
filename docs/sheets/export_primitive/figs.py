"""Export primitive sheet figures (schematics with synthetic data, not a real ski). Run from the repo root:
    python docs/sheets/export_primitive/figs.py
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "tooling"))
from figstyle import plt, np, save, arrow, BLUE, ORANGE, AQUA, RED, INK, INK2, MUTED, GRID, VIOLET  # noqa: E402

OUT = "docs/sheets/export_primitive/img/"
L = 1700.0
x = np.linspace(0, L, 400)
TAIL_X, FCP_X, ACP_X = 0.0, 1480.0, 220.0
MRS_X = (FCP_X + ACP_X) / 2


def sstep(t):
    t = np.clip(t, 0, 1)
    return t * t * (3 - 2 * t)


def baseline(x):
    """Rocker - camber - rocker (z in mm, schematic, smooth)."""
    u = (x - MRS_X) / ((FCP_X - ACP_X) / 2)
    camber = 4.0 * np.clip(1 - u ** 2, 0, None) ** 2
    return camber + 38 * sstep((x - 1300) / 400) ** 2 + 14 * sstep((200 - x) / 200) ** 2


def thickness(x):
    return 4 + 6 * np.exp(-((x - MRS_X) / 380) ** 2)


def style(ax, title, ylab=None):
    ax.grid(False)
    ax.set_xlim(-30, L + 40)
    for s in ["top", "right", "bottom"]:
        ax.spines[s].set_visible(False)
    ax.set_xticks([])
    ax.text(-0.075, 0.5, title, transform=ax.transAxes, ha="right", va="center", fontsize=8.5, fontweight="bold", color=INK2)


def marks(ax, names=("ACP", "MRS", "FCP"), y=None, top=True):
    for nm, xv in zip(names, (ACP_X, MRS_X, FCP_X)):
        ax.axvline(xv, color=GRID, lw=1, zorder=0)
    if top:
        for nm, xv in zip(names, (ACP_X, MRS_X, FCP_X)):
            ax.text(xv, 1.02, nm, transform=ax.get_xaxis_transform(), ha="center", va="bottom", fontsize=8, color=INK2)


def fig_hero():
    fig, axs = plt.subplots(5, 1, figsize=(10, 4.6), gridspec_kw={"height_ratios": [1.0, 0.9, 1.3, 1.0, 1.0]})
    fig.subplots_adjust(hspace=0.5, left=0.2, right=0.98, top=0.93, bottom=0.05)
    # EI
    ax = axs[0]
    style(ax, "EI band\n(only with Target EI)")
    ei = 40 + 110 * np.exp(-((x - MRS_X) / 420) ** 2)
    ax.plot(x, ei, color=VIOLET)
    ax.axhline(0, color=MUTED, lw=0.8)
    marks(ax)
    ax.set_ylim(-10, 190)
    ax.set_yticks([])
    # Baseline
    ax = axs[1]
    style(ax, "BASELINE\n(z scaled to band)")
    ax.plot(x, baseline(x), color=BLUE)
    ax.axhline(0, color=MUTED, lw=0.8)
    marks(ax, top=False)
    ax.set_ylim(-4, 45)
    ax.set_yticks([])
    ax.text(1690, 40, "TIP", fontsize=7, color=INK2, ha="right")
    ax.text(5, 14, "TAIL", fontsize=7, color=INK2)
    # Profile
    ax = axs[2]
    style(ax, "PROFILE\nbottom, top, ends,\ncavity depth profile")
    b = baseline(x)
    top = b + thickness(x)
    ax.plot(x, b, color=BLUE)
    ax.plot(x, top, color=INK2)
    cav = top - 2.2 - 0.0 * x
    m = (x > 330) & (x < 1250)
    ax.plot(x[m], np.where(m, top - 2.0 - 1.5 * np.exp(-((x - MRS_X) / 300) ** 2), np.nan)[m], color=AQUA, lw=2.2)
    ax.plot([0, 0], [b[0], top[0]], color=INK2)
    ax.plot([L, L], [b[-1], top[-1]], color=INK2)
    marks(ax, top=False)
    ax.set_ylim(-4, 60)
    ax.set_yticks([])
    ax.text(780, 21, "cavity depth profile (optional)", color=AQUA, fontsize=8, ha="center")
    # Footprint
    ax = axs[3]
    style(ax, "FOOTPRINT\n(unwrapped along s)")
    w = (34 - 9 * np.exp(-((x - MRS_X) / 380) ** 2)) * (1 - 0.85 * sstep((x - 1480) / 220)) * (1 - 0.6 * sstep((220 - x) / 220))
    ax.plot(x, w, color=INK2)
    ax.plot(x, -w, color=INK2)
    ax.axhline(0, color=MUTED, lw=0.8)
    marks(ax, top=False)
    ax.set_ylim(-60, 60)
    ax.set_yticks([])
    # Radius
    ax = axs[4]
    style(ax, "RADIUS (m)\nor CURVATURE (1/m)")
    r = np.where((x > 520) & (x < 1160), 14 + 0 * x, np.nan)
    ax.plot(x, r, color=RED, lw=2.2)
    ax.plot(x[(x > 1180) & (x < 1700)], -6 - 0.0 * x[(x > 1180) & (x < 1700)], color=RED, lw=2.2)
    ax.plot(x[(x > 0) & (x < 500)], -6 - 0.0 * x[(x > 0) & (x < 500)], color=RED, lw=2.2)
    ax.axhline(0, color=MUTED, lw=0.8)
    ax.text(840, 17, "sidecut +", fontsize=8, color=RED, ha="center")
    ax.text(1450, -11, "taper / tip -", fontsize=8, color=RED, ha="center")
    marks(ax, top=False)
    ax.set_ylim(-14, 24)
    ax.set_yticks([])
    ax.set_xlabel("x along the ski (mm); bands stacked below the part, FCP / MRS / ACP key lines optional", fontsize=8)
    ax.set_xticks([])
    save(fig, OUT + "primitive_bands.png")


def fig_cavity():
    """Cavity depth is measured along the bottom wire's normal, from the top of the ski to the cavity profile."""
    fig, (a, b) = plt.subplots(1, 2, figsize=(10, 3.3), gridspec_kw={"width_ratios": [1.5, 1]})
    xs = np.linspace(0, 100, 200)
    bot = 0.00005 * (xs - 50) ** 3 * 0 + 6 * np.sin(xs / 100 * 0.9 * np.pi) * 0.25
    top = bot + 10 + 3 * np.exp(-((xs - 50) / 25) ** 2)
    cav = top - 4 - 0.8 * np.exp(-((xs - 50) / 20) ** 2)
    m = (xs > 12) & (xs < 88)
    a.fill_between(xs, bot, top, color="#eef1f6")
    a.plot(xs, bot, color=BLUE, lw=2)
    a.plot(xs, top, color=INK2, lw=2)
    a.plot(xs[m], cav[m], color=AQUA, lw=2.4)
    a.grid(False)
    i = 100
    dx = xs[i + 1] - xs[i - 1]
    dz = bot[i + 1] - bot[i - 1]
    n = np.array([-dz, dx]) / np.hypot(dx, dz)
    p0 = np.array([xs[i], bot[i]])
    # intersect normal line with top and cav (small steps)
    t = np.linspace(0, 20, 4000)
    line = p0[:, None] + n[:, None] * t
    kt = np.argmin(np.abs(np.interp(line[0], xs, top) - line[1]))
    kc = np.argmin(np.abs(np.interp(line[0], xs, cav) - line[1]))
    pt, pc = line[:, kt], line[:, kc]
    a.plot([p0[0], pt[0]], [p0[1], pt[1]], color=ORANGE, lw=1.2, ls="--")
    arrow(a, tuple(pt), tuple(pc), col=RED, lw=1.6, ms=10)
    a.plot(*p0, "o", color=ORANGE, ms=5)
    a.text(p0[0] + 2, p0[1] - 3, "table row (bottom wire)", fontsize=8, color=ORANGE)
    a.text(pt[0] - 3, pt[1] + 0.8, "top of ski", fontsize=8, color=INK2)
    a.text(pc[0] + 2.2, (pt[1] + pc[1]) / 2 - 0.5, "cavity depth", fontsize=8.5, color=RED, fontweight="bold")
    a.text(50, cav[100] - 3.2, "cavity depth profile (picked wire)", fontsize=8, color=AQUA, ha="center")
    a.text(6, 14.2, "blank here:\nprofile does not reach", fontsize=7.5, color=MUTED)
    a.text(94, 14.2, "blank", fontsize=7.5, color=MUTED, ha="right")
    a.set_ylim(-8, 19)
    a.set_xlim(0, 100)
    a.set_xticks([])
    a.set_yticks([])
    for s in a.spines.values():
        s.set_visible(False)
    a.set_title("Measured along the bottom wire's normal (not vertically)", loc="left", fontsize=9.5)
    # table
    b.axis("off")
    b.grid(False)
    rows = [["x", "s", "w", "thck", "cavity depth", "baseline"],
            ["...", "...", "...", "...", "...", "..."],
            ["220 (ACP)", "", "", "10.0", "blank", ""],
            ["500", "", "", "11.2", "6.9", ""],
            ["860 (MRS)", "", "", "13.0", "8.8", ""],
            ["1200", "", "", "11.5", "7.2", ""],
            ["1480 (FCP)", "", "", "10.0", "blank", ""]]
    tb = b.table(cellText=[[r[0], r[3], r[4]] for r in rows], colLabels=None, cellLoc="center", loc="center",
                 colWidths=[0.38, 0.25, 0.37])
    tb.auto_set_font_size(False)
    tb.set_fontsize(8)
    for (r, c), cell in tb.get_celld().items():
        cell.set_edgecolor(GRID)
        cell.set_height(0.11)
        if r == 0:
            cell.set_text_props(fontweight="bold", color="#0b4f8a")
        if c == 2 and r > 0:
            cell.set_text_props(color=AQUA if r not in (2, 6) else MUTED, fontweight="bold")
    b.set_title("Table 6 'RSL data' (excerpt, illustrative numbers)", loc="left", fontsize=9)
    fig.subplots_adjust(wspace=0.05)
    save(fig, OUT + "primitive_cavity.png")


if __name__ == "__main__":
    fig_hero()
    fig_cavity()
