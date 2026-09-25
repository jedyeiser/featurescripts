"""fig11: Edges-mode adaptive sampling (unwrap.fs adaptiveEdgeSamples) replayed in numpy on the real U1 case:
Wrapped_profile edge 0 (15-CP B-spline, 1480 mm) unwrapped over FULL_BASELINE, alignment at X = 885.
Data: data/wires_dense.txt (fetch_wires.py). usage: python fig_adaptive.py"""
import os
import numpy as np
import matplotlib.pyplot as plt
from scipy.interpolate import CubicSpline
from scipy.spatial import cKDTree

from style import save, DATA, BLUE, ORANGE, VIOLET, INK2, MUTED, RED

edges = {"FULL_BASELINE": [], "Wrapped_profile": []}
for line in open(os.path.join(DATA, "wires_dense.txt")):
    head, rest = line[2:].split("|")
    name, cps, length = head.split()
    xz = np.array([float(t) for t in rest.split()]).reshape(-1, 2)
    edges[name].append((int(cps), float(length), xz))


def resample(xz, length, step):
    f = np.linspace(0, 1, len(xz))
    cs = CubicSpline(f, xz, axis=0)
    n = int(np.ceil(length / step)) + 1
    return cs(np.linspace(0, 1, n))


# Reference: its edges in X order, each densely resampled, joined into one polyline with arc length.
ref = []
for cps, length, xz in sorted(edges["FULL_BASELINE"], key=lambda e: min(e[2][0, 0], e[2][-1, 0])):
    if xz[-1, 0] < xz[0, 0]:
        xz = xz[::-1]
    pts = resample(xz, length, 0.05)
    ref.append(pts if not ref else pts[1:])
ref = np.vstack(ref)
seg = np.diff(ref, axis=0)
sarc = np.concatenate([[0], np.cumsum(np.linalg.norm(seg, axis=1))])
tree = cKDTree(ref)
tang = seg / np.linalg.norm(seg, axis=1)[:, None]
# curvature breaks: the reference's own edge joins
joins = []
acc = 0
for cps, length, xz in sorted(edges["FULL_BASELINE"], key=lambda e: min(e[2][0, 0], e[2][-1, 0]))[:-1]:
    acc += length
    joins.append(acc)


def foot(p):
    _, i = tree.query(p)
    best = None
    for j in (i - 1, i):
        if j < 0 or j >= len(seg):
            continue
        u = np.clip(np.dot(p - ref[j], tang[j]), 0, np.linalg.norm(seg[j]))
        q = ref[j] + u * tang[j]
        d = np.linalg.norm(p - q)
        if best is None or d < best[0]:
            n = np.array([-tang[j][1], tang[j][0]])
            best = (d, sarc[j] + u, np.dot(p - q, n))
    return best[1], best[2]


s_align = foot(np.array([885.0, 0.0]))[0]    # alignment on the reference (h 0 there)
cps, length, xz = edges["Wrapped_profile"][0]
if xz[-1, 0] < xz[0, 0]:
    xz = xz[::-1]
src = CubicSpline(np.linspace(0, 1, len(xz)), xz, axis=0)


def mapped(f):
    s, h = foot(src(f))
    return np.array([s - s_align, h])


# ---- the production rule (UNWRAP_SEED_PER_CP 3, UNWRAP_MAX_GAP 50 mm, 6 passes, 401 cap, tol = 0.005 mm / 4)
tol = 0.005 / 4
n = max(5, 3 * (cps - 1) + 1)
n = min(401, max(n, int(np.ceil(length / 50)) + 1))
params = list(np.linspace(0, 1, n))
feet = [mapped(f) for f in params]
openv = [True] * (n - 1)
for _ in range(6):
    spans = [j for j in range(len(params) - 1) if openv[j]]
    if not spans or len(params) + len(spans) > 401:
        break
    newp, newf, newo = [], [], []
    for j in range(len(params)):
        newp.append(params[j]); newf.append(feet[j])
        if j + 1 == len(params):
            break
        if openv[j]:
            m = 0.5 * (params[j] + params[j + 1])
            fm = mapped(m)
            h = params[j + 1] - params[j]
            p0, p1 = feet[j], feet[j + 1]
            m0 = (feet[j + 1] - feet[j - 1]) / (params[j + 1] - params[j - 1]) if j > 0 else (p1 - p0) / h
            m1 = (feet[j + 2] - feet[j]) / (params[j + 2] - params[j]) if j + 2 < len(params) else (p1 - p0) / h
            miss = np.max(np.abs(fm - (0.5 * (p0 + p1) + h / 8 * (m0 - m1))))
            if miss > tol:
                newo += [True]; newp.append(m); newf.append(fm); newo += [True]
                continue
        newo.append(False)
    params, feet, openv = newp, newf, newo
adaptive = np.array(feet)

# truth and a fixed 5 mm spacing for comparison
fdense = np.linspace(0, 1, 6001)
truth = np.array([mapped(f) for f in fdense])
fixed_n = int(np.ceil(length / 5)) + 1
fixed = np.array([mapped(f) for f in np.linspace(0, 1, fixed_n)])


def interp_err(sample_f, sample_pts):
    cs = CubicSpline(sample_f, sample_pts, axis=0)
    return np.max(np.linalg.norm(cs(fdense) - truth, axis=1))


err_a = interp_err(np.array(params), adaptive)
err_f = interp_err(np.linspace(0, 1, fixed_n), fixed)
print("adaptive: %d samples (seed %d), cubic-through-samples error %.5f mm" % (len(params), n, err_a))
print("fixed 5 mm: %d samples, error %.5f mm" % (fixed_n, err_f))

# flat curvature of the unwrapped curve
d1 = np.gradient(truth, fdense, axis=0)
d2 = np.gradient(d1, fdense, axis=0)
kap = (d1[:, 0] * d2[:, 1] - d1[:, 1] * d2[:, 0]) / np.linalg.norm(d1, axis=1) ** 3

fig, (a1, a2) = plt.subplots(2, 1, figsize=(10, 5.6), sharex=True, gridspec_kw={"height_ratios": [1.2, 1]})
a1.plot(truth[:, 0], 1000 * kap, color=VIOLET, lw=1.6)
for j in joins:
    a1.axvline(j - s_align, color=MUTED, lw=0.9, ls="--")
    a2.axvline(j - s_align, color=MUTED, lw=0.9, ls="--")
a1.set_ylabel("flat curvature (1/m)")
a1.set_title("U1 edge 0 unwrapped: curvature JUMPS at the reference edge joins (dashed), kinks at the source knots")
a2.plot(adaptive[:, 0], np.full(len(adaptive), 1.0), "|", color=BLUE, ms=14)
a2.plot(fixed[:, 0], np.full(len(fixed), 0.0), "|", color=ORANGE, ms=14)
a2.set_yticks([0, 1])
a2.set_yticklabels(["fixed 5 mm\n%d samples\nfit error %.4f mm" % (fixed_n, err_f),
                    "adaptive\n%d samples\nfit error %.4f mm" % (len(params), err_a)], fontsize=8)
a2.set_ylim(-0.6, 1.6)
a2.set_xlabel("flat x (mm)")
a2.grid(False)
save(fig, "fig11_adaptive_sampling.png")
