using System.Linq;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;
using Xunit;

namespace OuterRim.Core.Tests
{
    /// <summary>Trading (RULES §16.2, R-9.6, R-9.7; PLAN T3.2): the trade action and giving credits.</summary>
    public class TradeTests
    {
        private static readonly Content.GameContent C = TestContent.Base;

        private const string Gear = "base-market-gear-00", Cargo = "base-market-cargo-00", Bounty = "base-market-bounty-00";

        /// <summary>Three players; p1 is current and in the action step, p2 shares the space, p3 is elsewhere.</summary>
        private static GameState Game()
        {
            var s = GameEngine.NewGame(C, new GameSetup(91, new[]
            {
                new PlayerSetup("p1", "One", "green", "kessel", TestContent.G9Rigger, "base-character-00"),
                new PlayerSetup("p2", "Two", "red", "kessel", TestContent.G9Rigger, "base-character-03"),
                new PlayerSetup("p3", "Three", "yellow", "tatooine", TestContent.G9Rigger, "base-character-05"),
            }, new[] { new PackRef("base", "0.1.0") }), out _);
            s.CurrentPlayerId = "p1";
            foreach (var p in s.Players) p.Credits = 10000;
            foreach (var p in s.Patrols) p.SpaceId = "nav-tatooine-1";
            Assert.True(GameEngine.Execute(C, s, new MoveCommand("p1", new string[0])).Accepted);
            return s;
        }

        private static PlayerState P(GameState s, string id) => s.FindPlayer(id)!;

        private static void Give(GameState s, string player, string card, string kind)
        {
            P(s, player).Assets.Add(new AssetState { CardId = card, Kind = kind });
            foreach (var d in s.MarketDecks) d.Cards.Remove(card);
        }

        private static CommandResult Offer(GameState s, string partner, int giveCredits, string[] giveCards, int takeCredits, string[] takeCards)
        {
            return GameEngine.Execute(C, s, new OfferTradeCommand("p1", partner, giveCredits, giveCards, takeCredits, takeCards));
        }

        private static CommandResult Answer(GameState s, string option)
        {
            Assert.Equal("rules.trade_offer", s.Pending!.Prompt);
            return GameEngine.Execute(C, s, new ChooseOptionCommand(s.Pending.PlayerId, s.Pending.Id, option));
        }

        // ---- the trade action ----------------------------------------------------------------------

        [Fact]
        public void R16_8_R16_12_AnAcceptedTrade_MovesCreditsAndCardsBothWays()
        {
            var s = Game();
            Give(s, "p1", Gear, "gear");
            Give(s, "p2", Cargo, "cargo");
            Assert.True(Offer(s, "p2", 3000, new[] { Gear }, 1000, new[] { Cargo }).Accepted);
            Assert.Equal("p2", s.Pending!.PlayerId); // the partner answers
            var r = Answer(s, "accept");
            Assert.Equal(8000, P(s, "p1").Credits);
            Assert.Equal(12000, P(s, "p2").Credits);
            Assert.Contains(P(s, "p1").Assets, a => a.CardId == Cargo);
            Assert.Contains(P(s, "p2").Assets, a => a.CardId == Gear);
            Assert.Equal(2, r.Events.OfType<AssetTradedEvent>().Count());
        }

        [Fact]
        public void R16_10_ARefusedTrade_ChangesNothing_ButUsesTheAction()
        {
            var s = Game();
            Give(s, "p1", Gear, "gear");
            Offer(s, "p2", 0, new[] { Gear }, 0, new string[0]);
            Answer(s, "refuse");
            Assert.Contains(P(s, "p1").Assets, a => a.CardId == Gear);
            Assert.Contains("R-4.9", Offer(s, "p2", 1000, new string[0], 0, new string[0]).Reason);
        }

        [Fact]
        public void R16_10_TradesNeedNotBeEqual()
        {
            var s = Game();
            Assert.True(Offer(s, "p2", 0, new string[0], 4000, new string[0]).Accepted); // asking for credits, giving nothing
            Answer(s, "accept");
            Assert.Equal(14000, P(s, "p1").Credits);
        }

        [Fact]
        public void R16_8_OnlyWithAPlayerInYourSpace_DuringTheActionStep()
        {
            var s = Game();
            Assert.Contains("R-16.8", Offer(s, "p3", 1000, new string[0], 0, new string[0]).Reason);
            var planning = GameEngine.NewGame(C, new GameSetup(91, new[]
            {
                new PlayerSetup("p1", "One", "green", "kessel", TestContent.G9Rigger, "base-character-00"),
                new PlayerSetup("p2", "Two", "red", "kessel", TestContent.G9Rigger, "base-character-03"),
            }, new[] { new PackRef("base", "0.1.0") }), out _);
            var current = planning.CurrentPlayerId;
            var other = planning.Players.First(p => p.Id != current).Id;
            Assert.Contains("R-16.8", GameEngine.Execute(C, planning, new OfferTradeCommand(current, other, 1000, null, 0, null)).Reason);
        }

