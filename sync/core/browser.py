"""Playwright-driven browser session against Onshape.

Covers what the REST API cannot: Feature Studio compile diagnostics, Part
Studio regen notices (via Monitor), and screenshots. The user signs in once by
hand (`sync.main login`); the session is saved and restored across runs.
"""

from __future__ import annotations

import os
from urllib.parse import quote
import time
from pathlib import Path
from typing import Any

from dotenv import load_dotenv

STATE_FILE_ENV = "FS_SYNC_BROWSER_STATE"
DEFAULT_STATE_FILE = Path.home() / ".fs-sync" / "onshape-state.json"


def state_file() -> Path:
    """Where the signed-in browser session (cookies) is persisted.

    Onshape signs in with session cookies, which Chromium drops on exit even
    from a persistent profile, so we save/restore Playwright storage state
    explicitly instead.
    """
    load_dotenv()
    return Path(os.getenv(STATE_FILE_ENV, str(DEFAULT_STATE_FILE)))


def base_url() -> str:
    load_dotenv()
    return os.getenv("ONSHAPE_BASE_URL", "https://cad.onshape.com").rstrip("/")


class OnshapeBrowser:
    """Chromium session that restores/saves the Onshape login. Use as a context manager."""

    def __init__(self, headless: bool = False, slow_mo: int = 0) -> None:
        self.headless = headless
        self.slow_mo = slow_mo
        self._pw = None
        self.browser = None
        self.context = None
        self.page = None

    def __enter__(self) -> "OnshapeBrowser":
        from playwright.sync_api import sync_playwright

        sf = state_file()
        self._pw = sync_playwright().start()
        self.browser = self._pw.chromium.launch(
            headless=self.headless,
            slow_mo=self.slow_mo,
            args=["--disable-blink-features=AutomationControlled"],
        )
        self.context = self.browser.new_context(
            storage_state=str(sf) if sf.exists() else None,
            viewport={"width": 1600, "height": 1000},
        )
        self.page = self.context.new_page()
        return self

    def __exit__(self, *exc: Any) -> None:
        # Cookies rotate; always persist the latest so the session stays alive.
        if self.context is not None:
            try:
                self.save_state()
            except Exception:
                pass
            self.context.close()
        if self.browser is not None:
            self.browser.close()
        if self._pw is not None:
            self._pw.stop()

    def save_state(self) -> Path:
        sf = state_file()
        sf.parent.mkdir(parents=True, exist_ok=True)
        self.context.storage_state(path=str(sf))
        return sf

    # ------------------------------------------------------------------ auth

    SIGNIN_FORM = "input[type='password']"

    def is_logged_in(self) -> bool:
        """True if the persisted session is still authenticated.

        Onshape renders the sign-in form in place (the URL stays /documents),
        so detect it by the password field rather than by URL.
        """
        self.page.goto(f"{base_url()}/documents", wait_until="domcontentloaded")
        try:
            self.page.wait_for_selector(
                f"{self.SIGNIN_FORM}, .os-document-list-page-container",
                timeout=20000,
            )
        except Exception:
            pass
        return self.page.locator(self.SIGNIN_FORM).count() == 0

    def login_interactive(self, timeout_s: int = 300) -> bool:
        """Show the sign-in form and wait for the user to complete login by hand."""
        self.page.goto(f"{base_url()}/documents", wait_until="domcontentloaded")
        self.page.wait_for_selector(self.SIGNIN_FORM, timeout=20000)
        self.page.bring_to_front()
        try:
            self.page.wait_for_selector(self.SIGNIN_FORM, state="detached", timeout=timeout_s * 1000)
        except Exception:
            return False
        # Let the post-login redirect settle so all cookies are set before saving.
        self.page.wait_for_load_state("networkidle")
        self.save_state()
        return True

    # ------------------------------------------------------------- navigation

    def element_url(self, document_id: str, workspace_id: str, element_id: str) -> str:
        url = f"{base_url()}/documents/{document_id}/w/{workspace_id}/e/{element_id}"
        # A Part Studio opens in its default configuration unless the URL says otherwise.
        # FS_SYNC_CONFIGURATION takes the same "List_xxx=Value;List_yyy=Value" string the
        # REST API does, so a monitored regen can be run on the configuration the user is
        # actually looking at.
        configuration = os.environ.get("FS_SYNC_CONFIGURATION")
        if configuration:
            url += "?configuration=" + quote(configuration, safe="")
        return url

    def open_element(self, document_id: str, workspace_id: str, element_id: str) -> None:
        self.page.goto(
            self.element_url(document_id, workspace_id, element_id),
            wait_until="domcontentloaded",
        )

    def screenshot(self, path: Path, full_page: bool = False) -> Path:
        path.parent.mkdir(parents=True, exist_ok=True)
        self.page.screenshot(path=str(path), full_page=full_page)
        return path

    # ---------------------------------------------------------------- notices

    NOTICES_PANE = ".notices-container"

    def wait_for_editor(self, timeout_ms: int = 60000) -> None:
        """Wait for the Feature Studio editor; raise if the sign-in form shows instead.

        networkidle never fires (Onshape holds a websocket open), so wait on Ace.
        """
        self.page.wait_for_selector(f".ace_editor, {self.SIGNIN_FORM}", timeout=timeout_ms)
        if self.page.locator(self.SIGNIN_FORM).count():
            raise RuntimeError("Browser session is not signed in to Onshape. Run: python -m sync.main login")

    def open_notices_pane(self, timeout_ms: int = 20000) -> None:
        if self.page.locator(self.NOTICES_PANE).count() == 0:
            self.page.click(".notice-pane-toggle-button")
        self.page.wait_for_selector(self.NOTICES_PANE, timeout=timeout_ms)
        # The pane mounts empty; Angular fills in the per-element sets a beat later.
        self.page.wait_for_selector(
            f"{self.NOTICES_PANE} .element-notice-set-container", timeout=timeout_ms
        )

    def read_notices(
        self,
        elements: set[str] | None = None,
        settle_ms: int = 15000,
    ) -> list[dict[str, Any]]:
        """Scrape the document-wide FeatureScript notices pane.

        Args:
            elements: tab names to report on (None = every Feature Studio tab).
                Unmonitored Part/Variable Studios sit permanently "out of date",
                so the settle wait only considers the sets we care about.
            settle_ms: how long to wait for those sets to finish recompiling.

        Returns one dict per notice:
            {element, severity ('error'|'warning'|'info'), category, message,
             line, column, out_of_date, trace: [{element, line, column}, ...]}
        """
        self.open_notices_pane()
        wanted = list(elements) if elements else None
        # Sets stream in asynchronously and a clean studio has no set at all, so
        # "loaded" = the set list has stopped changing and nothing wanted is stale.
        deadline = time.time() + settle_ms / 1000
        prev: list[str] | None = None
        stable = 0
        while time.time() < deadline:
            names = self.page.evaluate(_SET_NAMES_JS)
            stale = self.page.evaluate(_STALE_SETS_JS, wanted)
            stable = stable + 1 if names == prev else 0
            prev = names
            if stable >= 2 and not stale:
                break
            time.sleep(1.0)
        # Notices past the first few have their line:col stack rows collapsed.
        self.page.evaluate(_EXPAND_NOTICES_JS, wanted)
        notices = self.page.evaluate(_READ_NOTICES_JS)
        if elements is not None:
            notices = [n for n in notices if n["element"] in elements]
        return notices


