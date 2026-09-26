FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

/*
 * Composite boolean -- clip a composite part against a solid, or cut a composite part out of bodies.
 *
 * Replaces two unsynced features:
 *   "Composite intersection" (compositeIntersect, doc Mindbender Cores bf5cecdc, tab 9fc4d0ad) and
 *   "Subtract Composite"     (subtractComposite,  doc 20BSS 25 XXX Design 0286a663, tab 718b7970).
 * Both old feature types are still exported below with their old parameter ids and their old
 * operation ids ("allCopies", "result", "subtractComposites"), so saved instances can be repointed
 * by namespace alone. See composite_part_tools/README.md.
 *
 * Clip (Intersect / Subtract solid):
 *   1. copy the solid constituents of the composite in one opPattern ("allCopies"),
 *   2. place each copy against the solid with evCollision: INSIDE, OUTSIDE, CROSSING or TOUCHING,
 *   3. one opBoolean for all CROSSING copies (keepTools, the solid is never consumed),
 *   4. one opDeleteBodies for the copies the operation drops,
 *   5. the survivors become a new composite part ("result"), optionally named.
 * No try/catch: nothing is attempted that the placement did not show to be valid.
 */

/**
 * What Composite boolean does with the composite part.
 */
export enum CompositeBooleanOperation
{
    annotation { "Name" : "Intersect with solid" }
    INTERSECT,
    annotation { "Name" : "Subtract solid" }
    SUBTRACT,
    annotation { "Name" : "Subtract composite from bodies" }
    SUBTRACT_FROM_BODIES
}

/**
 * Where one constituent lies relative to the solid.
 * TOUCHING: the bodies only abut and evCollision could not say on which side.
 */
export enum ConstituentPlacement
{
    INSIDE,
    OUTSIDE,
    CROSSING,
    TOUCHING
}

annotation { "Feature Type Name" : "Composite boolean",
        "Feature Type Description" : "Intersect a composite part with a solid, or subtract the solid from it, into a new composite part. Or subtract a composite part's constituents from other bodies." }
export const compositeBoolean = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Operation", "UIHint" : [UIHint.SHOW_LABEL], "Default" : CompositeBooleanOperation.INTERSECT,
                    "Description" : "Intersect with solid: keep the parts of the composite inside the solid. Subtract solid: keep the parts outside it. Both make a new composite part and leave the inputs alone. Subtract composite from bodies: cut the composite's constituents out of the selected bodies." }
        definition.operation is CompositeBooleanOperation;

        annotation { "Name" : "Composite part", "Filter" : EntityType.BODY && BodyType.COMPOSITE && ConstructionObject.NO && SketchObject.NO }
        definition.compositePart is Query;

        if (definition.operation == CompositeBooleanOperation.SUBTRACT_FROM_BODIES)
        {
            annotation { "Name" : "Bodies to subtract from", "Filter" : EntityType.BODY && BodyType.SOLID && ConstructionObject.NO && SketchObject.NO }
            definition.targetBodies is Query;

            annotation { "Name" : "Keep composite", "Default" : false,
                        "Description" : "Off (as the old Subtract Composite): the composite's constituents are consumed by the subtraction." }
            definition.keepComposite is boolean;
        }
        else
        {
            annotation { "Name" : "Solid body", "Filter" : EntityType.BODY && BodyType.SOLID && ConstructionObject.NO && SketchObject.NO, "MaxNumberOfPicks" : 1 }
            definition.solidBody is Query;

            annotation { "Name" : "Closed composite", "Default" : false,
                        "Description" : "Off (as the old Composite intersection): an open composite, whose constituents stay selectable and which can be nested in another composite." }
            definition.closedComposite is boolean;

            annotation { "Name" : "Result name", "Default" : "",
                        "Description" : "Name of the new composite part. Empty: Onshape's default name." }
            definition.resultName is string;
        }
    }
    {
        if (definition.operation == CompositeBooleanOperation.SUBTRACT_FROM_BODIES)
        {
            subtractCompositeFromBodies(context, id, definition.compositePart, definition.targetBodies, definition.keepComposite, "compositePart");
        }
        else
        {
            clipComposite(context, id, {
                        "compositePart" : definition.compositePart,
                        "solidBody" : definition.solidBody,
                        "operation" : definition.operation,
                        "closed" : definition.closedComposite,
                        "resultName" : definition.resultName
                    });
        }
    }, { "operation" : CompositeBooleanOperation.INTERSECT, "closedComposite" : false, "resultName" : "", "keepComposite" : false });

