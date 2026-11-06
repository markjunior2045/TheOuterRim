using System.Collections.Generic;
using System.Linq;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;
using Xunit;

namespace OuterRim.Core.Tests
{
    /// <summary>Market, card-game and sheet effects of encounter cards (EFFECTS.md §4, §8).</summary>
    public class EncounterCardTests
    {
        private static readonly Content.GameContent C = TestContent.Base;

        private static GameState Game(string space, ulong seed = 151)
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

        private static void MarketTop(GameState s, string deck, params string[] cards)
        {
            var d = Market.Deck(s, deck);
            for (int i = cards.Length - 1; i >= 0; i--)
            {
                d.Cards.Remove(cards[i]);
                d.Cards.Insert(0, cards[i]);
            }
        }

        [Fact]
        public void R16_45_R16_44_RevealThree_BuyAnIllegalOneWithADiscount_ThenShuffle()
        {
            var s = Game("kessel");
            P(s, "p1").Credits = 5000;
            MarketTop(s, "cargo", "base-market-cargo-00", "base-market-cargo-06", "base-market-cargo-03");
            var r = Encounter(s, "base-encounter-kessel-maelstrom-00"); // Kessel, Syndicate neutral
            Assert.Equal("effects.market_buy", s.Pending!.Prompt);
            Assert.Equal(new[] { "base-market-cargo-06", "none" }, s.Pending.Options.Select(o => o.Id)); // only the ILLEGAL one
            r = TestHelpers.DeclineAll(C, s, Answer(s, "base-market-cargo-06"));
            Assert.Equal(3000, P(s, "p1").Credits); // 3,000 less the 1,000 discount
            Assert.Contains(P(s, "p1").Assets, a => a.CardId == "base-market-cargo-06" && a.Cost == 3000);
            Assert.Contains(r.Events, e => e is MarketDeckShuffledEvent);
            Assert.Contains(r.Events, e => e is MarketCardRevealedEvent);
            Assert.Equal(9, Market.Deck(s, "cargo").Cards.Count);
        }

        [Fact]
        public void R16_34_BarteringWhenBuyingThroughAnEffect()
        {
            var s = Game("ring-of-kafrene");
            P(s, "p1").Credits = 0;
            P(s, "p1").Assets.Add(new AssetState { CardId = "base-market-gear-04", Kind = "gear", Cost = 8000 });
            Market.Deck(s, "gear").Cards.Remove("base-market-gear-04");
            MarketTop(s, "gear", "base-market-gear-07");
            var gearCost = (int)C.Card("base-market-gear-07")["cost"]!;
            Encounter(s, "base-encounter-ring-of-kafrene-takodana-00"); // reveal 3 gear, buy a mod with 1,000 off
            Answer(s, "base-market-gear-07");
            Assert.Equal("effects.barter", s.Pending!.Prompt);
            Assert.DoesNotContain(s.Pending.Options, o => o.Id == "done"); // cannot pay yet
            TestHelpers.DeclineAll(C, s, Answer(s, "base-market-gear-04"));
            Assert.Contains(P(s, "p1").Assets, a => a.CardId == "base-market-gear-07");
            Assert.DoesNotContain(P(s, "p1").Assets, a => a.CardId == "base-market-gear-04");
            Assert.Equal(0, P(s, "p1").Credits); // R-16.36: no change given
            Assert.True(gearCost - 1000 <= 8000);
        }

        [Fact]
        public void AFreeTopCard_IsBought_WhenTheCargoIsNotDelivered()
        {
            var s = Game("lothal");
            P(s, "p1").Reputation.Imperial = Reputation.Positive;
            int credits = P(s, "p1").Credits;
            MarketTop(s, "cargo", "base-market-cargo-04");
            Encounter(s, "base-encounter-lothal-mon-calamari-06"); // Lothal, Imperial positive
            Answer(s, "no"); // do not deliver
            Assert.Equal(new[] { "base-market-cargo-04", "none" }, s.Pending!.Options.Select(o => o.Id)); // R-25.18: optional
            TestHelpers.DeclineAll(C, s, Answer(s, "base-market-cargo-04"));
            Assert.Contains(P(s, "p1").Assets, a => a.CardId == "base-market-cargo-04");
            Assert.Equal(credits, P(s, "p1").Credits);
        }

        [Fact]
        public void TakingTheSyndicateFavor_FromTheJobDeck()
        {
            var s = Game("kessel");
            P(s, "p1").Reputation.Syndicate = Reputation.Positive;
            Encounter(s, "base-encounter-kessel-maelstrom-02");
            TestHelpers.DeclineAll(C, s, Answer(s, "yes"));
            Assert.Contains(P(s, "p1").Assets, a => a.CardId == "base-market-job-11");
            Assert.DoesNotContain("base-market-job-11", Market.Deck(s, "job").Cards);
        }

