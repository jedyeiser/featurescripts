FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

//import curveChain(export import)
export import(path : "670e82ad72abc97906ec9038", version : "64dc9244c25196d5236b6e33");
//import curveMappingUtils(export import)
export import(path : "de955d503dbb0ec88622e51b", version : "c9021c710052b752f3eab663");
//import curveMappingCore (export import)
export import(path : "683d867c35fdab9c98d47556", version : "af2d406a34cc80a3f648a41e");

//import tools/bspline_data
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/b1c7f2116fb64e6b40bf53f4", version : "4fe0cca8e00a4cd812896a8c");

annotation { "Feature Type Name" : "Wrap Curve",
             "Feature Type Description" : "Map curves from one reference edge to another using Frenet frame transformations",
             "Filter Selector" : "allparts",
             "Editing Logic Function" : "wrapCurveEditLogic" }
export const wrapCurve = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "From edge(s)",
                    "Filter" : EntityType.EDGE,
                    "MaxNumberOfPicks" : 10,
                    "Description" : "Reference edge(s) to map from (source reference)" }
        definition.fromEdge is Query;

        annotation { "Name" : "To edge(s)",
                    "Filter" : EntityType.EDGE,
                    "MaxNumberOfPicks" : 10,
                    "Description" : "Reference edge(s) to map to (target reference)" }
        definition.toEdge is Query;

        annotation { "Name" : "Source curves",
                    "Filter" : EntityType.EDGE,
                    "MaxNumberOfPicks" : 50,
                    "Description" : "Curves to map from fromEdge to toEdge" }
        definition.sourceCurves is Query;

        annotation { "Name" : "Alignment point",
                    "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR,
                    "MaxNumberOfPicks" : 1,
                    "Description" : "Point to align mapping (defaults to curve midpoints)" }
        definition.alignmentPoint is Query;

        annotation { "Name" : "Chain continuity" }
        definition.chainContinuity is ChainContinuity;

        annotation { "Name" : "Mapping mode" }
        definition.mappingMode is MappingMode;

        annotation { "Name" : "Advanced options",
                    "Default" : false }
        definition.showAdvanced is boolean;

        if (definition.showAdvanced)
        {
            annotation { "Name" : "Samples per segment",
                        "Description" : "Number of points to sample for each curve segment" }
            isInteger(definition.numSamples, POSITIVE_COUNT_BOUNDS);

            annotation { "Name" : "Minimal segmentation",
                        "Description" : "Only segment at C0 breaks (not G1)",
                        "Default" : false }
            definition.minimalSegmentation is boolean;
        }
    }
    {
        // Default values
        const numSamples = definition.showAdvanced ? definition.numSamples : 25;
        const minimalSegmentation = definition.showAdvanced ? definition.minimalSegmentation : false;

        // Step 1: Build curve chains with continuity validation
        var fromChain is CurveChain;
        var toChain is CurveChain;

        try
        {
            fromChain = buildCurveChain(context, definition.fromEdge,
                                       definition.chainContinuity, {});
        }
        catch (error)
        {
            throw regenError("From edge chain error", ["fromEdge"]);
        }

        try
        {
            toChain = buildCurveChain(context, definition.toEdge,
                                     definition.chainContinuity, {});
        }
        catch (error)
        {
            throw regenError("To edge chain error", ["toEdge"]);
        }

        // Step 2: Determine alignment point
        var alignmentPoint;
        if (size(evaluateQuery(context, definition.alignmentPoint)) > 0)
        {
            // User specified alignment point
            const alignVertex = evaluateQuery(context, definition.alignmentPoint)[0];
            if (alignVertex is BodyType.MATE_CONNECTOR)
            {
                alignmentPoint = evMateConnector(context, {
                    "mateConnector" : alignVertex
                }).origin;
            }
            else
            {
                alignmentPoint = evVertexPoint(context, {
                    "vertex" : alignVertex
                });
            }
        }
        else
        {
            // Default: use midpoint of fromChain
            const midEval = evaluateChain(context, fromChain, [0.5], 0);
            alignmentPoint = midEval.points[0];
        }

        // Step 3: Build mapping
        const mapping = buildCurveMapping(context, id, fromChain, toChain,
                                         alignmentPoint, AlignmentMode.AUTO,
                                         { "mappingMode" : definition.mappingMode });

        // Step 4: Process each source curve
        const sourceCurveArray = evaluateQuery(context, definition.sourceCurves);

        for (var i = 0; i < size(sourceCurveArray); i += 1)
        {
            const sourceEdge = sourceCurveArray[i];

            // Get BSpline representation of source curve
            var sourceBSpline;
            try
            {
                sourceBSpline = evApproximateBSplineCurve(context, {
                    "edge" : sourceEdge
                });
            }
            catch (error)
            {
                reportFeatureWarning(context, id,
                    "Could not convert edge " ~ (i + 1) ~ " to BSpline: " ~ toString(error));
                continue;
            }

            // Map curve with segmentation
            var mappedSegments;
            try
            {
                mappedSegments = mapCurveSegmented(context, mapping, sourceBSpline, {
                    "numSamplesPerSegment" : numSamples,
                    "minimalSegmentation" : minimalSegmentation
                });
            }
            catch (error)
            {
                reportFeatureWarning(context, id,
                    "Could not map curve " ~ (i + 1) ~ ": " ~ toString(error));
                continue;
            }

            // Join segments
            const joinedCurves = joinMappedSegments(context, mappedSegments, {
                "keepSeparateAtG0" : true
            });

            // Create geometry from mapped curves
            for (var j = 0; j < size(joinedCurves); j += 1)
            {
                try
                {
                    opCreateBSplineCurve(context, id + ("curve" ~ i ~ "_" ~ j), {
                        "bSplineCurve" : joinedCurves[j]
                    });
                }
                catch (error)
                {
                    reportFeatureWarning(context, id,
                        "Could not create mapped curve " ~ (i + 1) ~ "." ~ (j + 1) ~ ": " ~ toString(error));
                }
            }
        }
    }, {
        chainContinuity : ChainContinuity.G0,
        mappingMode : MappingMode.LENGTH,
        showAdvanced : false,
        numSamples : 25,
        minimalSegmentation : false
    });

/**
 * Editing logic to provide better defaults and validation.
 */
export function wrapCurveEditLogic(context is Context, id is Id, oldDefinition is map,
                                   definition is map, isCreating is boolean) returns map
{
    // Auto-select chain continuity based on from/to edges
    if (oldDefinition.fromEdge != definition.fromEdge ||
        oldDefinition.toEdge != definition.toEdge)
    {
        // Could analyze edges and auto-detect continuity
        // For now, keep user selection
    }

    return definition;
}
