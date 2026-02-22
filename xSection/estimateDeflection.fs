FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

/**
 * This tool/these tools are used to estimate beam deflections using
 * Euler Bernouli beam bending equations. User supplies stiffness curve, context info
 * and array of load/support conditions. Function calculates deflection and creates a curve matching that deflection. 
 * 
 * User provides a query for EI edges. Additional EI can be added with an additional EI query. 
 * User specifies if the analysis should have an applied moment, and if so, where that moment should be applied. 
 * User specifies number of evaluation points
 * User specifies total applied load. 
 * User specifies applied load ratio (load 1/ load 2)
 * User specifies two applied load shapes. 
 * User specifies up to three load shapes. These are assumed to be the forebody support, the aftbody support, and the center support. 
 * If three support shapes are specified, the user must specify what percentage of the support force goes on the center load. 
 * Total loads/supports are solved to provide shear and moment estimates. 
 * 
 * We can combine the moment with the EI at each X point and integrate twice. 
 * WE enforce the boundary condition that the deflection at the outermost SUPPORTS is zero. 
 * 
 * Each load has a shape, a center and a width (except a point load). 
 * Constant - constant load throughout the width
 * Linear - Triangular load with maximum at the center of the span
 * Quadratic - Quadratically shaped load with maximum at the center (zero slope)
 * Quintic - Qunitic approximation of a gaussian curve. Easy to solve for with repeated roots. 
 * Logistic - Two mirrored logistic functions with maximum at the center
 * 
 * calculate the deflection of the beam, and create an approximated curve of the deflected curve. Provide standard curve approximating
 * inputs
 */
 
 export enum LoadType
 {
     POINT,
     CONSTANT,
     LINEAR,
     QUADRATIC,
     QUINTIC,
     LOGISTIC
 }
 
 export enum LocationType
 {
     QUERY,
     X_VAL
 }
 
 export const LoadBalanceBounds = {(unitless) : [0.001, .75, .999]} as RealBoundSpec;
 export const EvaluationPointBounds = {(unitless) : [20, 100, 500]} as IntegerBoundSpec;
 export const AppliedLoadBounds = {(unitless) : [3, 80, 600]} as RealBoundSpec;
 
 export function estimateDeflectionEditLogic(context is Context, id is Id, oldDefinition is map,
    definition is map, isCreating is boolean, specifiedParameters is map) returns map
{
    println("estimateDeflectionEditLogic called");
   
    // Applied load 1 (always visible)
    definition.applied1NeedsWidth = (definition.applied1LoadShape != LoadType.POINT);
    definition.applied1IsQuery    = (definition.applied1LocationType == LocationType.QUERY);

    // Applied load 2 (shown when secondApplied == true)
    definition.applied2NeedsWidth = (definition.applied2LoadShape != LoadType.POINT);
    definition.applied2IsQuery    = (definition.applied2LocationType == LocationType.QUERY);

    // Support load 1 (always visible)
    definition.support1NeedsWidth = (definition.support1LoadShape != LoadType.POINT);
    definition.support1IsQuery    = (definition.support1LocationType == LocationType.QUERY);

    // Support load 2 (always visible)
    definition.support2NeedsWidth = (definition.support2LoadShape != LoadType.POINT);
    definition.support2IsQuery    = (definition.support2LocationType == LocationType.QUERY);

    // Support load 3 (shown when addThirdSupport == true)
    definition.support3NeedsWidth = (definition.support3LoadShape != LoadType.POINT);
    definition.support3IsQuery    = (definition.support3LocationType == LocationType.QUERY);

    return definition;
}


// =============================================================================
// HELPER FUNCTIONS
// =============================================================================

/**
 * Linearly interpolate EI at x from a sorted eiData array.
 * Flat-extrapolates outside the data range.
 */
