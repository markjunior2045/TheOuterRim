using System.Collections.Generic;
using System.Linq;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Persistence;
using OuterRim.Core.Rules;
using Xunit;

namespace OuterRim.Core.Tests
{
    public class AssetTests
    {
        private static readonly Content.GameContent C = TestContent.Base;
        private const string Hwk = "base-ship-hwk-290-freighter";        // cost 5,000: cargo, mod, crew x2
        private const string Lancer = "base-ship-lancer-class-pursuit-craft"; // cost 10,000

        private static GameState Game(int players = 2)
        {
            return GameEngine.NewGame(C, GameEngineTests.Setup(61, players), out _);
        }

        private static PlayerState P1(GameState s)
        {
            return s.FindPlayer("p1")!;
        }

        private static AssetState Asset(string id, string kind, int cost = 1000)
        {
            return new AssetState { CardId = id, Kind = kind, Cost = cost };
        }

        private static CommandResult Gain(GameState s, string player, string card, string kind, int cost = 1000, bool forced = false)
        {
            var f = new Frame(Assets.GainHandler);
            f.Set("player", player);
            f.Set("card", card);
            f.Set("kind", kind);
            f.Set("cost", cost);
            if (forced) f.Set("forced", "true");
            return GameEngine.StartResolution(C, s, f);
        }

        // ---- slots --------------------------------------------------------------------------

        [Fact]
        public void R14_2_ABoardAndAStarterShip_HaveTheirSlots()
        {
            var slots = Assets.SlotsOf(C, P1(Game()));
            Assert.Equal(7, slots.Sum(s => s.Count)); // gear x2, job or bounty x2, cargo, mod, crew
            Assert.Contains(slots, s => s.Types.SequenceEqual(new[] { "job", "bounty" }) && s.Count == 2);
        }

        [Fact]
        public void R14_13_R14_14_R14_16_SlotTypesAndCapacities()
        {
            var slots = new[] { new SlotSpec(new[] { "gear" }, 2), new SlotSpec(new[] { "cargo", "mod" }, 1) };
            Assert.True(Assets.Fits(slots, new[] { "gear", "gear", "mod" }));
            Assert.False(Assets.Fits(slots, new[] { "gear", "gear", "gear" }));      // x2 means two
            Assert.False(Assets.Fits(slots, new[] { "cargo", "mod" }));              // a dual slot holds one card
            Assert.False(Assets.Fits(slots, new[] { "crew" }));                      // type must match
        }

        [Fact]
        public void R14_17_PlacementIsFree_SoAnyValidArrangementCounts()
        {
            // A cargo could block the dual slot, but the cargo can use the cargo slot instead.
            var slots = new[] { new SlotSpec(new[] { "cargo", "mod" }, 1), new SlotSpec(new[] { "cargo" }, 1) };
            Assert.True(Assets.Fits(slots, new[] { "cargo", "mod" }));
        }

        // ---- gaining -------------------------------------------------------------------------

        [Fact]
        public void R14_2_AnAssetWithAFreeSlot_IsGainedAtOnce()
        {
            var s = Game();
            var r = Gain(s, "p1", "test-gear-a", "gear");
            Assert.Null(s.Pending);
            Assert.Single(P1(s).Assets);
            Assert.Contains(r.Events, e => e is AssetGainedEvent);
        }

        [Fact]
        public void R14_4_NoFreeSlot_DiscardOneOfYourCards_ToOpenOne()
        {
            var s = Game();
            P1(s).Assets.Add(Asset("old-gear-1", "gear"));
            P1(s).Assets.Add(Asset("old-gear-2", "gear"));
            P1(s).Assets.Add(Asset("old-cargo", "cargo"));
            Gain(s, "p1", "new-gear", "gear");
            var options = s.Pending!.Options.Select(o => o.Id).ToList();
            Assert.Equal(new[] { "old-gear-1", "old-gear-2", "decline" }, options); // the cargo would not open a gear slot
            GameEngine.Execute(C, s, new ChooseOptionCommand("p1", s.Pending.Id, "old-gear-2"));
            Assert.Equal(new[] { "old-gear-1", "old-cargo", "new-gear" }, P1(s).Assets.Select(a => a.CardId));
        }

        [Fact]
        public void R14_5_ThePlayerMayDecline_TheNewAsset()
        {
            var s = Game();
            P1(s).Assets.Add(Asset("old-crew", "crew"));
            Gain(s, "p1", "new-crew", "crew");
            var r = GameEngine.Execute(C, s, new ChooseOptionCommand("p1", s.Pending!.Id, "decline"));
            Assert.Equal(new[] { "old-crew" }, P1(s).Assets.Select(a => a.CardId));
            Assert.Contains(r.Events, e => e is AssetDeclinedEvent);
        }

        [Fact]
        public void R25_1_AForcedAsset_CannotBeDeclined()
        {
            var s = Game();
            P1(s).Assets.Add(Asset("old-crew", "crew"));
            Gain(s, "p1", "broken-arm", "crew", 0, forced: true);
            Assert.DoesNotContain(s.Pending!.Options, o => o.Id == "decline");
        }

        [Fact]
        public void R14_18_WithTooFewSlots_ThePlayerDiscards_UntilEverythingFits()
        {
            var s = Game();
            P1(s).Assets.Add(Asset("crew-a", "crew"));
            P1(s).Assets.Add(Asset("crew-b", "crew"));
            P1(s).Assets.Add(Asset("crew-c", "crew"));
            var f = new Frame(Assets.FitHandler);
            f.Set("player", "p1");
            GameEngine.StartResolution(C, s, f);
            GameEngine.Execute(C, s, new ChooseOptionCommand("p1", s.Pending!.Id, "crew-a"));
            Assert.NotNull(s.Pending); // still two crew for one crew slot
            var copy = SaveSerializer.Load(SaveSerializer.Save(s));
            GameEngine.Execute(C, s, new ChooseOptionCommand("p1", s.Pending!.Id, "crew-c"));
            GameEngine.Execute(C, copy, new ChooseOptionCommand("p1", copy.Pending!.Id, "crew-c"));
            Assert.Null(s.Pending);
            Assert.Equal(new[] { "crew-b" }, P1(s).Assets.Select(a => a.CardId));
            Assert.Equal(SaveSerializer.Save(s), SaveSerializer.Save(copy));
        }

