FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// xSectBeamAnalysis (beam stiffness computations)
import(path : "ebac109589e3bf405d3f3ae7", version : "f6756c9d11585cb98775aa96");

// xSectReferencePoints
import(path : "08fddb59786b6bfee020ee05", version : "61f33606f9384a2757e0729b");

/**
 * Takes a query for edges for an EI profile, a query for FCP and a query for ACP.
 * These queries can be points(verticies), planes/planar faces, mate connectors.
 *
 * Using the FCP/ACP queries as endpoints, lay 200 evenly spaced points along the EI queries
 * between the FCP/acp points. In the event that the EI curve does not extend to ACP/FCP, make our best guess using a linear interpolation and the slope at the end of
 * the ei curve.
 *
 * All calculations run in editing logic.
 * Using those 200 points, solve for the following stiffness measurements. These read only parameters get
 * updated in editing logic.
 *
 * UI Needs a 'refresh' button.
 *
 * 1. prismaticStiffnesslb: Assume that the ski is a actually a rectangular prism with contant bending stiffness EI_bar (average EI between ACP and FCP). What load - in pounds - does it take to deflect the prismatic beam 1" or 25.4mm
 * We do not need any integrations here - use basic beam bendng formula. Load is applied at MRS, beam center.
 * 2. prismaticStiffnessmm: Assume that the ski is a actually a rectangular prism with contant bending stiffness EI_bar (average EI between ACP and FCP). What would the maximum deflection be when 30kg are applied at MRS (halfway between FCP and ACP)
 * 3. estimatedStiffnesslb: Use our 200 EI points to numerically integrate deflections. Apply boundary constraints that deflection at FCP and ACP are zero. What is the load - in pounds - that is required to get a maximum deflection of 25.4mm? Load is applied at MRS.
 * 3. estimatedStiffnessmm: Use our 200 EI points to numerically integrate deflections. Apply boundary constraints that deflection at FCP and ACP are zero. What is the maximum deflection of the beam when 30kg are applied at MRS (beam center).
 */

export function estimateStiffnessEditingLogic(context is Context, id is Id, oldDefinition is map, definition is map,
   isCreating is boolean, specifiedParameters is map, hiddenBodies is Query, clickedButton is string) returns map
{
    if ( clickedButton == "recalculate" || (!oldDefinition.recalculate && definition.recalculate))
    {
        // Guard: eiEdges must be non-empty
        var edges = evaluateQuery(context, definition.eiEdges);
        if (size(edges) == 0)
            return definition;

        // Resolve FCP and ACP to world X
        var xFCP = resolveReferencePointX(context, definition.fcpQiery, definition.eiEdges);
        var xACP = resolveReferencePointX(context, definition.acpQiery, definition.eiEdges);

        if (xFCP == undefined || xACP == undefined)
            return definition;

        if (xFCP >= xACP)
            return definition;

        // Sample EI profile from curve geometry
        var eiData = getEIFromEdges(context, definition.eiEdges, xFCP, xACP);

        if (size(eiData) < 2)
            return definition;

        // Compute all four stiffness values
        var result = computeBeamStiffness(eiData, xFCP, xACP);

        definition.prismaticlb = result.prismaticStiffness_lbin;
        definition.prismaticmm = result.prismaticStiffness_mm * millimeter;
        definition.estimatedlb = result.estimatedStiffness_lbin;
        definition.estimatedmm = result.estimatedStiffness_mm * millimeter;
    }


    return definition;
}


/**
 * Sample EI values from EI visualization edge geometry.
 *
 * The EI curve produced by xSectVisualization.fs encodes EI as:
 *   point = vector(worldX, 0, EI_in_Nm2 * millimeter)
 * so Z / millimeter = EI in N·m².
 *
 * Samples 100 evenly spaced parametric points per edge, decodes EI from Z,
 * sorts by X, and linearly extrapolates to FCP/ACP if the curve is short.
 *
 * @param context {Context}
 * @param eiEdges {Query} : Edge(s) of the EI visualization curve
 * @param xFCP {ValueWithUnits} : Front contact point world X
 * @param xACP {ValueWithUnits} : Aft contact point world X
 * @returns {array} : Sorted array of { x: ValueWithUnits, EI: ValueWithUnits }
 */
