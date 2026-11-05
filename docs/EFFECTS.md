# EFFECTS.md — Effect inventory (T2.1)

Status: **draft, awaiting owner review** (2026-11-04).

Inventory of the objects, ability shapes, effect primitives, conditions, targets and timings found on the **242 base game cards** (`data/transcription/`, format 0.9), plus the extension points the *Unfinished Business* rules (`[UB]` in `RULES.md`) require. Expansion cards are not transcribed yet: their structural mechanics come from the rules, and card-specific effects are added when they are transcribed (`PLAN.md` T2.8).

This document sets the card data format for T2.2 (schema) and the work list for T2.3 (effect engine). It classifies; it does not change rules. Rules stay in `RULES.md`.

**Card references:** `Name-num`, where num is the image number in its deck (as in `data/transcription/`). Encounter cards use the section's space and the image number (`Ord Mantell-5` is the Ord Mantell section of `base-encounter-cantonica-ord-mantell-05`). Databank cards use the printed number and variant (`Imperial Presence-1a`).

---

## 1. Objects

### 1.1 Game state

| Object | Holds | Rules |
|---|---|---|
| Player | character (side), ship sheet (side), player board, credits, fame, reputation ×4, character damage, ship damage, assets in slots, secrets, goal tokens, defeated flag, "once per" usage counters | R-8, R-9, R-10, R-14, R-18 |
| Space | type (planet, navpoint, Maelstrom, Core Worlds `[UB]`), faction (planets), adjacency, contact spaces with class, occupants | R-5 |
| Contact space | class, holds 0–1 contact token | R-12.4 |
| Contact token | id, class, faction icon, droid icon, databank number, faceup/facedown, location (contact space, planet space `[UB]`, bounty card, crew card, removed) | R-12 |
| Patrol | faction, combat token (level, value, reward), space | R-11 |
| Planet marker | damage token on a planet (only Jyn Erso-2 so far) | R-25.14 |
| Market deck ×6 | ordered stack, top card faceup | R-16.41 |
| Encounter deck ×7 (+ Core Worlds `[UB]`) | one deck per pair of spaces; discards go to the bottom | R-15.73, R-22.1 |
| Databank | `number → list of cards`, random draw within a number, never a stack | R-13 |
| AI deck | ordered stack per AI type `[SOLO]` | R-23 |
| Supplies | ship sheets, credit tokens, damage tokens, goal tokens, debt tokens `[UB]` | R-22.4, R-22.6 |

### 1.2 Card kinds and their static fields

