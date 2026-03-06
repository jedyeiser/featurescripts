"""Health check system for FeatureScript sync configuration.

Detects configuration drift: ID/URL mismatches, duplicate names, path collisions,
and cross-project conflicts. Runs automatically during sync and on demand.
"""

from dataclasses import dataclass
from datetime import datetime, timezone
from enum import Enum
from pathlib import Path

from rich.console import Console

from ..models.project_config import FeatureScriptSettings, ProjectConfig
from .url_parser import parse_url, OnshapeUrlParseError


console = Console()


class IssueSeverity(str, Enum):
    WARNING = "WARNING"
    ERROR = "ERROR"


@dataclass
class HealthCheckIssue:
    severity: IssueSeverity
    check_type: str  # "id_mismatch" | "path_collision" | "duplicate_name" | "url_conflict"
    subject: str     # project/reference name involved
    message: str


def should_run_health_check(
    last_pull: str | None,
    last_push: str | None,
    threshold_minutes: int = 60,
) -> bool:
    """Return True if a health check should run based on last sync timestamps.

    Runs if both are None (never synced) or if the most recent timestamp is
    older than threshold_minutes.
    """
    if last_pull is None and last_push is None:
        return True

    timestamps = []
    for ts in (last_pull, last_push):
        if ts is None:
            continue
        try:
            dt = datetime.fromisoformat(ts)
            # Normalize naive timestamps (old entries without tz) to UTC
            if dt.tzinfo is None:
                dt = dt.replace(tzinfo=timezone.utc)
            else:
                dt = dt.astimezone(timezone.utc)
            timestamps.append(dt)
        except (ValueError, TypeError):
            continue

    if not timestamps:
        return True

    most_recent = max(timestamps)
    now = datetime.now(timezone.utc)
    elapsed_minutes = (now - most_recent).total_seconds() / 60
    return elapsed_minutes > threshold_minutes


