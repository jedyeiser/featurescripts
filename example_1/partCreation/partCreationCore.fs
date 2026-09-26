FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

export enum PartRegion
{
    TOP,
    BOTTOM
}

export enum TopPartType
{
    EXPOSED,
    SHAPED, 
    STRIPE,
    BINDING_MAT,
}

export enum BottomPartType
{
    EXPOSED,
    SHAPED,
    STRIPE, 
    TIP_SHEAR,
    TAIL_SHEAR,
    EDGE_SHEAR
}

export enum RegionExtentType
{
    QUERY,
    ALONG_REF
}


export function createStandardPartEditingLogic(context is Context, id is Id,
    oldDefinition is map, definition is map, isCreating is boolean,
    specifiedParameters is map, hiddenBodies is Query) returns map
{
    //if we're creating a binding mat and it's full width, we need a sidewallPeripherySurface
    return definition;
}


annotation { "Feature Type Name" : "Create standard part", "Feature Type Description" : "Create multiple different standard part geometries", "Editing Logic Function" : "createStandardPartEditingLogic" }
export const createStandardPart = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Part type", "UIHint" : UIHint.HORIZONTAL_ENUM }
        definition.partRegion is PartRegion;
        
        if (definition.partRegion == PartRegion.TOP)
        {
            annotation { "Name" : "Top part type", "UIHint" : UIHint.SHOW_LABEL }
            definition.topPartType is TopPartType;
            
            annotation { "Name" : "Top surface ref", "Filter" : EntityType.BODY && BodyType.SHEET, "MaxNumberOfPicks" : 1 }
            definition.topSurfaceRef is Query;
            
            
        }
        else if (definition.partRegion == PartRegion.BOTTOM)
        {
            annotation { "Name" : "Bottom part type", "UIHint" : UIHint.SHOW_LABEL }
            definition.bottomPartType is BottomPartType;
            
            annotation { "Name" : "Bottom surface ref", "Filter" : EntityType.BODY && BodyType.SHEET, "MaxNumberOfPicks" : 1 }
            definition.bottomSurfaceRef is Query;
            
            
        }
        
        annotation { "Name" : "Outside surface", "Filter" : EntityType.BODY && BodyType.SHEET, "MaxNumberOfPicks" : 1 }
        definition.outsideSheet is Query;
        
        annotation { "Name" : "needsSidewallPeriphery", "Default" : false }
        definition.needsSidewallPeriphery is boolean;
        
        
        annotation { "Name" : "Ref. wire", "Filter" : EntityType.BODY && BodyType.WIRE, "MaxNumberOfPicks" : 1 }
        definition.refWire is Query;
        
        annotation { "Name" : "Ref. point", "Filter" : (EntityType.VERTEX) || (BodyType.MATE_CONNECTOR) || (GeometryType.PLANE), "MaxNumberOfPicks" : 1 }
        definition.refPoint is Query;
        
        
        annotation { "Group Name" : "Part definition", "Collapsed By Default" : false }
        {
            if ((definition.partRegion == PartRegion.TOP && definition.topPartType == TopPartType.STRIPE) || (definition.partRegion == PartRegion.BOTTOM && definition.bottomPartType == BottomPartType.STRIPE))
            {
                annotation { "Name" : "Stripe width" }
                isLength(definition.stripeWidth, LENGTH_BOUNDS);
                
                annotation { "Name" : "Stripe endpoints from", "Default" : RegionExtentType.ALONG_REF }
                definition.stripeEndpointSource is RegionExtentType;
                
                if (definition.stripeEndpointSource == RegionExtentType.QUERY)
                {
                    annotation { "Name" : "Start point", "Filter" : EntityType.VERTEX || GeometryType.PLANE || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
                    definition.stripeStartQuery is Query;
                    
                    annotation { "Name" : "End point", "Filter" : EntityType.VERTEX || GeometryType.PLANE || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
                    definition.stripeEndQuery is Query;
                       
                }
                
                else if (definition.stripeEndpointSource == RegionExtentType.ALONG_REF)
                {
                    annotation { "Name" : "Start point from ref" }
                    isLength(definition.stripeStartFromRef, LENGTH_BOUNDS);
                    
                    annotation { "Name" : "End point from ref" }
                    isLength(definition.stripeEndFromRef, LENGTH_BOUNDS);
                }
                
                annotation { "Name" : "Add start point extension", "Default" : false }
                definition.addStripeStartPointExtension is boolean;
                
                if (definition.addStripeStartPointExtension)
                {
                    annotation { "Name" : "Start point extension length" }
                    isLength(definition.addStripeStartPointExtensionLength, LENGTH_BOUNDS);
                    
                }
                
                annotation { "Name" : "Add end point extension", "Default" : false }
                definition.addStripeEndPointExtension is boolean;
                
                if (definition.addStripeStartPointExtension)
                {
                    annotation { "Name" : "End point extension length" }
                    isLength(definition.addStripeEndPointExtensionLength, LENGTH_BOUNDS);
                    
                }
                
                annotation { "Name" : "Fillet stripe corners", "Default" : false }
                definition.filletStripeCorners is boolean;
                
                if (definition.filletStripeCorners)
                {
                    annotation { "Name" : "Stripe fillet radius" }
                    isLength(definition.stripeFilletRadius, LENGTH_BOUNDS);
                    
                }
                
            }
            
            
        }
        
        
        
    }
    {
        // Define the function's action
    });
