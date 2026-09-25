# Ski Material Library Audit (K2/LINE Ski Material Library, 136 rows)

**Scope:** I checked the actual numbers in every row of `scratchpad/kaitai_materials.csv`, not just the file layout. Everything was read-only; no repo files were changed. My scratch scripts are `mat_audit.py`, `mat_prop.py`, `section.py` and `csvout.py` in the scratchpad.
- **Checked (V):** rows parsed and the arithmetic run in Python. Wood ratios were taken from the Wood Handbook (FPL-GTR-190, ch. 5, Tables 5-1, 5-2 and 5-3a), downloaded and text-extracted.
- **Inferred (I):** fibre fractions, micromechanics, and values for European beech, bamboo, paulownia and BCore, which are not in the Handbook.

## 1. Summary

1. **The biggest error is in the GJ code, not the data.** `xSection/section/xSect_GJ.fs:231-236` uses `G = (Q11-Q12)/2` whenever `Q11/Q22 < 3`. That is only right for materials that are isotropic in-plane. A 0/90 fabric and a ±45 fabric both pass the `<3` test, and for them the result is badly wrong (V):
   - **0/90 fabrics, G too high by 5.7–13x:** all HongTex E-LT-xxx, K2-20, K2-22, ROVINAP 42/24, G-TAPE 6073FT, TWILL-3K.
   - **±45 fabrics, G too low by 2.7–12.5x:** C-PLY TBX270, DIAGOTEX D988C, BIMAX 374, DX FT 13.04, GC-EV BB566.
   - **Mixed fabrics, G too high by 1.3–1.9x:** DTF 17.38, DTF 14VC, G-PLY TL360, E-DBL-680, -800, -950.
   - `Q66` is already the correct in-plane shear stiffness in ski axes for every row. **Recommendation: always use Q66.**
2. **Every wood, WBK composite core, bamboo and BCore row is a placeholder that treats the material as isotropic** (E, ν = 0.33, so Q22 = Q11 and Q66 = E/2.66). Real wood has transverse stiffness of 5–12% of E_L and shear stiffness of 5–10% of E_L. As a result:
   - Q66 is 3.6–7.4x too high.
   - Q22 is 8–20x too high.
   - Q11 is 12% too high relative to the E column.
3. **Other placeholders or wrong numbers:**
   - 9 oz and 17 oz Uni: stored as isotropic, and the 17 oz density (2956 kg/m³) is impossible because it is higher than E-glass fibre itself (~2560).
   - Hexcell G-R80 / G-EV R68 / 765R / R82: stored as isotropic (exactly 5.9 Msi, ν 0.33).
   - Both Braid rows are off by exactly 1000x (entered as GPa but the numbers are MPa).
   - Amplitex carbon/flax hybrid: stored as isotropic.
