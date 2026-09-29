FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: primitive_frame.fs
export import(path : "5808546b3b3d863d82796d24", version : "0c44c88b4829c8f3e72eb25d");

/**
 * Export Primitive -- output geometry helpers: named point bodies and segments, band stacking and the closed
 * "<prefix> PRIMITIVE" composite with its data attribute. Operation ids are built from names (keys), never from
 * list positions, so adding a station does not re-bind another one's drawing references.
 */

export function primitiveName(context is Context, bodies is Query, name is string)
{
    setProperty(context, { "entities" : bodies, "propertyType" : PropertyType.NAME, "value" : name });
}

/** A point body (not a mate connector: a composite refuses those, correction 19). */
export function primitivePointBody(context is Context, id is Id, point is Vector, name is string) returns Query
{
    opPoint(context, id, { "point" : point });
    const body = qCreatedBy(id, EntityType.BODY);
    primitiveName(context, body, name);
    return body;
}

/** A straight wire from p0 to p1 (undefined-safe: returns qNothing() for a zero-length segment). */
export function primitiveSegment(context is Context, id is Id, p0 is Vector, p1 is Vector, name is string) returns Query
{
    if (norm(p1 - p0) < 1e-7 * meter)
    {
        return qNothing();
    }
    opFitSpline(context, id, { "points" : [p0, p1] });
    const body = qCreatedBy(id, EntityType.BODY);
    primitiveName(context, body, name);
    return body;
}

/**
 * Band reference heights, top to bottom: the first band's highest point sits `gap` below `top`, and each next band's
 * highest point `gap` below the previous band's lowest. Each extent is { lo, hi } relative to the band's own
 * reference; the returned offsets are whole millimetres.
 */
export function primitiveStack(extents is array, top is ValueWithUnits, gap is ValueWithUnits) returns array
{
    var offsets = [];
    var ceiling = top;
    for (var e in extents)
    {
        const z = floor((ceiling - gap - e.hi) / millimeter) * millimeter;
        offsets = append(offsets, z);
        ceiling = z + e.lo;
    }
    return offsets;
}

/** { lo, hi } of the bodies' z in the local frame (empty -> 0, 0). */
export function primitiveZExtent(context is Context, bodies is Query) returns map
{
    if (isQueryEmpty(context, bodies))
    {
        return { "lo" : 0 * meter, "hi" : 0 * meter };
    }
    const bb = evBox3d(context, { "topology" : bodies, "tight" : true });
    return { "lo" : bb.minCorner[2], "hi" : bb.maxCorner[2] };
}

/** Sets the Part Studio appearance of the bodies (wires and points keep it). */
export function primitiveColour(context is Context, bodies is Query, colour is Color)
{
    if (!isQueryEmpty(context, bodies))
    {
        setProperty(context, { "entities" : bodies, "propertyType" : PropertyType.APPEARANCE, "value" : colour });
    }
}

/**
 * Text as geometry in the LOCAL XZ plane (read from -Y, i.e. a front view): the text's outline loops as wire
 * bodies, named `name`. A sketch text's own curves overlap, so the loops are extracted from its region faces'
 * edges; the sketch bodies are deleted. `align` "LEFT" / "RIGHT" / "CENTER" puts that side of the text at anchor x;
 * the text box (height `height`) is centred on anchor z. Returns the wire bodies (qNothing() for no text).
 */
export function primitiveText(context is Context, id is Id, text is string, anchor is Vector, height is ValueWithUnits,
    align is string, name is string) returns Query
{
    const sketchId = id + "sketch";
    const sketch = newSketchOnPlane(context, sketchId, { "sketchPlane" : plane(vector(0, 0, 0) * meter, vector(0, -1, 0), vector(1, 0, 0)) });
    skText(sketch, "text", { "text" : text, "fontName" : "OpenSans-Regular.ttf",
                "firstCorner" : vector(anchor[0], anchor[2] - height / 2), "secondCorner" : vector(anchor[0] + height, anchor[2] + height / 2) });
    skSolve(sketch);
    const sketchBodies = qCreatedBy(sketchId, EntityType.BODY);
    const regionEdges = qOwnedByBody(qBodyType(sketchBodies, BodyType.SHEET), EntityType.EDGE);
    if (isQueryEmpty(context, regionEdges))
    {
        opDeleteBodies(context, id + "deleteSketch", { "entities" : sketchBodies });
        return qNothing();
    }
    opExtractWires(context, id + "wires", { "edges" : regionEdges });
    opDeleteBodies(context, id + "deleteSketch", { "entities" : sketchBodies });
    const wires = qCreatedBy(id + "wires", EntityType.BODY);
    if (align != "LEFT")
    {
        const bb = evBox3d(context, { "topology" : wires, "tight" : true });
        const shift = align == "RIGHT" ? anchor[0] - bb.maxCorner[0] : anchor[0] - (bb.minCorner[0] + bb.maxCorner[0]) / 2;
        opTransform(context, id + "align", { "bodies" : wires, "transform" : transform(vector(shift, 0 * meter, 0 * meter)) });
    }
    primitiveName(context, wires, name);
    return wires;
}

