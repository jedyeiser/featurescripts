FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: Variable_tools extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/eb9b32c556ff036c3dd19f73/3cac74f0bc2b98272db13cd3", version : "cffacd73d80aa6dc1a2c4273");
// IMPORT: simple_body_rename_icon.svg (feature icon)
IconNamespace::import(path : "026bf217ee0cf42310c85703", version : "fbdf1e6dfb8670db5c67d9c5");

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Simple Body Rename", "Feature Type Description" : "Select bodies or composite parts to rename, with an optional shared prefix and suffix applied to every name." }
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

        var renamed = 0;
        var renamedBodies = [];
        var blank = [];
        var lost = [];
        for (var i = 0; i < size(definition.renameArray); i += 1)
        {
            const renameBody = definition.renameArray[i];
            if (isQueryEmpty(context, renameBody.query))
            {
                lost = append(lost, i + 1);
                continue;
            }
            // A blank name is skipped (it used to rename the body to just the prefix/suffix).
            if (renameBody.renameString == "")
            {
                blank = append(blank, i + 1);
                continue;
            }
            setProperty(context, {
                    "entities" : renameBody.query,
                    "propertyType" : PropertyType.NAME,
                    "value" : prefix ~ renameBody.renameString ~ suffix
            });
            renamed += 1;
            renamedBodies = append(renamedBodies, renameBody.query);
        }

        if (size(lost) > 0)
        {
            reportFeatureWarning(context, id, "Item(s) " ~ itemList(lost) ~ " no longer select a body and were not renamed.");
        }
        else
        {
            var message = "Renamed " ~ renamed ~ (renamed == 1 ? " body." : " bodies.");
            if (size(blank) > 0)
            {
                message ~= " Item(s) " ~ itemList(blank) ~ " have no name and were skipped.";
            }
            reportFeatureInfo(context, id, message);
        }

        // Publish for Extract variables: the renamed bodies (modified in place, so qCreatedBy misses them).
        embedStandardOutputs(context, id, {
                    "output" : qUnion(renamedBodies),
                    "outputDescription" : "The renamed bodies"
                });
    });

// "1, 3, 4"
function itemList(numbers is array) returns string
{
    var text = "";
    for (var i = 0; i < size(numbers); i += 1)
    {
        text ~= (i == 0 ? "" : ", ") ~ numbers[i];
    }
    return text;
}
