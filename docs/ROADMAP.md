# ROADMAP.md — Outer Rim digital

Short view of where the project is going. Task details live in `PLAN.md`; decisions in `DECISIONS.md`. Order set by the owner on 2026-11-04.

Last updated: 2026-11-05.

| # | Stage | What it delivers | Tasks | Status |
|---|---|---|---|---|
| 1 | Rules | `RULES.md`, consolidated from the three rulebooks | T0.1 | Done |
| 2 | Images | extraction and sorting of the Tabletop Simulator scans | T0.3, T2.7 | Done |
| 3 | Transcription | printed text of every base game card, plus patrols, player boards and map | T2.8, T0.5 | Base game done |
| 4 | Patterns | inventory of objects, timings and effects (`EFFECTS.md`), with the expansion's extension points | T2.1 | Done |
| 5 | Data | card data format, validator, base pack; small sets encoded | T2.2, T2.4 | Done (base) |
| 6 | Core | rules engine in plain C#, tested with `dotnet test`: state, turn, movement, dice, combat, patrols, assets | T1.1 to T1.9 | **In progress** (T1.1 to T1.3 done) |
| 7 | Effects and content | effect engine; market, encounters, databank and secrets encoded and playable in the core | T2.3, T2.5, T2.6 | To do |
| 8 | V0 | seeded bot games in a terminal, thousands of them, checking every rule invariant | T3.1 | To do |
| 9 | Prototype (V1) | Unity project; map, movement and cards on screen using the Tabletop scans; one feature at a time | T0.4, T3.2 to T3.5 | To do |
| 10 | After V1 | official solo mode, bots, online play with a room code, *Unfinished Business* (transcription and content) | — | Later |
| 11 | Polish | card templates with recreated frame and text, Portuguese, better graphics and animations, then Spanish, sound and music | — | Last |

Principles:
- Rules and data first, screen later: the core plays the game before Unity shows it.
- The expansion is planned for (extension points in `EFFECTS.md` §10) but built after V1.
- The prototype uses the Tabletop scans as they are; final art and translation come once everything works.
