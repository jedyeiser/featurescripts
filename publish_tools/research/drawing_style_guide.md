# Drawing style guide (Onshape) -- ski / snowboard engineering, EOC brands (2026-09-29, v2 after user review)

Scope: every drawing template in "Ski Drawing Templates" (doc 52b5bde0, K2 enterprise). First master:
**K2 SKIS - PART A3 (template)** (plan: `drawing_templates_plan.md` sections 4-6). Direction from the user:
clean and modern, within what Onshape offers. Rule of thumb used below: prefer Onshape's own defaults unless a
standard or legibility gives a reason to change -- every default we keep is one less setting to drift between
templates and drawings.

What Onshape actually offers was read from a scratch drawing (deleted afterwards). Screenshots:
`img/templates/probe_*.png` (units_precision, dimensions, annotations, views, formats, tables, note_font_list,
note_drawing_properties, note_sheetref_properties, sheet_size, move_to_layer, layer_thickness_test).

## 1. Font -- Noto Sans (Onshape default)
| Offered (note editor + Drawing properties) | Verdict |
|---|---|
| **Noto Sans** | **chosen** |
| Open Sans | same design family as Noto Sans (Noto Sans grew out of it), fewer glyphs -- no gain |
| Noto Sans UI | tighter line spacing, lighter; fine on screen, weaker on laser prints |
| Arimo (Arial metrics) | neutral but reads "office 2005"; narrower -- the only real alternative if long names overflow |
| DejaVu Sans / Mono / Serif, Tinos, Noto Serif | wide or serif; not modern-technical |
| AR* (APMONO, ARComp, ARIsoP1, ARItal, ARMono, ARSimp, ARTxt) | CAD stroke fonts (ARIsoP1 = ISO 3098 lettering); single-stroke, thin and dated in PDF |
| Noto Sans CJK variants | only for Chinese / Japanese / Korean text |

Why Noto Sans: modern humanist sans, very legible at 2.5 mm, TrueType so PDFs carry real vector glyphs, full
Latin coverage (the o-umlaut in Voelkl, sharp s, degree, plus-minus, diameter), and it is Onshape's default for *every*
annotation type -- dimensions, notes, callouts, BOM / custom / revision tables, view labels -- so anything the
template does not set explicitly still matches. Where: Drawing properties > Dimensions / Annotations / Views /
Tables > Font (left at Noto Sans). In notes created by API the font is written with MText `{\fNoto Sans|b1|i0|c0|p0;...}`.

