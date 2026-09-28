# Case pattern, explained

*Define case, Close case and Case pattern (Onshape document **Case_Pattern**). Rewritten 2026-09-26 for v2. The design
note `case_pattern/DESIGN.md` (section 11) and corrections log entry 50 hold the history and the measurements.*

---

## 1. The idea: write the features once, call them per case

You build a chain of features once, for one case, against **named inputs**. Then you repeat that chain for other
cases, each with its own geometry and values. Think of it as a function:

```
Define case  "A"          <- the signature: inputs, shared references, values (case 1's)
  extrude #top by #bossH  <- the body: ordinary features using those names
  fillet ...
  #bossFaces = faces created by Boss
Close case                <- end of the definition: which features, which outputs
Case pattern "B", "C"     <- calls: new selections and values for each case
```

| Function word | Case pattern word | What it is |
|---|---|---|
| parameters | **Inputs** and **Values** of Define case | what changes per case |
| constants the body reads | **Shared references** of Define case | geometry used by every case, picked once |
| local variables | query variables made *inside* the body | recomputed for every case |
| return values | **Outputs** of Close case | published per case as `#<case>_<name>` |
| a call | a **case** in a Case pattern | binds the inputs, runs the body |
| globals | plain variables defined anywhere earlier (`#SW_Width`) | same in every case, usable freely |

It is not Onshape's Pattern: nothing is moved by a transform. Each case re-runs the features with the names bound to
that case. Case 1 is the chain in the tree itself; the Case patterns add the others.

## 2. The two rules

**Rule 1 -- geometry from before Define case comes in by name.** Anything the body uses that exists before the Define
case -- the surface to offset, a mate connector used as a side reference, a plane -- must reach the body as an
**input** (it changes per case) or a **shared reference** (it is the same for every case). Never click it inside a
repeated feature.

