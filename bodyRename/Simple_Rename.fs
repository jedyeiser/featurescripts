FeatureScript 3008;
import(path : "onshape/std/common.fs", version : "3008.0");

annotation { "Feature Type Name" : "Simple Body Rename", "Feature Type Description" : "Select bodies to rename, with an optional shared prefix and suffix applied to every body." }
export const myFeature = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Add prefix", "Default" : false }
        definition.usePrefix is boolean;
        if (definition.usePrefix)
        {
            annotation { "Name" : "Prefix", "Description" : "Prepended to every body name below." }
            definition.prefix is string;
        }

        annotation { "Name" : "Add suffix", "Default" : false }
        definition.useSuffix is boolean;
        if (definition.useSuffix)
        {
            annotation { "Name" : "Suffix", "Description" : "Appended to every body name below." }
            definition.suffix is string;
        }

        annotation { "Name" : "Bodies to Rename", "Item name" : "Body",
                "Driven query" : "query", "Item label template" : "#renameString" }
        definition.renameArray is array;
        for (var body in definition.renameArray)
        {
            annotation { "Name" : "Body", "Filter" : EntityType.BODY && (BodyType.SOLID || BodyType.SHEET || BodyType.WIRE), "MaxNumberOfPicks" : 1, "UIHint" : UIHint.FOCUS_INNER_QUERY }
            body.query is Query;

            annotation { "Name" : "New Body Name" }
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
