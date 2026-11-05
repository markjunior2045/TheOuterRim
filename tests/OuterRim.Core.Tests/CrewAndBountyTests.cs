using System.Collections.Generic;
using System.Linq;
using OuterRim.Core.Effects;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;
using Xunit;

namespace OuterRim.Core.Tests
{
    /// <summary>Crew and luxury abilities, extra market actions and bounties against crew (T2.6 part 4b).</summary>
    public class CrewAndBountyTests
    {
        private static readonly Content.GameContent C = TestContent.Base;

        private static GameState Game(string space, ulong seed = 181, string character = "base-character-00")
        {
            var s = GameEngine.NewGame(C, new GameSetup(seed, new[]
            {
                new PlayerSetup("p1", "One", "green", "kessel", TestContent.G9Rigger, character),
                new PlayerSetup("p2", "Two", "red", "tatooine", TestContent.G9Rigger, "base-character-03"),
            }, new[] { new PackRef("base", "0.1.0") }), out _);
            s.CurrentPlayerId = "p1";
            P(s, "p1").SpaceId = space;
            foreach (var p in s.Patrols) p.SpaceId = "nav-tatooine-1";
            return s;
        }

        private static PlayerState P(GameState s, string id) => s.FindPlayer(id)!;

        private static void Give(GameState s, string card, string kind, string player = "p1", int cost = 0)
        {
            P(s, player).Assets.Add(new AssetState { CardId = card, Kind = kind, Cost = cost });
            foreach (var d in s.MarketDecks) d.Cards.Remove(card);
        }

        private static void ToEncounter(GameState s)
        {
            GameEngine.Execute(C, s, new MoveCommand("p1", new string[0]));
            GameEngine.Execute(C, s, new EndActionStepCommand("p1"));
        }

        private static CommandResult Answer(GameState s, string option)
        {
            Assert.NotNull(s.Pending);
            Assert.Contains(s.Pending!.Options, o => o.Id == option);
            return GameEngine.Execute(C, s, new ChooseOptionCommand(s.Pending.PlayerId, s.Pending.Id, option));
        }

        [Fact]
        public void W1LE_PerformsAMarketAction_AsTheEncounter_EvenAtANavpoint()
        {
            var s = Game("nav-kessel-3");
            P(s, "p1").Credits = 10000;
            Give(s, "base-databank-022-asset", "crew");
            ToEncounter(s);
            var top = Market.Deck(s, "gear").Cards[0];
            var r = GameEngine.Execute(C, s, new UseAbilityCommand("p1", "base-databank-022-asset", "abilities[0]"));
            Assert.True(r.Accepted, r.Reason);
            Assert.Equal("market.session", s.Pending!.Prompt);
            r = TestHelpers.DeclineAll(C, s, Answer(s, "buy:gear"));
            Assert.Contains(P(s, "p1").Assets, a => a.CardId == top);
            Assert.Equal("p2", s.CurrentPlayerId); // it was the turn's encounter (R-4.16)
        }

        [Fact]
        public void R25_9_AphraBack_MarketActionInPlanning_DoesNotUseTheActionStepsOne()
        {
            var s = Game("kessel", character: "base-character-05");
            P(s, "p1").CharacterSide = 1;
            P(s, "p1").Credits = 20000;
            var r = GameEngine.Execute(C, s, new UseAbilityCommand("p1", "base-character-05", "sides[1].abilities[1]"));
            Assert.True(r.Accepted, r.Reason);
            Answer(s, "discard:cargo");
            Answer(s, "done");
            Assert.Equal(TurnStep.Action, s.Step);
            Assert.True(GameEngine.Execute(C, s, new MarketDiscardCommand("p1", "gear")).Accepted); // the action step's own market action
        }

        [Fact]
        public void K2SO_OpponentsCritsCountOne_InGroundCombat()
        {
            var s = Game("kessel");
            Give(s, "base-databank-016-asset", "crew");
            Assert.Equal(1, Modifiers.OpponentCritValue(C, s, P(s, "p1"), "ground"));
            Assert.Equal(2, Modifiers.OpponentCritValue(C, s, P(s, "p1"), "ship"));
            Assert.Equal(3, Combat.DamageOf(new[] { DieFace.Crit, DieFace.Crit, DieFace.Hit }, 1));
        }