/**
 * Legacy entry point: the old "Composite intersection" feature type, same parameter ids
 * (solidBody, compositePart), open composite, no name. Kept so the four Ski Cores instances
 * can be repointed by namespace only. New work: Composite boolean.
 */
annotation { "Feature Type Name" : "Composite intersection",
        "Feature Type Description" : "Legacy: same as Composite boolean, Intersect with solid, open composite." }
export const compositeIntersect = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Solid Body", "Filter" : EntityType.BODY && BodyType.SOLID && ConstructionObject.NO && SketchObject.NO, "MaxNumberOfPicks" : 1 }
        definition.solidBody is Query;

        annotation { "Name" : "Composite Part", "Filter" : EntityType.BODY && BodyType.COMPOSITE && ConstructionObject.NO && SketchObject.NO }
        definition.compositePart is Query;
    }
    {
        clipComposite(context, id, {
                    "compositePart" : definition.compositePart,
                    "solidBody" : definition.solidBody,
                    "operation" : CompositeBooleanOperation.INTERSECT,
                    "closed" : false,
                    "resultName" : ""
                });
    });

/**
 * Legacy entry point: the old "Subtract Composite" feature type, same parameter ids
 * (composteBody -- the old typo is the saved id -- and targetBodies), constituents consumed.
 * New work: Composite boolean, Subtract composite from bodies.
 */
annotation { "Feature Type Name" : "Subtract Composite",
        "Feature Type Description" : "Legacy: same as Composite boolean, Subtract composite from bodies." }
export const subtractComposite = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Composite Part", "Filter" : EntityType.BODY && BodyType.COMPOSITE && ConstructionObject.NO && SketchObject.NO, "MaxNumberOfPicks" : 1 }
        definition.composteBody is Query;

        annotation { "Name" : "Bodies to subtract from", "Filter" : EntityType.BODY && BodyType.SOLID && ConstructionObject.NO && SketchObject.NO }
        definition.targetBodies is Query;
    }
    {
        subtractCompositeFromBodies(context, id, definition.composteBody, definition.targetBodies, false, "composteBody");
    });

/**
 * Clips a composite part against a solid into a new composite part, leaving both inputs untouched.
 *
 * @param arg {{
 *      @field compositePart {Query} : The composite part(s) whose solid constituents are clipped.
 *      @field solidBody {Query} : The solid.
 *      @field operation {CompositeBooleanOperation} : INTERSECT keeps what is inside the solid, SUBTRACT what is outside.
 *      @field closed {boolean} : Make the result a closed composite.
 *      @field resultName {string} : Name for the result; "" leaves Onshape's default.
 * }}
 */
