FeatureScript 3008;
import(path : "onshape/std/common.fs", version : "3008.0");
import(path : "onshape/std/extend.fs", version : "3008.0");
import(path : "onshape/std/faceIntersection.fs", version : "3008.0");
import(path : "onshape/std/loft.fs", version : "3008.0");

//This feature is a simplified (arguably improved) version of create SW Rout surf
// 1. Create intersection curve/wire between bottom surface and periphery surface
// 2. When definition.showRefFrames is true, show 20 reference (frenet) frames along the intersection wire. ZAxis of frames is tangent to intersection wire. 
//      Defualt X-Axis (normal) is vertical up. xAxis direction is normal to bottom surface.
//      Default binormal (Y-Axis) is towards the center (towards y=0). ZAxis direction does not matter. X and Y axis directions do. 
// 3. Create an ordered array of 'edge maps' (lowest X to highest X). Each map should have the following keys: startPoint, endPoint, edgeQuery, pointArray. The point array is an array of maps (ordered 
//      smallest to largest) with point, frame as keys. The frame is the frenet frame at each point. We will treat the xAxis (normal) as the axis for HEIGHT offsets, and the yAxis (binormal) as the 
//      axis for WIDTH offsets. Creating this ordered array prevents us from needing to recalculate the data over and over again. 
// 4. Find (or insert) points into our table that divide our three regions (Tip, SW Rout, Tail).
// 5. For each region, do the following: if definition.joinCurves is true, approximate a single curve for each wire in each region. If defintition.joinCurves is not true, use approximation
//      definitions to approximate each curve within the region and opExtractCurves into a single wire. When this is the case, ensure tangency (when source curves are tangent).
//      if definition.useExistingCPCount is true, the maximum number of control points for a curve are the number of existing control points. The maximum number of control points for a 'joined' curve
//      (all curves within a region) is the total number of control points within that region from the source curves
//      a. Create a bottom wire. This should be either EXACTLY the intersection curve created in step 1 (within the region), or an appropriate approximation of that wire. (height, witdh offsets = 0)
//      b. Create a startWire. This is a height offset (along frenet X Axis) of definition.swRoutBottom and zero width offset.
//      c. [SW ROUT REGION ONLY]. If definition.swRoutStepIn != 0, create a stepInWire. From our source points, this wire has a vertical offset of definition.swRoutBottom and a width offset of
//      definition.swRoutStepIn
//      d. Create a top wire. Height offset is definition.swRoutHeight. Width offset is {definition.swRoutStepIn} <-- only if it exists in the region + (definition.swRoutHeight - definition.swRoutStart
//      )/tan(90 * degree - {RoutAngle} <-- [definition.tipRoutAngle || definition.swRoutAngle || definition.tailRoutAngle])
//      e. Find and save wire endpoints at region junctions (one in tip, two in SW Rout Region, one in tail)
//      f. Create a lofted surface between bottomWire, startWire, {stepInWire}, topWire
//      g. Find 'profileEdges'. These are the ends of our loft. Each edge will have two endpoints coincident with one of our wire endpoints (step e). Save these edges.
//      we do not need to find the surface edge that connects the bottomWire to the StartWire.
// 6: create a copy (opOffsetFace, offset = 0 * millimeter) of the swRout Faces, except the faces that connect the bottomWire to the startWire
// 7: at each end of the copied SWRout faces, create a revolved surface of the profileEdges (these will need to be NEW QUERIES - as it's a new body). Axis of revolution should be normal to the bottom 
//      surface. Place mate connectors at the startWire endpoint, but shifted in y by the cutter radius towards the outside of the ski. Revolve angle should be 60 degrees. This surface is now the 
//      trueSWRoutSurf. We do not need to do anything further with this surface. It should be named appropriately. 
// 8. If definition.blendRegions is true:
//      a. start tracking profileEdges in tips and tails - these will be moved
//      b. move profileEdges in tip and tail region by definition.blendOffset AWAY from the SW Rout region
//      c. create a lofted surface that is TANGENT to the SW Rout surface that connects either StarWire to TopWire or StepInWire to Top Wire (the angled surface) to the tip and tail profileSurfaces.
//      This loft should also be TANGENT to the tip/tail face.
//      d. If there is a SW Rout Step In, we will now have empty 'slivers' at each end. Create a filled surface that connects 1. SW Rout Stepin edge. 2. Bottom edge of new lofted surface. 3. edge that
//      follows the startWire in the tip/Tail Regions
// 9. Knit all surfaces (except the trueSWRoutSurf) together
// 10. Delete wires

