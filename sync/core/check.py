"""Post-push verification: read Onshape's FeatureScript notices for a project.

The REST API says nothing about whether a pushed Feature Studio compiles, so
this drives a browser to the document and scrapes the notices pane. With a
monitored Part Studio it also regenerates that studio and collects runtime
notices (println, stack traces) and per-feature status.
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from rich.console import Console
from rich.table import Table

from .browser import OnshapeBrowser, monitor_part_studio
from .client import OnshapeClient
from ..models.project_config import ProjectConfig

console = Console()

SEVERITY_ORDER = {"error": 0, "warning": 1, "info": 2, "unknown": 3}
SEVERITY_STYLE = {"error": "red", "warning": "yellow", "info": "dim", "unknown": "magenta"}
FEATURE_STYLE = {"OK": "green", "INFO": "cyan", "WARNING": "yellow", "ERROR": "red"}


def element_names_for_files(files: list[str] | None) -> set[str] | None:
    """Local .fs paths -> Onshape tab names (basename without extension)."""
    if not files:
        return None
    return {Path(f).stem for f in files}


def _empty_report() -> dict[str, Any]:
    return {"notices": [], "console": "", "features": [], "regen": None}


def check_project(
    proj: ProjectConfig,
    client: OnshapeClient,
    files: list[str] | None = None,
    headless: bool = True,
    settle_ms: int = 15000,
    monitor: str | None = None,
    regen_timeout_s: int = 120,
) -> dict[str, Any]:
    """Open the project's document in a browser and return its notices.

    Args:
        proj: project to check (must have document_id / workspace_id)
        files: restrict to these local files' tabs; None = all Feature Studios
        headless: run Chromium without a window
        monitor: name of a Part Studio to regenerate and collect runtime
            notices from, plus per-feature status via REST afterwards

    Returns {"notices": [...], "console": str,
             "features": [{name, type, status}], "regen": "Result: ..." | None}
    """
    if not proj.document_id or not proj.workspace_id:
        raise ValueError(f"Project '{proj.name}' has no document/workspace id")

    ws_id = proj.workspace_id
    elements = client.list_elements(proj.document_id, ws_id)
    studios = [e for e in elements if e.get("elementType") == "FEATURESTUDIO"]
    if not studios:
        raise ValueError(f"No Feature Studios in document for project '{proj.name}'")

    wanted = element_names_for_files(files)
    open_target = studios[0]
    if wanted:
        known = {e["name"] for e in studios}
        missing = wanted - known
        if missing:
            console.print(f"[yellow]Not in Onshape document (skipped): {', '.join(sorted(missing))}[/yellow]")
        wanted &= known
        if not wanted and not monitor:
            return _empty_report()
        open_target = next((e for e in studios if e["name"] in wanted), studios[0])

    ps = None
    if monitor:
        ps = next((e for e in elements if e.get("elementType") == "PARTSTUDIO" and e["name"] == monitor), None)
        if ps is None:
            raise ValueError(f"No Part Studio named '{monitor}' in document for project '{proj.name}'")

    report = _empty_report()
    with OnshapeBrowser(headless=headless) as b:
        b.open_element(proj.document_id, ws_id, open_target["id"])
        b.wait_for_editor()
        report["notices"] = b.read_notices(elements=wanted, settle_ms=settle_ms)
        if ps is not None:
            console.print(f"[blue]Monitoring Part Studio '{monitor}' (regenerating)...[/blue]")
            st = monitor_part_studio(b, monitor, timeout_s=regen_timeout_s)
            report["console"] = st["console"]
            results = [ln.strip() for ln in st["console"].splitlines() if ln.strip().startswith("Result:")]
            report["regen"] = results[-1] if results else None
            report["notices"] += b.read_notices(elements={monitor}, settle_ms=settle_ms)

    if ps is not None:
        # Monitoring already regenerated it, so REST now reflects per-feature status.
        r = client.get(
            f"/api/{client.API_VERSION}/partstudios/d/{proj.document_id}/w/{ws_id}/e/{ps['id']}/features"
        )
        states = r.get("featureStates", {})
        for f in r.get("features", []):
            status = states.get(f.get("featureId"), {}).get("featureStatus", "?")
            report["features"].append({"name": f.get("name"), "type": f.get("featureType"), "status": status})
    return report


def _trace_str(n: dict[str, Any]) -> str:
    parts = []
    for t in n["trace"]:
        el = t["element"].replace("\u00a0", " ")
        parts.append(f"{el}:{t['line']}:{t['column']}" if t["line"] is not None else el)
    return " <- ".join(parts)


def print_report(report: dict[str, Any], errors_only: bool = False, as_json: bool = False) -> None:
    notices = report["notices"]
    shown = [n for n in notices if not errors_only or n["severity"] == "error"]
    shown.sort(key=lambda n: (SEVERITY_ORDER.get(n["severity"], 9), n["element"], n["line"] or 0))

    if as_json:
        print(json.dumps({**report, "notices": shown}, indent=1))
        return

    if not shown:
        console.print("[green]No notices.[/green]")
    else:
        table = Table(show_header=True, header_style="bold")
        table.add_column("Sev")
        table.add_column("Location")
        table.add_column("Message")
        for n in shown:
            loc = n["element"]
            if n["line"] is not None:
                loc += f":{n['line']}:{n['column']}"
            style = SEVERITY_STYLE.get(n["severity"], "")
            msg = n["message"]
            # Runtime notices carry a stack trace through other modules; show it.
            if len(n["trace"]) > 1:
                msg += f"\n[dim]{_trace_str(n)}[/dim]"
            table.add_row(f"[{style}]{n['severity']}[/{style}]", loc, msg)
        console.print(table)

    counts: dict[str, int] = {}
    for n in notices:
        counts[n["severity"]] = counts.get(n["severity"], 0) + 1
    summary = ", ".join(
        f"{v} {k}{'s' if v != 1 else ''}"
        for k, v in sorted(counts.items(), key=lambda kv: SEVERITY_ORDER.get(kv[0], 9))
    ) or "none"
    console.print(f"[bold]Notices:[/bold] {summary}")

    if report["features"]:
        bad = [f for f in report["features"] if f["status"] != "OK"]
        console.print(f"[bold]Features:[/bold] {len(report['features'])} total, {len(bad)} not OK")
        for f in bad:
            style = FEATURE_STYLE.get(f["status"], "")
            console.print(f"  [{style}]{f['status']:7}[/{style}] {f['name']} ({f['type']})")
    if report["console"]:
        console.print("[bold]Console:[/bold]")
        for ln in report["console"].splitlines():
            if ln.strip():
                console.print(f"  {ln}")


def report_exit_code(report: dict[str, Any], strict: bool = False) -> int:
    """Non-zero on any error notice, any ERROR feature, or (strict) any warning."""
    bad = {"error", "warning"} if strict else {"error"}
    if any(n["severity"] in bad for n in report["notices"]):
        return 1
    if any(f["status"] == "ERROR" for f in report["features"]):
        return 1
    if report["regen"] and "Error" in report["regen"]:
        return 1
    return 0
