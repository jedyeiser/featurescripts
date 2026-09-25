# Track report: EI & Cross Section (xSection)

## 1. Bottom line
- **The geometry core works; the physics inputs don't.** I checked the code myself. The formulas are coded as documented: EI_eff = D11 - B11^2/A11, NA = B11/A11, Green's-theorem properties, and the Mohr integral. The systematic over-prediction comes from what goes into them:
  - **GJ shear-modulus rule is backwards.** `xSect_GJ.fs:235-236` uses `(Q11-Q12)/2` when Q11/Q22 < 3. The correct plate-twist modulus is always Q-bar66. I recomputed from the CSV: E-LT-400 gives 16.1 vs 2.60 GPa (6x over) and TBX270 +/-45 gives 2.57 vs 33.5 GPa (13x under).
  - **Wood cores are entered as isotropic.** The Aspen row has Q66 = 3.67 GPa, about 4x real G_LT.
  - **EI uses the plate modulus Q11.** It should use the beam modulus E_x. The difference is +12% for isotropic materials and up to 3.9x for +/-45 biax.
  - Together these plausibly explain the accepted 1.2-1.4x EI and 1.75x GJ over-predictions. That is inferred: it can't be checked until we know the ski's layup.
- **Failures are silently swallowed.** Empty try/catch blocks wrap the attribute store (`xSect.fs:457-467`, and again inside `xSectStorage.fs:143-168`) and the whole GJ loop (`xSect.fs:373-384`). The feature regenerates green even when downstream data is missing.
- **Solve GJ duplicates work.** xSect already computes GJ inline with the same pinned solver. Solve GJ's write-back edits a copy (`gjDataAccess.fs:125-137`, `255-265`: `existingSections` is never stored back). About 1,100 lines across GJ_Feature, gjAnalysis, gjDataAccess and the dead FEM in xSect_GJ can go.
- **Nothing is tested.** There is no xSection test Part Studio and no devtools checker, so none of the physics fixes can be validated. Building both is P0.
- **variable_tools and icons.** At most one key (`eiEdges`), and only after an FS 3070 bump. No xSection feature has an icon, and `install_icons.py:56` writes files as ASCII, so it would crash on the non-ASCII feature files.

## 2. Per-feature verdict

| Feature / module | State | Biggest problems | Effort |
|---|---|---|---|
| EI and Cross Section (`features/xSect.fs`, 532 lines, FS 2892) | Works, but fragile | Store and GJ failures swallowed; end stations graze the tip/tail; plate-vs-beam EI basis; composites on by default and slow; no icon | M |
| Solve GJ (`features/GJ_Feature.fs`) | Redundant, buggy | Recomputes GJ xSect already has; copy bug in `gjDataAccess`; refers to params that don't exist (`createGJCurve`/`curvePrefix`); crashes on empty selection | S (deprecate) |
| `section/xSect_GJ.fs` | D | Wrong G rule; ~330 dead FEM lines; GJ taken about one global centroid (`:104-131`, `:275-287`); holes counted as solid | S to replace with ABD formula |
| `materials/xSectCLT.fs` | B | Q11 basis; unused imports; stale "Known Limitation" comment | S |
| `materialData.csv` | C | Isotropic wood/core rows; 9 oz and 17 oz "Uni" rows are isotropic placeholders | S (data) |
| `section/xSectProcessing.fs` | C | Composite dedup grid misses long curves (`:678`, `:749`); partial overlap drops a curve (`:735`); no fallback if the batched intersect fails | M |
| `section/xSect_Triangulation.fs` | B- | Probable O(P^2) point-store copying; ear clip O(m^3) on collinear runs; sampling tied to control-point count (arcs lose up to ~10% of area) | M |
| `core/xSectUtils.fs` | C | ~45% dead code; `projectXToPathParameter` costs ~21 kernel calls per station; tip/tail station cap of 4 | S-M |
| `core/xSectReferencePoints.fs` | C | FCP/ACP plane check tests only normal Y (`:90`); `edgeQuery` unused | S |
| `core/xSectStorage.fs` | C+ | Swallowed failures; ~55-60% of the payload is unread (size inferred, not measured) | S |
| `beam/xSectBeamAnalysis.fs` | B | EI=0 stations treated as rigid (`:258`); assumes world X is the ski axis; EI-curve decoding written 3 times | S |
| `materials/xSectComposites.fs` | C | One op per curve per station (~3,500 ops); empty catches; labels use array index, not stationNumber | S-M |