export const SWRoutAngleBounds = {(degree)     : [0,   20,  45]} as AngleBoundSpec;
export const DistAboveBottomBounds = {(millimeter) : [1,    4,  10]} as LengthBoundSpec;
export const SWRoutHeightBounds = {(millimeter) : [5,   30, 100]} as LengthBoundSpec;
export const SWStepInBounds = {(millimeter) : [0,    0,   3]} as LengthBoundSpec;
export const CPMultiplierBounds = {(unitless) : [2, 3, 5]} as IntegerBoundSpec;
export const PointsPerEdgeBounds = {(unitless) : [10, 20, 50]} as IntegerBoundSpec;
export const CutterRadiusBounds = {(millimeter) : [5, 10, 20]} as LengthBoundSpec;

// ---- Implementation constants (centralized; tune here) --------------------
// Maximum number of frames DRAWN by showRefFrames (an evenly-spaced subset of
// the cache), so the visualization stays readable no matter how dense the cache
// is.  The cache itself is sampled per-edge from the sampling parameters -- see
// buildEdgeMapCache.
const TARGET_FRAME_COUNT = 20;
// Debug arrow length for the drawn reference frames.
const FRAME_ARROW_LEN = 10 * millimeter;
// Minimum length treated as non-degenerate when normalizing sampled tangents.
const TANGENT_EPS = 1e-9;
// Below this |y| a sampled point is treated as sitting ON the centerline (the
// nose/tail apex, where there is no lateral direction); the binormal then points
// toward the geometry center instead of toward the y=0 plane.  Tune here.
const CENTERLINE_EPS = 1 * millimeter;
// Consecutive cached points closer than this are treated as the same station
// (the duplicate point shared between adjacent edges) when flattening the cache.
const STATION_DEDUP_EPS = 1e-5 * meter;

export enum SamplingType
{
    annotation {"Name" : "Points per edge"}
    NUM_POINTS,
    annotation {"Name" : "Control Points"}
    CTRL_POINTS
}


