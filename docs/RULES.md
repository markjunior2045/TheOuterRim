# RULES.md — Star Wars: Outer Rim

Rules specification for implementation. Single source of truth for this project.

**Sources** (precedence order):

1. `SWOR_LIVING_RULES` — Living Rules Reference, version 1.1 (2022-10-06). **Authoritative.**
2. `SWOR_UF_RULES` — *Unfinished Business* expansion rulebook.
3. `SWOR_LEARN_TO_PLAY` — base game Learn to Play book.

The Living Rules states that it takes precedence over the Learn to Play book and the printed Rules Reference. Where the three texts disagree, this document follows the Living Rules and records the disagreement in a note.

A card takes precedence over this document. When a card and a rule can both be followed, both apply.

**Language:** this document, the code, identifiers and all project documentation are written in English. Player-facing text is translated separately, in the localization files.

---

## 0. How to use this document

### 0.1 Rule IDs

Every rule has a stable ID in the form `R-<section>.<number>`. Use them in code comments, test names and commit messages:

```csharp
// R-10.4: a tie in combat goes to the attacker
```

IDs are never reused. If a rule is removed, its ID stays retired.

### 0.2 Icon placeholders

The source PDFs lost their icons during extraction. Wherever the original text had an icon, this document uses a placeholder. Replace them with sprites during implementation.

| Placeholder | Meaning |
|---|---|
| `{HIT}` | hit die result |
| `{CRIT}` | crit die result |
| `{FOCUS}` | focus die result |
| `{CREDITS}` | credits |
| `{FAME}` | fame |
| `{GROUND_COMBAT}` | ground combat value |
| `{HEALTH}` | character health |
| `{SHIP_COMBAT}` | ship combat value |
| `{HULL}` | ship hull |
| `{HYPERDRIVE}` | hyperdrive (movement) |
| `{DROID}` | droid icon |
| `{REP_POS}` `{REP_NEU}` `{REP_NEG}` | positive, neutral, negative reputation |
| `{FACTION_REBEL}` `{FACTION_IMPERIAL}` `{FACTION_HUTT}` `{FACTION_SYNDICATE}` | factions |
| `{DECK_BOUNTY}` `{DECK_CARGO}` `{DECK_GEAR}` `{DECK_JOB}` `{DECK_LUXURY}` `{DECK_SHIP}` | market decks |
| `{ACROSS_THE_CORE}` | Across the Core icon |
| `{AMBITION_DASH}` `{AMBITION_NUM}` `{AMBITION_INF}` `{AMBITION_FAME}` | ambition goal types |

Where the source text read only "reputation ()" with the icon stripped, the intended icon was inferred from context and flagged **(icon inferred)** where there was any doubt.

### 0.3 Source markers

- Unmarked: base game rule, confirmed in the Living Rules.
- **[UB]**: applies only with the *Unfinished Business* expansion.
- **[OPT]**: optional rule, used only if players agree before the game.
- **[SOLO]**: single-player (AI) rule.
- **[?]**: ambiguous or not covered by the sources. Needs a decision from us, recorded in `DECISIONS.md`.

### 0.4 Terminology

Code identifiers use the rulebook's own terms: `PlanningStep`, `ActionStep`, `EncounterStep`, `Space`, `Planet`, `Navpoint`, `Maelstrom`, `Patrol`, `ContactToken`, `DatabankCard`, `EncounterCard`, `MarketDeck`, `Asset`, `Cargo`, `Crew`, `Gear`, `Mod`, `Job`, `Bounty`, `Secret`, `Fame`, `Reputation`, `Credit`, `Damage`, `Defeated`, `SkillTest`, `Goal`, `Ambition`, `Favor`, `DebtToken`, `Barter`, `Eliminate`, `Capture`, `Spawn`.

Two terms are easy to confuse: a **bounty** is the contract card (find and defeat a target); a **reward** is what any card pays out. Keep `Bounty` and `Reward` distinct in the model.

Translations of these terms into other languages belong in the localization files, not here.

---

## 1. Foundational rules

- **R-1.1** Each player controls one character. The rulebooks use "player" for both the person and the character. In the implementation, keep `Player` (seat, credits, fame, reputation) separate from `Character` (card, abilities, skills, health).
- **R-1.2** Dice, damage tokens, credit tokens and goal tokens are **unlimited**. Every other component is limited to what the box provides.
- **R-1.3** If there are not enough physical dice for a roll, the player records partial results, rerolls the number still needed and combines them. No limit applies in the implementation.
- **R-1.4** If a player must draw cards from a deck that has too few, they draw as many as are available, possibly zero.
- **R-1.5** Precedence: Living Rules over the Learn to Play book and the printed Rules Reference.
- **R-1.6** Precedence: card over the Living Rules. If a card and a rule can both be followed, follow both.

---

## 2. Components

### 2.1 Base game

| Component | Count |
|---|---|
| Ship sheets | 12 |
| Player boards | 4 (with 4 plastic fame markers) |
| Reputation tokens | 16 (4 per faction) |
| Patrol tokens | 16 (4 per faction) |
| Damage tokens | 40 |
| Contact tokens | 22 |
| Character standees | 8 |
| Credit tokens | 60 (40× 1,000; 16× 5,000; 4× 10,000) |
| Encounter cards | 70 (10 per type) |
| Market cards | 70 |
| Databank cards | 53 |
| Character cards | 8 |
| Reference cards | 4 |
| Goal tokens | 12 |
| Map endcaps | 2 |
| AI cards | 10 |
| Map tiles | 6 |
| Dice | 6 |

### 2.2 *Unfinished Business* expansion [UB]

| Component | Count |
|---|---|
| Core Worlds endcaps | 2 |
| Character standees | 8 |
| Encounter cards | 91 |
| Market cards | 45 |
| Character cards | 8 |
| Ship sheets | 6 |
| Databank cards | 52 |
| AI cards | 21 |
| Reference cards | 4 |
| Ambition sheets | 8 |
| Dice | 3 |
| Debt tokens | 4 |
| Orange contact tokens | 11 |
| 10,000 credit tokens | 4 |
| Damage tokens | 4 |
| Goal tokens | 12 |

- **R-2.1** [UB] Every expansion card carries the *Unfinished Business* icon, distinguishing it from base game cards. Implementation: every card record stores the content pack it belongs to.

---

## 3. Setup

Order follows Appendix 1 of the Living Rules, which already folds in the expansion.

- **R-3.1 Choose the fame requirement.** Players collectively decide how much fame is needed to win. Default 10. With ambitions, 8. Extended game, 12. See R-18.
- **R-3.2 Build the map,** left to right:
  1. Rebel and Syndicate endcap *(or a Core Worlds endcap)*
  2. Cantonica and Ord Mantell
  3. Lothal and Mon Calamari
  4. Kessel
  5. Tatooine and Nal Hutta
  6. Ryloth and Naboo
  7. The Ring of Kafrene and Takodana
  8. Empire and Hutt endcap *(or a Core Worlds endcap)*
- **R-3.3 Random map [OPT].** Shuffle the six map tiles facedown, reveal one and attach it to the Rebel/Syndicate endcap, reveal the rest in sequence attaching each to the previous, then attach the Empire/Hutt endcap to the last tile. The endcaps always stay at the ends.
- **R-3.4** [UB] Use the Core Worlds endcaps in place of the base game endcaps.
- **R-3.5 Patrols.** Sort patrol tokens by level (white dots on the back). Place each level-1 token faceup on the navpoint adjacent to the endcap space bearing its faction icon.
- **R-3.6** [UB] On the Core Worlds endcaps two patrols share one space: Rebel and Syndicate on the same navpoint of the Spinward Core endcap, Hutt and Imperial on the same navpoint of the Trailing Core endcap. New patrols spawn on those same spaces.
- **R-3.7 Patrol stacks.** Arrange the remaining tokens into four facedown stacks, one per faction, in numeric order by level: level 4 at the bottom, level 2 on top. Place each stack on the endcap space bearing its faction icon.
- **R-3.8 Contacts.** Randomize all contact tokens facedown and place one on each contact space on the map. The class (colored pips) of token and space must match.
- **R-3.9** [UB] Place one orange contact token facedown on **each planet** (on the planet space itself, not on a contact space), excluding the Core Worlds spaces.
- **R-3.10 Token supply.** Separate credit, damage and goal tokens into piles within reach of all players.
- **R-3.11 Choose characters.** Each player rolls six dice. Whoever rolls the most `{HIT}` and `{CRIT}` results is the first player; tied players reroll. Starting with the first player and proceeding clockwise, each player picks a character and takes:
  - a. the character card, "Personal Goal" side faceup;
  - b. the matching standee;
  - c. a player board with its fame marker on space "0";
  - d. 4 reputation tokens (one per faction), placed on the reputation tracks covering the faction icon (neutral);
  - e. 1 starter ship sheet, flipped to the chosen side: **G9 Rigger** or **G-1A Starfighter**;
  - f. 1 reference card;
  - g. credits by turn order: 4,000, 6,000, 8,000, 10,000.
- **R-3.12** A player must choose their starting ship **before** seeing their starting databank card.
- **R-3.13** [OPT] With favors, each player also takes the debt token matching their player board.
- **R-3.14** [OPT] With ambitions, skip the credit distribution in R-3.11.g.
- **R-3.15 Character setup.** Simultaneously, each player follows the instructions at the bottom of their character card:
  - a. take the databank card of the number given. If it is labelled "Cargo", it goes in a cargo slot above the ship sheet; otherwise it goes in a "Job or Bounty" slot below the player board;
  - b. place the standee on the starting planet named on that databank card;
  - c. apply any starting reputation by moving that faction's token to the indicated space.
- **R-3.16 Decks.**
  - a. Split market cards into six decks by card back, shuffle each, lay them in a row below the map and reveal the top card of each.
  - b. Split encounter cards into seven decks by card back (eight with the Core Worlds), shuffle each and place them beside the map.
  - c. Keep the databank in numeric order within reach of all players.
  - d. Keep unused ship sheets in a separate supply.
- **R-3.17** [UB] Before setup, fold the expansion's character, encounter, market and databank cards into the existing decks. **Replace** base game Takodana encounter card #10 with the expansion's card #10.
- **R-3.18** [UB] Shuffle the Core Worlds encounter deck and place it near one of the endcaps.
- **R-3.19** [UB] The expansion's reference cards replace the "Player Turn" side of the base game cards. The base game back ("Ways to Gain Fame" and "Skill Test") remains useful.
- **R-3.20 Ambitions [OPT].** Lay out random ambition sheets faceup equal to the number of players **plus one**. Starting with the **last player** and proceeding in **reverse turn order**, each player chooses a sheet. Remove the leftover sheet from the game. Each player then follows the setup instructions on their sheet; if those name a market card, they search the indicated deck for it, gain it, and shuffle that deck.

