FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: clean_surfaces_icon.svg (feature icon)
IconNamespace::import(path : "d4ae9caa9e5c8491cb5feb50", version : "c2a9472c4a1c1f94745df11f");

/**
 * Clean surfaces (milestone 1: Diagnose).
 *
 * Copies the input sheet body and colours every face of the COPY by how bad it is (sliver, tiny edge, tiny area).
 * The input is not touched. Cleaning (Clean mode) is not built yet; see clean_surfaces/DESIGN.md.
 *
 * Scope (2026-11-06): "Faces to clean" limits the work to a set of faces (empty = every face of the surfaces);
 * "Faces to keep" are intentional (a pinch fillet, say): never scored, never to be changed.
 *
 * Severity of a face = the worst of three tests, each with a watch / bad / critical / degenerate band:
 *   mean width 2A/P, shortest edge, area.
 * The bands are multiples of the two dialog thresholds (sliver width W, short edge E):
 *   width  : critical < W,  bad < 3 W,  watch < 10 W
 *   edge   : critical < E,  bad < 10 E, watch < 50 E
 *   area   : critical < W^2, bad < 10 W^2, watch < 100 W^2
 *   degenerate : edge or area below a tenth of critical, or needle aspect (P / 2w) above 5000
 * Faces at critical or worse also get their true maximum width (paired samples of the two long edges, which 2A/P
 * understates for tapering strips), a taper ratio (narrowest / widest; a pinch fillet tapers to a point, a
 * manufacturing sliver is nearly uniform) and a location.
 */

export const SliverWidthBounds =
{
    (meter)      : [1e-6, 1e-4, 0.1],
    (centimeter) : 0.01,
    (millimeter) : 0.1,
    (inch)       : 0.004,
    (foot)       : 0.0003,
    (yard)       : 0.0001
} as LengthBoundSpec;

export const ShortEdgeBounds =
{
    (meter)      : [1e-7, 1e-5, 0.1],
    (centimeter) : 0.001,
    (millimeter) : 0.01,
    (inch)       : 0.0004,
    (foot)       : 0.00003,
    (yard)       : 0.00001
} as LengthBoundSpec;

