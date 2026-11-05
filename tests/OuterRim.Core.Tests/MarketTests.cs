using System.Collections.Generic;
using System.Linq;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;
using Xunit;

namespace OuterRim.Core.Tests
{
    public class MarketTests
    {
        private static readonly Content.GameContent C = TestContent.Base;

        private static GameState Game(string space = "kessel", ulong seed = 111)
        {
            var s = GameEngine.NewGame(C, new GameSetup(seed, new[]
            {
                new PlayerSetup("p1", "One", "green", space, TestContent.G9Rigger, "base-character-00"),
                new PlayerSetup("p2", "Two", "red", "tatooine", TestContent.G9Rigger, "base-character-03"),
            }, new[] { new PackRef("base", "0.1.0") }), out _);
            s.CurrentPlayerId = "p1";
            GameEngine.Execute(C, s, new MoveCommand("p1", new string[0])); // to the action step
            return s;
        }

        private static PlayerState P1(GameState s) => s.FindPlayer("p1")!;

        private static void Top(GameState s, string deck, params string[] cards)
        {
            var d = Market.Deck(s, deck);
            for (int i = cards.Length - 1; i >= 0; i--)
            {
                d.Cards.Remove(cards[i]);
                d.Cards.Insert(0, cards[i]);
            }
        }

        private static CommandResult Buy(GameState s, string deck, params string[] barter)
        {
            return GameEngine.Execute(C, s, new MarketBuyCommand("p1", deck, barter));
        }

        [Fact]
        public void R3_16a_SixDecks_Shuffled_WithTheBaseGameCounts()
        {
            var s = Game();
            var sizes = s.MarketDecks.ToDictionary(d => d.Deck, d => d.Cards.Count);
            Assert.Equal(11, sizes["bounty"]);
            Assert.Equal(10, sizes["cargo"]);
            Assert.Equal(15, sizes["gear"]);
            Assert.Equal(14, sizes["job"]);
            Assert.Equal(11, sizes["luxury"]);
            Assert.Equal(9, sizes["ship"]);
            Assert.NotEqual(Game(seed: 1).MarketDecks[2].Cards, Game(seed: 2).MarketDecks[2].Cards);
            Assert.Equal(Game(seed: 1).MarketDecks[2].Cards, Game(seed: 1).MarketDecks[2].Cards);
        }

        [Fact]
        public void R16_22_R16_23_BuyingAGear_PaysAndPlacesIt()
        {
            var s = Game();
            Top(s, "gear", "base-market-gear-02"); // DL-44, 3,000
            int credits = P1(s).Credits;
            var r = TestHelpers.PassDiceWindows(C, s, Buy(s, "gear"));
            Assert.True(r.Accepted);
            Assert.Equal(credits - 3000, P1(s).Credits);
            Assert.Contains(P1(s).Assets, a => a.CardId == "base-market-gear-02" && a.Kind == "gear" && a.Cost == 3000);
            Assert.Equal(14, Market.Deck(s, "gear").Cards.Count);
            Assert.Contains(r.Events, e => e is MarketCardRevealedEvent);
        }

        [Fact]
        public void R11_13_R16_25_TheNewTopCardsPatrolIcon_MovesThatPatrolTowardTheBuyer()
        {
            var s = Game();
            // DL-44 on top, then the IQA-11 (Rebel patrol, distance 4) under it.
            Top(s, "gear", "base-market-gear-02", "base-market-gear-00");
            var rebelBefore = Patrols.Of(s, Faction.Rebel).SpaceId;
            var r = Buy(s, "gear");
            while (s.Pending != null) GameEngine.Execute(C, s, new ChooseOptionCommand(s.Pending.PlayerId, s.Pending.Id, s.Pending.Options[0].Id));
            var after = Patrols.Of(s, Faction.Rebel).SpaceId;
            Assert.NotEqual(rebelBefore, after);
            Assert.True(C.Map.Distance(after, "kessel") < C.Map.Distance(rebelBefore, "kessel"));
        }

