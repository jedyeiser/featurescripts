# Case pattern vs Query pattern (Evan Reese): which one?

*One page. Query pattern: public document "Query Pattern (Reese)", feature `Query pattern`. Case pattern: our Define case / Case / Close case.*

## In one sentence

- **Query pattern** repeats a feature chain **once per entity** in a set you select. Only one thing changes per repeat: which entity.
- **Case pattern** repeats a feature chain **once per case you define**. Each case can change **several selections and several numbers** at once.

## Side by side

| | Query pattern | Case pattern |
|---|---|---|
| What changes each repeat | one entity (a face, edge, body or vertex) | any number of named selections and typed values (length, angle, area, volume, number, text, boolean) |
| Where the repeats come from | a set of entities you pick (e.g. all faces of a sketch) | cases you write down, one feature per case |
| Setup | one feature: a name (the seed variable), the target entities, the features to loop | Define case (names), the features to repeat, Case per extra case, Close case |
| Per-repeat dimensions (heights, angles, offsets) | not available: take them from the entity or fix them | typed values per case, also `#caseName` and `#caseIndex` |
| Outputs | whatever the repeated features leave in their variables ("last repeat wins") | published per case by name; parts named per case |
| Naming parts | you name them | automatic: `<case>_<output>` |
| Run it again later with more cases | add entities to the target set | add a Case (it can use geometry the first cases made) |
| Edits to geometry that existed before the repeated features | refused inside the pattern (known open issue) | retried outside the pattern frame, so fillet / move face on existing geometry work |
| Learning curve | minutes | an hour: three features and the "function" idea |

## When to use which

**Use Query pattern when** you have a pile of similar entities and one recipe, and the differences come from the entities themselves.
Example: extrude and fillet every face of a sketch, where each face has its own outline.

**Use Case pattern when** each repeat needs its own geometry *and* its own numbers, or several selections, or per-case outputs and part names.
Example: ribs or bosses on different blocks, each with its own height, offset and reference faces.

**Quick test:** if the only thing that changes is *which entity you are on*, use Query pattern. The moment you want a per-repeat number or a second per-repeat selection, use Case pattern.

## Things both share

- The repeated features run once per repeat, so rebuild time grows with the count.
- Both use Onshape's feature-pattern machinery: sheet metal and derived features are refused, and geometry you click from outside the repeated list needs care.
- Reference geometry made *inside* the repeated list through a Query Variable ("created by") follows each repeat; clicked geometry stays on the first one.
- If every repeat is a **transform** of the first (same shape, moved or rotated), use Onshape's own Linear, Circular or Curve pattern instead of either.

## If none of these fit

- One repeat per entity with *different* recipes per entity: Derek Van Allen's Amalgamate.
- Same shape along a path: our Curve pattern plus.
