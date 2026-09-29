# Driver command file: Sheet properties > Reference := the option containing REF_TEXT (a part that has a view on
# the sheet), then OK. Expects the drawing open.
f = [x for x in page.frames if 'drawing' in x.url][0]
page.mouse.click(600, 700, button="right"); page.wait_for_timeout(1000)
f.get_by_text("Sheet properties...").first.click(); page.wait_for_timeout(2000)
idx = f.evaluate("(t) => [...document.querySelectorAll('select')].findIndex(s => [...s.options].some(o => o.text.includes(t)))", REF_TEXT)
sel = f.locator("select").nth(idx)
label = sel.evaluate("(e, t) => [...e.options].find(o => o.text.includes(t)).text", REF_TEXT)
sel.select_option(label=label); page.wait_for_timeout(1000)
page.mouse.click(296, 90); page.wait_for_timeout(5000)      # green check of the Sheet properties dialog
out("reference ->", label)
