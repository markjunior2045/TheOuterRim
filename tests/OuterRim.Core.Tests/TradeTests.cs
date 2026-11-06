using System.Linq;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;
using Xunit;

namespace OuterRim.Core.Tests
{
    /// <summary>Trading (RULES §16.2, R-9.6, R-9.7; PLAN T3.2, T3.2b): the trade action and credit requests.</summary>
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
        public void R16_8_R4_9_ARefusedOffer_ChangesNothing_AndAnotherOfferMayFollow_ToTheSameOrAnotherPlayer()
        {
            var s = Game();
            P(s, "p3").SpaceId = P(s, "p1").SpaceId;
            Give(s, "p1", Gear, "gear");
            Offer(s, "p2", 0, new[] { Gear }, 0, new string[0]);
            Answer(s, "refuse");
            Assert.Contains(P(s, "p1").Assets, a => a.CardId == Gear);
            Assert.True(Offer(s, "p2", 1000, new[] { Gear }, 0, new string[0]).Accepted); // the same player again
            Answer(s, "refuse");
            Assert.True(Offer(s, "p3", 0, new[] { Gear }, 0, new string[0]).Accepted); // or another one in the space
            Answer(s, "accept");
            Assert.Contains(P(s, "p3").Assets, a => a.CardId == Gear);
        }

        [Fact]
        public void R4_9_ATradeCarriedOut_UsesTheAction_NoOtherTradeInThatStep()
        {
            var s = Game();
            P(s, "p3").SpaceId = P(s, "p1").SpaceId;
            Offer(s, "p2", 1000, new string[0], 0, new string[0]);
            Answer(s, "accept");
            Assert.Contains("R-4.9", Offer(s, "p2", 1000, new string[0], 0, new string[0]).Reason);
            Assert.Contains("R-4.9", Offer(s, "p3", 1000, new string[0], 0, new string[0]).Reason);
        }

