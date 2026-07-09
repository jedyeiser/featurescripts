FeatureScript 3008;
import(path : "onshape/std/common.fs", version : "3008.0");

annotation { "Feature Type Name" : "Simple Body Rename", "Feature Type Description" : "Select bodies or composite parts to rename, with an optional shared prefix and suffix applied to every name." }
export const myFeature = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Add prefix", "Default" : false }
        definition.usePrefix is boolean;
        if (definition.usePrefix)
        {
            annotation { "Name" : "Prefix", "Description" : "Prepended to every name below." }
            definition.prefix is string;
        }

        annotation { "Name" : "Add suffix", "Default" : false }
        definition.useSuffix is boolean;
        if (definition.useSuffix)
        {
            annotation { "Name" : "Suffix", "Description" : "Appended to every name below." }
            definition.suffix is string;
        }

        annotation { "Name" : "Bodies and parts to rename", "Item name" : "Item",
                "Driven query" : "query", "Item label template" : "#renameString" }
        definition.renameArray is array;
        for (var body in definition.renameArray)
        {
            annotation { "Name" : "Body or composite part", "Filter" : EntityType.BODY && (BodyType.SOLID || BodyType.SHEET || BodyType.WIRE || BodyType.COMPOSITE), "MaxNumberOfPicks" : 1, "UIHint" : UIHint.FOCUS_INNER_QUERY }
            body.query is Query;

            annotation { "Name" : "New name" }
            body.renameString is string;
        }
    }
    {
        const prefix = definition.usePrefix ? definition.prefix : "";
        const suffix = definition.useSuffix ? definition.suffix : "";

        for (var renameBody in definition.renameArray)
        {
            const newName = prefix ~ renameBody.renameString ~ suffix;
            if (newName != "")
            {
                setProperty(context, {
                        "entities" : renameBody.query,
                        "propertyType" : PropertyType.NAME,
                        "value" : newName
                });
            }
        }
    });
