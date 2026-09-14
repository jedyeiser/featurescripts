FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

// getQueryVariable / setQueryVariable live in their own module, which common.fs does not
// re-export -- the same gap as ProjectionType in evaluate_profiles.
import(path : "onshape/std/queryVariable.fs", version : "3070.0");

/**
 *
 * This contains two functions that create and derive 'packages'. This extends the idea Greg Brown (Onshape VP of Product) laid out with his publish feature: https://k2-sports.onshape.com/documents/40d43cad542dccfa4772d7e1/v/856bd2b631f7e35c4a2b34ad/e/788996d08647863b81b2ff61
 *
 * The prevailing goal of this effort is to make transfering information/data from one part studio as robust, dynamic and flexible as possible.
 * The built-in Derive does not support query variables. One key differentiator of our export/package functionality is that we WILL 'create' or 'persist' query variables,
 * meaning that data set in the document being exported can be used in the imported/derived context
 *
 * A package is a collection of bodies, sketches, mate connectors, composite parts and query variables.
 *
 * Creating a package creates a composite part with any additional data set as an attribute on the created composite part for extraction when being imported.
 *
 * Sketch section - User selects sketches to bring into the new context.
 * Body Section - User selects bodies to bring into new context
 * Mate Connector Section - User selects mate connectors to bring into new context. Note that we may need to deal with special cases (mate connector has no owner body. Mate connector owner body is not part of export
 * Query Variable Section - User selects/creates query variables to bring into and use in the new context
 *  Existing - User selects existing query variables (query input parameter). These are recorded by name and brought into the new context (if possible)
 *  New - Array variable where user creates new query variables using a query variable predicate.
 * Notes - Any export notes
 *
 *
 * Deriving a package tests to see if a composite part with the required attributes is selected. Should be have like a standard Derive otherwise (when no composite part of the right type) is selected
 * If a composite part of the right type is selected, bring in export data (more than just the composite part - we have sketches and query variables). Show debug shows variables. Debug color code query variables.
 *
 *
 */

// ============================================================================
// Status
// ============================================================================
//
// Built: the package schema, witness capture and resolution, and Create package.
//
// Not built yet: Derive package. Its reading half is here and exported --
// readPackage and resolveWitness are what it will call -- but wrapping importDerived
// and republishing the query variables is its own piece of work.

// ============================================================================
// Schema
// ============================================================================

/**
 * Attribute name carried by a package's composite part.
 *
 * Deriving looks for exactly this. A composite part without it is not a package, and the
 * derive is expected to fall back to behaving like a plain Derive rather than complain.
 */
export const PACKAGE_ATTRIBUTE = "eocExportPackage";

/**
 * Schema version of the attribute payload.
 *
 * Additive changes keep the number; anything that changes the meaning or shape of an
 * existing field bumps it. A reader checks the prefix and refuses an unknown version rather
 * than guessing, because a half-understood package is worse than a rejected one.
 */
export const PACKAGE_SCHEMA = "exportPackage/1";

/** How close a witness point must land to count as identifying its entity. */
export const PACKAGE_WITNESS_TOL = 1e-7 * meter;

/**
 * Where a package's coordinates are measured from.
 *
 * Every witness point is stored in this frame rather than in world coordinates, which is
 * what lets a package be derived at the target origin or at a mate connector without the
 * stored points going stale. Derive already knows which it is -- importDerived takes exactly
 * this choice as its placement -- so the transform never has to be inferred from geometry.
 */
export enum PackageReference
{
    annotation { "Name" : "Part Studio origin" }
    ORIGIN,
    annotation { "Name" : "Mate connector" }
    MATE_CONNECTOR
}

/**
 * What kind of entity a stored query variable points at.
 *
 * Recorded because resolution differs: a body is found directly, while a face, edge or
 * vertex has to be found among the entities its owner body brought with it.
 */
export enum PackageEntityType
{
    annotation { "Name" : "Bodies" }
    BODY,
    annotation { "Name" : "Faces" }
    FACE,
    annotation { "Name" : "Edges" }
    EDGE,
    annotation { "Name" : "Vertices" }
    VERTEX
}

// ============================================================================
// Create package
// ============================================================================

annotation { "Feature Type Name" : "Create package",
        "Feature Type Description" : "Gather bodies, sketches, mate connectors and query variables into one composite part that can be derived with its data intact.",
        "Editing Logic Function" : "packageEditLogic" }
