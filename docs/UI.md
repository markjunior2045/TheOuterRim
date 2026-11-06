# UI.md — Outer Rim digital: screen layout

**Status: draft, awaiting the owner's review (`PLAN.md` T0.4).** Once approved, each screen task (T3.6 to T3.9) fills its part and may adjust it; changes of substance come back here.

What this document fixes: where things sit on screen and how the player interacts. What it does not fix: the visual style. The prototype shows the Tabletop Simulator scans as they are (`DECISIONS.md` §3, prototype exception); frames, fonts and colours come with the card templates after V1.

Target resolution 1920×1080; it must stay usable at 1366×768 (panels shrink, card zoom covers more of the board).

## Technology (suggested, confirmed in T0.4)

- **On screen** (panels, dialogs, cards, menus, log): UI Toolkit.
- **Board** (map pieces, standees, patrols, contact tokens, patrol stacks): sprites in world space, orthographic camera with pan and zoom.
- Every input becomes a core command sent through the session's command channel (`DECISIONS.md` §7); the views only follow the events and the player view (`PLAN.md` T3.4).

## 1. Game screen

```
┌───────────────────────────────────────────────────────────────────────────────────────────┐
│ Round 12 · ▶ Player 2 — Ketsu Onyo · [Planning] › Action › Encounter · Fame goal 10  [≡] │ top bar
├──────────────┬───────────────────────────────────────────────────────────┬────────────────┤
│ PLAYERS      │                                                           │ MARKET         │
│ ┌──────────┐ │                                                           │ ┌────┐ ┌────┐  │
│ │▶ P2 Ketsu│ │                                                           │ │gear│ │carg│  │
│ │ 8,000  ★3│ │                    BOARD (pan / zoom)                     │ └────┘ └────┘  │
│ │ ♥ 1/4 ⛭0/5│ │                                                           │ ┌────┐ ┌────┐  │
│ │ rep ▲ ● ▼ ●│ │   map arc, standees, patrols with level, contact tokens, │ │job │ │bnty│  │
│ │ [asset][asset]│ │  reachable spaces highlighted in the planning step    │ └────┘ └────┘  │
│ │ goal ●●○ │ │                                                           │ ┌────┐ ┌────┐  │
│ └──────────┘ │                                                           │ │lux │ │ship│  │
│ ┌──────────┐ │                                                           │ └────┘ └────┘  │
│ │  P1 Lando│ │                                                           ├────────────────┤
│ │ (compact)│ │                                                           │ LOG            │
│ └──────────┘ │                                                           │ P2 moves to    │
│ ┌──────────┐ │                                                           │ Kessel.        │
│ │  P3 Bot  │ │                                                           │ Hutt Patrol    │
│ └──────────┘ │                                                           │ Lvl 2 (Game)…  │
├──────────────┴───────────────────────────────────────────────────────────┴────────────────┤
│ ACTIONS: [Gain 2,000] [Recover damage] (click a highlighted space to move) [Use ability ▾] │ action bar
│          DICE: ⚀ ⚀ ⚀                                                                        │
└───────────────────────────────────────────────────────────────────────────────────────────┘
```

| Area | Size at 1080p | Content |
|---|---|---|
| Top bar | 48 px | round, acting player, the three steps with the current one marked, fame requirement, menu (save, load, quit) |
| Players (left) | 320 px | one panel per player in turn order. The acting player's panel is open: character card (faceup side, click to zoom), ship sheet, credits, fame, reputation with the four factions, damage against health and against hull, assets in their slots, goal tokens, number of secrets, defeated mark. The others are compact (name, character, credits, fame, damage) and open on click. |
| Board (centre) | the rest | the map; pieces as in the state; in the planning step, spaces reachable this move are highlighted and a click picks the path (`PLAN.md` T3.4); hovering a contact, patrol or player shows a tooltip |
| Market (right, top) | 360 px wide | the six decks with their faceup top card; in the action step, buy and discard buttons appear on the decks the player may use |
| Log (right, bottom) | 360 px wide | game log lines from the presentation layer (`PLAN.md` T3.3), newest last, scrollable |
| Action bar | 140 px | the commands available now for the step (planning options; market, deliver, trade, abilities, end step; the encounter choices), and the dice tray |

## 2. Decisions and cards

- **Decision dialog:** every `PendingDecision` opens one dialog over the board, with the question and its options. Options that are cards show the card; options that are spaces highlight them on the board and can be clicked there; "choose order" is a list the player reorders. A decision for a player who is not the acting one names that player in the dialog's title.
- **Card zoom:** hovering any card (market, assets, decision options) shows it at full readable size next to the cursor; a right-click pins it until closed.
- **Encounter card:** when drawn, it opens large in the centre with only the player's section shown; every subsection of that section is visible, the one that applies is active, the others dimmed (`DECISIONS.md` §4.1). The resolution then goes on through decision dialogs.
- **Databank cards and contacts:** a databank card resolving opens like an encounter card; a contact token turns faceup on the board.
- **Dice:** rolled in the tray with a short animation toward the result the core already decided (`DECISIONS.md` §7); dice windows (reroll, turn, add) are decision dialogs pointing at the dice.

## 3. Hot-seat hand-off

```
┌──────────────────────────────────────────────┐
│                                              │
│        Pass the game to Player 3             │
│        Doctor Aphra                          │
│                                              │
│        [ I am Player 3 — continue ]          │
│                                              │
└──────────────────────────────────────────────┘
```

Covers the whole screen when the player who must act changes and private information would show (secrets, a peeked card, a decision only that player sees). Not shown between turns when there is nothing private, nor for bot seats.

## 4. New game

```
┌────────────────────────────────────────────────────────────┐
│ NEW GAME                                                   │
│ Seat 1  [Human ▾]  [Ketsu Onyo ▾]      Ship [G9 Rigger ▾]  │
│ Seat 2  [Bot   ▾]  [Random     ▾]      Ship [G-1A      ▾]  │
│ Seat 3  [Empty ▾]                                          │
│ Seat 4  [Empty ▾]                                          │
│ Fame requirement [10]   Seed [random]                      │
│                       [Load game]  [Start]                 │
└────────────────────────────────────────────────────────────┘
```

Characters show their card on hover. Two seats cannot take the same character (R-3.11). After the game: a victory screen with the final fame of everyone and the tiebreak when it applied (R-18.16), then back here.

## Open points for the owner

1. The left/right split (players left, market and log right): or players along the bottom, like seats around a table?
2. Compact panels for the other players: is name, character, credits, fame and damage enough at a glance, or should their reputation also show?
3. Hand-off screen only when private information would show (above), or before every human turn for simplicity?
