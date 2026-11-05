using System.Linq;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;
using Xunit;

namespace OuterRim.Core.Tests
{
    /// <summary>Effects used by encounter cards (EFFECTS.md §4), run through the cards themselves.</summary>
    public class EncounterEffectTests
    {
        private static readonly Content.GameContent C = TestContent.Base;

        private static GameState Game(string space, ulong seed = 141)
        {
            var s = GameEngine.NewGame(C, new GameSetup(seed, new[]
            {
                new PlayerSetup("p1", "One", "green", "kessel", TestContent.G9Rigger, "base-character-00"),
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

        private static CommandResult Encounter(GameState s, string card)
        {
            var deck = Encounters.Deck(s, (string)C.Card(card)["deck"]!);
            deck.Cards.Remove(card);
            deck.Cards.Insert(0, card);
            return GameEngine.Execute(C, s, new EncounterCommand("p1", EncounterKind.Space));
        }

        private static CommandResult Answer(GameState s, string option)
        {
            Assert.NotNull(s.Pending);
            Assert.Contains(s.Pending!.Options, o => o.Id == option);
            return GameEngine.Execute(C, s, new ChooseOptionCommand(s.Pending.PlayerId, s.Pending.Id, option));
        }

        [Fact]
        public void ChooseAPlayer_ThatPlayerGainsReputation_AndSuffersDamage()
        {
            var s = Game("lothal");
            Encounter(s, "base-encounter-lothal-mon-calamari-07"); // Lothal: choose a player
            Assert.Equal("effects.choose_player", s.Pending!.Prompt);
            Answer(s, "p2");
            Assert.Equal(Reputation.Positive, P(s, "p2").Reputation.Rebel);
            Assert.Equal(2, P(s, "p2").CharacterDamage);
            Assert.Equal(0, P(s, "p1").CharacterDamage);
        }

        [Fact]
        public void R21_4_LosingAReputationYouHavePositive_Pays4000_AndOnlyOffersThoseFactions()
        {
            var s = Game("ord-mantell");
            P(s, "p1").Reputation.Rebel = Reputation.Positive;
            P(s, "p1").Reputation.Hutt = Reputation.Positive;
            int credits = P(s, "p1").Credits;
            Encounter(s, "base-encounter-cantonica-ord-mantell-08"); // Ord Mantell
            Answer(s, "yes");
            Assert.Equal(new[] { "rebel", "hutt" }, s.Pending!.Options.Select(o => o.Id));
            Answer(s, "hutt");
            Assert.Equal(Reputation.Neutral, P(s, "p1").Reputation.Hutt);
            Assert.Equal(credits + 4000, P(s, "p1").Credits);
        }

        [Fact]
        public void R5_23_MovingAPatrolDirectly_ToTheSpaceOfYourChoice()
        {
            var s = Game("tatooine");
            P(s, "p1").Credits = 5000;
            Encounter(s, "base-encounter-tatooine-nal-hutta-06"); // Tatooine: spend 2,000 to move the Hutt patrol
            Answer(s, "yes");
            Assert.Equal("effects.move_to", s.Pending!.Prompt);
            Answer(s, "naboo");
            Assert.Equal("naboo", Patrols.Of(s, Faction.Hutt).SpaceId);
            Assert.Equal(3000, P(s, "p1").Credits);
        }

        [Fact]
        public void MoveUpToTwoSpaces_OffersStayingAndSpacesWithinTwo()
        {
            var s = Game("nav-kessel-3");
            Encounter(s, "base-encounter-navpoint-08"); // no patrol, Imperial neutral: move up to 2 spaces
            Assert.Equal("effects.move_to", s.Pending!.Prompt);
            var ends = s.Pending.Options.Select(o => o.Id).ToList();
            Assert.Contains("nav-kessel-3", ends);
            Assert.All(ends, e => Assert.InRange(C.Map.Distance("nav-kessel-3", e), 0, 2));
            var far = ends.First(e => C.Map.Distance("nav-kessel-3", e) == 2 && e != "maelstrom");
            var r = Answer(s, far);
            Assert.Equal(far, P(s, "p1").SpaceId);
            Assert.Contains(r.Events, e => e is TurnEndedEvent); // the encounter still ends the turn, no new action step
        }

        [Fact]
        public void R5_12_EffectMovement_DoesNotPassThroughTheMaelstrom()
        {
            var s = Game("kessel");
            var paths = MovePlayerEffectHandler.Reachable(C, s, P(s, "p1"), 5, exact: false);
            foreach (var path in paths.Values)
            {
                var steps = path.Split(',');
                Assert.DoesNotContain("maelstrom", steps.Take(steps.Length - 1));
            }
        }

        [Fact]
        public void MoveAPatrolUpToFour_InAnyDirection()
        {
            var s = Game("lothal");
            P(s, "p1").Reputation.Imperial = Reputation.Positive;
            Encounter(s, "base-encounter-lothal-mon-calamari-05"); // Lothal, Imperial positive
            var from = Patrols.Of(s, Faction.Imperial).SpaceId;
            Assert.Equal("rules.patrol_destination", s.Pending!.Prompt);
            Assert.All(s.Pending.Options, o => Assert.InRange(C.Map.Distance(from, o.Id, false), 0, 4));
            var pick = s.Pending.Options.First(o => C.Map.Distance(from, o.Id, false) == 4).Id;
            Answer(s, pick);
            Assert.Equal(pick, Patrols.Of(s, Faction.Imperial).SpaceId);
        }

        [Fact]
        public void MoveThePatrolToYou_AndEncounterIt()
        {
            var s = Game("nav-kessel-3");
            P(s, "p1").Reputation.Hutt = Reputation.Negative;
            var r = Encounter(s, "base-encounter-navpoint-00"); // no patrol, Hutt negative
            Assert.Equal("nav-kessel-3", Patrols.Of(s, Faction.Hutt).SpaceId);
            var all = TestHelpers.DeclineAll(C, s, r);
            Assert.Contains(all.Events, e => e is CombatStartedEvent);
        }

        [Fact]
        public void R5_14_AnAdditionalTurn_FollowsThisOne_ButNotAnotherInsideIt()
        {
            for (ulong seed = 0; seed < 60; seed++)
            {
                var s = Game("maelstrom", seed);
                var r = TestHelpers.DeclineAll(C, s, Encounter(s, "base-encounter-kessel-maelstrom-02")); // test Piloting or Tech
                if (!r.Events.OfType<AdditionalTurnGainedEvent>().Any()) continue;
                Assert.Equal("p1", s.CurrentPlayerId);
                Assert.Equal(TurnStep.Planning, s.Step);
                Assert.True(s.InAdditionalTurn);
                // Inside the additional turn, another one is ignored.
                GameEngine.Execute(C, s, new MoveCommand("p1", new string[0]));
                GameEngine.Execute(C, s, new EndActionStepCommand("p1"));
                for (ulong k = 0; k < 40 && s.CurrentPlayerId == "p1"; k++)
                {
                    TestHelpers.DeclineAll(C, s, Encounter(s, "base-encounter-kessel-maelstrom-02"));
                }
                Assert.Equal("p2", s.CurrentPlayerId);
                Assert.False(s.AdditionalTurn);
                return;
            }
            Assert.Fail("No seed passed the test.");
        }

        [Fact]
        public void SpendUpTo3000_PassingGivesTwiceTheAmountSpent()
        {
            for (ulong seed = 0; seed < 60; seed++)
            {
                var s = Game("cantonica", seed);
                P(s, "p1").Credits = 5000;
                Encounter(s, "base-encounter-cantonica-ord-mantell-03"); // Cantonica: spend up to 3,000 to test Tactics
                Answer(s, "yes");
                Assert.Equal(new[] { "0", "1000", "2000", "3000" }, s.Pending!.Options.Select(o => o.Id));
                var r = TestHelpers.DeclineAll(C, s, Answer(s, "2000"));
                var passed = r.Events.OfType<SkillTestRolledEvent>().Last().Passed;
                Assert.Equal(passed ? 7000 : 3000, P(s, "p1").Credits);
                if (passed) return;
            }
            Assert.Fail("No seed passed the test.");
        }

        [Fact]
        public void Thief_TakesCreditsFromAPlayerInYourSpace()
        {
            var s = Game("tatooine");
            s.Step = TurnStep.Action;
            P(s, "p1").Assets.Add(new AssetState { CardId = "base-encounter-cantonica-ord-mantell-08-asset", Kind = "crew" });
            P(s, "p2").Credits = 3000;
            int mine = P(s, "p1").Credits;
            var r = GameEngine.Execute(C, s, new UseAbilityCommand("p1", "base-encounter-cantonica-ord-mantell-08-asset", "abilities[0]"));
            Assert.True(r.Accepted, r.Reason);
            Assert.Equal(0, P(s, "p2").Credits); // took what they had (R-9.8)
            Assert.Equal(mine + 3000, P(s, "p1").Credits);
            Assert.DoesNotContain(P(s, "p1").Assets, a => a.Kind == "crew");
        }
    }
}
