using System.Collections.Generic;
using System.Linq;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Persistence;
using Xunit;

namespace OuterRim.Core.Tests
{
    /// <summary>Scripted handlers that exercise pausing, nesting and the three decision kinds.</summary>
    internal static class TestHandlers
    {
        public static void Register()
        {
            ResolutionHandlers.Register(new RewardChoice());
            ResolutionHandlers.Register(new TieParent());
            ResolutionHandlers.Register(new TieChild());
            ResolutionHandlers.Register(new OtherPlayerChoice());
            ResolutionHandlers.Register(new OrderChoice());
            ResolutionHandlers.Register(new TwoChoices());
        }

        private static readonly DecisionOption[] Rewards =
        {
            new DecisionOption("small", "test.reward.small"),
            new DecisionOption("big", "test.reward.big"),
        };

        /// <summary>"Either gain 1,000 or gain 3,000": one pause, then the effect.</summary>
        public sealed class RewardChoice : IResolutionHandler
        {
            public string Id => "test.reward_choice";

            public StepResult Step(ResolutionContext ctx, Frame frame)
            {
                switch (frame.Step)
                {
                    case 0:
                        frame.Step = 1;
                        return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, ctx.State.CurrentPlayerId,
                            "test.reward", Rewards, "pick"));
                    default:
                        ctx.State.FindPlayer(ctx.State.CurrentPlayerId)!.Credits += frame.Get("pick") == "big" ? 3000 : 1000;
                        return StepResult.Done();
                }
            }
        }

        /// <summary>Calls a child that asks for a tie-break, then uses the value it returned.</summary>
        public sealed class TieParent : IResolutionHandler
        {
            public string Id => "test.tie_parent";

            public StepResult Step(ResolutionContext ctx, Frame frame)
            {
                switch (frame.Step)
                {
                    case 0:
                        frame.Step = 1;
                        var child = new Frame("test.tie_child");
                        child.Set("candidates", frame.Get("candidates"));
                        return StepResult.Call(child);
                    default:
                        ctx.State.FindPlayer(frame.Get("ret.winner"))!.Credits += 500;
                        return StepResult.Done();
                }
            }
        }

        /// <summary>R-5.30: tied "nearest" candidates; the current player picks.</summary>
        public sealed class TieChild : IResolutionHandler
        {
            public string Id => "test.tie_child";

            public StepResult Step(ResolutionContext ctx, Frame frame)
            {
                switch (frame.Step)
                {
                    case 0:
                        frame.Step = 1;
                        var options = frame.Get("candidates").Split(',').Select(id => new DecisionOption(id, id));
                        return StepResult.Await(ctx.AskTie("test.nearest", options, "winner"));
                    default:
                        ctx.Return("winner", frame.Get("winner"));
                        return StepResult.Done();
                }
            }
        }

        /// <summary>"That player chooses": the decision belongs to a player who is not the current one.</summary>
        public sealed class OtherPlayerChoice : IResolutionHandler
        {
            public string Id => "test.other_player_choice";

            public StepResult Step(ResolutionContext ctx, Frame frame)
            {
                switch (frame.Step)
                {
                    case 0:
                        frame.Step = 1;
                        return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, frame.Get("chooser"), "test.join",
                            new[] { new DecisionOption("yes", "yes"), new DecisionOption("no", "no") }, "join"));
                    default:
                        if (frame.Get("join") == "yes")
                        {
                            ctx.State.FindPlayer(frame.Get("chooser"))!.Fame += 1;
                        }
                        return StepResult.Done();
                }
            }
        }

        /// <summary>R-21.10: three simultaneous abilities; the current player orders them.</summary>
        public sealed class OrderChoice : IResolutionHandler
        {
            public string Id => "test.order";

            public StepResult Step(ResolutionContext ctx, Frame frame)
            {
                switch (frame.Step)
                {
                    case 0:
                        frame.Step = 1;
                        return StepResult.Await(ctx.AskOrder("test.order",
                            new[] { new DecisionOption("a", "a"), new DecisionOption("b", "b"), new DecisionOption("c", "c") },
                            "order"));
                    default:
                        return StepResult.Done();
                }
            }
        }

        /// <summary>Two decisions in a row, so a save can fall between them.</summary>
        public sealed class TwoChoices : IResolutionHandler
        {
            public string Id => "test.two_choices";

            public StepResult Step(ResolutionContext ctx, Frame frame)
            {
                var player = ctx.State.FindPlayer(ctx.State.CurrentPlayerId)!;
                switch (frame.Step)
                {
                    case 0:
                        frame.Step = 1;
                        return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, player.Id, "test.first", Rewards, "first"));
                    case 1:
                        player.Credits += frame.Get("first") == "big" ? 3000 : 1000;
                        frame.Step = 2;
                        return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, player.Id, "test.second", Rewards, "second"));
                    default:
                        player.Credits += frame.Get("second") == "big" ? 3000 : 1000;
                        return StepResult.Done();
                }
            }
        }
    }

    public class PendingDecisionTests
    {
        public PendingDecisionTests()
        {
            TestHandlers.Register();
        }

        private static GameState NewGame()
        {
            return GameEngine.NewGame(GameEngineTests.Setup(77, 3), out _);
        }

        private static PlayerState Current(GameState s)
        {
            return s.FindPlayer(s.CurrentPlayerId)!;
        }

        private static string NotCurrent(GameState s)
        {
            return s.Players.First(p => p.Id != s.CurrentPlayerId).Id;
        }

        [Fact]
        public void ScriptedEffect_PausesForADecision_ThenResumes()
        {
            var s = NewGame();
            int before = Current(s).Credits;
            var start = GameEngine.StartResolution(s, new Frame("test.reward_choice"));
            Assert.True(start.Accepted);
            var asked = Assert.IsType<DecisionRequestedEvent>(start.Events.Single());
            Assert.Equal(s.CurrentPlayerId, asked.Decision.PlayerId);
            Assert.NotNull(s.Pending);
            Assert.Equal(before, Current(s).Credits);

            var answer = GameEngine.Execute(s, new ChooseOptionCommand(s.CurrentPlayerId, asked.Decision.Id, "big"));
            Assert.True(answer.Accepted);
            Assert.Equal(before + 3000, Current(s).Credits);
            Assert.Null(s.Pending);
            Assert.Empty(s.Stack);
        }

        [Fact]
        public void WhileWaiting_OtherCommandsAreRejected_AndChangeNothing()
        {
            var s = NewGame();
            GameEngine.StartResolution(s, new Frame("test.reward_choice"));
            var before = SaveSerializer.Save(s);
            var r = GameEngine.Execute(s, new GainCreditsCommand(s.CurrentPlayerId));
            Assert.False(r.Accepted);
            Assert.Contains("Waiting for decision", r.Reason);
            Assert.Equal(before, SaveSerializer.Save(s));
        }

        [Fact]
        public void R21_12_ANewResolution_CannotStartWhileOneIsInProgress()
        {
            var s = NewGame();
            GameEngine.StartResolution(s, new Frame("test.reward_choice"));
            var r = GameEngine.StartResolution(s, new Frame("test.reward_choice"));
            Assert.False(r.Accepted);
            Assert.Contains("R-21.12", r.Reason);
        }

        [Fact]
        public void InvalidAnswers_AreRejected_AndChangeNothing()
        {
            var s = NewGame();
            GameEngine.StartResolution(s, new Frame("test.reward_choice"));
            int id = s.Pending!.Id;
            var before = SaveSerializer.Save(s);
            Assert.Contains("is for", GameEngine.Execute(s, new ChooseOptionCommand(NotCurrent(s), id, "big")).Reason);
            Assert.Contains("not the pending one", GameEngine.Execute(s, new ChooseOptionCommand(s.CurrentPlayerId, id + 1, "big")).Reason);
            Assert.Contains("not an option", GameEngine.Execute(s, new ChooseOptionCommand(s.CurrentPlayerId, id, "huge")).Reason);
            Assert.Contains("needs a ChooseOption answer", GameEngine.Execute(s, new ResolveTieCommand(s.CurrentPlayerId, id, "big")).Reason);
            Assert.Equal(before, SaveSerializer.Save(s));
        }

        [Fact]
        public void AnAnswer_WithNothingPending_IsRejected()
        {
            var s = NewGame();
            Assert.Contains("No decision is pending", GameEngine.Execute(s, new ChooseOptionCommand(s.CurrentPlayerId, 1, "big")).Reason);
        }

        [Fact]
        public void R15_35_ADecisionForAnotherPlayer_IsAnsweredByThatPlayer()
        {
            var s = NewGame();
            var other = NotCurrent(s);
            var frame = new Frame("test.other_player_choice");
            frame.Set("chooser", other);
            GameEngine.StartResolution(s, frame);
            Assert.Equal(other, s.Pending!.PlayerId);
            Assert.False(GameEngine.Execute(s, new ChooseOptionCommand(s.CurrentPlayerId, s.Pending.Id, "yes")).Accepted);
            Assert.True(GameEngine.Execute(s, new ChooseOptionCommand(other, s.Pending.Id, "yes")).Accepted);
            Assert.Equal(1, s.FindPlayer(other)!.Fame);
        }

        [Fact]
        public void R5_30_R21_13_TieBreak_GoesToTheCurrentPlayer_AndReturnsToTheCaller()
        {
            var s = NewGame();
            var others = s.Players.Where(p => p.Id != s.CurrentPlayerId).Select(p => p.Id).ToList();
            var frame = new Frame("test.tie_parent");
            frame.Set("candidates", string.Join(",", others));
            GameEngine.StartResolution(s, frame);
            Assert.Equal(DecisionKind.ResolveTie, s.Pending!.Kind);
            Assert.Equal(s.CurrentPlayerId, s.Pending.PlayerId);
            Assert.Equal(2, s.Stack.Count);
            int before = s.FindPlayer(others[1])!.Credits;
            Assert.True(GameEngine.Execute(s, new ResolveTieCommand(s.CurrentPlayerId, s.Pending.Id, others[1])).Accepted);
            Assert.Equal(before + 500, s.FindPlayer(others[1])!.Credits);
            Assert.Empty(s.Stack);
        }

        [Fact]
        public void R21_10_Order_MustListEveryOptionOnce()
        {
            var s = NewGame();
            GameEngine.StartResolution(s, new Frame("test.order"));
            int id = s.Pending!.Id;
            Assert.Contains("R-21.10", GameEngine.Execute(s, new ChooseOrderCommand(s.CurrentPlayerId, id, new[] { "a", "b" })).Reason);
            Assert.Contains("R-21.10", GameEngine.Execute(s, new ChooseOrderCommand(s.CurrentPlayerId, id, new[] { "a", "a", "b" })).Reason);
            var ok = GameEngine.Execute(s, new ChooseOrderCommand(s.CurrentPlayerId, id, new[] { "c", "a", "b" }));
            Assert.True(ok.Accepted);
            Assert.Equal("c,a,b", ok.Events.OfType<DecisionAnsweredEvent>().Single().Answer);
        }

        [Fact]
        public void DecisionIds_NeverRepeat()
        {
            var s = NewGame();
            GameEngine.StartResolution(s, new Frame("test.two_choices"));
            int first = s.Pending!.Id;
            GameEngine.Execute(s, new ChooseOptionCommand(s.CurrentPlayerId, first, "small"));
            Assert.Equal(first + 1, s.Pending!.Id);
        }

        [Fact]
        public void APausedGame_SurvivesSaveAndLoad_AndEndsTheSame()
        {
            string Finish(GameState s)
            {
                GameEngine.Execute(s, new ChooseOptionCommand(s.CurrentPlayerId, s.Pending!.Id, "big"));
                return SaveSerializer.Save(s);
            }

            var straight = NewGame();
            GameEngine.StartResolution(straight, new Frame("test.two_choices"));
            GameEngine.Execute(straight, new ChooseOptionCommand(straight.CurrentPlayerId, straight.Pending!.Id, "small"));

            var reloaded = SaveSerializer.Load(SaveSerializer.Save(straight));
            Assert.NotNull(reloaded.Pending);
            Assert.Single(reloaded.Stack);
            Assert.Equal(2, reloaded.Stack[0].Step);

            Assert.Equal(Finish(straight), Finish(reloaded));
        }

        [Fact]
        public void UnknownHandler_IsAnError()
        {
            var s = NewGame();
            Assert.Throws<System.InvalidOperationException>(() => GameEngine.StartResolution(s, new Frame("no.such.handler")));
        }
    }
}
