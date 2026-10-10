FeatureScript 3095;
import(path : "onshape/std/common.fs", version : "3095.0");
import(path : "onshape/std/patternUtils.fs", version : "3095.0");
// IMPORT: Variable_tools V2 extract_outputs.fs (embedStandardOutputs, extractable wrappers)
import(path : "a47f90bfa6b17a59e20cebd0/f4f872fe20d1498201fed64d/3cac74f0bc2b98272db13cd3", version : "b8c80ac05dcfd9f3cc172ffc");
// IMPORT: curve_pattern_spacing.fs (resolveCurveSpacing and the spacing enums)
import(path : "b04cdadd24f6d70a50c76980", version : "c8dcfffecd83eece521b35b8");
// IMPORT: curve_pattern_frames.fs (curveStationFrames, curveStationTransforms, mateConnectorFrame)
import(path : "c587afc527d1699f012e37ee", version : "3f03b5027def997a1aeabe4a");
// IMPORT: curve_pattern_plus_icon.svg (feature icon)
IconNamespace::import(path : "348c5db94758980ab98c9b92", version : "9f3924dd9eac5cd655dde204");

/**
 * Curve pattern plus.
 *
 * Patterns parts, faces or features along a path of edges, or places mate connectors along it. Compared with
 * Onshape's Curve pattern it adds Linear pattern plus style spacing (spacing or total length, count or target
 * spacing with rounding, start and end offsets, centering), orientation control (follow the path with no roll, keep the
 * seed orientation, a reference direction, or a reference face), a mate-connector-only output, and values
 * that Extract variables can read (count, spacing, length, the last instance).
 *
 * Frames are computed analytically (see curve_pattern_frames.fs), not with sweeps. The seed is treated as sitting at
 * the first station: instance i is the seed carried by the transform from the first frame to frame i.
 *
 * The spacing model, the option set, and the last-instance queries are adapted from "Linear pattern plus" by
 * Evan Reese (The Onsherpa, www.TheOnsherpa.com), Copyright (c) 2026 Evan Reese, used with permission under the
 * MIT License (https://opensource.org/licenses/MIT).
 */

export enum CurvePatternOutput
{
    annotation { "Name" : "Pattern" }
    PATTERN,
    annotation { "Name" : "Mate connectors only" }
    MATE_CONNECTORS
}

export enum CurveMateConnectorAxis
{
    annotation { "Name" : "Z along the path" }
    ALONG_PATH,
    annotation { "Name" : "Z along the normal" }
    ALONG_NORMAL
}

annotation { "Feature Type Name" : "Curve pattern plus",
             "Icon" : IconNamespace::BLOB_DATA,
             "Filter Selector" : "allparts" }
