# EOC Data Tools -- Export v2 ("package") proposal (2026-09-30)

Research: 3 agents (existing Export tab review; Onshape export API with live tests; FS-side tagging) + my own
checks. [V] = verified live, [D] = documented only, [U] = unverified. Samples: session scratchpad `export_research/`.

## 1. Where we stand (verified)
- The **Export tab already exists** (July): per-part STEP / Parasolid / IGES / STL, assemblies, drawings PDF/DXF/DWG,
  body-type filter, filename tokens, client-side zip or save-to-document. It covers ~half the wish-list.
- **Surfaces -> DXF is BROKEN.** It uses the Part Studio translation, which only translates sheet-metal flat
  patterns; a normal sheet body FAILS ("failed to translate") -- re-tested today on TOP_SURFACE [V].
- Gaps: one global model format (no per-body choice, no "none"); no format options sent (STEP AP, STL resolution,
  PDF options, DXF units/version); flat zip, no folders, no manifest, failures not in the zip; nothing remembered
  between sessions; no presets.
- Bugs: part list always read from the WORKSPACE even when exporting a VERSION (added/removed parts fail or go
  missing); multi-file results can collide in the zip; download holds the whole file in server memory and can
  exceed gunicorn's 200 s; call estimate (3/job) is low (polling at 1.5 s ~ 8 calls/job).
- No evidence it has been run end-to-end on a real K2 document.

## 2. Onshape API facts that shape the design
| Topic | Fact |
|---|---|
| Face / flat-surface DXF | Only via the UNDOCUMENTED `.../exportinternal` (what the UI's "Export as DXF" uses): face ids + `view` matrix, true ARC/LINE/SPLINE, mm, DXF version selectable [V]. Undocumented = can break without notice. |
| DIY DXF | `bodydetails` returns exact edge geometry (lines, arcs, B-spline control points) [V] -> we can write clean DXF ourselves (Python `ezdxf`), on the face's own plane, our layers, our naming. |
| STEP | AP242 / AP214 / AP203 selectable; **always metres** regardless of unit options [V]. Sheets/wires export as surface/curve sets [V]. Assemblies keep structure; `flattenAssemblies` has no effect [V]. Composite id exports all members [V]. |
| IGES | mm [V]. |
| STL / Parasolid / glTF | **Synchronous** GET endpoints (per part or whole studio), 0.2-1.7 s, STL in mm [V] -- no create/poll, far fewer calls. |
| Drawings | PDF: `selectablePdfText` embeds fonts [V]; sheet selection params accepted, effect unverified [U]. DXF/DWG: version via `versionString` [V]. |
| Ids | REST `partId` == FeatureScript body id for every part (90/90 in Station tests) [V] -> an FS query variable can drive the selection directly. |
| Limits | Enterprise quota 10,000 API calls per full user per year (402 when exceeded); API-key / private-OAuth calls count; 4xx/5xx don't [D]. Per-endpoint rate limit with `Retry-After` [V]; 12 parallel STEP jobs fine [V]. |
| Export rules | Company "export rules" exist (filename conventions); K2 has none configured [V]. |

## 3. Proposal
**A. Rules + presets instead of a format picker per row.**
Default rules by body type: Solids -> STEP (AP214); flat Sheets -> DXF; Wires / points -> none; Assemblies -> STEP;
Drawings -> PDF. Several formats per item allowed (e.g. STEP + IGES); per-row override incl. **None**.
Saved **presets** (company-wide): e.g. "Factory 2D" (flat DXF + drawing PDFs), "Tooling 3D" (STEP + IGES solids,
STL), "Everything". Remember the last selection per document (browser storage).

**B. Let the model say what to export (optional, per studio).** Read export query variables with one FS eval per
studio: `export_dxf` (flat sheets, e.g. Station geometry "FLAT" copies), `export_step`, `export_stl`. Selected
bodies are pre-ticked with their format; missing variables are shown as "not set" (the contract we agreed:
required names, tools flag what's missing). Without variables, the rules in A apply.

**C. Our own DXF writer (backend, `ezdxf`) for surfaces.** From `bodydetails`: exact lines/arcs/splines, mm, on the
face's plane (no projection surprises), one file per flat body, layers `OUTLINE` (+ later `STATIONS`, `DATUM` from
the Station geometry WIRES composite -- outline, station lines and datum in ONE DXF for the factory). Keep
`exportinternal` only as a fallback/compare -- don't build the core on an undocumented endpoint.

**D. Cheaper, sturdier jobs.** Sync endpoints for STL / Parasolid; async only for STEP / IGES / PDF / drawing DXF;
poll with backoff (1 s, 2 s, 4 s ...) not every 1.5 s; stream downloads (no whole-file buffering, no 200 s risk);
realistic call estimate shown before running.

**E. A package, not a pile.** Zip with folders (`DXF/`, `STEP/`, `PDF/`, ...), `manifest.csv` (file, part name,
number, revision, material, mass, body type, format, units -- STEP is metres!, source document + version id,
export date, user), `failures.txt`. Factory packages default to a **released version** as source (reproducible),
with the part list read from that same version (fixes the bug).

**F. Filenames.** Keep the token engine; add `{material}` and `{view}`; default for flats `{name}.dxf`.

## 4. Phasing
| Phase | Scope | Size |
|---|---|---|
| 0 | Fix bugs (version part list, zip collisions, call estimate); hide the broken surface->DXF path | small |
| 1 | Rules + multi-format + None + presets + folders + manifest + sync STL/Parasolid + poll backoff | medium |
| 2 | Own DXF writer from bodydetails (flat sheets); live test on a real RD doc | medium |
| 3 | Query-variable auto-selection; Station geometry WIRES (station lines, datum) as DXF layers | medium |

## 5. Decisions for the user
1. Rules + presets + per-row override (recommended) vs a format dropdown on every row?
2. Own DXF writer (recommended) vs the undocumented `exportinternal`?
3. Station lines / datum inside the factory DXF (as layers) -- wanted?
4. Default source for packages: latest released version (recommended) or workspace?
5. Which presets? (names + what each contains)
6. STEP in metres -- acceptable for the tooling suppliers, or do they need mm (then IGES/Parasolid, or convert)?

## 6. Note
The API research agent's one UI-dialog capture changed your Onshape user setting `exportDrawingOptions`: the
sketch DXF/DWG export dialog now defaults to "Download" instead of "Store file in a new tab". No document changed;
flip it back in the dialog if you prefer the old default.

## 7. User requirements (2026-09-30, round 2)
- Solids: extract TOP and/or BOTTOM face to DXF; 3D as IGES / STEP / Parasolid / STL (or the best B-rep if too much).
- Surfaces: faces -> DXF, include or exclude interior edges; holes always (boundary = edges used by exactly one selected face).
- Wires -> DXF. Optional spline -> arcs / lines / polylines conversion, optimised for FEWEST breaks (reuse our biarc /
  arc-recognition work from curve_core / arcFit).
- Drawings -> PDF.
- Names inherited from the reference; override; "restore from reference".
- Broken references must be handled; export settings should persist (asked whether that's foolhardy).
- DXF writer approved: combine faces, map wires/edges later, pattern/shift for the factory's cut file.
