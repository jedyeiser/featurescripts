# FeatureScript Sync

Sync FeatureScript code between Onshape and a local Git repository.

## Quick Start

1. **Set up Onshape API credentials** — See [docs/onshape-auth-setup.md](docs/onshape-auth-setup.md)

2. **Create your `.env` file:**
   ```bash
   cp .env.example .env
   # Edit .env with your credentials
   ```

3. **Set up Python environment:**
   ```bash
   python -m venv venv
   source venv/bin/activate  # or `venv\Scripts\activate` on Windows
   pip install -r requirements.txt
   ```

4. **Configure documents to sync** — Edit `sync/config.yaml`

5. **Pull your FeatureScripts:**
   ```bash
   python sync/main.py pull
   ```

## Documentation

- [Project Overview](docs/project-overview.md) — Architecture and design decisions
- [Onshape Auth Setup](docs/onshape-auth-setup.md) — API credential configuration
- [Sync Workflow](docs/sync-workflow.md) — Day-to-day usage

## Project Structure

```
.
├── docs/                  # Documentation
├── featurescripts/        # Synced FeatureScript source files
│   ├── core/              # Protected/internal code
│   └── public/            # User-facing features
├── sync/                  # Python sync tooling
├── .env.example           # Template for credentials
└── README.md
```

## Status

🚧 **Under Development** — Sync script is being built.
