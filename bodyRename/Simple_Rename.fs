FeatureScript 2473;
import(path : "onshape/std/common.fs", version : "2473.0");

annotation { "Feature Type Name" : "Simple Body Rename", "Feature Type Description" : "Select Bodies to rename and provide a new name" }
export const myFeature = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Bodies to Rename", "Item name" : "Body to Rename",
                "Driven query" : "query", "Item label template" : "#renameString" }
        definition.renameArray is array;
        for (var body in definition.renameArray)
        {
            annotation { "Name" : "Body to rename", "Filter" : EntityType.BODY && (BodyType.SOLID || BodyType.SHEET || BodyType.WIRE), "MaxNumberOfPicks" : 1 , "UIHint" : UIHint.FOCUS_INNER_QUERY}
            body.query is Query;
            
            annotation { "Name" : "New Body Name"}
            body.renameString is string;
            
            annotation { "Name" : "Assign Attributes" }
            body.attributes is boolean;
            
            if (body.attributes)
            {
                annotation { "Name" : "Assign body type attribute?" }
                body.assignStdAttribute is boolean;
                
                annotation { "Name" : "Assign custom attribute?" }
                body.assignCustomAttribute is boolean;
                
                if (body.assignCustomAttribute)
                {
                    annotation { "Name" : "Attribute Name" }
                    body.attributeName is string;
                    
                    annotation { "Name" : "Attribute Value" }
                    body.attributeValue is string;
                    
                }
                
            }

            
            
            // More nested parameters defined here, as e.g. widget.myParameter
        }
        
    }
    {
        for (var renameBody in definition.renameArray)
        {
            var body = renameBody.query;
            setProperty(context, {
                    "entities" : body,
                    "propertyType" : PropertyType.NAME,
                    "value" : renameBody.renameString
            });
            if (renameBody.assignStdAttribute)
            {
                var isSolid = !isQueryEmpty(context, qBodyType(body, BodyType.SOLID));
                var isSheet = !isQueryEmpty(context, qBodyType(body, BodyType.SHEET));
                var isWire = !isQueryEmpty(context, qBodyType(body, BodyType.WIRE));
                
                if (isSolid)
                {
                    setAttribute(context, {
                            "entities" : body,
                            "name" : 'bodyName',
                            "attribute" : renameBody.renameString
                    });
                    
                }
                if (isSheet)
                {
                    setAttribute(context, {
                            "entities" : body,
                            "name" : 'surfName',
                            "attribute" : renameBody.renameString
                    });
                    
                }
                if (isWire)
                {
                    setAttribute(context, {
                            "entities" : body,
                            "name" : 'curveName',
                            "attribute" : renameBody.renameString
                    });
                    
                }
                
            }
            
            if (renameBody.assignCustomAttribute)
            {
                setAttribute(context, {
                        "entities" : body,
                        "name" : renameBody.attributeName,
                        "attribute" : renameBody.attributeValue
                });
            }
            
        }
    });
