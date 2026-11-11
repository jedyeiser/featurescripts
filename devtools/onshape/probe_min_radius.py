"""Smallest radii of curvature on the faces of sheet bodies whose name contains a substring (3x3 samples per face, evFaceCurvature). Reports, per body, the 12 faces with the
smallest radius: radius (mm), area, centroid. An offset (Move face) larger than the local radius of curvature folds the surface. Read-only, 1 eval.
  PROBE_STUDIO=... PROBE_RB=7 python devtools/onshape/probe_min_radius.py <name> [name2 ...]   (exact body names)"""
import json, os, re, sys
from sync.core.client import OnshapeClient
c = OnshapeClient()
D = os.environ.get("PROBE_DOC", "08e8748f2ef24eea16072b75")
W = os.environ.get("PROBE_WS", "e24fbbae7712ef62dd256c89")
E = os.environ.get("PROBE_STUDIO", "ce67c6fa6edd49aba4637689")
names = sys.argv[1:]
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    const wanted = __NAMES__;
    for (var b in evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.SHEET)))
    {
        var nm = "";
        try silent { nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }); }
        if (!(nm is string) || indexOf(wanted, nm) < 0)
            continue;
        var rows = [];
        for (var f in evaluateQuery(context, qOwnedByBody(b, EntityType.FACE)))
        {
            var minRadius = 1e9;
            try
            {
                var params = [];
                for (var u in [0.2, 0.5, 0.8])
                    for (var v in [0.2, 0.5, 0.8])
                        params = append(params, vector(u, v));
                const curv = evFaceCurvature(context, { "face" : f, "parameters" : params });
                if (size(out) < 2)
                    out = append(out, "S sample: " ~ toString(curv));
                for (var cv in curv)
                {
                    const k = max(abs(cv.minCurvature / (1 / meter)), abs(cv.maxCurvature / (1 / meter)));
                    if (k > 1e-6)
                        minRadius = min(minRadius, 1000 / k);      // mm (k is 1/m)
                }
            }
            catch (curvErr)
            {
                if (size(out) < 6)
                    out = append(out, "S curvature failed: " ~ toString(curvErr));
            }
            if (minRadius < 1e8)
            {
                const c = evApproximateCentroid(context, { "entities" : f });
                rows = append(rows, { "r" : minRadius, "area" : evArea(context, { "entities" : f }) / (millimeter ^ 2), "c" : c });
            }
        }
        rows = sort(rows, function(a, b) { return a.r - b.r; });
        out = append(out, "S BODY " ~ nm ~ ": " ~ size(rows) ~ " faces measured");
        for (var i = 0; i < min(12, size(rows)); i += 1)
            out = append(out, "S   radius " ~ roundToPrecision(rows[i].r, 2) ~ " mm, area " ~ roundToPrecision(rows[i].area, 2) ~ " mm2, centroid (" ~ roundToPrecision(rows[i].c[0] / millimeter, 1) ~ ", " ~ roundToPrecision(rows[i].c[1] / millimeter, 1) ~ ", " ~ roundToPrecision(rows[i].c[2] / millimeter, 1) ~ ")");
    }
    return out;
}""".replace("__NAMES__", json.dumps(names))
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT}, query_params={"rollbackBarIndex": os.environ.get("PROBE_RB", "7")})
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)
