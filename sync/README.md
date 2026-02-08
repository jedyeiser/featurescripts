# FeatureScript Sync System

Bidirectional sync tool for Onshape FeatureScript development with support for **reference libraries** (read-only) and **working projects** (bidirectional).

## Overview

This sync system enables fluid FeatureScript development with Onshape by distinguishing between:

- **Reference Libraries**: Read-only code from Onshape (e.g., standard library, corporate standards)
- **Working Projects**: Active development with bidirectional sync (pull and push)

## Features

- ✅ **Smart Change Detection**: Only downloads changed files (microversion + hash tracking)
- ✅ **Conflict Detection**: Prevents data loss from concurrent modifications
- ✅ **Read-Only Enforcement**: Blocks accidental pushes to reference libraries
- ✅ **Selective Push**: Push specific files or entire projects
- ✅ **Project Configuration**: JSON-based configuration (`featurescriptSettings.json`)
- ✅ **API Inspector**: Debug tool for exploring Onshape API responses
- ✅ **Dry-Run Mode**: Preview operations before executing

## Quick Start

### 1. Authentication Setup

Create a `.env` file in the project root:

```env
ONSHAPE_ACCESS_KEY=your_access_key_here
ONSHAPE_SECRET_KEY=your_secret_key_here
ONSHAPE_BASE_URL=https://k2-sports.onshape.com  # or https://cad.onshape.com
```

### 2. Verify Authentication

```bash
python -m sync.main verify-auth
```

### 3. Add a Reference Library

```bash
# Add Onshape standard library as a reference
python -m sync.main reference add \
  "https://cad.onshape.com/documents/folder/FOLDER_ID" \
  "Onshape Standard Library" \
  --path ./references/std \
  --auto-update

# List references
python -m sync.main reference list
```

### 4. Add a Working Project

```bash
# Add a project for active development
python -m sync.main project add \
  "https://k2-sports.onshape.com/documents/d/DOC_ID/w/WS_ID" \
  "Gordon Surface" \
  --description "Gordon surface interpolation feature" \
  --path ./projects/gordon-surface

# List projects
python -m sync.main project list
```

### 5. Pull and Push

```bash
# Pull project from Onshape
python -m sync.main get "Gordon Surface"

# Make changes to .fs files locally...

# Check status
python -m sync.main project status "Gordon Surface"

# Push changes back to Onshape
python -m sync.main pushproject "Gordon Surface"

# Push specific files only
python -m sync.main pushproject "Gordon Surface" --files gordonSurface.fs
```

## Command Reference

### Authentication

```bash
# Verify API credentials
python -m sync.main verify-auth
```

### Reference Management (Read-Only)

```bash
# Add a reference library
python -m sync.main reference add <url> <name> [OPTIONS]
  --path PATH              Local path (default: ./references/{name})
  --auto-update            Enable automatic updates
  --no-recursive           Don't sync subfolders

# List all references
python -m sync.main reference list

# Update references
python -m sync.main reference update [name] [OPTIONS]
  --force                  Force update even if auto_update=false
  --check                  Only check for updates, don't download

# Remove a reference
python -m sync.main reference remove <name> [OPTIONS]
  --delete-files           Also delete local files
```

### Project Management (Bidirectional)

```bash
# Add a working project
python -m sync.main project add <url> <name> [OPTIONS]
  --description DESC       Project description
  --path PATH              Local path (default: ./projects/{name})
  --references REFS        Comma-separated list of reference names

# List all projects
python -m sync.main project list

# Show project status
python -m sync.main project status <name>

# Remove a project
python -m sync.main project remove <name> [OPTIONS]
  --delete-files           Also delete local files
```

### Pull and Push

```bash
# Pull project from Onshape
python -m sync.main get <project_name> [OPTIONS]
  --force                  Overwrite local changes
  --dry-run                Show what would happen

# Push project to Onshape
python -m sync.main pushproject <project_name> [OPTIONS]
  --files FILE1 FILE2...   Specific files to push
  --force                  Overwrite remote changes
  --dry-run                Show what would happen
```

### Legacy Commands (Backward Compatible)

```bash
# Pull all configured folders/documents (old style)
python -m sync.main pull [--dry-run] [--force]

# Push all configured folders/documents (old style)
python -m sync.main push [--dry-run] [--force]

# Show sync status (old style)
python -m sync.main status
```

### Debug Tools

```bash
# Inspect API endpoint
python -m sync.main inspect endpoint <endpoint> [OPTIONS]
  --method METHOD          HTTP method (default: GET)
  --params JSON            JSON parameters
  --save PATH              Save response to file

# Inspect document
python -m sync.main inspect document <doc_id> [--ws-id <ws_id>]

# Inspect folder
python -m sync.main inspect folder <folder_id>

# Inspect element
python -m sync.main inspect element <doc_id> <ws_id> <elem_id>

# Compare saved responses
python -m sync.main inspect compare <file1> <file2>
```

### Migration

```bash
# Migrate from config.yaml to featurescriptSettings.json
python -m sync.main migrate [--force]
```

## Configuration

### featurescriptSettings.json

Primary configuration file (auto-generated):

