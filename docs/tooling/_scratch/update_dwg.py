import sys
from playwright.sync_api import sync_playwright
from sync.core.browser import state_file, base_url
D, W = "73271cfcd708b3f5e3fc315c", "fea83d30106dba0a4e066761"
eid, out = sys.argv[1], sys.argv[2]
with sync_playwright() as pw:
    br = pw.chromium.launch()
    ctx = br.new_context(storage_state=str(state_file()), viewport={"width": 1600, "height": 1000}, device_scale_factor=2)
    p = ctx.new_page()
    p.goto(f"{base_url()}/documents/{D}/w/{W}/e/{eid}", wait_until="domcontentloaded")
    p.wait_for_timeout(30000)
    btn = p.locator("[data-bs-original-title*='pdate'], [title*='pdate'], [aria-label*='pdate']").first
    print("buttons:", p.locator("[title*='pdate'], [aria-label*='pdate'], [data-bs-original-title*='pdate']").count())
    p.mouse.move(109, 46)
    p.wait_for_timeout(1500)
    tip = p.locator(".tooltip, .os-tooltip, [role=tooltip]").all_inner_texts()
    print("tooltip:", tip)
    p.mouse.click(109, 46)
    p.wait_for_timeout(25000)
    p.screenshot(path=out)
    br.close()