annotation { "Feature Type Name" : "Create SW Rout Surface", "Feature Type Description" : "Creates a Sidewall Rout Surface based on user input" }
export const myFeature = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Bottom surface",
                     "Filter" : EntityType.BODY && BodyType.SHEET, "MaxNumberOfPicks" : 1,
                     "Description" : "Bottom of ski. Must extend beyond side surface." }
        definition.bottomSheet is Query;

        annotation { "Name" : "Periphery surface",
                     "Filter" : EntityType.BODY && BodyType.SHEET, "MaxNumberOfPicks" : 1,
                     "Description" : "Side surface of ski." }
        definition.sideSheet is Query;
        
        annotation { "Name" : "SW rout height", "Description" : "Height above bottom surface SW Rout Surface ends" }
        isLength(definition.swRoutHeight, SWRoutHeightBounds);
        
        annotation { "Name" : "FCP", "Filter" : BodyType.MATE_CONNECTOR || EntityType.VERTEX, "MaxNumberOfPicks" : 1, "UIHint" : UIHint.PREVENT_CREATING_NEW_MATE_CONNECTORS }
        definition.fcp is Query;
        
        annotation { "Name" : "ACP", "Filter" : BodyType.MATE_CONNECTOR || EntityType.VERTEX, "MaxNumberOfPicks" : 1, "UIHint" : UIHint.PREVENT_CREATING_NEW_MATE_CONNECTORS }
        definition.acp is Query;
        
        annotation { "Name" : "Forebody rout start", "Filter" : BodyType.MATE_CONNECTOR || EntityType.VERTEX, "MaxNumberOfPicks" : 1, "UIHint" : UIHint.PREVENT_CREATING_NEW_MATE_CONNECTORS }
        definition.fbRoutStart is Query;
        
        annotation { "Name" : "Aftbody rout end", "Filter" : BodyType.MATE_CONNECTOR || EntityType.VERTEX, "MaxNumberOfPicks" : 1, "UIHint" : UIHint.PREVENT_CREATING_NEW_MATE_CONNECTORS }
        definition.abRoutEnd is Query;
        
        annotation { "Name" : "SW rout begins above Bottom" }
        isLength(definition.swRoutBottom, DistAboveBottomBounds);
        
        annotation { "Name" : "SW Rout Angle" }
        isAngle(definition.swRoutAngle, SWRoutAngleBounds);
        
        annotation { "Name" : "SW Rout Step-in" }
        isLength(definition.swRoutStepIn, SWStepInBounds);
        
        annotation { "Name" : "Tip Rout Angle" }
        isAngle(definition.tipRoutAngle, SWRoutAngleBounds);
        
        annotation { "Name" : "Tail Rout Angle" }
        isAngle(definition.tailRoutAngle, SWRoutAngleBounds);
        
        annotation { "Name" : "Blend regions", "Default" : true }
        definition.blendRegions is boolean;
        
        if (definition.blendRegions)
        {
            annotation { "Name" : "Blend offset" }
            isLength(definition.blendOffset, LENGTH_BOUNDS);
        }
        
        annotation { "Name" : "Cutter radius" }
        isLength(definition.cutterRadius, CutterRadiusBounds);
        
        
        
        annotation { "Group Name" : "Sampling and approximation", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Sampling Type", "Default" : SamplingType.CTRL_POINTS, "UIHint" : UIHint.HORIZONTAL_ENUM }
            definition.edgeSamplingDef is SamplingType;
            
            if (definition.edgeSamplingDef == SamplingType.CTRL_POINTS)
            {
                annotation { "Name" : "Control Point multiplier" }
                isInteger(definition.ctrlPointMultiplier, CPMultiplierBounds);
            }
            else if (definition.edgeSamplingDef == SamplingType.NUM_POINTS)
            {
                annotation { "Name" : "Points per edge" }
                isInteger(definition.pointsPerEdge, PointsPerEdgeBounds);
            }
            
            annotation { "Name" : "Join curves", "Default" : true }
            definition.joinCurves is boolean;
            
            annotation { "Name" : "Use existing CP count", "Default" : true }
            definition.useExistingCPCount is boolean;
            
            
            curveApproximationPredicate(definition);
            
            
            
        }
        
        
        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Show ref frames", "Default" : true }
            definition.showRefFrames is boolean;

            annotation { "Name" : "Print ref frames", "Default" : false }
            definition.printRefFrames is boolean;

            annotation { "Name" : "Show regions", "Default" : false }
            definition.showRegions is boolean;

            annotation { "Name" : "Print debug", "Default" : false }
            definition.printDebug is boolean;
            
            
        }
        
        
    }
    {
        // ====================================================================
        // M1: intersection wire + Frenet edge-map cache (design steps 1-3)
        // ====================================================================
        const bottomFaces    = evaluateQuery(context, qOwnedByBody(definition.bottomSheet, EntityType.FACE));
        const botBox         = evBox3d(context, { "topology" : definition.bottomSheet, "tight" : true });
        const bottomCentroid = (botBox.minCorner + botBox.maxCorner) / 2;

        // Step 1: intersection wire between the bottom and periphery surfaces.
        const intId = id + "intersectionWire";
        intersectionCurve(context, intId, {
                "group1" : definition.bottomSheet,
                "group2" : definition.sideSheet
        });
        if (isQueryEmpty(context, qCreatedBy(intId, EntityType.BODY)))
        {
            throw regenError("SW Rout: the bottom and periphery surfaces do not intersect.",
                    ["bottomSheet", "sideSheet"]);
        }

        // Steps 2-3: ordered edge-map cache (lowest X to highest X), each edge
        // carrying an array of { point, frame } samples.  Frame convention:
        //   xAxis (normal)   = up, normal to the bottom surface  -> HEIGHT offsets
        //   yAxis (binormal) = inward, toward the ski centerline  -> WIDTH offsets
        //   zAxis (tangent)  = along the intersection wire (sign irrelevant)
        const edgeMaps = buildEdgeMapCache(context, qCreatedBy(intId, EntityType.BODY),
                bottomFaces, bottomCentroid, definition);

        if (definition.showRefFrames)
        {
            drawRefFrames(context, edgeMaps);
        }

        if (definition.printRefFrames)
        {
            printRefFramesTable(context, edgeMaps);
        }

        if (definition.printDebug)
        {
            var ptCount = 0;
            var perEdge = "";
            for (var em in edgeMaps)
            {
                ptCount += size(em.pointArray);
                perEdge = perEdge ~ toString(size(em.pointArray)) ~ " ";
            }
            println("[SW Rout] edge-map cache: " ~ toString(size(edgeMaps)) ~ " edges, " ~
                    toString(ptCount) ~ " sampled frames (per-edge: " ~ perEdge ~ ").");
        }

        // ====================================================================
        // M2: region division -- Tip / SW Rout / Tail (design step 4)
        // ====================================================================
        // Internal boundaries are fbRoutStart and abRoutEnd; FCP/ACP only mark
        // which physical wire end is the forebody/tip side vs aftbody/tail side.
        const regions = divideRegions(context, edgeMaps, definition, bottomFaces, bottomCentroid);

        // Unconditional validation: every region must have enough points to build
        // a wire (and this keeps region assignment honest before M3 consumes it).
        for (var rg in regions)
        {
            if (size(rg.stations) < 2)
            {
                throw regenError("SW Rout: region '" ~ rg.name ~ "' has too few points -- " ~
                        "check the FCP/ACP and rout start/end picks.");
            }
        }

        if (definition.printDebug)
        {
            printRegionSummary(regions);
        }

        // ====================================================================
        // M3: per-region offset wires (design step 5) -- LOFTS DISABLED for now
        // ====================================================================
        // Per region, build bottom / start / [step-in] / top wires by offsetting
        // the cached station points along each frame's HEIGHT (xAxis) and WIDTH
        // (yAxis) axes.  Lofting is temporarily off so the wires can be inspected.
        var regionBodies = [];
        for (var ri = 0; ri < size(regions); ri += 1)
        {
            const wires = buildRegionWires(context, id, ri, regions[ri], definition);
            for (var wb in wires)
            {
                regionBodies = append(regionBodies, { "name" : regions[ri].name, "body" : wb });
            }
        }

        if (definition.showRegions)
        {
            colorRegionSurfaces(context, regionBodies);
        }

        // ---- M4+ (offset-face copy, knit, cleanup) to follow ----
    });


