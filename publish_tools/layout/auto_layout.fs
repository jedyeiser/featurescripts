FeatureScript 3095;
import(path : "onshape/std/common.fs", version : "3095.0");
// IMPORT: auto_layout_icon.svg (feature icon)
IconNamespace::import(path : "642c7f60a7761a7a6372b089", version : "b2add4d46cb8621a3e29dab8");

/**
 * Auto layout plus: lays selected solids, surfaces and composite parts out in rows on the XY plane of an origin
 * mate connector. Bounding boxes are measured in the connector's frame and packed in shelf rows; x_buffer and
 * y_buffer are the gaps between bodies and the margin from the connector. Optionally limits the row width,
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
        "Feature Type Description" : "Lays out solids, surfaces and composite parts in rows on the XY plane of a " ~
        "mate connector, with x and y buffers between bodies. Bodies keep their orientation (optional 90 degree " ~
        "rotation). Can move or copy the bodies and group the result in a composite part." }
export const autoLayoutPlus = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Bodies to lay out",
                    "Filter" : EntityType.BODY && (BodyType.SOLID || BodyType.SHEET || BodyType.COMPOSITE) && ModifiableEntityOnly.YES }
        definition.bodies is Query;

        annotation { "Name" : "Pattern origin", "Filter" : BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
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
    }
    {
        // Origin: an MC pick may arrive as its owner body or vertex
        // (existing connectors via their owner body; an implicit connector created in the field resolves as the pick itself)
        const ownerMC = qBodyType(qOwnerBody(definition.origin), BodyType.MATE_CONNECTOR);
        // (an implicit connector does not evaluate to a body here, so evMateConnector alone validates the pick, as std Decal does)
        const originMC = size(evaluateQuery(context, ownerMC)) == 1 ? ownerMC : definition.origin;
        var cSys = undefined;
        try
        {
            cSys = evMateConnector(context, { "mateConnector" : originMC });
        }
        catch
        {
            throw regenError("Select a mate connector (existing or new) as the pattern origin.", ["origin"]);
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
            const rotate = definition.allowRotation && dy > dx;
            items = append(items, {
                        "body" : body,
                        "bb" : bb,
                        "rotate" : rotate,
                        "w" : rotate ? dy : dx,
                        "h" : rotate ? dx : dy
                    });
        }

        // Tallest first gives tidy shelves
        items = sort(items, function(a, b)
            {
                return b.h - a.h;
            });

        // Shelf packing in the origin frame
        var x = definition.xBuffer;
        var y = definition.yBuffer;
        var rowHeight = 0 * meter;
        var tooWide = 0;
        for (var i = 0; i < size(items); i += 1)
        {
            const fitsRow = x + items[i].w + definition.xBuffer <= definition.layoutWidth + TOLERANCE.zeroLength * meter;
            if (definition.limitWidth && !fitsRow && x > definition.xBuffer + TOLERANCE.zeroLength * meter)
            {
                x = definition.xBuffer;
                y += rowHeight + definition.yBuffer;
                rowHeight = 0 * meter;
            }
            if (definition.limitWidth && x + items[i].w + definition.xBuffer > definition.layoutWidth + TOLERANCE.zeroLength * meter)
            {
                tooWide += 1;
            }
            items[i].x = x;
            items[i].y = y;
            x += items[i].w + definition.xBuffer;
            if (items[i].h > rowHeight)
            {
                rowHeight = items[i].h;
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
                    ~ " wider than the layout width and sit alone in their row.");
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
            }
        }
    }, { "xBuffer" : 10 * millimeter, "yBuffer" : 10 * millimeter, "limitWidth" : false, "layoutWidth" : 2000 * millimeter,
            "allowRotation" : false, "dropToPlane" : true, "copyBodies" : false, "createComposite" : false });
