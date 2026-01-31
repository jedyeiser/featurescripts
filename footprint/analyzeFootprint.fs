FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

import(path : "a54a829744c4e15e8da55e0e", version : "5a283e0298e3adfbca7a9655");
import(path : "ba0d9a5428fa1db483099bce", version : "44f7eccf75fa18fba43bd3eb");
import(path : "8fbfc083d9b7c765ae06ab5b", version : "534f99b4ed4ee7b09f4e3ac7");

IconNamespace::import(path : "279bd6d83f4e7bcd77624952", version : "a9ec7800d2f223cb59b31642");


export function analyzeEditingLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map, hiddenBodies is Query, clickedButton is string) returns map
{
    // Handle reset button click - clear data first
    if (clickedButton == "resetData")
    {
        for (var entry in FOOTPRINT_DATA_DEFAULTS)
        {
            definition[entry.key] = entry.value;
        }
        // Don't return - fall through to re-analyze if inputs are valid
    }

    // Guard: Don't run analysis until all required inputs are selected
    if (isQueryEmpty(context, definition.fptEdges) ||
        isQueryEmpty(context, definition.rslEdge) ||
        isQueryEmpty(context, definition.fcpQuery) ||
        isQueryEmpty(context, definition.acpQuery))
    {
        return definition;
    }

    // Now safe to run analysis
    var contacts = checkInputData(context, definition);
    var prepared = prepareFootprintCurves(context, definition.fptEdges,
    contacts.fcp[0], contacts.acp[0], 0.001 * millimeter);

    // Analyze (pure math, works in editing logic)
    var footprintData = analyzeFootprintCurves(context, {
        "curveData" : prepared.curveData,
        "fcpPoint" : contacts.fcp,
        "acpPoint" : contacts.acp,
        "mrsPoint" : contacts.mrs
    });

    // Add tip/tail from preparation
    if (prepared.hasTipTail)
    {
        footprintData.tipLength = prepared.tipLength;
        footprintData.tailLength = prepared.tailLength;
    }

    var footprintDataKeys = keys(footprintData);
    var definitionKeys = keys(definition);

    for (var k = 0; k < size(definitionKeys); k += 1)
    {
        var dKey = definitionKeys[k];
        if (any(footprintDataKeys, function(x) { return x == dKey; }))
        {
            definition[dKey] = footprintData[dKey];
        }
    }

    return definition;
}

annotation { "Feature Type Name" : "Analyze footprint", "Icon": IconNamespace::BLOB_DATA, "Editing Logic Function": "analyzeEditingLogic","Feature Type Description" : "Takes footprint edges and basic coordinate information and returns analyzed footprint data" }
export const analyzeFootprint = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "hasTipTail", "UIHint": UIHint.ALWAYS_HIDDEN, "Description": "true if footprint has tip and tail data, false otherwise", "Default": false }
        definition.hasTipTail is boolean;

        annotation { "Name" : "Footprint edges", "Filter" : EntityType.EDGE}
        definition.fptEdges is Query;

        annotation { "Name" : "RSL line", "Filter" : EntityType.EDGE && GeometryType.LINE, "MaxNumberOfPicks" : 1 }
        definition.rslEdge is Query;

        annotation { "Name" : "FCP", "Filter" : (EntityType.FACE && GeometryType.PLANE) || (EntityType.VERTEX) || (BodyType.MATE_CONNECTOR), "MaxNumberOfPicks" : 1 }
        definition.fcpQuery is Query;

        annotation { "Name" : "ACP", "Filter" : (EntityType.FACE && GeometryType.PLANE) || (EntityType.VERTEX) || (BodyType.MATE_CONNECTOR), "MaxNumberOfPicks" : 1 }
        definition.acpQuery is Query;

        annotation { "Name" : "Sketch key points", "Default" : false, "Decription": "When true, outputs a sketch that includes key point data" }
        definition.outputSketch is boolean;

        footprintDataPredicate(definition);

        annotation { "Name" : "Recalculate" }
        isButton(definition.resetData);

    }
    {
        var contacts = checkInputData(context, definition);
        // Prepare edges (extract, split, detect tip/tail)

        var prepared = prepareFootprintCurves(context, definition.fptEdges, contacts.fcp[0], contacts.acp[0], 0.001 * millimeter);

        // Analyze (pure math, works in editing logic)
        var footprintData = analyzeFootprintCurves(context, {
            "curveData" : prepared.curveData,
            "fcpPoint" : contacts.fcp,
            "acpPoint" : contacts.acp,
            "mrsPoint" : contacts.mrs
        });

        // Add tip/tail from preparation
        if (prepared.hasTipTail)
        {
            footprintData.tipLength = prepared.tipLength;
            footprintData.tailLength = prepared.tailLength;
        }
                // Add tip/tail data to results
                footprintData.hasTipTail = prepared.hasTipTail;
                if (prepared.hasTipTail)
                {
                    footprintData.tipLength = prepared.tipLength;
                    footprintData.tailLength = prepared.tailLength;
                }

                //println('keys(footprintData) -> ' ~ keys(footprintData));

        if (definition.outputSketch)
        {
            var footprintSketch = newSketchOnPlane(context, id + "footprintSketch", {
                    "sketchPlane" : plane(vector(0, 0, 0) * millimeter, vector(0, 0, 1))
            });

            //println('footprintData.waist - > ' ~ footprintData.waist);
            skLineSegment(footprintSketch, "waistLine", {
                    "start" : vector(footprintData.waist.x, 0 * millimeter),
                    "end" : vector(footprintData.waist.x, footprintData.waist.point[1]),
                    "construction" : true
            });

            skLineSegment(footprintSketch, "fbWidestLine", {
                    "start" : vector(footprintData.fbWidestData.x, 0 * millimeter),
                    "end" : vector(footprintData.fbWidestData.x, footprintData.fbWidestData.point[1]),
                    "construction" : true
            });

            skLineSegment(footprintSketch, "abWidestLine", {
                    "start" : vector(footprintData.abWidestData.x, 0 * millimeter),
                    "end" : vector(footprintData.abWidestData.x, footprintData.abWidestData.point[1]),
                    "construction" : true
            });

            skLineSegment(footprintSketch, "connectWidest", {
                    "start" : vector(footprintData.fbWidestData.x, footprintData.fbWidestData.point[1]),
                    "end" : vector(footprintData.abWidestData.x, footprintData.abWidestData.point[1]),
                    "construction" : true
            });

            println('fbInflectionData - > ' ~ footprintData.fbInflectionData);
            if (footprintData.fbInflectionData.found)
            {
                skLineSegment(footprintSketch, "fbInflection", {
                    "start" : vector(footprintData.fbInflectionData.x, 0 * millimeter),
                    "end" : vector(footprintData.fbInflectionData.x, footprintData.fbInflectionData.point[1]),
                    "construction" : true
                });
            }

            if (footprintData.abInflectionData.found)
            {
                skLineSegment(footprintSketch, "abInflection", {
                    "start" : vector(footprintData.abInflectionData.x, 0 * millimeter),
                    "end" : vector(footprintData.abInflectionData.x, footprintData.abInflectionData.point[1]),
                    "construction" : true
                });
            }

            if (footprintData.fbInflectionData.found && footprintData.abInflectionData.found)
            {
                skLineSegment(footprintSketch, "connectInflection", {
                    "start" : vector(footprintData.fbInflectionData.x, footprintData.fbInflectionData.point[1]),
                    "end" : vector(footprintData.abInflectionData.x, footprintData.abInflectionData.point[1]),
                    "construction" : true
            });
            }

            skSolve(footprintSketch);


        }
    });

