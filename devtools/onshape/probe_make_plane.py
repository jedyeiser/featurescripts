"""Can a face that is flat to < 0.3 um but typed B-spline be turned into a true PLANE? Read-only (the eval's ops are discarded).
For the flat faces of the deformed body: (A) opFillSurface from the face's own edges -> surface type; (B) opReplaceFace with a plane template
-> surface type of the face afterwards and the face count. 1 eval.
  ONSHAPE_CALL_CAP=5 PYTHONPATH=. python devtools/onshape/probe_make_plane.py [rollbackBarIndex]"""
import json, os, re, sys
from sync.core.client import OnshapeClient
c = OnshapeClient()
D = os.environ.get("PROBE_DOC", "08e8748f2ef24eea16072b75")
W = os.environ.get("PROBE_WS", "e24fbbae7712ef62dd256c89")
E = os.environ.get("PROBE_STUDIO", "0dd2e23b1a37d950f29b0479")
BODY = os.environ.get("PROBE_BODY", "filleted_top_surf (deformed)")
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    var body = undefined;
    for (var b in evaluateQuery(context, qEverything(EntityType.BODY)))
    {
        var nm = "";
        try silent { nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }); }
        if (nm == "__BODY__")
            body = b;
    }
    if (body == undefined)
        return ["S body not found"];
    const faces = evaluateQuery(context, qOwnedByBody(body, EntityType.FACE));
    var flat = [];
    for (var i = 0; i < size(faces); i += 1)
    {
        try silent
        {
            var params = [];
            for (var a = 0; a <= 4; a += 1)
                for (var k = 0; k <= 4; k += 1)
                    params = append(params, vector(a / 4, k / 4));
            const planes = evFaceTangentPlanes(context, { "face" : faces[i], "parameters" : params });
            var dev = 0 * meter;
            for (var pl in planes)
                dev = max(dev, abs(dot(pl.origin - planes[12].origin, planes[12].normal)));
            const area = evArea(context, { "entities" : faces[i] });
            if (dev < 0.3e-6 * meter && area > 100 * millimeter ^ 2 && evSurfaceDefinition(context, { "face" : faces[i], "returnBSplinesAsOther" : true }).surfaceType != SurfaceType.PLANE)
                flat = append(flat, { "face" : faces[i], "i" : i, "area" : area, "centre" : planes[12] });
        }
    }
    out = append(out, "S flat faces " ~ size(flat) ~ " of " ~ size(faces));
    // (A) fill from the face's own edges, on the three smallest
    var sorted = sort(flat, function(a, b) { return a.area - b.area; });
    for (var n = 0; n < min(3, size(sorted)); n += 1)
    {
        const f = sorted[n];
        try
        {
            const fid = id_("A" ~ n);
            opFillSurface(context, fid, { "edgesG0" : qAdjacent(f.face, AdjacencyType.EDGE, EntityType.EDGE), "edgesG1" : qNothing(), "edgesG2" : qNothing(), "guideVertices" : qNothing() });
            const nf = qCreatedBy(fid, EntityType.FACE);
            out = append(out, "S A face " ~ f.i ~ " (" ~ roundToPrecision(f.area / (millimeter ^ 2), 0) ~ " mm2): fill -> " ~ size(evaluateQuery(context, nf)) ~ " face(s), type "
                ~ toString(evSurfaceDefinition(context, { "face" : nf, "returnBSplinesAsOther" : true }).surfaceType) ~ ", area " ~ roundToPrecision(evArea(context, { "entities" : nf }) / (millimeter ^ 2), 1));
            opDeleteBodies(context, id_("Ad" ~ n), { "entities" : qCreatedBy(fid, EntityType.BODY) });
        }
        catch (e) { out = append(out, "S A face " ~ f.i ~ " fill FAILED " ~ toString(e)); }
    }
    // (B) replace the smallest flat face's geometry with a plane
    if (size(sorted) > 0)
    {
        const f = sorted[0];
        try
        {
            const before = size(evaluateQuery(context, qGeometry(qOwnedByBody(body, EntityType.FACE), GeometryType.PLANE)));
            opPlane(context, id_("P"), { "plane" : plane(f.centre.origin, f.centre.normal), "width" : 0.5 * meter, "height" : 0.5 * meter });
            opReplaceFace(context, id_("R"), { "replaceFaces" : f.face, "templateFace" : qCreatedBy(id_("P"), EntityType.FACE) });
            const after = size(evaluateQuery(context, qGeometry(qOwnedByBody(body, EntityType.FACE), GeometryType.PLANE)));
            out = append(out, "S B replaceFace on face " ~ f.i ~ ": plane faces before " ~ before ~ ", after " ~ after ~ ", total faces " ~ size(evaluateQuery(context, qOwnedByBody(body, EntityType.FACE))));
        }
        catch (e) { out = append(out, "S B replaceFace FAILED " ~ toString(e)); }
    }
    return out;
}""".replace("__BODY__", BODY).replace("id_(", "makeId(")
qp = {"rollbackBarIndex": sys.argv[1]} if len(sys.argv) > 1 else None
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT}, query_params=qp)
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)