| Kind | Static fields (beyond name, image, pack, traits) | Abilities live in |
|---|---|---|
| Cargo | cost, destination, reward, buy restriction ("Cannot buy on X"), delivery check (ILLEGAL: roll, `{HIT}` delivers, else #1) | text, abilities |
| Crew | cost, skills, faction icon, droid icon | text, abilities |
| Gear | cost, ARMOR limit | text, abilities |
| Mod | cost, slot rule (Smuggling Compartment-8) | text, abilities |
| Job | cost, skills (with `certain`), destination, encounter, reward, end clause (discard or remove) | encounter, reward |
| Bounty | cost, class, combat (value, type), elimination reward, capture reward (destination), end clause | text |
| Ship (market) | cost, link to ship sheet | text ("gain this ship's sheet") |
| Special (market) | Used Ship for Sale-4 | custom (§8) |
| Encounter card | number, sections (space or patrol state), variants (reputation condition), secret flag, embedded asset, errata | section text |
| Databank: cargo check | sections keyed by die result | §5.4 |
| Databank: contact | one section, embedded crew or job | section text |
| Databank: job resolution | numbered steps | §5.4 |
| Databank: favor resolution | sections keyed by faction of the calling job | §5.4 |
| Databank: sabacc | numbered steps between two players, reward supplied by the caller | §5.4 |
| Databank: starting asset | embedded bounty, cargo or job, starting planet | — |
| Character | 2 sides; ground combat, health, skills; upper ability; lower ability or personal goal; setup | abilities, goal |
| Ship sheet | 2 sides (ship goal / flipped, or two starter sides); cost, slots, hyperdrive, ship combat, hull; goal or named ability | abilities, goal |
| AI card | steps with "first that applies" or "all that apply" items | AI script (§5.5) |

**Slots** come from the player board and the ship sheet. The player board (all four colours alike) has 2 `GEAR` slots and 2 dual `JOB OR BOUNTY` slots, a fame track 0–12 and four reputation tracks. Ship sheets add cargo, mod and crew slots, dual slots ("cargo or mod", "crew or bounty") and, on IG-2000, a bounty slot. Slot capacity can be changed by abilities (Weapon Specialist on Takodana-0, Smuggling Compartment-8).

**Traits** found: ILLEGAL, WEAPON, ARMOR, FAVOR, SHIP, STARTER SHIP. The droid icon (`{DROID}`) is an icon on crew, contacts and characters, not a trait, but effects test it the same way ("non-droid", "a droid crew").

---

## 2. Ability shape

Every card text maps to one or more **abilities**:

```
Ability {
  timing     — when it can or must happen (§3)
  limit      — once per turn, once per combat, unlimited (R-21.6, R-21.11)
  optional   — "may" (R-21.2)
  condition  — predicate checked when it would be used (§6)
  cost       — what must be fully paid first: credits, discard this card, lose reputation ("to", R-21.4)
  effects    — sequence of effect primitives (§4), joined by then / and / to (§5)
  duration   — instant, until end of turn, for this test, for this combat
  source     — card id + section, errata id if any
}
```

The text of the card is never parsed at run time. It is kept for display and localization; the ability tree is written by hand in the card data (T2.2).

---

## 3. Timings

### 3.1 Passive (static) abilities

Active while the card is held and its condition is met. They are read by queries, not triggered.

| Id | Pattern | Examples |
|---|---|---|
| `stat.modify` | "Gain +N {GROUND_COMBAT} / {SHIP_COMBAT} / {HYPERDRIVE} / {HULL} / {HEALTH}" | DL-44 Heavy Blaster Pistol-2, Quad Laser Cannon-8, Han Solo-4, Chewbacca crew (contact #7), Moldy Crow (HWK-290 back) |
| `stat.modify` + condition | "If you have Stealth or Strength, gain +1" | Vibroknife-1 |
| `stat.modify` + scale | "+1 {HYPERDRIVE} for each job you have" | Ark Angel (Heavy-Duty Lifter back) |
| `fame.extra` | "While …, you have N additional fame" (computed, lost when the condition ends, R-18.15) | Extravagant Wardrobe-0, Dejarik Holotable-10 |
| `reward.modify` | "Increase the reward for each of your jobs / cargo by X" | Black Sun Agent crew (#5), Hondo Ohnaka crew (#14) |
| `slot.modify` | slot holds more cards, or a card occupies a dual slot | Weapon Specialist (Takodana-0), Smuggling Compartment-8 |
| `limit.trait` | "Limit 1 ARMOR per character" | Armored Vest-3, Plastoid Armor-9, Mandalorian Armor-9 |
| `buy.restrict` | "Cannot buy on X" | every market cargo and job with a planet destination |
| `rule.modify` | changes a core rule for the holder | Codebreaker (Takodana-1: no stop at a `{REP_NEU}` patrol), Diplomatic Mission (GX1 back: not forced to encounter `{REP_NEG}` patrols), Long Range Ion Cannon (Aggressor back: no reputation loss on beating a patrol), Chromium Hull-1 (others may fight you as their encounter) |

### 3.2 Step abilities (activated)

Used by the holder during their own step, once per step instance (R-4). Cost often includes "discard this card".

| Id | Keyword | Examples |
|---|---|---|
| `step.planning` | "Planning:" | Doctor Aphra-5 back (extra market action), Hyperdrive Modification (Mon Calamari-1, "during your planning step") |
| `step.action` | "Action:" | Bossk-3, Boba Fett-6, Shield Upgrade-13, Bacta Supplies-7, Garindan crew (#10), most secrets |
| `step.encounter` | "Encounter:" (replaces the turn's encounter, R-4.12) | Ketsu Onyo-0, Bossk-3 back, W1-LE crew (#22), Firespray goal, secrets Lothal-2, Tatooine-8, Tatooine-0, Tatooine-4 |

### 3.3 Triggered abilities ("when", "after", "before", "at the start of")

"When" resolves at the event and takes precedence over "after" (R-21.7 to R-21.9). Triggers found in the base game:

| Trigger id | Event | Examples |
|---|---|---|
| `on.dice_rolled` | after you roll dice, any reason | Lando Calrissian-1 |
| `on.dice_rolled.combat` | after you roll dice during (ground / ship) combat | Greedo crew (#12), Thermal Detonator (Ord Mantell-5), Seismic Charge (Ring of Kafrene-9) |
| `on.dice_rolled.opponent` | after your opponent rolls dice during ship combat | Cloaking Device (Ring of Kafrene-7) |
| `on.dice_rolled.test` | after you roll dice for a skill test | Maz Kanata crew (#19) |
| `on.dice_rolled.delivery` | while rolling to deliver ILLEGAL cargo | Smuggling Compartment-8, Millennium Falcon (YT-1300 back), BoShek crew (#6) |
| `window.dice_modify` | during combat and/or skill test: turn, reroll, cancel results (§4.3) | Jyn Erso-2, Vibroknife-1, Vibro-Ax-5, Autoblaster-14, Ion Cannon-6, Jetpack-10, Targeting Computer-7, IG-88-7 |
| `on.combat_start` | at the start of (ground) combat | Black Krrsantan crew (#4), Doctor Evazan crew (#9) |
| `on.combat_won` | when / after you win a combat (filter: type, opponent kind, patrol faction, fame reward) | IQA-11 Blaster Rifle-0, Ketsu Onyo-0 back, Proton Torpedoes-5, Long Range Ion Cannon, secrets Lothal-1 and Ryloth-7 |
| `on.combat_lost` | when you lose a ship combat | Chromium Hull-1 |
| `on.would_be_defeated` | replacement: "when you would become defeated … instead" | IG-88-7 back, Medpack (Ord Mantell-4), Escape Pod (Ring of Kafrene-3), Nashtah Pup (YV-666 back) |
| `on.defeated` | when you become defeated | Gift for Jabba-2 |
| `on.test_before` | before you perform a skill test | Doctor Aphra-5, Hired Help-6 and -9 |
| `on.test_would_fail` | replacement: "when you would fail … pass instead" | Savvy Pilot (Cantonica-0); contacts #5, #12, #22 ("if you would fail, you may spend X to pass") |
| `on.move_into` | when you move into a patrol's space / the Maelstrom | Han Solo-4 back, Slave I (Firespray back), Codebreaker, secret Mon Calamari-5 |
| `on.other_moves_into` | when another player moves into your space | Tractor Beam (Lancer back) |
| `on.patrol_would_move` | when a patrol within 3 spaces would move during your market action | Jyn Erso-2 back |
| `on.fame_gained` | when you gain fame | Chromium Hull-1 (R-25.12) |
| `on.job_completed` | when you complete a job | Sy Snootles-4 (R-25.11) |
| `on.bounty_reward` | when you gain a bounty's reward / after you eliminate a contact | Mandalorian Armor-9, Boba Fett-6 back, every bounty ("after you gain a reward from this card, remove it") |
| `on.planning_credits` | when you choose to gain credits during the planning step | Lobot crew (#18) |
| `on.discarded` | when you discard this card | Refugees (Lothal-9) |
| `on.action_step_end` | at the end of your action step | Unrefined Coaxium (Kessel-9) |
| `on.damage_recovered` | when you recover damage from this ship | YV-666 ship goal |
| `on.asset_gained` | gain a crew while you have 2 crew | GX1 Short Hauler ship goal |

### 3.4 One-shot text

Resolved once, right away: encounter sections, databank sections, contact cards, job "Encounter" lines and rewards, cargo rewards, bounty rewards. Same effect primitives, no trigger.

### 3.5 Goals

| Shape | Examples |
|---|---|
| Counter of events ("win 3 combats", "complete 2 jobs", "deliver 2 ILLEGAL cargo", "gain rewards from 2 bounties", "win 2 combats against patrols") | Ketsu Onyo-0, Doctor Aphra-5, Lando Calrissian-1, Boba Fett-6, Aggressor goal |
| State check ("have a ship costing ≥ 15,000", "have 2 droid crew", "mod + 2 gear with combined cost ≥ 15,000", "damage token on 2 Imperial planets") | Han Solo-4, IG-88-7, Bossk-3, Jyn Erso-2 |
| Event + condition ("if you have at least 1 cargo, complete a job"; "with 2 mods or 1 mod and Chewbacca, deliver 1 cargo"; "deliver a contact on a bounty") | Heavy-Duty Lifter, YT-1300, Lancer goals |
| Activated goal ("Ship Goal – Action: spend 7,000"; "Ship Goal – Encounter: fight …") | HWK-290, Firespray goals |
| Triggered goal ("when you recover damage, spend 7,000"; "gain a crew while you have 2") | YV-666, GX1 goals |

Completing a goal: gain 1 fame and flip the sheet or card (`goal.complete`). Ring of Kafrene-4 completes a goal by paying (§8).

---

## 4. Effect primitives

### 4.1 Resources

| Id | Printed pattern | Parameters | Notes |
|---|---|---|---|
| `credits.gain` | "gain {CREDITS}X" | amount expression (§7) | |
| `credits.spend` | "spend X" | amount | a cost: must be paid in full (R-9.8) |
| `credits.lose` | "lose X" | amount | an effect: as much as possible (R-9.8) |
| `credits.take` | "take X from a player" | player, amount (up to) | Thief (Cantonica-8), Ziton Moj #23 |
| `credits.stake` | place credits on a card, winner takes them | — | Sabacc #40a |
| `fame.gain` / `fame.lose` | "gain / lose N fame" | amount; "additional" flag (R-25.12) | |
| `rep.gain` / `rep.lose` | "gain / lose N {faction} reputation" | faction selector (§6.3), amount, player | R-10.5, R-10.6 |
| `rep.choice` | "gain or lose 1 Hutt reputation" for a chosen player | — | Bib Fortuna #3 |

### 4.2 Damage

| Id | Pattern | Parameters |
|---|---|---|
| `damage.suffer` | "your character / ship suffers N damage" | target: character, ship, both, chosen character or ship |
| `damage.recover` | "recover N / all damage" | target, amount or all |
| `damage.prevent` | "prevent N / all damage from this combat" | amount, scope: this combat |
| `damage.cap` | "suffer a maximum of 2 damage" | IQA-11 Blaster Rifle-0 |
| `damage.crit_value` | opponent's `{CRIT}` counts as 1 damage | K-2SO crew (#16) |

### 4.3 Tests, dice and combat

| Id | Pattern | Parameters |
|---|---|---|
| `test.skill` | "test Stealth", "test Piloting or Knowledge" | skill, or a set the player picks from; result feeds branches (§5) |
| `test.pass` | "pass this test instead" | replacement, with cost |
| `test.skill_grant` | "gain a skill of your choice for that test" | duration: this test |
| `combat.fight` | "fight a ground / ship combat against X (value)" | type (ground, ship, or by space), opponent (§6.4), combat value |
| `dice.roll` | "roll N dice" outside tests and combat | N, outcome table keyed by result or count of `{HIT}` |
| `dice.reroll` | reroll 1 / any number / blanks | count, filter, limit |
| `dice.turn` | turn N results from X to Y | from, to, count, condition |
| `dice.add` | add N `{HIT}` / `{CRIT}` | count, face, cost |
| `dice.cancel` | cancel opponent's N `{HIT}` / `{CRIT}` / all `{HIT}` | count, face |
| `dice.opponent_fewer` | opponent rolls 1 fewer die | count, condition |
| `combat.auto_win` | win regardless of results (2+ `{FOCUS}`) | Ponda Baba crew (#20) |

### 4.4 Movement and turns

| Id | Pattern | Parameters |
|---|---|---|
| `move.player` | "move up to N spaces", "move 1 space" | distance, exact or up to; out-of-planning movement cannot pass the Maelstrom (R-5.12) |
| `move.player_direct` | "move directly to …" | destination selector (§6.2); R-5.23 |
| `move.patrol` | "move the X patrol N spaces toward you / in any direction" | patrol selector, distance, direction |
| `move.patrol_direct` | "move the X patrol directly to …" | patrol selector, destination |
| `turn.additional` | "take an additional turn" | R-5.14 (none during an additional turn) |
| `turn.end` / `step.end` | "end your turn", "end your action step" | |
| `market.extra_action` | perform a market action outside the action step or at a navpoint | Doctor Aphra-5 back, W1-LE crew (#22) |

### 4.5 Encounters and contacts

| Id | Pattern | Parameters |
|---|---|---|
| `encounter.patrol` | "encounter it", "must encounter that patrol" | who encounters, which patrol |
| `encounter.contact` | "you may encounter a contact on your planet" | Takodana-8 |
| `encounter.space` | "resolve another Takodana encounter card" | deck (Takodana-5) |
| `encounter_deck.peek` | look at the top card, put it on top or bottom | GA-97 (Takodana-4) |
| `contact.look` | "secretly look at" a contact | where (your planet, any planet, chosen planet, N planets), count (R-12.9) |
| `contact.move` | move a contact to an empty contact space on your planet, else exchange | which (any, facedown), source |
| `contact.exchange` | exchange two facedown contacts | Takodana-3 |
| `contact.relocate_self` | move this contact's token to a named planet, else exchange | Bib Fortuna #3, Maz Kanata #19 |
| `contact.discard_token` | discard this contact's token | most contact cards |
| `contact.place_facedown` | place the token facedown on its space or an empty one | Dengar #8 |
| `contact.reveal` | turn a facedown contact faceup | AI cards; `[UB]` reveal icon (R-16.26) |
| `planet.marker` | place a damage token on your planet | Jyn Erso-2 |

### 4.6 Cards and assets

| Id | Pattern | Parameters |
|---|---|---|
| `asset.gain_embedded` | "gain the crew / gear / mod / cargo / job below (above)" | cost; no barter (R-15.78) |
| `asset.gain_databank` | "gain card #90" | number; fallback |
| `market.buy` | buy from a deck | deck(s), source (top, reveal top N, search), filter (type, trait), discount, free, barter allowed; patrol icon after shuffle (R-11.14) |
| `market.take` | take a named card from the deck or another player | "Syndicate Favor" (Kessel-2), "Rebel Favor" (Mon Calamari-6) |
| `market.discard_top` | discard the top card of decks | AI card #10 |
| `asset.take` | take an asset from another player | type filter (crew, droid crew, cargo or gear, bounty matching a contact) |
| `asset.discard` | discard one of your assets | type filter, as cost or as effect |
| `card.discard_self` / `card.remove_self` | "discard this card", "remove this card from the game" | R-22 |
| `cargo.deliver` | deliver cargo, regardless of destination, without rolling | Ryloth-8, Lothal-6 |
| `bounty.deliver` | deliver a contact on a bounty for its capture reward anywhere | Tatooine-2 |
| `job.complete` / `job.fail` | "complete this job", "the job fails (keep the job card)" | R-15.37, R-15.38 |
| `goal.complete` | complete personal or ship goal | Ring of Kafrene-4 |
| `sheet.flip` | flip character card or ship sheet | goals |
| `ship.buy_sheet` | buy any ship sheet from the supply | Ryloth-3, Used Ship for Sale-4 |
| `ship.replace_starter` | replace your ship sheet with a STARTER SHIP sheet | Ryloth-9 (errata R-24.5) |
| `ship.trade_sheets` | trade ship sheets with another player | Tatooine-0 secret |
| `databank.resolve` | "resolve card #N" | number, context passed to the card (§5.4) |
| `secret.keep` | a section marked secret is kept facedown | R-15.82 |

---

## 5. Control flow

### 5.1 Connectors (R-21)

| Id | Word | Meaning |
|---|---|---|
| `seq.then` | then | A fully, then B (R-21.3) |
| `seq.and` | and | both, each as far as possible (R-21.5) |
| `seq.to` | to | A is a cost: B only if A is fully done (R-21.4) |
| `opt.may` | may | player chooses to resolve (R-21.2) |
| `choice.either` | either A or B / A or B | player picks one |
| `opt.unless` | "X unless you spend Y" | player may pay Y to avoid X (Ord Mantell-9, Nal Hutta-8, Jewel of Yavin #45) |
| `if.state` | if (condition) | §6 |
| `if.did` | "if you do", "if you do not gain the crew", "if you did not perform the test" | branch on the previous choice or outcome |
| `if.result` | "if you pass / fail / win / lose", "pass or fail", "win or lose", "if you win and you are not defeated" | branch on test or combat outcome |
| `if.before` | "if you already had {REP_POS}" | condition read **before** the effect it follows |

### 5.2 Encounter card resolution

The section is chosen by the space (or by patrol presence on navpoint cards), and the variant by the player's reputation **when the card is drawn** (R-15.77). A variant may be a secret. This is data, not code: `sections[].variants[].condition` already holds it.

### 5.3 Contact card resolution

Contact databank cards are plain one-shot text with branches; the embedded crew or job is gained with `asset.gain_embedded`, and the token is then discarded or moved.

### 5.4 Databank procedures

A databank card is a **procedure called with context**. The caller is a cargo, a job, an encounter section or a secret. Four shapes:

| Shape | Sections keyed by | Context from the caller | Cards |
|---|---|---|---|
| Cargo check | die result rolled to deliver (`{CRIT}`, `{FOCUS}`, blank) | the cargo being delivered | #1 a–d |
| Job steps | step number; `skip to step n`, `repeat this step`, `return to step n`, `the job fails`, `complete this job` | the job (its reward) | #42–#53 |
| Favor | faction of the calling FAVOR job | the job | #41 a–b |
| Sabacc | steps between "you" and an opponent | opponent, stake, reward supplied by the caller ("Reward:" line or the job reward) | #40 a–c, called by Cantonica-9, Tatooine-0 secret, Low Stakes Game of Sabacc (Nal Hutta-1), Legendary Game of Sabacc-7 |

Being defeated during a job procedure makes the job fail (R-15.39). The step machine is shared by every job procedure; `[UB]` multi-player and opposed jobs extend it (§10).

### 5.5 AI cards `[SOLO]`

AI cards are a separate script language: steps with "do the first that applies" or "do all that apply", whose items reuse the primitives above plus AI-only ones (`ai.move_toward_goal`, `ai.goal_token`, buy with a fallback deck). Implemented with the solo mode, after V1.

---

## 6. Conditions and selectors

### 6.1 Conditions on the player

- Reputation with a faction is in a set: `{REP_POS}`, `{REP_NEU}`, `{REP_NEG}`, or a union of two.
- Count of reputations: "at least 1 `{REP_NEG}`", "2 or 3 `{REP_POS}`", "4 `{REP_POS}`", "no `{REP_NEG}`", "`{REP_POS}` with any faction".
- Reputation with the faction of a patrol in your space (navpoint cards, cargo check intro).
- Character identity ("if you are Han Solo", Chewbacca #7, Lobot #18, W1-LE #22, Saw Gerrera #21).
- Holding a named asset ("Maz Kanata" crew on Takodana-4; "Chewbacca" crew on the YT-1300 goal).
- Holding assets by type, trait, icon, count or total cost (bounty, job, cargo, mod, gear, crew, droid crew, ILLEGAL; "no crew", "no gear or mods", "2 or more crew").
- Having a skill ("if you have Strength / Tactics").
- Credits ≥ X ("at least 6,000").
- Fame comparison: "the most famous player", "not the most famous", "a player with more fame than you".
- Location: on a planet, on a named planet, on an Imperial (Rebel) planet, at a navpoint, not at the Maelstrom, in a patrol's space.
- Ship: STARTER SHIP or not, ship cost, damage state ("did not have any damage").
- Defeated or not ("if you are not defeated").
- Game: number of players (`[UB]` reveal icon), a databank number exists (Nal Hutta-9).

### 6.2 Space selectors

Your space, any space, an adjacent space, the space of the X patrol, the nearest planet, the space of a player, a planet within 3 spaces of the Hutt patrol, a planet whose faction you have `{REP_POS}` with, an empty contact space on a planet, a named planet. Job destinations reuse these: a named planet, "the Imperial patrol's space" (Rebel Prison Break), "a space with another player that has at least 1 `{REP_NEG}`" (Wanted-9, -10), "another player's space", "any planet where you have `{REP_POS}` with its faction".

### 6.3 Faction selectors

Named faction; faction of your planet; faction of a patrol in your space; faction of the nearest patrol; faction of a chosen token; a faction you have `{REP_POS}` or `{REP_NEU}` with; a faction of another player's choice; each faction you have `{REP_NEG}` with.

### 6.4 Player and opponent selectors

You; another player (chosen); a player (you included); a player in your space; a player in your space with a condition (reputation, more fame); each other player; the player who participates. Combat opponents: a named non-player enemy with a fixed value ("TIE fighters (`{SHIP_COMBAT}` 3)"), a patrol, a contact (bounty), another player, the crew of another player (bounty on crew, R-15.61).

---

## 7. Amount expressions

Constant; "up to X" (player picks ≤ X); "any number / any amount"; "for each X" (bounty, `{REP_NEG}` faction, `{REP_POS}` reputation, crew, job, `{HIT}` rolled, 1,000 spent); multipliers of a chosen amount ("twice the amount you spent", "triple", "equal to twice its cost", "named number × 2,000"); "the same amount (or all of their credits)"; reward reduced to 0 (Sy Snootles-4, Mandalorian Armor-9, R-25.11).

---

## 8. Custom exceptions

Cards whose effect does not fit the primitives without a one-off rule. Each gets a named handler in the effect engine (`custom:<id>`), still called from card data.

| Handler | Card | Why |
|---|---|---|
| `used_ship_for_sale` | Used Ship for Sale-4 | special market card: buy a sheet costing more than yours, must barter your ship, ship suffers 3 damage, search and remove the ship's card, shuffle itself back (R-16.40) |
| `sabacc` | #40 a–c | two-player stake and step procedure; the reward comes from the caller (§5.4) |
| `name_a_number` | Cantonica-4 | name a number, roll 5 dice, gain number × 2,000 if enough `{HIT}` |
| `side_bets` | Cantonica-2 | every other player may pay 1,000 to choose a die result; payout table |
| `double_or_nothing` | Cantonica-5 | roll, then choose to stop or roll again |
| `shared_die_result` | Doctor Evazan crew (#9) | both combatants may turn blanks to a result chosen at the start |
| `chromium_hull` | Chromium Hull-1 | other players may choose you as their encounter |
| `patrol_redirect` | Jyn Erso-2 back | changes the target of a patrol move made by the market icon |
| `planet_damage_marker` | Jyn Erso-2 front | new token on planets, counted only by her goal (R-25.14) |
| `pay_for_goal` | Ring of Kafrene-4 | completes a goal without its condition |
| `replace_with_starter` | Ryloth-9 | errata R-24.5 applied at mapping |
| `buy_any_sheet` | Ryloth-3 | buy from the supply, then remove the ship card from the deck |
| `tractor_beam` | Shadow Caster (Lancer back) | stops another player's movement during their turn |

Everything else in the base game maps to the primitives in §4 (coverage in Appendix A).

---

## 9. Questions settled with the owner

- **Spend versus lose** (2026-11-04): "spend X" needs the full amount, also when offered as "unless you spend / lose X"; "lose X" imposed as an effect removes what the player has, down to 0. Recorded as `RULES.md` R-9.8.
- **Damage tokens on planets** (2026-11-04): markers for Jyn Erso's personal goal only, kept until the end of the game, no other effect. Recorded as `RULES.md` R-25.14.

---

## 10. `[UB]` extension points

Structural mechanics of *Unfinished Business*, from `RULES.md`. The base implementation leaves a named point for each, so the expansion adds data and handlers instead of rewriting the core.

| Point | What it needs | Rules |
|---|---|---|
| Pack id on every record | every card, sheet, token and map piece carries its pack; the databank list merges packs | R-2.1, R-13 note |
| Card replacement in packs | a pack manifest can replace a card (Takodana #10) | R-3.17 |
| Core Worlds spaces | space type, non-adjacent pair, crossing, "nearest" ignores it, patrols cannot cross, shared patrol spawn spaces | R-3.4 to R-3.6, R-5.4 to R-5.10, R-5.24 to R-5.33, R-11.22 |
| Core Worlds deck | sections "Core Worlds" / "Across the Core", chosen by an announcement before drawing | R-15.73, R-15.75, R-15.80 |
| Orange contacts | fourth class; placed on the planet space itself; empty contact space definition | R-3.9, R-12.3, R-12.4, R-25.6 |
| Contact matching a character | discarded on reveal, another revealed | R-12.14, R-16.26 |
| Reveal contact icon | market card bottom field (already transcribed as `reveal_contact`) with a player-count range | R-16.26 |
| Random job resolution card | `databank.resolve` already draws at random within a number; the expansion only adds cards | R-13.3, R-13.8 |
| Rotating assets | card with two asset faces and an orientation; `asset.rotate`; cost from the starting face; orientation kept on trade | R-14.7 to R-14.12 |
| Forced assets | flag: must be gained, discard to open a slot | R-25.1 |
| Multi-player and opposed jobs | job step machine with a second participant; "(opposed)" skill entries; job fails if any participant is defeated | R-15.33, R-15.40, R-15.41 |
| Bounties on characters | bounty target can be a character; fixed replacement reward; no capture | R-15.65 to R-15.71 |
| Two-way character flip | characters that flip back to the front side; goal tokens kept on assets | R-25.7 |
| Fame modifiers order | "gain X additional fame" is part of the reward; "you have X additional fame" is separate; modifiers that reduce rewards | R-18.15, R-25.2 to R-25.4, R-25.12 |
| Damage modifiers order | reductions apply before caps | R-25.5 |
| Ambitions `[OPT]` | sheet with setup override, goal types (dash, number, infinity, fame), progress tokens, sheet abilities with a destination | R-19 |
| Favors and debt tokens `[OPT]` | four temporary effects (skill for the next test, +1 die, treated as `{REP_POS}`, +1 `{HYPERDRIVE}`), request window, one received per turn | R-10.9, R-20 |
| Bounty hunter AI `[SOLO]` | second AI script deck, bounty targets, multiple AI | R-23 |

The "treated as having" reputation of favors (R-10.9) needs a reputation override layer: reputation is read through a query that applies overrides, never from the raw track.

---

## 11. Consequences for T2.2 and T2.3

- Card data = static fields (§1.2) + `abilities[]` (§2), each ability a small tree of primitives (§4) with connectors (§5.1), conditions (§6) and amounts (§7). Text stays as localization keys; errata are applied in the tree and recorded in `source`.
- The core reads stats, reputation, fame and slot capacity through **queries** that apply passive modifiers and overrides (§3.1, §10). Nothing caches a modified value.
- Triggers (§3.3) are named events emitted by the core; replacement triggers (`would_be_defeated`, `test_would_fail`, `damage.cap`) run before the event resolves.
- Dice modification happens in windows inside tests and combats; each window asks every holder with a matching ability (`RULES.md` §26.2).
- Databank cards are procedures with context (§5.4). The job step machine is shared code.
- The validator rejects unknown primitive ids and unknown custom handlers.

---

## Appendix A — Coverage of the base game

Primitive ids per card, by deck. `custom:` marks §8 handlers. Trivial shared lines (bounty "remove after reward", cargo "cannot buy on", ILLEGAL delivery check) are listed once per deck.

### A.1 Market

**Cargo** (all: `buy.restrict`, destination, reward with `credits.gain` / `fame.gain` / `rep.gain`; ILLEGAL ones: delivery check → `databank.resolve` #1)
- Salvage-0, Droid Parts-3, Coaxium Crystal-4, Food Supplies-5, Stolen Supplies-1, Stolen Prototype-2, Stolen Hyperfuel-6, Stolen Weapons-9: no other text.
- Bacta Supplies-7: `step.action` + condition (non-droid) + `card.discard_self` → `damage.recover` all.
- Smuggling Compartment-8 (mod): `slot.modify`; `on.dice_rolled.delivery` `dice.turn` blank → `{HIT}`.

**Gear and mods**
- IQA-11 Blaster Rifle-0: `stat.modify`; `on.combat_won` `damage.cap` 2.
- Vibroknife-1: `window.dice_modify` `dice.turn`; `stat.modify` + skill condition.
- DL-44 Heavy Blaster Pistol-2, MPL-57 Grenade Launcher-4, Quad Laser Cannon-8, Improved Nav Computer-11, Shield Upgrade-13: `stat.modify` (Shield Upgrade also `step.action` `damage.recover` 1 ship).
- Armored Vest-3: `stat.modify`, `limit.trait`.
- Vibro-Ax-5: `stat.modify`; `dice.turn` + skill condition.
- Ion Cannon-6, Jetpack-10: `dice.cancel`, skill condition upgrade.
- Targeting Computer-7: `dice.reroll` blanks, once per combat.
- Plastoid Armor-9: `stat.modify`, `dice.opponent_fewer` + skill condition, `limit.trait`.
- Maneuvering Thrusters-12: `stat.modify`, `dice.opponent_fewer` + skill condition.
- Autoblaster-14: `dice.turn` ×2, skill condition alternative.

**Jobs** (all: skills, destination, `databank.resolve` or inline test, reward, `card.remove_self` or `card.discard_self`)
- Rescue from the Mines-0, Rooting Out Spies-1, The Casino Heist-2, The Kessel Run-3, The Jewel of Yavin-4, Relic of the Past-7, Hijacking a Freighter-12, Jabba's Hidden Stash-13: `databank.resolve` (§5.4 job steps).
- Hutt Favor-5, Rebel Favor-8, Imperial Favor-10, Syndicate Favor-11: inline `test.skill` → `job.complete` or `databank.resolve` #41; reward with `if.before`.
- Hired Help-6, -9 (crew): `on.test_before` `test.skill_grant`; discard on pass.

**Bounty deck**
- Saw Gerrera-0 … 4-LOM-8 (9 bounties): class, combat, elimination / capture rewards, `on.bounty_reward` `card.remove_self`; Hera Syndulla-2, Ziton Moj-3, Garindan-5, Greedo-7 also `rep.lose`.
- Wanted-9, -10 (jobs): player destination (§6.2), `combat.fight` against a player, reward with fame comparison.

**Luxury**
- Extravagant Wardrobe-0: `fame.extra` by `{REP_POS}` count.
- Chromium Hull-1: `on.fame_gained` `fame.gain` +1; `on.combat_lost` `card.discard_self`; `custom:chromium_hull`.
- Gift for Jabba-2: cargo; `on.defeated` `asset.discard` crew.
- Stolen Jewels-3: `step.action` `card.discard_self`, both players `credits.gain` and `fame.gain`.
- Sy Snootles-4: `on.job_completed` reward to 0 → `fame.gain` additional.
- Proton Torpedoes-5: `stat.modify`; `on.combat_won` (patrol with fame reward) `fame.gain` additional.
- Attend Statue Dedication-6: job, destination by reputation, `job.complete`, reward with optional `credits.spend` → `fame.gain`.
- Legendary Game of Sabacc-7: job, `custom:sabacc`, reward split by who claims it.
- Undercover Agent-8: `step.action` + fame condition, `card.discard_self` → `fame.gain`, `rep.lose` (chosen faction, other player).
- Mandalorian Armor-9: `stat.modify`, `on.bounty_reward` reward to 0 → `fame.gain` additional, `limit.trait`.
- Dejarik Holotable-10: `fame.extra` by crew count; `step.action` at navpoint `dice.roll` per crew → `credits.gain` per result.

**Ships**
- Firespray-31-0, Modified YT-1300-1, HWK-290-2, YV-666-3, GX1-5, Lancer-6, Heavy-Duty Lifter-7, Aggressor-8: `ship.buy_sheet` of the linked sheet, `card.remove_self`.
- Used Ship for Sale-4: `custom:used_ship_for_sale`.

### A.2 Characters

- Ketsu Onyo-0: `step.encounter` patrol or contact within 3 spaces; goal counter (combats won); back `on.combat_won` `credits.gain`.
- Lando Calrissian-1: `on.dice_rolled` `dice.reroll` 1; goal counter (ILLEGAL delivered); back `step.action` on planet `credits.spend` → `test.skill` → `rep.gain` planet faction.
- Jyn Erso-2: `window.dice_modify` `dice.turn` `{FOCUS}`→`{CRIT}` once per turn; `step.action` `test.skill` → `custom:planet_damage_marker`; goal state check; back `custom:patrol_redirect`.
- Bossk-3: `step.action` `damage.recover` both; goal state check (cost sum); back `step.encounter` `combat.fight` player with more fame → `fame.gain`.
- Han Solo-4: `stat.modify` `{HYPERDRIVE}`; goal state check (ship cost); back `on.move_into` patrol `test.skill` → no stop.
- Doctor Aphra-5: `on.test_before` `test.skill_grant` once per turn; goal counter (jobs); back `step.planning` `market.extra_action`.
- Boba Fett-6: `step.action` `contact.look`; goal counter (bounty rewards); back `on.bounty_reward` (eliminate) `credits.gain`.
- IG-88-7: `dice.reroll` any, once per combat; `step.action` `asset.take` droid crew; goal state check (2 droid crew); back `on.would_be_defeated` `asset.discard` crew → `damage.recover` all.

Setup lines: `asset.gain_databank` (#90, #91, #92) and reputation set (R-10.7).

### A.3 Ship sheets

- Starter G9 Rigger / G-1A Starfighter: stats and slots only.
- Aggressor / IG-2000: goal counter (combats won against patrols); `rule.modify` (no reputation loss).
- Firespray / Slave I: activated goal (`step.encounter` `combat.fight` richer player); `on.move_into` `test.skill` → no stop.
- GX1 / Valorous: goal `on.asset_gained`; `rule.modify` (not forced to encounter).
- Heavy-Duty Lifter / Ark Angel: goal event + condition; `stat.modify` scaled by jobs.
- HWK-290 / Moldy Crow: activated goal (`step.action` `credits.spend`); `stat.modify` `{GROUND_COMBAT}`.
- Lancer / Shadow Caster: goal (deliver bounty contact); `custom:tractor_beam`.
- Modified YT-1300 / Millennium Falcon: goal event + condition (named crew); `on.dice_rolled.delivery` `dice.turn`.
- YV-666 / Hound's Tooth: goal `on.damage_recovered` `credits.spend`; `on.would_be_defeated` (ship combat) `dice.roll` → `damage.recover` 2.

### A.4 Contacts and contact databank cards (#2–#23)

Tokens: class, faction, droid, databank number (no text). Cards:
- #2 4-LOM, #6 BoShek, #14 Hondo Ohnaka: `credits.spend` (scaled discount on #2) → `asset.gain_embedded`, `contact.discard_token`. Crews: `stat.modify`; `on.dice_rolled.delivery` `dice.reroll`; `reward.modify`.
- #3 Bib Fortuna: `credits.spend` → `rep.choice` for a chosen player; `contact.relocate_self`.
- #4 Black Krrsantan: condition (has bounty or job) `credits.spend` → crew; else condition (`{REP_NEG}` count) `combat.fight` → `contact.discard_token`. Crew: `on.combat_start` `credits.spend` → `stat.modify` for this combat.
- #5 Black Sun Agent, #12 Greedo, #15 IG-RM Droid: reputation branches, `test.skill`, `damage.suffer`, `test.pass` paid; crews `reward.modify`, `on.dice_rolled.combat` `dice.add`, `stat.modify`.
- #7 Chewbacca, #18 Lobot, #22 W1-LE: identity condition → free crew, else paid or tested. Crews: `stat.modify` ×2; `on.planning_credits` `credits.gain` per `{REP_POS}`; `step.encounter` `market.extra_action`.
- #8 Dengar: `combat.fight` → `fame.gain`, `contact.discard_token`; else `contact.place_facedown`.
- #9 Doctor Evazan, #20 Ponda Baba: `test.skill`, `damage.suffer`, then paid crew. Crews: `custom:shared_die_result`; `combat.auto_win`.
- #10 Garindan: reputation condition `test.skill` → `move.patrol_direct` + `encounter.patrol`; paid crew. Crew: `step.action` `credits.spend` → `contact.look` any planet.
- #11 Grand Admiral Thrawn, #17 Leia Organa: reputation branches → embedded job, or `combat.fight` with `damage.prevent`, or `credits.spend` → `rep.gain`.
- #13 Hera Syndulla: reputation condition paid crew, else `rep.gain`. Crew: `step.action` `test.skill` → `move.player` 1 or `damage.suffer`.
- #16 K-2SO: `test.skill` → crew, or `damage.suffer` by reputation. Crew: `damage.crit_value`.
- #19 Maz Kanata: paid crew, else `contact.relocate_self`. Crew: `on.dice_rolled.test` `dice.reroll` once per turn.
- #21 Saw Gerrera: identity / reputation / test branches. Crew: `step.action` `damage.suffer` on a chosen character or ship, extra by its reputation.
- #23 Ziton Moj: `rep.lose` → `choice.either` `credits.gain` or `credits.take` from a player with more fame.

### A.5 Databank procedures

- #1 a–d cargo check: intro `if.state` → `step.end`; sections by die result with `combat.fight`, `test.skill`, `rep.lose`, `credits.spend`, `cargo.deliver`, `turn.end`.
- #40 a–c: `custom:sabacc`.
- #41 a–b: favor sections by faction: `if.state` → `card.discard_self` (job); `test.skill` → `rep.lose`.
- #42–#53: job step machine with `test.skill`, `combat.fight`, `damage.suffer`, `damage.prevent`, `credits.lose` (unless), `move.player_direct` (#46 to the Maelstrom), `job.fail`, `job.complete`.
- #90 a–d, #91 a–c, #92 a–c: starting bounty, cargo and job (static fields; job encounters inline `test.skill`).

### A.6 Encounter cards (70)

Every section is one-shot text except secrets, which become `step.action`, `step.encounter` or triggered abilities held by the player.

- **Cantonica / Ord Mantell:** Cantonica-5 `custom:double_or_nothing`; Cantonica-6 `dice.roll` table; Cantonica-7 `test.skill`; Cantonica-8 `credits.lose`, `test.skill`, crew (Thief: `step.action` `credits.take`); Cantonica-9 participation + `custom:sabacc`; Cantonica-0, -1 crew (Savvy Pilot `on.test_would_fail`; Runaway Stable Hands `step.action` `fame.gain`); Cantonica-2 `custom:side_bets`; Cantonica-3 amount spent × 2; Cantonica-4 `custom:name_a_number`. Ord Mantell: embedded gear and mods (Thermal Detonator, Flame Thrower, Medpack), `market.buy` reveal 3 with discount, `rep.lose` → `credits.gain`, secret `asset.take` bounty, `opt.unless` combat, `credits.gain` per bounty.
- **Kessel / Maelstrom:** embedded cargo (Spice, Unrefined Coaxium `on.action_step_end`), `market.buy` reveal 3 filtered ILLEGAL, `market.take` "Syndicate Favor", reputation trades for fame, `move.patrol` toward you per `{REP_NEG}`, `rep.lose` any amount (Kessel-7). Maelstrom: `test.skill` → `turn.additional` or `move.player` 1 + `damage.suffer` / `asset.discard`; `combat.fight` creature with `damage.prevent` all; `move.patrol_direct` + `encounter.patrol` with `dice.add`.
- **Lothal / Mon Calamari:** fame purchase by fame rank (`if.state` most famous), `market.buy` reveal 3, secrets (`move.player_direct` paid, errata R-24.3; combat against a player; `on.combat_won` vs patrol; `on.move_into` Maelstrom), embedded mod and jobs, `damage.recover` all with fallback, cargo `cargo.deliver` anywhere, `market.take` "Rebel Favor", `move.patrol` up to 4 any direction, chosen player `rep.gain` + `damage.suffer`, Refugees `on.discarded`.
- **Navpoint:** sections by patrol presence; reveal 3 any deck, `move.player` up to N, `move.patrol_direct` (to you, adjacent, anywhere, to another player), `encounter.patrol` forced on others, `rep.gain` with a patrol in your space + `if.before` bonus, `turn.additional`, secrets with fame transfer between players.
- **Ring of Kafrene / Takodana:** fame purchase with barter allowed (R-16.37), `asset.discard` → `credits.gain` 2× cost, embedded mods (Cloaking Device, Seismic Charge, Escape Pod) and crew (Gamorrean Mercenary, Weapon Specialist, Codebreaker, GA-97), `contact.move`, `contact.exchange` (errata R-24.4), `contact.look` per `{REP_POS}`, `encounter.space` (another Takodana card), `encounter.contact`, `custom:pay_for_goal`.
- **Ryloth / Naboo:** `cargo.deliver` anywhere, secrets that gain the cargo above when on a named planet, `asset.discard` → `credits.gain` / `rep.gain`, `fame.lose` → `market.buy` free, `market.buy` reveal 3 from a choice of decks, `custom:buy_any_sheet`, `custom:replace_with_starter` (errata R-24.5), `combat.fight` TIE interceptors, `choice.either` reputation.
- **Tatooine / Nal Hutta:** `contact.look` near the Hutt patrol, `move.patrol_direct` paid or free, embedded bounties (Hondo Ohnaka, IG-RM Droid) and cargo and job, chosen player `rep.lose`, secrets (combat against a player → `asset.take`; `custom:sabacc` with ship sheet trade; `bounty.deliver` anywhere; reputation loss by token faction, errata R-24.1), `asset.gain_databank` #90 with fallback, gamble by dice spent, `market.buy` search bounty deck.

### A.7 AI smuggler deck (10)

Steps and items of §5.5; primitives `damage.recover`, `ai.move_toward_goal`, `credits.gain`, `cargo.deliver`, `damage.suffer`, `rep.lose`, `market.buy` with fallback, `ai.goal_token`, `encounter.patrol`, `job.complete`, `credits.spend` → `fame.gain`, `contact.reveal` + `asset.gain_embedded`, `market.discard_top`, reshuffle (AI card #10).
