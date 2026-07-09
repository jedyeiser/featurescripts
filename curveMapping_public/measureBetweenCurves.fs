FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");
import(path : "onshape/std/path.fs", version : "2892.0");

IconNamespace::import(path : "010318fea6f2c95303990c0e", version : "a06cd2e9297f4b40538de361");


/**
 * Measures the distance between two groups of Edges or Wire Bodies. Results are output in a table. 
 */
 
 export enum CurveMeasurementType
 {
     CLOSEST,
     NORMAL_DIST,
     ALONG_AXIS
 }
 
 export enum AlongType
 {
     ALONG_CURVE,
     ALONG_AXIS,
 }
 
 export enum PointSpacingType
 {
     NUMBER_OF_POINTS,
     POINT_SPACING
 }
 
 export enum ExportUnits
 {
     MILLIMETER,
     CENTIMETER,
     METER,
     INCH
 }
 
 export enum AxisDefinition
 {
     annotation {"Name" : "World X", "Icon" : Icon.ALONG_X}
     ALONG_X,
     annotation {"Name" : "World Y", "Icon" : Icon.ALONG_Y}
     ALONG_Y,
     annotation {"Name" : "World Z", "Icon" : Icon.ALONG_Z}
     ALONG_Z,
     CUSTOM
 }
 
 export enum CustomAxisType
 {
     QUERY,
     INPUT
 }
 
 
 
 export const DecimalPrecisionBounds = {(unitless) : [0, 3, 10]} as IntegerBoundSpec;
 export const NumPointsBounds = {(unitless) : [2, 20, 200]} as IntegerBoundSpec;
 export const PointSpacingBounds = {(millimeter) : [0.5, 10, 100]} as LengthBoundSpec;
 export const AxisDefinitionBounds = {(unitless) : [-1e7, 0, 1e7] } as RealBoundSpec;

// Attribute key under which all "Measure curve distance" features accumulate their rows
// on the part-studio origin. One shared name; per-feature entries keyed by feature id.
const MEASURE_ATTR_NAME = "MeasureBetweenCurvesData";

// Flatten an edges/wire-bodies selection into a flat edge query.
function expandToEdges(context is Context, q is Query) returns Query
{
    var directEdges = qEntityFilter(q, EntityType.EDGE);
    var wireEdges = qOwnedByBody(qBodyType(qEntityFilter(q, EntityType.BODY), BodyType.WIRE), EntityType.EDGE);
    return qUnion([directEdges, wireEdges]);
}

// 10^n for a small non-negative integer n (FeatureScript has no exponent operator).
function pow10(n is number) returns number
{
    var f = 1;
    for (var i = 0; i < n; i += 1)
    {
        f = f * 10;
    }
    return f;
}

function roundToDecimals(v is number, decimals is number) returns number
{
    var f = pow10(decimals);
    return round(v * f) / f;
}

// Display scale factor: meters -> chosen table units.
function unitScale(units is ExportUnits) returns number
{
    if (units == ExportUnits.MILLIMETER)
    {
        return 1000;
    }
    if (units == ExportUnits.CENTIMETER)
    {
        return 100;
    }
    if (units == ExportUnits.METER)
    {
        return 1;
    }
    if (units == ExportUnits.INCH)
    {
        return 1000 / 25.4;
    }
    return 1000;
}

function unitSuffix(units is ExportUnits, include is boolean) returns string
{
    if (!include)
    {
        return "";
    }
    if (units == ExportUnits.MILLIMETER)
    {
        return " mm";
    }
    if (units == ExportUnits.CENTIMETER)
    {
        return " cm";
    }
    if (units == ExportUnits.METER)
    {
        return " m";
    }
    if (units == ExportUnits.INCH)
    {
        return " in";
    }
    return "";
}

// Format a length (stored in meters) for a table cell.
function fmtLength(meters is ValueWithUnits, fmt is map) returns string
{
    var v = roundToDecimals(meters.value * unitScale(fmt.units), fmt.decimals);
    return toString(v) ~ unitSuffix(fmt.units, fmt.include);
}