**Divergence note:** the Learn to Play book describes setup without expansion elements and distributes credits at its step 5. The numbering above follows the Living Rules.

---

## 4. Turn structure

- **R-4.1** Players take turns clockwise until someone wins.
- **R-4.2** Each turn has three steps, in order: **planning**, **action**, **encounter**.
- **R-4.3** If an effect ends a player's turn, that player resolves no further abilities, performs no actions and skips the remaining steps.
- **R-4.4** After all three steps, the turn ends and the player to the left begins theirs.
- **R-4.5** The **current player** is the one resolving their own turn, in any of the three steps.

### 4.1 Planning step

- **R-4.6** The player chooses and resolves **one** option:
  - **Move** up to the ship's `{HYPERDRIVE}` value (see section 5);
  - **Gain 2,000 credits**;
  - **Recover damage**: discard all damage tokens from the character card **and** the ship sheet.
- **R-4.7** Alternatively the player may resolve **one** ability preceded by the word "Planning" instead of moving, gaining credits or recovering.
- **R-4.8** If the current player is defeated, they **must** choose to recover damage. They cannot move, gain credits or use a "Planning" ability.

### 4.2 Action step

- **R-4.9** The player may perform **any number of actions, in any order**, but each action only once per action step.
- **R-4.10** The actions are: **Deliver**, **Market**, **Trade** and **resolve an "Action" ability**.
- **R-4.11** Each "Action" ability may be used once per action step. "Action" abilities cannot interrupt other abilities or actions: they are used before, after or on their own.

### 4.3 Encounter step

- **R-4.12** The player **must** resolve exactly one encounter.
- **R-4.13** Options: encounter the **space** they are in, a **patrol** in that space, or a **contact** in that space.
- **R-4.14** If a patrol is in the player's space and the player has `{REP_NEG}` reputation with its faction, they **must** encounter that patrol.
- **R-4.15** If not forced to encounter a patrol, the player chooses freely.
- **R-4.16** The player may resolve **one** ability preceded by the word "Encounter" instead of encountering their space.
- **R-4.17** A player forced to encounter a patrol cannot resolve an "Encounter" ability.
- **R-4.18** If an "Encounter" ability requires choosing another player in the same space and there is none, it cannot be resolved.
- **R-4.19** Bounty combats happen during the encounter step: they either interrupt a normal contact encounter, or, against crew, are resolved as an "Encounter" ability.

---

## 5. Map, spaces and movement

### 5.1 Spaces

- **R-5.1** There are three space types: **planets**, **navpoints** and the **Maelstrom**.
- **R-5.2** Spaces are connected by **paths** (blue lines). Two spaces are **adjacent** if a path connects them. Two map tiles are adjacent if they touch.
- **R-5.3** Each stack of patrol tokens on an endcap is adjacent to the space connected to it by a path. That is where new patrols from that stack spawn.
- **R-5.4** [UB] The two Core Worlds spaces are **not** adjacent to each other.
- **R-5.5** Some planets carry a faction icon. "Your planet's faction" is the icon on the planet the player occupies. A player not on a planet, or on a planet with no icon, has no planet faction.
- **R-5.6** The Ring of Kafrene is a planet for all game purposes, despite being depicted as two asteroids.
- **R-5.7** [UB] Each Core Worlds space carries the `{ACROSS_THE_CORE}` icon as a reminder that a player can cross the galaxy by encountering that space.

### 5.2 Core Worlds [UB]

- **R-5.8** Spinward Core and Trailing Core are together the Core Worlds spaces, located on the endcaps.
- **R-5.9** They are **planet** spaces for all purposes: players can move to them, perform market actions on them and encounter them. One exception: orange contact tokens are not placed on them during setup.
- **R-5.10** A Core Worlds space with no contact token counts as an empty contact token space and can receive one, for example when a player discards crew.

### 5.3 Maelstrom

- **R-5.11** A player who moves into the Maelstrom during their planning step ends movement immediately and proceeds to the action step.
- **R-5.12** If an ability grants extra movement or allows movement outside the player's own planning step, that player **cannot move through** the Maelstrom. They can, however, move out of it if they start there.
- **R-5.13** A player encountering the Maelstrom draws from the Kessel/Maelstrom deck. Those cards always offer some way to take an additional turn.
- **R-5.14** An additional turn is a full turn (planning, action, encounter) resolved after the current one. A player **cannot gain an additional turn while resolving an additional turn**; such gains are ignored.
- **R-5.15** Patrols move through the Maelstrom without stopping.
- **R-5.16** The Maelstrom is **not** a navpoint for ability purposes.

### 5.4 Movement

- **R-5.17** A player moves during the planning step, up to their ship's `{HYPERDRIVE}` value. They may choose to move zero spaces.
- **R-5.18** Moving from a space to an adjacent space is one space of movement.
- **R-5.19** "Gain +X `{HYPERDRIVE}`" adds to the ship's value. These are passive abilities, active while the player holds the card, and they **stack**.
- **R-5.20** A player who moves into a patrol's space **must end movement immediately**, unless they have `{REP_POS}` reputation with that patrol's faction.
- **R-5.21** A player moves freely through other players and through patrols of factions they have `{REP_POS}` reputation with.
- **R-5.22** A player starting movement in the Maelstrom or in a patrol's space can leave normally. The Maelstrom and patrols only interrupt movement on **entry**.
- **R-5.23** If an ability moves a player or patrol "directly" to a space, it is taken from its current space and placed there, **without** passing through intervening spaces.

### 5.5 Across the Core [UB]

- **R-5.24** The "Across the Core" section of a Core Worlds encounter card is resolved when the current player chooses to cross the galaxy while encountering a Core Worlds space.
- **R-5.25** When instructed to "move directly to the other Core Worlds space", the player moves their standee from one Core Worlds space to the other.
- **R-5.26** The player passes through **no** other space during that movement.
- **R-5.27** The player **ignores all patrols** during that movement.
- **R-5.28** Patrols **cannot** cross the Core Worlds or move from one Core Worlds space directly to the other, unless a card allows it.

### 5.6 Nearest

- **R-5.29** "Nearest" means the player, space or component the fewest spaces away from another. Always count the shortest distance.
- **R-5.30** If several candidates tie as nearest, the **current player** decides which are affected.
- **R-5.31** A patrol, planet or player in a player's own space is the nearest of that type to them.
- **R-5.32** When determining the nearest player, a player does **not** count themself.
- **R-5.33** [UB] When determining what is nearest, do **not** count through the Core Worlds.

---

## 6. Dice and skill tests

### 6.1 Dice

- **R-6.1** Each die has 8 faces: 3× `{HIT}`, 1× `{CRIT}`, 2× `{FOCUS}`, 2× blank.
- **R-6.2** `{CRIT}` **is not** `{HIT}` for game effects. An ability referring to hits does not refer to crits.
- **R-6.3** Abilities reference die results by icon; blank results are referenced by the word "blank".

### 6.2 Skills

- **R-6.4** Each character card and each crew asset carries one or more skills.
- **R-6.5** Losing a crew asset loses its skills. Skills on the character card can never be lost.
- **R-6.6** "Having a skill" means having it on the character card or on any crew.
- **R-6.7** The number of **instances** of a skill across character and crew determines the test level.
- **R-6.8** A skill granted by an ability is temporary and lasts only for that ability's effect. A player can gain a skill they already have, adding an instance.

### 6.3 Skill test

- **R-6.9** To test a skill, the player **rolls two dice**.
  - **Unskilled** (0 instances): passes with at least one `{CRIT}`.
  - **Skilled** (1 instance): passes with at least one `{HIT}` or `{CRIT}`.
  - **Highly skilled** (2+ instances): passes with at least one `{HIT}`, `{CRIT}` or `{FOCUS}`.
- **R-6.10** Any player can test any skill, including one they do not have.
- **R-6.11** The effects of passing or failing are on the card that called for the test. A test may have only a pass effect (failing does nothing) or only a fail effect (passing gives no bonus).

---

## 7. Combat

### 7.1 When it happens, and which type

- **R-7.1** Combat occurs when a player encounters a patrol, or through card effects.
- **R-7.2** There are two types: **ground** and **ship**. The type determines which combat value is used and whether the character or the ship can suffer damage.
- **R-7.3** Against a contact, patrol or card enemy with a `{SHIP_COMBAT}` value: ship combat.
- **R-7.4** Against a contact or card enemy with a `{GROUND_COMBAT}` value: ground combat.
- **R-7.5** Player versus player: ground combat if both are on a planet, ship combat otherwise, unless instructed otherwise.
- **R-7.6** A patrol encounter is always ship combat.
- **R-7.7** Players cannot attack each other unless a card specifically allows it.
- **R-7.8** The **attacker** is the current player. The **defender** is the patrol, contact, encounter card, job card or other player. If a combat occurs in which the current player is not a participant, the attacker is whoever is resolving that combat.
- **R-7.9** For card enemies, the card gives the enemy's name and its `{GROUND_COMBAT}` or `{SHIP_COMBAT}` value. The enemy is the defender, and **the player to the left of the current player rolls its dice**.

### 7.2 Combat steps

- **R-7.10 Step 1 — Roll attacker dice.** The attacker gathers dice equal to their combat value (ground or ship, per the combat type) and rolls.
  - The attacker, then the defender, may use abilities that **modify the attacker's combat value**.
  - Then the attacker, then the defender, may use **reroll** abilities. The same die may be rerolled multiple times.
- **R-7.11 Step 2 — Calculate attacker damage.** Each `{HIT}` counts as 1 damage; each `{CRIT}` counts as 2. `{FOCUS}` and blank deal nothing by themselves.
  - Both attacker and defender may use abilities that manipulate or add results. **Whoever rolled the dice uses their abilities first.**
- **R-7.12 Step 3 — Roll defender dice and calculate.** Same rules as steps 1 and 2.
  - The defender, then the attacker, may modify the defender's combat value; then, in that same order, reroll.
  - If the defender is a patrol, contact or card enemy, the player to the left of the attacker rolls.
- **R-7.13 Step 4 — Determine the winner.** Whoever rolled more damage wins.
  - **On a tie, the attacker wins.**
  - The effects of winning or losing depend on what started the combat: rewards, reputation, or nothing at all.
  - **The effect of winning resolves before step 5**, so a player can win a combat and still be defeated.
- **R-7.14 Step 5 — Suffer damage.** Attacker and defender each suffer the damage rolled by their opponent.
  - Ground combat: the character suffers it.
  - Ship combat: the ship suffers it.
  - Patrols, contacts and card enemies **do not suffer damage**: they simply win or lose.

