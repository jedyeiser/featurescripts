FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// betterCurveTrim extends the existing OS Trim (OS_Trim.fs) to make curve trimming and extending more powerful. 
// 1. Allow trimming or splitting (which retains all geometry, but splits it). When splitting, optionally return a single WIRE with multiple curves after split. 
// 2. If splitting, allow multiple splits (queries), or evenly distributed splits (along curve length) between two queries
// 3. Allow splitting/trimming a distance along the curve (towards curve midpoint) from selected help point
// 4. Support splitting/trimming curve at inflection points. Solve for inflection points. In the event of multiple inflection points, create manipulators (opPoint) at each inflection. When a user selects a given manipulator point (opPoint), split or trim the curve here