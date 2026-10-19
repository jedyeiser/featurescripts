"""Acceptance checks for the Deform stress studio: bodies, faces, naked edges, can it be offset, can it be thickened.
  ONSHAPE_CALL_CAP=5 PYTHONPATH=. python devtools/onshape/check_deform_stress.py   (1 eval)"""
import json, os, re
from sync.core.client import OnshapeClient
c = OnshapeClient()
D, W = "08e8748f2ef24eea16072b75", "e24fbbae7712ef62dd256c89"
E = os.environ.get("STRESS_STUDIO", "e0595b3c9b7337c1b2d19b25")
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    var n = 0;
    for (var b in evaluateQuery(context, qEverything(EntityType.BODY)))
    {
        var nm = "";
        try silent { nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }); }
        if (!(nm is string))
            continue;
        if (!(nm == "filleted_top_surf (deformed)" || (nm != "" && match(nm, "^stress_.* \\\\(deformed\\\\)$").hasMatch)))
            continue;
        n += 1;
        const isSolid = size(evaluateQuery(context, qBodyType(b, BodyType.SOLID))) > 0;
        const faces = qOwnedByBody(b, EntityType.FACE);
        var naked = 0;
        for (var e in evaluateQuery(context, qOwnedByBody(b, EntityType.EDGE)))
            if (size(evaluateQuery(context, qAdjacent(e, AdjacencyType.EDGE, EntityType.FACE))) == 1)
                naked += 1;
        var offsetOk = "no";
        var thickOk = "no";
        const idO = makeId("o" ~ n);
        try
        {
            opExtractSurface(context, idO, { "faces" : faces, "offset" : 1 * millimeter, "useFacesAroundToTrimOffset" : false });
            offsetOk = "ok(" ~ size(evaluateQuery(context, qCreatedBy(idO, EntityType.FACE))) ~ " faces)";
        }
        catch (err) { offsetOk = "FAIL " ~ toString(err); }
        const idT = makeId("t" ~ n);
        try
        {
            if (isSolid)
            {
                opOffsetFace(context, idT, { "moveFaces" : faces, "offsetDistance" : 0.3 * millimeter });
                thickOk = "ok(offsetFace)";
            }
            else
            {
                opThicken(context, idT, { "entities" : b, "thickness1" : 0.5 * millimeter, "thickness2" : 0 * millimeter, "keepTools" : true });
                thickOk = "ok(" ~ size(evaluateQuery(context, qBodyType(qCreatedBy(idT, EntityType.BODY), BodyType.SOLID))) ~ " solid)";
            }
        }
        catch (err) { thickOk = "FAIL " ~ toString(err); }
        out = append(out, "S " ~ nm ~ " | " ~ (isSolid ? "solid" : "sheet") ~ " | faces " ~ size(evaluateQuery(context, faces)) ~ " | naked edges " ~ naked ~ " | offset " ~ offsetOk ~ " | thicken " ~ thickOk);
    }
    out = append(out, "S bodies checked " ~ n);
    return out;
}"""
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT})
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)
