using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Persistence;
using OuterRim.Core.Simulation;
using Xunit;

namespace OuterRim.Core.Tests
{
    /// <summary>
    /// V0 (PLAN T3.1): seeded games between random bots, with the invariants of RULES §26.3 checked after
    /// every command. The full run of 1,000 games is the terminal tool (tools/OuterRim.Terminal); this keeps
    /// a smaller batch in the test suite.
    /// </summary>
    public class SimulationTests
    {
        private static readonly Content.GameContent C = TestContent.Base;

        private static string Report(SimulationResult r)
        {
            return "seed " + r.Seed + ": " + r.Outcome + " " + string.Join(" | ", r.Problems);
        }

        [Fact]
        public void R26_3_SeededGames_FinishWithAWinner_WithoutBreakingAnyInvariant()
        {
            var results = new SimulationResult[16];
            Parallel.For(0, results.Length, i =>
            {
                results[i] = GameRunner.Run(C, new SimulationOptions { Seed = (ulong)(1 + i), MaxRounds = 1000 });
            });
            foreach (var r in results)
            {
                Assert.True(r.Outcome == SimulationOutcome.Won, Report(r));
            }
            // Every player count is played (the count comes from the seed).
            Assert.Equal(4, results.Select(r => r.Characters.Count).Distinct().Count());
        }

        [Fact]
        public void TheSameSeed_PlaysTheSameGame()
        {
            var options = new SimulationOptions { Seed = 77, MaxRounds = 60 };
            var a = GameRunner.Run(C, options);
            var b = GameRunner.Run(C, options);
            Assert.True(a.Sound, Report(a));
            Assert.Equal(a.Commands, b.Commands);
            Assert.Equal(a.Rejections, b.Rejections);
            Assert.Equal(SaveSerializer.Save(a.FinalState!), SaveSerializer.Save(b.FinalState!));
        }

        [Fact]
        public void ARejectedCommand_ChangesNothing()
        {
            foreach (ulong seed in new ulong[] { 3, 4 })
            {
                var r = GameRunner.Run(C, new SimulationOptions { Seed = seed, MaxRounds = 25, CheckRejections = true, SaveCheckEvery = 1 });
                Assert.True(r.Sound, Report(r));
                Assert.True(r.Rejections > 0);
            }
        }

        [Fact]
        public void ThePlayerCount_CanBeFixed()
        {
            var r = GameRunner.Run(C, new SimulationOptions { Seed = 9, Players = 1, MaxRounds = 30 });
            Assert.True(r.Sound, Report(r));
            Assert.Single(r.Characters);
        }

        // ---- the checker itself ---------------------------------------------------------------------

        private static GameState NewGame()
        {
            var options = new SimulationOptions { Seed = 5, Players = 2 };
            var setup = GameRunner.Setup(C, options, new RandomBot(5).Random);
            var s = GameEngine.NewGame(C, setup, out _);
            while (s.Pending != null)
            {
                GameEngine.Execute(C, s, new ChooseOptionCommand(s.Pending.PlayerId, s.Pending.Id, s.Pending.Options[0].Id));
            }
            return s;
        }

        [Fact]
        public void TheChecker_PassesANewGame()
        {
            Assert.Empty(Invariants.Check(C, NewGame()));
        }

        [Fact]
        public void R8_4_R18_14_R9_8_TheChecker_FindsDamageAboveHealth_NegativeFame_NegativeCredits()
        {
            var s = NewGame();
            s.Players[0].CharacterDamage = 99;
            s.Players[0].Fame = -1;
            s.Players[1].Credits = -5;
            var errors = Invariants.Check(C, s);
            Assert.Contains(errors, e => e.StartsWith("R-8.4"));
            Assert.Contains(errors, e => e.StartsWith("R-18.14"));
            Assert.Contains(errors, e => e.StartsWith("R-9.8"));
        }

        [Fact]
        public void R11_9_TheChecker_FindsAMissingOrDoubledPatrol()
        {
            var s = NewGame();
            s.Patrols.Add(new PatrolState { Faction = Faction.Hutt, Level = 1, SpaceId = s.Patrols[0].SpaceId });
            Assert.Contains(Invariants.Check(C, s), e => e.StartsWith("R-11.9") && e.Contains("2 Hutt"));
            s.Patrols.RemoveAll(p => p.Faction == Faction.Rebel);
            Assert.Contains(Invariants.Check(C, s), e => e.StartsWith("R-11.9") && e.Contains("0 Rebel"));
        }

