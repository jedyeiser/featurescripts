FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: primitive_types.fs
export import(path : "PRIMITIVE_TYPES_EID", version : "PRIMITIVE_TYPES_MV");

/**
 * Primitive tables: the tables of every "<prefix> PRIMITIVE" composite (Export primitive) in the Part Studio,
 * read from its attribute (schema primitive/1), so they follow every regeneration:
 *     1 Theoretical scale factors   Tip / Running surface / Tail length along bottom and top, top/bottom %
 *     2 Metadata                    RSL, dimensions, widths, radii, taper angles (deflection / stiffness: phase 2)
 *     3 Key locations               FCP ACP MRS MP(s) XS1 XS2 TIP TAIL: [x, y, z] and [s, w, h]
 *     5 Baseline                    FCPh FRCP FRCPl FB_Roll MCh MCl AB_Roll ARCPl ARCP ACPh (+ phase-2 rows)
 *     Data                          x, s, y, ski_width, z, ski_thck, baseline_height, radius within the RSL
 * A drawing inserts every table this returns: filter by primitive name and pick one table per insertion.
 */
annotation { "Table Type Name" : "Primitive tables" }
export const primitiveTables = defineTable(function(context is Context, definition is map) returns TableArray
    precondition
    {
        annotation { "Name" : "Primitives containing", "Default" : "", "MaxLength" : 128,
                    "Description" : "Only primitives whose name contains this text (case matters). Empty = every primitive." }
        definition.nameFilter is string;

        annotation { "Name" : "Table", "Default" : PrimitiveTableKind.ALL, "UIHint" : UIHint.SHOW_LABEL }
        definition.tableKind is PrimitiveTableKind;
    }
    {
        var tables = [];
        for (var body in evaluateQuery(context, qHasAttribute(qEverything(EntityType.BODY), PRIMITIVE_ATTRIBUTE)))
        {
            const data = getAttribute(context, { "entity" : body, "name" : PRIMITIVE_ATTRIBUTE });
            if (!(data is map) || data.schema != PRIMITIVE_SCHEMA || !nameMatches(data.title, definition.nameFilter))
            {
                continue;
            }
            const kind = definition.tableKind;
            if (kind == PrimitiveTableKind.ALL || kind == PrimitiveTableKind.SCALE_FACTORS)
            {
                tables = append(tables, scaleTable(data, body));
            }
            if (kind == PrimitiveTableKind.ALL || kind == PrimitiveTableKind.METADATA)
            {
                tables = append(tables, metadataTable(data, body));
            }
            if (kind == PrimitiveTableKind.ALL || kind == PrimitiveTableKind.KEY_LOCATIONS)
            {
                tables = append(tables, keyTable(data, body));
            }
            if (kind == PrimitiveTableKind.ALL || kind == PrimitiveTableKind.BASELINE)
            {
                tables = append(tables, baselineTable(data, body));
            }
            if (kind == PrimitiveTableKind.ALL || kind == PrimitiveTableKind.DATA)
            {
                tables = append(tables, dataTable(data, body));
            }
        }
        return tableArray(tables);
    });

/** True when `filter` is empty or `title` contains it (case matters; any character other than letters, digits, space, _ and - matches any one character -- correction 53). */
function nameMatches(title is string, filter is string) returns boolean
{
    if (filter == "")
    {
        return true;
    }
    return match(title, ".*" ~ replace(filter, "[^A-Za-z0-9 _-]", ".") ~ ".*").hasMatch;
}

function column(key is string, heading is string) returns TableColumnDefinition
{
    return tableColumnDefinition(key, heading, TableTextAlignment.CENTER);
}

/** A number to 2 decimals (mm), text unchanged. */
function cell2(value)
{
    return value is number ? round(value * 100) / 100 : value;
}

/** A number to `digits` decimals, text unchanged. */
function cellN(value, digits is number)
{
    if (!(value is number))
    {
        return value;
    }
    const f = 10 ^ digits;
    return round(value * f) / f;
}

function scaleTable(data is map, body is Query) returns Table
{
    var rows = [];
    for (var r in data.scaleFactors)
    {
        rows = append(rows, tableRow({ "region" : r.name, "bottom" : cell2(r.bottom), "top" : cell2(r.top), "ratio" : cell2(r.ratio) }));
    }
    return table(data.title ~ " - 1 Theoretical scale factors", [
                    column("region", "Region"), column("bottom", "Bottom (mm)"), column("top", "Top (mm)"), column("ratio", "Top / Bottom (%)")
                ], rows, body);
}

function metadataTable(data is map, body is Query) returns Table
{
    var rows = [];
    for (var r in data.metadata)
    {
        const digits = r.unit == "mm" ? 2 : 3;
        rows = append(rows, tableRow({ "item" : r.name, "value" : cellN(r.value, digits), "unit" : r.unit, "note" : r.note }));
    }
    return table(data.title ~ " - 2 Metadata", [
                    column("item", "Item"), column("value", "Value"), column("unit", "Unit"), column("note", "Definition")
                ], rows, body);
}

function keyTable(data is map, body is Query) returns Table
{
    var rows = [];
    for (var r in data.keyLocations)
    {
        rows = append(rows, tableRow({ "name" : r.name, "x" : cell2(r.x), "y" : cell2(r.y), "z" : cell2(r.z),
                        "s" : cell2(r.s), "w" : cell2(r.w), "h" : cell2(r.h) }));
    }
    return table(data.title ~ " - 3 Key locations", [
                    column("name", "Location"), column("x", "x (mm)"), column("y", "y (mm)"), column("z", "z (mm)"),
                    column("s", "s (mm)"), column("w", "w (mm)"), column("h", "h (mm)")
                ], rows, body);
}

function baselineTable(data is map, body is Query) returns Table
{
    var rows = [];
    for (var r in data.baseline)
    {
        rows = append(rows, tableRow({ "name" : r.name, "value" : cell2(r.value), "x" : cell2(r.x), "s" : cell2(r.s) }));
    }
    return table(data.title ~ " - 5 Baseline", [
                    column("name", "Measure"), column("value", "Value (mm)"), column("x", "x (mm)"), column("s", "s (mm)")
                ], rows, body);
}

function dataTable(data is map, body is Query) returns Table
{
    var rows = [];
    for (var r in data.data)
    {
        rows = append(rows, tableRow({ "x" : cell2(r.x), "s" : cell2(r.s), "y" : cell2(r.y), "skiWidth" : cell2(r.skiWidth),
                        "z" : cell2(r.z), "skiThck" : cell2(r.skiThck), "baselineHeight" : cell2(r.baselineHeight),
                        "radius" : cellN(r.radius, 3) }));
    }
    return table(data.title ~ " - Data (RSL)", [
                    column("x", "x (mm)"), column("s", "s (mm)"), column("y", "y (mm)"), column("skiWidth", "ski_width (mm)"),
                    column("z", "z (mm)"), column("skiThck", "ski_thck (mm)"), column("baselineHeight", "baseline_height (mm)"),
                    column("radius", "Radius (m)")
                ], rows, body);
}
