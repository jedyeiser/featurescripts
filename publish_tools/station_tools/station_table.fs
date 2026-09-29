FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: station_utils.fs
export import(path : "8a8c023e223cf0814d973a63", version : "33837d750b6c0df3aee6127e");
// IMPORT: station_table_icon.svg (table icon)
IconNamespace::import(path : "87946e777d7b592c8d681392", version : "a856cda4eb0b4d440499a4d4");

/** Heading language: the Station definition's, or forced here (one model on an English and a German drawing). */
export enum StationTableLanguage
{
    annotation { "Name" : "Same as Station definition" }
    AS_DEFINED,
    annotation { "Name" : "English" }
    ENGLISH,
    annotation { "Name" : "Deutsch" }
    GERMAN
}

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
annotation { "Table Type Name" : "Station table", "Icon" : IconNamespace::BLOB_DATA }
export const stationTable = defineTable(function(context is Context, definition is map) returns TableArray
    precondition
    {
        annotation { "Name" : "Views containing", "Default" : "", "MaxLength" : 128,
                    "Description" : "Only views whose name contains this text, e.g. 4101 PLAN (case matters). Empty = every view. A drawing inserts every table this returns." }
        definition.viewFilter is string;

        annotation { "Name" : "Show edge positions", "Default" : false,
                    "Description" : "Add where each station line starts and ends, measured across the view from the datum axis (plan: -58 / +58 for a 116 mm width centred on the datum)." }
        definition.showEdges is boolean;

        annotation { "Name" : "Language", "Default" : StationTableLanguage.AS_DEFINED, "UIHint" : UIHint.SHOW_LABEL }
        definition.language is StationTableLanguage;
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
            tables = append(tables, viewTable(v.data, v.body, definition.showEdges, tableLanguage(definition.language, v.data)));
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

/** "en" or "de": forced by the table, else the view's (from its Station definition), else English. */
function tableLanguage(language is StationTableLanguage, data is map) returns string
{
    if (language == StationTableLanguage.GERMAN)
    {
        return "de";
    }
    if (language == StationTableLanguage.ENGLISH || data.language != "de")
    {
        return "en";
    }
    return "de";
}

/** Headings and notes by language (ASCII only). */
const WORDS = {
        "en" : { "station" : "Station", "stations" : "stations", "width" : "Width (mm)", "thickness" : "Thickness (mm)",
                "span" : "Span (mm)", "lower" : "Lower edge (mm)", "upper" : "Upper edge (mm)", "miss" : "misses the part" },
        "de" : { "station" : "Station", "stations" : "Stationen", "width" : "Breite (mm)", "thickness" : "Dicke (mm)",
                "span" : "Abmessung (mm)", "lower" : "Untere Kante (mm)", "upper" : "Obere Kante (mm)", "miss" : "verfehlt das Teil" }
    };

/** The size measured across each station: width in plan, thickness in profile, span in any other view. */
function spanHeading(view is string, words is map) returns string
{
    if (view == "PLAN")
    {
        return words.width;
    }
    if (view == "PROFILE")
    {
        return words.thickness;
    }
    return words.span;
}

function viewTable(data is map, body is Query, showEdges is boolean, language is string) returns Table
{
    const words = WORDS[language];
    var columns = [
            tableColumnDefinition("station", words.station, TableTextAlignment.CENTER),
            tableColumnDefinition("x", "x (mm)", TableTextAlignment.CENTER),
            tableColumnDefinition("span", spanHeading(data.view, words), TableTextAlignment.CENTER)
        ];
    if (showEdges)
    {
        columns = concatenateArrays([columns, [
                        tableColumnDefinition("lo", words.lower, TableTextAlignment.CENTER),
                        tableColumnDefinition("hi", words.upper, TableTextAlignment.CENTER)
                    ]]);
    }
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
            rows = append(rows, tableRow({ "station" : r.id, "x" : round2(r.x), "span" : words.miss, "lo" : "", "hi" : "" }));
        }
    }
    return table(data.title ~ " " ~ words.stations, columns, rows, body);
}

function round2(value is number) returns number
{
    return round(value * 100) / 100;
}