export const createPackage = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Package name", "Description" : "Names the composite part and prefixes the query variables recreated when it is derived." }
        definition.packageName is string;

        annotation { "Name" : "Bodies", "Filter" : EntityType.BODY && ConstructionObject.NO }
        definition.bodies is Query;

        annotation { "Name" : "Composite parts", "Filter" : EntityType.BODY && BodyType.COMPOSITE }
        definition.compositeParts is Query;

        // Sketch geometry is faces and edges, never bodies -- EntityType.BODY here is why
        // nothing was selectable. The owning body is what joins the package; these are
        // what the user actually points at.
        annotation { "Name" : "Sketches", "Filter" : (EntityType.FACE || EntityType.EDGE) && SketchObject.YES }
        definition.sketches is Query;

        annotation { "Name" : "Mate connectors", "Filter" : BodyType.MATE_CONNECTOR }
        definition.mateConnectors is Query;

        annotation { "Name" : "Measure from", "Default" : PackageReference.ORIGIN, "UIHint" : UIHint.SHOW_LABEL, "Description" : "The zero every stored point is measured from. The origin needs nothing else; a mate connector lets the package be derived anywhere and still resolve." }
        definition.referenceKind is PackageReference;

        if (definition.referenceKind == PackageReference.MATE_CONNECTOR)
        {
            annotation { "Name" : "Reference mate connector", "Filter" : BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            definition.referenceConnector is Query;
        }

        // Filled in by packageEditLogic, not by hand: every query variable the feature can
        // see in this Part Studio appears here with a tick box. Read-only so the list stays
        // a report of what exists rather than somewhere to invent a name that does not.
        annotation { "Name" : "Query variables found here", "Item name" : "variable",
                    "Item label template" : "#discoveredName", "UIHint" : UIHint.PREVENT_ARRAY_REORDER }
        definition.discoveredVariables is array;
        for (var entry in definition.discoveredVariables)
        {
            annotation { "Name" : "Variable", "MaxLength" : 256, "UIHint" : [UIHint.READ_ONLY, UIHint.UNCONFIGURABLE] }
            entry.discoveredName is string;

            annotation { "Name" : "Include", "Default" : true }
            entry.keep is boolean;

            annotation { "Name" : "Rename", "Default" : false }
            entry.rename is boolean;

            if (entry.rename)
            {
                // Not "variableName": parameter names must be unique across the entire
                // precondition, and the New array already claims that one.
                annotation { "Name" : "Name in the derived context", "MaxLength" : 256 }
                entry.renamedTo is string;
            }
        }

        annotation { "Name" : "New query variables", "Item name" : "query variable", "Item label template" : "#variableName" }
        definition.newVariables is array;
        for (var entry in definition.newVariables)
        {
            annotation { "Name" : "Name", "MaxLength" : 256, "Description" : "The name this becomes in the derived context." }
            entry.variableName is string;

            // Every entity kind on purpose: a query variable may name a body, a face, an
            // edge or a vertex. A Query parameter must still declare a filter or Onshape
            // rejects the precondition. Existing query variables appear in this dropdown
            // too, which is the other way to reuse one.
            annotation { "Name" : "Entities", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX }
            entry.entities is Query;

            annotation { "Name" : "Description", "MaxLength" : 256 }
            entry.description is string;
        }

        annotation { "Name" : "Notes", "MaxLength" : 4096, "Description" : "Anything the receiving context should know. Carried with the package and printed when it is derived." }
        definition.notes is string;

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print package", "Default" : false, "Description" : "Report what went into the package and where each witness point landed." }
            definition.debugPrint is boolean;

            annotation { "Name" : "Print variables", "Default" : false, "Description" : "List the variables this feature can see in the Part Studio, and say whether each reused name resolved." }
            definition.debugPrintVariables is boolean;
        }
    }
    {
        // opCreateCompositePart takes bodies, and only real ones. A mate connector is a
        // body by type but not a valid member -- passing one is what made it reject the
        // input. A sketch region is a face, so its owning body is what joins instead. And
        // nested composites are flattened: their grouping is recorded in the attribute, so
        // nothing is lost by handing the kernel their constituent bodies.
        const carried = qUnion([
                    definition.bodies,
                    qFlattenedCompositeParts(definition.compositeParts),
                    qOwnerBody(definition.sketches)
                ]);
        const members = qBodyType(carried, [BodyType.SOLID, BodyType.SHEET]);

        if (isQueryEmpty(context, members))
        {
            throw regenError("A package needs at least one body, composite part or sketch. "
                ~ "Mate connectors alone cannot carry a package: the attribute lives on a "
                ~ "composite part, and a composite part needs at least one body.",
                ["bodies", "compositeParts", "sketches"]);
        }

        const reference = packageReferenceFrame(context, definition);
        const payload = buildPackagePayload(context, id, definition, reference);

        // The composite part is the carrier: one selectable thing that survives Derive and
        // has somewhere to hang an attribute. Left open rather than closed so its members
        // stay individually selectable in the source studio.
        opCreateCompositePart(context, id + "package", { "bodies" : members });

        const composite = qCreatedBy(id + "package", EntityType.BODY);

        setAttribute(context, {
                    "entities" : composite,
                    "name" : PACKAGE_ATTRIBUTE,
                    "attribute" : payload
                });

        if (definition.packageName != "")
        {
            setProperty(context, {
                        "entities" : composite,
                        "propertyType" : PropertyType.NAME,
                        "value" : definition.packageName
                    });
        }

        if (definition.debugPrintVariables)
        {
            printAvailableVariables(context, definition);
        }

        if (definition.debugPrint)
        {
            printPackage(payload);
        }
    }, {
        "packageName" : "",
        "bodies" : qNothing(),
        "compositeParts" : qNothing(),
        "sketches" : qNothing(),
        "mateConnectors" : qNothing(),
        "referenceKind" : PackageReference.ORIGIN,
        "referenceConnector" : qNothing(),
        "discoveredVariables" : [],
        "newVariables" : [],
        "notes" : "",
        "debugPrint" : false,
        "debugPrintVariables" : false
    });

