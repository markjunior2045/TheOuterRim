# DECISIONS.md — Outer Rim digital

Last updated: 2026-11-04 (roadmap and prototype art exception)

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
- Work order (2026-11-04): card patterns → data → rules core → terminal V0 → Unity prototype, adding features one at a time → graphics, animation and sound polish last. Details in `PLAN.md`, "Roadmap".
- Platform: Windows only.

## 2. Rules (Decided)

- 1:1 fidelity with the original rulebooks, including errata.
- Single source of truth: `RULES.md`, consolidated from the Learn to Play book, the *Unfinished Business* rulebook and the Living Rules Reference 1.1 (authoritative). Reviewed by the owner on 2026-11-04.
- The owner has the physical base game and expansion.
- The character is part of V1 (skill tests depend on it).
- Everything the expansion changes is tagged `[UB]` in `RULES.md`, which also defines the core's extension points.

## 3. Content and art (Decided)

- From Tabletop Simulator, **only the image files** of the *Outer Rim* mods are used (cards, map, tokens), as a source of good-resolution scans. Credits go in `CREDITS.md`.
- **Extraction feasibility is validated first**, on a pilot set, before the Unity project is built. Without usable images there is no project.
- Interactive objects are built in Unity. Each card type (Job, Contact, Encounter and so on) has **one template** filled from data: title, art, text, icons and values come from data files.
- Only the cropped art is taken from a scan. Frame and text are recreated so they can be translated.
- **Prototype exception (Decided, 2026-11-04):** the template rule above is the final model. Until the templates are built (after V1), the prototype shows the full TTS scans of cards, sheets, tokens and map as they are, so it is English only. The game rules still read the card data files, never the image.
- Languages: English and Portuguese first; Spanish later. Player-facing text lives in localization files, separate from code.
- **Card text** is transcribed from the mod images (pilot of 2026-11-04: all text legible); photos of the physical cards only settle doubts. Raw printed text is stored first (format in `docs/TRANSCRIPTION_FORMAT.md`) and mapped to effects later (`PLAN.md` T2.8, T2.1). The base game is fully transcribed (2026-11-04). Errata are applied at mapping time, not in the transcription (`RULES.md` §24).
- Raw source images stay outside the repository, in `C:\TheOuterRimProject\RawGameFiles`; only processed assets are committed.

### 3.1 Image sources

- Two mods are used, referred to in conversation and documents as:
  - **[Mod 01]** — Workshop `1790581987`, "Star Wars: Outer Rim + Unfinished Business [scripted]". **Primary source.**
  - **(Mod 02)** — Workshop `2820783326`, "Star Wars Outer Rim: Unfinished Business (Scripted)". **Secondary source.**
  - The bracket style mirrors each mod's own title. Folder and file names use `mod01` / `mod02` instead, because brackets act as wildcards in PowerShell paths.
- Source per component (from the extraction pilot, 2026-11-04):
  - **Map tiles:** [Mod 01]. (Mod 02)'s map is a fan-made layout based on the board game, not the original.
  - **Cards, ship sheets, patrols, characters, standees:** [Mod 01]. It has equal or higher resolution in every category, and (Mod 02) is missing 12 patrol and 6 ship images from its cache.
  - **Player boards and dice:** [Mod 01] (four colours: green, red, yellow, white). Their PNGs have a corrupt colour profile chunk that Pillow rejects; the extractor decodes them with OpenCV (2026-11-04). Until then the (Mod 02) copies were used as a fallback.
  - **Ambition sheets:** (Mod 02). [Mod 01] has only 4 of 8; the other 4 links are dead in the mod itself.
  - **Names and categories** (deck names, contact classes, patrol and ship names): (Mod 02), used as an index and matched to [Mod 01] images.
- [Mod 01] keeps base game and expansion in separate decks, which is used to split content into the two packs.

## 4. Visual and audio (Decided)

- Mixed 2D and 3D: a map with some 3D elements, nearly all art in 2D.
- Simple animations. The known ones are dice rolling and the ship moving from its start space to its end space. Other simple animations will be defined as development goes.
- Some sound effects and background music, in the final phase.

### 4.1 Encounter cards in the interface (Decided, 2026-11-04)

- The core keeps **one encounter deck per pair of spaces**, as in the board game (`RULES.md` R-15.73, R-15.79). A card kept by one planet's player (secret or asset, R-15.84) also leaves the other planet's section out of the deck.
- The interface shows the player **only the section for their space**; the other planet's section is not shown. This is a display choice, not a rule change.
- Reputation subsections: the player **cannot choose** one. The subsection that applies to their game state when the card is drawn is resolved (`RULES.md` R-15.77; Living Rules, "Encounter Cards"). The interface shows every subsection of the player's section, with only the applicable one active and the others dimmed, so the player sees what their reputation gained or cost them.
- A split deck per planet would be a house rule; it is not planned.

### 4.2 Digital adaptations of table conventions (Decided)