4. **EI basis:** on a representative section, switching from Q11 to E lowers EI by 5–12%. A full laminate calculation (condensed ABD) agrees with the E basis to within 1%, so the planned move to the E column is correct (the ski behaves as a narrow beam, not a plate).
5. **GJ on the representative section** (Q66 plus the corrected aspen core, compared with today's rule):
   - With triax skins: −21% (−14% with Titanal).
   - With 0/90 biax skins: −71% (−56% with Titanal).
   - The core-shear placeholder alone accounts for about 100 N·m² of a roughly 500 N·m² section.

## 2. Flagged rows

(V) means computed from the CSV; (I) means inferred. Severity is judged against the representative section in §4.

| Rows | Problem | Evidence | Severity |
|---|---|---|---|
| **GJ rule (code)**, all 0/90 and ±45 fabrics | Wrong G formula | E-LT-600: rule 15.80 vs Q66 2.60 (6.1x). TWILL-3K: 33.87 vs 2.60 (13x). C-PLY TBX270: 2.57 vs 33.49 (0.08x). DX FT 13.04: 2.53 vs 6.80. DTF 17.38: 9.72 vs 5.25. (V) | **HIGH** (GJ −52% on a biax ski) |
| Wood ×12, WBK ×9, Bamboo, BCore 300 XL | Isotropic placeholder | Aspen Q66 3.665 vs Handbook-based 0.624 (5.9x). Q22 10.94 vs 0.84 (13x). Q11/E = 1.122. (V) | **HIGH** for GJ (core share 110→19–33 N·m²), LOW for EI (−1.3 to −2.2% on Q11 basis, 0% on E basis) |
| WBK N12 (13.44 GPa), N5 / N8 / N14 (all 11.549) | E is higher than any of the woods they are made of | N12 is stiffer than maple (12.62) and aspen (9.31). N5/N8 (fir/aspen) are stiffer than fir 10.24 and aspen 9.31. N14 (aspen/paulownia, 416 kg/m³) mixes 9.31 and 5.03, so it should be about 7.2. Three rows share exactly 1.675 Msi, which looks like a copy. N2 = N3 as well. (V/I) | **MED–HIGH**: the core carries about 18% of EI, so N14 would be ~−7% EI if the value should be ~7.2 |
| HongTex 9 oz Uni, 17 oz Uni | Isotropic placeholder; impossible density | Q22 = Q11 = 45.8, Q66 16.5, where 13 oz Uni has Q22 7.03 and Q66 2.63. Density 2290 / **2956** implies fibre fraction 0.81 / 1.28. (V) | **HIGH** where used (Q66 6.3x too high, mass +16% / +50%) |
| Hexcell G-R80, G-EV R68, G-EV 765R, G-EV R82 | Isotropic placeholder | Stored as E 40.679, ν 0.33, Q66 15.29. If they are UD glass (Vf ≈ 0.59 from density): E_y ≈ 12.9, G ≈ 3.8 (I) | **HIGH** if used as skins or stringers (Q66 4x too high); construction needs confirming |
| Glass - Braid, Glass - Rovings in braid | 1000x unit error | Braid E 0.00965 → 9.65 GPa, which matches a ±45 glass braid (my CLT estimate is 9.88). Rovings are exactly the G-R80 values ×1e-3. (V) | **HIGH** if used (they currently add almost no stiffness) |
| BComp Amplitex 115gsm-C 125gsm-F | Isotropic placeholder | Q11 = Q22 = 54.2, Q66 18.1, ν 0.33. From areal weights and t = 0.45: V_carbon 0.14, V_flax 0.19. A 0/90 hybrid gives E_x ≈ 36, E_y ≈ 13, G ≈ 1.7 (I) | MED (Q66 about 11x too high) |
| HongTex E-DBL-670-30M | Looks different from the rest of its family | Q22 7.1, Q66 2.8, ν 0.31, which looks like UD, while DBL-680 and DBL-800 look like ±30 (Q66 6.3) (V) | MED; check against the vendor datasheet |
| BASF Modipur injection foam | E probably far too low | 20 MPa at 600 kg/m³. Rigid PU at that density: 1.6 GPa × (0.6/1.2)² ≈ 0.4 GPa (Gibson-Ashby) (I) | MED if used as a core or stringer (10.5 × 7 mm) |
| Socrep TPU 511/519 ("Sidewall") | E probably far too low | 5 MPa and ν 0.499 is rubber-like. Sidewall TPU of Shore 55–65D is 150–400 MPa (I) | LOW–MED (sidewalls are a few % of EI) |
| Watom Duraclear 117/111, Watom 210/211 | E probably low | 0.1 GPa. Isosport ICP topsheets are 1.2; PA/TPU topsheets are usually 0.3–1.5 (I) | LOW (about 1% of EI) |
| ITOCHU HS40 rovings (both rows) | E may be low | 140 GPa is a standard-modulus UD value. If "HS40" is a 455 GPa fibre, UD E_x ≈ 250 (I, unverified) | MED if used as a carbon stringer |
| HongTex TWILL-3K | Values copied from another row | Q12 and Q66 are exactly those of A&P UNI-4.0-SM. E 69.3 is high for a 3K twill (typically 55–65) (V/I) | LOW |
| All UD and 0/90 glass/carbon rows | G12 on the low side | Library G12 is 1.8–2.7 GPa. Halpin-Tsai with G_m 1.1 GPa gives 3.0 (Vf 0.50) to 3.8 (Vf 0.59) (I) | LOW–MED: once GJ uses Q66, GJ with 0/90 skins may be 20–40% conservative. This is the largest remaining GJ uncertainty |
| E-LT-660, 750, 830, 980, E-DBL-950, 1150 | "Available dimensions" thickness too thin for the areal weight | Fibre fraction from gsm / (2560·t): 0.95, 0.79, 0.79, 0.85, 1.69, 0.96. Also 9, 13 and 17 oz are 0.22–0.28 (V) | LOW for stiffness; matters if stack thickness is taken from this field |
| Owens Corning M723 450 / 600 gsm | Density too high | 1994 kg/m³, but chopped-strand-mat laminates at Vf 0.2–0.35 are about 1400–1650 (I) | LOW (affects mass only) |
| IsoCore 150/250 vs Armacell GRX 150/250; Nico / XuRui fleece rows | Identical values across different products | (V) | LOW (placeholders) |
| Wood E values | Handbook E comes from bending tests | FPL Table 5-1 footnote: true E_L ≈ 1.10 × the tabulated value. On the E basis the core would be ~10% under, about −1.8% of section EI (V) | LOW; optionally multiply by 1.10 |
| Plausible as stored | — | Steel 207 / ν 0.29, Titanal 71.7, P-Tex 0.7–0.9 / ν 0.46, ABS 2.3, the Diagonap and Ruban rows, UD carbon tapes, re-Evo (random fibre, so isotropic is correct), Amplitex 5057 (E 35 at Vf ≈ 0.63) | OK |
| Local `xSection/materialData.csv` | Out of date | It is missing the 3 DIAGONAP DTVC rows, and its Volumat and G-EV 696R values differ from Onshape (V) | Note only |

## 3. Proposed theoretical values

**Method for wood** (Wood Handbook FPL-GTR-190, Tables 5-1 and 5-2, 12% moisture). A vertically laminated core has its width direction spread between radial (R) and tangential (T), so:
- E_x = E column (kept as stored)
- E_y = E_x · mean(E_T/E_L, E_R/E_L)
- G_xy = E_x · mean(G_LR/E_L, G_LT/E_L)
- ν_xy = mean(μ_LR, μ_LT)

Then Q is built from those four constants:
- ν_yx = ν_xy · E_y / E_x
- Q11 = E_x / (1 − ν_xy ν_yx), Q22 = E_y / (1 − ν_xy ν_yx), Q12 = ν_xy · Q22, Q66 = G_xy

This keeps E = Q11 − Q12²/Q22 exactly.

**Species mapping:**
- **Ash:** white ash (0.080 / 0.125 / 0.109 / 0.077; μ 0.371 / 0.440).
- **Aspen:** E ratios by reciprocity from Table 5-2 (μ_TL/μ_LT = 0.022/0.374, μ_RL/μ_LR = 0.054/0.489). G is taken from eastern cottonwood (same genus, 0.076 / 0.052); aspen is not in Table 5-1.
- **Poplar ×3:** eastern cottonwood (Populus), not yellow-poplar.
- **Hard maple:** sugar maple. **Soft maple:** red maple. **Birch:** yellow birch. **Fir:** subalpine fir.
- **Paulownia, WBK "Pal" and BCore:** basswood ratios as a low-density proxy (I).
- **European beech:** about 0.08 / 0.16 / 0.115 / 0.08, ν 0.48 (Hering et al. 2012, approximate) (I).
- **Bamboo:** 0.08 / 0.08, ν 0.30, laminated-bamboo literature (I).
- **WBK mixes:** blended by assumed volume fractions: stringer 1x = 85/15, 2x = 70/30, alternating = 50/50, Pal/Map = 80/20 (I).
- The WBK E values are kept as stored even though they are suspect (see §2).

**Other rows:**
- **9 oz / 17 oz Uni:** copied from 13 oz Uni (same product line, and E is almost the same: 41.4 vs 42.2).
- **G-R80 family and Rovings in braid:** UD glass. Vf 0.59 from density 1980 = (ρ − 1150)/(2560 − 1150). Halpin-Tsai with E_f 73, E_m 3.0, G_f 30, G_m 1.1, ξ = 2 / 1 gives E_y 12.86 and G 3.77. ν = 0.59·0.22 + 0.41·0.35 = 0.273. E_x is kept at 40.679.
- **Glass - Braid:** ±45 lamination of UD glass at Vf 0.50 (38.0 / 10.16 / 0.285 / 3.01), scaled to E_x = 9.653.
- **Amplitex C/F:** carbon assumed in the 0° direction and flax at 90°, each at its local Vf.

**Paste instructions:** paste over columns A–K (Category through Q26). CTE_x, CTE_y and Available dimensions stay as they are.

```
Category,Name,Density [kg/m^3],Poisson's Ratio,Young's Modulus [GPa],Q11 [GPa],Q22 [GPa],Q12 [GPa],Q66 [GPa],Q16 [GPa],Q26 [GPa]
Core - Wood,Ash,680,0.4055,12.31,12.521,1.2834,0.52042,1.1448,0,0
Core - Wood,Aspen,450,0.4315,9.75,9.9061,0.83832,0.36173,0.624,0,0
Core - Wood,Paulownia,280,0.385,5.6,5.6389,0.26221,0.10095,0.2856,0,0
Core - Wood,Hard Maple,705,0.45,12.62,12.877,1.2684,0.57077,1.0979,0,0
Core - Wood,Soft Maple,550,0.4715,11.3,11.566,1.1971,0.56443,1.1696,0,0
Core - Wood,European Beech,710,0.48,14.31,14.717,1.766,0.84769,1.3952,0,0
Core - Wood,Birch,740,0.4385,13.86,14.033,0.89809,0.39381,0.98406,0,0
Core - Wood,Bamboo,1150,0.3,12.6,12.691,1.0153,0.30459,1.008,0,0
Core - Wood,Fir,415,0.3365,10.24,10.322,0.72773,0.24488,0.65536,0,0
Core - Wood,Poplar - Light,370,0.382,7.59,7.6627,0.49807,0.19026,0.48576,0,0
Core - Wood,Poplar,412.5,0.382,9.245,9.3335,0.60668,0.23175,0.59168,0,0
Core - Wood,Poplar - Dense,455,0.382,10.9,11.004,0.71528,0.27324,0.6976,0,0
Core - Composite,WBK # N21 - Offset Stringer (1x): Pal/Map,421.88,0.398,6.8948,6.9575,0.39588,0.15756,0.40128,0,0
Core - Composite,WBK # N12 - Offset Stringer (2x): Asp/Map,604.4,0.437,13.445,13.677,1.2143,0.53073,0.95323,0,0
Core - Composite,WBK # N3 - Solid: Asp Veneer,570.3,0.4315,9.3079,9.4569,0.80031,0.34533,0.59571,0,0
Core - Composite,WBK # N5 - Alternating: Fir/Asp,531.63,0.384,11.549,11.682,0.90612,0.34795,0.73912,0,0
Core - Composite,WBK # N8 - Double Barrel: Fir/Asp,532.78,0.384,11.549,11.682,0.90612,0.34795,0.73912,0,0
Core - Composite,WBK # N2 - Solid: Asp,498.79,0.4315,9.3079,9.4569,0.80031,0.34533,0.59571,0,0
Core - Composite,WBK # N4 - Solid: Pal,321,0.385,5.0332,5.0681,0.23567,0.090732,0.25669,0,0
Core - Composite,WBK # N13 - Offset Stringers (1x): Asp/Map,546.51,0.4343,10.859,11.04,0.95723,0.4157,0.73246,0,0
Core - Composite,WBK # N14 - Alternating Asp/Pal,416.39,0.4083,11.549,11.676,0.76554,0.31253,0.66405,0,0
Wood,BComp: BCore 300 XL,300,0.385,6.2,6.243,0.2903,0.11177,0.3162,0,0
Fiberglass,HongTex: 9 oz. Uni,1974.61,0.282038,41.4198,41.979,7.02982,1.98268,2.62701,0,0
Fiberglass,HongTex: 17 oz. Uni,1974.61,0.282038,41.4198,41.979,7.02982,1.98268,2.62701,0,0
Precure laminate,Hexcell: G-R80 Laminate,1980,0.2733,40.679,41.663,13.17,3.5993,3.77,0,0
Precure laminate,Hexcell: G-EV R68,1980,0.2733,40.679,41.663,13.17,3.5993,3.77,0,0
Precure laminate,Hexcell: G-EV 765R,1980,0.2733,40.679,41.663,13.17,3.5993,3.77,0,0
Precure laminate,Hexcell: G-EV R82,1980,0.2733,40.679,41.663,13.17,3.5993,3.77,0,0
Braid,Glass - Braid,1980,0.6416,9.6527,16.407,16.407,10.527,10.576,0,0
Braid,Glass - Rovings in braid,1980,0.2733,40.679,41.663,13.17,3.5993,3.77,0,0
Carbon/Flax,BComp: Amplitex 115gsm-C 125gsm-F - 52mm,1450,0.1034,36.049,36.189,13.108,1.356,1.655,0,0
```

**Alternatives if you want consistency with the rest of the library:**
- For the G-R80 family, the library's own UD glass G12 convention is about 2.6 instead of 3.77, which gives Q66 = 2.6.
- For the Amplitex row, if carbon and flax run in both directions, use Q11 = Q22 = 24.65, Q12 1.356, Q66 1.655, E 24.57.

**Not proposed yet (need your input first):** WBK E values, E-DBL-670-30M, Modipur, Socrep TPU, HS40.

## 4. Impact on a representative section

**Section** (100 mm wide, z measured from the base in mm):
- P-Tex 4504 base, 0–1.5, 96 mm wide
- Steel edges 2 × (2 × 2), 0–2.0
- Skin 2.0–3.0
- Core 3.0–13.0: aspen 90 mm wide plus ABS sidewalls 2 × 5 mm
- Optional Titanal 0.4 mm
- Skin, 1.0 mm
- ICP 5275 topsheet, 0.6 mm

**Formulas:**
- EI (transformed section): z_NA = Σ M_k w_k (z_t² − z_b²)/2 ÷ Σ M_k w_k t_k, then EI = Σ M_k w_k [(z_t − z_NA)³ − (z_b − z_NA)³]/3, with M = Q11 or E.
- Exact beam EI: smeared ABD per unit width, invert the 6×6, then EI = b / d11. This models free edges: N_y = N_xy = M_y = M_xy = 0.
- GJ: 4 Σ G_k w_k [(z_t − c)³ − (z_b − c)³]/3 about the G-weighted centroid c. This is the same as the code's `4·Σ G·Iz` at `xSect_GJ.fs:283-290`.
- Units: 1 GPa·mm⁴ = 10⁻³ N·m².

**EI (N·m²):**

| Skins | Ti | Q11 basis (current) | E basis | Exact ABD | Q11 basis, new aspen |
|---|---|---|---|---|---|
| Triax DTVC 15.50.3 | no | 464.4 (z_NA 7.38) | 409.2 (−11.9%) | 413.5 (−11.0%) | 456.3 (−1.8%) |
| Triax DTVC 15.50.3 | 0.4 | 580.6 | 512.6 (−11.7%) | 518.3 (−10.7%) | 572.8 (−1.3%) |
| Biax E-LT-600 | no | 379.7 | 360.1 (−5.2%) | 362.5 (−4.5%) | 371.4 (−2.2%) |
| Biax E-LT-600 | 0.4 | 490.8 | 460.7 (−6.1%) | 463.6 (−5.6%) | 483.0 (−1.6%) |

Why the triax case drops more: the Q11/E ratio is 1.147 for triax, 1.122 for wood and Titanal, and 1.268 for PE base. The corrected core does not change EI on the E basis.

**GJ (N·m²):**

| Skins | Ti | Current rule | Always Q66 | Q66 + new aspen |
|---|---|---|---|---|
| Triax | no | 494.6 | 494.6 (0%; ratio 3.22 > 3, so the rule already picks Q66) | **392.3 (−20.7%)** |
| Triax | 0.4 | 643.9 | 643.9 | 552.0 (−14.3%) |
| Biax E-LT-600 | no | 624.3 | 297.2 (−52%) | **178.6 (−71%)** |
| Biax E-LT-600 | 0.4 | 781.0 | 437.1 (−44%) | 345.1 (−56%) |

**Worked check for the core term:** 4 · G · 90 · 10³ / 12 gives 4 · 3.665 · 7500 ≈ 110 N·m² today, versus 4 · 0.624 · 7500 ≈ 18.7 N·m² corrected.

**Biax skin term:** falls from 389 N·m² (rule G 15.8) to 76 N·m² (Q66 2.6).

**Current behaviour inverts reality:** the rule makes a 0/90 biax ski stiffer in torsion than a triax ski (624 vs 495 N·m²). Physically it is the other way round (179 vs 392 N·m²).

**Note on the thin-plate formula:** it smears the steel edges across the full width (83–140 N·m²). The FEM path handles them more realistically. That is a solver issue, not a data issue.

## 5. Open questions

1. **WBK cores:** where do the E values come from (measured, or vendor Msi figures)? Why are N12 and N5/N8/N14 stiffer than the woods they are made of? What are the stringer width fractions?
2. **Hexcell G-R80, R68, 765R, R82:** UD, 0/90, or multiaxial? Where are they used (skins, stringers, sidewall plates)?
3. **Glass braid:** confirm the braid angle (±45 assumed; my CLT result of 9.88 GPa agrees with the stored 9.65 after the 1000x fix). Confirm that "Rovings in braid" means axial UD rovings.
4. **Amplitex C/F:** the fibre directions for carbon and flax.
5. **HS40 roving:** fibre grade and modulus (230 or 455 GPa).
6. **Modipur and Socrep TPU 511/519:** rigid or elastomeric, and what are they used for?
7. **Library-wide UD / 0-90 G12 of about 2.6 GPa:** keep it (it implies Vf around 0.45) or raise it to the micromechanics 3.0–3.8? Without measured shear data, one torsion test on a biax plate strip would settle it.
8. **Wood E:** apply the FPL ×1.10 correction for shear deflection in bending tests? On the E basis it adds about +1.8% EI.
9. **Density fixes:** 9 oz / 17 oz Uni and OC M723 affect mass only. Also, which thickness does geometry use: the dims field or CAD?

**Code notes** (these belong to the xSection plan, not to data edits):
- `xSect_GJ.fs:231-236`: replace the ratio rule with Q66.
- `xSectCLT.fs:394-430`: the E basis is fine. As an optional extra, invert the already-assembled 6×6 [A B; B D] and use EI = 1/d11 (within 1% here, but better for ±45-heavy stacks).

## Critical Files for Implementation
- C:/Users/JED~1.YEI/AppData/Local/Temp/claude/C--Users-jed-yeiser-documents-featurescripts/5d5ebd87-0ec8-4aa9-b813-a5d2c57d5e68/scratchpad/kaitai_materials.csv
- C:/Users/jed.yeiser/documents/featurescripts/xSection/section/xSect_GJ.fs (lines 231-236)
- C:/Users/jed.yeiser/documents/featurescripts/xSection/materials/xSectCLT.fs (lines 390-430)
- C:/Users/jed.yeiser/documents/featurescripts/xSection/materials/xSectMaterials.fs (lines 107-160, CSV → Q matrix)
- C:/Users/jed.yeiser/documents/featurescripts/xSection/materialData.csv (out of date)

Source: [Wood Handbook ch. 5, FPL-GTR-190](https://www.fpl.fs.usda.gov/documnts/fplgtr/fplgtr190/chapter_05.pdf)
