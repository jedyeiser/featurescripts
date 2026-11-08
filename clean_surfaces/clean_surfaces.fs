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

/** Max tangent change: 0.01 deg min, 0.5 deg default, 15 deg max. Two edges either side of a short edge "line up" within this. */
export const MaxTangentBounds =
{
    (degree) : [0.01, 0.5, 15],
    (radian) : 0.0087
} as AngleBoundSpec;

/** What the face colours show. */
export enum CleanColorBy
{
    annotation { "Name" : "Severity" }
    SEVERITY,
    annotation { "Name" : "Offset test" }
    OFFSET,
    annotation { "Name" : "Tight curvature" }
    RADIUS
}

/** Offset test distance: 0.001 mm min, 0.5 mm default, 50 mm max. */
export const OffsetTestBounds =
{
    (meter)      : [1e-6, 5e-4, 0.05],
    (centimeter) : 0.05,
    (millimeter) : 0.5,
    (inch)       : 0.02,
    (foot)       : 0.0016,
    (yard)       : 0.0005
} as LengthBoundSpec;

/** Colours of the Offset test view: passes, fails. */
const OFFSET_PASS_COLOR = color(0.55, 0.80, 0.55);
const OFFSET_FAIL_COLOR = color(0.85, 0.10, 0.65);
/** Colours of the Tight curvature view by tightness 1 + d k (<= 0 inverts, < 0.5 tight, < 0.8 close): ok, close, tight, inverts. */
const RADIUS_COLORS = [color(0.80, 0.80, 0.80), color(1.00, 0.85, 0.20), color(1.00, 0.50, 0.10), color(0.85, 0.10, 0.10)];
/** A face corner sharper than this many degrees (between its two boundary edges leaving a vertex) is a cusp. */
const CUSP_DEGREES = 20;
/** Taper (narrowest / widest) below this means the face comes to a point. */
const POINTED_TAPER = 0.05;
/** UV samples per direction for the curvature test. */
const CURVATURE_GRID = 5;

