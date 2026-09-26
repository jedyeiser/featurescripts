# Research brief: Solvers + Small tools (Explore agent, 2026-09-25)

## A. Iterative Solve (solvers/iterative_solve.fs + solver_core.fs)
Doc 3cf445f157b6a28aa1d35fcf (now named "Stray_Weight_Concept_1"; local .document.json says "solvers"), ws
fcf7d435062364f18e5eb9da; tabs iterative_solve 2929b927..., solver_core 3b906109....
Purpose: find the value of one variable (#flat_length) that makes a result (mass, volume, another variable) hit a
target by re-running a feature list (header :8-19). Replaces the manual Pattern "Reapply features" 0 mm x N trick with
a proportional update (returns every iteration's bodies; slow: ~6%/pass, 10.20 g vs 10 g after 20 passes). No native
equivalent.
Concepts: TRIAL = set #var = x, re-run every listed feature under id+"trialK" like Pattern does (:402-419), measure.
Rejected trials' bodies are DELETED (discardTrial :950-957), not aborted (correction 31: a sketch re-run inside
startFeature builds nothing; memory wording "abort rejected trials" is stale). Accepted trial kept, listed features'
original bodies deleted (deleteOriginals :554-561), variable set to solution. No solution -> regenError, whole feature
rolls back, originals stay (:531-536). Solver owns the variable: if a listed feature reassigns it, restored right after
with a NOTE (:437-448) -> a Pattern-style folder with its own update step works unchanged. Residual = result - target
(SI). Reach target: |r| <= tol (:764-774); First match: condition < <= > >= (:775-790).
Methods (solver_core.fs): Start from current (solveTargetFromStart :31-114): trial at x0 then x0 +- step%, secant
x_n = x_b - r_b(x_b - x_a)/(r_b - r_a) (:82) clamped to bounds, failed trial retried up to 3x halving the step
(:93-99), switch to Brent once bracketed. Both bounds (solveTargetBracketed :120-155): trials at both bounds, must
bracket, then Brent (:190-296, NR zbrent; failed trial -> retry once at bracket midpoint; rel x tol 1e-12 :21).
First match (solveFirstMatch :161-183): steps+1 evenly spaced values lower->upper, stop at first meeting condition.
Insensitivity: first two trials same residual -> "the result does not depend on the iteration variable" (:303-306).
Figures: (a) timeline trial 0..K: rejected greyed/deleted, accepted kept, originals deleted; (b) residual vs x: start,
step, secants, bracket, Brent.
Dialog (:92-306): Features to iterate (FeatureList, tree order); Iteration variable (string, no #, must exist
upstream); Variable type Length/Angle/Number (Length); Lower/Upper bound (GOTCHA: both default equal -- Length 25/25
mm, Angle 0/0, Number 0/0 -> "The lower and upper bounds are equal" :370-373 until edited); Method Reach target
(default) / First value meeting condition; Reach -> Start from Current (default) / Both bounds; Current -> First step
(%) default 5 (1e-6..100, :87; step = |x0|*%, x0=0 -> % of bound span :513-517); First match -> Condition (< default,
<=, >, >=), Steps (20, 1..1000). Result: Variable (default) / Mass of created solids / Volume of created solids;
Variable -> Result variable; Mass -> Density g/cm^3 (1); Mass/Volume -> Store result as (optional name); measured on
the trial's SOLID bodies, evVolume HIGH (:740-749). Target from variables (bool): on -> Target variable (+ Tolerance
variable for Reach), re-read every trial (listed feature may compute them); for mass/volume a plain number = g or mm3
(:866-877); off -> Result type (Variable result: Number/Length/Angle/Area/Volume), Target, Tolerance in matching
units; tol defaults 0.01 for number/g/mm2/mm3 (:84); Length tol defaults 25 mm (NONNEGATIVE_LENGTH_BOUNDS -- large);
Angle tol 0 (never accepted unless exact). Max trials 30 (2..1000). Debug: Watch variables; Feature trace (body-count
deltas per listed feature :612-637); Keep failed trial (stop at first failure, keep partial geometry, later trials
skipped :395-398, :522-529); Run once at a value -> Value (single kept probe, no search :484-499).
Outputs: accepted trial's bodies (under <featureId>trialK); #iterationName = solution (:541); store variable = mass
or volume (:542-545); INFO "#x = ... after N trials; result ..." (:547); probe/kept-failure INFO/WARNING; no solution
-> regenError with solver message; console line per trial (:462-467), table with Debug (:642-657).
Walkthrough: validate names (:309-325); sort features by id (:327-338); check variables exist/type (:341-366); trial
closure (:390-482); dispatch (:502-519); deleteOriginals + set variable (:539-548). SI numbers via iterationUnit
(:675); mixed units in residual = failed trial (:755-760).
Example: Stray_Weight: #flat_length so mass of created solids hits target. Verified 2026-09-22: 20 mm -> 12.57 g, 21
-> 12.73, 5 -> 10.18; 10 g target ~3.85 mm below the 5 mm lower bound -> "target lies beyond the lower bound".
Design_Master derive configs Target 15 g / 20 g converge at trial 2.
Limits: downstream persistent references bind to <solver>trial2<featureId> -> break when the accepted trial number
changes (2026-09-24; fix = rebuild under fixed id, not built); kept trial leaves construction/sketch bodies (38);
outside modifications by listed features not undone by discard (:948); unreachable target errors (no clamp) -> 19
downstream ERRORs; cost: every trial re-runs the list (~10 s cold regen for the derive); stored-solution mode proposed
not built; NO test studio -- verified manually live 2026-09-22.

