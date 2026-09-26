"""Render real Onshape bodies for the docs (read-only REST tessellation -> matplotlib PNG).

Screenshots of Onshape show every result in one colour; this colours bodies by role (region, kept / removed,
input / output) so a figure shows what a feature did. Data is the workspace's current state.

usage (repo root):  PYTHONPATH=. python docs/tooling/render_parts.py docs/decks/<slug>/renders.json

renders.json:
{
  "doc": "...", "ws": "...", "elem": "...",
  "renders": [
    {"out": "docs/decks/<slug>/shots/regions.png",
     "view": "plan" | "iso" | "side",          plan = XY, side = XZ, iso = 3D
     "yscale": 3,                               (plan only: exaggerate width, noted in the corner)
     "xrange": [-50, 1850],                     (mm, optional crop)
     "title": "...",
     "layers": [ {"parts": ["RPSD", ...] | "name": "exact part name", "color": "#1baf7a", "label": "start",
                  "alpha": 0.9, "edges": true, "labelAt": [x, y] (mm, optional)} , ...],
     "size": [10, 3.2]}
  ]
}
Parts are matched by partId, or by exact name (all bodies of that name).
"""
import json
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from figstyle import plt, save, bare, INK2, MUTED  # noqa: E402
from mpl_toolkits.mplot3d.art3d import Poly3DCollection, Line3DCollection  # noqa: E402
from matplotlib.collections import PolyCollection, LineCollection  # noqa: E402

from sync.core.client import OnshapeClient  # noqa: E402

CACHE = {}


def part_ids(c, base_parts, layer):
    if "parts" in layer:
        return layer["parts"]
    return [p["partId"] for p in base_parts if p["name"] == layer["name"]]


def facets(c, spec, pid):
    key = ("f", pid)
    if key not in CACHE:
        r = c.get(f"/api/v10/partstudios/d/{spec['doc']}/w/{spec['ws']}/e/{spec['elem']}/tessellatedfaces",
                  {"partId": pid, "chordTolerance": "0.0002", "angleTolerance": "0.05"})
        tris = []
        for body in r.get("bodies", []):
            for face in body.get("faces", []):
                for f in face.get("facets", []):
                    tris.append([[v["x"] * 1000, v["y"] * 1000, v["z"] * 1000] for v in f["vertices"]])
        CACHE[key] = np.array(tris) if tris else np.zeros((0, 3, 3))
    return CACHE[key]


def edges(c, spec, pid):
    key = ("e", pid)
    if key not in CACHE:
        r = c.get(f"/api/v10/partstudios/d/{spec['doc']}/w/{spec['ws']}/e/{spec['elem']}/tessellatededges",
                  {"partId": pid, "chordTolerance": "0.0002", "angleTolerance": "0.05"})
        lines = []
        for body in r.get("bodies", []) if isinstance(r, dict) else r:
            for e in body.get("edges", []):
                pts = [[v[0] * 1000, v[1] * 1000, v[2] * 1000] if isinstance(v, list) else [v["x"] * 1000, v["y"] * 1000, v["z"] * 1000]
                       for v in e.get("vertices", [])]
                if len(pts) > 1:
                    lines.append(np.array(pts))
        CACHE[key] = lines
    return CACHE[key]


def shade(tris, rgb):
    n = np.cross(tris[:, 1] - tris[:, 0], tris[:, 2] - tris[:, 0])
    n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-12
    light = np.array([0.3, -0.5, 0.8])
    light /= np.linalg.norm(light)
    k = 0.55 + 0.45 * np.abs(n @ light)
    return np.clip(np.outer(k, rgb), 0, 1)


def hex_rgb(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)])


