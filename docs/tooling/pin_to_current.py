"""Re-pin named callers' import of ONE tab to that tab's current Onshape microversion (only those files).

usage (repo root): PYTHONPATH=. python docs/tooling/pin_to_current.py <project dir> <callee tab stem> <caller file> [...]
"""
import json
import re
import sys

from sync.core.client import OnshapeClient

project, callee, callers = sys.argv[1], sys.argv[2], sys.argv[3:]
c = OnshapeClient()
d = json.load(open("%s/.document.json" % project))
eid = d["feature_studios"][callee]
mv = {e["id"]: e["microversionId"] for e in c.list_elements(d["document_id"], d["workspace_id"])}[eid]
print(callee, "current microversion", mv)
for f in callers:
    s = open(f, encoding="utf-8").read()
    s2 = re.sub(r'(import\(path : "%s", version : ")[0-9a-f]{24}("\))' % eid, r"\g<1>%s\2" % mv, s)
    open(f, "w", encoding="utf-8", newline="").write(s2)
    print(f, "repinned" if s2 != s else "UNCHANGED")
