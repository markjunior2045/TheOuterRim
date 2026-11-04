# PLAN.md — Outer Rim digital

Task plan, ordered by dependency. Each task is meant to fit one Claude Code session, with a **Done when** criterion and the tests that prove it.

**This plan is a guide, not a contract.** Tasks may be done out of order, split, merged or replaced, as long as that does not hurt the project. When the order changes for good, update this file. Dependencies are listed so that skipping ahead is a conscious choice, not an accident.

Rule IDs (`R-x.y`) refer to `RULES.md`. Decisions live in `DECISIONS.md`. No time estimates, by decision.

**Status legend:** `DONE` · `REVIEW` (done, awaiting human check) · `TODO` · `BLOCKED` (needs input) · `DEFERRED` (intentionally postponed)

---

## Conventions

- **Done means:** tests pass, new behaviour cites its rule IDs in code comments and test names, docs are updated if a decision was made, one branch and at least one commit per task.
- The rules core has no `UnityEngine` references (see `DECISIONS.md` §7). The Unity layer displays state and sends commands.
- Core behaviour is verified with `dotnet test`, not by playing in the editor.
- Anything ambiguous in the rules is marked `[?]` in `RULES.md` and asked about, not guessed.
- Plain Git, no LFS. Raw source images do not go in the repository.

---

## Phase 0 — Foundation

### T0.1 — Manuals → RULES.md · `DONE`
- **Delivered:** `RULES.md` (English, rule IDs, icon placeholders, divergence notes, gap list in §27). Reviewed by the owner on 2026-11-04.

### T0.2 — Repository scaffold · `DONE`
- **Delivered:** plain Git repository, `.gitignore`, `CLAUDE.md`, core at `src/OuterRim.Core/` (local Unity package, `noEngineReferences: true`), xUnit project at `tests/OuterRim.Core.Tests/` compiling the same sources, build output in `artifacts/`. `dotnet test` green.

### T0.6 — Extraction tooling and transcription in the repository · `DONE`
- **Does:**
  - `tools/`: `tts_extract.py` (v1), `sort_outer_rim.py` (v2), `prepare_transcription_images.py` (reading copies for T2.8), `requirements.txt` (`pillow`, `numpy`, `opencv-python`);
  - `.claude/commands/transcribe.md`: the `/transcribe <deck> [range]` command used for every T2.8 batch;
  - `docs/TRANSCRIPTION_FORMAT.md` (format `outer-rim-transcription/0.4`);
  - `data/transcription/`: the pilot files.
- **Done when:** the scripts run from `tools/` against `RawGameFiles` with the same output as before, and the transcription files parse.

### T0.3 — Extraction pilot (feasibility) · `DONE`
- **Delivered (2026-11-04):** `tts_extract.py` (v1, generic) and `sort_outer_rim.py` (v2, project specific). Output copied to `RawGameFiles`.
- **Answers:**
  1. Resolution is ample (cards 875–1340 px wide).
  2. The art crop works with a fixed rectangle per card type.
  3. Printed text is legible; transcription from the images works (see T2.8 pilot). Photos of the physical cards are only needed to settle doubts.
  4. Missing: [Mod 01] ambition sheets 91–94 (dead links), dice and player boards (`.rawt`). Fallback: (Mod 02) for all of them.
- Image sources per component: `DECISIONS.md` §3.1.

### T0.4 — Unity project and CLI wiring · `TODO` (T0.2)
- **Does:** create the Unity 6.6 project (URP by default); consume the core assembly (one copy of the source); install the Unity CLI and `com.unity.pipeline`; document the commands in `CLAUDE.md`.
- **Done when:**
  - a trivial core class is called from a Unity script;
  - the same test file passes under `dotnet test` and under Unity's EditMode runner;
  - from the terminal, Claude Code can trigger a recompile and read compiler errors, run EditMode tests and get a pass/fail exit code, and query the scene.
- **Note:** if a CLI step fails, record the workaround in `CLAUDE.md` and continue.