export const curvePatternPlus = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Output", "UIHint" : [UIHint.REMEMBER_PREVIOUS_VALUE, UIHint.SHOW_LABEL] }
        definition.outputType is CurvePatternOutput;

        if (definition.outputType == CurvePatternOutput.PATTERN)
        {
            curvePatternTypePredicate(definition);
        }

        annotation { "Name" : "Path to pattern along", "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE && SketchObject.NO) }
        definition.edges is Query;

        annotation { "Name" : "Reverse path direction", "UIHint" : UIHint.OPPOSITE_DIRECTION }
        definition.oppositeDirection is boolean;

        annotation { "Group Name" : "Spacing", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Spacing type", "UIHint" : [UIHint.REMEMBER_PREVIOUS_VALUE, UIHint.SHOW_LABEL] }
            definition.spacingType is CurveSpacingType;

            if (definition.spacingType == CurveSpacingType.SPACING)
            {
                annotation { "Name" : "Spacing" }
                isLength(definition.distance, PATTERN_OFFSET_BOUND);

                annotation { "Name" : "Instance count" }
                isInteger(definition.count, CURVE_PATTERN_BOUNDS);
            }
            else
            {
                annotation { "Name" : "Fill the path", "Description" : "Use the whole path between the offsets as the total length." }
                definition.fillPath is boolean;

                if (!definition.fillPath)
                {
                    annotation { "Name" : "Total length" }
                    isLength(definition.distance, PATTERN_OFFSET_BOUND);
                }

                annotation { "Name" : "Instance count type", "UIHint" : [UIHint.REMEMBER_PREVIOUS_VALUE, UIHint.SHOW_LABEL] }
                definition.countType is CurveCountType;

                if (definition.countType == CurveCountType.COUNT)
                {
                    annotation { "Name" : "Instance count" }
                    isInteger(definition.count, CURVE_PATTERN_BOUNDS);
                }
                else
                {
                    annotation { "Name" : "Target spacing" }
                    isLength(definition.targetSpacing, LENGTH_BOUNDS);

                    annotation { "Name" : "Rounding type" }
                    definition.roundingType is CurveRoundingType;
                }
            }

            annotation { "Name" : "Start offset" }
            isLength(definition.startOffset, NONNEGATIVE_ZERO_INCLUSIVE_LENGTH_BOUNDS);

            annotation { "Name" : "End offset", "Description" : "Distance kept clear at the end of an open path." }
            isLength(definition.endOffset, NONNEGATIVE_ZERO_INCLUSIVE_LENGTH_BOUNDS);

            annotation { "Name" : "Centered", "Description" : "Centre the instances between the offsets." }
            definition.centered is boolean;
        }

        annotation { "Group Name" : "Orientation", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Orientation", "Default" : CurveOrientationType.FOLLOW, "UIHint" : [UIHint.REMEMBER_PREVIOUS_VALUE, UIHint.SHOW_LABEL] }
            definition.orientationType is CurveOrientationType;

            if (definition.orientationType == CurveOrientationType.DIRECTION)
            {
                annotation { "Name" : "Direction", "Filter" : QueryFilterCompound.ALLOWS_DIRECTION, "MaxNumberOfPicks" : 1,
                             "Description" : "Each instance is rolled so its normal is as close to this direction as the path allows." }
                definition.direction is Query;
            }
            else if (definition.orientationType == CurveOrientationType.FACE)
            {
                annotation { "Name" : "Face", "Filter" : EntityType.FACE && ConstructionObject.NO, "MaxNumberOfPicks" : 1,
                             "Description" : "Each instance is rolled so its normal follows the normal of this face at the point nearest the path." }
                definition.face is Query;

                annotation { "Name" : "Flip normal" }
                definition.flipFace is boolean;
            }
        }

        if (definition.outputType == CurvePatternOutput.MATE_CONNECTORS)
        {
            annotation { "Name" : "Mate connector axis", "UIHint" : [UIHint.REMEMBER_PREVIOUS_VALUE, UIHint.SHOW_LABEL] }
            definition.mateConnectorAxis is CurveMateConnectorAxis;
        }
        else
        {
            if (definition.patternType == PatternType.PART)
            {
                booleanPatternScopePredicate(definition);
            }

            if (definition.patternType == PatternType.FEATURE)
            {
                annotation { "Name" : "Reapply features" }
                definition.fullFeaturePattern is boolean;
            }
        }
    }
    {
        verifyNoMesh(context, definition, "edges");
        verifyNonemptyQuery(context, definition, "edges", ErrorStringEnum.PATTERN_CURVE_NO_EDGES);

        const mateMode = definition.outputType == CurvePatternOutput.MATE_CONNECTORS;

        // The reference entities pick the start of a closed path. Collected before the definition is adjusted.
        var referenceEntities = qNothing();
        if (!mateMode)
        {
            referenceEntities = patternReferenceEntities(context, definition);
            definition = adjustPatternDefinitionEntities(context, definition, false);
        }

        definition.edges = dissolveWires(definition.edges);

        var constructed;
        try
        {
            constructed = constructPath(context, definition.edges, { "referenceGeometry" : referenceEntities });
        }
        catch
        {
            throw regenError(ErrorStringEnum.PATH_EDGES_NOT_CONTINUOUS, ["edges"]);
        }
        const path = constructed.path;
        const pathLength = evPathLength(context, path);

        const spacing = resolveCurveSpacing({
                    "pathLength" : pathLength,
                    "closed" : path.closed,
                    "spacingType" : definition.spacingType,
                    "fillPath" : definition.fillPath,
                    "distance" : definition.distance,
                    "countType" : definition.countType,
                    "count" : definition.count,
                    "targetSpacing" : definition.targetSpacing,
                    "roundingType" : definition.roundingType,
                    "startOffset" : definition.startOffset,
                    "endOffset" : definition.endOffset,
                    "centered" : definition.centered
                });

        verifyPatternSize(context, id, spacing.count);
        if (!mateMode && tooFewPatternInstances(context, spacing.count))
        {
            throw regenError(ErrorStringEnum.PATTERN_INPUT_TOO_FEW_INSTANCES, ["count"]);
        }

        var direction = undefined;
        if (definition.orientationType == CurveOrientationType.DIRECTION)
        {
            direction = extractDirection(context, definition.direction);
            if (direction == undefined)
            {
                throw regenError("Select a direction.", ["direction"]);
            }
        }
        else if (definition.orientationType == CurveOrientationType.FACE)
        {
            verifyNonemptyQuery(context, definition, "face", "Select a face.");
        }

        const keepOrientation = definition.orientationType == CurveOrientationType.KEEP;
        const framesResult = curveStationFrames(context, path, pathLength, spacing.distances, {
                    "reversed" : definition.oppositeDirection,
                    "orientation" : definition.orientationType,
                    "direction" : direction,
                    "face" : definition.face,
                    "flipFace" : definition.flipFace,
                    "referenceGeometry" : referenceEntities
                });
        if (framesResult.fallbacks > 0)
        {
            reportFeatureInfo(context, id, framesResult.fallbacks ~ " station(s) had the reference normal along the path; the previous orientation was kept there.");
        }

        var instanceNames = [];
        for (var i = 1; i < spacing.count; i += 1)
        {
            instanceNames = append(instanceNames, toString(i));
        }
        definition.transforms = curveStationTransforms(framesResult.frames, keepOrientation);
        definition.instanceNames = instanceNames;

        if (mateMode)
        {
            // One mate connector at the first station, patterned to the rest: a single op however many there are.
            const seedId = id + "seedConnector";
            opMateConnector(context, seedId, {
                        "coordSystem" : mateConnectorFrame(framesResult.frames[0], definition.mateConnectorAxis == CurveMateConnectorAxis.ALONG_PATH)
                    });
            definition.patternType = PatternType.PART;
            definition.operationType = NewBodyOperationType.NEW;
            definition.entities = qCreatedBy(seedId, EntityType.BODY);
        }
        definition.seed = definition.entities;

        const remainingTransform = getRemainderPatternTransform(context, {
                    "references" : qUnion([getReferencesForRemainderTransform(definition), definition.edges])
                });
        applyPattern(context, id, definition, remainingTransform);

        reportFeatureInfo(context, id, spacing.count ~ " instances, " ~ roundToPrecision(spacing.spacing / millimeter, 3) ~ " mm apart.");

        // Values for a later Extract variables feature
        const lastIsSeed = spacing.count == 1;
        embedStandardOutputs(context, id, {
                    "output" : getPatternInstanceSetQuery(id, definition, { "instanceNames" : instanceNames, "includeSeed" : true }),
                    "outputDescription" : mateMode ? "The mate connectors" : "The pattern instances and the seed",
                    "inputs" : definition.edges,
                    "variables" : {
                        "count" : extractableVariable(spacing.count, "Instances, including the seed."),
                        "spacing" : extractableVariable(spacing.spacing, "Distance along the path between instances."),
                        "span" : extractableVariable(spacing.span, "Distance along the path from the first to the last instance."),
                        "pathLength" : extractableVariable(pathLength, "Length of the path."),
                        "firstDistance" : extractableVariable(spacing.first, "Distance along the path from its start to the first instance.")
                    },
                    "queries" : {
                        "lastInstance" : extractableQuery(getPatternInstanceSetQuery(id, definition, {
                                        "instanceNames" : lastIsSeed ? [] : [instanceNames[size(instanceNames) - 1]],
                                        "includeSeed" : lastIsSeed
                                    }), "The last instance (the seed if there is only one).")
                    }
                });
    }, {
            outputType : CurvePatternOutput.PATTERN,
            patternType : PatternType.PART,
            operationType : NewBodyOperationType.NEW,
            spacingType : CurveSpacingType.SPACING,
            fillPath : false,
            countType : CurveCountType.COUNT,
            roundingType : CurveRoundingType.EXACT,
            startOffset : 0 * meter,
            endOffset : 0 * meter,
            centered : false,
            oppositeDirection : false,
            orientationType : CurveOrientationType.FOLLOW,
            flipFace : false,
            mateConnectorAxis : CurveMateConnectorAxis.ALONG_PATH,
            fullFeaturePattern : false
        });