export function clipComposite(context is Context, id is Id, arg is map)
precondition
{
    arg.compositePart is Query;
    arg.solidBody is Query;
    arg.operation == CompositeBooleanOperation.INTERSECT || arg.operation == CompositeBooleanOperation.SUBTRACT;
    arg.closed is boolean;
    arg.resultName is string;
}
{
    if (isQueryEmpty(context, arg.compositePart))
    {
        throw regenError("Select a composite part.", ["compositePart"]);
    }
    if (isQueryEmpty(context, arg.solidBody))
    {
        throw regenError("Select a solid body.", ["solidBody"]);
    }
    const intersect = arg.operation == CompositeBooleanOperation.INTERSECT;
    const constituents = qContainedInCompositeParts(arg.compositePart);
    const solidConstituents = qBodyType(constituents, BodyType.SOLID);
    const skipped = size(evaluateQuery(context, qSubtraction(constituents, solidConstituents)));
    if (isQueryEmpty(context, solidConstituents))
    {
        throw regenError("The composite part has no solid constituents.", ["compositePart"]);
    }

    // 1. Copy every solid constituent at once. Op id kept from the old feature.
    const copyId = id + "allCopies";
    opPattern(context, copyId, {
                "entities" : solidConstituents,
                "transforms" : [identityTransform()],
                "instanceNames" : ["1"]
            });
    const copies = evaluateQuery(context, qCreatedBy(copyId, EntityType.BODY));

    // 2. Place each copy against the solid.
    var inside = [];
    var outside = [];
    var crossing = [];
    var touching = [];
    for (var copy in copies)
    {
        const placement = placementInSolid(context, copy, arg.solidBody);
        if (placement == ConstituentPlacement.INSIDE)
        {
            inside = append(inside, copy);
        }
        else if (placement == ConstituentPlacement.OUTSIDE)
        {
            outside = append(outside, copy);
        }
        else if (placement == ConstituentPlacement.CROSSING)
        {
            crossing = append(crossing, copy);
        }
        else
        {
            touching = append(touching, copy);
        }
    }

    // 3. One boolean for every copy that crosses the solid. The solid is kept.
    const clipId = id + "clip";
    if (size(crossing) > 0)
    {
        opBoolean(context, clipId, {
                    "operationType" : intersect ? BooleanOperationType.INTERSECTION : BooleanOperationType.SUBTRACTION,
                    "targets" : qUnion(crossing),
                    "tools" : arg.solidBody,
                    "keepTools" : true
                });
    }

    // 4. One delete for what the operation drops. TOUCHING copies are unresolved: an intersection
    //    leaves them out, a subtraction keeps them, and both warn.
    const dropped = intersect ? concatenateArrays([outside, touching]) : inside;
    if (size(dropped) > 0)
    {
        opDeleteBodies(context, id + "dropBodies", { "entities" : qUnion(dropped) });
    }

    // 5. The survivors (kept copies and boolean results) become the new composite.
    const survivors = qSubtraction(
        qBodyType(qUnion([qCreatedBy(copyId, EntityType.BODY), qCreatedBy(clipId, EntityType.BODY)]), [BodyType.SOLID, BodyType.SHEET]),
        arg.solidBody);
    if (isQueryEmpty(context, survivors))
    {
        throw regenError(intersect ? "No constituent of the composite part overlaps the solid." : "The solid removes every constituent of the composite part.",
            ["solidBody"]);
    }
    const resultId = id + "result"; // op id kept from the old feature: downstream references use it
    opCreateCompositePart(context, resultId, {
                "bodies" : survivors,
                "closed" : arg.closed
            });
    if (arg.resultName != "")
    {
        setProperty(context, {
                    "entities" : qBodyType(qCreatedBy(resultId, EntityType.BODY), BodyType.COMPOSITE),
                    "propertyType" : PropertyType.NAME,
                    "value" : arg.resultName
                });
    }

    // Summary.
    const nKept = size(evaluateQuery(context, survivors));
    var summary = (intersect ? "Intersect: " : "Subtract: ") ~ size(copies) ~ " constituent(s) -- " ~
        size(inside) ~ " inside, " ~ size(crossing) ~ " crossing (" ~ (intersect ? "intersected" : "cut") ~ "), " ~
        size(outside) ~ " outside; result composite of " ~ nKept ~ " bod" ~ (nKept == 1 ? "y." : "ies.");
    if (skipped > 0)
    {
        summary = summary ~ " " ~ skipped ~ " non-solid constituent(s) left out.";
    }
    if (size(touching) > 0)
    {
        reportFeatureWarning(context, id, summary ~ " " ~ size(touching) ~ " constituent(s) only touch the solid and could not be placed; " ~
                (intersect ? "they were left out." : "they were kept whole."));
    }
    else
    {
        reportFeatureInfo(context, id, summary);
    }
}

