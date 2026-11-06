using System;
using System.Collections.Generic;
using OuterRim.Core.Content;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;

namespace OuterRim.Client
{
    /// <summary>
    /// The way the interface reaches the rules: send a command, receive events (DECISIONS §7). A local game
    /// runs the core in process; an online game will send the command to the host behind the same interface.
    /// </summary>
    public interface ICommandChannel
    {
        /// <summary>Sends a command. A rejected command changes nothing and raises no events.</summary>
        CommandResult Send(Command command);

        /// <summary>Events of every accepted command, in order.</summary>
        event Action<IReadOnlyList<GameEvent>> EventsReceived;
    }

    /// <summary>
    /// One game in progress on this machine: the static content, the state, and the local command channel.
    /// Views read the state and follow the events; they never change the state themselves.
    /// </summary>
    public sealed class GameSession : ICommandChannel
    {
        private GameSession(GameContent content, GameState state)
        {
            Content = content;
            State = state;
        }

        public GameContent Content { get; }

        public GameState State { get; }

        public event Action<IReadOnlyList<GameEvent>> EventsReceived;

        /// <summary>Starts a new game (R-3) and returns the session with the setup events.</summary>
        public static GameSession NewGame(GameContent content, GameSetup setup, out IReadOnlyList<GameEvent> events)
        {
            return new GameSession(content, GameEngine.NewGame(content, setup, out events));
        }

        /// <summary>Resumes a game from a state, such as a loaded save (DECISIONS §6).</summary>
        public static GameSession Resume(GameContent content, GameState state)
        {
            return new GameSession(content, state);
        }

        public CommandResult Send(Command command)
        {
            var result = GameEngine.Execute(Content, State, command);
            if (result.Accepted)
            {
                EventsReceived?.Invoke(result.Events);
            }
            return result;
        }

        /// <summary>The player who must act now: the one a decision waits on, else the current player.</summary>
        public string Actor => State.Pending != null ? State.Pending.PlayerId : State.CurrentPlayerId;

        public bool Over => State.Phase == GamePhase.Ended;
    }
}
