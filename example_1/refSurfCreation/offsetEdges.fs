FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
export import(path : "onshape/std/geometriccontinuity.gen.fs", version : "3083.0");

// IMPORT: offset_edges_frames.fs (same document; path, frames, offset points)
// PLACEHOLDER: replace path with the offset_edges_frames tab's element id and version with its microversion once the tab exists.
import(path : "PLACEHOLDER_FRAMES_ELEMENT_ID", version : "PLACEHOLDER_FRAMES_MICROVERSION");
// IMPORT: offset_edges_output.fs (same document; regions, profile, output wire).
// export import: its enums are parameter types of this feature and must be reachable from this tab's exports.
// PLACEHOLDER: replace path with the offset_edges_output tab's element id and version with its microversion once the tab exists.
export import(path : "PLACEHOLDER_OUTPUT_ELEMENT_ID", version : "PLACEHOLDER_OUTPUT_MICROVERSION");
// IMPORT: offset_edges_icon.svg (feature icon)
IconNamespace::import(path : "9b66aac0eca270c9a24c93de", version : "7196161a1c8274f32f669319");



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

    // Light path (2026-09-26): only the path length and projections are needed here, so no parallel-transport
    // table -- it used to be rebuilt in full on every dialog change.
    var pathInfo = undefined;
    try silent
    {
        pathInfo = processPath(context, id + "elPath", definition, { "transportTable" : false, "curveTypes" : false });
    }
    if (pathInfo == undefined)
    {
        return definition;
    }

    // Auto-name regions, and give every region a stable hidden id (blends are keyed by it; regions saved before
    // ids existed get theirs here, on the first dialog edit -- until then they resolve by name).
    var nextId = nextRegionIdNumber(definition);
    var usedIds = {};
    var namedRegions = [];
    for (var i = 0; i < size(definition.regions); i += 1)
    {
        var reg = definition.regions[i];
        if (reg.regionName == "" || reg.regionName == undefined)
        {
            reg.regionName = "Region " ~ toString(i + 1);
        }
        if (reg.regionId == undefined || reg.regionId == "" || usedIds[reg.regionId] == true)
        {
            reg.regionId = "R" ~ toString(nextId);
            nextId += 1;
        }
        usedIds[reg.regionId] = true;
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
            "region1Id"       : regA.regionId,
            "region2Id"       : regB.regionId,
            "blend"           : true,
            "startContinuity" : GeometricContinuity.G1,
            "startDist"       : 10 * millimeter,
            "endContinuity"   : GeometricContinuity.G1,
            "endDist"         : 10 * millimeter
        };

        // Keep the settings of the existing entry for this pair: matched by region ids (survives renames), or
        // by names for an entry saved before ids existed.
        for (var existing in definition.intersections)
        {
            if (intersectionRefersTo(existing, 1, regA) && intersectionRefersTo(existing, 2, regB))
            {
                entry = mergeMaps(entry, existing);
                entry.isValid         = true;
                entry.intersectionNum = i + 1;
                entry.region1         = regA.regionName;
                entry.region2         = regB.regionName;
                entry.region1Id       = regA.regionId;
                entry.region2Id       = regB.regionId;
                break;
            }
        }
        newIntersections = append(newIntersections, entry);
    }
    definition.intersections = newIntersections;

    return definition;
}


// The next free number for a region id "R<n>": one past the largest used by any region or intersection, so an
// id is never handed out again while anything still refers to it.
function nextRegionIdNumber(definition is map) returns number
{
    var ids = [];
    for (var reg in definition.regions)
    {
        ids = append(ids, reg.regionId);
    }
    if (definition.intersections != undefined)
    {
        for (var intr in definition.intersections)
        {
            ids = append(ids, intr.region1Id);
            ids = append(ids, intr.region2Id);
        }
    }
    var next = 1;
    for (var rid in ids)
    {
        if (rid is string)
        {
            var m = match(rid, "R([0-9]+)");
            if (m.hasMatch)
            {
                next = max(next, stringToNumber(m.captures[1]) + 1);
            }
        }
    }
    return next;
}


// --- Feature ------------------------------------------------------------------