### T0.5 — Map geometry · `TODO` (T0.3)
- **Does:** JSON for spaces, paths and contact spaces (with class) for each map tile and endcap; the standard layout (R-3.2) and the Core Worlds variant `[UB]`; schema and validator.
- **Done when:** the validator passes and graph tests hold.
- **Tests:** counts per tile match the physical board; every path is symmetric; every space is reachable; the two Core Worlds spaces are not adjacent (R-5.4).
- **Needs from the owner:** a visual check of the result against the board.

---

## Phase 1 — Core without content

Fictional content, verified with `dotnet test`.

### T1.1 — State, commands, events, RNG, save/load · `TODO` (T0.2)
- **Done when:** state round-trips through save/load unchanged; the same seed always gives the same results; saves carry a format version and the list of content packs.
- **Tests:** round-trip equality; seeded determinism; a golden save file.

### T1.2 — Pending decisions and ties · `TODO` (T1.1)
- **Does:** pausing the engine to ask a player for a choice mid-effect (`RULES.md` §26.2), including `ChooseOption` and `ResolveTie` (R-5.30, R-21.10, R-21.13).
- **Done when:** a scripted effect can pause, wait, resume, and survive a save/load while paused.

### T1.3 — Map graph · `TODO` (T0.5, T1.1)
- Adjacency, shortest distance, "nearest" with ties, no counting through the Core Worlds (R-5.2, R-5.29 to R-5.33).

### T1.4 — Turn loop, planning step, movement · `TODO` (T1.2, T1.3)
- Turn order and the three steps (R-4); movement with patrol and Maelstrom interruption and "directly" moves (R-5.11 to R-5.23).
- **Tests:** one per rule in R-4.6 to R-4.8 and R-5.17 to R-5.23.

### T1.5 — Dice and skill tests · `TODO` (T1.1)
- **Tests:** face distribution; the three thresholds of R-6.9 over many seeded rolls; `{CRIT}` is not `{HIT}` (R-6.2).

### T1.6 — Combat · `TODO` (T1.2, T1.5)
- The five steps (R-7.10 to R-7.14), ability windows as hooks, ties to the attacker, win-then-defeated ordering.
- **Tests:** each step; tie rule; level-4 patrols roll no dice (R-11.6).

### T1.7 — Damage, defeat, reputation, fame, victory · `TODO` (T1.6)
- **Tests:** R-8 invariants; reputation clamping (R-10.5, R-10.6); fame floor; victory tiebreak (R-18.16).

### T1.8 — Patrols · `TODO` (T1.3, T1.6)
- Setup, spawning, movement toward the player with path and direction rules, forced encounters (R-11).
- **Tests:** one per rule in R-11.12 to R-11.29; always exactly one patrol per faction.

### T1.9 — Assets, slots, ships · `TODO` (T1.1)
- Slots, rearranging, forced discard, rotating assets, ship purchase and barter hooks (R-14, R-17).

---

## Phase 2 — Content

### T2.1 — Effect inventory · `TODO`
- Classify every card's effect, condition and target types, plus card and component types, into `docs/EFFECTS.md`.
- **Done when:** every card maps to a known effect type or is listed as a custom exception.
- Classification, not transcription: it sets the card data format. Works from the T2.8 transcriptions.

### T2.2 — Data schema and validator · `TODO` (T2.1, T1.9)
- JSON schema for cards, effects, characters, ships and **content packs** (id, version, dependencies); validator; stable IDs.
- **Done when:** the validator rejects malformed cards and unknown effect types.

### T2.3 — Effect engine · `TODO` (T2.2)
- The effects from the inventory, with hooks for custom exceptions.

### T2.4 — Small data sets · `TODO` (T2.2)
- Patrols (16), ships (18), contacts (33), characters (16), ambition sheets (8). **Done when:** all load and validate.

### T2.5 — Market · `TODO` (T2.3, T2.4)
- Discard, buy, barter, icons, ship purchase, "Used Ship for Sale" (R-16).

### T2.6 — Encounters, contacts, databank, secrets · `TODO` (T2.3)
- R-12, R-13, R-15.72 to R-15.91, including the Core Worlds announcement (R-15.80).
- One deck per pair of spaces; the interface shows only the player's section (`DECISIONS.md` §4.1).

