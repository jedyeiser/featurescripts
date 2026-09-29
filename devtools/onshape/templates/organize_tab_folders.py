"""Put the template set into Onshape tab folders (one per brand) and the old templates into
"LEGACY (old templates)" -- moves only (sync's TabBar: drag into folder / move to parent), nothing is edited or
deleted. Uses a very wide headless viewport so every root tab is on screen for the drag.
usage (repo root): PYTHONPATH=. python devtools/onshape/templates/organize_tab_folders.py [--dry]"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from _common import TD, TW, elements  # noqa: E402
from template_set import BRANDS, load_set  # noqa: E402
from sync.core.browser import OnshapeBrowser, TabBar  # noqa: E402

LEGACY = "LEGACY (old templates)"
LEGACY_NAMES = ["K2_Drawing_Template", "K2_Drawing_Template.dwt", "LINE_Drawing_Template",
                "LINE_Drawing_Template.dwt", "MADSHUS_Drawing_Template", "MADSHUS_Drawing_Template.dwt",
                "New_LINE_Logo.png", "madshus_logo.png", "line_logo.jpg", "k2-logo.png"]

st = load_set()
by_name = {e["name"]: e["id"] for e in elements(TD, TW)}
want = {}
for t in st["templates"].values():
    f = BRANDS[t["brand"]]["folder"]
    for k in ("drawing", "dwt", "logo"):
        if t.get(k):
            want[t[k]] = f
for n in LEGACY_NAMES:
    if n in by_name:
        want[by_name[n]] = LEGACY
anchor = st["templates"]["EOC A4"]["drawing"]

with OnshapeBrowser(headless=True) as b:
    b.page.set_viewport_size({"width": 3800, "height": 1000})
    b.open_element(TD, TW, anchor)
    bar = TabBar(b)
    bar.wait_ready()
    b.page.wait_for_timeout(6000)
    structure = bar.read_structure()
    print("before:", json.dumps({f: sorted(m.values()) for f, m in structure.items() if f}, indent=1))
    if "--dry" in sys.argv:
        raise SystemExit
    current = {eid: f for f, m in structure.items() for eid in m}
    # the old logos sit alone in folder IMAGES: move that whole folder into LEGACY instead of emptying it
    images_only_old = "IMAGES" in structure and set(structure["IMAGES"].values()) <= set(LEGACY_NAMES)
    if images_only_old:
        for eid in structure["IMAGES"]:
            want.pop(eid, None)
    names = {eid: n for m in structure.values() for eid, n in m.items()}
    for eid, f in want.items():
        have = current.get(eid)
        if have is None:
            print("not visible:", eid); continue
        if have == f:
            continue
        if have:
            bar.open_folder(have)
            bar.move_to_parent(eid)
            bar.go_home()
        if f not in structure:
            bar.create_folder(f)
            structure[f] = {}
            print("created folder", f)
        bar.drag_into_folder(eid, f)
        current[eid] = f
        print(names[eid], ":", have or "/", "->", f)
    if images_only_old:
        if LEGACY not in structure:
            bar.create_folder(LEGACY)
        fid = next(t["id"] for t in bar.tabs() if t["folder"] and t["name"] == "IMAGES")
        try:
            bar.drag_into_folder(fid, LEGACY)
            print("folder IMAGES -> " + LEGACY)
        except Exception as e:
            print("IMAGES folder drag failed:", str(e)[:200])
    structure = bar.read_structure()
    print("after:", json.dumps({f: sorted(m.values()) for f, m in structure.items()}, indent=1))
    b.save_state()
