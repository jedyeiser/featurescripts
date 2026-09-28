import sys
from playwright.sync_api import sync_playwright
from sync.core.browser import state_file, base_url
D, W, E = "9732a1a905d6ac91d8990105", "4b70b4378f844dff881b5282", "093984ec4a1e35072e737852"
with sync_playwright() as pw:
    br = pw.chromium.launch()
    ctx = br.new_context(storage_state=str(state_file()), viewport={"width": 1600, "height": 1000}, device_scale_factor=2)
    p = ctx.new_page()
    p.goto(f"{base_url()}/documents/{D}/w/{W}/e/{E}", wait_until="domcontentloaded")
    p.wait_for_timeout(60000)
    p.mouse.click(1252, 20)
    p.wait_for_timeout(6000)
    txt = p.inner_text("body")
    i = txt.find("FeatureScript notices")
    open("docs/tooling/_scratch/rd_notices.txt", "w", encoding="utf-8").write(txt[i:])
    p.screenshot(path=sys.argv[1])
    br.close()