/** Short edges are listed (and tested for alignment) below this many times the Short edge threshold. */
const SHORT_EDGE_LIST_FACTOR = 10;
/** Rows of short edges printed. */
const EDGE_ROWS = 30;

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

        annotation { "Name" : "Max tangent change" }
        isAngle(definition.maxTangent, MaxTangentBounds);

        annotation { "Name" : "Colour by", "UIHint" : UIHint.SHOW_LABEL }
        definition.colorBy is CleanColorBy;

        if (definition.colorBy == CleanColorBy.OFFSET || definition.colorBy == CleanColorBy.RADIUS)
        {
            annotation { "Name" : "Offset distance" }
            isLength(definition.offsetDistance, OffsetTestBounds);

            annotation { "Name" : "Flip offset side" }
            definition.offsetFlip is boolean;
        }

        annotation { "Name" : "Debug output" }
        definition.debugOutput is boolean;
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
        var shortEdges = [];
        const edgeWatch = 50 * edgeCrit;
        for (var e in evaluateQuery(context, qOwnedByBody(copy, EntityType.EDGE)))
        {
            const len = evLength(context, { "entities" : e }) / millimeter;
            if (len >= edgeWatch)
            {
                continue;
            }
            if (len < SHORT_EDGE_LIST_FACTOR * edgeCrit)
            {
                shortEdges = append(shortEdges, { "edge" : e, "len" : len });
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

        // Colour groups. Keep and outside faces always keep their own colours; the faces in scope are coloured by the chosen view.
        var groups = [
                { "faces" : byLevel[LEVEL_KEEP], "color" : LEVEL_COLORS[LEVEL_KEEP] },
                { "faces" : byLevel[LEVEL_OUTSIDE], "color" : LEVEL_COLORS[LEVEL_OUTSIDE] }
            ];
        var offsetTested = 0;
        var offsetFailed = 0;
        if (definition.colorBy == CleanColorBy.SEVERITY)
        {
            for (var lv = 0; lv < LEVEL_KEEP; lv += 1)
            {
                groups = append(groups, { "faces" : byLevel[lv], "color" : LEVEL_COLORS[lv] });
            }
        }
        else
        {
            const signedDistance = (definition.offsetFlip ? -1 : 1) * definition.offsetDistance;
            var probe = [[], [], [], []];
            for (var i = 0; i < size(rows); i += 1)
            {
                if (definition.colorBy == CleanColorBy.OFFSET)
                {
                    const fails = offsetRefused(context, id + ("off" ~ i), rows[i].face, signedDistance);
                    rows[i].offsetFail = fails;
                    probe[fails ? 1 : 0] = append(probe[fails ? 1 : 0], rows[i].face);
                    offsetTested += 1;
                    offsetFailed += fails ? 1 : 0;
                }
                else
                {
                    const t = tightness(context, rows[i].face, signedDistance);
                    rows[i].tight = t;
                    const bucket = t <= 0 ? 3 : (t < 0.5 ? 2 : (t < 0.8 ? 1 : 0));
                    probe[bucket] = append(probe[bucket], rows[i].face);
                }
            }
            const palette = definition.colorBy == CleanColorBy.OFFSET ? [OFFSET_PASS_COLOR, OFFSET_FAIL_COLOR] : RADIUS_COLORS;
            for (var p = 0; p < size(palette); p += 1)
            {
                groups = append(groups, { "faces" : probe[p], "color" : palette[p] });
            }
        }
        for (var g in groups)
        {
            if (size(g.faces) > 0)
            {
                setProperty(context, { "entities" : qUnion(g.faces), "propertyType" : PropertyType.APPEARANCE, "value" : g.color });
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
            rows[i].cusp = cornerAngle(context, rows[i].face);
            rows[i].kind = issueKind(rows[i]);
        }
        var kinds = { "micro-edge" : 0, "pointed" : 0, "spot" : 0, "strip" : 0 };
        for (var r in rows)
        {
            if (r.kind != undefined)
            {
                kinds[r.kind] = kinds[r.kind] + 1;
            }
        }

        var named = 0;
        for (var r in rows)
        {
            if (r.level < NAME_FROM_LEVEL || named >= NAME_LIMIT)
            {
                break;
            }
            setProperty(context, { "entities" : r.face, "propertyType" : PropertyType.NAME,
                        "value" : LEVEL_NAMES[r.level] ~ " " ~ (r.kind == undefined ? r.reason : r.kind) ~ " w=" ~ roundToPrecision(r.width, 3) ~ "mm"
                            ~ (r.wmax == undefined ? "" : " wmax=" ~ roundToPrecision(r.wmax, 3) ~ " taper=" ~ roundToPrecision(r.taper, 2)
                                ~ (r.cusp == undefined ? "" : " corner=" ~ roundToPrecision(r.cusp, 0) ~ "deg")) });
            named += 1;
        }

        // Short edges: where they are, what they touch, and whether the edges either side line up (mergeable).
        var edgeLines = [];
        shortEdges = sort(shortEdges, function(a, b)
            {
                return a.len - b.len;
            });
        var mergeable = [];
        var corners = [];
        const tangentDeg = definition.maxTangent / degree;
        for (var i = 0; i < size(shortEdges); i += 1)
        {
            const s = shortEdges[i];
            const rep = edgeReport(context, s.edge, keepSet, cleanSet, hasScope);
            const isMergeable = rep.angle != undefined && rep.angle <= tangentDeg;
            if (isMergeable)
            {
                mergeable = append(mergeable, s.edge);
            }
            else
            {
                corners = append(corners, s.edge);
            }
            edgeLines = append(edgeLines, "EDGE " ~ roundToPrecision(s.len, 4) ~ " mm at " ~ rep.where ~ " faces " ~ rep.faces ~ " valence " ~ rep.valence
                    ~ (rep.angle == undefined ? " no through-edge" : " align " ~ roundToPrecision(rep.angle, 2) ~ " deg") ~ (isMergeable ? " MERGEABLE" : " corner"));
        }
        if (size(mergeable) > 0)
        {
            addDebugEntities(context, qUnion(mergeable), DebugColor.GREEN);
        }
        if (size(corners) > 0)
        {
            addDebugEntities(context, qUnion(corners), DebugColor.RED);
        }

        const worstFaces = counts[3] + counts[4];
        const needsCleanup = worstFaces > 0 || size(mergeable) > 0 || offsetFailed > 0;
        const issues = "micro-edge " ~ kinds["micro-edge"] ~ ", pointed " ~ kinds["pointed"] ~ ", spot " ~ kinds["spot"] ~ ", strip " ~ kinds["strip"];

        if (definition.debugOutput)
        {
            println("COUNTS faces " ~ size(faces) ~ " ok " ~ counts[0] ~ " watch " ~ counts[1] ~ " bad " ~ counts[2] ~ " critical " ~ counts[3]
                    ~ " degenerate " ~ counts[4] ~ " keep " ~ counts[LEVEL_KEEP] ~ " outside " ~ counts[LEVEL_OUTSIDE]);
            println("ISSUES (critical+ faces) " ~ issues ~ "; needs cleanup " ~ needsCleanup);
            for (var i = 0; i < min(PRINT_ROWS, size(rows)); i += 1)
            {
                const r = rows[i];
                println(LEVEL_NAMES[r.level] ~ " " ~ (r.kind == undefined ? r.reason : r.kind) ~ " w=" ~ roundToPrecision(r.width, 4) ~ " A=" ~ roundToPrecision(r.area, 3)
                        ~ " minEdge=" ~ roundToPrecision(r.edge, 4)
                        ~ (r.wmax == undefined ? "" : " wmax=" ~ roundToPrecision(r.wmax, 4) ~ " taper=" ~ roundToPrecision(r.taper, 2) ~ " corner=" ~ roundToPrecision(r.cusp, 0) ~ " at " ~ r.where)
                        ~ (r.offsetFail == undefined ? "" : (r.offsetFail ? " OFFSET-FAIL" : " offset-ok"))
                        ~ (r.tight == undefined ? "" : " tight=" ~ roundToPrecision(r.tight, 2)));
            }
            for (var i = 0; i < min(EDGE_ROWS, size(edgeLines)); i += 1)
            {
                println(edgeLines[i]);
            }
            println("SHORTEDGES " ~ size(shortEdges) ~ " mergeable " ~ size(mergeable) ~ " corner " ~ size(corners));
            if (definition.colorBy == CleanColorBy.OFFSET)
            {
                // faces the kernel refuses to offset that the severity score does not call critical: what are they?
                for (var r in rows)
                {
                    if (r.offsetFail == true && r.level < NAME_FROM_LEVEL)
                    {
                        const m = stripMeasure(context, r.face);
                        println("UNFLAGGED-FAIL " ~ LEVEL_NAMES[r.level] ~ " " ~ faceType(context, r.face) ~ " A=" ~ roundToPrecision(r.area, 2) ~ " w=" ~ roundToPrecision(r.width, 3)
                                ~ " minEdge=" ~ roundToPrecision(r.edge, 3) ~ " edges=" ~ size(evaluateQuery(context, qAdjacent(r.face, AdjacencyType.EDGE, EntityType.EDGE)))
                                ~ " naked=" ~ size(evaluateQuery(context, qEdgeTopologyFilter(qAdjacent(r.face, AdjacencyType.EDGE, EntityType.EDGE), EdgeTopology.ONE_SIDED)))
                                ~ " corner=" ~ roundToPrecision(cornerAngle(context, r.face), 0) ~ " taper=" ~ roundToPrecision(m.taper, 2) ~ " at " ~ m.where);
                    }
                }
                println("OFFSET tested " ~ offsetTested ~ " failed " ~ offsetFailed);
                // does the curvature number predict the failures? (sign check: which side is "positive")
                for (var sgn in [1, -1])
                {
                    var tab = [0, 0, 0, 0];
                    for (var r in rows)
                    {
                        const tt = tightness(context, r.face, sgn * definition.offsetDistance);
                        const idx = (r.offsetFail ? 0 : 2) + (tt <= 0.5 ? 0 : 1);
                        tab[idx] = tab[idx] + 1;
                    }
                    println("AGREE sign " ~ sgn ~ ": fail&tight " ~ tab[0] ~ " fail&ok " ~ tab[1] ~ " pass&tight " ~ tab[2] ~ " pass&ok " ~ tab[3]);
                }
            }
        }

        reportFeatureInfo(context, id, (needsCleanup ? "Needs cleanup: " : "Looks clean: ") ~ worstFaces ~ " critical+ faces (" ~ issues ~ "), "
                ~ size(mergeable) ~ " mergeable short edges"
                ~ (definition.colorBy == CleanColorBy.OFFSET ? ", offset test " ~ offsetFailed ~ " of " ~ offsetTested ~ " faces refuse" : "")
                ~ ". " ~ size(shortEdges) ~ " short edges (" ~ size(mergeable) ~ " line up within " ~ roundToPrecision(tangentDeg, 2)
                ~ " deg: green; " ~ size(corners) ~ " corners: red). " ~ size(faces) ~ " faces: ok " ~ counts[0] ~ " | watch " ~ counts[1] ~ " | bad " ~ counts[2]
                ~ " | critical " ~ counts[3] ~ " | degenerate " ~ counts[4] ~ " | keep " ~ counts[LEVEL_KEEP] ~ " | outside " ~ counts[LEVEL_OUTSIDE]
                ~ ". Colours: grey ok, yellow watch, orange bad, red critical, violet degenerate, blue keep, dark grey outside the faces to clean."
                ~ " Original is unchanged; hide it to see the copy.");
    });

/**
 * Report on one short edge: its middle (mm), the classes of the faces it touches (K keep, O outside, C clean), the number of
 * edges at each end vertex, and the best alignment angle (deg) of an edge leaving one end with an edge leaving the other
 * (0 = they continue through the short edge as one smooth curve; undefined when an end has no other edge).
 */
function edgeReport(context is Context, edge is Query, keepSet is map, cleanSet is map, hasScope is boolean) returns map
{
    const mid = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 0.5 }).origin / millimeter;
    var classes = [];
    for (var f in evaluateQuery(context, qAdjacent(edge, AdjacencyType.EDGE, EntityType.FACE)))
    {
        const key = transientQueriesToStrings(f);
        classes = append(classes, keepSet[key] != undefined ? "K" : (hasScope && cleanSet[key] == undefined ? "O" : "C"));
    }
    const verts = evaluateQuery(context, qAdjacent(edge, AdjacencyType.VERTEX, EntityType.VERTEX));
    var result = {
        "where" : "(" ~ roundToPrecision(mid[0], 1) ~ ", " ~ roundToPrecision(mid[1], 1) ~ ", " ~ roundToPrecision(mid[2], 1) ~ ")",
        "faces" : joinArray(classes, "/"),
        "valence" : "?",
        "angle" : undefined
    };
    if (size(verts) != 2)
    {
        return result;
    }
    var away = [[], []];
    for (var v = 0; v < 2; v += 1)
    {
        const p = evVertexPoint(context, { "vertex" : verts[v] });
        for (var o in evaluateQuery(context, qSubtraction(qAdjacent(verts[v], AdjacencyType.VERTEX, EntityType.EDGE), edge)))
        {
            const t0 = evEdgeTangentLine(context, { "edge" : o, "parameter" : 0 });
            const t1 = evEdgeTangentLine(context, { "edge" : o, "parameter" : 1 });
            away[v] = append(away[v], norm(t0.origin - p) <= norm(t1.origin - p) ? t0.direction : -t1.direction);
        }
    }
    result["valence"] = (size(away[0]) + 1) ~ "/" ~ (size(away[1]) + 1);
    var best = -2;
    for (var a in away[0])
    {
        for (var b in away[1])
        {
            best = max(best, dot(a, -b));
        }
    }
    if (best > -2)
    {
        result["angle"] = acos(clamp(best, -1, 1)) / degree;
    }
    return result;
}