/**
 * Where `body` lies relative to `solid`, from evCollision (tools = solid, targets = body).
 * Clash types that prove shared volume (INTERFERE, TOOL_IN_TARGET) give CROSSING; TARGET_IN_TOOL
 * gives INSIDE; no clash (or EXISTS) gives OUTSIDE. When the two only ABUT, the pair is asked the
 * other way round (tools = body), which says whether the body is the inner one.
 */
export function placementInSolid(context is Context, body is Query, solid is Query) returns ConstituentPlacement
{
    const primary = clashTypes(evCollision(context, { "tools" : solid, "targets" : body }));
    if (primary[ClashType.INTERFERE] == true || primary[ClashType.TOOL_IN_TARGET] == true)
    {
        return ConstituentPlacement.CROSSING;
    }
    if (primary[ClashType.TARGET_IN_TOOL] == true)
    {
        return ConstituentPlacement.INSIDE;
    }
    const abuts = primary[ClashType.ABUT_NO_CLASS] == true || primary[ClashType.ABUT_TOOL_IN_TARGET] == true ||
        primary[ClashType.ABUT_TOOL_OUT_TARGET] == true;
    if (!abuts)
    {
        return ConstituentPlacement.OUTSIDE;
    }

    // Abutting only: ask with the body as the tool.
    const swapped = clashTypes(evCollision(context, { "tools" : body, "targets" : solid }));
    if (swapped[ClashType.INTERFERE] == true || swapped[ClashType.TARGET_IN_TOOL] == true)
    {
        return ConstituentPlacement.CROSSING;
    }
    if (swapped[ClashType.TOOL_IN_TARGET] == true || swapped[ClashType.ABUT_TOOL_IN_TARGET] == true)
    {
        return ConstituentPlacement.INSIDE;
    }
    if (primary[ClashType.ABUT_TOOL_IN_TARGET] == true)
    {
        // The solid sits inside the body, touching it: they share the solid's volume.
        return ConstituentPlacement.CROSSING;
    }
    if (swapped[ClashType.ABUT_TOOL_OUT_TARGET] == true || primary[ClashType.ABUT_TOOL_OUT_TARGET] == true)
    {
        return ConstituentPlacement.OUTSIDE;
    }
    return ConstituentPlacement.TOUCHING;
}

/**
 * The set of clash types in an evCollision result, as a map ClashType -> true.
 */
export function clashTypes(collisions is array) returns map
{
    var found = {};
    for (var collision in collisions)
    {
        found[collision["type"]] = true;
    }
    return found;
}

/**
 * Subtracts the solid constituents of a composite part from `targetBodies` (the old Subtract
 * Composite). Op id "subtractComposites" kept from the old feature: downstream references use it.
 */
export function subtractCompositeFromBodies(context is Context, id is Id, compositePart is Query, targetBodies is Query,
    keepComposite is boolean, compositeParameter is string)
{
    if (isQueryEmpty(context, compositePart))
    {
        throw regenError("Select a composite part.", [compositeParameter]);
    }
    if (isQueryEmpty(context, targetBodies))
    {
        throw regenError("Select the bodies to subtract from.", ["targetBodies"]);
    }
    const tools = qBodyType(qContainedInCompositeParts(compositePart), BodyType.SOLID);
    if (isQueryEmpty(context, tools))
    {
        throw regenError("The composite part has no solid constituents.", [compositeParameter]);
    }
    const nTools = size(evaluateQuery(context, tools));
    const nTargets = size(evaluateQuery(context, targetBodies));
    opBoolean(context, id + "subtractComposites", {
                "operationType" : BooleanOperationType.SUBTRACTION,
                "targets" : targetBodies,
                "tools" : tools,
                "keepTools" : keepComposite
            });
    reportFeatureInfo(context, id, "Subtracted " ~ nTools ~ " constituent(s) from " ~ nTargets ~ " bod" ~ (nTargets == 1 ? "y" : "ies") ~
            (keepComposite ? "; composite kept." : "; constituents consumed."));
}