// ===========================================================================
// M1 helpers: edge-map cache + frames
// ===========================================================================

// The WIDTH offset axis (binormal, inward) for a stored frame.  The frame is an
// orthonormal CoordSystem built with xAxis = up-normal and zAxis = tangent, so
// cross(zAxis, xAxis) is the inward binormal (see swRoutFrameAt).
function frameWidthAxis(f is CoordSystem) returns Vector
{
    return cross(f.zAxis, f.xAxis);
}

// Bottom-surface normal at pt, oriented "up" (+Z side).  Projects onto the
// CLOSEST of all the bottom-sheet faces (not just face 0), so a multi-face bottom
// -- e.g. a flat center face plus curved rocker faces at tip/tail -- reports the
// true local normal instead of the flat center's vertical normal everywhere.
function bottomNormalAt(context is Context, pt is Vector, bottomFaces is array) returns Vector
{
    var bestFace = bottomFaces[0];
    var bestUv   = undefined;
    var bestDist = undefined;
    for (var f in bottomFaces)
    {
        const d = evDistance(context, { "side0" : f, "side1" : pt });
        if (bestDist == undefined || d.distance < bestDist)
        {
            bestDist = d.distance;
            bestFace = f;
            bestUv   = d.sides[0].parameter;
        }
    }
    const nrm = evFaceTangentPlane(context, { "face" : bestFace, "parameter" : bestUv }).normal;
    return (nrm[2] >= 0) ? nrm : -1 * nrm;
}

// Builds the orthonormal Frenet-style frame at one sampled point:
//   xAxis = bottom-surface normal, oriented "up" (+Z side)   -> HEIGHT axis
//   yAxis = binormal, toward the ski centerline (y = 0)       -> WIDTH axis
//   zAxis = intersection-wire tangent (sign chosen so yAxis points inward)
// The binormal is cross(tangent, normal) (always lateral, in the bottom's
// tangent plane); its SIGN is chosen so it points toward the X-axis (the y = 0
// symmetry plane).  For points sitting essentially ON the centerline (the
// nose/tail apex, |y| < CENTERLINE_EPS) there is no lateral direction, so it
// falls back to pointing toward the geometry center.
// NOTE: this convention assumes the standard ski orientation -- length along X,
// symmetry plane at y = 0.
function swRoutFrameAt(context is Context, pt is Vector, tangentDir is Vector,
        bottomFaces is array, geometryCenter is Vector) returns CoordSystem
{
    // Up-normal from the bottom surface at the projected point.
    const upNormal = bottomNormalAt(context, pt, bottomFaces);

    // Tangent, orthogonalized against the normal (the intersection tangent lies
    // in the bottom surface, so this is a tiny correction).
    var tang = tangentDir - upNormal * dot(tangentDir, upNormal);
    if (norm(tang) < TANGENT_EPS)
    {
        tang = perpendicularVector(upNormal);
    }
    tang = normalize(tang);

    // Reference direction the binormal should point toward: the y = 0 plane (the
    // X-axis).  Purely lateral -- no longitudinal component to contaminate the
    // sign at the tip/tail transitions.
    var ref = vector(0 * meter, -pt[1], 0 * meter);
    if (norm(ref) < CENTERLINE_EPS)
    {
        // On the centerline (nose/tail apex): aim at the geometry center instead.
        ref = geometryCenter - pt;
    }
    ref = ref - vector(0, 0, 1) * ref[2];   // horizontal component only

    // Flip the tangent (hence the binormal) so cross(tang, upNormal) points to ref.
    if (norm(ref) > TOLERANCE.zeroLength * meter && dot(cross(tang, upNormal), ref) < 0)
    {
        tang = -1 * tang;
    }

    return coordSystem(pt, upNormal, tang);
}

