FeatureScript 3095;
import(path : "onshape/std/common.fs", version : "3095.0");
// IMPORT: auto_layout_icon.svg (feature icon)
IconNamespace::import(path : "642c7f60a7761a7a6372b089", version : "b2add4d46cb8621a3e29dab8");

/**
 * Auto layout plus: lays selected solids, surfaces and composite parts out on the XY plane of an origin
 * mate connector. Length is X, width is Y. Bounding boxes are measured in the connector's frame and packed with a
 * free-rectangle (MaxRects, bottom-left) heuristic that fills the width (Y) first and then moves along the length
 * (X). x_buffer and y_buffer are the gaps between bodies and the margin from the connector. Optionally limits the
 * layout width and length (a full sheet starts a new one, offset along +Y), rotates bodies 90 degrees about the
 * connector Z, copies instead of moves, and wraps the result in a named composite part.
 *
 * Internal nesting (opt-in): small bodies are placed inside the holes of large ones; the outline sampling is the slow part.
 */

export const AUTO_LAYOUT_BUFFER_BOUNDS =
{
    (millimeter) : [0, 10, 1000],
    (meter) : 0.01,
    (centimeter) : 1,
    (inch) : 0.4
} as LengthBoundSpec;

export const AUTO_LAYOUT_WIDTH_BOUNDS =
{
    (millimeter) : [1, 2000, 100000],
    (meter) : 2,
    (centimeter) : 200,
    (inch) : 80
} as LengthBoundSpec;

const FREE_TOL = TOLERANCE.zeroLength * meter;
const UNBOUNDED_EXTENT = 1000 * meter;

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Auto layout plus",
        "Feature Type Description" : "Lays out solids, surfaces and composite parts on the XY plane of a " ~
        "mate connector. Fills the width (Y) first, then continues along the length (X); an optional length " ~
        "limit starts a new sheet. Bodies are packed by bounding box with x and y buffers between them, and keep " ~
        "their orientation (optional 90 degree rotation). Can move or copy the bodies and group the result in a " ~
        "named composite part." }
