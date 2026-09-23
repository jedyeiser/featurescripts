FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: solver_core.fs
import(path : "3b906109aa60b4adbf7f9b60", version : "");

/**
 * Iterative Solve: re-runs a list of features over one variable until a result meets a condition.
 *
 * What a feature Pattern with "Reapply features" does when it is used as a solver, without its
 * pitfall: only the solution is returned. Each trial sets the iteration variable, re-runs the
 * listed features under its own id, measures, and is rolled back unless it meets the condition.
 * When a trial does, it is kept and the listed features' original bodies are deleted. When
 * none does, the feature fails and the model is left as it was.
 *
 * Nothing in the list may assign the iteration variable -- the solver sets it before each trial.
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

annotation { "Feature Type Name" : "Iterative Solve",
        "Feature Type Description" : "Re-runs a list of features over one variable until a result meets a condition; returns only the solution." }
export const iterativeSolve = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Features to iterate", "Description" : "Re-run on every trial, in tree order. None of them may assign the iteration variable." }
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
        for (var featureId, fn in definition.features)
        {
            idOf[featureId] = featureId;
        }
        const featureIds = valuesSortedById(context, idOf);

        // Everything the trials read must exist already: the listed features ran once upstream.
        const current = getVariable(context, definition.iterationName, undefined);
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
        if (definition.resultSource == SolveResultSource.VARIABLE && getVariable(context, definition.resultName, undefined) == undefined)
        {
            throw regenError("#" ~ definition.resultName ~ " is not set by the listed features.", ["resultName"]);
        }
        if (definition.targetFromVariable)
        {
            if (getVariable(context, definition.targetName, undefined) == undefined)
            {
                throw regenError("#" ~ definition.targetName ~ " is not defined.", ["targetName"]);
            }
            if (definition.method == SolveMethod.TARGET && getVariable(context, definition.toleranceName, undefined) == undefined)
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

        // Every trial reports here, so the kept solution's measurement is known without re-running it.
        const trialCount = new box(0);
        const lastTrial = new box({});

        const trial = function(x is number) returns map
            {
                const k = trialCount[];
                trialCount[] = k + 1;
                const trialId = id + ("trial" ~ k);
                const instanceId = trialId + "run";
                const value = x * unitValue;

                startFeature(context, trialId);
                setVariable(context, definition.iterationName, value);

                // The same frame a Pattern pushes around each instance (identity: nothing moves).
                setFeaturePatternInstanceData(context, instanceId, { "transform" : identityTransform() });
                var failure = undefined;
                var reassignedBy = undefined;
                for (var i = 0; i < size(features); i += 1)
                {
                    try
                    {
                        features[i](instanceId);
                    }
                    catch (e)
                    {
                        failure = "listed feature " ~ (i + 1) ~ " (" ~ toString(featureIds[i]) ~ ") failed: " ~ toString(e);
                        break;
                    }
                    if (getVariable(context, definition.iterationName, undefined) != value)
                    {
                        reassignedBy = i;
                        break;
                    }
                }
                unsetFeaturePatternInstanceData(context, instanceId);

                if (reassignedBy != undefined)
                {
                    abortFeature(context, trialId);
                    throw regenError("Listed feature " ~ (reassignedBy + 1) ~ " (" ~ toString(featureIds[reassignedBy]) ~ ") assigns #"
                        ~ definition.iterationName ~ "; the solver sets it on every trial. Remove that Variable feature from the list.", ["features"]);
                }

                var outcome = { "ok" : false, "accepted" : false, "residual" : undefined, "result" : undefined };
                if (failure == undefined)
                {
                    outcome = measureTrial(context, trialId, definition);
                    if (!outcome.ok)
                    {
                        failure = outcome.message;
                    }
                }

                println("trial " ~ k ~ ": #" ~ definition.iterationName ~ " = " ~ valueText(value)
                    ~ (failure == undefined
                        ? ("  result " ~ valueText(outcome.result) ~ "  target " ~ valueText(outcome.target)
                            ~ (outcome.accepted ? "  ACCEPTED" : ""))
                        : ("  FAILED: " ~ failure)));

                if (outcome.accepted)
                {
                    endFeature(context, trialId);
                }
                else
                {
                    abortFeature(context, trialId);
                }
                lastTrial[] = mergeMaps(outcome, { "value" : value });
                return outcome;
            };

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

        if (!solved.found)
        {
            // Throwing rolls the whole feature back: every trial is gone and the originals stay.
            throw regenError("No solution for #" ~ definition.iterationName ~ " after " ~ solved.trials
                ~ " trials. " ~ solved.message, ["features"]);
        }

        // The accepted trial is the last one run, and the only one not rolled back.
        const solution = lastTrial[];
        const originals = qCreatedBy(definition.features, EntityType.BODY);
        if (!isQueryEmpty(context, originals))
        {
            opDeleteBodies(context, id + "deleteOriginals", { "entities" : originals });
        }
        setVariable(context, definition.iterationName, solution.value);
        if (definition.resultSource != SolveResultSource.VARIABLE && definition.storeName != "")
        {
            setVariable(context, definition.storeName, solution.result);
        }

        reportFeatureInfo(context, id, "#" ~ definition.iterationName ~ " = " ~ valueText(solution.value)
            ~ " after " ~ solved.trials ~ " trials; result " ~ valueText(solution.result) ~ ".");
    });

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
        result = getVariable(context, definition.resultName, undefined);
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