/**
 * The pattern-type selection, as in Linear pattern plus: parts and mate connectors, faces, or features.
 */
predicate curvePatternTypePredicate(definition is map)
{
    annotation { "Name" : "Pattern type" }
    definition.patternType is PatternType;

    if (definition.patternType == PatternType.PART)
    {
        booleanStepTypePredicate(definition);

        annotation { "Name" : "Entities to pattern", "Filter" : EntityType.BODY || BodyType.MATE_CONNECTOR }
        definition.entities is Query;
    }
    else if (definition.patternType == PatternType.FACE)
    {
        annotation { "Name" : "Faces to pattern",
                     "UIHint" : ["ALLOW_FEATURE_SELECTION", "SHOW_CREATE_SELECTION"],
                     "Filter" : EntityType.FACE && ConstructionObject.NO && SketchObject.NO && ModifiableEntityOnly.YES }
        definition.faces is Query;
    }
    else if (definition.patternType == PatternType.FEATURE)
    {
        annotation { "Name" : "Features to pattern" }
        definition.instanceFunction is FeatureList;
    }
}

/**
 * The geometry used to pick the start of a closed path. Must be called before the definition is adjusted, because
 * adjusting turns `instanceFunction` into a sorted map.
 */
function patternReferenceEntities(context is Context, definition is map) returns Query
{
    if (definition.patternType == PatternType.FEATURE)
    {
        var queries = [];
        for (var idToFunction in definition.instanceFunction)
        {
            if (!isIdForSketch(context, idToFunction.key))
            {
                queries = append(queries, qCreatedBy(idToFunction.key));
            }
        }
        return qUnion(queries);
    }
    if (definition.patternType == PatternType.FACE)
    {
        return definition.faces;
    }
    return definition.entities;
}