// Format a world point for a table cell as "(x, y, z)".
function fmtPoint(pt is Vector, fmt is map) returns string
{
    var s = unitScale(fmt.units);
    var x = roundToDecimals(pt[0].value * s, fmt.decimals);
    var y = roundToDecimals(pt[1].value * s, fmt.decimals);
    var z = roundToDecimals(pt[2].value * s, fmt.decimals);
    return "(" ~ toString(x) ~ ", " ~ toString(y) ~ ", " ~ toString(z) ~ ")" ~ unitSuffix(fmt.units, fmt.include);
}

// ---- From-chain sampling toolkit ----------------------------------------

// Build a reusable description of the G1 from-chain: ordered edges + cumulative arc lengths.
function buildChain(context is Context, fromEdges is Query) returns map
{
    var path;
    try
    {
        path = constructPath(context, fromEdges);
    }
    catch (e)
    {
        throw regenError("'Measure from' edges must be G1 continuous and form a single chain.");
    }

    var edges = path.edges;
    var flipped = path.flipped;
    var n = size(edges);

    var lens = [];
    var cum = [0 * meter];
    var total = 0 * meter;
    for (var i = 0; i < n; i += 1)
    {
        var li = evLength(context, { "entities" : edges[i] });
        lens = append(lens, li);
        total = total + li;
        cum = append(cum, total);
    }
    return { "edges" : edges, "flipped" : flipped, "lens" : lens, "cum" : cum, "total" : total };
}

// Evaluate { point, tangent } at a global arc length s along the chain.
function chainPointAt(context is Context, chain is map, s is ValueWithUnits) returns map
{
    var n = size(chain.edges);
    var ss = s;
    if (ss < 0 * meter)
    {
        ss = 0 * meter;
    }
    if (ss > chain.total)
    {
        ss = chain.total;
    }
    var ei = 0;
    for (var i = 0; i < n; i += 1)
    {
        if (chain.cum[i] <= ss)
        {
            ei = i;
        }
    }
    var t = (chain.lens[ei].value > 1e-12) ? (ss - chain.cum[ei]).value / chain.lens[ei].value : 0;
    if (t < 0)
    {
        t = 0;
    }
    if (t > 1)
    {
        t = 1;
    }
    if (chain.flipped[ei])
    {
        t = 1 - t;
    }
    var ln = evEdgeTangentLines(context, { "edge" : chain.edges[ei], "parameters" : [t] })[0];
    var tan = chain.flipped[ei] ? -1 * ln.direction : ln.direction;
    return { "point" : ln.origin, "tangent" : tan };
}

// Dense ordered table [{ s, point, tangent }] for projecting a reference and inverting an axis
// coordinate. Cost is one evEdgeTangentLines call per edge (params batched).
function buildDenseTable(context is Context, chain is map, mPerEdge is number) returns array
{
    var dense = [];
    var n = size(chain.edges);
    for (var ei = 0; ei < n; ei += 1)
    {
        var fractions = [];
        var params = [];
        for (var a = 0; a <= mPerEdge; a += 1)
        {
            var frac = a / mPerEdge;
            fractions = append(fractions, frac);
            params = append(params, chain.flipped[ei] ? (1 - frac) : frac);
        }
        var lines = evEdgeTangentLines(context, { "edge" : chain.edges[ei], "parameters" : params });
        for (var a = 0; a <= mPerEdge; a += 1)
        {
            if (ei > 0 && a == 0)
            {
                continue; // skip the vertex shared with the previous edge
            }
            var sGlobal = chain.cum[ei] + fractions[a] * chain.lens[ei];
            var tan = chain.flipped[ei] ? -1 * lines[a].direction : lines[a].direction;
            dense = append(dense, { "s" : sGlobal, "point" : lines[a].origin, "tangent" : tan });
        }
    }
    return dense;
}

