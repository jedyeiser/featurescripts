import sys, json
from playwright.sync_api import sync_playwright
from sync.core.browser import state_file, base_url
D, W = "73271cfcd708b3f5e3fc315c", "fea83d30106dba0a4e066761"
steps = json.loads(sys.argv[2])   # list of ["click", x, y] / ["wait", s] / ["shot", path] / ["text", frameurlpart]
with sync_playwright() as pw:
    br = pw.chromium.launch()
    ctx = br.new_context(storage_state=str(state_file()), viewport={"width": 1600, "height": 1000}, device_scale_factor=2)
    p = ctx.new_page()
    p.goto(f"{base_url()}/documents/{D}/w/{W}/e/{sys.argv[1]}", wait_until="domcontentloaded")
    p.wait_for_timeout(35000)
    for s in steps:
        if s[0] == "click": p.mouse.click(s[1], s[2]); p.wait_for_timeout(2500)
        elif s[0] == "rclick": p.mouse.click(s[1], s[2], button="right"); p.wait_for_timeout(2500)
        elif s[0] == "dbl": p.mouse.dblclick(s[1], s[2]); p.wait_for_timeout(2500)
        elif s[0] == "wait": p.wait_for_timeout(s[1] * 1000)
        elif s[0] == "shot": p.screenshot(path=s[1])
        elif s[0] == "type": p.mouse.click(s[1], s[2]); p.keyboard.type(s[3]); p.wait_for_timeout(1500)
        elif s[0] == "clicktext":
            for f in p.frames:
                loc = f.get_by_text(s[1], exact=True)
                if loc.count():
                    loc.first.click(); p.wait_for_timeout(2500); print("clicked", s[1]); break
        elif s[0] == "waittext":
            import time as _t
            t0 = _t.time(); ok = False
            while _t.time() - t0 < s[2] and not ok:
                for f in p.frames:
                    if f.get_by_text(s[1], exact=True).count(): ok = True; break
                if not ok: p.wait_for_timeout(2000)
            print("waittext", s[1], ok, round(_t.time() - t0))
        elif s[0] == "fillafter":
            for f in p.frames:
                lab = f.get_by_text(s[1], exact=True)
                if lab.count():
                    inp = f.locator("xpath=//*[normalize-space(text())='%s']/following::input[1]" % s[1])
                    inp.first.click(); inp.first.fill(s[2]); inp.first.press("Tab"); p.wait_for_timeout(3000); print("filled", s[1]); break
        elif s[0] == "dump":
            for f in p.frames:
                if "drawing" in f.url:
                    print(f.inner_text("body")[:2500])
    br.close()
