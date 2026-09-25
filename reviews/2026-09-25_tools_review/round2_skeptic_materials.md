## Review of the material-data audit (skeptical pass)

I re-parsed all 136 rows, re-ran `section.py`, and ran my own checks in Python (from stdin; no files written). Verified (V) means recomputed or read from code. Inferred (I) means it comes from literature or memory. **Overall: the main conclusions hold.** The GJ rule is wrong, the wood rows are isotropic placeholders, and the E basis is right for EI. Six points need correcting, and the audit's GJ impact figure understates the change.

### A. Confirmed

1. **The GJ rule is wrong (V).**
   - Production GJ is thin-plate only. `computeTorsionalStiffness` returns `computeGJThinPlate` (`xSection/section/xSect_GJ.fs:131`), and G comes from the ratio rule at `:235-236`.
   - Q is never rotated anywhere in xSection (grep for rotate/orientation finds nothing), so the stored Q66 is the in-plane shear in ski axes.
   - In thin-strip torsion the dominant stress is τ_xy, which varies linearly through the thickness. The stiffness that matters is therefore D66, built from each ply's Q66. **Always use Q66 is correct.**
   - The rule's own comment ("back-calc gives true G12") shows what went wrong. For a ±45 stack, (Q11−Q12)/2 recovers the **ply** G12, not the laminate shear stiffness. Check with UNI-4.0-SM rotated to ±45: Q11 38.05, Q12 32.85, Q66 33.88, which matches BIMAX 374 exactly.
2. **Error factors reproduced (V).**
   - 0/90 fabrics: 5.7–13.0x too high (ROVINAP 24 is the low end, TWILL-3K the high end).
   - Mixed fabrics: 1.29–1.90x too high.
   - ±45 fabrics: the audit says 2.7–12.5x too low; the correct range is **2.6–13.0x** (BB566 is 2.58, TBX270 and BIMAX are 13.0).
3. **Wood, WBK and BCore rows are isotropic placeholders (V).** Q11/E = 1.1222 (ν 0.33), and Q66 is 3.6–7.4x too high. The Q22 range needs a small fix; see B6.
4. **The audit flagged the right isotropic rows.** Rows that are legitimately isotropic in-plane were correctly left alone: Volumat (Q66 = E/2(1+ν) exactly), OC M723 CSM, the Nico and XuRui veils, re-Evo, plastics and metals. The rows it did flag as isotropic (9/17 oz Uni, G-R80 family, braid, Amplitex C/F) are named or constructed as directional products, so the flags stand. BIMAX, TBX270 and TWILL-3K have Q11 = Q22 because they are balanced, not isotropic. They were correctly not called placeholders.
5. **Arithmetic checks (V).**
   - 17 oz Uni fibre fraction 1.28, 9 oz 0.81.
   - Braid values are exactly 1000x low.
   - "Rovings in braid" equals G-R80 ×1e-3.
   - Three WBK rows are exactly 1.675 Msi (11.549 GPa); N2 = N3.
   - Halpin-Tsai reproduced: E_y 12.86, G 3.77 (Vf 0.59); G 3.01 (Vf 0.50); G 2.68 (Vf 0.45).
6. **Wood ratios and the Q build (V/I).** Every proposed wood row recomputes from the stated ratios: Ash G 1.145, Aspen 0.624 / Q22 0.838, Birch 0.984, Fir 0.655, Paulownia 0.286. E = Q11 − Q12²/Q22 holds exactly. The Table 5-1/5-2 ratios used (white ash, cottonwood, sugar maple, red maple, yellow birch, subalpine fir, basswood; aspen by reciprocity 0.022/0.374 and 0.054/0.489) match my recollection of FPL-GTR-190. The ×1.10 footnote is real.
7. **EI results reproduced exactly (V).**
   - Triax skins: Q11 basis 464.4, E basis 409.2 (−11.9%), exact ABD 413.5.
   - Biax skins: 379.7 / 360.1 / 362.5.
   - The current code really is on the Q11 basis: `xSectCLT.fs` `EI_eff = D[0][0] − B²/A` with Q, and `updateProfile.fs` uses `qMatrix[0][0]`.
   - **The narrow-beam assumption holds.** The Searle parameter b²/(Rt) is 0.07–0.34 for R = 10–2 m, well below 1, so the ski bends as a beam and the E basis is correct.
8. **GJ results reproduced exactly (V).** 494.6 → 392.3; 624.3 → 297.2 → 178.6; biax skin term 389 → 76; core term 110–117 → 19–33.
9. **WBK N14 impact (V).** The core carries 14–22% of EI. Dropping N14 from 11.55 to about 7.2 GPa gives **−6 to −9% EI** (audit said about −7%).

### B. Corrected

1. **TWILL-3K was not copied by mistake. Withdraw that flag.**
   - Its values are an exact 0/90 layup of UNI-4.0-SM: (132.7 + 5.95)/2 = 69.32. Q12 1.57 and Q66 2.6 are invariant under 0/90 stacking, so matching the UD row is expected.
   - BIMAX 374 is the ±45 version of the same UD.
   - The only real question is the missing crimp knockdown: 69.3 vs a real 3K twill at 55–65, so it may be 5–15% high.
2. **Amplitex C/F: the audit's 0/90 assumption is probably wrong and would cut E_x by 25% (48.3 → 36).**
   - The Amplitex line in the library is UD (5057: Q22 4.6), and the row is a 52 mm tape, which suggests a stringer.
   - With the audit's own fibre fractions (V_c 0.144, V_flax 0.192), a UD rule of mixtures gives E_x 44.6–46.5. That is close to the stored 48.3; the audit's 0/90 hybrid gives 36.
   - **Recommendation:** keep E ≈ 48 and set E_y ≈ 5, G ≈ 1.8–2.0, ν ≈ 0.3 (I), pending confirmation of fibre directions. Do not paste the audit's row.
