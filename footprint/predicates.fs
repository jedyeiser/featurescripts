FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

export predicate footprintDataPredicate(definition is map)
{
    annotation { "Group Name" : "Footprint Data", "Collapsed By Default" : false }
    {
        annotation { "Name" : "Sidecut dimensions:", "UIHint" : UIHint.READ_ONLY, "Description" : "Dimensions of ski if drawn with this sidecut. Note that unless both an AB and FB inflection point are found on the curve that this feature creates, these dimensions will change once tip/tail shapes are drawn"}
        definition.dimensionStr is string;
        
        annotation { "Name" : "Waist width", "UIHint" : UIHint.READ_ONLY, "Description" : "Minimum width of sidecut between widest points" }
        isLength(definition.foundWaistWidth, LENGTH_BOUNDS);
        
        annotation { "Name" : "Waist location", "UIHint" : UIHint.READ_ONLY, "Description" : "X-Coordinate of sidecut waist" }
        isLength(definition.foundWaistLocation, LENGTH_BOUNDS);
        
        annotation { "Name" : "Taper angle: ", "UIHint" : UIHint.READ_ONLY, "Description" : "Acute angle between the centerline of the ski and a line connecting the FB and AB widest points" }
        isAngle(definition.foundTaperAngle, ANGLE_360_BOUNDS);
        
        if (definition.hasTipTail)
        {
            annotation { "Name" : "Tip length", "UIHint": UIHint.READ_ONLY , "Description": "Length from the contact point to the extent of the ski"}
            isLength(definition.tipLength, LENGTH_BOUNDS);
            
            annotation { "Name" : "Tail length", "UIHint": UIHint.READ_ONLY, "Description": "Length from the contact point to the extent of the ski" }
            isLength(definition.tailLength, LENGTH_BOUNDS);
        }
        
        annotation { "Group Name" : "Radius calculations", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Average radius:", "Description" : "Average radius of sidecut, ignoring tapered sections (only averages where curvature is positive)", "UIHint" : UIHint.READ_ONLY }
            definition.avgRadiusStr is string;
            
            annotation { "Name" : "Natural radius - widest:", "Description" : "Radius of arc connecting widest FB and AB points at the specified waist width. NOTE: If a FB or AB inflection point is not found, this value may change once tip/tail shapes are drawn.", "UIHint" : UIHint.READ_ONLY }
            definition.natRadiusWidestStr is string;
            
            annotation { "Name" : "Natural radius - inflection:", "Description" : "Radius of arc connecting FB and AB inflection points at the specified waist width. NOTE: If a FB or AB inflection point is not found, this value may change once tip/tail shapes are drawn.", "UIHint" : UIHint.READ_ONLY }
            definition.natRadiusInflectionStr is string;
        }
        
        annotation { "Group Name" : "Widest & Inflection points", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Widest forebody point:", "UIHint": UIHint.READ_ONLY, "Description" : "Distance from forebody contact point to forebody widest point" }
            isLength(definition.fbWidest, LENGTH_BOUNDS);
            
            annotation { "Name" : "Forebody inflection point:", "UIHint": UIHint.READ_ONLY, "Description" : "Distance from forebody contact point to forebody inflection point" }
            isLength(definition.fbInflection, LENGTH_BOUNDS);
            
            annotation { "Name" : "Forebody inflection to widest:", "UIHint": UIHint.READ_ONLY }
            isLength(definition.fbInflectionToWidest, LENGTH_BOUNDS);
            
            annotation { "Name" : "Widest aftbody point:", "UIHint": UIHint.READ_ONLY, "Description" : "Distance from aftbody contact point to aftbody widest point" }
            isLength(definition.abWidest, LENGTH_BOUNDS);
            
            annotation { "Name" : "Aftbody inflection point:", "UIHint": UIHint.READ_ONLY, "Description" : "Distance from aftbody contact point to aftbody inflection point" }
            isLength(definition.abInflection, LENGTH_BOUNDS);
            
            annotation { "Name" : "Aftbody inflection to widest:", "UIHint": UIHint.READ_ONLY }
            isLength(definition.abInflectionToWidest, LENGTH_BOUNDS);
        }
    }
}

// =============================================================================
// TYPE CHECKING PREDICATES
// =============================================================================

/**
 * Validates curve data array structure.
 * Each element should have a BSplineCurve and x-bounds.
 */
export predicate isCurveDataArray(value)
{
    value is array;
    for (var item in value)
    {
        item is map;
        item.bspline is BSplineCurve;
        item.xMin is ValueWithUnits;
        item.xMax is ValueWithUnits;
    }
}

/**
 * Validates contact points structure (FCP/ACP/MRS).
 */
export predicate isContactPoints(value)
{
    value is map;
    value.fcpX is ValueWithUnits;
    value.acpX is ValueWithUnits;
    value.mrsX is ValueWithUnits;
}

/**
 * Validates footprint analysis result structure.
 */
export predicate isFootprintAnalysisResult(value)
{
    value is map;
    value.waist is map;
    value.waist.found is boolean;
    value.waist.x is ValueWithUnits;
    value.waist.width is ValueWithUnits;

    value.widestPoints is map;
    value.widestPoints.fbX is ValueWithUnits;
    value.widestPoints.abX is ValueWithUnits;
}