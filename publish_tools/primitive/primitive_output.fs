FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: primitive_frame.fs
export import(path : "5808546b3b3d863d82796d24", version : "11f0f0307ee2dcceaf62c9c1");

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
