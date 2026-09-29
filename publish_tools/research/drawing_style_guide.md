# Drawing style guide (Onshape) -- ski / snowboard engineering, EOC brands (2026-09-29)

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
| Title-block field labels, zone label "NOTES" | 2.5 regular (NOTES bold) | per note (API `textHeight`) |
| Title-block values, notes, dimensions, table content + header, revision table title | 3.5 | Drawing properties > Dimensions / Annotations > Notes / Tables (3.5 is the default; revision + general table *title row* changed 4.55 -> 3.5) |
| Title-block part name (bold), units "mm" | 5 | per note |
| View labels (section / detail names, scale under the label) | 5 | Drawing properties > Views > View labels height |
| 7 mm | reserved for the large sheets (PRIMITIVE A0 title) | -- |

Why 5 (not 7) for the part name on A3: names like "RD 20TAC 28 178 4101" plus variant suffixes run 25-40
characters; at 7 mm they would overflow the 144 mm field, at 5 mm they fit with room (measured: 27 characters =
about 80 mm). Labels 2.5 / values 3.5 is the one "label vs value" rule used in the title block and in tables
(bold headers). Dense tables may drop content to 2.5 per table (ISO minimum for A3).

## 3. Line weights (ISO 128 line group 0.5 / 0.25, frame 0.7)
| Element | Weight | Where |
|---|---|---|
| Frame (ISO 5457) + centring marks | 0.70 | layer **Border frame**, Drawing properties > Formats > Border frame thickness |
| Zone structure (view / table-revision / notes-title split) incl. title-block outer edges | 0.35 | layer **Border zones**, Formats > Border zone thickness |
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
| View labels | style **A-A**, **scale label On** (scale under the label, as today's EDA - EDA / 1:2) | | Views |
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

## 6. Sheet zones (PART A3, from the plan) and layers
View zone y150-287 (plan view 1:5 + profile), table zone x20-230 y50-150, revisions x230-410 y50-150 (Onshape
revision table, top-left corner fixed at (230,150), 5 visible rows, grows down), notes x20-230 y10-50, title block
x230-410 y10-50. ISO 5457 frame 20 left / 10 elsewhere; centring marks in the margin only (the standard's 5 mm
overrun into the frame would clash with the y150 zone line). No grid references (A-F / 1-8) -- not needed for
PDF-first use; add later if prints are marked up by zone.

Layers: frame + centring marks -> Border frame; zone lines + "NOTES" -> Border zones; title block lines, labels,
values, symbol, logo -> Title block; **Formats > Lock = Locked** (users cannot move or delete them; property links
still update). The revision table stays on the drawing layer (it must be editable).

## 7. How to reproduce / known Onshape limits
Scripts: `devtools/onshape/templates/` (README in the module docstrings). Order: `build_part_a3_master.py`
(phase 1 value notes) -> `ui_link_properties.py` -> `build_part_a3_master.py --phase rest` ->
`ui_drawing_properties.py` -> `ui_layers.py` (frame, zones, tb, tb2; `move_zone_lines.py away/restore` around the tb
window pick) -> `upload_logo.py` + insert image (UI) -> revision table (UI) -> lock -> `export_dwt.py`.
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
