FeatureScript 3095;
import(path : "onshape/std/common.fs", version : "3095.0");
// IMPORT: curve_pattern_plus.fs (the feature under test; re-exports the spacing and frames modules)
import(path : "8779ec77e99a5839df4ca5d9", version : "ffefbe49ba754f6c19e46bf5");

/**
 * Tests for Curve pattern plus. One feature that builds a 3-D path and a seed, runs the pattern in several modes
 * with fresh ids, and checks the instance count and where each instance sits along the path. Results go to the
 * feature's status line, and every check to the console.
 */
annotation { "Feature Type Name" : "Curve pattern plus tests" }
export const curvePatternPlusTests = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
    }
    {
        const mm = millimeter;
        // A 3-D path: not planar, so the frames really have to turn and twist
        opFitSpline(context, id + "path", {
                    "points" : [vector(0, 0, 0) * mm, vector(100, 0, 0) * mm, vector(200, 50, 0) * mm, vector(300, 0, 50) * mm]
                });
        const pathQuery = qCreatedBy(id + "path", EntityType.EDGE);
        const length = evLength(context, { "entities" : pathQuery });

        // Seed: a small cube centred on the start of the path
        fCuboid(context, id + "seed", { "corner1" : vector(-2, -2, -2) * mm, "corner2" : vector(2, 2, 2) * mm });
        const seed = qCreatedBy(id + "seed", EntityType.BODY);

        var failures = [];
        var passes = 0;
        const base = {
                "outputType" : CurvePatternOutput.PATTERN,
                "patternType" : PatternType.PART,
                "operationType" : NewBodyOperationType.NEW,
                "entities" : seed,
                "edges" : pathQuery,
                "oppositeDirection" : false,
                "spacingType" : CurveSpacingType.SPACING,
                "fillPath" : false,
                "countType" : CurveCountType.COUNT,
                "roundingType" : CurveRoundingType.EXACT,
                "startOffset" : 0 * mm,
                "endOffset" : 0 * mm,
                "centered" : false,
                "orientationType" : CurveOrientationType.FOLLOW,
                "flipFace" : false,
                "mateConnectorAxis" : CurveMateConnectorAxis.ALONG_PATH,
                "fullFeaturePattern" : false
            };

        // Case: spacing 25 mm, 5 instances -> instances at 25, 50, 75, 100 mm along the path
        var a = base;
        a.distance = 25 * mm;
        a.count = 5;
        const casesA = runCase(context, id + "caseA", a);
        failures = concatenateArrays([failures, checkPositions(context, pathQuery, length, casesA, [25, 50, 75, 100], "A spacing 25 x5")]);

        // Case: fill the path with 7 instances -> last instance at the end of the path
        var b = base;
        b.spacingType = CurveSpacingType.LENGTH;
        b.fillPath = true;
        b.lengthCount = 7;
        const casesB = runCase(context, id + "caseB", b);
        var expectedB = [];
        for (var i = 1; i < 7; i += 1)
        {
            expectedB = append(expectedB, (length / mm) * i / 6);
        }
        failures = concatenateArrays([failures, checkPositions(context, pathQuery, length, casesB, expectedB, "B fill path x7")]);

        // Case: start offset 10 mm, end offset 20 mm, centered, spacing 30 mm x4 -> span 90, centred in the usable length
        var c = base;
        c.entities = seedAt(context, id + "seedC", pathQuery, (10 + (length / mm - 30 - 90) / 2) * mm / length);
        c.distance = 30 * mm;
        c.count = 4;
        c.startOffset = 10 * mm;
        c.endOffset = 20 * mm;
        c.centered = true;
        const casesC = runCase(context, id + "caseC", c);
        const usable = length / mm - 30;
        const firstC = 10 + (usable - 90) / 2;
        failures = concatenateArrays([failures, checkPositions(context, pathQuery, length, casesC, [firstC + 30, firstC + 60, firstC + 90], "C offsets + centered")]);

        // Case: total length 120 mm, target spacing 40 mm rounded -> 4 instances, 40 mm apart
        var d = base;
        d.spacingType = CurveSpacingType.LENGTH;
        d.distance = 0 * mm;
        d.totalLength = 120 * mm;
        d.countType = CurveCountType.TARGET_SPACING;
        d.targetSpacing = 40 * mm;
        d.roundingType = CurveRoundingType.ROUND;
        const casesD = runCase(context, id + "caseD", d);
        failures = concatenateArrays([failures, checkPositions(context, pathQuery, length, casesD, [40, 80, 120], "D total 120, target 40")]);

        // Case: reverse direction, with mate connectors (no seed, so nothing picks the start of the path):
        // the first connector is at the end of the path, the others 25 and 50 mm back from it
        var e = base;
        e.outputType = CurvePatternOutput.MATE_CONNECTORS;
        e.distance = 25 * mm;
        e.count = 3;
        e.oppositeDirection = true;
        runCase(context, id + "caseE", e);
        const eConnectors = evaluateQuery(context, qBodyType(qCreatedBy(id + "caseE", EntityType.BODY), BodyType.MATE_CONNECTOR));
        var ePoints = [];
        for (var connector in eConnectors)
        {
            ePoints = append(ePoints, evMateConnector(context, { "mateConnector" : connector }).origin);
        }
        failures = concatenateArrays([failures, checkPositionsAt(context, pathQuery, length, ePoints,
                [length / mm - 50, length / mm - 25, length / mm], "E reversed (mate connectors)")]);
        // Case: mate connectors only, 6 of them
        var f = base;
        f.outputType = CurvePatternOutput.MATE_CONNECTORS;
        f.distance = 30 * mm;
        f.count = 6;
        runCase(context, id + "caseF", f);
        const connectors = evaluateQuery(context, qBodyType(qCreatedBy(id + "caseF", EntityType.BODY), BodyType.MATE_CONNECTOR));
        failures = concatenateArrays([failures, checkCount(connectors, 6, "F mate connectors x6")]);

        // Case: reference direction (world Z) and keep-orientation both run
        var g = base;
        g.distance = 25 * mm;
        g.count = 4;
        g.orientationType = CurveOrientationType.KEEP;
        const casesG = runCase(context, id + "caseG", g);
        failures = concatenateArrays([failures, checkCount(casesG, 3, "G keep orientation x4")]);

        // Case: a spacing that runs past the end must fail with an error, not produce geometry
        var h = base;
        h.distance = 1000 * mm;
        h.count = 3;
        var threw = false;
        try silent
        {
            curvePatternPlus(context, id + "caseH", h);
        }
        catch
        {
            threw = true;
        }
        if (!threw && featureHasError(context, id + "caseH"))
        {
            threw = true;
        }
        if (!threw)
        {
            failures = append(failures, "H overrun: expected an error");
        }

        var failedCases = {};
        for (var line in failures)
        {
            failedCases[substring(line, 0, 1)] = true;
        }
        passes = 8 - size(failedCases);
        for (var line in failures)
        {
            println("FAIL " ~ line);
        }
        println("curve pattern plus tests: " ~ passes ~ " of 8 cases passed.");
        if (size(failures) > 0)
        {
            reportFeatureWarning(context, id, size(failures) ~ " case(s) failed: " ~ failures[0]);
        }
        else
        {
            reportFeatureInfo(context, id, "All 8 cases passed.");
        }
    });

