# New machine setup (written 2026-10-01 on the old laptop)

State at hand-off: `master` fully pushed to github.com/jedyeiser/featurescripts (a8648e6 or later).
The repo is PUBLIC -- never commit `.env`, memory files, or browser sessions.

## What is where
| Thing | In git? | How it moves |
|---|---|---|
| All code, docs, `.claude/agents`, `.claude/featurescript-corrections.md`, `.claude/settings.json`, `.claude/settings.local.json`, `CLAUDE.md` | yes | `git clone` |
| Claude **memory** (`~/.claude/projects/<encoded-path>/memory/`, 20 files) | NO | jump drive: `featurescripts/claude_memory/` |
| `.env` (Onshape API key/secret/base URL) | NO | jump drive `dot-env.SECRET.txt` (rename to `.env`) -- or generate new keys in the dev portal. Wipe the drive copy afterwards. |
| `.sync-state.json` (tab <-> Onshape element map) | NO | jump drive; optional (a `pull` rebuilds it) |
| Browser login for `sync.main notices/--check` (`~/.fs-sync/onshape-state.json`) | NO | do NOT copy; run `python -m sync.main login` |
| `venv/` | NO | recreate |
| Global `~/.claude/settings.json` (model, tui) | NO | `claude_global/settings.json`; merge, don't blindly overwrite |
| Past conversation transcripts (`*.jsonl`) | NO | not needed; memory + CLAUDE.md carry the knowledge |

## Order of operations
1. Install Git, Python 3.13, Node (only if you touch eocProductData), then **Claude Code**. Run `claude` once and sign in, then exit.
   (Installing first is cleaner: first run creates `~/.claude`; you then drop memory in. Copying files first also works, since they are plain files.)
2. `cd %USERPROFILE%\Documents` and `git clone https://github.com/jedyeiser/featurescripts.git`
   -- same path as before (`C:\Users\jed.yeiser\Documents\featurescripts`) if the Windows username is the same.
3. In the repo: `python -m venv venv`, `venv\Scripts\python.exe -m pip install -e ".[dev]"`, `venv\Scripts\python.exe -m playwright install chromium`.
4. Copy `dot-env.SECRET.txt` -> `featurescripts\.env`. Optionally copy `.sync-state.json` to the repo root.
5. Memory: copy the contents of `claude_memory\` to
   `%USERPROFILE%\.claude\projects\C--Users-<USERNAME>-Documents-featurescripts\memory\`.
   The folder name is the repo path with `\` and `:` turned into `-`. **If the Windows username or repo path differs, rename the folder to match.**
   Easiest check: start `claude` in the repo once, then look in `~/.claude/projects/` for the folder it created, and put memory there.
6. `venv\Scripts\python.exe -m sync.main verify-auth`, then `python -m sync.main login` (browser session).
7. Smoke test, no live API calls needed: `python fscheck.py driven_offset/*.fs`.
   Remember the API budget (CLAUDE.md): 10k calls/user/yr, shared by both machines.

## Things that bite on a new machine
- `settings.local.json` allow-rules hardcode `venv/Scripts/python.exe`; keep the venv at `venv\`.
- Windows line endings: git warns "LF will be replaced by CRLF". Consider `git config core.autocrlf true` (or add a `.gitattributes`) so diffs stay clean.
- Never run `.claude/worktrees` agents' leftovers: that dir is now gitignored and not cloned.
- Two machines: `featurescriptSettings.json` `last_push` timestamps change on every push and will cause merge noise. Commit/pull before switching machines.
- `pushproject` makes AUTO-BACKUP commits; push regularly so the other machine is not 1,000 commits behind again.
- Pinned Onshape versions of curve_core / extract_outputs are Onshape-side; nothing to move.

## eocProductData (separate repo, `C:\Users\jed.yeiser\Projects\eocProductData`)
Remote: github.com/jedyeiser/elevateProductData. **It had uncommitted work when this was written** (see `eocproductdata/UNCOMMITTED_eoc.patch` + untracked `bSplineCurve.fs` on the drive). Pushing `main` there deploys to production, so it was NOT committed or pushed. Commit it deliberately, or apply the patch on the new machine.
Its `.env`, its Claude memory (`eocproductdata/claude_memory`) and its untracked `.claude/agents` are on the drive too.
Its memory goes in `~/.claude/projects/C--Users-<USERNAME>-Projects-eocProductData/memory/`.

## Other repos under Documents (all have uncommitted changes, none ahead of origin)
Onshape_API_Tools, materialViewer, productData, productTest -- not covered here; commit or stash before you leave the old machine if you need them.
