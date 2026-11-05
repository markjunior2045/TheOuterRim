#nullable enable
using System.Collections.Generic;
using OuterRim.Core.Model;

namespace OuterRim.Core.Engine
{
    /// <summary>Something that happened. The UI animates events; the state already reflects them.</summary>
    public abstract class GameEvent
    {
    }

    /// <summary>A player's roll to decide the first player (R-3.11).</summary>
    public sealed class FirstPlayerRolledEvent : GameEvent
    {
        public FirstPlayerRolledEvent(string playerId, IReadOnlyList<DieFace> faces, int score)
        {
            PlayerId = playerId;
            Faces = faces;
            Score = score;
        }

        public string PlayerId { get; }

        public IReadOnlyList<DieFace> Faces { get; }

        /// <summary>Hits plus crits.</summary>
        public int Score { get; }
    }

    public sealed class FirstPlayerChosenEvent : GameEvent
    {
        public FirstPlayerChosenEvent(string playerId)
        {
            PlayerId = playerId;
        }

        public string PlayerId { get; }
    }

    public sealed class CreditsGainedEvent : GameEvent
    {
        public CreditsGainedEvent(string playerId, int amount)
        {
            PlayerId = playerId;
            Amount = amount;
        }

        public string PlayerId { get; }

        public int Amount { get; }
    }

    public sealed class DamageRecoveredEvent : GameEvent
    {
        public DamageRecoveredEvent(string playerId, int character, int ship)
        {
            PlayerId = playerId;
            Character = character;
            Ship = ship;
        }

        public string PlayerId { get; }

        public int Character { get; }

        public int Ship { get; }
    }

    public sealed class StepStartedEvent : GameEvent
    {
        public StepStartedEvent(string playerId, TurnStep step)
        {
            PlayerId = playerId;
            Step = step;
        }

        public string PlayerId { get; }

        public TurnStep Step { get; }
    }

    public sealed class TurnStartedEvent : GameEvent
    {
        public TurnStartedEvent(string playerId, int round)
        {
            PlayerId = playerId;
            Round = round;
        }

        public string PlayerId { get; }

        public int Round { get; }
    }

    public sealed class TurnEndedEvent : GameEvent
    {
        public TurnEndedEvent(string playerId)
        {
            PlayerId = playerId;
        }

        public string PlayerId { get; }
    }

    /// <summary>A player's standee moved. Direct moves (R-5.23) jump without passing through spaces.</summary>
    public sealed class PlayerMovedEvent : GameEvent
    {
        public PlayerMovedEvent(string playerId, string from, IReadOnlyList<string> path, bool direct)
        {
            PlayerId = playerId;
            From = from;
            Path = path;
            Direct = direct;
        }

        public string PlayerId { get; }

        public string From { get; }

        /// <summary>Spaces entered, in order; for a direct move, only the destination.</summary>
        public IReadOnlyList<string> Path { get; }

        public bool Direct { get; }
    }

    /// <summary>An encounter finished resolving (kind: space, patrol, contact).</summary>
    public sealed class EncounterResolvedEvent : GameEvent
    {
        public EncounterResolvedEvent(string playerId, string kind, string target)
        {
            PlayerId = playerId;
            Kind = kind;
            Target = target;
        }

        public string PlayerId { get; }

        public string Kind { get; }

        public string Target { get; }
    }

    /// <summary>A skill test was rolled and judged (R-6.9).</summary>
    public sealed class SkillTestRolledEvent : GameEvent
    {
        public SkillTestRolledEvent(string playerId, string rollerLabel, string skill, int instances, IReadOnlyList<DieFace> faces,
            bool passed)
        {
            PlayerId = playerId;
            RollerLabel = rollerLabel;
            Skill = skill;
            Instances = instances;
            Faces = faces;
            Passed = passed;
        }

        public string PlayerId { get; }

        /// <summary>Who rolled: "Lando Calrissian (Player 1)".</summary>
        public string RollerLabel { get; }

        public string Skill { get; }

        /// <summary>Instances of the skill counted for this test (R-6.7).</summary>
        public int Instances { get; }

        public IReadOnlyList<DieFace> Faces { get; }

        public bool Passed { get; }
    }