## 3. Prioritized recommendations

**P0**
1. **Remove the empty try/catch blocks** (S). Locations: `xSect.fs:373-384` and `:457-467`, `xSectStorage.fs:143-168`, Composites, Visualization. Let errors surface, or report skipped GJ stations with `reportFeatureInfo`. Rationale: the user's rule, and a failed store today looks like success.
2. **Build the test Part Studio and `devtools/onshape/check_xsection.py`** (M). The checker reads the `CrossSectionAnalysis` attribute through a read-only fseval. Cases, each named with its expected result:
   - `EI_iso_rect_100x10=83.3(beam)/93.5(plate)`
   - `GJ_TBX270_plate_100x2=8.93`
   - `GJ_ELT400_plate_100x2=0.69`
   - `GJ_step_50x10+50x4=66.7`
   - `hollow_rect_EI=analytic`
   - `circle_r5_area_err<0.5%`
   - `bimetal_NA=3.81mm`
   - `two_touching_laminates_no_dup`
   - `path_reversed_same_EI`
   - `FCP_gt_ACP_same_EI`
   - `tip_grazing_no_warning`

   This has to come before the physics changes, so each fix has a before/after.
3. **Set the GJ modulus to Q-bar66 for every body** (S). Delete the ratio branch at `xSect_GJ.fs:231-236`.
4. **Enter orthotropic values for the wood/core and 9 oz / 17 oz Uni rows** (S, data only). Use E_T ≈ 0.06 E_L, G_LT ≈ 0.08 E_L, nu_LT ≈ 0.4. The Wood Handbook ratios need confirming.

**P1**
5. **Add a "Stiffness basis" enum** (S): Beam E_x (default) or Plate Q11, with `UIHint.SHOW_LABEL`. Per body, E_x = Q11 - Q12^2/Q22, applied at `xSectCLT.fs:394-396` and `:430`.
   - I agree with the direction but am tempering the claim. A ~100 mm wide, 10 mm thick ski at metre-scale curvature sits between beam and plate behaviour (b^2/(R*t) ≈ 0.5), mostly toward beam. That's why this should be a selectable option checked against measurement, not a silent change. Existing tables will shift about 10%, which the user needs to hear about.
6. **Compute GJ from the ABD matrix already assembled**: GJ = 4*(D66 - B66^2/A66) (S-M).
   - This is O(bodies), needs no mesh, and handles holes.
   - Then deprecate Solve GJ and delete GJ_Feature, gjAnalysis, gjDataAccess and the FEM in xSect_GJ, after counting live Solve GJ instances.
   - Note: this is algebraically identical to today's global-centroid formula, so it fixes holes and speed but **not** the per-strip bias (see P2-12).
7. **Inset the tip/tail stations** by max(0.5 mm, 0.02*spacing) and drop zero-area stations before fitting the curves (S). Files: `xSectUtils.fs:373-395`. Also fix the FCP/ACP plane check to require `abs(normal[0]) > 1-tol` (`xSectReferencePoints.fs:90`).
8. **Performance** (S-M):
   - Place stations with one dense `evPathTangentLines` sample plus a batched refinement, instead of ~1,200 kernel calls.
   - Default `createComposites` to false, or build composites from the kept `opIntersectFaces` wires.
   - Strip collinear vertices before ear clipping.
   - Use a per-body local point store, which is safe once GJ comes from ABD.