        [Fact]
        public void R14_18_TheChecker_FindsAssetsWithoutSlots_AndACardInTwoPlaces()
        {
            var s = NewGame();
            var p = s.Players[0];
            for (int i = 0; i < 6; i++) p.Assets.Add(new AssetState { CardId = "test-crew-" + i, Kind = "crew" });
            p.Assets.Add(new AssetState { CardId = s.MarketDecks[0].Cards[0], Kind = "gear" });
            var errors = Invariants.Check(C, s);
            Assert.Contains(errors, e => e.StartsWith("R-14.18"));
            Assert.Contains(errors, e => e.Contains("is both in"));
        }

        [Fact]
        public void R4_12_R4_9_TheMonitor_FindsASecondEncounter_AndASecondDelivery()
        {
            var monitor = new InvariantMonitor();
            var ok = CommandResult.Ok(new GameEvent[0]);
            monitor.Start(new GameEvent[] { new TurnStartedEvent("p1", 1), new StepStartedEvent("p1", TurnStep.Action) });
            Assert.Empty(monitor.After(new DeliverCommand("p1", new[] { "a" }), ok));
            Assert.Contains(monitor.After(new DeliverCommand("p1", new[] { "b" }), ok), e => e.StartsWith("R-4.9"));
            monitor.After(new EndActionStepCommand("p1"), CommandResult.Ok(new GameEvent[] { new StepStartedEvent("p1", TurnStep.Encounter) }));
            Assert.Empty(monitor.After(new EncounterCommand("p1", EncounterKind.Space), ok));
            Assert.Contains(monitor.After(new EncounterCommand("p1", EncounterKind.Space), ok), e => e.StartsWith("R-4.12"));
        }

        [Fact]
        public void R4_12_TheMonitor_FindsATurnEndingInTheEncounterStep_WithNoEncounter()
        {
            var monitor = new InvariantMonitor();
            monitor.Start(new GameEvent[] { new TurnStartedEvent("p1", 1), new StepStartedEvent("p1", TurnStep.Encounter) });
            var errors = monitor.After(new EndActionStepCommand("p1"), CommandResult.Ok(new GameEvent[] { new TurnEndedEvent("p1") }));
            Assert.Contains(errors, e => e.StartsWith("R-4.12"));
        }

        [Fact]
        public void R8_8_TheMonitor_AcceptsATurnCutShort_BeforeTheEncounterStep()
        {
            // A defeat or a card ("end your turn", databank #1) can end the turn in the action step.
            var monitor = new InvariantMonitor();
            monitor.Start(new GameEvent[] { new TurnStartedEvent("p1", 1), new StepStartedEvent("p1", TurnStep.Action) });
            Assert.Empty(monitor.After(new DeliverCommand("p1", new[] { "a" }),
                CommandResult.Ok(new GameEvent[] { new TurnEndedEvent("p1"), new TurnStartedEvent("p2", 1) })));
        }

        [Fact]
        public void TheBot_OffersOnlyAnswers_WhileADecisionIsPending()
        {
            var bot = new RandomBot(1);
            var s = NewGame();
            GameEngine.Execute(C, s, new MoveCommand(s.CurrentPlayerId, new string[0]));
            Assert.Contains(bot.Candidates(C, s), c => c is EndActionStepCommand);
            s.Pending = new PendingDecision { Id = 99, Kind = DecisionKind.ChooseOption, PlayerId = s.Players[1].Id,
                Options = new List<DecisionOption> { new DecisionOption("a", "a"), new DecisionOption("b", "b") } };
            var answers = bot.Candidates(C, s);
            // The answers, plus now and then a credit request, which may interrupt a decision (R-16.16).
            Assert.Equal(2, answers.OfType<ChooseOptionCommand>().Count());
            Assert.All(answers, c => Assert.True(c is ChooseOptionCommand || c is RequestCreditsCommand));
            Assert.All(answers, c => Assert.Equal(s.Players[1].Id, c.PlayerId));
        }

        [Fact]
        public void R9_6_TheBot_AnswersAnOpenCreditRequest_BeforeAnythingElse()
        {
            var bot = new RandomBot(1);
            var s = NewGame();
            var other = s.Players.First(p => p.Id != s.CurrentPlayerId).Id;
            s.FindPlayer(other)!.Credits = 5000;
            Assert.True(GameEngine.Execute(C, s, new RequestCreditsCommand(s.CurrentPlayerId, other, CreditRequestKind.Ask, 2000)).Accepted);
            Assert.Equal(other, RandomBot.Actor(s));
            var candidates = bot.Candidates(C, s);
            Assert.Equal(2, candidates.OfType<AnswerCreditRequestCommand>().Count(c => c.PlayerId == other));
            Assert.All(candidates, c => Assert.True(c is AnswerCreditRequestCommand || c is CancelCreditRequestCommand));
        }
    }
}
