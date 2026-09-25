FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
import(path : "onshape/std/queryVariable.fs", version : "3083.0");

/**
 * Case Pattern: build a chain of features once against named query variables (case 1), then
 * re-run that chain for further cases, each case rebinding every name to new geometry and
 * suffixing the bodies it creates with its case name.
 *
 * Case template   declares the inputs (query variable names) and case values (# variables), binds
 *                 case 1, and holds the further cases. Publishes all of it under its own feature id.
 * Case pattern    takes a Case template and the features built on it and, per case, binds the
 *                 inputs and values and re-runs the features the way a feature Pattern with
 *                 "Reapply features" runs an instance (identity transform: nothing moves).
 *
 * Referencing geometry made by the repeated features: only a FeatureList parameter follows each
 * case (a Query Variable "created by"); clicks stay on case 1. Correction 41; design in
 * case_pattern/DESIGN.md.
 */

/** Most query variables one template may declare (the number of input slots in a case row). */
export const CASE_MAX_INPUTS = 8;

/** Most case values one template may declare (the number of value slots in a case row). */
export const CASE_MAX_VALUES = 4;

/** Returned by getVariable when a name is not set (an undefined default still throws). */
const MISSING = "__caseMissing__";

const CASE_LENGTH_BOUNDS = { (millimeter) : [-1e7, 0, 1e7] } as LengthBoundSpec;
const CASE_ANGLE_BOUNDS = { (degree) : [-1e6, 0, 1e6] } as AngleBoundSpec;
// Area and volume are not dialog parameter types (correction 30): entered in mm^2 / mm^3.
const CASE_REAL_BOUNDS = { (unitless) : [-1e12, 0, 1e12] } as RealBoundSpec;

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
    annotation { "Name" : "Text" }
    TEXT
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
    TEXT
}

// ---------------------------------------------------------------------------------------------
// Case template
// ---------------------------------------------------------------------------------------------

annotation { "Feature Type Name" : "Case template",
        "Editing Logic Function" : "caseTemplateEditLogic",
        "Feature Type Description" : "Declares the inputs (query variables) and values (# variables) a Case pattern rebinds, binds case 1, and lists the further cases with their own selections and values. Build case 1's features on these names, then repeat them with a Case pattern." }