        [Fact]
        public void TakingTheRebelFavor_FromAnotherPlayer()
        {
            var s = Game("mon-calamari");
            P(s, "p2").Assets.Add(new AssetState { CardId = "base-market-job-08", Kind = "job" });
            Market.Deck(s, "job").Cards.Remove("base-market-job-08");
            Encounter(s, "base-encounter-lothal-mon-calamari-06"); // Mon Calamari
            Answer(s, "yes");
            Assert.Contains(P(s, "p1").Assets, a => a.CardId == "base-market-job-08");
            Assert.Empty(P(s, "p2").Assets);
        }

        [Fact]
        public void NameANumber_PaysTheNumberTimes2000_OnEnoughHits()
        {
            for (ulong seed = 0; seed < 40; seed++)
            {
                var s = Game("cantonica", seed);
                P(s, "p1").Credits = 5000;
                Encounter(s, "base-encounter-cantonica-ord-mantell-04");
                Answer(s, "yes");
                var r = Answer(s, "2");
                var hits = r.Events.OfType<EffectDiceRolledEvent>().Single().Faces.Count(f => f == DieFace.Hit);
                Assert.Equal(hits >= 2 ? 8000 : 4000, P(s, "p1").Credits);
                if (hits >= 2) return;
            }
            Assert.Fail("No seed rolled two hits.");
        }

        [Fact]
        public void SideBets_EachPlayerWhoChoseTheRolledResult_Gains()
        {
            var s = Game("cantonica");
            P(s, "p1").Credits = 0;
            P(s, "p2").Credits = 2000;
            Encounter(s, "base-encounter-cantonica-ord-mantell-02");
            Answer(s, "hit"); // you
            Assert.Equal("p2", s.Pending!.PlayerId);
            var r = Answer(s, "crit"); // the other player pays 1,000 to bet
            var face = r.Events.OfType<EffectDiceRolledEvent>().Single().Faces[0];
            Assert.Equal(1000 + (face == DieFace.Hit ? 3000 : 0), P(s, "p1").Credits);
            Assert.Equal(1000 + (face == DieFace.Crit ? 8000 : 0), P(s, "p2").Credits);
        }

        [Fact]
        public void DiceGamble_TripleWithoutHits()
        {
            var s = Game("nal-hutta");
            P(s, "p1").Credits = 3000;
            Encounter(s, "base-encounter-tatooine-nal-hutta-00");
            var r = Answer(s, "2000");
            var faces = r.Events.OfType<EffectDiceRolledEvent>().Single().Faces;
            Assert.Equal(2, faces.Count);
            bool lost = faces.Any(f => f == DieFace.Hit || f == DieFace.Crit);
            Assert.Equal(lost ? 1000 : 7000, P(s, "p1").Credits);
        }

        [Fact]
        public void LoseAnyReputation_ThenAllNegative_GivesFameAndSyndicate()
        {
            var s = Game("kessel");
            Encounter(s, "base-encounter-kessel-maelstrom-07");
            Answer(s, "yes");
            Answer(s, "1"); // Hutt
            Answer(s, "1"); // Rebel
            Answer(s, "1"); // Imperial
            Assert.Equal(1, P(s, "p1").Fame);
            Assert.Equal(Reputation.Positive, P(s, "p1").Reputation.Syndicate);
        }

        [Fact]
        public void R24_5_ReplacingTheShipWithAStarter_GivesFameReputationAndCredits()
        {
            var s = Game("ryloth");
            var p = P(s, "p1");
            p.ShipSheetId = "base-ship-hwk-290-freighter";
            p.ShipSideId = C.ShipGoalSide("base-ship-hwk-290-freighter").Id;
            s.ShipSupply.Remove("base-ship-hwk-290-freighter");
            int credits = p.Credits;
            Encounter(s, "base-encounter-ryloth-naboo-09");
            TestHelpers.DeclineAll(C, s, Answer(s, "yes"));
            Assert.Equal("starter", C.ShipSide(p.ShipSideId).Role);
            Assert.Contains("base-ship-hwk-290-freighter", s.ShipSupply);
            Assert.Equal(1, p.Fame);
            Assert.Equal(credits + 5000, p.Credits);
        }

        [Fact]
        public void R24_5_NotReplacing_LetsYouTradeImperialReputationFor4000()
        {
            var s = Game("ryloth");
            var p = P(s, "p1");
            p.ShipSheetId = "base-ship-hwk-290-freighter";
            p.ShipSideId = C.ShipGoalSide("base-ship-hwk-290-freighter").Id;
            int credits = p.Credits;
            Encounter(s, "base-encounter-ryloth-naboo-09");
            Answer(s, "no");
            Answer(s, "yes");
            Assert.Equal(credits + 4000, p.Credits);
            Assert.Equal(Reputation.Negative, p.Reputation.Imperial);
        }

