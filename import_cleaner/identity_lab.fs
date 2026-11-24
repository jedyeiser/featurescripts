FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

/**
 * Identity lab (2026-11-24): an EXPERIMENT, not a product feature. It makes clean copies of derived / imported bodies (or only tags
 * them) and gives each a KEY taken from geometry, never from entity ids: kind (wire / sheet / solid) + rank by size (largest first;
 * a tie is broken by centroid z, y, x). The copy of each body is made by an operation whose id contains the key
 * (id + "copy_wire0"), so a downstream feature can select it with qCreatedBy(id + "copy_wire0", EntityType.BODY) however the ids of the
 * source bodies change. The copy also gets the attribute "idlab" = { run, key, kind, rank, mode } and, when listed in Names
 * ("key=name;key=name"), a name (the app reads names over REST).
 *
 * Modes: pattern copy (opPattern, properties and attributes copied), extract copy (opExtractSurface for sheets and solids, opExtractWires
 * for wires), extract copy with solids enclosed (opEnclose: a new solid made by our own operation), tag only (attribute on the input).
 * The inputs are kept unless "Delete inputs" is on.
 */
export enum IdentityLabMode
{
    annotation { "Name" : "Pattern copy (opPattern)" }
    COPY_PATTERN,
    annotation { "Name" : "Extract copy (surface / wires)" }
    COPY_EXTRACT,
    annotation { "Name" : "Extract copy, solids enclosed" }
    COPY_ENCLOSE,
    annotation { "Name" : "Tag only (no copy)" }
    TAG_ONLY
}

annotation { "Feature Type Name" : "Identity lab",
        "Feature Type Description" : "Experiment: clean copies of derived bodies with keys from geometry (kind and size rank), to test which references survive a source update." }
