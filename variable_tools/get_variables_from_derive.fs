FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

/** The registry variable that "Add variables for derive" writes in the source Part Studio. */
const EXPORT_REGISTRY = "exportVariables";

/** Returned by getVariable for a name that is not set (no value of the model is this map). */
const NOT_SET = { "notSet" : "get_variables_from_derive" };

/**
 * Get variables from derive.
 *
 * Pick the Part Studio you derive from. The feature builds that studio's context, reads its
 * `exportVariables` variable (written by "Add variables for derive") and sets every entry as an
 * ordinary variable here (`#name`). A variable that already exists in this studio is overwritten,
 * as with any Onshape variable; the last setter in the tree wins.
 *
 * Put it before the features that use the variables. The Part Studio is rebuilt once more for
 * this read (the Derived feature has its own copy), so the cost is one extra regeneration of the source.
 */
annotation { "Feature Type Name" : "Get variables from derive",
        "Feature Type Description" : "Sets the variables that a source Part Studio lists with Add variables for derive as variables in this Part Studio.",
        "UIHint" : UIHint.NO_PREVIEW_PROVIDED }
export const getVariablesFromDerive = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Part Studio", "UIHint" : UIHint.SHOW_INLINE_CONFIG_INPUTS }
        definition.partStudio is PartStudioData;
    }
    {
        if (definition.partStudio.buildFunction == undefined)
        {
            throw regenError("Pick the Part Studio you derive from.", ["partStudio"]);
        }

        const otherContext = @convert(definition.partStudio.buildFunction(definition.partStudio.configuration), undefined);
        if (otherContext == undefined)
        {
            throw regenError("The Part Studio could not be built.", ["partStudio"]);
        }

        const registry = getVariable(otherContext, EXPORT_REGISTRY, NOT_SET);
        if (registry == NOT_SET)
        {
            throw regenError("The Part Studio has no '" ~ EXPORT_REGISTRY ~ "' variable. Add an 'Add variables for derive' feature to it.", ["partStudio"]);
        }
        if (!(registry is map))
        {
            throw regenError("'" ~ EXPORT_REGISTRY ~ "' in the Part Studio is not a map.", ["partStudio"]);
        }

        var count = 0;
        for (var name, value in registry)
        {
            verifyVariableNameIsValid(name, "partStudio");
            setVariable(context, name, value, "From derived Part Studio");
            count += 1;
        }

        reportFeatureInfo(context, id, "Set " ~ count ~ " variable(s) from the derived Part Studio.");
    });