/**
 * Keep the discovered-variable list in step with the Part Studio.
 *
 * Runs whenever the dialog opens or a parameter changes. It rebuilds the list from what is
 * actually in the context and carries the user's choices across by name, so ticking a box
 * survives a variable being added or removed elsewhere -- which a positional match would
 * not, since the discovered order is not ours to control.
 *
 * A variable that has disappeared drops off the list rather than lingering as a stale entry
 * the user would have to clear by hand.
 */
export function packageEditLogic(context is Context, id is Id, oldDefinition is map,
    definition is map, isCreating is boolean, specifiedParameters is map) returns map
{
    var previous = {};

    for (var entry in definition.discoveredVariables)
    {
        previous[entry.discoveredName] = entry;
    }

    var rebuilt = [];

    for (var name in discoverQueryVariables(context))
    {
        const kept = previous[name];

        if (kept != undefined)
        {
            rebuilt = append(rebuilt, mergeMaps(kept, { "discoveredName" : name }));
            continue;
        }

        rebuilt = append(rebuilt, {
                    "discoveredName" : name,
                    "keep" : true,
                    "rename" : false,
                    "renamedTo" : ""
                });
    }

    return mergeMaps(definition, { "discoveredVariables" : rebuilt });
}

/**
 * Every query variable the context will admit to having.
 *
 * getAllVariables is the only enumeration std offers, and it is @internal. Whether query
 * variables appear in it is the open question this whole list depends on: setQueryVariable
 * writes through a separate builtin, and queryVariable.fs treats a name that getVariable can
 * find as proof the name belongs to a NON-query variable. So they are probably invisible
 * here, in which case this returns nothing and the New array below is the way in.
 *
 * Filtering on the value being a Query rather than on the name is what makes it correct
 * either way -- and it also catches an ordinary variable someone has parked a query in.
 */
function discoverQueryVariables(context is Context) returns array
{
    var found = [];

    for (var entry in getAllVariables(context))
    {
        if (entry.value is Query)
        {
            found = append(found, entry.key);
        }
    }

    return found;
}

/**
 * The frame every stored point is measured from.
 *
 * Returning a CoordSystem either way keeps the rest of the code free of the distinction:
 * the origin is simply the identity frame.
 */