9. **Trim the stored payload** (S). Keep: xCoord, EI_eff, GJ_eff, neutralAxisY, boundingBox, linealDensity, points2D and per-body triangles (Update profile needs the mesh). Add `readCrossSectionEntry`.
   - **Consolidate duplicates** (M): one EI-curve codec in xSectBeamAnalysis, replacing the three decoders; one alpha/beta fit in updateProfile.

**P2**
10. **Fix composite dedup** (M): use the sampled-geometry test, index curves in every grid cell their bbox spans, and keep both curves on partial overlap.
11. **Chord-tolerance sampling** (S). Delete the dead Alter-meshing UI and dead helpers, then do the mechanical style pass: 53 braceless blocks, non-ASCII lines, 5 SHOW_LABEL hints.
12. **Optional strip-wise local mid-plane GJ** (M): bin triangles by width. Stepped test shows 1.29x today.
13. **Icons, the `eiEdges` key, and the typo** (S). The typo is "Analze baseline" at `analyzeBaseline.fs:785`. Mohr integral: interpolate across zero-EI stations and use arc length.

## 4. Designs

**GJ consolidation.** Add the following to `xSectCLT` right after the ABD is assembled:

`GJ_eff = 4*(D[2][2] - B[2][2]^2/A[2][2])`

- D66/B66 must be built from Q66 (they already are, because the full Q is scaled).
- Store the result in `mechanicalProperties.GJ_eff` and delete Step 3b in `xSect.fs`.
- Optional phase 2: bin triangles by `point2D[1]` (about 2 mm bins), take a G-weighted centroid per bin, and sum 4*G*Iz per bin. This needs hole-aware triangulation first.

**Storage.** Use the minimal option above.
- The ideal option (one attribute on the EI curve body, read with `qHasAttribute`) removes the read-modify-write on the shared origin map. It is L effort for a small gain; defer.

**Station placement.**
- Sample the path once with about 512 parameters in a single `evPathTangentLines` call.
- Invert X by linear interpolation.
- Refine all stations with one batched secant step: 2-3 calls in total.

**Batched intersect fallback.** On a thrown `opIntersectFaces`, fall back to per-station intersection for that body only, and report the failed stations as info. This is the one justified try in the pipeline. The failure mode itself is inferred.

**Native replacement.** Not worth it. Getting section properties natively means splitting every body at every station. Keep the Green's-theorem code and use `evApproximateMassProperties` only as a test oracle.

## 5. variable_tools
- **EI and Cross Section:** one key, `eiEdges` = `extractableQuery(qCreatedBy(id + "eiCurve", EntityType.EDGE), "EI curve: world Z in mm = EI in N*m^2")`.
  - Real consumers: estimateStiffness.eiEdges, estimateDeflection.selEI, generateBaseline.eiEdgesQuery, updateProfile.measuredEIQuery.
  - The standard `output` mixes the EI curve with the NA, GJ, lineal-density and profile curves (plus composite/debug wires), so a dedicated key is useful.
  - No scalar keys: nothing reads maxEI or total mass downstream. No per-station tables.
  - **Gate:** xSect.fs is FS 2892 and `extract_outputs.fs` is 3070. Add the key only with a deliberate 3070 bump, regression-checked against the test studio. Whether a 2892 module can import a 3070 module is unverified.
- **Solve GJ:** none, because it's being deprecated.
- **Other xSection features:** none. They consume EI, they don't produce it.
- **Possible `gjEdges`:** only if estimateDeflection's "Plate stiffness" input actually expects a GJ curve (open question).

## 6. Icons
None of the xSection features or tables has an icon, and none appears in `icon_targets.py`. I confirmed both with grep.

Clean each file to ASCII before installing icons, because `install_icons.py:56` writes with `encoding="ascii"`. Better still, make the installer check for non-ASCII before it uploads the SVG, so a failure doesn't leave an orphan tab. Targets use subfolder paths, e.g. `("xSection","features/xSect",...)`. Style is 20x20, #333/#999/#FFF, with a #1651B0 accent.

