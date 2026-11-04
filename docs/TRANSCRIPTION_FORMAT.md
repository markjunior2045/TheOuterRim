# TRANSCRIPTION_FORMAT.md — outer-rim-transcription/0.9

Raw printed text of the cards, transcribed from the mod images (`PLAN.md` T2.8). This is **not** the game's card data format: mapping text to effects happens later (T2.1, T2.2). The goal here is a faithful, checkable copy of what is printed.

## Files

One file per deck, grouped in one folder per category: `data/transcription/{category}/{pack}-{category}-{deck}.json`, for example `market/base-market-cargo.json` or `encounter/base-encounter-lothal-mon-calamari.json`. The folder is the category (`market`, `encounter`, `databank`, `characters`, `contacts`, `ship-sheets`, `ai`, `ambitions`). `PROGRESS.md` stays at the root of `data/transcription/`.

```json
{
  "format": "outer-rim-transcription/0.9",
  "pack": "base",
  "deck": "cargo",
  "source": "[Mod 01] sheet 49, sorted/base/market/cargo",
  "notes": ["..."],
  "cards": [ ... ]
}
```

## Common rules

- Text is copied as printed: same wording, punctuation and capitals. Curly quotes and apostrophes are kept.
- Icons become the `RULES.md` placeholders: `{CREDITS}`, `{HIT}`, `{CRIT}`, `{FOCUS}`, `{DROID}`, `{HYPERDRIVE}`, `{REP_POS}`, `{REP_NEU}`, `{REP_NEG}`, `{DECK_JOB}` and so on. A new icon gets a new placeholder added to `RULES.md` first.
- Printed labels that have a field of their own (`Destination:`, `Reward:`, `Encounter:` on jobs) become that field, without the label. Labels inside free text (`Action:`, `Encounter:` in a secret) stay in the text.
- Bold text inside a paragraph (for example a reputation loss the player must not miss) is written between `**` `**`. Labelled fields carry no label and no bold. Bold small caps traits stay in `traits`.
- Traits (bold small caps above the text, R-14.19) go in `traits`, lower case.
- Errata: the printed text is kept; the section gets `"errata": "R-24.x"`. The correction is applied when mapping effects.
- `image` is the extracted file name without extension; it links the transcription to the art.
- `verify` lists anything the owner should check against the physical card. An empty list means nothing to check.

## Market cards

| Field | Content |
|---|---|
| `name`, `card_type` | name; `cargo`, `mod`, `gear`, `crew`, `job`, `bounty`, `luxury`, `ship`… |
| `cost` | credits as an integer |
| `traits` | list |
| `text` | list of printed paragraphs that are not a labelled field |
| `destination`, `reward` | when printed |
| `abilities` | labelled abilities such as `Action: …` |
| `skills` | crew: list of skills printed in the bottom boxes, lower case. Job: `[{ "skill", "certain" }]`, `[]` when `NONE` is printed |
| `flavor`, `encounter` | job only: italic flavor text; text of the `Encounter:` label |
| `class` | bounty: `{ "color", "pips" }`; the portrait frame color and the pips agree: white 1, green 2, yellow 3, orange 4 [UB] (R-12.3, R-15.48) |
| `combat` | bounty: `{ "value", "type" }`; `type` is `{GROUND_COMBAT}` or `{SHIP_COMBAT}` (R-15.51) |
| `elimination_reward`, `capture_reward` | bounty: text after the printed label |
| `slots` | ship: `[{ "type", "count" }]` in printed order; `type` is `cargo`, `cargo or mod`, `mod` or `crew` |
| `stats` | ship: `{ "hyperdrive", "ship_combat", "hull" }` (green `{HYPERDRIVE}`, red `{SHIP_COMBAT}`, yellow `{HULL}`) |
| `cost_label` | when the cost box prints a word instead of a number (for example `Special`): `cost` is `null` and `cost_label` holds the word |
| `bottom` | `null` on ship cards (nothing printed); printed at the top of the card on jobs and bounties, at the bottom on other cards; `deck_icon`; `patrol` = `{ "faction", "distance" }` or `null` (R-11.15); `reveal_contact` = player range or `null` (R-16.26) |

## Encounter cards

| Field | Content |
|---|---|
| `number` | printed card number (n/10) |
| `sections[]` | one per planet (or Maelstrom, navpoint state, Core Worlds / Across the Core) |
| `sections[].planet` | as printed in the header |
| `sections[].secret`, `orientation` | `true`/`false`; `upright` or `inverted` (printed upside down) |
| `sections[].variants[]` | one per reputation subsection: `condition` (printed line, or `null`), `flavor` (italic text), `text` (rules text) |
| `asset` | embedded asset or `null`: `orientation`, `name`, `card_type`, and the market or job fields that apply |
| `sections[].variants[].secret`, `orientation` | optional: when the secret box sits under a reputation condition, the variant carries them and the section has `secret: false` |
| `sections[].variants[].text` | rules text; several printed paragraphs are one string separated by `
` |

