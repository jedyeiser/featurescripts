FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: solver_core.fs
import(path : "3b906109aa60b4adbf7f9b60", version : "");
// IMPORT: iterative_solve_icon.svg (feature icon)
IconNamespace::import(path : "e9d7259c3c694292b86e1ae0", version : "dea984a918fd44259ebe0e47");

/**
 * Iterative Solve: re-runs a list of features over one variable until a result meets a condition.
 *
 * What a feature Pattern with "Reapply features" does when it is used as a solver, without its
 * pitfall: only the solution is returned. Each trial sets the iteration variable, re-runs the
 * listed features under its own id, measures, and is deleted unless it meets the condition.
 * When a trial does, it is kept and the listed features' original bodies are deleted. When
 * none does, the feature fails and the model is left as it was.
 *
 * The solver owns the iteration variable: it sets it before each trial and restores it after any
 * listed feature that assigns it, so a Pattern-style folder with its own update step works as is.
 */

export enum SolveValueType
{
    annotation { "Name" : "Length" }
    LENGTH,
    annotation { "Name" : "Angle" }
    ANGLE,
    annotation { "Name" : "Number" }
    NUMBER
}

export enum SolveMethod
{
    annotation { "Name" : "Reach target" }
    TARGET,
    annotation { "Name" : "First value meeting condition" }
    FIRST_MATCH
}

export enum SolveStart
{
    annotation { "Name" : "Current value of the variable" }
    CURRENT,
    annotation { "Name" : "Both bounds" }
    BOUNDS
}

export enum SolveCondition
{
    annotation { "Name" : "Result < target" }
    LESS,
    annotation { "Name" : "Result <= target" }
    LESS_EQUAL,
    annotation { "Name" : "Result > target" }
    GREATER,
    annotation { "Name" : "Result >= target" }
    GREATER_EQUAL
}

export enum SolveResultSource
{
    annotation { "Name" : "Variable" }
    VARIABLE,
    annotation { "Name" : "Mass of created solids" }
    MASS,
    annotation { "Name" : "Volume of created solids" }
    VOLUME
}

export enum SolveResultType
{
    annotation { "Name" : "Number" }
    NUMBER,
    annotation { "Name" : "Length" }
    LENGTH,
    annotation { "Name" : "Angle" }
    ANGLE,
    annotation { "Name" : "Area" }
    AREA,
    annotation { "Name" : "Volume" }
    VOLUME
}

const SOLVE_REAL_BOUNDS = { (unitless) : [-1e12, 0, 1e12] } as RealBoundSpec;
const SOLVE_TOLERANCE_BOUNDS = { (unitless) : [0, 0.01, 1e12] } as RealBoundSpec;
const SOLVE_DENSITY_BOUNDS = { (unitless) : [1e-9, 1, 1e6] } as RealBoundSpec;
const SOLVE_STEPS_BOUNDS = { (unitless) : [1, 20, 1000] } as IntegerBoundSpec;
const SOLVE_STEP_PERCENT_BOUNDS = { (unitless) : [1e-6, 5, 100] } as RealBoundSpec;
const SOLVE_MAX_TRIALS_BOUNDS = { (unitless) : [2, 30, 1000] } as IntegerBoundSpec;

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Iterative Solve",
        "Feature Type Description" : "Re-runs a list of features over one variable until a result meets a condition; returns only the solution." }
