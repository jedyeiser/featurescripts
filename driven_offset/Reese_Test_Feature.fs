FeatureScript 3044;
import(path : "onshape/std/common.fs", version : "3044.0");
import(path : "12312312345abcabcabcdeff/e11b9ebdf448b3ebc502b5dc/2f3802712bd8620b3f97f4d3", version : "8b17c3b22e2728a046f51b0e");


annotation { "Feature Type Name" : "Var Extraction Test", "Feature Type Description" : "" }
export const varExtractionTest = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        // Define the parameters of the feature type
    }
    {
        var everything = qEverything(EntityType.BODY);
        
        setQueryVariable(context, "@" ~ toString(id) ~ "everything", everything);
        
        setVariable(context, "everythingTest", everything);
    });


annotation { "Feature Type Name" : "Query Extraction Test", "Feature Type Description" : "" }
export const queryExtraction = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        // Define the parameters of the feature type
    }
    {
        setQueryVariable(context, "everythingWorked", getVariable(context, "everythingTest"));
    });
