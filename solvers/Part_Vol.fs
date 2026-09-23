FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

// Sets a variable to a solid's volume in cubic millimetres (a plain number).

annotation { "Feature Type Name" : "Part Volume", "Feature Type Description" : "" }
export const partVol = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Part", "Filter" : EntityType.BODY && BodyType.SOLID, "MaxNumberOfPicks" : 1 }
        definition.part is Query;

        annotation { "Name" : "Variable Name" }
        definition.varName is string;
    }
    {
        const vol = evVolume(context, {
                "entities" : definition.part,
                "accuracy" : VolumeAccuracy.HIGH
        });

        // ValueWithUnits divided by its unit is a plain number: m^3 -> mm^3 without a hand-typed factor.
        setVariable(context, definition.varName, vol / millimeter ^ 3);

        println("vol: " ~ toString(vol / millimeter ^ 3) ~ " mm^3");
    });