3. **The GJ impact is understated because of the steel edges.** The audit says "the FEM path handles steel more realistically", but that path is diagnostic only (`xSect_GJ.fs:619-622`); production is thin-plate.
   - The 2×2 mm steel edges sit at the free ends, where τ_xy actually decays to zero. Even so, they contribute 53–140 N·m² (17–30% of GJ).
   - Without the steel term, the changes are: triax **−24%** (audit −21%), triax + Ti −21% (audit −14%), biax **−81%** (audit −71%), biax + Ti −74% (audit −56%).
   - Treat the audit's GJ percentages as lower bounds on the change. The steel treatment is a solver problem in production, not just a note.
4. **G-R80 family: use the library's own glass precure as the template (I).**
   - G-EV 696R (same Hexcel family, UD-like) scaled to E_x 40.679 gives **E_y 12.79, G 2.79**.
   - The E_y matches Halpin-Tsai (12.86). The G is 26% below the audit's 3.77 but consistent with the rest of the library. Prefer Q66 ≈ 2.8 unless you choose to raise G12 across the whole library (see D5).
5. **Poplar - Dense is yellow-poplar, not a Populus species (I).**
   - 10.9 GPa and 455 kg/m³ are the Handbook yellow-poplar values; Poplar - Light (7.59, 370) is balsam poplar.
   - Using Table 5-1 yellow-poplar ratios (0.043 / 0.092 / 0.075 / 0.069, μ 0.318/0.392) gives E_y 0.736, **G 0.785** (audit 0.698, +12%), ν 0.355.
   - Mid "Poplar" is a blend of the two.
6. **Other small corrections.**
   - Wood Q22 is **9–24x** too high, not 8–20x. Paulownia and BCore are 24x, beech 9.1x.
   - Table 5-1 ratios are relative to true E_L. If the stored E is bending MOE, the proposed E_y and G should also be multiplied by 1.10 (about +10% on the core G terms), not just E.
   - The audit's text uses aspen = 9.31 in the WBK argument, but the Aspen wood row is 9.75. The 9.31 figure is WBK N2. The conclusion is unchanged.
7. **E-column inconsistencies the audit missed (V).**
   - DTVC 15.50.3: stored E 41.27, but Q11 − Q12²/Q22 = 40.80 (1.1%). This is the triax used in the representative section.
   - G-EV 696R: E is set equal to Q11 (34.29), while the Q values give 33.98.
   - Both are small, but they will show up once EI switches to the E column.

### C. Upgraded from the audit's "unverified"

- **ITOCHU HS40 (I, moderately confident).**
  - Pyrofil HS40 is Mitsubishi's roughly 455 GPa high-modulus fibre, and ITOCHU distributes Mitsubishi fibre.
  - The stored density 1577 fits HS40's fibre density of 1.85 at Vf 0.61 better than standard-modulus carbon (1.80, which would need Vf 0.66).
  - The row values (140 / 0.30 / Q66 5.0) look like a round-number template.
  - Likely UD E_x is about 250–270 GPa. If these rovings are used as stringers, their EI contribution is understated by about 1.8x.

### D. Could not verify

1. WBK core E values and stringer fractions; only the vendor or the sheet's author knows the source.
2. Construction of the G-R80 / R68 / 765R / R82 family. Its thickness fields (2.0–2.5 mm plates) suggest reinforcement plates, not skins.
3. **Braid angle.** Recomputed: E_x 9.65 fits ±45 (9.88) and also ±50–60 (8.5–8.9). Q66 is 8.9–10.8 across that whole range, so the audit's Q66 of 10.6 is safe to about 15%. A 0/±60 quasi-isotropic triaxial braid would give E_x about 18, so the stored isotropic assumption is wrong either way.
4. Modipur, Socrep TPU and Watom moduli: literature ranges only.
5. The real in-situ G12 for UD and 0/90 glass. Internal evidence: 13 oz Uni has Vf 0.585 from density and 0.549 from E, which implies G12 of about 3.2–3.8 (Halpin-Tsai); handbook E-glass/epoxy values are about 4. The library's 2.6 is therefore probably conservative. **With Q66 in the GJ formula, this becomes the dominant GJ uncertainty for 0/90 skins:** going from 2.6 to 3.8 raises GJ on a biax section by about 20%.
6. Beech and bamboo ratios. Beech agrees with Hering et al. 2012 as I recall them (G_LR/E_L ≈ 0.115, G_LT/E_L ≈ 0.076); I did not re-download it.

### Critical Files for Implementation
- C:/Users/jed.yeiser/documents/featurescripts/xSection/section/xSect_GJ.fs (lines 131, 226-236, 619-622)
- C:/Users/jed.yeiser/documents/featurescripts/xSection/materials/xSectCLT.fs (EI_eff = D11 − B11²/A11, Q11 basis)
- C:/Users/jed.yeiser/documents/featurescripts/xSection/features/updateProfile.fs (lines 204-240, also Q11 basis)
- C:/Users/jed.yeiser/documents/featurescripts/xSection/materials/xSectMaterials.fs (lines 107-160)
- C:/Users/JED~1.YEI/AppData/Local/Temp/claude/C--Users-jed-yeiser-documents-featurescripts/5d5ebd87-0ec8-4aa9-b813-a5d2c57d5e68/scratchpad/kaitai_materials.csv