/**
 * True when the kernel refuses to offset this one face by `distance` (the offset is made on its own and deleted again).
 * `try silent` is used here deliberately: the op failing IS the measurement; nothing else is hidden by it.
 */
function offsetRefused(context is Context, probeId is Id, face is Query, distance is ValueWithUnits) returns boolean
{
    var ok = false;
    try silent
    {
        opExtractSurface(context, probeId, { "faces" : face, "tangentPropagation" : false, "offset" : distance });
        ok = true;
    }
    if (ok)
    {
        opDeleteBodies(context, probeId + "del", { "entities" : qCreatedBy(probeId, EntityType.BODY) });
    }
    return !ok;
}

/**
 * How close a face is to inverting when offset by `distance`: the smallest 1 + distance * k over a CURVATURE_GRID square of
 * points on the face and both principal curvatures k (<= 0 means the offset surface turns inside out or pinches; 1 = flat).
 * The sign convention (which side is positive) is checked against the kernel's offset test in the debug output.
 */
function tightness(context is Context, face is Query, distance is ValueWithUnits) returns number
{
    var params = [];
    for (var i = 0; i < CURVATURE_GRID; i += 1)
    {
        for (var j = 0; j < CURVATURE_GRID; j += 1)
        {
            params = append(params, vector((i + 0.5) / CURVATURE_GRID, (j + 0.5) / CURVATURE_GRID));
        }
    }
    const planes = evFaceTangentPlanes(context, { "face" : face, "parameters" : params, "returnUndefinedOutsideFace" : true });
    var inside = [];
    for (var i = 0; i < size(params); i += 1)
    {
        if (planes[i] != undefined)
        {
            inside = append(inside, params[i]);
        }
    }
    var worst = 10;
    if (size(inside) == 0)
    {
        return worst;
    }
    for (var c in evFaceCurvatures(context, { "face" : face, "parameters" : inside }))
    {
        worst = min(worst, min(1 + distance * c.minCurvature, 1 + distance * c.maxCurvature));
    }
    return worst;
}