# Notice sets whose header icon says they are a Feature Studio (vs. a monitored
# Part Studio, Variable Studio, ...).
_SET_FILTER_JS = r"""
    const isFeatureStudio = s => !!s.querySelector('.element-notices-header .compact-table-icon[icon="feature-studio-element"]');
    const nameOf = s => ((s.querySelector('.element-notice-title') || {}).innerText || '').trim();
    const sets = [...document.querySelectorAll('.notices-container .element-notice-set-container')]
        .filter(s => wanted ? wanted.includes(nameOf(s)) : isFeatureStudio(s));
"""

_SET_NAMES_JS = r"""() => [...document.querySelectorAll('.notices-container .element-notice-set-container')]
    .map(s => ((s.querySelector('.element-notice-title') || {}).innerText || '').trim()
              + ':' + s.querySelectorAll('table.feature-script-notice-table').length)"""

_STALE_SETS_JS = "(wanted) => {" + _SET_FILTER_JS + r"""
    return sets.some(s => s.querySelector('.element-notices-header').classList.contains('notices-out-of-date'));
}"""

_EXPAND_NOTICES_JS = "(wanted) => {" + _SET_FILTER_JS + r"""
    for (const s of sets) {
        for (const t of s.querySelectorAll('table.feature-script-notice-table')) {
            if (t.querySelectorAll('tr.notice-location-row').length === 1) {
                t.querySelector('tr.notice-location-row').click();
            }
        }
    }
}"""

