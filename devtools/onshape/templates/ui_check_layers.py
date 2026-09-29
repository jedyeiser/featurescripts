# Driver command file: reports the format layer of entities picked at sheet points CHECKS [(name, x, y)]
# (the greyed "Move to" item is the entity's current layer).
f = [x for x in page.frames if 'drawing' in x.url][0]
X = lambda x: 200 + 2.951 * x
Y = lambda y: 961 - 2.951 * y
JS = r"""() => [...document.querySelectorAll('*')].filter(e=>e.childElementCount==0 && ['Border frame','Border zones','Title block','Drawing'].includes((e.innerText||'').trim()) && e.getBoundingClientRect().width>0 && getComputedStyle(e).color=='rgb(153, 153, 153)').map(e=>(e.innerText||'').trim())"""
for name, x, y in CHECKS:
    page.mouse.click(X(300), Y(220)); page.wait_for_timeout(300)
    page.mouse.click(X(x), Y(y)); page.wait_for_timeout(400)
    page.mouse.click(X(x), Y(y), button="right"); page.wait_for_timeout(800)
    mv = f.get_by_text("Move to", exact=True)
    vis = [i for i in range(mv.count()) if mv.nth(i).is_visible()]
    if vis:
        mv.nth(vis[0]).hover(); page.wait_for_timeout(600)
        out(name, f.evaluate(JS))
    else:
        out(name, "no menu")
    page.keyboard.press("Escape"); page.mouse.click(X(300), Y(220)); page.wait_for_timeout(300)
