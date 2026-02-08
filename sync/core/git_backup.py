"""Git backup utilities for safely syncing with Onshape."""

import subprocess
from pathlib import Path
from datetime import datetime


class GitBackupError(Exception):
    """Error during Git backup operations."""
    pass


class GitBackup:
    """Manages Git backups before Onshape sync operations."""

    def __init__(self, repo_path: Path):
        """Initialize Git backup manager.

        Args:
            repo_path: Path to the git repository root
        """
        self.repo_path = Path(repo_path)

    def _run_git(self, *args: str, check: bool = True) -> tuple[int, str, str]:
        """Run a git command.

        Args:
            *args: Git command arguments
            check: Whether to raise on non-zero exit

        Returns:
            Tuple of (return_code, stdout, stderr)
        """
        try:
            result = subprocess.run(
                ["git", *args],
                cwd=self.repo_path,
                capture_output=True,
                text=True,
                encoding="utf-8",
            )
            if check and result.returncode != 0:
                raise GitBackupError(
                    f"Git command failed: git {' '.join(args)}\n{result.stderr}"
                )
            return result.returncode, result.stdout, result.stderr
        except FileNotFoundError:
            raise GitBackupError("Git is not installed or not in PATH")
        except Exception as e:
            raise GitBackupError(f"Failed to run git command: {e}")

    def is_git_repo(self) -> bool:
        """Check if the directory is a git repository."""
        returncode, _, _ = self._run_git("rev-parse", "--git-dir", check=False)
        return returncode == 0

    def has_uncommitted_changes(self) -> bool:
        """Check if there are uncommitted changes in the repository.

        Returns:
            True if there are uncommitted changes (staged or unstaged)
        """
        if not self.is_git_repo():
            return False

        # Check for any changes (staged or unstaged)
        returncode, stdout, _ = self._run_git("status", "--porcelain", check=False)
        return bool(stdout.strip())

    def get_status(self) -> str:
        """Get git status output.

        Returns:
            Git status as string
        """
        if not self.is_git_repo():
            return "Not a git repository"

        _, stdout, _ = self._run_git("status", "--short")
        return stdout

    def create_backup_commit(
        self,
        operation: str,
        message: str | None = None,
        auto_add: bool = True,
    ) -> bool:
        """Create a backup commit before an Onshape operation.

        Args:
            operation: Type of operation (e.g., "pull", "push")
            message: Optional custom commit message
            auto_add: Whether to automatically stage all changes

        Returns:
            True if a commit was created, False if no changes to commit
        """
        if not self.is_git_repo():
            raise GitBackupError("Not a git repository")

        if not self.has_uncommitted_changes():
            return False

        # Stage changes if auto_add is enabled
        if auto_add:
            self._run_git("add", "-A")

        # Create commit with timestamp
        timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        if message:
            commit_message = f"[AUTO-BACKUP] {message}\n\nBefore {operation} at {timestamp}"
        else:
            commit_message = f"[AUTO-BACKUP] Before {operation}\n\nAutomatic backup at {timestamp}"

        self._run_git("commit", "-m", commit_message)
        return True

    def push_to_remote(self, remote: str = "origin") -> bool:
        """Push commits to remote repository.

        Args:
            remote: Remote name (default: "origin")

        Returns:
            True if push succeeded, False if nothing to push
        """
        if not self.is_git_repo():
            raise GitBackupError("Not a git repository")

        # Get current branch
        _, branch, _ = self._run_git("rev-parse", "--abbrev-ref", "HEAD")
        branch = branch.strip()

        if not branch or branch == "HEAD":
            raise GitBackupError("Not on a valid branch")

        # Try to push
        returncode, stdout, stderr = self._run_git(
            "push", remote, branch, check=False
        )

        if returncode != 0:
            # Check if it's just "everything up-to-date"
            if "up-to-date" in stderr or "up-to-date" in stdout:
                return False
            raise GitBackupError(f"Failed to push to {remote}/{branch}:\n{stderr}")

        return True

    def backup_before_sync(
        self,
        operation: str,
        auto_commit: bool = True,
        auto_push: bool = False,
        message: str | None = None,
    ) -> dict[str, bool]:
        """Create a complete backup before a sync operation.

        Args:
            operation: Type of operation (e.g., "pull", "push")
            auto_commit: Whether to automatically commit changes
            auto_push: Whether to automatically push to remote
            message: Optional custom commit message

        Returns:
            Dict with keys:
                - had_changes: Whether there were uncommitted changes
                - committed: Whether a commit was created
                - pushed: Whether changes were pushed to remote
        """
        result = {
            "had_changes": False,
            "committed": False,
            "pushed": False,
        }

        if not self.is_git_repo():
            return result

        result["had_changes"] = self.has_uncommitted_changes()

        if not result["had_changes"]:
            return result

        if auto_commit:
            result["committed"] = self.create_backup_commit(operation, message)

        if auto_push and result["committed"]:
            result["pushed"] = self.push_to_remote()

        return result