/**
 * primitiveText with superscripts: "^" makes the next character a superscript (0.6 of the height, raised), e.g.
 * "EI (N*m^2)" -> N*m followed by a small raised 2 (sketch text has no rich formatting; code stays ASCII). The pieces
 * are laid out left to right from their measured extents, then the whole label is aligned like primitiveText.
 */
export function primitiveLabel(context is Context, id is Id, text is string, anchor is Vector, height is ValueWithUnits,
    align is string, name is string) returns Query
{
    var pieces = [];
    var rest = text;
    while (true)
    {
        const m = match(rest, "([^\\^]*)\\^(.)(.*)");
        if (!m.hasMatch)
        {
            if (rest != "")
            {
                pieces = append(pieces, { "text" : rest, "sup" : false });
            }
            break;
        }
        if (m.captures[1] != "")
        {
            pieces = append(pieces, { "text" : m.captures[1], "sup" : false });
        }
        pieces = append(pieces, { "text" : m.captures[2], "sup" : true });
        rest = m.captures[3];
    }
    if (size(pieces) == 1 && !pieces[0].sup)
    {
        return primitiveText(context, id, text, anchor, height, align, name);
    }
    var x = anchor[0];
    var bodies = [];
    for (var k = 0; k < size(pieces); k += 1)
    {
        const sup = pieces[k].sup;
        const piece = primitiveText(context, id + ("part" ~ k), pieces[k].text,
            vector(x, anchor[1], sup ? anchor[2] + height * 0.25 : anchor[2]), sup ? height * 0.6 : height, "LEFT", name);
        if (!isQueryEmpty(context, piece))
        {
            x = evBox3d(context, { "topology" : piece, "tight" : true }).maxCorner[0] + height * 0.06;
            bodies = append(bodies, piece);
        }
    }
    const all = qUnion(bodies);
    if (align != "LEFT" && size(bodies) > 0)
    {
        const bb = evBox3d(context, { "topology" : all, "tight" : true });
        const shift = align == "RIGHT" ? anchor[0] - bb.maxCorner[0] : anchor[0] - (bb.minCorner[0] + bb.maxCorner[0]) / 2;
        opTransform(context, id + "align", { "bodies" : all, "transform" : transform(vector(shift, 0 * meter, 0 * meter)) });
    }
    return all;
}

/** |level| (whole level units) as text with `digits` decimals: 5, 2 -> "0.05"; 10, 0 -> "10". */
function levelDigits(level is number, digits is number) returns string
{
    const n = abs(round(level));
    if (digits <= 0 || n == 0)
    {
        return "" ~ n;
    }
    const f = round(10 ^ digits);
    var frac = "" ~ (n % f);
    while (length(frac) < digits)
    {
        frac = "0" ~ frac;
    }
    return floor(n / f) ~ "." ~ frac;
}

/** A scale level as a key fragment ("P10", "M20", "P0"; with decimals "P0_05"). */
function levelKey(level is number, digits is number) returns string
{
    return (level < 0 ? "M" : "P") ~ replace(levelDigits(level, digits), "[.]", "_");
}

/** A scale level as a body name fragment ("+10", "-20", "0", "+0.05"). */
function levelName(level is number, digits is number) returns string
{
    if (round(level) == 0)
    {
        return levelDigits(0, digits);
    }
    return (level > 0 ? "+" : "-") ~ levelDigits(level, digits);
}

/** A scale level as label text: levelName, or without the "+" when `signed` is false ("150", "0.05", "-0.02"). */
function levelText(level is number, digits is number, signed is boolean) returns string
{
    return (signed || level < 0) ? levelName(level, digits) : levelDigits(level, digits);
}

/**
 * A scale's levels: multiples of `step` covering lo..hi (plotted values, e.g. m of radius or N*m^2 of EI) outward,
 * never beyond +-cap. Always holds 0.
 */
export function primitiveLevels(lo is number, hi is number, step is number, cap is number) returns array
{
    const limit = floor(cap / step + 1e-9) * step;
    const top = min(limit, max(0, ceil(hi / step - 1e-9) * step));
    const bottom = max(-limit, min(0, floor(lo / step + 1e-9) * step));
    var levels = [];
    for (var level = bottom; level <= top; level += step)
    {
        levels = append(levels, level);
    }
    return levels;
}

