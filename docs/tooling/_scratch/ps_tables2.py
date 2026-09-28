import sys
from playwright.sync_api import sync_playwright
from sync.core.browser import state_file, base_url
D, W = "73271cfcd708b3f5e3fc315c", "fea83d30106dba0a4e066761"
with sync_playwright() as pw:
    br = pw.chromium.launch()
    ctx = br.new_context(storage_state=str(state_file()), viewport={"width": 1600, "height": 1000}, device_scale_factor=2)
    p = ctx.new_page()
    p.goto(f"{base_url()}/documents/{D}/w/{W}/e/bb2cddb24faf53d9d043c38e", wait_until="domcontentloaded")
    p.wait_for_timeout(35000)
    p.mouse.click(1584, 476)
    p.wait_for_timeout(8000)
    p.mouse.click(1220, 20)
    p.wait_for_timeout(5000)
    p.screenshot(path=sys.argv[1])
    txt = p.inner_text("body")
    i = txt.find("Station")
    j = txt.rfind("Regeneration complete"); print(txt[j:j + 1500])
    br.close()