/**
 * Builds a query for a set of pattern instances. Instances are created under different operation ids for part, face and
 * feature patterns, so this collects the applicable instance queries behind one interface. The seed is included
 * when asked. Adapted from Linear pattern plus by Evan Reese (MIT License).
 *
 * @param parameters {{
 *      @field instanceNames {array} : Names of the instances wanted.
 *      @field includeSeed {boolean} : Whether to include the seed.
 * }}
 */
function getPatternInstanceSetQuery(id is Id, definition is map, parameters is map) returns Query
{
    const instanceNames = parameters.instanceNames;
    var instanceQueries = [];

    if (parameters.includeSeed && definition.seed is Query)
    {
        instanceQueries = append(instanceQueries, definition.seed);
    }
    if (size(instanceNames) == 0)
    {
        return qUnion(instanceQueries);
    }

    if (definition.patternType == PatternType.PART)
    {
        instanceQueries = append(instanceQueries, qPatternInstances(id, instanceNames, EntityType.BODY));
        instanceQueries = append(instanceQueries, qPatternInstances(id + "smPattern", instanceNames, EntityType.BODY));
    }
    else if (definition.patternType == PatternType.FACE)
    {
        instanceQueries = append(instanceQueries, qPatternInstances(id, instanceNames, EntityType.FACE));
        instanceQueries = append(instanceQueries, qPatternInstances(id + "smPattern", instanceNames, EntityType.FACE));
    }
    else if (definition.fullFeaturePattern)
    {
        for (var instanceName in instanceNames)
        {
            instanceQueries = append(instanceQueries, qCreatedBy(id + instanceName));
        }
    }
    else
    {
        instanceQueries = append(instanceQueries, qPatternInstances(id + "parts", instanceNames, EntityType.BODY));
        instanceQueries = append(instanceQueries, qPatternInstances(id + "parts" + "smPattern", instanceNames, EntityType.BODY));
        instanceQueries = append(instanceQueries, qPatternInstances(id + "faces", instanceNames, EntityType.FACE));
        instanceQueries = append(instanceQueries, qPatternInstances(id + "faces" + "smPattern", instanceNames, EntityType.FACE));
    }
    return qUnion(instanceQueries);
}