/**
 * A fixed axis's levels (2026-09-29): the multiples of `step` from lo to hi (level units, lo <= 0 <= hi), both ends
 * included within 1e-9. Always holds 0.
 */
export function primitiveLevelsWithin(lo is number, hi is number, step is number) returns array
{
    const first = min(0, ceil(lo / step - 1e-9) * step);
    const last = max(0, floor(hi / step + 1e-9) * step);
    var levels = [];
    for (var level = first; level <= last + 1e-9 * step; level += step)
    {
        levels = append(levels, round(level / step) * step);
    }
    return levels;
}

/** The first of 1, 2, 5, 10, 20, 50, ... that is a multiple of `step` and at least `least` (whole level units). */
export function primitiveNiceStep(step is number, least is number) returns number
{
    var decade = 1;
    while (true)
    {
        for (var m in [1, 2, 5])
        {
            const candidate = m * decade;
            if (candidate >= step && candidate >= least && candidate % step == 0)
            {
                return candidate;
            }
        }
        decade *= 10;
    }
}

/**
 * Auto scale (2026-09-29): the axis for a band whose largest plotted value is `maxValue` (level units, > 0): top =
 * k * step with step one of `steps` or a multiple of one by 10, 100, ... and k = the fewest steps that keep maxValue
 * at most PRIMITIVE_AUTO_FILL_HI of top (at least 2). Of all steps, in this order: k <= 10; maxValue / top within
 * PRIMITIVE_AUTO_FILL_LO .. HI; 4 <= k <= 8; then, inside that range, the LARGEST step (fewest ticks), outside it the
 * fill nearest the middle of the range. Returns { step, top, fill } (fill = maxValue / top).
 */
export function primitiveAutoAxis(maxValue is number, steps is array) returns map
{
    const aim = (PRIMITIVE_AUTO_FILL_LO + PRIMITIVE_AUTO_FILL_HI) / 2;
    var best = undefined;
    var decade = 1;
    while (true)
    {
        for (var m in steps)
        {
            const step = m * decade;
            const k = max(2, ceil(maxValue / (PRIMITIVE_AUTO_FILL_HI * step) - 1e-9));
            const fill = maxValue / (k * step);
            const candidate = { "step" : step, "top" : k * step, "fill" : fill,
                    "allowed" : k <= 10, "inRange" : fill >= PRIMITIVE_AUTO_FILL_LO - 1e-9 && fill <= PRIMITIVE_AUTO_FILL_HI + 1e-9,
                    "preferred" : k >= 4 && k <= 8 };
            if (best == undefined || betterAxis(candidate, best, aim))
            {
                best = candidate;
            }
        }
        // Larger decades only give k = 2 with a lower fill.
        if (min(steps) * decade * 2 >= maxValue)
        {
            break;
        }
        decade *= 10;
    }
    return { "step" : best.step, "top" : best.top, "fill" : best.fill };
}

/** primitiveAutoAxis's order: true when candidate a beats b. */
function betterAxis(a is map, b is map, aim is number) returns boolean
{
    for (var key in ["allowed", "inRange", "preferred"])
    {
        if (a[key] != b[key])
        {
            return a[key];
        }
    }
    if (a.inRange)
    {
        return a.step > b.step;
    }
    return abs(a.fill - aim) < abs(b.fill - aim) - 1e-12;
}

/**
 * The label step (whole level units, a multiple of `step`) that keeps a scale's numbers at least 1.25 label heights
 * apart: `step` itself when they already are (radius 10 m = 100 mm, EI 50 N*m^2 = 25 mm at the default scales).
 */
export function primitiveLabelStep(step is number, perLevel is ValueWithUnits, labelHeight) returns number
{
    if (!(labelHeight is ValueWithUnits) || step * perLevel >= 1.25 * labelHeight)
    {
        return step;
    }
    return primitiveNiceStep(step, ceil(1.25 * labelHeight / perLevel - 1e-9));
}

/**
 * A grid or key line: ONE straight wire from `start` to `end` named `name`, light grey (PRIMITIVE_COLOURS.grid) in
 * the Part Studio. Drawings restyle it (dashed / colour) themselves (2026-09-29: one edge per line instead of hundreds
 * of short dash segments). Returns the body (qNothing() for a zero-length line).
 */
export function primitiveGridLine(context is Context, id is Id, start is Vector, end is Vector, name is string) returns Query
{
    const body = primitiveSegment(context, id, start, end, name);
    primitiveColour(context, body, PRIMITIVE_COLOURS.grid);
    return body;
}

