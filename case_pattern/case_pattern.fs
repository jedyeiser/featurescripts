FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
import(path : "onshape/std/queryVariable.fs", version : "3083.0");
// IMPORT: case_template_icon.svg (feature icon)
TemplateIconNamespace::import(path : "9351cbcfff2de180accfa3c3", version : "592bf744e05dc472b1a5301a");
// IMPORT: case_pattern_icon.svg (feature icon)
PatternIconNamespace::import(path : "43ecc8444d55ee8be1ecba57", version : "7de38dec2759e6efe06cf4e7");

/**
 * Case Pattern v2: a repeatable feature chain, written like a function and called per case.
 *
 * Define case     the signature: declares the inputs (query variables) and values (# variables) with
 *                 case 1's selections and values, and binds them. Also sets #caseName and #caseIndex.
 * (features)      the body, built on those names. Variables made here are locals: recomputed per case.
 * Close case      ends the body: points back to its Define case, lists the features to repeat, and
 *                 declares outputs, published per case as #<case>_<name>.
 * Case pattern    a call: picks a Close case, and for each case row binds new selections and values
 *                 and re-runs the body the way a Pattern with "Reapply features" runs an instance.
 *
 * Mechanism (correction 50): a FeatureList cannot be stored in a variable, so Case pattern calls the
 * Close case's own feature function inside its pattern frame, and the Close case -- seeing it is in a
 * pattern -- replays its listed features. Only FeatureList references and query variables follow each
 * case; clicks stay on case 1 (correction 41). Design: case_pattern/DESIGN.md section 11.
 */

/** Most inputs (query variables) one Define case may declare. */
export const CASE_MAX_INPUTS = 8;

/** Most values (# variables) one Define case may declare. */
export const CASE_MAX_VALUES = 6;

/** Returned by getVariable when a name is not set (an undefined default still throws, correction 32). */
const MISSING = "__caseMissing__";

/** Set by Case pattern around each call of a Close case: { caseId, caseName }. */
const CASE_REPLAY_KEY = "-caseReplay";

/** Set by a replayed Close case for its Case pattern: { origins, outputs, outside }. */
const CASE_RESULT_KEY = "-caseReplayResult";

/** Names reserved for the per-case variables every case sets. */
const CASE_RESERVED_NAMES = ["caseName", "caseIndex"];

const CASE_LENGTH_BOUNDS = { (millimeter) : [-1e7, 0, 1e7] } as LengthBoundSpec;
const CASE_ANGLE_BOUNDS = { (degree) : [-1e6, 0, 1e6] } as AngleBoundSpec;
// Area and volume are not dialog parameter types (correction 30): entered in mm^2 / mm^3.
const CASE_REAL_BOUNDS = { (unitless) : [-1e12, 0, 1e12] } as RealBoundSpec;
const CASE_INTEGER_BOUNDS = { (unitless) : [-1e6, 0, 1e6] } as IntegerBoundSpec;

/** The type of a case value. */
export enum CaseValueKind
{
    annotation { "Name" : "Length" }
    LENGTH,
    annotation { "Name" : "Angle" }
    ANGLE,
    annotation { "Name" : "Area" }
    AREA,
    annotation { "Name" : "Volume" }
    VOLUME,
    annotation { "Name" : "Number" }
    NUMBER,
    annotation { "Name" : "Integer" }
    INTEGER,
    annotation { "Name" : "Text" }
    TEXT,
    annotation { "Name" : "Boolean" }
    BOOLEAN
}

/** A case row's value slot type, set by the editing logic (NONE: slot unused). */
export enum CaseSlotKind
{
    NONE,
    LENGTH,
    ANGLE,
    AREA,
    VOLUME,
    NUMBER,
    INTEGER,
    TEXT,
    BOOLEAN
}

// ---------------------------------------------------------------------------------------------
// Define case
// ---------------------------------------------------------------------------------------------

annotation { "Feature Type Name" : "Define case", "Icon" : TemplateIconNamespace::BLOB_DATA,
        "Feature Type Description" : "Declares the inputs (query variables) and values (# variables) of a repeatable feature chain, and binds case 1. Build the features on these names, end them with Close case, then repeat them with Case pattern." }
export const defineCase = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Case 1 name", "Default" : "A", "MaxLength" : 64,
                    "Description" : "Letters, digits and _, starting with a letter. Outputs are published as #<case>_<name>; Case pattern swaps this suffix for each case's name when naming parts." }
        definition.caseName is string;

        annotation { "Name" : "Inputs", "Item name" : "Input", "Item label template" : "#inputName", "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS }
        definition.inputs is array;
        for (var input in definition.inputs)
        {
            annotation { "Name" : "Name", "Default" : "", "MaxLength" : 64,
                        "Description" : "Query variable name. It may already exist; it is redefined here." }
            input.inputName is string;

            annotation { "Name" : "Case 1 selection", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
            input.query is Query;
        }

        annotation { "Name" : "Values", "Item name" : "Value", "Item label template" : "#valueName", "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS,
                    "Description" : "# variables that change per case. Each is defined here with case 1's value; every case gives its own (case 1's by default)." }
        definition.values is array;
        for (var value in definition.values)
        {
            annotation { "Name" : "Name", "Default" : "", "MaxLength" : 64 }
            value.valueName is string;

            annotation { "Name" : "Type", "UIHint" : [UIHint.SHOW_LABEL] }
            value.valueKind is CaseValueKind;

            if (value.valueKind == CaseValueKind.LENGTH)
            {
                annotation { "Name" : "Case 1 value" }
                isLength(value.valueLength, CASE_LENGTH_BOUNDS);
            }
            if (value.valueKind == CaseValueKind.ANGLE)
            {
                annotation { "Name" : "Case 1 value" }
                isAngle(value.valueAngle, CASE_ANGLE_BOUNDS);
            }
            if (value.valueKind == CaseValueKind.AREA)
            {
                annotation { "Name" : "Case 1 value (mm^2)" }
                isReal(value.valueArea, CASE_REAL_BOUNDS);
            }
            if (value.valueKind == CaseValueKind.VOLUME)
            {
                annotation { "Name" : "Case 1 value (mm^3)" }
                isReal(value.valueVolume, CASE_REAL_BOUNDS);
            }
            if (value.valueKind == CaseValueKind.NUMBER)
            {
                annotation { "Name" : "Case 1 value" }
                isReal(value.valueNumber, CASE_REAL_BOUNDS);
            }
            if (value.valueKind == CaseValueKind.INTEGER)
            {
                annotation { "Name" : "Case 1 value" }
                isInteger(value.valueInteger, CASE_INTEGER_BOUNDS);
            }
            if (value.valueKind == CaseValueKind.TEXT)
            {
                annotation { "Name" : "Case 1 value", "Default" : "" }
                value.valueText is string;
            }
            if (value.valueKind == CaseValueKind.BOOLEAN)
            {
                annotation { "Name" : "Case 1 value", "Default" : "true", "Description" : "An expression that is true or false: true, false, #other, !#other." }
                isAnything(value.valueBoolean);
            }
        }
    }
    {
        // Re-run inside a pattern (listed by mistake): the Case pattern has already bound this case.
        if (isInFeaturePattern(context))
        {
            return;
        }

        verifyCaseName(definition.caseName, "caseName");
        const count = size(definition.inputs);
        const valueCount = size(definition.values);
        if (count == 0 && valueCount == 0)
        {
            throw regenError("Add at least one input or value.", ["inputs"]);
        }
        if (count > CASE_MAX_INPUTS)
        {
            throw regenError("A Define case takes at most " ~ CASE_MAX_INPUTS ~ " inputs.", ["inputs"]);
        }
        if (valueCount > CASE_MAX_VALUES)
        {
            throw regenError("A Define case takes at most " ~ CASE_MAX_VALUES ~ " values.", ["values"]);
        }

        var names = [];
        var queries = [];
        for (var input in definition.inputs)
        {
            verifyDeclaredName(input.inputName, names, "inputs");
            if (isQueryEmpty(context, input.query))
            {
                throw regenError("Case 1 selection for #" ~ input.inputName ~ " selects nothing.", ["inputs"]);
            }
            setQueryVariable(context, input.inputName, input.query);
            names = append(names, input.inputName);
            queries = append(queries, input.query);
        }

        var valueNames = [];
        var valueKinds = [];
        var caseOneValues = [];
        for (var value in definition.values)
        {
            verifyDeclaredName(value.valueName, concatenateArrays([names, valueNames]), "values");
            const caseOne = typedValue(value, "value", value.valueKind);
            verifyValueKind(caseOne, value.valueKind, value.valueName, "values");
            setVariable(context, value.valueName, caseOne);
            valueNames = append(valueNames, value.valueName);
            valueKinds = append(valueKinds, value.valueKind);
            caseOneValues = append(caseOneValues, caseOne);
        }

        setVariable(context, "caseName", definition.caseName);
        setVariable(context, "caseIndex", 1);
        setVariable(context, caseNamesKey(toString(id)), [definition.caseName]);
        setVariable(context, toString(id), {
                    "caseDefine" : true,
                    "caseName" : definition.caseName,
                    "names" : names,
                    "queries" : queries,
                    "valueNames" : valueNames,
                    "valueKinds" : valueKinds,
                    "values" : caseOneValues
                }, "Define case");
    }, {
        "caseName" : "A",
        "inputs" : [],
        "values" : []
    });