function packageReferenceFrame(context is Context, definition is map) returns CoordSystem
{
    if (definition.referenceKind == PackageReference.ORIGIN)
    {
        return coordSystem(vector(0, 0, 0) * meter, vector(1, 0, 0), vector(0, 0, 1));
    }

    if (isQueryEmpty(context, definition.referenceConnector))
    {
        throw regenError("Select the mate connector the package is measured from.",
            ["referenceConnector"]);
    }

    return evMateConnector(context, { "mateConnector" : definition.referenceConnector });
}

/**
 * Everything the derived context will need, as a plain map.
 *
 * Plain on purpose: an attribute survives Derive, but nothing about that guarantees a type
 * tag from this Feature Studio means anything on the other side. Scalars, strings, vectors
 * and nested maps are safe; a tagged value is a bet.
 */
function buildPackagePayload(context is Context, id is Id, definition is map,
    reference is CoordSystem) returns map
{
    var variables = [];

    for (var entry in definition.discoveredVariables)
    {
        if (entry.discoveredName == "" || !entry.keep)
        {
            continue;
        }

        // Resolved again at regeneration rather than trusted from the edit: the list was
        // built when the dialog opened, and the studio may have moved on since.
        const entities = getQueryVariable(context, entry.discoveredName);

        if (isQueryEmpty(context, entities))
        {
            reportFeatureInfo(context, id,
                "Query variable '" ~ entry.discoveredName ~ "' resolved to nothing and was not packaged.");
            continue;
        }

        const named = (entry.rename && entry.renamedTo != "") ? entry.renamedTo : entry.discoveredName;

        variables = append(variables, recordQueryVariable(context, {
                        "variableName" : named,
                        "entities" : entities,
                        "description" : "Reused from '" ~ entry.discoveredName ~ "'."
                    }, reference));
    }

    for (var entry in definition.newVariables)
    {
        if (entry.variableName == "" || isQueryEmpty(context, entry.entities))
        {
            continue;
        }

        variables = append(variables, recordQueryVariable(context, entry, reference));
    }

    return {
        "schema" : PACKAGE_SCHEMA,
        "name" : definition.packageName,
        "notes" : definition.notes,
        "referenceKind" : (definition.referenceKind == PackageReference.ORIGIN) ? "origin" : "mateConnector",
        "bodies" : recordBodies(context, definition.bodies, reference),
        "compositeParts" : recordBodies(context, qFlattenedCompositeParts(definition.compositeParts), reference),
        "sketches" : recordBodies(context, qOwnerBody(definition.sketches), reference),
        "mateConnectors" : recordConnectors(context, definition.mateConnectors, reference),
        "queryVariables" : variables
    };
}

/**
 * A witness point for every body, in the reference frame.
 *
 * A vertex is used rather than a centroid: a centroid can sit outside a sheet or a concave
 * solid, and a point that is not ON the body cannot identify it afterwards. Which vertex is
 * irrelevant -- only its position is recorded, and query evaluation order is explicitly
 * unpredictable, so no index could be trusted anyway.
 */
function recordBodies(context is Context, bodies is Query, reference is CoordSystem) returns array
{
    var out = [];

    for (var body in evaluateQuery(context, bodies))
    {
        const witness = bodyWitness(context, body);

        if (witness == undefined)
        {
            continue;
        }

        out = append(out, { "witness" : fromWorld(reference, witness) });
    }

    return out;
}

/**
 * Mate connectors, stored as their frame rather than a point: a connector carries an
 * orientation, and a point alone would lose it.
 */
function recordConnectors(context is Context, connectors is Query, reference is CoordSystem) returns array
{
    var out = [];

    for (var connector in evaluateQuery(context, connectors))
    {
        const cSys = evMateConnector(context, { "mateConnector" : connector });

        // Only the linear part for the axes: a Transform applied to a Vector translates
        // it, which is right for a position and meaningless for a direction.
        const intoReference = fromWorld(reference);

        out = append(out, {
                    "origin" : fromWorld(reference, cSys.origin),
                    "xAxis" : intoReference.linear * cSys.xAxis,
                    "zAxis" : intoReference.linear * cSys.zAxis
                });
    }

    return out;
}

/**
 * One query variable, as the owning body plus a witness per entity.
 *
 * The owner body is recorded alongside each entity because that is what narrows the search
 * on the other side: qOwnedByBody gives the candidates and the witness picks one out. Going
 * straight to a witness against every entity in the context would work too, and would be
 * both slower and easier to fool where two bodies touch.
 */