        [Fact]
        public void PondaBaba_TwoFocus_WinsTheGroundCombat()
        {
            var s = Game("kessel");
            Give(s, "base-databank-020-asset", "crew");
            Assert.True(Modifiers.AutoWins(C, s, P(s, "p1"), "ground", new[] { DieFace.Focus, DieFace.Blank, DieFace.Focus }));
            Assert.False(Modifiers.AutoWins(C, s, P(s, "p1"), "ground", new[] { DieFace.Focus, DieFace.Hit }));
            Assert.False(Modifiers.AutoWins(C, s, P(s, "p1"), "ship", new[] { DieFace.Focus, DieFace.Focus }));
        }

        [Fact]
        public void Evazan_TurnsUpToTwoBlanks_ToTheChosenResult()
        {
            var combat = new Frame(Combat.Handler);
            combat.Set("evazan", "crit");
            var faces = new List<DieFace> { DieFace.Blank, DieFace.Hit, DieFace.Blank, DieFace.Blank };
            Combat.Evazan(combat, faces);
            Assert.Equal(new[] { DieFace.Crit, DieFace.Hit, DieFace.Crit, DieFace.Blank }, faces);
        }

        [Fact]
        public void SavvyPilot_DiscardsItself_ToPassATestThatWouldFail()
        {
            for (ulong seed = 0; seed < 60; seed++)
            {
                var s = Game("kessel", seed);
                Give(s, "base-encounter-cantonica-ord-mantell-00-asset", "crew");
                var test = new Frame(SkillTests.Handler);
                test.Set("player", "p1");
                test.Set("skills", "tech");
                var r = TestHelpers.PassDiceWindows(C, s, GameEngine.StartResolution(C, s, test));
                if (s.Pending?.Prompt != "rules.use_ability") continue; // passed on its own
                r = Answer(s, "yes");
                Assert.True(r.Events.OfType<SkillTestRolledEvent>().Last().Passed);
                Assert.DoesNotContain(P(s, "p1").Assets, a => a.Kind == "crew");
                return;
            }
            Assert.Fail("No seed failed the test.");
        }

        [Fact]
        public void GainCard90_GivesAStartingBountyNobodyHolds()
        {
            var s = Game("nal-hutta");
            ToEncounter(s);
            var deck = Encounters.Deck(s, "tatooine-nal-hutta");
            deck.Cards.Remove("base-encounter-tatooine-nal-hutta-09");
            deck.Cards.Insert(0, "base-encounter-tatooine-nal-hutta-09");
            var held = s.Players.SelectMany(p => p.Assets).Select(a => a.CardId).ToList();
            TestHelpers.DeclineAll(C, s, GameEngine.Execute(C, s, new EncounterCommand("p1", EncounterKind.Space)));
            var bounty = P(s, "p1").Assets.Single(a => a.CardId.StartsWith("base-databank-090") && !held.Contains(a.CardId));
            Assert.Equal("bounty", bounty.Kind);
        }

        [Fact]
        public void StolenJewels_BothPlayersGain7000AndOneFame()
        {
            var s = Game("tatooine");
            s.Step = TurnStep.Action;
            Give(s, "base-market-luxury-03", "cargo", cost: 10000);
            int mine = P(s, "p1").Credits, theirs = P(s, "p2").Credits;
            var r = GameEngine.Execute(C, s, new UseAbilityCommand("p1", "base-market-luxury-03", "abilities[0]"));
            Assert.True(r.Accepted, r.Reason);
            Assert.Equal(mine + 7000, P(s, "p1").Credits);
            Assert.Equal(theirs + 7000, P(s, "p2").Credits);
            Assert.Equal(1, P(s, "p2").Fame);
        }

        [Fact]
        public void UndercoverAgent_TheMoreFamousPlayer_LosesAReputationOfYourChoice()
        {
            var s = Game("tatooine");
            s.Step = TurnStep.Action;
            P(s, "p2").Fame = 2;
            Give(s, "base-market-luxury-08", "crew", cost: 10000);
            var r = GameEngine.Execute(C, s, new UseAbilityCommand("p1", "base-market-luxury-08", "abilities[0]"));
            Assert.True(r.Accepted, r.Reason);
            Answer(s, "hutt");
            Assert.Equal(1, P(s, "p1").Fame);
            Assert.Equal(Reputation.Negative, P(s, "p2").Reputation.Hutt);
        }

