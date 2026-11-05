using System.Collections.Generic;
using System.Linq;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Persistence;
using OuterRim.Core.Rules;
using Xunit;

namespace OuterRim.Core.Tests
{
    public class GoalTests
    {
        private static readonly Content.GameContent C = TestContent.Base;
        private const string Ketsu = "base-character-00", Lando = "base-character-01", Han = "base-character-04";

        private static GameState Game(string character, ulong seed = 101)
        {
            var s = GameEngine.NewGame(C, new GameSetup(seed, new[] { new PlayerSetup("p1", "One", "green", "kessel", TestContent.G9Rigger, character) },
                new[] { new PackRef("base", "0.1.0") }), out _);
            s.CurrentPlayerId = "p1";
            return s;
        }

        private static PlayerState P1(GameState s) => s.FindPlayer("p1")!;

        private static void Ship(GameState s, string sheet, string side)
        {
            P1(s).ShipSheetId = sheet;
            P1(s).ShipSideId = side;
        }

        private static CommandResult Fire(GameState s, string evt, Dictionary<string, string>? context = null)
        {
            return GameEngine.StartResolution(C, s, TriggersHandler.For(evt, "p1", context));
        }

        private static Dictionary<string, string> Win(string opponent) => new Dictionary<string, string>
        {
            ["combat_type"] = "ship", ["combat_result"] = "won", ["opponent"] = opponent,
        };

        [Fact]
        public void R18_5_R18_6_Ketsu_ThreeCombatWins_GainFame_AndFlip()
        {
            var s = Game(Ketsu);
            Fire(s, "combat_won", Win("npc"));
            var r = Fire(s, "combat_won", Win("npc"));
            Assert.Equal(2, r.Events.OfType<GoalProgressEvent>().Single().Count);
            var copy = SaveSerializer.Load(SaveSerializer.Save(s)); // goal tokens are saved
            Fire(s, "combat_won", Win("npc"));
            Assert.Equal(1, P1(s).Fame);
            Assert.Equal(1, P1(s).CharacterSide);
            Assert.Empty(P1(s).GoalTokens); // R-18.5: tokens discarded on flipping
            Assert.Equal(2, Goals.Tokens(copy.FindPlayer("p1")!, "base-character-00#sides[0].abilities[1]"));
        }

        [Fact]
        public void Aggressor_CountsOnlyWinsAgainstPatrols()
        {
            var s = Game(Lando); // nothing of Lando's reacts to a win
            Ship(s, "base-ship-aggressor-class-assault-fighter", "base-ship-aggressor-class-assault-fighter-back");
            Fire(s, "combat_won", Win("npc"));
            Assert.Empty(P1(s).GoalTokens);
            Fire(s, "combat_won", Win("patrol"));
            Fire(s, "combat_won", Win("patrol"));
            Assert.Equal("base-ship-aggressor-class-assault-fighter", P1(s).ShipSideId); // IG-2000
            Assert.Equal(1, P1(s).Fame);
        }

        [Fact]
        public void HanSolo_HavingAShipCostingAtLeast15000_CompletesHisGoal()
        {
            var s = Game(Han);
            P1(s).Credits = 25000;
            var r = GameEngine.BuyShip(C, s, "p1", "base-ship-firespray-31-patrol-craft", new string[0]);
            Assert.True(r.Accepted);
            Assert.Equal(1, P1(s).Fame);
            Assert.Equal(1, P1(s).CharacterSide);
            Assert.Contains(r.Events, e => e is SheetFlippedEvent f && f.Sheet == "character");
        }

