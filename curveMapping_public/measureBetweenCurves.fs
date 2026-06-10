FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");
import(path : "onshape/std/path.fs", version : "2892.0");

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

// Sample numPoints points uniformly by arc length along a G1 chain.
// Returns an array of { "point" : Vector, "tangent" : Vector } in traversal order.
function sampleFromChainUniform(context is Context, fromEdges is Query, numPoints is number) returns array
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

    var samples = [];
    for (var k = 0; k < numPoints; k += 1)
    {
        var frac = (numPoints == 1) ? 0 : k / (numPoints - 1);
        var s = total * frac;

        var ei = 0;
        for (var i = 0; i < n; i += 1)
        {
            if (cum[i] <= s)
            {
                ei = i;
            }
        }

        var t = (lens[ei].value > 1e-12) ? (s - cum[ei]).value / lens[ei].value : 0;
        if (t < 0)
        {
            t = 0;
        }
        if (t > 1)
        {
            t = 1;
        }
        if (flipped[ei])
        {
            t = 1 - t;
        }

        var ln = evEdgeTangentLines(context, { "edge" : edges[ei], "parameters" : [t] })[0];
        var tan = flipped[ei] ? -1 * ln.direction : ln.direction;
        samples = append(samples, { "point" : ln.origin, "tangent" : tan });
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


annotation { "Feature Type Name" : "Measure curve distance", "Feature Type Description" : "Measures the distance between two groups of edges/wires and outputs results in a table" }
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
            annotation { "Name" : "Point spacing along axis", "UIHint" : UIHint.SHOW_LABEL, "Default" : AxisDefinition.ALONG_Z}
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
        // Phases 1-2: uniform sampling along the from-chain; CLOSEST / NORMAL_DIST / ALONG_AXIS.
        // (POINT_SPACING and along-axis spacing land in the next phase.)
        if (definition.pointSpacing != PointSpacingType.NUMBER_OF_POINTS)
        {
            throw regenError("Only 'Number of points' spacing is implemented so far.");
        }

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
        var samples = sampleFromChainUniform(context, fromEdges, definition.numPoints);

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
        var rowNum = 0;
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
                rowNum = rowNum + 1;
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
                            "n" : toString(rowNum),
                            "fromPt" : fmtPoint(fromPt, fmt),
                            "toPt" : fmtPoint(toPt, fmt),
                            "dist" : fmtLength(measVal, fmt)
                        });
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