        [Fact]
        public void Dejarik_RollsOneDiePerCrew_AtANavpoint()
        {
            var s = Game("nav-kessel-3");
            s.Step = TurnStep.Action;
            Give(s, "base-market-luxury-10", "cargo", cost: 10000);
            Give(s, "base-databank-006-asset", "crew");
            Give(s, "base-databank-014-asset", "crew");
            int credits = P(s, "p1").Credits;
            var r = GameEngine.Execute(C, s, new UseAbilityCommand("p1", "base-market-luxury-10", "abilities[1]"));
            Assert.True(r.Accepted, r.Reason);
            var faces = r.Events.OfType<EffectDiceRolledEvent>().Single().Faces;
            Assert.Equal(2, faces.Count);
            Assert.Equal(credits + 1000 * faces.Count(f => f == DieFace.Hit || f == DieFace.Focus), P(s, "p1").Credits);
            Assert.Equal(1, PlayerRules.Fame(C, s, P(s, "p1"))); // "While you have 2 or more crew, you have 1 additional fame"
        }

        [Fact]
        public void ChromiumHull_LetsAPlayerInItsSpaceFightItsOwner_AsTheirEncounter()
        {
            var s = Game("tatooine");
            Give(s, "base-market-luxury-01", "mod", "p2", 15000);
            ToEncounter(s);
            Assert.True(GameEngine.Execute(C, s, new EncounterCommand("p1", EncounterKind.Player, "p2")).Accepted);
            var t = Game("tatooine");
            ToEncounter(t);
            Assert.False(GameEngine.Execute(C, t, new EncounterCommand("p1", EncounterKind.Player, "p2")).Accepted);
        }

        [Fact]
        public void R15_56_R15_58_FightingYourOwnCrew_LosingDiscardsItAndReturnsItsToken()
        {
            bool lost = false, won = false;
            for (ulong seed = 0; seed < 120 && !(lost && won); seed++)
            {
                var s = Game("kessel", seed);
                Give(s, "base-market-bounty-00", "bounty");     // Saw Gerrera, ground 6
                Give(s, "base-databank-021-asset", "crew");     // Saw Gerrera crew
                var token = Contacts.Token(s, "base-contact-21-saw-gerrera");
                token.SpaceId = "";
                token.Slot = "";
                token.Pool = true;
                ToEncounter(s);
                var r = GameEngine.Execute(C, s, new EncounterCommand("p1", EncounterKind.BountyCrew, "base-market-bounty-00"));
                Assert.True(r.Accepted, r.Reason);
                while (s.Pending != null && s.Pending.Prompt != "rules.eliminate_or_capture")
                {
                    r = TestHelpers.DeclineAll(C, s, CommandResult.Ok(new GameEvent[0]));
                }
                Assert.DoesNotContain(P(s, "p1").Assets, a => a.CardId == "base-databank-021-asset"); // discarded either way
                if (s.Pending?.Prompt == "rules.eliminate_or_capture")
                {
                    Answer(s, "capture");
                    Assert.Equal("base-market-bounty-00", token.Bounty);
                    won = true;
                }
                else
                {
                    Assert.True(token.OnMap && token.FaceUp);
                    lost = true;
                }
            }
            Assert.True(lost && won);
        }

        [Fact]
        public void R15_61_TheOwnerMayProtectTheirCrew_AndFightYouInstead()
        {
            var s = Game("tatooine");
            Give(s, "base-market-bounty-00", "bounty");
            Give(s, "base-databank-021-asset", "crew", "p2");
            ToEncounter(s);
            Assert.True(GameEngine.Execute(C, s, new EncounterCommand("p1", EncounterKind.BountyCrew, "base-market-bounty-00")).Accepted);
            Assert.Equal("p2", s.Pending!.PlayerId);
            var r = TestHelpers.PassDiceWindows(C, s, Answer(s, "yes"));
            var combat = r.Events.OfType<CombatStartedEvent>().Single();
            Assert.Equal("player", combat.DefenderKind);
        }

        [Fact]
        public void RebelPrisonBreak_MayReplaceTheForcedImperialPatrolEncounter()
        {
            var s = Game("kessel");
            Patrols.Of(s, Faction.Imperial).SpaceId = "kessel";
            P(s, "p1").Reputation.Imperial = Reputation.Negative;
            Give(s, "base-databank-017-asset", "job");
            ToEncounter(s);
            Assert.Contains("R-4.14", GameEngine.Execute(C, s, new EncounterCommand("p1", EncounterKind.Space)).Reason);
            Assert.True(GameEngine.Execute(C, s, new EncounterCommand("p1", EncounterKind.Job, "base-databank-017-asset")).Accepted);
        }
    }
}
