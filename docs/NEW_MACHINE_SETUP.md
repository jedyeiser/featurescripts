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

## Where each file from the jump drive goes (D:\Laptop_Transfer\featurescripts\)
| On the drive | Put it at | Rename? |
|---|---|---|
| `dot-env.SECRET.txt` | `C:\Users\jed.yeiser\Documents\featurescripts\.env` (repo root, next to `pyproject.toml`) | Yes -> exactly `.env`. Turn on Explorer > View > Show > File name extensions so it does not become `.env.txt`. |
| `.sync-state.json` (optional) | repo root | no |
| `claude_memory\*` (the contents) | `C:\Users\jed.yeiser\.claude\projects\C--Users-jed-yeiser-Documents-featurescripts\memory\` | no (rename the project folder if username/path differ) |
| `claude_global\settings.json` | `C:\Users\jed.yeiser\.claude\settings.json` | no; merge into the installer's file |

No Windows environment variables are needed: credentials come from `.env` via python-dotenv. Optional per-run variables
(`ONSHAPE_CALL_CAP`, `ONSHAPE_CALLER`, `FS_SYNC_TIMEOUT`) are set on the command line.
Check: `venv\Scripts\python.exe -m sync.main verify-auth` from the repo root.

## Software to install (the new machine has only Python)
Required for the core workflow (FeatureScript edit -> fscheck -> push to Onshape -> read notices):
1. **Git for Windows** (2.50 here). Then `git config --global user.name jedyeiser` and set your email.
2. **GitHub CLI** `gh` (2.79 here), then `gh auth login`. Handy for PRs/visibility checks; plain `git push` also works via Git Credential Manager.
3. **Claude Code** (2.1.x here). Sign in once.
4. **VS Code** (+ extensions below).
5. **Node.js 22.23.1** (eocProductData needs it; featurescripts only needs Node >= 20, so 22 covers both. The old laptop had 20.19): required here because `.mcp.json` launches the Playwright MCP server through `npx @playwright/mcp@latest`.
6. **Python**: you have 3.14. A venv is built from whichever Python creates it; it does not choose a version for you. The repo says
   `requires-python >=3.10` and this laptop ran 3.13.2. 3.14 should work, but if `pip install` fails building a wheel
   (numpy/scipy/lxml/pillow), install 3.13 alongside and use `py -3.13 -m venv venv`.
7. After the venv: `pip install -e ".[dev]"`, then `venv\Scripts\python.exe -m playwright install chromium`
   (the sync `notices` / `--check` commands drive headless Chromium). Playwright is a declared dependency, but on the old laptop it was
   actually installed globally, not in the venv -- verify `venv\Scripts\python.exe -c "import playwright"` works.

Optional / only for some work:
- `pip install -r requirements-docs.txt`: explainers, decks, figures, contact sheets (matplotlib, numpy, scipy, python-pptx, pillow, CairoSVG...).
  These are NOT in pyproject.toml. CairoSVG may need the Cairo DLL on Windows; if SVG->PNG fails, install the GTK3 runtime or skip it.
- **Pandoc** (3.5 here): docs/markdown conversion.
- Not installed on the old laptop either, so not needed: LibreOffice, ffmpeg, ImageMagick, Graphviz, uv.
- eocProductData only (other agent's area): Node for the React/Vite frontends, its own venv (Django 5.2 + DRF), Docker/Postgres tooling.

VS Code extensions worth installing for this repo:
`anthropic.claude-code`, `ms-python.python`, `ms-python.vscode-pylance`, `ms-python.debugpy`, `github.vscode-pull-request-github`,
`mechatroner.rainbow-csv` (materialData / test CSVs), `ms-toolsai.jupyter` (the .ipynb files in Documents),
`humao.rest-client` or `rangav.vscode-thunder-client` (Onshape REST probing).
There is no FeatureScript extension: `.fs` files are edited as plain text and tested in Onshape.
For eocProductData add: `dbaeumer.vscode-eslint`, `esbenp.prettier-vscode`, `bradlc.vscode-tailwindcss`, `dsznajder.es7-react-js-snippets`,
`bigonesystems.django`, `maxchamps.django-commands`, `bibhasdn.django-html`, `ms-azuretools.vscode-containers`, `ms-ossdata.vscode-pgsql`.
Skip (installed on the old laptop but not needed): `saoudrizwan.claude-dev` (Cline, a different agent), `iceworks-team.iceworks-refactor`,
`christian-kohler.path-intellisense`, `qwtel.sqlite-viewer`, remote-containers.

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