// Builds the ordered edge-map cache from the intersection wire body/bodies.
// Edges are ordered along the continuous path (constructPath) and then flipped
// as a whole so the table runs lowest X to highest X (design convention).
function buildEdgeMapCache(context is Context, wireQuery is Query,
        bottomFaces is array, bottomCentroid is Vector, definition is map) returns array
{
    const allEdges = evaluateQuery(context, qOwnedByBody(wireQuery, EntityType.EDGE));
    if (size(allEdges) == 0)
    {
        throw regenError("SW Rout: the intersection produced no edges.");
    }

    // Continuous tip-to-tail ordering, independent of X-monotonicity at the tip.
    const pl = constructPath(context, qUnion(allEdges));

    var edgeMaps = [];
    for (var i = 0; i < size(pl.edges); i += 1)
    {
        const e       = pl.edges[i];
        const flipped = pl.flipped[i];

        // Samples per edge, driven by the sampling parameters:
        //   CTRL_POINTS : (edge control-point count) * ctrlPointMultiplier
        //   NUM_POINTS  : pointsPerEdge
        // Floored at 4 so every edge fits and approximateSpline has enough points.
        var nPts;
        if (definition.edgeSamplingDef == SamplingType.CTRL_POINTS)
        {
            const bs = evApproximateBSplineCurve(context, { "edge" : e });
            nPts = size(bs.controlPoints) * definition.ctrlPointMultiplier;
        }
        else
        {
            nPts = definition.pointsPerEdge;
        }
        nPts = max([4, nPts]);

        var params = [];
        for (var k = 0; k < nPts; k += 1)
        {
            params = append(params, k / (nPts - 1));
        }
        const useParams = flipped ? reverse(params) : params;

        const lines = evEdgeTangentLines(context, { "edge" : e, "parameters" : useParams });
        var pointArray = [];
        for (var j = 0; j < size(lines); j += 1)
        {
            const pt = lines[j].origin;
            var tang = lines[j].direction;
            if (flipped) { tang = -1 * tang; }
            const frame = swRoutFrameAt(context, pt, tang, bottomFaces, bottomCentroid);
            pointArray = append(pointArray, { "point" : pt, "frame" : frame });
        }

        edgeMaps = append(edgeMaps, {
                "startPoint" : pointArray[0].point,
                "endPoint"   : pointArray[size(pointArray) - 1].point,
                "edgeQuery"  : e,
                "pointArray" : pointArray
        });
    }

    // Orient the whole table lowest X to highest X.
    const firstX = edgeMaps[0].startPoint[0];
    const lastX  = edgeMaps[size(edgeMaps) - 1].endPoint[0];
    if (firstX > lastX)
    {
        edgeMaps = reverseEdgeMaps(edgeMaps);
    }
    return edgeMaps;
}

// Reverses an edge-map table end-to-end: reverses the edge order and, within
// each edge, reverses the point array and swaps start/end points.
function reverseEdgeMaps(edgeMaps is array) returns array
{
    var out = [];
    for (var i = size(edgeMaps) - 1; i >= 0; i -= 1)
    {
        const em  = edgeMaps[i];
        const rev = reverse(em.pointArray);
        out = append(out, {
                "startPoint" : rev[0].point,
                "endPoint"   : rev[size(rev) - 1].point,
                "edgeQuery"  : em.edgeQuery,
                "pointArray" : rev
        });
    }
    return out;
}

// Draws TARGET_FRAME_COUNT evenly-spaced frames along the cache:
//   red   = xAxis (normal, HEIGHT axis)
//   green = yAxis (binormal, WIDTH axis)
//   blue  = zAxis (tangent)
function drawRefFrames(context is Context, edgeMaps is array)
{
    var frames = [];
    for (var em in edgeMaps)
    {
        for (var pm in em.pointArray)
        {
            frames = append(frames, pm.frame);
        }
    }
    const n = size(frames);
    if (n == 0) { return; }

    const rad   = FRAME_ARROW_LEN * 0.06;
    const count = min([TARGET_FRAME_COUNT, n]);
    for (var i = 0; i < count; i += 1)
    {
        const idx = (count == 1) ? 0 : floor(i * (n - 1) / (count - 1) + 0.5);
        const f   = frames[idx];
        const o   = f.origin;
        addDebugArrow(context, o, o + FRAME_ARROW_LEN * f.xAxis,          rad,           DebugColor.RED);
        addDebugArrow(context, o, o + FRAME_ARROW_LEN * frameWidthAxis(f), rad * (2 / 3), DebugColor.GREEN);
        addDebugArrow(context, o, o + FRAME_ARROW_LEN * f.zAxis,          rad * 0.5,     DebugColor.BLUE);
    }
}

// Rounds a unitless number to 3 decimals for compact printing.
function fmtScalar(x) returns string
{
    return toString(round(x * 1000) / 1000);
}

// Formats a unitless (direction) vector as [x, y, z].
function fmtAxis(v is Vector) returns string
{
    return "[" ~ fmtScalar(v[0]) ~ ", " ~ fmtScalar(v[1]) ~ ", " ~ fmtScalar(v[2]) ~ "]";
}

// Formats a position vector as (x, y, z) in millimeters.
function fmtOriginMM(v is Vector) returns string
{
    return "(" ~ fmtScalar(v[0] / millimeter) ~ ", " ~ fmtScalar(v[1] / millimeter) ~
           ", " ~ fmtScalar(v[2] / millimeter) ~ ")";
}