export const identityLab = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Bodies", "Filter" : EntityType.BODY }
        definition.bodies is Query;

        annotation { "Name" : "Mode", "UIHint" : [UIHint.SHOW_LABEL] }
        definition.mode is IdentityLabMode;

        annotation { "Name" : "Run label", "Description" : "Written into the attribute of every copy; use it to tell runs apart." }
        definition.runLabel is string;

        annotation { "Name" : "Name prefix", "Description" : "Put in front of every name set below." }
        definition.namePrefix is string;

        annotation { "Name" : "Names", "Description" : "key=name pairs separated by ;  (keys: wire0, wire1, sheet0, sheet1, solid0 ...). The key is the kind and the rank by size, largest first." }
        definition.names is string;

        annotation { "Name" : "Remove copied deform tags", "Default" : true, "Description" : "A copy carries the source's attributes, including its Deform tag: remove them from the copy so the tag group is not found twice." }
        definition.purgeTags is boolean;

        annotation { "Name" : "Delete inputs", "Default" : false }
        definition.deleteInputs is boolean;
    }
    {
        const wanted = qUnion([qBodyType(definition.bodies, BodyType.WIRE), qBodyType(definition.bodies, BodyType.SHEET), qBodyType(definition.bodies, BodyType.SOLID)]);
        var infos = [];
        for (var body in evaluateQuery(context, wanted))
        {
            var kind = "solid";
            var order = 2;
            var extent = 0;
            if (!isQueryEmpty(context, qBodyType(body, BodyType.WIRE)))
            {
                kind = "wire";
                order = 0;
                extent = evLength(context, { "entities" : qOwnedByBody(body, EntityType.EDGE) }) / meter;
            }
            else if (!isQueryEmpty(context, qBodyType(body, BodyType.SHEET)))
            {
                kind = "sheet";
                order = 1;
                extent = evArea(context, { "entities" : qOwnedByBody(body, EntityType.FACE) }) / meter ^ 2;
            }
            else
            {
                extent = evVolume(context, { "entities" : body }) / meter ^ 3;
            }
            infos = append(infos, { "body" : body, "kind" : kind, "order" : order, "extent" : extent,
                        "c" : evApproximateCentroid(context, { "entities" : body }) / meter });
        }
        if (size(infos) == 0)
        {
            throw regenError("Select the bodies (wires, sheets or solids) to copy or tag.", ["bodies"]);
        }
        // kind, then size (largest first; sizes equal within 1e-6 relative are a tie), then the centroid z, y, x
        infos = sort(infos, function(a, b)
            {
                if (a.order != b.order)
                {
                    return a.order - b.order;
                }
                if (abs(a.extent - b.extent) > 1e-6 * max(abs(a.extent), abs(b.extent)))
                {
                    return b.extent > a.extent ? 1 : -1;
                }
                for (var axis in [2, 1, 0])
                {
                    if (abs(a.c[axis] - b.c[axis]) > 1e-9)
                    {
                        return a.c[axis] < b.c[axis] ? -1 : 1;
                    }
                }
                return 0;
            });
        const nameFor = parseNames(definition.names);
        var rankOf = { "wire" : 0, "sheet" : 0, "solid" : 0 };
        var summary = [];
        var copies = [];
        for (var info in infos)
        {
            const key = info.kind ~ rankOf[info.kind];
            const rank = rankOf[info.kind];
            rankOf[info.kind] = rank + 1;
            var result = info.body;
            if (definition.mode == IdentityLabMode.COPY_PATTERN)
            {
                opPattern(context, id + ("copy_" ~ key), { "entities" : info.body, "transforms" : [identityTransform()], "instanceNames" : [key],
                            "copyPropertiesAndAttributes" : true });
                result = qCreatedBy(id + ("copy_" ~ key), EntityType.BODY);
            }
            else if (definition.mode == IdentityLabMode.COPY_EXTRACT || definition.mode == IdentityLabMode.COPY_ENCLOSE)
            {
                if (info.kind == "wire")
                {
                    opExtractWires(context, id + ("copy_" ~ key), { "edges" : qOwnedByBody(info.body, EntityType.EDGE) });
                    result = qCreatedBy(id + ("copy_" ~ key), EntityType.BODY);
                }
                else
                {
                    opExtractSurface(context, id + ("copy_" ~ key), { "faces" : qOwnedByBody(info.body, EntityType.FACE), "tangentPropagation" : false });
                    result = qCreatedBy(id + ("copy_" ~ key), EntityType.BODY);
                    if (info.kind == "solid" && definition.mode == IdentityLabMode.COPY_ENCLOSE)
                    {
                        opEnclose(context, id + ("enc_" ~ key), { "entities" : result });
                        opDeleteBodies(context, id + ("sheet_" ~ key), { "entities" : result });
                        result = qCreatedBy(id + ("enc_" ~ key), EntityType.BODY);
                    }
                }
            }
            if (definition.purgeTags && result != info.body)
            {
                const copied = qHasAttribute(qUnion([result, qOwnedByBody(result, EntityType.FACE), qOwnedByBody(result, EntityType.EDGE),
                                    qOwnedByBody(result, EntityType.VERTEX)]), "deformRole");
                if (!isQueryEmpty(context, copied))
                {
                    setAttribute(context, { "entities" : copied, "name" : "deformRole", "attribute" : undefined });
                }
            }
            if (nameFor[key] != undefined)
            {
                setProperty(context, { "entities" : result, "propertyType" : PropertyType.NAME, "value" : definition.namePrefix ~ nameFor[key] });
            }
            setAttribute(context, { "entities" : result, "name" : "idlab",
                        "attribute" : { "run" : definition.runLabel, "key" : key, "kind" : info.kind, "rank" : rank, "mode" : toString(definition.mode) } });
            copies = append(copies, result);
            summary = append(summary, key ~ " (" ~ roundToPrecision(info.extent, 9) ~ ")");
        }
        if (definition.deleteInputs && definition.mode != IdentityLabMode.TAG_ONLY)
        {
            opDeleteBodies(context, id + "deleteInputs", { "entities" : wanted });
        }
        reportFeatureInfo(context, id, "Keys: " ~ join(summary, ", ") ~ ".");
    }, { "mode" : IdentityLabMode.COPY_PATTERN, "runLabel" : "", "namePrefix" : "", "names" : "", "purgeTags" : true, "deleteInputs" : false });

/** "key=name;key=name" as a map. */
function parseNames(text is string) returns map
{
    var result = {};
    var rest = text;
    while (rest != "")
    {
        const cut = indexOf(rest, ";");
        const entry = cut < 0 ? rest : substring(rest, 0, cut);
        rest = cut < 0 ? "" : substring(rest, cut + 1);
        const eq = indexOf(entry, "=");
        if (eq > 0)
        {
            result[substring(entry, 0, eq)] = substring(entry, eq + 1);
        }
    }
    return result;
}