// LABEL-PROPOSAL block (review item 16; needs the user's approval of the wording). Display text only --
// parameter ids, enum values and saved instances are untouched; revert by restoring each "was:" text.
// grep LABEL-PROPOSAL finds every changed label (also the OffsetType enum in offset_edges_output.fs).
// Math: "normal" offsets move along yAxis = T x N (out of the plane of a planar path), "binormal" offsets
// along the transported normal N (in that plane) -- the old labels had the two the other way round.
// LABEL-PROPOSAL (review item 16, display text only) was: "Offsets a G1-continuous edge chain by per-region normal and binormal amounts in the Frenet frame"
annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Offset edges",
             "Feature Type Description" : "Offsets an edge chain by per-region out-of-plane and in-plane amounts in the path frame (parallel transport)",
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

        // LABEL-PROPOSAL (review item 16, display text only) was: "Flip normal" / "Inverts the Frenet normal direction"
        annotation { "Name" : "Flip out-of-plane direction", "Default" : false,
                     "Description" : "Reverses the out-of-plane offset direction (the path frame's T x N axis)" }
        definition.flipNormal is boolean;

        // LABEL-PROPOSAL (review item 16, display text only) was: "Flip binormal" / "Inverts the Frenet binormal direction"
        annotation { "Name" : "Flip in-plane direction", "Default" : false,
                     "Description" : "Reverses the in-plane offset direction (the path frame's transported normal)" }
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

            // Stable key for blends (2026-09-26): written by the editing logic ("R<n>"); "" on regions saved
            // before it existed, which then resolve by name.
            annotation { "Name" : "Region id", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
            region.regionId is string;

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

            // LABEL-PROPOSAL (review item 16, display text only) was: "Which Frenet offsets this region applies (hides the unused fields)"
            annotation { "Name" : "Offset type", "Default" : OffsetType.NORMAL,
                         "UIHint" : UIHint.HORIZONTAL_ENUM,
                         "Description" : "Which offset directions this region applies (hides the unused fields)" }
            region.offsetType is OffsetType;

            if (region.offsetType == OffsetType.NORMAL || region.offsetType == OffsetType.BOTH)
            {
                // LABEL-PROPOSAL (review item 16, display text only) was: "Start normal offset" / "Frenet normal offset at the region start"
                annotation { "Name" : "Start out-of-plane offset",
                             "Description" : "Offset along the path frame's T x N axis at the region start (for a planar path: normal to its plane)" }
                isLength(region.startNormalOffset, OffsetBounds);

                // LABEL-PROPOSAL (review item 16, display text only) was: "End normal offset" / "Frenet normal offset at the region end"
                annotation { "Name" : "End out-of-plane offset",
                             "Description" : "Offset along the path frame's T x N axis at the region end (for a planar path: normal to its plane)" }
                isLength(region.endNormalOffset, OffsetBounds);
            }

            if (region.offsetType == OffsetType.BINORMAL || region.offsetType == OffsetType.BOTH)
            {
                // LABEL-PROPOSAL (review item 16, display text only) was: "Start binormal offset" / "Frenet binormal offset at the region start"
                annotation { "Name" : "Start in-plane offset",
                             "Description" : "Offset along the path frame's transported normal at the region start (for a planar path: in its plane, positive toward the side the path first curves to)" }
                isLength(region.startBinormalOffset, OffsetBounds);

                // LABEL-PROPOSAL (review item 16, display text only) was: "End binormal offset" / "Frenet binormal offset at the region end"
                annotation { "Name" : "End in-plane offset",
                             "Description" : "Offset along the path frame's transported normal at the region end (for a planar path: in its plane, positive toward the side the path first curves to)" }
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

            // The two regions' ids (see region.regionId); "" on intersections saved before ids existed.
            annotation { "Name" : "Region 1 id", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
            intersection.region1Id is string;

            annotation { "Name" : "Region 2 id", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
            intersection.region2Id is string;

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
                             "Description" : "Which offset directions this control point pins" }   // LABEL-PROPOSAL was: "Which Frenet offsets this control point pins"
                off.pointOffsetType is OffsetType;

                if (off.pointOffsetType == OffsetType.NORMAL || off.pointOffsetType == OffsetType.BOTH)
                {
                    // LABEL-PROPOSAL (review item 16, display text only) was: "Normal offset"
                    annotation { "Name" : "Out-of-plane offset" }
                    isLength(off.normalOffset, OffsetBounds);
                }
                if (off.pointOffsetType == OffsetType.BINORMAL || off.pointOffsetType == OffsetType.BOTH)
                {
                    // LABEL-PROPOSAL (review item 16, display text only) was: "Binormal offset"
                    annotation { "Name" : "In-plane offset" }
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
            // LABEL-PROPOSAL (review item 16, display text only) was: "Draw Frenet frames along the reference path"
            annotation { "Name" : "Show reference frames",
                         "Description" : "Draw path frames along the reference path (RED = out-of-plane, GREEN = in-plane, BLUE = tangent)" }
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
                         "Description" : "Sample path-frame xAxis/yAxis at 5 points per edge; show junction dot products" }   // LABEL-PROPOSAL was: "Sample Frenet xAxis/yAxis ..."
            definition.printFrameSamples is boolean;
        }
    }
    {
        // curveTypes: Keep as arcs asks every piece whether its source edge is a circle -- cache the types.
        var pathInfo = processPath(context, id + "refPath", definition,
            { "transportTable" : true, "curveTypes" : definition.arcMode == VaryingArcMode.BIARC });

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
            var arcs   = [];
            for (var fi = 0; fi < numF; fi += 1)
            {
                arcs = append(arcs, len * fi / max([1, numF - 1]));
            }
            var frs = sampleParallelTransportFrames(context, pathInfo.frenetPath, pathInfo.ptTable, arcs, undefined);
            for (var fi = 0; fi < numF; fi += 1)
            {
                var fr  = frs[fi];
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
