"""Render real Onshape bodies for the docs (read-only REST tessellation -> matplotlib PNG).

Screenshots of Onshape show every result in one colour; this colours bodies by role (region, kept / removed,
input / output) so a figure shows what a feature did. Data is the workspace's current state.

usage (repo root):  PYTHONPATH=. python docs/tooling/render_parts.py docs/decks/<slug>/renders.json

renders.json:
{
  "doc": "...", "ws": "...", "elem": "...",
  "renders": [
    {"out": "docs/decks/<slug>/shots/regions.png",
     "view": "plan" | "iso" | "side" | "section",  plan = XY, side = XZ, section = YZ (use "xfilter"), iso = 3D
                                                 (iso: "elev", "azim", "zoom" optional)
     "at": x,                                    (section only: cut every triangle with the plane X = x, draw the segments)
     "notes": [{"text", "at": [fx, fy] axes fractions, "color", "ha"}]   (optional labels anywhere),
     "xfilter": [lo, hi],                       (plan / side / section: only facets and edges centred in this X range)
     "yscale": 3,                               (plan only: exaggerate width, noted in the corner)
     "xrange": [-50, 1850],                     (mm, optional crop)
     "title": "...",
     "layers": [ {"parts": ["RPSD", ...] | "name": "exact part name" | "createdBy": "feature name" | "bodyKey": ["feature name", "top"], "color": "#1baf7a", "label": "start",
                  "alpha": 0.9, "edges": true, "labelAt": [x, y] (mm, optional; iso: [x, y, z]),
                  "key": ["feature name", "keptFaces1"]  (optional: only the faces that feature publishes under the key),
                  "edgeKey": ["feature name", "trimEdges"] (optional: draw those edges bold, "edgeColor"),
                  "cycle": ["#hex", ...] (optional: alternate edge colours along X, ticks at the joints)} , ...],
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


def created_by(c, spec, feature_name):
    """Part ids (transient ids) of the bodies a feature created."""
    import re as _re
    feats = {f["name"]: f["featureId"] for f in c.get(f"/api/v10/partstudios/d/{spec['doc']}/w/{spec['ws']}/e/{spec['elem']}/features")["features"]}
    script = """function(context is Context, queries) {
        var ids = [];
        for (var b in evaluateQuery(context, qCreatedBy(makeId("%s"), EntityType.BODY))) { ids = append(ids, "ID:" ~ b.transientId); }
        return ids; }""" % feats[feature_name]
    r = c.post(f"/api/v10/partstudios/d/{spec['doc']}/w/{spec['ws']}/e/{spec['elem']}/featurescript", json_data={"script": script})
    return _re.findall(r'ID:([A-Za-z0-9+/=]+)', json.dumps(r))


def part_ids(c, base_parts, layer):
    if "parts" in layer:
        return layer["parts"]
    if "createdBy" in layer:
        return created_by(c, layer["_spec"], layer["createdBy"])
    if "bodyKey" in layer:
        return sorted(key_ids(c, layer["_spec"], *layer["bodyKey"]))
    return [p["partId"] for p in base_parts if p["name"] == layer["name"]]


def facets(c, spec, pid):
    key = ("f", pid)
    if key not in CACHE:
        r = c.get(f"/api/v10/partstudios/d/{spec['doc']}/w/{spec['ws']}/e/{spec['elem']}/tessellatedfaces",
                  {"partId": pid, "chordTolerance": "0.0002", "angleTolerance": "0.05"})
        tris, ids = [], []
        for body in r.get("bodies", []):
            for face in body.get("faces", []):
                for f in face.get("facets", []):
                    tris.append([[v["x"] * 1000, v["y"] * 1000, v["z"] * 1000] for v in f["vertices"]])
                    ids.append(face.get("id"))
        CACHE[key] = (np.array(tris) if tris else np.zeros((0, 3, 3)), np.array(ids))
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
                    lines.append((e.get("id"), np.array(pts)))
        CACHE[key] = lines
    return CACHE[key]


def key_ids(c, spec, feature_name, key):
    """Transient ids of the entities a feature publishes under `key` (Variable_tools embedded map)."""
    import re as _re
    feats = {f["name"]: f["featureId"] for f in c.get(f"/api/v10/partstudios/d/{spec['doc']}/w/{spec['ws']}/e/{spec['elem']}/features")["features"]}
    script = """function(context is Context, queries) {
        var ids = [];
        for (var q in evaluateQuery(context, getVariable(context, toString(makeId("%s"))).query["%s"].value)) { ids = append(ids, "ID:" ~ q.transientId); }
        return ids; }""" % (feats[feature_name], key)
    r = c.post(f"/api/v10/partstudios/d/{spec['doc']}/w/{spec['ws']}/e/{spec['elem']}/featurescript", json_data={"script": script})
    return set(_re.findall(r'ID:([A-Za-z0-9+/=]+)', json.dumps(r)))


def cut_triangles(tris, x):
    """Segments where triangles cross the plane X = x (mm)."""
    segs = []
    for tri in tris:
        d = tri[:, 0] - x
        pts = []
        for a, b in ((0, 1), (1, 2), (2, 0)):
            if (d[a] < 0) != (d[b] < 0):
                f = d[a] / (d[a] - d[b])
                pts.append(tri[a] + (tri[b] - tri[a]) * f)
        if len(pts) == 2:
            segs.append(pts)
    return segs


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
        # a 3D axes is always square: make the square as wide as the figure (centred, overflowing top and bottom)
        # so a long, flat scene fills the width; saved without a tight crop (see the end of render)
        tall = size[0] / size[1]
        ax = fig.add_axes([0, 0.5 - tall / 2, 1, tall], projection="3d")
        allpts = []
        for layer in rs["layers"]:
            rgb = hex_rgb(layer.get("color", "#bbbbbb"))
            for pid in part_ids(c, base_parts, layer):
                t, _ = facets(c, spec, pid)
                if xr is not None and len(t):
                    t = t[(t[:, :, 0].mean(axis=1) >= xr[0]) & (t[:, :, 0].mean(axis=1) <= xr[1])]
                if len(t):
                    ax.add_collection3d(Poly3DCollection(t, facecolors=shade(t, rgb), edgecolors="none", alpha=layer.get("alpha", 1.0)), autolim=False)
                    allpts.append(t.reshape(-1, 3))
                if layer.get("edges", True):
                    ls = [l for _, l in edges(c, spec, pid) if xr is None or (xr[0] <= l[:, 0].mean() <= xr[1])]
                    if ls:
                        ax.add_collection3d(Line3DCollection(ls, colors="#333333", linewidths=0.6), autolim=False)
        P = np.vstack(allpts)
        lo, hi = P.min(axis=0), P.max(axis=0)
        ax.set_xlim(lo[0], hi[0]); ax.set_ylim(lo[1], hi[1]); ax.set_zlim(lo[2], hi[2])
        ax.set_box_aspect(np.maximum(hi - lo, 1e-3) * np.array([1, rs.get("yscale", 1), rs.get("zscale", 1)]), zoom=rs.get("zoom", 1))
        ax.view_init(rs.get("elev", 25), rs.get("azim", -60))
        ax.set_axis_off()
        for layer in rs["layers"]:
            # iso labels: "labelAt" is [x, y, z] in mm
            if layer.get("label") and layer.get("labelAt"):
                x, y, z = layer["labelAt"]
                ax.text(x, y, z, layer["label"], ha="center", va="bottom", fontsize=layer.get("labelSize", 10), fontweight="bold",
                        color=layer.get("labelColor", layer.get("color", "#0b0b0b")), zorder=10)
    else:
        ax = bare(fig.add_subplot(111), equal=False)
        i, j = {"plan": (0, 1), "side": (0, 2), "section": (1, 2)}[view]
        xf = rs.get("xfilter")   # [lo, hi] mm: keep only facets / edges whose centre X is inside (a slice)
        ys = rs.get("yscale", 1)
        for layer in rs["layers"]:
            rgb = hex_rgb(layer.get("color", "#bbbbbb"))
            for pid in part_ids(c, base_parts, layer):
                t, fids = facets(c, spec, pid)
                if layer.get("key") and len(t):
                    keep = key_ids(c, spec, *layer["key"])
                    t = t[np.array([f in keep for f in fids], dtype=bool)]
                if xf is not None and len(t):
                    xc = t[:, :, 0].mean(axis=1)
                    t = t[(xc >= xf[0]) & (xc <= xf[1])]
                if view == "section" and rs.get("at") is not None and len(t):
                    # a true cut: every triangle crossing the plane x = at contributes one segment in YZ
                    segs = cut_triangles(t, rs["at"])
                    if segs:
                        ax.add_collection(LineCollection([np.array(sg)[:, [1, 2]] for sg in segs], colors=[rgb], linewidths=layer.get("width", 3)))
                    t = t[:0]
                if len(t):
                    polys = t[:, :, [i, j]] * np.array([1, ys])
                    cols = [rgb]
                    if layer.get("bands"):
                        # colour each facet by which X band its centre falls in: {"x": [cuts], "colors": [n + 1]}
                        cuts = layer["bands"]["x"]
                        palette = [hex_rgb(h) for h in layer["bands"]["colors"]]
                        cols = [palette[int(np.searchsorted(cuts, xc))] for xc in t[:, :, 0].mean(axis=1)]
                    ax.add_collection(PolyCollection(polys, facecolors=cols, edgecolors=cols, linewidths=0.3, alpha=layer.get("alpha", 1.0)))
                if layer.get("edges", True) and view != "section":
                    ls = [l[:, [i, j]] * np.array([1, ys]) for _, l in edges(c, spec, pid)
                          if xf is None or (xf[0] <= l[:, 0].mean() <= xf[1])]
                    if ls and layer.get("cycle"):
                        # alternate colours edge by edge, so a wire's segmentation is visible (sorted along X)
                        ls = sorted(ls, key=lambda l: l[:, 0].mean())
                        cyc = layer["cycle"]
                        ax.add_collection(LineCollection(ls, colors=[cyc[k % len(cyc)] for k in range(len(ls))], linewidths=layer.get("lineWidth", 2.5)))
                        ends = np.array([l[0] for l in ls] + [ls[-1][-1]])
                        ax.plot(ends[:, 0], ends[:, 1], "|", color="#0b0b0b", ms=10, mew=1.2)
                    elif ls:
                        ax.add_collection(LineCollection(ls, colors=layer.get("lineColor", "#333333"), linewidths=layer.get("lineWidth", 0.6)))
                if layer.get("edgeKey") and view == "section" and rs.get("at") is not None:
                    # in a cut, an edge shows as the point where it crosses the plane
                    keep = key_ids(c, spec, *layer["edgeKey"])
                    x = rs["at"]
                    for eid, l in edges(c, spec, pid):
                        if eid not in keep:
                            continue
                        for a, b2 in zip(l[:-1], l[1:]):
                            if (a[0] - x < 0) != (b2[0] - x < 0):
                                f = (x - a[0]) / (b2[0] - a[0])
                                pnt = a + (b2 - a) * f
                                ax.plot(pnt[1], pnt[2], "o", color=layer.get("edgeColor", "#e87ba4"), ms=9, zorder=5)
                elif layer.get("edgeKey"):
                    keep = key_ids(c, spec, *layer["edgeKey"])
                    ls = [l[:, [i, j]] * np.array([1, ys]) for eid, l in edges(c, spec, pid) if eid in keep
                          and (xf is None or (xf[0] <= l[:, 0].mean() <= xf[1]))]
                    if ls:
                        ax.add_collection(LineCollection(ls, colors=layer.get("edgeColor", "#e87ba4"), linewidths=2.5))
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
    for n in rs.get("notes", []):
        # {"text", "at": [fx, fy] in axes fractions, "color"}
        (ax.text2D if view == "iso" else ax.text)(n["at"][0], n["at"][1], n["text"], transform=ax.transAxes, fontsize=n.get("size", 10), color=n.get("color", INK2),
                fontweight=n.get("weight", "bold"), ha=n.get("ha", "left"), va="center")
    if rs.get("title"):
        ax.set_title(rs["title"], fontsize=11, color=INK2)
    if view == "iso":
        os.makedirs(os.path.dirname(rs["out"]), exist_ok=True)
        fig.savefig(rs["out"], dpi=150)
        plt.close(fig)
        print("wrote", rs["out"])
    else:
        save(fig, rs["out"])


def main(path):
    spec = json.loads(open(path).read())
    c = OnshapeClient()
    base_parts = c.get(f"/api/v10/parts/d/{spec['doc']}/w/{spec['ws']}/e/{spec['elem']}",
                       {"includeSurfaces": "true", "includeWires": "true"})
    for rs in spec["renders"]:
        for layer in rs["layers"]:
            layer["_spec"] = spec
        render(c, spec, rs, base_parts)


if __name__ == "__main__":
    main(sys.argv[1])