Job skills: `[{ "skill": "stealth", "certain": false }]`; a skill printed in italics will certainly be tested, so `certain: true` (R-15.32). It does not mean the player must have the skill.

## Character cards

One entry per character; the card has two sides, so the file lists both images. Files: `characters/{pack}-characters.json`.

| Field | Content |
|---|---|
| `image`, `image_back` | front and back image names without extension (`base-character-00`, `base-character-00-back`) |
| `name` | as printed; a droid icon after the name is `{DROID}` |
| `stats` | `{ "ground_combat", "health" }` (red `{GROUND_COMBAT}`, yellow crosshair `{HEALTH}`) |
| `skills` | skill boxes at the bottom, lower case |
| `front`, `back` | `{ "upper", "lower" }`: paragraphs above and below the divider line. The front's lower part holds the Personal Goal; the back's lower part holds the ability gained when the card is flipped |
| `setup` | the `Setup:` line without the label |

## Contact tokens

One entry per token, from the face image only (the back differs just in class color and pips). Files: `contacts/{pack}-contacts.json`.

| Field | Content |
|---|---|
| `image` | face image name without extension, e.g. `base-contact-02-4-lom` |
| `name` | as printed |
| `databank_card` | the number printed on the face: the databank card to resolve when encountering the contact (R-12.2, R-12.15) |
| `class` | `{ "color", "pips" }` from the frame color: white 1, green 2, yellow 3, orange 4 [UB] (R-12.3) |
| `faction` | faction icon printed on the face (`rebel`, `imperial`, `hutt`, `syndicate`) or `null` |
| `droid` | `true` when the `{DROID}` icon is printed (R-12.13) |

## Ship sheets

One entry per sheet, with both sides. Files: `ship-sheets/{pack}-ship-sheets.json`.

| Field | Content |
|---|---|
| `name` | name of the sheet (the Ship Goal side, or the first side for starter ships) |
| `sides[]` | the two printed sides |
| `sides[].image`, `role` | image name without extension; `ship_goal`, `flipped` or `starter`. The file without `-back` is not always the same side, `role` is authoritative |
| `sides[].name`, `cost` | printed name and cost (0 on starter ships) |
| `sides[].slots` | `[{ "type", "count" }]` in printed order: `cargo`, `cargo or mod`, `mod`, `crew`, `crew or bounty`, `bounty`; `MOD X2` is `count: 2` |
| `sides[].stats` | `{ "hyperdrive", "ship_combat", "hull" }` |
| `sides[].traits` | e.g. `["starter ship"]` |
| `sides[].ability_name`, `text` | title of the ability (`null` if none) and the paragraphs of the white box; the Ship Goal side keeps `Ship Goal:` in the text |

## AI cards

One entry per card (`AI Player Turn`); one image each. Files: `ai/{pack}-ai-{deck}.json`.

| Field | Content |
|---|---|
| `image` | image name without extension |
| `number` | number printed at the bottom right (not the file order) |
| `title` | `AI Player Turn` |
| `steps[]` | `{ step, name, rule, items[] }`; `rule` is the opening sentence (`Do the first that applies:` or `Do all that apply:`), `null` on the Special step |
| `steps[].items[]` | `{ label, text }`; `label` is `a`, `b`, `c`... or `null` for a bullet |

## Databank cards

One entry per card image. Files: `databank/{pack}-databank.json`. The back of every card only repeats the number and the version pips and is not transcribed.

| Field | Content |
|---|---|
| `image` | image name without extension, e.g. `base-databank-001a` |
| `number`, `variant` | number printed at the bottom right; letter of the version from the file name, or `null` |
| `kind` | `cargo_check` (number 1), `contact` (2-23), `job` (40-53) or `starting_asset` (90-92) |
| `title` | printed title; on `starting_asset` the name of the asset |
| `starting_planet` | `starting_asset` only: text after `STARTING PLANET:`; otherwise `null` |
| `intro` | paragraphs before the sections, or `[]` |
| `sections[]` | `{ marker, heading, flavor, text[] }`. `marker` is `null`, `{ "type": "die", "value": "{CRIT}" \| "{FOCUS}" \| "BLANK" }`, `{ "type": "step", "value": n }` or `{ "type": "faction", "value": "hutt" }`; `heading` only on favor cards (`HUTT FAVOR`) |
| `asset` | embedded asset or `null`. `crew`: `name`, `skills`, `faction`, `droid`, `text[]`. `job`: `name`, `skills` (`{ skill, certain }`), `destination`, `encounter`, `reward`. `bounty`: `name`, `combat`, `elimination_reward`, `capture_reward`, `text[]`. `cargo`: `name`, `destination`, `reward`. Each carries `card_type` |

Branches such as "skip to step 3" or "repeat this step" stay as printed text.

## Pending for later decks

Core Worlds cards will add fields when their first batch is transcribed. Each addition bumps the minor version and is described here.