// Anchor a plane / vertex / mate-connector reference onto the chain. For a vertex or mate
// connector, project its point onto the curve; for a plane, find where the curve crosses it.
// Returns { s, point } of the nearest dense-table sample.
function anchorOnChain(context is Context, dense is array, q is Query) returns map
{
    var usePlane = false;
    var refPt = vector(0, 0, 0) * meter;
    var pl = undefined;
    if (!isQueryEmpty(context, qBodyType(q, BodyType.MATE_CONNECTOR)))
    {
        refPt = evMateConnector(context, { "mateConnector" : q }).origin;
    }
    else if (!isQueryEmpty(context, qEntityFilter(q, EntityType.VERTEX)))
    {
        refPt = evVertexPoint(context, { "vertex" : q });
    }
    else
    {
        usePlane = true;
        pl = evPlane(context, { "face" : q });
    }

    var bestIdx = 0;
    var bestD = 0 * meter;
    for (var i = 0; i < size(dense); i += 1)
    {
        var d;
        if (usePlane)
        {
            d = abs(dot(dense[i].point - pl.origin, pl.normal));
        }
        else
        {
            d = norm(dense[i].point - refPt);
        }
        if (i == 0 || d < bestD)
        {
            bestD = d;
            bestIdx = i;
        }
    }
    return { "s" : dense[bestIdx].s, "point" : dense[bestIdx].point };
}

// Resolve the ALONG_AXIS point-spacing direction to a unit vector.
function resolveSpacingAxis(context is Context, definition is map) returns Vector
{
    var a = definition.pointSpacingAxis;
    if (a == AxisDefinition.ALONG_X)
    {
        return vector(1, 0, 0);
    }
    if (a == AxisDefinition.ALONG_Y)
    {
        return vector(0, 1, 0);
    }
    if (a == AxisDefinition.ALONG_Z)
    {
        return vector(0, 0, 1);
    }
    if (definition.customPointSpacingAxisType == CustomAxisType.INPUT)
    {
        var v = vector(definition.spacingAlongVector_X, definition.spacingAlongVector_Y, definition.spacingAlongVector_Z);
        if (norm(v) < 1e-9)
        {
            throw regenError("Point-spacing axis vector is zero.");
        }
        return normalize(v);
    }
    var q = definition.pointSpacingNormalQuery;
    if (!isQueryEmpty(context, qBodyType(q, BodyType.MATE_CONNECTOR)))
    {
        return evMateConnector(context, { "mateConnector" : q }).zAxis;
    }
    return evPlane(context, { "face" : q }).normal;
}

// Clamp to [0, total], sort ascending, drop near-duplicates.
function sortAndDedup(targets is array, total is ValueWithUnits) returns array
{
    var arr = [];
    for (var s in targets)
    {
        var ss = s;
        if (ss < 0 * meter)
        {
            ss = 0 * meter;
        }
        if (ss > total)
        {
            ss = total;
        }
        arr = append(arr, ss);
    }
    for (var i = 1; i < size(arr); i += 1)
    {
        var key = arr[i];
        var j = i - 1;
        while (j >= 0 && arr[j] > key)
        {
            arr[j + 1] = arr[j];
            j = j - 1;
        }
        arr[j + 1] = key;
    }
    var dedupTol = 1e-6 * meter;
    var out = [];
    for (var i = 0; i < size(arr); i += 1)
    {
        if (size(out) == 0 || abs(arr[i] - out[size(out) - 1]) > dedupTol)
        {
            out = append(out, arr[i]);
        }
    }
    return out;
}

// Invert an axis coordinate ft to a global arc length via the dense table (first bracket, linear).
function invertAxisCoord(dense is array, fvals is array, ft is ValueWithUnits) returns ValueWithUnits
{
    var n = size(fvals);
    for (var i = 0; i < n - 1; i += 1)
    {
        var fa = fvals[i];
        var fb = fvals[i + 1];
        var lo = (fa < fb) ? fa : fb;
        var hi = (fa < fb) ? fb : fa;
        if (ft >= lo && ft <= hi)
        {
            var denom = fb - fa;
            var w = (abs(denom) < 1e-12 * meter) ? 0 : (ft - fa) / denom;
            return dense[i].s + w * (dense[i + 1].s - dense[i].s);
        }
    }
    return (abs(ft - fvals[0]) <= abs(ft - fvals[n - 1])) ? dense[0].s : dense[n - 1].s;
}

