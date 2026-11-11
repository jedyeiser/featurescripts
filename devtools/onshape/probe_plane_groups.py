"""How much could be merged into single planar faces? Region-grows groups of ADJACENT faces whose sample points all lie within TOL of one plane
(the plane of the group's largest face). Read-only, 1 eval. Prints each group of >= 2 faces: faces, total area, worst deviation, centroid range,
and the totals (faces absorbed). TOL in micrometers (default 50).
  ONSHAPE_CALL_CAP=5 PYTHONPATH=. python devtools/onshape/probe_plane_groups.py "<body>" [tol_um] [rollbackBarIndex]   (env PROBE_STUDIO)"""
import json, os, re, sys
from sync.core.client import OnshapeClient
c = OnshapeClient()
D = os.environ.get("PROBE_DOC", "08e8748f2ef24eea16072b75")
W = os.environ.get("PROBE_WS", "e24fbbae7712ef62dd256c89")
E = os.environ.get("PROBE_STUDIO", "074a17ad5d8f7e3faa3346b2")
name = sys.argv[1]
tol = sys.argv[2] if len(sys.argv) > 2 else "50"
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    var body = undefined;
    for (var b in evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.SHEET)))
    {
        var nm = "";
        try silent { nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }); }
        if (nm == "__NAME__")
            body = b;
    }
    if (body == undefined)
        return ["S body not found"];
    const tol = __TOL__ * 1e-6 * meter;
    const faces = evaluateQuery(context, qOwnedByBody(body, EntityType.FACE));
    const n = size(faces);
    var points = [];       // per face: sample points
    var area = makeArray(n, 0);
    var plane = [];        // per face: its own tangent plane at the centre
    for (var i = 0; i < n; i += 1)
    {
        var params = [];
        for (var a = 0; a <= 4; a += 1)
            for (var k = 0; k <= 4; k += 1)
                params = append(params, vector(0.02 + 0.96 * a / 4, 0.02 + 0.96 * k / 4));
        var pts = [];
        var pl = undefined;
        try silent
        {
            const planes = evFaceTangentPlanes(context, { "face" : faces[i], "parameters" : params });
            for (var q in planes)
                pts = append(pts, q.origin);
            pl = planes[12];
            area[i] = evArea(context, { "entities" : faces[i] }) / (meter ^ 2);
        }
        points = append(points, pts);
        plane = append(plane, pl);
    }
    // adjacency
    var adj = makeArray(n, []);
    for (var i = 0; i < n; i += 1)
    {
        for (var nb in evaluateQuery(context, qAdjacent(qAdjacent(faces[i], AdjacencyType.EDGE, EntityType.EDGE), AdjacencyType.EDGE, EntityType.FACE)))
            for (var j = 0; j < n; j += 1)
                if (j != i && nb == faces[j])
                    adj[i] = append(adj[i], j);
    }
    var order = sort(range(0, n - 1), function(a, b) { return area[b] - area[a]; });
    var assigned = makeArray(n, false);
    var absorbed = 0;
    var groups = 0;
    for (var seed in order)
    {
        if (assigned[seed] || plane[seed] == undefined)
            continue;
        const p0 = plane[seed].origin;
        const nrm = plane[seed].normal;
        var members = [seed];
        assigned[seed] = true;
        var frontier = [seed];
        var worst = 0 * meter;
        while (size(frontier) > 0)
        {
            var next = [];
            for (var f in frontier)
                for (var g in adj[f])
                {
                    if (assigned[g] || size(points[g]) == 0)
                        continue;
                    var dev = 0 * meter;
                    for (var p in points[g])
                        dev = max(dev, abs(dot(p - p0, nrm)));
                    if (dev < tol)
                    {
                        assigned[g] = true;
                        members = append(members, g);
                        next = append(next, g);
                        worst = max(worst, dev);
                    }
                }
            frontier = next;
        }
        if (size(members) >= 2)
        {
            var total = 0;
            for (var m in members)
                total += area[m];
            groups += 1;
            absorbed += size(members) - 1;
            out = append(out, "S group of " ~ size(members) ~ " faces, total " ~ roundToPrecision(total / 1e-6, 1) ~ " mm2, largest " ~ roundToPrecision(area[seed] / 1e-6, 1) ~ " mm2, worst deviation " ~ roundToPrecision(worst / meter * 1e6, 1) ~ " um, seed centre ("
                ~ roundToPrecision(p0[0] / millimeter, 0) ~ ", " ~ roundToPrecision(p0[1] / millimeter, 0) ~ ", " ~ roundToPrecision(p0[2] / millimeter, 0) ~ "), faces " ~ toString(members));
        }
    }
    out = append(out, "S TOTAL: " ~ groups ~ " plane groups would absorb " ~ absorbed ~ " of " ~ n ~ " faces (tolerance " ~ __TOL__ ~ " um)");
    return out;
}""".replace("__NAME__", name).replace("__TOL__", tol)
qp = {"rollbackBarIndex": sys.argv[3]} if len(sys.argv) > 3 else None
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT}, query_params=qp)
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)
