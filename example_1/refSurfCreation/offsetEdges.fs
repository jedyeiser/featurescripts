FeatureScript 2909;
import(path : "onshape/std/common.fs", version : "2909.0");
export import(path : "onshape/std/geometriccontinuity.gen.fs", version : "2909.0");

//import CurveWrapping_full/curveMappingCore
import(path : "08e8748f2ef24eea16072b75/34dde8fbf0531890b902d1d5/683d867c35fdab9c98d47556", version : "08ced7a9bfddd7090ead6e97");
//import CurveWrapping_full/Utils
import(path : "08e8748f2ef24eea16072b75/34dde8fbf0531890b902d1d5/ad98c7f43a25a4c0e8a428e7", version : "af176e222f5dedf312114187");


// ─── Enums ────────────────────────────────────────────────────────────────────

export enum RegionType
{
    LINEAR,
    QUADRATIC,
    SMOOTH
}

export enum RegionExtentType
{
    QUERY,
    X_EXTENTS
}

export enum QuadraticZeroSlope
{
    AT_START,
    AT_END
}

export enum OffsetType
{
    annotation { "Name" : "Normal" }
    NORMAL,
    annotation { "Name" : "Binormal" }
    BINORMAL,
    annotation { "Name" : "Both" }
    BOTH
}

export enum VaryingArcMode
{
    annotation { "Name" : "Convert to spline" }
    SPLINE,
    annotation { "Name" : "Best-fit biarc" }
    BIARC
}

export enum OffsetMode
{
    annotation { "Name" : "Multiple regions" }
    MULTI_REGION,
    annotation { "Name" : "Single region" }
    SINGLE_REGION
}


// ─── Bounds ───────────────────────────────────────────────────────────────────

export const RegionPointsBounds    = {(unitless)   : [20, 50, 100]}        as IntegerBoundSpec;
export const OffsetBounds          = {(millimeter) : [-100, 0, 100]}       as LengthBoundSpec;
export const DelayBounds           = {(millimeter) : [0, 0, 1000]}         as LengthBoundSpec;
export const ApproxToleranceBounds = {(millimeter) : [0.001, 0.01, 1]}     as LengthBoundSpec;
export const ApproxDegreeBounds    = {(unitless)   : [2, 3, 5]}            as IntegerBoundSpec;
export const ApproxMaxCPBounds     = {(unitless)   : [10, 100, 500]}       as IntegerBoundSpec;


// ─── Editing logic ────────────────────────────────────────────────────────────

export function generateOffsetEdgesEditingLogic(context is Context, id is Id,
    oldDefinition is map, definition is map, isCreating is boolean,
    specifiedParameters is map, hiddenBodies is Query) returns map
{
    // Single-region mode has no regions/intersections arrays to auto-manage.
    if (definition.offsetMode == OffsetMode.SINGLE_REGION)
    {
        return definition;
    }

    var pathInfo = undefined;
    try silent
    {
        pathInfo = processPath(context, id + "elPath", definition);
    }
    if (pathInfo == undefined)
        return definition;

    // Auto-name regions
    var namedRegions = [];
    for (var i = 0; i < size(definition.regions); i += 1)
    {
        var reg = definition.regions[i];
        if (reg.regionName == "" || reg.regionName == undefined)
            reg.regionName = "Region " ~ toString(i + 1);
        namedRegions = append(namedRegions, reg);
    }
    definition.regions = namedRegions;

    var sortedRegions = [];
    try silent
    {
        var processed = processRegions(context, definition, pathInfo);

        var newRegions = [];
        for (var i = 0; i < size(definition.regions); i += 1)
        {
            var reg = definition.regions[i];
            for (var pr in processed)
            {
                if (pr.regionNum == i)
                {
                    reg.length = pr.length;
                    break;
                }
            }
            newRegions = append(newRegions, reg);
        }
        definition.regions = newRegions;
        sortedRegions = sortRegionsByTStart(processed);
    }

    // Rebuild intersections for all consecutive sorted region pairs
    var newIntersections = [];
    for (var i = 0; i < size(sortedRegions) - 1; i += 1)
    {
        var regA = sortedRegions[i];
        var regB = sortedRegions[i + 1];

        var entry = {
            "isValid"         : true,
            "intersectionNum" : i + 1,
            "region1"         : regA.regionName,
            "region2"         : regB.regionName,
            "blend"           : true,
            "startContinuity" : GeometricContinuity.G1,
            "startDist"       : 10 * millimeter,
            "endContinuity"   : GeometricContinuity.G1,
            "endDist"         : 10 * millimeter
        };

        for (var existing in definition.intersections)
        {
            if (existing.region1 == regA.regionName && existing.region2 == regB.regionName)
            {
                entry = mergeMaps(entry, existing);
                entry.isValid         = true;
                entry.intersectionNum = i + 1;
                break;
            }
        }
        newIntersections = append(newIntersections, entry);
    }
    definition.intersections = newIntersections;

    return definition;
}


// ─── Feature ──────────────────────────────────────────────────────────────────

annotation { "Feature Type Name" : "Offset edges",
             "Feature Type Description" : "Offsets a G1-continuous edge chain by per-region normal and binormal amounts in the Frenet frame",
             "Editing Logic Function" : "generateOffsetEdgesEditingLogic" }