export const iterativeSolve = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Features to iterate", "Description" : "Re-run on every trial, in tree order. A feature that assigns the iteration variable is overridden." }
        definition.features is FeatureList;

        annotation { "Name" : "Iteration variable", "Description" : "Variable name without #. Define it before the listed features." }
        definition.iterationName is string;

        annotation { "Name" : "Variable type" }
        definition.iterationType is SolveValueType;

        if (definition.iterationType == SolveValueType.LENGTH)
        {
            annotation { "Name" : "Lower bound" }
            isLength(definition.lowerLength, LENGTH_BOUNDS);

            annotation { "Name" : "Upper bound" }
            isLength(definition.upperLength, LENGTH_BOUNDS);
        }
        else if (definition.iterationType == SolveValueType.ANGLE)
        {
            annotation { "Name" : "Lower bound" }
            isAngle(definition.lowerAngle, ANGLE_360_ZERO_DEFAULT_BOUNDS);

            annotation { "Name" : "Upper bound" }
            isAngle(definition.upperAngle, ANGLE_360_ZERO_DEFAULT_BOUNDS);
        }
        else if (definition.iterationType == SolveValueType.NUMBER)
        {
            annotation { "Name" : "Lower bound" }
            isReal(definition.lowerNumber, SOLVE_REAL_BOUNDS);

            annotation { "Name" : "Upper bound" }
            isReal(definition.upperNumber, SOLVE_REAL_BOUNDS);
        }

        annotation { "Name" : "Method" }
        definition.method is SolveMethod;

        if (definition.method == SolveMethod.TARGET)
        {
            annotation { "Name" : "Start from", "Description" : "Current value: secant steps from the variable's upstream value, bounds only as limits. Both bounds: a trial at each bound first." }
            definition.start is SolveStart;

            if (definition.start == SolveStart.CURRENT)
            {
                annotation { "Name" : "First step (%)", "Description" : "The second trial, as a percentage of the starting value." }
                isReal(definition.stepPercent, SOLVE_STEP_PERCENT_BOUNDS);
            }
        }

        if (definition.method == SolveMethod.FIRST_MATCH)
        {
            annotation { "Name" : "Condition" }
            definition.condition is SolveCondition;

            annotation { "Name" : "Steps", "Description" : "Values tried from the lower bound to the upper, both included: steps + 1." }
            isInteger(definition.steps, SOLVE_STEPS_BOUNDS);
        }

        annotation { "Name" : "Result" }
        definition.resultSource is SolveResultSource;

        if (definition.resultSource == SolveResultSource.VARIABLE)
        {
            annotation { "Name" : "Result variable", "Description" : "Variable name without #, set by the listed features." }
            definition.resultName is string;
        }
        else
        {
            if (definition.resultSource == SolveResultSource.MASS)
            {
                annotation { "Name" : "Density (g/cm^3)" }
                isReal(definition.density, SOLVE_DENSITY_BOUNDS);
            }

            annotation { "Name" : "Store result as", "Description" : "Optional variable name for the measured result of the solution." }
            definition.storeName is string;
        }

        annotation { "Name" : "Target from variables", "Description" : "Read the target (and tolerance) from variables on every trial instead of typing them." }
        definition.targetFromVariable is boolean;

        if (definition.targetFromVariable)
        {
            annotation { "Name" : "Target variable", "Description" : "A plain number is read as grams for mass and cubic millimetres for volume." }
            definition.targetName is string;

            if (definition.method == SolveMethod.TARGET)
            {
                annotation { "Name" : "Tolerance variable" }
                definition.toleranceName is string;
            }
        }
        else
        {
            if (definition.resultSource == SolveResultSource.VARIABLE)
            {
                annotation { "Name" : "Result type" }
                definition.resultType is SolveResultType;
            }

            if (definition.resultSource == SolveResultSource.MASS)
            {
                annotation { "Name" : "Target (g)" }
                isReal(definition.targetMass, SOLVE_REAL_BOUNDS);

                if (definition.method == SolveMethod.TARGET)
                {
                    annotation { "Name" : "Tolerance (g)" }
                    isReal(definition.toleranceMass, SOLVE_TOLERANCE_BOUNDS);
                }
            }

            if (definition.resultSource == SolveResultSource.VOLUME ||
                (definition.resultSource == SolveResultSource.VARIABLE && definition.resultType == SolveResultType.VOLUME))
            {
                annotation { "Name" : "Target (mm^3)" }
                isReal(definition.targetVolume, SOLVE_REAL_BOUNDS);

                if (definition.method == SolveMethod.TARGET)
                {
                    annotation { "Name" : "Tolerance (mm^3)" }
                    isReal(definition.toleranceVolume, SOLVE_TOLERANCE_BOUNDS);
                }
            }

            if (definition.resultSource == SolveResultSource.VARIABLE && definition.resultType == SolveResultType.NUMBER)
            {
                annotation { "Name" : "Target" }
                isReal(definition.targetNumber, SOLVE_REAL_BOUNDS);

                if (definition.method == SolveMethod.TARGET)
                {
                    annotation { "Name" : "Tolerance" }
                    isReal(definition.toleranceNumber, SOLVE_TOLERANCE_BOUNDS);
                }
            }

            if (definition.resultSource == SolveResultSource.VARIABLE && definition.resultType == SolveResultType.LENGTH)
            {
                annotation { "Name" : "Target" }
                isLength(definition.targetLength, LENGTH_BOUNDS);

                if (definition.method == SolveMethod.TARGET)
                {
                    annotation { "Name" : "Tolerance" }
                    isLength(definition.toleranceLength, NONNEGATIVE_LENGTH_BOUNDS);
                }
            }

            if (definition.resultSource == SolveResultSource.VARIABLE && definition.resultType == SolveResultType.ANGLE)
            {
                annotation { "Name" : "Target" }
                isAngle(definition.targetAngle, ANGLE_360_ZERO_DEFAULT_BOUNDS);

                if (definition.method == SolveMethod.TARGET)
                {
                    annotation { "Name" : "Tolerance" }
                    isAngle(definition.toleranceAngle, ANGLE_360_ZERO_DEFAULT_BOUNDS);
                }
            }

            if (definition.resultSource == SolveResultSource.VARIABLE && definition.resultType == SolveResultType.AREA)
            {
                annotation { "Name" : "Target (mm^2)" }
                isReal(definition.targetArea, SOLVE_REAL_BOUNDS);

                if (definition.method == SolveMethod.TARGET)
                {
                    annotation { "Name" : "Tolerance (mm^2)" }
                    isReal(definition.toleranceArea, SOLVE_TOLERANCE_BOUNDS);
                }
            }
        }

        annotation { "Name" : "Max trials" }
        isInteger(definition.maxTrials, SOLVE_MAX_TRIALS_BOUNDS);

        annotation { "Name" : "Debug", "Description" : "Console diagnostics, a single trial at a chosen value, and keeping a failed trial." }
        definition.debug is boolean;

        if (definition.debug)
        {
            annotation { "Name" : "Watch variables", "Description" : "Names separated by commas or spaces, printed after every trial (and after every listed feature with the trace on)." }
            definition.watchNames is string;

            annotation { "Name" : "Feature trace", "Description" : "Each listed feature's outcome and the bodies it adds, for every trial." }
            definition.traceFeatures is boolean;

            annotation { "Name" : "Keep failed trial", "Description" : "Stop at the first trial that fails and keep its partial geometry instead of rolling it back." }
            definition.keepFailed is boolean;

            annotation { "Name" : "Run once at a value", "Description" : "Skip the search: build one trial at this value and keep it." }
            definition.probe is boolean;

            if (definition.probe && definition.iterationType == SolveValueType.LENGTH)
            {
                annotation { "Name" : "Value" }
                isLength(definition.probeLength, LENGTH_BOUNDS);
            }

            if (definition.probe && definition.iterationType == SolveValueType.ANGLE)
            {
                annotation { "Name" : "Value" }
                isAngle(definition.probeAngle, ANGLE_360_ZERO_DEFAULT_BOUNDS);
            }

            if (definition.probe && definition.iterationType == SolveValueType.NUMBER)
            {
                annotation { "Name" : "Value" }
                isReal(definition.probeNumber, SOLVE_REAL_BOUNDS);
            }
        }
    }
    {
        verifyVariableNameIsValid(definition.iterationName, "iterationName");
        if (definition.resultSource == SolveResultSource.VARIABLE)
        {
            verifyVariableNameIsValid(definition.resultName, "resultName");
        }
        else if (definition.storeName != "")
        {
            verifyVariableNameIsValid(definition.storeName, "storeName");
        }
        if (definition.targetFromVariable)
        {
            verifyVariableNameIsValid(definition.targetName, "targetName");
            if (definition.method == SolveMethod.TARGET)
            {
                verifyVariableNameIsValid(definition.toleranceName, "toleranceName");
            }
        }

        const features = valuesSortedById(context, definition.features);
        if (size(features) == 0)
        {
            throw regenError("Select the features to iterate.", ["features"]);
        }
        // The listed feature ids in the same order as `features`, to name a feature in messages.
        var idOf = {};
        for (var featureId in keys(definition.features))
        {
            idOf[featureId] = featureId;
        }
        const featureIds = valuesSortedById(context, idOf);

        // Everything the trials read must exist already: the listed features ran once upstream.
        const current = optionalVariable(context, definition.iterationName);
        if (current == undefined)
        {
            throw regenError("#" ~ definition.iterationName ~ " is not defined before this feature. Define it with a Variable feature"
                ~ " ahead of the listed features -- they must read it.", ["iterationName"]);
        }
        if (!matchesValueType(current, definition.iterationType))
        {
            throw regenError("#" ~ definition.iterationName ~ " is " ~ valueText(current) ~ ", which does not match the variable type.",
                ["iterationName", "iterationType"]);
        }
        if (definition.resultSource == SolveResultSource.VARIABLE && optionalVariable(context, definition.resultName) == undefined)
        {
            throw regenError("#" ~ definition.resultName ~ " is not set by the listed features.", ["resultName"]);
        }
        if (definition.targetFromVariable)
        {
            if (optionalVariable(context, definition.targetName) == undefined)
            {
                throw regenError("#" ~ definition.targetName ~ " is not defined.", ["targetName"]);
            }
            if (definition.method == SolveMethod.TARGET && optionalVariable(context, definition.toleranceName) == undefined)
            {
                throw regenError("#" ~ definition.toleranceName ~ " is not defined.", ["toleranceName"]);
            }
        }

        const unitValue = iterationUnit(definition.iterationType);
        const bounds = iterationBounds(definition);
        if (bounds[0] == bounds[1])
        {
            throw regenError("The lower and upper bounds are equal.", ["lowerLength", "upperLength", "lowerAngle", "upperAngle", "lowerNumber", "upperNumber"]);
        }

        const debug = definition.debug == true;
        const watch = debug ? watchList(definition.watchNames) : [];
        const trace = debug && definition.traceFeatures == true;
        const keepFailed = debug && definition.keepFailed == true;
        const probe = debug && definition.probe == true;

        // Every trial reports here, so the kept solution's measurement is known without re-running it.
        const trialCount = new box(0);
        const lastTrial = new box({});
        const trialLog = new box([]);
        // Set when a failed trial is kept (debug): later trials are skipped without building anything.
        const stoppedAt = new box(undefined);
        // Positions of listed features seen assigning the iteration variable (reported once each).
        const overriddenBy = new box([]);

        const trial = function(x is number, kind is string) returns map
            {
                const k = trialCount[];
                trialCount[] = k + 1;
                const value = x * unitValue;
                if (stoppedAt[] != undefined)
                {
                    return { "ok" : false, "accepted" : false, "residual" : undefined, "result" : undefined, "skipped" : true };
                }
                // The listed features run under id + "trialK", as a Pattern runs an instance under
                // id + "1". Not inside startFeature: a re-run sketch builds nothing in a started
                // subfeature (verified 2026-09-22), so a trial is discarded by deleting its bodies.
                const trialId = id + ("trial" ~ k);
                const instanceId = trialId;

                setVariable(context, definition.iterationName, value);
                if (trace)
                {
                    println("trial " ~ k ~ " (" ~ kind ~ "): #" ~ definition.iterationName ~ " = " ~ valueText(value));
                }

                // The same frame a Pattern pushes around each instance (identity: nothing moves).
                setFeaturePatternInstanceData(context, instanceId, { "transform" : identityTransform() });
                var failure = undefined;
                var counts = trace ? bodyCounts(context, qCreatedBy(trialId, EntityType.BODY)) : undefined;
                for (var i = 0; i < size(features); i += 1)
                {
                    try
                    {
                        features[i](instanceId);
                    }
                    catch (e)
                    {
                        failure = "listed feature " ~ (i + 1) ~ " (" ~ toString(featureIds[i]) ~ ") failed: " ~ toString(e);
                        if (trace)
                        {
                            println("    " ~ (i + 1) ~ " " ~ toString(featureIds[i]) ~ "  FAILED: " ~ toString(e));
                        }
                        break;
                    }
                    if (trace)
                    {
                        const after = bodyCounts(context, qCreatedBy(trialId, EntityType.BODY));
                        println("    " ~ (i + 1) ~ " " ~ toString(featureIds[i]) ~ "  ok  " ~ countsDelta(counts, after)
                            ~ watchText(context, watch));
                        counts = after;
                    }
                    // A listed feature that updates the variable itself (a Pattern-style solver's own
                    // step) is overridden before the next feature reads it: the solver owns the value.
                    if (optionalVariable(context, definition.iterationName) != value)
                    {
                        setVariable(context, definition.iterationName, value);
                        if (!isIn(i, overriddenBy[]))
                        {
                            overriddenBy[] = append(overriddenBy[], i);
                            println("NOTE: listed feature " ~ (i + 1) ~ " (" ~ toString(featureIds[i]) ~ ") assigns #"
                                ~ definition.iterationName ~ "; the solver's value is restored after it on every trial.");
                        }
                    }
                }
                unsetFeaturePatternInstanceData(context, instanceId);

                var outcome = { "ok" : false, "accepted" : false, "residual" : undefined, "result" : undefined };
                if (failure == undefined)
                {
                    outcome = measureTrial(context, trialId, definition);
                    if (!outcome.ok)
                    {
                        failure = outcome.message;
                    }
                }

                println("trial " ~ k ~ " (" ~ kind ~ "): #" ~ definition.iterationName ~ " = " ~ valueText(value)
                    ~ (failure == undefined
                        ? ("  result " ~ valueText(outcome.result) ~ "  target " ~ valueText(outcome.target)
                            ~ (outcome.accepted ? "  ACCEPTED" : ""))
                        : ("  FAILED: " ~ failure))
                    ~ (trace ? "" : watchText(context, watch)));

                const keep = outcome.accepted || probe || (keepFailed && failure != undefined);
                if (!keep)
                {
                    discardTrial(context, trialId);
                }
                if (keepFailed && failure != undefined)
                {
                    stoppedAt[] = k;
                }
                trialLog[] = append(trialLog[], { "k" : k, "kind" : kind, "value" : value, "result" : outcome.result,
                            "residual" : outcome.residual, "accepted" : outcome.accepted, "failure" : failure });
                lastTrial[] = mergeMaps(outcome, { "value" : value, "failure" : failure });
                return outcome;
            };

        if (probe)
        {
            // One trial at the typed value, kept whatever it gives; the originals go so it is seen alone.
            const probed = trial(probeValue(definition), "probe");
            deleteOriginals(context, id, definition);
            printTrialLog(trialLog[], debug);
            const solution = lastTrial[];
            if (solution.failure != undefined)
            {
                reportFeatureWarning(context, id, "Run once at " ~ valueText(solution.value) ~ ": " ~ solution.failure);
                return;
            }
            reportFeatureInfo(context, id, "Run once at " ~ valueText(solution.value) ~ ": result " ~ valueText(solution.result)
                ~ ", residual " ~ toString(probed.residual) ~ (probed.accepted ? " (meets the condition)." : " (does not meet the condition)."));
            return;
        }

        var solved;
        if (definition.method == SolveMethod.FIRST_MATCH)
        {
            solved = solveFirstMatch(trial, bounds[0], bounds[1], definition.steps, definition.maxTrials);
        }
        else if (definition.start == SolveStart.BOUNDS)
        {
            solved = solveTargetBracketed(trial, bounds[0], bounds[1], definition.maxTrials);
        }
        else
        {
            const x0 = (current is ValueWithUnits) ? current.value : current;
            var step = abs(x0) * definition.stepPercent / 100;
            if (step == 0)
            {
                step = abs(bounds[1] - bounds[0]) * definition.stepPercent / 100;
            }
            solved = solveTargetFromStart(trial, x0, step, bounds[0], bounds[1], definition.maxTrials);
        }
        printTrialLog(trialLog[], debug);

        if (stoppedAt[] != undefined)
        {
            // Debug: the failed trial was kept; show it alone and report instead of rolling back.
            deleteOriginals(context, id, definition);
            const stopped = lastTrial[];
            reportFeatureWarning(context, id, "Stopped at trial " ~ stoppedAt[] ~ " and kept its partial geometry: " ~ stopped.failure);
            return;
        }

        if (!solved.found)
        {
            // Throwing rolls the whole feature back: every trial is gone and the originals stay.
            throw regenError("No solution for #" ~ definition.iterationName ~ " after " ~ size(trialLog[])
                ~ " trials. " ~ solved.message, ["features"]);
        }

        // The accepted trial is the last one run, and the only one not rolled back.
        const solution = lastTrial[];
        deleteOriginals(context, id, definition);
        setVariable(context, definition.iterationName, solution.value);
        if (definition.resultSource != SolveResultSource.VARIABLE && definition.storeName != "")
        {
            setVariable(context, definition.storeName, solution.result);
        }

        reportFeatureInfo(context, id, "#" ~ definition.iterationName ~ " = " ~ valueText(solution.value)
            ~ " after " ~ size(trialLog[]) ~ " trials; result " ~ valueText(solution.result) ~ ".");
    });

