FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
import(path : "onshape/std/queryVariable.fs", version : "3083.0");
// IMPORT: define_case_icon.svg (feature icon)
DefineIconNamespace::import(path : "5952d6dd4ff6b28dc31edc36", version : "22380bf432ee24dce3f8b51e");
// IMPORT: close_case_icon.svg (feature icon)
CloseIconNamespace::import(path : "a541081abe5d434e4a46669f", version : "920993e17dc692908d2d23a8");
// IMPORT: case_icon.svg (feature icon)
CaseIconNamespace::import(path : "a907927a4685054579fd324b", version : "cbf86416cfcbdb84156e27a5");

/**
 * Case Pattern v3: a repeatable feature chain, written like a function and called per case.
 *
 * Define case     the signature: declares the inputs (query variables) and values (# variables) with
 *                 case 1's selections and values, and binds them. Also sets #caseName and #caseIndex.
 * (features)      the body, built on those names. Variables made here are locals: recomputed per case.
 * Case            one further case: a selection per input and a value per value, laid out from the
 *                 Define case. Runs nothing by itself.
 * Close case      ends the chain and runs it: lists the body and the Case features and replays the body
 *                 once per case, the way a Pattern with "Reapply features" runs an instance. Outputs are
 *                 published per case as #<case>_<name>. Close again further down (another Close case on the
 *                 same Define case and body) to run cases that select geometry made after the first one.
 *
 * Mechanism (correction 60): the feature that replays the body must OWN the body's FeatureList and push the
 * pattern frame itself. v2 had a Case pattern call the Close case (a feature function called under a new id),
 * which dropped every reference the body clicked on geometry from before it; a frame around that call as well
 * kept them but blocked the step-out-of-frame retry for edits of outside geometry. One level does both.
 * Design: case_pattern/DESIGN.md section 12.
 */

/** Most inputs (query variables) one Define case may declare. */
export const CASE_MAX_INPUTS = 8;

/** Most values (# variables) one Define case may declare. */
export const CASE_MAX_VALUES = 6;

/** Returned by getVariable when a name is not set (an undefined default still throws, correction 32). */
const MISSING = "__caseMissing__";



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

annotation { "Feature Type Name" : "Define case", "Icon" : DefineIconNamespace::BLOB_DATA,
        "Feature Type Description" : "Declares the inputs (query variables) and values (# variables) of a repeatable feature chain, and binds case 1. Build the features on these names, add a Case feature per further case, and run them all with Close case." }
export const defineCase = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Case 1 name", "Default" : "A", "MaxLength" : 64,
                    "Description" : "Letters, digits and _, starting with a letter. Outputs are published as #<case>_<name>." }
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

        annotation { "Name" : "Shared references", "Item name" : "Reference", "Item label template" : "#sharedName", "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS,
                    "Description" : "Geometry from before this Define case that the repeated features use in EVERY case (a mate connector, a side reference). Reference it through this #name, never by clicking it inside the repeated features. No per-case selection." }
        definition.shared is array;
        for (var ref in definition.shared)
        {
            annotation { "Name" : "Name", "Default" : "", "MaxLength" : 64,
                        "Description" : "Query variable name. It may already exist; it is redefined here." }
            ref.sharedName is string;

            annotation { "Name" : "Selection", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
            ref.sharedQuery is Query;
        }

        annotation { "Name" : "Values", "Item name" : "Value", "Item label template" : "#valueName", "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS,
                    "Description" : "# variables that change per case: defined here with case 1's value, and every case gives its own. Values that are the same for every case need not be here -- plain variables from anywhere earlier work inside the repeated features." }
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
        // Re-run inside a pattern (listed by mistake): the Close case has already bound this case.
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

        var sharedNames = [];
        var sharedQueries = [];
        for (var ref in definition.shared)
        {
            verifyDeclaredName(ref.sharedName, concatenateArrays([names, sharedNames]), "shared");
            if (isQueryEmpty(context, ref.sharedQuery))
            {
                throw regenError("Shared reference #" ~ ref.sharedName ~ " selects nothing.", ["shared"]);
            }
            setQueryVariable(context, ref.sharedName, ref.sharedQuery);
            sharedNames = append(sharedNames, ref.sharedName);
            sharedQueries = append(sharedQueries, ref.sharedQuery);
        }

        var valueNames = [];
        var valueKinds = [];
        var caseOneValues = [];
        for (var value in definition.values)
        {
            verifyDeclaredName(value.valueName, concatenateArrays([names, sharedNames, valueNames]), "values");
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
                    "sharedNames" : sharedNames,
                    "sharedQueries" : sharedQueries,
                    "valueNames" : valueNames,
                    "valueKinds" : valueKinds,
                    "values" : caseOneValues
                }, "Define case");
    }, {
        "caseName" : "A",
        "inputs" : [],
        "shared" : [],
        "values" : []
    });

