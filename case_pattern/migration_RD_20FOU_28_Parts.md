# Case pattern -> Case migration: Parts (version 570113152b9eae9094cafc85)

9 Case pattern feature(s), top of the tree down.

## 1. core_bot_2d_pattern  (tree #17)
- Define case: **Define_Core_Bottom** (#6); Close case: **Close_Core_Bottom** (#16)
- Close case outputs (type the query-variable name): `surf` <- query variable `_out`

### Case `core_bot_2d`  -> insert just above the Close case
- #bottom: pick query variable **#bot_surf_2d**  (= body **2D_BOTTOM_SURFACE** at (900,36.3,0) mm, from "Derive_Ref_Geo")
- #periphery: pick query variable **#periphery_surf_2d**  (= body **2D_PERIPHERY** at (890,32.7,12) mm, from "Derive_Ref_Geo")
- #inside_shift (length) = `#BF_Tol`
- #bottom_inside_offset (length) = `#Base_Thck+#BF_Thck`

## 2. Core_2D_Bump  (tree #33)
- Define case: **Bump_Middle_Case** (#20); Close case: **bumpMiddleClose** (#32)
- Close case outputs (type the query-variable name): `surf` <- query variable `_mid`

### Case `core_bump_2d`  -> insert just above the Close case
- #profile: pick query variable **#sw_shelf_2d**  (= body **2D_SW_Shelf** at (890,33.1,12) mm, from "Derive_Ref_Geo")
- #middle_offset (length) = `#SW_Width+ #BF_Thck+#BF_Tol`
- #extent_offset (length) = `#SW_Width`

## 3. SW_3D_Bump  (tree #34)
- Define case: **Bump_Middle_Case** (#20); Close case: **bumpMiddleClose** (#32)
- Close case outputs (type the query-variable name): `surf` <- query variable `_mid`

### Case `sw_top_bump_3d`  -> insert just above the Close case
- #profile: pick query variable **#top_surf_3d**  (= body **TOP_SURFACE** at (884.4,36.3,29.7) mm, from "Derive_Ref_Geo")
- #middle_offset (length) = `#Top_Glass_Thck+#Top_Thck+#BF_Thck`
- #extent_offset (length) = `#Top_Glass_Thck+#Top_Thck`

## 4. SW_2D_Bump  (tree #35)
- Define case: **Bump_Middle_Case** (#20); Close case: **bumpMiddleClose** (#32)
- Close case outputs (type the query-variable name): `surf` <- query variable `_mid`

### Case `sw_top_bump_2d`  -> insert just above the Close case
- #profile: pick query variable **#top_surf_2d**  (= body **2D_PROFILE** at (890,36.3,10.7) mm, from "Derive_Ref_Geo")
- #middle_offset (length) = `#Top_Glass_Thck+#Top_Thck+#BF_Thck`
- #extent_offset (length) = `#Top_Glass_Thck+#Top_Thck`

## 5. 2d_bf_inside  (tree #43)
- Define case: **bf_inside_case** (#36); Close case: **close_bf_inside** (#42)
- Close case outputs (type the query-variable name): `bump` <- query variable `_out_1`; `surf` <- query variable `_out_2`

### Case `bf_inside_2d`  -> insert just above the Close case
- #inside: pick query variable **#sw_shelf_2d**  (= body **2D_SW_Shelf** at (890,33.1,12) mm, from "Derive_Ref_Geo")
- #outside: pick query variable **#periphery_surf_2d**  (= body **2D_PERIPHERY** at (890,32.7,12) mm, from "Derive_Ref_Geo")
- #join_start: CLICK body **2D_Tip_Steel_Trim** at (1729.9,36.9,12.5) mm, from "Derive_Ref_Geo"
- #join_end: CLICK body **2D_Tail_Steel_Trim** at (47.5,35.6,12.5) mm, from "Derive_Ref_Geo"

## 6. base_2d  (tree #74)
- Define case: **Define base_case** (#64); Close case: **Close case 1** (#73)
- Close case outputs (type the query-variable name): `body` <- query variable `_out`

### Case `base_2d`  -> insert just above the Close case
- #bottom: pick query variable **#bot_surf_2d**  (= body **2D_BOTTOM_SURFACE** at (900,36.3,0) mm, from "Derive_Ref_Geo")
- #periphery: pick query variable **#periphery_surf_2d**  (= body **2D_PERIPHERY** at (890,32.7,12) mm, from "Derive_Ref_Geo")
- #bump_1: CLICK body **2D_Tip_Steel_Trim** at (1729.9,36.9,12.5) mm, from "Derive_Ref_Geo"
- #bump_2: CLICK body **2D_Tail_Steel_Trim** at (47.5,35.6,12.5) mm, from "Derive_Ref_Geo"

## 7. tiptail_2d  (tree #124)
- Define case: **Define case 1** (#112); Close case: **Close case 2** (#123)
- Close case outputs (type the query-variable name): `tip` <- query variable `_start`; `tail` <- query variable `_end`

### Case `mat_2d`  -> insert just above the Close case
- #bottom: pick query variable **#bot_surf_2d**  (= body **2D_BOTTOM_SURFACE** at (900,36.3,0) mm, from "Derive_Ref_Geo")
- #side: pick query variable **#periphery_surf_2d**  (= body **2D_PERIPHERY** at (890,32.7,12) mm, from "Derive_Ref_Geo")
- #start_cap: CLICK body **2D_Tip_Core_Trim** at (1625,42.7,12.5) mm, from "Derive_Ref_Geo"
- #end_cap: CLICK body **2D_Tail_Core_Trim** at (135,33.5,12.5) mm, from "Derive_Ref_Geo"
- #start_bump: CLICK body **2D_Tip_Steel_Trim** at (1729.9,36.9,12.5) mm, from "Derive_Ref_Geo"
- #end_bump: CLICK body **2D_Tail_Steel_Trim** at (47.5,35.6,12.5) mm, from "Derive_Ref_Geo"

## 8. core_2d  (tree #151)
- Define case: **Define core case** (#135); Close case: **close_core** (#150)
- Close case outputs (type the query-variable name): `body` <- query variable `_out`

### Case `core_2d`  -> insert just above the Close case
- #bottom: pick query variable **#core_bot_2d_surf**  (= body **core_bot_2d_surf** at (900,36.3,1.9) mm, from "core_bot_2d_pattern")
- #periphery: pick query variable **#core_bump_2d_surf**  (= body **core_bump_2d_surf** at (890,31.6,12) mm, from "Core_2D_Bump")
- #top: pick query variable **#top_surf_2d**  (= body **2D_PROFILE** at (890,36.3,10.7) mm, from "Derive_Ref_Geo")
- #start_cap: CLICK body **2D_Tip_Core_Trim** at (1625,42.7,12.5) mm, from "Derive_Ref_Geo"
- #end_cap: CLICK body **2D_Tail_Core_Trim** at (135,33.5,12.5) mm, from "Derive_Ref_Geo"

## 9. Case pattern 1  (tree #222)
- Define case: **Define_CE_Case** (#197); Close case: **Close case 3** (#221)
- Close case outputs (type the query-variable name): `tip` <- query variable `_tip_block`; `tail` <- query variable `_tail_block`

### Case `ce_2d`  -> insert just above the Close case
- #bottom: pick query variable **#bot_surf_2d**  (= body **2D_BOTTOM_SURFACE** at (900,36.3,0) mm, from "Derive_Ref_Geo")
- #top: pick query variable **#top_surf_2d**  (= body **2D_PROFILE** at (890,36.3,10.7) mm, from "Derive_Ref_Geo")
- #side: pick query variable **#sw_shelf_2d**  (= body **2D_SW_Shelf** at (890,33.1,12) mm, from "Derive_Ref_Geo")
- #tip_cap: CLICK body **2D_Tip_Core_Trim** at (1625,42.7,12.5) mm, from "Derive_Ref_Geo"
- #tail_cap: CLICK body **2D_Tail_Core_Trim** at (135,33.5,12.5) mm, from "Derive_Ref_Geo"
- #tip_trim: CLICK body **2D_Tip_CE_Perphery** at (1705,36.3,12.5) mm, from "Derive_Ref_Geo"
- #tail_trim: CLICK body **2D_Tail_CE_Periphery** at (65,36.3,12.5) mm, from "Derive_Ref_Geo"
