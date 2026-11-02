FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

/** The registry variable the consumer ("Get variables from derive") looks for in the source Part Studio. */
const EXPORT_REGISTRY = "exportVariables";

/** Returned by getVariable for a name that is not set (no value of the model is this map). */
const NOT_SET = { "notSet" : "add_variables_for_derive" };

/**
 * Add variables for derive.
 *
 * Put this in the SOURCE Part Studio, after the variables it lists. It reads the current value of each named
 * variable and stores them together in one variable called `exportVariables` (a map of name -> value).
 * Extract variables, in the studio that derives from this one, reads that map from this studio's
 * context and sets every entry as an ordinary variable there.
 *
 * It adds to the `exportVariables` map (Variable + with "Export for derive" does the same), and a name
 * listed twice is overwritten by the later one. Values are snapshots taken at the feature's position in
 * the tree. Query variables cannot be exported.
 */
annotation { "Feature Type Name" : "Add variables for derive",
        "Feature Type Description" : "Lists the variables of this Part Studio that a Part Studio deriving from it can fetch with Get variables from derive.",
        "UIHint" : UIHint.NO_PREVIEW_PROVIDED }
export const addVariablesForDerive = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Variables", "Item name" : "variable", "Item label template" : "#exportName" }
        definition.exportEntries is array;
        for (var entry in definition.exportEntries)
        {
            annotation { "Name" : "Variable name", "Default" : "", "MaxLength" : 10000, "UIHint" : [UIHint.VARIABLE_NAME, UIHint.SHOW_LABEL],
                        "Description" : "The name of a variable set earlier in this Part Studio, without the #." }
            entry.exportName is string;
        }
    }
    {
        // Merge into the registry: Variable + (Export for derive) and other producers add to it too.
        var registry = getVariable(context, EXPORT_REGISTRY, {});
        if (!(registry is map))
        {
            registry = {};
        }
        for (var i, entry in definition.exportEntries)
        {
            const parameterId = faultyArrayParameterId("exportEntries", i, "exportName");
            const name = entry.exportName;
            verifyVariableNameIsValid(name, parameterId);
            if (name == EXPORT_REGISTRY)
            {
                throw regenError("'" ~ name ~ "' is the registry itself and cannot be exported.", [parameterId]);
            }
            const value = getVariable(context, name, NOT_SET);
            if (value == NOT_SET)
            {
                throw regenError("Variable '" ~ name ~ "' is not set before this feature (query variables cannot be exported).", [parameterId]);
            }
            if (value is Query)
            {
                throw regenError("Variable '" ~ name ~ "' is a query; only ordinary variables can be exported.", [parameterId]);
            }
            registry[name] = value;
        }

        setVariable(context, EXPORT_REGISTRY, registry, "Variables a derived Part Studio can fetch");

        reportFeatureInfo(context, id, "Exporting " ~ size(registry) ~ " variable(s) in '" ~ EXPORT_REGISTRY ~ "'.");
    });