    public sealed class CombatStartedEvent : GameEvent
    {
        public CombatStartedEvent(string type, string attackerId, string defenderKind, string defender, int attackerValue,
            string defenderValue)
        {
            Type = type;
            AttackerId = attackerId;
            DefenderKind = defenderKind;
            Defender = defender;
            AttackerValue = attackerValue;
            DefenderValue = defenderValue;
        }

        /// <summary>ground or ship (R-7.2).</summary>
        public string Type { get; }

        public string AttackerId { get; }

        /// <summary>player, npc or patrol.</summary>
        public string DefenderKind { get; }

        public string Defender { get; }

        public int AttackerValue { get; }

        /// <summary>Number of dice, or "infinite" for a level-4 patrol (R-11.6).</summary>
        public string DefenderValue { get; }
    }

    /// <summary>One side's roll and its damage (R-7.10 to R-7.12).</summary>
    public sealed class CombatRolledEvent : GameEvent
    {
        public CombatRolledEvent(string side, string? rollerId, string rollerLabel, IReadOnlyList<DieFace> faces, int damage)
        {
            Side = side;
            RollerId = rollerId;
            RollerLabel = rollerLabel;
            Faces = faces;
            Damage = damage;
        }

        /// <summary>attacker or defender.</summary>
        public string Side { get; }

        /// <summary>
        /// The player who rolled; null when the game rolled for a patrol, contact or card enemy
        /// (digital adaptation of R-7.9, DECISIONS §4.2).
        /// </summary>
        public string? RollerId { get; }

        /// <summary>Who rolled, for logs and the interface: "Ketsu Onyo (Player 1)", "Hutt Patrol Lvl 2 (Game)".</summary>
        public string RollerLabel { get; }

        public IReadOnlyList<DieFace> Faces { get; }

        public int Damage { get; }
    }

    /// <summary>The winner of a combat (R-7.13): attacker, defender, or none if it could not happen.</summary>
    public sealed class CombatResolvedEvent : GameEvent
    {
        public CombatResolvedEvent(string winner, int attackerDamage, int defenderDamage)
        {
            Winner = winner;
            AttackerDamage = attackerDamage;
            DefenderDamage = defenderDamage;
        }

        public string Winner { get; }

        public int AttackerDamage { get; }

        public int DefenderDamage { get; }
    }

    /// <summary>Damage placed on a character or ship (R-8.1); Placed may be less than Amount (R-8.4).</summary>
    public sealed class DamageSufferedEvent : GameEvent
    {
        public DamageSufferedEvent(string playerId, string target, int amount, int placed, bool becameDefeated)
        {
            PlayerId = playerId;
            Target = target;
            Amount = amount;
            Placed = placed;
            BecameDefeated = becameDefeated;
        }

        public string PlayerId { get; }

        /// <summary>character or ship.</summary>
        public string Target { get; }

        public int Amount { get; }

        public int Placed { get; }

        public bool BecameDefeated { get; }
    }

    /// <summary>Credits spent (a payment, R-9.8) or lost (as much as possible).</summary>
    public sealed class CreditsLostEvent : GameEvent
    {
        public CreditsLostEvent(string playerId, int amount, bool spent)
        {
            PlayerId = playerId;
            Amount = amount;
            Spent = spent;
        }

        public string PlayerId { get; }

        public int Amount { get; }

        public bool Spent { get; }
    }

    public sealed class ReputationChangedEvent : GameEvent
    {
        public ReputationChangedEvent(string playerId, Faction faction, Reputation from, Reputation to)
        {
            PlayerId = playerId;
            Faction = faction;
            From = from;
            To = to;
        }

        public string PlayerId { get; }

        public Faction Faction { get; }

        public Reputation From { get; }

        public Reputation To { get; }
    }

    public sealed class FameChangedEvent : GameEvent
    {
        public FameChangedEvent(string playerId, int change, int total)
        {
            PlayerId = playerId;
            Change = change;
            Total = total;
        }

        public string PlayerId { get; }

        public int Change { get; }

        public int Total { get; }
    }

    /// <summary>R-8.8: the player became defeated (standee laid down).</summary>
    public sealed class PlayerDefeatedEvent : GameEvent
    {
        public PlayerDefeatedEvent(string playerId)
        {
            PlayerId = playerId;
        }

        public string PlayerId { get; }
    }

