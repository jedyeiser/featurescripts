"""Topsheet figures from real kernel sections (data/topsheet_sections.txt, data/layup.txt):
fig06 sections, fig07 unroll, fig08 widths, fig09 stretch / shear map.
usage: python fig_topsheet.py   (from this folder)"""
import os
import numpy as np
import matplotlib.pyplot as plt
from matplotlib.colors import TwoSlopeNorm

import topsheet as T
from style import save, DATA, BLUE, ORANGE, AQUA, VIOLET, RED, INK, INK2, MUTED, YELLOW

sts, LW = T.load()
xs = np.array([s.x for s in sts])


def nearest(x):
    return sts[int(np.argmin(np.abs(xs - x)))]


# ---------------------------------------------------------------- fig06: sections
picks = [(-890, "tail end (flat again: W drops with it)", MUTED), (-878, "tail U-turn", ORANGE), (-640, "tail flap", BLUE),
         (-384, "transition zone", VIOLET), (0, "mid ski", INK2), (820, "tip flap", AQUA), (886, "tip U-turn", RED)]
fig, axes = plt.subplots(len(picks), 1, figsize=(9, 10.5), sharex=True)
for ax, (x, label, col) in zip(axes, picks):
    st = nearest(x)
    ax.plot(st.w, st.h, color=col, lw=2)
    ax.axhline(0, color=MUTED, lw=0.8, ls="--")
    ax.set_ylim(-8, 4.5)
    ax.set_ylabel("h (mm)")
    ax.text(0.01, 0.12, "%s   x = %.0f mm   flat width %.2f mm" % (label, st.x, st.L.max() - st.L.min()),
            transform=ax.transAxes, color=INK, fontsize=9)
axes[-1].set_xlabel("w across the reference plane (mm)")
axes[0].set_title("Topsheet mid-surface sections (kernel), station planes normal to W   [height exaggerated]")
fig.text(0.01, 0.005, "Dashed: the target W's extrusion (h = 0). Plateau on it; pressed step; flap below. Data: Unwrap_Testing Copy 2, RnRD.",
         color=INK2, fontsize=8)
save(fig, "fig06_topsheet_sections.png")

# ---------------------------------------------------------------- fig07: unroll one section
st = nearest(-384)
fig, (a1, a2) = plt.subplots(2, 1, figsize=(9, 5.2), sharex=True, gridspec_kw={"height_ratios": [1.6, 1]})
a1.plot(st.w, st.h, color=VIOLET, lw=2.2)
marks = np.arange(-70, 71, 10.0)
for m in marks:
    order = np.argsort(st.L)
    Lm = np.clip(m, st.L.min(), st.L.max())
    w = np.interp(Lm, st.L[order], st.w[order])
    h = np.interp(Lm, st.L[order], st.h[order])
    a1.plot(w, h, "o", color=VIOLET, ms=5)
    a2.plot(Lm, 0, "o", color=VIOLET, ms=5)
    a2.plot([w, Lm], [0.9, 0], color=MUTED, lw=0.8)
    a1.plot([w, w], [h, -9.5], color=MUTED, lw=0.6, ls=":")
a2.plot([st.L.min(), st.L.max()], [0, 0], color=VIOLET, lw=2.2)
a2.axvline(-75, color=MUTED, lw=0.8, ls="--"); a2.axvline(75, color=MUTED, lw=0.8, ls="--")
a2.set_ylim(-0.5, 1.0); a2.set_yticks([])
a1.set_ylim(-9.5, 1.5)
a1.set_ylabel("h (mm)")
a1.set_title("Undrape of one station (x = %.0f mm): marks every 10 mm of MID-SURFACE ARC" % st.x)
a2.set_xlabel("flat y = arc length from the centreline (mm)   (plotted with +arc to the right; the code's y is -arc)")
a2.text(76, 0.35, "plan rim\nw = 75", color=INK2, fontsize=8)
a2.text(st.L.max() - 1, -0.42, "flat rim %.2f" % st.L.max(), color=INK, fontsize=8, ha="right")
save(fig, "fig07_unroll_one_section.png")

# ---------------------------------------------------------------- fig08: widths along the ski + feature's outline
fig, ax = plt.subplots(figsize=(10, 3.6))
okm = np.array([s.ok for s in sts])
hw = np.array([s.L.max() for s in sts])
ax.plot(xs[okm], hw[okm], color=VIOLET, lw=2, label="flat half-width = mid-surface arc centre -> rim (this analysis)")
ax.axhline(75, color=MUTED, lw=1.2, ls="--", label="plan half-width (rim at w = 75)")
# the Unwrap feature's own flat plate (lowest face edges), +y half
pts = []
for line in open(os.path.join(DATA, "layup.txt")):
    if line.startswith("P "):
        v = np.array([float(t) for t in line.split("|")[1].split()]).reshape(-1, 2)
        pts.append(v)
first = True
for v in pts:
    sel = v[:, 1] > 60
    if sel.sum() > 1:
        ax.plot(v[sel, 0], v[sel, 1], color=ORANGE, lw=1, ls="-", label="the feature's flat plate outline (+y side)" if first else None)
        first = False
ax.set_xlabel("flat x (mm, alignment at 0)")
ax.set_ylabel("half-width (mm)")
ax.set_ylim(73, 80.5)
ax.set_title("Topsheet: the undraped outline is wider than the plan wherever the plate drapes")
ax.legend(loc="upper center", ncol=1, fontsize=8)
save(fig, "fig08_topsheet_widths.png")

# ---------------------------------------------------------------- fig09: stretch and shear map
ys = np.arange(-79, 79.5, 1.0)
xm, stretch, shear = T.deformation(sts, ys)
fig, (a1, a2) = plt.subplots(2, 1, figsize=(10, 6.4), sharex=True)
norm = TwoSlopeNorm(vmin=-6, vcenter=0, vmax=6)
m1 = a1.pcolormesh(xm, ys, 100 * stretch, cmap="PuOr_r", norm=norm, shading="nearest")
a1.set_ylabel("flat y (mm)")
a1.set_title("Lengthwise stretch of the undrape, % (flat length / 3D mid-surface length - 1), 4 mm stations")
c1 = fig.colorbar(m1, ax=a1, pad=0.01); c1.set_label("%")
m2 = a2.pcolormesh(xm, ys, shear, cmap="Greys", vmin=0, vmax=12, shading="nearest")
a2.set_ylabel("flat y (mm)")
a2.set_xlabel("flat x (mm)")
a2.set_title("Shear: how far the 3D lengthwise chord leans off square to its section, degrees")
c2 = fig.colorbar(m2, ax=a2, pad=0.01); c2.set_label("deg")
save(fig, "fig09_topsheet_stretch_shear.png")

inner = np.abs(xm) < 850
print("stretch outside U-turns: %.2f .. %.2f %%" % (np.nanmin(100 * stretch[:, inner]), np.nanmax(100 * stretch[:, inner])))
print("stretch all: %.2f .. %.2f %%" % (np.nanmin(100 * stretch), np.nanmax(100 * stretch)))
print("shear outside U-turns max %.1f deg, all %.1f" % (np.nanmax(shear[:, inner]), np.nanmax(shear)))
print("plateau (|y|<35) stretch %.4f .. %.4f %%" % (np.nanmin(100 * stretch[np.abs(ys) < 35][:, inner]), np.nanmax(100 * stretch[np.abs(ys) < 35][:, inner])))
