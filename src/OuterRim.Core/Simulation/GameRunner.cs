#nullable enable
using System;
using System.Collections.Generic;
using OuterRim.Core.Content;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Persistence;
using OuterRim.Core.Random;

namespace OuterRim.Core.Simulation
{
    /// <summary>How a simulated game ended.</summary>
    public enum SimulationOutcome
    {
        /// <summary>A player reached the fame requirement (R-18.8).</summary>
        Won,

        /// <summary>The round limit was reached with no winner; the game was sound up to there.</summary>
        RoundLimit,

        /// <summary>An invariant broke (RULES §26.3, <see cref="Invariants"/>).</summary>
        Invariant,

        /// <summary>No candidate command was accepted: the game could not go on.</summary>
        Stuck,

        /// <summary>The engine threw.</summary>
        Crashed,
    }

    public sealed class SimulationOptions
    {
        public ulong Seed { get; set; }

        /// <summary>1 to 4; 0 picks a count from the seed.</summary>
        public int Players { get; set; }

        /// <summary>A game still running after this round ends as <see cref="SimulationOutcome.RoundLimit"/>.</summary>
        public int MaxRounds { get; set; } = 1000;

        /// <summary>Guard against a loop inside one turn.</summary>
        public int MaxCommands { get; set; } = 100000;

        /// <summary>Every N accepted commands, save, load and save again and compare (DECISIONS §6). 0 turns it off.</summary>
        public int SaveCheckEvery { get; set; } = 50;

        /// <summary>Compare the save before and after every rejected command ("a rejected command changes nothing"). Slow.</summary>
        public bool CheckRejections { get; set; }

        public int FameRequirement { get; set; } = 10;

        /// <summary>Called with every event, the game's creation included.</summary>
        public Action<GameState, GameEvent>? OnEvent { get; set; }

        /// <summary>Called with every accepted command, before its events.</summary>
        public Action<GameState, Command>? OnCommand { get; set; }
    }

    public sealed class SimulationResult
    {
        public ulong Seed { get; set; }

        public SimulationOutcome Outcome { get; set; }

        public string WinnerId { get; set; } = "";

        public int Rounds { get; set; }

        public int Commands { get; set; }

        public int Rejections { get; set; }

        /// <summary>Characters by seat, for the report.</summary>
        public List<string> Characters { get; set; } = new List<string>();

        /// <summary>Invariant violations, the stuck reason or the exception.</summary>
        public List<string> Problems { get; set; } = new List<string>();

        public GameState? FinalState { get; set; }

        public bool Sound => Outcome == SimulationOutcome.Won || Outcome == SimulationOutcome.RoundLimit;
    }

    /// <summary>
    /// Plays one seeded game with <see cref="RandomBot"/> in every seat, checking the invariants after
    /// every command (PLAN T3.1). The same seed always plays the same game.
    /// </summary>
    public static class GameRunner
    {
        private static readonly string[] Colors = { "green", "red", "yellow", "white" };

