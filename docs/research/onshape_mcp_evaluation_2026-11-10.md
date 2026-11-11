# Onshape MCP server evaluation (2026-11-10)

Method: web search + page fetch only (standard mode). No Onshape calls, no installs, nothing run.
Most sources are vendor blogs or third-party directory pages; "unverified" marks what I could not confirm.

## Bottom line
Wait for the official server for production use; run ONE capped trial of it in a scratch document.
Do not adopt a community REST-wrapper server. Our python sync tool plus Playwright already cover
the same ground, and the gap that matters (compile notices) is not clearly covered by any MCP server.

## (a) Official server
- Exists. "Onshape FeatureScript MCP Server", launched 2026-08-13 by PTC under Onshape Labs (early access).
  - https://www.ptc.com/en/news/2026/onshape-launches-featurescript-mcp-server
  - https://www.onshape.com/en/blog/get-started-featurescript-mcp-server
  - https://www.onshape.com/en/blog/cad-data-ai-mcp-client-server (data safety)
- One integrator page (innfactory.ai) claims PTC has no official MCP server; this contradicts PTC's release
  and looks wrong or stale.
- Hosted, not local: `claude mcp add --transport http onshape https://fs-mcp.labs.onshape.app/mcp`
  (https://www.onshape.com/ja/resource-center/tech-tips/connect-featurescript-mcp-server-claude-code).
- Access: subscribe to the app in the Onshape App Store, then browser Authorize, then `/mcp` in Claude Code.
- Auth: OAuth-style app authorization as the signed-in user; no scopes published; no service account.
  Actions are attributed to the user.
- Tools (functional description only; exact tool names NOT published in anything I found, unverified):
  FeatureScript documentation search; create/insert/test/evaluate features; write code into a Feature
  Studio; review errors and iterate; inspect resulting geometry. Also stores a "notes file" in the account.
- No public source repo found (unverified).
- Whether it surfaces compile notices (the thing REST hides) is unverified. Blog says the AI "reviews errors";
  mechanism not described. Likely it uses the REST feature-status/errors it has, not the notices pane.

## (b) Community servers
| Name | URL | Stars/license | Auth | Notes |
|---|---|---|---|---|
| hedless/onshape-mcp | https://github.com/hedless/onshape-mcp | 164 stars, 74 forks, MIT, Python 3.10+, 80 commits on develop; last commit date not shown (unverified) | API keys (HMAC) in env/.env | 45 tools (listings differ: 13/35/45) |
| altendky/onshape-mcp | https://github.com/altendky/onshape-mcp | 20 stars, Apache-2.0 or MIT, likely Rust (unverified), 1854 commits, early dev | Own OAuth app (localhost callback) or API keys | Claims full REST API access, screenshots, export; no eval tool mentioned |
| saltyeg/cadkit-mcp | https://glama.ai/mcp/servers/saltyeg/cadkit-mcp | unverified | OAuth (read/write/delete scopes) | FeatureScript queries mentioned; directory page only |
| casys mcp-onshape | https://jsr.io/@casys/mcp-onshape/doc | unverified | unverified | seen in results, not examined |

hedless tool groups: documents (list/search/get/create, find_part_studios, get_elements, get_parts);
assembly + mates (12); assembly analysis (positions, features OK/ERROR/SUPPRESSED, interference);
sketch (4); features (extrude, thicken, revolve, fillet, chamfer, patterns, boolean); get_variables/set_variable/
get_features; `eval_featurescript` (read-only) + get_bounding_box; export_part_studio/export_assembly
(STL, STEP, PARASOLID, GLTF, OBJ).
Against our needs: NO Feature Studio create/edit/push tool listed; NO Part Studio error/notice reader
(only assembly feature states); NO generic feature insert (only fixed CAD feature types). It is a CAD-modeling
toolkit, not a FeatureScript-development one. Little value for us.

## (c) Quota implications
- Onshape states every MCP server call counts toward the API allocation (cad-data article above) and that
  MCP servers are counted like any API client (https://www.onshape.com/en/blog/ai-cad-engineering-roi-api-call-count).
- No per-tool call count is published. The model decides how many requests to make. Onshape's own example:
  about 2,500 API calls in a 7-hour FeatureScript MCP session (vendor figure). That is 25% of our 10,000/yr
  in one workday of use -- the key number for us.
- Usage draws from the personal allocation by default; a company allocation can be selected by instruction
  (Claude Code setup page). Our account is per-user enterprise, so this may be a way to move cost off the
  personal 10k pool. Unverified how the company allocation is sized or who controls it.
- Community servers: no caching mentioned in either README (unverified beyond README level). Expect 1+ REST
  call per tool call, plus listing/lookup calls before writes. Only 2xx/3xx count (matches our ledger rule).
- Our ledger/cap (`ONSHAPE_CALL_CAP`, `callstats`) only sees calls through `sync/core/client.py`. A hosted
  MCP server's calls are invisible to it, so a cap cannot be enforced locally. Only Onshape's usage page
  would show them.

## (d) Security
- Official: runs as the signed-in user, so it can touch any document that user can write. No scope
  narrowing published. Onshape's mitigation is advice: keep per-write approval prompts ON in the MCP client
  and test in a scratch document. Writes are "limited" to its tool list (no arbitrary REST), a plus vs
  community servers.
- Notes file stored in the account is sent to the AI provider each time it is read (vendor-stated).
- Community (hedless): long-lived API access/secret keys in env or .env -- full account rights, keys are
  not scoped to a document. altendky: `--client-secret` flag leaks via shell history; use env vars.
  OAuth apps registered as private still count against the quota (cadkit listing).
- Prompt injection: any tool that reads document content (feature names, variable names, comments,
  FeatureScript text, notes file) feeds attacker-controllable text into the model; combined with write
  tools and a user-level token this is a real path to unintended writes. Our own documents are single-author,
  so risk is low unless shared documents or imported libraries are read. Playwright has the same class of
  exposure already.
- Hedless has 45 tools incl. delete_feature and create_document: broad write surface; no safety notes in README.
- Hosted server means document-derived data transits PTC/Onshape infrastructure (same vendor, no new party)
  and the AI provider.

## (e) Overlap and gaps
We already have: python sync (push/pull tabs, repin), `fsapi.py` write/delete/status with call accounting,
REST evaluateFeatureScript runner, build_/check_ test pairs, Playwright notices (compile notices + regen
runtime errors, println, per-feature status), ledger + cap.
What an MCP server would add:
- FeatureScript docs search tool (official): we have a local std/ mirror (FS 2878) and FsDoc via
  onshape-api-researcher; modest value, and the mirror is older than the 3070/3083 versions in use.
- Interactive loop in chat without our scripts: convenience only; we already script it cheaper.
- Geometry inspection by the model: partial overlap with eval runner.
What it does NOT obviously add: compile notices with line/col (Playwright remains the only route),
call caps, pinned-version import handling, our println lint/fscheck, tab-folder placement.
Real risk: it duplicates the chatty edit-regen-read loop that burned 25% of the quota; our tooling was
built to collapse N calls to one eval. An agent driving the MCP server by default would bypass that.
For the Django/React iframe app: MCP is a dev-time agent tool; it does not belong in the runtime backend.
Keep direct REST there. No gap filled.

## (f) Recommendation: wait, with one bounded trial
Do not adopt now. Reasons: early access, tool names/scopes/call costs unpublished, uncapped quota burn
(vendor example 2,500 calls/7 h), no local ledger visibility, overlap with working tooling.
Revisit when ANY of: (1) Onshape publishes tool list + per-tool call counts; (2) it exposes compile
notices/line numbers or regen results in one call; (3) a company API allocation is confirmed available for
dev use; (4) it reaches GA with scoped (document-level or read/write) permissions; (5) our Playwright
notice path breaks or becomes the bottleneck.
Community servers: skip (REST wrappers, keys, no Feature Studio editing).

Minimal safe trial (do once, after approval; this report did none of it):
1. Create a throwaway document (ask first, per repo rule) with one Part Studio and one Feature Studio.
2. Add the server in a SEPARATE project-scope config or a scratch directory, not the two repos' .mcp.json.
   Keep per-write approval ON; do not allow-list its tools.
3. Note the usage figure on Onshape's account/API usage page before and after (our ledger cannot see it).
4. Task script: list tools (`/mcp`, zero calls), create FS1, write a 20-line feature, insert, evaluate,
   read an intentional compile error. Stop after 30 minutes or ~100 calls by that usage readout.
5. Record: tool names, calls per step, whether compile error text includes line/col, whether the notes
   file is created and what it holds.
6. Success = notices obtained in <=2 calls with line/col AND per-step cost under ~5 calls. Otherwise remove.
7. Remove the server and revoke the app authorization afterwards (App Store / Application Settings).

## Unverified list
Exact official tool names; official scopes; per-tool call counts; whether company allocation is usable by us;
last-commit dates and star counts beyond those fetched (page scrape, may be stale); cadkit and casys
details; whether the official server reads compile notices; repo for official server (none found).
Other sources: https://www.onshape.com/en/blog/featurescript-mcp-server-enables-text-code-cad ,
https://develop3d.com/cad/onshape-featurescript-mcp-lets-users-build-custom-cad-features-fast/ ,
https://www.getleo.ai/blog/cad-mcp-server-comparison-2026 , https://glama.ai/mcp/servers/hedless/onshape-mcp