## B. Part Volume (solvers/Part_Vol.fs, 28 lines, tab ecfd4623a75070adad74ec70, user-written)
Writes one solid's volume to a variable as a plain number in mm3 (:6, :19-25). Dialog: Part (single solid), Variable
Name (no validation). evVolume HIGH / millimeter^3 -> setVariable + println; no notice, no description (:8). Typical:
Iterative Solve result variable (target in mm3). Gotchas: empty name not caught; unitless value; one part. Memory's
"multiplies by 1e10" 10x bug is stale (fixed). Untested.

## C. Move Along Edge (smallTools/Move_Along_Edge.fs; smallTools doc 14cdaad24a8beab67af40fe4, ws 9bfd33d5...,
tab ab0debbe1dc8fa95294ea68a)
Purpose: move or copy bodies, mate connectors, sketch edges or sketch vertices along an edge / connected chain / wire,
by a distance along the path or "to the nearest point to a target", optionally rotating (:69-71). Native Transform
can't follow a curve by arc length; Curve pattern makes evenly spaced instances, not one move to a named location.
Concepts: start = reference point projected onto the path (:337-341); reference point (referencePoint :533-549) =
reference vertex, else MC origin, else centroid (correction 42: closest point ambiguous when entity ties with path).
End (endArcLength :438-451) = start +- distance, or nearest path point to target. Past ends: open path extrapolated
linearly along end tangent, closed path wraps (:588-641). Motion (:457-472): Move and rotate = full frame mapping;
Translate only = point-to-point shift; Rotate only = same rotation about the reference point. Orientation (:477-496):
Tangent only; Transported (no twist, RMF double reflection Wang 2008, 64 samples :502-527); Frenet (follows curvature
normal; arbitrary on lines, flips at inflections).
Figures: (a) S-curve with a block in the three orientation modes; (b) start projection -> distance -> end with blue
flip arrow.
Dialog (:72-167): Entities to move (bodies (not sketch), MCs, sketch edges, sketch vertices); Edges or wire to move
along; Provide Reference Point -> Reference Vertex; Move to Distance (default) / Nearest point to; Nearest -> Target
(vertex, edge, face, body, MC; where it crosses the path that crossing is used); Distance -> Distance to move (25 mm,
signed); Flip direction (Distance only; on-screen arrow toggles it :194-195, :282-289); Apply Move and rotate
(default) / Translate only / Rotate only; Orientation Tangent only (default) / Transported / Frenet (hidden for
Translate only); Copy Bodies? (false); Naming N/A / New name / Prefix / Suffix -> Name text; Mate connectors as points
(false); Additional copies array (Copy on): Move to -> Target or Distance from the SAME start point, Naming -> Name
text; hidden sourceNames filled by editing logic (:245-259, correction 36).
Outputs: moved bodies or copies (opPattern identity + opTransform); sketch edges -> new wires (opExtractWires); sketch
vertices, and MCs with points option -> points; source stays (:369-383). Keys: output, inputs, initial_copy,
copy_1..n (:222-238). Notices: rotate-only points INFO (:207-210); MCs can't be named (:212-215); missing source names
WARNING (:217-220). Main move keeps old ids moveBody<i> so downstream references survive.
Key functions: buildMovePath :304, moveBodyOnCurve :337, nearestOnPath :568, arcLengthOf :552, evalPathAtArcLength
:588, transportNormal :502.
Examples: slide a cube 50 mm along a 3-edge wire keeping it tangent; copy to 3 stations (main + 2 additional copies at
different distances, or at crossing curves in Nearest mode); named MCs -> points at a trim location.
Limits: positive direction = constructPath direction (first selected edge's own direction; user decided 2026-09-25 to
keep); Nearest ignores Flip; names refresh only on edit; MCs never renamed; Frenet unstable on lines; breaking change
old useFrenet/frameMode not migrated (user OK'd); flip arrow not verified visually; review: copy_j keys comply
(round2_lead_producers.md:48), crosscut.md:200 asks about snake_case keys. Tests: "Move along edge tests" studio
(1643f325...), devtools/onshape/build_/check_move_along_edge_tests.py, M1-M20 + M3b.

## D. Trim curve (smallTools/OS_Trim.fs, tab 3037b0c2c5e91245deb0807e)
VERBATIM copy of Onshape std moveCurveBoundary.fs (native "Trim curve", PTC MIT); only version bump 2878 -> 3083.
Thin wrapper over opMoveCurveBoundary (:15-99). Reference only (betterCurveTrim says "not imported" :15). Dialog:
Move curve boundary type Trim/Extend; Curves to adjust (non-sketch wires); Extend -> End condition Blind -> Distance
else Up to entity, then Extension shape; Trim -> Up to entity; Help point (vertex/MC picks end/side); Opposite
direction. Manipulator: linear drag arrow for Blind extend at the extended end, first wire only (:101-127, :184-202).
No icon, no tests. Doc note: prefer the native feature; exists to compare with Trim curve +.

## E. Trim curve + (smallTools/betterCurveTrim.fs + curveTrimCore.fs; tabs 276be4e4e6bce49e6b1d3c7d,
d56d74c24234ab2b885e6fc1 pinned mv 48a46b25...)
Purpose: trim or SPLIT wires at exact arc-length locations: at points, signed distances from a reference, even
division, or inflections (:13-33). Native only trims up to one entity. Extend mode pending (:23).
Concepts: every wire (multi-edge too) read as one constructPath; every cut = fraction 0..1 of total length
(curveTrimCore :74-105); cuts map to (edge, arc-length param) and use opSplitEdges (:329-350; correction 45: planes
also cut wherever else the curve crosses them). Split in place (returnSingleWire) keeps body identity; Separate pieces
extracts each span as its own wire then deletes the source (:383-413) -> downstream references to the source break.
Fraction cleaning: within 1e-6 of an end dropped, sorted, deduped (:558-580). Inflections: curvature signed by
binormal vs plane normal; 200 samples skipping 1e-3 at ends, bisect sign changes to 1e-5 (:499-600); curve must be
planar. Preview: red dots at cuts; dashed red line from an off-curve pick to its projection; blue arrow positive
direction (:276-317); magenta traces of removed side (:464-481).
Figures: (a) one 3-edge profile with the cut modes side by side; (b) separate wires vs single wire with piece_k labels.
Dialog (:102-219): Operation Trim / Split (DEFAULT Split, remembered); Curves to adjust (non-sketch wires); Cut
location (DEFAULT At points): Up to entity -> entity; Distance from point -> From point, Distance (signed, 25 mm),
Opposite direction, Split only: Additional distances (array), Also split at reference point; Even division -> Division
style: Between two points (Start point, End point, Number of segments 2..100 default 4) / Every distance from point
(From point, Spacing 0.1..1000 mm default 10, Number of cuts 1..100 default 5, Reverse direction); At points -> Cut
points (multi); At inflections -> Inflection selection Pick (click the dots, default) / Nearest each endpoint. Split ->
Return single wire; Trim -> Keep opposite side. Debug: Print inflection solve, Print cuts. Hidden inflectionIndices
(toggle manipulator :680-692).
Outputs (embedTrimOutputs :616-669): output/inputs, cut_1..n (vertex at cut k; 2 vertices per cut when split into
separate wires), cutVertices, piece_1..m (bodies, or edges when split in place), variable cutCount. Errors: "runs past
the end of the curve" (reference position + length :505-509); no valid cut; non-planar; no inflections; inflection
mode with >1 curve or multi-edge wire.
Walkthrough: body (:220-248) per curve adjustOneCurve (:264) -> cutFractionsFor (:472) -> cleanFractions -> applyCut
(:296); inflection via adjustAtInflection (:360).
Examples: RD 20FOU "Trim curve + 1" on FLAT_PROFILE (3-edge joinWires output; errored before multi-edge rewrite; cut
points are Move Along Edge outputs 5.9 and 9.3 mm off the profile -> dashed projection lines). Split a sidecut at the
inflection nearest each end; in Trim that keeps the MIDDLE span (:409-420).
Limits: Trim uses only the first cut (:277-280); Trim keeps END-side piece by default (:333) -- path-direction based,
unlike native help point; Distance mode positive = path direction (changed from toward midpoint) but Spacing mode
toward midpoint by default (curveTrimCore :246-250); Spacing cuts past an end silently dropped, Distance cuts past an
end error; Even division between points also cuts at the two picked points (:222-239); Up to entity uses per-edge
nearest point, so an entity that never touches still cuts at nearest point with no error (:196-208); inflection mode
one planar single-edge curve, picked indices shift on geometry change; MC picks resolved through owner body
(resolvePoint :586-594, correction 44); review flags cut_k/piece_k geometry-numbered keys (round2_lead_producers.md:47,
decision pending); RD pins smallTools 1e0ffe0e (user must version + update). Tests: "Trim curve + tests" studio,
check_trim_curve_plus_tests.py T1-T9, 9/9 2026-09-25; arrow not verified visually.

## Unclear
1. Iterative Solve test status: MEMORY index "20/20 live tests" likely copied from Move Along Edge; no suite exists.
2. Part_Vol 10x bug in memory is stale.
3. Move Along Edge test count 19/19 vs 20/20; checker has M1-M20 + M3b.
4. Trim curve + T1-T8 vs 9/9 vs T1-T9.
5. feature-icons.md says awaiting picks but Iterative Solve, Part Volume, Move Along Edge import icons; OS_Trim none.
6. corrections file has duplicate Correction 45/46 (lines 1547/1570, 1559/1584) -- cite by title.
7. Solvers doc renamed in Onshape.
8. Iterative Solve equal default bounds; 0 deg angle tol and 25 mm length tol questionable defaults.
9. Trim curve + snake_case keys undecided.