        [Fact]
        public void R9_7_R4_9_AnAcceptedTradeThatFallsThrough_LeavesTheActionUnused()
        {
            var s = Game();
            Offer(s, "p2", 0, new string[0], 8000, new string[0]);
            P(s, "p2").Credits = 1000; // spent meanwhile
            Assert.Contains(Answer(s, "accept").Events, e => e is TradeFailedEvent);
            Assert.True(Offer(s, "p2", 0, new string[0], 1000, new string[0]).Accepted);
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
        public void R14_2_CardsReceivedInATrade_AreGained_WhenYouGainGoalsSeeThem()
        {
            // GX1 Short Hauler (R-25.16): "gain a crew while you have 2 crew". The current player receives the third.
            var s = Game();
            var p1 = P(s, "p1");
            p1.ShipSheetId = "base-ship-gx1-short-hauler";
            p1.ShipSideId = "base-ship-gx1-short-hauler";
            p1.Assets.Add(new AssetState { CardId = "crew-a", Kind = "crew" });
            p1.Assets.Add(new AssetState { CardId = "crew-b", Kind = "crew" });
            P(s, "p2").Assets.Add(new AssetState { CardId = "crew-c", Kind = "crew" });
            Offer(s, "p2", 1000, new string[0], 0, new[] { "crew-c" });
            var r = Answer(s, "accept");
            Assert.Contains(r.Events, e => e is AssetTradedEvent t && t.ToId == "p1" && t.CardId == "crew-c");
            Assert.Null(s.Pending); // no discard: the goal flipped the ship to three crew slots
            Assert.Equal("base-ship-gx1-short-hauler-back", p1.ShipSideId);
            Assert.Equal(1, p1.Fame);
        }

        [Fact]
        public void R14_18_TheCurrentPlayer_AlsoSettlesTheSlots_AfterATrade()
        {
            var s = Game();
            var p1 = P(s, "p1");
            for (int i = 0; Assets.Valid(C, p1, Assets.With(p1.Assets, new AssetState { CardId = "x", Kind = "gear" })); i++)
            {
                p1.Assets.Add(new AssetState { CardId = "test-gear-" + i, Kind = "gear" });
            }
            Give(s, "p2", Gear, "gear");
            Offer(s, "p2", 0, new string[0], 0, new[] { Gear });
            Answer(s, "accept");
            Assert.Equal("rules.too_many_assets", s.Pending!.Prompt);
            Assert.Equal("p1", s.Pending.PlayerId);
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

        // ---- credit requests -----------------------------------------------------------------------

        private static CommandResult Request(GameState s, string from, string to, CreditRequestKind kind, int amount)
        {
            return GameEngine.Execute(C, s, new RequestCreditsCommand(from, to, kind, amount));
        }

        private static CommandResult Reply(GameState s, string player, bool accept)
        {
            return GameEngine.Execute(C, s, new AnswerCreditRequestCommand(player, s.CreditRequests.Single().Id, accept));
        }

        [Fact]
        public void R9_6_AskCredits_MovesThemOnlyWhenAccepted()
        {
            var s = Game();
            Assert.True(Request(s, "p1", "p2", CreditRequestKind.Ask, 3000).Accepted);
            Assert.Equal(10000, P(s, "p1").Credits); // nothing moves before the answer
            var r = Reply(s, "p2", true);
            Assert.Equal(13000, P(s, "p1").Credits);
            Assert.Equal(7000, P(s, "p2").Credits);
            Assert.Contains(r.Events, e => e is CreditsGivenEvent g && g.FromId == "p2" && g.ToId == "p1" && g.Amount == 3000);
            Assert.Empty(s.CreditRequests);
        }

        [Fact]
        public void R9_6_SendCredits_MovesThemOnlyWhenAccepted_ARefusalChangesNothing()
        {
            var s = Game();
            Request(s, "p2", "p1", CreditRequestKind.Send, 2000);
            var r = Reply(s, "p1", false);
            Assert.Contains(r.Events, e => e is CreditRequestClosedEvent c && c.Outcome == CreditRequestOutcome.Refused);
            Assert.Equal(10000, P(s, "p2").Credits);
            Request(s, "p2", "p1", CreditRequestKind.Send, 2000);
            Reply(s, "p1", true);
            Assert.Equal(12000, P(s, "p1").Credits);
            Assert.Equal(8000, P(s, "p2").Credits);
        }

        [Fact]
        public void R16_15_ARequestIsWithTheCurrentPlayer_AnywhereOnTheMap()
        {
            var s = Game();
            Assert.True(Request(s, "p3", "p1", CreditRequestKind.Send, 1000).Accepted); // p3 is elsewhere
            Assert.Contains("R-16.15", Request(s, "p2", "p3", CreditRequestKind.Send, 1000).Reason);
            Assert.False(Request(s, "p1", "p1", CreditRequestKind.Ask, 1000).Accepted);
        }

        [Fact]
        public void R16_15_ARequestIsNotAnAction_AndHoldsNothingUp()
        {
            var s = Game();
            Request(s, "p1", "p2", CreditRequestKind.Ask, 1000);
            Assert.True(GameEngine.Execute(C, s, new EndActionStepCommand("p1")).Accepted); // the turn goes on
            Assert.Single(s.CreditRequests);
            Assert.True(Reply(s, "p2", true).Accepted);
        }

        [Fact]
        public void R16_16_ARequest_MayBeMadeAndAnswered_WhileADecisionIsPending()
        {
            var s = Game();
            Offer(s, "p2", 0, new string[0], 1000, new string[0]);
            var pending = s.Pending!.Id;
            Assert.True(Request(s, "p1", "p3", CreditRequestKind.Send, 500).Accepted);
            Assert.True(Reply(s, "p3", true).Accepted);
            Assert.Equal(pending, s.Pending!.Id); // the decision still waits
            Assert.Equal(10500, P(s, "p3").Credits);
        }

        [Fact]
        public void R9_8_ThePayer_MustHaveTheAmount_WhenTheRequestIsMade()
        {
            var s = Game();
            Assert.Contains("R-9.8", Request(s, "p1", "p2", CreditRequestKind.Send, 10001).Reason);
            Assert.Contains("R-9.8", Request(s, "p1", "p2", CreditRequestKind.Ask, 10001).Reason);
            Assert.False(Request(s, "p1", "p2", CreditRequestKind.Ask, 0).Accepted);
        }

        [Fact]
        public void R9_7_AnAcceptedRequest_ThePayerCannotCover_MovesNothing()
        {
            var s = Game();
            Request(s, "p1", "p2", CreditRequestKind.Ask, 8000);
            P(s, "p2").Credits = 5000; // spent meanwhile
            var r = Reply(s, "p2", true);
            Assert.Contains(r.Events, e => e is CreditRequestClosedEvent c && c.Outcome == CreditRequestOutcome.Failed);
            Assert.DoesNotContain(r.Events, e => e is CreditsGivenEvent);
            Assert.Equal(10000, P(s, "p1").Credits);
            Assert.Equal(5000, P(s, "p2").Credits);
            Assert.Empty(s.CreditRequests);
        }

        [Fact]
        public void R9_6_OnlyTheOtherPlayerAnswers_OnlyTheRequesterCancels()
        {
            var s = Game();
            Request(s, "p1", "p2", CreditRequestKind.Ask, 1000);
            int id = s.CreditRequests.Single().Id;
            Assert.False(GameEngine.Execute(C, s, new AnswerCreditRequestCommand("p1", id, true)).Accepted);
            Assert.False(GameEngine.Execute(C, s, new CancelCreditRequestCommand("p2", id)).Accepted);
            var r = GameEngine.Execute(C, s, new CancelCreditRequestCommand("p1", id));
            Assert.Contains(r.Events, e => e is CreditRequestClosedEvent c && c.Outcome == CreditRequestOutcome.Cancelled);
            Assert.Empty(s.CreditRequests);
            Assert.False(GameEngine.Execute(C, s, new AnswerCreditRequestCommand("p2", id, true)).Accepted);
        }

        [Fact]
        public void R9_6_OneOpenRequest_BetweenTwoPlayers_ButSeveralAtOnce()
        {
            var s = Game();
            Assert.True(Request(s, "p1", "p2", CreditRequestKind.Ask, 1000).Accepted);
            Assert.False(Request(s, "p2", "p1", CreditRequestKind.Send, 1000).Accepted); // same pair, either way
            Assert.True(Request(s, "p3", "p1", CreditRequestKind.Send, 1000).Accepted);
            Assert.Equal(2, s.CreditRequests.Count);
        }

        [Fact]
        public void R16_15_OpenRequests_ExpireWhenTheTurnEnds()
        {
            var s = Game();
            Request(s, "p1", "p2", CreditRequestKind.Ask, 1000);
            s.TurnEndsAfterStep = true;
            var r = GameEngine.Execute(C, s, new EndActionStepCommand("p1"));
            Assert.Contains(r.Events, e => e is TurnEndedEvent);
            Assert.Contains(r.Events, e => e is CreditRequestClosedEvent c && c.Outcome == CreditRequestOutcome.Expired);
            Assert.Empty(s.CreditRequests);
            Assert.Equal(10000, P(s, "p2").Credits);
        }

        [Fact]
        public void R16_15_AnOpenRequest_IsSaved()
        {
            var s = Game();
            Request(s, "p1", "p2", CreditRequestKind.Send, 1000);
            var copy = Persistence.SaveSerializer.Load(Persistence.SaveSerializer.Save(s));
            Assert.Equal(CreditRequestKind.Send, copy.CreditRequests.Single().Kind);
            Assert.True(GameEngine.Execute(C, copy, new AnswerCreditRequestCommand("p2", copy.CreditRequests[0].Id, true)).Accepted);
            Assert.Equal(11000, copy.FindPlayer("p2")!.Credits);
        }

        [Fact]
        public void R16_16_ACreditRequest_MayEmptyAPurseMidTrade_AndTheTradeFallsThrough()
        {
            var s = Game();
            Offer(s, "p2", 0, new string[0], 8000, new string[0]);
            Assert.NotNull(s.Pending);
            // While p2 decides, p2 sends credits to the current player (R-16.16): the trade can no longer be paid in full.
            Request(s, "p2", "p1", CreditRequestKind.Send, 5000);
            Reply(s, "p1", true);
            Assert.NotNull(s.Pending);
            var r = Answer(s, "accept");
            Assert.Contains(r.Events, e => e is TradeFailedEvent);
            Assert.Equal(15000, P(s, "p1").Credits);
            Assert.Equal(5000, P(s, "p2").Credits);
        }
    }
}
