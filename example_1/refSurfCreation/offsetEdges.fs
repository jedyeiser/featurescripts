FeatureScript 2909;
import(path : "onshape/std/common.fs", version : "2909.0");
export import(path : "onshape/std/geometriccontinuity.gen.fs", version : "2909.0");

//import CurveWrapping_full/curveMappingCore
import(path : "08e8748f2ef24eea16072b75/d10f79db069c0599ead6b0bd/683d867c35fdab9c98d47556", version : "f8390061d90f2b059777b525");
//import CurveWrapping_full/Utils
import(path : "08e8748f2ef24eea16072b75/d10f79db069c0599ead6b0bd/ad98c7f43a25a4c0e8a428e7", version : "af176e222f5dedf312114187");


// --- Enums --------------------------------------------------------------------

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
    annotation { "Name" : "Convert to splines" }
    SPLINE,
    annotation { "Name" : "Keep as arcs" }
    BIARC
}

export enum OffsetMode
{
    annotation { "Name" : "Multiple regions" }
    MULTI_REGION,
    annotation { "Name" : "Single region" }
    SINGLE_REGION
}


// --- Bounds -------------------------------------------------------------------

export const RegionPointsBounds    = {(unitless)   : [20, 20, 100]}        as IntegerBoundSpec;
export const OffsetBounds          = {(millimeter) : [-100, 0, 100]}       as LengthBoundSpec;
export const DelayBounds           = {(millimeter) : [0, 0, 1000]}         as LengthBoundSpec;
export const ApproxToleranceBounds = {(millimeter) : [0.001, 0.01, 1]}     as LengthBoundSpec;
export const ApproxDegreeBounds    = {(unitless)   : [2, 3, 5]}            as IntegerBoundSpec;
export const ApproxMaxCPBounds     = {(unitless)   : [10, 100, 500]}       as IntegerBoundSpec;


// --- Editing logic ------------------------------------------------------------

