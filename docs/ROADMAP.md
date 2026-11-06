# ROADMAP.md — Outer Rim digital

Short view of where the project is going. Task details live in `PLAN.md`; decisions in `DECISIONS.md`. Order set by the owner on 2026-11-04.

Last updated: 2026-11-05 (V0 done).

Status: `Done` · `Next` · `To do` · `Later`.

## Overview

| # | Stage | What it delivers | Status |
|---|---|---|---|
| 1 | Rules | `RULES.md`, consolidated from the three rulebooks | Done |
| 2 | Images | extraction and sorting of the Tabletop Simulator scans | Done |
| 3 | Transcription | printed text of every base game card, plus patrols, player boards and map | Base game done |
| 4 | Patterns | inventory of objects, timings and effects (`EFFECTS.md`), with the expansion's extension points | Done |
| 5 | Data | card data format, validator, base pack; small sets encoded | Done (base) |
| 6 | Core | rules engine in plain C#, tested with `dotnet test` | Done |
| 7 | Effects and content | effect engine; market, encounters, databank and secrets playable in the core | Done (base) |
| 8 | V0 | seeded bot games in a terminal, checking every rule invariant | Done |
| 9 | Prototype (V1) | Unity project; map, movement and cards on screen with the Tabletop scans | **Next** |
| 10 | After V1 | solo mode, bots, online play, *Unfinished Business* | Later |
| 11 | Polish | card templates, Portuguese, graphics, animation, Spanish, sound | Last |

## Sub-steps

### 1. Rules · Done
- T0.1 `RULES.md` with rule ids, reviewed by the owner — Done
- Owner clarifications added as they come up (R-9.8, R-25.14 to R-25.17, patrol movement) — ongoing

### 2. Images · Done
- T0.3 extraction pilot (`tts_extract.py`) — Done
- T2.7 full extraction and sorting (`sort_outer_rim.py`), including the four player boards and the die — Done

### 3. Transcription · Base game done
- T2.8 base game cards, 242 of 242 — Done
- Patrol tokens (16) and player boards (4) — Done
- T0.5 map geometry (`data/map/`, validator, overlays checked by the owner) — Done
- T2.8 *Unfinished Business* cards — Later (with the expansion)

### 4. Patterns · Done
- T2.1 `EFFECTS.md`: objects, ability shape, timings, effect primitives, conditions, custom handlers, `[UB]` extension points, coverage of every base card — Done

### 5. Data · Done (base)
- T2.2 data format, registry, base pack manifest, English strings, validator and tests — Done
- T2.4 patrols, player boards, contacts, characters, ship sheets encoded — Done
- T2.4 expansion sets (sheets, contacts, characters, ambitions) — Later

### 6. Core · Done
- T1.1 state, commands, events, seeded RNG, save/load — Done
- T1.2 pending decisions (choose, tie-break, order) and save while paused — Done
- T1.3 map graph: adjacency, distance, nearest with ties — Done
- T1.4 turn loop, planning step, movement — Done
- T1.5 dice and skill tests — Done
- T1.6 combat (the game rolls for enemies) — Done
- T1.7 credits, reputation, fame, defeat, victory — Done
- T1.8 patrols: stacks, spawning, movement, encounters — Done
- T1.9 assets, slots, ships — Done

### 7. Effects and content · Done
- T2.3a effect interpreter, passive bonuses, step abilities — Done
- T2.3b triggers, replacements, dice windows — Done
- T2.3c personal and ship goals — Done
- T2.9 character setup from the starting databank cards (#90 to #92) — Done
- T2.5 market — Done: decks, discard and buy, barter, patrol movement icons, ship cards, Used Ship for Sale — encode bounty, job, luxury and ship decks
- T2.6 encounters, contacts, databank and secrets — Done: encounter decks, contact tokens on the map, databank procedures (cargo check, job steps, favors, sabacc) — encode the seven encounter decks and the databank
- Remove the remaining stubs (encountering a space) — Done

### 8. V0 · Done
- T3.1 terminal game with a random bot; 1,000 seeded games without a crash or a broken invariant — Done (the games found 12 engine defects, all fixed)

### 9. Prototype (V1) · Next
- T0.4 Unity project, core as a local package, Unity CLI wiring
- T3.2 map and movement on screen
- T3.3 first card object (data from the card file, full scan shown)
- T3.4 remaining V1 interface: ship choice, one character, one card of each type, one contact, one encounter
- T3.5 V1 complete (`DECISIONS.md` §8)

### 10. After V1 · Later
- Official solo mode (AI decks)
- General bot
- Online play: host and room code, relay behind the network interface
- *Unfinished Business*: transcription, data and content

### 11. Polish · Last
- Card templates with recreated frame and text (`DECISIONS.md` §3)
- Portuguese
- Better graphics and animations
- Spanish
- Sound and music

## Principles
- Rules and data first, screen later: the core plays the game before Unity shows it.
- The expansion is planned for (extension points in `EFFECTS.md` §10) but built after V1.
- The prototype uses the Tabletop scans as they are; final art and translation come once everything works.