| Feature | Glyph concept |
|---|---|
| EI and Cross Section | Grey beam in 3/4 view cut by a blue section plane, with a stepped laminate outline on the cut face |
| Estimate Stiffness | Beam on two supports, blue load arrow, small spring zigzag |
| Estimate Deflection | Same supports, beam sagging as a blue curve under the load |
| Update profile | Grey thickness profile with a blue arrow pushing it onto a dashed target line |
| Generate baseline | Blue camber/rocker side-profile over a grey ground line |
| Analyze baseline | Same profile plus a measure glyph |
| Solve GJ | Skip; it's being deprecated |
| Tables | Only if `defineTable` accepts an Icon (unverified) |

## 7. Conflicts, verification, open questions

**Spot-checks** (I read the cited code for every critical/high finding):

| Finding | Result |
|---|---|
| Swallowed store failure | Confirmed |
| Swallowed GJ loop | Confirmed |
| G-rule inversion | Confirmed, with CSV arithmetic re-done |
| Isotropic Aspen row | Confirmed: Q11 = 9.75/(1-0.33^2) |
| Q11 in D11 / EI_eff | Confirmed |
| Global-centroid Iz | Confirmed (`computeGJThinPlate` subtracts one `y_bar`) |
| Solve GJ redundancy | Confirmed |
| Write-back copy bug | Confirmed, and latent |

- **Write-back field mismatch.** gjDataAccess writes `crossSections[i].GJ_eff`, but xSect stores the value at `mechanicalProperties.GJ_eff`, so it is the wrong field as well as a copy. I downgraded the copy bug from high to medium: it is moot under deprecation and has no consumers.
- **Q11 vs E_x.** Kept at high, but reframed as a selectable basis (reasoning in P1-5). It also contradicts the memory note that says this correction was ruled out; the user should decide.
- **Holes in GJ.** Expert 1 rated it low, expert 2 medium. It doesn't matter which: the ABD route fixes it for free.
- **ABD route vs global-centroid bias.** Expert 2 proposes both, but they are not independent. The ABD formula reproduces the current global-centroid value, so the strip-wise method is a separate, later step.
- **Point store.** Expert 1 suggested dropping cross-body sharing, and expert 3 said the mesh must stay for Update profile. These are compatible: store per-body local meshes, trimmed.

**Unverified claims:**
- O(P^2) copy-on-write in the point store (inferred from value semantics, not measured).
- Payload size of ~100 KB per station.
- Batched-intersect failure on a coplanar or grazing plane.
- Whether a 2892 module can import a 3070 module.
- Core GJ excess of ~100 N*m^2 (the layup is unknown).
- Whether the batched-intersect version was checked against ROY_Test for identical EI (nothing records it).

**Stale memory:** the batched-intersection speed TODO is done, and along-path now accepts a single WIRE body only (memory says EDGE || WIRE).

**Open questions for the user:**
1. Which CSV materials are in the measured ski? The net effect of the G-rule fix depends on the 0/90 vs +/-45 mix.
2. Were the reference EI/GJ measurements taken from the same model that produced the 1.2-1.4x and 1.75x ratios, so we get a clean before/after?
3. Should EI default to the beam basis (E_x), given that existing tables will shift about 10%?
4. Are there live Solve GJ instances that need migrating before deletion?
5. Is `createComposites = true` by default intentional? It is probably the single biggest optional cost.
6. Should direct edge selection for "Wire to cross section along" come back?
7. Is the Update profile STD solver used? If not, storing the mesh could become an opt-in option.

### Critical Files for Implementation
- C:/Users/jed.yeiser/documents/featurescripts/xSection/features/xSect.fs
- C:/Users/jed.yeiser/documents/featurescripts/xSection/section/xSect_GJ.fs
- C:/Users/jed.yeiser/documents/featurescripts/xSection/materials/xSectCLT.fs
- C:/Users/jed.yeiser/documents/featurescripts/xSection/materialData.csv
- C:/Users/jed.yeiser/documents/featurescripts/xSection/core/xSectStorage.fs