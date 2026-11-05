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
