using System.Linq;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;
using Xunit;

namespace OuterRim.Core.Tests
{
    public class DeliverTests
    {
        private static readonly Content.GameContent C = TestContent.Base;
        private const string Salvage = "base-market-cargo-00";         // to Mon Calamari: 5,000 and 1 Rebel reputation
        private const string StolenSupplies = "base-market-cargo-01";  // ILLEGAL, to Kessel: 7,000 and 1 fame
        private const string Compartment = "base-market-cargo-08";     // Smuggling Compartment

        private static GameState Game(string space, ulong seed = 121, string character = "base-character-00")
        {
            var s = GameEngine.NewGame(C, new GameSetup(seed, new[]
            {
                new PlayerSetup("p1", "One", "green", space, TestContent.G9Rigger, character),
                new PlayerSetup("p2", "Two", "red", "tatooine", TestContent.G9Rigger, "base-character-03"),
            }, new[] { new PackRef("base", "0.1.0") }), out _);
            s.CurrentPlayerId = "p1";
            GameEngine.Execute(C, s, new MoveCommand("p1", new string[0]));
            return s;
        }

        private static PlayerState P1(GameState s) => s.FindPlayer("p1")!;

        private static void Hold(GameState s, string card, string kind = "cargo")
        {
            P1(s).Assets.Add(new AssetState { CardId = card, Kind = kind, Cost = 1000,
                Traits = C.Card(card)["traits"]!.ToObject<System.Collections.Generic.List<string>>()! });
            foreach (var d in s.MarketDecks) d.Cards.Remove(card);
        }

        private static CommandResult Deliver(GameState s, params string[] cargo)
        {
            return GameEngine.Execute(C, s, new DeliverCommand("p1", cargo));
        }

        /// <summary>Answers every pending decision with its first option, collecting events.</summary>
        private static System.Collections.Generic.List<GameEvent> Settle(GameState s, CommandResult r)
        {
            var events = r.Events.ToList();
            while (s.Pending != null)
            {
                events.AddRange(GameEngine.Execute(C, s, new ChooseOptionCommand(s.Pending.PlayerId, s.Pending.Id, s.Pending.Options[0].Id)).Events);
            }
            return events;
        }

        [Fact]
        public void R16_2_ACargoOnItsDestination_GivesItsReward_AndIsDiscarded()
        {
            var s = Game("mon-calamari");
            Hold(s, Salvage);
            int credits = P1(s).Credits;
            var r = Deliver(s, Salvage);
            Assert.True(r.Accepted);
            Assert.Equal(credits + 5000, P1(s).Credits);
            Assert.Equal(Reputation.Positive, P1(s).Reputation.Rebel);
            Assert.Empty(P1(s).Assets);
            Assert.Equal(Salvage, Market.Deck(s, "cargo").Cards.Last()); // R-22.1
            Assert.Equal(TurnStep.Action, s.Step);
        }

        [Fact]
        public void R16_1_DeliveringNeedsTheDestination_AndIsOneActionPerStep()
        {
            var s = Game("kessel");
            Hold(s, Salvage);
            Assert.Contains("R-16.2", Deliver(s, Salvage).Reason);
            var t = Game("mon-calamari");
            Hold(t, Salvage);
            Deliver(t, Salvage);
            Hold(t, "base-market-cargo-03");
            Assert.Contains("R-4.9", Deliver(t, "base-market-cargo-03").Reason);
        }

        [Fact]
        public void R16_7_CargoGainedInTheEncounterStep_WaitsForALaterTurn()
        {
            var s = Game("mon-calamari");
            Hold(s, Salvage);
            P1(s).Assets[0].GainedThisEncounter = true;
            Assert.Contains("R-16.7", Deliver(s, Salvage).Reason);
        }

        [Fact]
        public void R16_3_IllegalCargo_DeliversOnAHit_OtherwiseResolvesDatabankCard1()
        {
            int delivered = 0, checkedCards = 0;
            for (ulong seed = 0; seed < 80; seed++)
            {
                var s = Game("kessel", seed);
                Hold(s, StolenSupplies);
                var events = Settle(s, Deliver(s, StolenSupplies));
                var roll = events.OfType<DeliveryRolledEvent>().Single();
                if (roll.Face == DieFace.Hit)
                {
                    Assert.Contains(events, e => e is CargoDeliveredEvent);
                    Assert.DoesNotContain(events, e => e is DatabankResolvedEvent);
                    delivered++;
                }
                else
                {
                    var card = events.OfType<DatabankResolvedEvent>().Single().CardId;
                    Assert.StartsWith("base-databank-001", card);
                    Assert.Contains(events, e => e is TurnEndedEvent); // every section of card #1 ends the turn
                    checkedCards++;
                }
            }
            Assert.True(delivered > 0 && checkedCards > 0);
        }

        [Fact]
        public void CargoCheck_NegativeWithAPatrolHere_EndsTheActionStep_AndKeepsTheCargo()
        {
            for (ulong seed = 0; seed < 80; seed++)
            {
                var s = Game("kessel", seed);
                Hold(s, StolenSupplies);
                Patrols.Of(s, Faction.Imperial).SpaceId = "kessel";
                P1(s).Reputation.Imperial = Reputation.Negative;
                var events = Settle(s, Deliver(s, StolenSupplies));
                if (events.OfType<DeliveryRolledEvent>().Single().Face == DieFace.Hit) continue;
                Assert.Equal(TurnStep.Encounter, s.Step);
                Assert.Equal("p1", s.CurrentPlayerId);
                Assert.Contains(P1(s).Assets, a => a.CardId == StolenSupplies);
                return;
            }
            Assert.Fail("Every seed rolled a hit.");
        }

        [Fact]
        public void SmugglingCompartment_TurnsABlankIntoAHit_WhenDeliveringIllegalCargo()
        {
            for (ulong seed = 0; seed < 80; seed++)
            {
                var s = Game("kessel", seed);
                Hold(s, StolenSupplies);
                Hold(s, Compartment, "mod");
                Deliver(s, StolenSupplies);
                if (s.Pending?.Prompt != "rules.dice_window") { Settle(s, CommandResult.Ok(new GameEvent[0])); continue; }
                var r = GameEngine.Execute(C, s, new ChooseOptionCommand("p1", s.Pending.Id, Compartment + "#abilities[1]"));
                Assert.Equal(DieFace.Hit, r.Events.OfType<DeliveryRolledEvent>().Single().Face);
                Assert.Contains(r.Events, e => e is CargoDeliveredEvent);
                return;
            }
            Assert.Fail("No seed rolled a blank.");
        }

        [Fact]
        public void Lando_DeliveringIllegalCargo_CountsTowardHisGoal()
        {
            for (ulong seed = 0; seed < 80; seed++)
            {
                var s = Game("kessel", seed, "base-character-01");
                Hold(s, StolenSupplies);
                var events = Settle(s, Deliver(s, StolenSupplies));
                if (!events.OfType<CargoDeliveredEvent>().Any()) continue;
                Assert.Equal(1, Goals.Tokens(P1(s), "base-character-01#sides[0].abilities[1]"));
                return;
            }
            Assert.Fail("No seed delivered.");
        }
    }
}
