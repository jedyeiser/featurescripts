# QC Table - Quick Reference Guide

*Automated ski/snowboard quality control measurement table generation*

---

## WHAT IT DOES

Automatically measures core and sidewall geometry at precise stations along the board length. Generates comprehensive dimensional data tables for production QC verification.

**[IMAGE: Before/After - 3D model with measurement stations marked → resulting data table]**

---

## SETUP (4 SIMPLE STEPS)

**[IMAGE: Numbered screenshots showing each step]**

**1. SELECT REFERENCES**
- FCP (Forward Contact Point) - vertex, plane, or face
- ACP (Aft Contact Point) - vertex, plane, or face

**2. SELECT BODIES**
- Core body or composite part (optional)
- Sidewall body (optional)
- *Must select at least one*

**3. CONFIGURE STATIONS**
- Boundary: **Minimal** (recommended) - full data in stance + endpoints
- Spacing: **Evenly Spaced on RSL** with 40 points (typical)

**4. FORMAT TABLE**
- Detail Level: **Standard** (6 columns) or **Details** (15 columns)
- Order: Descending (tail to tip) is typical
- Units: Millimeters (default)

---

## OUTPUT MODES

### STANDARD MODE (Quick QC)
**[IMAGE: Table showing 6 columns with callouts highlighting each]**

| Callout | Station | X | Core Height | SW Height | Core/SW Δ |
|---------|---------|---|-------------|-----------|-----------|
| FCP | 0 | -750mm | 3.2mm | 3.1mm | +0.1mm |
| MRS | 5 | 0mm | 5.8mm | 5.7mm | +0.1mm |
| ACP | 10 | 750mm | 3.4mm | 3.3mm | +0.1mm |

**Use for:** Daily production checks, quick verification, standard reporting

### DETAILS MODE (Comprehensive Analysis)
**[IMAGE: Table showing all 15 columns with grouped sections highlighted]**

*Adds:* X references (ACP, tail positions) + Core geometry (width, grooved thickness, top width/angle, base rout depth/width)

**Use for:** Design verification, troubleshooting, comprehensive documentation

---

## STATION PLACEMENT

**[IMAGE: Side view diagram of ski/board with stations marked and three different spacing methods illustrated]**

### Critical Stations (Always Included)
- **FCP, XS-1, MRS, XS-2, ACP** - Key reference points
- **Body endpoints** - Tip and tail (if outside FCP/ACP)

### Three Spacing Methods
1. **Static Distance** - Fixed spacing (e.g., 3 inches between points)
2. **Evenly on RSL** - Divides stance length into equal parts
3. **Evenly on Length** - Divides full body length into equal parts

---

## UNDERSTANDING THE DATA

**[IMAGE: Cross-section diagrams showing each measurement with labels]**

### Core Measurements
- **Core Height** = Bottom to top thickness
- **Core Width** = Full width (2x from centerline)
- **Grooved Thickness** = Thickness after groove slot
- **Top Width/Angle** = Chamfered top edge (if present)
- **Base Rout** = Bottom chamfer (if present)

### Sidewall Measurements
- **SW Height** = Bottom to top at centerline
- Measured along averaged center spline

### Delta Values
**[IMAGE: Diagram showing core and sidewall overlay with delta callout]**
- **Core/SW Δ = Core Height - SW Height**
- **Positive** = Core thicker (typical: 0.0-0.3mm)
- **Negative** = SW taller (investigate!)
- **Near zero** = Good alignment

---

## BOUNDARY BEHAVIOR

**[IMAGE: Three diagrams showing station coverage for each mode]**

**IGNORE** - Only between FCP/ACP
**MINIMAL** - Full data in stance + one endpoint per body outside *(recommended)*
**NORMAL** - All points across entire body

---

## TYPICAL WORKFLOWS

### Daily Production QC
**[IMAGE: Screenshot sequence]**
→ Standard mode | Minimal boundary | Descending order | 40 stations
→ Quick visual scan of delta values
→ Flag values outside tolerance

### Design Verification
**[IMAGE: Screenshot sequence]**
→ Details mode | Normal boundary | Dense spacing (60-80 stations)
→ Export to Excel
→ Compare actual vs. design spec

### Troubleshooting
**[IMAGE: Screenshot sequence]**
→ Details mode | Add manual stations at problem areas
→ Analyze delta progression
→ Check core top edge and base rout dimensions

---

## QUICK TIPS

✓ **Use Minimal boundary** for cleanest output (recommended for 90% of use cases)

✓ **Start with Standard mode** - switch to Details only when needed

✓ **Descending order** (tail to tip) is industry standard for ski tables

✓ **40 stations on RSL** is a good balance of detail vs. performance

✓ **Composite cores** are automatically handled - no special setup required

✓ **Large delta values** (>0.5mm) may indicate alignment issues - investigate

✓ **Negative deltas** (SW taller) are unusual - verify measurements

✓ **Table updates** automatically when you regenerate the feature

---

## TROUBLESHOOTING

| Issue | Solution |
|-------|----------|
| No data at station | Body doesn't intersect that X position |
| Large delta values | Check core/SW alignment, verify references |
| Missing columns | Wrong detail level selected |
| Table not updating | Regenerate "Generate QC Table Data" feature |

**[IMAGE: Common error examples with visual indicators]**

---

*For detailed documentation and advanced usage, see full user guide.*
