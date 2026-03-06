FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// IMPORT: betterMeasureUtils.fs

/**
 * betterMeasure — Extended measurement and variable tool.
 *
 * Extends the built-in Variable feature with richer measurement types,
 * coordinate-system-aware axis decomposition, along-entity distance,
 * vector/angle/Euler outputs, and inline variable creation for any result.
 *
 * ── Measurement types (HORIZONTAL_ENUM) ─────────────────────────────────────
 *   DISTANCE  Straight-line distance between two entity reference points.
 *             Axis deltas (ΔX/ΔY/ΔZ) projected into the selected coord frame.
 *             Optional "along" measurement: arc length along a selected edge
 *             or approximated geodesic path along a selected face.
 *
 *   VECTOR    Delta vector (p2 - p1). Options: flip, normalize, strip units.
 *             Angle between entities shown when extractable.
 *             Euler angles (ZYX) shown when both entities are mate connectors.
 *
 *   ANGLE     Unsigned angle between the extractable directions of two entities.
 *             Works on: edges, wires (tangent), sheet faces (normal), MC (axis).
 *             Result clamped to [0, π/2] — smallest angle between lines.
 *
 *   LENGTH    Total arc length of all edges / wire edges in entity1.
 *             Supports multi-edge selection (sum of lengths).
 *
 * ── Coordinate system ───────────────────────────────────────────────────────
 *   WORLD            World X/Y/Z axes for axis-delta decomposition.
 *   MATE_CONNECTOR   Custom frame from a selected mate connector.
 *
 * ── Entity sub-selection ────────────────────────────────────────────────────
 *   SOLID body → COM | NEAREST_FACE | NEAREST_EDGE | NEAREST_VERTEX
 *   SHEET body → COA | NEAREST_EDGE | NEAREST_VERTEX
 *   Wire body  → centroid (no sub-selection)
 *   Edge       → closest point to the other entity (no sub-selection)
 *   Vertex     → exact vertex point
 *   Mate conn. → origin; axis selectable for ANGLE / VECTOR measurement
 *
 * ── Variable creation ───────────────────────────────────────────────────────
 *   Each displayed measurement result has a (save toggle + VARIABLE_NAME)
 *   pair in the "Create Variable" group. Enabling and naming creates a context
 *   variable accessible in downstream features via #varName.
 *
 * ── Display (READ_ONLY fields) ──────────────────────────────────────────────
 *   All numeric results are pushed into READ_ONLY precondition fields via
 *   setFeatureComputedParameter so values update live during dialog editing.
 *
 * ── Debug ───────────────────────────────────────────────────────────────────
 *   Show measurement points (green = p1, blue = p2), distance line (magenta),
 *   and coord frame arrows. Print raw values to console.
 */

// ══════════════════════════════════════════════════════════════════════════════
// ENUMS  (entity enums are in betterMeasureUtils.fs)
// ══════════════════════════════════════════════════════════════════════════════

/** Measurement mode — rendered as a horizontal button strip at dialog top. */
export enum BMMeasurementType
{
    annotation { "Name" : "Distance" } DISTANCE,
    annotation { "Name" : "Vector" }   VECTOR,
    annotation { "Name" : "Angle" }    ANGLE,
    annotation { "Name" : "Length" }   LENGTH
}

/** Coordinate system for axis-delta decomposition of distances and vectors. */
export enum BMCoordSystem
{
    annotation { "Name" : "World" }          WORLD,
    annotation { "Name" : "Mate connector" } MATE_CONNECTOR
}

// ══════════════════════════════════════════════════════════════════════════════
// EDITING LOGIC
// ══════════════════════════════════════════════════════════════════════════════

/**
 * betterMeasureEditingLogic
 *
 * Runs on every dialog change. Responsibilities:
 *   1. Detect entity category for entity1/entity2 → update hidden *Type fields.
 *      These drive sub-selection enum visibility in the precondition.
 *   2. (entity2 visibility is managed implicitly by the precondition's
 *      `if (measurementType != LENGTH)` guard — no extra logic needed.)
 *
 * @param context             {Context}
 * @param id                  {Id}
 * @param oldDefinition       {map}
 * @param definition          {map}     — mutated in-place and returned
 * @param isCreating          {boolean}
 * @param specifiedParameters {map}     — flags which params were just changed
 * @param hiddenBodies        {Query}
 * @param clickedButton       {string}
 * @returns {map}
 *
 * Ref: betterMeasureUtils.fs — getEntityBodyType
 */