function recordQueryVariable(context is Context, entry is map, reference is CoordSystem) returns map
{
    const resolved = evaluateQuery(context, entry.entities);
    const kind = packageEntityKind(context, entry.entities);

    var witnesses = [];

    for (var entity in resolved)
    {
        const point = entityWitness(context, entity, kind);

        if (point == undefined)
        {
            continue;
        }

        var record = { "witness" : fromWorld(reference, point) };

        if (kind != PackageEntityType.BODY)
        {
            const owner = bodyWitness(context, qOwnerBody(entity));
            if (owner != undefined)
            {
                record = mergeMaps(record, { "owner" : fromWorld(reference, owner) });
            }
        }

        witnesses = append(witnesses, record);
    }

    return {
        "name" : entry.variableName,
        "description" : entry.description,
        "entityType" : packageEntityName(kind),
        "witnesses" : witnesses
    };
}

/**
 * Which entity type a selection is, taken from the first thing it resolves to.
 *
 * A query variable that mixes types is not something the derived context could rebuild
 * coherently, so the first entity settles it and the rest are filtered to match.
 */
function packageEntityKind(context is Context, entities is Query) returns PackageEntityType
{
    if (!isQueryEmpty(context, qEntityFilter(entities, EntityType.VERTEX)))
    {
        return PackageEntityType.VERTEX;
    }
    if (!isQueryEmpty(context, qEntityFilter(entities, EntityType.EDGE)))
    {
        return PackageEntityType.EDGE;
    }
    if (!isQueryEmpty(context, qEntityFilter(entities, EntityType.FACE)))
    {
        return PackageEntityType.FACE;
    }

    return PackageEntityType.BODY;
}

/**
 * The stored spelling of an entity type. A string, not the enum: the reader may be a later
 * schema version, or the API, and neither should need this Feature Studio in scope.
 */
function packageEntityName(kind is PackageEntityType) returns string
{
    if (kind == PackageEntityType.VERTEX)
    {
        return "vertex";
    }
    if (kind == PackageEntityType.EDGE)
    {
        return "edge";
    }
    if (kind == PackageEntityType.FACE)
    {
        return "face";
    }

    return "body";
}

/**
 * A point known to lie on a body, or undefined when it has no vertices to offer.
 */
function bodyWitness(context is Context, body is Query)
{
    const vertices = evaluateQuery(context, qOwnedByBody(body, EntityType.VERTEX));

    if (size(vertices) == 0)
    {
        return undefined;
    }

    return evVertexPoint(context, { "vertex" : vertices[0] });
}

/**
 * A point known to lie on one entity.
 *
 * Faces and edges are sampled at their middle rather than at a corner: a corner is shared
 * with the neighbours, so a witness there would identify several entities at once and
 * qContainsPoint would have no way to choose.
 */
function entityWitness(context is Context, entity is Query, kind is PackageEntityType)
{
    if (kind == PackageEntityType.VERTEX)
    {
        return evVertexPoint(context, { "vertex" : entity });
    }

    if (kind == PackageEntityType.EDGE)
    {
        return evEdgeTangentLines(context, { "edge" : entity, "parameters" : [0.5] })[0].origin;
    }

    if (kind == PackageEntityType.FACE)
    {
        return faceWitness(context, entity);
    }

    return bodyWitness(context, entity);
}

/**
 * A point on a face, away from its boundary.
 *
 * The middle of parameter space is tried first and is right for nearly every face. It can
 * fall outside a trimmed one, though -- an annulus has a hole exactly there -- so a few
 * other spots are tried before giving up. returnUndefinedOutsideFace is what makes that
 * detectable rather than silently returning a point in the hole.
 *
 * Boundaries are avoided deliberately: a point on an edge belongs to both adjacent faces,
 * and a witness that identifies two faces identifies neither.
 */
function faceWitness(context is Context, face is Query)
{
    const spots = [
            vector(0.5, 0.5),
            vector(0.25, 0.25),
            vector(0.75, 0.25),
            vector(0.25, 0.75),
            vector(0.75, 0.75),
            vector(0.5, 0.25),
            vector(0.5, 0.75)
        ];

    const planes = evFaceTangentPlanes(context, {
                "face" : face,
                "parameters" : spots,
                "returnUndefinedOutsideFace" : true
            });

    for (var plane in planes)
    {
        if (plane != undefined)
        {
            return plane.origin;
        }
    }

    return undefined;
}

