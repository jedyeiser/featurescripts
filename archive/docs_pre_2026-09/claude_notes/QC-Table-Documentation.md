# QC Table Feature - User Guide

## What It Does

The QC Table feature automatically generates quality control measurement tables for ski and snowboard manufacturing. It measures core and sidewall geometry at precise stations along the board length, providing comprehensive dimensional data for production verification.

**Key Capabilities:**
- **Unified measurement** - Handles core only, sidewall only, or both together
- **Smart station placement** - Automatically spaces measurement points with multiple methods
- **Delta calculations** - Compares core and sidewall alignment when both are present
- **Flexible output** - Two detail levels: Standard (essential data) or Details (all measurements)

---

## Setup Requirements

**What You Need:**
1. **FCP Reference** - Front Contact Point (vertex, plane, or planar face)
2. **ACP Reference** - Aft Contact Point (vertex, plane, or planar face)
3. **Core Body** - Select core solid body or composite part (optional)
4. **Sidewall Body** - Select sidewall solid body (optional)

**Note:** At least one body (core or sidewall) must be selected.

---

## Measurement Stations

The feature measures geometry at multiple stations along the X-axis. Stations are automatically placed based on your chosen method:

### **Station Placement Methods**

**1. Static Distance** - Fixed spacing between points
- Choose starting location: Tail or MRS (center)
- Set distance between points (default: 3 inches)
- Points extend across body length

**2. Evenly Spaced on RSL** - Divides Reference Stance Length into equal segments
- Set number of points (default: 40)
- Pattern extends beyond FCP/ACP if bodies extend further

**3. Evenly Spaced on Body Length** - Divides actual body length into equal segments
- Set number of points (default: 40)
- Covers entire body from tip to tail

### **Critical Stations (Always Included)**
- **FCP** - Forward Contact Point
- **XS-1** - Quarter stance forward
- **MRS** - Mid Reference Stance (X = 0)
- **XS-2** - Quarter stance aft
- **ACP** - Aft Contact Point

### **Boundary Behavior**

Controls what happens with measurements outside FCP/ACP:

- **Ignore** - Only measure between FCP and ACP
- **Minimal** - Full data between FCP/ACP + one endpoint per body outside (default for most use)
- **Normal** - Measure all points across entire body length

---

## Table Output

### **Standard Mode** (Default - 6 columns)

Essential measurements for quick QC checks:

| Column | Description |
|--------|-------------|
| **Callout** | Station identifier (FCP, MRS, ACP, etc.) |
| **Station** | Sequential station number |
| **X** | Position from MRS (X=0) |
| **Core Height** | Core thickness at station |
| **SW Height** | Sidewall height at station |
| **Core/SW Δ** | Thickness difference (Core - SW) |

**Use Standard mode for:** Daily production QC, quick visual checks, standard reporting

### **Details Mode** (15 columns)

Comprehensive measurements including:

**Additional Distance References:**
- X from ACP
- X from Core Tail
- X from SW Tail

**Core Geometry Details:**
- Core Width (full width at widest point)
- Grooved Thickness (thickness with groove slot)
- Top Width (width at top edge if chamfered)
- Top Angle (top edge angle in degrees)
- BR Depth (base rout depth)
- BR Width (base rout width)

**Use Details mode for:** In-depth analysis, troubleshooting, design verification, comprehensive documentation

---

## Understanding the Measurements

### **Core Measurements**
- **Core Height** = Full thickness from bottom to top
- **Core Width** = Full width at widest point (doubled from centerline)
- **Grooved Thickness** = Thickness after groove slot (front plane split)
- **Top Edge** - Detected if widest point is not at top (chamfered core)
- **Base Rout** - Detected if widest point is not at bottom

### **Sidewall Measurements**
- **SW Height** = Height from bottom edge to top edge at centerline
- Measured along center splines (averaged between inside/outside edges)

### **Delta (Core/SW Δ)**
- **Positive value** = Core is thicker than sidewall at that station
- **Negative value** = Sidewall is taller than core (unusual, may indicate issue)
- **Near zero** = Good alignment between core and sidewall

---

## Step-by-Step Usage

**1. Insert Feature**
   - Add "Generate QC Table Data" feature to your Part Studio

**2. Set References**
   - Select FCP reference (vertex or plane)
   - Select ACP reference (vertex or plane)
   - FCP must be forward of ACP (positive RSL)

**3. Select Bodies**
   - Select core body or composite part (if measuring core)
   - Select sidewall body (if measuring sidewall)
   - Both optional, but at least one required

**4. Configure Stations**
   - Choose boundary behavior (Minimal recommended)
   - Select point generation method
   - Set spacing or point count
   - Add additional measurement points if needed (select vertices)

**5. Format Table**
   - Choose Detail Level: Standard or Details
   - Set table order: Ascending (tip to tail) or Descending (tail to tip)
   - Select units: mm, inches, or cm
   - Set decimal precision (significant figures)

**6. Generate Table**
   - Feature creates measurements automatically
   - Insert "QC Table" from Tables menu to display results
   - Table updates when feature regenerates

---

## Tips & Best Practices

**Station Placement:**
- Use **Minimal** boundary behavior for cleanest output (full data in stance area + endpoints)
- Use **Static Distance** with MRS center for symmetric spacing
- Use **Evenly Spaced on RSL** when focusing on stance area

**Detail Levels:**
- Start with **Standard** mode for quick checks
- Switch to **Details** when investigating specific issues
- Export to Excel/CSV for further analysis if needed

**Composite Parts:**
- Core composites (multiple bodies) are automatically handled
- System treats composite as unified geometry for measurements
- Sidewall must be single body (not composite)

**Troubleshooting:**
- Large Core/SW Δ values may indicate alignment issues
- Negative delta values (SW taller than core) should be investigated
- Missing measurements at station = no intersection at that X position

**Performance:**
- More stations = longer calculation time
- Minimal boundary behavior is fastest
- Use verbose debug mode to see measurement count

---

## Common Applications

**Daily Production QC:**
- Standard mode, Minimal boundary, 30-40 stations
- Quick verification of key dimensions
- Export table to production records

**Design Verification:**
- Details mode, Normal boundary, dense station spacing
- Compare actual vs. design specifications
- Analyze core top edge angles and base rout

**Troubleshooting:**
- Details mode with additional manual station points
- Investigate specific problem areas
- Compare delta values to identify misalignment

**Documentation:**
- Details mode for comprehensive records
- Include table in manufacturing drawings
- Archive for traceability

---

## Table Data Storage

The feature stores measurement data as an attribute on the origin. This allows:
- Multiple table inserts showing same data
- Table updates when feature regenerates
- Data persistence across Part Studio versions
- Export capability to external formats

To update the table after changes, simply regenerate the "Generate QC Table Data" feature.