// Prints one condensed line per cached frame:
//   #i  O(x, y, z)mm  X[..]  Y[..]  Z[..]
// (origin in mm; X/Y/Z are the unit normal/binormal/tangent axes.)
function printRefFramesTable(context is Context, edgeMaps is array)
{
    println("[SW Rout] Reference frames -- O = origin(mm), X = normal/height, " ~
            "Y = binormal/width, Z = tangent:");
    var idx = 0;
    for (var em in edgeMaps)
    {
        for (var pm in em.pointArray)
        {
            const f = pm.frame;
            println("  #" ~ toString(idx) ~
                    "  O" ~ fmtOriginMM(f.origin) ~
                    "  X" ~ fmtAxis(f.xAxis) ~
                    "  Y" ~ fmtAxis(frameWidthAxis(f)) ~
                    "  Z" ~ fmtAxis(f.zAxis));
            idx += 1;
        }
    }
}


// ===========================================================================
// M2 helpers: region division (Tip / SW Rout / Tail)
// ===========================================================================

// Flattens the per-edge cache into a single ordered station list, dropping the
// duplicate points shared between consecutive edges (edge i's endpoint == edge
// i+1's start point), which would otherwise create zero-length wire segments.
function flattenStations(edgeMaps is array) returns array
{
    var out = [];
    for (var em in edgeMaps)
    {
        for (var pm in em.pointArray)
        {
            if (size(out) == 0 ||
                norm(pm.point - out[size(out) - 1].point) > STATION_DEDUP_EPS)
            {
                out = append(out, pm);
            }
        }
    }
    return out;
}

// Resolves a picked query (mate connector or vertex) to a single 3D point via
// its tight bounding-box center.  Both a mate connector and a vertex are point
// entities, so their tight box is degenerate at the point -- one code path for
// either pick type.
function queryPoint(context is Context, q is Query, label is string) returns Vector
{
    if (isQueryEmpty(context, q))
    {
        throw regenError("SW Rout: '" ~ label ~ "' is not selected.");
    }
    const bb = evBox3d(context, { "topology" : q, "tight" : true });
    return (bb.minCorner + bb.maxCorner) / 2;
}

// Projects a 3D point onto the intersection wire and returns a station
// { point, frame } exactly at that projection (closest edge + its parameter),
// so a region boundary lands on the real wire rather than snapping to a cached
// sample.
function stationAtProjection(context is Context, pt is Vector, edgeMaps is array,
        bottomFaces is array, geometryCenter is Vector) returns map
{
    var bestDist  = undefined;
    var bestEdge  = edgeMaps[0].edgeQuery;
    var bestParam = 0;
    for (var em in edgeMaps)
    {
        const d = evDistance(context, { "side0" : em.edgeQuery, "side1" : pt });
        if (bestDist == undefined || d.distance < bestDist)
        {
            bestDist  = d.distance;
            bestEdge  = em.edgeQuery;
            bestParam = d.sides[0].parameter;
        }
    }
    const ln = evEdgeTangentLine(context, { "edge" : bestEdge, "parameter" : bestParam });
    return { "point" : ln.origin, "frame" : swRoutFrameAt(context, ln.origin, ln.direction, bottomFaces, geometryCenter) };
}

// Parameter t in [0, 1] of the closest point on segment a->b to point B.
function closestSegmentParam(pB is Vector, a is Vector, b is Vector) returns number
{
    const ab   = b - a;
    const len2 = dot(ab, ab);
    if (len2 < 1e-12 * meter * meter)
    {
        return 0;
    }
    return clamp(dot(pB - a, ab) / len2, 0, 1);
}

// Index at which to insert a boundary point into the ordered station list: the
// index of the station just AFTER the boundary (i.e. the far endpoint of the
// segment the boundary projects onto).
function segmentInsertionIndex(stations is array, pB is Vector) returns number
{
    var bestDist = undefined;
    var bestIdx  = 1;
    for (var i = 0; i < size(stations) - 1; i += 1)
    {
        const a  = stations[i].point;
        const b  = stations[i + 1].point;
        const t  = closestSegmentParam(pB, a, b);
        const cp = a + t * (b - a);
        const d  = norm(pB - cp);
        if (bestDist == undefined || d < bestDist)
        {
            bestDist = d;
            bestIdx  = i + 1;
        }
    }
    return bestIdx;
}

// Inclusive slice arr[a..b].
function sliceInclusive(arr is array, a is number, b is number) returns array
{
    var out = [];
    for (var i = a; i <= b; i += 1)
    {
        out = append(out, arr[i]);
    }
    return out;
}

