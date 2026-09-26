"""Save a finished research agent's final report (last assistant text in its JSONL transcript) as a brief file.
usage: python docs/tooling/save_brief.py <transcript.output> <out.md>"""
import json, sys
last = None
for line in open(sys.argv[1], encoding="utf-8"):
    try:
        rec = json.loads(line)
    except ValueError:
        continue
    msg = rec.get("message") or {}
    if msg.get("role") != "assistant":
        continue
    content = msg.get("content")
    texts = [c.get("text", "") for c in content if isinstance(c, dict) and c.get("type") == "text"] if isinstance(content, list) else [content or ""]
    t = "\n".join(x for x in texts if x)
    if t.strip():
        last = t
open(sys.argv[2], "w", encoding="utf-8").write(last or "")
print(sys.argv[2], len(last or ""), "chars")
