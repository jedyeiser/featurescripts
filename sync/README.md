# FeatureScript Sync

Bidirectional sync between this Git repo and Onshape Feature Studios.

## Setup

```bash
# Create virtual environment (one time)
python3 -m venv venv

# Activate (every terminal session)
source venv/bin/activate

# Install dependencies (one time)
pip install -r requirements.txt

# Create .env with your API keys (one time, not committed)
cp .env.example .env
# Edit .env with your credentials from https://k2-sports.onshape.com/appstore/dev-portal/keys
```

## Common Commands

```bash
# Always activate first
source venv/bin/activate

# Connection & Status
python -m sync.main verify-auth      # Test API connection
python -m sync.main tree             # Show Onshape folder structure
python -m sync.main status           # Show local sync status

# Sync Operations (use --dry-run first!)
python -m sync.main pull --dry-run   # Preview what pull would do
python -m sync.main pull             # Pull from Onshape → local
python -m sync.main pull --force     # Pull, overwriting local changes

python -m sync.main push --dry-run   # Preview what push would do
python -m sync.main push             # Push local → Onshape
python -m sync.main push --force     # Push, overwriting remote changes

# Std Library Cache
python -m sync.main cache status     # Show std library status
```

## Configuration

Edit `sync/config.yaml` to configure which Onshape folders to sync:

```yaml
folders:
  - name: "EOC Featurescripts"
    folder_id: "fe3ff54b1d12a3a8491215c6"  # From Onshape URL
    local_path: "./onshape"
    recursive: true
    exclude:
      - "_archive/*"
      - "_test*"
```

## How It Works

- **Pull**: Downloads Feature Studios from Onshape → creates local folders with `.fs` files
- **Push**: Uploads local `.fs` files → Onshape Feature Studios
- **Conflict Detection**: Tracks local hashes and remote microversions to detect conflicts
- **Folder Structure**: Each Onshape document becomes a local folder with a `.document.json` metadata file

## File Structure

```
featurescripts/
├── .env                 # API credentials (gitignored)
├── sync/                # Sync system
│   ├── config.yaml      # Folder mappings
│   └── main.py          # CLI entry point
├── std/                 # Onshape std library (local reference)
├── onshape/             # Synced content (created on pull)
├── footprint/           # Local FeatureScript projects
├── gordonSurface/
└── tools/
```

## Safety

- Always use `--dry-run` first to preview changes
- Pull creates backups in `.sync-backups/` before overwriting
- Push warns if remote has changed since last sync
- Use `--force` only when you're sure you want to overwrite