_READ_NOTICES_JS = r"""() => {
    const out = [];
    const sets = document.querySelectorAll('.notices-container .element-notice-set-container');
    for (const set of sets) {
        const header = set.querySelector('.element-notices-header');
        const element = (set.querySelector('.element-notice-title') || {}).innerText || '';
        const outOfDate = !!(header && header.classList.contains('notices-out-of-date'));
        for (const table of set.querySelectorAll('table.feature-script-notice-table')) {
            const rows = [...table.querySelectorAll('tr.notice-location-row')];
            if (!rows.length) continue;
            const icon = rows[0].querySelector('.icon-cell div');
            const cls = icon ? icon.className : '';
            const severity = /fs-notice-error/.test(cls) ? 'error'
                           : /fs-notice-warning/.test(cls) ? 'warning'
                           : /fs-notice-info/.test(cls) ? 'info' : 'unknown';
            const category = icon ? (icon.getAttribute('data-bs-original-title') || icon.getAttribute('title') || '') : '';
            const msgEl = rows[0].querySelector('.notice-location-message');
            const message = msgEl ? msgEl.innerText.trim() : '';
            const trace = [];
            for (const r of rows.slice(1)) {
                const ln = r.querySelector('.notice-location-line-number');
                const col = r.querySelector('.notice-location-column-number');
                const where = r.querySelector('.notice-location-message');
                trace.push({
                    element: where ? where.innerText.trim() : '',
                    line: ln ? parseInt(ln.innerText, 10) : null,
                    column: col ? parseInt(col.innerText, 10) : null,
                });
            }
            const top = trace[0] || {};
            out.push({element, severity, category, message,
                      line: top.line ?? null, column: top.column ?? null,
                      out_of_date: outOfDate, trace});
        }
    }
    return out;
}"""


# ------------------------------------------------------------ part studio monitor

_MONITOR_STATE_JS = r"""(name) => {
    const s = [...document.querySelectorAll('.notices-container .element-notice-set-container')]
        .find(e => ((e.querySelector('.element-notice-title') || {}).innerText || '').trim() === name);
    if (!s) return null;
    const label = s.querySelector('.watch-this-part-studio-menu .os-tool-command-name');
    const con = s.querySelector('.part-studio-console-output');
    return {
        monitoring: !!label && /monitoring/i.test(label.innerText),
        stale: s.querySelector('.element-notices-header').classList.contains('notices-out-of-date'),
        console: con ? con.innerText : '',
    };
}"""


def monitor_part_studio(browser: OnshapeBrowser, name: str, timeout_s: int = 120) -> dict[str, Any]:
    """Turn on Monitor for a Part Studio and wait for its regeneration to finish.

    Monitoring regenerates the Part Studio and routes its runtime notices
    (println, errors with stack traces, typecheck failures) into the notices
    pane under that element.

    Returns {"monitoring": bool, "stale": bool, "console": str}.
    """
    page = browser.page
    browser.open_notices_pane()
    st = page.evaluate(_MONITOR_STATE_JS, name)
    if st is None:
        raise ValueError(f"Part Studio '{name}' is not listed in the notices pane")
    if not st["monitoring"]:
        sec = page.locator(".notices-container .element-notice-set-container").filter(
            has=page.locator(".element-notice-title", has_text=name)
        )
        sec.locator(".watch-this-part-studio-menu .os-tool-command").first.click()
    deadline = time.time() + timeout_s
    while time.time() < deadline:
        st = page.evaluate(_MONITOR_STATE_JS, name) or st
        if st["monitoring"] and not st["stale"] and "Result:" in st["console"]:
            return st
        time.sleep(1.0)
    return st


# ------------------------------------------------------------------- tab bar
# The public API has no notion of tab folders, so placement is browser-only.
# Tabs: `.os-tab-bar-tab[data-id]`; folders add `.os-tab-bar-tab-group`. The bar
# shows one folder at a time; breadcrumbs (first = "All tabs") navigate up.

_TABS_JS = r"""() => [...document.querySelectorAll('.os-tab-bar-tab')].map(t => ({
    id: t.getAttribute('data-id'),
    name: ((t.querySelector('.os-tab-name') || {}).innerText || '').trim(),
    folder: t.classList.contains('os-tab-bar-tab-group'),
}))"""

_BREADCRUMBS_JS = r"""() => [...document.querySelectorAll('.os-tab-bar-breadcrumbs .os-tab-bar-breadcrumb')]
    .map(b => (b.innerText || '').trim())"""


