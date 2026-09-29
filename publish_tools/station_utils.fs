FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

/**
 * Stations: named places along a part where a drawing measures it.
 *
 * A station is { id, group, origin, hasDirection, direction }:
 *     id            unique name, safe as an operation id and a body-name suffix ("MRS", "RULE_3")
 *     group         the entry that made it (a Line entry makes group_1 .. group_N)
 *     origin        world point the station passes through (length vector)
 *     hasDirection  true when the station measures perpendicular to a given direction
 *     direction     unit vector of that direction ((0, 0, 0) when hasDirection is false)
 *
 * A station with no direction measures across the view's measuring axis (datum X).
 *
 * A station SET is the map a "Station definition" feature writes to a variable:
 *     { "schema" : STATION_SET_SCHEMA, "stations" : [ station, ... ], "language" : "en" | "de" }
 * (language: sets saved before 2026-09-28 have none and read as "en").
 * Plain values only -- queries are resolved to points when the set is defined.
 *
 * Ids come from names, never from positions in a list: operation ids (and so the drawing
 * references hanging off them) follow the station, not its row.
 */

export const STATION_SET_SCHEMA = "stationSet/1";

/**
 * Attribute every Station geometry view composite ("<prefix> PLAN", ...) carries, so the "Station table"
 * custom table can find them with qHasAttribute and build one table per part and view:
 *     { "schema" : STATION_TABLE_SCHEMA, "title" : "<prefix> <VIEW>", "prefix", "view" (PLAN / PROFILE / name),
 *       "rows" : [{ "id", "x", "lo", "hi", "span" (mm from the datum, plain numbers), "hit" }] }
 */
export const STATION_TABLE_ATTRIBUTE = "publishStationTable";
export const STATION_TABLE_SCHEMA = "stationTable/1";

export const STATION_COUNT_BOUNDS = { (unitless) : [2, 5, 200] } as IntegerBoundSpec;
/** Language of the station table's headings and notes; a station set stores it as its code ("en", "de"). */
export enum StationLanguage
{
    annotation { "Name" : "English" }
    ENGLISH,
    annotation { "Name" : "Deutsch" }
    GERMAN
}

export function stationLanguageCode(language is StationLanguage) returns string
{
    return language == StationLanguage.GERMAN ? "de" : "en";
}

export const STATION_FIRST_NUMBER_BOUNDS = { (unitless) : [0, 1, 1000] } as IntegerBoundSpec;

export enum StationEntryType
{
    annotation { "Name" : "Point" }
    POINT,
    annotation { "Name" : "Along a line" }
    LINE,
    annotation { "Name" : "Between two points" }
    BETWEEN
}

/**
 * One station entry. Shared by Station definition and by every publish feature's
 * "More stations" list, so both read the same way.
 */
