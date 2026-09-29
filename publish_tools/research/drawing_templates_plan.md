# Drawing templates for the publish features -- plan (2026-09-29)

Status: proposal for the user to build in Onshape. [V] = verified, [U] = not verified yet.

## 1. Worth it? Yes
- The generated features (Station geometry, Station table, Export primitive, Primitive tables, the coming Section
  geometry) put views and tables in PREDICTABLE places only if the sheet has fixed zones. The future drawing builder
  (API) needs the same fixed zones.
- Today's templates waste space and carry dead fields (see section 7): a half-width revision table, blank
  MATERIAL / WEIGHT / DWG NO / FINISH cells, and title text copied from old drawings. [V]
- Primitive and sections drawings need sheet sizes and layouts that don't exist yet (large sheet + A4 set).

## 2. Where they live: NOT with the FeatureScript
Keep them in the existing **"Ski Drawing Templates"** document (52b5bde0...), one drawing tab per template.
- FeatureScript documents get versioned constantly for code; templates change rarely and deliberately.
- Drawings are created from a template document (in the "Create a drawing" dialog you browse to it), so a stable,
  versioned template document is the clean reference -- also for the drawing builder.
- A template does not need to be next to the Part Studio: views and custom tables bind to the Part Studio you pick
  when inserting them.
- Make each template a drawing tab (the editable source); export a .dwt from it only if someone needs the file
  (export keeps only the first two sheets and strips views). [V: Onshape help, custom drawing templates]
- Clean up the K2 template tab first: it still has a sheet reference to doc 8f79891d ("DERIVED TEST") and two
  views. LINE and MADSHUS are clean. [V]
- [U] Whether K2's plan lets an admin pin company-wide templates: the help pages don't describe it. Sharing the
  template document with the company works either way.

