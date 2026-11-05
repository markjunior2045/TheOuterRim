using System.Collections.Generic;
using System.Linq;
using OuterRim.Core.Dice;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Persistence;
using OuterRim.Core.Random;
using OuterRim.Core.Rules;
using Xunit;

namespace OuterRim.Core.Tests
{
    /// <summary>A card effect that calls a skill test: "Test X. If you pass, gain 1,000 credits."</summary>
    internal sealed class CallSkillTest : IResolutionHandler
    {
        public string Id => "test.call_skill_test";

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            switch (frame.Step)
            {
                case 0:
                    frame.Step = 1;
                    var test = new Frame(SkillTests.Handler);
                    test.Set("player", ctx.State.CurrentPlayerId);
                    test.Set("skills", frame.Get("skills"));
                    return StepResult.Call(test);
                default:
                    if (frame.Get("ret.passed") == "true")
                    {
                        ctx.State.FindPlayer(ctx.State.CurrentPlayerId)!.Credits += 1000;
                    }
                    return StepResult.Done();
            }
        }
    }

    public class SkillTestTests
    {
        private static readonly Content.GameContent C = TestContent.Base;
        private const string Ketsu = "base-character-00";   // stealth, strength
        private const string Lando = "base-character-01";   // influence, knowledge, piloting

        public SkillTestTests()
        {
            ResolutionHandlers.Register(new CallSkillTest());
            C.AddCrew("test-crew-stealth", new[] { Skill.Stealth });
            C.AddCrew("test-crew-tech-stealth", new[] { Skill.Tech, Skill.Stealth });
        }

        private static GameState Game(string character = Ketsu, ulong seed = 31)
        {
            return GameEngine.NewGame(C, new GameSetup(seed, new[]
            {
                new PlayerSetup("p1", "One", "green", "kessel", TestContent.G9Rigger, character),
            }, new[] { new PackRef("base", "0.1.0") }), out _);
        }

        // ---- R-6.9 thresholds -----------------------------------------------------------

        [Theory]
        [InlineData(SkillLevel.Unskilled, new[] { DieFace.Crit, DieFace.Blank }, true)]
        [InlineData(SkillLevel.Unskilled, new[] { DieFace.Hit, DieFace.Hit }, false)]
        [InlineData(SkillLevel.Unskilled, new[] { DieFace.Focus, DieFace.Blank }, false)]
        [InlineData(SkillLevel.Skilled, new[] { DieFace.Hit, DieFace.Blank }, true)]
        [InlineData(SkillLevel.Skilled, new[] { DieFace.Blank, DieFace.Crit }, true)]
        [InlineData(SkillLevel.Skilled, new[] { DieFace.Focus, DieFace.Focus }, false)]
        [InlineData(SkillLevel.HighlySkilled, new[] { DieFace.Focus, DieFace.Blank }, true)]
        [InlineData(SkillLevel.HighlySkilled, new[] { DieFace.Blank, DieFace.Blank }, false)]
        public void R6_9_PassThresholds(SkillLevel level, DieFace[] faces, bool passes)
        {
            Assert.Equal(passes, SkillTests.Passes(level, faces));
        }

        [Fact]
        public void R6_9_PassRates_OverManySeededRolls()
        {
            // Two dice: unskilled 1-(7/8)^2 = 23.4%, skilled 1-(4/8)^2 = 75%, highly skilled 1-(2/8)^2 = 93.75%.
            var rng = new GameRandom(2026);
            var counts = new int[3];
            const int n = 40000;
            for (int i = 0; i < n; i++)
            {
                var faces = DiceRoller.Roll(rng, SkillTests.Dice);
                for (int l = 0; l < 3; l++)
                {
                    if (SkillTests.Passes((SkillLevel)l, faces))
                    {
                        counts[l]++;
                    }
                }
            }
            Assert.InRange(counts[0] / (double)n, 0.224, 0.244);
            Assert.InRange(counts[1] / (double)n, 0.740, 0.760);
            Assert.InRange(counts[2] / (double)n, 0.932, 0.943);
        }

        [Fact]
        public void R6_2_ACrit_IsNotAHit()
        {
            var faces = new[] { DieFace.Crit, DieFace.Crit };
            Assert.Equal(0, DiceRoller.Count(faces, DieFace.Hit));
            Assert.Equal(2, DiceRoller.Count(faces, DieFace.Crit));
        }

        // ---- R-6.6 to R-6.8 instances ------------------------------------------------------

        [Fact]
        public void R6_6_R6_7_InstancesCount_CharacterAndCrew()
        {
            var s = Game(Ketsu);
            var p = s.Players[0];
            Assert.Equal(1, SkillTests.Instances(C, p, Skill.Stealth));
            Assert.Equal(0, SkillTests.Instances(C, p, Skill.Tech));
            p.Assets.Add(new AssetState { CardId = "test-crew-stealth", Kind = "crew" });
            p.Assets.Add(new AssetState { CardId = "test-crew-tech-stealth", Kind = "crew" });
            Assert.Equal(3, SkillTests.Instances(C, p, Skill.Stealth));
            Assert.Equal(1, SkillTests.Instances(C, p, Skill.Tech));
            Assert.Equal(SkillLevel.HighlySkilled, SkillTests.Level(3));
        }

        [Fact]
        public void R6_5_LosingACrew_LosesItsSkill()
        {
            var s = Game(Ketsu);
            var p = s.Players[0];
            p.Assets.Add(new AssetState { CardId = "test-crew-tech-stealth", Kind = "crew" });
            Assert.Equal(1, SkillTests.Instances(C, p, Skill.Tech));
            p.Assets.Clear();
            Assert.Equal(0, SkillTests.Instances(C, p, Skill.Tech));
            Assert.Equal(1, SkillTests.Instances(C, p, Skill.Stealth));
        }

        [Fact]
        public void R6_8_AGrantedSkill_AddsAnInstance_EvenIfAlreadyHeld()
        {
            var p = Game(Ketsu).Players[0];
            Assert.Equal(2, SkillTests.Instances(C, p, Skill.Stealth, granted: 1));
            Assert.Equal(1, SkillTests.Instances(C, p, Skill.Tech, granted: 1));
        }

        [Fact]
        public void CharacterSkills_ComeFromTheCardData()
        {
            Assert.Equal(new HashSet<Skill> { Skill.Influence, Skill.Knowledge, Skill.Piloting }, C.Character(Lando).Skills);
            Assert.Equal(3, C.Character(Ketsu).GroundCombat);
            Assert.Equal(4, C.Character(Ketsu).Health);
        }

        // ---- the test as a resolution ---------------------------------------------------------

        [Fact]
        public void R6_10_R6_11_ACardCallsATest_AndActsOnTheResult()
        {
            int passed = 0, failed = 0;
            for (ulong seed = 0; seed < 60; seed++)
            {
                var s = Game(Ketsu, seed);
                int before = s.Players[0].Credits;
                var frame = new Frame("test.call_skill_test");
                frame.Set("skills", "tech"); // R-6.10: Ketsu can test a skill she does not have
                var r = GameEngine.StartResolution(C, s, frame);
                var rolled = r.Events.OfType<SkillTestRolledEvent>().Single();
                Assert.Equal(0, rolled.Instances);
                Assert.Equal(SkillTests.Passes(SkillLevel.Unskilled, rolled.Faces), rolled.Passed);
                Assert.Equal(before + (rolled.Passed ? 1000 : 0), s.Players[0].Credits);
                Assert.Empty(s.Stack);
                if (rolled.Passed) passed++; else failed++;
            }
            Assert.True(passed > 0 && failed > 0);
        }

        [Fact]
        public void TestXOrY_ThePlayerChoosesTheSkill_AndTheGameCanBeSavedMeanwhile()
        {
            var s = Game(Lando, 5);
            var frame = new Frame("test.call_skill_test");
            frame.Set("skills", "strength,piloting");
            GameEngine.StartResolution(C, s, frame);
            Assert.Equal(DecisionKind.ChooseOption, s.Pending!.Kind);
            Assert.Equal(new[] { "strength", "piloting" }, s.Pending.Options.Select(o => o.Id));

            var copy = SaveSerializer.Load(SaveSerializer.Save(s));
            var a = GameEngine.Execute(C, s, new ChooseOptionCommand("p1", s.Pending.Id, "piloting"));
            var b = GameEngine.Execute(C, copy, new ChooseOptionCommand("p1", copy.Pending!.Id, "piloting"));
            var rolled = a.Events.OfType<SkillTestRolledEvent>().Single();
            Assert.Equal("piloting", rolled.Skill);
            Assert.Equal(1, rolled.Instances);
            Assert.Equal(SaveSerializer.Save(s), SaveSerializer.Save(copy));
            Assert.Equal(rolled.Faces, b.Events.OfType<SkillTestRolledEvent>().Single().Faces);
        }
    }
}
