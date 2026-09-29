"""Persistent headless Onshape browser driven by command files (for interactive UI automation).

Starts sync's OnshapeBrowser (signed-in storage state restored in-process -- no secrets pass through any
tool), then polls QUEUE for cmd_<n>.py files. Each file is exec()'d with `page`, `b` (browser), `shot(name)`
and `out(text)` in scope; output goes to QUEUE/out_<n>.txt. Write QUEUE/stop to end (state is saved).

usage (repo root): PYTHONPATH=. python devtools/onshape/templates/ui_driver.py <queue_dir> [url]
"""
import os
import sys
import time
import traceback
from pathlib import Path

from sync.core.browser import OnshapeBrowser

Q = Path(sys.argv[1])
Q.mkdir(parents=True, exist_ok=True)
URL = sys.argv[2] if len(sys.argv) > 2 else None
IMG = Q / "img"
IMG.mkdir(exist_ok=True)

with OnshapeBrowser(headless=True) as b:
    page = b.page
    print("logged in:", b.is_logged_in(), flush=True)
    if URL:
        page.goto(URL, wait_until="domcontentloaded")
    (Q / "ready").write_text("ok")
    n_done = set()
    while True:
        if (Q / "stop").exists():
            break
        cmds = sorted(Q.glob("cmd_*.py"), key=lambda p: int(p.stem.split("_")[1]))
        for cf in cmds:
            if cf.name in n_done:
                continue
            n_done.add(cf.name)
            n = cf.stem.split("_")[1]
            buf = []

            def out(*a):
                buf.append(" ".join(str(x) for x in a))

            def shot(name, **kw):
                p = IMG / (name + ".png")
                page.screenshot(path=str(p), **kw)
                buf.append("shot: " + str(p))
                return p

            try:
                exec(cf.read_text(), {"page": page, "b": b, "out": out, "shot": shot, "time": time, "Path": Path})
            except Exception:
                buf.append("EXC: " + traceback.format_exc())
            (Q / ("out_" + n + ".txt")).write_text("\n".join(buf), encoding="utf-8")
            try:
                b.save_state()
            except Exception:
                pass
        time.sleep(0.3)
