FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// IMPORT: betterMeasureUtils.fs
export import(path : "2cfcc5809c8901350804ef33", version : "376aba3ecbadebd303163b5f");


// ---------------------------------------------------------------------------
// Feature-level enums
// ---------------------------------------------------------------------------

export enum BMMeasurementType { DISTANCE, VECTOR, ANGLE, LENGTH }

// ---------------------------------------------------------------------------
// Editing Logic
// ---------------------------------------------------------------------------

export function betterMeasureEditingLogic(context is Context, id is Id,
    oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map,
    hiddenBodies is Query) returns map
{
    if (specifiedParameters.entity1 == true)
    {
        definition.entity1Type = getEntityBodyType(context, definition.entity1);
    }
    if (specifiedParameters.entity2 == true)
    {
        definition.entity2Type = getEntityBodyType(context, definition.entity2);
    }
    return definition;
}

// ---------------------------------------------------------------------------
// Feature definition
// ---------------------------------------------------------------------------

annotation { "Feature Type Name" : "Better Measure",
             "Feature Type Description" : "Extended measurement and variable creation tool",
             "Feature Name Template" : "Better Measure",
             "UIHint" : UIHint.NO_PREVIEW_PROVIDED,
             "Editing Logic Function" : "betterMeasureEditingLogic" }
