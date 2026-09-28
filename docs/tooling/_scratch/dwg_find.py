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
    print([f.url[:80] for f in p.frames])
    for f in p.frames:
        els = f.locator("[title*='able' i], [aria-label*='able' i], [data-original-title*='able' i], [data-bs-original-title*='able' i], [tooltip*='able' i]")
        n = els.count()
        for i in range(min(n, 30)):
            e = els.nth(i)
            try:
                bb = e.bounding_box()
                print(f.url[:40], e.get_attribute("title"), e.get_attribute("aria-label"), e.get_attribute("data-original-title"), bb)
            except Exception as ex:
                pass
    br.close()
