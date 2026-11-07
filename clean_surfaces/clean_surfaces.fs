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
 * Severity of a face = the worst of three tests, each with a watch / bad / critical / degenerate band:
 *   mean width 2A/P, shortest edge, area.
 * The bands are multiples of the two dialog thresholds (sliver width W, short edge E):
 *   width  : critical < W,  bad < 3 W,  watch < 10 W
 *   edge   : critical < E,  bad < 10 E, watch < 50 E
 *   area   : critical < W^2, bad < 10 W^2, watch < 100 W^2
 *   degenerate : edge or area below a tenth of critical, or needle aspect (P / 2w) above 5000
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

/** Face colours by severity level 0..4: ok, watch, bad, critical, degenerate. */
const LEVEL_COLORS = [
    color(0.80, 0.80, 0.80),
    color(1.00, 0.85, 0.20),
    color(1.00, 0.50, 0.10),
    color(0.85, 0.10, 0.10),
    color(0.35, 0.00, 0.45)
];
const LEVEL_NAMES = ["ok", "watch", "bad", "critical", "degenerate"];
/** Faces of this level or worse get a NAME property (reason visible on selection), at most NAME_LIMIT of them. */
const NAME_FROM_LEVEL = 3;
const NAME_LIMIT = 40;
/** Aspect (P / 2w) above which a face is degenerate. */
const NEEDLE_ASPECT = 5000;
/** Rows printed to the console. */
const PRINT_ROWS = 25;

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Clean surfaces" }
export const cleanSurfaces = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Surfaces", "Filter" : EntityType.BODY && BodyType.SHEET && SketchObject.NO && ConstructionObject.NO }
        definition.surfaces is Query;

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

        const widthCrit = definition.sliverWidth / millimeter;
        const edgeCrit = definition.shortEdge / millimeter;
        const faces = evaluateQuery(context, qOwnedByBody(copy, EntityType.FACE));

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
        var byLevel = [[], [], [], [], []];
        var counts = [0, 0, 0, 0, 0];
        for (var f in faces)
        {
            const area = evArea(context, { "entities" : f }) / millimeter ^ 2;
            const per = evLength(context, { "entities" : qAdjacent(f, AdjacencyType.EDGE, EntityType.EDGE) }) / millimeter;
            const key = transientQueriesToStrings(f);
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
            rows = append(rows, { "face" : f, "level" : level, "reason" : reason, "width" : width, "area" : area, "per" : per, "edge" : shortest });
        }

        for (var lv = 0; lv < 5; lv += 1)
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
        var named = 0;
        for (var r in rows)
        {
            if (r.level < NAME_FROM_LEVEL || named >= NAME_LIMIT)
            {
                break;
            }
            setProperty(context, { "entities" : r.face, "propertyType" : PropertyType.NAME,
                        "value" : LEVEL_NAMES[r.level] ~ " " ~ r.reason ~ " w=" ~ roundToPrecision(r.width, 3) ~ "mm" });
            named += 1;
        }

        println("COUNTS faces " ~ size(faces) ~ " ok " ~ counts[0] ~ " watch " ~ counts[1] ~ " bad " ~ counts[2]
                ~ " critical " ~ counts[3] ~ " degenerate " ~ counts[4]);
        for (var i = 0; i < min(PRINT_ROWS, size(rows)); i += 1)
        {
            const r = rows[i];
            println(LEVEL_NAMES[r.level] ~ " (" ~ r.reason ~ ") w=" ~ roundToPrecision(r.width, 4) ~ " A=" ~ roundToPrecision(r.area, 4)
                    ~ " P=" ~ roundToPrecision(r.per, 2) ~ " minEdge=" ~ roundToPrecision(r.edge, 4));
        }
        reportFeatureInfo(context, id, size(faces) ~ " faces: ok " ~ counts[0] ~ " | watch " ~ counts[1] ~ " | bad " ~ counts[2]
                ~ " | critical " ~ counts[3] ~ " | degenerate " ~ counts[4]
                ~ ". Colours: grey ok, yellow watch, orange bad, red critical, violet degenerate. Original is unchanged; hide it to see the copy.");
    });

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
