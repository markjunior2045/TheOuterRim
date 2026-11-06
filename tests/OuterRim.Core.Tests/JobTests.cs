using System.Linq;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;
using Xunit;

namespace OuterRim.Core.Tests
{
    public class JobTests
    {
        private static readonly Content.GameContent C = TestContent.Base;
        private const string Statue = "base-market-luxury-06";   // Attend Statue Dedication: "complete this job", 1 fame
        private const string Casino = "base-market-job-02";      // The Casino Heist: #42, 15,000, on Cantonica
        private const string HuttFavor = "base-market-job-05";   // test Influence, else #41
        private const string LowStakes = "base-encounter-tatooine-nal-hutta-01-asset";
        private const string Wanted = "base-market-bounty-09";

        private static GameState Game(string space, ulong seed = 171, string character = "base-character-00")
        {
            var s = GameEngine.NewGame(C, new GameSetup(seed, new[]
            {
                new PlayerSetup("p1", "One", "green", "kessel", TestContent.G9Rigger, character),
                new PlayerSetup("p2", "Two", "red", "tatooine", TestContent.G9Rigger, "base-character-03"),
            }, new[] { new PackRef("base", "0.1.0") }), out _);
            s.CurrentPlayerId = "p1";
            P(s, "p1").SpaceId = space;
            foreach (var p in s.Patrols) p.SpaceId = "nav-tatooine-1";
            GameEngine.Execute(C, s, new MoveCommand("p1", new string[0]));
            GameEngine.Execute(C, s, new EndActionStepCommand("p1"));
            return s;
        }

        private static PlayerState P(GameState s, string id) => s.FindPlayer(id)!;

        private static void Hold(GameState s, string job, string player = "p1")
        {
            P(s, player).Assets.Add(new AssetState { CardId = job, Kind = (string)C.Card(job)["kind"]! });
            foreach (var d in s.MarketDecks) d.Cards.Remove(job);
        }

        private static CommandResult Attempt(GameState s, string job)
        {
            return GameEngine.Execute(C, s, new EncounterCommand("p1", EncounterKind.Job, job));
        }

        [Fact]
        public void R15_29_AJobIsAttemptedAtItsDestination()
        {
            var s = Game("lothal");
            Hold(s, Statue);
            Assert.Contains("R-15.29", Attempt(s, Statue).Reason); // Lothal is Imperial; no positive reputation
            P(s, "p1").Reputation.Imperial = Reputation.Positive;
            Assert.True(Attempt(s, Statue).Accepted);
        }

        [Fact]
        public void R15_37_CompletingAJob_GivesTheReward_AndRemovesTheCard()
        {
            var s = Game("lothal");
            P(s, "p1").Reputation.Imperial = Reputation.Positive;
            Hold(s, Statue);
            var r = TestHelpers.DeclineAll(C, s, Attempt(s, Statue));
            Assert.Equal(1, P(s, "p1").Fame);
            Assert.DoesNotContain(P(s, "p1").Assets, a => a.CardId == Statue);
            Assert.DoesNotContain(Statue, Market.Deck(s, "luxury").Cards); // removed from the game
            Assert.True(r.Events.OfType<JobCompletedEvent>().Single().Completed);
            Assert.Equal("p2", s.CurrentPlayerId); // R-4.16: it was the turn's encounter
        }

        [Fact]
        public void R15_36_R15_38_AProcedureRunsStepByStep_AndAFailedJobIsKept()
        {
            bool failed = false, completed = false;
            for (ulong seed = 0; seed < 120 && !(failed && completed); seed++)
            {
                var s = Game("cantonica", seed);
                Hold(s, Casino);
                int credits = P(s, "p1").Credits;
                var r = TestHelpers.DeclineAll(C, s, Attempt(s, Casino));
                Assert.Contains(r.Events, e => e is DatabankStepEvent d && d.CardId == "base-databank-042" && d.Step == 1);
                if (r.Events.OfType<JobFailedEvent>().Any())
                {
                    Assert.Contains(P(s, "p1").Assets, a => a.CardId == Casino);
                    failed = true;
                }
                else
                {
                    Assert.Equal(credits + 15000, P(s, "p1").Credits);
                    Assert.DoesNotContain(P(s, "p1").Assets, a => a.CardId == Casino);
                    completed = true;
                }
            }
            Assert.True(failed && completed);
        }

        [Fact]
        public void R15_38_FailingStep1OfTheCasinoHeist_EndsTheProcedure()
        {
            for (ulong seed = 0; seed < 120; seed++)
            {
                var s = Game("cantonica", seed);
                Hold(s, Casino);
                var r = TestHelpers.DeclineAll(C, s, Attempt(s, Casino));
                var first = r.Events.OfType<SkillTestRolledEvent>().First();
                if (first.Passed) continue;
                Assert.DoesNotContain(r.Events, e => e is DatabankStepEvent d && d.Step == 2);
                Assert.Contains(r.Events, e => e is JobFailedEvent);
                return;
            }
            Assert.Fail("No seed failed the first test.");
        }