// Divides the ordered station list into the three regions.  Boundaries are the
// projections of fbRoutStart and abRoutEnd; the tip (forebody) end is whichever
// physical wire end is nearer FCP.  Returns [Tip, SW Rout, Tail], each a map:
//   { name, angle, hasStepIn, stations }
function divideRegions(context is Context, edgeMaps is array, definition is map,
        bottomFaces is array, geometryCenter is Vector) returns array
{
    var stations = flattenStations(edgeMaps);
    if (size(stations) < 3)
    {
        throw regenError("SW Rout: not enough sampled points along the wire to form regions.");
    }

    // Resolve picks to points.
    const fcpPt = queryPoint(context, definition.fcp, "FCP");
    const fbPt  = queryPoint(context, definition.fbRoutStart, "Forebody rout start");
    const abPt  = queryPoint(context, definition.abRoutEnd, "Aftbody rout end");
    const acpPt = queryPoint(context, definition.acp, "ACP");

    // Exact boundary stations on the wire.
    const bFb = stationAtProjection(context, fbPt, edgeMaps, bottomFaces, geometryCenter);
    const bAb = stationAtProjection(context, abPt, edgeMaps, bottomFaces, geometryCenter);

    // Insert both boundary stations into the ordered list.
    var ins = [{ "idx" : segmentInsertionIndex(stations, bFb.point), "key" : "fb", "st" : bFb },
               { "idx" : segmentInsertionIndex(stations, bAb.point), "key" : "ab", "st" : bAb }];
    if (ins[0].idx > ins[1].idx)
    {
        ins = [ins[1], ins[0]];
    }

    var aug = [];
    var pos = {};
    var nextIns = 0;
    for (var i = 0; i <= size(stations); i += 1)
    {
        while (nextIns < size(ins) && ins[nextIns].idx == i)
        {
            aug = append(aug, ins[nextIns].st);
            pos[ins[nextIns].key] = size(aug) - 1;
            nextIns += 1;
        }
        if (i < size(stations))
        {
            aug = append(aug, stations[i]);
        }
    }
    const idxFb   = pos["fb"];
    const idxAb   = pos["ab"];
    const lastIdx = size(aug) - 1;

    // Which physical end is the tip (forebody)?  The one nearer FCP.
    const tipEndIsLow = norm(aug[0].point - fcpPt) < norm(aug[lastIdx].point - fcpPt);

    // ACP sanity: the tail end should be nearer ACP; warn (non-fatal) if not.
    if (definition.printDebug)
    {
        const tailEndPt = tipEndIsLow ? aug[lastIdx].point : aug[0].point;
        const tipEndPt  = tipEndIsLow ? aug[0].point : aug[lastIdx].point;
        if (norm(tailEndPt - acpPt) > norm(tipEndPt - acpPt))
        {
            println("[SW Rout] WARNING: ACP is nearer the tip end than the tail end -- " ~
                    "FCP/ACP may be swapped.");
        }
        println("[SW Rout] tip end is the " ~ (tipEndIsLow ? "low-X" : "high-X") ~ " end; " ~
                "boundary indices fb=" ~ toString(idxFb) ~ " ab=" ~ toString(idxAb) ~
                " of " ~ toString(lastIdx) ~ ".");
    }

    var tipStations; var swStations; var tailStations;
    if (tipEndIsLow)
    {
        // Along index 0->last: tipEnd(0) .. fbRoutStart .. abRoutEnd .. tailEnd(last)
        if (!(idxFb < idxAb))
        {
            throw regenError("SW Rout: fbRoutStart/abRoutEnd resolve out of order along " ~
                    "the wire -- check that they are on the correct forebody/aftbody sides.");
        }
        tipStations  = sliceInclusive(aug, 0, idxFb);
        swStations   = sliceInclusive(aug, idxFb, idxAb);
        tailStations = sliceInclusive(aug, idxAb, lastIdx);
    }
    else
    {
        // Along index 0->last: tailEnd(0) .. abRoutEnd .. fbRoutStart .. tipEnd(last)
        if (!(idxAb < idxFb))
        {
            throw regenError("SW Rout: fbRoutStart/abRoutEnd resolve out of order along " ~
                    "the wire -- check that they are on the correct forebody/aftbody sides.");
        }
        tailStations = sliceInclusive(aug, 0, idxAb);
        swStations   = sliceInclusive(aug, idxAb, idxFb);
        tipStations  = sliceInclusive(aug, idxFb, lastIdx);
    }

    return [
        { "name" : "Tip",     "angle" : definition.tipRoutAngle,  "hasStepIn" : false,
          "stations" : tipStations },
        { "name" : "SW Rout", "angle" : definition.swRoutAngle,   "hasStepIn" : (definition.swRoutStepIn > 0 * millimeter),
          "stations" : swStations },
        { "name" : "Tail",    "angle" : definition.tailRoutAngle, "hasStepIn" : false,
          "stations" : tailStations }
    ];
}

// Prints a one-line-per-region summary.
function printRegionSummary(regions is array)
{
    println("[SW Rout] Regions:");
    for (var rg in regions)
    {
        println("  " ~ rg.name ~ ": " ~ toString(size(rg.stations)) ~ " pts, angle=" ~
                toString(rg.angle / degree) ~ " deg, stepIn=" ~ toString(rg.hasStepIn));
    }
}

