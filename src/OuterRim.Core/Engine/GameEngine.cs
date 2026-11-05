using System;
using System.Collections.Generic;
using OuterRim.Core.Dice;
using OuterRim.Core.Model;
using OuterRim.Core.Random;

namespace OuterRim.Core.Engine
{
    /// <summary>Who sits at the table, before the game starts.</summary>
    public sealed class PlayerSetup
    {
        public PlayerSetup(string id, string name, string color)
        {
            Id = id;
            Name = name;
            Color = color;
        }

        public string Id { get; }

        public string Name { get; }

        public string Color { get; }
    }

    /// <summary>Parameters of a new game.</summary>
    public sealed class GameSetup
    {
        public GameSetup(ulong seed, IReadOnlyList<PlayerSetup> players, IReadOnlyList<PackRef> packs)
        {
            Seed = seed;
            Players = players;
            Packs = packs;
        }

        public ulong Seed { get; }

        /// <summary>Players in seating order, clockwise.</summary>
        public IReadOnlyList<PlayerSetup> Players { get; }

        public IReadOnlyList<PackRef> Packs { get; }

        public int FameRequirement { get; set; } = 10;
    }

    /// <summary>
    /// Entry point of the rules: creates games and applies commands. Stateless: the state is passed in
    /// and changed in place only when a command is accepted.
    /// </summary>
    public static class GameEngine
    {
        public const int MinPlayers = 1;
        public const int MaxPlayers = 4;

        /// <summary>R-3.11.g: starting credits by turn order.</summary>
        private static readonly int[] StartingCredits = { 4000, 6000, 8000, 10000 };

        /// <summary>R-4.6: credits gained by the planning option.</summary>
        public const int PlanningCredits = 2000;

        /// <summary>R-3.11: dice each player rolls to decide the first player.</summary>
        public const int FirstPlayerDice = 6;

        /// <summary>
        /// Creates a game: first player by dice (R-3.11), neutral reputation (R-3.11.d), fame 0 (R-3.11.c),
        /// credits by turn order (R-3.11.g). Characters, ships, map and decks are added by later tasks.
        /// </summary>
        public static GameState NewGame(GameSetup setup, out IReadOnlyList<GameEvent> events)
        {
            if (setup.Players.Count < MinPlayers || setup.Players.Count > MaxPlayers)
            {
                throw new ArgumentException("A game has 1 to 4 players (DECISIONS §1).", nameof(setup));
            }
            var seen = new HashSet<string>();
            foreach (var p in setup.Players)
            {
                if (!seen.Add(p.Id))
                {
                    throw new ArgumentException("Duplicate player id " + p.Id, nameof(setup));
                }
            }

            var state = new GameState
            {
                Seed = setup.Seed,
                Random = new GameRandom(setup.Seed),
                Packs = new List<PackRef>(setup.Packs),
                FameRequirement = setup.FameRequirement,
            };
            foreach (var p in setup.Players)
            {
                state.Players.Add(new PlayerState { Id = p.Id, Name = p.Name, Color = p.Color });
            }

            var log = new List<GameEvent>();
            string first = RollForFirstPlayer(state, log);
            state.FirstPlayerId = first;
            log.Add(new FirstPlayerChosenEvent(first));

            int firstSeat = state.SeatOf(first);
            for (int i = 0; i < state.Players.Count; i++)
            {
                var player = state.Players[(firstSeat + i) % state.Players.Count];
                player.Credits = StartingCredits[i];
                log.Add(new CreditsGainedEvent(player.Id, StartingCredits[i]));
            }

            state.Phase = GamePhase.Playing;
            state.Round = 1;
            state.CurrentPlayerId = first;
            state.Step = TurnStep.Planning;
            log.Add(new StepStartedEvent(first, TurnStep.Planning));
            events = log;
            return state;
        }

        /// <summary>R-3.11: everyone rolls six dice; most hits plus crits goes first; tied players reroll.</summary>
        private static string RollForFirstPlayer(GameState state, List<GameEvent> log)
        {
            var contenders = new List<string>();
            foreach (var p in state.Players)
            {
                contenders.Add(p.Id);
            }
            while (contenders.Count > 1)
            {
                int best = -1;
                var leaders = new List<string>();
                foreach (var id in contenders)
                {
                    var faces = DiceRoller.Roll(state.Random, FirstPlayerDice);
                    int score = DiceRoller.Count(faces, DieFace.Hit) + DiceRoller.Count(faces, DieFace.Crit);
                    log.Add(new FirstPlayerRolledEvent(id, faces, score));
                    if (score > best)
                    {
                        best = score;
                        leaders.Clear();
                    }
                    if (score == best)
                    {
                        leaders.Add(id);
                    }
                }
                contenders = leaders;
            }
            return contenders[0];
        }

        /// <summary>Validates and applies a command. A rejected command changes nothing.</summary>
        public static CommandResult Execute(GameState state, Command command)
        {
            if (state.Phase != GamePhase.Playing)
            {
                return CommandResult.Rejected("The game is not in progress.");
            }
            var player = state.FindPlayer(command.PlayerId);
            if (player == null)
            {
                return CommandResult.Rejected("Unknown player " + command.PlayerId + ".");
            }
            if (player.Id != state.CurrentPlayerId)
            {
                return CommandResult.Rejected("R-4.5: only the current player acts during their turn.");
            }

            switch (command)
            {
                case GainCreditsCommand _:
                    return GainCredits(state, player);
                case RecoverDamageCommand _:
                    return RecoverDamage(state, player);
                default:
                    return CommandResult.Rejected("Command not supported yet: " + command.GetType().Name + ".");
            }
        }

        /// <summary>R-4.6 planning option "gain 2,000 credits"; not allowed while defeated (R-4.8).</summary>
        private static CommandResult GainCredits(GameState state, PlayerState player)
        {
            if (state.Step != TurnStep.Planning)
            {
                return CommandResult.Rejected("R-4.6: gaining credits is a planning step option.");
            }
            if (player.Defeated)
            {
                return CommandResult.Rejected("R-4.8: a defeated player must recover damage.");
            }
            player.Credits += PlanningCredits;
            var events = new List<GameEvent> { new CreditsGainedEvent(player.Id, PlanningCredits) };
            EndPlanning(state, player, events);
            return CommandResult.Ok(events);
        }

        /// <summary>R-4.6 planning option "recover damage": remove all damage from character and ship.</summary>
        private static CommandResult RecoverDamage(GameState state, PlayerState player)
        {
            if (state.Step != TurnStep.Planning)
            {
                return CommandResult.Rejected("R-4.6: recovering damage is a planning step option.");
            }
            var events = new List<GameEvent>
            {
                new DamageRecoveredEvent(player.Id, player.CharacterDamage, player.ShipDamage),
            };
            player.CharacterDamage = 0;
            player.ShipDamage = 0;
            player.Defeated = false;
            EndPlanning(state, player, events);
            return CommandResult.Ok(events);
        }

        /// <summary>R-4.2: after the planning option, the action step begins.</summary>
        private static void EndPlanning(GameState state, PlayerState player, List<GameEvent> events)
        {
            state.Step = TurnStep.Action;
            events.Add(new StepStartedEvent(player.Id, TurnStep.Action));
        }
    }
}