    /// <summary>R-8.10: the player is no longer defeated (standee stood up).</summary>
    public sealed class PlayerRecoveredEvent : GameEvent
    {
        public PlayerRecoveredEvent(string playerId)
        {
            PlayerId = playerId;
        }

        public string PlayerId { get; }
    }

    /// <summary>R-8.8.2, R-15.89: all secrets discarded unrevealed.</summary>
    public sealed class SecretsDiscardedEvent : GameEvent
    {
        public SecretsDiscardedEvent(string playerId, IReadOnlyList<string> cards)
        {
            PlayerId = playerId;
            Cards = cards;
        }

        public string PlayerId { get; }

        public IReadOnlyList<string> Cards { get; }
    }

    /// <summary>R-18.8: the game is over. Tiebreak is true when several players reached the requirement together (R-18.16).</summary>
    public sealed class GameWonEvent : GameEvent
    {
        public GameWonEvent(string playerId, bool tiebreak)
        {
            PlayerId = playerId;
            Tiebreak = tiebreak;
        }

        public string PlayerId { get; }

        public bool Tiebreak { get; }
    }

    /// <summary>A patrol moved (R-11.15) or was moved directly (R-11.23).</summary>
    public sealed class PatrolMovedEvent : GameEvent
    {
        public PatrolMovedEvent(Faction faction, string from, string to, bool direct)
        {
            Faction = faction;
            From = from;
            To = to;
            Direct = direct;
        }

        public Faction Faction { get; }

        public string From { get; }

        public string To { get; }

        public bool Direct { get; }
    }

    /// <summary>R-11.8: a defeated patrol is removed from the game.</summary>
    public sealed class PatrolEliminatedEvent : GameEvent
    {
        public PatrolEliminatedEvent(Faction faction, int level, string spaceId)
        {
            Faction = faction;
            Level = level;
            SpaceId = spaceId;
        }

        public Faction Faction { get; }

        public int Level { get; }

        public string SpaceId { get; }
    }

    /// <summary>R-11.10: the next patrol of a faction enters the map next to its stack.</summary>
    public sealed class PatrolSpawnedEvent : GameEvent
    {
        public PatrolSpawnedEvent(Faction faction, int level, string spaceId)
        {
            Faction = faction;
            Level = level;
            SpaceId = spaceId;
        }

        public Faction Faction { get; }

        public int Level { get; }

        public string SpaceId { get; }
    }

    public sealed class AssetGainedEvent : GameEvent
    {
        public AssetGainedEvent(string playerId, string cardId, string kind)
        {
            PlayerId = playerId;
            CardId = cardId;
            Kind = kind;
        }

        public string PlayerId { get; }

        public string CardId { get; }

        public string Kind { get; }
    }

    /// <summary>An asset left the player's slots. Reason: open_slot (R-14.4), no_slot (R-14.18), barter (R-16.31), or an effect.</summary>
    public sealed class AssetDiscardedEvent : GameEvent
    {
        public AssetDiscardedEvent(string playerId, string cardId, string kind, string reason)
        {
            PlayerId = playerId;
            CardId = cardId;
            Kind = kind;
            Reason = reason;
        }

        public string PlayerId { get; }

        public string CardId { get; }

        public string Kind { get; }

        public string Reason { get; }
    }

    /// <summary>R-14.5: the player chose not to gain the asset; it is discarded.</summary>
    public sealed class AssetDeclinedEvent : GameEvent
    {
        public AssetDeclinedEvent(string playerId, string cardId, string kind)
        {
            PlayerId = playerId;
            CardId = cardId;
            Kind = kind;
        }

        public string PlayerId { get; }

        public string CardId { get; }

        public string Kind { get; }
    }

    /// <summary>R-16.27: the player replaced their ship sheet.</summary>
    public sealed class ShipChangedEvent : GameEvent
    {
        public ShipChangedEvent(string playerId, string fromSheet, string toSheet, string side, int pricePaid)
        {
            PlayerId = playerId;
            FromSheet = fromSheet;
            ToSheet = toSheet;
            Side = side;
            PricePaid = pricePaid;
        }

        public string PlayerId { get; }

        public string FromSheet { get; }

        public string ToSheet { get; }

        public string Side { get; }

        public int PricePaid { get; }
    }

