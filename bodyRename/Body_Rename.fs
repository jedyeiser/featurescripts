FeatureScript 2345;
import(path : "onshape/std/common.fs", version : "2345.0");
export import(path : "onshape/std/query.fs", version : "2345.0");



export enum featureScope
{
    annotation {"Name" : "Query All Bodies"}
    ALL_BODIES,
    annotation {"Name" : "Select Bodies to Rename"}
    SELECT_BODIES
}

export enum nameType
{
    annotation {"Name" : "Find and Replace"}
    FIND_REPLACE,
    annotation {"Name" : "Assign Individual Names"}
    INDIVIDUAL
}



export function elFunction(context is Context, id is Id, oldDefinition is map, definition is map, isCreating is boolean, specifiedParameters is map ) returns map
{
    definition.allBodies =  qEverything(EntityType.BODY);//qBodyType(qEverything(EntityType.BODY), bodyType[definition.bodyTypeFilter]);
    var evAllBodies = evaluateQuery(context, definition.allBodies);
   
    var bodyArray = [];
    var bodyIndivArray = [];
    println('size(allBodies) = ' ~ size(evAllBodies));
    for (var i = 0; i < size(evAllBodies); i += 1)
    {
        var body = qNthElement(definition.allBodies, i);
        var bodyName = getProperty(context, {
                "entity" : body,
                "propertyType" : PropertyType.NAME
        });
        if (!(bodyName is undefined))
        {
            println('bodyName is not undefined');
            bodyArray = append(bodyArray, {'query' : body, 'bodyName' : bodyName}); 
            bodyIndivArray = append(bodyIndivArray, {'bodyQueryAllIndiv' : body, 'curName' : bodyName});
        }
        
        if (definition.searchType == featureScope.SELECT_BODIES)
        {
            definition.allBodiesSelected = false;
        }
        else if (definition.searchType == featureScope.ALL_BODIES)
        {
            definition.allBodiesSelected = true;   
        }
        
        if (definition.nameMethod == nameType.INDIVIDUAL)
        {
            definition.findAndReplace = false;   
        }
        else if (definition.nameMethod == nameType.FIND_REPLACE)
        {
            definition.findAndReplace = true;   
        }
    
    }
    
    definition.bodyArray = bodyArray;
    definition.bodyArrayIndividual = bodyIndivArray;
    
    return definition;
}

export function notEditingLogic(context is Context, id is Id, definition is map) returns map
{
    var retMap = definition;
    
    return retMap;
}