// Target arc lengths for ALONG_AXIS spacing (equal increments of the axis projection).
function axisSpacingTargets(context is Context, chain is map, dense is array, axis is Vector, definition is map) returns array
{
    var origin = dense[0].point;
    var fvals = [];
    for (var i = 0; i < size(dense); i += 1)
    {
        fvals = append(fvals, dot(dense[i].point - origin, axis));
    }
    var fMin = fvals[0];
    var fMax = fvals[0];
    for (var i = 1; i < size(fvals); i += 1)
    {
        if (fvals[i] < fMin)
        {
            fMin = fvals[i];
        }
        if (fvals[i] > fMax)
        {
            fMax = fvals[i];
        }
    }
    if ((fMax - fMin) < 1e-9 * meter)
    {
        throw regenError("The 'measure from' curve has no extent along the chosen spacing axis.");
    }

    var fTargets = [];
    if (definition.pointSpacing == PointSpacingType.NUMBER_OF_POINTS)
    {
        var nPts = definition.numPoints;
        for (var k = 0; k < nPts; k += 1)
        {
            var frac = (nPts == 1) ? 0 : k / (nPts - 1);
            fTargets = append(fTargets, fMin + (fMax - fMin) * frac);
        }
    }
    else
    {
        var dist = definition.pointSpacingDist;
        if ((fMax - fMin) / dist > 1000)
        {
            throw regenError("Point spacing too small for the axis extent (>1000 points). Increase the spacing.");
        }
        var anchor = anchorOnChain(context, dense, definition.pointSpacingRef);
        var f0 = dot(anchor.point - origin, axis);
        fTargets = append(fTargets, f0);
        var f = f0 - dist;
        while (f > fMin)
        {
            fTargets = append(fTargets, f);
            f = f - dist;
        }
        f = f0 + dist;
        while (f < fMax)
        {
            fTargets = append(fTargets, f);
            f = f + dist;
        }
        fTargets = append(fTargets, fMin);
        fTargets = append(fTargets, fMax);
    }

    var targets = [];
    for (var ti = 0; ti < size(fTargets); ti += 1)
    {
        var ft = fTargets[ti];
        if (ft < fMin)
        {
            ft = fMin;
        }
        if (ft > fMax)
        {
            ft = fMax;
        }
        targets = append(targets, invertAxisCoord(dense, fvals, ft));
    }
    return targets;
}

// Target arc lengths for ALONG_CURVE POINT_SPACING (anchored at a reference, endpoints included).
function arcSpacingTargets(context is Context, chain is map, dense is array, definition is map) returns array
{
    var dist = definition.pointSpacingDist;
    if (chain.total / dist > 1000)
    {
        throw regenError("Point spacing too small for the curve length (>1000 points). Increase the spacing.");
    }
    var s0 = anchorOnChain(context, dense, definition.pointSpacingRef).s;
    var targets = [s0];
    var s = s0 - dist;
    while (s > 0 * meter)
    {
        targets = append(targets, s);
        s = s - dist;
    }
    s = s0 + dist;
    while (s < chain.total)
    {
        targets = append(targets, s);
        s = s + dist;
    }
    targets = append(targets, 0 * meter);
    targets = append(targets, chain.total);
    return targets;
}