        // ---- ships --------------------------------------------------------------------------------

        [Fact]
        public void R3_16d_TheSupply_HasEveryBuyableSheet_AndTheSpareStarters()
        {
            var s = Game(players: 2);
            Assert.Equal(2, s.ShipSupply.Count(id => id == "base-ship-g9-rigger"));
            Assert.Equal(8, s.ShipSupply.Count(id => id != "base-ship-g9-rigger"));
        }

        [Fact]
        public void R16_27_R16_28_R16_29_BuyingAShip_ReplacesTheSheet_ClearsShipDamage_ReturnsTheOld()
        {
            var s = Game();
            var p = P1(s);
            p.Credits = 10000;
            p.ShipDamage = 2;
            var r = GameEngine.BuyShip(C, s, "p1", Hwk, new string[0]);
            Assert.True(r.Accepted);
            Assert.Equal(5000, p.Credits);
            Assert.Equal(Hwk, p.ShipSheetId);
            Assert.Equal("ship_goal", C.ShipSide(p.ShipSideId).Role); // R-17.9: bought ships start on the ship goal side
            Assert.Equal(0, p.ShipDamage);
            Assert.DoesNotContain(Hwk, s.ShipSupply);
            Assert.Equal(3, s.ShipSupply.Count(id => id == "base-ship-g9-rigger"));
        }

        [Fact]
        public void R16_38_TheCurrentShip_IsAlwaysBartered()
        {
            var s = Game();
            var p = P1(s);
            p.Credits = 10000;
            GameEngine.BuyShip(C, s, "p1", Hwk, new string[0]);         // pays 5,000
            var r = GameEngine.BuyShip(C, s, "p1", Lancer, new string[0]); // 10,000 - 5,000 for the HWK
            Assert.Equal(5000, r.Events.OfType<ShipChangedEvent>().Single().PricePaid);
            Assert.Equal(0, p.Credits);
            Assert.Contains(Hwk, s.ShipSupply);
        }

        [Fact]
        public void R16_31_R16_36_BarteringAssets_ReducesThePrice_WithNoChange()
        {
            var s = Game();
            var p = P1(s);
            p.Credits = 0;
            p.Assets.Add(Asset("gear-8000", "gear", 8000));
            var r = GameEngine.BuyShip(C, s, "p1", Hwk, new[] { "gear-8000" });
            Assert.True(r.Accepted);
            Assert.Equal(0, r.Events.OfType<ShipChangedEvent>().Single().PricePaid);
            Assert.Equal(0, p.Credits);
            Assert.Empty(p.Assets);
        }

        [Fact]
        public void R16_32_R16_33_SomeAssets_CannotBeBartered()
        {
            var s = Game();
            var p = P1(s);
            p.Credits = 10000;
            p.Assets.Add(Asset("a-crew", "crew", 3000));
            p.Assets.Add(Asset("free-gear", "gear", 0));
            Assert.Contains("R-16.33", GameEngine.BuyShip(C, s, "p1", Hwk, new[] { "a-crew" }).Reason);
            Assert.Contains("R-16.32", GameEngine.BuyShip(C, s, "p1", Hwk, new[] { "free-gear" }).Reason);
            Assert.Equal("base-ship-g9-rigger", p.ShipSheetId);
        }

        [Fact]
        public void R9_8_AShipYouCannotAfford_IsRejected_AndNothingChanges()
        {
            var s = Game();
            P1(s).Credits = 4000;
            var before = SaveSerializer.Save(s);
            Assert.Contains("R-9.8", GameEngine.BuyShip(C, s, "p1", Hwk, new string[0]).Reason);
            Assert.Equal(before, SaveSerializer.Save(s));
        }

        [Fact]
        public void R17_8_OnlySheetsInTheSupply_CanBeBought()
        {
            var s = Game();
            var p = P1(s);
            p.Credits = 30000;
            GameEngine.BuyShip(C, s, "p1", Hwk, new string[0]);
            var other = s.FindPlayer("p2")!;
            other.Credits = 30000;
            Assert.Contains("R-17.8", GameEngine.BuyShip(C, s, "p2", Hwk, new string[0]).Reason);
        }

        [Fact]
        public void R16_30_ANewShipWithFewerSlots_ForcesDiscards()
        {
            var s = Game();
            var p = P1(s);
            p.Credits = 30000;
            GameEngine.BuyShip(C, s, "p1", Hwk, new string[0]); // two crew slots
            p.Assets.Add(Asset("crew-a", "crew"));
            p.Assets.Add(Asset("crew-b", "crew"));
            // Every buyable base sheet has at least 2 crew slots; only the starter has 1. Put the player back
            // on the starter (as Ryloth-9's ship replacement would) and run the same slot check a purchase runs.
            p.ShipSheetId = "base-ship-g9-rigger";
            p.ShipSideId = "base-ship-g9-rigger";
            var f = new Frame(Assets.FitHandler);
            f.Set("player", "p1");
            GameEngine.StartResolution(C, s, f);
            Assert.Equal(new[] { "crew-a", "crew-b" }, s.Pending!.Options.Select(o => o.Id));
        }
    }
}
