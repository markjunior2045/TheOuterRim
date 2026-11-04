---
description: Transcribe one batch of card images into data/transcription (PLAN.md T2.8)
argument-hint: <deck folder, e.g. base/market/gear> [index range, e.g. 0-9]
---

Transcription batch for: $ARGUMENTS

Talk to the owner in Brazilian Portuguese. Everything written to files is English.

## Before you start

1. Read `docs/TRANSCRIPTION_FORMAT.md` and one existing file in `data/transcription/` of the same kind (market or encounter) as an example. Read only the parts of `docs/RULES.md` you need (the icon table at the top, and rules cited below).
2. Prepare the reading copies (never read the full-size PNGs, and never write into `RawGameFiles`):
   `python tools/prepare_transcription_images.py --sorted C:\TheOuterRimProject\RawGameFiles\sorted --deck <deck> [--only <range>]`
3. Look at `artifacts/transcription-work/_legend-market-decks.jpg` once. It maps each market deck icon to its placeholder.

## Icon legend

- Credits: the hexagon with a stylised symbol before a number → `{CREDITS}`.
- Die results: single starburst → `{HIT}` (confirmed in the pilot). `{CRIT}` and `{FOCUS}` have not been seen yet: the first time one appears, add a `verify` entry describing it, and update this legend once the owner confirms.
- Reputation: hexagon with `+` → `{REP_POS}`, empty hexagon → `{REP_NEU}`, hexagon with `−` → `{REP_NEG}`.
- Patrol movement icon (bottom of market cards): yellow number = distance; inner icon = faction. Rebel = red crest, Imperial = blue cog, Hutt = green, Syndicate = gold.
- Market deck icons: see the legend image.
- Any icon not in the `RULES.md` table: do not guess. Write `{UNKNOWN_ICON}` and add a `verify` entry describing it.

## Transcribe

- One output file per deck: `data/transcription/<pack>-<category>-<deck>.json` (for example `base-market-gear.json`, `base-encounter-navpoint.json`, `base-databank.json`). If the file exists, add or replace only the cards of this batch, keyed by `image`.
- Copy the printed text exactly. Do not fix, summarise or interpret it. Do not invent rules.
- Errata: keep the printed text and add `"errata": "R-24.x"` when `RULES.md` §24 lists an erratum for that card.
- Uncertain word, number or icon → keep your best reading and add a `verify` entry. Never leave a card out silently.
- If this kind of card has no section in `docs/TRANSCRIPTION_FORMAT.md` yet (databank, characters, ship sheets, contacts, Core Worlds…): transcribe the first 3 cards with the fields you propose, then **stop and show the owner the proposal** before doing the rest. Once approved, add the new fields to the format document and bump the minor version.

## Check and finish

1. Validate: the JSON parses, every image in the batch is present exactly once, and `format` is the current version.
2. Re-open each image and compare it with its entry, line by line (names, numbers, icons, faction and distance).
3. Report to the owner, in Portuguese: number of cards, the `verify` items, anything new for the format.
4. Commit on the current task branch: `T2.8: transcribe <deck> [<range>]`.
