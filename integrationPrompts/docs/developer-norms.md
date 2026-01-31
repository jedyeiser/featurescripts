# Developer Norms for FeatureScript Sync

This document outlines conventions and best practices for working with the FeatureScript sync system.

## File Naming

- Use `snake_case.fs` for all FeatureScript files (matches `tools/` convention)
- Python files use `snake_case.py`
- Keep names concise but descriptive

## Commit Convention

Follow the sync-specific commit format:

```
sync(<scope>): <description>

Examples:
sync(cache): Add std library manifest structure
sync(pull): Implement lazy fetch for missing imports
sync(ci): Add GitHub Actions validation workflow
sync(auth): Fix HMAC signature for query params
```

### Scopes

| Scope | Description |
|-------|-------------|
| `cache` | Cache management changes |
| `pull` | Pull operation changes |
| `push` | Push operation changes |
| `auth` | Authentication changes |
| `config` | Configuration changes |
| `ci` | CI/CD workflow changes |
| `docs` | Documentation changes |

## Branch Strategy

```
master
├── sync/main              # Main sync system development
│   └── sync/feature-x     # Feature branches
├── tools/...              # Other developers' domains
└── footprint/...
```

- Create feature branches from `sync/main` for sync-related work
- Use descriptive branch names: `sync/add-conflict-resolution`
- Keep PRs focused on single concerns

## Cache Update Policy

### Cached Files ARE Committed to Git

The standard library cache (`integrationPrompts/cache/std/`) is committed to the repository so that:
- All developers share the same cached versions
- One developer fetches → everyone benefits
- Minimizes team-wide API calls against Onshape rate limits

### Update Process

1. Updates are **explicit** via CLI command:
   ```bash
   python sync/main.py cache update <file>
   # or
   python sync/main.py cache update --all
   ```

2. PRs that update cache should note:
   - Which documents changed
   - Why the update was needed
   - Any breaking changes in the std library

3. Review cache update PRs carefully - they affect everyone

## Environment Setup

### Local Development

1. Create virtual environment:
   ```bash
   cd integrationPrompts
   python -m venv venv
   source venv/bin/activate  # macOS/Linux
   # or: venv\Scripts\activate  # Windows
   ```

2. Install dependencies:
   ```bash
   pip install -r requirements.txt
   pip install -e ".[dev]"  # Install dev dependencies
   ```

3. Configure credentials:
   ```bash
   cp .env.example .env
   # Edit .env with your Onshape API keys
   ```

4. Verify setup:
   ```bash
   python sync/verify_auth.py
   ```

### Getting API Keys

1. Go to https://dev-portal.onshape.com/keys
2. Create new API keys with appropriate permissions
3. Store securely - never commit to git

## Testing

### Running Tests

```bash
cd integrationPrompts
pytest tests/ -v
```

### Before Submitting PRs

1. Run linting: `ruff check sync/`
2. Run type check: `mypy sync/`
3. Run tests: `pytest tests/`
4. Test your changes manually with `--dry-run`

## Conflict Resolution

### Pull Conflicts

When both local and remote have changed:
1. Review the conflict message
2. Backup your local changes manually if needed
3. Use `--force` to accept remote version, OR
4. Push your local version first (if appropriate)

### Push Conflicts

When remote has changed since last sync:
1. Pull first to get latest remote version
2. Merge changes manually
3. Push the merged result

## Security

### Never Commit

- `.env` files with real credentials
- API keys or secrets
- `.sync-state.json` (contains document IDs)

### API Key Best Practices

- Use dedicated API keys for sync (not personal keys)
- Grant minimum necessary permissions
- Rotate keys periodically
- Revoke compromised keys immediately

## Troubleshooting

### Authentication Errors

```
[FAIL] API error: 401 Unauthorized
```
- Check API keys are correct
- Verify keys haven't expired
- Ensure `.env` file is in correct location

### Rate Limiting

```
[FAIL] API error: 429 Too Many Requests
```
- Wait and retry
- Use cache more aggressively
- Batch operations where possible

### Sync State Corruption

If `.sync-state.json` becomes corrupted:
1. Delete the file
2. Pull with `--force` to re-establish state
3. Future syncs will work normally