function interpEI(eiData is array, x is ValueWithUnits) returns ValueWithUnits
{
    var n = size(eiData);
    if (n == 0)
    {
        return 0 * newton * meter * meter;
    }
    if (n == 1)
    {
        return eiData[0].EI;
    }
    if (x <= eiData[0].x)
    {
        return eiData[0].EI;
    }
    if (x >= eiData[n - 1].x)
    {
        return eiData[n - 1].EI;
    }
    for (var i = 0; i < n - 1; i += 1)
    {
        if (x >= eiData[i].x && x <= eiData[i + 1].x)
        {
            var xSpan = eiData[i + 1].x - eiData[i].x;
            if (abs(xSpan) < 1e-15 * meter)
            {
                return eiData[i].EI;
            }
            var t = (x - eiData[i].x) / xSpan;
            return eiData[i].EI + t * (eiData[i + 1].EI - eiData[i].EI);
        }
    }
    return eiData[n - 1].EI;
}

/**
 * Sample EI visualization edges and return a sorted array of { "x", "EI" } pairs.
 * EI is encoded as Z coordinate [mm] -> EI [N*m^2].
 */
function sampleEdgesForEI(context is Context, edgeQuery is Query, numSamples is number) returns array
{
    var samples = [];
    var edges = evaluateQuery(context, edgeQuery);
    for (var edge in edges)
    {
        for (var i = 0; i < numSamples; i += 1)
        {
            var t = i / (numSamples - 1);
            try
            {
                var tangentLine = evEdgeTangentLine(context, { "edge" : edge, "parameter" : t });
                var pt = tangentLine.origin;
                var EI = (pt[2] / millimeter) * newton * meter * meter;
                samples = append(samples, { "x" : pt[0], "EI" : EI });
            }
            catch (e)
            {
                // Skip failed evaluations
            }
        }
    }
    // Insertion sort by x
    for (var i = 1; i < size(samples); i += 1)
    {
        var key = samples[i];
        var j = i - 1;
        while (j >= 0 && samples[j].x > key.x)
        {
            samples[j + 1] = samples[j];
            j -= 1;
        }
        samples[j + 1] = key;
    }
    return samples;
}

/**
 * Extract world X coordinate from a vertex, mate connector, or planar face query.
 */
function resolveQueryX(context is Context, q is Query) returns ValueWithUnits
{
    // Try vertex
    try
    {
        var verts = evaluateQuery(context, qEntityFilter(q, EntityType.VERTEX));
        if (size(verts) > 0)
        {
            return evVertexPoint(context, { "vertex" : verts[0] })[0];
        }
    }
    catch (e) {}

    // Try mate connector
    try
    {
        var connectors = evaluateQuery(context, qBodyType(q, BodyType.MATE_CONNECTOR));
        if (size(connectors) > 0)
        {
            return evMateConnector(context, { "mateConnector" : connectors[0] }).origin[0];
        }
    }
    catch (e) {}

    // Try planar face
    try
    {
        var faces = evaluateQuery(context, qGeometry(q, GeometryType.PLANE));
        if (size(faces) > 0)
        {
            return evPlane(context, { "face" : faces[0] }).origin[0];
        }
    }
    catch (e) {}

    throw regenError("Could not resolve X position from the selected query. Use a vertex, mate connector, or planar face.");
}

/**
 * Distributed load intensity at x [N/m], positive = upward.
 * Returns 0 for POINT loads (those are handled as discrete shear jumps).
 *
 * All non-POINT shapes are normalised so the integral over the full width equals totalForce.
 */
