"""Check the Query Variable X tests built by build_qvx_tests.py ("QVX tests" studio).

One FeatureScript eval returns the size of every test variable; feature statuses come from one features GET.
Prints PASS/FAIL per test. Exit code 1 on any FAIL.

usage (repo root): PYTHONPATH=. python devtools/onshape/check_qvx_tests.py
"""
import json
import re
import sys

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("query_variable_x/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
E = [e for e in c.list_elements(D, W) if e["name"] == "QVX tests"][0]["id"]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"

EXPECT = {"t1": 6, "t2": 8, "t3": 4, "t4": 10, "t5": 2, "t6": 2, "t7": 3, "t8": 1, "t9": 13, "t10": 7, "t11": 6, "t12": 1, "t13": 5,
          "t14": 1, "t15": 1, "t16": 0, "t17": 1, "t18": 3}
SCRIPT = '''function(context is Context, queries) {
  var out = {};
  for (var n in NAMES) {
    const q = try silent(getQueryVariable(context, n));
    out[n] = q == undefined ? -1 : size(evaluateQuery(context, q));
  }
  out["solidFaces"] = size(evaluateQuery(context, qBodyType(qEverything(EntityType.FACE), BodyType.SOLID)));
  return toString(out);
}'''.replace("NAMES", json.dumps(list(EXPECT)))

tree = c.get(f"{BASE}/features")
status = {f["name"]: tree["featureStates"][f["featureId"]]["featureStatus"] for f in tree["features"]}
raw = c.post(f"{BASE}/featurescript", json_data={"script": SCRIPT})["result"]["value"]
got = {m.group(1): int(m.group(2)) for m in re.finditer(r"(\w+) : (-?\d+)", raw)}

fails = 0
print("solid faces in fixture: %s (expect 16)" % got.get("solidFaces"))
for name, st in status.items():
    if st != "OK":
        print("STATUS %-8s %s" % (st, name))
for t, want in EXPECT.items():
    have = got.get(t)
    ok = have == want
    fails += 0 if ok else 1
    label = [n for n in status if n.startswith(t + " ")]
    print("%s  %-4s want %-3s got %-3s  %s" % ("PASS" if ok else "FAIL", t, want, have, label[0][len(t) + 1:] if label else "(feature missing)"))
print("%d failure(s)" % fails)
sys.exit(1 if fails else 0)
