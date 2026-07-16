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
// Cache density: points sampled per intersection edge for the frame table.
const FRAME_SAMPLES_PER_EDGE = 40;
// Number of Frenet frames drawn when showRefFrames is on.
const REF_FRAME_COUNT = 20;
// Debug arrow length for the drawn reference frames.
const FRAME_ARROW_LEN = 10 * millimeter;
// Minimum length treated as non-degenerate when normalizing sampled tangents.
const TANGENT_EPS = 1e-9;

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
        
        annotation { "Name" : "FCP", "Filter" : (EntityType.BODY && BodyType.MATE_CONNECTOR) || EntityType.VERTEX, "MaxNumberOfPicks" : 1 }
        definition.fcp is Query;
        
        annotation { "Name" : "ACP", "Filter" : (EntityType.BODY && BodyType.MATE_CONNECTOR) || EntityType.VERTEX, "MaxNumberOfPicks" : 1 }
        definition.acp is Query;
        
        annotation { "Name" : "Forebody rout start", "Filter" : (EntityType.BODY && BodyType.MATE_CONNECTOR) || EntityType.VERTEX, "MaxNumberOfPicks" : 1 }
        definition.fbRoutStart is Query;
        
        annotation { "Name" : "Aftbody rout end", "Filter" : (EntityType.BODY && BodyType.MATE_CONNECTOR) || EntityType.VERTEX, "MaxNumberOfPicks" : 1 }
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
            
            annotation { "Name" : "Print debug", "Default" : false }
            definition.printDebug is boolean;
            
            
        }
        
        
    }
    {
        // ====================================================================
        // M1: intersection wire + Frenet edge-map cache (design steps 1-3)
        // ====================================================================
        const bottomFace     = qNthElement(qOwnedByBody(definition.bottomSheet, EntityType.FACE), 0);
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
                bottomFace, bottomCentroid);

        if (definition.showRefFrames)
        {
            drawRefFrames(context, edgeMaps);
        }

        if (definition.printDebug)
        {
            var ptCount = 0;
            for (var em in edgeMaps) { ptCount += size(em.pointArray); }
            println("[SW Rout] edge-map cache: " ~ toString(size(edgeMaps)) ~ " edges, " ~
                    toString(ptCount) ~ " sampled frames.");
        }

        // ---- M2+ (region division, wires, lofts, revolve, blend) to follow ----
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

// Builds the orthonormal Frenet-style frame at one sampled point:
//   xAxis = bottom-surface normal, oriented "up" (+Z side)  -> HEIGHT axis
//   yAxis = inward binormal, toward the bottom-sheet centroid -> WIDTH axis
//   zAxis = intersection-wire tangent (sign chosen so yAxis points inward)
// The inward direction is derived GEOMETRICALLY (horizontal vector toward the
// bottom-sheet centroid), so it is robust to rotated/mirrored parts rather than
// assuming a fixed global-Y layout.
function swRoutFrameAt(context is Context, pt is Vector, tangentDir is Vector,
        bottomFace is Query, bottomCentroid is Vector) returns CoordSystem
{
    // Up-normal from the bottom surface at the projected point.
    const uv  = evDistance(context, { "side0" : bottomFace, "side1" : pt }).sides[0].parameter;
    var   nrm = evFaceTangentPlane(context, { "face" : bottomFace, "parameter" : uv }).normal;
    const upNormal = (nrm[2] >= 0) ? nrm : -1 * nrm;

    // Tangent, orthogonalized against the normal (the intersection tangent lies
    // in the bottom surface, so this is a tiny correction).
    var tang = tangentDir - upNormal * dot(tangentDir, upNormal);
    if (norm(tang) < TANGENT_EPS)
    {
        tang = perpendicularVector(upNormal);
    }
    tang = normalize(tang);

    // Choose the tangent sign so cross(tang, upNormal) points inward.
    var inward = bottomCentroid - pt;
    inward = inward - vector(0, 0, 1) * inward[2];   // horizontal component only
    if (norm(inward) > TOLERANCE.zeroLength * meter && dot(cross(tang, upNormal), inward) < 0)
    {
        tang = -1 * tang;
    }

    return coordSystem(pt, upNormal, tang);
}

// Builds the ordered edge-map cache from the intersection wire body/bodies.
// Edges are ordered along the continuous path (constructPath) and then flipped
// as a whole so the table runs lowest X to highest X (design convention).
function buildEdgeMapCache(context is Context, wireQuery is Query,
        bottomFace is Query, bottomCentroid is Vector) returns array
{
    const allEdges = evaluateQuery(context, qOwnedByBody(wireQuery, EntityType.EDGE));
    if (size(allEdges) == 0)
    {
        throw regenError("SW Rout: the intersection produced no edges.");
    }

    // Continuous tip-to-tail ordering, independent of X-monotonicity at the tip.
    const pl = constructPath(context, qUnion(allEdges));

    var params = [];
    for (var k = 0; k <= FRAME_SAMPLES_PER_EDGE; k += 1)
    {
        params = append(params, k / FRAME_SAMPLES_PER_EDGE);
    }

    var edgeMaps = [];
    for (var i = 0; i < size(pl.edges); i += 1)
    {
        const e       = pl.edges[i];
        const flipped = pl.flipped[i];
        const useParams = flipped ? reverse(params) : params;

        const lines = evEdgeTangentLines(context, { "edge" : e, "parameters" : useParams });
        var pointArray = [];
        for (var j = 0; j < size(lines); j += 1)
        {
            const pt = lines[j].origin;
            var tang = lines[j].direction;
            if (flipped) { tang = -1 * tang; }
            const frame = swRoutFrameAt(context, pt, tang, bottomFace, bottomCentroid);
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

// Draws REF_FRAME_COUNT evenly-spaced frames along the cache:
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
    const count = min([REF_FRAME_COUNT, n]);
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