/** Surface type of a face as text (PLANE, CYLINDER, ..., OTHER). */
function faceType(context is Context, face is Query) returns string
{
    return "" ~ evSurfaceDefinition(context, { "face" : face, "returnBSplinesAsOther" : true }).surfaceType;
}

/** Direction of an edge leaving a vertex (unit vector). */
function awayDirection(context is Context, edge is Query, vertex is Query) returns Vector
{
    const p = evVertexPoint(context, { "vertex" : vertex });
    const t0 = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 0 });
    const t1 = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 1 });
    return norm(t0.origin - p) <= norm(t1.origin - p) ? t0.direction : -t1.direction;
}

/** Sharpest corner of a face in degrees: the angle between its two boundary edges leaving a vertex (180 when it has no such vertex). */
function cornerAngle(context is Context, face is Query) returns number
{
    var sharpest = 180;
    const boundary = qAdjacent(face, AdjacencyType.EDGE, EntityType.EDGE);
    for (var v in evaluateQuery(context, qAdjacent(face, AdjacencyType.VERTEX, EntityType.VERTEX)))
    {
        const here = evaluateQuery(context, qIntersection([qAdjacent(v, AdjacencyType.VERTEX, EntityType.EDGE), boundary]));
        if (size(here) == 2)
        {
            const a = awayDirection(context, here[0], v);
            const b = awayDirection(context, here[1], v);
            sharpest = min(sharpest, acos(clamp(dot(a, b), -1, 1)) / degree);
        }
    }
    return sharpest;
}

/** What kind of defect a critical face is: micro-edge (flagged by a tiny edge), pointed (tapers to a point / cusp corner), spot (as long as wide), else strip. */
function issueKind(r is map) returns string
{
    if (r.reason == "edge")
    {
        return "micro-edge";
    }
    if (r.taper < POINTED_TAPER || r.cusp < CUSP_DEGREES)
    {
        return "pointed";
    }
    if (r.per / max(2 * r.width, 1e-12) < 6)
    {
        return "spot";
    }
    return "strip";
}

/** Strings of an array joined with a separator. */
function joinArray(parts is array, separator is string) returns string
{
    var s = "";
    for (var i = 0; i < size(parts); i += 1)
    {
        s = s ~ (i == 0 ? "" : separator) ~ parts[i];
    }
    return s;
}

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
