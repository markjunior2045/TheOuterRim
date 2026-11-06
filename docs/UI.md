# UI.md — Outer Rim digital: user interface

**Status: not decided.** The interface is decided by the owner in a dedicated phase held in the claude.ai Project chat (`PLAN.md` T3.0). When it ends, this file is replaced by the decisions and the screen tasks (T3.6 to T3.10) start from them. Until then nothing below is a decision: it is the brief for that phase.

An earlier draft (an application layout with side panels) was set aside on 2026-11-06: the owner wants something closer to the game on the table.

## 1. References from the owner

In `C:\TheOuterRimProject\Other\` (not in the repository):

- `Board.png` — the Tabletop Simulator table: the map as a semicircular arc on a starfield, endcaps with the patrol stacks at both ends, the six market decks in a row in the middle, the encounter decks inside the arc near their planets, standees and patrols on the map, dice and tokens around.
- `PlayerBoard.png` — one player's area as the physical components: the ship sheet (cargo, mod and crew slots, hyperdrive, ship combat, hull), the player board (fame track 0 to 12, the four reputation sliders, gear slots, job or bounty slots) and the character card.

## 2. What the interface must show and let the player do

From the rules core, which already plays the whole base game (`PLAN.md` T3.1):

- **The map:** 11 planets, 34 navpoints, the Maelstrom, 51 paths (`data/map/`, with a normalised position for every space, contact space and patrol stack on its piece image). The pieces are segments of an arc; their placement in the assembled map is still to be recorded (T3.6).
- **On the map:** each player's ship, the four patrols with their level, contact tokens (facedown or faceup) on the planets' contact spaces, the patrol stacks.
- **Market:** six decks, top card faceup; discard up to two from one deck, buy one with barter (R-16).
- **Encounter decks:** seven, one per pair of spaces; when a card is drawn, only the player's section is shown, every subsection visible and the applicable one active (`DECISIONS.md` §4.1).
- **Databank cards** resolving (contacts, jobs, cargo checks), **ship sheets** and their supply, **character cards** (two sides).
- **Each player:** credits, fame against the requirement, reputation with four factions, character damage against health, ship damage against hull, assets in slots, goal tokens, secrets (hidden from the others), defeated.
- **Turn flow:** the three steps (planning, action, encounter) and what can be done in each.
- **Decisions:** about 120 kinds of question the rules ask mid-effect (choose an option, resolve a tie, choose an order), sometimes to a player who is not the current one (`RULES.md` §26.2).
- **Dice:** rolls with windows to reroll, turn or add results; the core decides the result, the screen animates toward it (`DECISIONS.md` §7).
- **The game log**, trades between players (T3.2), save and load, the new-game setup, the victory screen.
- **V1 seats:** 1 to 4, each human on the same screen or a bot; private information (secrets, peeked cards) must not show to the other humans (`DECISIONS.md` §8).

## 3. Constraints already decided

- Windows PC, Unity 6.6 with URP; mixed 2D and 3D, nearly all art 2D (`DECISIONS.md` §4, §5).
- The prototype and V1 show the Tabletop scans as they are, English only; card templates with recreated frame and translatable text come after V1 (`DECISIONS.md` §3). The layout decided now is the one the final game keeps; the templates, animations and sound change its finish, not its arrangement.
- Known animations: the ship moving space by space; dice rolling. Patrols moving is planned too.
- The interface only reads the state and sends commands; every rule stays in the core.

## 4. Questions for the phase

1. **Camera:** a flat view from above, or a tilted table in perspective as in `Board.png`? Can the player pan, zoom, rotate?
2. **Player areas:** the acting player's board docked on screen (as in `PlayerBoard.png`) and the others minimised, or every board on the table around the map?
3. **Pieces:** flat tokens and standees (the scans), or simple 3D models standing on the map?
4. **Cards:** how a card is read (zoom on hover, a larger view), how decks and hands sit on the table, how an encounter card opens.
5. **What sits above the table:** decision questions, the log, the menu, the step indicator — overlays, or part of the table?
6. **Hot-seat:** a hand-off screen only when private information would show, or before every human turn?
7. **Resolution and scaling:** 1920×1080 as the reference; the smallest size to support.
8. **UI technology:** UI Toolkit, uGUI or world-space objects (cards and boards as objects on the table); can be left to the implementation once the look is set.