### 7.3 Combat abilities

- **R-7.15** "Cancelling" a result: it is ignored, deals no damage and does not count toward determining the winner.
- **R-7.16** "Turning" a result into another: the original result is ignored, and abilities that would have modified the original can no longer be used on that die.
- **R-7.17** "Add a `{HIT}`": treat the roll as having an extra die showing `{HIT}`. That result **cannot be rerolled**.
- **R-7.18** "At the start of combat": the ability must be resolved before any player rolls dice for that combat.
- **R-7.19** "Gain +X" followed by an icon (`{GROUND_COMBAT}`, `{HEALTH}`, `{SHIP_COMBAT}`, `{HULL}`, `{HYPERDRIVE}`): adds to the matching value. These are passive while the player holds the card, and they **stack**.
- **R-7.20** The player to the left, while rolling for a contact, patrol or card enemy, **cannot use any of their own combat abilities**.

---

## 8. Damage and defeat

- **R-8.1** Damage is tracked with tokens on the character card or the ship sheet.
- **R-8.2** When a character has damage equal to its `{HEALTH}`, the player becomes **defeated**.
- **R-8.3** When a ship has damage equal to its `{HULL}`, the player becomes **defeated**.
- **R-8.4** A character cannot hold more damage than its health, nor a ship more than its hull. Excess damage is ignored.
- **R-8.5** Recovering damage removes tokens. Recovering during the planning step removes **all** tokens from both the character card and the ship sheet.
- **R-8.6** "Preventing" damage reduces the damage that would be suffered by the stated amount.
- **R-8.7** A character's "health remaining" is its health minus the damage on it.

### 8.1 Defeated

- **R-8.8** On becoming defeated, the player:
  1. loses 3,000 credits (or all of them, if they have less);
  2. discards **all** of their secrets;
  3. lays their standee on its side;
  4. finishes resolving the current action or step (if not in the action step), and then their turn ends.
- **R-8.9** During the planning step, a defeated player **must** choose to recover all damage. They then stand their standee back up.
- **R-8.10** If a defeated player recovers damage such that they no longer have damage equal to their health or hull, they are no longer defeated and stand their standee up, even outside the planning step.
- **R-8.11** If a player is defeated while resolving a job, the job **fails automatically**. They keep the job card and may attempt it again in a future encounter step.
- **R-8.12** A defeated player **cannot be attacked**.
- **R-8.13** A player holding a bounty for a defeated player's crew may still fight that crew; the defeated player **cannot** choose to protect it.
- **R-8.14** A defeated player **cannot be chosen** when an effect instructs a player to "choose a(nother) player".
- **R-8.15** If a player wins a combat and becomes defeated in that same combat, they still gain the rewards for winning before resolving the defeat.

**Divergence note:** the Learn to Play book says a defeated player finishes the current step and skips actions and steps not yet started. The Living Rules (R-8.8.4) is more precise. Implement per R-8.8.

---

## 9. Credits

- **R-9.1** Credits are the game's currency, represented by tokens worth 1,000, 5,000 and 10,000.
- **R-9.2** Gaining credits: take tokens from the supply with a combined value equal to the amount gained. Losing or spending: return tokens to the supply.
- **R-9.3** Players may exchange tokens with the supply at any time to make change.
- **R-9.4** A "discount" reduces a card's cost by the stated amount.
- **R-9.5** Buying something "free" means gaining the card without spending credits.
- **R-9.6** Any player may trade credits with the **current player** at any time during the game. Trades need not be equal: a player may give credits and receive less or nothing.
- **R-9.7** Any agreement that cannot be fulfilled immediately and completely is **non-binding**. Future promises can be broken.

> Implementation: R-9.6 and R-9.7 assume verbal negotiation between people. In a digital game this becomes an explicit credit transfer through the UI, with no enforceable contract. **[?]** Decide whether there is an offer/accept channel or whether transfers are one-sided.

---

## 10. Reputation and factions

- **R-10.1** There are four factions: Rebel `{FACTION_REBEL}`, Imperial `{FACTION_IMPERIAL}`, Hutt `{FACTION_HUTT}` and Syndicate `{FACTION_SYNDICATE}`.
- **R-10.2** Syndicate reputation represents standing with all the galaxy's crime syndicates (Black Sun, Pyke, Crimson Dawn and others) as a single bloc.
- **R-10.3** Each faction has a patrol that moves around the map.
- **R-10.4** Each player has `{REP_POS}`, `{REP_NEU}` or `{REP_NEG}` reputation with each faction, tracked on their player board. The neutral space sits at the centre of the track and is marked with the faction's icon rather than `{REP_NEU}`.
- **R-10.5** Gaining reputation moves the marker one space toward `{REP_POS}`. At `{REP_POS}` already, further gains have no effect.
- **R-10.6** Losing reputation moves the marker one space toward `{REP_NEG}`. At `{REP_NEG}` already, further losses have no effect.
- **R-10.7** "Setting" a reputation moves the marker straight to that value, wherever it was before.
- **R-10.8** Reputation affects which section of an encounter card a player resolves, whether they can move through a patrol's space, and whether they are forced to encounter it.
- **R-10.9** While a player is "treated as having" a reputation different from their actual one, they **ignore any reputation gained or lost** with that faction.

---

## 11. Patrols

- **R-11.1** Patrols are **not** considered ships for ability purposes.
- **R-11.2** A patrol's faction appears on both sides of its token.
- **R-11.3** There are four patrol levels (1 to 4), shown as one to four white dots on the token back.
- **R-11.4** Rewards printed on the token face: level 1 gives credits; levels 2 and 3 give fame; level 4 has no reward, because it cannot be defeated.
- **R-11.5** Each token carries a `{SHIP_COMBAT}` value, the number of dice it rolls.
- **R-11.6** Level-4 patrols have an infinite combat value: they cannot be eliminated, they **always win**, and they force the player to suffer enough ship damage to become defeated. **No dice are rolled** for that combat.
- **R-11.7** On **losing** a combat against a patrol, the player moves that patrol to an adjacent space of their choice.
- **R-11.8** On **winning** a combat against a patrol, the player gains the reward on the token (credits or fame), **loses 1 reputation** with that patrol's faction, eliminates the patrol (removing it from the game) and spawns a new patrol of that faction.
- **R-11.9** There is always exactly one patrol of each faction on the map.
- **R-11.10** To spawn a patrol, take the top token from that faction's stack (on an endcap) and place it faceup in the space adjacent to that stack.
- **R-11.11** Any number of patrols may occupy the same space.

### 11.1 Patrol movement

- **R-11.12** Patrols move via **patrol movement icons** on market deck cards.
- **R-11.13** The icon is resolved only when a player **buys or gains** a card from a market deck and reveals the new top card. **Discarding** a market card does **not** move a patrol.
- **R-11.14** When a player buys or gains a card after revealing or searching several cards of a deck, a patrol moves if there is a patrol icon on the top card **after shuffling**.
- **R-11.15** Each icon carries a faction and a distance. The faction says which token moves; the distance says how many spaces it moves **toward the current player**.
- **R-11.16** Where several paths are possible, the **current player** decides which the patrol takes, even if another is shorter.
- **R-11.17** Exception to R-11.16: if the patrol **can reach the current player's space**, it must move there.
- **R-11.18** To move toward the player, the patrol must move clockwise if the player is clockwise from it, and counterclockwise if the player is counterclockwise from it.
- **R-11.19** If the direction is unclear, the current player picks one. The patrol continues that way until an intersection, where it may reverse if that brings it closer to the player's space.
- **R-11.20** If the patrol enters the space of the player it is moving toward, it **stops**. No combat happens at that moment. If the player has `{REP_NEG}` reputation with it, they will have to encounter it during their encounter step.
- **R-11.21** Patrols move through other patrols, players and the Maelstrom without stopping.
- **R-11.22** [UB] Patrols **cannot** cross the Core Worlds or move from one Core Worlds space to the other, unless a card allows it.
- **R-11.23** Moving a patrol "directly" to a space takes it from its current space and places it there, with no intervening spaces.
- **R-11.24** Patrols can occupy navpoints, planets and the Maelstrom.

### 11.2 Encountering a patrol

- **R-11.25** Encountering a patrol means fighting a ship combat against it.
- **R-11.26** If the current player has `{REP_NEG}` reputation with the faction of a patrol in their space, they **must** encounter it.
- **R-11.27** With `{REP_POS}` or `{REP_NEU}` reputation, they **may** encounter it.
- **R-11.28** With several patrols in the same space, the player **must** encounter one of their choice among those whose factions they have `{REP_NEG}` reputation with. If all are neutral or positive, they **may** encounter one of their choice.
- **R-11.29** The effects of winning or losing resolve before determining whether the player is defeated: a player can win and gain the reward even while being defeated in that combat.

**Divergence note:** the Learn to Play book says a player losing to a patrol moves it "one space in any direction". The Living Rules says "to an adjacent space of their choice" (R-11.7), which is the same effect worded more precisely.

---

## 12. Contact tokens

- **R-12.1** Contact tokens begin the game facedown on the map.
- **R-12.2** Each token has a number on its face corresponding to a databank card.
- **R-12.3** Each token has a **class**, shown as colored pips, matching the class of its bounty card and of several contact spaces on the map. Classes: white, green, yellow and orange [UB].
- **R-12.4** During setup, contacts go on contact spaces of matching class. Orange contacts [UB] go on planet spaces themselves, excluding the Core Worlds.
- **R-12.5** Class does **not** indicate a contact's strength. It serves setup and helps bounty hunters narrow their search. Orange contacts tend to be more expensive to recruit as crew.
- **R-12.6** If an effect places or moves a contact token onto a planet, it may go in any contact space on that planet, with **no** need to match class. The token stays on its current side (faceup or facedown) unless the card says otherwise.
- **R-12.7** For that purpose, planet spaces, including the Core Worlds spaces, count as contact spaces.
- **R-12.8** While a token is faceup on the map, **any player** may look at its databank card at any time.
- **R-12.9** "Secretly looking at" a token: the player views the face of a facedown token without showing anyone, then returns it facedown to the same space. They **cannot** consult the databank card at that moment.
- **R-12.10** Contact tokens in a planet's contact spaces count as being on that planet. If a player is on that planet, those contacts are in that player's space.
- **R-12.11** A captured token placed on a bounty card is **not** treated as being in that player's space.
- **R-12.12** "Exchanging" two contact tokens: each is taken from its space and placed in the other's space, each keeping its faceup or facedown state.
- **R-12.13** Some contacts carry faction icons and/or the `{DROID}` icon. These have no inherent effect but may be referenced by abilities.
- **R-12.14** [UB] When a contact token is turned faceup and it **matches any player's character card**, it is discarded immediately, without resolving its databank card and without a bounty combat. Then:
  - if it was the chosen encounter, the player chooses another encounter;
  - if it was turned up by a reveal contact icon, the player turns another contact on their planet faceup, if able;
  - if it was turned up by an AI card instruction, repeat that instruction with another token.