// Establish the measurement sample points on the from-chain per the UI spacing options.
// Returns an array of { "point" : Vector, "tangent" : Vector }, ordered along the chain.
function establishSamples(context is Context, definition is map, fromEdges is Query) returns array
{
    var chain = buildChain(context, fromEdges);
    var targets = [];

    if (definition.pointSpacingAlong == AlongType.ALONG_CURVE)
    {
        if (definition.pointSpacing == PointSpacingType.NUMBER_OF_POINTS)
        {
            for (var k = 0; k < definition.numPoints; k += 1)
            {
                var frac = (definition.numPoints == 1) ? 0 : k / (definition.numPoints - 1);
                targets = append(targets, chain.total * frac);
            }
        }
        else
        {
            var dense = buildDenseTable(context, chain, 100);
            targets = arcSpacingTargets(context, chain, dense, definition);
        }
    }
    else
    {
        var dense = buildDenseTable(context, chain, 100);
        var axis = resolveSpacingAxis(context, definition);
        targets = axisSpacingTargets(context, chain, dense, axis, definition);
    }

    targets = sortAndDedup(targets, chain.total);

    var samples = [];
    for (var ti = 0; ti < size(targets); ti += 1)
    {
        samples = append(samples, chainPointAt(context, chain, targets[ti]));
    }
    return samples;
}

// Resolve the ALONG_AXIS measurement direction to a unit vector.
function resolveMeasurementAxis(context is Context, definition is map) returns Vector
{
    var a = definition.measurementDirectionAxis;
    if (a == AxisDefinition.ALONG_X)
    {
        return vector(1, 0, 0);
    }
    if (a == AxisDefinition.ALONG_Y)
    {
        return vector(0, 1, 0);
    }
    if (a == AxisDefinition.ALONG_Z)
    {
        return vector(0, 0, 1);
    }
    // CUSTOM
    if (definition.measurementDirectionAxisType == CustomAxisType.INPUT)
    {
        var v = vector(definition.measurementAlongVector_X, definition.measurementAlongVector_Y, definition.measurementAlongVector_Z);
        if (norm(v) < 1e-9)
        {
            throw regenError("Measurement axis vector is zero.");
        }
        return normalize(v);
    }
    // QUERY: mate connector Z axis (standard) or planar-face normal.
    var q = definition.measurementNormalQuery;
    if (!isQueryEmpty(context, qBodyType(q, BodyType.MATE_CONNECTOR)))
    {
        return evMateConnector(context, { "mateConnector" : q }).zAxis;
    }
    return evPlane(context, { "face" : q }).normal;
}

// Best-fit plane normal for an ordered set of points (Newell's method). Returns a unitless unit vector.
function fitPlaneNormal(points is array) returns Vector
{
    var n = size(points);
    var nx = 0;
    var ny = 0;
    var nz = 0;
    for (var i = 0; i < n; i += 1)
    {
        var j = (i + 1 == n) ? 0 : i + 1;
        var c = points[i];
        var d = points[j];
        nx = nx + (c[1].value - d[1].value) * (c[2].value + d[2].value);
        ny = ny + (c[2].value - d[2].value) * (c[0].value + d[0].value);
        nz = nz + (c[0].value - d[0].value) * (c[1].value + d[1].value);
    }
    var nv = vector(nx, ny, nz);
    if (norm(nv) < 1e-12)
    {
        throw regenError("The 'measure from' curve is too straight to define a plane. Normal/Along-axis modes need a planar (curved) source, or more sample points.");
    }
    return normalize(nv);
}


