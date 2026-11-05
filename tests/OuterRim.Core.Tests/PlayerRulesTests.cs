using System.Collections.Generic;
using System.Linq;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;
using Xunit;

namespace OuterRim.Core.Tests
{
    /// <summary>A card effect: "your character suffers N damage" (frame var "amount").</summary>
    internal sealed class HurtCurrent : IResolutionHandler
    {
        public string Id => "test.hurt_current";

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var p = ctx.State.FindPlayer(ctx.State.CurrentPlayerId)!;
            PlayerRules.SufferDamage(ctx.Content, ctx.State, p, CombatType.Ground, frame.GetInt("amount"), ctx.Events);
            return StepResult.Done();
        }
    }

    /// <summary>A card effect: every player listed in "players" gains "amount" fame at once.</summary>
    internal sealed class GainFameTogether : IResolutionHandler
    {
        public string Id => "test.gain_fame_together";

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            foreach (var id in frame.Get("players").Split(','))
            {
                PlayerRules.GainFame(ctx.State.FindPlayer(id)!, frame.GetInt("amount"), ctx.Events);
            }
            return StepResult.Done();
        }
    }

    public class PlayerRulesTests
    {
        private static readonly Content.GameContent C = TestContent.Base;

        public PlayerRulesTests()
        {
            ResolutionHandlers.Register(new HurtCurrent());
            ResolutionHandlers.Register(new GainFameTogether());
        }

        private static GameState Game(int players = 3, ulong seed = 41)
        {
            var colors = new[] { "green", "red", "yellow", "white" };
            var list = new List<PlayerSetup>();
            for (int i = 0; i < players; i++)
            {
                list.Add(new PlayerSetup("p" + (i + 1), "P" + (i + 1), colors[i], "kessel", TestContent.G9Rigger, "base-character-00"));
            }
            return GameEngine.NewGame(C, new GameSetup(seed, list, new[] { new PackRef("base", "0.1.0") }), out _);
        }

        private static PlayerState Current(GameState s)
        {
            return s.FindPlayer(s.CurrentPlayerId)!;
        }

        // ---- credits -------------------------------------------------------------------------

        [Fact]
        public void R9_8_Spending_NeedsTheFullAmount()
        {
            var p = new PlayerState { Credits = 2000 };
            var events = new List<GameEvent>();
            Assert.False(PlayerRules.TrySpend(p, 3000, events));
            Assert.Equal(2000, p.Credits);
            Assert.True(PlayerRules.TrySpend(p, 2000, events));
            Assert.Equal(0, p.Credits);
        }

        [Fact]
        public void R9_8_Losing_TakesWhatThePlayerHas()
        {
            var p = new PlayerState { Credits = 2000 };
            Assert.Equal(2000, PlayerRules.LoseCredits(p, 3000, new List<GameEvent>()));
            Assert.Equal(0, p.Credits);
        }

        // ---- reputation ---------------------------------------------------------------------------

        [Fact]
        public void R10_5_GainingAtPositive_DoesNothing()
        {
            var p = new PlayerState();
            var events = new List<GameEvent>();
            PlayerRules.GainReputation(p, Faction.Hutt, 1, events);
            Assert.Equal(Reputation.Positive, p.Reputation.Hutt);
            PlayerRules.GainReputation(p, Faction.Hutt, 2, events);
            Assert.Equal(Reputation.Positive, p.Reputation.Hutt);
            Assert.Single(events);
        }

        [Fact]
        public void R10_6_LosingAtNegative_DoesNothing()
        {
            var p = new PlayerState();
            var events = new List<GameEvent>();
            PlayerRules.LoseReputation(p, Faction.Rebel, 2, events);
            Assert.Equal(Reputation.Negative, p.Reputation.Rebel);
            PlayerRules.LoseReputation(p, Faction.Rebel, 1, events);
            Assert.Equal(Reputation.Negative, p.Reputation.Rebel);
            Assert.Single(events);
        }

        [Fact]
        public void R10_7_SettingReputation_JumpsStraightToTheValue()
        {
            var p = new PlayerState();
            p.Reputation.Imperial = Reputation.Negative;
            PlayerRules.SetReputation(p, Faction.Imperial, Reputation.Positive, new List<GameEvent>());
            Assert.Equal(Reputation.Positive, p.Reputation.Imperial);
        }

        // ---- fame ------------------------------------------------------------------------------------

        [Fact]
        public void R18_14_FameNeverGoesBelowZero()
        {
            var p = new PlayerState { Fame = 1 };
            Assert.Equal(1, PlayerRules.LoseFame(p, 3, new List<GameEvent>()));
            Assert.Equal(0, p.Fame);
            Assert.Equal(0, PlayerRules.LoseFame(p, 1, new List<GameEvent>()));
        }

        [Fact]
        public void R18_17_TiedPlayers_AreAllTheMostFamous()
        {
            var s = Game();
            Assert.All(s.Players, p => Assert.True(PlayerRules.IsMostFamous(C, s, p))); // everyone at the start
            s.Players[0].Fame = 3;
            s.Players[1].Fame = 3;
            Assert.True(PlayerRules.IsMostFamous(C, s, s.Players[0]));
            Assert.True(PlayerRules.IsMostFamous(C, s, s.Players[1]));
            Assert.False(PlayerRules.IsMostFamous(C, s, s.Players[2]));
        }

        // ---- damage and defeat ---------------------------------------------------------------------

        [Fact]
        public void R8_8_BecomingDefeated_Costs3000_AndAllSecrets()
        {
            var s = Game();
            var p = Current(s);
            p.Credits = 5000;
            p.Secrets.AddRange(new[] { "base-encounter-navpoint-02", "base-encounter-ryloth-naboo-00" });
            var events = new List<GameEvent>();
            PlayerRules.SufferDamage(C, s, p, CombatType.Ground, 4, events);
            Assert.True(p.Defeated);
            Assert.Equal(2000, p.Credits);
            Assert.Empty(p.Secrets);
            Assert.Contains(events, e => e is PlayerDefeatedEvent);
            Assert.Equal(2, events.OfType<SecretsDiscardedEvent>().Single().Cards.Count);
        }

        [Fact]
        public void R8_8_WithLessThan3000_ThePlayerLosesAll()
        {
            var s = Game();
            var p = Current(s);
            p.Credits = 1000;
            PlayerRules.SufferDamage(C, s, p, CombatType.Ship, 9, new List<GameEvent>());
            Assert.Equal(0, p.Credits);
        }

        [Fact]
        public void R8_8_4_DefeatedInTheActionStep_TheTurnEndsAfterThatAction()
        {
            var s = Game();
            var first = s.CurrentPlayerId;
            GameEngine.Execute(C, s, new GainCreditsCommand(first));
            Assert.Equal(TurnStep.Action, s.Step);
            var hurt = new Frame("test.hurt_current");
            hurt.Set("amount", 4);
            var r = GameEngine.StartResolution(C, s, hurt);
            Assert.True(Current(s).Id != first);
            Assert.Equal(TurnStep.Planning, s.Step);
            Assert.Contains(r.Events, e => e is TurnEndedEvent);
            Assert.True(s.FindPlayer(first)!.Defeated);
        }

        [Fact]
        public void R8_8_4_ADefeatedPlayer_WhoIsNotCurrent_KeepsTheTurnGoing()
        {
            var s = Game();
            var other = s.Players.First(p => p.Id != s.CurrentPlayerId);
            PlayerRules.SufferDamage(C, s, other, CombatType.Ground, 4, new List<GameEvent>());
            Assert.True(other.Defeated);
            Assert.False(s.TurnEndsAfterStep);
        }

        [Fact]
        public void R8_10_RecoveringBelowTheLimit_EndsDefeat_EvenOutsidePlanning()
        {
            var s = Game();
            var p = Current(s);
            PlayerRules.SufferDamage(C, s, p, CombatType.Ground, 4, new List<GameEvent>());
            Assert.True(p.Defeated);
            var events = new List<GameEvent>();
            PlayerRules.RecoverDamage(C, s, p, CombatType.Ground, 1, events);
            Assert.False(p.Defeated);
            Assert.Equal(3, p.CharacterDamage);
            Assert.Contains(events, e => e is PlayerRecoveredEvent);
        }

        [Fact]
        public void R4_8_R8_9_ADefeatedPlayer_MustRecover_AtTheirNextPlanningStep()
        {
            var s = Game();
            var p = Current(s);
            PlayerRules.SufferDamage(C, s, p, CombatType.Ground, 4, new List<GameEvent>());
            Assert.Contains("R-4.8", GameEngine.Execute(C, s, new GainCreditsCommand(p.Id)).Reason);
            Assert.True(GameEngine.Execute(C, s, new RecoverDamageCommand(p.Id)).Accepted);
            Assert.False(p.Defeated);
            Assert.Equal(0, p.CharacterDamage);
        }

        // ---- victory --------------------------------------------------------------------------------------

        private static CommandResult FameFor(GameState s, string players, int amount)
        {
            var f = new Frame("test.gain_fame_together");
            f.Set("players", players);
            f.Set("amount", amount);
            return GameEngine.StartResolution(C, s, f);
        }

        [Fact]
        public void R18_8_ReachingTheFameRequirement_EndsTheGameAtOnce()
        {
            var s = Game();
            s.Players[1].Fame = 9;
            var r = FameFor(s, "p2", 1);
            Assert.Equal(GamePhase.Ended, s.Phase);
            Assert.Equal("p2", s.WinnerId);
            Assert.False(r.Events.OfType<GameWonEvent>().Single().Tiebreak);
            Assert.Contains("not in progress", GameEngine.Execute(C, s, new GainCreditsCommand(s.CurrentPlayerId)).Reason);
        }

        [Fact]
        public void R18_9_TheRequirementIsConfigurable()
        {
            var s = Game();
            s.FameRequirement = 12;
            s.Players[0].Fame = 10;
            FameFor(s, "p1", 1);
            Assert.Equal(GamePhase.Playing, s.Phase);
            FameFor(s, "p1", 1);
            Assert.Equal("p1", s.WinnerId);
        }

        [Fact]
        public void R18_16_SimultaneousWinners_MostCreditsWins()
        {
            var s = Game();
            s.Players[0].Fame = 9;
            s.Players[2].Fame = 9;
            s.Players[0].Credits = 4000;
            s.Players[2].Credits = 9000;
            var r = FameFor(s, "p1,p3", 1);
            Assert.Equal("p3", s.WinnerId);
            Assert.True(r.Events.OfType<GameWonEvent>().Single().Tiebreak);
        }

        [Fact]
        public void R18_16_StillTied_TheOneSeatedFarthestClockwiseFromTheFirstPlayerWins()
        {
            for (ulong seed = 0; seed < 20; seed++)
            {
                var s = Game(4, seed);
                foreach (var p in s.Players)
                {
                    p.Fame = 9;
                    p.Credits = 5000;
                }
                FameFor(s, "p1,p2,p3,p4", 1);
                int first = s.SeatOf(s.FirstPlayerId);
                var expected = s.Players[(first + 3) % 4].Id; // last in turn order
                Assert.Equal(expected, s.WinnerId);
            }
        }
    }
}
