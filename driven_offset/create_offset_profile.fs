FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
export import(path : "onshape/std/geometriccontinuity.gen.fs", version : "3083.0");
// IMPORT: Variable_tools V2 extract_outputs.fs (embedStandardOutputs, extractable wrappers)
import(path : "a47f90bfa6b17a59e20cebd0/eb9b32c556ff036c3dd19f73/3cac74f0bc2b98272db13cd3", version : "cffacd73d80aa6dc1a2c4273");
// IMPORT: offset_profile_core.fs (same document; the profile machinery -- enums, dialog predicates, regions /
// points -> pieces, exact curves). export import: the enums are this feature's parameter types.
// PLACEHOLDER: replace path with the new tab's element id and version with its microversion once the tab exists.
export import(path : "9553c095d4d77c83c34a0a36", version : "223aaaeb6c249e3bf29f4710");
// IMPORT: create_offset_profile_icon.svg (feature icon)
IconNamespace::import(path : "5abec4cb3826cb3a41e6e340", version : "dff061882348551c0d057bb6");

/**
 * Create offset profile: builds the profile Driven edge offset and Driven offset surface read -- wires in
 * profile coordinates, X = station, Y = width offset, Z = height offset -- from a table of REGIONS or POINTS.
 * Design: research_create_offset_profile.md.
 *
 * The profile is data only. Where the offset jumps, or is not defined between two items, the profile BREAKS:
 * one wire per continuous piece. Whether and how a break is joined is the consumer's choice. The profile
 * starts at the first item and stops at the last.
 *
 * Every piece is exact: each sub-segment is a polynomial in x (a line, a smootherstep ramp, a Hermite blend),
 * written as an exact Bezier -- no sampling, no approximation tolerance. The sub-segments of a piece are
 * joined into one wire; continuity at each joint is what the inputs give (a buffer into a smooth ramp is
 * C2, a linear corner G0, a blend what was chosen).
 *
 * Any station (region start / end, point station) is a typed value or a picked vertex / mate connector: its
 * world X plus a signed distance along +X. The pick field allows creating a mate connector in place.
 *
 * Regions: start / end station, then either CONSTANT (one width, one height: a straight line) or start / end
 * width and height with a shape (linear or smooth) and optional buffers -- distances from each end toward the
 * centre over which the end value is held. Consecutive
 * regions join as they are when they touch with equal values; otherwise the profile breaks, unless their
 * intersection asks for a blend.
 *
 * Points: station, width and height, and how the profile runs to the next point: linear, smooth (flat at
 * both points, never overshoots), or hold (keep the value, then break). Two points at one station with
 * different values are a break.
 */

// The enums (OffsetProfileMode, OffsetProfileShape, OffsetPointTransition, OffsetStationSource) and
// OFFSET_PROFILE_TOLERANCE live in offset_profile_core, with everything that computes the profile.

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Create offset profile",
        "Feature Type Description" : "Build an offset profile (X station, Y width, Z height) for Driven edge offset / Driven offset surface from regions or points.",
        "Editing Logic Function" : "createOffsetProfileEditingLogic", "Manipulator Change Function" : "createOffsetProfileManipulatorChange" }
export const createOffsetProfile = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Mode", "Default" : OffsetProfileMode.REGIONS, "UIHint" : UIHint.HORIZONTAL_ENUM }
        definition.mode is OffsetProfileMode;

        if (definition.mode == OffsetProfileMode.REGIONS)
        {
            offsetProfileRegionsPredicate(definition);
        }
        else
        {
            offsetProfilePointsPredicate(definition);
        }

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print segments", "Default" : false, "Description" : "Every piece and sub-segment: stations, degree, end values." }
            definition.debugPrint is boolean;
        }
    }
    {
        definition = resolveOffsetProfileStations(context, definition, definition.mode == OffsetProfileMode.REGIONS, true);
        if (definition.mode == OffsetProfileMode.REGIONS)
        {
            // Before building, so the arrows still show when the blend itself is refused.
            addOffsetProfileBlendManipulators(context, id, definition);
        }
        var built;
        if (definition.mode == OffsetProfileMode.REGIONS)
        {
            built = offsetProfileRegionPieces(definition);
        }
        else
        {
            built = offsetProfilePointPieces(definition);
        }
        if (size(built.pieces) == 0)
        {
            throw regenError(definition.mode == OffsetProfileMode.REGIONS ? "Add at least one region." : "Add at least two points at different stations.",
                [definition.mode == OffsetProfileMode.REGIONS ? "regions" : "points"]);
        }

        // The exact curves in memory first (offset_profile_core), then one wire per piece.
        const curves = offsetProfileCurves(built.pieces);
        var wires = [];
        var pieceEnds = [];
        for (var k = 0; k < size(built.pieces); k += 1)
        {
            const pieceResult = buildPiece(context, id + ("piece" ~ k), built.pieces[k], curves[k]);
            wires = append(wires, pieceResult.wire);
            pieceEnds = append(pieceEnds, pieceResult);
        }

        if (definition.debugPrint)
        {
            printOffsetProfilePieces(built.pieces);
        }

        publishProfile(context, id, definition, wires, pieceEnds, built.breaks);
    }, {
        "mode" : OffsetProfileMode.REGIONS,
        "regions" : [],
        "intersections" : [],
        "points" : [],
        "debugPrint" : false
    });

