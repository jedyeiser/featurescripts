FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
import(path : "onshape/std/queryVariable.fs", version : "3083.0");

/**
 * Case Pattern: build a chain of features once against named query variables (the template,
 * case 1), then re-run that chain for further cases, each case rebinding every name to new
 * geometry and suffixing the bodies it creates with its case name.
 *
 * Case template   binds the names to case 1's queries and publishes its signature (names, case
 *                 name, case-1 queries) under its own feature id.
 * Case pattern    takes the template plus the features built on it as a FeatureList and, per
 *                 case, binds the names to that case's queries and re-runs the list the way a
 *                 feature Pattern with "Reapply features" runs an instance (identity transform:
 *                 nothing moves). Features inside the list that reference each other remap to
 *                 the current case's copies.
 *
 * Design: case_pattern/DESIGN.md.
 */

/** Most query variables one template may declare (the number of input slots in a case row). */
export const CASE_MAX_INPUTS = 8;

/** Returned by getVariable when a name is not set (an undefined default still throws). */
const MISSING = "__caseMissing__";

// ---------------------------------------------------------------------------------------------
// Case template
// ---------------------------------------------------------------------------------------------

annotation { "Feature Type Name" : "Case template",
        "Feature Type Description" : "Declares the named query variables a Case pattern rebinds. Build the features for case 1 on these names, then list this feature and those features in a Case pattern." }
export const caseTemplate = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Case name", "Default" : "A", "MaxLength" : 64,
                    "Description" : "Name of case 1. Case pattern swaps this suffix for each case's name when naming what the case creates." }
        definition.caseName is string;

        annotation { "Name" : "Inputs", "Item name" : "Input", "Item label template" : "#name" }
        definition.inputs is array;
        for (var input in definition.inputs)
        {
            annotation { "Name" : "Name", "Default" : "", "MaxLength" : 64,
                        "Description" : "Query variable name. It may already exist; it is redefined here." }
            input.name is string;

            annotation { "Name" : "Case 1 selection",
                        "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
            input.query is Query;
        }
    }
    {
        // Re-run inside a Case pattern: the pattern has already bound this case's queries.
        if (isInFeaturePattern(context))
        {
            return;
        }

        if (definition.caseName == "")
        {
            throw regenError("Name case 1.", ["caseName"]);
        }
        const count = size(definition.inputs);
        if (count == 0)
        {
            throw regenError("Add at least one input.", ["inputs"]);
        }
        if (count > CASE_MAX_INPUTS)
        {
            throw regenError("A Case template takes at most " ~ CASE_MAX_INPUTS ~ " inputs.", ["inputs"]);
        }

        var names = [];
        var queries = [];
        for (var n = 0; n < count; n += 1)
        {
            const input = definition.inputs[n];
            verifyVariableNameIsValid(input.name, "inputs");
            if (isIn(input.name, names))
            {
                throw regenError("Input name #" ~ input.name ~ " is used twice.", ["inputs"]);
            }
            if (isQueryEmpty(context, input.query))
            {
                throw regenError("#" ~ input.name ~ " selects nothing.", ["inputs"]);
            }
            setQueryVariable(context, input.name, input.query);
            names = append(names, input.name);
            queries = append(queries, input.query);
        }

        setVariable(context, toString(id), {
                    "caseTemplate" : true,
                    "caseName" : definition.caseName,
                    "names" : names,
                    "queries" : queries
                }, "Case template signature");
    }, { "caseName" : "A", "inputs" : [] });

// ---------------------------------------------------------------------------------------------
// Case pattern
// ---------------------------------------------------------------------------------------------

annotation { "Feature Type Name" : "Case pattern",
        "Editing Logic Function" : "casePatternEditLogic",
        "Feature Type Description" : "Re-runs a Case template and the features built on it once per case, binding the template's names to each case's selections. Bodies a case creates are named after the template's with the case name as suffix." }