def render(c, spec, rs, base_parts):
    view = rs.get("view", "plan")
    size = rs.get("size", [10, 3.2])
    fig = plt.figure(figsize=size)
    xr = rs.get("xrange")
    if view == "iso":
        ax = fig.add_subplot(111, projection="3d")
        allpts = []
        for layer in rs["layers"]:
            rgb = hex_rgb(layer.get("color", "#bbbbbb"))
            for pid in part_ids(c, base_parts, layer):
                t = facets(c, spec, pid)
                if xr is not None and len(t):
                    t = t[(t[:, :, 0].mean(axis=1) >= xr[0]) & (t[:, :, 0].mean(axis=1) <= xr[1])]
                if len(t):
                    ax.add_collection3d(Poly3DCollection(t, facecolors=shade(t, rgb), edgecolors="none", alpha=layer.get("alpha", 1.0)), autolim=False)
                    allpts.append(t.reshape(-1, 3))
                if layer.get("edges", True):
                    ls = [l for l in edges(c, spec, pid) if xr is None or (xr[0] <= l[:, 0].mean() <= xr[1])]
                    if ls:
                        ax.add_collection3d(Line3DCollection(ls, colors="#333333", linewidths=0.6), autolim=False)
        P = np.vstack(allpts)
        lo, hi = P.min(axis=0), P.max(axis=0)
        ax.set_xlim(lo[0], hi[0]); ax.set_ylim(lo[1], hi[1]); ax.set_zlim(lo[2], hi[2])
        ax.set_box_aspect(np.maximum(hi - lo, 1e-3) * np.array([1, rs.get("yscale", 1), rs.get("zscale", 1)]))
        ax.view_init(rs.get("elev", 25), rs.get("azim", -60))
        ax.set_axis_off()
    else:
        ax = bare(fig.add_subplot(111), equal=False)
        i, j = (0, 1) if view == "plan" else (0, 2)
        ys = rs.get("yscale", 1)
        for layer in rs["layers"]:
            rgb = hex_rgb(layer.get("color", "#bbbbbb"))
            for pid in part_ids(c, base_parts, layer):
                t = facets(c, spec, pid)
                if len(t):
                    polys = t[:, :, [i, j]] * np.array([1, ys])
                    cols = [rgb]
                    if layer.get("bands"):
                        # colour each facet by which X band its centre falls in: {"x": [cuts], "colors": [n + 1]}
                        cuts = layer["bands"]["x"]
                        palette = [hex_rgb(h) for h in layer["bands"]["colors"]]
                        cols = [palette[int(np.searchsorted(cuts, xc))] for xc in t[:, :, 0].mean(axis=1)]
                    ax.add_collection(PolyCollection(polys, facecolors=cols, edgecolors=cols, linewidths=0.3, alpha=layer.get("alpha", 1.0)))
                if layer.get("edges", True):
                    ls = [l[:, [i, j]] * np.array([1, ys]) for l in edges(c, spec, pid)]
                    if ls:
                        ax.add_collection(LineCollection(ls, colors="#333333", linewidths=0.6))
            for lab in ([{"text": layer["label"], "at": layer["labelAt"]}] if layer.get("label") and layer.get("labelAt") else []) + layer.get("labels", []):
                x, y = lab["at"]
                ax.text(x, y * ys, lab["text"], ha="center", va="center", fontsize=lab.get("size", 11), fontweight="bold", color=lab.get("color", "#0b0b0b"))
        ax.autoscale_view()
        ax.set_aspect("equal")
        lo, hi = ax.get_ylim()
        for xl in rs.get("xlines", []):
            # [x mm, label]: a dashed station line across the whole view
            ax.plot([xl[0], xl[0]], [lo, hi], "--", color=INK2, lw=1)
            ax.text(xl[0], hi, " " + xl[1], ha="left", va="bottom", fontsize=9, color=INK2)
        if xr:
            ax.set_xlim(*xr)
        if ys != 1:
            ax.text(1.0, -0.04, ("width" if view == "plan" else "height") + " exaggerated x%g" % ys, transform=ax.transAxes, ha="right", va="top", fontsize=8, color=MUTED)
    if rs.get("title"):
        ax.set_title(rs["title"], fontsize=11, color=INK2)
    save(fig, rs["out"])


def main(path):
    spec = json.loads(open(path).read())
    c = OnshapeClient()
    base_parts = c.get(f"/api/v10/parts/d/{spec['doc']}/w/{spec['ws']}/e/{spec['elem']}",
                       {"includeSurfaces": "true", "includeWires": "true"})
    for rs in spec["renders"]:
        render(c, spec, rs, base_parts)


if __name__ == "__main__":
    main(sys.argv[1])