/** Face colours: severity levels 0..4 (ok, watch, bad, critical, degenerate), 5 = keep, 6 = outside "Faces to clean". */
const LEVEL_COLORS = [
    color(0.80, 0.80, 0.80),
    color(1.00, 0.85, 0.20),
    color(1.00, 0.50, 0.10),
    color(0.85, 0.10, 0.10),
    color(0.35, 0.00, 0.45),
    color(0.25, 0.45, 0.85),
    color(0.55, 0.55, 0.55)
];
const LEVEL_NAMES = ["ok", "watch", "bad", "critical", "degenerate", "keep", "outside"];
const LEVEL_KEEP = 5;
const LEVEL_OUTSIDE = 6;
/** Faces of this level or worse (and below LEVEL_KEEP) get a NAME property (reason visible on selection), at most NAME_LIMIT of them. */
const NAME_FROM_LEVEL = 3;
const NAME_LIMIT = 40;
/** Aspect (P / 2w) above which a face is degenerate. */
const NEEDLE_ASPECT = 5000;
/** Rows printed to the console. */
const PRINT_ROWS = 25;
/** A face of the copy must match its source face in area to this fraction (the copy is mapped to the source by face order). */
const COPY_AREA_MATCH = 1e-6;

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Clean surfaces" }
export const cleanSurfaces = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Surfaces", "Filter" : EntityType.BODY && BodyType.SHEET && SketchObject.NO && ConstructionObject.NO }
        definition.surfaces is Query;

        annotation { "Name" : "Faces to clean (empty = all)", "Filter" : EntityType.FACE && SketchObject.NO && ConstructionObject.NO }
        definition.cleanFaces is Query;

        annotation { "Name" : "Faces to keep", "Filter" : EntityType.FACE && SketchObject.NO && ConstructionObject.NO }
        definition.keepFaces is Query;

        annotation { "Name" : "Sliver width" }
        isLength(definition.sliverWidth, SliverWidthBounds);

        annotation { "Name" : "Short edge" }
        isLength(definition.shortEdge, ShortEdgeBounds);
    }
    {
        const sources = evaluateQuery(context, qBodyType(definition.surfaces, BodyType.SHEET));
        if (size(sources) == 0)
        {
            throw regenError("Select at least one surface body.", ["surfaces"]);
        }

        opPattern(context, id + "copy", {
                    "entities" : qUnion(sources),
                    "transforms" : [identityTransform()],
                    "instanceNames" : ["Clean_surfaces_diagnose"]
                });
        const copy = qCreatedBy(id + "copy", EntityType.BODY);
        const faces = evaluateQuery(context, qOwnedByBody(copy, EntityType.FACE));

        // Scope sets, keyed by the COPY face (the selection is on the source; the copy has the same faces in the same order).
        const srcFaces = evaluateQuery(context, qOwnedByBody(qUnion(sources), EntityType.FACE));
        if (size(srcFaces) != size(faces))
        {
            throw regenError("The copy of the surfaces has a different number of faces.", ["surfaces"]);
        }
        var srcIndex = {};
        for (var i = 0; i < size(srcFaces); i += 1)
        {
            srcIndex[transientQueriesToStrings(srcFaces[i])] = i;
        }
        const cleanSet = copyKeys(context, definition.cleanFaces, srcIndex, srcFaces, faces);
        const keepSet = copyKeys(context, definition.keepFaces, srcIndex, srcFaces, faces);
        const hasScope = size(evaluateQuery(context, definition.cleanFaces)) > 0;

        const widthCrit = definition.sliverWidth / millimeter;
        const edgeCrit = definition.shortEdge / millimeter;

        // Shortest edge per face: one length per edge, pushed to the faces that use it (only edges below the watch band matter).
        var minEdge = {};
        const edgeWatch = 50 * edgeCrit;
        for (var e in evaluateQuery(context, qOwnedByBody(copy, EntityType.EDGE)))
        {
            const len = evLength(context, { "entities" : e }) / millimeter;
            if (len >= edgeWatch)
            {
                continue;
            }
            for (var f in evaluateQuery(context, qAdjacent(e, AdjacencyType.EDGE, EntityType.FACE)))
            {
                const key = transientQueriesToStrings(f);
                if (minEdge[key] == undefined || len < minEdge[key])
                {
                    minEdge[key] = len;
                }
            }
        }

        var rows = [];
        var byLevel = [[], [], [], [], [], [], []];
        var counts = [0, 0, 0, 0, 0, 0, 0];
        for (var f in faces)
        {
            const key = transientQueriesToStrings(f);
            if (keepSet[key] != undefined)
            {
                counts[LEVEL_KEEP] = counts[LEVEL_KEEP] + 1;
                byLevel[LEVEL_KEEP] = append(byLevel[LEVEL_KEEP], f);
                continue;
            }
            if (hasScope && cleanSet[key] == undefined)
            {
                counts[LEVEL_OUTSIDE] = counts[LEVEL_OUTSIDE] + 1;
                byLevel[LEVEL_OUTSIDE] = append(byLevel[LEVEL_OUTSIDE], f);
                continue;
            }
            const area = evArea(context, { "entities" : f }) / millimeter ^ 2;
            const per = evLength(context, { "entities" : qAdjacent(f, AdjacencyType.EDGE, EntityType.EDGE) }) / millimeter;
            const shortest = minEdge[key] == undefined ? edgeWatch : minEdge[key];
            const width = 2 * area / max(per, 1e-12);
            const aspect = per / max(2 * width, 1e-12);

            var level = 0;
            var reason = "";
            const tests = [
                    { "kind" : "width", "level" : bandLevel(width, widthCrit, 3, 10) },
                    { "kind" : "edge", "level" : bandLevel(shortest, edgeCrit, 10, 50) },
                    { "kind" : "area", "level" : bandLevel(area, widthCrit ^ 2, 10, 100) }
                ];
            for (var t in tests)
            {
                if (t.level > level)
                {
                    level = t.level;
                    reason = t["kind"];
                }
            }
            if (shortest < edgeCrit / 10 || area < widthCrit ^ 2 / 10 || aspect > NEEDLE_ASPECT)
            {
                if (level < 4)
                {
                    reason = aspect > NEEDLE_ASPECT ? "needle" : reason;
                }
                level = 4;
            }
            counts[level] = counts[level] + 1;
            byLevel[level] = append(byLevel[level], f);
            rows = append(rows, { "face" : f, "level" : level, "reason" : reason, "width" : width, "area" : area, "per" : per, "edge" : shortest,
                        "wmax" : undefined, "taper" : undefined, "where" : undefined });
        }

        for (var lv = 0; lv < size(byLevel); lv += 1)
        {
            if (size(byLevel[lv]) > 0)
            {
                setProperty(context, { "entities" : qUnion(byLevel[lv]), "propertyType" : PropertyType.APPEARANCE, "value" : LEVEL_COLORS[lv] });
            }
        }

        rows = sort(rows, function(a, b)
            {
                return a.level == b.level ? a.width - b.width : b.level - a.level;
            });

        // Strip measures (two edge samplings per face) only for the faces that will be named / printed.
        for (var i = 0; i < size(rows); i += 1)
        {
            if (rows[i].level < NAME_FROM_LEVEL || i >= NAME_LIMIT)
            {
                break;
            }
            const m = stripMeasure(context, rows[i].face);
            rows[i].wmax = m.wmax;
            rows[i].taper = m.taper;
            rows[i].where = m.where;
        }

        var named = 0;
        for (var r in rows)
        {
            if (r.level < NAME_FROM_LEVEL || named >= NAME_LIMIT)
            {
                break;
            }
            setProperty(context, { "entities" : r.face, "propertyType" : PropertyType.NAME,
                        "value" : LEVEL_NAMES[r.level] ~ " " ~ r.reason ~ " w=" ~ roundToPrecision(r.width, 3) ~ "mm"
                            ~ (r.wmax == undefined ? "" : " wmax=" ~ roundToPrecision(r.wmax, 3) ~ " taper=" ~ roundToPrecision(r.taper, 2)) });
            named += 1;
        }

        println("COUNTS faces " ~ size(faces) ~ " ok " ~ counts[0] ~ " watch " ~ counts[1] ~ " bad " ~ counts[2]
                ~ " critical " ~ counts[3] ~ " degenerate " ~ counts[4] ~ " keep " ~ counts[LEVEL_KEEP] ~ " outside " ~ counts[LEVEL_OUTSIDE]);
        for (var i = 0; i < min(PRINT_ROWS, size(rows)); i += 1)
        {
            const r = rows[i];
            println(LEVEL_NAMES[r.level] ~ " (" ~ r.reason ~ ") w=" ~ roundToPrecision(r.width, 4) ~ " A=" ~ roundToPrecision(r.area, 4)
                    ~ " P=" ~ roundToPrecision(r.per, 2) ~ " minEdge=" ~ roundToPrecision(r.edge, 4)
                    ~ (r.wmax == undefined ? "" : " wmax=" ~ roundToPrecision(r.wmax, 4) ~ " taper=" ~ roundToPrecision(r.taper, 2) ~ " at " ~ r.where));
        }
        reportFeatureInfo(context, id, size(faces) ~ " faces: ok " ~ counts[0] ~ " | watch " ~ counts[1] ~ " | bad " ~ counts[2]
                ~ " | critical " ~ counts[3] ~ " | degenerate " ~ counts[4] ~ " | keep " ~ counts[LEVEL_KEEP] ~ " | outside " ~ counts[LEVEL_OUTSIDE]
                ~ ". Colours: grey ok, yellow watch, orange bad, red critical, violet degenerate, blue keep, dark grey outside the faces to clean."
                ~ " Original is unchanged; hide it to see the copy.");
    });

