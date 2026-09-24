"""Run a FeatureScript snippet against a Part Studio through the eval API and print what it returns.

usage (from the repo root, Git Bash):
    PYTHONPATH=. MSYS_NO_PATHCONV=1 python devtools/onshape/fseval.py <script.fs> <did> <wid> <eid> [configuration]

The script is ONE anonymous `function(context is Context, queries) { ... }` returning an array
of strings; only onshape/std is in scope (our tabs are not). It sees the END of the tree.
`configuration` is e.g. "List_xxx=Value;List_yyy=Value" (sent as a signed query parameter;
in the body it is silently ignored). The first regeneration after a push can exceed the
client's 30 s timeout -- just run it again.
"""
import json
import re
import sys

from sync.core.client import OnshapeClient


def run(script, did, wid, eid, configuration=None):
    c = OnshapeClient()
    path = "/api/v10/partstudios/d/%s/w/%s/e/%s/featurescript" % (did, wid, eid)
    qp = {"configuration": configuration} if configuration else None
    return c.post(path, json_data={"script": script}, query_params=qp)


def show(result):
    for entry in result.get("notices") or []:
        print("[%s] %s" % (entry.get("level"), entry.get("message")))
    console = result.get("console")
    if console:
        print(console if isinstance(console, str) else json.dumps(console, indent=2))
    blob = json.dumps(result.get("result"))
    if blob and blob != "null":
        for s in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', blob):
            if s not in ("BTFSValueString", "BTFSValueArray"):
                print(s.encode("ascii", "replace").decode("ascii"))


if __name__ == "__main__":
    src = open(sys.argv[1], encoding="ascii").read()
    show(run(src, sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5] if len(sys.argv) > 5 else None))