export function betterMeasureEditingLogic(context is Context, id is Id,
    oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map,
    hiddenBodies is Query, clickedButton is string) returns map
{
    // Update entity1 category when its selection changes
    if (specifiedParameters.entity1 == true)
    {
        definition.entity1Type = getEntityBodyType(context, definition.entity1);
    }

    // Update entity2 category when its selection changes
    if (specifiedParameters.entity2 == true)
    {
        definition.entity2Type = getEntityBodyType(context, definition.entity2);
    }

    return definition;
}

// ══════════════════════════════════════════════════════════════════════════════
// FEATURE
// ══════════════════════════════════════════════════════════════════════════════

annotation { "Feature Type Name" : "Better Measure",
             "Feature Type Description" : "Extended measurement and variable creation tool",
             "Feature Name Template" : "Better Measure",
             "UIHint" : UIHint.NO_PREVIEW_PROVIDED,
             "Editing Logic Function" : "betterMeasureEditingLogic" }
export const betterMeasure = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        // ── Measurement type ──────────────────────────────────────────────────
        annotation { "Name" : "Measurement", "UIHint" : UIHint.HORIZONTAL_ENUM }
        definition.measurementType is BMMeasurementType;

        // ── Coordinate system ─────────────────────────────────────────────────
        annotation { "Name" : "Coordinate system", "UIHint" : UIHint.SHOW_LABEL }
        definition.coordSystem is BMCoordSystem;

        if (definition.coordSystem == BMCoordSystem.MATE_CONNECTOR)
        {
            annotation { "Name" : "Coordinate frame",
                         "Filter" : BodyType.MATE_CONNECTOR,
                         "MaxNumberOfPicks" : 1,
                         "UIHint" : UIHint.PREVENT_CREATING_NEW_MATE_CONNECTORS }
            definition.csQuery is Query;
        }

        // ── Element 1 ─────────────────────────────────────────────────────────
        annotation { "Name" : "Element 1",
                     "Filter" : BodyType.SOLID || BodyType.SHEET || BodyType.WIRE
                              || BodyType.MATE_CONNECTOR
                              || EntityType.EDGE || EntityType.VERTEX,
                     "MaxNumberOfPicks" : 1 }
        definition.entity1 is Query;

        // Hidden: entity category set by editing logic, controls sub-selection visibility
        annotation { "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.entity1Type is BMEntityType;

        if (definition.entity1Type == BMEntityType.SOLID)
        {
            annotation { "Name" : "Element 1 reference", "UIHint" : UIHint.SHOW_LABEL }
            definition.entity1SolidRef is BMSolidRef;
        }

        if (definition.entity1Type == BMEntityType.SHEET)
        {
            annotation { "Name" : "Element 1 reference", "UIHint" : UIHint.SHOW_LABEL }
            definition.entity1SheetRef is BMSheetRef;
        }

        // MC axis selection: only for ANGLE and VECTOR (where axis direction matters)
        if (definition.entity1Type == BMEntityType.MATE_CONNECTOR &&
            (definition.measurementType == BMMeasurementType.ANGLE ||
             definition.measurementType == BMMeasurementType.VECTOR))
        {
            annotation { "Name" : "Element 1 axis",
                         "UIHint" : UIHint.MATE_CONNECTOR_AXIS_TYPE,
                         "Default" : MateConnectorAxisType.PLUS_Z }
            definition.entity1MCAxis is MateConnectorAxisType;
        }

        // ── Element 2 (hidden for LENGTH — no second entity needed) ───────────
        if (definition.measurementType != BMMeasurementType.LENGTH)
        {
            annotation { "Name" : "Element 2",
                         "Filter" : BodyType.SOLID || BodyType.SHEET || BodyType.WIRE
                                  || BodyType.MATE_CONNECTOR
                                  || EntityType.EDGE || EntityType.VERTEX,
                         "MaxNumberOfPicks" : 1 }
            definition.entity2 is Query;

            annotation { "UIHint" : UIHint.ALWAYS_HIDDEN }
            definition.entity2Type is BMEntityType;

            if (definition.entity2Type == BMEntityType.SOLID)
            {
                annotation { "Name" : "Element 2 reference", "UIHint" : UIHint.SHOW_LABEL }
                definition.entity2SolidRef is BMSolidRef;
            }

            if (definition.entity2Type == BMEntityType.SHEET)
            {
                annotation { "Name" : "Element 2 reference", "UIHint" : UIHint.SHOW_LABEL }
                definition.entity2SheetRef is BMSheetRef;
            }

            if (definition.entity2Type == BMEntityType.MATE_CONNECTOR &&
                (definition.measurementType == BMMeasurementType.ANGLE ||
                 definition.measurementType == BMMeasurementType.VECTOR))
            {
                annotation { "Name" : "Element 2 axis",
                             "UIHint" : UIHint.MATE_CONNECTOR_AXIS_TYPE,
                             "Default" : MateConnectorAxisType.PLUS_Z }
                definition.entity2MCAxis is MateConnectorAxisType;
            }
        }

        // ── Along options (DISTANCE only) ─────────────────────────────────────
        if (definition.measurementType == BMMeasurementType.DISTANCE)
        {
            annotation { "Name" : "Measure along entity" }
            definition.useAlong is boolean;

            if (definition.useAlong)
            {
                annotation { "Name" : "Along",
                             "Filter" : EntityType.EDGE || EntityType.FACE,
                             "MaxNumberOfPicks" : 1 }
                definition.alongQuery is Query;

                // keepMeasurementWire only makes sense when the along target is a face
                // (face path creates a spline wire; edge along has no geometry to keep)
                annotation { "Name" : "Keep measurement wire",
                             "UIHint" : UIHint.DISPLAY_SHORT }
                definition.keepMeasurementWire is boolean;
            }
        }

        // ══ Measurements display group ════════════════════════════════════════
        // All fields here are READ_ONLY. Values are pushed by setFeatureComputedParameter
        // during feature execution so they update live while the dialog is open.
        // Ref: feature.fs — setFeatureComputedParameter; uihint.gen.fs — UIHint.READ_ONLY
        annotation { "Group Name" : "Measurements", "Collapsed By Default" : false }
        {
            // Distance scalar — shown for DISTANCE and VECTOR
            if (definition.measurementType == BMMeasurementType.DISTANCE ||
                definition.measurementType == BMMeasurementType.VECTOR)
            {
                annotation { "Name" : "Distance", "UIHint" : UIHint.READ_ONLY }
                isLength(definition.displayDistance, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);
            }

            // Angle — shown for ANGLE and VECTOR (when entity directions exist)
            if (definition.measurementType == BMMeasurementType.ANGLE ||
                definition.measurementType == BMMeasurementType.VECTOR)
            {
                annotation { "Name" : "Angle", "UIHint" : UIHint.READ_ONLY }
                isAngle(definition.displayAngle, ANGLE_360_ZERO_DEFAULT_BOUNDS);
            }

            // Length — shown for LENGTH
            if (definition.measurementType == BMMeasurementType.LENGTH)
            {
                annotation { "Name" : "Length", "UIHint" : UIHint.READ_ONLY }
                isLength(definition.displayLength, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);
            }

            // VECTOR options: flip, normalize, strip units, Euler toggle
            if (definition.measurementType == BMMeasurementType.VECTOR)
            {
                annotation { "Name" : "Flip direction",
                             "UIHint" : [UIHint.OPPOSITE_DIRECTION, UIHint.DISPLAY_SHORT] }
                definition.flipVector is boolean;

                annotation { "Name" : "Normalize", "UIHint" : UIHint.DISPLAY_SHORT }
                definition.normalizeVector is boolean;

                // Strip units: published variable will be a unitless number array
                // (display always shows length components; stripping only affects setVariable)
                annotation { "Name" : "Strip units from variable",
                             "UIHint" : UIHint.DISPLAY_SHORT }
                definition.stripVectorUnits is boolean;

                // Euler toggle only appears when both entities are mate connectors
                if (definition.entity1Type == BMEntityType.MATE_CONNECTOR &&
                    definition.entity2Type == BMEntityType.MATE_CONNECTOR)
                {
                    annotation { "Name" : "Show Euler angles" }
                    definition.showEuler is boolean;
                }
            }
        }

        // ══ Axis Deltas group (DISTANCE only) ════════════════════════════════
        // Separated from Measurements so it can be collapsed independently.
        // Values are signed projections of (p2 - p1) onto the coord frame axes.
        if (definition.measurementType == BMMeasurementType.DISTANCE)
        {
            annotation { "Group Name" : "Axis Deltas", "Collapsed By Default" : true }
            {
                annotation { "Name" : "ΔX", "UIHint" : UIHint.READ_ONLY }
                isLength(definition.displayDeltaX, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);

                annotation { "Name" : "ΔY", "UIHint" : UIHint.READ_ONLY }
                isLength(definition.displayDeltaY, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);

                annotation { "Name" : "ΔZ", "UIHint" : UIHint.READ_ONLY }
                isLength(definition.displayDeltaZ, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);

                if (definition.useAlong)
                {
                    annotation { "Name" : "Distance along", "UIHint" : UIHint.READ_ONLY }
                    isLength(definition.displayDistanceAlong, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);
                }
            }
        }

        // ══ Delta Vector group (VECTOR only) ══════════════════════════════════
        // Components of (p2 - p1), optionally normalized. Signed (can be negative).
        // Display uses isLength; "strip units" only affects the published variable.
        if (definition.measurementType == BMMeasurementType.VECTOR)
        {
            annotation { "Group Name" : "Delta Vector", "Collapsed By Default" : false }
            {
                annotation { "Name" : "X", "UIHint" : UIHint.READ_ONLY }
                isLength(definition.displayVecX, ZERO_DEFAULT_LENGTH_BOUNDS);

                annotation { "Name" : "Y", "UIHint" : UIHint.READ_ONLY }
                isLength(definition.displayVecY, ZERO_DEFAULT_LENGTH_BOUNDS);

                annotation { "Name" : "Z", "UIHint" : UIHint.READ_ONLY }
                isLength(definition.displayVecZ, ZERO_DEFAULT_LENGTH_BOUNDS);
            }
        }

        // ══ Euler Angles group (VECTOR + both MC + showEuler) ════════════════
        if (definition.measurementType == BMMeasurementType.VECTOR &&
            definition.entity1Type == BMEntityType.MATE_CONNECTOR &&
            definition.entity2Type == BMEntityType.MATE_CONNECTOR &&
            definition.showEuler)
        {
            annotation { "Group Name" : "Euler Angles (ZYX)", "Collapsed By Default" : false }
            {
                annotation { "Name" : "Rx", "UIHint" : UIHint.READ_ONLY }
                isAngle(definition.displayEulerX, ANGLE_360_ZERO_DEFAULT_BOUNDS);

                annotation { "Name" : "Ry", "UIHint" : UIHint.READ_ONLY }
                isAngle(definition.displayEulerY, ANGLE_360_ZERO_DEFAULT_BOUNDS);

                annotation { "Name" : "Rz", "UIHint" : UIHint.READ_ONLY }
                isAngle(definition.displayEulerZ, ANGLE_360_ZERO_DEFAULT_BOUNDS);
            }
        }

        // ══ Create Variable group ═════════════════════════════════════════════
        // Each visible measurement result has a (save boolean + VARIABLE_NAME string) pair.
        // UIHint.VARIABLE_NAME renders the string field with Onshape's variable-name styling.
        // In the feature body, publishIfEnabled() calls verifyVariableName + setVariable.
        // Ref: variable.fs:156 — UIHint.VARIABLE_NAME pattern; variable.fs:825 — publishVariableValue
        annotation { "Group Name" : "Create Variable", "Collapsed By Default" : true }
        {
            // Main result: distance / angle / length depending on measurement type
            annotation { "Name" : "Save result", "UIHint" : UIHint.DISPLAY_SHORT }
            definition.saveMainVar is boolean;

            if (definition.saveMainVar)
            {
                annotation { "Name" : "Variable name",
                             "UIHint" : UIHint.VARIABLE_NAME,
                             "MaxLength" : 10000 }
                definition.mainVarName is string;
            }

            // Axis delta variables — DISTANCE mode only
            if (definition.measurementType == BMMeasurementType.DISTANCE)
            {
                annotation { "Name" : "Save ΔX", "UIHint" : UIHint.DISPLAY_SHORT }
                definition.saveDeltaX is boolean;

                if (definition.saveDeltaX)
                {
                    annotation { "Name" : "ΔX name",
                                 "UIHint" : UIHint.VARIABLE_NAME,
                                 "MaxLength" : 10000 }
                    definition.deltaXVarName is string;
                }

                annotation { "Name" : "Save ΔY", "UIHint" : UIHint.DISPLAY_SHORT }
                definition.saveDeltaY is boolean;

                if (definition.saveDeltaY)
                {
                    annotation { "Name" : "ΔY name",
                                 "UIHint" : UIHint.VARIABLE_NAME,
                                 "MaxLength" : 10000 }
                    definition.deltaYVarName is string;
                }

                annotation { "Name" : "Save ΔZ", "UIHint" : UIHint.DISPLAY_SHORT }
                definition.saveDeltaZ is boolean;

                if (definition.saveDeltaZ)
                {
                    annotation { "Name" : "ΔZ name",
                                 "UIHint" : UIHint.VARIABLE_NAME,
                                 "MaxLength" : 10000 }
                    definition.deltaZVarName is string;
                }

                if (definition.useAlong)
                {
                    annotation { "Name" : "Save along dist", "UIHint" : UIHint.DISPLAY_SHORT }
                    definition.saveAlongVar is boolean;

                    if (definition.saveAlongVar)
                    {
                        annotation { "Name" : "Along name",
                                     "UIHint" : UIHint.VARIABLE_NAME,
                                     "MaxLength" : 10000 }
                        definition.alongVarName is string;
                    }
                }
            }
        }

        // ══ Debug group ═══════════════════════════════════════════════════════
        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Enable debug display" }
            definition.showDebug is boolean;

            if (definition.showDebug)
            {
                // Show measurement points as colored debug geometry (edit-mode only)
                annotation { "Name" : "Show points", "UIHint" : UIHint.DISPLAY_SHORT }
                definition.debugShowPoints is boolean;

                // Show distance line and coordinate frame arrows
                annotation { "Name" : "Show vectors", "UIHint" : UIHint.DISPLAY_SHORT }
                definition.debugShowVectors is boolean;

                // Print raw values to the FeatureScript console
                annotation { "Name" : "Print values", "UIHint" : UIHint.DISPLAY_SHORT }
                definition.debugPrintValues is boolean;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // FEATURE BODY
    // ══════════════════════════════════════════════════════════════════════════
    {
        // ── Guard ─────────────────────────────────────────────────────────────
        if (isQueryEmpty(context, definition.entity1))
        {
            throw regenError("Select at least one element.", ["entity1"]);
        }

        // ── 1. Coordinate frame ───────────────────────────────────────────────
        // Ref: betterMeasureUtils.fs — resolveCoordFrame
        //      coordSystem.fs — WORLD_COORD_SYSTEM, evMateConnector
        var cSys = resolveCoordFrame(context, definition.coordSystem, definition.csQuery);

        // ── 2. Resolve measurement points (two-pass for NEAREST_* accuracy) ───
        // Pass 1: rough centroid for both (ignores otherPoint)
        // Pass 2: re-resolve using the opposing rough point as the "from" side
        // Ref: betterMeasureUtils.fs — resolveEntityPoint
        var p1 = resolveEntityPoint(context, definition.entity1, definition.entity1Type,
            definition.entity1SolidRef, definition.entity1SheetRef, undefined);

        var p2 = p1; // fallback when entity2 is not used (LENGTH mode)
        var hasEntity2 = (definition.measurementType != BMMeasurementType.LENGTH) &&
                         !isQueryEmpty(context, definition.entity2);

        if (hasEntity2)
        {
            // First rough p2, then re-resolve p1, then final p2
            p2 = resolveEntityPoint(context, definition.entity2, definition.entity2Type,
                definition.entity2SolidRef, definition.entity2SheetRef, p1);
            p1 = resolveEntityPoint(context, definition.entity1, definition.entity1Type,
                definition.entity1SolidRef, definition.entity1SheetRef, p2);
            p2 = resolveEntityPoint(context, definition.entity2, definition.entity2Type,
                definition.entity2SolidRef, definition.entity2SheetRef, p1);
        }

        // ── 3. Compute measurements ───────────────────────────────────────────
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

        // ── 4. Highlight selections ───────────────────────────────────────────
        // Ref: feature.fs — setHighlightedEntities
        var highlightQ = definition.entity1;
        if (hasEntity2)
        {
            highlightQ = qUnion([definition.entity1, definition.entity2]);
        }
        setHighlightedEntities(context, { "entities" : highlightQ });

        // ── 5. Debug visualization ────────────────────────────────────────────
        // All debug entities are ephemeral (visible only while editing this feature).
        // Ref: debug.fs — addDebugPoint, addDebugLine, debug(context, CoordSystem)
        if (definition.showDebug)
        {
            if (definition.debugShowPoints)
            {
                addDebugPoint(context, p1, DebugColor.GREEN);
                if (hasEntity2)
                {
                    addDebugPoint(context, p2, DebugColor.BLUE);
                }
            }

            if (definition.debugShowVectors)
            {
                if (hasEntity2)
                {
                    // Magenta line between measurement points
                    addDebugLine(context, p1, p2, DebugColor.MAGENTA);
                }
                // Coordinate frame: three arrows from origin
                // Ref: debug.fs — debug(context, CoordSystem, color) shows RGB axis arrows
                debug(context, cSys);
            }

            if (definition.debugPrintValues)
            {
                println("betterMeasure: p1 = " ~ p1);
                if (hasEntity2)
                {
                    println("betterMeasure: p2 = " ~ p2);
                    println("betterMeasure: |p2-p1| = " ~ norm(p2 - p1));
                }
            }
        }
    },
    {
        // ── Default values ────────────────────────────────────────────────────
        // Note: definition fields that are only shown conditionally must still
        // have defaults so the precondition type-checks pass on first open.

        "measurementType"     : BMMeasurementType.DISTANCE,
        "coordSystem"         : BMCoordSystem.WORLD,
        "csQuery"             : qNothing(),

        "entity1"             : qNothing(),
        "entity1Type"         : BMEntityType.NONE,
        "entity1SolidRef"     : BMSolidRef.COM,
        "entity1SheetRef"     : BMSheetRef.COA,
        "entity1MCAxis"       : MateConnectorAxisType.PLUS_Z,

        "entity2"             : qNothing(),
        "entity2Type"         : BMEntityType.NONE,
        "entity2SolidRef"     : BMSolidRef.COM,
        "entity2SheetRef"     : BMSheetRef.COA,
        "entity2MCAxis"       : MateConnectorAxisType.PLUS_Z,

        "useAlong"            : false,
        "alongQuery"          : qNothing(),
        "keepMeasurementWire" : false,

        // READ_ONLY display — all zeroed on first open
        "displayDistance"     : 0 * meter,
        "displayDeltaX"       : 0 * meter,
        "displayDeltaY"       : 0 * meter,
        "displayDeltaZ"       : 0 * meter,
        "displayDistanceAlong": 0 * meter,
        "displayVecX"         : 0 * meter,
        "displayVecY"         : 0 * meter,
        "displayVecZ"         : 0 * meter,
        "displayAngle"        : 0 * radian,
        "displayEulerX"       : 0 * radian,
        "displayEulerY"       : 0 * radian,
        "displayEulerZ"       : 0 * radian,
        "displayLength"       : 0 * meter,

        // VECTOR options
        "flipVector"          : false,
        "normalizeVector"     : false,
        "stripVectorUnits"    : false,
        "showEuler"           : false,

        // Variable creation
        "saveMainVar"         : false,
        "mainVarName"         : "",
        "saveDeltaX"          : false,
        "deltaXVarName"       : "",
        "saveDeltaY"          : false,
        "deltaYVarName"       : "",
        "saveDeltaZ"          : false,
        "deltaZVarName"       : "",
        "saveAlongVar"        : false,
        "alongVarName"        : "",

        // Debug
        "showDebug"           : false,
        "debugShowPoints"     : true,
        "debugShowVectors"    : true,
        "debugPrintValues"    : false
    });

// ══════════════════════════════════════════════════════════════════════════════
// INTERNAL COMPUTATION FUNCTIONS
// ══════════════════════════════════════════════════════════════════════════════

/**
 * computeDistanceMeasurements
 *
 * Handles the DISTANCE measurement type:
 *   - Straight-line distance = norm(p2 - p1)
 *   - Axis deltas: dot(p2-p1, axis) for each axis of cSys (absolute values)
 *   - Along-edge arc length via measureAlongEdge
 *   - Along-face geodesic approximation via measureAlongFace
 *   - Draws a magenta line between measurement points (debug visible only)
 *   - Publishes requested variables via publishIfEnabled
 *
 * @param context    {Context}
 * @param id         {Id}
 * @param definition {map}
 * @param cSys       {CoordSystem}  — axis frame for delta decomposition
 * @param p1         {Vector}       — first measurement point (world space)
 * @param p2         {Vector}       — second measurement point (world space)
 *
 * Ref: coordSystem.fs — yAxis; feature.fs — setFeatureComputedParameter
 *      debug.fs — addDebugLine; betterMeasureUtils.fs — measureAlongEdge,
 *      measureAlongFace, warnIfOffEntity, publishIfEnabled
 */
function computeDistanceMeasurements(context is Context, id is Id, definition is map,
    cSys is CoordSystem, p1 is Vector, p2 is Vector)
{
    // Straight-line distance (always positive)
    var dist = norm(p2 - p1);

    // Axis delta decomposition
    // dot(Vector_with_length_units, unitless_direction) → ValueWithUnits (length)
    // Ref: mathUtils.fs — dot; coordSystem.fs — yAxis
    var delta = p2 - p1;
    var absDX = abs(dot(delta, cSys.xAxis));
    var absDY = abs(dot(delta, yAxis(cSys)));
    var absDZ = abs(dot(delta, cSys.zAxis));

    // Push all results into READ_ONLY display fields
    // Ref: feature.fs:401 — setFeatureComputedParameter
    setFeatureComputedParameter(context, id, { "name" : "displayDistance", "value" : dist });
    setFeatureComputedParameter(context, id, { "name" : "displayDeltaX",   "value" : absDX });
    setFeatureComputedParameter(context, id, { "name" : "displayDeltaY",   "value" : absDY });
    setFeatureComputedParameter(context, id, { "name" : "displayDeltaZ",   "value" : absDZ });

    // Measurement line — only visible while editing this feature
    // Ref: debug.fs:429 — addDebugLine
    try silent
    {
        addDebugLine(context, p1, p2, DebugColor.MAGENTA);
    }

    // Along-entity measurement
    var distAlong = undefined;
    if (definition.useAlong && !isQueryEmpty(context, definition.alongQuery))
    {
        // Warn if measurement points are not on/near the along entity
        // Ref: betterMeasureUtils.fs — warnIfOffEntity
        warnIfOffEntity(context, id, definition.alongQuery, p1, p2);

        var isEdge = !isQueryEmpty(context,
            qEntityFilter(definition.alongQuery, EntityType.EDGE));

        if (isEdge)
        {
            // Arc length along edge between closest parameter positions
            // Ref: betterMeasureUtils.fs — measureAlongEdge
            distAlong = measureAlongEdge(context, definition.alongQuery, p1, p2);
        }
        else
        {
            // UV-space polyline approximation of geodesic on face
            // Ref: betterMeasureUtils.fs — measureAlongFace
            distAlong = measureAlongFace(context, id + "alongFace",
                definition.alongQuery, p1, p2, definition.keepMeasurementWire);
        }

        setFeatureComputedParameter(context, id,
            { "name" : "displayDistanceAlong", "value" : distAlong });
    }

    // Publish context variables
    // Ref: betterMeasureUtils.fs — publishIfEnabled; variable.fs — setVariable
    publishIfEnabled(context, id, definition.saveMainVar,  definition.mainVarName, dist);
    publishIfEnabled(context, id, definition.saveDeltaX,   definition.deltaXVarName, absDX);
    publishIfEnabled(context, id, definition.saveDeltaY,   definition.deltaYVarName, absDY);
    publishIfEnabled(context, id, definition.saveDeltaZ,   definition.deltaZVarName, absDZ);

    if (distAlong != undefined)
    {
        publishIfEnabled(context, id, definition.saveAlongVar,
            definition.alongVarName, distAlong);
    }
}

/**
 * computeVectorMeasurements
 *
 * Handles the VECTOR measurement type:
 *   - Computes delta = p2 - p1, applies flip/normalize options
 *   - Pushes distance scalar and per-component display values
 *   - Computes angle between entity directions (try silent — may be undefined)
 *   - Computes ZYX Euler angles when both entities are mate connectors
 *   - Publishes the main variable as the (possibly transformed) delta vector
 *
 * Note on "Strip units":
 *   The display fields are always isLength (components in length units).
 *   stripVectorUnits only affects the published variable: it stores the delta
 *   as a unitless number array [dx/m, dy/m, dz/m] instead of a length Vector.
 *   If normalizeVector is also active, the result is already unitless, so
 *   stripVectorUnits has no additional effect on the published variable.
 *
 * @param context    {Context}
 * @param id         {Id}
 * @param definition {map}
 * @param p1         {Vector}  — first measurement point
 * @param p2         {Vector}  — second measurement point
 *
 * Ref: feature.fs — setFeatureComputedParameter
 *      betterMeasureUtils.fs — measureAngleBetweenEntities, computeRelativeEuler,
 *                              publishIfEnabled
 */
function computeVectorMeasurements(context is Context, id is Id, definition is map,
    p1 is Vector, p2 is Vector)
{
    var dist = norm(p2 - p1);
    setFeatureComputedParameter(context, id, { "name" : "displayDistance", "value" : dist });

    // Build the display delta and the publish delta separately
    var delta = p2 - p1;
    if (definition.flipVector)
    {
        delta = delta * -1;
    }

    // Display delta: always a length vector for the isLength READ_ONLY fields
    var displayDelta = delta;
    if (definition.normalizeVector && dist > TOLERANCE.zeroLength * meter)
    {
        // normalize() produces a unitless direction; multiply by meter so
        // components fit in isLength fields with magnitude 1m max
        displayDelta = normalize(delta) * meter;
    }

    setFeatureComputedParameter(context, id, { "name" : "displayVecX", "value" : displayDelta[0] });
    setFeatureComputedParameter(context, id, { "name" : "displayVecY", "value" : displayDelta[1] });
    setFeatureComputedParameter(context, id, { "name" : "displayVecZ", "value" : displayDelta[2] });

    // Angle between the two entities (try silent — returns undefined for SOLID/VERTEX)
    // Ref: betterMeasureUtils.fs — measureAngleBetweenEntities
    var angle = try silent(measureAngleBetweenEntities(context,
        definition.entity1, definition.entity1Type,
        definition.entity2, definition.entity2Type,
        definition.entity1MCAxis, definition.entity2MCAxis));

    if (angle != undefined)
    {
        setFeatureComputedParameter(context, id, { "name" : "displayAngle", "value" : angle });
    }

    // Euler angles — both entities must be mate connectors
    // Ref: betterMeasureUtils.fs — computeRelativeEuler
    if (definition.entity1Type == BMEntityType.MATE_CONNECTOR &&
        definition.entity2Type == BMEntityType.MATE_CONNECTOR &&
        definition.showEuler)
    {
        var euler = try silent(computeRelativeEuler(context,
            definition.entity1, definition.entity2));

        if (euler != undefined)
        {
            setFeatureComputedParameter(context, id, { "name" : "displayEulerX", "value" : euler[0] });
            setFeatureComputedParameter(context, id, { "name" : "displayEulerY", "value" : euler[1] });
            setFeatureComputedParameter(context, id, { "name" : "displayEulerZ", "value" : euler[2] });
        }
    }

    // Build the published variable value
    // Normalized: unitless direction vector
    // Strip units (non-normalized): unitless [dx/m, dy/m, dz/m] array
    // Default: the raw length delta Vector
    var publishDelta = delta;
    if (definition.normalizeVector && dist > TOLERANCE.zeroLength * meter)
    {
        publishDelta = normalize(delta); // unitless direction
    }
    else if (definition.stripVectorUnits)
    {
        // Divide each component by meter to get unitless numbers
        publishDelta = [delta[0] / meter, delta[1] / meter, delta[2] / meter];
    }

    publishIfEnabled(context, id, definition.saveMainVar, definition.mainVarName, publishDelta);
}

/**
 * computeAngleMeasurement
 *
 * Handles the ANGLE measurement type. Extracts direction vectors from both
 * entities and computes the unsigned angle between them in [0, π/2].
 *
 * Reports a warning (not a hard error) when:
 *   - entity2 is not selected
 *   - neither entity yields an extractable direction (SOLID, VERTEX, NONE)
 *
 * @param context    {Context}
 * @param id         {Id}
 * @param definition {map}
 *
 * Ref: betterMeasureUtils.fs — measureAngleBetweenEntities, publishIfEnabled
 *      feature.fs — setFeatureComputedParameter
 *      error.fs — reportFeatureWarning
 */
function computeAngleMeasurement(context is Context, id is Id, definition is map)
{
    if (isQueryEmpty(context, definition.entity2))
    {
        reportFeatureWarning(context, id, "Select a second element for angle measurement.");
        return;
    }

    // Ref: betterMeasureUtils.fs — measureAngleBetweenEntities
    var angle = try silent(measureAngleBetweenEntities(context,
        definition.entity1, definition.entity1Type,
        definition.entity2, definition.entity2Type,
        definition.entity1MCAxis, definition.entity2MCAxis));

    if (angle == undefined)
    {
        reportFeatureWarning(context, id,
            "Could not extract a direction from one or both elements. "
            ~ "Select edges, sheet faces, wires, or mate connectors.");
        return;
    }

    setFeatureComputedParameter(context, id, { "name" : "displayAngle", "value" : angle });
    publishIfEnabled(context, id, definition.saveMainVar, definition.mainVarName, angle);
}

/**
 * computeLengthMeasurement
 *
 * Handles the LENGTH measurement type. Computes total arc length of all edges
 * owned by entity1 (supports multi-edge wire selections).
 *
 * @param context    {Context}
 * @param id         {Id}
 * @param definition {map}
 *
 * Ref: evaluate.fs:1072 — evLength; feature.fs — setFeatureComputedParameter
 *      betterMeasureUtils.fs — publishIfEnabled
 */
function computeLengthMeasurement(context is Context, id is Id, definition is map)
{
    // evLength sums length over all edges and wire-owned edges in the query
    var len = evLength(context, { "entities" : definition.entity1 });
    setFeatureComputedParameter(context, id, { "name" : "displayLength", "value" : len });
    publishIfEnabled(context, id, definition.saveMainVar, definition.mainVarName, len);
}
