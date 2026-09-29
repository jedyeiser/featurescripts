# Export primitive -- open questions (2026-09-28)

Answer inline (edit this file) or in chat. Proposed answers are marked **Proposed**; say "ok" to accept.

## Decided today (for the record)
- Target EI = optional input; deflection / stiffness rows appear only when it is given. Rows without data are hidden.
- Tip / Tail block = the aluminium tooling blocks; name typed in, or taken from a picked wire body.
- RSL = straight x distance FCP..ACP. No asymmetric skis. Radius plot stays a wire "chart".
- Datum: new option "Datum uses: Origin (default) / Coordinate system" -- the ski's mate connectors have Z along
  the ski, which is why picking MRS rotated everything.
- Drawing: one large sheet with everything + a printable A4 set; new (branded) template.

## 1. Target EI input format
**Proposed:** an EI curve wire in the xSection convention -- XZ plane, height 1 mm = 1 N*m^2, x = ski x (what
"EI and Cross Section" draws, and what beamBuilder reads). OK, or do you also want to read the EI data an
"EI and Cross Section" feature already stored in the same studio (no wire needed)?

## 2. Tip_height / Tail_height
Still undefined. **Proposed:** height of the highest point of the BOTTOM profile beyond FCP (tip) / ACP (tail),
measured normal to the FCP-ACP line. Or is it the top of the tip/tail block, or the total ski height at the end?

## 3. SW rout table -- ANSWERED 2026-09-29, BUILT (Table 4)
User: measure at MRS only (datum YZ plane through MRS, +Y side; -Y mirrored when the surface is only there); angle =
rout surface (its section tangent at the start edge) vs WORLD Z, 1 decimal; step-in = width (y) from the start edge to
the ski's outside; start / stop = optional picks, else the surface's x extent; x, s, Dist. from tail per end.
Implemented with these notes (details: publish_tools/README.md "Table 4 SW rout"):
- **Start edge -- deviation, please confirm:** the user's rule "end closest to the centreline" picks the TOP overshoot
  on RD 20TAC's real rout surface (the rout face leans in going up and runs on past the ski's top: z 28.8, step-in 3.85).
  Built instead: the LOWEST section end inside the ski's outside (gives the designed 0.8 mm step-in / 4 mm up / 7.0 deg);
  on a rout leaning out both rules agree.
- **Outside** = the volume section's largest |y| at MRS on that side (normally the base edge). Assumed.
- **Dist. above base = ASSUMED:** z(start edge) - z(base), base = the profile's bottom wire at MRS (centreline).
- Start / stop with no picks: start = lowest x, stop = highest x of the surface (assumed; not tail -> tip).

Earlier proposal (superseded): one value each should fall out of the inputs: measure at every station where both the rout surface and the
volume side exist; report the value if all stations agree within a tolerance (0.1 deg / 0.05 mm), otherwise report
min..max and flag it (a varying rout is then a real finding). Definitions to confirm:
- **SW rout angle:** angle between the rout surface and the volume's side face, in the cross-section plane. OK?
- **Dist_above_base:** **Proposed:** height above the base (bottom profile) where the rout surface meets the volume side.
- **Step-in:** **Proposed:** horizontal distance, at the base, from the volume side to the rout surface (positive = into the ski).
- **Start / stop (x, s):** where the rout surface begins / ends along the ski (from the surface's extent, or the
  optional sw_rout_start / sw_rout_stop inputs if given?).

## 4. ISO box
**Proposed:** inputs = distance forward of MP, distance aft of MP, ISO minimum height (optionally a width).
The feature builds the box from the BOTTOM surface upward over MP-aft .. MP+fore, height = ISO min, and reports:
PASS/FAIL, the minimum actual thickness in that range and its location (x, s) -- the "solved" location.
- Box width: full ski width at each station, a fixed binding-mount width centred on the centreline, or centreline only?
- Several boxes (e.g. per binding standard / per MP for snowboards)?
- Show the box in the composite (profile band) as wires?

## 5. Drawing + template
- Large sheet: which size (A1 / A0 / custom long sheet ~ ski length at 1:5)?
- A4 set: which bands/tables per page? **Proposed:** p1 profile + baseline + Table 5; p2 footprint + radius plot + Table 2;
  p3 Tables 1, 3 (+4); p4 data table.
- Template branding per document (K2 / LINE / MADSHUS)? What should replace the unused title-block fields
  (e.g. ski name, length, RSL, date of primitive, source versions)?

## 6. Curvature comb on the baseline (curiosity)
Possible: generate the comb as wires (short lines normal to the curve, length proportional to curvature, plus the
envelope curve) as an optional extra in the baseline band -- Onshape's native comb never appears in drawings.
Cheap to add. Want it (off by default)?
