FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: primitive_types.fs
export import(path : "ecde24520874030ab412c981", version : "1d8cc9e2710fb3b719800884");
// IMPORT: primitive_tables_icon.svg (table icon)
IconNamespace::import(path : "eb32ed1a7e9ecf0a7ef61a7c", version : "a0113143f1b8cd5ca25093fa");

/**
 * Primitive tables: the tables of every "<prefix> PRIMITIVE" composite (Export primitive) in the Part Studio,
 * read from its attribute (schema primitive/1), so they follow every regeneration:
 *     1 Theoretical scale factors   Tip / Running surface / Tail length along bottom and top, top/bottom %
 *     2 Metadata                    RSL, dimensions, widths, radii, taper angles; deflection / stiffness with a target EI
 *     3 Key locations               FCP ACP MRS MP(s) XS1 XS2 TIP TAIL + extra key points by x (ascending): x, s and the
 *                                   distance from the tail (|x - x(TAIL)|; y z w h stay in the attribute only)
 *     4 SW rout                     (with a SW rout surface) angle to Z, step-in, distance above base at MRS; start / stop
 *                                   x, s, distance from the tail
 *     5 Baseline                    Tip / Tail block (when named), FCPh FRCP FRCPl FB_Roll MCh MCl AB_Roll ARCPl ARCP ACPh
 *                                   (not on a baseline that is flat within the RSL)
 *     6 RSL data                    x, s, y, ski_width, z, ski_thck, baseline_height, radius within the RSL, by x
 * x from the datum; s = distance along the bottom wire from the datum, same direction as x.
 * With "Show # column in RSL data" on (Export primitive), RSL data starts with a # column: 0 at the lowest x; a row at
 * a key location shows the key's name (e.g. "MRS", several joined with "/") instead of its number.
 * Only rows with data are stored, and a table without rows is not returned.
 * A drawing inserts every table this returns: filter by primitive name and pick one table per insertion.
 */
annotation { "Table Type Name" : "Primitive tables", "Icon" : IconNamespace::BLOB_DATA }
export const primitiveTables = defineTable(function(context is Context, definition is map) returns TableArray
    precondition
    {
        annotation { "Name" : "Primitives containing", "Default" : "", "MaxLength" : 128,
                    "Description" : "Only primitives whose name contains this text (case matters). Empty = every primitive." }
        definition.nameFilter is string;

        annotation { "Name" : "Table", "Default" : PrimitiveTableKind.ALL, "UIHint" : [UIHint.SHOW_LABEL],
                    "Description" : "Which table to return; a drawing inserts every table returned, so pick one per insertion. 4 SW rout exists only for a primitive with a SW rout surface." }
        definition.tableKind is PrimitiveTableKind;

        annotation { "Name" : "Show definitions", "Default" : false,
                    "Description" : "Adds the Definition column to 2 Metadata (how each value is defined). Off keeps drawings compact; the definitions stay in the published data." }
        definition.showDefinitions is boolean;
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
                tables = appendTable(tables, scaleTable(data, body));
            }
            if (kind == PrimitiveTableKind.ALL || kind == PrimitiveTableKind.METADATA)
            {
                tables = appendTable(tables, metadataTable(data, body, definition.showDefinitions == true));
            }
            if (kind == PrimitiveTableKind.ALL || kind == PrimitiveTableKind.KEY_LOCATIONS)
            {
                tables = appendTable(tables, keyTable(data, body));
            }
            if (kind == PrimitiveTableKind.ALL || kind == PrimitiveTableKind.SW_ROUT)
            {
                tables = appendTable(tables, swRoutTable(data, body));
            }
            if (kind == PrimitiveTableKind.ALL || kind == PrimitiveTableKind.BASELINE)
            {
                tables = appendTable(tables, baselineTable(data, body));
            }
            if (kind == PrimitiveTableKind.ALL || kind == PrimitiveTableKind.DATA)
            {
                tables = appendTable(tables, dataTable(data, body));
            }
        }
        return tableArray(tables);
    });

/** `tables` plus `t` unless it has no rows. */
function appendTable(tables is array, t is Table) returns array
{
    return size(t.rows) == 0 ? tables : append(tables, t);
}

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

