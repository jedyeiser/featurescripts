FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

//very simple script to return part volume in mm3 and set a variable

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
        var vol = evVolume(context, {
                "entities" : definition.part,
                "accuracy" : VolumeAccuracy.HIGH
        });
        
        setVariable(context, definition.varName, vol.value * 10e9);
        
        println('vol: ' ~ '-> ' ~ vol);
        
    });