export const betterMeasure = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        // --- Measurement type ---
        annotation { "Name" : "Measurement type", "UIHint" : UIHint.HORIZONTAL_ENUM }
        definition.measurementType is BMMeasurementType;

        // --- Coordinate system ---
        annotation { "Name" : "Coordinate system", "UIHint" : UIHint.SHOW_LABEL }
        definition.coordSystem is BMCoordSystem;

        if (definition.coordSystem == BMCoordSystem.MATE_CONNECTOR)
        {
            annotation { "Name" : "Coordinate system MC",
                         "Filter" : EntityType.BODY && BodyType.MATE_CONNECTOR,
                         "MaxNumberOfPicks" : 1,
                         "UIHint" : UIHint.PREVENT_CREATING_NEW_MATE_CONNECTORS }
            definition.csQuery is Query;
        }

        // --- Entity 1 ---
        annotation { "Name" : "Entity 1",
                     "Filter" : (EntityType.BODY && (BodyType.SOLID || BodyType.SHEET || BodyType.WIRE || BodyType.MATE_CONNECTOR))
                                 || EntityType.EDGE || EntityType.VERTEX,
                     "MaxNumberOfPicks" : 1 }
        definition.entity1 is Query;

        annotation { "Name" : "Entity 1 type", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.entity1Type is BMEntityType;

        if (definition.entity1Type == BMEntityType.SOLID)
        {
            annotation { "Name" : "Entity 1 solid reference", "UIHint" : UIHint.SHOW_LABEL }
            definition.entity1SolidRef is BMSolidRef;
        }

        if (definition.entity1Type == BMEntityType.MATE_CONNECTOR &&
            (definition.measurementType == BMMeasurementType.ANGLE || definition.measurementType == BMMeasurementType.VECTOR))
        {
            annotation { "Name" : "Entity 1 MC axis", "UIHint" : UIHint.SHOW_LABEL }
            definition.entity1MCAxis is BMMCAxis;
        }

        // --- Entity 2 (hidden for LENGTH) ---
        if (definition.measurementType != BMMeasurementType.LENGTH)
        {
            annotation { "Name" : "Entity 2",
                         "Filter" : (EntityType.BODY && (BodyType.SOLID || BodyType.SHEET || BodyType.WIRE || BodyType.MATE_CONNECTOR))
                                     || EntityType.EDGE || EntityType.VERTEX,
                         "MaxNumberOfPicks" : 1 }
            definition.entity2 is Query;

            annotation { "Name" : "Entity 2 type", "UIHint" : UIHint.ALWAYS_HIDDEN }
            definition.entity2Type is BMEntityType;

            if (definition.entity2Type == BMEntityType.SOLID)
            {
                annotation { "Name" : "Entity 2 solid reference", "UIHint" : UIHint.SHOW_LABEL }
                definition.entity2SolidRef is BMSolidRef;
            }

            if (definition.entity2Type == BMEntityType.MATE_CONNECTOR &&
                (definition.measurementType == BMMeasurementType.ANGLE || definition.measurementType == BMMeasurementType.VECTOR))
            {
                annotation { "Name" : "Entity 2 MC axis", "UIHint" : UIHint.SHOW_LABEL }
                definition.entity2MCAxis is BMMCAxis;
            }
        }

        // --- Along query (DISTANCE only) ---
        if (definition.measurementType == BMMeasurementType.DISTANCE)
        {
            annotation { "Name" : "Measure along edge/face" }
            definition.useAlong is boolean;

            if (definition.useAlong)
            {
                annotation { "Name" : "Along entity",
                             "Filter" : EntityType.EDGE || EntityType.FACE,
                             "MaxNumberOfPicks" : 1 }
                definition.alongQuery is Query;

                annotation { "Name" : "Keep measurement wire", "UIHint" : UIHint.DISPLAY_SHORT }
                definition.keepMeasurementWire is boolean;
            }
        }

        // --- Measurements group ---
        annotation { "Group Name" : "Measurements" }
        {
            if (definition.measurementType == BMMeasurementType.DISTANCE ||
                definition.measurementType == BMMeasurementType.VECTOR)
            {
                annotation { "Name" : "Distance", "UIHint" : UIHint.READ_ONLY }
                isLength(definition.displayDistance, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);
            }

            if (definition.measurementType == BMMeasurementType.ANGLE ||
                definition.measurementType == BMMeasurementType.VECTOR)
            {
                annotation { "Name" : "Angle", "UIHint" : UIHint.READ_ONLY }
                isAngle(definition.displayAngle, ANGLE_360_ZERO_DEFAULT_BOUNDS);
            }

            if (definition.measurementType == BMMeasurementType.LENGTH)
            {
                annotation { "Name" : "Length", "UIHint" : UIHint.READ_ONLY }
                isLength(definition.displayLength, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);
            }

            if (definition.measurementType == BMMeasurementType.VECTOR)
            {
                annotation { "Name" : "Flip direction", "UIHint" : [UIHint.OPPOSITE_DIRECTION, UIHint.DISPLAY_SHORT] }
                definition.flipVector is boolean;

                annotation { "Name" : "Normalize", "UIHint" : UIHint.DISPLAY_SHORT }
                definition.normalizeVector is boolean;

                annotation { "Name" : "Strip units", "UIHint" : UIHint.DISPLAY_SHORT }
                definition.stripVectorUnits is boolean;

                if (definition.entity1Type == BMEntityType.MATE_CONNECTOR &&
                    definition.entity2Type == BMEntityType.MATE_CONNECTOR)
                {
                    annotation { "Name" : "Show Euler angles", "UIHint" : UIHint.DISPLAY_SHORT }
                    definition.showEuler is boolean;
                }
            }
        }

        // --- Axis Deltas group (DISTANCE only) ---
        if (definition.measurementType == BMMeasurementType.DISTANCE)
        {
            annotation { "Group Name" : "Axis Deltas", "Collapsed By Default" : true }
            {
                annotation { "Name" : "Delta X", "UIHint" : UIHint.READ_ONLY }
                isLength(definition.displayDeltaX, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);

                annotation { "Name" : "Delta Y", "UIHint" : UIHint.READ_ONLY }
                isLength(definition.displayDeltaY, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);

                annotation { "Name" : "Delta Z", "UIHint" : UIHint.READ_ONLY }
                isLength(definition.displayDeltaZ, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);

                if (definition.useAlong)
                {
                    annotation { "Name" : "Distance along", "UIHint" : UIHint.READ_ONLY }
                    isLength(definition.displayDistanceAlong, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);
                }
            }
        }

        // --- Delta Vector group (VECTOR only) ---
        if (definition.measurementType == BMMeasurementType.VECTOR)
        {
            annotation { "Group Name" : "Delta Vector", "Collapsed By Default" : true }
            {
                annotation { "Name" : "Vec X", "UIHint" : UIHint.READ_ONLY }
                isLength(definition.displayVecX, ZERO_DEFAULT_LENGTH_BOUNDS);

                annotation { "Name" : "Vec Y", "UIHint" : UIHint.READ_ONLY }
                isLength(definition.displayVecY, ZERO_DEFAULT_LENGTH_BOUNDS);

                annotation { "Name" : "Vec Z", "UIHint" : UIHint.READ_ONLY }
                isLength(definition.displayVecZ, ZERO_DEFAULT_LENGTH_BOUNDS);
            }
        }

        // --- Euler Angles group (VECTOR, both MC, showEuler) ---
        if (definition.measurementType == BMMeasurementType.VECTOR &&
            definition.entity1Type == BMEntityType.MATE_CONNECTOR &&
            definition.entity2Type == BMEntityType.MATE_CONNECTOR &&
            definition.showEuler)
        {
            annotation { "Group Name" : "Euler Angles (ZYX)", "Collapsed By Default" : true }
            {
                annotation { "Name" : "Euler X", "UIHint" : UIHint.READ_ONLY }
                isAngle(definition.displayEulerX, ANGLE_360_ZERO_DEFAULT_BOUNDS);

                annotation { "Name" : "Euler Y", "UIHint" : UIHint.READ_ONLY }
                isAngle(definition.displayEulerY, ANGLE_360_ZERO_DEFAULT_BOUNDS);

                annotation { "Name" : "Euler Z", "UIHint" : UIHint.READ_ONLY }
                isAngle(definition.displayEulerZ, ANGLE_360_ZERO_DEFAULT_BOUNDS);
            }
        }

        // --- Create Variable group ---
        annotation { "Group Name" : "Create Variable", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Save result" }
            definition.saveMainVar is boolean;

            if (definition.saveMainVar)
            {
                annotation { "Name" : "Variable name", "UIHint" : [UIHint.VARIABLE_NAME, UIHint.SHOW_LABEL],
                             "MaxLength" : 10000 }
                definition.mainVarName is string;
            }

            if (definition.measurementType == BMMeasurementType.DISTANCE)
            {
                annotation { "Name" : "Save delta X" }
                definition.saveDeltaX is boolean;

                if (definition.saveDeltaX)
                {
                    annotation { "Name" : "Delta X variable", "UIHint" : [UIHint.VARIABLE_NAME, UIHint.SHOW_LABEL],
                                 "MaxLength" : 10000 }
                    definition.deltaXVarName is string;
                }

                annotation { "Name" : "Save delta Y" }
                definition.saveDeltaY is boolean;

                if (definition.saveDeltaY)
                {
                    annotation { "Name" : "Delta Y variable", "UIHint" : [UIHint.VARIABLE_NAME, UIHint.SHOW_LABEL],
                                 "MaxLength" : 10000 }
                    definition.deltaYVarName is string;
                }

                annotation { "Name" : "Save delta Z" }
                definition.saveDeltaZ is boolean;

                if (definition.saveDeltaZ)
                {
                    annotation { "Name" : "Delta Z variable", "UIHint" : [UIHint.VARIABLE_NAME, UIHint.SHOW_LABEL],
                                 "MaxLength" : 10000 }
                    definition.deltaZVarName is string;
                }

                if (definition.useAlong)
                {
                    annotation { "Name" : "Save along distance" }
                    definition.saveAlongVar is boolean;

                    if (definition.saveAlongVar)
                    {
                        annotation { "Name" : "Along variable", "UIHint" : [UIHint.VARIABLE_NAME, UIHint.SHOW_LABEL],
                                     "MaxLength" : 10000 }
                        definition.alongVarName is string;
                    }
                }
            }
        }

        // --- Debug group ---
        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Show debug" }
            definition.showDebug is boolean;

            if (definition.showDebug)
            {
                annotation { "Name" : "Show points", "UIHint" : UIHint.DISPLAY_SHORT }
                definition.debugShowPoints is boolean;

                annotation { "Name" : "Show vectors", "UIHint" : UIHint.DISPLAY_SHORT }
                definition.debugShowVectors is boolean;

                annotation { "Name" : "Print values", "UIHint" : UIHint.DISPLAY_SHORT }
                definition.debugPrintValues is boolean;
            }
        }
    }
    {
        // --- Guard: entity1 required ---
        if (isQueryEmpty(context, definition.entity1))
        {
            throw regenError("Select at least one entity.", ["entity1"]);
        }

        // --- Coordinate frame ---
        var cSys = resolveCoordFrame(context, definition.coordSystem, definition.csQuery);

        // --- Point resolution (two-pass for NEAREST_* accuracy) ---
        var p1 = undefined;
        var p2 = undefined;

        var e1 = definition.entity1;
        var t1 = definition.entity1Type;
        var sr1 = definition.entity1SolidRef;

        var e2 = definition.entity2;
        var t2 = definition.entity2Type;
        var sr2 = definition.entity2SolidRef;

        if (definition.measurementType == BMMeasurementType.LENGTH)
        {
            // LENGTH only needs entity1
            p1 = try silent(resolveEntityPoint(context, e1, t1, sr1, undefined));
            p2 = p1;
        }
        else
        {
            // Rough pass
            var p1Rough = try silent(resolveEntityPoint(context, e1, t1, sr1, undefined));
            var p2Rough = try silent(resolveEntityPoint(context, e2, t2, sr2, p1Rough));
            // Refined pass
            p1 = try silent(resolveEntityPoint(context, e1, t1, sr1, p2Rough));
            p2 = try silent(resolveEntityPoint(context, e2, t2, sr2, p1));
        }

        // Fallback if resolution failed
        if (p1 == undefined)
        {
            p1 = vector(0, 0, 0) * meter;
        }
        if (p2 == undefined)
        {
            p2 = vector(0, 0, 0) * meter;
        }

        // --- Dispatch to measurement type ---
        if (definition.measurementType == BMMeasurementType.DISTANCE)
        {
            computeDistanceMeasurements(context, id, definition, cSys, p1, p2);
        }
        else if (definition.measurementType == BMMeasurementType.VECTOR)
        {
            computeVectorMeasurements(context, id, definition, p1, p2);
        }
        else if (definition.measurementType == BMMeasurementType.ANGLE)
        {
            computeAngleMeasurement(context, id, definition);
        }
        else if (definition.measurementType == BMMeasurementType.LENGTH)
        {
            computeLengthMeasurement(context, id, definition);
        }

        // --- Highlight entities ---
        setHighlightedEntities(context, { "entities" : definition.entity1 });

        // --- Debug visualization ---
        if (definition.showDebug)
        {
            if (definition.debugShowPoints)
            {
                addDebugPoint(context, p1, DebugColor.GREEN);
                if (definition.measurementType != BMMeasurementType.LENGTH)
                {
                    addDebugPoint(context, p2, DebugColor.BLUE);
                }
            }
            if (definition.debugShowVectors && definition.measurementType != BMMeasurementType.LENGTH)
            {
                addDebugLine(context, p1, p2, DebugColor.MAGENTA);
            }
            if (definition.debugPrintValues)
            {
                debug(context, cSys);
            }
        }
    },
    // --- Defaults ---
    {
        "measurementType" : BMMeasurementType.DISTANCE,
        "coordSystem"     : BMCoordSystem.WORLD,
        "csQuery"         : qNothing(),

        "entity1"         : qNothing(),
        "entity1Type"     : BMEntityType.NONE,
        "entity1SolidRef" : BMSolidRef.COM,
        "entity1MCAxis"   : BMMCAxis.Z_AXIS,

        "entity2"         : qNothing(),
        "entity2Type"     : BMEntityType.NONE,
        "entity2SolidRef" : BMSolidRef.COM,
        "entity2MCAxis"   : BMMCAxis.Z_AXIS,

        "useAlong"            : false,
        "alongQuery"          : qNothing(),
        "keepMeasurementWire" : false,

        "displayDistance"      : 0 * meter,
        "displayDeltaX"        : 0 * meter,
        "displayDeltaY"        : 0 * meter,
        "displayDeltaZ"        : 0 * meter,
        "displayDistanceAlong" : 0 * meter,
        "displayVecX"          : 0 * meter,
        "displayVecY"          : 0 * meter,
        "displayVecZ"          : 0 * meter,
        "displayAngle"         : 0 * radian,
        "displayEulerX"        : 0 * radian,
        "displayEulerY"        : 0 * radian,
        "displayEulerZ"        : 0 * radian,
        "displayLength"        : 0 * meter,

        "flipVector"       : false,
        "normalizeVector"  : false,
        "stripVectorUnits" : false,
        "showEuler"        : false,

        "saveMainVar"  : false,
        "mainVarName"  : "",
        "saveDeltaX"   : false,
        "deltaXVarName" : "",
        "saveDeltaY"   : false,
        "deltaYVarName" : "",
        "saveDeltaZ"   : false,
        "deltaZVarName" : "",
        "saveAlongVar" : false,
        "alongVarName" : "",

        "showDebug"        : false,
        "debugShowPoints"  : true,
        "debugShowVectors" : true,
        "debugPrintValues" : false
    });