### T2.7 — Full image extraction · `DONE`
- Done by `sort_outer_rim.py` (v2): 1,040 files sorted by pack and deck, with manifest, report and `CREDITS.md`.
- **Follow-up · `TODO`:** name encounter images by their printed card number (n/10). Today the file order is the reverse of the printed number (file `00` = card 5/10). Re-run and check that every encounter deck still has its 10 (or 11) files.

### T2.8 — Card transcription in batches · `TODO` (priority: before T0.4, by owner decision 2026-11-04)
- Transcription comes first because the card patterns found here drive the core design (T2.1). Phase 0 resumes with T0.4 after the base game is transcribed and inventoried.
- Raw printed text into JSON, format `outer-rim-transcription/0.4` (`docs/TRANSCRIPTION_FORMAT.md`). Mapping text to effects happens later, in T2.1/T2.2.
- Each batch starts with an icon legend (market deck backs, reputation icons, die faces) and ends with the owner reviewing the cards listed under `verify`.
- English first; Portuguese through the localization files.
- **Pilot · `DONE` (2026-11-04):** 10 base cargo cards and 5 Lothal/Mon Calamari encounters; format validated; about 850 image tokens per market card and 1,700 per encounter.
- **Batches (base game):**
  - [x] Market: cargo
  - [ ] Market: bounty, gear, job, luxury, ship
  - [ ] Databank (53)
  - [ ] Encounters: lothal-mon-calamari (5 of 10 done), the other six decks
  - [ ] Characters (8), ship sheets, contacts, patrols, AI smuggler deck
- **Batches (Unfinished Business):** after the base game, same split; decide first which image version to read from (`DECISIONS.md` §9).
- **Done per batch when:** the JSON parses, every card in the deck is present, and the batch is reviewed. Validation against the data schema comes once T2.2 exists.

---

## Phase 3 — Playable

### T3.1 — V0: terminal game with random bot · `TODO` (T2.5, T2.6)
- **Done when:** 1,000 seeded games finish without crashing or breaking any invariant in `RULES.md` §26.3.

### T3.2 — Unity: map and movement · `TODO` (T0.4, T1.4)
- Show the map, pick a destination, animate the ship from start to end space.

### T3.3 — Unity: first card object · `TODO` (T0.4, T2.2)
- One Job card from data: template, three sample cards, English and Portuguese text, cropped art.
- The UI technology for cards is chosen here; changing it later is acceptable.

### T3.4 — Unity: remaining V1 interface · `TODO` (T3.2, T3.3)
- Ship choice, one character, one card of each type, one contact, one encounter (`DECISIONS.md` §8).
- Encounter cards show every subsection of the player's section, with only the applicable one active (`DECISIONS.md` §4.1).

### T3.5 — V1 · `TODO` (T3.4)
- Done when the decided V1 items in `DECISIONS.md` §8 are met.

---

## After V1 (not yet broken into tasks)

Official solo mode → general bot → host and room code (relay behind the network interface) → *Unfinished Business* pack → Spanish → sound and music.

---

## Deferred decisions

| Decision | Decide at |
|---|---|
| Expansion card images: [Mod 01] or (Mod 02) | before the first Unfinished Business transcription batch |
| UI technology for cards | T3.3 |
| UI technology for panels and menus | first screen UI task |
| Unity MCP server | only if the CLI proves insufficient |
| Offer/accept flow for credit trades (R-9.6) | when trading reaches the UI |

---

## Changelog

| Date | Change |
|---|---|
| 2026-11-04 | T0.3 and T2.7 done (v1 and v2 extraction). T2.8 pilot done; batches listed. Source of card text settled (images; photos only for doubts). T2.7 follow-up for encounter numbering. New T0.6 to bring the tools and the transcription format into the repository (T0.2 was already done). Encounter display notes in T2.6 and T3.4. Transcription (T2.8) and effect inventory (T2.1) moved ahead of T0.4. |
| 2026-10-24 | Extraction pilot moved before the Unity project (T0.3 ↔ T0.4). Git LFS dropped. Plan stated as a guide. Full extraction split from transcription (T2.7, T2.8). |
| 2026-10-24 | Initial plan. Unity spike dropped; its useful checks folded into the Unity setup task. |
