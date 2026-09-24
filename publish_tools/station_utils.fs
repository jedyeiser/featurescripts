FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

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
 *     { "schema" : STATION_SET_SCHEMA, "stations" : [ station, ... ] }
 * Plain values only -- queries are resolved to points when the set is defined.
 *
 * Ids come from names, never from positions in a list: operation ids (and so the drawing
 * references hanging off them) follow the station, not its row.
 */

export const STATION_SET_SCHEMA = "stationSet/1";

export const STATION_COUNT_BOUNDS = { (unitless) : [2, 5, 200] } as IntegerBoundSpec;

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
                "Description" : "Station id. Along a line / between two points: stations are named <name>_1 .. <name>_N." }
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
    if (group == "")
    {
        throw regenError("Station " ~ (index + 1) ~ " has no name.");
    }

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
            throw regenError("Station " ~ group ~ ": select a line.");
        }
        const ends = evEdgeTangentLines(context, { "edge" : entry.lineEdge, "parameters" : [0, 1] });
        a = ends[0].origin;
        b = ends[1].origin;
    }
    else
    {
        a = entryPoint(context, entry.point, group);
        b = entryPoint(context, entry.secondPoint, group);
    }

    if (norm(b - a) < TOLERANCE.zeroLength * meter)
    {
        throw regenError("Station " ~ group ~ ": the two ends coincide.");
    }

    // Number in a direction that does not depend on how the edge happens to be parameterised.
    if (runsForward(b - a) == entry.reverse)
    {
        const t = a;
        a = b;
        b = t;
    }

    const direction = normalize(b - a);
    var out = [];
    for (var k = 0; k < entry.count; k += 1)
    {
        const f = k / (entry.count - 1);
        out = append(out, makeStation(group ~ "_" ~ (k + 1), group, a + (b - a) * f, direction));
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
