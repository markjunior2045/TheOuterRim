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
}
