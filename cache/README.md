# Standard Library Cache

This directory contains cached copies of Onshape's standard library FeatureScript files.

## Purpose

Onshape/PTC has API rate limits. To minimize API calls, we cache standard library
files locally and only fetch when:
1. A document isn't available locally (first-time fetch)
2. An explicit update is requested via `cache update` command

## Structure

```
cache/
├── std/                    # Cached Onshape standard library
│   ├── manifest.json       # Index of cached docs + versions
│   └── *.fs                # Cached FeatureScript files
└── README.md               # This file
```

## Manifest

The `manifest.json` file tracks:
- Document IDs and element IDs for each cached file
- Microversions to detect when remote has changed
- Fetch timestamps for auditing

## Usage

```bash
# Check cache status
python sync/main.py cache status

# Update a specific file
python sync/main.py cache update geometry.fs

# Update all cached files
python sync/main.py cache update --all
```

## Committing Cache Files

Cached files ARE committed to Git so that:
- All developers share the same cached versions
- One developer fetches, everyone benefits
- Minimizes team-wide API calls

When updating cache files in a PR, note which documents changed and why.