function metadataTable(data is map, body is Query, showDefinitions is boolean) returns Table
{
    var rows = [];
    for (var r in data.metadata)
    {
        const digits = r.unit == "mm" ? 2 : 3;
        rows = append(rows, tableRow({ "item" : r.name, "value" : cellN(r.value, digits), "unit" : r.unit, "note" : r.note }));
    }
    var columns = [column("item", "Item"), column("value", "Value"), column("unit", "Unit")];
    if (showDefinitions)
    {
        columns = append(columns, column("note", "Definition"));
    }
    return table(data.title ~ " - 2 Metadata", columns, rows, body);
}

/** True when the primitive asks for the # column (RSL data only) and its rows carry numbers. */
function showStations(data is map, rows is array) returns boolean
{
    return data.settings is map && data.settings.stationNumbers == true && size(rows) > 0 && rows[0].station is number;
}

/** `columns` with the station # column in front when `show`. */
function withStation(columns is array, show is boolean) returns array
{
    return show ? concatenateArrays([[column("station", "#")], columns]) : columns;
}

/** Key locations (2026-09-29, user): Location | x | s | Dist. from tail; y z w h stay in the attribute rows only. */
function keyTable(data is map, body is Query) returns Table
{
    var rows = [];
    for (var r in data.keyLocations)
    {
        rows = append(rows, tableRow({ "name" : r.name, "x" : cell2(r.x), "s" : cell2(r.s), "distFromTail" : cell2(r.distFromTail) }));
    }
    return table(data.title ~ " - 3 Key locations", [
                    column("name", "Location"), column("x", "x (mm)"), column("s", "s (mm)"), column("distFromTail", "Dist. from tail (mm)")
                ], rows, body);
}

/** A number as text with exactly one decimal ("7.0"; user: the SW rout angle in degrees, 1 decimal), text unchanged. */
function fixed1(value)
{
    if (!(value is number))
    {
        return value;
    }
    const tenths = round(abs(value) * 10);
    return ((value < 0 && tenths > 0) ? "-" : "") ~ floor(tenths / 10) ~ "." ~ (tenths % 10);
}

/**
 * SW rout (2026-09-29): angle (deg, 1 decimal), step-in and distance above base (mm) measured at MRS, with the MRS
 * position; start / stop with their position only. No rows (no table) without a SW rout surface.
 */
function swRoutTable(data is map, body is Query) returns Table
{
    var rows = [];
    if (data.swRout is array)
    {
        for (var r in data.swRout)
        {
            rows = append(rows, tableRow({ "name" : r.name, "value" : r.unit == "deg" ? fixed1(r.value) : cellN(r.value, 2), "unit" : r.unit,
                            "x" : cell2(r.x), "s" : cell2(r.s), "distFromTail" : cell2(r.distFromTail) }));
        }
    }
    return table(data.title ~ " - 4 SW rout", [
                    column("name", "Measure"), column("value", "Value"), column("unit", "Unit"), column("x", "x (mm)"),
                    column("s", "s (mm)"), column("distFromTail", "Dist. from tail (mm)")
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

/** The # cell of an RSL data row: the key name(s) at a key location, else the row number. */
function dataRowLabel(r is map)
{
    if (r.name is string && r.name != "")
    {
        return r.name;
    }
    return r.station is number ? r.station : "";
}

function dataTable(data is map, body is Query) returns Table
{
    var rows = [];
    for (var r in data.data)
    {
        rows = append(rows, tableRow({ "station" : dataRowLabel(r), "x" : cell2(r.x), "s" : cell2(r.s), "y" : cell2(r.y), "skiWidth" : cell2(r.skiWidth),
                        "z" : cell2(r.z), "skiThck" : cell2(r.skiThck), "baselineHeight" : cell2(r.baselineHeight),
                        "radius" : cellN(r.radius, 3) }));
    }
    // Short headings keep the columns narrow (a custom table can't set column widths): lengths in mm, radius in m.
    return table(data.title ~ " - 6 RSL data (mm)", withStation([
                    column("x", "x"), column("s", "s"), column("y", "y"), column("skiWidth", "w"),
                    column("z", "z"), column("skiThck", "thck"), column("baselineHeight", "baseline"),
                    column("radius", "radius (m)")
                ], showStations(data, data.data)), rows, body);
}