### 12.1 Encountering a contact

- **R-12.15** Encountering a contact means reading and resolving the databank card whose number is printed on the token's face.
- **R-12.16** If the token is facedown, the player turns it faceup.
- **R-12.17** With more than one contact token on the planet, the player chooses which to encounter **before** flipping it, and must encounter the chosen one.
- **R-12.18** If the contact matches one of the player's bounties, they may fight it **instead** of resolving the databank card. The player may read the databank card before deciding.
- **R-12.19** After resolving the databank card, the token stays faceup on the planet unless the card instructs the player to discard it or remove it from the game.

---

## 13. Databank

- **R-13.1** The databank is a deck of numbered cards kept in numeric order and **never shuffled as a whole**.
- **R-13.2** Resolving a databank card means finding it and reading aloud the instructions in its top section.
- **R-13.3** Some cards share a number. When instructed to resolve such a number, the player resolves a **random** card of that number.
- **R-13.4** Pips on the back indicate how many cards share that number. **Those indicators are inaccurate for cards 42–53, 91 and 92 once the expansion is added.**
- **R-13.5** When organizing the deck, all cards of the same number are randomized and placed together.
- **R-13.6** A discarded databank card returns to its numeric position in the deck. Where several cards share the number, it is shuffled among them.
- **R-13.7** Not every number exists in the deck. For example, there is a card #40 but no #39.
- **R-13.8** [UB] **Every job** now has **two** resolution cards in the databank, even where some show only one pip. When a job card calls for a specific card, the player finds both, shuffles them and draws one at random.
- **R-13.9** [UB] There are now four starting cargo cards (#91) and four starting job cards (#92).

> Implementation: the databank is a dictionary of `number → list of cards`, with a uniform draw within the list. It is not a stack.

---

## 14. Assets and slots

### 14.1 Assets

- **R-14.1** Assets are: bounties, cargo, crew, gear, mods and jobs.
- **R-14.2** On gaining an asset, the player places it in an empty slot of the matching type, on their player board or ship sheet.
- **R-14.3** Cargo, gear and mod assets are printed upside down on encounter cards because they tuck into the top of player boards and ship sheets. (Physical detail; irrelevant to the implementation except for art cropping.)
- **R-14.4** If a player gains an asset with no empty slot for it, they may discard one of their cards to open a slot.
- **R-14.5** A player may choose **not** to gain an asset and discard it instead.
- **R-14.6** A player may only discard assets when an ability or rule allows it, or to open a slot for an asset they are gaining.

### 14.2 Rotating assets [UB]

- **R-14.7** Some cards carry an asset at each end.
- **R-14.8** A starting-asset icon in the upper left corner marks which of the two is the starting asset.
- **R-14.9** On buying or gaining such a card from the market, the player pays the cost of the **starting asset** and places the card in a slot with the starting asset visible and the other hidden.
- **R-14.10** "Rotate this card": the player turns it 180° so the other asset faces up. The card **stays in the same slot**.
- **R-14.11** If a rotating asset changes hands through a trade or other effect, it keeps its current orientation.
- **R-14.12** If a rotated asset is discarded, it returns to its deck in the same orientation as the other cards in that deck.

### 14.3 Slots

- **R-14.13** Each slot holds one asset whose type matches the slot's name.
- **R-14.14** Slots marked "x2" hold two assets of the stated type.
- **R-14.15** Some abilities allow more than one asset in a slot.
- **R-14.16** Slots with two names, such as "Cargo or Mod", hold **one** asset matching either name.
- **R-14.17** A player may **rearrange** their assets among slots at any time, as long as each asset ends up in a valid slot.
- **R-14.18** If a player ever lacks slots for all their assets, they **must** discard assets of their choice until every asset is either discarded or in an appropriate slot.

### 14.4 Traits

- **R-14.19** An asset may carry one or more traits, printed in bold smallcaps at the top of its text box (for example **illegal**, **weapon**, **armor**).
- **R-14.20** A trait has no inherent effect but may be referenced by other effects.

---

## 15. Card types

### 15.1 Cargo

- **R-15.1** Cargo is an asset that can be delivered to a specific planet for a reward.
- **R-15.2** It is usually bought from the `{DECK_CARGO}` deck; some characters start with cargo.
- **R-15.3** It goes in an empty "Cargo" slot on the ship sheet. A player may discard a card from a cargo slot to open space, and, during a market action, may barter with the discarded card.
- **R-15.4** On the planet named as the cargo's "Destination", the player may deliver it with a deliver action, gaining the stated reward and discarding the cargo.
- **R-15.5** Each market cargo card has a cost, which is both its purchase price and its barter value.
- **R-15.6** A player **cannot buy** cargo while on that cargo's destination planet.
- **R-15.7** Cargo with the **illegal** trait usually requires a die roll to deliver. See R-16.3.

### 15.2 Crew

- **R-15.8** Crew is an asset that grants skills and abilities.
- **R-15.9** It is usually gained by encountering contact tokens.
- **R-15.10** It goes in an empty "Crew" slot on the ship sheet. A player may discard to open space, or choose to discard the gained crew rather than place it.
- **R-15.11** On discarding crew, the card returns to its deck. If the crew has a contact token, that token is placed **faceup on the planet with an empty contact token space nearest to that player**, ignoring class.
- **R-15.12** Where several planets tie as nearest, the player chooses.
- **R-15.13** A planet with no contact token on the planet space itself counts as an empty contact token space, including the Core Worlds.
- **R-15.14** While the player holds the card they gain all its skills; discarding it loses them.
- **R-15.15** A player's crew assets count as being in that player's space, and therefore also in the space of any other player there.
- **R-15.16** Crew **cannot** be used to barter.

### 15.3 Gear

- **R-15.17** Gear is an asset that grants abilities to the **character**.
- **R-15.18** It is usually bought from the `{DECK_GEAR}` deck.
- **R-15.19** It goes in an empty "Gear" slot on the **player board**. A player may discard to open space and, during a market action, barter with the discarded card.
- **R-15.20** Each market gear card has a cost, serving as both price and barter value.

### 15.4 Mods

- **R-15.21** A mod is an asset that grants abilities to the **ship**.
- **R-15.22** It is usually bought from the `{DECK_GEAR}` deck (the same deck as gear).
- **R-15.23** It goes in an empty "Mod" slot on the ship sheet. Same discard and barter rules.
- **R-15.24** Each market mod card has a cost, serving as both price and barter value.

### 15.5 Jobs

- **R-15.25** A job is an asset resolved during the encounter step at a particular location.
- **R-15.26** It is usually bought from the `{DECK_JOB}` deck; some characters start with one. A job costing "0" can be taken for free.
- **R-15.27** Jobs **cannot** be used to barter.
- **R-15.28** A job goes in an empty "Job" slot on the player board or ship sheet; a player may discard to open space.
- **R-15.29** On the planet named as the job's "Destination", the player may attempt the job by resolving its ability.
- **R-15.30** Some jobs require choosing another player in the same space. Without such a player, the job cannot be attempted.
- **R-15.31** Many jobs instruct the player to resolve a databank card, which typically calls for a series of tests.
- **R-15.32** Above each job's name is a list of the skills that will be tested. A skill in **italics** means passing that test is mandatory to avoid failing the job.
- **R-15.33** A skill marked "(opposed)" means **both players** test that skill. The best outcome for the current player is to pass their test while the opponent fails theirs.
- **R-15.34** Players may read the sections of the databank card they are resolving, though the rulebook discourages reading ahead.
- **R-15.35** Where several players are involved in resolving a job's databank card, the **current player** makes all decisions unless stated otherwise.
- **R-15.36** Step-by-step resolution: read from step #1, stopping whenever a test or combat is required. After resolving a step, continue to the **next consecutive step** unless directed otherwise.
- **R-15.37** "Complete this job": the player gains the reward stated under "Reward". The reward specifies whether the job is discarded or removed from the game.
- **R-15.38** "The job fails": the player stops resolving, discards the databank card and **keeps** the job card. Their turn ends.
- **R-15.39** If the current player is defeated during a job, the job fails.
- **R-15.40** [UB] **Multi-player jobs:** some jobs require two players. The player must have another player in their space, at the stated destination, to attempt it. If **any** player involved is defeated during the job, the job fails.
- **R-15.41** [UB] **Opposed jobs:** some jobs let a player try to take credits or assets from another player, using the "(opposed)" mechanic in R-15.33.

### 15.6 Bounties

- **R-15.42** A bounty is a contract to capture or eliminate a specific contact or character. Each card's name and portrait match a contact token.
- **R-15.43** Bounties appear on market, encounter and databank cards.
- **R-15.44** A bounty goes in an empty "Bounty" slot; a player may discard a card from a bounty slot to open space.
- **R-15.45** On discarding a bounty, the card returns to its deck. If a contact token sat on the card, it is placed faceup on the planet with an empty contact token space nearest to that player, ignoring class.
- **R-15.46** A bounty costing "0" can be taken for free but **cannot** be used to barter.
- **R-15.47** Bounties **cannot** be used to barter in general (see R-16.33).
- **R-15.48** Each bounty has a class, in colored pips, matching the class of its contact token.

#### Resolving a bounty against a contact

- **R-15.49** To gain a reward, the player must find the named contact and **win a combat** against it.
- **R-15.50** On encountering the matching contact token, the player may fight **instead** of resolving that contact's databank card.
- **R-15.51** The bounty card gives the contact's combat value (how many dice it rolls) and a `{GROUND_COMBAT}` or `{SHIP_COMBAT}` icon setting the combat type.
- **R-15.52** On winning, the player chooses to **eliminate** or **capture**:
  - **Eliminate:** gain the "Elimination Reward" on the bounty, then remove the card and the contact token from the game.
  - **Capture:** place the contact token on the bounty card. The player may gain the "Capture Reward" with a deliver action on the planet named on the card. After gaining that reward, remove the bounty card and the token from the game.
- **R-15.53** On losing, the player keeps the bounty card and the contact token stays on its planet.
- **R-15.54** If the player wins and becomes defeated in the same combat, they eliminate or capture the contact **before** becoming defeated.
- **R-15.55** After gaining a reward from a bounty, the card is **removed from the game**, not merely discarded.

#### Bounty against the player's own crew

- **R-15.56** If the current player holds a bounty for a contact matching one of **their own** crew assets, they may fight that crew as an "Encounter" ability.
- **R-15.57** On winning: discard the crew card and eliminate or capture its contact token. To capture, take the token from the pool of discarded tokens beside the map and place it on the bounty card.
- **R-15.58** On losing: discard that crew from the ship sheet and place its contact token faceup on the planet with an empty contact token space nearest to that player.
- **R-15.59** During that combat, the player **cannot** use that crew's skills or abilities.

#### Bounty against another player's crew

- **R-15.60** If the current player holds the bounty and is in the same space as another player who has the matching crew, they may fight that crew as an "Encounter" ability.
- **R-15.61** The crew's owner may choose to **protect** it. If they do, the current player fights **the owner** instead: ground combat if they are on a planet, ship combat otherwise.
- **R-15.62** If the defender loses, they discard the crew card and the current player eliminates or captures its contact token.
- **R-15.63** If the owner does **not** protect the crew, the player to the left of the current player rolls dice for the crew, as in a normal bounty combat.
- **R-15.64** If the current player loses, the owner keeps the crew and there is no further effect.

#### Bounty against another player's character [UB]

- **R-15.65** Because contact tokens matching characters in play are discarded (R-12.14), a bounty for another character cannot be completed by locating its token.
- **R-15.66** Instead, the player holding the bounty may, **as their encounter for the turn**, fight that character if they are in the same space. Ground combat on a planet, ship combat otherwise.
- **R-15.67** On winning, they complete the bounty and gain the rewards in R-15.69. On losing, they keep the card and may try again on a future turn.
- **R-15.68** There is no option to **capture** another player's character, only to complete the bounty.
- **R-15.69 Player bounty rewards.** The player **ignores all rewards and other effects printed on the card** and instead:
  1. gains 5,000 credits;
  2. gains 1 fame;
  3. if the defeated player has `{REP_NEG}` reputation with one or more factions, the player completing the bounty chooses one of those factions and **gains** 1 reputation with it;
  4. if the defeated player has `{REP_POS}` reputation with one or more factions, the player completing the bounty chooses one of those factions and **loses** 1 reputation with it.
- **R-15.70** Once completed, the card is removed from the game.
- **R-15.71** A player **cannot** complete a bounty for their own character, but **may** acquire that card to stop others from completing it.

### 15.7 Encounter cards

- **R-15.72** Encounter cards correspond to spaces on the map.
- **R-15.73** There are several decks: most correspond to a pair of planets; one corresponds to all navpoints; one corresponds to Kessel and the Maelstrom; and [UB] the Core Worlds deck corresponds to both Core Worlds spaces and is also used by players crossing the galaxy.
- **R-15.74** The back names the corresponding spaces, or reads "Navpoint", and also lists the kinds of effect prominent in that deck.
- **R-15.75** The front is divided into sections: one per planet (or the Maelstrom); navpoint cards divide by whether a patrol is present; Core Worlds cards divide into "Core Worlds" and "Across the Core".
- **R-15.76** A section may contain subsections depending on the player's reputation with a faction.
- **R-15.77** A player resolves **only one** section (or subsection) per card: the one matching their game state (space, reputation and so on) **at the moment they draw the card**.
- **R-15.78** Some encounter cards contain assets, and the section specifies how they are gained. Those assets have **no cost** and **cannot** be used to barter.

#### Encountering a space

- **R-15.79** Encountering a space means drawing a card from the matching deck and resolving the section for that space.
- **R-15.80** [UB] Before resolving a Core Worlds encounter card, the player **must announce** whether they intend to cross to the other side of the galaxy. If they announce it, they resolve "Across the Core"; if they do not intend to or do not announce it, they resolve "Core Worlds".
- **R-15.81** The player reads the section aloud, **except** when it is a secret.
- **R-15.82** For a secret, the player takes the card and places it facedown in their play area. Secrets are printed upside down precisely so they are not read aloud by accident.
- **R-15.83** If the section is a bounty, the player may gain it.
- **R-15.84** After resolving, the player discards the card to the bottom of its encounter deck **unless** it provided a secret or an asset, in which case they keep it.

### 15.8 Secrets

- **R-15.85** Secrets are hidden abilities gained from encounter cards.
- **R-15.86** Each secret describes when and how it is resolved. Resolving a secret means reading it aloud and revealing it.
- **R-15.87** A player may **talk** about their own secrets, including lying about them, but cannot show the card, read the ability aloud or give the card to another player.
- **R-15.88** A player may look at their own secrets at any time.
- **R-15.89** On becoming defeated, a player discards **all** their secrets to the bottom of their respective encounter decks, without revealing them.
- **R-15.90** Secrets **do not occupy slots**.
- **R-15.91** Secrets **cannot** be traded.

---

## 16. Actions

### 16.1 Deliver action

- **R-16.1** During the action step, the current player may deliver cargo and bounties on the planet they occupy.
- **R-16.2** **Cargo:** on the destination planet, the player gains the stated reward and discards the cargo.
- **R-16.3** **Illegal cargo:** before delivering, the player rolls one die. On `{HIT}`, they deliver and gain the reward. On **any other result, including `{CRIT}`**, they resolve databank card **#1** instead of delivering.
- **R-16.4** **Captured bounty:** on the planet named on the card, the player gains the "Capture Reward" and **removes from the game** both the bounty card and the contact token.
- **R-16.5** Several deliveries may be made in a single deliver action, one at a time, in the player's chosen order.
- **R-16.6** If the effect of one delivery ends the player's turn, the remaining deliveries cannot be made that turn.
- **R-16.7** Cargo gained or a contact captured during the **encounter step** cannot be delivered that turn, even on the destination planet: only during the action step of a later turn.

### 16.2 Trade action

- **R-16.8** During the action step, while in a space with another player, the current player may trade any combination of credits and cards with them.
- **R-16.9** With several players in the space, the current player may trade with any of them, but **the others may only trade with the current player**, not with each other.
- **R-16.10** Trades need not be equal.
- **R-16.11** Any agreement that cannot be fulfilled immediately and completely is non-binding.
- **R-16.12** Tradeable: credits, jobs, bounties, cargo, crew, mods and gear.
- **R-16.13** If a traded bounty has a contact token on it, the token goes with it.
- **R-16.14** **Not** tradeable: secrets and ship sheets.
- **R-16.15** Credits and debt tokens may be exchanged with the current player at any point during their turn. This is **not an action** and does not require being in the same space.
- **R-16.16** Such an exchange of credits or debt tokens **may interrupt** an ability, an encounter or a combat.

### 16.3 Market action

- **R-16.17** While on a planet (including a Core Worlds space), during the action step, the current player may perform a market action to buy an asset.
- **R-16.18 Step 1 — Discard a card (optional).** The player may discard the top card of **any** market deck: place it facedown on the bottom of that deck and reveal the new top card.
- **R-16.19** After seeing the new top card, the player may discard a **second card from the same deck**. Never more than two.
- **R-16.20** Patrol movement icons and reveal contact icons are **not** resolved when a card is discarded from a market deck.
- **R-16.21 Step 2 — Buy a card (optional).** The player may buy the top card of **one** market deck of their choice. After buying, they reveal the new top card of that deck and resolve its icons.

#### Buying

- **R-16.22** Buying means spending credits equal to the card's cost.
- **R-16.23** On buying an asset, the player places it in a slot of the matching type, on the player board or ship sheet.
- **R-16.24** With no empty slot of that type, the player may discard one of their cards to open one.
- **R-16.25** After buying, the player **must** reveal the top card of that deck:
  - with a patrol movement icon, they **must** move that patrol (R-11.12 onward);
  - with a reveal contact icon, R-16.26 applies.
- **R-16.26** [UB] **Reveal contact icon.** If the number of players in the game falls within the range shown on the icon, the player **must** choose a facedown contact on their planet and turn it faceup, if able.
  - The player does **not** encounter that contact, though they may do so normally during their encounter step.
  - If the revealed contact matches any player's character, remove it from the game and flip another.
  - If the player is not on a planet, the icon has no effect.

#### Buying ships

- **R-16.27** On buying a ship, the player **removes the market card from the game** (as the card instructs) and replaces their ship sheet with the one for the purchased ship.
- **R-16.28** All damage on the ship is removed.
- **R-16.29** The old ship returns to the ship sheet supply. If it had a matching market card, that card does **not** return to the deck.
- **R-16.30** If the new ship lacks slots for all the player's assets, they **must** discard assets of their choice until each is either discarded or in an appropriate slot.

#### Bartering

- **R-16.31** When buying from a market deck, a player may discard one or more assets to reduce the card's cost by the total cost of the discarded assets.
- **R-16.32** Assets with no cost, such as those gained from encounter cards, **cannot** be used to barter.
- **R-16.33** Bounties, crew and jobs **cannot** be discarded to barter.
- **R-16.34** A player may barter whenever buying cards from a market deck, whether through a market action or another game effect.
- **R-16.35** A player **cannot** barter to pay for other effects, including gaining an asset on an encounter card.
- **R-16.36** **No change is given**: discarding assets worth more than the card bought returns no credits. The cost is only reduced, to a minimum of 0.
- **R-16.37** Some abilities specifically allow bartering when spending credits on an effect. Apart from not being a market purchase, all other barter rules apply.
- **R-16.38** When buying a new ship, the player **must** barter their current ship and then return it to the supply.
- **R-16.39** A player **cannot** barter their ship when buying assets, only when buying another ship.
- **R-16.40** A player may and **must** barter their current ship when resolving the "Used Ship for Sale" card from the `{DECK_SHIP}` deck.

### 16.4 Market decks

- **R-16.41** There are six decks: bounty `{DECK_BOUNTY}`, cargo `{DECK_CARGO}`, gear and mod `{DECK_GEAR}`, job `{DECK_JOB}`, luxury `{DECK_LUXURY}` and ship `{DECK_SHIP}`.
- **R-16.42** The top card of each deck is always revealed, placed faceup on the deck.
- **R-16.43** When the revealed card is bought, gained, discarded or shuffled back in, the new top card is revealed.
- **R-16.44** If an effect requires shuffling the deck after buying or gaining a card, the new top card is revealed only **after** the shuffle.
- **R-16.45** If an effect instructs a player to draw, reveal or search a number of cards in a deck, the faceup card **counts as one of them**.
- **R-16.46** Decks are always referred to by icon rather than name, because they hold several card types.
- **R-16.47** When an ability refers to a type of asset (cargo, for instance), it means any card of that type, **regardless of which deck it came from**.
- **R-16.48** **Luxury deck** `{DECK_LUXURY}`: expensive assets, and **every card in it provides fame in some way**. It holds cargo, crew, gear, jobs and mods. Costs run from 10,000 to 20,000 credits.

#### Deck composition (base game)

| Deck | Predominant content | Cards |
|---|---|---|
| `{DECK_BOUNTY}` | bounties, plus some jobs that let a player attack others | 11 |
| `{DECK_CARGO}` | cargo, deliverable for credits and sometimes fame | 10 |
| `{DECK_GEAR}` | gear and mods improving combat, health, hull or hyperdrive | 15 |
| `{DECK_JOB}` | jobs, plus some crew useful in any skill test | 14 |
| `{DECK_LUXURY}` | expensive cards that always provide fame | 11 |
| `{DECK_SHIP}` | ships | 9 |

---

## 17. Ships

- **R-17.1** Each player starts with a starter ship. The starter sheet has two sides: **G9 Rigger** and **G-1A Starfighter**.
- **R-17.2** `{HYPERDRIVE}` sets how many spaces the player can move during the planning step.
- **R-17.3** `{SHIP_COMBAT}` sets how many dice the player rolls in ship combat.
- **R-17.4** `{HULL}` sets how much damage the ship can take before the player is defeated.
- **R-17.5** Each ship has slots; each slot holds one asset of the type named on it.
- **R-17.6** Each ship has a cost, used both as price and as barter value.
- **R-17.7** Ships are bought from the `{DECK_SHIP}` deck. See R-16.27 to R-16.30 and R-16.38 to R-16.40.
- **R-17.8** A player may look through the ship sheets in the supply at any time.
- **R-17.9** Every ship buyable from the market has a **ship goal** printed on its sheet.
- **R-17.10** Patrols are **not** considered ships for ability purposes.

---

## 18. Goals, fame and winning

### 18.1 Goals

- **R-18.1** Each character card carries a **personal goal**; most ship sheets carry a **ship goal**.
- **R-18.2** Progress made toward a ship's goal before acquiring that ship does not count, unless the player is still fulfilling the condition.
- **R-18.3** Each goal describes how it is completed and what to do on completion. Completing it awards fame and instructs the player to flip the card or sheet.
- **R-18.4** Once flipped, the goal is replaced by a new ability, which describes how and when it is resolved.
- **R-18.5** Goal tokens may be placed on the character card or ship sheet to track progress. On flipping the card, discard its goal tokens.
- **R-18.6** Completing a personal goal awards 1 fame, as stated on the card.
- **R-18.7** If a card does not use the specific wording "complete this job", it does **not** count toward Doctor Aphra's personal goal.

### 18.2 Fame

- **R-18.8** The first player whose fame is equal to or greater than the fame requirement **wins**. The game ends the moment a player reaches it.
- **R-18.9** Standard requirement: **10**. Players may agree on another value before the game.
- **R-18.10** **Extended game [OPT]:** 12 fame.
- **R-18.11** **First game:** the Learn to Play book recommends 8 fame.
- **R-18.12** **With ambitions [OPT]:** 8 is recommended, since ambitions lengthen the game.
- **R-18.13** The main ways to gain fame: delivering illegal cargo, buying luxury cards, defeating level-2 and level-3 patrols, completing bounties, completing jobs and completing goals.
- **R-18.14** Every player starts with zero fame. A player at zero who would lose fame stays at zero.
- **R-18.15** If an asset grants additional fame, the player gains it. If they lose the asset or stop meeting the condition that granted it, they **lose that fame**.
- **R-18.16 Victory tiebreak:** if two players reach the required fame simultaneously, the tied player with more credits wins. Still tied, the tied player seated farthest clockwise from whoever started the game wins.
- **R-18.17 Most famous player:** the player with the most fame. On a tie, **all** tied players count as the most famous player. (At the start of the game, therefore, everyone is.)
- **R-18.18** An ability that specifically affects **the current player** if they are the most famous is not subject to the tiebreak rules: if the current player is the most famous, or tied for it, they **are** the target.

---

## 19. Ambitions [UB] [OPT]

- **R-19.1** With ambitions, each player has their own win conditions on an ambition sheet.
- **R-19.2** The first player to meet the fame requirement **and** complete **all** of their ambition goals wins.
- **R-19.3** Each player chooses a sheet during setup (R-3.20).
- **R-19.4** Each sheet has five parts: setup instructions, goal overview, additional abilities, goals and a progress tracker.
- **R-19.5** With ambitions, players do **not** receive credits by turn order during setup; they receive what their sheet grants.
- **R-19.6** If a sheet grants a market card, it is searched for in the deck shown by the icon beside the card title, and that deck is shuffled afterward.
- **R-19.7 Ambition goal types:**
  - `{AMBITION_DASH}` **Dash (–):** may be completed at any time.
  - `{AMBITION_NUM}` **Number:** must be completed in numeric order. Goal "1" must be complete before goal "2".
  - `{AMBITION_INF}` **Infinity (∞):** ongoing goals, complete only while their criteria hold. They may be completed at any time but stop being complete if the criteria stop being met.
  - `{AMBITION_FAME}` **Fame:** the final goal, completable only once the player has fame equal to or greater than the requirement **and** has completed every other goal on the sheet.
- **R-19.8** On completing a goal, the player places a goal token in that goal's space on the progress tracker.
- **R-19.9** If a goal requires repeating a task (for example, "deliver 3 **illegal** cargo"), the player places a token each time and the goal is complete when the required number is reached.
- **R-19.10** If a player stops meeting the criteria of an `{AMBITION_INF}` goal, they remove that goal's token.
- **R-19.11** Some sheets grant additional "Action" and "Encounter" abilities usable during the player's turn.
- **R-19.12** If such an ability lists a "Destination", the player must be at that destination to use it.
- **R-19.13** If an "Encounter" ability instructs the player to resolve a databank card, it is resolved the same way as a job's databank card. If the player is defeated during it, the encounter fails and they may try again on a future turn.

---

## 20. Favors [UB] [OPT]

- **R-20.1** Favors let players request game effects from one another.
- **R-20.2** There are four favors:
  - **Advice:** choose a skill to gain for your next skill test during the current turn. The skill must be on the granting player's character or crew.
  - **Combat Strategy:** roll one additional die in a combat involving your character or ship.
  - **Endorsement:** choose a faction the granting player has `{REP_POS}` reputation with. You are treated as having positive reputation with it until the end of the turn.
  - **Shortcut:** gain +1 `{HYPERDRIVE}` until the end of the turn.
- **R-20.3** A favor may be requested at any time **except after dice have been rolled** for a combat or a skill test.
- **R-20.4** Any player may request a favor, including a player who is not the current player.
- **R-20.5** Each player may **request** any number of favors per turn but may **receive** only one per turn.
- **R-20.6** A player cannot request a favor from themself.
- **R-20.7** Players do not need to be in the same space to grant a favor.
- **R-20.8** Players may offer credits, assets and debt tokens in exchange for a favor. Assets may only be exchanged if both players are in the same space.
- **R-20.9** If several players offer to grant the favor, the requester chooses which offer to accept.
- **R-20.10** Players may accept credits and debt tokens as an agreement **not** to grant a favor to someone, but such agreements are non-binding.

### 20.1 Debt tokens

- **R-20.11** Debt tokens represent a promise of a future favor and exist only with the favors rule.
- **R-20.12** Each player starts with the debt token matching their player board.
- **R-20.13** A player may include any debt tokens they hold, their own and other players', in trades for assets, credits and other debt tokens.
- **R-20.14** A player holding another player's debt token may return it to that player to receive a favor **of the holder's choice**. The player receiving their own token back **must** grant the favor.
- **R-20.15** That favor counts as the receiving player's one favor for the turn.

---

## 21. Interpreting cards and abilities

### 21.1 Word usage

- **R-21.1** "**Cannot**" is absolute and cannot be overridden by other abilities.
- **R-21.2** "**May**" makes the effect optional.
- **R-21.3** "**Then**": resolve the effect before "then" first, and the one after it second.
- **R-21.4** "**To**": the player must **completely** resolve the effect before "to" in order to resolve the one after it. Example: "lose 1 Hutt reputation **to** gain 1 fame" grants no fame if the player already has negative Hutt reputation.
- **R-21.5** "**And**": the player resolves both effects as completely as possible. Example: "gain 5,000 credits **and** lose 1 Rebel reputation" still grants the credits even if Rebel reputation is already negative.
- **R-21.6** "**Once per turn**": if the ability can be used during another player's turn, it may be used once during that turn, even if it was already used during the owner's own turn.

### 21.2 Timing

- **R-21.7** "**After**": the effect resolves immediately after the described event.
- **R-21.8** "**When**": the effect resolves at the moment of the described event.
- **R-21.9** "When" effects take precedence over "after" effects for the same event.
- **R-21.10** If several abilities resolve at the same time, the **current player** determines the order.
- **R-21.11** Each ability may be resolved once per **instance** of its timing event. An "at the start of combat" ability resolves once at the start of **each** combat.
- **R-21.12** Abilities **cannot interrupt** other abilities. They are used before, after or on their own.

### 21.3 Breaking ties

- **R-21.13** The **current player** resolves all ties and timing conflicts not explicitly addressed by the rules.
- **R-21.14** Ties on the win condition: see R-18.16.

> Implementation: R-21.10, R-21.13 and R-5.30 require an explicit decision point from the current player. Provide a `ResolveTie` / `ChooseOrder` command in the core rather than resolving silently.

---

## 22. Discarding and removal from the game

- **R-22.1** Discarded market and encounter cards go to the **bottom** of their decks.
- **R-22.2** Some market cards are **removed from the game** rather than discarded. The card always says so, typically after buying a ship, completing a job or gaining a bounty reward.
- **R-22.3** Discarded databank cards return to the deck in numeric order, shuffled among other cards of the same number.
- **R-22.4** Discarded tokens return to the supply.
- **R-22.5** When a contact or patrol is **eliminated**, its token is **removed from the game**.
- **R-22.6** Ship sheets return to the ship sheet supply.
- **R-22.7** Removing from the game is **not** discarding: the component goes to the box and is not used again that game.
- **R-22.8** On discarding crew, R-15.11 also applies to its contact token.
- **R-22.9** Discarded AI cards go facedown to the bottom of the AI deck.

---

## 23. Single-player mode (AI) [SOLO]

### 23.1 Overview

- **R-23.1** In solo play, the player controls a character normally and competes against **one or two** characters driven by AI cards.
- **R-23.2** [UB] With two AI opponents, they must be of **different types**.
- **R-23.3** Two AI types:
  - **Smuggler (purple deck):** characters starting with databank card **#91** or **#92**.
  - **Bounty hunter (pink deck) [UB]:** characters starting with databank card **#90**.

### 23.2 Solo setup

- **R-23.4** Normal setup, with the changes below.
- **R-23.5** Playing the extended game (12 fame) against the AI generally makes the game **easier**.
- **R-23.6** The human is the first player and does **not roll dice** during the character selection step.
- **R-23.7** Starting credits: the human gains 4,000 (unless using ambitions); the first AI in turn order gains 6,000; the second AI gains 8,000.
- **R-23.8** Set up every component for each AI as if it were a player.
- **R-23.9** The smuggler AI chooses the **G9 Rigger**; the bounty hunter AI chooses the **G-1A Starfighter**.
- **R-23.10** Each AI performs character setup normally. Then place a goal token on the destination named on the smuggler AI's starting cargo or job.
- **R-23.11 Smuggler AI deck:** [UB] shuffle that character's unique AI card together with the smuggler AI cards numbered **1, 2, 6, 7 and 9** (number in the lower-right corner). Without the expansion, shuffle all the base game AI cards.
- **R-23.12 Bounty hunter AI deck [UB]:** shuffle the character's unique AI card with the bounty hunter AI cards (1 through 5). Return the other AI cards to the box.
- **R-23.13** Remove "Disable Comms Network", "Honest Robbery", "Sail Barge Sabotage" and "Snatch and Grab" from the `{DECK_JOB}` deck.
- **R-23.14** Remove card 11 from the Ring of Kafrene/Takodana encounter deck.

### 23.3 Game flow

- **R-23.15** The human resolves their turn normally. **At the end of each human turn**, each AI takes a turn, in turn order.
- **R-23.16** An AI turn is: draw the top card of its deck and resolve it **from top to bottom**.
- **R-23.17** AI players **never** resolve encounter cards.
- **R-23.18** "Do the first that applies": resolve the first bullet that is possible. If it would have no effect or cannot be resolved, move to the next. If none can be resolved, the AI does nothing.
- **R-23.19** "Do all that apply": resolve each bullet from top to bottom, skipping those that would have no effect or cannot be resolved.
- **R-23.20** After resolving, discard the card facedown to the bottom of the AI deck.
- **R-23.21** The human wins by reaching the fame requirement first, and loses if any AI reaches it first.
- **R-23.22** With two AIs, they compete with the human **and with each other**. Track each one's credits and assets separately.

### 23.4 General AI behavior

- **R-23.23** An AI's fame, credits and cards are tracked as for a real player.
- **R-23.24** An AI **does not use card abilities**, except those that increase values on its ship or character (for example "Gain +1 `{SHIP_COMBAT}`").
- **R-23.25** An AI **cannot complete** personal goals or ship goals.
- **R-23.26** An AI follows all normal player rules except where this section says otherwise.
- **R-23.27** If an AI is defeated during its turn, it does not resolve the remaining bullets on its card: it finishes the bullet it was resolving, and then its turn ends.
- **R-23.28** In combats involving an AI, the human rolls dice for the opponent, tallying damage for each roll separately and following all normal combat timing.
- **R-23.29** If one of the human's card abilities forces the AI into a decision (gambling, for instance), the AI **always chooses not to participate** when that is possible.
- **R-23.30** If a decision is mandatory, determine the outcome **randomly**.
- **R-23.31** If databank card #40 forces a skill test on the AI, roll for it and resolve normally; mandatory decisions follow R-23.30.
- **R-23.32** If an effect reveals a contact matching a character in play, discard that contact and repeat the effect that revealed it, if able.
- **R-23.33** When an AI is instructed to discard the top card of the `{DECK_CARGO}` deck and gain credits, it is **not buying** that card, so it does **not** resolve icons on the next card revealed.

### 23.5 Smuggler AI goals

- **R-23.34** The smuggler AI uses goal tokens to mark planets where it can deliver cargo or complete jobs. Those tokens drive where it moves.
- **R-23.35** The first token is placed during setup, on the destination of the starting cargo or job. Further tokens are placed by AI cards.
- **R-23.36** When the AI delivers cargo or completes a job on a planet, remove one goal token from that planet.
- **R-23.37** [UB] When delivering rotating cargo, the AI rotates the card and **moves** the goal token to the new destination.

### 23.6 AI bounty targets [UB]

- **R-23.38** A bounty target is a character, crew or contact matching one of the AI's bounties.
- **R-23.39** If an AI holds a bounty matching no character, crew or faceup contact on the map, the **nearest facedown contact token of matching class** (white, green, yellow or orange) is treated as matching that bounty.
- **R-23.40 Target priority:** 1) character; 2) crew; 3) faceup contact of the **lowest** class (fewest pips); 4) facedown contact of the lowest class.
- **R-23.41** Among targets of equal priority, the AI chooses the nearest. Still tied, it chooses **at random**.

