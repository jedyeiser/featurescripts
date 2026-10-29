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
 * Phase 2 (not built): internal nesting, placing small bodies inside the holes of large ones.
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
            items = append(items, { "body" : body, "bb" : bb, "orientations" : orientations, "long" : dx > dy ? dx : dy });
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
        for (var i = 0; i < size(items); i += 1)
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
        }

        // Preview: debug geometry shows only while the feature is being edited
        if (definition.showPreview)
        {
            debug(context, cSys);
            for (var i = 0; i < size(items); i += 1)
            {
                const zLo = definition.dropToPlane ? 0 * meter : items[i].bb.minCorner[2];
                const zHi = zLo + items[i].bb.maxCorner[2] - items[i].bb.minCorner[2];
                debug(context, box3d(vector(items[i].x, items[i].y, zLo), vector(items[i].x + items[i].w, items[i].y + items[i].h, zHi)),
                    cSys, DebugColor.CYAN);
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
        const zAxisLocal = line(vector(0, 0, 0) * meter, vector(0, 0, 1));
        var placed = qNothing();
        for (var i = 0; i < size(items); i += 1)
        {
            const bb = items[i].bb;
            const minZ = definition.dropToPlane ? bb.minCorner[2] : 0 * meter;
            var local;
            if (items[i].rotate)
            {
                // (px, py) -> (-py, px): new min corner is (-maxY, minX)
                local = transform(vector(items[i].x + bb.maxCorner[1], items[i].y - bb.minCorner[0], -minZ))
                    * rotationAround(zAxisLocal, 90 * degree);
            }
            else
            {
                local = transform(vector(items[i].x - bb.minCorner[0], items[i].y - bb.minCorner[1], -minZ));
            }
            const worldTransform = fromOrigin * local * toOrigin;

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
        else if (size(sheets) > 1)
        {
            reportFeatureInfo(context, id, "The layout uses " ~ size(sheets) ~ " sheets; each further sheet is offset along +Y.");
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
            "allowRotation" : false, "dropToPlane" : true, "copyBodies" : false, "createComposite" : false,
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