export function checkInputData(context is Context, definition is map) returns map
{
    var fcpDist = evDistance(context, {
            "side0" : definition.rslEdge,
            "side1" : definition.fcpQuery
    });

    var acpDist = evDistance(context, {
            "side0" : definition.rslEdge,
            "side1" : definition.acpQuery
    });

    var fcpPoint = fcpDist.sides[0].point;
    var acpPoint = acpDist.sides[0].point;
    var mrsPoint = (fcpPoint + acpPoint)/2;

    return {'fcp': fcpPoint, 'mrs': mrsPoint, 'acp': acpPoint};

}

/**
 * Prepares footprint edges for analysis by extracting, splitting at Y=0 if needed,
 * and detecting tip/tail points.
 *
 * @param context
 * @param id
 * @param edges : Query - The input footprint edges
 * @param fcpX : ValueWithUnits - X coordinate of forebody contact point
 * @param acpX : ValueWithUnits - X coordinate of aftbody contact point
 * @param tolerance : ValueWithUnits - Tolerance for Y=0 detection (default 0.001mm)
 *
 * @returns map {
 *     edges : Query - Clean edges for analysis (Y >= 0 only)
 *     hasTipTail : boolean - Whether tip/tail points were found
 *     tipX : ValueWithUnits - X coordinate of tip (if found)
 *     tailX : ValueWithUnits - X coordinate of tail (if found)
 *     tipLength : ValueWithUnits - Distance from FCP to tip (if found)
 *     tailLength : ValueWithUnits - Distance from ACP to tail (if found)
 *     needsCleanup : boolean - Whether cleanup is required (extracted wires were created)
 * }
 */