### 23.7 AI movement

- **R-23.42** When instructed to move, the AI moves a number of spaces equal to its ship's `{HYPERDRIVE}`.
- **R-23.43** The smuggler AI always moves toward the nearest goal token. If its space already holds one, it does not move.
- **R-23.44** The bounty hunter AI moves toward a bounty target, per R-23.40.
- **R-23.45** Where several paths exist, the AI moves in the direction that leaves it **the fewest spaces remaining** from its target.
- **R-23.46** Like a normal player, the AI **stops** on entering a patrol's space when it lacks `{REP_POS}` reputation with that patrol.
- **R-23.47** Unlike a normal player, the AI may move through the **Maelstrom** as if it were a navpoint.
- **R-23.48** The AI **will not** take a longer path to avoid patrols. If the space nearest its target holds a patrol, it moves there anyway, even with `{REP_NEG}` reputation.
- **R-23.49** Between two paths equidistant from the target, the AI prefers, in order: 1) the path with no patrol to interrupt its movement; 2) ending movement on a planet. If both are planets, decide randomly.
- **R-23.50** [UB] The AI **cannot** move through the Core Worlds.
- **R-23.51 Important.** If the AI would end its movement on a **navpoint** after moving its full `{HYPERDRIVE}`, it moves **one or two spaces fewer** if that lets it end on a planet instead. Where both one fewer and two fewer would reach a planet, it moves **one fewer**.

