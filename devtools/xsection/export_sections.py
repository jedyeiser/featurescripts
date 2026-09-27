"""Read-only: export stored section outlines + body Q66 from the xSection studios (eval API) to sections/.

usage (repo root, Git Bash):  FS_SYNC_TIMEOUT=300 MSYS_NO_PATHCONV=1 python devtools/xsection/export_sections.py
"""
import json, os, sys
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
sys.path.insert(0, ROOT)
os.chdir(ROOT)
from sync.core.client import OnshapeClient

HERE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "sections")
TPL = r'''
function(context is Context, queries)
{
    const data = getAttribute(context, { "entity" : qOrigin(EntityType.BODY), "name" : "CrossSectionAnalysis" });
    var out = [];
    for (var entry in data)
    {
        const det = entry.value.details;
        for (var b in det.bodies)
        {
            var g = -1;
            if (b.hasMaterialData == true && b.materialData != undefined && b.materialData.qMatrix != undefined)
            {
                g = b.materialData.qMatrix[2][2] / (1e9 * pascal);
            }
            out = append(out, "B|" ~ toString(b.bodyIdx) ~ "|" ~ toString(g) ~ "|" ~ toString(b.materialName));
        }
        for (var si in STATIONS)
        {
            const s = det.crossSections[si];
            out = append(out, "S|" ~ toString(si) ~ "|" ~ toString(s.xCoord / millimeter) ~ "|" ~ toString(s.GJ_eff / (newton * meter * meter)));
            for (var bd in s.bodyData)
            {
                var stack = [];
                for (var gr in bd.groups)
                {
                    stack = append(stack, [gr, 0]);
                }
                while (size(stack) > 0)
                {
                    const top = stack[size(stack) - 1];
                    stack = resize(stack, size(stack) - 1);
                    var pts = "";
                    for (var idx in top[0].perimeterPointIndices)
                    {
                        const p = s.sectionPoints[idx].point2D;
                        pts = pts ~ toString(roundToPrecision(p[0] / millimeter, 5)) ~ "," ~ toString(roundToPrecision(p[1] / millimeter, 5)) ~ ";";
                    }
                    out = append(out, "L|" ~ toString(si) ~ "|" ~ toString(bd.bodyIdx) ~ "|" ~ toString(top[1]) ~ "|" ~ pts);
                    if (top[0].subgroups != undefined)
                    {
                        for (var sg in top[0].subgroups)
                        {
                            stack = append(stack, [sg, top[1] + 1]);
                        }
                    }
                }
            }
        }
    }
    return out;
}'''


def strings(v, acc):
    if isinstance(v, dict):
        if v.get("btType", "").endswith("BTFSValueString"):
            acc.append(v["value"])
        for x in v.values():
            if isinstance(x, (list, dict)):
                strings(x, acc)
    elif isinstance(v, list):
        for x in v:
            strings(x, acc)
    return acc


d = json.load(open("xSection/.document.json"))
c = OnshapeClient()
for studio, eid, st in [("ROY", "b406721e2f4fa7a0e28b2810", [20, 41]), ("Test", "489f267862a807871c8aa5f5", [12])]:
    r = c.post(f"/api/v10/partstudios/d/{d['document_id']}/w/{d['workspace_id']}/e/{eid}/featurescript",
               json_data={"script": TPL.replace("STATIONS", json.dumps(st))})
    rows = [x for x in strings(r.get("result"), []) if x[:2] in ("B|", "S|", "L|")]
    open(os.path.join(HERE, f"sec_{studio}.txt"), "w").write("\n".join(rows))
    print(studio, len(rows), r.get("notices") or "")
    for x in rows:
        if x.startswith(("B|", "S|")):
            print("  ", x)