export const offsetEdges = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Reference edges",
                     "Filter" : (EntityType.BODY && BodyType.WIRE) || EntityType.EDGE,
                     "Description" : "Edges to offset from. Must form a G1-continuous path" }
        definition.userSelection is Query;

        annotation { "Name" : "Reference point",
                     "Filter" : EntityType.VERTEX || GeometryType.PLANE || BodyType.MATE_CONNECTOR,
                     "MaxNumberOfPicks" : 1,
                     "Description" : "Defines X = 0 along the path" }
        definition.referencePoint is Query;

        annotation { "Name" : "Flip direction", "Default" : false, "UIHint" : UIHint.OPPOSITE_DIRECTION,
                     "Description" : "Reverses the traversal direction of the reference path" }
        definition.flipDirection is boolean;

        annotation { "Name" : "Show direction indicator", "Default" : true,
                     "Description" : "Draws arrows at the reference point so you can see traversal direction before setting offsets: GREEN = + (positive region start), RED = - (behind the reference point). Follows Flip direction." }
        definition.showDirection is boolean;

        annotation { "Name" : "Flip normal", "Default" : false,
                     "Description" : "Inverts the Frenet normal direction" }
        definition.flipNormal is boolean;

        annotation { "Name" : "Flip binormal", "Default" : false,
                     "Description" : "Inverts the Frenet binormal direction" }
        definition.flipBinormal is boolean;

        annotation { "Name" : "Varying-offset arc handling", "Default" : VaryingArcMode.SPLINE,
                     "UIHint" : UIHint.HORIZONTAL_ENUM,
                     "Description" : "For a circular source edge whose offset VARIES along it: 'Convert to spline' approximates it as a best-fit BSpline (default); 'Best-fit biarc' emits two tangent arcs that match the endpoint offsets exactly and stay tangent to the neighboring curves on both sides. Constant-offset arcs are always kept as exact arcs regardless of this setting." }
        definition.arcMode is VaryingArcMode;

        annotation { "Name" : "Sampling density",
                     "Description" : "Number of points evaluated per region (and per blend)" }
        isInteger(definition.numRegionPoints, RegionPointsBounds);

        annotation { "Name" : "Offset layout", "Default" : OffsetMode.MULTI_REGION,
                     "UIHint" : UIHint.HORIZONTAL_ENUM,
                     "Description" : "Multiple regions: define separate zones and join them with blends. Single region: one continuous run whose profile flows through a list of interior offsets (cleaner for a simple shaped sweep)." }
        definition.offsetMode is OffsetMode;

        // Gate on != SINGLE_REGION (not == MULTI_REGION) so a legacy instance whose offsetMode
        // resolves to undefined still shows its regions rather than hiding everything.
        if (definition.offsetMode != OffsetMode.SINGLE_REGION)
        {

        annotation { "Name" : "Regions", "Item name" : "Region",
                     "Item label template" : "#regionName",
                     "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS }
        definition.regions is array;
        for (var region in definition.regions)
        {
            annotation { "Name" : "Region type", "Default" : RegionType.LINEAR,
                         "UIHint" : UIHint.HORIZONTAL_ENUM }
            region.regionType is RegionType;

            if (region.regionType == RegionType.QUADRATIC)
            {
                annotation { "Name" : "Zero slope at", "Default" : QuadraticZeroSlope.AT_START }
                region.quadZeroSlope is QuadraticZeroSlope;
            }

            annotation { "Name" : "Region number", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isInteger(region.regionNum, POSITIVE_COUNT_BOUNDS);

            annotation { "Name" : "Region name" }
            region.regionName is string;

            annotation { "Name" : "Extent type", "Default" : RegionExtentType.X_EXTENTS }
            region.extentType is RegionExtentType;

            if (region.extentType == RegionExtentType.QUERY)
            {
                annotation { "Name" : "Extent points",
                             "Filter" : EntityType.VERTEX || GeometryType.PLANE || BodyType.MATE_CONNECTOR,
                             "MaxNumberOfPicks" : 2 }
                region.extentQueries is Query;
            }

            if (region.extentType == RegionExtentType.X_EXTENTS)
            {
                annotation { "Name" : "Region start",
                             "Description" : "Distance along path from reference point (negative = behind reference)" }
                isLength(region.regionStart, LENGTH_BOUNDS);

                annotation { "Name" : "Region end",
                             "Description" : "Distance along path from reference point" }
                isLength(region.regionEnd, LENGTH_BOUNDS);
            }

            annotation { "Name" : "Offset type", "Default" : OffsetType.NORMAL,
                         "UIHint" : UIHint.HORIZONTAL_ENUM,
                         "Description" : "Which Frenet offsets this region applies (hides the unused fields)" }
            region.offsetType is OffsetType;

            if (region.offsetType == OffsetType.NORMAL || region.offsetType == OffsetType.BOTH)
            {
                annotation { "Name" : "Start normal offset",
                             "Description" : "Frenet normal offset at the region start" }
                isLength(region.startNormalOffset, OffsetBounds);

                annotation { "Name" : "End normal offset",
                             "Description" : "Frenet normal offset at the region end" }
                isLength(region.endNormalOffset, OffsetBounds);
            }

            if (region.offsetType == OffsetType.BINORMAL || region.offsetType == OffsetType.BOTH)
            {
                annotation { "Name" : "Start binormal offset",
                             "Description" : "Frenet binormal offset at the region start" }
                isLength(region.startBinormalOffset, OffsetBounds);

                annotation { "Name" : "End binormal offset",
                             "Description" : "Frenet binormal offset at the region end" }
                isLength(region.endBinormalOffset, OffsetBounds);
            }

            annotation { "Name" : "Start dwell",
                         "Description" : "Distance in from the region start over which the start offset is held constant before the profile ramps (0 = no dwell)" }
            isLength(region.startDelay, DelayBounds);

            annotation { "Name" : "End dwell",
                         "Description" : "Distance in from the region end over which the end offset is held constant (0 = no dwell)" }
            isLength(region.endDelay, DelayBounds);

            annotation { "Name" : "Region length", "UIHint" : UIHint.READ_ONLY }
            isLength(region.length, LENGTH_BOUNDS);
        }

        annotation { "Name" : "Intersections", "Item name" : "Intersection",
                     "Item label template" : "#intersectionNum",
                     "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS }
        definition.intersections is array;
        for (var intersection in definition.intersections)
        {
            annotation { "Name" : "Is valid", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
            intersection.isValid is boolean;

            annotation { "Name" : "Intersection number", "UIHint" : UIHint.READ_ONLY }
            isInteger(intersection.intersectionNum, POSITIVE_COUNT_BOUNDS);

            annotation { "Name" : "Region 1", "UIHint" : UIHint.READ_ONLY }
            intersection.region1 is string;

            annotation { "Name" : "Region 2", "UIHint" : UIHint.READ_ONLY }
            intersection.region2 is string;

            annotation { "Name" : "Blend regions?", "Default" : true }
            intersection.blend is boolean;

            if (intersection.blend)
            {
                annotation { "Name" : "Start continuity", "Default" : GeometricContinuity.G1, "UIHint" : UIHint.SHOW_LABEL }
                intersection.startContinuity is GeometricContinuity;

                annotation { "Name" : "Start distance",
                             "Description" : "How far the blend reaches back into the first region from its endpoint (0 = connect at endpoint)" }
                isLength(intersection.startDist, LENGTH_BOUNDS);

                annotation { "Name" : "End continuity", "Default" : GeometricContinuity.G1, "UIHint" : UIHint.SHOW_LABEL }
                intersection.endContinuity is GeometricContinuity;

                annotation { "Name" : "End distance",
                             "Description" : "How far the blend reaches into the second region from its start (0 = connect at endpoint)" }
                isLength(intersection.endDist, LENGTH_BOUNDS);
            }
        }
        }

        if (definition.offsetMode == OffsetMode.SINGLE_REGION)
        {
            annotation { "Name" : "Transfer type", "Default" : RegionType.SMOOTH,
                         "UIHint" : UIHint.HORIZONTAL_ENUM,
                         "Description" : "How the offset moves between consecutive interior offsets" }
            definition.singleTransfer is RegionType;

            if (definition.singleTransfer == RegionType.QUADRATIC)
            {
                annotation { "Name" : "Zero slope at", "Default" : QuadraticZeroSlope.AT_START }
                definition.singleQuadZeroSlope is QuadraticZeroSlope;
            }

            annotation { "Name" : "Interior offsets", "Item name" : "Offset",
                         "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS,
                         "Description" : "Ordered offset control points along the path. The profile flows through them with the transfer type and holds flat beyond the first and last." }
            definition.interiorOffsets is array;
            for (var off in definition.interiorOffsets)
            {
                annotation { "Name" : "Location type", "Default" : RegionExtentType.X_EXTENTS,
                             "UIHint" : UIHint.HORIZONTAL_ENUM }
                off.locationType is RegionExtentType;

                if (off.locationType == RegionExtentType.X_EXTENTS)
                {
                    annotation { "Name" : "Position",
                                 "Description" : "Distance along path from reference point (negative = behind reference)" }
                    isLength(off.position, LENGTH_BOUNDS);
                }
                if (off.locationType == RegionExtentType.QUERY)
                {
                    annotation { "Name" : "Location point",
                                 "Filter" : EntityType.VERTEX || GeometryType.PLANE || BodyType.MATE_CONNECTOR,
                                 "MaxNumberOfPicks" : 1 }
                    off.locationQuery is Query;
                }

                annotation { "Name" : "Offset type", "Default" : OffsetType.NORMAL,
                             "UIHint" : UIHint.HORIZONTAL_ENUM,
                             "Description" : "Which Frenet offsets this control point pins" }
                off.offsetType is OffsetType;

                if (off.offsetType == OffsetType.NORMAL || off.offsetType == OffsetType.BOTH)
                {
                    annotation { "Name" : "Normal offset" }
                    isLength(off.normalOffset, OffsetBounds);
                }
                if (off.offsetType == OffsetType.BINORMAL || off.offsetType == OffsetType.BOTH)
                {
                    annotation { "Name" : "Binormal offset" }
                    isLength(off.binormalOffset, OffsetBounds);
                }
            }
        }

        annotation { "Group Name" : "Approximation", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Spline degree" }
            isInteger(definition.approxDegree, ApproxDegreeBounds);

            annotation { "Name" : "Approximation tolerance" }
            isLength(definition.approxTolerance, ApproxToleranceBounds);

            annotation { "Name" : "Max control points" }
            isInteger(definition.approxMaxCP, ApproxMaxCPBounds);
        }

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Show reference frames",
                         "Description" : "Draw Frenet frames along the reference path" }
            definition.showRefFrames is boolean;

            annotation { "Name" : "Show regions",
                         "Description" : "Highlight region output curves, alternating CYAN / MAGENTA per region" }
            definition.showRegions is boolean;

            annotation { "Name" : "Show blends",
                         "Description" : "Highlight blend output curves (yellow)" }
            definition.showBlends is boolean;

            annotation { "Name" : "Print curve details",
                         "Description" : "Print BSpline metadata to FeatureStudio console" }
            definition.printCurveDetails is boolean;

            annotation { "Name" : "Print path data",
                         "Description" : "Print path length, refParam, and edge count" }
            definition.printPathData is boolean;

            annotation { "Name" : "Print edge data",
                         "Description" : "Print per-edge arc lengths, lengths, and startSign values" }
            definition.printEdgeData is boolean;

            annotation { "Name" : "Print frame samples",
                         "Description" : "Sample Frenet xAxis/yAxis at 5 points per edge; show junction dot products" }
            definition.printFrameSamples is boolean;
        }
    }
    {
        var pathInfo = processPath(context, id + "refPath", definition);

        // Direction indicator: GREEN = + (increasing region start), RED = - (behind reference).
        // Drawn before the region check so it is available while setting the path up. It uses
        // the traversal tangent at the reference point, so it follows the Flip direction toggle.
        if (definition.showDirection != false)   // default-on; undefined on legacy instances still draws
        {
            var refArc = pathInfo.refParam * pathInfo.length;
            var dirFr  = sampleParallelTransportFrame(context, pathInfo.frenetPath, pathInfo.ptTable, refArc);
            var dOrg   = dirFr.frame.origin;
            var dTan   = dirFr.frame.zAxis;                 // traversal (+) direction
            var dLen   = pathInfo.length / 8;
            var dRad   = dLen * 0.03;
            addDebugArrow(context, dOrg, dOrg + dLen * dTan, dRad, DebugColor.GREEN);  // + direction
            addDebugArrow(context, dOrg, dOrg - dLen * dTan, dRad, DebugColor.RED);    // - direction
        }

        var processedRegions;
        if (definition.offsetMode == OffsetMode.SINGLE_REGION)
        {
            if (definition.interiorOffsets == undefined || size(definition.interiorOffsets) == 0)
            {
                reportFeatureWarning(context, id, "No interior offsets defined -- output follows the source path.");
            }
            processedRegions = assembleSingleRegion(context, definition, pathInfo);
        }
        else
        {
            if (size(definition.regions) == 0)
            {
                reportFeatureWarning(context, id, "No regions defined -- nothing to generate");
                return;
            }
            processedRegions = processRegions(context, definition, pathInfo);
            validateNoOverlap(context, id, processedRegions);
        }

        for (var pr in processedRegions)
        {
            if (pr.stationWarnings != undefined)
            {
                for (var w in pr.stationWarnings)
                {
                    reportFeatureWarning(context, id, w);
                }
            }
        }
        var sortedRegions = sortRegionsByTStart(processedRegions);
        buildOutputWire(context, id, definition, pathInfo, sortedRegions);

        if (definition.showRefFrames)
        {
            // Draw frames with flipNormal/flipBinormal applied so arrows match
            // the actual offset directions (RED = normal offset dir, GREEN = binormal offset dir, BLUE = tangent)
            var numF   = definition.numRegionPoints;
            var len    = pathInfo.length;
            var aLen   = len / max([1, numF - 1]) / 3;
            var aRad   = aLen * 0.05;
            for (var fi = 0; fi < numF; fi += 1)
            {
                var s   = len * fi / max([1, numF - 1]);
                var fr  = sampleParallelTransportFrame(context, pathInfo.frenetPath, pathInfo.ptTable, s);
                var org = fr.frame.origin;
                var nD  = definition.flipNormal   ? -yAxis(fr.frame) : yAxis(fr.frame);
                var bD  = definition.flipBinormal ? -fr.frame.xAxis  : fr.frame.xAxis;
                addDebugArrow(context, org, org + aLen * nD,              aRad,          DebugColor.RED);
                addDebugArrow(context, org, org + aLen * bD,              aRad * (2/3),  DebugColor.GREEN);
                addDebugArrow(context, org, org + aLen * fr.frame.zAxis,  aRad * 0.5,   DebugColor.BLUE);
            }
        }

        if (definition.printPathData)
        {
            println("=== offsetEdges path ===");
            println("  total length: " ~ toString(pathInfo.length / millimeter) ~ " mm");
            println("  refParam:     " ~ toString(pathInfo.refParam));
            println("  edge count:   " ~ toString(size(pathInfo.frenetPath.edgeData)));
        }

        if (definition.printEdgeData || definition.printFrameSamples)
        {
            var edgeData = pathInfo.frenetPath.edgeData;
            var nEdges   = size(edgeData);

            if (definition.printEdgeData)
            {
                println("=== Frenet edge data (" ~ toString(nEdges) ~ " edges) ===");
                for (var ei = 0; ei < nEdges; ei += 1)
                {
                    var ed = edgeData[ei];
                    println("  Edge " ~ toString(ei) ~ ":"
                        ~ "  startArc=" ~ toString(ed.startArcLength / millimeter) ~ "mm"
                        ~ "  len="      ~ toString(ed.length          / millimeter) ~ "mm"
                        ~ "  startSign=" ~ toString(ed.startSign));
                }
            }

            if (definition.printFrameSamples)
            {
                println("=== Parallel transport frame samples (5 pts/edge) ===");
                println("  xAxis = PT binormal dir  |  yAxis = PT normal dir  |  zAxis = tangent");
                for (var ei = 0; ei < nEdges; ei += 1)
                {
                    var ed       = edgeData[ei];
                    var arcStart = ed.startArcLength;
                    var arcEnd   = arcStart + ed.length;
                    println("  -- Edge " ~ toString(ei)
                        ~ "  startSign=" ~ toString(ed.startSign)
                        ~ "  len=" ~ toString(ed.length / millimeter) ~ "mm --");
                    for (var si = 0; si <= 4; si += 1)
                    {
                        var s  = arcStart + (arcEnd - arcStart) * si / 4;
                        var fr = sampleParallelTransportFrame(context, pathInfo.frenetPath, pathInfo.ptTable, s);
                        var xa = fr.frame.xAxis;
                        var ya = yAxis(fr.frame);
                        println("    s=" ~ toString(s / millimeter) ~ "mm"
                            ~ "  xAxis=[" ~ toString(xa[0]) ~ ", " ~ toString(xa[1]) ~ ", " ~ toString(xa[2]) ~ "]"
                            ~ "  yAxis=[" ~ toString(ya[0]) ~ ", " ~ toString(ya[1]) ~ ", " ~ toString(ya[2]) ~ "]");
                    }

                    if (ei < nEdges - 1)
                    {
                        var jArc = edgeData[ei + 1].startArcLength;
                        var fB   = sampleParallelTransportFrame(context, pathInfo.frenetPath, pathInfo.ptTable, jArc);
                        var fA   = sampleParallelTransportFrame(context, pathInfo.frenetPath, pathInfo.ptTable,
                            jArc + edgeData[ei + 1].length * 0.01);
                        var d    = dot(fB.frame.xAxis, fA.frame.xAxis);
                        var tag  = (d < 0.9) ? "  *** DISCONTINUITY (dot=" ~ toString(d) ~ ") ***" : "  OK";
                        println("  Junction " ~ toString(ei) ~ "->" ~ toString(ei + 1)
                            ~ "  dot(PT xBefore, xAfter)=" ~ toString(d) ~ tag);
                    }
                }
            }
        }
    });