/**
 * A scale band's frame in the LOCAL XZ plane (band "RADIUS", "CURVATURE" or "EI"): a vertical axis at each end of the
 * band (x = xLo / xHi, named by the ski end it is on: "<band> AXIS TIP" / "<band> AXIS TAIL") over the levels (or from
 * format.axisLo to format.axisHi, level units, when given: the fixed axes of 2026-09-29), a
 * PRIMITIVE_AXIS_TICK tick outward at every level on both axes ("<band> TICK +10 TIP"), optionally a grid line at
 * every level but 0 ("<band> GRID -20", one light-grey edge across the band, primitiveGridLine) and, with a text height,
 * the level numbers left of the low-x axis ("<band> LABEL +10"). Levels are whole level units, `perLevel` = plot height
 * of one unit. `format` = { digits (decimals of one level unit: 0 for radius m / EI N*m^2, 2 for curvature in 0.01
 * 1/m), signed (the label TEXT keeps its "+"; body names always do), labelStep (numbers only on its multiples),
 * labelTop (optional, auto scale: this level is always numbered and numbers closer than 1.25 text heights to it are
 * left out) }.
 * Operation ids and names come from the level and the end, never from list positions. Returns the bodies (grey; the
 * grid lines light grey).
 */
export function primitiveScaleFrame(context is Context, id is Id, band is string, levels is array, perLevel is ValueWithUnits,
    xLo is ValueWithUnits, xHi is ValueWithUnits, zRef is ValueWithUnits, dirSign is number, grid is boolean, textHeight,
    prefix is string, format is map) returns array
{
    const zero = 0 * meter;
    const digits = format.digits;
    var bodies = [];
    if (size(levels) < 2)
    {
        return bodies;
    }
    const zLo = zRef + (format.axisLo is number ? min(format.axisLo, levels[0]) : levels[0]) * perLevel;
    const zHi = zRef + (format.axisHi is number ? max(format.axisHi, levels[size(levels) - 1]) : levels[size(levels) - 1]) * perLevel;
    // The tip is at high x when the ski points +X.
    const ends = [{ "end" : dirSign > 0 ? "TAIL" : "TIP", "x" : xLo, "out" : -1 },
                  { "end" : dirSign > 0 ? "TIP" : "TAIL", "x" : xHi, "out" : 1 }];
    for (var e in ends)
    {
        bodies = append(bodies, primitiveSegment(context, id + ("axis" ~ e.end), vector(e.x, zero, zLo), vector(e.x, zero, zHi),
                    prefix ~ " " ~ band ~ " AXIS " ~ e.end));
        for (var level in levels)
        {
            const z = zRef + level * perLevel;
            bodies = append(bodies, primitiveSegment(context, id + ("tick" ~ levelKey(level, digits) ~ e.end), vector(e.x, zero, z),
                        vector(e.x + e.out * PRIMITIVE_AXIS_TICK, zero, z), prefix ~ " " ~ band ~ " TICK " ~ levelName(level, digits) ~ " " ~ e.end));
        }
    }
    if (textHeight is ValueWithUnits)
    {
        for (var level in levels)
        {
            var show = round(level) % format.labelStep == 0;
            // Auto scale: the axis top is always numbered; a number too close below it gives way.
            if (format.labelTop is number)
            {
                if (abs(level - format.labelTop) < 1e-9)
                {
                    show = true;
                }
                else if (abs(level - format.labelTop) * perLevel < 1.25 * textHeight)
                {
                    show = false;
                }
            }
            if (!show)
            {
                continue;
            }
            bodies = append(bodies, primitiveText(context, id + ("label" ~ levelKey(level, digits)), levelText(level, digits, format.signed),
                        vector(xLo - PRIMITIVE_AXIS_TICK - textHeight / 4, zero, zRef + level * perLevel), textHeight, "RIGHT",
                        prefix ~ " " ~ band ~ " LABEL " ~ levelName(level, digits)));
        }
    }
    primitiveColour(context, qUnion(bodies), PRIMITIVE_COLOURS.frame);
    if (grid)
    {
        for (var level in levels)
        {
            if (level == 0)
            {
                continue;
            }
            const z = zRef + level * perLevel;
            bodies = append(bodies, primitiveGridLine(context, id + ("grid" ~ levelKey(level, digits)), vector(xLo, zero, z),
                        vector(xHi, zero, z), prefix ~ " " ~ band ~ " GRID " ~ levelName(level, digits)));
        }
    }
    return bodies;
}