// ===========================================================================
// M3 helpers: per-region offset wires + loft
// ===========================================================================

// Offsets each station point by (height along the frame normal/xAxis, width
// along the frame binormal/yAxis).  Returns the ordered array of 3D points.
function offsetPoints(stations is array, height is ValueWithUnits, width is ValueWithUnits) returns array
{
    var pts = [];
    for (var st in stations)
    {
        const f = st.frame;
        pts = append(pts, st.point + height * f.xAxis + width * frameWidthAxis(f));
    }
    return pts;
}

// Fits a smooth BSpline wire through the given ordered points via approximateSpline
// (controlled control-point count -- an interpolating spline would chase sampling
// noise).  maxControlPoints is floored at 4, approximateSpline's minimum.
function makeWireFromPoints(context is Context, wId is Id, pts is array,
        label is string, printDebug is boolean) returns Query
{
    const deg   = min([3, size(pts) - 1]);
    const maxCP = max([4, size(pts)]);
    if (printDebug)
    {
        println("[SW Rout]   wire " ~ label ~ ": " ~ toString(size(pts)) ~ " input pts, degree " ~
                toString(deg) ~ ", maxControlPoints " ~ toString(maxCP));
    }
    const curve = approximateSpline(context, {
            "targets"          : [approximationTarget({ "positions" : pts })],
            "degree"           : deg,
            "tolerance"        : 1e-5 * meter,
            "isPeriodic"       : false,
            "maxControlPoints" : maxCP
    })[0];
    opCreateBSplineCurve(context, wId, { "bSplineCurve" : curve });
    return qCreatedBy(wId, EntityType.EDGE);
}

// Builds one region's cross-section wires from the cached stations.
// Wire heights/widths (per design step 5):
//   bottom : height 0,            width 0
//   start  : height swRoutBottom, width 0
//   stepIn : height swRoutBottom, width swRoutStepIn   (SW Rout region only)
//   top    : height swRoutHeight, width {stepIn} + (swRoutHeight - swRoutBottom)*tan(angle)
// Returns the wire bodies (kept in context for inspection).  Lofting is disabled
// for now -- to re-enable, fit each wire's edge (qCreatedBy(wId, EDGE)), loft
// CONSECUTIVE PAIRS into ruled surface patches (a single multi-section loft
// through the hard corner at start/step returns LOFT_INVALID), delete the scratch
// wires, and return the surface segments instead.
function buildRegionWires(context is Context, id is Id, ri is number,
        rg is map, definition is map) returns array
{
    const hStart = definition.swRoutBottom;
    const hTop   = definition.swRoutHeight;
    const stepIn = rg.hasStepIn ? definition.swRoutStepIn : 0 * millimeter;
    const wTop   = stepIn + (hTop - hStart) * tan(rg.angle);
    const suffix = toString(ri);

    // Ordered wire cross-section stations: (height, width, tag).
    var specs = [{ "h" : 0 * millimeter, "w" : 0 * millimeter, "tag" : "bottom" },
                 { "h" : hStart,         "w" : 0 * millimeter, "tag" : "start" }];
    if (rg.hasStepIn)
    {
        specs = append(specs, { "h" : hStart, "w" : stepIn, "tag" : "step" });
    }
    specs = append(specs, { "h" : hTop, "w" : wTop, "tag" : "top" });

    var wireBodies = [];
    for (var si = 0; si < size(specs); si += 1)
    {
        const wId = id + ("swW" ~ suffix ~ "_" ~ toString(si));
        makeWireFromPoints(context, wId, offsetPoints(rg.stations, specs[si].h, specs[si].w),
                rg.name ~ " " ~ specs[si].tag, definition.printDebug);
        const body = qCreatedBy(wId, EntityType.BODY);
        setBodyName(context, body, "SW Rout wire [" ~ rg.name ~ "] " ~ specs[si].tag);
        wireBodies = append(wireBodies, body);
    }

    return wireBodies;
}

// Sets a body's display name.
function setBodyName(context is Context, body is Query, name is string)
{
    setProperty(context, { "entities" : body, "propertyType" : PropertyType.NAME, "value" : name });
}

// Region debug color: Tip = red, SW Rout = green, Tail = blue.
function regionColor(name is string) returns Color
{
    if (name == "Tip")     { return color(0.85, 0.15, 0.15); }
    if (name == "SW Rout") { return color(0.15, 0.70, 0.20); }
    return color(0.15, 0.30, 0.85);   // Tail
}

// Applies the region debug colors to the lofted surfaces.
function colorRegionSurfaces(context is Context, regionSurfaces is array)
{
    for (var rs in regionSurfaces)
    {
        setProperty(context, {
                "entities"     : rs.body,
                "propertyType" : PropertyType.APPEARANCE,
                "value"        : regionColor(rs.name)
        });
    }
}
