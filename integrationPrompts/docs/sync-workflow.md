# Sync Workflow

> **Note:** This document describes the intended workflow. The sync script is under development.

## Quick Reference

```bash
# Pull latest from Onshape to local
python sync/main.py pull

# Push local changes to Onshape
python sync/main.py push

# Pull specific document only
python sync/main.py pull --document "Core Logic"

# Dry run (see what would change without doing it)
python sync/main.py pull --dry-run
```

## Typical Development Flow

### Starting a New Feature

```bash
# 1. Ensure you have latest from Onshape
python sync/main.py pull

# 2. Create a Git branch
git checkout -b feature/new-footprint-option

# 3. Edit .fs files locally
#    (Use your editor, Claude Code, etc.)

# 4. Push to Onshape to test
python sync/main.py push

# 5. Test in Onshape UI

# 6. Iterate steps 3-5 as needed

# 7. Commit and push to Git
git add .
git commit -m "Add accordion scaling to footprint feature"
git push origin feature/new-footprint-option
```

### Syncing Someone Else's Changes

If another maintainer edited directly in Onshape:

```bash
# Pull will overwrite local files with Onshape versions
python sync/main.py pull

# Review changes
git diff

# Commit the sync
git add .
git commit -m "Sync from Onshape"
```

## Conflict Resolution

The sync script does **not** merge changes. It's a simple "last write wins" model:

- `pull` — Onshape → Local (overwrites local files)
- `push` — Local → Onshape (overwrites Onshape Feature Studios)

**Best practice:** Always `pull` before starting work, and coordinate with other maintainers to avoid simultaneous edits.

## What Gets Synced

| Synced | Not Synced |
|--------|------------|
| FeatureScript source code (`.fs`) | Part Studios |
| Feature Studio names | Assemblies |
| | Drawings |
| | Document metadata |
| | Version history (Git handles this) |

## Configuration

Edit `sync/config.yaml` to configure which documents sync where:

```yaml
documents:
  - name: "Core Logic"
    document_id: "your-document-id-here"
    workspace_id: "main"  # or specific workspace ID
    local_path: "./featurescripts/core"
    
  - name: "Public Features"  
    document_id: "your-other-document-id"
    workspace_id: "main"
    local_path: "./featurescripts/public"

settings:
  # Backup local files before pull overwrites them
  backup_on_pull: true
  backup_dir: "./.sync-backups"
```

### Finding Document/Workspace IDs

From an Onshape URL:
```
https://cad.onshape.com/documents/{document_id}/w/{workspace_id}/e/{element_id}
```

## Troubleshooting

### "Feature Studio not found"
- The Feature Studio may have been renamed or deleted in Onshape
- Run `pull` to refresh local state

### "Push failed: conflict"
- Someone edited in Onshape since your last pull
- Run `pull` first, resolve any issues, then `push` again

### "Authentication failed"
- Check `.env` file has valid credentials
- See [Onshape Auth Setup](./onshape-auth-setup.md)

## Limitations

- **No automatic sync** — You must manually run pull/push
- **No merge** — Overwrites, doesn't merge
- **Single workspace** — Each document config targets one workspace
- **Feature Studios only** — Other element types not supported

## Future Enhancements (Potential)

- [ ] Watch mode for automatic sync on file change
- [ ] Smarter conflict detection
- [ ] Support for multiple workspaces/branches
- [ ] Pre-push syntax validation