// ─── Path processing ──────────────────────────────────────────────────────────

// Corrects startSign mismatches at inter-edge junctions that buildFrenetPath leaves unfixed.
//
// buildFrenetPath propagates startSign across the chain accounting only for intra-edge
// inflections. At junctions where the raw curvature direction is antiparallel between
// adjacent edges, it leaves startSign_{i+1} inconsistent with endSign_i.
//
// We detect this algebraically: the effective sign at the END of edge i is
//   endSign_i = startSign_i * (-1)^(number of inflections in edge i)
// If endSign_i != startSign_{i+1}, the junction is antiparallel and all edges i+1..n-1
// need their startSign flipped.
//
// This approach is robust against inflections near edge endpoints (which made the previous
// eps-based dot-product probe unreliable).
function fixFrenetPathSigns(frenetPath is map, printLog is boolean) returns map
{
    var edgeData = frenetPath.edgeData;
    var n = size(edgeData);
    if (n < 2)
    {
        return frenetPath;
    }

    if (printLog)
        println("=== fixFrenetPathSigns (" ~ toString(n) ~ " edges) ===");

    for (var i = 0; i < n - 1; i += 1)
    {
        var inflArcs   = edgeData[i].localInflectionArcs;
        var inflCount  = (inflArcs == undefined) ? 0 : size(inflArcs);
        var endSign    = edgeData[i].startSign * (inflCount % 2 == 1 ? -1 : 1);
        var nextStart  = edgeData[i + 1].startSign;
        var needsFlip  = (endSign != nextStart);

        if (printLog)
        {
            var action = needsFlip
                ? "  -> FLIPPING edges " ~ toString(i + 1) ~ ".." ~ toString(n - 1)
                : "  -> OK";
            println("  Junction " ~ toString(i) ~ "->" ~ toString(i + 1)
                ~ "  startSign[" ~ toString(i) ~ "]=" ~ toString(edgeData[i].startSign)
                ~ "  inflections=" ~ toString(inflCount)
                ~ "  endSign=" ~ toString(endSign)
                ~ "  startSign[" ~ toString(i + 1) ~ "]=" ~ toString(nextStart)
                ~ action);
        }

        if (needsFlip)
        {
            for (var j = i + 1; j < n; j += 1)
            {
                edgeData[j] = mergeMaps(edgeData[j], {
                    "startSign": -1 * edgeData[j].startSign
                });
            }
            frenetPath = mergeMaps(frenetPath, { "edgeData": edgeData });
        }
    }

    return frenetPath;
}


// Returns a frame at the given arc length along the path, handling both BSpline/line edges
// (delegated to getFrameAtArcLength) and circular arc edges (computed natively).
//
// For circular arcs, getFrameAtArcLength fails internally because evApproximateBSplineCurve
// returns a rational NURBS and the arc-length table pipeline does not support weighted
// BSplines.  We bypass it entirely:
//   - position + tangent  via evEdgeTangentLine  (native, works for any edge type)
//   - centripetal normal  via (center - position) (geometric, no BSpline needed)
//   - traversal direction via edgeData.stdDir
//   - sign correction     via edgeData.startSign  (same convention as getFrameAtArcLength)
//
// The returned map has the same shape as getFrameAtArcLength: { frame, sign, edgeIndex }.
function evalNativeFrame(context is Context, frenetPath is map, arcLength) returns map
{
    var edgeData = frenetPath.edgeData;
    var n        = size(edgeData);

    // Find the edge that contains this arc length
    var ei = n - 1;
    for (var i = 0; i < n - 1; i += 1)
    {
        if (edgeData[i + 1].startArcLength > arcLength)
        {
            ei = i;
            break;
        }
    }
    var ed = edgeData[ei];

    // Check curve type; only circles need special handling.
    // evCurveDefinition uses the field name "curveType" (not "type").
    var curveDef = evCurveDefinition(context, { "edge": ed.query });
    if (curveDef.curveType != CurveType.CIRCLE)
    {
        return getFrameAtArcLength(context, frenetPath, arcLength);
    }

    // ── Circular arc: compute frame directly ──────────────────────────────────

    // Convert local arc length to native edge parameter [0, 1].
    // For a circular arc, the native parameter is proportional to arc length.
    var localArc = arcLength - ed.startArcLength;
    var edgeLen  = ed.length;
    var p        = (edgeLen / meter > 1e-10) ? localArc / edgeLen : 0.0;
    p = ed.stdDir ? p : (1.0 - p);
    p = min(max(p, 0.0), 1.0);

    // Position and tangent from the native edge query
    var tl      = evEdgeTangentLine(context, { "edge": ed.query, "parameter": p });
    var origin  = tl.origin;
    var tangent = ed.stdDir ? tl.direction : -tl.direction;

    // Centripetal normal: direction from the point on the arc toward the circle center.
    // curveDef.coordSystem.origin IS the circle center for CurveType.CIRCLE.
    var center  = curveDef.coordSystem.origin;
    var diff    = center - origin;
    var diffLen = norm(diff);
    var rawNorm = (diffLen / meter > 1e-10) ? diff / diffLen : curveDef.coordSystem.xAxis;

    // Apply startSign (same convention buildFrenetPath uses for BSpline edges)
    var sign  = ed.startSign;
    var xAxis = sign * rawNorm;

    // Re-orthogonalize against tangent — should already be perpendicular for a true circle,
    // but floating-point and edge parameterization can introduce small errors.
    xAxis     = xAxis - tangent * dot(tangent, xAxis);
    var xLen  = norm(xAxis);
    if (xLen > 1e-10)
    {
        xAxis = xAxis / xLen;
    }
    else
    {
        // True degenerate (query point at circle center — geometrically impossible for valid input)
        xAxis = rawNorm;
    }

    return {
        "frame"     : coordSystem(origin, xAxis, tangent),
        "sign"      : sign,
        "edgeIndex" : ei
    };
}


// Builds a parallel transport (Bishop) frame table along the path.
//
// Starting from the Frenet frame at s=0, each step rotates the previous xAxis by
// the same rotation that carries prevTangent -> currTangent (Rodrigues formula).
// This eliminates the torsion-driven spinning of the Frenet normal while preserving
// tangent continuity — the frame never flips direction on smooth G1 BSpline chains.
//
// Returns an array of { arcLength, xAxis } entries at numSamples uniform positions.
// yAxis = cross(tangent, xAxis) is not stored; it is computed on demand.
function buildParallelTransportTable(context is Context, frenetPath is map, numSamples is number) returns array
{
    var totalLength = frenetPath.totalLength;
    var fr0         = evalNativeFrame(context, frenetPath, 0 * meter);
    var prevXAxis   = fr0.frame.xAxis;
    var prevTangent = fr0.frame.zAxis;

    var table = [{ "arcLength" : 0 * meter, "xAxis" : prevXAxis }];

    for (var i = 1; i < numSamples; i += 1)
    {
        var s           = totalLength * i / (numSamples - 1);
        var fr          = evalNativeFrame(context, frenetPath, s);
        var currTangent = fr.frame.zAxis;

        // Rodrigues rotation: rotate prevXAxis by the rotation taking prevTangent -> currTangent
        var k    = cross(prevTangent, currTangent);
        var kLen = norm(k);
        var newXAxis = prevXAxis;

        if (kLen >= 1e-10)
        {
            var kHat     = k / kLen;
            var sinTheta = kLen;                              // |cross(a,b)| = sin(angle) for unit vecs
            var cosTheta = dot(prevTangent, currTangent);
            newXAxis = prevXAxis * cosTheta
                + cross(kHat, prevXAxis) * sinTheta
                + kHat * (dot(kHat, prevXAxis) * (1 - cosTheta));
        }

        // Re-orthogonalize against tangent to prevent numerical drift
        newXAxis = newXAxis - currTangent * dot(currTangent, newXAxis);
        var xLen = norm(newXAxis);
        if (xLen > 1e-10)
        {
            newXAxis = newXAxis / xLen;
        }

        table       = append(table, { "arcLength" : s, "xAxis" : newXAxis });
        prevXAxis   = newXAxis;
        prevTangent = currTangent;
    }

    return table;
}


// Looks up the parallel transport xAxis at the given arc length via binary search + lerp,
// then returns a frame map in the same format as getFrameAtArcLength but with the PT
// xAxis substituted.  Origin and tangent (zAxis) are exact from getFrameAtArcLength.
function sampleParallelTransportFrame(context is Context, frenetPath is map, ptTable is array,
    arcLength) returns map
{
    var n  = size(ptTable);
    var fr = evalNativeFrame(context, frenetPath, arcLength);

    if (n == 0)
    {
        return fr;
    }

    // Binary search for the bracketing interval
    var lo = 0;
    var hi = n - 1;
    while (hi - lo > 1)
    {
        var mid = floor((lo + hi) / 2);
        if (ptTable[mid].arcLength <= arcLength)
        {
            lo = mid;
        }
        else
        {
            hi = mid;
        }
    }

    // Lerp + renormalize between lo and hi
    var span    = ptTable[hi].arcLength - ptTable[lo].arcLength;
    var alpha   = (span / meter > 1e-12) ? (arcLength - ptTable[lo].arcLength) / span : 0.0;
    var x0      = ptTable[lo].xAxis;
    var x1      = ptTable[hi].xAxis;
    var xLerp   = x0 * (1 - alpha) + x1 * alpha;
    var xLen    = norm(xLerp);
    var ptXAxis = (xLen > 1e-10) ? xLerp / xLen : x0;

    // Re-orthogonalize against the exact tangent at this arc length.
    // The interpolated xAxis may have drifted slightly off the perpendicular plane.
    var tangent = fr.frame.zAxis;
    ptXAxis = ptXAxis - tangent * dot(tangent, ptXAxis);
    var xLen2 = norm(ptXAxis);
    if (xLen2 > 1e-10)
    {
        ptXAxis = ptXAxis / xLen2;
    }
    else
    {
        // Degenerate: interpolated PT direction is nearly parallel to the tangent.
        // Fall back to the raw Frenet xAxis, which is guaranteed perpendicular.
        ptXAxis = fr.frame.xAxis;
    }

    return mergeMaps(fr, { "frame" : coordSystem(fr.frame.origin, ptXAxis, fr.frame.zAxis) });
}


