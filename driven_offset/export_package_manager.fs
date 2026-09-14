FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

/**
 * 
 * This contains two functions that create and derive 'packages'. This extends the idea Greg Brown (Onshape VP of Product) laid out with his publish feature: https://k2-sports.onshape.com/documents/40d43cad542dccfa4772d7e1/v/856bd2b631f7e35c4a2b34ad/e/788996d08647863b81b2ff61
 * 
 * The prevailing goal of this effort is to make transfering information/data from one part studio as robust, dynamic and flexible as possible. 
 * The built-in Derive does not support query variables. One key differentiator of our export/package functionality is that we WILL 'create' or 'persist' query variables,
 * meaning that data set in the document being exported can be used in the imported/derived context
 * 
 * A package is a collection of bodies, sketches, mate connectors, composite parts and query variables. 
 * 
 * Creating a package creates a composite part with any additional data set as an attribute on the created composite part for extraction when being imported. 
 * 
 * Sketch section - User selects sketches to bring into the new context. 
 * Body Section - User selects bodies to bring into new context
 * Mate Connector Section - User selects mate connectors to bring into new context. Note that we may need to deal with special cases (mate connector has no owner body. Mate connector owner body is not part of export
 * Query Variable Section - User selects/creates query variables to bring into and use in the new context
 *  Existing - User selects existing query variables (query input parameter). These are recorded by name and brought into the new context (if possible)
 *  New - Array variable where user creates new query variables using a query variable predicate. 
 * Notes - Any export notes
 * 
 * 
 * Deriving a package tests to see if a composite part with the required attributes is selected. Should be have like a standard Derive otherwise (when no composite part of the right type) is selected
 * If a composite part of the right type is selected, bring in export data (more than just the composite part - we have sketches and query variables). Show debug shows variables. Debug color code query variables. 
 * 
 * 
 */