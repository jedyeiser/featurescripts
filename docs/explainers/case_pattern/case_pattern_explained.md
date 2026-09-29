# Case pattern, explained

*Define case, Case and Close case (Onshape document **Case_Pattern**). Rewritten 2026-09-29 for v3. The design note
`case_pattern/DESIGN.md` (sections 11-12) and corrections log entries 41, 50 and 60 hold the history and the
measurements.*

---

## 1. The idea: write the features once, call them per case

You build a chain of features once, for one case, against **named inputs**. Then you repeat that chain for other
cases, each with its own geometry and values. Think of it as a function:

```
Define case  "A"          <- the signature: inputs, shared references, values (case 1's)
  extrude #top by #bossH  <- the body: ordinary features using those names
  fillet ...
  #bossFaces = faces created by Boss
Case "B", Case "C"        <- the arguments of each further call: new selections and values
Close case                <- runs it: the body once more per Case; publishes the outputs
```

| Function word | Case pattern word | What it is |
|---|---|---|
| parameters | **Inputs** and **Values** of Define case | what changes per case |
| constants the body reads | geometry from before the body, clicked directly (or a **Shared reference**) | the same in every case |
| local variables | query variables made *inside* the body | recomputed for every case |
| return values | **Outputs** of Close case | published per case as `#<case>_<name>` |
| a call | a **Case** feature, run by a Close case | binds the inputs, runs the body |
| globals | plain variables defined anywhere earlier (`#SW_Width`) | same in every case, usable freely |

It is not Onshape's Pattern: nothing is moved by a transform. Each case re-runs the features with the names bound to
that case. Case 1 is the chain in the tree itself; the Close case adds the others.

## 2. The one rule

**Geometry made inside the body is referenced through a query variable.** A feature in the body that needs geometry
another body feature made must get it through a native Query Variable, e.g. "created by Boss". A click (or
`qCreatedBy(makeId(...))`) always points at case 1's copy (test T3 shows the error you get).

**Geometry from before the body may be clicked directly.** A side reference, a mate connector, a face to extrude up
to, a plane: click it in the repeated feature as you would anywhere else; every case uses the same one (tests T9a,
T13, T14). Put it in Define case only if it **changes per case** (an input), or if several body features share it and
you want one place to change it (a shared reference).

*Values are different:* numbers, lengths, angles, booleans are not geometry, so plain variables from anywhere earlier
work inside the body. Put a value in Define case only if it **changes per case**.

> **Why v2 had a second rule, and why it is gone.** In v2 a *Case pattern* feature, placed after the Close case, called
> the Close case, which then replayed the body. A feature function called that way, from inside another feature,
> loses every click on geometry from before it: the selections resolved to nothing. Offset+ lost its side reference,
> an extrude "up to face" lost its face. Onshape's own Pattern never does this because the feature that replays the
> features *owns* their list and pushes the pattern frame itself. v3 does the same: the Close case owns the body and
> runs every case. Measured row by row in the "Case pattern outside-ref spikes" studio (correction 60).

## 3. Define case

| Parameter | Meaning |
|---|---|
| **Case 1 name** | The name of the case the tree itself builds (letters, digits, `_`; starts with a letter). |
| **Inputs** | Up to 8 query variables that change per case: a name and case 1's selection. |
| **Shared references** | Optional. Query variables that are the same for every case: a name and a selection, picked once. Clicking the geometry directly in the body does the same; a shared reference is just one place to change it. |
| **Values** | Up to 6 variables that change per case: a name, a type and case 1's value. Types: Length, Angle, Area (mm^2), Volume (mm^3), Number, Integer, Text, Boolean. A Boolean is typed as an expression (`true`, `false`, `!#other`). |

It also sets `#caseName` and `#caseIndex` (1 for case 1), which every case updates -- handy for part names or a
per-case switch.

## 4. The body

Ordinary features using the names. Useful patterns:

