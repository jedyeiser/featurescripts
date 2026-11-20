"""Check the QVX derive/split test ("QVX derive tests" studio, built by build_qvx_split_tests.py).

Two FeatureScript evals (one before the splits via rollbackBarIndex=2, one at the end) plus one features GET.
For `wires`, `w1` and L2 = wires - w1 they report: how many entities the variable returns and each one's x extent (mm);
plus the ground truth: wire bodies in the studio and bodies still carrying the saved-variable attribute.

Expected before the splits: wires 2, w1 1, L2 1, wire bodies 2 (plus the sketch is not a body), tagged 2.
After: wire bodies 3 (L1 split in two, L2 trimmed to one side). Compare what each variable returns.

usage (repo root): ONSHAPE_CALL_CAP=6 PYTHONPATH=. python devtools/onshape/check_qvx_split_tests.py
"""
import json

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("query_variable_x/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
E = [e for e in c.list_elements(D, W) if e["name"] == "QVX derive tests"][0]["id"]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"

SCRIPT = '''function(context is Context, queries) {
  var out = {};
  const wiresQ = try silent(getQueryVariable(context, "wires"));
  const w1Q = try silent(getQueryVariable(context, "w1"));
  var sets = {};
  sets["wires"] = wiresQ;
  sets["w1"] = w1Q;
  if (wiresQ != undefined && w1Q != undefined)
  {
    sets["L2"] = qSubtraction(wiresQ, w1Q);
  }
  for (var name, q in sets)
  {
    if (q == undefined)
    {
      out[name] = "undefined";
      continue;
    }
    const ents = evaluateQuery(context, q);
    var xs = [];
    for (var e in ents)
    {
      const bx = try silent(evBox3d(context, { "topology" : e }));
      xs = append(xs, bx == undefined ? "?" : round(bx.minCorner[0] / millimeter) ~ ".." ~ round(bx.maxCorner[0] / millimeter));
    }
    out[name] = size(ents) ~ " " ~ toString(xs);
  }
  out["wireBodies"] = size(evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.WIRE)));
  out["taggedBodies"] = size(evaluateQuery(context, qHasAttribute(qEverything(EntityType.BODY), "queryVariableName")));
  return toString(out);
}'''

tree = c.get(f"{BASE}/features")
for f in tree["features"]:
    print("%-8s %s" % (tree["featureStates"][f["featureId"]]["featureStatus"], f["name"]))
for label, params in (("BEFORE splits (rollback 2)", {"rollbackBarIndex": "2"}), ("AFTER splits (end)", None)):
    raw = c.post(f"{BASE}/featurescript", json_data={"script": SCRIPT}, query_params=params)["result"]["value"]
    print("%-28s %s" % (label, raw))