/**
 * The target EI in the LOCAL XZ plane: every EI edge (xSection convention: world x along the ski, z in mm = EI in
 * N*m^2; negative values read as 0) sampled at 41 points and fitted through (x local, zRef + EI * perUnit), so steps
 * between edges stay steps. x local = the datum frame's x of the world point (x, 0, 0). The plot is cut to the band's
 * fixed frame (2026-09-29): frameLo <= x <= frameHi, EI <= eiMax (it breaks above). Returns { bodies, hi (largest EI,
 * N*m^2, before the cut), xLo, xHi (local x range of the samples; undefined without edges) }.
 */
export function primitiveEIPlot(context is Context, id is Id, edges is Query, toLocal is Transform, zRef is ValueWithUnits,
    perUnit is ValueWithUnits, frameLo is ValueWithUnits, frameHi is ValueWithUnits, eiMax is number) returns map
{
    const zero = 0 * meter;
    var params = [];
    for (var k = 0; k <= 40; k += 1)
    {
        params = append(params, k / 40);
    }
    var bodies = [];
    var hi = 0;
    var xLo = undefined;
    var xHi = undefined;
    var index = 0;
    for (var edge in evaluateQuery(context, edges))
    {
        var samples = [];
        for (var tl in evEdgeTangentLines(context, { "edge" : edge, "parameters" : params }))
        {
            const ei = max(0, tl.origin[2] / millimeter);
            const x = (toLocal * vector(tl.origin[0], zero, zero))[0];
            hi = max(hi, ei);
            xLo = xLo == undefined ? x : min(xLo, x);
            xHi = xHi == undefined ? x : max(xHi, x);
            samples = append(samples, [x, ei]);
        }
        index += 1;
        const pieces = primitiveClipPolyline(samples, frameLo, frameHi, 0, eiMax);
        for (var r = 0; r < size(pieces); r += 1)
        {
            var points = [];
            for (var s in pieces[r])
            {
                const q = vector(s[0], zero, zRef + s[1] * perUnit);
                if (size(points) == 0 || norm(q - points[size(points) - 1]) > 1e-8 * meter)
                {
                    points = append(points, q);
                }
            }
            if (size(points) < 2 || norm(points[size(points) - 1] - points[0]) < 1e-7 * meter)
            {
                continue;
            }
            // The EI wire's edges carry no names: the id follows their (query) order; a second piece of an edge cut
            // by EI axis max gets a suffix.
            const eid = id + ("edge" ~ index ~ (r == 0 ? "" : "_" ~ r));
            opFitSpline(context, eid, { "points" : points });
            bodies = append(bodies, qCreatedBy(eid, EntityType.BODY));
        }
    }
    return { "bodies" : bodies, "hi" : hi, "xLo" : xLo, "xHi" : xHi };
}

/**
 * The largest EI (N*m^2) primitiveEIPlot would plot: over the same 41 samples per edge, those with local x in
 * frameLo..frameHi (auto scale's maximum). 0 without samples there.
 */
export function primitiveEIMax(context is Context, edges is Query, toLocal is Transform, frameLo is ValueWithUnits, frameHi is ValueWithUnits) returns number
{
    const zero = 0 * meter;
    var params = [];
    for (var k = 0; k <= 40; k += 1)
    {
        params = append(params, k / 40);
    }
    var hi = 0;
    for (var edge in evaluateQuery(context, edges))
    {
        for (var tl in evEdgeTangentLines(context, { "edge" : edge, "parameters" : params }))
        {
            const x = (toLocal * vector(tl.origin[0], zero, zero))[0];
            if (x >= frameLo && x <= frameHi)
            {
                hi = max(hi, tl.origin[2] / millimeter);
            }
        }
    }
    return hi;
}

/**
 * The closed composite of every generated body, named "<prefix> PRIMITIVE", excluded from the BOM and tagged with
 * the data attribute (schema primitive/1).
 */
export function primitiveComposite(context is Context, id is Id, members is Query, title is string, data is map) returns Query
{
    opCreateCompositePart(context, id, { "bodies" : qBodyType(members, [BodyType.WIRE, BodyType.POINT, BodyType.SHEET, BodyType.SOLID]), "closed" : true });
    const composite = qBodyType(qCreatedBy(id, EntityType.BODY), BodyType.COMPOSITE);
    primitiveName(context, composite, title);
    setProperty(context, { "entities" : composite, "propertyType" : PropertyType.EXCLUDE_FROM_BOM, "value" : true });
    setAttribute(context, { "entities" : composite, "name" : PRIMITIVE_ATTRIBUTE, "attribute" : data });
    return composite;
}