export function generateOffsetEdgesEditingLogic(context is Context, id is Id,
    oldDefinition is map, definition is map, isCreating is boolean,
    specifiedParameters is map, hiddenBodies is Query) returns map
{
    // Single-region mode: auto-name the interior offsets ("Offset 1", ...); nothing else to manage.
    if (definition.offsetMode == OffsetMode.SINGLE_REGION)
    {
        if (definition.interiorOffsets != undefined)
        {
            var namedOffsets = [];
            for (var i = 0; i < size(definition.interiorOffsets); i += 1)
            {
                var off = definition.interiorOffsets[i];
                if (off.offsetName == "" || off.offsetName == undefined)
                {
                    off.offsetName = "Offset " ~ toString(i + 1);
                }
                namedOffsets = append(namedOffsets, off);
            }
            definition.interiorOffsets = namedOffsets;
        }
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


// --- Feature ------------------------------------------------------------------

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

        annotation { "Name" : "Source arcs", "Default" : VaryingArcMode.SPLINE,
                     "Description" : "How circular source edges are output. Keep as arcs: each becomes a true arc (clicking it shows a radius) -- one concentric arc where the offset is constant, two tangent arcs where it varies (both end offsets and tangents exact, the joint placed closest to the true offset); blends over arcs are arcs too. Convert to splines: fitted splines, like every other source edge. Source splines are always output as splines." }
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
                         "Item label template" : "#offsetName",
                         "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS,
                         "Description" : "Ordered offset control points along the path. The profile flows through them with the transfer type and holds flat beyond the first and last." }
            definition.interiorOffsets is array;
            for (var off in definition.interiorOffsets)
            {
                annotation { "Name" : "Offset name" }
                off.offsetName is string;

                annotation { "Name" : "Location type", "Default" : RegionExtentType.QUERY,
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
                off.pointOffsetType is OffsetType;

                if (off.pointOffsetType == OffsetType.NORMAL || off.pointOffsetType == OffsetType.BOTH)
                {
                    annotation { "Name" : "Normal offset" }
                    isLength(off.normalOffset, OffsetBounds);
                }
                if (off.pointOffsetType == OffsetType.BINORMAL || off.pointOffsetType == OffsetType.BOTH)
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
                         "Description" : "Print per-edge arc lengths and lengths" }
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
                        ~ "  len="      ~ toString(ed.length          / millimeter) ~ "mm");
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


// --- Path processing ----------------------------------------------------------

// A frame at any arc length along the path. Only origin and tangent (zAxis) are used: the
// offset directions come from this feature's own parallel-transport table, seeded once at
// s = 0 by [ptSeedNormal].
//
// (Until 2026-09-23 this read per-edge Frenet normals with sign tracking from the July
// curveMapping core, whose buildFrenetPath threw "Reference edge normals are not coplanar"
// when three or more consecutive flat arcs (sagitta < 0.1% of length, e.g. R 20 m over
// 100 mm) were treated as lines and the middle one fell back to a world-axis normal. The
// current core carries one normal along the whole path and has no such check.)
function frameAtArc(context is Context, frenetPath is map, arcLength) returns map
{
    var edgeData = frenetPath.edgeData;
    var s = arcLength;
    if (s < 0 * meter)
    {
        s = 0 * meter;
    }
    if (s > frenetPath.totalLength)
    {
        s = frenetPath.totalLength;
    }

    var lo = 0;
    var hi = size(edgeData) - 1;
    while (lo < hi)
    {
        var mid = ceil((lo + hi) / 2);
        if (edgeData[mid].startArcLength <= s)
        {
            lo = mid;
        }
        else
        {
            hi = mid - 1;
        }
    }
    var ed = edgeData[lo];
    if (ed.exactFrames != true)
    {
        return getFrameAtArcLength(context, frenetPath, s);
    }

    // Curved source edge: the real curve, not the core's sampled table. The core treats an
    // edge within 0.1% of straight as its chord (a R 20 m arc over 100 mm qualifies), so the
    // offset of such an arc came out straight -- up to the arc's sagitta off, and collinear, so
    // no arc could be built through it.
    var frac = (ed.length / meter > 1e-12) ? (s - ed.startArcLength) / ed.length : 0;
    frac = min(max(frac, 0), 1);
    var tl = evEdgeTangentLine(context, {
                "edge"                      : ed.query,
                "parameter"                 : ed.stdDir ? frac : 1 - frac,
                "arcLengthParameterization" : true
            });
    var tangent = ed.stdDir ? tl.direction : -tl.direction;
    return {
        "frame"     : coordSystem(tl.origin, perpendicularVector(tangent), tangent),
        "edgeIndex" : lo
    };
}


// True when the old core treated this edge as a line: a real line, or an edge whose control
// polygon deviates from its chord by less than 0.1% of its length.
function seedTreatsAsLine(context is Context, edgeDat is map) returns boolean
{
    var curveDef = evCurveDefinition(context, { "edge" : edgeDat.query });
    if (curveDef.curveType == CurveType.LINE)
    {
        return true;
    }
    var cps      = edgeDat.bspline.controlPoints;
    var nCPs     = size(cps);
    var chord    = cps[nCPs - 1] - cps[0];
    var chordLen = norm(chord);
    if (chordLen < 1e-10 * meter)
    {
        return false;
    }
    var chordDir = chord / chordLen;
    var maxDev   = 0 * meter;
    for (var j = 1; j < nCPs - 1; j += 1)
    {
        var diff    = cps[j] - cps[0];
        var lateral = norm(diff - dot(diff, chordDir) * chordDir);
        if (lateral > maxDev)
        {
            maxDev = lateral;
        }
    }
    return maxDev < 0.001 * edgeDat.length;
}


// Curvature normal (toward the centre) at the traversal start of an edge.
function traversalStartNormal(context is Context, edgeDat is map) returns Vector
{
    return evEdgeCurvature(context, {
                "edge"                      : edgeDat.query,
                "parameter"                 : edgeDat.stdDir ? 0 : 1,
                "arcLengthParameterization" : true
            }).frame.xAxis;
}


// The normal the transport table starts from at s = 0 -- the same choice the old core made
// there, so offsets keep their side on every chain that worked before:
//   - a circular first edge: toward its centre;
//   - a first edge the old core treated as a line: the next edge's curvature normal at its
//     start (when that edge is curved), otherwise a world-axis perpendicular;
//   - any other first edge: its curvature normal at the start.
function ptSeedNormal(context is Context, frenetPath is map, startFrame is CoordSystem) returns Vector
{
    var edgeData = frenetPath.edgeData;
    var first    = edgeData[0];
    var tangent  = startFrame.zAxis;
    var curveDef = evCurveDefinition(context, { "edge" : first.query });

    var seed;
    if (curveDef.curveType == CurveType.CIRCLE)
    {
        seed = normalize(curveDef.coordSystem.origin - startFrame.origin);
    }
    else if (seedTreatsAsLine(context, first))
    {
        if (size(edgeData) > 1 && !seedTreatsAsLine(context, edgeData[1]))
        {
            seed = traversalStartNormal(context, edgeData[1]);
        }
        else
        {
            seed = lineFrenetFrame({ "origin" : startFrame.origin, "direction" : tangent }).xAxis;
        }
    }
    else
    {
        seed = traversalStartNormal(context, first);
    }

    seed = seed - dot(seed, tangent) * tangent;
    if (norm(seed) < 1e-9)
    {
        seed = lineFrenetFrame({ "origin" : startFrame.origin, "direction" : tangent }).xAxis;
    }
    return normalize(seed);
}


// Builds a parallel transport (Bishop) frame table along the path.
//
// Starting from the Frenet frame at s=0, each step rotates the previous xAxis by
// the same rotation that carries prevTangent -> currTangent (Rodrigues formula).
// This eliminates the torsion-driven spinning of the Frenet normal while preserving
// tangent continuity -- the frame never flips direction on smooth G1 BSpline chains.
//
// Returns an array of { arcLength, xAxis } entries at numSamples uniform positions.
// yAxis = cross(tangent, xAxis) is not stored; it is computed on demand.
function buildParallelTransportTable(context is Context, frenetPath is map, numSamples is number) returns array
{
    var totalLength = frenetPath.totalLength;
    var fr0         = frameAtArc(context, frenetPath, 0 * meter);
    var prevXAxis   = ptSeedNormal(context, frenetPath, fr0.frame);
    var prevTangent = fr0.frame.zAxis;

    var table = [{ "arcLength" : 0 * meter, "xAxis" : prevXAxis }];

    for (var i = 1; i < numSamples; i += 1)
    {
        var s           = totalLength * i / (numSamples - 1);
        var fr          = frameAtArc(context, frenetPath, s);
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
    var fr = frameAtArc(context, frenetPath, arcLength);

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
    var edgeData    = frenetPath.edgeData;
    for (var i = 0; i < size(edgeData); i += 1)
    {
        var curveType = evCurveDefinition(context, { "edge" : edgeData[i].query }).curveType;
        edgeData[i]   = mergeMaps(edgeData[i], { "exactFrames" : curveType != CurveType.LINE });
    }
    frenetPath      = mergeMaps(frenetPath, { "edgeData" : edgeData });
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


// --- Region processing --------------------------------------------------------
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
        var ot   = off.pointOffsetType;
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


// --- Profile evaluation -------------------------------------------------------

/**
 * Returns the interpolated offset at normalized position t in [0,1] within a region.
 *   LINEAR    : linear ramp
 *   QUADRATIC : true quadratic -- one endpoint has zero slope
 *   SMOOTH    : smootherstep (6t^5 - 15t^4 + 10t^3) -- C2 at both endpoints
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


// --- Offset point computation -------------------------------------------------

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


// --- Point arrays -------------------------------------------------------------

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
 * Evaluates the Hermite blend polynomial at s in [0,1] for a single scalar offset component.
 *
 * All slope/curvature args are in s-space (multiply region dOffset/dt by L, d^2Offset/dt^2 by L^2
 * before calling, where L = tBlendEnd - tBlendStart).
 *
 * Continuity dispatch:
 *   G0+G0 -> linear          G1+G0 / G0+G1 -> quadratic
 *   G1+G1 -> cubic Hermite   G2+G0 / G0+G2 -> cubic
 *   G2+G1 / G1+G2 -> quartic               G2+G2 -> quintic Hermite
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
        // G1+G0: quadratic -- h0, m0, h1
        return h0 + m0 * s + (h1 - h0 - m0) * s2;
    }
    else if (!matchSlopeStart && matchSlopeEnd && !matchCurvEnd)
    {
        // G0+G1: quadratic -- h0, h1, m1
        var dh = h1 - h0;
        return h0 + (2 * dh - m1) * s + (m1 - dh) * s2;
    }
    else if (matchSlopeStart && !matchCurvStart && matchSlopeEnd && !matchCurvEnd)
    {
        // G1+G1: cubic Hermite -- h0, m0, h1, m1
        return h0 * (2*s3 - 3*s2 + 1) + m0 * (s3 - 2*s2 + s)
             + h1 * (-2*s3 + 3*s2)    + m1 * (s3 - s2);
    }
    else if (matchCurvStart && !matchSlopeEnd)
    {
        // G2+G0: cubic -- h0, m0, k0, h1
        return h0 + m0 * s + (k0 / 2) * s2 + (h1 - h0 - m0 - k0 / 2) * s3;
    }
    else if (!matchSlopeStart && matchCurvEnd)
    {
        // G0+G2: cubic -- h0, h1, m1, k1
        var dh = h1 - h0;
        var d  = dh - m1 + k1 / 2;
        var c  = -(k1 + 3 * (dh - m1));
        var b  = 3 * dh - 2 * m1 + k1 / 2;
        return h0 + b * s + c * s2 + d * s3;
    }
    else if (matchCurvStart && matchSlopeEnd && !matchCurvEnd)
    {
        // G2+G1: quartic -- h0, m0, k0, h1, m1
        var dh = h1 - h0;
        var a4 = m1 + 2 * m0 + k0 / 2 - 3 * dh;
        var a3 = 4 * dh - 3 * m0 - k0 - m1;
        return h0 + m0 * s + (k0 / 2) * s2 + a3 * s3 + a4 * s4;
    }
    else if (matchSlopeStart && !matchCurvStart && matchCurvEnd)
    {
        // G1+G2: quartic -- h0, m0, h1, m1, k1
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
        // G2+G2: quintic Hermite -- h0, m0, k0, h1, m1, k1
        return h0 * (1 - 10*s3 + 15*s4 - 6*s5)
             + m0 * (s - 6*s3 + 8*s4 - 3*s5)
             + k0 * (s2/2 - 3*s3/2 + 3*s4/2 - s5/2)
             + h1 * (10*s3 - 15*s4 + 6*s5)
             + m1 * (-4*s3 + 7*s4 - 3*s5)
             + k1 * (s3/2 - s4 + s5/2);
    }
}


/**
 * The offsets across a blend zone [tBlendStart, tBlendEnd] as a zone (see [regionZone]):
 * normal and binormal offsets blended independently via blendOffsetAt, from the heights (and
 * slopes / curvatures the continuity asks for) of the two regions at the blend ends -- the
 * actual tPath positions, not the region endpoints, so the blend meets the trimmed regions.
 */
function blendZone(pathInfo is map, definition is map, bz is map) returns map
{
    var tBlendStart = bz.tBlendStart;
    var tBlendEnd   = bz.tBlendEnd;
    var regA = bz.regA;
    var regB = bz.regB;
    var L = tBlendEnd - tBlendStart;
    var contStart = bz.intr.startContinuity;
    var contEnd   = bz.intr.endContinuity;

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

    return {
        "offsetsAt" : function(t)
            {
                var s = (t - tBlendStart) / L;
                return {
                    "normalOff"   : blendOffsetAt(s, nOff0, nSlope0, nCurv0, nOff1, nSlope1, nCurv1, contStart, contEnd),
                    "binormalOff" : blendOffsetAt(s, bOff0, bSlope0, bCurv0, bOff1, bSlope1, bCurv1, contStart, contEnd)
                };
            },
        "tLo" : tBlendStart,
        "tHi" : tBlendEnd
    };
}


// --- Wire construction --------------------------------------------------------

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


// The offsets of one zone (a region or a blend) as a function of path parameter t:
// function(t) returns { "normalOff", "binormalOff" }, plus the zone's extent [tLo, tHi]
// (tangents are read one-sided at its ends).
function regionZone(region is map) returns map
{
    return {
        "offsetsAt" : function(t) { return computeOffsetsAt(region, t); },
        "tLo"       : region.tStart,
        "tHi"       : region.tEnd
    };
}


// A zone's offsets at path parameter t.
function zoneOffsets(zone is map, t is number) returns map
{
    const offsetsAt = zone.offsetsAt;
    return offsetsAt(t);
}


// True when both offset components of a zone are constant (within 1 um) across [tA, tB]; a
// constant offset over a circular source edge is exactly a concentric / translated arc.
function zoneOffsetConstant(zone is map, tA is number, tB is number) returns boolean
{
    var o0 = zoneOffsets(zone, tA);
    for (var i = 1; i < 5; i += 1)
    {
        var o = zoneOffsets(zone, tA + (tB - tA) * i / 4);
        if (abs(o.normalOff - o0.normalOff) > 1e-6 * meter || abs(o.binormalOff - o0.binormalOff) > 1e-6 * meter)
        {
            return false;
        }
    }
    return true;
}


// World point of a zone's offset curve at path parameter t.
function zonePoint(context is Context, pathInfo is map, definition is map, zone is map, t is number) returns Vector
{
    var o = zoneOffsets(zone, t);
    return computeOffsetPoint(context, pathInfo, definition, t, o.normalOff, o.binormalOff);
}


// Unit tangent of a zone's offset curve at t, in the direction of travel: central difference
// inside the zone, second-order one-sided (-3 P0 + 4 P1 - P2) at its ends, so the two pieces
// meeting at a joint read the same tangent to O(h^2); the frame tangent if the offset curve is
// momentarily stationary.
function zoneTangent(context is Context, pathInfo is map, definition is map, zone is map, t is number) returns Vector
{
    var h = 1e-4;
    var dir;
    if (t - h >= zone.tLo && t + h <= zone.tHi)
    {
        dir = zonePoint(context, pathInfo, definition, zone, t + h) - zonePoint(context, pathInfo, definition, zone, t - h);
    }
    else
    {
        var sgn = (t + 2 * h <= zone.tHi) ? 1 : -1;
        var p0  = zonePoint(context, pathInfo, definition, zone, t);
        var p1  = zonePoint(context, pathInfo, definition, zone, t + sgn * h);
        var p2  = zonePoint(context, pathInfo, definition, zone, t + sgn * 2 * h);
        dir = sgn * (4 * p1 - 3 * p0 - p2);
    }
    var dLen = norm(dir);
    if (dLen / meter < 1e-12)
    {
        return sampleParallelTransportFrame(context, pathInfo.frenetPath, pathInfo.ptTable, t * pathInfo.length).frame.zAxis;
    }
    return dir / dLen;
}


// The circular arc that STARTS at pStart travelling along tStartOut (unit, outgoing tangent)
// and ENDS at pEnd. Returns { straight, center, radius, e0, yA, sweep, nTravel }: the arc is
// center + radius * (cos(a) e0 + sin(a) yA) for a in [0, sweep], travelled with increasing a.
// straight = true when the chord is parallel to the tangent (no finite arc).
function arcFromStart(pStart is Vector, tStartOut is Vector, pEnd is Vector) returns map
{
    const c    = pEnd - pStart;
    const cLen = norm(c);
    if (cLen / meter < 1e-12)
    {
        return { "straight" : true };
    }
    const sinTheta = norm(cross(tStartOut, c / cLen));
    if (sinTheta < 1e-7)
    {
        return { "straight" : true };
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
    var sweep = atan2(dot(ve, yA) / meter, dot(ve, e0) / meter) / radian;
    if (sweep <= 0)
    {
        sweep = sweep + 2 * PI;
    }
    return { "straight" : false, "center" : center, "radius" : R, "e0" : e0, "yA" : yA,
             "sweep" : sweep, "nTravel" : nTravel };
}


// Point at the mid of the arc from [arcFromStart] -- on the travelled branch, so it uniquely
// reconstructs the intended arc via a 3-point skArc. Returns { "mid", "straight" }; a
// straight arc returns the chord midpoint.
function biarcArcMid(pStart is Vector, tStartOut is Vector, pEnd is Vector) returns map
{
    const arc = arcFromStart(pStart, tStartOut, pEnd);
    if (arc.straight)
    {
        return { "mid" : pStart + 0.5 * (pEnd - pStart), "straight" : true };
    }
    const midAng = (arc.sweep / 2) * radian;
    return { "mid" : arc.center + arc.radius * (cos(midAng) * arc.e0 + sin(midAng) * arc.yA), "straight" : false };
}


// Distance from point p to an arc from [arcFromStart] (to its end points when p lies outside
// the swept angle).
function distanceToArc(arc is map, pStart is Vector, pEnd is Vector, p is Vector) returns ValueWithUnits
{
    const v      = p - arc.center;
    const normal = dot(v, arc.nTravel);
    const inPl   = v - normal * arc.nTravel;
    var ang = atan2(dot(inPl, arc.yA) / meter, dot(inPl, arc.e0) / meter) / radian;
    if (ang < 0)
    {
        ang = ang + 2 * PI;
    }
    if (ang <= arc.sweep)
    {
        const radial = norm(inPl) - arc.radius;
        return sqrt(radial * radial + normal * normal);
    }
    return min(norm(p - pStart), norm(p - pEnd));
}


// The G1 biarc from p0 (tangent t0) to p1 (tangent t1), unit tangents in the direction of
// travel, with tangent-length ratio r = a / b (Q0 = p0 + a t0, Q1 = p1 - b t1,
// |Q1 - Q0| = a + b, joint J on Q0Q1 at a : b). Every such biarc matches both end points and
// both end tangents; r only moves the joint. r = 1 is the equal-tangent-length biarc.
//   |d - b (r t0 + t1)| = b (r + 1)  =>  2 r (1 - c) b^2 + 2 D b - |d|^2 = 0,
// with d = p1 - p0, c = t0 . t1, D = d . (r t0 + t1).
// Returns { ok, joint, tJoint }.
function biarcWithRatio(p0 is Vector, t0 is Vector, p1 is Vector, t1 is Vector, r is number) returns map
{
    const fail = { "ok" : false };
    const d    = p1 - p0;
    const dd   = dot(d, d);
    if (sqrt(dd) / meter < 1e-9)
    {
        return fail;
    }
    const c  = dot(t0, t1);
    const qa = 2 * r * (1 - c);
    const D  = dot(d, r * t0 + t1);
    var b;
    if (qa < 1e-12)
    {
        if (D / meter < 1e-12)
        {
            return fail;
        }
        b = dd / (2 * D);
    }
    else
    {
        b = (-D + sqrt(D * D + qa * dd)) / qa;
    }
    if (b / meter < 1e-12)
    {
        return fail;
    }
    const a  = r * b;
    const Q0 = p0 + a * t0;
    const Q1 = p1 - b * t1;
    const jm = Q1 - Q0;
    if (norm(jm) / meter < 1e-12)
    {
        return fail;
    }
    const J = (b * Q0 + a * Q1) / (a + b);
    if (norm(J - p0) / meter < 1e-9 || norm(p1 - J) / meter < 1e-9)
    {
        return fail;
    }
    return { "ok" : true, "joint" : J, "tJoint" : normalize(jm) };
}


/** Shortest biarc leg, as a fraction of the span's chord. */
const BIARC_MIN_LEG = 0.2;

// Largest distance from the sample points to the biarc; inf when the biarc is degenerate
// (a straight leg cannot be emitted as an arc).
function biarcDeviation(p0 is Vector, t0 is Vector, p1 is Vector, bi is map, samples is array) returns ValueWithUnits
{
    if (!bi.ok)
    {
        return inf * meter;
    }
    // Each leg at least BIARC_MIN_LEG of the span: on a flat arc the closest pair otherwise
    // puts the joint a few mm from one end, a leg too short and straight to be an arc.
    const span = norm(p1 - p0);
    if (norm(bi.joint - p0) < BIARC_MIN_LEG * span || norm(p1 - bi.joint) < BIARC_MIN_LEG * span)
    {
        return inf * meter;
    }
    const arcA = arcFromStart(p0, t0, bi.joint);
    const arcB = arcFromStart(bi.joint, bi.tJoint, p1);
    if (arcA.straight || arcB.straight)
    {
        return inf * meter;
    }
    var worst = 0 * meter;
    for (var p in samples)
    {
        const dist = min(distanceToArc(arcA, p0, bi.joint, p), distanceToArc(arcB, bi.joint, p1, p));
        if (dist > worst)
        {
            worst = dist;
        }
    }
    return worst;
}


// The G1 biarc from (p0, t0) to (p1, t1) that stays closest to the true offset curve (the
// sample points): the joint is placed by minimizing the largest deviation over the
// tangent-length ratio (coarse scan in log r, then golden-section refinement). Every
// candidate keeps both end points and both end tangents exact.
// Returns { ok, joint, midA, midB, deviation }.
function computeBiarcPoints(p0 is Vector, t0 is Vector, p1 is Vector, t1 is Vector, samples is array) returns map
{
    const LOG_MIN = -3;
    const LOG_MAX = 3;
    const N_SCAN  = 13;

    var bestU   = 0;
    var bestDev = biarcDeviation(p0, t0, p1, biarcWithRatio(p0, t0, p1, t1, 1), samples);
    for (var i = 0; i < N_SCAN; i += 1)
    {
        const u   = LOG_MIN + (LOG_MAX - LOG_MIN) * i / (N_SCAN - 1);
        const dev = biarcDeviation(p0, t0, p1, biarcWithRatio(p0, t0, p1, t1, exp(u)), samples);
        if (dev < bestDev)
        {
            bestDev = dev;
            bestU   = u;
        }
    }
    if (bestDev == inf * meter)
    {
        var at1 = biarcWithRatio(p0, t0, p1, t1, 1);
        return { "ok" : false, "reason" : at1.ok ? "every joint placement leaves a straight leg" : "degenerate end points / tangents" };
    }

    // Golden-section refinement on the scan cell around the best sample.
    const cell   = (LOG_MAX - LOG_MIN) / (N_SCAN - 1);
    const golden = (sqrt(5) - 1) / 2;
    var lo = bestU - cell;
    var hi = bestU + cell;
    var x1 = hi - golden * (hi - lo);
    var x2 = lo + golden * (hi - lo);
    var f1 = biarcDeviation(p0, t0, p1, biarcWithRatio(p0, t0, p1, t1, exp(x1)), samples);
    var f2 = biarcDeviation(p0, t0, p1, biarcWithRatio(p0, t0, p1, t1, exp(x2)), samples);
    for (var k = 0; k < 16; k += 1)
    {
        if (f1 < f2)
        {
            hi = x2; x2 = x1; f2 = f1;
            x1 = hi - golden * (hi - lo);
            f1 = biarcDeviation(p0, t0, p1, biarcWithRatio(p0, t0, p1, t1, exp(x1)), samples);
        }
        else
        {
            lo = x1; x1 = x2; f1 = f2;
            x2 = lo + golden * (hi - lo);
            f2 = biarcDeviation(p0, t0, p1, biarcWithRatio(p0, t0, p1, t1, exp(x2)), samples);
        }
    }
    if (min(f1, f2) < bestDev)
    {
        bestU   = (f1 < f2) ? x1 : x2;
        bestDev = min(f1, f2);
    }

    const bi = biarcWithRatio(p0, t0, p1, t1, exp(bestU));
    return {
        "ok"        : true,
        "joint"     : bi.joint,
        "midA"      : biarcArcMid(p0, t0, bi.joint).mid,
        "midB"      : biarcArcMid(bi.joint, bi.tJoint, p1).mid,
        "deviation" : bestDev
    };
}


// True when three points define an arc: the angle at pStart between the chords to pMid and to
// pEnd is not ~0. Relative, so a short leg of a very flat arc (4 mm on R 15 m bends by
// ~7e-5) still counts.
function arcPointsBend(pStart is Vector, pMid is Vector, pEnd is Vector) returns boolean
{
    var a = pMid - pStart;
    var b = pEnd - pStart;
    var la = norm(a);
    var lb = norm(b);
    if (la / meter < 1e-12 || lb / meter < 1e-12)
    {
        return false;
    }
    return norm(cross(a, b)) / (la * lb) > 1e-7;
}


// Builds a sketch arc through 3 world points on their common plane, under arcId. The sketch
// is what makes Onshape treat the output as an arc (clicking it shows a radius); a BSpline
// with the same shape reads as a spline. Returns qCreatedBy(arcId, EntityType.BODY), or
// undefined if the points are collinear (no unique arc) so the caller can fall back.
function emitArc3Point(context is Context, arcId is Id, pStart is Vector, pMid is Vector, pEnd is Vector)
{
    var nrm = cross(pMid - pStart, pEnd - pStart);
    if (!arcPointsBend(pStart, pMid, pEnd))
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


/** Interior points on which a best-fit biarc's deviation from the true offset is measured. */
const BIARC_DEVIATION_SAMPLES = 10;


// The arc output for [tA, tB] of a zone lying on one circular source edge:
//   - constant offset -> the exact concentric / translated arc (3-point sketch arc);
//   - varying offset  -> two tangent sketch arcs matching both end points and both end
//                        tangents of the true offset curve, the joint placed where the pair
//                        stays closest to it.
// Returns { ok, bodies, edges, isPair, deviation }; ok = false builds nothing (degenerate
// geometry), and the caller fits a spline instead.
function emitSourceArc(context is Context, wireId is Id, pathInfo is map, definition is map,
    zone is map, tA is number, tB is number) returns map
{
    var none = { "ok" : false };
    var p0 = zonePoint(context, pathInfo, definition, zone, tA);
    var p1 = zonePoint(context, pathInfo, definition, zone, tB);

    if (zoneOffsetConstant(zone, tA, tB))
    {
        var pM   = zonePoint(context, pathInfo, definition, zone, (tA + tB) / 2);
        var body = emitArc3Point(context, wireId, p0, pM, p1);
        if (body == undefined)
        {
            if (definition.printCurveDetails)
            {
                println("  constant-offset arc: the three points are collinear -> spline");
            }
            return none;
        }
        return { "ok" : true, "bodies" : [body], "edges" : [qCreatedBy(wireId, EntityType.EDGE)],
                 "isPair" : false, "deviation" : 0 * meter };
    }

    var t0 = zoneTangent(context, pathInfo, definition, zone, tA);
    var t1 = zoneTangent(context, pathInfo, definition, zone, tB);
    var truePts = [];
    for (var k = 1; k < BIARC_DEVIATION_SAMPLES; k += 1)
    {
        truePts = append(truePts, zonePoint(context, pathInfo, definition, zone, tA + (tB - tA) * k / BIARC_DEVIATION_SAMPLES));
    }
    var bi = computeBiarcPoints(p0, t0, p1, t1, truePts);

    // Both legs must be real arcs (same threshold emitArc3Point uses) before anything is built,
    // so a failure leaves no orphan sketch.
    var legsOk = bi.ok && arcPointsBend(p0, bi.midA, bi.joint) && arcPointsBend(bi.joint, bi.midB, p1);
    if (!legsOk)
    {
        if (definition.printCurveDetails)
        {
            println("  varying-offset arc pair not possible (" ~ (bi.ok ? "a leg is straight" : "no biarc: " ~ toString(bi.reason)) ~ ") -> spline");
        }
        return none;
    }
    var idA = wireId + "A";
    var idB = wireId + "B";
    emitArc3Point(context, idA, p0, bi.midA, bi.joint);
    emitArc3Point(context, idB, bi.joint, bi.midB, p1);
    return { "ok" : true,
             "bodies" : [qCreatedBy(idA, EntityType.BODY), qCreatedBy(idB, EntityType.BODY)],
             "edges"  : [qCreatedBy(idA, EntityType.EDGE), qCreatedBy(idB, EntityType.EDGE)],
             "isPair" : true, "deviation" : bi.deviation };
}


// Fits and creates one spline piece with both ends pinned to the true offset tangent of its
// zone. Neighbouring pieces (the next source edge, a blend and its regions, an arc) each pin
// their own zone's tangent at the shared point, and those agree wherever the offset is meant
// to be smooth (G1 / G2 blends, G1 source edges), so the joint is tangent by construction
// rather than to fit accuracy (~0.05 deg unpinned).
function emitSplinePiece(context is Context, wireId is Id, pathInfo is map, definition is map,
    zone is map, tA is number, tB is number, pts is array, interpolateIndices is array, maxCP is number) returns Query
{
    var chord = 0 * meter;
    for (var k = 0; k < size(pts) - 1; k += 1)
    {
        chord += norm(pts[k + 1] - pts[k]);
    }
    var target = {
        "positions"       : pts,
        "startDerivative" : zoneTangent(context, pathInfo, definition, zone, tA) * chord,
        "endDerivative"   : zoneTangent(context, pathInfo, definition, zone, tB) * chord
    };
    var bspline = approximateSpline(context, {
        "degree"             : definition.approxDegree,
        "tolerance"          : definition.approxTolerance,
        "isPeriodic"         : false,
        "maxControlPoints"   : maxCP,
        "targets"            : [approximationTarget(target)],
        "interpolateIndices" : interpolateIndices
    })[0];
    opCreateBSplineCurve(context, wireId, { "bSplineCurve" : bspline });
    if (definition.printCurveDetails)
    {
        println("  degree " ~ toString(bspline.degree) ~ "  CPs " ~ toString(size(bspline.controlPoints))
            ~ "  samples " ~ toString(size(pts)) ~ "  t [" ~ toString(tA) ~ ", " ~ toString(tB) ~ "]");
    }
    return qCreatedBy(wireId, EntityType.BODY);
}


// Split points of [tStart, tEnd]: its ends plus every source-edge boundary strictly inside.
function splitAtEdgeBoundaries(edgeBoundaryTs is array, tStart is number, tEnd is number) returns array
{
    var splitTs = [tStart];
    for (var tb in edgeBoundaryTs)
    {
        if (tb > tStart + 1e-6 && tb < tEnd - 1e-6)
        {
            splitTs = append(splitTs, tb);
        }
    }
    return append(splitTs, tEnd);
}


function buildOutputWire(context is Context, id is Id, definition is map,
    pathInfo is map, sortedRegions is array)
{
    var keepArcs      = definition.arcMode == VaryingArcMode.BIARC;
    var allWireBodies = [];
    var allWireEdges  = [];
    var biarcCount    = 0;            // varying-offset source arcs emitted as arc pairs
    var biarcWorst    = 0 * meter;    // their largest deviation from the true offset

    // Collect active blend zones (SINGLE_REGION mode declares no intersections)
    var blendZones = [];
    var intersections = (definition.intersections != undefined) ? definition.intersections : [];
    for (var intr in intersections)
    {
        if (!intr.isValid || !intr.blend)
        {
            continue;
        }

        var regA = undefined;
        var regB = undefined;
        for (var reg in sortedRegions)
        {
            if (reg.regionName == intr.region1)
            {
                regA = reg;
            }
            if (reg.regionName == intr.region2)
            {
                regB = reg;
            }
        }
        if (regA == undefined || regB == undefined)
        {
            continue;
        }

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

    // Edge-boundary t-values (one per inter-edge junction in the FrenetPath)
    var edgeBoundaryTs = [];
    var edgeData = pathInfo.frenetPath.edgeData;
    for (var ei = 1; ei < size(edgeData); ei += 1)
    {
        edgeBoundaryTs = append(edgeBoundaryTs, edgeData[ei].startArcLength / pathInfo.length);
    }

    // One or more wires per region -- split at edge boundaries so each output curve spans at
    // most one source edge.
    for (var ri = 0; ri < size(sortedRegions); ri += 1)
    {
        var reg       = sortedRegions[ri];
        var zone      = regionZone(reg);
        var tSegStart = reg.tStart;
        var tSegEnd   = reg.tEnd;

        for (var bz in blendZones)
        {
            if (bz.regA.regionName == reg.regionName)
            {
                tSegEnd   = min(tSegEnd,   bz.tBlendStart);
            }
            if (bz.regB.regionName == reg.regionName)
            {
                tSegStart = max(tSegStart, bz.tBlendEnd);
            }
        }

        if (tSegEnd - tSegStart < 1e-6)
        {
            reportFeatureWarning(context, id, "Region '" ~ reg.regionName ~
                "' was fully consumed by adjacent blend zones and produced no output. " ~
                "Reduce blend distances or expand the region extent.");
            continue;
        }

        var splitTs = splitAtEdgeBoundaries(edgeBoundaryTs, tSegStart, tSegEnd);
        for (var si = 0; si < size(splitTs) - 1; si += 1)
        {
            var tA = splitTs[si];
            var tB = splitTs[si + 1];
            if (tB - tA < 1e-6)
            {
                continue;
            }

            var wireId = id + ("reg_" ~ toString(ri) ~ "_" ~ toString(si));
            var colour = (ri % 2 == 0) ? DebugColor.CYAN : DebugColor.MAGENTA;
            if (definition.printCurveDetails)
            {
                println("=== Region " ~ toString(ri) ~ " ('" ~ reg.regionName ~ "') sub " ~ toString(si) ~ " ===");
            }

            if (keepArcs && sourceEdgeIsArc(context, pathInfo.frenetPath, (tA + tB) / 2, pathInfo.length))
            {
                var arcOut = emitSourceArc(context, wireId, pathInfo, definition, zone, tA, tB);
                if (arcOut.ok)
                {
                    allWireBodies = concatenateArrays([allWireBodies, arcOut.bodies]);
                    allWireEdges  = concatenateArrays([allWireEdges,  arcOut.edges]);
                    if (arcOut.isPair)
                    {
                        biarcCount += 1;
                        biarcWorst  = max(biarcWorst, arcOut.deviation);
                    }
                    if (definition.printCurveDetails)
                    {
                        println((arcOut.isPair ? "  ARC PAIR, deviation from true offset " ~ toString(arcOut.deviation / millimeter) ~ " mm" : "  ARC")
                            ~ "  t [" ~ toString(tA) ~ ", " ~ toString(tB) ~ "]");
                    }
                    if (definition.showRegions)
                    {
                        addDebugEntities(context, qUnion(arcOut.bodies), colour);
                    }
                    continue;
                }
            }

            var seg = generateSegmentPoints(context, pathInfo, definition, reg, tA, tB);
            if (size(seg.points) < 2)
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

            var body = emitSplinePiece(context, wireId, pathInfo, definition, zone, tA, tB,
                seg.points, seg.interpolateIndices, effMaxCP);
            allWireBodies = append(allWireBodies, body);
            allWireEdges  = append(allWireEdges, qCreatedBy(wireId, EntityType.EDGE));
            if (definition.showRegions)
            {
                addDebugEntities(context, body, colour);
            }
        }
    }

    // Blends. "Convert to splines": one spline per blend, as always. "Keep as arcs": split at
    // source-edge boundaries like the regions, so a blend over a source arc is an arc too.
    for (var bzi = 0; bzi < size(blendZones); bzi += 1)
    {
        var bz   = blendZones[bzi];
        var zone = blendZone(pathInfo, definition, bz);
        if (definition.printCurveDetails)
        {
            println("=== Blend " ~ toString(bzi) ~ " ('" ~ bz.regA.regionName ~ "' -> '" ~ bz.regB.regionName ~ "') ===");
        }

        var splitTs = keepArcs ? splitAtEdgeBoundaries(edgeBoundaryTs, bz.tBlendStart, bz.tBlendEnd) : [bz.tBlendStart, bz.tBlendEnd];
        for (var si = 0; si < size(splitTs) - 1; si += 1)
        {
            var tA = splitTs[si];
            var tB = splitTs[si + 1];
            if (tB - tA < 1e-6)
            {
                continue;
            }
            var wireId = keepArcs ? id + ("blend_" ~ toString(bzi) ~ "_" ~ toString(si)) : id + ("blend_" ~ toString(bzi));

            if (keepArcs && sourceEdgeIsArc(context, pathInfo.frenetPath, (tA + tB) / 2, pathInfo.length))
            {
                var arcOut = emitSourceArc(context, wireId, pathInfo, definition, zone, tA, tB);
                if (arcOut.ok)
                {
                    allWireBodies = concatenateArrays([allWireBodies, arcOut.bodies]);
                    allWireEdges  = concatenateArrays([allWireEdges,  arcOut.edges]);
                    if (arcOut.isPair)
                    {
                        biarcCount += 1;
                        biarcWorst  = max(biarcWorst, arcOut.deviation);
                    }
                    if (definition.showBlends)
                    {
                        addDebugEntities(context, qUnion(arcOut.bodies), DebugColor.YELLOW);
                    }
                    continue;
                }
            }

            // Points in proportion to this piece's share of the blend (all of it when unsplit).
            var n   = max([definition.approxDegree + 1, floor(definition.numRegionPoints * (tB - tA) / (bz.tBlendEnd - bz.tBlendStart) + 0.5)]);
            var pts = [];
            for (var i = 0; i < n; i += 1)
            {
                pts = append(pts, zonePoint(context, pathInfo, definition, zone, tA + (tB - tA) * i / (n - 1)));
            }
            var body = emitSplinePiece(context, wireId, pathInfo, definition, zone, tA, tB,
                pts, [0, size(pts) - 1], definition.approxMaxCP);
            allWireBodies = append(allWireBodies, body);
            allWireEdges  = append(allWireEdges, qCreatedBy(wireId, EntityType.EDGE));
            if (definition.showBlends)
            {
                addDebugEntities(context, body, DebugColor.YELLOW);
            }
        }
    }

    if (biarcCount > 0)
    {
        reportFeatureInfo(context, id, toString(biarcCount) ~ " varying-offset source arc(s) built as tangent arc pairs; " ~
            "largest deviation from the true offset " ~ toString(roundToPrecision(biarcWorst / millimeter, 4)) ~ " mm.");
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
