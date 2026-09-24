FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");
// IMPORT: station_utils.fs
export import(path : "8a8c023e223cf0814d973a63", version : "");

/**
 * Station definition: names the places along a ski where drawings measure it, once, for any
 * number of publish features.
 *
 * Writes a map variable (default #stations) holding a station set -- see station_utils.fs.
 * Selections are resolved to points here, so the variable holds plain values only; a
 * publish feature further down the tree names the variable and gets the same stations.
 */
annotation { "Feature Type Name" : "Station definition",
            "Feature Type Description" : "Named measurement stations -- points, or N stations along a line -- stored in one map variable that publish features read." }
export const stationDefinition = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Variable name", "Default" : "stations", "MaxLength" : 64,
                    "Description" : "The map variable to write. Publish features name it under Station set." }
        definition.variableName is string;

        annotation { "Name" : "Stations", "Item name" : "station", "Item label template" : "#stationName" }
        definition.stations is array;
        for (var entry in definition.stations)
        {
            stationEntryPredicate(entry);
        }

        annotation { "Name" : "Print stations", "Default" : false }
        definition.printStations is boolean;
    }
    {
        if (definition.variableName == "")
        {
            throw regenError("Name the variable.", ["variableName"]);
        }

        const stations = resolveStationEntries(context, definition.stations);
        setVariable(context, definition.variableName, { "schema" : STATION_SET_SCHEMA, "stations" : stations },
            "Station set: " ~ size(stations) ~ " stations");

        if (definition.printStations)
        {
            for (var s in stations)
            {
                println("[stations] " ~ s.id ~ " at " ~ toString(s.origin / millimeter) ~ " mm"
                    ~ (s.hasDirection ? " across " ~ toString(s.direction) : ""));
            }
        }

        reportFeatureInfo(context, id, "#" ~ definition.variableName ~ ": " ~ size(stations) ~ " stations.");
    }, {});
