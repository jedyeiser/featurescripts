FeatureScript 3095;
import(path : "onshape/std/common.fs", version : "3095.0");
// IMPORT: auto_layout_icon.svg (feature icon)
IconNamespace::import(path : "642c7f60a7761a7a6372b089", version : "b2add4d46cb8621a3e29dab8");

/**
 * Auto layout plus: lays selected solids, surfaces and composite parts out on the XY plane of an origin
 * mate connector. Length is X, width is Y. Bounding boxes are measured in the connector's frame and packed in
 * columns: the width (Y) is filled first, then a new column starts further along the length (X). x_buffer and
 * y_buffer are the gaps between bodies and the margin from the connector. Optionally limits the layout width,
 * rotates bodies 90 degrees about the connector Z, copies instead of moves, and wraps the result in a composite.
 *
 * v1: bounding-box packing only. Internal nesting (placing small bodies inside the holes of large ones) is phase 2.
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

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Auto layout plus",
        "Feature Type Description" : "Lays out solids, surfaces and composite parts on the XY plane of a " ~
        "mate connector. Fills the width (Y) first, then starts a new column along the length (X). " ~
        "Bodies are packed by bounding box, with x and y buffers between them. Bodies keep their orientation (optional 90 degree " ~
        "rotation). Can move or copy the bodies and group the result in a composite part." }
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
            annotation { "Name" : "Layout width", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
            isLength(definition.layoutWidth, AUTO_LAYOUT_WIDTH_BOUNDS);
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
                    "Description" : "While editing, draws each slot's box, the layout boundary and the origin frame." }
        definition.showPreview is boolean;
    }
    {
        // Origin: an MC pick may arrive as its owner body or vertex
        // (existing connectors via their owner body; an implicit connector created in the field resolves as the pick itself)
        const ownerMC = qBodyType(qOwnerBody(definition.origin), BodyType.MATE_CONNECTOR);
        // (a connector created in the field arrives as its vertex, as in export_tag; a real vertex gives world axes)
        if (isQueryEmpty(context, definition.origin))
        {
            throw regenError("Select a mate connector or vertex as the pattern origin.", ["origin"]);
        }
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

        // Measure each body in the origin frame
        var items = [];
        for (var body in bodyList)
        {
            const bb is Box3d = evBox3d(context, { "topology" : body, "cSys" : cSys });
            const dx = bb.maxCorner[0] - bb.minCorner[0];
            const dy = bb.maxCorner[1] - bb.minCorner[1];
            // Length is X, width is Y: the long side runs along X, which keeps the Y extent (the limited one) smallest
            const rotate = definition.allowRotation && dy > dx;
            items = append(items, {
                        "body" : body,
                        "bb" : bb,
                        "rotate" : rotate,
                        "w" : rotate ? dy : dx,
                        "h" : rotate ? dx : dy
                    });
        }

        // Longest first: each column then starts with its longest body
        items = sort(items, function(a, b)
            {
                return b.w - a.w;
            });

        // Column packing in the origin frame: fill the width (Y) first; when it is used up, start a new column
        // further along the length (X)
        var x = definition.xBuffer;
        var y = definition.yBuffer;
        var columnLength = 0 * meter;
        var tooWide = 0;
        for (var i = 0; i < size(items); i += 1)
        {
            const overflows = y + items[i].h + definition.yBuffer > definition.layoutWidth + TOLERANCE.zeroLength * meter;
            if (definition.limitWidth && overflows && y > definition.yBuffer + TOLERANCE.zeroLength * meter)
            {
                x += columnLength + definition.xBuffer;
                y = definition.yBuffer;
                columnLength = 0 * meter;
            }
            if (definition.limitWidth && y + items[i].h + definition.yBuffer > definition.layoutWidth + TOLERANCE.zeroLength * meter)
            {
                tooWide += 1;
            }
            items[i].x = x;
            items[i].y = y;
            y += items[i].h + definition.yBuffer;
            if (items[i].w > columnLength)
            {
                columnLength = items[i].w;
            }
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
                debug(context, box3d(vector(0 * meter, 0 * meter, 0 * meter),
                        vector(x + columnLength + definition.xBuffer, definition.layoutWidth, 0 * meter)), cSys, DebugColor.MAGENTA);
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

        if (tooWide > 0)
        {
            reportFeatureWarning(context, id, tooWide ~ " bod" ~ (tooWide == 1 ? "y is" : "ies are")
                    ~ " wider than the layout width (Y) and sit alone in their column.");
        }

        if (definition.createComposite)
        {
            if (size(evaluateQuery(context, qBodyType(placed, BodyType.COMPOSITE))) > 0)
            {
                reportFeatureWarning(context, id, "Composite part not created: a laid-out body is already a composite part.");
            }
            else
            {
                opCreateCompositePart(context, id + "composite", {
                            "bodies" : placed,
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
            }
        }
    }, { "xBuffer" : 10 * millimeter, "yBuffer" : 10 * millimeter, "limitWidth" : false, "layoutWidth" : 2000 * millimeter,
            "allowRotation" : false, "dropToPlane" : true, "copyBodies" : false, "createComposite" : false,
            "compositeName" : "Layout", "showPreview" : true });
