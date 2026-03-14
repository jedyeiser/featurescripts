FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

//take a bottom surface, a periphery, and a group of solid bodies. 
//create the 'top surface looking down' on thse bodies. 

//start at top faces. Keep side faces if no intersections. Trim to intersections and boolean. Intersections become next seed faces, etc. 