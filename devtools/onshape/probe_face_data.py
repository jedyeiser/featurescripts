"""Probe of the face data Deform could use (read-only, 1 eval): evSurfaceDefinition type per face, and for a few faces
evApproximateBSplineSurface degrees / net size / UV domain / trim loops, and how far the trim loops lie from the face's
real edges when the UV is read as the normalized box (domain check + trim accuracy at two tolerances).
  ONSHAPE_CALL_CAP=5 PYTHONPATH=. python devtools/onshape/probe_face_data.py [body name]   (default filleted_top_surf; studio PROBE_STUDIO)"""
import json, os, re, sys
from sync.core.client import OnshapeClient
c = OnshapeClient()
D, W = "08e8748f2ef24eea16072b75", "e24fbbae7712ef62dd256c89"
E = os.environ.get("PROBE_STUDIO", "e0595b3c9b7337c1b2d19b25")
NAME = sys.argv[1] if len(sys.argv) > 1 else "filleted_top_surf"
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    var body = undefined;
    for (var b in evaluateQuery(context, qEverything(EntityType.BODY)))
    {
        var nm = "";
        try silent { nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }); }
        if (nm == "__NAME__")
            body = b;
    }
    if (body == undefined)
        return ["S body not found"];
    const faces = evaluateQuery(context, qOwnedByBody(body, EntityType.FACE));
    out = append(out, "S faces " ~ size(faces));
    var detailed = 0;
    for (var i = 0; i < size(faces); i += 1)
    {
        var kind = "?";
        try silent { kind = toString(evSurfaceDefinition(context, { "face" : faces[i], "returnBSplinesAsOther" : true }).surfaceType); }
        const area = evArea(context, { "entities" : faces[i] });
        const isPlane = kind == "PLANE";
        out = append(out, "S face " ~ i ~ " " ~ kind ~ " area " ~ roundToPrecision(area / (millimeter ^ 2), 1) ~ " mm2");
        if (!(isPlane ? detailed < 99 : detailed < 6) || (i % 6 != 0 && !isPlane))
            continue;
        detailed += 1;
        for (var tol in [1e-6, 1e-8])
        {
            try
            {
                const a = evApproximateBSplineSurface(context, { "face" : faces[i], "tolerance" : tol });
                const s = a.bSplineSurface;
                const u0 = s.uKnots[0];
                const u1 = s.uKnots[size(s.uKnots) - 1];
                const v0 = s.vKnots[0];
                const v1 = s.vKnots[size(s.vKnots) - 1];
                var line = "S   approx tol " ~ tol ~ ": degree " ~ s.uDegree ~ "x" ~ s.vDegree ~ ", net " ~ size(s.controlPoints) ~ "x" ~ size(s.controlPoints[0])
                    ~ ", rational " ~ s.isRational ~ ", u [" ~ roundToPrecision(u0, 4) ~ ", " ~ roundToPrecision(u1, 4) ~ "] v [" ~ roundToPrecision(v0, 4) ~ ", " ~ roundToPrecision(v1, 4)
                    ~ "], trim curves " ~ size(a.boundaryBSplineCurves) ~ ", inner loops " ~ size(a.innerLoopBSplineCurves);
                // trim accuracy: sample each outer trim curve, read its UV as the normalized box, ask where that point of the face is, and how far from the face's edges
                var worst = 0 * meter;
                var samples = 0;
                var errText = "";
                var trimInfo = "";
                for (var cv in a.boundaryBSplineCurves)
                {
                    trimInfo = trimInfo ~ " d" ~ cv.degree ~ "/" ~ size(cv.controlPoints);
                    try
                    {
                        // control points that lie ON the curve: the end points, and every point of a degree-1 curve
                        var onCurve = [cv.controlPoints[0], cv.controlPoints[size(cv.controlPoints) - 1]];
                        if (cv.degree == 1)
                            onCurve = cv.controlPoints;
                        for (var q in onCurve)
                        {
                            const nu = (q[0] - u0) / (u1 - u0);
                            const nv = (q[1] - v0) / (v1 - v0);
                            const pl = evFaceTangentPlane(context, { "face" : faces[i], "parameter" : vector(clamp(nu, 0, 1), clamp(nv, 0, 1)) });
                            const dist = evDistance(context, { "side0" : pl.origin, "side1" : qAdjacent(faces[i], AdjacencyType.EDGE, EntityType.EDGE) }).distance;
                            worst = max(worst, dist);
                            samples += 1;
                        }
                    }
                    catch (e2) { errText = " [trim eval: " ~ toString(e2) ~ "]"; }
                }
                out = append(out, line ~ " | trim" ~ trimInfo ~ " | on-curve points " ~ samples ~ ", worst distance to the face's edges " ~ roundToPrecision(worst / meter * 1e6, 2) ~ " um" ~ errText);
            }
            catch (e) { out = append(out, "S   approx tol " ~ tol ~ " FAILED " ~ toString(e)); }
        }
    }
    return out;
}""".replace("__NAME__", NAME)
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT})
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)
