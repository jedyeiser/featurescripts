FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: station_utils.fs
export import(path : "8a8c023e223cf0814d973a63", version : "d4307c54b9d72c88604cc3fe");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/eb9b32c556ff036c3dd19f73/3cac74f0bc2b98272db13cd3", version : "cffacd73d80aa6dc1a2c4273");
// IMPORT: station_definition_icon.svg (feature icon)
IconNamespace::import(path : "3ee51c2d93f41ff1c25d1a68", version : "1fe37c9db8e98937c78132e1");

/**
 * Station definition: names the places along a ski where drawings measure it, once, for any
 * number of publish features.
 *
 * Stores a station set -- see station_utils.fs -- in its producer slot (extract_outputs key
 * stationSet), where a publish feature that picks this feature under "Station definition"
 * reads it, and optionally also in a # variable. Selections are resolved to points here, so
 * the set holds plain values only.
 */
annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Station definition",
            "Feature Type Description" : "Named measurement stations -- points, or N stations along a line -- stored in one map variable that publish features read." }
export const stationDefinition = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Variable name", "Default" : "stations", "MaxLength" : 64,
                    "Description" : "Optional: also store the set in this # variable. Publish features pick this Station definition itself. Empty = no variable." }
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
        const stations = resolveStationEntries(context, definition.stations);
        const set = { "schema" : STATION_SET_SCHEMA, "stations" : stations };
        if (definition.variableName != "")
        {
            setVariable(context, definition.variableName, set, "Station set: " ~ size(stations) ~ " stations");
        }

        if (definition.printStations)
        {
            for (var s in stations)
            {
                println("[stations] " ~ s.id ~ " at " ~ toString(s.origin / millimeter) ~ " mm"
                    ~ (s.hasDirection ? " across " ~ toString(s.direction) : ""));
            }
        }

        embedStandardOutputs(context, id, {
                    "output" : qNothing(),
                    "outputDescription" : "(none: a Station definition makes no geometry)",
                    "variables" : {
                        "stationSet" : extractableVariable(set, "The station set (schema " ~ STATION_SET_SCHEMA ~ "): id, group, origin, direction per station."),
                        "stationCount" : extractableVariable(size(stations), "Number of stations.")
                    }
                });

        reportFeatureInfo(context, id, size(stations) ~ " stations" ~ (definition.variableName != "" ? " (also in #" ~ definition.variableName ~ ")" : "") ~ ".");
    }, {});
