FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: station_utils.fs
import(path : "8a8c023e223cf0814d973a63", version : "d4307c54b9d72c88604cc3fe");

/**
 * Station table: one table per Station geometry view ("4101 PLAN", "4501 PROFILE", ...), for the Part Studio's
 * table panel and for drawings (Insert > Custom table > this Part Studio > pick the table).
 *
 * Each view composite carries the attribute STATION_TABLE_ATTRIBUTE (station_utils.fs) with its rows, written
 * by Station geometry; the table finds every body with that attribute and a matching schema, so any number of
 * Station geometry features, parts and views are tabulated, each in its own table. Rows are sorted along the
 * measuring axis (x from the datum). Nothing is read from feature parameters, so the table follows every
 * regeneration.
 */
annotation { "Table Type Name" : "Station table" }
export const stationTable = defineTable(function(context is Context, definition is map) returns TableArray
    precondition
    {
        annotation { "Name" : "Views containing", "Default" : "", "MaxLength" : 128,
                    "Description" : "Only views whose name contains this text, e.g. 4101 PLAN (case matters). Empty = every view. A drawing inserts every table this returns." }
        definition.viewFilter is string;
    }
    {
        var views = [];
        for (var body in evaluateQuery(context, qHasAttribute(qEverything(EntityType.BODY), STATION_TABLE_ATTRIBUTE)))
        {
            const data = getAttribute(context, { "entity" : body, "name" : STATION_TABLE_ATTRIBUTE });
            if (data is map && data.schema == STATION_TABLE_SCHEMA && data.rows is array && titleMatches(data.title, definition.viewFilter))
            {
                views = append(views, { "data" : data, "body" : body });
            }
        }
        // Model order (the feature tree's); strings cannot be ordered with < in FeatureScript.
        var tables = [];
        for (var v in views)
        {
            tables = append(tables, viewTable(v.data, v.body));
        }
        return tableArray(tables);
    });

/**
 * True when `filter` is empty or `title` contains it (case matters: FeatureScript regex has no (?i)). Any character
 * other than a letter, digit, space, _ or - matches any one character, so the filter needs no regex escaping.
 */
function titleMatches(title is string, filter is string) returns boolean
{
    if (filter == "")
    {
        return true;
    }
    return match(title, ".*" ~ replace(filter, "[^A-Za-z0-9 _-]", ".") ~ ".*").hasMatch;
}

/** The size measured across each station: width in plan, thickness in profile, span in any other view. */
function spanHeading(view is string) returns string
{
    if (view == "PLAN")
    {
        return "Width (mm)";
    }
    if (view == "PROFILE")
    {
        return "Thickness (mm)";
    }
    return "Span (mm)";
}

function viewTable(data is map, body is Query) returns Table
{
    const columns = [
            tableColumnDefinition("station", "Station"),
            tableColumnDefinition("x", "x from datum (mm)", TableTextAlignment.RIGHT),
            tableColumnDefinition("span", spanHeading(data.view), TableTextAlignment.RIGHT),
            tableColumnDefinition("lo", "From (mm)", TableTextAlignment.RIGHT),
            tableColumnDefinition("hi", "To (mm)", TableTextAlignment.RIGHT)
        ];
    const ordered = sort(data.rows, function(a, b) { return a.x - b.x; });
    var rows = [];
    for (var r in ordered)
    {
        if (r.hit)
        {
            rows = append(rows, tableRow({ "station" : r.id, "x" : round2(r.x), "span" : round2(r.span),
                            "lo" : round2(r.lo), "hi" : round2(r.hi) }));
        }
        else
        {
            rows = append(rows, tableRow({ "station" : r.id, "x" : round2(r.x), "span" : "misses the part", "lo" : "", "hi" : "" }));
        }
    }
    return table(data.title ~ " stations", columns, rows, body);
}

function round2(value is number) returns number
{
    return round(value * 100) / 100;
}