- **Switch features per case** with a Boolean value: suppress a feature by expression (`#pin`). The suppression is
  re-checked for every case (test T6). Case 1 must be the variant you want in the tree.
- **Numeric fields** take expressions: `#flag ? 3 mm : 0 mm`.
- **Edits to geometry from before the body** (fillet a block's rim, Move face) work: Onshape refuses them inside the
  pattern frame, and the Close case re-runs just that feature outside it (tests T1, T2, T12, T14).
- **Name parts** by giving them an output: with *Name parts after outputs* (Close case) the part is named like the
  output's variable (`<case>_<output>`), in every case. To name parts yourself instead, turn it off and rename
  inside the body (e.g. with `#caseName`).

## 5. Case

One feature per further case, placed after the body and before the Close case that runs it.

| Parameter | Meaning |
|---|---|
| **Define case** | The Define case this case belongs to. Picking it lays out the slots. |
| **Update from Define case** | Lays the slots out again after inputs or values were added, removed, renamed or retyped in the Define case. Selections and values are kept by name. (Onshape runs the dialog logic only on a change, so opening the dialog is not enough.) |
| **Case name** | Letters, digits, `_`; unique among the Define case's cases. |
| **Slots** | A selection for each input, a value for each value. New value slots start with case 1's value (a copy, not a link). |

A Case runs nothing by itself. A missing selection or value is an error on the Case (test T11), and a Close case
listing a failed Case fails too.

## 6. Close case

| Parameter | Meaning |
|---|---|
| **Define case** | The Define case this body belongs to. |
| **Features to repeat** | The body's features, in tree order. |
| **Cases** | The Case features to run, in tree order. May be empty: the Close case then just publishes case 1's outputs. |
| **Outputs** | Per output: a **Name** and the **Query variable** the body sets (its name, without `#`; usually a Query Variable "created by" a body feature). Published by every case as `#<case>_<name>`, case 1 included (`#A_rib`, `#B_rib`...). **Evaluate on use** off (default): the entities are fixed when the case closes and follow identity-preserving edits; **Track downstream changes** also follows entities later derived from them. On: the query is stored and re-evaluated wherever the variable is used. |
| **Keep** | Which new bodies each case keeps: parts, surfaces, curves and points, mate connectors, planes (on); sketches (off). |
| **Name parts after outputs** | On (default): a part an output points to is named like the output's variable -- output `rib` gives the part `B_rib` in case B and `A_rib` in case 1 (a second part of the same output gets `_2`, ...). Parts no output points to keep their names. Off: names are left to the body. |
| **Print bindings** | Print each case's selections and values to the FeatureScript notices. |

*Why outputs are typed by name:* a picked query is fixed when the feature starts, so the Close case could not read it
again after running a case. By name, it reads the variable after every case.

After the last case, later features see case 1 again: its inputs, values and outputs. Other variables the body set keep
the last case's.

**Closing again.** A second Close case further down, on the same Define case and with the body picked again, runs
further Cases. Those Cases can select geometry the first batch made -- in test T7, case D sits on top of the stud
case B built. The body must be picked again: borrowing another Close case's list would bring back v2's call from inside
another feature, and with it the lost clicks.

**Messages.** Warning "Not built: B: ..." -- the other cases are still built. Error "No case was built" when all fail.
Typical causes: `repeated feature 3 failed (...)` with Onshape's own reason, a case name used twice, `the Define case
changed; click Update from Define case in this Case`.

## 7. Examples (Case pattern tests)

**T1.** Three blocks A, B, C. Define case inputs `#top`, `#rim`; values `#bossH`, `#edgeR`. Body: Boss (extrude `#top`
by `#bossH`), a rim fillet on the block (`#rim`, geometry from before the body), `#bossEdges` = edges created by Boss,
a fillet of `#bossEdges` at `#edgeR`, `#bossFaces` = faces created by Boss. Cases B (25 mm, R3) and C (8 mm, R2); one
Close case with output `bossFaces`. Result: `B_bossFaces` and `C_bossFaces`, filleted, rims filleted, with
`#A_bossFaces`, `#B_bossFaces`, `#C_bossFaces` on the right bosses.