class HealthChecker:
    """Validates FeatureScript sync configuration for consistency."""

    def __init__(
        self,
        settings: FeatureScriptSettings,
        base_dir: Path,
        client=None,
    ) -> None:
        self.settings = settings
        self.base_dir = base_dir
        self.client = client

    def run_global_checks(self) -> list[HealthCheckIssue]:
        """Run all global configuration checks. Returns combined issue list."""
        issues: list[HealthCheckIssue] = []
        issues.extend(self._check_id_url_mismatches())
        issues.extend(self._check_cross_project_conflicts())
        issues.extend(self._check_path_overlaps())
        issues.extend(self._check_name_conflicts())
        return issues

    def run_new_project_checks(self, candidate: ProjectConfig) -> list[HealthCheckIssue]:
        """Check issues specific to a not-yet-added candidate project."""
        issues: list[HealthCheckIssue] = []

        # Name uniqueness (case-insensitive) against projects and references
        candidate_lower = candidate.name.lower()
        for proj in self.settings.projects:
            if proj.name.lower() == candidate_lower:
                issues.append(HealthCheckIssue(
                    severity=IssueSeverity.ERROR,
                    check_type="duplicate_name",
                    subject=candidate.name,
                    message=f"A project named '{proj.name}' already exists.",
                ))
                break
        for ref in self.settings.references:
            if ref.name.lower() == candidate_lower:
                issues.append(HealthCheckIssue(
                    severity=IssueSeverity.ERROR,
                    check_type="duplicate_name",
                    subject=candidate.name,
                    message=f"A reference named '{ref.name}' already exists with this name.",
                ))
                break

        # URL uniqueness: warn if same document_id + workspace_id exists
        if candidate.document_id:
            for proj in self.settings.projects:
                if (proj.document_id == candidate.document_id
                        and candidate.workspace_id is not None
                        and proj.workspace_id == candidate.workspace_id):
                    issues.append(HealthCheckIssue(
                        severity=IssueSeverity.WARNING,
                        check_type="url_conflict",
                        subject=candidate.name,
                        message=(
                            f"Project '{proj.name}' already targets the same Onshape document "
                            f"(doc={candidate.document_id}). This may be intentional."
                        ),
                    ))

        # Local path collision against existing projects and references
        try:
            candidate_path = (self.base_dir / candidate.working_directory).resolve()
        except Exception:
            candidate_path = None

        if candidate_path is not None:
            for proj in self.settings.projects:
                try:
                    existing = (self.base_dir / proj.working_directory).resolve()
                    if candidate_path == existing or candidate_path.is_relative_to(existing) or existing.is_relative_to(candidate_path):
                        issues.append(HealthCheckIssue(
                            severity=IssueSeverity.ERROR,
                            check_type="path_collision",
                            subject=candidate.name,
                            message=(
                                f"Local path '{candidate.working_directory}' overlaps with "
                                f"project '{proj.name}' at '{proj.working_directory}'."
                            ),
                        ))
                except Exception:
                    pass
            for ref in self.settings.references:
                try:
                    existing = (self.base_dir / ref.local_path).resolve()
                    if candidate_path == existing or candidate_path.is_relative_to(existing) or existing.is_relative_to(candidate_path):
                        issues.append(HealthCheckIssue(
                            severity=IssueSeverity.ERROR,
                            check_type="path_collision",
                            subject=candidate.name,
                            message=(
                                f"Local path '{candidate.working_directory}' overlaps with "
                                f"reference '{ref.name}' at '{ref.local_path}'."
                            ),
                        ))
                except Exception:
                    pass

        # Optional: verify document exists via API
        if self.client is not None and candidate.document_id:
            try:
                self.client.get_document_info(candidate.document_id)
            except Exception:
                issues.append(HealthCheckIssue(
                    severity=IssueSeverity.WARNING,
                    check_type="url_conflict",
                    subject=candidate.name,
                    message=(
                        f"Could not reach Onshape document '{candidate.document_id}'. "
                        "Check the URL or your network connection."
                    ),
                ))

        return issues

    def format_issues(self, issues: list[HealthCheckIssue]) -> None:
        """Print issues grouped by severity using Rich."""
        errors = [i for i in issues if i.severity == IssueSeverity.ERROR]
        warnings = [i for i in issues if i.severity == IssueSeverity.WARNING]

        if errors:
            console.print("\n[bold red]Health Check Errors:[/bold red]")
            for issue in errors:
                console.print(f"  [red][{issue.check_type}][/red] {issue.subject}: {issue.message}")

        if warnings:
            console.print("\n[bold yellow]Health Check Warnings:[/bold yellow]")
            for issue in warnings:
                console.print(f"  [yellow][{issue.check_type}][/yellow] {issue.subject}: {issue.message}")

    def has_blocking_issues(self, issues: list[HealthCheckIssue]) -> bool:
        """Return True if any issue has ERROR severity."""
        return any(i.severity == IssueSeverity.ERROR for i in issues)

    # ------------------------------------------------------------------
    # Private checks
    # ------------------------------------------------------------------

    def _check_id_url_mismatches(self) -> list[HealthCheckIssue]:
        """Check that stored document/workspace/folder IDs match the stored URL."""
        issues: list[HealthCheckIssue] = []

        for proj in self.settings.projects:
            try:
                parsed = parse_url(proj.onshape_url)
            except (OnshapeUrlParseError, Exception):
                issues.append(HealthCheckIssue(
                    severity=IssueSeverity.WARNING,
                    check_type="id_mismatch",
                    subject=proj.name,
                    message=f"URL '{proj.onshape_url}' could not be re-parsed for validation.",
                ))
                continue

            if proj.document_id and parsed.get("document_id") and proj.document_id != parsed["document_id"]:
                issues.append(HealthCheckIssue(
                    severity=IssueSeverity.ERROR,
                    check_type="id_mismatch",
                    subject=proj.name,
                    message=(
                        f"Stored document_id '{proj.document_id}' does not match "
                        f"URL-derived '{parsed['document_id']}'."
                    ),
                ))
            if proj.workspace_id and parsed.get("workspace_id") and proj.workspace_id != parsed["workspace_id"]:
                issues.append(HealthCheckIssue(
                    severity=IssueSeverity.ERROR,
                    check_type="id_mismatch",
                    subject=proj.name,
                    message=(
                        f"Stored workspace_id '{proj.workspace_id}' does not match "
                        f"URL-derived '{parsed['workspace_id']}'."
                    ),
                ))
            if proj.folder_id and parsed.get("folder_id") and proj.folder_id != parsed["folder_id"]:
                issues.append(HealthCheckIssue(
                    severity=IssueSeverity.ERROR,
                    check_type="id_mismatch",
                    subject=proj.name,
                    message=(
                        f"Stored folder_id '{proj.folder_id}' does not match "
                        f"URL-derived '{parsed['folder_id']}'."
                    ),
                ))

        return issues

    def _check_cross_project_conflicts(self) -> list[HealthCheckIssue]:
        """Detect URL duplicates and same-document pairs across projects."""
        issues: list[HealthCheckIssue] = []
        projects = self.settings.projects

        for i, a in enumerate(projects):
            for b in projects[i + 1:]:
                # Exact URL duplicate
                if a.onshape_url == b.onshape_url:
                    issues.append(HealthCheckIssue(
                        severity=IssueSeverity.ERROR,
                        check_type="url_conflict",
                        subject=f"{a.name} / {b.name}",
                        message=(
                            f"Projects '{a.name}' and '{b.name}' have identical Onshape URLs."
                        ),
                    ))
                # Same document + workspace (may be intentional sub-selections)
                elif (a.document_id and a.document_id == b.document_id
                        and a.workspace_id is not None
                        and a.workspace_id == b.workspace_id):
                    issues.append(HealthCheckIssue(
                        severity=IssueSeverity.WARNING,
                        check_type="url_conflict",
                        subject=f"{a.name} / {b.name}",
                        message=(
                            f"Projects '{a.name}' and '{b.name}' share the same "
                            f"document+workspace. This may be intentional."
                        ),
                    ))

                # Same resolved working_directory
                try:
                    path_a = (self.base_dir / a.working_directory).resolve()
                    path_b = (self.base_dir / b.working_directory).resolve()
                    if path_a == path_b:
                        issues.append(HealthCheckIssue(
                            severity=IssueSeverity.ERROR,
                            check_type="path_collision",
                            subject=f"{a.name} / {b.name}",
                            message=(
                                f"Projects '{a.name}' and '{b.name}' share the same "
                                f"local working directory: '{a.working_directory}'."
                            ),
                        ))
                except Exception:
                    pass

        return issues

    def _check_path_overlaps(self) -> list[HealthCheckIssue]:
        """Check that no project working_directory overlaps a reference local_path."""
        issues: list[HealthCheckIssue] = []

        for proj in self.settings.projects:
            try:
                proj_path = (self.base_dir / proj.working_directory).resolve()
            except Exception:
                continue

            for ref in self.settings.references:
                try:
                    ref_path = (self.base_dir / ref.local_path).resolve()
                    if proj_path == ref_path or proj_path.is_relative_to(ref_path) or ref_path.is_relative_to(proj_path):
                        issues.append(HealthCheckIssue(
                            severity=IssueSeverity.ERROR,
                            check_type="path_collision",
                            subject=f"{proj.name} / {ref.name}",
                            message=(
                                f"Project '{proj.name}' path '{proj.working_directory}' "
                                f"overlaps with reference '{ref.name}' path '{ref.local_path}'."
                            ),
                        ))
                except Exception:
                    pass

        return issues

    def _check_name_conflicts(self) -> list[HealthCheckIssue]:
        """Detect case-insensitive duplicate names within projects and within references."""
        issues: list[HealthCheckIssue] = []

        seen: dict[str, str] = {}
        for proj in self.settings.projects:
            key = proj.name.lower()
            if key in seen:
                issues.append(HealthCheckIssue(
                    severity=IssueSeverity.ERROR,
                    check_type="duplicate_name",
                    subject=proj.name,
                    message=f"Duplicate project name (case-insensitive): '{seen[key]}' and '{proj.name}'.",
                ))
            else:
                seen[key] = proj.name

        seen = {}
        for ref in self.settings.references:
            key = ref.name.lower()
            if key in seen:
                issues.append(HealthCheckIssue(
                    severity=IssueSeverity.ERROR,
                    check_type="duplicate_name",
                    subject=ref.name,
                    message=f"Duplicate reference name (case-insensitive): '{seen[key]}' and '{ref.name}'.",
                ))
            else:
                seen[key] = ref.name

        return issues
