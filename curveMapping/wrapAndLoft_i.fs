FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

//import wrapCurve.fs
import(path : "6863116065bf5063633f30ac", version : "049d7fc5255da2e874b66c81");


/**
 * This is a new and improved version of an old feature. The new and improved version 
 * uses functionality built in wrapCurve. This feature is simply an extension of that feature. 
 * 
 * User provides a selection of source edges. These must be G0 continuous. 
 * 
 * We will also test to see if the input curves are coplanar. This is dealt with in editing logic,
 * but it's an important aspect to bring up early. 
 * 
 * If the input curves are coplanar, the user does not need to specify a fromCurves query. 
 * If the input curves are coplanar, we assume that the 'fromCurve is just a projection of the toCurve onto the 
 * shared plane. We need to develop logic to do this. We need to make sure that we're first projecting each edge, and then only combining edges if they are colinear. 
 * Note that this could cause cases where the fromCurve does not extend 'far enough' through our source edges. In this case, 
 * use the zAxis (and associated measured coordinate) of the closest available frame to make sure that wrapping geometry is correct. 
 * We axckowledge thaat this means any sourceEdges outside of our reference curves will basically be measured in a tranlated frame along the frame z axis. 
 * our source edges to have a parameter on the 'theoretical curve'. 
 * 
 * The user specifies a source reference point. This is an important point as it specifies. how and where geometry might change in a subtle way. 
 * This is the point along the fromCurves we reference. 
 * 
 * The user specifies toCurves - a group of G1 continuous curves (may be lines.arcs) to wrap our source curves around. 
 * 
 * The user specifies a toCurves reference point. 
 * 
 * The user supplies a sampling multiplier. WE sample each curve at this multiplier multiplied by the number of its control points. Minimum is 5. 
 * User provides standard spline approximation input (tolerance, maxCPs, degree) to which all of our wrapped edges will be apprroximated. 
 * 
 * The user specifies a 'Primary offset'. We offset our wrapped curve (move normal to the frenet xAxis) by this amount and loft between the two curves. 
 * If the user specifies 'secondDirection', we allow the user to specify a different length, which we will offset in the opposite direction. 
 * 
 * We loft a surface between the outermost curves. 
 * 
 * If user has 'keep Output curves' selected, an enum pops up KEEP_ALL or KEEP_WRAPPED. 
 * If KEEP_ALL : keep the wrapped curve and the first (and if secondOffset and secondOffset  > 0 * millimeter, the second) offset curves. We use opExtractWires 
 * to join the wires of each wrapped/offset curve collected in group. Delete the curves we'd previously created - we're now doubling up. 
 * If KEEP_WRAPPED is selected, delete the offset curves but extract the wrapped curve as described above. 
 * 
 */ 
 
 
 export function wrapAndLoftEditingLogic(context is Context, id is Id, oldDefinition is map, definition is map,
   isCreating is boolean, specifiedParameters is map, hiddenBodies is Query, clickedButton is string) returns map
{
    //1. Test planaraity of sourceEdges
    //2. update definition.sourceEdgesArePlanar accordingly
    
    return definition;
}
 
 annotation { "Feature Type Name" : "Wrap and Loft", "Feature Type Description" : "Takes source edges and a wrapping definition to 'loft a surface' from wrapped versions of the source curves.", "Editing Logic Function" : "wrapAndLoftEditingLogic" }
 export const wrapAndLoft = defineFeature(function(context is Context, id is Id, definition is map)
     precondition
     {
         annotation { "Name" : "Wrap edges", "Filter" : EntityType.EDGE, "Decription" : "Edges that we will wrap and create a loft through"}
         definition.sourceEdges is Query;
         
         annotation { "Name" : "areSourceEdgesPlanar", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
         definition.sourceEdgesArePlanar is boolean;
         
         annotation { "Group Name" : "From data", "Collapsed By Default" : true }
        {
            if (!definition.sourceEdgesArePlanar)
            {
                annotation { "Name" : "From edge(s)",
                    "Filter" : EntityType.EDGE,
                    "MaxNumberOfPicks" : 10,
                    "Description" : "Reference edge(s) to map from (source reference)" }
                definition.fromEdges is Query;
            }

            annotation { "Name" : "From reference", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1, "Description" : "reference point on from curve" }
            definition.fromRef is Query;
        }

        annotation { "Group Name" : "To data", "Collapsed By Default" : true }
        {
            annotation { "Name" : "To edge(s)",
                    "Filter" : EntityType.EDGE,
                    "MaxNumberOfPicks" : 10,
                    "Description" : "Reference edge(s) to map to (target reference)" }
            definition.toEdges is Query;

            annotation { "Name" : "Flip", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false }
            definition.flipTo is boolean;

            annotation { "Name" : "To reference", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1, "Description" : "reference point on to curve" }
            definition.toRef is Query;

            annotation { "Name" : "Flip normal", "Default" : false, "Description" : "When true, flips the frenet frame normal vector on the to chain" }
            definition.flipToNormal is boolean;
        }


        annotation { "Name" : "Advanced options",
                    "Default" : false }
        definition.showAdvanced is boolean;

        if (definition.showAdvanced)
        {
            annotation { "Name" : "Sampling density", "Description" : "Distance between sample points along source edges" }
            isLength(definition.samplingDensity, samplingDensityBounds);

            annotation { "Name" : "Target degree", "Column Name" : "Approximation target degree" }
            isInteger(definition.approximationDegree, DEGREE_BOUND);

            annotation { "Name" : "Maximum control points" }
            isInteger(definition.approximationMaxCPs, { (unitless) : [4, 15, MAX_CONTROL_POINTS] } as IntegerBoundSpec);

            annotation { "Name" : "Tolerance" }
            isLength(definition.approximationTolerance, TOLERANCE_BOUND);
        }

        annotation { "Group Name" : "Debug Options",
                    "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print from BSplines",
                        "Description" : "Print BSpline data from 'from' reference chain",
                        "Default" : false }
            definition.debugFromBSplines is boolean;

            annotation { "Name" : "Print to BSplines",
                        "Description" : "Print BSpline data from 'to' reference chain",
                        "Default" : false }
            definition.debugToBSplines is boolean;

            annotation { "Name" : "Print source BSplines",
                        "Description" : "Print BSpline data from source curves",
                        "Default" : false }
            definition.debugSourceBSplines is boolean;

            annotation { "Name" : "Print wrapped BSplines",
                        "Description" : "Print BSpline data from wrapped output curves",
                        "Default" : false }
            definition.debugWrappedCurves is boolean;

            annotation { "Name" : "Detailed BSpline output",
                        "Description" : "Print full control points and knot vector (vs. metadata only)",
                        "Default" : false }
            definition.debugDetailedBSplines is boolean;

            annotation { "Name" : "Show from frames",
                        "Description" : "Draw Frenet frame axes along the from reference path",
                        "Default" : false }
            definition.debugShowFromFrames is boolean;

            annotation { "Name" : "Show to frames",
                        "Description" : "Draw Frenet frame axes along the to reference path",
                        "Default" : false }
            definition.debugShowToFrames is boolean;
        }
         
         
     }
     {
         // Define the function's action
     });
     
     //function to test planarity
     
     //function go get fromCurves (only create these curves if necessary. If so, make sure to delete them later on) when sourceEdges are planar
     
     //Function to offset curves from wrapped reference
     
     //function to clean and group curves with opExtractWires
 