# TRANSCRIPTION_FORMAT.md — outer-rim-transcription/0.3

Raw printed text of the cards, transcribed from the mod images (`PLAN.md` T2.8). This is **not** the game's card data format: mapping text to effects happens later (T2.1, T2.2). The goal here is a faithful, checkable copy of what is printed.

## Files

One file per deck, grouped in one folder per category: `data/transcription/{category}/{pack}-{category}-{deck}.json`, for example `market/base-market-cargo.json` or `encounter/base-encounter-lothal-mon-calamari.json`. The folder is the category (`market`, `encounter`, `databank`, `characters`, `contacts`, `ship-sheets`, `ai`, `ambitions`). `PROGRESS.md` stays at the root of `data/transcription/`.

```json
{
  "format": "outer-rim-transcription/0.3",
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

Job skills: `[{ "skill": "stealth", "certain": false }]`; a skill printed in italics will certainly be tested, so `certain: true` (R-15.32). It does not mean the player must have the skill.

## Pending for later decks

Databank cards (numbered steps, tests, branches), characters, ship sheets, contacts and Core Worlds cards will add fields when their first batch is transcribed. Each addition bumps the minor version and is described here.