// ---------------------------------------------------------------------------------------------
// Close case
// ---------------------------------------------------------------------------------------------

annotation { "Feature Type Name" : "Close case", "Icon" : TemplateIconNamespace::BLOB_DATA,
        "Editing Logic Function" : "closeCaseEditLogic",
        "Feature Type Description" : "Ends a repeatable feature chain: names its Define case, lists the features to repeat, and declares the outputs every case publishes as #<case>_<name>. Repeat it with Case pattern." }
export const closeCase = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Define case", "Description" : "The Define case declaring the inputs these features use." }
        definition.defineCase is FeatureList;

        annotation { "Name" : "Features to repeat",
                    "Description" : "The features built on the Define case's inputs. Reference geometry they create through a Query Variable, not by clicking it." }
        definition.features is FeatureList;

        annotation { "Name" : "Outputs", "Item name" : "Output", "Item label template" : "#outputName", "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS,
                    "Description" : "Query variables every case publishes as #<case>_<name>, case 1 included." }
        definition.outputs is array;
        for (var output in definition.outputs)
        {
            annotation { "Name" : "Name", "Default" : "", "MaxLength" : 64 }
            output.outputName is string;

            annotation { "Name" : "Query", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR,
                        "Description" : "Usually a query variable made inside the repeated features. A clicked selection stays on case 1's geometry." }
            output.outputQuery is Query;

            annotation { "Name" : "Evaluate on use", "Default" : false,
                        "Description" : "Off: the entities are fixed when the case closes (they follow identity-preserving edits). On: the query is stored and re-evaluated wherever the variable is used." }
            output.outputOnUse is boolean;

            if (!output.outputOnUse)
            {
                annotation { "Name" : "Track downstream changes", "Default" : false,
                            "Description" : "Also include entities later derived from these (the halves of a split edge). The same name can then mean different entities before and after an edit." }
                output.outputTrack is boolean;
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

        annotation { "Name" : "Name separator", "Default" : "_", "MaxLength" : 8,
                    "Description" : "Between a part's base name and its case name (Rib_A -> Rib_B)." }
        definition.separator is string;

        // Case 1 body names, cached by the editing logic (getProperty throws during regen,
        // correction 36). One line per body: "<feature index>\t<body index>\t<name>".
        annotation { "Name" : "Template names", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.templateNames is string;
    }
    {
        // Called by a Case pattern: run this case.
        const replay = getVariable(context, CASE_REPLAY_KEY, MISSING);
        if (replay is map)
        {
            replayCase(context, id, definition, replay);
            return;
        }
        if (isInFeaturePattern(context))
        {
            throw regenError("A Close case is repeated only by a Case pattern.");
        }

        const define = findSignature(context, definition.defineCase, "caseDefine");
        if (define == undefined)
        {
            throw regenError("Select the Define case.", ["defineCase"]);
        }
        if (define.count > 1)
        {
            throw regenError("Select one Define case.", ["defineCase"]);
        }
        if (size(definition.features) == 0)
        {
            throw regenError("Select the features built on the Define case's inputs.", ["features"]);
        }
        var outputNames = [];
        var outputs = [];
        for (var output in definition.outputs)
        {
            verifyDeclaredName(output.outputName, outputNames, "outputs");
            outputNames = append(outputNames, output.outputName);
            const onUse = output.outputOnUse;
            const track = !onUse && output.outputTrack == true;
            outputs = append(outputs, { "name" : output.outputName, "onUse" : onUse, "track" : track });
            publishCaseOutput(context, define.signature.caseName, output.outputName, output.outputQuery, onUse, track, "outputs");
        }

        var signature = define.signature;
        signature.caseDefine = undefined;
        signature.caseClose = true;
        signature.defineKey = toString(define.featureId);
        signature.outputs = outputs;
        signature.templateNames = definition.templateNames;
        signature.separator = definition.separator;
        signature.keep = {
                "keepParts" : definition.keepParts,
                "keepSurfaces" : definition.keepSurfaces,
                "keepCurves" : definition.keepCurves,
                "keepMateConnectors" : definition.keepMateConnectors,
                "keepPlanes" : definition.keepPlanes,
                "keepSketches" : definition.keepSketches
            };
        signature.containsSketch = containsSketch(context, definition.features);
        setVariable(context, toString(id), signature, "Close case");
    }, {
        "outputs" : [],
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
 * One case, or one part of it, run by the Close case when its Case pattern calls it after binding
 * the case's inputs and values.
 *
 * "frame" mode (the Case pattern has pushed the pattern frame): runs the listed features from
 * replay.from on. A kernel op that edits geometry from outside the list (fillet an existing edge,
 * move an existing face) refuses in the frame with SELF_INTERSECTING_CURVE_SELECTED -- Query
 * Pattern's open Move face bug. The frame was pushed by the Case pattern and cannot be popped here
 * ("Execution error"; a frame pushed inside this feature builds nothing -- both measured 2026-09-26),
 * so the replay stops and reports stoppedAt; the Case pattern pops the frame and calls again in
 * "direct" mode to run just that feature, then continues in frame mode. Any other failure stands, so
 * a feature whose in-list reference did not remap cannot fall back onto case 1's geometry.
 *
 * Records the bodies each listed feature creates (for naming) and, once the last feature ran, the
 * outputs' queries -- evaluated here, after the case's own query variables were set.
 */
function replayCase(context is Context, id is Id, definition is map, replay is map)
{
    const functions = valuesSortedById(context, definition.features);
    var origins = [];
    var before = evaluateQuery(context, qCreatedBy(replay.caseId, EntityType.BODY));
    const first = replay.mode == "direct" ? replay.index : replay.from;
    const last = replay.mode == "direct" ? replay.index : size(functions) - 1;
    for (var i = first; i <= last; i += 1)
    {
        var failure = undefined;
        try
        {
            functions[i](id);
        }
        catch (e)
        {
            failure = errorText(e);
        }
        if (failure != undefined)
        {
            if (replay.mode == "frame" && indexOf(failure, "SELF_INTERSECTING_CURVE_SELECTED") >= 0)
            {
                setVariable(context, CASE_RESULT_KEY, { "origins" : origins, "stoppedAt" : i });
                return;
            }
            throw regenError("repeated feature " ~ (i + 1) ~ " failed (" ~ failure ~ ")");
        }
        const after = evaluateQuery(context, qCreatedBy(replay.caseId, EntityType.BODY));
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
    var result = { "origins" : origins };
    if (last == size(functions) - 1)
    {
        var outputs = [];
        for (var output in definition.outputs)
        {
            outputs = append(outputs, output.outputQuery);
        }
        result.outputs = outputs;
    }
    setVariable(context, CASE_RESULT_KEY, result);
}

/**
 * Close case editing logic: caches the names of the bodies the listed features created for case 1
 * (the feature body cannot read names during regen).
 */
export function closeCaseEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map) returns map
{
    const featureIds = sortedFeatureIds(context, definition.features);
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
// Case pattern
// ---------------------------------------------------------------------------------------------

annotation { "Feature Type Name" : "Case pattern", "Icon" : PatternIconNamespace::BLOB_DATA,
        "Editing Logic Function" : "casePatternEditLogic",
        "Feature Type Description" : "Repeats the features of a Close case for new inputs: each case binds the Define case's inputs and values to its own selections and values and re-runs the features. Parts are named after case 1's with the case name as suffix; outputs are published as #<case>_<name>." }
export const casePattern = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Close case", "Description" : "The Close case ending the features to repeat." }
        definition.closeCase is FeatureList;

        annotation { "Name" : "Cases", "Item name" : "Case", "Item label template" : "#caseName",
                    "Description" : "Usually one. Each case gives a selection per input and a value per value (case 1's by default)." }
        definition.cases is array;
        for (var row in definition.cases)
        {
            annotation { "Name" : "Case name", "Default" : "", "MaxLength" : 64,
                        "Description" : "Letters, digits and _, starting with a letter; unique among the Define case's cases. Outputs are published as #<case>_<name>." }
            row.caseName is string;

            annotation { "Name" : "Use input 1", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.use1 is boolean;
            annotation { "Name" : "Input 1 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.in1Key is string;
            annotation { "Name" : "Use input 2", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.use2 is boolean;
            annotation { "Name" : "Input 2 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.in2Key is string;
            annotation { "Name" : "Use input 3", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.use3 is boolean;
            annotation { "Name" : "Input 3 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.in3Key is string;
            annotation { "Name" : "Use input 4", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.use4 is boolean;
            annotation { "Name" : "Input 4 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.in4Key is string;
            annotation { "Name" : "Use input 5", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.use5 is boolean;
            annotation { "Name" : "Input 5 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.in5Key is string;
            annotation { "Name" : "Use input 6", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.use6 is boolean;
            annotation { "Name" : "Input 6 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.in6Key is string;
            annotation { "Name" : "Use input 7", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.use7 is boolean;
            annotation { "Name" : "Input 7 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.in7Key is string;
            annotation { "Name" : "Use input 8", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.use8 is boolean;
            annotation { "Name" : "Input 8 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.in8Key is string;
            annotation { "Name" : "Use value 1", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.useValue1 is boolean;
            annotation { "Name" : "Value 1 type", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.v1Kind is CaseSlotKind;
            annotation { "Name" : "Value 1 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.v1Key is string;
            annotation { "Name" : "Use value 2", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.useValue2 is boolean;
            annotation { "Name" : "Value 2 type", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.v2Kind is CaseSlotKind;
            annotation { "Name" : "Value 2 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.v2Key is string;
            annotation { "Name" : "Use value 3", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.useValue3 is boolean;
            annotation { "Name" : "Value 3 type", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.v3Kind is CaseSlotKind;
            annotation { "Name" : "Value 3 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.v3Key is string;
            annotation { "Name" : "Use value 4", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.useValue4 is boolean;
            annotation { "Name" : "Value 4 type", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.v4Kind is CaseSlotKind;
            annotation { "Name" : "Value 4 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.v4Key is string;
            annotation { "Name" : "Use value 5", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.useValue5 is boolean;
            annotation { "Name" : "Value 5 type", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.v5Kind is CaseSlotKind;
            annotation { "Name" : "Value 5 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.v5Key is string;
            annotation { "Name" : "Use value 6", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.useValue6 is boolean;
            annotation { "Name" : "Value 6 type", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.v6Kind is CaseSlotKind;
            annotation { "Name" : "Value 6 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.v6Key is string;

            if (row.use1)
            {
                annotation { "Name" : "Input 1", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                row.in1Name is string;
                annotation { "Name" : "Selection", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
                row.input1 is Query;
            }

            if (row.use2)
            {
                annotation { "Name" : "Input 2", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                row.in2Name is string;
                annotation { "Name" : "Selection", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
                row.input2 is Query;
            }

            if (row.use3)
            {
                annotation { "Name" : "Input 3", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                row.in3Name is string;
                annotation { "Name" : "Selection", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
                row.input3 is Query;
            }

            if (row.use4)
            {
                annotation { "Name" : "Input 4", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                row.in4Name is string;
                annotation { "Name" : "Selection", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
                row.input4 is Query;
            }

            if (row.use5)
            {
                annotation { "Name" : "Input 5", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                row.in5Name is string;
                annotation { "Name" : "Selection", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
                row.input5 is Query;
            }

            if (row.use6)
            {
                annotation { "Name" : "Input 6", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                row.in6Name is string;
                annotation { "Name" : "Selection", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
                row.input6 is Query;
            }

            if (row.use7)
            {
                annotation { "Name" : "Input 7", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                row.in7Name is string;
                annotation { "Name" : "Selection", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
                row.input7 is Query;
            }

            if (row.use8)
            {
                annotation { "Name" : "Input 8", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                row.in8Name is string;
                annotation { "Name" : "Selection", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
                row.input8 is Query;
            }

            if (row.useValue1)
            {
                annotation { "Name" : "Value 1", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                row.v1Name is string;
                if (row.v1Kind == CaseSlotKind.LENGTH)
                {
                    annotation { "Name" : "Value" }
                    isLength(row.v1Length, CASE_LENGTH_BOUNDS);
                }
                if (row.v1Kind == CaseSlotKind.ANGLE)
                {
                    annotation { "Name" : "Value" }
                    isAngle(row.v1Angle, CASE_ANGLE_BOUNDS);
                }
                if (row.v1Kind == CaseSlotKind.AREA)
                {
                    annotation { "Name" : "Value (mm^2)" }
                    isReal(row.v1Area, CASE_REAL_BOUNDS);
                }
                if (row.v1Kind == CaseSlotKind.VOLUME)
                {
                    annotation { "Name" : "Value (mm^3)" }
                    isReal(row.v1Volume, CASE_REAL_BOUNDS);
                }
                if (row.v1Kind == CaseSlotKind.NUMBER)
                {
                    annotation { "Name" : "Value" }
                    isReal(row.v1Number, CASE_REAL_BOUNDS);
                }
                if (row.v1Kind == CaseSlotKind.INTEGER)
                {
                    annotation { "Name" : "Value" }
                    isInteger(row.v1Integer, CASE_INTEGER_BOUNDS);
                }
                if (row.v1Kind == CaseSlotKind.TEXT)
                {
                    annotation { "Name" : "Value", "Default" : "" }
                    row.v1Text is string;
                }
                if (row.v1Kind == CaseSlotKind.BOOLEAN)
                {
                    annotation { "Name" : "Value", "Default" : "true", "Description" : "An expression that is true or false: true, false, #other, !#other." }
                    isAnything(row.v1Boolean);
                }
            }

            if (row.useValue2)
            {
                annotation { "Name" : "Value 2", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                row.v2Name is string;
                if (row.v2Kind == CaseSlotKind.LENGTH)
                {
                    annotation { "Name" : "Value" }
                    isLength(row.v2Length, CASE_LENGTH_BOUNDS);
                }
                if (row.v2Kind == CaseSlotKind.ANGLE)
                {
                    annotation { "Name" : "Value" }
                    isAngle(row.v2Angle, CASE_ANGLE_BOUNDS);
                }
                if (row.v2Kind == CaseSlotKind.AREA)
                {
                    annotation { "Name" : "Value (mm^2)" }
                    isReal(row.v2Area, CASE_REAL_BOUNDS);
                }
                if (row.v2Kind == CaseSlotKind.VOLUME)
                {
                    annotation { "Name" : "Value (mm^3)" }
                    isReal(row.v2Volume, CASE_REAL_BOUNDS);
                }
                if (row.v2Kind == CaseSlotKind.NUMBER)
                {
                    annotation { "Name" : "Value" }
                    isReal(row.v2Number, CASE_REAL_BOUNDS);
                }
                if (row.v2Kind == CaseSlotKind.INTEGER)
                {
                    annotation { "Name" : "Value" }
                    isInteger(row.v2Integer, CASE_INTEGER_BOUNDS);
                }
                if (row.v2Kind == CaseSlotKind.TEXT)
                {
                    annotation { "Name" : "Value", "Default" : "" }
                    row.v2Text is string;
                }
                if (row.v2Kind == CaseSlotKind.BOOLEAN)
                {
                    annotation { "Name" : "Value", "Default" : "true", "Description" : "An expression that is true or false: true, false, #other, !#other." }
                    isAnything(row.v2Boolean);
                }
            }

            if (row.useValue3)
            {
                annotation { "Name" : "Value 3", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                row.v3Name is string;
                if (row.v3Kind == CaseSlotKind.LENGTH)
                {
                    annotation { "Name" : "Value" }
                    isLength(row.v3Length, CASE_LENGTH_BOUNDS);
                }
                if (row.v3Kind == CaseSlotKind.ANGLE)
                {
                    annotation { "Name" : "Value" }
                    isAngle(row.v3Angle, CASE_ANGLE_BOUNDS);
                }
                if (row.v3Kind == CaseSlotKind.AREA)
                {
                    annotation { "Name" : "Value (mm^2)" }
                    isReal(row.v3Area, CASE_REAL_BOUNDS);
                }
                if (row.v3Kind == CaseSlotKind.VOLUME)
                {
                    annotation { "Name" : "Value (mm^3)" }
                    isReal(row.v3Volume, CASE_REAL_BOUNDS);
                }
                if (row.v3Kind == CaseSlotKind.NUMBER)
                {
                    annotation { "Name" : "Value" }
                    isReal(row.v3Number, CASE_REAL_BOUNDS);
                }
                if (row.v3Kind == CaseSlotKind.INTEGER)
                {
                    annotation { "Name" : "Value" }
                    isInteger(row.v3Integer, CASE_INTEGER_BOUNDS);
                }
                if (row.v3Kind == CaseSlotKind.TEXT)
                {
                    annotation { "Name" : "Value", "Default" : "" }
                    row.v3Text is string;
                }
                if (row.v3Kind == CaseSlotKind.BOOLEAN)
                {
                    annotation { "Name" : "Value", "Default" : "true", "Description" : "An expression that is true or false: true, false, #other, !#other." }
                    isAnything(row.v3Boolean);
                }
            }

            if (row.useValue4)
            {
                annotation { "Name" : "Value 4", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                row.v4Name is string;
                if (row.v4Kind == CaseSlotKind.LENGTH)
                {
                    annotation { "Name" : "Value" }
                    isLength(row.v4Length, CASE_LENGTH_BOUNDS);
                }
                if (row.v4Kind == CaseSlotKind.ANGLE)
                {
                    annotation { "Name" : "Value" }
                    isAngle(row.v4Angle, CASE_ANGLE_BOUNDS);
                }
                if (row.v4Kind == CaseSlotKind.AREA)
                {
                    annotation { "Name" : "Value (mm^2)" }
                    isReal(row.v4Area, CASE_REAL_BOUNDS);
                }
                if (row.v4Kind == CaseSlotKind.VOLUME)
                {
                    annotation { "Name" : "Value (mm^3)" }
                    isReal(row.v4Volume, CASE_REAL_BOUNDS);
                }
                if (row.v4Kind == CaseSlotKind.NUMBER)
                {
                    annotation { "Name" : "Value" }
                    isReal(row.v4Number, CASE_REAL_BOUNDS);
                }
                if (row.v4Kind == CaseSlotKind.INTEGER)
                {
                    annotation { "Name" : "Value" }
                    isInteger(row.v4Integer, CASE_INTEGER_BOUNDS);
                }
                if (row.v4Kind == CaseSlotKind.TEXT)
                {
                    annotation { "Name" : "Value", "Default" : "" }
                    row.v4Text is string;
                }
                if (row.v4Kind == CaseSlotKind.BOOLEAN)
                {
                    annotation { "Name" : "Value", "Default" : "true", "Description" : "An expression that is true or false: true, false, #other, !#other." }
                    isAnything(row.v4Boolean);
                }
            }

            if (row.useValue5)
            {
                annotation { "Name" : "Value 5", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                row.v5Name is string;
                if (row.v5Kind == CaseSlotKind.LENGTH)
                {
                    annotation { "Name" : "Value" }
                    isLength(row.v5Length, CASE_LENGTH_BOUNDS);
                }
                if (row.v5Kind == CaseSlotKind.ANGLE)
                {
                    annotation { "Name" : "Value" }
                    isAngle(row.v5Angle, CASE_ANGLE_BOUNDS);
                }
                if (row.v5Kind == CaseSlotKind.AREA)
                {
                    annotation { "Name" : "Value (mm^2)" }
                    isReal(row.v5Area, CASE_REAL_BOUNDS);
                }
                if (row.v5Kind == CaseSlotKind.VOLUME)
                {
                    annotation { "Name" : "Value (mm^3)" }
                    isReal(row.v5Volume, CASE_REAL_BOUNDS);
                }
                if (row.v5Kind == CaseSlotKind.NUMBER)
                {
                    annotation { "Name" : "Value" }
                    isReal(row.v5Number, CASE_REAL_BOUNDS);
                }
                if (row.v5Kind == CaseSlotKind.INTEGER)
                {
                    annotation { "Name" : "Value" }
                    isInteger(row.v5Integer, CASE_INTEGER_BOUNDS);
                }
                if (row.v5Kind == CaseSlotKind.TEXT)
                {
                    annotation { "Name" : "Value", "Default" : "" }
                    row.v5Text is string;
                }
                if (row.v5Kind == CaseSlotKind.BOOLEAN)
                {
                    annotation { "Name" : "Value", "Default" : "true", "Description" : "An expression that is true or false: true, false, #other, !#other." }
                    isAnything(row.v5Boolean);
                }
            }

            if (row.useValue6)
            {
                annotation { "Name" : "Value 6", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                row.v6Name is string;
                if (row.v6Kind == CaseSlotKind.LENGTH)
                {
                    annotation { "Name" : "Value" }
                    isLength(row.v6Length, CASE_LENGTH_BOUNDS);
                }
                if (row.v6Kind == CaseSlotKind.ANGLE)
                {
                    annotation { "Name" : "Value" }
                    isAngle(row.v6Angle, CASE_ANGLE_BOUNDS);
                }
                if (row.v6Kind == CaseSlotKind.AREA)
                {
                    annotation { "Name" : "Value (mm^2)" }
                    isReal(row.v6Area, CASE_REAL_BOUNDS);
                }
                if (row.v6Kind == CaseSlotKind.VOLUME)
                {
                    annotation { "Name" : "Value (mm^3)" }
                    isReal(row.v6Volume, CASE_REAL_BOUNDS);
                }
                if (row.v6Kind == CaseSlotKind.NUMBER)
                {
                    annotation { "Name" : "Value" }
                    isReal(row.v6Number, CASE_REAL_BOUNDS);
                }
                if (row.v6Kind == CaseSlotKind.INTEGER)
                {
                    annotation { "Name" : "Value" }
                    isInteger(row.v6Integer, CASE_INTEGER_BOUNDS);
                }
                if (row.v6Kind == CaseSlotKind.TEXT)
                {
                    annotation { "Name" : "Value", "Default" : "" }
                    row.v6Text is string;
                }
                if (row.v6Kind == CaseSlotKind.BOOLEAN)
                {
                    annotation { "Name" : "Value", "Default" : "true", "Description" : "An expression that is true or false: true, false, #other, !#other." }
                    isAnything(row.v6Boolean);
                }
            }
        }

        annotation { "Name" : "Print bindings", "Default" : false,
                    "Description" : "Print every case's selections and values to the FeatureScript notices." }
        definition.debug is boolean;
    }
    {
        if (isInFeaturePattern(context))
        {
            throw regenError("A Case pattern cannot run inside another pattern.");
        }
        const close = findSignature(context, definition.closeCase, "caseClose");
        if (close == undefined)
        {
            throw regenError("Select a Close case.", ["closeCase"]);
        }
        if (close.count > 1)
        {
            throw regenError("Select one Close case.", ["closeCase"]);
        }
        const signature = close.signature;
        const closeFunctions = valuesSortedById(context, definition.closeCase);
        const caseCount = size(definition.cases);
        if (caseCount == 0)
        {
            throw regenError("Add a case.", ["cases"]);
        }

        const namesKey = caseNamesKey(signature.defineKey);
        var usedNames = getVariable(context, namesKey, MISSING);
        if (!(usedNames is array))
        {
            usedNames = [signature.caseName];
        }
        const templateNames = parseTemplateNames(signature.templateNames);
        var failures = [];
        var notes = [];
        var unnamed = 0;

        for (var row in definition.cases)
        {
            const caseName = row.caseName;
            if (caseName == "" || match(caseName, "[A-Za-z][A-Za-z0-9_]*").hasMatch != true)
            {
                failures = append(failures, "\"" ~ caseName ~ "\": name each case with letters, digits and _, starting with a letter");
                continue;
            }
            if (isIn(caseName, usedNames))
            {
                failures = append(failures, caseName ~ ": the case name is already used by this Define case");
                continue;
            }
            const caseIndex = size(usedNames) + 1;
            usedNames = append(usedNames, caseName);

            // Bind every input and value by name (slots keep their names; see casePatternEditLogic).
            var bindFailure = undefined;
            var selections = [];
            for (var name in signature.names)
            {
                const slot = findSlot(row, "in", CASE_MAX_INPUTS, name);
                const selection = slot == undefined ? undefined : row["input" ~ slot];
                if (selection == undefined || isQueryEmpty(context, selection))
                {
                    bindFailure = "#" ~ name ~ " selects nothing" ~ (slot == undefined ? " (edit this Case pattern after changing the Define case)" : "");
                    break;
                }
                selections = append(selections, selection);
            }
            if (bindFailure != undefined)
            {
                failures = append(failures, caseName ~ ": " ~ bindFailure);
                continue;
            }
            var values = [];
            for (var m = 0; m < size(signature.valueNames); m += 1)
            {
                const name = signature.valueNames[m];
                const kind = signature.valueKinds[m];
                const slot = findSlot(row, "v", CASE_MAX_VALUES, name);
                var value = undefined;
                if (slot != undefined && row["v" ~ slot ~ "Kind"] == slotKind(kind))
                {
                    value = typedValue(row, "v" ~ slot, kind);
                }
                if (value == undefined)
                {
                    value = signature.values[m];
                    notes = append(notes, caseName ~ " uses case 1's #" ~ name ~ " (edit this Case pattern after changing the Define case)");
                }
                else if (kind == CaseValueKind.BOOLEAN && !(value is boolean))
                {
                    bindFailure = "#" ~ name ~ " must be true or false";
                    break;
                }
                values = append(values, value);
            }
            if (bindFailure != undefined)
            {
                failures = append(failures, caseName ~ ": " ~ bindFailure);
                continue;
            }

            for (var n = 0; n < size(signature.names); n += 1)
            {
                setQueryVariable(context, signature.names[n], selections[n]);
            }
            for (var m = 0; m < size(values); m += 1)
            {
                setVariable(context, signature.valueNames[m], values[m]);
            }
            setVariable(context, "caseName", caseName);
            setVariable(context, "caseIndex", caseIndex);
            if (definition.debug)
            {
                println("Case " ~ caseName ~ " (#caseIndex " ~ caseIndex ~ "):");
                for (var n = 0; n < size(signature.names); n += 1)
                {
                    println("  #" ~ signature.names[n] ~ " = " ~ size(evaluateQuery(context, selections[n])) ~ " entities");
                }
                for (var m = 0; m < size(values); m += 1)
                {
                    println("  #" ~ signature.valueNames[m] ~ " = " ~ toString(values[m]));
                }
            }

            // Run the case as a Pattern runs one instance (correction 31: never inside startFeature):
            // the Close case replays its features under this frame (correction 50), stepping out of
            // it for features that edit geometry from outside the list (see replayCase).
            const caseId = id + ("case_" ~ caseName);
            const run = runCase(context, closeFunctions, caseId, caseName);
            const failure = run.failure;
            const result = run.result;
            const caseBodies = qCreatedBy(caseId, EntityType.BODY);
            if (failure != undefined)
            {
                if (!isQueryEmpty(context, caseBodies))
                {
                    opDeleteBodies(context, id + ("discard_" ~ caseName), { "entities" : caseBodies });
                }
                failures = append(failures, caseName ~ ": " ~ failure);
                continue;
            }
            if (definition.debug)
            {
                for (var i in result.outside)
                {
                    println("  repeated feature " ~ (i + 1) ~ " edits geometry from outside the list: ran outside the pattern frame");
                }
            }

            const dropped = unkeptBodies(caseBodies, signature.keep);
            if (!isQueryEmpty(context, dropped))
            {
                opDeleteBodies(context, id + ("drop_" ~ caseName), { "entities" : dropped });
            }
            for (var origin in result.origins)
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
                            "value" : caseBodyName(templateName, signature.caseName, caseName, signature.separator)
                        });
            }

            for (var k = 0; k < size(signature.outputs); k += 1)
            {
                const output = signature.outputs[k];
                const q = result.outputs[k];
                publishCaseOutput(context, caseName, output.name, q, output.onUse, output.track, "cases");
                const found = evaluateQuery(context, q);
                if (size(found) > 0 && isQueryEmpty(context, qIntersection([qUnion(found), qCreatedBy(caseId)])))
                {
                    notes = append(notes, "#" ~ caseName ~ "_" ~ output.name ~ " is not case " ~ caseName
                                ~ "'s geometry: make the Close case output a query variable, not a click");
                }
            }
        }

        // Later features see case 1's inputs again (variables the repeated features set keep the last case's).
        for (var n = 0; n < size(signature.names); n += 1)
        {
            setQueryVariable(context, signature.names[n], signature.queries[n]);
        }
        for (var m = 0; m < size(signature.valueNames); m += 1)
        {
            setVariable(context, signature.valueNames[m], signature.values[m]);
        }
        setVariable(context, "caseName", signature.caseName);
        setVariable(context, "caseIndex", 1);
        setVariable(context, namesKey, usedNames);
        setVariable(context, CASE_REPLAY_KEY, MISSING);

        setVariable(context, "-caseDebug-" ~ toString(id), { "failures" : failures, "notes" : notes }); // TEMP diagnosis, remove
        if (size(failures) == caseCount && !definition.debug) // TEMP: debug keeps the feature to read failures
        {
            throw regenError("No case was built. " ~ join(failures, "; "), ["cases"]);
        }
        if (size(failures) > 0)
        {
            reportFeatureWarning(context, id, "Not built: " ~ join(failures, "; "));
            return;
        }
        if (signature.containsSketch)
        {
            notes = append(notes, "Sketches are re-solved per case. Dimensions and constraints to the origin or the default planes are not"
                    ~ " reapplied, so those entities keep case 1's position; constrain sketches to geometry derived from the inputs.");
        }
        if (unnamed > 0)
        {
            notes = append(notes, unnamed ~ " bod" ~ (unnamed == 1 ? "y" : "ies") ~ " kept Onshape's default name: edit the Close case"
                    ~ " to refresh case 1's names.");
        }
        if (size(notes) > 0)
        {
            reportFeatureInfo(context, id, join(notes, " "));
        }
    }, {
        "cases" : [],
        "debug" : false
    });

/**
 * Case pattern editing logic: lays out every case row from the Close case's signature -- one
 * labelled selection per input, one labelled field of the right type per value -- keeping each
 * slot's selection or value by NAME, so adding, removing or reordering inputs in the Define case
 * does not shuffle them. A new slot, or one whose type changed, starts at case 1's value. Adds the
 * first case row when the Close case is picked on a new feature.
 */
export function casePatternEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map) returns map
{
    const close = findSignature(context, definition.closeCase, "caseClose");
    if (close == undefined)
    {
        return definition;
    }
    const signature = close.signature;
    if (size(definition.cases) == 0 && isCreating)
    {
        definition.cases = [emptyCaseRow()];
    }
    for (var r = 0; r < size(definition.cases); r += 1)
    {
        definition.cases[r] = layoutRow(definition.cases[r], signature);
    }
    return definition;
}

/** A case row laid out for `signature`, keeping selections and values by name. */
function layoutRow(row is map, signature is map) returns map
{
    var oldInputs = {};
    for (var k = 1; k <= CASE_MAX_INPUTS; k += 1)
    {
        if (row["in" ~ k ~ "Key"] is string && row["in" ~ k ~ "Key"] != "")
        {
            oldInputs[row["in" ~ k ~ "Key"]] = row["input" ~ k];
        }
    }
    var oldValues = {};
    for (var m = 1; m <= CASE_MAX_VALUES; m += 1)
    {
        if (row["v" ~ m ~ "Key"] is string && row["v" ~ m ~ "Key"] != "")
        {
            oldValues[row["v" ~ m ~ "Key"]] = { "kind" : row["v" ~ m ~ "Kind"], "row" : row, "slot" : m };
        }
    }
    var result = row;
    const count = size(signature.names);
    for (var k = 1; k <= CASE_MAX_INPUTS; k += 1)
    {
        const used = k <= count;
        const name = used ? signature.names[k - 1] : "";
        result["use" ~ k] = used;
        result["in" ~ k ~ "Key"] = name;
        result["in" ~ k ~ "Name"] = used ? "#" ~ name : "";
        const kept = used ? oldInputs[name] : undefined;
        result["input" ~ k] = kept is Query ? kept : qNothing();
    }
    const valueCount = size(signature.valueNames);
    for (var m = 1; m <= CASE_MAX_VALUES; m += 1)
    {
        const used = m <= valueCount;
        const name = used ? signature.valueNames[m - 1] : "";
        const kind = used ? signature.valueKinds[m - 1] : undefined;
        result["useValue" ~ m] = used;
        result["v" ~ m ~ "Key"] = name;
        result["v" ~ m ~ "Kind"] = used ? slotKind(kind) : CaseSlotKind.NONE;
        result["v" ~ m ~ "Name"] = used ? "#" ~ name ~ " (" ~ kindText(kind) ~ ")" : "";
        if (!used)
        {
            continue;
        }
        const old = oldValues[name];
        const kept = (old != undefined && old.kind == slotKind(kind)) ? typedValue(old.row, "v" ~ old.slot, kind) : undefined;
        result = setTypedField(result, "v" ~ m, kind, kept != undefined ? kept : signature.values[m - 1]);
    }
    return result;
}

// ---------------------------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------------------------

/**
 * Runs one case through its Close case (closeFunctions[0]): in the pattern frame, stepping out of it
 * for each feature the frame refuses (replayCase). Returns { failure } or { result : { origins,
 * outputs, outside } } (outside = indices of the features that ran outside the frame).
 */
function runCase(context is Context, closeFunctions is array, caseId is Id, caseName is string) returns map
{
    var origins = [];
    var outside = [];
    var from = 0;
    var segment = 0;
    while (true)
    {
        // The frame id must be the id the features run under (a frame on caseId with calls under
        // caseId + "s0" builds nothing -- measured 2026-09-26).
        const segmentId = caseId + ("s" ~ segment);
        segment += 1;
        setFeaturePatternInstanceData(context, segmentId, { "transform" : identityTransform() });
        var part = callClose(context, closeFunctions, segmentId,
            { "caseId" : caseId, "caseName" : caseName, "mode" : "frame", "from" : from });
        unsetFeaturePatternInstanceData(context, segmentId);
        if (part.failure != undefined)
        {
            return part;
        }
        origins = concatenateArrays([origins, part.result.origins]);
        if (part.result.stoppedAt == undefined)
        {
            return { "result" : { "origins" : origins, "outputs" : part.result.outputs, "outside" : outside } };
        }
        const index = part.result.stoppedAt;
        part = callClose(context, closeFunctions, caseId + ("d" ~ index),
            { "caseId" : caseId, "caseName" : caseName, "mode" : "direct", "index" : index });
        if (part.failure != undefined)
        {
            return part;
        }
        origins = concatenateArrays([origins, part.result.origins]);
        outside = append(outside, index);
        from = index + 1;
        if (part.result.outputs != undefined)
        {
            return { "result" : { "origins" : origins, "outputs" : part.result.outputs, "outside" : outside } };
        }
    }
}

/** Calls the Close case once with `replay` published; returns { failure } or { result }. */
function callClose(context is Context, closeFunctions is array, callId is Id, replay is map) returns map
{
    setVariable(context, CASE_REPLAY_KEY, replay);
    setVariable(context, CASE_RESULT_KEY, MISSING);
    var failure = undefined;
    try
    {
        closeFunctions[0](callId);
    }
    catch (e)
    {
        failure = errorText(e);
    }
    const result = getVariable(context, CASE_RESULT_KEY, MISSING);
    if (failure == undefined && !(result is map))
    {
        failure = "the Close case did not run";
    }
    return failure != undefined ? { "failure" : failure } : { "result" : result };
}

/** A new, empty case row: every row parameter at its default. */
function emptyCaseRow() returns map
{
    var row = { "caseName" : "" };
    for (var k = 1; k <= CASE_MAX_INPUTS; k += 1)
    {
        row["use" ~ k] = false;
        row["in" ~ k ~ "Key"] = "";
        row["in" ~ k ~ "Name"] = "";
        row["input" ~ k] = qNothing();
    }
    for (var m = 1; m <= CASE_MAX_VALUES; m += 1)
    {
        const prefix = "v" ~ m;
        row["useValue" ~ m] = false;
        row[prefix ~ "Key"] = "";
        row[prefix ~ "Kind"] = CaseSlotKind.NONE;
        row[prefix ~ "Name"] = "";
        row[prefix ~ "Length"] = 0 * millimeter;
        row[prefix ~ "Angle"] = 0 * degree;
        row[prefix ~ "Area"] = 0;
        row[prefix ~ "Volume"] = 0;
        row[prefix ~ "Number"] = 0;
        row[prefix ~ "Integer"] = 0;
        row[prefix ~ "Text"] = "";
        row[prefix ~ "Boolean"] = true;
    }
    return row;
}

/** The slot (1-based) of a row whose `<prefix><k>Key` is `name`, or undefined. */
function findSlot(row is map, prefix is string, slotCount is number, name is string)
{
    for (var k = 1; k <= slotCount; k += 1)
    {
        if (row[prefix ~ k ~ "Key"] == name)
        {
            return k;
        }
    }
    return undefined;
}

/** "<defineKey>" -> the variable holding every case name used so far for that Define case. */
function caseNamesKey(defineKey is string) returns string
{
    return "-caseNames-" ~ defineKey;
}

/** Case names become part of variable names (#<case>_<output>) and of part names. */
function verifyCaseName(caseName is string, faultyParameter is string)
{
    if (match(caseName, "[A-Za-z][A-Za-z0-9_]*").hasMatch != true)
    {
        throw regenError("Name case 1 with letters, digits and _, starting with a letter.", [faultyParameter]);
    }
}

/** A declared input, value or output name: a valid variable name, unused so far, not reserved. */
function verifyDeclaredName(name is string, used is array, faultyParameter is string)
{
    verifyVariableNameIsValid(name, faultyParameter);
    if (isIn(name, used))
    {
        throw regenError("#" ~ name ~ " is declared twice.", [faultyParameter]);
    }
    if (isIn(name, CASE_RESERVED_NAMES))
    {
        throw regenError("#" ~ name ~ " is set by every case; choose another name.", [faultyParameter]);
    }
}

/** A Boolean value must evaluate to true or false. */
function verifyValueKind(value, kind is CaseValueKind, name is string, faultyParameter is string)
{
    if (kind == CaseValueKind.BOOLEAN && !(value is boolean))
    {
        throw regenError("#" ~ name ~ " must be true or false (an expression such as true, false or !#other).", [faultyParameter]);
    }
}

/**
 * Publishes a case's output `q` as `#<caseName>_<outputName>`.
 *
 * Held (default): the std robust freeze -- the entities present now, each followed through
 * identity-preserving edits (what the std Query variable feature stores).
 * Tracked: that freeze plus startTracking, restricted to the entity types held now (a feature built
 * FROM the entities is derived from them too and would add its bodies and faces).
 * Evaluate on use: `q` stored as is and re-resolved wherever used. `q` is the query the case's
 * variables held when it closed, so later cases rebinding those variables do not change it.
 * Same scheme as Extract variables' publishQueryVariable (variable_tools/extract_variables_utils.fs).
 */
function publishCaseOutput(context is Context, caseName is string, outputName is string, q is Query,
    evaluateOnUse is boolean, track is boolean, faultyParameter is string)
{
    const name = caseName ~ "_" ~ outputName;
    verifyVariableNameIsValid(name, faultyParameter);
    var stored = q;
    if (!evaluateOnUse)
    {
        var held = makeRobustQueriesBatched(context, q);
        if (track)
        {
            const tracking = startTracking(context, q);
            for (var t in [EntityType.BODY, EntityType.FACE, EntityType.EDGE, EntityType.VERTEX])
            {
                if (!isQueryEmpty(context, qEntityFilter(q, t)))
                {
                    held = append(held, qEntityFilter(tracking, t));
                }
            }
        }
        stored = qUnion(held);
    }
    setQueryVariable(context, name, "Case " ~ caseName ~ " output", stored);
}

/** The value a typed field holds: `source[prefix ~ "Length"]` etc., with units for area/volume. */
function typedValue(source is map, prefix is string, kind is CaseValueKind)
{
    if (kind == CaseValueKind.LENGTH)
    {
        return source[prefix ~ "Length"];
    }
    if (kind == CaseValueKind.ANGLE)
    {
        return source[prefix ~ "Angle"];
    }
    if (kind == CaseValueKind.AREA)
    {
        const x = source[prefix ~ "Area"];
        return x == undefined ? undefined : x * squareMillimeter;
    }
    if (kind == CaseValueKind.VOLUME)
    {
        const x = source[prefix ~ "Volume"];
        return x == undefined ? undefined : x * cubicMillimeter;
    }
    if (kind == CaseValueKind.NUMBER)
    {
        return source[prefix ~ "Number"];
    }
    if (kind == CaseValueKind.INTEGER)
    {
        return source[prefix ~ "Integer"];
    }
    if (kind == CaseValueKind.BOOLEAN)
    {
        return source[prefix ~ "Boolean"];
    }
    return source[prefix ~ "Text"];
}

/** `row` with the typed field of `kind` set to `value` (the inverse of typedValue). */
function setTypedField(row is map, prefix is string, kind is CaseValueKind, value) returns map
{
    var result = row;
    if (kind == CaseValueKind.LENGTH)
    {
        result[prefix ~ "Length"] = value;
    }
    else if (kind == CaseValueKind.ANGLE)
    {
        result[prefix ~ "Angle"] = value;
    }
    else if (kind == CaseValueKind.AREA)
    {
        result[prefix ~ "Area"] = value == undefined ? undefined : value / squareMillimeter;
    }
    else if (kind == CaseValueKind.VOLUME)
    {
        result[prefix ~ "Volume"] = value == undefined ? undefined : value / cubicMillimeter;
    }
    else if (kind == CaseValueKind.NUMBER)
    {
        result[prefix ~ "Number"] = value;
    }
    else if (kind == CaseValueKind.INTEGER)
    {
        result[prefix ~ "Integer"] = value;
    }
    else if (kind == CaseValueKind.BOOLEAN)
    {
        result[prefix ~ "Boolean"] = value;
    }
    else
    {
        result[prefix ~ "Text"] = value;
    }
    return result;
}

/** A case row slot's type for a Define case value's type. */
function slotKind(kind is CaseValueKind) returns CaseSlotKind
{
    if (kind == CaseValueKind.LENGTH)
    {
        return CaseSlotKind.LENGTH;
    }
    if (kind == CaseValueKind.ANGLE)
    {
        return CaseSlotKind.ANGLE;
    }
    if (kind == CaseValueKind.AREA)
    {
        return CaseSlotKind.AREA;
    }
    if (kind == CaseValueKind.VOLUME)
    {
        return CaseSlotKind.VOLUME;
    }
    if (kind == CaseValueKind.NUMBER)
    {
        return CaseSlotKind.NUMBER;
    }
    if (kind == CaseValueKind.INTEGER)
    {
        return CaseSlotKind.INTEGER;
    }
    if (kind == CaseValueKind.BOOLEAN)
    {
        return CaseSlotKind.BOOLEAN;
    }
    return CaseSlotKind.TEXT;
}

/** "length", "area, mm^2", ... for a slot label. */
function kindText(kind is CaseValueKind) returns string
{
    if (kind == CaseValueKind.LENGTH)
    {
        return "length";
    }
    if (kind == CaseValueKind.ANGLE)
    {
        return "angle";
    }
    if (kind == CaseValueKind.AREA)
    {
        return "area, mm^2";
    }
    if (kind == CaseValueKind.VOLUME)
    {
        return "volume, mm^3";
    }
    if (kind == CaseValueKind.NUMBER)
    {
        return "number";
    }
    if (kind == CaseValueKind.INTEGER)
    {
        return "integer";
    }
    if (kind == CaseValueKind.BOOLEAN)
    {
        return "true or false";
    }
    return "text";
}

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

/**
 * The signature published by the first listed feature carrying `marker` ("caseDefine" or
 * "caseClose"), with its feature id and how many listed features carry the marker.
 */
function findSignature(context is Context, features is map, marker is string)
{
    var found = undefined;
    var count = 0;
    for (var featureId in sortedFeatureIds(context, features))
    {
        const value = getVariable(context, toString(featureId), MISSING);
        if (value is map && value[marker] == true)
        {
            count += 1;
            if (found == undefined)
            {
                found = { "signature" : value, "featureId" : featureId };
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

/** Parses the cached case 1 names into a map from "<feature index>.<body index>" to name. */
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
function unkeptBodies(caseBodies is Query, keep is map) returns Query
{
    const solidModel = qSketchFilter(caseBodies, SketchObject.NO);
    const regular = qConstructionFilter(solidModel, ConstructionObject.NO);
    var dropped = [];
    if (!keep.keepSketches)
    {
        dropped = append(dropped, qSketchFilter(caseBodies, SketchObject.YES));
    }
    if (!keep.keepParts)
    {
        dropped = append(dropped, qBodyType(regular, BodyType.SOLID));
    }
    if (!keep.keepSurfaces)
    {
        dropped = append(dropped, qBodyType(regular, BodyType.SHEET));
    }
    if (!keep.keepCurves)
    {
        dropped = append(dropped, qBodyType(regular, [BodyType.WIRE, BodyType.POINT]));
    }
    if (!keep.keepMateConnectors)
    {
        dropped = append(dropped, qBodyType(solidModel, BodyType.MATE_CONNECTOR));
    }
    if (!keep.keepPlanes)
    {
        dropped = append(dropped, qBodyType(qConstructionFilter(solidModel, ConstructionObject.YES), BodyType.SHEET));
    }
    return qUnion(dropped);
}

/** A short text for a caught regen error. */
function errorText(e) returns string
{
    if (e is map && e.customMessage != undefined)
    {
        return toString(e.customMessage);
    }
    if (e is map && e.message != undefined)
    {
        return toString(e.message);
    }
    return toString(e);
}