export predicate stationEntryPredicate(entry is map)
{
    annotation { "Name" : "Type", "Default" : StationEntryType.POINT, "UIHint" : UIHint.SHOW_LABEL }
    entry.stationType is StationEntryType;

    annotation { "Name" : "Name", "Default" : "", "MaxLength" : 64,
                "Description" : "Station id. Along a line / between two points: stations are named <name>_1 .. <name>_N, or just 1 .. N when the name is empty." }
    entry.stationName is string;

    if (entry.stationType == StationEntryType.POINT || entry.stationType == StationEntryType.BETWEEN)
    {
        annotation { "Name" : "Point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
        entry.point is Query;
    }

    if (entry.stationType == StationEntryType.BETWEEN)
    {
        annotation { "Name" : "Second point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
        entry.secondPoint is Query;
    }

    if (entry.stationType == StationEntryType.LINE)
    {
        annotation { "Name" : "Line", "Filter" : EntityType.EDGE && GeometryType.LINE, "MaxNumberOfPicks" : 1 }
        entry.lineEdge is Query;
    }

    if (entry.stationType == StationEntryType.LINE || entry.stationType == StationEntryType.BETWEEN)
    {
        annotation { "Name" : "Stations", "Description" : "Evenly spaced; both ends included. Each measures perpendicular to the line." }
        isInteger(entry.count, STATION_COUNT_BOUNDS);

        annotation { "Name" : "First number", "Description" : "Number of the first station: 0 gives <name>_0 .. <name>_(N-1)." }
        isInteger(entry.firstNumber, STATION_FIRST_NUMBER_BOUNDS);

        annotation { "Name" : "Reverse numbering", "Default" : false,
                    "Description" : "Numbering runs toward +X (else +Y, else +Z) unless reversed." }
        entry.reverse is boolean;
    }
}

/**
 * The stations a list of entries defines, in entry order. Throws on an unnamed entry, an
 * empty selection or a repeated id.
 */
export function resolveStationEntries(context is Context, entries is array) returns array
{
    var stations = [];
    for (var i = 0; i < size(entries); i += 1)
    {
        stations = concatenateArrays([stations, resolveStationEntry(context, entries[i], i)]);
    }
    checkUniqueIds(stations);
    return stations;
}

function resolveStationEntry(context is Context, entry is map, index is number) returns array
{
    const group = stationIdFromName(entry.stationName);
    if (group == "" && entry.stationType == StationEntryType.POINT)
    {
        throw regenError("Station " ~ (index + 1) ~ " has no name.");
    }
    // An unnamed line / between entry numbers its stations plainly: 1 .. N (or from First number).
    const label = group == "" ? toString(index + 1) : group;

    if (entry.stationType == StationEntryType.POINT)
    {
        return [makeStation(group, group, entryPoint(context, entry.point, group), undefined)];
    }

    var a;
    var b;
    if (entry.stationType == StationEntryType.LINE)
    {
        if (isQueryEmpty(context, entry.lineEdge))
        {
            throw regenError("Station " ~ label ~ ": select a line.");
        }
        const ends = evEdgeTangentLines(context, { "edge" : entry.lineEdge, "parameters" : [0, 1] });
        a = ends[0].origin;
        b = ends[1].origin;
    }
    else
    {
        a = entryPoint(context, entry.point, label);
        b = entryPoint(context, entry.secondPoint, label);
    }

    if (norm(b - a) < TOLERANCE.zeroLength * meter)
    {
        throw regenError("Station " ~ label ~ ": the two ends coincide.");
    }

    // Number in a direction that does not depend on how the edge happens to be parameterised.
    if (runsForward(b - a) == entry.reverse)
    {
        const t = a;
        a = b;
        b = t;
    }

    const direction = normalize(b - a);
    // Saved before First number existed: numbering starts at 1, as it did.
    const first = entry.firstNumber == undefined ? 1 : entry.firstNumber;
    const prefix = group == "" ? "" : group ~ "_";
    var out = [];
    for (var k = 0; k < entry.count; k += 1)
    {
        const f = k / (entry.count - 1);
        out = append(out, makeStation(prefix ~ (k + first), group, a + (b - a) * f, direction));
    }
    return out;
}

/**
 * True when `d` points toward +X, or -- when it has no real X part -- toward +Y, then +Z.
 */
function runsForward(d is Vector) returns boolean
{
    const u = normalize(d);
    for (var k = 0; k < 3; k += 1)
    {
        if (abs(u[k]) > 0.01)
        {
            return u[k] > 0;
        }
    }
    return true;
}

function makeStation(stationId is string, group is string, origin is Vector, direction) returns map
{
    return {
        "id" : stationId,
        "group" : group,
        "origin" : origin,
        "hasDirection" : direction != undefined,
        "direction" : direction == undefined ? vector(0, 0, 0) : direction
    };
}

/**
 * World position of a vertex or a mate connector's origin.
 */
export function entryPoint(context is Context, q is Query, stationName is string) returns Vector
{
    if (isQueryEmpty(context, q))
    {
        throw regenError("Station " ~ stationName ~ ": select a point.");
    }
    if (!isQueryEmpty(context, qBodyType(q, BodyType.MATE_CONNECTOR)))
    {
        return evMateConnector(context, { "mateConnector" : q }).origin;
    }
    return evVertexPoint(context, { "vertex" : q });
}

/**
 * A name made safe for an operation id and a body-name suffix: letters, digits and
 * underscores, everything else replaced by an underscore.
 */
export function stationIdFromName(name is string) returns string
{
    return replace(name, "[^A-Za-z0-9_]", "_");
}

function checkUniqueIds(stations is array)
{
    var seen = {};
    for (var s in stations)
    {
        if (seen[s.id] == true)
        {
            throw regenError("Two stations are named " ~ s.id ~ ". Station names must be unique.");
        }
        seen[s.id] = true;
    }
}

/**
 * The stations of the set stored in variable `name`. Throws when the variable is missing or
 * is not a station set.
 */
export function readStationSet(context is Context, name is string) returns array
{
    const missing = "__no_station_set__";
    const value = getVariable(context, name, missing);
    if (value == missing)
    {
        throw regenError("No variable named " ~ name ~ ". Add a Station definition above this feature.");
    }
    if (!(value is map) || value.schema != STATION_SET_SCHEMA || !(value.stations is array))
    {
        throw regenError("Variable " ~ name ~ " is not a station set.");
    }
    return value.stations;
}

/**
 * The stations of the Station definition features in `features` (a FeatureList), one
 * definition after another. A Station definition embeds its set in its hidden producer slot
 * toString(featureId), under variable.stationSet (extract_outputs); the entry is read structurally, as
 * a plain value or as an extractable-variable descriptor.
 */
export function readStationDefinitions(context is Context, features is map) returns array
{
    const missing = "__no_station_definition__";
    var stations = [];
    for (var featureId in keys(features))
    {
        const slot = getVariable(context, toString(featureId), missing);
        var set = undefined;
        if (slot is map && slot.variable is map && slot.variable.stationSet != undefined)
        {
            // embedStandardOutputs files values under "variable" (queries under "query").
            set = slot.variable.stationSet;
            if (set is map && set.extractable != undefined)
            {
                set = set.value;
            }
        }
        if (!(set is map) || set.schema != STATION_SET_SCHEMA || !(set.stations is array))
        {
            throw regenError("A picked feature gave no stations: pick Station definition features, and check that they regenerate.", ["stationDefinitions"]);
        }
        stations = concatenateArrays([stations, set.stations]);
    }
    return stations;
}

/**
 * The table language ("en" / "de") of the first picked Station definition that states one; "en" otherwise.
 */
export function readStationLanguage(context is Context, features is map) returns string
{
    for (var featureId in keys(features))
    {
        const slot = getVariable(context, toString(featureId), "__no_station_definition__");
        if (slot is map && slot.variable is map && slot.variable.stationSet != undefined)
        {
            var set = slot.variable.stationSet;
            if (set is map && set.extractable != undefined)
            {
                set = set.value;
            }
            if (set is map && set.language is string)
            {
                return set.language;
            }
        }
    }
    return "en";
}

/**
 * Stations from the picked Station definition features, followed by extra entries; ids must be
 * unique across all of them. `legacySetName` (a variable name, from features saved before the
 * Station definition pick existed) is read only when no definition is picked.
 */
export function collectStations(context is Context, definitions is map, legacySetName is string, entries is array) returns array
{
    var stations = [];
    if (size(definitions) > 0)
    {
        stations = readStationDefinitions(context, definitions);
    }
    else if (legacySetName != "")
    {
        stations = readStationSet(context, legacySetName);
    }
    stations = concatenateArrays([stations, resolveStationEntries(context, entries)]);
    checkUniqueIds(stations);
    return stations;
}

/**
 * Stations from a named set followed by extra entries; ids must be unique across both.
 */
export function collectStations(context is Context, setName is string, entries is array) returns array
{
    var stations = [];
    if (setName != "")
    {
        stations = readStationSet(context, setName);
    }
    stations = concatenateArrays([stations, resolveStationEntries(context, entries)]);
    checkUniqueIds(stations);
    return stations;
}