export const caseTemplate = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Case 1 name", "Default" : "A", "MaxLength" : 64,
                    "Description" : "Case pattern swaps this suffix for each case's name when naming what the case creates." }
        definition.caseName is string;

        annotation { "Name" : "Inputs", "Item name" : "Input", "Item label template" : "#inputName" }
        definition.inputs is array;
        for (var input in definition.inputs)
        {
            annotation { "Name" : "Name", "Default" : "", "MaxLength" : 64,
                        "Description" : "Query variable name. It may already exist; it is redefined here." }
            input.inputName is string;

            annotation { "Name" : "Case 1 selection", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
            input.query is Query;
        }

        annotation { "Name" : "Case values", "Item name" : "Value", "Item label template" : "#valueName",
                    "Description" : "# variables that change per case. The template defines each with case 1's value; every further case gives its own." }
        definition.values is array;
        for (var value in definition.values)
        {
            annotation { "Name" : "Name", "Default" : "", "MaxLength" : 64 }
            value.valueName is string;

            annotation { "Name" : "Type" }
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
            if (value.valueKind == CaseValueKind.TEXT)
            {
                annotation { "Name" : "Case 1 value", "Default" : "" }
                value.valueText is string;
            }
        }

        // Which slot labels show; set by the editing logic from the input count.
        annotation { "Name" : "Show slot 2", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.showSlot2 is boolean;
        annotation { "Name" : "Show slot 3", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.showSlot3 is boolean;
        annotation { "Name" : "Show slot 4", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.showSlot4 is boolean;
        annotation { "Name" : "Show slot 5", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.showSlot5 is boolean;
        annotation { "Name" : "Show slot 6", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.showSlot6 is boolean;
        annotation { "Name" : "Show slot 7", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.showSlot7 is boolean;
        annotation { "Name" : "Show slot 8", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.showSlot8 is boolean;

        annotation { "Group Name" : "Input slots", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Slot 1", "Default" : "", "UIHint" : UIHint.READ_ONLY }
            definition.slot1 is string;
            if (definition.showSlot2)
            {
                annotation { "Name" : "Slot 2", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                definition.slot2 is string;
            }
            if (definition.showSlot3)
            {
                annotation { "Name" : "Slot 3", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                definition.slot3 is string;
            }
            if (definition.showSlot4)
            {
                annotation { "Name" : "Slot 4", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                definition.slot4 is string;
            }
            if (definition.showSlot5)
            {
                annotation { "Name" : "Slot 5", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                definition.slot5 is string;
            }
            if (definition.showSlot6)
            {
                annotation { "Name" : "Slot 6", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                definition.slot6 is string;
            }
            if (definition.showSlot7)
            {
                annotation { "Name" : "Slot 7", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                definition.slot7 is string;
            }
            if (definition.showSlot8)
            {
                annotation { "Name" : "Slot 8", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                definition.slot8 is string;
            }
        }

        annotation { "Name" : "Further cases", "Item name" : "Case", "Item label template" : "#rowCaseName" }
        definition.cases is array;
        for (var row in definition.cases)
        {
            annotation { "Name" : "Case name", "Default" : "", "MaxLength" : 64 }
            row.rowCaseName is string;

            // Hidden layout flags, set by the editing logic from the inputs and values above.
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
            annotation { "Name" : "Use value 1", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.useValue1 is boolean;
            annotation { "Name" : "Value 1 type", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.v1Kind is CaseSlotKind;
            annotation { "Name" : "Use value 2", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.useValue2 is boolean;
            annotation { "Name" : "Value 2 type", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.v2Kind is CaseSlotKind;
            annotation { "Name" : "Use value 3", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.useValue3 is boolean;
            annotation { "Name" : "Value 3 type", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.v3Kind is CaseSlotKind;
            annotation { "Name" : "Use value 4", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.useValue4 is boolean;
            annotation { "Name" : "Value 4 type", "UIHint" : UIHint.ALWAYS_HIDDEN }
            row.v4Kind is CaseSlotKind;

            annotation { "Name" : "Input 1", "Default" : "", "UIHint" : UIHint.READ_ONLY }
            row.in1Name is string;
            annotation { "Name" : "Selection", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR }
            row.input1 is Query;
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
                if (row.v1Kind == CaseSlotKind.TEXT)
                {
                    annotation { "Name" : "Value", "Default" : "" }
                    row.v1Text is string;
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
                if (row.v2Kind == CaseSlotKind.TEXT)
                {
                    annotation { "Name" : "Value", "Default" : "" }
                    row.v2Text is string;
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
                if (row.v3Kind == CaseSlotKind.TEXT)
                {
                    annotation { "Name" : "Value", "Default" : "" }
                    row.v3Text is string;
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
                if (row.v4Kind == CaseSlotKind.TEXT)
                {
                    annotation { "Name" : "Value", "Default" : "" }
                    row.v4Text is string;
                }
            }
        }

        annotation { "Name" : "Print bindings", "Default" : false,
                    "Description" : "Print the input slots and every case's selections and values to the FeatureScript notices." }
        definition.debug is boolean;
    }
    {
        // Re-run inside a Case pattern: the pattern has already bound this case.
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
            verifyVariableNameIsValid(input.inputName, "inputs");
            if (isIn(input.inputName, names))
            {
                throw regenError("Input name #" ~ input.inputName ~ " is used twice.", ["inputs"]);
            }
            if (isQueryEmpty(context, input.query))
            {
                throw regenError("Case 1 selection for #" ~ input.inputName ~ " selects nothing.", ["inputs"]);
            }
            setQueryVariable(context, input.inputName, input.query);
            names = append(names, input.inputName);
            queries = append(queries, input.query);
        }

        const valueCount = size(definition.values);
        if (valueCount > CASE_MAX_VALUES)
        {
            throw regenError("A Case template takes at most " ~ CASE_MAX_VALUES ~ " values.", ["values"]);
        }
        var valueNames = [];
        var valueKinds = [];
        var caseOneValues = [];
        for (var value in definition.values)
        {
            verifyVariableNameIsValid(value.valueName, "values");
            if (isIn(value.valueName, valueNames) || isIn(value.valueName, names))
            {
                throw regenError("Name #" ~ value.valueName ~ " is used twice.", ["values"]);
            }
            const caseOne = typedValue(value, "value", value.valueKind);
            setVariable(context, value.valueName, caseOne);
            valueNames = append(valueNames, value.valueName);
            valueKinds = append(valueKinds, value.valueKind);
            caseOneValues = append(caseOneValues, caseOne);
        }

        var caseNames = [definition.caseName];
        var cases = [];
        for (var row in definition.cases)
        {
            if (row.rowCaseName == "")
            {
                throw regenError("Name case " ~ (size(caseNames) + 1) ~ ".", ["cases"]);
            }
            if (isIn(row.rowCaseName, caseNames))
            {
                throw regenError("Case name \"" ~ row.rowCaseName ~ "\" is used twice.", ["cases"]);
            }
            caseNames = append(caseNames, row.rowCaseName);
            var selections = [];
            for (var n = 0; n < count; n += 1)
            {
                selections = append(selections, row["input" ~ (n + 1)]);
            }
            // A slot the editing logic has not typed yet reads as undefined; Case pattern reports it.
            var values = [];
            for (var m = 0; m < valueCount; m += 1)
            {
                values = append(values, typedValue(row, "v" ~ (m + 1), valueKinds[m]));
            }
            cases = append(cases, { "caseName" : row.rowCaseName, "queries" : selections, "values" : values });
        }

        setVariable(context, toString(id), {
                    "caseTemplate" : true,
                    "caseName" : definition.caseName,
                    "names" : names,
                    "queries" : queries,
                    "valueNames" : valueNames,
                    "values" : caseOneValues,
                    "cases" : cases,
                    "debug" : definition.debug
                }, "Case template");

        if (definition.debug)
        {
            println("Case template " ~ toString(id) ~ ", case " ~ definition.caseName ~ ":");
            for (var n = 0; n < count; n += 1)
            {
                println("  Input " ~ (n + 1) ~ ": #" ~ names[n] ~ " = " ~ size(evaluateQuery(context, queries[n])) ~ " entities");
            }
            for (var m = 0; m < valueCount; m += 1)
            {
                println("  Value " ~ (m + 1) ~ ": #" ~ valueNames[m] ~ " = " ~ toString(caseOneValues[m]));
            }
        }
    }, {
        "caseName" : "A",
        "inputs" : [],
        "values" : [],
        "cases" : [],
        "debug" : false,
        "slot1" : "",
        "slot2" : "",
        "slot3" : "",
        "slot4" : "",
        "slot5" : "",
        "slot6" : "",
        "slot7" : "",
        "slot8" : "",
        "showSlot2" : false,
        "showSlot3" : false,
        "showSlot4" : false,
        "showSlot5" : false,
        "showSlot6" : false,
        "showSlot7" : false,
        "showSlot8" : false
    });

/**
 * Case template editing logic: labels the input slots with the input names, and lays out every
 * case row -- one labelled selection per input, one labelled field of the right type per value.
 */
export function caseTemplateEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map) returns map
{
    const count = size(definition.inputs);
    const valueCount = size(definition.values);
    for (var k = 1; k <= CASE_MAX_INPUTS; k += 1)
    {
        definition["slot" ~ k] = k <= count ? "Input " ~ k ~ ": #" ~ definition.inputs[k - 1].inputName : "";
        if (k > 1)
        {
            definition["showSlot" ~ k] = k <= count;
        }
    }
    for (var r = 0; r < size(definition.cases); r += 1)
    {
        for (var k = 1; k <= CASE_MAX_INPUTS; k += 1)
        {
            if (k > 1)
            {
                definition.cases[r]["use" ~ k] = k <= count;
            }
            definition.cases[r]["in" ~ k ~ "Name"] = k <= count ? "#" ~ definition.inputs[k - 1].inputName : "";
        }
        for (var m = 1; m <= CASE_MAX_VALUES; m += 1)
        {
            const used = m <= valueCount;
            definition.cases[r]["useValue" ~ m] = used;
            definition.cases[r]["v" ~ m ~ "Kind"] = used ? slotKind(definition.values[m - 1].valueKind) : CaseSlotKind.NONE;
            definition.cases[r]["v" ~ m ~ "Name"] = used ? "#" ~ definition.values[m - 1].valueName ~ " (" ~ kindText(definition.values[m - 1].valueKind) ~ ")" : "";
        }
    }
    return definition;
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
    return source[prefix ~ "Text"];
}

/** A case row slot's type for a template value's type. */
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
    return CaseSlotKind.TEXT;
}

/** "length", "area in mm^2", ... for a slot label. */
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
    return "text";
}

// ---------------------------------------------------------------------------------------------
// Case pattern
// ---------------------------------------------------------------------------------------------

annotation { "Feature Type Name" : "Case pattern",
        "Editing Logic Function" : "casePatternEditLogic",
        "Feature Type Description" : "Re-runs the features built on a Case template once per further case, binding the template's inputs and values to each case's. Bodies a case creates are named after case 1's with the case name as suffix." }
export const casePattern = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Case template", "Description" : "The Case template holding the inputs and cases." }
        definition.template is FeatureList;

        annotation { "Name" : "Features to repeat",
                    "Description" : "The features built on the template's inputs. Reference geometry they create through a Query Variable 'created by', not by clicking it." }
        definition.features is FeatureList;

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

        // Case 1 body names, cached by the editing logic (getProperty throws during regen,
        // correction 36). One line per body: "<feature index>\t<body index>\t<name>".
        annotation { "Name" : "Template names", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.templateNames is string;
    }
    {
        const template = findTemplate(context, sortedFeatureIds(context, definition.template));
        if (template == undefined)
        {
            throw regenError("Select a Case template.", ["template"]);
        }
        if (template.count > 1)
        {
            throw regenError("Select one Case template.", ["template"]);
        }
        const signature = template.signature;
        const functions = valuesSortedById(context, definition.features);
        if (size(functions) == 0)
        {
            throw regenError("Select the features built on the template's inputs.", ["features"]);
        }
        const cases = signature.cases;
        const caseCount = size(cases);
        if (caseCount == 0)
        {
            reportFeatureInfo(context, id, "The Case template has no further cases.");
            return;
        }
        const nameCount = size(signature.names);
        const valueNames = signature.valueNames;

        const templateNames = parseTemplateNames(definition.templateNames);
        var failures = [];
        var unnamed = 0;

        for (var k = 0; k < caseCount; k += 1)
        {
            const thisCase = cases[k];
            const caseId = id + ("case" ~ k);

            // Bind every input and value to this case's.
            var bindFailure = undefined;
            for (var n = 0; n < nameCount; n += 1)
            {
                const selection = thisCase.queries[n];
                if (selection == undefined || isQueryEmpty(context, selection))
                {
                    bindFailure = "input " ~ (n + 1) ~ " (#" ~ signature.names[n] ~ ") selects nothing";
                    break;
                }
                setQueryVariable(context, signature.names[n], selection);
            }
            if (bindFailure == undefined)
            {
                for (var m = 0; m < size(valueNames); m += 1)
                {
                    const value = thisCase.values[m];
                    if (value == undefined)
                    {
                        bindFailure = "value " ~ (m + 1) ~ " (#" ~ valueNames[m] ~ ") is not set; edit the Case template";
                        break;
                    }
                    setVariable(context, valueNames[m], value);
                }
            }
            if (bindFailure != undefined)
            {
                failures = append(failures, thisCase.caseName ~ ": " ~ bindFailure);
                continue;
            }
            if (signature.debug)
            {
                println("Case " ~ thisCase.caseName ~ ":");
                for (var n = 0; n < nameCount; n += 1)
                {
                    println("  Input " ~ (n + 1) ~ ": #" ~ signature.names[n] ~ " = "
                            ~ size(evaluateQuery(context, thisCase.queries[n])) ~ " entities");
                }
                for (var name in valueNames)
                {
                    println("  #" ~ name ~ " = " ~ toString(getVariable(context, name)));
                }
            }

            // Run the features as a Pattern runs one instance (correction 31: never inside
            // startFeature), recording the bodies each creates so they can be named after case 1's.
            setFeaturePatternInstanceData(context, caseId, { "transform" : identityTransform() });
            var origins = [];
            var before = evaluateQuery(context, qCreatedBy(caseId, EntityType.BODY));
            var failure = undefined;
            for (var i = 0; i < size(functions); i += 1)
            {
                const outcome = runListedFeature(context, functions, i, caseId);
                if (outcome.error != undefined)
                {
                    failure = "feature " ~ (i + 1) ~ " failed (" ~ outcome.error ~ ")";
                    break;
                }
                if (signature.debug && !outcome.inFrame)
                {
                    println("  feature " ~ (i + 1) ~ " edits geometry from outside the list: ran outside the pattern frame");
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
                failures = append(failures, thisCase.caseName ~ ": " ~ failure);
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
                            "value" : caseBodyName(templateName, signature.caseName, thisCase.caseName, definition.separator)
                        });
            }
        }

        // Later features see case 1 again.
        for (var n = 0; n < nameCount; n += 1)
        {
            setQueryVariable(context, signature.names[n], signature.queries[n]);
        }
        for (var m = 0; m < size(valueNames); m += 1)
        {
            setVariable(context, valueNames[m], signature.values[m]);
        }

        if (size(failures) == caseCount)
        {
            throw regenError("No case was built. " ~ join(failures, "; "), ["template"]);
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
                    ~ " reapplied, so those entities keep case 1's position; constrain sketches to geometry derived from the inputs.");
        }
        if (unnamed > 0)
        {
            notes = append(notes, unnamed ~ " bod" ~ (unnamed == 1 ? "y" : "ies") ~ " kept Onshape's default name: edit this feature to"
                    ~ " refresh case 1's names.");
        }
        if (size(notes) > 0)
        {
            reportFeatureInfo(context, id, join(notes, " "));
        }
    }, {
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
 * Case pattern editing logic: caches the names of the bodies the repeated features created for
 * case 1 (the feature body cannot read names during regen).
 */
export function casePatternEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
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

/**
 * Runs one listed feature for a case whose pattern frame is already pushed.
 *
 * The frame (identity transform) is what makes FeatureList parameters of the listed features --
 * a Query Variable "created by", say -- resolve to this case's copies. Plain queries (clicks,
 * qCreatedBy(makeId(...))) are NOT remapped; they keep pointing at case 1 (verified 2026-09-24).
 *
 * A kernel op that edits geometry from outside the list (fillet an existing edge, move an existing
 * face) refuses to run in the frame with SELF_INTERSECTING_CURVE_SELECTED -- Query Pattern's open
 * Move face bug. Only that refusal is retried, once, with the frame popped and under a fresh
 * sub-id (the aborted attempt's ids are not reusable); any other failure stands, so a feature
 * whose in-list reference did not remap cannot fall back onto case 1's geometry.
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

/** A short text for a caught regen error. */
function errorText(e) returns string
{
    if (e is map && e.message != undefined)
    {
        return toString(e.message);
    }
    return toString(e);
}
