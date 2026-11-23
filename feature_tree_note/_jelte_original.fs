FeatureScript 2796;
import(path : "onshape/std/feature.fs", version : "2796.0");
import(path : "onshape/std/featureList.fs", version : "2796.0");
import(path : "onshape/std/debug.fs", version : "2796.0");
ICON::import(path : "5db36f2dbd94dae497f2c12f", version : "e12a2e27652d4be6e377c94f");

annotation { "Feature Type Name" : "Feature tree note",
            "Feature Name Template" : "#subject",
            "UIHint" : UIHint.NO_PREVIEW_PROVIDED,
            "Tooltip Template" : "#subject: #description",
            "Icon" : ICON::BLOB_DATA,
            "Feature Type Description" : "This feature does nothing to the context or model, it just allows you to store and show some notes in the feature tree, like a READ ME note, and highlight referenced geometry" }
            
export const featureTreeNote = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Subject", "UIHint" : UIHint.VARIABLE_NAME, "MaxLength" : 256 }
        definition.subject is string;

        annotation { "Name" : "Description (shows on hover)", "MaxLength" : 512 }
        definition.description is string;
        
        annotation { "Name" : "More text here", "MaxLength" : 640000  }
        definition.text is string;
        
        annotation { "Name" : "Reference geometry", "Filter" : AllowMeshGeometry.YES && AllowFlattenedGeometry.YES, 
        "UIHint" : UIHint.PREVENT_CREATING_NEW_MATE_CONNECTORS }
        definition.geometry is Query;
        
        annotation { "Name" : "Reference features", "UIHint" : UIHint.ALLOW_FLAT_SKETCH_SELECTION }
        definition.features is FeatureList;
    }
    // body: 
    {    
        try silent
            {
                addDebugEntities(context, definition.geometry, DebugColor.YELLOW);
            
                setHighlightedEntities(context, { "entities": definition.geometry });
            }
    });