## 2. Text heights (ISO 3098 series 2.5 / 3.5 / 5 / 7)
| Role | Height | Where set |
|---|---|---|
| Title-block field labels, the "NOTES" label | 2.5 regular (NOTES bold); **A4: 1.8** (section 5b) | per note (API `textHeight`) |
| Title-block values, notes, dimensions | 3.5 | Drawing properties > Dimensions / Annotations > Notes (3.5 is the default) |
| **All tables** (BOM, custom, cut list, hole, revision, general): title, header and content rows | **2.5** (header bold -- Onshape's default B toggle on header rows, kept) | Drawing properties > Tables > each table type > Header / Content (/ Title) row text |
| Title-block part name (bold), units "mm" | 5 | per note |
| View labels (section / detail names, scale under the label) | 5 | Drawing properties > Views > View labels height |
| 7 mm | reserved for the large sheets (PRIMITIVE A0 title) | -- |

Why 5 (not 7) for the part name on A3: names like "RD 20TAC 28 178 4101" plus variant suffixes run 25-40
characters; at 7 mm they would overflow the 144 mm field, at 5 mm they fit with room (measured: 27 characters =
about 80 mm). Labels 2.5 / values 3.5 is the one "label vs value" rule used in the title block and in tables
(bold headers).

Why 2.5 for tables (user decision 2026-09-29): our tables are dense engineering tables (Station table, the six
primitive tables with 35+ data rows, the revision history) that must fit next to full-length views; 2.5 mm is the
smallest ISO 3098 height for general text on A3 and larger sheets, still reads cleanly in Noto Sans on laser prints and PDF, and matches the
title-block label height, so the sheet has one "small" size. Row height follows from text + padding: 2.5 mm text
with the default 1.5 mm vertical padding gives the minimum row of 7.17 mm (Onshape refuses anything lower).

## 3. Line weights (ISO 128 line group 0.5 / 0.25, frame 0.7)
| Element | Weight | Where |
|---|---|---|
| Frame (ISO 5457) + centring marks | 0.70 | layer **Border frame**, Drawing properties > Formats > Border frame thickness |
| Title-block outer edges (top + left; v2 has no interior zone lines) | 0.35 | layer **Border zones**, Formats > Border zone thickness |
| Title-block cell dividers, projection symbol | 0.25 | layer **Title block**, Formats > Title block thickness |
| Visible edges | 0.50 | Views > Visible edges (default 0.40) |
| Hidden / tangent edges, hatch, detail and aux profile lines, break lines, threads | 0.25 | Views |
| Section cutting lines | 0.50 | Views > Section views |
| Centerlines, centermarks, sketch geometry drawn in the drawing | 0.25 | Construction geometry |
| All table grids (BOM, custom, cut list, hole, revision, general) | 0.25 | Tables > Line thickness (default 0.13 disappears on laser prints) |

Onshape has **no per-line weight** for drawing sketch lines (toolbar has only constraints); the weight comes from
the layer. So the template uses exactly the three format layers + the drawing layer, which gives the one accent
(frame 0.7), one structure weight (0.35) and one thin weight (0.25). Colour: black everywhere (default).

## 4. Units, dimensions, projection
| Setting | Value | Why | Where |
|---|---|---|---|
| Standard | ISO | EOC / European consumers | created with `standard: ISO` |
| Units | mm | | Units and precision > Units |
| Decimal separator | **Period** (open question: comma for German consumers) | one choice for all brands; the K2 enterprise default came through as COMMA even when the API asked for PERIOD -- set explicitly in the UI | Units and precision |
| Linear precision | 0.1 | station widths to 0.1 mm; override to 0.12 per dimension for thickness | Units and precision > Precision |
| Tolerance precision | 0.12 (kept) | tolerances such as +-0.05 need 2 places | |
| Angular precision | 0.1 deg (default) | | |
| Leading zeros on / trailing zeros **off** (defaults) | "12" not "12.0"; "0.5" not ".5" (ISO) | Leading and trailing zeros |
| Arrowhead | 2.5 mm filled (default; Onshape offers size only, no style choice) | ISO 129 closed filled arrow | Dimensions / Annotations > Arrowhead |
| Text alignment | aligned with dimension line (ISO, default) | | Dimensions |
| Projection | **first angle** (ISO E) + symbol in the title block | ISO default; open question in the plan: confirm K2 practice | Views > Projection angle |
| View labels | style **A-A**, **scale label On** (scale under the label, as today's EDA - EDA / 1:2) | Onshape offers only On / Off for the scale label (checked 2026-09-29) -- there is no "only when it differs from the sheet scale". Kept On; on single-scale sheets delete or hide the label per view. | Views > Insert view defaults > Scale label |
| Date format in the title block | **yyyy-mm-dd** (ISO 8601) | unambiguous for US + EU readers; Onshape default is yyyy/mm/dd | note editor > date field > Date time format |

## 5. Title block (ISO 7200 style, 180 x 40, bottom right)
```
x230      266                                                      410
 +--------+--------------------------------------------------------+ y50
 |  LOGO  | TITLE        <part name, 5 bold>                       |
 |        +--------------------------------------------------------+ y38
 | (28 w) | DESCRIPTION  <part description>                        |
 |        +---------------------+------------+----------+----------+ y28
 |  SKI   | MATERIAL            | WEIGHT     | SCALE    | UNITS /  |
 |  ENG.  +----------+----------+-----+------+-----+----+ PROJ.    | y19
 |        | DRAWN BY | DATE     | SIZE| SHEET | REV |    | mm [1st] |
 +--------+----------+----------+-----+-------+-----+----+----------+ y10
```
Text placement (v2, measured in the exported PDF): labels 2.5 at 0.6 below the cell's top line; 3.5 values at
3.7 below it, which puts every value baseline 1.7 mm above the cell's lower line (descenders of "g" / "J" clear
it by 0.6-0.8 mm) and the value's cap top 0.55 mm under the label baseline. Part name (5 bold) baseline 2.4 mm
above its line.
Fields and sources (all live property links, not typed text):
| Field | Link |
|---|---|
| Title | sheet reference > Name (case: Unmodified) |
| Description | sheet reference > Description (Unmodified) |
| Material | sheet reference > Material (Unmodified -- the default "All uppercase" printed "P-TEX", "211.6 G") |
| Weight | sheet reference > Mass (Unmodified) |
| Scale / Size | drawing > Sheet scale / Sheet size |
| Sheet | drawing > Sheet number " / " Total sheets |
| Drawn by / Date | drawing > Drawing drawn by / Drawing date drawn (yyyy-mm-dd) |
| Rev | sheet reference > Revision (the part's release revision; the revision table keeps the drawing's own history) |
Dropped vs the old K2 template: FINISH, DWG NO. (part numbers are blank today), four unlabelled DRAWN rows,
duplicated logo. Labels are small uppercase black (no grey: grey prints unevenly). "Hide property background" is on
(no grey boxes on screen); placeholders stay on (Onshape prints nothing for an empty field anyway).

Logo cell 36 x 40: logo 28 mm wide (aspect kept), "SKI ENGINEERING" 2.5 bold under it. Source: brand PNG from
`icons/brands/<brand>_drawing.png`, uploaded as a blob tab; image placed with two corner clicks.

## 5b. A4 compact profile (user decision 2026-09-29; A3 / A2 unchanged)
On A4 the 180 x 40 block and the 180 mm revision table took too much of a 267 x 190 mm frame. A4 uses the
COMPACT profile of `sheet_layout.py` (`PROFILE_BY_SIZE = {"A4": COMPACT}`): the same cells and fields, scaled.
| Item | A3 / A2 (STANDARD) | A4 (COMPACT) |
|---|---|---|
| Title block | 180 x 40 | **150 x 32** (x 137-287, y 10-42); rows 7.2 / 7.2 / 8 / 9.6; logo cell 30 wide |
| Columns (from the block's left edge) | 36 / 92 / 120 / 144; 72 / 102 / 116 / 132 | 30 / 77 / 100 / 120; 60 / 85 / 97 / 110 |
| Field labels | 2.5 | **1.8** (top 0.5 below the cell line) |
| Values | 3.5 | **2.5** (top 3.1 below the line: baseline 1.6 above the 7.2 row's lower line) |
| Part name | 5 bold | **3.5 bold** (top 3.6 below the title line) |
| Units "mm" / projection symbol | 5 / 7 mm tall | 3.5 / 5.25 mm tall (symbol x 0.75) |
| Department line | 2.5 bold, left at 2.6 | 1.8 bold, centred in the logo cell; two lines when wider than 28 mm (SNOWBOARD / ENGINEERING) |
| Logo | 28 wide (32 for wide logos), centre (18, 26) | 23 wide (26 for wide logos: Ride, Volkl, LINE), centre (15, 20) |
| Revision table | 180 wide, cols 18 / 110 / 24 / 28, 2.5 text | **150 wide**, cols 15 / 92 / 20 / 23, **1.8 text** (title / header / content), rows at Onshape's minimum for that text; top-right at the frame corner, right edge = title block's |
| NOTES label | 2.5 bold | 1.8 bold, top 1 mm below the title block top |
| Other table defaults (BOM, custom, cut list, hole, general) | 2.5 | 2.5 (unchanged -- data tables must stay readable) |

Why 1.8 is allowed: ISO 3098-0 / ISO 7200 give the lettering series 1.8 / 2.5 / 3.5 / 5 / 7 and allow **1.8 mm as
the minimum** height for secondary text on the small formats (A4, A3); 2.5 remains the minimum for A2 and larger.
Here 1.8 is used only for what is read once and looked up, not measured: field labels, the department line, the
NOTES heading and the revision-history rows. Everything a reader takes a value from (title-block values,
dimensions, notes, data tables) stays 2.5 or larger. Noto Sans at 1.8 stays legible on 600 dpi laser prints and
in PDF (vector glyphs), and a single A4 sheet is normally read at arm's length or on screen.


Only four things are fixed; everything inside the frame is open space where users place views and tables:
| Element | A3 position | Rule (all sizes, `devtools/onshape/templates/sheet_layout.py`) |
|---|---|---|
| ISO 5457 frame + centring marks | x 20-410, y 10-287; marks at x 210 / y 148.5 in the margin only | margins 20 left / 10 elsewhere |
| Title block 180 x 40 | x 230-410, y 10-50 | bottom-right corner of the frame |
| Revision table (Onshape's own) | x 230-410, top at y 287; columns Revision 18 / Revision description 110 / State 24 / Date approved 28; rows 7.17; 5 visible rows | fixed corner TOP-RIGHT at the frame's top-right corner, so its right edge = the title block's right edge; it grows downward |
| "NOTES" label (2.5 bold, no lines) | x 22, top at y 48.8 | bottom-left, top aligned with the title block top |
The title block's own top and left edges are 0.35 lines on the Border zones layer (the frame supplies bottom and
right). No grid references (A-F / 1-8) -- not needed for PDF-first use.

Layers: frame + centring marks -> Border frame; title-block outer edges + "NOTES" -> Border zones; title block
cells, labels, values, symbol, logo -> Title block; **Formats > Lock = Locked** (users cannot move or delete them;
property links still update). The revision table stays on the drawing layer (it must be editable).

## 7. How to reproduce / known Onshape limits
A4 compact rework of an existing A4 tab, in place (2026-09-29, ~3 min per tab): `compact_a4.py <queue> <BRAND>`
= `template_set.relayout(eid, "A4", brand, heights=True)` (API: positions + text heights, property links kept) +
UI (Revision tables text 1.8, `compact_revision_table()` columns 15 / 92 / 20 / 23 and rows 6.01 -- Onshape's
minimum for 1.8 text is 5.41 title / 5.52 header / 6.01 content, all set to 6.01 -- logo re-inserted, lock) +
.dwt re-export + PNG. Onshape has no "switch template" on an existing drawing, so test drawings are rebuilt
from the .dwt (`make_size_test_drawing.py A4 --safe`, check, then `--finish`).

Scripts: `devtools/onshape/templates/` (README in the module docstrings). Geometry for every sheet size comes from
`sheet_layout.py` (`sheet_layout("A4" | "A3" | "A1" ...)`); `build_part_a3_master.py --size A4` builds that size's
tab (`K2 SKIS - PART A4 (template)`, layout json `part_a4_layout.json`). v2 rework of the existing A3 master:
`rework_part_a3_v2.py` (API) + UI: Tables text 2.5, revision table fixed corner / column widths / row heights /
drag to the frame corner. Order for a new master: `build_part_a3_master.py`
(phase 1 value notes) -> `ui_link_properties.py` -> `build_part_a3_master.py --phase rest` ->
`ui_drawing_properties.py` (add the 2.5 table rows) -> `ui_layers.py` (pixel picks are A3-specific; the tb window
pick no longer needs `move_zone_lines.py`, which is obsolete in v2) -> `upload_logo.py` + insert image (UI) ->
revision table (UI: toolbar Revision table, fixed corner top-right, click the frame's top-right corner; then per
column right-click > Resize... width, and row height 7.17) -> lock -> `export_dwt.py --replace`.
- The drawings API has no line weight, layer, font-per-document, image or property-field support; MText formatting
  (font, bold, \P) does work in API notes.
- A note keeps the width it was created with (text wraps inside it), so property notes are created with a long
  placeholder; a position-only `onshapeEditAnnotations` keeps the property link.
- `createDrawingAppElement` accepts only a **.dwt blob** as template (`templateElementId` of a drawing tab ->
  "not a BLOB element"); hence the `K2 SKIS - PART A3 (template).dwt` tab exported from the master. Re-export it
  after every template edit. The UI's "Create drawing" dialog can use either.
- Mass prints with the document's mass precision (3 decimals in the test document: "211.629 g"); the note field
  has no precision option. Set Document properties > Units > Mass precision to 0.1 in part documents, or accept.
- The sheet-reference dropdown lists only parts that have a view on the sheet; a composite view alone gives Name
  but blank Material / Mass (see plan section 5).

## Open questions (carry over)
Decimal separator (period vs comma), first vs third angle at K2, mass precision policy, whether each brand gets
its own PART A3/A4 or one EOC-branded set.