- **Dice for enemies (2026-11-05):** a player rolls only their own dice (skill tests and their side of a combat). For a patrol, contact or card enemy the game rolls; the "player to the left rolls" convention of the board game (`RULES.md` R-7.9, R-7.12) does not apply, and R-7.20 becomes moot. Combat results are unchanged: the enemy's dice still count as the defender's roll. Events and the interface name who rolled: the character and player for a player ("Ketsu Onyo (Player 1)"), the enemy followed by "(Game)" otherwise ("Hutt Patrol Lvl 2 (Game)", "TIE fighters (Game)").

## 5. Engine, tools and conventions (Decided)

- Unity 6.6 now, moving to 6.7 LTS when it is released.
- Claude Code for development, in small tasks, each with a done criterion and a test.
- Claude Code drives Unity through the **Unity CLI** and the `com.unity.pipeline` package (both beta/experimental). An MCP server is only considered if the CLI proves insufficient.
- No up-front technical spike in Unity.
- UI technology (UI Toolkit, uGUI, or plain 3D objects) is chosen when the first UI objects are built. Changing it later is acceptable.
- Version control: **plain Git, no LFS**. One branch per task. `CLAUDE.md` at the repository root.
- **Language:** all project documents, code and identifiers are written in English, and all file names are in English. Other languages exist only as in-game translations. Conversations with the assistant are in Brazilian Portuguese.
- **Folder layout:**
  - `C:\TheOuterRimProject\TheOuterRim` — the repository (game and `docs/`).
  - `C:\TheOuterRimProject\RawGameFiles` — raw game files (`tts\`, `sliced\`, `photos\`); never versioned.
  - `C:\TheOuterRimProject\Other` — reference material not used in the build (UI screenshots, PDF rule extraction).

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
- **JSON library (2026-11-05):** Newtonsoft.Json in the core: Unity ships it as `com.unity.nuget.newtonsoft-json` (declared in the core's `package.json`, referenced by its `.asmdef`), and the test project uses the same library from NuGet. `System.Text.Json` is not available on Unity's .NET Standard 2.1.
- **Random numbers (2026-11-05):** the core uses its own PCG32 generator, not `System.Random`, so the same seed gives the same game in .NET and in Unity; its state is part of the save.
- **Game data (2026-11-05):** card data is a tree of effect primitives (`DATA_FORMAT.md`), checked against one registry (`data/schema/registry.json`) that the Python validator reads now and the C# loader reads later. Card ids are the image ids from the extraction. Card text lives only in localization files; the English file is generated from the transcriptions.
- Before V1, define the **full inventory** of card types, effect types and components of the base game; implement one slice at a time.

### 7.1 Image extraction tooling (Adopted)

- **v1 — `tts_extract.py`** (done): generic extractor for any TTS mod. Lists local mods, inventories referenced images against the TTS cache, slices card sheets by the grid declared in the mod, writes a manifest and report. Used for the pilot.
- **v2 — `sort_outer_rim.py`** (done): specific to this project, not generic. A fixed mapping says what each sheet of each mod contains (component type, deck, pack, front/back); it picks the source per component (section 3.1), writes into folders by pack and deck, names every image with a stable ID that becomes the card ID, matches names from (Mod 02) by visual similarity, and generates the manifest and `CREDITS.md`. Built after a visual review of the pilot output.

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
- First end-to-end test: one Job card (data from the card file, full scan shown, three sample cards). The template version with English and Portuguese text comes after V1 (section 3).
- Official solo mode before the general bot.
- Online connection through Unity Relay (free at this scale), behind the network interface.

## 9. Open

- **Expansion card images:** [Mod 01]'s expansion scans have the highest resolution but look washed out with white speckles. Options: use (Mod 02)'s expansion cards where they look better despite lower resolution, or clean up [Mod 01]'s art (levels and denoise). Decide after a side-by-side review.
- Render pipeline: URP by default *(Suggested)*.
- Distribution: zip by link, with a version check when joining a room *(Suggested)*.
- Offer/accept flow for credit trades between players (`RULES.md` R-9.6, R-9.7).

Resolved: the effect inventory (`EFFECTS.md`, 2026-11-05). Resolved by `RULES.md`: skill tests and the character's role (R-6), movement (R-5), victory (R-18), starting ships (R-17.1; their values come from card data), expansion changes (`[UB]`), official solo mode (R-23).

## 10. Out of scope for now

- Time estimates.
- Unofficial packs (Mandalorian).
- Unofficial extras in (Mod 02) (5-player support, extra factions).
- Spanish.
- Music and sound effects.
- Bot difficulty levels and advanced bot strategy.

## 11. Way of working

- One chat per topic, with a clear name. At the end of important conversations, summarize the decisions and update this file.
- The repository is the source of truth; the claude.ai Project receives a copy (via GitHub sync).
- Work involving files, scripts and Git happens in Claude Code; Project chats are for deciding and reviewing.
- These documents guide the work; they do not freeze it.