/**
 * Deletes the bodies the listed features built before this feature, so only a trial remains.
 */
function deleteOriginals(context is Context, id is Id, definition is map)
{
    const originals = qCreatedBy(definition.features, EntityType.BODY);
    if (!isQueryEmpty(context, originals))
    {
        opDeleteBodies(context, id + "deleteOriginals", { "entities" : originals });
    }
}

/**
 * The debug single-trial value, as an SI number.
 */
function probeValue(definition is map) returns number
{
    if (definition.iterationType == SolveValueType.LENGTH)
    {
        return definition.probeLength.value;
    }
    if (definition.iterationType == SolveValueType.ANGLE)
    {
        return definition.probeAngle.value;
    }
    return definition.probeNumber;
}

/**
 * Watched variable names from the debug field: commas or spaces between them.
 */
function watchList(names is string) returns array
{
    var out = [];
    for (var name in splitByRegexp(names, "[\\s,]+"))
    {
        if (name != "")
        {
            out = append(out, name);
        }
    }
    return out;
}

/**
 * "  name = value" for each watched variable, or "" when nothing is watched.
 */
function watchText(context is Context, watch is array) returns string
{
    var text = "";
    for (var name in watch)
    {
        const value = optionalVariable(context, name);
        text = text ~ "  " ~ name ~ " = " ~ (value == undefined ? "(unset)" : valueText(value));
    }
    return text;
}