    /// <summary>A step ability was used (EFFECTS.md §3.2).</summary>
    public sealed class AbilityUsedEvent : GameEvent
    {
        public AbilityUsedEvent(string playerId, string cardId, string path, string step)
        {
            PlayerId = playerId;
            CardId = cardId;
            Path = path;
            Step = step;
        }

        public string PlayerId { get; }

        public string CardId { get; }

        public string Path { get; }

        public string Step { get; }
    }

    /// <summary>A character card or ship sheet was flipped (R-18.3). Side: 0 front, 1 back for characters.</summary>
    public sealed class SheetFlippedEvent : GameEvent
    {
        public SheetFlippedEvent(string playerId, string sheet, int side)
        {
            PlayerId = playerId;
            Sheet = sheet;
            Side = side;
        }

        public string PlayerId { get; }

        public string Sheet { get; }

        public int Side { get; }
    }

    /// <summary>R-25.14: a damage token placed on a planet as a marker.</summary>
    public sealed class PlanetMarkerPlacedEvent : GameEvent
    {
        public PlanetMarkerPlacedEvent(string playerId, string spaceId)
        {
            PlayerId = playerId;
            SpaceId = spaceId;
        }

        public string PlayerId { get; }

        public string SpaceId { get; }
    }

    /// <summary>An ability changed a roll (reroll, turn, add, cancel). Before and After use the DicePool codes.</summary>
    public sealed class DiceModifiedEvent : GameEvent
    {
        public DiceModifiedEvent(string playerId, string cardId, string op, string before, string after)
        {
            PlayerId = playerId;
            CardId = cardId;
            Op = op;
            Before = before;
            After = after;
        }

        public string PlayerId { get; }

        public string CardId { get; }

        public string Op { get; }

        public string Before { get; }

        public string After { get; }
    }

    /// <summary>Dice rolled by a card effect ("Roll a die. On a ..."), outside tests and combat.</summary>
    public sealed class EffectDiceRolledEvent : GameEvent
    {
        public EffectDiceRolledEvent(string playerId, string rollerLabel, string cardId, IReadOnlyList<DieFace> faces)
        {
            PlayerId = playerId;
            RollerLabel = rollerLabel;
            CardId = cardId;
            Faces = faces;
        }

        public string PlayerId { get; }

        public string RollerLabel { get; }

        public string CardId { get; }

        public IReadOnlyList<DieFace> Faces { get; }
    }

    /// <summary>R-18.5: a goal token placed toward a goal.</summary>
    public sealed class GoalProgressEvent : GameEvent
    {
        public GoalProgressEvent(string playerId, string cardId, string path, int count, int needed)
        {
            PlayerId = playerId;
            CardId = cardId;
            Path = path;
            Count = count;
            Needed = needed;
        }

        public string PlayerId { get; }

        public string CardId { get; }

        public string Path { get; }

        public int Count { get; }

        public int Needed { get; }
    }

    /// <summary>R-3.15: a player's starting databank card; they start on its planet.</summary>
    public sealed class StartingCardDrawnEvent : GameEvent
    {
        public StartingCardDrawnEvent(string playerId, string cardId, string spaceId)
        {
            PlayerId = playerId;
            CardId = cardId;
            SpaceId = spaceId;
        }

        public string PlayerId { get; }

        public string CardId { get; }

        public string SpaceId { get; }
    }

    public sealed class MarketCardBoughtEvent : GameEvent
    {
        public MarketCardBoughtEvent(string playerId, string deck, string cardId, int price)
        {
            PlayerId = playerId;
            Deck = deck;
            CardId = cardId;
            Price = price;
        }

        public string PlayerId { get; }

        public string Deck { get; }

        public string CardId { get; }

        public int Price { get; }
    }

    /// <summary>R-16.18: the top card went to the bottom of its deck.</summary>
    public sealed class MarketCardDiscardedEvent : GameEvent
    {
        public MarketCardDiscardedEvent(string deck, string cardId)
        {
            Deck = deck;
            CardId = cardId;
        }

        public string Deck { get; }

        public string CardId { get; }
    }

    /// <summary>R-16.42, R-16.43: a new top card is faceup.</summary>
    public sealed class MarketCardRevealedEvent : GameEvent
    {
        public MarketCardRevealedEvent(string deck, string cardId)
        {
            Deck = deck;
            CardId = cardId;
        }

