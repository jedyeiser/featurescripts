"""fig07: Orient to reference on real test geometry (Reference side tests R1, R2, R3), normals read through the eval
API after the feature ran. Orange = the feature flipped that surface, green = it already faced the right way.
usage (repo root): PYTHONPATH=. python docs/explainers/reference_side/img/src/orient_fig.py
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "..", "tooling"))
from figstyle import *  # noqa: E402,F403

from sync.core.client import OnshapeClient  # noqa: E402

OUT = os.path.dirname(HERE)
D, W, E = "22764764a00a7f607dbc1c4d", "3b5e11a121111a161684adee", "73222f3cbcfe185c0932d0a4"
c = OnshapeClient()
FEATS = {f["name"]: f["featureId"] for f in c.get(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/features")["features"]}


def fid(prefix):
    return [i for n, i in FEATS.items() if n.startswith(prefix)][0]


def sample(case_prefix):
    """[(x, z, nx, nz, flipped)] along the middle iso-lines of every surface the case oriented, near y = 0."""
    script = """function(context is Context, queries) {
        const out = getVariable(context, toString(makeId("%s")));
        var rows = [];
        for (var pair in [[out.query.flipped.value, 1], [out.query.unchanged.value, 0]]) {
            for (var f in evaluateQuery(context, qOwnedByBody(pair[0], EntityType.FACE))) {
                var ps = [];
                for (var i = 0; i < 9; i += 1) { ps = append(ps, vector((i + 0.5) / 9, 0.5)); ps = append(ps, vector(0.5, (i + 0.5) / 9)); }
                for (var tp in evFaceTangentPlanes(context, { "face" : f, "parameters" : ps })) {
                    if (abs(tp.origin[1]) < 1 * millimeter) {
                        rows = append(rows, "ROW " ~ (tp.origin[0] / millimeter) ~ " " ~ (tp.origin[2] / millimeter) ~ " " ~ tp.normal[0] ~ " " ~ tp.normal[2] ~ " " ~ pair[1]);
                    }
                }
            }
        }
        return rows; }""" % fid(case_prefix)
    r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": script})
    rows = re.findall(r"ROW ([-\d.e]+) ([-\d.e]+) ([-\d.e]+) ([-\d.e]+) (\d)", json.dumps(r))
    return [(float(a), float(b), float(nx), float(nz), f == "1") for a, b, nx, nz, f in rows]


def panel(ax, rows, ref, title, scale=12):
    bare(ax)
    for x, z, nx, nz, flipped in rows:
        arrow(ax, (x, z), (x + nx * scale, z + nz * scale), col=ORANGE if flipped else KEEP, lw=1.4, ms=9)
    ax.plot([r[0] for r in rows], [r[1] for r in rows], ".", color=BLUE, ms=4)
    ax.plot(*ref, "o", color=RED, ms=8)
    ax.text(ref[0], ref[1], "   reference", color=RED, fontsize=9, va="center")
    ax.set_title(title, fontsize=10)


r1 = sample("R1 Orient to reference")
r2 = sample("R2 Orient to reference")
r3 = sample("R3 Orient to reference")
fig, axes = plt.subplots(1, 3, figsize=(13, 4.4), gridspec_kw={"wspace": 0.15})
panel(axes[0], r1, (11000, 0), "R1, toward the reference: both sheets\nalready faced it -- left alone (green)")
panel(axes[1], r2, (11400, 0), "R2, the same sheets, AWAY from\nthe reference: both flipped (orange)")
panel(axes[2], r3, (11800, 0), "R3, half cylinder, reference on its axis,\ntoward: flipped to point at the axis", scale=5)
for ax, x0 in ((axes[0], 11000), (axes[1], 11400)):
    ax.set_xlim(x0 - 70, x0 + 70)
    ax.set_ylim(-75, 75)
axes[2].set_xlim(11800 - 16, 11800 + 16)
axes[2].set_ylim(-4, 14)
fig.suptitle("Orient to reference on the test geometry (side view; normals evaluated in Onshape)", fontweight="bold", y=1.06)
save(fig, os.path.join(OUT, "fig07_orient_normals.png"))
