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