*Why:* the cases run inside Onshape's pattern frame, and inside it Onshape refuses a clicked selection of geometry from
before the repeated features. Native features mostly cope; custom features see an empty selection. Offset+ used to
read that as "no side reference" and quietly offset along the surface normals -- right for some cases, flipped for
others. Reference_Side (from V5) now stops with an error instead: "The reference is selected but cannot be read here.
Inside a Case pattern, pass it in through a Define case input (#name)...". Inputs and shared references are resolved
before the frame, so they always work.

**Rule 2 -- geometry made inside the body is referenced through a query variable.** A feature in the body that needs
geometry another body feature made must get it through a native Query Variable, e.g. "created by Boss". A click (or
`qCreatedBy(makeId(...))`) always points at case 1's copy.

*Values are different:* numbers, lengths, angles, booleans are not geometry, so plain variables from anywhere earlier
work inside the body. Put a value in Define case only if it **changes per case**.

## 3. Define case

| Parameter | Meaning |
|---|---|
| **Case 1 name** | The name of the case the tree itself builds (letters, digits, `_`; starts with a letter). |
| **Inputs** | Up to 8 query variables that change per case: a name and case 1's selection. |
| **Shared references** | Query variables that are the same for every case: a name and a selection. No per-case slot. |
| **Values** | Up to 6 variables that change per case: a name, a type and case 1's value. Types: Length, Angle, Area (mm^2), Volume (mm^3), Number, Integer, Text, Boolean. A Boolean is typed as an expression (`true`, `false`, `!#other`). |

It also sets `#caseName` and `#caseIndex` (1 for case 1), which every case updates -- handy for part names or a
per-case switch.

## 4. The body

Ordinary features using the names. Useful patterns:

- **Switch features per case** with a Boolean value: suppress a feature by expression (`#pin`). The suppression is
  re-checked for every case (test T6). Case 1 must be the variant you want in the tree.
- **Numeric fields** take expressions: `#flag ? 3 mm : 0 mm`.
- **Name parts** by giving them an output: with *Name parts after outputs* (Close case) the part is named like the
  output's variable (`<case>_<output>`), in every case. To name parts yourself instead, turn it off and rename
  inside the body (e.g. with `#caseName`).

## 5. Close case

| Parameter | Meaning |
|---|---|
| **Define case** | The Define case this body belongs to. |
| **Features to repeat** | The body's features, in tree order. |
| **Outputs** | Query variables every case publishes as `#<case>_<name>`, case 1 included (`#A_rib`, `#B_rib`...). **Query**: usually a query variable made inside the body. **Evaluate on use** off (default): the entities are fixed when the case closes and follow identity-preserving edits; **Track downstream changes** also follows entities later derived from them. On: the query is stored and re-evaluated wherever the variable is used. |
| **Keep** | Which new bodies each case keeps: parts, surfaces, curves and points, mate connectors, planes (on); sketches (off). |
| **Name parts after outputs** | On (default): a part an output points to is named like the output's variable -- output `rib` gives the part `B_rib` in case B and `A_rib` in case 1 (a second part of the same output gets `_2`, ...). Parts no output points to keep their names. Off: names are left to the body. |

## 6. Case pattern

| Parameter | Meaning |
|---|---|
| **Close case** | The Close case to repeat. Picking it lays out the first case. |
| **Update from Define case** | Re-lays out every case after inputs or values were added, removed, renamed or retyped in the Define case. Selections and values are kept by name. (Onshape runs the dialog logic only on a change, so opening the dialog is not enough.) |
| **Cases** | Collapsed rows, usually one. Each: a case name, a selection for each input, a value for each value (new rows start with case 1's values). |
| **Print bindings** | Print each case's selections and values to the FeatureScript notices. |

**One pattern or several?** The rebuild cost is the same (each case runs the body once). Group independent cases in one
pattern; give a case its own pattern when it needs its own suppression or configuration, its own place in the tree,
or when a later case uses its result (test T7).

**Messages.** Warning "Not built: B: ..." -- the other cases are still built. Error "No case was built" when all fail.
Typical causes: `#x selects nothing (click Update from Define case...)`, `#x has no value (...)`, a case name used
twice, `repeated feature 3 failed (...)` with Onshape's own reason.

## 7. Example (Case pattern tests, T1 and T10)

**T1.** Three blocks A, B, C. Define case inputs `#top`, `#rim`; values `#bossH`, `#edgeR`. Body: Boss (extrude `#top`
by `#bossH`), a rim fillet on the block (geometry from before the Define case, reached through `#rim`), `#bossEdges` =
edges created by Boss, a fillet of `#bossEdges` at `#edgeR`, `#bossFaces` = faces created by Boss. Close case output
`bossFaces`. Case patterns B (25 mm, R3) and C (8 mm, R2) give `Boss_B` and `Boss_C`, filleted, with `#A_bossFaces`,
`#B_bossFaces`, `#C_bossFaces` on the right bosses.

**T10.** An Offset+ in the body offsets the input face `#wall10` toward `#ref10`, a **shared reference** (a corner of
block A). One Case pattern with cases B and C: both faces move toward the reference, although their normals point
different ways -- and nobody picked the reference per case.

The studio holds T1-T11; `devtools/onshape/check_case_pattern_tests.py` checks them all.

## 7b. Keeping the variable list short

Every query variable name stays in the list for the rest of the tree -- FeatureScript cannot delete one (overwriting
it with nothing still leaves the name). So the aim is fewer names, reused.

- **Local names: short, by role, prefixed `_`, reused in every body.** A query variable made inside the repeated
  features (Rule 2: "created by Boss") is a local: only that body uses it. Name it by what it is for -- `_body`,
  `_copy`, `_trim`, `_edges`, `_out` -- and use the same few names in every template. Each body redefines them, so
  ten templates share five local names instead of adding five each, and the `_` keeps them together, apart from the
  names other features use. This is safe: after a Case pattern a local holds the last case's value, and nothing
  outside the body should use it.
- **Outputs only for what is used later.** Every Close case output makes one name per case (`#B_rib`, `#C_rib`, ...).
  Declare only the outputs a later feature needs; everything else stays local.
- **Output names are plain text.** Type `body`, not `#body`: in an Onshape text field a leading `#` makes the field
  a reference to a variable, so the output is named after whatever that variable holds (seen on RD 20FOU 28: an
  output named `#name` picked up a Text value `name` and every case published `..._core_3d_glass_periphery`).
- **Values: specific names.** A value called `name` or `offset` is easy to pick up by accident; `bodyName`,
  `middleOffset` are not.
- **Let composite features carry the pieces.** A feature that publishes its own pieces (Join profile surfaces: its
  faces and joint edges) replaces a chain of helper query variables; extract only the one or two keys you use.
- **Tree:** give each template (Define case, body, Close case, Case patterns) its own feature-tree folder.

## 8. Limits

- **Cases run in order and see earlier results**: a case can consume what a later case selects.
- **A failed case cannot undo its edits to existing geometry**; only its new bodies are removed.
- **Edits to geometry from before the body** (a fillet on an existing block, Move face) run outside the pattern frame
  on Onshape's refusal; fillet and move face are verified, others (chamfer, shell, draft, booleans into existing
  parts) are not.
- **Sketches** in the body are re-solved per case, but constraints to the origin or default planes are not reapplied.
  Build sketches on geometry derived from the inputs. Untested live.
- **Not in a pattern**: sheet metal and derived features.
- **Cost**: every rebuild runs the body once per case.
- **Dialog labels**: after *Update from Define case* the grey slot labels may show old names until the dialog is
  reopened; the stored values are right.

## 9. The code

`case_pattern/case_pattern.fs` holds the three features. Define case binds case 1 and publishes a hidden signature.
Close case copies it with the feature list and outputs; when a Case pattern calls it, it replays the body in its own
pattern frame (stepping out of it for edits to existing geometry) and records the new bodies. Case pattern binds each
case -- inputs and shared references resolved *before* the frame -- calls the Close case, reads the outputs with a
second call, names and filters the new bodies, publishes the outputs and restores case 1. Why it is built this way,
with every dead end measured: corrections log entry 50.
