for _ in range(120):
    fr = [x for x in page.frames if 'drawing' in x.url]
    if fr: break
    page.wait_for_timeout(1000)
f = fr[0]
time.sleep(3)
page.keyboard.press("Escape")
page.mouse.click(1300, 700, button="right"); time.sleep(1)
f.locator("text=Zoom to sheet").first.click(); time.sleep(2)
