#nullable enable
using System.Collections.Generic;

namespace OuterRim.Core.Engine
{
    /// <summary>
    /// A request from a player (or the UI on their behalf). The core validates it against the
    /// rules and either rejects it or applies it and returns events (DECISIONS §7).
    /// </summary>
    public abstract class Command
    {
        protected Command(string playerId)
        {
            PlayerId = playerId;
        }

        /// <summary>The player issuing the command.</summary>
        public string PlayerId { get; }
    }

    /// <summary>Planning option: gain 2,000 credits (R-4.6).</summary>
    public sealed class GainCreditsCommand : Command
    {
        public GainCreditsCommand(string playerId) : base(playerId)
        {
        }
    }

    /// <summary>Planning option: recover all damage from character and ship (R-4.6).</summary>
    public sealed class RecoverDamageCommand : Command
    {
        public RecoverDamageCommand(string playerId) : base(playerId)
        {
        }
    }

    /// <summary>Planning option: move along a path of adjacent spaces (R-4.6, R-5.17). An empty path moves zero spaces.</summary>
    public sealed class MoveCommand : Command
    {
        public MoveCommand(string playerId, IReadOnlyList<string> path) : base(playerId)
        {
            Path = path;
        }

        /// <summary>Spaces entered, in order, not including the starting space.</summary>
        public IReadOnlyList<string> Path { get; }
    }

    /// <summary>Ends the action step (R-4.9: actions are optional).</summary>
    public sealed class EndActionStepCommand : Command
    {
        public EndActionStepCommand(string playerId) : base(playerId)
        {
        }
    }

    /// <summary>What the player encounters in the encounter step (R-4.13).</summary>
    public enum EncounterKind
    {
        Space,
        Patrol,
        Contact,

        /// <summary>Attempting a job at its destination (R-15.29); TargetId is the job card.</summary>
        Job,
    }

    /// <summary>The one encounter of the turn (R-4.12). TargetId is the patrol's faction or the contact token.</summary>
    public sealed class EncounterCommand : Command
    {
        public EncounterCommand(string playerId, EncounterKind kind, string? targetId = null) : base(playerId)
        {
            Kind = kind;
            TargetId = targetId;
        }

        public EncounterKind Kind { get; }

        public string? TargetId { get; }
    }

    /// <summary>Uses a "Planning:", "Action:" or "Encounter:" ability, located by card id and path in the card data.</summary>
    public sealed class UseAbilityCommand : Command
    {
        public UseAbilityCommand(string playerId, string cardId, string path) : base(playerId)
        {
            CardId = cardId;
            Path = path;
        }

        public string CardId { get; }

        /// <summary>JSON path of the ability inside the card, e.g. "abilities[0]" or "sides[1].abilities[1]".</summary>
        public string Path { get; }
    }

    /// <summary>Market action step 1: discard the top card of a deck to its bottom (R-16.18, R-16.19).</summary>
    public sealed class MarketDiscardCommand : Command
    {
        public MarketDiscardCommand(string playerId, string deck) : base(playerId)
        {
            Deck = deck;
        }

        public string Deck { get; }
    }

    /// <summary>Market action step 2: buy the top card of a deck, bartering the listed assets (R-16.21, R-16.31).</summary>
    public sealed class MarketBuyCommand : Command
    {
        public MarketBuyCommand(string playerId, string deck, IReadOnlyList<string>? barter = null) : base(playerId)
        {
            Deck = deck;
            Barter = barter ?? new string[0];
        }

        public string Deck { get; }

        public IReadOnlyList<string> Barter { get; }
    }

    /// <summary>The deliver action: cargo delivered one at a time, in this order (R-16.1, R-16.5).</summary>
    public sealed class DeliverCommand : Command
    {
        public DeliverCommand(string playerId, IReadOnlyList<string> cargo) : base(playerId)
        {
            Cargo = cargo;
        }

        public IReadOnlyList<string> Cargo { get; }
    }

    /// <summary>Base for answers to a <see cref="PendingDecision"/>.</summary>
    public abstract class AnswerCommand : Command
    {
        protected AnswerCommand(string playerId, int decisionId) : base(playerId)
        {
            DecisionId = decisionId;
        }

        public int DecisionId { get; }
    }

    /// <summary>Answers a <see cref="DecisionKind.ChooseOption"/> decision.</summary>
    public sealed class ChooseOptionCommand : AnswerCommand
    {
        public ChooseOptionCommand(string playerId, int decisionId, string optionId) : base(playerId, decisionId)
        {
            OptionId = optionId;
        }

        public string OptionId { get; }
    }

    /// <summary>Answers a <see cref="DecisionKind.ResolveTie"/> decision (R-5.30, R-21.13).</summary>
    public sealed class ResolveTieCommand : AnswerCommand
    {
        public ResolveTieCommand(string playerId, int decisionId, string optionId) : base(playerId, decisionId)
        {
            OptionId = optionId;
        }

        public string OptionId { get; }
    }

    /// <summary>Answers a <see cref="DecisionKind.ChooseOrder"/> decision with every option id, first to last (R-21.10).</summary>
    public sealed class ChooseOrderCommand : AnswerCommand
    {
        public ChooseOrderCommand(string playerId, int decisionId, IReadOnlyList<string> order) : base(playerId, decisionId)
        {
            Order = order;
        }

        public IReadOnlyList<string> Order { get; }
    }
}
