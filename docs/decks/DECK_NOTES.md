# Deck notes: what every deck (or a family of decks) must carry

Decisions from the user's reviews that apply beyond the deck they came from. Check this before writing any spec.

## All decks (from the Split+ review, 2026-09-25)

- One real dialog screenshot per dialog STATE (e.g. keep both / keep one side / face mode), with numbered badges
  (`dialogshot` slide type; positions from `onshape_shot.py` "labels", manual ones for text-less buttons).
- One example per slide (`example` type), wide image, and a picture that shows the RESULT unambiguously
  (colour what is kept / each region; grey what was removed) -- `render_parts.py` from the real bodies.
- Output keys: say what each key physically holds, verified live, with counts from the example
  (the user asked "what do these actually provide?" when the text was abstract).
- No jargon without a plain-language line ("piece placement uses one point" was not understood).

## Reference-side decks (Split+, Mutual Trim+, Offset+, Thicken+)

- First concept slide: `docs/explainers/reference_side/img/fig01_flip_vs_reference.png` (user: "probably put this
  in the deck").
- Tips slide, limit: **outputs keep their input's orientation** -- the reference picks the side, not the normal;
  built-ins downstream (Thicken, Offset surface, extrude up to, Move face, built-in Split) still follow the normal,
  so use the reference-side features there. Orienting output sheets by the reference is DECIDED (2026-09-25), not
  built: sheets only (Split+ surface pieces, Mutual Trim+ results, Offset+ surfaces), per body, default off
  (correction 25), form open (option on each feature vs a standalone "orient to reference" feature).
  Thicken+: its outputs are solids -- say orientation does not apply, and that it is the recommended downstream
  replacement for Thicken. Explainer section 1.4 has the full text. When the option is built, update this note,
  explainer 1.4 / 2.5, and every reference-side deck.
