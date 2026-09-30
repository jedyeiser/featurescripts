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
 * Station table: one table per PART (Station geometry prefix; a part with plan and profile gets ONE table with Width
 * and Thickness columns -- 2026-09-30), for the Part Studio's
 * table panel and for drawings (Insert > Custom table > this Part Studio > pick the table).
 *
 * Each view composite carries the attribute STATION_TABLE_ATTRIBUTE (station_utils.fs) with its rows, written
 * by Station geometry; the table finds every body with that attribute and a matching schema, so any number of
 * Station geometry features, parts and views are tabulated, each in its own table. Rows are sorted along the
 * measuring axis (x from the datum). Every view also has two rows at the part's ends (TIP / TAIL, or MIN X / MAX X):
 * x only, the other cells empty. Nothing is read from feature parameters, so the table follows every
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
        // One table per PART (prefix): a part with plan and profile gets one table with Width and Thickness columns
        // (user, 2026-09-30). Parts in model order (the feature tree's); strings cannot be ordered with <.
        var order = [];
        var groups = {};
        for (var v in views)
        {
            const key = v.data.prefix is string ? v.data.prefix : v.data.title;
            if (groups[key] == undefined)
            {
                order = append(order, key);
                groups[key] = [];
            }
            groups[key] = append(groups[key], v);
        }
        var tables = [];
        for (var key in order)
        {
            tables = append(tables, partTable(key, groups[key], definition.showEdges, tableLanguage(definition.language, groups[key][0].data)));
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

/** Headings, notes and extent-row names (by role TIP / TAIL / MIN / MAX, as station_geometry.fs) by language (ASCII only). */
const WORDS = {
        "en" : { "station" : "Station", "stations" : "stations", "width" : "Width (mm)", "thickness" : "Thickness (mm)",
                "span" : "Span (mm)", "lower" : "Lower edge (mm)", "upper" : "Upper edge (mm)", "miss" : "misses the part",
                "TIP" : "TIP", "TAIL" : "TAIL", "MIN" : "MIN X", "MAX" : "MAX X" },
        "de" : { "station" : "Station", "stations" : "Stationen", "width" : "Breite (mm)", "thickness" : "Dicke (mm)",
                "span" : "Abmessung (mm)", "lower" : "Untere Kante (mm)", "upper" : "Obere Kante (mm)", "miss" : "verfehlt das Teil",
                "TIP" : "SPITZE", "TAIL" : "ENDE", "MIN" : "X MIN", "MAX" : "X MAX" }
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

/**
 * One part's table: Station | x | one size column per view (Width for PLAN, Thickness for PROFILE, "<VIEW> span" for
 * others) [+ lower / upper edge per view]. Rows are merged by station id across the views (the stations are shared;
 * x is the first view's), sorted along x. Extent rows (the part's ends) show x only.
 */
function partTable(key is string, views is array, showEdges is boolean, language is string) returns Table
{
    const words = WORDS[language];
    var columns = [
            tableColumnDefinition("station", words.station, TableTextAlignment.CENTER),
            tableColumnDefinition("x", "x (mm)", TableTextAlignment.CENTER)
        ];
    var bodies = [];
    var ids = [];
    var merged = {};
    for (var k = 0; k < size(views); k += 1)
    {
        const data = views[k].data;
        bodies = append(bodies, views[k].body);
        const heading = size(views) > 1 && data.view != "PLAN" && data.view != "PROFILE" ? data.view ~ " " ~ words.span : spanHeading(data.view, words);
        columns = append(columns, tableColumnDefinition("span" ~ k, heading, TableTextAlignment.CENTER));
        if (showEdges)
        {
            const tag = size(views) > 1 ? data.view ~ " " : "";
            columns = concatenateArrays([columns, [
                            tableColumnDefinition("lo" ~ k, tag ~ words.lower, TableTextAlignment.CENTER),
                            tableColumnDefinition("hi" ~ k, tag ~ words.upper, TableTextAlignment.CENTER)
                        ]]);
        }
        for (var r in data.rows)
        {
            if (merged[r.id] == undefined)
            {
                ids = append(ids, r.id);
                const name = (r.extent != undefined && words[r.extent] != undefined) ? words[r.extent] : r.id;
                merged[r.id] = { "x" : r.x, "cells" : { "station" : name, "x" : round2(r.x) } };
            }
            var cells = merged[r.id].cells;
            if (r.extent != undefined)
            {
                cells["span" ~ k] = "";
                cells["lo" ~ k] = "";
                cells["hi" ~ k] = "";
            }
            else if (r.hit)
            {
                cells["span" ~ k] = round2(r.span);
                cells["lo" ~ k] = round2(r.lo);
                cells["hi" ~ k] = round2(r.hi);
            }
            else
            {
                cells["span" ~ k] = words.miss;
                cells["lo" ~ k] = "";
                cells["hi" ~ k] = "";
            }
            merged[r.id].cells = cells;
        }
    }
    var entries = [];
    for (var id in ids)
    {
        entries = append(entries, merged[id]);
    }
    entries = sort(entries, function(a, b) { return a.x - b.x; });
    var rows = [];
    for (var e in entries)
    {
        var cells = e.cells;
        // A station missing from one view (e.g. extra stations on one view only) gets empty cells there.
        for (var k = 0; k < size(views); k += 1)
        {
            if (cells["span" ~ k] == undefined)
            {
                cells["span" ~ k] = "";
                cells["lo" ~ k] = "";
                cells["hi" ~ k] = "";
            }
        }
        rows = append(rows, tableRow(cells));
    }
    const title = size(views) == 1 ? views[0].data.title : key;
    return table(title ~ " " ~ words.stations, columns, rows, qUnion(bodies));
}

function round2(value is number) returns number
{
    return round(value * 100) / 100;
}