/**
 * Solid / sheet / wire / point body counts of a query.
 */
function bodyCounts(context is Context, bodies is Query) returns map
{
    return {
            "solid" : size(evaluateQuery(context, qBodyType(bodies, BodyType.SOLID))),
            "sheet" : size(evaluateQuery(context, qBodyType(bodies, BodyType.SHEET))),
            "wire" : size(evaluateQuery(context, qBodyType(bodies, BodyType.WIRE))),
            "point" : size(evaluateQuery(context, qBodyType(bodies, BodyType.POINT)))
        };
}

/**
 * The change in body counts across one listed feature, e.g. "+1 solid -1 sheet", or "no body change".
 */
function countsDelta(before is map, after is map) returns string
{
    var parts = [];
    for (var kind in ["solid", "sheet", "wire", "point"])
    {
        const change = after[kind] - before[kind];
        if (change != 0)
        {
            parts = append(parts, ((change > 0) ? "+" : "") ~ change ~ " " ~ kind);
        }
    }
    return size(parts) == 0 ? "no body change" : join(parts, " ");
}

/**
 * The end-of-solve table (debug only): trial, step kind, value, result, residual, outcome.
 */
function printTrialLog(log is array, debug is boolean)
{
    if (!debug || size(log) == 0)
    {
        return;
    }
    println("---- trials ----");
    for (var entry in log)
    {
        println(entry.k ~ "  " ~ entry.kind ~ "  " ~ valueText(entry.value)
            ~ (entry.failure != undefined
                ? ("  FAILED: " ~ entry.failure)
                : ("  result " ~ valueText(entry.result) ~ "  residual " ~ toString(entry.residual)
                    ~ (entry.accepted ? "  ACCEPTED" : ""))));
    }
}