function loadIntensityAt(x is ValueWithUnits, center is ValueWithUnits,
    totalForce is ValueWithUnits, shape is LoadType, width is ValueWithUnits) returns ValueWithUnits
{
    if (shape == LoadType.POINT)
    {
        return 0 * newton / meter;
    }
    var halfW = width / 2;
    var relX = x - center;
    if (abs(relX) > halfW)
    {
        return 0 * newton / meter;
    }
    if (shape == LoadType.CONSTANT)
    {
        return totalForce / width;
    }
    else if (shape == LoadType.LINEAR)
    {
        // Triangle: peak at center, zero at edges. Integral = totalForce.
        var t = abs(relX) / halfW; // 0 at center, 1 at edge
        return (2 * totalForce / width) * (1 - t);
    }
    else if (shape == LoadType.QUADRATIC)
    {
        // Parabola: peak at center (zero slope), zero at edges. Integral = totalForce.
        var t = relX / halfW;
        return (1.5 * totalForce / width) * (1 - t * t);
    }
    else if (shape == LoadType.QUINTIC)
    {
        // C2-smooth bump: f(t)=1-6t^5+15t^4-10t^3, integral over [-1,1] = 1. Integral = totalForce.
        var t = abs(relX) / halfW; // 0 at center, 1 at edge
        var fq = 1 - 6 * (t ^ 5) + 15 * (t ^ 4) - 10 * (t ^ 3);
        return (totalForce / halfW) * fq;
    }
    else
    {
        // LOGISTIC: symmetric logistic pair. Integral over all x = totalForce (k=10).
        var t = relX / halfW;
        var u1 = 10.0 * (t + 0.5);
        var u2 = 10.0 * (t - 0.5);
        var shapeVal = 1 / (1 + exp(-u1)) - 1 / (1 + exp(-u2));
        return (totalForce / halfW) * shapeVal;
    }
}

/**
 * Return the index in x_eval whose value is closest to target.
 */