export function prepareFootprintEdges(context is Context, id is Id, args is map) returns map
{
    var edges = args.edges;
    var fcpX = args.fcpX;
    var acpX = args.acpX;
    var tolerance = (args.tolerance == undefined) ? (0.001 * millimeter) : args.tolerance;

    // === Step 1: Quick scan - do we have any Y < 0? ===
    var hasNegativeY = checkForNegativeY(context, edges, tolerance);

    var workingEdges = edges;
    var needsCleanup = false;

    if (hasNegativeY)
    {
        // === Step 2: Extract wires ===
        opExtractWires(context, id + "extractWires", {
            "edges" : edges
        });

        // === Step 3: Split at Y=0 ===
        var splitPlane = plane(vector(0, 0, 0) * meter, vector(0, 1, 0));

        opSplitPart(context, id + "split", {
            "targets" : qCreatedBy(id + "extractWires", EntityType.BODY),
            "tool" : splitPlane
        });

        // === Step 4: Delete Y < 0 bodies ===
        deleteNegativeYBodies(context, id + "deleteNeg", id + "extractWires", tolerance);

        workingEdges = qCreatedBy(id + "extractWires", EntityType.EDGE);
        needsCleanup = true;
    }

    // === Step 5: Detect tip/tail ===
    var tipTailResult = detectTipTail(context, workingEdges, fcpX, acpX, tolerance);

    return {
        "edges" : workingEdges,
        "hasTipTail" : tipTailResult.hasTipTail,
        "tipX" : tipTailResult.tipX,
        "tailX" : tipTailResult.tailX,
        "tipLength" : tipTailResult.tipLength,
        "tailLength" : tipTailResult.tailLength,
        "needsCleanup" : needsCleanup
    };
}

/**
 * Check if any edge points have Y < -tolerance
 */
function checkForNegativeY(context is Context, edges is Query, tolerance is ValueWithUnits) returns boolean
{
    var edgeArray = evaluateQuery(context, edges);

    for (var edge in edgeArray)
    {
        var box_ = evBox3d(context, { "topology" : edge, "tight" : true });
        if (box_.minCorner[1] < -tolerance)
        {
            return true;
        }
    }
    return false;
}

/**
 * Delete bodies created by extractWires that are in the Y < 0 region
 */
function deleteNegativeYBodies(context is Context, id is Id, extractId is Id, tolerance is ValueWithUnits)
{
    var bodies = evaluateQuery(context, qCreatedBy(extractId, EntityType.BODY));
    var toDelete = [];

    for (var body in bodies)
    {
        var box_ = evBox3d(context, { "topology" : body, "tight" : true });
        // Keep if maxY > tolerance (has positive Y content)
        // Delete if maxY <= tolerance (entirely in Y <= 0 region)
        if (box_.maxCorner[1] <= tolerance)
        {
            toDelete = append(toDelete, body);
        }
    }

    if (size(toDelete) > 0)
    {
        opDeleteBodies(context, id, {
            "entities" : qUnion(toDelete)
        });
    }
}

/**
 * Detect tip and tail points (where Y � 0 and X extends beyond FCP/ACP)
 */
function detectTipTail(context is Context, edges is Query, fcpX is ValueWithUnits, acpX is ValueWithUnits, tolerance is ValueWithUnits) returns map
{
    var edgeArray = evaluateQuery(context, edges);

    var minX = inf * meter;  // Potential tip (beyond FCP in negative X direction)
    var maxX = -inf * meter; // Potential tail (beyond ACP in positive X direction)

    // Determine which direction is FB vs AB
    var fbIsNegativeX = fcpX < acpX;

    for (var edge in edgeArray)
    {
        // Sample edge to find Y � 0 points
        var params = [0, 0.25, 0.5, 0.75, 1]; // Quick sample
        var tangentLines = evEdgeTangentLines(context, {
            "edge" : edge,
            "parameters" : params
        });

        for (var tl in tangentLines)
        {
            var pt = tl.origin;
            // Check if Y � 0
            if (abs(pt[1]) < tolerance)
            {
                minX = min([minX, pt[0]]);
                maxX = max([maxX, pt[0]]);
            }
        }
    }

    // Determine tip/tail based on orientation
    var tipX = undefined;
    var tailX = undefined;
    var tipLength = undefined;
    var tailLength = undefined;
    var hasTipTail = false;

    if (fbIsNegativeX)
    {
        // FB is negative X, so tip is at minX (if beyond FCP)
        if (minX < fcpX - tolerance)
        {
            tipX = minX;
            tipLength = abs(fcpX - tipX);
            hasTipTail = true;
        }
        // AB is positive X, so tail is at maxX (if beyond ACP)
        if (maxX > acpX + tolerance)
        {
            tailX = maxX;
            tailLength = abs(tailX - acpX);
            hasTipTail = true;
        }
    }
    else
    {
        // FB is positive X, so tip is at maxX (if beyond FCP)
        if (maxX > fcpX + tolerance)
        {
            tipX = maxX;
            tipLength = abs(tipX - fcpX);
            hasTipTail = true;
        }
        // AB is negative X, so tail is at minX (if beyond ACP)
        if (minX < acpX - tolerance)
        {
            tailX = minX;
            tailLength = abs(acpX - tailX);
            hasTipTail = true;
        }
    }

    return {
        "hasTipTail" : hasTipTail,
        "tipX" : tipX,
        "tailX" : tailX,
        "tipLength" : tipLength,
        "tailLength" : tailLength
    };
}

/**
 * Cleanup extracted wire bodies after analysis is complete
 */
export function cleanupPreparedEdges(context is Context, id is Id, prepareId is Id)
{
    try silent
    {
        opDeleteBodies(context, id, {
            "entities" : qCreatedBy(prepareId + "extractWires", EntityType.BODY)
        });
    }
}