class TabBar:
    """Tab-bar operations on an open Onshape document page."""

    TAB = ".os-tab-bar-tab"
    BREADCRUMB = ".os-tab-bar-breadcrumbs .os-tab-bar-breadcrumb"

    def __init__(self, browser: OnshapeBrowser) -> None:
        self.page = browser.page

    def wait_ready(self, timeout_ms: int = 60000) -> None:
        self.page.wait_for_selector(self.TAB, timeout=timeout_ms)
        self._settle()

    def _settle(self, ms: int = 600) -> None:
        # The bar re-renders asynchronously after navigation and drops.
        time.sleep(ms / 1000)

    def _wait_until(self, js: str, arg: Any = None, timeout_ms: int = 15000) -> None:
        self.page.wait_for_function(js, arg=arg, timeout=timeout_ms)
        self._settle()

    def tabs(self) -> list[dict[str, Any]]:
        return self.page.evaluate(_TABS_JS)

    def current_folder(self) -> str:
        """'' at the root, else the name of the open folder (one level)."""
        crumbs = self.page.evaluate(_BREADCRUMBS_JS)
        return crumbs[-1] if len(crumbs) > 1 else ""

    def go_home(self) -> None:
        if self.current_folder():
            self.page.locator(self.BREADCRUMB).first.click()
            self._wait_until("() => document.querySelectorAll('.os-tab-bar-breadcrumbs .os-tab-bar-breadcrumb').length <= 1")

    def open_folder(self, name: str) -> None:
        self.go_home()
        self.page.locator(f"{self.TAB}.os-tab-bar-tab-group", has_text=name).first.click()
        self._wait_until(
            "(n) => { const c = [...document.querySelectorAll('.os-tab-bar-breadcrumbs .os-tab-bar-breadcrumb')]; return c.length > 1 && c[c.length-1].innerText.trim() === n; }",
            name,
        )

    def read_structure(self) -> dict[str, dict[str, str]]:
        """{folder_name: {element_id: element_name}}; '' is the root (folders excluded)."""
        self.go_home()
        root = self.tabs()
        out: dict[str, dict[str, str]] = {"": {t["id"]: t["name"] for t in root if not t["folder"]}}
        for f in [t["name"] for t in root if t["folder"]]:
            self.open_folder(f)
            out[f] = {t["id"]: t["name"] for t in self.tabs() if not t["folder"]}
        self.go_home()
        return out

    def create_folder(self, name: str) -> None:
        """New folder at the root; Onshape opens an inline rename box on creation."""
        self.go_home()
        self.page.click("#add-element-button")
        self.page.click("#create-group-button")
        box = self.page.locator("input.rename-tab-input")
        box.wait_for(timeout=10000)
        box.fill(name)
        self.page.keyboard.press("Enter")
        self._wait_until(
            "(n) => [...document.querySelectorAll('.os-tab-bar-tab.os-tab-bar-tab-group .os-tab-name')].some(e => e.innerText.trim() === n)",
            name,
        )

    def move_to_parent(self, element_id: str) -> None:
        """Context menu 'Move to parent folder' on a tab in the currently open folder."""
        self.page.locator(f"{self.TAB}[data-id='{element_id}']").first.click(button="right")
        self.page.locator(".context-menu-root .context-menu-item", has_text="Move to parent folder").first.click()
        self._wait_until(
            "(id) => !document.querySelector(`.os-tab-bar-tab[data-id='${id}']`)", element_id
        )

    def drag_into_folder(self, element_id: str, folder: str) -> None:
        """Drag a root-level tab onto a root-level folder tab (no 'move to folder' menu exists)."""
        self.go_home()
        src = self.page.locator(f"{self.TAB}[data-id='{element_id}']").first
        dst = self.page.locator(f"{self.TAB}.os-tab-bar-tab-group", has_text=folder).first
        sb, db = src.bounding_box(), dst.bounding_box()
        if not sb or not db:
            raise RuntimeError(f"Tab {element_id} or folder '{folder}' not visible at root")
        x0, y0 = sb["x"] + sb["width"] / 2, sb["y"] + sb["height"] / 2
        x1, y1 = db["x"] + db["width"] / 2, db["y"] + db["height"] / 2
        self.page.mouse.move(x0, y0)
        self.page.mouse.down()
        steps = 20
        for i in range(1, steps + 1):
            self.page.mouse.move(x0 + (x1 - x0) * i / steps, y0 + (y1 - y0) * i / steps)
            time.sleep(0.03)
        time.sleep(0.3)
        self.page.mouse.up()
        self._wait_until(
            "(id) => !document.querySelector(`.os-tab-bar-tab[data-id='${id}']`)", element_id
        )