annotation { "Feature Type Name" : "Name Bodies" , "Editing Logic Function" : "elFunction"}
export const findReplaceName = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Search Type", "UIHint" : UIHint.SHOW_LABEL, "Default" : featureScope.ALL_BODIES}
        definition.searchType is featureScope;
        
        /*
        annotation { "Name" : "Body Type Filter", "UIHint" : UIHint.SHOW_LABEL}
        definition.bodyTypeFilter is BodyType;
        */
        
        annotation { "Name" : "Renaming Method", "UIHint" : UIHint.SHOW_LABEL, "Default" : nameType.FIND_REPLACE}
        definition.nameMethod is nameType;
        
        annotation { "Name" : "searchAllBodies", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : true }
        definition.allBodiesSelected is boolean;
        
        annotation { "Name" : "findAndReplace", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : true }
        definition.findAndReplace is boolean;
        
        if (definition.searchType == featureScope.ALL_BODIES)
        {
            if (definition.nameMethod == nameType.FIND_REPLACE)
            {
                annotation { "Name" : "Find String" }
                definition.findStringALL is string;
        
                annotation { "Name" : "Replace String" }
                definition.replaceStringALL is string;   
            }
            else if (definition.nameMethod == nameType.INDIVIDUAL)
            {
                annotation { "Name" : "Found Bodies to Assign Names to", "Item name" : "Found Body", "Item label template" : "#curName" }
                definition.bodyArrayIndividual is array;
                for (var bodyIndividual in definition.bodyArrayIndividual)
                {
                    annotation { "Name" : "Current Name", "UIHint" : UIHint.ALWAYS_HIDDEN}
                    bodyIndividual.curName is string;
                    
                    annotation { "Name" : "Body Query", "Filter" : EntityType.BODY, "MaxNumberOfPicks" : 1 , "UIHint" : UIHint.ALWAYS_HIDDEN}
                    bodyIndividual.bodyQueryAllIndiv is Query;
                    
                    annotation { "Name" : "New Body Name" }
                    bodyIndividual.newBodyName is string;
                } 
            }  
        }
        
        else if (definition.searchType == featureScope.SELECT_BODIES)
        {
            if (definition.nameMethod == nameType.FIND_REPLACE)
            {
                annotation { "Name" : "Find String" }
                definition.findStringIndividual is string;
                
                annotation { "Name" : "Replace String" }
                definition.replaceStringIndividual is string;
                
                annotation { "Name" : "Select Bodies to Rename [FIND AND REPLACE SUBSTRING]", "Item name" : "Body to Rename"}//, "Item label template" : "#curName" }
                definition.selectedBodiesReplace is array;
                for (var selBody in definition.selectedBodiesReplace)
                {
                    annotation { "Name" : "Body Query", "Filter" : EntityType.BODY, "MaxNumberOfPicks" : 1 }
                    selBody.bodyQuery is Query;

                }
            }
            else if (definition.nameMethod == nameType.INDIVIDUAL)
            {
                annotation { "Name" : "Select Bodies to Rename [SPECIFY INDIVIDUAL NAMES]", "Item name" : "Body to Rename"}//, "Item label template" : "#curName" }
                definition.selectedBodiesIndividual is array;
                for (var selBody in definition.selectedBodiesIndividual)
                {
                    annotation { "Name" : "Body Query", "Filter" : EntityType.BODY, "MaxNumberOfPicks" : 1 }
                    selBody.indvBodyQuery is Query;
                    
                    annotation { "Name" : "Set Body Name To" }
                    selBody.newName is string; 
                }
                
            }
            
        }
        
        annotation { "Name" : "allBodies", "Filter" : EntityType.BODY, "UIHint" : UIHint.ALWAYS_HIDDEN}
        definition.allBodies is Query;
        
        annotation { "Name" : "Bodies", "Item name" : "Body", "UIHint" : UIHint.ALWAYS_HIDDEN}
        definition.bodyArray is array;
        for (var body in definition.bodyArray)
        {
            // Nested parameters defined here, as e.g. widget.myParameter
            annotation { "Name" : "bodyQuery", "Filter" : EntityType.BODY, "MaxNumberOfPicks" : 1 }
            body.query is Query;
            
            annotation { "Name" : "bodyName" }
            body.bodyName is string;
            
        }
        
        
        
        
    }
    {
        // Define the function's action
        
        definition = notEditingLogic(context, id, definition);
        
        if (definition.searchType == featureScope.ALL_BODIES)
        {
            if (definition.nameMethod == nameType.FIND_REPLACE)
            {
                for (var bdy in definition.bodyArray)
                {
                    if (match(bdy.bodyName, ".*" ~ definition.findStringALL ~ ".*").hasMatch)
                    {
                        var newName = replace(bdy.bodyName, definition.findStringALL, definition.replaceStringALL);
                        println('newName = ' ~ newName);
                        
                        setProperty(context, {
                                "entities" : bdy.query,
                                "propertyType" : PropertyType.NAME,
                                "value" : toString(newName)
                        });
                        
                    }
                
                }
                
            }
            else if (definition.nameMethod == nameType.INDIVIDUAL)
            {
                for (var bdy in definition.bodyArrayIndividual)
                {
                    if (!(bdy.newBodyName == ''))
                    {
                        setProperty(context, {
                                "entities" : bdy.bodyQueryAllIndiv,
                                "propertyType" : PropertyType.NAME,
                                "value" : bdy.newBodyName
                        });   
                    }
                    
                }
                
            }
            
            
        }
        else if (definition.searchType == featureScope.SELECT_BODIES)
        {
            if (definition.nameMethod == nameType.INDIVIDUAL)
            {

                for (var selBody in definition.selectedBodiesIndividual)
                {
                    setProperty(context, {
                            "entities" : selBody.indvBodyQuery,
                            "propertyType" : PropertyType.NAME,
                            "value" : selBody.newName
                    });  
                }
                
            }
            else if (definition.nameMethod == nameType.FIND_REPLACE)
            {
                for (var selBody in definition.selectedBodiesReplace)
                {
                    var curName = getBodyName(context, id, selBody.bodyQuery, definition.bodyArray);
                    var newName = replace(curName, definition.findStringIndividual, definition.replaceStringIndividual);
                    setProperty(context, {
                            "entities" : selBody.bodyQuery,
                            "propertyType" : PropertyType.NAME,
                            "value" : newName
                    });
                }
       
            }
            
        }
        

        
    });
    
export function getBodyName(context is Context, id is Id, searchQuery is Query, nameArray is array) returns string
{
    var retString = '';
    for (var bodyMap in nameArray)
    {
        if (bodyMap.query == searchQuery) // I'm pretty sure this is just checking to see if the queries are equal, not if they evaluate to the same body?
        {
            retString = bodyMap.bodyName;   
        }
        
    }
    
    return retString;
}