```json
{
  "version": "1.0",
  "onshape": {
    "base_url": "https://k2-sports.onshape.com",
    "api_version": "v10"
  },
  "references": [
    {
      "name": "Onshape Standard Library",
      "type": "folder",
      "url": "https://cad.onshape.com/documents/folder/...",
      "local_path": "./references/std",
      "read_only": true,
      "auto_update": false,
      "recursive": true,
      "last_sync": "2026-02-07T19:30:00Z",
      "folder_id": "..."
    }
  ],
  "projects": [
    {
      "name": "Gordon Surface",
      "description": "Gordon surface interpolation feature",
      "working_directory": "./projects/gordon-surface",
      "onshape_url": "https://k2-sports.onshape.com/documents/...",
      "references": ["Onshape Standard Library"],
      "last_pull": "2026-02-07T18:00:00Z",
      "last_push": "2026-02-07T18:30:00Z",
      "document_id": "...",
      "workspace_id": "main"
    }
  ],
  "sync_metadata": {
    "document_cache": {}
  }
}
```

### config.yaml (Legacy)

Still supported for backward compatibility. Use `python -m sync.main migrate` to convert to new format.

## Workflow Examples

### Example 1: Setting Up a New Environment

```bash
# 1. Add standard library as reference
python -m sync.main reference add \
  "https://cad.onshape.com/documents/folder/STD_FOLDER_ID" \
  "Onshape Std" \
  --auto-update

# 2. Add your working project
python -m sync.main project add \
  "https://k2-sports.onshape.com/documents/d/MY_DOC/w/main" \
  "My Feature" \
  --references "Onshape Std"

# 3. Start working
code ./projects/my-feature/*.fs
```

### Example 2: Daily Development

```bash
# Morning: Pull latest changes
python -m sync.main get "My Feature"

# Work on code...
code ./projects/my-feature/myFeature.fs

# Check what changed
python -m sync.main project status "My Feature"

# Push your changes
python -m sync.main pushproject "My Feature"
```

### Example 3: Debugging API Issues

```bash
# Inspect your session info
python -m sync.main inspect endpoint /api/v10/users/sessioninfo \
  --save debug/session.json

# Inspect a specific document
python -m sync.main inspect document abc123def456 --ws-id main \
  --save debug/doc.json

# Compare two document states
python -m sync.main inspect compare debug/doc_before.json debug/doc_after.json
```

## Architecture

### Directory Structure

```
featurescripts/
├── featurescriptSettings.json    # Project configuration
├── .env                          # API credentials (gitignored)
├── .sync-state.json              # Sync state tracking
│
├── references/                    # Read-only references
│   └── std/                      # Onshape standard library
│       ├── .document.json        # Metadata
│       └── *.fs                  # FeatureScript files
│
├── projects/                      # Working projects
│   └── gordon-surface/           # Example project
│       ├── .document.json
│       └── *.fs
│
└── sync/                         # Sync tool package
    ├── core/                     # Core functionality
    │   ├── auth.py               # HMAC-SHA256 authentication
    │   ├── client.py             # Onshape API client
    │   ├── operations.py         # Pull/push operations
    │   ├── state.py              # State tracking
    │   ├── references.py         # Reference management
    │   ├── working.py            # Working directory management
    │   ├── url_parser.py         # URL parsing
    │   └── inspector.py          # Debug tools
    ├── models/                   # Data models
    │   ├── config.py             # Legacy config (YAML)
    │   └── project_config.py     # New config (JSON)
    └── main.py                   # CLI entry point
```

### Change Detection Strategy

**For References (Read-Only):**
1. Check `last_sync` timestamp
2. Query remote microversion
3. Compare with cached microversion
4. Download only if changed or `--force`

**For Working Directories (Pull):**
1. Compare local file hash vs tracked hash
2. Compare remote microversion vs cached
3. If only remote changed → safe pull
4. If only local changed → warn (pull will overwrite)
5. If both changed → conflict (require `--force`)

**For Working Directories (Push):**
1. Compare local file hash vs tracked hash
2. Check remote microversion hasn't changed
3. If remote changed → conflict (pull first)
4. If local unchanged → skip
5. If local changed + remote unchanged → safe push

## Data Protection

The sync system includes multiple layers of protection:

1. **Dry-Run Mode**: Preview all operations before execution
2. **Conflict Detection**: Block operations if concurrent changes detected
3. **Read-Only Enforcement**: Hard block on pushing reference directories
4. **Hash Validation**: Verify file integrity after download
5. **Microversion Tracking**: Detect remote modifications
6. **Force Flag Requirement**: Require explicit `--force` for dangerous operations

## Troubleshooting

### Authentication Errors

```bash
# Verify credentials are set
python -m sync.main verify-auth

# Check .env file exists and has correct keys
cat .env
```

### Conflicts

```bash
# If pull conflicts with local changes:
python -m sync.main get "MyProject" --force  # Overwrite local

# If push conflicts with remote changes:
python -m sync.main get "MyProject"          # Pull first
python -m sync.main pushproject "MyProject"  # Then push
```

### Debugging

```bash
# Inspect API responses
python -m sync.main inspect endpoint /api/v10/documents/d/DOC_ID \
  --save debug/response.json

# Check project status
python -m sync.main project status "MyProject"

# Use dry-run mode
python -m sync.main get "MyProject" --dry-run
python -m sync.main pushproject "MyProject" --dry-run
```

## API Documentation

### Onshape REST API

The sync system uses Onshape's REST API v10:
- Authentication: HMAC-SHA256 with API keys
- Base URL: `https://{instance}.onshape.com/api/v10`
- Documentation: https://cad.onshape.com/glassworks/explorer

### Key Endpoints Used

- `/users/sessioninfo` - Verify authentication
- `/documents/d/{did}/w/{wid}/elements` - List Feature Studios
- `/featurestudios/d/{did}/w/{wid}/e/{eid}/featurestudiocontents` - Get/update code
- `/documents/d/{did}/w/{wid}` - Get microversion
- `/globaltreenodes/folder/{fid}` - List folder contents

## License

Part of the FeatureScript development project.