// ---------------------------------------------------------------------------
// Internal compute functions (not exported)
// ---------------------------------------------------------------------------

function computeDistanceMeasurements(context is Context, id is Id, definition is map,
    cSys is CoordSystem, p1 is Vector, p2 is Vector)
{
    var delta = p2 - p1;
    var dist = norm(delta);

    // fromWorld decomposes the delta into cSys frame components cleanly
    var deltaInFrame = fromWorld(cSys, p2) - fromWorld(cSys, p1);
    var dX = abs(deltaInFrame[0]);
    var dY = abs(deltaInFrame[1]);
    var dZ = abs(deltaInFrame[2]);

    setFeatureComputedParameter(context, id, { "name" : "displayDistance", "value" : dist });
    setFeatureComputedParameter(context, id, { "name" : "displayDeltaX",   "value" : dX });
    setFeatureComputedParameter(context, id, { "name" : "displayDeltaY",   "value" : dY });
    setFeatureComputedParameter(context, id, { "name" : "displayDeltaZ",   "value" : dZ });

    // Along measurement
    var distAlong = 0 * meter;
    if (definition.useAlong && !isQueryEmpty(context, definition.alongQuery))
    {
        warnIfOffEntity(context, id, definition.alongQuery, p1, p2);

        var alongEdgeQ = qEntityFilter(definition.alongQuery, EntityType.EDGE);
        var alongFaceQ = qEntityFilter(definition.alongQuery, EntityType.FACE);

        if (!isQueryEmpty(context, alongEdgeQ))
        {
            distAlong = try silent(measureAlongEdge(context, alongEdgeQ, p1, p2));
            if (distAlong == undefined) { distAlong = 0 * meter; }
        }
        else if (!isQueryEmpty(context, alongFaceQ))
        {
            distAlong = try silent(measureAlongFace(context, id, alongFaceQ, p1, p2, definition.keepMeasurementWire));
            if (distAlong == undefined) { distAlong = 0 * meter; }
        }

        setFeatureComputedParameter(context, id, { "name" : "displayDistanceAlong", "value" : distAlong });
    }

    publishIfEnabled(context, id, definition.saveMainVar,  definition.mainVarName,  "mainVarName",  dist);
    publishIfEnabled(context, id, definition.saveDeltaX,   definition.deltaXVarName, "deltaXVarName", dX);
    publishIfEnabled(context, id, definition.saveDeltaY,   definition.deltaYVarName, "deltaYVarName", dY);
    publishIfEnabled(context, id, definition.saveDeltaZ,   definition.deltaZVarName, "deltaZVarName", dZ);
    if (definition.useAlong)
    {
        publishIfEnabled(context, id, definition.saveAlongVar, definition.alongVarName, "alongVarName", distAlong);
    }
}