// ============================================================================
// Reading a package
// ============================================================================

/**
 * The payload carried by a composite part, or undefined when it is not a package.
 *
 * Undefined rather than an error: the derive is meant to fall back to behaving like a plain
 * Derive when handed an ordinary composite part, so "not a package" is an ordinary answer.
 */
export function readPackage(context is Context, composite is Query)
{
    const payload = getAttribute(context, {
                "entity" : composite,
                "name" : PACKAGE_ATTRIBUTE
            });

    if (payload == undefined || !(payload is map))
    {
        return undefined;
    }

    if (payload.schema != PACKAGE_SCHEMA)
    {
        throw regenError("This package was written by a different version of Create package ("
            ~ toString(payload.schema) ~ "; this reads " ~ PACKAGE_SCHEMA
            ~ "). Recreate it, or use a matching version.", composite);
    }

    return payload;
}

/**
 * Find the entity a stored witness point identifies, in whatever context it now lives.
 *
 * The point is in the package's reference frame, so `placement` is wherever that frame
 * landed -- the target origin for an at-origin derive, or the chosen mate connector's frame
 * otherwise. Derive knows which without having to measure anything.
 *
 * qContainsPoint first because it is exact; qClosestTo only as a fallback, since a copied
 * body is geometrically identical and a witness that misses by more than rounding means
 * something is wrong rather than merely imprecise.
 */
export function resolveWitness(context is Context, candidates is Query, stored is Vector,
    placement is CoordSystem) returns Query
{
    const point = toWorld(placement, stored);
    const exact = qContainsPoint(candidates, point);

    if (!isQueryEmpty(context, exact))
    {
        return exact;
    }

    const nearest = qClosestTo(candidates, point);

    if (isQueryEmpty(context, nearest))
    {
        return qNothing();
    }

    return nearest;
}

// ============================================================================
// Debug
// ============================================================================

/**
 * What this feature can see in the Part Studio, and whether each reused name resolved.
 *
 * Onshape has no way to enumerate query variables -- getAllVariables covers ordinary ones
 * only, and there is no getAllQueryVariables -- so the list below is the ordinary variables
 * plus a resolved/not-resolved verdict on each name actually asked for. That is the whole
 * of the available insight, and pretending otherwise would be worse than saying so.
 *
 * The other way to find a query variable is to open the Entities dropdown under "New query
 * variables": Onshape surfaces query variables there natively, alongside geometry.
 */
function printAvailableVariables(context is Context, definition is map)
{
    println("");
    println("========== create package: variables ==========");

    const ordinary = getAllVariables(context);
    const names = keys(ordinary);

    println("[package] ordinary variables in context: " ~ toString(size(names)));
    for (var name in names)
    {
        println("[package]   #" ~ name);
    }

    println("[package] query variables cannot be listed by any std function; each reused");
    println("[package] name below was resolved individually.");

    println("[package] query variables discovered: "
        ~ toString(size(definition.discoveredVariables)));

    for (var entry in definition.discoveredVariables)
    {
        const found = !isQueryEmpty(context, getQueryVariable(context, entry.discoveredName));
        println("[package]   '" ~ entry.discoveredName ~ "': "
            ~ (found ? "resolved" : "NOT FOUND") ~ (entry.keep ? ", included" : ", skipped"));
    }

    println("========== end variables ==========");
    println("");
}

/**
 * What went into the package.
 */
function printPackage(payload is map)
{
    println("");
    println("========== create package: " ~ payload.name ~ " ==========");
    println("[package] measured from the " ~ payload.referenceKind);
    println("[package] bodies: " ~ toString(size(payload.bodies))
        ~ ", from composites: " ~ toString(size(payload.compositeParts))
        ~ ", sketch bodies: " ~ toString(size(payload.sketches))
        ~ ", mate connectors: " ~ toString(size(payload.mateConnectors)));

    for (var variable in payload.queryVariables)
    {
        println("[package]   query variable \"" ~ variable.name ~ "\": "
            ~ toString(size(variable.witnesses)) ~ " " ~ variable.entityType ~ "(s)");
    }

    if (payload.notes != "")
    {
        println("[package] notes: " ~ payload.notes);
    }

    println("========== end create package ==========");
    println("");
}