        [Fact]
        public void HWK290_ShipGoalAction_NeedsAModAnd7000()
        {
            var s = Game(Ketsu);
            Ship(s, "base-ship-hwk-290-freighter", "base-ship-hwk-290-freighter");
            P1(s).Credits = 10000;
            GameEngine.Execute(C, s, new MoveCommand("p1", new string[0]));
            Assert.Contains("condition", GameEngine.Execute(C, s, new UseAbilityCommand("p1", "base-ship-hwk-290-freighter", "sides[0].abilities[0]")).Reason);
            P1(s).Assets.Add(new AssetState { CardId = "base-market-gear-08", Kind = "mod", Cost = 2000 });
            Assert.True(GameEngine.Execute(C, s, new UseAbilityCommand("p1", "base-ship-hwk-290-freighter", "sides[0].abilities[0]")).Accepted);
            Assert.Equal(3000, P1(s).Credits);
            Assert.Equal(1, P1(s).Fame);
            Assert.Equal("base-ship-hwk-290-freighter-back", P1(s).ShipSideId); // Moldy Crow
        }

        [Fact]
        public void R25_17_YV666_RecoveringShipDamage_MayPay7000ToComplete()
        {
            var s = Game(Ketsu);
            Ship(s, "base-ship-yv-666-light-freighter", "base-ship-yv-666-light-freighter");
            P1(s).Credits = 9000;
            P1(s).ShipDamage = 2;
            GameEngine.Execute(C, s, new RecoverDamageCommand("p1"));
            Assert.Equal("rules.use_ability", s.Pending!.Prompt);
            var copy = SaveSerializer.Load(SaveSerializer.Save(s));
            GameEngine.Execute(C, s, new ChooseOptionCommand("p1", s.Pending.Id, "yes"));
            Assert.Equal(2000, P1(s).Credits);
            Assert.Equal(1, P1(s).Fame);
            Assert.Equal("base-ship-yv-666-light-freighter-back", P1(s).ShipSideId);

            GameEngine.Execute(C, copy, new ChooseOptionCommand("p1", copy.Pending!.Id, "no"));
            Assert.Equal(0, copy.FindPlayer("p1")!.Fame); // optional: declining is fine
        }

        [Fact]
        public void YV666_IsNotAsked_WhenOnlyTheCharacterRecovers()
        {
            var s = Game(Ketsu);
            Ship(s, "base-ship-yv-666-light-freighter", "base-ship-yv-666-light-freighter");
            P1(s).Credits = 9000;
            P1(s).CharacterDamage = 2;
            GameEngine.Execute(C, s, new RecoverDamageCommand("p1"));
            Assert.Null(s.Pending);
        }

        [Fact]
        public void R25_16_GX1_GainingAThirdCrew_CompletesTheGoal_AndKeepsAllThree()
        {
            var s = Game(Ketsu);
            Ship(s, "base-ship-gx1-short-hauler", "base-ship-gx1-short-hauler"); // two crew slots
            P1(s).Assets.Add(new AssetState { CardId = "crew-a", Kind = "crew" });
            P1(s).Assets.Add(new AssetState { CardId = "crew-b", Kind = "crew" });
            var gain = new Frame(Assets.GainHandler);
            gain.Set("player", "p1");
            gain.Set("card", "crew-c");
            gain.Set("kind", "crew");
            GameEngine.StartResolution(C, s, gain);
            Assert.Null(s.Pending); // no discard asked
            Assert.Equal(3, P1(s).Assets.Count(a => a.Kind == "crew"));
            Assert.Equal("base-ship-gx1-short-hauler-back", P1(s).ShipSideId); // Valorous, three crew slots
            Assert.Equal(1, P1(s).Fame);
        }

        [Fact]
        public void R18_2_ProgressOnAShip_DoesNotCarryToTheNextShip()
        {
            var s = Game(Lando);
            Ship(s, "base-ship-aggressor-class-assault-fighter", "base-ship-aggressor-class-assault-fighter-back");
            Fire(s, "combat_won", Win("patrol"));
            Assert.Single(P1(s).GoalTokens);
            P1(s).Credits = 30000;
            GameEngine.BuyShip(C, s, "p1", "base-ship-firespray-31-patrol-craft", new string[0]);
            Assert.Empty(P1(s).GoalTokens);
        }
    }
}