annotation { "Feature Type Name" : "Measure curve distance", "Feature Type Description" : "Measures the distance between two groups of edges/wires and outputs results in a table", "Icon" : IconNamespace::BLOB_DATA }
export const measureBetweenCurves = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Measurement Type", "Default" : CurveMeasurementType.NORMAL_DIST, "UIHint" : UIHint.HORIZONTAL_ENUM }
        definition.measurementType is CurveMeasurementType;
        
        annotation { "Name" : "Measurement Name", "Default" : "MeasureBetweenCurves", "Description" : "Name of the measurement to be displayed in the table" }
        definition.myString is string;
        
        
        if (definition.measurementType == CurveMeasurementType.ALONG_AXIS)
        {
            annotation { "Name" : "Measurement direction", "UIHint" : UIHint.SHOW_LABEL, "Default" : AxisDefinition.ALONG_Z}
            definition.measurementDirectionAxis is AxisDefinition;
            
            if (definition.measurementDirectionAxis == AxisDefinition.CUSTOM)
            {
                annotation { "Name" : "Measurement direction axis type", "Default" : CustomAxisType.INPUT}
                definition.measurementDirectionAxisType is CustomAxisType;
                
                if (definition.measurementDirectionAxisType == CustomAxisType.INPUT)
                {
                    annotation { "Name" : "X", "Icon" : Icon.ALONG_X }
                    isReal(definition.measurementAlongVector_X, AxisDefinitionBounds);
    
                    annotation { "Name" : "Y", "Icon" : Icon.ALONG_Y }
                    isReal(definition.measurementAlongVector_Y, AxisDefinitionBounds);
    
                    annotation { "Name" : "Z", "Icon" : Icon.ALONG_Z }
                    isReal(definition.measurementAlongVector_Z, AxisDefinitionBounds);   
                }
                else
                {
                    annotation { "Name" : "Measurement axis normal to", "Filter" : GeometryType.PLANE || EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
                    definition.measurementNormalQuery is Query;
                }
                
            }
            
        }
        
        annotation { "Name" : "Measure from", "Filter" : EntityType.EDGE || BodyType.WIRE, "Description" : "Entities to measure from. Must be G1 continuous"}
        definition.measureFrom is Query;
        
        annotation { "Name" : "Measure to", "Filter" : EntityType.EDGE || BodyType.WIRE, "Description" : "Entities to measure to. Must be G0 continuous"}
        definition.measureTo is Query;
        
        annotation { "Name" : "Point Spacing", "Default" : PointSpacingType.NUMBER_OF_POINTS, "Description" : "How measurement points will be spaced. Either along the from curves, or along a specified axis", "UIHint" : UIHint.SHOW_LABEL  }
        definition.pointSpacing is PointSpacingType;
        
        if (definition.pointSpacing == PointSpacingType.NUMBER_OF_POINTS)
        {
            annotation { "Name" : "Number of points" }
            isInteger(definition.numPoints, NumPointsBounds);
        }
        
        if (definition.pointSpacing == PointSpacingType.POINT_SPACING)
        {
            annotation { "Name" : "Spacing reference", "Filter" : GeometryType.PLANE || EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1, "Description" : "Point to start point spacing from" }
            definition.pointSpacingRef is Query;
            
            annotation { "Name" : "Distance between points" }
            isLength(definition.pointSpacingDist, PointSpacingBounds);
        }
        
        annotation { "Name" : "Spacing type", "Default" : AlongType.ALONG_CURVE, "Description" : "Defines if measurement points should be spaced on from curve, or along an axis" }
        definition.pointSpacingAlong is AlongType;
        
        if (definition.pointSpacingAlong == AlongType.ALONG_AXIS)
        {
            annotation { "Name" : "Point spacing along axis", "UIHint" : UIHint.SHOW_LABEL, "Default" : AxisDefinition.ALONG_X}
            definition.pointSpacingAxis is AxisDefinition;
            
            if (definition.pointSpacingAxis == AxisDefinition.CUSTOM)
            {
                annotation { "Name" : "Axis definition type", "Default" : CustomAxisType.INPUT}
                definition.customPointSpacingAxisType is CustomAxisType;
                
                if (definition.customPointSpacingAxisType == CustomAxisType.INPUT)
                {
                    annotation { "Name" : "X", "Icon" : Icon.ALONG_X }
                    isReal(definition.spacingAlongVector_X, AxisDefinitionBounds);
    
                    annotation { "Name" : "Y", "Icon" : Icon.ALONG_Y }
                    isReal(definition.spacingAlongVector_Y, AxisDefinitionBounds);
    
                    annotation { "Name" : "Z", "Icon" : Icon.ALONG_Z }
                    isReal(definition.spacingAlongVector_Z, AxisDefinitionBounds);   
                }
                else
                {
                    annotation { "Name" : "Point spacing normal to", "Filter" : GeometryType.PLANE || EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
                    definition.pointSpacingNormalQuery is Query;
                }
                
            }
            
        }
        
        annotation { "Group Name" : "Table formatting", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Table units", "UIHint" : UIHint.SHOW_LABEL }
            definition.tableUnits is ExportUnits;
            
            annotation { "Name" : "Include units in table?", "Default" : false , "Description" : "Allows table to include or exclude units"}
            definition.includeUints is boolean;
            
            annotation { "Name" : "Decimal precision", "Description" : "Number of decimals to include. If value requires fewer decimals, the minimum number of decimals are included" }
            isInteger(definition.decimalPrecision, DecimalPrecisionBounds);

            annotation { "Name" : "Reverse point order", "Default" : false, "Description" : "Flip the order in which points are evaluated and listed in the table" }
            definition.reverseOrder is boolean;

            annotation { "Name" : "Start point numbers at 1", "Default" : false, "Description" : "When off, the first point is numbered 0; when on, it is numbered 1" }
            definition.startAtOne is boolean;
        }
        
        annotation { "Name" : "Clear accumulated data", "Default" : false, "Description" : "Resets the shared measurement table to only this feature's data. Use to clear stale rows left behind by deleted measure features: toggle on, regenerate, then toggle off." }
        definition.clearStale is boolean;

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Show from points", "Default" : false } // DebugColor.CYAN
            definition.showFromPoints is boolean;
            
            annotation { "Name" : "Show to points", "Default" : false } // DebugColor.MAGENTA
            definition.showToPoints is boolean;
            
            annotation { "Name" : "Show point connections", "Default" : false } // DebugColor.GREEN
            definition.showConnections is boolean;
        }
        
        
        
        
        
    }
    {
        // All sampling modes (number-of-points / point-spacing, along-curve / along-axis) and all
        // measurement modes (CLOSEST / NORMAL_DIST / ALONG_AXIS).
        var fromEdges = expandToEdges(context, definition.measureFrom);
        var toEdges = expandToEdges(context, definition.measureTo);
        if (isQueryEmpty(context, fromEdges))
        {
            throw regenError("Select edges or a wire body for 'Measure from'.", ["measureFrom"]);
        }
        if (isQueryEmpty(context, toEdges))
        {
            throw regenError("Select edges or a wire body for 'Measure to'.", ["measureTo"]);
        }

        // 1-3. Establish measurement points along the from-chain.
        var samples = establishSamples(context, definition, fromEdges);
        if (definition.reverseOrder)
        {
            var rev = [];
            for (var i = size(samples) - 1; i >= 0; i -= 1)
            {
                rev = append(rev, samples[i]);
            }
            samples = rev;
        }

        var fmt = {
                "units" : definition.tableUnits,
                "decimals" : definition.decimalPrecision,
                "include" : definition.includeUints
            };

        var mode = definition.measurementType;

        // Directional modes measure inside a plane that CONTAINS the measurement direction and
        // cuts ACROSS the to-curve (normal = cross(direction, planeNormal)). Resolve the from-curve
        // plane (and the axis) once up front.
        var planeNormal = undefined;
        var axisDir = undefined;
        if (mode != CurveMeasurementType.CLOSEST)
        {
            var pts = [];
            for (var sm in samples)
            {
                pts = append(pts, sm.point);
            }
            planeNormal = fitPlaneNormal(pts);
            if (mode == CurveMeasurementType.ALONG_AXIS)
            {
                axisDir = resolveMeasurementAxis(context, definition);
            }
        }

        var crossingTol = 1e-5 * meter; // the plane "crosses" the to-curve when evDistance ~ 0

        // 4-5. Measure each point and build table rows (directional modes skip points with no crossing).
        var rows = [];
        var numBase = definition.startAtOne ? 1 : 0;
        var validCount = 0;
        for (var i = 0; i < size(samples); i += 1)
        {
            var fromPt = samples[i].point;
            var toPt = undefined;
            var measVal = undefined;
            var valid = true;

            if (mode == CurveMeasurementType.CLOSEST)
            {
                var r = evDistance(context, { "side0" : fromPt, "side1" : toEdges });
                toPt = r.sides[1].point;
                measVal = r.distance;
            }
            else
            {
                var dir = (mode == CurveMeasurementType.NORMAL_DIST)
                    ? normalize(cross(planeNormal, samples[i].tangent))
                    : axisDir;
                var sliceNormal = cross(dir, planeNormal);
                if (norm(sliceNormal) < 1e-6)
                {
                    throw regenError("Measurement axis is parallel to the curve-plane normal; cannot build a measurement plane that cuts the curves.");
                }
                var measPlane = plane(fromPt, normalize(sliceNormal));
                var r = evDistance(context, { "side0" : measPlane, "side1" : toEdges });
                if (r.distance < crossingTol)
                {
                    toPt = r.sides[1].point;
                    measVal = dot(toPt - fromPt, dir);
                }
                else
                {
                    valid = false; // from-curve overruns the to-curve at this station
                }
            }

            if (valid)
            {
                if (definition.showFromPoints)
                {
                    addDebugPoint(context, fromPt, DebugColor.CYAN);
                }
                if (definition.showToPoints)
                {
                    addDebugPoint(context, toPt, DebugColor.MAGENTA);
                }
                if (definition.showConnections)
                {
                    addDebugLine(context, fromPt, toPt, DebugColor.GREEN);
                }

                rows = append(rows, {
                            "n" : toString(validCount + numBase),
                            "fromPt" : fmtPoint(fromPt, fmt),
                            "toPt" : fmtPoint(toPt, fmt),
                            "dist" : fmtLength(measVal, fmt)
                        });
                validCount = validCount + 1;
            }
        }

        // 6. Store rows on the part-studio origin, keyed per-feature so multiple measure
        //    features accumulate into one shared table set (read -> drop own -> append -> write).
        var myKey = toAttributeId(id);
        var anchors = evaluateQuery(context, qHasAttribute(MEASURE_ATTR_NAME));
        var existing = [];
        if (size(anchors) > 0)
        {
            var prev = getAttribute(context, { "entity" : anchors[0], "name" : MEASURE_ATTR_NAME });
            if (prev != undefined)
            {
                existing = prev;
            }
        }

        var rebuilt = [];
        if (!definition.clearStale)
        {
            for (var entry in existing)
            {
                if (entry.featureId != myKey)
                {
                    rebuilt = append(rebuilt, entry);
                }
            }
        }
        rebuilt = append(rebuilt, { "featureId" : myKey, "name" : definition.myString, "rows" : rows });

        setAttribute(context, {
                    "entities" : qOrigin(EntityType.BODY),
                    "name" : MEASURE_ATTR_NAME,
                    "attribute" : rebuilt
                });
    });
    