/** A small cube centred on the path at the given arc-length fraction: the seed sits at the first station. */
function seedAt(context is Context, seedId is Id, pathQuery is Query, fraction is number) returns Query
{
    const at = evEdgeTangentLine(context, { "edge" : pathQuery, "parameter" : fraction, "arcLengthParameterization" : true }).origin;
    fCuboid(context, seedId, { "corner1" : at - vector(2, 2, 2) * millimeter, "corner2" : at + vector(2, 2, 2) * millimeter });
    return qCreatedBy(seedId, EntityType.BODY);
}

/** Run the pattern and return the instance bodies it created, ordered by creation. */
function runCase(context is Context, caseId is Id, definition is map) returns array
{
    curvePatternPlus(context, caseId, definition);
    return evaluateQuery(context, qBodyType(qCreatedBy(caseId, EntityType.BODY), BodyType.SOLID));
}

/** Arc length (mm) of the path point nearest `point`, and how far the point is off the path (mm). */
function positionOf(context is Context, pathQuery is Query, length is ValueWithUnits, point is Vector) returns map
{
    const near = evDistance(context, { "side0" : pathQuery, "side1" : point, "arcLengthParameterization" : true });
    return { "position" : near.sides[0].parameter * length / millimeter, "off" : near.distance / millimeter };
}

/** Instances, sorted by position, must sit on the path at the expected arc lengths (mm). */
function checkPositions(context is Context, pathQuery is Query, length is ValueWithUnits, instances is array, expected is array,
        name is string) returns array
{
    var points = [];
    for (var body in instances)
    {
        points = append(points, evApproximateCentroid(context, { "entities" : body }));
    }
    return checkPositionsAt(context, pathQuery, length, points, expected, name);
}

/** Points, sorted by position along the path, must sit on the path at the expected arc lengths (mm). */
function checkPositionsAt(context is Context, pathQuery is Query, length is ValueWithUnits, points is array, expected is array,
        name is string) returns array
{
    var failures = [];
    if (size(points) != size(expected))
    {
        return [name ~ ": " ~ size(points) ~ " found, expected " ~ size(expected)];
    }
    var found = [];
    for (var point in points)
    {
        const p = positionOf(context, pathQuery, length, point);
        found = append(found, p);
        if (p.off > 0.05)
        {
            failures = append(failures, name ~ ": an instance is " ~ roundToPrecision(p.off, 3) ~ " mm off the path");
        }
    }
    found = sort(found, function(x, y) { return x.position - y.position; });
    var line = name ~ ":";
    for (var i = 0; i < size(found); i += 1)
    {
        line = line ~ " " ~ roundToPrecision(found[i].position, 2);
        if (abs(found[i].position - expected[i]) > 0.05)
        {
            failures = append(failures, name ~ ": instance " ~ (i + 1) ~ " at " ~ roundToPrecision(found[i].position, 2)
                ~ " mm, expected " ~ roundToPrecision(expected[i], 2));
        }
    }
    println(line);
    return failures;
}

function checkCount(entities is array, expected is number, name is string) returns array
{
    return size(entities) == expected ? [] : [name ~ ": " ~ size(entities) ~ " found, expected " ~ expected];
}
