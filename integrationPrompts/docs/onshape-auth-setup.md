# Onshape API Authentication Setup

This guide walks through setting up API credentials for syncing FeatureScripts between Onshape and this local repository.

## Overview

Onshape offers two authentication methods:
1. **API Keys** — Simpler, good for personal/internal tools
2. **OAuth** — More complex, required for apps used by others

For this sync workflow, **API Keys** are the right choice.

## Step 1: Generate API Keys

1. Log into Onshape
2. Click your user icon (top right) → **My account**
3. Go to the **API keys** tab (left sidebar)
4. Click **Create new API key**
5. Give it a descriptive name (e.g., "FeatureScript Sync - Local Dev")
6. You'll receive:
   - **Access Key** — A public identifier (like a username)
   - **Secret Key** — Shown only once! Copy it immediately.

> ⚠️ **Important:** The secret key is displayed only at creation time. If you lose it, you'll need to delete the key and create a new one.

## Step 2: Configure Local Environment

Create a `.env` file in the project root (this file is gitignored):

```bash
# Onshape API Credentials
ONSHAPE_ACCESS_KEY=your_access_key_here
ONSHAPE_SECRET_KEY=your_secret_key_here

# Onshape Base URL (use your enterprise URL if applicable)
ONSHAPE_BASE_URL=https://cad.onshape.com
```

### Enterprise Users

If your company uses a custom Onshape domain (e.g., `https://yourcompany.onshape.com`), update `ONSHAPE_BASE_URL` accordingly.

## Step 3: Verify Setup

Once the sync script is built, you can verify authentication with:

```bash
python sync/verify_auth.py
```

This will attempt a simple API call (like listing your documents) to confirm credentials work.

## Security Notes

- **Never commit `.env` files** — The `.gitignore` should already exclude it
- **API keys have your full permissions** — Anyone with your keys can do anything you can do in Onshape
- **Rotate keys periodically** — Delete old keys in the Onshape portal if compromised or unused
- **Enterprise admins can see API key usage** — This is normal and expected

## Troubleshooting

### "401 Unauthorized" errors
- Double-check access/secret keys for typos
- Ensure no extra whitespace in `.env` values
- Verify the key hasn't been deleted in Onshape portal

### "403 Forbidden" errors
- Your API key works, but you lack permission for that document/action
- Check document sharing settings

### Connection errors
- Verify `ONSHAPE_BASE_URL` is correct
- Check network/firewall isn't blocking Onshape

## API Key vs OAuth: Why API Keys?

| Factor | API Keys | OAuth |
|--------|----------|-------|
| Setup complexity | Simple | Requires app registration |
| User experience | Just you | Supports multiple users |
| Token refresh | Never expires | Requires refresh flow |
| Use case | Internal tools | Distributed apps |

For a personal/team sync workflow, API keys are simpler and sufficient.

## Next Steps

Once authentication is configured, see:
- [Project Overview](./project-overview.md) — Architecture and goals
- [Sync Workflow](./sync-workflow.md) — How to use the sync script
