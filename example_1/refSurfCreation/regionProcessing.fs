FeatureScript 2909;
import(path : "onshape/std/common.fs", version : "2909.0");

//import CurveWrapping_full/curveMappingCore
import(path : "08e8748f2ef24eea16072b75/5f4337fadd14407df808e982/683d867c35fdab9c98d47556", version : "2799b2b0c9a4314b90f6ba6f");

//import CurveWrapping_full/Utils
import(path : "08e8748f2ef24eea16072b75/5f4337fadd14407df808e982/ad98c7f43a25a4c0e8a428e7", version : "1efe2444c4d02973fe212fcc");


//import refSurfCore
import(path : "828cc4108f1c8683bc0e59cf", version : "3b549ecb97dfe5ff5a7a7bce");
//import pathProcessessing
import(path : "e9dd34f07820388a202cb620", version : "803c9b9c6666ac121062742e");



// ─── Region processing ────────────────────────────────────────────────────────

export function processRegions(context is Context, definition is map, refPath is map) returns array
{
    var processed = [];

    for (var i = 0; i < size(definition.regions); i += 1)
    {
        var region = definition.regions[i];
        region.regionNum = i;

        var tStart;
        var tEnd;

        if (region.extentDef == RegionExtentDef.ALONG_REF)
        {
            tStart = refPath.refParam + region.startX / refPath.totalLength;
            tEnd   = refPath.refParam + region.endX  / refPath.totalLength;
        }
        else // QUERY
        {
            var queryPts = evaluateQuery(context, qUnion([region.startPoint, region.endPoint]));
            if (size(queryPts) != 2)
                throw regenError("Region '" ~ region.name ~ "': extent query must resolve to exactly 2 points");

            var pt0 = getRefPoint(context, queryPts[0]);
            var pt1 = getRefPoint(context, queryPts[1]);

            var r0 = projectOntoFrenetPath(refPath.frenetPath, pt0, undefined);
            var r1 = projectOntoFrenetPath(refPath.frenetPath, pt1, undefined);

            tStart = min(r0.arcLength, r1.arcLength) / refPath.totalLength;
            tEnd   = max(r0.arcLength, r1.arcLength) / refPath.totalLength;
        }

        tStart = min(max(tStart, 0), 1);
        tEnd   = min(max(tEnd,   0), 1);
        if (tStart > tEnd)
        {
            var tmp = tStart;
            tStart = tEnd;
            tEnd = tmp;
        }

        region.tStart = tStart;
        region.tEnd   = tEnd;
        region.length = (tEnd - tStart) * refPath.totalLength;
        
        //get start and end frames. 
        var startArcLength = tStart * refPath.totalLength;
        var endArcLength = tEnd * refPath.totalLength;
        
        /*
        println('tStart - > ' ~ tStart);
        println('tEnd - > ' ~ tEnd);
        println('length - > ' ~ region.totalLength);
        println('startArcLength - > ' ~ startArcLength);
        println('endArcLength - > ' ~ endArcLength);
        println('totalLength -> ' ~ refPath.totalLength);
        println('keys(refPath) -> ' ~ keys(refPath));
        */
        region['startFrame'] = getFrameAtArcLength(context, refPath.frenetPath, startArcLength).frame;
        region['endFrame'] = getFrameAtArcLength(context, refPath.frenetPath, endArcLength).frame;
        /*
        println('startFrame - > ' ~ region['startFrame']);
        println('endFrame - > ' ~ region['endFrame']);
        */
        processed = append(processed, region);
    }

    return processed;
}


export function validateNoOverlap(context is Context, id is Id, regions is array)
{
    for (var i = 0; i < size(regions) - 1; i += 1)
    {
        for (var j = i + 1; j < size(regions); j += 1)
        {
            var a = regions[i];
            var b = regions[j];
            var overlapStart = max(a.tStart, b.tStart);
            var overlapEnd   = min(a.tEnd,   b.tEnd);
            if (overlapEnd > overlapStart + 1e-6)
            {
                reportFeatureWarning(context, id,
                    "Regions '" ~ a.regionName ~ "' and '" ~ b.regionName ~
                    "' overlap. Results may be unexpected.");
            }
        }
    }
}


export function sortRegionsByTStart(regions is array) returns array
{
    // Insertion sort (region counts are expected to be small)
    var sorted = regions;
    for (var i = 1; i < size(sorted); i += 1)
    {
        var key = sorted[i];
        var j   = i - 1;
        while (j >= 0 && sorted[j].tStart > key.tStart)
        {
            sorted[j + 1] = sorted[j];
            j -= 1;
        }
        sorted[j + 1] = key;
    }
    return sorted;
}