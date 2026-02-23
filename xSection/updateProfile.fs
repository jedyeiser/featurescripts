FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

/**
 * This function is used to make informed decisions about what a ski or 
 * snowboard thickness profile SHOULD be to achieve a provided ei proifle. 
 * 
 * Much data will be given in the form of edges. We can assume that these EI curves will be in the XZ plane and can
 * convert from 'world/geometry space' to get the EI value by converting the world Z value in mm to 1Nm^2
 * 
 * Much of the analysis here is done prior with a cross section analysis. This 
 * feature relies on significicant functionality built by other features/files.
 * 
 * This feature therefore depends on the stability of those outputs. 
 * 
 * User provides a query for target EI edges.
 * 
 * User provides a feature reference to get the ID/Name of significant geometry and material prep work
 * 
 * User selects either INHERIT or QUERY to specify if we should treat the EI profile from
 * the feature as our measured EI profile, or if we would like to specify a query for our measured EI profile. 
 * 
 * User selectes either INHERIT or QUERY to specify if we should treat the thickness profile calculated
 * by the preceeding feature as our measured thickness profile, or if we should provide an edge query 
 * for the measured thickness. 
 * 
 * We now use this data to generate an updated profile guess or estimate to achieve our EI target. We have three basic modes. 
 * STD, DELTA, PERCENT that define how we will calculate our updated thickness profile. While it may be STD, these are the methods that 
 * will be the most complicated. 
 * 
 * The delta under consideration here is the difference between our target and measured stiffness profiles at each relevant X location from the 
 * preceeding feature. 
 * 
 * When STD is selected, a boolean appears - scaleDelta. When selected the user can apply a scalefactor delta (1, or 100% is the default). 
 * STD is going to use the detailed geometery and material data for our cross sections to hit a 'new' EI curve. 
 * In general, we asume the 'new target EI curve' to be equal to the measured EI Profile plus 100% of the delta. If we only want to go half the distance, we can 
 * do so by specifying the scalefactor. 
 * 
 * We can apply a very simple rule to 
 * control our solver can alter geometry efficently to solve for a stiffness - if the point is above the neutral axis, it moves up with height change. 
 * If a point is below the neutral axis, its height does not change with changes to the profile height. 
 * 
 * If either DELTA or PERCENT is selected, we're going to rely on some simple calculations. That is that we can fairly reasonably
 * capture how changes to profile affect changes in stiffness by saying EI(x)/b(x) = alpha * t ^ Beta. Solve for these, Our feature uses
 * data from the inherited feature to solve for these. 
 * 
 * If DELTA is selected, user specifies a percentage of the delta to apply, similar to STD
 * We solve for the new profile height at each cross section by using it's width and finding the t such that
 * the new EI = measuredEI + Delta * scalefactor using our EI/b = alpha * t ^ beta model. 
 * 
 * Ironically, if PERCENT is selected, we do not provide a percentage. 
 * Rather, for each cross section we calculate the percentage difference between the measured and 
 * target EI profiles. We specify that t_new^Beta = %change * t_old^Beta.
 * 
 * User provides standard spline approximation parameters, and solved profile is approximated to those parameters. 
 * 
 * User can provide a string to name the output profile. 
 * 
 * 
 */
 
 export enum DataInheretenceType 
 {
     INHERIT, 
     QUERY
     }
     
 export enum SolverType
 {
     STD,
     DELTA,
     PERCENT
 }
 
 export function updateProfileEditLogic(context is Context, id is Id, oldDefinition is map,
    definition is map, isCreating is boolean, specifiedParameters is map, clickedButton is string) returns map
{
    println("estimateDeflectionEditLogic called");
    
    if ( clickedButton == "recalculate" || (!oldDefinition.recalculate && definition.recalculate))
    {
        println('buttonClicked');
    }
    
    return definition;
}
 
 
 export const DeltaPercentBounds = {(unitless) : [0.001, 1, 1]} as RealBoundSpec;
 
 annotation { 
     "Feature Type Name" : "Update profile", 
     "Feature Type Description" : "Uses input data to make informed decisions about what the thickness profile of a ski/snowboard should be to achieve an EI Target profile",
     "Editing Logic Function" : "updateProfileEditLogic"
     }
 export const updateProfile = defineFeature(function(context is Context, id is Id, definition is map)
     precondition
     {
        annotation { "Name" : "Solver type", "UIHint" : UIHint.HORIZONTAL_ENUM, "Default" : SolverType.STD }
        definition.solverType is SolverType;
        
        annotation { "Name" : "Cross section feature", "MaxNumberOfPicks" : 1 }
        definition.xSectFeature is FeatureList;

        annotation { "Name" : "Target EI profile", "Filter" : EntityType.EDGE, "Description" : "Edges representing the target EI profile. World Z in mm = EI in N·m²" }
        definition.targetEIQuery is Query;

        annotation { "Name" : "Measured EI from", "Default" : DataInheretenceType.INHERIT, "Decription" : "Specifies if we should treat our modeled EI as our measured EI" , "UIHint" : UIHint.SHOW_LABEL }
        definition.measuredEIType is DataInheretenceType;
        
        annotation { "Name" : "provideMeasuredEI", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : false }
        definition.provideMeasuredEI is boolean;
        
        if (definition.provideMeasuredEI)
        {
            annotation { "Name" : "Measured EI edges", "Filter" : EntityType.EDGE}
            definition.measuredEIQuery is Query;
        }
        
        annotation { "Name" : "Measured thickness from", "Default" : DataInheretenceType.INHERIT, "UIHint" : UIHint.SHOW_LABEL, "Decription" : "Specifies if we should assume the measured ski had the correct (theoretical) profile, or if we're going to provide a measured profile" }
        definition.measuredThicknessType is DataInheretenceType;
        
        annotation { "Name" : "provideMeasuredThickness", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.provideMeasuredThickness is boolean;
        
        if (definition.provideMeasuredThickness)
        {
            annotation { "Name" : "Measured thickness edges", "Filter" : EntityType.EDGE}
            definition.measuredThicknessQuery is Query;
               
        }
        
        annotation { "Name" : "showCalcs", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : false, "Descrption" : "true when definition.solverType == solverType.DELTA or solverType.PERCENT" }
        definition.showCalcs is boolean;
        
        if (definition.showCalcs)
        {
            annotation { "Name" : "Override percentage delta" }
            definition.overridePercentageDelta is boolean;
            
            if (definition.overridePercentageDelta) // no matter what the applyDeltaPercentage is, if overridePercentageDelta is false, use 1 as the applyPercentageDelta. 
            {
                annotation { "Name" : "Apply percentage to delta" }
                isReal(definition.applyDeltaPercentage, DeltaPercentBounds);
            }
            
            
               
        }
        
        annotation { "Name" : "FCP", "Filter" : (EntityType.FACE && GeometryType.PLANE) || (EntityType.VERTEX) || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
        definition.fcpQiery is Query;

        annotation { "Name" : "ACP", "Filter" : (EntityType.FACE && GeometryType.PLANE) || (EntityType.VERTEX) || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
        definition.acpQiery is Query;
        
        annotation { "Name" : "stiffnessAvailable", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN, "Description" : "When true, show calculated stiffness data. gets triggered to true in editing logic when both definition.acpQuery and definition.fcpQuery are valid" }
        definition.stiffnessDataAvailable is boolean;
        
        if (definition.stiffnessDataAvailable)
        {
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
        }
        
        if (definition.showCalcs)
        {
            annotation { "Group Name" : "Width, thickness, stiffness data", "Collapsed By Default" : true }
            {
                annotation { "Name" : "Alpha", "Description" : "coefficent in EI/b = alpha * t ^ beta", "UIHint" : UIHint.READ_ONLY }
                isReal(definition.calcAlpha, POSITIVE_REAL_BOUNDS);
                
                annotation { "Name" : "Beta", "Description" : "exponent in EI/b = alpha * t ^ beta", "UIHint" : UIHint.READ_ONLY }
                isReal(definition.calcBeta, POSITIVE_REAL_BOUNDS);
                
            }
            
        }
        

        annotation { "Name" : "Recalculate" }
        isButton(definition.recalculate);
        
        
        
     }
     {
         var oldID = keys(definition.xSectFeature)[0][0];

        var allEIData = getAttribute(context, {
                "entity" : qOrigin(EntityType.BODY),
                "name" : "CrossSectionAnalysis"
        });

        var crossSectionData = allEIData[(oldID)];
        var crossSectionDetails = crossSectionData.details;
        var crossSectionTableData = crossSectionData.tableData;

        var bodies = crossSectionDetails.bodies;
        var crossSections = crossSectionDetails.crossSections;
        var numSections = size(crossSections);
     });
 