/** Marks "no such variable" for [optionalVariable]; not a value any feature publishes. */
const MISSING_VARIABLE = "__missing_variable__";

/**
 * The variable `name`, or undefined when there is none. (getVariable with an undefined
 * default does NOT do this: `{ defaultValue : undefined }` is no default, and it throws.)
 */
export function optionalVariable(context is Context, name is string)
{
    const value = getVariable(context, name, MISSING_VARIABLE);
    return value == MISSING_VARIABLE ? undefined : value;
}

/**
 * The unit an SI iteration value is multiplied by.
 */
function iterationUnit(valueType is SolveValueType)
{
    if (valueType == SolveValueType.LENGTH)
    {
        return meter;
    }
    if (valueType == SolveValueType.ANGLE)
    {
        return radian;
    }
    return 1;
}

/**
 * True when a variable's value has the units the iteration type gives it.
 */
function matchesValueType(value, valueType is SolveValueType) returns boolean
{
    if (valueType == SolveValueType.LENGTH)
    {
        return value is ValueWithUnits && value.unit == LENGTH_UNITS;
    }
    if (valueType == SolveValueType.ANGLE)
    {
        return value is ValueWithUnits && value.unit == ANGLE_UNITS;
    }
    return value is number;
}

/**
 * [lower, upper] as SI numbers.
 */