        [Fact]
        public void R16_20_DiscardingDoesNotMoveAPatrol_AndASecondDiscardComesFromTheSameDeck()
        {
            var s = Game();
            Top(s, "gear", "base-market-gear-00", "base-market-gear-01");
            var patrols = s.Patrols.Select(p => p.SpaceId).ToList();
            Assert.True(GameEngine.Execute(C, s, new MarketDiscardCommand("p1", "gear")).Accepted);
            Assert.Equal("base-market-gear-01", Market.Deck(s, "gear").Cards[0]);
            Assert.Equal("base-market-gear-00", Market.Deck(s, "gear").Cards.Last()); // R-22.1: to the bottom
            Assert.Contains("R-16.19", GameEngine.Execute(C, s, new MarketDiscardCommand("p1", "cargo")).Reason);
            Assert.True(GameEngine.Execute(C, s, new MarketDiscardCommand("p1", "gear")).Accepted);
            Assert.Contains("R-16.19", GameEngine.Execute(C, s, new MarketDiscardCommand("p1", "gear")).Reason);
            Assert.Equal(patrols, s.Patrols.Select(p => p.SpaceId).ToList());
        }

        [Fact]
        public void R4_9_OneMarketActionPerActionStep()
        {
            var s = Game();
            P1(s).Credits = 50000;
            Top(s, "gear", "base-market-gear-02");
            TestHelpers.PassDiceWindows(C, s, Buy(s, "gear"));
            while (s.Pending != null) GameEngine.Execute(C, s, new ChooseOptionCommand(s.Pending.PlayerId, s.Pending.Id, s.Pending.Options[0].Id));
            Assert.Contains("R-4.9", Buy(s, "cargo").Reason);
        }

        [Fact]
        public void R16_17_TheMarketNeedsAPlanet()
        {
            var s = Game();
            P1(s).SpaceId = "nav-kessel-3";
            Assert.Contains("R-16.17", Buy(s, "gear").Reason);
        }

        [Fact]
        public void R15_6_CargoCannotBeBought_OnItsDestination()
        {
            var s = Game(space: "kessel");
            Top(s, "cargo", "base-market-cargo-01"); // Stolen Supplies, to Kessel
            Assert.Contains("R-15.6", Buy(s, "cargo").Reason);
        }

        [Fact]
        public void R9_8_ACardYouCannotAfford_IsRejected()
        {
            var s = Game();
            P1(s).Credits = 1000;
            Top(s, "gear", "base-market-gear-04"); // 8,000
            Assert.Contains("R-9.8", Buy(s, "gear").Reason);
        }

        [Fact]
        public void R16_31_BarteringAGear_LowersThePrice_AndTheGearReturnsToItsDeck()
        {
            var s = Game();
            P1(s).Credits = 2000;
            P1(s).Assets.Add(new AssetState { CardId = "base-market-gear-02", Kind = "gear", Cost = 3000 });
            Market.Deck(s, "gear").Cards.Remove("base-market-gear-02");
            Top(s, "gear", "base-market-gear-00"); // IQA-11, 5,000
            Assert.True(Buy(s, "gear", "base-market-gear-02").Accepted);
            Assert.Equal(0, P1(s).Credits);
            Assert.Equal("base-market-gear-02", Market.Deck(s, "gear").Cards.Last());
        }

        [Fact]
        public void R16_27_BuyingAShipCard_ReplacesTheSheet_AndRemovesTheCard()
        {
            var s = Game();
            P1(s).Credits = 6000;
            Top(s, "ship", "base-market-ship-02"); // HWK-290, 5,000
            Assert.True(Buy(s, "ship").Accepted);
            Assert.Equal("base-ship-hwk-290-freighter", P1(s).ShipSheetId);
            Assert.Equal(1000, P1(s).Credits);
            Assert.DoesNotContain("base-market-ship-02", Market.Deck(s, "ship").Cards);
            Assert.Equal(8, Market.Deck(s, "ship").Cards.Count);
        }

        [Fact]
        public void R16_40_UsedShipForSale_BuysABetterSheet_With3Damage_AndGoesBackIntoTheDeck()
        {
            var s = Game();
            P1(s).Credits = 10000;
            Top(s, "ship", "base-market-ship-04");
            Assert.True(Buy(s, "ship").Accepted);
            Assert.Equal("market.used_ship", s.Pending!.Prompt);
            Assert.All(s.Pending.Options, o => Assert.True((C.ShipGoalSide(o.Id).Cost ?? 0) <= 10000));
            GameEngine.Execute(C, s, new ChooseOptionCommand("p1", s.Pending.Id, "base-ship-lancer-class-pursuit-craft"));
            Assert.Equal("base-ship-lancer-class-pursuit-craft", P1(s).ShipSheetId);
            Assert.Equal(0, P1(s).Credits);
            Assert.Equal(3, P1(s).ShipDamage);
            var deck = Market.Deck(s, "ship").Cards;
            Assert.Contains("base-market-ship-04", deck);
            Assert.DoesNotContain("base-market-ship-06", deck); // the Lancer's own card left the game
        }
    }
}