function getEIFromEdges(context is Context, eiEdges is Query, xFCP is ValueWithUnits, xACP is ValueWithUnits) returns array
{
    var edges = evaluateQuery(context, eiEdges);
    var points = [];
    var numSamples = 100;

    for (var edge in edges)
    {
        for (var i = 0; i < numSamples; i += 1)
        {
            var t = i / (numSamples - 1);
            try
            {
                var tangentLine = evEdgeTangentLine(context, { "edge" : edge, "parameter" : t });
                var pt = tangentLine.origin;
                var x = pt[0];
                var EI = (pt[2] / millimeter) * newton * meter * meter;
                points = append(points, { "x" : x, "EI" : EI });
            }
            catch (e)
            {
                // Skip failed evaluations
            }
        }
    }

    if (size(points) < 2)
        return points;

    // Insertion sort by x
    for (var i = 1; i < size(points); i += 1)
    {
        var key = points[i];
        var j = i - 1;
        while (j >= 0 && points[j].x > key.x)
        {
            points[j + 1] = points[j];
            j -= 1;
        }
        points[j + 1] = key;
    }

    var n = size(points);

    // Linear extrapolation at front boundary
    if (points[0].x > xFCP && n >= 2)
    {
        var dx = points[1].x - points[0].x;
        if (abs(dx) > 1e-10 * meter)
        {
            var slope = (points[1].EI - points[0].EI) / dx;
            var extEI = points[0].EI + slope * (xFCP - points[0].x);
            if (extEI < 0 * newton * meter * meter)
                extEI = 0 * newton * meter * meter;
            points = concatenateArrays([[{ "x" : xFCP, "EI" : extEI }], points]);
            n = size(points);
        }
    }

    // Linear extrapolation at rear boundary
    if (points[n - 1].x < xACP && n >= 2)
    {
        var dx2 = points[n - 1].x - points[n - 2].x;
        if (abs(dx2) > 1e-10 * meter)
        {
            var slope2 = (points[n - 1].EI - points[n - 2].EI) / dx2;
            var extEI2 = points[n - 1].EI + slope2 * (xACP - points[n - 1].x);
            if (extEI2 < 0 * newton * meter * meter)
                extEI2 = 0 * newton * meter * meter;
            points = append(points, { "x" : xACP, "EI" : extEI2 });
        }
    }

    return points;
}



annotation {
    "Feature Type Name" : "Estimate Stiffness",
    "Feature Type Description" : "Takes an EI profile, FCP and ACP locations and estimates stiffness",
    "Editing Logic Function" : "estimateStiffnessEditingLogic"
    }
export const estimateStiffness = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "EI Edges", "Filter" : EntityType.EDGE}
        definition.eiEdges is Query;

        annotation { "Name" : "FCP", "Filter" : (EntityType.FACE && GeometryType.PLANE) || (EntityType.VERTEX) || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
        definition.fcpQiery is Query;

        annotation { "Name" : "ACP", "Filter" : (EntityType.FACE && GeometryType.PLANE) || (EntityType.VERTEX) || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
        definition.acpQiery is Query;

        annotation { "Group Name" : "Stiffness estimates", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Prismatic stiffness (lb/in)", "UIHint" : UIHint.READ_ONLY }
            isReal(definition.prismaticlb, POSITIVE_REAL_BOUNDS);

            annotation { "Name" : "Prismatic stiffness (mm/30kg)", "UIHint" : UIHint.READ_ONLY }
            isLength(definition.prismaticmm, LENGTH_BOUNDS);

            annotation { "Name" : "Estimated stiffness (lb/in)", "UIHint" : UIHint.READ_ONLY }
            isReal(definition.estimatedlb, POSITIVE_REAL_BOUNDS);

            annotation { "Name" : "Estimated stiffness (mm/30kg)", "UIHint" : UIHint.READ_ONLY }
            isLength(definition.estimatedmm, LENGTH_BOUNDS);


        }

        annotation { "Name" : "Recalculate" }
               isButton(definition.recalculate);

    }
    {
        //Doesn't actually do anything
    });

