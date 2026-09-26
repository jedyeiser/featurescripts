FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
import(path : "onshape/std/queryVariable.fs", version : "3083.0");

/**
 * THROWAWAY spikes for the Case Pattern v2 redesign (Define case / Close case / Case pattern), 2026-09-26.
 * Delete this tab once the answers are recorded in DESIGN.md.
 *
 * Close case (test)    the v2 "Close case" end marker: lists the body features ONCE and publishes the
 *                FeatureList (a map of feature functions) through setVariable.
 * Case pattern (test)  the v2 "Case pattern": reads the body back from the variable and replays it once in a
 *                pattern frame with one query variable rebound and one boolean #flag set.
 *
 * Questions:
 *   S1  Are feature functions read back from a variable still remapped by the pattern frame
 *       (a Query Variable "created by" in the body follows the replayed copy)?
 *   S2  Does editing logic see variables set by earlier features (probe field)?
 *   S3  Is a body feature suppressed by #flag re-evaluated per replay, skipped always, or run always?
 *       (Also: is a suppressed feature still in the FeatureList?)
 *   S4  Does getAllVariables list query variables, and can a before/after diff find the
 *       variables the body set?
 */

const SPIKE_BODY_KEY = "-caseSpikeBody";
const MISSING = "__caseSpikeMissing__";

annotation { "Feature Type Name" : "Close case (test)" }
export const spikeBody = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Features to repeat" }
        definition.features is FeatureList;
    }
    {
        // Called by a Case pattern inside its pattern frame: replay the listed features (route B --
        // feature functions cannot be stored in a variable, setVariable fails "Execution error").
        if (isInFeaturePattern(context))
        {
            const functions = valuesSortedById(context, definition.features);
            for (var i = 0; i < size(functions); i += 1)
            {
                functions[i](id);
            }
            return;
        }
        setVariable(context, SPIKE_BODY_KEY, { "count" : size(definition.features) });
        reportFeatureInfo(context, id, "Published " ~ size(definition.features) ~ " feature functions.");
    });

annotation { "Feature Type Name" : "Case pattern (test)", "Editing Logic Function" : "spikeReplayEditLogic" }
export const spikeReplay = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Close case" }
        definition.closeCase is FeatureList;

        annotation { "Name" : "Query variable to rebind", "Default" : "seed", "MaxLength" : 64 }
        definition.seedName is string;

        annotation { "Name" : "New selection", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
        definition.seedQuery is Query;

        annotation { "Name" : "Boolean variable", "Default" : "flag", "MaxLength" : 64 }
        definition.flagName is string;

        annotation { "Name" : "Value for this replay", "Default" : false }
        definition.flagValue is boolean;

        annotation { "Name" : "Editing logic saw", "Default" : "", "UIHint" : UIHint.READ_ONLY }
        definition.probe is string;
    }
    {
        const stored = getVariable(context, SPIKE_BODY_KEY, MISSING);
        if (!(stored is map))
        {
            throw regenError("No Close case (test) before this feature.");
        }
        const functions = valuesSortedById(context, definition.closeCase);
        println("S2 editing logic saw: " ~ definition.probe);
        println("S3 functions in body: " ~ size(functions) ~ " (Close case listed " ~ stored.count ~ ")");

        const varsBefore = getAllVariables(context);
        println("S4 getAllVariables lists #" ~ definition.seedName ~ ": " ~ isIn(definition.seedName, keys(varsBefore)));

        const originalSeed = getQueryVariable(context, definition.seedName);
        const originalFlag = getVariable(context, definition.flagName, MISSING);
        setQueryVariable(context, definition.seedName, definition.seedQuery);
        setVariable(context, definition.flagName, definition.flagValue);

        const caseId = id + "case0";
        setFeaturePatternInstanceData(context, caseId, { "transform" : identityTransform() });
        var errors = [];
        for (var i = 0; i < size(functions); i += 1)
        {
            try
            {
                functions[i](caseId);
            }
            catch (e)
            {
                errors = append(errors, "feature " ~ (i + 1) ~ ": " ~ toString(e is map && e.message != undefined ? e.message : e));
            }
        }
        unsetFeaturePatternInstanceData(context, caseId);

        const varsAfter = getAllVariables(context);
        var setByBody = [];
        for (var name in keys(varsAfter))
        {
            if (name == definition.seedName || name == definition.flagName)
            {
                continue;
            }
            if (varsBefore[name] == undefined || varsBefore[name] != varsAfter[name])
            {
                setByBody = append(setByBody, name);
            }
        }
        const bodies = size(evaluateQuery(context, qSketchFilter(qCreatedBy(caseId, EntityType.BODY), SketchObject.NO)));
        const sketchBodies = size(evaluateQuery(context, qSketchFilter(qCreatedBy(caseId, EntityType.BODY), SketchObject.YES)));
        println("S1 bodies created by the replay: " ~ bodies ~ "; errors: " ~ (size(errors) == 0 ? "none" : join(errors, " | ")));
        println("S4 variables set or changed by the body: " ~ (size(setByBody) == 0 ? "none" : join(setByBody, ", ")));

        // S1: how much of each body-set query variable lies on this replay's geometry.
        var onReplay = {};
        for (var name in setByBody)
        {
            if (varsAfter[name] is Query)
            {
                onReplay[name] = size(evaluateQuery(context, varsAfter[name])) ~ " entities, "
                        ~ size(evaluateQuery(context, qIntersection([varsAfter[name], qCreatedBy(caseId)]))) ~ " created by the replay";
            }
        }
        setVariable(context, "-caseSpikeResult", {
                    "functions" : size(functions),
                    "listed" : stored.count,
                    "bodies" : bodies,
                    "sketchBodies" : sketchBodies,
                    "errors" : errors,
                    "seedListed" : isIn(definition.seedName, keys(varsBefore)),
                    "setByBody" : setByBody,
                    "onReplay" : onReplay,
                    "probe" : definition.probe
                });

        setQueryVariable(context, definition.seedName, originalSeed);
        if (originalFlag != MISSING)
        {
            setVariable(context, definition.flagName, originalFlag);
        }

        reportFeatureInfo(context, id, size(functions) ~ " functions, " ~ bodies ~ " bodies, "
                    ~ size(errors) ~ " errors; body set: " ~ (size(setByBody) == 0 ? "none" : join(setByBody, ", "))
                    ~ " (details in the console)");
    }, {
        "flagValue" : false,
        "probe" : ""
    });

/** S2: records what editing logic can read from the context before this feature. */
export function spikeReplayEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map) returns map
{
    const stored = getVariable(context, SPIKE_BODY_KEY, MISSING);
    const bodyText = stored is map ? "body with " ~ stored.count ~ " features" : "no body";
    definition.probe = bodyText ~ "; variables: " ~ join(keys(getAllVariables(context)), ", ");
    return definition;
}