export const casePattern = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Features to repeat",
                    "Description" : "The Case template and the features built on its inputs." }
        definition.features is FeatureList;

        annotation { "Name" : "Inputs", "UIHint" : UIHint.READ_ONLY, "Default" : "",
                    "Description" : "The template's names, in slot order." }
        definition.inputSummary is string;

        annotation { "Name" : "Cases", "Item name" : "Case", "Item label template" : "#caseName" }
        definition.cases is array;
        for (var row in definition.cases)
        {
            annotation { "Name" : "Case name", "Default" : "", "MaxLength" : 64 }
            row.caseName is string;

            // Slot k shows when useK is set; the editing logic sets them from the template's size.
            annotation { "Name" : "Use input 2", "Default" : true, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.use2 is boolean;
            annotation { "Name" : "Use input 3", "Default" : true, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.use3 is boolean;
            annotation { "Name" : "Use input 4", "Default" : true, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.use4 is boolean;
            annotation { "Name" : "Use input 5", "Default" : true, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.use5 is boolean;
            annotation { "Name" : "Use input 6", "Default" : true, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.use6 is boolean;
            annotation { "Name" : "Use input 7", "Default" : true, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.use7 is boolean;
            annotation { "Name" : "Use input 8", "Default" : true, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.use8 is boolean;

            annotation { "Name" : "Input 1",
                        "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
            row.input1 is Query;
            if (row.use2)
            {
                annotation { "Name" : "Input 2",
                            "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
                row.input2 is Query;
            }
            if (row.use3)
            {
                annotation { "Name" : "Input 3",
                            "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
                row.input3 is Query;
            }
            if (row.use4)
            {
                annotation { "Name" : "Input 4",
                            "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
                row.input4 is Query;
            }
            if (row.use5)
            {
                annotation { "Name" : "Input 5",
                            "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
                row.input5 is Query;
            }
            if (row.use6)
            {
                annotation { "Name" : "Input 6",
                            "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
                row.input6 is Query;
            }
            if (row.use7)
            {
                annotation { "Name" : "Input 7",
                            "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
                row.input7 is Query;
            }
            if (row.use8)
            {
                annotation { "Name" : "Input 8",
                            "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
                row.input8 is Query;
            }
        }

        annotation { "Group Name" : "Keep", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Parts", "Default" : true, "UIHint" : UIHint.DISPLAY_SHORT }
            definition.keepParts is boolean;
            annotation { "Name" : "Surfaces", "Default" : true, "UIHint" : UIHint.DISPLAY_SHORT }
            definition.keepSurfaces is boolean;
            annotation { "Name" : "Curves and points", "Default" : true, "UIHint" : UIHint.DISPLAY_SHORT }
            definition.keepCurves is boolean;
            annotation { "Name" : "Mate connectors", "Default" : true, "UIHint" : UIHint.DISPLAY_SHORT }
            definition.keepMateConnectors is boolean;
            annotation { "Name" : "Planes", "Default" : true, "UIHint" : UIHint.DISPLAY_SHORT }
            definition.keepPlanes is boolean;
            annotation { "Name" : "Sketches", "Default" : false, "UIHint" : UIHint.DISPLAY_SHORT }
            definition.keepSketches is boolean;
        }

        annotation { "Name" : "Name separator", "Default" : "_", "MaxLength" : 8 }
        definition.separator is string;

        // Template body names, cached by the editing logic (getProperty throws during regen,
        // correction 36). One line per body: "<feature index>\t<body index>\t<name>".
        annotation { "Name" : "Template names", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.templateNames is string;
    }
    {
        const featureIds = sortedFeatureIds(context, definition.features);
        const functions = valuesSortedById(context, definition.features);
        if (size(functions) == 0)
        {
            throw regenError("Select the Case template and the features built on it.", ["features"]);
        }
        const template = findTemplate(context, featureIds);
        if (template == undefined)
        {
            throw regenError("Features to repeat must include a Case template.", ["features"]);
        }
        if (template.count > 1)
        {
            throw regenError("Features to repeat includes " ~ template.count ~ " Case templates; list one.", ["features"]);
        }
        const signature = template.signature;
        const nameCount = size(signature.names);

        const caseCount = size(definition.cases);
        if (caseCount == 0)
        {
            reportFeatureInfo(context, id, "Add a case to repeat the features for.");
            return;
        }
        var seen = [signature.caseName];
        for (var k = 0; k < caseCount; k += 1)
        {
            const caseName = definition.cases[k].caseName;
            if (caseName == "")
            {
                throw regenError("Name case " ~ (k + 2) ~ ".", ["cases"]);
            }
            if (isIn(caseName, seen))
            {
                throw regenError("Case name \"" ~ caseName ~ "\" is used twice (case 1 is \"" ~ signature.caseName ~ "\").",
                    ["cases"]);
            }
            seen = append(seen, caseName);
        }

        const templateNames = parseTemplateNames(definition.templateNames);
        var failures = [];
        var unnamed = 0;

        for (var k = 0; k < caseCount; k += 1)
        {
            const row = definition.cases[k];
            const caseId = id + ("case" ~ k);

            // Bind every template name to this case's selection, slot by slot.
            var bindFailure = undefined;
            for (var n = 0; n < nameCount; n += 1)
            {
                const selection = row["input" ~ (n + 1)];
                if (selection == undefined || isQueryEmpty(context, selection))
                {
                    bindFailure = "input " ~ (n + 1) ~ " (#" ~ signature.names[n] ~ ") selects nothing";
                    break;
                }
                setQueryVariable(context, signature.names[n], selection);
            }
            if (bindFailure != undefined)
            {
                failures = append(failures, row.caseName ~ ": " ~ bindFailure);
                continue;
            }

            // Run the list as a Pattern runs one instance (correction 31: never inside startFeature).
            // Bodies each listed feature creates are recorded so they can be named after the
            // template's bodies from the same feature.
            setFeaturePatternInstanceData(context, caseId, { "transform" : identityTransform() });
            var origins = [];
            var before = evaluateQuery(context, qCreatedBy(caseId, EntityType.BODY));
            var failure = undefined;
            for (var i = 0; i < size(functions); i += 1)
            {
                // A failed feature ends only its case; the others are still built and the
                // failure is reported by name below.
                try
                {
                    functions[i](caseId);
                }
                catch (e)
                {
                    failure = "feature " ~ (i + 1) ~ " failed (" ~ errorText(e) ~ ")";
                    break;
                }
                const after = evaluateQuery(context, qCreatedBy(caseId, EntityType.BODY));
                var j = 0;
                for (var body in after)
                {
                    if (!isIn(body, before))
                    {
                        origins = append(origins, { "body" : body, "key" : i ~ "." ~ j });
                        j += 1;
                    }
                }
                before = after;
            }
            unsetFeaturePatternInstanceData(context, caseId);

            const caseBodies = qCreatedBy(caseId, EntityType.BODY);
            if (failure != undefined)
            {
                if (!isQueryEmpty(context, caseBodies))
                {
                    opDeleteBodies(context, id + ("discard" ~ k), { "entities" : caseBodies });
                }
                failures = append(failures, row.caseName ~ ": " ~ failure);
                continue;
            }

            const dropped = unkeptBodies(caseBodies, definition);
            if (!isQueryEmpty(context, dropped))
            {
                opDeleteBodies(context, id + ("drop" ~ k), { "entities" : dropped });
            }

            for (var origin in origins)
            {
                if (isQueryEmpty(context, origin.body) || isQueryEmpty(context, qSketchFilter(origin.body, SketchObject.NO)))
                {
                    continue;
                }
                const templateName = templateNames[origin.key];
                if (templateName == undefined)
                {
                    unnamed += 1;
                    continue;
                }
                setProperty(context, {
                            "entities" : origin.body,
                            "propertyType" : PropertyType.NAME,
                            "value" : caseBodyName(templateName, signature.caseName, row.caseName, definition.separator)
                        });
            }
        }

        // Later features see case 1 again.
        for (var n = 0; n < nameCount; n += 1)
        {
            setQueryVariable(context, signature.names[n], signature.queries[n]);
        }

        if (size(failures) == caseCount)
        {
            throw regenError("No case was built. " ~ join(failures, "; "), ["cases"]);
        }
        if (size(failures) > 0)
        {
            reportFeatureWarning(context, id, "Not built: " ~ join(failures, "; "));
            return;
        }
        var notes = [];
        if (containsSketch(context, definition.features))
        {
            notes = append(notes, "Sketches are re-solved per case. Dimensions and constraints to the origin or the default planes are not"
                    ~ " reapplied, so those entities keep the template's position; constrain sketches to geometry derived from the inputs.");
        }
        if (unnamed > 0)
        {
            notes = append(notes, unnamed ~ " bod" ~ (unnamed == 1 ? "y" : "ies") ~ " kept Onshape's default name: edit this feature to"
                    ~ " refresh the template names.");
        }
        if (size(notes) > 0)
        {
            reportFeatureInfo(context, id, join(notes, " "));
        }
    }, {
        "inputSummary" : "",
        "cases" : [],
        "keepParts" : true,
        "keepSurfaces" : true,
        "keepCurves" : true,
        "keepMateConnectors" : true,
        "keepPlanes" : true,
        "keepSketches" : false,
        "separator" : "_",
        "templateNames" : ""
    });

/**
 * Case pattern editing logic: sizes each case row to the template's input count, shows the
 * template's names, and caches the names of the bodies the listed features created (the feature
 * body cannot read names during regen).
 */
export function casePatternEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map) returns map
{
    const featureIds = sortedFeatureIds(context, definition.features);
    const template = findTemplate(context, featureIds);
    if (template == undefined)
    {
        definition.inputSummary = "";
        return definition;
    }
    const names = template.signature.names;

    var summary = [];
    for (var n = 0; n < size(names); n += 1)
    {
        summary = append(summary, (n + 1) ~ ": #" ~ names[n]);
    }
    definition.inputSummary = join(summary, ", ");

    for (var k = 0; k < size(definition.cases); k += 1)
    {
        for (var slot = 2; slot <= CASE_MAX_INPUTS; slot += 1)
        {
            definition.cases[k]["use" ~ slot] = slot <= size(names);
        }
    }

    var lines = [];
    for (var i = 0; i < size(featureIds); i += 1)
    {
        const bodies = evaluateQuery(context, qSketchFilter(qCreatedBy(featureIds[i], EntityType.BODY), SketchObject.NO));
        for (var j = 0; j < size(bodies); j += 1)
        {
            const name = try silent(getProperty(context, { "entity" : bodies[j], "propertyType" : PropertyType.NAME }));
            if (name is string && name != "")
            {
                lines = append(lines, i ~ "\t" ~ j ~ "\t" ~ name);
            }
        }
    }
    definition.templateNames = join(lines, "\n");
    return definition;
}

// ---------------------------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------------------------

/** The ids of a FeatureList in tree order (the same order as valuesSortedById of its functions). */
function sortedFeatureIds(context is Context, features is map) returns array
{
    var idOf = {};
    for (var featureId in keys(features))
    {
        idOf[featureId] = featureId;
    }
    return valuesSortedById(context, idOf);
}

/** The first Case template signature among the listed features, with how many were found. */
function findTemplate(context is Context, featureIds is array)
{
    var found = undefined;
    var count = 0;
    for (var i = 0; i < size(featureIds); i += 1)
    {
        const value = getVariable(context, toString(featureIds[i]), MISSING);
        if (value is map && value.caseTemplate == true)
        {
            count += 1;
            if (found == undefined)
            {
                found = { "signature" : value, "index" : i };
            }
        }
    }
    if (found == undefined)
    {
        return undefined;
    }
    found.count = count;
    return found;
}

/** Parses the cached template names into a map from "<feature index>.<body index>" to name. */
function parseTemplateNames(text is string) returns map
{
    var result = {};
    if (text == "")
    {
        return result;
    }
    for (var line in splitByRegexp(text, "\n"))
    {
        const parts = splitByRegexp(line, "\t");
        if (size(parts) >= 3)
        {
            result[parts[0] ~ "." ~ parts[1]] = parts[2];
        }
    }
    return result;
}

/** "Rib_A" -> "Rib_B": strips case 1's suffix when present, then appends this case's. */
function caseBodyName(templateName is string, templateCase is string, thisCase is string, separator is string) returns string
{
    const templateSuffix = separator ~ templateCase;
    var base = templateName;
    if (endsWith(templateName, templateSuffix) && length(templateName) > length(templateSuffix))
    {
        base = substring(templateName, 0, length(templateName) - length(templateSuffix));
    }
    return base ~ separator ~ thisCase;
}

/** The bodies of a case the Keep options discard. */
function unkeptBodies(caseBodies is Query, definition is map) returns Query
{
    const solidModel = qSketchFilter(caseBodies, SketchObject.NO);
    const regular = qConstructionFilter(solidModel, ConstructionObject.NO);
    var dropped = [];
    if (!definition.keepSketches)
    {
        dropped = append(dropped, qSketchFilter(caseBodies, SketchObject.YES));
    }
    if (!definition.keepParts)
    {
        dropped = append(dropped, qBodyType(regular, BodyType.SOLID));
    }
    if (!definition.keepSurfaces)
    {
        dropped = append(dropped, qBodyType(regular, BodyType.SHEET));
    }
    if (!definition.keepCurves)
    {
        dropped = append(dropped, qBodyType(regular, [BodyType.WIRE, BodyType.POINT]));
    }
    if (!definition.keepMateConnectors)
    {
        dropped = append(dropped, qBodyType(solidModel, BodyType.MATE_CONNECTOR));
    }
    if (!definition.keepPlanes)
    {
        dropped = append(dropped, qBodyType(qConstructionFilter(solidModel, ConstructionObject.YES), BodyType.SHEET));
    }
    return qUnion(dropped);
}

/** A short text for a caught regen error. */
function errorText(e) returns string
{
    if (e is map && e.message != undefined)
    {
        return toString(e.message);
    }
    return toString(e);
}