**Divergence note:** the Learn to Play book words R-23.51 differently ("if either of the two spaces it moved through before the navpoint is a planet, it stops on the planet"). Implement per the Living Rules (R-23.51).

### 23.8 AI buying

- **R-23.52** The AI card always names the deck to buy from. The AI only buys while on a planet, unless the card says otherwise.
- **R-23.53 Step 1 — Discard.** Before buying, the AI discards the top card of the named deck if **any** of these apply:
  - the card cannot be bought on the AI's current planet;
  - the AI lacks the credits for the top card (it discards regardless of whether it could afford anything in that deck);
  - the AI has no empty slot matching the top card (regardless of whether the deck holds cards for other slots);
  - it is buying from `{DECK_SHIP}` and the top card does **not** cost more than the AI's current ship;
  - the top card is cargo whose destination is a **patrol**;
  - the top card is "Wanted", "Information Broker" or "Special Order".
- **R-23.54** If any of those also apply to the new top card, the AI discards a **second** card. Never more than two per buy instruction.
- **R-23.55 Step 2 — Buy.** The AI buys the top card, spends the credits and places the card in an open slot.
  - it will not buy without a matching empty slot, and **will not discard** cards from slots to make space;
  - it obeys printed restrictions such as "Cannot buy on Kessel" and "Limit 1 **Armor** per character";
  - it will buy assets it cannot use (for example the "Hired Help" crew card);
  - it **never** buys a ship that does not cost more than its current one; it always barters its own ship, but **never** barters with assets;
  - resolving "Used Ship for Sale", it follows all abilities on the card and buys the most expensive ship it can afford; ties are broken randomly;
  - it will not buy cargo whose destination is a patrol;
  - it will not buy "Wanted", "Information Broker" or "Special Order".