function iterationBounds(definition is map) returns array
{
    if (definition.iterationType == SolveValueType.LENGTH)
    {
        return [definition.lowerLength.value, definition.upperLength.value];
    }
    if (definition.iterationType == SolveValueType.ANGLE)
    {
        return [definition.lowerAngle.value, definition.upperAngle.value];
    }
    return [definition.lowerNumber, definition.upperNumber];
}

/**
 * Measures one trial after its features have run: result, target, residual and acceptance.
 * Returns { ok, accepted, residual (SI number), result, target, message }.
 */
function measureTrial(context is Context, trialId is Id, definition is map) returns map
{
    var outcome = { "ok" : false, "accepted" : false, "residual" : undefined, "result" : undefined, "target" : undefined, "message" : "" };

    var result;
    if (definition.resultSource == SolveResultSource.VARIABLE)
    {
        result = optionalVariable(context, definition.resultName);
        if (result == undefined)
        {
            outcome.message = "#" ~ definition.resultName ~ " was not set";
            return outcome;
        }
    }
    else
    {
        const solids = qBodyType(qCreatedBy(trialId, EntityType.BODY), BodyType.SOLID);
        if (isQueryEmpty(context, solids))
        {
            outcome.message = "the listed features created no solid";
            return outcome;
        }
        const volume = evVolume(context, { "entities" : solids, "accuracy" : VolumeAccuracy.HIGH });
        result = definition.resultSource == SolveResultSource.MASS
            ? volume * definition.density * gram / centimeter ^ 3
            : volume;
    }
    outcome.result = result;

    const target = trialTarget(context, definition);
    outcome.target = target;
    const residual = siDifference(result, target);
    if (residual == undefined)
    {
        outcome.message = "the result " ~ valueText(result) ~ " and the target " ~ valueText(target) ~ " have different units";
        return outcome;
    }
    outcome.ok = true;
    outcome.residual = residual;

    if (definition.method == SolveMethod.TARGET)
    {
        const tolerance = siMagnitude(trialTolerance(context, definition), result);
        if (tolerance == undefined)
        {
            outcome.ok = false;
            outcome.message = "the tolerance and the result have different units";
            return outcome;
        }
        outcome.accepted = abs(residual) <= tolerance;
    }
    else if (definition.condition == SolveCondition.LESS)
    {
        outcome.accepted = residual < 0;
    }
    else if (definition.condition == SolveCondition.LESS_EQUAL)
    {
        outcome.accepted = residual <= 0;
    }
    else if (definition.condition == SolveCondition.GREATER)
    {
        outcome.accepted = residual > 0;
    }
    else
    {
        outcome.accepted = residual >= 0;
    }
    return outcome;
}