// ============================================================================
// Editing logic and manipulators (offset_profile_core)
// ============================================================================

/**
 * Copies stations picked from points into their value fields (so switching back to Value keeps the number, and
 * item labels show it), names unnamed regions "Region n" and rebuilds the intersection list: one entry per
 * consecutive pair (by start station), each keeping the settings of an existing entry for the same region-name pair.
 */
export function createOffsetProfileEditingLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map, hiddenBodies is Query) returns map
{
    return offsetProfileEditingUpdate(context, definition, definition.mode == OffsetProfileMode.REGIONS);
}

/** Writes a dragged blend arrow back into its intersection's distance. */
export function createOffsetProfileManipulatorChange(context is Context, definition is map, newManipulators is map) returns map
{
    return offsetProfileManipulatorChange(definition, newManipulators);
}

// ============================================================================
// Geometry
// ============================================================================

/**
 * One piece: one exact Bezier per segment (the in-memory curves of offsetProfileCurves, x linear in the
 * parameter), joined into a single wire. Consecutive segments share their joint point exactly.
 *
 * @param pieceCurves {map} : this piece's entry of offsetProfileCurves: { curves, start, end }.
 * @returns {map} : { wire (Query), start, end (Vectors: the piece's end points) }
 */
function buildPiece(context is Context, id is Id, segments is array, pieceCurves is map) returns map
{
    var bodies = [];
    for (var j = 0; j < size(segments); j += 1)
    {
        const segId = id + ("segment" ~ j);
        opCreateBSplineCurve(context, segId, {
                    "bSplineCurve" : pieceCurves.curves[j]
                });
        bodies = append(bodies, qCreatedBy(segId, EntityType.BODY));
    }
    const pieceBodies = qUnion(bodies);
    opExtractWires(context, id + "wire", { "edges" : qOwnedByBody(pieceBodies, EntityType.EDGE) });

    // Show each blend's edge of the finished wire in magenta while editing (found by its midpoint).
    for (var seg in segments)
    {
        if (seg.isBlend == true)
        {
            const xm = (seg.xa + seg.xb) / 2;
            addDebugEntities(context, qContainsPoint(qOwnedByBody(qCreatedBy(id + "wire", EntityType.BODY), EntityType.EDGE),
                        vector(xm, seg.w(xm), seg.h(xm)) * meter), DebugColor.MAGENTA);
        }
    }
    opDeleteBodies(context, id + "deleteSegments", { "entities" : pieceBodies });
    return { "wire" : qCreatedBy(id + "wire", EntityType.BODY), "start" : pieceCurves.start, "end" : pieceCurves.end };
}

// ============================================================================
// Published outputs
// ============================================================================

function publishProfile(context is Context, id is Id, definition is map, wires is array, pieceEnds is array, breaks is array)
{
    var breakStations = [];
    for (var x in breaks)
    {
        breakStations = append(breakStations, x * meter);
    }
    const firstPiece = pieceEnds[0];
    const lastPiece = pieceEnds[size(pieceEnds) - 1];
    const vertexAt = function(wire is Query, point is Vector) returns Query
        {
            return qClosestTo(qOwnedByBody(wire, EntityType.VERTEX), point);
        };

    var breakVertices = [];
    for (var k = 0; k + 1 < size(pieceEnds); k += 1)
    {
        breakVertices = append(breakVertices, vertexAt(pieceEnds[k].wire, pieceEnds[k].end));
        breakVertices = append(breakVertices, vertexAt(pieceEnds[k + 1].wire, pieceEnds[k + 1].start));
    }
    var queries = {
        "startVertex" : extractableQuery(vertexAt(firstPiece.wire, firstPiece.start), "Where the profile starts (lowest station).", DebugColor.GREEN),
        "endVertex" : extractableQuery(vertexAt(lastPiece.wire, lastPiece.end), "Where the profile ends (highest station).", DebugColor.RED),
        "breakVertices" : extractableQuery(qUnion(breakVertices), "The piece ends on both sides of every break.", DebugColor.MAGENTA)
    };
    for (var k = 0; k < size(wires); k += 1)
    {
        queries["piece" ~ (k + 1)] = extractableQuery(wires[k], "Piece " ~ (k + 1) ~ " of the profile, in station order.", DebugColor.CYAN);
    }

    embedStandardOutputs(context, id, {
                "output" : qUnion(wires),
                "outputDescription" : "The offset profile pieces (X station, Y width, Z height)",
                "variables" : {
                    "pieceCount" : extractableVariable(size(wires), "Continuous pieces of the profile."),
                    "breakCount" : extractableVariable(size(breaks), "Breaks between pieces."),
                    "breakStations" : extractableVariable(breakStations, "Station of each break (a jump, or the middle of a gap)."),
                    "startStation" : extractableVariable(firstPiece.start[0], "First station of the profile."),
                    "endStation" : extractableVariable(lastPiece.end[0], "Last station of the profile.")
                },
                "queries" : queries
            });
}
