# DECISIONS.md — Outer Rim digital

Last updated: 2026-10-24

Personal, non-commercial project: a digital version of the *Star Wars: Outer Rim* board game, built in Unity with Claude Code, to play with friends on PC.

## How to use this document

This is a **guide, not a rulebook**. It records the structural decisions so the work stays consistent. Work may happen out of order, or depart from what is written here, as long as that does not hurt the project. When a departure becomes the new direction, update this file afterwards.

Details (full card lists, animations, screens) live in their own documents as they get defined. Rules live in `RULES.md`; the task order lives in `PLAN.md`, which is also a guide.

**Status labels:**
- **Decided** — set by the project owner.
- **Adopted** — technical direction discussed and accepted; confirmed during implementation.
- **Suggested** — proposed, not yet confirmed.
- **Open** — needs a decision.

---

## 1. Scope (Decided)

- Game modes in the final product:
  - **Official solo mode** from the board game.
  - **Against bots**: bots as other players.
  - **Online multiplayer** with friends (see section 6).
- 1 to 4 players, as in the original.
- Modular content chosen when creating a game: base game only, base + *Unfinished Business*, and unofficial packs (e.g. Mandalorian).
- Development order: base game first, then *Unfinished Business*, unofficial packs last. The structure must make adding packs easy.
- Platform: Windows only.

## 2. Rules (Decided)

- 1:1 fidelity with the original rulebooks, including errata.
- Single source of truth: `RULES.md`, consolidated from the Learn to Play book, the *Unfinished Business* rulebook and the Living Rules Reference 1.1 (authoritative). Written; owner review pending.
- The owner has the physical base game and expansion.
- The character is part of V1 (skill tests depend on it).
- Everything the expansion changes is tagged `[UB]` in `RULES.md`, which also defines the core's extension points.

## 3. Content and art (Decided)

- From Tabletop Simulator, **only the image files** of the *Outer Rim* mods are used (cards, map, tokens), as a source of good-resolution scans. More than one mod may be used. Credits go in `CREDITS.md`.
- **Extraction feasibility is validated first**, on a pilot set, before the Unity project is built. Without usable images there is no project.
- Interactive objects are built in Unity. Each card type (Job, Contact, Encounter and so on) has **one template** filled from data: title, art, text, icons and values come from data files.
- Only the cropped art is taken from a scan. Frame and text are recreated so they can be translated.
- Languages: English and Portuguese first; Spanish later. Player-facing text lives in localization files, separate from code.
- *Suggested:* raw source images stay outside the repository (or in a git-ignored folder); only processed assets are committed.

## 4. Visual and audio (Decided)

- Mixed 2D and 3D: a map with some 3D elements, nearly all art in 2D.
- Simple animations. The known ones are dice rolling and the ship moving from its start space to its end space. Other simple animations will be defined as development goes.
- Some sound effects and background music, in the final phase.

## 5. Engine, tools and conventions (Decided)

- Unity 6.6 now, moving to 6.7 LTS when it is released.
- Claude Code for development, in small tasks, each with a done criterion and a test.
- Claude Code drives Unity through the **Unity CLI** and the `com.unity.pipeline` package (both beta/experimental). An MCP server is only considered if the CLI proves insufficient.
- No up-front technical spike in Unity.
- UI technology (UI Toolkit, uGUI, or plain 3D objects) is chosen when the first UI objects are built. Changing it later is acceptable.
- Version control: **plain Git, no LFS**. One branch per task. `CLAUDE.md` at the repository root.
- **Language:** all project documents, code and identifiers are written in English, and all file names are in English. Other languages exist only as in-game translations. Conversations with the assistant are in Brazilian Portuguese.

## 6. Multiplayer and games (Decided)

- At first, one player hosts inside the game itself, with no online servers.
- Joining by room code is the preferred connection method.
- Saving and resuming a game midway is a requirement.

## 7. Technical direction (Adopted)

- **Rules core in C# with no `UnityEngine` references.** It lives in the Unity project as its own assembly (no engine references), and the same source files are also compiled by a plain .NET test project.
  - Why: rule tests run in seconds without opening the editor; seeded bot-vs-bot games can run by the thousand; rules cannot leak into the UI; Claude Code can verify its work without Unity running.
  - What is still used from Unity: everything outside the rules — rendering, UI, input, animation, audio, Netcode/Relay, the Test Framework, the CLI. Those live in the Unity layer, which calls the core.
  - Revisit if it causes real friction.
- The core receives commands, returns events, uses seeded randomness and has serializable state.
- The core decides dice results; the UI only animates toward them.
- Cards, the map graph and saves are JSON with a format version number; cards and rules have stable IDs. The map is a graph (spaces, positions, paths) kept separate from its art.
- Content comes in **packs** with a manifest (id, version, dependencies, cards, characters, ships, rule hooks). In an online room all players must have the same packs and versions; a save records the packs it used.
- Networking sits behind a simple interface (send command, receive event), so the connection type can change without touching the rules.
- Before V1, define the **full inventory** of card types, effect types and components of the base game; implement one slice at a time.

## 8. V1

Decided:
- Choose between the two starting ships.
- Move around the map, with distance set by the chosen ship.
- An initial map, ready to use.
- At least one of each kind of interaction: one card of each type, one contact, one encounter.
- One character.

Suggested (not confirmed):
- Save and load already in V1.
- A screenless "V0": the core running in a terminal with automated test games.
- First end-to-end test: one Job card (template, three sample cards, English and Portuguese text, cropped art).
- Official solo mode before the general bot.
- Online connection through Unity Relay (free at this scale), behind the network interface.

## 9. Open

- Inventory of effect, condition and target types (`PLAN.md` T2.1).
- List of Tabletop Simulator mods used as image sources (needed for `PLAN.md` T0.3).
- Source of card text: transcription from mod images or from photos of the physical cards. The extraction pilot will inform this.
- Render pipeline: URP by default *(Suggested)*.
- Distribution: zip by link, with a version check when joining a room *(Suggested)*.
- Offer/accept flow for credit trades between players (`RULES.md` R-9.6, R-9.7).

Resolved by `RULES.md`: skill tests and the character's role (R-6), movement (R-5), victory (R-18), starting ships (R-17.1; their values come from card data), expansion changes (`[UB]`), official solo mode (R-23).

## 10. Out of scope for now

- Time estimates.
- Unofficial packs (Mandalorian).
- Spanish.
- Music and sound effects.
- Bot difficulty levels and advanced bot strategy.

## 11. Way of working

- One chat per topic, with a clear name. At the end of important conversations, summarize the decisions and update this file.
- The repository is the source of truth; the claude.ai Project receives a copy (via GitHub sync).
- Work involving files, scripts and Git happens in Claude Code; Project chats are for deciding and reviewing.
- These documents guide the work; they do not freeze it.