        [Fact]
        public void BuyAnySheet_AndItsCardLeavesTheShipDeck()
        {
            var s = Game("ryloth");
            P(s, "p1").Credits = 20000;
            Encounter(s, "base-encounter-ryloth-naboo-03"); // Ryloth, Rebel neutral
            Answer(s, "yes");
            var sheet = s.Pending!.Options[0].Id;
            TestHelpers.DeclineAll(C, s, Answer(s, sheet));
            Assert.Equal(sheet, P(s, "p1").ShipSheetId);
            Assert.DoesNotContain(Market.Deck(s, "ship").Cards, id => (string?)C.Card(id)["sheet"] == sheet);
        }

        [Fact]
        public void PayingToCompleteYourPersonalGoal_GainsFameAndFlipsTheCharacter()
        {
            var s = Game("ring-of-kafrene");
            P(s, "p1").Credits = 5000;
            Encounter(s, "base-encounter-ring-of-kafrene-takodana-04");
            TestHelpers.DeclineAll(C, s, Answer(s, "yes")); // Ketsu's personal goal is the only goal: no choice to make
            Assert.Equal(1, P(s, "p1").Fame);
            Assert.Equal(1, P(s, "p1").CharacterSide);
            Assert.Equal(1000, P(s, "p1").Credits);
        }

        [Fact]
        public void R8_6_WinningAgainstTheCreature_PreventsAllItsDamage()
        {
            for (ulong seed = 0; seed < 60; seed++)
            {
                var s = Game("maelstrom", seed);
                var r = TestHelpers.DeclineAll(C, s, Encounter(s, "base-encounter-kessel-maelstrom-03"));
                var result = r.Events.OfType<CombatResolvedEvent>().Single();
                if (result.Winner != "attacker" || result.DefenderDamage == 0) continue;
                Assert.Equal(0, P(s, "p1").ShipDamage);
                Assert.Contains(r.Events, e => e is AdditionalTurnGainedEvent);
                return;
            }
            Assert.Fail("No seed won against a creature that dealt damage.");
        }

        [Fact]
        public void GA97_LooksAtAnEncounterDecksTopCard_AndMayPutItAtTheBottom()
        {
            var s = Game("takodana");
            s.Step = TurnStep.Action;
            P(s, "p1").Assets.Add(new AssetState { CardId = "base-encounter-ring-of-kafrene-takodana-04-asset", Kind = "crew" });
            var deck = Encounters.Deck(s, "navpoint");
            var top = deck.Cards[0];
            GameEngine.Execute(C, s, new UseAbilityCommand("p1", "base-encounter-ring-of-kafrene-takodana-04-asset", "abilities[0]"));
            var r = Answer(s, "navpoint");
            Assert.Equal(top, r.Events.OfType<EncounterCardPeekedEvent>().Single().CardId);
            Answer(s, "bottom");
            Assert.Equal(top, deck.Cards.Last());
        }

        [Fact]
        public void DiscardingASecret_AsTheCostOfAnEncounter()
        {
            var s = Game("nal-hutta");
            P(s, "p1").Secrets.Add("base-encounter-navpoint-04#sections[0].variants[0]");
            Encounters.Deck(s, "navpoint").Cards.Remove("base-encounter-navpoint-04");
            int credits = P(s, "p1").Credits;
            Encounter(s, "base-encounter-tatooine-nal-hutta-05"); // Nal Hutta: discard a bounty or secret
            Answer(s, "yes");
            Assert.Empty(P(s, "p1").Secrets);
            Assert.Equal(credits + 2000, P(s, "p1").Credits);
            Assert.Equal("base-encounter-navpoint-04", Encounters.Deck(s, "navpoint").Cards.Last());
        }

        [Fact]
        public void PatrolHunt_SendsThePatrolToANegativePlayer_WhoMustEncounterIt()
        {
            var s = Game("nav-kessel-3");
            Patrols.Of(s, Faction.Hutt).SpaceId = "nav-kessel-3";
            P(s, "p1").Reputation.Hutt = Reputation.Positive; // not forced to fight it first (R-4.14)
            P(s, "p2").Reputation.Hutt = Reputation.Negative;
            Encounter(s, "base-encounter-navpoint-06"); // a patrol is here
            var r = TestHelpers.DeclineAll(C, s, Answer(s, "yes"));
            var moved = r.Events.OfType<PatrolMovedEvent>().First();
            Assert.Equal("tatooine", moved.To); // directly to that player's space
            Assert.True(moved.Direct);
            var combat = r.Events.OfType<CombatStartedEvent>().Single();
            Assert.Equal("p2", combat.AttackerId);
        }
    }
}