        [Fact]
        public void R16_9_R4_5_OnlyTheCurrentPlayerOffersATrade()
        {
            var s = Game();
            Assert.Contains("R-4.5", GameEngine.Execute(C, s, new OfferTradeCommand("p2", "p1", 1000, null, 0, null)).Reason);
        }

        [Fact]
        public void R16_14_R15_91_SecretsAndShipSheets_CannotBeTraded()
        {
            var s = Game();
            P(s, "p1").Secrets.Add("base-encounter-navpoint-04#sections[0].variants[0]");
            Assert.Contains("R-16.12", Offer(s, "p2", 0, new[] { "base-encounter-navpoint-04" }, 0, new string[0]).Reason);
            Assert.Contains("R-16.12", Offer(s, "p2", 0, new[] { P(s, "p1").ShipSheetId }, 0, new string[0]).Reason);
            Assert.Contains("R-16.12", Offer(s, "p2", 0, new string[0], 0, new[] { Gear }).Reason); // not held by p2
        }

        [Fact]
        public void R9_8_ATrade_IsPaidInFull()
        {
            var s = Game();
            Assert.Contains("R-9.8", Offer(s, "p2", 20000, new string[0], 0, new string[0]).Reason);
            Assert.Contains("R-9.8", Offer(s, "p2", 0, new string[0], 20000, new string[0]).Reason);
        }

        [Fact]
        public void R16_13_ABountyGoes_WithTheContactCapturedOnIt()
        {
            var s = Game();
            Give(s, "p1", Bounty, "bounty");
            var token = s.Contacts.First(t => t.Id == (string)C.Card(Bounty)["target"]!);
            token.Bounty = Bounty;
            Offer(s, "p2", 0, new[] { Bounty }, 0, new string[0]);
            Answer(s, "accept");
            Assert.Contains(P(s, "p2").Assets, a => a.CardId == Bounty);
            Assert.Same(token, Contacts.CapturedOn(s, Bounty));
        }

        [Fact]
        public void R14_11_ARotatedAsset_KeepsItsOrientation()
        {
            var s = Game();
            Give(s, "p1", Gear, "gear");
            Assets.Find(P(s, "p1"), Gear)!.Rotated = true;
            Offer(s, "p2", 0, new[] { Gear }, 0, new string[0]);
            Answer(s, "accept");
            Assert.True(Assets.Find(P(s, "p2"), Gear)!.Rotated);
        }

        [Fact]
        public void R14_18_AReceiverWithoutRoom_DiscardsUntilEverythingFits()
        {
            var s = Game();
            var p2 = P(s, "p2");
            for (int i = 0; Assets.Valid(C, p2, Assets.With(p2.Assets, new AssetState { CardId = "x", Kind = "gear" })); i++)
            {
                p2.Assets.Add(new AssetState { CardId = "test-gear-" + i, Kind = "gear" });
            }
            Give(s, "p1", Gear, "gear");
            Offer(s, "p2", 0, new[] { Gear }, 0, new string[0]);
            Answer(s, "accept");
            Assert.Equal("rules.too_many_assets", s.Pending!.Prompt);
            Assert.Equal("p2", s.Pending.PlayerId);
        }

        // ---- giving credits ------------------------------------------------------------------------

        [Fact]
        public void R16_15_CreditsGoToOrFromTheCurrentPlayer_AnywhereOnTheMap()
        {
            var s = Game();
            Assert.True(GameEngine.Execute(C, s, new GiveCreditsCommand("p3", "p1", 2000)).Accepted); // p3 is elsewhere
            Assert.True(GameEngine.Execute(C, s, new GiveCreditsCommand("p1", "p3", 500)).Accepted);
            Assert.Equal(11500, P(s, "p1").Credits);
            Assert.Equal(8500, P(s, "p3").Credits);
            Assert.Contains("R-16.15", GameEngine.Execute(C, s, new GiveCreditsCommand("p2", "p3", 1000)).Reason);
        }

        [Fact]
        public void R9_8_YouGiveOnlyCreditsYouHave()
        {
            var s = Game();
            Assert.Contains("R-9.8", GameEngine.Execute(C, s, new GiveCreditsCommand("p1", "p2", 10001)).Reason);
            Assert.False(GameEngine.Execute(C, s, new GiveCreditsCommand("p1", "p2", 0)).Accepted);
        }

        [Fact]
        public void R16_16_GivingCredits_MayInterruptADecision_AndAnUnpayableTradeFallsThrough()
        {
            var s = Game();
            Offer(s, "p2", 0, new string[0], 8000, new string[0]);
            Assert.NotNull(s.Pending);
            // While p2 decides, p2 gives credits away (R-16.16): the trade can no longer be paid in full.
            Assert.True(GameEngine.Execute(C, s, new GiveCreditsCommand("p2", "p1", 5000)).Accepted);
            Assert.NotNull(s.Pending);
            var r = Answer(s, "accept");
            Assert.Contains(r.Events, e => e is TradeFailedEvent);
            Assert.Equal(15000, P(s, "p1").Credits);
            Assert.Equal(5000, P(s, "p2").Credits);
        }
    }
}