function computeVectorMeasurements(context is Context, id is Id, definition is map,
    p1 is Vector, p2 is Vector)
{
    var delta = p2 - p1;
    if (definition.flipVector)
    {
        delta = -delta;
    }

    var dist = norm(delta);
    setFeatureComputedParameter(context, id, { "name" : "displayDistance", "value" : dist });
    setFeatureComputedParameter(context, id, { "name" : "displayVecX", "value" : delta[0] });
    setFeatureComputedParameter(context, id, { "name" : "displayVecY", "value" : delta[1] });
    setFeatureComputedParameter(context, id, { "name" : "displayVecZ", "value" : delta[2] });

    // Angle between entity directions
    var angle = try silent(measureAngleBetweenEntities(context,
        definition.entity1, definition.entity1Type,
        definition.entity2, definition.entity2Type,
        definition.entity1MCAxis, definition.entity2MCAxis));
    if (angle != undefined)
    {
        setFeatureComputedParameter(context, id, { "name" : "displayAngle", "value" : angle });
    }

    // Euler angles
    if (definition.entity1Type == BMEntityType.MATE_CONNECTOR &&
        definition.entity2Type == BMEntityType.MATE_CONNECTOR &&
        definition.showEuler)
    {
        var euler = try silent(computeRelativeEuler(context, definition.entity1, definition.entity2));
        if (euler != undefined)
        {
            setFeatureComputedParameter(context, id, { "name" : "displayEulerX", "value" : euler[0] });
            setFeatureComputedParameter(context, id, { "name" : "displayEulerY", "value" : euler[1] });
            setFeatureComputedParameter(context, id, { "name" : "displayEulerZ", "value" : euler[2] });
        }
    }

    // Build publish value: normalized unitless, stripped (array), or raw vector
    var publishVal = delta;
    if (definition.normalizeVector)
    {
        if (dist.value > 0)
        {
            publishVal = delta / dist;
        }
    }
    else if (definition.stripVectorUnits)
    {
        publishVal = [delta[0] / meter, delta[1] / meter, delta[2] / meter];
    }

    publishIfEnabled(context, id, definition.saveMainVar, definition.mainVarName, "mainVarName", publishVal);
}

function computeAngleMeasurement(context is Context, id is Id, definition is map)
{
    if (isQueryEmpty(context, definition.entity2))
    {
        reportFeatureWarning(context, id, "Select Entity 2 to measure an angle.");
        return;
    }

    var angle = try silent(measureAngleBetweenEntities(context,
        definition.entity1, definition.entity1Type,
        definition.entity2, definition.entity2Type,
        definition.entity1MCAxis, definition.entity2MCAxis));

    if (angle == undefined)
    {
        reportFeatureWarning(context, id, "Cannot determine direction for one or both entities.");
        return;
    }

    setFeatureComputedParameter(context, id, { "name" : "displayAngle", "value" : angle });
    publishIfEnabled(context, id, definition.saveMainVar, definition.mainVarName, "mainVarName", angle);
}

function computeLengthMeasurement(context is Context, id is Id, definition is map)
{
    var len = try(evLength(context, { "entities" : definition.entity1 }));
    if (len == undefined)
    {
        reportFeatureWarning(context, id, "Cannot measure length of selected entity.");
        return;
    }
    setFeatureComputedParameter(context, id, { "name" : "displayLength", "value" : len });
    publishIfEnabled(context, id, definition.saveMainVar, definition.mainVarName, "mainVarName", len);
}