- **R-23.56 Step 3 — Reveal.** After buying, reveal the top card and resolve patrol movement and reveal contact icons on it.
- **R-23.57** When resolving patrol movement, the patrol moves toward the AI. **Unlike for a human player**, the AI always moves the patrol along the shortest path.
- **R-23.58** When resolving a reveal contact icon:
  - the bounty hunter AI [UB] reveals a contact whose class matches one of its bounties, if able;
  - otherwise it turns faceup the facedown contact of the **lowest** class (fewest pips on its back) on its planet;
  - with more than one eligible token of the same class, choose at random;
  - if the revealed contact matches a player's character, discard it and reveal another by the same criteria.

### 23.9 AI jobs and cargo

- **R-23.59** To complete a job, the AI resolves **only the reward section** of the card (gaining or losing the listed reputation, fame and credits). It does **not** draw a databank card and does **not** perform tests.
- **R-23.60** To deliver cargo, the AI resolves the reward section and discards the card.
- **R-23.61** If the AI holds several cargo cards with the same destination, it delivers them all at once.
- **R-23.62** When delivering **illegal** cargo, the AI does **not** roll the die. Some AI cards do make it suffer damage when delivering illegal cargo.
- **R-23.63** If the AI is on a planet that is the destination of **two** of its jobs and the instruction reads "if at a job destination, complete it", it completes **only one**, chosen at random.

### 23.10 AI crew

- **R-23.64** The AI follows normal crew rules, except that it does **not fully resolve** the contact's databank card: draw the card and, if it carries a crew asset, place that crew in an empty crew slot on the AI's ship.
- **R-23.65** The AI **spends no credits**, performs no tests and resolves none of the databank card's abilities.
- **R-23.66** With no empty crew slot, the AI discards one of its crew **at random** to make room, following normal discard rules.
- **R-23.67** If the databank card has no crew on it, discard the card back to the deck.
- **R-23.68** AI cards always instruct it to reveal **facedown** contact tokens. If none on the planet are facedown, the AI resolves the lowest-class faceup token instead.
- **R-23.69** With two facedown, or two faceup, tokens of the same class on the planet, the AI resolves the **leftmost** one.
- **R-23.70** The AI **does not protect** its crew from bounties.

### 23.11 AI bounty encounters [UB]

- **R-23.71** The target's type determines how the AI resolves the encounter, always producing either an **unopposed** or an **opposed** bounty:
  - **Contact token:** the AI may encounter faceup and facedown tokens. Encountering a facedown one, it flips it. If the contact matches one of its bounties, it resolves an **unopposed** bounty. Otherwise the encounter ends.
  - **Crew card:** if crew matching one of its bounties is in the AI's space, it may encounter that crew. If the crew belongs to another player, that player may protect it: protected, the AI resolves an **opposed** bounty; unprotected, an **unopposed** one.
  - **Character card:** if the AI shares a space with a player whose character matches one of its bounties, it may encounter that character by resolving an **opposed** bounty.

