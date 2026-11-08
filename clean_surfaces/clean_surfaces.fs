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
    RADIUS,
    annotation { "Name" : "No face colour" }
    NONE
}

/** What the feature does. Quick = resurface the faces that refuse an offset; Thorough (not built yet) also joins broken loops, splits creased faces and sews with explicit matches. */
export enum CleanMode
{
    annotation { "Name" : "Diagnose" }
    DIAGNOSE,
    annotation { "Name" : "Clean: quick" }
    QUICK,
    annotation { "Name" : "Clean: thorough" }
    THOROUGH
}

/** Which side(s) of the surface the offset test runs on. Query = toward (or away from) a reference, read per body as Offset+ does. */
export enum OffsetSide
{
    annotation { "Name" : "Both sides" }
    BOTH,
    annotation { "Name" : "Toward a reference" }
    QUERY
}

/** Max change: 0.001 mm min, 0.05 mm default, 10 mm max. No resurfaced face may stray further than this from the original. */
export const MaxChangeBounds =
{
    (meter)      : [1e-6, 5e-5, 0.01],
    (centimeter) : 0.005,
    (millimeter) : 0.05,
    (inch)       : 0.002,
    (foot)       : 0.00016,
    (yard)       : 0.00005
} as LengthBoundSpec;

/** Fit tolerance (plain number, metres) of the rebuilt patches: tight so that a patch edge lands inside the 1 micron sewing tolerance. */
const PATCH_TOLERANCE = 1e-7;
/** An edge or vertex tolerance above this (metres) is "tolerant": the kernel had to loosen it to make the geometry fit (0.5 micron). */
const TOLERANT_TOLERANCE = 5e-7;
/** Offset distance used to prove that offsets can run at all (the editing-logic self check). */
const SELF_CHECK_OFFSET = 1e-5 * meter;

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
const CURVATURE_GRID = 12;

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

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Clean surfaces", "Editing Logic Function" : "cleanSurfacesEditLogic" }
export const cleanSurfaces = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Mode", "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.REMEMBER_PREVIOUS_VALUE] }
        definition.mode is CleanMode;

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

        if (definition.mode == CleanMode.DIAGNOSE)
        {
            annotation { "Name" : "Colour by", "UIHint" : UIHint.SHOW_LABEL }
            definition.colorBy is CleanColorBy;

            annotation { "Name" : "Mark short and tolerant edges", "Default" : true, "Description" : "Draw thin curves on the copy: green = short edge that lines up (mergeable), red = short edge at a corner, cyan = tolerant edge (tolerance above 0.5 um)." }
            definition.markEdges is boolean;
        }

        if (!(definition.mode == CleanMode.DIAGNOSE) || definition.colorBy == CleanColorBy.OFFSET || definition.colorBy == CleanColorBy.RADIUS)
        {
            annotation { "Name" : "Offset distance" }
            isLength(definition.offsetDistance, OffsetTestBounds);

            annotation { "Name" : "Offset side", "UIHint" : UIHint.SHOW_LABEL }
            definition.offsetSide is OffsetSide;

            if (definition.offsetSide == OffsetSide.QUERY)
            {
                annotation { "Name" : "Side reference", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                            "Description" : "Geometry on the side to test the offset toward (as in Offset+). The side is read per surface body." }
                definition.sideReference is Query;

                annotation { "Name" : "Offset toward reference", "Default" : true, "UIHint" : UIHint.OPPOSITE_DIRECTION,
                            "Description" : "On: test toward the reference. Off: away from it." }
                definition.towardReference is boolean;
            }

            annotation { "Name" : "Neighbours trim the offset", "Description" : "Off (default) offsets each face on its own, as Offset+ does. On is the kernel's default, where the extensions of the surrounding faces trim the offset: far more faces refuse, partly because of their neighbours." }
            definition.offsetTrim is boolean;

        }

        if (!(definition.mode == CleanMode.DIAGNOSE))
        {
            annotation { "Name" : "Max change" }
            isLength(definition.maxChange, MaxChangeBounds);

            annotation { "Name" : "Verify result", "Description" : "After cleaning, run the offset test on every face of the result and report how many still refuse. Slow." }
            definition.verifyResult is boolean;
        }

        annotation { "Name" : "Use cached analysis" }
        definition.useCache is boolean;

        if (definition.useCache)
        {
            annotation { "Name" : "Resolve", "Description" : "Run the offset analysis now and store which faces refuse the offset. Needed again after the surfaces, the distance, the side or the face selections change." }
            isButton(definition.resolve);

            annotation { "Name" : "Cached faces", "Filter" : EntityType.FACE, "UIHint" : UIHint.ALWAYS_HIDDEN }
            definition.cachedFaces is Query;

            annotation { "Name" : "Cache key", "Default" : "", "UIHint" : UIHint.ALWAYS_HIDDEN }
            definition.cacheKey is string;

            annotation { "Name" : "Cache status", "Default" : "Not resolved yet.", "UIHint" : UIHint.READ_ONLY }
            definition.cacheStatus is string;
        }

        annotation { "Name" : "Debug output" }
        definition.debugOutput is boolean;
    }
    {
        if (definition.mode == CleanMode.DIAGNOSE)
        {
            diagnose(context, id, definition);
        }
        else
        {
            resurface(context, id, definition);
        }
    });