        public string Deck { get; }

        public string CardId { get; }
    }

    public sealed class MarketDeckShuffledEvent : GameEvent
    {
        public MarketDeckShuffledEvent(string deck)
        {
            Deck = deck;
        }

        public string Deck { get; }
    }

    /// <summary>R-16.3: the die rolled before delivering an ILLEGAL cargo (after any changes).</summary>
    public sealed class DeliveryRolledEvent : GameEvent
    {
        public DeliveryRolledEvent(string playerId, string cargoId, DieFace face)
        {
            PlayerId = playerId;
            CargoId = cargoId;
            Face = face;
        }

        public string PlayerId { get; }

        public string CargoId { get; }

        public DieFace Face { get; }
    }

    public sealed class CargoDeliveredEvent : GameEvent
    {
        public CargoDeliveredEvent(string playerId, string cargoId)
        {
            PlayerId = playerId;
            CargoId = cargoId;
        }

        public string PlayerId { get; }

        public string CargoId { get; }
    }

    /// <summary>A databank card was drawn and resolved (R-13.2, R-13.3).</summary>
    public sealed class DatabankResolvedEvent : GameEvent
    {
        public DatabankResolvedEvent(string playerId, string cardId)
        {
            PlayerId = playerId;
            CardId = cardId;
        }

        public string PlayerId { get; }

        public string CardId { get; }
    }

    /// <summary>
    /// R-15.79: an encounter card was drawn. <see cref="Section"/> is the path of the section and variant
    /// resolved ("" if none applies); a secret is not read aloud (R-15.81), so the interface shows it only to
    /// its owner.
    /// </summary>
    public sealed class EncounterCardDrawnEvent : GameEvent
    {
        public EncounterCardDrawnEvent(string playerId, string deck, string cardId, string section, bool secret)
        {
            PlayerId = playerId;
            Deck = deck;
            CardId = cardId;
            Section = section;
            Secret = secret;
        }

        public string PlayerId { get; }

        public string Deck { get; }

        public string CardId { get; }

        public string Section { get; }

        public bool Secret { get; }
    }

    /// <summary>R-15.86: a secret was resolved and revealed, then discarded.</summary>
    public sealed class SecretDiscardedEvent : GameEvent
    {
        public SecretDiscardedEvent(string playerId, string cardId)
        {
            PlayerId = playerId;
            CardId = cardId;
        }

        public string PlayerId { get; }

        public string CardId { get; }
    }

    /// <summary>R-5.14: an additional turn will follow this one.</summary>
    public sealed class AdditionalTurnGainedEvent : GameEvent
    {
        public AdditionalTurnGainedEvent(string playerId)
        {
            PlayerId = playerId;
        }

        public string PlayerId { get; }
    }

    /// <summary>The engine paused and waits for a player's answer.</summary>
    public sealed class DecisionRequestedEvent : GameEvent
    {
        public DecisionRequestedEvent(PendingDecision decision)
        {
            Decision = decision;
        }

        public PendingDecision Decision { get; }
    }

    /// <summary>A pending decision was answered; resolution resumes.</summary>
    public sealed class DecisionAnsweredEvent : GameEvent
    {
        public DecisionAnsweredEvent(int decisionId, string playerId, string answer)
        {
            DecisionId = decisionId;
            PlayerId = playerId;
            Answer = answer;
        }

        public int DecisionId { get; }

        public string PlayerId { get; }

        /// <summary>The chosen option id, or option ids joined by ',' for an order.</summary>
        public string Answer { get; }
    }

    /// <summary>Outcome of a command: accepted with events, or rejected with a reason and no state change.</summary>
    public sealed class CommandResult
    {
        private CommandResult(bool accepted, string? reason, IReadOnlyList<GameEvent> events)
        {
            Accepted = accepted;
            Reason = reason;
            Events = events;
        }

        public bool Accepted { get; }

        /// <summary>Why the command was rejected, citing the rule id.</summary>
        public string? Reason { get; }

        public IReadOnlyList<GameEvent> Events { get; }

        public static CommandResult Ok(IReadOnlyList<GameEvent> events)
        {
            return new CommandResult(true, null, events);
        }

        public static CommandResult Rejected(string reason)
        {
            return new CommandResult(false, reason, new GameEvent[0]);
        }
    }
}