/**
 * The target, read on every trial when it comes from a variable (it may be computed by the list).
 */
function trialTarget(context is Context, definition is map)
{
    if (definition.targetFromVariable)
    {
        return withResultUnit(definition, getVariable(context, definition.targetName));
    }
    if (definition.resultSource == SolveResultSource.MASS)
    {
        return definition.targetMass * gram;
    }
    if (definition.resultSource == SolveResultSource.VOLUME)
    {
        return definition.targetVolume * cubicMillimeter;
    }
    if (definition.resultType == SolveResultType.LENGTH)
    {
        return definition.targetLength;
    }
    if (definition.resultType == SolveResultType.ANGLE)
    {
        return definition.targetAngle;
    }
    if (definition.resultType == SolveResultType.AREA)
    {
        return definition.targetArea * squareMillimeter;
    }
    if (definition.resultType == SolveResultType.VOLUME)
    {
        return definition.targetVolume * cubicMillimeter;
    }
    return definition.targetNumber;
}

function trialTolerance(context is Context, definition is map)
{
    if (definition.targetFromVariable)
    {
        return withResultUnit(definition, getVariable(context, definition.toleranceName));
    }
    if (definition.resultSource == SolveResultSource.MASS)
    {
        return definition.toleranceMass * gram;
    }
    if (definition.resultSource == SolveResultSource.VOLUME)
    {
        return definition.toleranceVolume * cubicMillimeter;
    }
    if (definition.resultType == SolveResultType.LENGTH)
    {
        return definition.toleranceLength;
    }
    if (definition.resultType == SolveResultType.ANGLE)
    {
        return definition.toleranceAngle;
    }
    if (definition.resultType == SolveResultType.AREA)
    {
        return definition.toleranceArea * squareMillimeter;
    }
    if (definition.resultType == SolveResultType.VOLUME)
    {
        return definition.toleranceVolume * cubicMillimeter;
    }
    return definition.toleranceNumber;
}