function processPath(context is Context, id is Id, definition is map) returns map
{
    var edges       = expandEdgeQuery(definition.userSelection);
    var frenetPath  = buildFrenetPath(context, id, edges, definition.flipDirection);
    frenetPath      = fixFrenetPathSigns(frenetPath, definition.printFrameSamples);
    var totalLength = frenetPath.totalLength;

    var numPTSamples = max([100, definition.numRegionPoints * 4]);
    var ptTable      = buildParallelTransportTable(context, frenetPath, numPTSamples);

    var refPt     = getRefPoint(context, definition.referencePoint);
    var refResult = projectOntoFrenetPath(frenetPath, refPt, undefined);
    var refParam  = refResult.arcLength / totalLength;

    return {
        "frenetPath" : frenetPath,
        "ptTable"    : ptTable,
        "length"     : totalLength,
        "refParam"   : refParam
    };
}


// ─── Region processing ────────────────────────────────────────────────────────
/**
 * Region processing
 * @param context {Context} : context
 * @param definition {{
 *      @field regions {array} : regions from defi
 *          }}
 * @param pathInfo {map} : frenetPath
 */
function processRegions(context is Context, definition is map, pathInfo is map) returns array
{
    var processed = [];

    for (var i = 0; i < size(definition.regions); i += 1)
    {
        var region = definition.regions[i];
        region.regionNum = i;

        var tStart;
        var tEnd;

        if (region.extentType == RegionExtentType.X_EXTENTS)
        {
            tStart = pathInfo.refParam + region.regionStart / pathInfo.length;
            tEnd   = pathInfo.refParam + region.regionEnd   / pathInfo.length;
        }
        else // QUERY
        {
            var queryPts = evaluateQuery(context, region.extentQueries);
            if (size(queryPts) != 2)
            {
                throw regenError("Region '" ~ region.regionName ~ "': extent query must resolve to exactly 2 points");
            }

            var pt0 = getRefPoint(context, queryPts[0]);
            var pt1 = getRefPoint(context, queryPts[1]);

            var r0 = projectOntoFrenetPath(pathInfo.frenetPath, pt0, undefined);
            var r1 = projectOntoFrenetPath(pathInfo.frenetPath, pt1, undefined);

            tStart = min(r0.arcLength, r1.arcLength) / pathInfo.length;
            tEnd   = max(r0.arcLength, r1.arcLength) / pathInfo.length;
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
        region.length = (tEnd - tStart) * pathInfo.length;

        var span = tEnd - tStart;
        var ot = (region.offsetType != undefined) ? region.offsetType : OffsetType.BOTH;
        var useNormal   = (ot == OffsetType.NORMAL   || ot == OffsetType.BOTH);
        var useBinormal = (ot == OffsetType.BINORMAL || ot == OffsetType.BOTH);

        // --- Resolve interior stations (fixed per-region slots) into {alpha, normalOff, binormalOff} ---
        var warnings  = [];
        var resolved  = [];

        for (var si = 1; si <= 5; si += 1)
        {
            var pfx = "station" ~ toString(si);
            if (region[pfx ~ "Enabled"] != true)
            {
                continue;   // slot disabled (or legacy region without this field)
            }
            var locType = region[pfx ~ "LocationType"];

            var stT;
            if (locType == RegionExtentType.QUERY)
            {
                var stPts = evaluateQuery(context, region[pfx ~ "Query"]);
                if (size(stPts) != 1)
                {
                    warnings = append(warnings, "Region '" ~ region.regionName ~
                        "' station " ~ toString(si) ~ ": location must resolve to exactly 1 point; skipped.");
                    continue;
                }
                var stPt  = getRefPoint(context, stPts[0]);
                var stRes = projectOntoFrenetPath(pathInfo.frenetPath, stPt, undefined);
                stT = stRes.arcLength / pathInfo.length;
            }
            else // X_EXTENTS (also the default when locType is unset)
            {
                stT = pathInfo.refParam + region[pfx ~ "Position"] / pathInfo.length;
            }

            var alpha = (span > 1e-10) ? (stT - tStart) / span : 0.0;
            if (alpha < -1e-6 || alpha > 1 + 1e-6)
            {
                warnings = append(warnings, "Region '" ~ region.regionName ~
                    "' station " ~ toString(si) ~ " lies outside the region extent; skipped.");
                continue;
            }
            alpha = min(max(alpha, 0.0), 1.0);

            var nOff = (useNormal   && region[pfx ~ "NormalOffset"]   != undefined) ? region[pfx ~ "NormalOffset"]   : undefined;
            var bOff = (useBinormal && region[pfx ~ "BinormalOffset"] != undefined) ? region[pfx ~ "BinormalOffset"] : undefined;

            resolved = append(resolved, { "alpha" : alpha, "normalOff" : nOff, "binormalOff" : bOff });
        }

        // Sort interior stations by alpha (insertion sort; counts are small)
        for (var a = 1; a < size(resolved); a += 1)
        {
            var key = resolved[a];
            var b   = a - 1;
            while (b >= 0 && resolved[b].alpha > key.alpha)
            {
                resolved[b + 1] = resolved[b];
                b -= 1;
            }
            resolved[b + 1] = key;
        }

        for (var a = 1; a < size(resolved); a += 1)
        {
            if (abs(resolved[a].alpha - resolved[a - 1].alpha) < 1e-4)
            {
                warnings = append(warnings, "Region '" ~ region.regionName ~
                    "': coincident interior stations detected.");
            }
        }

        region.stations = resolved;

        // --- Dwell fractions (alpha-space), clamped ---
        var startDelay = (region.startDelay != undefined) ? region.startDelay : 0 * meter;
        var endDelay   = (region.endDelay   != undefined) ? region.endDelay   : 0 * meter;
        var d0 = (region.length / meter > 1e-12) ? startDelay / region.length : 0.0;
        var d1 = (region.length / meter > 1e-12) ? endDelay   / region.length : 0.0;
        d0 = min(max(d0, 0.0), 1.0);
        d1 = min(max(d1, 0.0), 1.0);
        if (d0 + d1 > 1.0 + 1e-9)
        {
            warnings = append(warnings, "Region '" ~ region.regionName ~
                "': start + end dwell exceed the region length; dwell clamped.");
        }
        region.startDelayFrac = d0;
        region.endDelayFrac   = d1;

        region.stationWarnings = warnings;

        processed = append(processed, region);
    }

    return processed;
}


// Builds one synthetic region for SINGLE_REGION mode from the top-level interiorOffsets array.
// The region spans the whole path (tStart 0, tEnd 1); each offset point becomes a resolved
// station, and the per-component endpoint values are the first/last pinning point's value so
// the profile holds flat beyond the outer points. Returns [region] for the normal pipeline.
function assembleSingleRegion(context is Context, definition is map, pathInfo is map) returns array
{
    var offsets  = (definition.interiorOffsets != undefined) ? definition.interiorOffsets : [];
    var resolved = [];

    for (var i = 0; i < size(offsets); i += 1)
    {
        var off  = offsets[i];
        var ot   = off.offsetType;
        var useN = (ot == OffsetType.NORMAL   || ot == OffsetType.BOTH);
        var useB = (ot == OffsetType.BINORMAL || ot == OffsetType.BOTH);

        var t;
        if (off.locationType == RegionExtentType.QUERY)
        {
            var pts = evaluateQuery(context, off.locationQuery);
            if (size(pts) != 1)
            {
                continue;
            }
            var res = projectOntoFrenetPath(pathInfo.frenetPath, getRefPoint(context, pts[0]), undefined);
            t = res.arcLength / pathInfo.length;
        }
        else // X_EXTENTS (also default)
        {
            t = pathInfo.refParam + off.position / pathInfo.length;
        }
        t = min(max(t, 0), 1);

        var nOff = (useN && off.normalOffset   != undefined) ? off.normalOffset   : undefined;
        var bOff = (useB && off.binormalOffset != undefined) ? off.binormalOffset : undefined;
        resolved = append(resolved, { "alpha" : t, "normalOff" : nOff, "binormalOff" : bOff });
    }

    // Sort by alpha (insertion sort; counts are small)
    for (var a = 1; a < size(resolved); a += 1)
    {
        var key = resolved[a];
        var b   = a - 1;
        while (b >= 0 && resolved[b].alpha > key.alpha)
        {
            resolved[b + 1] = resolved[b];
            b -= 1;
        }
        resolved[b + 1] = key;
    }

    // Per-component flat-hold endpoint values: first/last point that pins that component.
    var startN = 0 * meter; var endN = 0 * meter; var haveN = false;
    var startB = 0 * meter; var endB = 0 * meter; var haveB = false;
    for (var i = 0; i < size(resolved); i += 1)
    {
        if (resolved[i].normalOff != undefined)
        {
            if (!haveN) { startN = resolved[i].normalOff; haveN = true; }
            endN = resolved[i].normalOff;
        }
        if (resolved[i].binormalOff != undefined)
        {
            if (!haveB) { startB = resolved[i].binormalOff; haveB = true; }
            endB = resolved[i].binormalOff;
        }
    }

    var qzs = (definition.singleQuadZeroSlope != undefined) ? definition.singleQuadZeroSlope : QuadraticZeroSlope.AT_START;

    var region = {
        "regionNum"           : 0,
        "regionName"          : "Region 1",
        "regionType"          : definition.singleTransfer,
        "quadZeroSlope"       : qzs,
        "offsetType"          : OffsetType.BOTH,
        "tStart"              : 0,
        "tEnd"                : 1,
        "length"              : pathInfo.length,
        "startNormalOffset"   : startN,
        "endNormalOffset"     : endN,
        "startBinormalOffset" : startB,
        "endBinormalOffset"   : endB,
        "startDelayFrac"      : 0,
        "endDelayFrac"        : 0,
        "stations"            : resolved,
        "stationWarnings"     : []
    };

    return [region];
}


function validateNoOverlap(context is Context, id is Id, regions is array)
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


function sortRegionsByTStart(regions is array) returns array
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


// ─── Profile evaluation ───────────────────────────────────────────────────────

/**
 * Returns the interpolated offset at normalized position t ∈ [0,1] within a region.
 *   LINEAR    : linear ramp
 *   QUADRATIC : true quadratic — one endpoint has zero slope
 *   SMOOTH    : smootherstep (6t⁵ − 15t⁴ + 10t³) — C2 at both endpoints
 */
function profileValueAt(t is number, startVal is ValueWithUnits, endVal is ValueWithUnits,
    regionType, quadZeroSlope) returns ValueWithUnits
{
    var s;
    if (regionType == RegionType.LINEAR)
    {
        s = t;
    }
    else if (regionType == RegionType.QUADRATIC)
    {
        if (quadZeroSlope == QuadraticZeroSlope.AT_END)
            s = 2 * t - t * t;
        else // AT_START
            s = t * t;
    }
    else // SMOOTH
    {
        s = t * t * t * (10 + t * (6 * t - 15));
    }
    return startVal + (endVal - startVal) * s;
}


/**
 * Evaluates a single control-point segment with optional start/end dwell plateaus.
 *   u   : segment-local parameter in [0,1]
 *   dd0 : fraction of the segment (from u=0) held flat at startVal
 *   dd1 : fraction of the segment (up to u=1) held flat at endVal
 * The ramp is compressed into [dd0, 1-dd1] and the region transfer is applied there.
 */
function segmentValueAt(u is number, startVal is ValueWithUnits, endVal is ValueWithUnits,
    regionType, quadZeroSlope, dd0 is number, dd1 is number) returns ValueWithUnits
{
    var a = min(max(dd0, 0), 1);
    var b = 1 - min(max(dd1, 0), 1);

    if (a >= b)
    {
        // Degenerate dwell (plateaus meet or overlap): collapse to a step at the crossover.
        var c = min(max(0.5 * (a + b), 0), 1);
        if (u <= c)
        {
            return startVal;
        }
        return endVal;
    }

    if (u <= a)
    {
        return startVal;
    }
    if (u >= b)
    {
        return endVal;
    }

    var uRamp = (u - a) / (b - a);
    return profileValueAt(uRamp, startVal, endVal, regionType, quadZeroSlope);
}


/**
 * Builds the ordered control-point list [{alpha, value}, ...] for one Frenet component,
 * pinning start (alpha 0), any interior stations that carry this component, and end (alpha 1).
 * component is "normal" or "binormal". Stations arrive pre-sorted by alpha from processRegions;
 * stations that do not pin this component (value undefined) are skipped.
 */
function getComponentCPs(region is map, component is string) returns array
{
    var startVal;
    var endVal;
    if (component == "normal")
    {
        startVal = region.startNormalOffset;
        endVal   = region.endNormalOffset;
    }
    else
    {
        startVal = region.startBinormalOffset;
        endVal   = region.endBinormalOffset;
    }

    var cps = [{ "alpha" : 0, "value" : startVal }];
    var stations = (region.stations != undefined) ? region.stations : [];

    for (var st in stations)
    {
        var v = (component == "normal") ? st.normalOff : st.binormalOff;
        if (v == undefined)
        {
            continue;
        }
        var a = min(max(st.alpha, 0), 1);
        if (a <= 1e-9 || a >= 1 - 1e-9)
        {
            continue;   // coincident with an endpoint pin
        }
        var last = cps[size(cps) - 1];
        if (abs(a - last.alpha) < 1e-9)
        {
            cps[size(cps) - 1] = { "alpha" : a, "value" : v };   // collapse coincident, later wins
        }
        else
        {
            cps = append(cps, { "alpha" : a, "value" : v });
        }
    }

    cps = append(cps, { "alpha" : 1, "value" : endVal });
    return cps;
}


/**
 * Evaluates a multi-station piecewise profile at alpha in [0,1].
 * The region transfer is applied on each segment; start/end dwell (d0,d1, alpha-space
 * fractions of the whole region) become plateaus on the first/last segments only.
 */
function evalComponentProfile(cps is array, alpha is number,
    regionType, quadZeroSlope, d0 is number, d1 is number) returns ValueWithUnits
{
    var m = size(cps);
    if (m == 0)
    {
        return 0 * meter;
    }
    if (m == 1)
    {
        return cps[0].value;
    }

    var nSeg = m - 1;

    // Locate the segment: largest i with cps[i].alpha <= alpha
    var i = 0;
    for (var k = 0; k < nSeg; k += 1)
    {
        if (alpha >= cps[k].alpha)
        {
            i = k;
        }
    }

    var a0      = cps[i].alpha;
    var a1      = cps[i + 1].alpha;
    var segSpan = a1 - a0;
    var u       = (segSpan > 1e-10) ? min(max((alpha - a0) / segSpan, 0), 1) : 0;

    // Dwell only on the outer segments; convert region-space fraction to segment-local fraction.
    var dd0 = 0;
    var dd1 = 0;
    if (i == 0 && segSpan > 1e-10)
    {
        dd0 = d0 / segSpan;
    }
    if (i == nSeg - 1 && segSpan > 1e-10)
    {
        dd1 = d1 / segSpan;
    }

    return segmentValueAt(u, cps[i].value, cps[i + 1].value, regionType, quadZeroSlope, dd0, dd1);
}


/**
 * Returns the sorted t-space breakpoints of the region profile: endpoints, dwell-plateau
 * edges, and station positions. Used to (a) keep finite differences inside a single smooth
 * segment and (b) land sample nodes on kinks during wire construction.
 */
function getBreakpointsT(region is map) returns array
{
    var tS   = region.tStart;
    var tE   = region.tEnd;
    var span = tE - tS;

    var d0 = (region.startDelayFrac != undefined) ? min(max(region.startDelayFrac, 0), 1) : 0;
    var d1 = (region.endDelayFrac   != undefined) ? min(max(region.endDelayFrac,   0), 1) : 0;

    var alphas = [0, 1];
    if (d0 > 1e-9)
    {
        alphas = append(alphas, d0);
    }
    if (d1 > 1e-9)
    {
        alphas = append(alphas, 1 - d1);
    }

    var stations = (region.stations != undefined) ? region.stations : [];
    for (var st in stations)
    {
        alphas = append(alphas, min(max(st.alpha, 0), 1));
    }

    var ts = [];
    for (var av in alphas)
    {
        ts = append(ts, tS + av * span);
    }

    for (var i = 1; i < size(ts); i += 1)
    {
        var key = ts[i];
        var j = i - 1;
        while (j >= 0 && ts[j] > key)
        {
            ts[j + 1] = ts[j];
            j -= 1;
        }
        ts[j + 1] = key;
    }
    return ts;
}


/**
 * Returns { normalOff, binormalOff } at path parameter tPath from the region's profile,
 * supporting interior stations and start/end dwell. With no stations and zero dwell this
 * reduces exactly to the legacy single-transfer ramp.
 */
function computeOffsetsAt(region is map, tPath is number) returns map
{
    var span  = region.tEnd - region.tStart;
    var alpha = (span > 1e-10) ? min(max((tPath - region.tStart) / span, 0), 1) : 0;
    var quadZS = region.quadZeroSlope;

    // Offset type gates which components are applied; the hidden ones contribute 0. Fall
    // back to BOTH for regions created before offsetType existed (preserves old behavior).
    var ot = (region.offsetType != undefined) ? region.offsetType : OffsetType.BOTH;
    var useNormal   = (ot == OffsetType.NORMAL   || ot == OffsetType.BOTH);
    var useBinormal = (ot == OffsetType.BINORMAL || ot == OffsetType.BOTH);

    var d0 = (region.startDelayFrac != undefined) ? min(max(region.startDelayFrac, 0), 1) : 0;
    var d1 = (region.endDelayFrac   != undefined) ? min(max(region.endDelayFrac,   0), 1) : 0;

    var normalOff = 0 * meter;
    if (useNormal)
    {
        var nCPs  = getComponentCPs(region, "normal");
        normalOff = evalComponentProfile(nCPs, alpha, region.regionType, quadZS, d0, d1);
    }

    var binormalOff = 0 * meter;
    if (useBinormal)
    {
        var bCPs    = getComponentCPs(region, "binormal");
        binormalOff = evalComponentProfile(bCPs, alpha, region.regionType, quadZS, d0, d1);
    }

    return { "normalOff" : normalOff, "binormalOff" : binormalOff };
}


/**
 * Returns { normalOff, binormalOff, normalSlope, binormalSlope, normalCurv, binormalCurv }
 * using central finite differences in t-space (VWU per t, VWU per t^2).
 *
 * The step is confined to the smooth segment containing tPath (bounded by getBreakpointsT),
 * so kinks at stations and dwell-plateau edges never contaminate the slope/curvature that the
 * blend logic consumes. Scale to s-space before blendOffsetAt: slope * L, curv * L^2.
 */
function computeOffsetDerivativesAt(region is map, tPath is number) returns map
{
    var dt  = 1e-4;
    var bps = getBreakpointsT(region);

    // Open segment (segLo, segHi) strictly containing tPath.
    var segLo   = region.tStart;
    var segHi   = region.tEnd;
    var onBreak = false;
    for (var bp in bps)
    {
        if (abs(bp - tPath) < 1e-9)
        {
            onBreak = true;
        }
        else if (bp < tPath && bp > segLo)
        {
            segLo = bp;
        }
        else if (bp > tPath && bp < segHi)
        {
            segHi = bp;
        }
    }

    var offMid = computeOffsetsAt(region, tPath);
    var h      = min(dt, min(tPath - segLo, segHi - tPath));

    if (onBreak || h <= 1e-9)
    {
        // tPath sits on a kink; step one-sided into the roomier neighbor segment.
        var roomHi = segHi - tPath;
        var roomLo = tPath - segLo;
        var dir    = (roomHi >= roomLo) ? 1 : -1;
        var hh     = min(dt, (dir > 0) ? roomHi : roomLo);
        if (hh <= 1e-12)
        {
            hh = dt;   // fully degenerate region; fall back so we never divide by ~0
        }
        var offA = offMid;
        var offB = computeOffsetsAt(region, tPath + dir * hh);
        var offC = computeOffsetsAt(region, tPath + dir * 2 * hh);
        return {
            "normalOff"     : offMid.normalOff,
            "binormalOff"   : offMid.binormalOff,
            "normalSlope"   : dir * (offB.normalOff   - offA.normalOff)   / hh,
            "binormalSlope" : dir * (offB.binormalOff - offA.binormalOff) / hh,
            "normalCurv"    : (offC.normalOff   - 2 * offB.normalOff   + offA.normalOff)   / (hh * hh),
            "binormalCurv"  : (offC.binormalOff - 2 * offB.binormalOff + offA.binormalOff) / (hh * hh)
        };
    }

    var offHi = computeOffsetsAt(region, tPath + h);
    var offLo = computeOffsetsAt(region, tPath - h);
    return {
        "normalOff"     : offMid.normalOff,
        "binormalOff"   : offMid.binormalOff,
        "normalSlope"   : (offHi.normalOff   - offLo.normalOff)   / (2 * h),
        "binormalSlope" : (offHi.binormalOff - offLo.binormalOff) / (2 * h),
        "normalCurv"    : (offHi.normalOff   - 2 * offMid.normalOff   + offLo.normalOff)   / (h * h),
        "binormalCurv"  : (offHi.binormalOff - 2 * offMid.binormalOff + offLo.binormalOff) / (h * h)
    };
}


// ─── Offset point computation ─────────────────────────────────────────────────

/**
 * Computes the 3D world point at path parameter t with the given Frenet-space offsets.
 * Frame convention from buildFrenetPath: xAxis = normal (sign-corrected), yAxis = binormal, zAxis = tangent.
 */
function computeOffsetPoint(context is Context, pathInfo is map, definition is map,
    t is number, normalOff is ValueWithUnits, binormalOff is ValueWithUnits) returns Vector
{
    var fr   = sampleParallelTransportFrame(context, pathInfo.frenetPath, pathInfo.ptTable, t * pathInfo.length);
    // Empirically: yAxis(frame) = visual normal direction, xAxis = visual binormal direction
    var nDir = definition.flipNormal   ? -yAxis(fr.frame) : yAxis(fr.frame);
    var bDir = definition.flipBinormal ? -fr.frame.xAxis  : fr.frame.xAxis;
    return fr.frame.origin + normalOff * nDir + binormalOff * bDir;
}


/**
 * Unit tangent of the offset curve at path parameter t, in the direction of increasing t
 * (the travel direction). Central finite difference of computeOffsetPoint, clamped so the
 * step stays inside [region.tStart, region.tEnd]; falls back to the frame tangent if the
 * offset curve is momentarily stationary. Used to seed the best-fit biarc endpoints.
 */
function offsetTangentAt(context is Context, pathInfo is map, definition is map,
    region is map, t is number) returns Vector
{
    var dtp = 1e-4;
    var tHi = min(t + dtp, region.tEnd);
    var tLo = max(t - dtp, region.tStart);
    if (tHi - tLo < 1e-12)
    {
        tHi = min(t + dtp, 1.0);
        tLo = max(t - dtp, 0.0);
    }

    var oHi = computeOffsetsAt(region, tHi);
    var oLo = computeOffsetsAt(region, tLo);
    var pHi = computeOffsetPoint(context, pathInfo, definition, tHi, oHi.normalOff, oHi.binormalOff);
    var pLo = computeOffsetPoint(context, pathInfo, definition, tLo, oLo.normalOff, oLo.binormalOff);

    var dir  = pHi - pLo;
    var dLen = norm(dir);
    if (dLen / meter < 1e-12)
    {
        var fr = sampleParallelTransportFrame(context, pathInfo.frenetPath, pathInfo.ptTable, t * pathInfo.length);
        return fr.frame.zAxis;
    }
    return dir / dLen;
}


// ─── Point arrays ─────────────────────────────────────────────────────────────

/**
 * Samples points along [tSegStart, tSegEnd], evaluating the region profile at each, and
 * returns { points, interpolateIndices }. Sample nodes are forced to land exactly on every
 * interior profile breakpoint (station or dwell-plateau edge) inside the segment, and those
 * node indices are returned so approximateSpline pins them. Stations only SHAPE and CONSTRAIN
 * the fit here; they do not split the output curve (edge-boundary splitting is separate).
 * tSegStart/tSegEnd may differ from region.tStart/tEnd when trimmed by an adjacent blend.
 */
function generateSegmentPoints(context is Context, pathInfo is map, definition is map,
    region is map, tSegStart is number, tSegEnd is number) returns map
{
    var nTotal = definition.numRegionPoints;
    var minPer = max([2, definition.approxDegree + 1]);

    // Interior breakpoints strictly inside this sub-curve (getBreakpointsT is sorted ascending).
    var bounds = [tSegStart];
    for (var bp in getBreakpointsT(region))
    {
        if (bp > tSegStart + 1e-9 && bp < tSegEnd - 1e-9 && bp - bounds[size(bounds) - 1] > 1e-9)
        {
            bounds = append(bounds, bp);
        }
    }
    bounds = append(bounds, tSegEnd);

    var nSub      = size(bounds) - 1;
    var totalSpan = tSegEnd - tSegStart;

    var points    = [];
    var interpIdx = [];

    for (var s = 0; s < nSub; s += 1)
    {
        var a    = bounds[s];
        var b    = bounds[s + 1];
        var frac = (totalSpan > 1e-12) ? (b - a) / totalSpan : (1.0 / nSub);
        var nSeg = max([minPer, floor(nTotal * frac + 0.5)]);

        // First sub-interval contributes its start node; later ones share the previous end node.
        var startI = (s == 0) ? 0 : 1;
        for (var i = startI; i < nSeg; i += 1)
        {
            var t    = a + (b - a) * i / (nSeg - 1);
            var offs = computeOffsetsAt(region, t);
            points = append(points, computeOffsetPoint(context, pathInfo, definition, t, offs.normalOff, offs.binormalOff));
        }

        // The node just added at b is an interior breakpoint (pin it) unless it is segEnd.
        if (s < nSub - 1)
        {
            interpIdx = append(interpIdx, size(points) - 1);
        }
    }

    var finalInterp = [0];
    for (var idx in interpIdx)
    {
        finalInterp = append(finalInterp, idx);
    }
    finalInterp = append(finalInterp, size(points) - 1);

    return { "points" : points, "interpolateIndices" : finalInterp };
}


/**
 * Evaluates the Hermite blend polynomial at s ∈ [0,1] for a single scalar offset component.
 *
 * All slope/curvature args are in s-space (multiply region dOffset/dt by L, d²Offset/dt² by L²
 * before calling, where L = tBlendEnd − tBlendStart).
 *
 * Continuity dispatch:
 *   G0+G0 → linear          G1+G0 / G0+G1 → quadratic
 *   G1+G1 → cubic Hermite   G2+G0 / G0+G2 → cubic
 *   G2+G1 / G1+G2 → quartic               G2+G2 → quintic Hermite
 */
function blendOffsetAt(s is number,
    h0 is ValueWithUnits, m0 is ValueWithUnits, k0 is ValueWithUnits,
    h1 is ValueWithUnits, m1 is ValueWithUnits, k1 is ValueWithUnits,
    contStart, contEnd) returns ValueWithUnits
{
    var matchSlopeStart = (contStart == GeometricContinuity.G1 || contStart == GeometricContinuity.G2);
    var matchCurvStart  = (contStart == GeometricContinuity.G2);
    var matchSlopeEnd   = (contEnd   == GeometricContinuity.G1 || contEnd   == GeometricContinuity.G2);
    var matchCurvEnd    = (contEnd   == GeometricContinuity.G2);

    var s2 = s * s;
    var s3 = s2 * s;
    var s4 = s3 * s;
    var s5 = s4 * s;

    if (!matchSlopeStart && !matchSlopeEnd)
    {
        // G0+G0: linear
        return h0 * (1 - s) + h1 * s;
    }
    else if (matchSlopeStart && !matchCurvStart && !matchSlopeEnd)
    {
        // G1+G0: quadratic — h0, m0, h1
        return h0 + m0 * s + (h1 - h0 - m0) * s2;
    }
    else if (!matchSlopeStart && matchSlopeEnd && !matchCurvEnd)
    {
        // G0+G1: quadratic — h0, h1, m1
        var dh = h1 - h0;
        return h0 + (2 * dh - m1) * s + (m1 - dh) * s2;
    }
    else if (matchSlopeStart && !matchCurvStart && matchSlopeEnd && !matchCurvEnd)
    {
        // G1+G1: cubic Hermite — h0, m0, h1, m1
        return h0 * (2*s3 - 3*s2 + 1) + m0 * (s3 - 2*s2 + s)
             + h1 * (-2*s3 + 3*s2)    + m1 * (s3 - s2);
    }
    else if (matchCurvStart && !matchSlopeEnd)
    {
        // G2+G0: cubic — h0, m0, k0, h1
        return h0 + m0 * s + (k0 / 2) * s2 + (h1 - h0 - m0 - k0 / 2) * s3;
    }
    else if (!matchSlopeStart && matchCurvEnd)
    {
        // G0+G2: cubic — h0, h1, m1, k1
        var dh = h1 - h0;
        var d  = dh - m1 + k1 / 2;
        var c  = -(k1 + 3 * (dh - m1));
        var b  = 3 * dh - 2 * m1 + k1 / 2;
        return h0 + b * s + c * s2 + d * s3;
    }
    else if (matchCurvStart && matchSlopeEnd && !matchCurvEnd)
    {
        // G2+G1: quartic — h0, m0, k0, h1, m1
        var dh = h1 - h0;
        var a4 = m1 + 2 * m0 + k0 / 2 - 3 * dh;
        var a3 = 4 * dh - 3 * m0 - k0 - m1;
        return h0 + m0 * s + (k0 / 2) * s2 + a3 * s3 + a4 * s4;
    }
    else if (matchSlopeStart && !matchCurvStart && matchCurvEnd)
    {
        // G1+G2: quartic — h0, m0, h1, m1, k1
        var H  = h1 - h0 - m0;
        var M  = m1 - m0;
        var K  = k1;
        var e  = (K - 4 * M + 6 * H) / 2;
        var d  = 5 * M - 8 * H - K;
        var c  = 6 * H - 3 * M + K / 2;
        return h0 + m0 * s + c * s2 + d * s3 + e * s4;
    }
    else
    {
        // G2+G2: quintic Hermite — h0, m0, k0, h1, m1, k1
        return h0 * (1 - 10*s3 + 15*s4 - 6*s5)
             + m0 * (s - 6*s3 + 8*s4 - 3*s5)
             + k0 * (s2/2 - 3*s3/2 + 3*s4/2 - s5/2)
             + h1 * (10*s3 - 15*s4 + 6*s5)
             + m1 * (-4*s3 + 7*s4 - 3*s5)
             + k1 * (s3/2 - s4 + s5/2);
    }
}


/**
 * Samples n points across the blend zone [tBlendStart, tBlendEnd].
 * Normal and binormal offsets are blended independently via blendOffsetAt.
 *
 * Heights at the blend boundaries are evaluated at the actual tPath positions
 * (not region endpoints) so the blend wire meets the trimmed region wire.
 */
function generateBlendPoints(context is Context, pathInfo is map, definition is map,
    tBlendStart is number, tBlendEnd is number,
    regA is map, regB is map, intr is map) returns array
{
    var n = definition.numRegionPoints;
    var L = tBlendEnd - tBlendStart;
    var contStart = intr.startContinuity;
    var contEnd   = intr.endContinuity;

    var needSlopeStart = (contStart == GeometricContinuity.G1 || contStart == GeometricContinuity.G2);
    var needSlopeEnd   = (contEnd   == GeometricContinuity.G1 || contEnd   == GeometricContinuity.G2);
    var needCurvStart  = (contStart == GeometricContinuity.G2);
    var needCurvEnd    = (contEnd   == GeometricContinuity.G2);

    var nOff0 = 0 * meter; var nSlope0 = 0 * meter; var nCurv0 = 0 * meter;
    var bOff0 = 0 * meter; var bSlope0 = 0 * meter; var bCurv0 = 0 * meter;
    var nOff1 = 0 * meter; var nSlope1 = 0 * meter; var nCurv1 = 0 * meter;
    var bOff1 = 0 * meter; var bSlope1 = 0 * meter; var bCurv1 = 0 * meter;

    if (needSlopeStart || needCurvStart)
    {
        var dA   = computeOffsetDerivativesAt(regA, tBlendStart);
        nOff0    = dA.normalOff;
        bOff0    = dA.binormalOff;
        nSlope0  = dA.normalSlope   * L;
        bSlope0  = dA.binormalSlope * L;
        if (needCurvStart)
        {
            nCurv0 = dA.normalCurv   * L * L;
            bCurv0 = dA.binormalCurv * L * L;
        }
    }
    else
    {
        var offA = computeOffsetsAt(regA, tBlendStart);
        nOff0 = offA.normalOff;
        bOff0 = offA.binormalOff;
    }

    if (needSlopeEnd || needCurvEnd)
    {
        var dB   = computeOffsetDerivativesAt(regB, tBlendEnd);
        nOff1    = dB.normalOff;
        bOff1    = dB.binormalOff;
        nSlope1  = dB.normalSlope   * L;
        bSlope1  = dB.binormalSlope * L;
        if (needCurvEnd)
        {
            nCurv1 = dB.normalCurv   * L * L;
            bCurv1 = dB.binormalCurv * L * L;
        }
    }
    else
    {
        var offB = computeOffsetsAt(regB, tBlendEnd);
        nOff1 = offB.normalOff;
        bOff1 = offB.binormalOff;
    }

    var points = [];
    for (var i = 0; i < n; i += 1)
    {
        var s    = i / (n - 1);
        var t    = tBlendStart + L * s;
        var nOff = blendOffsetAt(s, nOff0, nSlope0, nCurv0, nOff1, nSlope1, nCurv1, contStart, contEnd);
        var bOff = blendOffsetAt(s, bOff0, bSlope0, bCurv0, bOff1, bSlope1, bCurv1, contStart, contEnd);
        points = append(points, computeOffsetPoint(context, pathInfo, definition, t, nOff, bOff));
    }
    return points;
}


// ─── Wire construction ────────────────────────────────────────────────────────

// True when the source edge covering path parameter t is a circular arc.
function sourceEdgeIsArc(context is Context, frenetPath is map, t is number, totalLength is ValueWithUnits) returns boolean
{
    var edgeData = frenetPath.edgeData;
    var n        = size(edgeData);
    var arcLen   = t * totalLength;

    var ei = n - 1;
    for (var i = 0; i < n - 1; i += 1)
    {
        if (edgeData[i + 1].startArcLength > arcLen)
        {
            ei = i;
            break;
        }
    }

    var curveDef = evCurveDefinition(context, { "edge" : edgeData[ei].query });
    return curveDef.curveType == CurveType.CIRCLE;
}


// True when both offset components are constant (within tolerance) across [tA, tB].
// A constant offset over a circular source edge yields an exact concentric/translated arc.
function offsetConstantOver(region is map, tA is number, tB is number) returns boolean
{
    var nSamp = 5;
    var n0    = 0 * meter;
    var b0    = 0 * meter;
    var maxN  = 0 * meter;
    var maxB  = 0 * meter;

    for (var i = 0; i < nSamp; i += 1)
    {
        var t    = tA + (tB - tA) * i / (nSamp - 1);
        var offs = computeOffsetsAt(region, t);
        if (i == 0)
        {
            n0 = offs.normalOff;
            b0 = offs.binormalOff;
        }
        else
        {
            var dn = abs(offs.normalOff - n0);
            var db = abs(offs.binormalOff - b0);
            if (dn > maxN)
            {
                maxN = dn;
            }
            if (db > maxB)
            {
                maxB = db;
            }
        }
    }

    return (maxN < 1e-6 * meter) && (maxB < 1e-6 * meter);
}


// Point at the mid of the circular arc that STARTS at pStart travelling along tStartOut
// (unit, outgoing tangent) and ENDS at pEnd. The returned point lies on the actual traveled
// arc at HALF the swept angle -- the correct side / branch, so it uniquely reconstructs the
// intended arc via a 3-point skArc. pStart/pEnd carry length units; tStartOut is unitless.
// Returns { "mid" : Vector, "straight" : boolean }. When the arc degenerates to a straight
// line (chord parallel to the tangent) the chord midpoint is returned with straight = true.
function biarcArcMid(pStart is Vector, tStartOut is Vector, pEnd is Vector) returns map
{
    const c = pEnd - pStart;
    const cLen = norm(c);
    if (cLen / meter < 1e-12)
    {
        return { "mid" : pStart, "straight" : true };
    }

    // Straightness: chord parallel to the tangent -> zero curvature.
    const sinTheta = norm(cross(tStartOut, c / cLen));
    if (sinTheta < 1e-7)
    {
        return { "mid" : pStart + 0.5 * c, "straight" : true };
    }

    // Plane of the arc, and the in-plane unit normal to the tangent.
    const nPlane = normalize(cross(tStartOut, c));
    const mPerp  = normalize(cross(nPlane, tStartOut));

    // Center on the perpendicular through pStart, equidistant from pStart and pEnd:
    //   |s*mPerp|^2 = |s*mPerp - c|^2  =>  s = |c|^2 / (2 * mPerp . c).
    const s      = dot(c, c) / (2 * dot(mPerp, c));
    const center = pStart + s * mPerp;
    const R      = abs(s);

    // Circle frame oriented so travel from pStart along tStartOut is CCW (+) about nTravel.
    const e0      = (pStart - center) / R;
    const nTravel = normalize(cross(e0, tStartOut));
    const yA      = cross(nTravel, e0);

    // Signed sweep from pStart (angle 0) to pEnd, on the positive (traveled) branch.
    const ve = pEnd - center;
    const ex = dot(ve, e0);
    const ey = dot(ve, yA);
    var sweep = atan2(ey / meter, ex / meter) / radian;
    if (sweep <= 0)
    {
        sweep = sweep + 2 * PI;
    }

    const midAng = (sweep / 2) * radian;
    const mid    = center + R * (cos(midAng) * e0 + sin(midAng) * yA);
    return { "mid" : mid, "straight" : false };
}


// Constructs an equal-tangent-length (Bolton/Sabin k=1) 3D biarc interpolating POSITION and
// TANGENT at both endpoints, meeting G1 at a joint J. Returns one mid point on each sub-arc so
// each can be drawn as skArc({start, mid, end}): arc A = (p0, midA, J), arc B = (J, midB, p1).
// p0/p1 length units; t0/t1 unit tangents in the DIRECTION OF TRAVEL from p0 toward p1.
// Returns { "ok" : boolean, "joint" : Vector, "midA" : Vector, "midB" : Vector }.
function computeBiarcPoints(p0 is Vector, t0 is Vector, p1 is Vector, t1 is Vector) returns map
{
    const fail = { "ok" : false, "joint" : p0, "midA" : p0, "midB" : p1 };

    const d    = p1 - p0;
    const dd   = dot(d, d);
    const dLen = norm(d);
    if (dLen / meter < 1e-9)
    {
        return fail;
    }

    const ct = dot(t0, t1);          // cos(angle between tangents)
    const a  = 1 - ct;               // >= 0; 0 iff tangents identical
    const ds = dot(d, t0 + t1);

    // Equal-tangent-length condition |Q0 - Q1| = 2*alpha yields
    //   2*a*alpha^2 + 2*ds*alpha - dd = 0 , a = 1 - t0.t1 .
    var alpha;
    if (a < 1e-12)
    {
        // Parallel, same-direction tangents: quadratic collapses to the linear special case.
        if (ds / dLen < 1e-9)
        {
            return fail;
        }
        alpha = dd / (2 * ds);
    }
    else
    {
        const disc = ds * ds + 2 * a * dd;
        alpha = (-ds + sqrt(disc)) / (2 * a);
    }

    if (alpha / meter < 1e-12)
    {
        return fail;
    }

    const Q0 = p0 + alpha * t0;
    const Q1 = p1 - alpha * t1;
    const J  = 0.5 * (Q0 + Q1);

    const jm = Q1 - Q0;
    if (norm(jm) / meter < 1e-12)
    {
        return fail;
    }
    const tJ = normalize(jm);

    if (norm(J - p0) / meter < 1e-9 || norm(p1 - J) / meter < 1e-9)
    {
        return fail;
    }

    const resA = biarcArcMid(p0, t0, J);
    const resB = biarcArcMid(J, tJ, p1);

    return {
        "ok"    : true,
        "joint" : J,
        "midA"  : resA.mid,
        "midB"  : resB.mid
    };
}


// Builds a sketch arc through 3 world points on their common plane, under arcId.
// Returns qCreatedBy(arcId, EntityType.BODY), or undefined if the points are collinear
// (no unique arc) so the caller can fall back to a spline.
function emitArc3Point(context is Context, arcId is Id, pStart is Vector, pMid is Vector, pEnd is Vector)
{
    var nrm = cross(pMid - pStart, pEnd - pStart);
    if (norm(nrm) < 1e-9 * meter * meter)
    {
        return undefined;
    }

    var pl = plane(pStart, normalize(nrm));
    var sk = newSketchOnPlane(context, arcId, { "sketchPlane" : pl });
    skArc(sk, "arc", {
        "start" : worldToPlane(pl, pStart),
        "mid"   : worldToPlane(pl, pMid),
        "end"   : worldToPlane(pl, pEnd)
    });
    skSolve(sk);

    return qCreatedBy(arcId, EntityType.BODY);
}


// Emits the constant-offset arc over [tA, tB] as a sketch arc (3-point start/mid/end) so the
// output edge shows a radius. Returns the created sketch body query, or undefined when the
// three sample points are collinear (not a real arc) so the caller falls back to a spline.
function emitOffsetArc(context is Context, wireId is Id, pathInfo is map, definition is map,
    region is map, tA is number, tB is number)
{
    var tMid = (tA + tB) / 2;
    var oA   = computeOffsetsAt(region, tA);
    var oM   = computeOffsetsAt(region, tMid);
    var oB   = computeOffsetsAt(region, tB);
    var p0   = computeOffsetPoint(context, pathInfo, definition, tA,   oA.normalOff, oA.binormalOff);
    var pM   = computeOffsetPoint(context, pathInfo, definition, tMid, oM.normalOff, oM.binormalOff);
    var p1   = computeOffsetPoint(context, pathInfo, definition, tB,   oB.normalOff, oB.binormalOff);

    return emitArc3Point(context, wireId, p0, pM, p1);
}


function buildOutputWire(context is Context, id is Id, definition is map,
    pathInfo is map, sortedRegions is array)
{
    var allWireBodies = [];
    var allWireEdges  = [];

    // Collect active blend zones (SINGLE_REGION mode declares no intersections)
    var blendZones = [];
    var intersections = (definition.intersections != undefined) ? definition.intersections : [];
    for (var intr in intersections)
    {
        if (!intr.isValid || !intr.blend)
            continue;

        var regA = undefined;
        var regB = undefined;
        for (var reg in sortedRegions)
        {
            if (reg.regionName == intr.region1) regA = reg;
            if (reg.regionName == intr.region2) regB = reg;
        }
        if (regA == undefined || regB == undefined)
            continue;

        var tBlendStart = regA.tEnd   - intr.startDist / pathInfo.length;
        var tBlendEnd   = regB.tStart + intr.endDist   / pathInfo.length;

        if (tBlendStart < regA.tStart)
        {
            reportFeatureWarning(context, id, "Blend start distance between '" ~ regA.regionName ~
                "' and '" ~ regB.regionName ~ "' exceeds the extent of '" ~ regA.regionName ~
                "'. Clamping to region start.");
            tBlendStart = regA.tStart;
        }
        if (tBlendEnd > regB.tEnd)
        {
            reportFeatureWarning(context, id, "Blend end distance between '" ~ regA.regionName ~
                "' and '" ~ regB.regionName ~ "' exceeds the extent of '" ~ regB.regionName ~
                "'. Clamping to region end.");
            tBlendEnd = regB.tEnd;
        }
        if (tBlendStart >= tBlendEnd)
        {
            reportFeatureWarning(context, id, "Blend zone between '" ~ regA.regionName ~
                "' and '" ~ regB.regionName ~ "' has zero or negative length after clamping. Skipping blend.");
            continue;
        }

        blendZones = append(blendZones, {
            "tBlendStart" : tBlendStart,
            "tBlendEnd"   : tBlendEnd,
            "regA"        : regA,
            "regB"        : regB,
            "intr"        : intr
        });
    }

    // Collect edge-boundary t-values (one per inter-edge junction in the FrenetPath)
    var edgeBoundaryTs = [];
    var edgeData = pathInfo.frenetPath.edgeData;
    for (var ei = 1; ei < size(edgeData); ei += 1)
        edgeBoundaryTs = append(edgeBoundaryTs, edgeData[ei].startArcLength / pathInfo.length);

    // One or more BSpline wires per region — split at edge boundaries so each
    // output curve spans at most one source edge.
    for (var ri = 0; ri < size(sortedRegions); ri += 1)
    {
        var reg       = sortedRegions[ri];
        var tSegStart = reg.tStart;
        var tSegEnd   = reg.tEnd;

        for (var bz in blendZones)
        {
            if (bz.regA.regionName == reg.regionName)
                tSegEnd   = min(tSegEnd,   bz.tBlendStart);
            if (bz.regB.regionName == reg.regionName)
                tSegStart = max(tSegStart, bz.tBlendEnd);
        }

        if (tSegEnd - tSegStart < 1e-6)
        {
            reportFeatureWarning(context, id, "Region '" ~ reg.regionName ~
                "' was fully consumed by adjacent blend zones and produced no output. " ~
                "Reduce blend distances or expand the region extent.");
            continue;
        }

        // Build the ordered list of split points: region endpoints + any edge
        // boundaries that fall strictly inside the trimmed segment
        var splitTs = [tSegStart];
        for (var tb in edgeBoundaryTs)
        {
            if (tb > tSegStart + 1e-6 && tb < tSegEnd - 1e-6)
                splitTs = append(splitTs, tb);
        }
        splitTs = append(splitTs, tSegEnd);

        for (var si = 0; si < size(splitTs) - 1; si += 1)
        {
            var tA = splitTs[si];
            var tB = splitTs[si + 1];
            if (tB - tA < 1e-6)
            {
                continue;
            }

            var wireId = id + ("reg_" ~ toString(ri) ~ "_" ~ toString(si));
            var tMid   = (tA + tB) / 2;

            // Arc preservation. When this sub-curve lies on a single circular source edge:
            //   - constant offset            -> exact concentric/translated arc (always)
            //   - varying offset + BIARC mode -> two tangent arcs matching both endpoint
            //                                    offsets and staying tangent to neighbors
            // Anything else falls through to the best-fit spline below.
            if (sourceEdgeIsArc(context, pathInfo.frenetPath, tMid, pathInfo.length))
            {
                if (offsetConstantOver(reg, tA, tB))
                {
                    var arcBody = emitOffsetArc(context, wireId, pathInfo, definition, reg, tA, tB);
                    if (arcBody != undefined)
                    {
                        allWireBodies = append(allWireBodies, arcBody);
                        allWireEdges  = append(allWireEdges, qCreatedBy(wireId, EntityType.EDGE));
                        if (definition.printCurveDetails)
                        {
                            println("=== Region " ~ toString(ri) ~ " ('" ~ reg.regionName ~ "') sub " ~ toString(si) ~ " [ARC] ===");
                            println("  t range: [" ~ toString(tA) ~ ", " ~ toString(tB) ~ "]");
                        }
                        if (definition.showRegions)
                        {
                            addDebugEntities(context, arcBody, (ri % 2 == 0) ? DebugColor.CYAN : DebugColor.MAGENTA);
                        }
                        continue;
                    }
                }
                else if (definition.arcMode == VaryingArcMode.BIARC)
                {
                    var oA = computeOffsetsAt(reg, tA);
                    var oB = computeOffsetsAt(reg, tB);
                    var p0 = computeOffsetPoint(context, pathInfo, definition, tA, oA.normalOff, oA.binormalOff);
                    var p1 = computeOffsetPoint(context, pathInfo, definition, tB, oB.normalOff, oB.binormalOff);
                    var t0 = offsetTangentAt(context, pathInfo, definition, reg, tA);
                    var t1 = offsetTangentAt(context, pathInfo, definition, reg, tB);
                    var bi = computeBiarcPoints(p0, t0, p1, t1);

                    // Pre-check both legs are non-collinear (same threshold emitArc3Point uses),
                    // so we only create sketches when BOTH will succeed -- no orphan bodies.
                    var legAOk = norm(cross(bi.midA - p0,       bi.joint - p0)) >= 1e-9 * meter * meter;
                    var legBOk = norm(cross(bi.midB - bi.joint, p1 - bi.joint)) >= 1e-9 * meter * meter;

                    if (bi.ok && legAOk && legBOk)
                    {
                        var idA = wireId + "A";
                        var idB = wireId + "B";
                        emitArc3Point(context, idA, p0, bi.midA, bi.joint);
                        emitArc3Point(context, idB, bi.joint, bi.midB, p1);
                        allWireBodies = append(allWireBodies, qCreatedBy(idA, EntityType.BODY));
                        allWireBodies = append(allWireBodies, qCreatedBy(idB, EntityType.BODY));
                        allWireEdges  = append(allWireEdges, qCreatedBy(idA, EntityType.EDGE));
                        allWireEdges  = append(allWireEdges, qCreatedBy(idB, EntityType.EDGE));
                        if (definition.printCurveDetails)
                        {
                            println("=== Region " ~ toString(ri) ~ " ('" ~ reg.regionName ~ "') sub " ~ toString(si) ~ " [BIARC] ===");
                            println("  t range: [" ~ toString(tA) ~ ", " ~ toString(tB) ~ "]");
                        }
                        if (definition.showRegions)
                        {
                            addDebugEntities(context, qCreatedBy(idA, EntityType.BODY), (ri % 2 == 0) ? DebugColor.CYAN : DebugColor.MAGENTA);
                            addDebugEntities(context, qCreatedBy(idB, EntityType.BODY), (ri % 2 == 0) ? DebugColor.CYAN : DebugColor.MAGENTA);
                        }
                        continue;
                    }
                    // biarc degenerate or a near-straight leg -> fall through to spline
                }
            }

            var seg = generateSegmentPoints(context, pathInfo, definition, reg, tA, tB);
            var pts = seg.points;
            if (size(pts) < 2)
            {
                continue;
            }

            // Ensure the CP cap can accommodate all interior pins (endpoints + stations +
            // plateau edges); otherwise approximateSpline can fail rather than just warn.
            var effMaxCP = max([definition.approxMaxCP, size(seg.interpolateIndices) + 2]);
            if (effMaxCP > definition.approxMaxCP)
            {
                reportFeatureWarning(context, id, "Region '" ~ reg.regionName ~
                    "': raised max control points to " ~ toString(effMaxCP) ~
                    " to honor all interior station/dwell pins.");
            }

            var bspline = approximateSpline(context, {
                "degree"             : definition.approxDegree,
                "tolerance"          : definition.approxTolerance,
                "isPeriodic"         : false,
                "maxControlPoints"   : effMaxCP,
                "targets"            : [approximationTarget({ "positions" : pts })],
                "interpolateIndices" : seg.interpolateIndices
            })[0];

            opCreateBSplineCurve(context, wireId, { "bSplineCurve" : bspline });
            allWireBodies = append(allWireBodies, qCreatedBy(wireId, EntityType.BODY));
            allWireEdges  = append(allWireEdges, qCreatedBy(wireId, EntityType.EDGE));

            if (definition.printCurveDetails)
            {
                println("=== Region " ~ toString(ri) ~ " ('" ~ reg.regionName ~ "') sub " ~ toString(si) ~ " ===");
                println("  degree:  " ~ toString(bspline.degree));
                println("  CPs:     " ~ toString(size(bspline.controlPoints)));
                println("  samples: " ~ toString(size(pts)));
                println("  t range: [" ~ toString(tA) ~ ", " ~ toString(tB) ~ "]");
            }

            if (definition.showRegions)
            {
                addDebugEntities(context, qCreatedBy(wireId, EntityType.BODY),
                    (ri % 2 == 0) ? DebugColor.CYAN : DebugColor.MAGENTA);
            }
        }
    }

    // One BSpline wire per active blend zone
    for (var bzi = 0; bzi < size(blendZones); bzi += 1)
    {
        var bz  = blendZones[bzi];
        var pts = generateBlendPoints(context, pathInfo, definition,
            bz.tBlendStart, bz.tBlendEnd, bz.regA, bz.regB, bz.intr);
        if (size(pts) < 2) continue;

        var bspline = approximateSpline(context, {
            "degree"             : definition.approxDegree,
            "tolerance"          : definition.approxTolerance,
            "isPeriodic"         : false,
            "maxControlPoints"   : definition.approxMaxCP,
            "targets"            : [approximationTarget({ "positions" : pts })],
            "interpolateIndices" : [0, size(pts) - 1]
        })[0];

        var wireId = id + ("blend_" ~ toString(bzi));
        opCreateBSplineCurve(context, wireId, { "bSplineCurve" : bspline });
        allWireBodies = append(allWireBodies, qCreatedBy(wireId, EntityType.BODY));
        allWireEdges  = append(allWireEdges, qCreatedBy(wireId, EntityType.EDGE));

        if (definition.printCurveDetails)
        {
            println("=== Blend " ~ toString(bzi) ~ " ('" ~ bz.regA.regionName ~ "' → '" ~ bz.regB.regionName ~ "') ===");
            println("  degree:  " ~ toString(bspline.degree));
            println("  CPs:     " ~ toString(size(bspline.controlPoints)));
            println("  samples: " ~ toString(size(pts)));
        }

        if (definition.showBlends)
            addDebugEntities(context, qCreatedBy(wireId, EntityType.BODY), DebugColor.YELLOW);
    }

    // Collect all edges from individual wire bodies into one combined wire, then delete originals
    if (size(allWireBodies) > 0)
    {
        opExtractWires(context, id + "mergeWires", {
            "edges" : qUnion(allWireEdges)
        });
        opDeleteBodies(context, id + "deleteSourceWires", {
            "entities" : qUnion(allWireBodies)
        });
    }
}