        public static SimulationResult Run(GameContent content, SimulationOptions options)
        {
            var result = new SimulationResult { Seed = options.Seed };
            var bot = new RandomBot(options.Seed);
            var setup = Setup(content, options, bot.Random);
            foreach (var p in setup.Players) result.Characters.Add(p.CharacterId);

            GameState state;
            Command? last = null;
            try
            {
                state = GameEngine.NewGame(content, setup, out var created);
                result.FinalState = state;
                Report(options, state, created);
                var monitor = new InvariantMonitor();
                var problems = monitor.Start(created);
                problems.AddRange(Invariants.Check(content, state));
                if (Stop(result, problems, "setup")) return result;

                while (state.Phase == GamePhase.Playing)
                {
                    if (state.Round > options.MaxRounds)
                    {
                        result.Outcome = SimulationOutcome.RoundLimit;
                        break;
                    }
                    if (result.Commands >= options.MaxCommands)
                    {
                        result.Outcome = SimulationOutcome.Stuck;
                        result.Problems.Add("More than " + options.MaxCommands + " commands in " + state.Round + " rounds.");
                        break;
                    }
                    CommandResult? accepted = null;
                    var reasons = new List<string>();
                    foreach (var command in bot.Candidates(content, state))
                    {
                        last = command;
                        string? before = options.CheckRejections ? SaveSerializer.Save(state) : null;
                        var r = GameEngine.Execute(content, state, command);
                        if (r.Accepted)
                        {
                            accepted = r;
                            break;
                        }
                        result.Rejections++;
                        if (reasons.Count < 8) reasons.Add(command.GetType().Name + ": " + r.Reason);
                        if (before != null && SaveSerializer.Save(state) != before)
                        {
                            Stop(result, new List<string> { "A rejected " + command.GetType().Name + " changed the state (" + r.Reason + ")." }, Where(state, result));
                            return result;
                        }
                    }
                    if (accepted == null)
                    {
                        result.Outcome = SimulationOutcome.Stuck;
                        result.Problems.Add("No command accepted at " + Where(state, result) + " for " + RandomBot.Actor(state)
                            + (state.Pending != null ? " (decision " + state.Pending.Prompt + ")" : "") + ".");
                        result.Problems.AddRange(reasons);
                        break;
                    }
                    result.Commands++;
                    options.OnCommand?.Invoke(state, last!);
                    Report(options, state, accepted.Events);
                    problems = monitor.After(last!, accepted);
                    problems.AddRange(Invariants.Check(content, state));
                    if (options.SaveCheckEvery > 0 && result.Commands % options.SaveCheckEvery == 0)
                    {
                        problems.AddRange(SaveRoundTrip(state));
                    }
                    if (Stop(result, problems, Where(state, result) + " after " + last!.GetType().Name)) return result;
                }
                if (state.Phase == GamePhase.Ended)
                {
                    result.Outcome = SimulationOutcome.Won;
                    result.WinnerId = state.WinnerId;
                }
                result.Rounds = state.Round;
            }
            catch (Exception e)
            {
                result.Outcome = SimulationOutcome.Crashed;
                if (result.FinalState != null) result.Rounds = result.FinalState.Round;
                result.Problems.Add((last == null ? "During setup" : "After " + last.GetType().Name) + ": " + e);
            }
            return result;
        }

        /// <summary>
        /// The table for a seed: player count, distinct characters (R-3.11.a) and starter ship side
        /// (R-3.11.e) drawn from the bot's generator, so the game seed is left for the game.
        /// </summary>
        public static GameSetup Setup(GameContent content, SimulationOptions options, GameRandom rng)
        {
            int count = options.Players > 0 ? options.Players : 1 + rng.NextInt(GameEngine.MaxPlayers);
            var characters = new List<string>();
            foreach (var id in content.CardIds())
            {
                if ((string?)content.Card(id)["kind"] == "character" && content.HasCharacter(id)) characters.Add(id);
            }
            characters.Sort(StringComparer.Ordinal);
            rng.Shuffle(characters);
            var starters = new List<string>();
            foreach (var sheet in content.ShipSheetIds())
            {
                if (content.ShipSide(sheet).Role == "starter")
                {
                    starters.Add(sheet);
                    starters.Add(content.OtherShipSide(sheet).Id);
                }
            }
            var players = new List<PlayerSetup>();
            for (int i = 0; i < count; i++)
            {
                players.Add(new PlayerSetup("p" + (i + 1), "Bot " + (i + 1), Colors[i], "", starters[rng.NextInt(starters.Count)], characters[i]));
            }
            return new GameSetup(options.Seed, players, new[] { new PackRef("base", "0.1.0") }) { FameRequirement = options.FameRequirement };
        }

        private static List<string> SaveRoundTrip(GameState state)
        {
            var text = SaveSerializer.Save(state);
            var again = SaveSerializer.Save(SaveSerializer.Load(text));
            return again == text ? new List<string>() : new List<string> { "DECISIONS §6: the state changed through save and load." };
        }

        private static void Report(SimulationOptions options, GameState state, IReadOnlyList<GameEvent> events)
        {
            if (options.OnEvent == null) return;
            foreach (var e in events) options.OnEvent(state, e);
        }

        private static string Where(GameState state, SimulationResult result)
        {
            return "round " + state.Round + ", " + state.CurrentPlayerId + " " + state.Step + ", command " + (result.Commands + 1);
        }

        private static bool Stop(SimulationResult result, List<string> problems, string where)
        {
            if (problems.Count == 0) return false;
            result.Outcome = SimulationOutcome.Invariant;
            foreach (var p in problems) result.Problems.Add(where + ": " + p);
            if (result.FinalState != null) result.Rounds = result.FinalState.Round;
            return true;
        }
    }
}
