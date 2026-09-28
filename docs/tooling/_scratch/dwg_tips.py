import sys
from playwright.sync_api import sync_playwright
from sync.core.browser import state_file, base_url
D, W = "73271cfcd708b3f5e3fc315c", "fea83d30106dba0a4e066761"
with sync_playwright() as pw:
    br = pw.chromium.launch()
    ctx = br.new_context(storage_state=str(state_file()), viewport={"width": 1600, "height": 1000}, device_scale_factor=2)
    p = ctx.new_page()
    p.goto(f"{base_url()}/documents/{D}/w/{W}/e/{sys.argv[1]}", wait_until="domcontentloaded")
    p.wait_for_timeout(35000)
    for x in range(180, 1400, 32):
        p.mouse.move(x, 58); p.wait_for_timeout(900)
        t = p.locator(".tooltip:visible, [role=tooltip]:visible, .os-tooltip:visible").all_inner_texts()
        if t: print(x, t[:1])
    br.close()
