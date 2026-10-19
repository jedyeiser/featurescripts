"""Offset / thicken ladder: the same ops at several distances on the source ski surface and on each deformed result.
  ONSHAPE_CALL_CAP=3 PYTHONPATH=. python devtools/onshape/check_offset_ladder.py  (1 eval)"""
import json, os, re
from sync.core.client import OnshapeClient
c = OnshapeClient()
D, W = "08e8748f2ef24eea16072b75", "e24fbbae7712ef62dd256c89"
E = os.environ.get("STRESS_STUDIO", "e0595b3c9b7337c1b2d19b25")
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    var cases = [];
    for (var b in evaluateQuery(context, qCreatedBy(makeId("F0MSpNNTHSSzAXv_0"), EntityType.BODY)))
        if (size(evaluateQuery(context, qOwnedByBody(b, EntityType.FACE))) == 37 && size(evaluateQuery(context, qBodyType(b, BodyType.SHEET))) == 1)
            cases = append(cases, { "label" : "SOURCE", "body" : b });
    for (var b in evaluateQuery(context, qEverything(EntityType.BODY)))
    {
        var nm = "";
        try silent { nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }); }
        if (nm is string && match(nm, ".*\\\\(deformed\\\\)").hasMatch)
            cases = append(cases, { "label" : nm, "body" : b });
    }
    var n = 0;
    for (var cs in cases)
    {
        var offs = "";
        for (var d in [0.1, 0.15, 0.2, 0.25, 0.3, 0.4, -0.2, -1])
        {
            n += 1;
            var ok = "ok";
            try { opExtractSurface(context, makeId("o" ~ n), { "faces" : qOwnedByBody(cs.body, EntityType.FACE), "offset" : d * millimeter, "useFacesAroundToTrimOffset" : false }); }
            catch (err) { ok = "FAIL"; }
            offs = offs ~ d ~ "mm:" ~ ok ~ " ";
        }
        var thick = "";
        for (var d in [0.1, 0.15, 0.2, 0.25, 0.3, 0.4, -0.2, -1])
        {
            n += 1;
            var ok = "ok";
            try { opThicken(context, makeId("t" ~ n), { "entities" : cs.body, "thickness1" : d * millimeter, "thickness2" : 0 * millimeter, "keepTools" : true }); }
            catch (err) { ok = "FAIL"; }
            thick = thick ~ d ~ "mm:" ~ ok ~ " ";
        }
        out = append(out, "S " ~ cs.label ~ " (" ~ size(evaluateQuery(context, qOwnedByBody(cs.body, EntityType.FACE))) ~ " faces) | offset " ~ offs ~ "| thicken " ~ thick);
    }
    return out;
}"""
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT})
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)