## 3. The template set (one master layout, three logo variants: K2 / LINE / MADSHUS)
| Template | Sheet | Scale | Use |
|---|---|---|---|
| PART A3 | A3 landscape 420 x 297 | 1:5 (ski ~1.8 m -> 360 mm) | production part drawings (Station geometry + Station table) |
| PART A4 | A4 landscape 297 x 210 | 1:10 | small parts / quick prints (today's format, cleaned) |
| PRIMITIVE A0 | A0 landscape 1189 x 841 | 1:2.5 (1.9 m -> 760 mm) | everything on one sheet |
| PRIMITIVE A4 set | 4 x A4 landscape | 1:7.5 | printable set |
| SECTIONS A3 | A3 landscape, sheet 1 + one sheet per station | sheet 1 1:5, stations 1:1 | X-sect drawings |
Custom (non-ISO) sheet sizes, e.g. a long 2000 x 841 "ski sheet": [U] -- check in the Custom template dialog; if
available it beats A0 for the primitive.

## 4. Sheet geometry (ISO 5457: frame 20 mm left, 10 mm other sides, frame line 0.7 mm) [V]
Coordinates in mm from the sheet's lower-left corner.

### PART A3 (420 x 297; drawing space x 20-410, y 10-287)
```
y 287 +------------------------------------------------------------------------------+
      |  VIEW ZONE  x 20-410, y 150-287  (plan view full width, 1:5)                  |
      |  + optional profile/thickness view in the same zone                           |
y 150 +--------------------------------------------+---------------------------------+
      |  TABLE ZONE  x 20-225, y 55-145             |  REVISIONS  x 230-410, y 95-145 |
      |  (Station table, 3.5 mm text)               |  (header + 4 rows, 8 mm)        |
y 55  +--------------------------------------------+---------------------------------+
      |  NOTES  x 20-225, y 10-50                   |  TITLE BLOCK  x 230-410, y 10-50 |
y 10  +--------------------------------------------+---------------------------------+
      x 20                                          x 230                           x 410
```

### PART A4 (297 x 210; space x 20-287, y 10-200)
View x 20-287, y 110-200 (1:10). Table x 20-157, y 45-105. Revisions x 162-287, y 45-105 (compact).
Title block x 107-287, y 10-42 (180 x 32). Notes x 20-102, y 10-42.

### PRIMITIVE A0 (1189 x 841; space x 20-1179, y 10-831)
Bands zone x 20-800, y 70-831 (the EXPORT PRIMITIVE composite, 1:2.5; the bands stack about 1100 mm of model
height -> about 440 mm on paper, so there is room). Tables column x 810-1179, y 70-831: Table 2 (Metadata), 3 (Key
locations), 5 (Baseline), 1 (Scale factors), 6 (Data), top to bottom. Title block x 999-1179, y 10-60 (180 x 50);
notes x 20-990, y 10-60.

### PRIMITIVE A4 set (each 297 x 210)
| Sheet | Content |
|---|---|
| 1 | EI + baseline + profile bands (1:7.5), Table 5 Baseline, Table 2 Metadata |
| 2 | footprint + radius/curvature bands (1:7.5), Table 3 Key locations |
| 3 | Table 1 Scale factors (+ Table 4 SW rout later) |
| 4 | Table 6 Data (RSL) |

### SECTIONS A3
Sheet 1: plan + profile full width (1:5) in the view zone, the summary table in the table zone (RSL, SW rout specs,
estimated weight / stiffness / deflection, total reinforcement thickness, exposed layers, min SW left, ISO min
locations). Sheets 2+: one station each, 1:1 section view x 20-300, that station's table x 305-410, y 55-287.

## 5. Title block (ISO 7200: bottom-right, max 180 mm wide) [V]
Proposed 180 x 40 (A0: 180 x 50), two identification rows + one description row. Drop FINISH, DWG NO. (part numbers
are blank today) and the four unlabelled rows under DRAWN.
| Field | Source |
|---|---|
| Logo + "SKI ENGINEERING" | image + text (brand variant) |
| Title | sheet-reference part NAME (property link) |
| Description line | part DESCRIPTION (instead of today's "PART" / "ASSEMBLY") |
| Material | sheet-reference MATERIAL (property link) |
| Weight | sheet-reference MASS, 1 decimal, g (property link) |
| Scale / Sheet n of N / Size | drawing properties (already linked) |
| Drawn by / Date drawn | drawing properties (Copy Document now resets them) |
| Rev. | PART revision (sheet reference), the revision table keeps the drawing's own history |
| Units + projection symbol | static text "mm" + first-angle symbol |
Property links: insert them in the note editor as properties of the drawing / the SHEET REFERENCE [U: exact
menu wording]. For composites (Station geometry / Export primitive output) the sheet reference must be the REAL
part, not the composite, or MATERIAL / MASS come out blank (composites have no material). [U: whether a sheet
reference can be a part that only appears inside a composite view -- test first]

## 6. Settings to enter in each template (Drawing properties)
| Setting | Value |
|---|---|
| Standard | ISO |
| Units | millimeter; decimal separator period (or comma for the German consumers -- decide once) |
| Projection | first angle (ISO) -- [U] confirm what K2 drawings use today |
| Linear dimension precision | 1 decimal (station widths 0.1 mm); thicknesses override to 2 where needed; trailing zeros OFF |
| Angular precision | 1 decimal degree |
| Text heights (ISO 3098) | 3.5 mm dimensions / notes / tables, 5 mm view labels, 7 mm title |
| Line widths (ISO 128) | visible 0.5, thin 0.25, frame 0.7, title block 0.5 |
| Format layers | Border Frame / Title Block LOCKED (Drawing properties -> Formats tab) [V: Onshape tech tip] |
| Section / detail labels | "A-A" style, scale under the label (matches today's EDA - EDA / 1:2) |
| Callout formats | set once, place one callout and delete it so the format saves [V: Onshape tech tip] |
| Default Drawn by | EMPTY in the template (so nothing is copied) |
| Views of our composites | turn "include wires" ON when creating the view -- per view, not a template setting [V] |
Colours don't matter: drawing views render wires black. [V: 2026-09-28 PDF test]

## 7. Current state (for comparison)
- Current K2 template: `img/templates/current_K2_template.png`
- Current 4101 part drawing (A4 1:10): `img/templates/current_4101_part_drawing.png`
- Current X-sect sheet 1: `img/templates/current_xsect_sheet1.png`
- Export primitive bands (front view, Part Studio): `img/templates/primitive_bands_front.png`

## 8. Reference pictures online
- ISO 5457 sheet layout, with figures of the frame, margins and centring marks:
  https://cdn.standards.iteh.ai/samples/29017/e46c0ec5d98f470aab82dae76889f229/ISO-5457-1999.pdf
- ISO 7200 title block example (figure in the sample pages):
  https://cdn.standards.iteh.ai/samples/35446/d3b0887cb4fa47f49f8718807d3b8903/ISO-7200-2004.pdf
- Title block and revision box explained, with example images: https://www.roymech.co.uk/Useful_Tables/Drawing/Title_blocks.html
- Title block placement by sheet size: https://clickpost.co.uk/guides/drawing-title-blocks
- ISO 7200 overview: https://en.wikipedia.org/wiki/ISO_7200
- Onshape custom drawing templates (dialog options, DWT export): https://cad.onshape.com/help/Content/Drawing/custom_drawing_templates.htm
- Onshape tech tip, improving templates (format layers, callout formats, update properties from template):
  https://www.onshape.com/en/resource-center/tech-tips/tech-tip-how-to-improve-your-drawing-templates-in-onshape
- Onshape tech tip, switching templates on existing drawings:
  https://www.onshape.com/en/resource-center/tech-tips/tech-tip-switching-onshape-drawing-templates

## 9. Suggested order
1. Clean the K2 template tab; build PART A3 (K2) and test on 4101 with Station geometry + Station table: the
   property links, and whether the sheet reference can be the real part while the view shows `4101 PLAN`.
2. Copy to LINE / MADSHUS (logo swap). Version the template document.
3. PRIMITIVE A0 + A4 set once Export primitive's tables and bands settle (after pass 5).
4. SECTIONS A3 when Section geometry exists.
5. Then the drawing builder can target these zones.

## Open questions
- Decimal separator for the German consumers: period or comma?
- First or third angle? (Assume first angle / ISO.)
- Is a custom long sheet available (section 3)? If yes: primitive on one ~2000 x 841 sheet at 1:1?
