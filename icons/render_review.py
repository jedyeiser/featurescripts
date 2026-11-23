"""Render a review.html to a PNG with Playwright (system python): python icons/render_review.py <review.html> <out.png>"""
import pathlib
import sys

from playwright.sync_api import sync_playwright

src = pathlib.Path(sys.argv[1]).resolve()
out = pathlib.Path(sys.argv[2]).resolve()
with sync_playwright() as p:
    browser = p.chromium.launch()
    page = browser.new_page(viewport={"width": 1000, "height": 560}, device_scale_factor=2)
    page.goto(src.as_uri())
    page.screenshot(path=str(out), full_page=True)
    browser.close()
print("wrote", out)