/** Diagnose: copy the surfaces and colour the copy (severity, offset test or tight curvature). */
function diagnose(context is Context, id is Id, definition is map)
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
        definition.offsetSigns = offsetSignsFor(context, copy, definition);
        const cachedFails = cachedFailing(context, definition, srcIndex, srcFaces, faces);

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

        // Tolerant edges: the kernel loosened their tolerance (sewn or imported geometry); a likely cause of offset refusals.
        var tolerantEdges = [];
        var maxTolerantEdge = 0;
        for (var q, v in evTolerances(context, { "entities" : qOwnedByBody(copy, EntityType.EDGE) }))
        {
            maxTolerantEdge = max(maxTolerantEdge, v / meter);
            if (v / meter > TOLERANT_TOLERANCE)
            {
                tolerantEdges = append(tolerantEdges, q);
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
        else if (definition.colorBy == CleanColorBy.OFFSET || definition.colorBy == CleanColorBy.RADIUS)
        {
            var probe = [[], [], [], []];
            for (var i = 0; i < size(rows); i += 1)
            {
                if (definition.colorBy == CleanColorBy.OFFSET)
                {
                    const fails = cachedFails != undefined ? cachedFails[transientQueriesToStrings(rows[i].face)] != undefined : offsetFails(context, id + ("off" ~ i), rows[i].face, definition);
                    rows[i].offsetFail = fails;
                    probe[fails ? 1 : 0] = append(probe[fails ? 1 : 0], rows[i].face);
                    offsetTested += 1;
                    offsetFailed += fails ? 1 : 0;
                }
                else
                {
                    const t = tightnessFor(context, rows[i].face, definition);
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
        if (definition.colorBy != CleanColorBy.NONE)
        {
            for (var g in groups)
            {
                if (size(g.faces) > 0)
                {
                    setProperty(context, { "entities" : qUnion(g.faces), "propertyType" : PropertyType.APPEARANCE, "value" : g.color });
                }
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
        if (size(tolerantEdges) > 0)
        {
            addDebugEntities(context, qUnion(tolerantEdges), DebugColor.CYAN);
        }
        if (definition.markEdges == true)
        {
            markEdges(context, id + "markA", mergeable, color(0.20, 0.75, 0.30), "Short edges that line up");
            markEdges(context, id + "markB", corners, color(0.85, 0.10, 0.10), "Short edges at corners");
            markEdges(context, id + "markC", tolerantEdges, color(0.00, 0.75, 0.85), "Tolerant edges");
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
            println("TOLERANT edges " ~ size(tolerantEdges) ~ ", largest " ~ roundToPrecision(maxTolerantEdge * 1e6, 3) ~ " um");
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
                // control-net clustering vs the offset result: do the faces that refuse have crowded knots / control points?
                var netFail = [];
                var netPass = [];
                for (var r in rows)
                {
                    const nm = netClustering(context, r.face);
                    if (r.offsetFail)
                    {
                        netFail = append(netFail, nm);
                        println("NET fail " ~ faceType(context, r.face) ~ " A=" ~ roundToPrecision(r.area, 2) ~ " cps " ~ nm.nu ~ "x" ~ nm.nv ~ " knotRatio " ~ roundToPrecision(nm.knotRatio, 3)
                                ~ " cpRatio " ~ roundToPrecision(nm.cpRatio, 3) ~ " minGap " ~ roundToPrecision(nm.minGap, 5) ~ "mm");
                    }
                    else
                    {
                        netPass = append(netPass, nm);
                    }
                }
                for (var grp in [["fail", netFail], ["pass", netPass]])
                {
                    var lowKnot = 0;
                    var lowCp = 0;
                    var lowGap = 0;
                    var ratios = [];
                    for (var nm in grp[1])
                    {
                        lowKnot += nm.knotRatio < 0.05 ? 1 : 0;
                        lowCp += nm.cpRatio < 0.05 ? 1 : 0;
                        lowGap += nm.minGap < 0.05 ? 1 : 0;
                        ratios = append(ratios, nm.cpRatio);
                    }
                    ratios = sort(ratios, function(a, b)
                        {
                            return a - b;
                        });
                    println("NETSUM " ~ grp[0] ~ " n=" ~ size(grp[1]) ~ " knotRatio<0.05: " ~ lowKnot ~ " cpRatio<0.05: " ~ lowCp ~ " minGap<0.05mm: " ~ lowGap
                            ~ (size(ratios) > 0 ? " cpRatio median " ~ roundToPrecision(ratios[floor(size(ratios) / 2)], 3) ~ " min " ~ roundToPrecision(ratios[0], 4) : ""));
                }
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
                ~ size(mergeable) ~ " mergeable short edges, " ~ size(tolerantEdges) ~ " tolerant edges (cyan; tolerance above 0.5 um, largest "
                ~ roundToPrecision(maxTolerantEdge * 1e6, 2) ~ " um)"
                ~ (definition.colorBy == CleanColorBy.OFFSET ? ", offset test " ~ offsetFailed ~ " of " ~ offsetTested ~ " faces refuse" : "")
                ~ ". " ~ size(shortEdges) ~ " short edges (" ~ size(mergeable) ~ " line up within " ~ roundToPrecision(tangentDeg, 2)
                ~ " deg: green; " ~ size(corners) ~ " corners: red). " ~ size(faces) ~ " faces: ok " ~ counts[0] ~ " | watch " ~ counts[1] ~ " | bad " ~ counts[2]
                ~ " | critical " ~ counts[3] ~ " | degenerate " ~ counts[4] ~ " | keep " ~ counts[LEVEL_KEEP] ~ " | outside " ~ counts[LEVEL_OUTSIDE]
                ~ ". Colours: grey ok, yellow watch, orange bad, red critical, violet degenerate, blue keep, dark grey outside the faces to clean."
                ~ " Original is unchanged; hide it to see the copy.");
}

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
 * True when the kernel refuses to offset this one face by `distance` (trim = whether the surrounding faces' extensions trim the offset; Offset+ uses false) (the offset is made on its own and deleted again).
 * `try silent` is used here deliberately: the op failing IS the measurement; nothing else is hidden by it.
 */
function offsetRefused(context is Context, probeId is Id, face is Query, distance is ValueWithUnits, trim is boolean) returns boolean
{
    var ok = false;
    try silent
    {
        opExtractSurface(context, probeId, { "faces" : face, "tangentPropagation" : false, "offset" : distance, "useFacesAroundToTrimOffset" : trim });
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

/**
 * How crowded a face's B-spline control net is: knotRatio = smallest / largest knot span over both directions, cpRatio = smallest /
 * median distance between neighbouring control points, minGap = that smallest distance in mm, nu x nv = control points.
 * A low ratio means control points or knots bunch up (a surface that is hard to offset or fit). Uses evApproximateBSplineSurface.
 */
function netClustering(context is Context, face is Query) returns map
{
    const surf = evApproximateBSplineSurface(context, { "face" : face }).bSplineSurface;
    const cps = surf.controlPoints;
    const nu = size(cps);
    const nv = size(cps[0]);
    var gaps = [];
    for (var i = 0; i < nu; i += 1)
    {
        for (var j = 0; j < nv; j += 1)
        {
            if (i + 1 < nu)
            {
                gaps = append(gaps, norm(cps[i + 1][j] - cps[i][j]) / millimeter);
            }
            if (j + 1 < nv)
            {
                gaps = append(gaps, norm(cps[i][j + 1] - cps[i][j]) / millimeter);
            }
        }
    }
    gaps = sort(gaps, function(a, b)
        {
            return a - b;
        });
    var spans = [];
    for (var knots in [surf.uKnots, surf.vKnots])
    {
        for (var k = 0; k + 1 < size(knots); k += 1)
        {
            const d = knots[k + 1] - knots[k];
            if (d > 1e-12)
            {
                spans = append(spans, d);
            }
        }
    }
    spans = sort(spans, function(a, b)
        {
            return a - b;
        });
    const median = size(gaps) == 0 ? 1 : gaps[floor(size(gaps) / 2)];
    return { "nu" : nu, "nv" : nv, "minGap" : size(gaps) == 0 ? 0 : gaps[0],
            "cpRatio" : size(gaps) == 0 ? 1 : gaps[0] / max(median, 1e-12),
            "knotRatio" : size(spans) == 0 ? 1 : spans[0] / spans[size(spans) - 1] };
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
/**
 * Clean: quick. Resurface every face that refuses the offset: rebuild it as one B-spline patch from its approximated net (clean outer
 * loop, no micro-edges), prove the patch offsets and stays within Max change, then swap it into the copy of the surface: backup the old face,
 * void it, sew the patch in; if the patch will not sew, sew the backup back, so a failed face is never left as a hole.
 * The failures the kernel is expected to give (a loop that does not close, a surface it cannot build, a sew it refuses) are read with
 * try/catch so they can be reported per face; that is the one place this feature catches errors.
 */
function resurface(context is Context, id is Id, definition is map)
{
    const sources = evaluateQuery(context, qBodyType(definition.surfaces, BodyType.SHEET));
    if (size(sources) == 0)
    {
        throw regenError("Select at least one surface body.", ["surfaces"]);
    }
    opPattern(context, id + "copy", {
                "entities" : qUnion(sources),
                "transforms" : [identityTransform()],
                "instanceNames" : ["Clean_surfaces_result"]
            });
    const copyId = id + "copy";
    const body = qCreatedBy(copyId, EntityType.BODY);
    const faces = evaluateQuery(context, qOwnedByBody(body, EntityType.FACE));
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
    definition.offsetSigns = offsetSignsFor(context, body, definition);
    const before = bodyStats(context, body);
    const cachedFails = cachedFailing(context, definition, srcIndex, srcFaces, faces);
    const stale = definition.useCache == true && cachedFails == undefined;

    // 1. the faces to resurface: those that refuse the offset (tested here, or taken from the cache)
    var targets = [];
    var tested = 0;
    for (var i = 0; i < size(faces); i += 1)
    {
        const key = transientQueriesToStrings(faces[i]);
        if (keepSet[key] != undefined || (hasScope && cleanSet[key] == undefined))
        {
            continue;
        }
        tested += 1;
        const refused = cachedFails != undefined ? cachedFails[key] != undefined : offsetFails(context, id + ("off" ~ i), faces[i], definition);
        if (refused)
        {
            targets = append(targets, faces[i]);
        }
    }

    // 2. a patch for each, proved before anything is cut
    var jobs = [];
    var skipped = [];
    for (var t = 0; t < size(targets); t += 1)
    {
        const r = buildPatch(context, id, t, targets[t], definition);
        if (r.ok)
        {
            jobs = append(jobs, r);
        }
        else
        {
            skipped = append(skipped, r);
        }
    }

    // 3. swap each patch in, one at a time (a single union of all of them is refused by the kernel)
    var sewnAnchors = [];
    var keptOriginal = 0;
    var holes = 0;
    for (var job in jobs)
    {
        const cur = qBodyType(qCreatedBy(copyId, EntityType.BODY), BodyType.SHEET);
        const found = evaluateQuery(context, qContainsPoint(qOwnedByBody(cur, EntityType.FACE), job.anchor));
        if (size(found) != 1)
        {
            skipped = append(skipped, { "where" : job.where, "anchor" : job.anchor, "reason" : "face not found again" });
            deleteBodies(context, id + ("gone" ~ job.t), job.patch);
            continue;
        }
        const trial = trialSwap(context, id, job, cur, definition);
        if (definition.debugOutput && trial.tolAfter != undefined)
        {
            println("TRIAL at " ~ job.where ~ " " ~ (trial.ok ? "accepted" : "rejected") ~ ", max edge/vertex tolerance " ~ roundToPrecision(trial.tolBefore * 1e6, 2) ~ " -> "
                    ~ roundToPrecision(trial.tolAfter * 1e6, 2) ~ " um");
        }
        if (!trial.ok)
        {
            skipped = append(skipped, { "where" : job.where, "anchor" : job.anchor, "reason" : "trial swap rejected: " ~ trial.reason });
            deleteBodies(context, id + ("trialdel" ~ job.t), job.patch);
            continue;
        }
        const sewId = id + ("sew" ~ job.t);
        opExtractSurface(context, sewId + "bak", { "faces" : found[0], "tangentPropagation" : false });
        const backup = qCreatedBy(sewId + "bak", EntityType.BODY);
        var voided = false;
        var reason = "";
        try
        {
            opDeleteFace(context, sewId + "void", { "deleteFaces" : found[0], "includeFillet" : false, "capVoid" : false, "leaveOpen" : true });
            voided = true;
        }
        catch (e)
        {
            reason = "could not remove the old face: " ~ e.message;
        }
        if (!voided)
        {
            skipped = append(skipped, { "where" : job.where, "anchor" : job.anchor, "reason" : reason });
            deleteBodies(context, sewId + "d1", job.patch);
            deleteBodies(context, sewId + "d2", backup);
            continue;
        }
        var joined = false;
        try
        {
            opBoolean(context, sewId + "join", { "tools" : qUnion([cur, job.patch]), "operationType" : BooleanOperationType.UNION });
            joined = true;
        }
        catch (e)
        {
            reason = "patch would not sew: " ~ e.message;
        }
        if (joined)
        {
            sewnAnchors = append(sewnAnchors, job.anchor);
            deleteBodies(context, sewId + "d2", backup);
        }
        else
        {
            var restored = false;
            try
            {
                opBoolean(context, sewId + "back", { "tools" : qUnion([cur, backup]), "operationType" : BooleanOperationType.UNION });
                restored = true;
            }
            catch (e)
            {
                reason = reason ~ "; the original face could not be put back";
            }
            keptOriginal += restored ? 1 : 0;
            holes += restored ? 0 : 1;
            skipped = append(skipped, { "where" : job.where, "anchor" : job.anchor, "reason" : reason ~ (restored ? " (original face kept)" : "") });
            deleteBodies(context, sewId + "d1", job.patch);
        }
    }

    // 4. colour what happened and report
    const result = qBodyType(qCreatedBy(copyId, EntityType.BODY), BodyType.SHEET);
    var skippedAnchors = [];
    var reasons = {};
    for (var s in skipped)
    {
        if (s.anchor != undefined)
        {
            skippedAnchors = append(skippedAnchors, s.anchor);
        }
        const key = s.reason;
        reasons[key] = reasons[key] == undefined ? 1 : reasons[key] + 1;
    }
    colorFacesAt(context, result, sewnAnchors, color(0.55, 0.85, 0.55));
    colorFacesAt(context, result, skippedAnchors, color(1.00, 0.50, 0.10));
    const after = bodyStats(context, result);

    var refusing = 0;
    var checked = 0;
    if (definition.verifyResult == true || definition.debugOutput)
    {
        definition.offsetSigns = offsetSignsFor(context, result, definition);
        for (var f in evaluateQuery(context, qOwnedByBody(result, EntityType.FACE)))
        {
            checked += 1;
            if (offsetFails(context, id + ("ver" ~ checked), f, definition))
            {
                refusing += 1;
                if (definition.debugOutput)
                {
                    const sm = stripMeasure(context, f);
                    println("STILL-REFUSES area " ~ roundToPrecision(evArea(context, { "entities" : f }) / millimeter ^ 2, 2) ~ " " ~ faceType(context, f) ~ " at " ~ sm.where
                            ~ " edges " ~ size(evaluateQuery(context, qAdjacent(f, AdjacencyType.EDGE, EntityType.EDGE))));
                }
            }
        }
    }
    if (definition.debugOutput)
    {
        println("VERIFY faces of the result that still refuse the offset: " ~ refusing ~ " of " ~ checked);
        println("CLEAN tested " ~ tested ~ " faces, " ~ size(targets) ~ " refuse the offset (" ~ (cachedFails != undefined ? "cached" : "live") ~ "), patches built " ~ size(jobs)
                ~ ", swapped in " ~ size(sewnAnchors) ~ ", original kept " ~ keptOriginal ~ ", holes " ~ holes);
        println("CLEAN faces " ~ before.faces ~ " -> " ~ after.faces ~ ", edges " ~ before.edges ~ " -> " ~ after.edges ~ ", naked " ~ before.naked ~ " -> " ~ after.naked);
        for (var s in skipped)
        {
            println("SKIPPED at " ~ s.where ~ ": " ~ s.reason);
        }
        for (var job in jobs)
        {
            println("PATCH at " ~ job.where ~ " deviation " ~ roundToPrecision(job.dev / millimeter, 5) ~ " mm");
        }
    }

    var defSource = definition;
    defSource.offsetSigns = offsetSignsFor(context, qUnion(sources), definition);
    var defResult = definition;
    defResult.offsetSigns = offsetSignsFor(context, result, definition);
    const autoBefore = autoOffsetReport(context, id + "autoB", qUnion(sources), defSource);
    const autoAfter = autoOffsetReport(context, id + "autoA", result, defResult);

    var text = "Clean (quick): " ~ size(sewnAnchors) ~ " of " ~ size(targets) ~ " faces that refuse an offset of " ~ roundToPrecision(definition.offsetDistance / millimeter, 3)
        ~ " mm were resurfaced (" ~ (cachedFails != undefined ? "cached analysis" : "live analysis") ~ "). Faces " ~ before.faces ~ " -> " ~ after.faces
        ~ ", naked edges " ~ before.naked ~ " -> " ~ after.naked ~ ".";
    if (size(skipped) > 0)
    {
        text = text ~ " Left as they were: " ~ size(skipped) ~ " (orange):";
        for (var r in reasons)
        {
            text = text ~ " " ~ r.value ~ " x " ~ r.key ~ ";";
        }
    }
    if (holes > 0)
    {
        text = text ~ " WARNING: " ~ holes ~ " face(s) could not be restored and leave a hole.";
    }
    if (stale)
    {
        text = text ~ " The cached analysis was out of date, so it was analysed live; press Resolve to refresh it.";
    }
    if (definition.mode == CleanMode.THOROUGH)
    {
        text = text ~ " Thorough mode is not built yet; this is the quick result.";
    }
    if (checked > 0)
    {
        text = text ~ " Verify: " ~ refusing ~ " of " ~ checked ~ " faces of the result still refuse the offset (before: " ~ size(targets) ~ " of " ~ tested ~ ").";
    }
    text = text ~ " Offset+ Auto (direct offset, then thicken) at " ~ roundToPrecision(definition.offsetDistance / millimeter, 3) ~ " mm, whole surface: original " ~ autoBefore
        ~ "; result " ~ autoAfter ~ ".";
    text = text ~ " Green = resurfaced. Original is unchanged; hide it to see the result.";
    if (size(skipped) > 0 || holes > 0 || stale)
    {
        reportFeatureWarning(context, id, text);
    }
    else
    {
        reportFeatureInfo(context, id, text);
    }
}

/**
 * Build the replacement patch for one face (named in the id by t): sample points of the old face, approximate its net, make one B-spline
 * face with the approximated outer loop, then prove it offsets and stays within maxChange. On any failure the patch is deleted and the reason returned.
 * Returns { ok, reason, where, anchor, patch, dev, t }.
 */
function buildPatch(context is Context, id is Id, t is number, face is Query, definition is map) returns map
{
    var params = [];
    for (var i = 1; i < 7; i += 1)
    {
        for (var j = 1; j < 7; j += 1)
        {
            params = append(params, vector(i / 7, j / 7));
        }
    }
    const planes = evFaceTangentPlanes(context, { "face" : face, "parameters" : params, "returnUndefinedOutsideFace" : true });
    var pts = [];
    var anchor = undefined;
    var nearest = 1e9;
    for (var i = 0; i < size(params); i += 1)
    {
        if (planes[i] != undefined)
        {
            pts = append(pts, planes[i].origin);
            const d = norm(params[i] - vector(0.5, 0.5));
            if (d < nearest)
            {
                nearest = d;
                anchor = planes[i].origin;
            }
        }
    }
    const bb = evBox3d(context, { "topology" : face });
    const c = (bb.minCorner + bb.maxCorner) / 2 / millimeter;
    const where = "(" ~ roundToPrecision(c[0], 1) ~ ", " ~ roundToPrecision(c[1], 1) ~ ", " ~ roundToPrecision(c[2], 1) ~ ")";
    if (anchor == undefined)
    {
        return { "ok" : false, "reason" : "no interior point to rebuild from", "where" : where, "anchor" : undefined, "t" : t };
    }
    const patchId = id + ("patch" ~ t);
    var reason = "";
    try
    {
        const res = evApproximateBSplineSurface(context, { "face" : face, "tolerance" : PATCH_TOLERANCE });
        var def = { "bSplineSurface" : res.bSplineSurface };
        if (size(res.boundaryBSplineCurves) > 0)
        {
            def.boundaryBSplineCurves = res.boundaryBSplineCurves;
        }
        opCreateBSplineSurface(context, patchId, def);
    }
    catch (e)
    {
        reason = "cannot rebuild: " ~ e.message;
    }
    if (reason != "")
    {
        return { "ok" : false, "reason" : reason, "where" : where, "anchor" : anchor, "t" : t };
    }
    const patch = qCreatedBy(patchId, EntityType.BODY);
    if (offsetFailsAs(context, patchId + "off", qOwnedByBody(patch, EntityType.FACE), face, definition))
    {
        deleteBodies(context, patchId + "del", patch);
        return { "ok" : false, "reason" : "rebuilt face still refuses the offset", "where" : where, "anchor" : anchor, "t" : t };
    }
    var worst = 0 * meter;
    for (var p in pts)
    {
        worst = max(worst, evDistance(context, { "side0" : p, "side1" : patch }).distance);
    }
    if (worst > definition.maxChange)
    {
        deleteBodies(context, patchId + "del", patch);
        return { "ok" : false, "reason" : "rebuilt face is more than Max change from the original", "where" : where, "anchor" : anchor, "t" : t };
    }
    return { "ok" : true, "reason" : "", "where" : where, "anchor" : anchor, "patch" : patch, "dev" : worst, "t" : t };
}

function deleteBodies(context is Context, id is Id, bodies is Query)
{
    opDeleteBodies(context, id, { "entities" : bodies });
}

/** Colour the faces of `body` that contain the given points. */
function colorFacesAt(context is Context, body is Query, points is array, shade is Color)
{
    var found = [];
    for (var p in points)
    {
        found = concatenateArrays([found, evaluateQuery(context, qContainsPoint(qOwnedByBody(body, EntityType.FACE), p))]);
    }
    if (size(found) > 0)
    {
        setProperty(context, { "entities" : qUnion(found), "propertyType" : PropertyType.APPEARANCE, "value" : shade });
    }
}

/** Face, edge and naked-edge counts of a body. */
function bodyStats(context is Context, body is Query) returns map
{
    const edges = qOwnedByBody(body, EntityType.EDGE);
    return { "faces" : size(evaluateQuery(context, qOwnedByBody(body, EntityType.FACE))), "edges" : size(evaluateQuery(context, edges)),
            "naked" : size(evaluateQuery(context, qEdgeTopologyFilter(edges, EdgeTopology.ONE_SIDED))) };
}

/**
 * What a stored analysis is for: the offset distance and side and the sizes of the inputs. If any change the stored faces no longer mean
 * anything and the feature analyses live (the surfaces themselves are not compared, only their face count).
 */
function cacheKeyOf(context is Context, definition is map) returns string
{
    const faceCount = size(evaluateQuery(context, qOwnedByBody(qBodyType(definition.surfaces, BodyType.SHEET), EntityType.FACE)));
    return roundToPrecision(definition.offsetDistance / millimeter, 6) ~ "|" ~ (definition.offsetSide == OffsetSide.QUERY ? (definition.towardReference ? "Qt" : "Qa") ~ size(evaluateQuery(context, definition.sideReference)) : "B") ~ (definition.offsetTrim == true ? "T" : "F") ~ "|" ~ faceCount ~ "|"
        ~ size(evaluateQuery(context, definition.cleanFaces)) ~ "|" ~ size(evaluateQuery(context, definition.keepFaces));
}

/** The faces (keyed by the copy face) that the stored analysis says refuse the offset; undefined when the cache is off, not needed here, or out of date. */
function cachedFailing(context is Context, definition is map, srcIndex is map, srcFaces is array, faces is array)
{
    if (definition.useCache != true)
    {
        return undefined;
    }
    if (definition.mode == CleanMode.DIAGNOSE && definition.colorBy != CleanColorBy.OFFSET)
    {
        return undefined;
    }
    if (definition.cacheKey != cacheKeyOf(context, definition))
    {
        return undefined;
    }
    return copyKeys(context, definition.cachedFaces, srcIndex, srcFaces, faces);
}

/** The faces of a selection as a map of transient-id strings (the faces of the SOURCE surfaces, as the editing logic sees them). */
function faceKeys(context is Context, selection is Query) returns map
{
    var keys = {};
    for (var f in evaluateQuery(context, selection))
    {
        keys[transientQueriesToStrings(f)] = true;
    }
    return keys;
}

/**
 * Editing logic: the Resolve button runs the offset analysis here, once, and stores the faces that refuse the offset (a persistent face
 * selection) with a key of what they were computed for. The feature then skips the analysis. Offsets inside editing logic are checked by
 * a self test first: if they cannot run, the status says so and nothing is stored.
 */
export function cleanSurfacesEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map, hiddenBodies is Query, clickedButton is string) returns map
{
    if (clickedButton != "resolve" || definition.useCache != true)
    {
        return definition;
    }
    const sources = evaluateQuery(context, qBodyType(definition.surfaces, BodyType.SHEET));
    if (size(sources) == 0)
    {
        definition.cacheStatus = "Select the surfaces, then press Resolve.";
        return definition;
    }
    if (definition.offsetDistance == undefined)
    {
        definition.cacheStatus = "Choose a Clean mode or the Offset test view first: they set the offset distance.";
        return definition;
    }
    const faces = evaluateQuery(context, qOwnedByBody(qUnion(sources), EntityType.FACE));
    var selfRefused = 0;
    for (var i = 0; i < min(3, size(faces)); i += 1)
    {
        selfRefused += offsetRefused(context, id + ("self" ~ i), faces[i], SELF_CHECK_OFFSET, false) ? 1 : 0;
    }
    if (selfRefused >= min(3, size(faces)))
    {
        definition.cacheStatus = "Offsets cannot run inside the dialog here, so nothing was stored. Turn off Use cached analysis.";
        return definition;
    }
    const cleanKeys = faceKeys(context, definition.cleanFaces);
    const keepKeys = faceKeys(context, definition.keepFaces);
    const hasScope = size(cleanKeys) > 0;
    definition.offsetSigns = offsetSignsFor(context, qUnion(sources), definition);
    var failing = [];
    var tested = 0;
    for (var i = 0; i < size(faces); i += 1)
    {
        const key = transientQueriesToStrings(faces[i]);
        if (keepKeys[key] != undefined || (hasScope && cleanKeys[key] == undefined))
        {
            continue;
        }
        tested += 1;
        if (offsetFails(context, id + ("r" ~ i), faces[i], definition))
        {
            failing = append(failing, faces[i]);
        }
    }
    definition.cachedFaces = qUnion(failing);
    definition.cacheKey = cacheKeyOf(context, definition);
    definition.cacheStatus = "Resolved: " ~ size(failing) ~ " of " ~ tested ~ " faces refuse an offset of "
        ~ roundToPrecision(definition.offsetDistance / millimeter, 3) ~ " mm" ~ (definition.offsetSide == OffsetSide.QUERY ? " (toward the reference)." : " (either side).");
    return definition;
}

/** True when the kernel refuses to offset `face` on the tested side(s): either side for Both, else toward the reference. */
function offsetFails(context is Context, probeId is Id, face is Query, definition is map) returns boolean
{
    return offsetFailsAs(context, probeId, face, face, definition);
}

/** As offsetFails, for `testFace`, with the side taken from `sideFace` (a patch or a rehearsal copy is tested on the side of the face it stands for). */
function offsetFailsAs(context is Context, probeId is Id, testFace is Query, sideFace is Query, definition is map) returns boolean
{
    const trim = definition.offsetTrim == true;
    if (definition.offsetSide == OffsetSide.QUERY)
    {
        return offsetRefused(context, probeId, testFace, faceDirection(context, sideFace, definition) * definition.offsetDistance, trim);
    }
    return offsetRefused(context, probeId + "p", testFace, definition.offsetDistance, trim) || offsetRefused(context, probeId + "n", testFace, -definition.offsetDistance, trim);
}

/** +1 / -1: the way the offset test runs on this face's body (Query mode); the sides were read once by offsetSignsFor. */
function faceDirection(context is Context, face is Query, definition is map) returns number
{
    const owner = evaluateQuery(context, qOwnerBody(face));
    const direction = size(owner) == 1 ? definition.offsetSigns[transientQueriesToStrings(owner[0])] : undefined;
    if (direction == undefined)
    {
        throw regenError("Internal: the offset side of a face's surface was not resolved.");
    }
    return direction;
}

/** Offset tightness of a face: for Both the tighter side, otherwise the tested side. */
function tightnessFor(context is Context, face is Query, definition is map) returns number
{
    if (definition.offsetSide == OffsetSide.QUERY)
    {
        return tightness(context, face, faceDirection(context, face, definition) * definition.offsetDistance);
    }
    return min(tightness(context, face, definition.offsetDistance), tightness(context, face, -definition.offsetDistance));
}

/**
 * For Query mode, which way the offset test runs on each body of `bodies`: toward the side of the body the reference is on (+1 = along the
 * face normal), or away from it with Offset toward reference off. Same rule as Offset+ (reference_side/offset_plus.fs); empty map for Both.
 */
function offsetSignsFor(context is Context, bodies is Query, definition is map) returns map
{
    if (definition.offsetSide != OffsetSide.QUERY)
    {
        return {};
    }
    const probe = sideProbe(context, definition.sideReference);
    if (probe == undefined)
    {
        throw regenError("Offset side is Toward a reference: pick a side reference (or set Offset side to Both sides).", ["sideReference"]);
    }
    var signs = {};
    for (var b in evaluateQuery(context, bodies))
    {
        const side = sideOfProbe(context, probe, qOwnedByBody(b, EntityType.FACE));
        if (side == 0)
        {
            throw regenError("The side reference lies on a surface (or no side of it can be read there); pick something clearly to one side.", ["sideReference"]);
        }
        signs[transientQueriesToStrings(b)] = definition.towardReference ? side : -side;
    }
    return signs;
}

/** The side reference as something evDistance can measure from (a mate connector's origin, else the entity); undefined when nothing is picked. Mirrors referenceProbe in reference_side_utils.fs. */
function sideProbe(context is Context, reference is Query)
{
    if (isQueryEmpty(context, reference))
    {
        return undefined;
    }
    if (!isQueryEmpty(context, qBodyType(reference, BodyType.MATE_CONNECTOR)))
    {
        return evMateConnector(context, { "mateConnector" : reference }).origin;
    }
    return reference;
}

/** +1 when the probe is on the side the surface normal points to at the closest point, -1 on the other side, 0 when it lies on the surface or no side can be read. Mirrors sideSign in reference_side_utils.fs. */
function sideOfProbe(context is Context, probe, faces is Query) returns number
{
    const list = evaluateQuery(context, faces);
    if (size(list) == 0)
    {
        return 0;
    }
    const result = evDistance(context, { "side0" : probe, "side1" : qUnion(list) });
    var normal = undefined;
    try silent
    {
        normal = evFaceTangentPlane(context, { "face" : list[result.sides[1].index], "parameter" : result.sides[1].parameter }).normal;
    }
    if (normal == undefined)
    {
        return 0;
    }
    const s = dot(result.sides[0].point - result.sides[1].point, normal);
    return abs(s) <= 1e-6 * meter ? 0 : (s > 0 * meter ? 1 : -1);
}

/** Largest edge or vertex tolerance of a body, in metres. */
function maxTolerance(context is Context, body is Query) returns number
{
    var worst = 0;
    for (var q, v in evTolerances(context, { "entities" : qUnion([qOwnedByBody(body, EntityType.EDGE), qOwnedByBody(body, EntityType.VERTEX)]) }))
    {
        worst = max(worst, v / meter);
    }
    return worst;
}

/** A point well inside a face (the sample nearest the middle of its parameter box that lies on the face), or undefined. */
function interiorPoint(context is Context, face is Query)
{
    var params = [];
    for (var i = 1; i < 7; i += 1)
    {
        for (var j = 1; j < 7; j += 1)
        {
            params = append(params, vector(i / 7, j / 7));
        }
    }
    const planes = evFaceTangentPlanes(context, { "face" : face, "parameters" : params, "returnUndefinedOutsideFace" : true });
    var anchor = undefined;
    var nearest = 1e9;
    for (var i = 0; i < size(params); i += 1)
    {
        if (planes[i] != undefined && norm(params[i] - vector(0.5, 0.5)) < nearest)
        {
            nearest = norm(params[i] - vector(0.5, 0.5));
            anchor = planes[i].origin;
        }
    }
    return anchor;
}

/**
 * Rehearse one swap on a small copy (the face and the faces that share an edge with it) before touching the real body. Accepted only if the
 * patch sews, the faces and edges do not increase, the largest edge/vertex tolerance of the neighbourhood does not rise by more than
 * 0, the new face still passes the offset test AFTER sewing, and no neighbour that passed the offset test starts to refuse.
 * Returns { ok, reason }. Kernel refusals (void, sew) are read with try/catch, as in resurface.
 */
function trialSwap(context is Context, id is Id, job is map, cur is Query, definition is map) returns map
{
    const trialId = id + ("trial" ~ job.t);
    const found = evaluateQuery(context, qContainsPoint(qOwnedByBody(cur, EntityType.FACE), job.anchor));
    if (size(found) != 1)
    {
        return { "ok" : false, "reason" : "face not found again" };
    }
    const ring = evaluateQuery(context, qAdjacent(found[0], AdjacencyType.EDGE, EntityType.FACE));
    var ringPoints = [];
    var ringFaces = [];
    for (var r in ring)
    {
        const p = interiorPoint(context, r);
        if (p != undefined)
        {
            ringPoints = append(ringPoints, p);
            ringFaces = append(ringFaces, r);
        }
    }
    opExtractSurface(context, trialId + "mini", { "faces" : qUnion(append(ring, found[0])), "tangentPropagation" : false });
    const mini = qBodyType(qCreatedBy(trialId + "mini", EntityType.BODY), BodyType.SHEET);
    const before = bodyStats(context, mini);
    const tolBefore = maxTolerance(context, mini);
    var ringRefusedBefore = [];
    for (var i = 0; i < size(ringPoints); i += 1)
    {
        const rf = evaluateQuery(context, qContainsPoint(qOwnedByBody(mini, EntityType.FACE), ringPoints[i]));
        ringRefusedBefore = append(ringRefusedBefore, size(rf) != 1 || offsetFailsAs(context, trialId + ("rb" ~ i), rf[0], ringFaces[i], definition));
    }
    var reason = "";
    opPattern(context, trialId + "pat", { "entities" : job.patch, "transforms" : [identityTransform()], "instanceNames" : ["Trial_patch"] });
    const trialPatch = qCreatedBy(trialId + "pat", EntityType.BODY);
    const target = evaluateQuery(context, qContainsPoint(qOwnedByBody(mini, EntityType.FACE), job.anchor));
    if (size(target) != 1)
    {
        reason = "face not found in the rehearsal copy";
    }
    else
    {
        try
        {
            opDeleteFace(context, trialId + "void", { "deleteFaces" : target[0], "includeFillet" : false, "capVoid" : false, "leaveOpen" : true });
            opBoolean(context, trialId + "join", { "tools" : qUnion([mini, trialPatch]), "operationType" : BooleanOperationType.UNION });
        }
        catch (e)
        {
            reason = "would not sew: " ~ e.message;
        }
    }
    if (reason == "")
    {
        const after = bodyStats(context, mini);
        const tolAfter = maxTolerance(context, mini);
        if (after.faces > before.faces || after.edges > before.edges)
        {
            reason = "it would add faces or edges (" ~ before.faces ~ "/" ~ before.edges ~ " -> " ~ after.faces ~ "/" ~ after.edges ~ ")";
        }
        else
        {
            const nf = evaluateQuery(context, qContainsPoint(qOwnedByBody(mini, EntityType.FACE), job.anchor));
            if (size(nf) != 1 || offsetFailsAs(context, trialId + "nf", nf[0], found[0], definition))
            {
                reason = "the new face refuses the offset once sewn in";
            }
            for (var i = 0; i < size(ringPoints) && reason == ""; i += 1)
            {
                const rf = evaluateQuery(context, qContainsPoint(qOwnedByBody(mini, EntityType.FACE), ringPoints[i]));
                if (!ringRefusedBefore[i] && (size(rf) != 1 || offsetFailsAs(context, trialId + ("ra" ~ i), rf[0], ringFaces[i], definition)))
                {
                    reason = "a neighbouring face that offset before would refuse";
                }
            }
        }
    }
    const tolAfterAll = maxTolerance(context, qBodyType(qCreatedBy(trialId + "mini", EntityType.BODY), BodyType.SHEET));
    deleteBodies(context, trialId + "cleanup", qCreatedBy(trialId + "mini", EntityType.BODY));
    return { "ok" : reason == "", "reason" : reason, "tolBefore" : tolBefore, "tolAfter" : tolAfterAll };
}

/** Edges shorter than this get a cross marker as well (a curve this short cannot be seen), and the cross is CROSS_SIZE long. */
const MARKER_MIN_LENGTH = 1 * millimeter;
const CROSS_SIZE = 3 * millimeter;

/**
 * Draw a list of edges as thin curves (one wire body per edge) in one colour, named so they can be picked in the Parts list. An edge shorter
 * than MARKER_MIN_LENGTH is invisible, so it also gets a small cross (two short lines) at its middle.
 */
function markEdges(context is Context, id is Id, edges is array, shade is Color, label is string)
{
    if (size(edges) == 0)
    {
        return;
    }
    for (var i = 0; i < size(edges); i += 1)
    {
        opExtractWires(context, id + ("e" ~ i), { "edges" : edges[i] });
        if (evLength(context, { "entities" : edges[i] }) < MARKER_MIN_LENGTH)
        {
            const tl = evEdgeTangentLine(context, { "edge" : edges[i], "parameter" : 0.5 });
            const across = cross(tl.direction, abs(tl.direction[2]) < 0.9 ? vector(0, 0, 1) : vector(1, 0, 0));
            const side = across / norm(across);
            const half = CROSS_SIZE / 2;
            opCreateBSplineCurve(context, id + ("a" ~ i), { "bSplineCurve" : bSplineCurve({ "degree" : 1, "isPeriodic" : false,
                            "controlPoints" : [tl.origin - tl.direction * half, tl.origin + tl.direction * half], "knots" : knotArray([0, 0, 1, 1]) }) });
            opCreateBSplineCurve(context, id + ("b" ~ i), { "bSplineCurve" : bSplineCurve({ "degree" : 1, "isPeriodic" : false,
                            "controlPoints" : [tl.origin - side * half, tl.origin + side * half], "knots" : knotArray([0, 0, 1, 1]) }) });
        }
    }
    const wires = qCreatedBy(id, EntityType.BODY);
    setProperty(context, { "entities" : wires, "propertyType" : PropertyType.APPEARANCE, "value" : shade });
    setProperty(context, { "entities" : wires, "propertyType" : PropertyType.NAME, "value" : label });
}

/**
 * What Offset+ in Auto mode would do with a whole body: the direct offset (no neighbour trim, as Offset+ calls it), and when that fails the
 * one-sided thicken keeping the offset-side cap. Returns "direct", "thicken" or "FAILS". The bodies made on the way are deleted.
 */
function autoOffsetOutcome(context is Context, id is Id, faces is Query, signed is ValueWithUnits) returns string
{
    var direct = false;
    try silent
    {
        opExtractSurface(context, id + "d", { "faces" : faces, "tangentPropagation" : false, "offset" : signed, "useFacesAroundToTrimOffset" : false });
        direct = true;
    }
    if (direct)
    {
        deleteBodies(context, id + "dx", qCreatedBy(id + "d", EntityType.BODY));
        return "direct";
    }
    const along = signed > 0 * meter;
    var solid = false;
    try silent
    {
        opThicken(context, id + "s", { "entities" : faces, "thickness1" : along ? abs(signed) : 0 * meter, "thickness2" : along ? 0 * meter : abs(signed) });
        solid = true;
    }
    if (!solid)
    {
        return "FAILS";
    }
    var cap = false;
    try silent
    {
        opExtractSurface(context, id + "c", { "faces" : qCapEntity(id + "s", along ? CapType.START : CapType.END, EntityType.FACE) });
        cap = true;
    }
    deleteBodies(context, id + "sx", qCreatedBy(id + "s", EntityType.BODY));
    if (cap)
    {
        deleteBodies(context, id + "cx", qCreatedBy(id + "c", EntityType.BODY));
    }
    return cap ? "thicken" : "FAILS";
}

/** autoOffsetOutcome for every body of `bodies` on the tested side(s), as text: "along the normal OK (direct), against the normal FAILS". */
function autoOffsetReport(context is Context, id is Id, bodies is Query, definition is map) returns string
{
    var parts = [];
    var k = 0;
    for (var b in evaluateQuery(context, bodies))
    {
        const faces = qOwnedByBody(b, EntityType.FACE);
        var sides = [1, -1];
        if (definition.offsetSide == OffsetSide.QUERY)
        {
            sides = [faceDirection(context, evaluateQuery(context, faces)[0], definition)];
        }
        for (var s in sides)
        {
            k += 1;
            const outcome = autoOffsetOutcome(context, id + ("o" ~ k), faces, s * definition.offsetDistance);
            const label = definition.offsetSide == OffsetSide.QUERY ? (definition.towardReference ? "toward the reference" : "away from the reference") : (s > 0 ? "along the normal" : "against the normal");
            parts = append(parts, label ~ (outcome == "FAILS" ? " FAILS" : " ok (" ~ outcome ~ ")"));
        }
    }
    return joinArray(parts, ", ");
}