function findNearestIndex(x_eval is array, target is ValueWithUnits) returns number
{
    var best = 0;
    var bestDist = abs(x_eval[0] - target);
    for (var i = 1; i < size(x_eval); i += 1)
    {
        var d = abs(x_eval[i] - target);
        if (d < bestDist)
        {
            bestDist = d;
            best = i;
        }
    }
    return best;
}


 annotation { "Feature Type Name" : "Estimate Deflection",
             "Editing Logic Function" : "estimateDeflectionEditLogic",
             "Feature Type Description" : "Estimates beam deflection given an EI profile and loading conditions" }
 export const estimateDeflection = defineFeature(function(context is Context, id is Id, definition is map)
     precondition
     {
         annotation { "Name" : "EI Edges", "Filter" : EntityType.EDGE, "Description" : "Dedges defining EI profile" }
         definition.selEI is Query;
         
         annotation { "Name" : "Number of evaluation points" }
         isInteger(definition.numEvalPoints, EvaluationPointBounds);
         
         
         annotation { "Name" : "Add plate/mounting conditions?", "Default" : false, "Description" : "When true, allows users to add an additional query for plate stiffness as well as apply a mounting reaction moment" }
         definition.addPlateConditions is boolean;
         
         if (definition.addPlateConditions)
         {
             annotation { "Group Name" : "Plate conditions", "Collapsed By Default" : false, "Driving Parameter" : "addPlateConditions" }
             {
                 annotation { "Name" : "Plate stiffness", "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 1 }
                 definition.plateStiffness is Query;
                 
                 annotation { "Name" : "Reaction moment", "Description" : "Moment from forward pressure in Nm", "Default" : 0}
                 isReal(definition.reactionMoment, POSITIVE_REAL_BOUNDS);
                 
                 annotation { "Name" : "Moment x location", "Description" : "We assume that EI profile is in XZ plane. This is the location of the reaction moment in X", "Default" : 0 * meter }
                 isLength(definition.reactionMomentX, LENGTH_BOUNDS);
             }
             
         }
         
         annotation { "Group Name" : "Applied load data", "Collapsed By Default" : false }
         {
             annotation { "Name" : "My Count" }
             isReal(definition.appliedLoad, AppliedLoadBounds);
             
             annotation { "Name" : "Applied load balance", "Description" : "The percentage of the applied load borne by the first Applied load. When only one applied load is provided, this defaults to 1" }
             isReal(definition.appliedLoadBalance, LoadBalanceBounds);
             
             annotation { "Group Name" : "Applied load 1", "Collapsed By Default" : false }
             {
                 annotation { "Name" : "Applied load 1 Location Type" }
                 definition.applied1LocationType is LocationType;
                                     
                annotation { "Name" : "Applied load 1 load shape", "Description" : "Shape of the applied load" , "Default" : LoadType.POINT, "UIHint" : UIHint.SHOW_LABEL  }
                definition.applied1LoadShape is LoadType;
                
                annotation { "Name" : "applied1NeedsWidth", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
                definition.applied1NeedsWidth is boolean;
                
                if (definition.applied1NeedsWidth)
                {
                    annotation { "Name" : "Applied load 1 width" }
                    isLength(definition.applied1Width, LENGTH_BOUNDS);
                }
                 
                 annotation { "Name" : "applied1IsQuery", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN } // toggles query, x location visibility. 
                 definition.applied1IsQuery is boolean;
                 
                 if (definition.applied1IsQuery)
                 {
                     annotation { "Name" : "Applied load 1 query", "Description" : "Location of center of applied 1 load", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
                     definition.applied1Query is Query;
                 }
                 else
                 {
                     annotation { "Name" : "Applied load 1 x location", "Description" : "X location of first applied load center", "Default" : 0 * meter }
                     isLength(definition.applied1X, LENGTH_BOUNDS);
                 }
                 
             }
             
             annotation { "Name" : "Second applied load?", "Default" : false }
             definition.secondApplied is boolean;
             
             if (definition.secondApplied)
             {
                 annotation { "Group Name" : "Applied load 2", "Collapsed By Default" : false }
                 {
                     annotation { "Name" : "Applied load 2 Location Type", "UIHint" : UIHint.SHOW_LABEL }
                     definition.applied2LocationType is LocationType;
                     
                     annotation { "Name" : "Applied load 2 shape", "Description" : "Shape of the applied load" , "Default" : LoadType.POINT, "UIHint" : UIHint.SHOW_LABEL  }
                     definition.applied2LoadShape is LoadType;
                     
                     annotation { "Name" : "applied2NeedsWidth", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
                     definition.applied2NeedsWidth is boolean;
                     
                     if (definition.applied2NeedsWidth)
                     {
                         annotation { "Name" : "Applied load 2 width" }
                         isLength(definition.applied2Width, LENGTH_BOUNDS);
                     }
                     
                     annotation { "Name" : "applied2IsQuery", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN } // toggles query, x location visibility. 
                     definition.applied2IsQuery is boolean;
                     
                     if (definition.applied2IsQuery)
                     {
                         annotation { "Name" : "Applied load 2 query", "Description" : "Location of center of applied load 2", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
                         definition.applied2Query is Query;
                     }
                     else
                     {
                         annotation { "Name" : "Applied load 2 x location", "Description" : "X location of second applied load center", "Default" : 0 * meter }
                         isLength(definition.applied2X, LENGTH_BOUNDS);
                     }
                     
                 }
             }
             
             
             
         }
         
         annotation { "Group Name" : "Support load data", "Collapsed By Default" : false }
         {
            annotation { "Name" : "Add third support load", "Default" : false, "Description" : "When true, allows the addition of a third support load. A ratio of support loads between the 'standard 2' and the thrid needs to be established" }
            definition.addThirdSupport is boolean;
            
            if (definition.addThirdSupport)
            {
                annotation { "Name" : "Support load balance", "Description" : "The percentage of the support load borne by the third support load." }
                isReal(definition.supportLoadBalance, LoadBalanceBounds);
            }
            
            annotation { "Group Name" : "Support load 1", "Collapsed By Default" : false }
             {
                 annotation { "Name" : "Support load 1 Location Type", "UIHint" : UIHint.SHOW_LABEL }
                 definition.support1LocationType is LocationType;
                                     
                annotation { "Name" : "Support load 1 load shape", "Description" : "Shape of the support load" , "Default" : LoadType.POINT, "UIHint" : UIHint.SHOW_LABEL  }
                definition.support1LoadShape is LoadType;
                
                annotation { "Name" : "support1NeedsWidth", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
                definition.support1NeedsWidth is boolean;
                
                if (definition.support1NeedsWidth)
                {
                    annotation { "Name" : "Support load 1 width" }
                    isLength(definition.support1Width, LENGTH_BOUNDS);
                }
                 
                 annotation { "Name" : "support1IsQuery", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN } // toggles query, x location visibility. 
                 definition.support1IsQuery is boolean;
                 
                 if (definition.support1IsQuery)
                 {
                     annotation { "Name" : "Support load 1 query", "Description" : "Location of center of support 1 load", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
                     definition.support1Query is Query;
                 }
                 else
                 {
                     annotation { "Name" : "Support load 1 x location", "Description" : "X location of first support load center", "Default" : 0 * meter }
                     isLength(definition.support1X, LENGTH_BOUNDS);
                 }
                 
             }
             
             annotation { "Group Name" : "Support load 2", "Collapsed By Default" : false }
             {
                 annotation { "Name" : "Support load 2 Location Type", "UIHint" : UIHint.SHOW_LABEL }
                 definition.support2LocationType is LocationType;
                                     
                annotation { "Name" : "Support load 2 load shape", "Description" : "Shape of the support load" , "Default" : LoadType.POINT, "UIHint" : UIHint.SHOW_LABEL  }
                definition.support2LoadShape is LoadType;
                
                annotation { "Name" : "support2NeedsWidth", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
                definition.support2NeedsWidth is boolean;
                
                if (definition.support2NeedsWidth)
                {
                    annotation { "Name" : "Support load 2 width" }
                    isLength(definition.support2Width, LENGTH_BOUNDS);
                }
                 
                 annotation { "Name" : "support2IsQuery", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN } // toggles query, x location visibility. 
                 definition.support2IsQuery is boolean;
                 
                 if (definition.support2IsQuery)
                 {
                     annotation { "Name" : "Support load 2 query", "Description" : "Location of center of support 2 load", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
                     definition.support2Query is Query;
                 }
                 else
                 {
                     annotation { "Name" : "Support load 2 x location", "Description" : "X location of second support load center", "Default" : 0 * meter }
                     isLength(definition.support2X, LENGTH_BOUNDS);
                 }
                 
             }
             
             if (definition.addThirdSupport)
             {
                annotation { "Group Name" : "Support load 3", "Collapsed By Default" : false }
                {
                    annotation { "Name" : "Support load 3 Location Type", "UIHint" : UIHint.SHOW_LABEL }
                    definition.support3LocationType is LocationType;
                                         
                    annotation { "Name" : "Support load 3 load shape", "Description" : "Shape of the support load" , "Default" : LoadType.POINT, "UIHint" : UIHint.SHOW_LABEL  }
                    definition.support3LoadShape is LoadType;
                    
                    annotation { "Name" : "support3NeedsWidth", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
                    definition.support3NeedsWidth is boolean;
                    
                    if (definition.support3NeedsWidth)
                    {
                        annotation { "Name" : "Support load 3 width" }
                        isLength(definition.support3Width, LENGTH_BOUNDS);
                    }

                     annotation { "Name" : "support3IsQuery", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN } // toggles query, x location visibility.
                     definition.support3IsQuery is boolean;

                     if (definition.support3IsQuery)
                     {
                         annotation { "Name" : "Support load 3 query", "Description" : "Location of center of support 3 load", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
                         definition.support3Query is Query;
                     }
                     else
                     {
                         annotation { "Name" : "Support load 3 x location", "Description" : "X location of third support load center", "Default" : 0 * meter }
                         isLength(definition.support3X, LENGTH_BOUNDS);
                     }
                     
                 }   
             }
            
            
            
         }
         
         
         
         
         
     }
     {
        // --- 1. Extract EI data from selected edges ---
        var eiData = sampleEdgesForEI(context, definition.selEI, definition.numEvalPoints);
        if (size(eiData) < 2)
        {
            throw regenError("Need at least 2 EI sample points. Check EI edge selection.");
        }

        var N = definition.numEvalPoints;
        var xMin = eiData[0].x;
        var xMax = eiData[size(eiData) - 1].x;
        if (xMax - xMin < 1e-6 * meter)
        {
            throw regenError("EI profile has zero X range.");
        }
        println("=== estimateDeflection debug ===");
        println("EI samples: " ~ size(eiData) ~ ", xMin: " ~ (xMin / millimeter) ~ " mm, xMax: " ~ (xMax / millimeter) ~ " mm");
        println("EI[0]: " ~ (eiData[0].EI / (newton * meter * meter)) ~ " N*m^2 at x=" ~ (eiData[0].x / millimeter) ~ " mm");
        println("EI[last]: " ~ (eiData[size(eiData) - 1].EI / (newton * meter * meter)) ~ " N*m^2");

        // --- 2. Build uniform eval grid ---
        var dx = (xMax - xMin) / (N - 1);
        var x_eval = [];
        for (var i = 0; i < N; i += 1)
        {
            x_eval = append(x_eval, xMin + i * dx);
        }

        // --- 3. Resolve support X positions ---
        var xs1;
        if (definition.support1IsQuery)
        {
            xs1 = resolveQueryX(context, definition.support1Query);
        }
        else
        {
            xs1 = definition.support1X;
        }

        var xs2;
        if (definition.support2IsQuery)
        {
            xs2 = resolveQueryX(context, definition.support2Query);
        }
        else
        {
            xs2 = definition.support2X;
        }

        if (abs(xs2 - xs1) < 1e-6 * meter)
        {
            throw regenError("Support 1 and Support 2 must be at different X positions.");
        }

        var xs3 = 0 * meter;
        if (definition.addThirdSupport)
        {
            if (definition.support3IsQuery)
            {
                xs3 = resolveQueryX(context, definition.support3Query);
            }
            else
            {
                xs3 = definition.support3X;
            }
        }

        // --- 4. Resolve applied load X positions ---
        var x1;
        if (definition.applied1IsQuery)
        {
            x1 = resolveQueryX(context, definition.applied1Query);
        }
        else
        {
            x1 = definition.applied1X;
        }

        var x2 = 0 * meter;
        if (definition.secondApplied)
        {
            if (definition.applied2IsQuery)
            {
                x2 = resolveQueryX(context, definition.applied2Query);
            }
            else
            {
                x2 = definition.applied2X;
            }
        }

        println("xs1: " ~ (xs1 / millimeter) ~ " mm, xs2: " ~ (xs2 / millimeter) ~ " mm");
        println("x1: " ~ (x1 / millimeter) ~ " mm");

        // --- 5. Compute reactions by moment balance ---
        var F_total = definition.appliedLoad * newton;
        var F1;
        var F2;
        if (definition.secondApplied)
        {
            F1 = F_total * definition.appliedLoadBalance;
            F2 = F_total * (1 - definition.appliedLoadBalance);
        }
        else
        {
            F1 = F_total;
            F2 = 0 * newton;
        }

        var R1;
        var R2;
        var R3 = 0 * newton;
        if (definition.addThirdSupport)
        {
            R3 = F_total * definition.supportLoadBalance;
            var remainder = F_total - R3;
            var momentArm = F1 * (x1 - xs1) + F2 * (x2 - xs1) - R3 * (xs3 - xs1);
            R2 = momentArm / (xs2 - xs1);
            R1 = remainder - R2;
        }
        else
        {
            var momentArm = F1 * (x1 - xs1) + F2 * (x2 - xs1);
            R2 = momentArm / (xs2 - xs1);
            R1 = F_total - R2;
        }

        println("F_total: " ~ (F_total / newton) ~ " N, F1: " ~ (F1 / newton) ~ " N");
        println("R1: " ~ (R1 / newton) ~ " N, R2: " ~ (R2 / newton) ~ " N");

        // --- 6. Classify loads as distributed or point loads ---
        // distLoads: { "center", "force", "shape", "width", "sign" }  sign: +1=upward, -1=downward
        // pointLoads: { "x", "force" }  force: positive=upward
        var distLoads = [];
        var pointLoads = [];

        // Support 1
        if (definition.support1LoadShape == LoadType.POINT)
        {
            pointLoads = append(pointLoads, { "x" : xs1, "force" : R1 });
        }
        else
        {
            distLoads = append(distLoads, { "center" : xs1, "force" : R1,
                "shape" : definition.support1LoadShape, "width" : definition.support1Width, "sign" : 1 });
        }

        // Support 2
        if (definition.support2LoadShape == LoadType.POINT)
        {
            pointLoads = append(pointLoads, { "x" : xs2, "force" : R2 });
        }
        else
        {
            distLoads = append(distLoads, { "center" : xs2, "force" : R2,
                "shape" : definition.support2LoadShape, "width" : definition.support2Width, "sign" : 1 });
        }

        // Support 3 (optional)
        if (definition.addThirdSupport)
        {
            if (definition.support3LoadShape == LoadType.POINT)
            {
                pointLoads = append(pointLoads, { "x" : xs3, "force" : R3 });
            }
            else
            {
                distLoads = append(distLoads, { "center" : xs3, "force" : R3,
                    "shape" : definition.support3LoadShape, "width" : definition.support3Width, "sign" : 1 });
            }
        }

        // Applied load 1
        if (definition.applied1LoadShape == LoadType.POINT)
        {
            pointLoads = append(pointLoads, { "x" : x1, "force" : -F1 });
        }
        else
        {
            distLoads = append(distLoads, { "center" : x1, "force" : F1,
                "shape" : definition.applied1LoadShape, "width" : definition.applied1Width, "sign" : -1 });
        }

        // Applied load 2 (optional)
        if (definition.secondApplied)
        {
            if (definition.applied2LoadShape == LoadType.POINT)
            {
                pointLoads = append(pointLoads, { "x" : x2, "force" : -F2 });
            }
            else
            {
                distLoads = append(distLoads, { "center" : x2, "force" : F2,
                    "shape" : definition.applied2LoadShape, "width" : definition.applied2Width, "sign" : -1 });
            }
        }

        println("pointLoads: " ~ size(pointLoads) ~ ", distLoads: " ~ size(distLoads));

        // --- 7. Build distributed net load q_net at each eval point ---
        var q_net = [];
        for (var i = 0; i < N; i += 1)
        {
            var q = 0 * newton / meter;
            for (var dl in distLoads)
            {
                q = q + dl.sign * loadIntensityAt(x_eval[i], dl.center, dl.force, dl.shape, dl.width);
            }
            q_net = append(q_net, q);
        }

        // --- 8. Build shear V(x) by trapezoidal integration, then add point load jumps ---
        var V_arr = [];
        V_arr = append(V_arr, 0 * newton);
        for (var i = 1; i < N; i += 1)
        {
            var v_new = V_arr[i - 1] + (q_net[i - 1] + q_net[i]) / 2 * dx;
            V_arr = append(V_arr, v_new);
        }
        // Point load jumps: upward force (+) raises V for all x >= load location
        for (var pl in pointLoads)
        {
            for (var i = 0; i < N; i += 1)
            {
                if (x_eval[i] >= pl.x)
                {
                    V_arr[i] = V_arr[i] + pl.force;
                }
            }
        }

        // --- 9. Build moment M(x) by trapezoidal integration of V ---
        var M_arr = [];
        M_arr = append(M_arr, 0 * newton * meter);
        for (var i = 1; i < N; i += 1)
        {
            var m_new = M_arr[i - 1] + (V_arr[i - 1] + V_arr[i]) / 2 * dx;
            M_arr = append(M_arr, m_new);
        }

        println("V[0]: " ~ (V_arr[0] / newton) ~ " N, V[N-1]: " ~ (V_arr[N - 1] / newton) ~ " N");
        println("M[0]: " ~ (M_arr[0] / (newton * meter)) ~ " N*m, M[N-1]: " ~ (M_arr[N - 1] / (newton * meter)) ~ " N*m");

        // --- 10. Integrate curvature twice to get raw deflection ---
        // kappa = M / EI  [1/m];  theta = integral(kappa dx) [rad];  delta = integral(theta dx) [m]
        var EI_min = 1e-10 * newton * meter * meter;
        var kappa_arr = [];
        for (var i = 0; i < N; i += 1)
        {
            var EI_i = interpEI(eiData, x_eval[i]);
            if (EI_i < EI_min)
            {
                EI_i = EI_min;
            }
            kappa_arr = append(kappa_arr, M_arr[i] / EI_i);
        }

        // Integrate kappa [1/m] * dx [m] -> dimensionless slope theta
        var theta_arr = [];
        theta_arr = append(theta_arr, kappa_arr[0] * (0 * meter)); // 0 with correct dimensionless type
        for (var i = 1; i < N; i += 1)
        {
            var th_new = theta_arr[i - 1] + (kappa_arr[i - 1] + kappa_arr[i]) / 2 * dx;
            theta_arr = append(theta_arr, th_new);
        }

        // Integrate theta * dx [m] -> deflection [m]
        var delta_arr = [];
        delta_arr = append(delta_arr, 0 * meter);
        for (var i = 1; i < N; i += 1)
        {
            var d_new = delta_arr[i - 1] + (theta_arr[i - 1] + theta_arr[i]) / 2 * dx;
            delta_arr = append(delta_arr, d_new);
        }

        // --- 11. Apply zero-deflection BCs at outer supports ---
        // delta_corrected(x) = delta_raw(x) + C1*(x - x_eval[0]) + C2
        // Enforces delta_corrected = 0 at xs1 and xs2.
        var i1 = findNearestIndex(x_eval, xs1);
        var i2 = findNearestIndex(x_eval, xs2);
        var xSpan12 = x_eval[i2] - x_eval[i1];
        if (abs(xSpan12) < 1e-9 * meter)
        {
            throw regenError("Support positions map to the same eval grid node. Increase numEvalPoints or move supports.");
        }
        var C1 = -(delta_arr[i2] - delta_arr[i1]) / xSpan12;
        var C2 = -(delta_arr[i1] + C1 * (x_eval[i1] - x_eval[0]));

        println("BC indices: i1=" ~ i1 ~ ", i2=" ~ i2);
        println("delta_raw[i1]: " ~ (delta_arr[i1] / millimeter) ~ " mm, delta_raw[i2]: " ~ (delta_arr[i2] / millimeter) ~ " mm");
        println("C2: " ~ (C2 / millimeter) ~ " mm");

        var deflection = [];
        for (var i = 0; i < N; i += 1)
        {
            deflection = append(deflection, delta_arr[i] + C1 * (x_eval[i] - x_eval[0]) + C2);
        }
        println("deflection[0]: " ~ (deflection[0] / millimeter) ~ " mm");
        println("deflection[i1]: " ~ (deflection[i1] / millimeter) ~ " mm (should be ~0)");
        println("deflection[i2]: " ~ (deflection[i2] / millimeter) ~ " mm (should be ~0)");
        println("deflection[N-1]: " ~ (deflection[N - 1] / millimeter) ~ " mm");

        // --- 12. Create deflection spline curve (XZ plane, Z = deflection in meters) ---
        var pts = [];
        for (var i = 0; i < N; i += 1)
        {
            pts = append(pts, vector(x_eval[i], 0 * meter, deflection[i]));
        }
        opFitSpline(context, id + "deflCurve", { "points" : pts });
        setProperty(context, {
            "entities" : qCreatedBy(id + "deflCurve", EntityType.BODY),
            "propertyType" : PropertyType.NAME,
            "value" : "deflection_curve"
        });
     });
 

