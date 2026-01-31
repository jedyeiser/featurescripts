# FeatureScript Sync: Project Overview

## Purpose

This project enables bidirectional syncing of FeatureScript code between:
- **Onshape** — Where Feature Studios live and execute
- **Local Git repository** — For version control, code review, and AI-assisted development

## Why This Exists

FeatureScripts are stored inside Onshape documents, not as local files. This creates friction for:
- Using Git for version history and branching
- Collaborating via pull requests
- Working with tools like Claude Code that operate on local files
- Backup and disaster recovery

## Architecture

### Two-Document Pattern (Code Protection)

```
┌─────────────────────────────────────────────────────────────────┐
│                         ONSHAPE                                 │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  📁 Protected Document (restricted to maintainers)              │
│     └── Feature Studios with core logic                         │
│         ├── Internal calculations                               │
│         ├── Complex geometry operations                         │
│         └── Business logic                                      │
│              ▲                                                  │
│              │ import                                           │
│              │                                                  │
│  📁 Exposed Document (shared org-wide, view-only)               │
│     └── Feature Studios with user-facing features               │
│         ├── Thin wrappers around core logic                     │
│         ├── UI definitions (feature parameters)                 │
│         └── User-visible documentation                          │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
                              ▲
                              │ Onshape REST API
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      LOCAL REPOSITORY                           │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  /featurescripts                                                │
│     ├── /core          ← syncs with Protected Document          │
│     │   ├── laminate-math.fs                                    │
│     │   └── geometry-utils.fs                                   │
│     │                                                           │
│     └── /public        ← syncs with Exposed Document            │
│         ├── ski-footprint.fs                                    │
│         └── surface-tools.fs                                    │
│                                                                 │
│  /sync                 ← Python sync tooling                    │
│  /docs                 ← This documentation                     │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Why Two Documents?

Onshape permissions are document-level. By splitting code:
- **Core logic** stays hidden from general users
- **User-facing features** are accessible org-wide
- Users see *what* functions are called, but not *how* they're implemented
- Updates to core logic propagate when exposed features are re-versioned

## Key Concepts

### Feature Studio
A single "file" in Onshape containing FeatureScript code. Lives inside an Onshape document.

### Document vs Folder
Onshape documents are the permission boundary. Folders are organizational within your account but don't directly affect sharing (though you can apply permissions at folder level that cascade to documents).

### Version References
When one Feature Studio imports another, it references a specific *version* of the source document. This is important for stability but means you need to update version references when core logic changes.

### Workspaces
Onshape documents have "workspaces" (like branches). The main workspace is typically where active development happens. The sync script targets a configurable workspace.

## Sync Workflow (High Level)

1. **Pull**: Fetch FeatureScript source from Onshape API → write to local `.fs` files
2. **Edit**: Work on code locally with full Git/IDE/AI tooling
3. **Push**: Send updated source back to Onshape via API
4. **Commit**: Track changes in Git as normal

## Configuration

The sync script needs to know which Onshape documents map to which local folders. This is configured in `sync/config.yaml` (or similar):

```yaml
documents:
  - name: "Core Logic"
    document_id: "abc123..."
    local_path: "./featurescripts/core"
    
  - name: "Public Features"
    document_id: "def456..."
    local_path: "./featurescripts/public"
```

Document IDs are found in Onshape URLs:
```
https://cad.onshape.com/documents/{document_id}/...
```

## Related Documentation

- [Onshape Auth Setup](./onshape-auth-setup.md) — API credential configuration
- [Sync Workflow](./sync-workflow.md) — Day-to-day usage of the sync script
- [Onshape API Docs](https://onshape-public.github.io/docs/) — Official REST API reference
- [FeatureScript Reference](https://cad.onshape.com/FsDoc/) — Language documentation
