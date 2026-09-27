FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: Variable_tools extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/eb9b32c556ff036c3dd19f73/3cac74f0bc2b98272db13cd3", version : "cffacd73d80aa6dc1a2c4273");
// IMPORT: simple_body_rename_icon.svg (feature icon)
IconNamespace::import(path : "026bf217ee0cf42310c85703", version : "fbdf1e6dfb8670db5c67d9c5");

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Simple Body Rename", "Feature Type Description" : "Select bodies or composite parts to rename, with an optional shared prefix and suffix applied to every name. Optional find/replace changes literal text in the current names of other selected bodies.", "Editing Logic Function" : "simpleRenameEditingLogic" }
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

        // Default false: saved instances (which never had it) keep renaming only the list above (correction 25).
        annotation { "Name" : "Find and replace", "Default" : false,
                    "Description" : "Replace literal text (not a pattern) in the current names of the bodies picked here. Prefix and suffix are not applied; a body also listed above takes the name from the list." }
        definition.useFindReplace is boolean;
        if (definition.useFindReplace)
        {
            annotation { "Name" : "Bodies to find and replace in", "Filter" : EntityType.BODY && (BodyType.SOLID || BodyType.SHEET || BodyType.WIRE || BodyType.COMPOSITE) }
            definition.findReplaceBodies is Query;

            annotation { "Name" : "Find", "Default" : "", "MaxLength" : 128 }
            definition.findText is string;

            annotation { "Name" : "Replace with", "Default" : "", "MaxLength" : 128 }
            definition.replaceText is string;
        }

        // Current names of findReplaceBodies, one per line in query order, written by the editing logic: a
        // feature cannot read names while it regenerates (correction 36).
        annotation { "Name" : "Captured names", "Default" : "", "UIHint" : [UIHint.ALWAYS_HIDDEN] }
        definition.capturedNames is string;
    }
    {
        const prefix = definition.usePrefix ? definition.prefix : "";
        const suffix = definition.useSuffix ? definition.suffix : "";

        var renamed = 0;
        var renamedBodies = [];
        var blank = [];
        var lost = [];

        // Find/replace first, so a body also in the list takes the list's name.
        var findReplaceNote = "";
        var staleNames = false;
        if (definition.useFindReplace)
        {
            const result = applyFindReplace(context, definition);
            renamed += size(result.renamed);
            renamedBodies = concatenateArrays([renamedBodies, result.renamed]);
            findReplaceNote = result.note;
            staleNames = result.stale;
        }

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
            reportFeatureWarning(context, id, "Item(s) " ~ itemList(lost) ~ " no longer select a body and were not renamed." ~
                    (staleNames ? " " ~ findReplaceNote : ""));
        }
        else if (staleNames)
        {
            reportFeatureWarning(context, id, findReplaceNote);
        }
        else
        {
            var message = "Renamed " ~ renamed ~ (renamed == 1 ? " body." : " bodies.");
            if (size(blank) > 0)
            {
                message ~= " Item(s) " ~ itemList(blank) ~ " have no name and were skipped.";
            }
            if (findReplaceNote != "")
            {
                message ~= " " ~ findReplaceNote;
            }
            reportFeatureInfo(context, id, message);
        }

        // Publish for Extract variables: the renamed bodies (modified in place, so qCreatedBy misses them).
        embedStandardOutputs(context, id, {
                    "output" : qUnion(renamedBodies),
                    "outputDescription" : "The renamed bodies"
                });
    }, {
        "useFindReplace" : false,
        "findReplaceBodies" : qNothing(),
        "findText" : "",
        "replaceText" : "",
        "capturedNames" : ""
    });

// Name separator in capturedNames (Onshape names are single-line).
const NAME_SEPARATOR = "\n";

// Captures the current names of the find/replace bodies (getProperty works here, not in the feature body).
// The editing logic sees the context before this feature, so the names are the ORIGINAL ones.
export function simpleRenameEditingLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map, hiddenBodies is Query) returns map
{
    var names = [];
    if (definition.useFindReplace == true)
    {
        for (var body in evaluateQuery(context, definition.findReplaceBodies))
        {
            const current = getProperty(context, { "entity" : body, "propertyType" : PropertyType.NAME });
            names = append(names, current == undefined ? "" : current);
        }
    }
    definition.capturedNames = join(names, NAME_SEPARATOR);
    return definition;
}

// Renames each find/replace body to its captured name with every occurrence of findText replaced
// (literal text). Bodies whose name does not contain findText keep their name.
function applyFindReplace(context is Context, definition is map) returns map
{
    const bodies = evaluateQuery(context, definition.findReplaceBodies);
    if (size(bodies) == 0)
    {
        return { "renamed" : [], "note" : "Find and replace: no bodies selected.", "stale" : false };
    }
    if (definition.findText == "")
    {
        return { "renamed" : [], "note" : "Find and replace: nothing to find.", "stale" : false };
    }
    const names = splitNames(definition.capturedNames);
    if (size(names) != size(bodies))
    {
        return { "renamed" : [], "stale" : true,
                "note" : "Find and replace: the captured names (" ~ size(names) ~ ") do not match the " ~ size(bodies) ~
                    " selected bodies; edit the feature to capture them again. Nothing was replaced." };
    }
    var renamed = [];
    for (var i = 0; i < size(bodies); i += 1)
    {
        const newName = replaceLiteral(names[i], definition.findText, definition.replaceText);
        if (newName != names[i] && newName != "")
        {
            setProperty(context, {
                    "entities" : bodies[i],
                    "propertyType" : PropertyType.NAME,
                    "value" : newName
            });
            renamed = append(renamed, bodies[i]);
        }
    }
    return { "renamed" : renamed, "stale" : false,
            "note" : "Find and replace: " ~ size(renamed) ~ " of " ~ size(bodies) ~ " names changed." };
}

// Every occurrence of `find` in `s` replaced by `replacement`, as plain text (no regular expression).
function replaceLiteral(s is string, find is string, replacement is string) returns string
{
    var out = "";
    var start = 0;
    var at = indexOf(s, find, start);
    while (at >= 0)
    {
        out ~= substring(s, start, at) ~ replacement;
        start = at + length(find);
        at = indexOf(s, find, start);
    }
    return out ~ substring(s, start);
}

// capturedNames back into an array (a manual split: a regexp split would drop trailing empty names).
function splitNames(text is string) returns array
{
    if (text == "")
    {
        return [];
    }
    var names = [];
    var start = 0;
    var at = indexOf(text, NAME_SEPARATOR, start);
    while (at >= 0)
    {
        names = append(names, substring(text, start, at));
        start = at + length(NAME_SEPARATOR);
        at = indexOf(text, NAME_SEPARATOR, start);
    }
    return append(names, substring(text, start));
}

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
