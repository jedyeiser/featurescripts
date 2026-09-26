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
  built-ins downstream (Thicken, Offset surface, extrude up to, Move face, built-in Split) still follow the normal.
  Fix: **Orient to reference** (built 2026-09-25, standalone feature by the user's choice; sheets only, per body)
  before them, or use Thicken+ / Offset+ instead of the built-ins. Thicken+: its outputs are solids -- orientation
  does not apply; it is the recommended downstream replacement for Thicken. Explainer 1.4 / 2.6 have the full text.
- Related slide / table: name Orient to reference alongside the other reference-side features.

## Builder additions (2026-09-25, xSection family)
- `outputs` slides take `keysTitle` (default "Published for Extract variables"); use "Outputs" for features that are not producers.
- A `dialogshot` whose screenshot is taller than 2.4 x its width (and has 6+ fields) is split over two slides; each slide
  shows only the band of the screenshot holding its fields (crops in docs/tooling/_render/_crops/), so badges stay apart.
- Features without an installed icon get a deck-only draft in docs/decks/_icons/ (never installed to Onshape).