**T9a.** An Offset+ in the body offsets the input face `#wall9` toward a corner of block A that is **clicked directly**
in Offset+. Case B's face moves toward that corner. (In v2 this was the failing case.)

**T13 / T14.** The boss is extruded "up to" the top face of an outside tower, clicked directly. T14 also fillets the
case's block rim (an edit of geometry from before the body) and the boss edges (a query variable made in the body) --
all three kinds of reference in one case.

**T7.** Close case 1 runs case B (a stud on block B). Case D selects the top of B's stud; a second Close case runs it:
D's stud sits on B's.

The studio holds T1-T14; `devtools/onshape/check_case_pattern_tests.py` checks them all.

## 7b. Keeping the variable list short

Every query variable name stays in the list for the rest of the tree -- FeatureScript cannot delete one (overwriting
it with nothing still leaves the name). So the aim is fewer names, reused.

- **Local names: short, by role, prefixed `_`, reused in every body.** A query variable made inside the repeated
  features ("created by Boss") is a local: only that body uses it. Name it by what it is for -- `_body`, `_copy`,
  `_trim`, `_edges`, `_out` -- and use the same few names in every template. Each body redefines them, so ten
  templates share five local names instead of adding five each, and the `_` keeps them together.
- **Outputs only for what is used later.** Every Close case output makes one name per case (`#B_rib`, `#C_rib`, ...).
  Declare only the outputs a later feature needs; everything else stays local.
- **Output names are plain text.** Type `body`, not `#body`: in an Onshape text field a leading `#` makes the field a
  reference to a variable (seen on RD 20FOU 28: every case published `..._core_3d_glass_periphery`). The same goes for
  the output's query-variable name.
- **Values: specific names.** A value called `name` or `offset` is easy to pick up by accident; `bodyName`,
  `middleOffset` are not.
- **Let composite features carry the pieces.** A feature that publishes its own pieces (Join profile surfaces: its
  faces and joint edges) replaces a chain of helper query variables.
- **Tree:** give each template (Define case, body, Cases, Close case) its own feature-tree folder.

## 8. Limits

- **Cases run in order and see earlier results**: a case can consume what a later case selects.
- **A failed case cannot undo its edits to existing geometry**; only its new bodies are removed.
- **Edits to geometry from before the body** are verified for fillet and move face (incl. Move face+); chamfer,
  shell, draft and booleans into existing parts are untested.
- **Sketches** in the body are re-solved per case, but constraints to the origin or default planes are not reapplied.
  Build sketches on geometry derived from the inputs. Untested live.
- **Not in a pattern**: sheet metal and derived features.
- **Cost**: every rebuild runs the body once per case (plus case 1).
- **Dialog labels**: after *Update from Define case* the grey slot labels may show old names until the dialog is
  reopened; the stored values are right.
- **From v2**: Case pattern features are gone. Each of its case rows becomes a Case feature, listed in the Close case,
  and the Close case's outputs are typed as query-variable names.

## 9. The code

`case_pattern/case_pattern.fs` holds the three features. **Define case** binds case 1 and publishes a hidden
signature. **Case** lays its slots out from that signature (editing logic) and publishes a hidden record of its
selections and values. **Close case** publishes case 1's outputs, then for each Case binds the inputs (resolved
before any frame), pushes a pattern frame on its own sub-id, runs every body feature (`runListedFeature`: re-run
outside the frame on Onshape's refusal to edit outside geometry), removes unkept bodies, reads and publishes the
outputs, and finally restores case 1. Why it is built this way, with every dead end measured: corrections log entries
41, 50 and 60.
