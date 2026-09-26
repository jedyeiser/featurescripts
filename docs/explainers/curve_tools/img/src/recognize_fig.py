"""fig07: Recognize arcs -- curvature along two fixture splines vs the single arc that would replace them.

Data (data_recognize.json) read from Curve_tools' "Arc tangency tests" studio (read-only eval API, 2026-09-26):
F1 (spline through 9 points of an R100 quarter circle) -> R1 replaces it with an arc R100;
F4 (coarse spline through 3 points of an R50 circle over 120 deg) -> R5 finds no arc within 0.01 mm.
usage (repo root): python docs/explainers/curve_tools/img/src/recognize_fig.py
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "..", "tooling"))
from figstyle import *  # noqa: F401,F403,E402

D = json.load(open(os.path.join(HERE, "data_recognize.json")))


def edges(key, kind=None):
    out = []
    for s in D[key]:
        p = s.split("|")
        if kind and p[1] != kind:
            continue
        a = np.array([[float(v) for v in q.split(",")] for q in p[2:]])
        out.append((p[1], a))
    return out


def radius_along(a):
    xy = a[:, :2]
    s = np.concatenate([[0], np.cumsum(np.hypot(*np.diff(xy, axis=0).T))])
    return s / s[-1], 1.0 / np.maximum(a[:, 2], 1e-12)


fig, (a1, a2) = plt.subplots(1, 2, figsize=(12, 4.2))
for kind, a in edges("F1", "SPLINE"):
    t, r = radius_along(a)
    a1.plot(t, r, color=ORANGE, lw=2, label="F1: spline through 9 points of R100")
for kind, a in edges("R1"):
    t, r = radius_along(a)
    a1.plot(t, r, color=BLUE, lw=2, ls="--", label="R1: the replacing arc (R100.002)")
a1.set_ylim(97, 103)
a1.set_xlabel("position along the edge")
a1.set_ylabel("radius of curvature (mm)")
a1.legend(loc="upper center", fontsize=8.5)
a1.set_title("Recognized: within 0.01 mm of one arc -> replaced", fontsize=10)
for kind, a in edges("F4", "SPLINE"):
    t, r = radius_along(a)
    a2.plot(t, r, color=ORANGE, lw=2, label="F4: coarse spline, 3 points of R50 over 120 deg")
a2.axhline(50, color=BLUE, ls="--", lw=1.4, label="R50: the circle it was drawn from")
a2.set_xlabel("position along the edge")
a2.set_ylabel("radius of curvature (mm)")
a2.legend(loc="upper center", fontsize=8.5)
a2.set_title("Not recognized: no single arc within 0.01 mm -> stays a spline", fontsize=10)
save(fig, os.path.join(HERE, "..", "fig07_recognize_arcs.png"))
