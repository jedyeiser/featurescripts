FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: primitive_frame.fs
export import(path : "5808546b3b3d863d82796d24", version : "c5a2ade5b038e795de4b90b3");

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

/** A radius-plot level (whole metres) as a key fragment ("P10", "M20", "P0"). */
function levelKey(level is number) returns string
{
    return (level < 0 ? "M" : "P") ~ abs(level);
}

/** A radius-plot level as a name / label ("+10", "-20", "0"). */
function levelName(level is number) returns string
{
    return level > 0 ? "+" ~ level : "" ~ level;
}

/**
 * The radius plot's levels (m): multiples of PRIMITIVE_RADIUS_GRID_STEP covering the plot heights lo..hi (relative
 * to the reference line) outward, never beyond the radius limit. Always holds 0.
 */
export function primitiveRadiusLevels(lo is ValueWithUnits, hi is ValueWithUnits, radiusLimit is ValueWithUnits) returns array
{
    const step = PRIMITIVE_RADIUS_GRID_STEP;
    const perMetre = PRIMITIVE_RADIUS_PLOT_SCALE * meter;
    const cap = floor(radiusLimit / meter / step + 1e-9) * step;
    const top = min(cap, max(0, ceil(hi / perMetre / step - 1e-9) * step));
    const bottom = max(-cap, min(0, floor(lo / perMetre / step + 1e-9) * step));
    var levels = [];
    for (var level = bottom; level <= top; level += step)
    {
        levels = append(levels, level);
    }
    return levels;
}

/**
 * The radius band's frame in the LOCAL XZ plane: a vertical axis at each end of the band (x = xLo / xHi, named by the
 * ski end it is on: "RADIUS AXIS TIP" / "RADIUS AXIS TAIL") over the levels, a PRIMITIVE_AXIS_TICK tick outward at
 * every level on both axes ("RADIUS TICK +10 TIP"), optionally a dashed grid line at every level but 0 ("RADIUS GRID
 * -20", PRIMITIVE_GRID_DASH dashes, PRIMITIVE_GRID_GAP apart) and, with a text height, tick labels left of the low-x
 * axis ("RADIUS LABEL +10"). Operation ids and names come from the level and the end, never from list positions.
 * Returns the bodies (all grey).
 */
export function primitiveRadiusFrame(context is Context, id is Id, levels is array, xLo is ValueWithUnits, xHi is ValueWithUnits,
    zRef is ValueWithUnits, dirSign is number, dashed is boolean, textHeight, prefix is string) returns array
{
    const zero = 0 * meter;
    const perMetre = PRIMITIVE_RADIUS_PLOT_SCALE * meter;
    var bodies = [];
    if (size(levels) < 2)
    {
        return bodies;
    }
    const zLo = zRef + levels[0] * perMetre;
    const zHi = zRef + levels[size(levels) - 1] * perMetre;
    // The tip is at high x when the ski points +X.
    const ends = [{ "end" : dirSign > 0 ? "TAIL" : "TIP", "x" : xLo, "out" : -1 },
                  { "end" : dirSign > 0 ? "TIP" : "TAIL", "x" : xHi, "out" : 1 }];
    for (var e in ends)
    {
        bodies = append(bodies, primitiveSegment(context, id + ("axis" ~ e.end), vector(e.x, zero, zLo), vector(e.x, zero, zHi),
                    prefix ~ " RADIUS AXIS " ~ e.end));
        for (var level in levels)
        {
            const z = zRef + level * perMetre;
            bodies = append(bodies, primitiveSegment(context, id + ("tick" ~ levelKey(level) ~ e.end), vector(e.x, zero, z),
                        vector(e.x + e.out * PRIMITIVE_AXIS_TICK, zero, z), prefix ~ " RADIUS TICK " ~ levelName(level) ~ " " ~ e.end));
        }
    }
    if (dashed)
    {
        const pitch = PRIMITIVE_GRID_DASH + PRIMITIVE_GRID_GAP;
        const count = floor((xHi - xLo + PRIMITIVE_GRID_GAP) / pitch);
        for (var level in levels)
        {
            if (level == 0 || count < 1)
            {
                continue;
            }
            const z = zRef + level * perMetre;
            const gid = id + ("grid" ~ levelKey(level));
            const dash = primitiveSegment(context, gid + "dash", vector(xLo, zero, z), vector(xLo + PRIMITIVE_GRID_DASH, zero, z),
                prefix ~ " RADIUS GRID " ~ levelName(level));
            // Name and colour first: the pattern copies them.
            primitiveColour(context, dash, PRIMITIVE_COLOURS.frame);
            bodies = append(bodies, dash);
            var transforms = [];
            var names = [];
            for (var k = 1; k < count; k += 1)
            {
                transforms = append(transforms, transform(vector(k * pitch, zero, zero)));
                names = append(names, "d" ~ k);
            }
            if (size(transforms) > 0)
            {
                opPattern(context, gid + "copies", { "entities" : dash, "transforms" : transforms, "instanceNames" : names });
                bodies = append(bodies, qCreatedBy(gid + "copies", EntityType.BODY));
            }
        }
    }
    if (textHeight is ValueWithUnits)
    {
        for (var level in levels)
        {
            bodies = append(bodies, primitiveText(context, id + ("label" ~ levelKey(level)), levelName(level),
                        vector(xLo - PRIMITIVE_AXIS_TICK - textHeight / 4, zero, zRef + level * perMetre), textHeight, "RIGHT",
                        prefix ~ " RADIUS LABEL " ~ levelName(level)));
        }
    }
    primitiveColour(context, qUnion(bodies), PRIMITIVE_COLOURS.frame);
    return bodies;
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