/**
 * A plain number read from a variable, in the unit the dialog states for a measured result.
 */
function withResultUnit(definition is map, value)
{
    if (value is number && definition.resultSource == SolveResultSource.MASS)
    {
        return value * gram;
    }
    if (value is number && definition.resultSource == SolveResultSource.VOLUME)
    {
        return value * cubicMillimeter;
    }
    return value;
}

/**
 * a - b as an SI number, or undefined when the units differ.
 */
function siDifference(a, b)
{
    if (a is number && b is number)
    {
        return a - b;
    }
    if (a is ValueWithUnits && b is ValueWithUnits && a.unit == b.unit)
    {
        return (a - b).value;
    }
    return undefined;
}

/**
 * |v| as an SI number in the units of `like`, or undefined when they differ.
 */
function siMagnitude(v, like)
{
    if (v is number && like is number)
    {
        return abs(v);
    }
    if (v is ValueWithUnits && like is ValueWithUnits && v.unit == like.unit)
    {
        return abs(v.value);
    }
    return undefined;
}

/**
 * A value for the console and the feature notice, in mm / deg / g rather than SI.
 */
function valueText(v) returns string
{
    if (v is ValueWithUnits)
    {
        if (v.unit == LENGTH_UNITS)
        {
            return toString(roundToPrecision(v / millimeter, 5)) ~ " mm";
        }
        if (v.unit == ANGLE_UNITS)
        {
            return toString(roundToPrecision(v / degree, 5)) ~ " deg";
        }
        if (v.unit == MASS_UNITS)
        {
            return toString(roundToPrecision(v / gram, 5)) ~ " g";
        }
        if (v.unit == AREA_UNITS)
        {
            return toString(roundToPrecision(v / squareMillimeter, 4)) ~ " mm^2";
        }
        if (v.unit == VOLUME_UNITS)
        {
            return toString(roundToPrecision(v / cubicMillimeter, 3)) ~ " mm^3";
        }
    }
    if (v is number)
    {
        return toString(roundToPrecision(v, 6));
    }
    return toString(v);
}

/**
 * Discards a trial by deleting every body it built (sketches, tool surfaces and wires included).
 * A listed feature that modified a body from outside the list is NOT undone by this.
 */
function discardTrial(context is Context, trialId is Id)
{
    const made = qCreatedBy(trialId, EntityType.BODY);
    if (!isQueryEmpty(context, made))
    {
        opDeleteBodies(context, trialId + "discard", { "entities" : made });
    }
}