export const autoLayoutPlus = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Bodies to lay out",
                    "Filter" : EntityType.BODY && (BodyType.SOLID || BodyType.SHEET || BodyType.COMPOSITE) && ModifiableEntityOnly.YES }
        definition.bodies is Query;

        annotation { "Name" : "Pattern origin", "Filter" : BodyType.MATE_CONNECTOR || EntityType.VERTEX, "MaxNumberOfPicks" : 1,
                    "Description" : "A mate connector (existing or created here) or a vertex. A vertex gives world axes." }
        definition.origin is Query;

        annotation { "Name" : "X buffer", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
        isLength(definition.xBuffer, AUTO_LAYOUT_BUFFER_BOUNDS);

        annotation { "Name" : "Y buffer", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
        isLength(definition.yBuffer, AUTO_LAYOUT_BUFFER_BOUNDS);

        annotation { "Name" : "Limit layout width", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
        definition.limitWidth is boolean;

        if (definition.limitWidth)
        {
            annotation { "Name" : "Layout width (Y)", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
            isLength(definition.layoutWidth, AUTO_LAYOUT_WIDTH_BOUNDS);

            annotation { "Name" : "Limit layout length", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE,
                        "Description" : "When a sheet is full, start a new sheet offset along +Y." }
            definition.limitLength is boolean;

            if (definition.limitLength)
            {
                annotation { "Name" : "Layout length (X)", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
                isLength(definition.layoutLength, AUTO_LAYOUT_WIDTH_BOUNDS);
            }
        }

        annotation { "Name" : "Allow 90 degree rotation", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
        definition.allowRotation is boolean;

        annotation { "Name" : "Nest bodies inside holes", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE,
                    "Description" : "Place smaller bodies inside holes and windows of larger ones (flat bodies, outline and holes read from the largest flat face; 5 mm resolution; nested bodies keep their orientation)." }
        definition.allowNesting is boolean;

        if (definition.allowNesting)
        {
            annotation { "Name" : "Spread bodies in holes", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE,
                        "Description" : "Slide bodies nested in a hole to the middle of their free range, evening out the clearance to the hole wall and to each other. Off packs them tight against the first wall." }
            definition.spreadInHoles is boolean;
        }

        annotation { "Name" : "Place on origin plane", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
        definition.dropToPlane is boolean;

        annotation { "Name" : "Copy bodies", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
        definition.copyBodies is boolean;

        annotation { "Name" : "Create composite part", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
        definition.createComposite is boolean;

        if (definition.createComposite)
        {
            annotation { "Name" : "Composite name", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
            definition.compositeName is string;
        }

        annotation { "Name" : "Show layout preview", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE,
                    "Description" : "While editing, draws each slot's box, the sheet boundaries and the origin frame." }
        definition.showPreview is boolean;
    }
    {
        // Origin: a connector created in the field arrives as its vertex (as in export_tag); a real vertex gives world axes
        if (isQueryEmpty(context, definition.origin))
        {
            throw regenError("Select a mate connector or vertex as the pattern origin.", ["origin"]);
        }
        const ownerMC = qBodyType(qOwnerBody(definition.origin), BodyType.MATE_CONNECTOR);
        var cSys is CoordSystem = WORLD_COORD_SYSTEM;
        if (size(evaluateQuery(context, ownerMC)) > 0)
        {
            cSys = evMateConnector(context, { "mateConnector" : evaluateQuery(context, ownerMC)[0] });
        }
        else
        {
            cSys = coordSystem(evVertexPoint(context, { "vertex" : definition.origin }), vector(1, 0, 0), vector(0, 0, 1));
        }

        // A constituent of a selected composite moves with it
        const composites = qBodyType(definition.bodies, BodyType.COMPOSITE);
        const selected = qSubtraction(definition.bodies, qContainedInCompositeParts(composites));
        const bodyList = evaluateQuery(context, selected);
        if (size(bodyList) == 0)
        {
            throw regenError("Select at least one body to lay out.", ["bodies"]);
        }

        // Measure each body in the origin frame; each has one or two candidate orientations (long side along X first)
        var items = [];
        for (var body in bodyList)
        {
            const bb is Box3d = evBox3d(context, { "topology" : body, "cSys" : cSys });
            const dx = bb.maxCorner[0] - bb.minCorner[0];
            const dy = bb.maxCorner[1] - bb.minCorner[1];
            const turnFirst = definition.allowRotation && dy > dx;
            var orientations = [{ "rotate" : turnFirst, "w" : turnFirst ? dy : dx, "h" : turnFirst ? dx : dy }];
            if (definition.allowRotation && abs(dx - dy) > FREE_TOL)
            {
                orientations = append(orientations, { "rotate" : !turnFirst, "w" : turnFirst ? dx : dy, "h" : turnFirst ? dy : dx });
            }
            items = append(items, { "body" : body, "bb" : bb, "orientations" : orientations, "long" : dx > dy ? dx : dy, "dx" : dx,
                        "nested" : false, "facesDone" : false, "faces" : { "valid" : false },
                        "profiled" : false, "nest" : { "valid" : false } });
        }

        // Longest side first
        items = sort(items, function(a, b)
            {
                return b.long - a.long;
            });

        // Free-rectangle packing in the origin frame. A body occupies (w + xBuffer) x (h + yBuffer), its buffer on the
        // low side, so neighbours end up one buffer apart; the bin is one buffer smaller than the sheet to keep the margin.
        const sheetWidth = definition.limitWidth ? definition.layoutWidth : UNBOUNDED_EXTENT;
        const binWidth = definition.limitWidth ? definition.layoutWidth - definition.yBuffer : UNBOUNDED_EXTENT;
        const boundedLength = definition.limitWidth && definition.limitLength;
        const binLength = boundedLength ? definition.layoutLength - definition.xBuffer : UNBOUNDED_EXTENT;
        const sheetPitch = definition.limitWidth ? 1.1 * definition.layoutWidth : 0 * meter;
        var sheets = [{ "free" : [{ "x" : 0 * meter, "y" : 0 * meter, "w" : binLength, "h" : binWidth }], "usedX" : 0 * meter }];
        var oversize = 0;
        var nestedCount = 0;
        var regions = [];
        var holeRecords = [];
        const minHoleHeight = 2 * definition.yBuffer + 1 * millimeter;
        for (var i = 0; i < size(items); i += 1)
        {
            // Internal nesting first: a hole region of an already placed body, in that body's original frame
            var done = false;
            if (definition.allowNesting && anyRegionFitsX(regions, items[i].dx, definition.xBuffer))
            {
                // (a body keeps its orientation, so it can only go where a region is at least as long as it is)
                if (!items[i].profiled)
                {
                    if (!items[i].facesDone)
                    {
                        items[i].faces = bodyFaceData(context, items[i].body, cSys);
                        items[i].facesDone = true;
                    }
                    items[i].nest = outlineEnvelope(items[i].faces, items[i].bb);
                    items[i].profiled = true;
                }
                for (var r = 0; r < size(regions) && items[i].nest.valid && !done; r += 1)
                {
                    const fit = fitRegion(regions[r], items[i].nest, definition.xBuffer, definition.yBuffer);
                    if (fit != undefined)
                    {
                        const host = items[regions[r].host];
                        const shiftX = host.bb.minCorner[0] + (regions[r].i0 + fit.t) * NEST_STEP - items[i].bb.minCorner[0];
                        const zShift = definition.dropToPlane ? host.bb.minCorner[2] - items[i].bb.minCorner[2] : 0 * meter;
                        items[i].local = host.local * transform(vector(shiftX, fit.ty, zShift));
                        items[i].nested = true;
                        items[i].nestHost = regions[r].host;
                        items[i].nestT = regions[r].i0 + fit.t;
                        items[i].nestTy = fit.ty;
                        items[i].nestZ = zShift;
                        holeRecords[regions[r].hole].occupants = append(holeRecords[regions[r].hole].occupants, i);
                        var nextRegions = [];
                        for (var q = 0; q < size(regions); q += 1)
                        {
                            if (q != r)
                            {
                                nextRegions = append(nextRegions, regions[q]);
                            }
                        }
                        regions = concatenateArrays([nextRegions, splitRegion(regions[r], items[i].nest, fit, definition.xBuffer, minHoleHeight)]);
                        nestedCount += 1;
                        done = true;
                    }
                }
            }

            if (!done)
            {
                var best = undefined;
                for (var s = 0; s < size(sheets) && best == undefined; s += 1)
                {
                    best = findPosition(sheets[s].free, items[i].orientations, definition.xBuffer, definition.yBuffer);
                    if (best != undefined)
                    {
                        best.sheet = s;
                    }
                }
                if (best == undefined && boundedLength)
                {
                    // Sheet full: start a new one (a body bigger than a whole sheet is placed on it anyway)
                    const newY = size(sheets) * sheetPitch;
                    sheets = append(sheets, { "free" : [{ "x" : 0 * meter, "y" : newY, "w" : binLength, "h" : binWidth }], "usedX" : 0 * meter });
                    const s = size(sheets) - 1;
                    best = findPosition(sheets[s].free, items[i].orientations, definition.xBuffer, definition.yBuffer);
                    if (best == undefined)
                    {
                        best = { "orientation" : items[i].orientations[0], "x" : 0 * meter, "y" : newY };
                        oversize += 1;
                    }
                    best.sheet = s;
                }
                else if (best == undefined)
                {
                    // Only a body wider than the layout width can get here (the length is unbounded): put it beyond the others
                    best = { "orientation" : items[i].orientations[0], "x" : sheets[0].usedX, "y" : 0 * meter, "sheet" : 0 };
                    oversize += 1;
                }
                const o = best.orientation;
                const taken = { "x" : best.x, "y" : best.y, "w" : o.w + definition.xBuffer, "h" : o.h + definition.yBuffer };
                sheets[best.sheet].free = occupyRect(sheets[best.sheet].free, taken);
                if (taken.x + taken.w > sheets[best.sheet].usedX)
                {
                    sheets[best.sheet].usedX = taken.x + taken.w;
                }
                items[i].rotate = o.rotate;
                items[i].w = o.w;
                items[i].h = o.h;
                items[i].x = best.x + definition.xBuffer;
                items[i].y = best.y + definition.yBuffer;
                items[i].local = slotTransform(items[i], definition.dropToPlane);
            }

            // Register this body's holes as places for the bodies still to come
            // (only a body longer than the shortest remaining body can have a hole that one of them fits in)
            if (definition.allowNesting && i + 1 < size(items))
            {
                var shortest = items[i + 1].dx;
                for (var j = i + 2; j < size(items); j += 1)
                {
                    shortest = minLen(shortest, items[j].dx);
                }
                const needLength = shortest + 2 * definition.xBuffer;
                if (needLength <= items[i].dx + FREE_TOL)
                {
                    if (!items[i].facesDone)
                    {
                        items[i].faces = bodyFaceData(context, items[i].body, cSys);
                        items[i].facesDone = true;
                    }
                    for (var hole in holeProfiles(items[i].faces, items[i].bb, needLength))
                    {
                        const holeId = size(holeRecords);
                        const region = trimRegion(i, holeId, hole.lo, hole.hi, minHoleHeight);
                        if (region != undefined)
                        {
                            holeRecords = append(holeRecords, { "lo" : hole.lo, "hi" : hole.hi, "occupants" : [] });
                            regions = append(regions, region);
                        }
                    }
                }
            }
        }

        // Spread the bodies inside each hole: slide every occupant to the middle of its free range (walls and neighbours)
        if (nestedCount > 0 && definition.spreadInHoles)
        {
            for (var h = 0; h < size(holeRecords); h += 1)
            {
                var occupants = [];
                for (var k in holeRecords[h].occupants)
                {
                    occupants = append(occupants, { "lo" : items[k].nest.lo, "hi" : items[k].nest.hi, "T" : items[k].nestT, "ty" : items[k].nestTy });
                }
                if (size(occupants) > 0)
                {
                    const relaxed = relaxHole(holeRecords[h], occupants, definition.xBuffer, definition.yBuffer);
                    for (var n = 0; n < size(relaxed); n += 1)
                    {
                        items[holeRecords[h].occupants[n]].nestT = relaxed[n].T;
                        items[holeRecords[h].occupants[n]].nestTy = relaxed[n].ty;
                    }
                }
            }
            // Hosts come before the bodies inside them, so one pass in order rebuilds every nested transform
            for (var i = 0; i < size(items); i += 1)
            {
                if (items[i].nested)
                {
                    const host = items[items[i].nestHost];
                    const shiftX = host.bb.minCorner[0] + items[i].nestT * NEST_STEP - items[i].bb.minCorner[0];
                    items[i].local = host.local * transform(vector(shiftX, items[i].nestTy, items[i].nestZ));
                }
            }
        }

        // Preview: debug geometry shows only while the feature is being edited
        if (definition.showPreview)
        {
            debug(context, cSys);
            for (var i = 0; i < size(items); i += 1)
            {
                // (nested bodies sit inside their host's outline, so only the hosts are boxed)
                if (!items[i].nested)
                {
                    const zLo = definition.dropToPlane ? 0 * meter : items[i].bb.minCorner[2];
                    const zHi = zLo + items[i].bb.maxCorner[2] - items[i].bb.minCorner[2];
                    debug(context, box3d(vector(items[i].x, items[i].y, zLo), vector(items[i].x + items[i].w, items[i].y + items[i].h, zHi)),
                        cSys, DebugColor.CYAN);
                }
            }
            if (definition.limitWidth)
            {
                for (var s = 0; s < size(sheets); s += 1)
                {
                    const sheetLength = boundedLength ? definition.layoutLength : sheets[s].usedX + definition.xBuffer;
                    debug(context, box3d(vector(0 * meter, s * sheetPitch, 0 * meter),
                            vector(sheetLength, s * sheetPitch + sheetWidth, 0 * meter)), cSys, DebugColor.MAGENTA);
                }
            }
        }

        // Move or copy each body to its slot
        const toOrigin = fromWorld(cSys);
        const fromOrigin = toWorld(cSys);
        var placed = qNothing();
        for (var i = 0; i < size(items); i += 1)
        {
            const worldTransform = fromOrigin * items[i].local * toOrigin;

            if (definition.copyBodies)
            {
                const copyId = id + ("copy" ~ i);
                opPattern(context, copyId, {
                            "entities" : items[i].body,
                            "transforms" : [worldTransform],
                            "instanceNames" : ["1"]
                        });
                placed = qUnion([placed, qCreatedBy(copyId, EntityType.BODY)]);
            }
            else
            {
                opTransform(context, id + ("move" ~ i), {
                            "bodies" : items[i].body,
                            "transform" : worldTransform
                        });
                placed = qUnion([placed, items[i].body]);
            }
        }

        if (oversize > 0)
        {
            reportFeatureWarning(context, id, oversize ~ " bod" ~ (oversize == 1 ? "y is" : "ies are")
                    ~ " larger than the layout and " ~ (oversize == 1 ? "was" : "were") ~ " placed on their own.");
        }
        else if (size(sheets) > 1 || nestedCount > 0)
        {
            var notes = [];
            if (size(sheets) > 1)
            {
                notes = append(notes, "The layout uses " ~ size(sheets) ~ " sheets; each further sheet is offset along +Y.");
            }
            if (nestedCount > 0)
            {
                notes = append(notes, nestedCount ~ " bod" ~ (nestedCount == 1 ? "y was" : "ies were") ~ " nested inside holes.");
            }
            reportFeatureInfo(context, id, join(notes, " "));
        }

        if (definition.createComposite)
        {
            // A composite part cannot contain another composite part: laid-out composites (and, in copy mode, the
            // constituents that came with them) stay as they are; every other body goes into the new composite
            const placedComposites = qBodyType(placed, BodyType.COMPOSITE);
            const toGroup = qSubtraction(placed, qUnion([placedComposites, qContainedInCompositeParts(placedComposites)]));
            const grouped = size(evaluateQuery(context, toGroup));
            const skipped = size(evaluateQuery(context, placedComposites));
            if (grouped == 0)
            {
                reportFeatureWarning(context, id, "Composite part not created: every laid-out body is already a composite part.");
            }
            else
            {
                opCreateCompositePart(context, id + "composite", {
                            "bodies" : toGroup,
                            "closed" : false
                        });
                if (definition.compositeName != "")
                {
                    setProperty(context, {
                                "entities" : qCreatedBy(id + "composite", EntityType.BODY),
                                "propertyType" : PropertyType.NAME,
                                "value" : definition.compositeName
                            });
                }
                if (skipped > 0)
                {
                    reportFeatureWarning(context, id, skipped ~ " laid-out composite part(s) stay separate: "
                            ~ "a composite part cannot contain another composite part.");
                }
            }
        }
    }, { "xBuffer" : 10 * millimeter, "yBuffer" : 10 * millimeter, "limitWidth" : false, "layoutWidth" : 2000 * millimeter,
            "limitLength" : false, "layoutLength" : 2000 * millimeter,
            "allowRotation" : false, "allowNesting" : false, "spreadInHoles" : true, "dropToPlane" : true, "copyBodies" : false, "createComposite" : false,
            "compositeName" : "Layout", "showPreview" : true });

// ---- free-rectangle packing helpers (rectangles are maps with x, y, w, h lengths in the origin frame) ----

function rectsIntersect(a is map, b is map) returns boolean
{
    return a.x < b.x + b.w - FREE_TOL && a.x + a.w > b.x + FREE_TOL
        && a.y < b.y + b.h - FREE_TOL && a.y + a.h > b.y + FREE_TOL;
}

function rectInside(a is map, b is map) returns boolean
{
    return a.x >= b.x - FREE_TOL && a.y >= b.y - FREE_TOL
        && a.x + a.w <= b.x + b.w + FREE_TOL && a.y + a.h <= b.y + b.h + FREE_TOL;
}

// The lowest-x (then lowest-y) free corner where a body fits in one of its orientations: columns fill along Y first
function findPosition(free is array, orientations is array, xBuffer is ValueWithUnits, yBuffer is ValueWithUnits)
{
    var best = undefined;
    for (var o in orientations)
    {
        const w = o.w + xBuffer;
        const h = o.h + yBuffer;
        for (var f in free)
        {
            if (w <= f.w + FREE_TOL && h <= f.h + FREE_TOL)
            {
                const better = best == undefined || f.x < best.x - FREE_TOL
                    || (abs(f.x - best.x) <= FREE_TOL && f.y < best.y - FREE_TOL);
                if (better)
                {
                    best = { "orientation" : o, "x" : f.x, "y" : f.y };
                }
            }
        }
    }
    return best;
}

// Cut `taken` out of every free rectangle it overlaps (keeping the maximal remainders) and drop rectangles inside others
function occupyRect(free is array, taken is map) returns array
{
    var next = [];
    for (var f in free)
    {
        if (!rectsIntersect(f, taken))
        {
            next = append(next, f);
        }
        else
        {
            if (taken.x > f.x + FREE_TOL)
            {
                next = append(next, { "x" : f.x, "y" : f.y, "w" : taken.x - f.x, "h" : f.h });
            }
            if (taken.x + taken.w < f.x + f.w - FREE_TOL)
            {
                next = append(next, { "x" : taken.x + taken.w, "y" : f.y, "w" : f.x + f.w - (taken.x + taken.w), "h" : f.h });
            }
            if (taken.y > f.y + FREE_TOL)
            {
                next = append(next, { "x" : f.x, "y" : f.y, "w" : f.w, "h" : taken.y - f.y });
            }
            if (taken.y + taken.h < f.y + f.h - FREE_TOL)
            {
                next = append(next, { "x" : f.x, "y" : taken.y + taken.h, "w" : f.w, "h" : f.y + f.h - (taken.y + taken.h) });
            }
        }
    }
    var kept = [];
    for (var i = 0; i < size(next); i += 1)
    {
        var contained = false;
        for (var j = 0; j < size(next) && !contained; j += 1)
        {
            if (i != j && rectInside(next[i], next[j]) && (!rectInside(next[j], next[i]) || j < i))
            {
                contained = true;
            }
        }
        if (!contained)
        {
            kept = append(kept, next[i]);
        }
    }
    return kept;
}

// ---- slot transform and internal nesting helpers ----

const NEST_STEP = 5 * millimeter;
const NEST_EDGE_SAMPLE = 10 * millimeter;
const NEST_JOIN_TOL = 1e-6 * meter;
const NEST_BIG = 1000 * meter;

// Origin-frame transform that takes a body to its packed slot (rotation about the origin Z first, then translation)
function slotTransform(item is map, dropToPlane is boolean) returns Transform
{
    const bb = item.bb;
    const minZ = dropToPlane ? bb.minCorner[2] : 0 * meter;
    if (item.rotate)
    {
        // (px, py) -> (-py, px): new min corner is (-maxY, minX)
        return transform(vector(item.x + bb.maxCorner[1], item.y - bb.minCorner[0], -minZ))
            * rotationAround(line(vector(0, 0, 0) * meter, vector(0, 0, 1)), 90 * degree);
    }
    return transform(vector(item.x - bb.minCorner[0], item.y - bb.minCorner[1], -minZ));
}

function minLen(a is ValueWithUnits, b is ValueWithUnits) returns ValueWithUnits
{
    return a < b ? a : b;
}

function maxLen(a is ValueWithUnits, b is ValueWithUnits) returns ValueWithUnits
{
    return a > b ? a : b;
}

// The closed loops of a planar face as polylines in the origin frame (z dropped). Edges are sampled about every 10 mm and
// come oriented with the face on their left, so each edge continues from the end of the previous one.
function faceLoops(context is Context, face is Query, cSys is CoordSystem) returns array
{
    const edgeQuery = qAdjacent(face, AdjacencyType.EDGE, EntityType.EDGE);
    const edges = evaluateQuery(context, edgeQuery);
    // Straight edges need only their two ends; mark them once (two calls for the whole face instead of one per edge)
    const edgeIds = transientQueriesToStrings(edges);
    var straight = {};
    for (var lineId in transientQueriesToStrings(evaluateQuery(context, qGeometry(edgeQuery, GeometryType.LINE))))
    {
        straight[lineId] = true;
    }
    var chains = [];
    for (var ei = 0; ei < size(edges); ei += 1)
    {
        const e = edges[ei];
        const n = straight[edgeIds[ei]] == true ? 1 : clamp(ceil(evLength(context, { "entities" : e }) / NEST_EDGE_SAMPLE), 1, 40);
        var params = [];
        for (var k = 0; k <= n; k += 1)
        {
            params = append(params, k / n);
        }
        var points = [];
        for (var tangent in evEdgeTangentLines(context, { "edge" : e, "parameters" : params, "face" : face }))
        {
            const p = fromWorld(cSys, tangent.origin);
            points = append(points, vector(p[0], p[1], 0 * meter));
        }
        chains = append(chains, points);
    }
    var loops = [];
    var used = makeArray(size(chains), false);
    for (var i = 0; i < size(chains); i += 1)
    {
        if (!used[i])
        {
            used[i] = true;
            var loop = chains[i];
            var grew = true;
            while (grew)
            {
                grew = false;
                const tail = loop[size(loop) - 1];
                for (var j = 0; j < size(chains) && !grew; j += 1)
                {
                    if (!used[j] && norm(chains[j][0] - tail) < NEST_JOIN_TOL)
                    {
                        used[j] = true;
                        loop = concatenateArrays([loop, chains[j]]);
                        grew = true;
                    }
                }
            }
            loops = append(loops, loop);
        }
    }
    return loops;
}

function loopExtent(loop is array) returns map
{
    var ext = { "x0" : loop[0][0], "x1" : loop[0][0], "y0" : loop[0][1], "y1" : loop[0][1] };
    for (var p in loop)
    {
        ext.x0 = minLen(ext.x0, p[0]);
        ext.x1 = maxLen(ext.x1, p[0]);
        ext.y0 = minLen(ext.y0, p[1]);
        ext.y1 = maxLen(ext.y1, p[1]);
    }
    return ext;
}

// How many times the vertical line at x crosses a polyline, and the lowest and highest y of those crossings
function crossingSpan(loop is array, x is ValueWithUnits) returns map
{
    var count = 0;
    var lo = NEST_BIG;
    var hi = -NEST_BIG;
    for (var i = 0; i + 1 < size(loop); i += 1)
    {
        const a = loop[i];
        const b = loop[i + 1];
        if ((a[0] <= x && x < b[0]) || (b[0] <= x && x < a[0]))
        {
            const y = a[1] + (b[1] - a[1]) * ((x - a[0]) / (b[0] - a[0]));
            count += 1;
            lo = minLen(lo, y);
            hi = maxLen(hi, y);
        }
    }
    return { "n" : count, "lo" : lo, "hi" : hi };
}

// Per 5 mm cell from x0: the lowest and highest y of a body outline (a cell with no outline stays at NEST_BIG / -NEST_BIG)
function outlineCells(loop is array, x0 is ValueWithUnits, cells is number) returns map
{
    var samples = [];
    for (var b = 0; b <= cells; b += 1)
    {
        const span = crossingSpan(loop, x0 + b * NEST_STEP);
        samples = append(samples, span.n >= 2 ? span : undefined);
    }
    var lo = makeArray(cells, NEST_BIG);
    var hi = makeArray(cells, -NEST_BIG);
    for (var k = 0; k < cells; k += 1)
    {
        for (var s in [samples[k], samples[k + 1]])
        {
            if (s != undefined)
            {
                lo[k] = minLen(lo[k], s.lo);
                hi[k] = maxLen(hi[k], s.hi);
            }
        }
    }
    return { "lo" : lo, "hi" : hi };
}

// Per 5 mm cell from x0: the free interval of a hole (cells where the hole is not a single interval stay invalid)
function holeCells(loop is array, x0 is ValueWithUnits, cells is number) returns map
{
    var lo = makeArray(cells, NEST_BIG);
    var hi = makeArray(cells, -NEST_BIG);
    const ext = loopExtent(loop);
    const bStart = max(0, floor((ext.x0 - x0) / NEST_STEP));
    const bEnd = min(cells, ceil((ext.x1 - x0) / NEST_STEP));
    var previous = undefined;
    for (var b = bStart; b <= bEnd; b += 1)
    {
        const span = crossingSpan(loop, x0 + b * NEST_STEP);
        const current = span.n == 2 ? span : undefined;
        if (b > bStart && previous != undefined && current != undefined)
        {
            lo[b - 1] = maxLen(previous.lo, current.lo);
            hi[b - 1] = minLen(previous.hi, current.hi);
        }
        previous = current;
    }
    return { "lo" : lo, "hi" : hi };
}

// The overlap of two sets of holes (layers of a composite): every pair that still leaves room
function intersectHoles(a is array, b is array) returns array
{
    var result = [];
    for (var h in a)
    {
        for (var g in b)
        {
            var lo = [];
            var hi = [];
            var room = false;
            for (var k = 0; k < size(h.lo); k += 1)
            {
                lo = append(lo, maxLen(h.lo[k], g.lo[k]));
                hi = append(hi, minLen(h.hi[k], g.hi[k]));
                if (hi[k] - lo[k] > 0 * meter)
                {
                    room = true;
                }
            }
            if (room)
            {
                result = append(result, { "lo" : lo, "hi" : hi });
            }
        }
    }
    return result;
}

// The loops of the largest planar face of each layer of a body (a composite has several layers) in the origin frame, with
// the index of each layer's outer loop. Not valid for a body without a face parallel to the XY plane.
function bodyFaceData(context is Context, body is Query, cSys is CoordSystem) returns map
{
    const isComposite = size(evaluateQuery(context, qBodyType(body, BodyType.COMPOSITE))) > 0;
    const layers = isComposite ? evaluateQuery(context, qContainedInCompositeParts(body)) : [body];
    var layerData = [];
    for (var layer in layers)
    {
        const faces = evaluateQuery(context, qLargest(qGeometry(qOwnedByBody(layer, EntityType.FACE), GeometryType.PLANE)));
        if (size(faces) == 0 || abs(dot(evPlane(context, { "face" : faces[0] }).normal, cSys.zAxis)) < 0.999)
        {
            return { "valid" : false };
        }
        const loops = faceLoops(context, faces[0], cSys);
        if (size(loops) == 0)
        {
            return { "valid" : false };
        }
        var outer = 0;
        var outerArea = -1 * meter * meter;
        var extents = [];
        for (var i = 0; i < size(loops); i += 1)
        {
            const ext = loopExtent(loops[i]);
            extents = append(extents, ext);
            if ((ext.x1 - ext.x0) * (ext.y1 - ext.y0) > outerArea)
            {
                outerArea = (ext.x1 - ext.x0) * (ext.y1 - ext.y0);
                outer = i;
            }
        }
        layerData = append(layerData, { "loops" : loops, "extents" : extents, "outer" : outer });
    }
    return { "valid" : true, "layers" : layerData };
}

// The outline envelope of a body as a candidate for a hole (union over its layers); invalid if any strip has no outline
function outlineEnvelope(faceData is map, bb is Box3d) returns map
{
    if (!faceData.valid)
    {
        return { "valid" : false };
    }
    const x0 = bb.minCorner[0];
    const cells = ceil((bb.maxCorner[0] - x0) / NEST_STEP);
    var envLo = makeArray(cells, NEST_BIG);
    var envHi = makeArray(cells, -NEST_BIG);
    for (var layer in faceData.layers)
    {
        const env = outlineCells(layer.loops[layer.outer], x0, cells);
        for (var k = 0; k < cells; k += 1)
        {
            envLo[k] = minLen(envLo[k], env.lo[k]);
            envHi[k] = maxLen(envHi[k], env.hi[k]);
        }
    }
    for (var k = 0; k < cells; k += 1)
    {
        if (envHi[k] < envLo[k])
        {
            return { "valid" : false };
        }
    }
    return { "valid" : true, "lo" : envLo, "hi" : envHi };
}

// The holes of a body as per-strip free intervals (a composite keeps only what is open in every layer). Holes shorter than
// `needLength` along X cannot take any remaining body and are left out.
function holeProfiles(faceData is map, bb is Box3d, needLength is ValueWithUnits) returns array
{
    if (!faceData.valid)
    {
        return [];
    }
    const x0 = bb.minCorner[0];
    const cells = ceil((bb.maxCorner[0] - x0) / NEST_STEP);
    var holes = undefined;
    for (var layer in faceData.layers)
    {
        var layerHoles = [];
        for (var i = 0; i < size(layer.loops); i += 1)
        {
            if (i != layer.outer && layer.extents[i].x1 - layer.extents[i].x0 >= needLength - FREE_TOL)
            {
                layerHoles = append(layerHoles, holeCells(layer.loops[i], x0, cells));
            }
        }
        holes = holes == undefined ? layerHoles : intersectHoles(holes, layerHoles);
    }
    return holes == undefined ? [] : holes;
}

// Can a body of length `dx` (it keeps its orientation) fit, along X, in any hole region?
function anyRegionFitsX(regions is array, dx is ValueWithUnits, xBuffer is ValueWithUnits) returns boolean
{
    const needed = ceil(dx / NEST_STEP) + 2 * ceil(xBuffer / NEST_STEP);
    for (var region in regions)
    {
        if (size(region.lo) >= needed)
        {
            return true;
        }
    }
    return false;
}
// A hole's free intervals cut down to the cells that leave room for a body, as a region owned by item `host`
function trimRegion(host is number, holeId is number, lo is array, hi is array, minHeight is ValueWithUnits)
{
    var first = -1;
    var last = -1;
    for (var k = 0; k < size(lo); k += 1)
    {
        if (hi[k] - lo[k] > minHeight)
        {
            if (first < 0)
            {
                first = k;
            }
            last = k;
        }
    }
    if (last - first < 2)
    {
        return undefined;
    }
    var regionLo = [];
    var regionHi = [];
    for (var k = first; k <= last; k += 1)
    {
        regionLo = append(regionLo, lo[k]);
        regionHi = append(regionHi, hi[k]);
    }
    return { "host" : host, "hole" : holeId, "i0" : first, "lo" : regionLo, "hi" : regionHi };
}

// The lowest-x cell offset (then lowest y) where a body's envelope fits in the region with the buffers all round
function fitRegion(region is map, cand is map, xBuffer is ValueWithUnits, yBuffer is ValueWithUnits)
{
    const n = size(region.lo);
    const m = size(cand.lo);
    const nb = ceil(xBuffer / NEST_STEP);
    for (var t = nb; t + m + nb <= n; t += 1)
    {
        var lowY = -NEST_BIG;
        var highY = NEST_BIG;
        var ok = true;
        for (var j = 0; j < m && ok; j += 1)
        {
            lowY = maxLen(lowY, region.lo[t + j] - cand.lo[j]);
            highY = minLen(highY, region.hi[t + j] - cand.hi[j]);
            ok = lowY + yBuffer <= highY - yBuffer + FREE_TOL;
        }
        if (ok)
        {
            return { "t" : t, "ty" : lowY + yBuffer };
        }
    }
    return undefined;
}

// What is left of a region after a body took its place: the cells before and after it, and the strips above and below it
function splitRegion(region is map, cand is map, fit is map, xBuffer is ValueWithUnits, minHeight is ValueWithUnits) returns array
{
    const n = size(region.lo);
    const m = size(cand.lo);
    const nb = ceil(xBuffer / NEST_STEP);
    var pieces = [];
    var leftLo = [];
    var leftHi = [];
    for (var k = 0; k < fit.t - nb; k += 1)
    {
        leftLo = append(leftLo, region.lo[k]);
        leftHi = append(leftHi, region.hi[k]);
    }
    var rightLo = [];
    var rightHi = [];
    for (var k = fit.t + m + nb; k < n; k += 1)
    {
        rightLo = append(rightLo, region.lo[k]);
        rightHi = append(rightHi, region.hi[k]);
    }
    var aboveLo = [];
    var aboveHi = [];
    var belowLo = [];
    var belowHi = [];
    for (var j = 0; j < m; j += 1)
    {
        aboveLo = append(aboveLo, cand.hi[j] + fit.ty);
        aboveHi = append(aboveHi, region.hi[fit.t + j]);
        belowLo = append(belowLo, region.lo[fit.t + j]);
        belowHi = append(belowHi, cand.lo[j] + fit.ty);
    }
    const candidates = [
            { "i0" : region.i0, "lo" : leftLo, "hi" : leftHi },
            { "i0" : region.i0 + fit.t + m + nb, "lo" : rightLo, "hi" : rightHi },
            { "i0" : region.i0 + fit.t, "lo" : aboveLo, "hi" : aboveHi },
            { "i0" : region.i0 + fit.t, "lo" : belowLo, "hi" : belowHi }
        ];
    for (var c in candidates)
    {
        if (size(c.lo) > 0)
        {
            const piece = trimRegion(region.host, region.hole, c.lo, c.hi, minHeight);
            if (piece != undefined)
            {
                pieces = append(pieces, { "host" : piece.host, "hole" : piece.hole, "i0" : c.i0 + piece.i0, "lo" : piece.lo, "hi" : piece.hi });
            }
        }
    }
    return pieces;
}

// ---- spreading the bodies inside a hole (plain numbers in metres inside the loops: ValueWithUnits arithmetic is slow) ----

const NEST_RELAX_PASSES = 4;
const NEST_RELAX_REACH = 40; // cells (200 mm) an occupant may slide along the hole
const NEST_BIG_M = 1000;
const NEST_TOL_M = 1e-8;

// Occupant arrays at cell offset j (clipped, so the buffer cells past its ends repeat the end values)
function clippedAt(arr is array, j is number)
{
    return arr[clamp(j, 0, size(arr) - 1)];
}

// The other occupants whose cells come within `reach` cells of [T, T + m)
function neighboursOf(pos is array, a is number, T is number, m is number, reach is number) returns array
{
    var near = [];
    for (var b = 0; b < size(pos); b += 1)
    {
        if (b != a && pos[b].T < T + m + reach && pos[b].T + size(pos[b].lo) > T - reach)
        {
            near = append(near, pos[b]);
        }
    }
    return near;
}

// Is occupant a feasible at cell offset T and y shift ty: inside the hole walls and clear of the neighbours, with the
// buffers all round (x buffer as cells before and after, y buffer vertically)?
function occupantFits(wallLo is array, wallHi is array, me is map, near is array, T is number, ty is number, nb is number, yb is number) returns boolean
{
    const m = size(me.lo);
    for (var j = -nb; j < m + nb; j += 1)
    {
        const cell = T + j;
        if (cell < 0 || cell >= size(wallLo))
        {
            return false;
        }
        const myLo = clippedAt(me.lo, j) + ty;
        const myHi = clippedAt(me.hi, j) + ty;
        if (myLo < wallLo[cell] + yb - NEST_TOL_M || myHi > wallHi[cell] - yb + NEST_TOL_M)
        {
            return false;
        }
        for (var other in near)
        {
            const jb = cell - other.T;
            if (jb >= 0 && jb < size(other.lo))
            {
                const otherLo = other.lo[jb] + other.ty;
                const otherHi = other.hi[jb] + other.ty;
                if (myLo < otherHi + yb - NEST_TOL_M && myHi > otherLo - yb + NEST_TOL_M)
                {
                    return false;
                }
            }
        }
    }
    return true;
}

// The y shift in the middle of what occupant a may take at its current cells (walls and the neighbours above and below)
function centredY(wallLo is array, wallHi is array, pos is array, a is number, yb is number) returns number
{
    const me = pos[a];
    const m = size(me.lo);
    const near = neighboursOf(pos, a, me.T, m, 0);
    var lowBound = -NEST_BIG_M;
    var highBound = NEST_BIG_M;
    for (var j = 0; j < m; j += 1)
    {
        const cell = me.T + j;
        lowBound = max(lowBound, wallLo[cell] - me.lo[j] + yb);
        highBound = min(highBound, wallHi[cell] - me.hi[j] - yb);
        for (var other in near)
        {
            const jb = cell - other.T;
            if (jb >= 0 && jb < size(other.lo))
            {
                if (other.ty + (other.lo[jb] + other.hi[jb]) / 2 < me.ty + (me.lo[j] + me.hi[j]) / 2)
                {
                    lowBound = max(lowBound, other.hi[jb] + other.ty - me.lo[j] + yb);
                }
                else
                {
                    highBound = min(highBound, other.lo[jb] + other.ty - me.hi[j] - yb);
                }
            }
        }
    }
    return lowBound <= highBound ? (lowBound + highBound) / 2 : me.ty;
}

// The cell offset in the middle of how far occupant a can slide along the hole (found by bisection each way)
function centredT(wallLo is array, wallHi is array, pos is array, a is number, nb is number, yb is number) returns number
{
    const me = pos[a];
    const near = neighboursOf(pos, a, me.T - NEST_RELAX_REACH, size(me.lo) + 2 * NEST_RELAX_REACH, nb);
    if (!occupantFits(wallLo, wallHi, me, near, me.T, me.ty, nb, yb))
    {
        return me.T;
    }
    var reach = [0, 0];
    for (var side = 0; side < 2; side += 1)
    {
        const dir = side == 0 ? -1 : 1;
        var good = 0;
        var bad = NEST_RELAX_REACH + 1;
        while (bad - good > 1)
        {
            const mid = floor((good + bad) / 2);
            if (occupantFits(wallLo, wallHi, me, near, me.T + dir * mid, me.ty, nb, yb))
            {
                good = mid;
            }
            else
            {
                bad = mid;
            }
        }
        reach[side] = dir * good;
    }
    return me.T + round((reach[0] + reach[1]) / 2);
}

// Passes of sliding every occupant to the middle of its free range in y and then along x, until nothing moves: the clearance
// to the hole wall and to the other bodies inside it evens out. Rough on purpose: vertical gaps stand in for true distances.
function relaxHole(hole is map, occupants is array, xBuffer is ValueWithUnits, yBuffer is ValueWithUnits) returns array
{
    const nb = ceil(xBuffer / NEST_STEP);
    const yb = yBuffer / meter;
    var wallLo = [];
    var wallHi = [];
    for (var k = 0; k < size(hole.lo); k += 1)
    {
        wallLo = append(wallLo, hole.lo[k] / meter);
        wallHi = append(wallHi, hole.hi[k] / meter);
    }
    var pos = [];
    for (var o in occupants)
    {
        var lo = [];
        var hi = [];
        for (var k = 0; k < size(o.lo); k += 1)
        {
            lo = append(lo, o.lo[k] / meter);
            hi = append(hi, o.hi[k] / meter);
        }
        pos = append(pos, { "lo" : lo, "hi" : hi, "T" : o.T, "ty" : o.ty / meter });
    }
    for (var pass = 0; pass < NEST_RELAX_PASSES; pass += 1)
    {
        var moved = false;
        for (var a = 0; a < size(pos); a += 1)
        {
            const ty = centredY(wallLo, wallHi, pos, a, yb);
            if (abs(ty - pos[a].ty) > 1e-6)
            {
                moved = true;
            }
            pos[a].ty = ty;
            const T = centredT(wallLo, wallHi, pos, a, nb, yb);
            if (T != pos[a].T)
            {
                moved = true;
            }
            pos[a].T = T;
        }
        if (!moved)
        {
            break;
        }
    }
    var result = [];
    for (var p in pos)
    {
        result = append(result, { "T" : p.T, "ty" : p.ty * meter });
    }
    return result;
}