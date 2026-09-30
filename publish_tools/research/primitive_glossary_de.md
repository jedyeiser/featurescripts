# Export primitive -- German terms (PROPOSAL for review, 2026-09-30)

Primitives go to the factory, so tables and band labels get an English / Deutsch switch on the **Export primitive
feature** (not the table -- correction 64). Please mark each row: OK / better term / keep English.
Confidence: **H** = standard, **M** = likely, **L** = guess -- please check with a Völkl engineer.
Umlauts are fine in the output (code uses escapes).

## Band labels (composite text geometry)
| English | Deutsch (proposal) | Conf. | Note |
|---|---|---|---|
| BASELINE | BASISLINIE | L | or VORSPANNUNG (camber/rocker line)? |
| PROFILE | PROFIL | H | side profile / thickness |
| FOOTPRINT | TAILLIERUNG | M | or GRUNDRISS (plan shape) |
| RADIUS (m) | RADIUS (m) | H | |
| CURVATURE (1/m) | KRÜMMUNG (1/m) | H | |
| EI (Nm²) | EI (Nm²) | H | unchanged |

## Table titles
| English | Deutsch (proposal) | Conf. | Note |
|---|---|---|---|
| 1 Theoretical scale factors | 1 Theoretische Skalierungsfaktoren | M | |
| 2 Metadata | 2 Kenndaten | M | "Metadaten" reads as IT jargon |
| 3 Key locations | 3 Kennpunkte | M | |
| 4 SW rout | 4 Seitenwangenfräsung | M | SW = Seitenwange |
| 5 Baseline | 5 Basislinie | L | match the band label |
| 6 RSL data (mm) | 6 Auflagedaten (mm) | L | RSL = Auflagelänge? see below |

## Column headers
| English | Deutsch (proposal) | Conf. |
|---|---|---|
| Region | Bereich | H |
| Bottom (mm) / Top (mm) | Unterseite (mm) / Oberseite (mm) | H |
| Top / Bottom (%) | Ober- / Unterseite (%) | H |
| Item / Value / Unit | Größe / Wert / Einheit | H |
| Definition | Definition | H |
| Location | Position | H |
| Measure | Maß | H |
| Value (mm) | Wert (mm) | H |
| x (mm), s (mm) | x (mm), s (mm) | H (unchanged) |
| Dist. from tail (mm) | Abstand vom Ende (mm) | M |
| # | Nr. | H |
| RSL data: x, s, y, z | unchanged | H |
| RSL data: w | b (Breite) | M |
| RSL data: thck | d (Dicke) | M |
| RSL data: baseline | Basislinie | L |
| RSL data: radius (m) | Radius (m) | H |

## Row names
| Table | English | Deutsch (proposal) | Conf. | Note |
|---|---|---|---|---|
| 1 | Tip length | Schaufellänge | M | tip region = Schaufel |
| 1 | Running surface length | Auflagelänge | L | or Lauflänge / effektive Kantenlänge? |
| 1 | Tail length | Endenlänge | L | |
| 2 | RSL | Auflagelänge (RSL) | L | keep "RSL" as the abbreviation? |
| 2 | Dimensions (L x W x H) | Abmessungen (L x B x H) | H | |
| 2 | Widths (FB widest - waist - AB widest) | Breiten (Schaufel - Taille - Ende) | M | |
| 2 | Average radius | Mittlerer Radius | H | |
| 2 | Natural radius widest | Natürlicher Radius (breiteste Stellen) | M | |
| 2 | Natural radius inflection | Natürlicher Radius (Wendepunkte) | M | |
| 2 | Taper angle widest | Taperwinkel (breiteste Stellen) | M | "Taper" is used in German ski jargon |
| 2 | Taper angle inflection | Taperwinkel (Wendepunkte) | M | |
| 2 | Theoretical deflection | Theoretische Durchbiegung | H | unit mm/30 kg |
| 2 | Theoretical stiffness | Theoretische Steifigkeit | H | unit lb/in (or N/mm for the factory?) |
| 3 | FCP / ACP | SPA / EDA | M | the factory's own abbreviations on existing drawings |
| 3 | MRS | MRS | L | German abbreviation? (Mitte Auflage?) |
| 3 | MP | MP | H | Montagepunkt |
| 3 | XS1 / XS2 | XS1 / XS2 | M | |
| 3 | TIP / TAIL | SPITZE / ENDE | H | as the Station table |
| 4 | SW rout angle | Fräswinkel Seitenwange | M | |
| 4 | Step-in | Einzug | L | or Absatz / Stufe? |
| 4 | Dist. above base | Höhe über Belag | M | base = Belag |
| 4 | Start / Stop | Beginn / Ende der Fräsung | M | avoid plain "Ende" (= tail) |
| 5 | Tip block / Tail block | Schaufelblock / Endenblock | L | the aluminium tooling blocks |
| 5 | FCPh, FRCP, FRCPl, FB_Roll, MCh, MCl, AB_Roll, ARCPl, ARCP, ACPh | unchanged | M | codes stay; German only in the Definition column |
| units | deg | ° | H | |

## Definitions column (only shown with "Show definitions")
Proposal: translate as well (full sentences, ~20 rows) -- lower priority; can ship with English definitions first.

## Open questions for the reviewer
1. BASELINE: Basislinie, Vorspannung(slinie) or something else?
2. RSL / running surface length: Auflagelänge, Lauflänge, or keep "RSL"?
3. FCP / ACP shown as SPA / EDA in German tables -- yes?
4. MRS: is there a German abbreviation in use?
5. Stiffness unit for the factory: keep lb/in or add N/mm?
6. Step-in: Einzug, Absatz or Stufe?

## DECISIONS (user, 2026-09-30)
- Baseline -> **Buglinie** (band label BUGLINIE, table "5 Buglinie", RSL-data column "Buglinie").
- RSL / running surface -> **Laufsole**: table "6 Laufsolendaten (mm)", row "Laufsolenlänge (RSL)", scale-factor row
  "Laufsolenlänge".
- Step-in -> **Offset**.
- Keep BOTH theoretical stiffness (lb/in) and deflection (mm/30 kg) -- different readers need each.
- Translate the Definitions column too.
- Not answered -> use the proposals above (FCP/ACP as SPA/EDA, MRS unchanged, rest as proposed); flagged for review.