        [Fact]
        public void FavorCheck_ReadsTheSectionOfTheFavorJob()
        {
            bool discarded = false, lostRep = false;
            for (ulong seed = 0; seed < 200 && !(discarded && lostRep); seed++)
            {
                var s = Game("ord-mantell", seed);
                P(s, "p1").Reputation.Syndicate = Reputation.Positive;
                Hold(s, HuttFavor);
                var r = TestHelpers.DeclineAll(C, s, Attempt(s, HuttFavor));
                var card = r.Events.OfType<DatabankResolvedEvent>().Select(d => d.CardId).FirstOrDefault();
                if (card == "base-databank-041a")
                {
                    // HUTT FAVOR: "If you have {REP_POS} Syndicate reputation, discard this job."
                    Assert.DoesNotContain(P(s, "p1").Assets, a => a.CardId == HuttFavor);
                    Assert.Equal(HuttFavor, Market.Deck(s, "job").Cards.Last());
                    discarded = true;
                }
                else if (card == "base-databank-041b")
                {
                    Assert.Contains(P(s, "p1").Assets, a => a.CardId == HuttFavor);
                    lostRep |= P(s, "p1").Reputation.Hutt == Reputation.Negative;
                }
            }
            Assert.True(discarded && lostRep);
        }

        [Fact]
        public void LowStakesSabacc_EachLoses5000_AndTheWinnerGainsTheReward()
        {
            for (ulong seed = 0; seed < 40; seed++)
            {
                var s = Game("tatooine", seed);
                P(s, "p1").Credits = 6000;
                P(s, "p2").Credits = 6000;
                Hold(s, LowStakes);
                var r = TestHelpers.DeclineAll(C, s, Attempt(s, LowStakes));
                var won = r.Events.OfType<SabaccWonEvent>().SingleOrDefault();
                if (won == null) continue;
                var winner = P(s, won.WinnerId);
                var loser = P(s, won.LoserId);
                Assert.Equal(1000 + 10000, winner.Credits);
                Assert.Equal(1000, loser.Credits);
                Assert.DoesNotContain(P(s, "p1").Assets, a => a.CardId == LowStakes); // R-25.20: discarded whoever wins
                return;
            }
            Assert.Fail("No game of sabacc had a winner.");
        }

        [Fact]
        public void R15_92_Wanted_FightsANegativePlayerHere_AndPaysOnAWin()
        {
            for (ulong seed = 0; seed < 80; seed++)
            {
                var s = Game("tatooine", seed);
                P(s, "p2").Reputation.Hutt = Reputation.Negative;
                P(s, "p2").Fame = 3; // more fame than you: 2 fame
                Hold(s, Wanted);
                int credits = P(s, "p1").Credits;
                var r = TestHelpers.DeclineAll(C, s, Attempt(s, Wanted));
                if (r.Events.OfType<CombatResolvedEvent>().Single().Winner != "attacker") continue;
                Assert.Equal(credits + 5000, P(s, "p1").Credits);
                Assert.Equal(2, P(s, "p1").Fame);
                Assert.True(r.Events.OfType<JobCompletedEvent>().Single().Completed); // claiming the reward completes it (R-18.7, owner)
                Assert.Contains(Wanted, Market.Deck(s, "bounty").Cards);
                return;
            }
            Assert.Fail("No seed won the Wanted combat.");
        }

        [Fact]
        public void Wanted_NeedsANegativePlayerInYourSpace()
        {
            var s = Game("tatooine");
            Hold(s, Wanted);
            Assert.Contains("R-15.29", Attempt(s, Wanted).Reason);
        }

        [Fact]
        public void BlackSunAgent_IncreasesAJobsReward_By2000()
        {
            var s = Game("lothal");
            P(s, "p1").Reputation.Imperial = Reputation.Positive;
            Hold(s, Statue);
            P(s, "p1").Assets.Add(new AssetState { CardId = "base-databank-005-asset", Kind = "crew" });
            int credits = P(s, "p1").Credits;
            TestHelpers.DeclineAll(C, s, Attempt(s, Statue));
            Assert.Equal(credits + 2000, P(s, "p1").Credits);
        }

        [Fact]
        public void R25_11_SySnootles_TradesTheJobsCredits_ForOneAdditionalFame()
        {
            for (ulong seed = 0; seed < 150; seed++)
            {
                var s = Game("cantonica", seed);
                Hold(s, Casino);
                P(s, "p1").Assets.Add(new AssetState { CardId = "base-market-luxury-04", Kind = "crew" });
                int credits = P(s, "p1").Credits;
                var r = Attempt(s, Casino);
                while (s.Pending != null)
                {
                    bool sy = s.Pending.Prompt == "rules.use_ability" && s.Pending.Options.Any(o => o.Label.StartsWith("base-market-luxury-04"));
                    var answer = sy ? "yes"
                        : s.Pending.Options.Any(o => o.Id == "no") ? "no" : s.Pending.Options.Any(o => o.Id == "pass") ? "pass" : s.Pending.Options[0].Id;
                    r = GameEngine.Execute(C, s, new ChooseOptionCommand(s.Pending.PlayerId, s.Pending.Id, answer));
                }
                if (P(s, "p1").Assets.Any(a => a.CardId == Casino)) continue; // failed
                Assert.Equal(credits, P(s, "p1").Credits);
                Assert.Equal(1, P(s, "p1").Fame);
                return;
            }
            Assert.Fail("No seed completed the heist.");
        }

        [Fact]
        public void R18_7_ClaimingAJobsReward_CountsForDoctorAphrasGoal()
        {
            for (ulong seed = 0; seed < 80; seed++)
            {
                var s = Game("tatooine", seed, "base-character-05");
                P(s, "p2").Reputation.Hutt = Reputation.Negative;
                Hold(s, Wanted);
                var r = TestHelpers.DeclineAll(C, s, Attempt(s, Wanted));
                if (r.Events.OfType<CombatResolvedEvent>().Single().Winner != "attacker") continue;
                Assert.Equal(1, Goals.Tokens(P(s, "p1"), "base-character-05#sides[0].abilities[1]")); // owner decision: claimed = completed
                return;
            }
            Assert.Fail("No seed won the Wanted combat.");
        }
    }
}