/** The faces of `selection` (on the source surfaces) as a map keyed by the matching COPY face; the copy is checked by area. */
function copyKeys(context is Context, selection is Query, srcIndex is map, srcFaces is array, copyFaces is array) returns map
{
    var result = {};
    for (var f in evaluateQuery(context, selection))
    {
        const idx = srcIndex[transientQueriesToStrings(f)];
        if (idx == undefined)
        {
            continue;
        }
        const a = evArea(context, { "entities" : srcFaces[idx] });
        const b = evArea(context, { "entities" : copyFaces[idx] });
        if (abs(a - b) > COPY_AREA_MATCH * a)
        {
            throw regenError("A face of the copy does not match its source face (face order changed).");
        }
        result[transientQueriesToStrings(copyFaces[idx])] = true;
    }
    return result;
}

/**
 * Widest and narrowest distance between the two longest edges of a face (9 paired samples; the pairing direction is the
 * one with the smaller total), the ratio narrowest / widest, and the position of the middle of the face in mm.
 */
function stripMeasure(context is Context, face is Query) returns map
{
    var edges = [];
    for (var e in evaluateQuery(context, qAdjacent(face, AdjacencyType.EDGE, EntityType.EDGE)))
    {
        edges = append(edges, { "e" : e, "len" : evLength(context, { "entities" : e }) });
    }
    edges = sort(edges, function(a, b)
        {
            return b.len - a.len;
        });
    const bb = evBox3d(context, { "topology" : face });
    const c = (bb.minCorner + bb.maxCorner) / 2 / millimeter;
    const where = "(" ~ roundToPrecision(c[0], 1) ~ ", " ~ roundToPrecision(c[1], 1) ~ ", " ~ roundToPrecision(c[2], 1) ~ ")";
    if (size(edges) < 2)
    {
        return { "wmax" : 0, "taper" : 1, "where" : where };
    }
    const params = [0, 0.125, 0.25, 0.375, 0.5, 0.625, 0.75, 0.875, 1];
    const a = evEdgeTangentLines(context, { "edge" : edges[0].e, "parameters" : params });
    const b = evEdgeTangentLines(context, { "edge" : edges[1].e, "parameters" : params });
    var fwd = [];
    var rev = [];
    var fwdSum = 0;
    var revSum = 0;
    for (var m = 0; m < 9; m += 1)
    {
        const d1 = norm(a[m].origin - b[m].origin) / millimeter;
        const d2 = norm(a[m].origin - b[8 - m].origin) / millimeter;
        fwd = append(fwd, d1);
        rev = append(rev, d2);
        fwdSum += d1;
        revSum += d2;
    }
    const d = fwdSum < revSum ? fwd : rev;
    var lo = d[0];
    var hi = d[0];
    for (var x in d)
    {
        lo = min(lo, x);
        hi = max(hi, x);
    }
    return { "wmax" : hi, "taper" : lo / max(hi, 1e-12), "where" : where };
}

/** Level 0..3 of a value against three ascending bands: critical below c, bad below c * bad, watch below c * watch. */
function bandLevel(value is number, critical is number, badFactor is number, watchFactor is number) returns number
{
    if (value < critical)
    {
        return 3;
    }
    if (value < critical * badFactor)
    {
        return 2;
    }
    if (value < critical * watchFactor)
    {
        return 1;
    }
    return 0;
}