#### Unopposed bounty

- **R-23.72** 1) Remove the encountered contact or crew from the game. 2) The AI gains the "Elimination Reward" on the matching bounty card and resolves its "After you gain a reward from this card" text. 3) The AI suffers damage on its character or on its ship equal to the damage value shown on the bounty card.

> **[?]** The source indicates this damage by icon, lost in extraction. Confirm on the physical card whether a bounty carries its own damage value for this case.

#### Opposed bounty

- **R-23.73** The AI fights a combat against the player whose character or crew matches its bounty. Ground combat if both are on a planet, ship combat otherwise.
- **R-23.74** Follow the normal combat steps. The AI is the **attacker**, and all of its abilities are ignored except those adding to its combat value, health or hull.
- **R-23.75** If the defender wins, the AI **discards** the bounty card for that character or crew.
- **R-23.76** If the AI wins against **crew**: the crew card is removed from the game; the AI gains the "Elimination Reward" and resolves the "After you gain a reward from this card" text.
- **R-23.77** If the AI wins against a **character**: it gains 5,000 credits and 1 fame regardless of the printed reward; the bounty card is removed from the game; and then:
  - if the defeated player had `{REP_NEG}` reputation with one or more factions, the AI **gains** 1 reputation with one of them, chosen by the human, **preferring** a faction the AI does not already have positive reputation with;
  - if the defeated player had `{REP_POS}` reputation with one or more factions, the AI **loses** 1 reputation with one of them, chosen by the human.
- **R-23.78** [UB] The bounty hunter AI cannot buy the "Wanted" or "Information Broker" cards from the `{DECK_BOUNTY}` deck. Instructed to buy from that deck, it discards those cards if they are on top.

### 23.12 Solo variants

- **R-23.79 Expert mode [OPT]:** to raise the difficulty, start the AI with some fame. The more starting fame, the harder the game.
- **R-23.80 Ambitions in solo [OPT]:** during setup, instead of gaining starting credits, the human draws **two** random ambition sheets, chooses one and returns the rest to the box, then applies that sheet's setup effects.
- **R-23.81** AI players **do not use** ambition sheets. Instead they need **two more fame** than the requirement to win. [UB] In that case the AI starts with 6,000 credits.
- **R-23.82 Favors in solo:** the optional favors rule **cannot** be used in single-player games.

### 23.13 Multiple AI opponents [UB]

- **R-23.83** To play with two AIs, set up one bounty hunter character (databank #90) and one non-bounty-hunter character (#91 or #92), and determine turn order between them **randomly**. The human remains the first player.
- **R-23.84** The first AI in turn order starts with 6,000 credits; the other with 8,000.
- **R-23.85** Each AI uses the deck matching its type. After each human turn, draw and resolve one AI card from each deck, in turn order. Then play returns to the human.

---

## 24. Errata

Official corrections to printed components. Cards printed after the correction may already carry the right text. What follows is **what changed**; the final text of each card should be transcribed from the physical card.

- **R-24.1 Tatooine encounter #9.** In the secret under the Hutt reputation condition, an instruction to **discard the card** after resolving the effect was added at the end.
- **R-24.2 Lothal encounter #1.** In the secret, an instruction to **discard the card** was added at the end.
- **R-24.3 Mon Calamari encounter #2.** In the secret, an instruction to **discard the card** was added at the end.
- **R-24.4 Takodana encounter #9.** In the secret, an instruction to **discard the card** was added at the end.
- **R-24.5 Ryloth encounter #10.** The second sentence was corrected by inserting a negation: the condition becomes **not** replacing the ship sheet, and only then may the player lose 1 Imperial reputation to gain 4,000 credits. The printed version stated the opposite.

> Implementation: enter these five cards in their corrected form and flag the record with `errata: true` for auditing.

---

## 25. Component-specific clarifications

Points the Living Rules handles case by case. The implementation of the corresponding cards must respect them.

- **R-25.1** "Broken Arm", "Encrypted Message", "Priority Delivery" and "Structural Damage" [UB]: on gaining one of these, if there is no empty slot of the right type, the player **must** discard another card to open one. Declining is not an option.
- **R-25.2 Cad Bane [UB]:** when an ability grants "1 additional fame", Cad Bane's ability does **not** reduce it.
- **R-25.3 Cad Bane [UB]:** if an ability reads "you have X additional fame", that fame is **not** affected by his ability.
- **R-25.4 Cad Bane [UB]:** if an effect reduces the rewards of a bounty, job or cargo to 0, that does **not** affect the credits he gains from his ability.
- **R-25.5 Dengar [UB]:** with the "IQA-11 Blaster Rifle" gear card, Dengar's damage reduction applies **first**; if the resulting damage is still greater than 2, the rifle reduces it to 2.
- **R-25.6 Empty contact spaces [UB]:** planets with no orange contact token on them, **including the Core Worlds spaces**, are empty contact spaces for effects that refer to such spaces.
- **R-25.7 Hondo Ohnaka [UB]:** goal tokens placed on Hondo's assets stay on those cards even if he flips to the front side of his character card.
- **R-25.8 Lando Calrissian [UB]:** when resolving Lando's encounter card, the player may reroll their die using the Lando crew's ability. If they roll a blank during that encounter and Lando is their only crew, they **must** discard him.
- **R-25.9 "W1-LE":** if a player uses the "W1-LE" crew to perform a market action **as their encounter**, and that causes a patrol they have negative reputation with to move into their space, they do **not** have to fight it. They have already resolved their one encounter for the turn.
- **R-25.10 "Wanted":** if a player is defeated while resolving the "Encounter" ability on the "Wanted" job card but **wins** the combat, they **complete** the job. The card states the player claims the reward on winning the combat, which overrides the rule that a job fails when the player is defeated (R-15.39).
- **R-25.11 "Sy Snootles" and "Attend Statue Dedication":** Sy Snootles' ability reduces a job's credit reward to 0 in exchange for 1 additional fame. If the job has **no** credit reward, the ability cannot be used. The same applies to the "Mandalorian Armor" gear when completing bounties.
- **R-25.12 "Chromium Hull":** whenever a card uses the phrase "gain X additional fame", that fame is treated as **part of** the fame reward it modifies, not a new instance of gaining fame. So completing "Attend Statue Dedication" and spending 10,000 credits for additional fame yields only **1** additional fame from Chromium Hull, not 2.
- **R-25.13 Tatooine encounter #6:** when ship sheets are traded via that encounter, each sheet stays on its current side, and all damage and goal tokens stay with their sheet. **Assets tucked under the sheets stay with their original owners** and are not traded along with the ships.

---

## 26. Implementation summary

### 26.1 Core commands (draft)

Derived from the rules above. To be confirmed once the effect inventory is complete.

| Step | Commands |
|---|---|
| Planning | `Move(path)`, `GainCredits`, `RecoverDamage`, `UsePlanningAbility(id)` |
| Action | `Deliver(assetIds)`, `MarketDiscard(deck)`, `MarketBuy(deck, barterAssetIds)`, `Trade(playerId, offer)`, `UseActionAbility(id)` |
| Encounter | `EncounterSpace`, `EncounterPatrol(patrolId)`, `EncounterContact(tokenId)`, `FightBounty(bountyId, targetRef)`, `UseEncounterAbility(id)` |
| Cross-cutting | `TransferCredits(playerId, amount)`, `RequestFavor(type)`, `GrantFavor(playerId)`, `ChooseOption(promptId, optionId)`, `ResolveTie(promptId, choice)`, `RearrangeAssets(layout)` |

### 26.2 Points requiring a mid-effect player decision

The engine must **pause** and ask for a decision in these cases, among others:

- choosing which contact to encounter, **before** flipping it (R-12.17);
- deciding between resolving the databank card and fighting the bounty, **after** flipping the contact (R-12.18);
- announcing a Core Worlds crossing **before** drawing the encounter card (R-15.80);
- eliminating or capturing a defeated contact (R-15.52);
- protecting or not protecting one's own crew (R-15.61);
- choosing which assets to discard when slots run short (R-14.18, R-16.30);
- choosing the faction that gains or loses reputation on a player bounty (R-15.69);
- choosing a patrol's path (R-11.16) and its direction when ambiguous (R-11.19);
- resolving "nearest" ties (R-5.30) and the order of simultaneous abilities (R-21.10);
- using or declining abilities at each window in the combat steps (R-7.10 to R-7.12).

### 26.3 Invariants for tests

- character damage ≤ health; ship damage ≤ hull (R-8.4);
- fame ≥ 0 (R-18.14);
- reputation ∈ {positive, neutral, negative} per faction (R-10.4);
- exactly one patrol of each faction on the map at all times (R-11.9);
- every asset sits in a valid slot at the end of any operation (R-14.18);
- exactly one encounter resolved per turn (R-4.12);
- each action performed at most once per action step (R-4.9).

---

## 27. Gaps: what is **not** in these three files

Important for planning. The rulebooks give the rules system, not the content. Everything below is missing and must come from the cards and physical components:

1. **Text and effects of the encounter cards** (70 base + 91 expansion), including sections, reputation subsections and secrets.
2. **Text of the databank cards** (53 + 52), including #1 (illegal cargo seized), #40, #90/#91/#92 and every job resolution.
3. **Text of the market cards** (70 + 45): names, costs, traits, destinations, rewards, abilities, patrol movement icons and reveal contact icons.
4. **Character cards** (8 + 8): abilities on both sides, personal goal, skills, `{GROUND_COMBAT}`, `{HEALTH}`, starting databank card and starting reputation.
5. **Ship sheets** (12 + 6): `{HYPERDRIVE}`, `{SHIP_COMBAT}`, `{HULL}`, slots, cost, ship goal and reverse-side ability.
6. **Patrol tokens** (16): combat value and reward per level and faction.
7. **Contact tokens** (22 + 11): number, class, faction icons and `{DROID}`.
8. **Ambition sheets** (8): setup, abilities and goals.
9. **AI cards** (10 + 21): the content of each card, which is the AI's actual behavior.
10. **Map geometry**: the list of spaces, the paths between them and the positions of the contact spaces on each tile. The rules describe how the map works, never what the map is.
11. **Which patrols start on which navpoints** in the standard layout, by space name.

Until that data exists, the core can be built and tested with fictional content. Map geometry (item 10) is the first real blocker for V1.

---

## Changelog

| Date | Change |
|---|---|
| 2026-10-24 | Rewritten in English, per the project convention that all documentation is authored in English. No rule content changed. |
| 2026-10-23 | Initial version, consolidated from Living Rules 1.1, the *Unfinished Business* rulebook and the base game Learn to Play book. |