annotation { "Table Type Name" : "Measure between curves" }
export const measureBetweenCurvesTable = defineTable(function(context is Context, definition is map) returns TableArray
    precondition
    {
    }
    {
        var anchors = evaluateQuery(context, qHasAttribute(MEASURE_ATTR_NAME));
        if (size(anchors) == 0)
        {
            return tableArray([table("Measure between curves (No Data)", [], [])]);
        }

        var entries = getAttribute(context, {
                    "entity" : anchors[0],
                    "name" : MEASURE_ATTR_NAME
                });
        if (entries == undefined || size(entries) == 0)
        {
            return tableArray([table("Measure between curves (No Data)", [], [])]);
        }

        var cols = [
                tableColumnDefinition("n", "Point #"),
                tableColumnDefinition("fromPt", "From point"),
                tableColumnDefinition("toPt", "To point"),
                tableColumnDefinition("dist", "Distance")
            ];

        var tables = [];
        for (var entry in entries)
        {
            var title = (entry.name == "") ? "Measurement between curves" : ("Measurement between curves: " ~ entry.name);

            var rows = [];
            for (var r in entry.rows)
            {
                rows = append(rows, tableRow(r));
            }

            tables = append(tables, table(title, cols, rows));
        }

        return tableArray(tables);
    });