// ---------------------------------------------------------------------------------------------
// Case
// ---------------------------------------------------------------------------------------------

annotation { "Feature Type Name" : "Case", "Icon" : CaseIconNamespace::BLOB_DATA,
        "Editing Logic Function" : "caseEditLogic",
        "Feature Type Description" : "One further case of a Define case: a selection per input and a value per value. It runs nothing by itself; list it in a Close case, which runs it." }
export const caseFeature = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Define case", "Description" : "The Define case whose inputs and values this case gives." }
        definition.defineCase is FeatureList;

        // A real button (correction 27): pressing it runs the editing logic, which lays the slots out
        // again from the Define case (editing logic does not run when a dialog is only opened).
        annotation { "Name" : "Update from Define case",
                    "Description" : "Lay the slots out again after inputs or values were added, removed, renamed or retyped in the Define case. Selections and values are kept by name." }
        isButton(definition.updateSlots);

        annotation { "Name" : "Case name", "Default" : "", "MaxLength" : 64,
                    "Description" : "Letters, digits and _, starting with a letter; unique among the Define case's cases. Outputs are published as #<case>_<name>." }
        definition.caseName is string;

        annotation { "Name" : "Use input 1", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.use1 is boolean;
        annotation { "Name" : "Input 1 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.in1Key is string;
        annotation { "Name" : "Use input 2", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.use2 is boolean;
        annotation { "Name" : "Input 2 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.in2Key is string;
        annotation { "Name" : "Use input 3", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.use3 is boolean;
        annotation { "Name" : "Input 3 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.in3Key is string;
        annotation { "Name" : "Use input 4", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.use4 is boolean;
        annotation { "Name" : "Input 4 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.in4Key is string;
        annotation { "Name" : "Use input 5", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.use5 is boolean;
        annotation { "Name" : "Input 5 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.in5Key is string;
        annotation { "Name" : "Use input 6", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.use6 is boolean;
        annotation { "Name" : "Input 6 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.in6Key is string;
        annotation { "Name" : "Use input 7", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.use7 is boolean;
        annotation { "Name" : "Input 7 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.in7Key is string;
        annotation { "Name" : "Use input 8", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.use8 is boolean;
        annotation { "Name" : "Input 8 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.in8Key is string;
        annotation { "Name" : "Use value 1", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.useValue1 is boolean;
        annotation { "Name" : "Value 1 type", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.v1Kind is CaseSlotKind;
        annotation { "Name" : "Value 1 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.v1Key is string;
        annotation { "Name" : "Use value 2", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.useValue2 is boolean;
        annotation { "Name" : "Value 2 type", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.v2Kind is CaseSlotKind;
        annotation { "Name" : "Value 2 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.v2Key is string;
        annotation { "Name" : "Use value 3", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.useValue3 is boolean;
        annotation { "Name" : "Value 3 type", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.v3Kind is CaseSlotKind;
        annotation { "Name" : "Value 3 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.v3Key is string;
        annotation { "Name" : "Use value 4", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.useValue4 is boolean;
        annotation { "Name" : "Value 4 type", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.v4Kind is CaseSlotKind;
        annotation { "Name" : "Value 4 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.v4Key is string;
        annotation { "Name" : "Use value 5", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.useValue5 is boolean;
        annotation { "Name" : "Value 5 type", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.v5Kind is CaseSlotKind;
        annotation { "Name" : "Value 5 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.v5Key is string;
        annotation { "Name" : "Use value 6", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.useValue6 is boolean;
        annotation { "Name" : "Value 6 type", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.v6Kind is CaseSlotKind;
        annotation { "Name" : "Value 6 key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.v6Key is string;

        if (definition.use1)
        {
            annotation { "Name" : "Input 1", "Default" : "", "UIHint" : UIHint.READ_ONLY }
            definition.in1Name is string;
            annotation { "Name" : "Selection", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
            definition.input1 is Query;
        }

        if (definition.use2)
        {
            annotation { "Name" : "Input 2", "Default" : "", "UIHint" : UIHint.READ_ONLY }
            definition.in2Name is string;
            annotation { "Name" : "Selection", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
            definition.input2 is Query;
        }

        if (definition.use3)
        {
            annotation { "Name" : "Input 3", "Default" : "", "UIHint" : UIHint.READ_ONLY }
            definition.in3Name is string;
            annotation { "Name" : "Selection", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
            definition.input3 is Query;
        }

        if (definition.use4)
        {
            annotation { "Name" : "Input 4", "Default" : "", "UIHint" : UIHint.READ_ONLY }
            definition.in4Name is string;
            annotation { "Name" : "Selection", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
            definition.input4 is Query;
        }

        if (definition.use5)
        {
            annotation { "Name" : "Input 5", "Default" : "", "UIHint" : UIHint.READ_ONLY }
            definition.in5Name is string;
            annotation { "Name" : "Selection", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
            definition.input5 is Query;
        }

        if (definition.use6)
        {
            annotation { "Name" : "Input 6", "Default" : "", "UIHint" : UIHint.READ_ONLY }
            definition.in6Name is string;
            annotation { "Name" : "Selection", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
            definition.input6 is Query;
        }

        if (definition.use7)
        {
            annotation { "Name" : "Input 7", "Default" : "", "UIHint" : UIHint.READ_ONLY }
            definition.in7Name is string;
            annotation { "Name" : "Selection", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
            definition.input7 is Query;
        }

        if (definition.use8)
        {
            annotation { "Name" : "Input 8", "Default" : "", "UIHint" : UIHint.READ_ONLY }
            definition.in8Name is string;
            annotation { "Name" : "Selection", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
            definition.input8 is Query;
        }

        if (definition.useValue1)
        {
            annotation { "Name" : "Value 1", "Default" : "", "UIHint" : UIHint.READ_ONLY }
            definition.v1Name is string;
            if (definition.v1Kind == CaseSlotKind.LENGTH)
            {
                annotation { "Name" : "Value" }
                isLength(definition.v1Length, CASE_LENGTH_BOUNDS);
            }
            if (definition.v1Kind == CaseSlotKind.ANGLE)
            {
                annotation { "Name" : "Value" }
                isAngle(definition.v1Angle, CASE_ANGLE_BOUNDS);
            }
            if (definition.v1Kind == CaseSlotKind.AREA)
            {
                annotation { "Name" : "Value (mm^2)" }
                isReal(definition.v1Area, CASE_REAL_BOUNDS);
            }
            if (definition.v1Kind == CaseSlotKind.VOLUME)
            {
                annotation { "Name" : "Value (mm^3)" }
                isReal(definition.v1Volume, CASE_REAL_BOUNDS);
            }
            if (definition.v1Kind == CaseSlotKind.NUMBER)
            {
                annotation { "Name" : "Value" }
                isReal(definition.v1Number, CASE_REAL_BOUNDS);
            }
            if (definition.v1Kind == CaseSlotKind.INTEGER)
            {
                annotation { "Name" : "Value" }
                isInteger(definition.v1Integer, CASE_INTEGER_BOUNDS);
            }
            if (definition.v1Kind == CaseSlotKind.TEXT)
            {
                annotation { "Name" : "Value", "Default" : "" }
                definition.v1Text is string;
            }
            if (definition.v1Kind == CaseSlotKind.BOOLEAN)
            {
                annotation { "Name" : "Value", "Default" : "true", "Description" : "An expression that is true or false: true, false, #other, !#other." }
                isAnything(definition.v1Boolean);
            }
        }

        if (definition.useValue2)
        {
            annotation { "Name" : "Value 2", "Default" : "", "UIHint" : UIHint.READ_ONLY }
            definition.v2Name is string;
            if (definition.v2Kind == CaseSlotKind.LENGTH)
            {
                annotation { "Name" : "Value" }
                isLength(definition.v2Length, CASE_LENGTH_BOUNDS);
            }
            if (definition.v2Kind == CaseSlotKind.ANGLE)
            {
                annotation { "Name" : "Value" }
                isAngle(definition.v2Angle, CASE_ANGLE_BOUNDS);
            }
            if (definition.v2Kind == CaseSlotKind.AREA)
            {
                annotation { "Name" : "Value (mm^2)" }
                isReal(definition.v2Area, CASE_REAL_BOUNDS);
            }
            if (definition.v2Kind == CaseSlotKind.VOLUME)
            {
                annotation { "Name" : "Value (mm^3)" }
                isReal(definition.v2Volume, CASE_REAL_BOUNDS);
            }
            if (definition.v2Kind == CaseSlotKind.NUMBER)
            {
                annotation { "Name" : "Value" }
                isReal(definition.v2Number, CASE_REAL_BOUNDS);
            }
            if (definition.v2Kind == CaseSlotKind.INTEGER)
            {
                annotation { "Name" : "Value" }
                isInteger(definition.v2Integer, CASE_INTEGER_BOUNDS);
            }
            if (definition.v2Kind == CaseSlotKind.TEXT)
            {
                annotation { "Name" : "Value", "Default" : "" }
                definition.v2Text is string;
            }
            if (definition.v2Kind == CaseSlotKind.BOOLEAN)
            {
                annotation { "Name" : "Value", "Default" : "true", "Description" : "An expression that is true or false: true, false, #other, !#other." }
                isAnything(definition.v2Boolean);
            }
        }

        if (definition.useValue3)
        {
            annotation { "Name" : "Value 3", "Default" : "", "UIHint" : UIHint.READ_ONLY }
            definition.v3Name is string;
            if (definition.v3Kind == CaseSlotKind.LENGTH)
            {
                annotation { "Name" : "Value" }
                isLength(definition.v3Length, CASE_LENGTH_BOUNDS);
            }
            if (definition.v3Kind == CaseSlotKind.ANGLE)
            {
                annotation { "Name" : "Value" }
                isAngle(definition.v3Angle, CASE_ANGLE_BOUNDS);
            }
            if (definition.v3Kind == CaseSlotKind.AREA)
            {
                annotation { "Name" : "Value (mm^2)" }
                isReal(definition.v3Area, CASE_REAL_BOUNDS);
            }
            if (definition.v3Kind == CaseSlotKind.VOLUME)
            {
                annotation { "Name" : "Value (mm^3)" }
                isReal(definition.v3Volume, CASE_REAL_BOUNDS);
            }
            if (definition.v3Kind == CaseSlotKind.NUMBER)
            {
                annotation { "Name" : "Value" }
                isReal(definition.v3Number, CASE_REAL_BOUNDS);
            }
            if (definition.v3Kind == CaseSlotKind.INTEGER)
            {
                annotation { "Name" : "Value" }
                isInteger(definition.v3Integer, CASE_INTEGER_BOUNDS);
            }
            if (definition.v3Kind == CaseSlotKind.TEXT)
            {
                annotation { "Name" : "Value", "Default" : "" }
                definition.v3Text is string;
            }
            if (definition.v3Kind == CaseSlotKind.BOOLEAN)
            {
                annotation { "Name" : "Value", "Default" : "true", "Description" : "An expression that is true or false: true, false, #other, !#other." }
                isAnything(definition.v3Boolean);
            }
        }

        if (definition.useValue4)
        {
            annotation { "Name" : "Value 4", "Default" : "", "UIHint" : UIHint.READ_ONLY }
            definition.v4Name is string;
            if (definition.v4Kind == CaseSlotKind.LENGTH)
            {
                annotation { "Name" : "Value" }
                isLength(definition.v4Length, CASE_LENGTH_BOUNDS);
            }
            if (definition.v4Kind == CaseSlotKind.ANGLE)
            {
                annotation { "Name" : "Value" }
                isAngle(definition.v4Angle, CASE_ANGLE_BOUNDS);
            }
            if (definition.v4Kind == CaseSlotKind.AREA)
            {
                annotation { "Name" : "Value (mm^2)" }
                isReal(definition.v4Area, CASE_REAL_BOUNDS);
            }
            if (definition.v4Kind == CaseSlotKind.VOLUME)
            {
                annotation { "Name" : "Value (mm^3)" }
                isReal(definition.v4Volume, CASE_REAL_BOUNDS);
            }
            if (definition.v4Kind == CaseSlotKind.NUMBER)
            {
                annotation { "Name" : "Value" }
                isReal(definition.v4Number, CASE_REAL_BOUNDS);
            }
            if (definition.v4Kind == CaseSlotKind.INTEGER)
            {
                annotation { "Name" : "Value" }
                isInteger(definition.v4Integer, CASE_INTEGER_BOUNDS);
            }
            if (definition.v4Kind == CaseSlotKind.TEXT)
            {
                annotation { "Name" : "Value", "Default" : "" }
                definition.v4Text is string;
            }
            if (definition.v4Kind == CaseSlotKind.BOOLEAN)
            {
                annotation { "Name" : "Value", "Default" : "true", "Description" : "An expression that is true or false: true, false, #other, !#other." }
                isAnything(definition.v4Boolean);
            }
        }

        if (definition.useValue5)
        {
            annotation { "Name" : "Value 5", "Default" : "", "UIHint" : UIHint.READ_ONLY }
            definition.v5Name is string;
            if (definition.v5Kind == CaseSlotKind.LENGTH)
            {
                annotation { "Name" : "Value" }
                isLength(definition.v5Length, CASE_LENGTH_BOUNDS);
            }
            if (definition.v5Kind == CaseSlotKind.ANGLE)
            {
                annotation { "Name" : "Value" }
                isAngle(definition.v5Angle, CASE_ANGLE_BOUNDS);
            }
            if (definition.v5Kind == CaseSlotKind.AREA)
            {
                annotation { "Name" : "Value (mm^2)" }
                isReal(definition.v5Area, CASE_REAL_BOUNDS);
            }
            if (definition.v5Kind == CaseSlotKind.VOLUME)
            {
                annotation { "Name" : "Value (mm^3)" }
                isReal(definition.v5Volume, CASE_REAL_BOUNDS);
            }
            if (definition.v5Kind == CaseSlotKind.NUMBER)
            {
                annotation { "Name" : "Value" }
                isReal(definition.v5Number, CASE_REAL_BOUNDS);
            }
            if (definition.v5Kind == CaseSlotKind.INTEGER)
            {
                annotation { "Name" : "Value" }
                isInteger(definition.v5Integer, CASE_INTEGER_BOUNDS);
            }
            if (definition.v5Kind == CaseSlotKind.TEXT)
            {
                annotation { "Name" : "Value", "Default" : "" }
                definition.v5Text is string;
            }
            if (definition.v5Kind == CaseSlotKind.BOOLEAN)
            {
                annotation { "Name" : "Value", "Default" : "true", "Description" : "An expression that is true or false: true, false, #other, !#other." }
                isAnything(definition.v5Boolean);
            }
        }

        if (definition.useValue6)
        {
            annotation { "Name" : "Value 6", "Default" : "", "UIHint" : UIHint.READ_ONLY }
            definition.v6Name is string;
            if (definition.v6Kind == CaseSlotKind.LENGTH)
            {
                annotation { "Name" : "Value" }
                isLength(definition.v6Length, CASE_LENGTH_BOUNDS);
            }
            if (definition.v6Kind == CaseSlotKind.ANGLE)
            {
                annotation { "Name" : "Value" }
                isAngle(definition.v6Angle, CASE_ANGLE_BOUNDS);
            }
            if (definition.v6Kind == CaseSlotKind.AREA)
            {
                annotation { "Name" : "Value (mm^2)" }
                isReal(definition.v6Area, CASE_REAL_BOUNDS);
            }
            if (definition.v6Kind == CaseSlotKind.VOLUME)
            {
                annotation { "Name" : "Value (mm^3)" }
                isReal(definition.v6Volume, CASE_REAL_BOUNDS);
            }
            if (definition.v6Kind == CaseSlotKind.NUMBER)
            {
                annotation { "Name" : "Value" }
                isReal(definition.v6Number, CASE_REAL_BOUNDS);
            }
            if (definition.v6Kind == CaseSlotKind.INTEGER)
            {
                annotation { "Name" : "Value" }
                isInteger(definition.v6Integer, CASE_INTEGER_BOUNDS);
            }
            if (definition.v6Kind == CaseSlotKind.TEXT)
            {
                annotation { "Name" : "Value", "Default" : "" }
                definition.v6Text is string;
            }
            if (definition.v6Kind == CaseSlotKind.BOOLEAN)
            {
                annotation { "Name" : "Value", "Default" : "true", "Description" : "An expression that is true or false: true, false, #other, !#other." }
                isAnything(definition.v6Boolean);
            }
        }
    }
    {
        // Listed in a Close case's features by mistake: nothing to do in a replay.
        if (isInFeaturePattern(context))
        {
            return;
        }
        const define = findSignature(context, definition.defineCase, "caseDefine");
        if (define == undefined || define.count > 1)
        {
            throw regenError("Select one Define case.", ["defineCase"]);
        }
        const signature = define.signature;
        if (match(definition.caseName, "[A-Za-z][A-Za-z0-9_]*").hasMatch != true)
        {
            throw regenError("Name the case with letters, digits and _, starting with a letter.", ["caseName"]);
        }
        if (definition.caseName == signature.caseName)
        {
            throw regenError("\"" ~ definition.caseName ~ "\" is case 1's name.", ["caseName"]);
        }

        var selections = [];
        for (var name in signature.names)
        {
            const slot = findSlot(definition, "in", CASE_MAX_INPUTS, name);
            if (slot == undefined)
            {
                throw regenError("#" ~ name ~ " has no slot: click Update from Define case.", ["updateSlots"]);
            }
            const selection = definition["input" ~ slot];
            if (!(selection is Query) || isQueryEmpty(context, selection))
            {
                throw regenError("#" ~ name ~ " selects nothing.", ["input" ~ slot]);
            }
            selections = append(selections, selection);
        }
        var values = [];
        for (var m = 0; m < size(signature.valueNames); m += 1)
        {
            const name = signature.valueNames[m];
            const kind = signature.valueKinds[m];
            const slot = findSlot(definition, "v", CASE_MAX_VALUES, name);
            if (slot == undefined || definition["v" ~ slot ~ "Kind"] != slotKind(kind))
            {
                throw regenError("#" ~ name ~ " has no value: click Update from Define case.", ["updateSlots"]);
            }
            const value = typedValue(definition, "v" ~ slot, kind);
            verifyValueKind(value, kind, name, "v" ~ slot ~ "Boolean");
            values = append(values, value);
        }

        setVariable(context, toString(id), {
                    "caseRecord" : true,
                    "defineKey" : toString(define.featureId),
                    "caseName" : definition.caseName,
                    "selections" : selections,
                    "values" : values
                }, "Case");
    }, {});

/**
 * Case editing logic: lays the slots out from the Define case's signature -- one labelled selection per
 * input, one labelled field of the right type per value -- keeping each slot's selection or value by NAME.
 * A new slot, or one whose type changed, starts at case 1's value. Runs on any change in the dialog, including
 * the "Update from Define case" button; typing a name or value leaves a laid-out definition untouched.
 */
export function caseEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map, hiddenBodies is Query, clickedButton is string) returns map
{
    const define = findSignature(context, definition.defineCase, "caseDefine");
    if (define == undefined)
    {
        return definition;
    }
    const oldDefine = oldDefinition.defineCase is map ? toString(keys(oldDefinition.defineCase)) : "";
    const everything = isCreating || clickedButton == "updateSlots" || oldDefine != toString(keys(definition.defineCase));
    if (everything || !isLaidOut(definition, define.signature))
    {
        return layoutRow(definition, define.signature);
    }
    return definition;
}

// ---------------------------------------------------------------------------------------------
// Close case
// ---------------------------------------------------------------------------------------------

annotation { "Feature Type Name" : "Close case", "Icon" : CloseIconNamespace::BLOB_DATA,
        "Feature Type Description" : "Ends a repeatable feature chain and runs it: names its Define case, lists the features to repeat and the Case features, and replays the features once per case. Outputs are published per case as #<case>_<name>." }
export const closeCase = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Define case", "Description" : "The Define case declaring the inputs these features use." }
        definition.defineCase is FeatureList;

        annotation { "Name" : "Features to repeat",
                    "Description" : "The features built on the Define case's inputs. Reference geometry they create through a Query Variable, not by clicking it. Geometry from before them may be clicked directly." }
        definition.features is FeatureList;

        annotation { "Name" : "Cases",
                    "Description" : "The Case features to run, in tree order. Case 1 is the Define case itself and is not listed." }
        definition.cases is FeatureList;

        annotation { "Name" : "Outputs", "Item name" : "Output", "Item label template" : "#outputName", "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS,
                    "Description" : "Query variables the repeated features set, published by every case as #<case>_<name> (case 1 included)." }
        definition.outputs is array;
        for (var output in definition.outputs)
        {
            annotation { "Name" : "Name", "Default" : "", "MaxLength" : 64 }
            output.outputName is string;

            annotation { "Name" : "Query variable", "Default" : "", "MaxLength" : 64,
                        "Description" : "The name of a query variable the repeated features set (usually a Query Variable feature 'created by' one of them), without #. Read after each case." }
            output.outputVariable is string;

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

        annotation { "Name" : "Name parts after outputs", "Default" : true,
                    "Description" : "On: a part an output points to is named like its variable: output 'rib' in case B -> part 'B_rib' (case 1 too). Off: names are left to the repeated features." }
        definition.nameParts is boolean;

        annotation { "Name" : "Print bindings", "Default" : false,
                    "Description" : "Print every case's selections and values to the FeatureScript notices." }
        definition.debug is boolean;
    }
    {
        // Listed in another Close case's features by mistake: never run a chain from inside a replay.
        if (isInFeaturePattern(context))
        {
            return;
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
        const signature = define.signature;
        const defineKey = toString(define.featureId);

        var outputNames = [];
        for (var output in definition.outputs)
        {
            verifyDeclaredName(output.outputName, outputNames, "outputs");
            verifyVariableNameIsValid(output.outputVariable, "outputs");
            outputNames = append(outputNames, output.outputName);
        }

        // Case 1: its outputs as the body left them. The first Close case of a Define case publishes them; a
        // later one finds case 1's name already used and leaves them.
        const namesKey = caseNamesKey(defineKey);
        var usedNames = getVariable(context, namesKey, MISSING);
        if (!(usedNames is array))
        {
            usedNames = [signature.caseName];
        }
        const publishedKey = namesKey ~ "-caseOnePublished";
        const first = getVariable(context, publishedKey, MISSING) != true;
        var caseOneOutputs = [];
        for (var output in definition.outputs)
        {
            const q = try silent(getQueryVariable(context, output.outputVariable));
            if (!(q is Query))
            {
                throw regenError("#" ~ output.outputVariable ~ " is not set by the repeated features.", ["outputs"]);
            }
            caseOneOutputs = append(caseOneOutputs, q);
            if (first)
            {
                publishOutput(context, signature.caseName, output, q, definition.nameParts);
            }
        }

        // The further cases, in tree order.
        var records = [];
        for (var featureId in sortedFeatureIds(context, definition.cases))
        {
            const record = getVariable(context, toString(featureId), MISSING);
            if (!(record is map) || record.caseRecord != true)
            {
                throw regenError("Cases takes Case features only (a failing Case feature is skipped here too).", ["cases"]);
            }
            if (record.defineKey != defineKey)
            {
                throw regenError("Case " ~ record.caseName ~ " belongs to another Define case.", ["cases"]);
            }
            records = append(records, record);
        }

        const sharedNames = signature.sharedNames is array ? signature.sharedNames : [];
        var sharedResolved = [];
        for (var n = 0; n < size(sharedNames); n += 1)
        {
            sharedResolved = append(sharedResolved, qUnion(evaluateQuery(context, signature.sharedQueries[n])));
        }
        const functions = valuesSortedById(context, definition.features);
        const keep = {
                "keepParts" : definition.keepParts,
                "keepSurfaces" : definition.keepSurfaces,
                "keepCurves" : definition.keepCurves,
                "keepMateConnectors" : definition.keepMateConnectors,
                "keepPlanes" : definition.keepPlanes,
                "keepSketches" : definition.keepSketches
            };
        var failures = [];
        var notes = [];

        for (var record in records)
        {
            const caseName = record.caseName;
            if (isIn(caseName, usedNames))
            {
                failures = append(failures, caseName ~ ": the case name is already used by this Define case");
                continue;
            }
            if (size(record.selections) != size(signature.names) || size(record.values) != size(signature.valueNames))
            {
                failures = append(failures, caseName ~ ": the Define case changed; click Update from Define case in this Case");
                continue;
            }
            const caseIndex = size(usedNames) + 1;
            usedNames = append(usedNames, caseName);

            // Bind every input as the entities it resolves to HERE, before the frame is pushed.
            for (var n = 0; n < size(signature.names); n += 1)
            {
                setQueryVariable(context, signature.names[n], qUnion(evaluateQuery(context, record.selections[n])));
            }
            for (var m = 0; m < size(record.values); m += 1)
            {
                setVariable(context, signature.valueNames[m], record.values[m]);
            }
            for (var n = 0; n < size(sharedNames); n += 1)
            {
                setQueryVariable(context, sharedNames[n], sharedResolved[n]);
            }
            setVariable(context, "caseName", caseName);
            setVariable(context, "caseIndex", caseIndex);
            if (definition.debug)
            {
                println("Case " ~ caseName ~ " (#caseIndex " ~ caseIndex ~ "):");
                for (var n = 0; n < size(signature.names); n += 1)
                {
                    println("  #" ~ signature.names[n] ~ " = " ~ size(evaluateQuery(context, record.selections[n])) ~ " entities");
                }
                for (var m = 0; m < size(record.values); m += 1)
                {
                    println("  #" ~ signature.valueNames[m] ~ " = " ~ toString(record.values[m]));
                }
            }

            // Run the case in a pattern frame pushed HERE, by the feature that owns the list (correction 60).
            const caseId = id + ("case_" ~ caseName);
            var failure = undefined;
            setFeaturePatternInstanceData(context, caseId, { "transform" : identityTransform() });
            for (var i = 0; i < size(functions); i += 1)
            {
                const outcome = runListedFeature(context, functions, i, caseId);
                if (outcome.error != undefined)
                {
                    failure = "repeated feature " ~ (i + 1) ~ " failed (" ~ outcome.error ~ ")";
                    break;
                }
                if (!outcome.inFrame && definition.debug)
                {
                    println("  repeated feature " ~ (i + 1) ~ " edits geometry from before the repeated features: ran outside the pattern frame");
                }
            }
            unsetFeaturePatternInstanceData(context, caseId);

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
            const dropped = unkeptBodies(caseBodies, keep);
            if (!isQueryEmpty(context, dropped))
            {
                opDeleteBodies(context, id + ("drop_" ~ caseName), { "entities" : dropped });
            }
            for (var output in definition.outputs)
            {
                const q = getQueryVariable(context, output.outputVariable);
                publishOutput(context, caseName, output, q, definition.nameParts);
                const found = evaluateQuery(context, q);
                if (size(found) > 0 && isQueryEmpty(context, qIntersection([qUnion(found), qCreatedBy(caseId)])))
                {
                    notes = append(notes, "#" ~ caseName ~ "_" ~ output.outputName ~ " is not case " ~ caseName
                                ~ "'s geometry: make #" ~ output.outputVariable ~ " a query variable 'created by' a repeated feature");
                }
            }
        }

        // Later features see case 1 again: its inputs, values and outputs (other variables the repeated
        // features set keep the last case's).
        for (var n = 0; n < size(signature.names); n += 1)
        {
            setQueryVariable(context, signature.names[n], signature.queries[n]);
        }
        for (var m = 0; m < size(signature.valueNames); m += 1)
        {
            setVariable(context, signature.valueNames[m], signature.values[m]);
        }
        for (var n = 0; n < size(sharedNames); n += 1)
        {
            setQueryVariable(context, sharedNames[n], signature.sharedQueries[n]);
        }
        for (var k = 0; k < size(definition.outputs); k += 1)
        {
            setQueryVariable(context, definition.outputs[k].outputVariable, caseOneOutputs[k]);
        }
        setVariable(context, "caseName", signature.caseName);
        setVariable(context, "caseIndex", 1);
        setVariable(context, namesKey, usedNames);
        setVariable(context, publishedKey, true);

        if (size(records) > 0 && size(failures) == size(records))
        {
            throw regenError("No case was built. " ~ join(failures, "; "), ["cases"]);
        }
        if (size(failures) > 0)
        {
            reportFeatureWarning(context, id, "Not built: " ~ join(failures, "; "));
            return;
        }
        if (size(records) > 0 && containsSketch(context, definition.features))
        {
            notes = append(notes, "Sketches are re-solved per case. Dimensions and constraints to the origin or the default planes are not"
                    ~ " reapplied, so those entities keep case 1's position; constrain sketches to geometry derived from the inputs.");
        }
        if (size(notes) > 0)
        {
            reportFeatureInfo(context, id, join(notes, " "));
        }
    }, {
        "outputs" : [],
        "keepParts" : true,
        "keepSurfaces" : true,
        "keepCurves" : true,
        "keepMateConnectors" : true,
        "keepPlanes" : true,
        "keepSketches" : false,
        "nameParts" : true,
        "debug" : false
    });

/**
 * Runs listed feature `i` in the pattern frame the Close case pushed on `caseId`.
 *
 * The frame (identity transform) is what makes FeatureList parameters of the listed features -- a Query
 * Variable "created by", say -- resolve to this case's copies. Clicks on geometry the listed features make
 * are NOT remapped (they keep pointing at case 1, correction 41); clicks on geometry from before them resolve
 * as clicked (correction 60).
 *
 * A kernel op that edits geometry from outside the list (fillet an existing edge, move an existing face)
 * refuses to run in the frame with SELF_INTERSECTING_CURVE_SELECTED -- Query Pattern's open Move face bug.
 * Only that refusal is retried, once, with the frame popped and under a fresh sub-id (the aborted attempt's
 * ids are not reusable); any other failure stands, so a feature whose in-list reference did not remap cannot
 * fall back onto case 1's geometry.
 */
function runListedFeature(context is Context, functions is array, i is number, caseId is Id) returns map
{
    var frameError = undefined;
    try
    {
        functions[i](caseId);
    }
    catch (e)
    {
        frameError = errorText(e);
    }
    if (frameError == undefined)
    {
        return { "inFrame" : true };
    }
    if (indexOf(frameError, "SELF_INTERSECTING_CURVE_SELECTED") < 0)
    {
        return { "inFrame" : true, "error" : frameError };
    }
    unsetFeaturePatternInstanceData(context, caseId);
    var directError = undefined;
    try
    {
        functions[i](caseId + ("direct" ~ i));
    }
    catch (e)
    {
        directError = errorText(e);
    }
    setFeaturePatternInstanceData(context, caseId, { "transform" : identityTransform() });
    if (directError != undefined)
    {
        return { "inFrame" : false, "error" : directError };
    }
    return { "inFrame" : false };
}

/** Publishes one output of one case (#<case>_<name>) and, when asked, names its parts after it. */
function publishOutput(context is Context, caseName is string, output is map, q is Query, nameParts is boolean)
{
    const onUse = output.outputOnUse;
    const track = !onUse && output.outputTrack == true;
    publishCaseOutput(context, caseName, output.outputName, q, onUse, track, "outputs");
    if (nameParts)
    {
        nameOutputParts(context, q, caseName ~ "_" ~ output.outputName);
    }
}

// ---------------------------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------------------------

/** True when a case row's slots already carry the signature's names and value types. */
function isLaidOut(row is map, signature is map) returns boolean
{
    const count = size(signature.names);
    for (var k = 1; k <= CASE_MAX_INPUTS; k += 1)
    {
        if (row["in" ~ k ~ "Key"] != (k <= count ? signature.names[k - 1] : ""))
        {
            return false;
        }
    }
    const valueCount = size(signature.valueNames);
    for (var m = 1; m <= CASE_MAX_VALUES; m += 1)
    {
        if (row["v" ~ m ~ "Key"] != (m <= valueCount ? signature.valueNames[m - 1] : ""))
        {
            return false;
        }
        if (m <= valueCount && row["v" ~ m ~ "Kind"] != slotKind(signature.valueKinds[m - 1]))
        {
            return false;
        }
    }
    return true;
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
        if (used)
        {
            // Unused slots are hidden and unbound (their key is cleared); leaving them untouched keeps
            // the returned definition small.
            const kept = oldInputs[name];
            result["input" ~ k] = kept is Query ? kept : qNothing();
        }
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

/**
 * Names the parts an output points to after the output's variable ("B_rib"): the bodies holding its
 * entities, sketches and mate connectors excluded (connectors cannot be named, correction 44). A second
 * or later part of one output gets "_2", "_3", ... so no two parts share a name.
 */
function nameOutputParts(context is Context, q is Query, name is string)
{
    const bodies = evaluateQuery(context, qSketchFilter(qBodyType(qUnion([qEntityFilter(q, EntityType.BODY), qOwnerBody(q)]),
                    [BodyType.SOLID, BodyType.SHEET, BodyType.WIRE, BodyType.POINT]), SketchObject.NO));
    for (var i = 0; i < size(bodies); i += 1)
    {
        setProperty(context, { "entities" : bodies[i], "propertyType" : PropertyType.NAME, "value" : i == 0 ? name : name ~ "_" ~ (i + 1) });
    }
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
